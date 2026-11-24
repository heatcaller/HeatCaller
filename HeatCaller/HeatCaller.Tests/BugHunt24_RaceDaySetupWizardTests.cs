namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, Set Up Tonight: a class added with Add Class was said to be
/// ticked for tonight, but only reached the day when the Classes step was
/// left with a button - closing the window with its X lost it.
/// </summary>
public sealed class BugHunt24_RaceDaySetupWizardTests
{
    [Fact]
    public void AddClass_OnADayWithItsOwnClassList_PutsTheNewClassOnTheDay()
    {
        var racing = RaceDaySetupWizard.ClassesRacingAfterAdd(new List<string> { "Stock Buggy" }, "Mod Buggy");

        Assert.NotNull(racing);
        Assert.Equal(new[] { "Stock Buggy", "Mod Buggy" }, racing);
    }

    [Fact]
    public void AddClass_OnADayRacingEveryClass_LeavesTheDayAlone()
    {
        // A day with no classes of its own races every class with drivers;
        // a one-name list would cut tonight down to the new class.
        Assert.Null(RaceDaySetupWizard.ClassesRacingAfterAdd(new List<string>(), "Mod Buggy"));
    }

    [Fact]
    public void AddClass_ForAClassTheDayAlreadyNames_LeavesTheDayAlone()
    {
        Assert.Null(RaceDaySetupWizard.ClassesRacingAfterAdd(new List<string> { "Stock Buggy", "MOD BUGGY" }, "Mod Buggy"));
    }
}
