# Lua Coroutine Scripts (Orion-style Start/Stop) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the fixed-function-name event model (`on_group_state`/`on_room_state`/`on_room_change`, one slot each, collides if two scripts define the same name) with independent, named, long-running Lua coroutine scripts that each have their own Start/Stop lifecycle and block on `Wait(ms)`/`WaitGroupState()`/`WaitRoomState()`/`WaitRoomChange()` -- the same model Orion uses for Ultima Online scripting.

**Architecture:** Each `ScriptDefinition` becomes its own Lua coroutine (`KeraLua.Lua` thread created via `_lua.State.NewThread()`), driven entirely through the low-level `KeraLua.Lua.Resume`/`.Yield`/`.Status` API (confirmed present in KeraLua 1.4.1 by reflection) rather than NLua's `LuaFunction.Call()` (which wraps `lua_pcall` and isn't coroutine-aware). To avoid the hard problem of marshalling values *into* a suspended coroutine across the C#/Lua boundary, resume always passes zero arguments -- `WaitGroupState()`/`WaitRoomState()`/`WaitRoomChange()` are plain Lua functions (shared globals, defined once) that yield with a tag, then on resume read the latest snapshot from a `__last_group`/`__last_room_monsters`/`__last_room_change` global table that `RaiseGroupStateChanged`/`RaiseRoomStateChanged`/`RaiseRoomChanged` already update on every packet (reusing the existing `BuildCharacterTable` etc.). `Wait(ms)` is the one case that *does* need a value out of the coroutine (how long to sleep) -- read directly off the yielding thread's own stack right after `Resume` returns, no cross-thread move needed. A new `Tick()` method (driven by a `DispatcherTimer` in `MainWindow`) resumes any coroutine whose timer is due. The old `RegisterGroupStateHandler`/`RegisterRoomStateHandler`/`RegisterRoomChangeHandler`/the fixed `on_group_state` convention, and the `ScriptDefinition.Handle*` checkboxes are removed entirely -- this is a full replacement, not an addition. `LuaScriptAction` (the per-trigger Lua action) is untouched: it still calls `LoadScript` synchronously, no coroutine involved.

**Tech Stack:** Same NLua 1.7.3 / KeraLua 1.4.1 already referenced. No new dependency. Low-level coroutine driving uses `KeraLua.Lua` directly (`using KeraLua;` already present in `LuaScriptHost.cs`).

---

## File Structure

- **Modify** `Adan.Client.Common/Scripting/LuaScriptHost.SandboxSetup.cs` -- re-allow a restricted `coroutine` table (only `create`/`resume`/`yield`/`status`) after the sweep, same pattern already used for `SendCommand`.
- **Modify** `Adan.Client.Common/Scripting/LuaScriptHost.cs` -- remove `RegisterGroupStateHandler`/`RegisterRoomStateHandler`/`RegisterRoomChangeHandler` and the fixed-name dispatch in `RaiseGroupStateChanged`/`RaiseRoomStateChanged`/`RaiseRoomChanged`; add the coroutine scheduler (`StartScript`/`StopScript`/`GetScriptStatus`/`Tick`); add the `Wait`/`WaitGroupState`/`WaitRoomState`/`WaitRoomChange` Lua-source runtime prelude; change `RaiseGroupStateChanged`/`RaiseRoomStateChanged`/`RaiseRoomChanged` to update the `__last_*` globals and wake any coroutines waiting on that event tag.
- **Create** `Adan.Client.Common/Scripting/ScriptRunStatus.cs` -- enum `{ NotRunning, Running, WaitingOnTimer, WaitingOnGroupState, WaitingOnRoomState, WaitingOnRoomChange, Finished, Faulted }`.
- **Modify** `Adan.Client.Common/Model/ScriptDefinition.cs` -- remove `HandlerKind`/`HandleGroupState`/`HandleRoomState`/`HandleRoomChange` (no longer read or written; old saved `Scripts.xml` files with these XML attributes still deserialize fine, `XmlSerializer` silently ignores attributes that no longer have a matching property).
- **Modify** `Adan.Client.Common/Model/RootModel.cs` -- `ReloadScripts()` now calls `StartScript`/`StopScript` per enabled/disabled script instead of the old `Register*Handler` calls.
- **Modify** `Adan.Client/ViewModel/ScriptViewModel.cs` -- remove `HandleGroupState`/`HandleRoomState`/`HandleRoomChange`, add `Status` (read-only, reflects `LuaScriptHost.GetScriptStatus`) and `StartCommand`/`StopCommand`.
- **Modify** `Adan.Client/ViewModel/ScriptsViewModel.cs` -- expose the `RootModel`/`ScriptHost` needed for Start/Stop, and a way to refresh `Status` periodically while the dialog is open.
- **Modify** `Adan.Client/Dialogs/ScriptsEditDialog.xaml` -- replace the three Handler checkboxes with a status label + Start/Stop buttons, plus a `DispatcherTimer` in the code-behind to refresh displayed status.
- **Modify** `Adan.Client/MainWindow.xaml.cs` -- add a `DispatcherTimer` that calls `rootModel.ScriptHost.Tick()` for every open tab periodically.
- **Modify** `Adan.Client/ViewModel/HelpTopics.cs` -- rewrite the events/fields topics for the new model.
- **Test:** `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs` -- new tests for the coroutine engine; remove/replace the now-obsolete `RegisterXxxHandler`/`RaiseXxxChanged`-fixed-dispatch tests.

---

