namespace HeatCaller.Tests;

/// <summary>
/// Batch two: following one driver on a phone (PhoneFollow) - their card,
/// and the call-up for their next race.
/// </summary>
public sealed class PhoneFollowTests
{
    [Fact]
    public void Each_driver_is_called_for_the_first_race_they_are_in()
    {
        var upcoming = new[]
        {
            new UpcomingRace("Q2 Heat 1 (Stock)", new[] { "A", "B" }, OnTrack: true),
            new UpcomingRace("Q2 Heat 2 (Stock)", new[] { "C", "D", "E" }, OnTrack: false),
            new UpcomingRace("Q2 Heat 3 (Stock)", new[] { "F", "A" }, OnTrack: false),
        };

        var callUps = PhoneFollow.CallUps(upcoming);

        // On track now, whatever else they're in later.
        Assert.Equal(new CallUp("Q2 Heat 1 (Stock)", 0, 1, true), callUps["A"]);

        // The race on track doesn't count as one to wait for.
        Assert.Equal(new CallUp("Q2 Heat 2 (Stock)", 0, 3, false), callUps["E"]);
        Assert.Equal(new CallUp("Q2 Heat 3 (Stock)", 1, 1, false), callUps["F"]);
        Assert.False(callUps.ContainsKey("Z"));
    }

    [Fact]
    public void The_call_up_reads_as_a_driver_would_say_it()
    {
        Assert.Equal("On track now", PhoneFollow.Words(new CallUp("Heat 1", 0, 2, true)));
        Assert.Equal("You're up next", PhoneFollow.Words(new CallUp("Heat 2", 0, 2, false)));
        Assert.Equal("1 race before yours", PhoneFollow.Words(new CallUp("Heat 3", 1, 2, false)));
        Assert.Equal("3 races before yours", PhoneFollow.Words(new CallUp("Heat 5", 3, 2, false)));
    }

    [Fact]
    public void A_card_has_the_position_and_the_gaps_either_side()
    {
        var leader = new RacerStanding { TransponderId = "A", Laps = 6, ElapsedMs = 36_000 };
        leader.LapTimesMs.AddRange(new long[] { 6000, 6000, 6000, 6000, 6000, 6000 });

        // Four laps in 35s: the leader crossed for its fifth at 30s, before
        // this car's last crossing - a lap down.
        var second = new RacerStanding { TransponderId = "B", Laps = 4, ElapsedMs = 35_000 };
        second.LapTimesMs.AddRange(new long[] { 8750, 8750, 8750, 8750 });

        var cards = PhoneFollow.Cards(new[] { leader, second });

        Assert.Equal(1, cards["A"].Position);
        Assert.Equal("Leading", cards["A"].Ahead);
        Assert.Equal("+1L", cards["A"].Behind);

        Assert.Equal(2, cards["B"].Position);
        Assert.Equal("+1L", cards["B"].Ahead);
        Assert.Equal("-", cards["B"].Behind);
        Assert.Equal(8750, cards["B"].LastLapMs);
    }
}
