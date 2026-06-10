namespace Adan.Client.Plugins.GroupWidget
{
    using System;
    using System.Windows.Threading;

    /// <summary>
    /// Один общий "пульс" мигания для всех иконок аффектов.
    /// Замена WPF-сторибоардам: десятки одновременных анимационных часов
    /// заставляли рендер молотить тяжёлые кадры непрерывно (лаги при мобах
    /// с догорающими аффектами). Здесь — один DispatcherTimer на клиент,
    /// мигающие VM подписываются на тик и дёргают биндинг прозрачности.
    /// </summary>
    internal static class BlinkPulse
    {
        private static DispatcherTimer _timer;

        /// <summary>
        /// Текущая фаза мигания: true — иконка видна полностью.
        /// </summary>
        public static bool PhaseOn = true;

        public static event EventHandler Tick;

        /// <summary>
        /// Запускает общий таймер (однократно). Вызывать на UI-потоке.
        /// </summary>
        public static void EnsureStarted()
        {
            if (_timer != null) return;

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _timer.Tick += (s, e) =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                PhaseOn = !PhaseOn;
                var handler = Tick;
                if (handler != null) handler(null, EventArgs.Empty);
                sw.Stop();
                if (sw.ElapsedMilliseconds >= 20)
                    Common.Conveyor.PerfLog.WriteTotal("BLINK_TICK", sw.ElapsedMilliseconds,
                        string.Format("subscribers={0}", handler != null ? handler.GetInvocationList().Length : 0));
            };
            _timer.Start();
        }
    }
}
