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

    // 17 Oct 2026, Bug Hunt 8, A30-2: for the shared record rule (rentals
    // and excluded guests hold no records). Null reads as "no profiles",
    // which is the rule's own answer for an unknown transponder.
    private readonly ProfileStore? _profiles;

    // The four figures, each a number over its caption on a tile of its
    // own - the same tiles as Next Steps' day so far (StatTile).
    private readonly StatTile _races = new();
    private readonly StatTile _laps = new();
    private readonly StatTile _drivers = new();
    private readonly StatTile _best = new();

    private readonly Label _lblBestWho = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblEmpty = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    public AnalyticsOverviewControl(SessionHistoryStore history, ProfileStore? profiles = null)
    {
        _history = history;
        _profiles = profiles;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise take,
        // and set once it stayed at the size it was built at. See TextSize.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
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
    /// The four figures across the page as tiles, who set the best lap under
    /// them, and the track records in a card taking the height that is left.
    /// On the layout pieces (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be a header placed by hand - the figures 190px apart
    /// whatever their size, in the 29pt face set once - over a list docked
    /// to fill the rest.
    /// </summary>
    private void BuildLayout()
    {
        // --- At a glance ---
        var lblGlance = new Label
        {
            Text = AppTheme.Spaced("AT A GLANCE"),
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblGlance, () => AppTheme.MicroFont);

        var tiles = new[] { _races, _laps, _drivers, _best };
        var captions = new[] { "Races run", "Laps counted", "Drivers seen", "Best lap ever" };

        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];

            _textSize.Wear(tile.Value, () => AppTheme.TitleFont);
            _textSize.Wear(tile.Caption, () => AppTheme.MicroFont);
            tile.Set("-", captions[i]);
        }

        // Who set the best lap, where and when - under the tiles, which have
        // room for a number and not for a sentence.
        _lblBestWho.AutoSize = true;

        // A driver's or a layout's name is text: an '&' in it was dropped and
        // the letter after it underlined (10 Nov 2026, bug hunt 16).
        _lblBestWho.UseMnemonic = false;
        _lblBestWho.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblBestWho, () => AppTheme.SmallFont);

        // --- Track records ---
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

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));

        var records = new Section("Track records");

        records.AddFill(_grid, () => _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height, gapBelow: 0);

        // Shown in place of the list when there is nothing in it - in the
        // list's own room, behind it, and centred there. An empty grid with
        // column headings and no rows reads as broken; a sentence reads as
        // "nothing yet".
        _lblEmpty.AutoSize = false;
        _lblEmpty.Dock = DockStyle.Fill;
        _lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
        _lblEmpty.ForeColor = AppTheme.TextMuted;
        _lblEmpty.Text = "No races recorded yet - run one and the records start here.";
        _lblEmpty.Visible = false;

        _grid.Parent!.Controls.Add(_lblEmpty);

        // --- The page ---
        //
        // The tiles on the page itself, not in a card: they are cards.
        var page = new FillPage();

        page.Add(lblGlance, gapBelow: UiLayout.RowGap);
        page.Add(new TileStrip(tiles), gapBelow: UiLayout.HintGap);
        page.Add(_lblBestWho);
        page.AddFill(records, records.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>Called when the tab is opened, since races finish elsewhere.</summary>
    public void RefreshAll()
    {
        // Saved demo races are left out of every figure here: they are
        // simulated, so counting them would pad the race and lap totals
        // and could put an invented lap at the top of the records.
        var everything = _history.All
            .Where(s => !s.IsNotScored())
            .ToList();

        var races = everything
            .Where(s => s.SessionType == RaceSessionType.Race)
            .ToList();

        _races.Set(races.Count.ToString(), "Races run");

        _laps.Set(everything
            .SelectMany(s => s.Results)
            .Where(r => !GhostService.IsGhost(r.Transponder))
            .Sum(r => (long)r.Laps)
            .ToString("N0"), "Laps counted");

        _drivers.Set(everything
            .SelectMany(s => s.Results)
            .Select(r => r.Transponder)
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ToString(), "Drivers seen");

        // --- The outright best, and who set it ---
        var best = RecordLaps(everything, _profiles).FirstOrDefault();

        if (best.Result is null)
        {
            _best.Set("-", "Best lap ever");
            _lblBestWho.Text = string.Empty;
        }
        else
        {
            _best.Set(Lap(best.Result.FastLapMs), "Best lap ever");

            var who = string.IsNullOrWhiteSpace(best.Result.DriverName)
                ? best.Result.Transponder
                : best.Result.DriverName;

            var layout = string.IsNullOrWhiteSpace(best.Session.TrackLayoutName)
                ? "an unnamed layout"
                : best.Session.TrackLayoutName;

            _lblBestWho.Text = $"Best lap set by {who} on {layout}, {best.Session.StartedAtUtc.ToLocalTime():d MMM yyyy}";
        }

        FillRecords(everything);
    }

    /// <summary>
    /// Every lap that could stand as a record, quickest first.
    ///
    /// 17 Oct 2026, Bug Hunt 8, A30-2: through
    /// AchievementEvaluator.CanHoldTrackRecord rather than a ghost check
    /// alone, so this page names the same holder as the badge and the live
    /// record - a rental or an ExcludeFromRecords guest is never it. No
    /// SessionType filter: the evaluator's own BestEverOn has none either.
    /// Used by both the "Best lap ever" tile and the table so they agree.
    /// </summary>
    internal static IEnumerable<(SessionRecord Session, SessionResultEntry Result)> RecordLaps(
        IEnumerable<SessionRecord> sessions, ProfileStore? profiles) =>
        sessions
            .SelectMany(s => s.Results.Select(r => (Session: s, Result: r)))
            .Where(x => x.Result.FastLapMs > 0 && AchievementEvaluator.CanHoldTrackRecord(x.Result.Transponder, profiles))
            .OrderBy(x => x.Result.FastLapMs);

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
                var best = RecordLaps(group, _profiles)
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
