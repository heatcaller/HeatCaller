using System.Diagnostics;
using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// The music and crowd under the highlight videos (26 Oct 2026, asked for by
/// Asher: "video recaps should include the music that is playing as well.")
/// - RecapBed - and the Recording tab's move to the Club Website page the
/// same day ("the recording tab is in a bad location move it to the club
/// website for now.").
///
/// No sound device: the bed's arithmetic (the stamps, the mono and rate
/// conversion, the soft knee, the mix) is tested on its own, the cut of a
/// video's wav runs for real on two small files, and the wiring - which tap
/// feeds it, that the race asks for it and nothing else does, that the
/// mp3s stay the call alone - is checked in the source.
/// </summary>
public sealed class RecapBedTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- Converting a read ---------------------------------------------------

    [Fact]
    public void A_stereo_read_at_the_recordings_rate_becomes_the_average_of_its_two_channels()
    {
        var mono = RecapBed.ToMonoAtRate(new[] { 0.5f, 0.1f, -0.2f, -0.4f }, 4, channels: 2, fromRate: 44100, toRate: 44100);

        Assert.Equal(2, mono.Length);
        Assert.Equal(0.3f, mono[0], 5);
        Assert.Equal(-0.3f, mono[1], 5);
    }

    [Fact]
    public void A_48k_music_file_is_stretched_to_the_recordings_rate_without_changing_its_level()
    {
        // A tenth of a second of a steady level at 48kHz: 4800 frames in,
        // 4410 out, every one the same level.
        var read = Enumerable.Repeat(0.25f, 4800 * 2).ToArray();

        var mono = RecapBed.ToMonoAtRate(read, read.Length, channels: 2, fromRate: 48000, toRate: 44100);

        Assert.Equal(4410, mono.Length);
        Assert.All(mono, s => Assert.Equal(0.25f, s, 5));
    }

    [Fact]
    public void A_ramp_is_interpolated_between_its_samples_not_stepped()
    {
        // Mono 0, 1, 2, 3 at 2Hz, asked for at 4Hz: halfway points appear.
        var mono = RecapBed.ToMonoAtRate(new[] { 0f, 1f, 2f, 3f }, 4, channels: 1, fromRate: 2, toRate: 4);

        Assert.Equal(8, mono.Length);
        Assert.Equal(0.5f, mono[1], 5);
        Assert.Equal(1.5f, mono[3], 5);
    }

    [Fact]
    public void Not_a_number_is_silence_and_an_empty_read_is_nothing()
    {
        Assert.Equal(0f, RecapBed.ToMonoAtRate(new[] { float.NaN }, 1, 1, 44100, 44100)[0]);
        Assert.Empty(RecapBed.ToMonoAtRate(Array.Empty<float>(), 0, 2, 44100, 44100));
        Assert.Empty(RecapBed.ToMonoAtRate(new[] { 0.5f, 0.5f }, 2, 2, fromRate: 0, toRate: 44100));
    }

    // --- The level -------------------------------------------------------------

    [Fact]
    public void The_soft_knee_leaves_ordinary_levels_alone_and_never_passes_full_scale()
    {
        Assert.Equal(0.5f, RecapBed.SoftClip(0.5f));
        Assert.Equal(-0.8f, RecapBed.SoftClip(-0.8f));

        var last = 0.8f;

        foreach (var level in new[] { 0.9f, 1.0f, 1.5f, 2.0f, 10f })
        {
            var eased = RecapBed.SoftClip(level);

            Assert.True(eased > last, $"{level} eased to {eased}, not above {last}.");
            Assert.True(eased <= 1f, $"{level} eased to {eased}, past full scale.");
            Assert.Equal(-eased, RecapBed.SoftClip(-level), 5);
            last = eased;
        }

        Assert.Equal(0f, RecapBed.SoftClip(float.NaN));
    }

    [Fact]
    public void Two_loud_beds_added_together_stay_inside_16_bits()
    {
        Assert.InRange(RecapBed.ToPcm16(2f), (short)30_000, short.MaxValue);
        Assert.InRange(RecapBed.ToPcm16(-2f), short.MinValue, (short)-30_000);
        Assert.Equal((short)0, RecapBed.ToPcm16(0f));
    }

    // --- The mix under the call -----------------------------------------------

    [Fact]
    public void Where_the_bed_is_silent_the_call_is_left_exactly_as_recorded()
    {
        var call = Pcm(1000, -32768, 32767, 5);
        var original = (byte[])call.Clone();

        RecapBed.MixPcm16(call, call.Length, Pcm(0, 0, 0, 0), 8);

        Assert.Equal(original, call);
    }

    [Fact]
    public void The_bed_is_added_to_the_call_and_past_its_end_the_call_plays_alone()
    {
        var call = Pcm(1000, 2000, 3000);
        var bed = Pcm(500, 500);

        RecapBed.MixPcm16(call, call.Length, bed, bed.Length);

        Assert.Equal(new short[] { 1500, 2500, 3000 }, Samples(call));
    }

    [Fact]
    public void A_loud_call_over_a_loud_bed_is_eased_not_wrapped_round()
    {
        var call = Pcm(30_000, -30_000);
        var bed = Pcm(30_000, -30_000);

        RecapBed.MixPcm16(call, call.Length, bed, bed.Length);

        var mixed = Samples(call);
        Assert.True(mixed[0] > 30_000, $"Got {mixed[0]} - a sum past 16 bits wrapped round to negative.");
        Assert.True(mixed[1] < -30_000, $"Got {mixed[1]}.");
    }

    // --- When a read is heard -----------------------------------------------------

    [Fact]
    public void A_read_lands_in_the_file_at_the_samples_since_the_recording_started()
    {
        var second = Stopwatch.Frequency;

        Assert.Equal(44_100L, RecapBed.PositionFor(5 * second, 4 * second, 44_100, second));
        Assert.Equal(22_050L, RecapBed.PositionFor((4 * second) + (second / 2), 4 * second, 44_100, second));
        Assert.True(RecapBed.PositionFor(3 * second, 4 * second, 44_100, second) < 0);
    }

    [Fact]
    public void Music_goes_under_the_call_sample_the_room_heard_at_the_same_moment()
    {
        // The bus was already playing when the race began, so the race
        // file's first sample was heard a quarter of a second after the
        // start: the file is behind the clock by 11025 samples.
        var second = Stopwatch.Frequency;
        long start = 100 * second;

        var offset = RecapBed.OffsetFor(fileIndex: 0, heardTicks: start + (second / 4), start, 44_100, second);
        Assert.Equal(-11_025L, offset);

        // Music heard one second in lands on the call's sample heard then -
        // three quarters of a second into the file, not one second.
        Assert.Equal(33_075L, offset + RecapBed.PositionFor(start + second, start, 44_100, second));

        // And the recorder says so on every read of the call, before it
        // counts the read in.
        var mixed = Between(Source("RaceAudioRecorder.cs"), "public void Mixed(", "private void WriteLoop()");
        var told = mixed.IndexOf("bed.CallHeard(Interlocked.Read(ref _samplesAccepted), _callHeardAt.Next(frames, SampleRate));", StringComparison.Ordinal);
        Assert.True(told >= 0, "The recorder never tells the bed which of its samples is heard when.");
        Assert.True(told < mixed.IndexOf("Interlocked.Add(ref _samplesAccepted, frames);", StringComparison.Ordinal));
    }

    [Fact]
    public void An_outputs_first_fill_plays_from_now_one_buffer_after_another_and_a_refill_a_queue_from_now()
    {
        // The beds' output: 400ms over four buffers, so a refill is heard
        // 300ms after it is read.
        long now = 1_000_000;
        var stamper = new RecapBed.Stamper(latencyMs: 400, buffers: 4, clock: () => now);
        var tenth = Stopwatch.Frequency / 10;

        // The first fill: four reads at once, heard one after another from now.
        var first = stamper.Next(4410, 44100);
        var second = stamper.Next(4410, 44100);
        var third = stamper.Next(4410, 44100);

        Assert.Equal(now, first);
        Assert.Equal(now + tenth, second);
        Assert.Equal(now + (2 * tenth), third);

        _ = stamper.Next(4410, 44100);

        // A refill, a buffer later, joins on to the end of the queue - which
        // is where the queue says it should be, so the stamps stay joined up.
        now += tenth;
        Assert.Equal(now + (3 * tenth), stamper.Next(4410, 44100));

        // After a long gap (the stamps have strayed), back to a queue from now.
        now += 10 * Stopwatch.Frequency;
        Assert.Equal(now + (3 * tenth), stamper.Next(4410, 44100));
    }

    // --- The bed file, end to end ---------------------------------------------------

    [Fact]
    public void Music_offered_while_a_bed_runs_is_written_to_it_and_nothing_after_it_stops()
    {
        Directory.CreateDirectory(_data.Folder);
        var bed = RecapBed.StartFor(_data.Folder, "Heat 1", DateTime.UtcNow, maxSamples: 44_100L * 60, _ => { });
        Assert.NotNull(bed);

        var stamper = new RecapBed.Stamper(latencyMs: 400, buffers: 4);
        var read = Enumerable.Repeat(0.5f, 4410 * 2).ToArray();

        for (var i = 0; i < 5; i++)
        {
            RecapBed.Offer(AudioLevels.Music, stamper, read, 0, read.Length, 44100, 2);
        }

        bed!.Stop();

        // Offered after the stop: no bed is being fed, so nothing to throw at.
        RecapBed.Offer(AudioLevels.Music, stamper, read, 0, read.Length, 44100, 2);

        using var reader = new WaveFileReader(bed.Path);
        Assert.Equal(44100, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);

        var bytes = new byte[reader.Length];
        var got = reader.Read(bytes, 0, bytes.Length);
        var samples = Samples(bytes[..got]);

        // Half a second of music at the level it was heard, give or take the
        // conversion.
        Assert.True(samples.Count(s => Math.Abs(s - 16_384) < 40) >= 20_000, "The music did not land in the bed at its level.");
    }

    [Fact]
    public void The_level_meters_own_strips_are_never_kept_in_the_bed()
    {
        Directory.CreateDirectory(_data.Folder);
        var bed = RecapBed.StartFor(_data.Folder, "Heat 2", DateTime.UtcNow, maxSamples: 44_100L * 60, _ => { });
        var stamper = new RecapBed.Stamper(latencyMs: 300, buffers: 3);
        var read = Enumerable.Repeat(0.5f, 4410 * 2).ToArray();

        // The voice is the race recording's, from the bus - not the bed's.
        RecapBed.Offer(AudioLevels.Voice, stamper, read, 0, read.Length, 44100, 2);
        RecapBed.Offer(new AudioLevels.Meter("T"), stamper, read, 0, read.Length, 44100, 2);
        bed!.Stop();

        using var reader = new WaveFileReader(bed.Path);
        var bytes = new byte[reader.Length];
        var got = reader.Read(bytes, 0, bytes.Length);

        Assert.All(Samples(bytes[..got]), s => Assert.Equal((short)0, s));
    }

    [Fact]
    public void A_videos_wav_is_the_call_with_the_same_moments_of_the_bed_under_it()
    {
        // A two-second call at 1000 and a bed at 200 for its first second
        // only - a bed that stopped early.
        var call = Path.Combine(_data.Folder, "call.wav");
        var bed = Path.Combine(_data.Folder, "call" + RecapBed.Extension);
        var cut = Path.Combine(_data.Folder, "cut.wav");

        Directory.CreateDirectory(_data.Folder);
        WriteWav(call, Enumerable.Repeat((short)1000, 88_200).ToArray());
        WriteWav(bed, Enumerable.Repeat((short)200, 44_100).ToArray());

        // Half a second either side of the bed's end.
        RaceAudioRecorder.CutWavWithBed(call, bed, cut, new[] { new RaceHighlights.Window(500, 1500) });

        using var reader = new WaveFileReader(cut);
        var bytes = new byte[reader.Length];
        var got = reader.Read(bytes, 0, bytes.Length);
        var samples = Samples(bytes[..got]);

        Assert.Equal(44_100, samples.Length);
        Assert.All(samples[..22_050], s => Assert.Equal((short)1200, s));
        Assert.All(samples[22_050..], s => Assert.Equal((short)1000, s));
    }

    [Fact]
    public void A_bed_in_another_format_is_refused_so_the_video_gets_the_call_alone()
    {
        var call = Path.Combine(_data.Folder, "call2.wav");
        var bed = Path.Combine(_data.Folder, "call2" + RecapBed.Extension);

        Directory.CreateDirectory(_data.Folder);
        WriteWav(call, new short[4410]);

        using (var writer = new WaveFileWriter(bed, new WaveFormat(48000, 16, 1)))
        {
            writer.Write(new byte[960], 0, 960);
        }

        Assert.Throws<InvalidDataException>(() =>
            RaceAudioRecorder.CutWavWithBed(call, bed, Path.Combine(_data.Folder, "cut2.wav"), new[] { new RaceHighlights.Window(0, 100) }));
    }

    // --- The wiring ------------------------------------------------------------------

    [Fact]
    public void The_bed_is_fed_from_the_music_and_crowd_taps_and_never_takes_the_buses_one_listener()
    {
        var levels = Source("AudioLevels.cs");
        var tapRead = levels[levels.IndexOf("internal sealed class Tap", StringComparison.Ordinal)..];

        Assert.Contains("RecapBed.Offer(_meter, _heardAt, buffer, offset, read, _sampleRate, _channels);", tapRead);
        Assert.Contains("RecapBed.Cut(this, at);", levels);

        var bed = Source("RecapBed.cs");
        Assert.DoesNotContain("AudioBus.Listen", bed);
        Assert.DoesNotContain("lock (", bed);
    }

    [Fact]
    public void Only_a_race_asks_for_a_bed_and_only_when_its_videos_are_made()
    {
        Assert.Contains("message => StatusMessage?.Invoke(message),\n                    withVideoBed: store.MusicUnderVideos,\n                    isRace: true);", Source("RaceControlControl.AfterRace.cs"));
        Assert.DoesNotContain("withVideoBed", Source("BookendRecording.cs"));

        var recorder = Source("RaceAudioRecorder.cs");
        Assert.Contains("if (withVideoBed && store.MusicUnderVideos && store.DriverHighlights && store.HighlightVideos != HighlightVideo.Who.Off)", recorder);

        // Switched off between the green and the videos: they are the call alone.
        var publish = Between(recorder, "public static void Publish(", "private static void WriteClips(");
        Assert.Contains("if (!store.MusicUnderVideos)", publish);
        Assert.Contains("recording = recording with { BedPath = null };", publish);
    }

    // --- The switch (26 Oct 2026, Asher: "add option to switch off. on by default.") ---

    [Fact]
    public void The_music_under_the_videos_is_on_by_default_and_for_a_file_saved_before_it_existed()
    {
        RaceAudioStore.OffForTests = false;

        try
        {
            Assert.True(RaceAudioStore.Load().MusicUnderVideos);

            File.WriteAllText(_data.PathOf("race-audio.json"), "{\"Enabled\":true,\"HighlightVideos\":3}");
            Assert.True(RaceAudioStore.Load().MusicUnderVideos);
        }
        finally
        {
            RaceAudioStore.OffForTests = true;
        }
    }

    [Fact]
    public void Switching_the_music_off_is_saved()
    {
        RaceAudioStore.OffForTests = false;

        try
        {
            var store = RaceAudioStore.Load();
            store.MusicUnderVideos = false;

            Assert.False(RaceAudioStore.Load().MusicUnderVideos);
        }
        finally
        {
            RaceAudioStore.OffForTests = true;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_race_keeps_the_music_for_its_videos_only_while_the_switch_is_on(bool musicUnderVideos)
    {
        RaceAudioStore.OffForTests = false;

        try
        {
            File.WriteAllText(_data.PathOf("race-audio.json"), "{\"Enabled\":true,\"MinFreeMb\":100}");

            var store = RaceAudioStore.Load();
            store.FolderSetting = Path.Combine(_data.Folder, "audio");
            store.MusicUnderVideos = musicUnderVideos;

            // Asked for, as Race Control asks: the switch decides.
            var recorder = RaceAudioRecorder.StartFor(store, "Heat 1", DateTime.UtcNow, _ => { }, withVideoBed: true);
            Assert.NotNull(recorder);

            var recording = recorder!.Stop();

            if (musicUnderVideos)
            {
                Assert.NotNull(recording.BedPath);
                Assert.EndsWith(RecapBed.Extension, recording.BedPath);
            }
            else
            {
                Assert.Null(recording.BedPath);
                Assert.Empty(Directory.EnumerateFiles(recording.Folder, "*" + RecapBed.Extension));
            }
        }
        finally
        {
            RaceAudioStore.OffForTests = true;
        }
    }

    [Fact]
    public void The_switch_is_a_tick_box_with_the_other_video_settings_and_says_why()
    {
        var page = Source("RecordingControl.cs");

        Assert.Contains("Check(_chkMusicUnderVideos, \"Put the music and crowd under the highlight videos\");", page);
        Assert.Contains("_chkMusicUnderVideos.Checked = _audio.MusicUnderVideos;", page);
        Assert.Contains("_chkMusicUnderVideos.CheckedChanged += (_, _) => _audio.MusicUnderVideos = _chkMusicUnderVideos.Checked;", page);
        Assert.Contains("_tips.SetToolTip(_chkMusicUnderVideos,", page);
        Assert.Contains("licensed", page);
        Assert.Contains("audio.Add(_chkMusicUnderVideos,", page);
        Assert.Contains("_chkMusicUnderVideos.Enabled = clips;", page);
    }

    [Fact]
    public void The_videos_are_cut_with_the_bed_and_the_mp3s_are_still_the_call_alone()
    {
        var recorder = Source("RaceAudioRecorder.cs");

        var video = Between(recorder, "private static string? MakeVideoFile(", "internal static void CutWavWithBed(");
        Assert.Contains("CutWavWithBed(recording.WavPath, bed, wav, windows);", video);

        // The clips are cut from the call alone, as before.
        var clips = Between(recorder, "private static void WriteClips(", "private static void ShareClips(");
        Assert.Contains("CutWav(recording.WavPath, temp, clip.Windows);", clips);
        Assert.DoesNotContain("CutWavWithBed", clips);
    }

    [Fact]
    public void A_bed_never_outlives_its_race()
    {
        var recorder = Source("RaceAudioRecorder.cs");

        // Deleted when the race's videos are done, on every path out of Publish.
        var publishEnd = Between(recorder, "        finally\n        {\n            if (publishing is not null)", "/// <summary>One mp3 per driver");
        Assert.Contains("TryDelete(bedHeld, log);", publishEnd);

        // And swept up after a crash or a close.
        Assert.Contains("Directory.EnumerateFiles(root, \"*\" + RecapBed.Extension, SearchOption.AllDirectories)", recorder);
    }

    // --- The Recording tab on the Club Website page ----------------------------------------

    [Fact]
    public void The_recording_tab_is_on_the_club_website_page_and_no_longer_on_sounds()
    {
        var pages = Source("MainForm.Pages.cs");

        Assert.DoesNotContain("_tabSoundSubNav.TabPages.Add(subPageRecording);", pages);
        Assert.Contains("_recordingSubPage = subPageRecording;", pages);

        var club = pages[pages.IndexOf("private void BuildClubWebsitePage()", StringComparison.Ordinal)..];
        Assert.Contains("_tabClubWebsiteSubNav.TabPages.Add(subPageWebsite);", club);
        Assert.Contains("_tabClubWebsiteSubNav.TabPages.Add(_recordingSubPage);", club);
        Assert.Contains("pageOnline.Controls.Add(_tabClubWebsiteSubNav);", club);

        // Sounds is built first, so the tab exists when the page takes it.
        Assert.True(
            pages.IndexOf("BuildSoundsPage(announcer);", StringComparison.Ordinal)
                < pages.IndexOf("BuildClubWebsitePage();", StringComparison.Ordinal),
            "Club Website is built before the Recording tab it hosts.");
    }

    [Fact]
    public void Searching_for_the_recording_finds_it_on_the_club_website_page()
    {
        var nav = Source("MainForm.Navigation.cs");
        var tabs = nav[nav.IndexOf("NavSearchTabAliases = new", StringComparison.Ordinal)..];

        var sounds = Between(tabs, "[\"Sounds\"] = new[]", "[\"Club Website\"] = new[]");
        Assert.DoesNotContain("(\"Recording\"", sounds);

        var club = tabs[tabs.IndexOf("[\"Club Website\"] = new[]", StringComparison.Ordinal)..];
        Assert.Contains("(\"Website\", new[]", club);
        Assert.Contains("(\"Recording\", new[]", club);
        Assert.True(club.IndexOf("(\"Website\"", StringComparison.Ordinal) < club.IndexOf("(\"Recording\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_button_that_sends_somebody_to_set_up_the_club_website_lands_on_its_website_tab()
    {
        var pages = Source("MainForm.Pages.cs");

        Assert.Contains("GoToClubWebsiteSettings,", pages);
        Assert.DoesNotContain("() => GoToPage(\"Club Website\"),", pages);
        Assert.Contains("tabs.SelectedIndex = 0;", Between(pages, "private void GoToClubWebsiteSettings()", "private void BuildClubWebsitePage()"));
    }

    // --- Helpers ---------------------------------------------------------------------

    private static byte[] Pcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];

        for (var i = 0; i < samples.Length; i++)
        {
            bytes[i * 2] = (byte)(samples[i] & 0xFF);
            bytes[(i * 2) + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }

        return bytes;
    }

    private static short[] Samples(byte[] bytes)
    {
        var samples = new short[bytes.Length / 2];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(bytes[i * 2] | (bytes[(i * 2) + 1] << 8));
        }

        return samples;
    }

    private static void WriteWav(string path, short[] samples)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 1));
        var bytes = Pcm(samples);
        writer.Write(bytes, 0, bytes.Length);
    }

    private static string Between(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"\"{from}\" is not there.");

        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"\"{to}\" is not after \"{from}\".");

        return text[start..end];
    }

    /// <summary>The folder the repo is checked out in, found by the file that is only ever at its root (as AudioLevelsTests finds it).</summary>
    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName)).Replace("\r\n", "\n");
}
