namespace Adan.Client.Common.Scripting
{
    using System;
    using System.Collections.Generic;
    using Adan.Client.Common.Model;
    using KeraLua;
    using NLua;

    /// <summary>
    /// One persistent, sandboxed Lua state per tab (per RootModel). Created
    /// once when the tab opens, disposed once when the tab closes. Scripts
    /// attached to triggers/aliases and packet-state handlers registered
    /// for this tab all run inside this single state, so their variables
    /// do not leak across tabs.
    /// </summary>
    public sealed partial class LuaScriptHost : IDisposable
    {
        // 1,000,000 VM instructions is comfortably more than any real
        // trigger/state-handler script needs per invocation (a handler
        // iterating a 50-member group with a few comparisons is in the
        // low thousands), and low enough that a runaway loop is killed
        // in well under 50ms on typical hardware.
        private const int InstructionBudget = 1_000_000;

        // Once the budget has been exceeded once during an Eval call, the
        // hook re-arms itself with this much smaller count so that even if
        // a pcall in the script swallows the first error and execution
        // continues, the very next handful of instructions re-triggers the
        // hook instead of giving the script another full budget window.
        private const int ShrunkInstructionInterval = 100;

        private readonly NLua.Lua _lua;
        private readonly KeraLua.LuaHookFunction _hookFunction;
        private bool _disposed;
        private bool _budgetExceeded;

        // Set to true the first time a single Eval call exceeds its
        // instruction budget, and left true for the remainder of that Eval
        // call. While true, the hook unconditionally re-raises an error on
        // every subsequent tick -- regardless of how many pcall/xpcall
        // frames the script uses to catch and "continue past" the previous
        // error -- so the entire Eval call is guaranteed to terminate
        // rather than just the innermost protected frame.
        private bool _timeoutRequested;

        private readonly Action<string> _sendCommand;
        private string _groupStateHandlerName;
        private string _roomStateHandlerName;
        private string _roomChangeHandlerName;

        private readonly Dictionary<string, RunningScript> _runningScripts = new Dictionary<string, RunningScript>();

        private sealed class RunningScript
        {
            public KeraLua.Lua Thread;
            public ScriptRunStatus Status;
            public DateTime TimerDueAtUtc;
        }

        public LuaScriptHost()
            : this(null)
        {
        }

