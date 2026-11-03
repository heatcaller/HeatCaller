using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace HeatCaller;

/// <summary>
/// A driver's lap breakdown, painted by hand rather than assembled out of
/// Windows controls.
///
/// The strip that opens inside the leaderboard has to live inside a
/// DataGridView row: one line of text, one row tall, no room for anything
/// that isn't characters. That is fine for a glance and useless for
/// actually reading a race - which lap was the best one, whether the driver
/// is finding time or losing it, where a clean run ended.
///
/// So this is a floating card instead, drawn with the same primitives as
/// the rest of the app (<see cref="AppTheme.DrawCard"/>, the stat tile, the
/// pill, the micro-caption) so it belongs to HeatCaller rather than to
/// Windows. Every lap gets its own line: the time in the display face, a
/// pace bar scaled across the laps on screen, how far off the driver's best
/// it was, and what the clean run stood at - with the best lap and the lap
/// that ended a run called out in colour.
///
/// A WINDOW OF ITS OWN, ONE PER DRIVER (30 Oct 2026, asked for by Asher:
/// "multiple lap detail windows open at the same time on race control...
/// floating windows that can be dragged around"). Until then this was a
/// drop-down, which closed on any click elsewhere - so only one could ever
/// be open. Now each is a small borderless window owned by the main one:
///
/// - dragged by its header, and onto a second monitor if there is one;
/// - made taller or shorter from its bottom edge, for more or fewer laps;
///   the width is fixed, because the columns are laid out for it;
/// - scrolled back through every lap when they do not all fit (2 Nov 2026,
///   Asher: "lap breakdown should scroll in it's window to see all laps") -
///   by the wheel, the scrollbar on the right, or the arrow, Page and
///   Home/End keys. Scrolled to the bottom it follows the newest lap; scrolled
///   back it stays on the laps being read as new ones come in;
/// - closed by the X in its corner or Escape - a click elsewhere no longer
///   closes it, which is what lets several stay open side by side;
/// - opened without taking the focus from Race Control, and passing the
///   flag keys (F1 to F8) back to it when it has been clicked into, as the
///   Broadcast window does;
/// - above the main window and minimised with it, and kept off the taskbar
///   and Alt+Tab, since it belongs to Race Control rather than standing on
///   its own.
///
/// Race Control keeps them (RaceControlControl.Leaderboard): where each
/// opens and is remembered, and closing them all when the next heat is
/// called, so each heat starts on a clean screen.
///
/// The contents update live - <see cref="Apply"/> is called from the same
/// leaderboard refresh that redraws the grid - so one can be left open on a
/// driver for a whole race.
/// </summary>
public sealed class LapDetailWindow : Form
{
    /// <summary>Laps shown when a window opens for the first time.</summary>
    public const int DefaultRows = 12;

    /// <summary>The fewest and most laps a window can be sized for.</summary>
    public const int MinRows = 4;
    public const int MaxRows = 40;

    // Win32, for dragging by the header and sizing from the bottom edge
    // without a title bar or a border to do it with.
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const int HtCaption = 2;
    private const int HtBottom = 15;
    private const int WsExToolWindow = 0x00000080;

    private readonly LapDetailView _view;
    private readonly Func<Keys, bool>? _raceKey;

    /// <summary>The driver this window is showing, so the leaderboard can keep feeding it.</summary>
    public string Transponder { get; }

    /// <summary>Which remembered spot it is in - see LapWindowPlacement.FreeSlot.</summary>
    public int Slot { get; }

    /// <summary>How many laps it is sized to show.</summary>
    public int Rows => _view.Rows;

    /// <summary>The text size (Ctrl +/-) it was last laid out for.</summary>
    public float BuiltForFontScale { get; private set; }

    /// <summary>Raised when it has been dragged or resized, once the mouse lets go, so the spot can be remembered.</summary>
    public event EventHandler? Placed;

    /// <param name="raceKey">
    /// Race Control's flag keys, for a key pressed while this window has the
    /// focus - it is a form of its own, so MainForm's keys do not reach it.
    /// True when the key was one of them.
    /// </param>
    public LapDetailWindow(string transponder, string driverName, string subtitle, int streakThreshold, int slot, int rows,
        Func<Keys, bool>? raceKey = null)
    {
        Transponder = transponder;
        Slot = slot;
        _raceKey = raceKey;

        Text = string.IsNullOrWhiteSpace(driverName) ? "Lap breakdown" : $"{driverName.Trim()} - lap breakdown";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        ControlBox = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = AppTheme.Surface;
        Padding = Padding.Empty;

        _view = new LapDetailView(driverName, subtitle, streakThreshold) { Dock = DockStyle.Fill };
        // Posted, not closed here: the click is still being handled inside
        // the card the close is about to dispose.
        _view.CloseClicked += (_, _) => BeginInvoke(new Action(Close));
        Controls.Add(_view);

        SizeFor(rows);
    }

