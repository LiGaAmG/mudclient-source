using System.Text.RegularExpressions;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events.Rules
{
    public class MobKilledRule : IGameEventRule
    {
        private static readonly Regex Pattern = new Regex(
            @"^(.+?)\s+(?:убит[аo]?|мертв[аo]?|повержен[аo]?|пал[аo]?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public int Priority { get { return 8; } }

        public bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent)
        {
            gameEvent = null;
            var m = Pattern.Match((line ?? string.Empty).Trim());
            if (!m.Success) return false;
            gameEvent = new GameEvent
            {
                Type = GameEventType.MobKilled,
                EntityName = m.Groups[1].Value.Trim(),
                RawText = line,
                Importance = 4
            };
            return true;
        }
    }
}