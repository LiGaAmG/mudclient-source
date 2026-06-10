// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MapPlugin.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the MapPlugin type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.Map
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;
    using Common.Conveyor;
    using Common.Model;
    using Common.Plugins;
    using Common.Utils;
    using Common.ViewModel;
    using ConveyorUnits;
    using CSLib.Net.Diagnostics;
    using MessageDeserializers;

    /// <summary>
    /// A plugin to display zone map.
    /// </summary>
    [Export(typeof(PluginBase))]
    public sealed class MapPlugin : PluginBase
    {
        private const string PluginToggleConfigFileName = "plugin-toggles.conf";
        private readonly MapControl _mapControl;
        private ZoneManager _zoneManager;
        private RouteManager _routeManager;
        private HerbManager _herbManager;
        private bool _isHerbEnabled = true;

        /// <summary>
        /// 
        /// </summary>
        public MapPlugin()
        {
            _mapControl = new MapControl();
        }

        /// <summary>
        /// 
        /// </summary>
        public override string Name
        {
            get
            {
                return "Map";
            }
        }

        /// <summary>
        /// Gets the widgets of this plugin.
        /// </summary>
        public override IEnumerable<WidgetDescription> Widgets
        {
            get
            {

                return Enumerable.Repeat(new WidgetDescription("Map", "Map", _mapControl)
                {
                    Left = (int)SystemParameters.PrimaryScreenWidth - 400,
                    Top = (int)SystemParameters.PrimaryScreenHeight - 400,
                    Height = 400,
                    Width = 400,
                    ResizeToContent = false
                }, 1);
            }
        }

        public override void InitializeConveyor(MessageConveyor conveyor)
        {
            conveyor.AddConveyorUnit(new RouteUnit(_routeManager, conveyor));
            if (_isHerbEnabled)
            {
                conveyor.AddConveyorUnit(new ConveyorUnits.HerbUnit(_herbManager, conveyor));
            }

            conveyor.AddMessageDeserializer(new CurrentRoomMessageDeserializer(conveyor));
        }

        /// <summary>
        /// 
        /// </summary>
        public override void Dispose()
        {
            _zoneManager?.Dispose();

            base.Dispose();
        }

        /// <summary>
        /// Initializes this plugins with a specified <see cref="MessageConveyor"/> and <see cref="RootModel"/>.
        /// </summary>
        /// <param name="initializationStatusModel">The initialization status model.</param>
        /// <param name="MainWindowEx">The main window.</param>
        public override void Initialize(InitializationStatusModel initializationStatusModel, Window MainWindowEx)
        {
            Assert.ArgumentNotNull(initializationStatusModel, "initializationStatusModel");
            Assert.ArgumentNotNull(MainWindowEx, "MainWindowEx");

            initializationStatusModel.CurrentPluginName = "Map";
            initializationStatusModel.PluginInitializationStatus = "Initializing";

            _isHerbEnabled = IsPluginEnabled("Herb");
            _routeManager = new RouteManager(MainWindowEx);
            if (_isHerbEnabled)
            {
                _herbManager = new HerbManager(_routeManager);
                _herbManager.LoadSettings();
            }

            _mapControl.RouteManager = _routeManager;
            _mapControl.HerbManager = _herbManager;
            _zoneManager = new ZoneManager(_mapControl, MainWindowEx, _routeManager, _herbManager);
            _herbManager?.SetZoneManager(_zoneManager);
            initializationStatusModel.PluginInitializationStatus = "Routes loading";
            _routeManager.LoadRoutes();

            Task.Factory.StartNew(() =>
                {
                    try
                    {
                        MapDownloader.DownloadMaps();
                    }
                    catch (Exception) { }
                });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rootModel"></param>
        public override void OnChangedOutputWindow(RootModel rootModel)
        {
            _routeManager.OutputWindowChanged(rootModel);
            _herbManager?.OutputWindowChanged(rootModel);
            _zoneManager.OutputWindowChanged(rootModel);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rootModel"></param>
        public override void OnCreatedOutputWindow(RootModel rootModel)
        {
            _zoneManager.OutputWindowCreated(rootModel);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rootModel"></param>
        public override void OnClosedOutputWindow(RootModel rootModel)
        {
            _zoneManager.OutputWindowClosed(rootModel.Uid);
        }

        private static bool IsPluginEnabled(string pluginName)
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PluginToggleConfigFileName);
                if (!File.Exists(configPath))
                {
                    return true;
                }

                foreach (var rawLine in File.ReadAllLines(configPath))
                {
                    var line = rawLine == null ? string.Empty : rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
                    {
                        continue;
                    }

                    var name = line.Substring(0, separatorIndex).Trim();
                    if (!name.Equals(pluginName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = line.Substring(separatorIndex + 1).Trim();
                    bool isEnabled;
                    if (TryParseBoolean(value, out isEnabled))
                    {
                        return isEnabled;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Write(string.Format("Failed to read plugin toggle {0}: {1}\r\n{2}", pluginName, ex.Message, ex.StackTrace));
            }

            return true;
        }

        private static bool TryParseBoolean(string value, out bool result)
        {
            if (bool.TryParse(value, out result))
            {
                return true;
            }

            switch (value.ToLowerInvariant())
            {
                case "1":
                case "on":
                case "yes":
                    result = true;
                    return true;
                case "0":
                case "off":
                case "no":
                    result = false;
                    return true;
                default:
                    result = true;
                    return false;
            }
        }
    }
}
