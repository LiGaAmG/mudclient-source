using System.Text.RegularExpressions;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events.Rules
{
    public class MobSeenRule : IGameEventRule
    {
        private static readonly Regex Pattern = new Regex(
            @"(?:Здесь|Тут)\s+(?:стоит|бродит|лежит|сидит|находится|ходит)\s+(.+?)[\.,]?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public int Priority { get { return 9; } }

        public bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent)
        {
            gameEvent = null;
            var m = Pattern.Match((line ?? string.Empty).Trim());
            if (!m.Success) return false;
            gameEvent = new GameEvent
            {
                Type = GameEventType.MobSeen,
                EntityName = m.Groups[1].Value.Trim(),
                RawText = line,
                Importance = 2
            };
            return true;
        }
    }
}