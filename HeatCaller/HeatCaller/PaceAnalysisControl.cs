namespace HeatCaller;

/// <summary>
/// What a session's lap times say about how each driver drove.
///
/// Sorted by pace rather than by result, deliberately. Side by side with the
/// result sheet the disagreements are the interesting part - the driver who
/// finished sixth on the third-quickest pace lost that race somewhere other
/// than the throttle, and this is where that shows up.
///
/// This is a control rather than a window because it is wanted in two
/// places: straight after a race from Race Control, where the session is
/// already decided, and on Race Analytics, where it is not and a race has to
/// be picked. Those differ by one combo box, which is not a reason to have
/// two copies of a table with twelve columns in it. PaceAnalysisForm is now
/// a frame around this.
///
/// 13 Nov 2026, the Race Engineer rework (RaceCoach): one driver's race is
/// read first - whose is picked, the winner's to start with - in a headline,
/// then three gauges with zones (consistency, time left on the table, fade)
/// so a figure reads as good or bad without knowing the scale, then every
/// driver's verdict and one thing to try. The full table stays, under all of
/// that, for anyone who wants the figures.
/// </summary>
public sealed class PaceAnalysisControl : UserControl
{
    /// <summary>The track layouts, for a layout's length - the car lengths beside each gap (RaceCoach). Null says no car lengths.</summary>
    public TrackLayoutStore? Layouts { get; set; }

    /// <summary>One race in the picker, rendered the way it reads in a list.</summary>
    private sealed record Choice(SessionRecord Session)
    {
        public override string ToString()
        {
            var when = Session.StartedAtUtc == default
                ? "undated"
                : Session.StartedAtUtc.ToLocalTime().ToString("d MMM ") + AppTime.TimeOfDay(Session.StartedAtUtc.ToLocalTime());

            var what = string.IsNullOrWhiteSpace(Session.Label)
                ? Session.SessionType.ToString()
                : Session.Label;

            return string.IsNullOrWhiteSpace(Session.TrackLayoutName)
                ? $"{when}  -  {what}"
                : $"{when}  -  {what}  ({Session.TrackLayoutName})";
        }
    }

    /// <summary>One driver in the "whose race" picker.</summary>
    private sealed record DriverChoice(PaceAnalysis.Row Row, SessionResultEntry Entry)
    {
        public override string ToString() => NameOf(Row);
    }

    private readonly SessionHistoryStore? _history;
    private readonly SessionRecord? _pinned;

    private readonly ComboBox _cmbSession = new();
    private readonly ComboBox _cmbDriver = new();
    private readonly Label _lblPick = new();
    private readonly Label _lblSummary = new();
    private readonly DataGridView _grid = new();
    private readonly DataGridView _verdicts = new();
    private readonly DrillNote _drill = new();
    private Label _headline = new();

    private readonly ZoneGauge _consistency = new();
    private readonly ZoneGauge _table = new();
    private readonly ZoneGauge _fade = new();
    private GaugeRow? _gauges;

    /// <summary>The drivers of the race showing, as worked out for it - the gauges and the verdicts read these.</summary>
    private List<(PaceAnalysis.Row Row, SessionResultEntry Entry)> _rows = new();

    /// <summary>Set while the picker is being rebuilt, so that does not count as a choice.</summary>
    private bool _loading;

    /// <summary>The view opened on one particular session - no picker.</summary>
    public PaceAnalysisControl(SessionRecord session)
        : this(null, session)
    {
    }

    /// <summary>The view that picks its own session - the Race Analytics tab.</summary>
    public PaceAnalysisControl(SessionHistoryStore history)
        : this(history, null)
    {
    }

    private PaceAnalysisControl(SessionHistoryStore? history, SessionRecord? pinned)
    {
        _history = history;
        _pinned = pinned;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once: on Race Analytics this
        // sits in a tab whose row is drawn in the bold face, which it would
        // otherwise take. See TextSize.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();

        RefreshAll();
    }

    /// <summary>What this control keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The race picker's width at the normal text size.</summary>
    private const int SessionPickerWidth = 420;

