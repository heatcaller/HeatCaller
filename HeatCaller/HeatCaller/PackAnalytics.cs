namespace HeatCaller;

/// <summary>What a pack report looks at: one track layout, one kind of session, races since a date - each null for all of them.</summary>
public sealed record PackFilter(string? Layout = null, string? SessionType = null, DateTime? SinceUtc = null);

/// <summary>
/// One race a driver ran on a pack, in seconds. Laps is the clean laps
/// counted; MedianLap, Spread, Fade and Profile are zero / empty when the
/// run had fewer than three of them (it then counts only for the pack's
/// races and best lap).
/// </summary>
public sealed record PackRun(
    string SessionId,
    DateTime StartedUtc,
    string Layout,
    string Label,
    int Laps,
    double BestLap,
    double MedianLap,
    double Spread,
    double Fade,
    IReadOnlyList<double> Profile);

/// <summary>
/// One pack's numbers for one driver, in seconds. See PackAnalytics.For.
/// MedianLap is zero, and Profile empty, when no race on it had enough
/// clean laps to read.
/// </summary>
public sealed record PackStats(
    BatteryPack Pack,
    int Sessions,
    int Laps,
    double BestLap,
    double MedianLap,
    double Spread,
    double Fade,
    double GapToBestPack,
    bool Enough,
    IReadOnlyList<PackRun> Runs,
    IReadOnlyList<double> Profile);

/// <summary>The layouts a driver has raced their packs on (newest first), and each pack's numbers under the filter.</summary>
public sealed record PackReport(IReadOnlyList<string> Layouts, IReadOnlyList<PackStats> Packs);

/// <summary>
/// How a driver's battery packs compare: pace, consistency and fade across
/// a race, per pack - for the Batteries tab's list and chart, and for the
/// relay (contract: PackStat).
///
/// Works from the laps as raced (a correction made afterwards changes the
/// result, not what the pack did), leaving out the start lap - the run from
/// the green to the first crossing, not a lap (PaceAnalysis.CountedLaps) -
/// any lap of zero or less, and any lap more than half as long again as
/// that race's median: a crash, a marshal's wait, a stop in the pits. Those
/// say nothing about the pack, and one of them would swamp a race's fade.
///
/// Scoped to one driver, as BatteryPackStore.ComparePace is: a pack's pace
/// across different people says more about who used it than about the pack.
/// Demo and imported races never count (SessionRecord.IsNotScored).
/// </summary>
public static class PackAnalytics
{
    /// <summary>Races on a pack before its numbers are worth trusting.</summary>
    public const int EnoughSessions = 3;

    /// <summary>The tenths a race is cut into for its fade profile.</summary>
    public const int ProfileBuckets = 10;

    /// <summary>The fewest clean laps a race needs for its median, spread, fade and profile.</summary>
    private const int LeastLaps = 3;

    /// <summary>A lap more than this many times the race's median is not racing.</summary>
    private const double OutlierFactor = 1.5;

