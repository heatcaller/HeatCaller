namespace HeatCaller.Tests;

/// <summary>
/// The show's own music: finding the body of a piece, looping it without a
/// seam, and ending on the piece's own fade.
///
/// Nothing here plays anything - AudioBus is silent for the whole test run
/// (TestSetup). What is tested is everything underneath that: the
/// measurement that decides what loops, the sample arithmetic that makes
/// it loop, and the counting that silences the club's music while a show
/// is on.
///
/// The pieces are written here as wav files rather than carried as test
/// data, so each test says in its own body what shape of music it is
/// about - and so none of this depends on an mp3 decoder, which on Windows
/// means Media Foundation and a machine that has it.
/// </summary>
public sealed class ShowAudioTests : IDisposable
{
    private readonly TempAppData _data = new();

    /// <summary>Low on purpose: every piece below is seconds long, and this keeps the arrays small.</summary>
    private const int Rate = 8000;

    public ShowAudioTests()
    {
        ShowOnAir.ResetForTests();
        ShowMusic.Reload();
    }

    public void Dispose()
    {
        // Both static, so a test that left a show open would silence the
        // music in every test after it - and in a real app run started by
        // the layout test in the same process - and the loaded audio would
        // sit in memory for the rest of the run.
        ShowOnAir.ResetForTests();
        ShowMusic.Reload();
        _data.Dispose();
    }

    // --- Finding the body of a piece -----------------------------------------------------

    [Fact]
    public void A_fade_on_the_end_is_kept_out_of_the_loop()
    {
        // Eight seconds of music, then a two-second fade. The fade is the
        // show's ending; looping it would fade to nothing and snap back to
        // full every time round.
        var piece = Join(Music(8), FadeOut(Music(2)));

        var shape = ShowShape.Find(piece, 1, Rate);

        Assert.Equal(0, shape.LoopStart);
        Assert.InRange(Seconds(shape.LoopEnd), 7.8, 8.2);
    }

    [Fact]
    public void A_slow_fade_is_found_from_its_top_not_its_middle()
    {
        // The reason the measurement walks back up the slope. A level
        // threshold alone finds the MIDDLE of a fade - on a straight fade,
        // -6 dB is halfway down - and a loop carrying the first half of a
        // ten-second fade droops audibly every time round.
        var piece = Join(Music(50), FadeOut(Music(10)));

        var shape = ShowShape.Find(piece, 1, Rate);

        Assert.InRange(Seconds(shape.LoopEnd), 49.5, 50.5);
    }

    [Fact]
    public void A_piece_that_does_not_fade_loops_whole()
    {
        var piece = Music(10);

        var shape = ShowShape.Find(piece, 1, Rate);

        Assert.Equal(0, shape.LoopStart);
        Assert.InRange(Seconds(shape.LoopEnd), 9.9, 10.0);
    }

    [Fact]
    public void A_fade_in_is_kept_out_of_the_loop_too()
    {
        var piece = Join(FadeIn(Music(1)), Music(8), FadeOut(Music(2)));

        var shape = ShowShape.Find(piece, 1, Rate);

        Assert.InRange(Seconds(shape.LoopStart), 0.8, 1.2);
        Assert.InRange(Seconds(shape.LoopEnd), 8.8, 9.2);
    }

    [Fact]
    public void A_piece_too_short_to_measure_is_all_body()
    {
        var shape = ShowShape.Find(Music(0.2), 1, Rate);

        Assert.Equal(0, shape.LoopStart);
        Assert.Equal((int)(0.2 * Rate), shape.LoopEnd);
    }

    [Fact]
    public void A_piece_that_is_all_one_swell_is_not_cut_down_to_nothing()
    {
        // Tightening both ends of a ten-second swell would leave half a
        // second to loop. The looser reading is used instead - there was
        // no ending to keep back, which is the honest answer.
        var shape = ShowShape.Find(FadeIn(Music(10)), 1, Rate);

        Assert.True(
            Seconds(shape.BodyFrames) >= 4,
            $"a swell should still leave something worth looping, got {Seconds(shape.BodyFrames):0.0}s");
    }

