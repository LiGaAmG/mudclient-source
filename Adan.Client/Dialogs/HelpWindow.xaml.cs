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

        public HelpWindow()
        {
            InitializeComponent();
            DataContext = this;
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
                OnPropertyChanged("FilteredTopics");
            }
        }

        public IEnumerable<HelpTopic> FilteredTopics
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    return HelpTopics.All;
                }

                return HelpTopics.All.Where(t =>
                    t.Title.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Content.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }
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
