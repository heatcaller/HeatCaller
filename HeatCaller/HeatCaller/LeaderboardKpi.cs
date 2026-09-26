namespace HeatCaller;

/// <summary>
/// Extra per-driver stats that a live session doesn't itself keep, because
/// they're about history rather than current state: the session knows the
/// CURRENT streak, but not the longest one reached, and it knows the best
/// lap but not how many times that best has been beaten.
///
/// A surface calls Observe() on each refresh and the tracker notices the
/// changes. Shared so Race Control, Practice, the Big Screen and the web
/// pages all report identical numbers instead of each keeping their own.
/// </summary>
public sealed class KpiTracker
{
    private readonly Dictionary<string, int> _bestStreaks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _personalBests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _lastKnownBest = new(StringComparer.OrdinalIgnoreCase);
    private int? _generation;

    /// <summary>
    /// Observes standings from a specific session generation
    /// (RaceControlSession.SessionGeneration), starting over when it changes.
    ///
    /// Nothing ever reset this on Race Control or the Big Screen, so Best
    /// Streak and PBs carried over from race to race for the whole evening:
    /// a driver's 14-lap streak from the first heat still showed in the last.
    /// </summary>
    public void Observe(IEnumerable<RacerStanding> standings, int generation)
    {
        if (_generation != generation)
        {
            Reset();
            _generation = generation;
        }

        Observe(standings);
    }

    public void Observe(IEnumerable<RacerStanding> standings)
    {
        foreach (var s in standings)
        {
            var peak = Math.Max(_bestStreaks.GetValueOrDefault(s.TransponderId), s.ConsecutiveValidLaps);
            _bestStreaks[s.TransponderId] = peak;

            if (s.FastestLapMs > 0)
            {
                if (_lastKnownBest.TryGetValue(s.TransponderId, out var previous) && s.FastestLapMs < previous)
                {
                    _personalBests[s.TransponderId] = _personalBests.GetValueOrDefault(s.TransponderId) + 1;
                }
                _lastKnownBest[s.TransponderId] = s.FastestLapMs;
            }
        }
    }

    public int BestStreak(string transponderId) => _bestStreaks.GetValueOrDefault(transponderId);
    public int PersonalBests(string transponderId) => _personalBests.GetValueOrDefault(transponderId);

    public void Reset()
    {
        _bestStreaks.Clear();
        _personalBests.Clear();
        _lastKnownBest.Clear();
    }
}

/// <summary>How far one car is behind another: whole laps, or a time gap when on the same lap.</summary>
public readonly record struct RaceGap(int LapsDown, long? GapMs);

/// <summary>
/// Gap and laps-down between two cars, measured the way timing systems do
/// it: at the line, against the moment the car ahead crossed the SAME lap.
///
/// The old calculation compared lap counts first. The instant the leader
/// crossed the line, everyone behind them still had one lap fewer - so for
/// that moment every car on the lead lap showed "+1L" and the position
/// report (which fires exactly when the leader crosses) called them "a lap
/// down" nearly every time. It also said "a lap down" for any lap
/// difference, even two or three.
/// </summary>
public static class RaceGaps
{
    /// <param name="carLaps">Laps completed by the car behind.</param>
    /// <param name="carLastCrossingMs">Race time at which it completed its most recent lap.</param>
    /// <param name="aheadLaps">Laps completed by the car ahead.</param>
    /// <param name="aheadLastCrossingMs">Race time of the car ahead's most recent lap.</param>
    /// <param name="aheadCrossingMs">Race time at which the car ahead completed a given lap, or null if unknown.</param>
    public static RaceGap Compute(int carLaps, long carLastCrossingMs, int aheadLaps, long aheadLastCrossingMs, Func<int, long?> aheadCrossingMs)
    {
        if (carLaps <= 0 || aheadLaps <= 0)
        {
            return new RaceGap(Math.Max(0, aheadLaps - carLaps), null);
        }

        if (aheadLaps < carLaps)
        {
            return new RaceGap(0, null);
        }

        // Laps the car ahead had already completed beyond ours at the moment
        // we last crossed.
        var lapsDown = 0;
        for (var lap = carLaps + 1; lap <= aheadLaps; lap++)
        {
            if (aheadCrossingMs(lap) is { } t && t < carLastCrossingMs)
            {
                lapsDown++;
            }
        }

        // And if the car ahead has crossed TWO or more times since we last
        // did, we've been lapped out on track even though we haven't
        // reached the line yet to prove it.
        lapsDown = Math.Max(lapsDown, aheadLaps - carLaps - 1);

        if (lapsDown > 0)
        {
            return new RaceGap(lapsDown, null);
        }

        long? aheadAtOurLap = aheadLaps == carLaps ? aheadLastCrossingMs : aheadCrossingMs(carLaps);
        var gap = aheadAtOurLap is { } a ? carLastCrossingMs - a : (long?)null;

        return new RaceGap(0, gap is >= 0 ? gap : null);
    }

