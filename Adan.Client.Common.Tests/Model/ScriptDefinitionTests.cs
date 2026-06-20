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
