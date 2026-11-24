namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Big Screen roll call: a driver named or renamed
/// while the race is staged must show the new name at once, not keep the bare
/// transponder number until some other card flips.
/// </summary>
public sealed class BugHunt24_RollCallTests
{
    private static RollCall.Entry E(string transponder, string name, RollCall.State state = RollCall.State.Waiting)
        => new(transponder, name, state);

    [Fact]
    public void A_driver_named_during_staging_repaints_the_roll_call()
    {
        var before = new[] { E("1234567", "1234567"), E("7654321", "Alex") };
        var now = new[] { E("1234567", "Sam"), E("7654321", "Alex") };

        Assert.True(RollCallBoard.NeedsRedraw(before, now));
    }

    [Fact]
    public void The_same_cars_in_a_new_order_repaint_the_roll_call()
    {
        var before = new[] { E("1234567", "Sam"), E("7654321", "Alex") };
        var now = new[] { E("7654321", "Alex"), E("1234567", "Sam") };

        Assert.True(RollCallBoard.NeedsRedraw(before, now));
    }

    [Fact]
    public void An_unchanged_roll_call_does_not_repaint()
    {
        var before = new[] { E("1234567", "Sam"), E("7654321", "Alex", RollCall.State.Heard) };
        var now = new[] { E("1234567", "Sam"), E("7654321", "Alex", RollCall.State.Heard) };

        Assert.False(RollCallBoard.NeedsRedraw(before, now));
    }

    [Fact]
    public void A_card_flipping_or_a_car_joining_still_repaints_the_roll_call()
    {
        var before = new[] { E("1234567", "Sam") };

        Assert.True(RollCallBoard.NeedsRedraw(before, new[] { E("1234567", "Sam", RollCall.State.Heard) }));
        Assert.True(RollCallBoard.NeedsRedraw(before, new[] { E("1234567", "Sam"), E("7654321", "Alex") }));
    }
}