    /// <summary>Gap from <paramref name="car"/> back to <paramref name="ahead"/>, using the session's own lap history.</summary>
    public static RaceGap Between(RacerStanding car, RacerStanding ahead) =>
        Compute(car.Laps, car.ElapsedMs, ahead.Laps, ahead.ElapsedMs, lap => CrossingMs(ahead, lap));

    /// <summary>
    /// Race time at which a car completed the given lap: the running total of
    /// its lap times, shifted by any penalty (so it ranks the same way the
    /// standings do). Null if that lap isn't in its history - for instance
    /// a lap added by hand, which has no crossing time.
    /// </summary>
    public static long? CrossingMs(RacerStanding s, int lap) =>
        CrossingMs(s.LapTimesMs, s.Laps, s.ElapsedMs, lap);

    /// <inheritdoc cref="CrossingMs(RacerStanding, int)"/>
    /// <remarks>Works from a saved result too, which keeps laps, lap times and elapsed time but not the penalty on its own.</remarks>
    public static long? CrossingMs(IReadOnlyList<long> lapTimesMs, int laps, long elapsedMs, int lap)
    {
        // Manual lap adjustments change the lap count without adding a lap
        // time, so line the history up with the END of the lap count.
        var index = lap - 1 - (laps - lapTimesMs.Count);
        if (index < 0 || index >= lapTimesMs.Count)
        {
            return null;
        }

        long upTo = 0;
        long all = 0;
        for (var i = 0; i < lapTimesMs.Count; i++)
        {
            all += lapTimesMs[i];
            if (i <= index)
            {
                upTo = all;
            }
        }

        // Whatever elapsed time isn't explained by lap times is penalty.
        return upTo + (elapsedMs - all);
    }

    /// <summary>"+1L", "+3L" or "+2.35" - or "-" when there's nothing reliable to show.</summary>
    public static string Format(RaceGap gap) =>
        gap.LapsDown > 0 ? $"+{gap.LapsDown}L"
        : gap.GapMs is { } ms ? $"+{ms / 1000.0:0.00}"
        : "-";

    /// <summary>
    /// Real laps only - every counted lap except the first, which runs from
    /// the green to the car's first crossing and isn't a lap time. Used for
    /// averages and consistency, where that one odd value skewed everything.
    ///
    /// Every lap after the first, in order, so position in the list still
    /// means lap number - the web lap chart plots it that way. For figures
    /// worked out from lap times, use <see cref="PaceLaps"/>.
    /// </summary>
    public static IReadOnlyList<long> FlyingLaps(RacerStanding s) =>
        s.LapTimesMs.Count > 1 ? s.LapTimesMs.Skip(1).ToList() : new List<long>();

    /// <summary>
    /// The flying laps that say something about pace: <see cref="FlyingLaps"/>
    /// without any lap that ran through a crash recovery.
    ///
    /// Such a lap spans the whole stoppage, so one lap minutes long wrecked
    /// the average, the spread, the trend and the time lost for the rest of
    /// the race. Left out of the figures rather than out of the lap list,
    /// which still has to add up to the driver's race time. See
    /// RacerStanding.RecoveryLapIndexes.
    /// </summary>
    public static IReadOnlyList<long> PaceLaps(RacerStanding s) =>
        s.LapTimesMs.Where((_, i) => i > 0 && !s.IsRecoveryLap(i)).ToList();
}

/// <summary>Everything a KPI might need beyond the single standing being rendered.</summary>
public sealed class KpiContext
{
    public required IReadOnlyList<RacerStanding> Ordered { get; init; }
    public required ProfileStore Profiles { get; init; }
    public required KpiTracker Tracker { get; init; }

    /// <summary>How many recent laps the rolling average covers.</summary>
    public int RollingWindow { get; init; } = 5;

