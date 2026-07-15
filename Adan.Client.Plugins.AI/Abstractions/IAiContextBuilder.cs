using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Abstractions
{
    public interface IAiContextBuilder
    {
        string BuildPrompt(string userQuestion, GameSessionState session);
    }
}
