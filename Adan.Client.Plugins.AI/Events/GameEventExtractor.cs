using System.Collections.Generic;
using System.Linq;
using Adan.Client.Plugins.AI.Events.Rules;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events
{
    public class GameEventExtractor
    {
        private readonly IList<IGameEventRule> _rules;

        public GameEventExtractor() : this(CreateDefaultRules()) { }

        public GameEventExtractor(IList<IGameEventRule> rules)
        {
            _rules = rules.OrderBy(r => r.Priority).ToList();
        }

        public GameEvent TryExtract(string line, GameSessionState state)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            foreach (var rule in _rules)
            {
                GameEvent ev;
                if (rule.TryMatch(line, state, out ev))
                    return ev;
            }
            return null;
        }

        private static IList<IGameEventRule> CreateDefaultRules()
        {
            return new List<IGameEventRule>
            {
                new MovementRule(),
                new RoomEnteredRule(),
                new MobKilledRule(),
                new MobSeenRule(),
                new ItemPickedUpRule(),
            };
        }
    }
}
