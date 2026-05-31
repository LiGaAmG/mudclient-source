// --------------------------------------------------------------------------------------------------------------------
// <copyright file="RouteDestinationSelectDialog.xaml.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Interaction logic for RouteDescrinationSelectDialog.xaml
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.Map.Dialogs
{
    using System;
    using System.Linq;
    using System.Windows;
    using System.ComponentModel;
    using System.Windows.Controls;
    using System.Windows.Data;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// Interaction logic for RouteStartDialog.xaml
    /// </summary>
    public partial class RouteDestinationSelectDialog
    {
        private ICollectionView _view;

        /// <summary>
        /// Gets or sets the initial filter text (e.g. when opened from a partial command).
        /// </summary>
        public string InitialFilter { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RouteDestinationSelectDialog"/> class.
        /// </summary>
        public RouteDestinationSelectDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            var routeManager = DataContext as RouteManager;
            if (routeManager == null)
                return;

            _view = CollectionViewSource.GetDefaultView(routeManager.AvailableDestinations.ToList());
            RoutesList.ItemsSource = _view;

            if (!string.IsNullOrEmpty(InitialFilter))
            {
                SearchBox.Text = InitialFilter;
                SearchBox.CaretIndex = InitialFilter.Length;
            }

            SearchBox.Focus();
        }

        private void SearchBox_TextChanged([NotNull] object sender, [NotNull] TextChangedEventArgs e)
        {
            if (_view == null)
                return;

            var text = SearchBox.Text;
            if (string.IsNullOrEmpty(text))
            {
                _view.Filter = null;
            }
            else
            {
                _view.Filter = o => ((string)o).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (_view.Cast<object>().Any())
                RoutesList.SelectedIndex = 0;
        }

        private void HandleOkClicked([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            DialogResult = true;
        }
    }
}
