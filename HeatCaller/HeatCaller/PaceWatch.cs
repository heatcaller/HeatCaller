namespace HeatCaller;

/// <summary>
/// What kind of record a lap just broke, biggest first.
///
/// The order matters: one lap can be all four at once, and the announcer
/// should say the largest true thing about it. A lap that breaks the
/// layout record is obviously also the driver's personal best, and saying
/// both is how a broadcast starts sounding like a database.
/// </summary>
public enum PaceNoteKind
{
    /// <summary>Faster than this driver has ever gone here before.</summary>
    PersonalBest = 0,

    /// <summary>The fastest lap of this race so far.</summary>
    SessionBest = 1,

    /// <summary>The fastest lap anybody has turned tonight.</summary>
    NightBest = 2,

    /// <summary>The fastest lap ever set on this track layout.</summary>
    LayoutRecord = 3,
}

/// <summary>A lap as it comes in. Seconds, because that is what gets said.</summary>
public readonly record struct LapSeen(string DriverId, double LapSeconds);

/// <summary>
/// A lap worth stopping the commentary for.
/// </summary>
/// <param name="DriverId">Whose lap.</param>
/// <param name="LapSeconds">What they did.</param>
/// <param name="Kind">The biggest true thing about it.</param>
/// <param name="Beaten">What it beat, where that is known.</param>
/// <param name="PreviousHolder">Who held it, where that is known and is somebody else.</param>
public sealed record PaceNote(
    string DriverId,
    double LapSeconds,
    PaceNoteKind Kind,
    double? Beaten,
    string? PreviousHolder);

/// <summary>
/// Watches the laps going by for one worth a word.
///
/// A record falling is the cheapest live-feel line there is: it is rare,
/// it is unarguable, and it needs no judgement from the model at all. It
/// is also the segment that most needs history, because a record is only
/// a story if you can say what it beat and who held it - "fastest lap of
/// the night" on its own is a number, and "that takes two tenths off
/// Dave's mark from the second heat" is commentary.
///
/// WHAT IT WILL NOT SAY
///
///  - Anything in the first few laps of a race. The first lap is by
///    definition the fastest so far, and announcing it is absurd.
///  - A personal best for a driver whose previous best is unknown. Every
///    new driver's first lap would otherwise be a personal best, which is
///    true and worthless.
///  - A record broken by a few thousandths, where the margin is inside
///    the noise of the loop rather than a real improvement.
///  - A given driver's personal best more than once a race, however many
///    times they chip at it.
///
/// History comes in as functions rather than stores, so the whole thing
/// is a table test and the readers behind it can be as slow as they like.
/// </summary>
public sealed class PaceWatch
{
    private readonly Dictionary<string, double> _bestThisSession = new(StringComparer.Ordinal);
    private readonly HashSet<string> _personalBestSaid = new(StringComparer.Ordinal);

    private double _sessionBest = double.MaxValue;
    private string? _sessionBestHolder;
    private int _lapsSeen;

    /// <summary>This driver's best lap before this race (tonight's saved heats included), where it is known. From PersonalBests.</summary>
    public Func<string, double?>? PersonalBestBefore { get; set; }

    /// <summary>The fastest lap turned tonight before this race, and who turned it. From the day's sessions.</summary>
    public Func<(double Seconds, string Holder)?>? NightBest { get; set; }

    /// <summary>The fastest lap ever set on this layout, and who holds it. From the layout's records.</summary>
    public Func<(double Seconds, string Holder)?>? LayoutRecord { get; set; }

    /// <summary>
    /// Whether this driver may hold the layout record - false for a ghost,
    /// a rental or a driver excluded from records, the rule Race Control's
    /// live record check follows. Unset, everybody can (Bug hunt 11).
    /// </summary>
    public Func<string, bool>? CanHoldRecord { get; set; }

    /// <summary>
    /// Whether Race Control would write this lap onto the layout as its new
    /// record: (driver, lap seconds, the record it beats in seconds). False
    /// in a demo or practice run, and for a lap RecordPlausibility doubts.
    /// Unset, every lap that beats the record would (Bug hunt 11b).
    /// </summary>
    public Func<string, double, double, bool>? RecordWouldStand { get; set; }