## Build/test commands (same as every prior plan in this session)

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
VSTEST="/c/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/CommonExtensions/Microsoft/TestWindow/vstest.console.exe"
cd /c/tmp/mudclient
"$MSBUILD" Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
"$VSTEST" "Adan.Client.Common.Tests/bin/Debug/net48/Adan.Client.Common.Tests.dll"
"$MSBUILD" Adan.Client.Common/Adan.Client.Common.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```
Do NOT use `dotnet build`/`dotnet test`.

**Confirmed KeraLua 1.4.1 API (by reflection, not assumption) used throughout this plan:**
```
KeraLua.Lua NewThread()
LuaStatus Resume(KeraLua.Lua from, int arguments)
LuaStatus LoadString(string chunk, string name)
LuaStatus get_Status()
int GetTop()
string ToString(int index)
double ToNumber(int index)   // confirm exact name/overload when implementing -- reflection found ToString/PushNumber but verify ToNumber exists; if not, use ToString(index) and double.Parse, or check for a "ToNumber"-named method directly on KeraLua.Lua before assuming
void SetHook(LuaHookFunction, LuaHookMask, int)   // already used in LuaScriptHost.cs
enum LuaStatus { OK, Yield, ErrRun, ErrSyntax, ErrMem, ErrErr }
```

---

### Task 1: Re-allow a restricted `coroutine` table in the sandbox

**Files:**
- Modify: `Adan.Client.Common/Scripting/LuaScriptHost.SandboxSetup.cs`
- Test: `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs`

- [ ] **Step 1: Read the current file in full**

```bash
cat Adan.Client.Common/Scripting/LuaScriptHost.SandboxSetup.cs
```

Confirm the exact current shape of `AllowedGlobals` and the sweep `DoString` call -- this task adds to it, doesn't replace it wholesale.

- [ ] **Step 2: Write the failing tests**

Add to `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs`:

```csharp
[Test]
public void SandboxedState_HasRestrictedCoroutineTable()
{
    using (var host = new LuaScriptHost())
    {
        Assert.That(host.Eval("return type(coroutine) == 'table'"), Is.EqualTo(true));
        Assert.That(host.Eval("return type(coroutine.create) == 'function'"), Is.EqualTo(true));
        Assert.That(host.Eval("return type(coroutine.resume) == 'function'"), Is.EqualTo(true));
        Assert.That(host.Eval("return type(coroutine.yield) == 'function'"), Is.EqualTo(true));
        Assert.That(host.Eval("return type(coroutine.status) == 'function'"), Is.EqualTo(true));
    }
}

[Test]
public void SandboxedState_CoroutineTableHasNoWrapOrClose()
{
    // coroutine.wrap/close aren't needed by anything in this plan and
    // widen the surface unnecessarily -- confirm they're absent, not
    // just "the four we need are present".
    using (var host = new LuaScriptHost())
    {
        Assert.That(host.Eval("return coroutine.wrap == nil"), Is.EqualTo(true));
        Assert.That(host.Eval("return coroutine.close == nil"), Is.EqualTo(true));
    }
}
```

- [ ] **Step 3: Run to verify failure**

Build the test project. Expected: both tests fail (`coroutine` is currently nil -- confirm by running, since the sandbox currently strips it entirely per `AllowedGlobals` not including it).

- [ ] **Step 4: Implement**

In `LuaScriptHost.SandboxSetup.cs`, after the existing sweep `DoString` call inside `CreateSandboxedState()`, add:

```csharp
// The sweep above just deleted `coroutine` along with everything else
// not in AllowedGlobals. Re-create a minimal coroutine table exposing
// only create/resume/yield/status -- wrap/close/isyieldable/running
// aren't needed by anything in this codebase and are left out to keep
// the sandbox surface as small as possible.
lua.DoString(@"
    local real_coroutine = debug and nil  -- debug is already nil; placeholder removed below
");
```

That sketch is wrong -- `debug` is already stripped by the time this runs, so there is no way to recover the *original* `coroutine` table from Lua source after the sweep (it's gone). Instead, capture a reference to `coroutine` **before** the sweep runs, then re-expose only the four needed functions from that saved reference afterward:

```csharp
private static Lua CreateSandboxedState()
{
    var lua = new Lua();
    lua.State.Encoding = System.Text.Encoding.UTF8;

    // Save the original coroutine table before the sweep deletes it,
    // so a restricted version of it can be re-exposed afterward.
    var originalCoroutine = (LuaTable)lua.GetTable("coroutine");

    lua.DoString(@"
        local allowed = {}
        for _, name in ipairs({...}) do allowed[name] = true end
        for key in pairs(_G) do
            if not allowed[key] and key ~= '_G' then
                _G[key] = nil
            end
        end
    ", "sandbox-init", AllowedGlobals);

    var restrictedCoroutine = (LuaTable)lua.DoString("return {}")[0];
    restrictedCoroutine["create"] = originalCoroutine["create"];
    restrictedCoroutine["resume"] = originalCoroutine["resume"];
    restrictedCoroutine["yield"] = originalCoroutine["yield"];
    restrictedCoroutine["status"] = originalCoroutine["status"];
    lua["coroutine"] = restrictedCoroutine;

    return lua;
}
```

Check `Lua.GetTable(string)` is the right NLua method to fetch a global table by name (it's commonly used in NLua -- if it doesn't exist under that exact name in 1.7.3, use `lua["coroutine"]` indexer-style access, which NLua's `Lua` class supports via its indexer returning `object`, then cast to `LuaTable`). Verify against the actual NLua 1.7.3 API before assuming; this is a one-line fix if the name differs.

- [ ] **Step 5: Run tests to verify they pass.** Expected: all sandbox tests still pass (including the pre-existing `SandboxedState_HasNoIoLibrary` etc.), plus the 2 new ones, with no regressions.

- [ ] **Step 6: Commit**

```bash
git add Adan.Client.Common/Scripting/LuaScriptHost.SandboxSetup.cs Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs
git commit -m "feat: re-allow a restricted coroutine table (create/resume/yield/status only)"
```

---

### Task 2: Coroutine scheduler core -- StartScript/StopScript/GetScriptStatus/Tick (timer-only first)

**Files:**
- Create: `Adan.Client.Common/Scripting/ScriptRunStatus.cs`
- Modify: `Adan.Client.Common/Scripting/LuaScriptHost.cs`
- Test: `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs`

This task wires up `Wait(ms)` only (timer-based yield). Event-based `WaitGroupState`/`WaitRoomState`/`WaitRoomChange` are Task 3.

- [ ] **Step 1: Create the status enum**

```csharp
// Adan.Client.Common/Scripting/ScriptRunStatus.cs
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

        /// <summary>The coroutine's chunk ran to completion (returned
        /// without yielding again) -- a one-shot script, not an error.</summary>
        Finished,

        /// <summary>Resume returned an error status (syntax/runtime error,
        /// or the instruction-budget watchdog tripped). The script is
        /// dead and won't be resumed again; re-Start it to retry.</summary>
        Faulted,
    }
}
```

- [ ] **Step 2: Add the `<Compile>` entry**

In `Adan.Client.Common/Adan.Client.Common.csproj`, add `<Compile Include="Scripting\ScriptRunStatus.cs" />` next to the other `Scripting\*.cs` entries.

- [ ] **Step 3: Write the failing tests**

Append to `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs`:

```csharp
[Test]
public void StartScript_OneShotScript_RunsAndFinishes()
{
    using (var host = new LuaScriptHost())
    {
        host.StartScript("test", "did_run = true");
        Assert.That(host.Eval("return did_run"), Is.EqualTo(true));
        Assert.That(host.GetScriptStatus("test"), Is.EqualTo(ScriptRunStatus.Finished));
    }
}

