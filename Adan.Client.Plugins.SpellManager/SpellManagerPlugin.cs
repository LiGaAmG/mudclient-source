using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.Model;
using Adan.Client.Common.Plugins;
using Adan.Client.Common.ViewModel;
using CSLib.Net.Annotations;
using CSLib.Net.Diagnostics;

namespace Adan.Client.Plugins.SpellManager
{
    /// <summary>
    /// Plugin entry point for SpellManager.
    /// </summary>
    [Export(typeof(PluginBase))]
    public sealed class SpellManagerPlugin : PluginBase
    {
        private readonly SpellManagerControl _managerControl;
        private readonly SpellCounterControl _counterControl;
        private readonly GlobalSpellCounterControl _globalCounterControl;
        private SpellManager _manager;

        // Track active tab uid -> rootModel for button handlers
        private readonly Dictionary<string, RootModel> _rootModels = new Dictionary<string, RootModel>();

        /// <summary>
        /// Singleton reference so control code-behind can access it.
        /// </summary>
        public static SpellManagerPlugin Instance { get; private set; }

        public SpellManagerPlugin()
        {
            Instance = this;
            _managerControl = new SpellManagerControl();
            _counterControl = new SpellCounterControl();
            _globalCounterControl = new GlobalSpellCounterControl();
        }

        public override string Name
        {
            get { return "SpellManager"; }
        }

        public override IEnumerable<WidgetDescription> Widgets
        {
            get
            {
                yield return new WidgetDescription("SpellManager", "Заклинания", _managerControl)
                {
                    ResizeToContent = false,
                    Left = (int)SystemParameters.PrimaryScreenWidth - 620,
                };
                yield return new WidgetDescription("SpellCounter", "Счётчик заклинаний", _counterControl)
                {
                    ResizeToContent = false,
                    Left = (int)SystemParameters.PrimaryScreenWidth - 200,
                };
                yield return new WidgetDescription("GlobalSpellCounter", "Все заклинания", _globalCounterControl)
                {
                    ResizeToContent = false,
                    Left = (int)SystemParameters.PrimaryScreenWidth - 200,
                };
            }
        }

        /// <summary>
        /// Called once per conveyor (one per tab) when the conveyor is constructed.
        /// At this point conveyor.RootModel is set and we can get the uid.
        /// </summary>
        public override void InitializeConveyor(MessageConveyor conveyor)
        {
            if (_manager == null) return;

            var uid = conveyor.RootModel != null ? conveyor.RootModel.Uid : null;
            if (string.IsNullOrEmpty(uid)) return;

            var model = _manager.GetOrCreateModel(uid);
            conveyor.AddConveyorUnit(new SpellConveyorUnit(model, conveyor));
        }

        public override void Initialize([NotNull] InitializationStatusModel initializationStatusModel, [NotNull] Window mainWindow)
        {
            Assert.ArgumentNotNull(initializationStatusModel, "initializationStatusModel");
            Assert.ArgumentNotNull(mainWindow, "mainWindow");

            initializationStatusModel.CurrentPluginName = "Менеджер заклинаний";
            initializationStatusModel.PluginInitializationStatus = "Initializing";

            _manager = new SpellManager(_managerControl, _counterControl, _globalCounterControl);
        }

        public override void OnCreatedOutputWindow([NotNull] RootModel rootModel)
        {
            Assert.ArgumentNotNull(rootModel, "rootModel");

            _rootModels[rootModel.Uid] = rootModel;

            // Load saved spell plan for this tab
            if (_manager != null)
            {
                var model = _manager.GetOrCreateModel(rootModel.Uid, rootModel.Name);
                _manager.UpdateTabName(rootModel.Uid, rootModel.Name);
                model.Load();
            }
        }

        public override void OnChangedOutputWindow([NotNull] RootModel rootModel)
        {
            Assert.ArgumentNotNull(rootModel, "rootModel");

            if (_manager != null)
                _manager.OutputWindowChanged(rootModel.Uid);
        }

        public override void OnClosedOutputWindow([NotNull] RootModel rootModel)
        {
            Assert.ArgumentNotNull(rootModel, "rootModel");

            if (_manager != null)
                _manager.OutputWindowClosed(rootModel.Uid);

            _rootModels.Remove(rootModel.Uid);
        }

        /// <summary>
        /// Get the RootModel for a given tab uid (used by button handlers).
        /// </summary>
        public RootModel GetActiveRootModel(string uid)
        {
            RootModel model;
            return _rootModels.TryGetValue(uid, out model) ? model : null;
        }

        public override void Dispose()
        {
            if (_manager != null)
                _manager.SaveAll();
            base.Dispose();
        }
    }
}
