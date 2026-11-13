namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 17 (13 Nov 2026), the Practice page: a Yes to "Clear the
/// finished race?" is only for the race it was asked about, and the undo of
/// Clear Stats is not refused over laps only a ghost has run since.
/// </summary>
public sealed class BugHunt17_PracticeModeControlTests
{
    [Fact]
    public void A_yes_stands_only_while_the_same_finished_race_is_on_the_board()
    {
        Assert.True(PracticeModeControl.StillTheFinishedRaceAskedAbout(4, 4, RaceState.Finished, warmingUpNow: false));

        // Auto start staged, counted down or started the next race behind the question.
        Assert.False(PracticeModeControl.StillTheFinishedRaceAskedAbout(4, 5, RaceState.Staging, warmingUpNow: false));
        Assert.False(PracticeModeControl.StillTheFinishedRaceAskedAbout(4, 5, RaceState.Countdown, warmingUpNow: false));
        Assert.False(PracticeModeControl.StillTheFinishedRaceAskedAbout(4, 5, RaceState.Racing, warmingUpNow: false));

        // Another race run and finished meanwhile is not the one asked about either.
        Assert.False(PracticeModeControl.StillTheFinishedRaceAskedAbout(4, 5, RaceState.Finished, warmingUpNow: false));

        // A warm-up opened over the finished race leaves the generation alone.
        Assert.False(PracticeModeControl.StillTheFinishedRaceAskedAbout(4, 4, RaceState.Finished, warmingUpNow: true));
        Assert.False(PracticeModeControl.StillTheFinishedRaceAskedAbout(4, 4, RaceState.Staging, warmingUpNow: false));
    }

    [Fact]
    public void Only_a_real_car_s_laps_stop_the_undo_of_clear_stats()
    {
        var ghost = new RacerStanding { TransponderId = Ghosts.Prefix + "1", Laps = 2 };
        var real = new RacerStanding { TransponderId = "1234567", Laps = 1 };

        Assert.True(GhostService.IsGhost(ghost.TransponderId));

        Assert.False(PracticeModeControl.ClearedLapsWouldBeLost(Array.Empty<RacerStanding>()));
        Assert.False(PracticeModeControl.ClearedLapsWouldBeLost(new[] { ghost }));
        Assert.True(PracticeModeControl.ClearedLapsWouldBeLost(new[] { ghost, real }));
        Assert.True(PracticeModeControl.ClearedLapsWouldBeLost(new[] { real }));
    }
}
