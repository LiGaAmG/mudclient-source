using System;
using System.Threading.Tasks;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.Messages;
using Adan.Client.Common.Themes;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Memory;
using Adan.Client.Plugins.OutputWindow.Messages;

namespace Adan.Client.Plugins.AI.Commands
{
    public class AiCommandHandler : IAiCommandHandler
    {
        private readonly ILocalLlmService _llm;
        private readonly IAiContextBuilder _contextBuilder;
        private readonly IGameMemoryService _memory;
        private readonly ILoreSearchService _lore;
        private readonly GameSessionState _session;
        private readonly AiSettings _settings;
        private readonly MessageConveyor _conveyor;

        private string _lastPrompt = null;
        private static volatile bool _generating;
        private static readonly object _generateLock = new object();

        public AiCommandHandler(
            ILocalLlmService llm,
            IAiContextBuilder contextBuilder,
            IGameMemoryService memory,
            ILoreSearchService lore,
            GameSessionState session,
            AiSettings settings,
            MessageConveyor conveyor)
        {
            _llm = llm;
            _contextBuilder = contextBuilder;
            _memory = memory;
            _lore = lore;
            _session = session;
            _settings = settings;
            _conveyor = conveyor;
        }

        public bool TryHandle(string commandText)
        {
            if (commandText == null) return false;
            var trimmed = commandText.Trim();

            string rest;
            if (trimmed.StartsWith("/ai", StringComparison.OrdinalIgnoreCase))
                rest = trimmed.Substring(3).Trim();
            else if (trimmed.StartsWith("ии", StringComparison.OrdinalIgnoreCase))
                rest = trimmed.Substring(2).Trim();
            else
                return false;

            if (string.IsNullOrEmpty(rest) || Eq(rest, "help") || Eq(rest, "хелп"))
            {
                PrintHelp();
                return true;
            }

            if (Eq(rest, "on") || Eq(rest, "вкл"))
            {
                _settings.Enabled = true;
                AiSettingsSerializer.Save(_settings);
                var modelPath = _settings.ResolvedModelPath;
                if (!System.IO.File.Exists(modelPath))
                {
                    Info("AI включён, но модель не найдена: " + modelPath);
                    Info("Положи .gguf файл по этому пути и перезапусти клиент.");
                }
                else
                {
                    if (_llm != null && _llm.Status == Abstractions.LlmStatus.Disabled)
                    {
                        Info("AI включён. Загружаю модель...");
                        System.Threading.Tasks.Task.Run(() => _llm.LoadModelAsync());
                    }
                    else
                    {
                        Info("AI включён. Модель найдена: " + modelPath);
                    }
                }
                return true;
            }

            if (Eq(rest, "off") || Eq(rest, "выкл"))
            {
                _settings.Enabled = false;
                AiSettingsSerializer.Save(_settings);
                Info("AI выключён.");
                return true;
            }

            if (Eq(rest, "status") || Eq(rest, "статус"))
            {
                var mp = _settings.ResolvedModelPath;
                var modelExists = System.IO.File.Exists(mp);
                Info("AI: " + (_settings.Enabled ? "включён" : "выключен"));
                var llmStatus = _llm != null ? _llm.Status.ToString() : "не инициализирована";
                Info("Статус модели: " + llmStatus);
                var localLlm = _llm as Inference.LocalLlmService;
                if (localLlm != null && !string.IsNullOrEmpty(localLlm.LastError))
                    Info("Ошибка: " + localLlm.LastError);
                Info("Модель: " + mp + (modelExists ? " [OK]" : " [ФАЙЛ НЕ НАЙДЕН]"));
                Info("База:   " + _settings.ResolvedDatabasePath);
                Info("Лор:    " + _settings.ResolvedLoreDirectory);
                return true;
            }

            if (Eq(rest, "cancel") || Eq(rest, "отмена"))
            {
                _llm?.CancelCurrent();
                Info("Генерация отменена.");
                return true;
            }

            if (StartsWith(rest, "remember ") || StartsWith(rest, "заметка "))
            {
                var text = StartsWith(rest, "заметка ") ? rest.Substring(8).Trim() : rest.Substring(9).Trim();
                if (_memory != null)
                {
                    long? zoneId = _session.CurrentZoneId > 0 ? (long?)_session.CurrentZoneId : null;
                    long? roomId = _session.CurrentRoomId > 0 ? (long?)_session.CurrentRoomId : null;
                    _memory.SaveUserNote(text, zoneId, roomId);
                }
                Info("Заметка сохранена.");
                return true;
            }

            if (Eq(rest, "мейн") || Eq(rest, "main"))
            {
                _settings.OutputTarget = Configuration.AiOutputTarget.MainWindow;
                AiSettingsSerializer.Save(_settings);
                Info("AI пишет в: главное окно.");
                return true;
            }

            if (Eq(rest, "аутпут1") || Eq(rest, "output1"))
            {
                _settings.OutputTarget = Configuration.AiOutputTarget.AdditionalOutput;
                AiSettingsSerializer.Save(_settings);
                Info("AI пишет в: Additional Output.");
                return true;
            }

            if (Eq(rest, "аутпут2") || Eq(rest, "output2"))
            {
                _settings.OutputTarget = Configuration.AiOutputTarget.AdditionalOutput2;
                AiSettingsSerializer.Save(_settings);
                Info("AI пишет в: Additional Output 2.");
                return true;
            }

            if (Eq(rest, "что знаешь") || Eq(rest, "whatknow"))
            {
                Info("=== Что знает AI ===");
                if (_memory != null)
                {
                    long? zoneId = _session.CurrentZoneId > 0 ? (long?)_session.CurrentZoneId : null;
                    var events = _memory.GetRecentEvents(null, 20);
                    Info("Событий в базе (последние 20 из всех зон):");
                    if (events.Count == 0)
                        Info("  (пусто — поиграй немного чтобы накопились данные)");
                    foreach (var ev in events)
                        Info("  [" + ev.EventType + "] " + ev.RawText);
                    if (zoneId.HasValue)
                    {
                        var summary = _memory.GetZoneSummary(zoneId.Value);
                        if (!string.IsNullOrEmpty(summary))
                        {
                            Info("Сводка текущей зоны:");
                            Info("  " + summary);
                        }
                    }
                }
                else Info("  (память недоступна)");
                return true;
            }

            if (Eq(rest, "lore reindex") || Eq(rest, "лор переиндекс"))
            {
                if (_lore != null)
                    Task.Run(() => _lore.ReindexAll(msg => Info(msg)));
                Info("Переиндексация лора запущена...");
                return true;
            }

            if (StartsWith(rest, "lore search ") || StartsWith(rest, "лор поиск "))
            {
                string lq = StartsWith(rest, "лор поиск ") ? rest.Substring(10).Trim() : rest.Substring(12).Trim();
                if (_lore != null && _memory != null)
                {
                    var chunks = _lore.Search(lq, null, null, 5);
                    Info("Поиск лора: [" + lq + "] -> " + chunks.Count + " чанков");
                    foreach (var ch in chunks)
                        Info("  [" + ch.DocTitle + "/" + ch.SectionTitle + "]: " + ch.Content.Substring(0, Math.Min(80, ch.Content.Length)) + "...");
                    if (chunks.Count == 0)
                        Info("  (ничего не найдено)");
                }
                return true;
            }

            if (Eq(rest, "debug") || Eq(rest, "дебаг"))
            {
                if (string.IsNullOrEmpty(_lastPrompt))
                    Info("(промпт пуст - сначала задай вопрос)");
                else
                {
                    Info("=== Последний промпт ===");
                    var lines = _lastPrompt.Split(new char[]{(char)10}, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                        Info(line.TrimEnd());
                }
                return true;
            }

            // Ask question
            if (_llm == null)
            {
                Info("AI недоступен.");
                return true;
            }

            var question = rest;
            if (_llm.Status == Abstractions.LlmStatus.Loading)
            {
                Info("AI: модель ещё загружается, подожди...");
                return true;
            }
            if (_llm.Status != Abstractions.LlmStatus.Ready)
            {
                Info("AI: модель не готова (статус: " + _llm.Status + ")");
                return true;
            }
            lock (_generateLock)
            {
                if (_generating) { Info("AI: уже думаю, подожди..."); return true; }
                _generating = true;
            }
            Task.Run(async () =>
            {
                try
                {
                    Info("AI: думаю...");
                    string prompt = _contextBuilder != null
                        ? _contextBuilder.BuildPrompt(question, _session)
                        : question;
                    _lastPrompt = prompt;
                    AiLogger.Prompt(prompt);
                    AiLogger.Log("ASK", "Вопрос: " + question);

                    var answer = await _llm.GenerateAsync(prompt).ConfigureAwait(false);
                    var name = string.IsNullOrEmpty(_settings.AssistantName) ? "Лира" : _settings.AssistantName;
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        // Обрезаем по стоп-словам (модель может продолжать диалог сама с собой)
                        string[] stopTokens = { "<|im_end|>", "<|im_start|>", "Пользователь:", "User:", "Вопрос:", "[Вопрос]:", "Human:", "\nЛира:", "\r\nЛира:" };
                        AiLogger.Log("RAW", answer?.Replace("\n"," ") ?? "(null)");
                        string trimmedAnswer = answer;
                        // qwen3 с /no_think выдаёт пустой блок <think></think> в начале — вырезаем
                        int thinkEnd = trimmedAnswer.IndexOf("</think>", System.StringComparison.OrdinalIgnoreCase);
                        if (thinkEnd >= 0) trimmedAnswer = trimmedAnswer.Substring(thinkEnd + 8);
                        foreach (var stop in stopTokens)
                        {
                            int si = trimmedAnswer.IndexOf(stop, System.StringComparison.OrdinalIgnoreCase);
                            if (si > 0) trimmedAnswer = trimmedAnswer.Substring(0, si);
                        }
                        // Убираем самоповтор имени ассистента из ответа модели
                        trimmedAnswer = trimmedAnswer.Trim();
                        if (trimmedAnswer.StartsWith(name + ":", System.StringComparison.OrdinalIgnoreCase))
                            trimmedAnswer = trimmedAnswer.Substring(name.Length + 1).Trim();
                        if (trimmedAnswer.StartsWith("Лира:", System.StringComparison.OrdinalIgnoreCase))
                            trimmedAnswer = trimmedAnswer.Substring(5).Trim();
                        AiLogger.Log("FINAL", trimmedAnswer?.Replace("\n"," ") ?? "(null)");
                        var lines = trimmedAnswer.Split(new char[]{'\n','\r'}, System.StringSplitOptions.RemoveEmptyEntries);
                        var flat = string.Join(" ", System.Array.FindAll(lines, l => !l.Trim().StartsWith("[")));
                        if (string.IsNullOrWhiteSpace(flat)) flat = trimmedAnswer;
                        Output("[" + name + "]: " + flat.Trim());
                    }
                    else
                        Output("[" + name + "]: (нет ответа)");
                }
                catch (Exception ex)
                {
                    Output("AI: Ошибка: " + ex.Message);
                }
                finally
                {
                    _generating = false;
                }
            });

            return true;
        }

