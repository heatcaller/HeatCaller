namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), the race-audio recorder: the sweep for
/// recordings left behind by a crash leaves alone a wav a race is being
/// recorded to right now, however long ago it was last written - and takes
/// it up as before once the recorder has closed it.
/// </summary>
public sealed class BugHunt17_RaceAudioRecorderTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly string _folder;

    public BugHunt17_RaceAudioRecorderTests()
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

    [Fact]
    public void A_wav_still_being_recorded_is_not_swept_as_a_crashed_leftover()
    {
        File.WriteAllText(_data.PathOf("race-audio.json"), "{\"Enabled\":true,\"MinFreeMb\":100}");

        var store = RaceAudioStore.Load();
        store.FolderSetting = _folder;

        var recorder = RaceAudioRecorder.StartFor(store, "Heat 1", DateTime.UtcNow, _ => { });
        Assert.NotNull(recorder);

        string wav;

        try
        {
            wav = Assert.Single(Directory.EnumerateFiles(_folder, "*" + RaceAudioRecorder.InProgressExtension, SearchOption.AllDirectories));

            // As a PC with no sound device leaves it: open, and not written
            // for longer than the sweep's two minutes.
            File.SetLastWriteTimeUtc(wav, DateTime.UtcNow.AddMinutes(-10));

            var lines = new List<string>();
            RaceAudioRecorder.SweepOrphans(store, null, lines.Add);

            Assert.Empty(lines);
            Assert.True(File.Exists(wav));
        }
        finally
        {
            recorder!.Stop();
        }

        // Closed and left: now it is a leftover, and the sweep deals with it.
        File.SetLastWriteTimeUtc(wav, DateTime.UtcNow.AddMinutes(-10));

        var after = new List<string>();
        RaceAudioRecorder.SweepOrphans(store, null, after.Add);

        Assert.Contains(after, line => line.Contains("left behind by an earlier close", StringComparison.Ordinal));
        Assert.False(File.Exists(wav));
    }
}
