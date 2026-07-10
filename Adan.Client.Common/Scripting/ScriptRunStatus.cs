namespace Adan.Client.Common.Scripting
{
    /// <summary>
    /// Lifecycle state of one named coroutine script managed by
    /// LuaScriptHost's scheduler.
    /// </summary>
    public enum ScriptRunStatus
    {
        /// <summary>Never started, or explicitly stopped.</summary>
        NotRunning,

        /// <summary>Currently executing (between yields) -- only true
        /// transiently during Resume; scripts spend almost all their time
        /// in one of the Waiting* states instead.</summary>
        Running,

        /// <summary>Suspended inside Wait(ms), waiting for the deadline.</summary>
        WaitingOnTimer,

        /// <summary>Suspended inside WaitGroupState().</summary>
        WaitingOnGroupState,

        /// <summary>Suspended inside WaitRoomState().</summary>
        WaitingOnRoomState,

        /// <summary>Suspended inside WaitRoomChange().</summary>
        WaitingOnRoomChange,

        /// <summary>Suspended inside WaitText(), waiting for the next text line from the server.</summary>
        WaitingOnText,

        /// <summary>The coroutine's chunk ran to completion (returned
        /// without yielding again) -- a one-shot script, not an error.</summary>
        Finished,

        /// <summary>Resume returned an error status (syntax/runtime error,
        /// or the instruction-budget watchdog tripped). The script is
        /// dead and won't be resumed again; re-Start it to retry.</summary>
        Faulted,
    }
}