    /// <summary>The driver picker's least width at the normal text size - as wide as the longest name past that.</summary>
    private const int DriverPickerWidth = 200;

    /// <summary>The driver picker's width as last measured - see Pieces.PickerWidth.</summary>
    private (Font Font, int Count, int Width)? _driverWidth;

    /// <summary>The pickers' line - held so a refilled driver list can re-measure it.</summary>
    private FieldRow? _pickRow;

    /// <summary>The headline, for the tests.</summary>
    internal string Headline => _headline.Text;

    /// <summary>The "Try this" advice, or empty when there is none - for the tests.</summary>
    internal string Advice => _drill.Visible ? _drill.Advice : string.Empty;

    /// <summary>The gauges, for the tests: consistency, left on the table, fade.</summary>
    internal IReadOnlyList<ZoneGauge> Gauges => new[] { _consistency, _table, _fade };

    /// <summary>Whose race is being read, for the tests.</summary>
    internal string DriverShowing => (_cmbDriver.SelectedItem as DriverChoice)?.ToString() ?? string.Empty;

    /// <summary>The drivers in the verdict card, top to bottom, for the tests.</summary>
    internal IReadOnlyList<string> VerdictDrivers =>
        _verdicts.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["driver"].Value as string ?? string.Empty).ToList();

    /// <summary>The full table's rows, for the tests.</summary>
    internal int TableRows => _grid.Rows.Count;

    /// <summary>Picks whose race to read, by name - for the tests and pictures.</summary>
    internal void PickDriver(string name)
    {
        var index = _cmbDriver.Items.Cast<DriverChoice>().ToList()
            .FindIndex(c => string.Equals(c.ToString(), name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            _cmbDriver.SelectedIndex = index;
        }
    }

    /// <summary>The control's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this control through its tab's bold face - the only way it does, since it wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The race picker (on Race Analytics - not when the race is already
    /// decided) and whose race, the headline, the three gauges across the
    /// page, everyone's verdict, the thing to try, and the full table in a
    /// card taking the height that is left. On the layout pieces
    /// (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// One FillPage, the control's only child: PaceAnalysisForm asks it for
    /// the least the view can do with.
    /// </summary>
    private void BuildLayout()
    {
        BuildGrid();
        BuildVerdicts();

        // Every heading whole, at the text size in use, and every column of
        // figures kept wide enough for its widest figure - see
        // Pieces.KeepHeadingsWhole. The best lap and the fade were cut to
        // "11..." and "+..." at 200% while the driver's name gave way
        // beside columns with room to spare (layout pictures, 30 Sep).
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid, PaceWidest));
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_verdicts, ("pos", "P99"), ("run", "99.999")));

        // An empty table says so under its headings; why it is empty is the
        // headline (Render).
        EmptyNote.For(_grid, () => "No laps to read the pace from.");

        var page = new FillPage();
        var pickRow = new FieldRow();

        if (_pinned is null)
        {
            _cmbSession.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbSession.SelectedIndexChanged += (_, _) =>
            {
                if (!_loading)
                {
                    Render();
                }
            };

            _lblPick.Text = "Race:";
            pickRow.Add(_lblPick, _cmbSession, () => UiLayout.AtTextSize(SessionPickerWidth));
        }

        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriver.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading)
            {
                ShowDriver();
            }
        };

        pickRow.Add(new Label { Text = "Whose race:" }, _cmbDriver,
            () => Math.Max(UiLayout.AtTextSize(DriverPickerWidth), Pieces.PickerWidth(_cmbDriver, ref _driverWidth)));
        _pickRow = pickRow;

        page.Add(pickRow, gapBelow: UiLayout.RowGap);

        _headline = CoachPieces.Headline(_textSize);
        page.Add(_headline, gapBelow: UiLayout.RowGap);

        _consistency.Configure("Consistency", ConsistencyZones);
        _table.Configure("Left on the table", TableZones);
        _fade.Configure("Fade", FadeZones);

        _gauges = new GaugeRow(new[] { _consistency, _table, _fade });
        _textSize.Keep(() => _gauges.Remeasure());
        page.Add(_gauges, gapBelow: UiLayout.RowGap);

        var verdicts = new Section("Everyone's verdict");

        verdicts.Add(_verdicts, gapBelow: 0);
        page.Add(verdicts, gapBelow: UiLayout.RowGap);

        page.Add(_drill);

        // The figures the gauges and verdicts are read from, every driver,
        // every column, and a line on who stood out.
        _lblSummary.ForeColor = AppTheme.TextMuted;
        _lblSummary.AutoSize = true;
        _textSize.Wear(_lblSummary, () => AppTheme.SmallFont);

        var table = new Section("All the numbers");

        table.Add(_lblSummary);
        table.AddFill(_grid, () => _grid.ColumnHeadersHeight + 4 * _grid.RowTemplate.Height, gapBelow: 0);

        page.AddFill(table, table.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// Re-reads the session list and redraws. Called when the tab is opened,
    /// because races finish while this page sits in the background.
    /// </summary>
    public void RefreshAll()
    {
        if (_pinned is null && _history is not null)
        {
            var chosen = (_cmbSession.SelectedItem as Choice)?.Session.Id;

            _loading = true;

            try
            {
                _cmbSession.Items.Clear();

                // Newest first - the race somebody wants to read the pace of
                // is almost always the one that just finished. Practice is
                // included: a practice session is where most of the lap
                // times in this app come from, and its pace is as readable
                // as a race's. A pasted result is not: it has no lap times,
                // so it only ever drew an empty chart (2 Nov 2026).
                foreach (var session in _history.All.Where(s => s.Results.Count > 0 && !s.IsImported()))
                {
                    _cmbSession.Items.Add(new Choice(session));
                }

                var index = chosen is null
                    ? 0
                    : _cmbSession.Items
                        .Cast<Choice>()
                        .ToList()
                        .FindIndex(c => string.Equals(c.Session.Id, chosen, StringComparison.Ordinal));

                if (_cmbSession.Items.Count > 0)
                {
                    _cmbSession.SelectedIndex = index >= 0 ? index : 0;
                }
            }
            finally
            {
                _loading = false;
            }
        }

        Render();
    }

    private SessionRecord? Current =>
        _pinned ?? (_cmbSession.SelectedItem as Choice)?.Session;

    private void Render()
    {
        _grid.Rows.Clear();
        _rows = new List<(PaceAnalysis.Row, SessionResultEntry)>();

        if (Current is not { } session)
        {
            FillDrivers();
            Empty("No sessions recorded yet. The pace read fills in as soon as one is.");
            return;
        }

        // Ghost cars left out, by transponder: older saved sessions still
        // carry them, and a ghost's pace is generated, not driven.
        _rows = session.Results
            .OrderBy(r => r.Position == 0 ? int.MaxValue : r.Position)
            .Select(entry => (Row: PaceAnalysis.For(entry), Entry: entry))
            .Where(x => x.Row.CountedLaps > 0 && !GhostService.IsGhost(x.Row.Transponder))
            .ToList();

        FillDrivers();

        // A session where only ghosts ran has laps, just none a real driver
        // drove - the "recorded before lap times were kept" message would
        // be wrong for it.
        if (_rows.Count == 0 && session.Results.Any(r => r.LapTimesMs.Count > 0 && GhostService.IsGhost(r.Transponder)))
        {
            Empty("Only ghost cars ran in this session, so there is no real driver's pace to read.");
            return;
        }

        if (_rows.Count == 0)
        {
            Empty("This session was recorded before lap-by-lap times were kept, so there is no pace to read.");
            return;
        }

        var rows = _rows.Select(x => x.Row).ToList();

        BuildSummary(rows);
        Fill(rows);
        FillVerdicts();
        ShowDriver();
    }

    /// <summary>Nothing to read: says why in the headline, and clears the rest.</summary>
    private void Empty(string why)
    {
        _headline.Text = why;
        _lblSummary.Text = string.Empty;
        _consistency.Show(null, "-", string.Empty);
        _table.Show(null, "-", string.Empty);
        _fade.Show(null, "-", string.Empty);
        _verdicts.Rows.Clear();
        SizeVerdicts();
        _drill.Say(null);
    }

    /// <summary>
    /// The drivers of this race in the "whose race" picker, in finishing
    /// order. Whoever was being read stays picked if they ran in this race
    /// too; otherwise the winner, or the first driver there is.
    /// </summary>
    private void FillDrivers()
    {
        var previous = (_cmbDriver.SelectedItem as DriverChoice)?.ToString();

        _loading = true;

        try
        {
            _cmbDriver.Items.Clear();

            foreach (var (row, entry) in _rows)
            {
                _cmbDriver.Items.Add(new DriverChoice(row, entry));
            }

            var choices = _cmbDriver.Items.Cast<DriverChoice>().ToList();
            var index = previous is null ? -1 : choices.FindIndex(c => string.Equals(c.ToString(), previous, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                index = choices.FindIndex(c => c.Row.Position == 1);
            }

            if (choices.Count > 0)
            {
                _cmbDriver.SelectedIndex = Math.Max(0, index);
            }
        }
        finally
        {
            _loading = false;
        }

        _driverWidth = null;
        _pickRow?.PerformLayout();
    }

    /// <summary>Reads the picked driver's race: the headline, the gauges, the thing to try, and their line picked in the verdicts.</summary>
    private void ShowDriver()
    {
        if (_cmbDriver.SelectedItem is not DriverChoice { Row: var row, Entry: var entry } || Current is not { } session)
        {
            return;
        }

        var laps = RaceCoach.Laps(entry);
        var feet = RaceCoach.TrackFeet(Layouts, session.TrackLayoutName);
        var result = ResultClause(session, entry);

        _headline.Text = string.IsNullOrEmpty(result)
            ? RaceCoach.Headline(row, laps)
            : $"{RaceCoach.Headline(row, laps)} {result}";

        if (row.Thin)
        {
            const string thin = "Too few laps to read.";

            _consistency.Show(null, "-", thin);
            _table.Show(null, "-", thin);
            _fade.Show(null, "-", thin);
        }
        else
        {
            _consistency.Show(row.ConsistencyPercent, $"{row.ConsistencyPercent:0.0}%", ConsistencyReading(row, laps));
            _table.Show(row.PotentialGainMs / 1000.0, $"{row.PotentialGainMs / 1000.0:0.0}s", TableReading(row, feet));
            _fade.Show(
                row.FadeMs == 0 ? null : Math.Max(0, row.FadeMs / 1000.0),
                row.FadeMs == 0 ? "-" : PaceAnalysis.Delta(row.FadeMs),
                FadeReading(row));
        }

        _drill.Say(RaceCoach.Drill(row, laps));

        // Their line tinted, rather than selected: a grid that is not in
        // focus drops or greys its selection, and the tint says "this is
        // the one being read" whether or not anyone has clicked it.
        _verdicts.ClearSelection();

        foreach (DataGridViewRow line in _verdicts.Rows)
        {
            var mine = ReferenceEquals(line.Tag, entry);
            var back = mine ? AppTheme.PrimaryTint : Color.Empty;

            line.DefaultCellStyle.BackColor = back;
            line.DefaultCellStyle.SelectionBackColor = mine ? AppTheme.PrimaryTint : Color.Empty;
            line.DefaultCellStyle.SelectionForeColor = mine ? AppTheme.TextDark : Color.Empty;
        }
    }

    // --- The gauges -------------------------------------------------------------

    /// <summary>Consistency's zones: PaceAnalysis's own lines, 1.5% and 4%.</summary>
    internal static readonly GaugeZone[] ConsistencyZones =
    {
        new(1.5, ZoneKind.Good, "at your limit"),
        new(4.0, ZoneKind.Middling, "normal"),
        new(6.0, ZoneKind.Bad, "scrappy"),
    };

    /// <summary>Left on the table's zones: 1.5 s is where tidying up beats finding speed (PaceAnalysis.Row.Verdict).</summary>
    internal static readonly GaugeZone[] TableZones =
    {
        new(1.5, ZoneKind.Good, "tidy"),
        new(4.0, ZoneKind.Middling, "some to find"),
        new(8.0, ZoneKind.Bad, "lots"),
    };

    /// <summary>Fade's zones, in seconds a lap slower in the last third than the first.</summary>
    internal static readonly GaugeZone[] FadeZones =
    {
        new(0.1, ZoneKind.Good, "held pace"),
        new(0.4, ZoneKind.Middling, "slowed"),
        new(1.0, ZoneKind.Bad, "faded"),
    };

    /// <summary>
    /// What a driver's consistency means. When mistakes pushed it up, says
    /// what it would have been without them: the crash lap is one thing to
    /// fix, the drift of the rest is another.
    /// </summary>
    internal static string ConsistencyReading(PaceAnalysis.Row row, IReadOnlyList<long> laps)
    {
        var mistakes = RaceCoach.MistakeLaps(laps);
        var clean = laps.Where((_, i) => !mistakes.Contains(i)).ToList();
        var without = mistakes.Count > 0 && clean.Count >= PaceAnalysis.Window ? Spread(clean) : (double?)null;

        var lead = row.ConsistencyPercent <= 1.5 ? "At your limit."
            : row.ConsistencyPercent < 4.0 ? "Normal."
            : "Scrappy.";

        if (without is double rest)
        {
            var which = mistakes.Count == 1
                ? $"Lap {mistakes[0] + 1} pushed it up"
                : $"{mistakes.Count} mistake laps pushed it up";

            return $"{lead} {which}. Without {(mistakes.Count == 1 ? "it" : "them")} it's {rest:0.0}%.";
        }

        return row.ConsistencyPercent <= 1.5 ? $"{lead} Lap after lap within a whisker of each other."
            : row.ConsistencyPercent < 4.0 ? $"{lead} Could be tighter."
            : $"{lead} The lap times are all over the place.";
    }

    /// <summary>What the time left on the table means, with the car lengths a typical lap gave away when the layout's length is set.</summary>
    internal static string TableReading(PaceAnalysis.Row row, double? trackFeet)
    {
        var seconds = row.PotentialGainMs / 1000.0;

        var lead = seconds < 1.5 ? "Tidy. Under the 1.5 s line, so more speed has to come from the car or the line."
            : seconds < 4.0 ? "Some to find. Over the 1.5 s line, where tidying up beats finding speed."
            : "Lots. Clean laps would find more than any setup change.";

        var lengths = RaceCoach.SayLengths(row.AverageOffBestMs, row.BestLapMs, trackFeet);

        return lengths is null ? lead : $"{lead} A typical lap was {lengths} off your best.";
    }

    /// <summary>What the fade means: held the pace, slowed, faded, or got quicker.</summary>
    internal static string FadeReading(PaceAnalysis.Row row)
    {
        if (row.FadeMs == 0)
        {
            return row.CountedLaps < 6 ? "Too short a race to tell." : "Held pace from start to finish.";
        }

        var seconds = row.FadeMs / 1000.0;

        return seconds < 0 ? $"Got quicker: {-seconds:0.00} s a lap faster in the last third than the first."
            : seconds < 0.1 ? "Held pace from start to finish."
            : seconds < 0.4 ? $"{seconds:0.00} s a lap slower in the last third. Check the pack."
            : $"{seconds:0.00} s a lap slower in the last third. Check the pack and the tyres.";
    }

    /// <summary>The spread of some laps, as PaceAnalysis works it out: the standard deviation as a share of the average.</summary>
    private static double Spread(IReadOnlyList<long> laps)
    {
        var average = laps.Average(l => (double)l);
        var variance = laps.Select(l => (l - average) * (l - average)).Average();

        return average > 0 ? Math.Sqrt(variance) / average * 100.0 : 0;
    }

    /// <summary>
    /// The clause about the result after the headline: "Won it, 2.4 s clear
    /// of Sam Okafor.", "P2 of 5, 3.1 s behind Priya Shah.". Nothing for
    /// practice, which has no result to speak of, or for a driver with no
    /// place.
    /// </summary>
    internal static string ResultClause(SessionRecord session, SessionResultEntry entry)
    {
        if (session.SessionType == RaceSessionType.Practice || entry.Position <= 0)
        {
            return string.Empty;
        }

        var field = session.Results.Where(r => r.Position > 0 && !GhostService.IsGhost(r.Transponder)).OrderBy(r => r.Position).ToList();
        var count = field.Count;

        if (entry.Position == 1)
        {
            var second = field.FirstOrDefault(r => r.Position == 2);

            if (second is null)
            {
                return "Won it.";
            }

            if (second.Laps < entry.Laps)
            {
                return $"Won it by {Plural.Of(entry.Laps - second.Laps, "lap")}.";
            }

            return second.ElapsedMs > entry.ElapsedMs && entry.ElapsedMs > 0
                ? $"Won it, {(second.ElapsedMs - entry.ElapsedMs) / 1000.0:0.0} s clear of {NameOf(second)}."
                : "Won it.";
        }

        var winner = field.FirstOrDefault(r => r.Position == 1);
        var place = $"P{entry.Position} of {count}";

        if (winner is null)
        {
            return $"{place}.";
        }

        if (winner.Laps > entry.Laps)
        {
            return $"{place}, {Plural.Of(winner.Laps - entry.Laps, "lap")} down on {NameOf(winner)}.";
        }

        return entry.ElapsedMs > winner.ElapsedMs && winner.ElapsedMs > 0
            ? $"{place}, {(entry.ElapsedMs - winner.ElapsedMs) / 1000.0:0.0} s behind {NameOf(winner)}."
            : $"{place}.";
    }

    private static string NameOf(SessionResultEntry entry) =>
        string.IsNullOrWhiteSpace(entry.DriverName) ? entry.Transponder : entry.DriverName;

    private static string NameOf(PaceAnalysis.Row row) =>
        string.IsNullOrWhiteSpace(row.DriverName) ? row.Transponder : row.DriverName;

    // --- Everyone's verdict ------------------------------------------------------

    /// <summary>
    /// The verdict card's table: where each driver finished, the pace they
    /// could hold, and what their race adds up to. As tall as its rows - the
    /// page scrolls rather than this - and a click on a line reads that
    /// driver's race.
    /// </summary>
    private void BuildVerdicts()
    {
        _verdicts.ReadOnly = true;
        _verdicts.AllowUserToAddRows = false;
        _verdicts.AllowUserToDeleteRows = false;
        _verdicts.AllowUserToResizeRows = false;
        _verdicts.AllowUserToResizeColumns = false;
        _verdicts.RowHeadersVisible = false;
        _verdicts.MultiSelect = false;
        _verdicts.ScrollBars = ScrollBars.None;
        _verdicts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _verdicts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _verdicts.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        // The verdict is a sentence, and wraps rather than being cut short;
        // the rows grow to it.
        _verdicts.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        void Column(string name, string header, int weight, string tip)
        {
            var index = _verdicts.Columns.Add(name, header);
            _verdicts.Columns[index].FillWeight = weight;
            _verdicts.Columns[index].ToolTipText = tip;
            _verdicts.Columns[index].SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        Column("pos", "Fin", 30, "Where they finished the race.");
        Column("driver", "Driver", 110, "Driver name. Click a line to read that driver's race.");
        Column("run", "Best 3 in a row", 60, "Average of their quickest unbroken three laps: the pace they could hold. Sorted by this.");
        Column("read", "Read", 300, "What their race adds up to.");

        _verdicts.Columns["read"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

        _verdicts.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _verdicts.Rows[e.RowIndex].Tag is SessionResultEntry entry)
            {
                var index = _cmbDriver.Items.Cast<DriverChoice>().ToList().FindIndex(c => ReferenceEquals(c.Entry, entry));

                if (index >= 0)
                {
                    _cmbDriver.SelectedIndex = index;
                }
            }
        };

        // The rows' heights follow the width the sentences wrap to.
        var lastWidth = -1;

        _verdicts.SizeChanged += (_, _) =>
        {
            if (_verdicts.Width != lastWidth)
            {
                lastWidth = _verdicts.Width;
                _verdicts.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells);
                SizeVerdicts();
            }
        };

        _textSize.Keep(SizeVerdicts);
    }

    /// <summary>Every driver's verdict, by the pace they could hold.</summary>
    private void FillVerdicts()
    {
        _verdicts.Rows.Clear();

        var byPace = _rows
            .OrderBy(x => x.Row.BestConsecutiveAverageMs <= 0 ? long.MaxValue : x.Row.BestConsecutiveAverageMs)
            .ToList();

        foreach (var (row, entry) in byPace)
        {
            var index = _verdicts.Rows.Add(
                row.Position == 0 ? "-" : $"P{row.Position}",
                NameOf(row),
                PaceAnalysis.Time(row.BestConsecutiveAverageMs),
                RaceCoach.Headline(row, RaceCoach.Laps(entry)));

            _verdicts.Rows[index].Tag = entry;

            if (row.Thin)
            {
                _verdicts.Rows[index].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }
        }

        _verdicts.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells);
        SizeVerdicts();
    }

    /// <summary>The verdict table as tall as its headings and rows, and the page told.</summary>
    private void SizeVerdicts()
    {
        var height = _verdicts.ColumnHeadersHeight
                     + _verdicts.Rows.Cast<DataGridViewRow>().Sum(r => r.Height)
                     + 2;

        // At least a line, so an empty card is not a sliver.
        height = Math.Max(height, _verdicts.ColumnHeadersHeight + _verdicts.RowTemplate.Height + 2);

        if (_verdicts.Height != height)
        {
            _verdicts.Height = height;
            Pieces.FillPageOf(_verdicts)?.LeastHeightMayHaveChanged();
            _verdicts.Parent?.PerformLayout(_verdicts, nameof(Height));
        }
    }

    // --- All the numbers ----------------------------------------------------------

    /// <summary>
    /// The widest figure each column of figures shows (Pieces
    /// .KeepHeadingsWhole): a place, a lap count, a lap time
    /// (PaceAnalysis.Time, under a minute), a gap (PaceAnalysis.Delta), a
    /// spread and a time left on the table.
    /// </summary>
    private static readonly (string Column, string Widest)[] PaceWidest =
    {
        ("pos", "99"),
        ("laps", "999"),
        ("best", "99.999"),
        ("run", "99.999"),
        ("quick", "99.999"),
        ("avg", "99.999"),
        ("off", "+9.99s"),
        ("cons", "99.9%"),
        ("fade", "+9.99s"),
        ("gain", "99.9s"),
    };

    private void BuildGrid()
    {
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        void Column(string name, string header, int weight, string tip)
        {
            var index = _grid.Columns.Add(name, header);
            _grid.Columns[index].FillWeight = weight;
            _grid.Columns[index].ToolTipText = tip;
        }

        Column("pos", "Fin", 30, "Where they finished the race.");
        Column("driver", "Driver", 120, "Driver name.");
        Column("laps", "Laps", 32, "Counted laps, not including the run from the green to their first crossing.");
        Column("best", "Best", 46, "Their quickest single lap.");
        Column("run", "Best 3 in a row", 62, "Average of their quickest unbroken three laps - what they could actually sustain.");
        Column("quick", "Best 3 any", 56, "Average of their three quickest laps wherever they came. Far better than the column before it means the speed came in flashes.");
        Column("avg", "Average", 50, "Average of every counted lap.");
        Column("off", "Off best", 48, "How far off their own best they ran on a typical lap.");
        Column("cons", "Consistency", 54, "Spread of their lap times. Lower is tidier - under about 1.5% is driving at a limit, over 4% is scrappy.");
        Column("fade", "Fade", 44, "Last third of the race against the first. Positive means they slowed down.");
        Column("gain", "Left on table", 58, "What repeating their own best lap every lap would have been worth over this race.");
        // The verdict gives way before the name does: its whole wording is
        // a sentence, cut short in any narrow window, and the name is what
        // the row is about (it was 90 against this one's 150, and a long
        // name read "Alexa..." at 200%).
        Column("verdict", "Read", 120, "What the numbers add up to.");
    }

    private void Fill(List<PaceAnalysis.Row> rows)
    {
        // By pace, not by result. See the note on the class.
        var byPace = rows
            .OrderBy(r => r.BestConsecutiveAverageMs <= 0 ? long.MaxValue : r.BestConsecutiveAverageMs)
            .ToList();

        foreach (var row in byPace)
        {
            var index = _grid.Rows.Add(
                row.Position == 0 ? "-" : row.Position.ToString(),
                NameOf(row),
                row.CountedLaps.ToString(),
                PaceAnalysis.Time(row.BestLapMs),
                PaceAnalysis.Time(row.BestConsecutiveAverageMs),
                PaceAnalysis.Time(row.QuickestAverageMs),
                PaceAnalysis.Time(row.AverageLapMs),
                PaceAnalysis.Delta(row.AverageOffBestMs),
                row.Thin ? "-" : $"{row.ConsistencyPercent:0.0}%",
                row.FadeMs == 0 ? "-" : PaceAnalysis.Delta(row.FadeMs),
                row.PotentialGainMs <= 0 ? "-" : $"{row.PotentialGainMs / 1000.0:0.0}s",
                row.Verdict);

            var cells = _grid.Rows[index].Cells;

            if (row.Thin)
            {
                _grid.Rows[index].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
                continue;
            }

            // Colour only the two columns worth reacting to. Colouring every
            // number turns the table into wallpaper and none of it gets
            // read.
            //
            // In the picked row as well: its cells are drawn in the
            // selection's colour otherwise, and the first row - picked as
            // the table fills - lost its colours (layout pictures, 30 Sep).
            var consistency = row.ConsistencyPercent <= 1.5
                ? AppTheme.Success
                : row.ConsistencyPercent >= 4.0
                    ? AppTheme.Danger
                    : AppTheme.TextDark;

            cells["cons"].Style.ForeColor = consistency;
            cells["cons"].Style.SelectionForeColor = consistency;

            if (row.FadeMs != 0)
            {
                var fade = row.FadeMs > 0 ? AppTheme.Danger : AppTheme.Success;

                cells["fade"].Style.ForeColor = fade;
                cells["fade"].Style.SelectionForeColor = fade;
            }
        }
    }

    private void BuildSummary(List<PaceAnalysis.Row> rows)
    {
        var quickest = rows.OrderBy(r => r.BestLapMs <= 0 ? long.MaxValue : r.BestLapMs).First();

        var tidiest = rows.Where(r => !r.Thin)
            .OrderBy(r => r.ConsistencyPercent)
            .FirstOrDefault();

        var mostLeft = rows.OrderByDescending(r => r.PotentialGainMs).First();

        var parts = new List<string>();

        // Only when somebody actually set one. In a session where nobody
        // completed a flying lap - a red flag, an early crash - every
        // BestLapMs is 0 and Time(0) renders "-", so this line used to
        // read "Quickest single lap: Sam at -." and name a driver for a
        // lap that does not exist.
        if (quickest.BestLapMs > 0)
        {
            parts.Add($"Quickest single lap: {NameOf(quickest)} at {PaceAnalysis.Time(quickest.BestLapMs)}.");
        }

        if (tidiest is not null)
        {
            parts.Add($"Tidiest: {NameOf(tidiest)} at {tidiest.ConsistencyPercent:0.0}% spread.");
        }

        if (mostLeft.PotentialGainMs >= 1000)
        {
            parts.Add($"Most left on the table: {NameOf(mostLeft)}, {mostLeft.PotentialGainMs / 1000.0:0.0}s "
                      + "against repeating their own best lap.");
        }

        parts.Add("Sorted by the best three laps in a row, the pace each driver could actually hold.");

        _lblSummary.Text = string.Join("   ", parts);
    }
}
