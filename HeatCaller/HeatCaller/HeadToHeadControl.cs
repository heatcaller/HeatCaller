namespace HeatCaller;

/// <summary>
/// Compares two drivers across every session they both appeared in - who
/// finished ahead more often, and how their pace stacks up. Only sessions
/// containing BOTH drivers count, since a race one of them sat out says
/// nothing about how they match up.
/// </summary>
public sealed class HeadToHeadControl : UserControl
{
    private readonly SessionHistoryStore _history;

    private readonly ComboBox _cmbDriverA = new();
    private readonly ComboBox _cmbDriverB = new();
    private readonly Label _lblSummary = new();
    private readonly DataGridView _grid = new();
    private readonly DataGridView _statsGrid = new();
    private readonly Panel _pnlWinBar = new();

    // Populated by Compare() and read by the win-bar's paint handler.
    private string _nameA = string.Empty;
    private string _nameB = string.Empty;
    private int _winsA;
    private int _winsB;

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

    /// <summary>The driver pickers' width at the normal text size - never narrower, and as wide as the longest name.</summary>
    private const int DriverPickerWidth = 220;

    /// <summary>The comparison card's width at the normal text size - about two fifths of the page, between these.</summary>
    private const int ComparisonLeastWidth = 340;
    private const int ComparisonMostWidth = 520;

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. Forgotten when the lists are refilled.</summary>
    private (Font Font, int Count, int Width)? _widthA;
    private (Font Font, int Count, int Width)? _widthB;

    /// <summary>The two pickers - held so refilled lists can re-measure the line.</summary>
    private FieldRow? _pickRow;

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
    /// The intro; a card for the matchup - the two pickers, who leads, and
    /// the wins as a bar; and under it the races one by one beside the
    /// career comparison, both taking the height that is left. On the
    /// layout pieces (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be one tall column placed by hand over a 900x790 floor,
    /// the comparison's twenty rows in a fixed 292px box over the races -
    /// most of both below the fold on a 1080p screen. Side by side, each
    /// gets the page's height.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Pick two drivers to see how they stack up. Only sessions where both raced are counted - a race one of " +
                   "them sat out doesn't say anything about the matchup.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- The matchup ---
        _cmbDriverA.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverA.SelectedIndexChanged += (_, _) => Compare();

        _cmbDriverB.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverB.SelectedIndexChanged += (_, _) => Compare();

        var lblVs = new Label { Text = "vs", AutoSize = true };
        _textSize.Wear(lblVs, () => AppTheme.BoldFont);

        var pickRow = new FieldRow();

        pickRow.Add(new Label { Text = "Driver A:" }, _cmbDriverA, () => PickerWidth(_cmbDriverA, ref _widthA));
        pickRow.Add(null, lblVs);
        pickRow.Add(new Label { Text = "Driver B:" }, _cmbDriverB, () => PickerWidth(_cmbDriverB, ref _widthB));
        _pickRow = pickRow;

        // Who leads, in the heading face - wrapping rather than cut short.
        _lblSummary.AutoSize = true;
        _lblSummary.ForeColor = AppTheme.Accent;
        _textSize.Wear(_lblSummary, () => AppTheme.HeadingFont);

        // The wins as a bar, as tall as its names in the bold face.
        _pnlWinBar.Paint += WinBar_Paint;
        _pnlWinBar.Resize += (_, _) => _pnlWinBar.Invalidate();
        AppTheme.EnableDoubleBuffering(_pnlWinBar);

        _textSize.Keep(() =>
        {
            var height = WinBarHeight + WinBarPad * 2;

            if (_pnlWinBar.Height != height)
            {
                _pnlWinBar.Height = height;
            }
        });

        var matchup = new Section("Matchup");

        matchup.Add(pickRow);
        matchup.Add(_lblSummary, gapBelow: UiLayout.HintGap);
        matchup.Add(_pnlWinBar, gapBelow: 0);

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

        band = new Beside(() => ComparisonWidth(band?.Width ?? 0));

        band.AddFill(races, races.LeastHeight);
        band.AddSideFill(comparison, comparison.LeastHeight, gapBelow: 0);

        // --- The page ---
        var page = new FillPage();

        page.Add(lblIntro);
        page.Add(matchup);
        page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>A driver picker: as wide as the longest name, and never under its width at the normal text size.</summary>
    private static int PickerWidth(ComboBox picker, ref (Font Font, int Count, int Width)? kept) =>
        Math.Max(UiLayout.AtTextSize(DriverPickerWidth), Pieces.PickerWidth(picker, ref kept));

