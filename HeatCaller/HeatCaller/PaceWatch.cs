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

    private (double Seconds, string Holder)? _layoutAtGreen;

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
        if (LayoutAtGreen() is { } layout && CanHoldRecord?.Invoke(lap.DriverId) != false)
        {
            var (eligibleBest, eligibleHolder) = RecordEligibleBest(sessionBest, sessionHolder);
            var (mark, holder) = Standing(layout.Seconds, layout.Holder, eligibleBest, eligibleHolder);

            if (Beats(lap.LapSeconds, mark))
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

        var best = survivingLapSeconds
            .Where(s => s > 0 && s <= LongestCredibleLap)
            .DefaultIfEmpty(0)
            .Min();

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
        _sessionBest = double.MaxValue;
        _sessionBestHolder = null;
        _lapsSeen = 0;

        // The record as it stands now, at the green - see LayoutAtGreen.
        _layoutAtGreen = LayoutRecord?.Invoke();
    }
}