        public LuaScriptHost(Action<string> sendCommand)
        {
            _sendCommand = sendCommand ?? (_ => { });

            _lua = CreateSandboxedState();

            // Exposed to the sandbox's pcall/xpcall guard shim (see
            // LuaScriptHost.SandboxSetup.cs) so script-level pcalls can
            // detect a watchdog trip and re-raise instead of swallowing it.
            _lua.RegisterFunction("__watchdog_timeout", this, GetType().GetMethod(nameof(IsTimeoutRequested)));
            InstallPcallGuard();

            // Registered AFTER CreateSandboxedState's allowlist sweep has
            // already run (the sweep only happens once, inside
            // CreateSandboxedState, before this point), so "SendCommand"
            // is added back in as a global that survives -- the sweep
            // never runs again to strip it.
            _lua.RegisterFunction("SendCommand", this, GetType().GetMethod(nameof(SendCommandFromLua)));

            // Keep a reference to the delegate for the lifetime of the host:
            // KeraLua's SetHook stores a native callback pointer derived from
            // this delegate, and if the delegate were only a transient
            // lambda the GC could collect it while native code still held
            // the pointer.
            _hookFunction = OnInstructionHook;
            _lua.State.SetHook(_hookFunction, LuaHookMask.Count, InstructionBudget);

            // Shared by every coroutine script (they all see the same globals,
            // since Lua coroutines created via lua_newthread share the creating
            // state's _G by default). Wait(ms) yields with a "timer" tag + the
            // requested delay; Tick() reads that delay off the yielding thread's
            // own stack (no cross-thread value passing needed) and decides when
            // to resume. WaitGroupState/WaitRoomState/WaitRoomChange are added in
            // a later task.
            _lua.DoString(@"
                function Wait(ms)
                    coroutine.yield('timer', ms)
                end
            ", "scripting-runtime-prelude");
        }

        /// <summary>
        /// Exposed to Lua as <c>__watchdog_timeout</c> for the sandbox's
        /// pcall/xpcall guard shim. Not intended to be called from C#.
        /// </summary>
        public bool IsTimeoutRequested()
        {
            return _timeoutRequested;
        }

        private void OnInstructionHook(IntPtr luaStatePtr, IntPtr ar)
        {
            var state = KeraLua.Lua.FromIntPtr(luaStatePtr);

            if (_timeoutRequested)
            {
                // A timeout was already requested earlier in this Eval call.
                // A pcall somewhere up the stack may have caught the
                // previous error and let the script keep running, but the
                // whole Eval call must still terminate -- so re-raise
                // unconditionally on every tick, regardless of how many
                // times this has already fired. The hook is already armed
                // with the small interval (see below), so this fires again
                // within a few hundred instructions if caught again. The
                // pcall/xpcall guard shim in the sandbox additionally
                // re-raises after any script-level pcall returns while this
                // flag is set, so even an unbounded chain of fresh pcalls
                // the script creates cannot outrun the watchdog.
                state.Error("script exceeded instruction budget", Array.Empty<object>());
                return;
            }

            _budgetExceeded = true;
            _timeoutRequested = true;

            // Shrink the hook interval so that if a pcall in the script
            // catches this error and execution continues, the next trip
            // happens almost immediately instead of after another full
            // InstructionBudget window -- bounding the "damage per pcall"
            // to a small, fixed number of instructions.
            state.SetHook(_hookFunction, LuaHookMask.Count, ShrunkInstructionInterval);

            // Resolve the KeraLua wrapper for the state pointer the hook
            // fired on, then raise a Lua error from inside the hook; NLua
            // surfaces this as a LuaScriptException at the DoString call.
            state.Error("script exceeded instruction budget", Array.Empty<object>());
        }

        /// <summary>
        /// Evaluates a Lua expression of the form "return ...." and returns
        /// the first result. Intended for tests and simple host-side checks;
        /// production script entry points are the RegisterXxxHandler methods
        /// added in later tasks.
        /// </summary>
        /// <exception cref="NLua.Exceptions.LuaScriptException">
        /// Thrown when <paramref name="luaExpression"/> has invalid syntax or
        /// raises a runtime error during execution.
        /// </exception>
        /// <exception cref="LuaScriptTimeoutException">
        /// Thrown when the script exceeds the instruction budget and is aborted.
        /// </exception>
        public object Eval(string luaExpression)
        {
            return RunProtected(() => _lua.DoString(luaExpression));
        }

        /// <summary>
        /// Compiles and runs top-level Lua source (e.g. function definitions)
        /// into this host's persistent state, so the defined functions are
        /// available for later calls such as the RegisterXxxHandler /
        /// RaiseXxxChanged methods below. Routed through the same watchdog
        /// protection as <see cref="Eval"/>.
        /// </summary>
        public void LoadScript(string luaSource)
        {
            RunProtected(() => _lua.DoString(luaSource));
        }

        /// <summary>
        /// Starts (or restarts, if already running/finished/faulted under this
        /// name) a named coroutine script. The chunk runs immediately up to its
        /// first yield (Wait, or a future WaitXxxState) or to completion -- this
        /// call is synchronous for that first leg, same watchdog budget as Eval.
        /// </summary>
        public void StartScript(string name, string code)
        {
            StopScript(name);

            var thread = _lua.State.NewThread();
            thread.SetHook(_hookFunction, LuaHookMask.Count, InstructionBudget);

            var loadStatus = thread.LoadString(code, name);
            if (loadStatus != LuaStatus.OK)
            {
                _runningScripts[name] = new RunningScript { Thread = thread, Status = ScriptRunStatus.Faulted };
                return;
            }

            var script = new RunningScript { Thread = thread, Status = ScriptRunStatus.Running };
            _runningScripts[name] = script;
            ResumeScript(name, script);
        }

        /// <summary>
        /// Stops a running script -- it will not be resumed again. The
        /// coroutine itself is simply dropped (no cooperative cleanup/finally
        /// support in this version); its Lua thread becomes garbage.
        /// </summary>
        public void StopScript(string name)
        {
            _runningScripts.Remove(name);
        }

        /// <summary>
        /// Current lifecycle state of a named script, or NotRunning if it was
        /// never started, was stopped, or the name is unknown.
        /// </summary>
        public ScriptRunStatus GetScriptStatus(string name)
        {
            RunningScript script;
            return _runningScripts.TryGetValue(name, out script) ? script.Status : ScriptRunStatus.NotRunning;
        }

        /// <summary>
        /// Resumes every script whose Wait(ms) deadline has passed. Call this
        /// periodically (e.g. every 100-200ms from a UI timer).
        /// </summary>
        public void Tick()
        {
            var now = DateTime.UtcNow;
            foreach (var pair in new List<KeyValuePair<string, RunningScript>>(_runningScripts))
            {
                if (pair.Value.Status == ScriptRunStatus.WaitingOnTimer && pair.Value.TimerDueAtUtc <= now)
                {
                    ResumeScript(pair.Key, pair.Value);
                }
            }
        }

        /// <summary>
        /// Resumes the given script's coroutine with zero arguments. Inspects
        /// the result: OK means the chunk finished; Yield means it's suspended
        /// again (tag read off the thread's own stack decides which Waiting*
        /// state it's now in); any error status means Faulted.
        /// </summary>
        private void ResumeScript(string name, RunningScript script)
        {
            _budgetExceeded = false;
            _timeoutRequested = false;

            int resultCount;
            var status = script.Thread.Resume(_lua.State, 0, out resultCount);

            if (status == LuaStatus.OK)
            {
                script.Status = ScriptRunStatus.Finished;
                return;
            }

            if (status != LuaStatus.Yield)
            {
                script.Status = ScriptRunStatus.Faulted;
                return;
            }

            var tag = script.Thread.ToString(1);
            if (tag == "timer")
            {
                var delayMs = script.Thread.ToNumber(2);
                script.Status = ScriptRunStatus.WaitingOnTimer;
                script.TimerDueAtUtc = DateTime.UtcNow.AddMilliseconds(delayMs);
            }
            else
            {
                // Unknown yield tag (event-based tags like "group"/"room"/
                // "roomchange" are added in a later task) -- treat as an inert
                // suspended state so GetScriptStatus doesn't crash, but nothing
                // will auto-resume it yet.
                script.Status = ScriptRunStatus.WaitingOnTimer;
                script.TimerDueAtUtc = DateTime.MaxValue;
            }

            script.Thread.Pop(script.Thread.GetTop());
        }

        /// <summary>
        /// Exposed to Lua as the global function <c>SendCommand</c>.
        /// Invokes the delegate supplied to the
        /// <see cref="LuaScriptHost(Action{string})"/> constructor, or a
        /// no-op if the host was constructed with the parameterless
        /// constructor (e.g. design-time/empty RootModel instances with no
        /// live network connection).
        /// </summary>
        public void SendCommandFromLua(string command)
        {
            _sendCommand(command);
        }

        /// <summary>
        /// Registers the name of a Lua function (already defined in this
        /// host's state, e.g. via <see cref="LoadScript"/>) to be invoked by
        /// <see cref="RaiseGroupStateChanged"/> whenever the player's group
        /// state changes.
        /// </summary>
        public void RegisterGroupStateHandler(string luaFunctionName)
        {
            _groupStateHandlerName = luaFunctionName;
        }

        /// <summary>
        /// Registers the name of a Lua function (already defined in this
        /// host's state, e.g. via <see cref="LoadScript"/>) to be invoked by
        /// <see cref="RaiseRoomStateChanged"/> whenever the contents of the
        /// player's current room change.
        /// </summary>
        public void RegisterRoomStateHandler(string luaFunctionName)
        {
            _roomStateHandlerName = luaFunctionName;
        }

        /// <summary>
        /// Registers the name of a Lua function (already defined in this
        /// host's state, e.g. via <see cref="LoadScript"/>) to be invoked by
        /// <see cref="RaiseRoomChanged"/> whenever the server confirms the
        /// player has moved to a new room (the type-14 packet).
        /// </summary>
        public void RegisterRoomChangeHandler(string luaFunctionName)
        {
            _roomChangeHandlerName = luaFunctionName;
        }

        /// <summary>
        /// Invokes the registered group-state handler (see
        /// <see cref="RegisterGroupStateHandler"/>), if any, passing the
        /// supplied group members as a 1-based Lua array of tables built by
        /// <see cref="BuildCharacterTable"/> (every CharacterStatus field).
        /// A no-op if no handler has been registered. Routed through the
        /// same watchdog protection as <see cref="Eval"/> so a runaway
        /// handler is just as bounded as a runaway top-level script.
        /// </summary>
        public void RaiseGroupStateChanged(List<CharacterStatus> group)
        {
            if (group == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(_groupStateHandlerName))
            {
                return;
            }

            var function = _lua.GetFunction(_groupStateHandlerName);
            if (function == null)
            {
                return;
            }

            var groupTable = NewLuaTable();
            for (var i = 0; i < group.Count; i++)
            {
                groupTable[i + 1] = BuildCharacterTable(group[i]);
            }

            RunProtected(() => function.Call(groupTable));
        }

        /// <summary>
        /// Invokes the registered room-state handler (see
        /// <see cref="RegisterRoomStateHandler"/>), if any, passing the
        /// supplied monsters as a 1-based Lua array of tables built by
        /// <see cref="BuildCharacterTable"/> (every CharacterStatus field,
        /// plus MonsterStatus's own IsPlayerCharacter/IsBoss). A no-op if no
        /// handler has been registered. Routed through the same watchdog
        /// protection as <see cref="Eval"/> so a runaway handler is just as
        /// bounded as a runaway top-level script.
        /// </summary>
        public void RaiseRoomStateChanged(List<MonsterStatus> monsters)
        {
            if (monsters == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(_roomStateHandlerName))
            {
                return;
            }

            var function = _lua.GetFunction(_roomStateHandlerName);
            if (function == null)
            {
                return;
            }

            var monstersTable = NewLuaTable();
            for (var i = 0; i < monsters.Count; i++)
            {
                var monsterTable = BuildCharacterTable(monsters[i]);
                monsterTable["IsPlayerCharacter"] = monsters[i].IsPlayerCharacter;
                monsterTable["IsBoss"] = monsters[i].IsBoss;
                monstersTable[i + 1] = monsterTable;
            }

            RunProtected(() => function.Call(monstersTable));
        }

        /// <summary>
        /// Creates a fresh, empty Lua table. NLua has no direct "new table"
        /// API on <see cref="NLua.Lua"/> other than running a chunk that
        /// returns one.
        /// </summary>
        private LuaTable NewLuaTable()
        {
            return (LuaTable)_lua.DoString("return {}")[0];
        }

        /// <summary>
        /// Builds a Lua table exposing every <see cref="CharacterStatus"/>
        /// field -- shared by <see cref="RaiseGroupStateChanged"/> (group
        /// members, plain CharacterStatus) and
        /// <see cref="RaiseRoomStateChanged"/> (monsters, which add their
        /// own IsPlayerCharacter/IsBoss on top of this). <c>Position</c> is
        /// exposed as its string name (e.g. "Standing"), not a raw enum
        /// int, since Lua has no enum type. <c>Affects</c> is a nested
        /// 1-based array of tables with Name/Duration/Rounds.
        /// </summary>
        private LuaTable BuildCharacterTable(CharacterStatus character)
        {
            var table = NewLuaTable();
            table["Name"] = character.Name;
            table["TargetName"] = character.TargetName;
            table["Position"] = character.Position.ToString();
            table["InSameRoom"] = character.InSameRoom;
            table["IsAttacked"] = character.IsAttacked;
            table["HitsPercent"] = character.HitsPercent;
            table["MovesPercent"] = character.MovesPercent;
            table["MemTime"] = character.MemTime;
            table["WaitState"] = character.WaitState;

            var affectsTable = NewLuaTable();
            for (var i = 0; i < character.Affects.Count; i++)
            {
                var affectTable = NewLuaTable();
                affectTable["Name"] = character.Affects[i].Name;
                affectTable["Duration"] = character.Affects[i].Duration;
                affectTable["Rounds"] = character.Affects[i].Rounds;
                affectsTable[i + 1] = affectTable;
            }

            table["Affects"] = affectsTable;

            return table;
        }

        /// <summary>
        /// Invokes the registered room-change handler (see
        /// <see cref="RegisterRoomChangeHandler"/>), if any, passing
        /// roomId/zoneId (the only fields the server's CurrentRoomMessage
        /// packet itself carries) as two plain Lua numbers, plus a third
        /// table argument built from <paramref name="roomInfo"/> -- data
        /// the CLIENT already has locally from its own loaded zone files
        /// (name, description, coordinates, exits, user annotations), not
        /// from the server packet. <paramref name="roomInfo"/> may be null
        /// (room not present in the local map) -- the third argument is
        /// then Lua nil, not an empty table, so scripts can tell "unmapped"
        /// apart from "mapped but everything blank". Existing scripts
        /// written as <c>function on_room_change(roomId, zoneId)</c> are
        /// unaffected -- Lua ignores extra arguments a function doesn't
        /// declare. A no-op if no handler has been registered. Routed
        /// through the same watchdog protection as <see cref="Eval"/>.
        /// </summary>
        public void RaiseRoomChanged(int roomId, int zoneId, RoomInfo roomInfo)
        {
            if (string.IsNullOrEmpty(_roomChangeHandlerName))
            {
                return;
            }

            var function = _lua.GetFunction(_roomChangeHandlerName);
            if (function == null)
            {
                return;
            }

            object roomTable = null;
            if (roomInfo != null)
            {
                var table = NewLuaTable();
                table["ZoneName"] = roomInfo.ZoneName;
                table["Name"] = roomInfo.Name;
                table["Description"] = roomInfo.Description;
                table["X"] = roomInfo.X;
                table["Y"] = roomInfo.Y;
                table["Z"] = roomInfo.Z;
                table["Alias"] = roomInfo.Alias;
                table["Comments"] = roomInfo.Comments;
                table["HasBeenVisited"] = roomInfo.HasBeenVisited;
                table["HasHerb"] = roomInfo.HasHerb;
                table["HerbDangerLevel"] = roomInfo.HerbDangerLevel;

                var exitsTable = NewLuaTable();
                for (var i = 0; i < roomInfo.Exits.Count; i++)
                {
                    var exitTable = NewLuaTable();
                    exitTable["Direction"] = roomInfo.Exits[i].Direction;
                    exitTable["RoomId"] = roomInfo.Exits[i].RoomId;
                    exitsTable[i + 1] = exitTable;
                }

                table["Exits"] = exitsTable;
                roomTable = table;
            }

            RunProtected(() => function.Call((double)roomId, (double)zoneId, roomTable));
        }

        /// <summary>
        /// Runs a single Lua call against this host's state, resetting and
        /// re-arming the watchdog around it so each top-level call starts
        /// fresh and the host remains fully reusable afterward. Catches the
        /// NLua exception raised by the instruction-budget hook and
        /// translates it into <see cref="LuaScriptTimeoutException"/>;
        /// any other Lua error is rethrown unchanged. Shared by
        /// <see cref="Eval"/> and intended for reuse by future entry points
        /// (e.g. an InvokeHandler method) so the catch/rethrow logic is not
        /// duplicated.
        /// </summary>
        private object RunProtected(Func<object[]> luaCall)
        {
            _budgetExceeded = false;
            _timeoutRequested = false;
            try
            {
                var results = luaCall();
                return results.Length > 0 ? results[0] : null;
            }
            catch (NLua.Exceptions.LuaScriptException)
            {
                if (_budgetExceeded)
                {
                    throw new LuaScriptTimeoutException(
                        "Script exceeded the instruction budget (" + InstructionBudget + " instructions) and was aborted.");
                }

                throw;
            }
            finally
            {
                // Whether this call completed normally, threw a regular Lua
                // error, or timed out, the next call on this host must start
                // with a clean watchdog state and the original large
                // interval -- otherwise a previous timeout would leave the
                // shrunk interval in place and cripple unrelated later
                // scripts on the same host.
                _timeoutRequested = false;
                _lua.State.SetHook(_hookFunction, LuaHookMask.Count, InstructionBudget);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _lua.Dispose();
            _disposed = true;
        }
    }
}
