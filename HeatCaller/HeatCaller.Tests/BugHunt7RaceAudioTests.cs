namespace HeatCaller.Tests;

/// <summary>
/// The seventh bug hunt's race-audio fixes (13 Oct 2026): the prune only
/// deletes nights HeatCaller made, a meeting past midnight is one night,
/// and the ceiling still applies when no mp3 could be made.
///
/// Recording is turned on here and off again in Dispose, as RaceAudioTests
/// does, because one test below starts a real recording into this test's
/// own folder.
/// </summary>
public sealed class BugHunt7RaceAudioTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly string _folder;

    public BugHunt7RaceAudioTests()
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

    private RaceAudioStore Store(int keep)
    {
        var store = RaceAudioStore.Load();

        store.FolderSetting = _folder;
        store.KeepNights = keep;

        Assert.Equal(_folder, store.Folder);

        return store;
    }

    private string Night(string name, bool marked)
    {
        var folder = Path.Combine(_folder, name);

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "file.mp3"), "x");

        if (marked)
        {
            RaceAudioStore.MarkAsNight(folder);
        }

        return folder;
    }

    private string[] NightsLeft() =>
        Directory.EnumerateDirectories(_folder)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void A_dated_folder_the_club_already_had_is_never_deleted()
    {
        // Pictures, say, where the photo import files by date.
        Night("2026-08-01", marked: false);
        Night("2026-09-14", marked: false);
        Night("2026-09-27", marked: true);
        Night("2026-10-11", marked: true);

        Store(1).PruneOldNights();

        Assert.Equal(new[] { "2026-08-01", "2026-09-14", "2026-10-11" }, NightsLeft());
    }

    [Fact]
    public void Marking_a_night_twice_keeps_one_marker_and_never_throws()
    {
        var folder = Night("2026-10-11", marked: true);

        RaceAudioStore.MarkAsNight(folder);
        RaceAudioStore.MarkAsNight(Path.Combine(_folder, "never-made", "2026-10-11"));

        Assert.True(File.Exists(Path.Combine(folder, RaceAudioStore.NightMarkerFileName)));
    }

    [Fact]
    public void A_recording_marks_the_night_folder_it_makes()
    {
        var store = Store(6);

        var recorder = RaceAudioRecorder.StartFor(store, "Heat 1", DateTime.UtcNow, _ => { });

        Assert.NotNull(recorder);

        try
        {
            var recording = recorder!.Stop();

            Assert.True(File.Exists(Path.Combine(recording.Folder, RaceAudioStore.NightMarkerFileName)));
        }
        finally
        {
            recorder?.Dispose();
        }
    }

    [Fact]
    public void A_meeting_that_runs_past_midnight_is_one_night()
    {
        var store = Store(6);

        var night = Path.Combine(_folder, "2026-10-13");

        Assert.Equal(night, store.FolderForNight(new DateTime(2026, 10, 13, 19, 0, 0)));
        Assert.Equal(night, store.FolderForNight(new DateTime(2026, 10, 14, 0, 40, 0)));

        // The next evening is the next night.
        Assert.Equal(Path.Combine(_folder, "2026-10-14"), store.FolderForNight(new DateTime(2026, 10, 14, 19, 0, 0)));
    }

    [Fact]
    public void The_ceiling_still_applies_when_no_mp3_could_be_made()
    {
        Night("2026-09-06", marked: true);
        var tonight = Night("2026-10-11", marked: true);

        // Not a wav at all, so the encoder refuses it - as a PC with no mp3
        // encoder refuses every race.
        var wav = Path.Combine(tonight, "Heat 1" + RaceAudioRecorder.InProgressExtension);
        File.WriteAllText(wav, "not a wav");

        var recording = new RaceAudioRecording(wav, tonight, "Heat 1", DateTime.Now, 0, 60_000, 0, null);

        RaceAudioRecorder.Publish(recording, Store(1), null, null, _ => { });

        Assert.Equal(new[] { "2026-10-11" }, NightsLeft());
    }

    [Fact]
    public void The_ceiling_still_applies_when_the_recording_was_too_short_to_keep()
    {
        Night("2026-09-06", marked: true);
        var tonight = Night("2026-10-11", marked: true);

        var recording = new RaceAudioRecording(
            Path.Combine(tonight, "Heat 1" + RaceAudioRecorder.InProgressExtension), tonight, "Heat 1", DateTime.Now, 0, 100, 0, null);

        RaceAudioRecorder.Publish(recording, Store(1), null, null, _ => { });

        Assert.Equal(new[] { "2026-10-11" }, NightsLeft());
    }
}
