namespace HeatCaller.Tests;

/// <summary>
/// Finding a driver's own clip of the night, and playing the first twenty
/// seconds of it inside the piece about them.
///
/// Nothing here opens a sound device or encodes anything. AudioBus is
/// silent for the whole test run (TestSetup) and an mp3 needs Windows Media
/// Foundation, which is not something a test run should depend on - so the
/// clips below are written as wavs by hand, the same way ShowAudioTests
/// writes the show's music, and read through ShowClip.Load exactly as the
/// real path reads them.
///
/// What is tested is the two halves that can be wrong without a speaker:
/// WHICH file is a given driver's, which is the whole of the matching and
/// is done on file names because nothing indexes a clip; and how much of it
/// gets played, which is frame arithmetic.
/// </summary>
public sealed class DriversOwnClipTests : IDisposable
{
    private readonly TempAppData _data = new();

    /// <summary>Low on purpose: every clip below is seconds long, and this keeps the arrays small.</summary>
    private const int Rate = 8000;

    public void Dispose() => _data.Dispose();

    // --- Which file is this driver's ---------------------------------------

    [Fact]
    public void A_drivers_own_clip_is_found_by_the_name_the_recorder_wrote()
    {
        var night = Night();
        var clip = Clip(night, "2026-10-13 1942 - A Main - P1 Dave Smith.mp3");

        Assert.Equal(clip, DriversOwnClip.FindFor(night, "Dave Smith"));
    }

    [Fact]
    public void Another_drivers_clip_is_not_theirs()
    {
        var night = Night();
        Clip(night, "2026-10-13 1942 - A Main - P1 Dave Smith.mp3");

        Assert.Null(DriversOwnClip.FindFor(night, "Jane Brown"));
    }

    [Fact]
    public void A_club_mate_whose_name_ends_the_same_way_does_not_get_their_clip()
    {
        // The one that matters at a real club. "Ends with the name" would
        // have handed Dave Smith the clip of Big Dave Smith - thirty
        // seconds of somebody else's race, played under Dave's name, with
        // nothing anywhere saying so.
        var night = Night();
        Clip(night, "2026-10-13 1942 - A Main - P4 Big Dave Smith.mp3");

        Assert.Null(DriversOwnClip.FindFor(night, "Dave Smith"));
    }

    [Fact]
    public void The_races_own_recording_is_never_taken_for_a_clip()
    {
        // The race's whole recording sits in the folder above with no
        // position in its name. Nothing in this folder should match it, but
        // a club that drags one in is the obvious accident.
        var night = Night();
        Clip(night, "2026-10-13 1942 - A Main.mp3");

        Assert.Null(DriversOwnClip.FindFor(night, "A Main"));
    }

    [Fact]
    public void A_race_whose_own_name_looks_like_a_position_does_not_confuse_the_match()
    {
        // A club that calls a race "P1 Shootout" writes a clip named
        // "... - P1 Shootout - P2 Dave Smith", so the shape appears twice
        // and only the last one is the driver's.
        var night = Night();
        var clip = Clip(night, "2026-10-13 1942 - P1 Shootout - P2 Dave Smith.mp3");

        Assert.Equal(clip, DriversOwnClip.FindFor(night, "Dave Smith"));
        Assert.Null(DriversOwnClip.FindFor(night, "Shootout"));
    }

    [Fact]
    public void A_double_figure_finish_is_matched_as_readily_as_a_single()
    {
        var night = Night();
        var clip = Clip(night, "2026-10-13 1942 - A Main - P12 Dave Smith.mp3");

        Assert.Equal(clip, DriversOwnClip.FindFor(night, "Dave Smith"));
    }

    [Fact]
    public void A_second_clip_of_the_same_name_is_still_theirs()
    {
        // Two heats of the same class inside the same minute: the second
        // clip gets " (2)" put on it by UnusedPath.
        var night = Night();
        var clip = Clip(night, "2026-10-13 1942 - Heat 1 - P1 Dave Smith (2).mp3");

        Assert.Equal(clip, DriversOwnClip.FindFor(night, "Dave Smith"));
    }

