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

    /// <summary>Saving a result quicker than this is just filing it, not worth naming.</summary>
    public static readonly TimeSpan SavingWorthNaming = TimeSpan.FromMinutes(3);

    /// <summary>
    /// A gap this far over the set gap, with nothing else seen in it, is an
    /// "idle director" (12 Nov 2026, setup plan step 8, build plan 0.2).
    /// </summary>
    public static readonly TimeSpan IdleOver = TimeSpan.FromMinutes(5);

    /// <summary>A gap this long with nothing else seen in it is a break - dinner, watering the track - not a slow desk.</summary>
    public static readonly TimeSpan SittingBreak = TimeSpan.FromMinutes(20);

    /// <summary>
    /// The club's set gap between races (Auto start's), for "idle director"
    /// when a caller has no settings to hand it - set by Race Control from
    /// AppSettings.AutoStartGapSeconds, the way AppTime.Use24Hour is set
    /// once. A caller that knows better passes setGap to <see cref="For"/>.
    /// </summary>
    public static TimeSpan ClubGap { get; set; } = AutoStartClock.DefaultGap;

    /// <summary>What happened between two races, and how long it took.</summary>
    /// <param name="Before">The race that had just finished.</param>
    /// <param name="After">The race that went green next.</param>
    /// <param name="Length">From the finish of <paramref name="Before"/> to the green of <paramref name="After"/>.</param>
    /// <param name="Causes">What the laptop saw in the gap, in plain words, most telling first. Empty when it saw nothing.</param>
    public sealed record Gap(SessionRecord Before, SessionRecord After, TimeSpan Length, IReadOnlyList<string> Causes)
    {
        /// <summary>The causes as one phrase - "building the finals, correcting a result" - or "nothing recorded".</summary>
        public string CauseText => Causes.Count == 0 ? "nothing recorded" : string.Join(", ", Causes);

        /// <summary>From the green of <see cref="Before"/> to the green of <see cref="After"/>.</summary>
        public TimeSpan GreenToGreen => After.StartedAtUtc - Before.StartedAtUtc;

        /// <summary>From the finish to the result being saved (RoundDefinition.SavedAtUtc), or null when not recorded.</summary>
        public TimeSpan? FinishToSaved { get; init; }

        /// <summary>From the next race's call-up (RoundDefinition.CalledAtUtc) to its green, or null when not recorded.</summary>
        public TimeSpan? CalledToGreen { get; init; }
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

        /// <summary>
        /// The worst green to green of the night, with what caused it (12 Nov
        /// 2026, setup plan step 8, build plan 0.1) - the pair of races that
        /// started furthest apart. Usually the longest gap too, but not
        /// always: a long main followed by a quick turnaround can be worse.
        /// </summary>
        public Gap? WorstGreenToGreen { get; init; }

        /// <summary>The typical time from a checkered flag to its result being saved; null when no save was recorded.</summary>
        public TimeSpan? MedianFinishToSaved { get; init; }

        /// <summary>The typical time from a race's call-up to its green; null when no call-up was recorded.</summary>
        public TimeSpan? MedianCalledToGreen { get; init; }
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
    /// <param name="setGap">The club's set gap between races, for "idle director". Null is <see cref="ClubGap"/>.</param>
    /// <param name="breaks">The race day's planned breaks (setup plan D5), so a planned break is named as one. Null for none.</param>
    public static Report? For(
        IEnumerable<SessionRecord> sessions,
        IEnumerable<RoundDefinition>? rounds = null,
        IReadOnlyCollection<string>? plannedRoundIds = null,
        TimeSpan? setGap = null,
        IReadOnlyList<PlannedBreak>? breaks = null)
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
        var gapSet = setGap ?? ClubGap;

        // Which round each race ran, by the session's own tag and then by
        // the round's link - the second for races filed before tags.
        var byId = roundList.GroupBy(r => r.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var bySession = roundList
            .Where(r => !string.IsNullOrEmpty(r.LinkedSessionId))
            .GroupBy(r => r.LinkedSessionId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        RoundDefinition? RoundOf(SessionRecord s) =>
            (s.RoundId is { } id && byId.TryGetValue(id, out var tagged) ? tagged : null)
            ?? (bySession.TryGetValue(s.Id, out var linked) ? linked : null);

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

            var before = RoundOf(race);
            var after = RoundOf(next);

            gaps.Add(new Gap(race, next, length, CausesOf(race, next, before, after, roundList, all, gapSet, breaks))
            {
                // Only a stamp that falls inside this gap is this gap's: a
                // round saved again later, or called up in an earlier gap
                // that was abandoned, says nothing about this one.
                FinishToSaved = before?.SavedAtUtc is { } saved && saved >= race.EndedAtUtc && saved <= next.StartedAtUtc
                    ? saved - race.EndedAtUtc
                    : null,
                CalledToGreen = after?.CalledAtUtc is { } called && called >= race.EndedAtUtc && called <= next.StartedAtUtc
                    ? next.StartedAtUtc - called
                    : null,
            });
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
            planned)
        {
            WorstGreenToGreen = gaps.OrderByDescending(g => g.GreenToGreen).ThenBy(g => g.After.StartedAtUtc).FirstOrDefault(),
            MedianFinishToSaved = Median(gaps.Where(g => g.FinishToSaved is not null).Select(g => g.FinishToSaved!.Value).ToList()),
            MedianCalledToGreen = Median(gaps.Where(g => g.CalledToGreen is not null).Select(g => g.CalledToGreen!.Value).ToList()),
        };
    }

    /// <summary>
    /// What the laptop saw happen between <paramref name="before"/>'s finish
    /// and <paramref name="after"/>'s green.
    ///
    /// 12 Nov 2026, setup plan step 8 (build plan 0.2): first what the desk
    /// itself did - a planned break, Hold, +1 min, a driver's five minutes,
    /// a wait for the time drivers were given (RoundDefinition.GapNotes) -
    /// then what the laptop saw, as before. And when it saw nothing at all,
    /// a name for that too instead of "nothing recorded": a long gap is a
    /// "break" (dinner, watering the track), and one well over the set gap
    /// is an "idle director" - nobody pressed anything. That is the gap a
    /// race director most needs to see, because it is the one Auto start
    /// and a quicker desk would have saved.
    /// </summary>
    private static List<string> CausesOf(
        SessionRecord before,
        SessionRecord after,
        RoundDefinition? beforeRound,
        RoundDefinition? afterRound,
        List<RoundDefinition> rounds,
        List<SessionRecord> sessions,
        TimeSpan setGap,
        IReadOnlyList<PlannedBreak>? breaks)
    {
        var from = before.EndedAtUtc;
        var to = after.StartedAtUtc;
        var causes = new List<string>();

        bool InGap(DateTime utc) => utc > from && utc < to;

        var planned = NightBreaks.Length(NightBreaks.Between(beforeRound, afterRound, breaks));

        if (planned > TimeSpan.Zero)
        {
            causes.Add($"a planned break ({NightPlan.Minutes(planned)})");
        }

        var notes = afterRound?.GapNotes?.Where(n => n is not null && n.AtUtc >= from && n.AtUtc <= to).ToList() ?? new List<GapNote>();

        TimeSpan Total(GapNoteKind kind) => TimeSpan.FromSeconds(notes.Where(n => n.Kind == kind).Sum(n => Math.Max(0, n.Seconds)));

        if (Total(GapNoteKind.Held) is var held && held > TimeSpan.Zero)
        {
            causes.Add($"held {Short(held)} at the desk");
        }

        if (Total(GapNoteKind.PushedBack) is var pushed && pushed > TimeSpan.Zero)
        {
            causes.Add($"pushed back {Short(pushed)} (+1 min)");
        }

        if (Total(GapNoteKind.DriverAskedForTime) is var asked && asked > TimeSpan.Zero)
        {
            causes.Add($"{Short(asked)} more for a driver who asked");
        }

        if (Total(GapNoteKind.WaitedForPromise) is var waited && waited > TimeSpan.Zero)
        {
            causes.Add($"waited {Short(waited)} for the time drivers were given");
        }

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

        if (beforeRound?.SavedAtUtc is { } saved && saved >= from && saved <= to && saved - from >= SavingWorthNaming)
        {
            causes.Add($"saving the result took {Clock(saved - from)}");
        }

        if (after.StagedAtUtc is { } staged && staged >= from && to - staged >= GridWaitWorthNaming)
        {
            causes.Add($"waiting at the grid for {Clock(to - staged)}");
        }

        // Nothing seen at all: name the gap by its length.
        if (causes.Count == 0)
        {
            var length = to - from;

            if (length >= SittingBreak)
            {
                causes.Add("a break");
            }
            else if (length - setGap >= IdleOver)
            {
                // With the call-up's time when there is one: how long the
                // finished race sat before anybody called the next.
                causes.Add(afterRound?.CalledAtUtc is { } called && called > from && called < to
                    ? $"idle director (called up {Clock(called - from)} after the flag)"
                    : "idle director");
            }
        }

        return causes;
    }

    /// <summary>A length the desk added to a gap: "45 s" under a minute, "3 min" over.</summary>
    private static string Short(TimeSpan length) =>
        length < TimeSpan.FromMinutes(1)
            ? ((int)Math.Round(length.TotalSeconds)).ToString(CultureInfo.InvariantCulture) + " s"
            : NightPlan.Minutes(length);

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
    /// The night for the club's online results (12 Nov 2026, setup plan step
    /// 8, build plan 0.1): "Green to green 6:10 (worst 14:20) · 31% on track ·
    /// 18 of 18 races run".
    ///
    /// WHY NOT <see cref="Line"/>: the causes are for the club, not the
    /// public. "Idle director" or "correcting a result" on the page every
    /// driver's family reads is a volunteer told off in public, and a reason
    /// for a club to switch the whole thing off. The numbers alone are what
    /// "demonstrably quicker than last season" needs. Empty with nothing to say.
    /// </summary>
    public static string PublicLine(Report report)
    {
        var parts = new List<string>();

        if (report.MedianGreenToGreen is { } g2g)
        {
            parts.Add(report.WorstGreenToGreen is { } w && w.GreenToGreen > g2g
                ? $"Green to green {Clock(g2g)} (worst {Clock(w.GreenToGreen)})"
                : $"Green to green {Clock(g2g)}");
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

    /// <summary>
    /// The night in one line, for the Next Steps card and the console:
    /// "Green to green 6:10 · longest gap 14:02 (building the finals) · 31% on track".
    /// </summary>
    public static string Line(Report report)
    {
        var parts = new List<string>();

        if (report.MedianGreenToGreen is { } g2g)
        {
            // The worst beside the typical (12 Nov 2026, setup plan step 8),
            // with its cause - unless it is the longest gap too, which says
            // its cause straight after.
            var worst = report.WorstGreenToGreen is { } w && w.GreenToGreen > g2g
                ? ReferenceEquals(w, report.Longest)
                    ? $", worst {Clock(w.GreenToGreen)}"
                    : $", worst {Clock(w.GreenToGreen)} ({w.CauseText})"
                : string.Empty;

            parts.Add($"Green to green {Clock(g2g)}{worst}");
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
