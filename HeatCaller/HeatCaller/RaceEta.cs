namespace HeatCaller;

/// <summary>
/// When a race still to run is expected to start, for the phone that
/// follows a driver: "about 7 min (19:42)".
///
/// WHY (22 Oct 2026, driver experience, item 3): "2 races before yours" is
/// right and still leaves a driver guessing whether that is five minutes or
/// twenty-five - which is the difference between charging a pack and
/// missing the race. The laptop knows two things that answer it: the run
/// sheet's own times (and how late the day is running against them), and
/// how far apart tonight's races have actually been starting.
///
/// THE RULE, in this order:
///  1. The round's line on the run sheet has a time that reads: that time,
///     put on whichever day is nearest (a club night runs past midnight -
///     see ScheduleDrift.MinutesOff), plus the minutes the day is running
///     late. If that has already gone by, the cycle below instead.
///  2. Otherwise now + away x cycle, plus half a cycle while a race is on
///     track (on average it is half done). The cycle is the median gap
///     between tonight's starts - at least two gaps, so one start says
///     nothing - clamped to 3-20 minutes, so a lunch break or two heats
///     started back to back cannot make it absurd.
///  3. Otherwise nothing. A phone saying nothing beats a phone guessing.
/// </summary>
internal static class RaceEta
{
    /// <summary>The shortest a race cycle is believed to be.</summary>
    public static readonly TimeSpan ShortestCycle = TimeSpan.FromMinutes(3);

    /// <summary>The longest a race cycle is believed to be.</summary>
    public static readonly TimeSpan LongestCycle = TimeSpan.FromMinutes(20);

    /// <summary>Fewer gaps between starts than this and there is no cycle to speak of.</summary>
    public const int LeastGaps = 2;

    /// <summary>
    /// When the round is expected to start, in UTC, or null when nobody can
    /// say. Pure, and cheap enough for every Wi-Fi state build.
    /// </summary>
    /// <param name="roundId">The round, to find its run-sheet line. Null or empty skips rule 1.</param>
    /// <param name="away">Races to run before it, not counting one on track now (CallUp.Away).</param>
    /// <param name="raceOnTrack">A race is staged, counting down or running now.</param>
    /// <param name="schedule">The day's run sheet (DayScheduleStore.All).</param>
    /// <param name="minutesLate">DayScheduleStore.MinutesLate(): positive late, negative early, null unknown.</param>
    /// <param name="startsUtc">When the day's lines actually started (ScheduleEntry.ActualStartUtc). Only tonight's are used.</param>
    /// <param name="nowUtc">Now.</param>
    public static DateTime? For(
        string? roundId,
        int away,
        bool raceOnTrack,
        IEnumerable<ScheduleEntry>? schedule,
        int? minutesLate,
        IEnumerable<DateTime>? startsUtc,
        DateTime nowUtc)
    {
        var now = AsUtc(nowUtc);
        var cycle = Cycle(startsUtc, now);

        // Rule 2's answer, which rule 1 also falls back to once its time
        // has gone by.
        DateTime? byCycle = cycle is { } each
            ? now + TimeSpan.FromTicks(each.Ticks * Math.Max(0, away)) + (raceOnTrack ? TimeSpan.FromTicks(each.Ticks / 2) : TimeSpan.Zero)
            : null;

        if (ScheduledUtc(roundId, schedule, minutesLate, now) is { } scheduled)
        {
            return scheduled > now ? scheduled : byCycle;
        }

        return byCycle;
    }

    /// <summary>
    /// The round's run-sheet time as a moment, plus the day's lateness - or
    /// null when it has no line, or no time that reads.
    /// </summary>
    internal static DateTime? ScheduledUtc(string? roundId, IEnumerable<ScheduleEntry>? schedule, int? minutesLate, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(roundId) || schedule is null)
        {
            return null;
        }

        var line = schedule.FirstOrDefault(e =>
            e is not null && string.Equals(e.RoundId, roundId, StringComparison.Ordinal));

        return SlotUtc(line, minutesLate, nowUtc);
    }

    /// <summary>
    /// A run-sheet line's time as a moment, plus <paramref name="minutesLate"/>
    /// - or null when there is no line or no time that reads. Also the Day
    /// Schedule's expected time for a line not started yet (12 Nov 2026,
    /// ScheduleExpected).
    /// </summary>
    internal static DateTime? SlotUtc(ScheduleEntry? line, int? minutesLate, DateTime nowUtc)
    {
        // Read the way ScheduleDrift.MinutesOff reads it, so "~10:15" and
        // "TBD" are nothing here exactly as they are there.
        if (line is null || string.IsNullOrWhiteSpace(line.ScheduledTime) || !DateTime.TryParse(line.ScheduledTime, out var slot))
        {
            return null;
        }

        var nowLocal = AsUtc(nowUtc).ToLocalTime();

        // The slot is a time of day with no date: on whichever day puts it
        // nearest now, so 00:10 seen at 23:55 is fifteen minutes away, not
        // a day ago.
        var due = new[] { -1, 0, 1 }
            .Select(days => nowLocal.Date.AddDays(days).Add(slot.TimeOfDay))
            .MinBy(candidate => Math.Abs((nowLocal - candidate).Ticks));

        due = due.AddMinutes(minutesLate ?? 0);

        return DateTime.SpecifyKind(due, DateTimeKind.Local).ToUniversalTime();
    }

    /// <summary>
    /// The median gap between tonight's starts, clamped to 3-20 minutes -
    /// or null with fewer than two gaps.
    /// </summary>
    internal static TimeSpan? Cycle(IEnumerable<DateTime>? startsUtc, DateTime nowUtc)
    {
        if (startsUtc is null)
        {
            return null;
        }

        var now = AsUtc(nowUtc);
        var tonight = TonightAtTheTrack.NightOf(now.ToLocalTime());

        var starts = startsUtc
            .Select(AsUtc)
            .Where(s => s <= now && TonightAtTheTrack.NightOf(s.ToLocalTime()) == tonight)
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        if (starts.Count < LeastGaps + 1)
        {
            return null;
        }

        var gaps = new List<long>();

        for (var i = 1; i < starts.Count; i++)
        {
            gaps.Add((starts[i] - starts[i - 1]).Ticks);
        }

        gaps.Sort();

        var middle = gaps.Count / 2;
        var median = gaps.Count % 2 == 1 ? gaps[middle] : (gaps[middle - 1] + gaps[middle]) / 2;

        return TimeSpan.FromTicks(Math.Clamp(median, ShortestCycle.Ticks, LongestCycle.Ticks));
    }

    /// <summary>A stored time as UTC: one read back from JSON with no kind is taken as the UTC it was saved as.</summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
