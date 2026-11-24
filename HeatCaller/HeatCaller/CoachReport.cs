using System.Globalization;

namespace HeatCaller;

/// <summary>
/// A DRIVER'S REPORT CARD FOR THE NIGHT (22 Oct 2026).
///
/// Six short lines a driver can read after a run, on their own night page,
/// in the night email, and on the club's screens from a right-click on
/// a driver in Driver Hub or a result in Race History: their best lap and the
/// ideal lap their best sectors add up to, how consistent their clean laps
/// were, which sector costs them the most, how tonight compares with the
/// last night they ran this layout, what the setup change logged tonight
/// did to their pace, and how far they are off the quickest driver they
/// raced with tonight.
///
/// ARITHMETIC, NOT OPINION. Every line is a sum over laps the timing loop
/// read, and each one says nothing rather than guess: no ideal lap without
/// sector times, no consistency figure on fewer than <see cref="LeastCleanLaps"/>
/// clean laps, no setup verdict without <see cref="LeastLapsEachSide"/>
/// clean laps on BOTH sides of the change. A report card that told a driver
/// their spring change made them a tenth quicker on the strength of three
/// laps would be teaching them the wrong thing about their car, which is
/// worse than telling them nothing.
///
/// A CLEAN LAP is a flying lap (the start lap runs from the green and is not
/// a lap time) no slower than <see cref="CleanLapFactor"/> of the driver's
/// best on that layout tonight. A crash, a marshal stop or a pit visit is
/// not the driver's pace, and left in it would make a steady driver look
/// erratic.
///
/// ONE LAYOUT. A driver who ran two layouts in a night has two sets of
/// pace that cannot be compared, so the card is about the layout of their
/// most recent run tonight, and says so.
///
/// Pure: nothing here reads a store, so every line is pinned by
/// CoachReportTests. The callers (MainForm for the night page, the night email,
/// Driver Hub and Race History) gather the sessions, the
/// setup change and the names.
/// </summary>
internal static class CoachReport
{
    /// <summary>A lap slower than this times the best tonight is not a clean lap.</summary>
    internal const double CleanLapFactor = 1.07;

    /// <summary>Fewer clean laps than this and there is no consistency figure.</summary>
    internal const int LeastCleanLaps = 5;

    /// <summary>Fewer clean laps than this on either side of a setup change and nothing is said about it.</summary>
    internal const int LeastLapsEachSide = 8;

    /// <summary>Fewer laps with sectors than this and no sector is singled out.</summary>
    internal const int LeastSectorLaps = 3;

    /// <summary>A setup change whose effect is smaller than this is "about the same".</summary>
    internal const long SameWithinMs = 20;

    /// <summary>
    /// Two lap times closer than this are level: the card writes gaps to the
    /// hundredth, so anything smaller would print as "0.00".
    /// </summary>
    internal const long LevelWithinMs = 5;

    /// <summary>The setup change logged tonight for the car the driver ran, as the caller found it.</summary>
    /// <param name="AtUtc">When it was logged.</param>
    /// <param name="What">What changed, already in words - SetupDiff.Describe's "Pinion 22 → 23" - or null when it is not known.</param>
    internal sealed record SetupChange(DateTime AtUtc, string? What);

    /// <summary>Everything the card is worked out from.</summary>
    /// <param name="Transponder">The driver's transponder.</param>
    /// <param name="History">Every saved session; tonight's and earlier ones are told apart here.</param>
    /// <param name="Tonight">The night it is, as TonightAtTheTrack.NightOf gives it.</param>
    /// <param name="NightOf">The night a session started on, from its start time in UTC.</param>
    /// <param name="NameOf">The name to give another driver - the club's public form of it.</param>
    /// <param name="Setup">The setup change logged tonight for the car they ran, or null.</param>
    /// <param name="ClockOf">A UTC time as the club's clock reads it ("7:40 pm").</param>
    /// <param name="NightWord">"tonight" for the night in hand; "that night" for a past one opened from Race History.</param>
    internal sealed record Input(
        string Transponder,
        IEnumerable<SessionRecord> History,
        DateTime Tonight,
        Func<DateTime, DateTime> NightOf,
        Func<string, string> NameOf,
        SetupChange? Setup,
        Func<DateTime, string> ClockOf,
        string NightWord = "tonight");

    /// <summary>One driver's laps on one layout, in the order they were run.</summary>
    private sealed record Run(SessionRecord Session, SessionResultEntry Entry);

