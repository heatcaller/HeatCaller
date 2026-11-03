using System.Diagnostics;
using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// The level meters' source (25 Oct 2026): each output's sound cut into 10ms
/// blocks, the loudest sample of each kept and stamped with when it will be
/// heard, and the Broadcast window's channel strips (ChannelStrips) moving
/// to them.
/// </summary>
public sealed class AudioLevelsTests
{
    private static readonly long Block = Stopwatch.Frequency * AudioLevels.BlockMs / 1000;

    private static readonly long Second = Stopwatch.Frequency;

    /// <summary>100ms of stereo at 44.1kHz: left at 0.5 (both signs), right at 0.25 - mean squares 0.25 and 0.0625.</summary>
    private static float[] TenthOfASecond(float left = 0.5f, float right = 0.25f)
    {
        var buffer = new float[4410 * 2];

        for (var i = 0; i < 4410; i++)
        {
            buffer[i * 2] = i % 2 == 0 ? left : -left;
            buffer[(i * 2) + 1] = right;
        }

        return buffer;
    }

    /// <summary>A feeder for an output like AudioBus's (300ms over three buffers: heard 200ms after a refill is read), on a clock the test moves.</summary>
    private static AudioLevels.Feeder Feeder(AudioLevels.Meter meter, Func<long> clock, int channels = 2, int rate = 44100) =>
        new(meter, rate, channels, latencyMs: 300, buffers: 3, clock: clock);

    [Fact]
    public void A_read_is_cut_into_blocks_of_power_each_heard_after_the_one_before()
    {
        long now = 1_000_000;
        var meter = new AudioLevels.Meter("T");
        var feeder = Feeder(meter, () => now);

        feeder.Feed(TenthOfASecond(), 0, 4410 * 2);

        // The whole read: ten blocks, from now on (an output's first read
        // has nothing queued in front of it).
        var all = meter.Heard(now - 1, now + (9 * Block));
        Assert.Equal(10, all.Blocks);
        Assert.Equal(0.25f, all.Left, 5);
        Assert.Equal(0.0625f, all.Right, 5);

        // Now, only the first block has been heard.
        Assert.Equal(1, meter.Heard(now - 1, now).Blocks);

        // Before anything is heard, and after the last block: silence.
        Assert.Equal(0, meter.Heard(now - 2, now - 1).Blocks);
        Assert.Equal(0f, meter.Heard(now + (9 * Block), now + (20 * Block)).Left);
    }

    [Fact]
    public void The_first_fill_plays_in_turn_and_a_refill_is_heard_a_queue_from_now()
    {
        long now = 1_000_000;
        var meter = new AudioLevels.Meter("T");
        var feeder = Feeder(meter, () => now);

        // The first fill: three buffers read at once, played one after another.
        feeder.Feed(TenthOfASecond(0.1f, 0.1f), 0, 4410 * 2);
        feeder.Feed(TenthOfASecond(0.2f, 0.2f), 0, 4410 * 2);
        feeder.Feed(TenthOfASecond(0.3f, 0.3f), 0, 4410 * 2);

        Assert.Equal(0.04f, meter.Heard(now + (14 * Block), now + (15 * Block)).Left, 4);
        Assert.Equal(0.09f, meter.Heard(now + (24 * Block), now + (25 * Block)).Left, 4);

        // A buffer later the first has played and is refilled: heard after
        // the two still queued - 200ms from now - which is where the first
        // fill's stamps have got to.
        now += 10 * Block;
        feeder.Feed(TenthOfASecond(0.4f, 0.4f), 0, 4410 * 2);
        Assert.Equal(0.16f, meter.Heard(now + (20 * Block), now + (21 * Block)).Left, 4);
    }

    [Fact]
    public void Stamps_that_stray_from_the_queue_are_put_back_and_a_pause_comes_back_in_step()
    {
        long now = 1_000_000;
        var meter = new AudioLevels.Meter("T");
        var feeder = Feeder(meter, () => now);

        feeder.Feed(TenthOfASecond(), 0, 4410 * 2);

        // Five seconds of nothing - paused, or the sound card's clock gone
        // its own way - then a refill: heard 200ms from now, not straight
        // after the last block and not now.
        now += 5 * System.Diagnostics.Stopwatch.Frequency;
        feeder.Feed(TenthOfASecond(0.1f, 0.1f), 0, 4410 * 2);

        Assert.Equal(0, meter.Heard(now - 1, now + (19 * Block)).Blocks);
        Assert.Equal(0.01f, meter.Heard(now + (19 * Block), now + (20 * Block)).Left, 4);
    }

