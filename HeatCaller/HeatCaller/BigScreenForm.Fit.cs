namespace HeatCaller;

/// <summary>
/// Every driver on the board, however big the field (19 Nov 2026).
///
/// WHY: the board's rows were 64 pixels at the normal size (50 since the
/// open items of 19 Nov 2026 - see FullRowDesignHeight), so a 720p wall
/// had room for six drivers and an eight-car heat put P7 and P8 below the
/// edge. Scrolling didn't save them either: the board was cleared and
/// filled again on every lap, which sent it back to the top. Now:
/// <list type="number">
/// <item>Rows are updated where they are (RefreshAll), so the board stays
/// where it was put between laps.</item>
/// <item>A field too big for the board makes its rows shorter and its
/// writing smaller, down to three quarters of the normal size - an
/// eight-car heat fits a 720p wall and nothing moves.</item>
/// <item>A field too big even for that keeps the top three where they are
/// and rolls the rest past underneath them: a pause at the top, a row at a
/// time to the bottom, a pause there, back to the top. One pass takes the
/// same time however many rows are hidden, so the more there are, the
/// quicker it goes.</item>
/// </list>
/// A red flag holds the board at the top - the race is stopped and the
/// order is what people look for. The checkered flag and the results keep
/// rolling, so every driver gets to see where they finished.
/// </summary>
public sealed partial class BigScreenForm
{
    /// <summary>How short a row may get to fit the field, against its normal height. Below this the writing stops reading from the back of a hall, so the board rolls instead.</summary>
    internal const float LeastRowShare = 0.75f;

    /// <summary>How many rows at the top stay where they are while the rest roll - the leader and the podium are always on the board.</summary>
    internal const int PinnedRows = 3;

    /// <summary>How long the board rests at the top and at the bottom of each pass.</summary>
    internal const int ScrollHoldMs = 2500;

    /// <summary>How long the board takes to roll from the top to the bottom, however many rows are hidden.</summary>
    internal const int ScrollPassMs = 6000;

    /// <summary>The quickest the board steps a row, for a field so big the pass would otherwise blur.</summary>
    internal const int QuickestStepMs = 400;

    /// <summary>
    /// How the board fits a field: each row's height, how big its writing is
    /// against the normal size, how many rows at the top stay put, and how
    /// many rows have to roll past to be seen (0: everyone fits).
    /// </summary>
    internal readonly record struct BoardFit(int RowHeight, float FontShare, int Pinned, int Hidden);

    /// <summary>
    /// Fits <paramref name="rows"/> drivers into <paramref name="room"/>
    /// pixels of board under the headings, rows being
    /// <paramref name="fullRowHeight"/> at the normal size. The size steps
    /// in twentieths, so a field of nine and one of ten don't each make new
    /// faces for a pixel's difference.
    /// </summary>
    internal static BoardFit Fit(int rows, int room, int fullRowHeight)
    {
        if (rows <= 0 || room <= 0 || fullRowHeight <= 0 || rows * fullRowHeight <= room)
        {
            return new BoardFit(Math.Max(1, fullRowHeight), 1f, 0, 0);
        }

        var share = MathF.Floor(room / (float)rows / fullRowHeight * 20f) / 20f;

        if (share >= LeastRowShare)
        {
            return new BoardFit(Math.Max(1, (int)(fullRowHeight * share)), share, 0, 0);
        }

        var height = Math.Max(1, (int)(fullRowHeight * LeastRowShare));
        var showing = Math.Max(1, room / height);

        // A window dragged very small still keeps one row rolling.
        var pinned = Math.Min(PinnedRows, showing - 1);

        return new BoardFit(height, LeastRowShare, Math.Max(0, pinned), Math.Max(0, rows - showing));
    }

    /// <summary>How long the board rests on each row while rolling: one pass is ScrollPassMs whatever is hidden, so more hidden rows roll quicker.</summary>
    internal static int ScrollStepMs(int hidden) =>
        hidden <= 0 ? ScrollHoldMs : Math.Max(QuickestStepMs, ScrollPassMs / hidden);

    private readonly System.Windows.Forms.Timer _scrollTimer = new() { Interval = ScrollHoldMs };

    /// <summary>The board's normal row height at this window's size, set by ApplyScale; 0 until the window is built.</summary>
    private int _fullRowHeight;

    /// <summary>How the board fits now - see Fit.</summary>
    private BoardFit _fit = new(1, 1f, 0, 0);

