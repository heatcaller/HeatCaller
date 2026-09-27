namespace HeatCaller;

/// <summary>
/// Race Control's layout: building the page (BuildLayout), the right-hand
/// column, resizing, the clock display and the track picker.
///
/// Moved here unchanged from RaceControlControl.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>
    /// Puts the clock column where it belongs, against the width the page
    /// actually has right now.
    ///
    /// This used to be done once in the constructor, from
    /// Math.Max(700, Width - PageRight), with every control in the column
    /// anchored to the right afterwards. Both halves of that are wrong.
    /// Width in the constructor is the UserControl's default, not the width
    /// this page ends up with - it has no parent yet - so the floor of 700
    /// is what actually decided the layout, and then anchoring preserved a
    /// distance from a right edge that never existed. The column came out
    /// hard against the window edge with no margin at all, and the stat
    /// tiles ended up on a different left edge from the clock and buttons
    /// above them, which is the part that shows.
    ///
    /// Anchors are gone with it. Anchoring and explicit placement are two
    /// authorities on the same coordinate, and the one that ran last won -
    /// which is exactly the kind of disagreement that makes a column look
    /// hand-assembled.
    /// </summary>
    private void LayoutRightColumn()
    {
        // The clock and the stat strip are built partway down the
        // constructor. A size change before that point would arrive here
        // with them still unset, which is a crash on startup rather than a
        // layout glitch.
        if (!_rightColumnBuilt)
        {
            return;
        }

        // ClientSize, not Width: with a scrollbar showing, Width counts the
        // strip nothing can be placed in, and the column would sit under
        // it.
        //
        // The floor is only for the window that has not been given its real
        // width yet - a page laid out at its default 150px would stack this
        // column on top of the Race Control group beside it, and the repair
        // pass would then spend its budget shoving them apart. As soon as
        // there is a real width, the real width wins.
        var right = Math.Max(700, ClientSize.Width - UiLayout.PageRight);
        var left = right - ClockWidth;

        // The bar first, spanning the full width, with its two buttons
        // parked in its right end and the text told where to stop.
        _nextUpBar.Location = new Point(UiLayout.PageLeft, 8);
        _nextUpBar.Width = right - UiLayout.PageLeft;

        var buttonY = (_nextUpBar.Height - _btnStartNext.Height) / 2;

        _btnReseed.Location = new Point(_nextUpBar.Width - _btnReseed.Width - 8, buttonY);
        _btnStartNext.Location = new Point(_btnReseed.Left - UiLayout.ButtonGap - _btnStartNext.Width, buttonY);
        _nextUpBar.ReservedRight = _nextUpBar.Width - _btnStartNext.Left + 8;

        var contentTop = _nextUpBar.Bottom + 10;

        _clock.Location = new Point(left, contentTop);
        _btnQuickRace.Location = new Point(left, _clock.Bottom + 8);
        _btnBigScreen.Location = new Point(left, _btnQuickRace.Bottom + 8);
        _btnAnnouncer.Location = new Point(left, _btnBigScreen.Bottom + 8);
        _btnProjection.Location = new Point(left, _btnAnnouncer.Bottom + 8);

        // Last of the buttons: it is run once, before the first race, and
        // then not again all night - the ones above it are pressed during
        // the racing. The stat strip under it is a readout, not a button.
        _btnReadiness.Location = new Point(left, _btnProjection.Bottom + 8);
        _statStrip.Location = new Point(left, _btnReadiness.Bottom + 8);

        // The two groups share the space to the LEFT of the clock column:
        // flags on top, and the board taking everything else down to the
        // bottom edge. This is what makes the leaderboard the biggest thing
        // on the page at any window size, which is what a leaderboard is
        // for.
        if (_grpRaceControl is { } flags && _grpLeaderboard is { } board)
        {
            var innerRight = left - UiLayout.RowGap;

            flags.Location = new Point(UiLayout.PageLeft, contentTop);
            flags.Width = Math.Max(600, innerRight - flags.Left);

            board.Location = new Point(flags.Left, flags.Bottom + 10);
            board.Size = new Size(
                Math.Max(600, innerRight - board.Left),
                Math.Max(180, ClientSize.Height - board.Top - 12));

            // And the board's INSIDES against those bounds, in the same
            // breath. The docked strip has already taken the new width by
            // the time Size returns; this rewraps its buttons to it and
            // hands the grid everything above the strip.
            _reflowToolbar?.Invoke();
        }
    }

    /// <summary>The clock column is placed by hand, so it has to be re-placed whenever the page changes width.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutRightColumn();
    }

    private void BuildLayout()
    {
        // --- Session row --------------------------------------------------
        // Track and label on one aligned row, with the clock pinned right.
        // These used to sit at four different left edges with the group
        // below starting at a fifth.
        // Track Layout and Session Name live in Session Setup above this
        // page now. These controls stay - track-record checks read
        // _cmbTrackLayout - but they're hidden and driven from there, so
        // there's one place to set each value rather than two.
        var lblTrack = new Label { Text = "Track Layout:", Visible = false };

        _cmbTrackLayout.Visible = false;
        _cmbTrackLayout.Width = 200;
        _cmbTrackLayout.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrackLayout.SelectedIndexChanged += (_, _) =>
        {
            var selected = _cmbTrackLayout.SelectedItem as string;

            if (selected is not null)
            {
                TrackLayoutChanged?.Invoke(selected);
            }

            _settings.LastTrackLayoutName = selected ?? string.Empty;
            _settings.Save();
        };

        var lblLabel = new Label { Text = "Session Name:", Visible = false };

        _txtSessionLabel.Visible = false;
        _txtSessionLabel.Width = 220;
        _txtSessionLabel.PlaceholderText = "optional, e.g. \"Friday Main\"";
        _txtSessionLabel.TextChanged += (_, _) => SessionLabelChanged?.Invoke(_txtSessionLabel.Text);

        // The clock column: sizes and behaviour here, positions in
        // LayoutRightColumn, which runs again every time the page changes
        // width. Nothing in this block sets a Location or an Anchor - that
        // split is the whole point, and putting one back here is how the
        // column came to disagree with itself.
        const int clockWidth = ClockWidth;

        // Still wanted for the group box below, which is anchored on both
        // sides and so only needs a starting width. Note the floor of 700:
        // during a constructor this control has no parent and Width is the
        // UserControl default, which is why it is not to be trusted as a
        // right edge for anything placed by hand.
        var contentRight = Math.Max(700, Width - UiLayout.PageRight);

        // --- The Next Up bar ---
        //
        // Lives INSIDE the bar as children, so painting order is never a
        // question - a child always draws over its parent.
        // Sized for the WIDEST caption it ever wears, because swapping
        // text must not change the bar's geometry. It says "Race Running"
        // while a session is under way, and when the programme owes
        // something before the next race it wears that step's own caption
        // instead - see UpdateNextUpBar.
        _btnStartNext.Font = AppTheme.BodyFont;

        var startNextWide = 0;

        // Taken from ProgramCaptions rather than written out again here. The
        // same four strings used to be literals in both files, and renaming
        // one of them left this list behind - which does not break anything
        // loudly, it just clips the new caption on the one screen the
        // director watches all night.
        foreach (var caption in ProgramCaptions.NextUpBarWidest)
        {
            _btnStartNext.Text = caption;
            startNextWide = Math.Max(startNextWide, UiLayout.WidthFor(_btnStartNext));
        }

        _btnStartNext.Text = ProgramCaptions.StartNext;
        _btnStartNext.Size = new Size(startNextWide, UiLayout.ButtonHeight);
        _btnStartNext.Click += (_, _) =>
        {
            if (_race.State is not (RaceState.Idle or RaceState.Finished))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [day] Start Next: a session is already under way - finish or reset it first.");
                return;
            }

            // Something is owed before the next race can go out - see
            // UpdateNextUpBar. The button is captioned for it, so it does
            // it.
            if (_barStep is { } step)
            {
                RunProgramStepRequested?.Invoke(step);
                return;
            }

            if (NextRound() is { } next)
            {
                NextRoundStartRequested?.Invoke(next);
            }
            else
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [day] Start Next: nothing left to run - the program is complete. Quick Race still works.");
            }
        };

        // Was "Re-seed Remaining...". It rebuilds the rosters of the heats
        // that have not run yet from who is checked in now, which is what
        // the caption says and what "re-seed" did not.
        _btnReseed.Text = "Redo Remaining Heats...";
        _btnReseed.Font = AppTheme.BodyFont;
        _btnReseed.Size = new Size(UiLayout.WidthFor(_btnReseed), UiLayout.ButtonHeight);
        _btnReseed.Click += (_, _) => ReseedRemaining();

        _nextUpBar.Controls.Add(_btnStartNext);
        _nextUpBar.Controls.Add(_btnReseed);

        _clock.Size = new Size(clockWidth, 200);

        // The escape hatch. Whoever is on the track right now, the app
        // default format, no event, no program, no check-in - staged,
        // counted down and green in one press. A structured race day is
        // only acceptable when "just go" is always one button away.
        _btnQuickRace.Text = "Quick Race";
        _btnQuickRace.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);

        // A delegate, like the flag actions: F8 used to reach this through
        // PerformClick, which does nothing while the button is not visible -
        // so in Race Mode showing any page but this one, F8 was silently
        // ignored while F1-F7 worked.
        _actQuickRace = () =>
        {
            // Pressed again during its own warm-up: skip the rest of it and
            // go straight on to the start.
            if (SkipWarmUpIfRunning())
            {
                return;
            }

            if (_race.State is not (RaceState.Idle or RaceState.Finished))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Quick Race: a session is already under way - finish or reset it first.");
                return;
            }

            // MainForm drops the pickers back to Race + freeform, so the
            // session neither runs under some selected round's format nor
            // records itself against that round.
            QuickRaceSetupRequested?.Invoke();

            _txtSessionLabel.Text = $"Quick Race {DateTime.Now:HH:mm}";

            ApplyEffectiveFormat();
            ApplyJumpStartRules();

            // A warm-up in the app default format runs first. The format is
            // applied again at the end of it, in case anything moved on.
            if (TryBeginWarmUp(() =>
                {
                    ApplyEffectiveFormat();
                    ApplyJumpStartRules();
                    _ = RunAutoStartSequenceAsync();
                }))
            {
                return;
            }

            _ = RunAutoStartSequenceAsync();
        };
        _btnQuickRace.Click += (_, _) => _actQuickRace?.Invoke();

        _btnBigScreen.Text = "Open Big Screen";
        _btnBigScreen.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);

        // At the foot of the button column - see LayoutRightColumn. It is
        // run once before racing starts and then left alone, so it gives up
        // the higher slots to the buttons pressed during the night.
        _btnReadiness.Text = "Pre-race check...";
        _btnReadiness.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);
        _btnReadiness.Click += (_, _) => ShowReadinessCheck();

        // Fills the dead space that sat between the Big Screen button and
        // the leaderboard, and puts the leader's lap count and the session
        // best where they can be read without hunting for a row.
        // Opens the announcer's own window; there is a button on it that
        // brings this page back to the front.
        //
        // It was a tab under Voice, and this button switched the whole app
        // over to it. During a race these are the only two screens anybody
        // is on, and one replacing the other is the wrong shape: whoever is
        // on the microphone wants the running order in front of them WHILE
        // the director works the flags. A window can sit beside this one or
        // own a second monitor. A tab could do neither, and every glance at
        // the announcer's notes took Race Control off the screen.
        _btnAnnouncer.Text = "Open Announcer Screen";
        _btnAnnouncer.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);
        _btnAnnouncer.Click += (_, _) => AnnouncerRequested?.Invoke();

        _btnProjection.Text = "Qualifying Projection";
        _btnProjection.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);
        _btnProjection.Click += (_, _) => ToggleProjectionWindow();

        _statStrip.Size = new Size(clockWidth, 50);

        // Placed, rather than anchored. See LayoutRightColumn.
        _rightColumnBuilt = true;
        LayoutRightColumn();

        // (continues below - see LayoutRightColumn for where the five
        // controls above are actually put.)

        // Restored: my layout rewrite dropped this handler entirely, so the
        // button did nothing and TriggerOpenBigScreen - used by the remote
        // and the day schedule - silently no-opped. The compiler only
        // warned about the unassigned field, not the missing behaviour.
        _actBigScreen = () => new BigScreenForm(_race, _announcer, _profiles, _daySchedule, _sponsors, _settings, _projector).Show();
        _btnBigScreen.Click += (_, _) => _actBigScreen();

        var grpRaceControl = new GroupBox
        {
            Text = "Race Control",
            Location = new Point(UiLayout.PageLeft, UiLayout.PageTop),

            // A starting width only - no anchor. The layout pass sets the
            // real width against the page's real size on every resize, and
            // anchoring as well would make two authorities on the same
            // edge; whichever ran last would win. That lesson is written on
            // the clock column, and it applies here identically.
            Size = new Size(Math.Max(600, contentRight - clockWidth - 36), 292),
        };

        // The flag row is measured, not placed.
        //
        // These were seven buttons pinned to 86x30 at x = 12, 104, 196 and
        // so on. "Green (Start)" does not fit 86px at this text size, so
        // every launch the repair pass widened three of them and shoved the
        // four after them right - twenty-odd adjustments on the page the
        // app spends its life on, redone from scratch each time the window
        // opened. Measuring the row here means there is nothing left to
        // repair.
        //
        // Laid out after the captions are set, at the bottom of this block.
        _btnStage.Text = "Stage";
        _actStage = (confirmedOverRunningRace, restoreHostOnUndo) =>
        {
            // Before anything else - Stage clears the standings, and the
            // shared session is holding a practice's laps.
            if (RefuseIfPracticeRunning("stage a race"))
            {
                return;
            }

            // Stage during a warm-up skips the rest of it - the warm-up's
            // own continuation then stages (or runs Quick Race's sequence).
            if (SkipWarmUpIfRunning())
            {
                return;
            }

            // Between races, a format with a warm-up opens it first and
            // stages when it runs out. Not when this Stage IS the end of a
            // warm-up, and never over a race that is staged or running.
            if (!_warmUpJustFinished
                && TryBeginWarmUp(() => _actStage?.Invoke(false, restoreHostOnUndo)))
            {
                return;
            }

            // Stage over a running race wipes every lap it has counted, and
            // F1 is one key away from the flag keys - so it is asked about,
            // and can be undone, rather than simply done.
            var abandoningRace = _race.State == RaceState.Racing;

            if (abandoningRace && !confirmedOverRunningRace && !ConfirmAbandonRunningRace("stage a new race"))
            {
                return;
            }

            // A demo on the session is ended rather than abandoned, and not
            // put back by Undo: it is a rehearsal, and a demo race restored
            // without the simulator driving it would sit there as a "real"
            // race with fake drivers in it.
            var tookOverDemo = EndDemoForTakeover("stage a race");

            // Filled in once the new race is staged - the undo reads it
            // then, not now.
            var generationAfterStage = -1;

            if (abandoningRace && !tookOverDemo)
            {
                CaptureAbandonedRaceUndo("Stage over a running race", () => generationAfterStage, restoreHostOnUndo);
            }

            CancelAutoStart();

            // The format for the race about to run, written now. Nothing
            // else is guaranteed to have written it since the last race or
            // practice - see ApplyEffectiveFormat.
            ApplyEffectiveFormat(startingNewRace: true);
            ApplyJumpStartRules();
            _race.Stage();
            generationAfterStage = _race.SessionGeneration;

            // After Stage, so it knows which race it is for - it is spoken
            // only if this race is still staging when the model answers.
            var introComing = _race.State == RaceState.Staging && SpeakRoundIntro(generationAfterStage);

            // Chain onward once the staging call - and the round intro, when
            // one is being written - has finished speaking. Driven by the
            // audio rather than a timer, so the sequence always follows the
            // announcements instead of talking over them or leaving dead air.
            if (_fmt.AutoAdvance)
            {
                ChainAfterAudio(RaceState.Staging, () => _actCountdown?.Invoke(false),
                    introComing ? StagingWaitWithIntro : null);
            }
        };
        _btnStage.Click += (_, _) => _actStage(false, null);

        _btnCountdown.Text = "Countdown";
        _actCountdown = confirmedOverRunningRace =>
        {
            if (RefuseIfPracticeRunning("start a countdown"))
            {
                return;
            }

            // A countdown over a running race used to go straight ahead:
            // every car crossing the line was then booked as a jump start,
            // and the green that followed wiped the race. Asked about, and
            // undoable until that green, exactly as Stage is.
            var abandoningRace = _race.State == RaceState.Racing;

            if (abandoningRace && !confirmedOverRunningRace && !ConfirmAbandonRunningRace("start a new countdown"))
            {
                return;
            }

            // A demo is ended, not abandoned - see Stage.
            var tookOverDemo = EndDemoForTakeover("start a countdown");

            // A countdown does not start a new session - the green does -
            // so this is the generation of the race being abandoned, and
            // the undo holds until the new race goes green.
            var generationAfterCountdown = _race.SessionGeneration;

            if (abandoningRace && !tookOverDemo)
            {
                CaptureAbandonedRaceUndo("Countdown over a running race", () => generationAfterCountdown);
            }

            CancelAutoStart();
            ApplyEffectiveFormat(startingNewRace: true);

            // A countdown that did not come through Stage gets the class's
            // jump-start rules here, since Stage is where they were applied
            // and it was skipped - otherwise the race was judged on the
            // LAST race's rules. So does one taken over from a demo, whose
            // staging never applied any. A countdown restarted from a
            // countdown keeps what the first one set.
            if (tookOverDemo || _race.State is not (RaceState.Staging or RaceState.Countdown))
            {
                ApplyJumpStartRules();
            }

            _race.StartCountdown();

            if (_fmt.AutoAdvance)
            {
                ChainAfterAudio(RaceState.Countdown, () => _actGreen?.Invoke());
            }
        };
        _btnCountdown.Click += (_, _) => _actCountdown(false);

        _btnGreen.Text = "Green (Start)";
        _actGreen = () =>
        {
            if (RefuseIfPracticeRunning("start a race"))
            {
                return;
            }

            // A race already under green has nothing to start or resume, so
            // the press changes nothing at all. It used to throw the green
            // again, and the start was called out a second time in the middle
            // of the race - see GreenFlag in RaceControlSession. Checked before
            // anything is touched, and said only in the log: nothing needs
            // doing, and the phone remote is told it is already green.
            if (_race.State == RaceState.Racing && _race.Flag == FlagState.Green)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Green pressed - the race is already green, so nothing changed.");
                return;
            }

            // Nor can a green take back the checkered on a race's last lap.
            // Refused, with the reason, before anything is touched.
            if (GreenRefusal(_race.State, _race.Flag) is { } noGreen)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Green not thrown. {noGreen}");
                NoteRemoteRefusal(noGreen);
                return;
            }

            CancelAutoStart();

            var resuming = _race.State == RaceState.Racing;

            // A green over a demo's staging or countdown starts a real race
            // in its place, so the demo is ended first. Over a demo that is
            // already racing it is a restart after a caution, and the demo
            // carries on.
            var tookOverDemo = !resuming && EndDemoForTakeover("start a race");

            // A first green - from idle, staging or the countdown - starts a
            // race, so the race gets its format now. A restart after a
            // caution keeps the rules the race started with.
            if (!resuming)
            {
                ApplyEffectiveFormat(startingNewRace: true);

                // As for a countdown: a green with no staging (or taken over
                // from a demo's) never had the class's jump-start rules
                // applied, and would otherwise run on the last race's.
                if (tookOverDemo || _race.State is not (RaceState.Staging or RaceState.Countdown))
                {
                    ApplyJumpStartRules();
                }
            }

            // Straightforward now: the audio wait lives in the session gate
            // that every start path shares, so there's nothing to await
            // here. This used to wrap a Task.Run that no longer awaited
            // anything, which is what the compiler was warning about.
            StartGreenNow(resuming);
        };
        _btnGreen.Click += (_, _) => _actGreen();

        _btnYellow.Text = "Yellow";
        _actYellow = () => { CancelAutoStart(); _race.YellowFlag(); };
        _btnYellow.Click += (_, _) => _actYellow();

        _btnRed.Text = "Red";
        _actRed = () => { CancelAutoStart(); _race.RedFlag(); };
        _btnRed.Click += (_, _) => _actRed();

        _btnCheckered.Text = "Checkered";
        _actCheckered = () =>
        {
            // Practice runs on this session in the Racing state, and a
            // checkered from here finished it as a race - leaving the
            // Practice page "running" over a finished session, with its own
            // save and teardown never run.
            if (RefuseIfPracticeRunning("throw the checkered flag"))
            {
                return;
            }

            // Only a race that is running can be finished. Nothing staged, a
            // start still on its way to the green and a race that is already
            // over are all refused, and left exactly as they were - see
            // RefuseCheckeredWithoutARace.
            if (RefuseCheckeredWithoutARace())
            {
                return;
            }

            CancelAutoStart();
            _race.CheckeredFlag();
        };
        _btnCheckered.Click += (_, _) => _actCheckered();

        _btnReset.Text = "Reset";
        _actReset = () =>
        {
            // Refused during practice, as Stage is. A reset from here put the
            // session back to Idle under a practice the Practice page still
            // showed as running, with no lap counted until it was stopped
            // and started again - and practice has its own Stop, which saves
            // and tidies up, and its own Clear for starting the figures over.
            if (RefuseIfPracticeRunning("reset the race"))
            {
                return;
            }

            // A demo is ended, and the reset has nothing of it to undo - a
            // demo race put back without the simulator driving it would sit
            // on the board as a "real" race with fake drivers in it.
            var tookOverDemo = EndDemoForTakeover("reset");

            // Reset now also unbinds the round and clears the session name.
            // That was a second button - "Reset Round", in the session row a
            // few inches from this one - whose body was this one's plus
            // those two lines, and which took an undo entry of its own.
            // Null when there was no round and no name to clear, so an
            // already-clear session does not gain an undo step that undoes
            // nothing.
            var restoreBinding = ClearRoundBinding?.Invoke();

            // Snapshot first - Reset clears the live standings, so this has
            // to happen before anything is torn down. Only worth capturing
            // if there's actually something to lose.
            //
            // The generation is filled in once the reset has happened - the
            // undo action reads it then, not now.
            var generationAfterReset = -1;

            if (!tookOverDemo && (_race.GetOrderedStandings().Count > 0 || _race.State != RaceState.Idle))
            {
                var snapshot = _race.CreateSnapshot();
                var sessionStart = _sessionStartedAtUtc;
                var prompted = _promptedTransponders.ToList();

                _undo.Capture("Reset race", () =>
                {
                    RefuseStaleResetUndo(generationAfterReset);

                    _suppressFlagAnnouncements = true;
                    try
                    {
                        _race.RestoreSnapshot(snapshot);
                    }
                    finally
                    {
                        _suppressFlagAnnouncements = false;
                    }

                    _sessionStartedAtUtc = sessionStart;
                    _promptedTransponders.Clear();
                    foreach (var t in prompted)
                    {
                        _promptedTransponders.Add(t);
                    }
                    _previousFlag = _race.Flag;

                    // The reset cleared the journal; the race is back, so it
                    // is written again now rather than at its next crossing.
                    WriteJournal(force: true);

                    RenderLeaderboard();
                    UpdateClockDisplay();

                    // Last, so the round and the name come back with the
                    // race rather than as a separate press of Undo.
                    restoreBinding?.Invoke();
                });
            }
            else if (restoreBinding is not null)
            {
                // Nothing of the race to put back - it was already idle and
                // empty - but the round binding still is.
                _undo.Capture("Reset race", restoreBinding);
            }

            CancelAutoStart();
            CancelWarmUp();
            _race.Reset();
            generationAfterReset = _race.SessionGeneration;

            // Reset is a decision, not an accident - there is nothing left
            // to recover, and leaving the journal behind would offer the
            // race back on the next launch.
            _journal.Clear();

            _toldAboutRecordPace.Clear();
            _promptedTransponders.Clear();
            _unnamedTransponders.Clear();
            _previousFlag = FlagState.None;
            _currentRaceIsDemo = false;

            // The reset race's start goes with it (Undo puts it back). Left
            // here, a start that does not stamp its own - a demo's green -
            // kept it through the "??=" at Racing, and was saved as having
            // begun when the abandoned race did.
            _sessionStartedAtUtc = null;
            _announcer.StopAllPendingAudio();
            _dgvLeaderboard.Rows.Clear();
            UpdateClockDisplay(force: true);
        };
        _btnReset.Click += (_, _) => _actReset();

        var shortcutTips = new ToolTip();
        AppTheme.StyleToolTip(shortcutTips, keep: true);
        shortcutTips.SetToolTip(_btnStage, "Shortcut: F1 (while this page is active)");
        shortcutTips.SetToolTip(_btnCountdown, "Shortcut: F2");
        shortcutTips.SetToolTip(_btnGreen, "Shortcut: F3");
        shortcutTips.SetToolTip(_btnYellow, "Shortcut: F4");
        shortcutTips.SetToolTip(_btnRed, "Shortcut: F5");
        shortcutTips.SetToolTip(_btnCheckered, "Shortcut: F6");
        shortcutTips.SetToolTip(_btnReset, "Shortcut: F7 (asks first if a race is running)");
        shortcutTips.SetToolTip(_btnQuickRace, "Shortcut: F8");
        shortcutTips.SetToolTip(_btnBigScreen, "Shortcut: B");
        shortcutTips.SetToolTip(_btnReadiness, "Shortcut: R");
        shortcutTips.SetToolTip(_btnAnnouncer, "Shortcut: A  -  press ? for every shortcut");

        var lblStateCaption = new Label { Text = "State:", Location = new Point(12, 72), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _lblRaceState.Text = "Idle";
        _lblRaceState.Location = new Point(96, 72);
        _lblRaceState.AutoSize = true;

        var lblFlagCaption = new Label { Text = "Flag:", Location = new Point(200, 72), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _lblFlag.Text = "None";
        _lblFlag.ForeColor = AppTheme.TextMuted;
        _lblFlag.Location = new Point(246, 72);
        _lblFlag.AutoSize = true;

        // --- The race format chip ----------------------------------------
        //
        // Twelve setup knobs used to fill this group - limits, start type,
        // stagger, streak rules, the auto-start timings. They are the race
        // FORMAT, they live on the class now (overridable per round, app
        // default underneath - see RaceFormat), and on race day this page
        // is for RUNNING races, not for re-dialling the countdown between
        // heats. What remains is a chip saying which format is in force
        // and one button that edits it at its source.

        // Kept so anything still calling TriggerAutoStart keeps working -
        // the remote control and the day schedule both use it.
        //
        // Straight to Stage, which applies the jump-start rules itself -
        // AFTER its checks. Applying them here first changed the rules of a
        // race that was still running, even when the person then chose not
        // to abandon it.
        _actAutoStart = (confirmedOverRunningRace, restoreHostOnUndo) => _actStage?.Invoke(confirmedOverRunningRace, restoreHostOnUndo);

        // One row, left to right, each button as wide as its own caption.
        // A tight row rather than the standard one: seven buttons at the
        // usual minimum width would come out wider than the group holding
        // them.
        var flagRowRight = UiLayout.TightButtonRow(
            12, 26, _btnStage, _btnCountdown, _btnGreen, _btnYellow, _btnRed, _btnCheckered, _btnReset);

        // And if the captions still need more room than the group has - a
        // longer language, a bigger text size - the group grows rather than
        // clipping the last flag off the end.
        grpRaceControl.Width = Math.Max(grpRaceControl.Width, flagRowRight + 12);

        // Attached after every Click above, so the action runs and then
        // focus moves off. A flag button that keeps focus keeps answering
        // to Space and Enter, which on this row means a stray keypress can
        // re-start a race that is already running - and the focus ring it
        // draws over a flag colour looks like a fault besides.
        AppTheme.ReleaseFocusAfterClick(
            _btnStage, _btnCountdown, _btnGreen, _btnYellow, _btnRed, _btnCheckered, _btnReset);

        _lblFormat.AutoSize = true;
        _lblFormat.Font = AppTheme.BodyFont;
        _lblFormat.ForeColor = AppTheme.TextMuted;
        _lblFormat.MaximumSize = new Size(560, 0);
        _lblFormat.Location = new Point(12, 108);

        var formatTip = new ToolTip();
        AppTheme.StyleToolTip(formatTip, keep: true);
        formatTip.SetToolTip(_lblFormat,
            "Which race format is in force for what's about to run: the round's override if it has one, "
            + "else its class's format, else the app default. Class formats are set on the Classes page.");

        _btnFormat.Text = "Race Format...";
        _btnFormat.Font = AppTheme.BodyFont;
        _btnFormat.Size = new Size(UiLayout.WidthFor(_btnFormat), UiLayout.ButtonHeight);
        _btnFormat.Location = new Point(12, 148);
        _btnFormat.Click += (_, _) => EditFormat();

        grpRaceControl.Height = _btnFormat.Bottom + 16;

        grpRaceControl.Controls.AddRange(new Control[]
        {
            _btnStage, _btnCountdown, _btnGreen, _btnYellow, _btnRed, _btnCheckered, _btnReset,
            lblStateCaption, _lblRaceState, lblFlagCaption, _lblFlag,
            _lblFormat, _btnFormat,
        });

        ApplyEffectiveFormat();

        // BESIDE the clock column, not below it. The board used to run the
        // full width of the page, which meant it could only start below the
        // clock, Quick Race, the buttons and the stat tiles - half a screen
        // of dead space above it, and a board that never dominated the page
        // it is the whole point of. Now it starts directly under the flags
        // and stops just left of the clock column, and the layout pass
        // gives it every remaining pixel down to the bottom edge - see
        // LayoutRightColumn, which owns all of this geometry against the
        // page's real width. The placeholder numbers here are overwritten
        // by that pass before the page is ever seen.
        var leaderboardTop = grpRaceControl.Bottom + 10;

        var grpLeaderboard = new GroupBox
        {
            Text = "Leaderboard (double-click a driver for their lap breakdown - right-click for names, penalties and corrections)",
            Location = new Point(12, leaderboardTop),
            Size = new Size(876, 320),
        };
        // The correction toolbar gets a strip of its own, pinned to the
        // bottom of the group, and the board gets everything above it.
        //
        // It used to be the other way about: the buttons were placed at a
        // remembered y inside the group, and the board was given a fixed
        // height and anchored to the TOP only - because anchoring it to the
        // bottom as well made it grow straight down over them. That kept
        // the buttons visible at the price of a board that never got any
        // taller, so on a big screen there was a band of empty group box
        // under a board still scrolling at eight drivers.
        //
        // A strip of its own fixes both. The board can be anchored on all
        // four sides, because the space it grows into now stops where the
        // strip begins.
        // PLACED, not docked - see FlowToolbar, which puts both this strip
        // and the board inside the group's display rectangle in one pass.
        var toolbar = new Panel { Height = 1, BackColor = AppTheme.Surface, Tag = AppTheme.KeepBackColor };

        // Neither placed nor anchored here: FlowToolbar sets this board's
        // whole rectangle, and the strip's, from the group's display
        // rectangle on every pass. Anything written here would be
        // overwritten a few lines later, which is worse than nothing
        // because it reads as though it meant something.
        _dgvLeaderboard.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _dgvLeaderboard.ReadOnly = true;

        // No pop-ups on the board at all - not even the notes it used to put
        // on a cell. Hovering a live leaderboard mid-race kept putting boxes
        // over the rows being read. What those notes said is on the row
        // itself: a jump start is tinted red, a car waiting to be released is
        // greyed. Attach is kept only for what it switches OFF - the grid's
        // own Windows pop-up for cut-off text.
        GridTips.Attach(_dgvLeaderboard);
        _dgvLeaderboard.AllowUserToAddRows = false;
        _dgvLeaderboard.AllowUserToDeleteRows = false;
        _dgvLeaderboard.RowHeadersVisible = false;

        // Row heights are not the user's to drag. The board sets them
        // itself - a lap strip is measured to its own wrapped text, and a
        // driver row is sized to the theme's font - so a hand-dragged
        // height is only ever wrong by the next refresh. Both levers are
        // turned off: the rows themselves, and the column header's bottom
        // edge, which resizes on drag by default and is the one that is
        // actually reachable with the row headers hidden.
        _dgvLeaderboard.AllowUserToResizeRows = false;
        _dgvLeaderboard.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _dgvLeaderboard.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _dgvLeaderboard.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _dgvLeaderboard.CellDoubleClick += DgvLeaderboard_CellDoubleClick;

        // Right-click acts on the row under the pointer, not on whatever
        // happened to be selected before - clicking a row and getting the
        // previous driver's menu is how the wrong car gets a penalty.
        _dgvLeaderboard.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _dgvLeaderboard.HitTest(e.X, e.Y);

            if (hit.RowIndex >= 0 && hit.RowIndex < _dgvLeaderboard.Rows.Count)
            {
                _dgvLeaderboard.CurrentCell = _dgvLeaderboard.Rows[hit.RowIndex].Cells[Math.Max(0, hit.ColumnIndex)];
            }
        };

        _dgvLeaderboard.CellPainting += LeaderboardDetailPaint;
        _dgvLeaderboard.ContextMenuStrip = BuildLeaderboardMenu();
        RebuildLeaderboardColumns();
        grpLeaderboard.Controls.Add(_dgvLeaderboard);

        // --- Manual corrections toolbar ---
        var lblFix = new Label { Text = "Selected racer:", AutoSize = true, Font = AppTheme.BoldFont };

        var btnLapPlus = new Button { Text = "+1 Lap" };
        btnLapPlus.Click += (_, _) => AdjustSelected(laps: 1, penaltyMs: 0);

        var btnLapMinus = new Button { Text = "-1 Lap" };
        btnLapMinus.Click += (_, _) => AdjustSelected(laps: -1, penaltyMs: 0);

        var btnPenalty5 = new Button { Text = "+5s Penalty" };
        btnPenalty5.Click += (_, _) => AdjustSelected(laps: 0, penaltyMs: 5000);

        var btnPenalty10 = new Button { Text = "+10s Penalty" };
        btnPenalty10.Click += (_, _) => AdjustSelected(laps: 0, penaltyMs: 10000);

        var btnClearPenalty = new Button { Text = "Clear Penalties" };
        btnClearPenalty.Click += (_, _) => ClearSelectedPenalty();

        // Opens the reasons list under the button - see RaceControlWarnings.
        // W does the same from the keyboard, and the tooltip below says so,
        // as it does for every other key on this page. This was the one
        // button carrying its key in its caption as well - "(W)" - which
        // no other did, and which said less than the tooltip already does.
        var btnWarning = new Button { Text = "\u26A0 Warning" };
        btnWarning.Click += (_, _) => ShowWarningMenu(btnWarning, new Point(0, btnWarning.Height));

        // The keys for the selected driver - see ShortcutCatalog.
        var driverKeyTips = new ToolTip();
        AppTheme.StyleToolTip(driverKeyTips, keep: true);
        driverKeyTips.SetToolTip(btnLapPlus, "Shortcut: +");
        driverKeyTips.SetToolTip(btnLapMinus, "Shortcut: -");
        driverKeyTips.SetToolTip(btnPenalty5, "Shortcut: P");
        driverKeyTips.SetToolTip(btnPenalty10, "Shortcut: Shift+P");
        driverKeyTips.SetToolTip(btnClearPenalty, "Shortcut: C");
        driverKeyTips.SetToolTip(btnWarning, "Shortcut: W, then 1-7 for the reason.  Shift+W takes the last one back.");

        var btnColumns = new Button { Text = "Columns..." };
        btnColumns.Click += (_, _) =>
        {
            using var form = new KpiPickerForm("Race Control", _settings.RaceControlKpis, KpiCatalog.DefaultRaceControl);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                // The picker's Restore Defaults lands here too, so the old
                // list is kept for Undo. This page lives as long as the app.
                var before = _settings.RaceControlKpis.ToList();
                if (!before.SequenceEqual(form.SelectedKeys))
                {
                    _undo.Capture("Change the Race Control columns", () =>
                    {
                        _settings.RaceControlKpis = before.ToList();
                        _settings.Save();

                        if (!IsDisposed)
                        {
                            RebuildLeaderboardColumns();
                            RenderLeaderboard();
                        }
                    });
                }

                _settings.RaceControlKpis = form.SelectedKeys;
                _settings.Save();
                RebuildLeaderboardColumns();
                RenderLeaderboard();
            }
        };

        var lblFixHint = new Label
        {
            Text = "For missed or double-counted passes, on-track penalties and warnings. Press ? for keyboard shortcuts.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // The hint goes on its own line above the buttons rather than after
        // them. It used to sit at x=734 with six buttons in front of it,
        // and since every one of those buttons widens with the font, the
        // hint was being pushed off the right-hand edge of an 876-wide
        // group before anyone had even resized the window.
        lblFixHint.Location = new Point(12, 6);

        // PreferredHeight/PreferredWidth rather than Height/Width: an
        // AutoSize label has not measured its text until it is shown, so
        // reading Height here gives the default 23 and everything placed
        // under it lands too high. Asking for the preferred size forces the
        // measurement now.
        var buttonsTop = lblFixHint.Top + lblFixHint.PreferredHeight + 6;

        // "Selected racer:" sits ABOVE the buttons now instead of in front
        // of the first one.
        //
        // In the row it cost the first line about 110px, which is what made
        // the strip wrap onto two lines - and the second line then started
        // at the margin while the first started past the label, so the two
        // rows of buttons did not line up with each other. Above, every
        // button starts at the same left edge, and the row usually fits on
        // one line again: the label's line costs the height the wrap was
        // costing anyway.
        lblFix.Location = new Point(12, buttonsTop);

        var buttonRowTop = buttonsTop + lblFix.PreferredHeight + 4;

        // Sized here (ButtonRow measures each caption); POSITIONED by the
        // flow below, which re-runs on every toolbar resize.
        UiLayout.ButtonRow(
            12,
            buttonRowTop,
            btnLapPlus, btnLapMinus, btnPenalty5, btnPenalty10, btnClearPenalty, btnWarning, btnColumns);

        toolbar.Controls.AddRange(new Control[]
        {
            lblFixHint, lblFix, btnLapPlus, btnLapMinus, btnPenalty5, btnPenalty10, btnClearPenalty, btnWarning, btnColumns,
        });

        // The row WRAPS. It was laid out once against the old full-width
        // board; now that the board stops at the clock column, a narrower
        // window ran the last buttons straight off the group's right edge.
        // Flowing on resize means the row uses one line when it fits and
        // takes a second when it doesn't - and the board above gives up
        // exactly that row's height, no more.
        var flowItems = new Control[]
        {
            btnLapPlus, btnLapMinus, btnPenalty5, btnPenalty10, btnClearPenalty, btnWarning, btnColumns,
        };

        // Lays the strip out, then puts BOTH it and the board inside the
        // group in one pass.
        //
        // This is the third attempt at this rectangle and the first one
        // that cannot drift. The strip used to be docked to the bottom of
        // the group while the board's height was worked out from the
        // group's height, the board's own Top and the strip's height - the
        // same answer arrived at two different ways, in two places, with a
        // GroupBox's caption reserve (which follows the FONT, not the
        // painted caption) as an unstated third term. Whenever those three
        // disagreed the result was a band of empty group under the strip,
        // or the board painted over the strip's first row.
        //
        // Now there is one rectangle - the group's DisplayRectangle - and
        // the two children are placed from its bottom and its top. They
        // meet in the middle by construction, and the group's own insets
        // are whatever they are rather than something to be guessed at.
        void FlowToolbar()
        {
            var inner = grpLeaderboard.DisplayRectangle;

            // One width for all of them - the widest caption sets it - so
            // the two rows read as columns instead of ragging out to the
            // length of each caption.
            //
            // Re-measured from the captions here rather than taken from the
            // widths already on the buttons: reading those back and keeping
            // the largest would ratchet, so the row could grow with the font
            // but never shrink again.
            var uniform = 0;

            foreach (var item in flowItems)
            {
                if (item is Button measured)
                {
                    uniform = Math.Max(uniform, UiLayout.WidthFor(measured));
                }
            }

            foreach (var item in flowItems)
            {
                if (item is Button sized)
                {
                    sized.Size = new Size(uniform, UiLayout.ButtonHeight);
                }
            }

            var x = 12;
            var y = buttonRowTop;
            var limit = Math.Max(240, inner.Width - 12);

            foreach (var item in flowItems)
            {
                var width = item is Label label ? label.PreferredWidth : item.Width;

                if (x > 12 && x + width > limit)
                {
                    x = 12;
                    y += UiLayout.ButtonHeight + 6;
                }

                item.Location = item is Label fixLabel
                    ? new Point(x, y + Math.Max(0, (UiLayout.ButtonHeight - fixLabel.PreferredHeight) / 2))
                    : new Point(x, y);

                x += width + UiLayout.ButtonGap;
            }

            var needed = y + UiLayout.ButtonHeight + 6;

            // Flush to the bottom of the group, full width of it.
            toolbar.Bounds = new Rectangle(inner.Left, inner.Bottom - needed, inner.Width, needed);

            // And the board takes everything above, meeting the strip with
            // one gap that is stated once.
            _dgvLeaderboard.Bounds = new Rectangle(
                inner.Left,
                inner.Top,
                Math.Max(200, inner.Width),
                Math.Max(120, toolbar.Top - inner.Top - UiLayout.HintGap));
        }

        // No Resize hook. The strip's width is now set by FlowToolbar
        // itself, so a hook on its own Resize would be this method
        // re-entering itself - and the group's resize already runs
        // LayoutRightColumn, which calls this.
        _reflowToolbar = FlowToolbar;

        grpLeaderboard.Controls.Add(toolbar);

        FlowToolbar();

        // Two children placed by hand from one rectangle. The repair pass
        // is told to keep out, because a third opinion on this rectangle is
        // exactly what produced the band of dead space under the buttons.
        LayoutDoctor.LeaveAlone(grpLeaderboard);

        Controls.Add(lblTrack);
        Controls.Add(_cmbTrackLayout);
        Controls.Add(lblLabel);
        Controls.Add(_txtSessionLabel);
        Controls.Add(_clock);
        Controls.Add(_statStrip);
        Controls.Add(_nextUpBar);
        Controls.Add(_btnQuickRace);
        Controls.Add(_btnBigScreen);
        Controls.Add(_btnAnnouncer);
        Controls.Add(_btnProjection);
        Controls.Add(_btnReadiness);
        Controls.Add(grpRaceControl);
        Controls.Add(grpLeaderboard);

        _grpRaceControl = grpRaceControl;
        _grpLeaderboard = grpLeaderboard;

        // Now that both groups exist, the layout pass places them against
        // the real width - and keeps doing so on every resize.
        LayoutRightColumn();
    }

    /// <summary>
    /// Refreshes the big clock's text. Only updates while the race is
    /// actually Racing - it naturally freezes at whatever it last showed
    /// once the race finishes, and force=true (used by Reset) explicitly
    /// snaps it back to zero rather than waiting for the next tick.
    /// </summary>
    private void UpdateClockDisplay(bool force = false)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        // The clock panel reads the session directly, so there's nothing to
        // push into it - it just needs repainting. Keeping the state in one
        // place is what let the elapsed/remaining split collapse into a
        // single display.
        _clock.Invalidate();
        _statStrip.Invalidate();
        UpdateFlagButtonColors(_race.Flag);
    }

    private void RefreshTrackLayoutCombo()
    {
        var previous = _cmbTrackLayout.SelectedItem as string ?? (_settings.LastRaceControlTrackName.Length > 0 ? _settings.LastRaceControlTrackName : null);
        _cmbTrackLayout.Items.Clear();
        _cmbTrackLayout.Items.Add("(none)");
        foreach (var t in _trackLayouts.All)
        {
            _cmbTrackLayout.Items.Add(t.Name);
        }
        var idx = previous is not null ? _cmbTrackLayout.Items.IndexOf(previous) : -1;
        _cmbTrackLayout.SelectedIndex = idx >= 0 ? idx : 0;
    }
}
