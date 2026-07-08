# Lua Action Functions (Variables/Echo/Groups/Status/SendToWindow) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose 9 new Lua-callable functions in `LuaScriptHost` -- `SetVariable`/`ClearVariable`/`GetVariable`, `Echo`, `EnableGroup`/`DisableGroup`, `SetStatus`, `SendToWindow`/`SendToAllWindows` -- mirroring capabilities the trigger/alias Action system already has, so scripts aren't limited to `SendCommand` alone.

**Architecture:** Add a `LuaScriptHostBindings` class (one `Action`/`Func` delegate field per capability, all nullable, no-op when unset) and a new `LuaScriptHost(LuaScriptHostBindings)` constructor, registering one Lua-callable C# wrapper method per binding (same pattern `SendCommand` already uses: register the function AFTER the sandbox sweep so it survives). The two existing constructors (`LuaScriptHost()` and `LuaScriptHost(Action<string> sendCommand)`) are kept exactly as-is for backward compatibility with all 29 existing tests -- they internally delegate to the new bindings-based constructor with a bindings object that only has `SendCommand` set. `RootModel`'s real constructor wires every binding to the corresponding existing `RootModel` method (`SetVariableValue`, `ClearVariableValue`, `GetVariableValue`, `EnableGroup`, `DisableGroup`) or a small inline implementation (`Echo` via `OutputToMainWindowMessage`, `SetStatus` via a `#status` text command, `SendToWindow`/`SendToAllWindows` via the existing `_allModels` list + the same `TextCommand`+`FlushOutputQueueCommand` flush pattern `SendCommand` already established). `StartLog`/`StopLog` are explicitly OUT of scope for this plan -- their message types live in the `Adan.Client` project, not `Adan.Client.Common` where `RootModel`/`LuaScriptHost` live, so wiring them needs a different injection point (`ConveyorFactory.cs`, in `Adan.Client`) -- deferred to a follow-up.

**Tech Stack:** Same NLua/KeraLua already in use. No new dependency.

---

## File Structure

- **Create** `Adan.Client.Common/Scripting/LuaScriptHostBindings.cs` -- the delegate-bundle class.
- **Modify** `Adan.Client.Common/Scripting/LuaScriptHost.cs` -- add the bindings-based constructor, the 9 Lua-callable wrapper methods, and their `RegisterFunction` calls.
- **Modify** `Adan.Client.Common/Model/RootModel.cs` -- wire every binding in the networked constructor.
- **Modify** `Adan.Client/ViewModel/HelpTopics.cs` -- document the 9 new functions.
- **Test:** `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs` -- one test per new function, exercising it through a `LuaScriptHostBindings` with a test-recording delegate (no `RootModel` needed -- this is exactly why the bindings indirection exists, it keeps `LuaScriptHost` unit-testable without a live `RootModel`/`MessageConveyor`).

---

## Build/test commands (same as every prior plan this session)

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

**Confirmed RootModel signatures (read directly, not guessed) this plan wires against:**
```csharp
public void SetVariableValue([NotNull] string variableName, [NotNull] string value, bool isSilent)   // RootModel.cs:504
public string GetVariableValue([NotNull] string variableName)                                          // RootModel.cs:536
public void ClearVariableValue([NotNull] string variableName, bool isSilent)                            // RootModel.cs:687
public void EnableGroup([NotNull] string groupName)                                                     // RootModel.cs:754
public void DisableGroup([NotNull] string groupName)                                                    // RootModel.cs:771
public void SendToWindow(string name, IEnumerable<ActionBase> actionsToExecute, ActionExecutionContext)  // RootModel.cs:830 (NOT reused directly -- see Task 3, we send a plain TextCommand instead of an ActionBase list)
public string Name { get { return _name; } }                                                            // RootModel.cs:282
public void PushCommandToConveyor([NotNull] Command command)                                            // RootModel.cs:399 (confirmed in the prior session's plan)
public void PushMessageToConveyor([NotNull] Message message)                                            // RootModel.cs:413 (confirmed in the prior session's plan)
```
`OutputToMainWindowMessage` is in `Adan.Client.Common/Messages/OutputToMainWindowMessage.cs` (confirmed -- NOT in `Adan.Client`, so it's safe to construct directly from `RootModel.cs`). Its constructor shape (confirmed via `OutputToMainWindowAction.cs:100`): `new OutputToMainWindowMessage(text, TextColor, BackgroundColor) { SkipTriggers = true }`, where `TextColor` is the enum in `Adan.Client.Common.Themes` (`TextColor.None` is the default used when an action doesn't set a color explicitly).

