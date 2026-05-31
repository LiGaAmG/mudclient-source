// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TextMessageBlock.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Plain text message block that defines string and color.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.Common.Messages
{
    using System.Collections.Generic;
    using System.Linq;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Themes;

    /// <summary>
    /// Plain text message block that defines string and color.
    /// </summary>
    /// TODO: 
    public class TextMessageBlock
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TextMessageBlock"/> class.
        /// </summary>
        /// <param name="text">The text of created block.</param>
        /// <param name="foreground">The foreground.</param>
        /// <param name="background">The background.</param>
        /// <param name="toolTipText">Optional tooltip text for this block.</param>
        /// <param name="toolTipLines">Optional tooltip lines with individual colors.</param>
        public TextMessageBlock(
            [NotNull] string text,
            [NotNull] TextColor foreground,
            [NotNull] TextColor background,
            [CanBeNull] string toolTipText = null,
            [CanBeNull] IList<ToolTipLine> toolTipLines = null)
        {
            Assert.ArgumentNotNull(text, "text");
            Assert.ArgumentNotNull(foreground, "foreground");
            Assert.ArgumentNotNull(background, "background");

            Text = text;
            Foreground = foreground;
            Background = background;
            ToolTipText = toolTipText;
            ToolTipLines = CloneToolTipLines(toolTipLines);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TextMessageBlock"/> class.
        /// </summary>
        /// <param name="text">The text of created block.</param>
        /// <param name="foreground">The fore ground.</param>
        public TextMessageBlock([NotNull] string text, [NotNull] TextColor foreground)
            : this(text, foreground, TextColor.None)
        {
            Assert.ArgumentNotNull(text, "text");
            Assert.ArgumentNotNull(foreground, "foreground");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TextMessageBlock"/> class.
        /// </summary>
        /// <param name="text">The text of created block.</param>
        public TextMessageBlock([NotNull] string text)
            : this(text, TextColor.None, TextColor.None)
        {
            Assert.ArgumentNotNull(text, "text");
        }

        /// <summary>
        /// Gets the text of this block.
        /// </summary>
        public string Text
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the text foreground color of this block.
        /// </summary>
        public TextColor Foreground
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the text background color of this block.
        /// </summary>
        public TextColor Background
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets optional tooltip text for this block.
        /// </summary>
        [CanBeNull]
        public string ToolTipText
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets optional colored tooltip lines for this block.
        /// </summary>
        [CanBeNull]
        public IList<ToolTipLine> ToolTipLines
        {
            get;
            private set;
        }

        /// <summary>
        /// Changes the inner text.
        /// </summary>
        /// <param name="newInnerText">The new inner text.</param>
        public void ChangeInnerText([NotNull]string newInnerText)
        {
            Assert.ArgumentNotNull(newInnerText, "newInnerText");

            Text = newInnerText;
        }

        [CanBeNull]
        private static List<ToolTipLine> CloneToolTipLines([CanBeNull] IEnumerable<ToolTipLine> sourceLines)
        {
            if (sourceLines == null)
            {
                return null;
            }

            var result = new List<ToolTipLine>();
            foreach (var sourceLine in sourceLines)
            {
                if (sourceLine == null)
                {
                    continue;
                }

                result.Add(new ToolTipLine(sourceLine.Blocks));
            }

            return result.Count == 0 ? null : result;
        }

        /// <summary>
        /// Tooltip line that preserves colored text fragments.
        /// </summary>
        public sealed class ToolTipLine
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ToolTipLine"/> class.
            /// </summary>
            /// <param name="blocks">The blocks for this tooltip line.</param>
            public ToolTipLine([NotNull] IEnumerable<TextMessageBlock> blocks)
            {
                Assert.ArgumentNotNull(blocks, "blocks");

                Blocks = blocks
                    .Where(block => block != null)
                    .Select(block => new TextMessageBlock(block.Text, block.Foreground, block.Background))
                    .ToList();
            }

            /// <summary>
            /// Gets text blocks for this line.
            /// </summary>
            [NotNull]
            public IList<TextMessageBlock> Blocks
            {
                get;
                private set;
            }
        }
    }
}
