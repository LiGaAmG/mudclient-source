using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adan.Client.Common.Model;
using Adan.Client.Common.Settings;
using NUnit.Framework;

namespace Adan.Client.Common.Tests.Model
{
    /// <summary>
    /// RootModel.EnabledTriggersOrderedByPriority is a cached snapshot of
    /// Groups.Where(IsEnabled).SelectMany(Triggers), rebuilt only when
    /// RecalculatedEnabledTriggersPriorities() is called explicitly (the way
    /// EnableGroup/DisableGroup already do). Editing a trigger's MatchingPattern
    /// through the Triggers editor mutates the same Group.Triggers list in place,
    /// but nothing on that path called the recalculation -- so the cached snapshot
    /// kept matching against the trigger object as it was before the edit until
    /// something unrelated (toggling a group, reconnecting) forced a rebuild.
    /// </summary>
    [TestFixture]
    public class RootModelTriggerCacheTests
    {
        [SetUp]
        public void SetUp()
        {
            // RootModel.Groups pulls in SettingsHolder.Instance.Settings.GlobalGroups.
            // Bypass the real Initialize()/disk-backed settings folder and inject an
            // empty SettingsSerializer via reflection so the test doesn't touch disk.
            var settingsProperty = typeof(SettingsHolder).GetProperty("Settings", BindingFlags.Public | BindingFlags.Instance);
            settingsProperty.SetValue(SettingsHolder.Instance, new SettingsSerializer());
        }

        [Test]
        public void EditedTrigger_IsInvisibleToEnabledTriggersCache_UntilRecalculated()
        {
            var group = new Group { Name = "Test", IsEnabled = true, IsBuildIn = false };
            var oldTrigger = new TextTrigger { MatchingPattern = "old" };
            group.Triggers.Add(oldTrigger);

            var profile = new ProfileHolder("test-profile") { Groups = new List<Group> { group } };
            var rootModel = new RootModel(profile);

            // First access builds and caches the snapshot.
            var initial = rootModel.EnabledTriggersOrderedByPriority.OfType<TextTrigger>().ToList();
            Assert.AreEqual("old", initial.Single().MatchingPattern);

            // Same swap the trigger editor does on save: remove the old TextTrigger
            // instance from the group, insert the edited one in its place.
            var newTrigger = new TextTrigger { MatchingPattern = "new" };
            group.Triggers.Remove(oldTrigger);
            group.Triggers.Insert(0, newTrigger);

            // Without an explicit recalculation, the cache still serves the stale trigger.
            var stale = rootModel.EnabledTriggersOrderedByPriority.OfType<TextTrigger>().ToList();
            Assert.AreEqual("old", stale.Single().MatchingPattern,
                "documents the cache staleness bug: edited trigger isn't visible without RecalculatedEnabledTriggersPriorities()");

            rootModel.RecalculatedEnabledTriggersPriorities();

            var fresh = rootModel.EnabledTriggersOrderedByPriority.OfType<TextTrigger>().ToList();
            Assert.AreEqual("new", fresh.Single().MatchingPattern,
                "after explicit recalculation the edited trigger must be visible");
        }
    }
}
