namespace HeatCaller.Tests;

/// <summary>
/// Mixer faders (2 Oct bug hunt): a fader stores percent / 100f, and the
/// meter refresh reads that back every 120 ms. Cutting off the fraction
/// turned 0.53f and 0.59f into 52 and 58, so those two faders snapped one
/// step down and Up could never get past them.
/// </summary>
public sealed class BugHuntMixerTests
{
    [Fact]
    public void Every_whole_percent_reads_back_as_itself()
    {
        for (var percent = 0; percent <= 100; percent++)
        {
            Assert.Equal(percent, MixerControl.LevelToPercent(percent / 100f));
        }
    }

    [Fact]
    public void A_level_stored_as_double_reads_back_as_itself()
    {
        // Voice and Sound keep their level as a double; the round trip
        // through it must still come back as the percent that was written.
        for (var percent = 0; percent <= 100; percent++)
        {
            double stored = percent / 100f;
            Assert.Equal(percent, MixerControl.LevelToPercent((float)stored));
        }
    }

    [Theory]
    [InlineData(-0.5f, 0)]
    [InlineData(1.7f, 100)]
    [InlineData(float.NaN, 0)]
    public void Out_of_range_levels_stay_on_the_fader(float level, int expected)
    {
        Assert.Equal(expected, MixerControl.LevelToPercent(level));
    }
}
