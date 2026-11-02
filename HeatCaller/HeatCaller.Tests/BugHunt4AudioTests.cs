using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// Music, ambience and the announcer's own sounds (bug hunt 4, 4 Oct 2026).
///
/// music.json and ambience.json locked at launch were replaced by the first
/// change; the Mixer's greyed-out "Music after the recap" still faded music
/// started by hand; Repeat Race Summary offered an earlier race's recap after
/// a race that got none; Re-record all handed back the saved takes instead of
/// new ones; and a warning's buzzer borrowed the Invalid lap sound - off by
/// default, and treated like a lap chime.
/// </summary>
public sealed class BugHunt4AudioTests : IDisposable
{
    private readonly TempAppData _data = new();

    // VoiceLibrary.Shared is one folder for the whole run, fixed the first
    // time anything touches it - TempAppData does not move it. A take left
    // there turns up in later tests: the layout test failed on 1 Nov 2026
    // when this class's lines filled the Voice page's saved-lines list.
    private readonly List<(VoiceProfile Profile, string Text)> _saved = new();

    public void Dispose()
    {
        foreach (var (profile, text) in _saved)
        {
            VoiceLibrary.Shared.Forget(profile, text);
        }

        _data.Dispose();
    }

    // --- music.json and ambience.json locked at startup ---------------------

