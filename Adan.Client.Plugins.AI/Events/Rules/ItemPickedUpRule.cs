using System.Text.RegularExpressions;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events.Rules
{
    public class ItemPickedUpRule : IGameEventRule
    {
        private static readonly Regex Pattern = new Regex(
            @"(?:Вы взяли|подобрал[аи]?|взял[аи]?)\s+(.+?)[\.,]?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public int Priority { get { return 7; } }

        public bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent)
        {
            gameEvent = null;
            var m = Pattern.Match((line ?? string.Empty).Trim());
            if (!m.Success) return false;
            gameEvent = new GameEvent
            {
                Type = GameEventType.ItemPickedUp,
                EntityName = m.Groups[1].Value.Trim(),
                RawText = line,
                Importance = 3
            };
            return true;
        }
    }
}