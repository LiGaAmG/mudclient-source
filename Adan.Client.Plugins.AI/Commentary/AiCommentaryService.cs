using System;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.Messages;
using Adan.Client.Plugins.OutputWindow.Messages;
using Adan.Client.Common.Themes;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Commentary
{
    public class AiCommentaryService : IAiCommentaryService
    {
        private readonly AiSettings _settings;
        private readonly ILocalLlmService _llm;
        private readonly IAiContextBuilder _context;
        private readonly MessageConveyor _conveyor;
        private DateTime _lastCommentAt;

        public bool IsEnabled { get; set; }

        public AiCommentaryService(
            AiSettings settings,
            ILocalLlmService llm,
            IAiContextBuilder context,
            MessageConveyor conveyor)
        {
            _settings = settings;
            _llm = llm;
            _context = context;
            _conveyor = conveyor;
            IsEnabled = true;
            _lastCommentAt = DateTime.MinValue;
        }

        public async void OnGameEvent(GameEvent ev, GameSessionState session)
        {
            if (!IsEnabled || !_settings.CommentaryEnabled) return;
            if (_llm == null || _llm.Status != LlmStatus.Ready) return;
            if (ev.Importance < 3) return;
            if ((DateTime.UtcNow - _lastCommentAt).TotalSeconds < _settings.CommentaryCooldownSeconds) return;

            // Детектор цикла без LLM
            if (DetectLoop(session))
            {
                Output("[" + _settings.AssistantName + "]: Похоже, мы ходим по кругу.");
                _lastCommentAt = DateTime.UtcNow;
                return;
            }

            _lastCommentAt = DateTime.UtcNow;
            try
            {
                string trigger = "Кратко прокомментируй (1-2 предложения): " + ev.RawText;
                string prompt = _context.BuildPrompt(trigger, session);
                string comment = await _llm.GenerateAsync(prompt);
                if (!string.IsNullOrWhiteSpace(comment))
                    Output("[" + _settings.AssistantName + "]: " + comment);
            }
            catch { }
        }

        private static bool DetectLoop(GameSessionState session)
        {
            var path = session.RecentRoomPath;
            if (path == null || path.Count < 6) return false;
            int n = path.Count;
            return path[n - 1] == path[n - 4]
                && path[n - 2] == path[n - 5]
                && path[n - 3] == path[n - 6];
        }

        private void Output(string text)
        {
            switch (_settings.OutputTarget)
            {
                case Configuration.AiOutputTarget.AdditionalOutput:
                    _conveyor.PushMessage(new OutputToAdditionalWindowMessage(text, TextColor.Cyan, TextColor.Black));
                    break;
                case Configuration.AiOutputTarget.AdditionalOutput2:
                    _conveyor.PushMessage(new OutputToAdditionalWindow2Message(text, TextColor.Cyan, TextColor.Black));
                    break;
                default:
                    _conveyor.PushMessage(new OutputToMainWindowMessage(text, TextColor.Cyan));
                    break;
            }
        }
    }
}
