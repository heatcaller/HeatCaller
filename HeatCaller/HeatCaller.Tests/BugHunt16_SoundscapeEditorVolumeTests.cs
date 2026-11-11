namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16: the soundscape editor's per-sound play buttons played at a
/// fixed 90%, while its Preview and every other test followed the Mixer's
/// Sound effects fader - so with the fader turned down for a loud PA, a sound
/// auditioned here came out several times louder than on race night.
/// </summary>
public sealed class BugHunt16_SoundscapeEditorVolumeTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_sounds_play_button_follows_the_mixers_effects_level()
    {
        var levels = StaThread.Run(() =>
        {
            var fader = 0.2f;

            var working = Theme.CreateBuiltInDefaults().First();
            working.Id = Guid.NewGuid().ToString("N");
            working.IsBuiltIn = false;

            using var editor = new SoundscapeEditorForm(
                working,
                isNew: false,
                ThemeStore.Load(),
                VoiceProfile.CreateDefault(),
                (_, _) => Task.CompletedTask,
                () => { },
                () => fader);

            var first = editor.SoundTestVolume;

            // Read as each sound plays, not once as the editor opens.
            fader = 0.55f;

            return (first, second: editor.SoundTestVolume);
        }, TimeSpan.FromSeconds(30), () => "opening the soundscape editor");

        Assert.Equal(0.2f, levels.first);
        Assert.Equal(0.55f, levels.second);
    }
}
