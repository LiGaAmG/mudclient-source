namespace Adan.Client.Common.Conveyor
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Лёгкий лог производительности — пишет медленные вызовы конвейера в файл.
    /// Используется только в DEBUG-сборке.
    /// Файл: %TEMP%\adan_perf.log
    /// </summary>
    public static class PerfLog
    {
        private static readonly string _logPath = Path.Combine(Path.GetTempPath(), "adan_perf.log");
        private static readonly object _lock = new object();

        static PerfLog()
        {
            // Очищаем лог при старте приложения
            try { File.WriteAllText(_logPath, $"=== Adan PerfLog started {DateTime.Now:HH:mm:ss} ===\r\n"); }
            catch { }
        }

        /// <summary>
        /// Записывает медленный вызов юнита в лог.
        /// </summary>
        public static void Write(string unitName, string messageName, long elapsedMs)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} | {elapsedMs,4}ms | {unitName} | {messageName}\r\n";
            lock (_lock)
            {
                try { File.AppendAllText(_logPath, line, Encoding.UTF8); }
                catch { }
            }
        }

        /// <summary>
        /// Записывает суммарное время обработки одного сообщения всеми юнитами.
        /// </summary>
        public static void WriteTotal(string messageName, long totalMs, string slowUnits)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} | {totalMs,4}ms | *** TOTAL *** | {messageName} | [{slowUnits}]\r\n";
            lock (_lock)
            {
                try { File.AppendAllText(_logPath, line, Encoding.UTF8); }
                catch { }
            }
        }

        /// <summary>
        /// Записывает событие прихода данных из сети.
        /// </summary>
        public static void WriteNet(int bytes, long parseMs)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} |      | >>> NET {bytes}b parse={parseMs}ms\r\n";
            lock (_lock)
            {
                try { File.AppendAllText(_logPath, line, Encoding.UTF8); }
                catch { }
            }
        }

        /// <summary>
        /// Записывает событие рендера батча сообщений на UI-потоке.
        /// </summary>
        public static void WriteRender(int msgCount, long waitMs, long renderMs)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} |      | >>> RENDER batch={msgCount} waited={waitMs}ms render={renderMs}ms\r\n";
            lock (_lock)
            {
                try { File.AppendAllText(_logPath, line, Encoding.UTF8); }
                catch { }
            }
        }
    }
}
