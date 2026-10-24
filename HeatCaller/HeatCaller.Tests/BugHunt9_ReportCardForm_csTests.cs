
namespace HeatCaller.Tests;

/// <summary>Bug hunt 9 (22 Oct 2026): a saved report card is named for the night it covers.</summary>
public class BugHunt9_ReportCardForm_csTests
{
    [Fact]
    public void SuggestedFileName_UsesTheNightNotToday()
    {
        var name = ReportCardForm.SuggestedFileName("Sam", new DateTime(2026, 10, 15, 21, 30, 0));

        Assert.Equal("report-card-Sam-2026-10-15.png", name);
    }

    [Fact]
    public void SuggestedFileName_TwoNightsGetDifferentNames()
    {
        Assert.NotEqual(
            ReportCardForm.SuggestedFileName("Sam", new DateTime(2026, 10, 8)),
            ReportCardForm.SuggestedFileName("Sam", new DateTime(2026, 10, 15)));
    }

    [Fact]
    public void SuggestedFileName_SpacesBecomeDashes()
    {
        Assert.Equal("report-card-Sam-Lee-2026-10-15.png",
            ReportCardForm.SuggestedFileName("Sam Lee", new DateTime(2026, 10, 15)));
    }
}
