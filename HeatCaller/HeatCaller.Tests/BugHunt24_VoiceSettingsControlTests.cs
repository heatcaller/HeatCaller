namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): Test Voice on the Cast tab stays off while its
/// own test is rendering or playing, so a page refresh part-way can no longer
/// switch it back on and let a second press pay for a second take.
/// </summary>
public sealed class BugHunt24_VoiceSettingsControlTests
{
    [Fact]
    public void Test_Voice_is_off_while_a_test_runs_and_on_the_HeatCaller_voice()
    {
        Assert.True(VoiceSettingsControl.TestVoiceEnabled(locked: false, testRunning: false));
        Assert.False(VoiceSettingsControl.TestVoiceEnabled(locked: false, testRunning: true));
        Assert.False(VoiceSettingsControl.TestVoiceEnabled(locked: true, testRunning: false));
        Assert.False(VoiceSettingsControl.TestVoiceEnabled(locked: true, testRunning: true));
    }

    [Fact]
    public void The_HeatCaller_lock_no_longer_switches_Test_Voice_back_on_mid_test()
    {
        var source = Source("VoiceSettingsControl.cs");

        // The old lock list ended with the button and set it to !locked.
        Assert.DoesNotContain("_txtOutputFormat, _btnTestVoice,", source);
        Assert.Contains("_btnTestVoice.Enabled = TestVoiceEnabled(locked, _voiceTestRunning);", source);

        // The click sets the flag and refuses a second test while one runs.
        Assert.Contains("if (_voiceTestRunning) { return; }", source);
        Assert.Contains("_voiceTestRunning = true;", source);
        Assert.Contains("_voiceTestRunning = false;", source);
        Assert.DoesNotContain("_btnTestVoice.Enabled = true;", source);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
