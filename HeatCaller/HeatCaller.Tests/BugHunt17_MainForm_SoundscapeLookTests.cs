namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): a soundscape's look asked "Restart now?" having
/// checked for a running race only before the question. Practice that started
/// at the gate while it was on screen was restarted over on a yes. It now
/// goes through the same re-check as the restores
/// (MainForm.ConfirmedWithNothingStarted, tested in
/// BugHunt16RestoreRecheckTests). The question is a dialog on the main
/// window, so this checks the wiring by its source.
/// </summary>
public sealed class BugHunt17_MainForm_SoundscapeLookTests
{
    [Fact]
    public void The_restart_question_checks_the_race_again_once_answered()
    {
        var source = Source("MainForm.SoundscapeLook.cs");

        var recheck = source.IndexOf("if (ConfirmedWithNothingStarted(", StringComparison.Ordinal);
        var restart = source.IndexOf("RestartForNewLook();", StringComparison.Ordinal);

        Assert.True(recheck > 0, "the question is not asked through ConfirmedWithNothingStarted");
        Assert.True(restart > recheck, "the restart is not behind the re-check");
        Assert.Equal(restart, source.LastIndexOf("RestartForNewLook();", StringComparison.Ordinal));
        Assert.Contains("RaceUnderWayForRestore,", source[recheck..restart]);
    }

    [Fact]
    public void A_yes_with_practice_started_meanwhile_does_not_restart()
    {
        var practiceRunning = false;
        var startedMeanwhile = false;

        var restart = MainForm.ConfirmedWithNothingStarted(
            () =>
            {
                // A car over the loop while the question is on screen.
                practiceRunning = true;
                return true;
            },
            () => practiceRunning,
            () => startedMeanwhile = true);

        Assert.False(restart);
        Assert.True(startedMeanwhile);
    }

    /// <summary>The app source, for checks on how the pieces are wired in.</summary>
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
