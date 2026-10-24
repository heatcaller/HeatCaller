namespace HeatCaller.Tests;

/// <summary>
/// Race Control fix from the 23 Oct 2026 bug hunt (10): the "Quick Race HH:mm"
/// stamp Quick Race writes was never cleared, so later freeform races were
/// saved under it. Reset now releases it - see QuickRaceStampToRelease.
/// </summary>
public sealed class BugHunt10_RaceControlControl_LayoutTests
{
    [Fact]
    public void A_Quick_Race_stamp_is_released()
    {
        var stamp = $"Quick Race {new DateTime(2026, 10, 23, 19, 5, 0):HH:mm}";

        Assert.Equal(stamp, RaceControlControl.QuickRaceStampToRelease(stamp));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Race")]
    [InlineData("Stock - A Main")]
    [InlineData("Quick Race")]
    [InlineData("Quick Race practice")]
    public void A_name_the_director_typed_is_kept(string label)
    {
        Assert.Null(RaceControlControl.QuickRaceStampToRelease(label));
    }

    [Fact]
    public void No_name_at_all_is_kept()
    {
        Assert.Null(RaceControlControl.QuickRaceStampToRelease(null));
    }
}
