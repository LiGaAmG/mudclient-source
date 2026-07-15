using NUnit.Framework;
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Tests.Events
{
    [TestFixture]
    public class GameEventExtractorTests
    {
        private GameEventExtractor _extractor;
        private GameSessionState _state;

        [SetUp]
        public void SetUp()
        {
            _extractor = new GameEventExtractor();
            _state = new GameSessionState();
        }

        [Test]
        public void MobKilledLine_ReturnsMobKilledEvent()
        {
            var ev = _extractor.TryExtract("Тёмный тролль убит.", _state);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.Type, Is.EqualTo(GameEventType.MobKilled));
            Assert.That(ev.EntityName, Is.EqualTo("Тёмный тролль"));
        }

        [Test]
        public void MobSeenLine_ReturnsMobSeenEvent()
        {
            var ev = _extractor.TryExtract("Здесь бродит старый тролль.", _state);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.Type, Is.EqualTo(GameEventType.MobSeen));
        }

        [Test]
        public void ItemPickedUp_ReturnsItemPickedUpEvent()
        {
            var ev = _extractor.TryExtract("Вы взяли серебряный ключ.", _state);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.Type, Is.EqualTo(GameEventType.ItemPickedUp));
            Assert.That(ev.EntityName, Is.EqualTo("серебряный ключ"));
        }

        [Test]
        public void NullLine_ReturnsNull()
        {
            var ev = _extractor.TryExtract(null, _state);
            Assert.That(ev, Is.Null);
        }

        [Test]
        public void MovementCommand_ReturnsMovedEvent()
        {
            _state.LastPlayerCommand = "n";
            var ev = _extractor.TryExtract("Перекрёсток дорог", _state);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.Type, Is.EqualTo(GameEventType.PlayerMoved));
            Assert.That(ev.Direction, Is.EqualTo("n"));
        }

        [Test]
        public void RegularLine_NoMovement_NotMobKilled_ReturnsOtherOrNull()
        {
            _state.LastPlayerCommand = "look";
            var ev = _extractor.TryExtract("Ты смотришь вокруг.", _state);
            if (ev != null)
            {
                Assert.That(ev.Type, Is.Not.EqualTo(GameEventType.MobKilled));
                Assert.That(ev.Type, Is.Not.EqualTo(GameEventType.ItemPickedUp));
            }
        }
    }
}
