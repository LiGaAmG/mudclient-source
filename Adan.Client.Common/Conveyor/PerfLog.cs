namespace Adan.Client.Common.Conveyor
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Лёгкий лог производительности — пишет медленные вызовы конвейера в файл.
    /// Файл: %TEMP%\adan_perf.log
    /// </summary>
    public static class PerfLog
    {
        private static readonly string _logPath = Path.Combine(Path.GetTempPath(), "adan_perf.log");

        // Асинхронная запись: вызовы лишь кладут строку в очередь (без I/O и без ожидания),
        // фоновый поток пишет в постоянно открытый файл. Раньше каждая запись делала
        // синхронный append под общим lock — при заминке диска/антивируса на этом lock
        // вставали ВСЕ потоки, включая UI (фризы до 5 секунд).
        private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _queue =
            new System.Collections.Concurrent.ConcurrentQueue<string>();
        private const int MaxQueuedLines = 10000;

        static PerfLog()
        {
            var writerThread = new System.Threading.Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "PerfLogWriter",
                Priority = System.Threading.ThreadPriority.BelowNormal
            };
            writerThread.Start();
        }

        private static void Enqueue(string line)
        {
            if (_queue.Count < MaxQueuedLines)
                _queue.Enqueue(line);
        }

        private static void WriterLoop()
        {
            StreamWriter writer = null;
            try
            {
                writer = new StreamWriter(_logPath, false, Encoding.UTF8) { AutoFlush = false };
                writer.WriteLine($"=== Adan PerfLog started {DateTime.Now:HH:mm:ss} ===");
                writer.Flush();
            }
            catch
            {
                // Лог недоступен — молча работаем без него, очередь просто дренируется
            }

            while (true)
            {
                try
                {
                    bool wroteAny = false;
                    string line;
                    while (_queue.TryDequeue(out line))
                    {
                        if (writer != null) writer.Write(line);
                        wroteAny = true;
                    }

                    if (wroteAny && writer != null) writer.Flush();
                }
                catch
                {
                }

                System.Threading.Thread.Sleep(200);
            }
        }

        /// <summary>
        /// Записывает медленный вызов юнита в лог.
        /// </summary>
        public static void Write(string unitName, string messageName, long elapsedMs)
        {
            Enqueue($"{DateTime.Now:HH:mm:ss.fff} | {elapsedMs,4}ms | {unitName} | {messageName}\r\n");
        }

        /// <summary>
        /// Записывает суммарное время обработки одного сообщения всеми юнитами.
        /// </summary>
        public static void WriteTotal(string messageName, long totalMs, string slowUnits, string innerText = "")
        {
            var line = string.IsNullOrEmpty(innerText)
                ? $"{DateTime.Now:HH:mm:ss.fff} | {totalMs,4}ms | *** TOTAL *** | {messageName} | [{slowUnits}]\r\n"
                : $"{DateTime.Now:HH:mm:ss.fff} | {totalMs,4}ms | *** TOTAL *** | [{slowUnits}] | \"{innerText}\"\r\n";
            Enqueue(line);
        }

        /// <summary>
        /// Записывает событие прихода данных из сети.
        /// </summary>
        public static void WriteNet(int bytes, long parseMs, string uid = "")
        {
            Enqueue($"{DateTime.Now:HH:mm:ss.fff} |      | >>> NET [{Short(uid)}] {bytes}b parse={parseMs}ms\r\n");
        }

        private static string Short(string uid)
        {
            return string.IsNullOrEmpty(uid) ? "?" : (uid.Length > 8 ? uid.Substring(0, 8) : uid);
        }

        /// <summary>
        /// Записывает отправку данных на сервер — по паре SEND/NET виден round-trip команды.
        /// </summary>
        public static void WriteSend(int bytes, string uid = "", string text = "")
        {
            Enqueue($"{DateTime.Now:HH:mm:ss.fff} |      | >>> SEND [{Short(uid)}] {bytes}b \"{text}\"\r\n");
        }

        /// <summary>
        /// Записывает задержку обновления виджета: от постановки в очередь до выполнения на UI-потоке.
        /// </summary>
        public static void WriteWidget(string widgetName, long queuedToExecuteMs, long executeMs)
        {
            Enqueue($"{DateTime.Now:HH:mm:ss.fff} |      | >>> WIDGET {widgetName} waited={queuedToExecuteMs}ms update={executeMs}ms\r\n");
        }

        /// <summary>
        /// Записывает событие рендера батча сообщений на UI-потоке.
        /// </summary>
        public static void WriteRender(int msgCount, long waitMs, long renderMs)
        {
            Enqueue($"{DateTime.Now:HH:mm:ss.fff} |      | >>> RENDER batch={msgCount} waited={waitMs}ms render={renderMs}ms\r\n");
        }

        /// <summary>
        /// Записывает время OnRender в ScrollableFlowTextControl (>= 10ms).
        /// </summary>
        public static void WriteOnRender(int lines, long elapsedMs)
        {
            Enqueue($"{DateTime.Now:HH:mm:ss.fff} |      | >>> ONRENDER lines={lines} elapsed={elapsedMs}ms\r\n");
        }

        /// <summary>
        /// Записывает событие переключения таба.
        /// </summary>
        public static void WriteTabSwitch(string uid)
        {
            Enqueue($"{DateTime.Now:HH:mm:ss.fff} |      | >>> TAB_SWITCH uid={uid}\r\n");
        }

        /// <summary>
        /// Трассировка виджета монстров — помогает диагностировать показ монстра из чужой комнаты.
        /// </summary>
        public static void WriteMonsters(string phase, string forUid, string viewModelUid, string pendingForUid, int count, bool applied, string names = "")
        {
            var suffix = applied && names.Length > 0 ? $" [{names}]" : "";
            Enqueue($"{DateTime.Now:HH:mm:ss.fff} |      | >>> MONSTERS {phase,-8} forUid={forUid} vmUid={viewModelUid} pendingUid={pendingForUid} n={count} {(applied ? "APPLY" : "DISCARD")}{suffix}\r\n");
        }

        /// <summary>
        /// Переключение таба в менеджере монстров.
        /// </summary>
        public static void WriteMonstersSwitch(string uid, int charCount, string names = "")
        {
            Enqueue($"{DateTime.Now:HH:mm:ss.fff} |      | >>> MONSTERS SWITCH uid={uid} chars={charCount} [{names}]\r\n");
        }

        /// <summary>
        /// Точечная трассировка статистики.
        /// </summary>
        public static void WriteStatistics(string phase, int count, long elapsedMs, string details = "")
        {
            var suffix = string.IsNullOrEmpty(details) ? string.Empty : $" {details}";
            Enqueue($"{DateTime.Now:HH:mm:ss.fff} |      | >>> STATS {phase,-10} n={count} elapsed={elapsedMs}ms{suffix}\r\n");
        }
    }
}
