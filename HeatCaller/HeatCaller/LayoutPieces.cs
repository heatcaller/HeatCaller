using System.Runtime.CompilerServices;

namespace HeatCaller;

// THE LAYOUT PIECES
//
// What pages are rebuilt on, one at a time, in place of hand-placed
// coordinates. Every page in this app was laid out in pixels chosen by eye
// against the font of the day, and LayoutDoctor has been nudging them back
// into shape at runtime ever since the font moved. These pieces measure
// instead: a caption column as wide as its widest caption, a button as wide
// as its own caption in the font it is drawn in, a hint that wraps to the
// room it has, a row of buttons that wraps when the window is narrow, a list
// that takes the height that is left. Change the text size, or the window,
// and a page built from them re-measures itself - nothing to repair.
//
// Built on WinForms' own TableLayoutPanel and FlowLayoutPanel, not on a
// layout engine of our own - those two already know how to ask a control
// how big it wants to be at a given width, which is the whole trick.
//
//   PageStack   a page's content top to bottom, scrolling when it is taller
//               than the window.
//   FillPage    a page with one part - a list, the leaderboard - that takes
//               whatever height the rest leave, and scrolls rather than
//               squash it past the least it can do with.
//   Section     a card: the caption in small spaced capitals, then content.
//   FieldGrid   caption-and-field rows, the captions in one measured column
//               and the fields lined up beside them, with hints under them.
//   ButtonBar   buttons sized to their captions, wrapping onto a second row
//               when there is not room for one.
//   FieldRow    caption-and-field pairs along a line, the last field taking
//               the width left, wrapping when the line is too narrow.
//   Beside      a column down the right-hand side of the page's main parts -
//               Race Control's clock beside its flags and leaderboard.
//   SplitBar    two rows of buttons on one line, one against each edge.
//   SteadyLabel a label as tall as the longest thing it can say, so
//               rewording it never moves what is under it.
//   StatusLine  a coloured light and a line of text beside it that wraps -
//               the decoder's status on the Connection page.
//   TextSize    what a page keeps in step with the text size: the controls
//               that wear a face of their own, and anything sized from the
//               fonts.
//
// The rules, so a page built from these stays built from them:
//   - Nothing gets a Location. The piece places it.
//   - Nothing gets a width from a literal unless it is a field sized for
//     what goes IN it (a spinner for three digits), and then at the text
//     size in use (UiLayout.AtTextSize). Captions, buttons, hints and the
//     choices in a drop-down are measured.
//   - A page that uses these goes on LayoutAudit.ConvertedPages, and the
//     layout test holds it to nothing overlapping or cut off at every size.
//
// All of them keep the colour of whatever they are put in (they carry
// AppTheme.KeepBackColor, so the theme pass leaves them be), except Section,
// which is a card and paints its own.

/// <summary>
/// A page's content from top to bottom: sections, grids, rows of buttons,
/// each under the last. Fills its page, and scrolls when the content is
/// taller than the window - down only; nothing in it is ever wider than
/// the page, so it never scrolls sideways.
/// </summary>
internal sealed class PageStack : Panel
{
    private readonly StackColumn _column;

    public PageStack()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(UiLayout.PageLeft, UiLayout.PageTop, UiLayout.PageRight, UiLayout.PageTop);
        Tag = AppTheme.KeepBackColor;

        _column = new StackColumn();
        Controls.Add(_column);

        // After the column exists: switching the sideways bar off lays the
        // panel out there and then.
        Pieces.ScrollDownOnly(this);
    }

    /// <summary>
    /// Adds a part under the last, as wide as the page - or, with
    /// <paramref name="stretch"/> false, as wide as it wants to be, on the
    /// left. <paramref name="gapBelow"/> is the space before the next part.
    /// </summary>
    public T Add<T>(T part, bool stretch = true, int gapBelow = UiLayout.SectionGap) where T : Control
    {
        Pieces.PlaceInColumn(_column, part, stretch, gapBelow);
        return part;
    }

    /// <summary>
    /// How tall the page would have to be to show all of it without
    /// scrolling, at the width it has - for a host that sizes the page to
    /// fit, as MainForm sizes Next Steps' half of its split.
    ///
    /// The top margin, the column, and under it the bottom margin or the
    /// theme's room under the last control (AutoScrollMargin), whichever is
    /// more. WinForms counts that room, not the bottom margin, when it
    /// decides whether to scroll - the column is placed by hand, and the
    /// margin is only added for the old DockPadding - so a page sized to the
    /// two margins alone still scrolled by the difference.
    /// </summary>
    public int ContentHeight { get; private set; }

    /// <summary>
    /// Raised when <see cref="ContentHeight"/> changes. Raised from inside
    /// this page's layout, so a handler that answers by resizing the page
    /// has to wait for the layout to finish (BeginInvoke) - WinForms drops
    /// a layout asked for while one is running.
    /// </summary>
    public event Action? ContentHeightChanged;

    /// <summary>
    /// Places the column: the page's width, less its margins, and as tall as
    /// its content is at that width.
    ///
    /// Placed by hand rather than docked, and that is not a style choice. A
    /// scrolling panel leaves docked controls out of its scrolling range
    /// entirely (ScrollableControl skips them), so a column docked to the
    /// top would never scroll however tall it grew. Nor does it size itself:
    /// a table that sizes itself does it against no width at all, and every
    /// hint in it would stretch out on one line rather than wrap to the
    /// page.
    /// </summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        // Laid out already while the constructor set the docking and the
        // margins, before the column was there to place.
        if (_column is null)
        {
            base.OnLayout(levent);
            return;
        }

        PlaceColumn();

        // The scrolling range follows the column - and may bring the bar in
        // or take it away, which changes the width the column should have.
        //
        // And placing the column at a new width lays it out, which is when
        // a field grid in it learns what its rows really come to - after
        // the column was measured for the placing (see StackColumn's
        // OnLayout, whose own request to be placed again is dropped while
        // this runs). So it is placed until it stays put: the bar going
        // away, then the grids' real heights, when a page that scrolled is
        // widened past needing to (LayoutPiecesTests, narrow and then wide
        // again, 29 Sep - the card at the bottom hung 9px past the column).
        // Three times at most, so a column that could not settle cannot
        // hang the window.
        base.OnLayout(levent);

        for (var again = 0; again < 3 && PlaceColumn(); again++)
        {
            base.OnLayout(levent);
        }

        var content = Padding.Top + _column.Height + Math.Max(Padding.Bottom, AutoScrollMargin.Height);

        if (ContentHeight != content)
        {
            ContentHeight = content;
            ContentHeightChanged?.Invoke();
        }
    }

    /// <summary>Puts the column where it belongs now. True when that moved or resized it.</summary>
    private bool PlaceColumn()
    {
        var width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
        var height = _column.GetPreferredSize(new Size(width, 0)).Height;

        // Scrolled: the column's top is above the panel's by however far.
        var bounds = new Rectangle(
            Padding.Left + AutoScrollPosition.X,
            Padding.Top + AutoScrollPosition.Y,
            width,
            height);

        if (_column.Bounds == bounds)
        {
            return false;
        }

        _column.Bounds = bounds;
        return true;
    }

    /// <summary>
    /// The one-column table the parts go in. It tells the stack when what is
    /// in it has grown or shrunk - a hint rewritten, a row shown - since a
    /// control the stack places itself is not one WinForms re-measures.
    ///
    /// It tells it FIRST, before laying anything out. The stack answers by
    /// resizing this table, and WinForms drops a layout asked for while one
    /// is already running (Control.PerformLayout clears it on the way out) -
    /// so a table that laid itself out and then got resized would keep the
    /// old layout. Told first, it is resized, and then lays out once, at the
    /// size it ends up.
    /// </summary>
    private sealed class StackColumn : TableLayoutPanel
    {
        public StackColumn()
        {
            ColumnCount = 1;
            RowCount = 0;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Tag = AppTheme.KeepBackColor;

            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        }

        /// <summary>
        /// As tall as the parts need at the width proposed, worked out here
        /// part by part, as Section works its own height out, and for the
        /// same reason: asked, the table kept the height its parts had grown
        /// to while the page was narrow after it was widened again
        /// (dotnet/winforms issue 10379) - a page that scrolled on into empty
        /// space, and Next Steps' half of its split held open for content
        /// that was no longer there (LayoutPiecesTests, 29 Sep).
        /// </summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width;
            var total = 0;

            foreach (Control part in Controls)
            {
                // As in Pieces.TrailingGap.
                if (Visible && !part.Visible || part is ThinScrollBar)
                {
                    continue;
                }

                var inside = Math.Max(1, width - part.Margin.Horizontal);

                var height = Pieces.SizesItself(part)
                    ? part.GetPreferredSize(new Size(inside, 0)).Height
                    : part.Height;

                total += height + part.Margin.Vertical;
            }

            return new Size(width, total);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (Parent is PageStack stack && Height != GetPreferredSize(new Size(Width, 0)).Height)
            {
                stack.PerformLayout();
            }

            base.OnLayout(levent);

            if (Pieces.OutOfStep(this))
            {
                base.OnLayout(levent);
            }

            // And again after: laying the parts out is itself what can show
            // that one of them is taller than it said - a field grid learns
            // the height its rows really come to only by laying them out
            // (FieldGrid.GetPreferredSize), and the card it is in grows to
            // it here (Pieces.OutOfStep, above) - and asked before, the
            // stack placed this at the height it was told. A card was left
            // hanging 9px past the bottom of the column, and the page
            // scrolling 9px short of it (LayoutPiecesTests, 29 Sep). When the stack is
            // itself laying out, the request is dropped and it places this
            // again once its layout is done. If a scrollbar arriving takes
            // the width as well, the layout that asks for is dropped, this
            // one running - so it is done here.
            if (Parent is PageStack after && Height != GetPreferredSize(new Size(Width, 0)).Height)
            {
                var before = Size;

                after.PerformLayout();

                if (Size != before)
                {
                    base.OnLayout(levent);
                }
            }
        }
    }
}

/// <summary>
/// A page with one part that takes whatever height is left - a grid, a
/// list, the leaderboard - between parts that are only as tall as they
/// need to be.
///
/// When the window is too short for the other parts AND the least the
/// filling part can do with, the page scrolls instead: a list squashed to
/// a sliver is the one outcome worse than a scrollbar.
/// </summary>
internal sealed class FillPage : Panel
{
    private readonly FillRows _rows;

    /// <summary>The filling parts, with the least height each can do with at the width it is given.</summary>
    private readonly Dictionary<Control, Func<int, int>> _leastHeights = new();

    /// <summary>
    /// Raised when the least height the page can do with has changed - a
    /// part wrapping onto another line, a new text size - for a host that
    /// sizes the page, as Next Steps' split sizes the program under it.
    ///
    /// Raised from inside the page's own layout, so a host that answers by
    /// resizing the page must do it afterwards (BeginInvoke): WinForms drops
    /// a layout asked for while one is running.
    /// </summary>
    public event Action? LeastHeightChanged;

    public FillPage()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(UiLayout.PageLeft, UiLayout.PageTop, UiLayout.PageRight, UiLayout.PageTop);
        Tag = AppTheme.KeepBackColor;

        _rows = new FillRows { Dock = DockStyle.Fill };
        Controls.Add(_rows);