        private void PrintHelp()
        {
            Info("#=== Помощник " + _settings.AssistantName + " (локальный AI) ===");
            Info("#  ии <вопрос>           — задать вопрос помощнику");
            Info("#  ии заметка <текст>    — сохранить заметку");
            Info("#  ии статус             — статус и пути к файлам");
            Info("#  ии вкл / ии выкл      — включить / выключить");
            Info("#  ии мейн/аутпут1/аутпут2 — куда писать ответы");
            Info("#  ии отмена             — прервать генерацию");
            Info("#  ии лор переиндекс     — переиндексировать папку лора");
            Info("#  (также работает /ai + английские варианты команд)");
            Info("#Модель: " + _settings.ResolvedModelPath);
            Info("#База:   " + _settings.ResolvedDatabasePath);
            Info("#Лор:    " + _settings.ResolvedLoreDirectory);
        }

        private static bool Eq(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWith(string a, string b)
        {
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
        }

        private void Info(string text)
        {
            _conveyor.PushMessage(MakeMessage(text));
        }

        private void Output(string text)
        {
            _conveyor.PushMessage(MakeMessage(text));
        }

        private Common.Messages.Message MakeMessage(string text)
        {
            switch (_settings.OutputTarget)
            {
                case Configuration.AiOutputTarget.AdditionalOutput:
                    return new OutputToAdditionalWindowMessage(text, TextColor.BrightCyan, TextColor.Black);
                case Configuration.AiOutputTarget.AdditionalOutput2:
                    return new OutputToAdditionalWindow2Message(text, TextColor.BrightCyan, TextColor.Black);
                default:
                    return new OutputToMainWindowMessage(text, TextColor.BrightCyan);
            }
        }
    }
}
