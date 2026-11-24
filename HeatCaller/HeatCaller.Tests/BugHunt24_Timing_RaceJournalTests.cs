namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the race journal: the "Restore the unfinished race?" line
/// named the race clock's anchor as the start time, which a restore moves on,
/// so a race restored once and lost again was offered back as starting at the
/// wrong time.
/// </summary>
public sealed class BugHunt24_Timing_RaceJournalTests
{
    private static DateTime LocalToday(int hour, int minute) =>
        new DateTime(2026, 11, 20, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();

    private static RaceJournal.Entry RacingEntry() => new()
    {
        State = RaceState.Racing,
        SavedUtc = LocalToday(19, 25),
        Standings = { new RaceJournal.StandingRecord { TransponderId = "1001", Laps = 12 } },
    };

    [Fact]
    public void Describe_after_a_restore_still_says_when_the_race_really_started()
    {
        var entry = RacingEntry();

        // As a restore leaves it: went green at 19:00, the clock's anchor
        // moved to 19:16 when it was restored at 19:20 four minutes in.
        entry.SessionStartedAtUtc = LocalToday(19, 0);
        entry.RaceStartUtc = LocalToday(19, 16);

        var text = entry.Describe();

        Assert.Contains("started at 19:00", text);
        Assert.DoesNotContain("19:16", text);
    }

    [Fact]
    public void Describe_falls_back_to_the_race_clock_for_a_journal_without_a_session_start()
    {
        var entry = RacingEntry();
        entry.SessionStartedAtUtc = null;
        entry.RaceStartUtc = LocalToday(19, 16);

        Assert.Contains("started at 19:16", entry.Describe());
    }
}
