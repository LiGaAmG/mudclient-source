namespace Adan.Client.ViewModel
{
    using System.ComponentModel;
    using CSLib.Net.Annotations;

    /// <summary>
    /// View model for connection dialog.
    /// </summary>
    public class ConnectionDialogViewModel : INotifyPropertyChanged
    {
        private string _proxyHost;
        private int _proxyPort;

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets or sets the name of the host.
        /// </summary>
        [NotNull]
        public string HostName
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the port.
        /// </summary>
        public int Port
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the SOCKS5 proxy hostname.
        /// </summary>
        [NotNull]
        public string ProxyHost
        {
            get
            {
                return _proxyHost ?? string.Empty;
            }
            set
            {
                _proxyHost = value;
                OnPropertyChanged("ProxyHost");
            }
        }

        /// <summary>
        /// Gets or sets the SOCKS5 proxy port.
        /// </summary>
        public int ProxyPort
        {
            get
            {
                return _proxyPort;
            }
            set
            {
                _proxyPort = value;
                OnPropertyChanged("ProxyPort");
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
