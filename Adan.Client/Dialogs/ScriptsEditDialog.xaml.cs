namespace Adan.Client.Dialogs
{
    using System.Windows;

    public partial class ScriptsEditDialog : Window
    {
        public ScriptsEditDialog()
        {
            InitializeComponent();
        }

        private void HandleCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void HandleHelpClick(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow { Owner = this };
            helpWindow.Show();
        }
    }
}
