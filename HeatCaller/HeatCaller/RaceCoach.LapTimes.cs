namespace HeatCaller;

/// <summary>
/// The Lap Times tab's reading of one driver's race (13 Nov 2026, the Race
/// Engineer rework - see RaceCoach): where the fade starts, the run of laps
/// at the driver's limit, the headline sentence, and the three things the
/// card beside the chart says.
///
/// Everything here takes the counted laps (RaceCoach.Laps), indexed from
/// zero, and the lap numbers the chart gives them (LapNumbers), so a lap
/// called "lap 9" in a sentence is the lap above 9 on the chart.
/// </summary>
public static partial class RaceCoach
{
    /// <summary>
    /// A lap this close to the driver's best is "at their limit" - the band
    /// the chart shades. The Pace tab's "tight" line, 1.5%.
    /// </summary>
    public const double LimitShare = 0.015;

    /// <summary>The fewest clean laps a fade is looked for in: fewer, and one slow lap reads as a fade.</summary>
    public const int FadeLeastLaps = 8;

    /// <summary>
    /// A fade: from <see cref="Start"/> (a counted lap) to the flag, the laps
    /// ran <see cref="PerLapMs"/> a lap slower than the clean laps before
    /// it, <see cref="LostMs"/> in all.
    /// </summary>
    public sealed record Fade(int Start, long PerLapMs, long LostMs);

    /// <summary>One of the "Three things in this race": a line in bold and a detail under it.</summary>
    public sealed record Finding(string Line, string Detail);

    /// <summary>
    /// The lap number the chart gives each counted lap: its place in the
    /// saved laps plus one, which is the number the leaderboard and the lap
    /// windows give it (they count the start pass as lap 1). Counted laps
    /// leave out the start and any lap of zero, so the two are not simply
    /// one apart.
    /// </summary>
    public static List<int> LapNumbers(SessionResultEntry entry)
    {
        var numbers = new List<int>();

        if (entry.LapTimesMs.Count <= 1)
        {
            return numbers;
        }

        for (var i = entry.StartSlots(entry.LapTimesMs); i < entry.LapTimesMs.Count; i++)
        {
            if (entry.LapTimesMs[i] > 0)
            {
                numbers.Add(i + 1);
            }
        }

        return numbers;
    }

    /// <summary>
    /// The least slowing that counts as a fade: 75 ms a lap, or 0.5% of the
    /// best on a long lap. Under that is the drift of any race; the Pace
    /// tab's 5% rule is for "fading" as a whole verdict, and was too coarse
    /// to put a mark on the chart where the tenths started going.
    /// </summary>
    private static long FadeLeastMs(long best) => Math.Max(75, best / 200);

    /// <summary>
    /// Where a driver's pace dropped away and stayed down to the flag, or
    /// null when it did not.
    ///
    /// Read from the clean laps only (mistakes left out, so one crash late
    /// on is not a fade): the race is split in two at every lap from the
    /// fourth clean lap to the third from last, and the split where the two
    /// halves each sit closest to their own average is where the pace
    /// changed. It is a fade when the second half is slower, by at least
    /// <see cref="FadeLeastMs"/> a lap, and the last laps are still that
    /// much slower than the laps before them.
    /// </summary>
    public static Fade? FadeOf(IReadOnlyList<long> laps)
    {
        var mistakes = MistakeLaps(laps).ToHashSet();
        var clean = Enumerable.Range(0, laps.Count).Where(i => !mistakes.Contains(i)).ToList();

        if (clean.Count < FadeLeastLaps)
        {
            return null;
        }

        var times = clean.Select(i => (double)laps[i]).ToList();
        (int Split, double Before, double After, double Spread)? found = null;

        for (var k = 3; k <= times.Count - 3; k++)
        {
            var before = times.Take(k).Average();
            var after = times.Skip(k).Average();

            if (after <= before)
            {
                continue;
            }

            var spread = times.Take(k).Sum(t => (t - before) * (t - before))
                + times.Skip(k).Sum(t => (t - after) * (t - after));

            if (found is null || spread < found.Value.Spread)
            {
                found = (k, before, after, spread);
            }
        }

        if (found is not { } split)
        {
            return null;
        }

        var perLap = (long)Math.Round(split.After - split.Before);

        if (perLap < FadeLeastMs(laps.Min()))
        {
            return null;
        }

        // WHY (23 Nov 2026, bug hunt 24): a fade is pace that "stayed down to
        // the flag", but the old rule only asked that the laps after the
        // split averaged slower than the laps before it. A race that slumped
        // in the middle and ended on its quickest laps was told "quickest at
        // the end ... then you faded", got a "Fade starts" mark, and the card
        // beside it said "You finished strong". The last laps must now be
        // slower than the laps before them, counted as ThreeThings counts
        // them, so the two can never disagree.
        var lastCount = Math.Min(5, times.Count / 3);
        var tail = times.Skip(times.Count - lastCount).Average();
        var rest = times.Take(times.Count - lastCount).Average();

        if (tail - rest < FadeLeastMs(laps.Min()))
        {
            return null;
        }

        var lost = (long)Math.Round(times.Skip(split.Split).Sum(t => t - split.Before));

        return new Fade(clean[split.Split], perLap, lost);
    }