    /// <summary>Quickest lap anyone has set this session, or 0 if none yet.</summary>
    public long FastestOverall { get; init; }

    /// <summary>
    /// Consecutive clean laps that count as a streak, so the Streak column
    /// turns green at the same number the announcer calls at.
    ///
    /// Three until a surface passes the running session's own - which every
    /// one of them does.
    /// </summary>
    public int StreakThreshold { get; init; } = 3;

    /// <summary>
    /// Earned badges, when the surface has them to hand.
    ///
    /// Optional on purpose: a KPI that needs a store nobody passed should
    /// show a dash, not stop the board drawing.
    /// </summary>
    public AchievementStore? Achievements { get; init; }

    /// <summary>
    /// Where each driver on track would qualify if this heat ended now.
    ///
    /// Pre-computed by the surface rather than worked out per cell: the
    /// answer ranks the whole class twice and is the same for every row, so
    /// a column that computed it itself would do it once per driver, on
    /// every refresh, for one shared result.
    ///
    /// Null whenever the running session is not a qualifier - which is most
    /// of the time - and the column shows a dash.
    /// </summary>
    public IReadOnlyDictionary<string, QualifyingProjector.Slot>? Projection { get; init; }

    /// <summary>
    /// Names to show in place of the ones on the profiles, keyed by
    /// transponder.
    ///
    /// Set only by the surface that publishes to the relay, and null
    /// everywhere else - Race Control, the Big Screen, Practice and the
    /// check-in page all need full names to be useful.
    ///
    /// A map rather than a flag, because shortening has to be decided
    /// across the FIELD. "Ghost of Christmas Past" and "Ghost of Christmas
    /// Present" both become "Ghost of Christmas P" if each is shortened on
    /// its own, and a leaderboard with two identical names in it is worse
    /// than one with none shortened at all. A column sees one row, so it
    /// cannot make that call - it is made once by whoever holds the whole
    /// list, and handed down here.
    /// </summary>
    public IReadOnlyDictionary<string, string>? DisplayNames { get; init; }

    /// <summary>
    /// The board is going to the public relay, not a screen at the venue.
    ///
    /// The one thing that changes: no transponder number is ever shown.
    /// Locally an id is a useful fallback for a car nobody has named - the
    /// person reading it may be the one holding it. Online it is a key that
    /// follows somebody from meeting to meeting, so there the Driver column
    /// says "Unnamed driver" and the Transponder column is blank.
    /// </summary>
    public bool PublicCopy { get; init; }
}

/// <summary>How a KPI value should be tinted, so every surface highlights the same things.</summary>
public enum KpiEmphasis { None, Good, Bad, Highlight }

public sealed record KpiValue(string Text, KpiEmphasis Emphasis = KpiEmphasis.None);

public sealed record KpiDefinition(
    string Key,
    string Label,
    string Description,
    int Weight,
    Func<RacerStanding, int, KpiContext, KpiValue> Compute);

