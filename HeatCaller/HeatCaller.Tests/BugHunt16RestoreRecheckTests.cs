namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026): Restore Default Settings and Put Back Earlier
/// Settings checked for a running race only when their dialog opened. A
/// practice that started at the gate while the dialog was on screen was then
/// restarted over - frozen first, so it and its laps were never saved. The
/// race is now checked again once the dialog is confirmed
/// (MainForm.ConfirmedWithNothingStarted), and the restore is refused.
/// </summary>
public sealed class BugHunt16RestoreRecheckTests
{
    [Fact]
    public void PracticeStartedWhileTheDialogWasOpenRefusesTheRestore()
    {
        var practiceRunning = false;
        var said = 0;

        var go = MainForm.ConfirmedWithNothingStarted(
            () =>
            {
                // A car over the loop while the director reads the dialog.
                practiceRunning = true;
                return true;
            },
            () => practiceRunning,
            () => said++);

        Assert.False(go);
        Assert.Equal(1, said);
    }

    [Fact]
    public void ConfirmedWithNothingRunningGoesAhead()
    {
        var said = 0;

        Assert.True(MainForm.ConfirmedWithNothingStarted(() => true, () => false, () => said++));
        Assert.Equal(0, said);
    }

    [Fact]
    public void CancelledSaysNothing()
    {
        var said = 0;

        Assert.False(MainForm.ConfirmedWithNothingStarted(() => false, () => true, () => said++));
        Assert.Equal(0, said);
    }
}
