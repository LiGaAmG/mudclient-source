using Adan.Client.Commands;
using Adan.Client.Common.Commands;
using Adan.Client.Common.Controls;
using Adan.Client.Common.Model;
using Adan.Client.Common.Plugins;
using Adan.Client.Common.Settings;
using Adan.Client.Common.Themes;
using Adan.Client.Common.Utils;
using Adan.Client.Common.ViewModel;
using Adan.Client.Controls;
using Adan.Client.Dialogs;
using Adan.Client.Model.ActionDescriptions;
using Adan.Client.Model.ActionParameters;
using Adan.Client.Model.Actions;
using Adan.Client.Model.ParameterDescriptions;
using Adan.Client.ViewModel;
using CSLib.Net.Annotations;
using CSLib.Net.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Adan.Client.Resources.AvalonDock;
using Xceed.Wpf.AvalonDock.Layout;
using Xceed.Wpf.AvalonDock.Layout.Serialization;
using Adan.Client.Common.Messages;

namespace Adan.Client
{
    using System.Xml.Serialization;
    using Settings;

    public partial class MainWindow
    {
        #region Constants and Fields

        private readonly IList<Window> _allWidgets = new List<Window>();
        private readonly IList<OutputWindow> _outputWindows = new List<OutputWindow>();
        private readonly IList<RootModel> _allRootModels = new List<RootModel>();

        private WindowState _nonFullScreenWindowState;
        private IntPtr _smallIconHandle = IntPtr.Zero;
        private IntPtr _largeIconHandle = IntPtr.Zero;

        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        private System.Threading.Timer _uiLatencyTimer;
        private int _lastGen0, _lastGen1, _lastGen2;
        private Dictionary<string, long[]> _perfSnapshot;
        private long _perfMsgSnapshot;
        private System.Diagnostics.Stopwatch _perfIntervalSw;

        #endregion

