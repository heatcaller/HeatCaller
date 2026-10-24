using Xunit;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 9 (22 Oct 2026): the gap to the own-best ghost has to compare
/// both cars on the since-green clock - not their ElapsedMs, which carries
/// penalties, a qualifier's own zero, or an own-release offset.
/// </summary>
public class BugHunt9_RaceControlControl_Coaching_csTests
{
    private static RacerStanding Car(string id, long elapsedMs, long penaltyMs = 0, long qualifyingZeroMs = -1, long releaseOffsetMs = 0) =>
        new()
        {
            TransponderId = id,
            ElapsedMs = elapsedMs,
            PenaltyMs = penaltyMs,
            QualifyingZeroMs = qualifyingZeroMs,
            ReleaseOffsetMs = releaseOffsetMs,
        };

    private static long? Gap(RacerStanding driver, RacerStanding ghost, RaceStartType start, StaggerClock clock, long ghostLapMs) =>
        OwnBestGhost.GapToGhostMs(
            RaceControlControl.SinceGreenAtLastCrossingMs(driver, start, clock),
            RaceControlControl.SinceGreenAtLastCrossingMs(ghost, start, clock),
            ghostLapMs);

    [Fact]
    public void A_penalty_does_not_move_the_gap()
    {
        // On the track the driver crosses at 30.0 s, the ghost last crossed
        // at 23.8 s on an 8 s lap: the driver is 1.8 s AHEAD. A 2 s penalty
        // is folded into the driver's ElapsedMs.
        var driver = Car("1234", 32_000, penaltyMs: 2_000);
        var ghost = Car(Ghosts.Prefix + "1234", 23_800);

        Assert.Equal(-1_800, Gap(driver, ghost, RaceStartType.Standard, StaggerClock.OwnRelease, 8_000));
    }

    [Fact]
    public void A_qualifier_puts_each_cars_own_zero_back()
    {
        // The ghost zeroed at 1.0 s, the driver at 5.1 s. On the track the
        // driver crosses at 30.0 s and the ghost last crossed at 29.0 s:
        // the driver is 1 s behind.
        var driver = Car("1234", 30_000 - 5_100, qualifyingZeroMs: 5_100);
        var ghost = Car(Ghosts.Prefix + "1234", 29_000 - 1_000, qualifyingZeroMs: 1_000);

        Assert.Equal(1_000, Gap(driver, ghost, RaceStartType.Qualifier, StaggerClock.OwnRelease, 8_000));
    }

    [Fact]
    public void An_own_release_offset_is_put_back_for_the_driver_only()
    {
        // The driver was released 3 s after the green; the ghost runs from
        // the green. On the track: driver at 30.0 s, ghost at 29.5 s.
        var driver = Car("1234", 27_000, releaseOffsetMs: 3_000);
        var ghost = Car(Ghosts.Prefix + "1234", 29_500, releaseOffsetMs: 3_000);

        Assert.Equal(500, Gap(driver, ghost, RaceStartType.Standard, StaggerClock.OwnRelease, 8_000));
    }

    [Fact]
    public void A_plain_start_is_unchanged()
    {
        var driver = Car("1234", 30_000, releaseOffsetMs: 3_000);
        var ghost = Car(Ghosts.Prefix + "1234", 29_800);

        // One clock for everyone: the release offset is not on anyone's clock.
        Assert.Equal(200, Gap(driver, ghost, RaceStartType.Standard, StaggerClock.SharedGreen, 8_000));
    }
}