    private (double Seconds, string Holder)? _layoutAtGreen;

    // The laps this race Race Control would have written onto the layout,
    // per driver - only kept while RecordWouldStand is wired. See
    // RecordEligibleBest.
    private readonly Dictionary<string, List<double>> _recordLapsTaken = new(StringComparer.Ordinal);

    /// <summary>
    /// How much faster a lap has to be before it counts as beating
    /// something. A hundredth: inside that the loop's own timing is as
    /// likely an explanation as the driver.
    /// </summary>
    public double MeaningfulMargin { get; set; } = 0.01;

    /// <summary>How many laps of a race go by before "fastest so far" means anything.</summary>
    public int WarmUpLaps { get; set; } = 3;

    /// <summary>A sanity bound - a lap this long is a car that stopped, not a lap.</summary>
    public double LongestCredibleLap { get; set; } = 600;

    /// <summary>How many laps this race has been seen. For the console and the tests.</summary>
    public int LapsSeen => _lapsSeen;

    /// <summary>
    /// A lap has come in. Returns the one thing worth saying about it, or
    /// null - which is almost always.
    /// </summary>
    public PaceNote? Saw(LapSeen lap)
    {
        if (string.IsNullOrWhiteSpace(lap.DriverId) || lap.LapSeconds <= 0 || lap.LapSeconds > LongestCredibleLap)
        {
            return null;
        }

        _lapsSeen++;

        // Everything is judged against the state BEFORE this lap, then the
        // state is moved on. Doing it the other way round makes every lap
        // its own session best.
        var previousSessionBest = _sessionBest;
        var previousSessionHolder = _sessionBestHolder;

        _bestThisSession.TryGetValue(lap.DriverId, out var theirBestSoFar);

        var note = Judge(lap, previousSessionBest, previousSessionHolder, theirBestSoFar);

        if (lap.LapSeconds < _sessionBest)
        {
            _sessionBest = lap.LapSeconds;
            _sessionBestHolder = lap.DriverId;
        }

        if (theirBestSoFar <= 0 || lap.LapSeconds < theirBestSoFar)
        {
            _bestThisSession[lap.DriverId] = lap.LapSeconds;
        }

        if (note is { Kind: PaceNoteKind.PersonalBest })
        {
            _personalBestSaid.Add(lap.DriverId);
        }

        return note;
    }

