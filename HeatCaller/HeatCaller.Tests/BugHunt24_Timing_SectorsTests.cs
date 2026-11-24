namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), sector times to the hundredth.
///
/// The board, the Big Screen "FASTEST SECTOR" banner, Lap Detail and Race
/// History print Sectors.Format; the voice and its caption print
/// RaceNumbers.Plainly / HundredthsShown, whole hundredths half up. Format
/// used "0.00" on ms / 1000.0, so a time ending in 5 ms hung on how the
/// runtime rounds a double just under the half. These pin the two together.
/// </summary>
public sealed class BugHunt24_Timing_SectorsTests
{
    [Theory]
    [InlineData(1005, "1.01")]
    [InlineData(5125, "5.13")]
    [InlineData(5135, "5.14")]
    [InlineData(12315, "12.32")]
    [InlineData(9995, "10.00")]
    [InlineData(59995, "60.00")]
    [InlineData(3410, "3.41")]
    [InlineData(4, "0.00")]
    public void BugHunt24_SectorFormat_RoundsHalfUpLikeTheVoice(long ms, string shown)
    {
        Assert.Equal(shown, Sectors.Format(ms));
        Assert.Equal(RaceNumbers.HundredthsShown(ms), Sectors.Format(ms) + "s");
    }

    [Fact]
    public void BugHunt24_SectorFormat_EveryTimeEndingInFiveAgreesWithTheCaption()
    {
        for (long ms = 1005; ms <= 59995; ms += 10)
        {
            Assert.Equal(RaceNumbers.HundredthsShown(ms), Sectors.Format(ms) + "s");
        }
    }

    [Theory]
    [InlineData(-5135, "-5.14")]
    [InlineData(12315, "+12.32")]
    [InlineData(-80, "-0.08")]
    [InlineData(120, "+0.12")]
    [InlineData(0, "0.00")]
    public void BugHunt24_SectorFormatDelta_RoundsHalfUpWithItsSign(long ms, string shown)
    {
        Assert.Equal(shown, Sectors.FormatDelta(ms));
    }

    [Fact]
    public void BugHunt24_SectorFormat_NoTimeIsADash()
    {
        Assert.Equal("-", Sectors.Format(0));
        Assert.Equal("1.01 / 12.32", Sectors.Line([1005, 12315]));
    }
}
