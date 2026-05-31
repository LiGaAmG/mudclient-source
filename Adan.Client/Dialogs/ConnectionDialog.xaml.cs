namespace Adan.Client.Dialogs
{
    using System.Windows;
    using System.Windows.Threading;
    using Adan.Client.Common.Networking;
    using Adan.Client.ViewModel;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// Interaction logic for ConnectionDialog.xaml
    /// </summary>
    public partial class ConnectionDialog
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionDialog"/> class.
        /// </summary>
        public ConnectionDialog()
        {
            InitializeComponent();
        }

        private void HandleOkClicked([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            DialogResult = true;
        }

        private void HandleCheckProxyClicked([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            var viewModel = DataContext as ConnectionDialogViewModel;
            if (viewModel == null)
                return;

            var proxyHost = viewModel.ProxyHost;
            int proxyPort = viewModel.ProxyPort;

            if (string.IsNullOrWhiteSpace(proxyHost) || proxyPort <= 0)
            {
                MessageBox.Show(this, "Enter proxy host and port first.", "Check proxy", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var testHost = viewModel.HostName;
            var testPort = viewModel.Port;

            if (string.IsNullOrWhiteSpace(testHost) || testPort <= 0)
            {
                MessageBox.Show(this, "Enter target server host and port first (needed for test).", "Check proxy", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                button.IsEnabled = false;
                button.Content = "Testing...";
            }

            // Run proxy test in background to not freeze UI
            var dispatcher = Dispatcher;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string result = TelnetClient.TestSocks5Proxy(proxyHost, proxyPort, testHost, testPort);

                dispatcher.Invoke(() =>
                {
                    if (button != null)
                    {
                        button.IsEnabled = true;
                        button.Content = "Check proxy";
                    }

                    if (result == null)
                    {
                        MessageBox.Show(this, "Proxy is working!", "Check proxy", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(this, "Proxy test failed:\n" + result, "Check proxy", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });
            });
        }
    }
}
