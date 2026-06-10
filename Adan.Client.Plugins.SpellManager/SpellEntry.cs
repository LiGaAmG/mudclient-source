using System.ComponentModel;

namespace Adan.Client.Plugins.SpellManager
{
    /// <summary>
    /// Per-spell data for one tab.
    /// </summary>
    public class SpellEntry : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int _circle;
        private int _memorized;
        private int _memorizing;
        private int _desired;
        private bool _isTrackedInCounter;
        private bool _isTrackedGlobally;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged("Name");
                }
            }
        }

        public int Circle
        {
            get { return _circle; }
            set
            {
                if (_circle != value)
                {
                    _circle = value;
                    OnPropertyChanged("Circle");
                }
            }
        }

        public int Memorized
        {
            get { return _memorized; }
            set
            {
                if (_memorized != value)
                {
                    _memorized = value;
                    OnPropertyChanged("Memorized");
                    OnPropertyChanged("Total");
                }
            }
        }

        public int Memorizing
        {
            get { return _memorizing; }
            set
            {
                if (_memorizing != value)
                {
                    _memorizing = value;
                    OnPropertyChanged("Memorizing");
                    OnPropertyChanged("Total");
                }
            }
        }

        public int Desired
        {
            get { return _desired; }
            set
            {
                if (_desired != value)
                {
                    _desired = value;
                    OnPropertyChanged("Desired");
                }
            }
        }

        public bool IsTrackedInCounter
        {
            get { return _isTrackedInCounter; }
            set
            {
                if (_isTrackedInCounter != value)
                {
                    _isTrackedInCounter = value;
                    OnPropertyChanged("IsTrackedInCounter");
                }
            }
        }

        public bool IsTrackedGlobally
        {
            get { return _isTrackedGlobally; }
            set
            {
                if (_isTrackedGlobally != value)
                {
                    _isTrackedGlobally = value;
                    OnPropertyChanged("IsTrackedGlobally");
                }
            }
        }

        /// <summary>
        /// Total = Memorized + Memorizing
        /// </summary>
        public int Total
        {
            get { return _memorized + _memorizing; }
        }

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
