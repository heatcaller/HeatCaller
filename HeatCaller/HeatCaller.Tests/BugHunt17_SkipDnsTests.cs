namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): undoing a DNS skip put the whole grid back as
/// it was when the skip was made, so a driver returned to the grid since
/// (the Next Up bar's "Put ... back") was dropped again - off the grid, with
/// no DNS mark. Only the skipped driver goes back now, into the grid as it is.
/// </summary>
public sealed class BugHunt17_SkipDnsTests
{
    private static RoundDefinition Race(params string[] grid) =>
        new() { Id = "q1", ClassName = "Stock", RoundLabel = "Q1 Heat 1", Kind = RoundKind.Qualifier, AssignedTransponders = grid.ToList() };

    [Fact]
    public void Undoing_a_later_skip_keeps_a_driver_put_back_since()
    {
        var race = Race("a", "b", "c");

        SkipDns.Skip(race, "a");
        var undoB = SkipDns.Skip(race, "b")!;

        // The desk's "Put a back": on the end of the grid, DNS off - not an undo step.
        race.SkippedDns!.Remove("a");
        race.AssignedTransponders = race.AssignedTransponders.Append("a").ToList();

        SkipDns.PutBack(race, "b", undoB);

        Assert.Equal(new[] { "b", "c", "a" }, race.AssignedTransponders);
        Assert.Null(race.SkippedDns);
    }

    [Fact]
    public void An_undo_with_nothing_changed_since_gives_the_grid_as_it_was()
    {
        var race = Race("a", "b", "c", "d");
        var undo = SkipDns.Skip(race, "C")!;

        SkipDns.PutBack(race, "C", undo);

        Assert.Equal(new[] { "a", "b", "c", "d" }, race.AssignedTransponders);
    }

    [Fact]
    public void Two_skips_undone_newest_first_give_the_grid_as_it_was()
    {
        var race = Race("a", "b", "c");
        var undoA = SkipDns.Skip(race, "a")!;
        var undoB = SkipDns.Skip(race, "b")!;

        SkipDns.PutBack(race, "b", undoB);
        SkipDns.PutBack(race, "a", undoA);

        Assert.Equal(new[] { "a", "b", "c" }, race.AssignedTransponders);
        Assert.Null(race.SkippedDns);
    }

    [Fact]
    public void A_driver_already_back_on_the_grid_is_not_added_twice()
    {
        var race = Race("a", "b");
        var undo = SkipDns.Skip(race, "b")!;

        race.AssignedTransponders = new List<string> { "B", "a" };

        SkipDns.PutBack(race, "b", undo);

        Assert.Equal(new[] { "B", "a" }, race.AssignedTransponders);
    }
}
