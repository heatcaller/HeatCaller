namespace HeatCaller.Tests;

/// <summary>
/// An included track the club deleted stays deleted (13 Nov 2026, bug hunt 17).
///
/// Remove says an included track comes back at the next start "unless you
/// delete the file from the music folder". Deleting the file was what
/// brought it back: every launch copied in any shipped file that was
/// missing, and the sync listed it again, ticked. Each file copied in is
/// now remembered in music.json and copied once.
/// </summary>
public sealed class BugHunt17_MusicModelsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>A folder standing in for the app's shipped tracks.</summary>
    private string Shipped(params string[] files)
    {
        var folder = Path.Combine(_data.Folder, "shipped");
        Directory.CreateDirectory(folder);

        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(folder, file), "not really audio");
        }

        return folder;
    }

    [Fact]
    public void An_included_track_deleted_from_the_music_folder_is_not_copied_in_again()
    {
        var shipped = Shipped("Horizon Line.wav", "Cooldown Lap.wav");
        var unwanted = Path.Combine(MusicStore.MusicDirectory, "Horizon Line.wav");
        var kept = Path.Combine(MusicStore.MusicDirectory, "Cooldown Lap.wav");

        var store = MusicStore.Load();
        store.InstallBundledFrom(shipped, again: false);
        store.Save();

        Assert.True(File.Exists(unwanted));
        Assert.True(File.Exists(kept));

        // The club removes it and deletes the file, as the dialog says to.
        File.Delete(unwanted);

        // The next launch.
        var nextLaunch = MusicStore.Load();
        nextLaunch.InstallBundledFrom(shipped, again: false);

        Assert.False(File.Exists(unwanted));
        Assert.True(File.Exists(kept));
        Assert.DoesNotContain(nextLaunch.Current.Tracks, t => t.DisplayName == "Horizon Line");
    }

    [Fact]
    public void Restore_included_tracks_brings_a_deleted_one_back()
    {
        var shipped = Shipped("Horizon Line.wav", "Cooldown Lap.wav");
        var unwanted = Path.Combine(MusicStore.MusicDirectory, "Horizon Line.wav");

        var store = MusicStore.Load();
        store.InstallBundledFrom(shipped, again: false);
        File.Delete(unwanted);

        store.InstallBundledFrom(shipped, again: true);

        Assert.True(File.Exists(unwanted));
    }

    [Fact]
    public void A_track_new_in_a_later_version_still_arrives()
    {
        var shipped = Shipped("Horizon Line.wav");

        var store = MusicStore.Load();
        store.InstallBundledFrom(shipped, again: false);
        store.Save();

        Shipped("Cooldown Lap.wav");

        var nextLaunch = MusicStore.Load();
        nextLaunch.InstallBundledFrom(shipped, again: false);

        Assert.True(File.Exists(Path.Combine(MusicStore.MusicDirectory, "Cooldown Lap.wav")));
    }

    [Fact]
    public void A_music_folder_with_nothing_in_it_gets_everything_whatever_the_list_says()
    {
        // A backup restored on a new PC: music.json came, the files did not.
        var shipped = Shipped("Horizon Line.wav", "Cooldown Lap.wav");

        var store = MusicStore.Load();
        store.InstallBundledFrom(shipped, again: false);
        store.Save();

        foreach (var file in Directory.GetFiles(MusicStore.MusicDirectory))
        {
            File.Delete(file);
        }

        // The same store, so the list is the one just saved: a fresh Load
        // would fill the folder from the app's real tracks first.
        store.InstallBundledFrom(shipped, again: false);

        Assert.True(File.Exists(Path.Combine(MusicStore.MusicDirectory, "Horizon Line.wav")));
        Assert.True(File.Exists(Path.Combine(MusicStore.MusicDirectory, "Cooldown Lap.wav")));
    }
}
