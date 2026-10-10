namespace HeatCaller.Tests;

/// <summary>
/// The evening's running order: that no two shows in a night are the same
/// shape, and that every one of those shapes still gives the club's music
/// back.
///
/// WHAT IS ACTUALLY BEING TESTED. AudioBus is silent for the whole run
/// (TestSetup), so nothing here can be listened to. What can be checked is
/// the production's decisions - which cue a show is billed as, where in the
/// opener it starts, how far the bed is lifted at the end - and above all
/// the one thing that must never go wrong in this area: the counter that
/// silences the club's playlist comes back down on every single way out,
/// including the ways out nobody meant to take.
///
/// The pieces are written here as wav files, as the other show tests do it,
/// so each test says in its own body what shape of music it is about and
/// none of this depends on an mp3 decoder.
/// </summary>
public sealed class ShowRunningOrderTests : IDisposable
{
    private readonly TempAppData _data = new();

    /// <summary>Low on purpose, as in ShowAudioTests: every piece below is seconds long, and this keeps the arrays small.</summary>
    private const int Rate = 8000;

    public ShowRunningOrderTests()
    {
        ShowOnAir.ResetForTests();
        ShowMusic.Reload();
    }

    public void Dispose()
    {
        // Both static, so a test that left a show open would silence the
        // music in every test after it - and in a real app run started by
        // the layout test in the same process.
        ShowOnAir.ResetForTests();
        ShowMusic.Reload();
        _data.Dispose();
    }

    // --- Which show this is ----------------------------------------------------------------

    [Fact]
    public void The_first_show_of_the_night_gets_the_opener_whole()
    {
        var order = new ShowRunningOrder();

        var billing = order.Billing(null, At(19, 30), rejoinAllowed: true);

        Assert.Equal(ShowCue.TopOfTheNight, billing.Cue);
        Assert.False(billing.EnterOpenerLate);
    }

    [Fact]
    public void A_show_soon_after_another_comes_back_in_on_the_ring_out()
    {
        // The whole point of the feature. A club with race intros ticked
        // hears a show before every heat, and the full theme eleven times
        // in an evening stops being heard around the third one.
        var order = new ShowRunningOrder();

        order.Went(At(19, 30));

        var billing = order.Billing(null, At(19, 38), rejoinAllowed: true);

        Assert.Equal(ShowCue.Rejoin, billing.Cue);
        Assert.True(billing.EnterOpenerLate);
    }

    [Fact]
    public void A_show_after_a_long_quiet_spell_re_establishes_the_theme()
    {
        // A tea break, a battery charge, the second leg of a two-part
        // meeting. By then a re-entry sting lands on nothing, because the
        // room has had time to forget what it is re-entering.
        var order = new ShowRunningOrder();

        order.Went(At(19, 30));

        var billing = order.Billing(null, At(19, 30) + ShowRunningOrder.AfterABreak, rejoinAllowed: true);

        Assert.Equal(ShowCue.BackAfterABreak, billing.Cue);
        Assert.False(billing.EnterOpenerLate);
    }

    [Fact]
    public void The_gap_between_two_heats_is_not_a_break()
    {
        // The rule this must not get wrong in the other direction: a club
        // running back to back should hear rejoins all evening.
        var order = new ShowRunningOrder();

        order.Went(At(19, 30));

        var billing = order.Billing(null, At(19, 30) + ShowRunningOrder.AfterABreak - TimeSpan.FromSeconds(1), rejoinAllowed: true);

        Assert.Equal(ShowCue.Rejoin, billing.Cue);
    }

    [Fact]
    public void A_cold_open_is_the_top_of_the_night_whatever_went_before_it()
    {
        // Setting the levels with Play It is a show as far as the clock is
        // concerned. Letting those presses turn the real cold open into a
        // rejoin would cost a club the one full opener of the evening that
        // the whole room is waiting for.
        var order = new ShowRunningOrder();

        order.Went(At(19, 0));
        order.Went(At(19, 2));

        var billing = order.Billing(SegmentKind.ColdOpen, At(19, 5), rejoinAllowed: true);

        Assert.Equal(ShowCue.TopOfTheNight, billing.Cue);
        Assert.False(billing.EnterOpenerLate);
    }

