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

            // RichTextBox/FlowDocument wraps lines to the visible width by
            // default, which breaks the column alignment of any
            // fixed-width-formatted text (e.g. the tables in the help
            // content). A fixed, generously wide PageWidth disables that
            // reflow -- long lines instead overflow and scroll horizontally
            // (callers should enable HorizontalScrollBarVisibility), which
            // is also the behavior every plain-text code editor has.
            Document.PageWidth = 4000;
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

            // This branch only runs when Code changed from OUTSIDE this
            // control (e.g. the Scripts dialog's list selection switched to
            // a different script, or a fresh script's code was just loaded)
            // -- never on a per-keystroke edit, since those go through
            // HandleTextChanged with _isUpdatingFromDocument set, which the
            // early-return above already filters out. So clearing the undo
            // stack here only erases history at exactly the moment the
            // displayed content actually changed to a DIFFERENT script's
            // text, which is the only point where continuing to let Ctrl+Z
            // reach further back would mean undoing into unrelated, stale
            // content. Toggling IsUndoEnabled off-then-on is the documented
            // way to clear WPF's undo stack; normal typing keeps
            // accumulating undo history as expected in between these resets.
            editor.IsUndoEnabled = false;
            editor.IsUndoEnabled = true;
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

            // Briefly move the caret away before snapping it back to where
            // the user was typing. WPF only merges this edit's undo unit
            // with the previous one when the caret stays contiguous with
            // the prior edit's end position; this discontinuity is what
            // makes WPF start a fresh undo unit per keystroke instead of
            // accumulating the whole editing session into a single unit
            // (see ApplyHighlighting's comment for the other half of this
            // fix -- both were needed to stop Ctrl+Z from wiping the script).
            CaretPosition = Document.ContentStart;
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

                // Reuse the existing Paragraph object (just clear and refill
                // its Inlines) instead of replacing Document.Blocks wholesale
                // on every keystroke. Swapping the Block object itself made
                // WPF's undo manager treat every highlighting pass as a
                // brand-new "replace the whole document" edit with nothing
                // distinguishing it from the previous pass, so consecutive
                // keystrokes kept merging into one giant undo unit -- a
                // single Ctrl+Z could wipe an entire script. Mutating the
                // same Paragraph's Inlines keeps the edit scoped to content
                // changes only, which WPF can merge/segment more sensibly.
                var paragraph = Document.Blocks.FirstBlock as Paragraph;
                if (paragraph == null)
                {
                    paragraph = new Paragraph { Margin = new Thickness(0) };
                    Document.Blocks.Clear();
                    Document.Blocks.Add(paragraph);
                }
                else
                {
                    paragraph.Inlines.Clear();
                }

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
