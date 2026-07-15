namespace Adan.Client.Plugins.AI.Abstractions
{
    public interface IAiCommandHandler
    {
        bool TryHandle(string commandText);
    }
}
