namespace HeatCaller;

public enum RaceState
{
    Idle,
    Staging,
    Countdown,
    Racing,
    Finished,
}

public enum FlagState
{
    None,
    Green,
    Yellow,
    Red,
    Checkered,
}

/// <summary>A racer's live standing within the current direct-decoder session.</summary>
public sealed class RacerStanding
{
    public required string TransponderId { get; init; }

    /// <summary>
    /// Where this driver's qualifying clock zeroed, in ms since the green -
    /// their first crossing. -1 until they have crossed. Only meaningful
    /// when the race was started as RaceStartType.Qualifier.
    /// </summary>
    public long QualifyingZeroMs { get; set; } = -1;

    public int Laps { get; set; }
    public long FastestLapMs { get; set; }
    public long LastLapDeviceTimeMs { get; set; }

    /// <summary>
    /// Device time of the most recent pass of ANY kind - valid or rejected -
    /// used only for the minimum-lap-time validity check. See the note on
    /// MinLapTimeMs below for why this has to be separate from
    /// LastLapDeviceTimeMs (which only tracks counted laps).
    /// </summary>
    public long LastSeenDeviceTimeMs { get; set; }

    public long ElapsedMs { get; set; }
    public int ConsecutiveValidLaps { get; set; }

    /// <summary>
    /// Every counted lap's duration, in order. Kept so pace across a race
    /// can be charted afterward - FastestLapMs alone only says how good a
    /// driver's single best lap was, not whether they were consistent,
    /// fading, or finding time as the race went on.
    /// </summary>
    public List<long> LapTimesMs { get; } = new();

    /// <summary>
    /// The clean-run count as it stood after each counted lap, in step with
    /// LapTimesMs.
    ///
    /// Kept per lap so the leaderboard can show which laps were part of a
    /// run and where one broke - a streak is otherwise only ever visible as
    /// the single number it is at this instant, which says nothing about
    /// how the driver got there.
    /// </summary>
    public List<int> StreakAtLap { get; } = new();

    /// <summary>
    /// Indexes into LapTimesMs of laps that ran through a crash recovery -
    /// the first crossing after a race was brought back from the journal,
    /// which is timed from the car's last counted lap and so spans the whole
    /// stoppage.
    ///
    /// Kept per lap rather than worked out when needed, because nothing else
    /// about such a lap says what it was: once a lap has been removed by
    /// hand, re-deriving the best lap or the streak baseline from the lap
    /// list alone turned that minutes-long lap into an ordinary one. A list
    /// rather than a single index because a race can go down more than once.
    /// Empty for every lap of a race that never crashed.
    /// </summary>
    public List<int> RecoveryLapIndexes { get; } = new();

    /// <summary>Whether the lap at this index in LapTimesMs ran through a crash recovery - see RecoveryLapIndexes.</summary>
    public bool IsRecoveryLap(int lapIndex) => RecoveryLapIndexes.Contains(lapIndex);

    /// <summary>Total manually-applied penalty time, tracked separately from ElapsedMs so the UI can show that a penalty is in effect.</summary>
    public long PenaltyMs { get; set; }

    /// <summary>Running sum/count of lap times within the CURRENT streak only - used to compute the streak's own average pace, reset whenever the streak breaks (including breaking on an unusually slow lap).</summary>
    public long StreakLapTimeSumMs { get; set; }
    public int StreakLapCount { get; set; }

    /// <summary>
    /// The lap the NEXT lap is judged against for streak purposes - this
    /// driver's last real (flying) lap.
    ///
    /// Separate from LapTimesMs[^1] because the start lap lives in that list
    /// too, and its "time" runs from the green-flag reference to the first
    /// crossing rather than being a lap at all. Comparing a real lap against
    /// that would break every streak on lap two.
    /// </summary>
    public long StreakBaselineLapMs { get; set; }

    /// <summary>
    /// How far into the race this driver was released, in milliseconds after
    /// the green. Zero for a standard start, where everyone goes together.
    /// </summary>
    public long ReleaseOffsetMs { get; set; }

    /// <summary>Passes thrown out as too soon to be a real lap.</summary>
    public int RejectedLaps { get; set; }

    /// <summary>Laps this driver completed while leading on the road.</summary>
    public int LapsLed { get; set; }

    /// <summary>Where they started, 1-based, or zero when the grid wasn't known.</summary>
    public int StartPosition { get; set; }

    /// <summary>Warnings given by race control this race, oldest first. See RaceControlSession.IssueWarning.</summary>
    public List<RaceWarning> Warnings { get; } = new();

    /// <summary>True once any warning on the ladder has called this driver in.</summary>
    public bool IsBlackFlagged => Warnings.Any(w => w.Action == WarningAction.BlackFlag);
}

/// <summary>How the field is let go at the start.</summary>
public enum RaceStartType
{
    /// <summary>Everyone released on the green together.</summary>
    Standard,

    /// <summary>A fixed gap between cars, leader away first.</summary>
    StaggeredGrid,

    /// <summary>A fixed gap between cars, last on the grid away first - a pursuit start.</summary>
    StaggeredReverse,

    /// <summary>Each driver's delay is their handicap time credit.</summary>
    StaggeredHandicap,

    /// <summary>No schedule - the race director releases each driver by hand.</summary>
    HandReleased,

    /// <summary>
    /// IFMAR-style qualifying: everyone goes on the green together, but
    /// each driver's clock starts at their own FIRST crossing, so the run
    /// from the grid to the loop costs nobody anything and every driver is
    /// measured over their own laps alone. The race window itself still
    /// runs from the green.
    /// </summary>
    Qualifier,
}

/// <summary>What a staggered driver's own race clock is measured from.</summary>
public enum StaggerClock
{
    /// <summary>
    /// Each driver's clock starts when THEY are released, so everyone gets
    /// the same amount of green-flag time and the result is decided on pace.
    /// </summary>
    OwnRelease,

    /// <summary>
    /// One clock for everyone, running from the first green. A later release
    /// simply loses that time - a true handicap, where first across the line
    /// wins.
    /// </summary>
    SharedGreen,
}

/// <summary>
/// The race-control layer for the direct EasyLap decoder path: everything
/// here is pure software logic layered on top of the raw decoder stream.
/// The hardware itself has no concept of "races" - it only reports
/// transponder passes with a device timestamp and signal quality. Staging,
/// flags, lap validity, standings, and race completion are all decided
/// here, the same way Next Level Timing (or any timing software) would.
///
/// Confirmed by real testing: a stationary transponder near the sensor
/// retriggers the decoder at a highly regular ~2 second interval (with a
/// distinctive signal_hits/signal_strength signature, notably different
/// from real passes - not currently used for filtering, since we haven't
/// validated it's reliable across different tracks/decoders, but worth
/// revisiting). The min-lap-time filter (<see cref="MinLapTimeMs"/>)
/// rejects any single one of these retriggers, but an earlier bug let
/// every OTHER retrigger slip through as a false lap - see the comment on
/// LastSeenDeviceTimeMs in RacerStanding for why, and how it's fixed.
/// </summary>
public sealed class RaceControlSession
{
    /// <summary>
    /// Laps faster than this are treated as invalid glitch passes, not real
    /// laps. Validity is judged against the immediately preceding pass of
    /// ANY kind (valid or rejected) - not just the last COUNTED lap. Without
    /// that distinction, a stationary transponder retriggering just under
    /// this threshold can have every OTHER retrigger slip through: the gap
    /// from the last REJECTED pass is too short to count, but the gap from
    /// the last VALID lap (two retrigger periods back) can cross the
    /// threshold and get wrongly counted. Confirmed by real testing - a
    /// stationary car under the timing bridge produced exactly this pattern
    /// before this was fixed.
    /// </summary>
    public int MinLapTimeMs { get; set; } = 3000;

    /// <summary>
    /// Resolves how many consecutive clean laps make a streak.
    ///
    /// A function rather than a stored number, because this has to agree
    /// with the announcer, and the announcer re-reads it on every call. The
    /// figure a club actually sets lives in Sound Effects - races and
    /// practice have their own - so a copy kept here would need pushing
    /// back in whenever either changed. Nothing ever pushed it: nothing
    /// assigned this at all, so the board, the lap-detail popup and the
    /// race log sat on the default of three for every club while the voice
    /// used whatever had been set.
    ///
    /// MainForm wires this up, being the one place that can see both the
    /// sound profile and this session's practice flag.
    /// </summary>
    public Func<int>? StreakThresholdProvider { get; set; }

    /// <summary>
    /// Consecutive valid laps required before a racer is considered "on a
    /// streak". Three until something wires
    /// <see cref="StreakThresholdProvider"/>, and never below two - a run
    /// of one lap is a lap.
    /// </summary>
    public int StreakThreshold => Math.Max(2, StreakThresholdProvider?.Invoke() ?? 3);

    /// <summary>
    /// How far a lap may differ from the driver's PREVIOUS lap, as a
    /// percentage of it, and still extend the streak. 25 means anything from
    /// 25% quicker to 25% slower keeps the run alive.
    ///
    /// Judged against the last lap rather than the streak's running average,
    /// which is what it used to compare against. An average absorbs a drift:
    /// a driver slowing a little every lap kept a "streak" going indefinitely
    /// because the baseline slid along with them. Lap-to-lap can't drift -
    /// it asks the only question a consistency streak should, which is
    /// whether this lap matched the one before it.
    ///
    /// Set lower to demand tighter consistency, higher to tolerate more
    /// variation.
    /// </summary>
    public double StreakWindowPercent { get; set; } = 25;

    /// <summary>
    /// Whether a lap that is faster than the window also breaks the streak.
    ///
    /// On, the window is symmetric and a streak means genuine consistency -
    /// a lap a third quicker than the last one ends the run just as a slow
    /// one does. Off, only the slow side is enforced, so finding time is
    /// never punished.
    /// </summary>
    public bool StreakFasterLapBreaks { get; set; } = true;

    /// <summary>Race ends automatically once any racer reaches this many laps. Null = no lap limit.</summary>
    public int? LapLimit { get; set; }

    /// <summary>
    /// Whether reaching a limit puts the checkered flag OUT rather than
    /// ending the race on the spot.
    ///
    /// On - which is how racing actually works - the leader reaching the lap
    /// limit, or the clock running out, starts the last lap: the leader
    /// takes the flag there and then, and everyone else takes it on their
    /// next crossing. Off restores the blunt behaviour of stopping the race
    /// mid-lap for the whole field.
    ///
    /// Stopping dead is why a finished board could read 21, 19, 17, 17, 16:
    /// those aren't finishing positions so much as wherever each car
    /// happened to be when the buzzer went.
    /// </summary>
    public bool FinishOnNextCrossing { get; set; } = true;

    /// <summary>True once the checkered is out but cars are still taking it.</summary>
    public bool IsFinishing { get; private set; }

    /// <summary>Drivers who have crossed the line since the checkered came out.</summary>
    private readonly HashSet<string> _tookTheFlag = new(StringComparer.OrdinalIgnoreCase);

    private DateTime? _finishingSinceUtc;
    private bool _finishingOnTime;

    /// <summary>
    /// How long the race will wait for stragglers before calling it.
    ///
    /// Without a cap, one car that stopped on track - battery, breakage,
    /// picked up and carried off - would leave the race permanently in its
    /// last lap, and nothing after the flag would ever run.
    /// </summary>
    private static readonly TimeSpan FinishGrace = TimeSpan.FromSeconds(60);

    /// <summary>Whether this driver has already taken the checkered flag.</summary>
    public bool HasTakenFlag(string transponderId) => _tookTheFlag.Contains(transponderId);

    /// <summary>
    /// Puts the checkered out and starts the last lap, rather than ending
    /// the race where it stands.
    /// </summary>
    private void BeginFinishing(bool timeLimitReached)
    {
        if (IsFinishing || State != RaceState.Racing)
        {
            return;
        }

        IsFinishing = true;
        _finishingOnTime = timeLimitReached;
        _finishingSinceUtc = DateTime.UtcNow;
        _tookTheFlag.Clear();

        SetFlag(FlagState.Checkered);

        if (timeLimitReached)
        {
            Log("Time limit reached - last lap.");

            // Said plainly, because the board is about to show cars still
            // circulating under a checkered flag, and that reads as a fault
            // if nobody explained it.
            if (LastWindowExtension > TimeSpan.Zero)
            {
                Log($"Staggered start on each driver's own clock - cars released later keep running until their own window is up, out to {LastWindowExtension.TotalSeconds:0}s after this.");
            }

            SafeRaise(TimeLimitReached, h => h(), nameof(TimeLimitReached));
        }
        else
        {
            Log($"Lap limit of {LapLimit} reached - checkered flag out, last lap for everyone else.");
        }
    }

    /// <summary>
    /// Records a driver taking the flag, and ends the race once the last of
    /// them is in.
    /// </summary>
    private void MarkTookTheFlag(string transponderId)
    {
        if (!IsFinishing || !_tookTheFlag.Add(transponderId))
        {
            return;
        }

        Log($"Took the flag: {transponderId} on lap {(_standings.TryGetValue(transponderId, out var s) ? s.Laps : 0)}.");

        // Only drivers who actually appear in the standings can take a
        // flag, so that - not the grid - is the set to wait on.
        if (_standings.Keys.All(k => _tookTheFlag.Contains(k)))
        {
            Log("Every car has taken the flag.");
            Finish(_finishingOnTime);
        }
    }

    /// <summary>Race ends automatically after this much real time has elapsed. Null = no time limit.</summary>
    public TimeSpan? TimeLimit { get; set; }

    /// <summary>
    /// How much later than the green this driver's own race clock starts.
    ///
    /// Zero for everybody except a staggered field running on
    /// <see cref="StaggerClock.OwnRelease"/>, which promises each driver
    /// the same amount of green-flag time. Under SharedGreen the wait is
    /// meant to be lost - that is the whole point of it - so there is no
    /// offset to honour there.
    /// </summary>
    private long OwnClockOffsetMs(string transponderId) =>
        StaggerClockMode == StaggerClock.OwnRelease
            ? Math.Max(0, ReleaseOffsetMsFor(transponderId))
            : 0;

    /// <summary>
    /// How far past the time limit the LAST car's own window runs - the
    /// biggest release offset on the grid, or nothing at all when no such
    /// promise is in force.
    /// </summary>
    private TimeSpan LastWindowExtension =>
        StaggerClockMode == StaggerClock.OwnRelease && _releaseOffsetsMs.Count > 0
            ? TimeSpan.FromMilliseconds(Math.Max(0, _releaseOffsetsMs.Values.Max()))
            : TimeSpan.Zero;

    /// <summary>
    /// Whether this driver's own race window has run out.
    ///
    /// The time limit used to be read straight off the race clock, which
    /// meant a car released twenty seconds after the green got twenty
    /// seconds less race - the exact thing OwnRelease exists to prevent,
    /// applied at the one moment it costs the most. False when there is no
    /// time limit at all, because then nobody ever runs out.
    /// </summary>
    private bool IsOutOfTime(string transponderId) =>
        TimeLimit is { } limit
        && RaceStartUtc.HasValue
        && ElapsedRaceTime >= limit + TimeSpan.FromMilliseconds(OwnClockOffsetMs(transponderId));

    /// <summary>
    /// Whether the checkered that is already out applies to THIS driver.
    ///
    /// On a lap limit it always does: the limit is a count, and everybody
    /// is counting the same laps. On the clock it is different - a car
    /// still inside its own window is still inside its own race, and
    /// sending it the flag there takes back precisely the time the start
    /// mode was chosen to give it.
    ///
    /// With no window to extend - every standard race, and every staggered
    /// one on SharedGreen - this is true the moment the checkered is out,
    /// which is exactly what it was before any of this existed.
    /// </summary>
    private bool FlagAppliesTo(string transponderId) =>
        !_finishingOnTime
        || LastWindowExtension <= TimeSpan.Zero
        || IsOutOfTime(transponderId);

    public RaceState State { get; private set; } = RaceState.Idle;
    public FlagState Flag { get; private set; } = FlagState.None;
    public DateTime? RaceStartUtc { get; private set; }

    /// <summary>Total time already spent under red flag this race.</summary>
    private TimeSpan _accumulatedRedTime;

    /// <summary>
    /// Fastest lap of the race so far, across every driver. Held here
    /// rather than derived from the standings each time, so "is this a new
    /// fastest lap?" is one comparison that can't disagree with what was
    /// last announced.
    /// </summary>
    /// <summary>
    /// Whether to flag cars crossing before the green. Set from the racing
    /// class when a race starts - the session itself is class-agnostic.
    /// </summary>
    public bool DetectJumpStarts { get; set; } = true;

