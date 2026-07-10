using System.Windows;
using System.Windows.Controls;

namespace Adan.Client.Plugins.SpellManager
{
    /// <summary>
    /// Code-behind for SpellManagerControl.
    /// </summary>
    public partial class SpellManagerControl : UserControl
    {
        public SpellManagerControl()
        {
            InitializeComponent();
        }

        private void ExecutePlanButton_Click(object sender, RoutedEventArgs e)
        {
            var model = DataContext as SpellTabModel;
            if (model == null) return;

            var plugin = SpellManagerPlugin.Instance;
            if (plugin == null) return;

            var rootModel = plugin.GetActiveRootModel(model.Uid);
            model.ExecutePlan(rootModel);
        }

        private void ForgetAndRememorizeButton_Click(object sender, RoutedEventArgs e)
        {
            var model = DataContext as SpellTabModel;
            if (model == null) return;

            var plugin = SpellManagerPlugin.Instance;
            if (plugin == null) return;

            var rootModel = plugin.GetActiveRootModel(model.Uid);
            model.ForgetAndRememorize(rootModel);
        }

        private void ClearListButton_Click(object sender, RoutedEventArgs e)
        {
            var model = DataContext as SpellTabModel;
            if (model == null) return;

            if (MessageBox.Show("Очистить список заклинаний?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                model.ClearSpells();
            }
        }
    }
}
