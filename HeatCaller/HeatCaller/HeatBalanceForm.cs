using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// A class's heats side by side, with the drivers in them draggable from
/// one to another.
///
/// The Program tab's grid is a list of ROUNDS - it can say that Heat 2 has
/// four drivers in it, but not who they are, and certainly not who is in
/// Heat 1 at the same time. Moving a late arrival out of one heat and into
/// another meant opening one round's roster editor, unticking a name,
/// closing it, opening another round's, and finding the same name again -
/// with no point in that sequence where both heats were on screen at once.
/// The thing being balanced was never visible.
///
/// So this is the other view of the same data: rounds across, drivers
/// down, and a drag between them.
///
/// THREE RULES SHAPE THE WHOLE FORM.
///
/// Nothing is written until Save. Every column edits a copy, and Cancel
/// really cancels - a roster half-moved between two heats is worse than
/// no move at all, and the caller wants one undo entry for the session's
/// work rather than one per drag.
///
/// A recorded heat is locked, as a source and as a target. Running order
/// is presentation and can be corrected after the fact; a roster is what
/// a recorded result was computed against, and editing one silently
/// invalidates the result linked to it.
///
/// The "not in a heat" column is DERIVED, never edited. It is recomputed
/// after every move from the class roster minus everyone currently in a
/// heat, which is what makes dropping onto it mean "take them out" and
/// dragging off it mean "put them in" without two things claiming to own
/// the same fact.
///
/// "In a heat" is judged PER QUALIFYING ROUND. A night of Q1, Q2 and Q3
/// puts every driver in one heat of each, so a late arrival dragged into
/// Q1 still needs a heat in Q2 and Q3 - they stay in the left column,
/// marked with the rounds they are missing, until they have one. And a
/// driver can be in only one heat of any qualifying round: a drop that
/// would put them in a second is refused rather than half-done.
///
/// On the layout pieces (DialogFit): the title and the hint across the top,
/// the strip of heats taking the height, and the spread line beside Save
/// and Cancel at the foot. Inside the strip the columns are the form's own
/// to place (LayoutColumns) - it is a board that drivers are dragged across,
/// not a stack of parts - each as wide as the longest name, heading and
/// count it can show at the text size in use, and the strip scrolls sideways
/// past the four the window opens wide enough for. It used to be placed in
/// pixels: the strip, the spread line and the buttons at fixed distances
/// from the foot of a 560px window, and the columns never wider than 380px
/// at any text size, so at 200% the names were cut short.
/// </summary>
public sealed class HeatBalanceForm : Form
{
    /// <summary>One round's roster before and after the edit, for the caller to apply and to undo.</summary>
    public sealed record RosterChange(string RoundId, List<string> Before, List<string> After);

    /// <summary>What a drag carries: where it came from, and who is being moved.</summary>
    private sealed record DriverDrag(int FromColumn, string Transponder);

