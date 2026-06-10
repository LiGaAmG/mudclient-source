using System.ComponentModel;

namespace Adan.Client.Plugins.SpellManager
{
    public class GlobalSpellEntryViewModel : INotifyPropertyChanged
    {
        private readonly SpellEntry _entry;

        public event PropertyChangedEventHandler PropertyChanged;

        public GlobalSpellEntryViewModel(string tabUid, string displayName, SpellEntry entry)
        {
            TabUid = tabUid;
            DisplayName = displayName;
            _entry = entry;
            _entry.PropertyChanged += OnEntryChanged;
        }

        public string TabUid { get; private set; }
        public string DisplayName { get; set; }
        public string SpellName { get { return _entry.Name; } }
        public int Memorized { get { return _entry.Memorized; } }

        public void Detach()
        {
            _entry.PropertyChanged -= OnEntryChanged;
        }

        private void OnEntryChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Memorized")
                Notify("Memorized");
            else if (e.PropertyName == "Name")
                Notify("SpellName");
        }

        private void Notify(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
