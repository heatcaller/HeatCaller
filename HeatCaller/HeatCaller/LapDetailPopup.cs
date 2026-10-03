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
/// So this is a floating panel instead, drawn with the same primitives as
/// the rest of the app (<see cref="AppTheme.DrawCard"/>, the stat tile, the
/// pill, the micro-caption) so it belongs to HeatCaller rather than to
/// Windows. Every lap gets its own line: the time in the display face, a
/// pace bar scaled across the laps on screen, how far off the driver's best
/// it was, and what the clean run stood at - with the best lap and the lap
/// that ended a run called out in colour.
///
/// It is a ToolStripDropDown rather than a Form because a dropdown already
/// does the three things a panel like this needs and a Form does not: it
/// floats above the window without stealing focus from the race screen, it
/// closes when you click anywhere else or press Escape, and it never ends
/// up behind the main window while a race is running.
///
/// The contents update live - <see cref="Apply"/> is called from the same
/// leaderboard refresh that redraws the grid - so it can be left open on a
/// driver for a whole race.
/// </summary>
public sealed class LapDetailPopup : ToolStripDropDown
{
    private readonly LapDetailView _view;
    private readonly ToolStripControlHost _host;

    /// <summary>The driver this popup is showing, so the leaderboard can keep feeding it.</summary>
    public string Transponder { get; }

    public LapDetailPopup(string transponder, string driverName, string subtitle, int streakThreshold)
    {
        Transponder = transponder;

        _view = new LapDetailView(driverName, subtitle, streakThreshold);

        _host = new ToolStripControlHost(_view)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
            Size = _view.Size,
        };

        AutoSize = false;
        AutoClose = true;

        // The system shadow is drawn around the window RECTANGLE, so with a
        // rounded region it appears as square corners floating just outside
        // round ones. The card's own border does the separating instead.
        DropShadowEnabled = false;

        Padding = Padding.Empty;
        Margin = Padding.Empty;
        BackColor = AppTheme.Surface;

        Items.Add(_host);
        Size = _view.Size;
        ApplyRegion();
    }

    /// <summary>
    /// Feeds the popup the driver's current standing - or null when they are
    /// no longer in the standings, which the panel says rather than freezing
    /// on numbers that have stopped being true.
    /// </summary>
    public void Apply(RacerStanding? standing, int position)
    {
        _view.Apply(standing, position);

        if (Size != _view.Size)
        {
            _host.Size = _view.Size;
            Size = _view.Size;
            ApplyRegion();
        }

        _view.Invalidate();
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

    /// <summary>The panel itself - everything below here is paint.</summary>
    private sealed class LapDetailView : Control
    {
        /// <summary>Laps shown at once. Older ones scroll off the top, because the recent end is the one being read.</summary>
        private const int MaxRows = 12;

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
        private static int ViewWidth => Px(470);

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
            Size = new Size(ViewWidth, HeightFor(0));
        }

        private static int HeightFor(int rows) =>
            Pad + HeaderHeight + StatsHeight + Px(10) + ColumnHeaderHeight
            + Math.Max(1, rows) * RowHeight + Px(8) + FooterHeight + Pad;

        public void Apply(RacerStanding? standing, int position)
        {
            _present = standing is not null;
            _position = position;

            if (standing is null)
            {
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
            }

            var height = HeightFor(Math.Min(MaxRows, _lapMs.Length));

            if (Width != ViewWidth || Height != height)
            {
                Size = new Size(ViewWidth, height);
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
                FormatFlags = StringFormatFlags.NoWrap,
            };

            g.DrawString(AppTheme.Spaced(label.ToUpperInvariant()), AppTheme.MicroFont, labelBrush,
                new RectangleF(bounds.Left + 3, bounds.Top + Px(5), bounds.Width - 6, Px(13)), centred);

            var valueRect = new RectangleF(bounds.Left + 3, bounds.Top + Px(17), bounds.Width - 6, bounds.Height - Px(21));

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
            g.DrawString(AppTheme.Spaced("OFF BEST"), AppTheme.MicroFont, brush, new RectangleF(DeltaLeft(right), top, DeltaRight(right) - DeltaLeft(right), height), far);
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

            var shown = Math.Min(MaxRows, _lapMs.Length);
            var first = _lapMs.Length - shown;

            // The pace bars are scaled across the laps actually on screen,
            // and across flying laps only: the green-to-line pass is not a
            // lap time and would otherwise set the scale single-handedly,
            // squashing every real lap into the same short bar. A lap that
            // ran through a crash recovery is left out of the scale for the
            // same reason - it spans the whole stoppage - and simply draws as
            // a full-width bar, since the bar width is clamped.
            long fastest = 0;
            long slowest = 0;

            for (var i = Math.Max(first, 1); i < _lapMs.Length; i++)
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
            var shown = Math.Min(MaxRows, total);

            var text = total > shown
                ? $"Last {shown} of {total} laps - updates live - click away to close"
                : "Updates live - click away to close";

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
