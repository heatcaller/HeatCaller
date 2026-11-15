namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 20 (15 Nov 2026): Race Control's handicap recap in a shared heat,
/// a Replay Moment that runs past the end of the recording, a marshal swap
/// during the next race's warm-up, and the back-to-back rest for a driver on
/// his second car's transponder. (The demo-lap "best ever here" fix is tested
/// in VoiceSpeedPhase2And3Tests.)
/// </summary>
public sealed class BugHunt20_RaceControlFixesTests
{
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    [Fact]
    public void The_recap_s_handicap_ranks_only_the_class_s_own_drivers()
    {
        var source = Source("RaceControlControl.AfterRace.cs");
        var apply = source.IndexOf("raceHandicap = new RaceHandicap(mode, HandicapCalculator.Apply(", StringComparison.Ordinal);
        var filter = source.LastIndexOf(".Where(r => MixedHeats.Counts(round, round.ClassName, r.Transponder, r.ClassName, _profiles))", apply, StringComparison.Ordinal);

        Assert.True(apply > 0);
        Assert.True(filter > 0 && apply - filter < 400);
        Assert.DoesNotContain("HandicapCalculator.Apply(record.Results, handicaps, mode)", source);
    }

    [Fact]
    public void A_replay_that_outlasts_the_recording_leaves_the_end_of_the_race_held()
    {
        // Still recording at the press and at the end: start again, so the
        // replay is not replayed.
        Assert.True(RaceControlControl.RestartsAfterReplay(false, TimeSpan.FromMilliseconds(20)));

        // The recording stopped while the replay played on past the flag:
        // keep the end of the race.
        Assert.False(RaceControlControl.RestartsAfterReplay(false, TimeSpan.FromSeconds(3)));
        Assert.False(RaceControlControl.RestartsAfterReplay(false, TimeSpan.MaxValue));

        // Finished before the press: never.
        Assert.False(RaceControlControl.RestartsAfterReplay(true, TimeSpan.Zero));

        Assert.Contains("if (RestartsAfterReplay(finished, buffer.SinceLastAudio))", Source("RaceControlControl.Broadcast.cs"));
    }

    [Fact]
    public void The_round_in_the_box_still_to_run_is_the_next_race_for_a_marshal_swap()
    {
        // In its warm-up or On Air's opening: the round in the box is next,
        // though NextRound has moved on to the race after it.
        Assert.True(RaceControlControl.IsNextToStart("q2h3", "q2h3", activeStillToRun: true, nextId: "q2h4"));
        Assert.False(RaceControlControl.IsNextToStart("q2h4", "q2h3", activeStillToRun: true, nextId: "q2h4"));

        // On track or just run: the next race is the one after it.
        Assert.True(RaceControlControl.IsNextToStart("q2h4", "q2h3", activeStillToRun: false, nextId: "q2h4"));
        Assert.False(RaceControlControl.IsNextToStart("q2h3", "q2h3", activeStillToRun: false, nextId: "q2h4"));

        // No round in the box: the program's next.
        Assert.True(RaceControlControl.IsNextToStart("q1h1", null, activeStillToRun: true, nextId: "q1h1"));

        // And a swap for it then is late after its marshals were called.
        Assert.NotNull(MarshalSwapNotices.Lateness(false, true, marshalsCalled: true, marshalsBeingChecked: false, untilStart: null));
    }

    [Fact]
    public void Back_to_back_finds_a_driver_on_his_second_car_s_transponder()
    {
        string? mainOf(string t) => string.Equals(t, "B", StringComparison.OrdinalIgnoreCase) ? "A" : null;

        // Main transponder last race, alternate this one - and the other way round.
        Assert.Equal(new[] { "B" }, AutoStartClock.BackToBack(new[] { "A", "X" }, new[] { "B", "Y" }, mainOf));
        Assert.Equal(new[] { "a" }, AutoStartClock.BackToBack(new[] { "b" }, new[] { "a", "Y" }, mainOf));

        // Without the mapping, as before.
        Assert.Empty(AutoStartClock.BackToBack(new[] { "A" }, new[] { "B" }));

        Assert.Contains("AutoStartClock.BackToBack(justRan, next.AssignedTransponders, _profiles.PrimaryForAlternate)",
            Source("RaceControlControl.AutoStart.cs"));
    }
}