[Test]
public void StartScript_WithWait_SuspendsUntilTickPastDeadline()
{
    using (var host = new LuaScriptHost())
    {
        host.StartScript("test", "counter = 0 while true do counter = counter + 1 Wait(50) end");

        Assert.That(host.Eval("return counter"), Is.EqualTo(1));
        Assert.That(host.GetScriptStatus("test"), Is.EqualTo(ScriptRunStatus.WaitingOnTimer));

        // Not due yet -- Tick() right away must not resume it.
        host.Tick();
        Assert.That(host.Eval("return counter"), Is.EqualTo(1));

        System.Threading.Thread.Sleep(80);
        host.Tick();
        Assert.That(host.Eval("return counter"), Is.EqualTo(2));
        Assert.That(host.GetScriptStatus("test"), Is.EqualTo(ScriptRunStatus.WaitingOnTimer));
    }
}

[Test]
public void StopScript_PreventsFurtherResumes()
{
    using (var host = new LuaScriptHost())
    {
        host.StartScript("test", "counter = 0 while true do counter = counter + 1 Wait(10) end");
        host.StopScript("test");
        Assert.That(host.GetScriptStatus("test"), Is.EqualTo(ScriptRunStatus.NotRunning));

        System.Threading.Thread.Sleep(50);
        host.Tick();
        Assert.That(host.Eval("return counter"), Is.EqualTo(1));
    }
}

[Test]
public void GetScriptStatus_UnknownScript_ReturnsNotRunning()
{
    using (var host = new LuaScriptHost())
    {
        Assert.That(host.GetScriptStatus("never-started"), Is.EqualTo(ScriptRunStatus.NotRunning));
    }
}

[Test]
public void StartScript_SyntaxError_IsFaultedNotThrown()
{
    using (var host = new LuaScriptHost())
    {
        Assert.DoesNotThrow(() => host.StartScript("broken", "this is not valid lua ((("));
        Assert.That(host.GetScriptStatus("broken"), Is.EqualTo(ScriptRunStatus.Faulted));
    }
}

[Test]
public void StartScript_RunawayLoopWithoutWait_BecomesFaultedNotHung()
{
    using (var host = new LuaScriptHost())
    {
        host.StartScript("runaway", "while true do end");
        Assert.That(host.GetScriptStatus("runaway"), Is.EqualTo(ScriptRunStatus.Faulted));
    }
}
```

- [ ] **Step 4: Run to verify failure** -- expect build errors (`StartScript`/`StopScript`/`GetScriptStatus`/`Tick`/`ScriptRunStatus` don't exist).

- [ ] **Step 5: Implement the scheduler**

Read the current full `LuaScriptHost.cs` first (it has grown across several prior tasks in this session -- confirm the exact current constructor body, `RunProtected`, and field list before editing, since line numbers below are approximate). Add:

```csharp
// Add near the top of the class, with the other private fields:
using System.Collections.Generic;
// (System.Collections.Generic is likely already imported -- confirm.)

private readonly Dictionary<string, RunningScript> _runningScripts =
    new Dictionary<string, RunningScript>();

