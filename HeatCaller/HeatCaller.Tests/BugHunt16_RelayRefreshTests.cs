namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026): the ten-minute relay refresh forgot the site,
/// schedule, tracks and the rest, but not Qualifying or Sponsors - so a copy
/// of either the relay refused or lost never came back until it changed,
/// which for sponsors at a meeting is never.
/// </summary>
public sealed class BugHunt16_RelayRefreshTests
{
    [Theory]
    [InlineData(RelayChannel.Qualifying)]
    [InlineData(RelayChannel.Sponsors)]
    public void TenMinuteRefreshForgetsQualifyingAndSponsors(RelayChannel channel)
    {
        Assert.Contains(channel, MainForm.RelayDocumentsForgottenOnRefresh);
    }

    [Theory]
    [InlineData(RelayChannel.Site)]
    [InlineData(RelayChannel.Programme)]
    [InlineData(RelayChannel.Achievements)]
    [InlineData(RelayChannel.Tracks)]
    [InlineData(RelayChannel.CheckIn)]
    [InlineData(RelayChannel.Manage)]
    [InlineData(RelayChannel.Signup)]
    [InlineData(RelayChannel.Standings)]
    public void TenMinuteRefreshStillForgetsTheOthers(RelayChannel channel)
    {
        Assert.Contains(channel, MainForm.RelayDocumentsForgottenOnRefresh);
    }
}