        /// <summary>
        /// Раз в секунду меряет, сколько BeginInvoke ждёт в очереди диспетчера —
        /// это и есть «отзывчивость» UI. Результат — в индикатор справа от меню и в PerfStats (#perf).
        /// </summary>
        private void StartUiLatencyMonitor()
        {
            _uiLatencyTimer = new System.Threading.Timer(_ =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
                    {
                        sw.Stop();
                        long ms = sw.ElapsedMilliseconds;
                        Adan.Client.Common.Conveyor.PerfStats.RecordUiLatency(ms);
                        if (ms >= 100)
                        {
                            // Дельты GC с прошлого тика: если фриз совпал со всплеском сборок —
                            // виновник GC, а не код на UI-потоке.
                            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                            Adan.Client.Common.Conveyor.PerfLog.WriteTotal("UI_LATENCY", ms,
                                string.Format("dispatcher queue wait; GC delta g0={0} g1={1} g2={2}",
                                    g0 - _lastGen0, g1 - _lastGen1, g2 - _lastGen2));
                        }
                        _lastGen0 = GC.CollectionCount(0);
                        _lastGen1 = GC.CollectionCount(1);
                        _lastGen2 = GC.CollectionCount(2);

                        _uiLatencyIndicator.Text = string.Format("UI: {0} ms", ms);
                        _uiLatencyIndicator.Foreground =
                            ms < 50 ? System.Windows.Media.Brushes.LimeGreen :
                            ms < 150 ? System.Windows.Media.Brushes.Orange :
                            System.Windows.Media.Brushes.Red;

                        UpdateConveyorIndicators();

                        int timers = Adan.Client.Common.Conveyor.PerfStats.ActiveGameTimers;
                        _timersIndicator.Text = string.Format("⏱{0}", timers);
                        _timersIndicator.Foreground =
                            timers == 0 ? System.Windows.Media.Brushes.Gray :
                            timers < 10 ? System.Windows.Media.Brushes.LimeGreen :
                            System.Windows.Media.Brushes.Orange;
                    }));
                }
                catch
                {
                    // Окно закрывается — молча выходим
                }
            }, null, 2000, 1000);
        }

        private System.Threading.Timer _pingTimer;

        // EMA-сглаживание RTT по каждому табу: храним последние 4 сэмпла.
        private readonly Dictionary<string, Queue<long>> _rttEma =
            new Dictionary<string, Queue<long>>(StringComparer.Ordinal);
        // Гистерезис: сколько тиков подряд EMA >= порога красного.
        private readonly Dictionary<string, int> _rttRedStreak =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private const int RttEmaWindow = 4;
        private const int RttRedStreakThreshold = 2;   // тиков подряд до красного
        private const long RttOrangeMs = 400;
        private const long RttRedMs = 1000;

        private long GetEma(string uid, long sample)
        {
            Queue<long> q;
            if (!_rttEma.TryGetValue(uid, out q))
            {
                q = new Queue<long>(RttEmaWindow);
                _rttEma[uid] = q;
            }
            q.Enqueue(sample);
            while (q.Count > RttEmaWindow) q.Dequeue();
            long sum = 0;
            foreach (var v in q) sum += v;
            return sum / q.Count;
        }

        private System.Windows.Media.Brush RttBrush(string uid, long ema)
        {
            if (ema < 0)
                return System.Windows.Media.Brushes.Gray;
            if (ema < RttOrangeMs)
            {
                _rttRedStreak[uid] = 0;
                return System.Windows.Media.Brushes.LimeGreen;
            }
            if (ema < RttRedMs)
            {
                _rttRedStreak[uid] = 0;
                return System.Windows.Media.Brushes.Orange;
            }
            // Красный только если N тиков подряд выше порога
            int streak;
            _rttRedStreak.TryGetValue(uid, out streak);
            _rttRedStreak[uid] = streak + 1;
            return streak + 1 >= RttRedStreakThreshold
                ? System.Windows.Media.Brushes.Red
                : System.Windows.Media.Brushes.Orange;
        }

        /// <summary>
        /// Пассивный RTT-монитор: SEND→первый ответ сервера, per-tab, с EMA и гистерезисом.
        /// </summary>
        private void StartServerPingMonitor()
        {
            _pingTimer = new System.Threading.Timer(_ =>
            {
                var perUid = Adan.Client.Common.Conveyor.PerfStats.GetEffectiveRttPerUid();

                // Добавляем RoomWait к текущему окну если он больше
                long roomWait = Adan.Client.Common.Conveyor.PerfStats.RoomWaitMs;

                // Строим данные для отображения (вычисления в фоновом потоке)
                var segments = new List<Tuple<string, System.Windows.Media.Brush>>();

                if (perUid.Count == 0)
                {
                    segments.Add(Tuple.Create("rtt: ?", (System.Windows.Media.Brush)System.Windows.Media.Brushes.Gray));
                }
                else
                {
                    segments.Add(Tuple.Create("rtt: ", (System.Windows.Media.Brush)System.Windows.Media.Brushes.Gray));
                    bool first = true;
                    foreach (var kv in perUid.OrderBy(x => x.Key))
                    {
                        long effective = kv.Value;
                        if (roomWait > effective) effective = roomWait;
                        long ema = GetEma(kv.Key, effective);
                        var brush = RttBrush(kv.Key, ema);
                        string label = ema.ToString();

                        if (!first)
                            segments.Add(Tuple.Create("|", (System.Windows.Media.Brush)System.Windows.Media.Brushes.Gray));
                        segments.Add(Tuple.Create(label, brush));
                        first = false;
                    }
                }

                try
                {
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                    {
                        _pingIndicator.Inlines.Clear();
                        foreach (var seg in segments)
                            _pingIndicator.Inlines.Add(new Run(seg.Item1) { Foreground = seg.Item2 });
                    }));
                }
                catch
                {
                }
            }, null, 5000, 500);
        }

        private long _dispatcherOpStartTimestamp;

        /// <summary>
        /// Трассировка операций диспетчера: любая операция на UI-потоке дольше 50 мс
        /// пишется в perf-лог вместе с именем метода — чтобы найти, кто блокирует интерфейс.
        /// Операции диспетчера не вкладываются друг в друга, поэтому достаточно одного поля.
        /// </summary>
        private void StartDispatcherOpTracing()
        {
            var hooks = Dispatcher.Hooks;
            hooks.OperationStarted += (s, e) =>
            {
                _dispatcherOpStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            };
            hooks.OperationCompleted += (s, e) =>
            {
                long start = _dispatcherOpStartTimestamp;
                if (start == 0) return;
                long ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                if (ms >= 50)
                    Adan.Client.Common.Conveyor.PerfLog.WriteTotal("UI_OP", ms, DescribeDispatcherOperation(e.Operation));
            };
        }

        private static string DescribeDispatcherOperation(System.Windows.Threading.DispatcherOperation op)
        {
            try
            {
                var field = typeof(System.Windows.Threading.DispatcherOperation)
                    .GetField("_method", BindingFlags.NonPublic | BindingFlags.Instance);
                var del = field != null ? field.GetValue(op) as Delegate : null;
                if (del != null && del.Method != null)
                {
                    var declaring = del.Method.DeclaringType != null ? del.Method.DeclaringType.FullName : "?";
                    var targetType = del.Target != null ? del.Target.GetType().FullName : "";
                    return declaring + "." + del.Method.Name + (targetType.Length > 0 ? " (target: " + targetType + ")" : "");
                }
            }
            catch
            {
            }
            return "unknown";
        }

        /// <summary>
        /// Считает дельту статистики конвейера с прошлого тика (≈1 сек):
        /// самый загруженный юнит (% времени) и поток сообщений (сообщ/с).
        /// </summary>
        private void UpdateConveyorIndicators()
        {
            var current = Adan.Client.Common.Conveyor.PerfStats.Snapshot();
            long currentMsgs = Adan.Client.Common.Conveyor.PerfStats.TotalMessages;

            if (_perfSnapshot != null && _perfIntervalSw != null)
            {
                long intervalTicks = _perfIntervalSw.ElapsedTicks;
                if (intervalTicks > 0)
                {
                    string topName = null;
                    long topDeltaTicks = 0;

                    foreach (var kv in current)
                    {
                        long prevTicks = 0;
                        long[] prev;
                        if (_perfSnapshot.TryGetValue(kv.Key, out prev))
                            prevTicks = prev[0];

                        long deltaTicks = kv.Value[0] - prevTicks;
                        if (deltaTicks > topDeltaTicks)
                        {
                            topDeltaTicks = deltaTicks;
                            topName = kv.Key;
                        }
                    }

                    double topPercent = 100.0 * topDeltaTicks / intervalTicks;
                    double msgsPerSec = (double)(currentMsgs - _perfMsgSnapshot) * System.Diagnostics.Stopwatch.Frequency / intervalTicks;

                    if (topName != null && topPercent >= 0.5)
                    {
                        // Убираем шумные суффиксы для компактности: TriggersConveyorUnit -> Triggers
                        var shortName = topName.Replace("ConveyorUnit", "").Replace("Unit", "");
                        _convIndicator.Text = string.Format("{0}: {1:0}%", shortName, topPercent);
                        _convIndicator.Foreground =
                            topPercent < 10 ? System.Windows.Media.Brushes.LimeGreen :
                            topPercent < 30 ? System.Windows.Media.Brushes.Orange :
                            System.Windows.Media.Brushes.Red;
                    }
                    else
                    {
                        _convIndicator.Text = "конв: ~0%";
                        _convIndicator.Foreground = System.Windows.Media.Brushes.Gray;
                    }

                    _msgRateIndicator.Text = string.Format("{0:0}/с", msgsPerSec);
                    _msgRateIndicator.Foreground =
                        msgsPerSec < 300 ? System.Windows.Media.Brushes.Gray :
                        msgsPerSec < 1000 ? System.Windows.Media.Brushes.Orange :
                        System.Windows.Media.Brushes.Red;
                }
            }

            _perfSnapshot = current;
            _perfMsgSnapshot = currentMsgs;
            if (_perfIntervalSw == null)
                _perfIntervalSw = System.Diagnostics.Stopwatch.StartNew();
            else
                _perfIntervalSw.Restart();
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CopyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(
            string lpszFile,
            int nIconIndex,
            IntPtr[] phiconLarge,
            IntPtr[] phiconSmall,
            uint nIcons);

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            try
            {
                Icon = new BitmapImage(new Uri("pack://application:,,,/icon.ico", UriKind.Absolute));
            }
            catch
            {
            }

            StartUiLatencyMonitor();
            StartDispatcherOpTracing();
            StartServerPingMonitor();

            //Load all types for deserialization
            var types = new List<Type>
                            {
                                typeof(SendTextAction),
                                typeof(OutputToMainWindowAction),
                                typeof(ClearVariableValueAction),
                                typeof(ConditionalAction),
                                typeof(DisableGroupAction),
                                typeof(EnableGroupAction),
                                typeof(SetVariableValueAction),
                                typeof(StartLogAction),
                                typeof(StopLogAction),
                                typeof(TriggerOrCommandParameter),
                                typeof(VariableReferenceParameter),
                                typeof(MathExpressionParameter),
                                typeof(ConstantStringParameter),
                                typeof(ShowOutputWindowAction),
                                typeof(SendToWindowAction),
                                typeof(ToggleFullScreenModeAction),
                            };

            //Load plugins
            foreach (var plugin in PluginHost.Instance.AllPlugins)
            {
                foreach (var customType in plugin.CustomSerializationTypes)
                {
                    types.Add(customType);
                }
            }
            // placing it HERE and not on line 79, because I'm afraid changing the order might break something.
            // if I place it on line 79, then types from Plugins will be offset by 1
            types.Add(typeof(StatusAction));

            //Load settings
            Properties.Settings settings = Properties.Settings.Default;
            settings.Reload();

            SettingsHolder.Instance.Initialize((SettingsFolder)settings.SettingsFolder, types);
            SettingsHolder.Instance.ErrorOccurred += HandleSettingsError;

            var actionDescriptions = new List<ActionDescription>();
            var parameterDescriptions = new List<ParameterDescription>();

            actionDescriptions.Add(new SendTextActionDescription(parameterDescriptions, actionDescriptions));
            actionDescriptions.Add(new OutputToMainWindowActionDescription(parameterDescriptions, actionDescriptions));
            actionDescriptions.Add(new ClearVariableValueActionDescription(actionDescriptions));
            actionDescriptions.Add(new ConditionalActionDescription(parameterDescriptions, actionDescriptions));
            actionDescriptions.Add(new DisableGroupActionDescription(actionDescriptions));
            actionDescriptions.Add(new EnableGroupActionDescription(actionDescriptions));
            actionDescriptions.Add(new SetVariableValueActionDescription(actionDescriptions, parameterDescriptions));
            actionDescriptions.Add(new StartLogActionDescription(actionDescriptions, parameterDescriptions));
            actionDescriptions.Add(new StopLogActionDescription(actionDescriptions));
            actionDescriptions.Add(new ShowOutputWindowActionDescription(actionDescriptions));
            actionDescriptions.Add(new SendToWindowActionDescription(actionDescriptions));
            actionDescriptions.Add(new ToggleFullScreenModeActionDescription(actionDescriptions));
            actionDescriptions.Add(new StatusActionDescription(parameterDescriptions, actionDescriptions));
            actionDescriptions.Add(new LuaScriptActionDescription(actionDescriptions));

            parameterDescriptions.Add(new TriggerOrCommandParameterDescription(parameterDescriptions));
            parameterDescriptions.Add(new VariableReferenceParameterDescription(parameterDescriptions));
            parameterDescriptions.Add(new MathExpressionParameterDescription(parameterDescriptions));
            parameterDescriptions.Add(new ConstantStringParameterDescription(parameterDescriptions));

            RootModel.AllActionDescriptions = actionDescriptions;
            RootModel.AllParameterDescriptions = parameterDescriptions;

            //Initialize themes and add their to menu
            foreach (var themeDescription in ThemeManager.Instance.AvailableThemes)
            {
                var menuItem = new MenuItem
                {
                    Header = themeDescription.Name,
                    Tag = themeDescription,
                    IsChecked = themeDescription == ThemeManager.Instance.ActiveTheme
                };
                menuItem.Click += HandleThemeChange;
                _themesMenuItem.Items.Add(menuItem);
            }

            var initializationDalog = new PluginInitializationStatusDialog
            {
                ViewModel = new InitializationStatusModel()
            };

            Task task = Task.Factory.StartNew(() => PluginHost.Instance.InitializePlugins(initializationDalog.ViewModel, this))
                .ContinueWith(t => Dispatcher.Invoke(initializationDalog.Close));
            initializationDalog.ShowDialog();

            //Initialize plugins
            foreach (var plugin in PluginHost.Instance.Plugins)
            {
                if (plugin.HasOptions)
                {
                    var menuItem = new MenuItem { Header = plugin.OptionsMenuItemText };

                    var pluginClosure = plugin;
                    menuItem.Click += (o, e) => pluginClosure.ShowOptionsDialog(this);
                    _optionsMenuItem.Items.Insert(0, menuItem);

                    if (_optionsSeparator.Visibility != Visibility.Visible)
                        _optionsSeparator.Visibility = Visibility.Visible;
                }
            }

            //Initialize window's position
            Top = SettingsHolder.Instance.Settings.MainWindowTop;
            Left = SettingsHolder.Instance.Settings.MainWindowLeft;
            Width = SettingsHolder.Instance.Settings.MainWindowWidth;
            Height = SettingsHolder.Instance.Settings.MainWindowHeight;
            WindowState = SettingsHolder.Instance.Settings.MainWindowState;

            _dockManager.ActiveContentChanged += _dockManager_ActiveContentChanged;
            _dockManager.Theme = new AvalonDockDarkTheme();
        }

        #endregion

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyTaskbarIcons();
        }

        private void ApplyTaskbarIcons()
        {
            try
            {
                var executablePath = Assembly.GetExecutingAssembly().Location;
                var largeIcons = new IntPtr[1];
                var smallIcons = new IntPtr[1];
                var extracted = ExtractIconEx(executablePath, 0, largeIcons, smallIcons, 1);
                if (extracted > 0)
                {
                    _largeIconHandle = largeIcons[0];
                    _smallIconHandle = smallIcons[0];
                }

                if (_largeIconHandle == IntPtr.Zero || _smallIconHandle == IntPtr.Zero)
                {
                    using (var associatedIcon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath))
                    {
                        if (associatedIcon == null)
                        {
                            return;
                        }

                        using (var smallIcon = new System.Drawing.Icon(associatedIcon, new System.Drawing.Size(16, 16)))
                        using (var largeIcon = new System.Drawing.Icon(associatedIcon, new System.Drawing.Size(32, 32)))
                        {
                            if (_smallIconHandle == IntPtr.Zero)
                            {
                                _smallIconHandle = CopyIcon(smallIcon.Handle);
                            }

                            if (_largeIconHandle == IntPtr.Zero)
                            {
                                _largeIconHandle = CopyIcon(largeIcon.Handle);
                            }
                        }
                    }
                }

                var windowHandle = new WindowInteropHelper(this).Handle;
                if (windowHandle == IntPtr.Zero)
                {
                    return;
                }

                if (_smallIconHandle != IntPtr.Zero)
                {
                    SendMessage(windowHandle, WM_SETICON, new IntPtr(ICON_SMALL), _smallIconHandle);
                }

                if (_largeIconHandle != IntPtr.Zero)
                {
                    SendMessage(windowHandle, WM_SETICON, new IntPtr(ICON_BIG), _largeIconHandle);
                }
            }
            catch
            {
            }
        }

        private void ReleaseTaskbarIcons()
        {
            if (_smallIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_smallIconHandle);
                _smallIconHandle = IntPtr.Zero;
            }

            if (_largeIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_largeIconHandle);
                _largeIconHandle = IntPtr.Zero;
            }
        }

        private void HandleSettingsError(object sender, SettingsErrorEventArgs e)
        {
            var activeContent = _dockManager.ActiveContent as MainOutputWindow;
            if (activeContent != null)
            {
                var outputWindow = _outputWindows.FirstOrDefault(x => { return x.Uid == activeContent.RootModel.Uid; });

                if (outputWindow != null)
                {
                    outputWindow.RootModel.PushMessageToConveyor(new ErrorMessage(e.Message));
                }
            }
            else if (_outputWindows.Count > 0)
            {
                _outputWindows.First().RootModel.PushMessageToConveyor(new ErrorMessage(e.Message));
            }
        }


        #region Layouts

        private void HandleThemeChange([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            var newTheme = (ThemeDescription)((MenuItem)sender).Tag;
            ThemeManager.Instance.SwitchToTheme(newTheme);
            PluginHost.Instance.ApplyAdditionalPluginMergeDictionaries();

            foreach (MenuItem item in _themesMenuItem.Items)
            {
                item.IsChecked = item.Tag == ThemeManager.Instance.ActiveTheme;
            }
        }

        private void HandleDockManagerLoaded([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            foreach (var widget in PluginHost.Instance.Widgets)
            {
                _allWidgets.Add(CreateWidget(widget));
            }

            LoadLayout();

            if (_outputWindows.Count == 0)
            {
                CreateOutputWindow("Default", "a" + Guid.NewGuid().ToString("N"));
            }

            _dockManager.ActiveContent = _outputWindows.FirstOrDefault().VisibleControl;
        }

        private void LoadLayout()
        {
            var layoutFullPath = Path.Combine(SettingsHolder.Instance.Folder, "Settings", "Layout.xml");
            if (File.Exists(layoutFullPath))
            {
                try
                {
                    var serializer = new XmlLayoutSerializer(_dockManager);
                    serializer.LayoutSerializationCallback += LayoutSerializationCallback;
                    serializer.Deserialize(layoutFullPath);
                }
                catch (Exception ex)
                {
                    ErrorLogger.Instance.Write(string.Format("Error loading layout: {0}", ex));
                }
            }

            var widgetLayoutFullPath = Path.Combine(SettingsHolder.Instance.Folder, "Settings", "WidgetLayout.xml");
            if (File.Exists(widgetLayoutFullPath))
            {
                try
                {
                    using (var stream = File.OpenRead(widgetLayoutFullPath))
                    {

                        var serializer = new XmlSerializer(typeof(WidgetLayout));
                        var widgetLayout = (WidgetLayout)serializer.Deserialize(stream);
                        foreach (var widgetLayoutItem in widgetLayout.Widgets)
                        {
                            var widgetWindow = _allWidgets.FirstOrDefault(w => w.Tag != null && w.Tag is WidgetDescription && ((WidgetDescription)w.Tag).Name == widgetLayoutItem.WidgetName);
                            if (widgetWindow != null)
                            {
                                var widgetDescription = (WidgetDescription)widgetWindow.Tag;
                                widgetWindow.Visibility = widgetLayoutItem.Visible ? Visibility.Visible : Visibility.Collapsed;
                                widgetWindow.Left = widgetLayoutItem.Left;
                                widgetWindow.Top = widgetLayoutItem.Top;
                                if (!widgetDescription.ResizeToContent)
                                {
                                    widgetWindow.Height = widgetLayoutItem.Height;
                                    widgetWindow.Width = widgetLayoutItem.Width;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorLogger.Instance.Write(string.Format("Error loading widget layout: {0}", ex));
                }
            }
        }

        private void LayoutSerializationCallback(object sender, LayoutSerializationCallbackEventArgs args)
        {
            if (args.Model.ContentId.StartsWith("Plugin"))
            {
                args.Cancel = true;
            }
            else
            {
                var outputWindow = new OutputWindow(this, args.Model.Title, args.Model.ContentId, _allRootModels)
                {
                    Uid = args.Model.ContentId,
                    DockContent = args.Model
                };

                _outputWindows.Add(outputWindow);
                args.Content = outputWindow.VisibleControl;

                args.Model.Closed += OnOutputWindowClosed;

                var menuItem = new MenuItem
                {
                    Header = outputWindow.Name,
                    Name = outputWindow.Uid,
                };

                menuItem.Click += (s, e) => { ShowOutputWindow((string)((MenuItem)s).Header); };

                if (_windowMenuItem.Items.Count > 0)
                    _windowMenuItem.Items.Insert(0, menuItem);
                else
                    _windowMenuItem.Items.Add(menuItem);

                if (_windowSeparator.Visibility != Visibility.Visible)
                    _windowSeparator.Visibility = Visibility.Visible;

                PluginHost.Instance.OutputWindowCreated(outputWindow.RootModel);

                if (SettingsHolder.Instance.Settings.AutoConnect)
                {
                    outputWindow.RootModel.PushCommandToConveyor(
                        new ConnectCommand(SettingsHolder.Instance.Settings.ConnectHostName, SettingsHolder.Instance.Settings.ConnectPort));
                }
            }
        }

        private Window CreateWidget(WidgetDescription widgetDescription)
        {
            Window widgedWindow;
            if (widgetDescription.ResizeToContent)
            {
                widgedWindow = new AutoSizableWidgetWindow
                {
                    Title = widgetDescription.Description,
                    Left = widgetDescription.Left,
                    Top = widgetDescription.Top,
                    Content = widgetDescription.Control,
                    Tag = widgetDescription,
                };
            }
            else
            {
                widgedWindow = new WidgetWindow
                {
                    Height = widgetDescription.Height,
                    Width = widgetDescription.Width,
                    Title = widgetDescription.Description,
                    Left = widgetDescription.Left,
                    Top = widgetDescription.Top,
                    Content = widgetDescription.Control,
                    Tag = widgetDescription,
                };
            }

            widgedWindow.Owner = this;
            var menuItem = new MenuItem()
            {
                Header = widgetDescription.Description,
                Tag = widgedWindow,
            };

            var visibleBinding = new Binding("IsVisible")
            {
                Source = widgedWindow,
                Mode = BindingMode.OneWay,
            };

            menuItem.SetBinding(MenuItem.IsCheckedProperty, visibleBinding);
            menuItem.Click += HandleHideShowWidget;
            _viewMenuItem.Items.Add(menuItem);

            widgedWindow.Show();
            return widgedWindow;
        }

        private void CreateOutputWindow(string name, string uid)
        {
            OutputWindow outputWindow = new OutputWindow(this, name, uid, _allRootModels);
            _outputWindows.Add(outputWindow);
            outputWindow.Uid = uid;

            LayoutAnchorable anchorable = new LayoutAnchorable
            {
                CanAutoHide = false,
                CanClose = true,
                CanFloat = true,
                CanHide = false,
                Title = name,
                Content = outputWindow.VisibleControl,
                ContentId = uid,
                FloatingHeight = 600,
                FloatingWidth = 800,
            };

            outputWindow.DockContent = anchorable;

            anchorable.Closed += OnOutputWindowClosed;

            var menuItem = new MenuItem
            {
                Header = outputWindow.Name,
                Name = outputWindow.Uid,
            };

            menuItem.Click += (s, e) => { ShowOutputWindow((string)((MenuItem)s).Header); };

            if (_windowMenuItem.Items.Count > 0)
                _windowMenuItem.Items.Insert(0, menuItem);
            else
                _windowMenuItem.Items.Add(menuItem);

            if (_windowSeparator.Visibility != Visibility.Visible)
                _windowSeparator.Visibility = Visibility.Visible;

            PluginHost.Instance.OutputWindowCreated(outputWindow.RootModel);

            var firstDocumentPane = _dockManager.Layout.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();
            if (firstDocumentPane != null)
            {
                firstDocumentPane.Children.Add(anchorable);
            }

            if (SettingsHolder.Instance.Settings.AutoConnect)
            {
                outputWindow.RootModel.PushCommandToConveyor(
                    new ConnectCommand(SettingsHolder.Instance.Settings.ConnectHostName, SettingsHolder.Instance.Settings.ConnectPort));
            }
        }

        #endregion

        #region Windows interaction

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        public void ShowOutputWindow(string name)
        {
            OutputWindow outputWindowToSelect = null;
            if (string.IsNullOrEmpty(name))
            {
                var activeContent = _dockManager.ActiveContent as MainOutputWindow;
                if (activeContent != null)
                {
                    var currentWindow = _outputWindows.FirstOrDefault(x => x.Uid == activeContent.RootModel.Uid);
                    if (currentWindow == null)
                    {
                        return;
                    }

                    var currentWindowIndex = _outputWindows.IndexOf(currentWindow);
                    if (currentWindowIndex < 0)
                    {
                        return;
                    }

                    if (currentWindowIndex == _outputWindows.Count - 1)
                    {
                        currentWindowIndex = 0;
                    }
                    else
                    {
                        currentWindowIndex++;
                    }

                    outputWindowToSelect = _outputWindows[currentWindowIndex];
                }
                else if (_outputWindows.Count > 0)
                {
                    outputWindowToSelect = _outputWindows[0];
                }
            }
            else
            {
                outputWindowToSelect = _outputWindows.FirstOrDefault(x => x.Name == name);
            }

            if (outputWindowToSelect != null)
            {
                _dockManager.ActiveContent = outputWindowToSelect.VisibleControl;
            }
        }

        private void OnOutputWindowClosed(object sender, EventArgs e)
        {
            var dockable = (LayoutAnchorable)sender;
            var outputWindow = _outputWindows.FirstOrDefault(output => output.Uid == dockable.ContentId);

            if (outputWindow != null)
            {
                outputWindow.Save();
                PluginHost.Instance.OutputWindowClose(outputWindow.RootModel);
                outputWindow.Dispose();
                _outputWindows.Remove(outputWindow);

                foreach (var item in _windowMenuItem.Items)
                {
                    var menuItem = item as MenuItem;
                    if (menuItem != null && menuItem.Name == outputWindow.Uid)
                    {
                        _windowMenuItem.Items.Remove(menuItem);
                        break;
                    }
                }

                if (_windowMenuItem.Items.Count <= 2)
                    _windowSeparator.Visibility = Visibility.Collapsed;
            }
        }

        private void HandleAddNewWindow([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            var profiles = new ObservableCollection<ProfileViewModel>();

            foreach (string str in SettingsHolder.Instance.AllProfiles)
            {
                profiles.Add(new ProfileViewModel(str, str == "Default" ? true : false));
            }

            if (profiles.Count == 0)
            {
                MessageBox.Show(this, "Create profile first", "Error");
                return;
            }

            var chooseViewModel = new ProfileChooseViewModel(profiles, profiles[0].NameProfile);

            var chooseDialog = new ProfilesChooseDialog()
            {
                DataContext = chooseViewModel,
                Owner = this,
            };

            var result = chooseDialog.ShowDialog();
            if (result.HasValue && result.Value)
            {
                try
                {
                    var name = chooseViewModel.SelectedProfile.NameProfile;

                    //Name of MenuItem cannot start with number
                    CreateOutputWindow(name, "a" + Guid.NewGuid().ToString("N"));
                }
                catch (Exception ex)
                {
                    ErrorLogger.Instance.Write(string.Format("Error add new window: {0}\r\n{1}", ex.Message, ex.StackTrace));
                }
            }
        }

        private void _dockManager_ActiveContentChanged(object sender, EventArgs e)
        {
            var activeContent = _dockManager.ActiveContent as MainOutputWindow;
            if (activeContent != null)
            {
                var dockContent = _outputWindows.FirstOrDefault(x => { return x.Uid == activeContent.RootModel.Uid; });
                if (dockContent != null)
                {
                    dockContent.Focus();
                }
            }
        }

        #endregion

        #region Hotkeys

        private void HandleGlobalProfile([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            var globalProfileDialog = new ProfileOptionsEditDialog("Global")
            {
                DataContext = new ProfileOptionsViewModel("Global", SettingsHolder.Instance.Settings.GlobalGroups),
                Owner = this,
            };

            globalProfileDialog.Show();
            globalProfileDialog.Closed += (s, a) =>
            {
                SettingsHolder.Instance.SaveCommonSettings();
            };
        }

        #endregion

        #region Buttons

        private void HandleAbout([NotNull]object sender, [NotNull]RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            var aboutDialog = new AboutDialog() { Owner = this, }.ShowDialog();
        }

        private void HandleConnect([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            var activeContent = _dockManager.ActiveContent as MainOutputWindow;
            if (activeContent != null)
            {
                var outputWindow = _outputWindows.FirstOrDefault(x => { return x.Uid == activeContent.RootModel.Uid; });
                if (outputWindow == null)
                {
                    CreateOutputWindow("Default", "a" + Guid.NewGuid().ToString("N"));
                }

                outputWindow.RootModel.PushCommandToConveyor(
                        new ConnectCommand(SettingsHolder.Instance.Settings.ConnectHostName, SettingsHolder.Instance.Settings.ConnectPort));
            }
        }

        private void HandleConnectAll([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            if (_outputWindows.Count == 0)
                CreateOutputWindow("Default", "a" + Guid.NewGuid().ToString("N"));

            foreach (OutputWindow window in _outputWindows)
                window.RootModel.PushCommandToConveyor(new ConnectCommand(SettingsHolder.Instance.Settings.ConnectHostName, SettingsHolder.Instance.Settings.ConnectPort));
        }

        private void HandleDisconnect([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");
            var activeContent = _dockManager.ActiveContent as MainOutputWindow;
            if (activeContent != null)
            {
                var outputWindow = _outputWindows.FirstOrDefault(x => { return x.Uid == activeContent.RootModel.Uid; });

                if (outputWindow != null)
                    outputWindow.RootModel.PushCommandToConveyor(new DisconnectCommand());
            }
        }

        private void HandleDisconnectAll([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            foreach (OutputWindow window in _outputWindows)
                window.RootModel.PushCommandToConveyor(new DisconnectCommand());
        }

        private void HandleConnectionPreference([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            var connectionDialogViewModel = new ConnectionDialogViewModel
            {
                HostName = SettingsHolder.Instance.Settings.ConnectHostName,
                Port = SettingsHolder.Instance.Settings.ConnectPort,
            };

            var dialog = new ConnectionDialog { DataContext = connectionDialogViewModel, Owner = this };

            var result = dialog.ShowDialog();
            if (result.HasValue && result.Value)
            {
                SettingsHolder.Instance.Settings.ConnectHostName = connectionDialogViewModel.HostName;
                SettingsHolder.Instance.Settings.ConnectPort = connectionDialogViewModel.Port;
            }
        }

        private void HandleExit([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            Close();
        }

        private void HandleHideShowWidget([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            var dockContent = (Window)((MenuItem)sender).Tag;
            if (dockContent.Visibility == Visibility.Visible)
            {
                dockContent.Hide();
            }
            else
            {
                dockContent.Show();
            }
        }

        private void HandleEditProfiles([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            var models = new ObservableCollection<ProfileViewModel>();

            foreach (string str in SettingsHolder.Instance.AllProfiles)
            {
                models.Add(new ProfileViewModel(str, str == "Default" ? true : false));
            }

            var profilesViewModel = new ProfilesEditViewModel(models, models[0].NameProfile);
            var profileDialog = new ProfilesEditDialog() { DataContext = profilesViewModel, Owner = this };

            profileDialog.Show();
        }

        private void HandleEditOptions([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            var model = new OptionsViewModel()
            {
                AutoClearInput = SettingsHolder.Instance.Settings.AutoClearInput,
                AutoReconnect = SettingsHolder.Instance.Settings.AutoReconnect,
                CommandChar = SettingsHolder.Instance.Settings.CommandChar,
                CommandDelimiter = SettingsHolder.Instance.Settings.CommandDelimiter,
                StartOfLine = SettingsHolder.Instance.Settings.CursorPosition == CursorPositionHistory.StartOfLine,
                EndOfLine = SettingsHolder.Instance.Settings.CursorPosition == CursorPositionHistory.EndOfLine,
                HistorySize = SettingsHolder.Instance.Settings.CommandsHistorySize.ToString(),
                MinLengthHistory = SettingsHolder.Instance.Settings.MinLengthHistory.ToString(),
                ScrollBuffer = SettingsHolder.Instance.Settings.ScrollBuffer.ToString(),
                SettingsFolder = SettingsHolder.Instance.SettingsFolder == SettingsFolder.DocumentsAndSettings,
                AutoConnect = SettingsHolder.Instance.Settings.AutoConnect,
                SelectedFont = SettingsHolder.Instance.Settings.MUDFontName,
                SelectedFontSize = SettingsHolder.Instance.Settings.MUDFontSize,
                SelectedFontWeight = SettingsHolder.Instance.Settings.MudFontWeight,
            };

            var optionsDialog = new OptionsDialog() { DataContext = model, Owner = this };
            var dialogResult = optionsDialog.ShowDialog();

            if (dialogResult.HasValue && dialogResult.Value)
            {
                SettingsHolder.Instance.Settings.AutoClearInput = model.AutoClearInput;
                SettingsHolder.Instance.Settings.AutoReconnect = model.AutoReconnect;
                SettingsHolder.Instance.Settings.CommandChar = model.CommandChar;
                SettingsHolder.Instance.Settings.CommandDelimiter = model.CommandDelimiter;
                SettingsHolder.Instance.Settings.AutoConnect = model.AutoConnect;
                SettingsHolder.Instance.Settings.ColorTheme = model.SelectedTheme.Name;
                ThemeManager.Instance.ActiveTheme = model.SelectedTheme;
                SettingsHolder.Instance.Settings.MUDFontName = model.SelectedFont;
                SettingsHolder.Instance.Settings.MUDFontSize = model.SelectedFontSize;
                SettingsHolder.Instance.Settings.MudFontWeight = model.SelectedFontWeight;

                if (model.StartOfLine)
                    SettingsHolder.Instance.Settings.CursorPosition = CursorPositionHistory.StartOfLine;
                else
                    SettingsHolder.Instance.Settings.CursorPosition = CursorPositionHistory.EndOfLine;

                if (model.SettingsFolder)
                    SettingsHolder.Instance.SettingsFolder = SettingsFolder.DocumentsAndSettings;
                else
                    SettingsHolder.Instance.SettingsFolder = SettingsFolder.ProgramFolder;

                int val;
                if (int.TryParse(model.HistorySize, out val))
                    SettingsHolder.Instance.Settings.CommandsHistorySize = val;

                if (int.TryParse(model.MinLengthHistory, out val))
                    SettingsHolder.Instance.Settings.MinLengthHistory = val;

                if (int.TryParse(model.ScrollBuffer, out val))
                    SettingsHolder.Instance.Settings.ScrollBuffer = val < 100000 ? val : 100000;

                SettingsHolder.Instance.SaveCommonSettings();
            }
        }

        #endregion

        /// <summary>
        /// Toggles the full screen mode.
        /// </summary>
        public void ToggleFullScreenMode()
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = _nonFullScreenWindowState;
                _mainMenu.Visibility = Visibility.Visible;
            }
            else
            {
                _nonFullScreenWindowState = WindowState;
                WindowState = System.Windows.WindowState.Normal;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                _mainMenu.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Raises the <see cref="E:System.Windows.Window.Closing"/> event.
        /// </summary>
        /// <param name="e">A <see cref="T:System.ComponentModel.CancelEventArgs"/> that contains the event data.</param>
        protected override void OnClosing([NotNull] CancelEventArgs e)
        {
            Assert.ArgumentNotNull(e, "e");

            SaveAllSettings();
            PluginHost.Instance.Dispose();
            ReleaseTaskbarIcons();
            base.OnClosing(e);
        }

        public void SaveAllSettings()
        {
            try
            {
                if (WindowStyle == WindowStyle.None)
                {
                    ToggleFullScreenMode();
                }

                var layoutFullPath = Path.Combine(SettingsHolder.Instance.Folder, "Settings");
                if (!Directory.Exists(layoutFullPath))
                {
                    Directory.CreateDirectory(layoutFullPath);
                }

                layoutFullPath = Path.Combine(layoutFullPath, "Layout.xml");
                if (!File.Exists(layoutFullPath))
                    File.Delete(layoutFullPath);

                new XmlLayoutSerializer(_dockManager).Serialize(layoutFullPath);
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Write(string.Format("Error save layout:{0}\r\n{1}", ex.Message, ex.StackTrace));
            }

            var widgetLayoutFullPath = Path.Combine(SettingsHolder.Instance.Folder, "Settings", "WidgetLayout.xml");
            try
            {
                var widgetLayout = new WidgetLayout { Widgets = new List<WidgetLayoutItem>() };
                foreach (var widget in _allWidgets)
                {
                    var widgetDescription = (WidgetDescription)widget.Tag;
                    var widgetLayoutItem = new WidgetLayoutItem
                    {
                        WidgetName = widgetDescription.Name,
                        Top = widget.Top,
                        Left = widget.Left,
                        Height = widget.Height,
                        Width = widget.Width,
                        Visible = widget.Visibility == Visibility.Visible,
                    };
                    widgetLayout.Widgets.Add(widgetLayoutItem);
                }
                using (var stream = File.Open(widgetLayoutFullPath, FileMode.Create))
                {
                    var serializer = new XmlSerializer(typeof(WidgetLayout));
                    serializer.Serialize(stream, widgetLayout);
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Write(string.Format("Error saving widget layout: {0}", ex));
            }
            try
            {
                foreach (var outputWindow in _outputWindows)
                {
                    outputWindow.Save();
                }

                SettingsHolder.Instance.Settings.MainWindowTop = (int)Top;
                SettingsHolder.Instance.Settings.MainWindowLeft = (int)Left;
                SettingsHolder.Instance.Settings.MainWindowWidth = (int)Width;
                SettingsHolder.Instance.Settings.MainWindowHeight = (int)Height;
                SettingsHolder.Instance.Settings.MainWindowState = WindowState;

                SettingsHolder.Instance.SaveAllSettings();
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Write(string.Format("Error save settings:{0}\r\n{1}", ex.Message, ex.StackTrace));
            }
        }
    }
}


