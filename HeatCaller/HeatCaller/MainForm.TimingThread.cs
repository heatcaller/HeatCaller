namespace HeatCaller;

/// <summary>
/// MainForm's side of "Run timing on its own thread" (AppSettings.
/// TimingOnItsOwnThread): the lookups the race reads on the timing thread,
/// and the line's passes going straight to it. None of this runs inline -
/// with the setting off, every path is the one it always was.
/// </summary>
/// <remarks>
/// WHY (23 Oct 2026, timing layer, P4): the timing thread never waits on this
/// window. So whatever the race asks during a lap must already be to hand
/// without the UI thread (frozen lookups), and a pass from the decoder must
/// not have to queue behind the window's paint and dialogs to be counted.
/// </remarks>
public partial class MainForm
{
    /// <summary>How many clean laps make a streak in a race, as last read from the sound settings on the UI thread. See FrozenStreakThreshold.</summary>
    private volatile int _frozenStreakRace;

    /// <summary>The same for practice, which has its own figure.</summary>
    private volatile int _frozenStreakPractice;

    /// <summary>
    /// The race's streak threshold, threaded: the figure for the session it
    /// is (practice or race), read from the two kept by
    /// RefreshFrozenStreakThresholds. Called by the race on the timing thread
    /// - where its IsPracticeMode read runs inline - and safe from any thread.
    /// The race floors it at two, as the sound settings do.
    /// </summary>
    private int FrozenStreakThreshold() =>
        _race.IsPracticeMode ? _frozenStreakPractice : _frozenStreakRace;

    /// <summary>Reads the two streak figures from the sound settings. UI thread.</summary>
    private void RefreshFrozenStreakThresholds()
    {
        var sounds = _soundStore.Current;
        _frozenStreakRace = sounds.StreakThresholdFor(practice: false);
        _frozenStreakPractice = sounds.StreakThresholdFor(practice: true);
    }

    /// <summary>
    /// Brings every lookup the race reads on the timing thread up to date
    /// from the stores, on the UI thread - from the 500 ms race tick, so a
    /// change made in Sound Effects or the Track Designer reaches the race
    /// within half a second. The alternate transponders need nothing here:
    /// ProfileStore swaps its index in whole whenever the profiles change.
    /// Threaded mode only.
    /// </summary>
    private void RefreshFrozenLookups()
    {
        try
        {
            RefreshFrozenStreakThresholds();
        }
        catch
        {
            // The figures from the last tick stand.
        }

        try
        {
            SectorLengthsFeetNow();
        }
        catch
        {
            // A plan that can't be measured only costs the speeds - the
            // last lengths worked out stand, as the race's own read does.
        }

        // The split loop's copies, as UpdateConnectionModeUi and Line Up set
        // them - a backstop, as the streak figures are (24 Oct 2026, timing
        // layer, P8).
        _splitLoopOn = _settings.SplitLoopEnabled;
        _frozenSplitOffsetMs = _settings.SplitLoopOffsetMs;

        RefreshPracticeAutoStartPlan();
    }

    /// <summary>
    /// Threaded mode: tells the timing thread whether practice may start at
    /// the next line pass made at Idle, and with which targets (RaceTiming.
    /// PracticeAutoStart) - every check MaybeAutoStartPracticeOnGatePass and
    /// PracticeModeControl.TryAutoStartPractice make that only this window
    /// can: the setting on, the unfinished-race offer answered, Line Up shut,
    /// the Practice page there and not running a practice or starting one.
    /// The race being Idle with no warm-up the timing side checks itself, at
    /// the crossing. UI thread; does nothing inline.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P7): so a pass at Idle no longer has
    /// to go round by the window for practice to start on it. Called the
    /// moment any of those changes (the Practice page's
    /// AutoStartConditionsChanged, Line Up opening and shutting, the offer
    /// answered) and on the 500 ms race tick as a backstop. Anything that
    /// fails here takes the plan away: a practice that does not start on its
    /// own is the safe side.
    /// </remarks>
    private void RefreshPracticeAutoStartPlan()
    {
        if (!_race.IsThreaded)
        {
            return;
        }

        try
        {
            PracticeAutoStartPlan? plan = null;

            if (_settings.PracticeAutoStartOnGatePass
                && _recoveryDecided
                && !_lineUpOpen
                && _practiceControl is { } practice
                && practice.AutoStartTargets() is { } targets)
            {
                plan = new PracticeAutoStartPlan(targets.LapLimit, targets.TimeLimit, AdoptAutoStartedPractice);

                // The one armed already says the same: left in place.
                if (plan.SameAs(_race.PracticeAutoStart))
                {
                    return;
                }
            }

            _race.PracticeAutoStart = plan;
        }
        catch
        {
            try
            {
                _race.PracticeAutoStart = null;
            }
            catch
            {
                // Nothing more to do.
            }
        }
    }

    /// <summary>
    /// Threaded mode, on the UI thread, in order with the race's events: the
    /// timing thread started practice at the gate, and the Practice page
    /// takes it over (PracticeModeControl.AdoptAutoStartedPractice). The same
    /// console line MaybeAutoStartPracticeOnGatePass writes when it does.
    /// </summary>
    private void AdoptAutoStartedPractice(PracticeAutoStarted notice)
    {
        // A race start took it back before this window got to it: the session
        // is already the race's. One line, nothing to take over.
        if (notice.Cancelled)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [practice] Auto-start cancelled - a race was started.");
            return;
        }

