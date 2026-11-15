namespace HeatCaller;

/// <summary>
/// Who beats whom at the club, and any two drivers side by side. Only
/// sessions containing BOTH drivers of a pair count, since a race one of
/// them sat out says nothing about how they match up.
///
/// 13 Nov 2026, the Race Analytics rework (option D's Head to Head, see
/// RaceCoach): the page opens on a grid of every regular against every
/// other, so a driver finds where they stand without picking anyone, and a
/// click on a square opens that pair beside it - the score, the four
/// measures that decide a matchup, and what they add up to in a sentence.
/// The two pickers stay as the way to reach anyone the grid leaves out, and
/// the races one by one and the full career comparison stay under them.
/// The sums are RaceCoach's (RaceCoach.HeadToHead.cs), so they can be
/// tested without a window.
/// </summary>
public sealed class HeadToHeadControl : UserControl
{
    /// <summary>The track layouts, for a layout's length - the car lengths beside each gap (RaceCoach). Null says no car lengths.</summary>
    public TrackLayoutStore? Layouts { get; set; }

    /// <summary>The profiles, so drivers the club has hidden stay out of the who-beats-whom grid. Null hides nobody.</summary>
    public ProfileStore? Profiles { get; set; }

    private readonly SessionHistoryStore _history;

    private readonly ComboBox _cmbBeat = new();
    private readonly ComboBox _cmbSince = new();
    private readonly WhoBeatsWhomGrid _matrixView = new();

    private readonly ComboBox _cmbDriverA = new();
    private readonly ComboBox _cmbDriverB = new();
    private readonly Label _lblPair = new();
    private readonly Label _lblScore = new();
    private readonly Label _lblScoreCaption = new();
    private readonly Label _valQuicker = new();
    private readonly Label _valAhead = new();
    private readonly Label _valFastest = new();
    private readonly Label _valLastFive = new();
    private readonly Label _valGap = new();
    private Label? _capGap;
    private readonly Label _lblReading = new();

    private readonly DataGridView _grid = new();
    private readonly DataGridView _statsGrid = new();

    /// <summary>The races the page counts at the "since" chosen, oldest first - see RaceCoach.Fields.</summary>
    private List<RaceCoach.SharedField> _fields = new();

    /// <summary>The pair on show, as picked.</summary>
    private string _nameA = string.Empty;
    private string _nameB = string.Empty;

    /// <summary>Set while the page moves the pickers itself, so they do not each start a comparison of their own.</summary>
    private bool _syncing;

    /// <summary>The choices in the Count picker, in order.</summary>
    private static readonly (string Text, RaceCoach.Beat Beat)[] BeatChoices =
    {
        ("Finishing ahead", RaceCoach.Beat.FinishedAhead),
        ("Quicker best lap", RaceCoach.Beat.QuickerBestLap),
    };

    /// <summary>The choices in the Since picker, in order.</summary>
    private static readonly (string Text, RaceCoach.Since Since)[] SinceChoices =
    {
        ("All time", RaceCoach.Since.AllTime),
        ("This year", RaceCoach.Since.ThisYear),
        ("Last 3 months", RaceCoach.Since.LastThreeMonths),
    };

