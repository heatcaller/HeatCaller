namespace HeatCaller;

/// <summary>
/// The first tab of Race Analytics: the numbers somebody actually asks for
/// out loud.
///
/// Every other tab on this page answers a question you already have - how do
/// these two drivers compare, is this driver improving, what did that race
/// look like lap by lap. This one answers the question nobody has to ask
/// for, because it is the one on the wall of every club in the world: what
/// is the lap record here, and who holds it.
///
/// Everything is read from the saved session history, so it costs nothing to
/// keep and it is right the moment a race finishes.
/// </summary>
public sealed class AnalyticsOverviewControl : UserControl
{
    private readonly SessionHistoryStore _history;

    private readonly Label _lblRaces = new();
    private readonly Label _lblLaps = new();
    private readonly Label _lblDrivers = new();
    private readonly Label _lblBest = new();
    private readonly Label _lblBestWho = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblEmpty = new();

    public AnalyticsOverviewControl(SessionHistoryStore history)
    {
        _history = history;

        Dock = DockStyle.Fill;
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshAll();
    }

    private void BuildLayout()
    {
        // --- The headline strip ---
        var header = new Panel { Dock = DockStyle.Top };

        var y = UiLayout.PageTop;

        var lblTitle = new Label
        {
            Text = "AT A GLANCE",
            Location = new Point(UiLayout.PageLeft, y),
            AutoSize = true,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.Primary,
        };

        y = UiLayout.Below(lblTitle, UiLayout.HeadingGap);

        // Four figures across, each a caption over a number, measured off
        // each other rather than placed at remembered columns.
        var tiles = new[]
        {
            ("Races run", _lblRaces),
            ("Laps counted", _lblLaps),
            ("Drivers seen", _lblDrivers),
            ("Best lap ever", _lblBest),
        };

        var x = UiLayout.PageLeft;

        foreach (var (caption, value) in tiles)
        {
            var lblCaption = new Label
            {
                Text = caption,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
            };

            value.Text = "-";
            value.AutoSize = true;
            value.Font = AppTheme.StatFont;
            value.ForeColor = AppTheme.TextDark;
            value.Location = new Point(x, UiLayout.Below(lblCaption, 2));

            header.Controls.Add(lblCaption);
            header.Controls.Add(value);

            // A fixed column per tile, wide enough for the widest number any
            // of them will hold, so they do not shuffle sideways as the
            // figures grow during a race day.
            x += 190;
        }

        _lblBestWho.AutoSize = true;
        _lblBestWho.ForeColor = AppTheme.TextMuted;
        _lblBestWho.Font = AppTheme.SmallFont;
        _lblBestWho.MaximumSize = new Size(360, 0);
        _lblBestWho.Location = new Point(UiLayout.PageLeft + 190 * 3, UiLayout.Below(_lblBest, 2));

        header.Controls.Add(lblTitle);
        header.Controls.Add(_lblBestWho);

        header.Height = UiLayout.Below(_lblBestWho, UiLayout.SectionGap);

        // --- Track records ---
        var lblRecords = new Label
        {
            Text = "TRACK RECORDS",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = AppTheme.BoldFont.Height + UiLayout.RowGap,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.Primary,
            Padding = new Padding(UiLayout.PageLeft, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "layout", HeaderText = "Track layout", FillWeight = 130 },
            new DataGridViewTextBoxColumn { Name = "record", HeaderText = "Lap record", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "holder", HeaderText = "Held by", FillWeight = 120 },
            new DataGridViewTextBoxColumn { Name = "set", HeaderText = "Set", FillWeight = 80 },
            new DataGridViewTextBoxColumn { Name = "races", HeaderText = "Races here", FillWeight = 70 });

        // Sits over the grid and is shown only when there is nothing in it.
        // An empty grid with column headings and no rows reads as broken;
        // a sentence reads as "nothing yet".
        _lblEmpty.Dock = DockStyle.Fill;
        _lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
        _lblEmpty.ForeColor = AppTheme.TextMuted;
        _lblEmpty.Text = "No races recorded yet - run one and the records start here.";
        _lblEmpty.Visible = false;

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(UiLayout.PageLeft, 0, UiLayout.PageRight, UiLayout.RowGap) };

        // Empty label first so it sits BEHIND the grid in z-order; the grid
        // is hidden when it has nothing to show.
        body.Controls.Add(_lblEmpty);
        body.Controls.Add(_grid);

