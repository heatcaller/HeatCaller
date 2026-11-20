namespace HeatCaller.Tests;

/// <summary>
/// LISTEN NOW'S MUSIC, KEPT IN TIME (19 Nov 2026, the music "skips or
/// drops"). The sample math under the stream: a resampler that carries on
/// from one read to the next instead of starting afresh each time, a bed
/// that comes straight back after a stall rather than waiting out half a
/// second of silence, and trims and dry spells that fade rather than cut.
/// See RecapBed.Resampler and ListenStream.Run.Bed.
/// </summary>
public sealed class ListenStreamBedSmoothTests
{
    private const int Rate = ListenStream.SampleRate;
    private const int Tenth = Rate / 10;

    private static readonly int Fade = ListenStream.Run.Bed.FadeSamples;

    private static readonly int Lead = (int)(ListenStream.BedLeadSeconds * Rate);

    private static float[] Level(float level, int samples = Tenth) => Enumerable.Repeat(level, samples).ToArray();

    /// <summary>A stereo sine at <paramref name="rate"/>, interleaved.</summary>
    private static float[] Sine(int rate, int frames, double hz = 440, float size = 0.5f)
    {
        var stereo = new float[frames * 2];

        for (var f = 0; f < frames; f++)
        {
            stereo[2 * f] = stereo[(2 * f) + 1] = size * (float)Math.Sin(2 * Math.PI * hz * f / rate);
        }

        return stereo;
    }

    private static float[] InReads(RecapBed.Resampler resampler, float[] stereo, int rate, IReadOnlyList<int> frameSizes)
    {
        var output = new List<float>();
        var at = 0;
        var k = 0;

        while (at < stereo.Length)
        {
            var frames = Math.Min(frameSizes[k++ % frameSizes.Count], (stereo.Length - at) / 2);
            var read = stereo[at..(at + (frames * 2))];
            output.AddRange(resampler.Next(read, read.Length, 2, rate));
            at += frames * 2;
        }

        return output.ToArray();
    }

    private static float LargestStep(IReadOnlyList<float> samples)
    {
        var most = 0f;

        for (var i = 1; i < samples.Count; i++)
        {
            most = Math.Max(most, Math.Abs(samples[i] - samples[i - 1]));
        }

        return most;
    }

