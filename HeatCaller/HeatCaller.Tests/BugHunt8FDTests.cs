namespace HeatCaller.Tests;

/// <summary>
/// The eighth bug hunt's audio, voice-page and recording fixes (17 Oct
/// 2026): a club's own dated folder is never marked or emptied as a night
/// (A14-1), a recording started after the green keeps its clips on the
/// right part of the file (A14-3), a rivalries file with nulls in it loads
/// (A11-1), a consent recorded for a second voice keeps the first counted
/// as a clone (A10-2), and a driver's clip is found under any name they
/// went by tonight (A14-5).
///
/// Recording is turned on here and off again in Dispose, as
/// BugHunt7RaceAudioTests does, because some tests start a real recording
/// into this test's own folder. Nothing opens a sound device.
/// </summary>
public sealed class BugHunt8FDTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly string _folder;

    public BugHunt8FDTests()
    {
        RaceAudioStore.OffForTests = false;

        // Never the store's default, which is under the real Documents -
        // and outside the default is exactly where A14-1 bites.
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

    private static RaceAudioRecording RecordNothing(RaceAudioStore store, DateTime startedUtc)
    {
        var recorder = RaceAudioRecorder.StartFor(store, "Heat 1", startedUtc, _ => { });

        Assert.NotNull(recorder);

        try
        {
            return recorder!.Stop();
        }
        finally
        {
            recorder?.Dispose();
        }
    }

    // --- A14-1: the club's own dated folder -------------------------------

    [Fact]
    public void A_recording_into_a_dated_folder_the_club_already_had_does_not_mark_it()
    {
        var store = Store(6);
        var startedUtc = DateTime.UtcNow;

        // Pictures, say: the photo import made tonight's folder first.
        var night = store.FolderForNight(startedUtc.ToLocalTime());
        Directory.CreateDirectory(night);
        var photo = Path.Combine(night, "photo.jpg");
        File.WriteAllText(photo, "x");

        var recording = RecordNothing(store, startedUtc);

        Assert.Equal(night, recording.Folder);
        Assert.False(File.Exists(Path.Combine(night, RaceAudioStore.NightMarkerFileName)));
        Assert.True(File.Exists(photo));
    }

    [Fact]
    public void A_second_race_into_a_night_already_marked_keeps_the_marker()
    {
        var store = Store(6);
        var startedUtc = DateTime.UtcNow;

        var night = store.FolderForNight(startedUtc.ToLocalTime());
        Directory.CreateDirectory(night);
        RaceAudioStore.MarkAsNight(night);
        File.WriteAllText(Path.Combine(night, "2026-10-17 1900 - Heat 1.mp3"), "x");

        RecordNothing(store, startedUtc);

        Assert.True(File.Exists(Path.Combine(night, RaceAudioStore.NightMarkerFileName)));
    }

    [Fact]
    public void Pruning_a_marked_night_outside_the_default_leaves_the_clubs_own_files()
    {
        // The night HeatCaller made, which the photo import later filed
        // tonight's pictures into as well.
        var old = Path.Combine(_folder, "2026-09-06");
        Directory.CreateDirectory(Path.Combine(old, "Highlights"));
        RaceAudioStore.MarkAsNight(old);
        File.WriteAllText(Path.Combine(old, "2026-09-06 1900 - Heat 1.mp3"), "x");
        File.WriteAllText(Path.Combine(old, "2026-09-06 1930 - Heat 2.wav"), "x");
        File.WriteAllText(Path.Combine(old, "Highlights", "2026-09-06 1900 - Heat 1 - P1 Dave.mp3"), "x");
        File.WriteAllText(Path.Combine(old, "photo.jpg"), "x");

        var newer = Path.Combine(_folder, "2026-10-11");
        Directory.CreateDirectory(newer);
        RaceAudioStore.MarkAsNight(newer);
        File.WriteAllText(Path.Combine(newer, "race.mp3"), "x");

        Store(1).PruneOldNights();

        Assert.True(Directory.Exists(old));
        Assert.Equal(new[] { "photo.jpg" }, Directory.EnumerateFileSystemEntries(old).Select(Path.GetFileName).ToArray());
        Assert.True(File.Exists(Path.Combine(newer, "race.mp3")));
    }

    [Fact]
    public void Pruning_a_marked_night_with_nothing_else_in_it_removes_the_folder()
    {
        var old = Path.Combine(_folder, "2026-09-06");
        Directory.CreateDirectory(Path.Combine(old, "Highlights"));
        RaceAudioStore.MarkAsNight(old);
        File.WriteAllText(Path.Combine(old, "race.mp3"), "x");
        File.WriteAllText(Path.Combine(old, "Highlights", "clip.wav"), "x");

        var newer = Path.Combine(_folder, "2026-10-11");
        Directory.CreateDirectory(newer);
        RaceAudioStore.MarkAsNight(newer);

        var said = new List<string>();

        Store(1).PruneOldNights(said.Add);

        Assert.False(Directory.Exists(old));
        Assert.Contains("1 night", string.Join("\n", said));
    }

    // --- A14-3: recording started after the green -------------------------

    [Fact]
    public void A_green_before_the_recording_started_is_kept_negative()
    {
        var store = Store(6);
        var startedUtc = DateTime.UtcNow;

        var recorder = RaceAudioRecorder.StartFor(store, "Heat 1", startedUtc, _ => { });

        Assert.NotNull(recorder);

        try
        {
            // A race restored after a crash: four minutes in already.
            recorder!.NoteGreen(startedUtc.AddMinutes(-4));

            Assert.Equal(-240_000L, recorder.Stop().GreenAtMs);
        }
        finally
        {
            recorder?.Dispose();
        }
    }

    // --- A14-4: the clips still get cut without an mp3 encoder -------------

    [Fact]
    public void The_clips_are_cut_from_the_kept_wav_when_the_race_mp3_fails()
    {
        // Needs an encoder that refuses while CutWav still reads the file,
        // which a dev machine with Media Foundation cannot produce - so the
        // shape is pinned by reading the source.
        //
        // Deliberately stops at the kept wav rather than matching the whole
        // argument list. The bug was this branch returning before any clips
        // were cut, so the call existing AND being handed the kept wav is
        // the whole of what must stay true. Matching the trailing arguments
        // as well broke the test the first time WriteClips gained a
        // parameter (the highlight videos, 20 Oct 2026) while the behaviour
        // it pins was perfectly intact.
        var source = SourceOf("RaceAudioRecorder.cs");

        Assert.Contains("WriteClips(recording with { WavPath = kept }", source, StringComparison.Ordinal);
    }

    // --- A11-1: nulls in rivalries.json ------------------------------------

    [Fact]
    public void A_rivalries_file_with_nulls_in_it_loads_repaired()
    {
        File.WriteAllText(
            _data.PathOf("rivalries.json"),
            "[null, {\"Id\":\"x\",\"DriverA\":\"A\",\"DriverB\":null,\"Templates\":[null,\"hi\"]}, {\"Id\":\"y\",\"Templates\":null}]");

        var all = RivalryStore.Load().All;

        Assert.Equal(2, all.Count);
        Assert.All(all, r => Assert.NotNull(r.Templates));

        var x = all.Single(r => r.Id == "x");
        Assert.Equal(new[] { "hi" }, x.Templates);
        Assert.Equal(string.Empty, x.DriverB);

        // Empty, not the defaults: a null is not a request for them.
        Assert.Empty(all.Single(r => r.Id == "y").Templates);
    }

    // --- A10-2: a consent for a second voice -------------------------------

    [Fact]
    public void A_consent_for_another_voice_is_replacing_one_and_the_same_voice_is_not()
    {
        var held = new VoiceConsent.Record("clone-a", "Ann", DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddDays(-1));

        Assert.True(VoiceConsent.ReplacesAnotherVoice(held, "clone-b"));
        Assert.False(VoiceConsent.ReplacesAnotherVoice(held, " CLONE-A "));
        Assert.False(VoiceConsent.ReplacesAnotherVoice(null, "clone-b"));
    }

    [Fact]
    public void The_replaced_voice_still_counts_as_a_clone()
    {
        var settings = new AppSettings
        {
            HeatCallerConsentVoiceId = "clone-a",
            HeatCallerConsentWho = "Ann",
            HeatCallerConsentAtUtc = DateTime.UtcNow.AddDays(-3),
            HeatCallerConsentRevokedUtc = DateTime.UtcNow.AddDays(-1),
        };

        settings.HeatCallerClonedVoices =
            VoiceConsent.KeepReplacedVoiceAsClone(settings.HeatCallerClonedVoices, settings.HeatCallerConsentVoiceId);
        settings.HeatCallerConsentVoiceId = "clone-b";

        Assert.True(settings.HeatCallerVoiceIsCloned("clone-a"));
        Assert.True(settings.HeatCallerVoiceIsCloned("clone-b"));

        // Once, however often it is kept.
        Assert.Single(VoiceConsent.KeepReplacedVoiceAsClone(new[] { "Clone-A" }, "clone-a"));
    }

    // --- A14-5: a driver's clip under any name they went by tonight --------

    [Fact]
    public void A_drivers_clip_is_found_under_any_of_their_names()
    {
        var night = Path.Combine(_data.Folder, "night");
        var highlights = Path.Combine(night, DriversOwnClip.FolderName);
        Directory.CreateDirectory(highlights);

        var clip = Path.Combine(highlights, "2026-10-17 1900 - Heat 1 - P1 Dave.mp3");
        File.WriteAllText(clip, "x");

        Assert.Equal(clip, DriversOwnClip.FindForAny(night, new[] { "Dave Smith", "Dave" }));
        Assert.Null(DriversOwnClip.FindForAny(night, new[] { "Dave Smith" }));
        Assert.Null(DriversOwnClip.FindForAny(night, new string?[] { null, "  " }));
        Assert.Equal(clip, DriversOwnClip.FindFor(night, "Dave"));
    }

    // ------------------------------------------------------------------

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
