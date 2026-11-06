namespace HeatCaller.Tests;

/// <summary>
/// Race-session fix from the 5 Nov 2026 bug hunt: a countdown pressed
/// straight from Finished or Idle (no Stage) kept the last race's
/// alternate-transponder decisions, so the owner's spare that jumped in the
/// countdown was booked as a separate car. The green forgot the decisions,
/// the spare merged into the owner on its next pass, and the jump penalty
/// was lost - with an orphaned 1-lap start row left for the spare.
/// </summary>
public sealed class BugHunt11_TimingSessionTests : IDisposable
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
    private static RaceControlSession MayaWithSpare() => new()
    {
        JumpStartPenaltyMs = 10_000,
        AlternateTransponderOwner = id => string.Equals(id, "200", StringComparison.OrdinalIgnoreCase) ? "100" : null,
    };

    /// <summary>Countdown without Stage; the spare jumps, the green drops, the spare laps.</summary>
    private static void SpareJumpsACountdownWithoutStage(RaceControlSession race)
    {
        race.StartCountdown();
        Seen(race, "200", 99_500); // the spare goes early
        race.GreenFlag(100_000);
        Seen(race, "200", 110_000);
    }

    private static void AssertJumpStaysOnTheOwnersRow(RaceControlSession race)
    {
        Assert.True(race.HasJumpStarted("100"));
        Assert.False(race.HasJumpStarted("200"));
        Assert.DoesNotContain(race.GetOrderedStandings(), s => s.TransponderId == "200");

        var maya = StandingOf(race, "100");
        Assert.Equal(2, maya.Laps);
        Assert.Equal(10_000L, maya.PenaltyMs);
    }

    [Fact]
    public void Countdown_from_finished_without_stage_keeps_the_spares_jump_on_the_owners_row()
    {
        var race = MayaWithSpare();

        // The last heat: Maya on her main transponder.
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(10_000);
        Seen(race, "100", 20_000);
        Seen(race, "100", 30_000);
        race.CheckeredFlag();
        Assert.Equal(RaceState.Finished, race.State);

        // Next heat on her spare, Countdown pressed straight from Finished.
        SpareJumpsACountdownWithoutStage(race);

        AssertJumpStaysOnTheOwnersRow(race);
    }

    [Fact]
    public void Countdown_from_idle_without_stage_ignores_the_owners_passes_between_races()
    {
        var race = MayaWithSpare();

        // Maya's main car rolled across the loop while the session sat idle.
        Seen(race, "100", 50_000);

        SpareJumpsACountdownWithoutStage(race);

        AssertJumpStaysOnTheOwnersRow(race);
    }
}