    private PaceNote? Judge(LapSeen lap, double sessionBest, string? sessionHolder, double theirBestSoFar)
    {
        // The layout's record is the biggest thing a lap can be, and the
        // only one that stands on its own from the very first lap of a
        // race - it is a record for the whole track, not a position in
        // this one.
        //
        // WHY (20 Oct 2026): the night's best and the layout record are read
        // from SAVED races only (PaceHistory.NightBestFor, LayoutRecordFor),
        // so they never include the race being run. Once one car had beaten
        // the old mark, every later lap under the OLD mark - but slower than
        // the new one - was still called "the fastest lap of the night",
        // taken from the old holder. Each is now judged against whichever is
        // faster, the stored figure or this race's own best so far; a lap
        // that beats only the old mark falls through to the checks below.
        //
        // WHY (2026-11-05, Bug hunt 11): the layout record is NOT from saved
        // races. Race Control writes a new record onto the layout the moment
        // the lap crosses the line, before this watch is fed it on the
        // announcer's next tick - so the record read here was already the
        // lap itself, nothing a real record beat was ever called, and the
        // only laps called "the fastest ever on this layout" were the ones
        // Race Control refused (a rental, a driver excluded from records).
        // So the record is now the one standing at the green (see
        // LayoutAtGreen), this race's own best is counted only from drivers
        // who can hold it, and a driver who cannot hold it is never told
        // they broke it.
        //
        // WHY (6 Nov 2026, Bug hunt 11b review): CanHoldRecord was only half
        // of Race Control's rule. CheckAndAnnounceTrackRecord also refuses
        // every lap of a demo or practice run, and a lap it cannot believe
        // ("All-time record NOT taken" in the log). So a misread 7.2 s under
        // a 9.8 s record, never written to the layout, was still called "the
        // fastest lap ever set on this layout", and so was every demo lap
        // that beat the real record. Now a lap is called a record only when
        // Race Control would take it (RecordWouldStand, wired to
        // RecordPlausibility - the one rule both use), judged here against
        // the record as it stood when the lap was driven, because the layout
        // has already moved on by the time the announcer reads the lap.
        if (LayoutAtGreen() is { } layout && CanHoldRecord?.Invoke(lap.DriverId) != false)
        {
            var (eligibleBest, eligibleHolder) = RecordEligibleBest(sessionBest, sessionHolder);
            var (mark, holder) = Standing(layout.Seconds, layout.Holder, eligibleBest, eligibleHolder);

            var wouldStand = mark > 0
                && lap.LapSeconds < mark
                && RecordWouldStand?.Invoke(lap.DriverId, lap.LapSeconds, mark) != false;

            if (wouldStand && RecordWouldStand is not null)
            {
                if (!_recordLapsTaken.TryGetValue(lap.DriverId, out var taken))
                {
                    _recordLapsTaken[lap.DriverId] = taken = new List<double>();
                }

                taken.Add(lap.LapSeconds);
            }

            if (wouldStand && Beats(lap.LapSeconds, mark))
            {
                return new PaceNote(lap.DriverId, lap.LapSeconds, PaceNoteKind.LayoutRecord, mark, holder);
            }
        }

        if (NightBest?.Invoke() is { } night)
        {
            var (mark, holder) = Standing(night.Seconds, night.Holder, sessionBest, sessionHolder);

            if (Beats(lap.LapSeconds, mark))
            {
                return new PaceNote(lap.DriverId, lap.LapSeconds, PaceNoteKind.NightBest, mark, holder);
            }
        }

        // "Fastest of the race so far" needs a race to be faster than.
        if (_lapsSeen > WarmUpLaps && sessionBest < double.MaxValue && Beats(lap.LapSeconds, sessionBest))
        {
            return new PaceNote(lap.DriverId, lap.LapSeconds, PaceNoteKind.SessionBest, sessionBest, sessionHolder);
        }

        if (_personalBestSaid.Contains(lap.DriverId))
        {
            return null;
        }

        // A personal best is only a story against a known previous best.
        // Their own laps earlier in this race count too: a driver who has
        // never raced here has no history, and their fourth lap beating
        // their third is still them going quicker than they ever have.
        var before = PersonalBestBefore?.Invoke(lap.DriverId) is { } p && p > 0 ? p : 0;

        var benchmark = (before, theirBestSoFar) switch
        {
            ( > 0, > 0) => Math.Min(before, theirBestSoFar),
            ( > 0, _) => before,

            // No history at all: their best lap of this race is the best
            // they have ever done here, and beating it is a real personal
            // best. Their FIRST lap is not, which is what the zero case
            // below falls through to.
            (_, > 0) => theirBestSoFar,
            _ => 0,
        };

        if (benchmark > 0 && Beats(lap.LapSeconds, benchmark))
        {
            return new PaceNote(lap.DriverId, lap.LapSeconds, PaceNoteKind.PersonalBest, benchmark, lap.DriverId);
        }

        return null;
    }

    private bool Beats(double lap, double mark) => mark > 0 && lap < mark - MeaningfulMargin;

    /// <summary>
    /// The layout record as it stood at the green (Bug hunt 11,
    /// 2026-11-05): read at Reset, or at the first lap where there was
    /// nothing to read then (no layout wired yet, or none with a record).
    /// Kept for the race once found, because the store behind it is
    /// rewritten by this race's own record laps.
    /// </summary>
    private (double Seconds, string Holder)? LayoutAtGreen() =>
        _layoutAtGreen ??= LayoutRecord?.Invoke();

