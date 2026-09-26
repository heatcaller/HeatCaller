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
    private readonly RaceControlSession _race;
    private readonly ProfileStore _profiles;
    private readonly SessionHistoryStore _history;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly AppSettings _settings;
    private readonly CheckBox _chkAutoSave = new();

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

    private readonly KpiTracker _tracker = new();
    private List<KpiDefinition> _columns = new();

    // Optional - only needed for the Big Screen button. Practice is
    // constructible without them (e.g. in a context with no announcer),
    // in which case the button just stays disabled.
    private readonly AnnouncementService? _announcer;
    private readonly DayScheduleStore? _daySchedule;
    private readonly SponsorStore? _sponsors;
    private readonly UndoService? _undo;

    private readonly RaceClockPanel _clock;
    private readonly Button _btnBigScreen = new();
    private readonly System.Windows.Forms.Timer _clockRefreshTimer = new() { Interval = 250 };

    private readonly NumericUpDown _numLapTarget = new();
    private readonly NumericUpDown _numTimeTarget = new();
    private readonly CheckBox _chkAutoStartOnGate = new();
    private readonly GhostStore _ghostStore;
    private readonly CheckBox _chkGhosts = new();
    private readonly CheckedListBox _clbGhosts = new();
    private readonly Label _lblGhostHint = new();

    // True while RefreshGhosts is loading the list from the store, so the
    // ItemCheck / CheckedChanged handlers don't treat those programmatic
    // check changes as user edits (and re-save the store mid-rebuild).
    private bool _populatingGhosts;

    private readonly Label _lblIntro = new();

    public PracticeModeControl(RaceControlSession race, ProfileStore profiles, SessionHistoryStore history, TrackLayoutStore trackLayouts, AppSettings settings,
        GhostStore ghostStore,
        AnnouncementService? announcer = null, DayScheduleStore? daySchedule = null, SponsorStore? sponsors = null,
        UndoService? undo = null)
    {
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
        // Was 1120 - driven by the grid's own initial Size, not by anything
        // that actually needs to be visible without scrolling. The grid
        // itself stretches/compresses fine at any width (Fill-anchored,
        // AutoSizeColumnsMode.Fill), so the real floor is whatever the
        // clock and Big Screen button need at their new, closer-in position.
        MinimumSize = new Size(980, 590);
        Font = AppTheme.BodyFont;

        BuildLayout();

        // The grid's columns, from the saved selection. Nothing else built
        // them - only the Choose Columns dialog did - so the grid had none,
        // and the first lap of every practice threw from Rows.Add ("no row
        // can be added to a DataGridView that has no columns") on each
        // refresh: the lap handler and the half-second timer alike.
        RebuildColumns();

        RefreshGhosts();

        // The intro text's wrap width has to track the clock's actual
        // current position, not a fixed guess - the clock is Right-anchored
        // and genuinely moves as the window resizes, and a static wrap
        // width either overlaps it (too wide) or wraps needlessly early on
        // a big window (too narrow).
        Resize += (_, _) => UpdateIntroWrapWidth();
        UpdateIntroWrapWidth();

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

    /// <summary>
    /// Re-wraps the intro text to stop just short of the clock's LEFT edge -
    /// read live from <c>_clock.Left</c> rather than recomputed by hand, so
    /// this can't drift out of step with whatever the anchor math actually
    /// places the clock at. Floors at a sane minimum so a very narrow window
    /// still gets a readable multi-line block rather than a near-zero-width
    /// column of text.
    /// </summary>
    private void UpdateIntroWrapWidth()
    {
        var available = _clock.Left - _lblIntro.Left - 16;
        _lblIntro.MaximumSize = new Size(Math.Max(220, available), 0);
    }


    /// <summary>Raised when the user wants the full Ghost Cars page.</summary>
    public event Action? GhostsPageRequested;

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
            _lblGhostHint.Text = "No ghosts set up yet - press Manage to create one.";
            return;
        }

        var on = _ghostStore.Current.Ghosts.Count(g => g.Enabled);

        _lblGhostHint.Text = _chkGhosts.Checked
            ? $"{Plural.Of(on, "ghost")} will join this practice session."
            : "Ghosts are switched off for now.";
    }

    private void BuildLayout()
    {
        // Rebuilt against UiLayout. This page had grown three different left
        // edges, four control heights sharing one band, and a full-width
        // intro sitting over a cramped grid - each addition reasonable on
        // its own, incoherent together.
        //
        // Structure now: what practice IS, then the controls that start and
        // stop it, then how it's configured, then the results.
        var y = UiLayout.PageTop;
        const int clockWidth = 260;

        // Derived from the page's own minimum width rather than a guessed
        // 1108. Hardcoding a wider number put the clock and the grid past
        // the right edge of the content panel, which is narrower than the
        // window once the sidebar takes its share.
        const int pageWidth = 980;
        var contentRight = pageWidth - UiLayout.PageRight;
        var textWidth = contentRight - clockWidth - 24;

        // --- What this page does -----------------------------------------

        _lblIntro.Text =
            "Open lapping with no race structure - every transponder crossing the line is tracked, without touching "
            + "track records or standings. Saved to Race History flagged as Practice, so it feeds Personal Bests and "
            + "the Lap Times chart but never counts toward records or season points.";
        _lblIntro.Location = new Point(UiLayout.PageLeft, y);
        _lblIntro.AutoSize = true;
        _lblIntro.MaximumSize = new Size(textWidth, 0);
        _lblIntro.ForeColor = AppTheme.TextMuted;
        _lblIntro.Font = AppTheme.SmallFont;

        // Measured, not advanced by a literal 48. This is a wrapping
        // paragraph: how tall it is depends on how many lines it takes,
        // which depends on the text size. At a larger one it ran three
        // lines into four and the buttons below started underneath it.
        y = UiLayout.Below(_lblIntro, UiLayout.RowGap);

        // --- Session controls ---------------------------------------------

        var btnStart = new Button { Text = "Start Practice", Width = 130, Height = UiLayout.ButtonHeight };
        btnStart.Click += (_, _) => StartPractice();

        var btnStop = new Button { Text = "Stop", Width = 90, Height = UiLayout.ButtonHeight };
        btnStop.Click += (_, _) => StopPractice();

        var btnClear = new Button { Text = "Clear Stats", Width = 110, Height = UiLayout.ButtonHeight };
        btnClear.Click += (_, _) => ClearStats();

        var btnSave = new Button { Text = "Save Session", Width = 120, Height = UiLayout.ButtonHeight };
        btnSave.Click += (_, _) => SaveSession(silent: false);

        y = UiLayout.Row(y, btnStart, btnStop, btnClear, btnSave);

        // Explicit fonts on everything this layout MEASURES.
        //
        // UiLayout.After reads a control's PreferredSize, and a control
        // that has not been parented yet is still carrying the WinForms
        // default font - so it reports a width for a size it will never be
        // drawn at, and the field after it lands short. The page sets its
        // own Font, but a child only inherits that once it is added.
        _chkAutoSave.Font = AppTheme.BodyFont;
        _chkAutoStartOnGate.Font = AppTheme.BodyFont;
        _chkGhosts.Font = AppTheme.BodyFont;

        _chkAutoSave.Text = "Save automatically on stop";
        _chkAutoSave.Location = new Point(UiLayout.PageLeft, UiLayout.LabelY(y));
        _chkAutoSave.AutoSize = true;
        _chkAutoSave.Checked = _settings.AutoSavePractice;
        _chkAutoSave.CheckedChanged += (_, _) => { _settings.AutoSavePractice = _chkAutoSave.Checked; _settings.Save(); };

        _chkAutoStartOnGate.Text = "Start automatically when a car crosses the gate";
        _chkAutoStartOnGate.Location = new Point(UiLayout.After(_chkAutoSave) + UiLayout.SectionGap, UiLayout.LabelY(y));
        _chkAutoStartOnGate.AutoSize = true;
        _chkAutoStartOnGate.Checked = _settings.PracticeAutoStartOnGatePass;
        _chkAutoStartOnGate.CheckedChanged += (_, _) =>
        {
            _settings.PracticeAutoStartOnGatePass = _chkAutoStartOnGate.Checked;
            _settings.Save();
        };
        y = UiLayout.NextRow(y);

        // --- Ghosts ---------------------------------------------------------
        // On the practice page because this is what ghosts are FOR: racing
        // something when nobody else is on track. Making someone configure
        // them on another page first, then come back, misses the point.

        _chkGhosts.Text = "Race ghost cars:";
        _chkGhosts.Location = new Point(UiLayout.PageLeft, UiLayout.LabelY(y));
        _chkGhosts.AutoSize = true;
        _chkGhosts.Checked = _ghostStore.Current.Enabled;
        _chkGhosts.CheckedChanged += (_, _) =>
        {
            if (_populatingGhosts) return;
            _ghostStore.Current.Enabled = _chkGhosts.Checked;
            _ghostStore.Save();
            UpdateGhostState();
        };

        // After its own caption. The 150px gutter fitted "Race ghost cars:"
        // at the normal text size and nothing above it.
        _clbGhosts.Location = new Point(UiLayout.After(_chkGhosts) + UiLayout.RowGap, y - 2);
        _clbGhosts.Size = new Size(280, 74);
        _clbGhosts.CheckOnClick = true;
        _clbGhosts.BorderStyle = BorderStyle.FixedSingle;
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

        var btnManageGhosts = new Button { Text = "Manage...", Width = 96, Height = UiLayout.ButtonHeight };
        btnManageGhosts.Click += (_, _) => GhostsPageRequested?.Invoke();

        _lblGhostHint.Location = new Point(UiLayout.After(_clbGhosts) + UiLayout.RowGap, y + 2);
        _lblGhostHint.AutoSize = true;
        _lblGhostHint.MaximumSize = new Size(300, 0);
        _lblGhostHint.ForeColor = AppTheme.TextMuted;
        _lblGhostHint.Font = AppTheme.SmallFont;

        // Under the hint, not at a fixed 44px below the row - the hint
        // wraps, and a fourth line used to land on top of this button.
        btnManageGhosts.Location = new Point(_lblGhostHint.Left, UiLayout.Below(_lblGhostHint, UiLayout.RowGap));

        Controls.Add(_chkGhosts);
        Controls.Add(_clbGhosts);
        Controls.Add(btnManageGhosts);
        Controls.Add(_lblGhostHint);

        // The block is as tall as whichever of its three columns is
        // tallest. It used to declare itself 82px and was really 80 - two
        // pixels of clearance, and negative as soon as the button grew.
        y = Math.Max(
            UiLayout.Below(_clbGhosts, UiLayout.RowGap),
            UiLayout.Below(btnManageGhosts, UiLayout.RowGap));

        // --- Setup ---------------------------------------------------------

        var lblSetup = new Label { Text = "SETUP" };
        y = UiLayout.SectionHeading(UiLayout.SectionAfter(y - UiLayout.RowGap), lblSetup);

        var lblTrack = new Label { Text = "Track:", AutoSize = false, Width = UiLayout.LabelWidth };
        _cmbTrack.Width = UiLayout.FieldWidth;
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

        var btnColumns = new Button { Text = "Choose Columns...", Width = 150, Height = UiLayout.ButtonHeight };
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

        y = UiLayout.LabelledRow(y, lblTrack, _cmbTrack, btnColumns);

        var lblLength = new Label { Text = "Practice length:", AutoSize = false, Width = UiLayout.LabelWidth };

        _numLapTarget.Width = UiLayout.NumericWidth;
        _numLapTarget.Minimum = 0;
        _numLapTarget.Maximum = 9999;
        _numLapTarget.Value = Math.Clamp(_settings.PracticeLapLimit, 0, 9999);
        _numLapTarget.ValueChanged += (_, _) => { _settings.PracticeLapLimit = (int)_numLapTarget.Value; _settings.Save(); };

        // AutoSize, so the row after it is measured against the word
        // rather than against a 40px box the word outgrows.
        var lblLaps = new Label { Text = "laps", AutoSize = true };

        _numTimeTarget.Width = UiLayout.NumericWidth;
        _numTimeTarget.Minimum = 0;
        _numTimeTarget.Maximum = 180;
        _numTimeTarget.Value = Math.Clamp(_settings.PracticeTimeLimitMinutes, 0, 180);
        _numTimeTarget.ValueChanged += (_, _) => { _settings.PracticeTimeLimitMinutes = (int)_numTimeTarget.Value; _settings.Save(); };

        var lblMins = new Label
        {
            Text = "minutes    (0 = no limit; stops automatically when either is reached)",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // Each suffix is placed AFTER the spinner it belongs to, rather
        // than at FieldLeft + 72. NumericWidth is 74, so that literal put
        // the word "laps" two pixels inside its own spinner - already
        // overlapping at the normal text size, and further in at any
        // larger one. Three rows on this page had the same stale 72.
        lblLength.Location = new Point(UiLayout.PageLeft, UiLayout.LabelY(y));
        _numLapTarget.Location = new Point(UiLayout.FieldLeft, y);
        lblLaps.Font = AppTheme.BodyFont;
        lblLaps.Location = new Point(UiLayout.After(_numLapTarget), UiLayout.LabelY(y));
        _numTimeTarget.Location = new Point(UiLayout.After(lblLaps) + UiLayout.RowGap, y);
        lblMins.Font = AppTheme.BodyFont;
        lblMins.Location = new Point(UiLayout.After(_numTimeTarget), UiLayout.LabelY(y));
        y = UiLayout.NextRow(y);

        var lblRolling = new Label { Text = "Rolling average over:", AutoSize = false, Width = UiLayout.LabelWidth };
        _numRollingWindow.Width = UiLayout.NumericWidth;
        _numRollingWindow.Minimum = 2;
        _numRollingWindow.Maximum = 50;
        // Not persisted - matches the original behaviour rather than
        // inventing a setting that nothing else reads.
        _numRollingWindow.Value = 5;
        _numRollingWindow.ValueChanged += (_, _) => RefreshGrid();

        var lblRollingSuffix = new Label
        {
            Text = "laps    (Streak = clean laps now; Best Streak = longest this session; Streak Pace = average lap in the current streak)",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        lblRolling.Location = new Point(UiLayout.PageLeft, UiLayout.LabelY(y));
        _numRollingWindow.Location = new Point(UiLayout.FieldLeft, y);
        lblRollingSuffix.Location = new Point(UiLayout.After(_numRollingWindow), UiLayout.LabelY(y));

        // Capped against where it ACTUALLY starts, not against the page's
        // text width. It was allowed "textWidth - FieldLeft" while sitting
        // a spinner's width further right than FieldLeft, so it overran the
        // clock column by exactly that much - and the repair pass, finding
        // a label lying across the Open Big Screen button, shoved the
        // button left to separate them. That is the misalignment: the
        // button and the clock are the same width and were given the same
        // x, and something moved one of them afterwards.
        lblRollingSuffix.MaximumSize = new Size(
            Math.Max(160, textWidth - lblRollingSuffix.Left), 0);
        y = UiLayout.NextRow(y);

        // --- Clock and big screen, pinned top-right ------------------------

        _clock.Location = new Point(contentRight - clockWidth, UiLayout.PageTop);
        _clock.Size = new Size(clockWidth, 206);
        _clock.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        _btnBigScreen.Text = "Open Big Screen";
        _btnBigScreen.Location = new Point(contentRight - clockWidth, UiLayout.PageTop + 214);
        _btnBigScreen.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);
        _btnBigScreen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnBigScreen.Enabled = _announcer is not null;
        _btnBigScreen.Click += (_, _) =>
        {
            if (_announcer is not null)
            {
                new BigScreenForm(_race, _announcer, _profiles, _daySchedule, _sponsors, _settings).Show();
            }
        };

        // --- Results --------------------------------------------------------

        var resultsY = UiLayout.SectionAfter(Math.Max(y - UiLayout.RowGap, UiLayout.PageTop + 258));

        var lblResults = new Label { Text = "THIS SESSION" };
        y = UiLayout.SectionHeading(resultsY, lblResults);

        // Measured from the heading it sits beside, not parked at x=140.
        // The heading is drawn in the bold face at whatever the text-size
        // setting is, and at the larger sizes "THIS SESSION" is wider than
        // 140 - so the status line started inside it.
        var statusLeft = UiLayout.After(lblResults, AppTheme.BoldFont) + UiLayout.RowGap;

        _lblStatus.Location = new Point(statusLeft, resultsY + 2);
        _lblStatus.AutoSize = true;
        _lblStatus.MaximumSize = new Size(Math.Max(120, contentRight - statusLeft), 0);
        _lblStatus.ForeColor = AppTheme.TextMuted;

        _grid.Location = new Point(UiLayout.PageLeft, y);
        _grid.Size = new Size(contentRight - UiLayout.PageLeft, 240);
        // Anchored so a wider window grows the grid rather than leaving a
        // gap - the fixed width above is the MINIMUM, not the only size.
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        Controls.AddRange(new Control[]
        {
            _lblIntro,
            btnStart, btnStop, btnClear, btnSave,
            _chkAutoSave, _chkAutoStartOnGate,
            lblSetup,
            lblTrack, _cmbTrack, btnColumns,
            lblLength, _numLapTarget, lblLaps, _numTimeTarget, lblMins,
            lblRolling, _numRollingWindow, lblRollingSuffix,
            _clock, _btnBigScreen,
            lblResults, _lblStatus, _grid,
        });

        MinimumSize = new Size(pageWidth, 560);
        Size = new Size(pageWidth, y + 300);
    }

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
        if (_active || _race.State != RaceState.Idle)
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
    /// Stops a running practice as the app closes, so it is saved (when
    /// "Save automatically on stop" is ticked) and its crash journal is
    /// cleared - the same as pressing Stop. Closing without this skipped
    /// the save, and left the practice in the journal to be found at the
    /// next launch. Does nothing when no practice is running.
    /// </summary>
    public void StopForShutdown()
    {
        if (_active)
        {
            StopPractice("the app was closed");
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
                    + "saved if \"Save demo races to history\" is ticked). Starting practice clears the board.\r\n\r\n"
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

        if (!ConfirmRaceControlCanBeCleared())
        {
            return;
        }

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
    private void StopPractice(string? reason = null)
    {
        if (!_active)
        {
            return;
        }

        // Save BEFORE unsubscribing and resetting - Reset clears the
        // standings this reads from, so afterwards there'd be nothing left
        // to save.
        var autoSaved = false;
        var savedDrivers = 0;
        if (_settings.AutoSavePractice)
        {
            savedDrivers = SaveSession(silent: true);
            autoSaved = savedDrivers > 0;
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
        new RaceJournal().Clear();

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
        else
        {
            _lblStatus.Text = $"Stopped{suffix}";
            _lblStatus.ForeColor = AppTheme.TextMuted;
        }

        PracticeStopped?.Invoke();
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
            // The leading real car - a ghost lapping ahead of everybody used
            // to end the practice on reaching the target by itself.
            var leaderLaps = _race.RaceLeader()?.Laps ?? 0;
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
    }

    private void RefreshGrid()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        var previouslySelected = _grid.CurrentRow?.Tag as string;
        var window = (int)_numRollingWindow.Value;

        _grid.Rows.Clear();

        // Sorted by best lap rather than by position - in practice nobody
        // cares who's "winning", they care who's quick.
        var standings = _race.GetOrderedStandings()
            .Where(s => s.LapTimesMs.Count > 0)
            .OrderBy(s => s.FastestLapMs == 0 ? long.MaxValue : s.FastestLapMs)
            .ToList();

        // Session-wide quickest lap, for the gap-to-leader column.
        var fastestOverall = standings
            .Where(s => s.FastestLapMs > 0)
            .Select(s => s.FastestLapMs)
            .DefaultIfEmpty(0)
            .Min();

        _tracker.Observe(standings, _race.SessionGeneration);

        var context = new KpiContext
        {
            Ordered = standings,
            Profiles = _profiles,
            Tracker = _tracker,
            RollingWindow = window,
            FastestOverall = fastestOverall,
            StreakThreshold = _race.StreakThreshold,
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
                _grid.Rows[rowIndex].Selected = true;
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
    }

    /// <summary>
    /// Writes the current practice laps into race history, flagged as a
    /// Practice session. That flag is what keeps it out of track records
    /// and season points (both of which filter to real races) while still
    /// letting Personal Bests and the Lap Times chart use the data - which
    /// is the whole reason to save it rather than let it evaporate.
    /// </summary>
    private int SaveSession(bool silent = false)
    {
        // Ghosts are what a practice session is usually run against, so
        // leaving them in a saved result would put a made-up lap time into
        // the personal bests and the pace charts as if somebody had driven
        // it.
        var standings = _race.GetOrderedStandings()
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
                    CarModel = profile?.CarModel ?? string.Empty,
                    Laps = s.Laps,
                    FastLapMs = s.FastestLapMs,
                    ElapsedMs = s.ElapsedMs,
                    Status = "practice",
                    LapTimesMs = s.LapTimesMs.ToList(),
                };
            }).ToList();

        var trackName = _cmbTrack.SelectedItem as string;
        var record = new SessionRecord
        {
            SessionType = RaceSessionType.Practice,
            TrackLayoutName = trackName is null or "(none)" ? string.Empty : trackName,
            Label = "Practice",
            StartedAtUtc = _startedAtUtc ?? DateTime.UtcNow,
            EndedAtUtc = DateTime.UtcNow,
            Results = results,
        };

        _history.Add(record);

        if (silent)
        {
            return results.Count;
        }

        AppDialog.Show(this,
            $"Saved {results.Count} driver{(results.Count == 1 ? "" : "s")} to Race History as a Practice session.",
            "Practice saved", MessageBoxButtons.OK, MessageBoxIcon.Information);

        return results.Count;
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
