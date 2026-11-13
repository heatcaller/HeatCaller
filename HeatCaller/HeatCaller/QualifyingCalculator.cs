namespace HeatCaller;

/// <summary>
/// Ranks drivers across a class's recorded Qualifier rounds, to seed mains.
///
/// Two things are configured per class and combine freely:
///
///  - HOW a single heat is scored:
///      TotalLaps           - most laps, then lowest elapsed time
///      BestConsecutiveLaps - quickest unbroken run of N laps
///      RoundPoints         - (11 Nov 2026) not per heat but per ROUND:
///                            place points across all of a round's heats,
///                            lowest total wins - see RankByPoints
///
///  - HOW MANY heats count (CountBestRounds):
///      1        - a driver's single best heat decides it
///      2 or more - their best N heats are totalled and the rest dropped
///
/// Dropping the worst rounds is the usual way clubs stop one bad heat
/// ruining a day. Running MORE heats can therefore only help a driver,
/// never hurt them, which is the property that makes the whole thing fair
/// when people arrive late or miss a round.
/// </summary>
public static class QualifyingCalculator
{
    /// <summary>
    /// One driver's score in the heat that is running RIGHT NOW, handed in
    /// from outside because this class has no idea a race is in progress
    /// and should not learn.
    /// </summary>
    public sealed record LiveResult(
        string Transponder,
        string DriverName,
        string CarModel,
        int Laps,
        long ElapsedMs,
        long FastLapMs,
        IReadOnlyList<long> LapTimesMs,
        // WHY (20 Oct 2026): the penalty race control has given this driver
        // so far (jump start, penalty buttons, warning time) - the standing's
        // PenaltyMs, already inside ElapsedMs. The lap times never carry it,
        // so without this the best-consecutive-laps projection ranked a
        // penalised driver as if nothing had been given. Optional so a
        // caller that has no penalty to hand still compiles and means zero.
        long PenaltyMs = 0);

    /// <summary>
    /// The running heat, folded in as though it had just been recorded.
    ///
    /// RoundId is carried so that a heat being RE-run replaces its own
    /// recorded result rather than being counted twice - which would hand
    /// the driver a free extra heat under a best-N-of-M rule.
    /// </summary>
    public sealed record LiveHeat(
        string RoundId,
        string RoundLabel,
        DateTime StartedAtUtc,
        IReadOnlyList<LiveResult> Results);

    /// <summary>One driver's result in one heat, before any aggregation.</summary>
    private sealed record HeatScore(
        string RoundKey,
        string RoundLabel,
        string? SessionId,
        DateTime StartedAtUtc,
        int Laps,
        long ElapsedMs,
        long FastLapMs,
        long ConsecutiveMs,
        int ConsecutiveStartLap,
        // WHY (23 Oct 2026, bug hunt 10): the penalty share of ConsecutiveMs,
        // so "where these numbers came from" can tell laps from penalties.
        long ConsecutivePenaltyMs = 0,
        // WHY (11 Nov 2026, Phase 5.1): which qualifying ROUND the heat is
        // part of, and what to call it, for points qualifying - which ranks
        // the drivers of every heat of one round against each other. See
        // RoundGroupOf.
        string RoundGroup = "",
        string RoundGroupName = "");

