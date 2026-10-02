using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Plays a finished race back.
///
/// A result sheet says who won. It does not say that second place led for
/// eleven laps and lost it at the last stop, or that the two of them swapped
/// places six times - and those are the races people actually talk about
/// afterwards. All of it is already in the saved lap times; nobody has ever
/// been able to see it.
///
/// What is drawn is the running order over time, not the cars on the track:
/// a single crossing per lap says when a car passed the line and nothing at
/// all about where it was in between, so anything showing cars moving around
/// a circuit would be invention. A driver's lane holds their position, and
/// the lanes cross when the order changes - which is exactly the thing worth
/// watching, and is honest about what was measured.
///
/// On the layout pieces (DialogFit): the lanes taking the room, and under
/// them one line of playback controls - Play, the speed and the clock side by
/// side, and the scrub bar across the rest of the line, or on a line of its
/// own when the window leaves it too little. It opens at the size it had at
/// the normal text size, at the size in use - never bigger than the screen -
/// and can be made bigger, or narrower down to the width it always could be,
/// the lanes taking or giving up the room. It used to be placed in pixels:
/// the lanes docked over a bar of a fixed height, the speed, the clock and
/// the scrub bar at x's worked out from a Play button of a fixed size, and
/// the clock given 160px whatever the text size.
/// </summary>
public sealed class RaceReplayForm : Form
{
    /// <summary>How big the window opens inside its frame at the normal text size - the size it had. The lanes take any height past what the controls under them need.</summary>
    private const int DialogWidth = 980;
    private const int DialogHeight = 640;

    /// <summary>How narrow the window can be made, frame and all, at the normal text size - as narrow as it always could be.</summary>
    private const int LeastWindowWidth = 760;

    /// <summary>The least the lanes get at the normal text size before the window scrolls instead - about what they had in the smallest window the replay could be made.</summary>
    private const int CanvasLeastHeight = 360;

    /// <summary>The speed drop-down's width at the normal text size - wider when its longest choice needs it.</summary>
    private const int SpeedWidth = 88;

    /// <summary>The least the scrub bar can do with beside the other controls, at the normal text size - with less, it takes a line of its own.</summary>
    private const int ScrubLeastWidth = 160;

    private readonly SessionRecord _session;
    private readonly List<Runner> _runners;
    private readonly long _raceLengthMs;

    private readonly ReplayCanvas _canvas;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly SliderControl _scrub = new();
    private readonly Button _btnPlay = new();
    private readonly Label _lblClock = new();
    private readonly ComboBox _cmbSpeed = new();

    private long _positionMs;
    private double _speed = 4;
    private bool _scrubbing;

    /// <summary>One driver, as a sequence of moments they crossed the line.</summary>
    private sealed class Runner
    {
        public required string Name { get; init; }
        public required string Car { get; init; }
        public required Color Colour { get; init; }
        public required int Finished { get; init; }

        /// <summary>Elapsed race time at each crossing, cumulative.</summary>
        public required List<long> CrossingsMs { get; init; }

        /// <summary>Laps completed by a given moment.</summary>
        public int LapsAt(long ms)
        {
            var laps = 0;

            foreach (var crossing in CrossingsMs)
            {
                if (crossing > ms)
                {
                    break;
                }

                laps++;
            }

            return laps;
        }

        /// <summary>
        /// How far round the current lap they are, 0 to 1 - and 0 once they
        /// have made their last crossing, when there is no current lap.
        ///
        /// Interpolated between crossings, which is a claim about progress
        /// rather than about position on the track - a car is not
        /// travelling at a constant speed round a lap. It is what makes the
        /// order change smoothly instead of jumping, and it only ever
        /// decides who is drawn above whom between two crossings.
        /// </summary>
        public double FractionAt(long ms)
        {
            var laps = LapsAt(ms);

            // Past their last crossing there is no next lap they are partway
            // round: they have finished or stopped, and LapsAt already counts
            // every lap they did. This returned 1, which added a lap they
            // never ran - a car that retired after five laps jumped to six and
            // was drawn as the leader over cars still on their sixth, and the
            // winner taking the flag in a twenty-lap race turned the heading
            // to "Lap 21" and put the car half a second behind a lap down.
            if (laps >= CrossingsMs.Count)
            {
                return 0;
            }

            var from = laps == 0 ? 0 : CrossingsMs[laps - 1];
            var to = CrossingsMs[laps];
            var span = Math.Max(1, to - from);

            return Math.Clamp((ms - from) / (double)span, 0, 1);
        }

