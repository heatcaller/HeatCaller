namespace HeatCaller;

/// <summary>
/// Race Analytics' engineer: what the numbers mean, said in a sentence, and
/// one thing to try next heat.
///
/// 13 Nov 2026, the rework Asher picked from four mock-ups
/// (plans/race-analytics-rework-mockup.html): C, "The Race Engineer", with
/// A's car lengths beside the times, and D's who-beats-whom grid for Head to
/// Head. Every tab now opens with what the figures add up to, then the one
/// chart that shows it, then something to do. The figures stay, in a
/// supporting role.
///
/// Car lengths are the second unit because a driver cannot feel a tenth of
/// a second, and can feel a car length. They need the layout's length (the
/// Track Layouts page already keeps one), and say nothing without it rather
/// than guessing a track.
///
/// Everything here is worked out from saved lap times, so it reads every
/// session in the history, not only races run from now on.
/// </summary>
public static partial class RaceCoach
{
    /// <summary>
    /// A car, nose to tail, in feet: a 1/10 buggy or touring car is about
    /// 18 inches. Smaller scales read long - a Mini-Z gap in "1/10 lengths"
    /// - which is still the right way round, just generous.
    /// </summary>
    public const double CarLengthFeet = 1.5;

    /// <summary>
    /// A lap this far off the driver's own best is a mistake - a spin, a
    /// tangle, a marshal - not the ordinary drift of a race. 4% is about
    /// half a second on a 14-second lap: more than any line or traffic
    /// costs, less than any crash.
    /// </summary>
    public const double MistakeShare = 0.04;

    /// <summary>
    /// The share of the time a driver lost that has to have gone in the
    /// mistakes before the mistakes are the story.
    /// </summary>
    public const double MistakesAreTheStory = 0.30;

    /// <summary>
    /// The car lengths a gap is worth on a lap of a layout, or null when the
    /// layout's length is not set (or there is no gap to measure).
    /// </summary>
    public static double? CarLengths(long gapMs, long lapMs, double? trackFeet)
    {
        if (trackFeet is not double feet || feet <= 0 || lapMs <= 0 || gapMs <= 0)
        {
            return null;
        }

        return gapMs / (double)lapMs * feet / CarLengthFeet;
    }

    /// <summary>"7 car lengths", "1 car length", "under a car length".</summary>
    public static string Lengths(double lengths) =>
        lengths < 0.75 ? "under a car length"
        : lengths < 1.5 ? "1 car length"
        : $"{Math.Round(lengths):0} car lengths";

    /// <summary>A gap as car lengths, or null when the layout's length is not set.</summary>
    public static string? SayLengths(long gapMs, long lapMs, double? trackFeet) =>
        CarLengths(gapMs, lapMs, trackFeet) is double lengths ? Lengths(lengths) : null;

    /// <summary>
    /// A layout's length in feet, found by the name a session saved, or
    /// null when there is no such layout or it has no length set.
    /// </summary>
    public static double? TrackFeet(TrackLayoutStore? layouts, string? layoutName)
    {
        if (layouts is null || string.IsNullOrWhiteSpace(layoutName))
        {
            return null;
        }

        var layout = layouts.All.FirstOrDefault(t =>
            string.Equals(t.Name.Trim(), layoutName.Trim(), StringComparison.OrdinalIgnoreCase));

        return layout?.TrackLengthFeet is double feet && feet > 0 ? feet : null;
    }

    /// <summary>The laps a driver counted in a race, as PaceAnalysis counts them - the run to the first crossing left out.</summary>
    public static List<long> Laps(SessionResultEntry entry) => PaceAnalysis.CountedLapsOf(entry);

    /// <summary>
    /// Where in <paramref name="laps"/> the mistakes are: laps more than
    /// <see cref="MistakeShare"/> off the quickest of them that also stand
    /// out from the laps either side. Zero-based.
    /// </summary>
    public static List<int> MistakeLaps(IReadOnlyList<long> laps)
    {
        var found = new List<int>();

        if (laps.Count == 0)
        {
            return found;
        }

        var best = laps.Min();
        var limit = best + (long)(best * MistakeShare);

        for (var i = 0; i < laps.Count; i++)
        {
            if (laps[i] <= limit)
            {
                continue;
            }

            // And it has to stand out from the laps around it: 2% over the
            // middle of the five centred on it. Without this a driver who
            // warmed up slowly had every opening lap called a mistake - they
            // are off the best, but each is about the one before it. Five
            // laps rather than the two neighbours so that a crash that
            // spills into a second lap is two mistakes, not none.
            var around = laps.Skip(Math.Max(0, i - 2)).Take(Math.Min(i, 2) + 3).OrderBy(l => l).ToList();
            var middle = around[around.Count / 2];

            if (laps[i] > middle + middle / 50)
            {
                found.Add(i);
            }
        }

        return found;
    }

