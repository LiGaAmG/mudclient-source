using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Adan.Client.Plugins.SpellManager
{
    /// <summary>
    /// Aggregates spells marked IsTrackedGlobally from all tabs into one flat list.
    /// DataContext for the global counter widget — never changes on tab switch.
    /// </summary>
    public class GlobalSpellCounterModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly Dictionary<string, SpellTabModel> _tabs = new Dictionary<string, SpellTabModel>();
        // uid -> display name (tab name)
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();

        public ObservableCollection<GlobalSpellEntryViewModel> Items { get; private set; }

        private int _counterFontSize = 11;
        public int CounterFontSize
        {
            get { return _counterFontSize; }
            set
            {
                if (_counterFontSize != value)
                {
                    _counterFontSize = value;
                    Notify("CounterFontSize");
                    Save();
                }
            }
        }

        public void IncreaseFontSize() { if (CounterFontSize < 24) CounterFontSize++; }
        public void DecreaseFontSize() { if (CounterFontSize > 7) CounterFontSize--; }

        public GlobalSpellCounterModel()
        {
            Items = new ObservableCollection<GlobalSpellEntryViewModel>();
            Load();
        }

        // ---- Сохранение настроек виджета (размер шрифта) ----

        private static string GetSavePath()
        {
            var folder = System.IO.Path.Combine(Adan.Client.Common.Settings.SettingsHolder.Instance.Folder, "SpellManager");
            System.IO.Directory.CreateDirectory(folder);
            return System.IO.Path.Combine(folder, "global_counter.xml");
        }

        private void Save()
        {
            try
            {
                new System.Xml.Linq.XDocument(
                    new System.Xml.Linq.XElement("GlobalSpellCounter",
                        new System.Xml.Linq.XAttribute("counterFontSize", _counterFontSize)))
                    .Save(GetSavePath());
            }
            catch (System.Exception)
            {
                // Best effort
            }
        }

        private void Load()
        {
            try
            {
                var path = GetSavePath();
                if (!System.IO.File.Exists(path)) return;
                var doc = System.Xml.Linq.XDocument.Load(path);
                if (doc.Root != null)
                    _counterFontSize = (int?)doc.Root.Attribute("counterFontSize") ?? 11;
            }
            catch (System.Exception)
            {
                // Best effort
            }
        }

        public void UpdateTabName(string uid, string tabName)
        {
            if (string.IsNullOrWhiteSpace(tabName)) return;
            _names[uid] = tabName;
            foreach (var item in Items.Where(i => i.TabUid == uid))
                item.DisplayName = tabName;
        }

        public void AttachTab(string uid, string tabName, SpellTabModel model)
        {
            if (_tabs.ContainsKey(uid)) return;
            _tabs[uid] = model;
            _names[uid] = string.IsNullOrWhiteSpace(tabName) ? uid : tabName;

            model.Spells.CollectionChanged += (s, e) => OnSpellsChanged(uid, e);

            foreach (var spell in model.Spells)
                SubscribeSpell(uid, spell);
        }

        public void DetachTab(string uid)
        {
            SpellTabModel model;
            if (!_tabs.TryGetValue(uid, out model)) return;
            _tabs.Remove(uid);
            _names.Remove(uid);

            var toRemove = Items.Where(i => i.TabUid == uid).ToList();
            foreach (var item in toRemove)
            {
                item.Detach();
                Items.Remove(item);
            }
        }

        private void OnSpellsChanged(string uid, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (SpellEntry spell in e.NewItems)
                    SubscribeSpell(uid, spell);

            if (e.OldItems != null)
                foreach (SpellEntry spell in e.OldItems)
                    UnsubscribeSpell(uid, spell);
        }

        private void SubscribeSpell(string uid, SpellEntry spell)
        {
            spell.PropertyChanged += (s, e) => OnSpellPropertyChanged(uid, (SpellEntry)s, e);
            if (spell.IsTrackedGlobally)
                AddItem(uid, spell);
        }

        private void UnsubscribeSpell(string uid, SpellEntry spell)
        {
            var item = Items.FirstOrDefault(i => i.TabUid == uid && i.SpellName == spell.Name);
            if (item != null)
            {
                item.Detach();
                Items.Remove(item);
            }
        }

        private void OnSpellPropertyChanged(string uid, SpellEntry spell, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "IsTrackedGlobally") return;

            if (spell.IsTrackedGlobally)
                AddItem(uid, spell);
            else
                RemoveItem(uid, spell);
        }

        private void AddItem(string uid, SpellEntry spell)
        {
            if (Items.Any(i => i.TabUid == uid && i.SpellName == spell.Name)) return;

            string displayName;
            if (!_names.TryGetValue(uid, out displayName)) displayName = uid;

            var vm = new GlobalSpellEntryViewModel(uid, displayName, spell);

            int insertAt = Items.Count;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (Items[i].TabUid == uid) { insertAt = i + 1; break; }
            }
            Items.Insert(insertAt, vm);
        }

        private void RemoveItem(string uid, SpellEntry spell)
        {
            var item = Items.FirstOrDefault(i => i.TabUid == uid && i.SpellName == spell.Name);
            if (item != null)
            {
                item.Detach();
                Items.Remove(item);
            }
        }

        private void Notify(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
