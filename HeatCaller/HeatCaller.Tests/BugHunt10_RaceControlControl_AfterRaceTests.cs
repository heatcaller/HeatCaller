namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): with no race day filed, the pre-race Team
/// Principal's "each once a night" list was keyed on the calendar date and
/// cleared at midnight, so a night racing past 12 heard drivers' principals
/// twice. The key now follows the app's 6am-to-6am race night.
/// </summary>
public sealed class BugHunt10_RaceControlControl_AfterRaceTests
{
    [Fact]
    public void NoRaceDay_AfterMidnight_IsStillTheSameNight()
    {
        var evening = RaceControlControl.PrincipalNightKey(null, new DateTime(2026, 10, 23, 22, 30, 0));
        var lateMain = RaceControlControl.PrincipalNightKey(null, new DateTime(2026, 10, 24, 0, 15, 0));

        Assert.Equal("2026-10-23", evening);
        Assert.Equal(evening, lateMain);
    }

    [Fact]
    public void NoRaceDay_AfterSixAm_IsTheNextNight()
    {
        Assert.Equal("2026-10-24", RaceControlControl.PrincipalNightKey(null, new DateTime(2026, 10, 24, 6, 0, 0)));
    }

    [Fact]
    public void BlankEventId_CountsAsNoRaceDay()
    {
        Assert.Equal("2026-10-23", RaceControlControl.PrincipalNightKey("  ", new DateTime(2026, 10, 24, 1, 0, 0)));
    }

    [Fact]
    public void FiledRaceDay_UsesItsId()
    {
        Assert.Equal("evt-42", RaceControlControl.PrincipalNightKey("evt-42", new DateTime(2026, 10, 24, 1, 0, 0)));
    }
}
