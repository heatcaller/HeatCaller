namespace HeatCaller;

/// <summary>
/// Manage named championships: pick which recorded sessions count, set a
/// points-per-position scheme, and see a live-computed standings table.
/// </summary>
public sealed class SeasonsControl : UserControl
{
    private readonly SeasonStore _seasons;
    private readonly SessionHistoryStore _history;
    private readonly UndoService _undo;

    private readonly ListBox _lstSeasons = new();
    private readonly DataGridView _dgvPointsScheme = new();
    private readonly CheckedListBox _clbSessions = new();

    // Needed to answer "is this race a main, and whose class is it" - the
    // rule reads a session's round, not the session itself.
    private readonly RoundDefinitionStore _rounds;
    private readonly RaceClassStore _raceClasses;
    private readonly RaceEventStore? _events;

    // Read for each driver's "Exclude from records" tick, so a guest or
    // visiting driver scores no championship points and the club drivers
    // behind them close up. Optional only so older callers still compile;
    // without it nobody is excluded.
    private readonly ProfileStore? _profiles;
    private readonly DataGridView _dgvStandings = new();
    private readonly Label _lblStatus = new();

    private bool _suppressEvents;

    public SeasonsControl(
        SeasonStore seasons,
        SessionHistoryStore history,
        UndoService undo,
        RoundDefinitionStore rounds,
        RaceClassStore raceClasses,
        RaceEventStore? events = null,
        ProfileStore? profiles = null)
    {
        _seasons = seasons;
        _history = history;
        _undo = undo;
        _rounds = rounds;
        _raceClasses = raceClasses;
        _events = events;
        _profiles = profiles;

        Dock = DockStyle.Fill;
        Font = AppTheme.BodyFont;

        // Set the design size BEFORE building the layout. Anchors are
        // resolved against whatever size the control has when a child is
        // added, so without this they were computed against WinForms'
        // tiny default - which pushed every bottom-anchored control
        // (New / Rename / Delete, Export Standings) off the bottom of the
        // page once it was docked to fill.
        Size = new Size(1000, 510);
        MinimumSize = new Size(900, 510);

        BuildLayout();
        RefreshSeasonList();
    }

    // The page measures itself on every resize - see LayoutPage - so
    // everything it has to place is held rather than left as a local.
    private Label _lblSeasons = new();
    private Label _lblScheme = new();
    private Label _lblSessions = new();
    private Label _lblStandings = new();
    private Label _lblHint = new();
    private Button _btnNew = new();
    private Button _btnRename = new();
    private Button _btnDeleteSeason = new();
    private Button _btnResetScheme = new();
    private Button _btnRules = new();
    private Button _btnExportStandings = new();
    private Button _btnCarried = new();

