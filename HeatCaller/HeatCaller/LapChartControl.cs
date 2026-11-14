namespace HeatCaller;

/// <summary>
/// Plots lap-by-lap pace for every driver in a chosen session, so you can
/// see consistency, fade, and where time was found or lost - things a
/// single fastest-lap number can't show.
///
/// Drawn with plain GDI+ rather than pulling in a charting library: the
/// requirements here are a handful of polylines with axes, and this keeps
/// the app dependency-free and consistent with how the results card and
/// sidebar icons are already rendered.
///
/// 13 Nov 2026, the Race Engineer rework (RaceCoach): the page reads one
/// driver's race as well as plotting it - the focused driver, picked
/// beside the session. A sentence over the chart says what the race added
/// up to; the chart marks the best lap, each mistake with what it cost, and
/// where a fade starts, over a band for laps at the driver's limit; "Three
/// things in this race" sits beside it, and a "Try this" under it. Other
/// ticked drivers stay on the chart, fainter, to compare against. The
/// consistency table moved under all of that: the figures are evidence
/// now, not the story.
/// </summary>
public sealed class LapChartControl : UserControl
{
    /// <summary>The track layouts, for a layout's length - the car lengths beside each gap (RaceCoach). Null says no car lengths.</summary>
    public TrackLayoutStore? Layouts
    {
        get => _layouts;
        set
        {
            // Set after the page is built (MainForm sets it in the initializer),
            // so the reading is done again with the layout's length to hand.
            _layouts = value;
            RefreshCoach();
            _chartPanel.Invalidate();
        }
    }

    private TrackLayoutStore? _layouts;

    private readonly SessionHistoryStore _history;
    private readonly AppSettings _settings;

    private readonly ComboBox _cmbSession = new();
    private readonly ComboBox _cmbFocus = new();
    private readonly CheckedListBox _clbDrivers = new ThemedCheckedListBox();
    private readonly Panel _chartPanel = new();
    private readonly Label _lblInfo = new();
    private readonly DataGridView _statsTable = new();

    /// <summary>What the focused driver's race added up to - see RaceCoach.LapTimesHeadline.</summary>
    private readonly Label _headline;

    /// <summary>"Try this" under the chart - RaceCoach.Drill for the focused driver.</summary>
    private readonly DrillNote _drill = new();

    /// <summary>The three findings: a bold line and a muted detail each, hidden when there is no finding to show.</summary>
    private readonly List<(Label Line, Label Detail)> _things = new();

    /// <summary>What the findings card says when there is nothing to read.</summary>
    private readonly Label _thingsNone = new() { AutoSize = true, ForeColor = AppTheme.TextMuted };

    /// <summary>The focus picker's width as last measured - see Pieces.PickerWidth. Forgotten when the list is refilled.</summary>
    private (Font Font, int Count, int Width)? _focusWidth;

    /// <summary>Set while the page itself fills or moves the focus picker, so that is not taken for a choice.</summary>
    private bool _settingFocus;

    /// <summary>
    /// Every plotted point with the lap it represents, captured during
    /// paint so hovering can report an exact time. Recomputing the
    /// geometry on mouse-move would risk drifting out of step with what's
    /// actually drawn.
    /// </summary>
    private readonly List<(PointF At, string Driver, int Lap, long Ms, Color Color)> _plotted = new();

    private (string Driver, int Lap, long Ms, Color Color, PointF At)? _hover;

    private SessionRecord? _currentSession;

    public LapChartControl(SessionHistoryStore history, AppSettings settings)
    {
        _history = history;
        _settings = settings;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise
        // take. No MinimumSize: the 900x640 floor was honoured over the size
        // the page was given. A short window scrolls the page instead
        // (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        _headline = CoachPieces.Headline(_textSize);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The session picker's width at the normal text size.</summary>
    private const int SessionPickerWidth = 420;

    /// <summary>The drivers' card's width at the normal text size.</summary>
    private const int DriversWidth = 220;

    /// <summary>The least the chart gets at the normal text size - below that the page scrolls. 200 since 13 Nov 2026 (was 220), to make room for the headline and the "Try this" on a 760px page.</summary>
    private const int ChartLeastHeight = 200;

    /// <summary>Drivers shown in the consistency table without scrolling it - three, now it sits under the reading as evidence.</summary>
    private const int StatsRowsShown = 3;

    /// <summary>The focus picker's least width at the normal text size.</summary>
    private const int FocusPickerWidth = 200;

    /// <summary>The findings card's width at the normal text size: wide enough that a finding's bold line takes two lines at most.</summary>
    private const int ThingsWidth = 290;

    /// <summary>The narrowest the findings card gets, at the normal text size, so the chart keeps more of a narrow window.</summary>
    private const int ThingsLeastWidth = 230;

    /// <summary>
    /// The findings card: about a third of what the drivers leave, between
    /// its least and its full width. At its full width on a narrow window it
    /// left the chart so little that the labels on it had no room.
    /// </summary>
    private int ThingsCardWidth() =>
        Math.Max(
            Math.Clamp((int)((ClientSize.Width - UiLayout.AtTextSize(DriversWidth)) * 0.32f), UiLayout.AtTextSize(ThingsLeastWidth), UiLayout.AtTextSize(ThingsWidth)),
            (_thingsCard?.CaptionWidth ?? 0) + Section.Inset.Horizontal);

    /// <summary>The findings card, for its caption's width: never narrower than that, or "Three things in this race" wraps.</summary>
    private Section? _thingsCard;

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
    /// The intro and the session picker, then the drivers down the left and
    /// beside them the chart - taking the height that is left - over the
    /// consistency table. On the layout pieces (LayoutPieces.cs), so nothing
    /// is placed by hand.
    ///
    /// It used to be placed by hand over a 900x640 floor: the list and the
    /// chart at a fixed 340px, and the export button and the consistency box
    /// at fixed places under them, in faces handed out once.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Lap-by-lap pace for a saved race, read for the driver you focus on. Tick others on the left to compare them.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _cmbSession.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSession.SelectedIndexChanged += (_, _) => LoadSelectedSession();

        // The driver the page reads the race for. Picking one ticks them, so
        // the driver read about is always on the chart.
        _cmbFocus.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbFocus.SelectedIndexChanged += (_, _) =>
        {
            if (_settingFocus)
            {
                return;
            }

            if (_cmbFocus.SelectedIndex >= 0 && _cmbFocus.SelectedIndex < _clbDrivers.Items.Count
                && !_clbDrivers.GetItemChecked(_cmbFocus.SelectedIndex))
            {
                // Ticking it raises ItemCheck, which reads the race again.
                _clbDrivers.SetItemChecked(_cmbFocus.SelectedIndex, true);
                return;
            }

            _chartPanel.Invalidate();
            RefreshCoach();
        };

        // What the session holds, beside the picker - on a line of its own
        // when it no longer fits there (FieldRow).
        _lblInfo.AutoSize = false;
        _lblInfo.AutoEllipsis = true;
        _lblInfo.ForeColor = AppTheme.TextMuted;

        var sessionRow = new FieldRow();

        sessionRow.Add(new Label { Text = "Session:" }, _cmbSession, () => UiLayout.AtTextSize(SessionPickerWidth));
        sessionRow.Add(new Label { Text = "Focus on:" }, _cmbFocus,
            () => Math.Max(UiLayout.AtTextSize(FocusPickerWidth), Pieces.PickerWidth(_cmbFocus, ref _focusWidth)));
        sessionRow.AddLast(null, _lblInfo, () => _lblInfo.GetPreferredSize(Size.Empty).Width);

        // --- The drivers ---
        _clbDrivers.CheckOnClick = true;
        _clbDrivers.IntegralHeight = false;
        _clbDrivers.ItemCheck += (_, _) =>
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            // Later, because ItemCheck is raised before the tick changes.
            BeginInvoke(() => { KeepFocusTicked(); _chartPanel.Invalidate(); RefreshStats(); RefreshCoach(); });
        };