        // After the rows exist, for the same reason as PageStack's.
        Pieces.ScrollDownOnly(this);
    }

    /// <summary>Adds a part that is as tall as it needs to be. See PageStack.Add.</summary>
    public T Add<T>(T part, bool stretch = true, int gapBelow = UiLayout.SectionGap) where T : Control
    {
        Pieces.PlaceInColumn(_rows, part, stretch, gapBelow);
        return part;
    }

    /// <summary>
    /// Adds a part above all the others, as tall as it needs to be - for a
    /// part the page's host owns and hands over after the page is built, as
    /// MainForm does Race Control's Session row.
    /// </summary>
    public T AddFirst<T>(T part, bool stretch = true, int gapBelow = UiLayout.SectionGap) where T : Control
    {
        Pieces.PlaceInColumnAt(_rows, part, stretch, gapBelow, row: 0);
        return part;
    }

    /// <summary>
    /// Adds a part that takes the height left over, never less than
    /// <paramref name="leastHeight"/>. Two of them share what is left
    /// equally.
    ///
    /// The part goes in a plain panel of its own, filling it, rather than
    /// straight into the table. A list or grid gets a themed scrollbar
    /// parked beside it (ThinScrollBar), and that has to be somewhere that
    /// leaves it where it is put: in the table it would be a row, and in
    /// this page it would count towards the scrolling range it sits in,
    /// which then grows with every layout.
    ///
    /// So to take the part off the page, hide its holder (part.Parent), not
    /// the part: a hidden part leaves an empty holder keeping its row.
    /// </summary>
    public T AddFill<T>(T part, int leastHeight, int gapBelow = UiLayout.SectionGap) where T : Control =>
        AddFill(part, _ => leastHeight, gapBelow);

    /// <summary>
    /// The same, for a part whose least height depends on the width it is
    /// given or on the text size - a Beside, or a Section with a filling
    /// part of its own. <paramref name="leastHeight"/> is asked with the
    /// width the part will have, whenever the page works out its scrolling
    /// range.
    /// </summary>
    public T AddFill<T>(T part, Func<int, int> leastHeight, int gapBelow = UiLayout.SectionGap) where T : Control
    {
        var holder = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, Math.Max(0, gapBelow)),
            Padding = Padding.Empty,
            Tag = AppTheme.KeepBackColor,
        };

        part.Dock = DockStyle.Fill;
        part.Margin = Padding.Empty;
        holder.Controls.Add(part);

        // Something in it growing or shrinking - a filter row in a card
        // wrapping onto a second line - can change the least it can do with,
        // and the table here only sees the holder, whose size has not
        // changed. Asked as the part's layout starts - see
        // LeastHeightMayHaveChanged.
        part.Layout += (_, _) => LeastHeightMayHaveChanged();

        _leastHeights[holder] = leastHeight;

        _rows.RowCount++;
        _rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _rows.Controls.Add(holder, 0, _rows.RowCount - 1);

        return part;
    }

    /// <summary>
    /// Keeps the scrolling range at the least height the parts can do with.
    ///
    /// The rows are docked to fill this panel, and a scrolling panel leaves
    /// docked controls out of its scrolling range - so the range is given
    /// to it outright, as AutoScrollMinSize. Past it, the panel scrolls and
    /// its DisplayRectangle stops shrinking, so the docked rows, and the
    /// filling part in them, stop shrinking where the scrollbar starts.
    /// Both margins are counted: the rows are laid out inside them, and
    /// WinForms adds the bottom one to the range only for the old
    /// DockPadding. The theme's room under the last control
    /// (AutoScrollMargin) comes on top, as spare. Only ever the height:
    /// nothing here scrolls sideways.
    /// </summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        UpdateScrollRange();

        // The range was worked out at the width the panel had before this
        // layout - and the layout may bring the scrollbar in or take it
        // away, which changes the width, which changes how tall anything
        // that wraps is; and laying the parts out at a new width is when a
        // field grid in them learns what its rows really come to. Again
        // until it stays put, as PageStack does - three times at most.
        base.OnLayout(levent);

        for (var again = 0; again < 3 && UpdateScrollRange(); again++)
        {
            base.OnLayout(levent);
        }
    }

    /// <summary>
    /// Sets the scrolling range to what the parts need now. True when that
    /// changed it.
    ///
    /// Only while on screen: on a hidden page every part reports itself
    /// hidden, and the range would be worked out for an empty page - the
    /// page coming into view lays it out again (OnVisibleChanged). And not
    /// before the rows exist: setting the docking in the constructor lays
    /// out too.
    /// </summary>
    private bool UpdateScrollRange()
    {
        if (!Visible || _rows is null)
        {
            return false;
        }

        var least = Padding.Vertical + RowsHeight(Math.Max(1, ClientSize.Width - Padding.Horizontal));

        if (AutoScrollMinSize.Height == least && AutoScrollMinSize.Width == 0)
        {
            return false;
        }

        // Its own request for a layout is dropped - this is already one.
        AutoScrollMinSize = new Size(0, least);
        LeastHeightChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// The height the page needs at <paramref name="width"/> to show all of
    /// its parts at their least without scrolling - for a host that wants
    /// the whole page in view, as Next Steps' split does the program. See
    /// LeastHeightChanged.
    ///
    /// That is the scrolling range (see OnLayout) and the theme's room under
    /// the last control on top of it (AutoScrollMargin, which the theme pass
    /// gives every page that scrolls - LayoutDoctor.ThemeScrolling): WinForms
    /// adds that room to AutoScrollMinSize, and a page shorter than the two
    /// together scrolls, by as much as the room.
    /// </summary>
    public int LeastHeight(int width) =>
        Padding.Vertical + RowsHeight(Math.Max(1, width - Padding.Horizontal)) + AutoScrollMargin.Height;

    /// <summary>
    /// Coming into view - its page shown - is when the range can first be
    /// worked out (see OnLayout), and nothing else lays this panel out then
    /// unless the page also changed size.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            PerformLayout();
        }
    }

    /// <summary>
    /// Lays the page out again if the least its parts can do with is no
    /// longer the scrolling range it has.
    ///
    /// For something inside a filling part that has grown or shrunk - a
    /// line of text in a Beside wrapping onto a second line. The table here
    /// does not see that happen: it holds the part's holder, whose size has
    /// not changed. So the piece it happened in asks, and asks FIRST, before
    /// laying itself out - for the reason PageStack's column tells its stack.
    /// </summary>
    internal void LeastHeightMayHaveChanged()
    {
        if (Visible && _rows is not null && AutoScrollMinSize.Height != Padding.Vertical + RowsHeight(Math.Max(1, ClientSize.Width - Padding.Horizontal)))
        {
            PerformLayout();
        }
    }

    /// <summary>
    /// The height the rows can least do with, <paramref name="width"/> wide
    /// inside the margins: the others as tall as they need, the filling ones
    /// at their least.
    /// </summary>
    private int RowsHeight(int width)
    {
        var total = 0;

        foreach (Control part in _rows.Controls)
        {
            // A themed scrollbar is parked outside the tables (ThinScrollBar
            // .HostFor), but one is never a part either way. Hidden in its
            // own right leaves a part out - askable only while the page is
            // on screen, as in Pieces.TrailingGap: while it is hidden, every
            // part reports itself hidden.
            if ((Visible && !part.Visible) || part is ThinScrollBar)
            {
                continue;
            }

            var height = _leastHeights.TryGetValue(part, out var leastHeight)
                ? Math.Max(0, leastHeight(Math.Max(1, width - part.Margin.Horizontal)))
                : Pieces.SizesItself(part)
                    ? part.GetPreferredSize(new Size(Math.Max(1, width - part.Margin.Horizontal), 0)).Height
                    : part.Height;

            total += height + part.Margin.Vertical;
        }

        return total;
    }

    /// <summary>
    /// The table the parts go in. It tells the page when what is in it has
    /// grown or shrunk, so the scrolling range follows - first, before
    /// laying anything out, for the reason PageStack's column does.
    /// </summary>
    private sealed class FillRows : TableLayoutPanel
    {
        public FillRows()
        {
            ColumnCount = 1;
            RowCount = 0;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Tag = AppTheme.KeepBackColor;

            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            (Parent as FillPage)?.LeastHeightMayHaveChanged();

            base.OnLayout(levent);

            if (Pieces.OutOfStep(this))
            {
                base.OnLayout(levent);
            }

            // And after, for the reason PageStack's column asks again: laying
            // a part out can be what shows it is taller than it said. The
            // page answering can resize this - a scrollbar arriving takes
            // its width - and the layout that resize asks for is dropped,
            // this one running; so it is done here.
            var before = Size;

            (Parent as FillPage)?.LeastHeightMayHaveChanged();

            if (Size != before)
            {
                base.OnLayout(levent);
            }
        }
    }
}

/// <summary>
/// A card: the theme's rounded surface, the caption in small spaced
/// capitals, and the content under it, each part as wide as the card.
///
/// The same card the group boxes are painted as (AppTheme.StyleGroupBox),
/// built the other way round. A group box's caption is painted into its
/// frame at a fixed height and its content placed below by hand, and at a
/// larger text size the two meet; here the caption is a label in the first
/// row, and everything under it moves when it grows.
/// </summary>
internal sealed class Section : TableLayoutPanel
{
    /// <summary>Room inside the card's edge.</summary>
    public static readonly Padding Inset = new(16, 10, 16, 14);

    private readonly Label? _caption;

    /// <summary>The panel holding the part that takes the height left over, when there is one - see AddFill.</summary>
    private Control? _fillHolder;

    /// <summary>The least height that part can do with.</summary>
    private Func<int>? _fillLeast;

    public Section(string caption = "")
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        RowCount = 0;
        Margin = Padding.Empty;
        Padding = Inset;
        BackColor = AppTheme.Surface;
        Tag = AppTheme.KeepBackColor;
        DoubleBuffered = true;
        ResizeRedraw = true;

        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        if (!string.IsNullOrWhiteSpace(caption))
        {
            // The same treatment the card's painted caption gets: upper case,
            // spaced when short enough for the spacing to help.
            var text = caption.Trim().ToUpperInvariant();

            _caption = new Label
            {
                Text = text.Length <= 28 ? AppTheme.Spaced(text) : text,
                AutoSize = true,
                Font = AppTheme.MicroFont,
                ForeColor = AppTheme.TextMuted,
                UseMnemonic = false,
                AccessibleName = caption.Trim(),
            };

            Pieces.PlaceInColumn(this, _caption, stretch: true, gapBelow: UiLayout.HeadingGap);
        }
    }

    /// <summary>Adds a part under the last. See PageStack.Add.</summary>
    public T Add<T>(T part, bool stretch = true, int gapBelow = UiLayout.RowGap) where T : Control
    {
        Pieces.PlaceInColumn(this, part, stretch, gapBelow);
        return part;
    }

    /// <summary>
    /// Adds a part that takes whatever height the card has left - the list
    /// in a card that fills its page, Race Control's leaderboard. One only.
    ///
    /// The card stops sizing itself then: whatever holds it gives it its
    /// height (FillPage.AddFill or Beside.AddFill, with <see cref="LeastHeight"/>
    /// as the least), and this part takes what the others leave. It goes in
    /// a plain panel of its own, for the reason FillPage.AddFill gives.
    ///
    /// Give the last part of such a card a gapBelow of 0. A card that sizes
    /// itself leaves the gap under its last part out (see GetPreferredSize);
    /// one given its height keeps it inside, under the inset.
    /// </summary>
    /// <param name="leastHeight">The least the part can do with, asked whenever it is needed - so it can follow the text size.</param>
    public T AddFill<T>(T part, Func<int> leastHeight, int gapBelow = UiLayout.RowGap) where T : Control
    {
        if (_fillHolder is not null)
        {
            throw new InvalidOperationException("A card has one part that takes the height left over.");
        }

        AutoSize = false;

        var holder = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, Math.Max(0, gapBelow)),
            Padding = Padding.Empty,
            Tag = AppTheme.KeepBackColor,
        };

        part.Dock = DockStyle.Fill;
        part.Margin = Padding.Empty;
        holder.Controls.Add(part);

        _fillHolder = holder;
        _fillLeast = leastHeight;

        RowCount++;
        RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Controls.Add(holder, 0, RowCount - 1);

        return part;
    }

    /// <summary>
    /// How wide the caption is on one line - for a column that should never
    /// be narrower than its card's caption. Narrower, the caption wraps, and
    /// between its spaced letters rather than its words: "CHAMPIONSHIP" over
    /// a lone "S" (layout pictures, 29 Sep). 0 for a card without one.
    /// </summary>
    public int CaptionWidth => _caption is null ? 0 : _caption.GetPreferredSize(Size.Empty).Width + _caption.Margin.Horizontal;

    /// <summary>
    /// The least height this card can do with at <paramref name="width"/>:
    /// its inset, every part as tall as it needs to be at that width with
    /// the gap under it, and the part added with AddFill at its least - for
    /// a card that is given its height, which keeps the gap under its last
    /// part inside it. A card that sizes itself is this less that gap (see
    /// GetPreferredSize).
    /// </summary>
    public int LeastHeight(int width)
    {
        var inside = Math.Max(1, width - Padding.Horizontal);
        var total = Padding.Vertical;

        foreach (var part in Parts())
        {
            var height = ReferenceEquals(part, _fillHolder) && _fillLeast is not null
                ? Math.Max(0, _fillLeast())
                : Pieces.SizesItself(part)
                    ? part.GetPreferredSize(new Size(Math.Max(1, inside - part.Margin.Horizontal), 0)).Height
                    : part.Height;

            total += height + part.Margin.Vertical;
        }

        return total;
    }

    /// <summary>
    /// As tall as the parts need at the width proposed - worked out here,
    /// part by part, rather than by the table underneath.
    ///
    /// A TableLayoutPanel that sizes itself grows when what is in it wraps
    /// onto more lines, and does not shrink again when it unwraps
    /// (dotnet/winforms issue 10379). Race Control's cards were made while
    /// the page was narrow and then widened, and kept the height of a
    /// two-line flag row, and of a three-line Session row, around one line.
    /// Each part is asked how tall it is at the width it will have - which
    /// is how the table lays the rows out inside the card, so the card and
    /// its rows agree - and nothing is remembered from last time.
    ///
    /// The gap under the last part is the card's own inset's job, not the
    /// part's - see Pieces.TrailingGap. With no width proposed, the card is
    /// as wide as its widest part on one line.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4
            ? proposedSize.Width
            : NaturalWidth();

        return new Size(width, Math.Max(0, LeastHeight(width) - Pieces.TrailingGap(this)));
    }

    /// <summary>The card's width with every part at the width it wants, on one line.</summary>
    private int NaturalWidth()
    {
        var widest = 0;

        foreach (var part in Parts())
        {
            var width = Pieces.SizesItself(part) ? part.GetPreferredSize(Size.Empty).Width : part.Width;

            widest = Math.Max(widest, width + part.Margin.Horizontal);
        }

        return widest + Padding.Horizontal;
    }

    /// <summary>
    /// The parts that take a row. Hidden in its own right leaves its row
    /// out, as the table does - see Pieces.TrailingGap for why that is only
    /// askable while the card itself is on screen.
    /// </summary>
    private IEnumerable<Control> Parts() =>
        Controls.Cast<Control>().Where(part => (!Visible || part.Visible) && part is not ThinScrollBar);

    /// <summary>See Pieces.OutOfStep.</summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        if (Pieces.OutOfStep(this))
        {
            base.OnLayout(levent);
        }
    }

    /// <summary>
    /// The micro font is replaced, not resized, when the text size changes,
    /// and the caption would keep the old one. The window's own font change
    /// reaches this card (it has none of its own), and is the cue.
    /// </summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        if (_caption is not null && !Equals(_caption.Font, AppTheme.MicroFont))
        {
            _caption.Font = AppTheme.MicroFont;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // The corners show whatever the card sits on, or the rounding reads
        // as a notch.
        using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            e.Graphics.FillRectangle(behind, ClientRectangle);
        }

        AppTheme.DrawCard(e.Graphics, new RectangleF(0, 0, Width, Height));
    }
}

