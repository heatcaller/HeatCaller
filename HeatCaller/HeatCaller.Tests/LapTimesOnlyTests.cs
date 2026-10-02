using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Lap times only (LapTimesOnly), as the setup wizards apply it: what it
/// switches on, off and leaves alone; that its undo puts all of it back;
/// that applying it twice still remembers what came before the first; and
/// that leaving it - a soundscape applied again - puts back what it changed.
/// On stores in a data folder of the test's own.
/// </summary>
public sealed class LapTimesOnlyTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void It_reads_lap_times_and_the_safety_calls_and_nothing_else()
    {
        var (settings, voice, sound, themes) = Stores();

        LapTimesOnly.Apply(settings, voice, sound, themes);

        var events = voice.Current.Events;

        foreach (var type in LapTimesOnly.Spoken)
        {
            Assert.True(events[type.ToString()].IsEnabledFor(practice: false), $"{type} is not spoken in a race");
            Assert.True(events[type.ToString()].IsEnabledFor(practice: true), $"{type} is not spoken in practice");
        }

        // The chatter, off in races and practice alike.
        foreach (var type in new[]
                 {
                     AnnouncementEventType.FastLap, AnnouncementEventType.TrackRecord, AnnouncementEventType.LeaderChange,
                     AnnouncementEventType.Streak, AnnouncementEventType.GhostPassed, AnnouncementEventType.AchievementUnlocked,
                     AnnouncementEventType.ProvisionalPole,
                 })
        {
            Assert.False(events[type.ToString()].IsEnabledFor(practice: false), $"{type} is still spoken in a race");
            Assert.False(events[type.ToString()].IsEnabledFor(practice: true), $"{type} is still spoken in practice");
        }

        // The rest of the start and finish, as the club had them (the defaults).
        var defaults = VoiceProfile.CreateDefault().Events;

        foreach (var type in LapTimesOnly.LeftAsSet)
        {
            if (defaults.TryGetValue(type.ToString(), out var standard))
            {
                Assert.Equal(standard.Enabled, events[type.ToString()].Enabled);
            }
        }

        // No sound on a lap; the start and finish sounds as they were.
        foreach (var kind in LapTimesOnly.Stingers)
        {
            Assert.False(sound.Current.Effects[kind.ToString()].Enabled, $"{kind} still plays");
        }

        Assert.True(sound.Current.Effects[nameof(SoundEffectKind.RaceStarted)].Enabled);
        Assert.True(sound.Current.Effects[nameof(SoundEffectKind.RaceCompleted)].Enabled);

        // No music or ambience with a race, nothing spoken between races.
        Assert.False(settings.AudioStartMusic);
        Assert.False(settings.AudioStartAmbience);
        Assert.False(settings.CommentaryRaceIntros);
        Assert.False(settings.CommentaryRaceSummaries);
        Assert.False(settings.SummaryTeaserEnabled);

        // No soundscape in use.
        Assert.True(LapTimesOnly.IsOn(settings));
        Assert.Null(themes.ActiveThemeId);
    }

    [Fact]
    public void Its_undo_puts_everything_back()
    {
        var (settings, voice, sound, themes) = Stores();
        var voiceBefore = JsonSerializer.Serialize(voice.Current.Events);
        var soundBefore = JsonSerializer.Serialize(sound.Current.Effects);
        var appliedBefore = themes.ActiveThemeId;

        var undo = LapTimesOnly.Apply(settings, voice, sound, themes);
        undo();

        Assert.Equal(voiceBefore, JsonSerializer.Serialize(voice.Current.Events));
        Assert.Equal(soundBefore, JsonSerializer.Serialize(sound.Current.Effects));
        Assert.Equal(appliedBefore, themes.ActiveThemeId);

        Assert.True(settings.AudioStartMusic);
        Assert.True(settings.AudioStartAmbience);
        Assert.True(settings.CommentaryRaceIntros);
        Assert.True(settings.CommentaryRaceSummaries);
        Assert.True(settings.SummaryTeaserEnabled);
        Assert.False(LapTimesOnly.IsOn(settings));
    }

    [Fact]
    public void Applied_twice_it_still_remembers_what_came_before_the_first()
    {
        var (settings, voice, sound, themes) = Stores();

        LapTimesOnly.Apply(settings, voice, sound, themes);
        LapTimesOnly.Apply(settings, voice, sound, themes);

        var putBack = settings.LapTimesOnly!;

        Assert.True(putBack.StartMusic);
        Assert.True(putBack.RaceRecaps);
        Assert.True(putBack.Events[nameof(AnnouncementEventType.FastLap)].Enabled);
        Assert.False(putBack.Events[nameof(AnnouncementEventType.LapTime)].Enabled);
    }

    [Fact]
    public void Leaving_it_puts_back_what_it_changed()
    {
        var (settings, voice, sound, themes) = Stores();
        var voiceBefore = JsonSerializer.Serialize(voice.Current.Events);

        LapTimesOnly.Apply(settings, voice, sound, themes);
        var undoLeave = LapTimesOnly.Leave(settings, voice);

        Assert.NotNull(undoLeave);
        Assert.False(LapTimesOnly.IsOn(settings));
        Assert.True(settings.AudioStartMusic);
        Assert.True(settings.AudioStartAmbience);
        Assert.True(settings.CommentaryRaceIntros);
        Assert.True(settings.CommentaryRaceSummaries);
        Assert.True(settings.SummaryTeaserEnabled);

        // The announcements' switches as they were - which is what the
        // HeatCaller voice keeps when a soundscape is applied over it.
        Assert.Equal(voiceBefore, JsonSerializer.Serialize(voice.Current.Events));

        // Undone, the settings are lap times only's again.
        undoLeave!();

        Assert.True(LapTimesOnly.IsOn(settings));
        Assert.False(settings.AudioStartMusic);
        Assert.False(settings.CommentaryRaceSummaries);

        // And with the sound not lap times only, there is nothing to leave.
        settings.LapTimesOnly = null;
        Assert.Null(LapTimesOnly.Leave(settings, voice));
    }

    [Fact]
    public void What_it_puts_back_survives_a_restart()
    {
        var (settings, voice, sound, themes) = Stores();

        LapTimesOnly.Apply(settings, voice, sound, themes);

        var loaded = AppSettings.Load();

        Assert.True(LapTimesOnly.IsOn(loaded));
        Assert.True(loaded.LapTimesOnly!.StartMusic);
        Assert.True(loaded.LapTimesOnly.Events[nameof(AnnouncementEventType.LeaderChange)].Enabled);
        Assert.False(loaded.AudioStartMusic);
    }

    /// <summary>A new club's settings and stores, with the Podium soundscape applied last.</summary>
    private static (AppSettings Settings, VoiceSettingsStore Voice, SoundEffectsStore Sound, ThemeStore Themes) Stores()
    {
        var settings = new AppSettings();
        var voice = VoiceSettingsStore.Load();
        var sound = SoundEffectsStore.Load();
        var themes = ThemeStore.Load();

        var podium = themes.FindByName("Podium");
        Assert.NotNull(podium);
        themes.MarkApplied(podium!);

        return (settings, voice, sound, themes);
    }
}
