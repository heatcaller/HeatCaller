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
    private readonly CheckedListBox _clbDrivers = new();
    private readonly NumericUpDown _numPlaceholderCount = new();
    private readonly NumericUpDown _numStagingSeconds = new();
    private readonly NumericUpDown _numCountdownSeconds = new();
    private readonly Button _btnStart = new();
    private readonly Button _btnStop = new();
    private readonly TextBox _txtLog = new();
    private readonly Label _lblStatus = new();
    private readonly Label _lblPaceHint = new();

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

        // Design size set before the layout is built, so bottom-anchored
        // children resolve their offsets against real dimensions rather
        // than WinForms' default.
        Size = new Size(960, 560);
        MinimumSize = new Size(900, 560);
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshTrackCombo();
        RefreshRoundCombo();
        RefreshDriverList();
    }

    /// <summary>The programme, when the host has one to give. Null leaves the round picker out.</summary>
    private readonly RoundDefinitionStore? _rounds;

    /// <summary>Scopes the round list to today, so last month's heats are not in the dropdown.</summary>
    private readonly RaceEventStore? _events;

    /// <summary>The free-form entry, which is index 0 and never a round.</summary>
    private const string FreeForm = "Free-form - use the drivers ticked below";

    /// <summary>The gap between a group box's own edge and the controls inside it.</summary>
    private const int Inset = 12;

    /// <summary>
    /// Builds the page.
    ///
    /// Two structural changes over what was here before, both of which the
    /// layout pass was previously left to paper over on every launch.
    ///
    /// The log is now docked to the bottom and the rest of the page sits in
    /// a scrolling panel above it. It used to be placed at a fixed y of 498
    /// in a page 560 tall, so it ran 98 pixels off the end before anybody
    /// changed a font.
    ///
    /// And every position is derived from the control above or beside it
    /// rather than written down. The wrapped paragraphs are the reason: the
    /// intro, the driver-list caption and the save-to-history note all grow
    /// by a line or two at the current body size, and each one was sitting
    /// directly on top of whatever had been placed beneath it.
    /// </summary>
    private void BuildLayout()
    {
        var content = MinimumSize.Width - UiLayout.PageLeft - UiLayout.PageRight;
        var column = (content - UiLayout.RowGap) / 2;

        var body = new Panel { Dock = DockStyle.Fill };

        // Scrolls down only - switched off before AutoScroll goes on, or
        // AutoScroll brings it back. A few pixels of overflow across made a
        // full-width sideways bar that looked like a divider between the
        // page and its log.
        body.HorizontalScroll.Maximum = 0;
        body.HorizontalScroll.Enabled = false;
        body.HorizontalScroll.Visible = false;
        body.AutoScroll = true;

        var lblIntro = new Label
        {
            Text = "Runs a fake race through the same race-control logic as a real one - no decoder needed. " +
                   "Voice announcements and sound effects fire for real, so this is a genuine rehearsal of your setup before race day. " +
                   "Switch to the Race Control tab to watch it play out on the actual leaderboard and clock.\n\n" +
                   "Demo races never set track records, and are only kept in Race History if you tick the box below - even then " +
                   "they are marked [DEMO] and never count for championship points, heats, badges or personal bests.",
            Location = new Point(UiLayout.PageLeft, UiLayout.PageTop),
            AutoSize = true,
            MaximumSize = new Size(content, 0),
            ForeColor = AppTheme.TextMuted,
        };

        var groupTop = UiLayout.Below(lblIntro, UiLayout.SectionGap);

        var grpTrack = BuildTrackGroup(column);
        grpTrack.Location = new Point(UiLayout.PageLeft, groupTop);

        var grpRacers = BuildRacersGroup(column);
        grpRacers.Location = new Point(UiLayout.PageLeft + column + UiLayout.RowGap, groupTop);

        // Levelled, so two boxes side by side read as a pair rather than as
        // one box and a shorter box.
        var tallest = Math.Max(grpTrack.Height, grpRacers.Height);
        grpTrack.Height = tallest;
        grpRacers.Height = tallest;

        var y = groupTop + tallest + UiLayout.SectionGap;

        var chkSaveDemo = new CheckBox
        {
            Text = "Save demo races to Race History (marked [DEMO], never scored)",
            Location = new Point(UiLayout.PageLeft, y),
            AutoSize = true,
            Checked = _settings.SaveDemoSessions,
        };

        chkSaveDemo.CheckedChanged += (_, _) =>
        {
            _settings.SaveDemoSessions = chkSaveDemo.Checked;
            _settings.Save();
        };

        var lblSaveDemoHint = new Label
        {
            Text = "Off by default so fake results stay out of your real history. Turn it on to keep a copy of each demo "
                 + "race to look back over or print a results card from. A saved demo is never linked to a heat - even if "
                 + "a round is picked in the Round: box on Race Control - and never earns championship points, badges, "
                 + "personal bests or track records.",
            AutoSize = true,
            MaximumSize = new Size(content - UiLayout.Indent, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(UiLayout.PageLeft + UiLayout.Indent, UiLayout.Below(chkSaveDemo, UiLayout.HintGap)),
        };

        y = UiLayout.Below(lblSaveDemoHint, UiLayout.SectionGap);

        _btnStart.Text = "Start Demo Race";
        _btnStart.Click += BtnStart_Click;

        _btnStop.Text = "Stop";
        _btnStop.Enabled = false;
        _btnStop.Click += BtnStop_Click;

        // Sized to their captions in the bold face the theme gives buttons,
        // rather than to the 160x32 that "Start Demo Race" outgrew.
        UiLayout.ButtonRow(UiLayout.PageLeft, y, _btnStart, _btnStop);

        _lblStatus.Text = "Idle.";
        _lblStatus.AutoSize = true;
        _lblStatus.Font = AppTheme.BoldFont;
        _lblStatus.Location = new Point(UiLayout.PageLeft, UiLayout.Below(_btnStart, UiLayout.RowGap));

        // --- The log, part of the page under the buttons ---
        //
        // It used to be pinned to the bottom of the window in a strip of
        // its own, cut off from the buttons that drive it. Now it follows
        // straight on from the status line, the same width as everything
        // above, and scrolls inside itself as the demo writes to it.
        var lblLog = new Label
        {
            Text = "LOG",
            AutoSize = true,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.Primary,
            Location = new Point(UiLayout.PageLeft, UiLayout.Below(_lblStatus, UiLayout.SectionGap)),
        };

        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.WordWrap = true;

        // Monospaced so timestamps line up, sized off the body font so it
        // still follows Ctrl+ and Ctrl- like the rest of the app.
        _txtLog.Font = new Font("Consolas", Math.Max(7.5F, AppTheme.BodyFont.SizeInPoints - 2F));
        _txtLog.Location = new Point(UiLayout.PageLeft, UiLayout.Below(lblLog, UiLayout.HeadingGap));
        _txtLog.Size = new Size(content, _txtLog.Font.Height * 14 + 8);

        body.Controls.AddRange(new Control[]
        {
            lblIntro, grpTrack, grpRacers, chkSaveDemo, lblSaveDemoHint, _btnStart, _btnStop, _lblStatus, lblLog, _txtLog,
        });

        Controls.Add(body);
    }

    private GroupBox BuildTrackGroup(int column)
    {
        var group = new GroupBox
        {
            Text = "Track && Timing",
            Font = AppTheme.BodyFont,
            Width = column,
        };

        _cmbTrackLayout.Font = AppTheme.BodyFont;
        _numStagingSeconds.Font = AppTheme.BodyFont;
        _numCountdownSeconds.Font = AppTheme.BodyFont;

        var lblTrack = new Label { Text = "Track layout:", AutoSize = true };
        var lblStaging = new Label { Text = "Staging duration (sec):", AutoSize = true };
        var lblCountdown = new Label { Text = "Countdown duration (sec):", AutoSize = true };

        // One field column for the whole box, wide enough for the longest
        // caption in it. Three captions of different lengths each pushing
        // their own field to a different x is what makes a box look
        // unfinished even when nothing overlaps.
        //
        // MEASURED IN THE FONT THEY ARE ABOUT TO BE DRAWN IN. PreferredWidth
        // on a label that has not been added to anything yet is measured in
        // the WinForms default font, which is smaller than this app's - so
        // the column came out narrow and "Countdown duration (sec):" ran
        // into the box beside it. Same trap as UiLayout.After's
        // single-argument overload, and the same answer.
        var fieldLeft = Inset + new[] { lblTrack, lblStaging, lblCountdown }
            .Max(l => TextRenderer.MeasureText(l.Text, AppTheme.BodyFont).Width) + UiLayout.LabelGap;

        var y = UiLayout.InsideGroup(group);

        _cmbTrackLayout.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrackLayout.Width = Math.Max(120, column - Inset - fieldLeft);
        _cmbTrackLayout.Location = new Point(fieldLeft, y);
        _cmbTrackLayout.SelectedIndexChanged += (_, _) =>
        {
            UpdatePaceHint();
            var selected = _cmbTrackLayout.SelectedItem as string;
            _settings.LastDemoTrackName = selected is null || selected.StartsWith("(none") ? string.Empty : selected;
            _settings.Save();
        };

        lblTrack.Location = new Point(Inset, UiLayout.CentreOn(lblTrack, y, _cmbTrackLayout.Height));

        _lblPaceHint.AutoSize = true;
        _lblPaceHint.MaximumSize = new Size(_cmbTrackLayout.Width, 0);
        _lblPaceHint.ForeColor = AppTheme.TextMuted;
        _lblPaceHint.Font = AppTheme.SmallFont;
        _lblPaceHint.Location = new Point(fieldLeft, UiLayout.Below(_cmbTrackLayout, UiLayout.HintGap));

        // Room for the longest thing the hint can ever say, reserved before
        // it says anything.
        //
        // The text arrives later - UpdatePaceHint fills it once a track is
        // picked - so while this runs the label is empty and measures as
        // nothing. Two lines used to be reserved on the assumption that was
        // enough. The no-average-lap wording runs to four at this width, and
        // the label, sitting above the spinner in the z-order, grew straight
        // down over Staging duration: the box was not just covered, it was
        // unclickable, because every click landed on the label in front of
        // it. Measuring the worst case here means the rows below are in the
        // right place before a word of it exists, at any text size.
        var hintWidth = Math.Max(40, _lblPaceHint.MaximumSize.Width);
        var reserved = Math.Max(
            TextRenderer.MeasureText(NoPaceHint, AppTheme.SmallFont, new Size(hintWidth, int.MaxValue), TextFormatFlags.WordBreak).Height,
            _lblPaceHint.PreferredSize.Height);

        y = _lblPaceHint.Top + reserved + UiLayout.RowGap;

        _numStagingSeconds.Width = UiLayout.NumericWidth;
        _numStagingSeconds.Minimum = 1;
        _numStagingSeconds.Maximum = 60;
        _numStagingSeconds.Value = Math.Clamp(_settings.LastDemoStagingSeconds, (int)_numStagingSeconds.Minimum, (int)_numStagingSeconds.Maximum);
        _numStagingSeconds.Location = new Point(fieldLeft, y);
        _numStagingSeconds.ValueChanged += (_, _) =>
        {
            _settings.LastDemoStagingSeconds = (int)_numStagingSeconds.Value;
            _settings.Save();
        };

        lblStaging.Location = new Point(Inset, UiLayout.CentreOn(lblStaging, y, _numStagingSeconds.Height));

        y = UiLayout.Below(_numStagingSeconds, UiLayout.RowGap);

        _numCountdownSeconds.Width = UiLayout.NumericWidth;
        _numCountdownSeconds.Minimum = 1;
        _numCountdownSeconds.Maximum = 30;
        _numCountdownSeconds.Value = Math.Clamp(_settings.LastDemoCountdownSeconds, (int)_numCountdownSeconds.Minimum, (int)_numCountdownSeconds.Maximum);
        _numCountdownSeconds.Location = new Point(fieldLeft, y);
        _numCountdownSeconds.ValueChanged += (_, _) =>
        {
            _settings.LastDemoCountdownSeconds = (int)_numCountdownSeconds.Value;
            _settings.Save();
        };

        lblCountdown.Location = new Point(Inset, UiLayout.CentreOn(lblCountdown, y, _numCountdownSeconds.Height));

        group.Controls.AddRange(new Control[]
        {
            lblTrack, _cmbTrackLayout, _lblPaceHint, lblStaging, _numStagingSeconds, lblCountdown, _numCountdownSeconds,
        });

        // In front of the hint above them. The reservation should mean they
        // never meet, but a spin box that a label can cover is a control
        // that silently stops working, and the z-order costs nothing.
        _numStagingSeconds.BringToFront();
        _numCountdownSeconds.BringToFront();

        group.Height = UiLayout.Below(_numCountdownSeconds, Inset);

        return group;
    }

    private GroupBox BuildRacersGroup(int column)
    {
        var group = new GroupBox
        {
            Text = "Racers",
            Font = AppTheme.BodyFont,
            Width = column,
        };

        var inner = column - Inset * 2;

        var groupTop = UiLayout.InsideGroup(group);

        // --- What to run ---
        //
        // Above the tick list rather than beside it: this is the choice
        // that decides whether the tick list means anything at all, and a
        // control that governs another one belongs before it.
        var lblRound = new Label
        {
            Text = "Run:",
            AutoSize = true,
            Font = AppTheme.BodyFont,
        };

        _cmbRound.Font = AppTheme.BodyFont;
        _cmbRound.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRound.SelectedIndexChanged += (_, _) => UpdateRoundMode();

        lblRound.Location = new Point(Inset, UiLayout.CentreOn(lblRound, groupTop, _cmbRound.Height));

        var roundFieldLeft = UiLayout.After(lblRound, AppTheme.BodyFont);

        _cmbRound.Location = new Point(roundFieldLeft, groupTop);
        _cmbRound.Width = Math.Max(160, column - Inset - roundFieldLeft);

        _lblRoundHint.AutoSize = true;
        _lblRoundHint.MaximumSize = new Size(inner, 0);
        _lblRoundHint.Font = AppTheme.SmallFont;
        _lblRoundHint.ForeColor = AppTheme.TextMuted;
        _lblRoundHint.Location = new Point(Inset, UiLayout.Below(_cmbRound, UiLayout.HintGap));

        var lblDrivers = new Label
        {
            Text = "Check driver profiles to include (uses their real names - exercises real announcer/rivalry behavior):",
            AutoSize = true,
            MaximumSize = new Size(inner, 0),
            Location = new Point(Inset, UiLayout.Below(_lblRoundHint, UiLayout.SectionGap)),
        };

        _clbDrivers.Font = AppTheme.BodyFont;
        _clbDrivers.CheckOnClick = true;

        // Off, or the list rounds its height down to a whole number of rows
        // and leaves a gap underneath that nothing in the layout explains.
        _clbDrivers.IntegralHeight = false;

        _clbDrivers.Location = new Point(Inset, UiLayout.Below(lblDrivers, UiLayout.RowGap));
        _clbDrivers.Size = new Size(inner, AppTheme.BodyFont.Height * 8);
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

        var lblPlaceholder = new Label
        {
            Text = "Plus this many made-up placeholder racers:",
            AutoSize = true,
            MaximumSize = new Size(inner, 0),
            Location = new Point(Inset, UiLayout.Below(_clbDrivers, UiLayout.RowGap)),
        };

        _numPlaceholderCount.Font = AppTheme.BodyFont;
        _numPlaceholderCount.Width = UiLayout.NumericWidth;
        _numPlaceholderCount.Minimum = 0;
        _numPlaceholderCount.Maximum = 8;
        _numPlaceholderCount.Value = Math.Clamp(_settings.LastDemoPlaceholderCount, (int)_numPlaceholderCount.Minimum, (int)_numPlaceholderCount.Maximum);
        _numPlaceholderCount.ValueChanged += (_, _) =>
        {
            _settings.LastDemoPlaceholderCount = (int)_numPlaceholderCount.Value;
            _settings.Save();
        };

        // Under its caption rather than beside it: the caption wraps at this
        // column width, and a spinner placed after a two-line label sits
        // level with the wrong line of it.
        _numPlaceholderCount.Location = new Point(Inset, UiLayout.Below(lblPlaceholder, UiLayout.HintGap));

        group.Controls.AddRange(new Control[]
        {
            lblRound, _cmbRound, _lblRoundHint, lblDrivers, _clbDrivers, lblPlaceholder, _numPlaceholderCount,
        });

        group.Height = UiLayout.Below(_numPlaceholderCount, Inset);

        return group;
    }

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

    private void BtnStart_Click(object? sender, EventArgs e)
    {
        if (ChosenRound() is { } round)
        {
            RunRound(round);
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

        StartSimulation(realTransponders, placeholderCount);
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
            + $"Ghost cars run only if ticked above.{Environment.NewLine}");

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
