namespace HeatCaller;

/// <summary>
/// When a timed race's clock passes a mark worth calling - "five minutes to
/// go", "thirty seconds" - asked for by Asher (28 Oct 2026). Worked out here,
/// from the clock, and said by AnnouncementService.NotifyTimeLeft; which of
/// the marks are actually said is the announcer's settings' business.
///
/// THE MARKS: every whole minute from two up, then 45, 30 and 15 seconds.
/// One minute is not here: the session has called the final minute since
/// long before this (RaceControlSession.FinalMinute), and saying it twice
/// is the one thing this must not do.
///
/// WATCHED, NOT SCHEDULED. The window's race tick (every half second) asks
/// <see cref="Next"/> where the clock is, and a mark is called when the
/// time left first drops to it. Nothing is set up when the race starts, so
/// a red flag (the race clock stops) or a restored race needs nothing done
/// about it.
///
/// NO BACKLOG. The first look at a race only notes where its clock is - so
/// the length of the race is not called at the green, and a race restored
/// after a restart does not have every mark it passed before the restart
/// called at once. After a freeze only the latest mark passed is called, and
/// only while it is still near enough true (<see cref="SayWithin"/>).
/// </summary>
internal sealed class TimeLeftCalls
{
    /// <summary>The marks inside the last minute, in seconds.</summary>
    private static readonly int[] LastMinuteMarks = { 15, 30, 45 };

    /// <summary>The race being watched, by its start - a new start is a new race.</summary>
    private DateTime? _race;

    /// <summary>The mark the clock was last at, in seconds: the time left rounded UP to the next mark.</summary>
    private int _lastMark;

    /// <summary>
    /// The mark the time left is at: the smallest mark it has not yet passed
    /// - 4:59 is at the five-minute mark, and reaches the four-minute mark at
    /// exactly 4:00. 0 once the time is up.
    /// </summary>
    internal static int MarkFor(TimeSpan remaining)
    {
        var seconds = remaining.TotalSeconds;

        if (seconds <= 0)
        {
            return 0;
        }

        foreach (var mark in LastMinuteMarks)
        {
            if (seconds <= mark)
            {
                return mark;
            }
        }

        return (int)Math.Ceiling(seconds / 60) * 60;
    }

    /// <summary>
    /// How long after a mark it is still worth calling: five seconds in the
    /// last minute, where the next mark is fifteen behind it, and fifteen
    /// seconds before that.
    /// </summary>
    internal static TimeSpan SayWithin(int mark) =>
        TimeSpan.FromSeconds(mark < 60 ? 5 : 15);

    /// <summary>Whether a mark is one this class calls - one minute is the session's final minute.</summary>
    internal static bool IsCalled(int mark) => mark is 15 or 30 or 45 || (mark >= 120 && mark % 60 == 0);

    /// <summary>
    /// The mark to call now, in seconds left, or null. Asked every race
    /// tick.
    /// </summary>
    /// <param name="raceStart">The race's start, which tells one race from the next. Null with no race.</param>
    /// <param name="timeLimit">The race's time limit. Null for a race on laps.</param>
    /// <param name="elapsed">The race clock - which stops under a red flag.</param>
    /// <param name="timedRace">
    /// A race is running, green or under a yellow, and is not practice (which
    /// sets a time limit only to drive the clock's ring).
    /// </param>
    public int? Next(DateTime? raceStart, TimeSpan? timeLimit, TimeSpan elapsed, bool timedRace)
    {
        if (!timedRace || raceStart is not { } start || timeLimit is not { } limit || limit <= TimeSpan.Zero)
        {
            return null;
        }

        var remaining = limit - elapsed;
        var mark = MarkFor(remaining);

        if (_race != start)
        {
            // The first look at this race: note where it is, call nothing.
            _race = start;
            _lastMark = mark;
            return null;
        }

        if (mark >= _lastMark)
        {
            return null;
        }

        _lastMark = mark;

        if (!IsCalled(mark) || mark - remaining.TotalSeconds > SayWithin(mark).TotalSeconds)
        {
            return null;
        }

        return mark;
    }
}