    /// <summary>
    /// The quickest three laps in a row: where they start, and their
    /// average. Null with fewer than three laps.
    /// </summary>
    public static (int First, long AverageMs)? BestThree(IReadOnlyList<long> laps)
    {
        (int First, long AverageMs)? best = null;

        for (var i = 0; i + PaceAnalysis.Window <= laps.Count; i++)
        {
            var average = (long)Math.Round(laps.Skip(i).Take(PaceAnalysis.Window).Average());

            if (best is null || average < best.Value.AverageMs)
            {
                best = (i, average);
            }
        }

        return best;
    }

    /// <summary>
    /// The longest run of laps in a row all within <see cref="LimitShare"/>
    /// of the best - at least three of them, or null. Of two as long, the
    /// quicker.
    /// </summary>
    public static (int First, int Last)? LimitRun(IReadOnlyList<long> laps)
    {
        if (laps.Count == 0)
        {
            return null;
        }

        var limit = laps.Min() * (1 + LimitShare);
        (int First, int Last, double Average)? best = null;
        var start = -1;

        for (var i = 0; i <= laps.Count; i++)
        {
            var inside = i < laps.Count && laps[i] <= limit;

            if (inside && start < 0)
            {
                start = i;
            }
            else if (!inside && start >= 0)
            {
                var length = i - start;
                var average = laps.Skip(start).Take(length).Average();

                if (length >= PaceAnalysis.Window
                    && (best is null
                        || length > best.Value.Last - best.Value.First + 1
                        || length == best.Value.Last - best.Value.First + 1 && average < best.Value.Average))
                {
                    best = (start, i - 1, average);
                }

                start = -1;
            }
        }

        return best is { } run ? (run.First, run.Last) : null;
    }

    /// <summary>
    /// The Lap Times tab's headline, said to the driver: where they were
    /// quick, the mistakes, and how it ended. "Quick through the middle,
    /// one big mistake, then you faded by about a tenth a lap."
    ///
    /// Its own sentence rather than <see cref="Headline"/>: that one is
    /// written to read under any heading, and this one sits over the chart
    /// that shows each part of it - the marks on the chart are the same
    /// three things.
    /// </summary>
    public static string LapTimesHeadline(PaceAnalysis.Row row, IReadOnlyList<long> laps)
    {
        if (row.Thin || laps.Count < PaceAnalysis.Window || BestThree(laps) is not { } three)
        {
            return row.Verdict;
        }

        // Where the quickest three sat in the race, by their middle lap: the
        // first quarter is the start and the last quarter the end. Thirds
        // called a best run on laps 6 to 8 of 20 "from the start", which is
        // not how anyone who drove it would put it.
        var at = (three.First + 1.0) / Math.Max(1, laps.Count - 1);
        var part = at < 0.25 ? 0 : at > 0.75 ? 2 : 1;

        var opening = part switch
        {
            0 => "Quick from the start",
            1 => "Quick through the middle",
            _ => "Slow to get going, quickest at the end",
        };

        var mistakes = MistakeLaps(laps).Count switch
        {
            0 => "no big mistakes",
            1 => "one big mistake",
            2 => "two mistakes",
            var n => $"{n} mistakes",
        };

        var fade = FadeOf(laps);

        var ending = fade is not null
            ? $"then you faded by {SayPerLap(fade.PerLapMs)} a lap"
            : part == 2 ? "and the pace held to the flag"
            : "and you held your pace to the end";

        return $"{opening}, {mistakes}, {ending}.";
    }

    /// <summary>"about a tenth", "about three tenths", "about half a second" - a gap a lap, as a racer would say it.</summary>
    public static string SayPerLap(long ms) =>
        ms < 60 ? "a few hundredths"
        : ms < 150 ? "about a tenth"
        : ms < 450 ? $"about {Tenths((int)Math.Round(ms / 100.0))} tenths"
        : ms < 650 ? "about half a second"
        : $"about {ms / 1000.0:0.0} s";

    private static string Tenths(int n) => n switch
    {
        2 => "two",
        3 => "three",
        _ => "four",
    };

    /// <summary>Seconds to two places, with the unit: "1.74 s".</summary>
    public static string Seconds(long ms) => $"{ms / 1000.0:0.00} s";

    /// <summary>"lap 9", or "laps 3 to 8".</summary>
    private static string LapsCalled(IReadOnlyList<int> numbers, int first, int last) =>
        first == last ? $"lap {numbers[first]}" : $"laps {numbers[first]} to {numbers[last]}";

