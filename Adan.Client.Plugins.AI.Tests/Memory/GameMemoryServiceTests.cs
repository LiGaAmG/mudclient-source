using System;
using System.IO;
using NUnit.Framework;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Tests.Memory
{
    [TestFixture]
    public class GameMemoryServiceTests
    {
        private string _dbPath;
        private GameMemoryService _svc;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.GetTempFileName() + ".db";
            var settings = new AiSettings();
            settings.DatabasePath = _dbPath;
            _svc = new GameMemoryService(settings);
            _svc.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            _svc.Dispose();
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        [Test]
        public void UpsertZone_ReturnsSameIdForSameName()
        {
            long id1 = _svc.UpsertZone("Тёмный лес");
            long id2 = _svc.UpsertZone("Тёмный лес");
            Assert.That(id1, Is.EqualTo(id2));
        }

        [Test]
        public void UpsertZone_NormalizesCase()
        {
            long id1 = _svc.UpsertZone("ТЁМНЫЙ ЛЕС");
            long id2 = _svc.UpsertZone("тёмный лес");
            Assert.That(id1, Is.EqualTo(id2));
        }

        [Test]
        public void UpsertRoom_IncreasesVisitCount()
        {
            long zoneId = _svc.UpsertZone("Лес");
            _svc.UpsertRoom(zoneId, "Развилка", null);
            _svc.UpsertRoom(zoneId, "Развилка", null);
            var rooms = _svc.GetRoomsInZone(zoneId);
            Assert.That(rooms[0].VisitCount, Is.EqualTo(2));
        }

        [Test]
        public void ConfirmExit_StoredAndRetrieved()
        {
            long zoneId = _svc.UpsertZone("Лес");
            long r1 = _svc.UpsertRoom(zoneId, "Старт", null);
            long r2 = _svc.UpsertRoom(zoneId, "Финиш", null);
            _svc.ConfirmExit(r1, "n", r2);
            var exits = _svc.GetExitsFromRoom(r1);
            Assert.That(exits.Count, Is.EqualTo(1));
            Assert.That(exits[0].Direction, Is.EqualTo("n"));
            Assert.That(exits[0].ToRoomId, Is.EqualTo(r2));
            Assert.That(exits[0].IsConfirmed, Is.True);
        }

        [Test]
        public void FindShortestPath_TwoHops()
        {
            long zoneId = _svc.UpsertZone("Лес");
            long r1 = _svc.UpsertRoom(zoneId, "A", null);
            long r2 = _svc.UpsertRoom(zoneId, "B", null);
            long r3 = _svc.UpsertRoom(zoneId, "C", null);
            _svc.ConfirmExit(r1, "n", r2);
            _svc.ConfirmExit(r2, "n", r3);
            var path = _svc.FindShortestPath(r1, r3);
            Assert.That(path.Count, Is.EqualTo(3));
            Assert.That(path[0], Is.EqualTo(r1));
            Assert.That(path[1], Is.EqualTo(r2));
            Assert.That(path[2], Is.EqualTo(r3));
        }

        [Test]
        public void FindShortestPath_NoPath_ReturnsEmpty()
        {
            long zoneId = _svc.UpsertZone("Лес");
            long r1 = _svc.UpsertRoom(zoneId, "A", null);
            long r2 = _svc.UpsertRoom(zoneId, "B", null);
            var path = _svc.FindShortestPath(r1, r2);
            Assert.That(path.Count, Is.EqualTo(0));
        }

        [Test]
        public void SaveAndGetRecentEvents()
        {
            long zoneId = _svc.UpsertZone("Лес");
            _svc.SaveEvent(new GameEventRecord
            {
                Timestamp = DateTime.UtcNow,
                EventType = GameEventType.MobKilled,
                ZoneId = zoneId,
                RawText = "Тролль убит!",
                Importance = 3
            });
            var events = _svc.GetRecentEvents(zoneId, 10);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].RawText, Is.EqualTo("Тролль убит!"));
        }

        [Test]
        public void LoreDocumentChanged_TrueForNewDoc()
        {
            Assert.That(_svc.LoreDocumentChanged("test.md", 12345), Is.True);
        }

        [Test]
        public void LoreDocumentChanged_FalseAfterSave()
        {
            _svc.SaveLoreDocument("test.md", "Test", 12345);
            Assert.That(_svc.LoreDocumentChanged("test.md", 12345), Is.False);
        }

        [Test]
        public void LoreDocumentChanged_TrueAfterContentChange()
        {
            _svc.SaveLoreDocument("test.md", "Test", 12345);
            Assert.That(_svc.LoreDocumentChanged("test.md", 99999), Is.True);
        }
    }
}
