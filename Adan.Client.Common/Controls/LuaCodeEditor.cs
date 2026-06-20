namespace Adan.Client.Common.Controls
{
    using System;
    using System.Text.RegularExpressions;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Documents;
    using System.Windows.Media;

    /// <summary>
    /// A minimal Lua syntax-highlighting text editor. Plain RichTextBox
    /// subclass exposing a two-way bindable <see cref="Code"/> string
    /// property (RichTextBox itself has no such property -- its content
    /// lives in a FlowDocument). On every text change, re-tokenizes the
    /// whole document with a regex pass and recolors keywords/strings/
    /// comments/numbers. Scripts edited here are short (a handful of
    /// lines), so a full re-highlight per keystroke is cheap enough not
    /// to need incremental highlighting.
    /// </summary>
    public class LuaCodeEditor : RichTextBox
    {
        public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
            "Code",
            typeof(string),
            typeof(LuaCodeEditor),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCodePropertyChanged));

        // Order matters: comments/strings are matched as single tokens
        // first so keyword/number matching never reaches inside them.
        private static readonly Regex TokenRegex = new Regex(
            @"(?<comment>--\[\[[\s\S]*?\]\]|--[^\n]*)" +
            @"|(?<string>""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*')" +
            @"|(?<number>\b\d+(?:\.\d+)?\b)" +
            @"|(?<keyword>\b(?:and|break|do|else|elseif|end|false|for|function|goto|if|in|local|nil|not|or|repeat|return|then|true|until|while)\b)",
            RegexOptions.Compiled);

        private static readonly SolidColorBrush DefaultBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
        private static readonly SolidColorBrush KeywordBrush = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
        private static readonly SolidColorBrush StringBrush = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78));
        private static readonly SolidColorBrush CommentBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x99, 0x55));
        private static readonly SolidColorBrush NumberBrush = new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8));

        private bool _isUpdatingFromCode;
        private bool _isUpdatingFromDocument;
        private bool _isHighlighting;

        public LuaCodeEditor()
        {
            AcceptsTab = true;
            TextChanged += HandleTextChanged;

            // Every keystroke rebuilds the WHOLE FlowDocument (re-highlight,
            // see ApplyHighlighting), and switching which script is shown
            // (the Code property changing because a different list item got
            // selected) does the same via SetPlainText -- both go through
            // WPF's normal Document mutation path, which records onto this
            // control's undo stack same as a real edit would. That made
            // Ctrl+Z unwind PAST a script switch and corrupt/replace the
            // current script's text with a stale, unrelated previous
            // script's content -- not just "undo my last keystroke". WPF's
            // undo here only ever operates at "replace the whole document"
            // granularity anyway (never per-character), so it was never a
            // safe/meaningful undo to begin with; disabling it trades away
            // in-editor undo for not silently destroying script content.
            // The actual persisted state lives in ScriptDefinition.Code,
            // saved via the dialog's Save/Close, not in this transient
            // editor history.
            IsUndoEnabled = false;
        }

        public string Code
        {
            get { return (string)GetValue(CodeProperty); }
            set { SetValue(CodeProperty, value); }
        }

        private static void OnCodePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (LuaCodeEditor)d;
            if (editor._isUpdatingFromDocument)
            {
                // The Code property was just set BY this same edit (see
                // HandleTextChanged below) -- the document already
                // reflects this text, don't rebuild it and lose the
                // caret position the user is actively typing at.
                return;
            }

            editor._isUpdatingFromCode = true;
            try
            {
                editor.SetPlainText((string)e.NewValue ?? string.Empty);
                editor.ApplyHighlighting();
            }
            finally
            {
                editor._isUpdatingFromCode = false;
            }
        }

        private void HandleTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode || _isHighlighting)
            {
                return;
            }

            // Caret position saved before the rebuild below replaces every
            // Run/LineBreak (which would otherwise reset the caret to the
            // start), then used to put the caret back where the user was
            // typing. MUST use GetOffsetToPosition/GetPositionAtOffset on
            // BOTH ends -- WPF's TextPointer "offset" is a "symbol count",
            // not a plain character count, and once highlighting splits a
            // line into many small Run elements (one per token) a plain
            // character count (e.g. via TextRange.Text.Length) drifts from
            // the symbol count, which is exactly what made the caret jump
            // around while typing in longer/more-tokenized lines.
            var caretOffset = Document.ContentStart.GetOffsetToPosition(CaretPosition);

            _isUpdatingFromDocument = true;
            try
            {
                Code = GetPlainText();
            }
            finally
            {
                _isUpdatingFromDocument = false;
            }

            ApplyHighlighting();

            var restoredPosition = Document.ContentStart.GetPositionAtOffset(caretOffset);
            CaretPosition = restoredPosition ?? Document.ContentEnd;
        }

        private string GetPlainText()
        {
            return new TextRange(Document.ContentStart, Document.ContentEnd).Text;
        }

        private void SetPlainText(string text)
        {
            Document.Blocks.Clear();
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            AddTextWithLineBreaks(paragraph.Inlines, text, DefaultBrush);
            Document.Blocks.Add(paragraph);
        }

        private void ApplyHighlighting()
        {
            _isHighlighting = true;
            try
            {
                var text = GetPlainText();

                var paragraph = new Paragraph { Margin = new Thickness(0) };
                var lastIndex = 0;

                foreach (Match match in TokenRegex.Matches(text))
                {
                    if (match.Index > lastIndex)
                    {
                        AddTextWithLineBreaks(paragraph.Inlines, text.Substring(lastIndex, match.Index - lastIndex), DefaultBrush);
                    }

                    var brush = DefaultBrush;
                    if (match.Groups["keyword"].Success)
                    {
                        brush = KeywordBrush;
                    }
                    else if (match.Groups["string"].Success)
                    {
                        brush = StringBrush;
                    }
                    else if (match.Groups["comment"].Success)
                    {
                        brush = CommentBrush;
                    }
                    else if (match.Groups["number"].Success)
                    {
                        brush = NumberBrush;
                    }

                    AddTextWithLineBreaks(paragraph.Inlines, match.Value, brush);
                    lastIndex = match.Index + match.Length;
                }

                if (lastIndex < text.Length)
                {
                    AddTextWithLineBreaks(paragraph.Inlines, text.Substring(lastIndex), DefaultBrush);
                }

                Document.Blocks.Clear();
                Document.Blocks.Add(paragraph);
            }
            finally
            {
                _isHighlighting = false;
            }
        }

        /// <summary>
        /// Run.Text does not render embedded "\n" as a line break inside a
        /// FlowDocument -- WPF requires an explicit LineBreak inline
        /// element between lines. Splits the given segment on '\n' (and
        /// '\r\n') and inserts LineBreak elements between the pieces so
        /// multi-line script source actually wraps correctly.
        /// </summary>
        private static void AddTextWithLineBreaks(InlineCollection inlines, string segment, Brush brush)
        {
            var lines = segment.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                {
                    inlines.Add(new Run(lines[i]) { Foreground = brush });
                }

                if (i < lines.Length - 1)
                {
                    inlines.Add(new LineBreak());
                }
            }
        }
    }
}
