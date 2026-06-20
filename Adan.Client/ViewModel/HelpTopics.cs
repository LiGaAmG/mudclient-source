namespace Adan.Client.ViewModel
{
    using System.Collections.Generic;

    public static class HelpTopics
    {
        public static List<HelpTopic> All = new List<HelpTopic>
        {
            new HelpTopic(
                "Overview",
                "Every tab has one persistent, sandboxed Lua state (LuaScriptHost). " +
                "Scripts attached to a trigger/alias action (\"Run Lua script\") run in " +
                "this same state, sharing variables with scripts in the Scripts dialog " +
                "for the same tab. A script attached to a trigger runs every time that " +
                "trigger fires. A script in the Scripts dialog with a Handler set to " +
                "GroupState or RoomState runs its on_group_state/on_room_state function " +
                "every time the server sends that kind of packet -- no text parsing " +
                "involved, the data comes straight from the server's structured packet."),

            new HelpTopic(
                "Events: on_group_state(group)",
                "Set Handler = GroupState in the Scripts dialog and define exactly:\n\n" +
                "function on_group_state(group)\n" +
                "  for i = 1, #group do\n" +
                "    local member = group[i]\n" +
                "    -- member.Name, member.HitsPercent\n" +
                "  end\n" +
                "end\n\n" +
                "Called every time the server sends a group-status packet (type 12). " +
                "group is a 1-indexed Lua table; each entry currently exposes only " +
                "Name (string) and HitsPercent (number, 0-100). More CharacterStatus " +
                "fields (Position, IsAttacked, Affects, etc.) are not exposed yet."),

            new HelpTopic(
                "Events: on_room_state(monsters)",
                "Set Handler = RoomState in the Scripts dialog and define exactly:\n\n" +
                "function on_room_state(monsters)\n" +
                "  for i = 1, #monsters do\n" +
                "    local m = monsters[i]\n" +
                "    -- m.Name, m.HitsPercent\n" +
                "  end\n" +
                "end\n\n" +
                "Called every time the server sends a room-monsters packet (type 13), " +
                "roughly once per combat round. Same field limitations as group: only " +
                "Name and HitsPercent today."),

            new HelpTopic(
                "Functions: SendCommand(text)",
                "SendCommand(\"атаковать крысу\")\n\n" +
                "Sends a text command to the server, exactly as if you typed it. " +
                "Works the same from a trigger-attached script or a Scripts-dialog " +
                "script."),

            new HelpTopic(
                "Sandbox restrictions",
                "Only these globals are available: string, table, math, tostring, " +
                "tonumber, type, pairs, ipairs, select, error, pcall, xpcall, assert, " +
                "print, plus the functions documented here (SendCommand). io, os, " +
                "package, require, debug, dofile, loadfile, load, getmetatable, and " +
                "setmetatable are all removed and cannot be reintroduced from a " +
                "script. There is no filesystem, network, or process access from Lua " +
                "at all -- by design."),

            new HelpTopic(
                "Runaway scripts",
                "Every script call is limited to roughly 1,000,000 Lua VM " +
                "instructions. A script that loops forever (while true do end) is " +
                "killed automatically -- you'll see an error message instead of a " +
                "frozen tab. This applies even if the loop is wrapped in pcall."),

            new HelpTopic(
                "Known limitation: one script per handler kind",
                "If you enable two different scripts that both have Handler = " +
                "GroupState, only the last one (in list order) actually keeps its " +
                "on_group_state definition -- the second LoadScript call silently " +
                "redefines the function from the first. Keep at most one enabled " +
                "GroupState script and one enabled RoomState script per profile " +
                "until this is fixed in a future version."),
        };
    }
}
