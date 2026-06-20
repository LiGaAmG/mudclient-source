namespace Adan.Client.Common.Model
{
    /// <summary>
    /// Which LuaScriptHost packet-state event a ScriptDefinition's code
    /// should be registered against, if any.
    /// </summary>
    public enum ScriptHandlerKind
    {
        /// <summary>
        /// Plain script with no registered handler -- its top-level code
        /// runs once at profile-load time and nothing else.
        /// </summary>
        None,

        /// <summary>
        /// Script must define a function named exactly "on_group_state",
        /// registered via LuaScriptHost.RegisterGroupStateHandler.
        /// </summary>
        GroupState,

        /// <summary>
        /// Script must define a function named exactly "on_room_state",
        /// registered via LuaScriptHost.RegisterRoomStateHandler.
        /// </summary>
        RoomState
    }
}
