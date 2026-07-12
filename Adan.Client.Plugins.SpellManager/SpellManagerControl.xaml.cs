using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var oldModel = e.OldValue as SpellTabModel;
            if (oldModel != null)
                oldModel.SpellFilterChanged -= OnSpellFilterChanged;

            var newModel = e.NewValue as SpellTabModel;
            if (newModel != null)
            {
                newModel.SpellFilterChanged += OnSpellFilterChanged;
                var view = CollectionViewSource.GetDefaultView(newModel.Spells);
                view.Filter = o => !newModel.HideZeroDesired || ((SpellEntry)o).Desired > 0;
            }
        }

        private void OnSpellFilterChanged(object sender, EventArgs e)
        {
            var model = DataContext as SpellTabModel;
            if (model == null) return;
            var view = CollectionViewSource.GetDefaultView(model.Spells);
            view.Refresh();
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

        private bool _suppressPresetChange = false;

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressPresetChange) return;
            var model = DataContext as SpellTabModel;
            if (model == null) return;
            var selected = PresetComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;
            _suppressPresetChange = true;
            try { model.LoadPreset(selected); }
            finally { _suppressPresetChange = false; }
        }

        private void SavePresetButton_Click(object sender, RoutedEventArgs e)
        {
            var model = DataContext as SpellTabModel;
            if (model == null) return;
            var name = NewPresetNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                // Use currently selected preset name if box is empty
                name = PresetComboBox.SelectedItem as string;
            }
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Введите имя пресета.", "Пресет", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _suppressPresetChange = true;
            try
            {
                model.SaveCurrentAsPreset(name);
                PresetComboBox.SelectedItem = name;
                NewPresetNameBox.Text = string.Empty;
            }
            finally { _suppressPresetChange = false; }
        }

        private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
        {
            var model = DataContext as SpellTabModel;
            if (model == null) return;
            var name = PresetComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) return;
            if (MessageBox.Show(string.Format("Удалить пресет '{0}'?", name), "Пресет",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                model.DeletePreset(name);
            }
        }
    }
}
