namespace HeatCaller.Tests;

/// <summary>
/// The voice speed plan's first cuts (10 Nov 2026, phase 1). The baseline
/// race on eleven_v4_turbo took 2.63 s from a lap to its call, and held the
/// speaker 2.47 s longer than each call's words: a rendered call waited
/// ~0.8 s for a lap chime to finish, a flat 350 ms followed every line, and
/// the batching windows took 0.67 s before a call was queued.
/// </summary>
public sealed class VoiceSpeedPhase1Tests
{
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

    [Fact]
    public void A_standard_tail_already_covers_the_device_so_nothing_is_waited_after_it()
    {
        // The tail is mixed after the last word, so a tail at least as long
        // as the device's buffer means the word has been heard already.
        Assert.Equal(0, AnnouncementService.GraceAfterLineMs(AnnouncementService.DefaultTrailingSilenceSeconds));
        Assert.Equal(0, AnnouncementService.GraceAfterLineMs(AnnouncementService.LongLineTrailingSilenceSeconds));
    }

    [Fact]
    public void A_tail_shorter_than_the_device_buffer_waits_only_for_the_rest_of_it()
    {
        Assert.Equal(AudioBus.OutputLatencyMs - 100, AnnouncementService.GraceAfterLineMs(0.1));
        Assert.Equal(AudioBus.OutputLatencyMs, AnnouncementService.GraceAfterLineMs(0));
    }

    [Fact]
    public void The_flat_beat_after_every_line_is_gone()
    {
        Assert.DoesNotContain("await Task.Delay(350);", Source("AnnouncementService.Playback.cs"));
    }

    [Fact]
    public void A_lap_chime_does_not_take_the_voice_lock_but_the_flag_sounds_still_do()
    {
        var sounds = Source("AnnouncementService.Sounds.cs");

        Assert.Contains("var underTheVoice = IsPerLapSound(kind);", sounds);
        Assert.Contains("if (!underTheVoice)\n                {\n                    await _audioPlaybackLock.WaitAsync();", sounds.Replace("\r\n", "\n"));
        Assert.Contains("if (!underTheVoice)\n                    {\n                        _audioPlaybackLock.Release();", sounds.Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_chime_under_the_voice_is_quieter_but_still_heard()
    {
        Assert.InRange(AnnouncementService.ChimeUnderVoiceGain, 0.3f, 0.8f);
    }

    [Fact]
    public void The_flag_sounds_are_not_lap_chimes()
    {
        // The voice waits for these (WaitForLeadInSoundAsync), so they must
        // keep the speaker to themselves.
        foreach (var kind in new[] { SoundEffectKind.RaceStarted, SoundEffectKind.RaceCompleted, SoundEffectKind.RaceTimeOver, SoundEffectKind.YellowFlag, SoundEffectKind.RedFlag })
        {
            Assert.DoesNotContain(kind, SoundEffectsProfile.PerLapKinds);
        }
    }

    [Fact]
    public void A_standard_line_with_another_waiting_gets_the_back_to_back_tail()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");
        var playback = Source("AnnouncementService.Playback.cs");

        Assert.Contains("TailSecondsNow(tail)", queue);
        Assert.Contains("return _voiceQueue.Count > 0 ? BackToBackTrailingSilenceSeconds : DefaultTrailingSilenceSeconds;", playback);
    }

    [Fact]
    public void The_batching_windows_are_shorter()
    {
        Assert.Contains("PerLapBatchWindow = TimeSpan.FromMilliseconds(120)", Source("AnnouncementService.LapCalls.cs"));
        Assert.Equal(200, AnnouncerDensity.FieldWindow.TotalMilliseconds);
    }
}
