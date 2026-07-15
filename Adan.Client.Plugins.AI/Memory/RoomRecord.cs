using System;

namespace Adan.Client.Plugins.AI.Memory
{
    public class RoomRecord
    {
        public long Id { get; set; }
        public long ZoneId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int VisitCount { get; set; }
        public DateTime LastSeenAt { get; set; }
        public string Notes { get; set; }
    }
}
