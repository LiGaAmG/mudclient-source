# Lua "Scripts" Dialog + Help Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the user a place to write and persist the "global" Lua scripts (`on_group_state`/`on_room_state` packet-state handlers) that aren't tied to any single trigger, plus a searchable in-app help window documenting the scripting API — both follow-ups explicitly deferred from the prior `2026-06-20-lua-scripting-core.md` plan.

**Architecture:** A new `ScriptDefinition` model (Name, Code, IsEnabled, HandlerKind) persists per-profile to its own `Scripts.xml`, following the exact same `ProfileHolder.ReadVariables()/SaveVariables()` pattern already used for `Variable`. At profile-load time, `RootModel` loads every enabled script's code into its `LuaScriptHost` and registers the handler by a fixed, convention-based Lua function name (`on_group_state` for `HandlerKind.GroupState`, `on_room_state` for `HandlerKind.RoomState`) — the script author must define a function with that exact name. The editor dialog (`ScriptsEditDialog`) follows the same `ProfileOptionsViewModel`-driven dialog pattern as `HotkeysEditDialog`/`AliasesEditDialog`, but flatter (no per-Group nesting, since these scripts aren't part of the trigger/alias Group system). The help window is a standalone `HelpWindow` with a `TreeView` (categories) + `TextBox` search filter + a content panel, backed by a static, hand-written list of topics — no markdown parser, no external files.

**Known v1 limitation (documented in the help content, not solved by this plan):** `LuaScriptHost` currently holds exactly one registered function name per handler kind (`RegisterGroupStateHandler`/`RegisterRoomStateHandler` each overwrite the previous registration). If a profile has two enabled scripts both of `HandlerKind.GroupState`, only the **last one loaded** (i.e. last in the `Scripts` list) actually has its `on_group_state` definition survive — the earlier one's function gets silently redefined out of existence by the later `LoadScript` call, since both scripts must define a function with the identical name. The editor will not prevent the user from creating two such scripts, but the help content explicitly documents this. Multi-script composition per handler kind is a separate future plan (would need per-script function namespacing and a dispatcher in `LuaScriptHost`), out of scope here.

**Tech Stack:** Same as the prior plan — C#/.NET Framework WPF, NLua-backed `LuaScriptHost` (unchanged in this plan except for being a consumer, not modified).

---

## File Structure

