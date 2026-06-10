using System.Windows.Controls;

namespace Adan.Client.Plugins.SpellManager
{
    public partial class GlobalSpellCounterControl : UserControl
    {
        public GlobalSpellCounterControl()
        {
            InitializeComponent();
        }

        private void IncreaseFontSize_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var model = DataContext as GlobalSpellCounterModel;
            if (model != null) model.IncreaseFontSize();
        }

        private void DecreaseFontSize_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var model = DataContext as GlobalSpellCounterModel;
            if (model != null) model.DecreaseFontSize();
        }
    }
}