    [Fact]
    public void A_cut_drops_what_was_queued_but_not_what_comes_after()
    {
        long now = 1_000_000;
        var meter = new AudioLevels.Meter("T");
        var feeder = Feeder(meter, () => now);

        feeder.Feed(TenthOfASecond(), 0, 4410 * 2);
        meter.Cut(now + (2 * Block));

        // The blocks already heard by the cut stay; the rest are never heard.
        Assert.Equal(3, meter.Heard(now - 1, now + (9 * Block)).Blocks);

        // A new output's sound, written after the cut, is.
        var next = Feeder(meter, () => now);
        next.Feed(TenthOfASecond(), 0, 4410 * 2);
        Assert.True(meter.Heard(now + (3 * Block), now + (9 * Block)).Blocks > 0);
    }

    [Fact]
    public void A_block_is_carried_across_reads_and_mono_reads_the_same_both_sides()
    {
        long now = 1_000_000;
        var meter = new AudioLevels.Meter("T");
        var feeder = Feeder(meter, () => now, channels: 1);

        var first = new float[300];
        Array.Fill(first, 0.5f);
        feeder.Feed(first, 0, first.Length);

        // 300 of the block's 441 frames: nothing written yet.
        Assert.Equal(0, meter.Heard(now - 1, now + Second).Blocks);

        feeder.Feed(new float[141], 0, 141);

        var block = meter.Heard(now - 1, now + Second);
        Assert.Equal(0.25f * 300 / 441, block.Left, 5);
        Assert.Equal(block.Left, block.Right);
    }

    [Fact]
    public void Nothing_fed_to_it_can_make_it_throw_or_pin_the_bar()
    {
        long now = 1_000_000;
        var meter = new AudioLevels.Meter("T");
        var feeder = Feeder(meter, () => now, channels: 1);
        var buffer = new float[441];

        // Out of range, negative, empty: ignored.
        feeder.Feed(buffer, 100, 441);
        feeder.Feed(buffer, -1, 5);
        feeder.Feed(Array.Empty<float>(), 0, 0);

        // Not a number counts as silence; infinity as "very loud" (4).
        buffer[0] = float.NaN;
        buffer[1] = float.PositiveInfinity;
        feeder.Feed(buffer, 0, buffer.Length);

        Assert.Equal(16f / 441, meter.Heard(now - 1, now).Left, 5);
    }

    [Fact]
    public void The_newest_block_is_found_after_the_ring_has_gone_round()
    {
        long now = 10;
        var meter = new AudioLevels.Meter("T");
        var feeder = new AudioLevels.Feeder(meter, 1000, 1, latencyMs: 300, buffers: 3, clock: () => now);

        // Six hundred blocks into a ring of 512, the last one loud.
        var buffer = new float[10 * 600];
        Array.Fill(buffer, 0.7f, buffer.Length - 10, 10);
        feeder.Feed(buffer, 0, buffer.Length);

        var last = now + (599 * Block);
        Assert.Equal(0.49f, meter.Heard(last - 1, last).Left, 4);
    }

    [Fact]
    public void The_tap_hands_the_sound_on_unchanged()
    {
        var source = new Constant(0.4f);
        var meter = new AudioLevels.Meter("T");
        var tap = new AudioLevels.Tap(source, meter, latencyMs: 400, buffers: 4);
        var buffer = new float[882 * 2];

        Assert.Equal(buffer.Length, tap.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, sample => Assert.Equal(0.4f, sample));
        Assert.Same(source.WaveFormat, tap.WaveFormat);

        var heard = meter.Heard(0, System.Diagnostics.Stopwatch.GetTimestamp() + Second);
        Assert.Equal(0.16f, heard.Left, 4);
    }

