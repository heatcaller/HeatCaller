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
    [InlineData(true, "eleven_v4", true)]
    [InlineData(true, "eleven_v4_turbo", true)]
    [InlineData(true, "eleven_multilingual_v2", false)]
    [InlineData(true, "eleven_turbo_v2_5", false)]
    [InlineData(true, "eleven_flash_v2_5", false)]
    [InlineData(true, "", false)]
    [InlineData(true, null, false)]
    [InlineData(false, "eleven_v3", false)]
    [InlineData(false, "eleven_v4", false)]
    public void Speed_is_called_out_only_for_elevenlabs_on_v3_and_v4(bool elevenLabs, string? model, bool ignored)
    {
        Assert.Equal(ignored, VoiceSettingsControl.SpeedIgnored(elevenLabs, model));
    }
}

/// <summary>Audio tags are performed on the v3 and v4 families and stripped everywhere else.</summary>
public sealed class AudioTagModelTests
{
    [Theory]
    [InlineData("eleven_v3", true)]
    [InlineData("eleven_v4", true)]
    [InlineData("eleven_v4_turbo", true)]
    [InlineData("eleven_flash_v2_5", false)]
    [InlineData("eleven_multilingual_v2", false)]
    [InlineData("eleven_turbo_v2_5", false)]
    public void Tags_stay_in_only_on_v3_and_v4(string model, bool kept)
    {
        var profile = new VoiceProfile { Engine = VoiceEngine.ElevenLabs, ModelId = model, VoiceId = "v" };
        Assert.Equal(kept, profile.SupportsAudioTags);
        Assert.Equal(kept ? "[excited] Green flag!" : "Green flag!", profile.SanitiseForModel("[excited] Green flag!"));
    }
}