    public HeadToHeadControl(SessionHistoryStore history)
    {
        _history = history;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise
        // take. No MinimumSize: the 900x790 floor was taller than a 1080p
        // screen has room for, so the bottom of the page was never in view.
        // A short window scrolls the page instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The Count and Since pickers' least width at the normal text size.</summary>
    private const int ChoicePickerWidth = 150;

    /// <summary>The matchup column's width at the normal text size - about two fifths of the page, between these.</summary>
    private const int MatchupLeastWidth = 340;
    private const int MatchupMostWidth = 460;

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. Forgotten when the lists are refilled.</summary>
    private (Font Font, int Count, int Width)? _widthBeat;
    private (Font Font, int Count, int Width)? _widthSince;

    /// <summary>What is being counted, as picked.</summary>
    public RaceCoach.Beat Counting => BeatChoices[Math.Max(0, _cmbBeat.SelectedIndex)].Beat;

    /// <summary>How far back the page looks, as picked.</summary>
    public RaceCoach.Since Looking => SinceChoices[Math.Max(0, _cmbSince.SelectedIndex)].Since;

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
    /// The intro and what to count; then the grid, with the races one by
    /// one under it, beside the matchup card with the career comparison
    /// under that. On the layout pieces (LayoutPieces.cs), so nothing is
    /// placed by hand.
    ///
    /// The grid is the page's lead, so it takes the wide side. The matchup
    /// card is narrow enough to read down, like the mock-up's, and the
    /// comparison's three columns fit under it; the races' seven do not,
    /// so they go under the grid.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Who beats whom at the club, in races they both ran. Click a square to open that pair.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- What to count ---
        _cmbBeat.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbBeat.Items.AddRange(BeatChoices.Select(c => (object)c.Text).ToArray());
        _cmbBeat.SelectedIndex = 0;
        _cmbBeat.SelectedIndexChanged += (_, _) => Recount();

        _cmbSince.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSince.Items.AddRange(SinceChoices.Select(c => (object)c.Text).ToArray());
        _cmbSince.SelectedIndex = 0;
        _cmbSince.SelectedIndexChanged += (_, _) => RefreshAll();

        var countRow = new FieldRow();

        countRow.Add(new Label { Text = "Count:" }, _cmbBeat, () => ChoiceWidth(_cmbBeat, ref _widthBeat));
        countRow.Add(new Label { Text = "Since:" }, _cmbSince, () => ChoiceWidth(_cmbSince, ref _widthSince));

        // --- The grid ---
        _matrixView.PairPicked += (row, column) => Pick(row, column);

        // The grid draws in the theme's faces, which a new text size
        // replaces: measured again, and the card with it.
        _textSize.Keep(() =>
        {
            _matrixView.Invalidate();
            _matrixView.Parent?.PerformLayout();
        });

        var lblKey = new Label
        {
            Text = "Each square is the row driver's wins and losses against the column driver. "
                + "Green wins 60% or more, red 40% or less.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var gridCard = new Section("Who beats whom");

        gridCard.Add(_matrixView, gapBelow: UiLayout.HintGap);
        gridCard.Add(lblKey, gapBelow: 0);

        // --- The matchup ---
        _textSize.Wear(_lblPair, () => AppTheme.BoldFont);
        _lblPair.AutoSize = true;

        _lblScore.AutoSize = true;
        _lblScore.ForeColor = AppTheme.Accent;
        _textSize.Wear(_lblScore, () => AppTheme.StatFont);

        _lblScoreCaption.AutoSize = true;
        _lblScoreCaption.ForeColor = AppTheme.TextMuted;

        var deciding = new FieldGrid();

        foreach (var value in new[] { _valQuicker, _valAhead, _valFastest, _valLastFive, _valGap })
        {
            value.AutoSize = true;
            _textSize.Wear(value, () => AppTheme.BoldFont);
        }

        deciding.AddRow("Quicker best lap", _valQuicker, stretch: true);
        deciding.AddRow("Finished ahead", _valAhead, stretch: true);
        deciding.AddRow("Fastest lap of the race", _valFastest, stretch: true);
        deciding.AddRow("Last 5", _valLastFive, stretch: true);
        _capGap = deciding.AddRow("Best-lap gap", _valGap, stretch: true);

        _lblReading.AutoSize = true;
        _lblReading.ForeColor = AppTheme.TextDark;

        _cmbDriverA.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverA.SelectedIndexChanged += (_, _) => PickedByName();

        _cmbDriverB.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverB.SelectedIndexChanged += (_, _) => PickedByName();

        var lblPickers = new Label
        {
            Text = "Or pick any two drivers:",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var pickers = new FieldGrid();

        pickers.AddRow("Driver A:", _cmbDriverA, stretch: true);
        pickers.AddRow("Driver B:", _cmbDriverB, stretch: true);

        var matchup = new Section("Matchup");

        matchup.Add(_lblPair, gapBelow: 0);
        matchup.Add(_lblScore, gapBelow: 0);
        matchup.Add(_lblScoreCaption);
        matchup.Add(deciding);
        matchup.Add(_lblReading, gapBelow: UiLayout.SectionGap / 2);
        matchup.Add(lblPickers, gapBelow: UiLayout.HintGap);
        matchup.Add(pickers, gapBelow: 0);

        // --- The comparison, and the races one by one ---
        ConfigureGrid(_statsGrid);
        _statsGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "metric", HeaderText = "Metric", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "a", HeaderText = "Driver A", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "b", HeaderText = "Driver B", FillWeight = 50 }
        );

        ConfigureGrid(_grid);
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "date", HeaderText = "Date", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "track", HeaderText = "Track", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "posA", HeaderText = "A Pos", FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "posB", HeaderText = "B Pos", FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "bestA", HeaderText = "A Best", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "bestB", HeaderText = "B Best", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "winner", HeaderText = "Ahead", FillWeight = 50 }
        );

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_statsGrid));
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));

        var races = new Section("Session by session");

        races.AddFill(_grid, () => _grid.ColumnHeadersHeight + 4 * _grid.RowTemplate.Height, gapBelow: 0);

        var comparison = new Section("Career comparison (shared sessions only)");

        comparison.AddFill(_statsGrid, () => _statsGrid.ColumnHeadersHeight + 4 * _statsGrid.RowTemplate.Height, gapBelow: 0);

        Beside? band = null;

        band = new Beside(() => MatchupWidth(band?.Width ?? 0));

        band.Add(gridCard, gapBelow: UiLayout.SectionGap);
        band.AddFill(races, races.LeastHeight);
        band.AddSideCard(matchup, gapBelow: UiLayout.SectionGap);
        band.AddSideFill(comparison, comparison.LeastHeight, gapBelow: 0);

        // --- The page ---
        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(countRow);
        page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>A Count or Since picker: as wide as its longest choice, and never under its width at the normal text size.</summary>
    private static int ChoiceWidth(ComboBox picker, ref (Font Font, int Count, int Width)? kept) =>
        Math.Max(UiLayout.AtTextSize(ChoicePickerWidth), Pieces.PickerWidth(picker, ref kept));

    /// <summary>The matchup column: about two fifths of the page, between 340px and 460px at the normal text size, at the size in use.</summary>
    private static int MatchupWidth(int page) =>
        Math.Clamp((int)(page * 0.4f), UiLayout.AtTextSize(MatchupLeastWidth), UiLayout.AtTextSize(MatchupMostWidth));

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    }

    /// <summary>The grid as drawn - for the tests.</summary>
    internal RaceCoach.Matrix? Matrix => _matrixView.Matrix;

    /// <summary>The grid's control - for the tests, to click a square.</summary>
    internal WhoBeatsWhomGrid Grid => _matrixView;

    /// <summary>The pair on show and what the card says about them - for the tests.</summary>
    internal (string A, string B, string Score, string Reading) Showing => (_nameA, _nameB, _lblScore.Text, _lblReading.Text);

    /// <summary>The deciding rows as shown, top to bottom - for the tests.</summary>
    internal IReadOnlyList<string> Deciding => new[] { _valQuicker.Text, _valAhead.Text, _valFastest.Text, _valLastFive.Text, _valGap.Text };

    /// <summary>The rows of the full comparison and of the races one by one - for the tests.</summary>
    internal (int Comparison, int Races) DetailRows => (_statsGrid.Rows.Count, _grid.Rows.Count);

    /// <summary>The names in Driver A's picker, the placeholder left out - for the tests.</summary>
    internal IReadOnlyList<string> PickerNames => _cmbDriverA.Items.Cast<object>().Skip(1).Select(o => o.ToString() ?? "").ToList();

    /// <summary>Picks what to count and how far back - for the tests, as a click on the pickers would.</summary>
    internal void Choose(RaceCoach.Beat beat, RaceCoach.Since since)
    {
        _cmbBeat.SelectedIndex = Array.FindIndex(BeatChoices, c => c.Beat == beat);
        _cmbSince.SelectedIndex = Array.FindIndex(SinceChoices, c => c.Since == since);
    }

    /// <summary>Call when this page becomes visible - new sessions can be recorded while it's not active.</summary>
    public void RefreshAll()
    {
        var previousA = _cmbDriverA.SelectedItem as string;
        var previousB = _cmbDriverB.SelectedItem as string;

        _fields = RaceCoach.Fields(_history.All, RaceCoach.SinceUtc(Looking, DateTime.Now));

        // Every real driver in a counted race, for the pickers - the grid
        // shows the regulars, and these reach everyone else. Saved demo
        // races are left out (RaceCoach.Fields): the results in them are
        // simulated and the names made up. Ghost cars are left out too, by
        // transponder: a ghost is a pace reference, not a driver anyone
        // can be compared against.
        var drivers = _fields
            .SelectMany(f => f.Finishers)
            .Select(f => f.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _syncing = true;

        try
        {
            foreach (var combo in new[] { _cmbDriverA, _cmbDriverB })
            {
                combo.Items.Clear();
                combo.Items.Add("(select a driver)");
                foreach (var d in drivers)
                {
                    combo.Items.Add(d);
                }
            }

            BuildMatrix();

            // The pair that was on show, or the grid's top two, so the card
            // is never empty when there is a grid to open it from.
            var fallback = _matrixView.Matrix is { Drivers.Count: >= 2 } m ? (m.Drivers[0], m.Drivers[1]) : ((string?)null, (string?)null);

            if (!RestoreSelection(_cmbDriverA, previousA) | !RestoreSelection(_cmbDriverB, previousB))
            {
                RestoreSelection(_cmbDriverA, fallback.Item1);
                RestoreSelection(_cmbDriverB, fallback.Item2);
            }
        }
        finally
        {
            _syncing = false;
        }

        Compare();
    }

    /// <summary>The grid again, on the races and the measure in use.</summary>
    private void BuildMatrix()
    {
        // Races only (bug hunt 21): see RaceCoach.RacesOnly. The career table below keeps practice, labelled.
        _matrixView.Show(RaceCoach.BuildMatrix(RaceCoach.RacesOnly(_fields), Counting, Profiles), Counting);
        _matrixView.Parent?.PerformLayout();
    }

    /// <summary>A new measure: the grid and the card's score count it; the races do not change.</summary>
    private void Recount()
    {
        BuildMatrix();
        Compare();
    }

    /// <summary>True when <paramref name="previous"/> is in the list and now picked; otherwise the placeholder is.</summary>
    private static bool RestoreSelection(ComboBox combo, string? previous)
    {
        var idx = -1;

        for (var i = 1; previous is not null && i < combo.Items.Count; i++)
        {
            if (string.Equals(combo.Items[i] as string, previous.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        combo.SelectedIndex = idx >= 0 ? idx : 0;
        return idx >= 0;
    }

    /// <summary>A square of the grid was clicked: that pair, row driver first, in the pickers and the card.</summary>
    private void Pick(string a, string b)
    {
        _syncing = true;

        try
        {
            RestoreSelection(_cmbDriverA, a);
            RestoreSelection(_cmbDriverB, b);
        }
        finally
        {
            _syncing = false;
        }

        Compare();
    }

    /// <summary>A picker moved by hand.</summary>
    private void PickedByName()
    {
        if (!_syncing)
        {
            Compare();
        }
    }

    /// <summary>The matchup card cleared, saying why.</summary>
    private void ClearCard(string why)
    {
        _lblPair.Text = "Matchup";
        _lblScore.Text = "–";
        _lblScoreCaption.Text = string.Empty;

        foreach (var value in new[] { _valQuicker, _valAhead, _valFastest, _valLastFive, _valGap })
        {
            value.Text = "-";
        }

        _lblReading.Text = why;
        _lblReading.ForeColor = AppTheme.TextMuted;
    }

    /// <summary>The card: the score on the measure in use, the deciding rows, and what they add up to.</summary>
    private void FillCard()
    {
        var pair = RaceCoach.Pair(RaceCoach.RacesOnly(_fields), _nameA, _nameB);
        var (shortA, shortB) = RaceCoach.ShortNames(_nameA, _nameB);
        var (forA, forB) = pair.On(Counting);
        var trackCars = RaceCoach.TrackCars(Layouts, pair.Track);

        _lblPair.Text = $"{_nameA} vs {_nameB}";
        _lblScore.Text = $"{forA}–{forB}";
        _lblScoreCaption.Text = pair.Shared == 0
            ? "No races together."
            : $"{shortA}'s wins first. {pair.Shared} shared race{(pair.Shared == 1 ? "" : "s")}, "
              + (Counting == RaceCoach.Beat.QuickerBestLap ? "counting the quicker best lap." : "counting who finished ahead.");

        _valQuicker.Text = RaceCoach.Leads(_nameA, _nameB, pair.QuickerA, pair.QuickerB);
        _valAhead.Text = RaceCoach.Leads(_nameA, _nameB, pair.AheadA, pair.AheadB);
        _valFastest.Text = RaceCoach.Leads(_nameA, _nameB, pair.FastestOfRaceA, pair.FastestOfRaceB);
        _valLastFive.Text = RaceCoach.Leads(_nameA, _nameB, pair.LastFiveA, pair.LastFiveB);
        _valGap.Text = RaceCoach.BestLapGap(pair, trackCars) ?? "-";

        if (_capGap is not null)
        {
            _capGap.Text = pair.Track.Length > 0 ? $"Best-lap gap, {pair.Track}" : "Best-lap gap";
        }

        _lblReading.Text = RaceCoach.Reading(pair, trackCars);
        _lblReading.ForeColor = AppTheme.TextDark;
    }

    private void Compare()
    {
        if (_syncing)
        {
            return;
        }

        _grid.Rows.Clear();
        _statsGrid.Rows.Clear();

        _nameA = _cmbDriverA.SelectedIndex > 0 ? _cmbDriverA.SelectedItem as string ?? "" : "";
        _nameB = _cmbDriverB.SelectedIndex > 0 ? _cmbDriverB.SelectedItem as string ?? "" : "";

        _matrixView.Select(_nameA, _nameB);

        if (_nameA.Length == 0 || _nameB.Length == 0)
        {
            // Just the placeholder in the list means there is nobody to
            // pick - say why rather than asking for a choice that can't be made.
            ClearCard(_cmbDriverA.Items.Count <= 1
                ? "No saved races for real drivers yet. Demo races and ghost cars aren't counted."
                : "Click a square in the grid, or pick two drivers below.");
            return;
        }

        if (string.Equals(_nameA, _nameB, StringComparison.OrdinalIgnoreCase))
        {
            ClearCard("Pick two different drivers.");
            _nameA = _nameB = string.Empty;
            return;
        }

        _statsGrid.Columns["a"]!.HeaderText = RaceCoach.ShortNames(_nameA, _nameB).A;
        _statsGrid.Columns["b"]!.HeaderText = RaceCoach.ShortNames(_nameA, _nameB).B;

        FillCard();

        var winsA = 0;
        var winsB = 0;
        var bestLapsA = new List<long>();
        var bestLapsB = new List<long>();
        var allLapsA = new List<long>();
        var allLapsB = new List<long>();
        var positionsA = new List<int>();
        var positionsB = new List<int>();
        var lapsA = 0;
        var lapsB = 0;
        var fastestLapWinsA = 0;
        var fastestLapWinsB = 0;
        var biggestMarginA = 0;
        var biggestMarginB = 0;
        var closestGap = int.MaxValue;
        var results = new List<bool>(); // true = A ahead
        DateTime? firstMeeting = null;
        DateTime? lastMeeting = null;

        var outrightWinsA = 0;
        var outrightWinsB = 0;
        var podiumsA = 0;
        var podiumsB = 0;
        var comebackWinsA = 0;
        var comebackWinsB = 0;
        var mostLapsA = 0;
        var mostLapsB = 0;
        var trackWinsA = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var trackWinsB = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var carsA = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var carsB = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var raceMeetings = 0;
        var practiceMeetings = 0;

        // Oldest first, so the session list and form guide read
        // chronologically - RaceCoach.Fields' order.
        //
        // Ghosts skipped, and the field CLOSED UP behind them, by
        // RaceCoach.Fields. A ghost is a pace reference, not an entrant, and
        // it takes a finishing position in the saved result like anybody
        // else; counted off the recorded Position, a driver who won the race
        // outright read as second whenever a ghost beat them. The same
        // closing-up AchievementEvaluator does for the same reason - see
        // PlaceOf there.
        //
        // 17 Oct 2026, Bug Hunt 8, A30-3: and only cars that turned a lap.
        // A hand-released grid saves 0-lap rows for cars that never moved,
        // so a driver who sat on the line handed the other a "win". A driver
        // with no laps is not found, and the session is skipped - the same
        // as WinnerOf / PlaceOf.
        foreach (var field in _fields)
        {
            var session = field.Session;

            if (field.Find(_nameA) is not { } fa || field.Find(_nameB) is not { } fb)
            {
                continue;
            }

            var rA = fa.Entry;
            var rB = fb.Entry;
            var placeA = fa.Place;
            var placeB = fb.Place;

            var aAhead = placeA < placeB;
            results.Add(aAhead);
            if (aAhead) { winsA++; } else { winsB++; }

            var margin = Math.Abs(placeA - placeB);
            if (aAhead) { biggestMarginA = Math.Max(biggestMarginA, margin); }
            else { biggestMarginB = Math.Max(biggestMarginB, margin); }
            closestGap = Math.Min(closestGap, margin);

            if (rA.FastLapMs > 0) { bestLapsA.Add(rA.FastLapMs); }
            if (rB.FastLapMs > 0) { bestLapsB.Add(rB.FastLapMs); }
            // Flying laps only (see PaceAnalysis.CountedLaps): the first
            // entry is the dash from the green to the first crossing, and it
            // pulled the average lap down and the spread up. StartSlots, not
            // Skip(1) (bug hunt 21): a credited start lap's dash is the next
            // entry, after a zero.
            allLapsA.AddRange(rA.LapTimesMs.Skip(rA.StartSlots(rA.LapTimesMs)).Where(l => l > 0));
            allLapsB.AddRange(rB.LapTimesMs.Skip(rB.StartSlots(rB.LapTimesMs)).Where(l => l > 0));

            if (rA.FastLapMs > 0 && rB.FastLapMs > 0)
            {
                if (rA.FastLapMs < rB.FastLapMs) { fastestLapWinsA++; } else { fastestLapWinsB++; }
            }

            positionsA.Add(placeA);
            positionsB.Add(placeB);
            lapsA += rA.Laps;
            lapsB += rB.Laps;

            if (placeA == 1) { outrightWinsA++; }
            if (placeB == 1) { outrightWinsB++; }
            if (placeA <= 3) { podiumsA++; }
            if (placeB <= 3) { podiumsB++; }

            // Finished ahead despite the OTHER driver setting the quicker
            // single lap - raw pace lost to racecraft or consistency.
            if (rA.FastLapMs > 0 && rB.FastLapMs > 0)
            {
                if (aAhead && rA.FastLapMs > rB.FastLapMs) { comebackWinsA++; }
                if (!aAhead && rB.FastLapMs > rA.FastLapMs) { comebackWinsB++; }
            }

            mostLapsA = Math.Max(mostLapsA, rA.Laps);
            mostLapsB = Math.Max(mostLapsB, rB.Laps);

            var track = string.IsNullOrWhiteSpace(session.TrackLayoutName) ? "(no track)" : session.TrackLayoutName;
            var winTally = aAhead ? trackWinsA : trackWinsB;
            winTally[track] = winTally.GetValueOrDefault(track) + 1;

            if (!string.IsNullOrWhiteSpace(rA.CarModel)) { carsA[rA.CarModel] = carsA.GetValueOrDefault(rA.CarModel) + 1; }
            if (!string.IsNullOrWhiteSpace(rB.CarModel)) { carsB[rB.CarModel] = carsB.GetValueOrDefault(rB.CarModel) + 1; }

            if (session.SessionType == RaceSessionType.Practice) { practiceMeetings++; } else { raceMeetings++; }

            firstMeeting ??= session.StartedAtUtc;
            lastMeeting = session.StartedAtUtc;

            var rowIndex = _grid.Rows.Add(
                session.StartedAtUtc.ToLocalTime().ToString("d"),
                string.IsNullOrWhiteSpace(session.TrackLayoutName) ? "-" : session.TrackLayoutName,
                placeA, placeB,
                rA.FastLapMs > 0 ? $"{rA.FastLapMs / 1000.0:0.000}" : "-",
                rB.FastLapMs > 0 ? $"{rB.FastLapMs / 1000.0:0.000}" : "-",
                aAhead ? _nameA : _nameB);
            _grid.Rows[rowIndex].Cells["winner"].Style.ForeColor = aAhead ? AppTheme.Primary : AppTheme.Success;
        }

        var total = winsA + winsB;
        if (total == 0)
        {
            return;
        }

        AddStat("Sessions won", winsA.ToString(), winsB.ToString());
        AddStat("Win rate", $"{winsA * 100.0 / total:0}%", $"{winsB * 100.0 / total:0}%");
        AddStat("Current streak", StreakText(results, forA: true), StreakText(results, forA: false));
        AddStat("Longest win streak", LongestStreak(results, true).ToString(), LongestStreak(results, false).ToString());
        AddStat("Recent form (last 5)", FormText(results, true), FormText(results, false));
        AddStat("Fastest lap of session", fastestLapWinsA.ToString(), fastestLapWinsB.ToString());
        AddStat("Best lap ever", Fmt(bestLapsA.Count > 0 ? bestLapsA.Min() : 0), Fmt(bestLapsB.Count > 0 ? bestLapsB.Min() : 0));
        AddStat("Average best lap", Fmt(bestLapsA.Count > 0 ? (long)bestLapsA.Average() : 0), Fmt(bestLapsB.Count > 0 ? (long)bestLapsB.Average() : 0));
        AddStat("Consistency (std dev)", StdDevText(allLapsA), StdDevText(allLapsB));
        AddStat("Average finish position", positionsA.Count > 0 ? $"P{positionsA.Average():0.0}" : "-", positionsB.Count > 0 ? $"P{positionsB.Average():0.0}" : "-");
        AddStat("Biggest winning margin", biggestMarginA > 0 ? $"{biggestMarginA} place{(biggestMarginA == 1 ? "" : "s")}" : "-", biggestMarginB > 0 ? $"{biggestMarginB} place{(biggestMarginB == 1 ? "" : "s")}" : "-");
        AddStat("Outright wins (P1)", outrightWinsA.ToString(), outrightWinsB.ToString());
        AddStat("Podiums (P1-P3)", podiumsA.ToString(), podiumsB.ToString());
        AddStat("Wins without fastest lap", comebackWinsA.ToString(), comebackWinsB.ToString());
        AddStat("Pace gap on best lap", PaceGapText(bestLapsA, bestLapsB), PaceGapText(bestLapsB, bestLapsA));
        AddStat("Average lap (all laps)", allLapsA.Count > 0 ? Fmt((long)allLapsA.Average()) : "-", allLapsB.Count > 0 ? Fmt((long)allLapsB.Average()) : "-");
        AddStat("Improvement over time", ImprovementText(bestLapsA), ImprovementText(bestLapsB));
        AddStat("Strongest track", TopKey(trackWinsA), TopKey(trackWinsB));
        AddStat("Most-used car", TopKey(carsA), TopKey(carsB));
        AddStat("Most laps in a session", mostLapsA.ToString(), mostLapsB.ToString());
        AddStat("Total laps", lapsA.ToString(), lapsB.ToString());
        AddStat("Average laps per session", $"{lapsA / (double)total:0.0}", $"{lapsB / (double)total:0.0}");

        var span = firstMeeting is { } f && lastMeeting is { } l
            ? $"{f.ToLocalTime():d} to {l.ToLocalTime():d}"
            : "-";
        AddStat("Rivalry spans", span, span);
        var split = $"{raceMeetings} race{(raceMeetings == 1 ? "" : "s")}, {practiceMeetings} practice";
        AddStat("Meeting types", split, split);
        AddStat("Closest session finish", closestGap == int.MaxValue ? "-" : $"{closestGap} place{(closestGap == 1 ? "" : "s")} apart", closestGap == int.MaxValue ? "-" : $"{closestGap} place{(closestGap == 1 ? "" : "s")} apart");
    }

    /// <summary>How many sessions in a row this driver has most recently won. Zero if the other driver won the latest meeting.</summary>
    private static string StreakText(List<bool> results, bool forA)
    {
        var streak = 0;
        for (var i = results.Count - 1; i >= 0; i--)
        {
            if (results[i] != forA)
            {
                break;
            }
            streak++;
        }
        return streak == 0 ? "-" : $"W{streak}";
    }

    private static int LongestStreak(List<bool> results, bool forA)
    {
        var best = 0;
        var current = 0;
        foreach (var aAhead in results)
        {
            current = aAhead == forA ? current + 1 : 0;
            best = Math.Max(best, current);
        }
        return best;
    }

    /// <summary>Most recent five meetings, newest last - e.g. "W L W W W".</summary>
    private static string FormText(List<bool> results, bool forA)
    {
        var recent = results.TakeLast(5).Select(aAhead => aAhead == forA ? "W" : "L");
        return string.Join(" ", recent);
    }

    private static string StdDevText(List<long> laps)
    {
        if (laps.Count < 2)
        {
            return "-";
        }

        var avg = laps.Average();
        var stdDev = Math.Sqrt(laps.Sum(t => Math.Pow(t - avg, 2)) / laps.Count);
        return $"{stdDev / 1000.0:0.000}s";
    }



    /// <summary>How far this driver's best lap is off the other's - negative means they hold the advantage.</summary>
    private static string PaceGapText(List<long> mine, List<long> theirs)
    {
        if (mine.Count == 0 || theirs.Count == 0)
        {
            return "-";
        }

        var gap = mine.Min() - theirs.Min();
        if (gap == 0)
        {
            return "level";
        }

        return gap < 0 ? $"{gap / 1000.0:0.000}s ahead" : $"+{gap / 1000.0:0.000}s behind";
    }

    /// <summary>
    /// Average best lap across the earliest third of meetings versus the
    /// latest third - a rough "are they getting quicker" read that ignores
    /// the noise of any single session. Needs at least six meetings to say
    /// anything meaningful.
    /// </summary>
    private static string ImprovementText(List<long> bestLaps)
    {
        if (bestLaps.Count < 6)
        {
            return "-";
        }

        var slice = bestLaps.Count / 3;
        var early = bestLaps.Take(slice).Average();
        var late = bestLaps.TakeLast(slice).Average();
        var delta = late - early;

        if (Math.Abs(delta) < 1)
        {
            return "level";
        }

        return delta < 0 ? $"{Math.Abs(delta) / 1000.0:0.000}s faster" : $"{delta / 1000.0:0.000}s slower";
    }

    private static string TopKey(Dictionary<string, int> tally)
    {
        if (tally.Count == 0)
        {
            return "-";
        }

        var top = tally.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First();
        return $"{top.Key} ({top.Value})";
    }

    private static string Fmt(long ms) => ms > 0 ? $"{ms / 1000.0:0.000}s" : "-";

    private void AddStat(string metric, string a, string b) => _statsGrid.Rows.Add(metric, a, b);
}

/// <summary>
/// The who-beats-whom grid, drawn: a row and a column for each regular,
/// strongest at the top, and in each square the row driver's wins and
/// losses against the column driver, tinted by who is on top. Hover a
/// square for the pair in words; click it to open the pair.
///
/// Drawn rather than a DataGridView: a grid control's cells are text in
/// boxes, and the squares here are tints with a gap round them, sized to
/// the width the card gives (as wide as they need, up to a point, and
/// fewer drivers rather than squares too small to read when the page is
/// narrow). It sizes itself (AutoSize) at the width it is given, so the
/// card round it is as tall as it is.
/// </summary>
internal sealed class WhoBeatsWhomGrid : Control
{
    private RaceCoach.Matrix? _matrix;
    private RaceCoach.Beat _beat;
    private (int Row, int Column)? _selected;
    private (int Row, int Column)? _hover;
    private readonly ToolTip _tip = new() { InitialDelay = 300, ReshowDelay = 100 };

    /// <summary>A square was clicked: the row driver, then the column driver.</summary>
    public event Action<string, string>? PairPicked;

    public WhoBeatsWhomGrid()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        Tag = AppTheme.KeepBackColor;
        AccessibleName = "Who beats whom";
        AccessibleRole = AccessibleRole.Table;
    }

    /// <summary>The grid on show.</summary>
    public RaceCoach.Matrix? Matrix => _matrix;

    /// <summary>A new grid, counting <paramref name="beat"/>.</summary>
    public void Show(RaceCoach.Matrix matrix, RaceCoach.Beat beat)
    {
        _matrix = matrix;
        _beat = beat;
        _hover = null;
        Invalidate();
    }

    /// <summary>Rings the pair's square, when both are in the grid.</summary>
    public void Select(string a, string b)
    {
        var row = _matrix?.IndexOf(a) ?? -1;
        var column = _matrix?.IndexOf(b) ?? -1;

        _selected = row >= 0 && column >= 0 && row != column ? (row, column) : null;
        Invalidate();
    }

    /// <summary>Room round each square, inside its cell.</summary>
    private static int Gap => UiLayout.AtDpi(2);

    private static int HeaderHeight => AppTheme.SmallFont.Height + UiLayout.AtDpi(10);

    private static int RowHeight => AppTheme.BoldFont.Height + UiLayout.AtDpi(14);

    /// <summary>The narrowest a square gets: "10–10" in the bold face, with room.</summary>
    private static int LeastCell => TextRenderer.MeasureText("10–10", AppTheme.BoldFont).Width + UiLayout.AtDpi(10);

    /// <summary>The widest a square gets - wider only spreads the grid thin.</summary>
    private static int MostCell => UiLayout.AtTextSize(76);

    private int NameColumn()
    {
        var widest = _matrix?.Drivers.Select(d => TextRenderer.MeasureText(d, AppTheme.BoldFont).Width).DefaultIfEmpty(0).Max() ?? 0;

        return Math.Min(widest, UiLayout.AtTextSize(150)) + UiLayout.AtDpi(12);
    }

    /// <summary>How many drivers fit across <paramref name="width"/>, and how wide each square is then.</summary>
    private (int Count, int Cell) Fit(int width)
    {
        var drivers = _matrix?.Drivers.Count ?? 0;

        if (drivers == 0)
        {
            return (0, 0);
        }

        var room = Math.Max(0, width - NameColumn());
        var count = Math.Clamp(room / Math.Max(1, LeastCell), 1, drivers);

        return (count, Math.Clamp(room / count, LeastCell, MostCell));
    }

    /// <summary>The regulars left out of the grid - past the cap, or past the width - to name under it.</summary>
    private List<string> LeftOut(int count) =>
        _matrix is null ? new List<string>() : _matrix.Drivers.Skip(count).Concat(_matrix.Others).ToList();

    private string Message =>
        "Nobody has raced " + RaceCoach.RegularRaces + " times against someone else yet. The grid fills in as the club races.";

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : NaturalWidth();
        var (count, _) = Fit(width);

        if (count == 0)
        {
            var text = TextRenderer.MeasureText(Message, AppTheme.BodyFont, new Size(Math.Max(1, width), 0), TextFormatFlags.WordBreak);

            return new Size(width, text.Height + UiLayout.AtDpi(8));
        }

        var height = HeaderHeight + count * RowHeight;

        if (LeftOut(count).Count > 0)
        {
            height += UiLayout.AtDpi(6) + NoteHeight(width, count);
        }

        return new Size(width, height);
    }

    private int NaturalWidth() => NameColumn() + (_matrix?.Drivers.Count ?? 0) * MostCell;

    private string Note(int count)
    {
        var left = LeftOut(count);

        return $"Also racing: {string.Join(", ", left)}. Pick them in the Matchup card.";
    }

    private int NoteHeight(int width, int count) =>
        TextRenderer.MeasureText(Note(count), AppTheme.SmallFont, new Size(Math.Max(1, width), 0), TextFormatFlags.WordBreak).Height;

    /// <summary>The square under a point, or null.</summary>
    private (int Row, int Column)? CellAt(Point point)
    {
        var (count, cell) = Fit(Width);
        var left = NameColumn();

        if (count == 0 || point.X < left || point.Y < HeaderHeight)
        {
            return null;
        }

        var column = (point.X - left) / cell;
        var row = (point.Y - HeaderHeight) / RowHeight;

        return column < count && row < count ? (row, column) : null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var at = CellAt(e.Location);

        if (at == _hover)
        {
            return;
        }

        _hover = at;
        Cursor = at is { } c && c.Row != c.Column ? Cursors.Hand : Cursors.Default;
        _tip.SetToolTip(this, at is { } h ? Describe(h.Row, h.Column) : string.Empty);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (CellAt(e.Location) is { } at && at.Row != at.Column && _matrix is { } m)
        {
            PairPicked?.Invoke(m.Drivers[at.Row], m.Drivers[at.Column]);
        }
    }

    /// <summary>A square clicked, as the mouse would - for the tests.</summary>
    internal void ClickSquare(int row, int column)
    {
        if (_matrix is { } m && row != column && row < m.Drivers.Count && column < m.Drivers.Count)
        {
            PairPicked?.Invoke(m.Drivers[row], m.Drivers[column]);
        }
    }

    /// <summary>A square in words, for its tooltip: "Sam finished ahead of Leo in 9 of 14 races."</summary>
    internal string Describe(int row, int column)
    {
        if (_matrix is not { } m || row == column)
        {
            return string.Empty;
        }

        var (a, b) = RaceCoach.ShortNames(m.Drivers[row], m.Drivers[column]);
        var cell = m[row, column];

        if (cell.Races == 0)
        {
            return $"{a} and {b} have no races to count together.";
        }

        return _beat == RaceCoach.Beat.QuickerBestLap
            ? $"{a} set a quicker best lap than {b} in {cell.Wins} of {cell.Races} races."
            : $"{a} finished ahead of {b} in {cell.Wins} of {cell.Races} races.";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        using (var back = new SolidBrush(Parent?.BackColor ?? AppTheme.Surface))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        var (count, cell) = Fit(Width);

        if (count == 0 || _matrix is not { } m)
        {
            TextRenderer.DrawText(g, Message, AppTheme.BodyFont, ClientRectangle, AppTheme.TextMuted, TextFormatFlags.WordBreak | TextFormatFlags.Left);
            return;
        }

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var nameColumn = NameColumn();
        var header = HeaderHeight;
        var rowHeight = RowHeight;
        const TextFormatFlags centred = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        const TextFormatFlags name = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        // The column heads: first names, the picked pair's column in the accent.
        for (var j = 0; j < count; j++)
        {
            var shortName = RaceCoach.FirstName(m.Drivers[j]);
            var picked = _selected is { } s && s.Column == j;

            TextRenderer.DrawText(g, shortName, picked ? AppTheme.BoldFont : AppTheme.SmallFont,
                new Rectangle(nameColumn + j * cell, 0, cell, header), picked ? AppTheme.Accent : AppTheme.TextMuted, centred);
        }

        for (var i = 0; i < count; i++)
        {
            var top = header + i * rowHeight;
            var picked = _selected is { } s && s.Row == i;

            TextRenderer.DrawText(g, m.Drivers[i], AppTheme.BoldFont,
                new Rectangle(0, top, nameColumn - UiLayout.AtDpi(8), rowHeight), picked ? AppTheme.Accent : AppTheme.TextDark, name);

            for (var j = 0; j < count; j++)
            {
                var square = new Rectangle(nameColumn + j * cell + Gap, top + Gap, cell - Gap * 2, rowHeight - Gap * 2);

                if (i == j)
                {
                    using var blank = new SolidBrush(RosterInk.Blend(AppTheme.Surface, AppTheme.Border, 0.35f));
                    FillRound(g, blank, square);
                    continue;
                }

                var value = m[i, j];
                var (fill, ink) = Colours(RaceCoach.TintOf(value));

                using (var brush = new SolidBrush(fill))
                {
                    FillRound(g, brush, square);
                }

                TextRenderer.DrawText(g, value.Races == 0 ? "-" : $"{value.Wins}–{value.Losses}",
                    AppTheme.BoldFont, square, ink, centred);

                var ringed = _selected is { } sel && sel.Row == i && sel.Column == j;
                var hovered = _hover is { } hov && hov.Row == i && hov.Column == j;

                if (ringed || hovered)
                {
                    using var pen = new Pen(ringed ? AppTheme.Accent : AppTheme.TextMuted, UiLayout.AtDpi(ringed ? 2 : 1));
                    var ring = Rectangle.Inflate(square, -1, -1);
                    g.DrawRectangle(pen, ring);
                }
            }
        }

        if (LeftOut(count).Count > 0)
        {
            var noteTop = header + count * rowHeight + UiLayout.AtDpi(6);

            TextRenderer.DrawText(g, Note(count), AppTheme.SmallFont,
                new Rectangle(0, noteTop, Width, Math.Max(1, Height - noteTop)), AppTheme.TextMuted, TextFormatFlags.WordBreak | TextFormatFlags.Left);
        }
    }

    /// <summary>A square's fill and its figures' colour: green, amber or red over the card, or the card's own for no races.</summary>
    private static (Color Fill, Color Ink) Colours(RaceCoach.Tint tint)
    {
        var tone = tint switch
        {
            RaceCoach.Tint.Winning => AppTheme.Success,
            RaceCoach.Tint.Losing => AppTheme.Danger,
            RaceCoach.Tint.Even => AppTheme.Warning,
            _ => (Color?)null,
        };

        return tone is Color c
            ? (RosterInk.Blend(AppTheme.Surface, c, 0.2f), RosterInk.Blend(c, AppTheme.TextDark, 0.4f))
            : (RosterInk.Blend(AppTheme.Surface, AppTheme.Border, 0.15f), AppTheme.TextMuted);
    }

    private static void FillRound(Graphics g, Brush brush, Rectangle r)
    {
        var radius = UiLayout.AtDpi(5);

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(r.Left, r.Top, radius * 2, radius * 2, 180, 90);
        path.AddArc(r.Right - radius * 2, r.Top, radius * 2, radius * 2, 270, 90);
        path.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(r.Left, r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