        /// <summary>Total progress, in laps - the number the order is decided on.</summary>
        public double ProgressAt(long ms) => LapsAt(ms) + FractionAt(ms);
    }

    public RaceReplayForm(SessionRecord session)
    {
        _session = session;
        _runners = BuildRunners(session);
        _raceLengthMs = _runners.Count == 0
            ? 0
            : _runners.Max(r => r.CrossingsMs.Count == 0 ? 0 : r.CrossingsMs[^1]);

        Text = $"Replay - {(string.IsNullOrWhiteSpace(session.Label) ? "Race" : session.Label)}";

        _canvas = new ReplayCanvas(this);

        BuildTransport();

        // The lanes take the room; the playback controls are the last line
        // of the page, where their bar was.
        var page = new FillPage();
        page.AddFill(_canvas, UiLayout.AtTextSize(CanvasLeastHeight), gapBelow: UiLayout.RowGap);
        page.Add(BuildTransportBar(), gapBelow: 0);

        Controls.Add(page);

        // Nothing here starts it: only Play does (Toggle), so a window that
        // is built and never shown leaves no clock running.
        _timer.Tick += (_, _) => Advance();

        AppTheme.ApplyRecursively(this);

        // Styled after the theme pass, which would otherwise repaint it as
        // an ordinary secondary button.
        AppTheme.StyleButton(_btnPlay, AppTheme.ButtonKind.Primary);

        // After the theme pass, in the faces it leaves them in: the speed as
        // wide as it was at the normal text size, at the size in use, or as
        // its longest choice needs; the clock as big as its longest reading.
        (Font Font, int Count, int Width)? speedWidth = null;
        _cmbSpeed.Width = Math.Max(UiLayout.AtTextSize(SpeedWidth), Pieces.PickerWidth(_cmbSpeed, ref speedWidth));
        _lblClock.Size = ClockSize();

        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);

