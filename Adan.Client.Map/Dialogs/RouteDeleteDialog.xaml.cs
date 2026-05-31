// --------------------------------------------------------------------------------------------------------------------
// <copyright file="RouteDeleteDialog.xaml.cs" company="Adamand MUD">
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

    using Model;

    /// <summary>
    /// Interaction logic for RouteStartDialog.xaml
    /// </summary>
    public partial class RouteDeleteDialog
    {
        private ICollectionView _view;

        /// <summary>
        /// Initializes a new instance of the <see cref="RouteDeleteDialog"/> class.
        /// </summary>
        public RouteDeleteDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            var routeManager = DataContext as RouteManager;
            if (routeManager == null)
                return;

            _view = CollectionViewSource.GetDefaultView(routeManager.AllRoutes.ToList());
            RoutesList.ItemsSource = _view;

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
                _view.Filter = o =>
                {
                    var r = o as Route;
                    if (r == null) return false;
                    return r.StartName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                        || r.EndName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                };
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
