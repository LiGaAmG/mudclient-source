using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Abstractions
{
    public interface IAiCommentaryService
    {
        bool IsEnabled { get; set; }
        void OnGameEvent(GameEvent ev, GameSessionState session);
    }
}