    /// <summary>
    /// The card, a line at a time, best lap first. Empty when the driver
    /// has not run a lap tonight - a report card with nothing on it is not
    /// shown at all.
    /// </summary>
    internal static IReadOnlyList<string> Lines(Input input)
    {
        var who = (input.Transponder ?? string.Empty).Trim();

        if (who.Length == 0 || GhostService.IsGhost(who))
        {
            return Array.Empty<string>();
        }

        var sessions = input.History
            .Where(s => s is not null && !s.IsNotScored())
            .ToList();

        var tonight = sessions
            .Where(s => input.NightOf(s.StartedAtUtc) == input.Tonight.Date)
            .OrderBy(s => s.StartedAtUtc)
            .ToList();

        var mine = RunsOf(who, tonight);

        if (mine.Count == 0)
        {
            return Array.Empty<string>();
        }

        // The layout of their latest run. See ONE LAYOUT.
        var layout = mine[^1].Session.TrackLayoutName ?? string.Empty;
        mine = mine.Where(r => SameLayout(r.Session, layout)).ToList();

        var laps = mine.SelectMany(r => FlyingLaps(r.Entry)).ToList();

        if (laps.Count == 0)
        {
            return Array.Empty<string>();
        }

        var best = laps.Min();
        var clean = laps.Where(l => l <= best * CleanLapFactor).ToList();
        var lines = new List<string>();

        // 6 Nov 2026, Bug hunt 11b review: a session saved with the bare
        // sector count before bug hunt 11 keyed the cut on the tiles is read
        // as the layout's first placed cut (SectorCutHistory), so a night
        // that spans the upgrade - or a rival's earlier heat - is not split
        // off although the loops never moved. Every saved session, so the
        // first placement is the layout's, not just tonight's.
        var cuts = new SectorCutHistory(input.History);

        // 1. Best lap, and the ideal lap.
        var bestSectors = BestSectors(mine, cuts);
        var where = layout.Length > 0 ? $" on {layout}" : string.Empty;

        // WHY (23 Nov 2026, bug hunt 24): the ideal lap used to be written
        // whenever the best sectors summed to anything under the best lap, so
        // a 1-4 ms difference printed "0.00s still to find" - a gap of
        // nothing. Under LevelWithinMs it is the plain best-lap line.
        if (bestSectors is { } sectors && best - sectors.Bests.Sum() >= LevelWithinMs)
        {
            var ideal = sectors.Bests.Sum();
            lines.Add($"Best lap {input.NightWord}{where}: {Seconds(best)}s, from {Plural.Of(laps.Count, "lap")}. "
                      + $"Your best sectors add up to an ideal lap of {Seconds(ideal)}s - {Gap(best - ideal)}s still to find.");
        }
        else
        {
            lines.Add($"Best lap {input.NightWord}{where}: {Seconds(best)}s, from {Plural.Of(laps.Count, "lap")}.");
        }

        // 1b. Driver rating (22 Oct 2026): the 0-150 rating each race they ran
        // tonight earned - see DriverRating. Races only; practice has no
        // result to rate.
        if (RatingLine(who, tonight, input.NightWord) is { } rating)
        {
            lines.Add(rating);
        }

        // 2. Consistency.
        if (Consistency(clean) is { } spread)
        {
            lines.Add($"Consistency: your clean laps were on average {Gap(spread)}s off your typical lap "
                      + $"({clean.Count} of {laps.Count} laps clean).");
        }

        // 3. The sector that costs the most.
        if (bestSectors is { } bests && CostliestSector(mine, bests.Bests, bests.Cut, best, cuts) is { } costly)
        {
            lines.Add($"Sector {Words.Number(costly.Index + 1)} is where most of your time goes: "
                      + $"on a clean lap you are {Gap(costly.LossMs)}s slower through it than your best.");
        }

        // 4. The last night on this layout.
        if (LastNightHere(who, sessions, layout, input) is { } last)
        {
            var delta = best - last.BestMs;
            var when = last.Night.ToString("ddd d MMM", CultureInfo.CurrentCulture);

            lines.Add(Math.Abs(delta) < 5
                ? $"Level with your best here on {when} ({Seconds(last.BestMs)}s)."
                : delta < 0
                    ? $"{Gap(-delta)}s quicker than your best here on {when} ({Seconds(last.BestMs)}s)."
                    : $"{Gap(delta)}s off your best here on {when} ({Seconds(last.BestMs)}s).");
        }

        // 5. Setup against pace.
        if (input.Setup is { } setup && SetupLine(mine, best, setup, input.ClockOf) is { } setupLine)
        {
            lines.Add(setupLine);
        }

        // 6. Against the quickest driver they raced with.
        if (Benchmark(who, mine, best, bestSectors, cuts, input.NameOf, input.NightWord) is { } benchmark)
        {
            lines.Add(benchmark);
        }

        return lines;
    }