    public sealed record Entry(
        string Transponder,
        string DriverName,
        string CarModel,
        int BestLaps,
        long BestElapsedMs,
        long BestFastLapMs,
        int HeatsRun,
        long BestConsecutiveMs = 0,
        int ConsecutiveStartLap = 0,
        string SourceRoundLabel = "",
        string? SourceSessionId = null,
        DateTime? SourceSessionStartedUtc = null,
        /// <summary>How many heats were totalled - less than HeatsRun when some were dropped.</summary>
        int HeatsCounted = 1,
        /// <summary>
        /// The heats that did NOT count, with their scores. Showing only
        /// what counted leaves the drop invisible, which makes the result
        /// impossible to check and easy to mistrust.
        /// </summary>
        string DroppedSummary = "",
        /// <summary>
        /// How many of the counted heats had a valid consecutive window -
        /// the heats <see cref="BestConsecutiveMs"/> is actually a total of.
        /// Can be fewer than HeatsCounted: best 2 of 3 still counts a heat
        /// the driver crashed out of when they only ran two.
        /// </summary>
        int WindowsCounted = 0,
        /// <summary>
        /// The penalties inside <see cref="BestConsecutiveMs"/> when it is one
        /// heat's window (bug hunt 10), so the laps can be checked on their own.
        /// Zero when several heats are totalled.
        /// </summary>
        long ConsecutivePenaltyMs = 0,
        /// <summary>
        /// Points qualifying (11 Nov 2026, Phase 5.1): the counted rounds'
        /// points, totalled - lowest wins. Zero under the other methods.
        /// </summary>
        int Points = 0,
        /// <summary>How many rounds <see cref="Points"/> is a total of (the class's best N).</summary>
        int PointsRoundsCounted = 0,
        /// <summary>How many qualifying rounds the class has scored so far - the "of 3" in "best 2 of 3".</summary>
        int PointsRoundsRun = 0,
        /// <summary>
        /// Every round's finish and points, in running order - "Q1 1st (1 pt),
        /// Q2 DNS (9 pts), Q3 2nd (2 pts)" - so a points total can be checked
        /// against the heats it came from, the way DroppedSummary lets a
        /// dropped heat be checked.
        /// </summary>
        string PointsDetail = "",
        /// <summary>The driver's best finish in any one round - the first tie-breaker. Zero when never ranked.</summary>
        int BestRoundFinish = 0,
        /// <summary>
        /// The result the club's marshal rule took away (12 Nov 2026, setup
        /// plan step 8): "Lost Q2 Heat 1 (14 laps) - marshal no-show, Q3
        /// Heat 2 (Turn 1)". Empty for nearly everybody.
        /// </summary>
        string Forfeit = "")
    {
        /// <summary>
        /// A points total in words: "6 pts (best 2 of 3)", "9 pts (3 rounds)",
        /// or "1 pt (Q1)" after a single round. The standings, the mains
        /// preview and the relay all use it, so all three say it the same way.
        /// </summary>
        public string PointsWords()
        {
            var total = Points == 1 ? "1 pt" : $"{Points} pts";

            if (PointsRoundsRun <= 1)
            {
                return string.IsNullOrWhiteSpace(SourceRoundLabel) ? total : $"{total} ({SourceRoundLabel})";
            }

            return PointsRoundsCounted < PointsRoundsRun
                ? $"{total} (best {PointsRoundsCounted} of {PointsRoundsRun})"
                : $"{total} ({PointsRoundsRun} rounds)";
        }

        /// <summary>Average lap within the counted consecutive run(s).</summary>
        public long ConsecutiveAverageMs(int lapCount)
        {
            // Divided by the windows actually in the total, not the heats
            // counted - a heat with no window adds nothing to the total, so
            // counting its laps in the divisor understated the average.
            var laps = lapCount * Math.Max(1, WindowsCounted > 0 ? WindowsCounted : HeatsCounted);
            return BestConsecutiveMs > 0 && laps > 0 ? BestConsecutiveMs / laps : 0;
        }
    }

    /// <summary>
    /// Finds the fastest window of <paramref name="n"/> consecutive laps.
    ///
    /// A plain sliding window: every start position is tried and the
    /// cheapest total wins. Lap counts are small enough that the
    /// straightforward version is clearer than a running-sum optimisation.
    ///
    /// Returns (0, 0) when the driver hasn't completed N laps - common for
    /// someone who crashed out early, and for any session recorded before
    /// per-lap times were stored.
    /// </summary>
    /// <summary>
    /// The best window of N consecutive FLYING laps - the same laps the
    /// rest of the app counts.
    ///
    /// LapTimesMs[0] is not a lap. It is the run from the green flag to
    /// the car's first crossing: a second or two when the grid sits just
    /// behind the loop, and the session already bars it from being anyone's
    /// best lap for exactly that reason. RaceGaps.FlyingLaps drops it.
    /// PaceAnalysis.CountedLaps drops it. This calculator did not - so the
    /// best-of-N window slid over it and picked it every time, because a
    /// 1.4s dash beats any real lap.
    ///
    /// On a 12s track scoring best-of-3, a driver with laps
    /// [1.40, 12.30, 12.10, 11.90, 11.85, 11.90] scored 25.80s instead of
    /// their real best window of 35.65s. Every driver was being ranked on
    /// how close they parked to the line plus their two opening traffic
    /// laps - and that ranking is TQ, and TQ seeds the mains.
    ///
    /// The window's reported start lap is renumbered so it still refers to
    /// the lap the driver would count as their first.
    /// </summary>
    public static (long TotalMs, int StartLap) BestFlyingWindow(IReadOnlyList<long> laps, int n)
    {
        if (laps.Count <= 1)
        {
            return (0, 0);
        }

        var flying = laps.Skip(1).ToList();
        var (total, start) = BestConsecutiveWindow(flying, n);

        return total == 0 ? (0, 0) : (total, start + 1);
    }

    /// <summary>
    /// The raw sliding window over whatever list it is given. Callers that
    /// mean race laps want <see cref="BestFlyingWindow"/> instead.
    /// </summary>
    public static (long TotalMs, int StartLap) BestConsecutiveWindow(IReadOnlyList<long> laps, int n)
    {
        if (n <= 0 || laps.Count < n)
        {
            return (0, 0);
        }

        var bestTotal = long.MaxValue;
        var bestStart = 0;

        for (var start = 0; start + n <= laps.Count; start++)
        {
            long total = 0;
            for (var i = start; i < start + n; i++)
            {
                total += laps[i];
            }

            if (total < bestTotal)
            {
                bestTotal = total;
                bestStart = start;
            }
        }

        return bestTotal == long.MaxValue ? (0, 0) : (bestTotal, bestStart + 1);
    }