    // ------------------------------------------------------------------
    //  The resampler
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(48000)]
    [InlineData(32000)]
    [InlineData(96000)]
    [InlineData(22050)]
    public void Reads_of_any_size_come_out_as_one_long_read_would(int rate)
    {
        var sine = Sine(rate, rate * 2);
        var whole = new RecapBed.Resampler(Rate).Next(sine, sine.Length, 2, rate);
        var pieces = InReads(new RecapBed.Resampler(Rate), sine, rate, new[] { 4800, 4799, 1, 333, 4410, 2, 1024 });

        Assert.Equal(whole.Length, pieces.Length);

        for (var i = 0; i < whole.Length; i++)
        {
            Assert.Equal(whole[i], pieces[i], 5);
        }
    }

    [Fact]
    public void A_sine_has_no_step_at_any_read_boundary()
    {
        // 440 Hz at half size moves at most 2*pi*440*0.5/44100 a sample.
        var sine = Sine(48000, 48000 * 3);
        var output = InReads(new RecapBed.Resampler(Rate), sine, 48000, new[] { 4799, 4801, 480, 7 });
        var steepest = (float)(2 * Math.PI * 440 * 0.5 / Rate);

        Assert.InRange(LargestStep(output), 0f, steepest * 1.05f);
    }

    [Fact]
    public void An_hour_of_reads_neither_gains_nor_loses_a_sample()
    {
        // The one-read-at-a-time conversion dropped the fraction left at the
        // end of each read: 4799 frames at 48k is 4409.08 at 44.1k, so it
        // lost a sample every twelve reads, about 4 s an hour.
        var resampler = new RecapBed.Resampler(Rate);
        var read = new float[4799 * 2];
        long frames = 0;
        long made = 0;

        for (var i = 0; i < 36_000; i++)
        {
            made += resampler.Next(read, read.Length, 2, 48000).Length;
            frames += 4799;
        }

        var expected = (double)frames * Rate / 48000;
        Assert.InRange(made, (long)expected - 1, (long)expected + 1);
    }

    [Fact]
    public void A_read_at_the_streams_rate_is_passed_through_untouched()
    {
        var resampler = new RecapBed.Resampler(Rate);
        var first = resampler.Next(new[] { 0.5f, 0.1f, -0.2f, -0.4f }, 4, 2, Rate);
        var second = resampler.Next(new[] { 0.25f, 0.25f }, 2, 2, Rate);

        Assert.Equal(new[] { 0.3f, -0.3f }, first.Select(x => MathF.Round(x, 5)));
        Assert.Equal(new[] { 0.25f }, second);
    }

    [Fact]
    public void A_track_at_another_rate_carries_straight_on()
    {
        // A 48k track then a 44.1k one, the level steady across the change.
        var resampler = new RecapBed.Resampler(Rate);
        var output = new List<float>();
        output.AddRange(resampler.Next(Level(0.25f, 4801 * 2), 4801 * 2, 2, 48000));
        output.AddRange(resampler.Next(Level(0.25f, 4410 * 2), 4410 * 2, 2, Rate));
        output.AddRange(resampler.Next(Level(0.25f, 4800 * 2), 4800 * 2, 2, 48000));

        Assert.All(output, level => Assert.Equal(0.25f, level, 5));
    }

    [Fact]
    public void Not_a_number_is_silence_and_an_empty_read_is_nothing()
    {
        var resampler = new RecapBed.Resampler(Rate);

        Assert.Empty(resampler.Next(Array.Empty<float>(), 0, 2, 48000));
        Assert.Empty(resampler.Next(new[] { 0.5f }, 1, 1, 0));
        Assert.All(resampler.Next(new[] { float.NaN, float.NaN, float.PositiveInfinity, 1f }, 4, 1, 48000), level => Assert.True(float.IsFinite(level)));
    }

    // ------------------------------------------------------------------
    //  The bed
    // ------------------------------------------------------------------

    [Fact]
    public void A_bed_that_is_keeping_up_plays_its_music_exactly()
    {
        var bed = new ListenStream.Run.Bed();
        var next = 0;

        float[] Numbered()
        {
            var part = new float[Tenth];

            for (var i = 0; i < Tenth; i++)
            {
                part[i] = (next++ % 10_000) * 1e-5f;
            }

            return part;
        }

        for (var i = 0; i < 5; i++)
        {
            bed.Add(Numbered());
        }

        // The first read fades in; after that, sample for sample.
        bed.Under(new float[Tenth]);
        var heard = 1;

        for (var round = 0; round < 50; round++)
        {
            bed.Add(Numbered());
            var mix = new float[Tenth];
            bed.Under(mix);

            for (var i = 0; i < Tenth; i++)
            {
                Assert.Equal(((((heard * Tenth) + i) % 10_000) * 1e-5f), mix[i]);
            }

            heard++;
        }

        var health = bed.TakeHealth();
        Assert.Equal(0, health.Drops);
        Assert.Equal(0, health.Skips);
        Assert.Equal(0, health.SlowedSeconds);
    }

    [Fact]
    public void After_a_stall_the_music_is_back_with_the_next_read_not_half_a_second_later()
    {
        var bed = new ListenStream.Run.Bed();

        for (var i = 0; i < 6; i++)
        {
            bed.Add(Level(0.5f));
        }

        for (var i = 0; i < 6; i++)
        {
            bed.Under(new float[Tenth]);
        }

        // The music's output stalls for three of the bus's reads.
        for (var i = 0; i < 3; i++)
        {
            var silent = new float[Tenth];
            bed.Under(silent);
            Assert.All(silent, level => Assert.Equal(0f, level));
        }

        // One read of it arrives: the very next bus read plays it, faded in.
        bed.Add(Level(0.5f));
        var mix = new float[Tenth];
        bed.Under(mix);

        Assert.InRange(mix[0], 0f, 0.05f);
        Assert.All(mix.Skip(Fade).Take(Tenth - (2 * Fade)), level => Assert.Equal(0.5f, level, 4));

        var health = bed.TakeHealth();
        Assert.Equal(1, health.Drops);
        Assert.Equal(0.3, health.DropSeconds, 3);
    }

    [Fact]
    public void After_a_stall_it_plays_slower_until_its_lead_is_back_then_at_speed()
    {
        var bed = new ListenStream.Run.Bed();

        for (var i = 0; i < 6; i++)
        {
            bed.Add(Level(0.5f));
        }

        for (var i = 0; i < 9; i++)
        {
            bed.Under(new float[Tenth]);
        }

        // From here the music and the bus keep exact step, read for read.
        var silentReads = 0;
        var rounds = 0;
        bed.Add(Level(0.5f));
        bed.Under(new float[Tenth]);
        Assert.True(bed.Slowed);

        while (bed.Slowed && rounds < 600)
        {
            bed.Add(Level(0.5f));
            var mix = new float[Tenth];
            bed.Under(mix);

            if (mix.All(level => level == 0f))
            {
                silentReads++;
            }

            rounds++;
        }

        Assert.False(bed.Slowed);
        Assert.Equal(0, silentReads);
        Assert.InRange(bed.Held, Lead - Tenth, Lead + Tenth);

        // About a half second regained at two per cent slower: 20 to 30 s.
        var health = bed.TakeHealth();
        Assert.Equal(1, health.Drops);
        Assert.InRange(health.SlowedSeconds, 15.0, 30.0);
    }

    [Fact]
    public void Running_dry_fades_the_music_out_rather_than_cutting_it()
    {
        var bed = new ListenStream.Run.Bed();

        for (var i = 0; i < 5; i++)
        {
            bed.Add(Level(0.5f));
        }

        float[] last = Array.Empty<float>();

        for (var i = 0; i < 5; i++)
        {
            last = new float[Tenth];
            bed.Under(last);
        }

        // The last read of what was there ends on next to nothing, and gets
        // there in small steps.
        Assert.Equal(0.5f, last[Tenth - Fade - 1], 4);
        Assert.InRange(last[^1], 0f, 0.5f / Fade * 2);
        Assert.InRange(LargestStep(last[(Tenth - Fade - 1)..]), 0f, (0.5f / Fade) * 1.1f);
    }

    [Fact]
    public void A_trim_crossfades_from_where_the_music_was_to_where_it_jumps()
    {
        var bed = new ListenStream.Run.Bed();

        for (var i = 0; i < 6; i++)
        {
            bed.Add(Level(0.25f));
        }

        bed.Under(new float[Tenth]);

        // The music's device hands over a burst: 0.8 s more than the bus took.
        for (var i = 0; i < 8; i++)
        {
            bed.Add(Level(0.75f));
        }

        var mix = new float[Tenth];
        bed.Under(mix);

        // Starts where it was, blends across, and carries on at the new.
        Assert.InRange(mix[0], 0.25f, 0.27f);
        Assert.InRange(LargestStep(mix), 0f, (0.5f / Fade) * 1.1f);
        Assert.All(mix.Skip(Fade), level => Assert.Equal(0.75f, level));

        // Trimmed to the lead, not to nothing.
        var health = bed.TakeHealth();
        Assert.Equal(1, health.Skips);
        Assert.Equal(0.8, health.SkipSeconds, 3);
        Assert.Equal(ListenStream.BedLeadSeconds, health.LeastHeldSeconds, 3);
        Assert.Equal(Lead - Tenth, bed.Held);
    }

    [Fact]
    public void Back_from_a_bus_stall_it_crossfades_from_what_was_last_heard()
    {
        var bed = new ListenStream.Run.Bed();

        for (var i = 0; i < 6; i++)
        {
            bed.Add(Level(0.25f));
        }

        bed.Under(new float[Tenth]);

        // The bus stops reading for six seconds; the music keeps coming and
        // the oldest of it goes - all but what follows what was last heard.
        for (var i = 0; i < 60; i++)
        {
            bed.Add(Level(0.75f));
        }

        var mix = new float[Tenth];
        bed.Under(mix);

        Assert.InRange(mix[0], 0.25f, 0.27f);
        Assert.InRange(LargestStep(mix), 0f, (0.5f / Fade) * 1.1f);
        Assert.All(mix.Skip(Fade), level => Assert.Equal(0.75f, level));
    }

    [Fact]
    public void Music_stopped_and_started_again_waits_for_its_whole_lead_in_silence()
    {
        var bed = new ListenStream.Run.Bed();

        for (var i = 0; i < 6; i++)
        {
            bed.Add(Level(0.5f));
        }

        for (var i = 0; i < 6 + 60; i++)
        {
            bed.Under(new float[Tenth]);
        }

        bed.Add(Level(0.5f));
        var mix = new float[Tenth];
        bed.Under(mix);

        // A new start, not a drop: no hurry, and nothing slowed.
        Assert.All(mix, level => Assert.Equal(0f, level));
        Assert.False(bed.Slowed);
    }

    [Fact]
    public void The_console_says_how_long_the_music_played_slower()
    {
        var music = new ListenStream.BedHealth(30, 1, 0.3, 0.3, 0, 0, 0.1, 0.62, Rate, 24.5);
        var line = ListenStream.HealthLine(new ListenStream.Health(music, default, 0), null, sayClean: false);

        Assert.Equal(
            "music ran dry 1 time (0.30 s silent, longest 0.30 s). music held 0.10 s to 0.62 s, played slower for 24.50 s to build its lead back.",
            line);
    }

    // ------------------------------------------------------------------
    //  On the way to the relay
    // ------------------------------------------------------------------

    [Fact]
    public void The_rooms_sound_goes_out_between_the_relay_polls_too()
    {
        var publisher = Source("RelayPublisher.cs");

        foreach (var poll in new[] { "CollectCheckIns(pass.Token);", "CollectManageRequests(pass.Token);" })
        {
            var at = publisher.IndexOf(poll, StringComparison.Ordinal);
            Assert.True(at > 0, poll);

            var after = publisher.IndexOf("SendListenLinesWaiting(pass.Token);", at, StringComparison.Ordinal);
            var close = publisher.IndexOf('}', at);
            Assert.True(after > at && after < close, $"the sound waiting goes out after {poll}");
        }
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
