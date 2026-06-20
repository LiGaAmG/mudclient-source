namespace Adan.Client.Dialogs
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows;
    using System.Windows.Data;

    using ViewModel;

    public partial class HelpWindow : Window, INotifyPropertyChanged
    {
        private string _searchText = string.Empty;
        private HelpTopic _selectedTopic;
        private ListCollectionView _groupedTopics;

        public HelpWindow()
        {
            InitializeComponent();
            DataContext = this;
            RebuildGroupedTopics();
            SelectedTopic = HelpTopics.All.FirstOrDefault();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                _searchText = value;
                OnPropertyChanged("SearchText");
                RebuildGroupedTopics();
                OnPropertyChanged("GroupedTopics");
            }
        }

        public ICollectionView GroupedTopics
        {
            get { return _groupedTopics; }
        }

        public HelpTopic SelectedTopic
        {
            get { return _selectedTopic; }
            set
            {
                _selectedTopic = value;
                OnPropertyChanged("SelectedTopic");
            }
        }

        private void RebuildGroupedTopics()
        {
            IEnumerable<HelpTopic> source = HelpTopics.All;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                source = HelpTopics.All.Where(t =>
                    t.SearchableText.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }

            _groupedTopics = new ListCollectionView(source.ToList());
            _groupedTopics.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
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