/// <summary>
/// Caption-and-field rows. The captions share one column, as wide as the
/// widest of them at the text size in use, and the fields line up beside
/// them - the thing that makes a settings page look designed rather than
/// assembled, measured rather than set at 180px and hoped for. Grids in
/// separate cards can share one caption column (LineUp), so a page split
/// into cards still lines up down its length.
///
/// A caption is centred on its field - or, beside a field more than a line
/// tall, level with its first line: a list, a box of notes, a row of
/// controls that has wrapped onto a second line (see FirstLine). A
/// hint goes under the field it is about, in the field's column, and wraps
/// to the room there.
/// </summary>
internal sealed class FieldGrid : TableLayoutPanel
{
    /// <summary>The controls of the last row added, whose gap below a hint takes the place of.</summary>
    private readonly List<Control> _lastRow = new();

    /// <summary>The hints, which wear the small font rather than this grid's.</summary>
    private readonly List<Label> _hints = new();

    /// <summary>The captions, which LineUp gives a least width.</summary>
    private readonly List<Label> _captions = new();

    /// <summary>Each caption and the field it names - see LevelWithFirstLines.</summary>
    private readonly List<(Label Caption, Control Field)> _pairs = new();

    /// <summary>
    /// How tall the rows came to when the table last laid them out, and
    /// the width it did that at - see GetPreferredSize.
    /// </summary>
    private (int Width, int Height)? _laidOut;

    public FieldGrid()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 2;
        RowCount = 0;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;

        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
    }

    /// <summary>
    /// A caption and the field it names. With <paramref name="stretch"/> the
    /// field runs to the right-hand edge; without, it keeps its own width.
    /// For more than one control beside a caption, pass <see cref="Inline"/>.
    /// Returns the caption, for a tooltip or to hide the row with.
    /// </summary>
    public Label AddRow(string caption, Control field, bool stretch = false)
    {
        var label = new Label
        {
            Text = caption,
            AutoSize = true,

            // Left only: centred up and down on the row, which is as tall
            // as the field.
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, UiLayout.LabelGap, UiLayout.RowGap),
        };

        PlaceField(field, stretch);

        RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(label, 0, RowCount - 1);
        Controls.Add(field, 1, RowCount - 1);

        _lastRow.Clear();
        _lastRow.Add(label);
        _lastRow.Add(field);
        _captions.Add(label);
        _pairs.Add((label, field));

        return label;
    }

    /// <summary>
    /// A field with no caption of its own, in the field column under the
    /// fields above it - a check box that goes with them ("Only include
    /// drivers who have checked in"), lined up with them rather than with
    /// the captions.
    /// </summary>
    public T AddField<T>(T field, bool stretch = false) where T : Control
    {
        PlaceField(field, stretch);

        RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(field, 1, RowCount - 1);

        _lastRow.Clear();
        _lastRow.Add(field);

        return field;
    }

    /// <summary>A field's anchor and gap. An inline row wraps to the width it is given, so it has to be given the column's.</summary>
    private static void PlaceField(Control field, bool stretch)
    {
        var stretched = stretch || field is FlowLayoutPanel;

        // A box the theme outlines has its ring drawn a pixel outside it,
        // on this grid (InputOutline) - so a pixel is kept for it inside the
        // grid. Without it the ring round a box in the first row lost its
        // top line to the grid's edge, and a stretched box its right-hand
        // one (layout pictures, 29 Sep: Setup's and Venues' Name:). A
        // drop-down draws its own edge, inside itself.
        var outlined = field is TextBoxBase or UpDownBase or ListBox;

        field.Anchor = stretched ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
        field.Margin = new Padding(0, outlined ? 1 : 0, outlined && stretched ? 1 : 0, UiLayout.RowGap);

        Pieces.KeepHeightTold(field);
    }

    /// <summary>
    /// A control with no caption - a check box, a note - across both
    /// columns. It has to fit in the grid's width: a note that wraps, or a
    /// box narrower than the grid. One that wanted more would make the
    /// table widen the caption column to find it room, which this grid's
    /// own measure (GetPreferredSize) does not do, and the fields would
    /// move right of where the grid was measured to put them.
    /// </summary>
    public T AddWide<T>(T control, bool stretch = true) where T : Control
    {
        control.Anchor = stretch ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
        control.Margin = new Padding(0, 0, 0, UiLayout.RowGap);

        Pieces.KeepHeightTold(control);

        RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(control, 0, RowCount - 1);
        SetColumnSpan(control, 2);

        _lastRow.Clear();
        _lastRow.Add(control);

        return control;
    }

    /// <summary>
    /// A line of help under the field above, in the field's column, in the
    /// small muted type the app's hints use, wrapping to the room there is.
    /// </summary>
    public Label AddHint(string text) => AddHint(new Label { Text = text });

    /// <summary>
    /// The same, for a hint the page keeps hold of to reword - a
    /// SteadyLabel, whose wording follows another field.
    /// </summary>
    public T AddHint<T>(T hint) where T : Label
    {
        // The row above sits a hint's gap from its hint, not a row's.
        foreach (var control in _lastRow)
        {
            control.Margin = new Padding(control.Margin.Left, control.Margin.Top, control.Margin.Right, UiLayout.HintGap);
        }

        hint.AutoSize = true;
        hint.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        hint.Margin = new Padding(0, 0, 0, UiLayout.RowGap);
        hint.Font = AppTheme.SmallFont;
        hint.ForeColor = AppTheme.TextMuted;

        RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(hint, 1, RowCount - 1);

        _lastRow.Clear();
        _lastRow.Add(hint);
        _hints.Add(hint);

        return hint;
    }

    /// <summary>
    /// One caption column for several grids, as wide as the widest caption
    /// in any of them - so the fields of a page split into cards still line
    /// up down it. <paramref name="alsoFits"/> are wordings a caption can be
    /// changed to (Setup's "Races each driver gets:" on a club night): the
    /// column is wide enough for those too, so changing one never moves the
    /// fields.
    ///
    /// Measured now, in the fonts the captions have, so it has to be run
    /// again when the text size changes - TextSize.Keep does that. Each
    /// caption is given the width as its least, and the column is as wide
    /// as its widest caption, as ever.
    /// </summary>
    internal static void LineUp(IReadOnlyCollection<FieldGrid> grids, params string[] alsoFits)
    {
        var widest = 0;

        foreach (var grid in grids)
        {
            foreach (var caption in grid._captions)
            {
                widest = Math.Max(widest, CaptionWidth(caption.Text, caption) + caption.Padding.Horizontal);

                foreach (var wording in alsoFits)
                {
                    widest = Math.Max(widest, CaptionWidth(wording, caption) + caption.Padding.Horizontal);
                }
            }
        }

        var least = new Size(widest, 0);

        foreach (var caption in grids.SelectMany(grid => grid._captions))
        {
            if (caption.MinimumSize != least)
            {
                caption.MinimumSize = least;
            }
        }
    }

    /// <summary>
    /// A caption's width on one line in the caption's own font, measured the
    /// way a label measures itself - TextRenderer's default, padding and all
    /// - and never by asking the label, whose answer includes the least
    /// width LineUp gave it last time and so could only ever grow.
    /// </summary>
    private static int CaptionWidth(string text, Label caption) =>
        string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, caption.Font).Width;

    /// <summary>
    /// As tall as the rows need at the width proposed, less the gap under
    /// the last one (see Pieces.TrailingGap) - worked out here, row by row,
    /// rather than by the table underneath, for the reason Section works
    /// its own height out: a table that sizes itself grows when what is in
    /// it wraps onto more lines, and does not shrink again when it unwraps
    /// (dotnet/winforms issue 10379).
    ///
    /// Measured the way the table lays the rows out: the caption column as
    /// wide as its widest caption, the field column the rest, each part
    /// asked how tall it is at the width it will have, and a row as tall as
    /// its tallest part. With no width proposed, the grid is as wide as its
    /// widest caption and its widest field on one line.
    ///
    /// At the width the table last laid the rows out at, though, what they
    /// came to is the answer, not the sum. A drop-down, a number box or a
    /// check box can be a few pixels taller as drawn than it says it wants
    /// to be, and the table lays its row out round the drawn one - so the
    /// sum came out short, and the last row stuck out of the bottom of the
    /// grid by 2 to 10 pixels (the layout test, Race Day's Setup, 29 Sep).
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var captions = CaptionColumnWidth();

        var width = proposedSize.Width is > 0 and < int.MaxValue / 4
            ? proposedSize.Width
            : captions + NaturalFieldWidth();

        if (_laidOut is { } laidOut && laidOut.Width == width)
        {
            return new Size(width, laidOut.Height);
        }

        var fields = Math.Max(1, width - captions);
        var rows = new Dictionary<int, int>();

        foreach (var part in Parts())
        {
            var room = GetColumnSpan(part) > 1
                ? width
                : GetColumn(part) == 0 ? captions : fields;

            var inside = Math.Max(1, room - part.Margin.Horizontal);

            var height = Pieces.SizesItself(part)
                ? part.GetPreferredSize(new Size(inside, 0)).Height
                : part.Height;

            var row = GetRow(part);

            rows[row] = Math.Max(rows.GetValueOrDefault(row), height + part.Margin.Vertical);
        }

        return new Size(width, Math.Max(0, rows.Values.Sum() - Pieces.TrailingGap(this)));
    }

    /// <summary>The caption column: as wide as the widest thing in it that is not across both columns.</summary>
    private int CaptionColumnWidth() =>
        Parts()
            .Where(part => GetColumn(part) == 0 && GetColumnSpan(part) == 1)
            .Select(part => OwnWidth(part) + part.Margin.Horizontal)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>The field column with every field at the width it wants, on one line.</summary>
    private int NaturalFieldWidth() =>
        Parts()
            .Where(part => GetColumn(part) == 1)
            .Select(part => OwnWidth(part) + part.Margin.Horizontal)
            .DefaultIfEmpty(0)
            .Max();

    private static int OwnWidth(Control part) => Pieces.SizesItself(part) ? part.GetPreferredSize(Size.Empty).Width : part.Width;

    /// <summary>The parts that take a place in a row - see Section.Parts.</summary>
    private IEnumerable<Control> Parts() =>
        Controls.Cast<Control>().Where(part => (!Visible || part.Visible) && part is not ThinScrollBar);

    /// <summary>
    /// See Pieces.OutOfStep. Then notes what the rows came to (see
    /// GetPreferredSize): the rows the table laid out, less the gap under
    /// the last, or the lowest part's bottom when one sticks out of its row.
    /// The rows are laid out from the top whatever height the grid is, so
    /// the answer does not change with the height it is then given - asked
    /// again at this width, it is the same.
    /// </summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        if (Pieces.OutOfStep(this))
        {
            base.OnLayout(levent);
        }

        // Once more with the captions moved, which may make a row a pixel
        // or two taller; it moves nothing across, so nothing wraps anew.
        if (LevelWithFirstLines())
        {
            base.OnLayout(levent);
        }

        var rows = GetRowHeights().Sum() - Pieces.TrailingGap(this);
        var lowest = Parts().Select(part => part.Bottom).DefaultIfEmpty(0).Max();

        _laidOut = (Width, Math.Max(0, Math.Max(rows, lowest)));
    }

    /// <summary>
    /// Puts each caption beside a field more than a line tall level with
    /// the field's first line, rather than centred on the whole field - so
    /// "Notes:" is beside the first line of the notes, not halfway down the
    /// box, and "Working on:" stays beside the race day picker when the
    /// buttons after it wrap onto a second line. Every other caption stays
    /// centred on its field. True when it moved one, and the rows want
    /// laying out again.
    ///
    /// Run after the table has laid the rows out, because a row of controls
    /// only knows where its first line ends once it has been given its
    /// width. Only while on screen: a hidden control reports itself hidden,
    /// and the row would look empty. It is laid out as it comes into view
    /// (OnVisibleChanged).
    /// </summary>
    private bool LevelWithFirstLines()
    {
        if (!Visible)
        {
            return false;
        }

        var moved = false;

        foreach (var (caption, field) in _pairs)
        {
            var first = FirstLine(field);

            // Centred on the line the way the table centres a caption on a
            // whole field, rounding the same way - so a row of controls on
            // one line leaves its caption exactly where it was.
            var anchor = first is null ? AnchorStyles.Left : AnchorStyles.Top | AnchorStyles.Left;
            var top = first is { } line
                ? Math.Max(0, field.Margin.Top + (line.Top + line.Bottom - caption.PreferredHeight) / 2)
                : 0;

            if (caption.Anchor != anchor)
            {
                caption.Anchor = anchor;
                moved = true;
            }

            if (caption.Margin.Top != top)
            {
                caption.Margin = new Padding(caption.Margin.Left, top, caption.Margin.Right, caption.Margin.Bottom);
                moved = true;
            }
        }

        return moved;
    }

    /// <summary>
    /// Where a field's first line is, from its top - for a field whose
    /// first line is not all of it - or null for one that is one line tall,
    /// whose caption is centred on it.
    ///
    ///   A row of controls (Inline): the first line of them as the row has
    ///   laid them out - the controls level with the first one - so a row
    ///   on one line comes out as it always did.
    ///   A list: its first item. A grid: its headings.
    ///   A box of several lines: its first line of text.
    ///   A status line: the first line of what it says.
    /// </summary>
    private static (int Top, int Bottom)? FirstLine(Control field)
    {
        switch (field)
        {
            case FlowLayoutPanel flow:
            {
                var shown = flow.Controls.Cast<Control>().Where(control => control.Visible).ToList();

                if (shown.Count == 0)
                {
                    return null;
                }

                // Everything that starts above the first control's bottom is
                // on its line; the next line starts below the tallest of them.
                var first = shown[0];
                var line = shown.Where(control => control.Top < first.Bottom).ToList();

                return (line.Min(control => control.Top), line.Max(control => control.Bottom));
            }

            case ListBox list:
            {
                var border = BorderOf(list.BorderStyle);
                return (border, border + list.ItemHeight);
            }

            case DataGridView grid:
            {
                var border = BorderOf(grid.BorderStyle);
                return (border, border + (grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : grid.RowTemplate.Height));
            }

            case TextBoxBase { Multiline: true } box:
            {
                // The edit control starts its text a pixel inside its border.
                var top = BorderOf(box.BorderStyle) + 1;
                return (top, top + box.Font.Height);
            }

            case StatusLine status:
                return status.FirstLine;

            default:
                return null;
        }
    }

    /// <summary>How thick a control's border is, top and bottom.</summary>
    private static int BorderOf(BorderStyle style) => style switch
    {
        BorderStyle.Fixed3D => SystemInformation.Border3DSize.Height,
        BorderStyle.FixedSingle => SystemInformation.BorderSize.Height,
        _ => 0,
    };

    /// <summary>Coming into view is when the captions can be levelled - see LevelWithFirstLines.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            PerformLayout();
        }
    }

    /// <summary>
    /// The small font is replaced, not resized, when the text size changes,
    /// so the hints are handed the new one. The captions need nothing: they
    /// wear this grid's font, which follows the window's.
    /// </summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        foreach (var hint in _hints)
        {
            if (!Equals(hint.Font, AppTheme.SmallFont))
            {
                hint.Font = AppTheme.SmallFont;
            }
        }
    }

    /// <summary>
    /// Several controls side by side in one field - a box and its units, a
    /// drop-down and its Test button - wrapping onto a second line rather
    /// than running off the edge when the row is narrow.
    ///
    /// A ButtonBar underneath, so a button among them is measured to its
    /// caption exactly as one in a bar is. Everything that is not a button
    /// is centred up and down on the row - a drop-down beside a taller
    /// button sits level with its middle - and a label sizes itself.
    /// </summary>
    public static FlowLayoutPanel Inline(params Control[] controls)
    {
        foreach (var control in controls)
        {
            if (control is Label label)
            {
                label.AutoSize = true;
            }

            if (control is not Button)
            {
                control.Anchor = AnchorStyles.Left;
            }
        }

        return new ButtonBar().With(controls);
    }
}

