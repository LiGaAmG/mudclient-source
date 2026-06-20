using System.Collections.Generic;
using NUnit.Framework;
using Adan.Client.Common.Model;
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
        public void RegisterGroupStateHandler_FiresWithMemberData()
        {
            using (var host = new LuaScriptHost())
            {
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

                var capturedName = (string)host.Eval("return last_name");
                var capturedHits = (double)host.Eval("return last_hits");

                Assert.That(capturedName, Is.EqualTo("Нимриэль"));
                Assert.That(capturedHits, Is.EqualTo(73.5).Within(0.001));
            }
        }

        [Test]
        public void RaiseGroupStateChanged_ExposesAllCharacterStatusFields()
        {
            using (var host = new LuaScriptHost())
            {
                host.LoadScript(@"
                    function on_group_state(group)
                        local m = group[1]
                        last_position = m.Position
                        last_in_same_room = m.InSameRoom
                        last_is_attacked = m.IsAttacked
                        last_moves = m.MovesPercent
                        last_affect_name = m.Affects[1].Name
                        last_affect_rounds = m.Affects[1].Rounds
                    end
                ");
                host.RegisterGroupStateHandler("on_group_state");

                var member = new CharacterStatus
                {
                    Name = "Тазерал",
                    Position = Position.Fighting,
                    InSameRoom = true,
                    IsAttacked = true,
                    MovesPercent = 42.0f
                };
                member.Affects.Add(new Affect { Name = "Berserk", Rounds = 3 });

                host.RaiseGroupStateChanged(new List<CharacterStatus> { member });

                Assert.That(host.Eval("return last_position"), Is.EqualTo("Fighting"));
                Assert.That(host.Eval("return last_in_same_room"), Is.EqualTo(true));
                Assert.That(host.Eval("return last_is_attacked"), Is.EqualTo(true));
                Assert.That(host.Eval("return last_moves"), Is.EqualTo(42.0).Within(0.001));
                Assert.That(host.Eval("return last_affect_name"), Is.EqualTo("Berserk"));
                Assert.That(host.Eval("return last_affect_rounds"), Is.EqualTo(3));
            }
        }

        [Test]
        public void RaiseRoomStateChanged_ExposesMonsterOnlyFields()
        {
            using (var host = new LuaScriptHost())
            {
                host.LoadScript(@"
                    function on_room_state(monsters)
                        last_is_boss = monsters[1].IsBoss
                        last_is_player = monsters[1].IsPlayerCharacter
                    end
                ");
                host.RegisterRoomStateHandler("on_room_state");

                var monster = new MonsterStatus { Name = "Хозяин", IsBoss = true, IsPlayerCharacter = false };
                host.RaiseRoomStateChanged(new List<MonsterStatus> { monster });

                Assert.That(host.Eval("return last_is_boss"), Is.EqualTo(true));
                Assert.That(host.Eval("return last_is_player"), Is.EqualTo(false));
            }
        }

        [Test]
        public void RaiseRoomChanged_WithRoomInfo_ExposesLocalMapData()
        {
            using (var host = new LuaScriptHost())
            {
                host.LoadScript(@"
                    function on_room_change(roomId, zoneId, room)
                        last_room_id = roomId
                        last_zone_name = room.ZoneName
                        last_room_name = room.Name
                        last_alias = room.Alias
                        last_has_herb = room.HasHerb
                        last_exit_dir = room.Exits[1].Direction
                        last_exit_room = room.Exits[1].RoomId
                    end
                ");
                host.RegisterRoomChangeHandler("on_room_change");

                var roomInfo = new RoomInfo
                {
                    ZoneName = "Минас-Тирит",
                    Name = "Дерево",
                    Alias = "домашняя клетка",
                    HasHerb = true,
                };
                roomInfo.Exits.Add(new RoomExitInfo { Direction = "North", RoomId = 42 });

                host.RaiseRoomChanged(1842, 12, roomInfo);

                Assert.That(host.Eval("return last_room_id"), Is.EqualTo(1842));
                Assert.That(host.Eval("return last_zone_name"), Is.EqualTo("Минас-Тирит"));
                Assert.That(host.Eval("return last_room_name"), Is.EqualTo("Дерево"));
                Assert.That(host.Eval("return last_alias"), Is.EqualTo("домашняя клетка"));
                Assert.That(host.Eval("return last_has_herb"), Is.EqualTo(true));
                Assert.That(host.Eval("return last_exit_dir"), Is.EqualTo("North"));
                Assert.That(host.Eval("return last_exit_room"), Is.EqualTo(42));
            }
        }

        [Test]
        public void RaiseRoomChanged_NullRoomInfo_PassesNilForRoomTable()
        {
            using (var host = new LuaScriptHost())
            {
                host.LoadScript(@"
                    function on_room_change(roomId, zoneId, room)
                        last_room_is_nil = (room == nil)
                    end
                ");
                host.RegisterRoomChangeHandler("on_room_change");

                Assert.DoesNotThrow(() => host.RaiseRoomChanged(1842, 12, null));
                Assert.That(host.Eval("return last_room_is_nil"), Is.EqualTo(true));
            }
        }

        [Test]
        public void RaiseRoomChanged_OldTwoArgFunction_StillWorks()
        {
            // Scripts written before this change as
            // function on_room_change(roomId, zoneId) must keep working --
            // Lua silently ignores the extra third argument.
            using (var host = new LuaScriptHost())
            {
                host.LoadScript(@"
                    function on_room_change(roomId, zoneId)
                        last_sum = roomId + zoneId
                    end
                ");
                host.RegisterRoomChangeHandler("on_room_change");

                host.RaiseRoomChanged(100, 23, new RoomInfo());

                Assert.That(host.Eval("return last_sum"), Is.EqualTo(123));
            }
        }

        [Test]
        public void OneScript_CanRegisterAllThreeHandlersAtOnce()
        {
            // Demonstrates the actual answer to "can one script react to
            // several packet types": yes -- LuaScriptHost places no
            // restriction on registering all three handler kinds
            // simultaneously, as long as the single shared Lua state
            // defines all three functions. RootModel.ReloadScripts() is
            // what decides, per script, which of the three Register*Handler
            // calls to make (now driven by three independent checkboxes,
            // not a single Handler choice).
            using (var host = new LuaScriptHost())
            {
                host.LoadScript(@"
                    function on_group_state(group) last_group_count = #group end
                    function on_room_state(monsters) last_monster_count = #monsters end
                    function on_room_change(roomId, zoneId, room) last_room_id = roomId end
                ");
                host.RegisterGroupStateHandler("on_group_state");
                host.RegisterRoomStateHandler("on_room_state");
                host.RegisterRoomChangeHandler("on_room_change");

                host.RaiseGroupStateChanged(new List<CharacterStatus> { new CharacterStatus() });
                host.RaiseRoomStateChanged(new List<MonsterStatus> { new MonsterStatus(), new MonsterStatus() });
                host.RaiseRoomChanged(777, 1, null);

                Assert.That(host.Eval("return last_group_count"), Is.EqualTo(1));
                Assert.That(host.Eval("return last_monster_count"), Is.EqualTo(2));
                Assert.That(host.Eval("return last_room_id"), Is.EqualTo(777));
            }
        }

        [Test]
        public void RaiseGroupStateChanged_NoHandlerRegistered_DoesNothing()
        {
            using (var host = new LuaScriptHost())
            {
                Assert.DoesNotThrow(() => host.RaiseGroupStateChanged(new List<CharacterStatus>()));
            }
        }

        [Test]
        public void RaiseRoomStateChanged_FiresRegisteredHandlerWithMonsterData()
        {
            using (var host = new LuaScriptHost())
            {
                host.LoadScript(@"
                    function on_room_state(monsters)
                        last_monster_count = #monsters
                    end
                ");
                host.RegisterRoomStateHandler("on_room_state");

                var monsters = new List<MonsterStatus>
                {
                    new MonsterStatus { Name = "крыса" },
                    new MonsterStatus { Name = "паук" }
                };

                host.RaiseRoomStateChanged(monsters);

                var count = host.Eval("return last_monster_count");
                Assert.That(count, Is.EqualTo(2));
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
    }
}
