namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the stream's sponsor logo time and "presented
/// by" graphic counts were in memory only, so a mid-evening restart left the
/// sponsor report with the whole night's reads but only the logo time since
/// the restart.
/// </summary>
public sealed class BugHunt24_SponsorStreamTests
{
    private static readonly DateTime At = new DateTime(2026, 11, 14, 20, 0, 0, DateTimeKind.Local).ToUniversalTime();

    private static string CountTenSecondsAndTwoGraphics()
    {
        SponsorStream.ResetForTests();
        SponsorStream.Shown("s1", At);

        for (var i = 1; i <= 10; i++)
        {
            SponsorStream.Shown("s1", At.AddSeconds(i));
        }

        SponsorStream.GraphicShown("s1", "FastestLap", At.AddSeconds(11));
        SponsorStream.GraphicShown("s1", "Battle", At.AddSeconds(12));

        return SponsorStream.Save();
    }

    [Fact]
    public void Logo_time_and_graphic_counts_come_back_after_a_restart_on_the_same_night()
    {
        var saved = CountTenSecondsAndTwoGraphics();

        // The restart: everything in memory is gone.
        SponsorStream.ResetForTests();
        SponsorStream.Restore(saved, At.AddHours(1));

        var night = TonightAtTheTrack.NightOf(At.ToLocalTime());
        Assert.Equal(10, SponsorStream.SecondsOn(night)["s1"]);
        Assert.Equal(2, SponsorStream.GraphicsOn(night)["s1"]);
    }

    [Fact]
    public void A_save_from_last_night_does_not_come_back()
    {
        var saved = CountTenSecondsAndTwoGraphics();

        SponsorStream.ResetForTests();
        var nextNight = At.AddDays(1);
        SponsorStream.Restore(saved, nextNight);

        var night = TonightAtTheTrack.NightOf(nextNight.ToLocalTime());
        Assert.Empty(SponsorStream.SecondsOn(night));
        Assert.Empty(SponsorStream.GraphicsOn(night));
    }

    [Fact]
    public void Restoring_twice_does_not_double_the_counts()
    {
        var saved = CountTenSecondsAndTwoGraphics();

        SponsorStream.ResetForTests();
        SponsorStream.Restore(saved, At.AddHours(1));
        SponsorStream.Restore(saved, At.AddHours(1));

        var night = TonightAtTheTrack.NightOf(At.ToLocalTime());
        Assert.Equal(10, SponsorStream.SecondsOn(night)["s1"]);
        Assert.Equal(2, SponsorStream.GraphicsOn(night)["s1"]);
    }

    [Fact]
    public void A_save_that_will_not_read_is_passed_over()
    {
        SponsorStream.ResetForTests();
        SponsorStream.Restore("not a save", At);
        SponsorStream.Restore(string.Empty, At);

        var night = TonightAtTheTrack.NightOf(At.ToLocalTime());
        Assert.Empty(SponsorStream.SecondsOn(night));
        Assert.Empty(SponsorStream.GraphicsOn(night));
    }

    [Fact]
    public void Each_graphic_and_each_fifteen_seconds_of_logo_time_is_handed_on_to_be_kept()
    {
        SponsorStream.ResetForTests();
        var kept = new List<string>();
        SponsorStream.Changed = kept.Add;

        try
        {
            SponsorStream.Shown("s1", At);

            for (var i = 1; i <= 31; i++)
            {
                SponsorStream.Shown("s1", At.AddSeconds(i));
            }

            // Crossed 15 s and 30 s: two saves, not one per poll.
            Assert.Equal(2, kept.Count);

            SponsorStream.GraphicShown("s1", "Grid", At.AddSeconds(32));
            Assert.Equal(3, kept.Count);

            // The last one kept brings back what was counted.
            SponsorStream.ResetForTests();
            SponsorStream.Restore(kept[^1], At.AddSeconds(40));

            var night = TonightAtTheTrack.NightOf(At.ToLocalTime());
            Assert.Equal(31, SponsorStream.SecondsOn(night)["s1"]);
            Assert.Equal(1, SponsorStream.GraphicsOn(night)["s1"]);
        }
        finally
        {
            SponsorStream.ResetForTests();
        }
    }
}
