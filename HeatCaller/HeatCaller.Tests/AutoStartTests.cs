namespace HeatCaller.Tests;

/// <summary>
/// Race-night build plan, Phase 1 (11 Nov 2026): the automatic start between
/// races - counts the gap after each race, holds, waits for the announcer,
/// asks before starting without a car the loop hasn't heard, and starts once.
/// See AutoStartClock.
/// </summary>
public sealed class AutoStartTests
{
    private static readonly DateTime Flag = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<string> Nobody = Array.Empty<string>();

    private static DateTime At(double seconds) => Flag.AddSeconds(seconds);

    private static AutoStartClock Opened(TimeSpan? gap = null, IReadOnlyList<string>? resting = null)
    {
        var clock = new AutoStartClock();
        clock.Open("7", "heat2", Flag, gap ?? TimeSpan.FromSeconds(150), resting);
        return clock;
    }

    [Fact]
    public void A_closed_clock_does_nothing()
    {
        var clock = new AutoStartClock();

        Assert.Equal(AutoStartState.Off, clock.Step(At(500), false, Nobody));
        Assert.Null(clock.Remaining(At(0)));
    }

    [Fact]
    public void It_counts_the_gap_from_the_flag_and_starts_once_at_zero()
    {
        var clock = Opened();

        Assert.Equal(AutoStartState.Counting, clock.Step(At(1), false, Nobody));
        Assert.Equal(TimeSpan.FromSeconds(149), clock.Remaining(At(1)));
        Assert.Equal(AutoStartState.Counting, clock.Step(At(149), false, Nobody));
        Assert.Equal(AutoStartState.Start, clock.Step(At(150), false, Nobody));

        // Once: a second later it has nothing more to do.
        Assert.Equal(AutoStartState.Off, clock.Step(At(151), false, Nobody));
    }

    [Fact]
    public void Opening_again_for_the_same_race_keeps_the_count()
    {
        var clock = Opened();

        // The tick opens it every second with "now"; only the first counts.
        clock.Open("7", "heat2", At(60), TimeSpan.FromSeconds(150));

        Assert.Equal(At(150), clock.StartsAtUtc(At(60)));
    }

    [Fact]
    public void A_new_race_starts_a_new_count()
    {
        var clock = Opened();
        clock.Step(At(150), false, Nobody);

        clock.Open("8", "heat3", At(400), TimeSpan.FromSeconds(150));

        Assert.Equal(At(550), clock.StartsAtUtc(At(400)));
        Assert.Equal(AutoStartState.Counting, clock.Step(At(401), false, Nobody));
    }

    [Fact]
    public void Hold_stops_the_clock_and_resume_carries_on_from_where_it_stood()
    {
        var clock = Opened();

        clock.Hold(At(100));
        Assert.Equal(AutoStartState.Held, clock.Step(At(400), false, Nobody));

        clock.Resume(At(400));

        // 50 seconds were left when it was held; they are still left.
        Assert.Equal(TimeSpan.FromSeconds(50), clock.Remaining(At(400)));
        Assert.Equal(AutoStartState.Counting, clock.Step(At(449), false, Nobody));
        Assert.Equal(AutoStartState.Start, clock.Step(At(450), false, Nobody));
    }

    [Fact]
    public void Minutes_pushed_back_move_the_start()
    {
        var clock = Opened();

        Assert.Equal(At(210), clock.StartsAtUtc(At(0), pushedBackMinutes: 1));
        Assert.Equal(AutoStartState.Counting, clock.Step(At(150), false, Nobody, pushedBackMinutes: 1));
        Assert.Equal(AutoStartState.Start, clock.Step(At(210), false, Nobody, pushedBackMinutes: 1));
    }

    [Fact]
    public void Start_now_starts_at_once()
    {
        var clock = Opened();
        clock.Hold(At(10));

        clock.StartNow();

        Assert.Equal(AutoStartState.Start, clock.Step(At(11), false, Nobody));
    }

    [Fact]
    public void Time_up_while_the_announcer_talks_waits_for_the_floor()
    {
        var clock = Opened();

        Assert.Equal(AutoStartState.WaitingForFloor, clock.Step(At(160), floorBusy: true, Nobody));
        Assert.Equal(AutoStartState.Start, clock.Step(At(170), floorBusy: false, Nobody));
    }

    [Fact]
    public void A_car_not_seen_holds_the_start_until_the_director_approves()
    {
        var clock = Opened();
        var frank = new[] { "Frank" };

        Assert.Equal(AutoStartState.Counting, clock.Step(At(120), false, frank));
        Assert.Equal(AutoStartState.WaitingForCars, clock.Step(At(150), false, frank));
        Assert.Equal(AutoStartState.WaitingForCars, clock.Step(At(300), false, frank));
        Assert.Equal(new[] { "Frank" }, clock.NotSeen);

        clock.ApproveMissingCars();

        Assert.Equal(AutoStartState.Start, clock.Step(At(301), false, frank));
    }