    /// <summary>The driver's results tonight, oldest first. A ghost car's row is never theirs.</summary>
    private static List<Run> RunsOf(string who, IEnumerable<SessionRecord> sessions) =>
        sessions
            .SelectMany(s => s.Results
                .Where(r => r is not null && string.Equals((r.Transponder ?? string.Empty).Trim(), who, StringComparison.OrdinalIgnoreCase))
                .Select(r => new Run(s, r)))
            .ToList();

    private static bool SameLayout(SessionRecord session, string layout) =>
        string.Equals((session.TrackLayoutName ?? string.Empty).Trim(), layout.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A result's laps without the start lap, which runs from the green and is
    /// not a lap time. WHY StartSlots (15 Nov 2026, bug hunt 21): a driver
    /// credited with the start lap has a zero first and then the rest of the
    /// start - a dash Skip(1) let through as their best lap of the night.
    /// </summary>
    internal static IEnumerable<long> FlyingLaps(SessionResultEntry entry) =>
        entry.LapTimesMs.Skip(entry.StartSlots(entry.LapTimesMs)).Where(ms => ms > 0);

    /// <summary>
    /// The average distance of a clean lap from the driver's typical one
    /// (the median), in ms - or null on too few clean laps. The median, not
    /// the mean, so one scruffy lap that only just counted as clean does not
    /// move the middle.
    /// </summary>
    internal static long? Consistency(IReadOnlyList<long> clean)
    {
        if (clean.Count < LeastCleanLaps)
        {
            return null;
        }

        var typical = Median(clean);
        return (long)Math.Round(clean.Average(l => Math.Abs(l - typical)));
    }

    /// <summary>
    /// The driver's best time through each sector tonight, over every run on
    /// this layout whose laps were cut the same way - or null with no sector
    /// times. Runs cut differently (the split loop moved) are not mixed: the
    /// cut of the latest run with sectors wins.
    ///
    /// WHY (22 Oct 2026): it now hands back the cut it kept as well. The
    /// costliest sector and the benchmark only checked that a lap had the
    /// same NUMBER of sectors, so after the split loop moved on the same
    /// layout they measured laps (and a rival's sectors) cut one way against
    /// bests cut another, and could blame the wrong stretch of track.
    /// </summary>
    private static (long[] Bests, string Cut)? BestSectors(IReadOnlyList<Run> runs, SectorCutHistory cuts)
    {
        var withSectors = runs
            .Select(r => (Run: r, Bests: Sectors.Bests(r.Entry)))
            .Where(x => x.Bests is { Length: > 1 })
            .ToList();

        if (withSectors.Count == 0)
        {
            return null;
        }

        var cut = withSectors[^1].Run.Session.SectorCutOf(withSectors[^1].Bests!, cuts);
        long[]? best = null;

        foreach (var (run, bests) in withSectors)
        {
            if (bests!.Length != withSectors[^1].Bests!.Length || run.Session.SectorCutOf(bests, cuts) != cut)
            {
                continue;
            }

            best ??= bests.ToArray();

            for (var i = 0; i < best.Length; i++)
            {
                best[i] = Math.Min(best[i], bests[i]);
            }
        }

        return best is null ? null : (best, cut);
    }

    /// <summary>
    /// Which sector a clean lap loses the most time in against the driver's
    /// own best through it, on average - or null with too few clean laps that
    /// carry sectors, or when no sector loses as much as a hundredth.
    /// </summary>
    private static (int Index, long LossMs)? CostliestSector(IReadOnlyList<Run> runs, long[] bests, string cut, long bestLap, SectorCutHistory cuts)
    {
        var losses = new long[bests.Length];
        var counted = 0;

        foreach (var run in runs)
        {
            for (var lap = 1; lap < run.Entry.LapTimesMs.Count; lap++)
            {
                var lapMs = run.Entry.LapTimesMs[lap];

                if (lapMs <= 0 || lapMs > bestLap * CleanLapFactor
                    || run.Entry.SectorsOf(lap) is not { } sectors || sectors.Length != bests.Length
                    // WHY (22 Oct 2026): a lap cut differently from the bests (the
                    // split loop moved) is a different stretch of track per sector.
                    || run.Session.SectorCutOf(sectors, cuts) != cut)
                {
                    continue;
                }

                for (var i = 0; i < sectors.Length; i++)
                {
                    losses[i] += Math.Max(0, sectors[i] - bests[i]);
                }

                counted++;
            }
        }

        if (counted < LeastSectorLaps)
        {
            return null;
        }

        var worst = 0;

        for (var i = 1; i < losses.Length; i++)
        {
            if (losses[i] > losses[worst])
            {
                worst = i;
            }
        }

        var average = (long)Math.Round(losses[worst] / (double)counted);
        return average < 10 ? null : (worst, average);
    }

    /// <summary>The driver's best lap on this layout on the last night before tonight they ran it.</summary>
    private static (DateTime Night, long BestMs)? LastNightHere(string who, IReadOnlyList<SessionRecord> sessions, string layout, Input input)
    {
        var earlier = sessions
            .Where(s => SameLayout(s, layout))
            .Select(s => (Night: input.NightOf(s.StartedAtUtc), Session: s))
            .Where(x => x.Night < input.Tonight.Date)
            .SelectMany(x => RunsOf(who, new[] { x.Session }).Select(r => (x.Night, Laps: FlyingLaps(r.Entry).ToList())))
            .Where(x => x.Laps.Count > 0)
            .GroupBy(x => x.Night)
            .OrderByDescending(g => g.Key)
            .FirstOrDefault();

        return earlier is null ? null : (earlier.Key, earlier.SelectMany(x => x.Laps).Min());
    }

    /// <summary>
    /// What the setup change did to the driver's typical clean lap, runs
    /// before it against runs after it. A run that was going on when the
    /// change was logged belongs to neither side. Null unless both sides
    /// have <see cref="LeastLapsEachSide"/> clean laps.
    /// </summary>
    private static string? SetupLine(IReadOnlyList<Run> runs, long bestLap, SetupChange setup, Func<DateTime, string> clockOf)
    {
        var before = runs
            .Where(r => r.Session.EndedAtUtc != default && r.Session.EndedAtUtc <= setup.AtUtc)
            .SelectMany(r => FlyingLaps(r.Entry))
            .Where(l => l <= bestLap * CleanLapFactor)
            .ToList();

        var after = runs
            .Where(r => r.Session.StartedAtUtc >= setup.AtUtc)
            .SelectMany(r => FlyingLaps(r.Entry))
            .Where(l => l <= bestLap * CleanLapFactor)
            .ToList();

        if (before.Count < LeastLapsEachSide || after.Count < LeastLapsEachSide)
        {
            return null;
        }

        var from = Median(before);
        var to = Median(after);
        var delta = to - from;
        var what = string.IsNullOrWhiteSpace(setup.What) ? "the setup change" : $"the setup change ({setup.What.Trim()})";
        var since = $"Since {what} logged at {clockOf(setup.AtUtc)}";
        var sample = $"{before.Count} clean laps before, {after.Count} after";

        if (Math.Abs(delta) < SameWithinMs)
        {
            return $"{since}, your typical lap is about the same ({Seconds(from)}s → {Seconds(to)}s; {sample}).";
        }

        return delta < 0
            ? $"{since}, your typical lap is {Gap(-delta)}s quicker ({Seconds(from)}s → {Seconds(to)}s; {sample})."
            : $"{since}, your typical lap is {Gap(delta)}s slower ({Seconds(from)}s → {Seconds(to)}s; {sample}).";
    }

    /// <summary>
    /// How far off the quickest driver they raced with tonight on this
    /// layout - and, with sector times cut the same way on both, which
    /// sector most of the gap is in. Ghost cars are not drivers.
    /// </summary>
    private static string? Benchmark(string who, IReadOnlyList<Run> mine, long best, (long[] Bests, string Cut)? mineSectors, SectorCutHistory cuts, Func<string, string> nameOf, string nightWord)
    {
        var rivals = mine
            .Select(r => r.Session)
            .Distinct()
            .SelectMany(s => s.Results
                .Where(r => r is not null)
                .Select(r => new Run(s, r)))
            .Where(r => !string.IsNullOrWhiteSpace(r.Entry.Transponder)
                        && !GhostService.IsGhost(r.Entry.Transponder)
                        && !string.Equals(r.Entry.Transponder.Trim(), who, StringComparison.OrdinalIgnoreCase))
            .Select(r => (Run: r, Best: FlyingLaps(r.Entry).DefaultIfEmpty(long.MaxValue).Min()))
            .Where(x => x.Best != long.MaxValue)
            .ToList();

        if (rivals.Count == 0)
        {
            return null;
        }

        var quickest = rivals.OrderBy(x => x.Best).First();

        if (quickest.Best >= best)
        {
            return $"Nobody you raced with {nightWord} went quicker than your best lap.";
        }

        var name = nameOf(quickest.Run.Entry.Transponder.Trim());

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "the quickest driver";
        }

        var gap = best - quickest.Best;

        // WHY (23 Nov 2026, bug hunt 24): a rival 1-4 ms quicker used to get
        // "0.00s off Sam's best", a gap of nothing. Under LevelWithinMs the
        // driver is level with them, and no sector carries a gap that small.
        if (gap < LevelWithinMs)
        {
            return $"Level with {name}'s best of {Seconds(quickest.Best)}s, the quickest lap of anyone you raced with {nightWord}.";
        }

        var line = $"{Gap(gap)}s off {name}'s best of {Seconds(quickest.Best)}s, the quickest lap of anyone you raced with {nightWord}";

        // Most of it in one sector: the rival's best sectors against theirs,
        // only when both were cut the same way.
        // WHY (22 Oct 2026): "the same way" used to mean only the same number
        // of sectors; it now also compares the rival's session's cut with the
        // cut the driver's bests came from, so a moved split loop says nothing.
        if (mineSectors is { } ms
            && Sectors.Bests(quickest.Run.Entry) is { } theirs
            && theirs.Length == ms.Bests.Length
            && quickest.Run.Session.SectorCutOf(theirs, cuts) == ms.Cut)
        {
            var behind = ms.Bests.Select((mineMs, i) => mineMs - theirs[i]).ToArray();
            var worst = Array.IndexOf(behind, behind.Max());

            if (behind[worst] > 0 && behind[worst] * 2 > behind.Where(d => d > 0).Sum())
            {
                return line + $" - mostly in sector {Words.Number(worst + 1)}.";
            }
        }

        return line + ".";
    }

