namespace HeatCaller;

/// <summary>
/// Where everyone would qualify if the heat on track ended right now.
///
/// A race director watching a qualifier can see who is leading THIS heat.
/// What they cannot see - and what every driver in the pits is asking - is
/// what it means: a driver winning their heat may still be eighth overall,
/// and a driver running fourth may be about to take pole because this is
/// their drop-round replacement. The answer is a whole calculation away,
/// and until now it only existed after the heat was recorded.
///
/// So this asks QualifyingCalculator the same question it always answers,
/// twice: once from the recorded heats alone, and once with the running
/// heat folded in as though it had just finished. The difference between
/// the two orders is the interesting part - it is how many places this
/// heat has moved each driver, so far.
///
/// UI THREAD ONLY. It reads RoundDefinitionStore, SessionHistoryStore and
/// RaceControlSession, none of which are safe to touch from anywhere else.
/// That is why the web pages do not get this column: they are served on
/// thread-pool threads.
/// </summary>
public sealed class QualifyingProjector
{
    /// <summary>One driver's place in the projected order, for a leaderboard cell.</summary>
    /// <param name="Position">Projected overall qualifying position, 1-based.</param>
    /// <param name="Change">Places gained (positive) or lost (negative) by the heat so far.</param>
    /// <param name="IsNew">True when this heat is the driver's first, so there is no previous place to compare with.</param>
    public sealed record Slot(int Position, int Change, bool IsNew);

    /// <summary>One line of the projected order, for the panel.</summary>
    public sealed record Row(
        int Position,
        string Transponder,
        string DriverName,
        int Laps,
        long ElapsedMs,
        long BestConsecutiveMs,
        int HeatsRun,
        int HeatsCounted,
        bool InLiveHeat,
        int Change,
        bool IsNew);

    /// <summary>The whole picture, or null when nothing qualifying-shaped is running.</summary>
    public sealed record Projection(
        string ClassName,
        string RoundLabel,
        QualifyingMethod Method,
        int ConsecutiveLapCount,
        int CountBestRounds,
        IReadOnlyList<Row> Rows,
        IReadOnlyDictionary<string, Slot> Slots);

    private readonly RaceControlSession _race;
    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _raceClasses;
    private readonly SessionHistoryStore _history;
    private readonly RoundDefinitionStore? _rounds;
    private readonly RaceEventStore? _events;
    private readonly Func<RoundDefinition?>? _activeRound;

    // --- Cache ---
    //
    // Current() is called from the leaderboard refresh, which runs several
    // times a second, and each call ranks the whole class twice. The answer
    // can only change when a lap is counted, a penalty is applied or the
    // session changes, so the cache key is exactly those things. Time is
    // deliberately NOT part of it: a clock-based cache would recompute
    // during a red flag when nothing is happening and still hand back a
    // stale answer for a moment after a crossing.
    private string _cacheKey = string.Empty;
    private Projection? _cached;

    public QualifyingProjector(
        RaceControlSession race,
        ProfileStore profiles,
        RaceClassStore raceClasses,
        SessionHistoryStore history,
        RoundDefinitionStore? rounds,
        RaceEventStore? events,
        Func<RoundDefinition?>? activeRound)
    {
        _race = race;
        _profiles = profiles;
        _raceClasses = raceClasses;
        _history = history;
        _rounds = rounds;
        _events = events;
        _activeRound = activeRound;
    }

