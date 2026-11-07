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

    // Mute Everything during an audition (6 Nov 2026): it cleared the
    // audition flag by hand, skipping StopAudition's restore, so the
    // sidebar's unmute brought back the crowd and music the audition had
    // started, the crowd at race level. The handler needs a window, so this
    // reads the source.
    [Fact]
    public void Mute_everything_ends_an_audition_through_its_restore()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "MixerControl.cs"));

        var start = source.IndexOf("btnMuteAll.Click +=", StringComparison.Ordinal);
        Assert.True(start >= 0, "The Mute Everything handler has moved - this test needs its bounds updated.");

        var end = source.IndexOf("SetAnnouncerMuted(true);", start, StringComparison.Ordinal);
        Assert.True(end > start, "The Mute Everything handler no longer mutes through the sidebar - this test needs its bounds updated.");

        var beforeMute = source[start..end];

        Assert.Contains("StopAudition();", beforeMute);
        Assert.DoesNotContain("_auditioning = false;", beforeMute);
    }

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }
}
