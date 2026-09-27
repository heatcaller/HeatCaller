namespace HeatCaller.Tests;

/// <summary>
/// A car sitting on the loop before the green is not a jump start
/// (RaceControlSession.NoteSightingBeforeGreen). Rehearsal 3 had both cars
/// parked on the loop through staging and the countdown - an EasyLap reports
/// a car sitting there every two seconds - and both were called as jump
/// starts at their first pass of the countdown.
/// </summary>
public sealed class ParkedOnLoopTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>A pass from the decoder itself - not synthetic, so staging and the countdown see it.</summary>
    private static void Seen(RaceControlSession race, string car, uint deviceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = deviceTimeMs,
            SignalHits = 60,
            SignalStrength = 247,
        });

    [Fact]
    public void A_car_sitting_on_the_loop_since_staging_is_not_called_as_a_jump_start()
    {
        var race = new RaceControlSession();
        var log = new List<string>();
        race.LogMessage += log.Add;

        race.Stage();

        // Every two seconds, as rehearsal 3's cars were.
        Seen(race, "P1", 100_000);
        Seen(race, "P1", 102_000);
        Seen(race, "P1", 104_000);

        race.StartCountdown();
        Seen(race, "P1", 106_000);

        Assert.False(race.HasJumpStarted("P1"));
        Assert.Single(log, l => l.Contains("P1 is sitting on the loop", StringComparison.Ordinal));
        Assert.DoesNotContain(log, l => l.StartsWith("JUMP START", StringComparison.Ordinal));
    }

    [Fact]
    public void A_car_placed_across_the_loop_and_then_going_early_is_still_a_jump_start()
    {
        var race = new RaceControlSession();

        race.Stage();

        // Rolled across the loop to its grid slot, and left there.
        Seen(race, "P1", 100_000);

        race.StartCountdown();

        // Goes on the lights, long after it was put down.
        Seen(race, "P1", 110_000);

        Assert.True(race.HasJumpStarted("P1"));
    }

    [Fact]
    public void A_car_driven_onto_the_loop_during_the_countdown_is_a_jump_start_at_its_first_pass()
    {
        var race = new RaceControlSession();

        race.Stage();
        race.StartCountdown();

        Seen(race, "P1", 100_000);

        Assert.True(race.HasJumpStarted("P1"));
    }
}