private sealed class RunningScript
{
    public KeraLua.Lua Thread;
    public ScriptRunStatus Status;
    public DateTime TimerDueAtUtc;
}
```

Add a constant for the runtime prelude and load it once, right after the existing `SendCommand` registration in the constructor (after `InstallPcallGuard();` and the `_lua.RegisterFunction("SendCommand", ...)` line -- read the real constructor to place this correctly):

```csharp
// Shared by every coroutine script (they all see the same globals).
// Wait(ms) yields with a "timer" tag + the requested delay; Tick()
// reads that delay off the yielding thread's own stack (no cross-
// thread value passing needed) and decides when to resume. The
// WaitXxxState functions are added in Task 3.
_lua.DoString(@"
    function Wait(ms)
        coroutine.yield('timer', ms)
    end
", "scripting-runtime-prelude");
```

Add the scheduler methods (place near `LoadScript`):

```csharp
/// <summary>
/// Starts (or restarts, if already running/finished/faulted under this
/// name) a named coroutine script. The chunk runs immediately up to its
/// first yield (Wait/WaitGroupState/etc.) or to completion -- this call
/// is synchronous for that first leg, same watchdog budget as Eval.
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
/// periodically (e.g. every 100-200ms from a UI timer) -- see
/// MainWindow's DispatcherTimer (Task 6).
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
/// Resumes the given script's coroutine with zero arguments (see the
/// class-level remarks on why resume never passes values -- WaitXxxState
/// read shared globals instead). Inspects the result: OK means the
/// chunk finished; Yield means it's suspended again (tag read off the
/// thread's own stack decides which Waiting* state it's now in); any
/// error status means Faulted.
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
    else
    {
        // Unknown yield tag -- treat as a generic suspended state so it
        // at least doesn't get silently dropped; nothing will resume it
        // automatically (no Task-3 event for an unrecognized tag), but
        // GetScriptStatus won't crash.
        script.Status = ScriptRunStatus.WaitingOnTimer;
        script.TimerDueAtUtc = DateTime.MaxValue;
    }

    script.Thread.Pop(script.Thread.GetTop());
}
```

`KeraLua.Lua.ToNumber(int)` was assumed in the investigation but not directly confirmed in the reflection dump (only `PushNumber` was listed, not `ToNumber`) -- **before relying on this method name, run a quick reflection check** (`[KeraLua.Lua].GetMethods() | Where-Object Name -match "ToNumber|CheckNumber|OptNumber"`) and use whatever the real method is named. If `ToNumber` doesn't exist, `double.Parse(script.Thread.ToString(2))` is an acceptable fallback (Lua numbers stringify cleanly).

- [ ] **Step 6: Run tests to verify they pass.**

- [ ] **Step 7: Commit**

```bash
git add Adan.Client.Common/Scripting/ScriptRunStatus.cs Adan.Client.Common/Scripting/LuaScriptHost.cs Adan.Client.Common/Adan.Client.Common.csproj Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs
git commit -m "feat: coroutine script scheduler (StartScript/StopScript/Tick) with timer-based Wait(ms)"
```

---

### Task 3: Event-based waits -- WaitGroupState/WaitRoomState/WaitRoomChange

**Files:**
- Modify: `Adan.Client.Common/Scripting/LuaScriptHost.cs`
- Test: `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs`

This is also where the OLD `RegisterGroupStateHandler`/`RegisterRoomStateHandler`/`RegisterRoomChangeHandler` methods and the fixed-name dispatch inside `RaiseGroupStateChanged`/`RaiseRoomStateChanged`/`RaiseRoomChanged` get removed -- read the current bodies of those three methods in full before editing (they were last touched in the "expose all CharacterStatus/MonsterStatus fields" and "RoomInfo" tasks earlier this session).

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void WaitGroupState_ResumesWithLatestGroupSnapshot()
{
    using (var host = new LuaScriptHost())
    {
        host.StartScript("test", @"
            captured_count = 0
            while true do
                WaitGroupState()
                captured_count = captured_count + 1
                captured_name = __last_group[1].Name
            end
        ");

        Assert.That(host.GetScriptStatus("test"), Is.EqualTo(ScriptRunStatus.WaitingOnGroupState));

        host.RaiseGroupStateChanged(new List<CharacterStatus> { new CharacterStatus { Name = "Тазерал" } });

        Assert.That(host.Eval("return captured_count"), Is.EqualTo(1));
        Assert.That(host.Eval("return captured_name"), Is.EqualTo("Тазерал"));
        Assert.That(host.GetScriptStatus("test"), Is.EqualTo(ScriptRunStatus.WaitingOnGroupState));
    }
}

[Test]
public void WaitRoomState_ResumesWithLatestMonsterSnapshot()
{
    using (var host = new LuaScriptHost())
    {
        host.StartScript("test", @"
            while true do
                WaitRoomState()
                captured_count = #__last_room_monsters
            end
        ");

        host.RaiseRoomStateChanged(new List<MonsterStatus> { new MonsterStatus(), new MonsterStatus() });

        Assert.That(host.Eval("return captured_count"), Is.EqualTo(2));
    }
}

[Test]
public void WaitRoomChange_ResumesWithRoomIdZoneIdAndRoomInfo()
{
    using (var host = new LuaScriptHost())
    {
        host.StartScript("test", @"
            while true do
                WaitRoomChange()
                captured_room_id = __last_room_id
                captured_zone_name = __last_room and __last_room.ZoneName or nil
            end
        ");

        var info = new RoomInfo { ZoneName = "Минас-Тирит" };
        host.RaiseRoomChanged(1842, 12, info);

        Assert.That(host.Eval("return captured_room_id"), Is.EqualTo(1842));
        Assert.That(host.Eval("return captured_zone_name"), Is.EqualTo("Минас-Тирит"));
    }
}

[Test]
public void TwoScripts_CanBothWaitOnGroupStateIndependently()
{
    // The whole point of the coroutine model: no shared-function-name
    // collision, unlike the old on_group_state convention it replaces.
    using (var host = new LuaScriptHost())
    {
        host.StartScript("a", "a_count = 0 while true do WaitGroupState() a_count = a_count + 1 end");
        host.StartScript("b", "b_count = 0 while true do WaitGroupState() b_count = b_count + 1 end");

        host.RaiseGroupStateChanged(new List<CharacterStatus> { new CharacterStatus() });
        host.RaiseGroupStateChanged(new List<CharacterStatus> { new CharacterStatus() });

        Assert.That(host.Eval("return a_count"), Is.EqualTo(2));
        Assert.That(host.Eval("return b_count"), Is.EqualTo(2));
    }
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Add the runtime prelude functions**

In the constructor, extend the prelude `DoString` added in Task 2:

```csharp
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
", "scripting-runtime-prelude");
```

- [ ] **Step 4: Add a generic "resume everyone waiting on tag X" helper**

```csharp
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
```

- [ ] **Step 5: Rewrite `RaiseGroupStateChanged`/`RaiseRoomStateChanged`/`RaiseRoomChanged`**

Remove `RegisterGroupStateHandler`, `RegisterRoomStateHandler`, `RegisterRoomChangeHandler`, and the `_groupStateHandlerName`/`_roomStateHandlerName`/`_roomChangeHandlerName` fields entirely. Replace the three Raise methods' bodies (keep the existing table-building code -- `BuildCharacterTable`, the `Exits` table construction in `RaiseRoomChanged`, etc. -- only the dispatch at the end changes):

```csharp
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

public void RaiseRoomChanged(int roomId, int zoneId, RoomInfo roomInfo)
{
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

    _lua["__last_room_id"] = (double)roomId;
    _lua["__last_zone_id"] = (double)zoneId;
    _lua["__last_room"] = roomTable;
    ResumeAllWaitingOn(ScriptRunStatus.WaitingOnRoomChange);
}
```

Note these three methods are no longer routed through `RunProtected` themselves (updating a global and resuming waiters isn't itself a Lua call that can time out the same way `Eval` can) -- `ResumeScript` internally resets/checks the watchdog per resumed coroutine, which is the equivalent protection for the actual Lua execution that happens inside each resume.

- [ ] **Step 6: Run tests to verify they pass.** Expect all of Task 1-3's tests plus the pre-existing sandbox/watchdog tests from earlier sessions to pass. The OLD tests named `RegisterGroupStateHandler_FiresWithMemberData`, `RaiseGroupStateChanged_NoHandlerRegistered_DoesNothing`, `RaiseRoomStateChanged_FiresRegisteredHandlerWithMonsterData`, `RaiseGroupStateChanged_NullGroup_DoesNotThrow`, `RaiseRoomStateChanged_NullMonsters_DoesNotThrow`, `RaiseRoomChanged_WithRoomInfo_ExposesLocalMapData`, `RaiseRoomChanged_NullRoomInfo_PassesNilForRoomTable`, `RaiseRoomChanged_OldTwoArgFunction_StillWorks`, `OneScript_CanRegisterAllThreeHandlersAtOnce` reference the REMOVED `RegisterXxxHandler` methods -- **delete these tests**, they test API that no longer exists. (`RaiseGroupStateChanged_NullGroup_DoesNotThrow`/`RaiseRoomStateChanged_NullMonsters_DoesNotThrow` can be kept if rewritten to just call `Assert.DoesNotThrow(() => host.RaiseGroupStateChanged(null))` without the old handler setup -- the null-safety behavior itself is still correct and worth covering.)

- [ ] **Step 7: Commit**

```bash
git add Adan.Client.Common/Scripting/LuaScriptHost.cs Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs
git commit -m "feat: event-based WaitGroupState/WaitRoomState/WaitRoomChange; remove old fixed-handler dispatch"
```

---

### Task 4: `ScriptDefinition` cleanup + `RootModel.ReloadScripts()` rewrite

**Files:**
- Modify: `Adan.Client.Common/Model/ScriptDefinition.cs`
- Modify: `Adan.Client.Common/Model/RootModel.cs`

No new test -- `RootModel` remains untested per this codebase's established pattern; verified manually in Task 8.

- [ ] **Step 1: Simplify `ScriptDefinition`**

Remove the `HandleGroupState`, `HandleRoomState`, `HandleRoomChange` properties and the `HandlerKind` property (and the now-unused `ScriptHandlerKind` enum file, `Adan.Client.Common/Model/ScriptHandlerKind.cs` -- delete it and its `<Compile>` entry in `Adan.Client.Common.csproj`). Final shape:

```csharp
namespace Adan.Client.Common.Model
{
    using System;
    using System.Xml.Serialization;

