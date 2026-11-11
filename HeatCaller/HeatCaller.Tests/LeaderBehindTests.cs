namespace HeatCaller.Tests;

/// <summary>
/// "Leader behind" (11 Nov 2026, build plan 7.3): LeaderBehind works out
/// from the crossings whether the leader is about to lap a car, and which
/// lapping it would be, so the driver's lap voice can warn them once.
/// </summary>
public sealed class LeaderBehindTests
{
    /// <summary>A car's lap times, the start lap first, and its lap count and race time from them.</summary>
    private static (List<long> Laps, int Count, long Elapsed) Car(params long[] laps) =>
        (laps.ToList(), laps.Length, laps.Sum());

    private static int? Approach((List<long> Laps, int Count, long Elapsed) car, (List<long> Laps, int Count, long Elapsed) leader) =>
        LeaderBehind.Approach(car.Laps, car.Count, car.Elapsed, leader.Laps, leader.Count, leader.Elapsed);

    [Fact]
    public void A_leader_two_seconds_behind_on_track_and_a_second_a_lap_quicker_is_about_to_lap_you()
    {
        // Leader: start 2.0 then 9.0s laps; crossings 2, 11, 20, 29, 38, 47.
        var leader = Car(2000, 9000, 9000, 9000, 9000, 9000);
        // Backmarker: start 2.5 then 10.0s laps; crossings 2.5 ... 42.5.
        // At 42.5 the leader last crossed at 38 and is due at 47: 4.5s back.
        var car = Car(2500, 10000, 10000, 10000, 10000);
        Assert.Null(Approach(car, leader));

        // One lap on, at 52.5 the leader (last crossing 47, due 56) is 3.5s
        // back - still more than a lap's worth of the 1.0s it gains.
        Assert.Null(Approach(Car(2500, 10000, 10000, 10000, 10000, 10000), Car(2000, 9000, 9000, 9000, 9000, 9000)));

        // At 62.5 it last crossed at 56 and is due at 65: 2.5s - about to lap.
        Assert.Equal(1, Approach(Car(2500, 10000, 10000, 10000, 10000, 10000, 10000), Car(2000, 9000, 9000, 9000, 9000, 9000, 9000, 9000)));
    }

    [Fact]
    public void A_far_quicker_leader_is_warned_about_a_lap_of_closing_out()
    {
        // The leader gains 4s a lap: 3.5s back is inside one lap of that.
        var leader = Car(2000, 8000, 8000);   // crossings 2, 10, 18
        var car = Car(2500, 12000);           // crossings 2.5, 14.5; the leader crossed at 10 and again at 18
        Assert.Equal(1, Approach(car, leader));
    }

    [Fact]
    public void Nothing_for_a_leader_that_is_not_quicker()
    {
        var leader = Car(2000, 10000, 10000, 10000, 10000);
        var car = Car(2500, 10000, 10000, 10000);
        Assert.Null(Approach(car, leader));

        // Quicker by less than a tenth is the same pace.
        Assert.Null(Approach(Car(2500, 10050, 10050, 10050), Car(2000, 10000, 10000, 10000, 10000)));
    }

    [Fact]
    public void Nothing_for_the_car_just_behind_the_leader_on_the_same_lap()
    {
        // P2 a few tenths behind: the leader is most of a lap behind it on track.
        var leader = Car(2000, 9000, 9000, 9000);
        var car = Car(2300, 9200, 9200, 9200);
        Assert.Null(Approach(car, leader));
    }

    [Fact]
    public void A_car_already_a_lap_down_is_warned_about_the_second_lapping()
    {
        // Leader at 8s laps, car at 12s: by the car's crossing at 38.5 the
        // leader has done five laps to its three - lapped once - and is
        // 3.5s behind on track again (it crossed at 34 and again at 42).
        var leader = Car(2000, 8000, 8000, 8000, 8000, 8000);
        var car = Car(2500, 12000, 12000, 12000);
        Assert.Equal(2, Approach(car, leader));
    }

    [Fact]
    public void Uses_the_leaders_real_next_crossing_when_it_has_happened()
    {
        // Its usual 9.0s would have it 2.5s behind - but its lap was 9.4s,
        // so it crossed 2.9s after this car did: not yet close enough.
        var leader = Car(2000, 9000, 9000, 9000, 9000, 9000, 9000, 9400);  // crossings ... 56, 65.4
        var car = Car(2500, 10000, 10000, 10000, 10000, 10000, 10000);     // last crossing 62.5
        Assert.Null(Approach(car, leader));
    }

    [Fact]
    public void One_crash_lap_does_not_make_a_car_look_slow()
    {
        // The middle of the last three laps is the pace, so one 30s lap is
        // not "the leader gains 21s a lap".
        var leader = Car(2000, 9000, 9000, 9000, 9000, 9000);
        var car = Car(2500, 9000, 9000, 30000);
        Assert.Null(Approach(car, leader));
    }

    [Fact]
    public void Nothing_before_either_car_has_a_flying_lap()
    {
        Assert.Null(Approach(Car(2500), Car(2000, 9000)));
        Assert.Null(Approach(Car(2500, 10000), Car(2000)));
        Assert.Null(LeaderBehind.Approach(Array.Empty<long>(), 0, 0, new long[] { 2000, 9000 }, 2, 11000));
    }

    [Fact]
    public void Never_for_the_leader_itself()
    {
        var leader = new RacerStanding { TransponderId = "A", Laps = 3, ElapsedMs = 20000 };
        leader.LapTimesMs.AddRange(new long[] { 2000, 9000, 9000 });

        Assert.Null(LeaderBehind.Approach(leader, leader));
    }
}