- **Create** `Adan.Client.Common/Model/ScriptHandlerKind.cs` — enum `{ None, GroupState, RoomState }`.
- **Create** `Adan.Client.Common/Model/ScriptDefinition.cs` — the persisted model (mirrors `Variable.cs`).
- **Modify** `Adan.Client.Common/Settings/ProfileHolder.cs` — add `Scripts` property + `ReadScripts()`/`SaveScripts()`, wired into `Save()`/constructor exactly like `Variables`.
- **Modify** `Adan.Client.Common/Model/RootModel.cs` — after `_scriptHost` is constructed in the real (networked) constructor, loop `profile.Scripts` and load/register enabled ones.
- **Create** `Adan.Client/ViewModel/ScriptViewModel.cs` — wraps one `ScriptDefinition` for binding (mirrors the simplicity of `Variable`-style wrapping, not the complexity of `HotkeyViewModel`).
- **Create** `Adan.Client/ViewModel/ScriptsViewModel.cs` — flat `ObservableCollection<ScriptViewModel>` + Add/Delete commands (mirrors `HotkeysViewModel`'s command pattern, without the Group nesting).
- **Create** `Adan.Client/Dialogs/ScriptsEditDialog.xaml` + `.xaml.cs` — list on the left, code editor + handler-kind picker on the right, "Help" button.
- **Modify** `Adan.Client/Dialogs/ProfileOptionsEditDialog.xaml` — add a `Scripts` `ListBoxItem` next to the existing `Hotkeys`/`Triggers` items.
- **Modify** `Adan.Client/ViewModel/ProfileOptionsViewModel.cs` — add `ScriptsCount` property and a `case "Scripts":` branch in `EditProfile`, mirroring the `Hotkeys` branch.
- **Create** `Adan.Client/Dialogs/HelpWindow.xaml` + `.xaml.cs` — `TreeView` + search `TextBox` + content panel.
- **Create** `Adan.Client/ViewModel/HelpTopic.cs` — `{ Title, Content }` plain data class.
- **Create** `Adan.Client/ViewModel/HelpTopics.cs` — static `List<HelpTopic>` with the actual scripting-API documentation content.
- **Test:** `Adan.Client.Common.Tests/Model/ScriptDefinitionTests.cs` — round-trip XML serialization test for `ScriptDefinition` (the one piece of this plan that's plain C# with no WPF dependency, so it's the one piece that gets a real automated test, consistent with the rest of this codebase's testing posture).

---

## Build/test commands (same environment notes as the prior plan)

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
VSTEST="/c/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/CommonExtensions/Microsoft/TestWindow/vstest.console.exe"
cd /c/tmp/mudclient

# Tests (Adan.Client.Common.Tests, SDK-style):
"$MSBUILD" Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
"$VSTEST" "Adan.Client.Common.Tests/bin/Debug/net48/Adan.Client.Common.Tests.dll"

# Adan.Client.Common (old-style):
"$MSBUILD" Adan.Client.Common/Adan.Client.Common.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo

# Adan.Client (old-style, the WPF exe):
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```
Do NOT use `dotnet build`/`dotnet test`.

---

### Task 1: `ScriptDefinition` model + round-trip test

**Files:**
- Create: `Adan.Client.Common/Model/ScriptHandlerKind.cs`
- Create: `Adan.Client.Common/Model/ScriptDefinition.cs`
- Test: `Adan.Client.Common.Tests/Model/ScriptDefinitionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Adan.Client.Common.Tests/Model/ScriptDefinitionTests.cs
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using NUnit.Framework;
using Adan.Client.Common.Model;

namespace Adan.Client.Common.Tests.Model
{
    [TestFixture]
    public class ScriptDefinitionTests
    {
        [Test]
        public void RoundTrip_PreservesAllFields()
        {
            var original = new List<ScriptDefinition>
            {
                new ScriptDefinition
                {
                    Name = "Auto-heal",
                    Code = "function on_group_state(group)\n  -- heal logic\nend",
                    IsEnabled = true,
                    HandlerKind = ScriptHandlerKind.GroupState
                }
            };

            var serializer = new XmlSerializer(typeof(List<ScriptDefinition>));
            string xml;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, original);
                xml = writer.ToString();
            }

            List<ScriptDefinition> roundTripped;
            using (var reader = new StringReader(xml))
            {
                roundTripped = (List<ScriptDefinition>)serializer.Deserialize(reader);
            }

            Assert.That(roundTripped.Count, Is.EqualTo(1));
            Assert.That(roundTripped[0].Name, Is.EqualTo("Auto-heal"));
            Assert.That(roundTripped[0].Code, Is.EqualTo("function on_group_state(group)\n  -- heal logic\nend"));
            Assert.That(roundTripped[0].IsEnabled, Is.True);
            Assert.That(roundTripped[0].HandlerKind, Is.EqualTo(ScriptHandlerKind.GroupState));
        }

        [Test]
        public void DefaultConstructor_HasSaneDefaults()
        {
            var script = new ScriptDefinition();
            Assert.That(script.Name, Is.EqualTo(string.Empty));
            Assert.That(script.Code, Is.EqualTo(string.Empty));
            Assert.That(script.IsEnabled, Is.False);
            Assert.That(script.HandlerKind, Is.EqualTo(ScriptHandlerKind.None));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Build the test project with `$MSBUILD`. Expected: `CS0234`/`CS0246` — `ScriptDefinition`/`ScriptHandlerKind` don't exist yet.

- [ ] **Step 3: Implement the enum**

```csharp
// Adan.Client.Common/Model/ScriptHandlerKind.cs
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
```

- [ ] **Step 4: Implement the model**

```csharp
// Adan.Client.Common/Model/ScriptDefinition.cs
namespace Adan.Client.Common.Model
{
    using System;
    using System.Xml.Serialization;

    using CSLib.Net.Annotations;

    /// <summary>
    /// A single named, persisted Lua script not tied to any trigger/alias --
    /// the "global" scripts editable via the Scripts dialog. Mirrors
    /// Variable.cs's persistence shape (flat, profile-scoped, no Group nesting).
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

        [XmlAttribute]
        public ScriptHandlerKind HandlerKind
        {
            get;
            set;
        }
    }
}
```

`Code` is `[XmlElement]` rather than `[XmlAttribute]` (unlike `Name`) because script source can contain newlines and characters that are awkward/illegal inside an XML attribute value; `XmlSerializer` handles a multi-line string fine as an element's text content.

- [ ] **Step 5: Run tests to verify they pass**

Build + vstest. Expected: 20 total (18 from the prior plan + 2 new), 20 passed.

- [ ] **Step 6: Commit**

```bash
git add Adan.Client.Common/Model/ScriptHandlerKind.cs Adan.Client.Common/Model/ScriptDefinition.cs Adan.Client.Common.Tests/Model/ScriptDefinitionTests.cs
git commit -m "feat: add ScriptDefinition model for the global Scripts dialog"
```

---

### Task 2: `ProfileHolder.Scripts` persistence

**Files:**
- Modify: `Adan.Client.Common/Settings/ProfileHolder.cs`

No new test here (`ProfileHolder` touches the filesystem and isn't unit-tested anywhere else in this codebase, consistent with `Variables`/`Groups`). Verified by build + Task 7's manual pass.

- [ ] **Step 1: Read the current file in full**, focusing on the `Variables` property (around line 94-108), `ReadVariables()` (around line 298-326), `SaveVariables()` (around line 328-354), and `Save()` (where `SaveVariables()` is called alongside `SaveGroups()`/`SaveCommonSettings()`/`SaveCommandHistory()`). Confirm the exact current line numbers and surrounding code before editing -- they may have drifted from these approximate references.

- [ ] **Step 2: Add the field**

Near `private List<Variable> _variables;`, add:

```csharp
private List<ScriptDefinition> _scripts;
```

- [ ] **Step 3: Add the property**

Mirroring the `Variables` property exactly:

```csharp
/// <summary>
/// Gets or sets the global Lua scripts (not tied to any trigger/alias).
/// </summary>
[NotNull]
public List<ScriptDefinition> Scripts
{
    get
    {
        if (_scripts == null)
            ReadScripts();
        return _scripts;
    }
    set { _scripts = value; }
}
```

- [ ] **Step 4: Add `ReadScripts()`**

Mirroring `ReadVariables()` exactly, with `Scripts.xml` as the file name:

This deliberately follows `ReadVariables()`'s gentler error-handling pattern (log + warn + reset to empty), NOT `ReadGroups()`'s pattern (log + fatal `MessageBox` + `Application.Current.Shutdown()`). A corrupt `Scripts.xml` should never be able to take down the whole client the way a corrupt `Settings.xml` currently does -- this is a brand-new, low-stakes file, there's no reason to make it fatal:

```csharp
private void ReadScripts()
{
    var scriptsFileFullPath = Path.Combine(GetProfileSettingsFolder(), "Scripts.xml");
    if (!File.Exists(scriptsFileFullPath))
    {
        Scripts = new List<ScriptDefinition>();
        return;
    }

    using (var stream = File.OpenRead(scriptsFileFullPath))
    {
        try
        {
            var serializer = new XmlSerializer(typeof(List<ScriptDefinition>));
            Scripts = (List<ScriptDefinition>)serializer.Deserialize(stream);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Write(string.Format("Error read scripts: {0}\r\n{1}", ex.Message, ex.StackTrace));
            MessageBox.Show(
                "Произошла ошибка при загрузке " + scriptsFileFullPath + ": " + ex.Message + ". Скрипты обнулены.",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Scripts = new List<ScriptDefinition>();
        }
    }
}
```

Confirmed exact pattern by reading `ReadVariables()`'s real catch block (`ProfileHolder.cs:314-322`): `ErrorLogger.Instance.Write(...)` plus a non-fatal `MessageBox.Show(...)`, then falls through with an empty list -- no `ErrorOccurred` event raised on the read side (that's only used on the *save* side, see `SaveScripts()` below). Check that `System.Windows.MessageBox` is already imported in this file (it must be, since `ReadGroups`/`ReadVariables` already use it) -- no new `using` needed.

- [ ] **Step 5: Add `SaveScripts()`**

Mirroring `SaveVariables()` exactly:

Confirmed exact pattern by reading `SaveVariables()`'s real catch block (`ProfileHolder.cs:348-351`): log via `ErrorLogger.Instance.Write(...)`, then raise the `ErrorOccurred` event (declared on `ProfileHolder`, see `Adan.Client\ViewModel\ProfileOptionsViewModel.cs`'s `HandleSettingsError` or similar subscriber if one exists) with a `SettingsErrorEventArgs`:

```csharp
private void SaveScripts()
{
    if (!Directory.Exists(GetProfileSettingsFolder()))
        Directory.CreateDirectory(GetProfileSettingsFolder());

    var fileFullPath = Path.Combine(GetProfileSettingsFolder(), "Scripts.xml");
    using (var stream = File.Open(fileFullPath, FileMode.Create, FileAccess.Write))
    using (var streamWriter = new XmlTextWriter(stream, Encoding.UTF8))
    {
        streamWriter.Formatting = Formatting.Indented;
        try
        {
            var serializer = new XmlSerializer(typeof(List<ScriptDefinition>));
            serializer.Serialize(streamWriter, Scripts);
        }
        catch (Exception ex)
        {
            ErrorLogger.Instance.Write(string.Format("Error save scripts: {0}\r\n{1}", ex.Message, ex.StackTrace));

            if (ErrorOccurred != null)
                ErrorOccurred(this, new SettingsErrorEventArgs("#Ошибка при сохранении " + fileFullPath + ": " + ex.Message + "."));
        }
    }
}
```

- [ ] **Step 6: Wire into `Save()`**

Find the line that calls `SaveVariables();` inside `Save()` and add `SaveScripts();` immediately after it.

- [ ] **Step 7: Build**

```bash
"$MSBUILD" Adan.Client.Common/Adan.Client.Common.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```
Expected: no errors.

- [ ] **Step 8: Commit**

```bash
git add Adan.Client.Common/Settings/ProfileHolder.cs
git commit -m "feat: persist global Scripts list per profile (Scripts.xml)"
```

---

### Task 3: Load enabled scripts into `LuaScriptHost` at profile-load time

**Files:**
- Modify: `Adan.Client.Common/Model/RootModel.cs`

- [ ] **Step 1: Read the current networked constructor** (`public RootModel([NotNull] MessageConveyor conveyor, ProfileHolder profile, IList<RootModel> allModels)`) in full, including where `_scriptHost` is now constructed (added by the prior plan's Task 5).

- [ ] **Step 2: Add the load loop**

Immediately after the `_scriptHost = new Scripting.LuaScriptHost(...)` line, add:

```csharp
foreach (var script in profile.Scripts)
{
    if (!script.IsEnabled)
    {
        continue;
    }

    try
    {
        _scriptHost.LoadScript(script.Code);

        if (script.HandlerKind == ScriptHandlerKind.GroupState)
        {
            _scriptHost.RegisterGroupStateHandler("on_group_state");
        }
        else if (script.HandlerKind == ScriptHandlerKind.RoomState)
        {
            _scriptHost.RegisterRoomStateHandler("on_room_state");
        }
    }
    catch (Exception)
    {
        // A broken script must not prevent the tab from opening --
        // LoadScript already routes through the watchdog-protected
        // RunProtected, so this only catches genuine syntax/runtime
        // errors (LuaScriptTimeoutException or NLua.Exceptions.LuaScriptException),
        // not a hang. The user finds out their script is broken when they
        // notice its handler never fires, not via a crashed tab.
    }
}
```

This is a known, deliberately silent failure mode for v1 -- there is no UI feedback if a script fails to load at connect time. Flag this in the Help content (Task 6) rather than building error-reporting UI now.

Note `ScriptHandlerKind` needs `using Adan.Client.Common.Model;` -- check if `RootModel.cs` already has this `using` (it's in the same `Adan.Client.Common.Model` namespace as `RootModel` itself, so likely no `using` is even needed -- confirm by checking the current namespace declaration at the top of the file).

- [ ] **Step 3: Build**

```bash
"$MSBUILD" Adan.Client.Common/Adan.Client.Common.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Adan.Client.Common/Model/RootModel.cs
git commit -m "feat: load enabled profile Scripts into LuaScriptHost at connect time"
```

---

### Task 4: `ScriptViewModel` + `ScriptsViewModel`

**Files:**
- Create: `Adan.Client/ViewModel/ScriptViewModel.cs`
- Create: `Adan.Client/ViewModel/ScriptsViewModel.cs`

- [ ] **Step 1: Read `Adan.Client.Common/ViewModel/ViewModelBase.cs` and `Adan.Client.Common/Utils/DelegateCommand.cs` in full** to confirm `OnPropertyChanged(string)` and `DelegateCommand(Action<object>, bool)`/`CanBeExecuted` exist exactly as used below (already verified during planning research, but re-confirm before writing code since this is a fresh task).

- [ ] **Step 2: Implement `ScriptViewModel`**

```csharp
// Adan.Client/ViewModel/ScriptViewModel.cs
namespace Adan.Client.ViewModel
{
    using Common.Model;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// Wraps a single ScriptDefinition for binding in the Scripts dialog.
    /// </summary>
    public class ScriptViewModel : ViewModelBase
    {
        private readonly ScriptDefinition _script;

        public ScriptViewModel([NotNull] ScriptDefinition script)
        {
            Assert.ArgumentNotNull(script, "script");
            _script = script;
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

        public ScriptHandlerKind HandlerKind
        {
            get { return _script.HandlerKind; }
            set
            {
                _script.HandlerKind = value;
                OnPropertyChanged("HandlerKind");
            }
        }
    }
}
```

- [ ] **Step 3: Implement `ScriptsViewModel`**

```csharp
// Adan.Client/ViewModel/ScriptsViewModel.cs
namespace Adan.Client.ViewModel
{
    using System.Collections.ObjectModel;
    using System.Linq;

    using Common.Model;
    using Common.Utils;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// View model for the Scripts editor dialog -- a flat list of global
    /// Lua scripts, not nested under any trigger/alias Group.
    /// </summary>
    public class ScriptsViewModel : ViewModelBase
    {
        private readonly System.Collections.Generic.List<ScriptDefinition> _backingList;
        private ScriptViewModel _selectedScript;

        public ScriptsViewModel([NotNull] System.Collections.Generic.List<ScriptDefinition> backingList)
        {
            Assert.ArgumentNotNull(backingList, "backingList");

            _backingList = backingList;
            Scripts = new ObservableCollection<ScriptViewModel>(
                backingList.Select(s => new ScriptViewModel(s)));

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

        private void AddScriptCommandExecute(object obj)
        {
            var newScript = new ScriptDefinition { Name = "New script" };
            _backingList.Add(newScript);
            var newViewModel = new ScriptViewModel(newScript);
            Scripts.Add(newViewModel);
            SelectedScript = newViewModel;
        }

        private void DeleteScriptCommandExecute(object obj)
        {
            if (SelectedScript == null)
            {
                return;
            }

            _backingList.Remove(SelectedScript.Script);
            Scripts.Remove(SelectedScript);
            SelectedScript = null;
        }
    }
}
```

Check `DelegateCommand`'s actual constructor parameter order/`CanBeExecuted` property name against the real file read in Step 1 -- the research snippet showed `CanBeExecuted` used as a settable property on an existing `EditHotkeyCommand`/`DeleteHotkeyCommand` instance (`Adan.Client/ViewModel/HotkeysViewModel.cs:124-125`), so this should match, but confirm before relying on it.

- [ ] **Step 4: Build**

```bash
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```
Expected: errors about missing `<Compile>` entries in the old-style `.csproj` for the two new files -- add them (see Task 7 of the prior plan for the exact mechanism), then rebuild until clean.

- [ ] **Step 5: Commit**

```bash
git add Adan.Client/ViewModel/ScriptViewModel.cs Adan.Client/ViewModel/ScriptsViewModel.cs Adan.Client/Adan.Client.csproj
git commit -m "feat: add ScriptViewModel/ScriptsViewModel for the Scripts dialog"
```

---

### Task 5: `ScriptsEditDialog`

**Files:**
- Create: `Adan.Client/Dialogs/ScriptsEditDialog.xaml`
- Create: `Adan.Client/Dialogs/ScriptsEditDialog.xaml.cs`

- [ ] **Step 1: Read `Adan.Client/Dialogs/HotkeysEditDialog.xaml` and `.xaml.cs` in full** as the structural template (window chrome, `Style="{StaticResource DefaultWindowStyle}"`, close-button wiring) -- this new dialog is flatter (no Group nesting) but should reuse the same outer window conventions (size, style resource, `ShowInTaskbar="False"`, etc.) for visual consistency with the rest of the app.

- [ ] **Step 2: Implement the XAML**

```xml
<!-- Adan.Client/Dialogs/ScriptsEditDialog.xaml -->
<Window x:Class="Adan.Client.Dialogs.ScriptsEditDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        mc:Ignorable="d"
        Title="Scripts"
        Width="700" Height="500"
        WindowStartupLocation="CenterOwner"
        Style="{StaticResource DefaultWindowStyle}">
    <Grid Margin="5">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="200" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <ListBox Grid.Column="0" Grid.Row="0"
                  ItemsSource="{Binding Path=Scripts}"
                  SelectedItem="{Binding Path=SelectedScript}"
                  DisplayMemberPath="Name" Margin="0,0,5,5" />

        <Grid Grid.Column="1" Grid.Row="0" DataContext="{Binding Path=SelectedScript}">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,5">
                <Label>Name:</Label>
                <TextBox Width="250" Text="{Binding Path=Name, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
            <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,5">
                <CheckBox Content="Enabled" IsChecked="{Binding Path=IsEnabled, Mode=TwoWay}" VerticalAlignment="Center" Margin="0,0,15,0" />
                <Label>Handler:</Label>
                <ComboBox Width="150" SelectedValue="{Binding Path=HandlerKind, Mode=TwoWay}" SelectedValuePath="Content">
                    <ComboBoxItem Content="None" />
                    <ComboBoxItem Content="GroupState" />
                    <ComboBoxItem Content="RoomState" />
                </ComboBox>
            </StackPanel>
            <TextBlock Grid.Row="2" TextWrapping="Wrap" Margin="0,0,0,5" Foreground="#FF999999"
                       Text="GroupState scripts must define a function named exactly on_group_state(group). RoomState scripts must define on_room_state(monsters). See Help for the full API." />
            <Border Grid.Row="3" BorderBrush="#FF555555" BorderThickness="1" CornerRadius="3" Background="#FF1E1E1E">
                <TextBox Text="{Binding Path=Code, UpdateSourceTrigger=PropertyChanged}"
                         AcceptsReturn="True" AcceptsTab="True" TextWrapping="NoWrap"
                         FontFamily="Consolas" FontSize="13"
                         Background="#FF1E1E1E" Foreground="#FFD4D4D4" BorderThickness="0"
                         CaretBrush="White" Padding="4"
                         VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" />
            </Border>
        </Grid>

        <StackPanel Grid.Column="0" Grid.Row="1" Orientation="Horizontal" Margin="0,5,0,0">
            <Button Command="{Binding Path=AddScriptCommand}" MinWidth="60" Margin="0,0,5,0">Add</Button>
            <Button Command="{Binding Path=DeleteScriptCommand}" MinWidth="60">Delete</Button>
        </StackPanel>
        <StackPanel Grid.Column="1" Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,5,0,0">
            <Button Click="HandleHelpClick" MinWidth="60" Margin="0,0,5,0">Help</Button>
            <Button Click="HandleCloseClick" IsCancel="True" MinWidth="60">Close</Button>
        </StackPanel>
    </Grid>
</Window>
```

The `ComboBox` binding to an enum via `SelectedValue`/`SelectedValuePath="Content"` matching string-vs-enum-name is a pragmatic, minimal approach (no converter needed) -- it works because `ScriptHandlerKind`'s member names (`None`, `GroupState`, `RoomState`) are exactly the strings the `ComboBoxItem.Content` values are set to, and WPF's default `SelectedValue` binding for an enum-typed source property does string-to-enum coercion automatically. If this does NOT work when you build/run it (verify in Task 7's manual pass), the fallback is a `IValueConverter` -- don't pre-build one speculatively, only add it if the simple binding actually fails.

- [ ] **Step 3: Implement the code-behind**

```csharp
// Adan.Client/Dialogs/ScriptsEditDialog.xaml.cs
namespace Adan.Client.Dialogs
{
    using System.Windows;

    public partial class ScriptsEditDialog : Window
    {
        public ScriptsEditDialog()
        {
            InitializeComponent();
        }

        private void HandleCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void HandleHelpClick(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow { Owner = this };
            helpWindow.Show();
        }
    }
}
```

This references `HelpWindow`, created in Task 6 -- if building this task before Task 6 exists, this line will fail to compile; do Task 6 first if executing out of order, or stub `HelpWindow` minimally in this task and flesh it out in Task 6 (simplest: do Task 6 before Task 5 if you're executing tasks non-sequentially; if executing in written order, just be aware this file's `HandleHelpClick` won't compile until Task 6 lands -- don't try to build/commit Task 5 in isolation, build/commit Tasks 5 and 6 together as one checkpoint instead).

- [ ] **Step 4: Add `<Compile>`/`<Page>` entries**

In `Adan.Client/Adan.Client.csproj`, find where `HotkeysEditDialog.xaml`/`.xaml.cs` are declared (a `<Page Include="Dialogs\HotkeysEditDialog.xaml">...</Page>` plus a `<Compile Include="Dialogs\HotkeysEditDialog.xaml.cs">` with `<DependentUpon>HotkeysEditDialog.xaml</DependentUpon>`) and add matching entries for `ScriptsEditDialog.xaml`/`.xaml.cs`.

- [ ] **Step 5: Hold off on building/committing until Task 6 is also done** (see the note in Step 3) -- this task's build verification happens jointly with Task 6's Step 4.

---

### Task 6: `HelpWindow` + static API documentation content

**Files:**
- Create: `Adan.Client/Dialogs/HelpWindow.xaml`
- Create: `Adan.Client/Dialogs/HelpWindow.xaml.cs`
- Create: `Adan.Client/ViewModel/HelpTopic.cs`
- Create: `Adan.Client/ViewModel/HelpTopics.cs`

- [ ] **Step 1: Implement `HelpTopic`**

```csharp
// Adan.Client/ViewModel/HelpTopic.cs
namespace Adan.Client.ViewModel
{
    /// <summary>
    /// One entry in the static Lua scripting help tree.
    /// </summary>
    public class HelpTopic
    {
        public HelpTopic(string title, string content)
        {
            Title = title;
            Content = content;
        }

        public string Title
        {
            get;
            private set;
        }

        public string Content
        {
            get;
            private set;
        }
    }
}
```

- [ ] **Step 2: Implement `HelpTopics`**

This is the actual documentation content for the API as it exists today -- only what `LuaScriptHost`'s real public surface supports, no aspirational features:

```csharp
// Adan.Client/ViewModel/HelpTopics.cs
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
```

- [ ] **Step 3: Implement `HelpWindow.xaml`**

```xml
<!-- Adan.Client/Dialogs/HelpWindow.xaml -->
<Window x:Class="Adan.Client.Dialogs.HelpWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        mc:Ignorable="d"
        Title="Lua scripting help"
        Width="700" Height="450"
        WindowStartupLocation="CenterOwner"
        Style="{StaticResource DefaultWindowStyle}">
    <Grid Margin="5">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="220" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBox Grid.Row="0" Grid.Column="0" Margin="0,0,5,5"
                  Text="{Binding Path=SearchText, UpdateSourceTrigger=PropertyChanged}"
                  ToolTip="Search topics" />

        <ListBox Grid.Row="1" Grid.Column="0" Margin="0,0,5,0"
                  ItemsSource="{Binding Path=FilteredTopics}"
                  SelectedItem="{Binding Path=SelectedTopic}"
                  DisplayMemberPath="Title" />

        <TextBox Grid.Row="0" Grid.RowSpan="2" Grid.Column="1"
                 Text="{Binding Path=SelectedTopic.Content, Mode=OneWay}"
                 IsReadOnly="True" TextWrapping="Wrap" AcceptsReturn="True"
                 VerticalScrollBarVisibility="Auto"
                 Background="#FF1E1E1E" Foreground="#FFD4D4D4" BorderThickness="0"
                 FontFamily="Consolas" FontSize="13" Padding="6" />
    </Grid>
</Window>
```

- [ ] **Step 4: Implement `HelpWindow.xaml.cs`**

The window's own code-behind doubles as its view model here (simple enough not to warrant a separate `HelpWindowViewModel` class -- `INotifyPropertyChanged` implemented directly, following YAGNI):

```csharp
// Adan.Client/Dialogs/HelpWindow.xaml.cs
namespace Adan.Client.Dialogs
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows;

    using ViewModel;

    public partial class HelpWindow : Window, INotifyPropertyChanged
    {
        private string _searchText = string.Empty;
        private HelpTopic _selectedTopic;

        public HelpWindow()
        {
            InitializeComponent();
            DataContext = this;
            SelectedTopic = HelpTopics.All.FirstOrDefault();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                _searchText = value;
                OnPropertyChanged("SearchText");
                OnPropertyChanged("FilteredTopics");
            }
        }

        public IEnumerable<HelpTopic> FilteredTopics
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    return HelpTopics.All;
                }

                return HelpTopics.All.Where(t =>
                    t.Title.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Content.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        public HelpTopic SelectedTopic
        {
            get { return _selectedTopic; }
            set
            {
                _selectedTopic = value;
                OnPropertyChanged("SelectedTopic");
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
```

(The XAML's `Title="Lua scripting help"` and the plan's earlier description mentioned a `TreeView` with categories -- this implementation uses a flat searchable `ListBox` instead, which is simpler and equally functional for ~7 topics. A `TreeView` with sub-categories is unwarranted complexity at this content volume; revisit only if the topic count grows enough to need grouping.)

- [ ] **Step 5: Add `<Compile>`/`<Page>` entries**

Add entries to `Adan.Client/Adan.Client.csproj` for `HelpWindow.xaml`/`.xaml.cs` and `ScriptsEditDialog.xaml`/`.xaml.cs` (from Task 5) and `HelpTopic.cs`/`HelpTopics.cs`, following the existing `<Page>`/`<Compile>` conventions for other Dialogs/ViewModel files in this csproj.

- [ ] **Step 6: Build (this is the joint Task 5+6 checkpoint)**

```bash
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```
Expected: no errors. Fix any remaining issues (missing `<Compile>`/`<Page>` entries, namespace typos) before proceeding.

- [ ] **Step 7: Commit**

```bash
git add Adan.Client/Dialogs/ScriptsEditDialog.xaml Adan.Client/Dialogs/ScriptsEditDialog.xaml.cs Adan.Client/Dialogs/HelpWindow.xaml Adan.Client/Dialogs/HelpWindow.xaml.cs Adan.Client/ViewModel/HelpTopic.cs Adan.Client/ViewModel/HelpTopics.cs Adan.Client/Adan.Client.csproj
git commit -m "feat: add ScriptsEditDialog and the Lua scripting HelpWindow"
```

---

### Task 7: Wire the Scripts entry into the Profile Options dialog

**Files:**
- Modify: `Adan.Client/Dialogs/ProfileOptionsEditDialog.xaml`
- Modify: `Adan.Client/ViewModel/ProfileOptionsViewModel.cs`

- [ ] **Step 1: Add the ListBoxItem**

In `Adan.Client/Dialogs/ProfileOptionsEditDialog.xaml`, add a new `ListBoxItem` after the existing `Hotkeys` one (or anywhere in the list -- order doesn't matter functionally):

```xml
<ListBoxItem Tag="Scripts">
    <StackPanel Orientation="Horizontal">
        <TextBlock>Scripts</TextBlock>
        <TextBlock Text="{Binding Path=ScriptsCount, StringFormat=' ({0})'}" />
    </StackPanel>
</ListBoxItem>
```

- [ ] **Step 2: Add `ScriptsCount` to `ProfileOptionsViewModel`**

Near the existing `HotkeysCount`/`TriggersCount` properties (around line 110-128):

```csharp
/// <summary>
/// Amount of global scripts in the profile
/// </summary>
public int ScriptsCount { get { return Profile.Scripts.Count; } }
```

Confirm `Profile` is the correct accessor for the current `ProfileHolder` in this view model (it's used elsewhere in the file, e.g. in `ImportProfile`'s `Profile.Variables` access) -- use the same accessor here for `Scripts`.

- [ ] **Step 3: Add the `case "Scripts":` branch**

In `EditProfile`'s `switch (name)` block, add a branch mirroring the `Hotkeys` case exactly in structure, but simpler (no `GroupsViewModel`/`AllActionDescriptions` dependency):

```csharp
case "Scripts":
    var scriptsEditDialog = new ScriptsEditDialog
    {
        DataContext = new ScriptsViewModel(Profile.Scripts),
        Owner = owner
    };
    scriptsEditDialog.Closed += (s, e) =>
    {
        OnPropertyChanged("ScriptsCount");
        SettingsHolder.Instance.SetProfile(Profile.Name);
    };
    scriptsEditDialog.Show();
    break;
```

Check the exact `using` statements already present at the top of `ProfileOptionsViewModel.cs` -- `ScriptsEditDialog` is in `Adan.Client.Dialogs` and `ScriptsViewModel` is in `Adan.Client.ViewModel` (the same namespace this file is already in, per Task 4), so `ScriptsViewModel` should need no new `using`, but `Dialogs` namespace usage pattern should already exist (used for `HotKeysEditDialog` etc.) -- confirm and reuse.

- [ ] **Step 4: Build**

```bash
"$MSBUILD" Adan.Client/Adan.Client.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
```
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add Adan.Client/Dialogs/ProfileOptionsEditDialog.xaml Adan.Client/ViewModel/ProfileOptionsViewModel.cs
git commit -m "feat: add Scripts entry to the Profile Options dialog"
```

---

### Task 8: Manual verification pass

**Files:** none (verification only -- see `superpowers:verification-before-completion`)

- [ ] **Step 1: Run the automated test suite**

```bash
"$MSBUILD" Adan.Client.Common.Tests/Adan.Client.Common.Tests.csproj -p:Configuration=Debug -p:TargetFrameworkVersion=v4.8 -v:minimal -nologo
"$VSTEST" "Adan.Client.Common.Tests/bin/Debug/net48/Adan.Client.Common.Tests.dll"
```
Expected: 20 total, 20 passed.

- [ ] **Step 2: Rebuild and repackage the full client**

```bash
powershell.exe -ExecutionPolicy Bypass -File C:\bot\repos\adan-refactor-clients-workspace\build_client.ps1
```
Confirm it succeeds and the output folder still contains `NLua.dll`, `KeraLua.dll`, `lua54.dll` alongside `Adan.Client.exe`/`Adan.Client.Common.dll` (per the prior plan's packaging fixes).

- [ ] **Step 3: Open Profile Options -> Scripts**

Launch the built client, open the Profile Options dialog, confirm a "Scripts" entry appears in the list with a count, double-click it, confirm `ScriptsEditDialog` opens.

- [ ] **Step 4: Add a GroupState script and verify it fires**

Click Add, set Name to "test", check Enabled, set Handler to GroupState, paste:

```lua
function on_group_state(group)
    if #group > 0 then
        SendCommand("ооц lua group size " .. #group)
    end
end
```

Close the dialog (saves via the profile's in-memory `Scripts` list -- confirm whether a reconnect is needed for it to take effect, per the Task 3 implementation which only loads scripts at `RootModel` construction time; if so, reconnect/open a new tab to pick up the change) and confirm the client sends the `ооц lua group size N` command when your group status changes.

- [ ] **Step 5: Verify the Help window**

Click Help from the Scripts dialog, confirm the window opens, type a search term (e.g. "sandbox") in the search box, confirm the topic list filters, click a topic, confirm its content displays in the right panel.

- [ ] **Step 6: Verify the `SelectedValue`/enum `ComboBox` binding actually works**

Confirm that changing the Handler dropdown in the Scripts editor actually updates `ScriptDefinition.HandlerKind` (e.g. close and reopen the dialog, or check behavior in Step 4 actually fired -- if `GroupState` wasn't picked up correctly, the script wouldn't fire at all, which would already have surfaced as a failure in Step 4). If this binding does NOT work, add a minimal `IValueConverter` (`EnumToStringConverter` -- convert via `value.ToString()` / `Enum.Parse`) and wire it into the `ComboBox.SelectedValue` binding instead of the bare string-matching approach.

---

## Follow-up plans (explicitly out of scope here)

- **Multi-script-per-handler-kind composition** -- giving `LuaScriptHost` a way to register multiple `on_group_state`-equivalent functions (e.g. by having each `ScriptDefinition` define a uniquely-named function and having `RootModel`'s load loop pass that unique name to `RegisterGroupStateHandler`, with `LuaScriptHost` internally maintaining a list of handler names per kind and calling all of them) -- documented as a known limitation in this plan's Help content instead.
- **Live reload of Scripts changes into already-open tabs** -- this plan's Task 3 only loads `profile.Scripts` once, at `RootModel` construction (i.e. at connect time). Editing scripts in an already-open tab's profile requires a reconnect/new tab to take effect. A live-reload mechanism would need `ScriptsEditDialog` to notify all open `RootModel`s for the edited profile.
- **Per-script error reporting at load time** -- Task 3 silently swallows `LoadScript` failures rather than surfacing them in the UI (e.g. via `PushMessageToConveyor`/`ErrorMessage`, the pattern `LuaScriptAction` already uses). Deferred because `RootModel`'s constructor doesn't have an obviously safe point to push UI messages before the conveyor/output window machinery is fully wired up -- needs investigation, not a quick add.
