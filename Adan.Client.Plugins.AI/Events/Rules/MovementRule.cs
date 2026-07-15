using System.Collections.Generic;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events.Rules
{
    public class MovementRule : IGameEventRule
    {
        private static readonly HashSet<string> Directions = new HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase)
        {
            "n","north","север","с",
            "s","south","юг","ю",
            "e","east","восток","в",
            "w","west","запад","з",
            "u","up","вверх","вв",
            "d","down","вниз","вн",
            "ne","nw","se","sw","св","сз","юв","юз"
        };

        public int Priority { get { return 5; } }

        public bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent)
        {
            gameEvent = null;
            string cmd = (state.LastPlayerCommand ?? string.Empty).Trim().ToLowerInvariant();
            if (!Directions.Contains(cmd)) return false;
            gameEvent = new GameEvent
            {
                Type = GameEventType.PlayerMoved,
                Direction = cmd,
                RawText = line,
                Importance = 1
            };
            return true;
        }
    }
}
