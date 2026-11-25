using Xunit;

namespace HeatCaller.Tests;

/// <summary>
/// WHY (24 Nov 2026, asked by Asher): Auto start never counted after a demo,
/// so a night rehearsed with Start Next held never started a race by itself.
/// A demo of a program race now opens the count, and the race it starts is
/// a demo too.
/// </summary>
public class AutoStartAfterDemoTests
{
    [Fact]
    public void A_demo_of_a_program_race_counts_and_starts_the_next_as_a_demo()
    {
        Assert.Equal((true, true), RaceControlControl.AutoStartFollows(isDemo: true, notScored: true, roundId: null, demoOfARound: true));
    }

    [Fact]
    public void A_Demo_Mode_race_with_no_program_race_behind_it_never_counts()
    {
        Assert.Equal((false, false), RaceControlControl.AutoStartFollows(isDemo: true, notScored: true, roundId: null, demoOfARound: false));
    }

    [Fact]
    public void A_real_program_race_counts_and_starts_a_real_race()
    {
        Assert.Equal((true, false), RaceControlControl.AutoStartFollows(isDemo: false, notScored: false, roundId: "q1h1", demoOfARound: false));
    }

    [Fact]
    public void A_real_race_after_a_demo_starts_a_real_race_again()
    {
        // DemoRound can be left set by a demo on the way out; a real record wins.
        Assert.Equal((true, false), RaceControlControl.AutoStartFollows(isDemo: false, notScored: false, roundId: "q1h2", demoOfARound: true));
    }

    [Theory]
    [InlineData(true, "q1h1")] // not scored (a Quick Race filed against nothing scores nothing)
    [InlineData(false, null)]  // kept on its own, no round
    public void A_Quick_Race_or_a_race_kept_on_its_own_never_counts(bool notScored, string? roundId)
    {
        Assert.Equal((false, false), RaceControlControl.AutoStartFollows(isDemo: false, notScored: notScored, roundId: roundId, demoOfARound: false));
    }
}
