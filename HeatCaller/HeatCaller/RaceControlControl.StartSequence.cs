namespace HeatCaller;

/// <summary>
/// Starting and stopping a race: green, waiting for audio, abandoning a
/// running race or a demo, refusals, the warm-up, the automatic start,
/// jump starts, and staggered and handicap start schedules.
///
/// Moved here unchanged from RaceControlControl.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>
    /// Drops the green flag and stamps the session start.
    ///
    /// Split out so the audio wait above can hand back to the UI thread -
    /// the race session and the controls bound to it are not thread-safe.
    /// </summary>
    private void StartGreenNow(bool resuming)
    {
        // Only stamp the session start when this green actually starts the
        // race. Resuming from a yellow/red must not move it, or the saved
        // session would claim it began at the restart.
        if (!resuming)
        {
            _sessionStartedAtUtc = DateTime.UtcNow;
        }

        // The start-of-race work lives in the session's own green-flag gate
        // now - see where BeforeGreenFlagAsync is wired - so every start
        // path gets it whether or not it came through this method.
        _ = _race.GreenFlagAfterAudioAsync();
    }


    /// <summary>
    /// Runs the next step of the start sequence once the current step's
    /// audio has finished.
    ///
    /// A yellow, a red or a reset during the wait must abandon the chain -
    /// the whole point is that it advances a normal start, never that it
    /// overrides a human decision. It used to check the race STATE alone,
    /// and a yellow or red does not change the state: thrown during
    /// staging or the countdown, the chain carried straight on to the green
    /// under it. So the step only runs when all of these still hold:
    ///
    /// - nothing has cancelled it - every flag button that acts, Reset and
    ///   any other manual step cancels through CancelAutoStart (a checkered
    ///   refused before the green does not - see RefuseCheckeredWithoutARace);
    /// - no yellow or red is out (see AutoStartMayContinue);
    /// - it is still the same session - no reset or new stage in between;
    /// - and the race is still at the step this chain was waiting on.
    /// </summary>
    /// <param name="waitingIn">The state this step leaves the race in, which it must still be in for the chain to go on.</param>
    /// <param name="maxWait">
    /// The longest to wait for the audio before moving on anyway. 20 seconds
    /// when not given - enough for any call. Longer only for staging with a
    /// round intro being written (see StagingWaitWithIntro).
    /// </param>
    /// <param name="mutedSeconds">
    /// The format's length for this step, which it lasts at least while the
    /// announcer is muted - see StartStepFloor. Read here, on the UI thread.
    /// </param>
    private void ChainAfterAudio(RaceState waitingIn, Action next, TimeSpan? maxWait = null, int mutedSeconds = 0)
    {
        var wait = maxWait ?? TimeSpan.FromSeconds(20);
        var stepStartedUtc = DateTime.UtcNow;

        // A token of its own, held where CancelAutoStart can reach it. The
        // wait below used to take CancellationToken.None, so pressing a
        // flag button during it cancelled nothing.
        _autoStartCts?.Cancel();
        var cts = new CancellationTokenSource();
        _autoStartCts = cts;
        var token = cts.Token;

        var generation = _race.SessionGeneration;

        void Advance()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (ReferenceEquals(_autoStartCts, cts))
            {
                _autoStartCts = null;
            }

            if (token.IsCancellationRequested || !AutoStartMayContinue(generation, waitingIn))
            {
                return;
            }

            next();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Wait for the current step's audio, then move on promptly.
                //
                // There used to be an extra 400ms here on top of the gate's
                // own settle window, so the chained path paid both and the
                // countdown-to-green gap dragged - noticeably worse than
                // the demo, which pays neither.
                await _announcer.WaitForAudioAsync(wait, token);

                // Muted, there was no audio to wait for - so the step still
                // gets its set length. In here, so a cancel during it is
                // caught by the token check in Advance, as one above is.
                var floor = StartStepFloor(_announcer.Muted, DateTime.UtcNow - stepStartedUtc, mutedSeconds);

                if (floor > TimeSpan.Zero)
                {
                    await Task.Delay(floor, token);
                }
            }
            catch
            {
                // Fall through - a failed wait should not strand a race
                // mid-sequence, and a cancelled one is caught by the token
                // check in Advance.
            }

            if (IsDisposed || Disposing)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(Advance));
                    return;
                }

                Advance();
            }
            catch (InvalidOperationException)
            {
                // The window handle went away between the check above and
                // the post - the page is closing, and there is no start
                // left to advance.
            }
        });
    }

    /// <summary>
    /// How much longer a step of the automatic start waits, after its audio,
    /// before moving on.
    ///
    /// The chain follows the announcer, and a muted announcer has nothing
    /// to follow: every wait returned at once, so Stage went to Countdown to
    /// green in a fraction of a second - the clock running with cars still
    /// being carried to the grid, and a countdown too short to catch a jump
    /// start. So, muted, a step lasts at least the format's Stage (s) or
    /// Countdown (s). Never with the announcer on, where the audio alone
    /// decides, as before.
    /// </summary>
    /// <param name="elapsed">How long the step has lasted so far.</param>
    /// <param name="formatSeconds">The step's length in the race format.</param>
    internal static TimeSpan StartStepFloor(bool muted, TimeSpan elapsed, int formatSeconds)
    {
        if (!muted)
        {
            return TimeSpan.Zero;
        }

        var left = TimeSpan.FromSeconds(Math.Max(0, formatSeconds)) - elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// Asks whether to abandon the race that is running, for a Stage or
    /// Countdown pressed in the middle of it. True when nothing is running,
    /// or the person said yes.
    ///
    /// Public so the host can ask BEFORE it sets up a round and speaks its
    /// grid call - asking afterwards would have already announced a heat
    /// that then does not run. Pass the answer on as
    /// confirmedOverRunningRace so it is not asked twice.
    /// </summary>
    /// <param name="action">What is about to happen, as it reads after "If you" - "stage a new race".</param>
    public bool ConfirmAbandonRunningRace(string action)
    {
        if (_race.State != RaceState.Racing)
        {
            return true;
        }

        // What was running when the question was put. The dialog is modal,
        // but its message loop still runs everything posted to this thread
        // - a phone remote's Checkered, Reset or Stage, a demo finishing -
        // so by the time it is answered the race it asked about may be gone.
        var isDemo = _race.IsDemoMode;
        var generationAsked = _race.SessionGeneration;
        var stateAsked = _race.State;

        // A demo is a rehearsal: nothing is lost by ending it, but the
        // question still has to say what is being ended. It used to say "a
        // race is running" over a demo.
        var answer = isDemo
            ? AppDialog.Show(this,
                $"A demo is running. If you {action} now, the demo ends: it is not saved to Race History "
                + "(even with \"Save demo races to Race History\" ticked), and Undo does not bring it back.\n\n"
                + "End the demo?",
                "Demo in progress",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question)
            : AppDialog.Show(this,
                $"A race is running. If you {action} now, that race is abandoned: every lap it has counted is "
                + "cleared from the board, and it is not saved to Race History.\n\n"
                + "To end the race and keep its result, choose No and use Checkered instead.\n\n"
                + "If you go ahead by mistake, Undo puts the race back - as long as the new race has not gone green.\n\n"
                + "Abandon the running race?",
                "Race in progress",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
        {
            StatusMessage?.Invoke(isDemo
                ? $"{AppTime.LogStampNow()}  [race] Did not {action} - the demo was left running."
                : $"{AppTime.LogStampNow()}  [race] Did not {action} - the running race was left as it is.");
            NoteRemoteRefusal(isDemo
                ? "The laptop said no - the demo was left running."
                : "The laptop said no - the running race was left as it is.");
            return false;
        }

        // The yes was for the race that was running when it was asked. If
        // that race has since finished, been reset, been replaced, or a
        // demo has ended, the answer no longer applies - acting on it
        // anyway staged over whatever is there now, and captured an undo
        // of a race that was never the one agreed to.
        if (_race.SessionGeneration != generationAsked || _race.State != stateAsked || _race.IsDemoMode != isDemo)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [race] Did not {action} - the session changed while the question was open "
                + $"(it is now {_race.State}). Nothing was done; do it again if it is still wanted.");
            NoteRemoteRefusal("Not done - the race changed while the laptop was asking about it.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Ends the demo on the Demo page, wired by the host. Race Control calls
    /// it through <see cref="StopDemoForTakeover"/> just before it stages,
    /// starts or resets over a demo, so the demo lets go of the session
    /// instead of carrying on inside the real race.
    /// </summary>
    public Action? StopRunningDemo { get; set; }

    /// <summary>
    /// Ends a demo that is driving the session so Race Control can take it
    /// over, and says so. True when there was one.
    ///
    /// Stage, Countdown, Green and Reset used to guard against practice
    /// only. Over a demo, the simulator kept injecting fake laps into the
    /// real race and the session kept its demo flag, so the real race was
    /// taken for a demo - not saved, or saved as [DEMO], and never written
    /// to the crash journal - and a later Stop on the Demo page reset it.
    ///
    /// Public so the host can end the demo BEFORE it sets up a round and
    /// speaks its grid call: ending it clears the demo's backlog of queued
    /// lap calls, which would take the grid call with it if it came after.
    /// </summary>
    /// <param name="action">What Race Control is about to do, as it reads after "so Race Control can" - "stage a race".</param>
    public bool StopDemoForTakeover(string action)
    {
        if (!_race.IsDemoMode)
        {
            return false;
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Demo ended so Race Control can {action} - a demo ended this way is not saved to Race History.");

        if (StopRunningDemo is { } stopDemo)
        {
            stopDemo();
        }
        else
        {
            // Not wired: the simulator still lets go by itself once the demo
            // flag is cleared (below) or the session moves on, but its backlog
            // of lap calls is not cleared for it - so clear it here.
            _announcer.StopAllPendingAudio();
        }

        // Whatever the host did, the next race is not a demo - and is not
        // run for the round the demo was (see DemoRound).
        _race.IsDemoMode = false;
        _currentRaceIsDemo = false;
        DemoRound = null;
        _demoTakenOverGeneration = _race.SessionGeneration;
        return true;
    }

    /// <summary>
    /// The session generation in which a demo was last ended by a takeover,
    /// or -1. The host ends a demo itself before a program start, so by the
    /// time Stage runs there is no demo left for it to see - but the race
    /// still showing is that demo's, and it must not be offered back as an
    /// undo (it would return as a "real" race with nothing driving it).
    ///
    /// Used up by the Race Control action that takes the session over - see
    /// EndDemoForTakeover.
    /// </summary>
    private int _demoTakenOverGeneration = -1;

    /// <summary>
    /// Ends a running demo, or reports that one was just ended in this same
    /// session by the host - either way the race on screen is a demo's and
    /// is not captured for undo.
    ///
    /// Only Stage, Countdown, a first Green and Reset (and Reset Round) call
    /// this, and each of them IS the takeover, so the note is used up here:
    /// from this action on, the race on the board is that action's. It used
    /// to stand until the session generation moved on, which Stage, the
    /// green and Reset do but a countdown does not - so a Reset pressed
    /// straight after a Countdown had taken a demo over still read the race
    /// as the demo's and could not be undone, although by then it was a
    /// real race's countdown. The same went for a Reset during a green's
    /// wait for the countdown call. A Reset that ends a demo that is still
    /// running is unchanged: StopDemoForTakeover finds the demo, and that
    /// Reset is still not undoable.
    /// </summary>
    private bool EndDemoForTakeover(string action)
    {
        var endedDemo = StopDemoForTakeover(action) || _demoTakenOverGeneration == _race.SessionGeneration;

        _demoTakenOverGeneration = -1;
        return endedDemo;
    }

    /// <summary>
    /// Captures an undo for a race that Stage or Countdown is about to
    /// abandon. Called before anything is changed - the format and
    /// jump-start rules included, since the new race writes its own.
    ///
    /// Only good until the new race goes green: after that there is a new
    /// race with its own laps, and putting the old one back over it would
    /// lose those instead. The same rule as undoing Reset.
    /// </summary>
    /// <param name="generationAfter">The session generation once the new start has been made, read when Undo is pressed.</param>
    /// <param name="restoreHostOnUndo">
    /// Puts back what the HOST changed for the new race - the active round,
    /// its track and label - captured by the host before it changed them.
    /// Without it, undoing a program start restored the race but left the
    /// new round selected, so the restored race's result was filed under the
    /// wrong heat and that heat was marked as run.
    /// </param>
    private void CaptureAbandonedRaceUndo(string description, Func<int> generationAfter, Action? restoreHostOnUndo = null)
    {
        var snapshot = _race.CreateSnapshot();

        // When the race stopped counting, so the undo can leave the time it
        // was gone off the race clock - see RestoreSnapshotAfterGap.
        var snapshotTakenUtc = DateTime.UtcNow;
        var sessionStart = _sessionStartedAtUtc;
        var prompted = _promptedTransponders.ToList();

        // What kind of race it was: Stage clears these for the new race, and
        // a Quick Race put back without them was filed against the next heat
        // by auto-advance at the flag.
        var wasQuick = _currentRaceIsQuick;
        var wasRerun = _currentRaceIsRerun;

        // The rules the abandoned race was running under. The new start
        // writes its own format over these, and the race put back must not
        // finish under the next race's lap limit.
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

        // Which session this undo is good for - moved on by a Stage pressed
        // again before the green, see StageKeepingAbandonedRaceUndo.
        var goodFor = new AbandonedRaceUndo { Generation = generationAfter };
        _abandonedRaceUndo = goodFor;

        _undo.Capture(description, () =>
        {
            if (_race.SessionGeneration != goodFor.Generation()
                || _race.State is not (RaceState.Staging or RaceState.Countdown))
            {
                throw new InvalidOperationException(
                    "the new race has already gone green (or been reset), and undoing now would overwrite it. "
                    + "Nothing was changed.");
            }

            if (ReferenceEquals(_abandonedRaceUndo, goodFor))
            {
                _abandonedRaceUndo = null;
            }

            // Or the start sequence would carry on from under the race
            // that has just been put back.
            CancelAutoStart();

            // The host's side FIRST, while the session is still at the new
            // race's Staging or Countdown (checked above). RestoreSnapshot
            // below raises Racing, and what listens for it reads the host's
            // setup there and then: the run sheet stamps the active round as
            // started, and the ghosts start on the selected track. Put back
            // afterwards, the new round - which never went green - was
            // stamped as started, and the ghosts ran on its track.
            //
            // Safe to do before the limits: at Staging or Countdown the
            // format re-read a round change triggers only refreshes the
            // format line (ApplyEffectiveFormat writes to the session at
            // Idle or Finished only), and the limits are written back
            // straight after in any case. In a try, so the race still comes
            // back if this goes wrong.
            if (restoreHostOnUndo is not null)
            {
                try
                {
                    restoreHostOnUndo();
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [race] The race is being put back, but its round, track and label could not be: {ex.Message} "
                        + "Check the Round box before the race finishes.");
                }
            }

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
            _currentRaceIsQuick = wasQuick;
            _currentRaceIsRerun = wasRerun;
            _promptedTransponders.Clear();
            foreach (var t in prompted)
            {
                _promptedTransponders.Add(t);
            }

            _previousFlag = _race.Flag;

            // The format line (and the auto-advance setting read from it)
            // back to the restored race's round. Racing, so nothing is
            // written to the session - see ApplyEffectiveFormat.
            ApplyEffectiveFormat();

            WriteJournal(force: true);

            // Staging reset the announcer - see SeedAnnouncerFromRace.
            SeedAnnouncerFromRace();
            RenderLeaderboard();
            UpdateClockDisplay(force: true);

            // Held under red if it was running - see RestoreSnapshotAfterGap
            // - so the director is told why, and what to press.
            if (heldUnderRed)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [race] The race is back, held under a red flag - no laps were counted while it was gone. "
                    + "Green when the cars are ready to go again.");
            }

            NoteStartPutBack();
        });
    }

    /// <summary>The session generation an abandoned-race undo is good for, read when Undo is pressed.</summary>
    private sealed class AbandonedRaceUndo
    {
        public required Func<int> Generation { get; set; }
    }

    /// <summary>The newest abandoned-race undo (CaptureAbandonedRaceUndo), or null once it has been used.</summary>
    private AbandonedRaceUndo? _abandonedRaceUndo;

    /// <summary>
    /// Stages the session for the Stage button, and keeps an abandoned
    /// race's undo good when this Stage only restarts a start that has not
    /// gone green.
    ///
    /// Bug hunt 17: every Stage moves the session generation on, and the
    /// undo was good for the generation of the FIRST Stage only. So after
    /// "Abandon the running race?" was answered Yes by mistake, a second
    /// Stage - F1 pressed twice, a second "stage" from the phone, or Stage
    /// after a Countdown over the race - made Undo refuse with "the new race
    /// has already gone green", although no green had been thrown and there
    /// was nothing to overwrite. The abandoned race's laps were gone.
    /// </summary>
    /// <param name="stateBefore">The state Stage was pressed in.</param>
    /// <param name="tookOverDemo">Whether this Stage ended a demo - the start it replaces is then the demo's.</param>
    private void StageKeepingAbandonedRaceUndo(RaceState stateBefore, bool tookOverDemo)
    {
        var undo = _abandonedRaceUndo;
        var keeps = undo is not null
                    && RestageKeepsAbandonedRaceUndo(stateBefore, tookOverDemo, _race.SessionGeneration, undo.Generation());

        _race.Stage();

        if (keeps)
        {
            var restaged = _race.SessionGeneration;
            undo!.Generation = () => restaged;
        }
    }

    /// <summary>
    /// Whether a Stage leaves an abandoned race's undo good: only when it
    /// is pressed during the staging or countdown of the very start that
    /// undo was waiting on, which has therefore not gone green or been
    /// reset (both move the generation on, and leave another state).
    /// </summary>
    /// <param name="generationBefore">The session generation as Stage was pressed.</param>
    /// <param name="undoGeneration">The generation the abandoned-race undo is good for.</param>
    internal static bool RestageKeepsAbandonedRaceUndo(RaceState stateBefore, bool tookOverDemo, int generationBefore, int undoGeneration) =>
        !tookOverDemo
        && stateBefore is (RaceState.Staging or RaceState.Countdown)
        && undoGeneration == generationBefore;

    /// <summary>
    /// After an undo has put a race back: tells the director, when it came
    /// back at Staging or Countdown, that the start will not carry on by
    /// itself.
    /// </summary>
    private void NoteStartPutBack()
    {
        if (StartPutBackNotice(_race.State) is { } notice)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] {notice}");
        }
    }

    /// <summary>
    /// What the director is told when an undo puts a race back at Staging or
    /// Countdown; null for any other state.
    ///
    /// Bug hunt 15 (9 Nov 2026): the start sequence running then - the
    /// automatic start's chain, or the green armed after the countdown - was
    /// cancelled by the Reset and is not put back, so the restored start
    /// would just sit there. Its calls are not replayed either (see the
    /// _suppressFlagAnnouncements checks in OnRaceStateChanged): the
    /// countdown ticks used to play again from the start and end on no green.
    /// </summary>
    internal static string? StartPutBackNotice(RaceState state) => state switch
    {
        RaceState.Staging =>
            "The race is back at Staging. The start does not carry on by itself - press Countdown when the cars are ready.",
        RaceState.Countdown =>
            "The race is back at Countdown, but no countdown is running and no green will drop by itself. "
            + "Press Countdown to run the start again, or Green to start the race now.",
        _ => null,
    };

    /// <summary>
    /// True - after saying why - when open practice is running.
    ///
    /// Practice runs on the same race session as this page, in the Racing
    /// state, so Stage, Countdown or Green from here would take it over:
    /// Stage clears every lap the drivers have done, and the page would
    /// then treat the practice as a race. Refused outright rather than
    /// asked about, because the Practice page has its own save and
    /// teardown that stopping it there runs and taking it over here would
    /// skip.
    ///
    /// Public so the host can check before the programme or the Next Up
    /// bar sets up a round and speaks its grid call.
    /// </summary>
    /// <param name="why">
    /// Why, for the box - when not given, that it would end the practice or
    /// wipe its laps, which is not what a yellow or red would do (see
    /// NoFlagsInPractice).
    /// </param>
    public bool RefuseIfPracticeRunning(string action, string? why = null)
    {
        if (!_race.IsPracticeMode)
        {
            return false;
        }

        CancelAutoStart();

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Can't {action} - practice is running. Stop practice first on the Practice page.");

        // From the phone remote the reason goes back to the phone instead.
        // The box used to go up on the laptop regardless - in front of
        // whoever was there, for a press nobody there had made - while the
        // phone, told "sent", never heard that it was refused.
        if (_runningRemoteCommand)
        {
            NoteRemoteRefusal("Practice is running - stop it on the Practice page first.");
            return true;
        }

        AppDialog.Show(this,
            $"Practice is running, so HeatCaller can't {action} yet - {why ?? "it would end the practice or wipe its laps"}.\n\n"
            + "Stop practice first on the Practice page, then try again.",
            "Practice is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }

    /// <summary>
    /// RefuseIfPracticeRunning for anything that starts a race (Stage,
    /// Countdown, Green, On Air, a heat from the program, a demo): first, in
    /// threaded mode, a practice the timing thread started at the gate that
    /// this window has not taken over yet is handed back - the race wins -
    /// and no car can start one until this click is over. Inline that is a
    /// no-op and this is RefuseIfPracticeRunning exactly.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P7): see RaceTiming.
    /// TakeBackPracticeAutoStartForRace. A practice this window already runs
    /// is still refused here, as it always was.
    /// </remarks>
    public bool RefuseIfPracticeRunningForRaceStart(string action)
    {
        _race.TakeBackPracticeAutoStartForRace();
        return RefuseIfPracticeRunning(action);
    }

    /// <summary>Why a yellow or red is refused during practice, for RefuseIfPracticeRunning's box.</summary>
    private const string NoFlagsInPractice = "practice has no flags, and nothing could clear one until practice stopped";

    /// <summary>
    /// True - after saying why - when there is no race running for the
    /// checkered flag to end: nothing staged, a start still on its way to the
    /// green, or a race that has already finished.
    ///
    /// The checkered used to finish the session whatever state it was in, and
    /// a finish is what saves - see RaceControlSession.CheckeredFlag for what
    /// that did. It is refused here first, so that whoever pressed it hears
    /// why and nothing at all is touched.
    ///
    /// A start is left exactly as it was, its automatic green included. A
    /// refused press has done nothing, so it overrides nothing: a mis-hit F6
    /// must not stall a start the room is waiting on, and a checkered is not
    /// how a start is called off - Reset is, or a red holds it, and the
    /// refusal says so.
    ///
    /// Said in the log rather than in a box. A box put up during a start
    /// would sit in front of the flag buttons and the function keys just as
    /// the green dropped - the reason the tech warning at staging is not a
    /// box either. From the phone remote, the reason goes back to the phone.
    /// </summary>
    private bool RefuseCheckeredWithoutARace()
    {
        if (CheckeredRefusal(_race.State) is not { } reason)
        {
            return false;
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Checkered not thrown. {reason}");
        NoteRemoteRefusal(reason);
        return true;
    }

    /// <summary>
    /// Why the checkered flag can't be thrown with the race in this state, in
    /// a sentence short enough for a phone - or null when it can, which is
    /// only while a race is running.
    ///
    /// Public for the race-day manager, which has to refuse before it hands
    /// a flag over, as it cannot hear a refusal afterwards - so the two never
    /// disagree about the rule or the words.
    /// </summary>
    public static string? CheckeredRefusal(RaceState state)
    {
        if (state == RaceState.Racing)
        {
            return null;
        }

        return state switch
        {
            RaceState.Staging or RaceState.Countdown => "The race hasn't gone green yet. To call off this start, press Reset.",
            RaceState.Finished => "The race has already finished.",
            _ => "No race is running.",
        };
    }

    /// <summary>
    /// Why a green can't be thrown with the race in this state and under this
    /// flag, in a sentence short enough for a phone - or null when it can.
    ///
    /// The one refusal is a race on its last lap, under the checkered: a green
    /// there cannot take the flag back in (see RaceControlSession.GreenFlag).
    /// A green over a green is not a refusal - nothing needs doing, and the
    /// Green action simply leaves it. Public for the race-day manager, for
    /// the same reason as <see cref="CheckeredRefusal"/>.
    /// </summary>
    public static string? GreenRefusal(RaceState state, FlagState flag) =>
        state == RaceState.Racing && flag == FlagState.Checkered
            ? "The checkered flag is out - the race is finishing."
            : null;

    /// <summary>
    /// Cancels any auto-start sequence currently waiting between phases.
    /// Called at the top of every manual race-control button so a human
    /// clicking any button - including Stage or Countdown themselves -
    /// always overrides a pending automated step rather than racing against it.
    /// </summary>
    private void CancelAutoStart()
    {
        // WHY (24 Oct 2026, timing layer, P8): threaded, the automatic start's
        // green waits on the timing tick, not on _autoStartCts - so it is
        // dropped here too, first. A manual Green during the countdown then
        // greens as it always did. Inline: does nothing.
        _race.DisarmGreenAfterCountdown();

        _autoStartCts?.Cancel();
        _autoStartCts = null;

        // 17 Oct 2026, Bug Hunt 8, A03-2/A03-4: On Air is called off too.
        // Reset, a flag, Stage or a warm-up opening by hand moved the race
        // on, and staging is theirs - before this a Reset left On Air
        // running and it staged a freeform race at its end, and a warm-up
        // opened during it was skipped by its hand-over. On Air's own
        // hand-over is unaffected: _onAirCts is cleared before it runs, and
        // its second press does not come through here.
        if (_onAirCts is { } onAir)
        {
            _onAirStageNow = false;

            try
            {
                _ = onAir.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // On Air had already finished.
            }

            // And what it had queued stops now (1 Nov 2026), or the staging
            // call this press is about to queue waits behind the rest of the
            // opening - see SilenceOnAir.
            SilenceOnAir("On Air called off");
        }
    }

    // ==================== Warm-up ====================

    /// <summary>
    /// Opens the warm-up for the race about to run, if its format has one,
    /// and arranges for <paramref name="then"/> to run when it is over.
    /// Returns false - and does nothing - when there is no warm-up to run:
    /// the format has none, practice is on, or a race is already staged,
    /// counting down or running.
    /// </summary>
    /// <param name="forQuickRace">True when Quick Race is opening it - see _warmUpOpenedByQuickRace.</param>
    /// <param name="force">
    /// True for the Warm-up button (14 Nov 2026): a warm-up even when the
    /// format has none, for <see cref="WarmUpWhenFormatHasNone"/>. The footer's
    /// "Warm-up first" switch does the same for every start - see
    /// <see cref="StartWithWarmUp"/>.
    /// </param>
    private bool TryBeginWarmUp(Action then, bool forQuickRace = false, bool force = false)
    {
        // Threaded, a warm-up is a race start too (24 Oct 2026, timing
        // layer, P7) - see RefuseIfPracticeRunningForRaceStart.
        _race.TakeBackPracticeAutoStartForRace();

        if (_race.IsPracticeMode || _race.State is not (RaceState.Idle or RaceState.Finished))
        {
            return false;
        }

        var seconds = WarmUpSecondsFor(EffectiveFormat(out _).WarmUpSeconds, force || StartWithWarmUp, _settings.RaceWarmUpFirstSeconds);
        if (seconds <= 0)
        {
            return false;
        }

        // Nothing of an earlier warm-up (or a half-started sequence) may
        // carry on underneath this one.
        CancelAutoStart();
        CancelWarmUp();

        var length = TimeSpan.FromSeconds(seconds);
        if (!_race.BeginWarmUp(length))
        {
            return false;
        }

        var cts = new CancellationTokenSource();
        _warmUpCts = cts;
        _afterWarmUp = then;
        _warmUpOpenedByQuickRace = forQuickRace;

        var described = RaceFormat.DescribeWarmUp(seconds);
        _announcer.SpeakReminder($"Warm-up is open. {SpokenWarmUpLength(seconds)} of free laps before the race.");
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Warm-up open - {described}. Laps are not counted. Press Warm-up or Stage again to skip the rest.");

        _lblRaceState.Text = "Warm-up";
        UpdateClockDisplay(force: true);

        _ = RunWarmUpTimerAsync(cts, length);
        return true;
    }

    /// <summary>
    /// Waits out the warm-up on the UI thread's context (so everything after
    /// each await is back on it), with a thirty-second call when there is
    /// time for one, then moves on.
    /// </summary>
    private async Task RunWarmUpTimerAsync(CancellationTokenSource cts, TimeSpan length)
    {
        var warning = TimeSpan.FromSeconds(30);

        try
        {
            if (length > TimeSpan.FromSeconds(60))
            {
                await Task.Delay(length - warning, cts.Token);

                if (ReferenceEquals(_warmUpCts, cts) && _race.IsWarmingUp)
                {
                    _announcer.SpeakReminder("Thirty seconds left in warm-up.");
                }

                await Task.Delay(warning, cts.Token);
            }
            else
            {
                await Task.Delay(length, cts.Token);
            }
        }
        catch (TaskCanceledException)
        {
            return; // skipped, reset, or replaced
        }

        if (IsDisposed || Disposing || !ReferenceEquals(_warmUpCts, cts))
        {
            return;
        }

        FinishWarmUp(skipped: false);
    }

    /// <summary>
    /// Ends the warm-up and runs what was waiting on it - but only if the
    /// warm-up was still the thing going on. If somebody pressed Countdown
    /// or Green (or the phone did) in the meantime, the session already
    /// moved on, and staging over the top of that would be wrong.
    /// </summary>
    private void FinishWarmUp(bool skipped)
    {
        var then = _afterWarmUp;
        var wasWarmingUp = _race.IsWarmingUp;

        _warmUpCts?.Cancel();
        _warmUpCts = null;
        _afterWarmUp = null;

        // WHY (24 Oct 2026, timing layer, P7): threaded, the cars still lapping
        // when the warm-up ends must not start practice at the gate in the
        // moment between it ending and the Stage below - the race is held for
        // first. Taken before the warm-up ends, while the session cannot have
        // a gate practice; a no-op inline.
        _race.TakeBackPracticeAutoStartForRace();

        _race.EndWarmUp(skipped ? "Warm-up skipped." : "Warm-up over.");

        if (!wasWarmingUp || then is null || _race.State is not (RaceState.Idle or RaceState.Finished))
        {
            return;
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] {(skipped ? "Warm-up skipped" : "Warm-up over")} - staging.");

        _warmUpJustFinished = true;
        try
        {
            then();
        }
        finally
        {
            _warmUpJustFinished = false;
        }
    }

    /// <summary>Stage or Quick Race pressed during the warm-up: cut it short and start. True when there was one to skip.</summary>
    private bool SkipWarmUpIfRunning()
    {
        if (_warmUpCts is null || !_race.IsWarmingUp)
        {
            return false;
        }

        FinishWarmUp(skipped: true);
        return true;
    }

    /// <summary>Drops the warm-up without running what was waiting on it - Reset, or the page closing.</summary>
    private void CancelWarmUp()
    {
        _warmUpCts?.Cancel();
        _warmUpCts = null;
        _afterWarmUp = null;
        _race.EndWarmUp("Warm-up canceled.");
    }

    /// <summary>
    /// How long a warm-up runs when the format has none but one was asked for
    /// anyway - the Warm-up button, or the "Warm-up first" switch (14 Nov
    /// 2026): two minutes, the length most clubs give.
    /// </summary>
    internal const int WarmUpWhenFormatHasNone = 120;

    /// <summary>
    /// The warm-up to run, in seconds: the format's own, else
    /// <see cref="WarmUpWhenFormatHasNone"/> when one is wanted regardless,
    /// else none (0). Never past RaceFormat.MaxWarmUpSeconds.
    /// </summary>
    /// <param name="chosenSeconds">
    /// The length picked on the switch's right-click menu (15 Nov 2026), 0
    /// for none: when a warm-up is wanted it wins over the format's own.
    /// </param>
    internal static int WarmUpSecondsFor(int formatSeconds, bool wanted, int chosenSeconds = 0)
    {
        if (wanted && chosenSeconds > 0)
        {
            return Math.Min(chosenSeconds, RaceFormat.MaxWarmUpSeconds);
        }

        var seconds = Math.Clamp(formatSeconds, 0, RaceFormat.MaxWarmUpSeconds);
        return seconds > 0 ? seconds : wanted ? WarmUpWhenFormatHasNone : 0;
    }

    /// <summary>
    /// The Warm-up button (14 Nov 2026): opens the warm-up flag - timed free
    /// laps, then Stage, then the countdown. While the warm-up is open it
    /// skips the rest and stages, as Stage does. Only between races; over a
    /// staged or running race it says so and does nothing.
    /// </summary>
    private void PressWarmUp()
    {
        if (RefuseIfPracticeRunningForRaceStart("open a warm-up"))
        {
            return;
        }

        // Lit: the rest of the warm-up skipped, and on to Stage.
        if (SkipWarmUpIfRunning())
        {
            return;
        }

        if (OnAirRunning)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Warm-up: On Air is opening the race already - "
                + "it stages when it is done. Tick Warm-up first at the foot of the board to warm up before every race.");
            return;
        }

        if (_race.State is not (RaceState.Idle or RaceState.Finished))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Warm-up: a race is already {_race.State.ToString().ToLowerInvariant()} - "
                + "a warm-up is only between races. Reset first to warm up.");
            return;
        }

        if (!TryBeginWarmUp(() => _actStage?.Invoke(false, null), force: true))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Warm-up: could not open one now.");
        }
    }

    /// <summary>The warm-up length as the announcer should say it.</summary>
    private static string SpokenWarmUpLength(int seconds)
    {
        var minutes = seconds / 60;
        var rest = seconds % 60;

        if (minutes == 0)
        {
            return Plural.Of(rest, "second");
        }

        return rest == 0
            ? Plural.Of(minutes, "minute")
            : $"{Plural.Of(minutes, "minute")} and {Plural.Of(rest, "second")}";
    }

    /// <summary>
    /// The session's warm-up opened or closed - from here, or because
    /// something moved the race on. Keeps the state label and the lit
    /// Stage button honest, and lets go of a timer whose warm-up is gone.
    /// </summary>
    private void OnWarmUpChanged()
    {
        if (!_race.IsWarmingUp && _warmUpCts is not null)
        {
            _warmUpCts.Cancel();
            _warmUpCts = null;
            _afterWarmUp = null;
        }

        if (IsDisposed || Disposing)
        {
            return;
        }

        _lblRaceState.Text = _race.IsWarmingUp ? "Warm-up" : _race.State.ToString();
        UpdateClockDisplay(force: true);
    }

    /// <summary>
    /// How long until the green, while the automatic start is opening its
    /// countdown - the one path that knows. Null otherwise: a green pressed
    /// by hand could come at any moment, and one that follows the
    /// countdown's audio comes when the ticks end anyway.
    /// </summary>
    private TimeSpan? _countdownGreenIn;

    /// <summary>
    /// Runs Stage -> (wait) -> Countdown -> (wait) -> Green as one sequence
    /// instead of three separate manual clicks. Cancellable at any point by
    /// CancelAutoStart - if the person clicks any manual race-control button
    /// while a wait is in progress, the sequence simply stops where it is
    /// rather than firing a stale step on top of what they just did. (Not a
    /// checkered refused before the green, which does nothing - see
    /// RefuseCheckeredWithoutARace.)
    /// </summary>
    /// <summary>
    /// The longest the countdown waits for the staging cue to finish.
    ///
    /// Ten seconds covers every stock staging sound and a club's own
    /// recording of somebody saying "cars to the grid". It is deliberately
    /// NOT the twenty ChainAfterAudio allows: that one is waiting out a
    /// spoken line before a flag, and this is holding up the start of a
    /// race while a room stands waiting.
    /// </summary>
    private static readonly TimeSpan LongestStagingCue = TimeSpan.FromSeconds(10);

    private async Task RunAutoStartSequenceAsync()
    {
        CancelAutoStart();
        var cts = new CancellationTokenSource();
        _autoStartCts = cts;

        try
        {
            _race.Stage();
            var generation = _race.SessionGeneration;
            await Task.Delay(TimeSpan.FromSeconds(_fmt.StagingSeconds), cts.Token);

            // AND THEN LET THE STAGING CUE FINISH (13 Oct 2026), which is
            // the same fault the countdown-to-green seam below already had
            // and the same fix. The delay above is the club's configured
            // staging length and has no relationship to how long the
            // staging sound actually plays - so a club whose staging cue is
            // longer than its staging seconds heard "bring your cars to the
            // grid" cut off mid-word by the countdown, every single race.
            //
            // It showed up first on a demo, where the staging seconds come
            // from a spinner that can be set to one, but it is not a demo
            // bug: a real race with StagingSeconds short enough does the
            // same thing.
            //
            // BOUNDED, because a club is free to point its staging sound at
            // a thirty-second bed and the start must not wait that out. Past
            // the ceiling the cue IS cut, as before - the difference is that
            // it now only happens to a sound genuinely too long to wait for
            // rather than to every sound that outlasts a short timer.
            await _announcer.WaitForAudioAsync(LongestStagingCue, cts.Token);

            // The same test as the chained start (see ChainAfterAudio): a
            // flag thrown, or the session changed, from anywhere that does
            // not go through CancelAutoStart still stops the sequence.
            //
            // Checked AFTER the wait as well as the delay, because the wait
            // is more time in which somebody can throw a flag or reset.
            if (!AutoStartMayContinue(generation, RaceState.Staging))
            {
                return;
            }

            // The countdown ticks end on the green, so the announcer is told
            // when that is - for this one call only. The state change that
            // passes it on runs inside StartCountdown, on this thread.
            _countdownGreenIn = TimeSpan.FromSeconds(_fmt.CountdownSeconds);
            try
            {
                _race.StartCountdown();
            }
            finally
            {
                _countdownGreenIn = null;
            }

            // WHY (24 Oct 2026, timing layer, P8): threaded, the green is handed
            // to the timing tick now, due when the countdown ends, so a frozen
            // window cannot hold it while passes are booked as jump starts. This
            // path's green never re-applied the format, and the engine's gate
            // already waits for the audio, so neither is done here. Inline
            // (false), the wait below runs as it always did.
            if (ArmAutoGreen(TimeSpan.FromSeconds(_fmt.CountdownSeconds), waitForAudio: false, asTheGreenButton: false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(_fmt.CountdownSeconds), cts.Token);

            if (!AutoStartMayContinue(generation, RaceState.Countdown))
            {
                return;
            }

            // Let the countdown cue finish before the clock starts. The
            // delay above is just the configured countdown length and has
            // no relationship to how long the sound actually plays, so the
            // race used to go green while the cue was still sounding.
            _sessionStartedAtUtc = DateTime.UtcNow;

            // Nothing start-of-race here either: the gate below does it for
            // every path. This method is a second implementation of
            // StartGreenNow, and duplicating that work is what left a demo
            // with no release schedule.
            //
            // The session's own gate waits for audio - no separate wait here,
            // or the cue would be waited on twice.
            await _race.GreenFlagAfterAudioAsync();
        }
        catch (TaskCanceledException)
        {
            // Superseded by a manual button press or a second auto-start click - nothing further to do.
        }
        finally
        {
            if (_autoStartCts == cts)
            {
                _autoStartCts = null;
            }
        }
    }

    /// <summary>Threaded mode: the automatic start's green handed to the timing thread (RaceTiming.ArmGreenAfterCountdown), with what the Green button does first for a green from the countdown done now. False inline - the caller keeps its own path.</summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P8): see ArmGreenAfterCountdown. The rest
    /// of what _actGreen does is not needed for a green from the countdown:
    /// practice cannot be running in Countdown, On Air ended at Stage, the
    /// state is not Racing (so neither the already-green nor the GreenRefusal
    /// check applies), Stage already ended any demo, the jump-start rules are
    /// not re-applied from Countdown, and nothing is left for CancelAutoStart.
    /// </remarks>
    private bool ArmAutoGreen(TimeSpan notBefore, bool waitForAudio, bool asTheGreenButton)
    {
        if (!_race.IsThreaded)
        {
            return false;
        }

        if (asTheGreenButton)
        {
            // What _actGreen does for a first green from the countdown (BuildGreenButton).
            ApplyEffectiveFormat(startingNewRace: true);
            _currentRaceIsRerun |= _rerunPending;
            _rerunPending = false;
        }

        // Stamped by the green itself on the timing side (RaceJournalRecorder's
        // "??=" at Racing), so nothing from an abandoned race may be left in it.
        _sessionStartedAtUtc = null;

        return _race.ArmGreenAfterCountdown(notBefore,
            waitForAudio ? token => _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(20), token) : null,
            TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// Whether an automatic start may take its next step: still the same
    /// session, still at the step it was waiting on, and no yellow or red
    /// out.
    ///
    /// The flag has to be checked on its own. A yellow or red does not
    /// change the race state, so a start that checked the state alone went
    /// green under a caution thrown during staging or the countdown.
    ///
    /// Only a caution stops it, not "any flag": a countdown started
    /// straight from a finished race still shows that race's checkered,
    /// since only Stage clears the flag there - and that start must still go
    /// green.
    ///
    /// A countdown started over a race that is running clears that race's
    /// flag (see RaceControlSession.StartCountdown), so a yellow or red seen
    /// here was thrown during this start. It used to read the abandoned
    /// race's own yellow as one, and a countdown confirmed over a race under
    /// caution stopped before the green every time.
    ///
    /// Bug hunt 14 (7 Nov 2026): so does a countdown started straight from a
    /// finished or idle session, for a yellow or red left out from before it
    /// (thrown after the finish with F4/F5 or from the phone). That one also
    /// used to be read here as a caution thrown during this start, and the
    /// start stalled. A checkered left from the finished race is still kept.
    /// </summary>
    private bool AutoStartMayContinue(int generation, RaceState waitingIn)
    {
        if (IsDisposed || Disposing
            || _race.SessionGeneration != generation
            || _race.State != waitingIn)
        {
            // Somebody did something - a reset, a new stage, a start by
            // hand. That decision stands.
            return false;
        }

        if (_race.Flag is FlagState.Yellow or FlagState.Red)
        {
            // Said, because otherwise the start just seems to stall.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [race] Automatic start stopped at {waitingIn} - the {_race.Flag.ToString().ToLowerInvariant()} flag is out. "
                + "Press Green when the track is ready, or Stage again to restart the sequence.");
            return false;
        }

        return true;
    }


    /// <summary>Announces a jump start, and refreshes so the flag shows on the leaderboard.</summary>
    private void OnJumpStart(string transponder, long penaltyMs)
    {
        _announcer.NotifyJumpStart(transponder, penaltyMs);
        RenderLeaderboard();
    }

    /// <summary>
    /// Pushes the racing class's jump-start rules into the session. The
    /// session is class-agnostic, so this has to happen each time a race
    /// starts rather than once at construction.
    /// </summary>
    private void ApplyJumpStartRules()
    {
        // WHY (23 Oct 2026, bug hunt 10): read only the Round box, so a demo
        // of an already-run round (box on freeform, DemoRound set) ran on the
        // default jump-start rules, not the class's. ShowRound is the same
        // round EffectiveFormat already took the start type from.
        var round = ShowRound();
        var raceClass = round is not null && _raceClasses is not null
            ? _raceClasses.All.FirstOrDefault(rc => string.Equals(rc.Name, round.ClassName, StringComparison.OrdinalIgnoreCase))
            : null;

        _race.DetectJumpStarts = raceClass?.DetectJumpStarts ?? true;
        _race.JumpStartPenaltyMs = Math.Max(0, (raceClass?.JumpStartPenaltySeconds ?? 0) * 1000L);

        // Default false: a jump start is a time penalty, not a lost lap,
        // unless the class says otherwise.
        _race.JumpStartCostsTheLap = raceClass?.JumpStartCostsTheLap ?? false;

        // Class-agnostic session again: this class's warning ladder, or the
        // default when there is no class or it never set one.
        _race.WarningLadder = raceClass?.WarningLadder;

        if (_race.DetectJumpStarts)
        {
            StatusMessage?.Invoke(_race.JumpStartPenaltyMs > 0
                ? $"[race] Jump-start detection on, {_race.JumpStartPenaltyMs / 1000.0:0.0}s automatic penalty."
                : "[race] Jump-start detection on, flagging only.");
        }
    }

    /// <summary>
    /// A staggered driver's turn to go - their delay is up, or they were
    /// just released by hand.
    /// </summary>
    private void OnDriverReleased(string transponder, long offsetMs)
    {
        _announcer.NotifyDriverRelease(transponder);

        var profile = _profiles.Get(transponder);
        var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Released {name} at +{offsetMs / 1000.0:0.0}s.");

        // The board shows who is still waiting, so it needs redrawing the
        // moment that changes.
        RenderLeaderboard();
    }


    /// <summary>
    /// Everything the start needs settled before the flag drops: who waits
    /// how long, and their release calls rendered ready to play.
    ///
    /// Called on a real green only - a restart after a caution must not
    /// re-stagger a field that is already spread around the track.
    /// </summary>
    private void ApplyStartRules()
    {
        // A green thrown straight from idle never went through staging,
        // where the tech warning is normally given - so give it here. Read
        // before the flag drops, so State is still what the start came from.
        if (_race.State is not (RaceState.Staging or RaceState.Countdown))
        {
            WarnAboutUninspectedDrivers();
        }

        BuildStartSchedule();

        // Rendered now, while the cars are still coming to the line. The
        // grid is known the moment the schedule is, so every release call
        // can be audio in memory before the first one is needed - a release
        // is the one line where a second of synthesis is a second of a car
        // sitting still waiting to be told.
        //
        // Not for a qualifier either: everyone goes on the green and nobody
        // is ever released, so nothing would play them. The roster falls
        // back to the whole grid, and every qualifying heat used to pay for
        // a "go!" line per car at the green, taking a synthesis slot from
        // the start call.
        if (_race.StartType is not (RaceStartType.Standard or RaceStartType.Qualifier))
        {
            _announcer.PrimeReleaseLines(_race.ReleaseRoster());
        }

        // The class's qualifying rule, so provisional pole is called on the
        // order the board and the qualifying projection show. The announcer
        // knows nothing of classes, so it is told at each green, as the
        // jump-start rules are given to the session.
        // WHY (23 Oct 2026, bug hunt 10): ShowRound, not just the Round box,
        // so a demo of a round calls pole on that class's rule too.
        var round = ShowRound();
        var raceClass = round is not null && _raceClasses is not null
            ? _raceClasses.All.FirstOrDefault(rc => string.Equals(rc.Name, round.ClassName, StringComparison.OrdinalIgnoreCase))
            : null;
        _announcer.SetQualifyingRule(raceClass?.QualifyingMethod ?? QualifyingMethod.TotalLaps,
            raceClass?.ConsecutiveLapCount ?? 3);
    }

    /// <summary>
    /// The grid a staggered start orders itself by.
    ///
    /// The selected round's roster first, since that is someone stating who
    /// is in this race. Failing that, the last race's finishing order -
    /// which is the natural grid for the next heat anyway, and is what makes
    /// a staggered start work at a club night that isn't running a formal
    /// race program. Requiring a round was silently turning every staggered
    /// start into a standard one.
    ///
    /// The last-race fallback is for staggered starts only, and only when
    /// the last race was the same kind of race - run
    /// without a round too, so the same class of freeform running. A
    /// standard or qualifying start needs no grid to run, and handing it a
    /// different field's finishing order saved start positions nobody
    /// started from: "up from 8th" for a five-car heat, and Comeback Kid
    /// or Pole Converted awarded off another race's result.
    /// <paramref name="fromLastRace"/> says the grid came from that
    /// fallback, so the session numbers start positions over the cars
    /// that actually turn up rather than the old field's slots.
    /// </summary>
    private List<string> GridForStagger(out bool fromLastRace)
    {
        fromLastRace = false;

        // WHY (23 Oct 2026, bug hunt 10): this read only the Round box, so a
        // demo of an already-run round (box on freeform, DemoRound set) took
        // the round's start type but not its grid - a staggered or
        // hand-released demo started everyone together, or borrowed another
        // race's order. ShowRound is the grid the intros already call.
        var roster = ShowRound()?.AssignedTransponders?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (roster.Count > 0)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Grid from the selected round: {Plural.Of(roster.Count, "car")}.");
            return roster;
        }

        // No round, and a start that does not need a grid to run, or (hand
        // released) would put a borrowed one on the board: no grid, so start
        // positions stay at zero rather than borrowed - see above.
        if (!LastRaceCanBeTheGrid(_race.StartType))
        {
            return new List<string>();
        }

        // Never a saved demo: its grid is the simulator's made-up cars.
        var lastRace = _sessionHistory.All
            .FirstOrDefault(r => r.SessionType == RaceSessionType.Race && !r.IsNotScored() && r.Results.Count > 0);

        // Only the same kind of race: a race run from a round was a class's
        // own field, not the one at a freeform start, so it is no grid for
        // this one. There is no class to compare without a round, so "the
        // same class" is the last race also being run without one.
        if (lastRace is null || !string.IsNullOrWhiteSpace(lastRace.RoundId))
        {
            return new List<string>();
        }

        var lastOrder = lastRace.Results
            .OrderBy(r => r.Position)
            .Select(r => r.Transponder)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // WHY (10 Nov 2026, bug hunt 16): the release schedule is built on
        // this list, so every driver on it who is not here held up everyone
        // behind them by a gap, and had "X, go!" called on the PA for them -
        // from a race that may have been last week. Only tonight's race, and
        // only the cars on it that are here, as the desk and the decoder
        // know it. See BorrowedGridOfCarsHere.
        var watch = _transponderWatch;
        var desk = _checkIns is { Count: > 0 } checkIns ? checkIns : null;
        var fromResult = BorrowedGridOfCarsHere(
            lastOrder,
            lastRace.StartedAtUtc,
            DateTime.UtcNow,
            desk is null ? null : t => desk.IsCheckedIn(t),
            watch is null ? null : t => watch.Of(t) is not null);

        if (fromResult.Count > 0)
        {
            var dropped = lastOrder.Count - fromResult.Count;
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] No round selected - grid taken from the last race ({lastRace.Label}): {Plural.Of(fromResult.Count, "car")}"
                + (dropped > 0 ? $", leaving out {Plural.Of(dropped, "driver")} not here tonight." : "."));
            fromLastRace = true;
        }

        return fromResult;
    }

    /// <summary>How long ago the last race can have started and still be tonight's - see BorrowedGridOfCarsHere.</summary>
    internal static readonly TimeSpan BorrowedGridMaxAge = TimeSpan.FromHours(12);

    /// <summary>
    /// The last race's finishing order cut down to a grid for this race:
    /// empty when that race was not tonight's, otherwise only the cars on
    /// it that are here.
    ///
    /// "Here" is checked in at the desk (<paramref name="checkedIn"/>, null
    /// when the desk is not in use tonight) or heard by the decoder lately
    /// (<paramref name="heardLately"/>, null when there is no watch). With
    /// the desk in use, a car neither ticked in nor heard is left out. With
    /// only the decoder to go on, it is used once it has heard any car on
    /// the list - hearing none of them says the loop was quiet, not that
    /// the whole field went home - and with nothing to go on, the list is
    /// kept as it is.
    ///
    /// A car left out is not held at all: like a newcomer, it is timed
    /// from the green (see IsReleased), rather than waiting on the gaps of
    /// drivers who are not there.
    /// </summary>
    internal static List<string> BorrowedGridOfCarsHere(
        IReadOnlyList<string> lastRaceOrder,
        DateTime lastRaceStartedUtc,
        DateTime nowUtc,
        Func<string, bool>? checkedIn,
        Func<string, bool>? heardLately)
    {
        // A twelve-hour window rather than the calendar date, so a club
        // night that runs past midnight still has its own last race.
        var age = nowUtc - lastRaceStartedUtc;

        if (age < TimeSpan.Zero || age > BorrowedGridMaxAge)
        {
            return new List<string>();
        }

        if (checkedIn is not null)
        {
            return lastRaceOrder
                .Where(t => checkedIn(t) || heardLately?.Invoke(t) == true)
                .ToList();
        }

        if (heardLately is not null && lastRaceOrder.Any(heardLately))
        {
            return lastRaceOrder.Where(heardLately).ToList();
        }

        return lastRaceOrder.ToList();
    }

    /// <summary>
    /// Whether a start with no round can take the last race's finishing
    /// order as its grid - a staggered start only.
    ///
    /// A standard or qualifying start needs no grid (see GridForStagger).
    /// Nor does a hand-released start get one (bug hunt 13): it puts every
    /// car on its grid on the board at the green, held at the line, so the
    /// last race's field put its drivers who are not here tonight into this
    /// race, with no way to take them off, and they were filed with it as
    /// 0-lap finishers. Without a round there is nobody to hold, and
    /// everyone goes on the green - see BuildStartSchedule.
    /// </summary>
    internal static bool LastRaceCanBeTheGrid(RaceStartType startType) =>
        startType is RaceStartType.StaggeredGrid or RaceStartType.StaggeredReverse or RaceStartType.StaggeredHandicap;

    /// <summary>
    /// The clock a handicap start replaced with the shared one, so it can
    /// be put back for the next race. Null when nothing was replaced.
    /// </summary>
    private StaggerClock? _clockBeforeHandicapOverride;

    /// <summary>
    /// Works out who waits how long, and hands the session the schedule.
    ///
    /// The grid order is the round's assigned transponders, in the order
    /// they were assigned. With no round selected there is no grid, so a
    /// fixed-gap stagger has nothing to order by and falls back to a
    /// standard start rather than inventing an order from whoever happens
    /// to cross first.
    /// </summary>
    private void BuildStartSchedule()
    {
        // A handicap start moves the session onto one shared clock for
        // that race only. Put back whatever it replaced before this race is
        // set up, or the NEXT race - a grid stagger on each driver's own
        // clock, say - would quietly run on the handicap's shared one.
        if (_clockBeforeHandicapOverride is { } clockBefore)
        {
            _race.StaggerClockMode = clockBefore;
            _clockBeforeHandicapOverride = null;
        }

        var type = _race.StartType;

        // Resolved for EVERY start type, staggered or not.
        //
        // A standard start needs no schedule, but it still has a grid, and
        // knowing where everyone started is what makes "won from the back"
        // answerable afterwards. Empty when nothing could supply one, which
        // leaves start positions at zero rather than invented.
        //
        // A grid borrowed from the last race is numbered over the cars that
        // actually race in this one, not that field's slots - see
        // GridForStagger.
        var startingGrid = GridForStagger(out var gridFromLastRace);
        _race.SetGrid(startingGrid, renumberOverStarters: gridFromLastRace);

        if (type == RaceStartType.Standard)
        {
            _race.SetReleaseSchedule(new Dictionary<string, long>());
            return;
        }

        if (type == RaceStartType.Qualifier)
        {
            // Everyone goes on the green; the per-driver clocks zero at
            // each driver's own first crossing, inside the session.
            _race.SetReleaseSchedule(new Dictionary<string, long>());
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Qualifying start - everyone on the green, "
                + "each driver's clock runs from their own first crossing.");
            return;
        }

        if (type == RaceStartType.HandReleased)
        {
            // Nothing scheduled: every driver waits until released by hand.
            _race.SetReleaseSchedule(new Dictionary<string, long>());

            var handGrid = startingGrid;

            StatusMessage?.Invoke(handGrid.Count > 0
                ? $"{AppTime.LogStampNow()}  [race] Hand-released start - {Plural.Of(handGrid.Count, "car")} held at the line. Right-click a driver on the leaderboard to release them."
                : $"{AppTime.LogStampNow()}  [race] Hand-released start needs a round with an assigned grid - there's nobody to hold, so everyone goes on the green.");
            return;
        }

        if (type == RaceStartType.StaggeredHandicap)
        {
            // Loaded here rather than injected: this is the only place in
            // Race Control that needs handicaps, and once per race start is
            // not a cost worth widening the constructor for.
            var handicaps = HandicapStore.Load();
            var schedule = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            // A time credit is what a SLOWER driver is given - Suggest
            // works it out as their gap to the quickest driver over a race,
            // and the quickest gets none. So this is a pursuit start: the
            // biggest credit goes first, on the green, and everyone else
            // waits by however much less credit they have, the scratch
            // drivers longest of all.
            //
            // It used to hold each driver back BY their credit, which is the
            // exact opposite: the slowest car waited longest and the fastest
            // went on the green, so the handicap widened the gap it was set
            // to close - by twice the credit on a shared clock.
            //
            // The field is the grid, so a driver with no handicap entry at
            // all is still held as scratch.
            //
            // WHY (23 Oct 2026, bug hunt 10): with no grid the field used to
            // be every entry in the handicap store - every class, absent
            // drivers too - so an absent driver's big credit held the whole
            // field after the green while the shared race clock ran. With no
            // grid there is no telling who is racing, so it starts everyone
            // together instead, as the fixed-gap stagger does.
            if (startingGrid.Count == 0)
            {
                _race.SetReleaseSchedule(new Dictionary<string, long>());
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Handicap start has no grid to know who is racing - "
                    + "select a round with an assigned roster on Race Day \u2192 Next Steps, or run one race so the last "
                    + "result can be used. Starting everyone together instead.");
                return;
            }

            var credits = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in handicaps.All)
            {
                if (!string.IsNullOrWhiteSpace(entry.Transponder))
                {
                    credits[entry.Transponder] = (long)Math.Round(Math.Max(0, entry.TimeCreditSeconds) * 1000);
                }
            }

            var pursuitField = startingGrid;

            long CreditOf(string transponder) => credits.TryGetValue(transponder, out var ms) ? ms : 0;

            var maxCredit = pursuitField.Count > 0 ? pursuitField.Max(t => CreditOf(t)) : 0;

            if (maxCredit > 0)
            {
                foreach (var transponder in pursuitField)
                {
                    schedule[transponder] = maxCredit - CreditOf(transponder);
                }
            }

            // A handicap start only means anything on ONE clock. On each
            // driver's own clock, every car is timed from its own release,
            // so a head start is worth nothing and the result is decided on
            // raw pace - the handicap cancels itself out. Shared green is
            // what makes the head start count, and first across the line win.
            // Set for this race only: the format keeps whatever it says, and
            // is pushed back in between races as usual.
            if (maxCredit > 0 && _race.StaggerClockMode != StaggerClock.SharedGreen)
            {
                _clockBeforeHandicapOverride = _race.StaggerClockMode;
                _race.StaggerClockMode = StaggerClock.SharedGreen;
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Handicap start runs on one clock from the green - "
                    + "on each driver's own clock the head start would count for nothing.");
            }

            _race.SetReleaseSchedule(schedule);
            StatusMessage?.Invoke(maxCredit > 0
                ? $"{AppTime.LogStampNow()}  [race] Handicap start (pursuit) - biggest credit away first, "
                  + $"scratch drivers held {maxCredit / 1000.0:0.0}s."
                : $"{AppTime.LogStampNow()}  [race] Handicap start selected, but no driver has a time credit - everyone goes on the green.");

            // The same credit taken off the result as well would count it
            // twice - once as the head start, once off the total.
            if (maxCredit > 0
                && ShowRound() is { } handicapRound
                && handicaps.ModeFor(handicapRound.ClassName) == HandicapMode.TimeCredit)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Note: {handicapRound.ClassName} also takes time credits off "
                    + "the result - with a handicap start as well, each credit counts twice. Set the class's handicap mode to None "
                    + "for a handicap start.");
            }

            return;
        }

        var grid = startingGrid;

        if (grid.Count == 0)
        {
            _race.SetReleaseSchedule(new Dictionary<string, long>());
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Staggered start has no grid to order by - "
                + "select a round with an assigned roster on Race Day \u2192 Next Steps, or run one race so the last "
                + "result can be used. Starting everyone together instead.");
            return;
        }

        var gapMs = Math.Max(1, _fmt.StaggerGapSeconds) * 1000L;
        var staggered = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < grid.Count; i++)
        {
            // Reverse grid is a pursuit start: the back of the grid goes
            // first and the quick drivers have to come through the field.
            var place = _race.StartType == RaceStartType.StaggeredReverse
                ? grid.Count - 1 - i
                : i;

            staggered[grid[i]] = place * gapMs;
        }

        _race.SetReleaseSchedule(staggered);
        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [race] {(_race.StartType == RaceStartType.StaggeredReverse ? "Reverse-grid" : "Grid-order")} start - "
            + $"{Plural.Of(grid.Count, "car")}, {gapMs / 1000.0:0.#}s apart, "
            + $"{(_race.StaggerClockMode == StaggerClock.OwnRelease ? "each clock from their own release" : "one clock from the green")}.");
    }
}
