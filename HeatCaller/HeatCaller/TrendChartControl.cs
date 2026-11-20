namespace HeatCaller;

/// <summary>
/// Plots a driver's best lap per session over time, so improvement (or a
/// plateau, or a bad stretch) is visible across weeks and months.
///
/// Distinct from the Lap Times chart, which shows pace *within* a single
/// race. This is the long view: one point per session, x-axis is calendar
/// time rather than lap number.
///
/// Filtered by track, because comparing a fast layout against a slow one
/// produces a sawtooth that hides any real trend - progress only means
/// something against the same piece of track.
///
/// 13 Nov 2026, the Race Engineer rework (RaceCoach): the page now opens with
/// what the points add up to - still improving, and how fast now against
/// earlier - draws the curve they are following behind them, says where on
/// that curve the driver is, and ends with one thing to try. The reading is
/// RaceCoach.ReadTrend, kept out of here so it can be tested.
/// </summary>
public sealed class TrendChartControl : UserControl
{
    /// <summary>The track layouts, for a layout's length - the car lengths beside each gap (RaceCoach). Null says no car lengths.</summary>
    public TrackLayoutStore? Layouts { get; set; }

    /// <summary>The driver profiles, so the track record leaves out rentals and excluded guests as the Records page does (RaceCoach.RecordLaps). Null leaves out ghosts only.</summary>
    public ProfileStore? Profiles { get; set; }

    private readonly SessionHistoryStore _history;
    private readonly AppSettings _settings;

    private readonly ComboBox _cmbDriver = new();
    private readonly ComboBox _cmbTrack = new();
    private readonly CheckBox _chkIncludePractice = new ThemedCheckBox();
    private readonly Panel _chartPanel = new();
    private readonly Label _lblSummary = new();
    private readonly Label _lblWhere = new();
    private readonly Label _lblRecord = new();
    private readonly DrillNote _drill = new();
    private Label _headline = new();

    /// <summary>The side card's three stages: each one's pill and the line under it.</summary>
    private readonly List<(RaceCoach.CurveStage Stage, StagePill Pill, Label Note)> _stageParts = new();

    /// <summary>The stage lit in the side card, or null for none.</summary>
    private RaceCoach.CurveStage? _stage;

    /// <summary>The reading of the points now showing - its curve is drawn behind them. Null when there is no trend to read (all tracks, too few).</summary>
    private RaceCoach.TrendReading? _reading;

