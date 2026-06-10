using System.Windows;
using System.Windows.Controls;

namespace Adan.Client.Plugins.SpellManager
{
    /// <summary>
    /// Code-behind for SpellCounterControl.
    /// </summary>
    public partial class SpellCounterControl : UserControl
    {
        public SpellCounterControl()
        {
            InitializeComponent();
        }

        private void IncreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            var model = DataContext as SpellTabModel;
            if (model != null) model.IncreaseFontSize();
        }

        private void DecreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            var model = DataContext as SpellTabModel;
            if (model != null) model.DecreaseFontSize();
        }
    }
}