/// <summary>
/// The full set of leaderboard columns available anywhere in the app.
/// Surfaces pick a subset by key; adding a KPI here makes it immediately
/// selectable on Race Control, Practice, the Big Screen and the web pages
/// without touching any of them.
/// </summary>
public static class KpiCatalog
{
    public static readonly IReadOnlyList<KpiDefinition> All = new List<KpiDefinition>
    {
        new("pos", "Pos", "Running position.", 35,
            (_, index, _) => new KpiValue((index + 1).ToString())),

        new("driver", "Driver", "Driver name, or the raw transponder ID if unassigned.", 120,
            (s, _, ctx) =>
            {
                // A name chosen across the whole field wins, where the
                // surface supplied one.
                if (ctx.DisplayNames is not null
                    && ctx.DisplayNames.TryGetValue(s.TransponderId, out var chosen))
                {
                    return WithWarningBadge(chosen, s);
                }

                var profile = ctx.Profiles.Get(s.TransponderId);

                // The transponder id stays whole when it is the fallback:
                // shortening is for names, and an id is not one. What must
                // never happen is an id shown NEXT TO a name - that pair
                // is what would follow somebody from meeting to meeting -
                // and this returns one or the other, never both.
                if (!string.IsNullOrWhiteSpace(profile?.DriverName))
                {
                    return WithWarningBadge(profile!.DriverName, s);
                }

                return WithWarningBadge(ctx.PublicCopy ? RelayDocuments.UnnamedDriver : s.TransponderId, s);
            }),

        new("car", "Car", "Car model from the driver's profile.", 100,
            (s, _, ctx) => new KpiValue(ctx.Profiles.Get(s.TransponderId)?.CarModel ?? "-")),

        new("transponder", "Transponder", "Raw transponder ID.", 80,
            (s, _, ctx) => new KpiValue(ctx.PublicCopy ? "-" : s.TransponderId)),

        new("laps", "Laps", "Laps completed.", 45,
            (s, _, _) => new KpiValue(s.Laps.ToString())),

        new("last", "Last Lap", "Most recent counted lap.", 65,
            (s, _, _) => new KpiValue(Secs(s.LapTimesMs.Count > 0 ? s.LapTimesMs[^1] : 0))),

        new("best", "Best Lap", "Fastest lap this session. Highlighted when it's the quickest of anyone.", 65,
            (s, _, ctx) => new KpiValue(Secs(s.FastestLapMs),
                s.FastestLapMs > 0 && s.FastestLapMs == ctx.FastestOverall ? KpiEmphasis.Highlight : KpiEmphasis.None)),

        new("badges", "Badges", "How many achievement badges this driver has earned.", 60,
            (s, _, ctx) =>
            {
                if (ctx.Achievements is null)
                {
                    return new KpiValue("-");
                }

                var earned = ctx.Achievements.For(s.TransponderId).Unlocked
                    .Count(u => AchievementCatalogue.Get(u.Id) is not null);

                return earned > 0 ? new KpiValue(earned.ToString()) : new KpiValue("-");
            }),

        new("delta", "vs Best", "How far the last lap was off this driver's own best.", 60,
            (s, _, _) =>
            {
                if (s.LapTimesMs.Count == 0 || s.FastestLapMs <= 0)
                {
                    return new KpiValue("-");
                }
                var gap = s.LapTimesMs[^1] - s.FastestLapMs;
                return gap == 0
                    ? new KpiValue("BEST", KpiEmphasis.Good)
                    : new KpiValue($"+{gap / 1000.0:0.000}");
            }),

        new("qual", "Qual", "Where this driver would qualify OVERALL if the heat ended now, and how many places this heat has moved them. Blank outside a qualifier.", 80,
            (s, _, ctx) =>
            {
                if (ctx.Projection is null || !ctx.Projection.TryGetValue(s.TransponderId, out var slot))
                {
                    return new KpiValue("-");
                }

                // A driver's first heat has no previous place to have moved
                // from, so it gets the position and no arrow - an arrow
                // there would be claiming a gain nobody made.
                if (slot.IsNew || slot.Change == 0)
                {
                    return new KpiValue($"P{slot.Position}");
                }

                var arrow = slot.Change > 0
                    ? $"P{slot.Position}  \u25B2{slot.Change}"
                    : $"P{slot.Position}  \u25BC{-slot.Change}";

                return new KpiValue(arrow, slot.Change > 0 ? KpiEmphasis.Good : KpiEmphasis.Bad);
            }),

        new("gap", "Gap to P1", "Time or laps behind the leader.", 70,
            (s, index, ctx) =>
            {
                if (index == 0 || ctx.Ordered.Count == 0)
                {
                    return new KpiValue("-");
                }
                return new KpiValue(RaceGaps.Format(RaceGaps.Between(s, ctx.Ordered[0])));
            }),

        new("interval", "Interval", "Gap to the car directly ahead, rather than to the leader.", 70,
            (s, index, ctx) =>
            {
                if (index == 0 || index >= ctx.Ordered.Count)
                {
                    return new KpiValue("-");
                }
                return new KpiValue(RaceGaps.Format(RaceGaps.Between(s, ctx.Ordered[index - 1])));
            }),

        new("pace", "Best Pace Gap", "How far this driver's best lap is off the quickest lap of the session.", 75,
            (s, _, ctx) => ctx.FastestOverall > 0 && s.FastestLapMs > ctx.FastestOverall
                ? new KpiValue($"+{(s.FastestLapMs - ctx.FastestOverall) / 1000.0:0.000}")
                : new KpiValue("-")),

        // Averages, spread and trend use flying laps only - the green-to-line
        // start lap is not a lap time and dragged all of them off.
        new("avg", "Average", "Mean of every counted lap after the start.", 65,
            (s, _, _) =>
            {
                var laps = RaceGaps.PaceLaps(s);
                return new KpiValue(laps.Count > 0 ? Secs((long)laps.Average()) : "-");
            }),

        new("rolling", "Rolling Avg", "Mean of the most recent few laps - reacts faster than the session average.", 70,
            (s, _, ctx) =>
            {
                var laps = RaceGaps.PaceLaps(s);
                return laps.Count > 0
                    ? new KpiValue(Secs((long)laps.TakeLast(ctx.RollingWindow).Average()))
                    : new KpiValue("-");
            }),

        new("stddev", "Std Dev", "Lap-time spread. Lower means more consistent.", 65,
            (s, _, _) =>
            {
                var laps = RaceGaps.PaceLaps(s);
                if (laps.Count < 2)
                {
                    return new KpiValue("-");
                }
                var avg = laps.Average();
                var sd = Math.Sqrt(laps.Sum(t => Math.Pow(t - avg, 2)) / laps.Count);
                return new KpiValue(Secs((long)sd));
            }),

        new("streak", "Streak", "Consecutive clean laps right now.", 55,
            (s, _, ctx) => s.ConsecutiveValidLaps > 0
                ? new KpiValue(s.ConsecutiveValidLaps.ToString(), s.ConsecutiveValidLaps >= ctx.StreakThreshold ? KpiEmphasis.Good : KpiEmphasis.None)
                : new KpiValue("-")),

        new("beststreak", "Best Streak", "Longest clean run reached this session.", 65,
            (s, _, ctx) =>
            {
                var peak = ctx.Tracker.BestStreak(s.TransponderId);
                return new KpiValue(peak > 0 ? peak.ToString() : "-");
            }),

        new("streakpace", "Streak Pace", "Average lap within the current streak - shows whether a clean run is also a quick one.", 75,
            (s, _, _) => s.StreakLapCount > 0
                ? new KpiValue(Secs(s.StreakLapTimeSumMs / s.StreakLapCount))
                : new KpiValue("-")),

        new("trend", "Trend", "Second half of the session against the first - finding time or fading.", 65,
            (s, _, _) =>
            {
                var laps = RaceGaps.PaceLaps(s);
                if (laps.Count < 6)
                {
                    return new KpiValue("-");
                }
                var half = laps.Count / 2;
                var delta = laps.Skip(laps.Count - half).Average() - laps.Take(half).Average();
                if (Math.Abs(delta) < 20)
                {
                    return new KpiValue("level");
                }
                return delta < 0
                    ? new KpiValue($"\u25B2 {Math.Abs(delta) / 1000.0:0.00}", KpiEmphasis.Good)
                    : new KpiValue($"\u25BC {delta / 1000.0:0.00}", KpiEmphasis.Bad);
            }),

        // --- Analytics that answer "what should I do about it?" ---
        //
        // Average and std dev describe a race; these three point at
        // something. Time Lost says how much is on the table, Gained says
        // whether the race is going anywhere, Clean % says whether the
        // problem is pace or mistakes.
        new("timelost", "Time Lost", "Total time given away against this driver's own best lap - what a perfect run would have saved.", 70,
            (s, _, _) =>
            {
                var laps = RaceGaps.PaceLaps(s);

                // Measured against their OWN best, not the field's. This is
                // "how much did you leave out there", not "how far off the
                // pace are you" - Best Pace Gap already answers the latter,
                // and a slower driver having a tidy race should read as a
                // tidy race.
                if (laps.Count < 2 || s.FastestLapMs <= 0)
                {
                    return new KpiValue("-");
                }

                var lost = laps.Sum(l => Math.Max(0, l - s.FastestLapMs));

                // Their best lap is in the sum as a zero, which is correct -
                // it cost nothing.
                return lost <= 0
                    ? new KpiValue("0.000", KpiEmphasis.Good)
                    : new KpiValue($"{lost / 1000.0:0.000}");
            }),

        new("gained", "Gained", "Positions made up - or lost - since the grid.", 55,
            (s, index, _) =>
            {
                // Zero means the grid was never known, which is not the same
                // as starting where you are now. Without a round selected or
                // a previous race to order from, there is nothing to compare.
                if (s.StartPosition <= 0)
                {
                    return new KpiValue("-");
                }

                var moved = s.StartPosition - (index + 1);

                return moved switch
                {
                    > 0 => new KpiValue($"+{moved}", KpiEmphasis.Good),
                    < 0 => new KpiValue(moved.ToString(), KpiEmphasis.Bad),
                    _ => new KpiValue("="),
                };
            }),

        new("clean", "Clean %", "Share of this driver's passes that counted as laps - low means mistakes rather than pace.", 65,
            (s, _, _) =>
            {
                var passes = s.Laps + s.RejectedLaps;

                if (passes < 3)
                {
                    return new KpiValue("-");
                }

                var pct = 100.0 * s.Laps / passes;

                // Rejected passes are usually a car re-triggering the loop
                // after a shunt, so a low number here is a race full of
                // incidents - worth flagging, not just reporting.
                return new KpiValue($"{pct:0}%", pct >= 95 ? KpiEmphasis.Good : pct < 75 ? KpiEmphasis.Bad : KpiEmphasis.None);
            }),

        new("led", "Led", "Laps completed while leading on the road.", 45,
            (s, _, _) => s.LapsLed > 0 ? new KpiValue(s.LapsLed.ToString(), KpiEmphasis.Highlight) : new KpiValue("-")),

        new("pbs", "PBs", "How many times this driver has beaten their own best this session.", 40,
            (s, _, ctx) => new KpiValue(ctx.Tracker.PersonalBests(s.TransponderId).ToString())),

        new("penalty", "Penalty", "Manually applied time penalties.", 60,
            (s, _, _) => s.PenaltyMs > 0
                ? new KpiValue($"+{s.PenaltyMs / 1000.0:0}s", KpiEmphasis.Bad)
                : new KpiValue("-")),

        new("warnings", "Warnings", "Warnings from race control this race - and a black flag, once the class's ladder reaches one.", 70,
            (s, _, _) => s.Warnings.Count == 0
                ? new KpiValue("-")
                : new KpiValue(s.IsBlackFlagged ? $"{s.Warnings.Count} \u2691 BLACK" : $"\u26A0 {s.Warnings.Count}", KpiEmphasis.Bad)),

        new("total", "Total Time", "Elapsed race time for this driver.", 75,
            (s, _, _) => new KpiValue(s.ElapsedMs > 0 ? $"{s.ElapsedMs / 60000}:{s.ElapsedMs / 1000 % 60:00}" : "-")),
    };

