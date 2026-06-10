using Adan.Client.Common.Conveyor;
using Adan.Client.Common.Messages;
using Adan.Client.Plugins.Timers.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Adan.Client.Plugins.Timers
{
    public class TimerItem : IDisposable
    {
        private TimerWrapper _timer;
        public TimerItem(TimerWrapper.Callback callback, Tuple<TimeSpan, TimeSpan> span)
        {
            _timer = new TimerWrapper(callback, this, span);
        }

        ~TimerItem()
        {
            Dispose();
        }

        public Tuple<TimeSpan, TimeSpan> Period
        { 
            get
            {
                return _timer.Interval;
            }
        }

        public bool Once
        {
            get;
            set;
        }

        public string Name
        {
            get;
            set;
        }

        public Action<TimerItem> Action
        {
            get;
            set;
        }

        public TimeSpan GetTimeTillElapse()
        {
            var till = TimeSpan.FromMilliseconds(_timer.NextTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return till.TotalMilliseconds > 0 ? till : TimeSpan.Zero;
        }

        public void Reset()
        {
            _timer.Reset();
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }

    public class PeriodicTimers
    {
        private readonly Dictionary<string, TimerItem> _timers = new Dictionary<string, TimerItem>();

        public PeriodicTimers() { }

        public bool IsEmpty()
        {
            return _timers.Count() == 0;
        }

        public void ForEachTimer(Action<TimerItem> action)
        {
            foreach(var kvp in _timers)
            {
                action(kvp.Value);
            }
        }

        public TimerItem GetTimer(string name)
        {
            return _timers[name];
        }

        public void Clear()
        {
            ForEachTimer((timer) => timer.Dispose());
            for (int i = 0; i < _timers.Count; i++)
                Adan.Client.Common.Conveyor.PerfStats.GameTimerRemoved();
            _timers.Clear();
        }

        public bool CheckTimer(string name)
        {
            return _timers.ContainsKey(name);
        }

        public TimeSpan LeftTillElapsed(string name)
        {
            return _timers[name].GetTimeTillElapse();
        }

        public void AddTimer(string name, Tuple<TimeSpan, TimeSpan> span, bool once, Action<TimerItem> action)
        {
            if (_timers.ContainsKey(name))
            {
                RemoveTimer(name);
            }
            _timers.Add(name, new TimerItem(Timer_Elapsed, span)
                                {
                                    Name = name,
                                    Once = once,
                                    Action = action
                                });
            Adan.Client.Common.Conveyor.PerfStats.GameTimerAdded();
        }

        public string AddTimer(Tuple<TimeSpan, TimeSpan> span, bool once, Action<TimerItem> action)
        {
            string name = Guid.NewGuid().ToString("N");
            AddTimer(name, span, once, action);
            return name;
        }

        public void RemoveTimer(string name)
        {
            if (_timers.ContainsKey(name))
            {
                _timers[name].Dispose();
                _timers.Remove(name);
                Adan.Client.Common.Conveyor.PerfStats.GameTimerRemoved();
            }
        }

        public bool ResetTimer(string name)
        {
            if (!_timers.ContainsKey(name))
            {
                return false;
            }

            _timers[name].Reset();
            return true;
        }

        private void Timer_Elapsed(object sender)
        {
            TimerItem item = sender as TimerItem;

            // Маячок: срабатывание игрового таймера + время исполнения его действия
            var sw = System.Diagnostics.Stopwatch.StartNew();
            item.Action(item);
            sw.Stop();
            Adan.Client.Common.Conveyor.PerfLog.WriteTotal("TIMER_FIRE", sw.ElapsedMilliseconds,
                string.Format("name={0} once={1}", item.Name, item.Once));

            if (item.Once)
            {
                item.Dispose();
                _timers.Remove(item.Name);
                Adan.Client.Common.Conveyor.PerfStats.GameTimerRemoved();
            }
        }
    }
}
