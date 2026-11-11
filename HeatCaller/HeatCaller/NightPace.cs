using System.Globalization;

namespace HeatCaller;

/// <summary>
/// How the night ran, as numbers (11 Nov 2026, race-night build plan, Phase 0).
///
/// WHY: the competitor research found that the biggest complaint about a race
/// night is dead time between heats, not missing features - a racer is on
/// track about a fifth of the day. "Demonstrably better than LiveTime" has to
/// be a number a club can see, so this works out, from the races already
/// saved:
///
///  - green to green: how far apart the races started (the median, so one
///    long break does not hide a quick night);
///  - the gap: from one race's finish to the next one's green, and for the
///    longest gaps what the laptop saw happen in them - heats or finals being
///    built, a result being corrected, or drivers waiting at the grid;
///  - on track: the share of the night that cars were actually racing;
///  - heats run against the program.
///
/// Pure, so it can be tested without a window. Everything it reads is
/// already saved: the race's own start, finish and stage times
/// (SessionRecord), when each round was created (RoundDefinition.CreatedAtUtc)
/// and when each correction was made (SessionRecord.Edits). A gap longer than
/// <see cref="NewSitting"/> splits the night into separate sittings - an
/// afternoon practice and an evening program - and is not counted as dead
/// time, since nobody was waiting through it.
/// </summary>
public static class NightPace
{
    /// <summary>A stage this long before the green belongs to some earlier, abandoned attempt, not this race.</summary>
    public static readonly TimeSpan LongestGridWait = TimeSpan.FromMinutes(60);

    /// <summary>A gap longer than this is a break between sittings, not dead time.</summary>
    public static readonly TimeSpan NewSitting = TimeSpan.FromHours(3);

    /// <summary>A wait at the grid shorter than this is just the call-up and the countdown, not worth naming.</summary>
    public static readonly TimeSpan GridWaitWorthNaming = TimeSpan.FromMinutes(3);

    /// <summary>How many of the longest gaps the report lists.</summary>
    public const int GapsListed = 3;

    /// <summary>What happened between two races, and how long it took.</summary>
    /// <param name="Before">The race that had just finished.</param>
    /// <param name="After">The race that went green next.</param>
    /// <param name="Length">From the finish of <paramref name="Before"/> to the green of <paramref name="After"/>.</param>
    /// <param name="Causes">What the laptop saw in the gap, in plain words, most telling first. Empty when it saw nothing.</param>
    public sealed record Gap(SessionRecord Before, SessionRecord After, TimeSpan Length, IReadOnlyList<string> Causes)
    {
        /// <summary>The causes as one phrase - "building the finals, correcting a result" - or "nothing recorded".</summary>
        public string CauseText => Causes.Count == 0 ? "nothing recorded" : string.Join(", ", Causes);
    }

    /// <summary>The night's numbers. Null parts are ones there was not enough to say.</summary>
    /// <param name="Races">Races counted.</param>
    /// <param name="MedianGreenToGreen">The median time between one race's green and the next's.</param>
    /// <param name="MedianGap">The median time from a finish to the next green.</param>
    /// <param name="LongestGaps">The longest gaps, longest first, at most <see cref="GapsListed"/>.</param>
    /// <param name="OnTrackShare">Racing time as a share of the time from the first green to the last finish, 0-1.</param>
    /// <param name="RoundsRun">Of <paramref name="RoundsPlanned"/>, how many have a race; null with no program.</param>
    /// <param name="RoundsPlanned">The program's heats and mains; null with no program.</param>
    public sealed record Report(
        int Races,
        TimeSpan? MedianGreenToGreen,
        TimeSpan? MedianGap,
        IReadOnlyList<Gap> LongestGaps,
        double? OnTrackShare,
        int? RoundsRun,
        int? RoundsPlanned)
    {
        /// <summary>The longest gap of the night, or null with fewer than two races.</summary>
        public Gap? Longest => LongestGaps.Count > 0 ? LongestGaps[0] : null;
    }

    /// <summary>
    /// The stage time to keep with a race: <paramref name="stagedUtc"/> when
    /// it falls before the green and not absurdly long before it, else null.
    /// </summary>
    public static DateTime? StageFor(DateTime? stagedUtc, DateTime startedUtc) =>
        stagedUtc is { } staged && staged <= startedUtc && startedUtc - staged <= LongestGridWait
            ? staged
            : null;