    [Fact]
    public void A_bracketed_number_on_the_end_of_a_name_is_read_as_a_copy_number()
    {
        // UnusedPath puts " (2)" on a clip that would have written over
        // another, and there is nothing in a file name to tell that apart
        // from a driver whose own name ends that way. So the copy number
        // wins: "Dave (2)" is matched as Dave, and a driver actually called
        // that is not found. Recorded here because it is a real limit of
        // matching on names, and the alternative is a second record of the
        // night - which is the thing RaceHighlights opens by refusing.
        Assert.True(DriversOwnClip.IsFor("x - P1 Dave (2).mp3", "Dave"));
        Assert.False(DriversOwnClip.IsFor("x - P1 Dave (2).mp3", "Dave (2)"));

        // Anything in the brackets that is not a number is part of the name.
        Assert.False(DriversOwnClip.IsFor("x - P1 Dave (ok).mp3", "Dave"));
        Assert.True(DriversOwnClip.IsFor("x - P1 Dave (ok).mp3", "Dave (ok)"));
    }

    [Fact]
    public void The_most_recent_clip_wins_when_a_driver_raced_more_than_once()
    {
        var night = Night();

        var heat = Clip(night, "2026-10-13 1900 - Heat 1 - P3 Dave Smith.mp3");
        var main = Clip(night, "2026-10-13 2010 - A Main - P1 Dave Smith.mp3");

        // Written in that order, so the main is the newer file. Set
        // explicitly rather than trusted to the clock, because two files
        // written in the same millisecond would make this a coin toss.
        File.SetLastWriteTimeUtc(heat, new DateTime(2026, 10, 13, 19, 5, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(main, new DateTime(2026, 10, 13, 20, 15, 0, DateTimeKind.Utc));

        Assert.Equal(main, DriversOwnClip.FindFor(night, "Dave Smith"));
    }

    [Fact]
    public void The_most_recent_wins_even_when_it_sorts_first()
    {
        // Nothing here may lean on the order the folder happens to list in,
        // or on the names sorting the way the night ran.
        var night = Night();

        var older = Clip(night, "2026-10-13 2010 - A Main - P1 Dave Smith.mp3");
        var newer = Clip(night, "2026-10-13 1900 - Heat 1 - P3 Dave Smith.mp3");

        File.SetLastWriteTimeUtc(older, new DateTime(2026, 10, 13, 19, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 10, 13, 21, 0, 0, DateTimeKind.Utc));

        Assert.Equal(newer, DriversOwnClip.FindFor(night, "Dave Smith"));
    }

    [Fact]
    public void A_clip_the_encoder_refused_is_still_played()
    {
        // On a machine without Media Foundation every clip of every night
        // is a wav and nothing else - the recorder leaves it and logs why.
        // Looking only for mp3s would have meant this feature silently did
        // nothing at all on exactly those machines.
        var night = Night();
        var clip = Clip(night, "2026-10-13 1942 - A Main - P1 Dave Smith.wav");

        Assert.Equal(clip, DriversOwnClip.FindFor(night, "Dave Smith"));
    }

    [Fact]
    public void Nothing_else_in_the_folder_is_offered_as_a_clip()
    {
        var night = Night();
        var folder = Path.Combine(night, DriversOwnClip.FolderName);

        File.WriteAllText(Path.Combine(folder, "2026-10-13 1942 - A Main - P1 Dave Smith.txt"), "notes");
        File.WriteAllText(Path.Combine(folder, "2026-10-13 1942 - A Main - P1 Dave Smith.mp3recovered"), "not one");

        Assert.Null(DriversOwnClip.FindFor(night, "Dave Smith"));
    }

    [Fact]
    public void A_name_with_a_slash_in_it_is_matched_the_way_the_file_was_written()
    {
        // SafePart turns every character Windows will not have into "_",
        // and the clip was named through it - so the lookup has to go
        // through it as well or a class like "2wd/4wd" never matches.
        var night = Night();
        var clip = Clip(night, "2026-10-13 1942 - A Main - P1 Dave_Smith.mp3");

        Assert.Equal(clip, DriversOwnClip.FindFor(night, "Dave/Smith"));
    }

    [Fact]
    public void A_name_differing_only_in_case_is_the_same_driver()
    {
        var night = Night();
        var clip = Clip(night, "2026-10-13 1942 - A Main - P1 DAVE SMITH.mp3");

        Assert.Equal(clip, DriversOwnClip.FindFor(night, "Dave Smith"));
    }

    [Fact]
    public void No_folder_no_driver_and_no_clips_all_come_back_with_nothing()
    {
        // Most of the time there is no clip, and that is not a fault: the
        // piece plays without one exactly as it did before this existed.
        var night = Night();

        Assert.Null(DriversOwnClip.FindFor(night, "Dave Smith"));
        Assert.Null(DriversOwnClip.FindFor(Path.Combine(_data.Folder, "no-such-night"), "Dave Smith"));
        Assert.Null(DriversOwnClip.FindFor(night, null));
        Assert.Null(DriversOwnClip.FindFor(night, "   "));
        Assert.Null(DriversOwnClip.FindFor(null, "Dave Smith"));
        Assert.Null(DriversOwnClip.FindFor("  ", "Dave Smith"));
    }

    // --- How much of it gets played ----------------------------------------

    [Fact]
    public void A_long_clip_is_cut_to_the_budget()
    {
        // A clip may be up to RaceHighlights.MaxPerDriverMs - a hundred and
        // fifty seconds - and three of those is seven and a half minutes of
        // audio wrapped round a minute of words.
        var excerpt = new DriversOwnClip.Excerpt(Loaded(Tone(60)));

        Assert.Equal((int)(DriversOwnClip.MostSeconds * Rate), Drain(excerpt));
    }

    [Fact]
    public void A_short_clip_is_played_whole()
    {
        var excerpt = new DriversOwnClip.Excerpt(Loaded(Tone(6)));

        Assert.Equal(6 * Rate, Drain(excerpt));
    }

    [Fact]
    public void A_clip_exactly_the_length_of_the_budget_is_played_whole()
    {
        var excerpt = new DriversOwnClip.Excerpt(Loaded(Tone(DriversOwnClip.MostSeconds)));

        Assert.Equal((int)(DriversOwnClip.MostSeconds * Rate), Drain(excerpt));
    }

    [Fact]
    public void The_budget_is_counted_in_frames_and_not_in_samples()
    {
        // A stereo clip holds two samples per frame. A budget counted in
        // samples would play half as long - and worse, a budget that landed
        // half a frame in would put the channels out of step for the rest
        // of it.
        var excerpt = new DriversOwnClip.Excerpt(Loaded(Stereo(Tone(60)), channels: 2), mostSeconds: 4);

        var samples = Drain(excerpt);

        Assert.Equal(4 * Rate * 2, samples);
        Assert.Equal(0, samples % 2);
    }

    [Fact]
    public void A_clip_is_handed_to_the_bus_in_its_own_format()
    {
        // Resampling and up-mixing is AudioBus.PlayAsync's job on the way
        // in. Claiming the mix format here would have the bus convert from
        // a rate the samples are not at.
        var clip = Loaded(Stereo(Tone(3)), channels: 2);
        var excerpt = new DriversOwnClip.Excerpt(clip);

        Assert.Equal(Rate, excerpt.WaveFormat.SampleRate);
        Assert.Equal(2, excerpt.WaveFormat.Channels);
    }

    [Fact]
    public void A_drained_excerpt_answers_nothing_rather_than_starting_again()
    {
        var excerpt = new DriversOwnClip.Excerpt(Loaded(Tone(2)));

        Drain(excerpt);

        Assert.Equal(0, excerpt.Read(new float[64], 0, 64));
    }

    [Fact]
    public void A_read_asking_for_more_than_is_left_gets_what_is_left()
    {
        var excerpt = new DriversOwnClip.Excerpt(Loaded(Tone(1)));
        var buffer = new float[4 * Rate];

        Assert.Equal(Rate, excerpt.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, excerpt.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void A_read_fills_from_the_offset_it_was_given()
    {
        // The bus reads into the middle of its own buffer, and a provider
        // that ignored the offset would write over what was already there.
        var excerpt = new DriversOwnClip.Excerpt(Loaded(Tone(1)));
        var buffer = new float[16];

        buffer[0] = 0.25f;

        var read = excerpt.Read(buffer, 1, 4);

        Assert.Equal(4, read);
        Assert.Equal(0.25f, buffer[0]);
        Assert.Equal(0f, buffer[5]);
    }

    [Fact]
    public void A_clip_with_nothing_in_it_is_not_something_to_play()
    {
        // ShowClip.Load refuses an empty file, so this cannot arrive from
        // the real path - but a budget of nothing can, and a provider that
        // answered a negative count would take the whole mix down.
        var excerpt = new DriversOwnClip.Excerpt(Loaded(Tone(2)), mostSeconds: 0);

        Assert.Equal(0, excerpt.Read(new float[64], 0, 64));
    }

    [Fact]
    public void A_negative_budget_is_treated_as_none()
    {
        var excerpt = new DriversOwnClip.Excerpt(Loaded(Tone(2)), mostSeconds: -5);

        Assert.Equal(0, excerpt.Read(new float[64], 0, 64));
    }

    [Fact]
    public void The_clip_plays_at_the_openers_level_and_not_under_anything()
    {
        // This is the real sound of the room rather than production music,
        // so it is not ducked. Asserted because a level above unity would
        // be clamped by the bus and a level near nothing would be the
        // feature silently not working.
        // Through a local: both sides being compile-time constants is what
        // xUnit2000 is about.
        var level = DriversOwnClip.Level;

        Assert.Equal(0.9f, level);
        Assert.InRange(level, 0.5f, 1f);
    }

    [Fact]
    public void The_budget_is_well_inside_what_a_clip_may_be()
    {
        // If these ever met, every clip would be played whole and the
        // excerpt would be doing nothing.
        var budgetMs = DriversOwnClip.MostSeconds * 1000;

        Assert.True(budgetMs < RaceHighlights.MaxPerDriverMs);
    }

    // --- Scaffolding -------------------------------------------------------

    /// <summary>A night's folder with the Highlights folder inside it, empty.</summary>
    private string Night()
    {
        var night = Path.Combine(_data.Folder, "2026-10-13");

        Directory.CreateDirectory(Path.Combine(night, DriversOwnClip.FolderName));

        return night;
    }

    /// <summary>A one-second clip in the night's Highlights folder, under the given name.</summary>
    private static string Clip(string night, string name) =>
        Wav(Path.Combine(night, DriversOwnClip.FolderName, name), Tone(1));

    private ShowClip Loaded(float[] samples, int channels = 1)
    {
        var path = Wav(
            Path.Combine(_data.Folder, "loaded", Guid.NewGuid().ToString("N") + ".wav"),
            samples,
            channels);

        try
        {
            var clip = ShowClip.Load(path, out var problem);

            Assert.Null(problem);

            return clip!;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static float[] Tone(double seconds)
    {
        var samples = new float[(int)(seconds * Rate)];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(0.4 * Math.Sin(2 * Math.PI * 220 * i / Rate));
        }

        return samples;
    }

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

    /// <summary>
    /// Writes a 16-bit PCM wav, by hand rather than through NAudio - the
    /// same reason ShowAudioTests gives: a wav header is nine lines, and a
    /// test that writes its own file cannot be confused by what a writer
    /// decided to put in one.
    /// </summary>
    private static string Wav(string path, float[] samples, int channels = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

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

    /// <summary>Everything the excerpt will hand over, in samples.</summary>
    private static int Drain(DriversOwnClip.Excerpt excerpt)
    {
        var buffer = new float[Rate / 4];
        var total = 0;

        while (true)
        {
            var read = excerpt.Read(buffer, 0, buffer.Length);

            if (read <= 0)
            {
                return total;
            }

            total += read;
        }
    }
}
