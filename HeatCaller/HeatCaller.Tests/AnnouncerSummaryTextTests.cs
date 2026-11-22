namespace HeatCaller.Tests;

/// <summary>
/// LAST RACE SUMMARY on the Announcer Screen, before there is a summary
/// (22 Nov 2026, the owner, from the Voice of the Night video). On a club
/// without AI Commentary it said "Commentary is off", which read as if the
/// announcer was off while it was calling every race.
/// </summary>
public sealed class AnnouncerSummaryTextTests
{
    [Fact]
    public void Without_AI_Commentary_it_says_only_the_summary_needs_it()
    {
        var text = AnnouncerControl.NoSummaryText(commentaryAvailable: false);

        Assert.DoesNotContain("Commentary is off", text, StringComparison.Ordinal);
        Assert.Contains("Race summaries need AI Commentary", text, StringComparison.Ordinal);
        Assert.Contains("Voice > AI Commentary", text, StringComparison.Ordinal);
        Assert.Contains("race calls", text, StringComparison.Ordinal);
    }

    [Fact]
    public void With_AI_Commentary_it_says_one_comes_after_the_flag()
    {
        Assert.Equal(
            "No race summary yet - one is generated after the checkered flag.",
            AnnouncerControl.NoSummaryText(commentaryAvailable: true));
    }
}