/// <summary>
/// A row of buttons, each exactly as wide as its caption needs in the bold
/// face buttons are drawn in, wrapping onto another row when the room runs
/// out rather than running off the edge.
///
/// A button re-measures itself whenever its caption or its font changes -
/// a caption swapped at runtime, or the theme pass giving it the bold face
/// at a new text size - so nothing has to remember to.
///
/// Put it in one of the other pieces (a Section, a FieldGrid, a PageStack):
/// they give it the width it wraps to. On its own, placed by hand, it has
/// no width to wrap against and lays out in one line.
/// </summary>
internal sealed class ButtonBar : FlowLayoutPanel
{
    private readonly bool _tight;
    private readonly bool _sameWidth;

    /// <summary>Set while a button is being sized, so the size change it causes is not taken for a caption change.</summary>
    private bool _fitting;

    /// <param name="alignRight">Buttons against the right-hand edge, the first one furthest right - a dialog's OK and Cancel.</param>
    /// <param name="tight">
    /// Each button only as wide as its caption and a little room, with no
    /// standard minimum, and a smaller gap - for a long row that should stay
    /// on one line where it can: Race Control's seven flags. See
    /// UiLayout.TightButtonSizeFor.
    /// </param>
    /// <param name="sameWidth">
    /// Every button as wide as the widest needs, so a bar that wraps onto a
    /// second row reads as columns rather than ragging out to each caption.
    /// </param>
    public ButtonBar(bool alignRight = false, bool tight = false, bool sameWidth = false)
    {
        _tight = tight;
        _sameWidth = sameWidth;

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = true;
        FlowDirection = alignRight ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;
    }

    /// <summary>The gap between buttons, across and down.</summary>
    private int Gap => _tight ? UiLayout.TightButtonGap : UiLayout.ButtonGap;

    /// <summary>
    /// How wide the bar is with everything in it on one line - for a column
    /// made wide enough that its bar does not wrap (Batteries' packs).
    /// Known before the bar is laid out: each
    /// button is sized as it is added, and again when its caption or font
    /// changes. Hidden in its own right leaves a button out, as in
    /// Pieces.TrailingGap.
    /// </summary>
    public int OneLineWidth
    {
        get
        {
            // Each with its margin, the last one's gap included: the flow
            // counts a button's margin when it decides whether the button
            // fits on the line, so a bar exactly this wide stays on one.
            return Controls.OfType<Control>()
                .Where(part => !Visible || part.Visible)
                .Sum(part => part.Width + part.Margin.Horizontal);
        }
    }

    /// <summary>Adds buttons (or anything else) in order.</summary>
    public ButtonBar With(params Control[] items)
    {
        foreach (var item in items)
        {
            Controls.Add(item);
        }

        return this;
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);

        if (e.Control is not { } control)
        {
            return;
        }

        // The gap on the side the next button comes from, and below, for
        // when the row wraps.
        control.Margin = FlowDirection == FlowDirection.RightToLeft
            ? new Padding(Gap, 0, 0, Gap)
            : new Padding(0, 0, Gap, Gap);

        if (control is Button button)
        {
            button.TextChanged += ButtonChanged;
            button.FontChanged += ButtonChanged;
            Fit(button);
        }

        // A drop-down or a box among the buttons (FieldGrid.Inline) - see Pieces.KeepHeightTold.
        Pieces.KeepHeightTold(control);
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        if (e.Control is Button button)
        {
            button.TextChanged -= ButtonChanged;
            button.FontChanged -= ButtonChanged;
        }

        base.OnControlRemoved(e);

        // The widest may just have gone.
        if (_sameWidth && e.Control is Button)
        {
            Fit(null);
        }
    }

    /// <summary>
    /// The gap under the last row is not the bar's to keep: whatever comes
    /// after the bar sets its own distance, and the bar handing back an
    /// extra ten pixels would put every bar a row-gap further from the next
    /// thing than anything else on the page.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);

        // The same rule as Pieces.TrailingGap: while the bar itself is
        // hidden every button reports itself hidden, so then all count.
        var anything = Visible
            ? Controls.OfType<Control>().Any(c => c.Visible)
            : Controls.Count > 0;

        return anything
            ? new Size(size.Width, Math.Max(0, size.Height - Gap))
            : size;
    }

    private void ButtonChanged(object? sender, EventArgs e)
    {
        if (sender is Button button)
        {
            Fit(button);
        }
    }

    /// <summary>
    /// Sizes a button to its caption - or, in a bar of one width, every
    /// button to the widest caption, which one changing can change for all
    /// of them. Null re-fits a bar of one width after a button has left it.
    /// </summary>
    private void Fit(Button? changed)
    {
        if (_fitting)
        {
            return;
        }

        _fitting = true;

        try
        {
            if (!_sameWidth)
            {
                if (changed is not null)
                {
                    changed.AutoSize = false;
                    changed.Size = SizeFor(changed);
                }

                return;
            }

            var buttons = Controls.OfType<Button>().ToList();

            if (buttons.Count == 0)
            {
                return;
            }

            // Measured, all of them, every time: keeping the widest seen so
            // far would ratchet, so the bar could grow with the text but
            // never shrink again.
            var sizes = buttons.Select(SizeFor).ToList();
            var size = new Size(sizes.Max(s => s.Width), sizes.Max(s => s.Height));

            foreach (var button in buttons)
            {
                button.AutoSize = false;
                button.Size = size;
            }
        }
        finally
        {
            _fitting = false;
        }
    }

    /// <summary>What one button needs for its caption, in this bar's kind of row.</summary>
    private Size SizeFor(Button button) =>
        _tight ? UiLayout.TightButtonSizeFor(button) : UiLayout.ButtonSizeFor(button);
}

/// <summary>
/// Caption-and-field pairs along a line - Race Control's Session row, a
/// filter bar - each caption centred on its field and the pairs a section
/// gap apart. The last field takes whatever width its line has left.
///
/// A pair that does not fit on its line starts the next one. The last pair
/// moves too when its line would leave its field less than it can do with -
/// a round picker cut down to half a round's name is not a picker - and then
/// runs the full width of a line of its own.
///
/// A field can go without a caption: a check box says what it is itself,
/// and a line of text at the end of a filter bar needs none.
///
/// A pair whose field is hidden is left out, and the line closes up behind
/// it. Hide the caption with it.
///
/// Sizes itself: put it in a Section or a FieldGrid, which give it the width
/// it wraps to.
/// </summary>
internal sealed class FieldRow : Panel
{
    private readonly List<(Label? Caption, Control Field, Func<int>? Width)> _pairs = new();

    /// <summary>The least width the last field can do with, when it takes the width left - see AddLast.</summary>
    private Func<int>? _lastLeast;

