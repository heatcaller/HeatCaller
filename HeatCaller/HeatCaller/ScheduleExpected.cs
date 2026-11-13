namespace HeatCaller;

/// <summary>
/// When a Day Schedule line not started yet is expected to start, and how
/// late that is (12 Nov 2026, the first night on step 8).
///
/// WHY: the timetable's lateness was the day's drift alone
/// (DayScheduleStore.MinutesLate, measured from the last line started), so
/// an approved "Need 5 minutes", the desk's +1 min or a Hold moved Auto
/// start's count and the night plan but not the sheet the desk reads the
/// night off. Now the next race's push-back is added for its line and every
/// line after it. Display only: the stored ScheduledTime (HH:mm data the
/// club typed or the program filled) is never rewritten.
/// </summary>
internal static class ScheduleExpected
{
    /// <summary>Closer than this to its slot reads as on time - the same as a started line's drift (DayScheduleControl.Drift).</summary>
    public const int OnTimeWithin = 3;

    /// <summary>
    /// How late a line not started yet is expected to start, in minutes -
    /// positive late, negative early - or null when nobody can say (nothing
    /// started yet and no push-back). The day's lateness, plus the next
    /// race's push-back when the line is that race's or comes after it. With
    /// the next race not on the sheet, every line still to start waits for it.
    /// </summary>
    /// <param name="entries">The sheet in schedule order (DayScheduleStore.All).</param>
    /// <param name="line">The line.</param>
    /// <param name="dayLate">DayScheduleStore.MinutesLate().</param>
    /// <param name="next">Race Control's next race and its push-back (RaceControlControl.NextStartForPlan), or null.</param>
    public static int? MinutesLate(IReadOnlyList<ScheduleEntry> entries, ScheduleEntry line, int? dayLate, RaceControlControl.NextStartFact? next)
    {
        var pushed = next is { PushedBackMinutes: > 0 } fact && WaitsFor(entries, line, fact.RoundId) ? fact.PushedBackMinutes : 0;

        if (dayLate is null && pushed == 0)
        {
            return null;
        }

        return (dayLate ?? 0) + pushed;
    }

    /// <summary>
    /// The line's expected start in UTC, or null when its time doesn't read
    /// ("~10:15", "TBD") or nobody can say how late it is.
    /// </summary>
    public static DateTime? StartUtc(IReadOnlyList<ScheduleEntry> entries, ScheduleEntry line, int? dayLate, RaceControlControl.NextStartFact? next, DateTime nowUtc) =>
        MinutesLate(entries, line, dayLate, next) is { } late ? RaceEta.SlotUtc(line, late, nowUtc) : null;

    /// <summary>
    /// The words for a line not started yet: "expected 20:47 · 7 min late",
    /// or empty when it is on time or nobody can say.
    /// </summary>
    public static string Words(IReadOnlyList<ScheduleEntry> entries, ScheduleEntry line, int? dayLate, RaceControlControl.NextStartFact? next, DateTime nowUtc)
    {
        if (line.IsComplete || line.ActualStartUtc is not null
            || MinutesLate(entries, line, dayLate, next) is not { } late
            || Math.Abs(late) < OnTimeWithin
            || RaceEta.SlotUtc(line, late, nowUtc) is not { } startsUtc)
        {
            return string.Empty;
        }

        var off = late > 0 ? $"{late} min late" : $"{-late} min early";
        return $"expected {AppTime.TimeOfDay(startsUtc.ToLocalTime())} · {off}";
    }

    /// <summary>Whether the line starts no sooner than the next race - its own line, one after it, or any line when it has none.</summary>
    private static bool WaitsFor(IReadOnlyList<ScheduleEntry> entries, ScheduleEntry line, string? nextRoundId)
    {
        if (string.IsNullOrEmpty(nextRoundId))
        {
            return false;
        }

        var nextAt = -1;
        var lineAt = -1;

        for (var i = 0; i < entries.Count; i++)
        {
            if (nextAt < 0 && string.Equals(entries[i]?.RoundId, nextRoundId, StringComparison.Ordinal))
            {
                nextAt = i;
            }

            if (ReferenceEquals(entries[i], line))
            {
                lineAt = i;
            }
        }

        return nextAt < 0 || (lineAt >= 0 && lineAt >= nextAt);
    }
}
