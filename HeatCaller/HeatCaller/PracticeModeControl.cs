namespace HeatCaller;

/// <summary>
/// Open practice: no staging, no flags, no finish - just live lap stats for
/// whoever's on track. Arguably the most-used mode at a home track, where
/// most of the time is spent lapping and tuning rather than running formal
/// races, and where the full Race Control flow is more ceremony than the
/// situation calls for.
///
/// Reads the same RaceControlSession lap feed as Race Control, but presents
/// it as per-driver rolling stats (last lap, best, rolling average,
/// consistency) rather than a finishing order.
/// </summary>
public sealed class PracticeModeControl : UserControl
{
    private readonly RaceTiming _race;
    private readonly ProfileStore _profiles;
    private readonly SessionHistoryStore _history;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly AppSettings _settings;
    private readonly CheckBox _chkAutoSave = new ThemedCheckBox();

    private readonly DataGridView _grid = new();
    private readonly Label _lblStatus = new();
    private readonly NumericUpDown _numRollingWindow = new();
    private readonly ComboBox _cmbTrack = new();

    /// <summary>Suppresses the track-changed save while the list is rebuilt.</summary>
    private bool _repopulatingTracks;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 500 };

    private Action<RacerStanding, bool, bool>? _lapHandler;
    private bool _active;
    private DateTime? _startedAtUtc;

    // What the shared session held for the NEXT RACE before practice took
    // it over - its limits and its start type - so Stop can hand them
    // back. Stop used to clear the limits instead, and the race after a
    // practice then ran with no lap or time limit at all.
    private int? _lapLimitBeforePractice;
    private TimeSpan? _timeLimitBeforePractice;
    private RaceStartType _startTypeBeforePractice = RaceStartType.Standard;

    /// <summary>
    /// Raised when a practice stops and the shared session is handed back
    /// to Race Control. The host wires it to Race Control's RefreshFormat,
    /// so the next race's format is back in the session at once rather
    /// than at its Stage.
    /// </summary>
    public event Action? PracticeStopped;

    /// <summary>
    /// Raised when anything the gate-pass start depends on here changes - the
    /// tick box, the targets, a practice starting or stopping. The host
    /// (MainForm) hands the timing thread a fresh plan from it in threaded
    /// mode (see AutoStartTargets); inline it does nothing with it.
    /// </summary>
    public event Action? AutoStartConditionsChanged;

    /// <summary>
    /// True while StartPractice is under way, from its check that the session
    /// may be cleared to its end - see AdoptAutoStartedPractice.
    /// </summary>
    private bool _starting;

    /// <summary>True for the whole of StartPractice: no plan is offered for a gate-pass start meanwhile (AutoStartTargets).</summary>
    private bool _holdAutoStart;

    /// <summary>
    /// The track this practice is run on - the layout picked on this page,
    /// or null for "(none)". The host hands it to the ghost cars at the
    /// green, and the save below files the practice under it, so a PB or
    /// track-record ghost is paced from the same track the practice laps
    /// are kept under. The ghosts used to be given Race Control's track
    /// instead, which is often a different layout or "(none)" - and then a
    /// "beat your PB" ghost ran at the driver's best on some other track,
    /// or was skipped as having no PB here when there was one.
    ///
    /// The picker is only filled when the page is first shown (RefreshAll),
    /// so a practice started by a car crossing the gate before anyone has
    /// opened the page found it empty. The remembered layout stands in
    /// then - the one the picker would have landed on.
    ///
    /// UI thread only, as the picker is.
    /// </summary>
    public string? SelectedTrackName
    {
        get
        {
            // Before the picker has been filled, the remembered layout - but
            // only while it still exists, as RefreshAll would also fall back
            // to "(none)" for one renamed or deleted since.
            var remembered = _settings.PracticeTrackLayout;
            var name = _cmbTrack.Items.Count == 0
                ? (_trackLayouts.All.Any(t => string.Equals(t.Name, remembered, StringComparison.OrdinalIgnoreCase)) ? remembered : null)
                : _cmbTrack.SelectedItem as string;

            return string.IsNullOrWhiteSpace(name) || name == "(none)" ? null : name;
        }
    }

    private readonly KpiTracker _tracker = new();
    private List<KpiDefinition> _columns = new();

    // Optional - only needed for the Big Screen button. Practice is
    // constructible without them (e.g. in a context with no announcer),
    // in which case the button just stays disabled.
    private readonly AnnouncementService? _announcer;
    private readonly DayScheduleStore? _daySchedule;
    private readonly SponsorStore? _sponsors;
    private readonly UndoService? _undo;

    /// <summary>
    /// Race Control's qualifying projector and its live badge store, handed
    /// to a Big Screen opened from here so it shows the same Qual and Badges
    /// columns as one opened from Race Control. Optional: null when the main
    /// window did not supply them (see the Open Big Screen button).
    /// </summary>
    private readonly QualifyingProjector? _projector;
    private readonly Func<AchievementStore?>? _achievements;

    /// <summary>How stale the badge counts may get on a Big Screen opened
    /// from here when no live badge store was handed over.</summary>
    private static readonly TimeSpan BadgeReloadInterval = TimeSpan.FromSeconds(30);

    private readonly RaceClockPanel _clock;
    private readonly Button _btnBigScreen = new();
    private readonly System.Windows.Forms.Timer _clockRefreshTimer = new() { Interval = 250 };

    private readonly NumericUpDown _numLapTarget = new();
    private readonly NumericUpDown _numTimeTarget = new();
    private readonly CheckBox _chkAutoStartOnGate = new ThemedCheckBox();
    private readonly GhostStore _ghostStore;
    private readonly CheckBox _chkGhosts = new ThemedCheckBox();
    private readonly CheckedListBox _clbGhosts = new ThemedCheckedListBox();
    private readonly SteadyLabel _lblGhostHint = new();

    /// <summary>The ghosts' card and its button, which the card's width is measured from - see GhostsWidth.</summary>
    private Section? _ghostsCard;
    private ButtonBar? _ghostButtons;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The track picker's width as last measured - see Pieces.PickerWidth. Forgotten when the layouts are read again (RefreshAll).</summary>
    private (Font Font, int Count, int Width)? _trackWidth;

    /// <summary>The clock column's width at its narrowest - the clock, and Open Big Screen under it. Wider when that button's caption needs it. At the screen's DPI, as the clock's point-size text is.</summary>
    private static int ClockWidth => UiLayout.AtDpi(260);

    /// <summary>The clock's height. It is drawn, and drawn to fit whatever it is given. At the screen's DPI, as the clock's point-size text is.</summary>
    private static int ClockHeight => UiLayout.AtDpi(206);

    /// <summary>The ghost card's width at the normal text size, at its narrowest and widest - see GhostsWidth.</summary>
    private const int GhostsLeastWidth = 260;
    private const int GhostsMostWidth = 360;

    /// <summary>The narrowest the setup card beside it gets, at the normal text size.</summary>
    private const int SetupLeastWidth = 420;

    /// <summary>The fewest ghosts the ghost list shows before the page scrolls instead.</summary>
    private const int GhostListLeastRows = 3;

    /// <summary>The fewest drivers this session's table shows under its headings before the page scrolls instead.</summary>
    private const int GridLeastRows = 3;

    /// <summary>What the ghost hint says with no ghosts, and with them switched off - the longest of what it says, with the count, is measured for it (SteadyLabel).</summary>
    private const string NoGhostsHint = "No ghosts set up yet - press Manage to create one.";
    private const string GhostsOffHint = "Ghosts are switched off for now.";

    // True while RefreshGhosts is loading the list from the store, so the
    // ItemCheck / CheckedChanged handlers don't treat those programmatic
    // check changes as user edits (and re-save the store mid-rebuild).
    private bool _populatingGhosts;

    private readonly Label _lblIntro = new();

    public PracticeModeControl(RaceTiming race, ProfileStore profiles, SessionHistoryStore history, TrackLayoutStore trackLayouts, AppSettings settings,
        GhostStore ghostStore,
        AnnouncementService? announcer = null, DayScheduleStore? daySchedule = null, SponsorStore? sponsors = null,
        UndoService? undo = null,
        QualifyingProjector? projector = null, Func<AchievementStore?>? achievements = null)
    {
        _projector = projector;
        _achievements = achievements;
        _undo = undo;
        _ghostStore = ghostStore;
        _announcer = announcer;
        _daySchedule = daySchedule;
        _sponsors = sponsors;
        _clock = new RaceClockPanel(race);
        _race = race;
        _profiles = profiles;
        _history = history;
        _trackLayouts = trackLayouts;
        _settings = settings;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No MinimumSize: the 980x590 floor was honoured over the size the
        // page was given, and a small window lost the table off the bottom.
        // A short window scrolls the page instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();

        // The grid's columns, from the saved selection. Nothing else built
        // them - only the Choose Columns dialog did - so the grid had none,
        // and the first lap of every practice threw from Rows.Add ("no row
        // can be added to a DataGridView that has no columns") on each
        // refresh: the lap handler and the half-second timer alike.
        RebuildColumns();

        RefreshGhosts();

        _refreshTimer.Tick += (_, _) =>
        {
            if (_active)
            {
                RefreshGrid();
                CheckPracticeLengthTarget();
            }
        };

        // Runs continuously rather than only while a practice is active, so
        // the clock shows a correct idle ring before Start is pressed too -
        // matching how Race Control's own clock timer behaves.
        _clockRefreshTimer.Tick += (_, _) => _clock.Invalidate();
        _clockRefreshTimer.Start();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }


    /// <summary>Raised when the user wants the full Ghost Cars page.</summary>
    public event Action? GhostsPageRequested;

    /// <summary>
    /// "Chase a Best Lap..." (22 Oct 2026): pick a driver - those on the
    /// board first - and a ghost of their own best lap joins the practice,
    /// "Sam's best", at their best tonight on this layout or their best ever
    /// on it before tonight's. Picked again for the same driver, it is
    /// brought up to their latest best rather than added twice. Ghost racing
    /// is switched on with it, and in a running practice it goes straight on
    /// track (MainForm.KeepRunningGhostsInStep). One Undo puts the ghosts
    /// back as they were. See OwnBestGhost.
    /// </summary>
    private void ChaseABestLap()
    {
        var onTrack = _race.GetOrderedStandings().Select(s => s.TransponderId);
        var candidates = OwnBestGhost.Candidates(_profiles.All, onTrack);

        if (candidates.Count == 0)
        {
            AppDialog.Show(this,
                "No driver profiles yet - a driver's best lap is what the ghost runs at, so there needs to be "
                + "at least one driver on file.",
                "Nobody to chase", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new GhostRivalPickerForm(candidates,
            title: "Chase a Best Lap",
            question: "Whose best lap should the ghost run at?",
            okText: "Chase");

        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedTransponder is not { } transponder)
        {
            return;
        }

        var profile = _profiles.Get(transponder);
        var before = _ghostStore.Snapshot();

        var (ghost, refreshed) = OwnBestGhost.ChaseFor(_ghostStore.Current, transponder, profile?.DriverName, profile?.CarModel);

        var store = _ghostStore;
        _undo?.Capture(refreshed ? $"Bring {ghost.Name} up to date" : $"Chase {ghost.Name}", () =>
        {
            store.Restore(before);
            RefreshGhosts();
        });

        _ghostStore.Save();
        RefreshGhosts();
    }

    /// <summary>Wraps a ghost so the list shows its name and pace, not its ID.</summary>
    private sealed record GhostListItem(GhostCar Ghost)
    {
        public override string ToString()
        {
            var pace = Ghost.PacePercent == 100 ? string.Empty : $"  ({Ghost.PacePercent}%)";
            return $"{Ghost.Name}{pace}";
        }
    }

    /// <summary>
    /// Rebuilds the ghost list from the store.
    ///
    /// Called on show as well as at build time, so a ghost added on the
    /// Ghost Cars page appears here without restarting.
    /// </summary>
    public void RefreshGhosts()
    {
        _populatingGhosts = true;
        try
        {
            _clbGhosts.Items.Clear();

            foreach (var ghost in _ghostStore.Current.Ghosts)
            {
                var index = _clbGhosts.Items.Add(new GhostListItem(ghost));
                _clbGhosts.SetItemChecked(index, ghost.Enabled);
            }

            _chkGhosts.Checked = _ghostStore.Current.Enabled;
        }
        finally
        {
            _populatingGhosts = false;
        }

        UpdateGhostState();
    }

    private void UpdateGhostState()
    {
        _clbGhosts.Enabled = _chkGhosts.Checked;

        if (_ghostStore.Current.Ghosts.Count == 0)
        {
            _lblGhostHint.Text = NoGhostsHint;
            return;
        }

        var on = _ghostStore.Current.Ghosts.Count(g => g.Enabled);

        _lblGhostHint.Text = _chkGhosts.Checked
            ? $"{Plural.Of(on, "ghost")} will join this practice session."
            : GhostsOffHint;
    }

    /// <summary>
    /// What practice is, the buttons that start and stop it and the two
    /// switches that go with them, then two cards side by side - how it is
    /// set up, and the ghosts it runs against - with the clock and Open Big
    /// Screen down the right beside all of it. Under that, this session's
    /// table in a card that takes the height left over. On the layout
    /// pieces (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be placed in pixels against a 980px page: the intro
    /// wrapped to wherever the clock happened to be, the ghost list was
    /// 280x74 at every text size, the captions sat in a column 180px wide
    /// whatever they said, and under all of it a 980x590 floor the page kept
    /// however small its window. And the table's columns were never told to
    /// fill it, so they sat at 100px each whatever its width.
    /// </summary>
    private void BuildLayout()
    {
        // --- What this page does ---

        _lblIntro.Text =
            "Open lapping with no race structure - every transponder crossing the line is tracked. Saved to Race "
            + "History as Practice: it feeds Personal Bests and the Lap Times chart, but never track records, "
            + "standings or season points.";
        _lblIntro.AutoSize = true;
        _lblIntro.ForeColor = AppTheme.TextMuted;

        // --- Starting and stopping ---

        var btnStart = new Button { Text = "Start Practice" };
        btnStart.Click += (_, _) => StartPractice();

        var btnStop = new Button { Text = "Stop" };
        btnStop.Click += (_, _) => StopPractice();

        var btnClear = new Button { Text = "Clear Stats" };
        btnClear.Click += (_, _) => ClearStats();

        var btnSave = new Button { Text = "Save Session" };
        btnSave.Click += (_, _) => SaveSession(silent: false);

        _chkAutoSave.Text = "Save automatically on stop";
        _chkAutoSave.AutoSize = true;
        _chkAutoSave.Checked = _settings.AutoSavePractice;
        _chkAutoSave.CheckedChanged += (_, _) => { _settings.AutoSavePractice = _chkAutoSave.Checked; _settings.Save(); };

        _chkAutoStartOnGate.Text = "Start automatically when a car crosses the gate";
        _chkAutoStartOnGate.AutoSize = true;
        _chkAutoStartOnGate.Checked = _settings.PracticeAutoStartOnGatePass;
        _chkAutoStartOnGate.CheckedChanged += (_, _) =>
        {
            _settings.PracticeAutoStartOnGatePass = _chkAutoStartOnGate.Checked;
            _settings.Save();

            // Threaded, the timing thread is told at once - unticked, no car
            // may start practice from here on (24 Oct 2026, timing layer, P7).
            AutoStartConditionsChanged?.Invoke();
        };

        // Side by side, and one under the other when the line is too narrow
        // for both (FieldRow).
        var switches = new FieldRow();

        switches.Add(null, _chkAutoSave);
        switches.Add(null, _chkAutoStartOnGate);

        // --- Setup ---

        _cmbTrack.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrack.SelectedIndexChanged += (_, _) =>
        {
            if (_repopulatingTracks)
            {
                return;
            }

            var selected = _cmbTrack.SelectedItem as string;
            _settings.PracticeTrackLayout = selected is null or "(none)" ? string.Empty : selected;
            _settings.Save();
        };

        _numLapTarget.Minimum = 0;
        _numLapTarget.Maximum = 9999;
        _numLapTarget.Value = Math.Clamp(_settings.PracticeLapLimit, 0, 9999);
        _numLapTarget.ValueChanged += (_, _) => { _settings.PracticeLapLimit = (int)_numLapTarget.Value; _settings.Save(); AutoStartConditionsChanged?.Invoke(); };

        _numTimeTarget.Minimum = 0;
        _numTimeTarget.Maximum = 180;
        _numTimeTarget.Value = Math.Clamp(_settings.PracticeTimeLimitMinutes, 0, 180);
        _numTimeTarget.ValueChanged += (_, _) => { _settings.PracticeTimeLimitMinutes = (int)_numTimeTarget.Value; _settings.Save(); AutoStartConditionsChanged?.Invoke(); };

        _numRollingWindow.Minimum = 2;
        _numRollingWindow.Maximum = 50;

        // Not persisted - matches the original behaviour rather than
        // inventing a setting that nothing else reads.
        _numRollingWindow.Value = 5;
        _numRollingWindow.ValueChanged += (_, _) => RefreshGrid();

        // The number boxes as wide as they were at the normal text size, at
        // the size in use, and the track picker as wide as its longest
        // layout - again when the text size changes, and when the layouts
        // are read again (RefreshAll).
        _textSize.Keep(SizeFields);

        var fields = new FieldGrid();

        fields.AddRow("Track:", _cmbTrack);
        // Each box with its unit, so a narrow card puts "0 minutes" under
        // "0 laps" rather than leaving "minutes" on a line of its own
        // (layout pictures, 29 Sep: 1280x720 and 200%).
        fields.AddRow("Practice length:", FieldGrid.Inline(
            FieldGrid.Inline(_numLapTarget, new Label { Text = "laps" }),
            FieldGrid.Inline(_numTimeTarget, new Label { Text = "minutes" })));
        fields.AddHint("0 = no limit. Practice stops by itself when either is reached.");
        fields.AddRow("Rolling average over:", FieldGrid.Inline(_numRollingWindow, new Label { Text = "laps" }));
        fields.AddHint("Streak = clean laps now; Best Streak = longest this session; Streak Pace = average lap in the current streak.");

        var setup = new Section("Setup");

        setup.Add(fields, gapBelow: 0);

        // --- Ghost cars ---
        //
        // On the practice page because this is what ghosts are FOR: racing
        // something when nobody else is on track. Making someone configure
        // them on another page first, then come back, misses the point.

        _chkGhosts.Text = "Race ghost cars";
        _chkGhosts.AutoSize = true;
        _chkGhosts.Checked = _ghostStore.Current.Enabled;
        _chkGhosts.CheckedChanged += (_, _) =>
        {
            if (_populatingGhosts) return;
            _ghostStore.Current.Enabled = _chkGhosts.Checked;
            _ghostStore.Save();
            UpdateGhostState();
        };

        _clbGhosts.CheckOnClick = true;
        _clbGhosts.BorderStyle = BorderStyle.FixedSingle;

        // Off, or the list rounds its height down to a whole number of rows
        // and leaves a gap under it that nothing in the layout explains.
        _clbGhosts.IntegralHeight = false;
        _clbGhosts.ItemCheck += (_, args) =>
        {
            // ItemCheck fires BEFORE the state changes, so GetItemChecked would
            // be one behind - but args.NewValue already carries the new state,
            // so no deferral is needed. (The old BeginInvoke deferral crashed
            // at startup: RefreshGhosts runs from the constructor, before the
            // window handle exists, and SetItemChecked raises this event.)
            if (_populatingGhosts) return;

            if (_clbGhosts.Items[args.Index] is GhostListItem item)
            {
                item.Ghost.Enabled = args.NewValue == CheckState.Checked;
                _ghostStore.Save();
                UpdateGhostState();
            }
        };

        // As tall as the longest thing it says, so rewording it never moves
        // the button under it.
        _lblGhostHint.ForeColor = AppTheme.TextMuted;
        _lblGhostHint.Wordings = new[] { NoGhostsHint, GhostsOffHint, "99 ghosts will join this practice session." };
        _textSize.Wear(_lblGhostHint, () => AppTheme.SmallFont);

        var btnManageGhosts = new Button { Text = "Manage..." };
        btnManageGhosts.Click += (_, _) => GhostsPageRequested?.Invoke();

        // Chase your own ghost (22 Oct 2026) - see OwnBestGhost.
        var btnChaseBest = new Button { Text = "Chase a Best Lap..." };
        btnChaseBest.Click += (_, _) => ChaseABestLap();

        _ghostButtons = new ButtonBar().With(btnChaseBest, btnManageGhosts);

        _ghostsCard = new Section("Ghost cars");

        _ghostsCard.Add(_chkGhosts, stretch: false);
        _ghostsCard.AddFill(_clbGhosts, () => GhostListItemHeight() * GhostListLeastRows + 4);
        _ghostsCard.Add(_lblGhostHint);
        _ghostsCard.Add(_ghostButtons, gapBelow: 0);

        // The two cards side by side in a band as tall as the taller needs,
        // the ghosts down the right and filling the band's height, so the
        // two end level.
        Beside? cards = null;

        cards = new Beside(() => GhostsWidth(cards?.Width ?? 0))
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        cards.AddFill(setup, setup.LeastHeight);
        cards.AddSideFill(_ghostsCard, _ghostsCard.LeastHeight, gapBelow: 0);

        // --- The clock and the Big Screen, down the right ---

        _clock.Height = ClockHeight;

        _btnBigScreen.Text = "Open Big Screen";
        _btnBigScreen.Enabled = _announcer is not null;
        _btnBigScreen.Click += (_, _) =>
        {
            if (_announcer is not null)
            {
                // The window is not tied to the practice: it watches the
                // shared session and often stays on the wall through the
                // heats and mains that follow, which DO award badges and
                // DO have a qualifying order. So it gets the same live
                // sources Race Control's own Big Screen uses when the main
                // window handed them over. Without them, the badges come
                // from disk, re-read at most every BadgeReloadInterval (not
                // on every half-second refresh), and the Qual column stays
                // empty, as there is no projector to fill it.
                var achievements = _achievements ?? ThrottledAchievementLoader();

                var screen = new BigScreenForm(_race, _announcer, _profiles, _daySchedule, _sponsors, _settings,
                    _projector, achievements);

                // Its console lines - the sponsor video path, which has
                // nowhere else to report. See BigScreenForm.StatusMessage.
                screen.StatusMessage += line => StatusMessage?.Invoke(line);

                screen.Show();
            }
        };

        var top = new Beside(sideLeastWidth: ClockWidth)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        top.Add(_lblIntro);
        top.Add(new ButtonBar().With(btnStart, btnStop, btnClear, btnSave));
        top.Add(switches);
        top.Add(cards, gapBelow: 0);

        top.AddSide(_clock);
        top.AddSide(_btnBigScreen, gapBelow: 0);

        // --- This session ---

        // Its own face, so the line kept for it is measured in the face it
        // is drawn in - see TextSize.ReserveLines. It says something from
        // the start: an empty line over the table read as a gap.
        _lblStatus.Text = "Not running.";
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblStatus, () => AppTheme.BodyFont);
        _textSize.ReserveLines(_lblStatus, 1);

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // Every heading whole, at the text size in use - see
        // Pieces.KeepHeadingsWhole. Given again whenever the columns are
        // built again (RebuildColumns).
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));

        var btnColumns = new Button { Text = "Choose Columns..." };
        btnColumns.Click += (_, _) =>
        {
            using var form = new KpiPickerForm("Practice", _settings.PracticeKpis, KpiCatalog.DefaultPractice);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                // The picker's Restore Defaults lands here too, so the old
                // list is kept for Undo. This page lives as long as the app.
                var before = _settings.PracticeKpis.ToList();
                if (!before.SequenceEqual(form.SelectedKeys))
                {
                    _undo?.Capture("Change the Practice columns", () =>
                    {
                        _settings.PracticeKpis = before.ToList();
                        _settings.Save();

                        if (!IsDisposed)
                        {
                            RebuildColumns();
                            RefreshGrid();
                        }
                    });
                }

                _settings.PracticeKpis = form.SelectedKeys;
                _settings.Save();
                RebuildColumns();
                RefreshGrid();
            }
        };

        var session = new Section("This session");

        session.Add(_lblStatus);
        session.AddFill(_grid, () => _grid.ColumnHeadersHeight + GridLeastRows * _grid.RowTemplate.Height);
        session.Add(new ButtonBar().With(btnColumns), gapBelow: 0);

        var page = new FillPage();

        page.Add(top);
        page.AddFill(session, session.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// The fields' sizes at the text size in use: the number boxes the width
    /// they were at the normal text size, at this size, and the track picker
    /// as wide as its longest layout and never narrower than a field was
    /// then. Run again when the text size changes (TextSize.Keep) and when
    /// the layouts are read again (RefreshAll).
    /// </summary>
    private void SizeFields()
    {
        var number = UiLayout.AtTextSize(UiLayout.NumericWidth);

        Fit(_cmbTrack, Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbTrack, ref _trackWidth)));
        Fit(_numLapTarget, number);
        Fit(_numTimeTarget, number);
        Fit(_numRollingWindow, number);

        // Only when it changed: a new width lays the grid out again.
        static void Fit(Control field, int width)
        {
            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>
    /// The ghost card's width: about a third of the band, between 260px and
    /// 360px at the normal text size, at the size in use - and never
    /// narrower than its caption, its switch or its widest button need. The setup
    /// card has the rest, and never less than 420px at the text size in
    /// use; too narrow for both at that, and each gives up the same share
    /// of its width, as Batteries' two cards do. Nothing here scrolls
    /// sideways (FillPage).
    /// </summary>
    private int GhostsWidth(int band)
    {
        // The widest one button, not the whole row (22 Oct 2026): with
        // Chase a Best Lap... beside Manage... the row on one line could ask
        // for more than a narrow window at a large text size has to give, so
        // a tight card lets the bar wrap its two buttons onto two lines (it
        // always could - ButtonBar wraps) rather than claiming the width.
        var widestButton = _ghostButtons?.Controls.OfType<Control>()
            .Where(part => part.Visible || !_ghostButtons.Visible)
            .Select(part => part.Width + part.Margin.Horizontal)
            .DefaultIfEmpty(0)
            .Max() ?? 0;

        var needs = Math.Max(
            Math.Max(_ghostsCard?.CaptionWidth ?? 0, _chkGhosts.GetPreferredSize(Size.Empty).Width),
            widestButton) + Section.Inset.Horizontal;

        var ghosts = Math.Max(
            Math.Clamp((int)(band * 0.34f), UiLayout.AtTextSize(GhostsLeastWidth), UiLayout.AtTextSize(GhostsMostWidth)),
            needs);

        var setupLeast = UiLayout.AtTextSize(SetupLeastWidth);

        // What the two share: all of the band but the gap a Beside leaves
        // between its parts and its column.
        var room = Math.Max(0, band - UiLayout.RowGap);

        if (ghosts + setupLeast > room)
        {
            ghosts = (int)((long)ghosts * room / (ghosts + setupLeast));
        }

        return Math.Max(0, ghosts);
    }

    /// <summary>A row of the ghost list, in the face it is drawn in.</summary>
    private int GhostListItemHeight() => Math.Max(_clbGhosts.ItemHeight, _clbGhosts.Font.Height);

    /// <summary>
    /// Starts practice from outside - used by the gate-pass auto-start.
    ///
    /// Returns false if a session is already running, so the caller can
    /// tell a real start from a no-op rather than assuming it worked.
    /// </summary>
    /// <param name="triggeringDeviceTimeMs">
    /// The decoder time of the crossing that triggered this start
    /// (DirectLapEvent.DeviceTimeMs), when the caller has it. The green is
    /// then stamped just before that crossing, so it counts as the driver's
    /// start lap. Without it the green is stamped from the estimated decoder
    /// clock, which can land a few milliseconds AFTER the crossing - and the
    /// crossing, then earlier than the start, was thrown away.
    /// </param>
    public bool TryAutoStartPractice(long? triggeringDeviceTimeMs = null)
    {
        // Only from a genuinely idle session. A car crossing the gate while
        // a race is staged, counting down, running - or finished and still
        // on the board - is part of that race, and nothing here may clear it.
        //
        // A race's warm-up is not a RaceState of its own - the session stays
        // Idle through it - so it is asked about separately. A car on its
        // warm-up laps used to start practice here, which cancelled the
        // warm-up and the Stage waiting behind it, and left Race Control
        // refusing to stage until someone found the Practice page.
        if (_active || _race.State != RaceState.Idle || _race.IsWarmingUp)
        {
            return false;
        }

        // Uses whatever is currently on the page, which is last session's
        // settings - the track, lap/time targets and column choices all
        // persist, so this genuinely picks up where practice left off.
        //
        // Green at once - see StartPractice - because the host hands the
        // crossing that triggered this to the session straight afterwards,
        // and it has to find practice already running to be counted.
        StartPractice(fromGatePass: true, triggeringDeviceTimeMs: triggeringDeviceTimeMs);
        return _active;
    }

    /// <summary>
    /// Threaded mode: the lap and time targets a practice started at the gate
    /// would take from this page now - read exactly as StartPractice reads
    /// them - or null when this page may not have practice started for it:
    /// one is running, one is being started, or the page is gone. UI thread.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P7): the host builds the timing
    /// thread's plan from this (see RaceTiming.PracticeAutoStart). The race
    /// being Idle with no warm-up is the timing side's own check, made at the
    /// crossing - and at Idle with no warm-up ConfirmRaceControlCanBeCleared
    /// lets practice through without a dialog, so nothing that asks the
    /// person is skipped by starting practice there.
    /// </remarks>
    internal (int? LapLimit, TimeSpan? TimeLimit)? AutoStartTargets()
    {
        if (_active || _starting || _holdAutoStart || IsDisposed || Disposing)
        {
            return null;
        }

        return (
            (int)_numLapTarget.Value == 0 ? null : (int)_numLapTarget.Value,
            (int)_numTimeTarget.Value == 0 ? null : TimeSpan.FromMinutes((double)_numTimeTarget.Value));
    }

    /// <summary>
    /// Threaded mode, on the UI thread: the timing thread has started practice
    /// at the gate (RaceTiming.PostLinePass) - the engine's part of
    /// StartPractice is done and the crossing counted. This page takes the
    /// running session over WITHOUT resetting it: the rest of StartPractice,
    /// with the before-values the timing thread kept. True when it did, so
    /// the host can say so in the console.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY (24 Oct 2026, timing layer, P7). Delivered in order with the race's
    /// events - after the start's, before the crossing's lap - and ahead of
    /// any command this window issues, so a click always finds the page as
    /// the race already is. When it cannot take the practice over:
    /// </para>
    /// <list type="bullet">
    /// <item>A Start pressed here is under way (_starting): that start is
    /// taking the session anyway, and resets it. Only the before-values are
    /// kept from the notice - they are the session's as it was before either
    /// practice, while the ones that start read may already be the gate
    /// practice's targets.</item>
    /// <item>The page is gone (the app is closing): there is no page to stop
    /// it, so the session is handed back here as Stop would - reset, practice
    /// mode off, the race's limits and start type back. Practice is not
    /// journalled, so there is no journal to clear.</item>
    /// <item>A practice is already running here: left as it is. (The timing
    /// side starts no second practice until this one is taken over.)</item>
    /// <item>The session is no longer practice (something else took it -
    /// a race recovery's restore): left to whoever has it, with a line in
    /// the console.</item>
    /// </list>
    /// </remarks>
    internal bool AdoptAutoStartedPractice(PracticeAutoStarted notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        // Taken back by a race start before this page got to it - nothing
        // here to take over (the host says so in the console).
        if (notice.Cancelled)
        {
            return false;
        }

        if (_starting)
        {
            _lapLimitBeforePractice = notice.LapLimitBefore;
            _timeLimitBeforePractice = notice.TimeLimitBefore;
            _startTypeBeforePractice = notice.StartTypeBefore;
            return false;
        }

        if (IsDisposed || Disposing)
        {
            _race.Reset();
            _race.IsPracticeMode = false;
            _race.LapLimit = notice.LapLimitBefore;
            _race.TimeLimit = notice.TimeLimitBefore;
            _race.StartType = notice.StartTypeBefore;
            return false;
        }

        if (_active)
        {
            return false;
        }

        if (!_race.IsPracticeMode)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [practice] A car started practice at the gate, but the session had been taken over before this page got to it - left as it is.");
            return false;
        }

        // Kept to hand back at Stop - see the fields. The timing thread read
        // them before its Reset, as StartPractice does.
        _lapLimitBeforePractice = notice.LapLimitBefore;
        _timeLimitBeforePractice = notice.TimeLimitBefore;
        _startTypeBeforePractice = notice.StartTypeBefore;

        // From here on, StartPractice's own steps after the green.
        _lapHandler = (_, _, _) =>
        {
            if (InvokeRequired) { BeginInvoke(RefreshGrid); return; }
            RefreshGrid();
        };
        _race.LapProcessed += _lapHandler;

        _tracker.Reset();

        _active = true;

        // When it started, not when this page got to it - the same moment
        // StartPractice takes, however long the window was busy meanwhile.
        _startedAtUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds((double)Math.Max(0L, _race.DeliveringAgeMs ?? 0L));
        _refreshTimer.Start();
        _lblStatus.Text = "Practice running - lap the track freely";
        _lblStatus.ForeColor = AppTheme.Success;

        RefreshGrid();

        AutoStartConditionsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Stops a running practice as the app closes, so it is saved (when
    /// "Save automatically on stop" is ticked) and its crash journal is
    /// cleared - the same as pressing Stop. Closing without this skipped
    /// the save, and left the practice in the journal to be found at the
    /// next launch. Does nothing when no practice is running.
    /// </summary>
    /// <param name="personIsThere">
    /// True when a person closed the app (CloseReason.UserClosing), so a
    /// failed auto-save can be put to them in a dialog before the window
    /// goes. Off by default: when Windows is shutting down nobody may be
    /// there to answer it, and a dialog would hold the shutdown up.
    /// </param>
    public void StopForShutdown(bool personIsThere = false)
    {
        if (_active)
        {
            StopPractice("the app was closed", mayShowDialog: personIsThere);
        }
    }

    /// <summary>
    /// Asks whether practice may take over the shared session.
    ///
    /// Practice and Race Control share ONE RaceControlSession, and starting
    /// practice resets it - so pressing Start Practice here used to wipe
    /// whatever race was on Race Control, even one mid-way through, with no
    /// warning. A race that is staged, counting down or running is refused
    /// outright: there is no safe way to have both. A finished race has
    /// already been written to Race History at the checkered flag (see
    /// RaceControlControl.SaveCompletedSessionToHistory), so clearing it
    /// loses nothing saved - but it does take the result off the board, and
    /// somebody may still be reading it, so that is asked rather than done.
    /// </summary>
    private bool ConfirmRaceControlCanBeCleared()
    {
        // WHY (20 Oct 2026): a warm-up is not a RaceState - the session stays
        // Idle (or Finished) through it - so the switch below let Start Practice
        // straight through, and the Reset that follows cancelled the warm-up
        // and the Stage queued behind it with no warning. Refused here the
        // same way TryAutoStartPractice refuses it, and ahead of the switch so
        // a warm-up open over a finished race is caught too. The advice is
        // Reset, not skip: skipping a warm-up stages the race straight away,
        // which this page refuses as well.
        if (_race.IsWarmingUp)
        {
            AppDialog.Show(this,
                "A warm-up is open on Race Control, and the race is staged automatically when it ends.\r\n\r\n"
                + "Practice uses the same timing session, so starting it now would cancel the warm-up and that race start. "
                + "Press Reset on Race Control to cancel the warm-up first, then start practice.",
                "Warm-up in progress", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        switch (_race.State)
        {
            case RaceState.Staging:
            case RaceState.Countdown:
            case RaceState.Racing:
                AppDialog.Show(this,
                    $"A race is {(_race.State == RaceState.Racing ? "running" : "being started")} on Race Control.\r\n\r\n"
                    + "Practice uses the same timing session, so starting it now would wipe that race. "
                    + "Finish the race (checkered flag) or reset it on Race Control first, then start practice.",
                    "Race in progress", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;

            case RaceState.Finished when _race.GetOrderedStandings().Count > 0:
                var answer = AppDialog.Show(this,
                    "The last race's finishing order is still on the Race Control board.\r\n\r\n"
                    + "Its result was saved to Race History when the checkered flag fell (a demo race is only "
                    + "saved if \"Save demo races to Race History\" is ticked). Starting practice clears the board.\r\n\r\n"
                    + "Start practice now?",
                    "Clear the finished race?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                return answer == DialogResult.Yes;

            default:
                return true;
        }
    }

    /// <summary>
    /// Wipes the practice figures without ending the practice.
    ///
    /// Reset() drops the session to Idle and forgets the start reference,
    /// and ProcessLapEvent ignores every pass outside the Racing state - so
    /// a bare Reset here left practice looking "running" on this page while
    /// no lap was counted until Stop and Start were pressed. The green is
    /// thrown again straight away (no countdown audio: this is a clear, not
    /// a start) so the next crossing counts as it always would.
    ///
    /// When no practice is running this must not touch the shared session
    /// at all - whatever is on it then belongs to Race Control.
    /// </summary>
    private void ClearStats()
    {
        if (!_active)
        {
            _grid.Rows.Clear();
            return;
        }

        // The figures as they were, for Undo - the whole session, since
        // Reset clears all of it.
        var snapshot = _race.CreateSnapshot();
        var startedAt = _startedAtUtc;
        var race = _race;

        _race.Reset();

        // Reset() leaves IsPracticeMode and the lap/time targets alone, so
        // the practice guards in the session still apply after the re-green.
        _race.GreenFlag(_race.EstimatedDeviceTimeNowMs);

        // Which session the clear left behind: the undo is refused once the
        // practice has been stopped, restarted or cleared again - and once
        // any lap has been counted since, which putting the old figures
        // back would throw away.
        var generationAfterClear = _race.SessionGeneration;

        _undo?.Capture("Clear practice figures", () =>
        {
            if (!race.IsPracticeMode || race.SessionGeneration != generationAfterClear || IsDisposed || !_active)
            {
                throw new InvalidOperationException(
                    "the practice has been stopped, restarted or cleared again since. Nothing was changed.");
            }

            if (race.GetOrderedStandings().Count > 0)
            {
                throw new InvalidOperationException(
                    "laps have been counted since the clear, and putting the old figures back would lose them. Nothing was changed.");
            }

            race.RestoreSnapshot(snapshot);
            _tracker.Reset();
            _startedAtUtc = startedAt;
            RefreshGrid();
        });

        _tracker.Reset();
        _startedAtUtc = DateTime.UtcNow;
        RefreshGrid();
    }

    /// <param name="fromGatePass">
    /// True when a car crossing the gate started this. The green is then
    /// dropped at once rather than after the announcer falls silent: the
    /// host passes that same crossing to the session the moment this
    /// returns, and waiting for audio (a quarter of a second at the least)
    /// meant it arrived while nothing was running - so the driver who
    /// started the practice lost their first lap. Nothing is said at a
    /// practice start, so there is nothing to wait for.
    /// </param>
    /// <param name="triggeringDeviceTimeMs">
    /// For a gate-pass start, the decoder time of the crossing that
    /// triggered it - see TryAutoStartPractice.
    /// </param>
    private void StartPractice(bool fromGatePass = false, long? triggeringDeviceTimeMs = null)
    {
        if (_active)
        {
            return;
        }

        // WHY (24 Oct 2026, timing layer, P7): threaded, the timing thread
        // may start practice at the gate on its own (AutoStartTargets). From
        // here on it must not - this start is taking the session - so the
        // host takes its plan back first. Then the window catches up with the
        // timing thread: a practice a car started a moment ago is taken over
        // now, quietly, and this Start has nothing left to do - rather than
        // finding a "race running" and refusing it with a dialog. Both are
        // no-ops inline.
        _holdAutoStart = true;

        try
        {
            AutoStartConditionsChanged?.Invoke();
            _race.CatchUpWithTiming();

            if (_active || !ConfirmRaceControlCanBeCleared())
            {
                return;
            }

            // Kept as a guard: a gate practice can no longer start from here
            // on, but one that did is handed to this page in the middle of
            // the start below, and only its before-values are kept (see
            // AdoptAutoStartedPractice).
            _starting = true;

            try
            {
                StartPracticeTakingTheSession(fromGatePass, triggeringDeviceTimeMs);
            }
            finally
            {
                _starting = false;
            }
        }
        finally
        {
            _holdAutoStart = false;
            AutoStartConditionsChanged?.Invoke();
        }
    }

    /// <summary>The rest of StartPractice, once the session may be cleared - word for word as it was.</summary>
    private void StartPracticeTakingTheSession(bool fromGatePass, long? triggeringDeviceTimeMs)
    {
        // Kept to hand back at Stop - see the fields.
        _lapLimitBeforePractice = _race.LapLimit;
        _timeLimitBeforePractice = _race.TimeLimit;
        _startTypeBeforePractice = _race.StartType;

        _race.Reset();

        // Practice deliberately reuses the Racing state so the existing lap
        // pipeline (validity checks, lap timing) applies unchanged - but it
        // never saves to history and never touches track records, which is
        // what actually distinguishes it from a real race.
        // The announcer reads this from the session itself (see
        // AnnouncementService.PracticeModeProvider), so there is nothing
        // else to keep in step here.
        _race.IsPracticeMode = true;

        // For the clock's progress ring only - guarded in RaceControlSession
        // so these can never auto-finish the session the way they would for
        // a real race.
        _race.LapLimit = (int)_numLapTarget.Value == 0 ? null : (int)_numLapTarget.Value;
        _race.TimeLimit = (int)_numTimeTarget.Value == 0 ? null : TimeSpan.FromMinutes((double)_numTimeTarget.Value);

        // Everyone goes when they like. The session still held the race
        // format's start type, so practice inherited a staggered or
        // hand-released start - cars held at the line or timed from a
        // release nobody called - or a qualifying start's per-driver clocks.
        _race.StartType = RaceStartType.Standard;

        if (fromGatePass && triggeringDeviceTimeMs is { } crossingMs)
        {
            // One millisecond before the crossing that started it, so that
            // crossing - handed to the session straight after this - is the
            // driver's start lap rather than a pass from before the start.
            _race.GreenFlagBefore(crossingMs);
        }
        else if (fromGatePass)
        {
            _race.GreenFlag(_race.EstimatedDeviceTimeNowMs);
        }
        else
        {
            // Stamped with the decoder clock as it reads when the flag
            // actually drops. Passing LastKnownDeviceTimeMs was the time of
            // whatever car last crossed - possibly minutes ago - read before
            // the audio wait, so every driver's first lap and total carried
            // that gap (the session only corrects a gap over five seconds).
            _ = _race.GreenFlagAfterAudioAsync();
        }

        _lapHandler = (_, _, _) =>
        {
            if (InvokeRequired) { BeginInvoke(RefreshGrid); return; }
            RefreshGrid();
        };
        _race.LapProcessed += _lapHandler;

        _tracker.Reset();

        _active = true;
        _startedAtUtc = DateTime.UtcNow;
        _refreshTimer.Start();
        _lblStatus.Text = "Practice running - lap the track freely";
        _lblStatus.ForeColor = AppTheme.Success;

        RefreshGrid();
    }

    /// <summary>
    /// Stops the practice session - the manual Stop button, and the
    /// automatic length check below, both go through here so save/teardown
    /// behaves identically either way. <paramref name="reason"/> is folded
    /// into the final status message rather than set separately, since this
    /// method always overwrites that text at the end - setting it beforehand
    /// would just get clobbered.
    /// </summary>
    /// <param name="mayShowDialog">
    /// False only when the app is closing and cannot tell whether a person
    /// is there to answer (see StopForShutdown) - a failed auto-save then
    /// says so in the status line alone, rather than holding up a Windows
    /// shutdown with a dialog nobody sees.
    /// </param>
    private void StopPractice(string? reason = null, bool mayShowDialog = true)
    {
        if (!_active)
        {
            return;
        }

        // Save BEFORE unsubscribing and resetting - Reset clears the
        // standings this reads from, so afterwards there'd be nothing left
        // to save.
        var autoSaved = false;
        var autoSaveFailed = false;
        var savedDrivers = 0;
        if (_settings.AutoSavePractice)
        {
            savedDrivers = SaveSession(silent: true);
            autoSaved = savedDrivers > 0;
            autoSaveFailed = savedDrivers == SaveFailed;
        }

        if (_lapHandler is not null)
        {
            _race.LapProcessed -= _lapHandler;
            _lapHandler = null;
        }

        _refreshTimer.Stop();
        _race.Reset();
        _race.IsPracticeMode = false;

        // The practice is over and, where wanted, saved - so the crash
        // journal Race Control keeps of the running session has nothing
        // left to protect. Left behind, it was offered back at the next
        // launch as an unfinished race. Reset has already put the session
        // at Idle, which the journal never writes, so nothing re-creates
        // the file after this. (Its path is fixed; a fresh instance clears
        // the same file Race Control's writes to.)
        //
        // Not while a finished race is still waiting to get into the race
        // history: the journal is then that race's only copy on disk (Race
        // Control's own clears all wait for it). Stopping practice used to
        // delete it regardless, and a crash before the next Stage lost the
        // race for good.
        if (HasUnfiledRaces?.Invoke() != true)
        {
            new RaceJournal().Clear();
        }
        else
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [practice] Crash journal kept - an earlier race still has to get into the race history.");
        }

        // Handed back rather than cleared - Reset() doesn't touch these, a
        // practice target left here would leak into the next race, and
        // clearing them instead left that race with no limits at all.
        // Race Control writes its format again at the next Stage,
        // Countdown or Green in any case.
        _race.LapLimit = _lapLimitBeforePractice;
        _race.TimeLimit = _timeLimitBeforePractice;
        _race.StartType = _startTypeBeforePractice;

        _active = false;

        var suffix = reason is null ? string.Empty : $" ({reason})";

        // Keep the auto-save confirmation visible rather than immediately
        // replacing it with a bare "Stopped" - otherwise there's no sign
        // the save happened at all.
        if (autoSaved)
        {
            _lblStatus.Text = $"Stopped{suffix} - auto-saved {savedDrivers} driver{(savedDrivers == 1 ? "" : "s")} to Race History.";
            _lblStatus.ForeColor = AppTheme.Success;
        }
        else if (autoSaveFailed)
        {
            // Not a normal stop: the laps are held in memory only and go
            // when the app closes (see SaveSession), so it is said in red
            // and kept on screen, not passed off as a plain "Stopped".
            _lblStatus.Text = $"Stopped{suffix} - NOT saved: the race history file couldn't be written.";
            _lblStatus.ForeColor = AppTheme.Danger;
        }
        else
        {
            _lblStatus.Text = $"Stopped{suffix}";
            _lblStatus.ForeColor = AppTheme.TextMuted;
        }

        PracticeStopped?.Invoke();

        // Threaded, the gate-pass start may be armed again (24 Oct 2026,
        // timing layer, P7).
        AutoStartConditionsChanged?.Invoke();

        // Last, with the session already handed back and the refresh timer
        // stopped, so nothing runs underneath the dialog while it is up -
        // the length target is checked from that timer, and would otherwise
        // be free to come back in here.
        if (autoSaveFailed && mayShowDialog)
        {
            ShowNotSavedDialog();
        }
    }

    /// <summary>
    /// A badge source for a Big Screen opened without Race Control's live
    /// store: reads achievements.json from disk, then hands back that copy
    /// until BadgeReloadInterval has passed. The Big Screen asks on every
    /// half-second refresh, so reading the file each time would be wasteful,
    /// but a copy kept forever froze the counts for the rest of the night.
    /// Called only on the UI thread (the Big Screen's refresh timer), so the
    /// captured state needs no locking.
    /// </summary>
    private static Func<AchievementStore?> ThrottledAchievementLoader()
    {
        AchievementStore? badges = null;
        var readUtc = DateTime.MinValue;

        return () =>
        {
            if (badges is null || DateTime.UtcNow - readUtc >= BadgeReloadInterval)
            {
                badges = AchievementStore.Load();
                readUtc = DateTime.UtcNow;
            }

            return badges;
        };
    }

    /// <summary>
    /// Checks whether the configured practice length has been reached and,
    /// if so, stops the session the same way the Stop button would. This
    /// deliberately does NOT go through RaceControlSession's own lap/time-
    /// limit machinery (see the IsPracticeMode guards there) - that path
    /// calls Finish(), which sets a checkered flag and fires RaceCompleted,
    /// real race-over semantics a practice session has no business
    /// triggering.
    /// </summary>
    private void CheckPracticeLengthTarget()
    {
        if (_race.LapLimit is { } lapTarget)
        {
            // The most laps any REAL car has done. A ghost lapping ahead of
            // everybody used to end the practice on reaching the target by
            // itself. RaceLeader() alone is not enough for that: it skips
            // ghosts only while a real car has a standing, and falls back to
            // the leading ghost before then. A real driver only gets a
            // standing at their first crossing, while ghosts start lapping
            // at the green - so a driver who pressed Start and then walked
            // to the rostrum could find the practice already stopped by a
            // ghost's fifth lap. Ghosts never count towards the target here.
            var leaderLaps = _race.GetOrderedStandings()
                .Where(s => !GhostService.IsGhost(s.TransponderId))
                .Select(s => s.Laps)
                .DefaultIfEmpty(0)
                .Max();
            if (leaderLaps >= lapTarget)
            {
                StopPractice($"{lapTarget} lap target reached");
                return;
            }
        }

        if (_race.TimeLimit is { } timeTarget && _race.ElapsedRaceTime >= timeTarget)
        {
            StopPractice($"{timeTarget.TotalMinutes:0.0} min target reached");
        }
    }


    /// <summary>Rebuilds the grid's columns from the saved KPI selection.</summary>
    private void RebuildColumns()
    {
        _columns = KpiCatalog.Resolve(_settings.PracticeKpis, KpiCatalog.DefaultPractice);

        _grid.Columns.Clear();
        foreach (var kpi in _columns)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = kpi.Key,
                HeaderText = kpi.Label,
                FillWeight = kpi.Weight,
            });
        }

        // New columns: each kept as wide as its heading, at its own weight.
        Pieces.KeepHeadingsWhole(_grid);
    }

    private void RefreshGrid()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        var previouslySelected = _grid.CurrentRow?.Tag as string;

        // WHY (20 Oct 2026): Rows.Clear threw the scroll position away, so a
        // list longer than the card snapped back to the top on every refresh.
        // Kept here and put back once the rows are refilled.
        var scroll = _grid.FirstDisplayedScrollingRowIndex;
        var reselected = false;
        var window = (int)_numRollingWindow.Value;

        _grid.Rows.Clear();

        // Sorted by best lap rather than by position - in practice nobody
        // cares who's "winning", they care who's quick.
        var standings = _race.GetOrderedStandings()
            .Where(s => s.LapTimesMs.Count > 0)
            .OrderBy(s => s.FastestLapMs == 0 ? long.MaxValue : s.FastestLapMs)
            .ToList();

        // Session-wide quickest lap, for the gap-to-leader column - a ghost's
        // left out while a real car is out (Ghosts.FastestLapMs).
        var fastestOverall = Ghosts.FastestLapMs(standings);

        _tracker.Observe(standings, _race.SessionGeneration);

        var context = new KpiContext
        {
            Ordered = standings,
            Profiles = _profiles,
            Tracker = _tracker,
            RollingWindow = window,
            FastestOverall = fastestOverall,
            StreakThreshold = _race.StreakThreshold,
            SectorLengthsFeet = _race.SectorLengthsFeet,
        };

        for (var index = 0; index < standings.Count; index++)
        {
            var s = standings[index];

            var values = _columns.Select(k => k.Compute(s, index, context)).ToList();
            var rowIndex = _grid.Rows.Add(values.Select(v => (object)v.Text).ToArray());
            _grid.Rows[rowIndex].Tag = s.TransponderId;

            // Emphasis comes from the KPI definitions, so every surface
            // highlights the same things without repeating the rules.
            for (var col = 0; col < values.Count; col++)
            {
                var emphasis = values[col].Emphasis;
                if (emphasis == KpiEmphasis.None)
                {
                    continue;
                }

                _grid.Rows[rowIndex].Cells[col].Style.ForeColor = emphasis switch
                {
                    KpiEmphasis.Good => AppTheme.Success,
                    KpiEmphasis.Bad => AppTheme.Danger,
                    KpiEmphasis.Highlight => AppTheme.Accent,
                    _ => _grid.DefaultCellStyle.ForeColor,
                };
            }

            if (s.TransponderId == previouslySelected)
            {
                // WHY (20 Oct 2026): setting Selected alone left CurrentRow on
                // row 0 (where refilling the grid puts it), so the next refresh
                // read row 0's driver as the selection and the highlight jumped
                // to the top. The current cell is moved first, then selected.
                // Cells[0] always exists: RebuildColumns adds a visible column
                // for every KPI, and Rows.Add would have thrown without one.
                _grid.CurrentCell = _grid.Rows[rowIndex].Cells[0];
                _grid.Rows[rowIndex].Selected = true;
                reselected = true;
            }
        }

        // Nobody's row to put back (none picked, or that driver left the
        // board): the grid's own row-0 pick is cleared, so it is not taken for
        // a choice at the next refresh.
        if (!reselected)
        {
            _grid.CurrentCell = null;
            _grid.ClearSelection();
        }

        // After CurrentCell, which can scroll the grid on its own.
        //
        // Guarded (20 Oct 2026): this runs on every counted lap, and the
        // grid refuses a scroll it cannot show (a row it has no room for
        // while the page is hidden or being resized). Keeping the place in
        // the list is a nicety - it must never cost the lap its refresh.
        if (scroll >= 0 && scroll < _grid.Rows.Count)
        {
            try
            {
                _grid.FirstDisplayedScrollingRowIndex = scroll;
            }
            catch (InvalidOperationException)
            {
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        if (standings.Count == 0 && _active)
        {
            _lblStatus.Text = "Practice running - waiting for the first lap...";
        }
        else if (_active)
        {
            _lblStatus.Text = $"Practice running - {standings.Count} driver{(standings.Count == 1 ? "" : "s")} on track";
        }
    }

    /// <summary>Call when this page becomes visible - track layouts can change elsewhere while it's not active.</summary>
    public void RefreshAll()
    {
        var previous = _cmbTrack.SelectedItem as string;

        _repopulatingTracks = true;
        try
        {
            _cmbTrack.Items.Clear();
            _cmbTrack.Items.Add("(none)");
            foreach (var t in _trackLayouts.All)
            {
                _cmbTrack.Items.Add(t.Name);
            }

            // Falls back to the remembered layout when there's no in-session
            // selection yet - that's the first refresh after launch, which
            // used to land on "(none)" every time.
            var target = previous ?? _settings.PracticeTrackLayout;

            var idx = string.IsNullOrWhiteSpace(target) ? -1 : _cmbTrack.Items.IndexOf(target);
            _cmbTrack.SelectedIndex = idx >= 0 ? idx : 0;
        }
        finally
        {
            _repopulatingTracks = false;
        }

        // The longest layout may have come or gone: measured again.
        _trackWidth = null;
        SizeFields();
    }

    /// <summary>The battery packs - each driver's pack in the car is recorded on the practice when it is saved (BatteryPackStore.AutoTag). Set by MainForm.</summary>
    public BatteryPackStore? BatteryPacks { get; set; }

    /// <summary>
    /// The stores the home and garage badges read - set by MainForm. At a
    /// home track a saved practice is judged for the badges a driver can
    /// earn alone (AchievementEvaluator.EvaluatePractice); at a club it is not.
    /// </summary>
    public AchievementContext? AchievementStores { get; set; }

    /// <summary>
    /// Whether Race Control has a finished race still to get into the race
    /// history (RaceControlControl.HasUnfiledRaces) - while it does, Stop
    /// leaves the crash journal alone. Set by MainForm.
    /// </summary>
    public Func<bool>? HasUnfiledRaces { get; set; }

    /// <summary>A line for the console - the badges a practice earned.</summary>
    public event Action<string>? StatusMessage;

    /// <summary>Raised after a practice at home awarded badges - for Race Control's board and the Big Screen to read them afresh.</summary>
    public event Action? BadgesAwarded;

    /// <summary>
    /// The practice whose badges have been worked out - by when it started.
    /// Save Session part-way through and the save when it stops both file
    /// the whole board, and judging both as they stand would count every lap
    /// of the first twice towards the lap totals. So a later save takes back
    /// what the earlier judgement added (_judgedPracticeLaps) and judges the
    /// whole board again - skipping it instead, as this once did, left every
    /// lap run after the first save unjudged.
    /// </summary>
    private DateTime? _judgedPracticeStart;

    /// <summary>
    /// The laps the last judgement of _judgedPracticeStart's practice added
    /// to each driver's lap total, by transponder - see _judgedPracticeStart.
    /// </summary>
    private Dictionary<string, int> _judgedPracticeLaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The record this practice was last saved as. Save Session part-way
    /// through, then Stop's auto-save (or Save pressed twice), each filed the
    /// whole board as a NEW session, so every lap before the first save was in
    /// Race History twice - doubled in the Lap Times chart, the lap and
    /// session totals and anything else that adds history up. A later save of
    /// the same practice brings this record up to date instead.
    /// </summary>
    private SessionRecord? _savedPractice;

    /// <summary>
    /// Writes the current practice laps into race history, flagged as a
    /// Practice session. That flag is what keeps it out of track records
    /// and season points (both of which filter to real races) while still
    /// letting Personal Bests and the Lap Times chart use the data - which
    /// is the whole reason to save it rather than let it evaporate.
    /// </summary>
    /// <returns>
    /// How many drivers were saved; 0 when there was nothing to save; or
    /// SaveFailed when there was, but it could not be written.
    /// </returns>
    private int SaveSession(bool silent = false)
    {
        // Only a practice that is running here has anything of ours to save.
        // Practice and Race Control share one session, so when no practice
        // is running whatever is on it belongs to somebody else - a race
        // still on the board after the checkered flag (already saved to
        // Race History as a race), or a demo's made-up laps under real
        // drivers' transponders. Saving either as Practice put a second copy
        // of the race, or laps nobody drove, into the Personal Bests, the
        // Lap Times chart and the PB ghosts. ClearStats keeps off the shared
        // session for the same reason. The session's own flags are checked
        // as well as _active, so that a demo somehow holding the session
        // while this page still thinks practice is running is refused too.
        // Stop's auto-save runs before _active is cleared, so it still
        // passes this.
        if (!_active || !_race.IsPracticeMode || _race.IsDemoMode)
        {
            if (!silent)
            {
                AppDialog.Show(this,
                    "No practice is running, so there's nothing to save.\r\n\r\n"
                    + "Whatever is on the timing board now belongs to Race Control or a demo, not to practice. "
                    + "A practice is saved when you stop it if \"Save automatically on stop\" is ticked; "
                    + "otherwise press Save Session before you stop it.",
                    "Nothing to save", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        // Ghosts are what a practice session is usually run against, so
        // leaving them in a saved result would put a made-up lap time into
        // the personal bests and the pace charts as if somebody had driven
        // it.
        var board = _race.GetOrderedStandings();
        var standings = board
            .Where(s => s.LapTimesMs.Count > 0 && !GhostService.IsGhost(s.TransponderId))
            .ToList();
        if (standings.Count == 0)
        {
            // An auto-save with no laps is the normal case for a practice
            // that was started and stopped without anyone running - say
            // nothing rather than popping a dialog they didn't ask for.
            if (!silent)
            {
                AppDialog.Show(this, "No laps recorded yet - there's nothing to save.", "Nothing to save",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        var results = standings
            .OrderBy(s => s.FastestLapMs == 0 ? long.MaxValue : s.FastestLapMs)
            .Select((s, i) =>
            {
                var profile = _profiles.Get(s.TransponderId);
                return new SessionResultEntry
                {
                    Position = i + 1,
                    Transponder = s.TransponderId,
                    DriverName = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName : s.TransponderId,
                    CarModel = _profiles.CarModelFor(s.TransponderId),
                    Laps = s.Laps,
                    FastLapMs = s.FastestLapMs,
                    ElapsedMs = s.ElapsedMs,
                    Status = "practice",
                    LapTimesMs = s.LapTimesMs.ToList(),
                    SectorTimesMs = SessionResultEntry.SectorsToSave(s),
                };
            }).ToList();

        var record = new SessionRecord
        {
            SessionType = RaceSessionType.Practice,
            TrackLayoutName = SelectedTrackName ?? string.Empty,
            Label = "Practice",
            StartedAtUtc = _startedAtUtc ?? DateTime.UtcNow,
            EndedAtUtc = DateTime.UtcNow,
            Results = results,
        };

        record.SectorCut = TrackLayout.CutOfResults(
            _trackLayouts.All.FirstOrDefault(t => !string.IsNullOrEmpty(record.TrackLayoutName) && t.Name == record.TrackLayoutName),
            results);

        // A failed write is no longer picked up by a later save (see
        // SessionHistoryStore.Add), and practice has no crash journal, so
        // saying "saved" here would hide a session that is gone at the next
        // restart.
        //
        // A silent save (Stop's auto-save) returns SaveFailed rather than 0,
        // so Stop can tell "couldn't be written" from "nothing to save". The
        // two used to look the same there, and an auto-save that failed was
        // reported as a plain "Stopped" - the practice then vanished at the
        // next restart with nobody told.
        //
        // Already saved once (see _savedPractice): that record is brought up
        // to date in place, as long as it is still in Race History - deleted
        // there meanwhile, this is filed afresh - and nobody has corrected
        // its result since, which overwriting would quietly throw away.
        var earlier = _savedPractice is { } prior
            && prior.StartedAtUtc == record.StartedAtUtc
            && prior.Edits is not { Count: > 0 }
            && ReferenceEquals(_history.Get(prior.Id), prior)
                ? prior
                : null;

        bool saved;

        if (earlier is not null)
        {
            earlier.TrackLayoutName = record.TrackLayoutName;
            earlier.EndedAtUtc = record.EndedAtUtc;
            earlier.Results = record.Results;
            earlier.SectorCut = record.SectorCut;
            record = earlier;

            // One whose first write failed is still Add's to file (see
            // SessionHistoryStore._unsaved), which takes it as a retry.
            saved = _history.IsUnsaved(record) ? _history.Add(record) : _history.SaveEdited(record);
        }
        else
        {
            saved = _history.Add(record);
        }

        // Even when the write failed: Add keeps a failed record listed, and
        // the next save of this practice must retry that one rather than
        // list a second.
        _savedPractice = record;

        if (!saved)
        {
            if (!silent)
            {
                ShowNotSavedDialog();
            }

            return SaveFailed;
        }

        AfterPracticeSaved(record, board);

        if (silent)
        {
            return results.Count;
        }

        AppDialog.Show(this,
            $"Saved {results.Count} driver{(results.Count == 1 ? "" : "s")} to Race History as a Practice session.",
            "Practice saved", MessageBoxButtons.OK, MessageBoxIcon.Information);

        return results.Count;
    }

    /// <summary>
    /// Once a practice is safely in Race History: each driver's pack in the
    /// car recorded on it, and - at a home track - its badges. Neither may
    /// stop the save it follows, so neither is allowed to throw out of here.
    /// </summary>
    private void AfterPracticeSaved(SessionRecord record, IReadOnlyList<RacerStanding> board)
    {
        try
        {
            BatteryPacks?.AutoTag(record, _profiles);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [packs] Couldn't record the packs on this practice: {ex.Message}");
        }

        if (_settings.RacingAt != RacingSetting.Home)
        {
            return;
        }

        try
        {
            var store = AchievementStore.Load();

            if (store.LoadedUnreadable)
            {
                StatusMessage?.Invoke("[achievements] achievements.json could not be read just now, so achievements were not worked out for this practice.");
                return;
            }

            // Judged by best lap: in practice nobody finishes ahead of
            // anybody, a ghost included.
            var beaten = AchievementEvaluator.GhostsBeatenBy(
                board.Select(s => (s.TransponderId, s.Laps, s.FastestLapMs)).ToList(),
                AchievementStores?.GhostCars?.Invoke() ?? Array.Empty<GhostCar>(),
                byBestLap: true);

            var context = (AchievementStores ?? new AchievementContext()) with
            {
                RacingAt = _settings.RacingAt,
                Profiles = _profiles,
                GhostsBeaten = beaten,
            };

            // Judged already, part-way through: the whole board is judged
            // again so the laps since count too, so first take back what that
            // judgement added to each lap total, or those laps count twice.
            // Badges already held are not awarded (or said) again.
            if (_judgedPracticeStart == record.StartedAtUtc)
            {
                foreach (var (transponder, laps) in _judgedPracticeLaps)
                {
                    var driver = store.For(transponder);
                    driver.TotalLaps = Math.Max(0, driver.TotalLaps - laps);
                }
            }

            var unlocks = AchievementEvaluator.EvaluatePractice(record, _history.All, store, context, out var lapsSaved);

            // What EvaluatePractice added to the totals - and only when it
            // saved them. When its save failed the file still holds what the
            // last judgement left (or nothing of this practice), so what is
            // taken back next time stays as it was: taking back laps that
            // were never saved shrank the career lap total for good.
            if (lapsSaved is not null)
            {
                _judgedPracticeStart = record.StartedAtUtc;
                _judgedPracticeLaps = lapsSaved;
            }

            if (unlocks.Count > 0)
            {
                BadgesAwarded?.Invoke();
            }

            foreach (var unlock in unlocks)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [achievements] {unlock.DriverName} unlocked \"{unlock.Badge.Name}\" in practice"
                    + (string.IsNullOrWhiteSpace(unlock.Detail) ? "." : $" ({unlock.Detail})."));
            }

            // Said, as after a race - the first few, rarest first.
            foreach (var unlock in unlocks.Take(3))
            {
                _announcer?.NotifyAchievement(unlock.Transponder, unlock.Badge.Name);
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [achievements] Couldn't work out achievements for this practice: {ex.Message}");
        }
    }

    /// <summary>
    /// What SaveSession returns when the practice had laps to save but the
    /// race history file could not be written - see SaveSession.
    /// </summary>
    private const int SaveFailed = -1;

    /// <summary>
    /// Says a practice could not be written to the race history file. One
    /// wording for the Save button and for Stop's auto-save, so the two
    /// cannot drift apart.
    /// </summary>
    private void ShowNotSavedDialog()
    {
        AppDialog.Show(this,
            "The practice session couldn't be written to the race history file - something may have it open, "
            + "or the disk may be full. It's listed in Race History until HeatCaller closes, but won't be kept.",
            "Practice not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_lapHandler is not null)
            {
                _race.LapProcessed -= _lapHandler;
            }
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _clockRefreshTimer.Stop();
            _clockRefreshTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
