namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), "Leader behind" in a backmarker's ear.
///
/// It is about where the cars are on the track, but it read the race time
/// with each car's time penalty in it and took whoever topped the order as
/// the leader. So a penalty moved the call most of a lap early (and used up
/// the real one) or lost it, and a ghost at the top of the order had real
/// drivers told to move over for a car that is not there.
/// </summary>
public sealed class BugHunt17_LeaderBehindTests
{
    /// <summary>A car from its lap times, the start lap first, with any time penalty on top of its race time.</summary>
    private static RacerStanding Car(string transponder, long penaltyMs, params long[] laps)
    {
        var car = new RacerStanding
        {
            TransponderId = transponder,
            Laps = laps.Length,
            ElapsedMs = laps.Sum() + penaltyMs,
            PenaltyMs = penaltyMs,
        };

        car.LapTimesMs.AddRange(laps);
        return car;
    }

    // The leader crosses at 2, 11, 20 ... 65; the backmarker at 2.5, 12.5 ... 62.5.
    // At 62.5 the leader is due at 65: 2.5 s behind and a second a lap quicker.
    private static readonly long[] LeaderLaps = { 2000, 9000, 9000, 9000, 9000, 9000, 9000, 9000 };
    private static readonly long[] BackmarkerLaps = { 2500, 10000, 10000, 10000, 10000, 10000, 10000 };

    [Fact]
    public void A_time_penalty_on_the_backmarker_does_not_lose_the_call()
    {
        Assert.Equal(1, LeaderBehind.Approach(Car("2", 0, BackmarkerLaps), Car("1", 0, LeaderLaps)));

        // With 5 s on its race time the leader looked as if it had gone by.
        Assert.Equal(1, LeaderBehind.Approach(Car("2", 5000, BackmarkerLaps), Car("1", 0, LeaderLaps)));
    }

    [Fact]
    public void A_time_penalty_on_the_backmarker_does_not_bring_the_call_early()
    {
        // A lap sooner: the backmarker at 52.5, the leader due at 56 - 3.5 s
        // back, too far. A 1.5 s penalty made that read as 2.0 s.
        var leader = Car("1", 0, LeaderLaps.Take(6).ToArray());

        Assert.Null(LeaderBehind.Approach(Car("2", 0, BackmarkerLaps.Take(6).ToArray()), leader));
        Assert.Null(LeaderBehind.Approach(Car("2", 1500, BackmarkerLaps.Take(6).ToArray()), leader));
    }

    [Fact]
    public void A_time_penalty_on_the_leader_does_not_lose_the_call()
    {
        Assert.Equal(1, LeaderBehind.Approach(Car("2", 0, BackmarkerLaps), Car("1", 5000, LeaderLaps)));
    }

    [Fact]
    public void A_ghost_on_top_of_the_order_is_not_the_leader()
    {
        var ghost = Car(Ghosts.Prefix + "1", 0, LeaderLaps);

        // The real leader is on the backmarker's pace, a few tenths up the road.
        var leader = Car("1", 0, 2300, 10000, 10000, 10000, 10000, 10000, 10000);
        var car = Car("2", 0, BackmarkerLaps);
        var ordered = new[] { ghost, leader, car };

        // The ghost alone would have had the driver told.
        Assert.Equal(1, LeaderBehind.Approach(car, ghost));

        Assert.Null(LeaderBehind.Approach(car, ordered));
        Assert.Null(LeaderBehind.Approach(leader, ordered));
    }

    [Fact]
    public void The_real_leader_is_still_warned_about_with_a_ghost_ahead_of_it()
    {
        var ghost = Car(Ghosts.Prefix + "1", 0, 1900, 8900, 8900, 8900, 8900, 8900, 8900, 8900);
        var leader = Car("1", 0, LeaderLaps);
        var car = Car("2", 0, BackmarkerLaps);

        Assert.Equal(1, LeaderBehind.Approach(car, new[] { ghost, leader, car }));
    }

    [Fact]
    public void A_ghost_is_never_warned_and_a_field_of_ghosts_has_no_leader()
    {
        var leader = Car("1", 0, LeaderLaps);
        var ghost = Car(Ghosts.Prefix + "2", 0, BackmarkerLaps);

        Assert.Null(LeaderBehind.Approach(ghost, new[] { leader, ghost }));
        Assert.Null(LeaderBehind.Approach(ghost, new[] { Car(Ghosts.Prefix + "1", 0, LeaderLaps), ghost }));
    }
}
