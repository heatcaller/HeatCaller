namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026): which filed sessions take the night-before
/// preview off the club's page. Only a race from the open day's own rounds.
/// </summary>
public sealed class BugHunt8_MainForm_Commentary_csTests
{
    private static readonly IReadOnlySet<string> Saturday =
        new HashSet<string>(StringComparer.Ordinal) { "sat-r1", "sat-r2" };

    [Fact]
    public void A_practice_does_not_start_the_meeting()
    {
        var practice = new SessionRecord { SessionType = RaceSessionType.Practice, RoundId = "sat-r1" };

        Assert.False(MainForm.StartsTheMeeting(practice, Saturday));
    }

    [Fact]
    public void A_race_for_no_round_does_not_start_the_meeting()
    {
        Assert.False(MainForm.StartsTheMeeting(new SessionRecord { SessionType = RaceSessionType.Race }, Saturday));
        Assert.False(MainForm.StartsTheMeeting(new SessionRecord { SessionType = RaceSessionType.Race, RoundId = "" }, Saturday));
    }

    [Fact]
    public void A_race_from_another_days_round_does_not_start_the_meeting()
    {
        var other = new SessionRecord { SessionType = RaceSessionType.Race, RoundId = "wed-r1" };

        Assert.False(MainForm.StartsTheMeeting(other, Saturday));
    }

    [Fact]
    public void A_race_from_the_days_own_round_starts_the_meeting()
    {
        var heat = new SessionRecord { SessionType = RaceSessionType.Race, RoundId = "sat-r2" };

        Assert.True(MainForm.StartsTheMeeting(heat, Saturday));
    }

    [Fact]
    public void The_legacy_holder_counts_any_round()
    {
        var heat = new SessionRecord { SessionType = RaceSessionType.Race, RoundId = "anything" };

        Assert.True(MainForm.StartsTheMeeting(heat, null));
    }
}