        // Fill first, then the two Top bands - docking resolves from the
        // last child backwards.
        Controls.Add(body);
        Controls.Add(lblRecords);
        Controls.Add(header);
    }

    /// <summary>Called when the tab is opened, since races finish elsewhere.</summary>
    public void RefreshAll()
    {
        // Saved demo races are left out of every figure here: they are
        // simulated, so counting them would pad the race and lap totals
        // and could put an invented lap at the top of the records.
        var everything = _history.All
            .Where(s => !s.IsDemoSession())
            .ToList();

        var races = everything
            .Where(s => s.SessionType == RaceSessionType.Race)
            .ToList();

        _lblRaces.Text = races.Count.ToString();

        _lblLaps.Text = everything
            .SelectMany(s => s.Results)
            .Where(r => !GhostService.IsGhost(r.Transponder))
            .Sum(r => (long)r.Laps)
            .ToString("N0");

        _lblDrivers.Text = everything
            .SelectMany(s => s.Results)
            .Select(r => r.Transponder)
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ToString();

        // --- The outright best, and who set it ---
        var best = everything
            .SelectMany(s => s.Results.Select(r => (Session: s, Result: r)))
            .Where(x => x.Result.FastLapMs > 0 && !GhostService.IsGhost(x.Result.Transponder))
            .OrderBy(x => x.Result.FastLapMs)
            .FirstOrDefault();

        if (best.Result is null)
        {
            _lblBest.Text = "-";
            _lblBestWho.Text = string.Empty;
        }
        else
        {
            _lblBest.Text = Lap(best.Result.FastLapMs);

            var who = string.IsNullOrWhiteSpace(best.Result.DriverName)
                ? best.Result.Transponder
                : best.Result.DriverName;

            var layout = string.IsNullOrWhiteSpace(best.Session.TrackLayoutName)
                ? "an unnamed layout"
                : best.Session.TrackLayoutName;

            _lblBestWho.Text = $"{who} on {layout}, {best.Session.StartedAtUtc.ToLocalTime():d MMM yyyy}";
        }

        FillRecords(everything);
    }

    /// <summary>
    /// One row per track layout: its record, who holds it, and how much
    /// racing has happened there.
    ///
    /// Grouped by layout name rather than by a layout id, because a session
    /// records the name it ran under - renaming a layout starts a new line
    /// rather than rewriting history, which is the honest behaviour when
    /// nobody can say whether the track was also changed.
    /// </summary>
    private void FillRecords(IReadOnlyList<SessionRecord> sessions)
    {
        _grid.Rows.Clear();

        // Resolved to plain strings inside the grouping rather than
        // carried out as a tuple that may be empty - a "best lap" that does
        // not exist is a null the rest of this method would have to keep
        // apologising for.
        var groups = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.TrackLayoutName))
            .GroupBy(s => s.TrackLayoutName, StringComparer.CurrentCultureIgnoreCase)
            .Select(group =>
            {
                var best = group
                    .SelectMany(s => s.Results.Select(r => (Session: s, Result: r)))
                    .Where(x => x.Result.FastLapMs > 0 && !GhostService.IsGhost(x.Result.Transponder))
                    .OrderBy(x => x.Result.FastLapMs)
                    .Take(1)
                    .ToList();

                var record = "-";
                var holder = "-";
                var when = "-";

                if (best.Count == 1)
                {
                    var (session, result) = best[0];

                    record = Lap(result.FastLapMs);

                    holder = string.IsNullOrWhiteSpace(result.DriverName)
                        ? result.Transponder
                        : result.DriverName;

                    when = session.StartedAtUtc.ToLocalTime().ToString("d MMM yyyy");
                }

                return new
                {
                    Layout = group.Key,
                    Record = record,
                    Holder = holder,
                    When = when,
                    Races = group.Count(s => s.SessionType == RaceSessionType.Race),
                };
            })
            .OrderBy(g => g.Layout, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            _grid.Rows.Add(group.Layout, group.Record, group.Holder, group.When, group.Races.ToString());
        }

        var empty = _grid.Rows.Count == 0;

        _grid.Visible = !empty;
        _lblEmpty.Visible = empty;
    }

    /// <summary>A lap time as people say it: seconds to three places.</summary>
    private static string Lap(long ms) => $"{ms / 1000.0:0.000}";
}