    /// <summary>Opened beside the race screen, not in front of its keyboard.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            // A tool window: no taskbar button, no Alt+Tab entry.
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow;
            return cp;
        }
    }

    /// <summary>
    /// Feeds the window the driver's current standing - or null when they
    /// are no longer in the standings, which the card says rather than
    /// freezing on numbers that have stopped being true.
    /// </summary>
    public void Apply(RacerStanding? standing, int position)
    {
        if (BuiltForFontScale != AppTheme.FontScale)
        {
            Rescale();
        }

        _view.Apply(standing, position);
    }

    /// <summary>
    /// Laid out again at the text size in use, and in the theme's colours:
    /// every dimension of the card follows the text size, so a window left
    /// open across Ctrl +/- would otherwise clip its own numbers.
    /// </summary>
    public void Rescale()
    {
        BackColor = AppTheme.Surface;
        _view.BackColor = AppTheme.Surface;
        SizeFor(_view.Rows);
        _view.Invalidate();
    }

    /// <summary>
    /// The most laps a window can show at the text size in use: MaxRows,
    /// or fewer when a window that tall would not fit on the tallest
    /// screen's working area (31 Oct 2026). Windows will not make a window
    /// taller than that - on a 1080p screen with the taskbar, 40 laps came
    /// out as a window cut off at 33 and a bit, not whole laps.
    /// </summary>
    internal static int MostRows
    {
        get
        {
            var screens = Screen.AllScreens;

            if (screens.Length == 0)
            {
                return MaxRows;
            }

            return LapDetailView.RowsFor(screens.Max(s => s.WorkingArea.Height));
        }
    }

    /// <summary>Sized to show this many laps, within the limits and the screen, at the text size in use.</summary>
    public void SizeFor(int rows)
    {
        var most = MostRows;
        rows = Math.Clamp(rows, MinRows, Math.Max(MinRows, most));
        BuiltForFontScale = AppTheme.FontScale;

        var width = LapDetailView.ViewWidth;

        // Limits first, or the new size is clamped to the old ones.
        MinimumSize = Size.Empty;
        MaximumSize = Size.Empty;

        _view.Rows = rows;
        ClientSize = new Size(width, LapDetailView.HeightFor(rows));

        MinimumSize = new Size(width, LapDetailView.HeightFor(MinRows));
        MaximumSize = new Size(width, LapDetailView.HeightFor(Math.Max(MinRows, most)));

        ApplyRegion();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // Live while the bottom edge is dragged, so the laps fill in as it
        // grows; snapped to a whole lap when the mouse lets go.
        _view.Rows = LapDetailView.RowsFor(ClientSize.Height);
        ApplyRegion();
        _view.Invalidate();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);

        var rows = LapDetailView.RowsFor(ClientSize.Height);

        if (ClientSize.Height != LapDetailView.HeightFor(rows))
        {
            SizeFor(rows);
        }

        // ResizeEnd follows a drag by the header too, not only a resize.
        Placed?.Invoke(this, EventArgs.Empty);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WmNcHitTest)
        {
            return;
        }

        var point = PointToClient(LapDetailView.PointFrom(m.LParam));

        if (point.Y >= ClientSize.Height - LapDetailView.GripHeight)
        {
            m.Result = HtBottom;
        }
        else if (_view.IsDragArea(point))
        {
            m.Result = HtCaption;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        if (keyData is >= Keys.F1 and <= Keys.F8 && _raceKey?.Invoke(keyData) == true)
        {
            return true;
        }

        if (_view.ScrollKey(keyData))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Clips the window to the same rounded rectangle the card is drawn
    /// with. Without it the corners are square window behind round paint,
    /// which is visible against anything but a matching background.
    /// </summary>
    private void ApplyRegion()
    {
        if (Width < 8 || Height < 8)
        {
            return;
        }

        using var path = AppTheme.RoundedRect(new RectangleF(0, 0, Width, Height), 10);

        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    /// <summary>The card itself - everything below here is paint, and the close button.</summary>
    private sealed class LapDetailView : Control
    {
        /// <summary>
        /// Laps shown at once - as many as the window is sized for. Older
        /// ones scroll off the top, because the recent end is the one being read.
        /// </summary>
        public int Rows { get; set; } = DefaultRows;

        // How many laps back from the newest the list is scrolled: 0 follows
        // the newest lap live. Counted from that end rather than the start,
        // so it means the same thing as laps are added - see Apply.
        private int _scrollBack;

        // The scrollbar thumb being dragged: where the pointer went down and
        // which lap was at the top then. Null when it is not.
        private int? _thumbDragY;
        private int _thumbDragFirst;

        private int MaxScrollBack => Math.Max(0, _lapMs.Length - Rows);

        private int ScrollBack => Math.Clamp(_scrollBack, 0, MaxScrollBack);

        /// <summary>Raised when the X in the corner is clicked.</summary>
        public event EventHandler? CloseClicked;

        private bool _closeHot;
        private bool _closePressed;

        private readonly string _driverName;
        private readonly string _subtitle;
        private readonly int _streakThreshold;

        private long[] _lapMs = Array.Empty<long>();
        private int[] _streakAtLap = Array.Empty<int>();

        // Indexes into _lapMs of laps that ran through a crash recovery (see
        // RacerStanding.RecoveryLapIndexes). Such a lap spans the whole
        // stoppage, so it is still listed - it is part of the driver's race
        // time - but it is left out of the Average tile and the pace-bar
        // scale, the same way RaceGaps.PaceLaps leaves it out of the
        // leaderboard's figures. Without that, the popup's Average disagreed
        // with the board's for the same driver, and one lap minutes long
        // squashed every other bar down to the stub.
        private HashSet<int> _recoveryLaps = new();

        // Each lap's sector times (RacerStanding.SectorsOf), in step with
        // _lapMs, and the driver's best in each and their ideal lap - all
        // empty without a split loop, when the PACE bars show as they did.
        // With sectors, the bars' room shows them instead.
        private long[]?[] _sectors = Array.Empty<long[]?>();
        private long[]? _bestSectors;
        private long? _idealMs;

        private bool HasSectors => _bestSectors is not null;

        private bool _present;
        private int _position;
        private int _laps;
        private long _bestMs;
        private long _penaltyMs;
        private int _currentRun;

        // Every dimension is scaled with the app's font scale, so the panel
        // grows with the text instead of clipping it at larger sizes.
        private static int Px(float value) => UiLayout.AtTextSize(value);

        private static int Pad => Px(14);
        private static int HeaderHeight => Px(46);
        private static int StatsHeight => Px(54);
        private static int ColumnHeaderHeight => Px(18);
        private static int RowHeight => Px(25);
        private static int FooterHeight => Px(20);
        public static int ViewWidth => Px(470);

        /// <summary>How close to the bottom edge the pointer has to be to make the window taller or shorter.</summary>
        public static int GripHeight => Px(6);

        private static Font Display(float size, FontStyle style = FontStyle.Bold) =>
            FontLibrary.Create(AppTheme.DisplayFamily, Math.Max(5f, size * AppTheme.FontScale), style);

        public LapDetailView(string driverName, string subtitle, int streakThreshold)
        {
            _driverName = string.IsNullOrWhiteSpace(driverName) ? "Driver" : driverName;
            _subtitle = subtitle ?? string.Empty;
            _streakThreshold = Math.Max(2, streakThreshold);

            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
            TabStop = false;
            BackColor = AppTheme.Surface;
            Size = new Size(ViewWidth, HeightFor(DefaultRows));
        }

        public static int HeightFor(int rows) => ChromeHeight + Math.Max(1, rows) * RowHeight;

        /// <summary>The laps that fit a window this tall, within the limits.</summary>
        public static int RowsFor(int height) => Math.Clamp((height - ChromeHeight) / Math.Max(1, RowHeight), MinRows, MaxRows);

        /// <summary>Everything but the lap rows: header, tiles, column titles, footer and padding.</summary>
        private static int ChromeHeight =>
            Pad + HeaderHeight + StatsHeight + Px(10) + ColumnHeaderHeight + Px(8) + FooterHeight + Pad;

        /// <summary>Where the first lap row is drawn.</summary>
        private static int ListTop => Pad + HeaderHeight + StatsHeight + Px(10) + ColumnHeaderHeight;

        /// <summary>
        /// The scrollbar's track: beside the lap rows, in the margin right of
        /// the Run column, as tall as the rows on screen.
        /// </summary>
        private Rectangle ScrollTrack => new(Width - Pad - Px(1), ListTop, Px(6), Rows * RowHeight);

        /// <summary>The thumb, or empty when every lap fits and there is nothing to scroll.</summary>
        private Rectangle ScrollThumb
        {
            get
            {
                var total = _lapMs.Length;

                if (!_present || total <= Rows)
                {
                    return Rectangle.Empty;
                }

                var track = ScrollTrack;
                var height = Math.Max(Px(20), track.Height * Rows / total);
                var first = total - Rows - ScrollBack;
                var top = track.Top + (int)Math.Round((track.Height - height) * first / (double)(total - Rows));

                return new Rectangle(track.Left, top, track.Width, height);
            }
        }

        /// <summary>Scrolled by this many laps - positive towards the older end.</summary>
        private void ScrollBy(int laps) => ScrollTo(ScrollBack + laps);

        private void ScrollTo(int back)
        {
            back = Math.Clamp(back, 0, MaxScrollBack);

            if (back != _scrollBack)
            {
                _scrollBack = back;
                Invalidate();
            }
        }

        /// <summary>The arrow, Page and Home/End keys, while the window has the focus. True when it was one of them.</summary>
        public bool ScrollKey(Keys key)
        {
            switch (key)
            {
                case Keys.Up: ScrollBy(1); return true;
                case Keys.Down: ScrollBy(-1); return true;
                case Keys.PageUp: ScrollBy(Math.Max(1, Rows - 1)); return true;
                case Keys.PageDown: ScrollBy(-Math.Max(1, Rows - 1)); return true;
                case Keys.Home: ScrollTo(MaxScrollBack); return true;
                case Keys.End: ScrollTo(0); return true;
                default: return false;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            // Three laps a notch, as a list box scrolls three lines. Wheel up
            // goes back towards the start of the race.
            var notches = e.Delta / 120;
            ScrollBy((notches == 0 ? Math.Sign(e.Delta) : notches) * 3);
        }

        /// <summary>The X, top right of the header.</summary>
        private Rectangle CloseRect => new(Width - Pad - Px(26), Pad + Px(2), Px(26), Px(26));

        /// <summary>The header, where the window is dragged from - all of it but the X.</summary>
        public bool IsDragArea(Point point) =>
            point.Y >= 0 && point.Y < Pad + HeaderHeight && !CloseRect.Contains(point);

        /// <summary>The screen point a hit-test message carries.</summary>
        public static Point PointFrom(IntPtr lParam)
        {
            var value = unchecked((int)(long)lParam);
            return new Point(unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
        }

        protected override void WndProc(ref Message m)
        {
            // The header and the bottom edge belong to the window under the
            // card, which drags and sizes itself from them - see
            // LapDetailWindow.WndProc. Told so by answering "not here".
            if (m.Msg == WmNcHitTest && IsHandleCreated)
            {
                var point = PointToClient(PointFrom(m.LParam));

                if (IsDragArea(point) || point.Y >= Height - GripHeight)
                {
                    m.Result = HtTransparent;
                    return;
                }
            }

            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetCloseHot(CloseRect.Contains(e.Location));

            if (_thumbDragY is { } startY)
            {
                var total = _lapMs.Length;
                var room = ScrollTrack.Height - ScrollThumb.Height;

                if (total > Rows && room > 0)
                {
                    var first = _thumbDragFirst + (int)Math.Round((e.Y - startY) * (total - Rows) / (double)room);
                    ScrollTo(total - Rows - first);
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _closePressed = false;
            SetCloseHot(false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _closePressed = e.Button == MouseButtons.Left && CloseRect.Contains(e.Location);

            var thumb = ScrollThumb;

            if (e.Button != MouseButtons.Left || thumb.IsEmpty)
            {
                return;
            }

            // A few pixels either side of the track count, since it is thin.
            var track = ScrollTrack;
            track.Inflate(Px(4), 0);

            if (!track.Contains(e.Location))
            {
                return;
            }

            if (e.Y >= thumb.Top && e.Y < thumb.Bottom)
            {
                _thumbDragY = e.Y;
                _thumbDragFirst = _lapMs.Length - Rows - ScrollBack;
                Invalidate(ScrollTrack);
            }
            else
            {
                // Above or below the thumb: a page that way, as a scrollbar does.
                var page = Math.Max(1, Rows - 1);
                ScrollBy(e.Y < thumb.Top ? page : -page);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_thumbDragY is not null)
            {
                _thumbDragY = null;
                Invalidate(ScrollTrack);
            }

            var clicked = _closePressed && e.Button == MouseButtons.Left && CloseRect.Contains(e.Location);
            _closePressed = false;

            if (clicked)
            {
                CloseClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        private void SetCloseHot(bool hot)
        {
            if (_closeHot != hot)
            {
                _closeHot = hot;
                Invalidate(CloseRect);
            }
        }

        public void Apply(RacerStanding? standing, int position)
        {
            _present = standing is not null;
            _position = position;

            var lapsBefore = _lapMs.Length;

            if (standing is null)
            {
                _scrollBack = 0;
                _lapMs = Array.Empty<long>();
                _streakAtLap = Array.Empty<int>();
                _recoveryLaps = new HashSet<int>();
                _sectors = Array.Empty<long[]?>();
                _bestSectors = null;
                _idealMs = null;
                _laps = 0;
                _bestMs = 0;
                _penaltyMs = 0;
                _currentRun = 0;
            }
            else
            {
                // Copied rather than read during paint. Ghost cars feed laps
                // in from a background timer, and a List being appended to on
                // one thread while it is walked on another either throws or
                // hands back nonsense - neither of which is worth risking to
                // save copying a dozen longs.
                //
                // All three are copied into locals first and only then
                // assigned, so a copy that throws part-way leaves the previous
                // set intact as a whole rather than lap times from this lap
                // paired with recovery indexes from the last one.
                try
                {
                    var lapMs = standing.LapTimesMs.ToArray();
                    var streakAtLap = standing.StreakAtLap.ToArray();
                    var recoveryLaps = new HashSet<int>(standing.RecoveryLapIndexes.ToArray());

                    var sectors = Enumerable.Range(0, lapMs.Length).Select(standing.SectorsOf).ToArray();
                    var bestSectors = Sectors.Bests(standing);
                    var ideal = Sectors.TheoreticalBest(standing);

                    _lapMs = lapMs;
                    _streakAtLap = streakAtLap;
                    _recoveryLaps = recoveryLaps;
                    _sectors = sectors;
                    _bestSectors = bestSectors;
                    _idealMs = ideal;
                }
                catch
                {
                    // Caught mid-append - keep the previous copy. The next
                    // lap redraws this anyway.
                }

                _laps = standing.Laps;
                _bestMs = standing.FastestLapMs;
                _penaltyMs = standing.PenaltyMs;
                _currentRun = standing.ConsecutiveValidLaps;

                // Scrolled back to read older laps, a new one coming in moves
                // the count along with it, so the same laps stay on screen
                // instead of the list creeping a row a lap. At the bottom it
                // stays at 0 and follows the newest lap.
                if (_scrollBack > 0 && _lapMs.Length > lapsBefore)
                {
                    _scrollBack += _lapMs.Length - lapsBefore;
                }
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            AppTheme.DrawCard(g, new RectangleF(0, 0, Width, Height), AppTheme.Primary, 10);

            var left = Pad + Px(6);
            var right = Width - Pad - Px(6);

            DrawHeader(g, left, right, Pad);
            DrawClose(g);

            var statsTop = Pad + HeaderHeight;
            DrawStats(g, left, right - left, statsTop);

            var listTop = statsTop + StatsHeight + Px(10);
            DrawColumnHeader(g, left, right, listTop);

            DrawLaps(g, left, right, listTop + ColumnHeaderHeight);

            DrawFooter(g, left, right, Height - Pad - FooterHeight);
        }

        private void DrawHeader(Graphics g, int left, int right, int top)
        {
            using var nameFont = Display(15f);
            using var nameBrush = new SolidBrush(AppTheme.TextDark);
            using var subBrush = new SolidBrush(AppTheme.TextMuted);
            using var trim = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center,
            };

            // Left of the X, which takes the header's top-right corner.
            right = Math.Min(right, CloseRect.Left - Px(6));

            var textRight = (float)right;

            if (_present && _position > 0)
            {
                var pill = new RectangleF(right - Px(46), top + Px(3), Px(46), Px(23));
                AppTheme.DrawPill(g, pill, AppTheme.Primary, $"P{_position}", AppTheme.OnPrimary);
                textRight = pill.Left - Px(8);
            }

            g.DrawString(_driverName, nameFont, nameBrush,
                new RectangleF(left, top, textRight - left, Px(24)), trim);

            g.DrawString(_subtitle, AppTheme.SmallFont, subBrush,
                new RectangleF(left, top + Px(23), textRight - left, Px(17)), trim);
        }

        /// <summary>The X: a cross in the muted text colour, on a soft tile while the pointer is over it.</summary>
        private void DrawClose(Graphics g)
        {
            var rect = CloseRect;

            if (_closeHot)
            {
                using var path = AppTheme.RoundedRect(rect, 6);
                using var fill = new SolidBrush(AppTheme.RowAlt);
                g.FillPath(fill, path);
            }

            var inset = Px(9);
            using var pen = new Pen(_closeHot ? AppTheme.TextDark : AppTheme.TextMuted, Math.Max(1.5f, Px(2)))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            g.DrawLine(pen, rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
            g.DrawLine(pen, rect.Right - inset, rect.Top + inset, rect.Left + inset, rect.Bottom - inset);
        }

        private void DrawStats(Graphics g, int left, int innerWidth, int top)
        {
            var gap = Px(8);
            var tileWidth = (innerWidth - gap * 3) / 4f;
            var tileHeight = StatsHeight - Px(6);

            var average = AverageFlyingMs();

            DrawTile(g, new RectangleF(left, top, tileWidth, tileHeight),
                "Laps", _present ? _laps.ToString() : "-", AppTheme.TextDark);

            DrawTile(g, new RectangleF(left + tileWidth + gap, top, tileWidth, tileHeight),
                "Best", _bestMs > 0 ? $"{_bestMs / 1000.0:0.000}" : "-", AppTheme.Primary);

            DrawTile(g, new RectangleF(left + (tileWidth + gap) * 2, top, tileWidth, tileHeight),
                "Average", average > 0 ? $"{average / 1000.0:0.000}" : "-", AppTheme.TextDark);

            DrawTile(g, new RectangleF(left + (tileWidth + gap) * 3, top, tileWidth, tileHeight),
                "Run", _present ? _currentRun.ToString() : "-",
                _currentRun >= _streakThreshold ? AppTheme.Success : AppTheme.TextDark);
        }

        /// <summary>
        /// The driver's average over FLYING laps only.
        ///
        /// The first counted pass runs from the green flag to the line, which
        /// is not a lap and is typically a second or more slower than one.
        /// Including it drags the average down by exactly the amount that
        /// makes a driver's numbers look wrong to them.
        ///
        /// Laps that ran through a crash recovery are left out too, matching
        /// RaceGaps.PaceLaps, so this reads the same as the leaderboard's
        /// Average column for the same driver.
        /// </summary>
        private double AverageFlyingMs()
        {
            long total = 0;
            var count = 0;

            for (var i = 1; i < _lapMs.Length; i++)
            {
                if (_recoveryLaps.Contains(i))
                {
                    continue;
                }

                total += _lapMs[i];
                count++;
            }

            return count > 0 ? total / (double)count : 0;
        }

        /// <summary>The app's stat tile, sized down and with the value measured to fit.</summary>
        private static void DrawTile(Graphics g, RectangleF bounds, string label, string value, Color valueColor)
        {
            AppTheme.DrawCard(g, bounds);

            using var labelBrush = new SolidBrush(AppTheme.TextMuted);
            using var valueBrush = new SolidBrush(valueColor);
            using var centred = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,

                // NoClip as well: a line a pixel taller than its box, from
                // rounding, still draws whole rather than shaved.
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
            };

            // The label's box is as tall as the font's own line, measured on
            // the surface being painted (31 Oct 2026, Asher: "tops of the
            // kpi's on the lap detail windows is cut off"). It was a fixed
            // 13 pixels, shorter than the caption font's line, and DrawString
            // clips to its box - so the centred caps lost their top edge, and
            // at a larger text size or display scale lost more of it.
            var labelHeight = (float)Math.Ceiling(AppTheme.MicroFont.GetHeight(g));
            var labelRect = new RectangleF(bounds.Left + 3, bounds.Top + Px(4), bounds.Width - 6, labelHeight);

            g.DrawString(AppTheme.Spaced(label.ToUpperInvariant()), AppTheme.MicroFont, labelBrush, labelRect, centred);

            var valueTop = labelRect.Bottom - Px(1);
            var valueRect = new RectangleF(bounds.Left + 3, valueTop, bounds.Width - 6, Math.Max(1f, bounds.Bottom - Px(3) - valueTop));

            // Stepped down in whole points until it fits, the same way the
            // race stat strip does it - a lap time at a fixed size ends up
            // ellipsised into "4.5..." on a narrow tile, which is worse than
            // slightly smaller digits.
            for (var size = 14f; size >= 9f; size -= 1f)
            {
                using var font = Display(size);

                if (g.MeasureString(value, font).Width <= bounds.Width - 8 || size <= 9f)
                {
                    g.DrawString(value, font, valueBrush, valueRect, centred);
                    return;
                }
            }
        }

        // Column geometry, in one place so the header and the rows cannot
        // drift apart - which is what happens the moment they each compute
        // their own offsets.
        private static int TimeX(int left) => left + Px(42);
        private static int BarLeft(int left) => left + Px(122);
        private static int RunLeft(int right) => right - Px(76);
        private static int DeltaRight(int right) => RunLeft(right) - Px(8);
        private static int DeltaLeft(int right) => DeltaRight(right) - Px(72);
        private static int BarRight(int right) => DeltaLeft(right) - Px(10);

        private void DrawColumnHeader(Graphics g, int left, int right, int top)
        {
            using var brush = new SolidBrush(AppTheme.TextMuted);
            using var near = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
            using var far = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };

            var height = ColumnHeaderHeight;

            g.DrawString(AppTheme.Spaced("LAP"), AppTheme.MicroFont, brush, new RectangleF(left, top, Px(40), height), near);
            g.DrawString(AppTheme.Spaced("TIME"), AppTheme.MicroFont, brush, new RectangleF(TimeX(left), top, Px(76), height), near);
            g.DrawString(AppTheme.Spaced(HasSectors ? "SECTORS" : "PACE"), AppTheme.MicroFont, brush, new RectangleF(BarLeft(left), top, BarRight(right) - BarLeft(left), height), near);
            // Room to the left of the figures' column for the whole caption -
            // in the figures' own 72 pixels it was cut to "OFF BES". Drawn
            // from the right, so it ends over the figures as before.
            g.DrawString(AppTheme.Spaced("OFF BEST"), AppTheme.MicroFont, brush, new RectangleF(DeltaRight(right) - Px(110), top, Px(110), height), far);
            g.DrawString(AppTheme.Spaced("RUN"), AppTheme.MicroFont, brush, new RectangleF(RunLeft(right), top, right - RunLeft(right), height), far);

            using var rule = new Pen(AppTheme.Border);
            g.DrawLine(rule, left, top + height - 2, right, top + height - 2);
        }

        private void DrawLaps(Graphics g, int left, int right, int top)
        {
            if (!_present)
            {
                DrawNote(g, left, right, top, "This driver is no longer in the standings.");
                return;
            }

            if (_lapMs.Length == 0)
            {
                DrawNote(g, left, right, top, "No laps yet - this fills in as they cross the line.");
                return;
            }

            var shown = Math.Min(Rows, _lapMs.Length);
            var first = _lapMs.Length - shown - ScrollBack;
            var last = first + shown;

            // The pace bars are scaled across the laps actually on screen,
            // and across flying laps only: the green-to-line pass is not a
            // lap time and would otherwise set the scale single-handedly,
            // squashing every real lap into the same short bar. A lap that
            // ran through a crash recovery is left out of the scale for the
            // same reason - it spans the whole stoppage - and simply draws as
            // a full-width bar, since the bar width is clamped.
            long fastest = 0;
            long slowest = 0;

            for (var i = Math.Max(first, 1); i < last; i++)
            {
                if (_recoveryLaps.Contains(i))
                {
                    continue;
                }

                var ms = _lapMs[i];

                if (fastest == 0 || ms < fastest)
                {
                    fastest = ms;
                }

                if (ms > slowest)
                {
                    slowest = ms;
                }
            }

            var span = Math.Max(1, slowest - fastest);

            for (var row = 0; row < shown; row++)
            {
                DrawLapRow(g, left, right, top + row * RowHeight, first + row, fastest, span);
            }

            DrawScrollbar(g);
        }

        /// <summary>A slim track and thumb beside the rows, only when there are more laps than fit.</summary>
        private void DrawScrollbar(Graphics g)
        {
            var thumb = ScrollThumb;

            if (thumb.IsEmpty)
            {
                return;
            }

            using (var trackPath = AppTheme.RoundedRect(ScrollTrack, 3))
            using (var trackFill = new SolidBrush(AppTheme.RowAlt))
            {
                g.FillPath(trackFill, trackPath);
            }

            using var thumbPath = AppTheme.RoundedRect(thumb, 3);
            using var thumbFill = new SolidBrush(_thumbDragY is null ? AppTheme.Border : AppTheme.TextMuted);
            g.FillPath(thumbFill, thumbPath);
        }

        private void DrawLapRow(Graphics g, int left, int right, int top, int lapIndex, long fastest, long span)
        {
            var lapMs = _lapMs[lapIndex];

            // The first counted pass runs from the green flag to the line.
            // Labelled as what it is rather than as "lap 1", because calling
            // it a lap is what made the app's own best-lap numbers look
            // wrong for a season.
            var isStartLap = lapIndex == 0;

            // The clean-run count as it stood after this lap. Laps recorded
            // before this was tracked - and any lap added by hand - have no
            // entry, so the run column is simply left blank rather than
            // guessed at.
            var streak = lapIndex < _streakAtLap.Length ? _streakAtLap[lapIndex] : -1;
            var previous = lapIndex > 0 && lapIndex - 1 < _streakAtLap.Length ? _streakAtLap[lapIndex - 1] : -1;

            var inRun = streak >= _streakThreshold;
            var endedRun = streak == 1 && previous >= _streakThreshold;
            var isBest = !isStartLap && _bestMs > 0 && lapMs == _bestMs;

            if (isBest || endedRun)
            {
                var rowRect = new RectangleF(left - Px(4), top + 1, right - left + Px(8), RowHeight - 2);
                using var path = AppTheme.RoundedRect(rowRect, 5);
                using var fill = new SolidBrush(isBest ? AppTheme.PrimaryTint : Tinted(AppTheme.Danger, 28));
                g.FillPath(fill, path);
            }

            using var muted = new SolidBrush(AppTheme.TextMuted);
            using var near = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
            };
            using var far = new StringFormat
            {
                Alignment = StringAlignment.Far,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center,
            };

            g.DrawString(isStartLap ? "START" : $"L{lapIndex + 1}", AppTheme.SmallFont, muted,
                new RectangleF(left, top, Px(40), RowHeight), near);

            using var timeFont = Display(11.5f);
            using var timeBrush = new SolidBrush(isBest ? AppTheme.Primary : isStartLap ? AppTheme.TextMuted : AppTheme.TextDark);

            g.DrawString($"{lapMs / 1000.0:0.000}", timeFont, timeBrush,
                new RectangleF(TimeX(left), top, Px(76), RowHeight), near);

            if (HasSectors)
            {
                // The sectors where the bar would be: each in the driver's
                // best colour when it is their best in that sector, and a
                // dash for a lap without them (a split missed, the start lap).
                DrawSectors(g, BarLeft(left), BarRight(right), top, lapIndex);
            }
            else if (!isStartLap)
            {
                var track = new RectangleF(BarLeft(left), top + RowHeight / 2f - Px(4), BarRight(right) - BarLeft(left), Px(8));

                using (var trackPath = AppTheme.RoundedRect(track, 4))
                using (var trackFill = new SolidBrush(AppTheme.RowAlt))
                {
                    g.FillPath(trackFill, trackPath);
                }

                // Longer bar = slower lap. A minimum stub so the quickest lap
                // on screen still reads as a bar rather than as nothing.
                var stub = Px(10);
                var scale = Math.Clamp((lapMs - fastest) / (double)span, 0, 1);
                var barWidth = (float)(stub + scale * Math.Max(0, track.Width - stub));

                if (barWidth > 2)
                {
                    using var barPath = AppTheme.RoundedRect(new RectangleF(track.Left, track.Top, barWidth, track.Height), 4);
                    using var barFill = new SolidBrush(isBest ? AppTheme.Success : inRun ? AppTheme.Primary : AppTheme.Accent);
                    g.FillPath(barFill, barPath);
                }
            }

            var delta = isStartLap
                ? "green to line"
                : _bestMs <= 0 ? string.Empty
                : lapMs <= _bestMs ? "their best"
                : $"+{(lapMs - _bestMs) / 1000.0:0.000}";

            g.DrawString(delta, AppTheme.SmallFont, isBest ? timeBrush : muted,
                new RectangleF(DeltaLeft(right), top, DeltaRight(right) - DeltaLeft(right), RowHeight), far);

            if (endedRun)
            {
                using var ended = new SolidBrush(AppTheme.Danger);
                g.DrawString("run ended", AppTheme.SmallFont, ended,
                    new RectangleF(RunLeft(right), top, right - RunLeft(right), RowHeight), far);
            }
            else if (inRun)
            {
                AppTheme.DrawPill(g, new RectangleF(right - Px(38), top + Px(4), Px(38), RowHeight - Px(8)),
                    AppTheme.Success, streak.ToString(), AppTheme.OnSuccess);
            }
            else if (streak > 0 && !isStartLap)
            {
                g.DrawString(streak.ToString(), AppTheme.SmallFont, muted,
                    new RectangleF(RunLeft(right), top, right - RunLeft(right), RowHeight), far);
            }
        }

        /// <summary>One lap's sectors across the room the pace bar has, equally spaced.</summary>
        private void DrawSectors(Graphics g, int left, int right, int top, int lapIndex)
        {
            var sectors = lapIndex < _sectors.Length ? _sectors[lapIndex] : null;
            var count = _bestSectors?.Length ?? 0;

            using var muted = new SolidBrush(AppTheme.TextMuted);
            using var plain = new SolidBrush(AppTheme.TextDark);
            using var best = new SolidBrush(AppTheme.Success);
            using var near = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
            };

            if (sectors is null || sectors.Length != count)
            {
                g.DrawString("-", AppTheme.SmallFont, muted, new RectangleF(left, top, right - left, RowHeight), near);
                return;
            }

            var width = (right - left) / (float)count;

            for (var i = 0; i < count; i++)
            {
                var isBest = _bestSectors is { } bests && sectors[i] == bests[i];

                g.DrawString(Sectors.Format(sectors[i]), AppTheme.SmallFont, isBest ? best : plain,
                    new RectangleF(left + width * i, top, width, RowHeight), near);
            }
        }

        private void DrawNote(Graphics g, int left, int right, int top, string text)
        {
            using var brush = new SolidBrush(AppTheme.TextMuted);
            using var centred = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };

            g.DrawString(text, AppTheme.BodyFont, brush, new RectangleF(left, top, right - left, RowHeight), centred);
        }

        private void DrawFooter(Graphics g, int left, int right, int top)
        {
            var total = _lapMs.Length;
            var shown = Math.Min(Rows, total);
            var back = ScrollBack;

            var text = total <= shown
                ? "Updates live - drag the name to move, Esc or X closes"
                : back > 0
                    ? $"Laps {total - shown - back + 1}-{total - back} of {total} - scroll down for the latest"
                    : $"Last {shown} of {total} laps - scroll up for earlier ones - updates live";

            if (_penaltyMs != 0)
            {
                text = $"Penalty {_penaltyMs / 1000.0:0.0}s applied - " + text;
            }

            // Their best sectors on one lap: what they could do, and how
            // much of it they have put together so far.
            if (_idealMs is { } ideal && ideal > 0)
            {
                var gap = _bestMs > ideal ? $" ({(_bestMs - ideal) / 1000.0:0.00} under their best)" : string.Empty;
                text = $"Ideal lap {Sectors.Format(ideal)}{gap} - " + text;
            }

            using var brush = new SolidBrush(AppTheme.TextMuted);
            using var format = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
                LineAlignment = StringAlignment.Center,
            };

            using var rule = new Pen(AppTheme.Border);
            g.DrawLine(rule, left, top - Px(4), right, top - Px(4));

            g.DrawString(text, AppTheme.SmallFont, brush, new RectangleF(left, top, right - left, FooterHeight), format);
        }

        private static Color Tinted(Color color, int alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