    public FieldRow()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;
    }

    /// <summary>
    /// A caption and a field. The field keeps the width it was given - or
    /// the width it wants, when it sizes itself (a check box) - or, with
    /// <paramref name="width"/>, the width that says, asked whenever the row
    /// is measured: a drop-down as wide as its longest choice at the text
    /// size in use. Null for a field with no caption. Returns the caption.
    /// </summary>
    public Label? Add(Label? caption, Control field, Func<int>? width = null)
    {
        if (_lastLeast is not null)
        {
            throw new InvalidOperationException("The pair that takes the width left has to be the last one.");
        }

        Pair(caption, field, width);
        return caption;
    }

    /// <summary>
    /// The last pair, whose field runs to the end of its line. When that
    /// would make it narrower than <paramref name="leastWidth"/> says, the
    /// pair goes onto a line of its own. Asked whenever the row is measured,
    /// so it can follow the text size and what the field holds.
    /// </summary>
    public Label? AddLast(Label? caption, Control field, Func<int> leastWidth)
    {
        if (_lastLeast is not null)
        {
            throw new InvalidOperationException("A row has one pair that takes the width left.");
        }

        Pair(caption, field, width: null);
        _lastLeast = leastWidth;
        return caption;
    }

    private void Pair(Label? caption, Control field, Func<int>? width)
    {
        if (caption is not null)
        {
            caption.AutoSize = true;
            caption.Margin = Padding.Empty;
            Controls.Add(caption);
        }

        field.Margin = Padding.Empty;

        Pieces.KeepHeightTold(field);

        _pairs.Add((caption, field, width));
        Controls.Add(field);

        // A label that sizes itself tells this row when it is rewritten;
        // one the row sizes does not - see FieldTextChanged.
        if (field is Label)
        {
            field.TextChanged += FieldTextChanged;
        }
    }

    /// <summary>How tall the pairs come to at the width proposed, and how wide the widest line is.</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < Unbounded ? proposedSize.Width : Unbounded;

        return Flow(width, place: false);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        Flow(Math.Max(1, ClientSize.Width), place: true);
    }

    /// <summary>
    /// A pair shown or hidden changes the lines, and so does the row coming
    /// into view: while it was hidden every field reported itself hidden,
    /// so the lines were worked out with all of them in.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            PerformLayout();
        }
    }

    /// <summary>
    /// A label field the row sizes itself - the one that takes the width
    /// left, cut short with "..." - has been rewritten. How long it is can
    /// decide which line it goes on (Who's Racing's summary moves onto a line
    /// of its own when it no longer fits beside the filter), and nothing
    /// tells the row: a label only asks its parent to lay out again when it
    /// sizes itself. So this asks - the whole row, and whatever it is in,
    /// only when the row changes height; otherwise the pairs are just put
    /// where they now go, which a summary rewritten on every tick at the
    /// check-in desk makes worth sparing.
    /// </summary>
    private void FieldTextChanged(object? sender, EventArgs e)
    {
        if (sender is Label { AutoSize: true } || !Visible)
        {
            return;
        }

        var width = Math.Max(1, ClientSize.Width);

        if (Flow(width, place: false).Height != Height)
        {
            PerformLayout();
        }
        else
        {
            Flow(width, place: true);
        }
    }

    /// <summary>Wider than any row will be - "no width to wrap to".</summary>
    private const int Unbounded = int.MaxValue / 4;

    /// <summary>
    /// Works the pairs out along lines <paramref name="width"/> wide -
    /// placing them there when <paramref name="place"/> is set - and returns
    /// the widest line and how tall the lines come to.
    /// </summary>
    private Size Flow(int width, bool place)
    {
        // Hidden in its own right leaves the pair out. While the row itself
        // is hidden that cannot be told, so then every pair counts - see
        // Pieces.TrailingGap.
        var pairs = _pairs.Where(pair => !Visible || pair.Field.Visible).ToList();

        if (pairs.Count == 0)
        {
            return Size.Empty;
        }

        // One height for every line - the tallest caption or field - so the
        // captions and fields on a line share a centre, and a second line
        // lines up under the first.
        var lineHeight = pairs.Max(pair => Math.Max(
            pair.Caption is null ? 0 : CaptionSize(pair.Caption).Height,
            FieldSize(pair.Field).Height));

        var last = _lastLeast is null ? null : _pairs[^1].Field;
        var x = 0;
        var line = 0;
        var widest = 0;

        foreach (var (caption, field, measure) in pairs)
        {
            var captionSize = caption is null ? Size.Empty : CaptionSize(caption);
            var captionGap = caption is null ? 0 : UiLayout.LabelGap;
            var own = FieldSize(field);
            var stretches = ReferenceEquals(field, last);

            var fieldWidth = stretches
                ? Math.Max(1, _lastLeast!())
                : measure is not null
                    ? Math.Max(1, measure())
                    : own.Width;
            var needed = captionSize.Width + captionGap + fieldWidth;
            var left = x == 0 ? 0 : x + UiLayout.SectionGap;

            if (x > 0 && left + needed > width)
            {
                line++;
                left = 0;
            }

            var top = line * (lineHeight + UiLayout.RowGap);
            var fieldLeft = left + captionSize.Width + captionGap;

            // To the end of the line - when there is an end to go to. On a
            // line too narrow even for its least, it takes the line and no
            // more: past the edge is worse than a narrow picker.
            if (stretches && width < Unbounded)
            {
                fieldWidth = Math.Max(1, width - fieldLeft);
            }

            if (place)
            {
                caption?.SetBounds(left, top + (lineHeight - captionSize.Height) / 2, captionSize.Width, captionSize.Height);
                field.SetBounds(fieldLeft, top + (lineHeight - own.Height) / 2, fieldWidth, own.Height);
            }

            x = fieldLeft + fieldWidth;
            widest = Math.Max(widest, x);
        }

        return new Size(widest, (line + 1) * lineHeight + line * UiLayout.RowGap);
    }

    /// <summary>A caption's size on one line, measured rather than read back: an AutoSize label has not sized itself until it has been laid out.</summary>
    private static Size CaptionSize(Label caption) => caption.GetPreferredSize(Size.Empty);

    /// <summary>
    /// A field's size on one line. A label is one line of its font, whatever
    /// size it was given - the one that takes the width left is sized by the
    /// row, and cut short with "..." rather than wrapped. Anything else that
    /// sizes itself - a check box - is measured, as a caption is, and for the
    /// same reason. A text box's "AutoSize" is only its height, which it
    /// keeps in step itself; it and everything else are the size they were
    /// given.
    /// </summary>
    private static Size FieldSize(Control field) => field switch
    {
        TextBoxBase => field.Size,
        Label label => label.GetPreferredSize(Size.Empty),
        _ when field.AutoSize => field.GetPreferredSize(Size.Empty),
        _ => field.Size,
    };
}

/// <summary>
/// A column down the right-hand side, beside the page's main parts - Race
/// Control's clock and the buttons under it, beside the flags and the
/// leaderboard; Who's Racing's scan card beside its roster.
///
/// On the left, parts one under another as a FillPage has them: each as
/// tall as it needs to be, and one that takes the height left over (Add,
/// AddFill). On the right, the column (AddSide, AddSideCard): as wide as
/// the widest button in it needs, and never narrower than it was told, with
/// every part in it that wide, one under another from the top - and one of
/// them can take the height the column has left, as Driver Profiles' card
/// does (AddSideFill).
///
/// Goes in a FillPage as its filling part, with LeastHeight as the least it
/// can do with - the column, or the left-hand parts with the filling one at
/// its least, whichever is taller - so a window too short for either
/// scrolls rather than squashing the list or running the column off the
/// bottom. Or in a PageStack as a part that sizes itself (AutoSize), when
/// it is exactly that tall: Next Steps' card and Ready to Race.
///
/// The column is placed by hand rather than by a table: its width comes
/// from the buttons' captions, not from what the parts in it would report,
/// and every part in it is given that width.
///
/// The column can go down the left instead (SideOnLeft) - a list picked
/// from, beside what it shows: Race History's sessions, Lap Times' drivers.
/// </summary>
internal sealed class Beside : Panel
{
    private readonly MainColumn _main;

    /// <summary>The column's parts, in order. A card (Measured) is as tall as it needs to be at the column's width; anything else keeps its own height.</summary>
    private readonly List<(Control Part, int GapBelow, bool Measured)> _side = new();

    /// <summary>What each button in the column needs across for its caption. The column is as wide as the widest.</summary>
    private readonly Dictionary<Button, int> _captionWidths = new();

    private readonly Func<int> _sideLeastWidth;

    /// <summary>The panel holding the part on the left that takes the height left over, and the least that part can do with.</summary>
    private Control? _fillHolder;
    private Func<int, int>? _fillLeast;

    /// <summary>The part of the column that takes the height the column has left, and the least it can do with at the column's width - see AddSideFill.</summary>
    private (Control Part, Func<int, int> Least)? _sideFill;

    /// <summary>Set while a button is being sized, so the size change it causes is not taken for a caption change.</summary>
    private bool _fitting;

    /// <summary>The column down the left-hand edge rather than the right, the other parts to the right of it. Set as the Beside is made.</summary>
    public bool SideOnLeft { get; init; }

    /// <param name="sideLeastWidth">The narrowest the column gets - the width its drawn parts (a clock) were designed at.</param>
    public Beside(int sideLeastWidth = 0)
        : this(() => sideLeastWidth)
    {
    }

    /// <param name="sideLeastWidth">
    /// The narrowest the column gets, asked whenever it is placed - for a
    /// column whose least follows the text size, or the buttons inside a
    /// card in it, which the column does not see.
    /// </param>
    public Beside(Func<int> sideLeastWidth)
    {
        _sideLeastWidth = sideLeastWidth;

        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;

        _main = new MainColumn();
        Controls.Add(_main);
    }

    /// <summary>Adds a part on the left, under the last, as tall as it needs to be. See PageStack.Add.</summary>
    public T Add<T>(T part, bool stretch = true, int gapBelow = UiLayout.RowGap) where T : Control
    {
        Pieces.PlaceInColumn(_main, part, stretch, gapBelow);
        return part;
    }