    [Fact]
    public void The_sign_off_is_played_out_on_the_bed()
    {
        // The one show where the music finishing IS the point.
        var order = new ShowRunningOrder();

        order.Went(At(19, 30));

        var billing = order.Billing(SegmentKind.SignOffNumbers, At(21, 40), rejoinAllowed: true);

        Assert.Equal(ShowCue.Playout, billing.Cue);
        Assert.True(billing.PlayoutSeconds > 0, "the sign-off should hold the bed on after the last word");
        Assert.False(billing.EnterOpenerLate);
    }

    [Fact]
    public void The_sign_off_is_the_only_cue_that_holds_the_bed_on()
    {
        foreach (var cue in new[] { ShowCue.TopOfTheNight, ShowCue.Rejoin, ShowCue.BackAfterABreak })
        {
            Assert.True(
                ShowBilling.For(cue).PlayoutSeconds <= 0,
                $"{cue} should not hold the bed on after the last word - only the way out does that");
        }
    }

    [Fact]
    public void Every_cue_lifts_the_bed_at_the_end_of_the_read()
    {
        // The lift is the punctuation, not a flourish for one segment: a
        // show that ends by the bed simply running out sounds like the
        // music stopped, whichever show it was.
        foreach (var cue in new[] { ShowCue.TopOfTheNight, ShowCue.Rejoin, ShowCue.BackAfterABreak, ShowCue.Playout })
        {
            Assert.True(ShowBilling.For(cue).LiftSeconds > 0, $"{cue} should bring the bed up at the end");
        }
    }

    [Fact]
    public void A_rejoin_is_the_briskest_at_both_ends()
    {
        var rejoin = ShowBilling.For(ShowCue.Rejoin);
        var top = ShowBilling.For(ShowCue.TopOfTheNight);

        Assert.True(rejoin.AirSeconds < top.AirSeconds, "coming back from a break is not an occasion");
        Assert.True(rejoin.LiftSeconds < top.LiftSeconds);
        Assert.True(rejoin.BedAloneSeconds < top.BedAloneSeconds);
    }

    [Fact]
    public void Turning_the_shortening_off_still_varies_the_shape()
    {
        // The setting takes the shortened OPENER away and nothing else. A
        // club who want their jingle whole every time still get a show
        // that knows where in the evening it is.
        var order = new ShowRunningOrder();

        order.Went(At(19, 30));

        var billing = order.Billing(null, At(19, 38), rejoinAllowed: false);

        Assert.Equal(ShowCue.Rejoin, billing.Cue);
        Assert.False(billing.EnterOpenerLate);
        Assert.True(
            billing.AirSeconds < ShowBilling.For(ShowCue.TopOfTheNight).AirSeconds,
            "a rejoin should still be the brisker show, whatever the club asked of the opener");
    }

    [Fact]
    public void A_new_night_forgets_what_went_out_last_night()
    {
        var order = new ShowRunningOrder();

        order.Went(At(19, 30));

        Assert.Equal(1, order.ShowsTonight);

        order.NewNight();

        Assert.Equal(0, order.ShowsTonight);
        Assert.Equal(ShowCue.TopOfTheNight, order.Billing(null, At(19, 31), rejoinAllowed: true).Cue);
    }

    [Fact]
    public void A_clock_that_goes_backwards_reads_as_no_time_passing()
    {
        // An NTP correction mid-evening. The conservative way round: an
        // extra rejoin is a shorter opener, where an extra full open is
        // the theme playing twice in two minutes.
        var order = new ShowRunningOrder();

        order.Went(At(20, 0));

        Assert.Equal(ShowCue.Rejoin, order.Billing(null, At(19, 0), rejoinAllowed: true).Cue);
    }