    /// <summary>One driver in one list. ToString is never drawn - the list is owner-drawn - but keeps debugging readable.</summary>
    private sealed record Entry(string Transponder, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>One heat on screen, or the derived "not in a heat" column when Round is null.</summary>
    private sealed class Column
    {
        public Column(RoundDefinition? round, Panel host, Label header, Label count, ListBox list, bool locked, List<string> original)
        {
            Round = round;
            Host = host;
            Header = header;
            Count = count;
            List = list;
            Locked = locked;
            Original = original;
        }

        public RoundDefinition? Round { get; }
        public Panel Host { get; }
        public Label Header { get; }
        public Label Count { get; }
        public ListBox List { get; }
        public bool Locked { get; }
        public List<string> Original { get; }

        /// <summary>True for the derived column on the left.</summary>
        public bool IsSpares => Round is null;

        public List<string> Current =>
            List.Items.Cast<Entry>().Select(e => e.Transponder).ToList();
    }

    /// <summary>The gap between two columns, at the normal text size.</summary>
    private const int Gap = 10;

    /// <summary>Room either side of the text in a column - its heading, its count and every name - at the normal text size.</summary>
    private const int Inset = 8;

    /// <summary>Every column shows its heading and at least this many drivers before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    private readonly List<Column> _columns = new();
    private readonly Panel _strip = new();

    /// <summary>The spread, as tall as the longest it can come to - it is rewritten after every move (UpdateHeaders).</summary>
    private readonly SteadyLabel _lblSpread = new();

    private readonly Color _accent;

    // What the columns are sized from, at the text size in use. Worked out
    // once, as the window is built: it is short-lived, and is built at the
    // text size it opens at (see DialogFit).
    private readonly int _columnGap;
    private readonly int _textInset;
    private readonly int _headerTop;
    private readonly int _headerHeight;
    private readonly int _countHeight;
    private readonly int _itemHeight;

    /// <summary>Where a column's list starts: under its heading and its count.</summary>
    private readonly int _listTop;

    /// <summary>
    /// Everyone this form is allowed to show. The class roster plus anyone
    /// who was already in a heat when it opened - the second part matters,
    /// because a driver running a class they are not registered for is
    /// normal on a race day, and dragging one out of a heat must not make
    /// them disappear off the form altogether.
    /// </summary>
    private readonly Dictionary<string, string> _universe = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every column's width - see MeasureColumnWidth.</summary>
    private readonly int _columnWidth;

    /// <summary>
    /// True when every round handed in belongs to one race day - the
    /// caller had a race day open and passed only its rounds. Then a heat
    /// that has already run is tonight's, and counts for "one heat per
    /// qualifying round". See RunHeatCounts.
    /// </summary>
    private readonly bool _roundsAreOneRaceDay;

    /// <summary>
    /// The heat out on track when the form opened, or null. Locked like a
    /// recorded one: its race is running against the roster it has, and
    /// the phone refuses to change it for the same reason.
    /// </summary>
    private readonly string? _onTrackRoundId;

    /// <summary>
    /// With no race day open, how recently a run heat must have been made
    /// to count as tonight's. Long enough for the longest meeting, short
    /// enough that last week's heats never do.
    /// </summary>
    private static readonly TimeSpan NoRaceDayWindow = TimeSpan.FromHours(18);

    // --- Drag state ---
    private Column? _pressColumn;
    private int _pressIndex = -1;
    private Point _pressAt;
    private Column? _dropColumn;
    private int _dropIndex = -1;

    /// <summary>What changed. Empty when nothing did, even after an OK.</summary>
    public IReadOnlyList<RosterChange> Changes { get; private set; } = new List<RosterChange>();

    public HeatBalanceForm(
        string className,
        IReadOnlyList<RoundDefinition> rounds,
        IReadOnlyList<DriverProfile> classRoster,
        ProfileStore profiles,
        Color accent,
        bool roundsAreOneRaceDay = false,
        string? onTrackRoundId = null)
    {
        _accent = accent;
        _roundsAreOneRaceDay = roundsAreOneRaceDay;
        _onTrackRoundId = onTrackRoundId;

        Text = $"Move drivers between heats - {className}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        // A line of each face with a little room round it - measured as GDI
        // draws the line (Pieces.LineHeight), which for Barlow is taller than
        // Font.Height - and the gaps at the text size in use.
        _columnGap = UiLayout.AtTextSize(Gap);
        _textInset = UiLayout.AtTextSize(Inset);
        _headerTop = UiLayout.AtTextSize(4);
        _headerHeight = Pieces.LineHeight(AppTheme.BoldFont) + UiLayout.AtTextSize(8);
        _countHeight = Pieces.LineHeight(AppTheme.SmallFont) + UiLayout.AtTextSize(6);
        _itemHeight = Math.Max(UiLayout.AtTextSize(24), Pieces.LineHeight(AppTheme.BodyFont) + UiLayout.AtTextSize(6));
        _listTop = _headerTop + _headerHeight + _countHeight + _headerTop;

        BuildUniverse(rounds, classRoster, profiles);

        _columnWidth = MeasureColumnWidth(rounds);

        var lblTitle = new Label
        {
            Text = AppTheme.Spaced($"HEATS - {className.ToUpperInvariant()}"),
            AutoSize = true,
            Font = AppTheme.MicroFont,
            ForeColor = AppTheme.Primary,
        };

        var lblHint = new Label
        {
            Text = "Drag a driver from one heat to another. Drop onto \"Not in a heat\" - or press Delete - "
                 + "to take somebody out. Nothing is saved until you press Save.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _strip.AutoScroll = true;
        _strip.BackColor = AppTheme.Background;

        // The gaps between and around the columns are droppable too, so a
        // drag aimed slightly wide still scrolls the strip instead of
        // stalling over dead space.
        _strip.AllowDrop = true;
        _strip.DragOver += (_, e) =>
        {
            // Never a drop target itself - the gap between two heats is not
            // a heat, and a cursor that says otherwise is a lie. It accepts
            // the drag only so that a pointer held out here still scrolls
            // the strip towards the column being aimed for.
            e.Effect = DragDropEffects.None;

            ScrollStrip(new Point(e.X, e.Y));
        };

        _strip.Resize += (_, _) => LayoutColumns();

        BuildColumns(rounds);

        _lblSpread.ForeColor = AppTheme.TextMuted;
        _lblSpread.Font = AppTheme.SmallFont;
        _lblSpread.Wordings = SpreadWordings(rounds);

        var btnSave = new Button { Text = "Save" };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        btnSave.Click += (_, _) => Commit();

        var page = new FillPage();
        page.Add(lblTitle, gapBelow: UiLayout.HintGap);
        page.Add(lblHint, gapBelow: UiLayout.RowGap);

        // The heats take the height; the strip keeps a column's heading and
        // a few drivers, and room under them for the bar it scrolls
        // sideways with, before the window scrolls instead.
        page.AddFill(_strip, _ => ColumnLeast + SystemInformation.HorizontalScrollBarHeight, gapBelow: UiLayout.RowGap);

        // The spread on the left; Save and Cancel against the right-hand
        // edge, Cancel furthest right, as they always were - and Tab still
        // reaches Save first: the bar takes them right to left, in the order
        // they were added.
        page.Add(
            new SplitBar(
                new ButtonBar().With(_lblSpread),
                new ButtonBar(alignRight: true).With(btnCancel, btnSave)),
            gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        RefreshSpares();
        UpdateHeaders();
        LayoutColumns();

        // The strip scrolls sideways ON PURPOSE - that is how a class with
        // nine heats is looked at. The repair pass and the layout check both
        // report content past the right edge as a fault, which is the right
        // default everywhere else and wrong here, so the one keeps out and
        // the other is told it is meant.
        LayoutDoctor.LeaveAlone(_strip);
        LayoutAudit.AllowOverflow(_strip);

        // And no room under the columns. The theme pass gives a panel that
        // scrolls room below its last control (LayoutDoctor.ThemeScrolling),
        // for a page scrolled to its end; under columns that are exactly as
        // tall as the strip, it was a strip that also scrolled down, by that
        // much, for nothing.
        LayoutDoctor.NoRoomBelow(_strip);

        AppTheme.ApplyRecursively(this);

        // Wide enough for four columns, or for all of them when there are
        // fewer - a form that opens already scrolled hides the very thing
        // it exists to show - and never narrower than it always was. The
        // columns are measured at the text size in use, so the width is too.
        var wanted = UiLayout.PageLeft + UiLayout.PageRight + Math.Min(4, _columns.Count) * (_columnWidth + _columnGap);

        DialogFit.Keep(this, page, width: Math.Max(UiLayout.AtTextSize(640), wanted), height: 560, widthMeasured: true);
    }

    // ------------------------------------------------------------------
    //  Building
    // ------------------------------------------------------------------

    private void BuildUniverse(
        IReadOnlyList<RoundDefinition> rounds,
        IReadOnlyList<DriverProfile> classRoster,
        ProfileStore profiles)
    {
        foreach (var driver in classRoster)
        {
            _universe[driver.Transponder] = string.IsNullOrWhiteSpace(driver.DriverName)
                ? "(unnamed)"
                : driver.DriverName;
        }

        foreach (var transponder in rounds.SelectMany(r => r.AssignedTransponders))
        {
            if (_universe.ContainsKey(transponder))
            {
                continue;
            }

            var profile = profiles.Get(transponder);

            _universe[transponder] = string.IsNullOrWhiteSpace(profile?.DriverName)
                ? transponder
                : profile!.DriverName;
        }
    }

    private string NameOf(string transponder) =>
        _universe.TryGetValue(transponder, out var name) ? name : transponder;

    /// <summary>
    /// Every column's width: wide enough for the longest thing any of them
    /// shows, measured at the text size in use rather than picked at design
    /// time - the same fault that had every other page in this app clipping
    /// its captions once the text size went up.
    ///
    /// The longest name with the highest position number in front of it,
    /// as DrawEntry draws it, the room either side, and a scrollbar's width
    /// beside it - a list that scrolls has the native bar, with the themed
    /// one parked over it, taking that off its rows. Kept between two widths
    /// chosen at the normal text size, so a class of short names still reads
    /// as columns and one very long name does not make every column
    /// enormous; past the wider one a name ends in "...", as it always has.
    ///
    /// And never narrower than a heading or a count needs, which are labels
    /// and would be cut off: every heat's name, and every count a column can
    /// come to - any driver on the form can end up in any one heat, while a
    /// locked heat keeps the drivers it has.
    /// </summary>
    private int MeasureColumnWidth(IReadOnlyList<RoundDefinition> rounds)
    {
        var most = MostInAColumn(rounds);

        var longestName = _universe.Values.MaxBy(Drawn) ?? string.Empty;

        var entries = Math.Clamp(
            Drawn($"{WidestNumber(most, AppTheme.BodyFont)}    {longestName}") + 2 * _textInset + SystemInformation.VerticalScrollBarWidth,
            UiLayout.AtTextSize(190),
            UiLayout.AtTextSize(380));

        var headings = rounds
            .Select(round => HeadingOf(round))
            .Append(HeadingOf(null))
            .Max(heading => OnOneLine(heading, AppTheme.BoldFont));

        var counts = Enumerable.Range(0, most + 1)
            .SelectMany(n => new[]
            {
                CountText(spares: true, locked: false, recorded: false, n),
                CountText(spares: false, locked: false, recorded: false, n),
            })
            .Concat(rounds
                .Where(IsLocked)
                .Select(round => CountText(spares: false, locked: true, round.IsRecorded, round.AssignedTransponders.Count)))
            .Max(text => OnOneLine(text, AppTheme.SmallFont));

        return Math.Max(entries, Math.Max(headings, counts) + 2 * _textInset);
    }

    /// <summary>The most drivers one column can come to hold: everyone on the form, or a heat that already lists more.</summary>
    private int MostInAColumn(IReadOnlyList<RoundDefinition> rounds) =>
        Math.Max(_universe.Count, rounds.Select(r => r.AssignedTransponders.Count).DefaultIfEmpty(0).Max());

    /// <summary>How wide a row's text is drawn, as DrawEntry draws it.</summary>
    private static int Drawn(string text) =>
        TextRenderer.MeasureText(
            text,
            AppTheme.BodyFont,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;

    /// <summary>
    /// How wide a label of fixed size has to be for <paramref name="text"/>
    /// on one line - measured the way the layout check measures one
    /// (LayoutAudit.CheckLabel), which is the way the label wraps it.
    /// </summary>
    private static int OnOneLine(string text, Font font) =>
        TextRenderer.MeasureText(
            text,
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Width;

    /// <summary>
    /// The number from 0 to <paramref name="most"/> drawn widest in
    /// <paramref name="font"/> - not always the largest, since the digits
    /// of a face need not all be one width.
    /// </summary>
    private static int WidestNumber(int most, Font font) =>
        Enumerable.Range(0, Math.Max(0, most) + 1)
            .MaxBy(n => TextRenderer.MeasureText(n.ToString(), font).Width);

    /// <summary>A column's heading: the heat's name in capitals, or the column of drivers in no heat.</summary>
    private static string HeadingOf(RoundDefinition? round) =>
        round is null ? "NOT IN A HEAT" : round.RoundLabel.ToUpperInvariant();

    /// <summary>
    /// Locked, as a source and as a target: a heat that has been recorded,
    /// or the one out on track now.
    /// </summary>
    private bool IsLocked(RoundDefinition round) =>
        round.IsRecorded || string.Equals(round.Id, _onTrackRoundId, StringComparison.Ordinal);

    /// <summary>The count under a column's heading, for a column holding <paramref name="n"/> drivers.</summary>
    private static string CountText(bool spares, bool locked, bool recorded, int n) =>
        spares
            ? n == 1 ? "1 driver sitting out" : $"{n} drivers sitting out"
            : locked
                ? recorded
                    ? $"{Plural.Of(n, "driver")} - recorded, locked"
                    : $"{Plural.Of(n, "driver")} - on track, locked"
                : $"{Plural.Of(n, "driver")}";

    /// <summary>
    /// The longest the spread line can come to (see UpdateHeaders), so the
    /// label is that wide from the start and a move that rewords it never
    /// moves Save and Cancel, or puts them on a line of their own. The
    /// number of heats does not change here; how many drivers are in one
    /// can go up to everyone on the form.
    /// </summary>
    private string[] SpreadWordings(IReadOnlyList<RoundDefinition> rounds)
    {
        if (rounds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var wide = WidestNumber(MostInAColumn(rounds), AppTheme.SmallFont);

        return new[] { $"{Plural.Of(rounds.Count, "heat")}, between {wide} and {wide} drivers each." };
    }

    private void BuildColumns(IReadOnlyList<RoundDefinition> rounds)
    {
        _columns.Add(NewColumn(null));

        foreach (var round in rounds)
        {
            _columns.Add(NewColumn(round));
        }
    }

    private Column NewColumn(RoundDefinition? round)
    {
        var locked = round is not null && IsLocked(round);

        var host = new Panel
        {
            BackColor = AppTheme.Surface,
            Tag = AppTheme.KeepBackColor,
        };

        var header = new Label
        {
            Text = HeadingOf(round),
            AutoSize = false,
            Font = AppTheme.BoldFont,
            ForeColor = round is null ? AppTheme.TextMuted : AppTheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var count = new Label
        {
            AutoSize = false,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = _itemHeight,
            IntegralHeight = false,
            BorderStyle = BorderStyle.None,
            BackColor = AppTheme.Surface,
            SelectionMode = SelectionMode.One,
            AllowDrop = !locked,
        };

        var column = new Column(
            round,
            host,
            header,
            count,
            list,
            locked,
            round?.AssignedTransponders.ToList() ?? new List<string>());

        if (round is not null)
        {
            foreach (var transponder in round.AssignedTransponders)
            {
                list.Items.Add(new Entry(transponder, NameOf(transponder)));
            }
        }

        list.DrawItem += (_, e) => DrawEntry(column, e);

        if (!locked)
        {
            WireDrag(column);
        }

        // The heading strip is reserved with Padding rather than by docking
        // two more controls, so there is no z-order question about which of
        // the three gets laid out first. The heading and the count go in it
        // one under the other, as wide as the column less the room either
        // side - which MeasureColumnWidth made wide enough for both.
        host.Padding = new Padding(0, _listTop, 0, 0);

        header.Bounds = new Rectangle(_textInset, _headerTop, _columnWidth - 2 * _textInset, _headerHeight);
        count.Bounds = new Rectangle(_textInset, _headerTop + _headerHeight, _columnWidth - 2 * _textInset, _countHeight);

        host.Controls.Add(list);
        host.Controls.Add(header);
        host.Controls.Add(count);

        // The class's colour across the top of each heat, the same band the
        // printed heat sheet carries - above the heading, at any text size.
        if (round is not null)
        {
            var band = UiLayout.AtTextSize(3);

            host.Paint += (s, e) =>
            {
                if (s is not Panel panel)
                {
                    return;
                }

                using var brush = new SolidBrush(locked ? AppTheme.Border : _accent);

                e.Graphics.FillRectangle(brush, 0, 0, panel.Width, band);
            };
        }

        _strip.Controls.Add(host);

        return column;
    }

    /// <summary>The least a column can do with: its heading, its count and a few drivers.</summary>
    private int ColumnLeast => _listTop + LeastRows * _itemHeight;

    /// <summary>
    /// The columns side by side along the strip, a gap apart, each as tall
    /// as the strip - less the bar along its bottom when they are too many
    /// to fit across, so they stop above it and the strip never needs a bar
    /// down its side as well. Worked out before placing them rather than
    /// read back from the strip, whose inside only shrinks once the bar has
    /// arrived. Never shorter than their least: the window scrolls first
    /// (FillPage), so the strip is not.
    ///
    /// From where the strip is scrolled to: a column put at its place in
    /// the strip's window while the strip was scrolled sideways landed that
    /// far from where the scrolling expects it.
    /// </summary>
    private void LayoutColumns()
    {
        var across = _columns.Count * _columnWidth + Math.Max(0, _columns.Count - 1) * _columnGap;
        var bar = across > _strip.Width ? SystemInformation.HorizontalScrollBarHeight : 0;
        var height = Math.Max(ColumnLeast, _strip.Height - bar);
        var origin = _strip.AutoScrollPosition;
        var x = 0;
        var moved = false;

        foreach (var column in _columns)
        {
            var bounds = new Rectangle(origin.X + x, origin.Y, _columnWidth, height);

            if (column.Host.Bounds != bounds)
            {
                column.Host.Bounds = bounds;
                moved = true;
            }

            x += _columnWidth + _columnGap;
        }

        // And the strip's bars worked out again for the columns as they are
        // now. It works them out when it lays itself out, which a resize
        // does before it raises Resize - so with the columns still at the
        // old height. The window opens at the screen's height and then
        // shrinks to its own (DialogFit), and the strip was left with bars
        // for columns twice its height: one down its side scrolling into
        // nothing, and the width that took putting one along the bottom
        // too, over the last heat's edge (layout pictures, 29 Sep).
        if (moved)
        {
            _strip.PerformLayout();
        }
    }

    // ------------------------------------------------------------------
    //  Drawing
    // ------------------------------------------------------------------

    private void DrawEntry(Column column, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= column.List.Items.Count)
        {
            return;
        }

        var entry = (Entry)column.List.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;

        var back = selected
            ? AppTheme.PrimaryTint
            : e.Index % 2 == 1 ? AppTheme.RowAlt : AppTheme.Surface;

        using (var brush = new SolidBrush(back))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        // Numbered in a heat because the order is the grid order; not
        // numbered in the spares column, where there is no order to mean
        // anything.
        var text = column.IsSpares ? entry.Name : $"{e.Index + 1}    {entry.Name}";

        TextRenderer.DrawText(
            e.Graphics,
            text,
            AppTheme.BodyFont,
            new Rectangle(e.Bounds.X + _textInset, e.Bounds.Y, e.Bounds.Width - 2 * _textInset, e.Bounds.Height),
            column.Locked ? AppTheme.TextMuted : AppTheme.TextDark,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (!ReferenceEquals(_dropColumn, column) || _dropIndex < 0)
        {
            return;
        }

        using var pen = new Pen(AppTheme.Primary, 2f);

        if (_dropIndex == e.Index)
        {
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Top + 1, e.Bounds.Right, e.Bounds.Top + 1);
        }
        else if (_dropIndex >= column.List.Items.Count && e.Index == column.List.Items.Count - 1)
        {
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 2, e.Bounds.Right, e.Bounds.Bottom - 2);
        }
    }

    // ------------------------------------------------------------------
    //  Dragging
    // ------------------------------------------------------------------

    private void WireDrag(Column column)
    {
        var list = column.List;

        list.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                _pressIndex = -1;
                return;
            }

            _pressColumn = column;
            _pressIndex = list.IndexFromPoint(e.Location);
            _pressAt = e.Location;
        };

        list.MouseMove += (_, e) =>
        {
            if (_pressIndex < 0 || !ReferenceEquals(_pressColumn, column) || (e.Button & MouseButtons.Left) == 0)
            {
                return;
            }

            var threshold = SystemInformation.DragSize;

            if (Math.Abs(e.X - _pressAt.X) < threshold.Width
                && Math.Abs(e.Y - _pressAt.Y) < threshold.Height)
            {
                return;
            }

            var index = _pressIndex;

            // Cleared before the drag: DoDragDrop blocks until the drop is
            // over, and a second drag starting inside the first is the one
            // way this gets confusing.
            _pressIndex = -1;

            if (index >= list.Items.Count || list.Items[index] is not Entry entry)
            {
                return;
            }

            list.SelectedIndex = index;

            list.DoDragDrop(new DriverDrag(_columns.IndexOf(column), entry.Transponder), DragDropEffects.Move);

            ClearDropTarget();
        };

        list.MouseUp += (_, _) => _pressIndex = -1;

        list.DragOver += (_, e) =>
        {
            if (e.Data?.GetDataPresent(typeof(DriverDrag)) != true)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Move;

            ScrollStrip(new Point(e.X, e.Y));

            var point = list.PointToClient(new Point(e.X, e.Y));

            SetDropTarget(column, DropIndexAt(list, point.Y));
        };

        list.DragLeave += (_, _) => ClearDropTarget();

        list.DragDrop += (_, e) =>
        {
            var payload = e.Data?.GetData(typeof(DriverDrag)) as DriverDrag;
            var index = _dropIndex;

            ClearDropTarget();

            if (payload is not null && index >= 0)
            {
                PerformDrop(column, payload, index);
            }
        };

        // The second door out of a heat, for anyone who would rather not
        // drag. Delete means "not in a heat", never "delete the driver".
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete || column.IsSpares || list.SelectedItem is not Entry entry)
            {
                return;
            }

            e.Handled = true;

            PerformDrop(_columns[0], new DriverDrag(_columns.IndexOf(column), entry.Transponder), 0);
        };
    }

    private static int DropIndexAt(ListBox list, int y)
    {
        if (list.Items.Count == 0)
        {
            return 0;
        }

        var index = list.IndexFromPoint(2, y);

        if (index < 0)
        {
            // Past the last item - IndexFromPoint reports no match for the
            // empty space under the list rather than clamping to the end.
            return list.Items.Count;
        }

        var bounds = list.GetItemRectangle(index);

        return y > bounds.Top + bounds.Height / 2 ? index + 1 : index;
    }

    private void SetDropTarget(Column column, int index)
    {
        if (ReferenceEquals(_dropColumn, column) && _dropIndex == index)
        {
            return;
        }

        var previous = _dropColumn;

        _dropColumn = column;
        _dropIndex = index;

        // The tint is what makes an EMPTY heat readable as a target: with
        // no rows to draw an insertion line against, the background is the
        // only thing left to say anything.
        if (previous is not null && !ReferenceEquals(previous, column))
        {
            previous.List.BackColor = AppTheme.Surface;
            previous.List.Invalidate();
        }

        column.List.BackColor = AppTheme.PrimaryTint;
        column.List.Invalidate();
    }

    private void ClearDropTarget()
    {
        if (_dropColumn is null)
        {
            return;
        }

        _dropColumn.List.BackColor = AppTheme.Surface;
        _dropColumn.List.Invalidate();

        _dropColumn = null;
        _dropIndex = -1;
    }

    /// <summary>
    /// Creeps the strip sideways when a drag is held near either edge, so
    /// a driver can be taken to a heat that is not currently on screen.
    /// </summary>
    private void ScrollStrip(Point screenPoint)
    {
        var point = _strip.PointToClient(screenPoint);
        var edge = Math.Max(30, _columnWidth / 6);

        if (point.X < edge)
        {
            NudgeStrip(-24);
        }
        else if (point.X > _strip.ClientSize.Width - edge)
        {
            NudgeStrip(24);
        }
    }

    private void NudgeStrip(int dx)
    {
        // AutoScrollPosition reads back negative and is assigned positive.
        // Getting that backwards scrolls the wrong way on one of the two
        // edges, which reads as the strip refusing to move.
        var x = -_strip.AutoScrollPosition.X + dx;

        _strip.AutoScrollPosition = new Point(Math.Max(0, x), 0);
    }

    // ------------------------------------------------------------------
    //  Moving a driver
    // ------------------------------------------------------------------

    private void PerformDrop(Column target, DriverDrag drag, int index)
    {
        if (drag.FromColumn < 0 || drag.FromColumn >= _columns.Count)
        {
            return;
        }

        var source = _columns[drag.FromColumn];

        if (source.Locked || target.Locked)
        {
            return;
        }

        var moving = source.List.Items.Cast<Entry>()
            .FirstOrDefault(e => string.Equals(e.Transponder, drag.Transponder, StringComparison.OrdinalIgnoreCase));

        if (moving is null)
        {
            return;
        }

        var from = source.List.Items.IndexOf(moving);

        if (ReferenceEquals(source, target))
        {
            if (target.IsSpares)
            {
                // The spares column has no order to rearrange.
                return;
            }

            // The gap closes behind the driver as they are lifted out, so
            // an insertion point below them is one place too far.
            if (index > from)
            {
                index--;
            }

            if (index == from)
            {
                return;
            }

            target.List.Items.RemoveAt(from);
            target.List.Items.Insert(Math.Clamp(index, 0, target.List.Items.Count), moving);
            target.List.SelectedIndex = Math.Clamp(index, 0, target.List.Items.Count - 1);

            AfterChange();
            return;
        }

        // Checked BEFORE anything moves. This used to take the driver out
        // of the source first and then quietly not add them when the target
        // already had them - so dragging someone onto a heat they were
        // already in lost them from the heat they came from.
        if (!target.IsSpares && target.Round is { } targetRound)
        {
            // The rules live in DescribeMoveBlocker so the phone's "move a
            // driver" follows exactly the same ones - see that method.
            var heats = _columns
                .Where(c => c.Round is not null)
                .Select(c => new HeatState(c.Round!, c.Locked, c.Current))
                .ToList();

            if (DescribeMoveBlocker(heats, source.Round, targetRound, drag.Transponder, _roundsAreOneRaceDay, NameOf) is { } refusal)
            {
                Refuse(refusal);
                return;
            }
        }

        if (!source.IsSpares)
        {
            source.List.Items.RemoveAt(from);
        }

        if (!target.IsSpares)
        {
            // A fresh entry rather than the dragged one: in the left column
            // the name carries a "(not in Q2)" note that does not belong in
            // a heat.
            var placed = new Entry(moving.Transponder, NameOf(moving.Transponder));

            target.List.Items.Insert(Math.Clamp(index, 0, target.List.Items.Count), placed);
            target.List.SelectedIndex = Math.Clamp(index, 0, target.List.Items.Count - 1);
        }

        AfterChange();
    }

    /// <summary>
    /// Whether a heat that has already run counts for "one heat per
    /// qualifying round".
    ///
    /// On a race day the form is only given that day's rounds, so every run
    /// heat is tonight's and counts. With no race day it is given every
    /// round the class has ever had, and last week's "Q1 Heat 2" must not
    /// stop somebody going into tonight's "Q1 Heat 1" - so there a run heat
    /// counts only when it was made recently enough to be tonight's.
    /// </summary>
    public static bool RunHeatCounts(RoundDefinition round, bool roundsAreOneRaceDay) =>
        roundsAreOneRaceDay
        || round.CreatedAtUtc >= DateTime.UtcNow - NoRaceDayWindow;

    /// <summary>
    /// One heat as the move rules see it: the round, whether it is locked
    /// (already run), and who is in it at this moment - which on the form
    /// is the edited copy, not the stored roster.
    /// </summary>
    public sealed record HeatState(RoundDefinition Round, bool Locked, IReadOnlyCollection<string> Drivers);

    /// <summary>
    /// Why a driver cannot be put into <paramref name="target"/>, or null
    /// when they can - the rules this form enforces on a drop, as a
    /// function, so the phone's "move a driver" (RaceDayService) refuses
    /// exactly what this form refuses.
    ///
    /// Two rules: nobody goes into a heat they are already in, and nobody
    /// is in two heats of the same qualifying round. The heat they are
    /// coming out of (<paramref name="source"/>, null when they are coming
    /// from "not in a heat") does not count - moving between two heats of
    /// the same round is the whole point. A heat that has already run
    /// counts only when it is tonight's (see RunHeatCounts). Locked heats
    /// are the caller's to check: this form ignores a drop on one silently,
    /// the phone says why.
    /// </summary>
    /// <param name="heats">Every heat of the class being balanced.</param>
    /// <param name="nameOf">Turns a transponder into the name to put in the message.</param>
    public static string? DescribeMoveBlocker(
        IReadOnlyList<HeatState> heats,
        RoundDefinition? source,
        RoundDefinition target,
        string transponder,
        bool roundsAreOneRaceDay,
        Func<string, string> nameOf)
    {
        var targetDrivers = heats.FirstOrDefault(h => h.Round.Id == target.Id)?.Drivers
                            ?? (IReadOnlyCollection<string>)target.AssignedTransponders;

        // Checked BEFORE anything moves. This used to take the driver out
        // of the source first and then quietly not add them when the target
        // already had them - so dragging someone onto a heat they were
        // already in lost them from the heat they came from.
        if (targetDrivers.Contains(transponder, StringComparer.OrdinalIgnoreCase))
        {
            return $"{nameOf(transponder)} is already in {target.RoundLabel}.";
        }

        var onForm = heats.Select(h => (RoundDefinition?)h.Round).ToList();

        // One heat per qualifying round. A heat that has already run
        // counts only when it is tonight's: ignoring every run heat let a
        // driver who had raced Q1 Heat 1 be dropped into Q1 Heat 3 and run
        // Q1 twice.
        if (RoundKeyFor(target, onForm) is not { } qualifyingRound)
        {
            return null;
        }

        var clash = heats.FirstOrDefault(h =>
            (!h.Locked || RunHeatCounts(h.Round, roundsAreOneRaceDay))
            && h.Round.Id != source?.Id
            && h.Round.Id != target.Id
            && RoundKeyFor(h.Round, onForm) == qualifyingRound
            && h.Drivers.Contains(transponder, StringComparer.OrdinalIgnoreCase));

        if (clash is null)
        {
            return null;
        }

        // "the same round as", not "the same qualifying round as": on a
        // club night these heats are called races, and the sentence has
        // both labels in it already for anyone who wants to check.
        return clash.Locked
            ? $"{nameOf(transponder)} has already raced {clash.Round.RoundLabel}, which is in the same round "
              + $"as {target.RoundLabel}."
            : $"{nameOf(transponder)} is already in {clash.Round.RoundLabel}, which is in the same round "
              + $"as {target.RoundLabel}. Drag them from {clash.Round.RoundLabel} to move them.";
    }

    /// <summary>
    /// Says why a drop was not made. Posted rather than shown on the spot,
    /// because this runs inside the drop itself and a modal dialog opened
    /// there holds the drag open behind it.
    /// </summary>
    private void Refuse(string message)
    {
        BeginInvoke(() =>
        {
            AppDialog.Show(this, message, "Already in that round", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    /// <summary>
    /// Which qualifying round a heat belongs to, or null for anything that
    /// is not a qualifier.
    ///
    /// Read from the label, because rounds carry no round number of their
    /// own. The programme builder writes "Q2 Heat 3" (round Q2) on a night
    /// of several rounds and plain "Heat 3" on a one-round night; a round
    /// added by hand may say "Round 2 Heat 1", "R2 Heat 1" or "Qualifier 2
    /// Heat 1", which are all read as Q2 too. On a club night that same
    /// builder writes "Race 2" and "Race 2 Heat 1" - the club-night word
    /// for the very same thing - so "Race" is a round word here as well.
    /// Without it every club-night race read as the one unnumbered round,
    /// and "one heat per round" then refused every move from Race 1 into
    /// Race 2 - which is the whole of a club night.
    ///
    /// A qualifier with no round number belongs to the one unnumbered
    /// round, "" - see RoundKeyOf for what happens to that when numbered
    /// rounds are on the form as well.
    ///
    /// Mains and practice are left out on purpose: a bump-up puts a driver
    /// in the B-main AND the A-main, so "one per round" is not a rule there.
    /// </summary>
    public static string? QualifyingRoundOf(RoundDefinition? round)
    {
        if (round is null || round.Kind != RoundKind.Qualifier)
        {
            return null;
        }

        var match = RoundNumberPattern.Match((round.RoundLabel ?? string.Empty).Trim());

        return match.Success && int.TryParse(match.Groups[1].Value, out var number)
            ? $"Q{number}"
            : string.Empty;
    }

    /// <summary>
    /// A round word and its number at the start of a label - "Q2", "Q 2",
    /// "Round 2", "Rd 2", "R2", "Qual 2", "Qualifier 2", "Qualifying 2",
    /// "Race 2" - with the number ending the word, so "Q2x Final" is not
    /// read as Q2.
    ///
    /// "race" is listed before the bare "r" only for a reader's sake; the
    /// engine backtracks through the alternatives either way. It is only
    /// ever applied to a Qualifier (QualifyingRoundOf returns before this
    /// for anything else), so it cannot swallow "Race of Champions" as a
    /// round number on a main.
    /// </summary>
    private static readonly Regex RoundNumberPattern = new(
        @"^(?:qualifying|qualifier|qual|q|race|round|rd|r)\s*(\d{1,4})(?=$|[\s\-:,.])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The qualifying round a column counts towards for "one heat per
    /// round", or null when it counts towards none.
    ///
    /// An unnumbered qualifier ("Heat 5") is its own round only when every
    /// qualifier on the form is unnumbered - a one-round night. Beside
    /// numbered rounds it is a heat added by hand that belongs to no
    /// particular one of them; lumping it in as a round of its own would
    /// list every driver as "not in qualifying" and refuse moves between
    /// hand-added heats for no reason.
    /// </summary>
    private string? RoundKeyOf(Column column) => RoundKeyFor(column.Round, _columns.Select(c => c.Round));

    /// <summary>
    /// RoundKeyOf for any set of heats - <paramref name="onForm"/> is every
    /// heat being balanced together, which decides whether an unnumbered
    /// qualifier is a round of its own. Shared with the phone.
    /// </summary>
    public static string? RoundKeyFor(RoundDefinition? round, IEnumerable<RoundDefinition?> onForm)
    {
        var key = QualifyingRoundOf(round);

        if (key is { Length: 0 } && onForm.Any(r => QualifyingRoundOf(r) is { Length: > 0 }))
        {
            return null;
        }

        return key;
    }

    /// <summary>
    /// What to call a round key on screen, read back off a heat that is in
    /// it - so a club night's "Race 2" is shown as "Race 2" and not as the
    /// "Q2" the key happens to be spelled with. Falls back to the key for
    /// a label with no round word on the front, which only an unnumbered
    /// qualifier has.
    /// </summary>
    private static string RoundNameFor(string key, RoundDefinition? sample)
    {
        if (key.Length == 0)
        {
            return "qualifying";
        }

        var match = RoundNumberPattern.Match((sample?.RoundLabel ?? string.Empty).Trim());

        return match.Success ? match.Value : key;
    }

    /// <summary>Sorts "Q2" before "Q10", and the unnumbered round first.</summary>
    private static int RoundKeyOrder(string key) =>
        key.Length > 1 && int.TryParse(key.AsSpan(1), out var number) ? number : 0;

    private void AfterChange()
    {
        RefreshSpares();
        UpdateHeaders();

        foreach (var column in _columns)
        {
            column.List.Invalidate();
        }
    }

    /// <summary>
    /// Rebuilds the left column from everyone who is in no heat at all.
    ///
    /// Derived rather than edited, so there is exactly one answer to "is
    /// this driver racing?" - the heats. Dropping onto this column takes
    /// somebody out of a heat and they appear here because of that, not
    /// because anything was added to a second list.
    /// </summary>
    private void RefreshSpares()
    {
        var racing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in _columns.Where(c => !c.IsSpares))
        {
            foreach (var transponder in column.Current)
            {
                racing.Add(transponder);
            }
        }

        // Who is in each qualifying round. Judged per round rather than
        // across the whole form: somebody in a Q1 heat and in no Q2 heat is
        // still sitting out Q2, and used to vanish from this column the
        // moment they were put in Q1 - leaving no way to add them to Q2.
        var qualifyingRounds = _columns
            .Where(c => !c.IsSpares && RoundKeyOf(c) is not null)
            .GroupBy(c => RoundKeyOf(c)!, StringComparer.Ordinal)
            .OrderBy(g => RoundKeyOrder(g.Key))
            .Select(g => (Name: RoundNameFor(g.Key, g.First().Round),
                          Racing: g.SelectMany(c => c.Current).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .ToList();

        var spares = _columns[0];

        var wanted = new List<Entry>();

        foreach (var transponder in _universe.Keys.OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!racing.Contains(transponder))
            {
                wanted.Add(new Entry(transponder, NameOf(transponder)));
                continue;
            }

            var missing = qualifyingRounds
                .Where(r => !r.Racing.Contains(transponder))
                .Select(r => r.Name)
                .ToList();

            if (missing.Count > 0)
            {
                wanted.Add(new Entry(transponder, $"{NameOf(transponder)}  (not in {string.Join(", ", missing)})"));
            }
        }

        // Compared as entries, name included, so a note that changes - a
        // driver now missing Q3 only rather than Q2 and Q3 - is redrawn.
        if (wanted.SequenceEqual(spares.List.Items.Cast<Entry>()))
        {
            return;
        }

        spares.List.BeginUpdate();
        spares.List.Items.Clear();

        foreach (var entry in wanted)
        {
            spares.List.Items.Add(entry);
        }

        spares.List.EndUpdate();
    }

    private void UpdateHeaders()
    {
        var sizes = new List<int>();

        foreach (var column in _columns)
        {
            var n = column.List.Items.Count;

            column.Count.Text = CountText(column.IsSpares, column.Locked, column.Round?.IsRecorded == true, n);

            if (column.IsSpares)
            {
                continue;
            }

            sizes.Add(n);

            column.Count.ForeColor = column.Locked ? AppTheme.TextMuted
                : n == 0 ? AppTheme.Primary
                : AppTheme.TextMuted;
        }

        _lblSpread.Text = sizes.Count == 0
            ? "No heats for this class yet."
            : sizes.Min() == sizes.Max()
                ? $"{Plural.Of(sizes.Count, "heat")}, {Plural.Of(sizes.Min(), "driver")} in each."
                : $"{Plural.Of(sizes.Count, "heat")}, between {sizes.Min()} and {sizes.Max()} drivers each.";
    }

    // ------------------------------------------------------------------
    //  Saving
    // ------------------------------------------------------------------

    private void Commit()
    {
        var changes = new List<RosterChange>();

        foreach (var column in _columns)
        {
            if (column.Round is null || column.Locked)
            {
                continue;
            }

            var after = column.Current;

            if (!after.SequenceEqual(column.Original, StringComparer.OrdinalIgnoreCase))
            {
                changes.Add(new RosterChange(column.Round.Id, column.Original.ToList(), after));
            }
        }

        // An empty heat is legal - a class can shrink during the day - but
        // it is far more often a driver dragged somewhere by accident, and
        // it is invisible once this form is closed.
        var emptied = _columns.Count(c => c.Round is not null && !c.Locked && c.List.Items.Count == 0);

        if (emptied > 0
            && AppDialog.Show(this,
                emptied == 1
                    ? "One heat has no drivers in it. Save anyway?"
                    : $"{emptied} heats have no drivers in them. Save anyway?",
                "Empty heats", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        Changes = changes;
        DialogResult = DialogResult.OK;
        Close();
    }
}
