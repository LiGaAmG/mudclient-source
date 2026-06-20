namespace Adan.Client.Dialogs
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows;

    using ViewModel;

    public partial class HelpWindow : Window, INotifyPropertyChanged
    {
        private string _searchText = string.Empty;
        private HelpTopic _selectedTopic;
        private HelpListEntry _selectedEntry;
        private List<HelpListEntry> _entries;

        public HelpWindow()
        {
            InitializeComponent();
            DataContext = this;
            RebuildEntries();
            SelectedEntry = _entries.FirstOrDefault(e => !e.IsHeader);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                _searchText = value;
                OnPropertyChanged("SearchText");
                RebuildEntries();
                OnPropertyChanged("Entries");
            }
        }

        public List<HelpListEntry> Entries
        {
            get { return _entries; }
        }

        public HelpListEntry SelectedEntry
        {
            get { return _selectedEntry; }
            set
            {
                _selectedEntry = value;
                OnPropertyChanged("SelectedEntry");
                if (value != null && !value.IsHeader)
                {
                    SelectedTopic = value.Topic;
                }
            }
        }

        public HelpTopic SelectedTopic
        {
            get { return _selectedTopic; }
            private set
            {
                _selectedTopic = value;
                OnPropertyChanged("SelectedTopic");
            }
        }

        private void RebuildEntries()
        {
            IEnumerable<HelpTopic> source = HelpTopics.All;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                source = HelpTopics.All.Where(t =>
                    t.SearchableText.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var entries = new List<HelpListEntry>();
            string lastCategory = null;
            foreach (var topic in source)
            {
                if (topic.Category != lastCategory)
                {
                    entries.Add(HelpListEntry.ForHeader(topic.Category));
                    lastCategory = topic.Category;
                }

                entries.Add(HelpListEntry.ForTopic(topic));
            }

            _entries = entries;
        }

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