    [Fact]
    public void A_car_that_turns_up_clears_the_wait_by_itself()
    {
        var clock = Opened();

        Assert.Equal(AutoStartState.WaitingForCars, clock.Step(At(150), false, new[] { "Frank" }));
        Assert.Equal(AutoStartState.Start, clock.Step(At(160), false, Nobody));
    }

    [Fact]
    public void Cars_are_only_looked_for_close_to_the_start()
    {
        var clock = Opened();

        // A car not yet on the loop two minutes out is normal, not news.
        clock.Step(At(30), false, new[] { "Frank" });
        Assert.Empty(clock.NotSeen);

        clock.Step(At(150 - AutoStartClock.LookForCarsFrom.TotalSeconds), false, new[] { "Frank" });
        Assert.Equal(new[] { "Frank" }, clock.NotSeen);
    }

    [Fact]
    public void A_driver_in_back_to_back_races_gets_a_rest()
    {
        var clock = Opened(resting: new[] { "Sam" });

        Assert.Equal(At(150) + AutoStartClock.RestForBackToBack, clock.StartsAtUtc(At(0)));
        Assert.Equal(new[] { "Sam" }, clock.Resting);
        Assert.StartsWith("starts in 4:30 (rest for Sam)",
            AutoStartClock.Words(AutoStartState.Counting, clock.Remaining(At(0)), clock.NotSeen, clock.Resting));
    }

    [Fact]
    public void Back_to_back_drivers_are_matched_by_transponder_ignoring_case()
    {
        var both = AutoStartClock.BackToBack(new[] { "abc1", "X2", "" }, new[] { "ABC1", "y3", "abc1" });

        Assert.Equal(new[] { "ABC1" }, both);
        Assert.Empty(AutoStartClock.BackToBack(null, new[] { "a" }));
    }

    [Fact]
    public void Close_ends_the_gap()
    {
        var clock = Opened();
        clock.Hold(At(5));

        clock.Close();

        Assert.False(clock.IsOpen);
        Assert.False(clock.IsHeld);
        Assert.Equal(AutoStartState.Off, clock.Step(At(200), false, Nobody));
    }

    [Theory]
    [InlineData(150, "2:30")]
    [InlineData(59.2, "1:00")]
    [InlineData(0, "0:00")]
    public void The_count_reads_minutes_and_seconds_rounded_up(double seconds, string expected)
    {
        Assert.Equal(expected, AutoStartClock.Clock(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void The_bar_says_what_the_clock_is_doing()
    {
        var left = TimeSpan.FromSeconds(102);

        Assert.Equal("starts in 1:42", AutoStartClock.Words(AutoStartState.Counting, left, Nobody, Nobody));
        Assert.Equal("start held - press Resume", AutoStartClock.Words(AutoStartState.Held, left, Nobody, Nobody));
        Assert.Equal("starts when the announcer finishes", AutoStartClock.Words(AutoStartState.WaitingForFloor, null, Nobody, Nobody));
        Assert.Equal("waiting for Frank and Sam - not seen by the loop",
            AutoStartClock.Words(AutoStartState.WaitingForCars, null, new[] { "Frank", "Sam" }, Nobody));
        Assert.Equal(string.Empty, AutoStartClock.Words(AutoStartState.Off, null, Nobody, Nobody));
    }

    [Fact]
    public void Names_read_the_way_they_are_said()
    {
        Assert.Equal("Frank, Sam and 2 more", AutoStartClock.Names(new[] { "Frank", "Sam", "Jo", "Al" }));
        Assert.Equal("Frank, Sam and Jo", AutoStartClock.Names(new[] { "Frank", "Sam", "Jo" }));
    }

    [Fact]
    public void Off_for_tonight_is_tonight_only()
    {
        Assert.True(AutoStartClock.OffTonight(switchedOn: true, offForNight: "2026-11-11", tonight: "2026-11-11"));
        Assert.False(AutoStartClock.OffTonight(switchedOn: true, offForNight: "2026-11-11", tonight: "2026-11-12"));
        Assert.True(AutoStartClock.OffTonight(switchedOn: false, offForNight: "", tonight: "2026-11-12"));
        Assert.False(AutoStartClock.OffTonight(switchedOn: true, offForNight: "", tonight: "2026-11-12"));
    }

    [Fact]
    public void Auto_start_is_on_by_default_with_a_two_and_a_half_minute_gap()
    {
        var settings = new AppSettings();

        Assert.True(settings.AutoStartBetweenRaces);
        Assert.Equal(150, settings.AutoStartGapSeconds);
        Assert.Equal(string.Empty, settings.AutoStartOffForNight);
    }

    /// <summary>The race control source, for checks on how the clock is wired in.</summary>
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }

    [Fact]
    public void The_start_goes_through_Start_Next_so_every_refusal_still_applies()
    {
        var source = Source("RaceControlControl.AutoStart.cs");

        Assert.Contains("case AutoStartState.Start:", source);
        Assert.Contains("StartNext();", source);
    }

    [Fact]
    public void Only_a_real_program_race_leads_to_an_automatic_start()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        Assert.Contains("_autoStartAfter = !record.IsNotScored() && record.RoundId is not null", source);
    }
}
