namespace HeatCaller.Tests;

/// <summary>
/// ElevenLabs retires its Default voices on 31 Dec 2026 (24 Nov 2026, Asher).
/// A custom soundscape saved with one still carries it, so applying it after
/// that date put a voice ElevenLabs no longer has back on the announcer.
/// From 1 January it brings HeatCaller's announcer voice instead; before
/// then the soundscape keeps its own voice.
/// </summary>
[Collection("ThemeStoreClock")]
public sealed class ElevenLabsRetiredVoicesSoundscapeTests : IDisposable
{
    private const string Charlie = "IKne3meq5aSn9XLyUdCD";
    private const string Finley = "fnYMz3F5gMEDGMWcH1ex";
    private const string Someone = "aMadeUpClubVoice0001";

    private readonly TempAppData _data = new();
    private readonly Func<DateOnly> _today = ThemeStore.Today;

    public void Dispose()
    {
        ThemeStore.Today = _today;
        _data.Dispose();
    }

    private static VoiceSettingsStore ApplySavedWith(string voiceId, string voiceName, DateOnly today)
    {
        ThemeStore.Today = () => today;

        var voice = VoiceSettingsStore.Load();
        var sound = SoundEffectsStore.Load();

        var current = voice.Current;
        current.Engine = VoiceEngine.ElevenLabs;
        current.VoiceId = Someone;
        current.VoiceName = "Our own announcer";
        voice.Save(current);

        var theme = Theme.CreateBuiltInDefaults().First();
        theme.Name = "Our saved soundscape";
        theme.Voice.VoiceId = voiceId;
        theme.Voice.VoiceName = voiceName;

        ThemeStore.Apply(theme, voice, sound);
        return voice;
    }

    [Fact]
    public void After_31_December_a_soundscape_saved_on_a_retired_voice_brings_HeatCallers_announcer_voice()
    {
        var voice = ApplySavedWith(Charlie, "Charlie", new DateOnly(2027, 1, 1));

        Assert.Equal(Finley, voice.Current.VoiceId);
        Assert.Equal(Finley, VoiceSettingsStore.Load().Current.VoiceId);
    }

    [Fact]
    public void Before_then_the_soundscape_keeps_its_own_voice()
    {
        var voice = ApplySavedWith(Charlie, "Charlie", new DateOnly(2026, 12, 31));

        Assert.Equal(Charlie, voice.Current.VoiceId);
    }

    [Fact]
    public void A_soundscape_saved_on_a_voice_that_is_not_retiring_is_left_alone()
    {
        var voice = ApplySavedWith("anotherClubVoice0002", "Their voice", new DateOnly(2027, 3, 1));

        Assert.Equal("anotherClubVoice0002", voice.Current.VoiceId);
    }
}
