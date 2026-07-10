using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using CSLib.Net.Annotations;
using CSLib.Net.Diagnostics;

namespace Adan.Client.Plugins.SpellManager
{
    /// <summary>
    /// Manages per-tab SpellTabModel instances and updates UI controls.
    /// </summary>
    public class SpellManager
    {
        private readonly SpellManagerControl _managerControl;
        private readonly SpellCounterControl _counterControl;
        private readonly GlobalSpellCounterControl _globalCounterControl;
        private readonly GlobalSpellCounterModel _globalModel;
        private readonly Dictionary<string, SpellTabModel> _models = new Dictionary<string, SpellTabModel>();
        private bool _updatePending = false;
        private SpellTabModel _pendingModel = null;

        public SpellManager([NotNull] SpellManagerControl managerControl, [NotNull] SpellCounterControl counterControl, [NotNull] GlobalSpellCounterControl globalCounterControl)
        {
            Assert.ArgumentNotNull(managerControl, "managerControl");
            Assert.ArgumentNotNull(counterControl, "counterControl");
            Assert.ArgumentNotNull(globalCounterControl, "globalCounterControl");
            _managerControl = managerControl;
            _counterControl = counterControl;
            _globalCounterControl = globalCounterControl;

            _globalModel = new GlobalSpellCounterModel();
            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new System.Action(() =>
                {
                    _globalCounterControl.DataContext = _globalModel;

                    // Скрытые виджеты не перепривязываем при переключении табов
                    // (DataGrid менеджера дорог — ~55мс на свитч) — применяем при показе.
                    _managerControl.IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) ApplyPendingModel(_managerControl); };
                    _counterControl.IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) ApplyPendingModel(_counterControl); };
                }));
        }

        private void ApplyPendingModel(System.Windows.FrameworkElement control)
        {
            var m = _pendingModel;
            if (m == null || ReferenceEquals(control.DataContext, m))
                return;
            // Defer to Loaded priority so any in-progress layout/collection refresh
            // finishes first — setting DataContext during IsVisibleChanged causes
            // "DeferRefresh not allowed during AddNew/EditItem" on the inner DataGrid.
            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new System.Action(() => { if (!ReferenceEquals(control.DataContext, _pendingModel)) control.DataContext = _pendingModel; }));
        }

        /// <summary>
        /// Get an existing model or create a new one for the given uid.
        /// </summary>
        public SpellTabModel GetOrCreateModel([NotNull] string uid, string tabName = null)
        {
            Assert.ArgumentNotNullOrWhiteSpace(uid, "uid");

            SpellTabModel model;
            if (!_models.TryGetValue(uid, out model))
            {
                model = new SpellTabModel(uid);
                if (!string.IsNullOrWhiteSpace(tabName))
                    model.TabName = tabName;
                _models[uid] = model;
                _globalModel.AttachTab(uid, tabName ?? uid, model);
            }
            return model;
        }

        public void UpdateTabName([NotNull] string uid, string tabName)
        {
            Assert.ArgumentNotNullOrWhiteSpace(uid, "uid");
            _globalModel.UpdateTabName(uid, tabName);
            SpellTabModel model;
            if (_models.TryGetValue(uid, out model) && !string.IsNullOrWhiteSpace(tabName))
                model.TabName = tabName;
        }

        /// <summary>
        /// Called when a tab is switched to – update per-tab controls' DataContext.
        /// Global counter DataContext never changes.
        /// </summary>
        public void OutputWindowChanged([NotNull] string uid)
        {
            Assert.ArgumentNotNullOrWhiteSpace(uid, "uid");

            var model = GetOrCreateModel(uid);
            if (ReferenceEquals(_managerControl.DataContext, model) &&
                ReferenceEquals(_counterControl.DataContext, model))
                return;

            _pendingModel = model;
            if (_updatePending) return;
            _updatePending = true;

            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new System.Action(() =>
            {
                _updatePending = false;
                var m = _pendingModel;
                if (m == null) return;
                // Перепривязываем только видимые виджеты; скрытые получат модель
                // из _pendingModel при показе (IsVisibleChanged).
                if (_managerControl.IsVisible && !ReferenceEquals(_managerControl.DataContext, m))
                    _managerControl.DataContext = m;
                if (_counterControl.IsVisible && !ReferenceEquals(_counterControl.DataContext, m))
                    _counterControl.DataContext = m;
            }));
        }

        /// <summary>
        /// Called when a tab is closed – save and remove its model.
        /// </summary>
        public void OutputWindowClosed([NotNull] string uid)
        {
            Assert.ArgumentNotNullOrWhiteSpace(uid, "uid");

            SpellTabModel model;
            if (_models.TryGetValue(uid, out model))
            {
                model.Save();
                _globalModel.DetachTab(uid);
                _models.Remove(uid);
            }
        }

        /// <summary>
        /// Get model by uid (may return null).
        /// </summary>
        public SpellTabModel GetModel(string uid)
        {
            SpellTabModel model;
            return _models.TryGetValue(uid, out model) ? model : null;
        }

        /// <summary>
        /// Save all open tabs — called on application exit so Memorized counts survive.
        /// </summary>
        public void SaveAll()
        {
            foreach (var model in _models.Values)
                model.Save();
        }
    }
}
