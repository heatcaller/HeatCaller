namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19 (14 Nov 2026): closing mid-race and waiting on the clips
/// (#18), AltGr at the desk (#19), and the entries-close calls over a heat
/// still on track (#43).
/// </summary>
public sealed class BugHunt19_ClosingDeskEntriesTests
{
    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    // ------------------------------------------------------------ #18

    [Fact]
    public void The_race_already_agreed_to_does_not_stop_the_wait_to_close()
    {
        Assert.False(MainForm.RaceStartedWhileWaitingForClips(raceOnTrack: true, closingOnRunningRace: true));
    }

    [Fact]
    public void A_race_started_while_waiting_still_stops_it()
    {
        Assert.True(MainForm.RaceStartedWhileWaitingForClips(raceOnTrack: true, closingOnRunningRace: false));
        Assert.False(MainForm.RaceStartedWhileWaitingForClips(raceOnTrack: false, closingOnRunningRace: false));
    }

    [Fact]
    public void The_wait_notes_the_race_it_was_agreed_on_and_closes_without_asking_about_it_again()
    {
        var wait = Source("MainForm.ClosingWait.cs");
        var main = Source("MainForm.cs");

        Assert.Contains("_closingOnRunningRace = _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;", wait);
        Assert.Contains("_closingOnRunningRace &= raceOnTrack;", wait);
        Assert.Contains("if (RaceStartedWhileWaitingForClips(raceOnTrack, _closingOnRunningRace))", wait);
        Assert.Contains("&& !_closingAgreedMidRace", main);
    }

    // ------------------------------------------------------------ #19

    [Theory]
    [InlineData(Keys.Q | Keys.Control | Keys.Alt)] // '@' on a German keyboard
    [InlineData(Keys.D0 | Keys.Control | Keys.Alt)] // '@' on a French keyboard
    [InlineData(Keys.E | Keys.Control | Keys.Alt)] // the euro sign
    [InlineData(Keys.D7 | Keys.Control | Keys.Alt | Keys.Shift)]
    public void AltGr_characters_can_be_typed_at_the_desk(Keys key)
    {
        Assert.True(MainForm.DeskViewAllowsKey(key));
    }

    [Theory]
    [InlineData(Keys.F4 | Keys.Alt)]
    [InlineData(Keys.D1 | Keys.Alt)]
    [InlineData(Keys.F4 | Keys.Control | Keys.Alt)]
    [InlineData(Keys.S | Keys.Control)]
    public void Alt_shortcuts_and_function_keys_are_still_refused(Keys key)
    {
        Assert.False(MainForm.DeskViewAllowsKey(key));
    }

    // ------------------------------------------------------------ #43

    [Fact]
    public void A_heat_on_track_counts_as_racing_started()
    {
        var built = new List<RoundDefinition> { new() { Kind = RoundKind.Qualifier } };

        Assert.True(EntriesClose.RacingStarted(built, raceOnTrack: true));
        Assert.False(EntriesClose.RacingStarted(built, raceOnTrack: false));
    }

    [Fact]
    public void A_saved_race_still_counts_and_saved_practice_still_does_not()
    {
        var raced = new List<RoundDefinition> { new() { Kind = RoundKind.Qualifier, LinkedSessionId = "s1" } };
        var practised = new List<RoundDefinition> { new() { Kind = RoundKind.Practice, LinkedSessionId = "s1" } };

        Assert.True(EntriesClose.RacingStarted(raced, raceOnTrack: false));
        Assert.False(EntriesClose.RacingStarted(practised, raceOnTrack: false));
    }

    [Fact]
    public void No_entries_close_call_is_made_over_a_heat_on_track()
    {
        var close = new DateTime(2026, 11, 14, 18, 30, 0, DateTimeKind.Utc);
        var built = new List<RoundDefinition> { new() { Kind = RoundKind.Qualifier } };
        var started = EntriesClose.RacingStarted(built, raceOnTrack: true);

        Assert.Equal(EntriesClose.Call.None, EntriesClose.Due(close, close.AddSeconds(10), started, false, false));
        Assert.Equal(EntriesClose.Call.None, EntriesClose.Due(close, close.AddMinutes(-10), started, false, false));
    }

    [Fact]
    public void The_tick_passes_the_race_on_track_but_not_practice()
    {
        var tick = Source("MainForm.NightSetup.cs");

        Assert.Contains("raceOnTrack: !_race.IsPracticeMode && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing", tick);
    }
}
