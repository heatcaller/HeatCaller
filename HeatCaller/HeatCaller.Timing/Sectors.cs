namespace HeatCaller;

/// <summary>
/// Reading a car's sector times (RacerStanding.SectorsAtLap): checked
/// against the lap they belong to, their bests, the theoretical best lap,
/// the session's best of each, and the live gap at a split loop.
///
/// A sector is the time between two timing points: the line to Split 1 is
/// S1, Split 1 to Split 2 (or back to the line) is S2, and so on. With one
/// split loop a lap has two sectors.
/// </summary>
public static class Sectors
{
    /// <summary>
    /// The sectors, when they belong to a lap of <paramref name="lapMs"/> -
    /// at least two, every one positive, adding up to the lap exactly - or
    /// null. A lap merged or edited by hand no longer matches the sectors it
    /// had, and shows none rather than wrong ones.
    /// </summary>
    public static long[]? Checked(long[]? sectors, long lapMs) =>
        sectors is { Length: >= 2 } && lapMs > 0 && sectors.All(ms => ms > 0) && sectors.Sum() == lapMs
            ? sectors
            : null;

    /// <summary>
    /// How many sectors a car's laps have now: those of its latest lap with
    /// any, or 0. Laps cut a different way (the split loops changed mid-
    /// session) are left out of everything below.
    /// </summary>
    public static int CountOf(RacerStanding standing)
    {
        for (var i = standing.LapTimesMs.Count - 1; i >= 0; i--)
        {
            if (standing.SectorsOf(i) is { } sectors)
            {
                return sectors.Length;
            }
        }

        return 0;
    }

    /// <summary>The car's laps that have sectors, cut the way its latest is - lap index and sectors.</summary>
    public static IEnumerable<(int Lap, long[] Sectors)> LapsOf(RacerStanding standing)
    {
        var count = CountOf(standing);

        if (count == 0)
        {
            yield break;
        }

        for (var i = 0; i < standing.LapTimesMs.Count; i++)
        {
            if (!standing.IsRecoveryLap(i) && standing.SectorsOf(i) is { } sectors && sectors.Length == count)
            {
                yield return (i, sectors);
            }
        }
    }

    /// <summary>The car's sectors on its last lap, or null.</summary>
    public static long[]? Last(RacerStanding standing) =>
        standing.LapTimesMs.Count == 0 ? null : standing.SectorsOf(standing.LapTimesMs.Count - 1);

    /// <summary>The car's best time in each sector, or null before it has a lap with sectors.</summary>
    public static long[]? Bests(RacerStanding standing)
    {
        long[]? best = null;

        foreach (var (_, sectors) in LapsOf(standing))
        {
            best ??= sectors.ToArray();

            for (var i = 0; i < sectors.Length; i++)
            {
                best[i] = Math.Min(best[i], sectors[i]);
            }
        }

        return best;
    }

    /// <summary>
    /// The car's theoretical best lap: its best sectors added up - what it
    /// could do with every sector at its best on one lap. Null without one.
    /// </summary>
    public static long? TheoreticalBest(RacerStanding standing) => Bests(standing)?.Sum();

    /// <summary>
    /// The quickest time in each sector by anyone in the session (ghosts
    /// left out), for cars cut into <paramref name="count"/> sectors - or
    /// null when nobody has one yet.
    /// </summary>
    public static long[]? SessionBests(IEnumerable<RacerStanding> standings, int count)
    {
        if (count < 2)
        {
            return null;
        }

        long[]? best = null;

        foreach (var standing in standings)
        {
            if (Ghosts.IsGhost(standing.TransponderId) || Bests(standing) is not { } mine || mine.Length != count)
            {
                continue;
            }

            best ??= mine.ToArray();

            for (var i = 0; i < count; i++)
            {
                best[i] = Math.Min(best[i], mine[i]);
            }
        }

        return best;
    }

