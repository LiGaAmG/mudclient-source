using System.Collections.Generic;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Lore
{
    public class LoreSearchService : ILoreSearchService
    {
        private readonly LoreIndexer _indexer;
        private readonly IGameMemoryService _memory;

        public LoreSearchService(AiSettings settings, IGameMemoryService memory)
        {
            _memory = memory;
            _indexer = new LoreIndexer(settings, memory);
        }

        public void ReindexAll(System.Action<string> onDone = null)
        {
            var result = _indexer.ReindexAll();
            if (onDone != null)
            {
                string msg;
                if (result.Scanned == 0)
                    msg = "Лор: файлов в папке не найдено.";
                else if (result.Updated == 0)
                    msg = string.Format("Лор: {0} файл(ов) в индексе, изменений нет.", result.Scanned);
                else
                    msg = string.Format("Лор: проиндексировано {0} из {1} файл(ов), {2} чанков.", result.Updated, result.Scanned, result.Chunks);
                onDone(msg);
            }
        }

        // Ищем только по тексту вопроса — лор глобальный, не зависит от текущей зоны
        public IList<LoreChunkRecord> Search(string query, string zoneName, string roomName, int limit)
        {
            var results = _memory.SearchLore(query ?? string.Empty, limit);
            if (results.Count == 0)
                AiLogger.Log("LORE", "ничего не найдено по: " + query);
            else
                AiLogger.Log("LORE", string.Format("найдено {0} чанк(ов) по [{1}]: {2}", results.Count, query, results[0].SectionTitle));
            return results;
        }
    }
}
