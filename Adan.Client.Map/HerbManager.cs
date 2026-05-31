namespace Adan.Client.Map
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Timers;
    using System.Xml.Linq;
    using Common.Commands;
    using Common.Messages;
    using Common.Model;
    using Common.Settings;
    using Common.Themes;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Model;
    using ViewModel;

    /// <summary>
    /// Manages herb-gathering automation across zones.
    /// </summary>
    public class HerbManager
    {
        private enum GatherState { Idle, GatheringInZone, TravelingToZone, ReturningToZoneEntry, ReturningHome }

        private readonly RouteManager _routeManager;
        private ZoneManager _zoneManager;

        private readonly Random _random = new Random();
        private readonly Timer _herbCollectionTimer;
        private volatile bool _waitingForHerbCollection;

        // Watchdog: if stuck in combat and can't move, retry after timeout
        private readonly Timer _retryMoveTimer;
        private int _lastSeenRoomId;

        public HerbManager([NotNull] RouteManager routeManager)
        {
            Assert.ArgumentNotNull(routeManager, "routeManager");
            _routeManager = routeManager;

            _herbCollectionTimer = new Timer { AutoReset = false };
            _herbCollectionTimer.Elapsed += OnHerbCollectionTimerElapsed;

            _retryMoveTimer = new Timer { AutoReset = true, Interval = 4000 };
            _retryMoveTimer.Elapsed += OnRetryMoveTimerElapsed;

            _autoRepeatTimer = new Timer { AutoReset = false, Interval = AutoRepeatDelayMs };
            _autoRepeatTimer.Elapsed += OnAutoRepeatTimerElapsed;

            _herbDetectionTimer = new Timer { AutoReset = false, Interval = 2500 };
            _herbDetectionTimer.Elapsed += OnHerbDetectionTimerElapsed;
        }

        /// <summary>Called by MapPlugin after ZoneManager is created.</summary>
        public void SetZoneManager([NotNull] ZoneManager zoneManager)
        {
            _zoneManager = zoneManager;
        }

        private RootModel _rootModel;
        private RoomViewModel _currentRoom;
        private ZoneViewModel _currentZone;

        private GatherState _state = GatherState.Idle;
        private HerbDangerLevel _maxDangerLevel;

        // Auto-repeat mode
        private bool _autoRepeat;
        private readonly Timer _autoRepeatTimer;
        private const double AutoRepeatDelayMs = 10 * 60 * 1000; // 10 minutes

        // Home point
        private int _homeRoomId;
        private ZoneViewModel _homeZone;

        // Herb rooms remaining in current zone
        private readonly HashSet<int> _pendingRoomsInZone = new HashSet<int>();

        // Zones still to visit: zoneId -> list of herb room IDs
        private List<KeyValuePair<int, List<int>>> _pendingZones;

        // Target zone we are traveling to
        private int _travelTargetZoneId;
        private string _travelTargetWaypoint;

        // Room we arrived at when entering current zone (used to return before next GotoDestination)
        private int _zoneEntryRoomId;
        private bool _returnHomeAfterZoneEntry;

        // Invisibility cast-on-arrival setting
        private bool _invisibilityEnabled = true;
        private string _invisibilityCommand = "!невидимость!";
        private const string InvisibilityAffectName = "невидимость";

        // Herb skill (auto-detected from "травничество X%" MUD message)
        private int _herbingSkill = 0; // 0 = not yet detected

        // Per-room herb detection
        private volatile bool _waitingForHerbDetection;
        private readonly Timer _herbDetectionTimer;

        // Herbs seen in room description (with timestamp per herb)
        // Herbs arrive BEFORE UpdateCurrentRoom — we keep timestamps and filter by recency
        private readonly List<(HerbEntry Herb, DateTime SeenAt)> _recentHerbList = new List<(HerbEntry, DateTime)>();
        private static readonly TimeSpan HerbSeenWindow = TimeSpan.FromSeconds(3);

        // Sequential gather queue for current room (one `собрать X` at a time)
        private readonly Queue<HerbEntry> _gatherQueue = new Queue<HerbEntry>();

        // Herb database: normalized room message → (gather command, min skill)
        private struct HerbEntry { public string GatherName; public int MinSkill; }
        private static readonly Dictionary<string, HerbEntry> _herbDatabase = BuildHerbDatabase();

        private static Dictionary<string, HerbEntry> BuildHerbDatabase()
        {
            return new Dictionary<string, HerbEntry>(StringComparer.OrdinalIgnoreCase)
            {
                { "куст ромашки растет здесь",              new HerbEntry { GatherName = "ромашки",           MinSkill = 0   } },
                { "несколько одуванчиков растет здесь",     new HerbEntry { GatherName = "одуван",            MinSkill = 0   } },
                { "подорожник растет здесь",                new HerbEntry { GatherName = "подорожник",        MinSkill = 10  } },
                { "клевер растет здесь",                    new HerbEntry { GatherName = "клевер",            MinSkill = 15  } },
                { "папоротник растет здесь",                new HerbEntry { GatherName = "папоротник",        MinSkill = 20  } },
                { "шалфей растет здесь",                    new HerbEntry { GatherName = "шалфей",            MinSkill = 30  } },
                { "табак растет здесь",                     new HerbEntry { GatherName = "табак",             MinSkill = 40  } },
                { "хмель растет здесь",                     new HerbEntry { GatherName = "хмель",             MinSkill = 50  } },
                { "пещерный мох растет здесь",              new HerbEntry { GatherName = "пещерный.мох",      MinSkill = 60  } },
                { "земляной корень виднеется здесь",        new HerbEntry { GatherName = "земляной.корень",   MinSkill = 60  } },
                { "королевский лист растет здесь",          new HerbEntry { GatherName = "королевский.лист",  MinSkill = 75  } },
                { "цветок ириса растет здесь",              new HerbEntry { GatherName = "ирис",              MinSkill = 80  } },
                { "куст яснотки растет здесь",              new HerbEntry { GatherName = "яснотка",           MinSkill = 90  } },
                { "беладона растет здесь",                  new HerbEntry { GatherName = "беладона",          MinSkill = 100 } },
                { "куст колючника растет здесь",            new HerbEntry { GatherName = "колючник",          MinSkill = 105 } },
                { "цветок медуницы растет здесь",           new HerbEntry { GatherName = "медуница",          MinSkill = 115 } },
                { "татарник растет здесь",                  new HerbEntry { GatherName = "татарник",          MinSkill = 125 } },
                { "зеленоватый мох растет здесь",           new HerbEntry { GatherName = "зеленоватый.мох",   MinSkill = 125 } },
            };
        }

        // ─── public API ────────────────────────────────────────────────────────

        public bool IsActive => _state != GatherState.Idle;
        public bool IsAutoRepeat => _autoRepeat;

        public void EnableAutoRepeat()
        {
            _autoRepeat = true;
            PushInfo(string.Format(CultureInfo.InvariantCulture,
                "Автоповтор включён — травник будет перезапускаться каждые {0} мин после возвращения домой.", (int)(AutoRepeatDelayMs / 60000)));
        }

        public void DisableAutoRepeat()
        {
            _autoRepeat = false;
            _autoRepeatTimer.Stop();
            PushInfo("Автоповтор выключен.");
        }

        public void SetInvisibilityCommand(string command)
        {
            _invisibilityCommand = command;
            _invisibilityEnabled = true;
            SaveSettings();
            PushInfo(string.Format(CultureInfo.InvariantCulture, "Команда невидимости: {0}", command));
        }

        public void DisableInvisibility()
        {
            _invisibilityEnabled = false;
            SaveSettings();
            PushInfo("Проверка невидимости отключена.");
        }

        /// <summary>
        /// Called by HerbUnit when a "X растет/виднеется здесь" message is received.
        /// Always caches the herb. If we are already waiting for detection — acts immediately.
        /// </summary>
        public void OnRoomMessageReceived(string normalizedText)
        {
            if (!_herbDatabase.TryGetValue(normalizedText, out var entry)) return;

            // Always accumulate with timestamp
            _recentHerbList.Add((entry, DateTime.UtcNow));

            // If we are in fallback-wait (herbs arrived AFTER UpdateCurrentRoom),
            // cancel the timer and act immediately on what we have so far.
            if (_waitingForHerbDetection && _state == GatherState.GatheringInZone)
            {
                _herbDetectionTimer.Stop();
                var cutoff = DateTime.UtcNow - HerbSeenWindow;
                var current = _recentHerbList.Where(h => h.SeenAt >= cutoff).Select(h => h.Herb).ToList();
                _recentHerbList.Clear();
                if (current.Count > 0)
                    ActOnDetectedHerbs(current);
            }
        }

        // How many "Вы успешно собрали" we are still waiting for in this room
        private int _pendingCollectCount;
        // Room where current gather cycle started — to ignore delayed messages from prev rooms
        private int _gatherRoomId;

        private void ActOnDetectedHerbs(List<HerbEntry> herbs)
        {
            _herbDetectionTimer.Stop();
            _waitingForHerbCollection = true;
            _waitingForHerbDetection = false;
            _gatherQueue.Clear();
            _pendingCollectCount = 0;
            _gatherRoomId = _currentRoom?.RoomId ?? 0;

            // Build gather list: skip herbs we can't gather
            foreach (var herb in herbs)
            {
                if (_herbingSkill > 0 && herb.MinSkill > _herbingSkill)
                    PushInfo(string.Format(CultureInfo.InvariantCulture,
                        "Пропускаем {0} (нужно {1}, у нас {2}).", herb.GatherName, herb.MinSkill, _herbingSkill));
                else
                    _gatherQueue.Enqueue(herb);
            }

            if (_gatherQueue.Count == 0)
            {
                MoveToNextHerbRoom();
                return;
            }

            CastInvisibilityIfNeeded();

            // Send ALL gather commands at once — MUD queues them.
            // Track how many confirmations we expect.
            _pendingCollectCount = _gatherQueue.Count;
            var names = new System.Text.StringBuilder();
            while (_gatherQueue.Count > 0)
            {
                var herb = _gatherQueue.Dequeue();
                _rootModel.PushCommandToConveyor(new TextCommand("собрать " + herb.GatherName));
                if (names.Length > 0) names.Append(", ");
                names.Append(herb.GatherName);
            }
            // Flush the send buffer immediately — without this the commands sit in the
            // buffer and are only sent when the buffer fills or a manual command is typed.
            _rootModel.PushCommandToConveyor(FlushOutputQueueCommand.Instance);
            PushInfo(string.Format(CultureInfo.InvariantCulture,
                "Собираем: {0} (комнат: {1}).", names, _pendingRoomsInZone.Count));

            _retryMoveTimer.Stop();
            _herbCollectionTimer.Stop();
            // Timeout: give each herb up to 10s, total cap
            _herbCollectionTimer.Interval = Math.Max(10000, _pendingCollectCount * 8000);
            _herbCollectionTimer.Start();
        }

        /// <summary>
        /// Called by HerbUnit when "травничество X%" is seen in MUD output.
        /// </summary>
        public void OnHerbingSkillDetected(int skill)
        {
            if (_herbingSkill == skill) return;
            _herbingSkill = skill;
            SaveSettings();
            PushInfo(string.Format(CultureInfo.InvariantCulture, "Травничество обновлено: {0}%.", skill));
        }

        private void OnAutoRepeatTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (!_autoRepeat || _state != GatherState.Idle) return;
            PushInfo("Автоповтор: запускаем следующий обход...");
            StartGathering(_maxDangerLevel);
        }

        public void OutputWindowChanged(RootModel rootModel)
        {
            StopGathering();
            _rootModel = rootModel;
        }

        /// <summary>
        /// Called by ZoneManager on every room transition.
        /// </summary>
        public void UpdateCurrentRoom([CanBeNull] RoomViewModel room, [NotNull] ZoneViewModel zone)
        {
            _currentRoom = room;
            _currentZone = zone;

            if (_state == GatherState.Idle || _rootModel == null || room == null)
                return;

            // Reset watchdog on every room change
            _lastSeenRoomId = room.RoomId;

            switch (_state)
            {
                case GatherState.GatheringInZone:
                    HandleGatheringStep();
                    break;
                case GatherState.TravelingToZone:
                    HandleTravelStep();
                    break;
                case GatherState.ReturningToZoneEntry:
                    HandleReturnToZoneEntryStep();
                    break;
                case GatherState.ReturningHome:
                    HandleReturnStep();
                    break;
            }
        }

        /// <summary>
        /// Start herb gathering with specified max danger tolerance.
        /// </summary>
        public void StartGathering(HerbDangerLevel maxDangerLevel)
        {
            if (_rootModel == null || _currentRoom == null || _currentZone == null)
            {
                PushError("Невозможно начать сбор: нет данных о текущей комнате.");
                return;
            }

            if (_state != GatherState.Idle)
            {
                PushError("Сбор уже запущен. Используйте 'травник стоп' чтобы остановить.");
                return;
            }

            _maxDangerLevel = maxDangerLevel;
            _homeRoomId = _currentRoom.RoomId;
            _homeZone = _currentZone;

            // Build cross-zone herb map from disk
            var allHerbZones = _zoneManager?.ScanAllHerbRooms(maxDangerLevel) ?? new List<KeyValuePair<int, List<int>>>();

            if (!allHerbZones.Any())
            {
                PushInfo("Нет отмеченных травяных клеток с выбранным уровнем опасности.");
                return;
            }

            // Pre-load all target zones so waypoints get registered
            foreach (var kv in allHerbZones)
            {
                if (kv.Key != _currentZone.Id)
                    _zoneManager.GetZone(kv.Key); // triggers RegisterZoneWaypoints
            }

            // Put current zone first, then others
            var orderedZones = new List<KeyValuePair<int, List<int>>>();
            var currentZoneEntry = allHerbZones.FirstOrDefault(kv => kv.Key == _currentZone.Id);
            if (currentZoneEntry.Value != null)
                orderedZones.Add(currentZoneEntry);
            orderedZones.AddRange(allHerbZones.Where(kv => kv.Key != _currentZone.Id));

            _pendingZones = new List<KeyValuePair<int, List<int>>>(orderedZones);

            string dangerText = maxDangerLevel == HerbDangerLevel.Safe ? "безопасные"
                              : maxDangerLevel == HerbDangerLevel.Dangerous ? "безопасные + опасные"
                              : "все";

            int totalRooms = orderedZones.Sum(kv => kv.Value.Count);
            PushInfo(string.Format(CultureInfo.InvariantCulture,
                "Травник запущен. Клеток: {0} в {1} зонах ({2}). Домашняя точка сохранена.",
                totalRooms, orderedZones.Count, dangerText));

            EnterNextZone();
        }

        public void StopGathering()
        {
            if (_state == GatherState.Idle)
                return;

            _herbCollectionTimer.Stop();
            _herbDetectionTimer.Stop();
            _retryMoveTimer.Stop();
            _autoRepeatTimer.Stop();
            _autoRepeat = false;
            _waitingForHerbCollection = false;
            _waitingForHerbDetection = false;
            _state = GatherState.Idle;
            _pendingRoomsInZone.Clear();
            _pendingZones = null;
            _routeManager.StopRoutingToDestination();
            PushInfo("Сбор травы остановлен.");
        }

        public void ReturnHome()
        {
            if (_homeRoomId == 0 || _homeZone == null)
            {
                PushError("Домашняя точка не сохранена. Запустите сбор командой 'травник старт'.");
                return;
            }

            if (_state != GatherState.Idle)
                StopGatheringQuiet();

            _state = GatherState.ReturningHome;
            PushInfo("Возвращаемся домой...");
            NavigateHome();
        }

        public void ShowHelp()
        {
            if (_rootModel == null) return;
            PushInfo("─── Травник — команды ────────────────────────────");
            PushInfo("  травник старт              — собирать только безопасные клетки");
            PushInfo("  травник старт опасно       — + опасные клетки");
            PushInfo("  травник старт очень опасно — все клетки");
            PushInfo("  травник стоп               — остановить сбор (сбрасывает автоповтор)");
            PushInfo("  травник домой              — вернуться в точку старта");
            PushInfo("  травник список             — показать все отмеченные клетки");
            PushInfo("  травник авто               — включить автоповтор (каждые 10 мин)");
            PushInfo("  травник авто стоп          — выключить автоповтор");
            PushInfo("─── Травник — настройка ──────────────────────────");
            PushInfo("  1. Откройте карту зоны (Map) и войдите в нужную комнату.");
            PushInfo("  2. Двойной клик по клетке → отметьте 'Есть трава' и уровень опасности.");
            PushInfo("  3. Для кросс-зонного обхода нужен маршрут до зоны:");
            PushInfo("     route start <имя>       — начать запись маршрута");
            PushInfo("     route stoprecording <имя> — сохранить маршрут");
            PushInfo("     (встаньте на вход в зону перед записью)");
            PushInfo("─── Травник — невидимость ────────────────────────");
            PushInfo(string.Format(CultureInfo.InvariantCulture,
                "  травник невидимость <команда>  — задать команду каста (сейчас: {0})",
                _invisibilityEnabled ? _invisibilityCommand : "выкл"));
            PushInfo("  травник невидимость выкл        — не проверять невидимость");
            PushInfo("  При прибытии на травяную клетку кастует невидимость, если её нет.");
            PushInfo("─── Травник — навык травничества ─────────────────");
            PushInfo(string.Format(CultureInfo.InvariantCulture,
                "  Текущее травничество: {0}",
                _herbingSkill > 0 ? _herbingSkill + "%" : "не определено"));
            PushInfo("  Травы с уровнем выше навыка будут пропущены.");
            PushInfo("  Чтобы обновить — напишите 'умения', % обновится автоматически.");
            PushInfo("──────────────────────────────────────────────────");
        }

        public void ShowList()
        {
            if (_rootModel == null) return;

            var allZones = _zoneManager?.ScanAllHerbRooms(HerbDangerLevel.VeryDangerous) ?? new List<KeyValuePair<int, List<int>>>();
            if (!allZones.Any())
            {
                PushInfo("Нет отмеченных травяных клеток.");
                return;
            }

            int total = 0;
            foreach (var kv in allZones)
            {
                string zoneName = _zoneManager?.GetZoneName(kv.Key);
                if (string.IsNullOrEmpty(zoneName)) zoneName = "зона " + kv.Key;
                PushInfo(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: {1} клеток", zoneName, kv.Value.Count));
                total += kv.Value.Count;
            }
            PushInfo(string.Format(CultureInfo.InvariantCulture, "Всего: {0} клеток в {1} зонах.", total, allZones.Count));
        }

        // ─── private: state machine ────────────────────────────────────────────

        private void HandleGatheringStep()
        {
            // Waiting for collection or herb detection — ignore room updates
            if (_waitingForHerbCollection || _waitingForHerbDetection)
                return;

            // Arrived at a target herb room — collect all herbs from room description
            if (_pendingRoomsInZone.Contains(_currentRoom.RoomId))
            {
                _pendingRoomsInZone.Remove(_currentRoom.RoomId);

                // Set detection flag FIRST so any queued ThreadPool timer callbacks bail out.
                // Do NOT reset _waitingForHerbCollection to false here — keeps collection
                // timer callbacks from calling MoveToNextHerbRoom during detection phase.
                _waitingForHerbDetection = true;

                _retryMoveTimer.Stop();
                _herbCollectionTimer.Stop();
                _herbDetectionTimer.Stop();

                // Herbs arrive BEFORE UpdateCurrentRoom — list is already full by now.
                // Act immediately; use timer only as fallback if list is empty.
                var cutoff = DateTime.UtcNow - HerbSeenWindow;
                var current = _recentHerbList.Where(h => h.SeenAt >= cutoff).Select(h => h.Herb).ToList();
                _recentHerbList.Clear();

                if (current.Count > 0)
                {
                    ActOnDetectedHerbs(current);
                }
                else
                {
                    // Fallback: herbs may arrive slightly after — wait up to 1s
                    _herbDetectionTimer.Interval = 1000;
                    _herbDetectionTimer.Start();
                }
                return;
            }

            MoveToNextHerbRoom();
        }

        private void OnHerbDetectionTimerElapsed(object sender, ElapsedEventArgs e)
        {
            // If OnRoomMessageReceived already handled the herbs, bail out.
            if (!_waitingForHerbDetection) return;
            if (_state != GatherState.GatheringInZone) return;

            // Only use herbs seen within the last 3 seconds (ignore stale entries from prev rooms)
            var cutoff = DateTime.UtcNow - HerbSeenWindow;
            var current = _recentHerbList.Where(h => h.SeenAt >= cutoff).Select(h => h.Herb).ToList();
            _recentHerbList.Clear(); // clear — done with this room

            if (current.Count == 0)
            {
                _waitingForHerbDetection = false;
                PushInfo("Трава не обнаружена в комнате, пропускаем.");
                MoveToNextHerbRoom();
                return;
            }

            ActOnDetectedHerbs(current);
        }

        private void CastInvisibilityIfNeeded()
        {
            if (!_invisibilityEnabled || string.IsNullOrEmpty(_invisibilityCommand) || _rootModel == null)
                return;

            var self = _rootModel.GroupStatus?.FirstOrDefault();
            if (self == null) return;

            bool hasInvisibility = self.Affects.Any(a =>
                string.Equals(a.Name, InvisibilityAffectName, StringComparison.OrdinalIgnoreCase));

            if (!hasInvisibility)
                _rootModel.PushCommandToConveyor(new TextCommand(_invisibilityCommand));
        }

        private void OnRetryMoveTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_state == GatherState.Idle || _waitingForHerbCollection || _waitingForHerbDetection || _currentRoom == null)
                return;

            // Don't interfere while GotoDestination is active
            if (_routeManager.CanStopCurrentRoute)
                return;

            // If we haven't moved since last command — retry
            if (_currentRoom.RoomId != _lastSeenRoomId)
                return;

            switch (_state)
            {
                case GatherState.GatheringInZone:
                    var nextRoom = FindNearestHerbRoom();
                    if (nextRoom != null)
                        _routeManager.NavigateToRoom(nextRoom, firstStepOnly: true);
                    break;

                case GatherState.ReturningToZoneEntry:
                    var entryRoom = _currentZone?.AllRooms.FirstOrDefault(r => r.RoomId == _zoneEntryRoomId);
                    if (entryRoom != null)
                        _routeManager.NavigateToRoom(entryRoom, firstStepOnly: true);
                    break;

                case GatherState.ReturningHome:
                    if (_currentZone?.Id == _homeZone?.Id)
                    {
                        var homeRoom = _homeZone?.AllRooms.FirstOrDefault(r => r.RoomId == _homeRoomId);
                        if (homeRoom != null)
                            _routeManager.NavigateToRoom(homeRoom, firstStepOnly: true);
                    }
                    break;
            }
        }

        /// <summary>
        /// Called by HerbUnit when "Вы успешно собрали..." message is received.
        /// Resets the collection timer so we wait for all herbs to be collected.
        /// </summary>
        public void OnHerbCollected()
        {
            if (_state != GatherState.GatheringInZone)
                return;

            // Ignore delayed confirmations from a previous herb room
            if (_currentRoom != null && _currentRoom.RoomId != _gatherRoomId)
                return;

            _pendingCollectCount = Math.Max(0, _pendingCollectCount - 1);

            if (_pendingCollectCount > 0)
            {
                // Still waiting for more confirmations — reset timeout
                _herbCollectionTimer.Stop();
                _herbCollectionTimer.Interval = 10000;
                _herbCollectionTimer.Start();
                return;
            }

            // All done — move to next room
            _retryMoveTimer.Stop();
            _herbCollectionTimer.Stop();
            _waitingForHerbCollection = false;
            MoveToNextHerbRoom();
        }

        private void OnHerbCollectionTimerElapsed(object sender, ElapsedEventArgs e)
        {
            _waitingForHerbCollection = false;

            if (_state != GatherState.GatheringInZone || _waitingForHerbDetection)
                return;

            MoveToNextHerbRoom();
        }

        private void MoveToNextHerbRoom()
        {
            if (_pendingRoomsInZone.Count > 0)
            {
                var nextRoom = FindNearestHerbRoom();
                if (nextRoom != null)
                {
                    _routeManager.NavigateToRoom(nextRoom, firstStepOnly: true);
                    _lastSeenRoomId = _currentRoom?.RoomId ?? 0;
                    _retryMoveTimer.Start();
                }
                else
                {
                    PushInfo("Не удалось достичь некоторых травяных клеток в зоне, пропускаем.");
                    _pendingRoomsInZone.Clear();
                    EnterNextZone();
                }
            }
            else
            {
                EnterNextZone();
            }
        }

        private void HandleTravelStep()
        {
            // Wait until RouteManager finishes navigating to the waypoint AND we are in the target zone.
            // RouteManager stops itself when it reaches the destination room.
            if (_currentZone.Id == _travelTargetZoneId && !_routeManager.CanStopCurrentRoute)
            {
                _zoneEntryRoomId = _currentRoom.RoomId;
                PushInfo(string.Format(CultureInfo.InvariantCulture, "Прибыли в зону {0}. Начинаем сбор...", _currentZone.Name));
                _state = GatherState.GatheringInZone;
                HandleGatheringStep();
            }
            // else: RouteManager is still navigating, nothing to do
        }

        private void HandleReturnToZoneEntryStep()
        {
            // Still not on a route room — keep walking
            var routeRoom = FindNearestRouteRoomInCurrentZone();
            if (routeRoom != null)
            {
                _routeManager.NavigateToRoom(routeRoom, firstStepOnly: true);
                _lastSeenRoomId = _currentRoom?.RoomId ?? 0;
                _retryMoveTimer.Start();
                return;
            }

            // Reached zone entry — now go home or to next zone
            if (_returnHomeAfterZoneEntry)
            {
                _returnHomeAfterZoneEntry = false;
                _state = GatherState.Idle;
                NavigateHome();
                return;
            }

            _state = GatherState.TravelingToZone;
            bool started = _routeManager.GotoDestination(_travelTargetWaypoint);
            if (!started)
            {
                PushInfo(string.Format(CultureInfo.InvariantCulture,
                    "Не удалось построить маршрут до '{0}', пропускаем зону.", _travelTargetWaypoint));
                _state = GatherState.GatheringInZone;
                EnterNextZone();
            }
        }

        private void HandleReturnStep()
        {
            if (_currentZone?.Id == _homeZone?.Id && _currentRoom?.RoomId == _homeRoomId)
            {
                _state = GatherState.Idle;
                PushInfo("Вернулись домой!");
                if (_autoRepeat)
                {
                    PushInfo(string.Format(CultureInfo.InvariantCulture,
                        "Автоповтор: следующий обход через {0} мин.", (int)(AutoRepeatDelayMs / 60000)));
                    _autoRepeatTimer.Start();
                }
                return;
            }

            if (_currentZone?.Id == _homeZone?.Id)
            {
                // Same zone — step by step BFS to home room
                var homeRoom = _homeZone.AllRooms.FirstOrDefault(r => r.RoomId == _homeRoomId);
                if (homeRoom != null)
                {
                    _routeManager.NavigateToRoom(homeRoom, firstStepOnly: true);
                    _lastSeenRoomId = _currentRoom?.RoomId ?? 0;
                    _retryMoveTimer.Start();
                }
            }
            // else: GotoDestination is handling cross-zone navigation
        }

        private void EnterNextZone()
        {
            if (_pendingZones == null || _pendingZones.Count == 0)
            {
                PushInfo("Сбор травы завершён! Все отмеченные клетки посещены. Возвращаемся домой...");
                ReturnHome();
                return;
            }

            var nextZoneEntry = PickNearestPendingZone();
            int zoneId = nextZoneEntry.Key;
            var herbRoomIds = nextZoneEntry.Value;

            if (_currentZone.Id == zoneId)
            {
                // Already in this zone
                _pendingRoomsInZone.Clear();
                foreach (var id in herbRoomIds)
                    _pendingRoomsInZone.Add(id);

                // Filter to only rooms that exist in current zone
                var allCurrentRoomIds = new HashSet<int>(_currentZone.AllRooms.Select(r => r.RoomId));
                _pendingRoomsInZone.IntersectWith(allCurrentRoomIds);

                if (_pendingRoomsInZone.Count == 0)
                {
                    EnterNextZone();
                    return;
                }

                _state = GatherState.GatheringInZone;
                PushInfo(string.Format(CultureInfo.InvariantCulture,
                    "Начинаем сбор в текущей зоне: {0} клеток.", _pendingRoomsInZone.Count));
                HandleGatheringStep();
            }
            else
            {
                // Need to travel to another zone — find a waypoint there
                string waypoint = FindWaypointForZone(zoneId);
                if (waypoint == null)
                {
                    var zoneName = _zoneManager != null ? _zoneManager.GetZoneName(zoneId) : zoneId.ToString();
                    if (string.IsNullOrEmpty(zoneName)) zoneName = zoneId.ToString(CultureInfo.InvariantCulture);
                    PushInfo(string.Format(CultureInfo.InvariantCulture,
                        "Нет именованного маршрута до зоны '{0}' — пропускаем. (Добавьте маршрут через 'route rec')", zoneName));
                    EnterNextZone();
                    return;
                }

                _pendingRoomsInZone.Clear();
                foreach (var id in herbRoomIds)
                    _pendingRoomsInZone.Add(id);

                _travelTargetZoneId = zoneId;
                _travelTargetWaypoint = waypoint;
                _state = GatherState.TravelingToZone;

                PushInfo(string.Format(CultureInfo.InvariantCulture,
                    "Двигаемся в зону {0} через '{1}' ({2} клеток)...", zoneId, waypoint, herbRoomIds.Count));

                // If we're in an arbitrary herb room not on any route, navigate to nearest
                // route endpoint in this zone before calling GotoDestination.
                var routeRoom = FindNearestRouteRoomInCurrentZone();
                if (routeRoom != null)
                {
                    _state = GatherState.ReturningToZoneEntry;
                    _routeManager.NavigateToRoom(routeRoom, firstStepOnly: true);
                    _lastSeenRoomId = _currentRoom?.RoomId ?? 0;
                    _retryMoveTimer.Start();
                    return;
                }

                bool started = _routeManager.GotoDestination(waypoint);
                if (!started)
                {
                    PushInfo(string.Format(CultureInfo.InvariantCulture,
                        "Не удалось построить маршрут до '{0}', пропускаем зону.", waypoint));
                    _state = GatherState.GatheringInZone; // stay, try next
                    EnterNextZone();
                }
            }
        }

        private KeyValuePair<int, List<int>> PickNearestPendingZone()
        {
            // If currently in one of the pending zones — pick it first (already there)
            var currentEntry = _pendingZones.FirstOrDefault(kv => kv.Key == _currentZone.Id);
            if (currentEntry.Value != null)
            {
                _pendingZones.Remove(currentEntry);
                return currentEntry;
            }

            // Find waypoint for each pending zone and pick the one with shortest route distance
            // from our current waypoint (_travelTargetWaypoint, or null if starting fresh).
            string fromWaypoint = _travelTargetWaypoint;

            // If no previous waypoint, try to find waypoint for current zone as starting reference
            if (string.IsNullOrEmpty(fromWaypoint) && _currentZone != null)
                fromWaypoint = FindWaypointForZone(_currentZone.Id);

            if (string.IsNullOrEmpty(fromWaypoint))
            {
                var first = _pendingZones[0];
                _pendingZones.RemoveAt(0);
                return first;
            }

            int bestDist = int.MaxValue;
            int bestIdx = 0;

            for (int i = 0; i < _pendingZones.Count; i++)
            {
                string wp = FindWaypointForZone(_pendingZones[i].Key);
                if (wp == null) continue;

                int dist = GetRoutDistance(fromWaypoint, wp);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            var chosen = _pendingZones[bestIdx];
            _pendingZones.RemoveAt(bestIdx);
            return chosen;
        }

        /// <summary>
        /// Returns the actual room count distance between two named waypoints by doing
        /// a Dijkstra search over the route graph, using route.RouteRoomIdentifiers.Count
        /// as edge weight. This gives a much better estimate than hop count.
        /// </summary>
        private int GetRoutDistance(string fromWaypoint, string toWaypoint)
        {
            if (string.Equals(fromWaypoint, toWaypoint, StringComparison.CurrentCultureIgnoreCase))
                return 0;

            // Build adjacency on demand: waypoint -> list of (neighbour, roomCount)
            // We use Dijkstra with room count as cost.
            var dist = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase)
            {
                [fromWaypoint] = 0
            };
            var queue = new SortedSet<(int cost, string node)>(Comparer<(int, string)>.Create((a, b) =>
                a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : string.Compare(a.Item2, b.Item2, StringComparison.Ordinal)));
            queue.Add((0, fromWaypoint));

            while (queue.Count > 0)
            {
                var (cost, node) = queue.Min;
                queue.Remove(queue.Min);

                if (string.Equals(node, toWaypoint, StringComparison.CurrentCultureIgnoreCase))
                    return cost;

                if (dist.TryGetValue(node, out int known) && known < cost)
                    continue;

                foreach (var route in _routeManager.AllRoutes)
                {
                    string neighbour = null;
                    if (string.Equals(route.StartName, node, StringComparison.CurrentCultureIgnoreCase))
                        neighbour = route.EndName;
                    else if (string.Equals(route.EndName, node, StringComparison.CurrentCultureIgnoreCase))
                        neighbour = route.StartName;

                    if (neighbour == null) continue;

                    int edgeCost = route.RouteRoomIdentifiers.Count;
                    int newCost = cost + edgeCost;
                    if (!dist.TryGetValue(neighbour, out int existing) || newCost < existing)
                    {
                        dist[neighbour] = newCost;
                        queue.Add((newCost, neighbour));
                    }
                }
            }

            return int.MaxValue;
        }

        private void NavigateHome()
        {
            if (_homeZone == null || _currentRoom == null) return;

            if (_currentZone.Id == _homeZone.Id)
            {
                // Same zone — BFS directly
                _state = GatherState.ReturningHome;
                var homeRoom = _homeZone.AllRooms.FirstOrDefault(r => r.RoomId == _homeRoomId);
                if (homeRoom != null)
                {
                    _routeManager.NavigateToRoom(homeRoom, firstStepOnly: true);
                    _lastSeenRoomId = _currentRoom?.RoomId ?? 0;
                    _retryMoveTimer.Start();
                }
                return;
            }

            // Different zone — if not on a route room, navigate to nearest one first
            var routeRoom = FindNearestRouteRoomInCurrentZone();
            if (routeRoom != null)
            {
                _returnHomeAfterZoneEntry = true;
                _state = GatherState.ReturningToZoneEntry;
                _routeManager.NavigateToRoom(routeRoom, firstStepOnly: true);
                _lastSeenRoomId = _currentRoom?.RoomId ?? 0;
                _retryMoveTimer.Start();
                return;
            }

            _state = GatherState.ReturningHome;
            string waypoint = FindWaypointForZone(_homeZone.Id);
            if (waypoint != null)
            {
                _routeManager.GotoDestination(waypoint);
            }
            else
            {
                PushError("Не удалось найти маршрут домой.");
                _state = GatherState.Idle;
            }
        }

        // ─── private: navigation helpers ───────────────────────────────────────

        [CanBeNull]
        private RoomViewModel FindNearestHerbRoom()
        {
            if (_currentRoom == null || _currentZone == null) return null;

            RoomViewModel nearest = null;
            int minSteps = int.MaxValue;

            foreach (var roomId in _pendingRoomsInZone)
            {
                var targetRoom = _currentZone.AllRooms.FirstOrDefault(r => r.RoomId == roomId);
                if (targetRoom == null) continue;

                var path = RouteManager.FindRouteToRoom(_currentRoom, targetRoom).ToList();
                if (path.Count > 0 && path.Count < minSteps)
                {
                    minSteps = path.Count;
                    nearest = targetRoom;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Finds the nearest room in the current zone that is a route endpoint (start or end of any route).
        /// Returns null if no such room exists or we're already there.
        /// </summary>
        [CanBeNull]
        private RoomViewModel FindNearestRouteRoomInCurrentZone()
        {
            if (_currentRoom == null || _currentZone == null) return null;

            var zoneRoomIds = new HashSet<int>(_currentZone.AllRooms.Select(r => r.RoomId));
            var routeRoomIds = new HashSet<int>();

            foreach (var route in _routeManager.AllRoutes)
            {
                if (zoneRoomIds.Contains(route.StartRoomId)) routeRoomIds.Add(route.StartRoomId);
                if (zoneRoomIds.Contains(route.EndRoomId))   routeRoomIds.Add(route.EndRoomId);
            }

            if (routeRoomIds.Count == 0) return null;
            if (routeRoomIds.Contains(_currentRoom.RoomId)) return null; // already on a route room

            // Find nearest by BFS distance
            RoomViewModel nearest = null;
            int minDist = int.MaxValue;
            foreach (var rid in routeRoomIds)
            {
                var room = _currentZone.AllRooms.FirstOrDefault(r => r.RoomId == rid);
                if (room == null) continue;
                var path = RouteManager.FindRouteToRoom(_currentRoom, room).ToList();
                if (path.Count > 0 && path.Count < minDist)
                {
                    minDist = path.Count;
                    nearest = room;
                }
            }
            return nearest;
        }

        [CanBeNull]
        private string FindWaypointForZone(int zoneId)
        {
            // Look for a route endpoint whose room is in the target zone.
            // We check loaded zones first (via AllRoutes + ZoneManager's loaded data
            // is not directly accessible, so we match by available destinations).
            foreach (var route in _routeManager.AllRoutes)
            {
                // We'll try to match by checking which waypoints are reachable.
                // If route start or end is "in" the target zone, use its name.
                // We do this by checking ZoneViewModel.AllRooms via the route manager's
                // available info — but we don't have direct zone access here.
                // Instead, use the AvailableDestinations list and match zone ID
                // via a separate index we build on demand.
                _ = route; // suppress warning — real logic below
            }

            // Fallback: try zone name as waypoint name (works when zone name == route point name)
            // This is populated when HerbManager gets access to loaded zone names.
            // For now return null (zone must have a named waypoint in Routes.xml)
            return _zoneIdToWaypoint.TryGetValue(zoneId, out var wp) ? wp : null;
        }

        // Built externally by ZoneManager when zones are loaded.
        // zoneId → nearest waypoint name reachable from that zone
        private readonly Dictionary<int, string> _zoneIdToWaypoint = new Dictionary<int, string>();

        /// <summary>
        /// Called by ZoneManager when a zone is loaded so we can register its waypoints.
        /// </summary>
        public void RegisterZoneWaypoints([NotNull] ZoneViewModel zone)
        {
            if (_zoneIdToWaypoint.ContainsKey(zone.Id))
                return;

            var zoneRoomIds = new HashSet<int>(zone.AllRooms.Select(r => r.RoomId));

            foreach (var route in _routeManager.AllRoutes)
            {
                if (zoneRoomIds.Contains(route.StartRoomId) && !_zoneIdToWaypoint.ContainsKey(zone.Id))
                {
                    _zoneIdToWaypoint[zone.Id] = route.StartName;
                    break;
                }
                if (zoneRoomIds.Contains(route.EndRoomId) && !_zoneIdToWaypoint.ContainsKey(zone.Id))
                {
                    _zoneIdToWaypoint[zone.Id] = route.EndName;
                    break;
                }
            }
        }

        // ─── settings persistence ──────────────────────────────────────────────

        private static string GetSettingsFilePath()
        {
            return Path.Combine(SettingsHolder.Instance.Folder, "herb_settings.xml");
        }

        public void LoadSettings()
        {
            try
            {
                var path = GetSettingsFilePath();
                if (!File.Exists(path)) return;

                var doc = XDocument.Load(path);
                var root = doc.Root;
                if (root == null) return;

                var invis = root.Element("InvisibilityCommand");
                if (invis != null)
                {
                    var enabled = invis.Attribute("enabled");
                    _invisibilityEnabled = enabled == null || enabled.Value != "false";
                    _invisibilityCommand = invis.Value ?? "!невидимость!";
                }

                var skill = root.Element("HerbingSkill");
                if (skill != null && int.TryParse(skill.Value, out int s))
                    _herbingSkill = s;
            }
            catch { /* ignore corrupt settings */ }
        }

        private void SaveSettings()
        {
            try
            {
                var doc = new XDocument(new XElement("HerbSettings",
                    new XElement("InvisibilityCommand",
                        new XAttribute("enabled", _invisibilityEnabled ? "true" : "false"),
                        _invisibilityCommand),
                    new XElement("HerbingSkill", _herbingSkill)));
                doc.Save(GetSettingsFilePath());
            }
            catch { /* ignore save errors */ }
        }

        // ─── private: helpers ──────────────────────────────────────────────────

        private void StopGatheringQuiet()
        {
            _herbCollectionTimer.Stop();
            _herbDetectionTimer.Stop();
            _retryMoveTimer.Stop();
            _waitingForHerbCollection = false;
            _waitingForHerbDetection = false;
            _state = GatherState.Idle;
            _pendingRoomsInZone.Clear();
            _pendingZones = null;
            _routeManager.StopRoutingToDestination();
        }

        private void PushInfo(string text)
        {
            _rootModel?.PushMessageToConveyor(new InfoMessage(text, TextColor.BrightGreen));
        }

        private void PushError(string text)
        {
            _rootModel?.PushMessageToConveyor(new ErrorMessage(text));
        }
    }
}
