using System;
using System.Collections.Generic;
using Adan.Client.Plugins.AI.Events;

namespace Adan.Client.Plugins.AI.Memory
{
    public class GameSessionState
    {
        public string CurrentZoneName { get; set; }
        public long CurrentZoneId { get; set; }
        public string CurrentRoomName { get; set; }
        public long CurrentRoomId { get; set; }
        public string LastPlayerCommand { get; set; }
        public List<string> VisibleMobs { get; set; }
        public List<string> VisibleItems { get; set; }
        public bool InCombat { get; set; }
        public int? HealthPercent { get; set; }
        public List<GameEvent> RecentImportantEvents { get; set; }
        public List<string> RecentLines { get; set; }
        public DateTime LastCommentaryAt { get; set; }
        public List<long> RecentRoomPath { get; set; }

        public GameSessionState()
        {
            CurrentZoneName = string.Empty;
            CurrentRoomName = string.Empty;
            LastPlayerCommand = string.Empty;
            VisibleMobs = new List<string>();
            VisibleItems = new List<string>();
            RecentImportantEvents = new List<GameEvent>();
            RecentLines = new List<string>();
            LastCommentaryAt = DateTime.MinValue;
            RecentRoomPath = new List<long>();
        }
    }
}
