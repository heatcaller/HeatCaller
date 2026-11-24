namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, follow-up requests (bundle A_program): changes other groups'
/// fixes needed in the Program tab's files to reach the director.
/// Pole Shootout's hand changes standing (F256) is pinned as behaviour; the
/// wiring that can't be driven without a window is pinned in the source, in
/// the style of the other bug hunt tests.
/// </summary>
public sealed class BugHunt24_Requests_A_programTests
{
    private static readonly string[] Field = { "q1", "q2", "q3", "q4" };

    // --- F256: a Pole Shootout run changed by hand stands --------------------------

    [Fact]
    public void BugHunt24_A_shootout_run_given_another_driver_by_hand_stands_while_qualifying_is_unchanged()
    {
        var runs = PoleShootout.BuildRuns("Stock", Field, 1, null, "Club");
        runs[0].AssignedTransponders = new List<string> { "x9" };

        Assert.Equal(PoleShootout.RefreshResult.Unchanged, PoleShootout.Refresh(runs, Field));
        Assert.Equal("x9", runs[0].AssignedTransponders.Single());
    }

    [Fact]
    public void BugHunt24_A_hand_change_stands_whatever_the_case_of_the_transponders()
    {
        var runs = PoleShootout.BuildRuns("Stock", Field, 1, null, "Club");
        runs[1].AssignedTransponders = new List<string> { "x9" };

        Assert.Equal(PoleShootout.RefreshResult.Unchanged, PoleShootout.Refresh(runs, new[] { "Q1", "Q2", "Q3", "Q4" }));
        Assert.Equal("x9", runs[1].AssignedTransponders.Single());
    }

    [Fact]
    public void BugHunt24_A_shootout_run_deleted_by_hand_is_still_built_again_so_the_shootout_can_finish()
    {
        // The runs left still count the deleted one, so leaving it deleted
        // would keep the shootout unfinished and the A-Main grid unset.
        var runs = PoleShootout.BuildRuns("Stock", Field, 1, null, "Club");
        runs.RemoveAt(0);

        Assert.Equal(PoleShootout.RefreshResult.Rebuild, PoleShootout.Refresh(runs, Field));
    }

    [Fact]
    public void BugHunt24_A_real_change_in_qualifying_is_still_followed_and_remembered()
    {
        var runs = PoleShootout.BuildRuns("Stock", Field, 1, null, "Club");
        var moved = new[] { "q2", "q1", "q3", "q4" };

        Assert.All(runs, r => Assert.True(Field.SequenceEqual(r.ShootoutField!)));
        Assert.Equal(PoleShootout.RefreshResult.Updated, PoleShootout.Refresh(runs, moved));
        Assert.Equal("q2", runs.Single(r => r.ShootoutRun == 4).AssignedTransponders.Single());
        Assert.All(runs, r => Assert.True(moved.SequenceEqual(r.ShootoutField!)));

        // A hand change after that stands too.
        runs[0].AssignedTransponders = new List<string> { "x9" };
        Assert.Equal(PoleShootout.RefreshResult.Unchanged, PoleShootout.Refresh(runs, moved));

        // And a field of another size is still built again.
        Assert.Equal(PoleShootout.RefreshResult.Rebuild, PoleShootout.Refresh(runs, new[] { "q2", "q1", "q3" }));
    }

    [Fact]
    public void BugHunt24_Shootout_runs_saved_before_the_field_was_kept_follow_the_old_rule()
    {
        var runs = PoleShootout.BuildRuns("Stock", Field, 1, null, "Club");

        foreach (var run in runs)
        {
            run.ShootoutField = null;
        }

        runs[0].AssignedTransponders = new List<string> { "x9" };
        Assert.Equal(PoleShootout.RefreshResult.Updated, PoleShootout.Refresh(runs, Field));

        foreach (var run in runs)
        {
            run.ShootoutField = null;
        }

        runs.RemoveAt(0);
        Assert.Equal(PoleShootout.RefreshResult.Rebuild, PoleShootout.Refresh(runs, Field));
    }

    // --- Wiring, pinned in the source ----------------------------------------------

    [Fact]
    public void BugHunt24_The_Program_judges_papers_against_the_race_night_not_the_calendar_day()
    {
        var source = Source("RaceProgramControl.Posting.cs");

        Assert.DoesNotContain("DeskPapers.Problems(", source);
        Assert.Contains("CheckInControl.PapersProblemsAt(p, _settings, DateTime.Now)", source);
    }

    [Fact]
    public void BugHunt24_The_Program_due_column_shows_times_in_the_clubs_clock()
    {
        Assert.Contains("g => DayScheduleControl.ShownTime(g.First().ScheduledTime.Trim())", Source("RaceProgramControl.cs"));
    }

    [Fact]
    public void BugHunt24_Move_Drivers_Between_Heats_can_ask_what_is_on_track_at_Save()
    {
        var body = Between(Source("RaceProgramControl.cs"), "using var form = new HeatBalanceForm(", "RosterEditOpen = true;");

        Assert.Contains("onTrackNow: () => RoundOnTrackId?.Invoke()", body);
    }

    [Fact]
    public void BugHunt24_The_Program_tabs_Start_refuses_on_a_closed_day()
    {
        var body = Between(Source("RaceProgramControl.cs"), "private void StartRound(RoundDefinition round)", "has no drivers assigned");

        Assert.Contains("ClosedDayRefusal()", body);
        Assert.Contains("return;", body);
    }

    [Fact]
    public void BugHunt24_The_calendar_outlines_tonights_race_night()
    {
        var source = Source("RaceCalendar.cs");

        Assert.DoesNotContain("DateTime.Today", source);
        Assert.Contains("day == TonightAtTheTrack.NightOf(DateTime.Now)", source);
    }

    [Fact]
    public void BugHunt24_The_nights_saving_drop_downs_take_no_turn_of_the_wheel()
    {
        var source = Source("NightFields.cs");

        Assert.Contains("foreach (var combo in new[] { Shape, Championship, FirstRound, GapPreset })", source);
        Assert.Contains("RefuseWheel(combo);", source);
        Assert.Contains("box.DroppedDown", source);
    }

    [Fact]
    public void BugHunt24_The_phones_program_list_names_late_cars_as_the_channel_may_see_them()
    {
        var body = Between(Source("RaceDayOpsProgram.cs"), "private OpResult ProgramList(", "var byClass");

        Assert.Contains("NextStepFor(_host.Program.NextStep(), context.Channel)", body);
    }

    [Fact]
    public void BugHunt24_Save_Run_Sheet_names_the_file_for_the_race_night()
    {
        Assert.Contains("run-sheet-{TonightAtTheTrack.NightOf(DateTime.Now):yyyy-MM-dd}.png", Source("DayScheduleControl.cs"));
    }

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("Couldn't find the repo root (no folder with check.cmd in it) above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string Between(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);
        Assert.True(end >= 0, $"\"{until}\" is not after \"{from}\" any more - this test needs updating.");

        return source[start..end];
    }
}
