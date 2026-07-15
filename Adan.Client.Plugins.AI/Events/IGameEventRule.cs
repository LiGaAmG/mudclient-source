using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events
{
    public interface IGameEventRule
    {
        int Priority { get; }
        bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent);
    }
}