        if (_practiceControl is not { } practice)
        {
            // Never: the plan is only armed with the page there.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [practice] A car started practice at the gate, but there is no Practice page to run it.");
            return;
        }

        if (practice.AdoptAutoStartedPractice(notice))
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [practice] Auto-started - a car crossed the gate.");
        }
    }

    /// <summary>
    /// Threaded mode, on the decoder's own thread: a pass from the decoder
    /// that is the line goes to the timing thread now, without waiting for
    /// the window (RaceTiming.PostLinePass), whatever the race is doing. The
    /// screens' share of it (LinePassSeenOnUi) is still done on the UI
    /// thread. At Idle practice may start on it there, on the timing thread,
    /// when this window has armed it (RefreshPracticeAutoStartPlan) - it no
    /// longer goes round by the window for that (24 Oct 2026, timing layer,
    /// P7). False for any other pass - a split loop's, or one from a decoder
    /// with no role - which takes the UI path it always did.
    /// </summary>
    /// <remarks>
    /// The line is told by _lineDecoder, the copy kept for the decoders'
    /// threads, not by the picker (RoleOf), which only the UI thread may read.
    /// The two differ only for the moment after the line is switched, which
    /// is refused mid-race (RaceUnderWay) and disconnects both decoders.
    /// </remarks>
    private bool SendLinePassToTiming(DirectLapEvent lap)
    {
        if (lap.Decoder != _lineDecoder)
        {
            return false;
        }

        _race.PostLinePass(
            lap,
            uiSide: pass => OnUiThread(() => LinePassSeenOnUi(pass)));

        return true;
    }

    /// <summary>
    /// Threaded mode, on the decoder's own thread: a pass from the split
    /// loop's decoder goes to the timing thread now, on the same queue as
    /// the line's, so the two reach the race in the order they arrived
    /// (RaceTiming.PostSplitPass). False when the split loop is off, or the
    /// pass is from neither decoder in use - which then takes the UI path.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P8): split passes used to wait for the
    /// window while the line's did not, so a freeze closed every lap before
    /// its split read arrived and those sector times were lost. As in
    /// SendLinePassToTiming, the role is told by the copies kept for these
    /// threads (_lineDecoder, _splitLoopOn), not by the picker.
    /// </remarks>
    private bool SendSplitPassToTiming(DirectLapEvent lap)
    {
        if (!_splitLoopOn || _splitReader is null || lap.Decoder != OtherDecoder(_lineDecoder))
        {
            return false;
        }

        PostSplitToTiming(lap.Stamped(lap.Decoder, lap.ReceivedMs, TimingPoint.Split1));
        return true;
    }

    /// <summary>
    /// Threaded mode: a split pass (stamped Split1) to the timing thread.
    /// There it is understood (_splitReader), the UI's split loop is queued
    /// to keep it (console, Recent, the unknown list, Line Up), and it goes
    /// into the race on the row SplitIntoRace finds. Any thread.
    /// </summary>
    private void PostSplitToTiming(DirectLapEvent stamped)
    {
        var reader = _splitReader!;

        _race.PostSplitPass<SplitReading>(
            () => reader.Understand(stamped),
            reading => OnUiThread(() => _splitLoop?.Keep(reading)),
            SplitIntoRace);
    }

    /// <summary>
    /// On the timing thread: the race row a split reading belongs to and
    /// where on the line's clock it crossed - or null while the clocks can't
    /// be tied (a split time that is a guess is worse than none) or the row
    /// can't be told. RowForSplit's rule (SplitRows.RowFor) on the rows'
    /// ids, with the profiles' frozen row lookup.
    /// </summary>
    private (string Row, TimingPoint Point, long LineMs)? SplitIntoRace(SplitReading reading, IReadOnlyList<string> rows) =>
        reading.Pass.LineMs is { } line && SplitRows.RowFor(reading.Pass, rows, _profiles.RowOwnerForAnyThread) is { } row
            ? (row, reading.Pass.Point, line)
            : null;

    /// <summary>
    /// Starts recording every pass and race command to a file (see
    /// TimingRecorder), unless it already is. Safe to call at any time; a
    /// file that can't be opened just means no recording (24 Oct 2026).
    /// </summary>
    private void StartPassRecording()
    {
        if (_race.Recorder is not null)
        {
            return;
        }

        _race.Recorder = TimingRecorder.StartFile(log: SessionLog.Write);

        if (_race.Recorder is null)
        {
            const string Couldnt = "[timing] Pass recording couldn't start - the recordings folder can't be written to.";

            // At startup the console isn't built yet; the session log is.
            if (IsHandleCreated)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  {Couldnt}");
            }
            else
            {
                SessionLog.Write(Couldnt);
            }
        }
    }

    /// <summary>Stops the pass recording, if there is one, and closes its file.</summary>
    private void StopPassRecording()
    {
        var recorder = _race.Recorder;

        if (recorder is null)
        {
            return;
        }

        _race.Recorder = null;
        recorder.Dispose();
    }
}
