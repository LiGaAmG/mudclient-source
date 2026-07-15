using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Adan.Client.Common.Scripting;

namespace Adan.Client.Common.Tests.Scripting
{
    [TestFixture]
    public class ScriptFileManagerTests
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "adan-script-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        [Test]
        public void SaveMetadata_WritesAdjacentJsonForScript()
        {
            File.WriteAllText(Path.Combine(_folder, "heal.lua"), "return true");
            using (var manager = new ScriptFileManager(_folder))
            {
                var script = manager.Entries.Single();
                script.IsGlobal = true;
                script.AutoStart = true;
                manager.SaveMetadata();
            }

            var metadataPath = Path.Combine(_folder, "heal.script.json");
            Assert.That(File.Exists(metadataPath), Is.True);
            StringAssert.Contains("\"global\":true", File.ReadAllText(metadataPath));
        }

        [Test]
        public void GetApplicableScripts_IncludesGlobalForNewProfile()
        {
            File.WriteAllText(Path.Combine(_folder, "shared.lua"), "return true");
            using (var manager = new ScriptFileManager(_folder))
            {
                manager.Entries.Single().IsGlobal = true;
                manager.SaveMetadata();

                Assert.That(manager.GetApplicableScripts("new-profile").Select(s => s.FileName),
                    Is.EquivalentTo(new[] { "shared.lua" }));
            }
        }

        [Test]
        public void GetApplicableScripts_UsesProfileNamesForNonGlobalScript()
        {
            File.WriteAllText(Path.Combine(_folder, "heal.lua"), "return true");
            using (var manager = new ScriptFileManager(_folder))
            {
                var script = manager.Entries.Single();
                script.EnabledProfileNames.Add("Mage");
                manager.SaveMetadata();

                Assert.That(manager.GetApplicableScripts("Mage").Count, Is.EqualTo(1));
                Assert.That(manager.GetApplicableScripts("Warrior"), Is.Empty);
            }
        }

        [Test]
        public void InvalidAdjacentMetadata_UsesSafeDefaults()
        {
            File.WriteAllText(Path.Combine(_folder, "broken.lua"), "return true");
            File.WriteAllText(Path.Combine(_folder, "broken.script.json"), "not json");

            using (var manager = new ScriptFileManager(_folder))
            {
                var script = manager.Entries.Single();
                Assert.That(script.IsGlobal, Is.False);
                Assert.That(script.AutoStart, Is.False);
                Assert.That(script.EnabledProfileNames, Is.Empty);
            }
        }
    }
}