    /// <summary>Automatic penalty for a jump start, in milliseconds. Zero flags without penalising.</summary>
    public long JumpStartPenaltyMs { get; set; }

    /// <summary>
    /// Whether a jump start also costs the lap. See
    /// RaceClass.JumpStartCostsTheLap - off means the jumper is credited
    /// with the start lap at the green and pays the time penalty only.
    /// </summary>
    public bool JumpStartCostsTheLap { get; set; }

    /// <summary>
    /// Device time of each jumper's pre-green crossing, so the green can
    /// credit them with the start lap they had already completed.
    /// </summary>
    private readonly Dictionary<string, long> _jumpStartCrossingMs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How far before the green a crossing can sit and still be read as a
    /// jump rather than a car being placed on the grid over the loop.
    ///
    /// Eight seconds covers a driver going on the first light of a long
    /// countdown and nothing that happens while the field is being called.
    /// </summary>
    private const long JumpStartCreditWindowMs = 8000;

    /// <summary>
    /// Raised when a car crosses the line before the green drops. Carries
    /// the driver and the penalty applied (zero when flagging only).
    /// </summary>
    public event Action<string, long>? JumpStartDetected;

    /// <summary>Transponders that jumped the start this race.</summary>
    private readonly HashSet<string> _jumpStarters = new(StringComparer.OrdinalIgnoreCase);

    public bool HasJumpStarted(string transponderId) => _jumpStarters.Contains(transponderId);

    private long _raceFastestLapMs;
    private bool _lastLapAnnounced;
    private bool _finalMinuteAnnounced;
    private string _raceFastestLapTransponder = string.Empty;

    /// <summary>Fastest lap of the race so far, across all drivers. Zero before any lap is counted.</summary>
    public long RaceFastestLapMs => _raceFastestLapMs;

    /// <summary>Who currently holds the race's fastest lap.</summary>
    public string RaceFastestLapTransponder => _raceFastestLapTransponder;

    /// <summary>When the current red flag began, or null if not currently red.</summary>
    private DateTime? _redFlagStartedUtc;

    /// <summary>
    /// The race clock as it stood the moment the race finished - see
    /// ElapsedRaceTime. Only read while the state is Finished, and written
    /// by every finish, so a value left over from an earlier race is never
    /// the one shown.
    /// </summary>
    private TimeSpan? _elapsedAtFinish;

    /// <summary>
    /// Race time with any red-flag periods subtracted - the single source
    /// of truth for "how long has this race actually been running".
    ///
    /// A red flag means cars are stopped, so counting that time would both
    /// misreport elapsed time and let a time-limited race expire while
    /// nobody was driving. Laps were already ignored under red; the clock
    /// now matches.
    /// </summary>
    public TimeSpan ElapsedRaceTime
    {
        get
        {
            if (RaceStartUtc is not { } start)
            {
                return TimeSpan.Zero;
            }

            // A finished race's clock stops at the finish. Worked out live
            // from the start, it carried on counting after the checkered -
            // the Race Control clock, the Big Screen and the announcer page
            // all went on showing a race getting longer for as long as the
            // result sat on the board, and a warning given after the flag
            // was stamped with that ever-growing time.
            if (State == RaceState.Finished && _elapsedAtFinish is { } frozen)
            {
                return frozen;
            }

            // Staging and the countdown come before the next race has a
            // clock of its own. RaceStartUtc is still the LAST race's start
            // then, so the clocks counted on from that race right through
            // the call to the grid.
            if (State is RaceState.Staging or RaceState.Countdown)
            {
                return TimeSpan.Zero;
            }

            var raw = DateTime.UtcNow - start - _accumulatedRedTime;

            // Subtract the in-progress red period too, so the clock is
            // visibly frozen while the flag is out rather than jumping
            // backwards when it clears.
            if (_redFlagStartedUtc is { } redStart)
            {
                raw -= DateTime.UtcNow - redStart;
            }

            return raw < TimeSpan.Zero ? TimeSpan.Zero : raw;
        }
    }

    /// <summary>
    /// True while a demo/rehearsal race (simulated locally, no real
    /// timing hardware) is running. Everything else in the session behaves
    /// completely normally either way - this exists purely so downstream
    /// consumers (currently just session-history saving) can tell a demo
    /// apart from a real race and skip persisting fake results.
    /// </summary>
    public bool IsDemoMode { get; set; }

    /// <summary>
    /// True when the green flag last thrown STARTED a race - from staging,
    /// the countdown, an idle or a finished session - and false when it
    /// resumed one after a yellow or a red, or came back with a restored
    /// race. Set just before the flag changes, so FlagChanged handlers can
    /// tell the start from a restart.
    ///
    /// Race Control used to tell them apart by the flag before the green: a
    /// green after a yellow or a red was taken for a restart. So a yellow or
    /// red thrown to hold a start - during staging or the countdown - made
    /// the start itself sound like a restart: no start horn, and with the
    /// "Green flag (resumed)" call off by default, nothing said at all.
    /// </summary>
    public bool GreenStartedTheRace { get; private set; }

    /// <summary>
    /// True while open practice is running. Like IsDemoMode, this marks
    /// laps as "not a real race" so Race Control - which stays subscribed
    /// to this session's lap feed for the app's whole lifetime - skips
    /// setting track records and saving to history for them. Practice has
    /// its own explicit Save button instead.
    /// </summary>
    public bool IsPracticeMode { get; set; }

    /// <summary>
    /// The most recent device timer value seen from any lap event, valid or
    /// not, updated by every call to ProcessLapEvent. Exists so GreenFlag's
    /// zero-point can be read from here directly (race.LastKnownDeviceTimeMs)
    /// rather than requiring whichever UI calls GreenFlag to separately track
    /// its own copy of the same value - useful now that race control and the
    /// raw decoder connection can live in different windows.
    /// </summary>
    public long LastKnownDeviceTimeMs { get; private set; }

    /// <summary>Wall-clock moment LastKnownDeviceTimeMs was received, for estimating the decoder clock between passes.</summary>
    private DateTime? _lastKnownDeviceWallUtc;

    /// <summary>
    /// Best estimate of what the decoder's clock reads RIGHT NOW: the last
    /// device time seen, plus the wall time since it arrived.
    ///
    /// The race start used to be stamped with LastKnownDeviceTimeMs itself -
    /// the time of whatever pass happened last, which could be a car rolling
    /// over the line minutes earlier. Every driver's first lap and Total
    /// Time then included that whole gap.
    /// </summary>
    public long EstimatedDeviceTimeNowMs =>
        _lastKnownDeviceWallUtc is { } wall
            ? LastKnownDeviceTimeMs + (long)(DateTime.UtcNow - wall).TotalMilliseconds
            : LastKnownDeviceTimeMs;

    /// <summary>Whether the start reference has been sanity-checked against the first real pass of this race.</summary>
    private bool _startReferenceChecked;

    private long? _raceStartDeviceTimeMs;

    /// <summary>
    /// Every driver's standing, by transponder - ignoring case, like every
    /// other per-transponder collection here. It was the one that did not,
    /// so a grid id, a release or an alternate's owner typed in a different
    /// case from what the decoder reports split one driver into two rows.
    /// </summary>
    private readonly Dictionary<string, RacerStanding> _standings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Given a transponder id, the main transponder of the driver it is the
    /// ALTERNATE transponder of - or null when it is nobody's alternate.
    /// Wired to ProfileStore.PrimaryForAlternate.
    ///
    /// A callback rather than a ProfileStore reference, for the same reason
    /// as BeforeGreenFlagAsync: the session knows about laps, not about
    /// drivers. Left null, every pass is timed under its own id exactly as
    /// before.
    /// </summary>
    public Func<string, string?>? AlternateTransponderOwner { get; set; }

    /// <summary>
    /// Per race: each alternate transponder heard so far, and whether its
    /// laps are being filed under the owner's main transponder (true) or
    /// timed as a car of its own (false). Decided once, on the alternate's
    /// first pass of the race, and kept - so a driver's laps never flip
    /// between two rows halfway through. Cleared whenever the standings are.
    /// </summary>
    private readonly Dictionary<string, bool> _alternateMerged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Main transponders whose alternate is being filed under them this race.</summary>
    private readonly HashSet<string> _mergedInto = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every transponder heard since the standings were last cleared, by its own id.</summary>
    private readonly HashSet<string> _heardThisRace = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Main transponders already warned about running alongside their merged alternate, so the log says it once.</summary>
    private readonly HashSet<string> _bothCarsWarned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Forgets the per-race alternate decisions - called wherever the standings are cleared for a new race.</summary>
    private void ClearAlternateDecisions()
    {
        _alternateMerged.Clear();
        _mergedInto.Clear();
        _heardThisRace.Clear();
        _bothCarsWarned.Clear();
    }

    public event Action<RaceState>? StateChanged;
    public event Action<FlagState>? FlagChanged;

    /// <summary>bool = counted as valid, bool = this lap set a new personal-best (fastest) for this racer.</summary>
    /// <summary>Fires for every counted pass. Flags: valid, and whether it's the fastest lap of the RACE.</summary>
    public event Action<RacerStanding, bool, bool>? LapProcessed;

    /// <summary>
    /// Fires when a driver beats their own previous best this race.
    ///
    /// A separate event rather than another flag on LapProcessed: widening
    /// that signature would break every subscriber for something most of
    /// them don't care about. Only the announcer needs this.
    /// </summary>
    public event Action<RacerStanding>? PersonalBestLap;

    /// <summary>
    /// Fires when a race director changes a standing by hand - a lap added
    /// or removed, or a time penalty.
    ///
    /// Separate from LapProcessed on purpose. Adjustments used to be raised
    /// as an invalid LapProcessed, so the announcer treated every +1 lap or
    /// penalty as a car crossing the line: invalid-lap buzzer, a "lap
    /// completed" call, the penalty read out as a lap time, and the current
    /// streak announced again.
    /// </summary>
    public event Action<RacerStanding>? StandingAdjusted;

    /// <summary>
    /// Changes every time the standings are wiped for a new session (stage,
    /// a fresh green, reset). Lets anything that keeps its own per-driver
    /// history - the Best Streak and PBs columns - notice a new race started
    /// and start over, instead of carrying last race's numbers forward.
    /// </summary>
    public int SessionGeneration { get; private set; }

    public event Action? RaceCompleted;

    /// <summary>The leader has crossed onto the final lap of a lap-limited race. Fires once per race.</summary>
    public event Action<RacerStanding>? LastLapStarted;

    /// <summary>A timed race has one minute left. Fires once per race.</summary>
    public event Action? FinalMinute;

    /// <summary>Fired specifically when the configured time limit ends the race (as opposed to a lap limit or manual checkered flag) - maps to the "race time over / last lap" announcement.</summary>
    public event Action? TimeLimitReached;

    public event Action<string>? LogMessage;

    /// <summary>
    /// Manually adjusts a racer's lap count. Needed because no decoder is
    /// perfect: a missed pass undercounts, a double-read overcounts, and
    /// without a way to fix either, one bad read silently corrupts the
    /// results, any track record set, and the season standings downstream.
    /// Lap count is floored at zero. Returns false if the transponder
    /// isn't in this session.
    /// </summary>
    public bool AdjustLaps(string transponderId, int delta)
    {
        if (!_standings.TryGetValue(transponderId, out var standing))
        {
            return false;
        }

        var before = standing.Laps;
        var mergedAny = false;

        // Taking a lap off has to take its TIME with it, or the count and
        // the lap list disagree. It used to change the count alone, so a
        // double read corrected by hand left its phantom split in place: a
        // 3.5s "lap" still set the driver's best, still held the race's
        // fastest lap, and still sat in the best-of-N window qualifying
        // seeds the mains from.
        //
        // The last lap is folded into the one before it rather than thrown
        // away. Two crossings a moment apart were one real lap, and summing
        // them gives that lap back; dropping one would lose its time and
        // leave the laps no longer adding up to the elapsed time.
        //
        // Laps added by hand have no time of their own - adding keeps its
        // old behaviour, a count with nothing behind it - so they are the
        // first to go, and only once they are used up is a timed lap merged.
        for (var i = 0; i < -delta && standing.Laps > 0; i++)
        {
            if (standing.Laps > standing.LapTimesMs.Count)
            {
                standing.Laps--;
                continue;
            }

            var lastIndex = standing.LapTimesMs.Count - 1;

            if (standing.LapTimesMs.Count >= 2)
            {
                standing.LapTimesMs[lastIndex - 1] += standing.LapTimesMs[lastIndex];

                // A lap that ran through a crash recovery stays one when it
                // is folded into the lap before it: the merged lap still
                // spans the stoppage, so it must stay out of bests and
                // streaks exactly as the recovery lap was.
                if (standing.IsRecoveryLap(lastIndex) && !standing.IsRecoveryLap(lastIndex - 1))
                {
                    standing.RecoveryLapIndexes.Add(lastIndex - 1);
                }
            }

            standing.LapTimesMs.RemoveAt(lastIndex);

            // The removed index no longer exists; nothing may point at it.
            standing.RecoveryLapIndexes.RemoveAll(index => index >= standing.LapTimesMs.Count);

            // Kept in step with the lap list: the merged lap keeps the run
            // count it had, which is how the run stood when it ended.
            if (standing.StreakAtLap.Count > standing.LapTimesMs.Count)
            {
                standing.StreakAtLap.RemoveAt(standing.StreakAtLap.Count - 1);
            }

            standing.Laps--;
            mergedAny = true;
        }

        if (delta > 0)
        {
            standing.Laps = Math.Max(0, standing.Laps + delta);
        }

        if (mergedAny)
        {
            RecomputeAfterLapRemoval(standing);
        }

        Log($"Manual adjustment: {transponderId} laps {before} -> {standing.Laps}");
        SafeRaise(StandingAdjusted, h => h(standing), nameof(StandingAdjusted));
        return true;
    }

