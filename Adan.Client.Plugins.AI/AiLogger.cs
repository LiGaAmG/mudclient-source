using System;
using System.IO;

namespace Adan.Client.Plugins.AI
{
    // Пишет лог в файл рядом с базой данных — читается через rtk read без скринов
    internal static class AiLogger
    {
        private static string _logPath;
        private static readonly object _lock = new object();

        public static void Init(string dbDir)
        {
            _logPath = Path.Combine(dbDir, "ai-debug.log");
            // Ротация: если > 500KB — обрезаем
            try
            {
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 500_000)
                    File.WriteAllText(_logPath, $"[{Now()}] --- лог обрезан (ротация) ---\n");
            }
            catch { }
        }

        public static void Log(string category, string msg)
        {
            if (_logPath == null) return;
            var line = $"[{Now()}] [{category}] {msg}";
            Console.WriteLine(line);
            try { lock (_lock) File.AppendAllText(_logPath, line + "\n"); } catch { }
        }

        public static void Prompt(string prompt)
        {
            if (_logPath == null) return;
            var sep = new string('=', 60);
            try
            {
                lock (_lock)
                    File.AppendAllText(_logPath,
                        $"\n[{Now()}] [PROMPT]\n{sep}\n{prompt}\n{sep}\n");
            }
            catch { }
        }

        public static void Answer(string raw, string trimmed)
        {
            if (_logPath == null) return;
            try
            {
                lock (_lock)
                    File.AppendAllText(_logPath,
                        $"[{Now()}] [RAW_ANSWER] {raw}\n" +
                        $"[{Now()}] [TRIMMED]    {trimmed}\n");
            }
            catch { }
        }

        private static string Now() => DateTime.Now.ToString("HH:mm:ss.fff");
    }
}
