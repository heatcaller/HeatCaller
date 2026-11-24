namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, Suggest Handicaps: changing "Use last" or "Typical race"
/// rebuilt the list with every driver at their starting tick, so a driver
/// the director had unticked was ticked again and a locked driver ticked on
/// purpose was unticked. The rule for each row's tick is
/// HandicapSuggestTicks.StartTicked.
/// </summary>
public sealed class BugHunt24_HandicapFormsTests
{
    [Fact]
    public void SuggestTicks_DriverUntickedByDirector_StaysUnticked()
    {
        var previous = new Dictionary<string, bool> { ["1001"] = false };

        Assert.False(HandicapSuggestTicks.StartTicked(previous, "1001", locked: false));
    }

    [Fact]
    public void SuggestTicks_LockedDriverTickedByDirector_StaysTicked()
    {
        var previous = new Dictionary<string, bool> { ["2002"] = true };

        Assert.True(HandicapSuggestTicks.StartTicked(previous, "2002", locked: true));
    }

    [Fact]
    public void SuggestTicks_DriverNewToTheList_StartsTickedUnlessLocked()
    {
        var previous = new Dictionary<string, bool> { ["1001"] = false };

        Assert.True(HandicapSuggestTicks.StartTicked(previous, "3003", locked: false));
        Assert.False(HandicapSuggestTicks.StartTicked(previous, "3003", locked: true));
    }

    [Fact]
    public void SuggestTicks_FirstFill_StartsAtDefaults()
    {
        var none = new Dictionary<string, bool>();

        Assert.True(HandicapSuggestTicks.StartTicked(none, "1001", locked: false));
        Assert.False(HandicapSuggestTicks.StartTicked(none, "2002", locked: true));
    }
}