---

### Task 1: `LuaScriptHostBindings` + wire all 9 functions into `LuaScriptHost`

**Files:**
- Create: `Adan.Client.Common/Scripting/LuaScriptHostBindings.cs`
- Modify: `Adan.Client.Common/Scripting/LuaScriptHost.cs`
- Modify: `Adan.Client.Common/Adan.Client.Common.csproj`
- Test: `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs`

- [ ] **Step 1: Create the bindings class**

```csharp
// Adan.Client.Common/Scripting/LuaScriptHostBindings.cs
namespace Adan.Client.Common.Scripting
{
    using System;

    /// <summary>
    /// Delegate bundle LuaScriptHost uses to reach RootModel capabilities
    /// without depending on RootModel directly (RootModel HOLDS a
    /// LuaScriptHost, not the other way around -- a direct reference would
    /// be circular). Every field is nullable; LuaScriptHost no-ops (or
    /// returns an empty string, for GetVariable) any binding that's left
    /// unset, the same way the original SendCommand-only constructor
    /// already no-ops when given a null delegate. This is also what keeps
    /// LuaScriptHost unit-testable without a live RootModel: tests construct
    /// a LuaScriptHostBindings with simple recording delegates instead.
    /// </summary>
    public sealed class LuaScriptHostBindings
    {
        public Action<string> SendCommand;
        public Action<string, string> SetVariable;
        public Action<string> ClearVariable;
        public Func<string, string> GetVariable;
        public Action<string> Echo;
        public Action<string> EnableGroup;
        public Action<string> DisableGroup;
        public Action<string> SetStatus;
        public Action<string, string> SendToWindow;
        public Action<string> SendToAllWindows;
    }
}
```

- [ ] **Step 2: Add the `<Compile>` entry**

In `Adan.Client.Common/Adan.Client.Common.csproj`, add `<Compile Include="Scripting\LuaScriptHostBindings.cs" />` next to the other `Scripting\*.cs` entries.

- [ ] **Step 3: Write the failing tests**

Read the CURRENT `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs` first to confirm the file's existing `using` directives and the last test in the file (append after it, don't duplicate usings). Append:

