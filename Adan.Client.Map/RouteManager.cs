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
        // 0 = disabled (classic one-step mode). Persisted in Settings.
        private int _lookaheadSize = Properties.Settings.Default.RouteLookaheadSize;
        private int _pipelineDepth;  // commands currently in the server queue

        // Lookahead applies only to explicit user "маршрут идти" commands.
        // Herb-driven automated travel needs synchronous per-room control (it reacts
        // to each room individually), so it requests useLookahead: false.
        private int _effectiveLookahead;


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
            _effectiveLookahead = useLookahead ? _lookaheadSize : 0;
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
        public int LookaheadSize => _lookaheadSize;

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

        /// <summary>
        /// Sets the lookahead depth. 0 = disabled. Output is handled by the caller (RouteUnit).
        /// </summary>
        public void SetLookahead(int size)
        {
            _lookaheadSize = Math.Max(0, Math.Min(size, 10));
            Properties.Settings.Default.RouteLookaheadSize = _lookaheadSize;
            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Handles the change of main output window.
        /// </summary>
        /// <param name="rootModel">The root model of new output window.</param>
        public void OutputWindowChanged(RootModel rootModel)
        {
            CancelRouteRecording();
            StopRoutingToDestination();
            _rootModel = rootModel;
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

                var fromRoomVm = _currentZone?.AllRooms.FirstOrDefault(r => r.RoomId == fromRoomId);
                if (fromRoomVm == null)
                    break;

                var fromExit = fromRoomVm.Room.Exits.FirstOrDefault(ex => ex.RoomId == toRoomId);
                if (fromExit == null)
                    break;

                GotoDirection(fromExit.Direction);
                _pipelineDepth++;

                // Don't overshoot: stop after queuing the step that reaches the destination
                bool isTarget = _allRoutes.Any(r =>
                    (r.StartRoomId == toRoomId && string.Equals(_currentRouteTarget, r.StartName, StringComparison.CurrentCultureIgnoreCase))
                    || (r.EndRoomId == toRoomId && string.Equals(_currentRouteTarget, r.EndName, StringComparison.CurrentCultureIgnoreCase)));
                if (isTarget)
                    break;
            }
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
                var visitedRoutePoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { route.StartName };
                var visitedRoutes = new HashSet<Route> { route };
                route.RoutePointsAvailableFromStart[route.StartName] = 0;
                TraverseRoute(route.StartName, visitedRoutePoints, visitedRoutes, route.RoutePointsAvailableFromStart, 1);

                visitedRoutePoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { route.EndName };
                visitedRoutes = new HashSet<Route> { route };
                route.RoutePointsAvailableFromEnd[route.EndName] = 0;
                TraverseRoute(route.EndName, visitedRoutePoints, visitedRoutes, route.RoutePointsAvailableFromEnd, 1);
            }
        }

        private void TraverseRoute([NotNull] string routePointName, [NotNull] ISet<string> visitedRoutePoints, [NotNull] ISet<Route> visitedRoutes, [NotNull] IDictionary<string, int> routePointsAvailabilityList, int currentDepth)
        {
            if (!_routesByPoint.TryGetValue(routePointName, out var neighbors))
                return;

            foreach (var nextRoute in neighbors)
            {
                if (visitedRoutes.Contains(nextRoute))
                    continue;

                string neighbor = null;
                if (nextRoute.StartName.Equals(routePointName, StringComparison.OrdinalIgnoreCase)
                    && !visitedRoutePoints.Contains(nextRoute.EndName))
                    neighbor = nextRoute.EndName;
                else if (nextRoute.EndName.Equals(routePointName, StringComparison.OrdinalIgnoreCase)
                    && !visitedRoutePoints.Contains(nextRoute.StartName))
                    neighbor = nextRoute.StartName;

                if (neighbor == null)
                    continue;

                visitedRoutes.Add(nextRoute);
                visitedRoutePoints.Add(neighbor);
                routePointsAvailabilityList[neighbor] = currentDepth;
                TraverseRoute(neighbor, visitedRoutePoints, visitedRoutes, routePointsAvailabilityList, currentDepth + 1);
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
                        if (route.StartRoomId != newCurrentRoom.RoomId
                            && route.RoutePointsAvailableFromStart[_currentRouteTarget] < minDestinationLength)
                        {
                            gotoStart = true;
                            minDestinationLength = route.RoutePointsAvailableFromStart[_currentRouteTarget];
                            minRoute = route;
                        }
                    }

                    if (route.RoutePointsAvailableFromEnd.ContainsKey(_currentRouteTarget))
                    {
                        if (route.EndRoomId != newCurrentRoom.RoomId && route.RoutePointsAvailableFromEnd[_currentRouteTarget] < minDestinationLength)
                        {
                            gotoStart = false;
                            minDestinationLength = route.RoutePointsAvailableFromEnd[_currentRouteTarget];
                            minRoute = route;
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
                        var exit = newCurrentRoom.Room.Exits.FirstOrDefault(ex => ex.RoomId == nextRoomId);
                        if (exit == null)
                        {
                            _rootModel.PushMessageToConveyor(new ErrorMessage(string.Format(CultureInfo.InvariantCulture, Resources.RouteTargetIsNotAvailable, _currentRouteTarget)));
                            StopRoutingToDestination();
                            return;
                        }
                        GotoDirection(exit.Direction);
                        _pipelineDepth++;
                    }

                    if (_effectiveLookahead > 0)
                        FillPipeline(minRoute, gotoStart, currentRoomIndex);
                }
            }

            var prevZone = _currentZone;
            _currentRoom = newCurrentRoom;
            _currentZone = newCurrentZone;
            if (prevZone == null || prevZone.Id != _currentZone.Id)
            {
                UpdateCurrentZoneRooms();
            }
        }
    }
}
