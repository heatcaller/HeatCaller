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

    [Fact]
    public void Start_Next_holds_for_as_long_as_the_other_holds()
    {
        // 0.8 to 1 second was asked for; the same second Red, Checkered,
        // Reset and Skip take.
        Assert.InRange(HoldToAct.HoldFor.TotalMilliseconds, 800, 1000);
    }
}
