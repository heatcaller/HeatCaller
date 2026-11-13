namespace HeatCaller;

/// <summary>
/// THE RACE DIRECTOR'S TWO MEASURES (12 Nov 2026, setup plan step 8 - Part 4
/// of plans/race-night-setup-plan.md).
///
/// WHY: the director's review of the setup plan found that clicks are the
/// wrong measure of a quick night. Starting the day takes thirty seconds;
/// the real time goes on the queue at the desk, drivers choosing classes,
/// chasing money and finding a dead transponder after the heats are up. So
/// it asked for two numbers, and these are them:
///
///  - entries close to heats posted (target under 5 minutes): from the race
///    day's "Entries close at" (RaceEvent.EntriesCloseLocalTime) to the
///    heats going on the board (RaceEvent.HeatsPostedUtc). A club that
///    doesn't post its heats is measured to the first green instead, and the
///    line says so.
///  - doors open to first green: from the first check-in of the night
///    (RaceEvent.DoorsOpenUtc, stamped by <see cref="StampDoorsOpen"/>) to
///    the first race that isn't practice going green.
///
/// They go on the event report's Night pace section, the Next Steps "how
/// the night went" note and the closing checklist, beside NightPace's
/// numbers. Pure, like NightPace: everything is handed in.
/// </summary>
public static class NightMeasures
{
    /// <summary>The two measures. Null parts are ones the night didn't record.</summary>
    /// <param name="EntriesToHeats">Entries close to heats posted (or to the first green - see <paramref name="HeatsNotPosted"/>). Negative when the heats went up before entries closed.</param>
    /// <param name="HeatsNotPosted">True when the heats were never posted, so <paramref name="EntriesToHeats"/> runs to the first green.</param>
    /// <param name="DoorsToGreen">Doors open (the first check-in) to the first green.</param>
    public sealed record Figures(TimeSpan? EntriesToHeats, bool HeatsNotPosted, TimeSpan? DoorsToGreen)
    {
        /// <summary>True when there is nothing to say.</summary>
        public bool Empty => EntriesToHeats is null && DoorsToGreen is null;
    }

    /// <summary>
    /// Stamps doors open on the race day - the first check-in of the night -
    /// when it hasn't been already. True when it stamped it, so the caller
    /// knows to save. Not on the legacy holder, and not on a closed day.
    /// </summary>
    public static bool StampDoorsOpen(RaceEvent? day, DateTime nowUtc)
    {
        if (day is not { IsLegacy: false, IsOpen: true } || day.DoorsOpenUtc is not null)
        {
            return false;
        }

        day.DoorsOpenUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// The night's first green: the earliest race that isn't practice, demo
    /// or imported, and isn't a practice round - or null with none yet.
    /// </summary>
    public static DateTime? FirstGreen(IEnumerable<SessionRecord> sessions, IEnumerable<RoundDefinition>? rounds = null)
    {
        var practice = (rounds ?? Enumerable.Empty<RoundDefinition>())
            .Where(r => r is { Kind: RoundKind.Practice })
            .Select(r => r.Id)
            .ToHashSet(StringComparer.Ordinal);

        return sessions
            .Where(s => s is not null
                        && s.SessionType == RaceSessionType.Race
                        && !s.IsNotScored()
                        && s.StartedAtUtc > DateTime.MinValue
                        && (s.RoundId is null || !practice.Contains(s.RoundId)))
            .Select(s => (DateTime?)s.StartedAtUtc)
            .Min();
    }

    /// <summary>The two measures for a race day from its sessions. Empty figures without a race day.</summary>
    public static Figures For(RaceEvent? day, IEnumerable<SessionRecord> sessions, IEnumerable<RoundDefinition>? rounds = null)
    {
        if (day is null || day.IsLegacy)
        {
            return new Figures(null, false, null);
        }

        var firstGreen = FirstGreen(sessions, rounds);

        TimeSpan? entries = null;
        var notPosted = day.HeatsPostedUtc is null;

        if (day.EntriesCloseLocalTime is { } close && (day.HeatsPostedUtc ?? firstGreen) is { } until)
        {
            // The close on the day nearest the moment measured to - a 23:50
            // close posted at 00:05 is fifteen minutes, not a day.
            entries = until - NightPlan.NearestOccurrence(close, until);
        }

        TimeSpan? doors = day.DoorsOpenUtc is { } open && firstGreen is { } green && green >= open
            ? green - open
            : null;

        return new Figures(entries, notPosted, doors);
    }

    /// <summary>
    /// The measures as a line: "entries close to heats posted 4:10 · doors
    /// open to first green 38:00". Empty when there is nothing to say.
    /// </summary>
    public static string Line(Figures figures)
    {
        var parts = new List<string>();

        if (figures.EntriesToHeats is { } entries)
        {
            // Never posted, the span runs to the first green - so a race sent
            // off before the close is a green before the close, not a posting.
            parts.Add(entries < TimeSpan.Zero
                ? figures.HeatsNotPosted
                    ? $"first green {NightPace.Clock(-entries)} before entries closed (heats not posted)"
                    : $"heats posted {NightPace.Clock(-entries)} before entries closed"
                : figures.HeatsNotPosted
                    ? $"entries close to first green {NightPace.Clock(entries)} (heats not posted)"
                    : $"entries close to heats posted {NightPace.Clock(entries)}");
        }

        if (figures.DoorsToGreen is { } doors)
        {
            parts.Add($"doors open to first green {NightPace.Clock(doors)}");
        }

        return string.Join(" · ", parts);
    }
}