    using CSLib.Net.Annotations;

    /// <summary>
    /// A single named, persisted Lua coroutine script not tied to any
    /// trigger/alias -- editable via the Scripts dialog. IsEnabled means
    /// "start automatically when a tab connects" (RootModel.ReloadScripts);
    /// it does not mean "currently running" -- see LuaScriptHost.GetScriptStatus
    /// for runtime state, which isn't persisted.
    /// </summary>
    [Serializable]
    public class ScriptDefinition
    {
        public ScriptDefinition()
        {
            Name = string.Empty;
            Code = string.Empty;
        }

        [NotNull]
        [XmlAttribute]
        public string Name
        {
            get;
            set;
        }

        [NotNull]
        [XmlElement]
        public string Code
        {
            get;
            set;
        }

        [XmlAttribute]
        public bool IsEnabled
        {
            get;
            set;
        }
    }
}
```

Old `Scripts.xml` files with `HandlerKind="GroupState"` or `HandleGroupState="true"` XML attributes still deserialize without error -- `XmlSerializer` silently ignores XML attributes with no matching property. The scripts themselves (their `Code`) are preserved; only the old routing metadata is dropped, which is correct since the routing model itself no longer exists.

- [ ] **Step 2: Rewrite `RootModel.ReloadScripts()`**

Read the current method in full (added across two earlier tasks this session -- confirm exact current body before editing). Replace the per-script try/catch block:

```csharp
public void ReloadScripts()
{
    if (_scriptHost == null || _profile == null)
    {
        return;
    }

    foreach (var script in _profile.Scripts)
    {
        if (script.IsEnabled)
        {
            try
            {
                _scriptHost.StartScript(script.Name, script.Code);
            }
            catch (Exception)
            {
                // StartScript itself already catches Lua-level errors
                // (LoadString failures, watchdog trips on the first leg)
                // and reports them via GetScriptStatus == Faulted instead
                // of throwing -- this catch is defense in depth only,
                // e.g. against an unexpected non-Lua exception, so one
                // broken script can never prevent the tab from opening.
            }
        }
        else
        {
            _scriptHost.StopScript(script.Name);
        }
    }
}
```

Note `StartScript` is idempotent-safe to call repeatedly (it calls `StopScript` on the same name first internally, per Task 2's implementation) -- calling `ReloadScripts()` again (e.g. from the Scripts dialog's Save button) restarts every enabled script from scratch, which is the correct behavior for "apply my edits now" (a script's old running state, mid-Wait, is discarded and it starts over from the top).

- [ ] **Step 3: Build**

```bash
"$MSBUILD" Adan.Client.Common/Adan.Client.Common.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```

Fix any remaining references to the deleted `ScriptHandlerKind` type or `HandlerKind`/`Handle*` properties elsewhere in `Adan.Client.Common` (there should be none outside `ScriptDefinition.cs`/`RootModel.cs` after this task, but the compiler will catch any missed spot).

- [ ] **Step 4: Commit**

```bash
git add Adan.Client.Common/Model/ScriptDefinition.cs Adan.Client.Common/Model/RootModel.cs Adan.Client.Common/Adan.Client.Common.csproj
git rm Adan.Client.Common/Model/ScriptHandlerKind.cs
git commit -m "feat: ScriptDefinition no longer carries handler-kind routing; ReloadScripts drives Start/StopScript"
```

---

### Task 5: `ScriptViewModel`/`ScriptsViewModel` -- Status, Start/Stop commands

**Files:**
- Modify: `Adan.Client/ViewModel/ScriptViewModel.cs`
- Modify: `Adan.Client/ViewModel/ScriptsViewModel.cs`

- [ ] **Step 1: Read both files in full** (as last left by the earlier "Live-reload Scripts" and "let one script handle multiple packet types" tasks this session) before editing.

- [ ] **Step 2: Rewrite `ScriptViewModel`**

Remove `HandleGroupState`/`HandleRoomState`/`HandleRoomChange`. Add a `Status` property and `StartCommand`/`StopCommand`, taking the owning `LuaScriptHost` so it can call `StartScript`/`StopScript`/`GetScriptStatus` directly:

```csharp
namespace Adan.Client.ViewModel
{
    using Common.Model;
    using Common.Scripting;
    using Common.ViewModel;
    using Common.Utils;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// Wraps a single ScriptDefinition for binding in the Scripts dialog,
    /// plus live Start/Stop control over its LuaScriptHost coroutine.
    /// </summary>
    public class ScriptViewModel : ViewModelBase
    {
        private readonly ScriptDefinition _script;
        private readonly LuaScriptHost _scriptHost;

        public ScriptViewModel([NotNull] ScriptDefinition script, [NotNull] LuaScriptHost scriptHost)
        {
            Assert.ArgumentNotNull(script, "script");
            Assert.ArgumentNotNull(scriptHost, "scriptHost");
            _script = script;
            _scriptHost = scriptHost;

            StartCommand = new DelegateCommand(StartCommandExecute, true);
            StopCommand = new DelegateCommand(StopCommandExecute, true);
        }

