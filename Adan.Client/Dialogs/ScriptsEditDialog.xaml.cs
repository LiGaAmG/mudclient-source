namespace Adan.Client.Dialogs
{
    using System;
    using System.IO;
    using System.Windows;
    using System.Windows.Threading;

    using Microsoft.Win32;

    using ViewModel;

    public partial class ScriptsEditDialog : Window
    {
        private readonly DispatcherTimer _statusRefreshTimer;

        public ScriptsEditDialog()
        {
            InitializeComponent();

            _statusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _statusRefreshTimer.Tick += HandleStatusRefreshTick;
            _statusRefreshTimer.Start();
            Closed += (s, e) => _statusRefreshTimer.Stop();
        }

        private void HandleStatusRefreshTick(object sender, EventArgs e)
        {
            var scriptsViewModel = DataContext as ScriptsViewModel;
            if (scriptsViewModel != null)
            {
                scriptsViewModel.RefreshAllStatuses();
            }
        }

        /// <summary>
        /// Raised when the user clicks Save -- the dialog stays open
        /// (unlike Closed, which only fires once, when the window is
        /// actually closing). The owner (ProfileOptionsViewModel) handles
        /// both events with the same apply-and-reload logic.
        /// </summary>
        public event EventHandler SaveRequested;

        private void HandleCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void HandleHelpClick(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow { Owner = this };
            helpWindow.Show();
        }

        private void HandleSaveClick(object sender, RoutedEventArgs e)
        {
            var handler = SaveRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void HandleLoadFromFileClick(object sender, RoutedEventArgs e)
        {
            var scriptsViewModel = DataContext as ScriptsViewModel;
            var selectedScript = scriptsViewModel != null ? scriptsViewModel.SelectedScript : null;
            if (selectedScript == null)
            {
                MessageBox.Show(
                    "Select a script in the list first.",
                    "Load script from file",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var fileDialog = new OpenFileDialog
            {
                Filter = "Lua scripts|*.lua|All files|*.*",
                Multiselect = false
            };

            var result = fileDialog.ShowDialog(this);
            if (result.HasValue && result.Value)
            {
                selectedScript.Code = File.ReadAllText(fileDialog.FileName);
            }
        }
    }
}
