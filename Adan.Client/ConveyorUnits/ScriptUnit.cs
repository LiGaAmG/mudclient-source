using System.Collections.Generic;
using System.Linq;
using Adan.Client.Common.Commands;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.ConveyorUnits;
using Adan.Client.Common.Messages;
using Adan.Client.Common.Scripting;

namespace Adan.Client.ConveyorUnits
{
    /// <summary>
    /// Handles 'скрипт' commands for named script management from the command line.
    /// </summary>
    public class ScriptUnit : ConveyorUnit
    {
        private const string CmdPrefix = "скрипт";
        private const string CmdList   = "список";
        private const string CmdStart  = "старт";
        private const string CmdStop   = "стоп";

        public ScriptUnit(MessageConveyor conveyor) : base(conveyor) { }

        public override IEnumerable<int> HandledMessageTypes => Enumerable.Empty<int>();
        public override IEnumerable<int> HandledCommandTypes
            => new[] { BuiltInCommandTypes.TextCommand };

        public override void HandleCommand(Command command, bool isImport = false)
        {
            var textCommand = command as TextCommand;
            if (textCommand == null) return;

            var text = textCommand.CommandText.Trim();

            if (string.Equals(text, CmdPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                ShowHelp();
                command.Handled = true;
                return;
            }

            if (!text.StartsWith(CmdPrefix + " ", System.StringComparison.OrdinalIgnoreCase))
                return;

            var args = text.Substring(CmdPrefix.Length).Trim();

            if (string.Equals(args, CmdList, System.StringComparison.OrdinalIgnoreCase))
            {
                ShowList();
                command.Handled = true;
                return;
            }

            if (args.StartsWith(CmdStart + " ", System.StringComparison.OrdinalIgnoreCase))
            {
                var name = args.Substring(CmdStart.Length).Trim();
                StartScript(name);
                command.Handled = true;
                return;
            }

            if (args.StartsWith(CmdStop + " ", System.StringComparison.OrdinalIgnoreCase))
            {
                var name = args.Substring(CmdStop.Length).Trim();
                StopScript(name);
                command.Handled = true;
                return;
            }

            ShowHelp();
            command.Handled = true;
        }

        private void ShowHelp()
        {
            Info("скрипт список           — список скриптов и их статусы");
            Info("скрипт старт <имя>      — запустить скрипт");
            Info("скрипт стоп <имя>       — остановить скрипт");
        }

        private void ShowList()
        {
            var model = Conveyor.RootModel;
            if (model?.Profile?.Scripts == null || model.Profile.Scripts.Count == 0)
            {
                Info("Нет скриптов. Добавьте через Options → Scripts.");
                return;
            }

            foreach (var script in model.Profile.Scripts)
            {
                var status = model.ScriptHost.GetScriptStatus(script.Name);
                var dot = StatusDot(status);
                var auto = script.IsEnabled ? " [авто]" : "";
                var error = status == ScriptRunStatus.Faulted
                    ? "  ошибка: " + (model.ScriptHost.GetScriptError(script.Name) ?? "?")
                    : string.Empty;
                Info(string.Format("{0} {1}{2}{3}", dot, script.Name, auto, error));
            }
        }

        private void StartScript(string name)
        {
            var model = Conveyor.RootModel;
            if (model?.Profile?.Scripts == null) return;

            var script = model.Profile.Scripts.FirstOrDefault(
                s => string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase));

            if (script == null)
            {
                Info(string.Format("Скрипт '{0}' не найден. Проверьте 'скрипт список'.", name));
                return;
            }

            model.ScriptHost.StartScript(script.Name, script.Code);
        }

        private void StopScript(string name)
        {
            var model = Conveyor.RootModel;
            if (model?.Profile?.Scripts == null) return;

            var script = model.Profile.Scripts.FirstOrDefault(
                s => string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase));

            if (script == null)
            {
                Info(string.Format("Скрипт '{0}' не найден. Проверьте 'скрипт список'.", name));
                return;
            }

            model.ScriptHost.StopScript(script.Name);
        }

        private static string StatusDot(ScriptRunStatus status)
        {
            switch (status)
            {
                case ScriptRunStatus.Running:
                case ScriptRunStatus.WaitingOnTimer:
                case ScriptRunStatus.WaitingOnGroupState:
                case ScriptRunStatus.WaitingOnRoomState:
                case ScriptRunStatus.WaitingOnRoomChange:
                case ScriptRunStatus.WaitingOnText:
                    return "●";  // зелёный (подсветим через InfoMessage цвет не можем — просто символ)
                case ScriptRunStatus.Faulted:
                    return "✖";
                case ScriptRunStatus.Finished:
                    return "◎";
                default:
                    return "○";
            }
        }

        private void Info(string text)
        {
            Conveyor.PushMessage(new InfoMessage(text));
        }
    }
}
