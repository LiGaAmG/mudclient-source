using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Adan.Client.ViewModel;

namespace Adan.Client.Windows
{
    /// <summary>WinForms presentation of the existing Lua help topic catalogue.</summary>
    internal sealed class ScriptHelpForm : Form
    {
        private readonly ToolStripTextBox _search = new ToolStripTextBox();
        private readonly TreeView _topics = new TreeView();
        // RichTextBox scrolls formatted RTF through the legacy edit control and visibly
        // repaints during wheel scrolling. WebBrowser is a stock WinForms control and
        // keeps the help text crisp while retaining formatting and Lua colours.
        private readonly WebBrowser _content = new WebBrowser();

        public ScriptHelpForm()
        {
            Text = "Lua scripting help";
            Size = new Size(1100, 700);
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterParent;

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 440, FixedPanel = FixedPanel.Panel1 };
            var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            _search.AutoSize = false;
            _search.Width = 300;
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.BackColor = SystemColors.Window;
            _search.ForeColor = SystemColors.WindowText;
            _search.AccessibleName = "Поиск по справке";
            var clearSearch = new ToolStripButton("Очистить") { Enabled = false };
            clearSearch.Click += (s, e) => _search.Text = string.Empty;
            _search.TextChanged += (s, e) => { clearSearch.Enabled = !string.IsNullOrWhiteSpace(_search.Text); RebuildTopics(); };
            toolbar.Items.Add(new ToolStripButton("Назад") { Enabled = false });
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(new ToolStripLabel("Поиск:"));
            toolbar.Items.Add(_search);
            toolbar.Items.Add(clearSearch);
            _topics.Dock = DockStyle.Fill;
            _topics.Scrollable = true;
            _topics.ShowNodeToolTips = true;
            _topics.DrawMode = TreeViewDrawMode.OwnerDrawText;
            _topics.DrawNode += DrawTopicNode;
            _topics.HideSelection = false;
            _topics.AfterSelect += (s, e) => ShowTopic(e.Node.Tag as HelpTopic);
            split.Panel1.Controls.Add(_topics);

            _content.Dock = DockStyle.Fill;
            _content.AllowWebBrowserDrop = false;
            _content.IsWebBrowserContextMenuEnabled = false;
            _content.ScriptErrorsSuppressed = true;
            _content.WebBrowserShortcutsEnabled = true;
            split.Panel2.Controls.Add(_content);
            Controls.Add(split);
            Controls.Add(toolbar);
            Load += (s, e) =>
            {
                split.Panel1MinSize = 400;
                split.Panel2MinSize = 420;
                split.SplitterDistance = 440;
            };
            RebuildTopics();
        }

        private void RebuildTopics()
        {
            var filter = _search.Text.Trim();
            _topics.BeginUpdate();
            _topics.Nodes.Clear();
            foreach (var category in HelpTopics.All
                .Where(t => string.IsNullOrEmpty(filter) || t.SearchableText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(t => t.Category))
            {
                var parent = _topics.Nodes.Add(category.Key);
                foreach (var topic in category)
                {
                    var title = topic.Title.Length > 44 ? topic.Title.Substring(0, 43) + "…" : topic.Title;
                    var node = parent.Nodes.Add(title);
                    node.Tag = topic;
                    node.ToolTipText = topic.Title;
                }
            }
            _topics.ExpandAll();
            var first = _topics.Nodes.Cast<TreeNode>().SelectMany(n => n.Nodes.Cast<TreeNode>()).FirstOrDefault();
            if (first != null) _topics.SelectedNode = first;
            _topics.EndUpdate();
        }

        private void ShowTopic(HelpTopic topic)
        {
            if (topic == null) return;
            _content.DocumentText = BuildTopicHtml(topic);
        }

        private void DrawTopicNode(object sender, DrawTreeNodeEventArgs e)
        {
            var topic = e.Node.Tag as HelpTopic;
            if (topic == null || (e.State & TreeNodeStates.Selected) != 0)
            {
                e.DrawDefault = true;
                return;
            }

            Color background;
            if (topic.Category == "События (Wait)") background = Color.FromArgb(255, 255, 220);
            else if (topic.Category == "Данные пакетов") background = Color.FromArgb(220, 230, 245);
            else if (topic.Category == "Функции") background = Color.FromArgb(225, 242, 225);
            else if (topic.Category == "Управление и ограничения") background = Color.FromArgb(250, 225, 225);
            else background = SystemColors.Window;

            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Node.Text, _topics.Font, e.Bounds, SystemColors.WindowText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static string BuildTopicHtml(HelpTopic topic)
        {
            var html = new StringBuilder();
            html.Append("<!doctype html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><style>");
            html.Append("body{margin:16px;font-family:'Segoe UI',sans-serif;font-size:14px;line-height:1.38;color:#000;background:#fff;}");
            html.Append("h1{margin:0 0 20px;font-size:26px;line-height:1.2;}p{margin:0 0 16px;white-space:pre-wrap;}");
            html.Append("pre{margin:0 0 16px;padding:10px;border:1px solid #ddd;background:#f5f5f5;font:13px Consolas,monospace;line-height:1.35;white-space:pre-wrap;}");
            html.Append(".kw{color:#0000cc}.str{color:#a31515}.com{color:#008000}</style></head><body>");
            html.Append("<h1>").Append(EscapeHtml(topic.Title)).Append("</h1>");
            foreach (var block in topic.Blocks)
            {
                html.Append(block.IsCode
                    ? "<pre>" + FormatLua(block.Code) + "</pre>"
                    : "<p>" + EscapeHtml(block.Text) + "</p>");
            }
            return html.Append("</body></html>").ToString();
        }

        private static string FormatLua(string code)
        {
            var result = new StringBuilder();
            var position = 0;
            foreach (Match match in Regex.Matches(code, "--.*$|\\b(?:and|break|do|else|elseif|end|false|for|function|if|in|local|nil|not|or|repeat|return|then|true|until|while)\\b|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", RegexOptions.Multiline))
            {
                result.Append(EscapeHtml(code.Substring(position, match.Index - position)));
                var cssClass = match.Value.StartsWith("--") ? "com" : (match.Value.StartsWith("\"") || match.Value.StartsWith("'") ? "str" : "kw");
                result.Append("<span class=\"").Append(cssClass).Append("\">").Append(EscapeHtml(match.Value)).Append("</span>");
                position = match.Index + match.Length;
            }
            return result.Append(EscapeHtml(code.Substring(position))).ToString();
        }

        private static string EscapeHtml(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