    [Fact]
    public void Silence_is_all_body()
    {
        // Nothing to measure against. Looping silence is silent either way;
        // what matters is that it doesn't throw or divide by nothing.
        var shape = ShowShape.Find(new float[10 * Rate], 1, Rate);

        Assert.Equal(0, shape.LoopStart);
        Assert.Equal(10 * Rate, shape.LoopEnd);
    }

    [Fact]
    public void Stereo_is_measured_the_same_as_mono()
    {
        var piece = Stereo(Join(Music(8), FadeOut(Music(2))));

        var shape = ShowShape.Find(piece, 2, Rate);

        Assert.InRange(Seconds(shape.LoopEnd), 7.8, 8.2);
    }

    // --- Loading ---------------------------------------------------------------------------

    [Fact]
    public void A_file_that_is_not_there_says_which_one()
    {
        var clip = ShowClip.Load(Path.Combine(AppPaths.Root, "bed.wav"), out var problem);

        Assert.Null(clip);
        Assert.NotNull(problem);
        Assert.Contains("bed.wav", problem);
    }

    [Fact]
    public void Nothing_chosen_says_so()
    {
        Assert.Null(ShowClip.Load(string.Empty, out var problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void A_file_with_no_sound_in_it_says_so()
    {
        var path = Wav("empty.wav", Array.Empty<float>());

        var clip = ShowClip.Load(path, out var problem);

        Assert.Null(clip);
        Assert.NotNull(problem);
        Assert.Contains("no sound", problem);
    }

    [Fact]
    public void Something_far_too_long_to_loop_is_refused_with_a_reason()
    {
        // A bed is looped, so an album track is a mistake worth telling a
        // club about rather than quietly holding a gigabyte of it.
        var path = Wav("album.wav", Music(ShowClip.MostSeconds + 30));

        var clip = ShowClip.Load(path, out var problem);

        Assert.Null(clip);
        Assert.NotNull(problem);
        Assert.Contains("album.wav", problem);
    }

    [Fact]
    public void A_loaded_piece_knows_its_own_shape()
    {
        var path = Wav("bed.wav", Join(Music(8), FadeOut(Music(2))));

        var clip = ShowClip.Load(path, out var problem);

        Assert.Null(problem);
        Assert.NotNull(clip);
        Assert.Equal(1, clip!.Channels);
        Assert.Equal(Rate, clip.SampleRate);
        Assert.InRange(clip.Length.TotalSeconds, 9.9, 10.1);
        Assert.InRange(Seconds(clip.Shape.LoopEnd), 7.8, 8.2);
    }

    // --- Looping ---------------------------------------------------------------------------

    [Fact]
    public void The_bed_runs_for_far_longer_than_the_piece_it_came_from()
    {
        // The whole point. A show runs for as long as the read takes, and
        // the bed has to still be there at the end of it.
        var bed = new ShowBed(Clip("bed.wav", Join(Music(8), FadeOut(Music(2)))));

        var buffer = new float[Rate];
        var frames = 0;

        for (var i = 0; i < 40; i++)
        {
            var read = bed.Read(buffer, 0, buffer.Length);

            Assert.Equal(buffer.Length, read);
            frames += read;
        }

        Assert.False(bed.Finished);
        Assert.True(Seconds(frames) > 30, "the bed should have outlasted the piece several times over");
    }

    [Fact]
    public void The_first_time_round_is_played_straight()
    {
        // There is nothing behind the first pass to crossfade its head
        // against. Fading it up from the piece's own tail would open every
        // show with the end of the bed mixed over its beginning.
        var clip = Clip("bed.wav", Join(Music(8), FadeOut(Music(2))));
        var bed = new ShowBed(clip);

        var seam = (int)(ShowBed.SeamSeconds * Rate);
        var buffer = new float[seam];

        bed.Read(buffer, 0, buffer.Length);

        for (var i = 0; i < seam; i++)
        {
            Assert.Equal(clip.Samples[clip.Shape.LoopStart + i], buffer[i], 4);
        }
    }

    [Fact]
    public void The_seam_is_crossfaded_the_second_time_round()
    {
        // Jumping from the end of the body back to its start clicks unless
        // the two happen to match. The body's tail is mixed over its head
        // instead, so every time round is identical and the join is a
        // crossfade rather than an edge.
        var clip = Clip("bed.wav", Join(Music(8), FadeOut(Music(2))));
        var bed = new ShowBed(clip);

        var body = clip.Shape.LoopEnd - clip.Shape.LoopStart;
        var seam = Math.Clamp((int)(ShowBed.SeamSeconds * Rate), 1, body / 3);
        var period = body - seam;

        // Round once, so the next frames are the crossfaded head.
        Read(bed, period);

        var buffer = new float[seam];
        bed.Read(buffer, 0, buffer.Length);

        // Mid-seam the two halves are mixed in equal measure, so the output
        // follows neither on its own.
        var at = seam / 2;
        var head = clip.Samples[clip.Shape.LoopStart + at];
        var tail = clip.Samples[clip.Shape.LoopStart + period + at];

        Assert.Equal(head * 0.5f + tail * 0.5f, buffer[at], 3);
    }

    // --- Ending ----------------------------------------------------------------------------

    [Fact]
    public void Winding_up_plays_the_piece_s_own_ending_and_then_finishes()
    {
        var clip = Clip("bed.wav", Join(Music(8), FadeOut(Music(2))));
        var bed = new ShowBed(clip);

        Read(bed, 4 * Rate);

        bed.WindUp();

        var ending = clip.Frames - clip.Shape.LoopEnd;
        var played = ReadToEnd(bed, longest: ending + 2 * Rate);

        Assert.True(bed.Finished, "the bed should finish once its ending has played");
        Assert.InRange(played, ending - Rate, ending + Rate);
    }

    [Fact]
    public void A_piece_with_no_ending_of_its_own_is_faded_out_instead()
    {
        // A piece that simply stops has nothing to hand over to, and
        // stopping dead is the one way a show must not end.
        var bed = new ShowBed(Clip("bed.wav", Music(10)));

        Read(bed, 2 * Rate);

        bed.WindUp();

        var buffer = new float[(int)(ShowBed.FadeOutSeconds * Rate) + Rate];
        var read = bed.Read(buffer, 0, buffer.Length);

        Assert.True(bed.Finished);
        Assert.True(read < buffer.Length, "the fade should have run out inside the buffer");

        // Ends at nothing rather than mid-note.
        for (var i = Math.Max(0, read - 40); i < read; i++)
        {
            Assert.True(Math.Abs(buffer[i]) < 0.05f, $"sample {i} was {buffer[i]:0.000} - the fade should be out by here");
        }
    }

    [Fact]
    public void Winding_up_twice_is_harmless()
    {
        var bed = new ShowBed(Clip("bed.wav", Join(Music(8), FadeOut(Music(2)))));

        bed.WindUp();
        bed.WindUp();

        ReadToEnd(bed, longest: 20 * Rate);

        Assert.True(bed.Finished);
    }

    [Fact]
    public void A_one_shot_plays_once_and_runs_short()
    {
        var clip = Clip("opener.wav", Music(2));
        var opener = new ShowOneShot(clip);

        var buffer = new float[3 * Rate];
        var read = opener.Read(buffer, 0, buffer.Length);

        Assert.Equal(clip.Samples.Length, read);
        Assert.Equal(0, opener.Read(buffer, 0, buffer.Length));
    }

    // --- Which segments get it -------------------------------------------------------------

    [Fact]
    public void Nothing_saved_means_the_evening_s_top_and_tail()
    {
        var on = ShowSegments.Parse(null);

        Assert.Contains(SegmentKind.ColdOpen, on);
        Assert.Contains(SegmentKind.SignOffNumbers, on);
        Assert.DoesNotContain(SegmentKind.PaceNote, on);
    }

    [Fact]
    public void A_blank_setting_still_means_the_default_pair()
    {
        // A club that has never opened the page should still get the
        // staging - that is the whole reason this list is the ones that are
        // ON rather than the ones that are off.
        Assert.Equal(Sorted(ShowSegments.Default), Sorted(ShowSegments.Parse("   ")));
    }

    [Fact]
    public void None_means_no_music_on_anything()
    {
        // Which a blank string cannot say, hence the word.
        Assert.Empty(ShowSegments.Parse("none"));
        Assert.Empty(ShowSegments.Parse(" NONE "));
    }

    [Fact]
    public void Saved_names_come_back()
    {
        var chosen = new[] { SegmentKind.ColdOpen, SegmentKind.ChampionshipCheck };

        var back = ShowSegments.Parse(ShowSegments.Save(chosen));

        Assert.Equal(Sorted(chosen), Sorted(back));
    }

    [Fact]
    public void Choosing_no_segments_saves_as_none_and_comes_back_empty()
    {
        // Saving an empty list as a blank string would read back as the
        // default pair, so unticking everything would quietly turn the
        // music back on for the cold open.
        var saved = ShowSegments.Save(Array.Empty<SegmentKind>());

        Assert.Equal(ShowSegments.Nothing, saved);
        Assert.Empty(ShowSegments.Parse(saved));
    }

    [Fact]
    public void Every_segment_a_club_may_choose_is_a_real_one()
    {
        // The page builds its tick boxes from this list and asks the budget
        // for each one's title, so a name that has been renamed or dropped
        // from SegmentKind would throw while the page was being built.
        foreach (var kind in ShowSegments.Candidates)
        {
            // Its title is the tick box's caption, so a segment with none
            // would put a blank row on the page.
            Assert.False(
                string.IsNullOrWhiteSpace(CommentaryBudget.Spec(kind).Title),
                $"{kind} has no title to put on a tick box");
        }

        Assert.All(ShowSegments.Default, kind => Assert.Contains(kind, ShowSegments.Candidates));
    }

    [Fact]
    public void A_name_from_a_newer_version_is_ignored_rather_than_throwing()
    {
        // A setting written by a newer version and then rolled back must
        // not stop the announcer.
        var back = ShowSegments.Parse("ColdOpen,TheWeatherReport");

        Assert.Equal(new[] { SegmentKind.ColdOpen }, Sorted(back));
    }

    // --- Silencing the club's music --------------------------------------------------------

    [Fact]
    public void The_music_comes_back_only_once_the_last_show_has_ended()
    {
        var said = new List<bool>();
        void Watch(bool playing) => said.Add(playing);

        ShowOnAir.PlayingChanged += Watch;

        try
        {
            ShowOnAir.Begin();
            ShowOnAir.Begin();

            ShowOnAir.End();

            Assert.True(ShowOnAir.Playing, "one show ending must not bring the music back under the other");

            ShowOnAir.End();

            Assert.False(ShowOnAir.Playing);
        }
        finally
        {
            ShowOnAir.PlayingChanged -= Watch;
        }

        // Only on a real change, so the music isn't told twice.
        Assert.Equal(new[] { true, false }, said);
    }

    [Fact]
    public void Ending_more_shows_than_started_is_harmless()
    {
        ShowOnAir.End();
        ShowOnAir.End();

        Assert.False(ShowOnAir.Playing);

        ShowOnAir.Begin();

        Assert.True(ShowOnAir.Playing);
    }

    // --- Making pieces of music ------------------------------------------------------------

    private static double Seconds(int frames) => (double)frames / Rate;

    /// <summary>
    /// In a fixed order. Both sides of these comparisons are sets, and a
    /// set does not promise the order it hands its members back in - a
    /// test that compared them as sequences would pass or fail on how the
    /// hashes happened to land.
    /// </summary>
    private static SegmentKind[] Sorted(IEnumerable<SegmentKind> kinds) => kinds.OrderBy(k => k).ToArray();

    /// <summary>
    /// Music-shaped audio: a tone with a little level variation, because a
    /// perfectly constant signal is not what the measurement meets and
    /// would not exercise the slope it walks.
    /// </summary>
    private static float[] Music(double seconds)
    {
        var frames = (int)(seconds * Rate);
        var samples = new float[frames];

        // Fixed seed: a test that measures must not depend on the draw.
        var wobble = new Random(7);

        for (var i = 0; i < frames; i++)
        {
            var level = 0.45 * (0.9 + 0.2 * wobble.NextDouble());
            samples[i] = (float)(level * Math.Sin(2 * Math.PI * 220 * i / Rate) * Math.Sqrt(2));
        }

        return samples;
    }

    private static float[] FadeOut(float[] piece)
    {
        var faded = new float[piece.Length];

        for (var i = 0; i < piece.Length; i++)
        {
            faded[i] = piece[i] * (1f - (float)i / piece.Length);
        }

        return faded;
    }

    private static float[] FadeIn(float[] piece)
    {
        var faded = new float[piece.Length];

        for (var i = 0; i < piece.Length; i++)
        {
            faded[i] = piece[i] * ((float)i / piece.Length);
        }

        return faded;
    }

    private static float[] Join(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>The same piece in both channels, to check the measurement counts frames and not samples.</summary>
    private static float[] Stereo(float[] mono)
    {
        var stereo = new float[mono.Length * 2];

        for (var i = 0; i < mono.Length; i++)
        {
            stereo[2 * i] = mono[i];
            stereo[2 * i + 1] = mono[i];
        }

        return stereo;
    }

    private ShowClip Clip(string name, float[] samples)
    {
        var clip = ShowClip.Load(Wav(name, samples), out var problem);

        Assert.Null(problem);

        return clip!;
    }

    /// <summary>
    /// Writes a 16-bit PCM wav in the test's own folder.
    ///
    /// By hand rather than through NAudio: a wav header is nine lines, and
    /// a test that writes its own file cannot be confused by what a writer
    /// decided to put in one.
    /// </summary>
    private string Wav(string name, float[] samples, int channels = 1)
    {
        var path = Path.Combine(AppPaths.Root, name);
        Directory.CreateDirectory(AppPaths.Root);

        using var file = File.Create(path);
        using var write = new BinaryWriter(file);

        var bytes = samples.Length * 2;

        write.Write("RIFF".ToCharArray());
        write.Write(36 + bytes);
        write.Write("WAVE".ToCharArray());
        write.Write("fmt ".ToCharArray());
        write.Write(16);
        write.Write((short)1);
        write.Write((short)channels);
        write.Write(Rate);
        write.Write(Rate * channels * 2);
        write.Write((short)(channels * 2));
        write.Write((short)16);
        write.Write("data".ToCharArray());
        write.Write(bytes);

        foreach (var sample in samples)
        {
            write.Write((short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue));
        }

        return path;
    }

    private static int Read(ShowBed bed, int frames)
    {
        var buffer = new float[Math.Min(frames, Rate)];
        var done = 0;

        while (done < frames)
        {
            var read = bed.Read(buffer, 0, Math.Min(buffer.Length, frames - done));

            if (read <= 0)
            {
                break;
            }

            done += read;
        }

        return done;
    }

    private static int ReadToEnd(ShowBed bed, int longest)
    {
        var buffer = new float[Rate / 4];
        var done = 0;

        while (done < longest)
        {
            var read = bed.Read(buffer, 0, buffer.Length);

            done += read;

            if (read < buffer.Length)
            {
                break;
            }
        }

        return done;
    }
}