    /// <summary>
    /// This race's best lap so far among drivers who can hold the layout
    /// record - every driver, when CanHoldRecord is not wired.
    /// </summary>
    private (double Seconds, string? Holder) RecordEligibleBest(double sessionBest, string? sessionHolder)
    {
        // With RecordWouldStand wired, only the laps Race Control would have
        // taken (Bug hunt 11b, 6 Nov 2026): a refused phantom is not the
        // record, and must not become the bar a real record has to beat.
        if (RecordWouldStand is not null)
        {
            var bestTaken = double.MaxValue;
            string? takenBy = null;

            foreach (var (driver, laps) in _recordLapsTaken)
            {
                foreach (var seconds in laps)
                {
                    if (seconds > 0 && seconds < bestTaken)
                    {
                        bestTaken = seconds;
                        takenBy = driver;
                    }
                }
            }

            return (bestTaken, takenBy);
        }

        if (CanHoldRecord is not { } canHold)
        {
            return (sessionBest, sessionHolder);
        }

        var best = double.MaxValue;
        string? holder = null;

        foreach (var (driver, seconds) in _bestThisSession)
        {
            if (seconds > 0 && seconds < best && canHold(driver))
            {
                best = seconds;
                holder = driver;
            }
        }

        return (best, holder);
    }

    /// <summary>
    /// The figure a stored record really stands at now: the stored one, or
    /// this race's own best where that is already faster. A stored figure
    /// of zero or less means "none" (Beats ignores it), so this race's best
    /// takes over only when there is a real stored mark it has beaten.
    /// sessionBest is double.MaxValue before the race's first lap, which
    /// leaves the stored figure as it is.
    /// </summary>
    private static (double Seconds, string Holder) Standing(
        double storedSeconds, string storedHolder, double sessionBest, string? sessionHolder) =>
        storedSeconds > 0 && sessionBest < storedSeconds
            ? (sessionBest, sessionHolder ?? string.Empty)
            : (storedSeconds, storedHolder);

    /// <summary>
    /// A lap of this driver's was taken off by hand (bug hunt 10). Their
    /// session best is rebuilt from the laps that are left, and the race's
    /// fastest lap from every driver's best, so a phantom split that was
    /// taken off no longer stands as the bar for the rest of the race.
    /// Whether their personal best has been said is left alone: it was
    /// said, and saying it twice is the thing that rule prevents.
    /// </summary>
    // WHY (23 Oct 2026, bug hunt 10): FeedLaps only ever added laps, so a
    // double read folded away by -1 lap stayed the session best.
    internal void Forget(string driverId, IEnumerable<double> survivingLapSeconds)
    {
        if (string.IsNullOrWhiteSpace(driverId))
        {
            return;
        }

        _bestThisSession.Remove(driverId);

        var surviving = survivingLapSeconds
            .Where(s => s > 0 && s <= LongestCredibleLap)
            .ToList();

        var best = surviving.DefaultIfEmpty(0).Min();

        // A record lap taken off stops being the record this race holds.
        if (_recordLapsTaken.TryGetValue(driverId, out var taken))
        {
            taken.RemoveAll(t => !surviving.Any(s => Math.Abs(s - t) < 1e-9));
        }

        if (best > 0)
        {
            _bestThisSession[driverId] = best;
        }

        _sessionBest = double.MaxValue;
        _sessionBestHolder = null;

        foreach (var (driver, seconds) in _bestThisSession)
        {
            if (seconds > 0 && seconds < _sessionBest)
            {
                _sessionBest = seconds;
                _sessionBestHolder = driver;
            }
        }
    }

    /// <summary>
    /// A new race. The session's own bests go; what the night and the
    /// layout hold does not, because those belong to the stores behind
    /// them. The night's best is read fresh every time; the layout record
    /// is read here, once, as it stands at the green (Bug hunt 11).
    /// </summary>
    public void Reset()
    {
        _bestThisSession.Clear();
        _personalBestSaid.Clear();
        _recordLapsTaken.Clear();
        _sessionBest = double.MaxValue;
        _sessionBestHolder = null;
        _lapsSeen = 0;

        // The record as it stands now, at the green - see LayoutAtGreen.
        _layoutAtGreen = LayoutRecord?.Invoke();
    }
}

