using System.Globalization;

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
    private readonly CheckedListBox _clbSessions = new ThemedCheckedListBox();

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

        // The body face, worn rather than set once: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise
        // take, and set once it stayed at the size it was built at when the
        // text size changed. See TextSize.
        //
        // No design size and no MinimumSize. The 900x510 floor was honoured
        // over the size the page was given, so a small window cut the
        // bottom of all three columns off - buttons included. A short
        // window scrolls the page instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshSeasonList();
    }

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>Rows of the points scheme shown without scrolling - the rest are a scroll away.</summary>
    private const int SchemeRowsShown = 5;
    private const string SchemeHintText = "Type in the Points column to change what each place scores.";

    /// <summary>The championships' buttons - while there is room, the column is no narrower than the widest of them. See ColumnWidths.</summary>
    private ButtonBar? _seasonButtons;

    /// <summary>The championships' card - while there is room, the column is no narrower than its caption. See ColumnWidths.</summary>
    private Section? _seasonsCard;

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
    /// Four cards in three columns, on the layout pieces (LayoutPieces.cs):
    /// the championships, their buttons under the list; the points scheme
    /// over the races that count; and the standings, the widest column.
    /// Every list takes the height its card has left, down to its headings
    /// and a few rows, and past that the page scrolls. Nothing is placed by
    /// hand.
    ///
    /// It used to be three columns placed on every resize, their widths
    /// shares of the page and their lists stretched to a common floor, over
    /// a 900x510 minimum the page kept however small its window - and every
    /// heading, list and button in its own fixed font, so none of it
    /// followed the text size.
    ///
    /// The columns are two Besides, one inside the other: the standings
    /// down the right of everything else, and the scheme and races down
    /// the right of the championships.
    /// </summary>
    private void BuildLayout()
    {
        // --- The championships ---
        _lstSeasons.SelectedIndexChanged += (_, _) => LoadSelectedSeasonIntoUi();

        // The lists fill their cards to the pixel: a list that keeps to
        // whole rows stops short of the room it is given, and its outline
        // with it.
        _lstSeasons.IntegralHeight = false;

        var btnNew = new Button { Text = "New..." };
        btnNew.Click += BtnNewSeason_Click;

        var btnRename = new Button { Text = "Rename..." };
        btnRename.Click += BtnRenameSeason_Click;

        var btnDeleteSeason = new Button { Text = "Delete" };
        btnDeleteSeason.Click += BtnDeleteSeason_Click;

        var seasons = new Section("Championships");
        _seasonsCard = seasons;

        seasons.AddFill(_lstSeasons, () => ListLeast(_lstSeasons, 3));
        // Each only as wide as its caption: three short words, which as
        // standard buttons - never under 148px - went one under another in
        // a column this narrow (layout pictures, 29 Sep).
        _seasonButtons = seasons.Add(new ButtonBar(tight: true).With(btnNew, btnRename, btnDeleteSeason), gapBelow: 0);

        // --- The points scheme ---
        var schemeHint = new Label { Text = SchemeHintText, AutoSize = true, ForeColor = AppTheme.TextMuted };

        _dgvPointsScheme.AllowUserToAddRows = false;
        _dgvPointsScheme.AllowUserToDeleteRows = false;
        _dgvPointsScheme.RowHeadersVisible = false;
        _dgvPointsScheme.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _dgvPointsScheme.Columns.Add(new DataGridViewTextBoxColumn { Name = "position", HeaderText = "Finishing Position", ReadOnly = true, FillWeight = 130 });
        _dgvPointsScheme.Columns.Add(new DataGridViewTextBoxColumn { Name = "points", HeaderText = "Points", FillWeight = 70 });
        // A Points cell that isn't a whole number is refused where it was
        // typed. It used to be saved as 0 without a word, so "1O" for "10"
        // left every race winner scoring nothing for the win, on disk, and
        // the cell showed 0 next time with no trace of the typo.
        _dgvPointsScheme.CellValidating += (_, e) =>
        {
            if (_suppressEvents || _dgvPointsScheme.Columns[e.ColumnIndex].Name != "points")
            {
                return;
            }

            var typed = e.FormattedValue?.ToString()?.Trim() ?? string.Empty;

            var whole = int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);

            // Nor below zero (22 Oct 2026, known issues): points are never
            // taken away for a finishing place - see Season.PointsForPosition.
            if (whole && value >= 0)
            {
                return;
            }

            e.Cancel = true;
            var problem = typed.Length == 0
                ? "Points can't be blank - type 0 for none, or press Esc to put back what was there."
                : whole
                    ? "Points can't be below 0 - a place scores 0 at the least. Fix it, or press Esc to put back what was there."
                    : $"Points must be a whole number - \"{typed}\" isn't one. Fix it, or press Esc to put back what was there.";
            _dgvPointsScheme.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = problem;
            schemeHint.Text = problem;
            schemeHint.ForeColor = AppTheme.Danger;
        };
        _dgvPointsScheme.CellEndEdit += (_, e) =>
        {
            _dgvPointsScheme.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = string.Empty;
            schemeHint.Text = SchemeHintText;
            schemeHint.ForeColor = AppTheme.TextMuted;
        };
        _dgvPointsScheme.CellEndEdit += (_, _) => { if (!_suppressEvents) { SaveCurrentSeasonFromUi(); RecomputeStandings(); } };

        var btnResetScheme = new Button { Text = "Reset to Default" };
        btnResetScheme.Click += BtnResetScheme_Click;

        var btnRules = new Button { Text = "Scoring Rules..." };
        btnRules.Click += (_, _) => EditRules();

        // A short table, shown a few places tall: the card is as tall as it
        // needs to be for that, and the races under it get the rest.
        var scheme = new Section("Points scheme");

        scheme.Add(schemeHint);
        scheme.AddFill(_dgvPointsScheme, SchemeHeight);
        // Custom scoring rules are Advanced (1 Nov 2026, Simple mode).
        scheme.Add(new ButtonBar().With(btnResetScheme, SimpleMode.Hide(btnRules)), gapBelow: 0);

        // --- The races that count ---
        _clbSessions.CheckOnClick = true;
        _clbSessions.IntegralHeight = false;
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

        var sessionsHint = new Label
        {
            Text = "Only sessions recorded as \"Race\" type appear below - practice never counts toward points.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var sessions = new Section("Races counted");

        sessions.Add(sessionsHint);
        sessions.AddFill(_clbSessions, () => ListLeast(_clbSessions, 4), gapBelow: 0);

        // --- The standings ---
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

        // What the table adds up: how many drivers and races, and the rule
        // it scored them by. It wraps rather than being cut short - with a
        // few drivers carrying points in and races forced out, it runs long.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        var btnExportStandings = new Button { Text = "Export Standings to CSV..." };
        btnExportStandings.Click += (_, _) => ExportStandings();

        var btnCarried = new Button { Text = "Points Carried In..." };
        btnCarried.Click += (_, _) => EditCarriedPoints();

        var standings = new Section("Standings");

        standings.AddFill(_dgvStandings, () => GridLeast(_dgvStandings, 3));
        standings.Add(_lblStatus);
        standings.Add(new ButtonBar().With(btnExportStandings, btnCarried), gapBelow: 0);

        // The hints in the small type, at the text size in use.
        _textSize.Wear(schemeHint, () => AppTheme.SmallFont);
        _textSize.Wear(sessionsHint, () => AppTheme.SmallFont);

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_dgvPointsScheme));
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_dgvStandings));

        // --- The page ---
        Beside? outer = null;

        outer = new Beside(() => ColumnWidths(outer?.Width ?? 0).Standings);

        var inner = new Beside(() => ColumnWidths(outer?.Width ?? 0).Middle);

        inner.AddFill(seasons, seasons.LeastHeight);
        inner.AddSideCard(scheme);
        inner.AddSideFill(sessions, sessions.LeastHeight, gapBelow: 0);

        outer.AddFill(inner, inner.LeastHeight);
        outer.AddSideFill(standings, standings.LeastHeight, gapBelow: 0);

        var page = new FillPage();

        page.AddFill(outer, outer.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// The three columns' widths, given the width of all three: the
    /// championships under a fifth and the middle column a quarter,
    /// between 160-280px and 260-420px at the normal text size, at the size
    /// in use - the championships never too narrow for the widest of their
    /// buttons or their caption - and the standings the rest, never under 240px at the
    /// normal text size.
    ///
    /// The standings get the most because they need it: nine columns, each
    /// kept as wide as its heading (Pieces.KeepHeadingsWhole), and past what
    /// those need the list scrolls sideways. The other two were a fifth and
    /// a third, up to 460px, and at 150% every standings heading was cut
    /// short while the scheme's two columns had room to spare (layout
    /// pictures, 29 Sep).
    ///
    /// Too narrow for all three at that - a small window at the largest
    /// text size - and each gives up the same share of its width. Left to
    /// the two Besides, the standings would keep their least and the
    /// championships, whose width is only what the other two leave, would
    /// give up all of theirs. Nothing here scrolls sideways (FillPage).
    /// </summary>
    private (int Seasons, int Middle, int Standings) ColumnWidths(int content)
    {
        var widestButton = _seasonButtons?.Controls.OfType<Button>().Select(button => button.Width).DefaultIfEmpty(0).Max() ?? 0;

        var seasons = Math.Max(
            Math.Clamp((int)(content * 0.18f), UiLayout.AtTextSize(160), UiLayout.AtTextSize(280)),
            Math.Max(widestButton, _seasonsCard?.CaptionWidth ?? 0) + Section.Inset.Horizontal);

        var middle = Math.Clamp((int)(content * 0.25f), UiLayout.AtTextSize(260), UiLayout.AtTextSize(420));

        // What the three share: all of it but the gap between each two -
        // the gap a Beside leaves between its parts and its column.
        var room = Math.Max(0, content - UiLayout.RowGap * 2);
        var least = seasons + middle + UiLayout.AtTextSize(240);

        if (least > room)
        {
            seasons = (int)((long)seasons * room / least);
            middle = (int)((long)middle * room / least);
        }

        return (seasons, middle, Math.Max(0, room - seasons - middle));
    }

    /// <summary>The points scheme's height: its headings and a few places, as the theme sizes its rows - asked whenever the card is measured, so it follows the text size.</summary>
    private int SchemeHeight() =>
        _dgvPointsScheme.ColumnHeadersHeight + SchemeRowsShown * _dgvPointsScheme.RowTemplate.Height + 2;

    /// <summary>The least a grid can do with: its headings and <paramref name="rows"/> rows. Below that the page scrolls instead - see FillPage.</summary>
    private static int GridLeast(DataGridView grid, int rows) =>
        grid.ColumnHeadersHeight + rows * grid.RowTemplate.Height;

    /// <summary>The least a list can do with: <paramref name="rows"/> of its items and its border.</summary>
    private static int ListLeast(ListBox list, int rows) =>
        rows * list.ItemHeight + 6;

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

        // And the rounds planned, which the dialog also sets - undo used to
        // leave the new number in place (the commentary's "rounds to go").
        var wasPlanned = season.PlannedRounds;
        var wasNote = season.AnnouncerNote;

        // And the races ticked in and out, which turning the rule off now
        // writes to (see ApplyRules).
        var wasIncluded = season.IncludedSessionIds.ToList();
        var wasExcluded = season.ExcludedSessionIds.ToList();

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
            current.PlannedRounds = wasPlanned;
            current.AnnouncerNote = wasNote;
            current.IncludedSessionIds = wasIncluded.ToList();
            current.ExcludedSessionIds = wasExcluded.ToList();

            seasons.Upsert(current);
            RefreshAfterUndo();
        });

        ApplyRules(season, form.ApplyTo, _events, _rounds, _history);
        _seasons.Upsert(season);

        LoadSelectedSeasonIntoUi();
    }

    /// <summary>
    /// Writes the Scoring Rules dialog onto a season.
    ///
    /// Turning "Add new races to this championship automatically" off keeps
    /// the races the rule was counting, as ticks of their own. It used to
    /// flip the switch alone, and a hand-picked championship counts only its
    /// ticks - so every race the rule had brought in came off the table (and
    /// the club's online standings) at once, when turning it off is how a
    /// director freezes the table at the end of a season.
    ///
    /// Turned back on, the ticks the rule takes again come off: only the
    /// disagreements are stored (see OverridesFor). Left there, every race
    /// the freeze had copied stayed ticked in by hand, and a later change of
    /// class or race days kept the old races counting.
    /// </summary>
    internal static void ApplyRules(Season season, Action<Season> apply, RaceEventStore? events,
        RoundDefinitionStore rounds, SessionHistoryStore history)
    {
        var wasAuto = season.AutoInclude;
        var counted = wasAuto
            ? EventSpine.SessionsFor(season, events, rounds, history).Select(s => s.Id).ToList()
            : null;

        apply(season);

        if (counted is not null && !season.AutoInclude)
        {
            season.IncludedSessionIds = season.IncludedSessionIds
                .Concat(counted)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        else if (!wasAuto && season.AutoInclude)
        {
            // A race also taken out by hand keeps its tick: the tick is what
            // has it counting, so dropping it would change the table.
            season.IncludedSessionIds = season.IncludedSessionIds
                .Where(id => history.Get(id) is not { } session
                    || !EventSpine.RuleTakes(season, session, rounds, events, history)
                    || season.ExcludedSessionIds.Contains(id, StringComparer.Ordinal))
                .ToList();
        }
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
    /// <remarks>
    /// Stays on the championship being looked at (10 Nov 2026, bug hunt 16).
    /// This runs every time the Standings tab is shown, and a bare refresh
    /// jumped to the first championship in the list - so after a look at
    /// Race History, a tick or points edit landed on the wrong one.
    /// </remarks>
    public void RefreshAll() => RefreshSeasonList(SelectedSeason()?.Id);

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
                // As invariant text (17 Oct 2026, Bug Hunt 8, A26-2): a boxed
                // int is shown in the current culture, a Unicode minus on
                // sv/nb/fi Windows, and CellValidating's invariant parse then
                // refused to let a penalty row's cell be left.
                _dgvPointsScheme.Rows.Add(entry.Position, entry.Points.ToString(CultureInfo.InvariantCulture));
            }

            // Asked fresh, so anything recorded since this tab was last
            // open is already in it - and nothing is written to disk just
            // for opening a tab, because there is no materialised list to
            // keep in step any more.
            var counted = EventSpine.SessionsFor(season, _events, _rounds, _history)
                .Select(x => x.Id)
                .ToHashSet(StringComparer.Ordinal);

            var raceSessions = _history.All.Where(s => s.SessionType == RaceSessionType.Race && !s.IsNotScored()).ToList();

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

                var display = $"{session.StartedAtUtc.ToLocalTime():yyyy-MM-dd} {AppTime.TimeOfDay(session.StartedAtUtc.ToLocalTime())} - {label}{trackPart}{kindPart}";

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
            // The grid refuses a non-number as it is typed; should one reach
            // here anyway, keep what is on file for that place rather than
            // quietly scoring it 0.
            var points = int.TryParse(row.Cells["points"].Value?.ToString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
                ? p
                : season.PointsScheme.FirstOrDefault(e => e.Position == position)?.Points ?? 0;
            // Clamped as it is saved too, should a negative reach here past
            // the cell's check (a paste, an old file's row): 0 at the least.
            scheme.Add(new PointsSchemeEntry { Position = position, Points = Math.Max(0, points) });
        }

        var rows = new List<(SessionRecord Session, bool Ticked)>();

        for (var i = 0; i < _clbSessions.Items.Count; i++)
        {
            if (_clbSessions.Items[i] is SessionListItem item)
            {
                rows.Add((item.Session, _clbSessions.GetItemChecked(i)));
            }
        }

        var (forcedIn, forcedOut) = OverridesFor(season, rows, _rounds, _events, _history);

        // One undo step per change, like every other edit on this page.
        // Without it a mistaken tick took no step, so Ctrl+Z straight
        // afterward undid whatever was captured before it - a rename, or
        // carried points on another championship - and left the tick as it
        // was. The step is only taken when something really changed: the
        // points grid ends an edit even when the value was typed back the
        // same, and an empty step would make the next Undo seem to do
        // nothing. The scheme is compared in position order, the order the
        // grid shows it in, and the override lists as sets, since the order
        // they are written in carries no meaning.
        var schemeChanged = !season.PointsScheme
            .OrderBy(p => p.Position)
            .Select(p => (p.Position, p.Points))
            .SequenceEqual(scheme.OrderBy(p => p.Position).Select(p => (p.Position, p.Points)));
        var racesChanged = !season.IncludedSessionIds.ToHashSet().SetEquals(forcedIn)
            || !season.ExcludedSessionIds.ToHashSet().SetEquals(forcedOut);

        if (schemeChanged || racesChanged)
        {
            // Copies of what is on file now, taken before it is replaced,
            // so later edits to the season cannot reach what Undo restores.
            var seasons = _seasons;
            var seasonId = season.Id;
            var oldScheme = season.PointsScheme
                .Select(p => new PointsSchemeEntry { Position = p.Position, Points = p.Points })
                .ToList();
            var oldIncluded = season.IncludedSessionIds.ToList();
            var oldExcluded = season.ExcludedSessionIds.ToList();

            // Named for what the director did: a tick never touches the
            // scheme, while a points edit can also drop an override for a
            // race no longer in the list, which is not what they changed.
            var what = schemeChanged ? "Points" : "Races counted";

            _undo.Capture($"{what} for '{season.Name}'", () =>
            {
                // Onto the championship as it is on file now, by id - one
                // deleted since stays deleted rather than being written back.
                if (seasons.Get(seasonId) is not { } current)
                {
                    throw new InvalidOperationException("that championship has been deleted since. Nothing was changed.");
                }

                current.PointsScheme = oldScheme
                    .Select(p => new PointsSchemeEntry { Position = p.Position, Points = p.Points })
                    .ToList();
                current.IncludedSessionIds = oldIncluded.ToList();
                current.ExcludedSessionIds = oldExcluded.ToList();

                seasons.Upsert(current);
                RefreshAfterUndo();
            });
        }

        season.PointsScheme = scheme;
        season.IncludedSessionIds = forcedIn;
        season.ExcludedSessionIds = forcedOut;

        _seasons.Upsert(season);
    }

    /// <summary>
    /// The two override lists the ticks under Races counted come to.
    ///
    /// A tick means "this race counts". WHY it counts decides where it is
    /// recorded: if the rule already takes it, ticking is simply agreement
    /// and nothing needs storing - only the disagreements have to be
    /// written down. That is what keeps both override lists small, rather
    /// than the season carrying a copy of every race it happens to include.
    /// </summary>
    internal static (List<string> ForcedIn, List<string> ForcedOut) OverridesFor(
        Season season,
        IEnumerable<(SessionRecord Session, bool Ticked)> rows,
        RoundDefinitionStore rounds,
        RaceEventStore? events,
        SessionHistoryStore history)
    {
        var forcedIn = new List<string>();

        // The races already taken out by hand stay out until one is ticked
        // back in. Rebuilt from the ticks alone, an exclusion was thrown
        // away on the next tick or points edit once the rule stopped taking
        // that race - turned off, or narrowed to another class - and turning
        // the rule back on put the rained-off round back in the table.
        var forcedOut = season.ExcludedSessionIds.ToList();

        foreach (var (session, ticked) in rows)
        {
            // The same test the table counts by - a race whose heat was run
            // again is not the rule's, so leaving it unticked excludes
            // nothing and ticking it puts it in by hand.
            var byRule = season.AutoInclude && EventSpine.RuleTakes(season, session, rounds, events, history);

            if (ticked)
            {
                forcedOut.RemoveAll(id => string.Equals(id, session.Id, StringComparison.Ordinal));
            }

            if (byRule && !ticked)
            {
                if (!forcedOut.Contains(session.Id, StringComparer.Ordinal))
                {
                    forcedOut.Add(session.Id);
                }
            }
            else if (!byRule && ticked)
            {
                forcedIn.Add(session.Id);
            }
        }

        return (forcedIn, forcedOut);
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

    /// <summary>
    /// Asks for one line of text over this page - a new championship's name,
    /// or a new name for one - in the app's shared prompt (TextPromptForm):
    /// the question, the box under it with the default in it, and OK and
    /// Cancel. Null when cancelled; the answer as typed otherwise, which the
    /// callers trim.
    ///
    /// That prompt is this window exactly, on the layout pieces - the one
    /// the Tech Checklist, a chassis sheet and a setup sheet's fields ask
    /// with. This one used to be built here, in pixels, where the layout test
    /// could not reach it: a 380x150 window with the box a fixed 340px, and
    /// OK and Cancel a fixed 80px wide at fixed places under it whatever the
    /// text size.
    /// </summary>
    private string? PromptForText(string title, string prompt, string defaultValue)
    {
        using var dialog = new TextPromptForm(title, prompt, defaultValue);

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Answer : null;
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
