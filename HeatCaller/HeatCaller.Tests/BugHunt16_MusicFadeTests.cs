namespace HeatCaller.Tests;

/// <summary>
/// A track that starts during the fade-out (10 Nov 2026, bug hunt 16).
///
/// FadeOutAsync used to write its level only into the playing track's
/// volume. A track that ended part-way through the fade had the next one
/// opened at the full set level, and the device queued its first 400ms at
/// that level before the fade's next step - a burst of a new song at full
/// volume in a fade to silence. The fade's level is now kept by the player,
/// and both a track already on and one being opened are given it.
///
/// A real fade needs a sound device playing, so the fade is put part-way
/// through with HoldFadeForTests, and the place AdvanceTrack takes its
/// starting level from is read from the source, as BugHunt5AudioLockTests
/// does for the same reason.
/// </summary>
public class BugHunt16_MusicFadeTests
{
    [Fact]
    public void Part_way_through_a_fade_the_music_level_is_the_faded_one()
    {
        using var player = new MusicPlayer { Settings = new MusicSettings { Volume = 0.8f } };

        Assert.Equal(0.8f, player.MusicLevelForTests, 3);

        player.HoldFadeForTests(0.36f);

        // 0.8 x 0.36: what the next track opened now starts at.
        Assert.Equal(0.288f, player.MusicLevelForTests, 3);
    }

    [Fact]
    public void Moving_the_music_fader_mid_fade_keeps_the_fade()
    {
        using var player = new MusicPlayer { Settings = new MusicSettings { Volume = 0.8f } };

        player.HoldFadeForTests(0.5f);
        player.Settings.Volume = 0.6f;
        player.RefreshVolume();

        Assert.Equal(0.3f, player.MusicLevelForTests, 3);
    }

    [Fact]
    public void Calling_off_the_fade_puts_the_music_back_at_its_set_level()
    {
        using var player = new MusicPlayer { Settings = new MusicSettings { Volume = 0.8f } };

        player.HoldFadeForTests(0.2f);
        player.CancelFade();

        Assert.Equal(0.8f, player.MusicLevelForTests, 3);
    }

    [Fact]
    public void Stop_leaves_no_fade_behind_for_the_next_play()
    {
        using var player = new MusicPlayer { Settings = new MusicSettings { Volume = 0.8f } };

        player.HoldFadeForTests(0.2f);
        player.Stop();

        Assert.Equal(0.8f, player.MusicLevelForTests, 3);
    }

    [Fact]
    public void The_next_track_and_the_playing_one_both_take_the_level_with_the_fade_in_it()
    {
        var source = SourceOf("MusicPlayer.cs");

        Assert.Contains("startVolume = MusicLevelLocked();", source, StringComparison.Ordinal);
        Assert.Contains("_volume.Volume = MusicLevelLocked();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("startVolume = Math.Clamp(Settings.Volume", source, StringComparison.Ordinal);
    }

    private static string SourceOf(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null)
        {
            var candidate = Path.Combine(folder.FullName, "HeatCaller", "HeatCaller", fileName);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            folder = folder.Parent;
        }

        throw new FileNotFoundException($"No HeatCaller\\HeatCaller\\{fileName} above {AppContext.BaseDirectory}.");
    }
}