    /// <summary>
    /// Up to three things to take from a race, each a bold line and a
    /// detail: the run at the driver's limit, the biggest mistake set
    /// against the fade, and how the last laps compared with the rest.
    /// Nothing for a race too short to read.
    /// </summary>
    /// <param name="numbers">The chart's lap numbers for the laps (LapNumbers).</param>
    /// <param name="trackCars">The layout's length in its own cars (RaceCoach.TrackCars), for car lengths; null leaves them out.</param>
    public static List<Finding> ThreeThings(IReadOnlyList<long> laps, IReadOnlyList<int> numbers, double? trackCars)
    {
        var found = new List<Finding>();

        if (laps.Count < PaceAnalysis.Window || numbers.Count != laps.Count || BestThree(laps) is not { } three)
        {
            return found;
        }

        var best = laps.Min();
        var mistakes = MistakeLaps(laps);
        var fade = FadeOf(laps);

        // 1. The run at their limit, or failing that the best three.
        // Short: the best lap itself is marked on the chart beside this.
        var threeSaid = $"Best three in a row: {PaceAnalysis.Time(three.AverageMs)}.";

        if (LimitRun(laps) is { } run)
        {
            var length = run.Last - run.First + 1;

            found.Add(new Finding(
                $"{Capital(LapsCalled(numbers, run.First, run.Last))} were all at your limit",
                $"{Plural.Of(length, "lap")} in a row within 1.5% of your best. {threeSaid}"));
        }
        else
        {
            found.Add(new Finding(
                $"Your quickest three in a row were {LapsCalled(numbers, three.First, three.First + 2)}",
                $"They averaged {PaceAnalysis.Time(three.AverageMs)}. No three laps in a row came within 1.5% of your best {PaceAnalysis.Time(best)}."));
        }

        // 2. The biggest mistake, against what the fade cost.
        if (mistakes.Count > 0)
        {
            var worst = mistakes.OrderByDescending(i => laps[i]).First();
            var cost = laps[worst] - best;
            var lengths = SayLengths(cost, best, trackCars);
            var line = $"Lap {numbers[worst]} alone cost {Seconds(cost)}" + (lengths is null ? string.Empty : $", {lengths}");

            string detail;

            if (fade is not null && fade.LostMs > 0)
            {
                var fadeLaps = LapsCalled(numbers, fade.Start, laps.Count - 1);
                var compare = cost >= fade.LostMs * 1.15 ? "More than"
                    : cost >= fade.LostMs * 0.85 ? "About as much as"
                    : cost >= fade.LostMs * 0.6 ? "Nearly as much as"
                    : "Less than";

                detail = $"{compare} the fade lost over {fadeLaps} between them ({Seconds(fade.LostMs)}).";
            }
            else if (mistakes.Count > 1)
            {
                detail = $"Your {mistakes.Count} mistakes cost {Seconds(MistakeCostMs(laps))} in all, against your best lap.";
            }
            else
            {
                detail = OffByMoreThanMistakeShare(laps) == 1
                    ? "Your only lap over 4% off your best. Every other lap was clean."
                    : "The only lap that stood out from the laps around it.";
            }

            found.Add(new Finding(line, detail));
        }
        else
        {
            // WHY (15 Nov 2026, bug hunt 21): a mistake must also stand out
            // from the laps around it (MistakeLaps), so a slow warm-up or a
            // fade can be well over 4% off the best with no mistake in it -
            // and this still said every lap was within 4%.
            var off = OffByMoreThanMistakeShare(laps);

            found.Add(new Finding("No big mistakes", off == 0
                ? "Every lap was within 4% of your best."
                : $"{Plural.Of(off, "lap")} over 4% off your best, but none stood out from the laps around it."));
        }

        // 3. The last laps against the rest, mistakes left out of both.
        var lastCount = Math.Min(5, laps.Count / 3);

        if (lastCount >= 2)
        {
            var skip = mistakes.ToHashSet();
            var lastFrom = laps.Count - lastCount;
            var last = Enumerable.Range(lastFrom, lastCount).Where(i => !skip.Contains(i)).Select(i => laps[i]).ToList();
            var rest = Enumerable.Range(0, lastFrom).Where(i => !skip.Contains(i)).Select(i => laps[i]).ToList();

            if (last.Count > 0 && rest.Count > 0)
            {
                var lastAverage = (long)Math.Round(last.Average());
                var gap = lastAverage - (long)Math.Round(rest.Average());
                var least = FadeLeastMs(best);
                var leftOut = last.Count < lastCount ? " Mistake laps left out." : string.Empty;
                var lengths = SayLengths(Math.Abs(gap), best, trackCars);
                var asLengths = lengths is null ? string.Empty : $" ({lengths})";

                var detail = gap >= least
                    ? $"{Seconds(gap)} a lap slower than the laps before them{asLengths}. Battery or tyres: check the pack."
                    : gap <= -least
                        ? $"{Seconds(-gap)} a lap quicker than the laps before them{asLengths}. You finished strong."
                        : "Within a few hundredths of the laps before them. You held your pace to the flag.";

                found.Add(new Finding($"Your last {lastCount} laps averaged {PaceAnalysis.Time(lastAverage)}", detail + leftOut));
            }
        }

        return found;
    }

    /// <summary>How many laps are more than MistakeShare off the best - the first test MistakeLaps makes.</summary>
    private static int OffByMoreThanMistakeShare(IReadOnlyList<long> laps)
    {
        var best = laps.Min();
        var limit = best + (long)(best * MistakeShare);
        return laps.Count(l => l > limit);
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