    /// <summary>
    /// Puts everything derived from a driver's lap list back in line with
    /// it, after a lap has been merged away: their best, their run, the
    /// lap the next one is judged against, and the race's fastest lap -
    /// which may have been the very lap that just disappeared.
    /// </summary>
    private void RecomputeAfterLapRemoval(RacerStanding standing)
    {
        // Index 0 is the start lap and never a best - see ProcessLapEvent.
        // Nor is a lap that ran through a crash recovery: it spans the
        // stoppage, and left in here it could become the driver's best (or
        // the yardstick below) the moment the laps around it were removed.
        standing.FastestLapMs = standing.LapTimesMs
            .Where((t, i) => i > 0 && t > 0 && !standing.IsRecoveryLap(i))
            .DefaultIfEmpty(0)
            .Min();

        standing.ConsecutiveValidLaps = standing.StreakAtLap.Count > 0 ? standing.StreakAtLap[^1] : 0;

        // A recovery lap is never the yardstick for the next one - the same
        // as ProcessLapEvent leaves it, where it zeroes the baseline.
        var lastLapIndex = standing.LapTimesMs.Count - 1;
        standing.StreakBaselineLapMs = lastLapIndex >= 1 && !standing.IsRecoveryLap(lastLapIndex)
            ? standing.LapTimesMs[lastLapIndex]
            : 0;

        // The run's average restarts from the lap that now ends the list.
        // Its history is not kept per lap, so it cannot be rebuilt exactly;
        // starting it again is the honest version.
        standing.StreakLapTimeSumMs = standing.StreakBaselineLapMs;
        standing.StreakLapCount = standing.StreakBaselineLapMs > 0 ? 1 : 0;

        // Ghosts never hold the race's fastest lap while there is a real
        // car in the race - the same rule the lap itself follows in
        // ProcessLapEvent.
        var realCarsRacing = _standings.Keys.Any(k => !GhostService.IsGhost(k));
        var holder = _standings.Values
            .Where(s => s.FastestLapMs > 0 && !(realCarsRacing && GhostService.IsGhost(s.TransponderId)))
            .OrderBy(s => s.FastestLapMs)

            // On a tie the lap that got there first keeps it, which is the
            // current holder if they are among the tied.
            .ThenBy(s => string.Equals(s.TransponderId, _raceFastestLapTransponder, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();

        _raceFastestLapMs = holder?.FastestLapMs ?? 0;
        _raceFastestLapTransponder = holder?.TransponderId ?? string.Empty;
    }

    /// <summary>
    /// Adds a time penalty (or credit, if negative) to a racer's elapsed
    /// time. Since standings sort by laps then elapsed time, adding time
    /// pushes a racer back among others on the same lap - the usual way a
    /// track-cutting or contact penalty is applied.
    /// </summary>
    public bool AdjustPenaltyTime(string transponderId, long deltaMs)
    {
        if (!_standings.TryGetValue(transponderId, out var standing))
        {
            return false;
        }

        // A credit can take back penalty time already given, but never more
        // than that: a negative total would be folded into elapsed on every
        // later lap and quietly hand the driver time they never lost.
        if (deltaMs < 0)
        {
            deltaMs = Math.Max(deltaMs, -Math.Max(0, standing.PenaltyMs));
        }

        standing.ElapsedMs = Math.Max(0, standing.ElapsedMs + deltaMs);
        standing.PenaltyMs += deltaMs;
        Log($"Penalty applied: {transponderId} {(deltaMs >= 0 ? "+" : "")}{deltaMs / 1000.0:0.0}s (total {standing.PenaltyMs / 1000.0:0.0}s)");

        // Safely, like every other event here: the penalty is already on the
        // standing, and a throwing listener must not come back out through
        // the button that gave it (or through EditStanding, losing its Undo).
        SafeRaise(StandingAdjusted, h => h(standing), nameof(StandingAdjusted));
        return true;
    }

    /// <summary>
    /// The jump-start penalty this driver still owes - held until their next
    /// counted lap books it into PenaltyMs - or zero when none is owed.
    ///
    /// Race Control asks so that Clear Penalties can be offered while a
    /// penalty is owed but not yet booked. It only looked at PenaltyMs, so a
    /// jumper credited with the start lap showed nothing to clear, and the
    /// penalty the director had meant to waive landed a lap later anyway.
    /// </summary>
    public long OwedJumpStartPenaltyMs(string transponderId) =>
        _pendingJumpStartPenalties.TryGetValue(transponderId, out var owed) ? Math.Max(0, owed) : 0;

    /// <summary>
    /// Wipes every second of penalty a driver is carrying - the buttons',
    /// the jump start's and the warnings' alike - and returns how much that
    /// was (zero when there was nothing to clear, or no such driver).
    ///
    /// The warnings themselves stay on the record, but the time each one is
    /// noted as having added is zeroed as well. Clearing used to leave it,
    /// so taking a warning back afterwards subtracted its time a second
    /// time: the total went negative and every later lap handed the driver
    /// that much time back.
    /// </summary>
    public long ClearPenalties(string transponderId)
    {
        if (!_standings.TryGetValue(transponderId, out var standing))
        {
            return 0;
        }

        // A jump-start penalty not yet booked goes too. It is only moved
        // into PenaltyMs on the driver's next counted lap, so a jumper who
        // was credited with the start lap and cleared before crossing again
        // had the whole penalty land on them one lap after it was cleared.
        var owedJumpStart = _pendingJumpStartPenalties.Remove(transponderId);

        if (standing.PenaltyMs == 0 && !owedJumpStart)
        {
            // Nothing carried - but a credit can have brought the total to
            // zero while a warning still notes the time it added, and that
            // would be taken off again if the warning were removed later.
            foreach (var warning in standing.Warnings)
            {
                warning.PenaltyMs = 0;
            }

            return 0;
        }

        var cleared = standing.PenaltyMs;

        // Subtracting the accumulated total restores the original elapsed
        // time exactly, rather than guessing at what it used to be.
        standing.ElapsedMs = Math.Max(0, standing.ElapsedMs - cleared);
        standing.PenaltyMs = 0;

        foreach (var warning in standing.Warnings)
        {
            warning.PenaltyMs = 0;
        }

        Log($"Penalties cleared: {transponderId} -{cleared / 1000.0:0.0}s (total 0.0s)"
            + (owedJumpStart ? ", and the jump-start penalty still owed was dropped." : string.Empty));
        SafeRaise(StandingAdjusted, h => h(standing), nameof(StandingAdjusted));
        return cleared;
    }

    /// <summary>
    /// What each warning in a race costs, set from the racing class when a
    /// race starts (as the jump-start rules are). Null follows
    /// WarningRules.DefaultLadder.
    /// </summary>
    public List<WarningStep>? WarningLadder { get; set; }

    /// <summary>Fires when race control warns a driver - for the voice call, the Big Screen banner and the log.</summary>
    public event Action<RacerStanding, RaceWarning>? WarningIssued;

    /// <summary>What the next warning for this driver would do, for the menus to say before it is given.</summary>
    public WarningStep NextWarningStep(string transponderId) =>
        WarningRules.StepFor(WarningLadder,
            (_standings.TryGetValue(transponderId, out var standing) ? standing.Warnings.Count : 0) + 1);

    /// <summary>
    /// Warns a driver, and applies whatever the ladder says their Nth
    /// warning of the race costs: nothing, a time penalty (booked into
    /// PenaltyMs exactly as the penalty buttons do, so the Penalty column
    /// and Clear Penalty see it), or a black flag. Returns the warning, or
    /// null if the driver isn't in this session.
    /// </summary>
    public RaceWarning? IssueWarning(string transponderId, string reason)
    {
        if (!_standings.TryGetValue(transponderId, out var standing))
        {
            return null;
        }

        var number = standing.Warnings.Count + 1;
        var step = WarningRules.StepFor(WarningLadder, number);

        var warning = new RaceWarning
        {
            AtUtc = DateTime.UtcNow,
            RaceTimeMs = RaceStartUtc.HasValue && State is RaceState.Racing or RaceState.Finished
                ? (long)ElapsedRaceTime.TotalMilliseconds
                : 0,
            Number = number,
            Reason = (reason ?? string.Empty).Trim(),
            Action = step.Action,
            PenaltyMs = step.Action == WarningAction.TimePenalty ? Math.Max(0, step.PenaltySeconds) * 1000L : 0,
        };

        standing.Warnings.Add(warning);

        if (warning.PenaltyMs > 0)
        {
            standing.ElapsedMs = Math.Max(0, standing.ElapsedMs + warning.PenaltyMs);
            standing.PenaltyMs += warning.PenaltyMs;
        }

        Log($"Warning issued: {transponderId} - {warning.Summary()}");

        // One listener at a time: a Big Screen banner that throws must not
        // stop the board being told, nor lose the Undo the caller captures
        // once this returns.
        SafeRaise(WarningIssued, h => h(standing, warning), nameof(WarningIssued));
        SafeRaise(StandingAdjusted, h => h(standing), nameof(StandingAdjusted));
        return warning;
    }

    /// <summary>
    /// Takes back a driver's most recent warning - a mis-click, or the wrong
    /// car - along with any time it added. Returns the warning removed, or
    /// null when there was none.
    /// </summary>
    public RaceWarning? RemoveLastWarning(string transponderId)
    {
        if (!_standings.TryGetValue(transponderId, out var standing) || standing.Warnings.Count == 0)
        {
            return null;
        }

        var warning = standing.Warnings[^1];
        standing.Warnings.RemoveAt(standing.Warnings.Count - 1);

        // Never more than the driver is still carrying: time already taken
        // off by a credit is not given back twice.
        var refundMs = Math.Min(warning.PenaltyMs, Math.Max(0, standing.PenaltyMs));

        if (refundMs > 0)
        {
            standing.ElapsedMs = Math.Max(0, standing.ElapsedMs - refundMs);
            standing.PenaltyMs -= refundMs;
        }

        Log($"Warning removed: {transponderId} - {warning.Summary()}");
        SafeRaise(StandingAdjusted, h => h(standing), nameof(StandingAdjusted));
        return warning;
    }

    /// <summary>
    /// One driver's figures as they stood either side of a correction made
    /// by hand - a lap added or taken off, a penalty given or cleared, a
    /// warning taken back - so the app's Undo can reverse exactly that
    /// correction. See EditStanding and UndoStandingEdit.
    ///
    /// Only this one driver, and only what the correction touched, rather
    /// than a snapshot of the whole race: the race keeps running after a
    /// correction, and putting the whole board back would throw away every
    /// lap counted since.
    /// </summary>
    public sealed class StandingEdit
    {
        // Set by EditStanding only - internal, and not "required" (a
        // required member may not be less visible than its public class).
        internal string TransponderId { get; init; } = string.Empty;
        internal int Generation { get; init; }

        internal int LapsBefore { get; init; }
        internal List<long> LapTimesBefore { get; init; } = new();
        internal List<int> StreakBefore { get; init; } = new();
        internal List<int> RecoveryBefore { get; init; } = new();
        internal long FastestLapBefore { get; init; }
        internal int ConsecutiveBefore { get; init; }
        internal long StreakBaselineBefore { get; init; }
        internal long StreakSumBefore { get; init; }
        internal int StreakCountBefore { get; init; }
        internal long PenaltyBefore { get; init; }
        internal List<RaceWarning> WarningsBefore { get; init; } = new();
        internal List<long> WarningPenaltiesBefore { get; init; } = new();
        internal long? OwedJumpStartBefore { get; init; }

        // Stamped once the correction has been made.
        internal int LapsAfter { get; set; }
        internal List<long> LapTimesAfter { get; set; } = new();
        internal int StreakCountAfter { get; set; }
        internal long PenaltyAfter { get; set; }
        internal List<RaceWarning> WarningsAfter { get; set; } = new();

        // The time each warning in WarningsBefore noted once the correction
        // was made, in the same order - so Undo only puts back the notes the
        // correction itself changed (Clear Penalty zeroes them), and leaves
        // alone one cleared by a later, separate press.
        internal List<long> WarningPenaltiesAfter { get; set; } = new();

        // Whether the correction itself dropped a jump-start penalty still
        // owed. Only then does Undo put it back: one still owed after the
        // correction and booked on a lap since must not be owed twice.
        internal bool OwedJumpStartCleared { get; set; }

        internal bool LapsChanged =>
            LapsAfter != LapsBefore || !LapTimesAfter.SequenceEqual(LapTimesBefore);
    }

    /// <summary>
    /// Runs a correction to one driver (change returns false when it did
    /// nothing) and hands back what Undo needs to reverse it, or null when
    /// the driver isn't in this session or nothing changed.
    /// </summary>
    public StandingEdit? EditStanding(string transponderId, Func<bool> change)
    {
        if (!_standings.TryGetValue(transponderId, out var standing))
        {
            return null;
        }

        var edit = new StandingEdit
        {
            TransponderId = standing.TransponderId,
            Generation = SessionGeneration,
            LapsBefore = standing.Laps,
            LapTimesBefore = standing.LapTimesMs.ToList(),
            StreakBefore = standing.StreakAtLap.ToList(),
            RecoveryBefore = standing.RecoveryLapIndexes.ToList(),
            FastestLapBefore = standing.FastestLapMs,
            ConsecutiveBefore = standing.ConsecutiveValidLaps,
            StreakBaselineBefore = standing.StreakBaselineLapMs,
            StreakSumBefore = standing.StreakLapTimeSumMs,
            StreakCountBefore = standing.StreakLapCount,
            PenaltyBefore = standing.PenaltyMs,
            WarningsBefore = standing.Warnings.ToList(),
            WarningPenaltiesBefore = standing.Warnings.Select(w => w.PenaltyMs).ToList(),
            OwedJumpStartBefore = _pendingJumpStartPenalties.TryGetValue(transponderId, out var owed) ? (long?)owed : null,
        };

        if (!change())
        {
            return null;
        }

        edit.LapsAfter = standing.Laps;
        edit.LapTimesAfter = standing.LapTimesMs.ToList();
        edit.StreakCountAfter = standing.StreakAtLap.Count;
        edit.PenaltyAfter = standing.PenaltyMs;
        edit.WarningsAfter = standing.Warnings.ToList();

        // Read off the same warning objects as before the change: a warning
        // taken off the board still carries the note it had.
        edit.WarningPenaltiesAfter = edit.WarningsBefore.Select(w => w.PenaltyMs).ToList();
        edit.OwedJumpStartCleared = edit.OwedJumpStartBefore is not null && !_pendingJumpStartPenalties.ContainsKey(transponderId);

        var nothingChanged = !edit.LapsChanged
                             && edit.PenaltyAfter == edit.PenaltyBefore
                             && edit.WarningsAfter.SequenceEqual(edit.WarningsBefore)
                             && standing.Warnings.Select(w => w.PenaltyMs).SequenceEqual(edit.WarningPenaltiesBefore)
                             && !edit.OwedJumpStartCleared;

        return nothingChanged ? null : edit;
    }

    /// <summary>
    /// Reverses one correction made through EditStanding, leaving anything
    /// that happened to the driver since - laps counted after it, a later
    /// penalty - where it is.
    ///
    /// Throws (which is how an undo says it could not be done - UndoService
    /// reports it) when the race it was made in has gone, or when the
    /// driver's laps were corrected again since in a way it cannot see past.
    /// </summary>
    public void UndoStandingEdit(StandingEdit edit)
    {
        if (edit.Generation != SessionGeneration || !_standings.TryGetValue(edit.TransponderId, out var standing))
        {
            throw new InvalidOperationException(
                "that race has been reset or replaced since, so the correction is no longer on the board. Nothing was changed.");
        }

        // Whether the driver has crossed the line since, taken before
        // anything below moves the lap list - see the jump-start penalty.
        var crossedSince = standing.LapTimesMs.Count > edit.LapTimesAfter.Count;

        if (edit.LapsChanged)
        {
            // Laps counted since the correction sit after the ones it left,
            // so they are carried over on top of the laps as they were
            // before it. If the lap list no longer starts with what the
            // correction left, it was corrected again since, and there is
            // no telling what to put back.
            var afterCount = edit.LapTimesAfter.Count;
            var lapsSince = standing.LapTimesMs.Count - afterCount;

            if (lapsSince < 0 || !standing.LapTimesMs.Take(afterCount).SequenceEqual(edit.LapTimesAfter)
                || standing.Laps - edit.LapsAfter < lapsSince)
            {
                throw new InvalidOperationException(
                    "that driver's laps have been corrected again since, and undoing this now would undo that too. Nothing was changed.");
            }

            var newTimes = standing.LapTimesMs.Skip(afterCount).ToList();
            var newStreaks = standing.StreakAtLap.Skip(edit.StreakCountAfter).ToList();
            var shift = edit.LapTimesBefore.Count - afterCount;
            var newRecovery = standing.RecoveryLapIndexes.Where(i => i >= afterCount).Select(i => i + shift).ToList();
            var lapsCountedSince = standing.Laps - edit.LapsAfter;

            standing.LapTimesMs.Clear();
            standing.LapTimesMs.AddRange(edit.LapTimesBefore);
            standing.LapTimesMs.AddRange(newTimes);

            standing.StreakAtLap.Clear();
            standing.StreakAtLap.AddRange(edit.StreakBefore);
            standing.StreakAtLap.AddRange(newStreaks);

            standing.RecoveryLapIndexes.Clear();
            standing.RecoveryLapIndexes.AddRange(edit.RecoveryBefore);
            standing.RecoveryLapIndexes.AddRange(newRecovery);

            standing.Laps = Math.Max(0, edit.LapsBefore + lapsCountedSince);

            if (newTimes.Count == 0)
            {
                // Nothing counted since: the driver's figures go back exactly.
                standing.FastestLapMs = edit.FastestLapBefore;
                standing.ConsecutiveValidLaps = edit.ConsecutiveBefore;
                standing.StreakBaselineLapMs = edit.StreakBaselineBefore;
                standing.StreakLapTimeSumMs = edit.StreakSumBefore;
                standing.StreakLapCount = edit.StreakCountBefore;
            }

            // Works the best lap, the run and the race's fastest lap out again
            // from the list as it now stands.
            var fastest = standing.FastestLapMs;
            var consecutive = standing.ConsecutiveValidLaps;
            var baseline = standing.StreakBaselineLapMs;
            var streakSum = standing.StreakLapTimeSumMs;
            var streakCount = standing.StreakLapCount;

            RecomputeAfterLapRemoval(standing);

            if (newTimes.Count == 0)
            {
                // Kept as restored above - only the race's fastest lap needed
                // working out again.
                standing.FastestLapMs = fastest;
                standing.ConsecutiveValidLaps = consecutive;
                standing.StreakBaselineLapMs = baseline;
                standing.StreakLapTimeSumMs = streakSum;
                standing.StreakLapCount = streakCount;
            }
        }

        // Warnings the correction took away go back where they were, and ones
        // it added (none of today's corrections do) come off - each by
        // reference, so a warning given since is left alone, and one taken
        // back since by a separate press stays taken back.
        foreach (var added in edit.WarningsAfter.Except(edit.WarningsBefore).ToList())
        {
            standing.Warnings.Remove(added);
        }

        for (var i = 0; i < edit.WarningsBefore.Count; i++)
        {
            var warning = edit.WarningsBefore[i];

            if (!edit.WarningsAfter.Contains(warning) && !standing.Warnings.Contains(warning))
            {
                standing.Warnings.Insert(Math.Min(i, standing.Warnings.Count), warning);
            }

            // The time each notes as having added - Clear Penalty zeroes it.
            // Only where this correction changed it, and it still reads as
            // the correction left it: a note zeroed by a later Clear
            // Penalty stays zeroed, as that press's time stays off.
            if (standing.Warnings.Contains(warning)
                && edit.WarningPenaltiesAfter[i] != edit.WarningPenaltiesBefore[i]
                && warning.PenaltyMs == edit.WarningPenaltiesAfter[i])
            {
                warning.PenaltyMs = edit.WarningPenaltiesBefore[i];
            }
        }

        // Penalty time is put back as a difference rather than a value, so a
        // penalty given since stays on. Taking time off never goes below
        // zero - the same floor AdjustPenaltyTime keeps - or undoing a
        // penalty that a later Clear Penalty already removed would leave a
        // negative total, handing the driver that much time on every lap.
        var penaltyDelta = edit.PenaltyBefore - edit.PenaltyAfter;

        if (penaltyDelta < 0)
        {
            penaltyDelta = Math.Max(penaltyDelta, -Math.Max(0, standing.PenaltyMs));
        }

        if (penaltyDelta != 0)
        {
            standing.ElapsedMs = Math.Max(0, standing.ElapsedMs + penaltyDelta);
            standing.PenaltyMs += penaltyDelta;
        }

        if (edit.OwedJumpStartCleared && edit.OwedJumpStartBefore is { } owed
            && !_pendingJumpStartPenalties.ContainsKey(edit.TransponderId))
        {
            if (crossedSince && owed > 0)
            {
                // The driver has counted a lap since, and that lap is where
                // the owed penalty would have been booked - so it is booked
                // now, rather than left waiting for a lap that has been.
                standing.ElapsedMs = Math.Max(0, standing.ElapsedMs + owed);
                standing.PenaltyMs += owed;
            }
            else
            {
                _pendingJumpStartPenalties[edit.TransponderId] = owed;
            }
        }

        Log($"Undo: correction to {edit.TransponderId} reversed (laps {standing.Laps}, penalty {standing.PenaltyMs / 1000.0:0.0}s)");
        SafeRaise(StandingAdjusted, h => h(standing), nameof(StandingAdjusted));
    }

    public IReadOnlyList<RacerStanding> GetOrderedStandings() =>
        _standings.Values.OrderByDescending(s => s.Laps).ThenBy(s => s.ElapsedMs).ToList();

    /// <summary>
    /// The leader of the race proper: the first real car, passing over any
    /// ghost ahead of it - a ghost can never end the race, so its laps are
    /// not the ones counting down to the flag. In a field of nothing but
    /// ghosts, simply the first of them. Null before anyone has crossed.
    /// </summary>
    public RacerStanding? RaceLeader()
    {
        var ordered = GetOrderedStandings();
        return ordered.FirstOrDefault(s => !GhostService.IsGhost(s.TransponderId)) ?? ordered.FirstOrDefault();
    }

    /// <summary>
    /// A human-readable "X laps to go" or "X:XX remaining" string based on
    /// whichever limit is configured for this race, or empty if neither is
    /// set or the race isn't currently in progress. Shared by the Race
    /// Control countdown banner and Big Screen mode so both always agree.
    /// </summary>
    public string GetCountdownText()
    {
        // The warm-up has its own clock, and every screen that shows this
        // line (Race Control, Big Screen, the web pages) should count it down.
        if (IsWarmingUp)
        {
            var left = WarmUpRemaining;
            return $"WARM-UP {(int)left.TotalMinutes}:{left.Seconds:00} LEFT";
        }

        if (State != RaceState.Racing)
        {
            return string.Empty;
        }

        if (LapLimit.HasValue)
        {
            // Counted from the real leader: a ghost out in front cannot end
            // the race (see ProcessLapEvent), so its laps are not the ones
            // still to go.
            var leaderLaps = RaceLeader()?.Laps ?? 0;
            var lapsToGo = Math.Max(0, LapLimit.Value - leaderLaps);
            return lapsToGo == 1 ? "1 LAP TO GO" : $"{lapsToGo} LAPS TO GO";
        }

        if (TimeLimit.HasValue && RaceStartUtc.HasValue)
        {
            var remaining = TimeLimit.Value - ElapsedRaceTime;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }
            // From total minutes, as the race clock does: mm on its own
            // wraps at the hour, so a two-hour race read "00:00" at the start.
            return $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00} REMAINING";
        }

        return string.Empty;
    }