    /// <summary>
    /// What each sector usually takes this session: the median of every
    /// lap's time through it, over the cars cut into <paramref name="count"/>
    /// sectors (ghosts and start laps left out) - or null with fewer than
    /// three such laps, too few to say what is usual. A misread can't move
    /// a median the way it moves a best.
    /// </summary>
    public static long[]? Typical(IEnumerable<RacerStanding> standings, int count)
    {
        if (count < 2)
        {
            return null;
        }

        var laps = standings
            .Where(s => !Ghosts.IsGhost(s.TransponderId))
            .SelectMany(s => LapsOf(s).Where(l => l.Lap > 0).Select(l => l.Sectors))
            .Where(sectors => sectors.Length == count)
            .ToList();

        if (laps.Count < 3)
        {
            return null;
        }

        var typical = new long[count];

        for (var i = 0; i < count; i++)
        {
            var sorted = laps.Select(l => l[i]).OrderBy(ms => ms).ToList();
            typical[i] = sorted[sorted.Count / 2];
        }

        return typical;
    }

    /// <summary>
    /// The live gap at the split loop the car crossed last on the lap in
    /// progress: how far it is up (negative) or down on its own best lap's
    /// time to that point. The best lap's own sectors when it has them,
    /// otherwise its best sectors. Null before the car has crossed a split
    /// this lap or has anything to compare with.
    /// </summary>
    public static (int Split, long DeltaMs)? LiveDelta(RacerStanding standing)
    {
        var reads = standing.SplitReadsMs;
        var last = -1;

        for (var i = reads.Length - 1; i >= 0; i--)
        {
            if (reads[i].Count > 0)
            {
                last = i;
                break;
            }
        }

        // Nothing read yet this lap, or read twice at one split loop (a lap
        // the line may have missed): no gap worth showing.
        if (last < 0 || reads[last].Count != 1 || standing.LastLapDeviceTimeMs <= 0)
        {
            return null;
        }

        var reference = BestLapSectors(standing) ?? Bests(standing);

        // Cut the same way as this lap: one more sector than split loops.
        if (reference is null || reference.Length != reads.Length + 1)
        {
            return null;
        }

        var toHere = reads[last][0] - standing.LastLapDeviceTimeMs;
        var referenceToHere = reference.Take(last + 1).Sum();

        // A read that belongs to the next lap, arrived before its line
        // crossing did: not a gap on this one.
        if (toHere <= 0 || toHere > reference.Sum())
        {
            return null;
        }

        return (last + 1, toHere - referenceToHere);
    }

    /// <summary>The sectors of the car's best lap, when that lap has them.</summary>
    public static long[]? BestLapSectors(RacerStanding standing)
    {
        if (standing.FastestLapMs <= 0)
        {
            return null;
        }

        foreach (var (lap, sectors) in LapsOf(standing))
        {
            if (standing.LapTimesMs[lap] == standing.FastestLapMs)
            {
                return sectors;
            }
        }

        return null;
    }

    /// <summary>Sector times on one line: "3.41 / 6.02".</summary>
    public static string Line(long[] sectors) => string.Join(" / ", sectors.Select(Format));

    /// <summary>A sector time on the board: seconds to the hundredth - the split loop's clock tie is good to about that, not to the thousandth.</summary>
    public static string Format(long ms) => ms > 0 ? Hundredths(ms) : "-";

    /// <summary>A gap at a split: "-0.08" (up) or "+0.12" (down).</summary>
    public static string FormatDelta(long ms) =>
        (ms < 0 ? "-" : ms > 0 ? "+" : string.Empty) + Hundredths(ms);

    // WHY (23 Nov 2026, bug hunt 24): Format and FormatDelta printed
    // "0.00" on ms / 1000.0, so whether a 12.315 sector read 12.32 (as the
    // voice says it and its caption shows it) or 12.31 hung on how the
    // runtime rounds the double 12.3149999... - the same trap bug hunt 16
    // took the split call out of. Now whole hundredths, half up, from the
    // milliseconds: the arithmetic of RaceNumbers.HundredthsOf (which lives
    // in the app layer, so it is repeated here), and the board, the Big
    // Screen banner and the voice can't disagree by a hundredth.
    private static string Hundredths(long ms)
    {
        var c = (Math.Abs(ms) + 5) / 10;
        return $"{c / 100}.{c % 100:00}";
    }
}