    private void BuildLayout()
    {
        // --- Column 1: season list ---
        _lblSeasons.Text = "Seasons / Championships";
        _lblSeasons.AutoSize = true;
        _lblSeasons.Font = AppTheme.BoldFont;

        _lstSeasons.Font = AppTheme.BodyFont;
        _lstSeasons.SelectedIndexChanged += (_, _) => LoadSelectedSeasonIntoUi();

        var btnNew = _btnNew;
        btnNew.Text = "New...";
        btnNew.Click += BtnNewSeason_Click;

        var btnRename = _btnRename;
        btnRename.Text = "Rename...";
        btnRename.Click += BtnRenameSeason_Click;

        var btnDeleteSeason = _btnDeleteSeason;
        btnDeleteSeason.Text = "Delete";
        btnDeleteSeason.Click += BtnDeleteSeason_Click;

        // --- Column 2: points scheme + session picker ---
        var lblScheme = _lblScheme;
        lblScheme.Text = "Points scheme (edit the Points column)";
        lblScheme.AutoSize = true;
        lblScheme.Font = AppTheme.BoldFont;


        _dgvPointsScheme.AllowUserToAddRows = false;
        _dgvPointsScheme.AllowUserToDeleteRows = false;
        _dgvPointsScheme.RowHeadersVisible = false;
        _dgvPointsScheme.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _dgvPointsScheme.Columns.Add(new DataGridViewTextBoxColumn { Name = "position", HeaderText = "Finishing Position", ReadOnly = true, FillWeight = 130 });
        _dgvPointsScheme.Columns.Add(new DataGridViewTextBoxColumn { Name = "points", HeaderText = "Points", FillWeight = 70 });
        _dgvPointsScheme.CellEndEdit += (_, _) => { if (!_suppressEvents) { SaveCurrentSeasonFromUi(); RecomputeStandings(); } };

        var btnResetScheme = _btnResetScheme;
        btnResetScheme.Text = "Reset to Default";

        _btnRules.Text = "Scoring Rules...";
        _btnRules.Click += (_, _) => EditRules();
        btnResetScheme.Click += BtnResetScheme_Click;

        var lblSessions = _lblSessions;
        lblSessions.Text = "Sessions counted toward this season:";
        lblSessions.AutoSize = true;
        lblSessions.Font = AppTheme.BoldFont;

        _clbSessions.Font = AppTheme.BodyFont;
        _clbSessions.CheckOnClick = true;
        _clbSessions.ItemCheck += (_, _) =>
        {
            // ItemCheck fires BEFORE the check state is applied, so defer
            // saving/recomputing until the UI has actually updated.
            if (_suppressEvents)
            {
                return;
            }
            // BeginInvoke throws if the handle doesn't exist yet, and
            // SetItemChecked during list population raises this event
            // before the control is ever shown.
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            BeginInvoke(() => { SaveCurrentSeasonFromUi(); RecomputeStandings(); });
        };

        // --- Column 3: standings ---
        var lblStandings = _lblStandings;
        lblStandings.Text = "Standings";
        lblStandings.AutoSize = true;
        lblStandings.Font = AppTheme.BoldFont;


        _dgvStandings.ReadOnly = true;
        _dgvStandings.AllowUserToAddRows = false;
        _dgvStandings.AllowUserToDeleteRows = false;
        _dgvStandings.RowHeadersVisible = false;
        _dgvStandings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _dgvStandings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _dgvStandings.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "rank", HeaderText = "Rank", FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 100 },
            new DataGridViewTextBoxColumn { Name = "points", HeaderText = "Points", FillWeight = 50 },

