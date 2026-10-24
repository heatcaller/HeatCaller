namespace HeatCaller.Tests;

/// <summary>
/// Race-session fix from the 23 Oct 2026 bug hunt: a friend on the owner's
/// registered spare jumped in the countdown, the spare's pass was filed
/// under the owner's main transponder, and the jump stayed with the owner
/// after the green - penalty, red row and a start lap they never drove.
/// </summary>
public sealed class BugHunt10_RaceControlSessionTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

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

    /// <summary>Maya owns 100, with 200 as her spare; the class has a 10s jump penalty.</summary>
    private static RaceControlSession MayaWithSpareJumpingTheStart()
    {
        var race = new RaceControlSession
        {
            JumpStartPenaltyMs = 10_000,
            AlternateTransponderOwner = id => string.Equals(id, "200", StringComparison.OrdinalIgnoreCase) ? "100" : null,
        };

        race.Stage();
        race.StartCountdown();
        Seen(race, "200", 99_500); // the spare goes early
        race.GreenFlag(100_000);
        return race;
    }

    [Fact]
    public void A_borrowed_spare_that_jumped_takes_the_jump_once_the_main_car_shows_up()
    {
        var race = MayaWithSpareJumpingTheStart();

        // Filed under the owner at the green, as before.
        Assert.True(race.HasJumpStarted("100"));

        // Maya's own car crosses: the call ProcessLapEvent makes on a main
        // transponder's own pass (RaceControlSession.Laps.cs).
        race.RefileCountdownJumpToAlternate("100");

        Assert.False(race.HasJumpStarted("100"));
        Assert.Equal(0L, race.OwedJumpStartPenaltyMs("100"));
        Assert.True(race.HasJumpStarted("200"));
        Assert.Equal(10_000L, race.OwedJumpStartPenaltyMs("200"));

        var spare = StandingOf(race, "200");
        Assert.True(spare.StartLapCredited);
        Assert.Equal(1, spare.Laps);
        Assert.Equal(99_500L, spare.LastLapDeviceTimeMs);
        Assert.DoesNotContain(race.GetOrderedStandings(), s => s.TransponderId == "100");

        // Once only.
        race.RefileCountdownJumpToAlternate("100");
        Assert.True(race.HasJumpStarted("200"));

        Seen(race, "100", 100_800);
        Seen(race, "200", 110_000);

        Assert.Equal(0L, StandingOf(race, "100").PenaltyMs);
        Assert.Equal(2, StandingOf(race, "200").Laps);
        Assert.Equal(10_000L, StandingOf(race, "200").PenaltyMs);
    }

    [Fact]
    public void The_owner_alone_on_their_spare_keeps_the_jump_on_their_own_row()
    {
        var race = MayaWithSpareJumpingTheStart();

        // The spare is heard first after the green and merged again - it is
        // Maya on her spare, so nothing moves.
        Seen(race, "200", 110_000);
        race.RefileCountdownJumpToAlternate("100");

        Assert.True(race.HasJumpStarted("100"));
        Assert.False(race.HasJumpStarted("200"));
        Assert.Equal(2, StandingOf(race, "100").Laps);
        Assert.DoesNotContain(race.GetOrderedStandings(), s => s.TransponderId == "200");
    }
}