/// <summary>
/// Whether a lap that beats a layout's all-time record is one Race Control
/// will write onto the layout - the one rule its record
/// (RaceControlControl.CheckAndAnnounceTrackRecord) and the announcer's
/// PaceWatch both follow.
///
/// WHY (6 Nov 2026, Bug hunt 11b review): the plausibility questions lived
/// inside Race Control as IsPlausibleRecord, so the announcer could not ask
/// them, and called laps "the fastest ever on this layout" that the layout
/// never took. Moved here unchanged, so the two cannot drift apart.
///
/// The two possible mistakes do not cost the same. Refusing a real record
/// costs one lap: the driver is on the pace and will do it again. Accepting
/// a phantom writes a time nobody drove into the club's permanent record,
/// where it becomes a ghost pace source, an achievement and an announcement
/// - and nothing in the app ever questions it again. The only way back is
/// to clear the record by hand on the track layout.
///
/// A transponder left sitting under the bridge retriggers, and any two of
/// those reads far enough apart to clear the minimum lap time are counted
/// as a lap. That is what the minimum is for, and the operator sets it. But
/// a lap wildly off the pace of everything around it is not a record,
/// whatever the decoder said.
///
/// Three questions, any one of which is enough to leave the record alone.
/// They are deliberately loose - each is asking whether the time is
/// possible, not whether it is impressive.
/// </summary>
public static class RecordPlausibility
{
    /// <summary>
    /// Whether Race Control would write this lap onto the layout: never in a
    /// demo or practice run (simulated lap times are fake), otherwise only
    /// when <see cref="Doubt(RaceTiming, string, long, long)"/> finds
    /// nothing. Who may hold a record at all is asked separately
    /// (AchievementEvaluator.CanHoldTrackRecord).
    /// </summary>
    public static bool WouldTake(RaceTiming race, string transponder, long lapMs, long recordLapMs) =>
        !race.IsDemoMode && !race.IsPracticeMode && Doubt(race, transponder, lapMs, recordLapMs) is null;

    /// <summary>
    /// Why this lap is not believable as a record, judged against the race
    /// it was driven in, or null when it is. recordLapMs is the record it
    /// beats (0 for none).
    /// </summary>
    public static string? Doubt(RaceTiming race, string transponder, long lapMs, long recordLapMs)
    {
        var own = race.GetOrderedStandings()
            .FirstOrDefault(s => string.Equals(s.TransponderId, transponder, StringComparison.OrdinalIgnoreCase));

        // Recovery laps are left out too (RaceGaps.PaceLaps): one spans a
        // whole stoppage, and a few of them would lift the median until an
        // ordinary lap looked like a phantom.
        return Doubt(lapMs, race.MinLapTimeMs, recordLapMs, own is null ? null : RaceGaps.PaceLaps(own));
    }

    /// <summary>
    /// The rule on plain numbers: why a lap of lapMs beating recordLapMs
    /// (0 for none) is not believable, or null when it is.
    /// driverPaceLapsMs are the driver's pace laps this race.
    /// </summary>
    public static string? Doubt(long lapMs, int minLapTimeMs, long recordLapMs, IEnumerable<long>? driverPaceLapsMs)
    {
        // 1. Sitting on top of the minimum is the shape of a retrigger, not
        //    of a lap. The operator sets the minimum just under a real lap
        //    for the class, so a tenth above it is inside the very noise
        //    that setting exists to describe.
        if (minLapTimeMs > 0 && lapMs < minLapTimeMs * 1.1)
        {
            return $"it is within a tenth of the {minLapTimeMs / 1000.0:0.000}s minimum lap time, which is what a retriggering transponder looks like";
        }

        // 2. Against the record it claims to beat. Track records fall by
        //    hundredths and occasionally by tenths. A quarter of a lap is
        //    not an improvement, it is a different event.
        if (recordLapMs > 0 && lapMs < recordLapMs * 0.75)
        {
            return $"it beats the standing record of {recordLapMs / 1000.0:0.000}s by more than a quarter";
        }

        // 3. Against the driver's own laps - which is what catches a single
        //    bad read on a layout that has no record yet. A median rather
        //    than a best, so one phantom cannot drag down the figure it is
        //    being judged against, and only once there are enough laps for
        //    a median to mean anything. (PaceLaps leaves out index 0, the
        //    start lap, which is not a lap of the circuit.)
        var theirLaps = driverPaceLapsMs?.Where(t => t > 0).OrderBy(t => t).ToList();

        if (theirLaps is { Count: >= 3 })
        {
            var median = theirLaps[theirLaps.Count / 2];

            if (lapMs < median * 0.5)
            {
                return $"it is less than half this driver's typical {median / 1000.0:0.000}s lap this race";
            }
        }

        return null;
    }
}
