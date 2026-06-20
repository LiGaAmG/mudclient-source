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
    }
}
