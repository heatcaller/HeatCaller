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
    /// True when this driver's start lap was credited at the green because
    /// they were past the line (a jump start - see GreenFlag). Their next
    /// crossing then runs from wherever they crossed before the green, which
    /// may be a car being set on its grid slot across the loop: a dash of a
    /// few seconds, not a lap. So that crossing is treated as their start
    /// lap too and never competes for best lap.
    ///
    /// Without it, the credited zero made the dash look like a flying lap:
    /// it became the race's fastest lap - no real lap could beat it, so the
    /// fastest-lap call never fired again - and was saved as a personal
    /// best and a track record.
    /// </summary>
    public bool StartLapCredited { get; set; }

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
    /// Whether the clean run was broken AFTER the last counted lap - by a
    /// rejected pass, which zeroes ConsecutiveValidLaps but adds nothing to
    /// StreakAtLap. Set by a rejected pass, cleared by the next counted lap.
    ///
    /// WHY (20 Oct 2026): working the run out again after a lap was removed
    /// by hand read it off StreakAtLap's last entry, which still held the
    /// run from before the rejection, so -1 Lap (or undoing a correction)
    /// brought a broken streak back to life on the board and in the calls.
    /// </summary>
    public bool RunBrokenSinceLastLap { get; set; }

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

    /// <summary>
    /// Each counted lap's sector times - split loop to split loop, in order,
    /// the last one ending at the line - in step with LapTimesMs; null for a
    /// lap with none (no split loop, a split missed, the start lap, a lap
    /// through a crash recovery, a ghost's).
    ///
    /// Read through <see cref="SectorsOf"/>, never directly: the last sector
    /// is the lap less the others, so a lap's sectors always add up to its
    /// time, and a lap merged or edited by hand since no longer matches them
    /// and simply shows none. That makes a list that slips out of step with
    /// the laps show blanks rather than another lap's sectors.
    /// </summary>
    public List<long[]?> SectorsAtLap { get; } = new();

    /// <summary>
    /// This lap's sector times, or null - only while they still add up to
    /// the lap's time exactly (see <see cref="SectorsAtLap"/>).
    /// </summary>
    public long[]? SectorsOf(int lapIndex) =>
        lapIndex >= 0 && lapIndex < SectorsAtLap.Count && lapIndex < LapTimesMs.Count
            ? Sectors.Checked(SectorsAtLap[lapIndex], LapTimesMs[lapIndex])
            : null;

    /// <summary>
    /// Each time a split loop has read the car going past since its last
    /// line crossing, on the line decoder's clock - one list per split loop,
    /// Split 1 first, sized to the session's SplitCount as it is used. One
    /// read is the usual; two in one lap is a lap the line may have missed
    /// (worked out when the line next counts one - see
    /// RaceControlSession.PossibleMissedLap). A read later than the line's
    /// next crossing, which can arrive before that crossing does, is carried
    /// on to the lap it belongs to.
    /// </summary>
    public List<long>[] SplitReadsMs { get; set; } = Array.Empty<List<long>>();

    /// <summary>
    /// The latest raw read at each split loop, repeats included - what a
    /// repeat is measured from, so a car parked on a split loop, read every
    /// couple of seconds, stays one crossing rather than becoming several.
    /// </summary>
    public long[] SplitLastRawMs { get; set; } = Array.Empty<long>();

    /// <summary>Forgets every split read for the lap in progress - a jump-start credit, a clock that went backwards.</summary>
    public void ClearSplitReads()
    {
        SplitReadsMs = Array.Empty<List<long>>();
        SplitLastRawMs = Array.Empty<long>();
        OpenLastLap = null;
    }

    /// <summary>
    /// The lap just finished, when it finished without all its splits - its
    /// start and end on the line's clock and what had been read - so a split
    /// read arriving a moment after the line (a split loop just before the
    /// line, the I-Lap's serial delay) can still complete it. Null otherwise.
    /// </summary>
    public (long StartMs, long EndMs, long?[] Splits)? OpenLastLap { get; set; }

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

    /// <summary>
    /// A deep copy: every figure, and a copy of every list and array (each
    /// warning copied too), so nothing done to this standing afterwards
    /// reaches the copy and nothing done to the copy reaches this one.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P4): with timing on its own thread the
    /// live standing belongs to that thread and changes under anyone else
    /// reading it, so the screens are handed one of these instead (see
    /// RaceTiming). A field added to this class must be copied here too -
    /// TimingLayer_P4_FacadeTests checks every property by reflection.
    /// Not used in inline mode, where the live objects are handed out as
    /// they always were.
    /// </remarks>
    public RacerStanding Clone()
    {
        var copy = new RacerStanding
        {
            TransponderId = TransponderId,
            QualifyingZeroMs = QualifyingZeroMs,
            Laps = Laps,
            FastestLapMs = FastestLapMs,
            LastLapDeviceTimeMs = LastLapDeviceTimeMs,
            StartLapCredited = StartLapCredited,
            LastSeenDeviceTimeMs = LastSeenDeviceTimeMs,
            ElapsedMs = ElapsedMs,
            ConsecutiveValidLaps = ConsecutiveValidLaps,
            RunBrokenSinceLastLap = RunBrokenSinceLastLap,
            SplitReadsMs = SplitReadsMs.Length == 0
                ? Array.Empty<List<long>>()
                : SplitReadsMs.Select(reads => reads.ToList()).ToArray(),
            SplitLastRawMs = SplitLastRawMs.Length == 0 ? Array.Empty<long>() : SplitLastRawMs.ToArray(),
            OpenLastLap = OpenLastLap is { } open ? (open.StartMs, open.EndMs, open.Splits.ToArray()) : null,
            PenaltyMs = PenaltyMs,
            StreakLapTimeSumMs = StreakLapTimeSumMs,
            StreakLapCount = StreakLapCount,
            StreakBaselineLapMs = StreakBaselineLapMs,
            ReleaseOffsetMs = ReleaseOffsetMs,
            RejectedLaps = RejectedLaps,
            LapsLed = LapsLed,
            StartPosition = StartPosition,
        };

        copy.LapTimesMs.AddRange(LapTimesMs);
        copy.StreakAtLap.AddRange(StreakAtLap);
        copy.RecoveryLapIndexes.AddRange(RecoveryLapIndexes);
        copy.SectorsAtLap.AddRange(SectorsAtLap.Select(sectors => sectors?.ToArray()));
        copy.Warnings.AddRange(Warnings.Select(warning => warning.Clone()));
        return copy;
    }
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
public sealed partial class RaceControlSession
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
    /// How many split loops are round the track (0-3): how many sectors a lap
    /// is cut into, less one. 0 - no split loop - means no lap has sectors.
    /// Set by MainForm from the Connection page.
    /// </summary>
    public int SplitCount
    {
        get => _splitCount;
        set => _splitCount = Math.Clamp(value, 0, 3);
    }

    private int _splitCount;

    /// <summary>
    /// The length of each sector in feet, for sector speeds - from the split
    /// loops placed on the track layout's plan in the Track Designer. Null
    /// when the layout has none. Set by MainForm, which has the layouts.
    /// </summary>
    public Func<double[]?>? SectorLengthsFeetProvider { get; set; }

    /// <summary>The sectors' lengths in feet now, or null - see SectorLengthsFeetProvider.</summary>
    public double[]? SectorLengthsFeet
    {
        get
        {
            try
            {
                return SectorLengthsFeetProvider?.Invoke();
            }
            catch
            {
                // A plan that can't be measured only costs the speeds.
                return null;
            }
        }
    }

    /// <summary>
    /// The least time between the line and a split loop, or two split loops,
    /// for a read to be a car going round rather than one read twice.
    /// </summary>
    public const long MinSectorMs = 300;

    /// <summary>A lap's sectors were worked out: the standing, the lap's index in LapTimesMs, and the sectors.</summary>
    public event Action<RacerStanding, int, long[]>? SectorsRecorded;

    /// <summary>A car crossed a split loop: the standing, which split, and how long since it crossed the line.</summary>
    public event Action<RacerStanding, TimingPoint, long>? SplitCrossed;

    /// <summary>
    /// A car was read at a split loop twice without the line reading it in
    /// between - a lap the line may have missed: the standing, and the two
    /// reads on the line's clock. Nothing is added; the race director decides.
    /// </summary>
    public event Action<RacerStanding, long, long>? PossibleMissedLap;

    /// <summary>
    /// A split loop read this row's car at <paramref name="lineMs"/> (on the
    /// line decoder's clock). True when it was used for the lap in progress
    /// (or, arriving late, the lap just finished).
    ///
    /// This can never change a lap: it only notes where the car was, and
    /// the lap's sectors are worked out when the line counts it.
    /// </summary>
    public bool ProcessSplitPass(string rowTransponder, TimingPoint point, long lineMs)
    {
        var slot = (int)point - 1;

        // Not under a red: cars rolling round to the line while the race is
        // stopped are not racing laps, and the line ignores them too.
        if (SplitCount == 0 || slot < 0 || slot >= SplitCount
            || State != RaceState.Racing
            || Flag == FlagState.Red
            || !_standings.TryGetValue(rowTransponder, out var standing)
            || Ghosts.IsGhost(standing.TransponderId)
            || HasTakenFlag(standing.TransponderId))
        {
            return false;
        }

        // Not until the car has crossed the line once: before that it is
        // on its way from the grid, and there is no lap to cut.
        if (standing.LapTimesMs.Count == 0 || standing.LastLapDeviceTimeMs <= 0)
        {
            return false;
        }

        // Sized to the split loops in use, each on its own: reads carried
        // over from the last lap (CloseSplits) are kept.
        if (standing.SplitReadsMs.Length != SplitCount)
        {
            standing.SplitReadsMs = Enumerable.Range(0, SplitCount).Select(_ => new List<long>()).ToArray();
        }

        if (standing.SplitLastRawMs.Length != SplitCount)
        {
            standing.SplitLastRawMs = new long[SplitCount];
        }

        // A repeat: the same crossing read again, or a car sitting on the
        // loop - measured from the latest read, so a parked car stays one
        // crossing however long it sits there.
        var lastRaw = standing.SplitLastRawMs[slot];
        standing.SplitLastRawMs[slot] = Math.Max(lastRaw, lineMs);

        if (lastRaw > 0 && lineMs >= lastRaw - Math.Max(MinLapTimeMs, 1000) && lineMs - lastRaw < Math.Max(MinLapTimeMs, 1000))
        {
            return false;
        }

        var since = lineMs - standing.LastLapDeviceTimeMs;

        // Read before the line's last crossing: it belongs to the lap just
        // finished, when that one is still waiting for it.
        if (since < 0)
        {
            return CompleteOpenLap(standing, slot, lineMs);
        }

        if (since < MinSectorMs)
        {
            return false;
        }

        var reads = standing.SplitReadsMs[slot];
        reads.Add(lineMs);

        // The live gap is for the first crossing of the lap only.
        if (reads.Count == 1)
        {
            SafeRaise(SplitCrossed, h => h(standing, point, since), nameof(SplitCrossed));
        }

        return true;
    }

    /// <summary>
    /// The sectors of a lap that ran from <paramref name="startMs"/> to
    /// <paramref name="endMs"/> through these split reads, or null when one
    /// is missing or out of place. The last sector is the lap less the
    /// others, so they always add up to it.
    /// </summary>
    internal static long[]? SectorsFrom(long startMs, long endMs, IReadOnlyList<long?> splits)
    {
        if (splits.Count == 0 || endMs <= startMs)
        {
            return null;
        }

        var sectors = new long[splits.Count + 1];
        var from = startMs;

        for (var i = 0; i < splits.Count; i++)
        {
            if (splits[i] is not { } at || at - from < MinSectorMs || endMs - at < MinSectorMs)
            {
                return null;
            }

            sectors[i] = at - from;
            from = at;
        }

        sectors[^1] = endMs - startMs - sectors.Take(splits.Count).Sum();
        return sectors;
    }

    /// <summary>
    /// Closes the lap in progress as the line counts it: its sectors, or null,
    /// and the split reads cleared for the next lap - all but any read later
    /// than this crossing, which belongs to the next lap and only arrived
    /// first. Two reads at one split loop inside the lap mean the car went
    /// past it twice without the line seeing it: a possible missed lap.
    /// A lap left waiting on a split that may still be on its way is
    /// remembered (OpenLastLap).
    /// </summary>
    private long[]? CloseSplits(RacerStanding standing, bool allowed, long startMs, long endMs)
    {
        var reads = standing.SplitReadsMs;
        standing.OpenLastLap = null;

        // Nothing read yet this race, or the split loops changed: no reads.
        if (reads.Length != SplitCount)
        {
            reads = Enumerable.Range(0, SplitCount).Select(_ => new List<long>()).ToArray();
        }

        // Carried on: the reads that came after this crossing.
        standing.SplitReadsMs = reads.Select(list => list.Where(at => at > endMs).ToList()).ToArray();

        var inLap = reads.Select(list => list.Where(at => at > startMs && at <= endMs).ToList()).ToArray();

        var twice = inLap.FirstOrDefault(list => list.Count >= 2);

        if (twice is not null && startMs > 0)
        {
            var (first, second) = (twice[0], twice[1]);

            Log($"Possible missed lap: {standing.TransponderId} went past a split loop twice "
                + $"({(second - first) / 1000.0:0.000}s apart) without the line reading them in between.");
            SafeRaise(PossibleMissedLap, h => h(standing, first, second), nameof(PossibleMissedLap));
            return null;
        }

        if (!allowed || SplitCount == 0)
        {
            return null;
        }

        var splits = inLap.Select(list => list.Count == 1 ? list[0] : (long?)null).ToArray();
        var sectors = SectorsFrom(startMs, endMs, splits);

        if (sectors is null && splits.Any(at => at is null))
        {
            standing.OpenLastLap = (startMs, endMs, splits);
        }

        return sectors;
    }

    /// <summary>A split read for the lap just finished, arriving after the line counted it.</summary>
    private bool CompleteOpenLap(RacerStanding standing, int slot, long lineMs)
    {
        if (standing.OpenLastLap is not { } open
            || lineMs <= open.StartMs || lineMs >= open.EndMs
            || open.Splits[slot] is not null
            || standing.SectorsAtLap.Count != standing.LapTimesMs.Count
            || standing.SectorsAtLap[^1] is not null)
        {
            return false;
        }

        open.Splits[slot] = lineMs;

        if (SectorsFrom(open.StartMs, open.EndMs, open.Splits) is not { } sectors
            || Sectors.Checked(sectors, standing.LapTimesMs[^1]) is null)
        {
            return open.Splits.Any(s => s is null);
        }

        var lapIndex = standing.LapTimesMs.Count - 1;
        standing.SectorsAtLap[lapIndex] = sectors;
        standing.OpenLastLap = null;
        SafeRaise(SectorsRecorded, h => h(standing, lapIndex, sectors), nameof(SectorsRecorded));
        return true;
    }

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

    /// <summary>_finishingSinceUtc on the monotonic clock (TimingClock.NowMs) - what the straggler wait is measured with. See _raceStartClockMs.</summary>
    private long? _finishingSinceClockMs;
    private bool _finishingOnTime;

    /// <summary>
    /// The moment the checkered came out, on the session's clock - null when
    /// it isn't out, or when it came back with a restored race and nobody
    /// knows when it came out.
    ///
    /// WHY (23 Oct 2026, timing layer, P1): a pass is judged by when it
    /// arrived, so the checkered applies to it only if it was already out by
    /// then. A pass that crossed at 4:59.6 but was handed over after the
    /// clock had put the flag out (a busy window, a backlog) used to take the
    /// flag on a crossing made before the flag existed - that driver finished
    /// a lap early. For the time limit this is the exact instant the limit
    /// was reached, not the poll that noticed it, so the answer does not
    /// depend on where the 500ms tick happened to fall. Null means no cutoff:
    /// every pass while finishing is under the flag, as it always was.
    /// </summary>
    private long? _checkeredOutAtClockMs;

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
    /// <param name="timeLimitReached">True when the clock ran out, false for the lap limit.</param>
    /// <param name="checkeredOutAtMs">
    /// The moment the checkered really came out, on the session's clock - the
    /// instant the time limit was reached, when that is known exactly. Left
    /// out, it is now (for a pass being judged, the moment that pass arrived).
    /// See _checkeredOutAtClockMs.
    /// </param>
    private void BeginFinishing(bool timeLimitReached, long? checkeredOutAtMs = null)
    {
        if (IsFinishing || State != RaceState.Racing)
        {
            return;
        }

        IsFinishing = true;
        _finishingOnTime = timeLimitReached;
        _finishingSinceUtc = UtcNow;
        _finishingSinceClockMs = NowMs;
        _checkeredOutAtClockMs = checkeredOutAtMs ?? NowMs;
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
        if (EveryExpectedCarHasTakenTheFlag())
        {
            Log("Every car has taken the flag.");
            Finish(_finishingOnTime);
        }
    }

    /// <summary>
    /// Whether every car the finish is still waiting on has taken the flag.
    ///
    /// 17 Oct 2026, Bug Hunt 8, A01-1: a black-flagged car has been told to
    /// come in, and a hand-released car that was never let go is still on
    /// the grid - neither is going to cross again, so waiting on them held
    /// the finish open for the whole straggler grace. A black-flagged car
    /// that does cross is still recorded in _tookTheFlag as before. With
    /// every row left out, All() over nothing is true and the race ends.
    /// </summary>
    private bool EveryExpectedCarHasTakenTheFlag() =>
        _standings.Values
            .Where(s => !s.IsBlackFlagged && IsReleased(s.TransponderId))
            .All(s => _tookTheFlag.Contains(s.TransponderId));

    /// <summary>A row taken off the board mid-race - a ghost switched off (RemoveGhostStanding).</summary>
    public event Action<RacerStanding>? StandingRemoved;

    /// <summary>
    /// Takes a ghost switched off mid-race off the board. Ghosts only - a
    /// real car's row is never removed this way - and only while racing.
    ///
    /// Switching a ghost off used to stop it lapping and leave its row
    /// behind, frozen: the finish waited on it to take the flag until the
    /// 60s grace ran out, the track map called it a stopped car, and every
    /// driver ahead of the frozen row was credited with beating it.
    /// </summary>
    public bool RemoveGhostStanding(string transponderId)
    {
        // WHY (23 Oct 2026, timing layer, P0): recorded for replay - see Recorder.
        RecordCommand(new TimingRecord { Command = "RemoveGhost", Transponder = transponderId });

        if (State != RaceState.Racing || !Ghosts.IsGhost(transponderId)
            || !_standings.Remove(transponderId, out var standing))
        {
            return false;
        }

        _tookTheFlag.Remove(transponderId);
        Log($"Ghost {transponderId} switched off - taken off the board.");
        SafeRaise(StandingRemoved, h => h(standing), nameof(StandingRemoved));

        // Switched off after the checkered, it may have been the last car
        // the finish was waiting on.
        if (IsFinishing && EveryExpectedCarHasTakenTheFlag())
        {
            Log("Every car has taken the flag.");
            Finish(_finishingOnTime);
        }

        return true;
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
    ///
    /// 17 Oct 2026, Bug Hunt 8, A01-3: only cars with a row on the board
    /// count. The schedule is built from the whole grid, which can be a
    /// borrowed one, and a no-show's late slot kept the board frozen past
    /// the limit for a car that never raced.
    /// </summary>
    internal TimeSpan LastWindowExtension =>
        StaggerClockMode == StaggerClock.OwnRelease && _releaseOffsetsMs.Count > 0
            ? TimeSpan.FromMilliseconds(Math.Max(0, _releaseOffsetsMs
                .Where(kv => _standings.ContainsKey(kv.Key))
                .Select(kv => kv.Value)
                .DefaultIfEmpty(0)
                .Max()))
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
    /// <remarks>Internal so TimingWatch's host can leave a car whose window is over out of the cars it expects to cross.</remarks>
    internal bool IsOutOfTime(string transponderId) =>
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

    /// <summary>
    /// When the race now on (or last run) was first staged or counted down
    /// from rest, in UTC - null when it went straight to green, or nothing
    /// has been staged yet. Kept through the finish so the saved result can
    /// carry it (SessionRecord.StagedAtUtc), and replaced by the next stage.
    /// </summary>
    public DateTime? StagedAtUtc { get; private set; }

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
    /// How soon after the green a car credited with the start lap can cross
    /// and be taken for one that was behind the line all along - see
    /// ProcessLapEvent. A car sitting just behind the line is over it within
    /// a second or so of the green.
    ///
    /// Fixed, not the minimum lap time: that is set a little under the
    /// quickest real lap, and a car that went on the first light of the
    /// countdown comes round a whole lap later well inside it.
    /// </summary>
    private const long BehindTheLineAtTheGreenWindowMs = 2000;

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

    /// <summary>_redFlagStartedUtc on the monotonic clock (TimingClock.NowMs). See _raceStartClockMs.</summary>
    private long? _redFlagStartedClockMs;

    /// <summary>
    /// RaceStartUtc on the monotonic clock (TimingClock.NowMs) - what the
    /// race clock is actually measured with.
    ///
    /// The race clock, the time limit and the wait for stragglers used to
    /// be worked out from DateTime.UtcNow, which is the wall clock - and
    /// Windows steps the wall clock when it syncs the time. A laptop that
    /// joined the Wi-Fi mid-heat and was stepped 40 seconds forward threw
    /// the checkered on a 5:00 race 40 seconds early (a step back ran it
    /// long), and the same step could call the race on its stragglers or
    /// "correct" a start reference that was fine. TimingClock only ever goes
    /// forward, so durations are measured on it; the UTC stamps are kept as
    /// they were, for the screens, the journal and the snapshot an undo
    /// puts back.
    ///
    /// Each one is set wherever its UTC stamp is, and null when that is.
    /// A stamp put back from a snapshot or the journal has no reading of
    /// its own, so it is placed on the monotonic clock from the wall clock
    /// once, as it comes back (ClockMsAt) - the race then runs on from
    /// there unmoved by any later time sync.
    /// </summary>
    private long? _raceStartClockMs;

    /// <summary>
    /// Where a wall-clock moment falls on the monotonic clock
    /// (TimingClock.NowMs), read against the wall clock now. Only for a
    /// stamp put back from a snapshot or the journal, which has no reading
    /// of its own - see _raceStartClockMs.
    /// </summary>
    private long ClockMsAt(DateTime utc) =>
        NowMsSource() - (long)(UtcNowSource() - utc).TotalMilliseconds;

    /// <summary>
    /// The session's monotonic clock - TimingClock.NowMs, unless a test or
    /// the replay harness has put its own clock here.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P0): the narrowest seam that lets a
    /// recorded or scripted night be played through the engine on a clock
    /// the test controls - a five-minute heat in no time, every lap to the
    /// millisecond. Every clock read in the session goes through this or
    /// <see cref="UtcNowSource"/>, and the defaults are exactly the clocks
    /// the session read before, so the app behaves as it always did.
    /// </remarks>
    internal Func<long> NowMsSource { get; set; } = static () => TimingClock.NowMs;

    /// <summary>The session's wall clock - DateTime.UtcNow unless a test has put its own here. See NowMsSource.</summary>
    internal Func<DateTime> UtcNowSource { get; set; } = static () => DateTime.UtcNow;

    /// <summary>
    /// The moment the pass being judged arrived, on the session's clock, while
    /// ProcessLapEvent is judging it on <see cref="_judgingThreadId"/> - null
    /// the rest of the time. See NowMs.
    /// </summary>
    private long? _judgingAtMs;

    /// <summary>The thread ProcessLapEvent is judging a pass on - the only thread that sees _judgingAtMs.</summary>
    private int _judgingThreadId;

    /// <summary>
    /// A pass is never judged as having come before the last command that
    /// moved the race clock on (a green, a red, a yellow, a restore, a
    /// reset) - the session's state already includes that command. See
    /// JudgingMomentOf.
    /// </summary>
    private long? _judgeFloorClockMs;

    /// <summary>
    /// "Now" for everything the session works out from its clock: the moment
    /// the pass being judged arrived, while one is (see ProcessLapEvent), and
    /// the clock itself otherwise.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P1): every pass is judged by when it
    /// reached the app, not by when the race got round to it. The time
    /// limit, who the checkered applies to, a car's own window, whether a
    /// staggered car had been released, the start-reference check - all of
    /// them used to read the clock at processing time, so a window frozen for
    /// ten seconds around the time limit could finish a driver a lap early.
    /// Only on the judging thread: the ghosts read MsSinceGreen from their own
    /// timer thread, and they must always get the present.
    /// </remarks>
    private long NowMs =>
        _judgingAtMs is { } at && _judgingThreadId == Environment.CurrentManagedThreadId
            ? at
            : NowMsSource();

    /// <summary>The wall clock at <see cref="NowMs"/> - the moment a pass arrived while it is being judged.</summary>
    private DateTime UtcNow =>
        _judgingAtMs is { } at && _judgingThreadId == Environment.CurrentManagedThreadId
            ? UtcNowSource() - TimeSpan.FromMilliseconds(Math.Max(0, NowMsSource() - at))
            : UtcNowSource();

    /// <summary>
    /// When a pass reached the app, on the session's clock: its arrival stamp
    /// (DirectLapEvent.ReceivedMs, taken on the decoder's own thread), or now
    /// when it has none (0 - a test, or anything nobody stamped). Never later
    /// than now.
    /// </summary>
    internal long ArrivalOf(DirectLapEvent lap)
    {
        var now = NowMsSource();
        return lap.ReceivedMs > 0 && lap.ReceivedMs < now ? lap.ReceivedMs : now;
    }

    /// <summary>
    /// The moment a pass is judged at: when it arrived, but never before the
    /// last command that moved the race clock on (see _judgeFloorClockMs) -
    /// a pass that arrived just before a red, and is only got to after it,
    /// is judged where the red left the clock, which is where the race
    /// stood when it arrived.
    /// </summary>
    private long JudgingMomentOf(DirectLapEvent lap)
    {
        var arrived = ArrivalOf(lap);
        return _judgeFloorClockMs is { } floor && arrived < floor
            ? Math.Min(floor, NowMsSource())
            : arrived;
    }

    /// <summary>Notes that a command has just moved the race clock on - see _judgeFloorClockMs.</summary>
    private void NoteClockCommand() => _judgeFloorClockMs = NowMsSource();

    /// <summary>
    /// Where every pass and command is recorded, when AppSettings.
    /// RecordTimingPasses is on - null (nothing recorded) otherwise.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P0): recorded here, inside the session,
    /// rather than by each caller, so whatever drives the race - Race
    /// Control, the phone, the demo, the ghosts - is caught the same way, at
    /// the moment the race acts on it.
    /// </remarks>
    internal TimingRecorder? Recorder { get; set; }

    /// <summary>
    /// Above zero while a command made of other commands runs (Release All,
    /// an undo after a gap), so only the outer one is recorded - a replay of
    /// both would do it twice.
    /// </summary>
    private int _recordingSuppressed;

    /// <summary>Which Snapshot line each snapshot was recorded as, so a Restore can say which one it put back.</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SessionSnapshot, System.Runtime.CompilerServices.StrongBox<long>> _recordedSnapshots = new();

    /// <summary>
    /// Records a command, stamped with the session's clock and the state and
    /// flag it found. Returns the line's Seq, or null when nothing is being
    /// recorded. Never throws.
    /// </summary>
    private long? RecordCommand(TimingRecord record)
    {
        if (Recorder is not { } recorder || _recordingSuppressed > 0)
        {
            return null;
        }

        try
        {
            record.State = State.ToString();
            record.Flag = Flag.ToString();
            return recorder.Command(record, NowMsSource(), UtcNowSource());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Records the race's rules as they stand - at the countdown and the
    /// green, so a replay sets up the same race. The warning ladder goes as
    /// JSON; the sector lengths and the streak threshold are left out (they
    /// change no result).
    /// </summary>
    private void RecordConfig()
    {
        if (Recorder is null || _recordingSuppressed > 0)
        {
            return;
        }

        try
        {
            var config = new Dictionary<string, string>
            {
                ["TimeLimitMs"] = TimeLimit is { } limit ? ((long)limit.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
                ["LapLimit"] = LapLimit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                ["FinishOnNextCrossing"] = FinishOnNextCrossing.ToString(),
                ["MinLapTimeMs"] = MinLapTimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["StartType"] = StartType.ToString(),
                ["StaggerClockMode"] = StaggerClockMode.ToString(),
                ["DetectJumpStarts"] = DetectJumpStarts.ToString(),
                ["JumpStartPenaltyMs"] = JumpStartPenaltyMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["JumpStartCostsTheLap"] = JumpStartCostsTheLap.ToString(),
                ["StreakWindowPercent"] = StreakWindowPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["StreakFasterLapBreaks"] = StreakFasterLapBreaks.ToString(),
                ["IsPracticeMode"] = IsPracticeMode.ToString(),
                ["IsDemoMode"] = IsDemoMode.ToString(),
                ["SplitCount"] = SplitCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["WarningLadder"] = WarningLadder is null ? string.Empty : System.Text.Json.JsonSerializer.Serialize(WarningLadder),
            };

            RecordCommand(new TimingRecord { Command = "Config", Config = config });
        }
        catch
        {
            // A recording is never worth a race.
        }
    }

    /// <summary>
    /// Milliseconds since the green on the monotonic clock - red flags
    /// included, unlike ElapsedRaceTime - or 0 with no race start. What
    /// the ghosts are timed with, and what the start reference is checked
    /// against. See _raceStartClockMs.
    /// </summary>
    public long MsSinceGreen =>
        RaceStartUtc is { } start ? NowMs - (_raceStartClockMs ?? ClockMsAt(start)) : 0;

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

            // On the monotonic clock, so a Windows time sync mid-race does
            // not move it - see _raceStartClockMs.
            var nowMs = NowMs;
            var raw = TimeSpan.FromMilliseconds(nowMs - (_raceStartClockMs ?? ClockMsAt(start))) - _accumulatedRedTime;

            // Subtract the in-progress red period too, so the clock is
            // visibly frozen while the flag is out rather than jumping
            // backwards when it clears.
            if (_redFlagStartedUtc is { } redStart)
            {
                raw -= TimeSpan.FromMilliseconds(nowMs - (_redFlagStartedClockMs ?? ClockMsAt(redStart)));
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

    /// <summary>
    /// When LastKnownDeviceTimeMs was received (TimingClock.NowMs), for
    /// estimating the decoder clock between passes. The monotonic clock
    /// rather than the wall clock, so a time sync between the last pass
    /// and the green does not move the race's start reference.
    /// </summary>
    private long? _lastKnownDeviceClockMs;

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
        _lastKnownDeviceClockMs is { } receivedMs
            ? LastKnownDeviceTimeMs + (NowMs - receivedMs)
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
    /// The transponder actually crossing the line for a row: the row's own
    /// id, or - when one of the driver's other transponders is being filed
    /// under their main one this race - that one. The car on the board is
    /// the car the heard transponder is fitted to (ProfileStore.CarModelFor),
    /// so a driver out in their second car is announced in it.
    /// </summary>
    public string HeardAs(string rowTransponder) =>
        string.IsNullOrWhiteSpace(rowTransponder) ? rowTransponder
        : _heardAsByRow.TryGetValue(rowTransponder, out var heard) ? heard
        : _heardAsLastRace.TryGetValue(rowTransponder, out var heardLastRace) ? heardLastRace
        : rowTransponder;

    /// <summary>
    /// 5 Nov 2026, Bug hunt 11 review: HeardAs's rows for the race still on
    /// the board while a countdown pressed straight from Finished runs.
    /// StartCountdown forgets the last race's alternate decisions (see the
    /// WHY there), and _heardAsByRow with them - but the finished race's
    /// rows stay on the board until the green, and a driver who raced on
    /// their spare was then shown in their main car. Display only: nothing
    /// that decides where a lap goes reads it, so the new countdown's
    /// decisions start clean. Emptied with every other decision at the
    /// green, at Stage and wherever else they are cleared.
    /// </summary>
    private volatile Dictionary<string, string> _heardAsLastRace = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// For HeardAs: each row a transponder is being filed under this race,
    /// and that transponder. Replaced whole, never changed in place, since
    /// the voice queue reads it on its own thread while laps arrive on this
    /// one.
    /// </summary>
    private volatile Dictionary<string, string> _heardAsByRow = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>HeardAs's rows worked out again from the race's alternates - after a crash recovery puts them back.</summary>
    private void RebuildHeardAs()
    {
        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, merged) in _alternateMerged)
        {
            if (merged && AlternateTransponderOwner?.Invoke(id) is { Length: > 0 } primary)
            {
                rows.TryAdd(primary, id);
            }
        }

        _heardAsByRow = rows;
    }

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
        _heardAsByRow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _heardAsLastRace = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _mergedInto.Clear();
        _heardThisRace.Clear();
        _bothCarsWarned.Clear();
        _jumpFiledFromAlternate.Clear();
    }

    /// <summary>
    /// Main transponders whose countdown jump start was really their
    /// alternate's pass (merged before the green), and that alternate -
    /// written at the green, when the decisions themselves are cleared.
    /// See RefileCountdownJumpToAlternate.
    /// </summary>
    private readonly Dictionary<string, string> _jumpFiledFromAlternate = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Called on a main transponder's own pass, before it is filed: if its
    /// countdown jump start came from its alternate, and that alternate has
    /// not been heard since the green, the two are separate cars - so the
    /// jump, its penalty and any start lap the green credited for it go to
    /// the alternate, and the main car's pass starts a clean row of its own.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, bug hunt 10): a borrowed spare that jumped in the
    /// countdown was merged into its owner's main transponder, so the jump
    /// was booked under the owner. The green forgets the merge decisions
    /// but kept the jump - so the owner, who had not jumped, took the
    /// penalty, the red row and a start lap they never drove (their own
    /// first crossing was then thrown out as too soon), while the friend
    /// who jumped raced clean a lap down. The decisions are still cleared
    /// at the green (keeping them would put both cars in one row); only
    /// the jump is moved once the main car shows both are out. When the
    /// alternate is heard first after the green and merged again, it is
    /// the owner on their spare, and the jump stays where it is.
    /// </remarks>
    internal void RefileCountdownJumpToAlternate(string mainId)
    {
        if (State != RaceState.Racing
            || string.IsNullOrWhiteSpace(mainId)
            || !_jumpFiledFromAlternate.Remove(mainId, out var alternate)
            || _alternateMerged.ContainsKey(alternate))
        {
            return;
        }

        if (_jumpStarters.Remove(mainId))
        {
            _jumpStarters.Add(alternate);
        }

        if (_pendingJumpStartPenalties.Remove(mainId, out var owed))
        {
            _pendingJumpStartPenalties[alternate] = owed;
        }

        if (_jumpStartCrossingMs.Remove(mainId, out var crossedAt))
        {
            _jumpStartCrossingMs[alternate] = crossedAt;
        }

        // Only a row that is still exactly what the green made it - the
        // credited start lap and nothing since - is moved. Anything else
        // (a hand-released grid row, say) is the main car's own.
        if (_standings.TryGetValue(mainId, out var credited)
            && credited.StartLapCredited && credited.Laps == 1 && credited.LapTimesMs.Count == 1
            && !_standings.ContainsKey(alternate))
        {
            _standings.Remove(mainId);

            var moved = new RacerStanding
            {
                TransponderId = alternate,
                StartPosition = StartPositionOf(alternate),
            };

            CreditStartLap(moved, credited.LastLapDeviceTimeMs);
            _standings[alternate] = moved;
            RenumberStartPositionsOverStarters();
        }

        // Decided now, as a car of its own: its next pass must not be
        // merged back into the owner's row.
        _alternateMerged[alternate] = false;
        _heardThisRace.Add(alternate);

        Log($"{mainId} is on track as well as its alternate {alternate} - the jump start before the green "
            + $"was {alternate}'s, so it has been moved to {alternate}.");
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

    /// <summary>
    /// Earlier values of SessionGeneration that name the race now on the
    /// board, because an undo put that race back.
    ///
    /// Reset, and a Stage that abandons a race, move SessionGeneration on;
    /// undoing them puts the old race back but leaves the generation where
    /// it is, since everything else that watches it (the reset undo's own
    /// staleness check, the demo, the after-race guards) counts on it only
    /// ever going forward. So a correction made to that race before the
    /// reset still carried the old number, and undoing it was refused as
    /// "that race has been reset or replaced" with the race and the driver
    /// right there on the board again. A correction whose generation is in
    /// this set belongs to the race on the board, and UndoStandingEdit takes
    /// it. Emptied whenever the generation moves on, because from then on
    /// the board holds a different race; refilled from the snapshot a
    /// restore puts back.
    /// </summary>
    private readonly HashSet<int> _restoredGenerations = new();

    /// <summary>
    /// Whether a generation names the race now on the board: the current
    /// one, or one an undo brought back (see _restoredGenerations). Public
    /// so anything else keyed on SessionGeneration - the Best Streak and PBs
    /// history - can tell a race put back from a new one.
    /// </summary>
    public bool IsSameRace(int generation) =>
        generation == SessionGeneration || _restoredGenerations.Contains(generation);

    /// <summary>
    /// Moves SessionGeneration on for a new race, forgetting which earlier
    /// generations the old board answered to - they are not this race.
    /// </summary>
    private void StartNewGeneration()
    {
        SessionGeneration++;
        _restoredGenerations.Clear();
    }

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
        // WHY (23 Oct 2026, timing layer, P0): recorded for replay - see Recorder.
        RecordCommand(new TimingRecord { Command = "AdjustLaps", Transponder = transponderId, Value = delta });

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

            // The only timed lap had nothing to fold into, so its crossing
            // is gone too. Left as the lap reference, the next crossing was
            // timed from it rather than from the green: a misread at 4s
            // taken off, then a real start lap at 12s, booked an 8s start
            // lap against a 12s elapsed time. The last pass seen is kept, so
            // the minimum lap time still judges the next one.
            if (standing.LapTimesMs.Count == 0)
            {
                standing.LastLapDeviceTimeMs = 0;
                standing.ClearSplitReads();
            }

            // The removed index no longer exists; nothing may point at it.
            standing.RecoveryLapIndexes.RemoveAll(index => index >= standing.LapTimesMs.Count);

            // Kept in step with the lap list: the merged lap keeps the run
            // count it had, which is how the run stood when it ended.
            if (standing.StreakAtLap.Count > standing.LapTimesMs.Count)
            {
                standing.StreakAtLap.RemoveAt(standing.StreakAtLap.Count - 1);
            }

            // The merged lap's sectors no longer add up to it, so it shows
            // none (SectorsOf); the removed lap's go with it.
            if (standing.SectorsAtLap.Count > standing.LapTimesMs.Count)
            {
                standing.SectorsAtLap.RemoveRange(standing.LapTimesMs.Count, standing.SectorsAtLap.Count - standing.LapTimesMs.Count);
            }

            standing.OpenLastLap = null;

            standing.Laps--;
            mergedAny = true;
        }

        if (delta > 0)
        {
            standing.Laps = Math.Max(0, standing.Laps + delta);
        }

        // A credited jump-starter's zero start lap folded away: what is left
        // in the start-lap slot is their dash, already merged into it, so
        // nothing is "the start lap in effect" any more. Left set, their
        // first real flying lap after this was taken for that dash and kept
        // out of their best and the race's fastest. Undo puts it back - see
        // StandingEdit.StartLapCreditedBefore.
        if (mergedAny && standing.StartLapCredited && standing.LapTimesMs.Count <= 1)
        {
            standing.StartLapCredited = false;
        }

        if (mergedAny)
        {
            RecomputeAfterLapRemoval(standing);
        }

        Log($"Manual adjustment: {transponderId} laps {before} -> {standing.Laps}");
        SafeRaise(StandingAdjusted, h => h(standing), nameof(StandingAdjusted));

        // After the board has been told, so a finish this sets off is not
        // taken for a correction made after the flag.
        ApplyLapLimitAfterCorrection(standing, before);
        return true;
    }

    /// <summary>
    /// The lap limit, looked at again after a driver's laps were corrected
    /// by hand. It was only ever checked on a counted crossing, so a
    /// correction at the end of a lap race never moved the finish: +1 Lap
    /// for the leader's missed last crossing left the checkered in until
    /// somebody crossed at the limit again (the leader a lap too many), and
    /// -1 Lap on a phantom crossing that had brought the checkered out (a
    /// double read, a car carried back across the loop) left that driver
    /// marked as having taken it, so their real last lap was thrown away and
    /// they were classified a lap down.
    ///
    /// The checkered is never taken back in. Cars may already have taken it
    /// and pulled off, and a green now would be called as the start; a race
    /// flagged early ends there.
    /// </summary>
    private void ApplyLapLimitAfterCorrection(RacerStanding standing, int lapsBefore)
    {
        if (LapLimit is not { } limit || State != RaceState.Racing || IsPracticeMode)
        {
            return;
        }

        var id = standing.TransponderId;

        if (lapsBefore >= limit && standing.Laps < limit)
        {
            // Only for the car that brought the checkered out: the first to
            // take it, so every other real car took it on a later crossing.
            // Those others took it with the flag already out, which finishes
            // them whatever their count - a lap taken off for an earlier
            // double read changes nothing about that.
            var broughtItOut = _tookTheFlag.All(k =>
                string.Equals(k, id, StringComparison.OrdinalIgnoreCase)
                || Ghosts.IsGhost(k)
                || !_standings.TryGetValue(k, out var other)
                || other.LastLapDeviceTimeMs > standing.LastLapDeviceTimeMs);

            if (IsFinishing && !_finishingOnTime && broughtItOut && _tookTheFlag.Remove(id))
            {
                Log($"{id} is below the lap limit again - their next crossing takes the checkered flag.");
            }

            return;
        }

        // Brought up to the limit while the flag is still in: the lap added
        // is the crossing that reached it, so the checkered comes out as it
        // would have then. Not for a ghost while real cars race, and not
        // under a red, when no crossing would have counted either. Nor for a
        // black-flagged car (bug hunt 14), as for its crossings.
        var isGhost = _standings.Keys.Any(k => !Ghosts.IsGhost(k)) && Ghosts.IsGhost(id);

        if (lapsBefore < limit && standing.Laps >= limit && !IsFinishing && !isGhost && !standing.IsBlackFlagged && Flag != FlagState.Red)
        {
            Log($"Lap limit of {limit} reached by {id} - lap added by hand.");

            if (FinishOnNextCrossing)
            {
                BeginFinishing(timeLimitReached: false);
                MarkTookTheFlag(id);
            }
            else
            {
                Finish(timeLimitReached: false);
            }
        }
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
        // For a jump-starter credited the start lap at the green, index 1
        // is their start lap in effect (the dash from wherever they crossed
        // before the green) and is left out too, the same as ProcessLapEvent
        // does - see RacerStanding.StartLapCredited.
        // Nor is a lap that ran through a crash recovery: it spans the
        // stoppage, and left in here it could become the driver's best (or
        // the yardstick below) the moment the laps around it were removed.
        var lastStartLapIndex = standing.StartLapCredited ? 1 : 0;
        standing.FastestLapMs = standing.LapTimesMs
            .Where((t, i) => i > lastStartLapIndex && t > 0 && !standing.IsRecoveryLap(i))
            .DefaultIfEmpty(0)
            .Min();

        // WHY (20 Oct 2026): a pass rejected since the last counted lap
        // broke the run without leaving a StreakAtLap entry, so the last
        // entry is the run from BEFORE the rejection. It stays broken.
        standing.ConsecutiveValidLaps = !standing.RunBrokenSinceLastLap && standing.StreakAtLap.Count > 0
            ? standing.StreakAtLap[^1]
            : 0;

        // A recovery lap is never the yardstick for the next one - the same
        // as ProcessLapEvent leaves it, where it zeroes the baseline.
        var lastLapIndex = standing.LapTimesMs.Count - 1;
        standing.StreakBaselineLapMs = lastLapIndex > lastStartLapIndex && !standing.IsRecoveryLap(lastLapIndex)
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
        var realCarsRacing = _standings.Keys.Any(k => !Ghosts.IsGhost(k));
        var holder = _standings.Values
            .Where(s => s.FastestLapMs > 0 && !(realCarsRacing && Ghosts.IsGhost(s.TransponderId)))
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
        // WHY (23 Oct 2026, timing layer, P0): recorded for replay - see Recorder.
        RecordCommand(new TimingRecord { Command = "Penalty", Transponder = transponderId, Value = deltaMs });

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
        // WHY (23 Oct 2026, timing layer, P0): recorded for replay - see Recorder.
        RecordCommand(new TimingRecord { Command = "ClearPenalties", Transponder = transponderId });

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
        // WHY (23 Oct 2026, timing layer, P0): recorded for replay - see Recorder.
        RecordCommand(new TimingRecord { Command = "Warning", Transponder = transponderId, Text = reason });

        if (!_standings.TryGetValue(transponderId, out var standing))
        {
            return null;
        }

        var number = standing.Warnings.Count + 1;
        var step = WarningRules.StepFor(WarningLadder, number);

        var warning = new RaceWarning
        {
            AtUtc = UtcNow,
            RaceTimeMs = RaceStartUtc.HasValue && State is RaceState.Racing or RaceState.Finished
                ? (long)ElapsedRaceTime.TotalMilliseconds
                : 0,
            SinceGreenMs = RaceStartUtc.HasValue && State is RaceState.Racing or RaceState.Finished
                ? Math.Max(0, MsSinceGreen)
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

        // 17 Oct 2026, Bug Hunt 8, A01-1: a black flag after the checkered
        // can take away the last car the finish was waiting on.
        if (warning.Action == WarningAction.BlackFlag && IsFinishing && State == RaceState.Racing
            && EveryExpectedCarHasTakenTheFlag())
        {
            Log("Every car has taken the flag.");
            Finish(_finishingOnTime);
        }

        return warning;
    }

    /// <summary>
    /// Takes back a driver's most recent warning - a mis-click, or the wrong
    /// car - along with any time it added. Returns the warning removed, or
    /// null when there was none.
    /// </summary>
    public RaceWarning? RemoveLastWarning(string transponderId)
    {
        // WHY (23 Oct 2026, timing layer, P0): recorded for replay - see Recorder.
        RecordCommand(new TimingRecord { Command = "RemoveWarning", Transponder = transponderId });

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
        internal List<long[]?> SectorsBefore { get; init; } = new();
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

        // Taking a lap off a credited jump-starter can clear their start-lap
        // credit (see AdjustLaps), and putting the laps back needs it back.
        internal bool StartLapCreditedBefore { get; init; }

        // Taking off a car's only timed lap clears the crossing its next lap
        // is timed from (see AdjustLaps), so the next lap is timed from the
        // green instead. Undo puts the crossing back, or - when a lap has
        // been counted since - takes the restored lap's time back out of it.
        internal long LastLapDeviceTimeBefore { get; init; }
        internal bool LapReferenceCleared { get; set; }

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

        // Whether the driver had taken the checkered either side of the
        // correction - a lap correction at the limit can change that (see
        // ApplyLapLimitAfterCorrection), and Undo puts it back.
        internal bool TookTheFlagBefore { get; init; }
        internal bool TookTheFlagAfter { get; set; }

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
            SectorsBefore = Enumerable.Range(0, standing.LapTimesMs.Count)
                .Select(i => i < standing.SectorsAtLap.Count ? standing.SectorsAtLap[i] : null)
                .ToList(),
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
            TookTheFlagBefore = _tookTheFlag.Contains(standing.TransponderId),
            StartLapCreditedBefore = standing.StartLapCredited,
            LastLapDeviceTimeBefore = standing.LastLapDeviceTimeMs,
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
        edit.TookTheFlagAfter = _tookTheFlag.Contains(standing.TransponderId);
        edit.LapReferenceCleared = edit.LastLapDeviceTimeBefore > 0 && standing.LastLapDeviceTimeMs == 0;

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
        // WHY (23 Oct 2026, timing layer, P0): recorded so a replay knows a
        // hand correction was undone here (it cannot redo it - see the harness).
        RecordCommand(new TimingRecord { Command = "UndoEdit", Transponder = edit.TransponderId });

        if (!IsSameRace(edit.Generation) || !_standings.TryGetValue(edit.TransponderId, out var standing))
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
            // By lap index, so each new lap keeps its own sectors whatever
            // length the list had - one restored from an older journal is short.
            var newSectors = Enumerable.Range(afterCount, Math.Max(0, standing.LapTimesMs.Count - afterCount))
                .Select(i => i < standing.SectorsAtLap.Count ? standing.SectorsAtLap[i] : null)
                .ToList();
            var shift = edit.LapTimesBefore.Count - afterCount;
            var newRecovery = standing.RecoveryLapIndexes.Where(i => i >= afterCount).Select(i => i + shift).ToList();
            var lapsCountedSince = standing.Laps - edit.LapsAfter;

            // See StandingEdit.LapReferenceCleared: the first lap counted
            // since was timed from the green, so it also covers the laps
            // going back in front of it.
            if (edit.LapReferenceCleared)
            {
                if (newTimes.Count > 0)
                {
                    newTimes[0] = Math.Max(0, newTimes[0] - edit.LapTimesBefore.Sum());
                }
                else if (standing.LastLapDeviceTimeMs == 0)
                {
                    standing.LastLapDeviceTimeMs = edit.LastLapDeviceTimeBefore;
                }
            }

            standing.LapTimesMs.Clear();
            standing.LapTimesMs.AddRange(edit.LapTimesBefore);
            standing.LapTimesMs.AddRange(newTimes);

            standing.StreakAtLap.Clear();
            standing.StreakAtLap.AddRange(edit.StreakBefore);
            standing.StreakAtLap.AddRange(newStreaks);

            standing.SectorsAtLap.Clear();
            standing.SectorsAtLap.AddRange(edit.SectorsBefore);
            standing.SectorsAtLap.AddRange(newSectors);
            standing.OpenLastLap = null;

            standing.RecoveryLapIndexes.Clear();
            standing.RecoveryLapIndexes.AddRange(edit.RecoveryBefore);
            standing.RecoveryLapIndexes.AddRange(newRecovery);

            standing.Laps = Math.Max(0, edit.LapsBefore + lapsCountedSince);

            // With the lap list it went with, and before the recompute below
            // reads it - see StandingEdit.StartLapCreditedBefore.
            standing.StartLapCredited = edit.StartLapCreditedBefore;

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

                // WHY (20 Oct 2026): the run saved with the edit is how it
                // stood before it; a pass rejected since has broken it, and
                // putting the saved count back revived it.
                if (standing.RunBrokenSinceLastLap)
                {
                    standing.ConsecutiveValidLaps = 0;
                }
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

        // Whether the driver has taken the checkered goes back as the
        // correction found it, while that finish is still going on - after
        // the board has been told, as in AdjustLaps. The flag stays out.
        if (IsFinishing && State == RaceState.Racing && edit.TookTheFlagBefore != edit.TookTheFlagAfter)
        {
            if (edit.TookTheFlagBefore)
            {
                MarkTookTheFlag(standing.TransponderId);
            }
            else if (_tookTheFlag.Remove(standing.TransponderId))
            {
                Log($"{standing.TransponderId} has not taken the checkered flag after all - their next crossing takes it.");
            }
        }
    }

    public IReadOnlyList<RacerStanding> GetOrderedStandings() =>
        _standings.Values.OrderByDescending(s => s.Laps).ThenBy(s => s.ElapsedMs).ToList();

    /// <summary>
    /// The leader of the race proper: the first real car, passing over any
    /// ghost ahead of it - a ghost can never end the race, so its laps are
    /// not the ones counting down to the flag. In a field of nothing but
    /// ghosts, simply the first of them. Null before anyone has crossed.
    ///
    /// A black-flagged car is passed over too, for the same reason (bug
    /// hunt 13): it cannot end the race either, so laps to go and laps led
    /// are not counted from it. Only when every real car is black-flagged
    /// is the first of those the leader.
    /// </summary>
    public RacerStanding? RaceLeader()
    {
        var ordered = GetOrderedStandings();
        return ordered.FirstOrDefault(s => !Ghosts.IsGhost(s.TransponderId) && !s.IsBlackFlagged)
            ?? ordered.FirstOrDefault(s => !Ghosts.IsGhost(s.TransponderId))
            ?? ordered.FirstOrDefault();
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

            var left = end - UtcNowSource();
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
        WarmUpEndsUtc = UtcNowSource() + length;
        Log($"Warm-up open - {WarmUpText.Describe((int)length.TotalSeconds)} of free laps. Passes are not counted.");
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
        // WHY (23 Oct 2026, timing layer, P0): recorded for replay - see Recorder.
        RecordCommand(new TimingRecord { Command = "Stage" });

        _standings.Clear();
        ClearAlternateDecisions();
        StartNewGeneration();

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
    /// <remarks>
    /// The run of sightings each car has on the loop is deliberately NOT
    /// forgotten here. It is not something flagged in a race, it is what the
    /// decoder has been seeing, and a car left sitting on the loop is still
    /// sitting there when the next start begins. Clearing it made every
    /// parked car start again at a run of one the moment a countdown was
    /// started without Stage (or after a Stage of only a few seconds), so its
    /// first countdown pass was called as a jump start. An old entry cannot
    /// mark a newly arrived car as parked: a run only continues from a pass
    /// at most ParkedRepeatGapMs earlier on the same clock.
    /// </remarks>
    private void ClearJumpStartWindow()
    {
        _jumpStarters.Clear();
        _pendingJumpStartPenalties.Clear();
        _jumpStartCrossingMs.Clear();
        _parkedOnLoopLogged.Clear();
    }

    public void StartCountdown()
    {
        // WHY (23 Oct 2026, timing layer, P0): recorded for replay - see Recorder.
        RecordConfig();
        RecordCommand(new TimingRecord { Command = "Countdown" });

        // Read before the state moves on. A countdown straight over a race
        // that is running starts the next race there and then, and the one
        // running is abandoned - Race Control has asked, or the phone has.
        var overRunningRace = State == RaceState.Racing;

        // Bug hunt 14: read here too - a countdown that did not come
        // through Stage (nor restarts one) is a fresh start, see below.
        var freshStart = State is not (RaceState.Staging or RaceState.Countdown);

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

            // WHY (2026-11-05, Bug hunt 11: alternate decisions carried into
            // a countdown that skipped Stage): Stage forgets the last race's
            // alternate-transponder decisions; a countdown straight from
            // Finished or Idle did not. The owner's main transponder was
            // still "heard this race" from the last heat (or from passes
            // between races), so a spare that jumped in the countdown was
            // kept as a separate car and the jump, penalty and crossing went
            // under the spare. The green then forgot the decisions, the
            // spare merged into the owner on its next pass, and the penalty
            // was never applied - with an orphaned 1-lap start row for the
            // spare. Forgotten here as Stage does, so the jump lands on the
            // owner's row exactly as it does after Stage.
            //
            // 5 Nov 2026, Bug hunt 11 review: but the finished race's rows
            // stay on the board until the green, so who they were heard as
            // is kept for display (see _heardAsLastRace) - only the decisions
            // the new race's laps and jumps are filed by are forgotten.
            var heardAsOnTheBoard = _heardAsByRow;

            ClearAlternateDecisions();

            _heardAsLastRace = heardAsOnTheBoard;
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
        //
        // Bug hunt 14 (7 Nov 2026): a yellow or red left out from before the
        // countdown goes too, when the countdown did not come through Stage.
        // One thrown after the finish (F4/F5 or the phone) or at Idle was
        // still showing, and the automatic start stopped before the green as
        // if it had been thrown during this start. Stage clears it already;
        // a countdown restarted mid-start keeps it, since that one was thrown
        // during this start. A leftover red's stoppage goes with it - there
        // is no race for it to have paused - so it is dropped, not banked.
        if (overRunningRace || (freshStart && (Flag is FlagState.Yellow or FlagState.Red)))
        {
            if (!overRunningRace)
            {
                _redFlagStartedUtc = null;
                _redFlagStartedClockMs = null;
            }

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
            // back. A red or a yellow only changes the flag, so each is
            // checked on its own; one that was already out when the green was
            // asked for is the one this green is meant to clear, so it does
            // not count. A yellow thrown while the countdown call finished
            // used to be overruled by the green that followed it - the same
            // rule AutoStartMayContinue keeps for the automatic start.
            var overruledBy =
                SessionGeneration != generationAtRequest ? "the session was reset or re-staged"
                : State != stateAtRequest ? $"the race moved to {State}"
                : Flag == FlagState.Red && flagAtRequest != FlagState.Red ? "a red flag came out"
                : Flag == FlagState.Yellow && flagAtRequest != FlagState.Yellow ? "a yellow flag came out"
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
        // WHY (23 Oct 2026, timing layer, P0/P1): recorded for replay (the
        // rules first, for a green that starts a race), and no pass is judged
        // as having come before it - see _judgeFloorClockMs.
        if (State != RaceState.Racing)
        {
            RecordConfig();
        }

        RecordCommand(new TimingRecord { Command = State == RaceState.Racing ? "Resume" : "Green", DeviceTimeMs = currentDeviceTimeMs });
        NoteClockCommand();

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

            // A car already at the lap limit got there while the field was
            // stopped: a missed crossing added by hand under the red, which
            // leaves the checkered in (see ApplyLapLimitAfterCorrection).
            // Nothing looked again here, so the green went out, the leader
            // had to cross once more before the checkered came out, and the
            // whole field ran a lap long. The restart puts it out instead, as
            // the crossing that reached the limit would have, and every car
            // already there has finished. Not for a ghost while real cars
            // race, which can never put the checkered out - nor for a
            // black-flagged car, out of the race (bug hunt 14).
            if (LapLimit is { } limit && !IsPracticeMode)
            {
                var realCarsRacing = _standings.Keys.Any(k => !Ghosts.IsGhost(k));
                var atTheLimit = _standings.Values
                    .Where(s => s.Laps >= limit && !s.IsBlackFlagged && !(realCarsRacing && Ghosts.IsGhost(s.TransponderId)))
                    .Select(s => s.TransponderId)
                    .ToList();

                if (atTheLimit.Count > 0)
                {
                    Log($"Lap limit of {limit} already reached by {string.Join(", ", atTheLimit)} - the race resumes on its last lap.");

                    if (FinishOnNextCrossing)
                    {
                        BeginFinishing(timeLimitReached: false);

                        foreach (var id in atTheLimit)
                        {
                            MarkTookTheFlag(id);
                        }
                    }
                    else
                    {
                        Finish(timeLimitReached: false);
                    }

                    return;
                }
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
        _finishingSinceClockMs = null;
        _checkeredOutAtClockMs = null;
        _tookTheFlag.Clear();

        // Read before the decisions are cleared - see
        // RefileCountdownJumpToAlternate. Only a main transponder that was
        // never heard on its own before the green: if it was, both cars
        // were out and the jump cannot be pinned on either.
        var heardAsBeforeGreen = _heardAsByRow;
        var heardOwnIdBeforeGreen = new HashSet<string>(_heardThisRace, StringComparer.OrdinalIgnoreCase);

        _standings.Clear();
        ClearAlternateDecisions();

        if (startedFrom is RaceState.Staging or RaceState.Countdown)
        {
            foreach (var (mainId, alternate) in heardAsBeforeGreen)
            {
                if (!heardOwnIdBeforeGreen.Contains(mainId)
                    && (_jumpStartCrossingMs.ContainsKey(mainId) || _jumpStarters.Contains(mainId)))
                {
                    _jumpFiledFromAlternate[mainId] = alternate;
                }
            }
        }

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

        StartNewGeneration();
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

                CreditStartLap(jumper, crossedAt);

                _standings[transponder] = jumper;

                Log($"{transponder} was past the line at the green - credited with the start lap.");
            }

            // A credited jumper has counted a lap already - numbered now, as
            // ProcessLapEvent numbers everybody else on their first lap.
            RenumberStartPositionsOverStarters();
        }
        _startReferenceChecked = false;
        // The monotonic reading first: read from another thread (the
        // ghosts) between the two, the old race's reading under the new
        // start would make it look minutes into the race.
        _raceStartClockMs = NowMs;
        RaceStartUtc = UtcNow;

        // Set before the state changes, not just before the flag: Race
        // Control's StateChanged handler reads it to tell a fresh start
        // (clear the board and the announcer) from anything else, and set
        // after SetState it still held the last green's value there.
        GreenStartedTheRace = true;
        SetState(RaceState.Racing);
        SetFlag(FlagState.Green);
        Log("Green flag - racing begins.");
    }

    /// <summary>
    /// Books the start lap for a car that was already past the line at the
    /// green - see GreenFlag. Shared with RefileCountdownJumpToAlternate,
    /// which moves such a credit onto the car that actually earned it.
    /// </summary>
    private static void CreditStartLap(RacerStanding jumper, long crossedAt)
    {
        jumper.Laps = 1;
        jumper.ElapsedMs = 0;
        jumper.LapTimesMs.Clear();
        jumper.LapTimesMs.Add(0);
        jumper.StartLapCredited = true;

        // The start lap is the first lap of their run, exactly as it
        // is for everybody whose start lap comes through
        // ProcessLapEvent: a run of one, with one StreakAtLap entry
        // to sit beside the zero above, and no yardstick yet (a start
        // lap is never the lap the next one is judged against).
        //
        // These were left alone, so a credited jumper's run was
        // always one lap behind a clean starter's after the same
        // laps - their streak was called a lap late - and StreakAtLap
        // was one entry short of LapTimesMs for the whole race. Every
        // run marker on the lap strip and in the lap popup sat one
        // lap early, and removing a lap by hand kept the removed
        // lap's run value, because the trim there only fires when
        // StreakAtLap is the LONGER of the two.
        jumper.StreakAtLap.Clear();
        jumper.StreakAtLap.Add(1);

        // A start lap has no sectors.
        jumper.SectorsAtLap.Clear();
        jumper.SectorsAtLap.Add(null);
        jumper.ClearSplitReads();
        jumper.ConsecutiveValidLaps = 1;
        jumper.RunBrokenSinceLastLap = false;
        jumper.StreakLapTimeSumMs = 0;
        jumper.StreakLapCount = 0;
        jumper.StreakBaselineLapMs = 0;

        // Measured from where they actually crossed, so their
        // first real lap comes out a full lap long rather than
        // short by however early they went.
        jumper.LastLapDeviceTimeMs = crossedAt;
        jumper.LastSeenDeviceTimeMs = crossedAt;
    }

    public void YellowFlag()
    {
        // WHY (23 Oct 2026, timing layer, P0/P1): recorded for replay, and it
        // ends a red - see _judgeFloorClockMs.
        RecordCommand(new TimingRecord { Command = "Yellow" });
        NoteClockCommand();

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
        // WHY (23 Oct 2026, timing layer, P0/P1): recorded for replay, and it
        // stops the clock - see _judgeFloorClockMs.
        RecordCommand(new TimingRecord { Command = "Red" });
        NoteClockCommand();

        if (Flag != FlagState.Red)
        {
            _redFlagStartedUtc = UtcNow;
            _redFlagStartedClockMs = NowMs;
        }
        SetFlag(FlagState.Red);
        Log("Red flag - race clock paused, laps will not be counted until green resumes.");
    }

    /// <summary>Banks the time spent under the current red flag and clears it. Called when racing resumes or the session resets.</summary>
    private void EndRedFlagPeriod()
    {
        if (_redFlagStartedUtc is { } redStart)
        {
            // Measured on the monotonic clock - see _raceStartClockMs.
            _accumulatedRedTime += TimeSpan.FromMilliseconds(NowMs - (_redFlagStartedClockMs ?? ClockMsAt(redStart)));
            _redFlagStartedUtc = null;
            _redFlagStartedClockMs = null;

            // Nobody can take a flag while the field is stopped, so the wait
            // for stragglers starts again when it moves. Otherwise a red
            // during the last lap - or a race restored from the journal,
            // which always comes back under red - was called on the first
            // tick after the restart, before anyone had had the chance.
            if (IsFinishing)
            {
                _finishingSinceUtc = UtcNow;
                _finishingSinceClockMs = NowMs;
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
        // WHY (23 Oct 2026, timing layer, P0): recorded for replay - see Recorder.
        RecordCommand(new TimingRecord { Command = "Checkered" });

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
        _finishingSinceClockMs = null;
        _checkeredOutAtClockMs = null;

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
        TimeSpan? ElapsedAtFinish = null,

        // Every SessionGeneration the race went by when the snapshot was
        // taken - the one then current, and any it had answered to since an
        // earlier undo - so that once it is put back, the corrections made
        // to it before the reset can still be undone. See
        // _restoredGenerations. Null - the journal, whose generations meant
        // nothing to the app that is now running - answers to none.
        IReadOnlyList<int>? Generations = null,

        // Whether the grid is the last race's order, numbered over the cars
        // that race - see RenumberStartPositionsOverStarters. Null - a
        // journal written before it was stored - leaves it as it is.
        bool? RenumberGridOverStarters = null);

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

                // Copied too, or an undone Reset or Stage brought a credited
                // jump-starter back without it, and their grid dash then
                // competed for best lap - see RacerStanding.StartLapCredited.
                StartLapCredited = s.StartLapCredited,

                // Copied too (20 Oct 2026), or after an undone Reset or
                // Stage, removing a lap by hand brought back a run a
                // rejected pass had broken - see RunBrokenSinceLastLap.
                RunBrokenSinceLastLap = s.RunBrokenSinceLastLap,
            };
            copy.LapTimesMs.AddRange(s.LapTimesMs);
            copy.StreakAtLap.AddRange(s.StreakAtLap);
            copy.SectorsAtLap.AddRange(s.SectorsAtLap.Select(sectors => sectors?.ToArray()));
            copy.RecoveryLapIndexes.AddRange(s.RecoveryLapIndexes);
            copy.Warnings.AddRange(s.Warnings);
            return copy;
        }).ToList();

        // Copies, not the live collections - Reset clears those in place.
        var snapshot = new SessionSnapshot(copies, State, Flag, RaceStartUtc, _raceStartDeviceTimeMs, _accumulatedRedTime, _redFlagStartedUtc,
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
            _elapsedAtFinish,
            _restoredGenerations.Append(SessionGeneration).ToList(),
            _renumberGridOverStarters);

        // WHY (23 Oct 2026, timing layer, P0): recorded, and remembered by
        // its line, so a Restore can say which snapshot it put back and a
        // replay can take the same one at the same point.
        if (RecordCommand(new TimingRecord { Command = "Snapshot" }) is { } snapshotId)
        {
            _recordedSnapshots.AddOrUpdate(snapshot, new System.Runtime.CompilerServices.StrongBox<long>(snapshotId));
        }

        return snapshot;
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
        // WHY (23 Oct 2026, timing layer, P0/P1): recorded for replay (a
        // journal's snapshot has no line of its own, so no id), and no pass is
        // judged as having come before it - see _judgeFloorClockMs.
        RecordCommand(new TimingRecord
        {
            Command = "Restore",
            SnapshotId = _recordedSnapshots.TryGetValue(snapshot, out var recordedAs) ? recordedAs.Value : null,
        });
        NoteClockCommand();

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

        // The race put back answers to the generations it went by, while
        // SessionGeneration itself stays where it is - see
        // _restoredGenerations. Whatever the board answered to before is
        // dropped first: that race is no longer on it.
        _restoredGenerations.Clear();

        if (snapshot.Generations is not null)
        {
            foreach (var generation in snapshot.Generations)
            {
                if (generation != SessionGeneration)
                {
                    _restoredGenerations.Add(generation);
                }
            }
        }

        RaceStartUtc = snapshot.RaceStartUtc;
        _elapsedAtFinish = snapshot.ElapsedAtFinish;
        _raceStartDeviceTimeMs = snapshot.RaceStartDeviceTimeMs;
        _accumulatedRedTime = snapshot.AccumulatedRedTime;
        _redFlagStartedUtc = snapshot.RedFlagStartedUtc;

        // Placed on the monotonic clock once, here - see _raceStartClockMs.
        _raceStartClockMs = RaceStartUtc is { } restoredStart ? ClockMsAt(restoredStart) : null;
        _redFlagStartedClockMs = _redFlagStartedUtc is { } restoredRed ? ClockMsAt(restoredRed) : null;
        _raceFastestLapMs = snapshot.RaceFastestLapMs;
        _raceFastestLapTransponder = snapshot.RaceFastestLapTransponder;

        IsFinishing = snapshot.IsFinishing;
        _finishingOnTime = snapshot.IsFinishing && snapshot.FinishingOnTime;

        // The straggler grace runs from here when the snapshot didn't say
        // when the flag came out - a journal restored after a crash - so a
        // race that was on its last lap is not called the moment it resumes.
        _finishingSinceUtc = snapshot.IsFinishing ? (DateTime?)(snapshot.FinishingSinceUtc ?? UtcNow) : null;
        _finishingSinceClockMs = _finishingSinceUtc is { } restoredFinishing ? ClockMsAt(restoredFinishing) : null;

        // WHY (23 Oct 2026, timing layer, P1): nobody knows when a restored
        // race's checkered came out, so it applies to every pass, as it always did.
        _checkeredOutAtClockMs = null;

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
        _renumberGridOverStarters = snapshot.RenumberGridOverStarters ?? _renumberGridOverStarters;
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

        // A race put back is not a race starting, whatever flag it comes
        // back under. Set before the state changes, so StateChanged
        // handlers see it too - see GreenFlag.
        GreenStartedTheRace = false;
        SetState(snapshot.State);
        SetFlag(snapshot.Flag);
        Log("Session restored (reset undone).");
    }

    /// <summary>
    /// Puts back a race an undo is bringing back - after Reset, or after
    /// Stage or Countdown abandoned it - with the clock where it stood when
    /// the snapshot was taken, and a race that was running held under red.
    ///
    /// RestoreSnapshot alone put the start time back exactly as it was, so
    /// every second between the reset and the undo was charged to the race
    /// clock, though no lap could be counted in it. A 5:00 race reset at 4:10
    /// and undone a minute later came back at 5:10 under green: the next tick
    /// threw the checkered with 50 seconds never run, and a race reset on its
    /// last lap was called on the spot. The crash restore in RaceJournal has
    /// the same problem with the time the app was down, and this answers it
    /// the same way:
    ///
    /// The start moves forward by the gap, so the clock picks up where it
    /// stopped. A red that was already out at the snapshot has its time up
    /// to then banked into the red total, because the red is restarted now
    /// and its old start would otherwise take that time off twice - see
    /// RaceJournal.Restore for the arithmetic. And the race comes back
    /// stopped, under red from this moment, because the cars have been
    /// driving all this time with nothing counted: the director gets them
    /// back in order and goes green, and green after red is called as a
    /// restart. A race on its last lap keeps its checkered; the wait for
    /// stragglers starts again at that green (see EndRedFlagPeriod).
    ///
    /// The decoder's own clock, and every reading taken on it, moves
    /// forward by the same gap - see the comment where it is done. Left
    /// behind, each driver's next crossing was booked as a lap as long as
    /// the time the race was gone.
    ///
    /// Anything but a running race - finished, idle, staging - has no clock
    /// running, and is put back exactly as it was.
    /// </summary>
    /// <param name="snapshot">What CreateSnapshot took.</param>
    /// <param name="takenUtc">When it was taken - the moment of the reset or abandon.</param>
    /// <returns>True when a running race came back held under red.</returns>
    /// <param name="nowUtc">
    /// The moment to measure the gap to, for a test that needs the gap to
    /// be exactly what it says. Left out on every real call, which measures
    /// to now.
    ///
    /// It is here because the gap is wall-clock time and the arithmetic
    /// below moves the decoder's reference by it, so a test that sets
    /// takenUtc to "a minute ago" and then asserts a lap time to the
    /// millisecond is really asserting how long the test itself took.
    /// BugHunt5ResultsTests did exactly that and passed on one machine for
    /// a day before failing on another by 20ms - the time that machine
    /// spent between taking the snapshot and restoring it. A test that
    /// depends on the speed of the PC it runs on is a test that will
    /// eventually be ignored, so the gap is made exact instead of the
    /// assertion made loose: what that test is for is that the laps still
    /// add up to the elapsed time, and that is an exact claim.
    /// </param>
    public bool RestoreSnapshotAfterGap(SessionSnapshot snapshot, DateTime takenUtc, DateTime? nowUtc = null)
    {
        // WHY (23 Oct 2026, timing layer, P0): recorded once, as itself, with
        // the snapshot it puts back - the restore inside it is not recorded
        // again. The body is unchanged.
        RecordCommand(new TimingRecord
        {
            Command = "RestoreAfterGap",
            SnapshotId = _recordedSnapshots.TryGetValue(snapshot, out var recordedAs) ? recordedAs.Value : null,
            TakenUtc = takenUtc,
        });

        _recordingSuppressed++;

        try
        {
            return RestoreSnapshotAfterGapCore(snapshot, takenUtc, nowUtc);
        }
        finally
        {
            _recordingSuppressed--;
        }
    }

    /// <summary>RestoreSnapshotAfterGap itself - see there.</summary>
    private bool RestoreSnapshotAfterGapCore(SessionSnapshot snapshot, DateTime takenUtc, DateTime? nowUtc)
    {
        if (snapshot.State != RaceState.Racing || snapshot.RaceStartUtc is not { } start)
        {
            RestoreSnapshot(snapshot);
            return false;
        }

        var now = nowUtc ?? UtcNow;

        // Floored at zero, for a clock that moved backwards in between - a
        // time sync, say. Shifting by a negative gap would wind the race
        // clock forward instead.
        var gap = now - takenUtc;

        if (gap < TimeSpan.Zero)
        {
            gap = TimeSpan.Zero;
        }

        var redBeforeSnapshot = TimeSpan.Zero;

        if (snapshot.RedFlagStartedUtc is { } redStartedUtc)
        {
            redBeforeSnapshot = takenUtc - redStartedUtc;

            if (redBeforeSnapshot < TimeSpan.Zero)
            {
                redBeforeSnapshot = TimeSpan.Zero;
            }
        }

        // The DECODER clock has to move with the race clock, and it did
        // not.
        //
        // The start moves forward by the gap below, but the decoder
        // reference and every driver's stored readings were left on the
        // clock they were taken on - and the decoder went on counting
        // throughout. So the next crossing after the green was measured
        // from a reference the whole gap behind where the race now starts:
        // on the 5:00 heat reset at 4:10 and undone a minute later, a car
        // crossing five seconds after the green was booked a 67-second lap
        // and its ElapsedMs read 1:25 while the race clock read 0:25. Both
        // are saved - RaceFiling copies ElapsedMs into the result and
        // RaceJournal into the journal - and nothing marked that lap as one
        // run through a stoppage, so it sat in the pace chart as a real lap,
        // broke the streak and could become the streak baseline.
        //
        // Nothing else caught it. The start-reference check in
        // RaceControlSession.Laps.cs fires once per green and had already
        // fired in the race being put back, and the first-crossing-after-
        // recovery path needs a reading of zero, which the snapshot
        // restores non-zero.
        //
        // RaceJournal.Restore clears the per-driver readings instead,
        // because a crash may have taken the decoder with it and its clock
        // is then anybody's guess. Here the decoder has been running the
        // whole time and the gap is known exactly, so the readings are
        // moved by it rather than thrown away - which leaves the undo
        // behaving as a red flag out for the same time would: the lap
        // through the stoppage is long, and every lap since the green still
        // adds up to the driver's elapsed time.
        var gapMs = (long)gap.TotalMilliseconds;

        RestoreSnapshot(snapshot with
        {
            Flag = FlagState.Red,
            RaceStartUtc = start + gap,
            RaceStartDeviceTimeMs = snapshot.RaceStartDeviceTimeMs is { } reference ? reference + gapMs : null,
            AccumulatedRedTime = snapshot.AccumulatedRedTime + redBeforeSnapshot,
            RedFlagStartedUtc = now,
            FinishingSinceUtc = snapshot.FinishingSinceUtc is { } finishingSince ? finishingSince + gap : null,
        });

        // The live rows rather than the snapshot's, so this moves the one
        // row RestoreSnapshot kept where an old journal held two for the
        // same transponder - see KeepOneOfCaseVariants.
        //
        // A zero is left alone in both: it means "never seen" and "no
        // counted lap yet", and moving it would turn this driver's first
        // crossing into a lap as long as the gap.
        foreach (var standing in _standings.Values)
        {
            // 17 Oct 2026, Bug Hunt 8, A01-2: a ghost's readings are on
            // MsSinceGreen, which the moved RaceStartUtc already carries on
            // from where it was. Shifting them too put them gapMs ahead, and
            // the ghost's next lap came out negative ("decoder clock went
            // backwards") or short.
            if (Ghosts.IsGhost(standing.TransponderId))
            {
                continue;
            }

            if (standing.LastLapDeviceTimeMs > 0)
            {
                standing.LastLapDeviceTimeMs += gapMs;
            }

            if (standing.LastSeenDeviceTimeMs > 0)
            {
                standing.LastSeenDeviceTimeMs += gapMs;
            }
        }

        Log("Race put back under a red flag - laps count again from the green.");
        return true;
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
            RebuildHeardAs();
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
        // WHY (23 Oct 2026, timing layer, P0/P1): recorded for replay - see
        // Recorder and _judgeFloorClockMs.
        RecordCommand(new TimingRecord { Command = "Reset" });
        NoteClockCommand();

        _standings.Clear();
        ClearAlternateDecisions();
        StartNewGeneration();
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
        _renumberGridOverStarters = false;

        IsFinishing = false;
        _finishingOnTime = false;
        _finishingSinceUtc = null;
        _finishingSinceClockMs = null;
        _checkeredOutAtClockMs = null;
        _tookTheFlag.Clear();
        _raceStartDeviceTimeMs = null;
        RaceStartUtc = null;
        _raceStartClockMs = null;
        _accumulatedRedTime = TimeSpan.Zero;
        _redFlagStartedUtc = null;
        _redFlagStartedClockMs = null;
        EndWarmUp("Warm-up canceled.");
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
            //
            // WHY (23 Oct 2026, timing layer, P1): stamped with the instant
            // the limit was actually reached - now less however far past it
            // the clock already is - so whether a pass is under the flag does
            // not depend on whether the poll or a pass noticed first. See
            // _checkeredOutAtClockMs.
            var pastTheLimitMs = Math.Max(0, (long)(ElapsedRaceTime - limit).TotalMilliseconds);
            BeginFinishing(timeLimitReached: true, checkeredOutAtMs: NowMs - pastTheLimitMs);
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

        // On the monotonic clock - see _raceStartClockMs.
        if (IsFinishing && _finishingSinceUtc is { } since
            && TimeSpan.FromMilliseconds(NowMs - (_finishingSinceClockMs ?? ClockMsAt(since))) > grace)
        {
            // 17 Oct 2026, Bug Hunt 8, A01-1: a black-flagged car is not
            // being waited on, so the log does not name it.
            var waitingOn = _standings.Values
                .Where(st => !st.IsBlackFlagged && !_tookTheFlag.Contains(st.TransponderId))
                .Select(st => st.TransponderId)
                .ToList();
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
    /// <param name="parkedOnTheLoop">
    /// The car has been sitting on the loop since before this pass - see
    /// NoteSightingBeforeGreen. Its crossing is still recorded (it IS on the
    /// line, and the green credits its start lap the same way), but it is
    /// not flagged, penalised or called out: it did not go early, it was
    /// put down in the wrong place.
    /// </param>
    private void RegisterJumpStart(string transponderId, long? crossingDeviceTimeMs = null, bool parkedOnTheLoop = false)
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

        if (!DetectJumpStarts || parkedOnTheLoop)
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

    /// <summary>
    /// A pass this soon after the same car's previous one, before the green,
    /// is the car still being there rather than the car arriving. An EasyLap
    /// reports a transponder sitting on its loop every two seconds.
    /// </summary>
    private const long ParkedRepeatGapMs = 3000;

    /// <summary>How many such passes in a row mean the car is sitting on the loop.</summary>
    private const int ParkedSightingsInARow = 3;

    /// <summary>Each car's last pass outside its own racing (between races, before the green, or before its release), and how many passes in a row have come within ParkedRepeatGapMs of the one before. Kept across starts - see ClearJumpStartWindow.</summary>
    private readonly Dictionary<string, (long LastMs, int InARow)> _sightingsBeforeGreen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cars already logged as sitting on the loop this start, so it is said once.</summary>
    private readonly HashSet<string> _parkedOnLoopLogged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Notes a pass during staging or the countdown, and says whether the car
    /// is sitting on the loop: <see cref="ParkedSightingsInARow"/> passes in a
    /// row, each within <see cref="ParkedRepeatGapMs"/> of the one before.
    /// </summary>
    /// <remarks>
    /// Rehearsal 3 (27 Sep 2026) had both cars parked on the loop through
    /// staging and the countdown, reported every two seconds, and both were
    /// called as jump starts - "Frank jumped the start!" - at their first
    /// pass of the countdown. A car that was already there before the lights
    /// began has not anticipated them; it is on the grid in the wrong place,
    /// which is worth saying, but not as a jump.
    ///
    /// A real jump still reads as one: a car placed on its grid slot across
    /// the loop is seen once or twice, and its crossing in the countdown
    /// comes long after that - so its run starts again at one. Only a car
    /// seen at least twice just before, a couple of seconds apart, is taken
    /// to be sitting there. A car moved ONTO the loop during the countdown
    /// is flagged at its first pass, as before.
    ///
    /// Also asked after the green, for a car whose staggered or hand release
    /// has not come yet - it is still before ITS green. The run carries on
    /// from the countdown's (and from passes between races, which are
    /// counted by <see cref="CountSightingInARow"/>), so a car that has sat
    /// on the loop all along is recognised however the start was begun.
    /// </remarks>
    private bool NoteSightingBeforeGreen(string transponderId, long deviceTimeMs)
    {
        if (string.IsNullOrWhiteSpace(transponderId))
        {
            return false;
        }

        var inARow = CountSightingInARow(transponderId, deviceTimeMs);

        if (inARow < ParkedSightingsInARow)
        {
            return false;
        }

        if (_parkedOnLoopLogged.Add(transponderId))
        {
            Log($"{transponderId} is sitting on the loop - seen {inARow} times in a row, no more than "
                + $"{ParkedRepeatGapMs / 1000}s apart. Not called as a jump start while it stays there, "
                + "but it belongs behind the line.");
        }

        return true;
    }

    /// <summary>
    /// Records a pass in the car's run of sightings and returns how long the
    /// run now is. Says nothing and flags nothing - used on its own between
    /// races, and by <see cref="NoteSightingBeforeGreen"/> before a start.
    /// </summary>
    private int CountSightingInARow(string transponderId, long deviceTimeMs)
    {
        if (string.IsNullOrWhiteSpace(transponderId))
        {
            return 0;
        }

        // A clock that went backwards (a reconnect) starts the run again, and
        // so does any gap longer than a parked car's repeat - which is also
        // why an entry left over from long ago can never count towards a
        // run now.
        var inARow = _sightingsBeforeGreen.TryGetValue(transponderId, out var seen)
                     && deviceTimeMs > seen.LastMs
                     && deviceTimeMs - seen.LastMs <= ParkedRepeatGapMs
            ? seen.InARow + 1
            : 1;

        _sightingsBeforeGreen[transponderId] = (deviceTimeMs, inARow);
        return inARow;
    }

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

    /// <summary>The grid as set for this race - empty when it wasn't known. Read by TimingWatch's host, to expect every car on it.</summary>
    public IReadOnlyList<string> Grid => _grid;

    /// <param name="transponders">The grid, in order.</param>
    /// <param name="renumberOverStarters">True when the grid is only a guess at the field - the last race's finishing order - so start positions are numbered over the cars on it that actually race, not its slots. See RenumberStartPositionsOverStarters.</param>
    public void SetGrid(IEnumerable<string> transponders, bool renumberOverStarters = false)
    {
        _grid.Clear();

        // Once each, ignoring case, so one driver cannot hold two grid slots
        // (and two held rows in a hand-released start).
        _grid.AddRange(transponders
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        _renumberGridOverStarters = renumberOverStarters && _grid.Count > 0;

        // WHY (23 Oct 2026, timing layer, P0): recorded for replay, as it was
        // set - the caller's list may only be readable once.
        RecordCommand(new TimingRecord { Command = "SetGrid", Grid = _grid.ToList(), Value = _renumberGridOverStarters ? 1 : 0 });
    }

    /// <summary>
    /// Whether this race's grid is the last race's finishing order rather
    /// than a stated roster, so its start positions are renumbered over the
    /// cars that actually race. See RenumberStartPositionsOverStarters.
    /// </summary>
    private bool _renumberGridOverStarters;

    /// <summary>
    /// For a grid borrowed from the last race: numbers start positions over
    /// the cars on it that have counted a lap in THIS race, in grid order.
    ///
    /// That field's slots are not this one's. Drivers from it who are not
    /// here left gaps, so the fifth car of a five-car heat was saved as
    /// starting 8th - "up from 8th on the grid", and a Comeback Kid or Pole
    /// Converted awarded off a race it never ran. A car on the grid that has
    /// not raced (never turned up, or still held in a hand-released start)
    /// has no start position at all. Run as each car counts its first lap,
    /// so by the flag every car that raced is numbered.
    /// </summary>
    private void RenumberStartPositionsOverStarters()
    {
        if (!_renumberGridOverStarters)
        {
            return;
        }

        var position = 0;

        foreach (var transponder in _grid)
        {
            if (!_standings.TryGetValue(transponder, out var standing))
            {
                continue;
            }

            standing.StartPosition = standing.Laps > 0 ? ++position : 0;
        }
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

        // WHY (23 Oct 2026, timing layer, P0): recorded for replay, as it was set.
        RecordCommand(new TimingRecord
        {
            Command = "SetReleaseSchedule",
            Offsets = new Dictionary<string, long>(_releaseOffsetsMs, StringComparer.OrdinalIgnoreCase),
        });

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
        // WHY (23 Oct 2026, timing layer, P0): recorded for replay - see Recorder.
        RecordCommand(new TimingRecord { Command = "Release", Transponder = transponderId });

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
        // WHY (23 Oct 2026, timing layer, P0): recorded once, as itself - a
        // replay of each release inside it as well would release twice.
        RecordCommand(new TimingRecord { Command = "ReleaseAll" });

        if (State != RaceState.Racing)
        {
            return;
        }

        _recordingSuppressed++;

        try
        {
            foreach (var transponder in _standings.Keys.ToList())
            {
                if (!IsReleased(transponder))
                {
                    ReleaseDriver(transponder);
                }
            }
        }
        finally
        {
            _recordingSuppressed--;
        }
    }

    /// <summary>This driver's delay after the green, or zero when they have none.</summary>
    public long ReleaseOffsetMsFor(string transponderId) =>
        _releaseOffsetsMs.TryGetValue(transponderId, out var offset) ? offset : 0;

    /// <summary>
    /// Whether this driver is under way yet.
    ///
    /// A hand-released start is the one case where a driver with no schedule
    /// entry is NOT released: nothing says they were let go yet, and treating
    /// silence as "released" would make the whole mode do nothing. Every
    /// other start type releases anyone it has no entry for, so a car that
    /// was never on the grid still races rather than being silently ignored.
    ///
    /// Only a car ON the grid is held, though. A car that is not - someone
    /// who did not run in the race the grid was taken from, with no round
    /// picked - was held as well, and nothing could ever let it go: it got
    /// no row on the board to release it from, and Release All only goes
    /// through the rows there. Its first crossing was called as a jump start
    /// with the class penalty, and every crossing after it was dropped, so
    /// the driver ran the whole race and was missing from the result. Such a
    /// car is timed from the green instead, the same as in every other start
    /// type.
    /// </summary>
    public bool IsReleased(string transponderId)
    {
        if (State != RaceState.Racing || !RaceStartUtc.HasValue)
        {
            return false;
        }

        if (HandReleaseArmed
            && !_releaseOffsetsMs.ContainsKey(transponderId)
            && _grid.Contains(transponderId, StringComparer.OrdinalIgnoreCase))
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

    private void SetState(RaceState state)
    {
        // Anything that moves the session on - a stage from the phone, a
        // countdown or green pressed early, practice, a demo - ends the
        // warm-up there and then, whoever started it.
        if (state is not (RaceState.Idle or RaceState.Finished))
        {
            EndWarmUp($"Warm-up ended - {state.ToString().ToLowerInvariant()}.");
        }

        // When this race was first called to the grid (11 Nov 2026, night
        // pace): the first move into staging or the countdown from rest. A
        // countdown after a stage keeps the stage's time - the grid wait is
        // from when the drivers were called, not from the last press.
        if (state is RaceState.Staging or RaceState.Countdown && State is RaceState.Idle or RaceState.Finished)
        {
            StagedAtUtc = UtcNow;
        }
        else if (state is RaceState.Racing && State is RaceState.Idle or RaceState.Finished)
        {
            // Straight to green: this race was never staged, and the last
            // race's stage is not its.
            StagedAtUtc = null;
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

        // WHY (23 Oct 2026, timing layer, P1): listeners live in the present.
        // A pass is judged at the moment it arrived (see NowMs), but the board,
        // the announcer and the filing that hear about it read the race clock
        // as it is now, exactly as they did before - so the arrival moment is
        // put aside while they run, and put back for the rest of the judging.
        var judging = _judgingAtMs;
        _judgingAtMs = null;

        try
        {
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
        finally
        {
            _judgingAtMs = judging;
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

        // Listeners read the present - see SafeRaise.
        var judging = _judgingAtMs;
        _judgingAtMs = null;

        try
        {
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
        finally
        {
            _judgingAtMs = judging;
        }
    }
}