    public TrendChartControl(SessionHistoryStore history, AppSettings settings)
    {
        _history = history;
        _settings = settings;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise
        // take. No MinimumSize: the 900x560 floor was honoured over the size
        // the page was given. A short window scrolls the page instead
        // (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The pickers' width at the normal text size - never narrower, and as wide as their longest choice.</summary>
    private const int PickerLeastWidth = 200;

    /// <summary>The least the chart gets at the normal text size - below that the page scrolls.</summary>
    private const int ChartLeastHeight = 240;

    /// <summary>The side card's width at the normal text size - never narrower.</summary>
    private const int SideLeastWidth = 280;

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. Forgotten when the lists are refilled.</summary>
    private (Font Font, int Count, int Width)? _driverWidth;
    private (Font Font, int Count, int Width)? _trackWidth;

    /// <summary>The pickers' line - held so refilled lists can re-measure it.</summary>
    private FieldRow? _pickRow;

    /// <summary>The headline, for the tests.</summary>
    internal string Headline => _headline.Text;

    /// <summary>The "Try this" advice, or empty when there is none - for the tests.</summary>
    internal string Advice => _drill.Visible ? _drill.Advice : string.Empty;

    /// <summary>The stage lit on the curve, for the tests.</summary>
    internal RaceCoach.CurveStage? Stage => _stage;

    /// <summary>The gap to the track record, as the side card says it - for the tests.</summary>
    internal string RecordLine => _lblRecord.Text;

    /// <summary>Picks a driver and a track by name, as a club member would - for the tests and pictures.</summary>
    internal void Pick(string driver, string? track)
    {
        var d = _cmbDriver.Items.IndexOf(driver);
        _cmbDriver.SelectedIndex = d >= 0 ? d : 0;

        var t = track is null ? 0 : _cmbTrack.Items.IndexOf(track);
        _cmbTrack.SelectedIndex = t >= 0 ? t : 0;
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its tab's bold face - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The pickers, the headline, then the chart in its card beside "Where
    /// you are on the curve", then one thing to try. On the layout pieces
    /// (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be an intro and one card - pickers, a line of figures, the
    /// chart and the export button - and before that was placed by hand over
    /// a 900x560 floor.
    /// </summary>
    private void BuildLayout()
    {
        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriver.SelectedIndexChanged += (_, _) => Redraw();

        _cmbTrack.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrack.SelectedIndexChanged += (_, _) => Redraw();

        _chkIncludePractice.Text = "Include practice sessions";
        _chkIncludePractice.AutoSize = true;
        _chkIncludePractice.Checked = true;
        _chkIncludePractice.CheckedChanged += (_, _) => Redraw();

        var pickRow = new FieldRow();

        pickRow.Add(new Label { Text = "Driver:" }, _cmbDriver, () => PickerWidth(_cmbDriver, ref _driverWidth));
        pickRow.Add(new Label { Text = "Track:" }, _cmbTrack, () => PickerWidth(_cmbTrack, ref _trackWidth));
        pickRow.Add(null, _chkIncludePractice);
        _pickRow = pickRow;

        _headline = CoachPieces.Headline(_textSize);

        // The figures the headline rests on, muted, over the chart -
        // wrapping rather than cut short.
        _lblSummary.AutoSize = true;
        _lblSummary.ForeColor = AppTheme.TextMuted;

        _chartPanel.BackColor = AppTheme.Surface;
        _chartPanel.Paint += (_, e) => DrawChart(e.Graphics, _chartPanel.ClientRectangle, ChartPalette.Screen());

        // It draws its axes and labels to its size, so a resize is a
        // repaint - and buffered, so that repaint does not flicker.
        AppTheme.EnableDoubleBuffering(_chartPanel);
        _chartPanel.Resize += (_, _) => _chartPanel.Invalidate();

        var btnExport = new Button { Text = "Save Chart as Image..." };
        btnExport.Click += (_, _) => ExportChart();

        var trend = new Section("Trend");

        trend.Add(_lblSummary);
        trend.AddFill(_chartPanel, () => UiLayout.AtTextSize(ChartLeastHeight));
        trend.Add(new ButtonBar().With(btnExport), gapBelow: 0);

        var curve = new Section("Where you are on the curve");

        _lblWhere.AutoSize = true;
        _lblWhere.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblWhere, () => AppTheme.SmallFont);
        curve.Add(_lblWhere);

        foreach (var stage in Stages)
        {
            var pill = new StagePill
            {
                Text = stage.ToString(),
                Kind = stage switch
                {
                    RaceCoach.CurveStage.Learning => ZoneKind.Good,
                    RaceCoach.CurveStage.Refining => ZoneKind.Middling,
                    _ => ZoneKind.Bad,
                },
                ForeColor = AppTheme.TextDark,
            };

            _textSize.Wear(pill, () => AppTheme.BoldFont);

            var note = new Label { AutoSize = true, UseMnemonic = false };

            curve.Add(pill, stretch: false, gapBelow: UiLayout.HintGap);
            curve.Add(note);

            _stageParts.Add((stage, pill, note));
        }

        _lblRecord.AutoSize = true;
        _lblRecord.ForeColor = AppTheme.TextDark;
        curve.Add(_lblRecord, gapBelow: 0);

        // The notes' faces depend on which stage is lit, so they are put on
        // again at a new text size along with everything else.
        _textSize.Keep(() => ShowStage(_stage));

        var band = new Beside(() => Math.Max(UiLayout.AtTextSize(SideLeastWidth), curve.CaptionWidth + Section.Inset.Horizontal));

        band.AddFill(trend, trend.LeastHeight);
        band.AddSideCard(curve, gapBelow: 0);

        var page = new FillPage();

        page.Add(pickRow, gapBelow: UiLayout.RowGap);
        page.Add(_headline, gapBelow: UiLayout.RowGap);
        page.AddFill(band, band.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(_drill, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>The stages, in the order a driver goes through them.</summary>
    private static readonly RaceCoach.CurveStage[] Stages =
    {
        RaceCoach.CurveStage.Learning, RaceCoach.CurveStage.Refining, RaceCoach.CurveStage.Plateau,
    };

    /// <summary>Lights the stage the driver is in and fades the others. Null lights none.</summary>
    private void ShowStage(RaceCoach.CurveStage? stage)
    {
        _stage = stage;

        foreach (var (each, pill, note) in _stageParts)
        {
            var lit = each == stage;

            pill.Lit = lit;
            pill.Invalidate();

            var text = lit ? "You are here. " + RaceCoach.StageNote(each) : RaceCoach.StageNote(each);
            var font = lit ? AppTheme.BoldFont : AppTheme.SmallFont;

            if (note.Text != text)
            {
                note.Text = text;
            }

            if (!Equals(note.Font, font))
            {
                note.Font = font;
            }

            note.ForeColor = lit ? AppTheme.TextDark : AppTheme.TextMuted;
        }
    }

    /// <summary>A picker: as wide as its longest choice, and never under its width at the normal text size.</summary>
    private static int PickerWidth(ComboBox picker, ref (Font Font, int Count, int Width)? kept) =>
        Math.Max(UiLayout.AtTextSize(PickerLeastWidth), Pieces.PickerWidth(picker, ref kept));

    private sealed record TrendPoint(DateTime When, long BestLapMs, string Label);

    /// <summary>Call when this page becomes visible - new sessions can be recorded while it's not active.</summary>
    public void RefreshAll()
    {
        var previousDriver = _cmbDriver.SelectedItem as string;
        var previousTrack = _cmbTrack.SelectedItem as string;

        // Saved demo races are left out throughout: their lap times are
        // invented, so they would draw a trend nobody actually drove, and
        // the names in them are made up.
        //
        // Ghost cars are left out too. Sessions saved before ghosts were
        // stripped from results still carry them, and a ghost's lap times
        // are generated - a trend for one is a line somebody typed in, not
        // anyone's improvement. They are told apart by transponder, the
        // same test every other page uses, because a ghost can be given
        // any name - including a real driver's.
        var drivers = _history.All
            .Where(s => !s.IsNotScored())
            .SelectMany(s => s.Results)
            .Where(r => !GhostService.IsGhost(r.Transponder))
            .Select(r => r.DriverName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        _cmbDriver.Items.Clear();
        _cmbDriver.Items.Add("(select a driver)");
        foreach (var d in drivers)
        {
            _cmbDriver.Items.Add(d);
        }

        var tracks = _history.All
            .Where(s => !s.IsNotScored())
            .Select(s => s.TrackLayoutName)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();

        _cmbTrack.Items.Clear();
        _cmbTrack.Items.Add("(all tracks)");
        foreach (var t in tracks)
        {
            _cmbTrack.Items.Add(t);
        }

        // The longest choice may have come or gone: measured again, and the
        // line with it.
        _driverWidth = null;
        _trackWidth = null;
        _pickRow?.PerformLayout();

        Restore(_cmbDriver, previousDriver);
        Restore(_cmbTrack, previousTrack);

        // Restoring the same choices raises no change, and the sessions
        // under them may be new.
        Redraw();
    }

    private static void Restore(ComboBox combo, string? previous)
    {
        var idx = previous is not null ? combo.Items.IndexOf(previous) : -1;
        combo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private string? Driver => _cmbDriver.SelectedIndex > 0 ? _cmbDriver.SelectedItem as string : null;

    private string? Track => _cmbTrack.SelectedIndex > 0 ? _cmbTrack.SelectedItem as string : null;

    private List<TrendPoint> BuildPoints()
    {
        var driver = Driver;
        if (driver is null)
        {
            return new List<TrendPoint>();
        }

        var trackFilter = Track;

        var points = new List<TrendPoint>();

        // Oldest first so the line reads left-to-right chronologically.
        // Sorted by date rather than taken in the history's order, which is
        // the order sessions were saved in - an imported night lands at the
        // end of it whatever its date.
        foreach (var session in _history.All.OrderBy(s => s.StartedAtUtc))
        {
            if (session.IsNotScored())
            {
                continue;
            }

            if (!_chkIncludePractice.Checked && session.SessionType == RaceSessionType.Practice)
            {
                continue;
            }

            if (trackFilter is not null && !string.Equals(session.TrackLayoutName, trackFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Ghosts skipped here as well as in the list: a ghost named
            // after the driver would otherwise be picked up as them.
            var result = session.Results.FirstOrDefault(r =>
                !GhostService.IsGhost(r.Transponder)
                && string.Equals(r.DriverName, driver, StringComparison.OrdinalIgnoreCase));

            if (result is null || result.FastLapMs <= 0)
            {
                continue;
            }

            points.Add(new TrendPoint(
                session.StartedAtUtc.ToLocalTime(),
                result.FastLapMs,
                DayLabel(session.StartedAtUtc.ToLocalTime(), System.Globalization.CultureInfo.CurrentCulture)));
        }

        return points;
    }

    /// <summary>
    /// A session's day under its point, day and month in the order this PC
    /// writes dates: a fixed month/day read as the wrong date wherever the
    /// day comes first - 3 November as "11/3", which is 11 March there.
    /// </summary>
    internal static string DayLabel(DateTime local, System.Globalization.CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var day = pattern.IndexOf('d');
        var month = pattern.IndexOf('M');

        return local.ToString(day >= 0 && month >= 0 && day < month ? "d/M" : "M/d", culture);
    }

    private void Redraw()
    {
        var points = BuildPoints();
        var track = Track;

        _reading = null;

        if (points.Count == 0)
        {
            // Only the "(select a driver)" placeholder in the list means
            // there is nobody to pick at all - say why, rather than asking
            // for a choice that can't be made.
            _headline.Text = _cmbDriver.SelectedIndex > 0
                ? "No sessions match those filters."
                : _cmbDriver.Items.Count <= 1
                    ? "No saved races for real drivers yet. Demo races and ghost cars aren't counted."
                    : "Pick a driver to see how their lap times are moving.";
            _lblSummary.Text = string.Empty;
            _lblWhere.Text = "Pick a driver and a track to see where they are.";
            _lblRecord.Text = string.Empty;
            ShowStage(null);
            _drill.Say(null);
            _chartPanel.Invalidate();
            return;
        }

        _lblSummary.Text = Facts(points);

        if (track is null)
        {
            // Across tracks a quick layout and a slow one sit side by side,
            // and the line between them is the layouts, not the driver.
            _headline.Text = "Across all tracks the line mixes quick layouts with slow ones, so it's no real trend. Pick a track to see how you're improving.";
            _lblWhere.Text = "Pick one track to see this.";
            _lblRecord.Text = string.Empty;
            ShowStage(null);
            _drill.Say("Pick the track you race most, above, and read your trend on that.");
            _chartPanel.Invalidate();
            return;
        }

        var reading = RaceCoach.ReadTrend(points.Select(p => new RaceCoach.TrendSample(p.When, p.BestLapMs)));

        _reading = reading;
        _headline.Text = RaceCoach.TrendHeadline(reading);
        _lblWhere.Text = reading.Stage is null
            ? $"It takes {RaceCoach.TrendLeastSessions} sessions over more than a week to place you."
            : $"From your last {Plural.Of(reading.RecentSessions, "session")} here.";
        ShowStage(reading.Stage);
        _lblRecord.Text = RecordGap(track, Driver!, points.Min(p => p.BestLapMs));
        _drill.Say(RaceCoach.TrendDrill(reading));

        _chartPanel.Invalidate();
    }

    /// <summary>The figures under the headline: sessions, best, and the latest against the first.</summary>
    private static string Facts(IReadOnlyList<TrendPoint> points)
    {
        var best = points.Min(p => p.BestLapMs);
        var delta = points[^1].BestLapMs - points[0].BestLapMs;

        var direction = points.Count < 2
            ? "only one session so far"
            : delta < 0
                ? $"the latest is {Math.Abs(delta) / 1000.0:0.000} s quicker than the first"
                : delta > 0
                    ? $"the latest is {delta / 1000.0:0.000} s slower than the first"
                    : "the latest is level with the first";

        return $"{Plural.Of(points.Count, "session")}. Best ever {PaceAnalysis.Time(best)}, and {direction}.";
    }

    /// <summary>
    /// The gap from the driver's best on a track to its record, in seconds
    /// and - when the layout's length is set - in car lengths, which a
    /// driver can picture where a tenth means nothing.
    ///
    /// The record is the quickest lap any real driver has set there in a
    /// scored session, practice included, as the points are. "Real" is
    /// RaceCoach.RecordLaps' rule - no ghosts, rentals or excluded guests -
    /// so this names the same holder as the Records page and the badge.
    ///
    /// Whether the driver holds it is read from those record laps, not from
    /// their best on the chart: a rental or excluded guest can be quicker
    /// than the record without holding it, and with practice unticked the
    /// chart's best can miss the practice lap the driver set the record with.
    /// </summary>
    private string RecordGap(string track, string driver, long driverBest) =>
        RecordText(_history.All, track, driver, driverBest, Profiles, Layouts);

    internal static string RecordText(
        IEnumerable<SessionRecord> sessions, string track, string driver, long driverBest, ProfileStore? profiles, TrackLayoutStore? layouts)
    {
        var laps = RaceCoach.RecordLaps(
                sessions.Where(s => !s.IsNotScored() && string.Equals(s.TrackLayoutName, track, StringComparison.OrdinalIgnoreCase)),
                profiles)
            .Select(x => x.Result)
            .ToList();

        var holder = laps.FirstOrDefault();

        if (holder is null)
        {
            return string.Empty;
        }

        var record = holder.FastLapMs;
        var own = laps.FirstOrDefault(r => string.Equals(r.DriverName, driver, StringComparison.OrdinalIgnoreCase));

        var who = string.IsNullOrWhiteSpace(holder.DriverName) ? string.Empty : $" by {holder.DriverName}";

        // WHY (15 Nov 2026, bug hunt 21): <= told a driver who EQUALLED the
        // record that they held it. Only the driver it is credited to holds
        // it; an equal lap ties whoever set it first - as
        // PersonalBestsControl.GapToRecord already says.
        if (own is not null && own.FastLapMs <= record)
        {
            return string.Equals(holder.DriverName, driver, StringComparison.OrdinalIgnoreCase)
                ? $"You hold the track record here: {PaceAnalysis.Time(record)}."
                : $"You equal the track record here: {PaceAnalysis.Time(record)}, set first{who}.";
        }

        if (driverBest <= record)
        {
            return $"Track record {PaceAnalysis.Time(record)}{who}. Your {PaceAnalysis.Time(driverBest)} doesn't count towards it: it was on a rental transponder, or you're left out of records.";
        }

        var gap = driverBest - record;
        var lengths = RaceCoach.SayLengths(gap, record, RaceCoach.TrackCars(layouts, track));

        return lengths is null
            ? $"Track record {PaceAnalysis.Time(record)}{who}. Your best is {gap / 1000.0:0.000} s off it."
            : $"Track record {PaceAnalysis.Time(record)}{who}. Your best is {gap / 1000.0:0.000} s off it, about {lengths} a lap.";
    }

    private void DrawChart(Graphics g, Rectangle bounds, ChartPalette palette)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(palette.Background))
        {
            g.FillRectangle(bg, bounds);
        }

        var points = BuildPoints();
        if (points.Count == 0)
        {
            using var msgBrush = new SolidBrush(palette.Muted);
            const string msg = "No data for these filters.";
            var size = g.MeasureString(msg, AppTheme.BodyFont);

            // Lifted by about half a line of the body face, at the text size
            // in use, so it stays centred as the face grows.
            g.DrawString(msg, AppTheme.BodyFont, msgBrush, bounds.Left + (bounds.Width - size.Width) / 2, bounds.Top + bounds.Height / 2f - UiLayout.AtTextSize(10));
            return;
        }

        // The curve, when there is a trend to read on this track (see
        // Redraw): its value at each point, and between them.
        var fit = Track is null ? null : _reading?.Fit;

        // The room round the plot holds the time labels down the left and
        // the dates under it, all in the small face - so it grows with the
        // text size, or at 200% the labels run into the plot and off the
        // bottom. Worked out on every paint, so a new text size is picked up
        // by the repaint it brings. A line more under the dates for the
        // key, when the curve is drawn.
        var keyHeight = fit is null ? 0 : UiLayout.AtTextSize(22);
        var padLeft = UiLayout.AtTextSize(70);
        var padRight = UiLayout.AtTextSize(20);
        var padTop = UiLayout.AtTextSize(20);
        var padBottom = UiLayout.AtTextSize(44) + keyHeight;
        var plot = new Rectangle(
            bounds.Left + padLeft, bounds.Top + padTop,
            Math.Max(10, bounds.Width - padLeft - padRight),
            Math.Max(10, bounds.Height - padTop - padBottom));

        var fitted = fit is null ? null : points.Select(p => fit.At(p.When)).ToList();

        var minMs = (double)points.Min(p => p.BestLapMs);
        var maxMs = (double)points.Max(p => p.BestLapMs);

        if (fitted is not null)
        {
            minMs = Math.Min(minMs, fitted.Min());
            maxMs = Math.Max(maxMs, fitted.Max());
        }

        var span = Math.Max(1, maxMs - minMs);
        var low = minMs - span * 0.12;
        var high = maxMs + span * 0.12;
        var range = Math.Max(1, high - low);
        var bestMs = points.Min(p => p.BestLapMs);

        using var gridPen = new Pen(palette.Grid);
        using var axisPen = new Pen(palette.Axis);
        using var labelBrush = new SolidBrush(palette.Muted);

        // The labels' offsets from their lines are fractions of a line of
        // the small face they are drawn in, so they follow the text size
        // with it - a time label lifted a fixed 7px sits below its gridline
        // once the face is twice as tall.
        var labelGap = UiLayout.AtTextSize(8);
        var halfLabel = UiLayout.AtTextSize(7);

        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4f;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString($"{(low + range * i / 4.0) / 1000.0:0.00}s", AppTheme.SmallFont, labelBrush, bounds.Left + labelGap, y - halfLabel);
        }

        g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        var xFor = new Func<int, float>(i =>
            plot.Left + (points.Count == 1 ? plot.Width / 2f : plot.Width * i / (float)(points.Count - 1)));
        var yFor = new Func<double, float>(ms =>
            (float)(plot.Bottom - (ms - low) / range * plot.Height));

        // Horizontal marker at the all-time best, so it's obvious whether a
        // session actually beat it or just came close.
        var bestY = yFor(bestMs);
        using (var bestPen = new Pen(palette.Highlight, 1.4f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
        {
            g.DrawLine(bestPen, plot.Left, bestY, plot.Right, bestY);
        }
        using (var bestBrush = new SolidBrush(palette.Highlight))
        {
            // Its room back from the right edge and up from the line, at the
            // text size in use - the words grow with the small face, and a
            // fixed 76px cut them off at the plot's edge at 200%.
            g.DrawString("all-time best", AppTheme.SmallFont, bestBrush,
                plot.Right - UiLayout.AtTextSize(76), bestY - UiLayout.AtTextSize(16));
        }

        // The curve behind the points, dashed and muted: the shape the
        // points are following, not a thing anyone drove. Points are spaced
        // a session apart, not a day, so between two the curve is worked out
        // at the dates in between and drawn across that gap.
        if (fit is not null && points.Count > 1)
        {
            var curve = new List<PointF>();

            for (var i = 0; i < points.Count - 1; i++)
            {
                const int Steps = 8;

                for (var s = 0; s < Steps; s++)
                {
                    var t = s / (double)Steps;
                    var when = points[i].When + TimeSpan.FromTicks((long)((points[i + 1].When - points[i].When).Ticks * t));

                    curve.Add(new PointF(xFor(i) + (xFor(i + 1) - xFor(i)) * (float)t, yFor(fit.At(when))));
                }
            }

            curve.Add(new PointF(xFor(points.Count - 1), yFor(fit.At(points[^1].When))));

            using var curvePen = new Pen(palette.Muted, 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawLines(curvePen, curve.ToArray());
        }

        var linePoints = points.Select((p, i) => new PointF(xFor(i), yFor(p.BestLapMs))).ToArray();

        using (var linePen = new Pen(palette.Series[0], 2.4f))
        {
            if (linePoints.Length > 1)
            {
                g.DrawLines(linePen, linePoints);
            }
        }

        using var dotBrush = new SolidBrush(palette.Series[0]);
        using var pbBrush = new SolidBrush(palette.Highlight);

        // The dots at the text size in use, in step with the labels round
        // them, so the chart keeps its proportions as the text grows.
        var bestDot = (float)UiLayout.AtTextSize(8f);
        var dot = (float)UiLayout.AtTextSize(5f);

        for (var i = 0; i < linePoints.Length; i++)
        {
            var isBest = points[i].BestLapMs == bestMs;
            var brush = isBest ? pbBrush : dotBrush;
            var size = isBest ? bestDot : dot;
            g.FillEllipse(brush, linePoints[i].X - size / 2, linePoints[i].Y - size / 2, size, size);
        }

        // Thin out date labels so they never overlap on a long history.
        // Centred under their points by about half a date's width, and
        // dropped clear of the axis, at the text size in use.
        var labelStep = Math.Max(1, points.Count / 12);
        var halfDate = UiLayout.AtTextSize(14);
        for (var i = 0; i < points.Count; i += labelStep)
        {
            g.DrawString(points[i].Label, AppTheme.SmallFont, labelBrush, xFor(i) - halfDate, plot.Bottom + labelGap);
        }

        if (fit is not null)
        {
            DrawKey(g, palette, plot.Left, bounds.Bottom - keyHeight);
        }
    }

    /// <summary>The key under the dates: what the solid line is, and what the dashed one is.</summary>
    private static void DrawKey(Graphics g, ChartPalette palette, float left, float top)
    {
        var swatch = UiLayout.AtTextSize(22);
        var gap = UiLayout.AtTextSize(6);
        var middle = top + UiLayout.AtTextSize(8);
        var x = left;

        using var labelBrush = new SolidBrush(palette.Muted);

        void Item(Pen pen, string words)
        {
            g.DrawLine(pen, x, middle, x + swatch, middle);
            x += swatch + gap;
            g.DrawString(words, AppTheme.SmallFont, labelBrush, x, middle - UiLayout.AtTextSize(7));
            x += g.MeasureString(words, AppTheme.SmallFont).Width + gap * 3;
        }

        using (var solid = new Pen(palette.Series[0], 2.4f))
        {
            Item(solid, "best lap each session");
        }

        using var dashed = new Pen(palette.Muted, 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        Item(dashed, "the curve it is following");
    }

    private void ExportChart()
    {
        AppUsageTracker.Note("chart_exported");
        if (BuildPoints().Count == 0)
        {
            AppDialog.Show(this, "Nothing to export - select a driver with session history first.", "Nothing to export",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var optionsForm = new ChartExportForm(_settings, DrawChart);
        if (optionsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Only spaces used to be dealt with here, so a driver entered as
        // "AC/DC" or "Bob: The Builder" got a suggested file name Windows
        // won't take, from a dialog that sits outside the try below.
        var driver = PlainFileName.SafePart(_cmbDriver.SelectedItem as string, "driver").Replace(' ', '-');

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"trend-{driver}-{DateTime.Now:yyyy-MM-dd}.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var size = optionsForm.Options.ImageSize;
            var palette = ChartPalette.For(optionsForm.Options.StyleKey);

            using var bitmap = new Bitmap(size.Width, size.Height);

            // A new bitmap takes the screen's DPI, and in a DPI-aware run
            // (144 DPI at 150%) that would draw the chart's point-size text
            // half as big again inside the same pixels. At 96 DPI, which is
            // what a DPI-unaware run always sees, this changes nothing.
            bitmap.SetResolution(96, 96);

            using (var g = Graphics.FromImage(bitmap))
            {
                DrawChart(g, new Rectangle(0, 0, size.Width, size.Height), palette);
            }
            bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);

            AppDialog.Show(this, $"Saved to:\n{dialog.FileName}", "Chart saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save the chart: {ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