    /// <summary>What the mistakes cost: each one's time over the driver's best lap, added up.</summary>
    public static long MistakeCostMs(IReadOnlyList<long> laps)
    {
        if (laps.Count == 0)
        {
            return 0;
        }

        var best = laps.Min();

        return MistakeLaps(laps).Sum(i => laps[i] - best);
    }

    /// <summary>
    /// What a driver's race adds up to, in a sentence about them (no names,
    /// no pronouns, so it reads under any heading).
    ///
    /// PaceAnalysis.Row.Verdict's reading, with one rule in front of it: a
    /// race where the mistakes are most of the time lost is a race about the
    /// mistakes, whatever the spread says. A single crash lap lifts the
    /// spread just enough to read "solid and even", which was the wrong
    /// thing to tell the driver who crashed.
    ///
    /// A lap is named by the number the charts and the leaderboard give it,
    /// which count the start pass as lap 1: <paramref name="lapNumbers"/>
    /// (RaceCoach.LapNumbers) when the caller has the entry, otherwise the
    /// usual case of the first counted lap being lap 2.
    /// </summary>
    public static string Headline(PaceAnalysis.Row row, IReadOnlyList<long> laps, IReadOnlyList<int>? lapNumbers = null)
    {
        if (row.Thin || laps.Count == 0)
        {
            return row.Verdict;
        }

        var mistakes = MistakeLaps(laps);
        var cost = MistakeCostMs(laps);

        if (mistakes.Count > 0 && row.PotentialGainMs > 0 && cost >= row.PotentialGainMs * MistakesAreTheStory)
        {
            var share = Share(cost / (double)row.PotentialGainMs);

            return mistakes.Count == 1
                ? $"Fast, but one mistake (lap {(lapNumbers is { } numbers && mistakes[0] < numbers.Count ? numbers[mistakes[0]] : mistakes[0] + 2)}) cost {share} of the time lost."
                : $"Quick laps, but {mistakes.Count} mistakes cost {share} of the time lost.";
        }

        return row.Verdict;
    }

    /// <summary>
    /// One thing to try next heat, picked by what the race says.
    ///
    /// Said to the driver ("you"): it is advice for whoever is reading
    /// their own race. Always something to do on the track, never a grade.
    /// </summary>
    public static string Drill(PaceAnalysis.Row row, IReadOnlyList<long> laps)
    {
        if (row.Thin || laps.Count == 0)
        {
            return "Run a full heat next time - three laps in a row is the least there is to read.";
        }

        var mistakes = MistakeLaps(laps);
        var cost = MistakeCostMs(laps);
        var best = laps.Min();

        if (mistakes.Count > 0 && row.PotentialGainMs > 0 && cost >= row.PotentialGainMs * MistakesAreTheStory)
        {
            // Did a mistake come straight after a lap within 1% of the best?
            // That is pushing on after a quick one - the commonest crash.
            var afterQuick = mistakes.Any(i =>
                Enumerable.Range(Math.Max(0, i - 2), Math.Min(2, i)).Any(j => laps[j] <= best + best / 100));

            return afterQuick
                ? "A mistake came right after one of your quickest laps. When you set a best, drive the next lap at 99% to settle."
                : "Find the corner where the mistakes happen and take it a little wider for a whole heat. One clean heat is worth more than a quick lap.";
        }

        var fadeLimit = Math.Max(150, row.BestLapMs / 20);

        if (row.FadeMs >= fadeLimit)
        {
            return $"Check the pack and the tyres: the last third was {row.FadeMs / 1000.0:0.00}s a lap slower than the first.";
        }

        if (row.FadeMs <= -fadeLimit)
        {
            return $"Do two hard laps in practice before the race. You were {-row.FadeMs / 1000.0:0.00}s a lap quicker by the end, so the start is where the time is.";
        }

        if (row.ConsistencyPercent <= 1.5 && row.PotentialGainMs < 1500)
        {
            return "You're at your own limit. The next step is setup or a new line, not trying harder.";
        }

        if (row.ConsistencyPercent >= 4.0)
        {
            return $"Aim for laps within 1.5% of your best ({PaceAnalysis.Time(best + best * 15 / 1000)} or quicker) rather than chasing a new best.";
        }

        return row.BestConsecutiveAverageMs > 0
            ? $"Your best three in a row averaged {PaceAnalysis.Time(row.BestConsecutiveAverageMs)}. Make that the pace for every lap of the next heat."
            : "Aim for the same lap time every lap of the next heat, a little off your best.";
    }

    /// <summary>A share in words: "a third", "half", "most", or a percentage.</summary>
    public static string Share(double share) =>
        share >= 0.9 ? "nearly all"
        : share >= 0.7 ? "most"
        : share >= 0.45 && share <= 0.55 ? "half"
        : share >= 0.3 && share <= 0.37 ? "a third"
        : share >= 0.23 && share <= 0.27 ? "a quarter"
        : $"{share * 100:0}%";
}