        var drivers = new Section("Drivers");

        drivers.AddFill(_clbDrivers, () => 4 * _clbDrivers.ItemHeight + 6, gapBelow: 0);

        // --- The chart ---
        _chartPanel.BackColor = AppTheme.Surface;

        // The hover readout repaints the chart, so this has to be buffered
        // or moving the mouse across it flickers the whole plot.
        AppTheme.EnableDoubleBuffering(_chartPanel);
        _chartPanel.Paint += ChartPanel_Paint;
        _chartPanel.MouseMove += ChartPanel_MouseMove;
        _chartPanel.MouseLeave += (_, _) =>
        {
            if (_hover is not null)
            {
                _hover = null;
                _chartPanel.Invalidate();
            }
        };

        // It draws its own axes and labels to its size, so a resize is a
        // repaint.
        _chartPanel.Resize += (_, _) => _chartPanel.Invalidate();

        var btnExport = new Button { Text = "Save Chart as Image..." };
        btnExport.Click += (_, _) => ExportChart();

        var chart = new Section("Lap times");

        chart.AddFill(_chartPanel, () => UiLayout.AtTextSize(ChartLeastHeight));
        chart.Add(new ButtonBar().With(btnExport), gapBelow: 0);

        // --- Consistency ---
        _statsTable.ReadOnly = true;
        _statsTable.AllowUserToAddRows = false;
        _statsTable.AllowUserToDeleteRows = false;
        _statsTable.AllowUserToResizeRows = false;
        _statsTable.RowHeadersVisible = false;
        _statsTable.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _statsTable.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _statsTable.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 80 },
            new DataGridViewTextBoxColumn { Name = "best", HeaderText = "Best", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "avg", HeaderText = "Average", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "spread", HeaderText = "Avg-Best", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "consistency", HeaderText = "Std Dev", FillWeight = 50 }
        );

        // Every heading whole, and a few drivers tall, at the text size in
        // use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() =>
        {
            Pieces.KeepHeadingsWhole(_statsTable);

            var height = _statsTable.ColumnHeadersHeight + StatsRowsShown * _statsTable.RowTemplate.Height + 2;

            if (_statsTable.Height != height)
            {
                _statsTable.Height = height;
            }
        });

        var consistency = new Section("Consistency");

        consistency.Add(_statsTable, gapBelow: 0);

        // --- Three things in this race ---
        //
        // Three findings at most, each a bold line over a muted detail, and
        // a muted line in their place when there is nothing to read. Made
        // once and shown or hidden, so the card never rebuilds its rows.
        var things = _thingsCard = new Section("Three things in this race");

        for (var i = 0; i < 3; i++)
        {
            var line = _textSize.Wear(new Label { AutoSize = true, ForeColor = AppTheme.TextDark }, () => AppTheme.BoldFont);
            var detail = new Label { AutoSize = true, ForeColor = AppTheme.TextMuted };

            things.Add(line, gapBelow: 2);
            things.Add(detail, gapBelow: i < 2 ? UiLayout.RowGap : 0);
            _things.Add((line, detail));
        }

        things.Add(_thingsNone, gapBelow: 0);

        // --- The page ---
        //
        // The chart with the findings beside it, as the mock-up has them;
        // the "Try this" and the figures under both. The drivers down the
        // left of all of it, ticked from.
        var reading = new Beside(ThingsCardWidth);

        reading.AddFill(chart, chart.LeastHeight, gapBelow: 0);
        reading.AddSideCard(things, gapBelow: 0);

        var band = new Beside(() => UiLayout.AtTextSize(DriversWidth)) { SideOnLeft = true };

        band.AddFill(reading, reading.LeastHeight, gapBelow: UiLayout.RowGap);
        band.Add(_drill, gapBelow: UiLayout.RowGap);
        band.Add(consistency, gapBelow: 0);
        band.AddSideFill(drivers, drivers.LeastHeight, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(sessionRow, gapBelow: UiLayout.RowGap);
        page.Add(_headline, gapBelow: UiLayout.RowGap);
        page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// Std dev is the headline consistency number: how tightly a driver's
    /// laps cluster. Avg-Best complements it by showing how much time is
    /// being left on the table relative to what they've proven they can
    /// do - a driver can be very consistent but consistently slow, and
    /// these two numbers together tell those apart.
    /// </summary>
    private void RefreshStats()
    {
        _statsTable.Rows.Clear();

        foreach (var driver in CheckedDrivers())
        {
            // Flying laps only, as PaceAnalysis.CountedLaps counts them: the
            // first entry is the run from the green to the first crossing,
            // and a car parked just behind the line made it the "Best" here
            // and dragged the average and spread with it. RaceCoach.Laps
            // since 13 Nov 2026, so a driver credited with the start lap
            // loses its second start slot here too, as the reading above does.
            var laps = RaceCoach.Laps(driver);
            if (laps.Count == 0)
            {
                continue;
            }

            var best = laps.Min();
            var avg = laps.Average();
            var variance = laps.Sum(t => Math.Pow(t - avg, 2)) / laps.Count;
            var stdDev = Math.Sqrt(variance);

            _statsTable.Rows.Add(
                driver.DriverName,
                $"{best / 1000.0:0.000}s",
                $"{avg / 1000.0:0.000}s",
                $"+{(avg - best) / 1000.0:0.000}s",
                $"{stdDev / 1000.0:0.000}s");
        }
    }

    /// <summary>Call whenever this page becomes visible - new sessions can be recorded while it's not active.</summary>
    public void RefreshAll()
    {
        var previousId = (_cmbSession.SelectedItem as SessionComboItem)?.Session.Id;

        _cmbSession.Items.Clear();
        foreach (var session in _history.All)
        {
            _cmbSession.Items.Add(new SessionComboItem(session));
        }

        if (_cmbSession.Items.Count == 0)
        {
            _lblInfo.Text = "No saved sessions yet.";
            _clbDrivers.Items.Clear();
            FillFocus();
            _currentSession = null;
            _chartPanel.Invalidate();
            RefreshCoach();

            // Clearing the driver list raises no ItemCheck, so nothing else
            // would empty the Consistency table - it kept the rows of a
            // session that had just been deleted. RefreshStats with no
            // drivers ticked leaves it empty.
            RefreshStats();
            return;
        }

        var index = 0;
        if (previousId is not null)
        {
            for (var i = 0; i < _cmbSession.Items.Count; i++)
            {
                if (_cmbSession.Items[i] is SessionComboItem item && item.Session.Id == previousId)
                {
                    index = i;
                    break;
                }
            }
        }

        _cmbSession.SelectedIndex = index;
    }

    private void LoadSelectedSession()
    {
        _currentSession = (_cmbSession.SelectedItem as SessionComboItem)?.Session;
        _clbDrivers.Items.Clear();

        if (_currentSession is null)
        {
            FillFocus();
            _chartPanel.Invalidate();

            // As in RefreshAll: the list was just cleared without raising
            // ItemCheck, so the table has to be emptied here or it goes on
            // showing the previous session's drivers.
            RefreshStats();
            RefreshCoach();
            return;
        }

        // Ghost cars left out, by transponder: sessions saved before ghosts
        // were stripped from results still carry them, and their laps are
        // generated rather than driven.
        var withLaps = _currentSession.Results
            .Where(r => r.LapTimesMs.Count > 0 && !GhostService.IsGhost(r.Transponder))
            .ToList();

        foreach (var result in withLaps)
        {
            var i = _clbDrivers.Items.Add(new DriverComboItem(result));
            // Default to the first few checked - showing eight overlapping
            // lines at once by default would be unreadable.
            _clbDrivers.SetItemChecked(i, i < 3);
        }

        _lblInfo.Text = withLaps.Count == 0
            ? _currentSession.Results.Any(r => r.LapTimesMs.Count > 0)
                ? "Only ghost cars ran in this session - there are no real driver laps to chart."
                : "This session has no lap-by-lap data recorded."
            : $"{withLaps.Count} driver{(withLaps.Count == 1 ? "" : "s")} with lap data";

        FillFocus();
        _chartPanel.Invalidate();
        RefreshStats();
        RefreshCoach();
    }

    private List<SessionResultEntry> CheckedDrivers() =>
        _clbDrivers.CheckedItems.Cast<DriverComboItem>().Select(i => i.Result).ToList();

    /// <summary>
    /// The driver the page reads the race for: the one picked to focus on,
    /// while they are ticked, or else the first ticked. Null with no one
    /// ticked.
    /// </summary>
    private SessionResultEntry? FocusedDriver()
    {
        var ticked = CheckedDrivers();

        return _cmbFocus.SelectedItem is DriverComboItem picked && ticked.Contains(picked.Result)
            ? picked.Result
            : ticked.FirstOrDefault();
    }

    /// <summary>The focus picker, filled with the session's drivers in the list's order, the first picked.</summary>
    private void FillFocus()
    {
        _settingFocus = true;

        try
        {
            _cmbFocus.Items.Clear();
            _focusWidth = null;

            foreach (var item in _clbDrivers.Items)
            {
                _cmbFocus.Items.Add(item);
            }

            _cmbFocus.SelectedIndex = _cmbFocus.Items.Count > 0 ? 0 : -1;
        }
        finally
        {
            _settingFocus = false;
        }
    }

    /// <summary>
    /// The focused driver unticked: the picker moves to whoever the page now
    /// reads (the first ticked), so it never names a driver the reading is
    /// not about.
    /// </summary>
    private void KeepFocusTicked()
    {
        if (FocusedDriver() is not { } focus || ReferenceEquals((_cmbFocus.SelectedItem as DriverComboItem)?.Result, focus))
        {
            return;
        }

        _settingFocus = true;

        try
        {
            for (var i = 0; i < _cmbFocus.Items.Count; i++)
            {
                if (_cmbFocus.Items[i] is DriverComboItem item && ReferenceEquals(item.Result, focus))
                {
                    _cmbFocus.SelectedIndex = i;
                    break;
                }
            }
        }
        finally
        {
            _settingFocus = false;
        }
    }

    /// <summary>
    /// The reading of the focused driver's race: the headline, the three
    /// things, and the "Try this". Hidden, all of it, with no one to read.
    /// </summary>
    private void RefreshCoach()
    {
        var focus = FocusedDriver();
        var laps = focus is null ? new List<long>() : RaceCoach.Laps(focus);

        if (focus is null || laps.Count == 0)
        {
            _headline.Text = string.Empty;
            _headline.Visible = false;
            _drill.Say(null);

            ShowThings(new List<RaceCoach.Finding>(), _currentSession is null
                ? "Pick a session to read a race."
                : "Tick a driver to read their race.");
            return;
        }

        var row = PaceAnalysis.For(focus);
        var feet = RaceCoach.TrackFeet(Layouts, _currentSession?.TrackLayoutName);

        _headline.Text = RaceCoach.LapTimesHeadline(row, laps);
        _headline.Visible = true;
        _drill.Say(RaceCoach.Drill(row, laps));

        ShowThings(RaceCoach.ThreeThings(laps, RaceCoach.LapNumbers(focus), feet), "Too few laps to read anything into.");
    }

    private void ShowThings(List<RaceCoach.Finding> found, string none)
    {
        for (var i = 0; i < _things.Count; i++)
        {
            var (line, detail) = _things[i];
            var shown = i < found.Count;

            line.Text = shown ? $"{i + 1}. {found[i].Line}" : string.Empty;
            detail.Text = shown ? found[i].Detail : string.Empty;
            line.Visible = shown;
            detail.Visible = shown;
        }

        _thingsNone.Text = none;
        _thingsNone.Visible = found.Count == 0;
    }

    /// <summary>The headline, for the tests.</summary>
    internal string HeadlineText => _headline.Visible ? _headline.Text : string.Empty;

    /// <summary>The findings shown, bold line then detail, for the tests.</summary>
    internal IReadOnlyList<(string Line, string Detail)> ThingsShown =>
        _things.Where(t => t.Line.Visible).Select(t => (t.Line.Text, t.Detail.Text)).ToList();

    /// <summary>The "Try this" advice, for the tests.</summary>
    internal string DrillText => _drill.Visible ? _drill.Advice : string.Empty;

    /// <summary>The marks the chart last drew on screen, their text and whether a label found room - for the tests.</summary>
    internal IReadOnlyList<(string Text, Rectangle? Label)> MarksDrawn => _marksDrawn;

    private readonly List<(string Text, Rectangle? Label)> _marksDrawn = new();

    /// <summary>Picks the driver to focus on by name, for the tests.</summary>
    internal void FocusOn(string driverName)
    {
        for (var i = 0; i < _cmbFocus.Items.Count; i++)
        {
            if (_cmbFocus.Items[i] is DriverComboItem item && item.Result.DriverName == driverName)
            {
                _cmbFocus.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>The chart as it would be saved, for the tests and the screenshot.</summary>
    internal void DrawChartForTest(Graphics g, Rectangle bounds) => DrawChart(g, bounds, ChartPalette.Screen(), onScreen: true);

    private void ChartPanel_Paint(object? sender, PaintEventArgs e) =>
        DrawChart(e.Graphics, _chartPanel.ClientRectangle, ChartPalette.Screen());

    // WHY (20 Oct 2026): onScreen says whether this draw is the panel's own
    // paint. The export preview and the exported bitmap draw through here
    // too, at their own sizes, and they used to clear and refill _plotted -
    // so after an export the hover hit-tested the panel's cursor against
    // the bitmap's coordinates and showed the wrong lap over empty space.
    // Only the on-screen draw now touches the hover data.
    private void DrawChart(Graphics g, Rectangle bounds, ChartPalette palette, bool onScreen = true)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(palette.Background))
        {
            g.FillRectangle(bg, bounds);
        }

        var drivers = CheckedDrivers();

        // Flying laps only, as RefreshStats and PaceAnalysis.CountedLaps
        // count them. Index 0 of LapTimesMs is the run from the green to the
        // first crossing, not a lap: with the grid just behind the line it is
        // a second or so against laps of nine or ten, so plotting it pulled
        // the time axis down to fit it and squashed every real lap into the
        // top band of the chart - and the hover called it a "lap 1" quicker
        // than the Best in the table underneath. A lap of zero or less (a
        // jump start's placeholder) says nothing about pace either.
        //
        // Since 13 Nov 2026 each driver's laps are RaceCoach.Laps and their
        // numbers RaceCoach.LapNumbers, so a lap the reading calls "lap 9"
        // is the point over 9 here, and a driver credited with the start lap
        // no longer plots the rest of their start as a lap.
        var maxLaps = drivers.Count == 0 ? 0 : drivers.Max(d => d.LapTimesMs.Count);
        var series = drivers.Select(d => (Driver: d, Laps: RaceCoach.Laps(d), Numbers: RaceCoach.LapNumbers(d))).ToList();
        var allTimes = series.SelectMany(s => s.Laps).ToList();

        if (_currentSession is null || drivers.Count == 0 || allTimes.Count == 0)
        {
            using var msgBrush = new SolidBrush(palette.Muted);
            var msg = _currentSession is null ? "Select a session."
                : drivers.Count == 0 ? "Tick a driver to plot their laps."
                : "No full laps to plot yet - only the start pass was recorded.";
            var size = g.MeasureString(msg, AppTheme.BodyFont);

            // Lifted by about half a line of the body face, at the text size
            // in use, so it stays centred as the face grows.
            g.DrawString(msg, AppTheme.BodyFont, msgBrush, bounds.Left + (bounds.Width - size.Width) / 2, bounds.Top + bounds.Height / 2f - UiLayout.AtTextSize(10));

            // Nothing is plotted, so nothing is left to hover over - the
            // points of the chart that was showing before would otherwise
            // still turn the cursor into a hand over empty space.
            if (onScreen)
            {
                _plotted.Clear();
            }
            return;
        }

        var focus = FocusedDriver();
        var focusIndex = focus is null ? -1 : drivers.IndexOf(focus);
        var focusLaps = focusIndex >= 0 ? series[focusIndex].Laps : new List<long>();
        var reading = focusLaps.Count >= PaceAnalysis.Window;

        // The legend, along the top above the plot rather than inside it
        // (13 Nov 2026): inside, it sat on the slow laps at the top, which
        // is exactly where a crash lap's mark and label go, and on a narrow
        // chart it ran off the right-hand edge. In tick order, the focused
        // driver in bold, then what the shaded band means; wrapping onto
        // another line when the chart is too narrow for one.
        var legend = Enumerable.Range(0, drivers.Count)
            .Select(d => (Text: drivers[d].DriverName,
                Face: d == focusIndex && reading && drivers.Count > 1 ? AppTheme.BoldFont : AppTheme.SmallFont,
                Swatch: palette.Series[d % palette.Series.Length], Band: false))
            .ToList();

        if (reading)
        {
            legend.Add(("At your limit (within 1.5% of your best)", AppTheme.SmallFont, Color.FromArgb(90, palette.Highlight), true));
        }

        // The room round the plot holds the time labels down the left and
        // the lap numbers and "Lap" under it, all in the small face - so it
        // grows with the text size, or at 200% the labels run into the plot
        // and off the bottom. Worked out on every paint, so a new text size
        // is picked up by the repaint it brings. The legend's lines come on
        // top of the room above it.
        var padLeft = UiLayout.AtTextSize(64);
        var padRight = UiLayout.AtTextSize(16);
        var padBottom = UiLayout.AtTextSize(40);

        var swatchWidth = UiLayout.AtTextSize(14);
        var swatchHeight = UiLayout.AtTextSize(4);
        var swatchGap = UiLayout.AtTextSize(6);
        var itemGap = UiLayout.AtTextSize(16);
        var legendRow = (int)Math.Ceiling(Math.Max(AppTheme.SmallFont.GetHeight(g), AppTheme.BoldFont.GetHeight(g))) + UiLayout.AtTextSize(2);
        var legendLeft = bounds.Left + padLeft;
        var legendRight = bounds.Right - padRight;
        var placedLegend = new List<(string Text, Font Face, Color Swatch, bool Band, float X, float Y)>();
        var legendX = (float)legendLeft;
        var legendY = (float)(bounds.Top + UiLayout.AtTextSize(6));

        foreach (var item in legend)
        {
            var width = swatchWidth + swatchGap + g.MeasureString(item.Text, item.Face).Width;

            if (legendX > legendLeft && legendX + width > legendRight)
            {
                legendX = legendLeft;
                legendY += legendRow;
            }

            placedLegend.Add((item.Text, item.Face, item.Swatch, item.Band, legendX, legendY));
            legendX += width + itemGap;
        }

        var padTop = (int)(legendY - bounds.Top) + legendRow + UiLayout.AtTextSize(8);

        var plot = new Rectangle(
            bounds.Left + padLeft, bounds.Top + padTop,
            Math.Max(10, bounds.Width - padLeft - padRight),
            Math.Max(10, bounds.Height - padTop - padBottom));


        var minMs = allTimes.Min();
        var maxMs = allTimes.Max();

        // Pad the range slightly so lines never sit exactly on the frame,
        // and guard against a flat range (every lap identical) dividing by zero.
        var span = Math.Max(1, maxMs - minMs);
        var lowMs = minMs - span * 0.1;
        var highMs = maxMs + span * 0.1;
        var range = Math.Max(1, highMs - lowMs);

        using var axisPen = new Pen(palette.Axis);
        using var gridPen = new Pen(palette.Grid);
        using var labelBrush = new SolidBrush(palette.Muted);

        // The labels' offsets from their lines are fractions of a line of
        // the small face they are drawn in, so they follow the text size
        // with it - a time label lifted a fixed 7px sits below its gridline
        // once the face is twice as tall.
        var labelGap = UiLayout.AtTextSize(6);
        var halfLabel = UiLayout.AtTextSize(7);

        // Horizontal gridlines + y-axis time labels.
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - (plot.Height * i / 4f);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var valueMs = lowMs + range * i / 4.0;
            g.DrawString($"{valueMs / 1000.0:0.00}s", AppTheme.SmallFont, labelBrush, bounds.Left + labelGap, y - halfLabel);
        }

        g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        // The x axis runs over the flying laps, LapTimesMs index 1 to the
        // last, so the first real lap sits on the left edge. Each is labeled
        // index + 1, the same lap number the leaderboard and the lap windows
        // give it (they show index 0 as the start pass), so "lap 5" here is
        // "L5" there.
        var flyingCount = Math.Max(1, maxLaps - 1);
        float LapX(int lapIndex) =>
            plot.Left + (flyingCount == 1 ? plot.Width / 2f : plot.Width * (lapIndex - 1f) / (flyingCount - 1f));

        // X-axis lap labels, thinned out so they never overlap.
        var labelStep = Math.Max(1, flyingCount / 10);
        for (var lapIndex = 1; lapIndex <= flyingCount; lapIndex += labelStep)
        {
            var x = LapX(lapIndex);
            g.DrawString((lapIndex + 1).ToString(), AppTheme.SmallFont, labelBrush, x - labelGap, plot.Bottom + labelGap);
        }

        // "Lap" a line below the lap numbers, at the text size in use.
        using var axisLabelBrush = new SolidBrush(palette.Muted);
        g.DrawString("Lap", AppTheme.SmallFont, axisLabelBrush,
            plot.Left + plot.Width / 2f - UiLayout.AtTextSize(12), plot.Bottom + UiLayout.AtTextSize(22));

        // Rebuilt every paint so hover data can never describe a stale
        // chart - only the on-screen render is hit-tested.
        if (onScreen)
        {
            _plotted.Clear();
        }

        // A lap's dot at the text size in use, so the chart keeps its
        // proportions as the face grows.
        var dot = (float)UiLayout.AtTextSize(5f);

        float Y(double ms) => (float)(plot.Bottom - (ms - lowMs) / range * plot.Height);

        // The band of laps within 1.5% of the focused driver's best - "at
        // your limit" - under everything else, clipped to the plot.
        if (reading)
        {
            var best = focusLaps.Min();
            var top = Math.Max(plot.Top, Y(best * (1 + RaceCoach.LimitShare)));
            var bottom = Math.Min(plot.Bottom, Y(best));

            using var band = new SolidBrush(Color.FromArgb(48, palette.Highlight));
            g.FillRectangle(band, plot.Left, top, plot.Width, Math.Max(1f, bottom - top));
        }

        // One polyline per driver, the focused one last so it is drawn on
        // top, thicker; the others fainter when there is a race being read,
        // so they compare without competing.
        var order = Enumerable.Range(0, drivers.Count).Where(d => d != focusIndex).Append(focusIndex).Where(d => d >= 0);
        var focusPoints = new List<PointF>();

        foreach (var d in order)
        {
            var (driver, laps, numbers) = series[d];
            var color = palette.Series[d % palette.Series.Length];
            var faint = reading && d != focusIndex && drivers.Count > 1;
            var ink = faint ? Color.FromArgb(120, color) : color;
            using var linePen = new Pen(ink, d == focusIndex && drivers.Count > 1 ? 2.75f : faint ? 1.5f : 2f);
            using var dotBrush = new SolidBrush(ink);

            var points = new List<PointF>();
            for (var i = 0; i < laps.Count; i++)
            {
                var x = LapX(numbers[i] - 1);
                var y = Y(laps[i]);
                points.Add(new PointF(x, y));
                if (onScreen)
                {
                    _plotted.Add((new PointF(x, y), driver.DriverName, numbers[i], laps[i], color));
                }
            }

            if (points.Count > 1)
            {
                g.DrawLines(linePen, points.ToArray());
            }

            foreach (var p in points)
            {
                g.FillEllipse(dotBrush, p.X - dot / 2f, p.Y - dot / 2f, dot, dot);
            }

            if (d == focusIndex)
            {
                focusPoints = points;
            }
        }

        using (var legendBrush = new SolidBrush(palette.Text))
        {
            foreach (var item in placedLegend)
            {
                var middle = item.Y + legendRow / 2f;
                var height = item.Band ? swatchHeight * 2.5f : swatchHeight;

                using (var swatchBrush = new SolidBrush(item.Swatch))
                {
                    g.FillRectangle(swatchBrush, item.X, middle - height / 2f, swatchWidth, height);
                }

                var textHeight = item.Face.GetHeight(g);
                g.DrawString(item.Text, item.Face, legendBrush, item.X + swatchWidth + swatchGap, middle - textHeight / 2f);
            }
        }

        // The marks' labels keep off nothing but each other and the marks
        // now the legend is out of the plot.
        var taken = new List<RectangleF>();

        if (reading && focus is not null)
        {
            var marks = MarksFor(focusLaps, RaceCoach.TrackFeet(Layouts, _currentSession?.TrackLayoutName), palette);
            var drawn = DrawMarks(g, plot, focusPoints, marks, taken, dot, palette.Background);

            if (onScreen)
            {
                _marksDrawn.Clear();
                _marksDrawn.AddRange(drawn);
            }
        }
        else if (onScreen)
        {
            _marksDrawn.Clear();
        }

        // WHY (20 Oct 2026): the hover point is in the panel's coordinates,
        // so on the export preview it ringed a spot that had nothing to do
        // with the smaller chart there. It is an on-screen aid only.
        if (onScreen)
        {
            DrawHoverReadout(g, bounds, palette);
        }
    }

    /// <summary>
    /// Draws the hover readout: driver, lap number and exact lap time.
    /// Added because reading a precise value off the plot was guesswork -
    /// which made hand-checking the qualifying maths impractical.
    /// </summary>

    /// <summary>
    /// Finds the nearest plotted point to the cursor and shows its exact
    /// time. Only repaints when the highlighted lap actually changes -
    /// invalidating on every mouse move would redraw the whole chart
    /// dozens of times a second.
    /// </summary>
    private void ChartPanel_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_plotted.Count == 0)
        {
            return;
        }

        const float maxDistance = 14f;
        var bestDistance = float.MaxValue;
        (string Driver, int Lap, long Ms, Color Color, PointF At)? best = null;

        foreach (var (at, driver, lap, ms, color) in _plotted)
        {
            var dx = at.X - e.X;
            var dy = at.Y - e.Y;
            var distance = MathF.Sqrt(dx * dx + dy * dy);

            if (distance < bestDistance && distance <= maxDistance)
            {
                bestDistance = distance;
                best = (driver, lap, ms, color, at);
            }
        }

        if (best?.Driver == _hover?.Driver && best?.Lap == _hover?.Lap)
        {
            return;
        }

        _hover = best;
        _chartPanel.Cursor = best is null ? Cursors.Default : Cursors.Hand;
        _chartPanel.Invalidate();
    }

    private void DrawHoverReadout(Graphics g, Rectangle bounds, ChartPalette palette)
    {
        if (_hover is not { } h)
        {
            return;
        }

        var text = $"{h.Driver}  -  lap {h.Lap}  -  {h.Ms / 1000.0:0.000}s";

        // Nine points at the text size in use - no shared face is nine
        // points at the normal size, and a fixed 9pt stayed small at 200%
        // beside axis labels twice its size.
        using var font = new Font(AppTheme.BoldFont.FontFamily, 9F * AppTheme.FontScale, FontStyle.Bold);
        var size = TextRenderer.MeasureText(text, font);

        // The box's padding, its distance from the point and from the
        // chart's edges, and the ring and swatch in it, all at the text size
        // in use, so the box grows round the larger text rather than
        // pinching it.
        var padX = UiLayout.AtTextSize(18);
        var padY = UiLayout.AtTextSize(10);
        var offsetX = UiLayout.AtTextSize(12);
        var offsetY = UiLayout.AtTextSize(10);
        var dropBelow = UiLayout.AtTextSize(14);
        var edge = UiLayout.AtTextSize(4);
        var ring = (float)UiLayout.AtTextSize(11f);
        var swatchLeft = UiLayout.AtTextSize(6);
        var swatchSize = UiLayout.AtTextSize(6);
        var textLeft = UiLayout.AtTextSize(16);

        var width = size.Width + padX;
        var height = size.Height + padY;

        // Sits just above and to the right of the point so the value reads
        // next to the data it describes, rather than parked in a corner.
        var x = (int)h.At.X + offsetX;
        var y = (int)h.At.Y - height - offsetY;

        // Flip sides or below when that would run off the plot, so the box
        // stays fully visible near the edges.
        if (x + width > bounds.Right - edge)
        {
            x = (int)h.At.X - width - offsetX;
        }
        if (y < bounds.Top + edge)
        {
            y = (int)h.At.Y + dropBelow;
        }

        x = Math.Clamp(x, bounds.Left + edge, Math.Max(bounds.Left + edge, bounds.Right - width - edge));
        y = Math.Clamp(y, bounds.Top + edge, Math.Max(bounds.Top + edge, bounds.Bottom - height - edge));

        var box = new Rectangle(x, y, width, height);

        // Ring the hovered point so it's obvious which one is being read.
        using (var marker = new Pen(h.Color, 2f))
        {
            g.DrawEllipse(marker, h.At.X - ring / 2f, h.At.Y - ring / 2f, ring, ring);
        }

        // Leader line from the point to the box.
        using (var leader = new Pen(Color.FromArgb(150, h.Color), 1f))
        {
            g.DrawLine(leader, h.At.X, h.At.Y,
                box.Left + box.Width / 2f < h.At.X ? box.Right : box.Left,
                box.Top + box.Height / 2f);
        }

        using (var back = new SolidBrush(palette.Background))
        {
            g.FillRectangle(back, box);
        }
        using (var border = new Pen(h.Color, 1.6f))
        {
            g.DrawRectangle(border, box);
        }
        using (var swatch = new SolidBrush(h.Color))
        {
            g.FillEllipse(swatch, box.Left + swatchLeft, box.Top + box.Height / 2 - swatchSize / 2, swatchSize, swatchSize);
        }

        TextRenderer.DrawText(g, text, font,
            new Rectangle(box.Left + textLeft, box.Top, box.Width - padX, box.Height),
            palette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private void ExportChart()
    {
        if (_currentSession is null || CheckedDrivers().Count == 0)
        {
            AppDialog.Show(this, "Select a session and tick at least one driver first.", "Nothing to export",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var optionsForm = new ChartExportForm(_settings, (g, b, p) => DrawChart(g, b, p, onScreen: false));
        if (optionsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"lap-chart-{_currentSession.StartedAtUtc.ToLocalTime():yyyy-MM-dd_HH-mm}.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var size = optionsForm.Options.ImageSize;
            var palette = ChartPalette.For(optionsForm.Options.StyleKey);

            // Clear the hover box first - it's an on-screen aid, not
            // something that belongs in an exported image.
            var savedHover = _hover;
            _hover = null;

            using var bitmap = new Bitmap(size.Width, size.Height);

            // A new bitmap takes the screen's DPI, and in a DPI-aware run
            // (144 DPI at 150%) that would draw the chart's point-size text
            // half as big again inside the same pixels. At 96 DPI, which is
            // what a DPI-unaware run always sees, this changes nothing.
            bitmap.SetResolution(96, 96);

            // WHY (20 Oct 2026): the hover is put back in a finally, so a
            // draw that throws no longer leaves the on-screen readout lost.
            try
            {
                using var g = Graphics.FromImage(bitmap);
                DrawChart(g, new Rectangle(0, 0, size.Width, size.Height), palette, onScreen: false);
            }
            finally
            {
                _hover = savedHover;
            }

            bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);

            AppDialog.Show(this, $"Saved to:\n{dialog.FileName}", "Chart saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save the chart: {ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>A mark on the focused driver's line: the counted lap it sits on, what its label says, and its colour.</summary>
    private sealed record Mark(int Lap, string Text, Color Color, bool Line = false);

    /// <summary>
    /// The marks the chart puts on the focused driver's race: the best lap,
    /// each mistake with what it cost (and the car lengths, when the layout
    /// has a length), and where the fade starts. The mistakes come first,
    /// worst first, so when there is not room for every label the costly
    /// ones keep theirs; then the best, then the fade.
    /// </summary>
    private static List<Mark> MarksFor(IReadOnlyList<long> laps, double? trackFeet, ChartPalette palette)
    {
        var marks = new List<Mark>();
        var best = laps.Min();
        var mistake = palette.Series.Length > 5 ? palette.Series[5] : palette.Text;
        var fadeInk = palette.Series.Length > 2 ? palette.Series[2] : palette.Text;

        foreach (var i in RaceCoach.MistakeLaps(laps).OrderByDescending(i => laps[i]))
        {
            var cost = laps[i] - best;
            var lengths = RaceCoach.SayLengths(cost, best, trackFeet);

            marks.Add(new Mark(i, $"+{RaceCoach.Seconds(cost)}" + (lengths is null ? string.Empty : $", {lengths}"), mistake));
        }

        marks.Add(new Mark(Enumerable.Range(0, laps.Count).First(i => laps[i] == best), $"Best {PaceAnalysis.Time(best)}", palette.Highlight));

        if (RaceCoach.FadeOf(laps) is { } fade)
        {
            marks.Add(new Mark(fade.Start, "Fade starts", fadeInk, Line: true));
        }

        return marks;
    }

    /// <summary>
    /// Rings each mark's point and puts its label beside it: above, below,
    /// or to a side, whichever first fits inside the plot without covering
    /// the legend, another label, another mark or (if it can help it) a lap
    /// on the line. A label that fits nowhere is left off rather than drawn
    /// over something; its ring stays. A label set off from its point gets
    /// a short line back to it.
    /// </summary>
    private static List<(string Text, Rectangle? Label)> DrawMarks(
        Graphics g, Rectangle plot, IReadOnlyList<PointF> points, List<Mark> marks, List<RectangleF> taken, float dot, Color background)
    {
        var drawn = new List<(string Text, Rectangle? Label)>();

        if (points.Count == 0)
        {
            return drawn;
        }

        using var face = new Font(AppTheme.SmallFont, FontStyle.Bold);
        var gap = UiLayout.AtTextSize(6);
        var padX = UiLayout.AtTextSize(4);
        var padY = UiLayout.AtTextSize(1);
        var ring = dot * 2.6f;

        // The other marks' points are as much in the way as a label.
        var markSpots = marks
            .Where(m => m.Lap < points.Count)
            .Select(m => new RectangleF(points[m.Lap].X - ring / 2, points[m.Lap].Y - ring / 2, ring, ring))
            .ToList();
        var laps = points.Select(p => new RectangleF(p.X - dot, p.Y - dot, dot * 2, dot * 2)).ToList();
        var placed = new List<RectangleF>();

        foreach (var mark in marks)
        {
            if (mark.Lap >= points.Count)
            {
                continue;
            }

            var at = points[mark.Lap];

            using (var ringPen = new Pen(mark.Color, 2f))
            {
                if (mark.Line)
                {
                    // Where the fade starts: a dashed line down to the axis,
                    // so the laps from there on read as one stretch.
                    using var dash = new Pen(Color.FromArgb(150, mark.Color), 1.25f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                    g.DrawLine(dash, at.X, at.Y + ring / 2, at.X, plot.Bottom);
                }

                g.DrawEllipse(ringPen, at.X - ring / 2, at.Y - ring / 2, ring, ring);
            }

            var size = TextRenderer.MeasureText(g, mark.Text, face, Size.Empty, TextFormatFlags.NoPadding);
            var w = size.Width + padX * 2;
            var h = size.Height + padY * 2;
            var near = ring / 2 + gap;

            var tries = new[]
            {
                new PointF(at.X - w / 2f, at.Y - near - h),
                new PointF(at.X - w / 2f, at.Y + near),
                new PointF(at.X + near, at.Y - h / 2f),
                new PointF(at.X - near - w, at.Y - h / 2f),
                new PointF(at.X + near / 2, at.Y - near - h),
                new PointF(at.X - near / 2 - w, at.Y - near - h),
                new PointF(at.X + near / 2, at.Y + near),
                new PointF(at.X - near / 2 - w, at.Y + near),
                new PointF(at.X - w / 2f, at.Y - near * 2.5f - h),
                new PointF(at.X - w / 2f, at.Y + near * 2.5f),
            };

            RectangleF? spot = null;

            // First somewhere that covers no lap on the line either; then
            // anywhere that covers no label, legend or mark.
            foreach (var strict in new[] { true, false })
            {
                foreach (var t in tries)
                {
                    var box = new RectangleF(
                        Math.Clamp(t.X, plot.Left + 1, Math.Max(plot.Left + 1, plot.Right - w - 1)),
                        Math.Clamp(t.Y, plot.Top + 1, Math.Max(plot.Top + 1, plot.Bottom - h - 1)),
                        w, h);

                    if (box.Right > plot.Right || box.Bottom > plot.Bottom
                        || taken.Any(box.IntersectsWith) || placed.Any(box.IntersectsWith)
                        || markSpots.Any(box.IntersectsWith)
                        || strict && laps.Any(box.IntersectsWith))
                    {
                        continue;
                    }

                    spot = box;
                    break;
                }

                if (spot is not null)
                {
                    break;
                }
            }

            if (spot is not { } label)
            {
                drawn.Add((mark.Text, null));
                continue;
            }

            placed.Add(label);

            // A label pushed away from its point gets a line back to it.
            var nearestX = Math.Clamp(at.X, label.Left, label.Right);
            var nearestY = Math.Clamp(at.Y, label.Top, label.Bottom);
            var away = MathF.Sqrt((nearestX - at.X) * (nearestX - at.X) + (nearestY - at.Y) * (nearestY - at.Y));

            if (away > near * 1.6f)
            {
                using var leader = new Pen(Color.FromArgb(150, mark.Color), 1f);
                var dx = nearestX - at.X;
                var dy = nearestY - at.Y;
                var start = new PointF(at.X + dx / away * ring / 2, at.Y + dy / away * ring / 2);
                g.DrawLine(leader, start, new PointF(nearestX, nearestY));
            }

            using (var back = new SolidBrush(Color.FromArgb(225, background)))
            {
                g.FillRectangle(back, label);
            }

            var whole = Rectangle.Round(label);

            TextRenderer.DrawText(g, mark.Text, face, whole, mark.Color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            drawn.Add((mark.Text, whole));
        }

        return drawn;
    }

    private sealed record SessionComboItem(SessionRecord Session)
    {
        public override string ToString()
        {
            var track = string.IsNullOrWhiteSpace(Session.TrackLayoutName) ? "" : $" - {Session.TrackLayoutName}";
            var label = string.IsNullOrWhiteSpace(Session.Label) ? "" : $" - {Session.Label}";
            return $"{AppTime.DateAndTime(Session.StartedAtUtc.ToLocalTime())}{track}{label}";
        }
    }

    private sealed record DriverComboItem(SessionResultEntry Result)
    {
        public override string ToString() => $"{Result.DriverName} ({Plural.Of(Result.LapTimesMs.Count, "lap")})";
    }
}