    /// <summary>
    /// The driver's name with their warning mark after it ("Asher  \u26A02"),
    /// so a warning shows on every board that has a Driver column without
    /// anyone having to add a column for it. Red on Race Control; the Big
    /// Screen keeps only positive highlights, so there it is just the mark.
    /// </summary>
    private static KpiValue WithWarningBadge(string name, RacerStanding s) =>
        s.Warnings.Count == 0
            ? new KpiValue(name)
            : new KpiValue(name + WarningRules.Badge(s.Warnings), KpiEmphasis.Bad);

    private static string Secs(long ms) => ms > 0 ? $"{ms / 1000.0:0.000}" : "-";

    public static KpiDefinition? Find(string key) =>
        All.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Resolves saved keys to definitions, skipping any that no longer exist and falling back to the defaults if nothing usable is left.</summary>
    public static List<KpiDefinition> Resolve(IEnumerable<string> keys, IEnumerable<string> fallback)
    {
        var resolved = keys.Select(Find).Where(k => k is not null).Select(k => k!).ToList();
        return resolved.Count > 0
            ? resolved
            : fallback.Select(Find).Where(k => k is not null).Select(k => k!).ToList();
    }

    // Sensible starting sets per surface. Race Control gets the classic
    // columns; Practice leans on pace and streak data; the Big Screen and
    // web stay deliberately sparse so they're readable from a distance.
    public static readonly string[] DefaultRaceControl = { "pos", "driver", "car", "laps", "best", "gap", "streak" };
    public static readonly string[] DefaultPractice = { "driver", "laps", "last", "delta", "best", "rolling", "stddev", "streak", "beststreak", "streakpace", "trend", "pbs" };
    public static readonly string[] DefaultBigScreen = { "pos", "driver", "laps", "best", "gap" };
    // Phone-shaped: seven columns is about what a handset shows without
    // sideways scrolling, so this buys "last" and "trend" by dropping "car"
    // - the name is what identifies someone at trackside, and the car model
    // is a desktop nicety. Everything else stays one tick away on the Web
    // Server page.
    public static readonly string[] DefaultWeb = { "pos", "driver", "laps", "last", "best", "gap", "trend" };
}
