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
//
// The rules, so a page built from these stays built from them:
//   - Nothing gets a Location. The piece places it.
//   - Nothing gets a width from a literal unless it is a field sized for
//     what goes IN it (a spinner for three digits). Captions, buttons and
//     hints are measured.
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
        // Once more if it did; the second placing cannot change the answer
        // back (narrower only ever makes the column taller, so it still
        // needs the bar that narrowed it).
        base.OnLayout(levent);

        if (PlaceColumn())
        {
            base.OnLayout(levent);
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
        // that wraps is. Once more if it did; the second answer cannot undo
        // the first (narrower only ever makes the parts taller, so the bar
        // that narrowed them is still needed).
        base.OnLayout(levent);

        if (UpdateScrollRange())
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

        var least = Padding.Vertical + RowsHeight();

        if (AutoScrollMinSize.Height == least && AutoScrollMinSize.Width == 0)
        {
            return false;
        }

        // Its own request for a layout is dropped - this is already one.
        AutoScrollMinSize = new Size(0, least);
        return true;
    }

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
        if (Visible && _rows is not null && AutoScrollMinSize.Height != Padding.Vertical + RowsHeight())
        {
            PerformLayout();
        }
    }

    /// <summary>The height the rows can least do with: the others as tall as they need, the filling ones at their least.</summary>
    private int RowsHeight()
    {
        var width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
        var total = 0;

        foreach (Control part in _rows.Controls)
        {
            // A themed scrollbar is parked outside the tables (ThinScrollBar
            // .HostFor), but one is never a part either way.
            if (!part.Visible || part is ThinScrollBar)
            {
                continue;
            }

            var height = _leastHeights.TryGetValue(part, out var leastHeight)
                ? Math.Max(0, leastHeight(Math.Max(1, width - part.Margin.Horizontal)))
                : part.AutoSize
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
                : part.AutoSize
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
            var width = part.AutoSize ? part.GetPreferredSize(Size.Empty).Width : part.Width;

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
/// assembled, measured rather than set at 180px and hoped for.
///
/// A caption is centred on its field. A hint goes under the field it is
/// about, in the field's column, and wraps to the room there.
/// </summary>
internal sealed class FieldGrid : TableLayoutPanel
{
    /// <summary>The controls of the last row added, whose gap below a hint takes the place of.</summary>
    private readonly List<Control> _lastRow = new();

    /// <summary>The hints, which wear the small font rather than this grid's.</summary>
    private readonly List<Label> _hints = new();

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

        // An inline row wraps to the width it is given, so it has to be
        // given the column's.
        var stretched = stretch || field is FlowLayoutPanel;

        field.Anchor = stretched ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
        field.Margin = new Padding(0, 0, 0, UiLayout.RowGap);

        RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(label, 0, RowCount - 1);
        Controls.Add(field, 1, RowCount - 1);

        _lastRow.Clear();
        _lastRow.Add(label);
        _lastRow.Add(field);

        return label;
    }

    /// <summary>A control with no caption - a check box, a note - across both columns.</summary>
    public T AddWide<T>(T control, bool stretch = true) where T : Control
    {
        control.Anchor = stretch ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
        control.Margin = new Padding(0, 0, 0, UiLayout.RowGap);

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
    public Label AddHint(string text)
    {
        // The row above sits a hint's gap from its hint, not a row's.
        foreach (var control in _lastRow)
        {
            control.Margin = new Padding(control.Margin.Left, control.Margin.Top, control.Margin.Right, UiLayout.HintGap);
        }

        var hint = new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 0, 0, UiLayout.RowGap),
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(hint, 1, RowCount - 1);

        _lastRow.Clear();
        _lastRow.Add(hint);
        _hints.Add(hint);

        return hint;
    }

    /// <summary>
    /// Every row carries the gap to the next one below it, and the last row
    /// has no next one - see Pieces.TrailingGap.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize) =>
        Pieces.WithoutTrailingGap(base.GetPreferredSize(proposedSize), this);

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
    /// caption exactly as one in a bar is. A label among them sizes itself
    /// and is centred on the row.
    /// </summary>
    public static FlowLayoutPanel Inline(params Control[] controls)
    {
        foreach (var label in controls.OfType<Label>())
        {
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
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
/// A pair whose field is hidden is left out, and the line closes up behind
/// it. Hide the caption with it.
///
/// Sizes itself: put it in a Section or a FieldGrid, which give it the width
/// it wraps to.
/// </summary>
internal sealed class FieldRow : Panel
{
    private readonly List<(Label Caption, Control Field, Func<int>? Width)> _pairs = new();

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
    /// A caption and a field that keeps the width it was given - or, with
    /// <paramref name="width"/>, the width that says, asked whenever the row
    /// is measured: a drop-down as wide as its longest choice at the text
    /// size in use. Returns the caption.
    /// </summary>
    public Label Add(Label caption, Control field, Func<int>? width = null)
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
    public Label AddLast(Label caption, Control field, Func<int> leastWidth)
    {
        if (_lastLeast is not null)
        {
            throw new InvalidOperationException("A row has one pair that takes the width left.");
        }

        Pair(caption, field, width: null);
        _lastLeast = leastWidth;
        return caption;
    }

    private void Pair(Label caption, Control field, Func<int>? width)
    {
        caption.AutoSize = true;
        caption.Margin = Padding.Empty;
        field.Margin = Padding.Empty;

        _pairs.Add((caption, field, width));
        Controls.Add(caption);
        Controls.Add(field);
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
        var lineHeight = pairs.Max(pair => Math.Max(CaptionSize(pair.Caption).Height, pair.Field.Height));

        var last = _lastLeast is null ? null : _pairs[^1].Field;
        var x = 0;
        var line = 0;
        var widest = 0;

        foreach (var (caption, field, measure) in pairs)
        {
            var captionSize = CaptionSize(caption);
            var stretches = ReferenceEquals(field, last);

            var fieldWidth = stretches
                ? Math.Max(1, _lastLeast!())
                : measure is not null
                    ? Math.Max(1, measure())
                    : field.Width;
            var needed = captionSize.Width + UiLayout.LabelGap + fieldWidth;
            var left = x == 0 ? 0 : x + UiLayout.SectionGap;

            if (x > 0 && left + needed > width)
            {
                line++;
                left = 0;
            }

            var top = line * (lineHeight + UiLayout.RowGap);
            var fieldLeft = left + captionSize.Width + UiLayout.LabelGap;

            // To the end of the line - when there is an end to go to. On a
            // line too narrow even for its least, it takes the line and no
            // more: past the edge is worse than a narrow picker.
            if (stretches && width < Unbounded)
            {
                fieldWidth = Math.Max(1, width - fieldLeft);
            }

            if (place)
            {
                caption.SetBounds(left, top + (lineHeight - captionSize.Height) / 2, captionSize.Width, captionSize.Height);
                field.SetBounds(fieldLeft, top + (lineHeight - field.Height) / 2, fieldWidth, field.Height);
            }

            x = fieldLeft + fieldWidth;
            widest = Math.Max(widest, x);
        }

        return new Size(widest, (line + 1) * lineHeight + line * UiLayout.RowGap);
    }

    /// <summary>A caption's size on one line, measured rather than read back: an AutoSize label has not sized itself until it has been laid out.</summary>
    private static Size CaptionSize(Label caption) => caption.GetPreferredSize(Size.Empty);
}

/// <summary>
/// A column down the right-hand side, beside the page's main parts - Race
/// Control's clock and the buttons under it, beside the flags and the
/// leaderboard.
///
/// On the left, parts one under another as a FillPage has them: each as
/// tall as it needs to be, and one that takes the height left over (Add,
/// AddFill). On the right, the column (AddSide): as wide as the widest
/// button in it needs, and never narrower than it was told, with every part
/// in it that wide, one under another from the top.
///
/// Goes in a FillPage as its filling part, with LeastHeight as the least it
/// can do with - the column, or the left-hand parts with the filling one at
/// its least, whichever is taller - so a window too short for either
/// scrolls rather than squashing the list or running the column off the
/// bottom.
///
/// The column is placed by hand rather than by a table: its width comes
/// from the buttons' captions, not from what the parts in it would report,
/// and every part in it is given that width.
/// </summary>
internal sealed class Beside : Panel
{
    private readonly MainColumn _main;
    private readonly List<(Control Part, int GapBelow)> _side = new();

    /// <summary>What each button in the column needs across for its caption. The column is as wide as the widest.</summary>
    private readonly Dictionary<Button, int> _captionWidths = new();

    private readonly int _sideLeastWidth;

    /// <summary>The panel holding the part on the left that takes the height left over, and the least that part can do with.</summary>
    private Control? _fillHolder;
    private Func<int, int>? _fillLeast;

    /// <summary>Set while a button is being sized, so the size change it causes is not taken for a caption change.</summary>
    private bool _fitting;

    /// <param name="sideLeastWidth">The narrowest the column gets - the width its drawn parts (a clock) were designed at.</param>
    public Beside(int sideLeastWidth = 0)
    {
        _sideLeastWidth = Math.Max(0, sideLeastWidth);

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
    /// is not asked how tall it wants to be.
    /// </summary>
    public T AddSide<T>(T part, int gapBelow = UiLayout.RowGap) where T : Control
    {
        // Sized here, never by the layout: one that sized itself would be
        // resized by the panel to its width on one line.
        part.AutoSize = false;
        part.Margin = Padding.Empty;

        _side.Add((part, Math.Max(0, gapBelow)));
        Controls.Add(part);

        if (part is Button button)
        {
            button.TextChanged += ButtonChanged;
            button.FontChanged += ButtonChanged;
            Fit(button);
        }

        return part;
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
                : part.AutoSize
                    ? part.GetPreferredSize(new Size(partWidth, 0)).Height
                    : part.Height;

            main += height + part.Margin.Vertical;
        }

        return Math.Max(side.Height, main);
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

    /// <summary>The left-hand parts across the width the column leaves, the column against the right-hand edge.</summary>
    private void Place()
    {
        var client = ClientSize;
        var side = MeasureSide();

        var main = new Rectangle(0, 0, MainWidth(client.Width, side), Math.Max(0, client.Height));

        if (_main.Bounds != main)
        {
            _main.Bounds = main;
        }

        var left = client.Width - side.Width;
        var y = 0;

        foreach (var (part, gapBelow) in _side)
        {
            if (!Counts(part))
            {
                continue;
            }

            var bounds = new Rectangle(left, y, side.Width, part.Height);

            if (part.Bounds != bounds)
            {
                part.Bounds = bounds;
            }

            y += part.Height + gapBelow;
        }
    }

    /// <summary>The column's width, and how tall its parts come to one under another. Empty when it has none.</summary>
    private Size MeasureSide()
    {
        var width = _sideLeastWidth;
        var height = 0;
        var gapAbove = 0;
        var any = false;

        foreach (var (part, gapBelow) in _side)
        {
            if (!Counts(part))
            {
                continue;
            }

            if (part is Button button && _captionWidths.TryGetValue(button, out var needs))
            {
                width = Math.Max(width, needs);
            }

            height += (any ? gapAbove : 0) + part.Height;
            gapAbove = gapBelow;
            any = true;
        }

        return any ? new Size(width, height) : Size.Empty;
    }

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
            }

            base.OnLayout(levent);

            if (Pieces.OutOfStep(this))
            {
                base.OnLayout(levent);
            }
        }
    }
}

/// <summary>What the pieces share.</summary>
internal static class Pieces
{
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
            if (!part.Visible || !part.AutoSize || part.Dock != DockStyle.None || part is ThinScrollBar or TextBoxBase)
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
