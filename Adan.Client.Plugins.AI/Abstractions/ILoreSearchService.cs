using System.Collections.Generic;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Abstractions
{
    public interface ILoreSearchService
    {
        void ReindexAll(System.Action<string> onDone = null);
        IList<LoreChunkRecord> Search(string query, string zoneName, string roomName, int limit);
    }
}
