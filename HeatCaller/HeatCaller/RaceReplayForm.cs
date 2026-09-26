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
/// </summary>
public sealed class RaceReplayForm : Form
{
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
        /// How far round the current lap they are, 0 to 1.
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

            if (laps >= CrossingsMs.Count)
            {
                return 1;
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
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 480);
        ClientSize = new Size(980, 640);

        _canvas = new ReplayCanvas(this) { Dock = DockStyle.Fill };

        BuildTransport();

        Controls.Add(_canvas);
        Controls.Add(BuildTransportBar());

        _timer.Tick += (_, _) => Advance();

        AppTheme.ApplyRecursively(this);

        // Styled after the theme pass, which would otherwise repaint it as
        // an ordinary secondary button.
        AppTheme.StyleButton(_btnPlay, AppTheme.ButtonKind.Primary);
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
        _btnPlay.Size = new Size(UiLayout.ButtonWidth, UiLayout.ButtonHeight);
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

        _cmbSpeed.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSpeed.Items.AddRange(new object[] { "1x", "2x", "4x", "8x", "16x" });
        _cmbSpeed.SelectedIndex = 2;
        _cmbSpeed.Width = 88;
        _cmbSpeed.SelectedIndexChanged += (_, _) =>
            _speed = double.Parse(((string)_cmbSpeed.SelectedItem!).TrimEnd('x'));

        _lblClock.AutoSize = true;
        _lblClock.Font = AppTheme.BoldFont;

        UpdateClock();
    }

    private Control BuildTransportBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = UiLayout.ButtonHeight + 24,

            // Sized to the form before anything is placed on it. A docked
            // panel is still its default 200 wide until the form lays it
            // out, and the scrub bar's right margin is worked out from this
            // width - anchoring it against 200 would leave it a stub for
            // the life of the window.
            Width = ClientSize.Width,
        };

        _btnPlay.Location = new Point(12, 12);
        _cmbSpeed.Location = new Point(_btnPlay.Right + UiLayout.ButtonGap, 14);
        _lblClock.Location = new Point(_cmbSpeed.Right + UiLayout.ButtonGap, 20);

        // The scrub bar takes whatever is left, and keeps taking it as the
        // window changes size.
        _scrub.Location = new Point(_lblClock.Left + Math.Max(160, _lblClock.PreferredWidth + 24), 12);
        _scrub.Width = Math.Max(120, bar.Width - _scrub.Left - 12);
        _scrub.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        bar.Controls.AddRange(new Control[] { _btnPlay, _cmbSpeed, _lblClock, _scrub });

        return bar;
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

    private void UpdateClock()
    {
        var span = TimeSpan.FromMilliseconds(_positionMs);
        var total = TimeSpan.FromMilliseconds(_raceLengthMs);

        _lblClock.Text = $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds / 100}"
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

            if (runners.Count == 0)
            {
                TextRenderer.DrawText(g,
                    "This race was recorded before lap-by-lap times were kept, so there is nothing to play back.",
                    AppTheme.BodyFont,
                    new Rectangle(24, 24, Math.Max(100, Width - 48), 60),
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

            const int top = 56;
            var laneHeight = Math.Max(26, Math.Min(46, (Height - top - 24) / Math.Max(1, order.Count)));

            var leader = order[0].progress;

            TextRenderer.DrawText(g, "Running order", AppTheme.BoldFont,
                new Point(24, 18), AppTheme.TextDark);

            TextRenderer.DrawText(g,
                $"Lap {Math.Max(1, (int)Math.Floor(leader))}",
                AppTheme.BoldFont, new Point(Math.Max(120, Width - 140), 18), AppTheme.Primary);

            for (var place = 0; place < order.Count; place++)
            {
                var (runner, _, progress) = order[place];
                var y = top + place * laneHeight;

                var row = new Rectangle(24, y, Math.Max(80, Width - 48), laneHeight - 4);

                if (place % 2 == 1)
                {
                    using var stripe = new SolidBrush(AppTheme.RowAlt);
                    using var path = AppTheme.RoundedRect(row, 6);
                    g.FillPath(stripe, path);
                }

                // Position, name, then a bar showing how far behind the
                // leader they are - which is the part that makes a gap
                // opening or closing visible while it happens.
                TextRenderer.DrawText(g, $"P{place + 1}", AppTheme.BoldFont,
                    new Point(row.Left + 10, y + 6), AppTheme.TextMuted);

                using (var dot = new SolidBrush(runner.Colour))
                {
                    g.FillEllipse(dot, row.Left + 52, y + 8, 12, 12);
                }

                TextRenderer.DrawText(g, runner.Name, AppTheme.BodyFont,
                    new Point(row.Left + 72, y + 6), AppTheme.TextDark);

                var behind = leader - progress;

                var barLeft = row.Left + 260;
                var barWidth = Math.Max(40, row.Right - barLeft - 90);

                // A lap down is the full width of the bar. Beyond that
                // everyone is simply pinned at the end - the difference
                // between two laps down and three is not what this is for.
                var fraction = Math.Clamp(behind, 0, 1);

                using (var trackBrush = new SolidBrush(AppTheme.Border))
                {
                    g.FillRectangle(trackBrush, barLeft, y + laneHeight / 2 - 3, barWidth, 5);
                }

                using (var fill = new SolidBrush(runner.Colour))
                {
                    var marker = barLeft + (int)(barWidth * fraction);
                    g.FillEllipse(fill, marker - 5, y + laneHeight / 2 - 8, 11, 11);
                }

                var gapText = place == 0
                    ? "leader"
                    : behind >= 1
                        ? $"{(int)behind} lap{((int)behind == 1 ? string.Empty : "s")}"
                        : $"-{behind:0.00} lap";

                TextRenderer.DrawText(g, gapText, AppTheme.SmallFont,
                    new Point(row.Right - 82, y + 8), AppTheme.TextMuted);
            }
        }
    }
}
