namespace HeatCaller.Tests;

/// <summary>
/// From bug hunt 15 (B28): the Undo list stored only a step count per item,
/// so a phone change captured while the list was open was undone in place
/// of the step the operator picked. Items now carry the ids they showed,
/// and UndoThrough refuses when the stack no longer starts with them.
/// </summary>
public sealed class BugHunt15_UndoListTests
{
    [Fact]
    public void A_phone_change_landing_while_the_list_is_open_is_not_undone_by_the_pick()
    {
        var undo = new UndoService();
        var undone = new List<string>();
        undo.Capture("Delete class Stock Buggy", () => undone.Add("class"));

        // The list opens, showing "Delete class Stock Buggy" on top.
        var shown = undo.PendingIds.Take(1).ToList();

        // A phone logs a setup while it is open.
        undo.Capture("Phone: Log a setup for 7", () => undone.Add("setup"));

        Assert.Null(undo.UndoThrough(shown));
        Assert.Empty(undone);
        Assert.Equal(2, undo.Count);
    }

    [Fact]
    public void An_unchanged_stack_undoes_through_the_picked_step()
    {
        var undo = new UndoService();
        var undone = new List<string>();
        undo.Capture("A", () => undone.Add("A"));
        undo.Capture("B", () => undone.Add("B"));
        undo.Capture("C", () => undone.Add("C"));

        var shown = undo.PendingIds.Take(2).ToList();

        Assert.Equal(new[] { "C", "B" }, undo.UndoThrough(shown));
        Assert.Equal(new[] { "C", "B" }, undone);
        Assert.Equal("A", undo.Description);
    }

    [Fact]
    public void A_step_undone_elsewhere_while_the_list_is_open_is_not_followed_by_an_unseen_one()
    {
        var undo = new UndoService();
        var undone = new List<string>();
        undo.Capture("A", () => undone.Add("A"));
        undo.Capture("B", () => undone.Add("B"));

        var shown = undo.PendingIds.Take(1).ToList();
        undo.Undo(); // Ctrl+Z, say - B comes off.

        Assert.Null(undo.UndoThrough(shown));
        Assert.Equal(new[] { "B" }, undone);
        Assert.Equal(1, undo.Count);
    }
}