    /// <summary>
    /// Adds the part on the left that takes the height left over. One only.
    /// In a panel of its own, for the reason FillPage.AddFill gives.
    /// </summary>
    /// <param name="leastHeight">The least the part can do with at the width it is given.</param>
    public T AddFill<T>(T part, Func<int, int> leastHeight, int gapBelow = 0) where T : Control
    {
        if (_fillHolder is not null)
        {
            throw new InvalidOperationException("A Beside has one part that takes the height left over.");
        }

        var holder = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, Math.Max(0, gapBelow)),
            Padding = Padding.Empty,
            Tag = AppTheme.KeepBackColor,
        };

        part.Dock = DockStyle.Fill;
        part.Margin = Padding.Empty;
        holder.Controls.Add(part);

        _fillHolder = holder;
        _fillLeast = leastHeight;

        // Something in it growing or shrinking - a line of a card wrapping -
        // can change the least this can do with, and only the part sees it.
        // See AddSideCard.
        part.Layout += (_, _) => PerformLayout();

        _main.RowCount++;
        _main.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _main.Controls.Add(holder, 0, _main.RowCount - 1);

        return part;
    }

    /// <summary>
    /// Adds a part to the column down the right, under the last. It is given
    /// the column's width; its height is its own. A button is sized to its
    /// caption, and again whenever that or its font changes. Anything else
    /// keeps the height it was given, so give it one - a part in the column
    /// is not asked how tall it wants to be. A card is: see AddSideCard.
    /// </summary>
    public T AddSide<T>(T part, int gapBelow = UiLayout.RowGap) where T : Control
    {
        Side(part, gapBelow, measured: false);

        if (part is Button button)
        {
            button.TextChanged += ButtonChanged;
            button.FontChanged += ButtonChanged;
            Fit(button);
        }

        return part;
    }

    /// <summary>
    /// Adds a card to the column, under the last - Who's Racing's scan card,
    /// Next Steps' Ready to Race. It is given the column's width, and is as
    /// tall as it needs to be at that width.
    ///
    /// It is asked whenever the column is placed, and the column is placed
    /// again whenever the card lays itself out: a line in it wrapping onto a
    /// second, or a part in it shown, is something only the card sees, and a
    /// card placed by hand has no parent that asks it. Told as the card's
    /// layout starts, before its rows are placed - for the reason PageStack's
    /// column tells its stack.
    ///
    /// Buttons inside the card are the card's to size; make the column at
    /// least as wide as they need (the least width, as a Func).
    /// </summary>
    public Section AddSideCard(Section card, int gapBelow = UiLayout.RowGap)
    {
        Side(card, gapBelow, measured: true);

        card.Layout += (_, _) => PerformLayout();

        return card;
    }

    /// <summary>
    /// Adds the part of the column that takes the height the column has
    /// left once the others have theirs - Driver Profiles' card, the
    /// driver's picture and record, which scrolls inside itself. One only.
    /// It is given the column's width, and never less height than
    /// <paramref name="leastHeight"/> says at that width, which is what the
    /// column counts it as when working out the least this can do with.
    ///
    /// Placed again whenever it lays itself out, as a card added with
    /// AddSideCard is: something in it shown or hidden can change its least.
    /// </summary>
    public T AddSideFill<T>(T part, Func<int, int> leastHeight, int gapBelow = UiLayout.RowGap) where T : Control
    {
        if (_sideFill is not null)
        {
            throw new InvalidOperationException("A Beside's column has one part that takes the height left over.");
        }

        Side(part, gapBelow, measured: false);
        _sideFill = (part, leastHeight);

        part.Layout += (_, _) => PerformLayout();

        return part;
    }

    private void Side(Control part, int gapBelow, bool measured)
    {
        // Sized here, never by the layout: one that sized itself would be
        // resized by the panel to its width on one line.
        part.AutoSize = false;
        part.Margin = Padding.Empty;

        _side.Add((part, Math.Max(0, gapBelow), measured));
        Controls.Add(part);
    }

    /// <summary>
    /// The least height this can do with at <paramref name="width"/>: the
    /// column, or the parts on the left with the filling one at its least,
    /// whichever is taller.
    /// </summary>
    public int LeastHeight(int width)
    {
        var side = MeasureSide();
        var mainWidth = MainWidth(width, side);
        var main = 0;

        foreach (Control part in _main.Controls)
        {
            // As in Section.LeastHeight.
            if (_main.Visible && !part.Visible || part is ThinScrollBar)
            {
                continue;
            }

            var partWidth = Math.Max(1, mainWidth - part.Margin.Horizontal);

            var height = ReferenceEquals(part, _fillHolder) && _fillLeast is not null
                ? Math.Max(0, _fillLeast(partWidth))
                : Pieces.SizesItself(part)
                    ? part.GetPreferredSize(new Size(partWidth, 0)).Height
                    : part.Height;

            main += height + part.Margin.Vertical;
        }

        return Math.Max(side.Height, main);
    }

    /// <summary>
    /// As tall as it can least be at the width proposed - for a Beside that
    /// sizes itself (AutoSize), as a part of a PageStack rather than the
    /// filling part of a FillPage. With no filling part, or one whose least
    /// is all it needs, that is exactly as tall as what is in it.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width;

        return new Size(width, LeastHeight(width));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // Laid out already while the constructor set the margins, before
        // the column on the left was there to place.
        if (_main is null)
        {
            base.OnLayout(levent);
            return;
        }

        // A part in the column that grew changes the least the page can do
        // with. The page is told first - see FillPage.LeastHeightMayHaveChanged.
        Pieces.FillPageOf(this)?.LeastHeightMayHaveChanged();

        // One that sizes itself, in a PageStack, asks to be given its new
        // height first, for the same reason: asked after, the stack would
        // resize it while it was still laying out, and that layout is
        // dropped.
        if (AutoSize && Parent is not null && Height != LeastHeight(Width))
        {
            Parent.PerformLayout(this, nameof(PreferredSize));
        }

        Place();

        base.OnLayout(levent);
    }

    /// <summary>
    /// Coming into view: while hidden, a part hidden in its own right could
    /// not be told from the rest, so the column was worked out with it in.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            PerformLayout();
        }
    }

    /// <summary>The other parts across the width the column leaves, the column against the right-hand edge - or the left, with SideOnLeft.</summary>
    private void Place()
    {
        var client = ClientSize;
        var side = MeasureSide();

        var mainLeft = SideOnLeft && side.Width > 0 ? side.Width + UiLayout.RowGap : 0;
        var main = new Rectangle(mainLeft, 0, MainWidth(client.Width, side), Math.Max(0, client.Height));

        if (_main.Bounds != main)
        {
            _main.Bounds = main;
        }

        var left = SideOnLeft ? 0 : client.Width - side.Width;
        var y = 0;

        // What the column's filling part has on top of its least: whatever
        // the others leave of the height this has.
        var spare = Math.Max(0, client.Height - side.Height);

        foreach (var (part, gapBelow, measured) in _side)
        {
            if (!Counts(part))
            {
                continue;
            }

            var height = SideHeight(part, measured, side.Width)
                + (IsSideFill(part) ? spare : 0);

            var bounds = new Rectangle(left, y, side.Width, height);

            if (part.Bounds != bounds)
            {
                part.Bounds = bounds;
            }

            y += height + gapBelow;
        }
    }

    /// <summary>The column's width, and how tall its parts come to one under another. Empty when it has none.</summary>
    private Size MeasureSide()
    {
        var width = Math.Max(0, _sideLeastWidth());
        var any = false;

        foreach (var (part, _, _) in _side)
        {
            if (!Counts(part))
            {
                continue;
            }

            any = true;

            if (part is Button button && _captionWidths.TryGetValue(button, out var needs))
            {
                width = Math.Max(width, needs);
            }
        }

        if (!any)
        {
            return Size.Empty;
        }

        // The width first: a card's height is its height at that width.
        var height = 0;
        var gapAbove = -1;

        foreach (var (part, gapBelow, measured) in _side)
        {
            if (!Counts(part))
            {
                continue;
            }

            height += Math.Max(0, gapAbove) + SideHeight(part, measured, width);
            gapAbove = gapBelow;
        }

        return new Size(width, height);
    }

    /// <summary>
    /// A part's least height in the column: the filling part at its least, a
    /// card as tall as it needs to be at the column's width, anything else
    /// its own.
    /// </summary>
    private int SideHeight(Control part, bool measured, int width) =>
        IsSideFill(part) && _sideFill is { } fill
            ? Math.Max(0, fill.Least(Math.Max(1, width)))
            : measured
                ? part.GetPreferredSize(new Size(Math.Max(1, width), 0)).Height
                : part.Height;

    private bool IsSideFill(Control part) => _sideFill is { } fill && ReferenceEquals(fill.Part, part);

    /// <summary>What the column leaves on the left, with a gap between the two.</summary>
    private static int MainWidth(int width, Size side) =>
        side.Width > 0
            ? Math.Max(1, width - side.Width - UiLayout.RowGap)
            : Math.Max(1, width);

    /// <summary>Hidden in its own right leaves a part out - askable only while this is on screen, as in Pieces.TrailingGap.</summary>
    private bool Counts(Control part) => !Visible || part.Visible;

    private void ButtonChanged(object? sender, EventArgs e)
    {
        if (sender is Button button)
        {
            Fit(button);
        }
    }

    /// <summary>
    /// Sizes a button in the column: as tall as the theme's rule says, and
    /// remembers how wide its caption needs it - the column's width comes
    /// from that. Its width is the column's, set as it is placed.
    /// </summary>
    private void Fit(Button button)
    {
        if (_fitting)
        {
            return;
        }

        _fitting = true;

        try
        {
            var size = UiLayout.ButtonSizeFor(button);

            _captionWidths[button] = size.Width;
            button.Height = size.Height;
        }
        finally
        {
            _fitting = false;
        }

        // The width it needs may have changed the column's without changing
        // the button's own size, which is what would have laid this out.
        PerformLayout();
    }

    /// <summary>
    /// The table the left-hand parts go in. What is in it growing or
    /// shrinking - a line of text wrapping - changes the least the page can
    /// do with, and the page does not see in here, so this tells it: first,
    /// before laying anything out, for the reason PageStack's column tells
    /// its stack.
    /// </summary>
    private sealed class MainColumn : TableLayoutPanel
    {
        public MainColumn()
        {
            ColumnCount = 1;
            RowCount = 0;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Tag = AppTheme.KeepBackColor;

            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (Parent is Beside beside)
            {
                Pieces.FillPageOf(beside)?.LeastHeightMayHaveChanged();

                // And a Beside that sizes itself, likewise - see its own
                // OnLayout.
                if (beside.AutoSize && beside.Parent is not null && beside.Height != beside.LeastHeight(beside.Width))
                {
                    beside.Parent.PerformLayout(beside, nameof(PreferredSize));
                }
            }

            base.OnLayout(levent);

            if (Pieces.OutOfStep(this))
            {
                base.OnLayout(levent);
            }
        }
    }
}

/// <summary>
/// Two bars of buttons on one line, one against each edge - on Next Steps'
/// program, the commands for the selected round on the left and the tools
/// for the whole day on the right, the glossary hard against the right-hand
/// edge where help is looked for.
///
/// When the line is too narrow for both, the right-hand bar goes under the
/// left, still against the right-hand edge, so both lines end in the same
/// place; either wraps further if it has to. Make the right-hand bar a
/// ButtonBar(alignRight: true), whose first button is furthest right.
///
/// Sizes itself: put it in a Section, which gives it the width.
/// </summary>
internal sealed class SplitBar : Panel
{
    private readonly ButtonBar _left;
    private readonly ButtonBar _right;

    public SplitBar(ButtonBar left, ButtonBar right)
    {
        _left = left;
        _right = right;

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;

        Controls.Add(left);
        Controls.Add(right);
    }

    /// <summary>How tall the two come to at the width proposed. With none, both on one line.</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4
            ? proposedSize.Width
            : _left.GetPreferredSize(Size.Empty).Width + UiLayout.SectionGap + _right.GetPreferredSize(Size.Empty).Width;

        var (left, right) = Arrange(width);

        return new Size(width, Math.Max(left.Bottom, right.Bottom));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        var (left, right) = Arrange(Math.Max(1, ClientSize.Width));

        if (_left.Bounds != left)
        {
            _left.Bounds = left;
        }

        if (_right.Bounds != right)
        {
            _right.Bounds = right;
        }
    }

    /// <summary>A bar shown or hidden - and the row coming into view - changes the lines, as for FieldRow.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            PerformLayout();
        }
    }

    /// <summary>
    /// Where the two bars go on a line <paramref name="width"/> wide: side
    /// by side, the left one's buttons and the right one's a section gap
    /// apart at least, when both fit on one row each; otherwise one under
    /// the other, each wrapped to the whole width.
    /// </summary>
    private (Rectangle Left, Rectangle Right) Arrange(int width)
    {
        // A bar hidden in its own right takes no room - askable only while
        // this is on screen, as in Pieces.TrailingGap.
        var leftShown = !Visible || _left.Visible;
        var rightShown = !Visible || _right.Visible;

        if (!leftShown || !rightShown)
        {
            var only = leftShown ? _left : _right;
            var size = only.GetPreferredSize(new Size(width, 0));
            var bounds = new Rectangle(leftShown ? 0 : width - Math.Min(width, size.Width), 0, Math.Min(width, size.Width), size.Height);

            return leftShown ? (bounds, Rectangle.Empty) : (Rectangle.Empty, bounds);
        }

        var leftOne = _left.GetPreferredSize(Size.Empty);
        var rightOne = _right.GetPreferredSize(Size.Empty);

        if (leftOne.Width + UiLayout.SectionGap + rightOne.Width <= width)
        {
            var height = Math.Max(leftOne.Height, rightOne.Height);

            return (
                new Rectangle(0, (height - leftOne.Height) / 2, leftOne.Width, leftOne.Height),
                new Rectangle(width - rightOne.Width, (height - rightOne.Height) / 2, rightOne.Width, rightOne.Height));
        }

        var left = _left.GetPreferredSize(new Size(width, 0));
        var right = _right.GetPreferredSize(new Size(width, 0));

        var leftWidth = Math.Min(width, left.Width);
        var rightWidth = Math.Min(width, right.Width);

        return (
            new Rectangle(0, 0, leftWidth, left.Height),
            new Rectangle(width - rightWidth, left.Height + UiLayout.RowGap, rightWidth, right.Height));
    }
}

/// <summary>
/// A label as tall as the longest of the things it can say, at the width it
/// has - so rewording it never moves what is under it. Setup's hints, which
/// change with the kind of night; a status line whose longest form is
/// known.
///
/// It sizes itself and wraps to the width it is given, as any label in a
/// piece does, and is never shorter than any of its Wordings would be
/// there. The old way was a label of fixed size with two lines kept for it,
/// which on a wide page was a blank line under one line of text, and at a
/// large text size not two lines' worth.
/// </summary>
internal sealed class SteadyLabel : Label
{
    public SteadyLabel()
    {
        AutoSize = true;
    }

    /// <summary>Everything it can say - or, for text built from numbers, the longest it can come to. Measured alongside what it says now.</summary>
    public IReadOnlyList<string> Wordings { get; set; } = Array.Empty<string>();

    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);

        if (Wordings.Count == 0)
        {
            return size;
        }

        // A label treats a width of 1 as none at all; so does this.
        var bounded = proposedSize.Width is > 1 and < int.MaxValue / 4;

        var room = new Size(
            bounded ? Math.Max(1, proposedSize.Width - Padding.Horizontal) : int.MaxValue,
            int.MaxValue);

        // The flags a label wraps its text with.
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;

        if (!UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }

        foreach (var wording in Wordings)
        {
            if (string.IsNullOrEmpty(wording))
            {
                continue;
            }

            var needed = TextRenderer.MeasureText(wording, Font, room, flags);

            size = new Size(
                Math.Max(size.Width, bounded ? Math.Min(proposedSize.Width, needed.Width + Padding.Horizontal) : needed.Width + Padding.Horizontal),
                Math.Max(size.Height, needed.Height + Padding.Vertical));
        }

        return size;
    }
}

/// <summary>
/// A light and what it means, side by side - the decoder's status on the
/// Connection page: grey, amber, green or red, and a line saying what is
/// happening. The line wraps to the room there is, and the light stays
/// level with its first line.
///
/// The light is whatever control it is handed, coloured by its owner
/// through its BackColor - which the theme pass is told to leave alone - and
/// sized here: the 14px square it was at the normal text size, at the size
/// in use. The text is a label its owner keeps and rewrites, and the line
/// follows what it says (see TextRewritten). An error from a decoder can
/// run to two sentences; a label that sized itself would have kept them to
/// one line, off the edge of the card.
///
/// Sizes itself: put it in a Section or a FieldGrid, which give it the
/// width it wraps to. In a FieldGrid its caption is level with its first
/// line (FieldGrid.FirstLine).
/// </summary>
internal sealed class StatusLine : Panel
{
    /// <summary>The light's side at the normal text size.</summary>
    private const int LightSide = 14;

    private readonly Control _light;
    private readonly Label _text;

    public StatusLine(Control light, Label text)
    {
        _light = light;
        _text = text;

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;

        // The colour is what the light says. Left to the theme pass it was
        // repainted in the page's colour every time the look or the text
        // size changed, and stayed invisible until the status next did.
        light.Tag = AppTheme.KeepBackColor;
        light.Margin = Padding.Empty;

        // Sized and placed here: a label that sizes itself keeps to one line
        // whatever width it has.
        text.AutoSize = false;
        text.Margin = Padding.Empty;
        text.TextChanged += TextRewritten;

        Controls.Add(light);
        Controls.Add(text);
    }

    /// <summary>The light's side at the text size in use.</summary>
    private static int Side => UiLayout.AtTextSize(LightSide);

    /// <summary>One line of the text, as the label draws it.</summary>
    private int TextLine => _text.Font.Height + _text.Padding.Vertical;

    /// <summary>Where the text's first line is, from the top - what FieldGrid levels a caption with.</summary>
    internal (int Top, int Bottom) FirstLine
    {
        get
        {
            var (_, textTop) = Tops();
            return (textTop, textTop + TextLine);
        }
    }

