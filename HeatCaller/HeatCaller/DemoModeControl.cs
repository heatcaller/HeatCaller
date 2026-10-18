namespace HeatCaller;

/// <summary>
/// Lets the user run a fully simulated race - no decoder required - to
/// rehearse the whole voice/sound/leaderboard setup before race day.
/// Drives the real, shared RaceControlSession, so switch
/// to the Race Control tab while a demo is running to watch it play out on
/// the actual leaderboard and race clock.
/// </summary>
public sealed class DemoModeControl : UserControl
{
    private readonly DemoRaceSimulator _simulator;
    private readonly RaceControlSession _race;
    private readonly ProfileStore _profiles;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly AppSettings _settings;

    private readonly ComboBox _cmbTrackLayout = new();

    /// <summary>
    /// Which scheduled round to run, or free-form.
    ///
    /// Added because a demo was only ever reachable for a round through the
    /// Program tab's right-click menu, and getting there meant selecting
    /// the round, which is the same gesture that arms it for a REAL start.
    /// Picking it here instead means any heat, qualifier or main can be
    /// rehearsed from the page whose whole job is rehearsing.
    /// </summary>
    private readonly ComboBox _cmbRound = new();

    private readonly Label _lblRoundHint = new();
    private readonly CheckedListBox _clbDrivers = new ThemedCheckedListBox();
    private readonly NumericUpDown _numPlaceholderCount = new();
    private readonly NumericUpDown _numStagingSeconds = new();
    private readonly NumericUpDown _numCountdownSeconds = new();
    private readonly Button _btnStart = new();
    private readonly Button _btnStop = new();
    private readonly TextBox _txtLog = new();
    private readonly Label _lblStatus = new();
    private readonly SteadyLabel _lblPaceHint = new();

    /// <summary>
    /// The ghost cars and whether they are switched on, so a demo of ghosts
    /// alone can be refused when none of them would actually run. Optional:
    /// without it that check is skipped, as it always was.
    /// </summary>
    private readonly GhostStore? _ghostStore;

