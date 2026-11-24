namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the Music page (23 Nov 2026): the intro named a count of
/// included tracks the build doesn't ship; the seek bar ignored keys and the
/// wheel and could stay frozen after a cut-short drag; the Plays tile said
/// Theme music while the club's own music played; Restore Included Tracks
/// said "Nothing to add" after putting a deleted file back; and Clear
/// Library didn't say the included tracks come back.
/// </summary>
public sealed class BugHunt24_MusicControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- F177: the intro line -------------------------------------------

    [Fact]
    public void The_music_page_intro_names_no_count_of_included_tracks()
    {
        Assert.DoesNotContain("Eleven", MusicControl.IntroText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Add Track", MusicControl.IntroText, StringComparison.Ordinal);
    }

    // --- F178: the seek bar ----------------------------------------------

    [Fact]
    public void A_key_or_wheel_move_on_the_seek_bar_seeks_at_once()
    {
        var (dragging, seekNow) = MusicControl.SeekBarStep(false, MusicControl.SeekBarInput.Moved, MouseButtons.None);

        Assert.False(dragging);
        Assert.True(seekNow);
    }

    [Fact]
    public void A_drag_seeks_only_when_the_left_button_comes_up()
    {
        var (dragging, seekNow) = MusicControl.SeekBarStep(false, MusicControl.SeekBarInput.Press, MouseButtons.Left);
        Assert.True(dragging);
        Assert.False(seekNow);

        (dragging, seekNow) = MusicControl.SeekBarStep(dragging, MusicControl.SeekBarInput.Moved, MouseButtons.None);
        Assert.True(dragging);
        Assert.False(seekNow);

        (dragging, seekNow) = MusicControl.SeekBarStep(dragging, MusicControl.SeekBarInput.Release, MouseButtons.Left);
        Assert.False(dragging);
        Assert.True(seekNow);
    }

    [Fact]
    public void Losing_the_mouse_mid_drag_lets_go_of_the_seek_bar()
    {
        var (dragging, _) = MusicControl.SeekBarStep(false, MusicControl.SeekBarInput.Press, MouseButtons.Left);

        var (after, seekNow) = MusicControl.SeekBarStep(dragging, MusicControl.SeekBarInput.CaptureLost, MouseButtons.None);

        Assert.False(after);
        Assert.False(seekNow);
    }

    [Fact]
    public void A_right_click_on_the_seek_bar_neither_holds_it_nor_seeks()
    {
        var (dragging, seekNow) = MusicControl.SeekBarStep(false, MusicControl.SeekBarInput.Press, MouseButtons.Right);
        Assert.False(dragging);
        Assert.False(seekNow);

        (dragging, seekNow) = MusicControl.SeekBarStep(dragging, MusicControl.SeekBarInput.Release, MouseButtons.Right);
        Assert.False(dragging);
        Assert.False(seekNow);
    }

    [Fact]
    public void The_seek_bar_points_into_the_track_and_nowhere_without_one()
    {
        Assert.Equal(TimeSpan.FromSeconds(50), MusicControl.SeekTarget(500, TimeSpan.FromSeconds(100)));
        Assert.Equal(TimeSpan.FromSeconds(100), MusicControl.SeekTarget(1000, TimeSpan.FromSeconds(100)));
        Assert.Null(MusicControl.SeekTarget(500, TimeSpan.Zero));
    }

    // --- F179: the Plays tile --------------------------------------------

    [Fact]
    public void The_plays_tile_says_my_music_when_theme_music_is_chosen_but_has_nothing_to_play()
    {
        using var player = new MusicPlayer();
        player.Settings = new MusicSettings { PlayThemeMusic = true };
        player.Theme = null;

        Assert.False(player.ThemeMusicInUse);
        Assert.Equal("My music", MusicControl.PlaysTileText(player.ThemeMusicInUse));
        Assert.Equal("Theme music", MusicControl.PlaysTileText(true));
    }

    // --- F180: Restore Included Tracks -----------------------------------

    [Fact]
    public void Restore_counts_a_listed_included_track_whose_file_it_put_back()
    {
        var file = Path.Combine(_data.Folder, "Horizon Line.wav");
        var track = new MusicTrack { Title = "Horizon Line", FilePath = file, IsBuiltIn = true };
        var tracks = new List<MusicTrack> { track };

        // The file was deleted while the app was open: still listed, MISSING.
        var beforeIds = tracks.Select(t => t.Id).ToHashSet();
        var missingIds = MusicControl.MissingIncludedIds(tracks);
        Assert.Equal(new[] { track.Id }, missingIds);

        // Restore copies it back; the list keeps the same entry.
        Directory.CreateDirectory(_data.Folder);
        File.WriteAllText(file, "not really audio");

        var (added, putBack) = MusicControl.RestoreCounts(beforeIds, missingIds, tracks);

        Assert.Equal(0, added);
        Assert.Equal(1, putBack);
        Assert.Equal("Put back 1 missing included track.", MusicControl.RestoreStatus(added, putBack));
    }

    [Fact]
    public void Restore_counts_a_new_entry_even_when_the_same_pass_drops_another()
    {
        var dropped = new MusicTrack { Title = "Old Song", FilePath = Path.Combine(_data.Folder, "Old Song.mp3"), IsBuiltIn = true };
        var beforeIds = new[] { dropped.Id }.ToHashSet();
        var after = new List<MusicTrack>
        {
            new() { Title = "Cooldown Lap", FilePath = Path.Combine(_data.Folder, "Cooldown Lap.wav"), IsBuiltIn = true },
        };

        var (added, putBack) = MusicControl.RestoreCounts(beforeIds, new HashSet<string>(), after);

        Assert.Equal(1, added);
        Assert.Equal(0, putBack);
        Assert.Equal("Added 1 included track.", MusicControl.RestoreStatus(added, putBack));
    }

    [Fact]
    public void Restore_with_nothing_done_leaves_no_status_so_the_page_explains()
    {
        Assert.Null(MusicControl.RestoreStatus(0, 0));
    }

    // --- F181: Clear Library's question ----------------------------------

    [Fact]
    public void Clear_library_warns_that_included_tracks_come_back()
    {
        var withIncluded = MusicControl.ClearLibraryQuestion(12, anyIncluded: true);
        var ownOnly = MusicControl.ClearLibraryQuestion(3, anyIncluded: false);

        Assert.Contains("come back next time the app starts", withIncluded, StringComparison.Ordinal);
        Assert.Contains("Include / Exclude", withIncluded, StringComparison.Ordinal);
        Assert.DoesNotContain("come back", ownOnly, StringComparison.Ordinal);
        Assert.StartsWith("Remove all 3 tracks from the library?", ownOnly, StringComparison.Ordinal);
    }
}
