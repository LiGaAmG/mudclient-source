namespace Adan.Client.Common.Conveyor
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    /// <summary>
    /// Накопительная статистика производительности конвейера.
    /// Заполняется из MessageConveyor.PushMessage (любой поток),
    /// читается командой #perf. Сбрасывается командой #perf reset.
    /// </summary>
    public static class PerfStats
    {
        private class UnitStat
        {
            public long Calls;
            public long TotalTicks;
            public long MaxTicks;
        }

        private static readonly ConcurrentDictionary<string, UnitStat> _units = new ConcurrentDictionary<string, UnitStat>();
        private static DateTime _since = DateTime.Now;

        // Задержка UI-потока (обновляется монитором из MainWindow)
        private static long _uiLastMs;
        private static long _uiMaxMs;

        /// <summary>
        /// Записать время одного вызова HandleMessage юнита. Потокобезопасно, дёшево.
        /// </summary>
        public static void RecordUnit(string unitName, long ticks)
        {
            var stat = _units.GetOrAdd(unitName, _ => new UnitStat());
            System.Threading.Interlocked.Increment(ref stat.Calls);
            System.Threading.Interlocked.Add(ref stat.TotalTicks, ticks);
            // Max без блокировки: небольшая гонка допустима для статистики
            if (ticks > stat.MaxTicks) stat.MaxTicks = ticks;
        }

        /// <summary>
        /// Записать измеренную задержку UI-потока (мс).
        /// </summary>
        public static void RecordUiLatency(long ms)
        {
            _uiLastMs = ms;
            if (ms > _uiMaxMs) _uiMaxMs = ms;
        }

        public static long UiLastMs { get { return _uiLastMs; } }
        public static long UiMaxMs { get { return _uiMaxMs; } }

        // Хост сервера для встроенного пинга (заполняется при подключении)
        private static volatile string _serverHost;
        public static string ServerHost
        {
            get { return _serverHost; }
            set { _serverHost = value; }
        }

        private static long _pingLastMs = -1;
        private static long _pingMaxMs;

        // Per-uid последний завершённый RTT (мс). Заполняется RecordPing.
        private static readonly ConcurrentDictionary<string, long> _pingPerUid =
            new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

        // Висящие (неотвеченные) отправки по соединениям: uid -> Stopwatch ticks.
        // Позволяет светофору показывать яму ЖИВЬЁМ, пока ответ ещё не пришёл.
        private static readonly ConcurrentDictionary<string, long> _pendingSends =
            new ConcurrentDictionary<string, long>();

        public static void SetPendingSend(string uid, long timestamp)
        {
            if (uid != null) _pendingSends[uid] = timestamp;
        }

        public static void ClearPendingSend(string uid)
        {
            long dummy;
            if (uid != null) _pendingSends.TryRemove(uid, out dummy);
        }

        // Ожидание КОМНАТЫ после шага маршрута: rtt гасится первым же мелким пакетом
        // (эхо/промпт), а комната — большой пакет и может ехать сильно дольше.
        private static long _roomWaitTimestamp;

        public static void RoomWaitStarted()
        {
            System.Threading.Interlocked.Exchange(ref _roomWaitTimestamp, Stopwatch.GetTimestamp());
        }

        public static void RoomWaitEnded()
        {
            System.Threading.Interlocked.Exchange(ref _roomWaitTimestamp, 0);
        }

        /// <summary>
        /// Сколько мс уже ждём комнату после шага маршрута (0 — не ждём).
        /// </summary>
        public static long RoomWaitMs
        {
            get
            {
                long ts = System.Threading.Interlocked.Read(ref _roomWaitTimestamp);
                if (ts == 0) return 0;
                long age = (Stopwatch.GetTimestamp() - ts) * 1000 / Stopwatch.Frequency;
                // Страховка от залипания (маршрут прерван, комната не придёт)
                return age > 30000 ? 0 : age;
            }
        }

        /// <summary>
        /// Возраст самой старой неотвеченной команды (мс), 0 — всё отвечено.
        /// </summary>
        public static long OldestPendingMs
        {
            get
            {
                long oldest = 0;
                long now = Stopwatch.GetTimestamp();
                foreach (var kv in _pendingSends)
                {
                    long age = (now - kv.Value) * 1000 / Stopwatch.Frequency;
                    if (age > oldest) oldest = age;
                }

                return oldest;
            }
        }

        public static long PingLastMs { get { return _pingLastMs; } }
        public static long PingMaxMs { get { return _pingMaxMs; } }

        public static void RecordPing(long ms, string uid = null)
        {
            _pingLastMs = ms;
            if (ms > _pingMaxMs) _pingMaxMs = ms;
            if (uid != null) _pingPerUid[uid] = ms;
        }

        /// <summary>
        /// Возвращает "эффективный" RTT для каждого известного таба (мс).
        /// Если таб ждёт ответа — берём возраст висящей отправки, иначе последний завершённый RTT.
        /// </summary>
        public static Dictionary<string, long> GetEffectiveRttPerUid()
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            long now = Stopwatch.GetTimestamp();

            // Сначала заполняем последними завершёнными RTT
            foreach (var kv in _pingPerUid)
                result[kv.Key] = kv.Value;

            // Перекрываем висящими отправками где они больше
            foreach (var kv in _pendingSends)
            {
                long age = (now - kv.Value) * 1000 / Stopwatch.Frequency;
                long existing;
                if (!result.TryGetValue(kv.Key, out existing) || age > existing)
                    result[kv.Key] = age;
            }

            return result;
        }

        // Активные игровые таймеры (#timer + #wait, по всем табам)
        private static int _activeGameTimers;
        public static int ActiveGameTimers { get { return _activeGameTimers; } }
        public static void GameTimerAdded() { System.Threading.Interlocked.Increment(ref _activeGameTimers); }
        public static void GameTimerRemoved() { System.Threading.Interlocked.Decrement(ref _activeGameTimers); }

        // Счётчик сообщений, прошедших через конвейер (всех типов, по всем табам)
        private static long _totalMessages;
        public static long TotalMessages { get { return _totalMessages; } }

        public static void RecordMessage()
        {
            System.Threading.Interlocked.Increment(ref _totalMessages);
        }

        /// <summary>
        /// Снимок текущих накопленных значений (для вычисления дельт «за последнюю секунду»).
        /// Ключ — имя юнита, значение — [TotalTicks, Calls].
        /// </summary>
        public static Dictionary<string, long[]> Snapshot()
        {
            var result = new Dictionary<string, long[]>();
            foreach (var kv in _units)
                result[kv.Key] = new[] { kv.Value.TotalTicks, kv.Value.Calls };
            return result;
        }

        public static void Reset()
        {
            _units.Clear();
            _uiMaxMs = 0;
            _pingMaxMs = 0;
            _pingPerUid.Clear();
            _since = DateTime.Now;
        }

        /// <summary>
        /// Текстовый отчёт для вывода в окно клиента.
        /// </summary>
        public static IEnumerable<string> BuildReport()
        {
            var elapsed = DateTime.Now - _since;
            yield return string.Format("=== Производительность (за {0:hh\\:mm\\:ss}, с {1:HH:mm:ss}) ===", elapsed, _since);
            yield return string.Format("UI-поток: сейчас {0} мс, максимум {1} мс", _uiLastMs, _uiMaxMs);
            if (_pingLastMs >= 0)
                yield return string.Format("Отклик игры (команда→ответ): последний {0} мс, максимум {1} мс", _pingLastMs, _pingMaxMs);
            yield return string.Format("Активных таймеров (#timer/#wait, все табы): {0}", _activeGameTimers);
            yield return string.Format("GC: gen0={0} gen1={1} gen2={2}, памяти {3:0} МБ",
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                GC.GetTotalMemory(false) / 1048576.0);

            int renderTier = -1;
            try { renderTier = System.Windows.Media.RenderCapability.Tier >> 16; }
            catch { }
            yield return string.Format("Рендеринг: Tier {0} ({1})", renderTier,
                renderTier == 2 ? "полное GPU-ускорение" :
                renderTier == 1 ? "частичное GPU-ускорение" :
                renderTier == 0 ? "ПРОГРАММНЫЙ — всё рисует процессор!" : "не удалось определить");

            if (_units.IsEmpty)
            {
                yield return "Нет данных по юнитам (не было сообщений).";
            }
            else
            {
                yield return string.Format("{0,-34} {1,10} {2,10} {3,8} {4,8}", "Юнит", "Вызовов", "Всего мс", "Сред мкс", "Макс мс");
                double ticksPerMs = Stopwatch.Frequency / 1000.0;
                double ticksPerUs = Stopwatch.Frequency / 1000000.0;
                foreach (var kv in _units.OrderByDescending(kv => kv.Value.TotalTicks))
                {
                    var s = kv.Value;
                    long totalMs = (long)(s.TotalTicks / ticksPerMs);
                    long avgUs = s.Calls > 0 ? (long)(s.TotalTicks / s.Calls / ticksPerUs) : 0;
                    long maxMs = (long)(s.MaxTicks / ticksPerMs);
                    yield return string.Format("{0,-34} {1,10} {2,10} {3,8} {4,8}", kv.Key, s.Calls, totalMs, avgUs, maxMs);
                }
            }

            yield return "Команды: #perf — показать, #perf reset — сбросить, #perf clear — очистить лог.";
            yield return "Подробный лог медленных событий: %TEMP%\\adan_perf.log";
        }
    }
}
