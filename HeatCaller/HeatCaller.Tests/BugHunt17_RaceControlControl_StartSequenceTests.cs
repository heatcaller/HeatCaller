namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: the undo of a race abandoned by Stage or Countdown was good
/// for the session generation of the first Stage only, so a second Stage
/// before the green - which moves the generation on - made Undo refuse with
/// "the new race has already gone green", and the abandoned race's laps were
/// lost. A Stage that only restarts the start the undo is waiting on now
/// carries the undo with it (RaceControlControl.RestageKeepsAbandonedRaceUndo).
/// </summary>
public sealed class BugHunt17_RaceControlControl_StartSequenceTests
{
    [Fact]
    public void Stage_pressed_again_during_the_new_starts_staging_or_countdown_keeps_the_undo()
    {
        Assert.True(RaceControlControl.RestageKeepsAbandonedRaceUndo(
            RaceState.Staging, tookOverDemo: false, generationBefore: 7, undoGeneration: 7));

        // Stage after a Countdown that was confirmed over the running race.
        Assert.True(RaceControlControl.RestageKeepsAbandonedRaceUndo(
            RaceState.Countdown, tookOverDemo: false, generationBefore: 7, undoGeneration: 7));
    }

    [Fact]
    public void A_Stage_after_the_new_race_went_green_or_was_reset_does_not()
    {
        // The green, the flag and Reset all move the generation on and leave
        // Staging and Countdown; a Stage after them starts another race.
        Assert.False(RaceControlControl.RestageKeepsAbandonedRaceUndo(
            RaceState.Finished, tookOverDemo: false, generationBefore: 8, undoGeneration: 7));
        Assert.False(RaceControlControl.RestageKeepsAbandonedRaceUndo(
            RaceState.Idle, tookOverDemo: false, generationBefore: 8, undoGeneration: 7));

        // Staged again later on, for a different race.
        Assert.False(RaceControlControl.RestageKeepsAbandonedRaceUndo(
            RaceState.Staging, tookOverDemo: false, generationBefore: 9, undoGeneration: 7));
    }

    [Fact]
    public void The_Stage_that_abandons_the_race_and_one_that_ends_a_demo_do_not()
    {
        // The first Stage: the undo it has just captured is not filled in yet.
        Assert.False(RaceControlControl.RestageKeepsAbandonedRaceUndo(
            RaceState.Racing, tookOverDemo: false, generationBefore: 6, undoGeneration: -1));

        Assert.False(RaceControlControl.RestageKeepsAbandonedRaceUndo(
            RaceState.Staging, tookOverDemo: true, generationBefore: 7, undoGeneration: 7));
    }
}
