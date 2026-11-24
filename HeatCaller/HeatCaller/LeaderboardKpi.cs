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
        // A car yet to cross the line - still held at the line in a
        // hand-released start, say - is only lapped once the car ahead has
        // crossed TWO or more times, the same rule as below. Taking the
        // plain difference showed every held car as "+1L" the moment the
        // first released car finished its start lap (bug hunt 13), the very
        // "leader crossed first" mistake this class is here to avoid.
        if (carLaps <= 0 || aheadLaps <= 0)
        {
            return new RaceGap(Math.Max(0, aheadLaps - carLaps - 1), null);
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

    /// <summary>
    /// "+1L", "+3L" or "+2.345" - or "-" when there's nothing reliable to show.
    /// To the thousandth (1 Nov 2026, asked for by Asher: "the gap to p1 ...
    /// should be to the thousandth like everything else"), as the lap times
    /// are and as the running order says it.
    /// </summary>
    public static string Format(RaceGap gap) =>
        gap.LapsDown > 0 ? $"+{gap.LapsDown}L"
        : gap.GapMs is { } ms ? "+" + (ms / 1000.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
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
    /// <remarks>
    /// Nor the lap after a credited start lap (bug hunt 17, see
    /// RacerStanding.StartLapCredited): that one runs from a crossing before
    /// the green, so it is the car's real start lap and the session keeps it
    /// out of best lap. Left in here, Time Lost measured it against a best
    /// it was never allowed to set.
    /// </remarks>
    public static IReadOnlyList<long> PaceLaps(RacerStanding s) =>
        s.LapTimesMs.Where((_, i) => i > 0 && !(s.StartLapCredited && i == 1) && !s.IsRecoveryLap(i)).ToList();
}

/// <summary>Everything a KPI might need beyond the single standing being rendered.</summary>
public sealed class KpiContext
{
    public required IReadOnlyList<RacerStanding> Ordered { get; init; }

    /// <summary>
    /// Who each transponder belongs to. A ProfileStore on the screens; its
    /// AnyThread view for the live state built off the UI thread.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P8): an interface rather than the
    /// store, so the web server's board can read the frozen profiles
    /// (ProfileStore.GetForAnyThread) when timing runs on its own thread and
    /// the live state is built off the UI. Every surface that passes a
    /// ProfileStore is unchanged.
    /// </remarks>
    public required IDriverLookup Profiles { get; init; }
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

    /// <summary>
    /// The length of each sector in feet, from the split loops on the track
    /// layout's plan - for the speed columns. Null when the layout has none,
    /// and they show a dash.
    /// </summary>
    public double[]? SectorLengthsFeet { get; init; }

    private readonly Dictionary<int, long[]?> _sessionSectorBests = new();

    private double? _fieldTopSpeed;

    /// <summary>The field's top speed through any sector, worked out once per board refresh - see KpiCatalog's Top Speed.</summary>
    internal double FieldTopSpeed(Func<double> work) => _fieldTopSpeed ??= work();

    /// <summary>
    /// The quickest time anyone in the field has set in each sector, for
    /// laps cut into <paramref name="count"/> sectors - worked out once per
    /// board refresh, not once per cell.
    /// </summary>
    public long[]? SessionSectorBests(int count)
    {
        if (!_sessionSectorBests.TryGetValue(count, out var bests))
        {
            bests = Sectors.SessionBests(Ordered, count);
            _sessionSectorBests[count] = bests;
        }

        return bests;
    }
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

        // 22 Oct 2026: "12*" when race control counted any of them by hand
        // (+1 Lap for a transponder that stopped reading) - see
        // HandCountedLaps. The number is the same; the mark says how it
        // was got.
        new("laps", "Laps", "Laps completed. A * means some were counted by hand.", 45,
            // On the relay's public copy too since 7 Nov 2026 (known
            // issues): the copy carries handCounted, and the relay's board
            // draws the legend under it - see WebServerService.
            (s, _, ctx) => new KpiValue(HandCountedLaps.LapsText(s.Laps, HandCountedLaps.During(s)))),

        new("last", "Last Lap", "Most recent counted lap.", 65,
            (s, _, _) => new KpiValue(Secs(s.LapTimesMs.Count > 0 ? s.LapTimesMs[^1] : 0))),

        new("best", "Best Lap", "Fastest lap this session. Highlighted when it's the quickest of anyone.", 65,
            (s, _, ctx) => new KpiValue(Secs(s.FastestLapMs),
                s.FastestLapMs > 0 && s.FastestLapMs == ctx.FastestOverall ? KpiEmphasis.Highlight : KpiEmphasis.None)),

        SectorKpi(1),
        SectorKpi(2),
        SectorKpi(3),
        SectorKpi(4),

        SpeedKpi(1),
        SpeedKpi(2),
        SpeedKpi(3),
        SpeedKpi(4),

        new("topspeed", "Top Speed", "The fastest the driver has gone through any sector this session, in mph - with the split loop a few feet past the line, that is a speed trap. Needs the split loops placed on the layout's plan in the Track Designer.", 60,
            (s, _, ctx) =>
            {
                if (TopSpeed(s, ctx.SectorLengthsFeet) is not { } top)
                {
                    return new KpiValue("-");
                }

                var fieldTop = ctx.FieldTopSpeed(() => ctx.Ordered
                    .Where(o => !GhostService.IsGhost(o.TransponderId))
                    .Select(o => TopSpeed(o, ctx.SectorLengthsFeet) ?? 0)
                    .DefaultIfEmpty(0)
                    .Max());

                return new KpiValue(Mph(top), top >= fieldTop ? KpiEmphasis.Highlight : KpiEmphasis.None);
            }),

        new("ideal", "Ideal Lap", "The driver's best sectors added up: their lap with every sector at its best. Needs a split loop.", 65,
            (s, _, _) => Sectors.TheoreticalBest(s) is { } ideal ? new KpiValue(Sectors.Format(ideal)) : new KpiValue("-")),

        new("atsplit", "At Split", "Live, as the car crosses a split loop: how far up (green) or down (red) it is on its own best lap to that point - or on its best sectors, when its best lap has none. Needs a split loop.", 60,
            (s, _, _) => Sectors.LiveDelta(s) is { } live
                ? new KpiValue(Sectors.FormatDelta(live.DeltaMs), live.DeltaMs < 0 ? KpiEmphasis.Good : live.DeltaMs > 0 ? KpiEmphasis.Bad : KpiEmphasis.None)
                : new KpiValue("-")),

        new("badges", "Achievements", "How many achievements this driver has earned.", 85,
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
                    : new KpiValue(Invariant($"+{gap / 1000.0:0.000}"));
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
                ? new KpiValue(Invariant($"+{(s.FastestLapMs - ctx.FastestOverall) / 1000.0:0.000}"))
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
                    ? new KpiValue(Invariant($"\u25B2 {Math.Abs(delta) / 1000.0:0.00}"), KpiEmphasis.Good)
                    : new KpiValue(Invariant($"\u25BC {delta / 1000.0:0.00}"), KpiEmphasis.Bad);
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
                    : new KpiValue(Invariant($"{lost / 1000.0:0.000}"));
            }),

        new("gained", "Gained", "Positions made up - or lost - since the grid.", 55,
            (s, index, ctx) =>
            {
                // Zero means the grid was never known, which is not the same
                // as starting where you are now. Without a round selected or
                // a previous race to order from, there is nothing to compare.
                if (s.StartPosition <= 0)
                {
                    return new KpiValue("-");
                }

                // WHY (23 Nov 2026, bug hunt 24): this was the raw grid slot
                // less the board row. A stated roster is never renumbered, so
                // with the slot-3 driver a no-show the cars from slots 4, 5
                // and 6 read "+1" in green having passed nobody, and a ghost
                // row at the top made the pole-sitter read "-1". Both places
                // are now counted among the real cars with a lap, the way the
                // Rating column on the same row counts them.
                if (ctx?.Ordered is null)
                {
                    return GainedValue(s.StartPosition - (index + 1));
                }

                return LiveGridPlaces.PlacesGained(ctx.Ordered, s) is { } gained
                    ? GainedValue(gained)
                    : new KpiValue("-");
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

        // THE DRIVER RATING (18 Oct 2026) - 0 to 150, NASCAR's idea built
        // only from what the timing records. See DriverRating for the
        // weights. Worked out over the whole field for every row, which is
        // a handful of cars a handful of times a second.
        new("rating", "Rating", "Driver rating for this race, 0 to 150: finish, laps led, pace against the race's best lap, the fastest lap, distance and places gained, less 5 a warning. Live until the flag.", 55,
            (s, _, ctx) => DriverRating.ForStandings(ctx.Ordered).TryGetValue(s.TransponderId, out var rating)
                ? new KpiValue(DriverRating.Format(rating), rating >= 120 ? KpiEmphasis.Good : KpiEmphasis.None)
                : new KpiValue("-")),

        new("pbs", "PBs", "How many times this driver has beaten their own best this session.", 40,
            (s, _, ctx) => new KpiValue(ctx.Tracker.PersonalBests(s.TransponderId).ToString())),

        new("penalty", "Penalty", "Manually applied time penalties.", 60,
            // To the tenth when there is one: a 2.5 s penalty rounded to
            // "+3s" (or a 0.4 s jump-start penalty to "+0s") is a number the
            // director cannot check against what was given.
            (s, _, _) => s.PenaltyMs > 0
                ? new KpiValue(Invariant($"+{s.PenaltyMs / 1000.0:0.#}s"), KpiEmphasis.Bad)
                : new KpiValue("-")),

        new("byhand", "By Hand", "Laps race control added by hand this race (+1 Lap), for a transponder that stopped being read.", 55,
            (s, _, _) => HandCountedLaps.During(s) is > 0 and var byHand
                ? new KpiValue($"+{byHand}", KpiEmphasis.Bad)
                : new KpiValue("-")),

        new("warnings", "Warnings", "Warnings from race control this race - and a black flag, once the class's ladder reaches one.", 70,
            (s, _, _) => s.Warnings.Count == 0
                ? new KpiValue("-")
                : new KpiValue(s.IsBlackFlagged ? $"{s.Warnings.Count} \u2691 BLACK" : $"\u26A0 {s.Warnings.Count}", KpiEmphasis.Bad)),

        new("total", "Total Time", "Elapsed race time for this driver.", 75,
            (s, _, _) => new KpiValue(s.ElapsedMs > 0 ? $"{s.ElapsedMs / 60000}:{s.ElapsedMs / 1000 % 60:00}" : "-")),

        // THE RESULT, THE RC WAY (14 Nov 2026, stream overlay plan 6.2): laps
        // and total time in one, "23/5:02.341" - what racers read. More laps
        // wins; on the same laps, the quicker time. The * for laps counted by
        // hand stays on the laps.
        new("result", "Result", "Laps and total time the RC way, 23/5:02.341: more laps wins, and on the same laps the quicker time.", 85,
            (s, _, _) => new KpiValue(ResultText(s))),
    };

    /// <summary>
    /// "23/5:02.341": laps (with a * when some were counted by hand), then the
    /// time from the start to the crossing that finished the last of them.
    /// "1:02:05.120" past an hour. "-" before a car has a lap.
    /// </summary>
    internal static string ResultText(RacerStanding s) =>
        ResultText(s.Laps, s.ElapsedMs, HandCountedLaps.During(s));

    internal static string ResultText(int laps, long elapsedMs, int handCounted = 0)
    {
        if (laps <= 0 || elapsedMs <= 0)
        {
            return "-";
        }

        var t = TimeSpan.FromMilliseconds(elapsedMs);
        var time = t.TotalHours >= 1
            ? Invariant($"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}")
            : Invariant($"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds:000}");

        return HandCountedLaps.LapsText(laps, handCounted) + "/" + time;
    }

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

    /// <summary>"+2" in green, "-1" in red, "=" for no change - the Gained column's cell.</summary>
    private static KpiValue GainedValue(int moved) => moved switch
    {
        > 0 => new KpiValue($"+{moved}", KpiEmphasis.Good),
        < 0 => new KpiValue(moved.ToString(System.Globalization.CultureInfo.InvariantCulture), KpiEmphasis.Bad),
        _ => new KpiValue("="),
    };

    /// <summary>
    /// A number on the board with a decimal point whatever the PC's region
    /// (bug hunt 17), as <see cref="RaceGaps.Format"/> writes the gaps. On a
    /// comma-decimal Windows the lap times read "12,345" beside a gap of
    /// "+2.345" on the same row, on the screens and on the phone board.
    /// </summary>
    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private static string Secs(long ms) => ms > 0 ? Invariant($"{ms / 1000.0:0.000}") : "-";

    /// <summary>Feet a second to miles an hour.</summary>
    private const double MphPerFootPerSecond = 3600.0 / 5280.0;

    /// <summary>A speed on the board: "23.4".</summary>
    private static string Mph(double mph) => mph.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The average speed in mph through a sector of this length, taken in this time - or null.</summary>
    public static double? SectorMph(double feet, long ms) => feet > 0 && ms > 0 ? feet / (ms / 1000.0) * MphPerFootPerSecond : null;

    /// <summary>The car's highest average speed through any sector this session, in mph, or null.</summary>
    private static double? TopSpeed(RacerStanding s, double[]? lengths)
    {
        if (lengths is null)
        {
            return null;
        }

        double? top = null;

        foreach (var (_, sectors) in Sectors.LapsOf(s))
        {
            if (sectors.Length != lengths.Length)
            {
                continue;
            }

            for (var i = 0; i < sectors.Length; i++)
            {
                if (SectorMph(lengths[i], sectors[i]) is { } mph && (top is null || mph > top))
                {
                    top = mph;
                }
            }
        }

        return top;
    }

    /// <summary>
    /// The average speed through a sector on the last lap, in mph, from its
    /// length on the layout's plan. Coloured as the sector's time is.
    /// </summary>
    private static KpiDefinition SpeedKpi(int sector) => new(
        $"v{sector}",
        $"S{sector} mph",
        $"Average speed through sector {sector} on the last lap, in mph. Needs the split loops placed on the layout's plan in the Track Designer.",
        50,
        (s, index, ctx) =>
        {
            if (ctx.SectorLengthsFeet is not { } lengths
                || Sectors.Last(s) is not { } last
                || last.Length != lengths.Length
                || last.Length < sector
                || SectorMph(lengths[sector - 1], last[sector - 1]) is not { } mph)
            {
                return new KpiValue("-");
            }

            // The same colour as the sector's time: quickest of anyone, or their own best.
            var timeEmphasis = SectorKpi(sector).Compute(s, index, ctx).Emphasis;
            return new KpiValue(Mph(mph), timeEmphasis);
        });

    /// <summary>
    /// A sector of the last lap. Highlighted when it is the quickest anyone
    /// has gone through that sector this session, green when it is the
    /// driver's own best there - the colours a timing screen uses.
    /// </summary>
    private static KpiDefinition SectorKpi(int sector) => new(
        $"s{sector}",
        $"S{sector}",
        $"Sector {sector} of the last lap: from {(sector == 1 ? "the line" : $"split {sector - 1}")} to the next timing point. "
        + "Highlighted when it's the quickest of anyone this session, green when it's the driver's own best. Needs a split loop.",
        50,
        (s, _, ctx) =>
        {
            if (Sectors.Last(s) is not { } last || last.Length < sector)
            {
                return new KpiValue("-");
            }

            var ms = last[sector - 1];
            var emphasis = KpiEmphasis.None;

            if (ctx.SessionSectorBests(last.Length) is { } session && ms == session[sector - 1])
            {
                emphasis = KpiEmphasis.Highlight;
            }
            else if (Sectors.LapsOf(s).Skip(1).Any() && Sectors.Bests(s) is { } own && own.Length == last.Length && ms == own[sector - 1])
            {
                // Not on their first lap with sectors, which is a best by default.
                emphasis = KpiEmphasis.Good;
            }

            return new KpiValue(Sectors.Format(ms), emphasis);
        });

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

/// <summary>
/// Places made up or lost since the grid, for the live Gained column.
/// </summary>
/// <remarks>
/// WHY (23 Nov 2026, bug hunt 24): the column took the raw grid slot less
/// the row on the board. A stated roster is never renumbered over the cars
/// that started, and the board has no row for a no-show but does have one
/// for a ghost, so drivers who had passed nobody read "+1" and a pole-sitter
/// leading every real car read "-1". Both places are counted here among the
/// real cars with a lap - DriverRating.ForStandings' field - so the column
/// agrees with the Rating beside it and the phone's "Gained N places" after.
/// </remarks>
public static class LiveGridPlaces
{
    /// <summary>
    /// Grid place less current place, both among the real cars in
    /// <paramref name="ordered"/> (board order) with a lap; null for a ghost,
    /// a car with no lap yet, or one whose grid slot was never known.
    /// </summary>
    public static int? PlacesGained(IReadOnlyList<RacerStanding> ordered, RacerStanding car)
    {
        if (car is null
            || string.IsNullOrWhiteSpace(car.TransponderId)
            || GhostService.IsGhost(car.TransponderId)
            || car.Laps < 1
            || car.StartPosition <= 0)
        {
            return null;
        }

        var real = (ordered ?? Array.Empty<RacerStanding>())
            .Where(s => s is not null
                && !string.IsNullOrWhiteSpace(s.TransponderId)
                && !GhostService.IsGhost(s.TransponderId)
                && s.Laps >= 1)
            .ToList();

        var place = real.FindIndex(s => string.Equals(s.TransponderId, car.TransponderId, StringComparison.OrdinalIgnoreCase));

        if (place < 0)
        {
            return null;
        }

        var gridPlace = 1 + real.Count(s => s.StartPosition > 0
            && s.StartPosition < car.StartPosition
            && !string.Equals(s.TransponderId, car.TransponderId, StringComparison.OrdinalIgnoreCase));

        return gridPlace - (place + 1);
    }
}
