namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16: a soundscape's Undo put back the race-call model from
/// before the apply, although Apply keeps it as this PC's own setting -
/// so a faster model picked on the Voice page since was quietly lost.
/// </summary>
public sealed class BugHunt16_SoundscapeUndoTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Undo_keeps_the_race_call_model_picked_since()
    {
        var voice = VoiceSettingsStore.Load();
        var sound = SoundEffectsStore.Load();

        var before = voice.Current;
        before.RaceCallModelId = "eleven_multilingual_v2";
        voice.Save(before);

        var undo = ThemeStore.CaptureForUndo(null, voice, sound);

        var later = voice.Current;
        later.RaceCallModelId = "eleven_flash_v2_5";
        voice.Save(later);

        undo();

        Assert.Equal("eleven_flash_v2_5", voice.Current.RaceCallModelId);
        Assert.Equal("eleven_flash_v2_5", VoiceSettingsStore.Load().Current.RaceCallModelId);
    }
}
