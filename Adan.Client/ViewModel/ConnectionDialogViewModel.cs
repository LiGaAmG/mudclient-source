namespace Adan.Client.ViewModel
{
    using System.ComponentModel;
    using CSLib.Net.Annotations;

    /// <summary>
    /// View model for connection dialog.
    /// </summary>
    public class ConnectionDialogViewModel : INotifyPropertyChanged
    {
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

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
