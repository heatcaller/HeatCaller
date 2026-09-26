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
/// </summary>
public sealed class PaceAnalysisControl : UserControl
{
    /// <summary>One race in the picker, rendered the way it reads in a list.</summary>
    private sealed record Choice(SessionRecord Session)
    {
        public override string ToString()
        {
            var when = Session.StartedAtUtc == default
                ? "undated"
                : Session.StartedAtUtc.ToLocalTime().ToString("d MMM HH:mm");

            var what = string.IsNullOrWhiteSpace(Session.Label)
                ? Session.SessionType.ToString()
                : Session.Label;

            return string.IsNullOrWhiteSpace(Session.TrackLayoutName)
                ? $"{when}  -  {what}"
                : $"{when}  -  {what}  ({Session.TrackLayoutName})";
        }
    }

    private readonly SessionHistoryStore? _history;
    private readonly SessionRecord? _pinned;

    private readonly ComboBox _cmbSession = new();
    private readonly Label _lblPick = new();
    private readonly Label _lblSummary = new();
    private readonly DataGridView _grid = new();

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
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshAll();
    }

    private void BuildLayout()
    {
        BuildGrid();

        var header = new Panel { Dock = DockStyle.Top };

        var y = UiLayout.PageTop;

        if (_pinned is null)
        {
            _lblPick.Text = "Race:";
            _lblPick.AutoSize = true;

            _cmbSession.Font = AppTheme.BodyFont;
            _cmbSession.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbSession.Width = 420;
            _cmbSession.SelectedIndexChanged += (_, _) =>
            {
                if (!_loading)
                {
                    Render();
                }
            };

            _lblPick.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(_lblPick, y, _cmbSession.Height));
            _cmbSession.Location = new Point(UiLayout.After(_lblPick), y);

            header.Controls.Add(_lblPick);
            header.Controls.Add(_cmbSession);

            y = UiLayout.Below(_cmbSession, UiLayout.RowGap);
        }

        _lblSummary.Font = AppTheme.BodyFont;
        _lblSummary.ForeColor = AppTheme.TextMuted;
        _lblSummary.AutoSize = true;
        _lblSummary.MaximumSize = new Size(1040, 0);
        _lblSummary.Location = new Point(UiLayout.PageLeft, y);

        header.Controls.Add(_lblSummary);

        // Two lines' worth reserved whether or not the summary needs them,
        // so the table below does not jump when a different race is picked
        // and the sentence gets longer.
        header.Height = _lblSummary.Top + AppTheme.BodyFont.Height * 2 + UiLayout.RowGap;

        _grid.Dock = DockStyle.Fill;

        // Fill first, header second: docking resolves from the last child
        // backwards, so the Fill control has to be added before the edges it
        // gives way to.
        Controls.Add(_grid);
        Controls.Add(header);
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
                // as a race's.
                foreach (var session in _history.All.Where(s => s.Results.Count > 0))
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

        if (Current is not { } session)
        {
            _lblSummary.Text = "No sessions recorded yet - the pace table fills in as soon as one is.";
            return;
        }

        // Ghost cars left out, by transponder: older saved sessions still
        // carry them, and a ghost's pace is generated, not driven.
        var rows = PaceAnalysis.For(session)
            .Where(r => r.CountedLaps > 0 && !GhostService.IsGhost(r.Transponder))
            .ToList();

        // A session where only ghosts ran has laps, just none a real driver
        // drove - the "recorded before lap times were kept" message would
        // be wrong for it.
        if (rows.Count == 0 && session.Results.Any(r => r.LapTimesMs.Count > 0 && GhostService.IsGhost(r.Transponder)))
        {
            _lblSummary.Text = "Only ghost cars ran in this session, so there is no real driver's pace to read.";
            return;
        }

        BuildSummary(rows);
        Fill(rows);
    }

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
        Column("driver", "Driver", 90, "Driver name.");
        Column("laps", "Laps", 32, "Counted laps, not including the run from the green to their first crossing.");
        Column("best", "Best", 46, "Their quickest single lap.");
        Column("run", "Best 3 in a row", 62, "Average of their quickest unbroken three laps - what they could actually sustain.");
        Column("quick", "Best 3 any", 56, "Average of their three quickest laps wherever they came. Far better than the column before it means the speed came in flashes.");
        Column("avg", "Average", 50, "Average of every counted lap.");
        Column("off", "Off best", 48, "How far off their own best they ran on a typical lap.");
        Column("cons", "Consistency", 54, "Spread of their lap times. Lower is tidier - under about 1.5% is driving at a limit, over 4% is scrappy.");
        Column("fade", "Fade", 44, "Last third of the race against the first. Positive means they slowed down.");
        Column("gain", "Left on table", 58, "What repeating their own best lap every lap would have been worth over this race.");
        Column("verdict", "Read", 150, "What the numbers add up to.");
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
                string.IsNullOrWhiteSpace(row.DriverName) ? row.Transponder : row.DriverName,
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
            cells["cons"].Style.ForeColor = row.ConsistencyPercent <= 1.5
                ? AppTheme.Success
                : row.ConsistencyPercent >= 4.0
                    ? AppTheme.Danger
                    : AppTheme.TextDark;

            if (row.FadeMs != 0)
            {
                cells["fade"].Style.ForeColor = row.FadeMs > 0 ? AppTheme.Danger : AppTheme.Success;
            }
        }
    }

    private void BuildSummary(List<PaceAnalysis.Row> rows)
    {
        if (rows.Count == 0)
        {
            _lblSummary.Text = "This session was recorded before lap-by-lap times were kept, so there is no pace to read.";
            return;
        }

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
            parts.Add($"Quickest single lap: {Name(quickest)} at {PaceAnalysis.Time(quickest.BestLapMs)}.");
        }

        if (tidiest is not null)
        {
            parts.Add($"Tidiest: {Name(tidiest)} at {tidiest.ConsistencyPercent:0.0}% spread.");
        }

        if (mostLeft.PotentialGainMs >= 1000)
        {
            parts.Add($"Most left on the table: {Name(mostLeft)}, {mostLeft.PotentialGainMs / 1000.0:0.0}s "
                      + "against repeating their own best lap.");
        }

        parts.Add("Sorted by the best three laps in a row - the pace each driver could actually hold.");

        _lblSummary.Text = string.Join("   ", parts);

        static string Name(PaceAnalysis.Row row) =>
            string.IsNullOrWhiteSpace(row.DriverName) ? row.Transponder : row.DriverName;
    }
}
