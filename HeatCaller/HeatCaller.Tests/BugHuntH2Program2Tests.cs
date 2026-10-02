namespace HeatCaller.Tests;

/// <summary>
/// The undo of Move Drivers Between Heats, Edit Drivers and Bump-ups stays
/// on the stack after a heat is filed, so it must not hand a heat that has
/// raced since its old drivers, nor undo a later edit along with it.
/// CheckRosterRestore is the rule all three undos share.
/// </summary>
public class BugHuntH2Program2Tests
{
    private static RoundDefinition Heat(string? session, params string[] drivers) => new()
    {
        ClassName = "Stock",
        RoundLabel = "Q2 Heat 2",
        LinkedSessionId = session,
        AssignedTransponders = drivers.ToList(),
    };

    private static RaceProgramControl.RosterRestore Restore(RoundDefinition round, string? sessionThen, string[] given, string[] old) =>
        new(round.Id, sessionThen, given.ToList(), old.ToList());

    [Fact]
    public void AnUnrunHeatStillHoldingTheMoveIsPutBack()
    {
        var heat = Heat(null, "S01", "S02", "S03");

        Assert.Equal(RaceProgramControl.RosterRestoreBlock.None,
            RaceProgramControl.CheckRosterRestore(heat, Restore(heat, null, new[] { "S01", "S02", "S03" }, new[] { "S01", "S02" })));
    }

    [Fact]
    public void AHeatRunSinceTheMoveIsLeftAlone()
    {
        // Moved X (S03) in, then the heat raced and was filed.
        var heat = Heat("session-1", "S01", "S02", "S03");

        Assert.Equal(RaceProgramControl.RosterRestoreBlock.RunSince,
            RaceProgramControl.CheckRosterRestore(heat, Restore(heat, null, new[] { "S01", "S02", "S03" }, new[] { "S01", "S02" })));
    }

    [Fact]
    public void AHeatRunAgainSinceIsLeftAlone()
    {
        var heat = Heat("session-2", "S01", "S02");

        Assert.Equal(RaceProgramControl.RosterRestoreBlock.RunSince,
            RaceProgramControl.CheckRosterRestore(heat, Restore(heat, "session-1", new[] { "S01", "S02" }, new[] { "S01", "S02", "S03" })));
    }

    [Fact]
    public void AHeatThatHadRunAlreadyCanStillBePutBack()
    {
        // Edit Drivers on a recorded heat, or the feeder of a bump-up: run
        // before the change, not since.
        var heat = Heat("session-1", "S01", "S02");

        Assert.Equal(RaceProgramControl.RosterRestoreBlock.None,
            RaceProgramControl.CheckRosterRestore(heat, Restore(heat, "session-1", new[] { "S01", "S02" }, new[] { "S01", "S02", "S03" })));
    }

    [Fact]
    public void AHeatEditedSinceIsLeftAlone()
    {
        var heat = Heat(null, "S01", "S04");

        Assert.Equal(RaceProgramControl.RosterRestoreBlock.EditedSince,
            RaceProgramControl.CheckRosterRestore(heat, Restore(heat, null, new[] { "S01", "S02", "S03" }, new[] { "S01", "S02" })));
    }

    [Fact]
    public void TheSameDriversInAnotherOrderCountAsEdited()
    {
        // The order is the grid order, so a reshuffle is an edit too.
        var heat = Heat(null, "S02", "S01");

        Assert.Equal(RaceProgramControl.RosterRestoreBlock.EditedSince,
            RaceProgramControl.CheckRosterRestore(heat, Restore(heat, null, new[] { "S01", "S02" }, new[] { "S01" })));
    }

    // CheckRosterUndo: a move or a bump-up changes several rounds together,
    // so it goes back as a whole or not at all.

    private static RoundDefinition Round(string label, string? session, params string[] drivers) => new()
    {
        ClassName = "Stock",
        RoundLabel = label,
        LinkedSessionId = session,
        AssignedTransponders = drivers.ToList(),
    };

    private static Func<string, RoundDefinition?> OnFile(params RoundDefinition[] rounds) =>
        id => rounds.FirstOrDefault(r => r.Id == id);

    // Move X (S05) from Heat 1 [S01,S02,S05] to Heat 2 [S03,S04].
    private static (RoundDefinition Heat1, RoundDefinition Heat2, List<RaceProgramControl.RosterRestore> Restores) MoveXFromHeat1ToHeat2(
        string? heat1SessionNow, string? heat2SessionNow)
    {
        var heat1 = Round("Q2 Heat 1", heat1SessionNow, "S01", "S02");
        var heat2 = Round("Q2 Heat 2", heat2SessionNow, "S03", "S04", "S05");

        var restores = new List<RaceProgramControl.RosterRestore>
        {
            new(heat1.Id, null, new List<string> { "S01", "S02" }, new List<string> { "S01", "S02", "S05" }),
            new(heat2.Id, null, new List<string> { "S03", "S04", "S05" }, new List<string> { "S03", "S04" }),
        };

        return (heat1, heat2, restores);
    }

    [Fact]
    public void AMoveNeitherHeatOfWhichHasRunGoesBack()
    {
        var (heat1, heat2, restores) = MoveXFromHeat1ToHeat2(null, null);

        var verdict = RaceProgramControl.CheckRosterUndo(restores, OnFile(heat1, heat2));

        Assert.True(verdict.CanRestore);
    }