    /// <summary>
    /// The night's numbers from its sessions - or null with fewer than two
    /// races, since one race says nothing about the gaps between them.
    /// </summary>
    /// <param name="sessions">The night's saved sessions. Practice, demo and imported sessions are left out here.</param>
    /// <param name="rounds">Every round, to see which were built in a gap. Null skips that cause.</param>
    /// <param name="plannedRoundIds">The night's program (a race day's RoundIds), for heats run against plan. Null skips it.</param>
    public static Report? For(
        IEnumerable<SessionRecord> sessions,
        IEnumerable<RoundDefinition>? rounds = null,
        IReadOnlyCollection<string>? plannedRoundIds = null)
    {
        var all = sessions.Where(s => s is not null).ToList();

        var races = all
            .Where(s => s.SessionType == RaceSessionType.Race && !s.IsNotScored() && s.EndedAtUtc > s.StartedAtUtc)
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(s => s.StartedAtUtc)
            .ToList();

        if (races.Count < 2)
        {
            return null;
        }

        var roundList = rounds?.Where(r => r is not null).ToList() ?? new List<RoundDefinition>();
        var greenToGreen = new List<TimeSpan>();
        var gaps = new List<Gap>();
        var racing = TimeSpan.Zero;
        var span = TimeSpan.Zero;
        var sittingStart = races[0].StartedAtUtc;

        for (var i = 0; i < races.Count; i++)
        {
            var race = races[i];
            racing += race.EndedAtUtc - race.StartedAtUtc;

            var last = i == races.Count - 1;
            var next = last ? null : races[i + 1];

            if (next is null || next.StartedAtUtc - race.EndedAtUtc > NewSitting)
            {
                // The end of a sitting: count its span, and start the next.
                span += race.EndedAtUtc - sittingStart;

                if (next is not null)
                {
                    sittingStart = next.StartedAtUtc;
                }

                continue;
            }

            greenToGreen.Add(next.StartedAtUtc - race.StartedAtUtc);

            var length = next.StartedAtUtc - race.EndedAtUtc;

            // Races that overlap (a re-run started before the last was
            // filed) leave no gap at all.
            if (length < TimeSpan.Zero)
            {
                length = TimeSpan.Zero;
            }

            gaps.Add(new Gap(race, next, length, CausesOf(race, next, roundList, all)));
        }

        int? planned = null;
        int? run = null;

        if (plannedRoundIds is { Count: > 0 })
        {
            var program = plannedRoundIds
                .Select(id => roundList.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal)))
                .Where(r => r is not null && r.Kind != RoundKind.Practice)
                .ToList();

            if (program.Count > 0)
            {
                planned = program.Count;
                run = program.Count(r => !string.IsNullOrEmpty(r!.LinkedSessionId));
            }
        }

        return new Report(
            races.Count,
            Median(greenToGreen),
            Median(gaps.Select(g => g.Length).ToList()),
            gaps.OrderByDescending(g => g.Length).ThenBy(g => g.After.StartedAtUtc).Take(GapsListed).ToList(),
            span > TimeSpan.Zero ? Math.Clamp(racing.TotalSeconds / span.TotalSeconds, 0, 1) : null,
            run,
            planned);
    }

    /// <summary>
    /// What the laptop saw happen between <paramref name="before"/>'s finish
    /// and <paramref name="after"/>'s green.
    /// </summary>
    private static List<string> CausesOf(SessionRecord before, SessionRecord after, List<RoundDefinition> rounds, List<SessionRecord> sessions)
    {
        var from = before.EndedAtUtc;
        var to = after.StartedAtUtc;
        var causes = new List<string>();

        bool InGap(DateTime utc) => utc > from && utc < to;

        var built = rounds.Where(r => r.Kind != RoundKind.Practice && InGap(r.CreatedAtUtc)).ToList();

        if (built.Any(r => r.Kind == RoundKind.Main))
        {
            causes.Add("building the finals");
        }
        else if (built.Count > 0)
        {
            causes.Add("building heats");
        }

        if (sessions.Any(s => s.Edits.Any(e => InGap(e.AtUtc))))
        {
            causes.Add("correcting a result");
        }

        if (after.StagedAtUtc is { } staged && staged >= from && to - staged >= GridWaitWorthNaming)
        {
            causes.Add($"waiting at the grid for {Clock(to - staged)}");
        }

        return causes;
    }

    private static TimeSpan? Median(List<TimeSpan> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : TimeSpan.FromTicks((sorted[middle - 1].Ticks + sorted[middle].Ticks) / 2);
    }

    /// <summary>A length as a race director reads one: "4:52" under an hour, "1 h 03 min" over.</summary>
    public static string Clock(TimeSpan length)
    {
        if (length < TimeSpan.Zero)
        {
            length = TimeSpan.Zero;
        }

        var seconds = (long)Math.Round(length.TotalSeconds);

        if (seconds < 3600)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600} h {seconds % 3600 / 60:00} min");
    }

    /// <summary>A share as a whole percentage: "31%".</summary>
    public static string Percent(double share) =>
        ((int)Math.Round(share * 100)).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// The night in one line, for the Next Steps card and the console:
    /// "Green to green 6:10 · longest gap 14:02 (building the finals) · 31% on track".
    /// </summary>
    public static string Line(Report report)
    {
        var parts = new List<string>();

        if (report.MedianGreenToGreen is { } g2g)
        {
            parts.Add($"Green to green {Clock(g2g)}");
        }

        if (report.Longest is { } longest)
        {
            parts.Add($"longest gap {Clock(longest.Length)} ({longest.CauseText})");
        }

        if (report.OnTrackShare is { } share)
        {
            parts.Add($"{Percent(share)} on track");
        }

        if (report.RoundsRun is { } run && report.RoundsPlanned is { } planned)
        {
            parts.Add($"{run} of {planned} races run");
        }

        return string.Join(" · ", parts);
    }
}
