namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), RaceControlControl.SkipDns.cs.
/// </summary>
public sealed class BugHunt17_RaceControlControl_SkipDnsTests
{
    // --- "Skip me" for the race in the Round box, picked but not started ----

    /// <summary>
    /// The round in the Round box was always passed over as "the one on
    /// track", started or not. Before the first race, or while On Air opened
    /// it, an approved "Skip me" took the driver out of the race AFTER the
    /// one their phone showed, and left them on the grid about to start.
    /// </summary>
    [Theory]
    [InlineData(RaceState.Idle, false, false, false, true)]
    [InlineData(RaceState.Finished, false, false, false, true)]
    [InlineData(RaceState.Staging, false, false, false, false)]
    [InlineData(RaceState.Countdown, false, false, false, false)]
    [InlineData(RaceState.Racing, false, false, false, false)]
    // A demo on the round is a rehearsal: the round itself is still to run.
    [InlineData(RaceState.Racing, true, false, false, true)]
    // Open practice before the first race runs on the same clock, and the
    // Round box can already hold Q1 Heat 1: that heat is still to run, and
    // the phone lists it as next.
    [InlineData(RaceState.Racing, false, true, false, true)]
    [InlineData(RaceState.Staging, false, true, false, true)]
    // A finished race not filed yet may be this round's: passed over, as before.
    [InlineData(RaceState.Finished, false, false, true, false)]
    [InlineData(RaceState.Idle, false, false, true, false)]
    [InlineData(RaceState.Racing, false, true, true, false)]
    public void The_round_in_the_Round_box_is_passed_over_only_while_it_is_on_track(
        RaceState state, bool demo, bool practice, bool raceWaitingToBeFiled, bool stillToRun)
    {
        Assert.Equal(stillToRun, RaceControlControl.ActiveRoundStillToRun(state, demo, practice, raceWaitingToBeFiled));
    }

    [Fact]
    public void Skip_me_reads_practice_mode_as_the_phones_list_does()
    {
        Assert.Contains(
            "ActiveRoundStillToRun(_race.State, _race.IsDemoMode, _race.IsPracticeMode, HasUnfiledRaces)",
            Source("RaceControlControl.SkipDns.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_desk_skips_from_the_race_its_menu_named()
    {
        var skip = Source("RaceControlControl.SkipDns.cs");

        Assert.Contains("item.Click += (_, _) => SkipFromDesk(who, next);", skip, StringComparison.Ordinal);
        Assert.Contains("SkipDriverDns(transponder, name, \"skipped at the desk\", next)", skip, StringComparison.Ordinal);
        Assert.Contains("(inRace ?? NextRaceOf(transponder)) is not { } race", skip, StringComparison.Ordinal);
    }

    [Fact]
    public void More_time_for_the_race_in_the_Round_box_is_not_owed_to_a_gap_that_never_comes()
    {
        var autoStart = Source("RaceControlControl.AutoStart.cs");

        var theirs = autoStart.IndexOf("if (NextRaceOf(transponder) is not { } theirs)", StringComparison.Ordinal);
        var inHand = autoStart.IndexOf("if (string.Equals(theirs.Id, _activeRound?.Invoke()?.Id, StringComparison.Ordinal))", StringComparison.Ordinal);
        var owed = autoStart.IndexOf("_moreTimeOwed.Owe(theirs.Id", StringComparison.Ordinal);

        Assert.True(theirs > 0 && inHand > theirs && owed > inHand,
            "The race in hand must be answered before any minutes are owed to it.");
    }

    // --- "Put back" on the Next Up bar ---------------------------------------

    /// <summary>
    /// "Put back" added the driver to the end of the grid, so the top
    /// qualifier skipped from the A-Main by mistake was gridded last.
    /// </summary>
    [Fact]
    public void The_pole_sitter_put_back_is_on_pole_again()
    {
        var before = new[] { "A", "B", "C", "D" };

        Assert.Equal(new[] { "A", "B", "C", "D" }, RaceControlControl.BackInTheirSlot(new[] { "B", "C", "D" }, before, "A"));
    }

    [Fact]
    public void A_driver_from_the_middle_goes_back_behind_the_car_that_was_ahead()
    {
        var before = new[] { "A", "B", "C", "D" };

        // Matched whatever the case, and put back as it was asked for.
        Assert.Equal(new[] { "A", "B", "c", "D" }, RaceControlControl.BackInTheirSlot(new[] { "A", "B", "D" }, before, "c"));
    }

    [Fact]
    public void The_slot_holds_up_when_the_grid_has_changed_since_the_skip()
    {
        var before = new[] { "A", "B", "C", "D" };

        // B was skipped as well, D moved to the front and a late entry added.
        Assert.Equal(new[] { "D", "A", "C", "E" }, RaceControlControl.BackInTheirSlot(new[] { "D", "A", "E" }, before, "C"));

        // Nobody who was ahead is left: to the front.
        Assert.Equal(new[] { "C", "D", "E" }, RaceControlControl.BackInTheirSlot(new[] { "D", "E" }, before, "C"));
    }

    [Fact]
    public void With_no_grid_remembered_they_go_on_the_end_as_before()
    {
        Assert.Equal(new[] { "B", "C", "A" }, RaceControlControl.BackInTheirSlot(new[] { "B", "C" }, null, "A"));
        Assert.Equal(new[] { "B", "C", "A" }, RaceControlControl.BackInTheirSlot(new[] { "B", "C" }, new[] { "B", "C" }, "A"));
    }

    [Fact]
    public void A_skip_remembers_the_grid_and_Put_back_uses_it()
    {
        var skip = Source("RaceControlControl.SkipDns.cs");

        Assert.Contains("_gridBeforeSkip[$\"{roundId}|{transponder}\"] = undo.GridBefore;", skip, StringComparison.Ordinal);
        Assert.Contains("race.AssignedTransponders = BackInTheirSlot(race.AssignedTransponders, gridBefore, transponder);", skip, StringComparison.Ordinal);
        Assert.DoesNotContain("race.AssignedTransponders.Append(transponder)", skip, StringComparison.Ordinal);
    }

    private static string Source(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
        {
            folder = folder.Parent;
        }

        Assert.True(folder is not null, "No folder with check.cmd in it above " + AppContext.BaseDirectory);

        return File.ReadAllText(Path.Combine(folder!.FullName, "HeatCaller", "HeatCaller", fileName)).Replace("\r\n", "\n");
    }
}
