namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: undoing a soundscape apply put back the club's own typed
/// announcer lines even when the HeatCaller voice had been picked since -
/// so those lines were spoken on HeatCaller's account, which Apply and the
/// Voice page's own undo both refuse to allow.
/// </summary>
public sealed class BugHunt24_ThemeStoreTests : IDisposable
{
    private const string TypedLine = "Our own club line, typed by hand.";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly string Key = nameof(AnnouncementEventType.RaceStarted);

    private static (VoiceSettingsStore Voice, SoundEffectsStore Sound, Action Undo) ApplyBuiltInOverTypedLines()
    {
        var voice = VoiceSettingsStore.Load();
        var sound = SoundEffectsStore.Load();

        var before = voice.Current;
        before.Engine = VoiceEngine.ElevenLabs;
        if (!before.Events.TryGetValue(Key, out var config) || config is null)
        {
            config = new AnnouncementEventConfig();
            before.Events[Key] = config;
        }

        config.Templates = new List<string> { TypedLine };
        voice.Save(before);

        var builtIn = Theme.CreateBuiltInDefaults().First();
        var undo = ThemeStore.CaptureForUndo(builtIn, voice, sound);
        ThemeStore.Apply(builtIn, voice, sound);

        return (voice, sound, undo);
    }

    [Fact]
    public void Undoing_a_soundscape_on_the_HeatCaller_voice_keeps_the_lines_it_speaks_now()
    {
        var (voice, _, undo) = ApplyBuiltInOverTypedLines();

        // The HeatCaller voice picked after the apply.
        var switched = voice.Current;
        switched.Engine = VoiceEngine.HeatCaller;
        voice.Save(switched);
        var linesBeforeUndo = voice.Current.Events[Key].EffectiveTemplates.ToList();

        undo();

        Assert.Equal(VoiceEngine.HeatCaller, voice.Current.EffectiveEngine);
        Assert.DoesNotContain(TypedLine, voice.Current.Events[Key].EffectiveTemplates);
        Assert.Equal(linesBeforeUndo, voice.Current.Events[Key].EffectiveTemplates.ToList());
        Assert.DoesNotContain(TypedLine, VoiceSettingsStore.Load().Current.Events[Key].EffectiveTemplates);
    }

    [Fact]
    public void Undoing_a_soundscape_on_the_clubs_own_voice_still_puts_its_typed_lines_back()
    {
        var (voice, _, undo) = ApplyBuiltInOverTypedLines();

        undo();

        Assert.Equal(VoiceEngine.ElevenLabs, voice.Current.EffectiveEngine);
        Assert.Contains(TypedLine, voice.Current.Events[Key].EffectiveTemplates);
    }
}