    [Fact]
    public void A_bar_moves_like_a_VU_needle_and_its_peak_line_holds()
    {
        var bar = new ChannelStrips.Bar();
        var t = TimeSpan.FromSeconds(10);
        var frame = TimeSpan.FromMilliseconds(33);

        // Full scale from silence: part of the way after one frame, nearly
        // there by 300ms - not at once, as a peak meter would be.
        bar.Take(1.0, t += frame, frame);
        Assert.True(bar.Db < -2f, $"one frame in: {bar.Db}");

        for (var i = 0; i < 9; i++)
        {
            bar.Take(1.0, t += frame, frame);
        }

        Assert.True(bar.Db > -0.1f, $"300ms in: {bar.Db}");
        Assert.True(bar.HoldDb >= bar.Db);

        // And down again the same way; the peak line holds where it was.
        bar.Take(0.0, t += frame, frame);
        Assert.True(bar.Db < -0.1f && bar.Db > -10f, $"one frame down: {bar.Db}");
        Assert.True(bar.HoldDb > -0.1f);

        // Past the hold, the peak line falls, never below the bar.
        for (var i = 0; i < 60; i++)
        {
            bar.Take(0.0, t += frame, frame);
        }

        Assert.True(bar.HoldDb < -0.1f && bar.HoldDb >= bar.Db);
    }

    [Fact]
    public void Power_becomes_the_scale_s_dB_and_never_leaves_it()
    {
        Assert.Equal(0f, ChannelStrips.ToDb(1f), 3);
        Assert.Equal(-3.01f, ChannelStrips.ToDb(0.5f), 2);
        Assert.Equal(0f, ChannelStrips.ToDb(9f));
        Assert.Equal(ChannelStrips.FloorDb, ChannelStrips.ToDb(0f));
        Assert.Equal(ChannelStrips.FloorDb, ChannelStrips.ToDb(float.NaN));
        Assert.Equal(ChannelStrips.FloorDb, ChannelStrips.ToDb(0.00001f));

        // A full-scale sine (mean square a half) reads 0, as a VU is set.
        Assert.Equal(0f, ChannelStrips.ToDb(0.5f * ChannelStrips.SineCalibration), 3);

        Assert.Equal(1f, ChannelStrips.Fraction(0f));
        Assert.Equal(0f, ChannelStrips.Fraction(ChannelStrips.FloorDb));
        Assert.Equal(0.5f, ChannelStrips.Fraction(-20f), 3);
    }

    [Fact]
    public void The_strips_follow_their_meters_and_sit_at_the_foot_when_nothing_plays()
    {
        var seen = StaThread.Run(() =>
        {
            var loud = new AudioLevels.Meter("A");
            var quiet = new AudioLevels.Meter("B");
            using var strips = new ChannelStrips(loud, quiet);

            // Heard just now, both sides at full scale, over a few frames.
            for (var i = 0; i < 12; i++)
            {
                loud.Write(System.Diagnostics.Stopwatch.GetTimestamp() - 1, 0.5f, 0.5f);
                strips.Step();
                Thread.Sleep(33);
            }

            return (strips.Bars.Count, loudDb: strips.Bars[0].Db, quietDb: strips.Bars[2].Db, size: strips.Measure());
        }, TimeSpan.FromSeconds(30), () => "stepping the channel strips");

        Assert.Equal(4, seen.Count);
        Assert.True(seen.loudDb > -1f, $"loud: {seen.loudDb}");
        Assert.Equal(ChannelStrips.FloorDb, seen.quietDb);
        Assert.True(seen.size.Width > 0 && seen.size.Height > 0);
    }

    [Fact]
    public void The_three_outputs_each_feed_their_own_strip_and_stops_are_cut()
    {
        // Read from the source, not run: the outputs open sound devices.
        Assert.Contains("new(AudioLevels.Voice, MixSampleRate, MixChannels, OutputLatencyMs, OutputBuffers)", Source("AudioBus.cs"));

        var music = Source("MusicPlayer.cs");
        Assert.Contains("new AudioLevels.Tap(duckGain, AudioLevels.Music, BedLatencyMs, BedBuffers)", music);
        Assert.Contains("AudioLevels.Music.Cut();", music);

        var crowd = Source("AmbienceService.cs");
        Assert.Contains("new AudioLevels.Tap(_duckGain, AudioLevels.Crowd, MusicPlayer.BedLatencyMs, MusicPlayer.BedBuffers)", crowd);
        Assert.Contains("AudioLevels.Crowd.Cut();", crowd);
    }

