namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), "Leader behind" after the leader has taken the
/// checkered flag. A finished leader will not cross again, but its next
/// crossing was still projected as its last one plus its usual lap, so a
/// backmarker finishing just ahead of that projection was told the leader
/// was about to lap them.
/// </summary>
public sealed class BugHunt24_LeaderBehindTests
{
    /// <summary>A car from its lap times, the start lap first.</summary>
    private static RacerStanding Car(string transponder, params long[] laps)
    {
        var car = new RacerStanding
        {
            TransponderId = transponder,
            Laps = laps.Length,
            ElapsedMs = laps.Sum(),
        };

        car.LapTimesMs.AddRange(laps);
        return car;
    }

    // The leader crosses at 2, 11, 20 ... 65 and takes the flag at 65.
    private static readonly long[] LeaderLaps = { 2000, 9000, 9000, 9000, 9000, 9000, 9000, 9000 };

    // The backmarker crosses at 2.5, 12.5 ... 72.5: at 72.5 a racing leader
    // would be due at 74, 1.5 s behind and a second a lap quicker.
    private static readonly long[] BackmarkerFinishLaps = { 2500, 10000, 10000, 10000, 10000, 10000, 10000, 10000 };

    // The same backmarker a lap earlier, at 62.5, with the leader's crossing at 65 really seen.
    private static readonly long[] BackmarkerLaps = { 2500, 10000, 10000, 10000, 10000, 10000, 10000 };

    [Fact]
    public void A_leader_still_racing_is_still_warned_about()
    {
        var leader = Car("1", LeaderLaps);
        var car = Car("2", BackmarkerFinishLaps);

        Assert.NotNull(LeaderBehind.Approach(car, new[] { leader, car }));
        Assert.NotNull(LeaderBehind.Approach(car, new[] { leader, car }, tookTheFlag: _ => false));
    }

    [Fact]
    public void A_leader_that_has_taken_the_flag_is_not_warned_about()
    {
        var leader = Car("1", LeaderLaps);
        var car = Car("2", BackmarkerFinishLaps);

        Assert.Null(LeaderBehind.Approach(car, new[] { leader, car }, tookTheFlag: id => id == "1"));
        Assert.Null(LeaderBehind.Approach(car, leader, tookTheFlag: id => id == "1"));
        Assert.Null(LeaderBehind.Approach(
            car.LapTimesMs, car.Laps, car.ElapsedMs,
            leader.LapTimesMs, leader.Laps, leader.ElapsedMs,
            leaderFinished: true));
    }

    [Fact]
    public void A_finishing_crossing_the_leader_really_made_still_counts()
    {
        // At 62.5 the leader really was 2.5 s behind and crossed at 65 to
        // finish: that warning was right, flag or not.
        var leader = Car("1", LeaderLaps);
        var car = Car("2", BackmarkerLaps);

        Assert.Equal(1, LeaderBehind.Approach(car, new[] { leader, car }, tookTheFlag: id => id == "1"));
    }
}
