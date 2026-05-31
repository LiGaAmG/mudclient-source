
namespace Adan.Client.Common.Controls
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Documents;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Windows.Media.TextFormatting;
    using System.Windows.Threading;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    using Messages;

    using TextFormatting;
    using Themes;
    using Settings;
    using Utils;

    #endregion

    /// <summary>
    /// Interaction logic for ScrollableFlowTextControl.xaml
    /// </summary>
    public class ScrollableFlowTextControl : Control, IScrollInfo
    {
        #region Constants and Fields

        private readonly List<TextMessage> _messages = new List<TextMessage>();
        private readonly TextFormatter _formatter = TextFormatter.Create(TextFormattingMode.Ideal);
        private readonly TextSelectionSettings _selectionSettings = new TextSelectionSettings();
        private readonly TextSelectionSettings _tempSelectionSettings = new TextSelectionSettings();
        private readonly MessageTextSource _textSource;
        private readonly CustomTextParagraphProperties _customTextParagraphProperties = new CustomTextParagraphProperties();
        private readonly TextRunCache _textRunCache = new TextRunCache();
        private readonly Stack<TextLine> _linesToRenderStack = new Stack<TextLine>();
        private readonly ToolTip _interactiveToolTip = new ToolTip();
        private readonly List<ToolTipHotspot> _toolTipHotspots = new List<ToolTipHotspot>();


        private readonly DispatcherTimer _doubleClickTimer;

        private int _currentLineNumber;
        private int _currentNumberOfLinesInView;
        private double _lineHeight;
        private string _shownToolTipText = string.Empty;
        private IList<TextMessageBlock.ToolTipLine> _shownToolTipLines;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ScrollableFlowTextControl"/> class.
        /// </summary>
        public ScrollableFlowTextControl()
        {
            AutoScroll = true;
            ClipToBounds = true;
            _textSource = new MessageTextSource(_selectionSettings);
            _doubleClickTimer = new DispatcherTimer(new TimeSpan(0, 0, 0, 0, 150), DispatcherPriority.Background, (o, e) => ClearTextSelection(), Dispatcher.CurrentDispatcher);
            _doubleClickTimer.Stop();
            _interactiveToolTip.Placement = PlacementMode.Relative;
            _interactiveToolTip.PlacementTarget = this;
            _interactiveToolTip.StaysOpen = true;
            _interactiveToolTip.MaxWidth = 800;
            _interactiveToolTip.IsHitTestVisible = false;
            ToolTipService.SetShowDuration(this, int.MaxValue);

            MaximumLinesToStore = Math.Max(SettingsHolder.Instance.Settings.ScrollBuffer, 100);
            SettingsHolder.Instance.Settings.OnSettingsChanged += HandleSettingsChanged;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the lines overflow percent before cleanup.
        /// </summary>
        /// <value>
        /// The lines overflow percent before cleanup.
        /// </value>
        public int LinesOverflowPercentBeforeCleanup
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the maximum lines to store.
        /// </summary>
        /// <value>
        /// The maximum lines to store.
        /// </value>
        public int MaximumLinesToStore
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether content will be automatically scrolled down to last line.
        /// </summary>
        /// <value>
        ///   <c>true</c> if content will be automatically scrolled down to last line; otherwise, <c>false</c>.
        /// </value>
        public bool AutoScroll
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the parent scroll info.
        /// </summary>
        /// <value>
        /// The parent scroll info.
        /// </value>
        [CanBeNull]
        public IScrollInfo ParentScrollInfo
        {
            get;
            set;
        }

        #endregion

        #region Implementation of IScrollInfo properties
        /// <summary>
        /// Gets or sets a value indicating whether scrolling on the vertical axis is possible. 
        /// </summary>
        /// <returns>
        /// true if scrolling is possible; otherwise, false. This property has no default value.
        /// </returns>
        public bool CanVerticallyScroll
        {
            get
            {
                return true;
            }

            set
            {
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether scrolling on the horizontal axis is possible.
        /// </summary>
        /// <returns>
        /// true if scrolling is possible; otherwise, false. This property has no default value.
        /// </returns>
        public bool CanHorizontallyScroll
        {
            get
            {
                return false;
            }

            set
            {
            }
        }

        /// <summary>
        /// Gets the horizontal size of the extent.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Double"/> that represents, in device independent pixels, the horizontal size of the extent. This property has no default value.
        /// </returns>
        public double ExtentWidth
        {
            get
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the vertical size of the extent.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Double"/> that represents, in device independent pixels, the vertical size of the extent.This property has no default value.
        /// </returns>
        public double ExtentHeight
        {
            get
            {
                return _messages.Count;
            }
        }

        /// <summary>
        /// Gets the horizontal size of the viewport for this content.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Double"/> that represents, in device independent pixels, the horizontal size of the viewport for this content. This property has no default value.
        /// </returns>
        public double ViewportWidth
        {
            get
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the vertical size of the viewport for this content.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Double"/> that represents, in device independent pixels, the vertical size of the viewport for this content. This property has no default value.
        /// </returns>
        public double ViewportHeight
        {
            get
            {
                if (ParentScrollInfo != null && _currentNumberOfLinesInView == 0)
                {
                    return ParentScrollInfo.ViewportHeight;
                }

                return _currentNumberOfLinesInView;
            }
        }

        /// <summary>
        /// Gets the horizontal offset of the scrolled content.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Double"/> that represents, in device independent pixels, the horizontal offset. This property has no default value.
        /// </returns>
        public double HorizontalOffset
        {
            get
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the vertical offset of the scrolled content.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Double"/> that represents, in device independent pixels, the vertical offset of the scrolled content. Valid values are between zero and the <see cref="P:System.Windows.Controls.Primitives.IScrollInfo.ExtentHeight"/> minus the <see cref="P:System.Windows.Controls.Primitives.IScrollInfo.ViewportHeight"/>. This property has no default value.
        /// </returns>
        public double VerticalOffset
        {
            get
            {
                return _currentLineNumber;
            }
        }

        /// <summary>
        /// Gets or sets a <see cref="T:System.Windows.Controls.ScrollViewer"/> element that controls scrolling behavior.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Windows.Controls.ScrollViewer"/> element that controls scrolling behavior. This property has no default value.
        /// </returns>
        public ScrollViewer ScrollOwner
        {
            get;
            set;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Adds the messages.
        /// </summary>
        /// <param name="messages">The messages.</param>
        public void AddMessages([NotNull] IEnumerable<TextMessage> messages)
        {
            Assert.ArgumentNotNull(messages, "messages");
            _messages.AddRange(messages);

            CheckMessagesOverflow();

            if (ScrollOwner != null)
            {
                ScrollOwner.InvalidateScrollInfo();
            }

            if (!AutoScroll)
            {
                return;
            }

            if (_selectionSettings.Dragging)
            {
                ClearTextSelection();
            }

            _currentLineNumber = _messages.Count;

            InvalidateVisual();
        }

        /// <summary>
        /// Scrolls to end.
        /// </summary>
        public void ScrollToEnd()
        {
            _currentLineNumber = _messages.Count;
            InvalidateVisual();
        }

        #endregion

        #region Implementation of IScrollInfo methods

        /// <summary>
        /// Scrolls up within content by one logical unit. 
        /// </summary>
        public void LineUp()
        {
            if (_currentLineNumber <= 0 || _currentLineNumber <= _currentNumberOfLinesInView)
            {
                return;
            }

            _currentLineNumber--;

            if (ScrollOwner != null)
            {
                ScrollOwner.InvalidateScrollInfo();
            }

            InvalidateVisual();
        }

        /// <summary>
        /// Scrolls down within content by one logical unit. 
        /// </summary>
        public void LineDown()
        {
            if (_currentLineNumber >= _messages.Count)
            {
                return;
            }

            _currentLineNumber++;

            if (ScrollOwner != null)
            {
                ScrollOwner.InvalidateScrollInfo();
            }

            InvalidateVisual();
        }

        /// <summary>
        /// Scrolls left within content by one logical unit.
        /// </summary>
        public void LineLeft()
        {
        }

        /// <summary>
        /// Scrolls right within content by one logical unit.
        /// </summary>
        public void LineRight()
        {
        }

        /// <summary>
        /// Scrolls up within content by one page.
        /// </summary>
        public void PageUp()
        {
            if (_currentNumberOfLinesInView == 0)
            {
                if (ParentScrollInfo != null)
                {
                    _currentLineNumber -= (int)ParentScrollInfo.ViewportHeight;
                }
                else
                {
                    _currentLineNumber -= 10;
                }
            }
            else
            {
                _currentLineNumber -= (_currentNumberOfLinesInView / 2) - 1;
            }

            if (_currentLineNumber == _messages.Count)
            {
                _currentLineNumber--;
            }

            if (_currentLineNumber < _currentNumberOfLinesInView)
            {
                _currentLineNumber = _currentNumberOfLinesInView;
            }

            if (ScrollOwner != null)
            {
                ScrollOwner.InvalidateScrollInfo();
            }

            InvalidateVisual();
        }

        /// <summary>
        /// Scrolls down within content by one page.
        /// </summary>
        public void PageDown()
        {
            if (_currentNumberOfLinesInView == 0)
            {
                return;
            }

            _currentLineNumber += (_currentNumberOfLinesInView / 2) - 1;
            if (_currentLineNumber > _messages.Count)
            {
                _currentLineNumber = _messages.Count;
            }

            if (ScrollOwner != null)
            {
                ScrollOwner.InvalidateScrollInfo();
            }

            InvalidateVisual();
        }

        /// <summary>
        /// Scrolls left within content by one page.
        /// </summary>
        public void PageLeft()
        {
        }

        /// <summary>
        /// Scrolls right within content by one page.
        /// </summary>
        public void PageRight()
        {
        }

        /// <summary>
        /// Scrolls up within content after a user clicks the wheel button on a mouse.
        /// </summary>
        public void MouseWheelUp()
        {
            LineUp();
            LineUp();
            LineUp();
        }

        /// <summary>
        /// Scrolls down within content after a user clicks the wheel button on a mouse.
        /// </summary>
        public void MouseWheelDown()
        {
            LineDown();
            LineDown();
            LineDown();
        }

        /// <summary>
        /// Scrolls left within content after a user clicks the wheel button on a mouse.
        /// </summary>
        public void MouseWheelLeft()
        {
        }

        /// <summary>
        /// Scrolls right within content after a user clicks the wheel button on a mouse.
        /// </summary>
        public void MouseWheelRight()
        {
        }

        /// <summary>
        /// Sets the amount of horizontal offset.
        /// </summary>
        /// <param name="offset">The degree to which content is horizontally offset from the containing viewport.</param>
        public void SetHorizontalOffset(double offset)
        {
        }

        /// <summary>
        /// Sets the amount of vertical offset.
        /// </summary>
        /// <param name="offset">The degree to which content is vertically offset from the containing viewport.</param>
        public void SetVerticalOffset(double offset)
        {
            _currentLineNumber = (int)Math.Ceiling(offset) + _currentNumberOfLinesInView;
            if (_currentLineNumber < _currentNumberOfLinesInView)
            {
                _currentLineNumber = _currentNumberOfLinesInView;
            }

            if (_currentLineNumber > _messages.Count)
            {
                _currentLineNumber = _messages.Count;
            }

            InvalidateVisual();
        }

        /// <summary>
        /// Forces content to scroll until the coordinate space of a <see cref="T:System.Windows.Media.Visual"/> object is visible.
        /// </summary>
        /// <param name="visual">A <see cref="T:System.Windows.Media.Visual"/> that becomes visible.</param>
        /// <param name="rectangle">A bounding rectangle that identifies the coordinate space to make visible.</param>
        /// <returns>
        /// A <see cref="T:System.Windows.Rect"/> that is visible.
        /// </returns>
        public Rect MakeVisible([NotNull] Visual visual, Rect rectangle)
        {
            Assert.ArgumentNotNull(visual, "visual");

            return rectangle;
        }

        #endregion

        #region TextSelecting

        /// <summary>
        /// Raises the <see cref="E:System.Windows.Controls.Control.MouseDoubleClick"/> routed event.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnMouseDoubleClick([NotNull] MouseButtonEventArgs e)
        {
            Assert.ArgumentNotNull(e, "e");

            base.OnMouseDoubleClick(e);

            _selectionSettings.SelectedMessages.Clear();

            int lineNumber = 0;
            if (_lineHeight > 0)
            {
                lineNumber = _currentLineNumber - (int)Math.Floor((ActualHeight - e.GetPosition(this).Y) / _lineHeight);
            }

            if (_messages.Count >= lineNumber && lineNumber > 0)
            {
                _selectionSettings.SelectedMessages.Add(_messages[lineNumber - 1]);
                _selectionSettings.SelectionStartCharacterNumber = 0;
                _selectionSettings.SelectionEndCharacterNumber = _messages[lineNumber - 1].InnerText.Length;
                try
                {
                    Clipboard.SetText(_messages[lineNumber - 1].InnerText);
                }
                catch
                {
                }

                _doubleClickTimer.Start();
            }

            InvalidateVisual();
            e.Handled = true;
        }

        /// <summary>
        /// Invoked when an unhandled <see cref="E:System.Windows.UIElement.MouseLeftButtonDown"/> routed event is raised on this element. Implement this method to add class handling for this event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.Windows.Input.MouseButtonEventArgs"/> that contains the event data. The event data reports that the left mouse button was pressed.</param>
        protected override void OnMouseLeftButtonDown([NotNull] MouseButtonEventArgs e)
        {
            Assert.ArgumentNotNull(e, "e");

            base.OnMouseLeftButtonDown(e);
            CloseInteractiveToolTip();
            _selectionSettings.Dragging = true;
            _selectionSettings.NeedUpdate = true;
            _selectionSettings.SetSelectionStartPosition(e.GetPosition(this));
            CaptureMouse();
            //e.Handled = true;
        }

        /// <summary>
        /// Invoked when an unhandled <see cref="UIElement.MouseMove"/> attached event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.Windows.Input.MouseEventArgs"/> that contains the event data.</param>
        protected override void OnMouseMove([NotNull] MouseEventArgs e)
        {
            Assert.ArgumentNotNull(e, "e");

            base.OnMouseMove(e);
            if (_selectionSettings.Dragging)
            {
                Mouse.OverrideCursor = Cursors.IBeam;
                _selectionSettings.SetSelectionEndPosition(e.GetPosition(this));
                _selectionSettings.NeedUpdate = true;
                e.Handled = true;
                InvalidateVisual();
                return;
            }

            UpdateInteractiveToolTip(e.GetPosition(this));
        }

        /// <summary>
        /// Invoked when an unhandled <see cref="E:System.Windows.UIElement.MouseLeftButtonUp"/> routed event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.Windows.Input.MouseButtonEventArgs"/> that contains the event data. The event data reports that the left mouse button was released.</param>
        protected override void OnMouseLeftButtonUp([NotNull] MouseButtonEventArgs e)
        {
            Assert.ArgumentNotNull(e, "e");

            base.OnMouseLeftButtonUp(e);

            if (_selectionSettings.Dragging)
            {
                _selectionSettings.Dragging = false;
                CopySelectedMessagesToClipboard();
                ReleaseMouseCapture();
                Mouse.OverrideCursor = null;
                ClearTextSelection();
            }
        }

        /// <summary>
        /// Invoked when an unhandled <see cref="UIElement.MouseLeave"/> attached event is raised on this element. Implement this method to add class handling for this event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.Windows.Input.MouseEventArgs"/> that contains the event data.</param>
        protected override void OnMouseLeave([NotNull] MouseEventArgs e)
        {
            Assert.ArgumentNotNull(e, "e");

            CloseInteractiveToolTip();
            if (!_selectionSettings.Dragging)
            {
                Mouse.OverrideCursor = null;
            }

            base.OnMouseLeave(e);
        }

        /// <summary>
        /// Invoked when an unhandled <see cref="UIElement.LostMouseCapture"/> attached event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.Windows.Input.MouseEventArgs"/> that contains event data.</param>
        protected override void OnLostMouseCapture([NotNull] MouseEventArgs e)
        {
            Assert.ArgumentNotNull(e, "e");

            CloseInteractiveToolTip();
            _selectionSettings.Dragging = false;
            ClearTextSelection();
            base.OnLostMouseCapture(e);
        }

        #endregion

        #region Rendering

        /// <summary>
        /// When overridden in a derived class, participates in rendering operations that are directed by the layout system.
        /// The rendering instructions for this element are not used directly when this method is invoked,
        /// and are instead preserved for later asynchronous use by layout and drawing.
        /// </summary>
        /// <param name="drawingContext">The drawing instructions for a specific element. This context is provided to the layout system.</param>
        protected override void OnRender([NotNull] DrawingContext drawingContext)
        {
            Assert.ArgumentNotNull(drawingContext, "drawingContext");

            try
            {
                if (Visibility != Visibility.Visible)
                {
                    CloseInteractiveToolTip();
                    return;
                }

                _toolTipHotspots.Clear();
                drawingContext.DrawRectangle(Themes.ThemeManager.Instance.ActiveTheme.GetBrushByTextColor(Themes.TextColor.None, true), new Pen(Themes.ThemeManager.Instance.ActiveTheme.GetBrushByTextColor(Themes.TextColor.None, true), 0), new Rect(0, 0, ActualWidth, ActualHeight));
                var renderedLines = 0;
                if (_selectionSettings.NeedUpdate)
                {
                    _tempSelectionSettings.SelectedMessages.Clear();
                }

                if (_messages.Count > 0)
                {
                    var currentHeight = ActualHeight;
                    var lineNumber = _currentLineNumber;
                    while (currentHeight > 0 && lineNumber > 0)
                    {
                        var currentMessage = _messages[lineNumber - 1];
                        _textSource.Message = currentMessage;
                        var textStorePosition = 0;
                        var toolTipRanges = GetToolTipRanges(currentMessage);

                        _linesToRenderStack.Clear();

                        do
                        {
                            var line = _formatter.FormatLine(_textSource, textStorePosition, ActualWidth, _customTextParagraphProperties, null, _textRunCache);
                            _linesToRenderStack.Push(line);
                            textStorePosition += line.Length;
                        }
                        while (textStorePosition < currentMessage.InnerText.Length);

                        var drawnChars = 0;
                        bool messageSelected = false;
                        while (_linesToRenderStack.Count > 0)
                        {
                            var line = _linesToRenderStack.Pop();
                            var lineTop = currentHeight - line.Height;
                            var lineStart = drawnChars;
                            line.Draw(drawingContext, new Point(0, lineTop), InvertAxes.None);

                            if (toolTipRanges.Count > 0)
                            {
                                AddToolTipHotspotsForLine(toolTipRanges, line, lineStart, lineTop);
                            }

                            _lineHeight = line.Height;
                            drawnChars += line.Length;
                            if (_selectionSettings.NeedUpdate)
                            {
                                messageSelected = ProcessLineForSelection(line, currentMessage, currentHeight, drawnChars, messageSelected);
                            }

                            currentHeight -= line.Height;
                            line.Dispose();
                        }

                        renderedLines++;
                        lineNumber--;
                        _textRunCache.Invalidate();
                    }
                }

                _currentNumberOfLinesInView = renderedLines;
                if (_selectionSettings.NeedUpdate)
                {
                    _selectionSettings.SelectionEndCharacterNumber = _tempSelectionSettings.SelectionEndCharacterNumber;
                    _selectionSettings.SelectionStartCharacterNumber = _tempSelectionSettings.SelectionStartCharacterNumber;
                    _selectionSettings.SelectedMessages.Clear();
                    _selectionSettings.SelectedMessages.AddRange(_tempSelectionSettings.SelectedMessages);
                    _selectionSettings.NeedUpdate = false;
                    InvalidateVisual();
                }
            }
            catch (Exception ex)
            {
                _selectionSettings.NeedUpdate = false;
                _textRunCache.Invalidate();
                ClearTextSelection();
                ErrorLogger.Instance.Write(string.Format("Error rendering text {0}", ex));
            }

        }

        /// <summary>
        /// Raises the <see cref="E:System.Windows.FrameworkElement.SizeChanged"/> event, using the specified information as part of the eventual event data.
        /// </summary>
        /// <param name="sizeInfo">Details of the old and new size involved in the change.</param>
        protected override void OnRenderSizeChanged([NotNull] SizeChangedInfo sizeInfo)
        {
            Assert.ArgumentNotNull(sizeInfo, "sizeInfo");

            base.OnRenderSizeChanged(sizeInfo);
            if (ScrollOwner != null)
            {
                ScrollOwner.InvalidateScrollInfo();
            }
        }

        #endregion

        #region Methods

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "Ok")]
        private bool ProcessLineForSelection([NotNull] TextLine line, [NotNull] TextMessage message, double bottomY, int alreadyDrawCharsForMessage, bool messageSelected)
        {
            Assert.ArgumentNotNull(line, "line");
            Assert.ArgumentNotNull(message, "message");

            bool alreadyAdded = _tempSelectionSettings.SelectedMessages.Contains(message);

            double selectionTop = _selectionSettings.SelectionStartPosition.Y;
            double selectionBottom = _selectionSettings.SelectionEndPosition.Y;
            if (selectionBottom < selectionTop)
            {
                selectionTop = _selectionSettings.SelectionEndPosition.Y;
                selectionBottom = _selectionSettings.SelectionStartPosition.Y;
            }

            double selectionLeft = _selectionSettings.SelectionStartPosition.X;
            double selectionRight = _selectionSettings.SelectionEndPosition.X;
            if (selectionLeft > selectionRight)
            {
                selectionLeft = _selectionSettings.SelectionEndPosition.X;
                selectionRight = _selectionSettings.SelectionStartPosition.X;
            }

            if (selectionBottom > ActualHeight)
            {
                selectionBottom = ActualHeight;
            }

            if (selectionTop < 0)
            {
                selectionTop = 0;
            }

            if (selectionRight > ActualWidth)
            {
                selectionRight = ActualWidth;
            }

            if (selectionLeft < 0)
            {
                selectionLeft = 0;
            }

            double lineBottom = bottomY;
            double lineTop = bottomY - line.Height;

            if (lineBottom < selectionBottom && lineTop > selectionTop && alreadyDrawCharsForMessage > 0)
            {
                if (!alreadyAdded)
                {
                    _tempSelectionSettings.SelectedMessages.Add(message);
                }
                return true;
            }

            if ((selectionBottom > lineTop && selectionBottom < lineBottom) && (selectionTop > lineTop && selectionTop < lineBottom))
            {
                var leftCharIndex = line.GetCharacterHitFromDistance(selectionLeft).FirstCharacterIndex;
                var rightCharIndex = line.GetCharacterHitFromDistance(selectionRight).FirstCharacterIndex;
                if (rightCharIndex > leftCharIndex)
                {
                    _tempSelectionSettings.SelectionStartCharacterNumber = leftCharIndex;
                    _tempSelectionSettings.SelectionEndCharacterNumber = rightCharIndex;

                    if (!alreadyAdded)
                    {
                        _tempSelectionSettings.SelectedMessages.Add(message);
                    }
                }

                return true;
            }

            if (selectionTop > lineTop && selectionTop < lineBottom)
            {
                double distance = _selectionSettings.SelectionEndPosition.X;
                if (selectionTop == _selectionSettings.SelectionStartPosition.Y)
                {
                    distance = _selectionSettings.SelectionStartPosition.X;
                }

                var charIndex = line.GetCharacterHitFromDistance(distance).FirstCharacterIndex;
                _tempSelectionSettings.SelectionStartCharacterNumber = charIndex;

                if (!alreadyAdded)
                {
                    _tempSelectionSettings.SelectedMessages.Add(message);
                }

                return true;
            }

            if (selectionBottom > lineTop && selectionBottom < lineBottom)
            {
                double distance = _selectionSettings.SelectionEndPosition.X;
                if (selectionBottom == _selectionSettings.SelectionStartPosition.Y)
                {
                    distance = _selectionSettings.SelectionStartPosition.X;
                }

                var charIndex = line.GetCharacterHitFromDistance(distance).FirstCharacterIndex;
                _tempSelectionSettings.SelectionEndCharacterNumber = charIndex;

                if (!alreadyAdded)
                {
                    _tempSelectionSettings.SelectedMessages.Add(message);
                }
                return true;
            }

            if (alreadyAdded && !messageSelected)
            {
                _tempSelectionSettings.SelectedMessages.Remove(message);
            }

            return messageSelected;
        }

        private void UpdateInteractiveToolTip(Point position)
        {
            if (_toolTipHotspots.Count == 0)
            {
                CloseInteractiveToolTip();
                return;
            }

            ToolTipHotspot hoveredHotspot = null;
            foreach (var hotspot in _toolTipHotspots)
            {
                if (hotspot.Bounds.Contains(position))
                {
                    hoveredHotspot = hotspot;
                    break;
                }
            }

            if (hoveredHotspot == null || IsToolTipEmpty(hoveredHotspot.ToolTipText, hoveredHotspot.ToolTipLines))
            {
                CloseInteractiveToolTip();
                return;
            }

            var isSameToolTip = _interactiveToolTip.IsOpen
                && string.Equals(_shownToolTipText, hoveredHotspot.ToolTipText, StringComparison.Ordinal)
                && ReferenceEquals(_shownToolTipLines, hoveredHotspot.ToolTipLines);
            _interactiveToolTip.HorizontalOffset = position.X + 16;
            _interactiveToolTip.VerticalOffset = position.Y + 16;

            if (!isSameToolTip)
            {
                _shownToolTipText = hoveredHotspot.ToolTipText;
                _shownToolTipLines = hoveredHotspot.ToolTipLines;
                _interactiveToolTip.Content = BuildInteractiveToolTipContent(hoveredHotspot.ToolTipText, hoveredHotspot.ToolTipLines);
                _interactiveToolTip.IsOpen = false;
                _interactiveToolTip.IsOpen = true;
            }
        }

        private void CloseInteractiveToolTip()
        {
            _shownToolTipText = string.Empty;
            _shownToolTipLines = null;
            _interactiveToolTip.IsOpen = false;
        }

        [NotNull]
        private static object BuildInteractiveToolTipContent([CanBeNull] string text, [CanBeNull] IList<TextMessageBlock.ToolTipLine> toolTipLines)
        {
            if (toolTipLines == null || toolTipLines.Count == 0)
            {
                return text ?? string.Empty;
            }

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var activeTheme = ThemeManager.Instance.ActiveTheme;
            var fontFamily = new FontFamily(SettingsHolder.Instance.Settings.MUDFontName);
            var fontSize = SettingsHolder.Instance.Settings.MUDFontSize + 2;

            foreach (var toolTipLine in toolTipLines)
            {
                var lineTextBlock = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = fontFamily,
                    FontSize = fontSize,
                    Foreground = activeTheme.GetBrushByTextColor(TextColor.BrightWhite, false)
                };

                if (toolTipLine != null && toolTipLine.Blocks != null)
                {
                    foreach (var block in toolTipLine.Blocks)
                    {
                        if (block == null || string.IsNullOrEmpty(block.Text))
                        {
                            continue;
                        }

                        var run = new Run(block.Text)
                        {
                            Foreground = activeTheme.GetBrushByTextColor(block.Foreground, false)
                        };
                        if (block.Background != TextColor.None)
                        {
                            run.Background = activeTheme.GetBrushByTextColor(block.Background, true);
                        }

                        lineTextBlock.Inlines.Add(run);
                    }
                }

                stackPanel.Children.Add(lineTextBlock);
            }

            return stackPanel;
        }

        private static bool IsToolTipEmpty([CanBeNull] string text, [CanBeNull] IList<TextMessageBlock.ToolTipLine> toolTipLines)
        {
            return string.IsNullOrWhiteSpace(text) && (toolTipLines == null || toolTipLines.Count == 0);
        }

        [NotNull]
        private static List<ToolTipRange> GetToolTipRanges([NotNull] TextMessage message)
        {
            Assert.ArgumentNotNull(message, "message");

            var result = new List<ToolTipRange>();
            var currentOffset = 0;
            foreach (var block in message.MessageBlocks)
            {
                var blockLength = block.Text.Length;
                if (blockLength == 0)
                {
                    continue;
                }

                if (!IsToolTipEmpty(block.ToolTipText, block.ToolTipLines))
                {
                    result.Add(new ToolTipRange
                    {
                        Start = currentOffset,
                        End = currentOffset + blockLength,
                        ToolTipText = block.ToolTipText,
                        ToolTipLines = block.ToolTipLines
                    });
                }

                currentOffset += blockLength;
            }

            return result;
        }

        private void AddToolTipHotspotsForLine([NotNull] IEnumerable<ToolTipRange> toolTipRanges, [NotNull] TextLine line, int lineStart, double lineTop)
        {
            Assert.ArgumentNotNull(toolTipRanges, "toolTipRanges");
            Assert.ArgumentNotNull(line, "line");

            var lineLength = line.Length;
            var lineEnd = lineStart + lineLength;
            if (lineLength <= 0)
            {
                return;
            }

            foreach (var toolTipRange in toolTipRanges)
            {
                if (toolTipRange.End <= lineStart || toolTipRange.Start >= lineEnd)
                {
                    continue;
                }

                var overlapStart = Math.Max(lineStart, toolTipRange.Start);
                var overlapEnd = Math.Min(lineEnd, toolTipRange.End);
                if (overlapEnd <= overlapStart)
                {
                    continue;
                }

                var startInLine = overlapStart - lineStart;
                var endInLine = overlapEnd - lineStart;

                double xStart;
                double xEnd;
                try
                {
                    xStart = line.GetDistanceFromCharacterHit(new CharacterHit(startInLine, 0));
                    xEnd = line.GetDistanceFromCharacterHit(new CharacterHit(endInLine, 0));
                }
                catch
                {
                    continue;
                }

                if (xEnd < xStart)
                {
                    var temp = xStart;
                    xStart = xEnd;
                    xEnd = temp;
                }

                var width = Math.Max(1.0, xEnd - xStart);
                _toolTipHotspots.Add(new ToolTipHotspot
                {
                    Bounds = new Rect(xStart, lineTop, width, line.Height),
                    ToolTipText = toolTipRange.ToolTipText,
                    ToolTipLines = toolTipRange.ToolTipLines
                });
            }
        }

        private void ClearTextSelection()
        {
            _selectionSettings.SelectedMessages.Clear();
            _doubleClickTimer.Stop();
            InvalidateVisual();
        }

        private void CopySelectedMessagesToClipboard()
        {

            try
            {
                string result = string.Empty;
                if (_selectionSettings.SelectedMessages.Count == 1)
                {
                    var text = _selectionSettings.SelectedMessages[0].InnerText;
                    if (_selectionSettings.SelectionStartCharacterNumber < text.Length)
                    {
                        var length = Math.Min(text.Length, _selectionSettings.SelectionEndCharacterNumber);
                        result = text.Substring(_selectionSettings.SelectionStartCharacterNumber, length - _selectionSettings.SelectionStartCharacterNumber);
                    }
                }

                if (_selectionSettings.SelectedMessages.Count > 1)
                {
                    var text = _selectionSettings.SelectedMessages[_selectionSettings.SelectedMessages.Count - 1].InnerText;
                    result = text.Substring(Math.Min(_selectionSettings.SelectionStartCharacterNumber, text.Length));

                    for (int i = _selectionSettings.SelectedMessages.Count - 2; i > 0; i--)
                    {
                        result += "\r\n";
                        result += _selectionSettings.SelectedMessages[i].InnerText;
                    }

                    result += "\r\n";
                    text = _selectionSettings.SelectedMessages[0].InnerText;
                    var length = Math.Min(text.Length, _selectionSettings.SelectionEndCharacterNumber);
                    result += text.Substring(0, length);
                }

                Clipboard.SetText(result);
            }
            catch (Exception ex)
            {
                ClearTextSelection();
                ErrorLogger.Instance.Write(string.Format("Error copying text {0}", ex));
            }
        }

        private void CheckMessagesOverflow()
        {
            var currentNumberOfMessages = _messages.Count;
            if (currentNumberOfMessages > MaximumLinesToStore * (100 + LinesOverflowPercentBeforeCleanup) / 100.0)
            {
                _messages.RemoveRange(0, currentNumberOfMessages - MaximumLinesToStore);
                _currentLineNumber = _currentLineNumber - (currentNumberOfMessages - _messages.Count);
                if (_currentLineNumber <= 0)
                {
                    _currentLineNumber = 1;
                }

                InvalidateVisual();
                if (ScrollOwner != null)
                {
                    ScrollOwner.InvalidateScrollInfo();
                }
            }
        }

        private void HandleSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            if (e.Name == "MUDFontName" || e.Name == "MUDFontSize" || e.Name == "ColorTheme")
            {
                _textRunCache.Invalidate();
                InvalidateVisual();
            }

            if (e.Name == "ScrollBuffer")
            {
                MaximumLinesToStore = Math.Max(SettingsHolder.Instance.Settings.ScrollBuffer, 100);
            }
        }

        private sealed class ToolTipRange
        {
            public int Start
            {
                get;
                set;
            }

            public int End
            {
                get;
                set;
            }

            public string ToolTipText
            {
                get;
                set;
            }

            public IList<TextMessageBlock.ToolTipLine> ToolTipLines
            {
                get;
                set;
            }
        }

        private sealed class ToolTipHotspot
        {
            public Rect Bounds
            {
                get;
                set;
            }

            public string ToolTipText
            {
                get;
                set;
            }

            public IList<TextMessageBlock.ToolTipLine> ToolTipLines
            {
                get;
                set;
            }
        }

        #endregion
    }
}
