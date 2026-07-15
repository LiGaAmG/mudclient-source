using System.Text;
using System.Text.RegularExpressions;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Context
{
    public class AiContextBuilder : IAiContextBuilder
    {
        private static readonly Regex MdStrip = new Regex(@"\*{1,2}|_{1,2}|`", System.Text.RegularExpressions.RegexOptions.Compiled);

        private readonly AiSettings _settings;
        private readonly IGameMemoryService _memory;
        private readonly ILoreSearchService _lore;

        public AiContextBuilder(AiSettings settings, IGameMemoryService memory, ILoreSearchService lore)
        {
            _settings = settings;
            _memory = memory;
            _lore = lore;
        }

        public string BuildPrompt(string userQuestion, GameSessionState session)
        {
            // qwen3 обучен на ChatML — сырой текст "Вопрос:/Лира:" модель принимает
            // за сценарий пьесы и дописывает реплики за игрока. ChatML заставляет
            // её остановиться на <|im_end|> после одного ответа.
            var sys = new StringBuilder();
            sys.AppendLine("Ты " + _settings.AssistantName + ", помощник игрока в MUD Adan. Отвечай по-русски, кратко (2-4 предложения), только на основе данных ниже. Не выдумывай.");
            sys.AppendLine("Сленг: \"лут\", \"что падает с X\" — предметы-добыча с монстра X. Строки вида \"предмет — характеристики\" в справке — это добыча.");

            if (!string.IsNullOrEmpty(session.CurrentZoneName))
                sys.AppendLine("Зона: " + session.CurrentZoneName);
            if (!string.IsNullOrEmpty(session.CurrentRoomName))
                sys.AppendLine("Комната: " + session.CurrentRoomName);

            // Что видит игрок (MobSeen, без markdown)
            if (_memory != null)
            {
                long? zoneId = session.CurrentZoneId > 0 ? (long?)session.CurrentZoneId : null;
                var events = _memory.GetRecentEvents(zoneId, 3);
                bool hasEvents = false;
                foreach (var ev in events)
                {
                    if (ev.EventType == GameEventType.MobSeen || ev.EventType == GameEventType.MobKilled || ev.EventType == GameEventType.ItemPickedUp)
                    {
                        if (!hasEvents) { sys.AppendLine("Наблюдения:"); hasEvents = true; }
                        // RawText из игры часто уже начинается с "- " — убираем, чтобы не было "- -"
                        sys.AppendLine("- " + MdStrip.Replace(ev.RawText, "").Trim().TrimStart('-', ' ', '–', '—').Trim());
                    }
                }
            }

            // Лор — полный чанк без обрезки (секционный, умещается)
            if (_lore != null && !string.IsNullOrWhiteSpace(userQuestion))
            {
                var chunks = _lore.Search(userQuestion, session.CurrentZoneName, session.CurrentRoomName, 3);
                if (chunks.Count > 0)
                {
                    sys.AppendLine();
                    sys.AppendLine("Справка:");
                    var seenChunks = new System.Collections.Generic.HashSet<string>();
                    foreach (var ch in chunks)
                    {
                        if (!seenChunks.Add(ch.Content)) continue;
                        if (!string.IsNullOrEmpty(ch.SectionTitle))
                            sys.AppendLine("[" + ch.SectionTitle.Trim() + "]");
                        sys.AppendLine(ch.Content.Trim());
                    }
                }
            }

            var sb = new StringBuilder();
            sb.Append("<|im_start|>system\n");
            sb.Append(sys.ToString().TrimEnd());
            sb.Append("\n<|im_end|>\n");
            sb.Append("<|im_start|>user\n");
            sb.Append(userQuestion);
            sb.Append(" /no_think\n<|im_end|>\n");
            sb.Append("<|im_start|>assistant\n");

            return sb.ToString();
        }
    }
}
