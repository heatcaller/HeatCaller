namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026), two MainForm fixes.
///
/// "One minute to go" heard late after a freeze got its fifteen seconds
/// from when it was heard, so it could be said with half a minute left.
/// It now runs from the minute itself, and past it the call is dropped.
///
/// The update offer's idle check read the live race journal even mid-race,
/// and that read could make the running race's journal write fail. The
/// journal is now read only when nothing else already holds the update.
/// </summary>
public sealed class BugHunt16_FinalMinuteTests
{
    private const long Now = 1_000_000;

    [Fact]
    public void One_minute_to_go_heard_on_time_gets_its_full_say_by()
    {
        Assert.Equal(Now + AnnouncementService.FinalMinuteSayWithinMs,
            AnnouncementService.FinalMinuteSayByMs(Now, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void One_minute_to_go_heard_late_counts_its_say_by_from_the_minute()
    {
        // Heard twelve seconds after the minute: three seconds left to say it.
        Assert.Equal(Now + AnnouncementService.FinalMinuteSayWithinMs - 12_000,
            AnnouncementService.FinalMinuteSayByMs(Now, TimeSpan.FromSeconds(48)));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(30)]
    [InlineData(5)]
    [InlineData(0)]
    public void One_minute_to_go_heard_past_its_window_is_not_said(int secondsLeft)
    {
        Assert.Null(AnnouncementService.FinalMinuteSayByMs(Now, TimeSpan.FromSeconds(secondsLeft)));
    }

    [Fact]
    public void With_no_clock_to_read_it_keeps_the_old_say_by()
    {
        Assert.Equal(Now + AnnouncementService.FinalMinuteSayWithinMs,
            AnnouncementService.FinalMinuteSayByMs(Now, null));
    }

    [Theory]
    [InlineData(true, RaceState.Idle)]
    [InlineData(false, RaceState.Staging)]
    [InlineData(false, RaceState.Racing)]
    [InlineData(false, RaceState.Finished)]
    public void A_held_update_does_not_read_the_journal(bool raceDayOpen, RaceState state)
    {
        var reads = 0;

        Assert.False(AppUpdates.NothingInProgress(raceDayOpen, state, () => { reads++; return false; }));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void An_idle_app_still_asks_the_journal()
    {
        var reads = 0;

        Assert.False(AppUpdates.NothingInProgress(false, RaceState.Idle, () => { reads++; return true; }));
        Assert.True(AppUpdates.NothingInProgress(false, RaceState.Idle, () => { reads++; return false; }));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void The_live_idle_check_hands_the_journal_over_as_a_question()
    {
        var main = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "MainForm.cs"));

        Assert.Contains("unfinishedRaceInJournal: () => RaceJournal.Unfinished() is not null), whenGone: false);", main, StringComparison.Ordinal);
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "HeatCaller", "HeatCaller")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
        }
    }
}
