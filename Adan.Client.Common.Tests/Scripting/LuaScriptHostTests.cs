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
