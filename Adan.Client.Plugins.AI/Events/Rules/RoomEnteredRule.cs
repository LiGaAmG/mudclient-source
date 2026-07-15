using System.Text.RegularExpressions;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events.Rules
{
    public class RoomEnteredRule : IGameEventRule
    {
        // Название комнаты: короткое (до 60 символов), без точек в конце, без глаголов
        private static readonly Regex Pattern = new Regex(
            @"^[А-ЯЁ][А-ЯЁа-яё0-9\s\-]{2,55}$",
            RegexOptions.Compiled);

        // Фразы которые ТОЧНО не являются названиями комнат
        private static readonly string[] Blacklist = {
            "Вы ", "вы ", "Welcome", "INFO", "Модуль", "Лора", "Лор:", "AI:",
            "[Лира]", "===", "Переиндекс", "помощник", "MUD-игре", "тебя зовут",
            "нет больше", "нет места", "включён", "запущена", "статистик",
            "AdamantMUD", "сбора", "локальный", "текстов", "зовут"
        };

        public int Priority { get { return 10; } }

        public bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent)
        {
            gameEvent = null;
            string trimmed = (line ?? string.Empty).Trim();
            // Название комнаты — короткое, без знаков препинания в конце
            if (trimmed.Length < 3 || trimmed.Length > 60) return false;
            if (trimmed.EndsWith(".") || trimmed.EndsWith("!") || trimmed.EndsWith("?") || trimmed.EndsWith(",")) return false;
            if (!Pattern.IsMatch(trimmed)) return false;

            foreach (var bad in Blacklist)
                if (trimmed.IndexOf(bad, System.StringComparison.OrdinalIgnoreCase) >= 0) return false;

            // Только кириллица (русские названия комнат)
            bool hasCyrillic = false;
            foreach (char c in trimmed)
                if (c >= 'А' && c <= 'я' || c == 'ё' || c == 'Ё') { hasCyrillic = true; break; }
            if (!hasCyrillic) return false;

            gameEvent = new GameEvent
            {
                Type = GameEventType.RoomEntered,
                EntityName = trimmed,
                RawText = line,
                Importance = 3
            };
            return true;
        }
    }
}