    // ==================== Warm-up ====================

    /// <summary>
    /// When the warm-up runs out, or null when there is no warm-up on.
    ///
    /// A warm-up is timed free laps before a race is staged. It is not a
    /// RaceState of its own on purpose: the session sits Idle (or Finished)
    /// through it, so passes are ignored exactly as they are between races
    /// and every screen and guard that reads the state keeps working
    /// unchanged. Race Control owns the timer; this only records that one
    /// is running, so the clock, Big Screen and web pages can show it.
    /// </summary>
    public DateTime? WarmUpEndsUtc { get; private set; }

    /// <summary>How long the running warm-up was set for - for the clock's progress ring.</summary>
    public TimeSpan WarmUpLength { get; private set; }

    public bool IsWarmingUp => WarmUpEndsUtc.HasValue;

    /// <summary>Time left in the warm-up, rounded UP to the second so it reads 0:01 until it is really over.</summary>
    public TimeSpan WarmUpRemaining
    {
        get
        {
            if (WarmUpEndsUtc is not { } end)
            {
                return TimeSpan.Zero;
            }

            var left = end - DateTime.UtcNow;
            return left <= TimeSpan.Zero
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds));
        }
    }

    /// <summary>Raised when a warm-up opens or ends, for whatever shows it.</summary>
    public event Action? WarmUpChanged;

    /// <summary>
    /// Opens a warm-up. Only between races - over a staged, counting-down
    /// or running race it would mean nothing, so it is refused.
    /// </summary>
    public bool BeginWarmUp(TimeSpan length)
    {
        if (State is not (RaceState.Idle or RaceState.Finished) || length <= TimeSpan.Zero)
        {
            return false;
        }

        WarmUpLength = length;
        WarmUpEndsUtc = DateTime.UtcNow + length;
        Log($"Warm-up open - {RaceFormat.DescribeWarmUp((int)length.TotalSeconds)} of free laps. Passes are not counted.");
        SafeRaise(WarmUpChanged, h => h(), nameof(WarmUpChanged));
        return true;
    }

    /// <summary>Closes the warm-up, if one is open. Safe to call when none is.</summary>
    public void EndWarmUp(string reason = "Warm-up over.")
    {
        if (WarmUpEndsUtc is null)
        {
            return;
        }

        WarmUpEndsUtc = null;
        Log(reason);
        SafeRaise(WarmUpChanged, h => h(), nameof(WarmUpChanged));
    }

    public void Stage()
    {
        _standings.Clear();
        ClearAlternateDecisions();
        SessionGeneration++;

        // The jump-start window OPENS here.
        //
        // Anything flagged from now until the green belongs to the race
        // about to run; anything before it belonged to the last one. This
        // clear used to sit in GreenFlag, which is the one moment it must
        // not: every jump start is by definition recorded BEFORE the
        // green, so clearing there threw away exactly the penalties it was
        // meant to apply. The comment there said jump starts "belong to
        // the race that's beginning" - which is true, and is the argument
        // for clearing at staging rather than at the start.
        ClearJumpStartWindow();

        SetState(RaceState.Staging);
        SetFlag(FlagState.None);
        Log("Staging - racers called to the grid.");
    }

    /// <summary>
    /// Opens a fresh jump-start window: forgets who jumped, what they owe
    /// and where they crossed. Called wherever a new race's start begins -
    /// Stage, a countdown that did not come through Stage, a green with no
    /// countdown at all - so nothing flagged in one race reaches the next.
    /// </summary>
    private void ClearJumpStartWindow()
    {
        _jumpStarters.Clear();
        _pendingJumpStartPenalties.Clear();
        _jumpStartCrossingMs.Clear();
    }

    public void StartCountdown()
    {
        // Read before the state moves on. A countdown straight over a race
        // that is running starts the next race there and then, and the one
        // running is abandoned - Race Control has asked, or the phone has.
        var overRunningRace = State == RaceState.Racing;

        // A countdown that did not come through Stage opens the jump-start
        // window itself. Only Stage used to, so a countdown started straight
        // from a finished (or idle) session carried the LAST race's jump
        // starters in: their red row tint, their owed penalty and their
        // pre-green crossing - which the green then credited as a start lap
        // nobody had driven in this race.
        //
        // Not when a countdown is already running: a crossing flagged during
        // it belongs to the race about to start, and restarting the lights
        // does not make it any less of a jump.
        if (State is not (RaceState.Staging or RaceState.Countdown))
        {
            ClearJumpStartWindow();
        }

        SetState(RaceState.Countdown);

        // The abandoned race's flag goes with it, as it does at Stage. It
        // used to stay out: a countdown confirmed over a race under yellow
        // (or red) still had that flag showing, the automatic start took it
        // for a caution thrown during THIS start and stopped before the
        // green, and the green pressed by hand was then announced as the old
        // race resuming rather than the new one starting. A yellow or red
        // thrown during the new countdown comes after this, so it still
        // stops the automatic start. A countdown from a finished race keeps
        // that race's checkered, as before - it stops nothing.
        if (overRunningRace)
        {
            SetFlag(FlagState.None);
        }

        Log("Countdown started.");
    }

    /// <param name="currentDeviceTimeMs">
    /// The most recent device timer value seen (from any lap event) - used
    /// as the zero-point lap times are measured from.
    /// </param>
    /// <summary>
    /// Starts the race, OR resumes it from a yellow/red caution.
    ///
    /// The distinction matters: this used to unconditionally clear
    /// standings and reset the start time, which is right for starting a
    /// race but destroys one that's already running - so throwing a yellow
    /// and then going back to green wiped every lap counted so far. Now a
    /// green thrown while the race is already underway only changes the
    /// flag, leaving laps and elapsed time intact.
    /// </summary>
    /// <summary>
    /// Called before a race actually starts, to let the app hold the flag
    /// until the countdown audio has finished.
    ///
    /// A callback rather than a reference to the announcer: the session
    /// deliberately knows nothing about audio, and every start path -
    /// manual, automated, demo, quick race, practice - routes through
    /// GreenFlag, so gating here covers all of them at once. Patching each
    /// caller separately is what left demo and quick-start still cutting
    /// the cue off.
    /// </summary>
    public Func<Task>? BeforeGreenFlagAsync { get; set; }

    /// <summary>
    /// Starts the race after waiting on <see cref="BeforeGreenFlagAsync"/>.
    ///
    /// Use this from UI paths. GreenFlag itself stays synchronous so the
    /// resume-from-caution case, which must be immediate, is unaffected.
    /// </summary>
    /// <summary>
    /// Starts the race after the audio gate, stamping the start with the
    /// decoder clock as estimated at the moment the flag actually drops.
    ///
    /// The overload taking a device time captured it BEFORE waiting on the
    /// countdown audio, so the recorded start was several seconds earlier
    /// than the real one. Use this for real-hardware starts; the demo keeps
    /// passing its own simulated clock.
    /// </summary>
    public Task GreenFlagAfterAudioAsync() => GreenFlagAfterAudioAsync(null);

    public Task GreenFlagAfterAudioAsync(long currentDeviceTimeMs) => GreenFlagAfterAudioAsync((long?)currentDeviceTimeMs);

    private async Task GreenFlagAfterAudioAsync(long? currentDeviceTimeMs)
    {
        Log($"[audio-trace] green requested (state={State}, gate={(BeforeGreenFlagAsync is null ? "NOT WIRED" : "wired")})");

        // What the session looked like when the green was asked for, so the
        // wait below can tell whether anybody overruled it in the meantime.
        var generationAtRequest = SessionGeneration;
        var stateAtRequest = State;
        var flagAtRequest = Flag;

        // Resuming from a caution is a live instruction - never delayed.
        if (State != RaceState.Racing && BeforeGreenFlagAsync is { } gate)
        {
            try
            {
                await gate();
            }
            catch
            {
                // An audio wait must never stop a race from starting.
            }

            // The wait can take several seconds, and the green was asked for
            // at the START of it. Reset, Stage or Red pressed during those
            // seconds - or a demo stopped, which resets the session - is a
            // later decision, and it wins. Dropping the green regardless
            // started a race the director had just called off. A checkered
            // used to be one of them as well; it is refused now until a race
            // has gone green (see CheckeredFlag), so it leaves a start alone.
            //
            // Stage and Reset both move SessionGeneration on. The state is
            // checked too, for what moves the race along without that - a
            // countdown pressed meanwhile, or an undo putting another race
            // back. A red only changes the flag, so it is checked on its own;
            // a red that was already out when the green was asked for is the
            // one this green is meant to clear, so it does not count.
            var overruledBy =
                SessionGeneration != generationAtRequest ? "the session was reset or re-staged"
                : State != stateAtRequest ? $"the race moved to {State}"
                : Flag == FlagState.Red && flagAtRequest != FlagState.Red ? "a red flag came out"
                : null;

            if (overruledBy is not null)
            {
                Log($"Green flag not dropped - {overruledBy} while the countdown audio was finishing.");
                return;
            }
        }

        Log("[audio-trace] gate released - dropping green now");
        GreenFlag(currentDeviceTimeMs ?? EstimatedDeviceTimeNowMs);
    }

    /// <summary>
    /// Starts the race with its zero point one millisecond before a given
    /// crossing, for a start that is triggered BY that crossing - practice
    /// going green as the first car comes over the line.
    ///
    /// Stamping such a start from EstimatedDeviceTimeNowMs could put the
    /// zero point a few milliseconds AFTER the crossing that triggered it -
    /// the estimate is the last pass plus the wall time since, and the
    /// triggering pass is that last pass - so when the crossing was then
    /// processed its time since the start came out at zero or less, and the
    /// car's first lap was thrown away. One millisecond before it is the
    /// smallest start that still counts that crossing as the start lap.
    ///
    /// Otherwise exactly GreenFlag: resumes from a caution when the race is
    /// already running, and the crossing must be on the decoder's clock (a
    /// DirectLapEvent's DeviceTimeMs), not a ghost's.
    /// </summary>
    public void GreenFlagBefore(long crossingDeviceTimeMs) => GreenFlag(crossingDeviceTimeMs - 1);

    public void GreenFlag(long currentDeviceTimeMs)
    {
        // Read before anything below moves the state on.
        var startedFrom = State;

        if (State == RaceState.Racing)
        {
            // A green while racing resumes from a yellow or a red, and only
            // then. Under green there is nothing to resume, and under the
            // checkered the race is on its last lap - a green cannot take the
            // flag back in, since the finish carries on whatever the flag
            // shows. Either way nothing changes, and no flag change goes out.
            //
            // It used to set the flag again regardless, and Race Control,
            // hearing a green that had not followed a yellow or a red, took
            // it for the start: the horn and the "race started" call went out
            // again in the middle of the race, cutting across whatever was
            // being said - and on the last lap the flag then showed green over
            // a race that was finishing.
            if (Flag is FlagState.Green or FlagState.Checkered)
            {
                Log(Flag == FlagState.Green
                    ? "Green flag - already green, nothing changed."
                    : "Green flag not thrown - the checkered flag is out and the race is finishing.");
                return;
            }

            EndRedFlagPeriod();
            GreenStartedTheRace = false;

            // A yellow or a red thrown during the last lap stops or slows the
            // field, but it does not un-finish the race: cars are still
            // taking the flag. So "resume" puts the checkered back out rather
            // than showing green - which the board read as the race running
            // on and the announcer called as "racing resumes". A race
            // restored from the journal on its last lap comes back under red
            // and resumes the same way.
            if (IsFinishing)
            {
                SetFlag(FlagState.Checkered);
                Log("Checkered flag back out - the race resumes on its last lap.");
                return;
            }

            SetFlag(FlagState.Green);
            Log("Green flag - racing resumes.");
            return;
        }

        EndRedFlagPeriod();
        _accumulatedRedTime = TimeSpan.Zero;
        _raceFastestLapMs = 0;
        _raceFastestLapTransponder = string.Empty;
        _lastLapAnnounced = false;
        _finalMinuteAnnounced = false;

        // NOT cleared here. See Stage().
        //
        // Every jump start is recorded between staging and the green, so
        // clearing at the green wiped the lot - the penalty never reached
        // a standing, and the red row tint vanished the instant the race
        // began. Staging is where the window opens and where the previous
        // race's flags are dropped.
        //
        // The one case still handled here is a green with no staging and no
        // countdown at all - straight from Idle or Finished. Nothing can
        // have been flagged for THIS race, so whatever is in the set is left
        // over from the last one. A countdown that skipped Stage has already
        // opened its own window - see StartCountdown.
        if (startedFrom is not (RaceState.Staging or RaceState.Countdown))
        {
            ClearJumpStartWindow();
        }

        // The schedule survives a green (it was built just before it), but
        // which releases have been CALLED belongs to the race starting now.
        _releaseCalled.Clear();

        IsFinishing = false;
        _finishingSinceUtc = null;
        _tookTheFlag.Clear();

        _standings.Clear();
        ClearAlternateDecisions();

        // Where everyone started, for as long as anybody knows it. The grid
        // is whatever the caller resolved - a round's roster, the last
        // race's finishing order - and is empty when neither existed, which
        // leaves every start position at zero rather than invented.
        _startPositions.Clear();
        for (var i = 0; i < _grid.Count; i++)
        {
            _startPositions[_grid[i]] = i + 1;
        }

        // A hand-released grid goes on the board at the green, before anyone
        // has crossed anything - they are in the race, just not yet moving,
        // and the director has to be able to see them to release them.
        if (HandReleaseArmed)
        {
            foreach (var transponder in _grid)
            {
                _standings[transponder] = new RacerStanding
                {
                    TransponderId = transponder,
                    StartPosition = StartPositionOf(transponder),
                };
            }

            Log($"Hand-released start - {Plural.Of(_grid.Count, "car")} held at the line.");
        }

        SessionGeneration++;
        _raceStartDeviceTimeMs = currentDeviceTimeMs;

        // Anyone already past the line when the green dropped starts the
        // race having completed their start lap - because they had.
        //
        // Without this their pre-green pass was simply discarded, so the
        // rest of the grid banked a start lap a second after the green
        // while the jumper did not score until a full lap later. That is
        // roughly ten seconds of elapsed time, carried all the way to the
        // flag, for a jump of two tenths - a punishment nobody chose, on
        // top of whatever penalty the class actually specifies.
        //
        // The zero in LapTimesMs is the start-lap slot: the run from the
        // green to a first crossing, which for them was already over.
        // Every consumer skips index 0 already.
        //
        // Held starts are excluded. All of this rests on the green being
        // the instant the start lap ends for the whole grid. In a
        // hand-released or staggered start it isn't - each driver's lap
        // begins at their own release - so there is no shared moment to
        // credit a lap at, and a car that moved early simply moved early.
        //
        // A qualifier is excluded for the same reason from the other side.
        // Each driver's clock zeroes at their own first crossing AFTER the
        // green, so there is no start lap to lose by being past the line -
        // and crediting one handed the jumper a lap the rest of the grid
        // could not have, on a clock the jump had already cost them nothing
        // on.
        if (!JumpStartCostsTheLap && !HandReleaseArmed && StartType != RaceStartType.Qualifier)
        {
            foreach (var (transponder, crossedAt) in _jumpStartCrossingMs)
            {
                if (ReleaseOffsetMsFor(transponder) > 0)
                {
                    continue; // their lap starts at their release, not here
                }

                // A jump start is a driver anticipating the lights, so the
                // crossing sits a moment before the green. A pass from long
                // before it is a car being lifted onto the grid across the
                // loop while the field is still being called - the driver is
                // standing at the rostrum, not a lap up, and crediting that
                // would hand them a lap nobody drove.
                if (crossedAt < currentDeviceTimeMs - JumpStartCreditWindowMs)
                {
                    continue;
                }

                var jumper = _standings.TryGetValue(transponder, out var existing)
                    ? existing
                    : new RacerStanding
                    {
                        TransponderId = transponder,
                        StartPosition = StartPositionOf(transponder),
                    };

                jumper.Laps = 1;
                jumper.ElapsedMs = 0;
                jumper.LapTimesMs.Clear();
                jumper.LapTimesMs.Add(0);

                // Measured from where they actually crossed, so their
                // first real lap comes out a full lap long rather than
                // short by however early they went.
                jumper.LastLapDeviceTimeMs = crossedAt;
                jumper.LastSeenDeviceTimeMs = crossedAt;

                _standings[transponder] = jumper;

                Log($"{transponder} was past the line at the green - credited with the start lap.");
            }
        }
        _startReferenceChecked = false;
        RaceStartUtc = DateTime.UtcNow;
        SetState(RaceState.Racing);
        GreenStartedTheRace = true;
        SetFlag(FlagState.Green);
        Log("Green flag - racing begins.");
    }

    public void YellowFlag()
    {
        // Leaving a red for a yellow is racing again - laps count under
        // yellow - so the stoppage ends here, not at the next green. Without
        // this the race clock stayed frozen for however long the field ran
        // under caution, and a timed race ran long by exactly that much.
        EndRedFlagPeriod();
        SetFlag(FlagState.Yellow);
        Log("Yellow flag - caution.");
    }

    public void RedFlag()
    {
        if (Flag != FlagState.Red)
        {
            _redFlagStartedUtc = DateTime.UtcNow;
        }
        SetFlag(FlagState.Red);
        Log("Red flag - race clock paused, laps will not be counted until green resumes.");
    }

    /// <summary>Banks the time spent under the current red flag and clears it. Called when racing resumes or the session resets.</summary>
    private void EndRedFlagPeriod()
    {
        if (_redFlagStartedUtc is { } redStart)
        {
            _accumulatedRedTime += DateTime.UtcNow - redStart;
            _redFlagStartedUtc = null;

            // Nobody can take a flag while the field is stopped, so the wait
            // for stragglers starts again when it moves. Otherwise a red
            // during the last lap - or a race restored from the journal,
            // which always comes back under red - was called on the first
            // tick after the restart, before anyone had had the chance.
            if (IsFinishing)
            {
                _finishingSinceUtc = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// The checkered flag thrown by hand: the race ends now, for everyone.
    ///
    /// Only a race that is running can be finished this way - in any other
    /// state nothing happens. It used to finish from ANY state, and a finish
    /// is what saves the result. Thrown during a countdown that had skipped
    /// Stage, it saved whatever was still on the board as a brand-new race:
    /// the last race's rows, already in the history, or the rows of the race
    /// that countdown had just abandoned - a demo's invented laps among them,
    /// once the takeover had cleared the demo flag - and it could mark the
    /// round picked for the new race as run with them. A second checkered
    /// after the finish saved the same race all over again. During staging,
    /// or with nothing staged, there was nothing to save, but the finish
    /// fanfare and call still went out for a race that never ran. And where
    /// Stage or Countdown had abandoned a running race, the finish took away
    /// the Undo that could still have put it back.
    ///
    /// Race Control checks first, so it can say why to whoever pressed it
    /// (see its Checkered action); this keeps the rule for any other caller.
    /// The lap limit, the time limit and the wait for stragglers end a race
    /// too, and each of them is only ever reached while it is racing.
    /// </summary>
    public void CheckeredFlag()
    {
        if (State != RaceState.Racing)
        {
            Log($"Checkered flag not thrown - the session is at {State}, and only a race that is running can be finished.");
            return;
        }

        Finish(timeLimitReached: false);
    }

    private void Finish(bool timeLimitReached)
    {
        // Read before it is cleared. A timed race finishing on the next
        // crossing has already said "time limit reached" once, when
        // BeginFinishing put the checkered out; saying it again here, as
        // the last car comes home a lap later, made the announcer call the
        // last lap a second time over the end of the race.
        var alreadyAnnouncedTimeLimit = IsFinishing && _finishingOnTime;

        IsFinishing = false;
        _finishingSinceUtc = null;

        // Read while the state is still Racing, so it is the live clock -
        // and a finish under red stops it where the red did.
        _elapsedAtFinish = ElapsedRaceTime;

        SetFlag(FlagState.Checkered);
        SetState(RaceState.Finished);

        if (timeLimitReached && !alreadyAnnouncedTimeLimit)
        {
            Log("Time limit reached - last lap.");
            SafeRaise(TimeLimitReached, h => h(), nameof(TimeLimitReached));
        }

        Log("Checkered flag - race complete.");

        // Every subscriber on this path is called on its own - see
        // SafeRaise. RaceCompleted is the one that saves the result, and it
        // comes last, so a fault in anything raised before it used to mean
        // the race simply never reached the history.
        SafeRaise(RaceCompleted, h => h(), nameof(RaceCompleted));
    }

    /// <summary>
    /// A complete point-in-time copy of a race session, used to make Reset
    /// undoable. Standings are deep-copied rather than referenced, because
    /// Reset clears the live collection and the originals would otherwise
    /// be mutated or discarded out from under the snapshot.
    /// </summary>
    public sealed record SessionSnapshot(
        List<RacerStanding> Standings,
        RaceState State,
        FlagState Flag,
        DateTime? RaceStartUtc,
        long? RaceStartDeviceTimeMs,
        TimeSpan AccumulatedRedTime,
        DateTime? RedFlagStartedUtc,
        long RaceFastestLapMs,
        string RaceFastestLapTransponder,

        // The rest of what Reset clears. Undoing a reset put the standings
        // back but not these, so a race reset during its last lap came back
        // with no checkered out and nobody having taken it - and a staggered
        // or hand-released one came back with no grid, no schedule and every
        // release to be called again. Optional, so the journal (which
        // writes only some of them) can still build one; a null collection
        // leaves the live one as it is.
        bool IsFinishing = false,
        bool FinishingOnTime = false,
        DateTime? FinishingSinceUtc = null,
        IReadOnlyList<string>? TookTheFlag = null,
        IReadOnlyList<string>? Grid = null,
        IReadOnlyDictionary<string, long>? ReleaseOffsetsMs = null,
        IReadOnlyList<string>? ReleaseCalled = null,
        IReadOnlyDictionary<string, int>? StartPositions = null,
        IReadOnlyList<string>? JumpStarters = null,
        IReadOnlyDictionary<string, long>? PendingJumpStartPenalties = null,
        IReadOnlyDictionary<string, long>? JumpStartCrossingMs = null,

        // Which alternate transponders are being filed under their owner's
        // main one this race, and who has been heard. Without them an undo
        // forgot every decision, and a driver on their second car was split
        // into a second row on the alternate's next crossing. Null - an old
        // journal - rebuilds what it can from the standings instead.
        IReadOnlyDictionary<string, bool>? AlternateMerged = null,
        IReadOnlyList<string>? MergedInto = null,
        IReadOnlyList<string>? HeardThisRace = null,

        // Whether "last lap" and "one minute to go" have already been called
        // this race. Without them an undo or a crash restore called both
        // again - the last lap named whichever car next reached the lap
        // before the limit, which was not the leader. Null - a journal from
        // before these were stored - works them out from the race itself;
        // see RestoreSnapshot.
        bool? LastLapAnnounced = null,
        bool? FinalMinuteAnnounced = null,

        // Where a finished race's clock stopped - see _elapsedAtFinish.
        // Without it an undo putting back an earlier finished race showed
        // the clock of whichever race finished last. Null - the journal -
        // leaves a finished race's clock worked out live, as it always was.
        TimeSpan? ElapsedAtFinish = null);

    public SessionSnapshot CreateSnapshot()
    {
        var copies = _standings.Values.Select(s =>
        {
            var copy = new RacerStanding
            {
                TransponderId = s.TransponderId,
                Laps = s.Laps,
                FastestLapMs = s.FastestLapMs,
                LastLapDeviceTimeMs = s.LastLapDeviceTimeMs,
                LastSeenDeviceTimeMs = s.LastSeenDeviceTimeMs,
                ElapsedMs = s.ElapsedMs,
                ConsecutiveValidLaps = s.ConsecutiveValidLaps,
                PenaltyMs = s.PenaltyMs,
                StreakLapTimeSumMs = s.StreakLapTimeSumMs,
                StreakLapCount = s.StreakLapCount,
                StreakBaselineLapMs = s.StreakBaselineLapMs,
                ReleaseOffsetMs = s.ReleaseOffsetMs,

                // Copied like everything else, and it was the one field
                // that was not. A restored standing came back with the
                // default -1, so the next crossing re-zeroed that driver's
                // qualifying clock at THAT pass - elapsed restarted from
                // zero and whoever crossed first after an undo sat on top
                // of every same-lap tie for the rest of the session.
                QualifyingZeroMs = s.QualifyingZeroMs,
                RejectedLaps = s.RejectedLaps,
                LapsLed = s.LapsLed,
                StartPosition = s.StartPosition,
            };
            copy.LapTimesMs.AddRange(s.LapTimesMs);
            copy.StreakAtLap.AddRange(s.StreakAtLap);
            copy.RecoveryLapIndexes.AddRange(s.RecoveryLapIndexes);
            copy.Warnings.AddRange(s.Warnings);
            return copy;
        }).ToList();

        // Copies, not the live collections - Reset clears those in place.
        return new SessionSnapshot(copies, State, Flag, RaceStartUtc, _raceStartDeviceTimeMs, _accumulatedRedTime, _redFlagStartedUtc,
            _raceFastestLapMs, _raceFastestLapTransponder,
            IsFinishing,
            _finishingOnTime,
            _finishingSinceUtc,
            _tookTheFlag.ToList(),
            _grid.ToList(),
            new Dictionary<string, long>(_releaseOffsetsMs, StringComparer.OrdinalIgnoreCase),
            _releaseCalled.ToList(),
            new Dictionary<string, int>(_startPositions, StringComparer.OrdinalIgnoreCase),
            _jumpStarters.ToList(),
            new Dictionary<string, long>(_pendingJumpStartPenalties, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, long>(_jumpStartCrossingMs, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, bool>(_alternateMerged, StringComparer.OrdinalIgnoreCase),
            _mergedInto.ToList(),
            _heardThisRace.ToList(),
            _lastLapAnnounced,
            _finalMinuteAnnounced,
            _elapsedAtFinish);
    }

    /// <summary>Replaces a live collection's contents with a snapshot's, or leaves it alone when the snapshot didn't carry it.</summary>
    private static void RestoreInto<T>(ICollection<T> live, IEnumerable<T>? saved)
    {
        if (saved is null)
        {
            return;
        }

        live.Clear();
        foreach (var item in saved)
        {
            live.Add(item);
        }
    }

    /// <summary>
    /// Chooses which of two rows for the same transponder - differing only
    /// in case, from a journal written before the standings ignored case -
    /// to keep.
    ///
    /// They cannot be merged lap by lap: the journal keeps each lap's time
    /// but not when it was driven, so there is no honest way to interleave
    /// two lap lists. One row is kept whole. A row with no laps was a
    /// driver seen but never counted, and the one with laps wins outright;
    /// when both have laps the one with more is almost certainly the car
    /// itself and the other a handful of reads under a mistyped id, so the
    /// longer race wins - and the log says so, because those few laps are
    /// dropped. On a tie of laps the one with more timed laps wins, then the
    /// one seen first.
    /// </summary>
    private RacerStanding KeepOneOfCaseVariants(RacerStanding first, RacerStanding second)
    {
        var keepSecond = second.Laps > first.Laps
                         || (second.Laps == first.Laps && second.LapTimesMs.Count > first.LapTimesMs.Count);

        var kept = keepSecond ? second : first;
        var dropped = keepSecond ? first : second;

        if (dropped.Laps > 0)
        {
            Log($"Restored race had two rows for one transponder ({kept.TransponderId} and {dropped.TransponderId}). "
                + $"Kept {kept.TransponderId} with {Plural.Of(kept.Laps, "lap")}; "
                + $"the {Plural.Of(dropped.Laps, "lap")} under {dropped.TransponderId} could not be merged into it and were dropped.");
        }
        else
        {
            Log($"Restored race had two rows for one transponder ({kept.TransponderId} and {dropped.TransponderId}) - "
                + $"kept {kept.TransponderId}; the other had no laps.");
        }

        return kept;
    }

    /// <summary>Puts a session back exactly as CreateSnapshot found it.</summary>
    public void RestoreSnapshot(SessionSnapshot snapshot)
    {
        _standings.Clear();
        ClearAlternateDecisions();
        foreach (var standing in snapshot.Standings)
        {
            // Two rows for one transponder, differing only in case. A
            // snapshot taken here never has them - the standings ignore
            // case - but a journal written by 0.45 or earlier can, from
            // before they did. Assigning the second straight over the first
            // silently threw one row away, which could be the one with the
            // laps. See KeepOneOfCaseVariants.
            if (_standings.TryGetValue(standing.TransponderId, out var existing))
            {
                var kept = KeepOneOfCaseVariants(existing, standing);

                // Removed and added again rather than overwritten: an
                // overwrite keeps the dictionary's original key, which
                // would then disagree in case with the row it holds.
                _standings.Remove(existing.TransponderId);
                _standings[kept.TransponderId] = kept;
                continue;
            }

            _standings[standing.TransponderId] = standing;
        }

        RestoreAlternateDecisions(snapshot);

        RaceStartUtc = snapshot.RaceStartUtc;
        _elapsedAtFinish = snapshot.ElapsedAtFinish;
        _raceStartDeviceTimeMs = snapshot.RaceStartDeviceTimeMs;
        _accumulatedRedTime = snapshot.AccumulatedRedTime;
        _redFlagStartedUtc = snapshot.RedFlagStartedUtc;
        _raceFastestLapMs = snapshot.RaceFastestLapMs;
        _raceFastestLapTransponder = snapshot.RaceFastestLapTransponder;

        IsFinishing = snapshot.IsFinishing;
        _finishingOnTime = snapshot.IsFinishing && snapshot.FinishingOnTime;

        // The straggler grace runs from here when the snapshot didn't say
        // when the flag came out - a journal restored after a crash - so a
        // race that was on its last lap is not called the moment it resumes.
        _finishingSinceUtc = snapshot.IsFinishing ? (DateTime?)(snapshot.FinishingSinceUtc ?? DateTime.UtcNow) : null;

        if (!snapshot.IsFinishing)
        {
            _tookTheFlag.Clear();
        }
        else
        {
            RestoreInto(_tookTheFlag, snapshot.TookTheFlag);
        }

        RestoreInto(_grid, snapshot.Grid);
        RestoreInto(_releaseOffsetsMs, snapshot.ReleaseOffsetsMs);
        RestoreInto(_releaseCalled, snapshot.ReleaseCalled);
        RestoreInto(_startPositions, snapshot.StartPositions);
        RestoreInto(_jumpStarters, snapshot.JumpStarters);
        RestoreInto(_pendingJumpStartPenalties, snapshot.PendingJumpStartPenalties);
        RestoreInto(_jumpStartCrossingMs, snapshot.JumpStartCrossingMs);

        // After the standings and clock above, because an older journal
        // that did not store these has them worked out from both.
        //
        // The last lap has been called once any car has reached the lap
        // before the limit - that crossing is the one that calls it. A
        // minute to go has been called once the race clock is inside the
        // final minute (or past the limit). Either way, what matters is not
        // calling it a second time; a race restored just short of either
        // point still gets its call when it gets there.
        _lastLapAnnounced = snapshot.LastLapAnnounced
            ?? (LapLimit is { } lapLimit && _standings.Values.Any(s => s.Laps >= lapLimit - 1));

        _finalMinuteAnnounced = snapshot.FinalMinuteAnnounced
            ?? (TimeLimit is { } timeLimit && RaceStartUtc.HasValue && timeLimit - ElapsedRaceTime <= TimeSpan.FromMinutes(1));

        SetState(snapshot.State);

        // A race put back is not a race starting, whatever flag it comes
        // back under.
        GreenStartedTheRace = false;
        SetFlag(snapshot.Flag);
        Log("Session restored (reset undone).");
    }

    /// <summary>
    /// Puts back which alternate transponders were being merged into their
    /// owner's row, after the decisions were cleared for a restore.
    ///
    /// Clearing them and leaving it there meant the next pass from an
    /// alternate was decided afresh - and as the owner's row already had
    /// laps, it read as "both cars out" and the driver's second car turned
    /// into a second row halfway through the race.
    /// </summary>
    private void RestoreAlternateDecisions(SessionSnapshot snapshot)
    {
        if (snapshot.AlternateMerged is not null)
        {
            RestoreInto(_alternateMerged, snapshot.AlternateMerged);
            RestoreInto(_mergedInto, snapshot.MergedInto);
            RestoreInto(_heardThisRace, snapshot.HeardThisRace);
            return;
        }

        // Nothing saved - a journal written before these were. Rebuilt from
        // the standings as far as they can say: a row kept under an
        // alternate's own id was being timed as a car of its own, and any
        // row with a timed lap was heard. What they cannot say is whether a
        // main transponder's laps included its alternate's, so a merge is
        // not guessed at.
        foreach (var standing in _standings.Values)
        {
            var hasCrossed = standing.LapTimesMs.Count > 0;
            var owner = AlternateTransponderOwner?.Invoke(standing.TransponderId);

            if (!string.IsNullOrWhiteSpace(owner))
            {
                _alternateMerged[standing.TransponderId] = false;
                _heardThisRace.Add(standing.TransponderId);
            }
            else if (hasCrossed)
            {
                _heardThisRace.Add(standing.TransponderId);
            }
        }
    }

    public void Reset()
    {
        _standings.Clear();
        ClearAlternateDecisions();
        SessionGeneration++;
        _raceFastestLapMs = 0;
        _raceFastestLapTransponder = string.Empty;
        _jumpStarters.Clear();
        _pendingJumpStartPenalties.Clear();
        _jumpStartCrossingMs.Clear();

        // Or the next race inherits the last one's grid - and, in a
        // hand-released start, drivers who were already let go.
        _releaseOffsetsMs.Clear();
        _releaseCalled.Clear();
        _grid.Clear();
        _startPositions.Clear();

        IsFinishing = false;
        _finishingOnTime = false;
        _finishingSinceUtc = null;
        _tookTheFlag.Clear();
        _raceStartDeviceTimeMs = null;
        RaceStartUtc = null;
        _accumulatedRedTime = TimeSpan.Zero;
        _redFlagStartedUtc = null;
        EndWarmUp("Warm-up cancelled.");
        SetState(RaceState.Idle);
        SetFlag(FlagState.None);
        Log("Session reset.");
    }

    /// <summary>
    /// Brings the race clock up to date: puts the checkered out, or ends
    /// the race, once the time limit has passed.
    ///
    /// Asked by the 500ms tick AND by every pass as it arrives, which is
    /// the point of it existing separately. On the poll alone, the race did
    /// not know the clock had run out for up to half a second - and a car
    /// crossing inside that window banked an ordinary lap, then had to
    /// complete a whole further lap to take a flag that should already have
    /// been out. On a Mini-Z track that is the better part of ten seconds
    /// and a place or two, decided by nothing but where the crossing
    /// happened to fall between two timer ticks.
    ///
    /// Safe to ask twice: BeginFinishing and Finish both refuse to run a
    /// second time, so the poll and the pass can both call it. Both callers
    /// are on the UI thread, so there is no race between them either.
    /// </summary>
    private void ApplyTimeLimit()
    {
        if (State != RaceState.Racing || Flag == FlagState.Red)
        {
            return;
        }

        if (TimeLimit is not { } limit || !RaceStartUtc.HasValue || ElapsedRaceTime < limit)
        {
            return;
        }

        // Practice sets TimeLimit too, purely so the clock panel can show
        // a progress ring toward it - it must never auto-finish a practice
        // session (checkered flag, RaceCompleted event, all real race-over
        // semantics). PracticeModeControl stops itself through its own
        // ordinary Stop path instead.
        if (IsPracticeMode)
        {
            return;
        }

        if (FinishOnNextCrossing)
        {
            // The checkered goes out on schedule - the leader's race really
            // is over - but who it applies to is decided per driver, in
            // FlagAppliesTo.
            BeginFinishing(timeLimitReached: true);
        }
        else if (ElapsedRaceTime >= limit + LastWindowExtension)
        {
            // Stopping dead, so the session cannot end until the last car
            // released has had its own full window. Cars whose window
            // closed earlier stop scoring where they are - see the guard in
            // ProcessLapEvent.
            Finish(timeLimitReached: true);
        }
    }

    /// <summary>Call periodically (e.g. every 500ms from a UI timer) so time-limit races end on their own.</summary>
    public void Tick()
    {
        if (State != RaceState.Racing || Flag == FlagState.Red)
        {
            return;
        }

        // One minute to go, for a timed race. Before the time-limit check
        // below so the warning always lands ahead of the finish it warns
        // about. Practice sets a TimeLimit only to drive its clock ring, so
        // it is excluded here the same way it is from the finish.
        if (!_finalMinuteAnnounced && !IsPracticeMode
            && TimeLimit.HasValue && RaceStartUtc.HasValue)
        {
            var remaining = TimeLimit.Value - ElapsedRaceTime;

            if (remaining > TimeSpan.Zero && remaining <= TimeSpan.FromMinutes(1))
            {
                _finalMinuteAnnounced = true;
                SafeRaise(FinalMinute, h => h(), nameof(FinalMinute));
            }
        }

        ApplyTimeLimit();

        // Stragglers can't hold the race open forever - see FinishGrace.
        // The grace covers the stagger as well, or the cars the extension
        // above was meant to protect would be cut off by the very timeout
        // that exists for cars which have stopped.
        var grace = FinishGrace + LastWindowExtension;

        if (IsFinishing && _finishingSinceUtc is { } since && DateTime.UtcNow - since > grace)
        {
            var waitingOn = _standings.Keys.Where(k => !_tookTheFlag.Contains(k)).ToList();
            Log($"Calling the race {grace.TotalSeconds:0}s after the flag - still waiting on {Plural.Of(waitingOn.Count, "car")}: {string.Join(", ", waitingOn)}");
            Finish(_finishingOnTime);
        }
    }


    /// <summary>
    /// Records a jump start, once per driver per race.
    ///
    /// Only the first pass counts: a car sitting on the line can trigger
    /// the decoder repeatedly, and stacking a penalty for each read would
    /// turn one mistake into a disqualification.
    /// </summary>
    private void RegisterJumpStart(string transponderId, long? crossingDeviceTimeMs = null)
    {
        if (string.IsNullOrWhiteSpace(transponderId))
        {
            return;
        }

        // Recorded even when detection is off, because it is not about
        // punishing anybody - it is the evidence that this car was already
        // past the line when the green dropped, which is what the green
        // needs in order to credit their start lap.
        if (crossingDeviceTimeMs is { } crossedAt)
        {
            _jumpStartCrossingMs[transponderId] = crossedAt;
        }

        if (!DetectJumpStarts)
        {
            return;
        }

        if (!_jumpStarters.Add(transponderId))
        {
            return; // already flagged this race
        }

        var penaltyMs = Math.Max(0, JumpStartPenaltyMs);

        if (penaltyMs > 0)
        {
            // Held against the driver so it can be applied the moment they
            // appear in the standings - at this point the race hasn't
            // started, so there's nothing to penalise yet.
            _pendingJumpStartPenalties[transponderId] = penaltyMs;
            Log($"JUMP START: {transponderId} crossed before the green. {penaltyMs / 1000.0:0.0}s penalty will be applied.");
        }
        else
        {
            Log($"JUMP START: {transponderId} crossed before the green. Flagged only - no automatic penalty.");
        }

        // Safely: this is raised from inside ProcessLapEvent during the
        // countdown, and a throwing listener used to come straight back out
        // through the decoder's lap handler.
        SafeRaise(JumpStartDetected, h => h(transponderId, penaltyMs), nameof(JumpStartDetected));
    }

    /// <summary>Penalties owed by jump starters, applied once the race is under way and they have a standing.</summary>
    private readonly Dictionary<string, long> _pendingJumpStartPenalties = new(StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------
    // Starts
    // ---------------------------------------------------------------

    /// <summary>How the field is released. Standard is everyone on the green.</summary>
    public RaceStartType StartType { get; set; } = RaceStartType.Standard;

    /// <summary>What a staggered driver's own clock runs from. Ignored for a standard start.</summary>
    public StaggerClock StaggerClockMode { get; set; } = StaggerClock.OwnRelease;

    /// <summary>
    /// Each driver's delay after the green, in milliseconds.
    ///
    /// Empty for a standard start, which is why every existing path behaves
    /// exactly as before: no entry means no offset, and no offset means the
    /// green is the zero point for everyone.
    /// </summary>
    private readonly Dictionary<string, long> _releaseOffsetsMs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when a driver is released, with their delay after the green.</summary>
    public event Action<string, long>? DriverReleased;

    /// <summary>
    /// This race's grid, in order.
    ///
    /// Needed for a hand-released start and nothing else: a driver who has
    /// not been released has not crossed the line, so they have no standing
    /// and would never appear on the leaderboard to be released from. The
    /// grid is what puts them on the board at the green, waiting.
    /// </summary>
    private readonly List<string> _grid = new();

    /// <summary>Where each driver started, resolved from the grid at the green.</summary>
    private readonly Dictionary<string, int> _startPositions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>This driver's grid slot, 1-based, or zero when the grid wasn't known.</summary>
    public int StartPositionOf(string transponderId) =>
        _startPositions.TryGetValue(transponderId, out var position) ? position : 0;

    public void SetGrid(IEnumerable<string> transponders)
    {
        _grid.Clear();

        // Once each, ignoring case, so one driver cannot hold two grid slots
        // (and two held rows in a hand-released start).
        _grid.AddRange(transponders
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether a hand-released start actually has a grid to hold back.
    ///
    /// Without one there is nobody to release and no way to release them, so
    /// holding every car would leave the race permanently stopped. Treating
    /// that as "no holding" turns a misconfiguration into an ordinary start
    /// rather than a dead session.
    /// </summary>
    private bool HandReleaseArmed => StartType == RaceStartType.HandReleased && _grid.Count > 0;

    /// <summary>
    /// Sets the whole release schedule at once, before the green.
    ///
    /// The session deliberately doesn't know how the order was arrived at -
    /// grid position, reverse grid, handicap credit are all decisions for the
    /// caller. All it needs is who waits how long.
    /// </summary>
    public void SetReleaseSchedule(IReadOnlyDictionary<string, long> offsetsMs)
    {
        _releaseOffsetsMs.Clear();

        foreach (var (transponder, offset) in offsetsMs)
        {
            if (!string.IsNullOrWhiteSpace(transponder))
            {
                _releaseOffsetsMs[transponder] = Math.Max(0, offset);
            }
        }

        Log(_releaseOffsetsMs.Count == 0
            ? "Release schedule cleared - standard start, everyone on the green."
            : $"Release schedule set for {Plural.Of(_releaseOffsetsMs.Count, "driver")}: "
              + string.Join(", ", _releaseOffsetsMs.OrderBy(kv => kv.Value).Select(kv => $"{kv.Key} +{kv.Value / 1000.0:0.0}s")));
    }

    /// <summary>
    /// Releases a driver right now, for a hand-released start.
    ///
    /// Their offset is whatever the clock already says, so the schedule ends
    /// up recording what actually happened rather than a plan. Before the
    /// green there is nothing to measure from, so this is a no-op then - the
    /// director drops the green first, then releases.
    /// </summary>
    public void ReleaseDriver(string transponderId)
    {
        if (string.IsNullOrWhiteSpace(transponderId))
        {
            return;
        }

        if (State != RaceState.Racing || !RaceStartUtc.HasValue)
        {
            Log($"Release ignored for {transponderId} - the race hasn't started yet.");
            return;
        }

        // On the race clock, the same clock IsReleased checks against. The
        // wall clock since the green runs on through a red; measured on
        // that, a car released by hand after a stoppage would have an
        // offset the race clock had not reached yet, and would read as
        // still held - its first crossing flagged as a jump start.
        var offset = Math.Max(0, (long)ElapsedRaceTime.TotalMilliseconds);
        _releaseOffsetsMs[transponderId] = offset;

        // Marked before the event goes out, so the poll above can never call
        // the same driver a second time.
        _releaseCalled.Add(transponderId);

        if (_standings.TryGetValue(transponderId, out var standing))
        {
            standing.ReleaseOffsetMs = offset;
        }

        Log($"Released: {transponderId} at +{offset / 1000.0:0.0}s");
        SafeRaise(DriverReleased, h => h(transponderId, offset), nameof(DriverReleased));
    }

    /// <summary>Everyone still waiting goes now.</summary>
    public void ReleaseAll()
    {
        if (State != RaceState.Racing)
        {
            return;
        }

        foreach (var transponder in _standings.Keys.ToList())
        {
            if (!IsReleased(transponder))
            {
                ReleaseDriver(transponder);
            }
        }
    }

    /// <summary>This driver's delay after the green, or zero when they have none.</summary>
    public long ReleaseOffsetMsFor(string transponderId) =>
        _releaseOffsetsMs.TryGetValue(transponderId, out var offset) ? offset : 0;

    /// <summary>
    /// Whether this driver is under way yet.
    ///
    /// A hand-released start is the one case where an unknown driver is NOT
    /// released: with no schedule entry there is nothing saying they were let
    /// go, and treating silence as "released" would make the whole mode do
    /// nothing. Every other start type releases anyone it has no entry for,
    /// so a car that was never on the grid still races rather than being
    /// silently ignored.
    /// </summary>
    public bool IsReleased(string transponderId)
    {
        if (State != RaceState.Racing || !RaceStartUtc.HasValue)
        {
            return false;
        }

        if (HandReleaseArmed && !_releaseOffsetsMs.ContainsKey(transponderId))
        {
            return false;
        }

        var offset = ReleaseOffsetMsFor(transponderId);

        // Against the race clock, not the wall clock since the green. The
        // race clock stops under red, so a car still waiting when the red
        // came out stays waiting - its release used to fall due, and be
        // called, while the whole field was stopped, and at the restart it
        // went with everybody else and its stagger was simply gone.
        return offset <= 0
               || ElapsedRaceTime.TotalMilliseconds >= offset;
    }

    /// <summary>Drivers whose release has already been called, so it is called once.</summary>
    private readonly HashSet<string> _releaseCalled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raises <see cref="DriverReleased"/> for any scheduled driver whose
    /// delay has just come up.
    ///
    /// Polled rather than timed per driver: the caller already ticks several
    /// times a second for the race clock, and one pass over a handful of
    /// offsets is far cheaper than a timer per car - and can't leave a timer
    /// running into the next race.
    ///
    /// The car on zero is skipped deliberately. They go on the green, and
    /// the green-flag call has already told them so; a release call on top
    /// of it would just talk over the start.
    /// </summary>
    public void PollReleases()
    {
        if (State != RaceState.Racing
            || StartType is RaceStartType.Standard or RaceStartType.HandReleased or RaceStartType.Qualifier
            || _releaseOffsetsMs.Count == 0)
        {
            return;
        }

        foreach (var (transponder, offset) in _releaseOffsetsMs.OrderBy(kv => kv.Value).ToList())
        {
            if (offset <= 0 || _releaseCalled.Contains(transponder) || !IsReleased(transponder))
            {
                continue;
            }

            _releaseCalled.Add(transponder);

            if (_standings.TryGetValue(transponder, out var standing))
            {
                standing.ReleaseOffsetMs = offset;
            }

            Log($"Release due: {transponder} at +{offset / 1000.0:0.0}s");

            // Safely: the release is already marked as called above, so a
            // listener that threw here would lose this call for good, and
            // skip every release still due behind it on this poll.
            SafeRaise(DriverReleased, h => h(transponder, offset), nameof(DriverReleased));
        }
    }

    /// <summary>
    /// Every driver this race might have to call away: the scheduled ones
    /// and, for a hand-released start, the whole grid.
    ///
    /// Available as soon as the schedule is built - before the green - which
    /// is what makes rendering their release calls in advance possible.
    /// </summary>
    public IReadOnlyList<string> ReleaseRoster() =>
        _releaseOffsetsMs.Keys
            .Concat(_grid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Drivers with a scheduled release still to come, soonest first.</summary>
    public IReadOnlyList<(string Transponder, long OffsetMs)> PendingReleases() =>
        _releaseOffsetsMs
            .Where(kv => !IsReleased(kv.Key))
            .OrderBy(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    /// <summary>
    /// Files a pass from a driver's alternate transponder under their main
    /// one, so the laps land in the driver's own row - one lap count, one
    /// best, one position - and everything downstream that is keyed by
    /// transponder (history, records, handicaps, the grid and release
    /// schedule, which all hold main transponders) sees the driver it
    /// expects.
    ///
    /// Decided once per race, on the alternate's first pass:
    ///   - the main transponder hasn't been heard this race: the driver is
    ///     on their second car, so its laps go under the main transponder
    ///     for the rest of the race;
    ///   - the main transponder HAS been heard: both cars are out (a friend
    ///     borrowing the spare, say). Merging would interleave two cars'
    ///     crossings into nonsense lap times, so the alternate is timed as
    ///     a car of its own - it still shows the owner's name, because name
    ///     lookups resolve alternates.
    /// The decision is kept for the whole race, so a driver's laps never
    /// jump between rows. If the main car turns up AFTER its alternate was
    /// merged, the two can no longer be told apart; the log says so once.
    ///
    /// Passes that are not an alternate - main transponders, rentals,
    /// unknown cars - come back untouched.
    /// </summary>
    private DirectLapEvent ResolveAlternateTransponder(DirectLapEvent lap)
    {
        var id = lap.TransponderId;
        var primary = AlternateTransponderOwner?.Invoke(id);

        if (string.IsNullOrWhiteSpace(primary))
        {
            _heardThisRace.Add(id);

            if (_mergedInto.Contains(id) && _bothCarsWarned.Add(id))
            {
                Log($"{id} is on track while its alternate transponder is already being timed as {id} - "
                    + "both cars' crossings are going into one row, so its lap times can't be trusted this race.");
            }

            return lap;
        }

        if (!_alternateMerged.TryGetValue(id, out var merged))
        {
            merged = !_heardThisRace.Contains(primary);
            _alternateMerged[id] = merged;

            if (merged)
            {
                _mergedInto.Add(primary);
            }
            else
            {
                Log($"{id} is {primary}'s alternate transponder, but {primary} is on track too - "
                    + $"timing {id} as a separate car this race.");
            }
        }

        _heardThisRace.Add(id);

        if (!merged)
        {
            return lap;
        }

        // A copy rather than a change: the event is init-only, and the same
        // instance has already been handed to TransponderWatch. Every field
        // is carried across - if DirectLapEvent grows one, add it here.
        return new DirectLapEvent
        {
            TransponderId = primary,
            DeviceTimeMs = lap.DeviceTimeMs,
            SignalHits = lap.SignalHits,
            SignalStrength = lap.SignalStrength,
            IsSynthetic = lap.IsSynthetic,
        };
    }

    public void ProcessLapEvent(DirectLapEvent lap)
    {
        // Updated unconditionally, even outside the Racing state, so
        // whatever's about to call GreenFlag has a fresh reference point -
        // the decoder is still sending events during Staging/Countdown, and
        // that time needs to be captured before it's needed as lap timing's
        // zero-point.
        //
        // Ghost passes don't touch it: they run on their own clock (time
        // since the green), not the decoder's.
        if (!lap.IsSynthetic)
        {
            LastKnownDeviceTimeMs = lap.DeviceTimeMs;
            _lastKnownDeviceWallUtc = DateTime.UtcNow;

            lap = ResolveAlternateTransponder(lap);
        }

        // Before the pass is judged, not after: if the clock ran out a
        // fraction of a second ago, the checkered is already out and THIS
        // crossing is the one that takes it - exactly as the lap limit
        // already works. Leaving it to the next poll gave the driver an
        // ordinary lap and a whole extra one to run.
        ApplyTimeLimit();

        // A pass during the countdown is a jump start - the car crossed the
        // line before the green. These passes were already arriving and
        // being discarded here; this just acts on them.
        //
        // Staging is NOT the countdown. It is the field being called: cars
        // are carried out, placed, rolled across the loop to their grid
        // slot, and every one of those crossings used to be booked as a
        // jump start - with the class's penalty - before anyone had been
        // told to go. Only once the countdown is running is a crossing a
        // driver anticipating the start. Staging passes are ignored, as
        // they were before jump starts were detected at all.
        if ((State is RaceState.Staging or RaceState.Countdown) && !lap.IsSynthetic)
        {
            if (State == RaceState.Countdown)
            {
                RegisterJumpStart(lap.TransponderId, lap.DeviceTimeMs);
            }

            return;
        }

        if (State != RaceState.Racing || Flag == FlagState.Red || _raceStartDeviceTimeMs is null)
        {
            return; // not actively racing - ignore passes
        }

        // A car that crosses before its own release has jumped, exactly as
        // one crossing before the green has. Ghosts run to their own clock
        // and are never held. Standard starts never reach the body of this:
        // with no schedule, IsReleased is true for everyone from the green.
        if (!lap.IsSynthetic && !IsReleased(lap.TransponderId))
        {
            RegisterJumpStart(lap.TransponderId);
            return;
        }

        // Their race is already over: they took the checkered, and the
        // only reason the session is still running is that others haven't.
        if (IsFinishing && _tookTheFlag.Contains(lap.TransponderId))
        {
            return;
        }

        // Their own window has closed, and the session is only still open
        // because a later-released car is still inside its own. Reachable
        // only with FinishOnNextCrossing off; with it on there is a
        // checkered out and their next crossing is their finish, which
        // counts.
        //
        // Gated on there being a window to extend at all, so a standard
        // race - where the session ends on the same tick every driver runs
        // out - reaches none of this. Practice sets a time limit purely to
        // drive its clock ring and must never be cut off by it.
        if (!FinishOnNextCrossing
            && !IsPracticeMode
            && LastWindowExtension > TimeSpan.Zero
            && IsOutOfTime(lap.TransponderId))
        {
            return;
        }

        if (!_standings.TryGetValue(lap.TransponderId, out var standing))
        {
            // The first real car of the race. Until now it was a race of
            // ghosts, where a ghost may hold the fastest lap (see below) -
            // but from here a ghost may not, and one that took it while the
            // real field was still on its way to the line would otherwise
            // keep it, and be the lap every real one was measured against,
            // until somebody beat it.
            if (!GhostService.IsGhost(lap.TransponderId)
                && GhostService.IsGhost(_raceFastestLapTransponder)
                && _standings.Keys.All(k => GhostService.IsGhost(k)))
            {
                _raceFastestLapMs = 0;
                _raceFastestLapTransponder = string.Empty;
            }

            standing = new RacerStanding
            {
                TransponderId = lap.TransponderId,
                ReleaseOffsetMs = ReleaseOffsetMsFor(lap.TransponderId),
                StartPosition = StartPositionOf(lap.TransponderId),
            };
            _standings[lap.TransponderId] = standing;
        }

        // Where this driver's own race began.
        //
        // OwnRelease shifts the zero point to their release, so everyone is
        // measured over the same amount of green-flag time and the result is
        // decided on pace. SharedGreen leaves it at the green, so the wait is
        // time lost and first across the line wins. A standard start has no
        // offset either way, so both behave as they always did.
        long releaseZeroMs;
        long elapsedSinceStart;

        // True when this crossing is the one that zeroed a qualifier's
        // clock - which makes it their start lap. See below.
        var qualifyingClockZeroedHere = false;

        // A qualifier's time since the green BEFORE it is floored at zero -
        // see the start-reference check below, which needs the real figure.
        long rawQualifierSinceGreenMs = 0;

        if (StartType == RaceStartType.Qualifier)
        {
            // Qualifying: this driver's clock zeroes at their own first
            // crossing, whenever that was. Ghosts report time since the
            // green directly, which serves as the same reference.
            //
            // Never below zero. -1 is how a standing says "not crossed yet",
            // and a car over the loop a few milliseconds before the
            // reference produced exactly that: its zero read as unset, the
            // NEXT crossing zeroed the clock again, and that driver was
            // timed from a lap later than everybody else.
            rawQualifierSinceGreenMs = lap.IsSynthetic
                ? lap.DeviceTimeMs
                : lap.DeviceTimeMs - _raceStartDeviceTimeMs.Value;
            var sinceGreen = Math.Max(0, rawQualifierSinceGreenMs);

            if (standing.QualifyingZeroMs < 0)
            {
                standing.QualifyingZeroMs = sinceGreen;
                qualifyingClockZeroedHere = true;
            }

            releaseZeroMs = standing.QualifyingZeroMs;
            elapsedSinceStart = sinceGreen - releaseZeroMs;
        }
        else
        {
            releaseZeroMs = StaggerClockMode == StaggerClock.OwnRelease
                ? standing.ReleaseOffsetMs
                : 0;

            // Ghosts report time since the green directly - see GhostService.
            elapsedSinceStart = lap.IsSynthetic
                ? lap.DeviceTimeMs
                : lap.DeviceTimeMs - _raceStartDeviceTimeMs.Value - releaseZeroMs;
        }

        // Sanity check on the first real pass of the race. If the start
        // reference disagrees with the wall clock by more than a few
        // seconds, it came from a different clock - no pass seen since the
        // app opened (reference 0), a stale pass, a decoder power cycle, or
        // the demo's simulated clock - and every lap time and gap this race
        // would be off by that amount (or negative, rejecting every lap).
        // Re-anchor from this pass instead. Skipped for the demo, whose
        // simulated clock intentionally doesn't track wall time.
        if (!lap.IsSynthetic && !_startReferenceChecked && !IsDemoMode && RaceStartUtc is { } startUtc)
        {
            _startReferenceChecked = true;
            var wallSinceStartMs = (long)(DateTime.UtcNow - startUtc).TotalMilliseconds;

            // Compared against time since THIS driver's release, since that
            // is what elapsedSinceStart now measures. Comparing against the
            // green would read a staggered driver's legitimate delay as a
            // broken clock and "correct" a reference that was fine.
            var wallSinceReleaseMs = wallSinceStartMs - releaseZeroMs;

            // A qualifier is judged on time since the GREEN, unfloored.
            //
            // Its elapsed time is measured from the driver's own zero, which
            // was itself worked out from the reference under test - so on
            // the first crossing both sides of the comparison moved together
            // and it could not see anything. Worse, the zero is floored at
            // nothing: a reference AHEAD of the decoder's clock (a decoder
            // that was power-cycled restarts lower) put every driver at
            // zero, elapsed stayed at zero for the whole session, and the
            // check passed on the floored value.
            var decoderSaysMs = StartType == RaceStartType.Qualifier ? rawQualifierSinceGreenMs : elapsedSinceStart;
            var wallSaysMs = StartType == RaceStartType.Qualifier ? wallSinceStartMs : wallSinceReleaseMs;

            if (Math.Abs(decoderSaysMs - wallSaysMs) > 5000)
            {
                Log($"Start reference corrected: decoder said {decoderSaysMs}ms since {(StartType == RaceStartType.Qualifier ? "the green" : "release")}, wall clock says {wallSaysMs}ms.");

                // The stored reference stays anchored to the GREEN - it is
                // shared by every driver, and only the per-driver offset
                // above moves it to a release.
                _raceStartDeviceTimeMs = lap.DeviceTimeMs - Math.Max(1, wallSinceStartMs);
                elapsedSinceStart = Math.Max(1, wallSinceReleaseMs);

                // A qualifier's zero was worked out from the reference just
                // thrown away, so it has to be worked out again from the
                // corrected one. Left alone, the zero sat wherever the stale
                // reference put it - minutes out - and the "release" the
                // line above measures from was that nonsense figure, so the
                // driver was timed from somewhere that never happened.
                if (StartType == RaceStartType.Qualifier)
                {
                    var correctedSinceGreen = Math.Max(0, lap.DeviceTimeMs - _raceStartDeviceTimeMs.Value);

                    if (qualifyingClockZeroedHere)
                    {
                        standing.QualifyingZeroMs = correctedSinceGreen;
                    }

                    releaseZeroMs = standing.QualifyingZeroMs;
                    elapsedSinceStart = Math.Max(0, correctedSinceGreen - releaseZeroMs);
                }
            }
        }

        long lapTimeMs;
        bool isValid;

        // True for the first crossing of a driver brought back from the race
        // journal after a crash - see below.
        var firstLapAfterRecovery = false;

        if (standing.LastSeenDeviceTimeMs == 0)
        {
            // Very first pass of the race for this racer - always counts.
            lapTimeMs = elapsedSinceStart;
            isValid = elapsedSinceStart > 0;

            // Unless they have timed laps already. That is a driver restored
            // from the race journal: it clears the per-driver decoder
            // readings (a decoder power-cycled while the app was down would
            // make them meaningless), so this crossing lands here - and used
            // to be booked as a lap as long as the WHOLE race so far. That
            // one lap then sat in the pace chart, and pushed every earlier
            // crossing the gap column works out from the lap list out of
            // line with the elapsed time.
            //
            // Timed instead from where their laps already add up to - the
            // race time of their last counted crossing - so the lap list
            // still sums to their elapsed time. It spans the stoppage, so it
            // is long, the same as any lap run through a red flag; it is
            // kept out of best laps and streaks below.
            //
            // Not for a qualifier re-zeroing here, which the case below
            // already deals with.
            if (standing.LapTimesMs.Count > 0 && !qualifyingClockZeroedHere)
            {
                firstLapAfterRecovery = true;
                lapTimeMs = Math.Max(0, elapsedSinceStart - standing.LapTimesMs.Sum());
            }

            // A qualifier's first crossing is their start lap, the same as
            // anybody's first crossing is: it ends the run from the grid
            // and starts the clock. Its "time" is zero because their clock
            // starts right here - the same zero a jump-start credit puts in
            // the start-lap slot.
            //
            // It used to fail the check above on exactly that zero, so every
            // driver in every qualifier began with a rejected pass - counted
            // in RejectedLaps, buzzed as an invalid lap, streak reset - and
            // their first REAL lap then became the start lap, which never
            // competes for a best. The quickest lap of a short qualifier
            // could be the one that was thrown away.
            //
            // Only when their lap list is empty: a standing restored from a
            // journal too old to hold its qualifying zero re-zeroes here
            // mid-race, and that crossing is not a start lap.
            if (qualifyingClockZeroedHere && standing.LapTimesMs.Count == 0)
            {
                lapTimeMs = 0;
                isValid = true;
            }
        }
        else
        {
            // Validity is judged against the immediately preceding pass of
            // ANY kind, valid or rejected - see the class-level comment on
            // MinLapTimeMs for why this matters.
            var gapFromLastSeen = lap.DeviceTimeMs - standing.LastSeenDeviceTimeMs;
            isValid = gapFromLastSeen >= MinLapTimeMs;

            // The lap TIME we report/announce is still measured from the
            // last actually-counted lap, since that's the meaningful
            // duration for a real lap - not from whatever rejected noise
            // happened to precede it.
            lapTimeMs = standing.LastLapDeviceTimeMs > 0
                ? lap.DeviceTimeMs - standing.LastLapDeviceTimeMs
                : elapsedSinceStart;
        }

        // A device clock that has gone BACKWARDS cannot produce a duration
        // worth having, and this is not hypothetical: ILapClient restarts
        // its stopwatch on every Connect, so a USB hiccup, a wrong port or
        // somebody pressing Connect mid-race re-zeroes device time. The
        // arithmetic above would then hand out a lap of about minus six
        // minutes, and a negative ElapsedMs sorts that driver to the top of
        // every same-lap tie for the rest of the race - while the lap chart
        // and the pace analysis carry a -388s lap.
        //
        // Rejected rather than guessed at. Once the timebase has moved
        // under us the real duration is gone, and a rejected lap is a
        // visible, explainable loss where a negative one is a silently
        // wrong result that looks like a win.
        //
        // Not only when the pass was otherwise valid. The first crossing on
        // the new timebase is usually "too soon" as well - its gap from the
        // last pass seen is negative too - and was rejected as such, without
        // anything below being put right.
        var clockWentBackwards = lapTimeMs < 0 && standing.LastSeenDeviceTimeMs != 0;

        if (clockWentBackwards)
        {
            Log($"Lap rejected: the decoder clock went backwards for {lap.TransponderId} "
                + $"({lapTimeMs}ms since that driver's last counted lap). "
                + "The decoder was most likely reconnected during the race.");

            isValid = false;

            // Re-anchored on this pass, so the NEXT lap is measured on the
            // new timebase. Without it every later crossing was measured
            // from a counted lap on the old clock, came out negative in its
            // turn, and was rejected too: one reconnect cost the driver the
            // rest of the race rather than the one lap it really broke.
            standing.LastLapDeviceTimeMs = lap.DeviceTimeMs;

            // The race's own zero point is on the old clock as well, and
            // every driver's elapsed time is measured from it - left there,
            // elapsed came out negative, was floored at zero, and the order
            // within each lap stopped meaning anything. Moved once, from the
            // wall clock, the same way the start-reference check above does
            // it; the next driver through finds it already right.
            if (!lap.IsSynthetic && !IsDemoMode && RaceStartUtc is { } resetStartUtc && elapsedSinceStart < 0)
            {
                var wallSinceGreenMs = (long)(DateTime.UtcNow - resetStartUtc).TotalMilliseconds;
                _raceStartDeviceTimeMs = lap.DeviceTimeMs - Math.Max(1, wallSinceGreenMs);
                Log($"Race start reference moved onto the decoder's new clock ({wallSinceGreenMs}ms since the green).");
            }
        }

        standing.LastSeenDeviceTimeMs = lap.DeviceTimeMs; // always updated, valid or not

        var isFastestLap = false;
        var isPersonalBest = false;

        // Ghosts run in real races as pace cars to chase. They keep their
        // own laps and bests, but nothing that decides or describes the
        // real race - its fastest lap, who led, the last-lap call, the
        // finish - may come from one. A track-record ghost reaching the lap
        // limit used to put the checkered out on the real field.
        //
        // Only when there IS a real field. A race of nothing but ghosts has
        // no real race to protect, and left to this rule it could never
        // reach its lap limit at all.
        var realCarsRacing = _standings.Keys.Any(k => !GhostService.IsGhost(k));
        var isGhost = realCarsRacing && GhostService.IsGhost(standing.TransponderId);

        if (isValid)
        {
            standing.Laps++;
            standing.LastLapDeviceTimeMs = lap.DeviceTimeMs;

            // A jump-start penalty is owed from before the race began, so
            // it's applied on the driver's first counted lap - the earliest
            // point at which there's an elapsed time to add it to.
            //
            // Into PenaltyMs, not straight into ElapsedMs: elapsed is
            // rebuilt from device time a few lines down, so anything added
            // to it directly would survive until the driver's next
            // crossing and no longer.
            if (_pendingJumpStartPenalties.Remove(standing.TransponderId, out var owed) && owed > 0)
            {
                standing.PenaltyMs += owed;
                Log($"Jump-start penalty applied: {standing.TransponderId} +{owed / 1000.0:0.0}s");
            }

            // Elapsed is device time since the start PLUS every penalty
            // this driver is carrying, and it is rebuilt from scratch on
            // every counted lap.
            //
            // It used to be the device time alone. A penalty went into
            // ElapsedMs through AdjustPenaltyTime, moved the driver down
            // the order exactly as intended - and was then wiped by this
            // line on their very next crossing, while the board went on
            // showing "+10s" in the penalty column. Ten seconds of
            // punishment lasted one lap, and nothing about the display
            // said so. The same line quietly undid every jump-start
            // penalty on the driver's second lap.
            //
            // PenaltyMs is the record; this is the one place it is folded
            // in. AdjustPenaltyTime keeps adjusting ElapsedMs directly so
            // the board moves the instant a penalty is given, and the two
            // agree because both are derived from the same PenaltyMs.
            standing.ElapsedMs = Math.Max(0, elapsedSinceStart + standing.PenaltyMs);
            // The first counted pass is NOT a lap. Its "time" runs from the
            // green to the car's first crossing - a second or two when the
            // grid is just behind the line, or a lap plus reaction time when
            // it isn't. It was allowed to set the best lap, so a 1.4s
            // grid-to-line dash became a driver's best (and the race's
            // fastest lap), no real lap could ever beat it, and the fastest
            // lap announcement stopped firing. The demo's staggered first
            // crossing did the same thing.
            //
            // Still counted as a lap and still in LapTimesMs - lap counts and
            // gaps depend on it - but it never competes for best lap.
            var isStartLap = standing.LapTimesMs.Count == 0;

            // The first lap after a crash recovery is not a timed lap either:
            // it runs through the stoppage. See where it is worked out above.
            var competesForBest = !isStartLap && !firstLapAfterRecovery;

            // Personal best for this driver - its own event, distinct from
            // the race's fastest lap below.
            if (competesForBest && lapTimeMs > 0 && (standing.FastestLapMs == 0 || lapTimeMs < standing.FastestLapMs))
            {
                var hadOwnBest = standing.FastestLapMs > 0;
                standing.FastestLapMs = lapTimeMs;
                isPersonalBest = hadOwnBest; // first flying lap isn't an improvement
            }

            // "Fastest lap" means fastest of the RACE, not the driver's own
            // best. Comparing against the driver's personal best meant every
            // racer set off the announcement as they each improved, which
            // made it near-constant early on and robbed it of any meaning.
            if (competesForBest && !isGhost && lapTimeMs > 0 && (_raceFastestLapMs == 0 || lapTimeMs < _raceFastestLapMs))
            {
                var hadPrevious = _raceFastestLapMs > 0;
                _raceFastestLapMs = lapTimeMs;
                _raceFastestLapTransponder = standing.TransponderId;

                // The first lap of the race is fastest by default, which
                // isn't news - only announce once there's something to beat.
                isFastestLap = hadPrevious;
            }

            // A lap can be perfectly VALID (cleared the minimum-lap-time
            // filter) while still being a poor, inconsistent lap compared
            // to how this racer has been running. If it's noticeably slower
            // than the current streak's own average, the streak breaks here
            // - this lap becomes lap 1 of a fresh potential streak rather
            // than silently extending the old one. The very first lap of
            // the race is excluded from seeding this average: its duration
            // is measured from the Green-flag reference point rather than a
            // real previous pass, so it's not a meaningful lap time to
            // compare future laps against.
            if (firstLapAfterRecovery)
            {
                // A run cannot carry on through a crash and a restart: this
                // lap starts a fresh one, and - like the start lap - it is
                // not the yardstick for the next, which is judged on its own.
                standing.ConsecutiveValidLaps = 0;
                standing.StreakLapTimeSumMs = 0;
                standing.StreakLapCount = 0;
                standing.StreakBaselineLapMs = 0;
            }
            else if (!isStartLap)
            {
                // Judged against the driver's previous flying lap, within a
                // window of plus-or-minus StreakWindowPercent of it.
                var baselineMs = standing.StreakBaselineLapMs;
                var window = Math.Max(0, StreakWindowPercent) / 100.0;
                var allowance = baselineMs * window;

                var tooSlow = baselineMs > 0 && lapTimeMs > baselineMs + allowance;
                var tooFast = StreakFasterLapBreaks && baselineMs > 0 && lapTimeMs < baselineMs - allowance;

                var breaksStreakOnPace = standing.ConsecutiveValidLaps > 0 && (tooSlow || tooFast);

                if (breaksStreakOnPace)
                {
                    Log($"Streak broken by {(tooFast ? "fast" : "slow")} lap: {lap.TransponderId}  "
                        + $"lap_time={lapTimeMs}ms vs previous lap {baselineMs}ms "
                        + $"(±{StreakWindowPercent:0.#}% = {baselineMs - allowance:0}-{baselineMs + allowance:0}ms)");
                    standing.ConsecutiveValidLaps = 0;
                    standing.StreakLapTimeSumMs = 0;
                    standing.StreakLapCount = 0;
                }
                else
                {
                    // Only laps that BELONG to the streak feed its average.
                    // The breaking lap used to be added here too, seeding
                    // the fresh streak with the very time judged too slow -
                    // so the next lap was compared against a badly inflated
                    // baseline, and the check drifted slower after every
                    // incident. A driver going 7.0, 7.0, 7.0, 10.0 ended up
                    // with a 10.0 baseline, letting an 8.5 pass unnoticed.
                    standing.StreakLapTimeSumMs += lapTimeMs;
                    standing.StreakLapCount++;
                }

                // The yardstick for the NEXT lap, set whether or not this one
                // broke the run - a broken streak still starts again from the
                // lap that broke it, which is the last one actually driven.
                standing.StreakBaselineLapMs = lapTimeMs;
            }

            standing.ConsecutiveValidLaps++;
            standing.LapTimesMs.Add(lapTimeMs);
            standing.StreakAtLap.Add(standing.ConsecutiveValidLaps);

            // Remembered, so it stays out of bests and streaks when the
            // laps are worked out again after a lap is removed by hand -
            // see RecomputeAfterLapRemoval.
            if (firstLapAfterRecovery)
            {
                standing.RecoveryLapIndexes.Add(standing.LapTimesMs.Count - 1);
            }

            // Were they ahead at the moment they finished this lap?
            //
            // Asked here rather than reconstructed afterwards, because the
            // running order BETWEEN crossings is never stored - this is the
            // only moment the question has an honest answer. Ordering is the
            // standings' own, so it agrees with the board. A ghost is never
            // credited, and one out in front does not rob the real leader.
            if (!isGhost && ReferenceEquals(RaceLeader(), standing))
            {
                standing.LapsLed++;
            }

            Log($"Lap counted: {lap.TransponderId}  lap={standing.Laps}  lap_time={lapTimeMs}ms" +
                (standing.ConsecutiveValidLaps >= StreakThreshold ? $"  streak={standing.ConsecutiveValidLaps}" : "") +
                (isFastestLap ? "  (new fastest lap)" : ""));
        }
        else
        {
            standing.ConsecutiveValidLaps = 0;
            standing.StreakLapTimeSumMs = 0;
            standing.StreakLapCount = 0;
            standing.RejectedLaps++;
            Log($"Lap rejected (too soon, {lapTimeMs}ms < {MinLapTimeMs}ms minimum): {lap.TransponderId}");
        }

        // Safely, like everything else on the way to a finish: the flag is
        // taken further down this method, and a throwing lap listener used
        // to stop the race ending on the very crossing that ended it.
        SafeRaise(LapProcessed, h => h(standing, isValid, isFastestLap), nameof(LapProcessed));

        if (isPersonalBest)
        {
            SafeRaise(PersonalBestLap, h => h(standing), nameof(PersonalBestLap));
        }

        // The leader crossing to start the final lap. Announced once, for
        // the first car to reach one short of the limit - by definition the
        // leader on laps. Practice has no limit-driven finish, so it never
        // has a last lap to call.
        if (isValid && !isGhost && !IsPracticeMode && !_lastLapAnnounced
            && LapLimit.HasValue && standing.Laps == LapLimit.Value - 1)
        {
            _lastLapAnnounced = true;
            SafeRaise(LastLapStarted, h => h(standing), nameof(LastLapStarted));
        }

        // Crossing the line while the checkered is out IS taking it.
        // Deliberately after LapProcessed, so the lap that brought them home
        // is reported before the race is declared over.
        if (isValid && IsFinishing && FlagAppliesTo(lap.TransponderId))
        {
            MarkTookTheFlag(lap.TransponderId);
        }

        // A ghost can take the flag once it is out (above), but can never
        // be the car that puts it out.
        //
        // Only while the race is still running: when this crossing was the
        // last car taking the flag, the race finished just above, and it
        // then logged "lap limit reached" underneath "race complete".
        if (isValid && !isGhost && State == RaceState.Racing && LapLimit.HasValue && standing.Laps >= LapLimit.Value)
        {
            // Same reasoning as the time-limit guard in Tick() above.
            if (IsPracticeMode)
            {
                return;
            }

            Log($"Lap limit of {LapLimit.Value} reached by {lap.TransponderId}.");

            if (FinishOnNextCrossing)
            {
                // The car that reached the limit has taken the flag on this
                // very crossing; everyone else takes it on their next one.
                BeginFinishing(timeLimitReached: false);
                MarkTookTheFlag(lap.TransponderId);
            }
            else
            {
                Finish(timeLimitReached: false);
            }
        }
    }

    private void SetState(RaceState state)
    {
        // Anything that moves the session on - a stage from the phone, a
        // countdown or green pressed early, practice, a demo - ends the
        // warm-up there and then, whoever started it.
        if (state is not (RaceState.Idle or RaceState.Finished))
        {
            EndWarmUp($"Warm-up ended - {state.ToString().ToLowerInvariant()}.");
        }

        State = state;
        SafeRaise(StateChanged, h => h(state), nameof(StateChanged));
    }

    private void SetFlag(FlagState flag)
    {
        Flag = flag;
        SafeRaise(FlagChanged, h => h(flag), nameof(FlagChanged));
    }

    /// <summary>
    /// Raises an event one subscriber at a time, so one that throws cannot
    /// stop the rest - or stop the session.
    ///
    /// A plain Invoke runs the whole list and gives up at the first
    /// exception, and the exception then comes straight back out through
    /// whatever raised it. On the finish path that is Finish itself:
    /// StateChanged goes out before RaceCompleted, so a fault in any screen
    /// listening for the state change meant RaceCompleted was never raised
    /// and the result was never saved. The race was over, the board said
    /// so, and the history never heard about it.
    ///
    /// The failure is logged rather than swallowed, so it can still be
    /// found and fixed.
    /// </summary>
    private void SafeRaise<T>(T? handlers, Action<T> invoke, string eventName) where T : Delegate
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                invoke((T)handler);
            }
            catch (Exception ex)
            {
                Log($"A {eventName} handler failed and was skipped ({ex.GetType().Name}: {ex.Message}). The race carries on.");
            }
        }
    }

    /// <summary>
    /// Writes to the race log. Each listener is called on its own and a
    /// failing one is ignored - the log is where SafeRaise reports faults,
    /// so it must be the one thing here that cannot throw.
    /// </summary>
    private void Log(string message)
    {
        if (LogMessage is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<string>)handler)(message);
            }
            catch
            {
                // Nowhere left to report it.
            }
        }
    }
}
