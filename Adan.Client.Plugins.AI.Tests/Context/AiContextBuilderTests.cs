using System.IO;
using NUnit.Framework;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Context;
using Adan.Client.Plugins.AI.Lore;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Tests.Context
{
    [TestFixture]
    public class AiContextBuilderTests
    {
        private string _dbPath;
        private GameMemoryService _memory;
        private AiContextBuilder _builder;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.GetTempFileName() + ".db";
            var settings = new AiSettings();
            settings.DatabasePath = _dbPath;
            settings.LoreDirectory = Path.GetTempPath();
            _memory = new GameMemoryService(settings);
            _memory.Initialize();
            var lore = new LoreSearchService(settings, _memory);
            _builder = new AiContextBuilder(settings, _memory, lore);
        }

        [TearDown]
        public void TearDown()
        {
            _memory.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void BuildPrompt_ContainsUserQuestion()
        {
            var session = new GameSessionState();
            session.CurrentZoneName = "Лес";
            string prompt = _builder.BuildPrompt("что за место?", session);
            Assert.That(prompt, Does.Contain("что за место?"));
        }

        [Test]
        public void BuildPrompt_ContainsZoneName()
        {
            var session = new GameSessionState();
            session.CurrentZoneName = "Тёмный лес";
            string prompt = _builder.BuildPrompt("где я?", session);
            Assert.That(prompt, Does.Contain("Тёмный лес"));
        }

        [Test]
        public void BuildPrompt_DoesNotExceedReasonableLength()
        {
            var session = new GameSessionState();
            session.CurrentZoneName = "Лес";
            string prompt = _builder.BuildPrompt("тест", session);
            Assert.That(prompt.Length, Is.LessThan(8000));
        }

        [Test]
        public void BuildPrompt_NeverOmitsUserQuestion()
        {
            var session = new GameSessionState();
            string prompt = _builder.BuildPrompt("секретный вопрос", session);
            Assert.That(prompt, Does.Contain("секретный вопрос"));
        }

        [Test]
        public void BuildPrompt_WithEmptySession_DoesNotThrow()
        {
            var session = new GameSessionState();
            Assert.DoesNotThrow(() => _builder.BuildPrompt("вопрос", session));
        }
    }
}
