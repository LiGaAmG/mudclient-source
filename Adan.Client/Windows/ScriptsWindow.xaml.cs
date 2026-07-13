using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Adan.Client.Common.Scripting;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Adan.Client.Windows
{
    public class TabEntry : INotifyPropertyChanged
    {
        private bool _isEnabled;
        public string Uid { get; set; }
        public string Name { get; set; }
        public LuaScriptHost ScriptHost { get; set; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsEnabled")); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class ScriptListItem : INotifyPropertyChanged
    {
        private ScriptRunStatus _status = ScriptRunStatus.NotRunning;
        public string FileName { get; set; }
        public ScriptFileEntry Entry { get; set; }

        public ScriptRunStatus Status
        {
            get => _status;
            set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Status")); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("StatusColor")); }
        }

        public Brush StatusColor
        {
            get
            {
                switch (_status)
                {
                    case ScriptRunStatus.Running:
                    case ScriptRunStatus.WaitingOnText:
                    case ScriptRunStatus.WaitingOnTimer:
                    case ScriptRunStatus.WaitingOnGroupState:
                    case ScriptRunStatus.WaitingOnRoomState:
                    case ScriptRunStatus.WaitingOnRoomChange:
                        return Brushes.LimeGreen;
                    case ScriptRunStatus.Faulted:
                        return Brushes.Red;
                    case ScriptRunStatus.Finished:
                        return Brushes.Gray;
                    default:
                        return new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                }
            }
        }

        public Visibility SharedVisible => Entry?.IsShared == true ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler PropertyChanged;
        public void Notify(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class ScriptsWindow : Window
    {
        private readonly ScriptFileManager _manager;
        private readonly List<TabEntry> _allTabs;
        private readonly ObservableCollection<ScriptListItem> _items = new ObservableCollection<ScriptListItem>();
        private readonly System.Windows.Threading.DispatcherTimer _statusTimer;
        private ScriptListItem _currentItem;
        private bool _suppressEvents;

        public ScriptsWindow(ScriptFileManager manager, IEnumerable<(string Uid, string Name, LuaScriptHost Host)> tabs)
        {
            InitializeComponent();
            _manager = manager;
            _allTabs = tabs.Select(t => new TabEntry { Uid = t.Uid, Name = t.Name, ScriptHost = t.Host }).ToList();

            TabCheckList.ItemsSource = _allTabs;
            ScriptList.ItemsSource = _items;
            FolderLabel.Text = _manager.Folder;

            _manager.ScriptsChanged += OnScriptsChanged;

            CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (s, e) => HandleSaveClick(s, null)));

            _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _statusTimer.Tick += (s, e) => RefreshStatuses();
            _statusTimer.Start();

            Closed += (s, e) => { _statusTimer.Stop(); _manager.ScriptsChanged -= OnScriptsChanged; };

            RefreshList();
        }

        private void OnScriptsChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RefreshList));
        }

        private void RefreshList()
        {
            var selected = _currentItem?.FileName;
            _items.Clear();
            foreach (var entry in _manager.Entries)
                _items.Add(new ScriptListItem { FileName = entry.FileName, Entry = entry });

            if (selected != null)
            {
                var item = _items.FirstOrDefault(i => i.FileName == selected);
                if (item != null) ScriptList.SelectedItem = item;
            }

            RefreshStatuses();
        }

        private void RefreshStatuses()
        {
            foreach (var item in _items)
            {
                // Aggregate status across all tabs that have this script
                var status = ScriptRunStatus.NotRunning;
                foreach (var tab in _allTabs)
                {
                    var s = tab.ScriptHost.GetScriptStatus(item.FileName);
                    if (s == ScriptRunStatus.Running || s == ScriptRunStatus.WaitingOnText ||
                        s == ScriptRunStatus.WaitingOnTimer || s == ScriptRunStatus.WaitingOnGroupState ||
                        s == ScriptRunStatus.WaitingOnRoomState || s == ScriptRunStatus.WaitingOnRoomChange)
                    { status = s; break; }
                    if (s == ScriptRunStatus.Faulted) status = s;
                    else if (status == ScriptRunStatus.NotRunning && s == ScriptRunStatus.Finished)
                        status = ScriptRunStatus.Finished;
                }
                item.Status = status;
            }
        }

        private void HandleScriptSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _currentItem = ScriptList.SelectedItem as ScriptListItem;
            LoadScriptIntoEditor(_currentItem);
        }

        private void LoadScriptIntoEditor(ScriptListItem item)
        {
            _suppressEvents = true;
            try
            {
                if (item == null)
                {
                    CodeEditor.Text = string.Empty;
                    EditorFileLabel.Text = string.Empty;
                    SharedCheck.IsChecked = false;
                    AutoStartCheck.IsChecked = false;
                    foreach (var t in _allTabs) t.IsEnabled = false;
                    return;
                }

                CodeEditor.Text = _manager.ReadCode(item.FileName);
                EditorFileLabel.Text = _manager.GetFilePath(item.FileName);

                SharedCheck.IsChecked = item.Entry.IsShared;
                AutoStartCheck.IsChecked = item.Entry.AutoStart;

                // Update tab checkboxes
                foreach (var t in _allTabs)
                    t.IsEnabled = item.Entry.IsShared || item.Entry.EnabledTabUids.Contains(t.Uid);
            }
            finally { _suppressEvents = false; }
        }

        private void HandleSaveClick(object sender, RoutedEventArgs e)
        {
            if (_currentItem == null) return;
            _manager.WriteCode(_currentItem.FileName, CodeEditor.Text);
        }

        private void HandleNewClick(object sender, RoutedEventArgs e)
        {
            var dialog = new NewScriptNameDialog { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var name = dialog.ScriptName?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            _manager.CreateScript(name);
            RefreshList();
            var item = _items.FirstOrDefault(i => i.FileName.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            if (item != null) ScriptList.SelectedItem = item;
        }

        private void HandleDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_currentItem == null) return;
            var result = MessageBox.Show($"Удалить {_currentItem.FileName}?", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            foreach (var tab in _allTabs)
                tab.ScriptHost.StopScript(_currentItem.FileName);

            _manager.DeleteScript(_currentItem.FileName);
            RefreshList();
        }

        private void HandleReloadFolderClick(object sender, RoutedEventArgs e)
        {
            _manager.Reload();
            RefreshList();
        }

        private void HandleSharedChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents || _currentItem == null) return;
            _currentItem.Entry.IsShared = SharedCheck.IsChecked == true;
            foreach (var t in _allTabs) t.IsEnabled = _currentItem.Entry.IsShared;
            _currentItem.Notify("SharedVisible");
            _manager.SaveMetadata();
        }

        private void HandleAutoStartChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents || _currentItem == null) return;
            _currentItem.Entry.AutoStart = AutoStartCheck.IsChecked == true;
            _manager.SaveMetadata();
        }

        private void HandleTabEnableChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents || _currentItem == null) return;
            if (_currentItem.Entry.IsShared) return;
            var check = (System.Windows.Controls.CheckBox)sender;
            var uid = (string)check.Tag;
            if (check.IsChecked == true)
            { if (!_currentItem.Entry.EnabledTabUids.Contains(uid)) _currentItem.Entry.EnabledTabUids.Add(uid); }
            else
            { _currentItem.Entry.EnabledTabUids.Remove(uid); }
            _manager.SaveMetadata();
        }

        private void HandleAutoreloadChanged(object sender, RoutedEventArgs e)
        {
            // FileSystemWatcher is always active; autoreload here means
            // restart the running script on file change
            // TODO: wire autoreload per-script
        }

        private void HandleStartAllClick(object sender, RoutedEventArgs e)
        {
            if (_currentItem == null) return;
            HandleSaveClick(null, null);
            var code = _manager.ReadCode(_currentItem.FileName);
            foreach (var tab in GetTargetTabs(_currentItem))
                tab.ScriptHost.StartScript(_currentItem.FileName, code);
        }

        private void HandleStopAllClick(object sender, RoutedEventArgs e)
        {
            if (_currentItem == null) return;
            foreach (var tab in _allTabs)
                tab.ScriptHost.StopScript(_currentItem.FileName);
        }

        private IEnumerable<TabEntry> GetTargetTabs(ScriptListItem item)
        {
            if (item.Entry.IsShared) return _allTabs;
            return _allTabs.Where(t => item.Entry.EnabledTabUids.Contains(t.Uid));
        }
    }
}