    /// <summary>
    /// The projection as things stand, or null when this is not a qualifier
    /// - a main, a practice run, or a session that was started without a
    /// round attached at all.
    ///
    /// Null rather than an empty projection, so every caller has one
    /// question to ask: is there a projection to show. A column that shows
    /// a dash and a panel that hides itself both come from the same answer.
    /// </summary>
    public Projection? Current()
    {
        if (_rounds is null || _activeRound is null)
        {
            return null;
        }

        var round = _activeRound();

        if (round is null || round.Kind != RoundKind.Qualifier || string.IsNullOrWhiteSpace(round.ClassName))
        {
            return null;
        }

        var standings = _race.GetOrderedStandings();

        var key = BuildCacheKey(round, standings);

        if (key == _cacheKey && _cached is not null)
        {
            return _cached;
        }

        var raceClass = _raceClasses.All
            .FirstOrDefault(c => string.Equals(c.Name, round.ClassName, StringComparison.OrdinalIgnoreCase));

        // The same set the Program tab seeds from (RaceProgramControl.
        // QualifyingRoundFilter): a real race day's rounds, or every round
        // when the legacy holder or nothing is active. It used to take the
        // legacy holder's rounds as a filter, so the projection ranked from
        // a different set of heats than the seeding it forecasts.
        var filter = _events?.Active is { IsLegacy: false } active ? active.RoundIds : null;

        var live = new QualifyingCalculator.LiveHeat(
            round.Id,
            round.RoundLabel,
            DateTime.UtcNow,
            standings.Select(s => new QualifyingCalculator.LiveResult(
                s.TransponderId,
                DisplayName(s.TransponderId),
                _profiles.Get(s.TransponderId)?.CarModel ?? string.Empty,
                s.Laps,
                s.ElapsedMs,
                s.FastestLapMs,
                s.LapTimesMs)).ToList());

        // Without the running heat: where qualifying stood when this one
        // went out. This is the baseline every arrow is measured from.
        var before = QualifyingCalculator
            .Compute(round.ClassName, _rounds, _history, _profiles, raceClass, filter)
            .Select((e, i) => (e.Transponder, Position: i + 1))
            .ToDictionary(x => x.Transponder, x => x.Position, StringComparer.OrdinalIgnoreCase);

        var after = QualifyingCalculator
            .Compute(round.ClassName, _rounds, _history, _profiles, raceClass, filter, live);

        var onTrack = standings
            .Select(s => s.TransponderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<Row>(after.Count);
        var slots = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < after.Count; i++)
        {
            var entry = after[i];
            var position = i + 1;

            var known = before.TryGetValue(entry.Transponder, out var previous);

            // Positive means places GAINED, which is the direction an arrow
            // pointing up has to mean. Positions count downwards, so the
            // subtraction is the way round that looks wrong and is not.
            var change = known ? previous - position : 0;

            rows.Add(new Row(
                position,
                entry.Transponder,
                entry.DriverName,
                entry.BestLaps,
                entry.BestElapsedMs,
                entry.BestConsecutiveMs,
                entry.HeatsRun,
                entry.HeatsCounted,
                onTrack.Contains(entry.Transponder),
                change,
                !known));

            if (onTrack.Contains(entry.Transponder))
            {
                slots[entry.Transponder] = new Slot(position, change, !known);
            }
        }

        _cached = new Projection(
            round.ClassName,
            round.RoundLabel,
            raceClass?.QualifyingMethod ?? QualifyingMethod.TotalLaps,
            Math.Max(1, raceClass?.ConsecutiveLapCount ?? 3),
            Math.Max(1, raceClass?.CountBestRounds ?? 1),
            rows,
            slots);

        _cacheKey = key;

        return _cached;
    }

    private string DisplayName(string transponder)
    {
        var profile = _profiles.Get(transponder);

        return string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;
    }

    /// <summary>
    /// Everything that can change the answer, and nothing that cannot.
    ///
    /// Laps covers a crossing; elapsed covers a penalty or a lap taken back
    /// by hand; the generation covers a reset; the round covers moving on
    /// to the next heat. The field size is in there too, for a transponder
    /// appearing part way through.
    /// </summary>
    private string BuildCacheKey(RoundDefinition round, IReadOnlyList<RacerStanding> standings)
    {
        long laps = 0;
        long elapsed = 0;

        foreach (var s in standings)
        {
            laps += s.Laps;
            elapsed += s.ElapsedMs;
        }

        return $"{round.Id}|{_race.SessionGeneration}|{standings.Count}|{laps}|{elapsed}";
    }
}
