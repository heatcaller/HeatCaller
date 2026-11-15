namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 18 (14 Nov 2026), "Leader behind" on per-car clocks.
///
/// A qualifier's ElapsedMs runs from each car's own first crossing, and
/// under OwnRelease from its own release, but LeaderBehind compared the two
/// cars' times as if they were on one clock. A few seconds between the two
/// cars' zeros moved the call most of a lap early, or lost it.
/// </summary>
public sealed class BugHunt18_LeaderBehindClockTests
{
    private static RacerStanding Car(string transponder, long qualifyingZeroMs, long releaseOffsetMs, params long[] laps)
    {
        var car = new RacerStanding
        {
            TransponderId = transponder,
            Laps = laps.Length,
            ElapsedMs = laps.Sum(),
            QualifyingZeroMs = qualifyingZeroMs,
            ReleaseOffsetMs = releaseOffsetMs,
        };

        car.LapTimesMs.AddRange(laps);
        return car;
    }

    [Fact]
    public void A_qualifier_on_own_clocks_does_not_lose_the_call()
    {
        // On the green's clock the leader crosses at 2, 11 ... 65 and the
        // backmarker at 2.5, 12.5 ... 62.5: the leader is due 2.5 s behind
        // and a second a lap quicker. Each clock starts at its first
        // crossing, so the leader's reads 63 s and the backmarker's 60 s.
        var leader = Car("1", 2000, 0, 0, 9000, 9000, 9000, 9000, 9000, 9000, 9000);
        var car = Car("2", 2500, 0, 0, 10000, 10000, 10000, 10000, 10000, 10000);

        Assert.Equal(1, LeaderBehind.Approach(car, new[] { leader, car }, RaceStartType.Qualifier, StaggerClock.OwnRelease));
    }

    [Fact]
    public void An_own_release_stagger_does_not_bring_the_call_early()
    {
        // On the green's clock the backmarker crosses at 52.5 and the leader
        // is due at 56 - 3.5 s back, too far. The leader was released 1.5 s
        // after the green, and on its own clock looked 2 s back.
        var leader = Car("1", -1, 1500, 500, 9000, 9000, 9000, 9000, 9000);
        var car = Car("2", -1, 0, 2500, 10000, 10000, 10000, 10000, 10000);

        Assert.Null(LeaderBehind.Approach(car, new[] { leader, car }, RaceStartType.StaggeredGrid, StaggerClock.OwnRelease));

        // On a shared green the release offset is no clock zero at all.
        var sharedLeader = Car("1", -1, 1500, 2000, 9000, 9000, 9000, 9000, 9000, 9000, 9000);
        var sharedCar = Car("2", -1, 0, 2500, 10000, 10000, 10000, 10000, 10000, 10000);
        Assert.Equal(1, LeaderBehind.Approach(sharedCar, new[] { sharedLeader, sharedCar }, RaceStartType.StaggeredGrid, StaggerClock.SharedGreen));
    }
}
