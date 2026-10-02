namespace HeatCaller.Tests;

/// <summary>
/// From the second 2 Oct 2026 bug hunt: once an undo had put a reset (or
/// abandoned) race back, every correction made to that race before the
/// reset was refused as "that race has been reset or replaced", though the
/// race and the driver were back on the board.
/// </summary>
public sealed class BugHuntH2SessionUndoTests : IDisposable
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

    private static RaceControlSession RunningRaceWith(string car)
    {
        var race = new RaceControlSession();
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);
        Seen(race, car, 101_000);
        Seen(race, car, 113_000);
        return race;
    }

    private static long PenaltyOf(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => s.TransponderId == car).PenaltyMs;

    [Fact]
    public void A_correction_made_before_a_reset_can_be_undone_once_the_reset_is_undone()
    {
        var race = RunningRaceWith("A");

        var edit = race.EditStanding("A", () => race.AdjustPenaltyTime("A", 5000));
        Assert.NotNull(edit);
        Assert.Equal(5000, PenaltyOf(race, "A"));

        // Reset by mistake, then put back - the way the Reset undo does it.
        var snapshot = race.CreateSnapshot();
        race.Reset();
        race.RestoreSnapshot(snapshot);

        race.UndoStandingEdit(edit!);

        Assert.Equal(0, PenaltyOf(race, "A"));
    }

    [Fact]
    public void A_correction_survives_two_resets_each_undone()
    {
        var race = RunningRaceWith("A");
        var edit = race.EditStanding("A", () => race.AdjustPenaltyTime("A", 5000));

        var first = race.CreateSnapshot();
        race.Reset();
        race.RestoreSnapshot(first);

        var second = race.CreateSnapshot();
        race.Reset();
        race.RestoreSnapshot(second);

        race.UndoStandingEdit(edit!);

        Assert.Equal(0, PenaltyOf(race, "A"));
    }

    [Fact]
    public void A_correction_to_a_race_that_was_reset_and_not_put_back_is_still_refused()
    {
        var race = RunningRaceWith("A");
        var edit = race.EditStanding("A", () => race.AdjustPenaltyTime("A", 5000));

        race.Reset();

        // A new race with the same driver in it: the old correction is not
        // about this one.
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(200_000);
        Seen(race, "A", 201_000);

        Assert.Throws<InvalidOperationException>(() => race.UndoStandingEdit(edit!));
    }

    [Fact]
    public void Putting_a_race_back_then_starting_another_refuses_the_old_correction()
    {
        var race = RunningRaceWith("A");
        var edit = race.EditStanding("A", () => race.AdjustPenaltyTime("A", 5000));

        var snapshot = race.CreateSnapshot();
        race.Reset();
        race.RestoreSnapshot(snapshot);

        // The restored race is then staged over: a different race again.
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(300_000);
        Seen(race, "A", 301_000);

        Assert.Throws<InvalidOperationException>(() => race.UndoStandingEdit(edit!));
    }
}