```csharp
[Test]
public void SetVariable_InvokesBoundDelegateWithNameAndValue()
{
    string capturedName = null;
    string capturedValue = null;
    var bindings = new LuaScriptHostBindings
    {
        SetVariable = (name, value) => { capturedName = name; capturedValue = value; }
    };

    using (var host = new LuaScriptHost(bindings))
    {
        host.Eval("SetVariable('charname', 'Тазерал')");
    }

    Assert.That(capturedName, Is.EqualTo("charname"));
    Assert.That(capturedValue, Is.EqualTo("Тазерал"));
}

[Test]
public void ClearVariable_InvokesBoundDelegateWithName()
{
    string capturedName = null;
    var bindings = new LuaScriptHostBindings
    {
        ClearVariable = name => capturedName = name
    };

    using (var host = new LuaScriptHost(bindings))
    {
        host.Eval("ClearVariable('charname')");
    }

    Assert.That(capturedName, Is.EqualTo("charname"));
}

[Test]
public void GetVariable_ReturnsBoundDelegateResult()
{
    var bindings = new LuaScriptHostBindings
    {
        GetVariable = name => name == "charname" ? "Тазерал" : string.Empty
    };

    using (var host = new LuaScriptHost(bindings))
    {
        var result = host.Eval("return GetVariable('charname')");
        Assert.That(result, Is.EqualTo("Тазерал"));
    }
}

[Test]
public void GetVariable_NoBindingSet_ReturnsEmptyStringNotNil()
{
    using (var host = new LuaScriptHost(new LuaScriptHostBindings()))
    {
        var result = host.Eval("return GetVariable('whatever') == ''");
        Assert.That(result, Is.EqualTo(true));
    }
}

[Test]
public void Echo_InvokesBoundDelegateWithText()
{
    string capturedText = null;
    var bindings = new LuaScriptHostBindings
    {
        Echo = text => capturedText = text
    };

    using (var host = new LuaScriptHost(bindings))
    {
        host.Eval("Echo('локальное сообщение')");
    }

    Assert.That(capturedText, Is.EqualTo("локальное сообщение"));
}

[Test]
public void EnableGroup_InvokesBoundDelegateWithName()
{
    string capturedName = null;
    var bindings = new LuaScriptHostBindings
    {
        EnableGroup = name => capturedName = name
    };

    using (var host = new LuaScriptHost(bindings))
    {
        host.Eval("EnableGroup('Heal')");
    }

    Assert.That(capturedName, Is.EqualTo("Heal"));
}

[Test]
public void DisableGroup_InvokesBoundDelegateWithName()
{
    string capturedName = null;
    var bindings = new LuaScriptHostBindings
    {
        DisableGroup = name => capturedName = name
    };

    using (var host = new LuaScriptHost(bindings))
    {
        host.Eval("DisableGroup('Heal')");
    }

    Assert.That(capturedName, Is.EqualTo("Heal"));
}

[Test]
public void SetStatus_InvokesBoundDelegateWithText()
{
    string capturedText = null;
    var bindings = new LuaScriptHostBindings
    {
        SetStatus = text => capturedText = text
    };

    using (var host = new LuaScriptHost(bindings))
    {
        host.Eval("SetStatus('busy')");
    }

    Assert.That(capturedText, Is.EqualTo("busy"));
}

[Test]
public void SendToWindow_InvokesBoundDelegateWithNameAndText()
{
    string capturedName = null;
    string capturedText = null;
    var bindings = new LuaScriptHostBindings
    {
        SendToWindow = (name, text) => { capturedName = name; capturedText = text; }
    };

    using (var host = new LuaScriptHost(bindings))
    {
        host.Eval("SendToWindow('Heal', 'лечи меня')");
    }

    Assert.That(capturedName, Is.EqualTo("Heal"));
    Assert.That(capturedText, Is.EqualTo("лечи меня"));
}

[Test]
public void SendToAllWindows_InvokesBoundDelegateWithText()
{
    string capturedText = null;
    var bindings = new LuaScriptHostBindings
    {
        SendToAllWindows = text => capturedText = text
    };

    using (var host = new LuaScriptHost(bindings))
    {
        host.Eval("SendToAllWindows('всем привет')");
    }

    Assert.That(capturedText, Is.EqualTo("всем привет"));
}

[Test]
public void AllNewFunctions_NoBindingsSet_DoNotThrow()
{
    using (var host = new LuaScriptHost(new LuaScriptHostBindings()))
    {
        Assert.DoesNotThrow(() => host.Eval(@"
            SetVariable('a', 'b')
            ClearVariable('a')
            Echo('x')
            EnableGroup('g')
            DisableGroup('g')
            SetStatus('s')
            SendToWindow('w', 't')
            SendToAllWindows('t')
        "));
    }
}

[Test]
public void ExistingSendCommandConstructor_StillWorks()
{
    // Backward-compat check: the original single-delegate constructor
    // must keep working exactly as before -- this plan must not break
    // any of the 29 pre-existing tests built against it.
    string sentCommand = null;
    using (var host = new LuaScriptHost(cmd => sentCommand = cmd))
    {
        host.Eval("SendCommand('атаковать крысу')");
        Assert.That(sentCommand, Is.EqualTo("атаковать крысу"));
    }
}
```

