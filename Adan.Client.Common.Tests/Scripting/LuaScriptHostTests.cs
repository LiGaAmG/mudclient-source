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
    }
}