    /// <summary>
    /// How far down a line of the text the middle of its capitals is - where
    /// the light's middle goes. Not the middle of the line: a line is as
    /// tall as the font's ascent and descent together, and the capitals sit
    /// on the baseline, below the middle. Centred on the line, the light
    /// sat 3px above "Disconnected" at the normal text size and more at 200%
    /// (layout pictures, 29 Sep); worked out from the typeface's own
    /// measurements, it moved one pixel, because what GDI draws is not what
    /// those measurements say. So a capital is drawn, the way the label
    /// draws its text, and looked at - once for each font (CapitalsIn).
    /// </summary>
    private int CapitalsMiddle() =>
        _text.Padding.Top + CapitalsIn.GetValue(_text.Font, font => new StrongBox<int>(MeasureCapitals(font))).Value;

    /// <summary>The middle of the capitals, from the top of a line, for each font asked about - fonts are replaced, not changed, so a font is its own key.</summary>
    private static readonly ConditionalWeakTable<Font, StrongBox<int>> CapitalsIn = new();

    /// <summary>Draws an "H" in <paramref name="font"/> as a label draws text, from the top of the line, and finds its first and last rows of ink.</summary>
    private static int MeasureCapitals(Font font)
    {
        var middleOfLine = font.Height / 2;

        try
        {
            var size = TextRenderer.MeasureText("H", font, Size.Empty, TextFormatFlags.NoPadding);

            using var bitmap = new Bitmap(Math.Max(1, size.Width + 4), Math.Max(1, font.Height + 4));

            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                TextRenderer.DrawText(g, "H", font, new Point(0, 0), Color.Black, Color.White, TextFormatFlags.NoPadding);
            }

            var top = -1;
            var bottom = -1;

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);

                    if (pixel.R + pixel.G + pixel.B < 3 * 128)
                    {
                        if (top < 0)
                        {
                            top = y;
                        }

                        bottom = y;
                        break;
                    }
                }
            }

            return top < 0 ? middleOfLine : (top + bottom + 1) / 2;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            // Nothing could be drawn to look at. The middle of the line is
            // near enough.
            return middleOfLine;
        }
    }

    /// <summary>
    /// Where the light and the text start: the light's middle on the middle
    /// of the capitals, and the text moved down only when the light would
    /// otherwise start above the top.
    /// </summary>
    private (int Light, int Text) Tops()
    {
        var light = CapitalsMiddle() - Side / 2;

        return light >= 0 ? (light, 0) : (0, -light);
    }

    /// <summary>How tall the two come to at the width proposed. With none, the text on one line.</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        // A label treats a width of 1 as none at all; so does this.
        var width = proposedSize.Width is > 1 and < int.MaxValue / 4
            ? proposedSize.Width
            : Side + UiLayout.LabelGap + _text.GetPreferredSize(Size.Empty).Width;

        var (light, text) = Arrange(width);

        return new Size(width, Math.Max(light.Bottom, text.Bottom));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        Place();
    }

    /// <summary>Coming into view: while it was hidden, what it said was not laid out - see TextRewritten.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            PerformLayout();
        }
    }

    /// <summary>
    /// The status has been rewritten. Nothing tells the line: the label does
    /// not size itself, so it does not ask. So this asks - the whole line,
    /// and whatever it is in, only when it changes height; otherwise the
    /// text is just given the room it now needs, as FieldRow does for its
    /// summary.
    /// </summary>
    private void TextRewritten(object? sender, EventArgs e)
    {
        if (!Visible)
        {
            return;
        }

        if (GetPreferredSize(new Size(Math.Max(2, ClientSize.Width), 0)).Height != Height)
        {
            PerformLayout();
        }
        else
        {
            Place();
        }
    }

    private void Place()
    {
        var (light, text) = Arrange(Math.Max(2, ClientSize.Width));

        if (_light.Bounds != light)
        {
            _light.Bounds = light;
        }

        if (_text.Bounds != text)
        {
            _text.Bounds = text;
        }
    }

    /// <summary>
    /// The light on the left, level with the capitals of the first line
    /// (Tops); the text beside it, wrapped to the rest of the width.
    /// </summary>
    private (Rectangle Light, Rectangle Text) Arrange(int width)
    {
        var side = Side;
        var (lightTop, textTop) = Tops();
        var left = side + UiLayout.LabelGap;
        var room = Math.Max(2, width - left);
        var height = Math.Max(TextLine, _text.GetPreferredSize(new Size(room, 0)).Height);

        return (
            new Rectangle(0, lightTop, side, side),
            new Rectangle(left, textTop, room, height));
    }
}

/// <summary>
/// One number with a caption under it, on a card of its own - the theme's
/// rounded card, as a Section is. Next Steps' day so far; Race Analytics'
/// overview. A row of them is a TileStrip; the page gives the value and
/// the caption their faces (TextSize.Wear: the title face and the micro
/// one), which the strip sizes the tiles to.
///
/// One line each, cut with an ellipsis rather than wrapped. A round
/// label in the last tile used to wrap to a second line, the tile's
/// contents then overran the tile, and the old layout pass turned
/// scrolling on INSIDE the tile - the stray bars that showed across
/// "classes racing".
/// </summary>
internal sealed class StatTile
{
    public readonly TileCard Panel = new();

    public readonly Label Value = new() { AutoSize = false, AutoEllipsis = true, ForeColor = AppTheme.TextDark };
    public readonly Label Caption = new() { AutoSize = false, AutoEllipsis = true, ForeColor = AppTheme.TextMuted };

    public StatTile()
    {
        Panel.Controls.Add(Value);
        Panel.Controls.Add(Caption);
    }

    public void Set(string value, string caption)
    {
        Value.Text = value;
        Caption.Text = AppTheme.Spaced(caption.ToUpperInvariant());
    }
}

/// <summary>A tile's card: the theme's rounded surface, the page's colour in the corners - as Section paints itself.</summary>
internal sealed class TileCard : Panel
{
    public TileCard()
    {
        BackColor = AppTheme.Surface;
        Tag = AppTheme.KeepBackColor;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            e.Graphics.FillRectangle(behind, ClientRectangle);
        }

        AppTheme.DrawCard(e.Graphics, new RectangleF(0, 0, Width, Height));
    }
}

/// <summary>
/// The tiles, across the page in one row, sharing its width equally and
/// as tall as a value and a caption in the fonts in use. They get
/// narrower with the page rather than scrolling it sideways, and a value
/// too long for its tile ends in "...".
/// </summary>
internal sealed class TileStrip : Panel
{
    private readonly IReadOnlyList<StatTile> _tiles;

    public TileStrip(IReadOnlyList<StatTile> tiles)
    {
        _tiles = tiles;

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;

        foreach (var tile in tiles)
        {
            Controls.Add(tile.Panel);
        }
    }

    /// <summary>Room inside a tile, and between two.</summary>
    private const int Pad = UiLayout.RowGap;

    private static int ValueHeight => AppTheme.TitleFont.Height + 2;

    private static int CaptionHeight => AppTheme.MicroFont.Height + 2;

    private static int TileHeight => Pad + ValueHeight + UiLayout.HintGap + CaptionHeight + Pad;

    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width, TileHeight);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        if (_tiles.Count == 0)
        {
            return;
        }

        var height = TileHeight;
        var room = Math.Max(0, ClientSize.Width - Pad * (_tiles.Count - 1));

        for (var i = 0; i < _tiles.Count; i++)
        {
            // Shared out to the pixel: the last tile ends on the edge.
            var left = i * (room / _tiles.Count) + i * Pad;
            var width = i == _tiles.Count - 1 ? ClientSize.Width - left : room / _tiles.Count;
            var tile = _tiles[i];

            tile.Panel.Bounds = new Rectangle(left, 0, Math.Max(1, width), height);
            tile.Value.Bounds = new Rectangle(Pad, Pad, Math.Max(1, width - Pad * 2), ValueHeight);
            tile.Caption.Bounds = new Rectangle(Pad, Pad + ValueHeight + UiLayout.HintGap, Math.Max(1, width - Pad * 2), CaptionHeight);
        }
    }
}

/// <summary>
/// What a page keeps in step with the text size.
///
/// AppTheme replaces its fonts when the text size changes - it does not
/// resize them - so a control that was handed one keeps the old size. One
/// that wears the font it inherits needs nothing. One that wears a face of
/// its own - a heading, a hint in the small type, a page in the body face
/// inside a tab whose row is bold - is handed the new one here (Wear). So
/// is anything else sized from the fonts: a list five lines tall, a status
/// line with two lines kept for it, a field as wide as it was at the normal
/// text size (Keep).
///
/// The page calls Apply when its own font changes and when its parent's
/// does. The second is the one that matters for a page that wears a face of
/// its own, which is not told the first way: Race Day's pages sit in tabs
/// whose row is drawn in the bold face (AppTheme.StyleTabControl), which
/// they would otherwise inherit, so they wear the body face themselves.
/// </summary>
internal sealed class TextSize
{
    private readonly List<Action> _kept = new();

    /// <summary>Set while the kept things are being applied - one of them may change the page's own font, which calls Apply again.</summary>
    private bool _applying;

    /// <summary>Gives a control a face of its own, and keeps it at the text size in use.</summary>
    public T Wear<T>(T control, Func<Font> face) where T : Control
    {
        Keep(() =>
        {
            var font = face();

            if (!Equals(control.Font, font))
            {
                control.Font = font;
            }
        });

        return control;
    }

    /// <summary>
    /// Keeps <paramref name="lines"/> lines of its font for a label whatever
    /// it says, so what is under it does not hop as it is rewritten - a
    /// status line. A label that sizes itself still grows past them for
    /// more. Give it its face first: the lines are of the font it has.
    /// </summary>
    public T ReserveLines<T>(T label, int lines) where T : Label
    {
        Keep(() =>
        {
            var least = new Size(0, lines * label.Font.Height + label.Padding.Vertical);

            if (label.MinimumSize != least)
            {
                label.MinimumSize = least;
            }
        });

        return label;
    }

    /// <summary>Anything else sized from the fonts. Done now, and again whenever the text size changes.</summary>
    public void Keep(Action apply)
    {
        _kept.Add(apply);
        apply();
    }

    /// <summary>Everything kept, at the fonts AppTheme has now. In the order they were kept, so a face goes on before anything measured in it.</summary>
    public void Apply()
    {
        if (_applying)
        {
            return;
        }

        _applying = true;

        try
        {
            foreach (var apply in _kept)
            {
                apply();
            }
        }
        finally
        {
            _applying = false;
        }
    }
}

/// <summary>What the pieces share.</summary>
internal static class Pieces
{
    /// <summary>
    /// Keeps every column of a grid at least as wide as its whole heading
    /// (AppTheme.HeaderLeastWidth). Past that, a grid whose columns fill it
    /// shares out what is left by their weights; one too narrow for all the
    /// headings together scrolls sideways rather than cutting them to
    /// "...". Run again when the text size changes - through TextSize.Keep -
    /// since the heading face grows with it.
    ///
    /// Weights alone gave every column its share whatever its heading
    /// needed: "CARRIED IN" cut short at the normal text size beside a
    /// driver column with room to spare, and every heading of the
    /// championship standings cut short at 150% (layout pictures, 29 Sep).
    ///
    /// A grid whose columns are built again - Practice's, from the columns
    /// picked for it - is given this again after each rebuild, which keeps
    /// the new columns' weights and headings the same way.
    /// </summary>
    internal static void KeepHeadingsWhole(DataGridView grid)
    {
        if (!Kept.TryGetValue(grid, out _))
        {
            Kept.Add(grid, new object());
            grid.SizeChanged += (_, _) => KeepFillWeights(grid);
        }

        foreach (DataGridViewColumn column in grid.Columns)
        {
            // The weight the column was built with, kept the first time it
            // is seen - see KeepFillWeights.
            if (!BuiltWeights.TryGetValue(column, out _))
            {
                BuiltWeights.Add(column, new StrongBox<float>(column.FillWeight));
            }

            var least = Math.Max(2, AppTheme.HeaderLeastWidth(column.HeaderText));

            if (column.MinimumWidth != least)
            {
                column.MinimumWidth = least;
            }
        }

        KeepFillWeights(grid);
    }

    /// <summary>The grids given KeepHeadingsWhole, each watched for a new size once.</summary>
    private static readonly ConditionalWeakTable<DataGridView, object> Kept = new();

    /// <summary>
    /// The fill weight each column of those grids was built with. Kept per
    /// column rather than per grid, so a grid whose columns are built again
    /// never has the old ones' weights put on the new.
    /// </summary>
    private static readonly ConditionalWeakTable<DataGridViewColumn, StrongBox<float>> BuiltWeights = new();

