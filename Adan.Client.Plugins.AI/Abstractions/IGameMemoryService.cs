using System;
using System.Collections.Generic;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Abstractions
{
    public interface IGameMemoryService : IDisposable
    {
        void Initialize();
        long UpsertZone(string name);
        long UpsertRoom(long zoneId, string name, string description);
        void ConfirmExit(long fromRoomId, string direction, long toRoomId);
        IList<RoomRecord> GetRoomsInZone(long zoneId);
        IList<ExitRecord> GetExitsFromRoom(long roomId);
        IList<long> FindShortestPath(long fromRoomId, long toRoomId);
        long UpsertMob(string name, string description);
        long UpsertItem(string name, string description);
        void RecordMobInRoom(long roomId, long mobId);
        void RecordItemInRoom(long roomId, long itemId);
        void SaveEvent(GameEventRecord ev);
        IList<GameEventRecord> GetRecentEvents(long? zoneId, int limit);
        void SaveUserNote(string text, long? zoneId, long? roomId);
        string GetZoneSummary(long zoneId);
        void SaveZoneSummary(long zoneId, string summary);
        void SaveLoreDocument(string path, string title, long contentHash);
        bool LoreDocumentChanged(string path, long contentHash);
        void SaveLoreChunk(string docPath, int chunkIndex, string content, string sectionTitle);
        IList<LoreChunkRecord> SearchLore(string query, int limit);
    }
}
