namespace HeatCaller;

/// <summary>
/// What a driver's lap times say about how they actually drove.
///
/// A result sheet answers who won. It does not answer the question every
/// driver asks straight afterwards, which is some version of "was I quick,
/// or was I just tidy?" - and those are different drivers with different
/// things to work on. Someone whose best lap is half a second clear of the
/// field and whose average is mid-pack is fast and wasteful; someone whose
/// average sits a tenth off their best is driving at their limit and needs
/// more pace, not more discipline. Both finish fourth.
///
/// Everything here comes out of the lap times already saved with a race, so
/// it works on every session in the history rather than only on races run
/// from now on.
///
/// There is deliberately no "theoretical best" of the kind a sector-timed
/// series quotes. That number is the sum of a driver's best sectors, and
/// this timer sees one crossing per lap - there are no sectors to add up.
/// Quoting one anyway would mean inventing it. PotentialGainMs answers the
/// same question with what is actually measured: what perfect repetition of
/// their own best lap would have been worth over the race.
/// </summary>
public static class PaceAnalysis
{
    /// <summary>
    /// How many laps the consecutive-run and quickest-laps figures use.
    ///
    /// Three is the smallest run that cannot be a fluke - one tow, one
    /// lucky line - and short enough that a driver who only completed a
    /// handful of laps still gets a figure. A driver with fewer counted
    /// laps than this gets no figure at all (0, shown as "-"), not an
    /// average of whatever they did manage.
    /// </summary>
    public const int Window = 3;

    /// <summary>One driver's pace, all of it derived from their lap times.</summary>
    public sealed record Row(
        string Transponder,
        string DriverName,
        string CarModel,
        int Position,
        int CountedLaps,
        long BestLapMs,
        long MedianLapMs,
        long AverageLapMs,
        long BestConsecutiveAverageMs,
        long QuickestAverageMs,
        double ConsistencyPercent,
        long FadeMs,
        long PotentialGainMs)
    {
        /// <summary>How far off their own best they ran on an average lap.</summary>
        public long AverageOffBestMs => AverageLapMs > 0 && BestLapMs > 0 ? AverageLapMs - BestLapMs : 0;

        /// <summary>True when there were not enough laps for the figures to mean anything.</summary>
        public bool Thin => CountedLaps < Window;

        /// <summary>
        /// A plain-language read on this driver's race.
        ///
        /// Deliberately about what to do next rather than a grade. "0.8%
        /// consistency" tells a driver nothing they can act on; "you have
        /// the pace, you are losing it in the mistakes" tells them where to
        /// spend the next heat.
        /// </summary>
        public string Verdict
        {
            get
            {
                if (Thin)
                {
                    return "Too few laps to read anything into.";
                }

                var loose = ConsistencyPercent >= 4.0;
                var tight = ConsistencyPercent <= 1.5;

                // A second and a half lost over a race is the point where
                // tidying up beats finding more speed.
                var costly = PotentialGainMs >= 1500;

                var fading = FadeMs >= Math.Max(150, BestLapMs / 20);
                var building = FadeMs <= -Math.Max(150, BestLapMs / 20);

                if (loose && costly)
                {
                    return "The pace is there, the repetition isn't - most of this race went in the mistakes.";
                }

                if (tight && !costly)
                {
                    return "Running at their own limit lap after lap. More pace has to come from the car or the line.";
                }

                if (fading)
                {
                    return "Started better than they finished - fading with the battery, the tyres, or concentration.";
                }

                if (building)
                {
                    return "Got quicker as it went on - either warming up slowly or learning the track.";
                }

                return loose
                    ? "Some scrappy laps in among the good ones."
                    : "Solid and even, with a little left on the table.";
            }
        }
    }

    /// <summary>
    /// Works out every driver's pace in a saved session.
    ///
    /// Ordered by the race result rather than by pace, so this reads
    /// alongside the result sheet - the interesting part is usually how
    /// little the two orders agree.
    /// </summary>
    public static List<Row> For(SessionRecord session) =>
        session.Results
            .OrderBy(r => r.Position == 0 ? int.MaxValue : r.Position)
            .Select(For)
            .ToList();