    /// <summary>The comparison's card: about two fifths of the page, between 340px and 520px at the normal text size, at the size in use.</summary>
    private static int ComparisonWidth(int page) =>
        Math.Clamp((int)(page * 0.4f), UiLayout.AtTextSize(ComparisonLeastWidth), UiLayout.AtTextSize(ComparisonMostWidth));

    /// <summary>The bar itself: its names in the bold face, with room above and below them.</summary>
    private static int WinBarHeight => AppTheme.BoldFont.Height + 8;

    /// <summary>Room above and below the bar, inside its panel.</summary>
    private const int WinBarPad = 6;

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

    /// <summary>Call when this page becomes visible - new sessions can be recorded while it's not active.</summary>
    public void RefreshAll()
    {
        var previousA = _cmbDriverA.SelectedItem as string;
        var previousB = _cmbDriverB.SelectedItem as string;

        // Saved demo races are left out: the results in them are
        // simulated, so they must not decide who is ahead in a real
        // head-to-head, and the names in them are made up.
        //
        // Ghost cars are left out too, by transponder: older saved sessions
        // still carry them, and a ghost is a pace reference, not a driver
        // anyone can be compared against.
        var drivers = _history.All
            .Where(s => !s.IsDemoSession())
            .SelectMany(s => s.Results)
            .Where(r => !GhostService.IsGhost(r.Transponder))
            .Select(r => r.DriverName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        foreach (var combo in new[] { _cmbDriverA, _cmbDriverB })
        {
            combo.Items.Clear();
            combo.Items.Add("(select a driver)");
            foreach (var d in drivers)
            {
                combo.Items.Add(d);
            }
        }

        // The longest name may have come or gone: measured again, and the
        // line with it.
        _widthA = null;
        _widthB = null;
        _pickRow?.PerformLayout();

        RestoreSelection(_cmbDriverA, previousA);
        RestoreSelection(_cmbDriverB, previousB);
    }

    private static void RestoreSelection(ComboBox combo, string? previous)
    {
        var idx = previous is not null ? combo.Items.IndexOf(previous) : -1;
        combo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void Compare()
    {
        _grid.Rows.Clear();
        _statsGrid.Rows.Clear();

        _nameA = _cmbDriverA.SelectedIndex > 0 ? _cmbDriverA.SelectedItem as string ?? "" : "";
        _nameB = _cmbDriverB.SelectedIndex > 0 ? _cmbDriverB.SelectedItem as string ?? "" : "";
        _winsA = 0;
        _winsB = 0;
        _pnlWinBar.Invalidate();

        if (_nameA.Length == 0 || _nameB.Length == 0)
        {
            // Just the placeholder in the list means there is nobody to
            // pick - say why rather than asking for a choice that can't be made.
            _lblSummary.Text = _cmbDriverA.Items.Count <= 1
                ? "No saved races for real drivers yet - demo races and ghost cars aren't counted."
                : "Select two drivers to compare.";
            _lblSummary.ForeColor = AppTheme.TextMuted;
            return;
        }

        if (string.Equals(_nameA, _nameB, StringComparison.OrdinalIgnoreCase))
        {
            _lblSummary.Text = "Pick two different drivers.";
            _lblSummary.ForeColor = AppTheme.TextMuted;
            _nameA = _nameB = string.Empty;
            return;
        }

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

        // Oldest first, so the session list and form guide read chronologically.
        foreach (var session in _history.All.Reverse())
        {
            if (session.IsDemoSession())
            {
                continue;
            }

            // Ghosts skipped, and the field CLOSED UP behind them.
            //
            // A ghost is a pace reference, not an entrant, and it takes a
            // finishing position in the saved result like anybody else. Both
            // drivers were already being found past them - one named after
            // either would otherwise stand in for them - but every number
            // below came off the recorded Position, which still counted the
            // ghosts that finished ahead. A driver who won the race outright
            // read as second whenever a ghost beat them, so their outright
            // wins and their podiums were both short, their average finishing
            // position was flattered downwards, and the margin between the
            // two was inflated by any ghost sitting between them.
            //
            // The same closing-up AchievementEvaluator does for the same
            // reason - see PlaceOf there.
            //
            // 17 Oct 2026, Bug Hunt 8, A30-3: and only cars that turned a
            // lap. A hand-released grid saves 0-lap rows for cars that never
            // moved, so a driver who sat on the line handed the other a
            // "win" (and if both did, Position picked one at random). A
            // driver with no laps is now not found, and the session is
            // skipped below - the same as WinnerOf / PlaceOf.
            var real = session.Results
                .Where(r => !GhostService.IsGhost(r.Transponder) && r.Laps >= 1)
                .OrderBy(r => r.Position)
                .ToList();

            var rA = real.FirstOrDefault(r => string.Equals(r.DriverName, _nameA, StringComparison.OrdinalIgnoreCase));
            var rB = real.FirstOrDefault(r => string.Equals(r.DriverName, _nameB, StringComparison.OrdinalIgnoreCase));
            if (rA is null || rB is null)
            {
                continue;
            }

            var placeA = real.IndexOf(rA) + 1;
            var placeB = real.IndexOf(rB) + 1;

            var aAhead = placeA < placeB;
            results.Add(aAhead);
            if (aAhead) { _winsA++; } else { _winsB++; }

            var margin = Math.Abs(placeA - placeB);
            if (aAhead) { biggestMarginA = Math.Max(biggestMarginA, margin); }
            else { biggestMarginB = Math.Max(biggestMarginB, margin); }
            closestGap = Math.Min(closestGap, margin);

            if (rA.FastLapMs > 0) { bestLapsA.Add(rA.FastLapMs); }
            if (rB.FastLapMs > 0) { bestLapsB.Add(rB.FastLapMs); }
            // Flying laps only (see PaceAnalysis.CountedLaps): the first
            // entry is the dash from the green to the first crossing, and it
            // pulled the average lap down and the spread up.
            allLapsA.AddRange(rA.LapTimesMs.Skip(1).Where(l => l > 0));
            allLapsB.AddRange(rB.LapTimesMs.Skip(1).Where(l => l > 0));

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

        var total = _winsA + _winsB;
        if (total == 0)
        {
            _lblSummary.Text = "These two have never raced in the same session.";
            _lblSummary.ForeColor = AppTheme.TextMuted;
            _pnlWinBar.Invalidate();
            return;
        }

        var leader = _winsA == _winsB ? "dead level" : $"{(_winsA > _winsB ? _nameA : _nameB)} leads";
        _lblSummary.Text = $"{_nameA}  {_winsA} - {_winsB}  {_nameB}   ({leader}, {total} shared session{(total == 1 ? "" : "s")})";
        _lblSummary.ForeColor = AppTheme.Accent;
        _pnlWinBar.Invalidate();

        AddStat("Sessions won", _winsA.ToString(), _winsB.ToString());
        AddStat("Win rate", $"{_winsA * 100.0 / total:0}%", $"{_winsB * 100.0 / total:0}%");
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

    /// <summary>
    /// A single proportional bar showing the win split at a glance - the
    /// numbers in the table are precise, but this makes a lopsided rivalry
    /// obvious without reading anything.
    /// </summary>
    private void WinBar_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // The card's colour round it, not the page's: it sits in a card.
        using (var bg = new SolidBrush(_pnlWinBar.Parent?.BackColor ?? AppTheme.Surface))
        {
            g.FillRectangle(bg, _pnlWinBar.ClientRectangle);
        }

        var total = _winsA + _winsB;
        if (total == 0)
        {
            return;
        }

        // As tall as its names in the bold face at the text size in use - it
        // was 22px whatever the text size, and a larger one ran the names
        // off the bottom of it.
        var bounds = new Rectangle(0, WinBarPad, _pnlWinBar.Width, Math.Max(1, _pnlWinBar.Height - WinBarPad * 2));
        var widthA = (int)(bounds.Width * (_winsA / (double)total));

        using (var brushA = new SolidBrush(AppTheme.Primary))
        using (var brushB = new SolidBrush(AppTheme.Success))
        {
            g.FillRectangle(brushA, bounds.Left, bounds.Top, widthA, bounds.Height);
            g.FillRectangle(brushB, bounds.Left + widthA, bounds.Top, bounds.Width - widthA, bounds.Height);
        }

        // Labels sit inside their own segment, and are skipped when the
        // segment is too narrow to hold them legibly. Each takes the text
        // colour for its own fill - the two segments are different colours,
        // and one of them can want dark text while the other wants white.
        //
        // Centred down the bar, and cut with "..." rather than run into the
        // other segment.
        var legible = UiLayout.AtTextSize(70);
        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        if (widthA > legible)
        {
            TextRenderer.DrawText(g, $"{_nameA}  {_winsA}", AppTheme.BoldFont,
                new Rectangle(bounds.Left + 8, bounds.Top, widthA - 16, bounds.Height),
                AppTheme.OnPrimary, flags | TextFormatFlags.Left);
        }
        if (bounds.Width - widthA > legible)
        {
            TextRenderer.DrawText(g, $"{_winsB}  {_nameB}", AppTheme.BoldFont,
                new Rectangle(bounds.Left + widthA + 8, bounds.Top, bounds.Width - widthA - 16, bounds.Height),
                AppTheme.OnSuccess, flags | TextFormatFlags.Right);
        }
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
