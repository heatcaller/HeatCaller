namespace HeatCaller.Tests;

/// <summary>
/// BUG HUNT 8 (20 Oct 2026) - AnnouncementService.Synthesis.cs.
///
/// The wait for a synthesis slot and the ElevenLabs request share one 30 s
/// limit. A line that got its slot late was cut off and reported as
/// "ElevenLabs didn't respond", which rested ElevenLabs for a minute though
/// only this laptop's queue was slow. Now a timeout after a wait that took
/// most of the limit counts as a slot timeout. These pin where that line is.
/// </summary>
public sealed class BugHunt8_AnnouncementService_Synthesis_csTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void ShortSlotWait_StillBlamesElevenLabs(int waitedSeconds)
    {
        Assert.False(AnnouncementService.SlotWaitTookMostOfLimit(TimeSpan.FromSeconds(waitedSeconds)));
    }

    [Theory]
    [InlineData(16.0)]
    [InlineData(27.0)]
    [InlineData(29.9)]
    public void LongSlotWait_IsBlamedOnTheLocalQueue(double waitedSeconds)
    {
        Assert.True(AnnouncementService.SlotWaitTookMostOfLimit(TimeSpan.FromSeconds(waitedSeconds)));
    }
}
