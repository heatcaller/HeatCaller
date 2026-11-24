namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, filing a finished race: the session log's words about a demo
/// on a demo night, and about a late-saved race whose heat was filed since.
/// </summary>
public sealed class BugHunt24_RaceFilingServiceTests
{
    [Fact]
    public void DemoNight_WithARound_DemoLine_DoesNotSayItIsNotLinkedToAHeat()
    {
        var line = RaceFilingService.DemoSavedLine(demoMayFillTheProgramme: true, hasRound: true);

        Assert.DoesNotContain("not linked to a heat", line);
        Assert.Contains("earns no points", line);
        Assert.Contains("[DEMO]", line);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void DemoNotOnADemoNight_OrWithNoRound_StillSaysItIsNotLinked(bool demoNight, bool hasRound)
    {
        var line = RaceFilingService.DemoSavedLine(demoNight, hasRound);

        Assert.Contains("It is not linked to a heat", line);
        Assert.Contains("earns no points", line);
    }

    [Fact]
    public void LateSave_RoundWithheld_DropsCommitsNotTiedToAHeatLine_KeepsTheRest()
    {
        var seen = new List<string>();
        var log = RaceFilingService.LateSaveLog(seen.Add, roundWithheld: true);

        log("[session] Saved \"Heat 1\" to history with 4 racers.");
        log("[session] Saved to history, but NOT tied to a heat - no round was picked in the \"Round:\" box on Race Control, and "
            + "\"Start the next race automatically\" is off for this day (Race Day > Setup).");
        log("[session] Saved to history, but NOT tied to a heat - the race program has no unrecorded rounds left for it to advance to.");

        Assert.Single(seen);
        Assert.StartsWith("[session] Saved \"Heat 1\"", seen[0]);
    }

    [Fact]
    public void LateSave_RoundNotWithheld_PassesEveryLine()
    {
        var seen = new List<string>();
        var log = RaceFilingService.LateSaveLog(seen.Add, roundWithheld: false);

        log("[session] Saved \"Heat 1\" to history with 4 racers.");
        log("[session] Saved to history, but NOT tied to a heat - the race program has no unrecorded rounds left for it to advance to.");

        Assert.Equal(2, seen.Count);
    }
}
