namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: unmuting brought the crowd back at the mood it had when the
/// mute stopped it, not the race's - idle under a green flag when the race
/// started while muted. See MainForm._ambienceMoodMissed.
/// </summary>
public sealed class BugHunt17_MainForm_AudioTests
{
    [Theory]
    [InlineData(RaceState.Idle, FlagState.None, AmbienceService.Intensity.Idle)]
    [InlineData(RaceState.Staging, FlagState.None, AmbienceService.Intensity.Anticipation)]
    [InlineData(RaceState.Countdown, FlagState.None, AmbienceService.Intensity.Anticipation)]
    [InlineData(RaceState.Racing, FlagState.Green, AmbienceService.Intensity.Racing)]
    [InlineData(RaceState.Racing, FlagState.None, AmbienceService.Intensity.Racing)]
    [InlineData(RaceState.Racing, FlagState.Yellow, AmbienceService.Intensity.Anticipation)]
    [InlineData(RaceState.Racing, FlagState.Red, AmbienceService.Intensity.Idle)]
    [InlineData(RaceState.Finished, FlagState.Checkered, AmbienceService.Intensity.Idle)]
    public void The_crowd_s_mood_for_the_race_as_it_stands(RaceState state, FlagState flag, AmbienceService.Intensity mood)
    {
        Assert.Equal(mood, MainForm.AmbienceMoodFor(state, flag));
    }

    [Fact]
    public void The_unmute_sets_the_mood_the_race_moved_on_to_while_the_crowd_was_off()
    {
        var audio = Source("MainForm.Audio.cs");

        // Noted by both handlers when the mute has the crowd off, and by a
        // session that starts muted.
        Assert.Contains("_ambienceMoodMissed |= _ambienceStoppedByMute;", audio);
        Assert.Contains("_ambienceMoodMissed |= _ambienceStoppedByMute && !_ambience.IsRunning;", audio);

        var unmute = audio[audio.IndexOf("private void SetAllSoundMuted(bool muted)", StringComparison.Ordinal)..];

        Assert.Contains("_ambience.SetIntensity(AmbienceMoodFor(_race.State, _race.Flag));", unmute);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
