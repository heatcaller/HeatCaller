namespace HeatCaller;

/// <summary>
/// One entry in a day's event schedule - e.g. "9:00 AM - Stock Buggy -
/// Qualifying Round 1". Times are freeform text rather than a parsed
/// TimeSpan/DateTime, since a shop's schedule is usually approximate
/// ("~10:15", "TBD") rather than a precise timestamp, and the sheet is not
/// sorted by time for the same reason - the order the day actually runs in
/// matters more than exact clock times.
///
/// That order is no longer this list's to keep. The timetable used to hold
/// its own hand-maintained sequence of the same day the programme held, with
/// Move Up on one page and Move Up on the other working against two different
/// orderings - so moving a heat on one did not move its line on the other, and
/// the next rebuild quietly threw one of the two away. The programme is the
/// running order now, and this list contributes the two things it adds: a
/// clock time per round, and the club's own lines interleaved between them.
/// See DayScheduleStore.All for how the two are merged.
/// </summary>
public sealed class ScheduleEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ScheduledTime { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsComplete { get; set; }

    /// <summary>
    /// The event this line belongs to.
    ///
    /// Without it the schedule was one global list that had to be cleared
    /// by hand between race days, which meant last week's timetable was
    /// still on screen at the start of this one.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// The round this line runs, when the line was generated from the
    /// program rather than typed.
    ///
    /// This is what stops the schedule and the program being two
    /// hand-maintained lists of the same thing: a generated line knows
    /// which heat it is, so it can tick itself off when that heat is
    /// recorded, and the run sheet can say what is next without anyone
    /// keeping the two in step.
    /// </summary>
    public string RoundId { get; set; } = string.Empty;

    /// <summary>When this line actually started, if it has. What "running late" is measured from.</summary>
    public DateTime? ActualStartUtc { get; set; }

    /// <summary>
    /// For a hand-typed line: how many of the programme's rounds come before
    /// it. Meaningless on a generated line, which takes its place from its
    /// round.
    ///
    /// This is what lets "lunch" stay between Q2 and Q3 through a rebuild. The
    /// old code had nothing of the kind: rebuilding the generated half removed
    /// every generated line and appended a fresh set, so every typed line ended
    /// up above the entire programme however carefully it had been placed. It
    /// is a count rather than "after round X" on purpose - deleting a round
    /// shifts a count by one, predictably, where an id would simply dangle.
    ///
    /// -1 means "written before lines carried this", and the loader works it
    /// out from where the line sits in the file. Zero is a real value: before
    /// the first round.
    /// </summary>
    public int AfterRounds { get; set; } = -1;

    /// <summary>True for a line the program generated - safe to rebuild, unlike one somebody typed.</summary>
    public bool IsGenerated => !string.IsNullOrEmpty(RoundId);
}

/// <summary>
/// How far off its slot something on the timetable actually ran.
///
/// Here rather than in either of the two places that needed it, because
/// there were two of these and only one had the fix below.
/// </summary>
public static class ScheduleDrift
{
    /// <summary>
    /// Minutes between when a line was due and when it happened - positive
    /// late, negative early. Null when the line has no time, or its time
    /// cannot be read.
    ///
    /// The slot is a time of day carrying no date, so it is taken on
    /// whichever day - the day before, the same day, or the day after -
    /// puts it nearest the real moment. Taking the moment's own date read
    /// a 23:55 slot reached at 00:10 as 1425 minutes EARLY rather than 15
    /// late. That is a club night running past midnight, which is not an
    /// edge case: it is most of them.
    /// </summary>
    public static int? MinutesOff(string? scheduledTime, DateTime actualLocal)
    {
        if (string.IsNullOrWhiteSpace(scheduledTime) || !DateTime.TryParse(scheduledTime, out var scheduled))
        {
            return null;
        }

        var due = new[] { -1, 0, 1 }
            .Select(days => actualLocal.Date.AddDays(days).Add(scheduled.TimeOfDay))
            .MinBy(candidate => Math.Abs((actualLocal - candidate).Ticks));

        return (int)Math.Round((actualLocal - due).TotalMinutes);
    }
}
