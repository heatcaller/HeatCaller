namespace HeatCaller.Tests;

/// <summary>
/// The tenth bug hunt's recordings-folder fix (23 Oct 2026): pruning an old
/// night in a club's own folder leaves a phone video at the night folder's
/// top level alone, and still removes HeatCaller's highlight videos from
/// Highlights.
///
/// (The rehearsal half - a rehearsal always records into its own folder -
/// is not pinned here: AppPaths.UseRehearsalRoot cannot be undone, and it
/// would leak into every test that runs after it.)
/// </summary>
public sealed class BugHunt10_RaceAudioStoreTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly string _folder;

    public BugHunt10_RaceAudioStoreTests()
    {
        // Never the store's default, which is under the real Documents -
        // and outside the default is where only our own files may go.
        _folder = Path.Combine(_data.Folder, "audio");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        _data.Dispose();
    }

    private RaceAudioStore Store(int keep)
    {
        var store = RaceAudioStore.Load();

        store.FolderSetting = _folder;
        store.KeepNights = keep;

        Assert.Equal(_folder, store.Folder);

        return store;
    }

    [Fact]
    public void Pruning_a_marked_night_outside_the_default_keeps_the_clubs_own_videos()
    {
        var old = Path.Combine(_folder, "2026-09-06");
        Directory.CreateDirectory(Path.Combine(old, "Highlights"));
        RaceAudioStore.MarkAsNight(old);
        File.WriteAllText(Path.Combine(old, "2026-09-06 1900 - Heat 1.mp3"), "x");
        File.WriteAllText(Path.Combine(old, "VID_0001.mp4"), "x");
        File.WriteAllText(Path.Combine(old, "Highlights", "2026-09-06 1900 - Heat 1 - P1 Dave.mp4"), "x");

        var newer = Path.Combine(_folder, "2026-10-11");
        Directory.CreateDirectory(newer);
        RaceAudioStore.MarkAsNight(newer);

        Store(1).PruneOldNights();

        Assert.True(File.Exists(Path.Combine(old, "VID_0001.mp4")));
        Assert.False(File.Exists(Path.Combine(old, "2026-09-06 1900 - Heat 1.mp3")));
        Assert.False(Directory.Exists(Path.Combine(old, "Highlights")));
        Assert.Equal(new[] { "VID_0001.mp4" }, Directory.EnumerateFileSystemEntries(old).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void DeleteOurOwnFrom_removes_a_highlight_video_and_the_empty_night()
    {
        var night = Path.Combine(_folder, "2026-09-13");
        Directory.CreateDirectory(Path.Combine(night, "Highlights"));
        RaceAudioStore.MarkAsNight(night);
        File.WriteAllText(Path.Combine(night, "Highlights", "2026-09-13 1900 - Heat 1 - P1 Dave.mp4"), "x");

        RaceAudioStore.DeleteOurOwnFrom(night);

        Assert.False(Directory.Exists(night));
    }
}