    /// <summary>One driver's pace.</summary>
    public static Row For(SessionResultEntry entry)
    {
        var laps = CountedLaps(entry.LapTimesMs);

        if (laps.Count == 0)
        {
            return new Row(entry.Transponder, entry.DriverName, entry.CarModel, entry.Position,
                0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var best = laps.Min();
        var average = (long)laps.Average();
        var median = Median(laps);

        // Population rather than sample standard deviation: these are all
        // the laps the driver ran, not a sample drawn from some larger set
        // of laps they might have run.
        var variance = laps.Select(l => (l - (double)average) * (l - (double)average)).Average();
        var consistency = average > 0 ? Math.Sqrt(variance) / average * 100.0 : 0;

        return new Row(
            entry.Transponder,
            entry.DriverName,
            entry.CarModel,
            entry.Position,
            laps.Count,
            best,
            median,
            average,
            BestConsecutiveAverage(laps, Window),
            QuickestAverage(laps, Window),
            consistency,
            Fade(laps),
            PotentialGain(laps, best));
    }

    /// <summary>
    /// The laps that count as laps.
    ///
    /// The first entry is not one. It runs from the green flag to the car's
    /// first crossing, which is a second or two if the grid is right behind
    /// the line and most of a lap if it isn't - the session itself already
    /// bars it from setting a best lap for exactly this reason, and letting
    /// it into an average would drag every figure here off by however far
    /// back the car was parked.
    /// </summary>
    private static List<long> CountedLaps(List<long> lapTimesMs) =>
        lapTimesMs.Count <= 1
            ? new List<long>()
            : lapTimesMs.Skip(1).Where(l => l > 0).ToList();

    private static long Median(List<long> laps)
    {
        var sorted = laps.OrderBy(l => l).ToList();
        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>
    /// The best unbroken run of <paramref name="window"/> laps, averaged.
    ///
    /// Unbroken is the point: it is a claim about what the driver could
    /// actually sustain, which the quickest-laps average below deliberately
    /// is not.
    /// </summary>
    public static long BestConsecutiveAverage(List<long> laps, int window)
    {
        if (laps.Count == 0)
        {
            return 0;
        }

        // Fewer laps than the window is no run at all. This used to return
        // the plain average of the one or two laps there were, which then
        // printed under "Best 3 in a row" and - being the pace table's sort
        // key - put a driver who crashed out after two flying laps at the
        // top of it. QualifyingCalculator.BestConsecutiveWindow already
        // says "no window" for the same laps; this now agrees with it.
        if (laps.Count < window)
        {
            return 0;
        }

        var running = 0L;

        for (var i = 0; i < window; i++)
        {
            running += laps[i];
        }

        var best = running;

        for (var i = window; i < laps.Count; i++)
        {
            running += laps[i] - laps[i - window];
            best = Math.Min(best, running);
        }

        return best / window;
    }

    /// <summary>
    /// The quickest few laps averaged, wherever in the race they came.
    ///
    /// Paired with the consecutive figure on purpose: when the two are
    /// close the driver strung their good laps together, and when the
    /// quickest average is far better than the consecutive one their speed
    /// came in isolated flashes with something going wrong in between.
    /// That gap is the most useful single number here.
    /// </summary>
    public static long QuickestAverage(List<long> laps, int window)
    {
        // Same rule as the consecutive figure: "Best 3 any" from two laps
        // used to be a two-lap mean under a three-lap heading.
        if (laps.Count < window)
        {
            return 0;
        }

        return (long)laps.OrderBy(l => l).Take(window).Average();
    }

    /// <summary>
    /// How much slower the last third was than the first.
    ///
    /// Thirds rather than first-half against second-half: the middle of a
    /// race is where the traffic and the scraps are, and including it in
    /// both halves buries the trend it is meant to show. Negative means
    /// they got quicker.
    /// </summary>
    private static long Fade(List<long> laps)
    {
        if (laps.Count < 6)
        {
            return 0;
        }

        var third = laps.Count / 3;

        var opening = laps.Take(third).Average();
        var closing = laps.Skip(laps.Count - third).Average();

        return (long)(closing - opening);
    }

    /// <summary>
    /// What repeating their own best lap every lap would have been worth.
    ///
    /// The honest version of a theoretical best for a timer that sees one
    /// crossing a lap. It is not a time anyone was ever going to run - it
    /// is the size of the gap between a driver's best and their normal,
    /// expressed as the seconds it cost them over this particular race,
    /// which is the form in which it is worth arguing about.
    /// </summary>
    private static long PotentialGain(List<long> laps, long best) =>
        best <= 0 ? 0 : laps.Sum(l => Math.Max(0, l - best));

    /// <summary>
    /// Formats a duration the way the rest of the app does - seconds to
    /// three places under a minute, minutes and seconds above it.
    /// </summary>
    public static string Time(long ms)
    {
        if (ms <= 0)
        {
            return "-";
        }

        var span = TimeSpan.FromMilliseconds(ms);

        return span.TotalMinutes >= 1
            ? $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds:000}"
            : $"{span.TotalSeconds:0.000}";
    }

    /// <summary>A signed gap, so a driver who got quicker reads as quicker.</summary>
    public static string Delta(long ms) =>
        ms == 0 ? "-" : (ms > 0 ? $"+{ms / 1000.0:0.00}s" : $"{ms / 1000.0:0.00}s");
}