    /// <summary>
    /// Puts a grid's fill weights back to the ones it was built with.
    ///
    /// A grid whose columns fill it takes a column pushed to a new width -
    /// by its minimum, or by being squeezed down to it - as a new weight for
    /// that column, and keeps it: once the minimums went on, the transponder
    /// column on Handicaps and the asset tag on Rentals took half the list
    /// at every size after, the others squeezed up beside them (layout
    /// pictures, 29 Sep). So the weights are put back after the minimums
    /// are set and whenever the grid is resized. A column added since the
    /// grid was last given KeepHeadingsWhole is left alone until it is.
    /// </summary>
    private static void KeepFillWeights(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (BuiltWeights.TryGetValue(column, out var weight) && column.FillWeight != weight.Value)
            {
                column.FillWeight = weight.Value;
            }
        }
    }

    /// <summary>
    /// Fits a details-view list's columns to its width - now, and again
    /// whenever its room or its font changes (a scrollbar arriving, a new
    /// text size). Each column is the width it had at the normal text size,
    /// at the size in use, or its heading's if that is wider; the one that
    /// stretches takes whatever the others leave, never less than its own.
    /// When that does not fit, the others give way first, down to their
    /// headings, before the list scrolls sideways.
    ///
    /// As Driver Profiles' roster does it (ProfileManagerControl.FitColumns,
    /// whose own least is a photo and a name). At fixed widths a list's
    /// columns overran it at 1280x720 and at 200% text, and stopped short of
    /// it on a wide screen with a blank band down the right.
    /// </summary>
    /// <param name="stretch">The column that takes what the others leave.</param>
    /// <param name="widths">Every column's width at the normal text size, in order - the stretching one's is its least.</param>
    internal static void FitListColumns(ListView list, int stretch, params int[] widths)
    {
        void Fit() => FitListColumnsNow(list, stretch, widths);

        list.ClientSizeChanged += (_, _) => Fit();
        list.FontChanged += (_, _) => Fit();
        Fit();
    }

    /// <summary>Set while a list's columns are being sized - which can bring a scrollbar in or take it away, and so change the room they are being sized to. One per thread: the tests build windows on threads of their own.</summary>
    [ThreadStatic]
    private static bool _fittingList;

    private static void FitListColumnsNow(ListView list, int stretch, int[] widths)
    {
        if (_fittingList || list.Columns.Count != widths.Length || stretch < 0 || stretch >= widths.Length)
        {
            return;
        }

        _fittingList = true;

        try
        {
            var count = widths.Length;
            var least = new int[count];
            var wanted = new int[count];

            for (var i = 0; i < count; i++)
            {
                least[i] = ListHeadingWidth(list.Columns[i].Text);
                wanted[i] = Math.Max(least[i], UiLayout.AtTextSize(widths[i]));
            }

            var stretchLeast = wanted[stretch];
            var others = Enumerable.Range(0, count).Where(i => i != stretch).ToList();
            var wantedTotal = others.Sum(i => wanted[i]);
            var leastTotal = others.Sum(i => least[i]);
            var forOthers = list.ClientSize.Width - stretchLeast;
            var used = 0;

            foreach (var i in others)
            {
                var width = forOthers >= wantedTotal || wantedTotal == leastTotal
                    ? wanted[i]
                    : forOthers <= leastTotal
                        ? least[i]
                        : least[i] + (wanted[i] - least[i]) * (forOthers - leastTotal) / (wantedTotal - leastTotal);

                if (list.Columns[i].Width != width)
                {
                    list.Columns[i].Width = width;
                }

                used += width;
            }

            var stretched = Math.Max(stretchLeast, list.ClientSize.Width - used);

            if (list.Columns[stretch].Width != stretched)
            {
                list.Columns[stretch].Width = stretched;
            }
        }
        finally
        {
            _fittingList = false;
        }
    }

    /// <summary>A list's column heading as its header draws it, whole, with the inset round it - see ListViewHeaderSkin.LeastWidth.</summary>
    private static int ListHeadingWidth(string heading) => ListViewHeaderSkin.LeastWidth(heading);

    /// <summary>
    /// Puts a part on the next row of a one-column table: stretched across
    /// it, or at its own width on the left, with a gap below.
    /// </summary>
    internal static void PlaceInColumn(TableLayoutPanel column, Control part, bool stretch, int gapBelow)
    {
        part.Anchor = stretch ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top : AnchorStyles.Left | AnchorStyles.Top;
        part.Margin = new Padding(0, 0, 0, Math.Max(0, gapBelow));

        column.RowCount++;
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        column.Controls.Add(part, 0, column.RowCount - 1);
    }

    /// <summary>
    /// The same, on row <paramref name="row"/> rather than the next: every
    /// row from there down, and its style, moves down one to make room.
    /// </summary>
    internal static void PlaceInColumnAt(TableLayoutPanel column, Control part, bool stretch, int gapBelow, int row)
    {
        part.Anchor = stretch ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top : AnchorStyles.Left | AnchorStyles.Top;
        part.Margin = new Padding(0, 0, 0, Math.Max(0, gapBelow));

        row = Math.Clamp(row, 0, column.RowCount);

        column.SuspendLayout();

        try
        {
            // The lowest first, so no two are ever given the same cell on the
            // way - a table that finds two in one cell moves one of them.
            var below = column.Controls
                .Cast<Control>()
                .Where(control => column.GetRow(control) >= row)
                .OrderByDescending(control => column.GetRow(control))
                .ToList();

            foreach (var control in below)
            {
                column.SetRow(control, column.GetRow(control) + 1);
            }

            column.RowCount++;
            column.RowStyles.Insert(row, new RowStyle(SizeType.AutoSize));
            column.Controls.Add(part, 0, row);
        }
        finally
        {
            column.ResumeLayout(true);
        }
    }

    /// <summary>
    /// What a drop-down needs across for the longest thing it shows -
    /// measured the way LayoutAudit checks one, padding and all, with room
    /// for the arrow and either side of the text - and never less than
    /// <paramref name="alsoFits"/> needs, for a choice it can show that is
    /// not in its list yet.
    ///
    /// Kept in <paramref name="kept"/> until the drop-down's font or the
    /// length of its list changes: a row is measured many times in one
    /// layout, and a round list can run to hundreds. So forget it (set it
    /// to null) when the list is rebuilt, which can change what is in it
    /// without changing how long it is. Capped at the first few hundred
    /// choices, like the audit's own check - they say as much as all of
    /// them.
    /// </summary>
    internal static int PickerWidth(ComboBox picker, ref (Font Font, int Count, int Width)? kept, string alsoFits = "")
    {
        if (kept is { } known && ReferenceEquals(known.Font, picker.Font) && known.Count == picker.Items.Count)
        {
            return known.Width;
        }

        var widest = string.IsNullOrEmpty(alsoFits) ? 0 : TextRenderer.MeasureText(alsoFits, picker.Font).Width;

        foreach (var item in picker.Items.Cast<object>().Take(400))
        {
            var text = picker.GetItemText(item) ?? string.Empty;

            widest = Math.Max(widest, TextRenderer.MeasureText(text, picker.Font).Width);
        }

        var width = widest + SystemInformation.VerticalScrollBarWidth + 24;

        kept = (picker.Font, picker.Items.Count, width);
        return width;
    }

    /// <summary>
    /// Keeps the height the layouts go by for a drop-down, a one-line text
    /// box or a number box in step with the height it really has.
    ///
    /// Their heights are their own to decide - a drop-down's from its font
    /// and, once the theme draws it, its row height; a box's from its font
    /// and its border, which the theme takes off (InputOutline) - and they
    /// settle them when their window is made, their font changes or their
    /// border goes. That does not go through the size the layouts were
    /// given: a table measures a part that does not size itself by the
    /// size it was last given, and nothing told it again. So a drop-down
    /// built at one height and drawn at another was centred on a row made
    /// for the first, and ran over the hint under it by the difference -
    /// Demo Mode's two pickers at 150% and 200% (layout test, 29 Sep); and a
    /// name box sat at the top of a row made for it with its border, beside
    /// a caption centred lower down, the top of its outline cut off by the
    /// grid's edge (Setup's and Venues' Name:, layout pictures, 29 Sep).
    ///
    /// So when its height changes, it is given that height, and the layout
    /// it is in lays it out at that. With a nudge down a pixel and back,
    /// the height passed with each: given the bounds it already has, a
    /// control does nothing at all. Never its width - a width passed here
    /// would become the one the layouts go by, and one a table had cut to a
    /// cell (a page not yet at its size) would stay cut. Once per drop-down,
    /// however many pieces it passes through.
    /// </summary>
    internal static void KeepHeightTold(Control field)
    {
        if (field is not (ComboBox or UpDownBase or TextBoxBase) || HeightsTold.TryGetValue(field, out _))
        {
            return;
        }

        var told = new StrongBox<int>(field.Height);
        HeightsTold.Add(field, told);

        field.SizeChanged += (_, _) =>
        {
            // Anything it is told below comes back here with the height it
            // was just told, and stops. A box of several lines is given its
            // height by its page, which the layouts already go by.
            if (field.IsDisposed || field.Height == told.Value || field is TextBoxBase { Multiline: true })
            {
                return;
            }

            told.Value = field.Height;

            // A move raises no SizeChanged; the second is nothing when the
            // layout has already put it back.
            var top = field.Top;

            field.SetBounds(0, top + 1, 0, told.Value, BoundsSpecified.Y | BoundsSpecified.Height);
            field.SetBounds(0, top, 0, told.Value, BoundsSpecified.Y | BoundsSpecified.Height);
        };
    }

    /// <summary>The height each field given KeepHeightTold was last given, so its own changes are told apart from the window's.</summary>
    private static readonly ConditionalWeakTable<Control, StrongBox<int>> HeightsTold = new();

    /// <summary>
    /// Whether a part sizes itself - is asked how big it wants to be -
    /// rather than keeping the size it was given. A text box says it does
    /// (AutoSize) and does not, as far as WinForms' own layouts are
    /// concerned: its "AutoSize" is only its one line's height, which it
    /// keeps itself, and TextBoxBase does not pass it on to the layouts,
    /// which size a text box by the bounds it was given. Asked for its size, a
    /// three-line notes box answers with its text's - one line when empty -
    /// so a card measuring it that way came out two lines shorter than the
    /// table inside laid it out. Measured the way the tables measure, the
    /// two agree.
    /// </summary>
    internal static bool SizesItself(Control part) => part.AutoSize && part is not TextBoxBase;

    /// <summary>The FillPage a control is in, however deep, or null.</summary>
    internal static FillPage? FillPageOf(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is FillPage page)
            {
                return page;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a part in a table that sizes itself has come out a different
    /// height from the one it wants at the width the table has just given
    /// it - the table is then laid out once more.
    ///
    /// A table works out a row's height before it widens the part in it, and
    /// a part that wraps - a row of buttons, a Session row, a hint - wants
    /// fewer lines once it is wider. The part lays itself out at the new
    /// width and asks the table to lay out again, but the table is still
    /// laying out, and WinForms drops the request (Control.PerformLayout
    /// clears it on the way out). So the row kept the height the part had
    /// when it was narrow: Race Control's cards, made while the page was
    /// narrow and then widened, kept two lines of flags' height under one.
    /// Checked after every layout, and a second pass then measures the
    /// parts at the widths they have now. Only a part that sizes itself,
    /// on screen and not docked, is asked - and not a text box, whose
    /// "AutoSize" is its one line's height and nothing to do with this.
    /// </summary>
    internal static bool OutOfStep(TableLayoutPanel table)
    {
        foreach (Control part in table.Controls)
        {
            if (!part.Visible || !SizesItself(part) || part.Dock != DockStyle.None || part is ThinScrollBar)
            {
                continue;
            }

            var wanted = part.GetPreferredSize(new Size(Math.Max(1, part.Width), 0)).Height;

            if (wanted != part.Height)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The gap below the lowest row of a table - the space a row keeps
    /// before the next one, which the last row has no next one for.
    ///
    /// Each part carries its gap as a bottom margin, which is how a table
    /// spaces its rows; left in, the last one's gap is added to whatever
    /// the table sits in, and every card and grid came out with a row-gap
    /// more below its content than above it. Handed back instead: the
    /// table is sized without it, and the margin hangs past its edge where
    /// nothing is drawn. The controls themselves are all inside.
    ///
    /// A control hidden in its own right leaves its row out, as the table
    /// does. While the table itself is hidden every control reports itself
    /// hidden, so then all of them count.
    /// </summary>
    internal static int TrailingGap(TableLayoutPanel table)
    {
        var lowest = -1;
        var gap = 0;

        foreach (Control control in table.Controls)
        {
            if (table.Visible && !control.Visible || control is ThinScrollBar)
            {
                continue;
            }

            var row = table.GetRow(control);

            if (row > lowest)
            {
                lowest = row;
                gap = control.Margin.Bottom;
            }
            else if (row == lowest)
            {
                gap = Math.Max(gap, control.Margin.Bottom);
            }
        }

        return gap;
    }

    /// <summary>A table's preferred size with <see cref="TrailingGap"/> taken off the height.</summary>
    internal static Size WithoutTrailingGap(Size size, TableLayoutPanel table) =>
        new(size.Width, Math.Max(0, size.Height - TrailingGap(table)));

    /// <summary>
    /// Scrolls down, never sideways. The sideways bar is switched off BEFORE
    /// AutoScroll goes on - once it is on, WinForms ignores these settings
    /// (LayoutDoctor found the same, the hard way).
    /// </summary>
    internal static void ScrollDownOnly(ScrollableControl scrollable)
    {
        scrollable.HorizontalScroll.Maximum = 0;
        scrollable.HorizontalScroll.Enabled = false;
        scrollable.HorizontalScroll.Visible = false;
        scrollable.AutoScroll = true;
    }
}
