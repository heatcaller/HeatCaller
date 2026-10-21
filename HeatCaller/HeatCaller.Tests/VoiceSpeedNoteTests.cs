namespace HeatCaller.Tests;

/// <summary>
/// The note under Speed on the Voice page: ElevenLabs' eleven_v3 takes no
/// speed, so the box does nothing there and the page says so.
/// </summary>
public sealed class VoiceSpeedNoteTests
{
    [Theory]
    [InlineData(true, "eleven_v3", true)]
    [InlineData(true, "ELEVEN_V3", true)]
    [InlineData(true, "eleven_multilingual_v2", false)]
    [InlineData(true, "eleven_turbo_v2_5", false)]
    [InlineData(true, "eleven_flash_v2_5", false)]
    [InlineData(true, "", false)]
    [InlineData(true, null, false)]
    [InlineData(false, "eleven_v3", false)]
    public void Speed_is_called_out_only_for_elevenlabs_on_v3(bool elevenLabs, string? model, bool ignored)
    {
        Assert.Equal(ignored, VoiceSettingsControl.SpeedIgnored(elevenLabs, model));
    }
}
