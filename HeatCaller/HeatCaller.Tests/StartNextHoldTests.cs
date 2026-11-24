namespace HeatCaller.Tests;

/// <summary>
/// Hold Start Next for a demo run (11 Nov 2026): what a full hold of the
/// Next Up bar's Start Next comes to, and that the let-go of a completed
/// hold is never also taken as the click that starts the race for real.
/// </summary>
public sealed class StartNextHoldTests
{
    [Theory]
    [InlineData(RaceState.Idle)]
    [InlineData(RaceState.Finished)]
    public void A_full_hold_between_races_runs_the_next_round_as_a_demo(RaceState state)
    {
        Assert.Equal(StartNextHoldOutcome.RunDemo, StartNextHold.Decide(state, onAirRunning: false, stepOwed: false, hasNextRound: true));
    }

    [Fact]
    public void A_hold_is_ignored_while_a_race_is_under_way()
    {
        foreach (var state in Enum.GetValues<RaceState>())
        {
            if (state is RaceState.Idle or RaceState.Finished)
            {
                continue;
            }

            Assert.Equal(StartNextHoldOutcome.RaceUnderWay, StartNextHold.Decide(state, onAirRunning: false, stepOwed: false, hasNextRound: true));

            // Whatever else is true at the same time.
            Assert.Equal(StartNextHoldOutcome.RaceUnderWay, StartNextHold.Decide(state, onAirRunning: true, stepOwed: true, hasNextRound: false));
        }
    }

    [Fact]
    public void A_hold_is_ignored_while_On_Air_is_opening_a_race()
    {
        Assert.Equal(StartNextHoldOutcome.OnAirOpening, StartNextHold.Decide(RaceState.Idle, onAirRunning: true, stepOwed: false, hasNextRound: true));
    }

    [Fact]
    public void A_hold_runs_no_demo_while_the_program_owes_a_step_or_has_nothing_next()
    {
        Assert.Equal(StartNextHoldOutcome.StepOwed, StartNextHold.Decide(RaceState.Finished, onAirRunning: false, stepOwed: true, hasNextRound: true));
        Assert.Equal(StartNextHoldOutcome.NothingNext, StartNextHold.Decide(RaceState.Finished, onAirRunning: false, stepOwed: false, hasNextRound: false));
    }

    [Fact]
    public void The_tooltip_says_hold_for_a_demo_run()
    {
        Assert.Contains("Hold for a demo run", StartNextHold.Tip);
    }

    [Fact]
    public void A_short_click_still_starts_the_race()
    {
        var clicks = new HeldClickFilter();

        clicks.Pressed();

        Assert.True(clicks.ClickActs());

        // And the next one, and a click from the keyboard with no press.
        clicks.Pressed();
        Assert.True(clicks.ClickActs());
        Assert.True(clicks.ClickActs());
    }

    [Fact]
    public void The_let_go_of_a_completed_hold_does_not_also_start_the_race()
    {
        var clicks = new HeldClickFilter();

        clicks.Pressed();
        clicks.Held();

        Assert.False(clicks.ClickActs());

        // Swallowed once only: the next click is a click.
        Assert.True(clicks.ClickActs());
    }

    [Fact]
    public void A_hold_whose_let_go_never_arrived_cannot_swallow_the_next_click()
    {
        // A dialog took the mouse mid-hold, so no click followed the hold.
        var clicks = new HeldClickFilter();

        clicks.Pressed();
        clicks.Held();

        // The next press clears it.
        clicks.Pressed();

        Assert.True(clicks.ClickActs());
    }

    /// <summary>
    /// Let go before the demo, nothing happens (23 Nov 2026, Asher: "when
    /// clicking and holding the start button on race day if you release it
    /// prior to it starting as a demo it should go back to it's unpressed
    /// state and not the popup window"). It used to be a click however long
    /// it was held, and the start's popup came up.
    /// </summary>
    [Theory]
    [InlineData(80, true)]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(700, false)]
    [InlineData(990, false)]
    public void A_press_held_past_a_click_and_let_go_before_the_demo_does_nothing(long heldMs, bool starts)
    {
        var now = 10_000L;
        var clicks = new HeldClickFilter(() => now);

        clicks.Down();
        now += heldMs;

        Assert.Equal(starts, clicks.ClickActs());

        // Whatever that press came to, the next click is a click.
        clicks.Down();
        now += 100;
        Assert.True(clicks.ClickActs());
    }

    [Fact]
    public void Space_or_Enter_with_no_mouse_press_is_always_a_click()
    {
        var now = 10_000L;
        var clicks = new HeldClickFilter(() => now);

        // A long press that went nowhere (a dialog took the mouse), then the let-go was handled.
        clicks.Down();
        now += 5_000;
        clicks.Pressed();

        Assert.True(clicks.ClickActs());
    }

    [Fact]
    public void Both_Start_buttons_time_the_press()
    {
        static string Source(string file)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
            {
                dir = dir.Parent;
            }

            return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
        }

        Assert.Contains("_startNextClicks.Down();", Source("RaceControlControl.Layout.cs"), StringComparison.Ordinal);

        // Race Day's Start only while it offers a demo: on any other step it is a plain button.
        var today = Source("TodayControl.cs");
        Assert.Contains("_heldClicks.Down();", today, StringComparison.Ordinal);
        Assert.True(today.IndexOf("if (DemoRound() is not null)", StringComparison.Ordinal) < today.IndexOf("_heldClicks.Down();", StringComparison.Ordinal));
    }

    [Fact]
    public void Start_Next_holds_for_as_long_as_the_other_holds()
    {
        // 0.8 to 1 second was asked for; the same second Red, Checkered,
        // Reset and Skip take.
        Assert.InRange(HoldToAct.HoldFor.TotalMilliseconds, 800, 1000);
    }
}
