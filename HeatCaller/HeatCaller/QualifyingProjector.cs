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

        // The same set the Program tab seeds from (RaceProgramControl.
        // QualifyingRoundFilter, which is RaceEventStore.QualifyingScope):
        // the open race day's rounds, and NONE when no real race day is open
        // - the legacy holder included. This used to be "every round ever
        // recorded" when no day was open, which is what the Program tab
        // itself did before the 25 Sep audit. After End Race Day the Round
        // box still offers every unrun qualifier, so a heat run then was
        // ranked against months of old heats, and the leaderboard column,
        // the Big Screen and this window showed places like P14 while the
        // Program tab said no race day is open.
        //
        // With nothing to count there is no projection to show - the same
        // answer the Program tab gives - so this returns null rather than
        // ranking the live heat against itself, which would only repeat the
        // leaderboard with every driver marked new. It is checked before the
        // cache on purpose: the cache key does not know about race days, so
        // ending the day part way through a heat would otherwise keep
        // handing back the projection worked out while it was still open.
        //
        // No event store at all (a projector built by a tool or a test, never
        // by Race Control) has no notion of race days, so it keeps ranking
        // every recorded round, as it always has.
        IReadOnlyCollection<string>? filter = null;

        if (_events is not null)
        {
            var scope = _events.QualifyingScope();

            if (scope.Count == 0)
            {
                return null;
            }

            filter = scope;
        }

        var standings = _race.GetOrderedStandings();

        var key = BuildCacheKey(round, standings);

        if (key == _cacheKey && _cached is not null)
        {
            return _cached;
        }

        var raceClass = _raceClasses.All
            .FirstOrDefault(c => string.Equals(c.Name, round.ClassName, StringComparison.OrdinalIgnoreCase));

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

        // Keyed by the driver, the way the calculator files them: a car on
        // its owner's ALTERNATE transponder, timed as a row of its own, is
        // ranked under the owner's main transponder. Keyed by the raw row
        // id, that driver was never found on track - no "in this heat" mark
        // and no projected place beside their row on the leaderboard. Each
        // driver keeps the row ids they are on the board under, because the
        // leaderboard looks a slot up by the row's own id.
        var onTrack = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in standings)
        {
            var driverKey = _profiles.PrimaryForAlternate(s.TransponderId) ?? s.TransponderId;

            if (!onTrack.TryGetValue(driverKey, out var rowIds))
            {
                rowIds = new List<string>();
                onTrack[driverKey] = rowIds;
            }

            rowIds.Add(s.TransponderId);
        }

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
                onTrack.ContainsKey(entry.Transponder),
                change,
                !known));

            if (onTrack.TryGetValue(entry.Transponder, out var rowIdsOnTrack))
            {
                var slot = new Slot(position, change, !known);
                slots[entry.Transponder] = slot;

                // And under every row the driver is on the board as.
                foreach (var rowId in rowIdsOnTrack)
                {
                    slots[rowId] = slot;
                }
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
    ///
    /// 17 Oct 2026, Bug Hunt 8, A26-3: the class's qualifying rules are in
    /// it too, so changing the method, the consecutive-lap count or how
    /// many rounds count redraws the projection now rather than at the
    /// next crossing, which under a red can be minutes away.
    /// </summary>
    private string BuildCacheKey(RoundDefinition round, IReadOnlyList<RacerStanding> standings)
    {
        var raceClass = _raceClasses.All
            .FirstOrDefault(c => string.Equals(c.Name, round.ClassName, StringComparison.OrdinalIgnoreCase));

        long laps = 0;
        long elapsed = 0;

        foreach (var s in standings)
        {
            laps += s.Laps;
            elapsed += s.ElapsedMs;
        }

        return $"{round.Id}|{_race.SessionGeneration}|{standings.Count}|{laps}|{elapsed}"
            + $"|{raceClass?.QualifyingMethod}|{raceClass?.ConsecutiveLapCount}|{raceClass?.CountBestRounds}"
            // 17 Oct 2026, Bug Hunt 8, A26-3: an Edit Result DQ or a Delete
            // during a live qualifier redraws the projection at once.
            + $"|{_history.Revision}";
    }
}
