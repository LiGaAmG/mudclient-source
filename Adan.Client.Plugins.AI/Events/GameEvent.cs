using System;

namespace Adan.Client.Plugins.AI.Events
{
    public class GameEvent
    {
        public GameEventType Type { get; set; }
        public string RawText { get; set; }
        public string EntityName { get; set; }
        public string Direction { get; set; }
        public int Importance { get; set; }
        public DateTime Timestamp { get; set; }

        public GameEvent() { Timestamp = DateTime.UtcNow; }
    }
}
