namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 13, timing: a black-flagged car can no longer end the race by
/// reaching the lap limit, call the last lap or lead it. The race journal
/// keeps whether a borrowed grid is numbered over the cars that race. A car
/// yet to cross the line is not "+1L" on the leader's start lap. And a
/// hand-released start with no round no longer holds the last race's field
/// at the line, absentees and all.
/// </summary>
public sealed class BugHunt13_TimingTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- A black-flagged car cannot end the race --------------------------------

    [Fact]
    public void A_black_flagged_leader_reaching_the_limit_does_not_finish_the_race()
    {
        var race = Racing(lapLimit: 3);
        race.FinishOnNextCrossing = false;
        race.WarningLadder = new List<WarningStep> { new(WarningAction.BlackFlag) };

        Cross(race, "A", 5_000);
        Cross(race, "B", 6_000);
        Cross(race, "A", 15_000);
        Cross(race, "B", 17_000);

        race.IssueWarning("A", "contact");

        // A ignores the call to come in and gets to the limit first.
        Cross(race, "A", 25_000);

        Assert.Equal(3, Standing(race, "A").Laps);
        Assert.Equal(RaceState.Racing, race.State);

        // B, the real leader, ends it.
        Cross(race, "B", 28_000);

        Assert.Equal(RaceState.Finished, race.State);
    }

    [Fact]
    public void A_black_flagged_leader_reaching_the_limit_does_not_put_the_checkered_out()
    {
        var race = Racing(lapLimit: 3);
        race.WarningLadder = new List<WarningStep> { new(WarningAction.BlackFlag) };

        Cross(race, "A", 5_000);
        Cross(race, "B", 6_000);
        Cross(race, "A", 15_000);
        Cross(race, "B", 17_000);

        race.IssueWarning("A", "contact");
        Cross(race, "A", 25_000);

        Assert.False(race.IsFinishing);
        Assert.Equal(RaceState.Racing, race.State);

        // B puts it out and takes it - the only car still being waited on,
        // so the race is over.
        Cross(race, "B", 28_000);

        Assert.True(race.HasTakenFlag("B"));
        Assert.Equal(RaceState.Finished, race.State);
    }

    [Fact]
    public void A_black_flagged_car_does_not_call_the_last_lap_or_lead()
    {
        var race = Racing(lapLimit: 3);
        race.WarningLadder = new List<WarningStep> { new(WarningAction.BlackFlag) };

        var lastLap = new List<string>();
        race.LastLapStarted += s => lastLap.Add(s.TransponderId);

        Cross(race, "A", 5_000);
        Cross(race, "B", 6_000);
        race.IssueWarning("A", "contact");

        // A is ahead on laps, but out of the race.
        Cross(race, "A", 15_000);

        Assert.Empty(lastLap);
        Assert.Equal("B", race.RaceLeader()!.TransponderId);
        Assert.Equal("2 LAPS TO GO", race.GetCountdownText());

        Cross(race, "B", 17_000);

        Assert.Equal(new[] { "B" }, lastLap);
    }

    // --- The journal keeps the borrowed grid's renumbering ----------------------

    [Fact]
    public void A_restored_race_still_numbers_a_borrowed_grid_over_its_starters()
    {
        var race = new RaceControlSession { LapLimit = 10 };
        var timing = new RaceTiming(race);
        race.SetGrid(new[] { "A", "B", "C", "D" }, renumberOverStarters: true);
        race.Stage();
        race.GreenFlag(0);
        Cross(race, "A", 5_000);

        new RaceJournal().Record(timing, DateTime.UtcNow, "Club oval", force: true);

        var entry = RaceJournal.Unfinished();
        Assert.NotNull(entry);
        Assert.True(entry!.RenumberGridOverStarters);

        var restored = new RaceControlSession();
        RaceJournal.Restore(new RaceTiming(restored), entry);

        Assert.True(restored.CreateSnapshot().RenumberGridOverStarters);
    }

    // --- A car yet to cross is not lapped ---------------------------------------

    [Fact]
    public void A_car_yet_to_cross_is_not_a_lap_down_on_the_leader_s_first_lap()
    {
        Assert.Equal(new RaceGap(0, null), RaceGaps.Compute(0, 0, 1, 5_000, _ => 5_000));
        Assert.Equal("-", RaceGaps.Format(RaceGaps.Compute(0, 0, 1, 5_000, _ => 5_000)));
    }

    [Fact]
    public void A_car_yet_to_cross_is_lapped_once_the_leader_has_crossed_twice()
    {
        Assert.Equal(1, RaceGaps.Compute(0, 0, 2, 15_000, lap => lap * 7_500L).LapsDown);
        Assert.Equal(2, RaceGaps.Compute(0, 0, 3, 22_500, lap => lap * 7_500L).LapsDown);
    }

    [Fact]
    public void A_held_car_in_a_hand_released_start_is_not_plus_one_lap()
    {
        var race = new RaceControlSession { StartType = RaceStartType.HandReleased };
        race.SetGrid(new[] { "A", "B" });
        race.Stage();
        race.GreenFlag(0);
        race.ReleaseDriver("A");
        Cross(race, "A", 5_000);

        var ordered = race.GetOrderedStandings();
        Assert.Equal("A", ordered[0].TransponderId);
        Assert.Equal("B", ordered[1].TransponderId);
        Assert.Equal(0, ordered[1].Laps);

        Assert.Equal(0, RaceGaps.Between(ordered[1], ordered[0]).LapsDown);
    }

    // --- A hand-released start does not borrow the last race's field ------------

    [Theory]
    [InlineData(RaceStartType.StaggeredGrid, true)]
    [InlineData(RaceStartType.StaggeredReverse, true)]
    [InlineData(RaceStartType.StaggeredHandicap, true)]
    [InlineData(RaceStartType.HandReleased, false)]
    [InlineData(RaceStartType.Standard, false)]
    [InlineData(RaceStartType.Qualifier, false)]
    public void Only_a_staggered_start_takes_the_last_race_as_its_grid(RaceStartType type, bool borrows)
    {
        Assert.Equal(borrows, RaceControlControl.LastRaceCanBeTheGrid(type));
    }

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = raceTimeMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

    private static RaceControlSession Racing(int? lapLimit = null)
    {
        var race = new RaceControlSession { LapLimit = lapLimit };
        race.Stage();
        race.GreenFlag(0);
        return race;
    }

    private static RacerStanding Standing(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => s.TransponderId == car);
}
