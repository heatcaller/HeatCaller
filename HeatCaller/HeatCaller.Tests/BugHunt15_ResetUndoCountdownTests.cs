namespace HeatCaller.Tests;

/// <summary>
/// Race Control fix from the 9 Nov 2026 bug hunt (15), B6: undoing a Reset
/// made during the countdown put the session back at Countdown, and the
/// state change replayed the countdown ticks - with nothing armed to drop
/// the green at their end. The ticks are no longer replayed while a snapshot
/// is put back, and the director is told what to press instead.
/// </summary>
public sealed class BugHunt15_ResetUndoCountdownTests
{
    [Fact]
    public void A_start_put_back_at_Countdown_says_to_press_Countdown_or_Green()
    {
        var notice = RaceControlControl.StartPutBackNotice(RaceState.Countdown);

        Assert.NotNull(notice);
        Assert.Contains("Countdown", notice);
        Assert.Contains("Green", notice);
    }

    [Fact]
    public void A_start_put_back_at_Staging_says_to_press_Countdown()
    {
        var notice = RaceControlControl.StartPutBackNotice(RaceState.Staging);

        Assert.NotNull(notice);
        Assert.Contains("Countdown", notice);
    }

    [Theory]
    [InlineData(RaceState.Idle)]
    [InlineData(RaceState.Racing)]
    [InlineData(RaceState.Finished)]
    public void Nothing_is_said_for_a_race_put_back_anywhere_else(RaceState state)
    {
        Assert.Null(RaceControlControl.StartPutBackNotice(state));
    }

    [Fact]
    public void The_countdown_and_staging_calls_are_skipped_while_a_snapshot_is_put_back()
    {
        // The announcer calls live on the UI control, which the tests do not
        // build, so this reads the source: each start-step call must sit
        // behind the restore guard.
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "HeatCaller", "HeatCaller", "RaceControlControl.RaceEvents.cs"));

        foreach (var call in new[] { "_announcer.NotifyCountdownStarted(", "_announcer.NotifyRaceStaging(" })
        {
            var at = source.IndexOf(call, StringComparison.Ordinal);
            Assert.True(at > 0, $"{call} not found");

            var before = source[Math.Max(0, at - 200)..at];
            Assert.Contains("if (!_suppressFlagAnnouncements)", before);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HeatCaller", "HeatCaller.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
