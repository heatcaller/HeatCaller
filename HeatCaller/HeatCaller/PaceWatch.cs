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

    /// <summary>This driver's best lap before tonight, where it is known. From PersonalBests.</summary>
    public Func<string, double?>? PersonalBestBefore { get; set; }

    /// <summary>The fastest lap turned tonight before this race, and who turned it. From the day's sessions.</summary>
    public Func<(double Seconds, string Holder)?>? NightBest { get; set; }

    /// <summary>The fastest lap ever set on this layout, and who holds it. From the layout's records.</summary>
    public Func<(double Seconds, string Holder)?>? LayoutRecord { get; set; }

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
        if (LayoutRecord?.Invoke() is { } layout && Beats(lap.LapSeconds, layout.Seconds))
        {
            return new PaceNote(lap.DriverId, lap.LapSeconds, PaceNoteKind.LayoutRecord, layout.Seconds, layout.Holder);
        }

        if (NightBest?.Invoke() is { } night && Beats(lap.LapSeconds, night.Seconds))
        {
            return new PaceNote(lap.DriverId, lap.LapSeconds, PaceNoteKind.NightBest, night.Seconds, night.Holder);
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
    /// A new race. The session's own bests go; what the night and the
    /// layout hold does not, because those are read fresh every time and
    /// belong to the stores behind them.
    /// </summary>
    public void Reset()
    {
        _bestThisSession.Clear();
        _personalBestSaid.Clear();
        _sessionBest = double.MaxValue;
        _sessionBestHolder = null;
        _lapsSeen = 0;
    }
}
