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
        /// Записывает медленный вызов в лог.
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
    }
}