    [Fact]
    public void A_music_library_locked_at_startup_is_not_written_over()
    {
        var club = MusicStore.Load();
        club.Current.Tracks.Add(new MusicTrack { Title = "Club Anthem", FilePath = Path.Combine(_data.Folder, "Club Anthem.mp3") });
        club.Save();
        var before = File.ReadAllText(_data.PathOf("music.json"));

        MusicStore tonight;
        using (new FileStream(_data.PathOf("music.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = MusicStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);
        Assert.DoesNotContain(tonight.Current.Tracks, t => t.Title == "Club Anthem");

        // The Mixer's Music fader, once whatever held the file has let go.
        tonight.Current.Volume = 0.9f;
        tonight.Save();

        Assert.Equal(before, File.ReadAllText(_data.PathOf("music.json")));
        Assert.Contains(MusicStore.Load().Current.Tracks, t => t.Title == "Club Anthem");
    }

    [Fact]
    public void A_readable_music_library_is_not_held()
    {
        var club = MusicStore.Load();
        club.Current.Tracks.Add(new MusicTrack { Title = "Club Anthem", FilePath = Path.Combine(_data.Folder, "Club Anthem.mp3") });
        club.Save();

        var store = MusicStore.Load();

        Assert.False(store.LoadedUnreadable);
        store.Current.Volume = 0.9f;
        store.Save();

        var again = MusicStore.Load();
        Assert.Equal(0.9f, again.Current.Volume);
        Assert.Contains(again.Current.Tracks, t => t.Title == "Club Anthem");
    }

    [Fact]
    public void Ambience_locked_at_startup_is_not_written_over()
    {
        var club = AmbienceStore.Load();
        club.Current.Layers.Add(new AmbienceLayer { Name = "Club crowd", FilePath = Path.Combine(_data.Folder, "crowd.wav") });
        club.Save();
        var before = File.ReadAllText(_data.PathOf("ambience.json"));

        AmbienceStore tonight;
        using (new FileStream(_data.PathOf("ambience.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = AmbienceStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);
        Assert.Empty(tonight.Current.Layers);

        // A soundscape applied from Set Up Tonight, once the lock has gone.
        tonight.Current.Layers.Add(new AmbienceLayer { Name = "Soundscape bed" });
        tonight.Save();

        Assert.Equal(before, File.ReadAllText(_data.PathOf("ambience.json")));
        Assert.Contains(AmbienceStore.Load().Current.Layers, l => l.Name == "Club crowd");
    }

    [Fact]
    public void A_readable_ambience_file_is_not_held()
    {
        var club = AmbienceStore.Load();
        club.Current.Layers.Add(new AmbienceLayer { Name = "Club crowd", FilePath = Path.Combine(_data.Folder, "crowd.wav") });
        club.Save();

        var store = AmbienceStore.Load();

        Assert.False(store.LoadedUnreadable);
        store.Current.Layers.Add(new AmbienceLayer { Name = "Pit lane" });
        store.Save();

        Assert.Equal(new[] { "Club crowd", "Pit lane" }, AmbienceStore.Load().Current.Layers.Select(l => l.Name));
    }

    // --- Music after the recap while auto-start is off ----------------------

    [Fact]
    public void Music_after_the_recap_does_nothing_while_auto_start_is_off_for_races()
    {
        // Start for: Races unticked greys the whole When it ends card. Music
        // the club started by hand was still faded after every recap.
        var settings = new AppSettings
        {
            AudioStartWithRaces = false,
            AudioStartWithPractice = true,
            MusicAfterSummary = MusicEndBehaviour.FadeOut,
        };

        Assert.False(MainForm.EndsMusicAfterRecap(settings, musicPlaying: true));
    }

    [Fact]
    public void Music_after_the_recap_still_ends_music_when_auto_start_is_on_for_races()
    {
        var settings = new AppSettings
        {
            AudioStartWithRaces = true,
            MusicAfterSummary = MusicEndBehaviour.FadeOut,
        };

        Assert.True(MainForm.EndsMusicAfterRecap(settings, musicPlaying: true));
        Assert.False(MainForm.EndsMusicAfterRecap(settings, musicPlaying: false));

        // Stopped at the flag instead - nothing left for after the recap.
        settings.AudioStopMusicAfterSession = true;
        Assert.False(MainForm.EndsMusicAfterRecap(settings, musicPlaying: true));
    }

    // --- Repeat Race Summary after a race with no recap ---------------------

    private static void SetCommentary(CommentaryService commentary, string property, string? value) =>
        (typeof(CommentaryService).GetProperty(property) ?? throw new InvalidOperationException($"CommentaryService.{property} has gone."))
            .SetValue(commentary, value);

    [Fact]
    public void Clearing_the_race_summary_forgets_the_last_recap_and_keeps_a_talking_point()
    {
        var commentary = new CommentaryService(ProfileStore.Load(), new AppSettings());
        SetCommentary(commentary, nameof(CommentaryService.LastRaceSummary), "Heat 3: Asher held off Sam by a tenth.");
        SetCommentary(commentary, nameof(CommentaryService.LastLine), "Sam is closing on the leader.");
        SetCommentary(commentary, nameof(CommentaryService.LastLineKind), "Talking point");

        commentary.ClearRaceSummary();

        Assert.Null(commentary.LastRaceSummary);
        Assert.Equal("Sam is closing on the leader.", commentary.LastLine);
        Assert.Equal("Talking point", commentary.LastLineKind);
    }

    [Fact]
    public void Clearing_the_race_summary_also_clears_it_as_the_last_line()
    {
        const string recap = "Heat 3: Asher held off Sam by a tenth.";
        var commentary = new CommentaryService(ProfileStore.Load(), new AppSettings());
        SetCommentary(commentary, nameof(CommentaryService.LastRaceSummary), recap);
        SetCommentary(commentary, nameof(CommentaryService.LastLine), recap);
        SetCommentary(commentary, nameof(CommentaryService.LastLineKind), "Summary");

        commentary.ClearRaceSummary();

        Assert.Null(commentary.LastRaceSummary);
        Assert.Null(commentary.LastLine);
        Assert.Null(commentary.LastLineKind);
    }

    // --- Re-record all ------------------------------------------------------

    private const double TakeStability = 0.35;
    private const double TakeStyle = 0.6;

    /// <summary>
    /// An announcer on the club's own ElevenLabs voice, with a take of
    /// <paramref name="text"/> already in the saved-voice library. The key
    /// is one HttpClient refuses to send, so any render fails right here
    /// without a request going anywhere.
    /// </summary>
    private async Task<(AnnouncementService Announcer, byte[] SavedTake)> AnnouncerWithSavedTakeAsync(string text)
    {
        var voices = VoiceSettingsStore.Load();
        voices.Current.Engine = VoiceEngine.ElevenLabs;
        voices.Current.VoiceId = "club-voice";
        voices.Current.ApiKey = "not-a\nkey";

        // No stand-in, so a render that fails fails here rather than coming
        // back in the Kokoro voice: this test is about what Re-record all
        // asks ElevenLabs for. Saved under the old JSON name, which is why
        // the property reads KokoroStandsIn and the file says
        // WindowsVoiceStandsIn - see VoiceProfile.
        voices.Current.KokoroStandsIn = false;

        var announcer = new AnnouncementService(ProfileStore.Load(), voices, SoundEffectsStore.Load());

        // Looks like an MP3 to the library, which keeps nothing else.
        var take = new byte[512];
        take[0] = (byte)'I';
        take[1] = (byte)'D';
        take[2] = (byte)'3';
        take[100] = 42;

        var profile = voices.Current.CloneForDelivery(TakeStability, TakeStyle);
        VoiceLibrary.Shared.Save(profile, text, take);
        _saved.Add((profile, text));

        // Saved in the background.
        for (var i = 0; i < 100 && !VoiceLibrary.Shared.Contains(profile, text); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(VoiceLibrary.Shared.Contains(profile, text));
        return (announcer, take);
    }

    [Fact]
    public async Task Re_record_all_asks_for_a_new_take_instead_of_handing_back_the_saved_one()
    {
        const string text = "Drivers, this is the re-record check, bring your cars to the grid please";
        var (announcer, saved) = await AnnouncerWithSavedTakeAsync(text);

        var take = await announcer.RecordVoiceLineAsync(text, TakeStability, TakeStyle, fresh: true);

        // No new take could be made, so none - the old recording is put back
        // by the Themes page and the run says the line wasn't recorded.
        Assert.Null(take);
        Assert.NotEqual(saved, take);
    }

    [Fact]
    public async Task Recording_a_missing_line_still_uses_the_take_already_saved()
    {
        const string text = "Drivers, this is the record-missing check, bring your cars to the grid please";
        var (announcer, saved) = await AnnouncerWithSavedTakeAsync(text);

        var take = await announcer.RecordVoiceLineAsync(text, TakeStability, TakeStyle);

        Assert.Equal(saved, take);
    }

    // --- The warning buzzer -------------------------------------------------

    [Fact]
    public void A_warning_sounds_its_own_buzzer_on_a_default_install()
    {
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());
        var log = new ConcurrentQueue<string>();
        announcer.StatusMessage += log.Enqueue;

        announcer.NotifyWarning("Warning, Sam, blocking.", blackFlag: false);
        announcer.NotifyWarning("Black flag, Sam.", blackFlag: true);

        // Nothing actually played in a test.
        announcer.Muted = true;

        var lines = log.ToList();
        Assert.Equal(2, lines.Count(l => l.Contains("PlaySound requested: Warning ", StringComparison.Ordinal)));
        Assert.DoesNotContain(lines, l => l.Contains("PlaySound requested: InvalidLap", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Skipped warning buzzer", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Skipped black flag buzzer", StringComparison.Ordinal));
    }

    [Fact]
    public void The_warning_buzzer_is_on_by_default_and_is_not_a_lap_sound()
    {
        var defaults = SoundEffectsProfile.CreateDefault();

        Assert.True(defaults.Effects[nameof(SoundEffectKind.Warning)].Enabled);

        // So it is never held under a yellow, dropped as late for its lap,
        // pushed out by lap chimes or binned by a flag call.
        Assert.DoesNotContain(SoundEffectKind.Warning, SoundEffectsProfile.PerLapKinds);
        Assert.DoesNotContain(SoundEffectKind.Warning, LapTimesOnly.Stingers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_older_file_keeps_the_warning_buzzer_it_heard_through_invalid_lap(bool invalidLapOn)
    {
        var older = SoundEffectsProfile.CreateDefault();
        older.Effects.Remove(nameof(SoundEffectKind.Warning));
        older.Effects[nameof(SoundEffectKind.InvalidLap)].Enabled = invalidLapOn;

        SoundEffectsStore.FillMissing(older);

        Assert.Equal(invalidLapOn, older.Effects[nameof(SoundEffectKind.Warning)].Enabled);
    }

    [Fact]
    public void An_older_file_keeps_the_same_buzzer_for_warnings_including_its_own_file()
    {
        var older = SoundEffectsProfile.CreateForPack("Arcade");
        older.Effects.Remove(nameof(SoundEffectKind.Warning));
        older.Effects[nameof(SoundEffectKind.InvalidLap)] = new SoundEffectConfig
        {
            Enabled = true,
            EnabledInPractice = false,
            BuiltInSound = "PitLane",
            CustomFilePath = @"C:\Club Sounds\buzzer.wav",
        };

        SoundEffectsStore.FillMissing(older);

        var warning = older.Effects[nameof(SoundEffectKind.Warning)];
        Assert.True(warning.Enabled);
        Assert.False(warning.IsEnabledFor(practice: true));
        Assert.Equal("PitLane", warning.BuiltInSound);
        Assert.Equal(@"C:\Club Sounds\buzzer.wav", warning.CustomFilePath);
    }

    [Fact]
    public void A_pack_plays_its_invalid_lap_buzzer_for_a_warning()
    {
        var buzzer = SoundPacks.FileFor("Podium", SoundEffectKind.InvalidLap);

        Assert.NotNull(buzzer);
        Assert.Equal(buzzer, SoundPacks.FileFor("Podium", SoundEffectKind.Warning));
    }

    [Fact]
    public void Built_in_soundscapes_saved_before_the_warning_buzzer_come_back_with_it_as_a_new_install_has_it()
    {
        // themes.json as the last version left it: the built-ins at version
        // 3, with no Warning row, next to a club's own soundscape and with
        // Arcade the one applied.
        var older = Theme.CreateBuiltInDefaults();
        foreach (var builtIn in older)
        {
            builtIn.BuiltInVersion = 3;
            builtIn.SoundEffects.Effects.Remove(nameof(SoundEffectKind.Warning));
        }

        var clubs = new Theme { Name = "Club Night", SoundEffects = SoundEffectsProfile.CreateForPack("Arcade") };
        clubs.SoundEffects.Effects.Remove(nameof(SoundEffectKind.Warning));
        clubs.SoundEffects.Effects[nameof(SoundEffectKind.InvalidLap)].Enabled = false;
        older.Add(clubs);

        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("themes.json"), System.Text.Json.JsonSerializer.Serialize(older));
        File.WriteAllText(_data.PathOf("soundscape-active.txt"), Theme.BuiltInId("Arcade"));

        var themes = ThemeStore.Load();
        var fresh = Theme.CreateBuiltInDefaults();

        // Applying a built-in copies its sounds over the club's, so it has to
        // bring the same Warning however HeatCaller got onto this PC.
        Assert.Contains(fresh, t => t.SoundEffects.Effects[nameof(SoundEffectKind.Warning)].Enabled);
        foreach (var builtIn in fresh)
        {
            var loaded = themes.Get(builtIn.Id);
            Assert.NotNull(loaded);
            Assert.Equal(
                builtIn.SoundEffects.Effects[nameof(SoundEffectKind.Warning)].Enabled,
                loaded!.SoundEffects.Effects[nameof(SoundEffectKind.Warning)].Enabled);
        }

        // The club's own soundscape and the applied marker are untouched,
        // and its Warning still starts the way its Invalid lap was set.
        var kept = Assert.Single(themes.All, t => t.Name == "Club Night");
        Assert.Equal(clubs.Id, kept.Id);
        Assert.False(kept.SoundEffects.Effects[nameof(SoundEffectKind.Warning)].Enabled);
        Assert.Equal(Theme.BuiltInId("Arcade"), themes.ActiveThemeId);
        Assert.Equal(older.Count, themes.All.Count);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Music_after_the_recap_is_live_on_the_Mixer_exactly_when_it_does_something(bool races, bool practice, bool stopAtFlag)
    {
        // Start for: Practice alone used to leave it live, set to Fade out,
        // with nothing ever faded - the recap is only for races.
        var settings = new AppSettings
        {
            AudioStartWithRaces = races,
            AudioStartWithPractice = practice,
            AudioStopMusicAfterSession = stopAtFlag,
            MusicAfterSummary = MusicEndBehaviour.FadeOut,
        };

        Assert.Equal(
            MainForm.EndsMusicAfterRecap(settings, musicPlaying: true),
            MixerControl.MusicAfterRecapApplies(races, stopAtFlag));
    }
}
