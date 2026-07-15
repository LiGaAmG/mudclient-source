using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Adan.Client.Common.Model;
using Adan.Client.Common.Scripting;
using Adan.Client.Common.Settings;

namespace Adan.Client.Windows
{
    public class ScriptsForm : Form
    {
        private readonly ScriptFileManager _manager;
        private readonly IList<RootModel> _rootModels;
        private ListBox _scriptList;
        private RichTextBox _editor;
        private RichTextBox _lineNumbers;
        private CheckBox _sharedCheck, _autoStartCheck;
        private CheckedListBox _profileList;
        private Label _fileLabel;
        private RichTextBox _log;
        private System.Windows.Forms.Timer _refreshTimer;
        private ScriptFileEntry _currentEntry;
        private bool _suppressEvents;
        private bool _editorDirty;
        private readonly Dictionary<string, string> _drafts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dirtyFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ScriptDraftHistory> _draftHistories = new Dictionary<string, ScriptDraftHistory>(StringComparer.OrdinalIgnoreCase);
        private bool _isApplyingDraftHistory;
        private bool _isHighlighting;
        private bool _autoReload = true;
        private ScriptHelpForm _helpForm;
        private int _lineNumbersFirstVisibleLine;

        private const int EmGetFirstVisibleLine = 0x00CE;
        private const int EmLineScroll = 0x00B6;
        private const int EmStopGroupTyping = 0x0458;

        private enum ToolbarIcon { New, Delete, Save, SaveAll, Refresh, Start, Stop, AutoReload, Clear, Help }

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int message, int wParam, int lParam);

        public ScriptsForm(ScriptFileManager manager, IList<RootModel> rootModels)
        {
            _manager = manager;
            _rootModels = rootModels;
            BuildUI();
            SubscribeScriptEvents();
            _manager.ScriptsChanged += OnScriptsChanged;
            _manager.ScriptFileChanged += OnExternalScriptChanged;
            FormClosing += OnFormClosing;
            FormClosed += (s, e) => { _manager.ScriptsChanged -= OnScriptsChanged; _manager.ScriptFileChanged -= OnExternalScriptChanged; UnsubscribeScriptEvents(); _refreshTimer.Stop(); _manager.Dispose(); };
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _refreshTimer.Tick += (s, e) => RebuildProfileList();
            _refreshTimer.Start();
            RefreshList();
        }

        private void SubscribeScriptEvents() { foreach (var m in _rootModels) m.ScriptHost.ScriptEvent += OnScriptEvent; }
        private void UnsubscribeScriptEvents() { foreach (var m in _rootModels) m.ScriptHost.ScriptEvent -= OnScriptEvent; }

