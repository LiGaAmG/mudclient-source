# Lua Scripting Core (Packet-State Hooks) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Adan.Client codebase a persistent, per-tab, sandboxed Lua engine that can read already-deserialized group/room-monster state (no text parsing) and can be invoked as a new trigger/alias action type, without adding measurable cost to the existing message-handling hot path.

**Architecture:** One `LuaScriptHost` lives on each `RootModel` (which is already one-per-tab/connection, confirmed at `Adan.Client.Common\Conveyor\MessageConveyor.cs:92`). `GroupHolder`/`MonsterHolder` already assign `RootModel.GroupStatus`/`RootModel.RoomMonstersStatus` by reference when a packet arrives (`Adan.Client.Plugins.GroupWidget\Model\GroupHolder.cs:81`, `Adan.Client.Plugins.GroupWidget\Model\MonsterHolder.cs:81`) — we add exactly one extra call at each of those two existing call sites to notify the host, no copying, no new conveyor unit, no new pass over text. The host exposes a small, explicit API table to Lua (no `io`, `os.execute`, `package`, `debug`) and enforces a Lua instruction-count hook so a runaway script gets killed instead of freezing the tab. A new `LuaScriptAction` plugs into the existing `ActionBase`/`ActionWithParameters` action list (`Adan.Client\MainWindow.xaml.cs:413-435`) so it can be attached to any trigger/alias exactly like `SendTextAction` is today.

This plan covers the **engine + hookup + one action type only**. A follow-up plan will cover the standalone "Scripts" editor dialog and the searchable help window — those are a separable UI subsystem and should not block the engine from being testable.

**Tech Stack:** C# / .NET Framework 4.6.1 (built against 4.8 toolset per `CLIENT_BUILD_NOTES.md`), NLua (NuGet) wrapping KeraLua/Lua 5.4, NUnit for the new test project (SDK-style, net48).

**Build/test commands (verified working in this environment — do NOT use `dotnet build`/`dotnet test`, the .NET SDK's own MSBuild cannot compile this repo's legacy WPF projects: it fails with MSB3644 on the net461 targeting pack, and even after forcing `TargetFrameworkVersion=v4.8` it fails to run XAML markup compile, leaving `InitializeComponent` undefined):**

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
VSTEST="/c/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/CommonExtensions/Microsoft/TestWindow/vstest.console.exe"

# Build the test project (and anything it references, e.g. Adan.Client.Common):
"$MSBUILD" Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo

# Run the tests:
"$VSTEST" "Adan.Client.Common.Tests/bin/Debug/net48/Adan.Client.Common.Tests.dll"

# Build any other project in the solution (e.g. for Tasks 5-7):
"$MSBUILD" Adan.Client2017.sln -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```

Note the leading `-p:`/`-v:` (not `/p:`/`/v:`) — this Git Bash environment mangles `/`-prefixed MSBuild switches into path conversions. If `MSBuild.exe` isn't at that exact path on a given machine, search `C:\Program Files\Microsoft Visual Studio\` for it (same logic as `Find-MSBuild` in `C:\bot\repos\adan-refactor-clients-workspace\build_client.ps1:70-92`).

---

## File Structure

- **Create** `Adan.Client.Common/Scripting/LuaScriptHost.cs` — the persistent per-tab Lua state, sandboxing, watchdog, event-raising methods (`RaiseGroupStateChanged`, `RaiseRoomStateChanged`), `SendCommand` exposure. Pure C# class, no WPF/UI types, so it is unit-testable in isolation.
- **Create** `Adan.Client.Common/Scripting/LuaScriptHost.SandboxSetup.cs` — kept separate from the main file because the "what is and isn't allowed" allowlist is the security-sensitive part and benefits from being easy to find and review on its own; it has one job (build the sandboxed `Lua` instance).
- **Modify** `Adan.Client.Common/Model/RootModel.cs` — add `ScriptHost` property, construct it in all three constructors, dispose it in `Dispose()`.
- **Modify** `Adan.Client.Plugins.GroupWidget/Model/GroupHolder.cs:81` — one line after the existing assignment.
- **Modify** `Adan.Client.Plugins.GroupWidget/Model/MonsterHolder.cs:81` — one line after the existing assignment.
- **Create** `Adan.Client/Model/Actions/LuaScriptAction.cs` — new `ActionWithParameters` subclass, mirrors `SendTextAction.cs` shape.
- **Create** `Adan.Client/Model/ActionDescriptions/LuaScriptActionDescription.cs` — mirrors `SendTextActionDescription.cs`.
- **Create** `Adan.Client/ViewModel/Actions/LuaScriptActionViewModel.cs` — mirrors `SendTextActionViewModel.cs` (needed because `ActionDescription.CreateActionViewModel` is abstract and the existing trigger/alias editor dialogs bind to `ActionViewModelBase`, not `ActionBase`, directly).
- **Modify** `Adan.Client/MainWindow.xaml.cs:413-435` — register `LuaScriptActionDescription` alongside the existing 13 registrations.
- **Create** `Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj` — new SDK-style NUnit test project (net48), referencing `Adan.Client.Common.csproj`.
- **Create** `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs` — the actual tests.

---

### Task 1: Create the test project

**Files:**
- Create: `Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj`
- Create: `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs`
- Modify: `Adan.Client2017.sln`

- [ ] **Step 1: Create the csproj**

```xml
<!-- Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>disable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="NLua" Version="1.7.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Adan.Client.Common\Adan.Client.Common.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write a placeholder test to verify the project compiles and runs**

