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

        private readonly LuaScriptHostBindings _bindings;

        private readonly Dictionary<string, RunningScript> _runningScripts = new Dictionary<string, RunningScript>();

        private sealed class RunningScript
        {
            public KeraLua.Lua Thread;
            public ScriptRunStatus Status;
            public string LastError;
            public DateTime TimerDueAtUtc;
            public DateTime TextTimeoutUtc = DateTime.MaxValue;
            public string Code;
        }

        public enum ScriptEventType { Started, Stopped, Faulted, Finished }

        /// <summary>
        /// Raised when a named coroutine script transitions to a terminal or
        /// initial state: Started, Stopped (explicit), Finished (ran to end),
        /// or Faulted (runtime/syntax error). Third arg is the error string
        /// for Faulted, null otherwise. Always raised on the UI thread
        /// (the same thread that calls StartScript/StopScript/Tick).
        /// </summary>
        public event Action<string, ScriptEventType, string> ScriptEvent;

        public LuaScriptHost()
            : this(new LuaScriptHostBindings())
        {
        }

        public LuaScriptHost(Action<string> sendCommand)
            : this(new LuaScriptHostBindings { SendCommand = sendCommand })
        {
        }

        public LuaScriptHost(LuaScriptHostBindings bindings)
        {
            _bindings = bindings ?? new LuaScriptHostBindings();

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
            _lua.RegisterFunction("SetVariable", this, GetType().GetMethod(nameof(SetVariableFromLua)));
            _lua.RegisterFunction("ClearVariable", this, GetType().GetMethod(nameof(ClearVariableFromLua)));
            _lua.RegisterFunction("GetVariable", this, GetType().GetMethod(nameof(GetVariableFromLua)));
            _lua.RegisterFunction("Echo", this, GetType().GetMethod(nameof(EchoFromLua)));
            _lua.RegisterFunction("EnableGroup", this, GetType().GetMethod(nameof(EnableGroupFromLua)));
            _lua.RegisterFunction("DisableGroup", this, GetType().GetMethod(nameof(DisableGroupFromLua)));
            _lua.RegisterFunction("SetStatus", this, GetType().GetMethod(nameof(SetStatusFromLua)));
            _lua.RegisterFunction("SendToWindow", this, GetType().GetMethod(nameof(SendToWindowFromLua)));
            _lua.RegisterFunction("SendToAllWindows", this, GetType().GetMethod(nameof(SendToAllWindowsFromLua)));
            _lua.RegisterFunction("Lower", this, GetType().GetMethod(nameof(LowerFromLua)));

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
            // to resume. WaitGroupState/WaitRoomState/WaitRoomChange yield with
            // their own tags and are resumed by ResumeAllWaitingOn whenever the
            // corresponding RaiseXxxChanged method updates the matching
            // __last_* global -- the script reads that global itself right
            // after the yield returns, so it always sees fresh data.
            _lua.DoString(@"
                function Wait(ms)
                    coroutine.yield('timer', ms)
                end

                function WaitGroupState()
                    coroutine.yield('group')
                end

                function WaitRoomState()
                    coroutine.yield('room')
                end

                function WaitRoomChange()
                    coroutine.yield('roomchange')
                end

                function WaitText(timeout_ms)
                    coroutine.yield('text', timeout_ms or 0)
                    return __last_text
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
                var loadError = thread.ToString(-1);
                _runningScripts[name] = new RunningScript { Thread = thread, Status = ScriptRunStatus.Faulted, LastError = loadError, Code = code };
                ScriptEvent?.Invoke(name, ScriptEventType.Faulted, loadError);
                return;
            }

            var script = new RunningScript { Thread = thread, Status = ScriptRunStatus.Running, Code = code };
            _runningScripts[name] = script;
            ResumeScript(name, script);
            if (script.Status != ScriptRunStatus.Faulted && script.Status != ScriptRunStatus.Finished)
                ScriptEvent?.Invoke(name, ScriptEventType.Started, null);
        }

        /// <summary>
        /// Stops a running script -- it will not be resumed again. The
        /// coroutine itself is simply dropped (no cooperative cleanup/finally
        /// support in this version); its Lua thread becomes garbage.
        /// </summary>
        public void StopScript(string name)
        {
            if (_runningScripts.TryGetValue(name, out var existing))
            {
                bool wasAlive = existing.Status != ScriptRunStatus.Faulted
                             && existing.Status != ScriptRunStatus.Finished
                             && existing.Status != ScriptRunStatus.NotRunning;
                _runningScripts.Remove(name);
                if (wasAlive)
                    ScriptEvent?.Invoke(name, ScriptEventType.Stopped, null);
            }
        }

        /// <summary>Returns the Lua source code of the currently-running instance of a named
        /// script, or null if no such script is registered.</summary>
        public string GetScriptCode(string name)
        {
            RunningScript s;
            return _runningScripts.TryGetValue(name, out s) ? s.Code : null;
        }

        /// <summary>Returns the number of scripts currently in a live (non-terminal) state
        /// across all named scripts in this host.</summary>
        public int GetRunningCount()
        {
            int count = 0;
            foreach (var s in _runningScripts.Values)
            {
                if (s.Status != ScriptRunStatus.Faulted
                 && s.Status != ScriptRunStatus.Finished
                 && s.Status != ScriptRunStatus.NotRunning)
                    count++;
            }
            return count;
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
        /// The Lua error message captured the last time this script's
        /// status became Faulted (syntax error at StartScript, or a
        /// runtime error/watchdog trip during a resume), or null if it
        /// never faulted. Lets the UI show WHY a script died instead of
        /// just "Faulted".
        /// </summary>
        public string GetScriptError(string name)
        {
            RunningScript script;
            return _runningScripts.TryGetValue(name, out script) ? script.LastError : null;
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
                else if (pair.Value.Status == ScriptRunStatus.WaitingOnText && pair.Value.TextTimeoutUtc <= now)
                {
                    _lua["__last_text"] = null;
                    pair.Value.TextTimeoutUtc = DateTime.MaxValue;
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
                ScriptEvent?.Invoke(name, ScriptEventType.Finished, null);
                return;
            }

            if (status != LuaStatus.Yield)
            {
                // The watchdog hook (OnInstructionHook) raises its error via
                // state.Error(...) on the SAME thread that's executing --
                // i.e. this coroutine's own thread, not the main _lua.State
                // -- so the budget-exceeded message ends up here exactly
                // like any other runtime error, with no special-casing
                // needed to tell the two apart in the captured text.
                script.LastError = script.Thread.ToString(-1);
                script.Status = ScriptRunStatus.Faulted;
                ScriptEvent?.Invoke(name, ScriptEventType.Faulted, script.LastError);
                return;
            }

            var tag = script.Thread.ToString(1);
            if (tag == "timer")
            {
                var delayMs = script.Thread.ToNumber(2);
                script.Status = ScriptRunStatus.WaitingOnTimer;
                script.TimerDueAtUtc = DateTime.UtcNow.AddMilliseconds(delayMs);
            }
            else if (tag == "group")
            {
                script.Status = ScriptRunStatus.WaitingOnGroupState;
            }
            else if (tag == "room")
            {
                script.Status = ScriptRunStatus.WaitingOnRoomState;
            }
            else if (tag == "roomchange")
            {
                script.Status = ScriptRunStatus.WaitingOnRoomChange;
            }
            else if (tag == "text")
            {
                script.Status = ScriptRunStatus.WaitingOnText;
                var timeoutMs = script.Thread.ToNumber(2);
                script.TextTimeoutUtc = timeoutMs > 0
                    ? DateTime.UtcNow.AddMilliseconds(timeoutMs)
                    : DateTime.MaxValue;
            }
            else
            {
                // Unknown yield tag -- treat as an inert suspended state so
                // GetScriptStatus doesn't crash, but nothing will auto-resume
                // it.
                script.Status = ScriptRunStatus.WaitingOnTimer;
                script.TimerDueAtUtc = DateTime.MaxValue;
            }

            script.Thread.Pop(script.Thread.GetTop());
        }

        /// <summary>
        /// Resumes every currently-suspended script whose Waiting* status
        /// matches the given one. Called by RaiseGroupStateChanged/
        /// RaiseRoomStateChanged/RaiseRoomChanged after they update the
        /// corresponding __last_* global, so resumed scripts see fresh data
        /// the moment they read it.
        /// </summary>
        private void ResumeAllWaitingOn(ScriptRunStatus waitingStatus)
        {
            foreach (var pair in new List<KeyValuePair<string, RunningScript>>(_runningScripts))
            {
                if (pair.Value.Status == waitingStatus)
                {
                    ResumeScript(pair.Key, pair.Value);
                }
            }
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
            if (_bindings.SendCommand != null)
            {
                _bindings.SendCommand(command);
            }
        }

        /// <summary>Exposed to Lua as the global function <c>SetVariable</c>.</summary>
        public void SetVariableFromLua(string name, string value)
        {
            if (_bindings.SetVariable != null)
            {
                _bindings.SetVariable(name, value);
            }
        }

        /// <summary>Exposed to Lua as the global function <c>ClearVariable</c>.</summary>
        public void ClearVariableFromLua(string name)
        {
            if (_bindings.ClearVariable != null)
            {
                _bindings.ClearVariable(name);
            }
        }

        /// <summary>
        /// Exposed to Lua as the global function <c>GetVariable</c>. Returns
        /// empty string (never nil) when no GetVariable binding is set, so Lua
        /// scripts can always safely concatenate the result without a nil check.
        /// </summary>
        public string GetVariableFromLua(string name)
        {
            return _bindings.GetVariable != null ? _bindings.GetVariable(name) : string.Empty;
        }

        /// <summary>
        /// Exposed to Lua as the global function <c>Echo</c> -- outputs text to
        /// the main window WITHOUT sending it to the server (unlike SendCommand).
        /// </summary>
        public void EchoFromLua(string text)
        {
            if (_bindings.Echo != null)
            {
                _bindings.Echo(text);
            }
        }

        /// <summary>Exposed to Lua as the global function <c>EnableGroup</c>.</summary>
        public void EnableGroupFromLua(string name)
        {
            if (_bindings.EnableGroup != null)
            {
                _bindings.EnableGroup(name);
            }
        }

        /// <summary>Exposed to Lua as the global function <c>DisableGroup</c>.</summary>
        public void DisableGroupFromLua(string name)
        {
            if (_bindings.DisableGroup != null)
            {
                _bindings.DisableGroup(name);
            }
        }

        /// <summary>Exposed to Lua as the global function <c>SetStatus</c>.</summary>
        public void SetStatusFromLua(string text)
        {
            if (_bindings.SetStatus != null)
            {
                _bindings.SetStatus(text);
            }
        }

        /// <summary>
        /// Exposed to Lua as the global function <c>SendToWindow</c> -- sends a
        /// text command to another open tab, identified by its profile/character
        /// name (the same name shown in the tab and used by the existing
        /// SendToWindowAction's "window name" field).
        /// </summary>
        public void SendToWindowFromLua(string windowName, string text)
        {
            if (_bindings.SendToWindow != null)
            {
                _bindings.SendToWindow(windowName, text);
            }
        }

        /// <summary>
        /// Exposed to Lua as the global function <c>SendToAllWindows</c> -- sends
        /// a text command to every currently open tab (including this one).
        /// </summary>
        public string LowerFromLua(string s)
        {
            return s != null ? s.ToLowerInvariant() : string.Empty;
        }

        public void SendToAllWindowsFromLua(string text)
        {
            if (_bindings.SendToAllWindows != null)
            {
                _bindings.SendToAllWindows(text);
            }
        }

        /// <summary>
        /// Updates the <c>__last_group</c> Lua global with a freshly built
        /// 1-based array of tables (one per group member, via
        /// <see cref="BuildCharacterTable"/>), then resumes every script
        /// currently suspended in <see cref="ScriptRunStatus.WaitingOnGroupState"/>
        /// (i.e. blocked inside <c>WaitGroupState()</c>). A no-op when
        /// <paramref name="group"/> is null.
        /// </summary>
        public void RaiseGroupStateChanged(List<CharacterStatus> group)
        {
            if (group == null)
            {
                return;
            }

            var groupTable = NewLuaTable();
            for (var i = 0; i < group.Count; i++)
            {
                groupTable[i + 1] = BuildCharacterTable(group[i]);
            }

            _lua["__last_group"] = groupTable;
            ResumeAllWaitingOn(ScriptRunStatus.WaitingOnGroupState);
        }

        /// <summary>
        /// Updates the <c>__last_room_monsters</c> Lua global with a freshly
        /// built 1-based array of tables (one per monster, via
        /// <see cref="BuildCharacterTable"/> plus MonsterStatus's own
        /// IsPlayerCharacter/IsBoss), then resumes every script currently
        /// suspended in <see cref="ScriptRunStatus.WaitingOnRoomState"/>
        /// (i.e. blocked inside <c>WaitRoomState()</c>). A no-op when
        /// <paramref name="monsters"/> is null.
        /// </summary>
        public void RaiseRoomStateChanged(List<MonsterStatus> monsters)
        {
            if (monsters == null)
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

            _lua["__last_room_monsters"] = monstersTable;
            ResumeAllWaitingOn(ScriptRunStatus.WaitingOnRoomState);
        }

        /// <summary>
        /// Updates <c>__last_text</c> with the incoming line and resumes every
        /// script currently suspended inside <c>WaitText()</c>.
        /// </summary>
        public void RaiseTextReceived(string line)
        {
            _lua["__last_text"] = line ?? string.Empty;
            ResumeAllWaitingOn(ScriptRunStatus.WaitingOnText);
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
        /// Updates the <c>__last_room_id</c>/<c>__last_zone_id</c>/
        /// <c>__last_room</c> Lua globals -- roomId/zoneId are the only
        /// fields the server's CurrentRoomMessage packet itself carries;
        /// <paramref name="roomInfo"/> is data the CLIENT already has
        /// locally from its own loaded zone files (name, description,
        /// coordinates, exits, user annotations), not from the server
        /// packet. <paramref name="roomInfo"/> may be null (room not
        /// present in the local map) -- <c>__last_room</c> is then Lua nil,
        /// not an empty table, so scripts can tell "unmapped" apart from
        /// "mapped but everything blank". Then resumes every script
        /// currently suspended in
        /// <see cref="ScriptRunStatus.WaitingOnRoomChange"/> (i.e. blocked
        /// inside <c>WaitRoomChange()</c>).
        /// </summary>
        public void RaiseRoomChanged(int roomId, int zoneId, RoomInfo roomInfo)
        {
            _lua["__last_room_id"] = (double)roomId;
            _lua["__last_zone_id"] = (double)zoneId;

            // Skip expensive Lua table construction when no script is waiting on
            // a room change -- avoids N+2 DoString("return {}") calls per step
            // on routes even when no scripts are running at all.
            bool anyWaiting = false;
            foreach (var pair in _runningScripts)
            {
                if (pair.Value.Status == ScriptRunStatus.WaitingOnRoomChange)
                {
                    anyWaiting = true;
                    break;
                }
            }

            if (!anyWaiting)
                return;

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

            _lua["__last_room"] = roomTable;
            ResumeAllWaitingOn(ScriptRunStatus.WaitingOnRoomChange);
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
