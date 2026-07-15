using System;
using Adan.Client.Plugins.AI.Events;

namespace Adan.Client.Plugins.AI.Memory
{
    public class GameEventRecord
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public GameEventType EventType { get; set; }
        public long? ZoneId { get; set; }
        public long? RoomId { get; set; }
        public string RawText { get; set; }
        public string StructuredDataJson { get; set; }
        public int Importance { get; set; }
    }
}