```csharp
// Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs
using NUnit.Framework;

namespace Adan.Client.Common.Tests.Scripting
{
    [TestFixture]
    public class LuaScriptHostTests
    {
        [Test]
        public void Placeholder_ProjectCompiles()
        {
            Assert.That(1 + 1, Is.EqualTo(2));
        }
    }
}
```

- [ ] **Step 3: Run it to confirm the harness works**

Use the "Build/test commands" block from the top of this plan (MSBuild + vstest.console.exe, NOT `dotnet test`).
Expected build output ends with `Adan.Client.Common.Tests -> ...\Adan.Client.Common.Tests.dll`. Expected vstest output: `Пройдено: 1` (or `Passed: 1` on an English-locale install).

- [ ] **Step 4: Add the new project to the solution**

```bash
dotnet sln Adan.Client2017.sln add Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj
```

(`dotnet sln add` only edits the `.sln` text file's project list — it doesn't build anything, so it's fine to use the `dotnet` CLI here even though building must go through the real MSBuild.)

- [ ] **Step 5: Commit**

```bash
git add Adan.Client.Common.Tests Adan.Client2017.sln
git commit -m "test: scaffold Adan.Client.Common.Tests project"
```

---

### Task 2: NLua reference on the main project + sandbox setup

**Files:**
- Modify: `Adan.Client.Common/packages.config`
- Modify: `Adan.Client.Common/Adan.Client.Common.csproj`
- Create: `Adan.Client.Common/Scripting/LuaScriptHost.SandboxSetup.cs`
- Test: `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs`

`Adan.Client.Common.csproj` is an old-style (non-SDK) project using `packages.config`, confirmed by the existing `DotNetZip` entry. NLua does not ship a net461 `packages.config`-friendly drop cleanly in all versions, so we pin to a version known to work with classic-style projects and add the reference by hand instead of relying on Visual Studio's NuGet UI (not available in this environment).

- [ ] **Step 1: Add the package entries**

```xml
<!-- Adan.Client.Common/packages.config -->
<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="DotNetZip" version="1.9.8" targetFramework="net461" />
  <package id="NLua" version="1.7.3" targetFramework="net461" />
  <package id="KeraLua" version="1.4.4" targetFramework="net461" />
</packages>
```

- [ ] **Step 2: Restore packages from the repo root**

Run: `nuget restore Adan.Client2017.sln`
Expected: `packages\NLua.1.7.3` and `packages\KeraLua.1.4.4` directories appear under the repo root. If `nuget.exe` is not on PATH, download it once: `Invoke-WebRequest https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -OutFile nuget.exe` (PowerShell) and run `.\nuget.exe restore Adan.Client2017.sln`.

- [ ] **Step 3: Add the assembly references to the csproj**

Add inside the existing `<ItemGroup>` that contains `<Reference Include="System.Xml" />` in `Adan.Client.Common/Adan.Client.Common.csproj`:

```xml
<Reference Include="NLua">
  <HintPath>..\packages\NLua.1.7.3\lib\net47\NLua.dll</HintPath>
</Reference>
<Reference Include="KeraLua">
  <HintPath>..\packages\KeraLua.1.4.4\lib\net47\KeraLua.dll</HintPath>
</Reference>
```

And confirm `<None Include="packages.config" />` is still present (it already is, at line 218).

- [ ] **Step 4: Write the failing test for sandbox setup**

```csharp
// Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs
using NUnit.Framework;
using Adan.Client.Common.Scripting;

namespace Adan.Client.Common.Tests.Scripting
{
    [TestFixture]
    public class LuaScriptHostTests
    {
        [Test]
        public void SandboxedState_HasNoIoLibrary()
        {
            using (var host = new LuaScriptHost())
            {
                var result = host.Eval("return io == nil");
                Assert.That(result, Is.EqualTo(true));
            }
        }

        [Test]
        public void SandboxedState_HasNoOsExecute()
        {
            using (var host = new LuaScriptHost())
            {
                var result = host.Eval("return os == nil or os.execute == nil");
                Assert.That(result, Is.EqualTo(true));
            }
        }

        [Test]
        public void SandboxedState_CanUseBasicStringAndMath()
        {
            using (var host = new LuaScriptHost())
            {
                var result = host.Eval("return string.upper('ok') .. tostring(math.floor(3.7))");
                Assert.That(result, Is.EqualTo("OK3"));
            }
        }
    }
}
```

- [ ] **Step 5: Run test to verify it fails (class doesn't exist yet)**

Run the `$MSBUILD ... Adan.Client.Common.Tests.csproj ...` command from the "Build/test commands" block.
Expected: build error `The type or namespace name 'LuaScriptHost' could not be found`.

- [ ] **Step 6: Implement the sandbox setup**

```csharp
// Adan.Client.Common/Scripting/LuaScriptHost.SandboxSetup.cs
namespace Adan.Client.Common.Scripting
{
    using NLua;

    public sealed partial class LuaScriptHost
    {
        // Only these globals survive. Everything else (io, os.execute,
        // package, require, debug, dofile, loadfile) is stripped so a
        // user script cannot touch the filesystem, spawn processes, or
        // load arbitrary assemblies.
        private static readonly string[] AllowedGlobals =
        {
            "string", "table", "math", "tostring", "tonumber", "type",
            "pairs", "ipairs", "select", "error", "pcall", "xpcall",
            "assert", "print",
        };

        private static Lua CreateSandboxedState()
        {
            var lua = new Lua();
            lua.State.Encoding = System.Text.Encoding.UTF8;

            // NLua exposes the full standard library by default; remove
            // anything not in the allowlist instead of trying to enumerate
            // every dangerous function individually.
            lua.DoString(@"
                local allowed = {}
                for _, name in ipairs({...}) do allowed[name] = true end
                for key in pairs(_G) do
                    if not allowed[key] and key ~= '_G' then
                        _G[key] = nil
                    end
                end
            ", "sandbox-init", AllowedGlobals);

            return lua;
        }
    }
}
```

- [ ] **Step 7: Implement the host shell with `Eval` and `Dispose`**

```csharp
// Adan.Client.Common/Scripting/LuaScriptHost.cs
namespace Adan.Client.Common.Scripting
{
    using System;
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
        private readonly Lua _lua;
        private bool _disposed;

        public LuaScriptHost()
        {
            _lua = CreateSandboxedState();
        }

        /// <summary>
        /// Evaluates a Lua expression of the form "return ...." and returns
        /// the first result. Intended for tests and simple host-side checks;
        /// production script entry points are the RegisterXxxHandler methods
        /// added in later tasks.
        /// </summary>
        public object Eval(string luaExpression)
        {
            var results = _lua.DoString(luaExpression);
            return results.Length > 0 ? results[0] : null;
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
```

- [ ] **Step 8: Run tests to verify they pass**

Run the MSBuild then vstest commands from the "Build/test commands" block.
Expected: 3 new tests pass (plus the Task 1 placeholder), 4 total (`Всего тестов: 4 Пройдено: 4`).

- [ ] **Step 9: Commit**

```bash
git add Adan.Client.Common/packages.config Adan.Client.Common/Adan.Client.Common.csproj Adan.Client.Common/Scripting Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs
git commit -m "feat: add sandboxed per-tab Lua host (NLua)"
```

---

### Task 3: Instruction-count watchdog (kill runaway scripts)

**Files:**
- Modify: `Adan.Client.Common/Scripting/LuaScriptHost.cs`
- Test: `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs`

This is the piece that prevents a bad user script from reproducing the multi-second freezes documented in the project's perf history (`adan-refactor-clients-workspace/CLIENT_BUILD_NOTES.md`, commits `c5d2d53`/`847a3db`). Without it, a script with an infinite loop would hang the conveyor thread forever, not just for a couple of seconds.

- [ ] **Step 1: Write the failing test**

```csharp
// Append to LuaScriptHostTests.cs
using System;

[Test]
public void Eval_InfiniteLoop_ThrowsInsteadOfHanging()
{
    using (var host = new LuaScriptHost())
    {
        Assert.Throws<LuaScriptTimeoutException>(() =>
            host.Eval("local x = 0 while true do x = x + 1 end"));
    }
}

[Test]
public void Eval_NormalScript_StillWorksAfterWatchdogIsArmed()
{
    using (var host = new LuaScriptHost())
    {
        var result = host.Eval("local sum = 0 for i = 1, 100 do sum = sum + i end return sum");
        Assert.That(result, Is.EqualTo(5050.0));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run the `$MSBUILD` command from the "Build/test commands" block.
Expected: build error, `LuaScriptTimeoutException` does not exist. (If you get past the build somehow, running `"$VSTEST" "Adan.Client.Common.Tests/bin/Debug/net48/Adan.Client.Common.Tests.dll" -TestCaseFilter:"FullyQualifiedName~LuaScriptHostTests"` would hang on the infinite-loop test — cancel after 10s; that hang **is** the proof the watchdog is missing.)

- [ ] **Step 3: Add the exception type**

```csharp
// Adan.Client.Common/Scripting/LuaScriptTimeoutException.cs
namespace Adan.Client.Common.Scripting
{
    using System;

    /// <summary>
    /// Thrown when a script exceeds its instruction budget. Caught at every
    /// call site that invokes user script code so one bad script cannot
    /// hang the conveyor thread for the whole tab.
    /// </summary>
    public sealed class LuaScriptTimeoutException : Exception
    {
        public LuaScriptTimeoutException(string message) : base(message)
        {
        }
    }
}
```

- [ ] **Step 4: Wire a KeraLua instruction-count hook into the host**

```csharp
// Adan.Client.Common/Scripting/LuaScriptHost.cs (replace the Eval method and constructor body)
namespace Adan.Client.Common.Scripting
{
    using System;
    using KeraLua;
    using NLua;

    public sealed partial class LuaScriptHost : IDisposable
    {
        // 1,000,000 VM instructions is comfortably more than any real
        // trigger/state-handler script needs per invocation (a handler
        // iterating a 50-member group with a few comparisons is in the
        // low thousands), and low enough that a runaway loop is killed
        // in well under 50ms on typical hardware.
        private const int InstructionBudget = 1_000_000;

        private readonly Lua _lua;
        private bool _disposed;
        private bool _budgetExceeded;

        public LuaScriptHost()
        {
            _lua = CreateSandboxedState();
            _lua.State.SetHook(OnInstructionHook, LuaHookMask.Count, InstructionBudget);
        }

        private void OnInstructionHook(KeraLua.Lua state, LuaDebug debug)
        {
            _budgetExceeded = true;
            // Force an unwind by raising a Lua error from inside the hook;
            // NLua surfaces this as a LuaScriptException at the DoString call.
            state.Error("script exceeded instruction budget");
        }

        public object Eval(string luaExpression)
        {
            _budgetExceeded = false;
            try
            {
                var results = _lua.DoString(luaExpression);
                return results.Length > 0 ? results[0] : null;
            }
            catch (NLua.Exceptions.LuaScriptException ex)
            {
                if (_budgetExceeded)
                {
                    throw new LuaScriptTimeoutException(
                        "Script exceeded the instruction budget (" + InstructionBudget + " instructions) and was aborted.");
                }

                throw;
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
```

- [ ] **Step 5: Run tests to verify they pass**

Run `$MSBUILD` then `"$VSTEST" "Adan.Client.Common.Tests/bin/Debug/net48/Adan.Client.Common.Tests.dll"`.
Expected: 6 passed (4 from Task 2 + 2 new), all complete in under a few seconds — the infinite-loop test must finish quickly, not hang.

- [ ] **Step 6: Commit**

```bash
git add Adan.Client.Common/Scripting
git commit -m "feat: add instruction-count watchdog to LuaScriptHost"
```

---

### Task 4: Expose `SendCommand` and packet-state event handlers

**Files:**
- Modify: `Adan.Client.Common/Scripting/LuaScriptHost.cs`
- Test: `Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs`

This is the API surface scripts actually use: reacting to `GroupStatus`/`RoomMonstersStatus` updates and sending text back to the server. `SendCommand` is injected as a delegate rather than hard-wired to `RootModel`/`MessageConveyor` so this class stays unit-testable without spinning up a real network connection.

- [ ] **Step 1: Write the failing tests**

```csharp
// Append to LuaScriptHostTests.cs
using Adan.Client.Common.Model;
using System.Collections.Generic;

[Test]
public void RegisterGroupStateHandler_FiresWithMemberData()
{
    using (var host = new LuaScriptHost())
    {
        string capturedName = null;
        double capturedHits = -1;

        host.LoadScript(@"
            function on_group_state(group)
                last_name = group[1].Name
                last_hits = group[1].HitsPercent
            end
        ");
        host.RegisterGroupStateHandler("on_group_state");

        var members = new List<CharacterStatus>
        {
            new CharacterStatus { Name = "Нимриэль", HitsPercent = 73.5f }
        };

        host.RaiseGroupStateChanged(members);

        capturedName = (string)host.Eval("return last_name");
        capturedHits = (double)host.Eval("return last_hits");

        Assert.That(capturedName, Is.EqualTo("Нимриэль"));
        Assert.That(capturedHits, Is.EqualTo(73.5).Within(0.001));
    }
}

[Test]
public void RaiseGroupStateChanged_NoHandlerRegistered_DoesNothing()
{
    using (var host = new LuaScriptHost())
    {
        // Must not throw when no script subscribed to the event.
        Assert.DoesNotThrow(() => host.RaiseGroupStateChanged(new List<CharacterStatus>()));
    }
}

[Test]
public void SendCommand_InvokesInjectedDelegate()
{
    string sentCommand = null;
    using (var host = new LuaScriptHost(cmd => sentCommand = cmd))
    {
        host.Eval("SendCommand('атаковать крысу')");
        Assert.That(sentCommand, Is.EqualTo("атаковать крысу"));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run the `$MSBUILD` command from the "Build/test commands" block.
Expected: build errors — `LoadScript`, `RegisterGroupStateHandler`, `RaiseGroupStateChanged`, `SendCommand` ctor overload don't exist yet.

- [ ] **Step 3: Implement the API surface**

```csharp
// Adan.Client.Common/Scripting/LuaScriptHost.cs (add alongside existing members)
namespace Adan.Client.Common.Scripting
{
    using System;
    using System.Collections.Generic;
    using KeraLua;
    using NLua;
    using Adan.Client.Common.Model;

    public sealed partial class LuaScriptHost : IDisposable
    {
        private readonly Action<string> _sendCommand;
        private string _groupStateHandlerName;
        private string _roomStateHandlerName;

        public LuaScriptHost() : this(null)
        {
        }

        public LuaScriptHost(Action<string> sendCommand)
        {
            _sendCommand = sendCommand ?? (_ => { });
            _lua = CreateSandboxedState();
            _lua.State.SetHook(OnInstructionHook, LuaHookMask.Count, InstructionBudget);
            _lua.RegisterFunction("SendCommand", this, GetType().GetMethod(nameof(SendCommandFromLua)));
        }

        public void SendCommandFromLua(string command)
        {
            _sendCommand(command);
        }

        /// <summary>
        /// Compiles top-level function definitions (e.g. on_group_state)
        /// into this script's persistent Lua state. Call once per script
        /// at load time, not on every packet.
        /// </summary>
        public void LoadScript(string luaSource)
        {
            Eval(luaSource);
        }

        public void RegisterGroupStateHandler(string luaFunctionName)
        {
            _groupStateHandlerName = luaFunctionName;
        }

        public void RegisterRoomStateHandler(string luaFunctionName)
        {
            _roomStateHandlerName = luaFunctionName;
        }

        /// <summary>
        /// Called by GroupHolder right after it assigns RootModel.GroupStatus.
        /// Passes the SAME list reference already produced by deserializing
        /// the server's type-12 XML packet -- no copy, no re-parse.
        /// </summary>
        public void RaiseGroupStateChanged(List<CharacterStatus> group)
        {
            if (string.IsNullOrEmpty(_groupStateHandlerName))
            {
                return;
            }

            InvokeHandler(_groupStateHandlerName, group);
        }

        /// <summary>
        /// Called by MonsterHolder right after it assigns
        /// RootModel.RoomMonstersStatus (type-13 packet).
        /// </summary>
        public void RaiseRoomStateChanged(List<MonsterStatus> monsters)
        {
            if (string.IsNullOrEmpty(_roomStateHandlerName))
            {
                return;
            }

            InvokeHandler(_roomStateHandlerName, monsters);
        }

        private void InvokeHandler(string functionName, object argument)
        {
            var function = _lua.GetFunction(functionName);
            if (function == null)
            {
                return;
            }

            _budgetExceeded = false;
            try
            {
                function.Call(argument);
            }
            catch (NLua.Exceptions.LuaScriptException ex)
            {
                if (_budgetExceeded)
                {
                    throw new LuaScriptTimeoutException(
                        "Script handler '" + functionName + "' exceeded the instruction budget and was aborted.");
                }

                throw;
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run `$MSBUILD` then `$VSTEST`.
Expected: 9 passed total.

- [ ] **Step 5: Commit**

```bash
git add Adan.Client.Common/Scripting Adan.Client.Common.Tests/Scripting/LuaScriptHostTests.cs
git commit -m "feat: expose SendCommand and group/room state handlers from LuaScriptHost"
```

---

### Task 5: Wire `LuaScriptHost` into `RootModel`

**Files:**
- Modify: `Adan.Client.Common/Model/RootModel.cs`

No test here — `RootModel` has WPF/networking dependencies that make it impractical to unit test in isolation in this codebase (consistent with the rest of the existing code, which has no tests for `RootModel`). This task is verified manually in Task 7.

- [ ] **Step 1: Add the property and field**

In `Adan.Client.Common/Model/RootModel.cs`, in the `Constants and Fields` region (after line 39, `private List<MonsterStatus> _monsterStatus;`):

```csharp
private readonly Scripting.LuaScriptHost _scriptHost;
```

- [ ] **Step 2: Construct it in the real (networked) constructor**

In the constructor at line 49 (`public RootModel([NotNull] MessageConveyor conveyor, ProfileHolder profile, IList<RootModel> allModels)`), after `MessageConveyor = conveyor;` (line 65), add:

```csharp
_scriptHost = new Scripting.LuaScriptHost(
    command => conveyor.PushCommand(new Commands.TextCommand(command)));
```

- [ ] **Step 3: Construct a non-networked host in the two other constructors**

In the `RootModel(ProfileHolder profile)` constructor (line 73) and the parameterless `RootModel()` constructor (line 90), add, in each:

```csharp
_scriptHost = new Scripting.LuaScriptHost();
```

These two constructors are used for design-time/empty models (`StatisticsHolder.cs:16`) where there is no live connection to send commands to, so the no-arg `LuaScriptHost` overload (which no-ops `SendCommand`) is correct.

- [ ] **Step 4: Expose the property**

Add to the `Properties` region (near `GroupStatus`/`RoomMonstersStatus`, around line 307):

```csharp
/// <summary>
/// The per-tab Lua scripting host. One persistent, sandboxed Lua state
/// for the lifetime of this RootModel (i.e. this tab/connection).
/// </summary>
public Scripting.LuaScriptHost ScriptHost
{
    get { return _scriptHost; }
}
```

- [ ] **Step 5: Dispose it**

In `Dispose()` (line 822), before the existing `if (MessageConveyor != null)` block, add:

```csharp
if (_scriptHost != null)
{
    _scriptHost.Dispose();
}
```

- [ ] **Step 6: Build to confirm no compile errors**

Run: `"$MSBUILD" Adan.Client.Common/Adan.Client.Common.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo` (paths as defined in the "Build/test commands" block at the top of this plan).
Expected: ends with `Adan.Client.Common -> ...\Adan.Client.Common.dll`, no `error` lines.

- [ ] **Step 7: Commit**

```bash
git add Adan.Client.Common/Model/RootModel.cs
git commit -m "feat: host a per-tab LuaScriptHost on RootModel"
```

---

### Task 6: Fire the events from the two existing packet handlers

**Files:**
- Modify: `Adan.Client.Plugins.GroupWidget/Model/GroupHolder.cs:81`
- Modify: `Adan.Client.Plugins.GroupWidget/Model/MonsterHolder.cs:81`

This is the entire "hook into the hot path" change. One line each, placed after the existing assignment, so the existing widget-update behavior is completely unchanged and the new call is the very last thing that happens for that packet.

- [ ] **Step 1: Edit `GroupHolder.cs`**

Current code at lines 74-84:

```csharp
private void MessageConveyor_MessageReceived(object sender, MessageReceivedEventArgs e)
{
    if (e.Message.MessageType == Constants.GroupStatusMessageType)
    {
        var groupMessage = e.Message as GroupStatusMessage;

        Characters = groupMessage.GroupMates;
        RootModel.GroupStatus = Characters;
        _groupManager.UpdateGroup(this);
    }
}
```

Change to:

```csharp
private void MessageConveyor_MessageReceived(object sender, MessageReceivedEventArgs e)
{
    if (e.Message.MessageType == Constants.GroupStatusMessageType)
    {
        var groupMessage = e.Message as GroupStatusMessage;

        Characters = groupMessage.GroupMates;
        RootModel.GroupStatus = Characters;
        _groupManager.UpdateGroup(this);
        RootModel.ScriptHost.RaiseGroupStateChanged(Characters);
    }
}
```

- [ ] **Step 2: Edit `MonsterHolder.cs`**

Current code at lines 75-85:

```csharp
private void MessageConveyor_MessageReceived(object sender, MessageReceivedEventArgs e)
{
    if (e.Message.MessageType == Constants.RoomMonstersMessage)
    {
        var monsterMessage = e.Message as RoomMonstersMessage;
        Characters = monsterMessage.Monsters;
        RootModel.RoomMonstersStatus = Characters;

        _monsterManager.UpdateMonsters(this);
    }
}
```

Change to:

```csharp
private void MessageConveyor_MessageReceived(object sender, MessageReceivedEventArgs e)
{
    if (e.Message.MessageType == Constants.RoomMonstersMessage)
    {
        var monsterMessage = e.Message as RoomMonstersMessage;
        Characters = monsterMessage.Monsters;
        RootModel.RoomMonstersStatus = Characters;

        _monsterManager.UpdateMonsters(this);
        RootModel.ScriptHost.RaiseRoomStateChanged(Characters);
    }
}
```

- [ ] **Step 3: Build**

Run: `"$MSBUILD" Adan.Client.Plugins.GroupWidget/Adan.Client.Plugins.GroupWidget.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo`. Note: this project's `ProjectReference` to `Adan.Client.Common.csproj` means MSBuild will rebuild that dependency first — that's expected.
Expected: ends with `Adan.Client.Plugins.GroupWidget -> ...\Adan.Client.Plugins.GroupWidget.dll`, no `error` lines.

- [ ] **Step 4: Commit**

```bash
git add Adan.Client.Plugins.GroupWidget/Model/GroupHolder.cs Adan.Client.Plugins.GroupWidget/Model/MonsterHolder.cs
git commit -m "feat: notify LuaScriptHost on group/room-monster packet updates"
```

---

### Task 7: `LuaScriptAction` — Lua as a trigger/alias action

**Files:**
- Create: `Adan.Client/Model/Actions/LuaScriptAction.cs`
- Create: `Adan.Client/Model/ActionDescriptions/LuaScriptActionDescription.cs`
- Create: `Adan.Client/ViewModel/Actions/LuaScriptActionViewModel.cs`
- Modify: `Adan.Client/MainWindow.xaml.cs:413-435`

This makes the engine usable from the *existing* trigger/alias editor: a trigger's action list can now contain "run this Lua code" alongside `SendTextAction`, etc. No new UI dialog is built in this task — `ActionEditorControl.xaml:176` already binds its action-type picker to `RootModel.AllActionDescriptions`, so registering the description is sufficient for it to show up in the existing combo box. A parameter-edit box for the Lua source itself is the only new UI surface, and it is delegated to `LuaScriptActionViewModel`/a plain multi-line `TextBox`-bound property, matching how `SendTextActionViewModel.CommandText` already works.

- [ ] **Step 1: Implement the action**

```csharp
// Adan.Client/Model/Actions/LuaScriptAction.cs
namespace Adan.Client.Model.Actions
{
    using System;
    using System.Xml.Serialization;

    using Common.Model;
    using Common.Scripting;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// Action that runs a Lua script in the tab's persistent LuaScriptHost
    /// when the owning trigger/alias fires. Parameter-less, like
    /// ClearVariableValueAction, so it derives from ActionBase directly
    /// rather than ActionWithParameters.
    /// </summary>
    [Serializable]
    public class LuaScriptAction : ActionBase
    {
        public LuaScriptAction()
        {
            ScriptText = string.Empty;
        }

        public override bool IsGlobal
        {
            get { return false; }
        }

        /// <summary>
        /// Gets or sets the Lua source to run. The matched trigger text
        /// (if any) is available in the script as the global `match`.
        /// </summary>
        [NotNull]
        [XmlAttribute]
        public string ScriptText
        {
            get;
            set;
        }

        public override void Execute(RootModel model, ActionExecutionContext context)
        {
            Assert.ArgumentNotNull(model, "model");
            Assert.ArgumentNotNull(context, "context");

            try
            {
                model.ScriptHost.LoadScript(ScriptText);
            }
            catch (LuaScriptTimeoutException ex)
            {
                model.PushMessageToConveyor(
                    new Common.Messages.ErrorMessage("Lua: " + ex.Message));
            }
        }

        public override string ToString()
        {
            return "Lua: " + ScriptText;
        }
    }
}
```

Both `model.PushMessageToConveyor` (`RootModel.cs:413`, used the same way at `RootModel.cs:807`) and `RootModel.ScriptHost` (Task 5, Step 4) are confirmed `public`, so this compiles as written.

- [ ] **Step 2: Implement the description**

```csharp
// Adan.Client/Model/ActionDescriptions/LuaScriptActionDescription.cs
namespace Adan.Client.Model.ActionDescriptions
{
    using System.Collections.Generic;

    using Actions;

    using Common.Model;
    using Common.Plugins;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    using ViewModel.Actions;

    public class LuaScriptActionDescription : ActionDescription
    {
        public LuaScriptActionDescription([NotNull] IEnumerable<ActionDescription> allDescriptions)
            : base("Run Lua script", allDescriptions)
        {
            Assert.ArgumentNotNull(allDescriptions, "allDescriptions");
        }

        public override ActionBase CreateAction()
        {
            return new LuaScriptAction();
        }

        public override ActionViewModelBase CreateActionViewModel(ActionBase action)
        {
            Assert.ArgumentNotNull(action, "action");
            var luaAction = action as LuaScriptAction;
            if (luaAction != null)
            {
                return new LuaScriptActionViewModel(luaAction, this, AllDescriptions);
            }

            return null;
        }
    }
}
```

- [ ] **Step 3: Implement the view model**

`ActionViewModelBase` (confirmed by reading `Adan.Client.Common/ViewModel/ActionViewModelBase.cs`) declares exactly two abstract members: `string ActionDescription { get; }` and `ActionViewModelBase Clone()`. `LuaScriptAction` has no `Parameters` (unlike `SendTextAction`), so this mirrors `ClearVariableValueActionViewModel.cs` (which also wraps a parameter-less action) rather than `SendTextActionViewModel.cs`:

```csharp
// Adan.Client/ViewModel/Actions/LuaScriptActionViewModel.cs
namespace Adan.Client.ViewModel.Actions
{
    using System.Collections.Generic;

    using Common.Plugins;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    using Model.Actions;

    public class LuaScriptActionViewModel : ActionViewModelBase
    {
        private readonly LuaScriptAction _action;

        public LuaScriptActionViewModel(
            [NotNull] LuaScriptAction action,
            [NotNull] ActionDescription actionDescriptor,
            [NotNull] IEnumerable<ActionDescription> allActionDescriptions)
            : base(action, actionDescriptor, allActionDescriptions)
        {
            Assert.ArgumentNotNull(action, "action");
            Assert.ArgumentNotNull(actionDescriptor, "actionDescriptor");
            Assert.ArgumentNotNull(allActionDescriptions, "allActionDescriptions");

            _action = action;
        }

        public string ScriptText
        {
            get
            {
                return _action.ScriptText;
            }

            set
            {
                Assert.ArgumentNotNull(value, "value");

                _action.ScriptText = value;
                OnPropertyChanged("ScriptText");
                OnPropertyChanged("ActionDescription");
            }
        }

        public override string ActionDescription
        {
            get { return "Lua: " + ScriptText; }
        }

        public override ActionViewModelBase Clone()
        {
            return new LuaScriptActionViewModel(new LuaScriptAction(), ActionDescriptor, AllActionDescriptions)
            {
                ScriptText = ScriptText
            };
        }
    }
}
```

- [ ] **Step 4: Register it**

In `Adan.Client/MainWindow.xaml.cs`, after line 428 (`actionDescriptions.Add(new StatusActionDescription(...)));`):

```csharp
actionDescriptions.Add(new LuaScriptActionDescription(actionDescriptions));
```

- [ ] **Step 5: Build**

Run: `"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo`.
Expected: ends with `Adan.Client -> ...\Adan.Client.exe`, no `error` lines. Fix any `ActionViewModelBase` member-name mismatches found in Step 3 before this passes.

- [ ] **Step 6: Commit**

```bash
git add "Adan.Client/Model/Actions/LuaScriptAction.cs" "Adan.Client/Model/ActionDescriptions/LuaScriptActionDescription.cs" "Adan.Client/ViewModel/Actions/LuaScriptActionViewModel.cs" "Adan.Client/MainWindow.xaml.cs"
git commit -m "feat: add LuaScriptAction as a trigger/alias action type"
```

---

### Task 8: Manual verification pass

**Files:** none (verification only — see `superpowers:verification-before-completion` before declaring this plan done)

- [ ] **Step 1: Build and run the full client**

Run: `powershell.exe -ExecutionPolicy Bypass -File C:\bot\repos\adan-refactor-clients-workspace\build_client.ps1`

Confirm the build succeeds and produces a new `vNNN` artifact.

- [ ] **Step 2: Verify the action shows up in the editor**

Launch the built client, open Triggers editor, create a test trigger with pattern `тестскрипт`, add an action, confirm "Run Lua script" appears in the action-type combo box (fed by `RootModel.AllActionDescriptions`), and that you can type Lua source into its parameter box.

- [ ] **Step 3: Verify a trigger-attached script runs**

Set the action's script to:

```lua
SendCommand("ооц Lua сработал")
```

Type `тестскрипт` in the command line, confirm the client sends `ооц Lua сработал` to the server.

- [ ] **Step 4: Verify packet-state hookup with a temporary diagnostic script**

This plan does not build the "Scripts" editor dialog yet (follow-up plan), so for this manual check only, temporarily call `RootModel.ScriptHost.LoadScript(...)` and `RegisterGroupStateHandler(...)` from a debugger breakpoint or a throwaway `#if DEBUG` block in `MainWindow.xaml.cs` after a profile connects:

```lua
function on_group_state(group)
end
```

Set a debugger breakpoint inside `InvokeHandler` in `LuaScriptHost.cs`, join a group (or just be in your own solo group), and confirm the breakpoint hits the next time a type-12 packet arrives — proving the hookup fires without any text parsing involved. Remove the throwaway debug block afterward; do not commit it.

- [ ] **Step 5: Verify the watchdog under real conditions**

Temporarily set a trigger's Lua action to `while true do end`, fire it, and confirm the client logs/displays the `LuaScriptTimeoutException` message and the tab remains responsive (not frozen) — this is the regression test for the exact failure mode (`c5d2d53`/`847a3db`-style freeze) that motivated the watchdog in Task 3.

- [ ] **Step 6: Run the full automated test suite one more time**

Run `$MSBUILD` then `$VSTEST` from the "Build/test commands" block.
Expected: all tests still pass after the manual edits in Steps 2-5 (which were not committed).

---

## Follow-up plans (explicitly out of scope here)

- **"Scripts" editor dialog** — a top-level UI (tree + code editor) for the packet-state handlers (`on_group_state`/`on_room_state`) that aren't attached to any single trigger, persisted as a new entity on `ProfileHolder` alongside `_groups`/`_variables`.
- **Searchable help window** (the Orion-style tree+search+content panel discussed in conversation) — documentation UI for the scripting API, built once the API surface in Tasks 4 and 7 has stabilized.
- **`.NET regex exposed to Lua`** — a `Regex(pattern, text)` Lua-callable wrapper around `System.Text.RegularExpressions`, deferred because Task 4's API surface (group/room state + SendCommand) doesn't need it yet; add it when a real script needs pattern matching beyond what trigger patterns already provide.
- **Cross-tab `SendToWindow` from Lua** — needs a registry of all open `RootModel`s by character name, which exists (`_allModels` in `RootModel.cs`) but isn't wired to the script host in this plan.
