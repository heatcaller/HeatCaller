namespace HeatCaller;

/// <summary>
/// Race Control's wiring to the session: the handlers for state, flag, lap
/// and standing changes, and the record-pace and track-record checks.
///
/// Moved here unchanged from RaceControlControl.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class RaceControlControl
{
    // Stored so they can be unsubscribed from the shared, long-lived
    // RaceControlSession when this window closes. Without this, closing
    // Race Control leaves these delegates subscribed forever - the session
    // outlives the window by design (it keeps running even with Race
    // Control closed), so a closed-and-disposed window would otherwise
    // keep silently receiving every future lap event and crash trying to
    // update controls that no longer exist.
    private Action<RaceState>? _stateChangedHandler;
    private Action<FlagState>? _flagChangedHandler;
    private Action? _warmUpChangedHandler;
    private Action<RacerStanding, bool, bool>? _lapProcessedHandler;

    /// <summary>A car crossed a split loop, or a lap's sectors came in late - the board's split columns follow.</summary>
    private Action<RacerStanding, TimingPoint, long>? _splitCrossedHandler;
    private Action<RacerStanding, int, long[]>? _sectorsRecordedHandler;
    private Action<RacerStanding>? _personalBestHandler;
    private Action<RacerStanding>? _standingAdjustedHandler;
    private Action<RacerStanding>? _standingRemovedHandler;
    private Action<string, long>? _jumpStartHandler;
    private Action<RacerStanding, RaceWarning>? _warningIssuedHandler;
    private Action? _timeLimitReachedHandler;
    private Action? _raceCompletedHandler;

    /// <summary>
    /// The start gate that was wired before this page added its own - the
    /// host's wait for the announcer to finish speaking. Chained onto, and
    /// put back on teardown, rather than overwritten.
    /// </summary>
    private Func<Task>? _gateBeforeThisPage;
    private Action<string, long>? _driverReleasedHandler;

    private void WireRaceControl()
    {
        // The show's block is abandoned the moment the race moves, and
        // BEFORE the marshal to the UI thread rather than after it.
        //
        // A block can hold two or three items with music under them (see
        // ShowBlock). Staging, a countdown, a green or any flag means the
        // room's attention is on the track, and anything the block had left
        // to say is dropped - never queued, because "still to come, the
        // A-main" said after the A-main started is worse than silence.
        //
        // Before the marshal because a post sits behind whatever the UI
        // thread is already doing, and the whole point is that it stops
        // NOW. AbandonBlock is built to be called off the UI thread.
        // Harmless when there is no block, and harmless twice - the recap
        // and the round intro also pass their own cancellation token, so
        // this is the belt to their braces.
        _stateChangedHandler = state =>
        {
            ShowMusic?.Invoke()?.AbandonBlock();

            if (InvokeRequired) { BeginInvoke(() => OnRaceStateChanged(state)); return; }
            OnRaceStateChanged(state);
        };
        _race.StateChanged += _stateChangedHandler;

        _flagChangedHandler = flag =>
        {
            ShowMusic?.Invoke()?.AbandonBlock();

            if (InvokeRequired) { BeginInvoke(() => OnFlagChanged(flag)); return; }
            OnFlagChanged(flag);
        };
        _race.FlagChanged += _flagChangedHandler;

        _warmUpChangedHandler = () =>
        {
            if (InvokeRequired) { BeginInvoke(OnWarmUpChanged); return; }
            OnWarmUpChanged();
        };
        _race.WarmUpChanged += _warmUpChangedHandler;

        _lapProcessedHandler = (standing, isValid, isFastestLap) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnLapProcessed(standing, isValid, isFastestLap)); return; }
            OnLapProcessed(standing, isValid, isFastestLap);
        };
        _race.LapProcessed += _lapProcessedHandler;

        // The At Split column changes as a car crosses a split loop, between
        // its laps; and a lap's sectors can arrive a moment after the lap.
        _splitCrossedHandler = (standing, point, _) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnSplitCrossed(standing, point)); return; }
            OnSplitCrossed(standing, point);
        };
        _race.SplitCrossed += _splitCrossedHandler;

        _sectorsRecordedHandler = (standing, lapIndex, sectors) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnSectorsRecorded(standing, lapIndex, sectors)); return; }
            OnSectorsRecorded(standing, lapIndex, sectors);
        };
        _race.SectorsRecorded += _sectorsRecordedHandler;

        _personalBestHandler = standing =>
        {
            if (InvokeRequired) { BeginInvoke(() => _announcer.NotifyPersonalBestLap(standing.TransponderId, standing.FastestLapMs)); return; }
            _announcer.NotifyPersonalBestLap(standing.TransponderId, standing.FastestLapMs);
        };
        _race.PersonalBestLap += _personalBestHandler;

        _standingAdjustedHandler = standing =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnStandingAdjusted(standing)); return; }
            OnStandingAdjusted(standing);
        };
        _race.StandingAdjusted += _standingAdjustedHandler;

        _standingRemovedHandler = standing =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnStandingRemoved(standing)); return; }
            OnStandingRemoved(standing);
        };
        _race.StandingRemoved += _standingRemovedHandler;

        _jumpStartHandler = (transponder, penaltyMs) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnJumpStart(transponder, penaltyMs)); return; }
            OnJumpStart(transponder, penaltyMs);
        };
        _race.JumpStartDetected += _jumpStartHandler;

        _warningIssuedHandler = (standing, warning) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnWarningIssued(standing, warning)); return; }
            OnWarningIssued(standing, warning);
        };
        _race.WarningIssued += _warningIssuedHandler;

        _timeLimitReachedHandler = () => _announcer.NotifyRaceTimeOver();
        _race.TimeLimitReached += _timeLimitReachedHandler;

        // The call first, the save whatever the call does - see RaceFiling.AfterTheFlag.
        _raceCompletedHandler = () => RaceFiling.AfterTheFlag(
            _announcer.NotifyRaceCompleted,
            () =>
            {
                if (InvokeRequired) { BeginInvoke(SaveCompletedSessionToHistory); return; }
                SaveCompletedSessionToHistory();
            },
            message => StatusMessage?.Invoke(message));
        _race.RaceCompleted += _raceCompletedHandler;

        // The one place every start path meets.
        //
        // The Green button, the automated Stage->Countdown->Green sequence,
        // a demo and a quick race all reach the flag through
        // GreenFlagAfterAudioAsync, and it awaits this gate before dropping
        // it. Start-of-race work belongs here rather than in any one
        // caller: it used to sit in the Green button's own handler, and a
        // demo - which never presses that button - silently started without
        // a release schedule.
        //
        // Only ever runs for a REAL start. The session skips the gate when
        // it is already racing, so a restart after a caution can't
        // re-stagger a field that is already spread around the track.
        // CHAINED onto whatever was already there, not assigned over it.
        //
        // BeforeGreenFlagAsync is a single delegate, not an event, and
        // MainForm wires it first - to wait for the announcer to finish
        // speaking before the green, so a start never talks over its own
        // countdown call. This page is built afterwards, and assigning its
        // own gate simply REPLACED that one. The wait for audio had not
        // run before a green flag since this was written, and nothing
        // said so: the only symptom is a start that tramples the call.
        var hostGate = _race.BeforeGreenFlagAsync;
        _gateBeforeThisPage = hostGate;

        _race.BeforeGreenFlagAsync = async () =>
        {
            _lastUnlocks = new List<AchievementUnlock>();

            // Reads on-screen values - the gap box, the selected round - so
            // it has to be on the UI thread. The gate is awaited from
            // wherever the start happened to be triggered from.
            //
            // Never for practice. Open lapping has no grid and no start
            // procedure, and running this there handed it the race
            // format's staggered or hand-released start - cars held at the
            // line, or their laps timed from a release nobody called.
            if (!IsDisposed && !Disposing && !_race.IsPracticeMode)
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(ApplyStartRules));
                }
                else
                {
                    ApplyStartRules();
                }
            }

            // Then the host's gate - the audio wait. Rules first because
            // they are instant and read what is on screen now; the wait
            // is the part that takes time.
            if (hostGate is not null)
            {
                await hostGate();
            }
        };

        _driverReleasedHandler = (transponder, offsetMs) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnDriverReleased(transponder, offsetMs)); return; }
            OnDriverReleased(transponder, offsetMs);
        };
        _race.DriverReleased += _driverReleasedHandler;
    }

    private void UnwireRaceControl()
    {
        if (_stateChangedHandler is not null) _race.StateChanged -= _stateChangedHandler;
        if (_flagChangedHandler is not null) _race.FlagChanged -= _flagChangedHandler;
        if (_warmUpChangedHandler is not null) _race.WarmUpChanged -= _warmUpChangedHandler;
        if (_lapProcessedHandler is not null) _race.LapProcessed -= _lapProcessedHandler;
        if (_splitCrossedHandler is not null) _race.SplitCrossed -= _splitCrossedHandler;
        if (_sectorsRecordedHandler is not null) _race.SectorsRecorded -= _sectorsRecordedHandler;
        if (_personalBestHandler is not null) _race.PersonalBestLap -= _personalBestHandler;
        if (_standingAdjustedHandler is not null) _race.StandingAdjusted -= _standingAdjustedHandler;
        if (_standingRemovedHandler is not null) _race.StandingRemoved -= _standingRemovedHandler;
        if (_jumpStartHandler is not null) _race.JumpStartDetected -= _jumpStartHandler;
        if (_warningIssuedHandler is not null) _race.WarningIssued -= _warningIssuedHandler;
        if (_timeLimitReachedHandler is not null) _race.TimeLimitReached -= _timeLimitReachedHandler;
        if (_raceCompletedHandler is not null) _race.RaceCompleted -= _raceCompletedHandler;
        if (_driverReleasedHandler is not null) _race.DriverReleased -= _driverReleasedHandler;

        // Handed back, not cleared. The session outlives this page, and a
        // gate pointing at a disposed control would run start-of-race work
        // against controls that are gone - but setting it to null also
        // threw away the host's audio wait, which belongs to the session
        // rather than to this page.
        _race.BeforeGreenFlagAsync = _gateBeforeThisPage;
    }

    private void OnRaceStateChanged(RaceState state)
    {
        // Stage and Countdown light with the start's own progress.
        UpdateFlagButtonColors(_race.Flag);

        // Staging already clears the session's standings, but the grid only
        // redraws on a lap event - so the previous race's rows stayed on
        // screen through staging and the countdown, right when everyone is
        // looking at the board. Clearing here empties it the moment cars
        // are called to the grid.
        if (state == RaceState.Staging)
        {
            _dgvLeaderboard.Rows.Clear();

            // The announcer keeps its own copy of the standings for leader
            // changes and position reports, and nothing cleared it between
            // races on this path - only a Next Level Timing connect did. The
            // last race's drivers stayed in its running order with their
            // final lap counts, so a new race could open with a false
            // "new leader" call and a position report naming someone who
            // wasn't even on track (or never fire, stuck on an old lap).
            _announcer.ResetSession();

            // A new race is being called, so no start time from before it -
            // a practice, a reset or an abandoned race - may carry into it.
            // The Green button and the automatic start stamp their own; a
            // demo's green does not, and took the stale one through the
            // "??=" below. (Undoing an abandon puts the old one back.)
            _sessionStartedAtUtc = null;
        }

        // Captured here rather than in the Green button handler: a demo
        // drives the session directly and never presses that button, so
        // a button-only capture always read false for simulations.
        if (state == RaceState.Racing)
        {
            _currentRaceIsDemo = _race.IsDemoMode;

            // A fresh start clears the standings in the session, but the
            // grid only redraws on a lap event - so last race's rows sat
            // there until someone crossed the line. Clear it now, and only
            // for a genuine start: resuming from a caution must keep the
            // laps already counted.
            //
            // Told by the session's own signal, not by the standings being
            // empty: a hand-released start puts its held grid on the board
            // at the green, so a start that skipped Stage never looked
            // empty and kept the last race's standings in the announcer.
            if (_race.GreenStartedTheRace)
            {
                // Redrawn from the session rather than just emptied, now
                // that this also runs when a held grid is already in it:
                // empty standings draw an empty board as before, and a
                // hand-released grid shows its held cars to be released.
                RenderLeaderboard();

                // Practice and quick starts skip staging - same reset.
                _announcer.ResetSession();

                // 17 Oct 2026, Bug Hunt 8, A29-2: the room's vote closes at
                // the green - a prediction gathered after the flag is not a
                // prediction. A demo never touches the vote, as in
                // SettleTheRoomsVote.
                if (!_race.IsDemoMode)
                {
                    try
                    {
                        TonightsVote?.Invoke()?.StartedRace(_activeRound?.Invoke()?.Id);
                    }
                    catch (Exception ex)
                    {
                        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] The room's vote could not be closed at the green: {ex.Message}");
                    }
                }
            }

            // Also covers the demo and remote paths, which don't go through
            // the Green button where this used to be set.
            _sessionStartedAtUtc ??= DateTime.UtcNow;
        }

        _lblRaceState.Text = state.ToString();

        // The unknown-transponder banner is for between races.
        _unknownBanner.RefreshVisibility();

        // Posted, not called. A lap is what ends a race, so this handler
        // can be sitting inside RecordLap right now - see
        // NoteUnnamedTransponderLaps.
        if (state is (RaceState.Finished or RaceState.Idle) && IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(() => NoteUnnamedTransponderLaps());
        }

        switch (state)
        {
            case RaceState.Staging:
                _announcer.NotifyRaceStaging();
                UpdateClockDisplay(force: true);

                // Here rather than in the Stage button, so the automatic
                // sequence (Quick Race, Start Next) is covered too.
                WarnAboutUninspectedDrivers();
                break;
            case RaceState.Countdown:
                // Set only while the automatic start opens the countdown -
                // it alone knows when the green will drop. See
                // _countdownGreenIn.
                _announcer.NotifyCountdownStarted(_countdownGreenIn);
                break;
        }
    }

    /// <summary>
    /// Writes the crash journal now rather than at the next throttled
    /// moment - for the window closing mid-race, so the last crossings
    /// before the app went away are the ones offered back next time.
    /// </summary>
    public void FlushJournal() => WriteJournal(force: true);

    private void OnFlagChanged(FlagState flag)
    {
        // A flag is a moment worth being able to come back to, so this one
        // is written straight away rather than waiting for the next lap.
        WriteJournal(force: true);

        _lblFlag.Text = flag.ToString();
        // Bright variants, not the dark ones these used to be: this label
        // sits on the page background, which is near-black in Dark and
        // Synthwave modes - Color.Black for Checkered and the system text
        // color for None were both effectively invisible there.
        _lblFlag.ForeColor = flag switch
        {
            FlagState.Green => Color.FromArgb(61, 220, 132),
            FlagState.Yellow => Color.FromArgb(255, 209, 71),
            FlagState.Red => Color.FromArgb(255, 92, 92),
            FlagState.Checkered => AppTheme.TextDark,
            _ => AppTheme.TextMuted,
        };

        // Practice and demo runs drive the same shared session, and this
        // control stays subscribed for the app's lifetime - announcing
        // flags for those would be wrong, so skip them here.
        // Demo is no longer excluded. It's how the announcer actually gets
        // tested - suppressing the green and checkered calls there meant
        // the two most important announcements could never be heard while
        // setting the thing up. Practice still skips them: an open lapping
        // session has no flags to call.
        if (!_race.IsPracticeMode && !_suppressFlagAnnouncements)
        {
            switch (flag)
            {
                // A green that started the race is the start, whatever flag
                // was out before it. This used to go by the flag before
                // alone, so a start held with a yellow or a red during
                // staging or the countdown was called as a restart - no
                // horn, and nothing said with that call switched off, as it
                // is by default. See RaceControlSession.GreenStartedTheRace.
                case FlagState.Green when _race.GreenStartedTheRace:
                    _announcer.NotifyRaceStarted();
                    break;
                case FlagState.Green when _previousFlag is FlagState.Yellow or FlagState.Red:
                    _announcer.NotifyGreenFlagResumed();
                    break;
                case FlagState.Green:
                    _announcer.NotifyRaceStarted();
                    break;
                case FlagState.Yellow:
                    _announcer.NotifyYellowFlag();
                    break;
                case FlagState.Red:
                    _announcer.NotifyRedFlag();
                    break;

                // No Checkered case, on purpose. The finish is called from
                // the time limit and the race completing, not from the flag
                // - and a checkered put back out by green after a yellow or
                // red on the last lap (see RaceControlSession) is the same
                // finish, already called. Saying nothing beats "racing
                // resumes" or a second "time's up".
            }
        }

        UpdateFlagButtonColors(flag);
        _previousFlag = flag;
    }

    /// <summary>
    /// Lights up whichever flag is currently in effect - and Stage or
    /// Countdown while the start is at that point - and leaves the rest at
    /// rest. Driven from the actual race state rather than from clicks, so
    /// it stays correct when a flag is set from the phone remote, a
    /// keyboard shortcut, or the auto-start sequence. Hover is the
    /// buttons' own business (see FlagButton); this only says what is lit.
    /// </summary>
    private void UpdateFlagButtonColors(FlagState flag)
    {
        var state = _race.State;

        // Lit through the warm-up too: pressing it again is how to skip it.
        _btnStage.Lit = state == RaceState.Staging || _race.IsWarmingUp;
        _btnCountdown.Lit = state == RaceState.Countdown;
        _btnGreen.Lit = flag == FlagState.Green;
        _btnYellow.Lit = flag == FlagState.Yellow;
        _btnRed.Lit = flag == FlagState.Red;
        _btnCheckered.Lit = flag == FlagState.Checkered;
    }

    /// <summary>
    /// Bridges a race-control lap update into the shared AnnouncementService,
    /// which has no idea whether the data came from Next Level Timing or
    /// straight from the decoder - it just needs the same shapes it already
    /// understands.
    /// </summary>
    private void OnLapProcessed(RacerStanding standing, bool isValid, bool isFastestLap)
    {
        NoteUnknownTransponder(standing.TransponderId);

        var profile = _profiles.Get(standing.TransponderId);

        var miniZStanding = new RaceStanding
        {
            Transponder = standing.TransponderId,
            Name = profile?.DriverName,
            Laps = standing.Laps,
            FastLapMs = standing.FastestLapMs,
            ElapsedMs = standing.ElapsedMs,
            Status = isValid ? "active" : "invalid",
            Id = standing.TransponderId,
            LapsLed = standing.LapsLed,
        };
        _announcer.OnStandingUpdated(miniZStanding);

        // Track record check, against the selected layout's all-time best.
        // Checked on any lap that just became this driver's best, not only
        // on a "fastest lap of the race" - that flag is deliberately false
        // for the first flying lap of the race (nothing to beat yet), which
        // meant a record set on that lap was never noticed.
        // CheckAndAnnounceTrackRecord itself only acts on a genuine record.
        var thisLapMs = isValid && standing.LapTimesMs.Count > 1 ? standing.LapTimesMs[^1] : 0;
        if (thisLapMs > 0 && thisLapMs == standing.FastestLapMs)
        {
            CheckAndAnnounceTrackRecord(miniZStanding);
        }

        var gate = new GatePassEvent
        {
            Transponder = standing.TransponderId,
            Gate = "1",
            GateType = "finish",
            Valid = isValid,
            // Streak below is unused now that StreakCountOverride is set -
            // RaceControlSession computes the real streak count itself
            // (including breaking it on unusually slow laps), so the
            // announcer should use that directly rather than re-deriving
            // its own simpler approximation.
            Streak = isValid,
            Fast = isFastestLap,
            StreakCountOverride = standing.ConsecutiveValidLaps,

            // The session's own measured lap. The announcer used to rebuild
            // it from changes in elapsed time, which read a penalty as part
            // of the next lap and, on a rejected pass, announced the whole
            // race time as a "lap time". Null for the start lap.
            LapTimeMs = thisLapMs > 0 ? (long?)thisLapMs : null,
        };
        _announcer.OnGatePassed(gate);

        // The safety net. Written after every crossing, so that whatever
        // happens next - a crash, a power cut, a closed laptop - the race
        // can be picked up from this lap.
        WriteJournal();

        CheckRecordPace(standing, isValid, thisLapMs);

        RenderLeaderboard();
    }

    /// <summary>
    /// A ghost switched off mid-race, taken off the board. Off the
    /// announcer's running order too, and out of the crash journal.
    /// </summary>
    private void OnStandingRemoved(RacerStanding standing)
    {
        _announcer.OnStandingRemoved(standing.TransponderId);
        WriteJournal(force: true);
        RenderLeaderboard();
    }

    /// <summary>
    /// A lap added or removed, or a penalty, by hand. Updates the board and
    /// the announcer's running order - but it is not a pass, so no lap,
    /// lap-time, streak or invalid-lap announcement is made for it.
    /// </summary>
    private void OnStandingAdjusted(RacerStanding standing)
    {
        var profile = _profiles.Get(standing.TransponderId);

        _announcer.OnStandingUpdated(new RaceStanding
        {
            Transponder = standing.TransponderId,
            Name = profile?.DriverName,
            Laps = standing.Laps,
            FastLapMs = standing.FastestLapMs,
            ElapsedMs = standing.ElapsedMs,
            Status = "adjusted",
            Id = standing.TransponderId,
            LapsLed = standing.LapsLed,
        });

        // Forced: a correction by hand is rare and is exactly the sort of
        // thing nobody wants to do twice.
        WriteJournal(force: true);

        // After the flag the result has already gone to the history, and
        // this only changes the board. Said plainly, because the board then
        // disagrees with the saved result, the points and the qualifying
        // figures - and nothing else would say which one is right. Rewriting
        // the saved session from here would also have to undo and redo its
        // points, achievements and round link, which is not something to do
        // quietly from a leaderboard right-click.
        if (_race.State == RaceState.Finished && !_race.IsPracticeMode)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [race] Adjustment after the flag - history not updated. "
                + "The saved result still has the figures it had at the checkered flag.");
        }

        RenderLeaderboard();
    }

    /// <summary>
    /// Drivers already told they're on record pace this race.
    ///
    /// Once each: the whole value of the call is that it's rare. Somebody
    /// circulating a tenth off the record for eight laps should be called
    /// out once and then left alone, not announced every time round.
    /// </summary>
    private readonly HashSet<string> _toldAboutRecordPace = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The session generation the set above belongs to. Stage, a fresh
    /// green and Reset each move the generation on, so a new race starts
    /// with nobody told. The set used to be emptied by Reset alone, and
    /// races normally follow one another without one - so the call went
    /// out once per driver per evening rather than once per race.
    /// </summary>
    private int _toldAboutRecordPaceGeneration = -1;

    /// <summary>
    /// How close to the record counts as being on for it.
    ///
    /// Two percent is about a tenth of a second on a five second lap -
    /// close enough that the next lap could genuinely take it, which is the
    /// only thing worth saying out loud.
    /// </summary>
    private const double RecordPaceWindow = 1.02;

    /// <summary>
    /// Watches for somebody running at the pace of the track record.
    ///
    /// The record itself is already announced when it falls. This is the
    /// bit before that: the lap that says it's ABOUT to, which is the part
    /// worth watching and the part an announcer would actually call.
    ///
    /// Deliberately not fired on the first flying lap - one quick lap is a
    /// tow or a lucky line, and the interesting claim is that the pace is
    /// real.
    /// </summary>
    private void CheckRecordPace(RacerStanding standing, bool isValid, long lapMs)
    {
        if (_toldAboutRecordPaceGeneration != _race.SessionGeneration)
        {
            _toldAboutRecordPace.Clear();
            _toldAboutRecordPaceGeneration = _race.SessionGeneration;
        }

        if (!isValid || lapMs <= 0 || _race.IsPracticeMode)
        {
            return;
        }

        // Only for somebody who could actually take the record - the same
        // drivers CheckAndAnnounceTrackRecord lets set one. Calling a ghost,
        // a rental or an excluded guest "on record pace" promises a record
        // that cannot fall to them.
        if (GhostService.IsGhost(standing.TransponderId)
            || _profiles.Get(standing.TransponderId) is { ExcludeFromRecords: true } or { IsRental: true })
        {
            return;
        }

        // Two counted laps is the green-to-line pass plus one real lap;
        // three means they have actually strung a pace together.
        if (standing.LapTimesMs.Count < 3 || _toldAboutRecordPace.Contains(standing.TransponderId))
        {
            return;
        }

        var trackName = SelectedTrackName();

        if (string.IsNullOrWhiteSpace(trackName))
        {
            return;
        }

        var track = _trackLayouts.All.FirstOrDefault(t => t.Name == trackName);

        if (track is null || track.RecordLapMs <= 0)
        {
            return;
        }

        // Beating it is a different event, announced elsewhere - this is
        // only for the laps that get close without taking it.
        if (lapMs <= track.RecordLapMs || lapMs > track.RecordLapMs * RecordPaceWindow)
        {
            return;
        }

        _toldAboutRecordPace.Add(standing.TransponderId);

        var profile = _profiles.Get(standing.TransponderId);
        var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? standing.TransponderId : profile!.DriverName;
        var off = (lapMs - track.RecordLapMs) / 1000.0;

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [pace] {name} is on record pace - {lapMs / 1000.0:0.000} "
            + $"against the {track.Name} record of {track.RecordLapMs / 1000.0:0.000} ({off:0.000} off, "
            + $"held by {(string.IsNullOrWhiteSpace(track.RecordHolderName) ? "nobody on record" : track.RecordHolderName)}).");

        _announcer.NotifyRecordPace(new RaceStanding
        {
            Transponder = standing.TransponderId,
            Name = profile?.DriverName,
            Laps = standing.Laps,
            FastLapMs = standing.FastestLapMs,
            ElapsedMs = standing.ElapsedMs,
            Status = "active",
            Id = standing.TransponderId,
        }, lapMs);
    }

    /// <summary>
    /// Compares this lap against the selected track layout's all-time
    /// record (independent of race-specific "fastest lap" tracking) and,
    /// if it's a new record, persists it and fires the special announcement.
    /// No-ops if no track layout is selected - track records only make
    /// sense once laps are attributed to a specific physical track.
    /// </summary>
    private void CheckAndAnnounceTrackRecord(RaceStanding standing)
    {
        if (_race.IsDemoMode || _race.IsPracticeMode)
        {
            return; // simulated lap times are fake - never let them set a real all-time record
        }

        // Honour the profile's exclusion flag. Guests, staff demo cars and
        // rental transponders shouldn't own a club's all-time record - the
        // flag exists precisely so that can be prevented without banning
        // them from racing.
        //
        // Ghosts are checked by transponder as well as by their profile's
        // flag, so a ghost whose profile is missing can't take one either.
        var recordProfile = _profiles.Get(standing.Transponder);
        if (GhostService.IsGhost(standing.Transponder)
            || recordProfile is { ExcludeFromRecords: true } or { IsRental: true })
        {
            return;
        }

        var selectedTrackName = _cmbTrackLayout.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedTrackName))
        {
            return;
        }

        var track = _trackLayouts.All.FirstOrDefault(t => t.Name == selectedTrackName);
        if (track is null)
        {
            return;
        }

        if (track.RecordLapMs != 0 && standing.FastLapMs >= track.RecordLapMs)
        {
            return; // not a new record
        }

        if (!IsPlausibleRecord(standing.FastLapMs, standing.Transponder, track, out var doubt))
        {
            var who = string.IsNullOrWhiteSpace(standing.Name) ? standing.Transponder : standing.Name;

            // Said out loud in the log rather than dropped quietly. If the
            // lap really was driven, the operator needs to know the record
            // was left alone - and the driver is on the pace, so they will
            // do it again in a moment.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [race] All-time record NOT taken from {who}'s {standing.FastLapMs / 1000.0:0.000}s - {doubt}. "
                + "The lap still counts toward the race; only the record is left as it was.");
            return;
        }

        track.RecordLapMs = standing.FastLapMs;
        track.RecordHolderName = string.IsNullOrWhiteSpace(standing.Name) ? standing.Transponder : standing.Name;
        track.RecordSetUtc = DateTime.UtcNow;

        // Its sectors when it had some - for record pace at the split. A
        // record set without them clears the old record's.
        track.KeepRecordSectors(RecordLapSectors(standing.Transponder, standing.FastLapMs));
        _trackLayouts.Upsert(track);

        _announcer.NotifyTrackRecord(standing);
    }

    /// <summary>
    /// Whether a lap that beats the all-time record is believable as a lap.
    ///
    /// The two possible mistakes do not cost the same. Refusing a real
    /// record costs one lap: the driver is on the pace and will do it
    /// again, and the log line above says it happened. Accepting a phantom
    /// writes a time nobody drove into the club's permanent record, where
    /// it becomes a ghost pace source, an achievement and an announcement -
    /// and nothing in the app ever questions it again. The only way back is
    /// to clear the record by hand on the track layout.
    ///
    /// A transponder left sitting under the bridge retriggers, and any two
    /// of those reads far enough apart to clear the minimum lap time are
    /// counted as a lap. That is what the minimum is for, and the operator
    /// sets it. But a lap wildly off the pace of everything around it is
    /// not a record, whatever the decoder said.
    ///
    /// Three questions, any one of which is enough to leave the record
    /// alone. They are deliberately loose - each is asking whether the time
    /// is possible, not whether it is impressive.
    /// </summary>
    private bool IsPlausibleRecord(long lapMs, string transponder, TrackLayout track, out string doubt)
    {
        doubt = string.Empty;

        // 1. Sitting on top of the minimum is the shape of a retrigger, not
        //    of a lap. The operator sets the minimum just under a real lap
        //    for the class, so a tenth above it is inside the very noise
        //    that setting exists to describe.
        if (_race.MinLapTimeMs > 0 && lapMs < _race.MinLapTimeMs * 1.1)
        {
            doubt = $"it is within a tenth of the {_race.MinLapTimeMs / 1000.0:0.000}s minimum lap time, which is what a retriggering transponder looks like";
            return false;
        }

        // 2. Against the record it claims to beat. Track records fall by
        //    hundredths and occasionally by tenths. A quarter of a lap is
        //    not an improvement, it is a different event.
        if (track.RecordLapMs > 0 && lapMs < track.RecordLapMs * 0.75)
        {
            doubt = $"it beats the standing record of {track.RecordLapMs / 1000.0:0.000}s by more than a quarter";
            return false;
        }

        // 3. Against the driver's own laps - which is what catches a single
        //    bad read on a layout that has no record yet. A median rather
        //    than a best, so one phantom cannot drag down the figure it is
        //    being judged against, and only once there are enough laps for
        //    a median to mean anything. Index 0 is the start lap, which is
        //    not a lap of the circuit.
        var own = _race.GetOrderedStandings()
            .FirstOrDefault(s => string.Equals(s.TransponderId, transponder, StringComparison.OrdinalIgnoreCase));

        // Recovery laps are left out too (RaceGaps.PaceLaps): one spans a
        // whole stoppage, and a few of them would lift the median until an
        // ordinary lap looked like a phantom.
        var theirLaps = own is null ? null : RaceGaps.PaceLaps(own).Where(t => t > 0).OrderBy(t => t).ToList();

        if (theirLaps is { Count: >= 3 })
        {
            var median = theirLaps[theirLaps.Count / 2];

            if (lapMs < median * 0.5)
            {
                doubt = $"it is less than half this driver's typical {median / 1000.0:0.000}s lap this race";
                return false;
            }
        }

        return true;
    }
}
