using System.Windows;
using System.Windows.Input;

namespace Adan.Client.Windows
{
    public partial class NewScriptNameDialog : Window
    {
        public string ScriptName { get; private set; }

        public NewScriptNameDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => NameBox.Focus();
        }

        private void HandleOkClick(object sender, RoutedEventArgs e)
        {
            ScriptName = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(ScriptName)) return;
            DialogResult = true;
        }

        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) HandleOkClick(null, null);
        }
    }
}
