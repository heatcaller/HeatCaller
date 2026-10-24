namespace HeatCaller.Tests;

/// <summary>
/// The tenth bug hunt's race-audio recorder fix (23 Oct 2026): a race's wav
/// whose mp3 has already landed is renamed off ".recording.wav", and the
/// sweep deletes a leftover of that name rather than making a second mp3.
/// </summary>
public sealed class BugHunt10_RaceAudioRecorderTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly string _folder;

    public BugHunt10_RaceAudioRecorderTests()
    {
        RaceAudioStore.OffForTests = false;

        // Never the store's default, which is under the real Documents.
        _folder = Path.Combine(_data.Folder, "audio");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        RaceAudioStore.OffForTests = true;
        _data.Dispose();
    }

    private RaceAudioStore Store()
    {
        var store = RaceAudioStore.Load();

        store.FolderSetting = _folder;

        Assert.Equal(_folder, store.Folder);

        return store;
    }

    private string Night(string name)
    {
        var folder = Path.Combine(_folder, name);
        Directory.CreateDirectory(folder);
        RaceAudioStore.MarkAsNight(folder);
        return folder;
    }

    [Fact]
    public void The_published_name_swaps_the_in_progress_marker()
    {
        var night = Night("2026-10-16");
        var wav = Path.Combine(night, "A-Main" + RaceAudioRecorder.InProgressExtension);

        Assert.Equal(
            Path.Combine(night, "A-Main" + RaceAudioRecorder.PublishedExtension),
            RaceAudioRecorder.RenamedOffInProgress(wav, RaceAudioRecorder.PublishedExtension));

        // A file without the marker has nothing to take off.
        Assert.Null(RaceAudioRecorder.RenamedOffInProgress(Path.Combine(night, "A-Main.wav"), RaceAudioRecorder.PublishedExtension));
    }

    [Fact]
    public void A_leftover_from_a_race_already_saved_is_removed_and_never_made_into_a_second_mp3()
    {
        var night = Night("2026-10-16");
        File.WriteAllText(Path.Combine(night, "A-Main.mp3"), "the race's own mp3");

        var leftover = Path.Combine(night, "A-Main" + RaceAudioRecorder.PublishedExtension);
        File.WriteAllText(leftover, "not a wav");
        File.SetLastWriteTimeUtc(leftover, DateTime.UtcNow.AddDays(-7));

        var lines = new List<string>();
        RaceAudioRecorder.SweepOrphans(Store(), null, lines.Add);

        Assert.False(File.Exists(leftover));
        Assert.Equal(new[] { "A-Main.mp3" }, Directory.EnumerateFiles(night, "*.mp3").Select(Path.GetFileName).ToArray());
        Assert.DoesNotContain(lines, line => line.Contains("left behind by an earlier close", StringComparison.Ordinal));
    }

    [Fact]
    public void A_published_wav_written_moments_ago_is_left_for_the_next_sweep()
    {
        var night = Night("2026-10-23");

        var fresh = Path.Combine(night, "Heat 1" + RaceAudioRecorder.PublishedExtension);
        File.WriteAllText(fresh, "not a wav");

        RaceAudioRecorder.SweepOrphans(Store(), null, _ => { });

        Assert.True(File.Exists(fresh));
    }
}
