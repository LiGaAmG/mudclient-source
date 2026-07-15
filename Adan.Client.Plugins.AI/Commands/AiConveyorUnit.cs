using System;
using System.Collections.Generic;
using Adan.Client.Common.Commands;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.Messages;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Commands
{
    public class AiConveyorUnit : Adan.Client.Common.ConveyorUnits.ConveyorUnit
    {
        private readonly IAiCommandHandler _commandHandler;
        private readonly IAiCommentaryService _commentary;
        private readonly GameSessionState _session;
        private readonly GameEventExtractor _extractor;
        private readonly IGameMemoryService _memory;
        private readonly AiSettings _settings;
        private const int MaxRecentLines = 30;

        public AiConveyorUnit(
            MessageConveyor conveyor,
            IAiCommandHandler commandHandler,
            IAiCommentaryService commentary,
            GameEventExtractor extractor,
            GameSessionState session,
            IGameMemoryService memory = null,
            AiSettings settings = null)
            : base(conveyor)
        {
            _settings = settings;
            _commandHandler = commandHandler;
            _commentary = commentary;
            _extractor = extractor;
            _session = session;
            _memory = memory;
        }

        public override IEnumerable<int> HandledMessageTypes
        {
            get { yield return BuiltInMessageTypes.TextMessage; }
        }

        public override IEnumerable<int> HandledCommandTypes
        {
            get { yield return BuiltInCommandTypes.TextCommand; }
        }

        public override void HandleCommand(Adan.Client.Common.Commands.Command command, bool isImport = false)
        {
            var textCmd = command as TextCommand;
            if (textCmd == null) return;

            if (textCmd.CommandText != null)
            {
                var trimmed = textCmd.CommandText.TrimStart();
                var isAiCommand = trimmed.StartsWith("/ai", System.StringComparison.OrdinalIgnoreCase)
                               || trimmed.StartsWith("ии", System.StringComparison.OrdinalIgnoreCase);
                if (isAiCommand && _commandHandler != null && _commandHandler.TryHandle(textCmd.CommandText))
                {
                    textCmd.Handled = true;
                }
            }
        }

        public override void HandleMessage(Message message)
        {
            // Модуль выключен — не тратим время на буфер/регексы/SQLite.
            // Команды (ии вкл) при этом продолжают работать через HandleCommand.
            if (_settings != null && !_settings.Enabled) return;

            var textMsg = message as OutputToMainWindowMessage;
            if (textMsg == null) return;

            var line = textMsg.InnerText;
            if (string.IsNullOrEmpty(line)) return;

            lock (_session.RecentLines)
            {
                _session.RecentLines.Add(line);
                while (_session.RecentLines.Count > MaxRecentLines)
                    _session.RecentLines.RemoveAt(0);
            }

            if (_extractor != null && _memory != null)
            {
                var ev = _extractor.TryExtract(line, _session);
                // RoomEntered — 70%+ всех событий, но в контекст промпта не попадает.
                // Не тратим SQLite-запись на каждый переход между комнатами.
                if (ev != null && ev.Type != GameEventType.RoomEntered)
                {
                    try
                    {
                        _memory.SaveEvent(new GameEventRecord
                        {
                            Timestamp = ev.Timestamp,
                            EventType = ev.Type,
                            RawText = ev.RawText,
                            Importance = ev.Importance,
                            ZoneId = _session.CurrentZoneId > 0 ? (long?)_session.CurrentZoneId : null,
                            RoomId = _session.CurrentRoomId > 0 ? (long?)_session.CurrentRoomId : null,
                            StructuredDataJson = ev.EntityName != null ? "{\"entity\":\"" + ev.EntityName.Replace("\"", "\\\"") + "\"}" : null,
                        });
                    }
                    catch { }
                }
            }
        }
    }
}