    /// <summary>
    /// The report for one driver (any of their transponders, through
    /// BatteryPackStore.DriverKey), over every scored session they ran on a
    /// pack still in the store. One pass over the history.
    /// </summary>
    public static PackReport For(string driverKey, BatteryPackStore store, SessionHistoryStore history, ProfileStore? profiles, PackFilter? filter = null)
    {
        var key = BatteryPackStore.DriverKey(profiles, driverKey);

        if (key.Length == 0)
        {
            return new PackReport(Array.Empty<string>(), Array.Empty<PackStats>());
        }

        // A transponder's key is looked up once, however many races it is in.
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string KeyOf(string transponder)
        {
            if (!keys.TryGetValue(transponder, out var k))
            {
                k = BatteryPackStore.DriverKey(profiles, transponder);
                keys[transponder] = k;
            }

            return k;
        }

        var layouts = new List<string>();
        var seenLayouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runsByPack = new Dictionary<string, List<(PackRun Run, List<double> Clean)>>(StringComparer.OrdinalIgnoreCase);
        var packs = new Dictionary<string, BatteryPack?>(StringComparer.OrdinalIgnoreCase);

        // Newest first, as the history lists them.
        foreach (var session in history.All)
        {
            if (session.IsNotScored() || store.PackIdFor(session.Id, key) is not { } packId)
            {
                continue;
            }

            if (!packs.TryGetValue(packId, out var pack))
            {
                pack = store.Get(packId);
                packs[packId] = pack;
            }

            if (pack is null)
            {
                continue;
            }

            var result = session.Results.FirstOrDefault(r => string.Equals(KeyOf(r.Transponder), key, StringComparison.OrdinalIgnoreCase));

            if (result is null)
            {
                continue;
            }

            var layout = session.TrackLayoutName?.Trim() ?? string.Empty;

            if (layout.Length > 0 && seenLayouts.Add(layout))
            {
                layouts.Add(layout);
            }

            if (!Matches(session, layout, filter) || RunOf(session, layout, result) is not { } run)
            {
                continue;
            }

            if (!runsByPack.TryGetValue(pack.Id, out var list))
            {
                list = new List<(PackRun, List<double>)>();
                runsByPack[pack.Id] = list;
            }

            list.Add(run);
        }

        var stats = runsByPack
            .Select(kv => PackOf(packs[kv.Key]!, kv.Value))
            .ToList();

        // The best pack's median: among the packs with enough races, or among
        // them all when none has enough yet.
        var withMedian = stats.Where(s => s.MedianLap > 0).ToList();
        var trusted = withMedian.Where(s => s.Enough).ToList();
        var reference = trusted.Count > 0 ? trusted.Min(s => s.MedianLap) : withMedian.Count > 0 ? withMedian.Min(s => s.MedianLap) : 0;

        var ordered = stats
            .Select(s => s with { GapToBestPack = s.MedianLap > 0 && reference > 0 ? Round(s.MedianLap - reference) : 0 })
            .OrderBy(s => s.MedianLap > 0 ? 0 : 1)
            .ThenBy(s => s.MedianLap > 0 ? s.MedianLap : s.BestLap)
            .ThenBy(s => s.Pack.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PackReport(layouts, ordered);
    }

    private static bool Matches(SessionRecord session, string layout, PackFilter? filter)
    {
        if (filter is null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filter.Layout) && !string.Equals(layout, filter.Layout.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.SessionType)
            && !string.Equals(session.SessionType.ToString(), filter.SessionType.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return filter.SinceUtc is not { } since || session.StartedAtUtc >= since;
    }

    /// <summary>One race's numbers, or null when it has no lap to go on at all.</summary>
    private static (PackRun Run, List<double> Clean)? RunOf(SessionRecord session, string layout, SessionResultEntry result)
    {
        var raced = result.AsRaced is { LapTimesMs.Count: > 0 } asRaced ? asRaced.LapTimesMs : result.LapTimesMs;
        var clean = CleanLaps(raced);

        var recordedBest = result.AsRaced is { FastLapMs: > 0 } r ? r.FastLapMs : result.FastLapMs;
        var best = clean.Count > 0 ? clean.Min() : recordedBest > 0 ? recordedBest / 1000.0 : 0;

        if (best <= 0)
        {
            return null;
        }

        var label = string.IsNullOrWhiteSpace(session.Label) ? string.Empty : session.Label;

        if (clean.Count < LeastLaps)
        {
            return (new PackRun(session.Id, session.StartedAtUtc, layout, label, clean.Count, Round(best),
                0, 0, 0, Array.Empty<double>()), new List<double>());
        }

        var median = Median(clean);
        var third = clean.Count / 3;
        var fade = Median(clean.Skip(clean.Count - third).ToList()) - Median(clean.Take(third).ToList());

        return (new PackRun(session.Id, session.StartedAtUtc, layout, label, clean.Count, Round(best),
            Round(median), Round(StdDev(clean)), Round(fade), ProfileOf(clean, median)), clean);
    }

    /// <summary>The laps as raced in seconds: no start lap, nothing of zero or less, nothing past half as long again as the median.</summary>
    private static List<double> CleanLaps(List<long> lapTimesMs)
    {
        var laps = lapTimesMs.Skip(1).Where(ms => ms > 0).Select(ms => ms / 1000.0).ToList();

        if (laps.Count == 0)
        {
            return laps;
        }

        var limit = Median(laps) * OutlierFactor;

        return laps.Where(l => l <= limit).ToList();
    }

    /// <summary>
    /// Each tenth of the race's median minus the race's median. With fewer
    /// laps than tenths, an empty tenth takes the lap nearest to it.
    /// </summary>
    private static IReadOnlyList<double> ProfileOf(List<double> laps, double median)
    {
        var n = laps.Count;
        var buckets = new List<double>[ProfileBuckets];

        for (var b = 0; b < ProfileBuckets; b++)
        {
            buckets[b] = new List<double>();
        }

        for (var i = 0; i < n; i++)
        {
            buckets[Math.Min(ProfileBuckets - 1, i * ProfileBuckets / n)].Add(laps[i]);
        }

        var profile = new double[ProfileBuckets];

        for (var b = 0; b < ProfileBuckets; b++)
        {
            if (buckets[b].Count == 0)
            {
                var nearest = Math.Clamp((int)((b + 0.5) * n / ProfileBuckets), 0, n - 1);
                buckets[b].Add(laps[nearest]);
            }

            profile[b] = Round(Median(buckets[b]) - median);
        }

        return profile;
    }

    private static PackStats PackOf(BatteryPack pack, List<(PackRun Run, List<double> Clean)> newestFirst)
    {
        var runs = newestFirst.AsEnumerable().Reverse().ToList();
        var read = runs.Where(r => r.Clean.Count >= LeastLaps).ToList();
        var allLaps = read.SelectMany(r => r.Clean).ToList();

        var profile = read.Count == 0
            ? Array.Empty<double>()
            : Enumerable.Range(0, ProfileBuckets).Select(b => Round(read.Average(r => r.Run.Profile[b]))).ToArray();

        return new PackStats(
            pack,
            runs.Count,
            allLaps.Count,
            Round(runs.Min(r => r.Run.BestLap)),
            allLaps.Count == 0 ? 0 : Round(Median(allLaps)),
            read.Count == 0 ? 0 : Round(read.Average(r => r.Run.Spread)),
            read.Count == 0 ? 0 : Round(read.Average(r => r.Run.Fade)),
            0,
            runs.Count >= EnoughSessions,
            runs.Select(r => r.Run).ToList(),
            profile);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>Population standard deviation.</summary>
    private static double StdDev(List<double> values)
    {
        var mean = values.Average();

        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }

    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