        [NotNull]
        public ScriptDefinition Script
        {
            get { return _script; }
        }

        public string Name
        {
            get { return _script.Name; }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                _script.Name = value;
                OnPropertyChanged("Name");
            }
        }

        public string Code
        {
            get { return _script.Code; }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                _script.Code = value;
                OnPropertyChanged("Code");
            }
        }

        public bool IsEnabled
        {
            get { return _script.IsEnabled; }
            set
            {
                _script.IsEnabled = value;
                OnPropertyChanged("IsEnabled");
            }
        }

        /// <summary>
        /// Live runtime status -- NOT persisted. Call RefreshStatus()
        /// periodically (see ScriptsEditDialog's DispatcherTimer, Task 6)
        /// to keep this current while the dialog is open.
        /// </summary>
        public ScriptRunStatus Status
        {
            get { return _scriptHost.GetScriptStatus(_script.Name); }
        }

        [NotNull]
        public DelegateCommand StartCommand
        {
            get;
            private set;
        }

        [NotNull]
        public DelegateCommand StopCommand
        {
            get;
            private set;
        }

        /// <summary>
        /// Re-reads Status from the host and raises PropertyChanged --
        /// call this from a UI timer, since LuaScriptHost has no change
        /// notification of its own.
        /// </summary>
        public void RefreshStatus()
        {
            OnPropertyChanged("Status");
        }

        private void StartCommandExecute(object obj)
        {
            _scriptHost.StartScript(_script.Name, _script.Code);
            RefreshStatus();
        }

        private void StopCommandExecute(object obj)
        {
            _scriptHost.StopScript(_script.Name);
            RefreshStatus();
        }
    }
}
```

- [ ] **Step 3: Update `ScriptsViewModel`** to pass `scriptHost` through to each `ScriptViewModel`, and add a `RefreshAllStatuses()` for the dialog's timer to call:

```csharp
namespace Adan.Client.ViewModel
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;

    using Common.Model;
    using Common.Scripting;
    using Common.Utils;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// View model for the Scripts editor dialog -- a flat list of named
    /// coroutine scripts, not nested under any trigger/alias Group.
    /// </summary>
    public class ScriptsViewModel : ViewModelBase
    {
        private readonly List<ScriptDefinition> _backingList;
        private readonly LuaScriptHost _scriptHost;
        private ScriptViewModel _selectedScript;

        public ScriptsViewModel([NotNull] List<ScriptDefinition> backingList, [NotNull] LuaScriptHost scriptHost)
        {
            Assert.ArgumentNotNull(backingList, "backingList");
            Assert.ArgumentNotNull(scriptHost, "scriptHost");

            _backingList = backingList;
            _scriptHost = scriptHost;
            Scripts = new ObservableCollection<ScriptViewModel>(
                backingList.Select(s => new ScriptViewModel(s, scriptHost)));

            AddScriptCommand = new DelegateCommand(AddScriptCommandExecute, true);
            DeleteScriptCommand = new DelegateCommand(DeleteScriptCommandExecute, false);
        }

        [NotNull]
        public ObservableCollection<ScriptViewModel> Scripts
        {
            get;
            private set;
        }

        [CanBeNull]
        public ScriptViewModel SelectedScript
        {
            get { return _selectedScript; }
            set
            {
                _selectedScript = value;
                DeleteScriptCommand.CanBeExecuted = value != null;
                OnPropertyChanged("SelectedScript");
            }
        }

        [NotNull]
        public DelegateCommand AddScriptCommand
        {
            get;
            private set;
        }

        [NotNull]
        public DelegateCommand DeleteScriptCommand
        {
            get;
            private set;
        }

        /// <summary>Called from ScriptsEditDialog's DispatcherTimer.</summary>
        public void RefreshAllStatuses()
        {
            foreach (var scriptViewModel in Scripts)
            {
                scriptViewModel.RefreshStatus();
            }
        }

        private void AddScriptCommandExecute(object obj)
        {
            var newScript = new ScriptDefinition { Name = "New script" };
            _backingList.Add(newScript);
            var newViewModel = new ScriptViewModel(newScript, _scriptHost);
            Scripts.Add(newViewModel);
            SelectedScript = newViewModel;
        }

        private void DeleteScriptCommandExecute(object obj)
        {
            if (SelectedScript == null)
            {
                return;
            }

            _scriptHost.StopScript(SelectedScript.Script.Name);
            _backingList.Remove(SelectedScript.Script);
            Scripts.Remove(SelectedScript);
            SelectedScript = null;
        }
    }
}
```

- [ ] **Step 4: Build**

```bash
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```

This will fail until Task 6 updates the `new ScriptsViewModel(Profile.Scripts)` call site in `ProfileOptionsViewModel.cs` to pass the `LuaScriptHost` too (it now takes 2 constructor arguments, not 1) -- expect and accept that build failure here; Task 6 fixes it. Do not try to make this task build in isolation.

- [ ] **Step 5: Commit** (deferred to Task 6, since this task alone doesn't build -- see Task 6's commit step, which includes these files).

---

### Task 6: Wire `ProfileOptionsViewModel`, add `ScriptsEditDialog`'s status-refresh timer, Start/Stop UI

**Files:**
- Modify: `Adan.Client/ViewModel/ProfileOptionsViewModel.cs`
- Modify: `Adan.Client/Dialogs/ScriptsEditDialog.xaml`
- Modify: `Adan.Client/Dialogs/ScriptsEditDialog.xaml.cs`

- [ ] **Step 1: Find and fix the `ScriptsViewModel` construction site**

In `Adan.Client/ViewModel/ProfileOptionsViewModel.cs`, find `case "Scripts":` (added in an earlier task this session) and change:

```csharp
DataContext = new ScriptsViewModel(Profile.Scripts),
```

to:

```csharp
DataContext = new ScriptsViewModel(Profile.Scripts, RootModel.ScriptHost),
```

Confirm `RootModel` is a real, in-scope static/instance reference at this point in the file -- it's already used elsewhere in this same method (`RootModel.AllActionDescriptions` appears in other cases) as a STATIC type reference (`Adan.Client.Common.Model.RootModel`), not an instance -- but `ScriptHost` is an INSTANCE property (added much earlier this session on `RootModel` instances, not as a static member). This means the static `RootModel.AllActionDescriptions` pattern used elsewhere does NOT give you a `ScriptHost` -- you need an actual `RootModel` *instance* representing the tab whose profile this is, which this clone-based edit flow (`ProfilesEditViewModel.EditProfile`) does not currently have access to (it only has a cloned `ProfileHolder`, not a live `RootModel`).

**This is the real crux of this task.** Read `ApplyScriptsChanges()` in `ProfileOptionsViewModel.cs` (added in the "live-reload" task earlier this session) -- it already iterates `_allRootModels` to find matching live `RootModel`s by profile name. Use the SAME pattern here: find the first live, already-connected `RootModel` for this profile (if any) and use ITS `ScriptHost`; if none is connected yet, construct a throwaway design-time-only `LuaScriptHost` purely so the dialog has something non-null to call Start/Stop against (Start/Stop on a throwaway host before any tab is open is inherently a no-op for gameplay purposes, but the UI still needs a non-null host to avoid crashing):

```csharp
var liveRootModel = _allRootModels != null
    ? _allRootModels.FirstOrDefault(m => m.Profile != null && m.Profile.Name == Profile.Name)
    : null;
