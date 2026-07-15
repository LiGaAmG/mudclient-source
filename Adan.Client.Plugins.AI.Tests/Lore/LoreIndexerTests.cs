using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Lore;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Tests.Lore
{
    [TestFixture]
    public class LoreIndexerTests
    {
        private string _dbPath;
        private string _loreDir;
        private GameMemoryService _memory;
        private LoreIndexer _indexer;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.GetTempFileName() + ".db";
            _loreDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_loreDir);
            var settings = new AiSettings();
            settings.DatabasePath = _dbPath;
            settings.LoreDirectory = _loreDir;
            _memory = new GameMemoryService(settings);
            _memory.Initialize();
            _indexer = new LoreIndexer(settings, _memory);
        }

        [TearDown]
        public void TearDown()
        {
            _memory.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_loreDir)) Directory.Delete(_loreDir, true);
        }

        [Test]
        public void ReindexAll_DoesNotThrow_WhenDirEmpty()
        {
            Assert.DoesNotThrow(() => _indexer.ReindexAll());
        }

        [Test]
        public void ReindexAll_SkipsMissingDir()
        {
            var settings = new AiSettings();
            settings.DatabasePath = _dbPath;
            settings.LoreDirectory = Path.Combine(Path.GetTempPath(), "nonexistent_" + Path.GetRandomFileName());
            var indexer = new LoreIndexer(settings, _memory);
            Assert.DoesNotThrow(() => indexer.ReindexAll());
        }

        [Test]
        public void ReindexAll_ProcessesTextFile_NoExceptions()
        {
            File.WriteAllText(Path.Combine(_loreDir, "test.md"), "# Тёмный лес\nЗдесь водятся тролли.");
            Assert.DoesNotThrow(() => _indexer.ReindexAll());
        }

        [Test]
        public void ReindexAll_SavesLoreDocument()
        {
            string path = Path.Combine(_loreDir, "a.txt");
            File.WriteAllText(path, "Контент");
            var first = _indexer.ReindexAll();

            Assert.That(first.Updated, Is.EqualTo(1));
            Assert.That(_indexer.ReindexAll().Updated, Is.EqualTo(0));
        }

        [Test]
        public void ReindexAll_DoesNotReindexUnchangedFile()
        {
            string path = Path.Combine(_loreDir, "b.txt");
            File.WriteAllText(path, "Контент файла");
            Assert.That(_indexer.ReindexAll().Updated, Is.EqualTo(1));
            Assert.That(_indexer.ReindexAll().Updated, Is.EqualTo(0));
        }

        [Test]
        public void ReindexAll_SkipsNonTextFiles()
        {
            File.WriteAllBytes(Path.Combine(_loreDir, "binary.exe"), new byte[] { 0x4D, 0x5A, 0x00 });
            Assert.DoesNotThrow(() => _indexer.ReindexAll());
        }

        [Test]
        public void SplitIntoChunks_ReturnsAtLeastOneChunk()
        {
            var chunks = LoreIndexer.SplitIntoChunks("Небольшой текст");
            Assert.That(chunks.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void SplitIntoChunks_ExtractsSectionTitle()
        {
            var chunks = LoreIndexer.SplitIntoChunks("# Секция\nКонтент секции");
            Assert.That(chunks[0].Section, Is.EqualTo("Секция"));
        }

        [Test]
        public void SplitIntoChunks_SplitsLargeContent()
        {
            string big = new string('А', 2500);
            var chunks = LoreIndexer.SplitIntoChunks(big);
            Assert.That(chunks.Count, Is.GreaterThan(1));
        }

    }
}
