namespace HeatCaller.Tests;

/// <summary>
/// Lap-processing fixes from the 13 Nov 2026 bug hunt: a spare that jumped
/// the countdown as a car of its own and was then merged into its owner's
/// row left its jump behind, and a decoder clock that restarted above the
/// race's start reference left every elapsed time on the old zero.
/// </summary>
public sealed class BugHunt17_RaceControlSession_LapsTests
{
    /// <summary>A pass from the decoder itself - not synthetic, so the start checks see it.</summary>
    private static void Seen(RaceControlSession race, string car, uint deviceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = deviceTimeMs,
            SignalHits = 60,
            SignalStrength = 247,
        });

    private static RacerStanding StandingOf(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => string.Equals(s.TransponderId, car, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_spare_that_jumped_after_the_main_car_was_seen_in_staging_takes_its_jump_to_the_owners_row()
    {
        // Maya owns 100, with 200 as her spare; the class has a 10s jump penalty.
        var race = new RaceControlSession
        {
            JumpStartPenaltyMs = 10_000,
            AlternateTransponderOwner = id => string.Equals(id, "200", StringComparison.OrdinalIgnoreCase) ? "100" : null,
        };

        race.Stage();
        Seen(race, "100", 80_000); // her main car carried over the loop to the pit table

        race.StartCountdown();
        Seen(race, "200", 99_500); // she races the spare, and goes early
        race.GreenFlag(100_000);
        Seen(race, "200", 110_000);

        Assert.True(race.HasJumpStarted("100"));
        Assert.False(race.HasJumpStarted("200"));
        Assert.DoesNotContain(race.GetOrderedStandings(), s => s.TransponderId == "200");

        var maya = StandingOf(race, "100");
        Assert.Equal(2, maya.Laps);
        Assert.Equal(10_000L, maya.PenaltyMs);
    }

    [Fact]
    public void A_staggered_spare_that_jumped_is_timed_from_the_owners_own_release()
    {
        // Bug hunt 18: Maya owns 100, with 200 as her spare, and is released
        // 5 s after the green on her own clock. The owner's row the refile
        // built had no release offset, so she was timed from the green.
        long nowMs = 1_000_000;
        var utc0 = new DateTime(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            NowMsSource = () => nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(nowMs),
            StartType = RaceStartType.StaggeredGrid,
            StaggerClockMode = StaggerClock.OwnRelease,
            AlternateTransponderOwner = id => string.Equals(id, "200", StringComparison.OrdinalIgnoreCase) ? "100" : null,
        };

        void Cross(string car, uint deviceTimeMs)
        {
            nowMs = 1_000_000 + (deviceTimeMs - 100_000L);
            race.PollReleases();
            Seen(race, car, deviceTimeMs);
        }

        race.Stage();
        race.SetReleaseSchedule(new Dictionary<string, long> { ["100"] = 5_000 });
        Cross("100", 80_000); // her main car carried over the loop to the pit table

        race.StartCountdown();
        Cross("200", 99_500); // she races the spare, and goes early
        nowMs = 1_000_000;
        race.GreenFlag(100_000);

        Cross("200", 110_000);
        Cross("200", 120_000);

        Assert.DoesNotContain(race.GetOrderedStandings(), s => s.TransponderId == "200");

        var maya = StandingOf(race, "100");
        Assert.Equal(5_000L, maya.ReleaseOffsetMs);
        Assert.Equal(2, maya.Laps); // the first pass after her release is her start lap
        Assert.Equal(15_000L, maya.ElapsedMs); // 120 s less the green and her 5 s
    }

    [Fact]
    public void A_decoder_clock_that_restarts_above_the_start_reference_still_moves_the_reference()
    {
        long nowMs = 1_000_000;
        var utc0 = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            NowMsSource = () => nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(nowMs),
        };

        // The decoder was plugged in 20 s before the green.
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(20_000);

        void Cross(string car, uint deviceTimeMs, long msSinceGreen)
        {
            nowMs = 1_000_000 + msSinceGreen;
            Seen(race, car, deviceTimeMs);
        }

        Cross("A", 21_000, 1_000);
        Cross("A", 31_000, 11_000);
        Cross("A", 41_000, 21_000);
        Assert.Equal(21_000L, StandingOf(race, "A").ElapsedMs);

        // The cable is knocked and the decoder restarts from zero. A's next
        // crossing is 51 s into the race, and the new clock reads 35 s:
        // behind A's last lap, but ahead of the old start reference.
        Cross("A", 35_000, 51_000);
        Assert.Equal(3, StandingOf(race, "A").Laps);

        // The lap after it is timed on the new clock, and A's race time is
        // the race's: 61 s, not the 25 s the old reference made of it.
        Cross("A", 45_000, 61_000);

        var a = StandingOf(race, "A");
        Assert.Equal(4, a.Laps);
        Assert.Equal(10_000L, a.LapTimesMs[^1]);
        Assert.Equal(61_000L, a.ElapsedMs);
    }
}
