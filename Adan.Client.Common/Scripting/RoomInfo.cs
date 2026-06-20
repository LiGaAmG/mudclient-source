namespace Adan.Client.Common.Scripting
{
    using System.Collections.Generic;

    /// <summary>
    /// Plain-data snapshot of everything the client already knows about a
    /// room/zone LOCALLY (from the loaded zone XML files), as opposed to
    /// the two raw ids the server's CurrentRoomMessage packet (type 14)
    /// actually carries. Built by ZoneHolder (Adan.Client.Map) and passed
    /// to <see cref="LuaScriptHost.RaiseRoomChanged"/> -- this class lives
    /// in Adan.Client.Common (not Adan.Client.Map) so LuaScriptHost doesn't
    /// need a dependency on the Map plugin's Room/Zone model types.
    /// Null fields/lists mean "not known" (e.g. an unmapped room), not an
    /// error -- scripts should check for null/empty before using them.
    /// </summary>
    public class RoomInfo
    {
        public RoomInfo()
        {
            ZoneName = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            Exits = new List<RoomExitInfo>();
            Alias = string.Empty;
            Comments = string.Empty;
            HerbDangerLevel = string.Empty;
        }

        public string ZoneName { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        public int Z { get; set; }

        public List<RoomExitInfo> Exits { get; private set; }

        public string Alias { get; set; }

        public string Comments { get; set; }

        public bool HasBeenVisited { get; set; }

        public bool HasHerb { get; set; }

        public string HerbDangerLevel { get; set; }
    }

    /// <summary>
    /// One exit out of a <see cref="RoomInfo"/> room.
    /// </summary>
    public class RoomExitInfo
    {
        public string Direction { get; set; }

        public int RoomId { get; set; }
    }
}