        private void OnScriptEvent(string name, LuaScriptHost.ScriptEventType type, string error)
        {
            if (_log == null || _log.IsDisposed) return;
            Color color; string prefix;
            switch (type)
            {
                case LuaScriptHost.ScriptEventType.Started:  color = Color.LimeGreen;  prefix = "START "; break;
                case LuaScriptHost.ScriptEventType.Stopped:  color = Color.Gray;       prefix = "STOP  "; break;
                case LuaScriptHost.ScriptEventType.Finished: color = Color.DodgerBlue; prefix = "DONE  "; break;
                case LuaScriptHost.ScriptEventType.Faulted:  color = Color.OrangeRed;  prefix = "ERROR "; break;
                default:                                      color = Color.White;      prefix = "      "; break;
            }
            var msg = string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, prefix, name);
            if (!string.IsNullOrEmpty(error)) msg += "\r\n  " + error;
            AppendLog(msg, color);
            RefreshScriptIndicators();
        }

        private void AppendLog(string text, Color color)
        {
            if (_log.InvokeRequired) { _log.Invoke(new Action(() => AppendLog(text, color))); return; }
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color;
            _log.AppendText(text + "\r\n");
            if (_log.Lines.Length > 500) { _log.Select(0, _log.GetFirstCharIndexFromLine(100)); _log.SelectedText = ""; }
            _log.ScrollToCaret();
        }

        private void BuildUI()
        {
            Text = "Scripts";
            Size = new Size(1180, 760);
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterParent;

            var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, ImageScalingSize = new Size(20, 20) };
            var btnNew      = CreateToolbarButton("Новый", ToolbarIcon.New, (s, e) => HandleNew());
            var btnDel      = CreateToolbarButton("Удалить", ToolbarIcon.Delete, (s, e) => HandleDelete());
            var btnSave     = CreateToolbarButton("Сохранить", ToolbarIcon.Save, (s, e) => HandleSave()); btnSave.ToolTipText = "Ctrl+S";
            var btnSaveAll  = CreateToolbarButton("Сохранить всё", ToolbarIcon.SaveAll, (s, e) => HandleSaveAll());
            var btnRefresh  = CreateToolbarButton("Обновить", ToolbarIcon.Refresh, (s, e) => { _manager.Reload(); RefreshList(); });
            var btnStart    = CreateToolbarButton("Старт", ToolbarIcon.Start, (s, e) => HandleStart());
            var btnStop     = CreateToolbarButton("Стоп", ToolbarIcon.Stop, (s, e) => HandleStop());
            var btnAutoReload = CreateToolbarButton("Автообновление", ToolbarIcon.AutoReload, null); btnAutoReload.CheckOnClick = true; btnAutoReload.Checked = true;
            btnAutoReload.CheckedChanged += (s, e) => _autoReload = btnAutoReload.Checked;
            var btnClearLog = CreateToolbarButton("Очистить лог", ToolbarIcon.Clear, (s, e) => _log.Clear());
            var btnHelp     = CreateToolbarButton("Help", ToolbarIcon.Help, (s, e) => ShowHelp());
            toolbar.Items.AddRange(new ToolStripItem[] { btnNew, btnDel, new ToolStripSeparator(), btnSave, btnSaveAll, btnRefresh, new ToolStripSeparator(), btnStart, btnStop, new ToolStripSeparator(), btnAutoReload, new ToolStripSeparator(), btnClearLog, btnHelp });

            var outerSplit = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 5, SplitterDistance = 320 };
            var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4) };
            leftPanel.RowCount = 3;
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 47));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

            var scriptsGroup = new GroupBox { Text = "Скрипты", Dock = DockStyle.Fill };
            _scriptList = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f), IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 30 };
            _scriptList.SelectedIndexChanged += OnScriptSelected;
            _scriptList.DrawItem += DrawScriptListItem;
            _sharedCheck    = new CheckBox { Text = "Глобальный: все профили и новые", Font = new Font("Segoe UI", 9f), Left = 8, Top = 20, AutoSize = true };
            _autoStartCheck = new CheckBox { Text = "Авто-старт при подключении", Font = new Font("Segoe UI", 9f), Left = 8, Top = 43, AutoSize = true };
            _sharedCheck.CheckedChanged    += OnSharedChanged;
            _autoStartCheck.CheckedChanged += OnAutoStartChanged;
            var scriptSettings = new Panel { Dock = DockStyle.Top, Height = 70 };
            scriptSettings.Controls.AddRange(new Control[] { _sharedCheck, _autoStartCheck });
            scriptsGroup.Controls.Add(_scriptList);
            scriptsGroup.Controls.Add(scriptSettings);

            var profilesGroup = new GroupBox { Text = "Назначение профилям", Dock = DockStyle.Fill };
            _profileList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            _profileList.ItemCheck += OnProfileItemCheck;
            profilesGroup.Controls.Add(_profileList);

            var folderGroup = new GroupBox { Text = "Папка скриптов", Dock = DockStyle.Fill };
            var folderLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(5, 3, 5, 5) };
            folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            var folderPath = new TextBox { Text = _manager.Folder, ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9f) };
            var openFolder = new Button { Text = "Открыть", Dock = DockStyle.Fill };
            openFolder.Click += (s, e) => System.Diagnostics.Process.Start("explorer.exe", _manager.Folder);
            folderLayout.Controls.Add(folderPath, 0, 0);
            folderLayout.Controls.Add(openFolder, 1, 0);
            folderGroup.Controls.Add(folderLayout);
            leftPanel.Controls.Add(scriptsGroup, 0, 0);
            leftPanel.Controls.Add(profilesGroup, 0, 1);
            leftPanel.Controls.Add(folderGroup, 0, 2);
            outerSplit.Panel1.Controls.Add(leftPanel);

            var rightLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(4) };
            rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            var editorHeader = new GroupBox { Text = "Редактор", Dock = DockStyle.Fill, Height = 48 };
            _fileLabel = new Label { Left = 8, Top = 20, Width = 760, Height = 18, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right, Font = new Font("Consolas", 8.5f), ForeColor = SystemColors.GrayText };
            editorHeader.Controls.Add(_fileLabel);

            var editorLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _lineNumbers = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                TabStop = false,
                ShortcutsEnabled = false,
                Font = new Font("Consolas", 11f),
                WordWrap = false,
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = RichTextBoxScrollBars.None,
                BackColor = Color.FromArgb(232, 232, 232),
                ForeColor = Color.FromArgb(80, 80, 80),
                Cursor = Cursors.Default,
            };
            _editor = new RichTextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 11f), WordWrap = false, AcceptsTab = true, BorderStyle = BorderStyle.FixedSingle, ScrollBars = RichTextBoxScrollBars.Both };
            _editor.KeyDown += HandleEditorKeyDown;
            // Reformatting a RichTextBox during every keystroke records formatting
            // changes in its native undo buffer and briefly selects all text.
            // Keep typing/Undo fully native; colour existing code when it is loaded.
            _editor.TextChanged += (s, e) =>
            {
                UpdateLineNumbers();
                if (!_suppressEvents && _currentEntry != null)
                {
                    var fileName = _currentEntry.FileName;
                    if (!_isApplyingDraftHistory)
                    {
                        string previous;
                        if (!_drafts.TryGetValue(fileName, out previous)) previous = _manager.ReadCode(fileName);
                        if (!string.Equals(previous, _editor.Text, StringComparison.Ordinal)) GetDraftHistory(fileName).RecordChange(previous);
                    }
                    SetDraftText(fileName, _editor.Text);
                }
                // RichEdit otherwise groups the whole typing session into one Undo.
                if (!_suppressEvents && _editor.IsHandleCreated) SendMessage(_editor.Handle, EmStopGroupTyping, 0, 0);
            };
            _editor.VScroll += (s, e) => SyncLineNumberScroll();
            _editor.MouseWheel += (s, e) => BeginInvoke(new Action(SyncLineNumberScroll));
            _editor.KeyUp += (s, e) => BeginInvoke(new Action(SyncLineNumberScroll));
            _editor.ContextMenuStrip = BuildEditorMenu();
            editorLayout.Controls.Add(_lineNumbers, 0, 0);
            editorLayout.Controls.Add(_editor, 1, 0);
            _log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Window, ForeColor = SystemColors.WindowText, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.FixedSingle, WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both };
            var logGroup = new GroupBox { Text = "Лог скриптов", Dock = DockStyle.Fill };
            logGroup.Controls.Add(_log);
            rightLayout.Controls.Add(editorHeader, 0, 0);
            rightLayout.Controls.Add(editorLayout, 0, 1);
            rightLayout.Controls.Add(logGroup, 0, 2);
            outerSplit.Panel2.Controls.Add(rightLayout);
            Controls.Add(outerSplit);
            Controls.Add(toolbar);

            Load += (s, e) =>
            {
                outerSplit.Panel1MinSize = 280;
                outerSplit.Panel2MinSize = 480;
                outerSplit.SplitterDistance = 320;
            };

            RebuildProfileList();
            KeyPreview = true;
        }

        private static ToolStripButton CreateToolbarButton(string text, ToolbarIcon icon, EventHandler click)
        {
            var button = new ToolStripButton(text)
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                Image = CreateToolbarIcon(icon),
                ImageTransparentColor = Color.Magenta
            };
            if (click != null) button.Click += click;
            return button;
        }

        private static Bitmap CreateToolbarIcon(ToolbarIcon icon)
        {
            var image = new Bitmap(16, 16);
            using (var graphics = Graphics.FromImage(image))
            using (var pen = new Pen(Color.FromArgb(70, 70, 70), 1.4f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Magenta);
                if (icon == ToolbarIcon.Start)
                {
                    using (var brush = new SolidBrush(Color.ForestGreen)) graphics.FillPolygon(brush, new[] { new Point(4, 2), new Point(4, 14), new Point(14, 8) });
                    graphics.DrawPolygon(pen, new[] { new Point(4, 2), new Point(4, 14), new Point(14, 8) });
                }
                else if (icon == ToolbarIcon.Stop || icon == ToolbarIcon.Delete)
                {
                    using (var brush = new SolidBrush(icon == ToolbarIcon.Stop ? Color.IndianRed : Color.OrangeRed)) graphics.FillEllipse(brush, 2, 2, 12, 12);
                    using (var white = new Pen(Color.White, 2)) { graphics.DrawLine(white, 5, 5, 11, 11); graphics.DrawLine(white, 11, 5, 5, 11); }
                }
                else if (icon == ToolbarIcon.Save || icon == ToolbarIcon.SaveAll)
                {
                    using (var brush = new SolidBrush(Color.RoyalBlue)) graphics.FillRectangle(brush, 2, 2, 11, 12);
                    graphics.DrawRectangle(pen, 2, 2, 11, 12);
                    using (var white = new SolidBrush(Color.White)) { graphics.FillRectangle(white, 5, 3, 5, 4); graphics.FillRectangle(white, 4, 9, 7, 4); }
                    if (icon == ToolbarIcon.SaveAll) { using (var accent = new SolidBrush(Color.DodgerBlue)) graphics.FillRectangle(accent, 11, 7, 4, 7); }
                }
                else if (icon == ToolbarIcon.Refresh || icon == ToolbarIcon.AutoReload)
                {
                    using (var brush = new SolidBrush(icon == ToolbarIcon.AutoReload ? Color.ForestGreen : Color.SeaGreen)) graphics.FillPie(brush, 2, 2, 12, 12, 35, 280);
                    graphics.DrawArc(pen, 2, 2, 12, 12, 35, 280);
                    using (var white = new SolidBrush(Color.White)) graphics.FillPolygon(white, new[] { new Point(11, 1), new Point(15, 2), new Point(13, 6) });
                }
                else if (icon == ToolbarIcon.New)
                {
                    using (var brush = new SolidBrush(Color.White)) graphics.FillRectangle(brush, 3, 2, 9, 12);
                    graphics.DrawRectangle(pen, 3, 2, 9, 12);
                    using (var plus = new Pen(Color.ForestGreen, 2)) { graphics.DrawLine(plus, 8, 6, 8, 12); graphics.DrawLine(plus, 5, 9, 11, 9); }
                }
                else if (icon == ToolbarIcon.Clear)
                {
                    using (var brush = new SolidBrush(Color.SandyBrown)) graphics.FillPolygon(brush, new[] { new Point(3, 10), new Point(11, 4), new Point(13, 6), new Point(5, 12) });
                    using (var handle = new Pen(Color.SaddleBrown, 2)) graphics.DrawLine(handle, 10, 3, 14, 7);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.DodgerBlue)) graphics.FillEllipse(brush, 2, 2, 12, 12);
                    using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
                        TextRenderer.DrawText(graphics, "?", font, new Rectangle(2, 1, 12, 13), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            image.MakeTransparent(Color.Magenta);
            return image;
        }

        private void ShowHelp()
        {
            if (_helpForm != null && !_helpForm.IsDisposed)
            {
                _helpForm.Activate();
                return;
            }
            _helpForm = new ScriptHelpForm();
            _helpForm.FormClosed += (s, e) => _helpForm = null;
            _helpForm.Show(this);
        }

        private ContextMenuStrip BuildEditorMenu()
        {
            var menu = new ContextMenuStrip();
            foreach (var category in new[] { "Lua", "Клиент", "Ожидание" })
            {
                var categoryMenu = new ToolStripMenuItem(category);
                foreach (var snippet in ScriptSnippetCatalog.All.Where(s => s.Category == category))
                {
                    var item = new ToolStripMenuItem(snippet.Title);
                    var code = snippet.Code;
                    item.Click += (s, e) => InsertSnippet(code);
                    categoryMenu.DropDownItems.Add(item);
                }
                menu.Items.Add(categoryMenu);
            }
            menu.Items.Add(new ToolStripSeparator());
            var help = new ToolStripMenuItem("Открыть справку по Lua");
            help.Click += (s, e) => ShowHelp();
            menu.Items.Add(help);
            return menu;
        }

        private void InsertSnippet(string code)
        {
            var start = _editor.SelectionStart;
            _editor.SelectedText = code;
            _editor.SelectionStart = start + code.Length;
            _editor.SelectionLength = 0;
            _editor.Focus();
        }

        private void HandleEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Control) return;
            if (e.KeyCode == Keys.S) { HandleSave(); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Z) { UndoCurrentDraft(); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Y) { RedoCurrentDraft(); e.SuppressKeyPress = true; }
        }

        private ScriptDraftHistory GetDraftHistory(string fileName)
        {
            ScriptDraftHistory history;
            if (_draftHistories.TryGetValue(fileName, out history)) return history;
            history = new ScriptDraftHistory();
            _draftHistories.Add(fileName, history);
            return history;
        }

        private void UndoCurrentDraft()
        {
            if (_currentEntry == null) return;
            string text;
            if (GetDraftHistory(_currentEntry.FileName).TryUndo(_editor.Text, out text)) ApplyDraftHistoryText(text);
        }

        private void RedoCurrentDraft()
        {
            if (_currentEntry == null) return;
            string text;
            if (GetDraftHistory(_currentEntry.FileName).TryRedo(_editor.Text, out text)) ApplyDraftHistoryText(text);
        }

        private void ApplyDraftHistoryText(string text)
        {
            _isApplyingDraftHistory = true;
            try
            {
                _editor.Text = text;
                _editor.SelectionStart = _editor.TextLength;
            }
            finally { _isApplyingDraftHistory = false; }
            SetDraftText(_currentEntry.FileName, text);
        }

        private void SetDraftText(string fileName, string text)
        {
            _editorDirty = true;
            _drafts[fileName] = text;
            _dirtyFiles.Add(fileName);
            RefreshScriptIndicators();
        }

        private void UpdateLineNumbers()
        {
            if (_lineNumbers == null || _lineNumbers.IsDisposed) return;
            var count = Math.Max(1, _editor.Lines.Length);
            var numbers = new StringBuilder();
            for (var i = 1; i <= count; i++) numbers.Append(i).Append('\n');
            _lineNumbers.Text = numbers.ToString();
            _lineNumbers.Select(0, 0);
            _lineNumbers.ScrollToCaret();
            _lineNumbersFirstVisibleLine = 0;
            BeginInvoke(new Action(SyncLineNumberScroll));
        }

        private void SyncLineNumberScroll()
        {
            if (_editor == null || _lineNumbers == null || !_editor.IsHandleCreated || !_lineNumbers.IsHandleCreated) return;
            var firstVisibleLine = SendMessage(_editor.Handle, EmGetFirstVisibleLine, 0, 0);
            var delta = firstVisibleLine - _lineNumbersFirstVisibleLine;
            if (delta == 0) return;
            SendMessage(_lineNumbers.Handle, EmLineScroll, 0, delta);
            _lineNumbersFirstVisibleLine = firstVisibleLine;
        }

        private void RebuildProfileList()
        {
            if (InvokeRequired) { Invoke(new Action(RebuildProfileList)); return; }
            var names = SettingsHolder.Instance.AllProfiles.OrderBy(n => n).ToList();
            if (_profileList.Items.Cast<object>().Select(v => (string)v).SequenceEqual(names)) return;

            _suppressEvents = true;
            try
            {
                _profileList.Items.Clear();
                foreach (var name in names) _profileList.Items.Add(name);
                ApplyProfileSelection();
            }
            finally { _suppressEvents = false; }
        }

        private void ApplyProfileSelection()
        {
            if (_profileList == null) return;
            _profileList.Enabled = _currentEntry == null || !_currentEntry.IsGlobal;
            for (var i = 0; i < _profileList.Items.Count; i++)
            {
                var name = (string)_profileList.Items[i];
                _profileList.SetItemChecked(i, _currentEntry != null && (_currentEntry.IsGlobal || _currentEntry.EnabledProfileNames.Contains(name)));
            }
        }

        private void RefreshList()
        {
            if (InvokeRequired) { Invoke(new Action(RefreshList)); return; }
            var selected = (_scriptList.SelectedItem as ScriptListEntry)?.FileName;
            _scriptList.BeginUpdate();
            try
            {
                _scriptList.Items.Clear();
                foreach (var entry in _manager.Entries)
                {
                    var item = new ScriptListEntry(entry);
                    _scriptList.Items.Add(item);
                    if (selected != null && string.Equals(entry.FileName, selected, StringComparison.OrdinalIgnoreCase)) _scriptList.SelectedItem = item;
                }
            }
            finally { _scriptList.EndUpdate(); }
        }

        private void RefreshScriptIndicators()
        {
            if (InvokeRequired) { BeginInvoke(new Action(RefreshScriptIndicators)); return; }
            _scriptList.Invalidate();
        }

        private void DrawScriptListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            var entry = ((ScriptListEntry)_scriptList.Items[e.Index]).Entry;
            var running = _rootModels.Any(m =>
            {
                var status = m.ScriptHost.GetScriptStatus(entry.FileName);
                return status != ScriptRunStatus.NotRunning && status != ScriptRunStatus.Finished && status != ScriptRunStatus.Faulted;
            });
            var dirty = _dirtyFiles.Contains(entry.FileName);
            DrawStatusCircle(e.Graphics, e.Bounds.Left + 7, e.Bounds.Top + 8, running, Color.LimeGreen);
            DrawStatusCircle(e.Graphics, e.Bounds.Left + 29, e.Bounds.Top + 8, entry.AutoStart, Color.DodgerBlue);
            DrawStatusCircle(e.Graphics, e.Bounds.Left + 51, e.Bounds.Top + 8, dirty, Color.OrangeRed);
            var textColor = (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : SystemColors.WindowText;
            TextRenderer.DrawText(e.Graphics, entry.FileName, _scriptList.Font, new Point(e.Bounds.Left + 74, e.Bounds.Top + 5), textColor, TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        private static void DrawStatusCircle(Graphics graphics, int x, int y, bool active, Color color)
        {
            using (var outline = new Pen(active ? Color.FromArgb(80, 80, 80) : SystemColors.ControlDark))
            {
                if (active)
                {
                    using (var brush = new SolidBrush(color)) graphics.FillEllipse(brush, x, y, 14, 14);
                }
                graphics.DrawEllipse(outline, x, y, 14, 14);
            }
        }

        private void OnScriptsChanged(object sender, EventArgs e) => RefreshList();
        private void OnExternalScriptChanged(object sender, ScriptFileChangedEventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OnExternalScriptChanged(sender, e))); return; }
            if (!_autoReload || IsDisposed) return;
            var entry = _manager.Entries.FirstOrDefault(s => string.Equals(s.FileName, e.FileName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                // A deleted file must never leave an orphaned coroutine alive.
                foreach (var model in _rootModels) model.ScriptHost.StopScript(e.FileName);
                return;
            }
            var code = _manager.ReadCode(entry.FileName);
            foreach (var model in _rootModels.Where(m => m.ScriptHost.NeedsRestart(entry.FileName, code)))
            {
                string error;
                if (model.ScriptHost.TryValidateScript(entry.FileName, code, out error)) model.ScriptHost.StartScript(entry.FileName, code);
                else AppendLog("Автообновление отменено: " + error, Color.OrangeRed);
            }
        }
        private void OnScriptSelected(object sender, EventArgs e)
        {
            var selectedEntry = (_scriptList.SelectedItem as ScriptListEntry)?.Entry;
            _currentEntry = selectedEntry;
            LoadIntoEditor(_currentEntry);
        }

        private void LoadIntoEditor(ScriptFileEntry entry)
        {
            _suppressEvents = true;
            try
            {
                if (entry == null) { _editor.Text = ""; _fileLabel.Text = ""; _sharedCheck.Checked = false; _autoStartCheck.Checked = false; ApplyProfileSelection(); return; }
                string draft;
                _editor.Text = _drafts.TryGetValue(entry.FileName, out draft) ? draft : _manager.ReadCode(entry.FileName);
                HighlightLua(_editor);
                _fileLabel.Text = _manager.GetFilePath(entry.FileName);
                _sharedCheck.Checked = entry.IsShared;
                _autoStartCheck.Checked = entry.AutoStart;
                ApplyProfileSelection();
            }
            finally { _suppressEvents = false; _editorDirty = entry != null && _dirtyFiles.Contains(entry.FileName); }
        }

        private void HandleSave()
        {
            if (_currentEntry == null) return;
            SaveDraft(_currentEntry.FileName, _editor.Text);
        }

        private void SaveDraft(string fileName, string code, bool applyAutoReload = true)
        {
            _manager.WriteCode(fileName, code);
            _drafts.Remove(fileName);
            _dirtyFiles.Remove(fileName);
            _editorDirty = false;
            RefreshScriptIndicators();
            if (!applyAutoReload || !_autoReload) return;

            string error;
            foreach (var model in _rootModels.Where(m => m.ScriptHost.NeedsRestart(fileName, code)))
            {
                if (!model.ScriptHost.TryValidateScript(fileName, code, out error))
                {
                    AppendLog("Автообновление отменено: " + error, Color.OrangeRed);
                    continue;
                }
                model.ScriptHost.StartScript(fileName, code);
            }
        }

        private void HandleSaveAll()
        {
            foreach (var draft in _drafts.ToList()) SaveDraft(draft.Key, draft.Value);
            RefreshScriptIndicators();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_dirtyFiles.Count == 0) return;
            var result = MessageBox.Show("Есть несохранённые изменения. Сохранить все черновики?", "Scripts", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel) { e.Cancel = true; return; }
            if (result != DialogResult.Yes) return;
            foreach (var draft in _drafts.ToList()) SaveDraft(draft.Key, draft.Value, false);
        }

        private void HandleNew()
        {
            using (var form = new Form())
            {
                form.Text = "Новый скрипт"; form.Size = new Size(350, 130); form.FormBorderStyle = FormBorderStyle.FixedDialog; form.StartPosition = FormStartPosition.CenterParent; form.MaximizeBox = false; form.MinimizeBox = false;
                var lbl = new Label { Text = "Имя файла (.lua):", Left = 12, Top = 12, AutoSize = true };
                var txt = new TextBox { Left = 12, Top = 30, Width = 310 };
                var ok  = new Button { Text = "Создать", Left = 165, Top = 60, Width = 75, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Отмена", Left = 248, Top = 60, Width = 75, DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { lbl, txt, ok, cancel }); form.AcceptButton = ok; form.CancelButton = cancel;
                if (form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(txt.Text)) { _manager.CreateScript(txt.Text.Trim()); RefreshList(); }
            }
        }

        private void HandleDelete()
        {
            if (_currentEntry == null) return;
            if (MessageBox.Show("Удалить " + _currentEntry.FileName + "?", "Удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var m in _rootModels) m.ScriptHost.StopScript(_currentEntry.FileName);
            _drafts.Remove(_currentEntry.FileName);
            _dirtyFiles.Remove(_currentEntry.FileName);
            _manager.DeleteScript(_currentEntry.FileName); _currentEntry = null; _editorDirty = false; RefreshList();
        }

        private void HandleStart()
        {
            if (_currentEntry == null) return;
            HandleSave();
            var code = _manager.ReadCode(_currentEntry.FileName);
            var targets = _currentEntry.IsGlobal ? _rootModels : (IEnumerable<RootModel>)_rootModels.Where(m => m.Profile != null && _currentEntry.EnabledProfileNames.Contains(m.Profile.Name));
            foreach (var m in targets) m.ScriptHost.StartScript(_currentEntry.FileName, code);
        }

        private void HandleStop() { if (_currentEntry == null) return; foreach (var m in _rootModels) m.ScriptHost.StopScript(_currentEntry.FileName); }

        private void OnSharedChanged(object sender, EventArgs e)
        {
            if (_suppressEvents || _currentEntry == null) return;
            _currentEntry.IsGlobal = _sharedCheck.Checked;
            _suppressEvents = true;
            ApplyProfileSelection();
            _suppressEvents = false;
            _manager.SaveMetadata();
        }

        private void OnAutoStartChanged(object sender, EventArgs e) { if (_suppressEvents || _currentEntry == null) return; _currentEntry.AutoStart = _autoStartCheck.Checked; _manager.SaveMetadata(); RefreshScriptIndicators(); }

        private void OnProfileItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressEvents || _currentEntry == null || _currentEntry.IsGlobal) return;
            BeginInvoke(new Action(() =>
            {
                if (_suppressEvents || _currentEntry == null) return;
                _currentEntry.EnabledProfileNames = _profileList.CheckedItems.Cast<string>().ToList();
                _manager.SaveMetadata();
            }));
        }

        private void HighlightLua(RichTextBox box)
        {
            if (_isHighlighting) return;
            _isHighlighting = true;
            try
            {
                var position = box.SelectionStart;
                var length = box.SelectionLength;
                box.SelectAll();
                box.SelectionColor = SystemColors.WindowText;
                foreach (Match match in Regex.Matches(box.Text, "--.*$|\\b(?:and|break|do|else|elseif|end|false|for|function|if|in|local|nil|not|or|repeat|return|then|true|until|while)\\b|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", RegexOptions.Multiline))
                {
                    box.Select(match.Index, match.Length);
                    box.SelectionColor = match.Value.StartsWith("--") ? Color.Green : (match.Value.StartsWith("\"") || match.Value.StartsWith("'") ? Color.Brown : Color.Blue);
                }
                box.Select(position, length);
            }
            finally { _isHighlighting = false; }
        }
    }

    internal sealed class ScriptListEntry
    {
        public ScriptFileEntry Entry { get; private set; }
        public string FileName { get { return Entry.FileName; } }

        public ScriptListEntry(ScriptFileEntry entry)
        {
            Entry = entry;
        }

        public override string ToString()
        {
            return Entry.FileName;
        }
    }
}