var scriptHostForDialog = liveRootModel != null
    ? liveRootModel.ScriptHost
    : new Scripting.LuaScriptHost();

var scriptsEditDialog = new ScriptsEditDialog
{
    DataContext = new ScriptsViewModel(Profile.Scripts, scriptHostForDialog),
    Owner = owner
};
```

Add `using System.Linq;` if not already present (check the file's current `using` list first -- `ApplyScriptsChanges` likely already needs/uses it for the same `FirstOrDefault`-style pattern, given it loops `_allRootModels`).

- [ ] **Step 2: Simplify `ApplyScriptsChanges()`**

The old version's per-RootModel loop called `rootModel.ReloadScripts()` on every matching tab. That's still correct and doesn't need to change -- `ReloadScripts()` (rewritten in Task 4) now drives `StartScript`/`StopScript` instead of the old handler registration, but the calling code in `ProfileOptionsViewModel` doesn't need to know that detail. Leave `ApplyScriptsChanges()` as-is from the earlier task UNLESS it referenced anything from the removed `HandlerKind`/`Handle*` properties (it shouldn't have -- confirm by reading it).

- [ ] **Step 3: Replace the Handler checkboxes in `ScriptsEditDialog.xaml` with Status + Start/Stop**

Read the current file in full (last modified by the "let one script handle multiple packet types" task earlier this session) before editing. Replace the `<StackPanel Grid.Row="1" ...>` block that currently has the three `CheckBox`es:

```xml
<StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,5">
    <CheckBox Content="Auto-start on connect" IsChecked="{Binding Path=IsEnabled, Mode=TwoWay}" VerticalAlignment="Center" Margin="0,0,15,0" />
    <Label>Status:</Label>
    <TextBlock Text="{Binding Path=Status}" VerticalAlignment="Center" Margin="0,0,15,0" FontWeight="Bold" />
    <Button Content="Start" Command="{Binding Path=StartCommand}" MinWidth="60" Margin="0,0,5,0" />
    <Button Content="Stop" Command="{Binding Path=StopCommand}" MinWidth="60" />
</StackPanel>
```

Update the explanatory `TextBlock` below it (the one starting "Check any combination..."):

```xml
<TextBlock Grid.Row="2" TextWrapping="Wrap" Margin="0,0,0,5" Foreground="#FF999999"
           Text="Write a script that loops with Wait(ms)/WaitGroupState()/WaitRoomState()/WaitRoomChange() to react to game state. Click Start to run it now (regardless of Auto-start), Stop to halt it. Auto-start scripts begin running automatically when a tab connects to this profile. See Help for the full API." />
```

- [ ] **Step 4: Add a status-refresh timer to `ScriptsEditDialog.xaml.cs`**

```csharp
namespace Adan.Client.Dialogs
{
    using System;
    using System.IO;
    using System.Windows;
    using System.Windows.Threading;

    using Microsoft.Win32;

    using ViewModel;

    public partial class ScriptsEditDialog : Window
    {
        private readonly DispatcherTimer _statusRefreshTimer;

        public ScriptsEditDialog()
        {
            InitializeComponent();

            _statusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _statusRefreshTimer.Tick += HandleStatusRefreshTick;
            _statusRefreshTimer.Start();
            Closed += (s, e) => _statusRefreshTimer.Stop();
        }

        private void HandleStatusRefreshTick(object sender, EventArgs e)
        {
            var scriptsViewModel = DataContext as ScriptsViewModel;
            if (scriptsViewModel != null)
            {
                scriptsViewModel.RefreshAllStatuses();
            }
        }

        public event EventHandler SaveRequested;

        private void HandleCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void HandleHelpClick(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow { Owner = this };
            helpWindow.Show();
        }