    /// <param name="roundFilter">
    /// When supplied, only rounds whose id is in this set are counted.
    /// Used to keep one event's qualifying separate from another's -
    /// without it, a second race day silently folds in the first day's
    /// heats and seeds the mains from a mixture of both.
    /// Null counts every recorded round, which is right for a club that
    /// never needs the distinction.
    /// </param>
    /// <param name="live">
    /// The heat that is running right now, scored as if it had finished.
    ///
    /// Null for the ordinary question - where qualifying STANDS - which
    /// must not see a half-finished heat. Supplied only by the live
    /// projection, which is asking a different question: where would this
    /// leave everyone if it ended here.
    /// </param>
    public static List<Entry> Compute(string className, RoundDefinitionStore rounds, SessionHistoryStore history,
        ProfileStore profiles, RaceClass? raceClass = null, IReadOnlyCollection<string>? roundFilter = null,
        LiveHeat? live = null)
    {
        var method = raceClass?.QualifyingMethod ?? QualifyingMethod.TotalLaps;
        var lapCount = Math.Max(1, raceClass?.ConsecutiveLapCount ?? 3);
        var countBest = Math.Max(1, raceClass?.CountBestRounds ?? 1);

        // Gather every heat result per driver first, then decide which
        // count. Picking as we go can't support "best 2 of 3" - that needs
        // the full set in hand before anything is dropped.
        var byDriver = new Dictionary<string, List<HeatScore>>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, (string Driver, string Car)>(StringComparer.OrdinalIgnoreCase);
        var disqualified = new HashSet<(string Driver, string Group)>(new DriverGroupComparer());

        var qualifiers = rounds.ForClass(className)
            .Where(r => r.Kind == RoundKind.Qualifier && r.IsRecorded)
            .Where(r => roundFilter is null || roundFilter.Contains(r.Id))

            // A heat being re-run is represented by the live scores, not by
            // the result it is about to replace.
            .Where(r => live is null || !string.Equals(r.Id, live.RoundId, StringComparison.Ordinal))
            .ToList();

        // The heats being scored, the running one included, for deciding
        // which qualifying ROUND each heat is part of (points qualifying -
        // see RoundGroupOf). Built for every method because it is cheap and
        // keeps one code path; only points reads it.
        var liveRound = live is null
            ? null
            : rounds.Get(live.RoundId) ?? new RoundDefinition
            {
                Id = live.RoundId,
                ClassName = className,
                RoundLabel = live.RoundLabel,
                Kind = RoundKind.Qualifier,
            };
        var scoredHeats = liveRound is null ? qualifiers : qualifiers.Append(liveRound).ToList();

        foreach (var round in qualifiers)
        {
            var (group, groupName) = RoundGroupOf(round, scoredHeats);
            var session = history.Get(round.LinkedSessionId!);

            // A DEMO-LINKED HEAT DOES SEED, and that is the point of a
            // demo night (13 Oct 2026).
            //
            // This used to skip demos outright, with the reason "invented
            // lap times must never seed a real main" - written when a demo
            // could only reach a round by accident, because nothing linked
            // one deliberately. Now something does: a club rehearsing a
            // whole night (AppSettings.DemoFillsTheProgramme), and skipping
            // here left that night's mains seeded off nothing at all.
            //
            // The main it seeds is not a real one. A round is only linked to
            // a demo because the club asked for a demo night, and
            // RaceFiling.WouldOverwriteARealResult refuses to let a demo
            // land on a heat that really ran - so a demo in here means the
            // whole programme is a rehearsal. The results it produces still
            // score nothing: the championship, badges, personal bests and
            // every record check IsDemoSession for themselves.
            if (session is null)
            {
                continue;
            }

            foreach (var result in session.Results)
            {
                // A ghost is a pace reference, not an entrant. One left in
                // here takes a qualifying slot, and seeding the mains off
                // that puts a car in the A-main that cannot turn up to it.
                if (GhostService.IsGhost(result.Transponder))
                {
                    continue;
                }

                // WHY (11 Nov 2026, Phase 4.1, mixed-class heats): a heat
                // this class shares with another is one of its heats, but
                // only for its own drivers - to Stock, the 3 Stock cars of a
                // 3 Stock + 4 Mod race are the heat. Always true for a
                // one-class heat, so nothing else changes.
                if (!MixedHeats.Counts(round, className, result.Transponder, result.ClassName, profiles))
                {
                    continue;
                }

                // Disqualified from this heat after the race (the result
                // editor): the heat does not count for them at all. Their
                // lap times are still on the result - they are what the
                // timing saw - and without this the best-consecutive-laps
                // rule would still rank them from a heat they were thrown
                // out of.
                if (result.Outcome == ResultEditor.Dq)
                {
                    // Remembered for points qualifying only, so the round
                    // reads "DQ" rather than "DNS" in the breakdown. It
                    // scores the same - see RankByPoints.
                    disqualified.Add((DriverKey(result.Transponder, profiles), group));
                    continue;
                }

                var (windowMs, startLap) = BestFlyingWindow(result.LapTimesMs, lapCount);

                // Penalties from the result editor count here too. They
                // only ever reached Laps and ElapsedMs - which the
                // total-laps rule reads - while the window is worked out
                // from the lap times, which a penalty leaves alone. So under
                // best-consecutive-laps a penalised driver qualified as if
                // nothing had been given.
                //
                // A time penalty is added to the window as it is to the
                // total time. A lap penalty is a ruling that takes the lap
                // COUNT down and leaves every lap time standing (see
                // ResultEditor), so it does not take the window away; it is
                // charged as one average lap of the window per lap
                // penalised - the time the driver would have needed to make
                // up each lap they lost. Only a heat with a window is
                // touched: one without has nothing to add to.
                //
                // WHY (20 Oct 2026): penalties given DURING the race (jump
                // start, penalty buttons, warning time) count too. They used
                // to live only inside ElapsedMs, so a driver penalized at the
                // track still took pole. RaceFiling now saves them as
                // RacePenaltyMs; a result saved before that reads zero, which
                // is how it behaved before. The average lap is taken from the
                // clean window, before any penalty is added.
                long penaltyMs = 0;
                if (windowMs > 0)
                {
                    var averageLapMs = windowMs / lapCount;
                    penaltyMs = Math.Max(0, result.RacePenaltyMs) + Math.Max(0, result.TimePenaltyMs) + Math.Max(0, result.LapPenalty) * averageLapMs;
                    windowMs += penaltyMs;
                }

                var driverKey = DriverKey(result.Transponder, profiles);

                AddHeat(byDriver, driverKey, method, new HeatScore(round.Id, round.RoundLabel, round.LinkedSessionId, session.StartedAtUtc,
                    result.Laps, result.ElapsedMs, result.FastLapMs, windowMs, startLap, penaltyMs, group, groupName));

                var profile = profiles.Get(driverKey);
                names[driverKey] = (
                    !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName : result.DriverName,
                    profile?.CarModel ?? result.CarModel);
            }
        }

        if (live is not null)
        {
            var (liveGroup, liveGroupName) = RoundGroupOf(liveRound!, scoredHeats);

            foreach (var result in live.Results)
            {
                if (GhostService.IsGhost(result.Transponder))
                {
                    continue; // same as above - the heat running right now
                }

                // The running heat's other class's cars, as above (Phase 4.1).
                if (!MixedHeats.Counts(liveRound!, className, result.Transponder, null, profiles))
                {
                    continue;
                }

                var (windowMs, startLap) = BestFlyingWindow(result.LapTimesMs, lapCount);

                // WHY (20 Oct 2026): penalties given during the running heat
                // count on the window too, as they do on a saved heat. They
                // were left off, so the projection and the provisional pole
                // call put a penalised driver ahead of a clean one. As above,
                // only a heat with a window has anything to add to.
                long penaltyMs = 0;
                if (windowMs > 0)
                {
                    penaltyMs = Math.Max(0, result.PenaltyMs);
                    windowMs += penaltyMs;
                }

                var driverKey = DriverKey(result.Transponder, profiles);

                AddHeat(byDriver, driverKey, method, new HeatScore(live.RoundId, live.RoundLabel, null, live.StartedAtUtc,
                    result.Laps, result.ElapsedMs, result.FastLapMs, windowMs, startLap, penaltyMs, liveGroup, liveGroupName));

                var liveProfile = profiles.Get(driverKey);
                names[driverKey] = (
                    !string.IsNullOrWhiteSpace(liveProfile?.DriverName) ? liveProfile!.DriverName : result.DriverName,
                    liveProfile?.CarModel ?? result.CarModel);
            }
        }

        // The club's marshal rule (12 Nov 2026, setup plan step 8): a driver
        // logged as a marshal no-show under "loses their best qualifying
        // result" has their best counted heat taken out before anything is
        // counted - so their next-best stands in for it, best-N drops one
        // fewer, and the standings say which heat went and why. Read from
        // the rounds themselves (MarshalRule.ForfeitsFor), so every caller -
        // the standings, the mains, the phones, the event report - agrees.
        var forfeitWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var forfeitReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (transponder, reason) in MarshalRule.ForfeitsFor(className, rounds.All, roundFilter))
        {
            var key = DriverKey(transponder, profiles);

            if (!byDriver.TryGetValue(key, out var heats) || heats.Count == 0 || forfeitReasons.ContainsKey(key))
            {
                continue;
            }

            forfeitReasons[key] = reason;

            if (method == QualifyingMethod.RoundPoints)
            {
                continue; // RankByPoints takes their best ROUND - see there
            }

            var best = OrderBest(heats, method)[0];
            heats.Remove(best);
            forfeitWords[key] = $"Lost {best.RoundLabel} ({Plural.Of(best.Laps, "lap")}) - {reason}";

            // Nothing else run: they stay in the standings, at the foot,
            // rather than vanishing from the finals altogether.
            if (heats.Count == 0)
            {
                heats.Add(new HeatScore("#forfeit", "result lost", null, best.StartedAtUtc, 0, 0, 0, 0, 0));
            }
        }