    /// <summary>
    /// "Driver rating tonight: 118.5 out of 150, in A-Main." for one
    /// race, or the average and the best for several - or null when they ran
    /// no race tonight (22 Oct 2026). Every layout counts: a rating is about
    /// the race, not the lap.
    /// </summary>
    internal static string? RatingLine(string who, IEnumerable<SessionRecord> tonight, string nightWord)
    {
        var rated = new List<(double Rating, string Race)>();

        foreach (var session in tonight)
        {
            var ratings = DriverRating.ForSession(session);
            var mine = ratings.FirstOrDefault(r => string.Equals((r.Key ?? string.Empty).Trim(), who, StringComparison.OrdinalIgnoreCase));

            if (mine.Key is not null)
            {
                rated.Add((mine.Value, (session.Label ?? string.Empty).Trim()));
            }
        }

        if (rated.Count == 0)
        {
            return null;
        }

        static string In(string race) => race.Length > 0 ? $", in {race}" : string.Empty;

        if (rated.Count == 1)
        {
            return $"Driver rating {nightWord}: {DriverRating.Format(rated[0].Rating)} out of 150{In(rated[0].Race)}.";
        }

        var best = rated.OrderByDescending(r => r.Rating).First();

        return $"Driver rating {nightWord}: {DriverRating.Format(rated.Average(r => r.Rating))} out of 150 on average over "
               + $"{rated.Count} races - best {DriverRating.Format(best.Rating)}{In(best.Race)}.";
    }

    /// <summary>The middle lap - the lower of the two middle ones on an even count, so it is always a lap that was run.</summary>
    internal static long Median(IReadOnlyList<long> laps)
    {
        var sorted = laps.OrderBy(l => l).ToList();
        return sorted[(sorted.Count - 1) / 2];
    }

    /// <summary>A lap time as the night page writes them: "8.912".</summary>
    internal static string Seconds(long ms) =>
        (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>A gap to the hundredth: "0.15".</summary>
    internal static string Gap(long ms) =>
        (Math.Abs(ms) / 1000.0).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Sector numbers as words, the way a driver reads them.</summary>
    private static class Words
    {
        internal static string Number(int n) => n switch
        {
            1 => "one",
            2 => "two",
            3 => "three",
            4 => "four",
            5 => "five",
            6 => "six",
            _ => n.ToString(CultureInfo.InvariantCulture),
        };
    }
}
