namespace HeatCaller;

/// <summary>
/// Race Control's layout: building the page (BuildLayout), the clock
/// display and the track picker.
///
/// Moved here unchanged from RaceControlControl.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
///
/// Rebuilt on the layout pieces (LayoutPieces.cs) on 28 Sep 2026: nothing
/// on the page is placed by hand any more. It used to be - a right-hand
/// column worked out on every resize, a correction strip flowed by hand
/// under the board, the flag row and the state line at fixed pixels - and
/// at a larger text size the flags ran into the state line, the state line
/// into the format chip, and the chip into its button. The page is on
/// LayoutAudit.ConvertedPages now, and the layout test holds it to nothing
/// overlapping or cut off at every size it tries.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>
    /// Puts the host's Session row - the Name, Track and Round pickers,
    /// which MainForm owns - at the top of the page, above the Next Up bar.
    ///
    /// On the page rather than docked above it, as it used to be: in a
    /// strip of its own it had to be told how wide this page's scrollbar
    /// was to keep to the same edges, and it now simply has them.
    /// </summary>
    internal void ShowSessionRow(Control row) => _page.AddFirst(row, gapBelow: UiLayout.RowGap);

    /// <summary>Gives a label a face of its own, and keeps it in step with the text size.</summary>
    private T Wear<T>(T label, Func<Font> face) where T : Label
    {
        label.Font = face();
        _ownFaces.Add((label, face));
        return label;
    }

    /// <summary>
    /// A new text size. The window's font change reaches this page, which
    /// has none of its own, and is the cue - AppTheme's faces have already
    /// been rebuilt at the new size by then.
    /// </summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        foreach (var (label, face) in _ownFaces)
        {
            var font = face();

            if (!Equals(label.Font, font))
            {
                label.Font = font;
            }
        }
    }

    /// <summary>A line of help in the small muted type the app's hints use, wrapping to the room it is given.</summary>
    private Label Hint(string text) =>
        Wear(new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted }, () => AppTheme.SmallFont);

    /// <summary>
    /// The least height the leaderboard's grid can do with: its headings and
    /// three drivers. Below that the page scrolls instead - see FillPage.
    /// Asked whenever it is needed, because the row height follows the text
    /// size (AppTheme.StyleGrid).
    /// </summary>
    private int LeastBoardHeight() =>
        _dgvLeaderboard.ColumnHeadersHeight + 3 * _dgvLeaderboard.RowTemplate.Height;

    private void BuildLayout()
    {
        // --- The hidden track and session-name fields ---
        //
        // Track and Session Name are set on the Session row, MainForm's, at
        // the top of this page. These stay - track-record checks read
        // _cmbTrackLayout - but they're hidden and driven from there, so
        // there's one place to set each value rather than two.
        _cmbTrackLayout.Visible = false;
        _cmbTrackLayout.Width = 200;
        _cmbTrackLayout.DropDownStyle = ComboBoxStyle.DropDownList;

        // Its SelectedIndexChanged handler is wired in the constructor, after
        // the first RefreshTrackLayoutCombo - not here. There used to be one
        // here as well, so every change raised TrackLayoutChanged and saved
        // the settings twice, and this one stored the "(none)" placeholder
        // itself as the last track. Being wired before the first fill, it
        // did so at every start: on a fresh install the first-run wizard
        // then offered "(none)" as the track's name, and accepting it made
        // a layout that every race save reads as "no track".

        _txtSessionLabel.Visible = false;
        _txtSessionLabel.Width = 220;
        _txtSessionLabel.PlaceholderText = "optional, e.g. \"Friday Main\"";
        _txtSessionLabel.TextChanged += (_, _) => SessionLabelChanged?.Invoke(_txtSessionLabel.Text);

        // --- The Next Up bar ---
        //
        // Its two buttons live INSIDE the bar, so painting order is never a
        // question - a child always draws over its parent - and the bar
        // lays them out at its right-hand end and stops its text short of
        // them. See NextUpBar.
        //
        // Start Next is sized for the WIDEST caption it ever wears, because
        // swapping text must not change the bar's geometry. It says "Race
        // Running" while a session is under way, and when the programme owes
        // something before the next race it wears that step's own caption
        // instead - see UpdateNextUpBar.
        //
        // Taken from ProgramCaptions rather than written out again here. The
        // same four strings used to be literals in both files, and renaming
        // one of them left this list behind - which does not break anything
        // loudly, it just clips the new caption on the one screen the
        // director watches all night.
        _btnStartNext.Text = ProgramCaptions.StartNext;
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
        _btnReseed.Click += (_, _) => ReseedRemaining();

        _nextUpBar.AddButton(_btnStartNext, ProgramCaptions.NextUpBarWidest);
        _nextUpBar.AddButton(_btnReseed);

        // --- The clock column ---
        //
        // Down the right-hand side, beside the flags and the board: the
        // clock, the buttons pressed during the night, and the stat tiles.
        // One width for all of them - ClockWidth, or wider when a caption
        // needs it at a larger text size - and the buttons sized to their
        // captions; the column itself is put together with the rest of the
        // page at the end of this method.
        _clock.Height = ClockHeight;

        // The escape hatch. Whoever is on the track right now, the app
        // default format, no event, no program, no check-in - staged,
        // counted down and green in one press. A structured race day is
        // only acceptable when "just go" is always one button away.
        _btnQuickRace.Text = "Quick Race";

        // A delegate, like the flag actions: F8 used to reach this through
        // PerformClick, which does nothing while the button is not visible -
        // so in Race Mode showing any page but this one, F8 was silently
        // ignored while F1-F7 worked.
        _actQuickRace = () =>
        {
            // Pressed again during its own warm-up: skip the rest of it and
            // go straight on to the start.
            //
            // Only its OWN. A warm-up Stage opened ends in Stage's
            // continuation, and skipping it from here staged the heat in
            // the Round box under its own format - no freeform setup, no
            // Quick Race label, no start sequence - so the race was filed
            // against that heat. That warm-up is closed instead, and the
            // Quick Race asked for goes ahead below, straight to its start:
            // the cars have just had their free laps.
            var stageWarmUpClosed = false;

            if (_warmUpCts is not null && _race.IsWarmingUp)
            {
                if (_warmUpOpenedByQuickRace)
                {
                    SkipWarmUpIfRunning();
                    return;
                }

                CancelWarmUp();
                stageWarmUpClosed = true;
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Quick Race pressed during the heat's warm-up - warm-up closed, running a Quick Race instead.");
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

            // Never filed against a heat by auto-advance - see the filing
            // in AfterRace. Cleared by any other start and by Reset.
            _currentRaceIsQuick = true;

            _txtSessionLabel.Text = $"Quick Race {DateTime.Now:HH:mm}";

            ApplyEffectiveFormat();
            ApplyJumpStartRules();

            // A warm-up in the app default format runs first. The format is
            // applied again at the end of it, in case anything moved on.
            if (!stageWarmUpClosed
                && TryBeginWarmUp(() =>
                {
                    ApplyEffectiveFormat();
                    ApplyJumpStartRules();
                    _ = RunAutoStartSequenceAsync();
                }, forQuickRace: true))
            {
                return;
            }

            _ = RunAutoStartSequenceAsync();
        };
        _btnQuickRace.Click += (_, _) => _actQuickRace?.Invoke();

        _btnBigScreen.Text = "Open Big Screen";

        // At the foot of the button column. It is run once before racing
        // starts and then left alone, so it gives up the higher slots to the
        // buttons pressed during the night.
        _btnReadiness.Text = "Pre-race check...";
        _btnReadiness.Click += (_, _) => ShowReadinessCheck();

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
        _btnAnnouncer.Click += (_, _) => AnnouncerRequested?.Invoke();

        _btnProjection.Text = "Qualifying Projection";
        _btnProjection.Click += (_, _) => ToggleProjectionWindow();

        // The stat strip, at the foot of the column, fills the dead space
        // that sat between the Big Screen button and the leaderboard, and
        // puts the leader's lap count and the session best where they can be
        // read without hunting for a row. It sizes itself to the text size -
        // see RaceStatStrip.

        // Restored: my layout rewrite dropped this handler entirely, so the
        // button did nothing and TriggerOpenBigScreen - used by the remote
        // and the day schedule - silently no-opped. The compiler only
        // warned about the unassigned field, not the missing behaviour.
        // The badge store is read through a lambda, not passed as-is, because
        // AwardAchievements replaces _achievements after every unlock and the
        // Big Screen should show the new count as soon as it is announced.
        //
        // The transponder watch and the next race's roster give it the grid
        // roll call before the green (RollCall) - the same cars the
        // pre-race check checks.
        _actBigScreen = () => new BigScreenForm(_race, _announcer, _profiles, _daySchedule, _sponsors, _settings, _projector,
            () => _achievements,
            watch: _transponderWatch,
            roster: () => PreRaceRoster().Roster,
            trackMap: TrackMap).Show();
        _btnBigScreen.Click += (_, _) => _actBigScreen();

        // --- The flags ---
        //
        // One row, in F1-F7 order, each button as wide as its own caption:
        // a tight row (ButtonBar tight), because seven buttons at the usual
        // minimum width would not fit on one line beside the clock. It
        // wraps rather than clipping the last flag off the end when even
        // that is too wide - a longer language, a bigger text size.
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

            // A race staged here is never a Quick Race (that starts through
            // RunAutoStartSequenceAsync), whatever ran before it.
            _currentRaceIsQuick = false;
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

                // When the race stopped counting, so the undo can leave the
                // time it was gone off the race clock - see
                // RestoreSnapshotAfterGap.
                var snapshotTakenUtc = DateTime.UtcNow;
                var sessionStart = _sessionStartedAtUtc;
                var prompted = _promptedTransponders.ToList();

                // The rules the reset race was running under. The snapshot
                // does not carry them, and the undo stays good for as long
                // as the session sits at Idle - where the format IS written
                // to the session, by picking another round, editing a
                // format or changing the venue settings. Put back without
                // them, the race finished and was saved under the next
                // heat's lap or time limit (or none at all). Putting the old
                // round back in the box is not enough either: it writes the
                // round's format as it stands NOW, which may have been
                // edited since, so these are written after it and win.
                var restoreRules = CaptureRaceRulesForUndo();

                _undo.Capture("Reset race", () =>
                {
                    RefuseStaleResetUndo(generationAfterReset);

                    // The round and the name FIRST, while the session is
                    // still Idle - as the abandoned-race undo does, and for
                    // the same reason. RestoreSnapshot below raises Racing
                    // for a race that was running, and what listens for it
                    // reads the host's setup there and then: the run sheet
                    // stamps the round in the box as started (and keeps the
                    // first stamp), and the ghosts start on its track. Put
                    // back afterwards, a round picked by hand after the
                    // reset - one that never ran - was stamped as started,
                    // and the timetable's drift figures went wrong with it.
                    //
                    // Before restoreRules too: at Idle, picking the round
                    // writes that round's format to the session, and the
                    // rules the reset race was really running under have to
                    // be the last word. In a try, so the race still comes
                    // back if this goes wrong.
                    if (restoreBinding is not null)
                    {
                        try
                        {
                            restoreBinding();
                        }
                        catch (Exception ex)
                        {
                            StatusMessage?.Invoke(
                                $"{AppTime.LogStampNow()}  [race] The race is being put back, but its round and name could not be: {ex.Message} "
                                + "Check the Round box before the race finishes.");
                        }
                    }

                    // Before the snapshot, as the abandoned-race undo does:
                    // the session is still Idle here, and RestoreSnapshot
                    // reads the limits when it works out where the warning
                    // ladder had got to.
                    restoreRules();

                    bool heldUnderRed;
                    _suppressFlagAnnouncements = true;
                    try
                    {
                        heldUnderRed = _race.RestoreSnapshotAfterGap(snapshot, snapshotTakenUtc);
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

                    // Held under red if it was running - see
                    // RestoreSnapshotAfterGap - so the director is told why,
                    // and what to press.
                    if (heldUnderRed)
                    {
                        StatusMessage?.Invoke(
                            $"{AppTime.LogStampNow()}  [race] The race is back, held under a red flag - no laps were counted while it was gone. "
                            + "Green when the cars are ready to go again.");
                    }

                    // The format line (and the auto-advance setting read
                    // from it) back in step with the round now in the box.
                    // Racing, so this writes nothing to the session - the
                    // rules put back above stand.
                    ApplyEffectiveFormat();
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
            //
            // Not while an earlier race is still waiting to be saved: the
            // journal is then the only copy of it on disk (see
            // FileUnfiledRaces).
            if (_unfiledRaces.Count == 0)
            {
                _journal.Clear();
            }

            _toldAboutRecordPace.Clear();
            _promptedTransponders.Clear();
            _unnamedTransponders.Clear();
            _previousFlag = FlagState.None;
            _currentRaceIsDemo = false;
            _currentRaceIsQuick = false;

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

        // The state and the flag, each caption beside what it names. They
        // sat at fixed x positions, and "Flag:" at 200 ran into "None" at
        // 246 at every text size - by five pixels at the normal one.
        var lblStateCaption = Wear(new Label { Text = "State:", AutoSize = true }, () => AppTheme.BoldFont);
        _lblRaceState.Text = "Idle";
        _lblRaceState.AutoSize = true;

        var lblFlagCaption = Wear(new Label { Text = "Flag:", AutoSize = true }, () => AppTheme.BoldFont);
        _lblFlag.Text = "None";
        _lblFlag.ForeColor = AppTheme.TextMuted;
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

        // Attached after every Click above, so the action runs and then
        // focus moves off. A flag button that keeps focus keeps answering
        // to Space and Enter, which on this row means a stray keypress can
        // re-start a race that is already running - and the focus ring it
        // draws over a flag colour looks like a fault besides.
        AppTheme.ReleaseFocusAfterClick(
            _btnStage, _btnCountdown, _btnGreen, _btnYellow, _btnRed, _btnCheckered, _btnReset);

        // The chip wraps to the card's width - the value can run long - and
        // wears the window's font.
        _lblFormat.AutoSize = true;
        _lblFormat.ForeColor = AppTheme.TextMuted;

        var formatTip = new ToolTip();
        AppTheme.StyleToolTip(formatTip, keep: true);
        formatTip.SetToolTip(_lblFormat,
            "Which race format is in force for what's about to run: the round's override if it has one, "
            + "else its class's format, else the app default. Class formats are set on the Classes page.");

        _btnFormat.Text = "Race Format...";
        _btnFormat.Click += (_, _) => EditFormat();

        ApplyEffectiveFormat();

        // --- The leaderboard ---
        //
        // BESIDE the clock column, not below it, and given every pixel the
        // flags leave down to the bottom of the page, so it is the biggest
        // thing on the page at any window size - which is what a
        // leaderboard is for. It used to run the full width of the page,
        // which meant it could only start below the clock, the buttons and
        // the stat tiles: half a screen of dead space above a board that
        // never dominated the page it is the whole point of.
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

        // --- Manual corrections toolbar ---
        //
        // Under the board, in its card: what the buttons are for, then who
        // they act on, then the buttons, which wrap onto a second row when
        // the board is narrow - and the board gives up exactly that row's
        // height, no more. "Selected racer:" sits ABOVE the buttons rather
        // than in front of the first one, so both rows of buttons start at
        // the same left edge.
        var lblFix = Wear(new Label { Text = "Selected racer:", AutoSize = true }, () => AppTheme.BoldFont);

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

        // --- The page ---
        //
        // Top to bottom: the host's Session row (see ShowSessionRow), the
        // Next Up bar, then the flags and the board beside the clock column,
        // the board taking the height that is left. Too short a window
        // scrolls the page rather than squashing the board below its least.
        var flags = new Section("Race Control");

        flags.Add(new ButtonBar(tight: true).With(
            _btnStage, _btnCountdown, _btnGreen, _btnYellow, _btnRed, _btnCheckered, _btnReset));

        var status = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Tag = AppTheme.KeepBackColor,
        };

        status.Controls.Add(StatusPair(lblStateCaption, _lblRaceState));
        status.Controls.Add(StatusPair(lblFlagCaption, _lblFlag));

        flags.Add(status);
        flags.Add(_lblFormat);
        flags.Add(new ButtonBar().With(_btnFormat));

        // The board's instructions used to be its group box's caption, all
        // hundred-odd characters of it; a card's caption is a heading, and
        // they are a line of help under it.
        var board = new Section("Leaderboard");

        board.Add(
            Hint("Double-click a driver for their lap breakdown. Right-click for names, penalties and corrections."),
            gapBelow: UiLayout.HintGap);

        board.AddFill(_dgvLeaderboard, LeastBoardHeight);

        board.Add(
            Hint("For missed or double-counted passes, on-track penalties and warnings. Press ? for keyboard shortcuts."),
            gapBelow: UiLayout.HintGap);

        board.Add(lblFix, stretch: false, gapBelow: UiLayout.HintGap);

        // One width for all of them - the widest caption sets it - so the
        // two rows read as columns instead of ragging out to the length of
        // each caption.
        board.Add(
            new ButtonBar(sameWidth: true).With(btnLapPlus, btnLapMinus, btnPenalty5, btnPenalty10, btnClearPenalty, btnWarning, btnColumns),
            gapBelow: 0);

        var body = new Beside(sideLeastWidth: ClockWidth);

        body.Add(flags);
        body.AddFill(board, board.LeastHeight);

        body.AddSide(_clock);
        body.AddSide(_btnQuickRace);
        body.AddSide(_btnBigScreen);
        body.AddSide(_btnAnnouncer);
        body.AddSide(_btnProjection);
        body.AddSide(_btnReadiness);
        body.AddSide(_statStrip);

        _page.Add(_nextUpBar, gapBelow: UiLayout.RowGap);
        _page.Add(_unknownBanner, gapBelow: UiLayout.RowGap);
        _page.AddFill(body, body.LeastHeight, gapBelow: 0);

        Controls.Add(_page);

        // Hidden, and driven from the Session row - see the top of this
        // method. Off the page: nothing is laid out round them.
        Controls.Add(_cmbTrackLayout);
        Controls.Add(_txtSessionLabel);
    }

    /// <summary>
    /// A caption and what it names, kept together on one line - "State:
    /// Racing" - so a row of them that wraps never leaves a caption at the
    /// end of one line and its value at the start of the next. A section
    /// gap after it, before the next pair.
    /// </summary>
    private static FlowLayoutPanel StatusPair(Label caption, Label value)
    {
        caption.Margin = Padding.Empty;
        value.Margin = Padding.Empty;

        var pair = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, UiLayout.SectionGap, 0),
            Padding = Padding.Empty,
            Tag = AppTheme.KeepBackColor,
        };

        pair.Controls.Add(caption);
        pair.Controls.Add(value);

        return pair;
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

    /// <summary>
    /// Takes down the rules the race in the session is running under - its
    /// format, its jump-start rules and its warning ladder - and returns
    /// what writes them back, for an undo that puts that race back.
    ///
    /// The session snapshot carries the standings and the clock but not
    /// these, and between the capture and the undo something may have
    /// written the next race's format over them (at Idle, picking another
    /// round is enough). A race put back without its own rules finished
    /// and was saved under the next heat's lap or time limit. The same
    /// fields the abandoned-race undo (CaptureAbandonedRaceUndo) keeps,
    /// for the same reason; the handicap-start override goes with them,
    /// since a format written at Idle clears it.
    ///
    /// Call the returned action BEFORE RestoreSnapshot: the snapshot reads
    /// the limits when it works out where the warning ladder had got to.
    /// </summary>
    private Action CaptureRaceRulesForUndo()
    {
        var lapLimit = _race.LapLimit;
        var timeLimit = _race.TimeLimit;
        var minLapTimeMs = _race.MinLapTimeMs;
        var streakWindowPercent = _race.StreakWindowPercent;
        var streakFasterLapBreaks = _race.StreakFasterLapBreaks;
        var startType = _race.StartType;
        var staggerClockMode = _race.StaggerClockMode;
        var finishOnNextCrossing = _race.FinishOnNextCrossing;
        var clockBeforeHandicapOverride = _clockBeforeHandicapOverride;
        var detectJumpStarts = _race.DetectJumpStarts;
        var jumpStartPenaltyMs = _race.JumpStartPenaltyMs;
        var jumpStartCostsTheLap = _race.JumpStartCostsTheLap;
        var warningLadder = _race.WarningLadder;

        return () =>
        {
            _race.LapLimit = lapLimit;
            _race.TimeLimit = timeLimit;
            _race.MinLapTimeMs = minLapTimeMs;
            _race.StreakWindowPercent = streakWindowPercent;
            _race.StreakFasterLapBreaks = streakFasterLapBreaks;
            _race.StartType = startType;
            _race.StaggerClockMode = staggerClockMode;
            _race.FinishOnNextCrossing = finishOnNextCrossing;
            _clockBeforeHandicapOverride = clockBeforeHandicapOverride;
            _race.DetectJumpStarts = detectJumpStarts;
            _race.JumpStartPenaltyMs = jumpStartPenaltyMs;
            _race.JumpStartCostsTheLap = jumpStartCostsTheLap;
            _race.WarningLadder = warningLadder;
        };
    }
}