        if (method == QualifyingMethod.RoundPoints)
        {
            return RankByPoints(byDriver, names, disqualified, countBest, forfeitReasons);
        }

        var entries = byDriver
            .Select(kv => BuildEntry(kv.Key, names[kv.Key], kv.Value, method, countBest) is var entry && forfeitWords.TryGetValue(kv.Key, out var lost)
                ? entry with { Forfeit = lost }
                : entry)
            .ToList();

        return Rank(entries, method);
    }

    /// <summary>A driver's heats best first under the class's rule - the order BuildEntry counts them in.</summary>
    private static List<HeatScore> OrderBest(List<HeatScore> heats, QualifyingMethod method) =>
        method == QualifyingMethod.BestConsecutiveLaps
            ? heats
                .OrderBy(h => h.ConsecutiveMs > 0 ? 0 : 1)
                .ThenBy(h => h.ConsecutiveMs)
                .ThenByDescending(h => h.Laps)
                .ThenBy(h => h.ElapsedMs)
                .ToList()
            : heats
                .OrderByDescending(h => h.Laps)
                .ThenBy(h => h.ElapsedMs)
                .ToList();

    /// <summary>Compares (driver, round) pairs ignoring the case of the transponder, as every driver lookup here does.</summary>
    private sealed class DriverGroupComparer : IEqualityComparer<(string Driver, string Group)>
    {
        public bool Equals((string Driver, string Group) x, (string Driver, string Group) y) =>
            string.Equals(x.Driver, y.Driver, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Group, y.Group, StringComparison.Ordinal);

        public int GetHashCode((string Driver, string Group) pair) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(pair.Driver), StringComparer.Ordinal.GetHashCode(pair.Group));
    }

    /// <summary>
    /// Which qualifying ROUND a heat is part of, and what to call it - for
    /// points qualifying, which ranks every heat of a round together
    /// (11 Nov 2026, Phase 5.1).
    ///
    /// Read from the label the way the rest of the program reads it
    /// (HeatBalanceForm.RoundKeyFor): "Q2 Heat 3" and "Race 2 Heat 1" are
    /// round 2. A one-round night's unnumbered "Heat 1", "Heat 2" are all
    /// one round. An unnumbered heat BESIDE numbered rounds was added by
    /// hand and belongs to none of them, so it is scored as a round of its
    /// own - folding it into Q1 would rank its drivers against heats they
    /// never shared a track with, and dropping it would throw away a heat
    /// they really ran.
    /// </summary>
    private static (string Key, string Name) RoundGroupOf(RoundDefinition round, IReadOnlyList<RoundDefinition> scoredHeats)
    {
        var key = HeatBalanceForm.RoundKeyFor(round, scoredHeats);

        if (key is null)
        {
            return ("#" + round.Id, string.IsNullOrWhiteSpace(round.RoundLabel) ? "Extra heat" : round.RoundLabel.Trim());
        }

        if (key.Length == 0)
        {
            return (string.Empty, "Qualifying");
        }

        // "Race 2" on a club night, "Q2" otherwise - the round named the way
        // its heats are.
        var number = key.Substring(1);
        var name = (round.RoundLabel ?? string.Empty).TrimStart().StartsWith("Race", StringComparison.OrdinalIgnoreCase)
            ? $"Race {number}"
            : key;

        return (key, name);
    }

    /// <summary>
    /// Points qualifying (11 Nov 2026, race-night build plan, Phase 5.1).
    ///
    /// For each qualifying ROUND, every driver who ran one of its heats is
    /// ranked against everybody else in that round - most laps, then least
    /// time, which is the total-laps rule applied inside the round - and
    /// scores their place: 1st = 1 point, 2nd = 2 and so on. Drivers level
    /// on laps AND time to the millisecond share the place (and its points),
    /// and the next driver takes the place after them, the way a results
    /// sheet prints a dead heat.
    ///
    /// A driver who did not run a round - late arrival, broken car, or
    /// disqualified from it in Edit Result - scores the FIELD SIZE PLUS ONE
    /// for it, the field being every driver with any qualifying result in
    /// the class. That is worse than last place in any round, so missing a
    /// round can never beat running it, and with best-N it is simply the
    /// first round dropped. It also means every driver has a score for every
    /// round, so totals of the same number of rounds are always compared -
    /// the trap Rank has to step around for best consecutive laps cannot
    /// happen here.
    ///
    /// CountBestRounds picks each driver's best (lowest) N rounds. Lowest
    /// total wins. Ties, in order: the best single-round finish (a driver
    /// who won a round beats one who never did), then the best single heat
    /// by laps then time, then the order they were found in.
    ///
    /// Penalties need nothing new here: race-control penalties, time
    /// penalties and lap penalties already reach Laps and ElapsedMs, which
    /// is what each round is ranked on. The running heat (LiveHeat) is
    /// simply one more heat of its round, so the projection and the
    /// provisional pole call rank it against the round's finished heats.
    /// Alternate transponders were folded into their driver before any of
    /// this (DriverKey, AddHeat).
    /// </summary>
    private static List<Entry> RankByPoints(Dictionary<string, List<HeatScore>> byDriver,
        Dictionary<string, (string Driver, string Car)> names, HashSet<(string Driver, string Group)> disqualified, int countBest,
        IReadOnlyDictionary<string, string>? forfeits = null)
    {
        var field = byDriver.Count;
        var missed = field + 1;

        // Each driver's one result per round: their better heat if they ran
        // two heats of it (moved between heats, or a hand-added heat filed
        // under a round), by the same rule the round is ranked on.
        var perRound = byDriver.ToDictionary(
            kv => kv.Key,
            kv => kv.Value
                .GroupBy(h => h.RoundGroup, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Laps).ThenBy(h => h.ElapsedMs).First(), StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

        // The rounds in running order - the earliest heat of each.
        var groups = byDriver.Values
            .SelectMany(v => v)
            .GroupBy(h => h.RoundGroup, StringComparer.Ordinal)
            .Select(g => (Key: g.Key, Name: g.First().RoundGroupName, Start: g.Min(h => h.StartedAtUtc)))
            .OrderBy(g => g.Start)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // place[driver][round] - zero for a round the driver did not run.
        var places = byDriver.Keys.ToDictionary(k => k, _ => new Dictionary<string, int>(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var ranked = perRound
                .Where(kv => kv.Value.ContainsKey(group.Key))
                .Select(kv => (Driver: kv.Key, Heat: kv.Value[group.Key]))
                .OrderByDescending(x => x.Heat.Laps)
                .ThenBy(x => x.Heat.ElapsedMs)
                .ToList();

            for (var i = 0; i < ranked.Count; i++)
            {
                var tiedWithAbove = i > 0
                    && ranked[i].Heat.Laps == ranked[i - 1].Heat.Laps
                    && ranked[i].Heat.ElapsedMs == ranked[i - 1].Heat.ElapsedMs;

                places[ranked[i].Driver][group.Key] = tiedWithAbove ? places[ranked[i - 1].Driver][group.Key] : i + 1;
            }
        }

        // The marshal rule's lost result (12 Nov 2026, setup plan step 8):
        // under points it is the driver's best ROUND that goes - their place
        // in it is taken away and the round scores as one they missed. Every
        // other driver keeps the place they earned in it.
        var lost = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lostWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (driver, reason) in forfeits ?? new Dictionary<string, string>())
        {
            if (!places.TryGetValue(driver, out var mine))
            {
                continue;
            }

            var best = groups
                .Select((g, index) => (g, index, Place: mine.TryGetValue(g.Key, out var p) ? p : 0))
                .Where(x => x.Place > 0)
                .OrderBy(x => x.Place)
                .ThenBy(x => x.index)
                .FirstOrDefault();

            if (best.Place <= 0)
            {
                continue;
            }

            mine.Remove(best.g.Key);
            lost[driver] = best.g.Key;
            lostWords[driver] = $"Lost {best.g.Name} ({Ordinal(best.Place)}) - {reason}";
        }

        var entries = new List<Entry>();

        foreach (var (driver, heats) in byDriver)
        {
            var scores = groups
                .Select(g => (Group: g, Place: places[driver].TryGetValue(g.Key, out var p) ? p : 0))
                .Select(x => (x.Group, x.Place, Points: x.Place > 0 ? x.Place : missed))
                .ToList();

            // Best N, lowest points first; a tie between two rounds keeps
            // the earlier one, so the choice is stable.
            var counted = scores
                .Select((s, index) => (s, index))
                .OrderBy(x => x.s.Points)
                .ThenBy(x => x.index)
                .Take(Math.Min(countBest, scores.Count))
                .Select(x => x.s)
                .ToList();
            var dropped = scores.Where(s => !counted.Contains(s)).ToList();

            string Finish((string Key, string Name, DateTime Start) group, int place) =>
                place > 0 ? Ordinal(place)
                : disqualified.Contains((driver, group.Key)) ? "DQ"
                : lost.TryGetValue(driver, out var lostKey) && lostKey == group.Key ? "lost (marshal)"
                : "DNS";

            string PointsText(int points) => points == 1 ? "1 pt" : $"{points} pts";

            var detail = string.Join(", ", scores.Select(s => $"{s.Group.Name} {Finish(s.Group, s.Place)} ({PointsText(s.Points)})"));
            var droppedSummary = string.Join(", ", dropped.Select(s => $"{s.Group.Name} ({Finish(s.Group, s.Place)}, {PointsText(s.Points)})"));

            var countedHeats = counted
                .Where(s => s.Place > 0)
                .Select(s => perRound[driver][s.Group.Key])
                .ToList();

            // The best single heat anywhere - the second tie-breaker, and
            // the laps and time shown beside the points.
            var bestHeat = heats.OrderByDescending(h => h.Laps).ThenBy(h => h.ElapsedMs).First();
            var bestFinish = scores.Where(s => s.Place > 0).Select(s => s.Place).DefaultIfEmpty(0).Min();
            var single = countedHeats.Count == 1 && counted.Count == 1 ? countedHeats[0] : null;

            entries.Add(new Entry(
                driver,
                names[driver].Driver,
                names[driver].Car,
                bestHeat.Laps,
                bestHeat.ElapsedMs,
                countedHeats.Where(h => h.FastLapMs > 0).Select(h => h.FastLapMs).DefaultIfEmpty(0).Min(),
                heats.Count,
                // In running order, whichever order they were picked in.
                SourceRoundLabel: string.Join(" + ", scores.Where(counted.Contains).Select(s => s.Group.Name)),
                SourceSessionId: single?.SessionId,
                SourceSessionStartedUtc: single?.StartedAtUtc,
                HeatsCounted: counted.Count,
                DroppedSummary: droppedSummary,
                Points: counted.Sum(s => s.Points),
                PointsRoundsCounted: counted.Count,
                PointsRoundsRun: groups.Count,
                PointsDetail: detail,
                BestRoundFinish: bestFinish,
                Forfeit: lostWords.TryGetValue(driver, out var lostLine) ? lostLine : string.Empty));
        }

        return entries
            .OrderBy(e => e.Points)
            .ThenBy(e => e.BestRoundFinish > 0 ? e.BestRoundFinish : int.MaxValue)
            .ThenByDescending(e => e.BestLaps)
            .ThenBy(e => e.BestElapsedMs)
            .ToList();
    }

    /// <summary>"1st", "2nd", "11th" - a round finish in the points breakdown.</summary>
    private static string Ordinal(int n) => (n % 100) is 11 or 12 or 13
        ? $"{n}th"
        : (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        };

    /// <summary>
    /// Who a result belongs to: the driver's MAIN transponder when it was
    /// set on their alternate, otherwise the transponder as recorded.
    ///
    /// Results were grouped by the transponder alone, so a driver who ran a
    /// heat on their second car - saved under its own id by Next Level
    /// Timing, or timed as a separate row here - appeared twice in
    /// qualifying, and took two seats in the mains. Everything the mains
    /// are built from (rosters, grids, handicaps) holds main transponders,
    /// so that is the id the driver is ranked under.
    /// </summary>
    private static string DriverKey(string transponder, ProfileStore profiles) =>
        profiles.PrimaryForAlternate(transponder) ?? transponder;

    /// <summary>
    /// Files one heat result under a driver - keeping only the better one
    /// when they already have a result from the same heat.
    ///
    /// Two rows in one heat is a driver's two cars once they are filed under
    /// one driver. Counting both would hand them an extra heat under a
    /// best-N rule; a driver can only drive one car at a time, so the better
    /// of the two is the one that stands for them.
    /// </summary>
    private static void AddHeat(Dictionary<string, List<HeatScore>> byDriver, string driverKey,
        QualifyingMethod method, HeatScore score)
    {
        if (!byDriver.TryGetValue(driverKey, out var list))
        {
            list = new List<HeatScore>();
            byDriver[driverKey] = list;
        }

        var sameHeat = list.FindIndex(h => string.Equals(h.RoundKey, score.RoundKey, StringComparison.Ordinal));

        if (sameHeat < 0)
        {
            list.Add(score);
        }
        else if (IsBetterHeat(score, list[sameHeat], method))
        {
            list[sameHeat] = score;
        }
    }

    /// <summary>Whether heat <paramref name="a"/> beats heat <paramref name="b"/> under the class's scoring rule - the same order BuildEntry sorts by.</summary>
    private static bool IsBetterHeat(HeatScore a, HeatScore b, QualifyingMethod method)
    {
        if (method == QualifyingMethod.BestConsecutiveLaps)
        {
            var aHasWindow = a.ConsecutiveMs > 0;
            var bHasWindow = b.ConsecutiveMs > 0;

            if (aHasWindow != bHasWindow)
            {
                return aHasWindow;
            }

            if (aHasWindow && a.ConsecutiveMs != b.ConsecutiveMs)
            {
                return a.ConsecutiveMs < b.ConsecutiveMs;
            }
        }

        return a.Laps != b.Laps ? a.Laps > b.Laps : a.ElapsedMs < b.ElapsedMs;
    }

    /// <summary>Picks a driver's counting heats and totals them.</summary>
    private static Entry BuildEntry(string transponder, (string Driver, string Car) who,
        List<HeatScore> heats, QualifyingMethod method, int countBest)
    {
        // Order best-first under the chosen scoring rule, then keep the top N.
        var ordered = method == QualifyingMethod.BestConsecutiveLaps
            ? heats
                // Heats with no valid window sort last, so they're the first
                // to be dropped rather than displacing a real result.
                .OrderBy(h => h.ConsecutiveMs > 0 ? 0 : 1)
                .ThenBy(h => h.ConsecutiveMs)
                // Among those, most laps then least time, as IsBetterHeat and
                // Rank judge them. Without it they stayed in running order,
                // so a driver with no full run anywhere was counted on their
                // earliest heats rather than their best.
                .ThenByDescending(h => h.Laps)
                .ThenBy(h => h.ElapsedMs)
                .ToList()
            : heats
                .OrderByDescending(h => h.Laps)
                .ThenBy(h => h.ElapsedMs)
                .ToList();

        var counted = ordered.Take(Math.Min(countBest, ordered.Count)).ToList();
        var dropped = ordered.Skip(counted.Count).ToList();

        // Each dropped heat with the figure it was judged on, so it's clear
        // WHY it lost out rather than just that it did.
        var droppedSummary = string.Join(", ", dropped.Select(h =>
            method == QualifyingMethod.BestConsecutiveLaps
                ? h.ConsecutiveMs > 0
                    ? $"{h.RoundLabel} ({h.ConsecutiveMs / 1000.0:0.000}s)"
                    : $"{h.RoundLabel} (no valid run)"
                : $"{h.RoundLabel} ({Plural.Of(h.Laps, "lap")})"));

        // A counted heat with no window still shows, marked, so a total made
        // of fewer runs than the heats listed beside it explains itself.
        var label = counted.Count == 1
            ? counted[0].RoundLabel
            : string.Join(" + ", counted.Select(h =>
                method == QualifyingMethod.BestConsecutiveLaps && h.ConsecutiveMs <= 0
                    ? $"{h.RoundLabel} (no valid run)"
                    : h.RoundLabel));

        return new Entry(
            transponder,
            who.Driver,
            who.Car,
            counted.Sum(h => h.Laps),
            counted.Sum(h => h.ElapsedMs),
            // Fastest lap is the best across the counting heats, not a sum.
            counted.Where(h => h.FastLapMs > 0).Select(h => h.FastLapMs).DefaultIfEmpty(0).Min(),
            heats.Count,
            // The windows that exist, totalled. It used to be all or
            // nothing - one counted heat without a window zeroed the lot -
            // which sent a driver who ran two heats and crashed out of one
            // to the bottom, below somebody who only turned up for one.
            // Rank puts it right: how many windows a total is made of is
            // compared before the total itself.
            counted.Where(h => h.ConsecutiveMs > 0).Sum(h => h.ConsecutiveMs),
            counted.Count == 1 ? counted[0].ConsecutiveStartLap : 0,
            label,
            counted.Count == 1 ? counted[0].SessionId : null,
            counted.Count == 1 ? counted[0].StartedAtUtc : null,
            counted.Count,
            droppedSummary,
            counted.Count(h => h.ConsecutiveMs > 0),
            counted.Count == 1 ? counted[0].ConsecutivePenaltyMs : 0);
    }

    /// <summary>
    /// Orders the field. Under the consecutive rule, drivers with no valid
    /// window have nothing to rank on and must sort to the BOTTOM rather
    /// than to the top on a zero - they're then ordered among themselves by
    /// the total-laps rule so the list stays sensible rather than arbitrary.
    ///
    /// Among those WITH a window, the number of windows in the total comes
    /// first. Counting the best two heats sums two windows for a driver who
    /// ran them both and one for a driver who only ran one - and a one-heat
    /// total of 39s beat a two-heat total of 70s, so missing a heat was the
    /// quickest way to top qualifying. A total can only be compared with a
    /// total of the same number of runs, so more runs ranks first and the
    /// time decides within that. It also keeps the promise at the top of
    /// this class: running more heats can only help.
    /// </summary>
    private static List<Entry> Rank(List<Entry> entries, QualifyingMethod method)
    {
        if (method != QualifyingMethod.BestConsecutiveLaps)
        {
            return entries
                .OrderByDescending(x => x.BestLaps)
                .ThenBy(x => x.BestElapsedMs)
                .ToList();
        }

        var withWindow = entries.Where(x => x.BestConsecutiveMs > 0)
            .OrderByDescending(x => x.WindowsCounted)
            .ThenBy(x => x.BestConsecutiveMs)
            .ToList();

        var withoutWindow = entries.Where(x => x.BestConsecutiveMs <= 0)
            .OrderByDescending(x => x.BestLaps)
            .ThenBy(x => x.BestElapsedMs)
            .ToList();

        return withWindow.Concat(withoutWindow).ToList();
    }
}