    public DemoModeControl(RaceControlSession race, AnnouncementService announcer, ProfileStore profiles, TrackLayoutStore trackLayouts, AppSettings settings,
        RoundDefinitionStore? rounds = null, RaceEventStore? events = null, GhostStore? ghostStore = null)
    {
        _profiles = profiles;
        _trackLayouts = trackLayouts;
        _settings = settings;
        _rounds = rounds;
        _events = events;
        _ghostStore = ghostStore;
        _race = race;
        _simulator = new DemoRaceSimulator(race, announcer);
        _simulator.LogMessage += OnSimulatorLog;
        _simulator.TakenOver += OnSimulatorTakenOver;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No design size and no MinimumSize: the 900x560 floor was honoured
        // over the size the page was given, and a small window lost the log
        // off the bottom. A short window scrolls the page instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshTrackCombo();
        RefreshRoundCombo();
        RefreshDriverList();
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

    /// <summary>The programme, when the host has one to give. Null leaves the round picker out.</summary>
    private readonly RoundDefinitionStore? _rounds;

    /// <summary>Scopes the round list to today, so last month's heats are not in the dropdown.</summary>
    private readonly RaceEventStore? _events;

    /// <summary>The free-form entry, which is index 0 and never a round.</summary>
    private const string FreeForm = "Free-form - use the drivers ticked below";

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The save-to-history box, and the settings' captions, measured for the least the cards beside the racers can do with - see RacersWidth.</summary>
    private readonly CheckBox _chkSaveDemo = new ThemedCheckBox();
    private readonly CheckBox _chkDemoNight = new ThemedCheckBox();
    private readonly List<Label> _settingsCaptions = new();

    /// <summary>The racers' card and the caption of its round picker, which the column's width is measured from - see RacersWidth.</summary>
    private Section? _racers;
    private Label? _lblRun;

    /// <summary>The cards and the racers' column beside them, laid out again when the round list is refilled - its longest round can widen the column.</summary>
    private Beside? _band;

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. Forgotten when their lists are refilled.</summary>
    private (Font Font, int Count, int Width)? _trackWidth;
    private (Font Font, int Count, int Width)? _roundWidth;

    /// <summary>The log's monospaced face, made for the text size in use.</summary>
    private Font? _logFont;

    /// <summary>The racers' column at the normal text size, at its narrowest and widest - see RacersWidth.</summary>
    private const int RacersLeastWidth = 320;
    private const int RacersMostWidth = 480;

    /// <summary>The narrowest the cards beside the racers get, at the normal text size.</summary>
    private const int OthersLeastWidth = 440;

    /// <summary>The fewest drivers the list shows before the page scrolls instead.</summary>
    private const int DriverListLeastRows = 4;

    /// <summary>The fewest lines the log shows before the page scrolls instead.</summary>
    private const int LogLeastLines = 5;

    /// <summary>
    /// Builds the page: what a demo is; then, down the left, the buttons
    /// that start and stop it with what it is doing beside them, the
    /// settings card, and the log taking the height that is left - and down
    /// the right, beside all of it, the racers, their list taking the
    /// column's height. On the layout pieces (LayoutPieces.cs), so nothing
    /// is placed by hand.
    ///
    /// It used to be two group boxes side by side, each half of a 900px
    /// page, levelled by hand; the settings, the buttons and a log 14 lines
    /// tall stacked under them, all in pixels measured once at build - and a
    /// 900x560 floor the page kept however small its window. The log's type
    /// was set once, so it never followed the text size.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Runs a fake race through the same race-control logic as a real one - no decoder needed. " +
                   "Voice announcements and sound effects fire for real, so this is a genuine rehearsal of your setup before race day. " +
                   "Switch to the Race Control page to watch it play out on the actual leaderboard and clock.\n\n" +
                   "Demo races never set track records, and are only kept in Race History if you tick the box below - even then " +
                   "they are marked [DEMO] and never count for championship points, heats, achievements or personal bests.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- Starting and stopping, and what it is doing ---

        _btnStart.Text = "Start Demo Race";
        _btnStart.Click += BtnStart_Click;

        _btnStop.Text = "Stop";
        _btnStop.Enabled = false;
        _btnStop.Click += BtnStop_Click;

        // At the end of the buttons' line, cut short with "..." rather than
        // wrapped - or on a line of its own when it no longer fits there
        // (FieldRow).
        _lblStatus.Text = "Idle.";
        _lblStatus.AutoSize = false;
        _lblStatus.AutoEllipsis = true;
        _textSize.Wear(_lblStatus, () => AppTheme.BoldFont);

        var run = new FieldRow();

        run.Add(null, new ButtonBar().With(_btnStart, _btnStop));
        run.AddLast(null, _lblStatus, () => _lblStatus.GetPreferredSize(Size.Empty).Width);

        // --- Settings ---

        _cmbTrackLayout.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrackLayout.SelectedIndexChanged += (_, _) =>
        {
            UpdatePaceHint();
            var selected = _cmbTrackLayout.SelectedItem as string;
            _settings.LastDemoTrackName = selected is null || selected.StartsWith("(none") ? string.Empty : selected;
            _settings.Save();
        };

        // As tall as the longest thing it says, before it says anything -
        // UpdatePaceHint fills it once a track is picked - so the rows under
        // it are where they belong from the start, and a track picked later
        // never moves them (SteadyLabel). It used to be measured once, at
        // build, against a width worked out by hand.
        _lblPaceHint.Wordings = new[] { NoPaceHint, "Pacing laps around 999.99s (this track's average)." };

        _numStagingSeconds.Minimum = 1;
        _numStagingSeconds.Maximum = 60;
        _numStagingSeconds.Value = Math.Clamp(_settings.LastDemoStagingSeconds, (int)_numStagingSeconds.Minimum, (int)_numStagingSeconds.Maximum);
        _numStagingSeconds.ValueChanged += (_, _) =>
        {
            _settings.LastDemoStagingSeconds = (int)_numStagingSeconds.Value;
            _settings.Save();
        };

        _numCountdownSeconds.Minimum = 1;
        _numCountdownSeconds.Maximum = 30;
        _numCountdownSeconds.Value = Math.Clamp(_settings.LastDemoCountdownSeconds, (int)_numCountdownSeconds.Minimum, (int)_numCountdownSeconds.Maximum);
        _numCountdownSeconds.ValueChanged += (_, _) =>
        {
            _settings.LastDemoCountdownSeconds = (int)_numCountdownSeconds.Value;
            _settings.Save();
        };

        // Short, with what a saved demo is in the hint under it: at a large
        // text size the whole of it in the box's caption was wider than the
        // card beside the racers could be.
        _chkSaveDemo.Text = "Save demo races to Race History";
        _chkSaveDemo.AutoSize = true;
        _chkSaveDemo.Checked = _settings.SaveDemoSessions;
        _chkSaveDemo.CheckedChanged += (_, _) =>
        {
            _settings.SaveDemoSessions = _chkSaveDemo.Checked;
            _settings.Save();
        };

        var lblSaveDemoHint = new Label
        {
            Text = "Off by default so fake results stay out of your real history. Turn it on to keep a copy of each demo "
                 + "race, marked [DEMO], to look back over or print a results card from. A saved demo is never scored - no "
                 + "championship points, no achievements, no personal bests, no track records - whatever else it is tied to.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblSaveDemoHint, () => AppTheme.SmallFont);

        _chkDemoNight.Text = "Let a demo fill in the race program (rehearse a whole night)";
        _chkDemoNight.AutoSize = true;
        _chkDemoNight.Checked = _settings.DemoFillsTheProgramme;
        _chkDemoNight.CheckedChanged += (_, _) =>
        {
            _settings.DemoFillsTheProgramme = _chkDemoNight.Checked;

            // Implied, not merely required: there is nothing to tie a round
            // to without a saved race, and a club that ticked this and then
            // watched the program never move would have no way of knowing
            // why. Shown as well as set, so the box agrees with what is
            // happening.
            if (_chkDemoNight.Checked && !_settings.SaveDemoSessions)
            {
                _settings.SaveDemoSessions = true;
                _chkSaveDemo.Checked = true;
            }

            _settings.Save();
        };

        var lblDemoNightHint = new Label
        {
            Text = "Off by default. With this on, a demo race marks its heat as run, the program moves to the next one, "
                 + "and qualifying and the mains - bump-ups and all - are worked out from the demo results. That is how you "
                 + "rehearse a whole night: heats, qualifying, mains, bump-ups, the lot. It still scores nothing - no "
                 + "championship points, no achievements, no personal bests, no track records - because every one of those is "
                 + "worked out separately and each one ignores a demo. A demo will not replace a heat that has really been "
                 + "run. To clear a rehearsed night, delete its [DEMO] races from Race History and the heats go back to "
                 + "unrun. Needs demo races saved to history, so ticking this ticks that.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblDemoNightHint, () => AppTheme.SmallFont);

        var fields = new FieldGrid();

        // Short captions: "Countdown duration (sec):" set the caption column,
        // and at 200% took the room the track picker needed beside the
        // racers (layout test, 29 Sep).
        _settingsCaptions.Add(fields.AddRow("Track layout:", _cmbTrackLayout));
        fields.AddHint(_lblPaceHint);
        _settingsCaptions.Add(fields.AddRow("Staging (sec):", _numStagingSeconds));
        _settingsCaptions.Add(fields.AddRow("Countdown (sec):", _numCountdownSeconds));

        var settings = new Section("Settings");

        settings.Add(fields);
        settings.Add(_chkSaveDemo, stretch: false, gapBelow: UiLayout.HintGap);
        settings.Add(lblSaveDemoHint, gapBelow: UiLayout.HintGap);
        settings.Add(_chkDemoNight, stretch: false, gapBelow: UiLayout.HintGap);
        settings.Add(lblDemoNightHint, gapBelow: 0);

        // --- The log ---

        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.WordWrap = true;

        // Monospaced so timestamps line up, a size under the body face -
        // made again for a new text size, which it never was when it was
        // set once here.
        _textSize.Keep(() =>
        {
            var size = Math.Max(7.5F, AppTheme.BodyFont.SizeInPoints - 2F);

            if (_logFont is not null && Math.Abs(_logFont.SizeInPoints - size) < 0.01F)
            {
                return;
            }

            var old = _logFont;

            _logFont = new Font("Consolas", size);
            _txtLog.Font = _logFont;
            old?.Dispose();
        });

        var log = new Section("Log");

        log.AddFill(_txtLog, () => Pieces.LineHeight(_txtLog.Font) * LogLeastLines + 8, gapBelow: 0);

        // --- Racers ---

        _cmbRound.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRound.SelectedIndexChanged += (_, _) => UpdateRoundMode();

        var lblDrivers = new Label
        {
            Text = "Check driver profiles to include (uses their real names - exercises real announcer/rivalry behavior):",
            AutoSize = true,
        };

        _clbDrivers.CheckOnClick = true;

        // Off, or the list rounds its height down to a whole number of rows
        // and leaves a gap underneath that nothing in the layout explains.
        _clbDrivers.IntegralHeight = false;
        _clbDrivers.ItemCheck += (_, _) =>
        {
            // BeginInvoke throws if the handle doesn't exist yet, and
            // SetItemChecked during list population raises this event
            // before the control is ever shown.
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            BeginInvoke(SaveCheckedDrivers);
        };

        _numPlaceholderCount.Minimum = 0;
        _numPlaceholderCount.Maximum = 8;
        _numPlaceholderCount.Value = Math.Clamp(_settings.LastDemoPlaceholderCount, (int)_numPlaceholderCount.Minimum, (int)_numPlaceholderCount.Maximum);
        _numPlaceholderCount.ValueChanged += (_, _) =>
        {
            _settings.LastDemoPlaceholderCount = (int)_numPlaceholderCount.Value;
            _settings.Save();
        };

        // What to run above the tick list rather than beside it: this is the
        // choice that decides whether the tick list means anything at all,
        // and a control that governs another one belongs before it.
        var runGrid = new FieldGrid();

        _lblRun = runGrid.AddRow("Run:", _cmbRound, stretch: true);
        runGrid.AddHint(_lblRoundHint);

        _racers = new Section("Racers");

        _racers.Add(runGrid);
        _racers.Add(lblDrivers);
        _racers.AddFill(_clbDrivers, () => DriverListItemHeight() * DriverListLeastRows + 4);

        // The spinner after its caption, and under it when the column is too
        // narrow for both on one line.
        _racers.Add(FieldGrid.Inline(new Label { Text = "Plus this many made-up placeholder racers:" }, _numPlaceholderCount), gapBelow: 0);

        // The pickers and the boxes at the text size in use - see SizeFields.
        _textSize.Keep(SizeFields);

        // --- The page ---

        Beside? band = null;

        band = new Beside(() => RacersWidth(band?.Width ?? 0));

        band.Add(run);
        band.Add(settings);
        band.AddFill(log, log.LeastHeight);
        band.AddSideFill(_racers, _racers.LeastHeight, gapBelow: 0);

        _band = band;

        var page = new FillPage();

        page.Add(lblIntro);
        page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// The fields' sizes at the text size in use: the number boxes the width
    /// they were at the normal text size, at this size, and the track picker
    /// as wide as its longest choice and never narrower than a field was
    /// then. Run again when the text size changes (TextSize.Keep) and when
    /// the lists are refilled.
    /// </summary>
    private void SizeFields()
    {
        var number = UiLayout.AtTextSize(UiLayout.NumericWidth);

        Fit(_cmbTrackLayout, TrackPickerWidth());
        Fit(_numStagingSeconds, number);
        Fit(_numCountdownSeconds, number);
        Fit(_numPlaceholderCount, number);

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
    /// The racers' column: about two fifths of the page, between 320px and
    /// 480px at the normal text size, at the size in use - and never
    /// narrower than its caption, or than Run: and a picker as wide as its
    /// longest choice need. The other cards have the rest, and never less
    /// than 440px at the text size in use, or than the settings' captions
    /// and fields or the save box need; too narrow for both at that, and
    /// each gives up the same share of its width, as Batteries' two cards
    /// do. Nothing here scrolls sideways (FillPage).
    /// </summary>
    private int RacersWidth(int band)
    {
        var run = (_lblRun?.GetPreferredSize(Size.Empty).Width ?? 0) + UiLayout.LabelGap
            + Pieces.PickerWidth(_cmbRound, ref _roundWidth);

        var needs = Math.Max(_racers?.CaptionWidth ?? 0, run) + Section.Inset.Horizontal;

        var racers = Math.Max(
            Math.Clamp((int)(band * 0.4f), UiLayout.AtTextSize(RacersLeastWidth), UiLayout.AtTextSize(RacersMostWidth)),
            needs);

        // The settings' caption column and their widest field, as the grid
        // lays them out - not the grid's own width on one line, which counts
        // the pace hint unwrapped. The picker's width as it is meant to be,
        // not as it is: a table cuts a field down to the column it has.
        var settings = _settingsCaptions.Select(caption => caption.GetPreferredSize(Size.Empty).Width).DefaultIfEmpty(0).Max()
            + UiLayout.LabelGap
            + Math.Max(TrackPickerWidth(), Math.Max(_numStagingSeconds.Width, _numCountdownSeconds.Width));

        var othersLeast = Math.Max(
            UiLayout.AtTextSize(OthersLeastWidth),
            Math.Max(settings, _chkSaveDemo.GetPreferredSize(Size.Empty).Width) + Section.Inset.Horizontal);

        // What the two share: all of the band but the gap a Beside leaves
        // between its parts and its column.
        var room = Math.Max(0, band - UiLayout.RowGap);

        if (racers + othersLeast > room)
        {
            racers = (int)((long)racers * room / (racers + othersLeast));
        }

        return Math.Max(0, racers);
    }

    /// <summary>The track picker: as wide as its longest choice, and never narrower than a field was at the normal text size.</summary>
    private int TrackPickerWidth() =>
        Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbTrackLayout, ref _trackWidth));

    /// <summary>A row of the driver list, in the face it is drawn in.</summary>
    private int DriverListItemHeight() => Math.Max(_clbDrivers.ItemHeight, _clbDrivers.Font.Height);

    /// <summary>Call whenever this tab becomes visible, since track layouts and driver profiles can be added/edited elsewhere while it's not active.</summary>
    public void RefreshAvailableData()
    {
        RefreshTrackCombo();
        RefreshRoundCombo();
        RefreshDriverList();
    }

    /// <summary>One scheduled round in the dropdown.</summary>
    private sealed record RoundChoice(RoundDefinition Round)
    {
        public override string ToString()
        {
            var kind = Round.Kind switch
            {
                RoundKind.Main => "Main",
                RoundKind.Practice => "Practice",
                _ => "Qualifier",
            };

            var run = Round.IsRecorded ? "  -  already run" : string.Empty;

            return $"{Round.RoundLabel}  -  {Round.ClassName} ({kind}){run}";
        }
    }

    /// <summary>The round the demo will run, or null for free-form.</summary>
    /// <summary>
    /// The ghost cars picked for the demo that is running - the host starts
    /// only these, instead of every enabled ghost.
    /// </summary>
    public IReadOnlyCollection<string> SelectedGhosts => _simulator.SelectedGhosts;

    /// <summary>Race Control's show before the start, run when a demo stages - see DemoRaceSimulator.Staged.</summary>
    public Func<IReadOnlyList<string>, TimeSpan?>? ShowAtStaging
    {
        get => _simulator.Staged;
        set => _simulator.Staged = value;
    }

    /// <summary>
    /// The track the demo is run on - the layout picked on this page, or
    /// null for the generic made-up pace. The host hands it to the ghost
    /// cars at the green, so a PB or track-record ghost in a demo is paced
    /// from the same track as the simulated cars. It used to be given Race
    /// Control's track, which has nothing to do with the demo. The picker
    /// is locked while a demo runs, so this cannot change under one.
    ///
    /// UI thread only, as the picker is.
    /// </summary>
    public string? SelectedTrackName => SelectedTrack()?.Name;

    private RoundDefinition? ChosenRound() => (_cmbRound.SelectedItem as RoundChoice)?.Round;

    /// <summary>
    /// Fills the round dropdown from today's programme.
    ///
    /// Recorded rounds are INCLUDED, and deliberately: rehearsing a main
    /// that has already been run is the most useful demo there is, because
    /// it is the one whose grid you already know. Nothing here writes to
    /// the round either way.
    /// </summary>
    private void RefreshRoundCombo()
    {
        if (_rounds is null)
        {
            return;
        }

        var previousId = ChosenRound()?.Id;

        _cmbRound.Items.Clear();
        _cmbRound.Items.Add(FreeForm);

        var day = _events?.Active;

        var rounds = day is null || day.IsLegacy || day.RoundIds.Count == 0
            ? _rounds.All
            : _rounds.All.Where(r => day.RoundIds.Contains(r.Id, StringComparer.Ordinal)).ToList();

        foreach (var round in rounds)
        {
            _cmbRound.Items.Add(new RoundChoice(round));
        }

        var index = 0;

        if (previousId is not null)
        {
            for (var i = 1; i < _cmbRound.Items.Count; i++)
            {
                if (_cmbRound.Items[i] is RoundChoice choice && choice.Round.Id == previousId)
                {
                    index = i;
                    break;
                }
            }
        }

        _cmbRound.SelectedIndex = index;
        UpdateRoundMode();

        // The longest round may have come or gone, and the racers' column is
        // as wide as it needs - see RacersWidth.
        _roundWidth = null;
        _band?.PerformLayout();
    }

    /// <summary>
    /// Greys out what the round now decides.
    ///
    /// The tick list is DISABLED rather than re-ticked, which is the whole
    /// point of the change. Re-ticking it overwrote the driver selection
    /// this page saves across restarts - so running one heat as a demo
    /// silently threw away whatever set of drivers somebody had chosen for
    /// their own testing, and there was no way back to it.
    /// </summary>
    private void UpdateRoundMode()
    {
        var round = ChosenRound();

        _clbDrivers.Enabled = round is null;
        _numPlaceholderCount.Enabled = round is null;

        if (round is null)
        {
            _lblRoundHint.Text = "The demo runs whoever is ticked below, plus any placeholders.";
            _lblRoundHint.ForeColor = AppTheme.TextMuted;
            return;
        }

        var count = round.AssignedTransponders.Count;

        _lblRoundHint.Text = count == 0
            ? $"\"{round.RoundLabel}\" has no drivers assigned, so placeholders will stand in. "
              + "Nothing is written back to the program."
            : $"Runs the {Plural.Of(count, "driver")} already in \"{round.RoundLabel}\". Your own selection below is kept "
              + "and comes back when you choose free-form. Nothing is written back to the program.";

        _lblRoundHint.ForeColor = AppTheme.Accent;
    }

    private void RefreshTrackCombo()
    {
        var previous = _cmbTrackLayout.SelectedItem as string ?? (_settings.LastDemoTrackName.Length > 0 ? _settings.LastDemoTrackName : null);
        _cmbTrackLayout.Items.Clear();
        _cmbTrackLayout.Items.Add("(none - use a generic made-up pace)");
        foreach (var t in _trackLayouts.All)
        {
            _cmbTrackLayout.Items.Add(t.Name);
        }

        var idx = previous is not null ? _cmbTrackLayout.Items.IndexOf(previous) : -1;
        _cmbTrackLayout.SelectedIndex = idx >= 0 ? idx : 0;
        UpdatePaceHint();

        // The longest layout may have come or gone: measured again.
        _trackWidth = null;
        SizeFields();
    }

    private void RefreshDriverList()
    {
        var checkedNames = new HashSet<string>(
            _clbDrivers.CheckedItems.Cast<DriverListItem>().Select(i => i.Profile.Transponder),
            StringComparer.OrdinalIgnoreCase);

        // On first load (nothing checked yet in memory), fall back to whichever drivers were checked last time, saved across restarts.
        var useSaved = checkedNames.Count == 0 && _settings.LastDemoCheckedDrivers.Count > 0;

        _clbDrivers.Items.Clear();
        foreach (var profile in _profiles.AllRaceable)
        {
            var index = _clbDrivers.Items.Add(new DriverListItem(profile));
            var shouldCheck = useSaved
                ? _settings.LastDemoCheckedDrivers.Contains(profile.Transponder, StringComparer.OrdinalIgnoreCase)
                : checkedNames.Contains(profile.Transponder);
            if (shouldCheck)
            {
                _clbDrivers.SetItemChecked(index, true);
            }
        }
    }

    private void SaveCheckedDrivers()
    {
        _settings.LastDemoCheckedDrivers = _clbDrivers.CheckedItems.Cast<DriverListItem>().Select(i => i.Profile.Transponder).ToList();
        _settings.Save();
    }

    private TrackLayout? SelectedTrack() =>
        _cmbTrackLayout.SelectedIndex > 0 ? _trackLayouts.All.FirstOrDefault(t => t.Name == (string)_cmbTrackLayout.SelectedItem!) : null;

    /// <summary>
    /// The longest the pace hint ever gets, and the one the layout reserves
    /// room for. Named rather than written out twice so the reservation
    /// cannot drift from the wording.
    /// </summary>
    private const string NoPaceHint =
        "No average lap time set for this track - using a generic made-up pace. Set one when editing the track layout.";

    private void UpdatePaceHint()
    {
        var track = SelectedTrack();
        _lblPaceHint.Text = track?.AverageLapSeconds is > 0
            ? $"Pacing laps around {track.AverageLapSeconds:0.00}s (this track's average)."
            : NoPaceHint;
    }

    /// <summary>
    /// On Air in front of the demo (17 Oct 2026) - the round, or null for a
    /// demo of the checked drivers, and the demo's own start, which On Air
    /// calls in place of Stage. Wired by MainForm to Race Control's
    /// GoOnAirThenAsync, the same way the Race Program's "Run as Demo" goes,
    /// which starts it straight away when Start races On Air is unticked.
    /// Null: the demo starts straight away, as it always did.
    /// </summary>
    internal Func<RoundDefinition?, Action, Task>? GoOnAirThen { get; set; }

    private void BtnStart_Click(object? sender, EventArgs e)
    {
        if (ChosenRound() is { } round)
        {
            StartThroughOnAir(round, () => RunRound(round));
            return;
        }

        var realTransponders = _clbDrivers.CheckedItems.Cast<DriverListItem>().Select(i => i.Profile.Transponder).ToList();
        var placeholderCount = (int)_numPlaceholderCount.Value;

        if (realTransponders.Count == 0 && placeholderCount == 0)
        {
            AppDialog.Show(this, "Check at least one driver profile, or set the placeholder racer count above zero.",
                "No racers selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        StartThroughOnAir(null, () => StartSimulation(realTransponders, placeholderCount));
    }

    /// <summary>
    /// The Start button's demo, through On Air when MainForm has wired it -
    /// see <see cref="GoOnAirThen"/>. Async void, as a click is, so a fault
    /// surfaces the same way it did when the click started the demo itself.
    /// </summary>
    private async void StartThroughOnAir(RoundDefinition? round, Action start)
    {
        if (GoOnAirThen is { } onAir)
        {
            await onAir(round, start);
            return;
        }

        start();
    }

    /// <summary>
    /// Runs one scheduled round's own drivers, reading the round and
    /// writing nothing to it.
    ///
    /// The single path both the Start button and the Program tab's "Start
    /// as Demo" go through, so a demo launched either way is the same
    /// demo.
    /// </summary>
    private void RunRound(RoundDefinition round)
    {
        var transponders = round.AssignedTransponders.ToList();

        // A round with nobody in it still gets a usable race rather than
        // silently doing nothing - placeholders stand in, which is what the
        // Start button does in the same situation.
        var placeholders = transponders.Count == 0 ? Math.Max(1, (int)_numPlaceholderCount.Value) : 0;

        if (!StartSimulation(transponders, placeholders))
        {
            return;
        }

        _txtLog.AppendText($"{AppTime.LogStampNow()}  Running \"{round.RoundLabel}\" ({round.ClassName}) as a demo. "
            + $"{Plural.Of(transponders.Count, "driver")} from the program; the round itself is untouched.{Environment.NewLine}");
    }

    /// <summary>
    /// Runs a specific round's drivers as a demo race - used by "Start as
    /// Demo" on the Race Program's right-click menu.
    ///
    /// Goes through the same StartSimulation path as the Start button so
    /// there's one place that actually launches a demo, rather than a
    /// parallel copy that could drift out of step with it.
    /// </summary>
    public void StartDemoForRound(RoundDefinition round)
    {
        // Selects the round in the picker rather than re-ticking the driver
        // list. The old version ticked this round's drivers and unticked
        // everyone else - and every one of those ticks is SAVED, across
        // restarts, as this page's own driver selection. So demoing one
        // heat quietly destroyed whatever set somebody had built up for
        // their own testing, with nothing to put it back.
        for (var i = 1; i < _cmbRound.Items.Count; i++)
        {
            if (_cmbRound.Items[i] is RoundChoice choice && choice.Round.Id == round.Id)
            {
                _cmbRound.SelectedIndex = i;
                break;
            }
        }

        RunRound(round);
    }

    /// <summary>Shared launch path - settings are read from this page either way.</summary>
    private bool StartSimulation(IReadOnlyList<string> realTransponders, int placeholderCount)
    {
        // A demo drives the same race session as Race Control and Practice,
        // and starting one resets it. A real heat or a practice in progress
        // would be wiped without a word - so refuse, unless what's running is
        // this page's own demo (which the simulator stops and restarts).
        var busy = !_simulator.IsRunning
            && (_race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing);
        if (busy)
        {
            var what = _race.IsPracticeMode ? "A practice session" : "A race";
            var where = _race.IsPracticeMode ? "Stop practice first on the Practice page" : "Finish it or reset it on Race Control first";
            AppDialog.Show(this, $"{what} is running. Starting a demo would wipe it. {where}.",
                "Can't start a demo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (RefuseGhostOnlyDemoWithNoGhosts(realTransponders, placeholderCount))
        {
            return false;
        }

        _txtLog.Clear();

        var averageLapSeconds = SelectedTrack()?.AverageLapSeconds;
        _simulator.Start(realTransponders, placeholderCount, (int)_numStagingSeconds.Value, (int)_numCountdownSeconds.Value, averageLapSeconds);

        // Says exactly who is in this demo, so nobody has to work it out
        // from the leaderboard.
        var names = realTransponders
            .Select(t => _profiles.Get(t)?.DriverName is { Length: > 0 } name ? name : t)
            .ToList();

        if (placeholderCount > 0)
        {
            names.Add($"{placeholderCount} placeholder racer{(placeholderCount == 1 ? string.Empty : "s")}");
        }

        _txtLog.AppendText($"{AppTime.LogStampNow()}  In this demo: {(names.Count == 0 ? "nobody" : string.Join(", ", names))}. "
            + $"Ghost cars run only if ticked under Racers.{Environment.NewLine}");

        _btnStart.Enabled = false;
        _btnStop.Enabled = true;
        _cmbTrackLayout.Enabled = false;
        _clbDrivers.Enabled = false;
        _numPlaceholderCount.ReadOnly = true;
        _numStagingSeconds.ReadOnly = true;
        _numCountdownSeconds.ReadOnly = true;
        _lblStatus.Text = "Running...";
        return true;
    }

    /// <summary>
    /// True - after saying why - when the demo would have nobody in it: only
    /// ghost cars were picked, and none of them will run.
    ///
    /// Ghosts are run by the ghost service, not the simulator, and only when
    /// ghosts are switched on and that ghost is enabled. A demo of ghosts
    /// alone with them switched off used to start anyway - an empty race
    /// that no lap could ever finish, left running until somebody noticed.
    ///
    /// Only what can be known up front is checked: a ghost that is switched
    /// on but has no pace to run at is reported by the ghost service when
    /// the race goes green.
    /// </summary>
    private bool RefuseGhostOnlyDemoWithNoGhosts(IReadOnlyList<string> transponders, int placeholderCount)
    {
        if (_ghostStore is null || placeholderCount > 0 || transponders.Count == 0)
        {
            return false;
        }

        if (transponders.Any(t => !GhostService.IsGhost(t)))
        {
            return false; // a simulated car is in it
        }

        var ghostSettings = _ghostStore.Current;
        var anyWillRun = ghostSettings.Enabled
            && ghostSettings.Ghosts.Any(g => g.Enabled && transponders.Contains(g.Transponder, StringComparer.OrdinalIgnoreCase));

        if (anyWillRun)
        {
            return false;
        }

        var why = ghostSettings.Enabled
            ? "none of the ghost cars in it is switched on"
            : "ghost cars are switched off";

        AppDialog.Show(this,
            $"This demo has only ghost cars in it, and {why} - so nothing would ever cross the line and the demo would never finish.\n\n"
            + "Switch the ghosts on on the Ghost Cars page, or add a driver or some placeholder racers to the demo.",
            "Nobody to race", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }

    private void BtnStop_Click(object? sender, EventArgs e)
    {
        _simulator.Stop();
        OnDemoEnded("Stopped.");
    }

    /// <summary>
    /// Ends a running demo so Race Control can use the session for a real
    /// race - wired by the host to Race Control, which calls it just before
    /// it stages, starts or resets over a demo. The session is left for
    /// Race Control; the demo is not saved. Does nothing when no demo is
    /// running.
    /// </summary>
    public void StopForTakeover() => _simulator.StopForTakeover();

    /// <summary>The demo let go of the session because something else took it - puts this page back to idle.</summary>
    private void OnSimulatorTakenOver(string reason)
    {
        if (InvokeRequired) { BeginInvoke(() => OnSimulatorTakenOver(reason)); return; }

        OnDemoEnded("Stopped - Race Control took over the session.");
    }

    private void OnSimulatorLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => OnSimulatorLog(message)); return; }

        _txtLog.AppendText($"{AppTime.LogStampNow()}  {message}{Environment.NewLine}");

        if (message.Contains("complete", StringComparison.OrdinalIgnoreCase))
        {
            OnDemoEnded("Complete.");
        }
    }

    private void OnDemoEnded(string statusText)
    {
        _btnStart.Enabled = true;
        _btnStop.Enabled = false;
        _cmbTrackLayout.Enabled = true;

        // Back to whatever the round picker allows, not simply on: with a
        // round chosen the tick list does not decide who runs, and enabling
        // it here offered a selection the next Start would ignore.
        UpdateRoundMode();
        _numPlaceholderCount.ReadOnly = false;
        _numStagingSeconds.ReadOnly = false;
        _numCountdownSeconds.ReadOnly = false;
        _lblStatus.Text = statusText;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _simulator.Dispose();
        }
        base.Dispose(disposing);

        // After the log that wears it is gone.
        if (disposing)
        {
            _logFont?.Dispose();
            _logFont = null;
        }
    }

    private sealed record DriverListItem(DriverProfile Profile)
    {
        public override string ToString()
        {
            var name = string.IsNullOrWhiteSpace(Profile.DriverName)
                ? Profile.Transponder
                : $"{Profile.DriverName} ({Profile.Transponder})";

            // Ghosts marked, not hidden. A virtual opponent in a list of
            // real drivers is only confusing if you can't tell which is
            // which - the raw GHOST-xxxxxx transponder is no help.
            return GhostService.IsGhost(Profile.Transponder)
                ? $"{Profile.DriverName}  [ghost]"
                : name;
        }
    }
}