    /// <summary>How far the board has rolled: rows past the pinned ones, 0 at the top.</summary>
    private int _rolledBy;

    private void BuildFit()
    {
        _grid.SizeChanged += (_, _) => FitTheBoard();
        _scrollTimer.Tick += (_, _) => { if (!RollByHandForTests) RollTheBoard(); };
        _grid.RowPostPaint += PaintPinnedLine;
        Disposed += (_, _) =>
        {
            _scrollTimer.Stop();
            _scrollTimer.Dispose();
        };
    }

    /// <summary>
    /// Sizes the rows and writing to the field and the board's room, pins
    /// the top rows and starts or stops the roll. Run once the rows are in
    /// (RefreshAll), when the board's room changes (a bar coming or going
    /// below it) and when the window is resized (ApplyScale). Cheap when
    /// nothing changed.
    /// </summary>
    private void FitTheBoard()
    {
        if (_fullRowHeight <= 0 || IsDisposed || Disposing)
        {
            return;
        }

        var room = _grid.ClientSize.Height - (_grid.ColumnHeadersVisible ? _grid.ColumnHeadersHeight : 0);
        var fit = Fit(_grid.Rows.Count, room, _fullRowHeight);

        if (Math.Abs(fit.FontShare - _fit.FontShare) > 0.001f)
        {
            SetBoardFace(fit.FontShare);
        }

        _grid.RowTemplate.Height = fit.RowHeight;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Height != fit.RowHeight)
            {
                row.Height = fit.RowHeight;
            }
        }

        PinTopRows(fit.Pinned, fit.RowHeight);

        var wasRolling = _fit.Hidden > 0;
        _fit = fit;

        if (fit.Hidden <= 0)
        {
            _scrollTimer.Stop();
            _rolledBy = 0;
            ShowFromRow(0);
            return;
        }

        _rolledBy = Math.Min(_rolledBy, fit.Hidden);

        // Where it was, as the rows were updated rather than rebuilt; a
        // board that only now needs to roll starts at the top, resting.
        ShowFromRow(_rolledBy);

        if (!wasRolling || !_scrollTimer.Enabled)
        {
            _scrollTimer.Interval = ScrollHoldMs;
            _scrollTimer.Start();
        }
    }

    /// <summary>One step of the roll - see the class notes for its pattern.</summary>
    private void RollTheBoard()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        var hidden = _fit.Hidden;

        if (hidden <= 0 || HoldTheBoard())
        {
            _rolledBy = 0;
            ShowFromRow(0);
            _scrollTimer.Interval = ScrollHoldMs;
            return;
        }

        _rolledBy = _rolledBy >= hidden ? 0 : _rolledBy + 1;
        ShowFromRow(_rolledBy);

        _scrollTimer.Interval = _rolledBy == 0 || _rolledBy == hidden ? ScrollHoldMs : ScrollStepMs(hidden);
    }

    /// <summary>A red flag holds the board at the top: the race is stopped, and the order is what people look for.</summary>
    private bool HoldTheBoard() => _race.Flag == FlagState.Red;

    /// <summary>Puts the board's first rolling row <paramref name="rolledBy"/> rows past the pinned ones.</summary>
    private void ShowFromRow(int rolledBy)
    {
        var first = _fit.Pinned + rolledBy;

        if (first >= _grid.Rows.Count || _grid.FirstDisplayedScrollingRowIndex == first)
        {
            return;
        }

        try
        {
            _grid.FirstDisplayedScrollingRowIndex = first;
        }
        catch (InvalidOperationException)
        {
            // The grid has no room laid out yet (a window still opening);
            // the next fit or step puts it right.
        }
    }

    /// <summary>The top <paramref name="pinned"/> rows held in place while the rest roll; none when the board doesn't roll.</summary>
    private void PinTopRows(int pinned, int rowHeight)
    {
        var rows = _grid.Rows;

        // Bottom up to let go, so no row above a frozen one is let go
        // first; then the last pinned row, which pins every row above it.
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (i >= pinned && rows[i].Frozen)
            {
                rows[i].Frozen = false;
            }
        }

        if (pinned > 0 && pinned <= rows.Count && !rows[pinned - 1].Frozen)
        {
            rows[pinned - 1].Frozen = true;
        }

        // A heavier line under the pinned rows, so P3 sitting over P7 reads
        // as the top three and the rest rolling, not as P4 to P6 missing.
        var divider = pinned > 0 ? Math.Max(2, rowHeight / 16) : 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var wanted = i == pinned - 1 ? divider : 0;

            if (rows[i].DividerHeight != wanted)
            {
                rows[i].DividerHeight = wanted;
            }
        }
    }

    /// <summary>The line under the pinned rows (PinTopRows), in the look's muted colour - the grid's own hairline colour is too faint to see across a hall.</summary>
    private void PaintPinnedLine(object? sender, DataGridViewRowPostPaintEventArgs e)
    {
        if (_fit.Pinned <= 0 || e.RowIndex != _fit.Pinned - 1)
        {
            return;
        }

        var divider = _grid.Rows[e.RowIndex].DividerHeight;

        if (divider <= 0)
        {
            return;
        }

        using var brush = new SolidBrush(MutedColor);
        var row = e.RowBounds;
        e.Graphics.FillRectangle(brush, row.Left, row.Bottom - divider, row.Width, divider);
    }

    /// <summary>
    /// The board's writing at <paramref name="share"/> of its normal size.
    /// The face it replaces is let go once the grid no longer wears it.
    /// </summary>
    private void SetBoardFace(float share)
    {
        var old = _grid.DefaultCellStyle.Font;

        _grid.DefaultCellStyle.Font = OwnFont(new Font(
            AppTheme.BodyFont.FontFamily, Math.Max(1f, BoardPoints * _scale * share), FontStyle.Bold));

        if (old is not null && _ownedFonts.Remove(old))
        {
            old.Dispose();
        }
    }

    /// <summary>The board's writing at the normal size, in points before the window's scale.</summary>
    private const float BoardPoints = 24F;

    /// <summary>
    /// A board row's height at the normal size, in pixels before the
    /// window's scale (ApplyScale). 50, not the 64 it was (open items, 19 Nov
    /// 2026): at 64, with the bands above it, a 1080p TV had room for six of
    /// an eight-car heat at full size, and the fit shrank the writing to get
    /// the rest on. 50 still holds the 24-point line with RowPadDesign above
    /// and below, so eight fit at full size, with the up-next bar showing.
    /// </summary>
    internal const int FullRowDesignHeight = 50;

    /// <summary>The board cells' padding above and below the writing at the normal size.</summary>
    internal const int RowPadDesign = 2;

    /// <summary>The board's headings band at the normal size: room for its 12-point line and no more.</summary>
    internal const int HeadingsDesignHeight = 30;

    /// <summary>The board's writing at the normal size, for the tests.</summary>
    internal const float BoardPointsForTests = BoardPoints;

    /// <summary>The board's room under its headings now, in pixels - for the tests.</summary>
    internal int BoardRoomForTests => _grid.ClientSize.Height - (_grid.ColumnHeadersVisible ? _grid.ColumnHeadersHeight : 0);

    /// <summary>The board's line of writing with its padding, against a row's height now - for the tests.</summary>
    internal (int Line, int Row) BoardLineForTests =>
        ((_grid.DefaultCellStyle.Font?.Height ?? 0) + _grid.DefaultCellStyle.Padding.Vertical, _fit.RowHeight);

    /// <summary>How the board fits, for the tests.</summary>
    internal (int RowHeight, float FontShare, int Pinned, int Hidden) BoardFitForTests =>
        (_fit.RowHeight, _fit.FontShare, _fit.Pinned, _fit.Hidden);

    /// <summary>The rows wholly on the board now, by index (0 the leader) - for the tests.</summary>
    internal IReadOnlyList<int> RowsShowingForTests()
    {
        var showing = new List<int>();

        for (var i = 0; i < _grid.Rows.Count; i++)
        {
            var shown = _grid.GetRowDisplayRectangle(i, cutOverflow: true);

            if (shown.Height >= _grid.Rows[i].Height)
            {
                showing.Add(i);
            }
        }

        return showing;
    }

    /// <summary>One step of the roll, for the tests.</summary>
    internal void RollTheBoardForTests() => RollTheBoard();

    /// <summary>How many rows the board holds, for the tests.</summary>
    internal int BoardRowsForTests => _grid.Rows.Count;

    /// <summary>
    /// For the tests: the roll's timer steps nothing, and the test steps it
    /// with RollTheBoardForTests (open items, 19 Nov 2026). With shorter rows
    /// a rolling field hides more, so the timer's steps came quicker than the
    /// tests' pauses and moved the board under them.
    /// </summary>
    internal bool RollByHandForTests { get; set; }
}