    [Fact]
    public void The_bus_is_split_into_voice_and_effects_and_only_the_announcer_is_voice()
    {
        var bus = Source("AudioBus.cs");
        Assert.Contains("new(AudioLevels.Effects, MixSampleRate, MixChannels, OutputLatencyMs, OutputBuffers)", bus);
        Assert.Contains("bool allowBoost = false, bool voice = false", bus);
        Assert.Contains("Tapped.Heard(_voice, _offAir, buffer, offset, read);", bus);

        // The announcer's lines; nothing else says it is voice, so everything
        // else is SFX - recordings played back included.
        Assert.Contains("allowBoost: true, voice: true,", Source("AnnouncementService.Playback.cs"));
        Assert.DoesNotContain("voice: true", Source("AnnouncerControl.Director.cs"));
    }

    [Fact]
    public void The_board_is_voice_sfx_music_and_crowd_and_Race_Control_s_is_the_same_board_smaller()
    {
        var seen = StaThread.Run(() =>
        {
            using var full = new ChannelStrips();
            using var compact = ChannelStrips.ForRaceControl();

            return (full: full.AccessibleName, fullBars: full.Bars.Count, compactBars: compact.Bars.Count,
                fullSize: full.Measure(), compactSize: compact.Measure());
        }, TimeSpan.FromSeconds(30), () => "building the boards");

        Assert.Equal("Level meters: voice, sfx, music, crowd", seen.full);
        Assert.Equal(8, seen.fullBars);
        Assert.Equal(8, seen.compactBars);
        Assert.True(seen.compactSize.Height < seen.fullSize.Height, $"{seen.compactSize} against {seen.fullSize}");
    }

    [Fact]
    public void Race_Control_s_row_carries_the_meters_and_the_strip_under_it_says_what_is_playing()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new Form { Width = 1600, Height = 300 };
            var bar = new ButtonBar(tight: true);
            bar.With(new Button { Text = "Stage", Width = 140, Height = 40 });
            var row = new FlagRow(bar) { Dock = DockStyle.Top };
            var strip = new HoldStrip { Dock = DockStyle.Top };
            form.Controls.Add(strip);
            form.Controls.Add(row);

            // Shown, so Visible reads each control's own setting rather than
            // false for everything in a window that is not on screen.
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            row.PerformLayout();

            strip.SetNowPlaying("After-the-flag bed · Neon Nights");
            row.SetNowPlaying("After-the-flag bed · Neon Nights");

            // Narrow: the meters give way, whole.
            var wide = (row.Meters.Visible, row.Meters.Width);
            form.Width = 360;
            Application.DoEvents();
            row.PerformLayout();
            var narrow = row.Meters.Visible;
            form.Close();

            return (wide, narrow, strip: strip.NowPlayingText, row: row.NowPlayingText, rowHeight: row.Height, rowWithin: row.Meters.Bottom <= row.Height);
        }, TimeSpan.FromSeconds(30), () => "laying the flag row out");

        Assert.True(seen.wide.Visible && seen.wide.Width > 0);
        Assert.False(seen.narrow);
        Assert.Equal("After-the-flag bed · Neon Nights", seen.strip);
        Assert.Equal("After-the-flag bed · Neon Nights", seen.row);
        Assert.True(seen.rowWithin);
    }

    [Fact]
    public void Race_Control_puts_now_playing_on_the_strip_under_the_row()
    {
        var morph = Source("RaceControlControl.Morph.cs");
        Assert.Contains("_holdStrip.SetNowPlaying(playing);", morph);
        Assert.Contains("_flagRow.SetNowPlaying(playing);", morph);
    }

    private sealed class Constant : ISampleProvider
    {
        private readonly float _value;

        public Constant(float value) => _value = value;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Fill(buffer, _value, offset, count);
            return count;
        }
    }

    /// <summary>The folder the repo is checked out in, found by the file that is only ever at its root (as ShowOnScreenTests finds it).</summary>
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
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));
}