    // --- Where a later show comes in on the opener ------------------------------------------

    [Fact]
    public void A_late_entry_lands_where_the_opener_stops_hitting()
    {
        // The same point the voice comes in on a full open, which is the
        // whole trick: the back of the piece is the part the room
        // recognises, because it is the part they heard the announcer over.
        var clip = Clip("opener.wav", Join(Music(8), FadeOut(Music(2))));

        var from = ShowBilling.EntersLateAt(clip);

        Assert.InRange(Seconds(from), 7.8, 8.2);
        Assert.Equal(from, ShowBilling.For(ShowCue.Rejoin).OpenerFrom(clip));
    }

    [Fact]
    public void An_opener_with_too_little_ring_out_is_played_whole()
    {
        // A club whose opener is a one-second stab. Coming in late on that
        // is not a shorter theme, it is a hit with the hit cut off - and
        // there was nothing to shorten in the first place.
        var clip = Clip("stab.wav", Music(1.2));

        Assert.Equal(0, ShowBilling.EntersLateAt(clip));
        Assert.Equal(0, ShowBilling.For(ShowCue.Rejoin).OpenerFrom(clip));
    }

    [Fact]
    public void The_full_open_always_starts_at_the_top()
    {
        var clip = Clip("opener.wav", Join(Music(8), FadeOut(Music(2))));

        foreach (var cue in new[] { ShowCue.TopOfTheNight, ShowCue.BackAfterABreak, ShowCue.Playout })
        {
            Assert.Equal(0, ShowBilling.For(cue).OpenerFrom(clip));
        }
    }

    // --- The opener's own provider ----------------------------------------------------------

    [Fact]
    public void A_one_shot_can_start_partway_in()
    {
        var clip = Clip("opener.wav", Music(3));
        var at = Rate;

        var opener = new ShowOneShot(clip, fromFrame: at);

        var buffer = new float[Rate];
        var read = opener.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);

