using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Adan.Client.Common.Model;
using Adan.Client.Common.Scripting;

namespace Adan.Client.Common.Tests.Scripting
{
    [TestFixture]
    public class LuaScriptHostTests
    {
        [Test]
        public void ScriptDraftHistory_KeepsUndoAndRedoAcrossEditorSwitches()
        {
            var history = new ScriptDraftHistory();
            string text;

            history.RecordChange("one");
            history.RecordChange("two");

            Assert.That(history.TryUndo("three", out text), Is.True);
            Assert.That(text, Is.EqualTo("two"));
            Assert.That(history.TryRedo("two", out text), Is.True);
            Assert.That(text, Is.EqualTo("three"));
        }

        [Test]
        public void ScriptSnippetCatalog_SeparatesLuaClientAndWaitSnippets()
        {
            var categories = ScriptSnippetCatalog.All.Select(s => s.Category).Distinct().ToList();

            Assert.That(categories, Does.Contain("Lua"));
            Assert.That(categories, Does.Contain("Клиент"));
            Assert.That(categories, Does.Contain("Ожидание"));
        }

        [Test]
        public void NeedsRestart_OnlyReturnsTrueWhenRunningCodeChanged()
        {
            using (var host = new LuaScriptHost())
            {
                host.StartScript("test.lua", "while true do Wait(1000) end");

                Assert.That(host.NeedsRestart("test.lua", "while true do Wait(1000) end"), Is.False);
                Assert.That(host.NeedsRestart("test.lua", "while true do Wait(2000) end"), Is.True);
                host.StopScript("test.lua");
                Assert.That(host.NeedsRestart("test.lua", "while true do Wait(2000) end"), Is.False);
            }
        }

        [Test]
        public void TryValidateScript_InvalidCodeDoesNotStopRunningScript()
        {
            using (var host = new LuaScriptHost())
            {
                host.StartScript("keep", "while true do Wait(1000) end");
                string error;

                Assert.That(host.TryValidateScript("keep", "this is not lua ((", out error), Is.False);
                Assert.That(error, Is.Not.Empty);
                Assert.That(host.GetScriptStatus("keep"), Is.Not.EqualTo(ScriptRunStatus.NotRunning));
            }
        }

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

        [Test]
        public void SandboxedState_HasNoLoad()
        {
            using (var host = new LuaScriptHost())
            {
                var result = host.Eval("return load == nil");
                Assert.That(result, Is.EqualTo(true));
            }
        }

        [Test]
        public void SandboxedState_HasNoSetMetatable()
        {
            using (var host = new LuaScriptHost())
            {
                var result = host.Eval("return setmetatable == nil");
                Assert.That(result, Is.EqualTo(true));
            }
        }

        [Test]
        public void SandboxedState_HasNoGetMetatable()
        {
            using (var host = new LuaScriptHost())
            {
                var result = host.Eval("return getmetatable == nil");
                Assert.That(result, Is.EqualTo(true));
            }
        }

        [Test]
        public void Eval_InvalidSyntax_ThrowsLuaException()
        {
            using (var host = new LuaScriptHost())
            {
                Assert.Throws<NLua.Exceptions.LuaScriptException>(() => host.Eval("this is not valid lua ((("));
            }
        }

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

        [Test]
        public void Eval_InfiniteLoopInsidePcall_StillThrowsTimeout()
        {
            using (var host = new LuaScriptHost())
            {
                Assert.Throws<LuaScriptTimeoutException>(() =>
                    host.Eval("while true do pcall(function() while true do end end) end"));
            }
        }

        [Test]
        public void Eval_AfterTimeout_SameHostCanRunNormalScriptAfterward()
        {
            using (var host = new LuaScriptHost())
            {
                Assert.Throws<LuaScriptTimeoutException>(() =>
                    host.Eval("while true do end"));

                // The same host instance must still work correctly for a normal
                // script after a previous call timed out -- a timeout must not
                // leave the host's hook state (or the Lua stack) corrupted.
                var result = host.Eval("local sum = 0 for i = 1, 100 do sum = sum + i end return sum");
                Assert.That(result, Is.EqualTo(5050.0));
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

        [Test]
        public void DefaultConstructor_SendCommand_DoesNotThrow()
        {
            // No-arg constructor must still work (e.g. for design-time/empty
            // RootModel instances that have no live network connection).
            using (var host = new LuaScriptHost())
            {
                Assert.DoesNotThrow(() => host.Eval("SendCommand('whatever')"));
            }
        }

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
            using (var host = new LuaScriptHost())
            {
                Assert.That(host.Eval("return coroutine.wrap == nil"), Is.EqualTo(true));
                Assert.That(host.Eval("return coroutine.close == nil"), Is.EqualTo(true));
            }
        }

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

        [Test]
        public void RaiseGroupStateChanged_NullGroup_DoesNotThrow()
        {
            using (var host = new LuaScriptHost())
            {
                Assert.DoesNotThrow(() => host.RaiseGroupStateChanged(null));
            }
        }

        [Test]
        public void RaiseRoomStateChanged_NullMonsters_DoesNotThrow()
        {
            using (var host = new LuaScriptHost())
            {
                Assert.DoesNotThrow(() => host.RaiseRoomStateChanged(null));
            }
        }

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
            // any of the pre-existing tests built against it.
            string sentCommand = null;
            using (var host = new LuaScriptHost(cmd => sentCommand = cmd))
            {
                host.Eval("SendCommand('атаковать крысу')");
                Assert.That(sentCommand, Is.EqualTo("атаковать крысу"));
            }
        }
    }
}