        private void HandleSaveClick(object sender, RoutedEventArgs e)
        {
            var handler = SaveRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void HandleLoadFromFileClick(object sender, RoutedEventArgs e)
        {
            var scriptsViewModel = DataContext as ScriptsViewModel;
            var selectedScript = scriptsViewModel != null ? scriptsViewModel.SelectedScript : null;
            if (selectedScript == null)
            {
                MessageBox.Show(
                    "Select a script in the list first.",
                    "Load script from file",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var fileDialog = new OpenFileDialog
            {
                Filter = "Lua scripts|*.lua|All files|*.*",
                Multiselect = false
            };

            var result = fileDialog.ShowDialog(this);
            if (result.HasValue && result.Value)
            {
                selectedScript.Code = File.ReadAllText(fileDialog.FileName);
            }
        }
    }
}
```

(This is the existing file's content with the `DispatcherTimer` fields/wiring added at the top -- confirm the rest matches what's already there before replacing the whole file, in case it has drifted.)

- [ ] **Step 5: Build**

```bash
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```

Fix any remaining build errors before proceeding -- this is the integration point for Task 5's changes too.

- [ ] **Step 6: Commit** (covers Task 5 + Task 6's files together, since Task 5 didn't build on its own)

```bash
git add Adan.Client/ViewModel/ScriptViewModel.cs Adan.Client/ViewModel/ScriptsViewModel.cs Adan.Client/ViewModel/ProfileOptionsViewModel.cs Adan.Client/Dialogs/ScriptsEditDialog.xaml Adan.Client/Dialogs/ScriptsEditDialog.xaml.cs
git commit -m "feat: Scripts dialog gets live Status + Start/Stop buttons, replacing the Handler checkboxes"
```

---

### Task 7: `MainWindow` periodic `Tick()` driver

**Files:**
- Modify: `Adan.Client/MainWindow.xaml.cs`

- [ ] **Step 1: Find a good place to add a timer**

Read `MainWindow.xaml.cs`'s constructor and look for any existing `DispatcherTimer` setup (the project history mentions a "BlinkPulse" timer -- search for `DispatcherTimer` to find the established pattern and add this alongside it, not as a one-off).

- [ ] **Step 2: Add the scripts-tick timer**

```csharp
private readonly DispatcherTimer _scriptsTickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
```

In the constructor (wherever other timers are started):

```csharp
_scriptsTickTimer.Tick += HandleScriptsTickTimer;
_scriptsTickTimer.Start();
```

Add the handler:

```csharp
private void HandleScriptsTickTimer(object sender, EventArgs e)
{
    foreach (var rootModel in _allRootModels)
    {
        rootModel.ScriptHost.Tick();
    }
}
```

150ms keeps `Wait(ms)` reasonably responsive (worst-case extra delay ~150ms on top of the requested duration) without adding a meaningfully hot per-frame cost -- this only does work for tabs that actually have a script suspended on a timer; `Tick()` is a no-op scan over a typically-tiny dictionary otherwise.

- [ ] **Step 3: Build**

```bash
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```

- [ ] **Step 4: Commit**

```bash
git add Adan.Client/MainWindow.xaml.cs
git commit -m "feat: drive LuaScriptHost.Tick() from a 150ms DispatcherTimer for every open tab"
```

---

### Task 8: Rewrite Help content for the coroutine model

**Files:**
- Modify: `Adan.Client/ViewModel/HelpTopics.cs`

- [ ] **Step 1: Read the current file in full.**

- [ ] **Step 2: Replace the "Events" topics and the Scripts-dialog description in "Обзор"** with content describing: `Wait(ms)`, `WaitGroupState()`, `WaitRoomState()`, `WaitRoomChange()`, the `while true do ... end` idiom, Start/Stop/Auto-start, `__last_group`/`__last_room_monsters`/`__last_room_id`/`__last_zone_id`/`__last_room` as the globals these functions implicitly read (mention them so curious users understand WHY the Wait functions don't take/return the data directly as call arguments), and that two independent scripts can now both react to the same packet type without colliding (the old limitation is gone). Remove the "Известное ограничение: один скрипт на тип обработчика" topic entirely -- it no longer applies. Write the actual Russian text in this step (this plan does not pre-write it -- the implementer should follow the same tone/structure as the existing file, covering every new function/concept above).

- [ ] **Step 3: Build and manually proofread** (Help content has no automated test -- visually confirm in Task 9's manual pass that it renders and reads correctly).

- [ ] **Step 4: Commit**

```bash
git add Adan.Client/ViewModel/HelpTopics.cs
git commit -m "docs: rewrite help for the coroutine Wait/WaitGroupState/WaitRoomState/WaitRoomChange model"
```

---

### Task 9: Manual verification pass

**Files:** none (verification only)

- [ ] **Step 1: Run the full automated test suite**

```bash
"$MSBUILD" Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
"$VSTEST" "Adan.Client.Common.Tests/bin/Debug/net48/Adan.Client.Common.Tests.dll"
```

Expected: all tests pass (exact count depends on which Task-3 deletions/rewrites happened, but there should be zero failures and zero references to removed APIs).

- [ ] **Step 2: Rebuild and repackage the full client**

```bash
powershell.exe -ExecutionPolicy Bypass -File C:\bot\repos\adan-refactor-clients-workspace\build_client.ps1
```

Confirm `NLua.dll`/`KeraLua.dll`/`lua54.dll` are still in the output (unchanged from earlier in this session, but verify the packaging step wasn't accidentally broken).

- [ ] **Step 3: Verify a one-shot script**

Scripts dialog -> Add -> Code: `SendCommand("ооц hello from a one-shot script")`, leave Auto-start unchecked, click Start. Confirm the command is sent once and Status shows `Finished`.

- [ ] **Step 4: Verify a `Wait`-looping script**

```lua
counter = 0
while true do
    counter = counter + 1
    SendCommand("ооц tick " .. counter)
    Wait(2000)
end
```

Click Start. Confirm "tick 1", "tick 2", ... arrive roughly every 2 seconds, and Status shows `WaitingOnTimer` between ticks. Click Stop -- confirm ticks stop arriving.

- [ ] **Step 5: Verify two independent scripts on the same event don't collide**

Create two scripts, both:

```lua
while true do
    WaitGroupState()
    SendCommand("ооц script-NAME saw group update")
end
```

(substitute distinct text per script so you can tell them apart in the output). Start both. Trigger a group-state change (move, take damage). Confirm BOTH scripts' messages appear -- this is the headline capability this whole plan exists to deliver.

- [ ] **Step 6: Verify Auto-start**

Check Auto-start on one script, Save, then reconnect (new tab or disconnect/reconnect this profile). Confirm the script's Status becomes `Running`/`WaitingOnTimer`/etc. without manually clicking Start.

- [ ] **Step 7: Verify a runaway script without `Wait` still gets killed**

```lua
while true do end
```

Click Start. Confirm Status becomes `Faulted` quickly (not a frozen client) -- this is the same watchdog protection from earlier in this session, now applied per-coroutine-resume instead of per-`Eval`-call.

---

## Self-review notes (already applied above, recorded for the executor's awareness)

- **`KeraLua.Lua.ToNumber`** is flagged explicitly in Task 2 as unconfirmed by the reflection pass that grounded this plan -- the executor MUST verify the real method name before relying on it, with a documented fallback (`double.Parse(ToString(2))`).
- **`Lua.GetTable`** (Task 1) is similarly flagged as needing verification against the real NLua 1.7.3 API, with the indexer-based fallback (`lua["coroutine"]`) spelled out.
- **Test deletions** (Task 3, Step 6) are listed by exact existing test name so the executor doesn't have to guess which pre-existing tests reference removed APIs.
- **`ProfileOptionsViewModel`'s `ScriptsViewModel` construction site** (Task 6) is the one place this plan's design has a real gap in the existing codebase (no live `RootModel` is available from the clone-based Profiles-edit flow when no tab is connected yet) -- Task 6 names the gap explicitly and gives a concrete, if imperfect, resolution (throwaway host when nothing's connected) rather than leaving it as a TODO.
