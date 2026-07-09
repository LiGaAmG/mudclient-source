namespace Adan.Client.Map
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Windows;
    using System.Windows.Threading;
    using System.Xml;
    using System.Xml.Serialization;
    using Common.Commands;
    using Common.Dialogs;
    using Common.Messages;
    using Common.Model;
    using Common.Settings;
    using Common.Themes;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Dialogs;
    using Model;
    using Properties;
    using ViewModel;

    /// <summary>
    /// Class to manage routes and navigation.
    /// </summary>
    public sealed class RouteManager
    {
        private readonly XmlSerializer _routesSerializer = new XmlSerializer(typeof(List<Route>));
        private readonly Window _mainWindow;
        private readonly HashSet<int> _routeRoomIdentifiers = new HashSet<int>();
        private readonly HashSet<int> _routeEndRoomIdentifiers = new HashSet<int>();
        private readonly Queue<Tuple<RoomViewModel, ZoneViewModel>> _pendingUpdates = new Queue<Tuple<RoomViewModel, ZoneViewModel>>();

        private RootModel _rootModel;
        private bool _isUpdateInProgress;
        private int _groupMembersCountOnRouteStart;
        private Route _currentlyRecordedRoute;
        private RoomViewModel _currentRoom;
        private ZoneViewModel _currentZone;
        private string _currentRouteTarget = string.Empty;
        private IList<Route> _allRoutes = new List<Route>();

        // Route lookahead (pipeline): how many commands to keep in flight ahead of confirmed position.
        // 0 = disabled (classic one-step mode). Per-tab: each uid has its own value.
        private readonly Dictionary<string, int> _lookaheadPerUid = new Dictionary<string, int>();
        private int _pipelineDepth;  // commands currently in the server queue

        // Lookahead applies only to explicit user "маршрут идти" commands.
        // Herb-driven automated travel needs synchronous per-room control (it reacts
        // to each room individually), so it requests useLookahead: false.
        private int _effectiveLookahead;

        // Track which route+direction the current pipeline was built for.
        // FillPipeline is skipped when minRoute switches mid-pipeline: the in-flight
        // commands for the OLD route are already on the server and can't be cancelled;
        // adding new commands for a DIFFERENT route would send directions relative to
        // a future position, not the actual character position when they execute.
        private Route _pipelineRoute;
        private bool _pipelineGotoStart;

        // Room visited two navigation steps ago — used to detect A→B→A→B oscillation.
        // A true loop is confirmed when newCurrentRoom == _roomTwoStepsAgo AND nextRoomId == _currentRoom.
        // This avoids false positives on legitimate dead-end zones (arrive at W, gather, leave via W).
        private int _roomTwoStepsAgo;

        // Route debug log — toggled with "маршрут лог", written to file for post-analysis.
        private bool _routeLogEnabled;
        private string _routeLogPath;
        private StreamWriter _routeLogWriter;

        // Background tab route state: routes keep running even when the tab is not displayed.
        private sealed class BgTabContext
        {
            public readonly RootModel RootModel;
            public string RouteTarget = string.Empty;
            public int PipelineDepth;
            public int EffectiveLookahead;
            public int GroupMembersCountOnStart;
            public RoomViewModel CurrentRoom;
            public ZoneViewModel CurrentZone;
            public bool IsUpdateInProgress;
            public Route PipelineRoute;
            public bool PipelineGotoStart;
            public readonly Queue<Tuple<RoomViewModel, ZoneViewModel>> PendingUpdates
                = new Queue<Tuple<RoomViewModel, ZoneViewModel>>();
            public BgTabContext(RootModel rootModel) { RootModel = rootModel; }
        }
        private readonly Dictionary<string, BgTabContext> _bgTabContexts = new Dictionary<string, BgTabContext>();


        /// <summary>
        /// Initializes a new instance of the <see cref="RouteManager"/> class.
        /// </summary>
        /// <param name="MainWindowEx">The main window.</param>
        public RouteManager(Window MainWindowEx)
        {
            _rootModel = null;
            _mainWindow = MainWindowEx;

            SelectedRouteDestination = string.Empty;
        }

        /// <summary>
        /// Gets or sets the selected route destination.
        /// </summary>
        /// <value>
        /// The selected route destination.
        /// </value>
        [NotNull]
        public string SelectedRouteDestination
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the selected route.
        /// </summary>
        /// <value>
        /// The selected route.
        /// </value>
        [CanBeNull]
        public Route SelectedRoute
        {
            get;
            set;
        }

        public bool SkipNextUpdate { get; set; }

        /// <summary>
        /// Gets all routes.
        /// </summary>
        [NotNull]
        public IEnumerable<Route> AllRoutes => _allRoutes;

        /// <summary>
        /// Gets the available destinations.
        /// </summary>
        [NotNull]
        public IEnumerable<string> AvailableDestinations
        {
            get
            {
                var res = new HashSet<string>();
                if (_currentRoom == null)
                {
                    return res;
                }

                var routesContainigCurrentRoom = _allRoutes.Where(r => r.RoomIdentifiersSet.Contains(_currentRoom.RoomId));
                foreach (var route in routesContainigCurrentRoom)
                {
                    res.UnionWith(route.RoutePointsAvailableFromStart.Keys);
                    res.UnionWith(route.RoutePointsAvailableFromEnd.Keys);
                }

                return res;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this route manager can create new route.
        /// </summary>
        public bool CanCreateNewRoute => _currentRoom != null && string.IsNullOrEmpty(_currentRouteTarget) && _currentlyRecordedRoute == null;

        /// <summary>
        /// Gets a value indicating whether this route manager can cancel current route recording.
        /// </summary>
        public bool CanCancelCurrentRouteRecording => _currentlyRecordedRoute != null;

        /// <summary>
        /// Gets a value indicating whether this route manager stop current route recording.
        /// </summary>
        public bool CanStopCurrentRouteRecording => _currentlyRecordedRoute != null;

        /// <summary>
        /// Gets a value indicating whether this route manager can start route.
        /// </summary>
        public bool CanStartRoute => string.IsNullOrEmpty(_currentRouteTarget) && _currentlyRecordedRoute == null && AvailableDestinations.Any();

        /// <summary>
        /// Gets a value indicating whether this route manager can stop current route.
        /// </summary>
        public bool CanStopCurrentRoute => !string.IsNullOrEmpty(_currentRouteTarget);

        /// <summary>
        /// Loads the routes.
        /// </summary>
        public void LoadRoutes()
        {
            var routesFileName = Path.Combine(GetMapsFolder(), "Routes.xml");

            if (File.Exists(routesFileName))
            {
                using (var inStream = File.OpenRead(routesFileName))
                {
                    _allRoutes = (List<Route>)_routesSerializer.Deserialize(inStream);
                }
            }

            RebuildRouteIndexes();
        }

        /// <summary>
        /// Updates the current room.
        /// </summary>
        /// <param name="newCurrentRoom">The new current room.</param>
        /// <param name="newCurrentZone">The new current zone.</param>
        public void UpdateCurrentRoom([CanBeNull] RoomViewModel newCurrentRoom, [NotNull] ZoneViewModel newCurrentZone)
        {
            Assert.ArgumentNotNull(newCurrentZone, "newCurrentZone");

            if (SkipNextUpdate)
            {
                SkipNextUpdate = false;
                return;
            }

            if (_rootModel == null)
            {
                return;
            }

            if (_isUpdateInProgress || _pendingUpdates.Count > 0)
            {
                _pendingUpdates.Enqueue(new Tuple<RoomViewModel, ZoneViewModel>(newCurrentRoom, newCurrentZone));
                return;
            }

            _isUpdateInProgress = true;

            // Маячок пути шага: маршрут получил комнату и думает над следующим шагом
            var routeSw = System.Diagnostics.Stopwatch.StartNew();

            UpdateCurrentRoomInternal(newCurrentZone, newCurrentRoom);

            while (_pendingUpdates.Count > 0)
            {
                var update = _pendingUpdates.Dequeue();
                UpdateCurrentRoomInternal(update.Item2, update.Item1);
            }

            _isUpdateInProgress = false;

            routeSw.Stop();
            Common.Conveyor.PerfLog.WriteTotal("ROUTE_THINK", routeSw.ElapsedMilliseconds,
                string.Format("room={0}", newCurrentRoom != null ? newCurrentRoom.RoomId : -1));
        }

        public void UpdateCurrentRoomWithNoRoute([NotNull] ZoneViewModel newCurrentZone, [CanBeNull] RoomViewModel newCurrentRoom)
        {
            Assert.ArgumentNotNull(newCurrentZone, "newCurrentZone");

            var prevZone = _currentZone;
            _currentRoom = newCurrentRoom;
            _currentZone = newCurrentZone;
            if (prevZone == null || prevZone.Id != _currentZone.Id)
            {
                UpdateCurrentZoneRooms();
            }
        }

        /// <summary>
        /// Starts the new route recording.
        /// </summary>
        /// <returns><c>true</c> if route recording was started; otherwise - <c>false</c>.</returns>
        public bool StartNewRouteRecording()
        {
            string startName = string.Empty;
            if (_rootModel == null)
            {
                return false;
            }

            if (_currentRoom == null)
            {
                return false;
            }

            var existingRoute = _allRoutes.FirstOrDefault(r => r.StartRoomId == _currentRoom.RoomId);
            if (existingRoute != null)
            {
                startName = existingRoute.StartName;
            }
            else
            {
                existingRoute = _allRoutes.FirstOrDefault(r => r.EndRoomId == _currentRoom.RoomId);
                if (existingRoute != null)
                {
                    startName = existingRoute.EndName;
                }
            }

            if (string.IsNullOrEmpty(startName))
            {
                SelectedRouteDestination = string.Empty;
                var roomStartDialog = new RoutePointNameEnterDialog { Owner = _mainWindow, DataContext = this };
                var result = roomStartDialog.ShowDialog();
                if (!(result.HasValue && result.Value))
                {
                    return false;
                }

                startName = SelectedRouteDestination;
            }

            return StartNewRouteRecording(startName);
        }

        /// <summary>
        /// Starts the new route recording.
        /// </summary>
        /// <param name="startName">The start name.</param>
        /// <returns><c>true</c> if route recording was started; otherwise - <c>false</c>.</returns>
        public bool StartNewRouteRecording([NotNull]string startName)
        {
            Assert.ArgumentNotNull(startName, "startName");

            if (_rootModel == null)
            {
                return false;
            }

            if (_currentRoom == null)
            {
                return false;
            }

            var existingRoute = _allRoutes.FirstOrDefault(r => r.StartRoomId == _currentRoom.RoomId);
            if (existingRoute != null)
            {
                startName = existingRoute.StartName;
            }

            existingRoute = _allRoutes.FirstOrDefault(r => r.EndRoomId == _currentRoom.RoomId);
            if (existingRoute != null)
            {
                startName = existingRoute.EndName;
            }

            if (_allRoutes.Any(r => r.EndRoomId != _currentRoom.RoomId && string.Equals(r.EndName, startName, StringComparison.CurrentCultureIgnoreCase))
                || _allRoutes.Any(r => r.StartRoomId != _currentRoom.RoomId && string.Equals(r.StartName, startName, StringComparison.CurrentCultureIgnoreCase)))
            {
                _rootModel.PushMessageToConveyor(new ErrorMessage(string.Format(CultureInfo.InvariantCulture, Resources.RoutePointNameNotUnique, startName)));
                return false;
            }

            if (string.IsNullOrEmpty(startName))
            {
                _rootModel.PushMessageToConveyor(new ErrorMessage(Resources.RouteStartNameCanNotBeEmpty));
                return false;
            }

            var route = new Route { StartName = startName };
            route.RouteRoomIdentifiers.Add(_currentRoom.RoomId);
            _rootModel.PushMessageToConveyor(new InfoMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteRecordingStarted, startName), TextColor.BrightYellow));
            _currentlyRecordedRoute = route;
            _currentRoom.IsStartOrEndOfRoute = true;
            _currentRoom.IsPartOfRecordedRoute = true;
            return true;
        }

        /// <summary>
        /// Stops the route recording.
        /// </summary>
        /// <returns><c>true</c> if route recording was stopped; otherwise - <c>false</c>.</returns>
        public bool StopRouteRecording()
        {
            string endName = string.Empty;
            if (_rootModel == null)
            {
                return false;
            }

            if (_currentRoom == null)
            {
                return false;
            }

            var existingRoute = _allRoutes.FirstOrDefault(r => r.StartRoomId == _currentRoom.RoomId);
            if (existingRoute != null)
            {
                endName = existingRoute.StartName;
            }
            else
            {
                existingRoute = _allRoutes.FirstOrDefault(r => r.EndRoomId == _currentRoom.RoomId);
                if (existingRoute != null)
                {
                    endName = existingRoute.EndName;
                }
            }

            if (string.IsNullOrEmpty(endName))
            {
                SelectedRouteDestination = string.Empty;
                var routeEnddialog = new RoutePointNameEnterDialog { Owner = _mainWindow, DataContext = this };
                var result = routeEnddialog.ShowDialog();
                if (!(result.HasValue && result.Value))
                {
                    return false;
                }

                endName = SelectedRouteDestination;
            }

            return StopRouteRecording(endName);
        }

        /// <summary>
        /// Stops route recording.
        /// </summary>
        /// <param name="endName">The end name.</param>
        /// <returns>
        ///   <c>true</c> if route recording was stopped; otherwise - <c>false</c>.
        /// </returns>
        public bool StopRouteRecording([NotNull]string endName)
        {
            Assert.ArgumentNotNull(endName, "endName");

            if (_rootModel == null)
            {
                return false;
            }

            if (_currentlyRecordedRoute == null)
            {
                return false;
            }

            if (_currentRoom == null)
            {
                return false;
            }

            var existingRoute = _allRoutes.FirstOrDefault(r => r.StartRoomId == _currentRoom.RoomId);
            if (existingRoute != null)
            {
                endName = existingRoute.StartName;
            }

            existingRoute = _allRoutes.FirstOrDefault(r => r.EndRoomId == _currentRoom.RoomId);
            if (existingRoute != null)
            {
                endName = existingRoute.EndName;
            }

            if (_allRoutes.Any(r => r.EndRoomId != _currentRoom.RoomId && string.Equals(r.EndName, endName, StringComparison.CurrentCultureIgnoreCase))
                || _allRoutes.Any(r => r.StartRoomId != _currentRoom.RoomId && string.Equals(r.StartName, endName, StringComparison.CurrentCultureIgnoreCase)))
            {
                _rootModel.PushMessageToConveyor(new ErrorMessage(string.Format(CultureInfo.InvariantCulture, Resources.RoutePointNameNotUnique, endName)));
                return false;
            }

            if (string.IsNullOrEmpty(endName))
            {
                _rootModel.PushMessageToConveyor(new ErrorMessage(Resources.RouteStartNameCanNotBeEmpty));
                return false;
            }

            _currentlyRecordedRoute.EndName = endName;
            _rootModel.PushMessageToConveyor(new InfoMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteRecordingStopped, _currentlyRecordedRoute.StartName, _currentlyRecordedRoute.EndName), TextColor.BrightYellow));
            _currentRoom.IsStartOrEndOfRoute = true;
            _currentRoom.IsPartOfRecordedRoute = true;
            _allRoutes.Add(_currentlyRecordedRoute);
            _currentlyRecordedRoute = null;
            RebuildRouteIndexes();
            UpdateCurrentZoneRooms();
            SaveRoutes();
            return true;
        }

        /// <summary>
        /// Cancels route recording.
        /// </summary>
        public void CancelRouteRecording()
        {
            if (_rootModel == null)
            {
                return;
            }

            if (_currentlyRecordedRoute == null)
            {
                return;
            }

            _currentlyRecordedRoute = null;
            _rootModel.PushMessageToConveyor(new InfoMessage(Resources.RouteRecordingCanceled, TextColor.BrightYellow));
            UpdateCurrentZoneRooms();
        }

        /// <summary>
        /// Deletes the route.
        /// </summary>
        /// <returns><c>true</c> if route was deleted; otherwise - <c>false</c></returns>
        public bool DeleteRoute()
        {
            if (_rootModel == null)
            {
                return false;
            }

            var deleteDialog = new RouteDeleteDialog { Owner = _mainWindow, DataContext = this };
            var res = deleteDialog.ShowDialog();
            if (res.HasValue && res.Value && SelectedRoute != null)
            {
                _allRoutes.Remove(SelectedRoute);
                RebuildRouteIndexes();
                UpdateCurrentZoneRooms();
                SaveRoutes();
                _rootModel.PushMessageToConveyor(new InfoMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteDeletedMessage, SelectedRoute.StartName, SelectedRoute.EndName), TextColor.BrightYellow));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Navigates to specified room.
        /// </summary>
        /// <param name="roomAliasOrName">Name of the room alias or.</param>
        public void NavigateToRoom([NotNull] string roomAliasOrName)
        {
            Assert.ArgumentNotNullOrWhiteSpace(roomAliasOrName, "roomAliasOrName");

            if (_rootModel == null)
            {
                return;
            }

            if (_currentRoom == null || _currentZone == null)
            {
                return;
            }

            var room = _currentZone.AllRooms.FirstOrDefault(r => string.Equals(r.Alias, roomAliasOrName, StringComparison.CurrentCultureIgnoreCase))
                       ??
                       _currentZone.AllRooms.FirstOrDefault(r => string.Equals(r.Name, roomAliasOrName, StringComparison.CurrentCultureIgnoreCase));

            if (room == null)
            {
                _rootModel.PushMessageToConveyor(new ErrorMessage(Resources.RouteSpecifiedRoomDoesNotExists));
                return;
            }

            NavigateToRoom(room);
        }

        /// <summary>
        /// Navigates to specified room.
        /// </summary>
        public void NavigateToRoom([NotNull] RoomViewModel roomToNavigateTo, IEnumerable<RoomColor> roomColorsToSkip = null, bool firstStepOnly = false)
        {
            Assert.ArgumentNotNull(roomToNavigateTo, "roomToNavigateTo");

            if (_rootModel == null)
            {
                return;
            }

            if (_currentRoom == null || _currentZone == null)
            {
                return;
            }

            var currentRoom = _currentRoom;
            foreach (var room in FindRouteToRoom(currentRoom, roomToNavigateTo, roomColorsToSkip))
            {
                var closureRoom = room;
                var exit = currentRoom.Exits.FirstOrDefault(ex => ex.Room == closureRoom);
                if (exit == null)
                {
                    _rootModel.PushMessageToConveyor(new ErrorMessage(Resources.RoomNavigationError));
                    return;
                }

                GotoDirection(exit.Direction);

                currentRoom = room;

                if (firstStepOnly)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Shows the routes dialog.
        /// </summary>
        public void ShowRoutesDialog()
        {
            var dialog = new RoutesDialog { Owner = _mainWindow, DataContext = this };
            dialog.ShowDialog();
        }

        /// <summary>
        /// Starts routing to some destination.
        /// </summary>
        /// <returns><c>true</c> if routing was started; otherwise - <c>false</c>.</returns>
        public bool GotoDestination()
        {
            if (_rootModel == null)
            {
                return false;
            }

            if (_currentRoom == null)
            {
                return false;
            }

            if (!_allRoutes.Any(r => r.RoomIdentifiersSet.Contains(_currentRoom.RoomId)))
            {
                _rootModel.PushMessageToConveyor(new ErrorMessage(Resources.RoutesAreNotAvailable));
                return false;
            }

            var dialog = new RouteDestinationSelectDialog { Owner = _mainWindow, DataContext = this };
            var res = dialog.ShowDialog();
            if (res.HasValue && res.Value && !string.IsNullOrEmpty(SelectedRouteDestination))
            {
                return GotoDestination(SelectedRouteDestination);
            }

            return false;
        }

        /// <summary>
        /// Starts routing to some destination.
        /// </summary>
        /// <param name="selectedRouteDestination">The selected route destination.</param>
        /// <param name="useLookahead">Whether to apply the user-configured lookahead pipeline.
        /// Automated callers (herb gatherer) should pass false — they react per-room and don't
        /// tolerate moves being sent ahead of confirmation.</param>
        /// <returns>
        ///   <c>true</c> if routing was started; otherwise - <c>false</c>.
        /// </returns>
        public bool GotoDestination([NotNull] string selectedRouteDestination, bool useLookahead = true)
        {
            Assert.ArgumentNotNull(selectedRouteDestination, "selectedRouteDestination");

            if (_rootModel == null)
            {
                return false;
            }

            if (_currentRoom == null)
            {
                return false;
            }

            var routesContainingCurrentRoom = _allRoutes.Where(r => r.RoomIdentifiersSet.Contains(_currentRoom.RoomId)).ToList();
            if (!routesContainingCurrentRoom.Any())
            {
                _rootModel.PushMessageToConveyor(new ErrorMessage(Resources.RoutesAreNotAvailable));
                return false;
            }

            // Exact match
            bool exactMatch = routesContainingCurrentRoom.Any(r => r.RoutePointsAvailableFromStart.ContainsKey(selectedRouteDestination))
                           || routesContainingCurrentRoom.Any(r => r.RoutePointsAvailableFromEnd.ContainsKey(selectedRouteDestination));

            if (!exactMatch)
            {
                // Substring match across all available destinations
                var matches = AvailableDestinations
                    .Where(d => d.IndexOf(selectedRouteDestination, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                if (matches.Count == 1)
                {
                    return GotoDestination(matches[0], useLookahead);
                }

                if (matches.Count > 1)
                {
                    // Open dialog with pre-filled filter so user can pick
                    var dialog = new Dialogs.RouteDestinationSelectDialog
                    {
                        Owner = _mainWindow,
                        DataContext = this,
                        InitialFilter = selectedRouteDestination
                    };
                    var res = dialog.ShowDialog();
                    if (res.HasValue && res.Value && !string.IsNullOrEmpty(SelectedRouteDestination))
                    {
                        return GotoDestination(SelectedRouteDestination, useLookahead);
                    }
                    return false;
                }

                _rootModel.PushMessageToConveyor(new ErrorMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteTargetIsNotAvailable, selectedRouteDestination)));
                return false;
            }

            _currentRouteTarget = selectedRouteDestination;
            _rootModel.PushMessageToConveyor(new InfoMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteStarted, selectedRouteDestination), TextColor.BrightYellow));
            _groupMembersCountOnRouteStart = _rootModel.GroupStatus.Count(g => g.InSameRoom);
            _effectiveLookahead = useLookahead ? GetLookahead() : 0;
            _pipelineDepth = 0;

            UpdateCurrentRoom(_currentRoom, _currentZone);

            return true;
        }

        public bool IsCurrentRoomEndOfDestination([NotNull] string destination)
        {
            Assert.ArgumentNotNull(destination, "selectedRouteDestination");

            if (_currentRoom == null)
            {
                return false;
            }

            var route = _allRoutes.FirstOrDefault(r => r.EndName == destination);
            return route?.EndRoomId == _currentRoom.RoomId;
        }

        /// <summary>
        /// Stops the routing to destination.
        /// </summary>
        public void StopRoutingToDestination()
        {
            if (_rootModel == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(_currentRouteTarget))
            {
                return;
            }

            _rootModel.PushMessageToConveyor(new InfoMessage(Resources.RouteStopped, TextColor.BrightYellow));
            _currentRouteTarget = string.Empty;
            _pipelineDepth = 0;
            _effectiveLookahead = 0;
            _pipelineRoute = null;
            _roomTwoStepsAgo = 0;
        }

        public void EnableRouteLog()
        {
            if (_routeLogEnabled)
            {
                var cur = _routeLogPath ?? "?";
                _rootModel?.PushMessageToConveyor(new InfoMessage(
                    string.Format(CultureInfo.InvariantCulture, "Лог маршрута уже включён: {0}", cur),
                    TextColor.BrightYellow));
                return;
            }
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _routeLogPath = Path.Combine(docs, string.Format(CultureInfo.InvariantCulture,
                "route-debug-{0:yyyyMMdd-HHmmss}.log", DateTime.Now));
            _routeLogWriter = new StreamWriter(_routeLogPath, append: false, encoding: Encoding.UTF8) { AutoFlush = true };
            _routeLogEnabled = true;
            _routeLogWriter.WriteLine("=== Route debug log {0} ===", DateTime.Now);
            _rootModel?.PushMessageToConveyor(new InfoMessage(
                string.Format(CultureInfo.InvariantCulture, "Лог маршрута включён: {0}", _routeLogPath),
                TextColor.BrightYellow));
        }

        public void DisableRouteLog()
        {
            if (!_routeLogEnabled)
            {
                _rootModel?.PushMessageToConveyor(new InfoMessage("Лог маршрута не был включён.", TextColor.BrightYellow));
                return;
            }
            _routeLogWriter?.WriteLine("=== Log closed {0} ===", DateTime.Now);
            _routeLogWriter?.Dispose();
            _routeLogWriter = null;
            _routeLogEnabled = false;
            _rootModel?.PushMessageToConveyor(new InfoMessage(
                string.Format(CultureInfo.InvariantCulture, "Лог маршрута выключен. Файл: {0}", _routeLogPath),
                TextColor.BrightYellow));
        }

        private void RouteLog(string fmt, params object[] args)
        {
            if (!_routeLogEnabled || _routeLogWriter == null) return;
            try
            {
                var msg = args.Length > 0
                    ? string.Format(CultureInfo.InvariantCulture, fmt, args)
                    : fmt;
                _routeLogWriter.WriteLine("[{0:HH:mm:ss.fff}] {1}", DateTime.Now, msg);
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Generates routes for all zones using BFS and reloads them.
        /// </summary>
        public void GenerateRoutes()
        {
            if (_rootModel == null) return;

            var zonesDir = Path.Combine(GetMapsFolder(), "MapGenerator", "MapResults");
            if (!Directory.Exists(zonesDir))
            {
                _rootModel.PushMessageToConveyor(new ErrorMessage($"Папка с зонами не найдена: {zonesDir}"));
                return;
            }

            _rootModel.PushMessageToConveyor(new InfoMessage("Генерация маршрутов... подождите.", TextColor.BrightYellow));

            RouteGenerationResult result;
            try
            {
                result = RouteGenerator.Generate(zonesDir, _allRoutes);
            }
            catch (Exception ex)
            {
                _rootModel.PushMessageToConveyor(new ErrorMessage($"Ошибка генерации маршрутов: {ex.Message}"));
                return;
            }

            _allRoutes = result.AllRoutes;
            RebuildRouteIndexes();
            UpdateCurrentZoneRooms();
            SaveRoutes();

            _rootModel.PushMessageToConveyor(new InfoMessage(
                $"Маршруты сгенерированы: было {result.OriginalRouteCount}, добавлено {result.NewRouteCount}, итого {result.AllRoutes.Count}.",
                TextColor.BrightYellow));
            _rootModel.PushMessageToConveyor(new InfoMessage(
                $"Зон всего: {result.ZonesTotal}. Уже было покрыто: {result.ZonesCoveredBefore}. Новых зон охвачено: {result.ZonesNewlyCovered}. Без маршрутов: {result.ZonesUncovered}.",
                TextColor.BrightYellow));
        }

        /// <summary>
        /// Prints the help.
        /// </summary>
        public int LookaheadSize => GetLookahead();

        public void PrintHelp()
        {
            if (_rootModel == null) return;
            _rootModel.PushMessageToConveyor(new InfoMessage(Resources.RouteHelpGoto, TextColor.BrightYellow));
            _rootModel.PushMessageToConveyor(new InfoMessage(Resources.RouteHelpStartRecording, TextColor.BrightYellow));
            _rootModel.PushMessageToConveyor(new InfoMessage(Resources.RouteHelpStopRecording, TextColor.BrightYellow));
            _rootModel.PushMessageToConveyor(new InfoMessage(Resources.RouteHelpCancelRecording, TextColor.BrightYellow));
            _rootModel.PushMessageToConveyor(new InfoMessage(Resources.RouteHelpRoute, TextColor.BrightYellow));
            _rootModel.PushMessageToConveyor(new InfoMessage(Resources.RouteHelpStopRoute, TextColor.BrightYellow));
            _rootModel.PushMessageToConveyor(new InfoMessage(Resources.RouteHelpLookahead, TextColor.BrightYellow));
            _rootModel.PushMessageToConveyor(new InfoMessage(Resources.RouteHelpHelp, TextColor.BrightYellow));
        }

        private int GetLookahead()
        {
            if (_rootModel != null && _lookaheadPerUid.TryGetValue(_rootModel.Uid, out int v))
                return v;
            return 0;
        }

        /// <summary>
        /// Sets the lookahead depth for the current tab. 0 = disabled.
        /// </summary>
        public void SetLookahead(int size)
        {
            if (_rootModel == null) return;
            _lookaheadPerUid[_rootModel.Uid] = Math.Max(0, Math.Min(size, 10));
        }

        /// <summary>
        /// Handles the change of main output window.
        /// </summary>
        /// <param name="rootModel">The root model of new output window.</param>
        public void OutputWindowChanged(RootModel rootModel)
        {
            CancelRouteRecording();

            // Move active routing state into background context so the route keeps running.
            if (_rootModel != null)
            {
                if (!string.IsNullOrEmpty(_currentRouteTarget))
                {
                    var bg = new BgTabContext(_rootModel)
                    {
                        RouteTarget = _currentRouteTarget,
                        PipelineDepth = _pipelineDepth,
                        EffectiveLookahead = _effectiveLookahead,
                        GroupMembersCountOnStart = _groupMembersCountOnRouteStart,
                        CurrentRoom = _currentRoom,
                        CurrentZone = _currentZone,
                    };
                    _bgTabContexts[_rootModel.Uid] = bg;
                }
                else
                {
                    _bgTabContexts.Remove(_rootModel.Uid);
                }
            }

            // Clear active state (no announcements — route continues in bg).
            _currentRouteTarget = string.Empty;
            _pipelineDepth = 0;
            _effectiveLookahead = 0;
            _pipelineRoute = null;

            _rootModel = rootModel;

            // Restore routing state for the tab we're switching to.
            if (_rootModel != null && _bgTabContexts.TryGetValue(_rootModel.Uid, out var saved))
            {
                _bgTabContexts.Remove(_rootModel.Uid);
                _currentRouteTarget = saved.RouteTarget;
                _pipelineDepth = 0;  // pipeline state unknown after background run; reset and resync
                _effectiveLookahead = saved.EffectiveLookahead;
                _groupMembersCountOnRouteStart = saved.GroupMembersCountOnStart;
                _currentRoom = saved.CurrentRoom;
                _currentZone = saved.CurrentZone;
                // Kick off next step from last known position.
                if (_currentRoom != null && _currentZone != null)
                    UpdateCurrentRoom(_currentRoom, _currentZone);
            }
        }

        /// <summary>
        /// Called by ZoneHolder for EVERY room change on any tab, regardless of which tab is displayed.
        /// Processes background routing for tabs that are not currently active.
        /// </summary>
        public void ProcessRoomChangeForTab([NotNull] RootModel tabRootModel, [CanBeNull] RoomViewModel room, [NotNull] ZoneViewModel zone)
        {
            Assert.ArgumentNotNull(tabRootModel, "tabRootModel");
            Assert.ArgumentNotNull(zone, "zone");

            // Active tab is handled by the normal UpdateCurrentRoom path via MapControl.
            if (_rootModel != null && tabRootModel.Uid == _rootModel.Uid)
                return;

            if (!_bgTabContexts.TryGetValue(tabRootModel.Uid, out var ctx))
                return;  // No active route for this background tab.

            if (ctx.IsUpdateInProgress || ctx.PendingUpdates.Count > 0)
            {
                ctx.PendingUpdates.Enqueue(Tuple.Create(room, zone));
                return;
            }

            ctx.IsUpdateInProgress = true;
            ProcessBgTabInternal(ctx, zone, room);
            while (ctx.PendingUpdates.Count > 0)
            {
                var u = ctx.PendingUpdates.Dequeue();
                ProcessBgTabInternal(ctx, u.Item2, u.Item1);
            }
            ctx.IsUpdateInProgress = false;
        }

        [NotNull]
        public static string GetMapsFolder()
        {
            return Path.Combine(SettingsHolder.Instance.Folder, "Maps");
        }

        [NotNull]
        public static IEnumerable<RoomViewModel> FindRouteToRoom([NotNull]RoomViewModel currentRoom, [NotNull] RoomViewModel roomToNavigateTo, IEnumerable<RoomColor> roomColorsToSkip = null)
        {
            Assert.ArgumentNotNull(currentRoom, "currentRoom");
            Assert.ArgumentNotNull(roomToNavigateTo, "roomToNavigateTo");

            if (currentRoom == roomToNavigateTo)
            {
                return Enumerable.Empty<RoomViewModel>();
            }

            var visitedRooms = new HashSet<RoomViewModel>();
            var pathQueue = new Queue<RoutePathElement>();
            var currentElement = new RoutePathElement(currentRoom);
            while (true)
            {
                if (currentElement.Room == roomToNavigateTo)
                {
                    break;
                }

                foreach (var exit in currentElement.Room.Exits)
                {
                    if (exit.Room == null)
                    {
                        continue;
                    }

                    if (!visitedRooms.Contains(exit.Room))
                    {
                        bool skipRoom = roomColorsToSkip?.Any(roomColor => exit.Room.Color == roomColor) == true;

                        if (skipRoom)
                        {
                            continue;
                        }

                        pathQueue.Enqueue(new RoutePathElement(exit.Room, currentElement));
                        visitedRooms.Add(exit.Room);
                    }
                }

                if (pathQueue.Count == 0)
                {
                    return Enumerable.Empty<RoomViewModel>();
                }

                currentElement = pathQueue.Dequeue();
            }

            var result = new List<RoomViewModel>();
            while (currentElement.Previous != null)
            {
                result.Add(currentElement.Room);
                currentElement = currentElement.Previous;
            }

            result.Reverse();
            return result;
        }

        // Sends additional route commands to fill the server queue up to _lookaheadSize.
        // Called after the first step is already sent (pipelineDepth >= 1).
        private void FillPipeline(Route route, bool gotoStart, int confirmedIndex)
        {
            while (_pipelineDepth < _effectiveLookahead)
            {
                int fromOffset = _pipelineDepth;
                int fromIndex = gotoStart ? confirmedIndex - fromOffset : confirmedIndex + fromOffset;
                int toIndex = gotoStart ? fromIndex - 1 : fromIndex + 1;

                if (toIndex < 0 || toIndex >= route.RouteRoomIdentifiers.Count)
                    break;

                int fromRoomId = route.RouteRoomIdentifiers[fromIndex];
                int toRoomId = route.RouteRoomIdentifiers[toIndex];

                // Don't pre-fill a command that departs FROM a junction room.
                // When we physically arrive at a junction we may need to switch routes — the
                // direction on the current route might be wrong.  Stopping here means we arrive
                // at the junction with pd=0 and re-evaluate direction cleanly.
                bool fromIsJunction = _allRoutes.Any(r => r.StartRoomId == fromRoomId || r.EndRoomId == fromRoomId);
                if (fromIsJunction)
                    break;

                var fromRoomVm = _currentZone?.AllRooms.FirstOrDefault(r => r.RoomId == fromRoomId);
                if (fromRoomVm == null)
                    break;

                var fromExit = fromRoomVm.Room.Exits.FirstOrDefault(ex => ex.RoomId == toRoomId);
                if (fromExit == null)
                    break;

                GotoDirection(fromExit.Direction);
                _pipelineDepth++;

                // Don't overshoot: stop after queuing the step that reaches the destination.
                bool isTarget = _allRoutes.Any(r =>
                    (r.StartRoomId == toRoomId && string.Equals(_currentRouteTarget, r.StartName, StringComparison.CurrentCultureIgnoreCase))
                    || (r.EndRoomId == toRoomId && string.Equals(_currentRouteTarget, r.EndName, StringComparison.CurrentCultureIgnoreCase)));
                if (isTarget)
                    break;
            }
        }

        private void ProcessBgTabInternal(BgTabContext ctx, ZoneViewModel newZone, RoomViewModel newRoom)
        {
            if (string.IsNullOrEmpty(ctx.RouteTarget) || newRoom == null)
            {
                ctx.CurrentRoom = newRoom;
                ctx.CurrentZone = newZone;
                return;
            }

            var routesHere = _allRoutes.Where(r => r.RoomIdentifiersSet.Contains(newRoom.RoomId));
            Route minRoute = null;
            int minLen = int.MaxValue;
            bool gotoStart = false;
            bool targetAchieved = false;

            foreach (var route in routesHere)
            {
                if (route.StartRoomId == newRoom.RoomId && string.Equals(ctx.RouteTarget, route.StartName, StringComparison.CurrentCultureIgnoreCase)) { targetAchieved = true; break; }
                if (route.EndRoomId == newRoom.RoomId && string.Equals(ctx.RouteTarget, route.EndName, StringComparison.CurrentCultureIgnoreCase)) { targetAchieved = true; break; }

                if (route.RoutePointsAvailableFromStart.ContainsKey(ctx.RouteTarget) && route.StartRoomId != newRoom.RoomId
                    && route.RoutePointsAvailableFromStart[ctx.RouteTarget] < minLen)
                { gotoStart = true; minLen = route.RoutePointsAvailableFromStart[ctx.RouteTarget]; minRoute = route; }

                if (route.RoutePointsAvailableFromEnd.ContainsKey(ctx.RouteTarget) && route.EndRoomId != newRoom.RoomId
                    && route.RoutePointsAvailableFromEnd[ctx.RouteTarget] < minLen)
                { gotoStart = false; minLen = route.RoutePointsAvailableFromEnd[ctx.RouteTarget]; minRoute = route; }
            }

            // Same intermediate-room override as the main tab: if _pipelineRoute is set and
            // the current room is a non-junction intermediate room on that route, stay on it.
            if (!targetAchieved && minRoute != null && ctx.PipelineRoute != null
                && minRoute != ctx.PipelineRoute
                && ctx.PipelineRoute.RoomIdentifiersSet.Contains(newRoom.RoomId))
            {
                bool atTransitJunction = ctx.PipelineGotoStart
                    ? ctx.PipelineRoute.StartRoomId == newRoom.RoomId
                    : ctx.PipelineRoute.EndRoomId == newRoom.RoomId;
                if (!atTransitJunction)
                {
                    bool canReach = ctx.PipelineRoute.RoutePointsAvailableFromStart.ContainsKey(ctx.RouteTarget)
                                 || ctx.PipelineRoute.RoutePointsAvailableFromEnd.ContainsKey(ctx.RouteTarget);
                    if (canReach)
                    {
                        minRoute = ctx.PipelineRoute;
                        gotoStart = ctx.PipelineGotoStart;
                    }
                }
            }

            if (targetAchieved)
            {
                ctx.RootModel.PushMessageToConveyor(new InfoMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteTargetAchieved, ctx.RouteTarget), TextColor.BrightYellow));
                ctx.RouteTarget = string.Empty;
                ctx.PipelineDepth = 0;
                ctx.EffectiveLookahead = 0;
                ctx.PipelineRoute = null;
                _bgTabContexts.Remove(ctx.RootModel.Uid);
            }
            else if (minRoute == null)
            {
                ctx.RootModel.PushMessageToConveyor(new ErrorMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteTargetIsNotAvailable, ctx.RouteTarget)));
                ctx.RouteTarget = string.Empty;
                _bgTabContexts.Remove(ctx.RootModel.Uid);
            }
            else if (ctx.RootModel.GroupStatus.Count(gr => gr.InSameRoom) != ctx.GroupMembersCountOnStart
                     || ctx.RootModel.GroupStatus.Any(gr => gr.InSameRoom && gr.MovesPercent < 2))
            {
                ctx.RootModel.PushMessageToConveyor(new ErrorMessage(Resources.RouteGroupMateLostOrTired));
                ctx.RouteTarget = string.Empty;
                _bgTabContexts.Remove(ctx.RootModel.Uid);
            }
            else
            {
                if (ctx.PipelineDepth > 0) ctx.PipelineDepth--;

                var idx = minRoute.RouteRoomIdentifiers.IndexOf(newRoom.RoomId);
                if (ctx.PipelineDepth == 0)
                {
                    int nextId = gotoStart ? minRoute.RouteRoomIdentifiers[idx - 1] : minRoute.RouteRoomIdentifiers[idx + 1];
                    var exit = newRoom.Room.Exits.FirstOrDefault(ex => ex.RoomId == nextId);
                    if (exit == null)
                    {
                        ctx.RootModel.PushMessageToConveyor(new ErrorMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteTargetIsNotAvailable, ctx.RouteTarget)));
                        ctx.RouteTarget = string.Empty;
                        _bgTabContexts.Remove(ctx.RootModel.Uid);
                        ctx.CurrentRoom = newRoom; ctx.CurrentZone = newZone;
                        return;
                    }
                    GotoDirectionVia(ctx.RootModel, exit.Direction);
                    ctx.PipelineRoute = minRoute;
                    ctx.PipelineGotoStart = gotoStart;
                    ctx.PipelineDepth++;
                }

                if (ctx.EffectiveLookahead > 0)
                {
                    while (ctx.PipelineDepth < ctx.EffectiveLookahead)
                    {
                        int off = ctx.PipelineDepth;
                        int fi = gotoStart ? idx - off : idx + off;
                        int ti = gotoStart ? fi - 1 : fi + 1;
                        if (ti < 0 || ti >= minRoute.RouteRoomIdentifiers.Count) break;
                        int bgFromRoomId = minRoute.RouteRoomIdentifiers[fi];
                        int bgToRoomId = minRoute.RouteRoomIdentifiers[ti];
                        bool fromIsJunction = _allRoutes.Any(r => r.StartRoomId == bgFromRoomId || r.EndRoomId == bgFromRoomId);
                        if (fromIsJunction) break;
                        var fvm = ctx.CurrentZone?.AllRooms.FirstOrDefault(r => r.RoomId == bgFromRoomId);
                        if (fvm == null) break;
                        var fex = fvm.Room.Exits.FirstOrDefault(ex => ex.RoomId == bgToRoomId);
                        if (fex == null) break;
                        GotoDirectionVia(ctx.RootModel, fex.Direction);
                        ctx.PipelineDepth++;
                        bool isTarget = _allRoutes.Any(r =>
                            (r.StartRoomId == bgToRoomId && string.Equals(ctx.RouteTarget, r.StartName, StringComparison.CurrentCultureIgnoreCase))
                            || (r.EndRoomId == bgToRoomId && string.Equals(ctx.RouteTarget, r.EndName, StringComparison.CurrentCultureIgnoreCase)));
                        if (isTarget) break;
                    }
                }
            }

            ctx.CurrentRoom = newRoom;
            ctx.CurrentZone = newZone;
        }

        private void GotoDirectionVia(RootModel rootModel, ExitDirection exitDirection)
        {
            Common.Conveyor.PerfLog.WriteTotal("ROUTE_STEP", 0, exitDirection.ToString());
            Common.Conveyor.PerfStats.RoomWaitStarted();
            switch (exitDirection)
            {
                case ExitDirection.North: rootModel.PushCommandToConveyor(new TextCommand("north")); break;
                case ExitDirection.South: rootModel.PushCommandToConveyor(new TextCommand("south")); break;
                case ExitDirection.East:  rootModel.PushCommandToConveyor(new TextCommand("east"));  break;
                case ExitDirection.West:  rootModel.PushCommandToConveyor(new TextCommand("west"));  break;
                case ExitDirection.Up:    rootModel.PushCommandToConveyor(new TextCommand("up"));    break;
                case ExitDirection.Down:  rootModel.PushCommandToConveyor(new TextCommand("down"));  break;
                default: rootModel.PushMessageToConveyor(new ErrorMessage(Resources.RoomNavigationError)); break;
            }
            rootModel.PushCommandToConveyor(FlushOutputQueueCommand.Instance);
        }

        private void GotoDirection(ExitDirection exitDirection)
        {
            // Маячок пути шага: маршрут решил и отправляет направление
            Common.Conveyor.PerfLog.WriteTotal("ROUTE_STEP", 0, exitDirection.ToString());
            // Живой замер ожидания комнаты (светофор "шаг")
            Common.Conveyor.PerfStats.RoomWaitStarted();

            switch (exitDirection)
            {
                case ExitDirection.North:
                    _rootModel.PushCommandToConveyor(new TextCommand("north"));
                    break;
                case ExitDirection.South:
                    _rootModel.PushCommandToConveyor(new TextCommand("south"));
                    break;
                case ExitDirection.East:
                    _rootModel.PushCommandToConveyor(new TextCommand("east"));
                    break;
                case ExitDirection.West:
                    _rootModel.PushCommandToConveyor(new TextCommand("west"));
                    break;
                case ExitDirection.Up:
                    _rootModel.PushCommandToConveyor(new TextCommand("up"));
                    break;
                case ExitDirection.Down:
                    _rootModel.PushCommandToConveyor(new TextCommand("down"));
                    break;
                default:
                    _rootModel.PushMessageToConveyor(new ErrorMessage(Resources.RoomNavigationError));
                    break;
            }

            _rootModel.PushCommandToConveyor(FlushOutputQueueCommand.Instance);
        }

        // pointName -> routes that start or end there; rebuilt in RebuildRouteIndexes
        private Dictionary<string, List<Route>> _routesByPoint = new Dictionary<string, List<Route>>(StringComparer.OrdinalIgnoreCase);

        private void RebuildRouteIndexes()
        {
            _routeEndRoomIdentifiers.Clear();
            _routeRoomIdentifiers.Clear();

            // Build adjacency lookup so TraverseRoute is O(degree) not O(N)
            _routesByPoint.Clear();
            foreach (var route in _allRoutes)
            {
                if (!_routesByPoint.TryGetValue(route.StartName, out var startList))
                    _routesByPoint[route.StartName] = startList = new List<Route>();
                startList.Add(route);

                if (!_routesByPoint.TryGetValue(route.EndName, out var endList))
                    _routesByPoint[route.EndName] = endList = new List<Route>();
                endList.Add(route);

                _routeEndRoomIdentifiers.Add(route.EndRoomId);
                _routeEndRoomIdentifiers.Add(route.StartRoomId);
                _routeRoomIdentifiers.UnionWith(route.RouteRoomIdentifiers);

                route.RoutePointsAvailableFromStart.Clear();
                route.RoutePointsAvailableFromEnd.Clear();
                route.RoomIdentifiersSet.Clear();
                route.RoomIdentifiersSet.UnionWith(route.RouteRoomIdentifiers);
            }

            foreach (var route in _allRoutes)
            {
                TraverseRouteWeighted(route.StartName, route.RoutePointsAvailableFromStart);
                TraverseRouteWeighted(route.EndName, route.RoutePointsAvailableFromEnd);
            }
        }

        // Dijkstra over the waypoint graph; edge weight = number of rooms in the route.
        // Replaces the old hop-count BFS — мм+ no longer looks "cheap" just because
        // it has many connections; a 200-room detour now costs 200, not 1.
        private void TraverseRouteWeighted(string startPoint, IDictionary<string, int> distanceMap)
        {
            distanceMap.Clear();
            distanceMap[startPoint] = 0;

            // Priority queue: SortedDictionary<dist, Queue<pointName>>
            var pending = new SortedDictionary<int, Queue<string>>();
            var startQ = new Queue<string>();
            startQ.Enqueue(startPoint);
            pending[0] = startQ;

            while (pending.Count > 0)
            {
                var first = pending.First();
                int curDist = first.Key;
                string curPoint = first.Value.Dequeue();
                if (first.Value.Count == 0)
                    pending.Remove(curDist);

                // Stale entry — a shorter path was already found.
                if (distanceMap.TryGetValue(curPoint, out int recorded) && recorded < curDist)
                    continue;

                if (!_routesByPoint.TryGetValue(curPoint, out var routes))
                    continue;

                foreach (var nextRoute in routes)
                {
                    string neighbor;
                    if (nextRoute.StartName.Equals(curPoint, StringComparison.OrdinalIgnoreCase))
                        neighbor = nextRoute.EndName;
                    else if (nextRoute.EndName.Equals(curPoint, StringComparison.OrdinalIgnoreCase))
                        neighbor = nextRoute.StartName;
                    else
                        continue;

                    int newDist = curDist + nextRoute.RouteRoomIdentifiers.Count;
                    if (!distanceMap.TryGetValue(neighbor, out int existing) || newDist < existing)
                    {
                        distanceMap[neighbor] = newDist;
                        if (!pending.TryGetValue(newDist, out var q))
                            pending[newDist] = q = new Queue<string>();
                        q.Enqueue(neighbor);
                    }
                }
            }
        }

        private void UpdateCurrentZoneRooms()
        {
            if (_currentZone == null)
            {
                return;
            }

            foreach (var room in _currentZone.AllRooms)
            {
                room.IsStartOrEndOfRoute = _routeEndRoomIdentifiers.Contains(room.RoomId);
                room.IsPartOfRoute = _routeRoomIdentifiers.Contains(room.RoomId);
                room.IsPartOfRecordedRoute = _currentlyRecordedRoute != null && _currentlyRecordedRoute.RouteRoomIdentifiers.Contains(room.RoomId);
            }
        }

        private void SaveRoutes()
        {
            if (!Directory.Exists(GetMapsFolder()))
            {
                Directory.CreateDirectory(GetMapsFolder());
            }

            var routesFileName = Path.Combine(GetMapsFolder(), "Routes.xml");
            using (var outStream = File.Open(routesFileName, FileMode.Create, FileAccess.Write))
            using (var streamWriter = new XmlTextWriter(outStream, Encoding.UTF8))
            {
                streamWriter.Formatting = Formatting.Indented;
                _routesSerializer.Serialize(streamWriter, _allRoutes);
            }
        }

        private void UpdateCurrentRoomInternal([NotNull] ZoneViewModel newCurrentZone, [CanBeNull] RoomViewModel newCurrentRoom)
        {
            Assert.ArgumentNotNull(newCurrentZone, "newCurrentZone");

            if (_currentlyRecordedRoute != null && newCurrentRoom != null)
            {
                var lastRoom = _currentlyRecordedRoute.RouteRoomIdentifiers.LastOrDefault();
                if (lastRoom != newCurrentRoom.RoomId)
                {
                    _currentlyRecordedRoute.RouteRoomIdentifiers.Add(newCurrentRoom.RoomId);
                    if (_routeEndRoomIdentifiers.Contains(newCurrentRoom.RoomId) && newCurrentRoom.RoomId != _currentlyRecordedRoute.StartRoomId)
                    {
                        var existingRoute = _allRoutes.FirstOrDefault(r => r.StartRoomId == _currentlyRecordedRoute.StartRoomId && r.EndRoomId == newCurrentRoom.RoomId);
                        existingRoute = existingRoute ?? _allRoutes.FirstOrDefault(r => r.EndRoomId == _currentlyRecordedRoute.StartRoomId && r.StartRoomId == newCurrentRoom.RoomId);
                        _currentRoom = newCurrentRoom;
                        _currentZone = newCurrentZone;

                        if (existingRoute != null)
                        {
                            var yesNoDialog = new YesNoDialog
                            {
                                Owner = _mainWindow,
                                Title = Resources.RouteAlreadyExistsTitle,
                                TextToDisplay = string.Format(CultureInfo.InvariantCulture, Resources.RouteAlreadyExistsQuestion, existingRoute.StartName, existingRoute.EndName)
                            };

                            var res = yesNoDialog.ShowDialog();
                            if (res.HasValue && res.Value)
                            {
                                StopRouteRecording();
                                StartNewRouteRecording();
                                _allRoutes.Remove(existingRoute);
                                RebuildRouteIndexes();
                            }
                            else
                            {
                                _currentlyRecordedRoute = null;
                                StartNewRouteRecording();
                            }
                        }
                        else
                        {
                            StopRouteRecording();
                            StartNewRouteRecording();
                        }

                        UpdateCurrentZoneRooms();
                    }

                    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() => newCurrentRoom.IsPartOfRecordedRoute = true));

                }
            }

            if (!string.IsNullOrEmpty(_currentRouteTarget) && newCurrentRoom != null)
            {
                RouteLog("ROOM {0} | prev={1} | prev2={2} | target={3} | pd={4} | pipeRoute={5}",
                    newCurrentRoom.RoomId,
                    _currentRoom?.RoomId.ToString(CultureInfo.InvariantCulture) ?? "-",
                    _roomTwoStepsAgo,
                    _currentRouteTarget,
                    _pipelineDepth,
                    _pipelineRoute != null ? (_pipelineRoute.StartName + "→" + _pipelineRoute.EndName) : "-");

                var routesContainigCurrentRoom = _allRoutes.Where(r => r.RoomIdentifiersSet.Contains(newCurrentRoom.RoomId));
                Route minRoute = null;
                int minDestinationLength = int.MaxValue;
                bool gotoStart = false;
                bool targetAchieved = false;

                foreach (var route in routesContainigCurrentRoom)
                {
                    if (route.StartRoomId == newCurrentRoom.RoomId && string.Equals(_currentRouteTarget, route.StartName, StringComparison.CurrentCultureIgnoreCase))
                    {
                        targetAchieved = true;
                        break;
                    }

                    if (route.EndRoomId == newCurrentRoom.RoomId && string.Equals(_currentRouteTarget, route.EndName, StringComparison.CurrentCultureIgnoreCase))
                    {
                        targetAchieved = true;
                        break;
                    }

                    if (route.RoutePointsAvailableFromStart.ContainsKey(_currentRouteTarget))
                    {
                        if (route.StartRoomId != newCurrentRoom.RoomId)
                        {
                            int dist = route.RoutePointsAvailableFromStart[_currentRouteTarget];
                            // Penalize backward traversal only when we JUST arrived via this route
                            // (pipeline route). That prevents bouncing back along the same route,
                            // but allows a different route that happens to end here to be selected
                            // normally (e.g. пригорка→мт+ at мт+ after arriving via мм+→мт+).
                            if (route.EndRoomId == newCurrentRoom.RoomId && route == _pipelineRoute)
                                dist += route.RouteRoomIdentifiers.Count;
                            if (dist < minDestinationLength)
                            {
                                gotoStart = true;
                                minDestinationLength = dist;
                                minRoute = route;
                            }
                        }
                    }

                    if (route.RoutePointsAvailableFromEnd.ContainsKey(_currentRouteTarget))
                    {
                        if (route.EndRoomId != newCurrentRoom.RoomId)
                        {
                            int dist = route.RoutePointsAvailableFromEnd[_currentRouteTarget];
                            if (dist < minDestinationLength)
                            {
                                gotoStart = false;
                                minDestinationLength = dist;
                                minRoute = route;
                            }
                        }
                    }
                }

                RouteLog("  candidates: {0}", string.Join(", ", _allRoutes
                    .Where(r => r.RoomIdentifiersSet.Contains(newCurrentRoom.RoomId))
                    .Select(r => string.Format(CultureInfo.InvariantCulture,
                        "{0}→{1}(dist={2})",
                        r.StartName, r.EndName,
                        r.RoutePointsAvailableFromStart.ContainsKey(_currentRouteTarget)
                            ? r.RoutePointsAvailableFromStart[_currentRouteTarget].ToString(CultureInfo.InvariantCulture)
                            : r.RoutePointsAvailableFromEnd.ContainsKey(_currentRouteTarget)
                                ? r.RoutePointsAvailableFromEnd[_currentRouteTarget].ToString(CultureInfo.InvariantCulture)
                                : "n/a"))));
                RouteLog("  selected: {0} gotoStart={1} dist={2} targetAchieved={3}",
                    minRoute != null ? (minRoute.StartName + "→" + minRoute.EndName) : "NULL",
                    gotoStart, minDestinationLength, targetAchieved);

                // If minRoute switched away from _pipelineRoute but the current room is still
                // an intermediate room on _pipelineRoute (not yet at its junction endpoint),
                // stay on _pipelineRoute. Without this, a room shared by two routes (e.g. 15048
                // appears in both мт+→эдорас and эдорас→изен) causes a premature route switch
                // that skips the required junction (эдорас) when pipelineDepth reaches 0.
                if (!targetAchieved && minRoute != null && _pipelineRoute != null
                    && minRoute != _pipelineRoute
                    && _pipelineRoute.RoomIdentifiersSet.Contains(newCurrentRoom.RoomId))
                {
                    bool atTransitJunction = _pipelineGotoStart
                        ? _pipelineRoute.StartRoomId == newCurrentRoom.RoomId
                        : _pipelineRoute.EndRoomId == newCurrentRoom.RoomId;
                    if (!atTransitJunction)
                    {
                        bool canReach = _pipelineRoute.RoutePointsAvailableFromStart.ContainsKey(_currentRouteTarget)
                                     || _pipelineRoute.RoutePointsAvailableFromEnd.ContainsKey(_currentRouteTarget);
                        if (canReach)
                        {
                            RouteLog("  PIPELINE PROTECT: staying on {0}→{1} (not at junction)", _pipelineRoute.StartName, _pipelineRoute.EndName);
                            minRoute = _pipelineRoute;
                            gotoStart = _pipelineGotoStart;
                        }
                    }
                }

                if (targetAchieved)
                {
                    _rootModel.PushMessageToConveyor(new InfoMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteTargetAchieved, _currentRouteTarget), TextColor.BrightYellow));
                    StopRoutingToDestination();
                }
                else if (minRoute == null)
                {
                    _rootModel.PushMessageToConveyor(new ErrorMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteTargetIsNotAvailable, _currentRouteTarget)));
                    StopRoutingToDestination();
                }
                else if (_rootModel.GroupStatus.Count(gr => gr.InSameRoom) != _groupMembersCountOnRouteStart || _rootModel.GroupStatus.Any(gr => gr.InSameRoom && gr.MovesPercent < 2))
                {
                    _rootModel.PushMessageToConveyor(new ErrorMessage(Resources.RouteGroupMateLostOrTired));
                    StopRoutingToDestination();
                }
                else
                {
                    // One room confirmed: one in-flight command consumed
                    if (_pipelineDepth > 0)
                        _pipelineDepth--;

                    var currentRoomIndex = minRoute.RouteRoomIdentifiers.IndexOf(newCurrentRoom.RoomId);

                    // Only send next command if pipeline is empty.
                    // When lookahead is active the next command is already in-flight from a previous FillPipeline call.
                    if (_pipelineDepth == 0)
                    {
                        int nextRoomId = gotoStart
                                             ? minRoute.RouteRoomIdentifiers[currentRoomIndex - 1]
                                             : minRoute.RouteRoomIdentifiers[currentRoomIndex + 1];

                        RouteLog("  nextRoom={0} idx={1}/{2}", nextRoomId, currentRoomIndex, minRoute.RouteRoomIdentifiers.Count - 1);

                        // Oscillation detection: confirmed A→B→A→B loop when:
                        //   - route says go back to where we just came from (_currentRoom)
                        //   - AND we were at this very room (_roomTwoStepsAgo) two steps ago
                        // Dead-end zones are safe: after W→herb→W the next step is outward (not herb), so condition is false.
                        if (_currentRoom != null
                            && nextRoomId == _currentRoom.RoomId
                            && _roomTwoStepsAgo != 0
                            && newCurrentRoom.RoomId == _roomTwoStepsAgo)
                        {
                            RouteLog("  OSCILLATION DETECTED: {0}→{1}→{0} loop, next={1}", newCurrentRoom.RoomId, _currentRoom.RoomId);

                            // Try any exit that is NOT back to the previous room and leads toward target via some route.
                            var fwdExit = newCurrentRoom.Room.Exits
                                .Where(ex => ex.RoomId != 0 && ex.RoomId != _currentRoom.RoomId)
                                .FirstOrDefault(ex => _allRoutes.Any(r =>
                                    r.RoomIdentifiersSet.Contains(ex.RoomId) &&
                                    (r.RoutePointsAvailableFromStart.ContainsKey(_currentRouteTarget) ||
                                     r.RoutePointsAvailableFromEnd.ContainsKey(_currentRouteTarget))));

                            if (fwdExit != null)
                            {
                                RouteLog("  OSCILLATION BREAK: going to {0} via {1}", fwdExit.RoomId, fwdExit.Direction);
                                _rootModel.PushMessageToConveyor(new InfoMessage(
                                    string.Format(CultureInfo.InvariantCulture,
                                        "Обход петли маршрута в комнате {0} (A→B→A→B).", newCurrentRoom.RoomId),
                                    TextColor.BrightYellow));
                                GotoDirection(fwdExit.Direction);
                                _pipelineRoute = null;
                                _pipelineDepth++;
                                _roomTwoStepsAgo = 0; // reset to avoid re-triggering immediately
                                var prevZoneEarly = _currentZone;
                                _currentRoom = newCurrentRoom;
                                _currentZone = newCurrentZone;
                                if (prevZoneEarly == null || prevZoneEarly.Id != _currentZone.Id)
                                    UpdateCurrentZoneRooms();
                                return;
                            }
                            else
                            {
                                RouteLog("  OSCILLATION: no forward exit found, falling through (dead end?)");
                            }
                        }

                        var exit = newCurrentRoom.Room.Exits.FirstOrDefault(ex => ex.RoomId == nextRoomId);
                        if (exit == null)
                        {
                            RouteLog("  ERROR: no exit to nextRoom={0}", nextRoomId);
                            _rootModel.PushMessageToConveyor(new ErrorMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteTargetIsNotAvailable, _currentRouteTarget)));
                            StopRoutingToDestination();
                            return;
                        }
                        RouteLog("  SEND: {0} → room {1}", exit.Direction, nextRoomId);
                        GotoDirection(exit.Direction);
                        _pipelineRoute = minRoute;
                        _pipelineGotoStart = gotoStart;
                        _pipelineDepth++;
                    }

                    // Only fill ahead when we're still on the same route that started the pipeline.
                    // If minRoute switched while pd > 0, in-flight commands belong to a different route;
                    // adding lookahead for the new route would send directions computed relative to a
                    // future room position that the character won't actually be in when they execute.
                    if (_effectiveLookahead > 0 && minRoute == _pipelineRoute && gotoStart == _pipelineGotoStart)
                        FillPipeline(minRoute, gotoStart, currentRoomIndex);
                }
            }

            var prevZone = _currentZone;
            _roomTwoStepsAgo = _currentRoom?.RoomId ?? 0;
            _currentRoom = newCurrentRoom;
            _currentZone = newCurrentZone;
            if (prevZone == null || prevZone.Id != _currentZone.Id)
            {
                UpdateCurrentZoneRooms();
            }
        }
    }
}
