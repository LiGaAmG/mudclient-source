namespace Adan.Client.Plugins.OutputWindow
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.Linq;
    using System.Windows;
    using Common.Conveyor;
    using Common.Model;
    using Common.Plugins;
    using Common.ViewModel;
    using ConveyorUnits;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Model;
    using Model.Actions;
    using ViewModel;

    /// <summary>
    /// A <see cref="PluginBase"/> implementation to add additional output window.
    /// </summary>
    [Export(typeof(PluginBase))]
    public sealed class OutputToAdditionalWindowPlugin : PluginBase
    {
        private readonly AdditionalOutputWindowManager _manager;
        private readonly WidgetDescription _widget;
        private readonly AdditionalOutputWindowManager _manager2;
        private readonly WidgetDescription _widget2;

        public override string Name
        {
            get { return "OutputAdditionalWindow"; }
        }

        public OutputToAdditionalWindowPlugin()
        {
            var viewModel = new AdditionalOutputWindowsViewModel();
            var additionalOutputWindowControl = new AdditionalOutputWindow(viewModel);
            _manager = new AdditionalOutputWindowManager(viewModel);

            _widget = new WidgetDescription("AdditionalOutputWindow", "Additional output", additionalOutputWindowControl)
            {
                Left = (int)SystemParameters.PrimaryScreenWidth - 400,
                Height = 300,
                Width = 400,
                ResizeToContent = false
            };

            var viewModel2 = new AdditionalOutputWindowsViewModel();
            var additionalOutputWindowControl2 = new AdditionalOutputWindow(viewModel2);
            _manager2 = new AdditionalOutputWindowManager(viewModel2);

            _widget2 = new WidgetDescription("AdditionalOutputWindow2", "Additional output 2", additionalOutputWindowControl2)
            {
                Left = (int)SystemParameters.PrimaryScreenWidth - 400,
                Top = 320,
                Height = 300,
                Width = 400,
                ResizeToContent = false
            };
        }

        public override IEnumerable<WidgetDescription> Widgets
        {
            get { return new[] { _widget, _widget2 }; }
        }

        public override IEnumerable<ActionDescription> CustomActions
        {
            get
            {
                return new ActionDescription[]
                {
                    new OutputToAdditionalWindowActionDescription(RootModel.AllParameterDescriptions, RootModel.AllActionDescriptions),
                    new OutputToAdditionalWindow2ActionDescription(RootModel.AllParameterDescriptions, RootModel.AllActionDescriptions),
                };
            }
        }

        public override IEnumerable<string> PluginXamlResourcesToMerge
        {
            get
            {
                return new[]
                {
                    @"/Adan.Client.Plugins.OutputWindow;component/OutputToAdditionalWindowActionEditingTemplate.xaml",
                    @"/Adan.Client.Plugins.OutputWindow;component/OutputToAdditionalWindow2ActionEditingTemplate.xaml",
                };
            }
        }

        public override IEnumerable<Type> CustomSerializationTypes
        {
            get
            {
                return new[] { typeof(OutputToAdditionalWindowAction), typeof(OutputToAdditionalWindow2Action) };
            }
        }

        /// <summary>
        /// Initializes this plugins with a specified <see cref="MessageConveyor"/> and <see cref="RootModel"/>.
        /// </summary>
        /// <param name="initializationStatusModel">The initialization status model.</param>
        /// <param name="MainWindowEx">The main window.</param>
        public override void Initialize(InitializationStatusModel initializationStatusModel, [NotNull] Window MainWindowEx)
        {
            Assert.ArgumentNotNull(initializationStatusModel, "initializationStatusModel");

            initializationStatusModel.CurrentPluginName = "Additional window";
            initializationStatusModel.PluginInitializationStatus = "Initializing";
        }

        public override void InitializeConveyor(MessageConveyor conveyor)
        {
            conveyor.AddConveyorUnit(new OutputToAdditionalWindowConveyorUnit(_manager, conveyor));
            conveyor.AddConveyorUnit(new OutputToAdditionalWindow2ConveyorUnit(_manager2, conveyor));
        }

        public override void OnCreatedOutputWindow(RootModel rootModel)
        {
            _manager.OutputWindowCreated(rootModel);
            _manager2.OutputWindowCreated(rootModel);
        }

        public override void OnChangedOutputWindow(RootModel rootModel)
        {
            _manager.OutputWindowChanged(rootModel);
            _manager2.OutputWindowChanged(rootModel);
        }

        public override void OnClosedOutputWindow(RootModel rootModel)
        {
            _manager.OutputWindowClosed(rootModel);
            _manager2.OutputWindowClosed(rootModel);
        }
    }
}