        for (var i = 0; i < read; i++)
        {
            Assert.Equal(clip.Samples[at + i], buffer[i], 4);
        }
    }

    [Fact]
    public void A_one_shot_can_carry_a_beat_of_silence_in_front()
    {
        // The club's music is silenced the instant a show opens, so
        // without this the playlist stops dead and the opener lands in the
        // same sample - which reads as the music cutting out.
        var clip = Clip("opener.wav", Music(2));
        var air = Rate / 4;

        var opener = new ShowOneShot(clip, airFrames: air);

        var buffer = new float[Rate];
        var read = opener.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);

        for (var i = 0; i < air; i++)
        {
            Assert.Equal(0f, buffer[i]);
        }

        // And then the piece, from its top.
        Assert.Equal(clip.Samples[0], buffer[air], 4);
        Assert.Equal(clip.Samples[1], buffer[air + 1], 4);
    }

    [Fact]
    public void The_air_counts_towards_how_long_the_opener_plays_for()
    {
        // It is written into the provider, so the clip is longer by it -
        // and AudioBus reads a short buffer as the end of a sound, so an
        // off-by-one here would cut the opener's last sample.
        var clip = Clip("opener.wav", Music(1));
        var air = Rate / 2;

        var opener = new ShowOneShot(clip, airFrames: air);

        var buffer = new float[4 * Rate];
        var read = opener.Read(buffer, 0, buffer.Length);

        Assert.Equal(air + clip.Samples.Length, read);
        Assert.Equal(0, opener.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void A_one_shot_with_nothing_asked_of_it_behaves_as_it_always_did()
    {
        var clip = Clip("opener.wav", Music(2));
        var opener = new ShowOneShot(clip);

        var buffer = new float[3 * Rate];

        Assert.Equal(clip.Samples.Length, opener.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, opener.Read(buffer, 0, buffer.Length));
    }

    // --- A real show, staged ----------------------------------------------------------------

    [Fact]
    public async Task The_second_show_of_an_evening_is_a_rejoin()
    {
        // End to end, through the treatment the announcer screen and Race
        // Control share - which is the only thing that proves the running
        // order is actually wired to the staging rather than merely
        // correct on its own.
        var treatment = Treatment(withOpener: true);

        var first = await treatment.StartAsync();

        Assert.NotNull(first);
        Assert.Equal(ShowCue.TopOfTheNight, treatment.LastBillingForTests?.Cue);

        await first!.FinishAsync(cutShort: true);

        var second = await treatment.StartAsync();

        Assert.NotNull(second);
        Assert.Equal(ShowCue.Rejoin, treatment.LastBillingForTests?.Cue);

        await second!.FinishAsync(cutShort: true);

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_cold_open_said_by_name_is_billed_as_the_top_of_the_night()
    {
        var treatment = Treatment(withOpener: true);

        var warmUp = await treatment.StartAsync();
        Assert.NotNull(warmUp);
        await warmUp!.FinishAsync(cutShort: true);

        var show = await treatment.StartAsync(SegmentKind.ColdOpen);

        Assert.NotNull(show);
        Assert.Equal(ShowCue.TopOfTheNight, treatment.LastBillingForTests?.Cue);

        await show!.FinishAsync(cutShort: true);

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_new_night_on_the_treatment_puts_the_full_opener_back()
    {
        var treatment = Treatment(withOpener: true);

        var first = await treatment.StartAsync();
        Assert.NotNull(first);
        await first!.FinishAsync(cutShort: true);

        treatment.NewNight();

        var next = await treatment.StartAsync();

        Assert.NotNull(next);
        Assert.Equal(ShowCue.TopOfTheNight, treatment.LastBillingForTests?.Cue);

        await next!.FinishAsync(cutShort: true);

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_club_who_want_the_whole_opener_every_time_get_it()
    {
        var treatment = Treatment(withOpener: true, rejoin: false);

        var first = await treatment.StartAsync();
        Assert.NotNull(first);
        await first!.FinishAsync(cutShort: true);

        var second = await treatment.StartAsync();

        Assert.NotNull(second);
        Assert.Equal(ShowCue.Rejoin, treatment.LastBillingForTests?.Cue);
        Assert.False(treatment.LastBillingForTests!.EnterOpenerLate);

        await second!.FinishAsync(cutShort: true);

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_cancelled_start_is_not_counted_as_a_show_that_went_out()
    {
        // The next race called to the grid during a recap's teaser. The
        // opener may never have been heard, so the evening is left where
        // it was: the next show plays the whole thing, which is never the
        // wrong answer.
        var treatment = Treatment(withOpener: true);

        using var cts = new CancellationTokenSource();

        var starting = treatment.StartAsync(cts.Token);
        cts.Cancel();

        Assert.Null(await starting);
        Assert.False(ShowOnAir.Playing);

        var next = await treatment.StartAsync();

        Assert.NotNull(next);
        Assert.Equal(ShowCue.TopOfTheNight, treatment.LastBillingForTests?.Cue);

        await next!.FinishAsync(cutShort: true);

        Assert.False(ShowOnAir.Playing);
    }

    // --- The lift, and the music coming back ------------------------------------------------

    [Fact]
    public async Task The_bed_is_brought_up_once_the_talking_stops()
    {
        var treatment = Treatment(withOpener: false);

        var show = await treatment.StartAsync();

        Assert.NotNull(show);

        var underTheRead = show!.BedTargetForTests;

        Assert.True(underTheRead > 0, "the bed should be up at its own level for the read");

        await show.FinishAsync();

        Assert.True(
            show.BedTargetForTests > underTheRead,
            $"the bed ended at {show.BedTargetForTests:0.000}, no higher than the {underTheRead:0.000} it read at - "
            + "a show that ends on the bed running out sounds like the music stopped");

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_show_cut_short_gets_no_lift()
    {
        // A race intro's bed has to be gone the moment the countdown
        // starts. A flourish in front of that fade is the last thing the
        // moment needs.
        var treatment = Treatment(withOpener: false);

        var show = await treatment.StartAsync();

        Assert.NotNull(show);

        var underTheRead = show!.BedTargetForTests;

        await show.FinishAsync(cutShort: true);

        Assert.Equal(underTheRead, show.BedTargetForTests, 3);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task The_lift_never_leaves_the_bed_looping()
    {
        // The fifth bug hunt's shape, and the reason the hold swallows its
        // own failures: the wind-up is the statement after it, and a bed
        // that is never wound up does not stop - it loops under the club's
        // music coming back for the rest of the evening.
        var treatment = Treatment(withOpener: false);

        var show = await treatment.StartAsync();

        Assert.NotNull(show);

        await show!.FinishAsync();

        var bed = treatment.LastBedForTests;

        Assert.NotNull(bed);

        var played = ReadToEnd(bed!, longest: 30 * Rate);

        Assert.True(
            bed!.Finished,
            $"the bed was still looping after {Seconds(played):0.0}s - the lift must not be able to skip the wind-up");

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task Every_way_a_show_can_end_gives_the_club_s_music_back()
    {
        // The one thing in this area that must never go wrong. A stuck
        // counter means the club's playlist never comes back for the rest
        // of the night, which is worse than anything else on this page.
        var treatment = Treatment(withOpener: true);

        // 1. The natural end, with the lift.
        var natural = await treatment.StartAsync();
        Assert.NotNull(natural);
        await natural!.FinishAsync();
        Assert.False(ShowOnAir.Playing);

        // 2. Cut short.
        var cut = await treatment.StartAsync();
        Assert.NotNull(cut);
        await cut!.FinishAsync(cutShort: true);
        Assert.False(ShowOnAir.Playing);

        // 3. Ended twice, which every caller does - once at the natural
        // end and once from its finally.
        var twice = await treatment.StartAsync();
        Assert.NotNull(twice);
        await twice!.FinishAsync();
        await twice.FinishAsync(cutShort: true);
        Assert.False(ShowOnAir.Playing);

        // 4. Cancelled on the way in, so nothing was ever handed back to
        // end - the start has to give the music back itself.
        using var cts = new CancellationTokenSource();
        var starting = treatment.StartAsync(cts.Token);
        cts.Cancel();
        Assert.Null(await starting);
        Assert.False(ShowOnAir.Playing);

        // 5. And a show whose read never finishes, ended out of a using -
        // the shape RunAsync's finally has.
        var held = await treatment.StartAsync();
        Assert.NotNull(held);

        try
        {
            throw new InvalidOperationException("the segment fell over mid-read");
        }
        catch (InvalidOperationException)
        {
            await held!.FinishAsync();
        }

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_run_built_without_a_billing_ends_exactly_as_it_always_did()
    {
        // The nine-argument constructor the earlier bug-hunt tests build,
        // and the shape every show had before the running order: straight
        // from the read into the ending, no lift, nothing held on.
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

        ShowOnAir.Begin();

        var run = new ShowRun(announcer, _ => { }, new ShowSpeaking(), null, null, Task.CompletedTask, TimeSpan.Zero, 0.22f, null);

        await run.FinishAsync();

        Assert.False(ShowOnAir.Playing);
        Assert.Equal(0f, run.BedTargetForTests, 3);
    }

    // --- What the page tells a club ---------------------------------------------------------

    [Fact]
    public void The_page_says_where_a_later_show_will_come_in()
    {
        // Measured off their file, so it is otherwise unknowable - the same
        // reason Describe exists.
        ShowMusicFile("opener.wav", Join(Music(8), FadeOut(Music(2))));

        var said = new ShowMusic(new AppSettings()).DescribeRunningOrder();

        Assert.Contains("later shows come in at", said);
    }

    [Fact]
    public void The_page_says_so_when_an_opener_is_too_short_to_shorten()
    {
        ShowMusicFile("opener.wav", Music(1.2));

        var said = new ShowMusic(new AppSettings()).DescribeRunningOrder();

        Assert.Contains("every show plays all of it", said);
    }

    [Fact]
    public void The_page_says_so_when_there_is_no_opener_to_vary()
    {
        ShowMusicFile("bed.wav", Music(10));

        var said = new ShowMusic(new AppSettings()).DescribeRunningOrder();

        Assert.Contains("no opener", said);
    }

    [Fact]
    public void The_page_has_nothing_to_say_with_no_music_in_at_all()
    {
        var said = new ShowMusic(new AppSettings()).DescribeRunningOrder();

        Assert.Contains("nothing to vary", said);
    }

    // --- Staging a show for a test ----------------------------------------------------------

    /// <summary>
    /// A treatment with the club's music in place, on this test's own data
    /// folder. The opener has a real fade on it, so there is a ring-out for
    /// a rejoin to come in on - a piece that does not fade is all body and
    /// would always be played whole.
    /// </summary>
    private ShowTreatment Treatment(bool withOpener, bool rejoin = true)
    {
        ShowMusicFile("bed.wav", Join(Music(8), FadeOut(Music(2))));

        if (withOpener)
        {
            // Eight seconds of body and a two-second fade, which is the
            // shape ShowAudioTests proves the measurement reads properly:
            // a body long enough to be believed (ShortestBodySeconds) and
            // a ring-out long enough to come in on. A shorter piece is all
            // body, so there would be nothing for a rejoin to enter.
            ShowMusicFile("opener.wav", Join(Music(8), FadeOut(Music(2))));
        }

        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

        return new ShowTreatment(
            new AppSettings
            {
                ShowRejoinEnabled = rejoin,

                // The entry point is SET here rather than measured, only so
                // these tests do not sit out the eight seconds the measured
                // one would be on a piece this shape. Where the voice comes
                // in is tested on its own above; what is being tested here
                // is which show the running order billed, and whether the
                // club got its music back.
                ShowVoiceEntersMs = 200,
            },
            announcer);
    }

    /// <summary>A time tonight, as the running order is handed them - UTC, because that is what it is given.</summary>
    private static DateTime At(int hour, int minute) => new(2026, 10, 10, hour, minute, 0, DateTimeKind.Utc);

    private static double Seconds(int frames) => (double)frames / Rate;

    /// <summary>
    /// Music-shaped audio, as the other show tests make it: a tone with a
    /// little level variation, because a perfectly constant signal is not
    /// what the measurement meets.
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

    private static float[] Join(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    private ShowClip Clip(string name, float[] samples)
    {
        var clip = ShowClip.Load(Wav(Path.Combine(AppPaths.Root, name), samples), out var problem);

        Assert.Null(problem);

        return clip!;
    }

    /// <summary>A piece in the club's own show music folder, where the treatment looks for it.</summary>
    private string ShowMusicFile(string name, float[] samples)
    {
        Directory.CreateDirectory(ShowMusic.Folder);

        return Wav(Path.Combine(ShowMusic.Folder, name), samples);
    }

    /// <summary>
    /// Writes a 16-bit PCM wav, by hand rather than through NAudio - a wav
    /// header is nine lines, and a test that writes its own file cannot be
    /// confused by what a writer decided to put in one.
    /// </summary>
    private static string Wav(string path, float[] samples)
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
        write.Write((short)1);
        write.Write(Rate);
        write.Write(Rate * 2);
        write.Write((short)2);
        write.Write((short)16);
        write.Write("data".ToCharArray());
        write.Write(bytes);

        foreach (var sample in samples)
        {
            write.Write((short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue));
        }

        return path;
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
