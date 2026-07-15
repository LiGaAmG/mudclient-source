using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adan.Client.Common.Commands;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.ConveyorUnits;
using Adan.Client.Common.Messages;
using Adan.Client.Common.Scripting;
using Adan.Client.Common.Settings;

namespace Adan.Client.ConveyorUnits
{
    /// <summary>Handles the shared .lua script catalogue from the command line.</summary>
    public class ScriptUnit : ConveyorUnit
    {
        private const string Prefix = "скрипт";

        public ScriptUnit(MessageConveyor conveyor) : base(conveyor) { }

        public override IEnumerable<int> HandledMessageTypes => Enumerable.Empty<int>();
        public override IEnumerable<int> HandledCommandTypes => new[] { BuiltInCommandTypes.TextCommand };

        public override void HandleCommand(Command command, bool isImport = false)
        {
            var textCommand = command as TextCommand;
            if (textCommand == null) return;
            var text = textCommand.CommandText.Trim();
            if (!string.Equals(text, Prefix, StringComparison.OrdinalIgnoreCase) && !text.StartsWith(Prefix + " ", StringComparison.OrdinalIgnoreCase)) return;

            command.Handled = true;
            var args = text.Length == Prefix.Length ? string.Empty : text.Substring(Prefix.Length).Trim();
            if (string.IsNullOrEmpty(args)) { ShowHelp(); return; }

            var parts = args.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            var action = parts[0].ToLowerInvariant();
            if (action == "список") { ShowList(); return; }
            if (action == "старт" && parts.Length >= 2) { Start(parts[1]); return; }
            if (action == "стоп" && parts.Length >= 2) { Stop(parts[1]); return; }
            if (action == "рестарт" && parts.Length >= 2) { Restart(parts[1]); return; }
            if (action == "создать" && parts.Length >= 2) { Create(parts[1]); return; }
            if (action == "удалить" && parts.Length >= 2) { Delete(parts[1]); return; }
            if (action == "авто" && parts.Length >= 3) { SetAutoStart(parts[1], parts[2]); return; }
            if (action == "путь") { Info("Папка скриптов: " + ScriptsFolder); return; }
            ShowHelp();
        }

        private string ScriptsFolder => Path.Combine(SettingsHolder.Instance.Folder, "scripts");

        private void ShowHelp()
        {
            Info("скрипт список                         — список файлов и статусов");
            Info("скрипт старт <имя.lua>                — запустить на текущем профиле");
            Info("скрипт стоп <имя.lua>                 — остановить на текущем профиле");
            Info("скрипт рестарт <имя.lua>              — перезапустить");
            Info("скрипт создать <имя.lua>              — создать файл");
            Info("скрипт удалить <имя.lua>              — остановить и удалить файл");
            Info("скрипт авто <имя.lua> вкл|выкл         — автостарт при подключении");
            Info("скрипт путь                           — показать папку scripts");
        }

        private void ShowList()
        {
            var model = Conveyor.RootModel;
            using (var manager = new ScriptFileManager(ScriptsFolder, false))
            {
                if (manager.Entries.Count == 0) { Info("Нет скриптов. Создайте через «скрипт создать <имя.lua>» или окно Scripts."); return; }
                foreach (var entry in manager.Entries)
                {
                    var assigned = model != null && model.Profile != null && (entry.IsGlobal || entry.EnabledProfileNames.Contains(model.Profile.Name));
                    var status = model == null ? ScriptRunStatus.NotRunning : model.ScriptHost.GetScriptStatus(entry.FileName);
                    var flags = (entry.IsGlobal ? " [глобальный]" : assigned ? " [назначен]" : " [не назначен]") + (entry.AutoStart ? " [авто]" : "");
                    Info(string.Format("{0} {1}{2}", StatusDot(status), entry.FileName, flags));
                }
            }
        }

        private void Start(string requestedName)
        {
            var model = Conveyor.RootModel;
            if (model == null || model.Profile == null) { Info("Нет активного профиля."); return; }
            using (var manager = new ScriptFileManager(ScriptsFolder, false))
            {
                var entry = Find(manager, requestedName);
                if (entry == null) { NotFound(requestedName); return; }
                if (!entry.IsGlobal && !entry.EnabledProfileNames.Contains(model.Profile.Name)) { Info("Скрипт не назначен текущему профилю."); return; }
                model.ScriptHost.StartScript(entry.FileName, manager.ReadCode(entry.FileName));
            }
        }

        private void Stop(string requestedName)
        {
            var model = Conveyor.RootModel;
            if (model == null) { Info("Нет активной вкладки."); return; }
            model.ScriptHost.StopScript(NormalizeName(requestedName));
        }

        private void Restart(string requestedName)
        {
            Stop(requestedName);
            Start(requestedName);
        }

        private void Create(string name)
        {
            using (var manager = new ScriptFileManager(ScriptsFolder, false))
            {
                var path = manager.CreateScript(name);
                Info("Создан скрипт: " + Path.GetFileName(path));
            }
        }

        private void Delete(string requestedName)
        {
            var model = Conveyor.RootModel;
            using (var manager = new ScriptFileManager(ScriptsFolder, false))
            {
                var entry = Find(manager, requestedName);
                if (entry == null) { NotFound(requestedName); return; }
                model?.ScriptHost.StopScript(entry.FileName);
                manager.DeleteScript(entry.FileName);
                Info("Удалён скрипт: " + entry.FileName);
            }
        }

        private void SetAutoStart(string requestedName, string value)
        {
            var enabled = value.Equals("вкл", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase);
            var disabled = value.Equals("выкл", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase);
            if (!enabled && !disabled) { Info("Используйте: скрипт авто <имя.lua> вкл|выкл"); return; }
            using (var manager = new ScriptFileManager(ScriptsFolder, false))
            {
                var entry = Find(manager, requestedName);
                if (entry == null) { NotFound(requestedName); return; }
                entry.AutoStart = enabled;
                manager.SaveMetadata();
                Info(string.Format("Автостарт {0}: {1}", entry.FileName, enabled ? "включён" : "выключен"));
            }
        }

        private static ScriptFileEntry Find(ScriptFileManager manager, string requestedName)
        {
            var name = NormalizeName(requestedName);
            return manager.Entries.FirstOrDefault(entry => string.Equals(entry.FileName, name, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeName(string name) => name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? name : name + ".lua";

        private static string StatusDot(ScriptRunStatus status)
        {
            switch (status)
            {
                case ScriptRunStatus.Running:
                case ScriptRunStatus.WaitingOnTimer:
                case ScriptRunStatus.WaitingOnGroupState:
                case ScriptRunStatus.WaitingOnRoomState:
                case ScriptRunStatus.WaitingOnRoomChange:
                case ScriptRunStatus.WaitingOnText: return "●";
                case ScriptRunStatus.Faulted: return "✖";
                case ScriptRunStatus.Finished: return "○";
                default: return "◌";
            }
        }

        private void NotFound(string name) { Info("Скрипт '" + name + "' не найден. Проверьте «скрипт список»."); }
        private void Info(string text) { Conveyor.PushMessage(new InfoMessage(text)); }
    }
}