            // Of those points, how many were carried in from before - blank
            // for everybody who carried none (27 Sep 2026).
            new DataGridViewTextBoxColumn { Name = "carried", HeaderText = "Carried In", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "wins", HeaderText = "Wins", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "podiums", HeaderText = "Podiums", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "sessions", HeaderText = "Races", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "dnfs", HeaderText = "DNF", FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "best", HeaderText = "Best Finish", FillWeight = 60 }
        );

        _lblStatus.AutoSize = false;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Font = AppTheme.BodyFont;

        var lblHint = _lblHint;
        lblHint.Text = "Only sessions recorded as \"Race\" type appear below. Practice sessions never count toward points.";
        lblHint.AutoSize = true;
        lblHint.ForeColor = AppTheme.TextMuted;
        lblHint.Font = AppTheme.SmallFont;

        Controls.Add(_lblSeasons);
        Controls.Add(_lstSeasons);
        Controls.Add(btnNew);
        Controls.Add(btnRename);
        Controls.Add(btnDeleteSeason);
        Controls.Add(lblScheme);
        Controls.Add(_dgvPointsScheme);
        var btnExportStandings = _btnExportStandings;
        btnExportStandings.Text = "Export Standings to CSV...";
        btnExportStandings.Click += (_, _) => ExportStandings();
        Controls.Add(btnExportStandings);

        _btnCarried.Text = "Points Carried In...";
        _btnCarried.Click += (_, _) => EditCarriedPoints();
        Controls.Add(_btnCarried);

        Controls.Add(btnResetScheme);
        Controls.Add(_btnRules);
        Controls.Add(lblSessions);
        Controls.Add(_clbSessions);
        Controls.Add(lblHint);
        Controls.Add(lblStandings);
        Controls.Add(_dgvStandings);
        Controls.Add(_lblStatus);


        // This page places every one of its children on every resize, so
        // the repair pass is told to keep out - two things moving the same
        // control is worse than neither doing it. See
        // LayoutDoctor.LeaveAlone.
        LayoutDoctor.LeaveAlone(this);
        LayoutPage();
    }

    /// <summary>
    /// Three columns, measured, all ending on the same line.
    ///
    /// They used to be three fixed-width stacks of literal coordinates -
    /// the season list at x=12 exactly 380 tall, the points scheme at 216
    /// exactly 180, the standings at 592 exactly 420 - with a mixture of
    /// anchors on top. Two of the three stretched with the window and the
    /// standings grid did not, so on anything taller than the size this
    /// was drawn at, the right-hand third of the page was empty below the
    /// grid while the other two columns ran on past it. That is the dead
    /// quarter-screen.
    ///
    /// Now every column is given its share of the width, every list and
    /// grid takes the height that is left in its own column after the
    /// headings and buttons are accounted for, and all three finish level.
    /// </summary>
    private void LayoutPage()
    {
        if (Width < 200 || Height < 200 || _lblSeasons.IsDisposed)
        {
            return;
        }

        var left = UiLayout.PageLeft;
        var right = Width - UiLayout.PageRight;
        var top = UiLayout.PageTop;
        var bottom = Height - UiLayout.PageTop;

        var content = Math.Max(300, right - left);
        var gap = UiLayout.SectionGap;

        // The season list needs the least room and the standings the most.
        var seasonsWidth = Math.Clamp((int)(content * 0.20f), 160, 280);
        var middleWidth = Math.Clamp((int)(content * 0.34f), 260, 460);
        var standingsWidth = Math.Max(240, content - seasonsWidth - middleWidth - gap * 2);

        var seasonsLeft = left;
        var middleLeft = seasonsLeft + seasonsWidth + gap;
        var standingsLeft = middleLeft + middleWidth + gap;

        foreach (var button in new[] { _btnNew, _btnRename, _btnDeleteSeason, _btnResetScheme, _btnRules, _btnExportStandings, _btnCarried })
        {
            var text = TextRenderer.MeasureText(
                button.Text,
                AppTheme.BoldFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            button.Size = new Size(text.Width + 26, UiLayout.ButtonHeight);
        }

        // --- Column one: the seasons, with its buttons on the floor ---
        _lblSeasons.Location = new Point(seasonsLeft, top);

        var seasonsButtonsTop = bottom - UiLayout.ButtonHeight;
        var x = seasonsLeft;

        foreach (var button in new[] { _btnNew, _btnRename, _btnDeleteSeason })
        {
            button.Location = new Point(x, seasonsButtonsTop);
            x += button.Width + UiLayout.ButtonGap;
        }

        var listTop = UiLayout.Below(_lblSeasons, UiLayout.RowGap);

        _lstSeasons.Location = new Point(seasonsLeft, listTop);
        _lstSeasons.Size = new Size(seasonsWidth, Math.Max(80, seasonsButtonsTop - UiLayout.RowGap - listTop));

        // --- Column two: points scheme over the session list ---
        _lblScheme.Location = new Point(middleLeft, top);

        var schemeTop = UiLayout.Below(_lblScheme, UiLayout.RowGap);

        // The scheme is a short fixed table; the session list gets the rest
        // of the column. A third of the available height each way reads
        // about right and keeps both usable.
        var columnHeight = bottom - schemeTop;
        var schemeHeight = Math.Clamp((int)(columnHeight * 0.34f), 120, 260);

        _dgvPointsScheme.Location = new Point(middleLeft, schemeTop);
        _dgvPointsScheme.Size = new Size(middleWidth, schemeHeight);

        var schemeButtonsTop = UiLayout.Below(_dgvPointsScheme, UiLayout.RowGap);

        UiLayout.TightButtonRow(middleLeft, schemeButtonsTop, _btnResetScheme, _btnRules);

        _lblSessions.Location = new Point(middleLeft, UiLayout.Below(_btnResetScheme, UiLayout.SectionGap));

        _lblHint.MaximumSize = new Size(middleWidth, 0);
        _lblHint.Location = new Point(middleLeft, bottom - _lblHint.PreferredSize.Height);

        var sessionsTop = UiLayout.Below(_lblSessions, UiLayout.RowGap);

        _clbSessions.Location = new Point(middleLeft, sessionsTop);
        _clbSessions.Size = new Size(
            middleWidth,
            Math.Max(80, _lblHint.Top - UiLayout.RowGap - sessionsTop));

        // --- Column three: the standings, down to the same floor ---
        _lblStandings.Location = new Point(standingsLeft, top);

        _btnExportStandings.Location = new Point(standingsLeft, bottom - UiLayout.ButtonHeight);

        // Level with the heading, at the column's right edge - beside Export
        // Standings the two did not fit the column at the page's narrowest.
        _btnCarried.Location = new Point(standingsLeft + standingsWidth - _btnCarried.Width, top);

        var statusHeight = AppTheme.BodyFont.Height + 2;

        _lblStatus.Location = new Point(
            standingsLeft,
            _btnExportStandings.Top - UiLayout.RowGap - statusHeight);

        _lblStatus.Size = new Size(standingsWidth, statusHeight);

        var standingsTop = Math.Max(UiLayout.Below(_lblStandings, UiLayout.RowGap), UiLayout.Below(_btnCarried, UiLayout.RowGap));

        _dgvStandings.Location = new Point(standingsLeft, standingsTop);
        _dgvStandings.Size = new Size(
            standingsWidth,
            Math.Max(100, _lblStatus.Top - UiLayout.RowGap - standingsTop));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutPage();
    }

    /// <summary>Opens the scoring rules, then re-runs them over the whole history.</summary>
    private void EditRules()
    {
        if (SelectedSeason() is not { } season)
        {
            AppDialog.Show(this, "Pick a championship first.", "No championship selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new SeasonRulesForm(season, _raceClasses, _events);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // The whole rule, so undo restores what the standings were computed
        // from rather than just the switch that happened to be flipped.
        var wasAuto = season.AutoInclude;
        var wasClass = season.ClassName;
        var wasMains = season.MainsOnly;
        var wasBest = season.CountBestRounds;
        var wasEvents = season.IncludedEventIds.ToList();

        var seasons = _seasons;
        var seasonId = season.Id;

        _undo.Capture($"Scoring rules for '{season.Name}'", () =>
        {
            // Onto the championship as it is on file now, by id - one
            // deleted since stays deleted rather than being written back.
            if (seasons.Get(seasonId) is not { } current)
            {
                throw new InvalidOperationException("that championship has been deleted since. Nothing was changed.");
            }

            current.AutoInclude = wasAuto;
            current.ClassName = wasClass;
            current.MainsOnly = wasMains;
            current.CountBestRounds = wasBest;
            current.IncludedEventIds = wasEvents.ToList();

            seasons.Upsert(current);
            RefreshAfterUndo();
        });

        form.ApplyTo(season);
        _seasons.Upsert(season);

        LoadSelectedSeasonIntoUi();
    }

    private Season? SelectedSeason() => (_lstSeasons.SelectedItem as SeasonListItem)?.Season;

    /// <summary>
    /// The points drivers carry into the selected championship from before
    /// it ran here (27 Sep 2026). One undo step for the whole list.
    /// </summary>
    private void EditCarriedPoints()
    {
        if (SelectedSeason() is not { } season)
        {
            AppDialog.Show(this, "Pick a championship first.", "No championship selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // The names the table already goes by, offered as a name is typed.
        var known = SeasonStore.ComputeStandings(season, EventSpine.SessionsFor(season, _events, _rounds, _history), _profiles)
            .Select(e => e.DriverName)
            .ToList();

        using var form = new CarriedPointsForm(season.Name, season.CarriedPoints, known);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var before = season.CarriedPoints.Select(e => new CarriedPointsEntry { DriverName = e.DriverName, Points = e.Points }).ToList();
        var seasons = _seasons;
        var seasonId = season.Id;

        _undo.Capture($"Points carried into '{season.Name}'", () =>
        {
            // Onto the championship as it is on file now, by id - one
            // deleted since stays deleted rather than being written back.
            if (seasons.Get(seasonId) is not { } current)
            {
                throw new InvalidOperationException("that championship has been deleted since. Nothing was changed.");
            }

            current.CarriedPoints = before.Select(e => new CarriedPointsEntry { DriverName = e.DriverName, Points = e.Points }).ToList();
            seasons.Upsert(current);
            RefreshAfterUndo();
        });

        season.CarriedPoints = form.Entries;
        _seasons.Upsert(season);

        RecomputeStandings();
    }

    /// <summary>Called when this page becomes visible - new sessions become available to include while it's inactive.</summary>
    public void RefreshAll() => RefreshSeasonList();

    private void RefreshSeasonList(string? selectId = null)
    {
        _lstSeasons.Items.Clear();
        foreach (var season in _seasons.All)
        {
            _lstSeasons.Items.Add(new SeasonListItem(season));
        }

        if (selectId is not null)
        {
            for (var i = 0; i < _lstSeasons.Items.Count; i++)
            {
                if (((SeasonListItem)_lstSeasons.Items[i]).Season.Id == selectId)
                {
                    _lstSeasons.SelectedIndex = i;
                    return;
                }
            }
        }

        if (_lstSeasons.Items.Count > 0 && _lstSeasons.SelectedIndex < 0)
        {
            _lstSeasons.SelectedIndex = 0;
        }
        else if (_lstSeasons.Items.Count == 0)
        {
            LoadSelectedSeasonIntoUi(); // clears the panels
        }
    }

    private void LoadSelectedSeasonIntoUi()
    {
        _suppressEvents = true;
        try
        {
            var season = SelectedSeason();

            _dgvPointsScheme.Rows.Clear();
            _clbSessions.Items.Clear();

            var enabled = season is not null;
            _dgvPointsScheme.Enabled = enabled;
            _clbSessions.Enabled = enabled;

            if (season is null)
            {
                _dgvStandings.Rows.Clear();
                return;
            }

            foreach (var entry in season.PointsScheme.OrderBy(e => e.Position))
            {
                _dgvPointsScheme.Rows.Add(entry.Position, entry.Points);
            }

            // Asked fresh, so anything recorded since this tab was last
            // open is already in it - and nothing is written to disk just
            // for opening a tab, because there is no materialised list to
            // keep in step any more.
            var counted = EventSpine.SessionsFor(season, _events, _rounds, _history)
                .Select(x => x.Id)
                .ToHashSet(StringComparer.Ordinal);

            var raceSessions = _history.All.Where(s => s.SessionType == RaceSessionType.Race && !s.IsDemoSession()).ToList();

            foreach (var session in raceSessions)
            {
                var label = string.IsNullOrWhiteSpace(session.Label) ? "(no label)" : session.Label;
                var trackPart = string.IsNullOrWhiteSpace(session.TrackLayoutName) ? "" : $" @ {session.TrackLayoutName}";

                // What the round says about it, which is what the rule
                // reads - so a row that the rule skipped shows why.
                var round = string.IsNullOrEmpty(session.RoundId) ? null : _rounds.Get(session.RoundId);

                var kindPart = round is null
                    ? "  (freeform)"
                    : $"  [{round.ClassName} {round.Kind}]";

                var display = $"{session.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} - {label}{trackPart}{kindPart}";

                var index = _clbSessions.Items.Add(new SessionListItem(session, display));
                _clbSessions.SetItemChecked(index, counted.Contains(session.Id));
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        RecomputeStandings();
    }

    private void SaveCurrentSeasonFromUi()
    {
        var season = SelectedSeason();
        if (season is null)
        {
            return;
        }

        var scheme = new List<PointsSchemeEntry>();
        foreach (DataGridViewRow row in _dgvPointsScheme.Rows)
        {
            if (row.Cells["position"].Value is null)
            {
                continue;
            }
            var position = Convert.ToInt32(row.Cells["position"].Value);
            var points = int.TryParse(row.Cells["points"].Value?.ToString(), out var p) ? p : 0;
            scheme.Add(new PointsSchemeEntry { Position = position, Points = points });
        }
        season.PointsScheme = scheme;

        // A tick means "this race counts". WHY it counts decides where it
        // is recorded: if the rule already takes it, ticking is simply
        // agreement and nothing needs storing - only the disagreements
        // have to be written down. That is what keeps both override lists
        // small, rather than the season carrying a copy of every race it
        // happens to include.
        var forcedIn = new List<string>();
        var forcedOut = new List<string>();

        for (var i = 0; i < _clbSessions.Items.Count; i++)
        {
            if (_clbSessions.Items[i] is not SessionListItem item)
            {
                continue;
            }

            var ticked = _clbSessions.GetItemChecked(i);
            var byRule = season.AutoInclude && EventSpine.RuleAccepts(season, item.Session, _rounds, _events);

            if (byRule && !ticked)
            {
                forcedOut.Add(item.Session.Id);
            }
            else if (!byRule && ticked)
            {
                forcedIn.Add(item.Session.Id);
            }
        }

        season.IncludedSessionIds = forcedIn;
        season.ExcludedSessionIds = forcedOut;

        _seasons.Upsert(season);
    }

    private void RecomputeStandings()
    {
        var season = SelectedSeason();
        _dgvStandings.Rows.Clear();

        if (season is null)
        {
            return;
        }

        var sessions = EventSpine.SessionsFor(season, _events, _rounds, _history);
        var standings = SeasonStore.ComputeStandings(season, sessions, _profiles);

        var rank = 1;
        foreach (var entry in standings)
        {
            _dgvStandings.Rows.Add(
                rank++,
                entry.DriverName,
                entry.TotalPoints,
                entry.CarriedIn == 0 ? string.Empty : entry.CarriedIn.ToString(),
                entry.Wins,
                entry.Podiums,

                // "5 of 7" when the best-N rule threw two away, so the
                // table shows the dropping happened rather than just a
                // total that does not add up from the rounds above it.
                entry.DroppedRounds > 0
                    ? $"{entry.SessionsCounted - entry.DroppedRounds} of {entry.SessionsCounted}"
                    : entry.SessionsCounted.ToString(),
                entry.Dnfs,
                entry.BestFinish == int.MaxValue ? "-" : $"P{entry.BestFinish}");
        }

        var rule = season.AutoInclude
            ? (season.ClassName.Length > 0 ? season.ClassName : "every class")
              + (season.MainsOnly ? ", mains only" : ", heats and mains")
              + (season.IncludedEventIds.Count > 0 ? $", {Plural.Of(season.IncludedEventIds.Count, "race day")}" : string.Empty)
            : "hand-picked";

        var drop = season.CountBestRounds switch
        {
            <= 0 => string.Empty,
            1 => " \u00b7 best round counts",
            _ => $" \u00b7 best {season.CountBestRounds} rounds count",
        };

        var overrides = season.ExcludedSessionIds.Count > 0
            ? $" \u00b7 {season.ExcludedSessionIds.Count} forced out"
            : string.Empty;

        var carriedIn = standings.Count(e => e.CarriedIn != 0);

        if (carriedIn > 0)
        {
            overrides += $" \u00b7 {Plural.Of(carriedIn, "driver")} carrying points in";
        }

        _lblStatus.Text = standings.Count == 0
            ? $"Nothing counted yet ({rule}). Tick a race, or change the scoring rules."
            : $"{Plural.Of(standings.Count, "driver")}, {Plural.Of(sessions.Count, "race")} \u00b7 {rule}{drop}{overrides}";
    }

    private void BtnNewSeason_Click(object? sender, EventArgs e)
    {
        var name = PromptForText("New Season", "Season name:", "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // New championships get the rule; old ones deserialize without it
        // and stay hand-picked, so nothing anybody already published moves.
        var season = new Season { Name = name.Trim(), AutoInclude = true };

        _seasons.Upsert(season);
        RefreshSeasonList(season.Id);
    }

    private void BtnRenameSeason_Click(object? sender, EventArgs e)
    {
        var season = SelectedSeason();
        if (season is null)
        {
            AppDialog.Show(this, "Select a season first.", "No season selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var name = PromptForText("Rename Season", "Season name:", season.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var seasons = _seasons;
        var seasonId = season.Id;
        var oldName = season.Name;
        var newName = name.Trim();

        _undo.Capture($"Rename season '{oldName}' to '{newName}'", () =>
        {
            // Only while it still has the name this rename gave it.
            if (seasons.Get(seasonId) is { } current && current.Name == newName)
            {
                current.Name = oldName;
                seasons.Upsert(current);
                RefreshAfterUndo();
            }
        });

        season.Name = newName;
        _seasons.Upsert(season);
        RefreshSeasonList(season.Id);
    }

    /// <summary>
    /// Redraws after an undo. The undo works on the store; the page is only
    /// redrawn if it is still there to redraw.
    /// </summary>
    private void RefreshAfterUndo()
    {
        if (!IsDisposed)
        {
            // Stays on the championship being looked at: a bare refresh
            // jumped to the first in the list, and its panels showed that
            // one instead of the one the undo had just put right.
            RefreshSeasonList(SelectedSeason()?.Id);
        }
    }

    private void BtnDeleteSeason_Click(object? sender, EventArgs e)
    {
        var season = SelectedSeason();
        if (season is null)
        {
            AppDialog.Show(this, "Select a season first.", "No season selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (AppDialog.Show(this, $"Delete season \"{season.Name}\"?\r\n\r\nThe underlying race sessions are not affected, and this can be reversed with Undo.",
            "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        // Capture before deleting - the season object carries its points
        // scheme and its list of counted sessions, all of which need to
        // come back together.
        var seasons = _seasons;

        _undo.Capture($"Delete season '{season.Name}'", () =>
        {
            // Upsert goes by id, so a season already back is not doubled.
            seasons.Upsert(season);
            RefreshAfterUndo();
        });

        _seasons.Delete(season.Id);
        RefreshSeasonList();
    }

    private void BtnResetScheme_Click(object? sender, EventArgs e)
    {
        var season = SelectedSeason();
        if (season is null)
        {
            return;
        }

        // The scheme as it was - copied, since the grid edits entries in
        // place - so Undo puts back every position's points.
        var seasons = _seasons;
        var seasonId = season.Id;
        var oldScheme = season.PointsScheme
            .Select(p => new PointsSchemeEntry { Position = p.Position, Points = p.Points })
            .ToList();

        _undo.Capture($"Reset points for '{season.Name}'", () =>
        {
            if (seasons.Get(seasonId) is { } current)
            {
                current.PointsScheme = oldScheme
                    .Select(p => new PointsSchemeEntry { Position = p.Position, Points = p.Points })
                    .ToList();
                seasons.Upsert(current);
                RefreshAfterUndo();
            }
        });

        season.PointsScheme = Season.DefaultPointsScheme();
        _seasons.Upsert(season);
        LoadSelectedSeasonIntoUi();
    }

    private string? PromptForText(string title, string prompt, string defaultValue)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(380, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
        };

        var lbl = new Label { Text = prompt, Location = new Point(12, 16), AutoSize = true };
        var txt = new TextBox { Location = new Point(12, 40), Width = 340, Text = defaultValue };
        var btnOk = new Button { Text = "OK", Location = new Point(188, 74), Width = 80, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(272, 74), Width = 80, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
        dialog.AcceptButton = btnOk;
        dialog.CancelButton = btnCancel;
        dialog.BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(dialog);

        return dialog.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
    }

    private sealed record SeasonListItem(Season Season)
    {
        public override string ToString() => Season.Name;
    }

    private sealed record SessionListItem(SessionRecord Session, string Display)
    {
        public override string ToString() => Display;
    }

    /// <summary>Exports the selected season's standings, matching the export options already on Race History.</summary>
    private void ExportStandings()
    {
        var season = SelectedSeason();
        if (season is null)
        {
            AppDialog.Show(this, "Select a season first.", "No season selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var standings = SeasonStore.ComputeStandings(
            season,
            EventSpine.SessionsFor(season, _events, _rounds, _history),
            _profiles);

        if (standings.Count == 0)
        {
            AppDialog.Show(this, "This season has no standings yet - tick some sessions to count toward it first.",
                "Nothing to export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            // Only spaces used to be dealt with here, so a season named, say, "Winter 2025/26"
            // got a suggested file name Windows won't take. See PlainFileName.SafePart.
            FileName = $"{PlainFileName.SafePart(season.Name, "season").Replace(' ', '-')}-standings-{DateTime.Now:yyyy-MM-dd}.csv",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var lines = new List<string>
            {
                CsvHelper.BuildRow("Rank", "Driver", "Points", "Wins", "Podiums", "Sessions Counted", "DNF", "Best Finish", "Carried In"),
            };

            var rank = 1;
            foreach (var entry in standings)
            {
                lines.Add(CsvHelper.BuildRow(
                    rank++,
                    entry.DriverName,
                    entry.TotalPoints,
                    entry.Wins,
                    entry.Podiums,
                    entry.SessionsCounted,
                    entry.Dnfs,
                    entry.BestFinish == int.MaxValue ? "-" : $"P{entry.BestFinish}",
                    entry.CarriedIn));
            }

            File.WriteAllLines(dialog.FileName, lines, CsvHelper.FileEncoding);
            AppDialog.Show(this, $"Exported {Plural.Of(standings.Count, "driver")} to:\n{dialog.FileName}",
                "Standings exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't export: {ex.Message}", "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