- [ ] **Step 4: Run to verify failure**

Build the test project. Expected: build errors -- `LuaScriptHostBindings` doesn't exist, `LuaScriptHost(LuaScriptHostBindings)` constructor doesn't exist.

- [ ] **Step 5: Implement**

Read the CURRENT full `Adan.Client.Common/Scripting/LuaScriptHost.cs` constructor region first (it's been modified by ~12 prior tasks this session -- confirm the exact current body of both existing constructors, the `CreateSandboxedState()` call, the `SendCommand` registration line, and the `_hookFunction`/`SetHook` setup before editing). The current constructors look like:

```csharp
public LuaScriptHost()
    : this(null)
{
}

public LuaScriptHost(Action<string> sendCommand)
{
    _sendCommand = sendCommand ?? (_ => { });
    _lua = CreateSandboxedState();
    // ... pcall guard, SendCommand registration, hook setup, runtime prelude ...
}
```

Replace this constructor pair (keeping every line of the EXISTING constructor body that builds the sandbox/watchdog/prelude -- only the `_sendCommand`-specific lines and the constructor signatures change) with three constructors:

```csharp
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

    // ... KEEP every existing line here exactly as it is in the current
    // file: the __watchdog_timeout registration, InstallPcallGuard(),
    // the SendCommand RegisterFunction call (still needed -- it's now
    // backed by _bindings.SendCommand instead of a dedicated field, see
    // Step 6), _hookFunction/SetHook setup, and the runtime-prelude
    // DoString call defining Wait/WaitGroupState/WaitRoomState/
    // WaitRoomChange. Do not remove or reorder any of that -- this task
    // only ADDS the bindings field and the 8 new RegisterFunction calls
    // (Step 7) alongside what's already there.
}
```

- [ ] **Step 6: Replace the `_sendCommand` field with `_bindings`, update `SendCommandFromLua`**

Find:
```csharp
private readonly Action<string> _sendCommand;
```
Replace with:
```csharp
private readonly LuaScriptHostBindings _bindings;
```

Find `SendCommandFromLua` (the method registered as the Lua global `SendCommand`):
```csharp
public void SendCommandFromLua(string command)
{
    _sendCommand(command);
}
```
Replace with:
```csharp
public void SendCommandFromLua(string command)
{
    if (_bindings.SendCommand != null)
    {
        _bindings.SendCommand(command);
    }
}
```

- [ ] **Step 7: Add the 8 new Lua-callable wrapper methods and their registrations**

Add these methods near `SendCommandFromLua`:

```csharp
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
public void SendToAllWindowsFromLua(string text)
{
    if (_bindings.SendToAllWindows != null)
    {
        _bindings.SendToAllWindows(text);
    }
}
```

Find the existing `SendCommand` registration line:
```csharp
_lua.RegisterFunction("SendCommand", this, GetType().GetMethod(nameof(SendCommandFromLua)));
```
Add these 8 lines immediately after it (same place in the constructor -- AFTER `CreateSandboxedState()`'s sweep has already run, same reasoning as the existing comment above the `SendCommand` line already explains):

```csharp
_lua.RegisterFunction("SetVariable", this, GetType().GetMethod(nameof(SetVariableFromLua)));
_lua.RegisterFunction("ClearVariable", this, GetType().GetMethod(nameof(ClearVariableFromLua)));
_lua.RegisterFunction("GetVariable", this, GetType().GetMethod(nameof(GetVariableFromLua)));
_lua.RegisterFunction("Echo", this, GetType().GetMethod(nameof(EchoFromLua)));
_lua.RegisterFunction("EnableGroup", this, GetType().GetMethod(nameof(EnableGroupFromLua)));
_lua.RegisterFunction("DisableGroup", this, GetType().GetMethod(nameof(DisableGroupFromLua)));
_lua.RegisterFunction("SetStatus", this, GetType().GetMethod(nameof(SetStatusFromLua)));
_lua.RegisterFunction("SendToWindow", this, GetType().GetMethod(nameof(SendToWindowFromLua)));
_lua.RegisterFunction("SendToAllWindows", this, GetType().GetMethod(nameof(SendToAllWindowsFromLua)));
```

- [ ] **Step 8: Run tests to verify they pass**

Expected: 29 (pre-existing) + 13 new = 42 total, all passing, including `ExistingSendCommandConstructor_StillWorks` (confirms backward compat actually holds, not just compiles).

- [ ] **Step 9: Commit**

```bash
git add Adan.Client.Common/Scripting/LuaScriptHostBindings.cs Adan.Client.Common/Scripting/LuaScriptHost.cs Adan.Client.Common/Adan.Client.Common.csproj Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs
git commit -m "feat: add SetVariable/ClearVariable/GetVariable/Echo/EnableGroup/DisableGroup/SetStatus/SendToWindow/SendToAllWindows to LuaScriptHost"
```

Do NOT touch or stage any other files (pre-existing unrelated dirty files in this repo must be left alone).

---

### Task 2: Wire `RootModel` -- variables, Echo, groups, status

**Files:**
- Modify: `Adan.Client.Common/Model/RootModel.cs`

No new test -- `RootModel` remains untested per this codebase's established pattern (WPF/networking dependencies); verified manually in Task 4.

- [ ] **Step 1: Read the current networked constructor in full**

Find the exact current body of `public RootModel([NotNull] MessageConveyor conveyor, ProfileHolder profile, IList<RootModel> allModels)`, specifically the `_scriptHost = new Scripting.LuaScriptHost(...)` call (it currently passes a single `Action<string>` lambda for `SendCommand`, added/modified across several prior tasks this session -- confirm its exact current body, including the `FlushOutputQueueCommand` push, before editing).

- [ ] **Step 2: Replace the single-delegate constructor call with a full bindings object**

Replace:
```csharp
_scriptHost = new Scripting.LuaScriptHost(
    command =>
    {
        conveyor.PushCommand(new Commands.TextCommand(command));
        conveyor.PushCommand(Commands.FlushOutputQueueCommand.Instance);
    });
```
with:
```csharp
_scriptHost = new Scripting.LuaScriptHost(new Scripting.LuaScriptHostBindings
{
    SendCommand = command =>
    {
        conveyor.PushCommand(new Commands.TextCommand(command));
        conveyor.PushCommand(Commands.FlushOutputQueueCommand.Instance);
    },
    SetVariable = (name, value) => SetVariableValue(name, value, true),
    ClearVariable = name => ClearVariableValue(name, true),
    GetVariable = name => GetVariableValue(name),
    Echo = text => PushMessageToConveyor(new OutputToMainWindowMessage(text, Themes.TextColor.None, Themes.TextColor.None) { SkipTriggers = true }),
    EnableGroup = name => EnableGroup(name),
    DisableGroup = name => DisableGroup(name),
    SetStatus = text =>
    {
        conveyor.PushCommand(new Commands.TextCommand("#status " + text));
        conveyor.PushCommand(Commands.FlushOutputQueueCommand.Instance);
    },
});
```

Check the file's current `using` directives for `Adan.Client.Common.Themes` (needed for `Themes.TextColor.None` -- if the file already has `using Themes;` or similar, just write `TextColor.None` without the `Themes.` prefix; read the actual top-of-file usings before deciding which form compiles). `SetVariableValue`/`ClearVariableValue`/`GetVariableValue`/`EnableGroup`/`DisableGroup` are all instance methods already on this same `RootModel` class -- called here without a `this.` qualifier the same way other code in this file already calls them.

`SendToWindow`/`SendToAllWindows` are NOT in this step -- they're added in Task 3, since they need `_allModels`, which is a separate, slightly more involved piece worth its own task/commit.

- [ ] **Step 3: Build**

```bash
"$MSBUILD" Adan.Client.Common/Adan.Client.Common.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```

Expected: zero errors.

- [ ] **Step 4: Commit**

```bash
git add Adan.Client.Common/Model/RootModel.cs
git commit -m "feat: wire SetVariable/ClearVariable/GetVariable/Echo/EnableGroup/DisableGroup/SetStatus bindings on RootModel"
```

---

### Task 3: Wire `SendToWindow`/`SendToAllWindows`

**Files:**
- Modify: `Adan.Client.Common/Model/RootModel.cs`

- [ ] **Step 1: Read `RootModel.SendToWindow`/`SendToAllWindows` (the EXISTING action-based methods, around line 830-865) in full** to confirm the exact `_allModels` iteration/matching pattern (`w => w._name == name` or `w.Name == name` -- both are equivalent since `Name` just returns `_name`, but match whichever style the surrounding code already uses for consistency) and the `FlushOutputQueueCommand` push at the end of each.

- [ ] **Step 2: Add the two new bindings to the SAME `LuaScriptHostBindings` object from Task 2**

Extend the object initializer added in Task 2 with two more properties:

```csharp
SendToWindow = (name, text) =>
{
    var targetModel = _allModels.FirstOrDefault(w => w.Name == name);
    if (targetModel != null)
    {
        targetModel.PushCommandToConveyor(new Commands.TextCommand(text));
        targetModel.PushCommandToConveyor(Commands.FlushOutputQueueCommand.Instance);
    }
},
SendToAllWindows = text =>
{
    foreach (var targetModel in _allModels)
    {
        targetModel.PushCommandToConveyor(new Commands.TextCommand(text));
        targetModel.PushCommandToConveyor(Commands.FlushOutputQueueCommand.Instance);
    }
},
```

Confirm `System.Linq` is already imported in this file (needed for `FirstOrDefault`) -- it almost certainly is already, given the file's existing use of LINQ elsewhere (e.g. `RecalculatedEnabledTriggersPriorities` uses `.Where`/`.SelectMany`), but check before assuming.

- [ ] **Step 3: Build**

```bash
"$MSBUILD" Adan.Client.Common/Adan.Client.Common.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```

- [ ] **Step 4: Build `Adan.Client` too** (nothing in this task touches it, but confirm nothing else broke):

```bash
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```

- [ ] **Step 5: Commit**

```bash
git add Adan.Client.Common/Model/RootModel.cs
git commit -m "feat: wire SendToWindow/SendToAllWindows bindings on RootModel"
```

---

### Task 4: Document the 9 new functions in Help, manual verification, full build

**Files:**
- Modify: `Adan.Client/ViewModel/HelpTopics.cs`

- [ ] **Step 1: Read the current file in full.** It currently has topics for `Wait(ms)`, `WaitGroupState()`, `WaitRoomState()`, `WaitRoomChange()`, the `__last_*` field tables, `SendCommand(text)`, script management, sandbox, watchdog, and "Чего пока нет".

- [ ] **Step 2: Add a new topic, right after the existing "Функции: SendCommand(text)" topic**, written in the same Russian/structured style as the rest of the file (write the actual prose -- this is the same level of detail the existing `SendCommand` topic has):

Cover, for each function: exact call signature, what it does, that it works from BOTH a trigger-attached script and a Scripts-dialog coroutine (same as `SendCommand`), and one realistic example each:
- `SetVariable(name, value)` / `ClearVariable(name)` / `GetVariable(name)` -- note these are the SAME variables visible elsewhere in the client (e.g. usable as `$varname` in other triggers/aliases), not something private to Lua.
- `Echo(text)` -- the key distinction from `SendCommand`: text appears locally in the output window, the SERVER never sees it. Good for "scripts talking to the player" without spamming the game.
- `EnableGroup(name)` / `DisableGroup(name)` -- toggling a whole Group of triggers/aliases/hotkeys on/off by name (same Groups visible in the Groups editor).
- `SetStatus(text)` -- sends `#status <text>` to the server.
- `SendToWindow(windowName, text)` / `SendToAllWindows(text)` -- cross-tab coordination: send a command to ANOTHER open tab (identified by its profile/character name, the same name shown on the tab) or to every open tab at once. Note explicitly: there's no way for a script to discover what tabs/names are currently open -- the name has to be known/typed in advance, exactly the same limitation the existing SendToWindowAction (in the Triggers/Aliases editor) already has.

Also update the "Чего пока нет" topic: remove any line that said `SendCommand` was "the only function added beyond standard Lua" if such a line exists (read the actual current text first -- the exact wording may differ from this description), since that's no longer true.

- [ ] **Step 3: Build**

```bash
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```

- [ ] **Step 4: Run the full automated test suite**

```bash
"$MSBUILD" Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
"$VSTEST" "Adan.Client.Common.Tests/bin/Debug/net48/Adan.Client.Common.Tests.dll"
```

Expected: 42 total, 42 passed (per Task 1's count).

- [ ] **Step 5: Rebuild and repackage the full client**

```bash
powershell.exe -ExecutionPolicy Bypass -File C:\bot\repos\adan-refactor-clients-workspace\build_client.ps1
```

- [ ] **Step 6: Manual verification**

In a Scripts-dialog coroutine or a trigger's Lua action, run each:
- `SetVariable("test_var", "hello")` then check it's visible as `$test_var` somewhere the client already substitutes variables (e.g. an `OutputToMainWindowAction`'s text field, or just `Echo(GetVariable("test_var"))` right after).
- `Echo("видно только мне")` -- confirm it appears in the output window and does NOT show up as something sent to the server (no echo of an outgoing command).
- `EnableGroup`/`DisableGroup` on a real Group name from your profile -- confirm the Group's enabled checkbox in the Groups editor actually flips.
- `SetStatus("test")` -- confirm `#status test` is sent (visible the same way any `#status` command's effect is normally visible).
- With two tabs open (two different character names), from tab A run `SendToWindow("<tab B's name>", "осмотреться")` -- confirm tab B executes it. Then `SendToAllWindows("улыбнуться")` from tab A -- confirm BOTH tabs execute it.

- [ ] **Step 7: Commit**

```bash
git add Adan.Client/ViewModel/HelpTopics.cs
git commit -m "docs: document SetVariable/ClearVariable/GetVariable/Echo/EnableGroup/DisableGroup/SetStatus/SendToWindow/SendToAllWindows"
```

---

## Follow-up (explicitly out of scope here)

- **`StartLog(filename)`/`StopLog()`** -- `StartLoggingMessage`/`StopLoggingMessage` live in `Adan.Client/Messages/`, not `Adan.Client.Common`, so `RootModel` (in Common) can't construct them directly the way it can `OutputToMainWindowMessage`. Wiring these needs a binding set from `Adan.Client`-side code (e.g. `ConveyorFactory.CreateNew`, which already runs after `RootModel`/`LuaScriptHost` exist and lives in the right project) rather than from `RootModel`'s own constructor -- a different, slightly bigger change than this plan's scope.
- **Enumerating open tab names from Lua** -- `SendToWindow`/`SendToAllWindows` require knowing a target tab's name in advance (typed, not discovered) -- same limitation the existing `SendToWindowAction` UI already has, not a regression introduced here, but a real gap if cross-tab coordination scripts become common.
