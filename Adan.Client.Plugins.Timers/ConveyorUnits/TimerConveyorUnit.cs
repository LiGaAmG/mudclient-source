using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Adan.Client.Plugins.Timers.ConveyorUnits
{
    using Adan.Client.Common.Commands;
    using Adan.Client.Common.ConveyorUnits;
    using Adan.Client.Common.Messages;
    using Adan.Client.Common.Utils;
    using Common.Conveyor;
    using CSLib.Net.Diagnostics;
    using System.Globalization;
    using System.Text.RegularExpressions;

    public class TimerConveyorUnit : ConveyorUnit
    {
        private PeriodicTimers _timers = new PeriodicTimers();
        private PeriodicTimers _waitTimers = new PeriodicTimers();

        private bool _timerEcho = true;
        private bool _waitEcho = true;

        private readonly Regex _regexTimer = new Regex(@"#timer\s*(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly Regex _regexUnTimer = new Regex(@"#untimer\s*(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly Regex _regexUpTimer = new Regex(@"#uptimer\s*(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly Regex _regexWait = new Regex(@"#wait\s*(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly Regex _regexUnWait = new Regex(@"#unwait\s*(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="OutputToAdditionalWindowConveyorUnit"/> class.
        /// </summary>
        public TimerConveyorUnit(MessageConveyor conveyor)
            : base(conveyor)
        { }

        /// <summary>
        /// Gets a set of message types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledMessageTypes
        {
            get
            {
                return Enumerable.Empty<int>();
            }
        }

        /// <summary>
        /// Gets a set of command types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledCommandTypes
        {
            get
            {
                return new[] { BuiltInCommandTypes.TextCommand };
            }
        }

        public override void HandleCommand(Command command, bool isImport = false)
        {
            Assert.ArgumentNotNull(command, "command");

            var textCommand = command as TextCommand;

            if (textCommand == null)
            {
                return;
            }

            string commandText = textCommand.CommandText.Trim();
            if (CheckTimer(commandText) || CheckUnTimer(commandText) || CheckUpTimer(commandText)
                || CheckWait(commandText) || CheckUnWait(commandText))
            {
                textCommand.Handled = true;
            }
        }

        private bool CheckTimer(string commandText)
        {
            Match match = _regexTimer.Match(commandText);
            if (!match.Success)
            {
                return false;
            }

            string[] args = CommandLineParser.GetArgs(match.Groups[1].ToString());
            if (args.Length > 0)
            {
                string name = args[0];
                if (args.Length > 1)
                {
                    string[] beg_end = args[1].Split('-');
                    if (beg_end.Length == 2 
                        && double.TryParse(beg_end[0].Replace(',', '.'), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out double begin)
                        && double.TryParse(beg_end[1].Replace(',', '.'), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out double end)
                        && begin <= end)
                    {
                        _timers.AddTimer(name, new Tuple<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(begin), TimeSpan.FromSeconds(end)), false, 
                                        (timer) => base.PushMessageToConveyor(new OutputToMainWindowMessage("#TIMER '" + timer.Name + "'")));
                        EchoTimer(new InfoMessage("#Таймер '" + name + "' со случайным периодом '" 
                            + _timers.GetTimer(name).Period.Item1.TotalSeconds + "-" + _timers.GetTimer(name).Period.Item2.TotalSeconds + "' запущен", Common.Themes.TextColor.BrightYellow));
                        return true;
                    }
                    else if (double.TryParse(args[1].Replace(',', '.'), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out double period))
                    {
                        _timers.AddTimer(name, new Tuple<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(period), TimeSpan.FromSeconds(period)), false, 
                                        (timer) => base.PushMessageToConveyor(new OutputToMainWindowMessage("#TIMER '" + timer.Name + "'")));
                        EchoTimer(new InfoMessage("#Таймер '" + name + "' с периодом " + _timers.GetTimer(name).Period.Item1.TotalSeconds + " запущен", Common.Themes.TextColor.BrightYellow));
                        return true;
                    }
                    base.PushMessageToConveyor(new InfoMessage("#Для получения справки наберите #timer help"));
                    return true;
                }
                else if (args[0] == "help")
                {
                    PrintTimerHelp();
                    return true;
                }
                else if (args[0] == "echo")
                {
                    if (args.Length > 1 && args[1] == "on")
                    {
                        _timerEcho = true;
                    }
                    else if (args.Length > 1 && args[1] == "off")
                    {
                        _timerEcho = false;
                    }
                    else
                    {
                        _timerEcho = !_timerEcho;
                    }
                    base.PushMessageToConveyor(new InfoMessage("#Эхо в основное окно " + (_timerEcho ? "включено" : "выключено"), Common.Themes.TextColor.BrightYellow));
                    return true;
                }
            }

            if (_timers.IsEmpty())
            {
                base.PushMessageToConveyor(new InfoMessage("#В данный момент нет ни одного запущенного таймера", Common.Themes.TextColor.BrightYellow));
            }
            else
            {
                base.PushMessageToConveyor(new InfoMessage("#Список запущенных таймеров: ", Common.Themes.TextColor.BrightYellow));
                _timers.ForEachTimer(PrintTimerInfo);
            }
            base.PushMessageToConveyor(new InfoMessage("#Для получения справки наберите #timer help"));

            return true;
        }

        private bool CheckUpTimer(string commandText)
        {
            Match match = _regexUpTimer.Match(commandText);
            if (!match.Success)
            {
                return false;
            }

            string[] args = CommandLineParser.GetArgs(match.Groups[1].ToString());
            if (args.Length > 0)
            {
                string name = args[0];
                if (name == "help")
                {
                    PrintTimerHelp();
                    return true;
                }
                else
                {
                    _timers.ResetTimer(name);
                    EchoTimer(new InfoMessage("#Таймер '" + name + "' запущен заново", Common.Themes.TextColor.BrightYellow));
                    return true;
                }
            }

            base.PushMessageToConveyor(new InfoMessage("#Для получения справки наберите #uptimer help"));

            return true;
        }

        private bool CheckUnTimer(string commandText)
        {
            Match match = _regexUnTimer.Match(commandText);
            if (!match.Success)
            {
                return false;
            }

            string[] args = CommandLineParser.GetArgs(match.Groups[1].ToString());
            if (args.Length > 0)
            {
                string name = args[0];
                if (name == "help")
                {
                    PrintTimerHelp();
                    return true;
                }
                else if (name == "all")
                {
                    _timers.Clear();
                    EchoTimer(new InfoMessage("#Таймеры остановлены", Common.Themes.TextColor.BrightYellow));
                    return true;
                }
                else
                {
                    _timers.RemoveTimer(name);
                    EchoTimer(new InfoMessage("#Таймер '" + name + "' остановлен", Common.Themes.TextColor.BrightYellow));
                    return true;
                }
            }

            base.PushMessageToConveyor(new InfoMessage("#Для получения справки наберите #untimer help"));

            return true;
        }

        private bool CheckWait(string commandText)
        {
            Match match = _regexWait.Match(commandText);
            if (!match.Success)
            {
                return false;
            }

            string[] args = CommandLineParser.GetArgs(match.Groups[1].ToString());
            if (args.Length > 0)
            {
                if (args.Length > 1)
                {
                    string[] beg_end = args[0].Split('-');
                    if (beg_end.Length == 2
                        && double.TryParse(beg_end[0].Replace(',', '.'), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out double begin)
                        && double.TryParse(beg_end[1].Replace(',', '.'), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out double end)
                        && begin <= end)
                    {
                        double period = begin + new Random().NextDouble() * (end - begin);
                        string name = _waitTimers.AddTimer(new Tuple<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(period), TimeSpan.FromSeconds(period)), true,
                                                (timer) =>
                                                {
                                                    Adan.Client.Common.Conveyor.PerfLog.WriteTotal("WAIT_FIRE", 0, args[1]);
                                                    base.PushCommandToConveyor(new TextCommand(args[1]));
                                                    Conveyor.RootModel.PushCommandToConveyor(FlushOutputQueueCommand.Instance);
                                                });
                        EchoWait(new InfoMessage("#Отложенная команда будет выполнена через " + _waitTimers.GetTimer(name).Period.Item1.TotalSeconds + " секунд ", Common.Themes.TextColor.BrightYellow));
                        return true;
                    }
                    else if (double.TryParse(args[0].Replace(',', '.'), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out double period))
                    {
                        string name = _waitTimers.AddTimer(new Tuple<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(period), TimeSpan.FromSeconds(period)), true,
                                                (timer) =>
                                                {
                                                    Adan.Client.Common.Conveyor.PerfLog.WriteTotal("WAIT_FIRE", 0, args[1]);
                                                    base.PushCommandToConveyor(new TextCommand(args[1]));
                                                    Conveyor.RootModel.PushCommandToConveyor(FlushOutputQueueCommand.Instance);
                                                });
                        EchoWait(new InfoMessage("#Отложенная команда будет выполнена через " + _waitTimers.GetTimer(name).Period.Item1.TotalSeconds + " секунд ", Common.Themes.TextColor.BrightYellow));
                        return true;
                    }
                }
                else if (args[0] == "help")
                {
                    PrintWaitHelp();
                    return true;
                }
                else if (args[0] == "echo")
                {
                    if (args.Length > 1 && args[1] == "on")
                    {
                        _waitEcho = true;
                    }
                    else if (args.Length > 1 && args[1] == "off")
                    {
                        _waitEcho = false;
                    }
                    else
                    {
                        _waitEcho = !_waitEcho;
                    }
                    base.PushMessageToConveyor(new InfoMessage("#Эхо в основное окно " + (_waitEcho ? "включено" : "выключено"), Common.Themes.TextColor.BrightYellow));
                    return true;
                }
            }
            base.PushMessageToConveyor(new InfoMessage("#Для получения справки наберите #wait help"));

            return true;
        }

        private bool CheckUnWait(string commandText)
        {
            Match match = _regexUnWait.Match(commandText);
            if (!match.Success)
            {
                return false;
            }

            string[] args = CommandLineParser.GetArgs(match.Groups[1].ToString());
            if (args.Length > 0)
            {
                if (args[0] == "help")
                {
                    PrintWaitHelp();
                    return true;
                }
                else if (args[0] == "all")
                {
                    _waitTimers.Clear();
                    EchoWait(new InfoMessage("#Все отложенные команды удалены", Common.Themes.TextColor.BrightYellow));
                    return true;
                }
            }

            base.PushMessageToConveyor(new InfoMessage("#Для получения справки наберите #unwait help"));

            return true;
        }

        private void PrintWaitHelp()
        {
            base.PushMessageToConveyor(new InfoMessage("#Добавление отложенной команды: #wait {время} {команда}"));
            base.PushMessageToConveyor(new InfoMessage("#Время может быть дробным в секундах или каждый раз случайным в диапазоне, например, #wait {1.5-2.5} {asd}"));
            base.PushMessageToConveyor(new InfoMessage("#Удаление всех отложенных команды: #unwait all"));
            base.PushMessageToConveyor(new InfoMessage("#Включение/Выключение отклика о запуске команды wait в основное окно: #wait echo on/off"));
        }

        private void PrintTimerHelp()
        {
            base.PushMessageToConveyor(new InfoMessage("#Добавление таймера: #timer {номер} {период в секундах}"));
            base.PushMessageToConveyor(new InfoMessage("#Период может быть дробным в секундах или случайным в диапазоне, например, #timer 1 {1.5-2.5}"));
            base.PushMessageToConveyor(new InfoMessage("#Удаление таймера/всех таймеров: #untimer {номер}/{all}"));
            base.PushMessageToConveyor(new InfoMessage("#Получение информации о всех запущенных таймерах: #timer"));
            base.PushMessageToConveyor(new InfoMessage("#Получение информации о конкретном таймере: #timer {номер}"));
            base.PushMessageToConveyor(new InfoMessage("#Ресет таймера (таймер сработает через заданный период, начиная с текущего момента): #uptimer {номер}"));
            base.PushMessageToConveyor(new InfoMessage("#Включение/Выключение отклика о запуске/остановке команды таймер в основное окно: #timer echo on/off"));
            base.PushMessageToConveyor(new InfoMessage("#При срабатывание таймера в основное окно будет выведено сообщение '#TIMER + номер таймера'"));
        }

        private void PrintTimerInfo(string name)
        {
            if (_timers.CheckTimer(name))
            {
                PrintTimerInfo(_timers.GetTimer(name));
            }
            else
            {
                base.PushMessageToConveyor(new InfoMessage("#Таймер '" + name + "' не запущен", Common.Themes.TextColor.BrightYellow));
            }
        }

        private void PrintTimerInfo(TimerItem timer)
        {
            if (timer.Period.Item1 != timer.Period.Item2)
            {
                base.PushMessageToConveyor(new InfoMessage("#Таймер '" + timer.Name + "' запущен со случайным периодом '" + timer.Period.Item1.TotalSeconds + "-"
                        + timer.Period.Item2.TotalSeconds
                        + "' секунд (до срабатывания " + timer.GetTimeTillElapse().TotalSeconds + " секунд)", Common.Themes.TextColor.BrightYellow));
            }
            else
            {
                base.PushMessageToConveyor(new InfoMessage("#Таймер '" + timer.Name + "' запущен с периодом '" + timer.Period.Item1.TotalSeconds
                        + "' секунд (до срабатывания " + timer.GetTimeTillElapse().TotalSeconds + " секунд)", Common.Themes.TextColor.BrightYellow));

            }
        }

        private void EchoTimer(Message message)
        {
            if (_timerEcho)
            {
                base.PushMessageToConveyor(message);
            }
        }

        private void EchoWait(Message message)
        {
            if (_waitEcho)
            {
                base.PushMessageToConveyor(message);
            }
        }
    }
}