    [Fact]
    public void AMoveWhoseDestinationHasRunIsRefusedWhole()
    {
        // The reported case: Heat 2 raced with X in it. Putting Heat 1 back
        // on its own would hand X a second run in the same round.
        var (heat1, heat2, restores) = MoveXFromHeat1ToHeat2(null, "session-1");

        var verdict = RaceProgramControl.CheckRosterUndo(restores, OnFile(heat1, heat2));

        Assert.False(verdict.CanRestore);
        Assert.Equal(new[] { "Q2 Heat 2" }, verdict.Blocked);
    }

    [Fact]
    public void AMoveWhoseSourceHasRunIsRefusedWhole()
    {
        // Putting Heat 2 back on its own would take X out of it and into no heat.
        var (heat1, heat2, restores) = MoveXFromHeat1ToHeat2("session-1", null);

        var verdict = RaceProgramControl.CheckRosterUndo(restores, OnFile(heat1, heat2));

        Assert.False(verdict.CanRestore);
        Assert.Equal(new[] { "Q2 Heat 1" }, verdict.Blocked);
    }

    [Fact]
    public void AMoveWithOneHeatEditedSinceIsRefusedWhole()
    {
        var (heat1, heat2, restores) = MoveXFromHeat1ToHeat2(null, null);
        heat2.AssignedTransponders = new List<string> { "S03", "S05" };

        var verdict = RaceProgramControl.CheckRosterUndo(restores, OnFile(heat1, heat2));

        Assert.False(verdict.CanRestore);
        Assert.Equal(new[] { "Q2 Heat 2" }, verdict.Blocked);
    }

    [Fact]
    public void AMoveWhoseSourceWasDeletedIsRefusedRatherThanLeaveTheDriverInNoHeat()
    {
        var (_, heat2, restores) = MoveXFromHeat1ToHeat2(null, null);

        var verdict = RaceProgramControl.CheckRosterUndo(restores, OnFile(heat2));

        Assert.False(verdict.CanRestore);
        Assert.Empty(verdict.Blocked);
        Assert.Equal(new[] { "S05" }, verdict.Stranded);
    }

    [Fact]
    public void AMoveWhoseDestinationWasDeletedCanStillPutTheSourceBack()
    {
        // X goes back into Heat 1 and is in no other heat, so nobody is
        // doubled and nobody is left out.
        var (heat1, _, restores) = MoveXFromHeat1ToHeat2(null, null);

        var verdict = RaceProgramControl.CheckRosterUndo(restores, OnFile(heat1));

        Assert.True(verdict.CanRestore);
    }

    // Bump-up: S05 and S06 promoted from the B-main (run, session-b) into
    // the A-main (not run).
    private static (RoundDefinition AMain, RoundDefinition BMain, List<RaceProgramControl.RosterRestore> Restores) BumpUp(
        string? aSessionNow, string? bSessionNow)
    {
        var aMain = Round("A-Main", aSessionNow, "S01", "S02", "S05", "S06");
        var bMain = Round("B-Main", bSessionNow, "S03", "S04");

        var restores = new List<RaceProgramControl.RosterRestore>
        {
            new(aMain.Id, null, new List<string> { "S01", "S02", "S05", "S06" }, new List<string> { "S01", "S02" }),
            new(bMain.Id, "session-b", new List<string> { "S03", "S04" }, new List<string> { "S03", "S04", "S05", "S06" }),
        };

        return (aMain, bMain, restores);
    }

    [Fact]
    public void ABumpUpGoesBackWhileTheFeederHasOnlyRunBefore()
    {
        var (aMain, bMain, restores) = BumpUp(null, "session-b");

        Assert.True(RaceProgramControl.CheckRosterUndo(restores, OnFile(aMain, bMain)).CanRestore);
    }

    [Fact]
    public void ABumpUpWhoseFeederHasRunAgainIsRefusedWhole()
    {
        // Putting the A-main back on its own took the promoted drivers out
        // of it and into no race at all.
        var (aMain, bMain, restores) = BumpUp(null, "session-b2");

        var verdict = RaceProgramControl.CheckRosterUndo(restores, OnFile(aMain, bMain));

        Assert.False(verdict.CanRestore);
        Assert.Equal(new[] { "B-Main" }, verdict.Blocked);
    }

    [Fact]
    public void ABumpUpWhoseMainHasRunIsRefusedWhole()
    {
        var (aMain, bMain, restores) = BumpUp("session-a", "session-b");

        var verdict = RaceProgramControl.CheckRosterUndo(restores, OnFile(aMain, bMain));

        Assert.False(verdict.CanRestore);
        Assert.Equal(new[] { "A-Main" }, verdict.Blocked);
    }

    [Fact]
    public void ABumpUpWhoseFeederWasDeletedIsRefused()
    {
        var (aMain, _, restores) = BumpUp(null, "session-b");

        var verdict = RaceProgramControl.CheckRosterUndo(restores, OnFile(aMain));

        Assert.False(verdict.CanRestore);
        Assert.Equal(new[] { "S05", "S06" }, verdict.Stranded);
    }
}
