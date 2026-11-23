namespace HeatCaller.Tests;

/// <summary>
/// The HeatCaller voice's choice under Speak with doesn't say "no bill"
/// (22 Nov 2026, the owner): HeatCaller itself is paid for, so "no bill"
/// read as if HeatCaller were free. It says the voice is included instead.
/// </summary>
public sealed class HeatCallerVoiceNoBillTests
{
    [Fact]
    public void The_HeatCaller_voice_choice_says_included_and_not_no_bill()
    {
        var text = VoiceSettingsControl.HeatCallerChoiceText;

        Assert.DoesNotContain("bill", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("free", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("included with your HeatCaller setup", text, StringComparison.Ordinal);
    }
}
