namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the race-day manager's results ops (RaceDayOpsResults.cs):
///  - #259: the phone's correction question said a class's mains "have
///    already been seeded" when the corrected qualifier was from another
///    race day, or when no race day was open at all.
/// </summary>
public sealed class BugHunt24_RaceDayOpsResultsTests
{
    private static RoundDefinition Round(RoundKind kind, string className = "Stock") =>
        new() { Kind = kind, ClassName = className };

    [Fact]
    public void A_qualifier_from_an_earlier_day_does_not_warn_about_tonights_mains()
    {
        var lastWeeksQualifier = Round(RoundKind.Qualifier);
        var tonightsQualifier = Round(RoundKind.Qualifier);
        var tonightsMain = Round(RoundKind.Main);
        var tonight = new HashSet<string>(StringComparer.Ordinal) { tonightsQualifier.Id, tonightsMain.Id };

        Assert.False(RaceDayService.MainsAlreadySeededFor(
            lastWeeksQualifier, tonight, new[] { tonightsQualifier, tonightsMain }));
    }

    [Fact]
    public void With_no_race_day_open_no_qualifier_warns_about_seeded_mains()
    {
        var qualifier = Round(RoundKind.Qualifier);
        var main = Round(RoundKind.Main);

        Assert.False(RaceDayService.MainsAlreadySeededFor(qualifier, null, new[] { qualifier, main }));
    }

    [Fact]
    public void Tonights_qualifier_with_tonights_mains_still_warns()
    {
        var qualifier = Round(RoundKind.Qualifier);
        var main = Round(RoundKind.Main);
        var tonight = new HashSet<string>(StringComparer.Ordinal) { qualifier.Id, main.Id };

        Assert.True(RaceDayService.MainsAlreadySeededFor(qualifier, tonight, new[] { qualifier, main }));
    }

    [Fact]
    public void Tonights_qualifier_without_mains_yet_does_not_warn()
    {
        var qualifier = Round(RoundKind.Qualifier);
        var tonight = new HashSet<string>(StringComparer.Ordinal) { qualifier.Id };

        Assert.False(RaceDayService.MainsAlreadySeededFor(qualifier, tonight, new[] { qualifier }));
    }
}