        // And it can be made narrower than it opened, down to the width it
        // always could be, at the text size in use: nothing in it is measured
        // for its width - the lanes are drawn across whatever they have, and
        // the scrub bar goes under the other controls when the line is short.
        // After DialogFit's own Load handler, added above, which sets the
        // least to the width it opened at.
        Load += (_, _) => MinimumSize = new Size(
            Math.Min(MinimumSize.Width, UiLayout.AtTextSize(LeastWindowWidth)),
            MinimumSize.Height);
    }

    /// <summary>
    /// Turns each driver's lap times into the moments they crossed the line.
    ///
    /// The first entry is the run from the green to their first crossing,
    /// so the cumulative total is a real elapsed race time from the start
    /// and the lanes all begin together.
    /// </summary>
    private static List<Runner> BuildRunners(SessionRecord session)
    {
        var palette = AppTheme.SeriesColours();
        var runners = new List<Runner>();

        var ordered = session.Results
            .OrderBy(r => r.Position == 0 ? int.MaxValue : r.Position)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var entry = ordered[i];

            if (entry.LapTimesMs.Count == 0)
            {
                // Saved before lap-by-lap recording existed. There is
                // nothing to play back for this driver, and inventing an
                // even pace from their total would be a drawing of an
                // average, not of a race.
                continue;
            }

            var crossings = new List<long>(entry.LapTimesMs.Count);
            var running = 0L;

            foreach (var lap in entry.LapTimesMs)
            {
                running += Math.Max(0, lap);
                crossings.Add(running);
            }

            runners.Add(new Runner
            {
                Name = string.IsNullOrWhiteSpace(entry.DriverName) ? entry.Transponder : entry.DriverName,
                Car = entry.CarModel,
                Colour = palette[i % palette.Count],
                Finished = entry.Position,
                CrossingsMs = crossings,
            });
        }

        return runners;
    }

    private void BuildTransport()
    {
        _btnPlay.Text = "Play";
        _btnPlay.Click += (_, _) => Toggle();

        _scrub.Minimum = 0;
        _scrub.Maximum = (int)Math.Max(1, _raceLengthMs / 100);
        _scrub.Height = UiLayout.ButtonHeight;

        // Scrubbing has to stop the clock, or the timer keeps writing the
        // position back underneath the drag and the handle fights the hand
        // holding it.
        _scrub.MouseDown += (_, _) => _scrubbing = true;
        _scrub.MouseUp += (_, _) => _scrubbing = false;
        _scrub.ValueChanged += (_, _) =>
        {
            if (!_scrubbing)
            {
                return;
            }

            _positionMs = _scrub.Value * 100L;
            _canvas.Invalidate();
            UpdateClock();
        };

        // In the body face: the window's own is Windows' default, which does
        // not grow with the text, and at 200% the speed was the one small
        // thing on the line (layout pictures, 30 Sep).
        _cmbSpeed.Font = AppTheme.BodyFont;
        _cmbSpeed.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSpeed.Items.AddRange(new object[] { "1x", "2x", "4x", "8x", "16x" });
        _cmbSpeed.SelectedIndex = 2;
        _cmbSpeed.SelectedIndexChanged += (_, _) =>
            _speed = double.Parse(((string)_cmbSpeed.SelectedItem!).TrimEnd('x'));

        // A size of its own rather than one that follows what it says: it is
        // rewritten on every tick while the race plays, and a clock that
        // grew and shrank with its digits would nudge the scrub bar beside it
        // along, and lay the line out again, thirty times a second. Sized to
        // its longest reading once the theme pass is done (ClockSize).
        _lblClock.AutoSize = false;
        _lblClock.Font = AppTheme.BoldFont;

        UpdateClock();
    }

    /// <summary>
    /// The playback controls, on one line under the lanes: Play, the speed
    /// and the clock side by side, a button's gap apart, and the scrub bar
    /// taking the rest of the line - or, when that would leave it less than
    /// it can do with, a line of its own under them (FieldRow).
    /// </summary>
    private FieldRow BuildTransportBar()
    {
        // Centred up and down on the Play button beside them, as
        // FieldGrid.Inline centres what is not a button.
        _cmbSpeed.Anchor = AnchorStyles.Left;
        _lblClock.Anchor = AnchorStyles.Left;

        var bar = new FieldRow();
        bar.Add(null, new ButtonBar().With(_btnPlay, _cmbSpeed, _lblClock));
        bar.AddLast(null, _scrub, () => UiLayout.AtTextSize(ScrubLeastWidth));

        return bar;
    }

    /// <summary>
    /// How big the clock is: its longest reading in this race, in its own
    /// face. The race's length is on both sides of the slash at the end, and
    /// the running side is measured with each digit in turn in every place,
    /// since the digits are not all as wide as each other - so the clock is
    /// never too narrow for the time it is showing, and never changes size
    /// as the race plays. A couple of pixels over, for digits that sit a
    /// little differently beside the colon and the point than beside each
    /// other.
    /// </summary>
    private Size ClockSize()
    {
        var longest = ClockText(_raceLengthMs, _raceLengthMs);
        var slash = longest.IndexOf("  /", StringComparison.Ordinal);
        var running = slash < 0 ? longest : longest[..slash];
        var rest = slash < 0 ? string.Empty : longest[slash..];

        // The flags a label measures its text with.
        const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
        var unbounded = new Size(int.MaxValue, int.MaxValue);
        var widest = Size.Empty;

        foreach (var digit in "0123456789")
        {
            var reading = new string(running.Select(c => char.IsDigit(c) ? digit : c).ToArray()) + rest;
            var measured = TextRenderer.MeasureText(reading, _lblClock.Font, unbounded, flags);

            widest = new Size(Math.Max(widest.Width, measured.Width), Math.Max(widest.Height, measured.Height));
        }

        return new Size(
            widest.Width + 2 + _lblClock.Padding.Horizontal,
            widest.Height + _lblClock.Padding.Vertical);
    }

    private void Toggle()
    {
        if (_timer.Enabled)
        {
            _timer.Stop();
            _btnPlay.Text = "Play";
            return;
        }

        // Playing on from the end would sit there doing nothing, so the
        // button restarts instead.
        if (_positionMs >= _raceLengthMs)
        {
            _positionMs = 0;
        }

        _timer.Start();
        _btnPlay.Text = "Pause";
    }

    private void Advance()
    {
        _positionMs += (long)(_timer.Interval * _speed);

        if (_positionMs >= _raceLengthMs)
        {
            _positionMs = _raceLengthMs;
            _timer.Stop();
            _btnPlay.Text = "Replay";
        }

        if (!_scrubbing)
        {
            var wanted = (int)Math.Clamp(_positionMs / 100, _scrub.Minimum, _scrub.Maximum);

            if (_scrub.Value != wanted)
            {
                _scrub.Value = wanted;
            }
        }

        UpdateClock();
        _canvas.Invalidate();
    }

    private void UpdateClock() => _lblClock.Text = ClockText(_positionMs, _raceLengthMs);

    /// <summary>What the clock reads <paramref name="positionMs"/> into a race <paramref name="lengthMs"/> long - see ClockSize, which measures it at its longest.</summary>
    private static string ClockText(long positionMs, long lengthMs)
    {
        var span = TimeSpan.FromMilliseconds(positionMs);
        var total = TimeSpan.FromMilliseconds(lengthMs);

        return $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds / 100}"
               + $"  /  {(int)total.TotalMinutes}:{total.Seconds:00}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The lanes.
    ///
    /// Each driver holds a row, ordered by how far round they are at the
    /// moment being shown, and the rows animate between orders - so a pass
    /// is two lanes crossing, which is legible at a glance in a way that a
    /// table of changing numbers is not.
    /// </summary>
    private sealed class ReplayCanvas : Control
    {
        private readonly RaceReplayForm _owner;

        public ReplayCanvas(RaceReplayForm owner)
        {
            _owner = owner;

            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var back = new SolidBrush(AppTheme.Background))
            {
                g.FillRectangle(back, ClientRectangle);
            }

            var runners = _owner._runners;

            // Every measure in the lanes is at the text size in use: the
            // positions, names and gaps are drawn in the app's faces, which
            // grow with it, and at fixed pixels a longer name ran into the
            // bar and the lanes were too short for their own text at 200%.
            // Worked out on every paint - each tick while it plays, and on
            // every resize - so the drawing follows the room the window gives
            // it.
            var margin = UiLayout.AtTextSize(24);

            if (runners.Count == 0)
            {
                TextRenderer.DrawText(g,
                    "This race was recorded before lap-by-lap times were kept, so there is nothing to play back.",
                    AppTheme.BodyFont,
                    new Rectangle(margin, margin, Math.Max(UiLayout.AtTextSize(100), Width - margin * 2), UiLayout.AtTextSize(60)),
                    AppTheme.TextMuted,
                    TextFormatFlags.WordBreak);

                return;
            }

            var now = _owner._positionMs;

            var order = runners
                .Select((runner, index) => (runner, index, progress: runner.ProgressAt(now)))
                .OrderByDescending(x => x.progress)

                // A stable tiebreak, or drivers who have not started yet
                // shuffle between frames for no reason.
                .ThenBy(x => x.runner.Finished == 0 ? int.MaxValue : x.runner.Finished)
                .ThenBy(x => x.index)
                .ToList();

            // The heading's line, and the lanes' least and most height,
            // shared out of whatever height the window gives, as before.
            //
            // The least gives way to the room there is, down to the 26px it
            // is at the normal text size: the window keeps its size, and at
            // 200% sixteen drivers at the full least would lose their last
            // lanes off the foot of a window that held them all at 100%.
            // Below that the lanes run off the foot as they always did. At
            // the normal text size this is exactly the 26 it was.
            var top = UiLayout.AtTextSize(56);
            var headingTop = UiLayout.AtTextSize(18);
            var room = (Height - top - margin) / Math.Max(1, order.Count);

            // Never shorter than a line of the names' face and a pixel: at
            // 200% the lanes gave way to the room down to 40px, the line
            // alone is about 34, and each name ran into the lane under it.
            var textLine = Pieces.LineHeight(AppTheme.BodyFont);
            var leastLane = Math.Max(
                Math.Min(UiLayout.AtTextSize(26), Math.Max(room, UiLayout.AtDpi(26))),
                textLine + UiLayout.AtDpi(1));
            var laneHeight = Math.Max(leastLane, Math.Min(UiLayout.AtTextSize(46), room));

            // Across a lane: the position, the colour dot, the name, the bar
            // and the gap after it, where they were at the normal text size.
            // The gap between two lanes gives way before the line of text
            // does, so a lane squeezed for room still holds its name whole.
            var laneGap = Math.Max(UiLayout.AtDpi(1), Math.Min(UiLayout.AtTextSize(4), laneHeight - textLine));
            var stripeRadius = UiLayout.AtTextSize(6);
            var placeLeft = UiLayout.AtTextSize(10);
            var dotLeft = UiLayout.AtTextSize(52);
            var dotSize = UiLayout.AtTextSize(12);
            var nameLeft = UiLayout.AtTextSize(72);
            var nameRoom = UiLayout.AtTextSize(8);
            var barStart = UiLayout.AtTextSize(260);
            var barEndRoom = UiLayout.AtTextSize(90);
            var barLeast = UiLayout.AtTextSize(40);
            var trackHeight = UiLayout.AtTextSize(5);
            var markerSize = UiLayout.AtTextSize(11);
            var gapFromRight = UiLayout.AtTextSize(82);

            // Everything in a lane is centred up and down on its stripe - the
            // words, the dot, the track and the car on it. They were each a
            // fixed drop from the lane's top, made for the 26-30px lanes of
            // the normal text size: in a taller lane the dots and the track
            // sat above the words, and at 200% the words hung below their
            // stripe (layout pictures, 30 Sep).
            const TextFormatFlags InLane = TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

            var leader = order[0].progress;

            TextRenderer.DrawText(g, "Running order", AppTheme.BoldFont,
                new Point(margin, headingTop), AppTheme.TextDark);

            TextRenderer.DrawText(g,
                $"Lap {Math.Max(1, (int)Math.Floor(leader))}",
                AppTheme.BoldFont,
                new Point(Math.Max(UiLayout.AtTextSize(120), Width - UiLayout.AtTextSize(140)), headingTop),
                AppTheme.Primary);

            for (var place = 0; place < order.Count; place++)
            {
                var (runner, _, progress) = order[place];
                var y = top + place * laneHeight;

                var row = new Rectangle(margin, y, Math.Max(UiLayout.AtTextSize(80), Width - margin * 2), laneHeight - laneGap);
                var middle = row.Top + row.Height / 2f;

                if (place % 2 == 1)
                {
                    using var stripe = new SolidBrush(AppTheme.RowAlt);
                    using var path = AppTheme.RoundedRect(row, stripeRadius);
                    g.FillPath(stripe, path);
                }

                // Position, name, then a bar showing how far behind the
                // leader they are - which is the part that makes a gap
                // opening or closing visible while it happens.
                TextRenderer.DrawText(g, $"P{place + 1}", AppTheme.BoldFont,
                    new Rectangle(row.Left + placeLeft, row.Top, Math.Max(0, dotLeft - placeLeft), row.Height),
                    AppTheme.TextMuted, InLane);

                using (var dot = new SolidBrush(runner.Colour))
                {
                    g.FillEllipse(dot, row.Left + dotLeft, middle - dotSize / 2f, dotSize, dotSize);
                }

                var barLeft = row.Left + barStart;

                // The name up to the bar, and "..." where a long one would
                // have run on under the bar and its car.
                TextRenderer.DrawText(g, runner.Name, AppTheme.BodyFont,
                    new Rectangle(row.Left + nameLeft, row.Top, Math.Max(0, barLeft - nameRoom - (row.Left + nameLeft)), row.Height),
                    AppTheme.TextDark, InLane);

                var behind = leader - progress;

                var barWidth = Math.Max(barLeast, row.Right - barLeft - barEndRoom);

                // A lap down is the full width of the bar. Beyond that
                // everyone is simply pinned at the end - the difference
                // between two laps down and three is not what this is for.
                var fraction = Math.Clamp(behind, 0, 1);

                using (var trackBrush = new SolidBrush(AppTheme.Border))
                {
                    g.FillRectangle(trackBrush, barLeft, middle - trackHeight / 2f, barWidth, trackHeight);
                }

                using (var fill = new SolidBrush(runner.Colour))
                {
                    var marker = barLeft + (int)(barWidth * fraction);
                    g.FillEllipse(fill, marker - markerSize / 2f, middle - markerSize / 2f, markerSize, markerSize);
                }

                // "level" rather than "-0.00 lap" for a car no distance
                // behind the leader - everyone on the grid at the start.
                var gapText = place == 0
                    ? "leader"
                    : behind >= 1
                        ? $"{(int)behind} lap{((int)behind == 1 ? string.Empty : "s")}"
                        : behind < 0.005
                            ? "level"
                            : $"-{behind:0.00} lap";

                TextRenderer.DrawText(g, gapText, AppTheme.SmallFont,
                    new Rectangle(row.Right - gapFromRight, row.Top, gapFromRight, row.Height),
                    AppTheme.TextMuted, InLane);
            }
        }
    }
}
