namespace HeatCaller;

/// <summary>
/// Ranks drivers across a class's recorded Qualifier rounds, to seed mains.
///
/// Two things are configured per class and combine freely:
///
///  - HOW a single heat is scored:
///      TotalLaps           - most laps, then lowest elapsed time
///      BestConsecutiveLaps - quickest unbroken run of N laps
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
        long ConsecutivePenaltyMs = 0);

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
        long ConsecutivePenaltyMs = 0)
    {
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

        var qualifiers = rounds.ForClass(className)
            .Where(r => r.Kind == RoundKind.Qualifier && r.IsRecorded)
            .Where(r => roundFilter is null || roundFilter.Contains(r.Id))

            // A heat being re-run is represented by the live scores, not by
            // the result it is about to replace.
            .Where(r => live is null || !string.Equals(r.Id, live.RoundId, StringComparison.Ordinal));

        foreach (var round in qualifiers)
        {
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

                // Disqualified from this heat after the race (the result
                // editor): the heat does not count for them at all. Their
                // lap times are still on the result - they are what the
                // timing saw - and without this the best-consecutive-laps
                // rule would still rank them from a heat they were thrown
                // out of.
                if (result.Outcome == ResultEditor.Dq)
                {
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
                    result.Laps, result.ElapsedMs, result.FastLapMs, windowMs, startLap, penaltyMs));

                var profile = profiles.Get(driverKey);
                names[driverKey] = (
                    !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName : result.DriverName,
                    profile?.CarModel ?? result.CarModel);
            }
        }

        if (live is not null)
        {
            foreach (var result in live.Results)
            {
                if (GhostService.IsGhost(result.Transponder))
                {
                    continue; // same as above - the heat running right now
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
                    result.Laps, result.ElapsedMs, result.FastLapMs, windowMs, startLap, penaltyMs));

                var liveProfile = profiles.Get(driverKey);
                names[driverKey] = (
                    !string.IsNullOrWhiteSpace(liveProfile?.DriverName) ? liveProfile!.DriverName : result.DriverName,
                    liveProfile?.CarModel ?? result.CarModel);
            }
        }

        var entries = byDriver
            .Select(kv => BuildEntry(kv.Key, names[kv.Key], kv.Value, method, countBest))
            .ToList();

        return Rank(entries, method);
    }

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
