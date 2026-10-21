namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 8 (20 Oct 2026), finding 84: closing the track designer
/// without saving threw the plan away with no question asked.
/// </summary>
public class BugHunt8_RcpTrackEditorForm_csTests
{
    [Theory]
    [InlineData(CloseReason.UserClosing)] // the title bar's X
    [InlineData(CloseReason.None)]        // Escape / the Cancel button on a modal dialog
    public void Asks_when_closing_with_changes_by_hand(CloseReason reason)
    {
        Assert.True(RcpTrackEditorForm.ShouldAskBeforeClosing(DialogResult.Cancel, reason, alreadyCancelled: false, changed: true));
    }

    [Fact]
    public void Never_asks_on_save()
    {
        Assert.False(RcpTrackEditorForm.ShouldAskBeforeClosing(DialogResult.OK, CloseReason.None, alreadyCancelled: false, changed: true));
    }

    [Fact]
    public void Never_asks_without_changes()
    {
        Assert.False(RcpTrackEditorForm.ShouldAskBeforeClosing(DialogResult.Cancel, CloseReason.UserClosing, alreadyCancelled: false, changed: false));
    }

    [Theory]
    [InlineData(CloseReason.WindowsShutDown)]
    [InlineData(CloseReason.TaskManagerClosing)]
    [InlineData(CloseReason.FormOwnerClosing)]
    [InlineData(CloseReason.ApplicationExitCall)]
    public void Never_holds_up_shutdown_or_the_owner(CloseReason reason)
    {
        Assert.False(RcpTrackEditorForm.ShouldAskBeforeClosing(DialogResult.Cancel, reason, alreadyCancelled: false, changed: true));
    }

    [Fact]
    public void Snapshot_matches_a_clone_and_differs_after_an_edit()
    {
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 1, Row = 2 });

        var before = RcpTrackEditorForm.DesignSnapshot(design);

        Assert.Equal(before, RcpTrackEditorForm.DesignSnapshot(design.Clone()));

        design.Tiles.Add(new RcpTile { Col = 3, Row = 4 });

        Assert.NotEqual(before, RcpTrackEditorForm.DesignSnapshot(design));
    }
}
