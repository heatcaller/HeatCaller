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

    /// <summary>The filling parts, with the least height each can do with.</summary>
    private readonly Dictionary<Control, int> _leastHeights = new();

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
    public T AddFill<T>(T part, int leastHeight, int gapBelow = UiLayout.SectionGap) where T : Control
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

        _leastHeights[holder] = Math.Max(0, leastHeight);

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
                ? leastHeight
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
            if (Parent is FillPage { Visible: true } page && page.AutoScrollMinSize.Height != page.Padding.Vertical + page.RowsHeight())
            {
                page.PerformLayout();
            }

            base.OnLayout(levent);
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
    /// The gap under the last part is the card's own inset's job, not the
    /// part's - see Pieces.TrailingGap.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize) =>
        Pieces.WithoutTrailingGap(base.GetPreferredSize(proposedSize), this);

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
    /// <summary>Set while a button is being sized, so the size change it causes is not taken for a caption change.</summary>
    private bool _fitting;

    /// <param name="alignRight">Buttons against the right-hand edge, the first one furthest right - a dialog's OK and Cancel.</param>
    public ButtonBar(bool alignRight = false)
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = true;
        FlowDirection = alignRight ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;
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
            ? new Padding(UiLayout.ButtonGap, 0, 0, UiLayout.ButtonGap)
            : new Padding(0, 0, UiLayout.ButtonGap, UiLayout.ButtonGap);

        if (control is Button button)
        {
            Fit(button);
            button.TextChanged += ButtonChanged;
            button.FontChanged += ButtonChanged;
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
            ? new Size(size.Width, Math.Max(0, size.Height - UiLayout.ButtonGap))
            : size;
    }

    private void ButtonChanged(object? sender, EventArgs e)
    {
        if (sender is Button button)
        {
            Fit(button);
        }
    }

    private void Fit(Button button)
    {
        if (_fitting)
        {
            return;
        }

        _fitting = true;

        try
        {
            button.AutoSize = false;
            button.Size = UiLayout.ButtonSizeFor(button);
        }
        finally
        {
            _fitting = false;
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
