namespace HeatCaller.Tests;

/// <summary>
/// The show treatment, the counters that silence the club's music, and the
/// recorded-line index (bug hunt 5, 9 Oct 2026).
///
/// A show whose start was cancelled during the opener - the next race called
/// to the grid over a recap's teaser - let go of the club's music but not of
/// the bed, which went on looping under the playlist for the rest of the
/// evening; ShowOnAir and AdBreak raised their events outside their own
/// locks, so a begin and an end on two threads could be reported in the
/// wrong order and leave the music muted all night; one ShowTreatment is
/// shared by the announcer's director and Race Control, and a second show
/// opening wrote over the first one's view of whether the announcer was
/// talking; an absolute path left in ShowOpenerPath or ShowBedPath was
/// opened as it stood, so a planted UNC path had Windows log on to another
/// machine as soon as the Commentary page was opened; and a recorded line
/// deleted while index.json could not be read came back the next time the
/// file read, pointing at audio that had really been deleted.
///
/// Nothing here plays anything - AudioBus is silent for the whole test run
/// (TestSetup) - and nothing here touches a real network path: the planted
/// ones are refused before anything looks at them, which is the point.
/// </summary>
public sealed class BugHunt5ShowAudioTests : IDisposable
{
    private readonly TempAppData _data = new();

    /// <summary>Low on purpose, as in ShowAudioTests: every piece below is seconds long, and this keeps the arrays small.</summary>
    private const int Rate = 8000;

    public BugHunt5ShowAudioTests()
    {
        ShowOnAir.ResetForTests();
        AdBreak.ResetForTests();
        ShowMusic.Reload();
    }

    public void Dispose()
    {
        // Both counters and the loaded audio are static, so a test that left
        // a show open would silence the music in every test after it.
        ShowOnAir.ResetForTests();
        AdBreak.ResetForTests();
        ShowMusic.Reload();
        _data.Dispose();
    }

    // --- A start that is cancelled partway through -----------------------------------------

    [Fact]
    public async Task A_cancelled_start_stops_the_bed_it_had_already_started()
    {
        // The token cancels inside the opener-alone wait, which is what
        // happens when the next race is called to the grid during a recap's
        // teaser. Nothing is handed back, so nothing will ever call
        // FinishAsync - and the bed LOOPS, so unless the start cuts it here
        // there is nothing left alive that can.
        var treatment = Treatment(withOpener: true);

        using var cts = new CancellationTokenSource();

        // StartAsync runs as far as its first await - the opener-alone wait
        // - before it hands a task back, so the bed is already playing by
        // the time this is cancelled.
        var starting = treatment.StartAsync(cts.Token);
        cts.Cancel();

        Assert.Null(await starting);

        var bed = treatment.LastBedForTests;
        Assert.NotNull(bed);

        var played = ReadToEnd(bed!, longest: 30 * Rate);

        Assert.True(
            bed!.Finished,
            $"the bed was still looping after {Seconds(played):0.0}s - a cancelled start has to cut it, or it plays under the club's music all night");

        // Cut short rather than wound up: there is no show to end.
        Assert.InRange(Seconds(played), 0.5, ShowBed.CutShortSeconds + 1.0);
    }

    [Fact]
    public async Task A_cancelled_start_still_gives_the_club_s_music_back()
    {
        var treatment = Treatment(withOpener: true);

        using var cts = new CancellationTokenSource();
        var starting = treatment.StartAsync(cts.Token);
        cts.Cancel();

        Assert.Null(await starting);
        Assert.False(ShowOnAir.Playing);
    }

    // --- Counting a show on air ------------------------------------------------------------

    [Fact]
    public void A_show_beginning_and_ending_at_once_cannot_leave_the_music_told_the_wrong_thing()
    {
        // The event used to be raised outside the lock, so a Begin on one
        // thread and an End on another could be counted in one order and
        // reported in the other. Whatever the LAST handler to come back
        // said is what the music player is left holding - and "playing"
        // means the club's playlist never comes back.
        var entered = new ManualResetEventSlim(false);
        var told = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var finished = new List<bool>();

        void Watch(bool playing)
        {
            if (playing)
            {
                // Held open, so the end below has to arrive in the middle of
                // this one.
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
            else
            {
                told.Set();
            }

            lock (finished)
            {
                finished.Add(playing);
            }
        }

        ShowOnAir.PlayingChanged += Watch;

        try
        {
            // A lambda rather than the method group: ShowOnAir.Begin took
            // an optional caption on 10 Oct 2026 (what the Big Screen puts on
            // the crowd's screen), and a method with an optional parameter
            // does not convert to Action - the delegate has to match the
            // signature, and C# makes no zero-argument overload for it.
            var begin = Task.Run(() => ShowOnAir.Begin());

            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "the handler was never told the show had started");

            var end = Task.Run(ShowOnAir.End);

            // Under the bug the end reports "not playing" straight away,
            // while the beginning is still reporting "playing". Given a
            // moment to do so, it does.
            told.Wait(TimeSpan.FromMilliseconds(250));

            release.Set();

            // xUnit1031 wants this awaited rather than waited on. The whole
            // point of this test is a particular interleaving of two pool
            // threads against a handler held open, and awaiting hands the
            // test thread's continuation to the scheduler in the middle of
            // the thing being measured. There is no synchronisation context
            // under xunit, so the deadlock the rule guards against cannot
            // happen here.
#pragma warning disable xUnit1031
            Assert.True(Task.WaitAll(new[] { begin, end }, TimeSpan.FromSeconds(10)), "a begin or an end never came back");
#pragma warning restore xUnit1031

        }
        finally
        {
            ShowOnAir.PlayingChanged -= Watch;
            release.Set();
        }

        Assert.False(ShowOnAir.Playing);

        lock (finished)
        {
            Assert.Equal(2, finished.Count);
            Assert.False(
                finished[^1],
                "the last thing the music player was told was that the show was still on - its playlist stays silenced for the rest of the night");
        }
    }

    [Fact]
    public void An_ad_break_beginning_and_ending_at_once_cannot_leave_the_music_muted()
    {
        // Identically, and for the same reason: two Big Screens each running
        // their own rotation is exactly how two threads come to count at
        // once.
        var entered = new ManualResetEventSlim(false);
        var told = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var finished = new List<bool>();

        void Watch(bool playing)
        {
            if (playing)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
            else
            {
                told.Set();
            }

            lock (finished)
            {
                finished.Add(playing);
            }
        }

        AdBreak.PlayingChanged += Watch;

        try
        {
            var begin = Task.Run(AdBreak.Begin);

            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "the handler was never told the break had started");

            var end = Task.Run(AdBreak.End);

            told.Wait(TimeSpan.FromMilliseconds(250));

            release.Set();

            // xUnit1031 wants this awaited rather than waited on. The whole
            // point of this test is a particular interleaving of two pool
            // threads against a handler held open, and awaiting hands the
            // test thread's continuation to the scheduler in the middle of
            // the thing being measured. There is no synchronisation context
            // under xunit, so the deadlock the rule guards against cannot
            // happen here.
#pragma warning disable xUnit1031
            Assert.True(Task.WaitAll(new[] { begin, end }, TimeSpan.FromSeconds(10)), "a begin or an end never came back");
#pragma warning restore xUnit1031

        }
        finally
        {
            AdBreak.PlayingChanged -= Watch;
            release.Set();
        }

        Assert.False(AdBreak.Playing);

        lock (finished)
        {
            Assert.Equal(2, finished.Count);
            Assert.False(finished[^1], "the last thing the music player was told was that the ad was still on");
        }
    }

    [Fact]
    public void A_handler_that_throws_does_not_take_the_show_down_with_it()
    {
        // MainForm's handler reaches into the music player, and a handler
        // that threw used to come straight back out of Begin - with the show
        // already counted on, and the exception landing on whoever was
        // staging the segment.
        static void Fall(bool playing) => throw new InvalidOperationException("the music player fell over");

        ShowOnAir.PlayingChanged += Fall;

        try
        {
            ShowOnAir.Begin();

            Assert.True(ShowOnAir.Playing);

            ShowOnAir.End();

            Assert.False(ShowOnAir.Playing);
        }
        finally
        {
            ShowOnAir.PlayingChanged -= Fall;
        }
    }

    // --- Whether the announcer is talking, per show ----------------------------------------

    [Fact]
    public async Task A_second_show_opening_does_not_clobber_the_first_one_s_view_of_the_announcer()
    {
        // One ShowTreatment is shared by the announcer screen's director and
        // Race Control. The speaking flag used to be a field on it, which
        // StartAsync wrote false into - so a second show opening told the
        // first one the announcer had stopped talking. SpeakingChanged only
        // fires on edges, so nothing put that right during a long line: the
        // first show's wait for quiet returned, and its bed wound up and
        // un-muted the playlist over the top of a line still being read.
        var treatment = Treatment(withOpener: false);

        var first = await treatment.StartAsync();
        Assert.NotNull(first);

        // The announcer has started a long line under the first show.
        first!.Speaking.Now = true;

        var second = await treatment.StartAsync();
        Assert.NotNull(second);

        Assert.NotSame(first.Speaking, second!.Speaking);

        Assert.True(
            first.Speaking.Now,
            "the second show opening told the first one the announcer had gone quiet - its bed winds up over a line still being read");

        Assert.False(second.Speaking.Now);

        await second.FinishAsync(cutShort: true);
        await first.FinishAsync(cutShort: true);

        Assert.False(ShowOnAir.Playing);
    }

    // --- Where the show's music is allowed to come from ------------------------------------

    [Fact]
    public void A_show_music_path_on_another_machine_is_treated_as_not_set()
    {
        // Merely checking such a path exists has Windows log on to that
        // server with this account - and ShowClip.Load calls File.Exists the
        // moment the Commentary page describes the file. Nothing in the app
        // ever writes a non-empty value here, so any is legacy or planted.
        var settings = new AppSettings
        {
            ShowOpenerPath = @"\\attacker\share\opener.wav",
            ShowBedPath = @"//attacker/share/bed.wav",
        };

        var music = new ShowMusic(settings);

        Assert.Null(music.OpenerPath);
        Assert.Null(music.BedPath);
    }

    [Fact]
    public void A_show_music_path_that_is_not_fully_qualified_is_treated_as_not_set()
    {
        // It would land wherever the app happens to be running from, which
        // for an installed build is Program Files.
        var settings = new AppSettings { ShowBedPath = @"..\..\Windows\Media\bed.wav" };

        Assert.Null(new ShowMusic(settings).BedPath);
    }

    [Fact]
    public void A_planted_path_falls_back_to_the_club_s_own_show_music_folder()
    {
        // "Not set" is the honest reading of a value the app cannot have
        // written, and not set means the file in the folder.
        var bed = ShowMusicFile("bed.wav", Music(10));

        var settings = new AppSettings { ShowBedPath = @"\\attacker\share\bed.wav" };

        Assert.Equal(bed, new ShowMusic(settings).BedPath);
    }

    [Fact]
    public void A_local_path_a_club_set_before_is_still_used()
    {
        // Older versions let a club point at a file where it sat, and a
        // mapped drive letter is a real setup. Only the shapes the app could
        // never have written are refused.
        var path = Path.Combine(_data.Folder, "chosen-long-ago.wav");

        var settings = new AppSettings { ShowBedPath = path };

        Assert.Equal(path, new ShowMusic(settings).BedPath);
    }

    // --- Recorded lines deleted while index.json could not be read -------------------------

    [Fact]
    public void A_line_deleted_while_the_index_was_unreadable_does_not_come_back()
    {
        const string line = "GREEN FLAG - WE ARE RACING";

        var folder = Path.Combine(AppPaths.Root, "VoiceLines");
        var indexPath = Path.Combine(folder, "index.json");

        // An earlier session's recording, in index.json and on disk.
        var earlier = StingerLibrary.ForTests(folder, TimeSpan.Zero);
        earlier.Save(line, new byte[] { 1, 2, 3 }, "Pit Lane", "Club");
        Assert.True(earlier.Has(line));

        var library = StingerLibrary.ForTests(folder, TimeSpan.Zero);

        // index.json held open by a backup or sync tool, which is the usual
        // cause. This session records the line again and then thinks better
        // of it - the take on disk is named after the line, so deleting it
        // deletes the one the file's own entry points at.
        using (new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            library.Save(line, new byte[] { 4, 5, 6 }, "Pit Lane", "Club");

            Assert.Equal(1, library.Delete(new[] { line }));
        }

        // The file reads again. The deletion used to be left out of the
        // merge, so the entry came back pointing at audio that was gone:
        // Has said the line was recorded and TryLoad found nothing, and the
        // only way out was to record it again.
        Assert.False(library.Has(line), "the deleted line came back when index.json read again");
        Assert.Equal(0, library.CountRecorded(new[] { line }));
        Assert.Null(library.TryLoad(line));

        Assert.DoesNotContain("WE ARE RACING", File.ReadAllText(indexPath));
    }

    [Fact]
    public void A_line_recorded_again_after_being_deleted_while_unreadable_keeps_its_new_take()
    {
        // The other way round, and why the merge takes the deletions out
        // BEFORE it puts the new takes in: a line deleted and then recorded
        // again while the file was held open must keep the take that is
        // actually on disk.
        const string line = "LIGHTS OUT";

        var folder = Path.Combine(AppPaths.Root, "VoiceLines");
        var indexPath = Path.Combine(folder, "index.json");

        var earlier = StingerLibrary.ForTests(folder, TimeSpan.Zero);
        earlier.Save(line, new byte[] { 1, 1, 1 }, "Pit Lane", "Club");

        var library = StingerLibrary.ForTests(folder, TimeSpan.Zero);

        using (new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            library.Save(line, new byte[] { 2, 2, 2 }, "Pit Lane", "Club");
            library.Delete(new[] { line });
            library.Save(line, new byte[] { 3, 3, 3 }, "Pit Lane", "Club");
        }

        Assert.True(library.Has(line));

        var back = library.TryLoad(line);

        Assert.NotNull(back);
        Assert.Equal(new byte[] { 3, 3, 3 }, back!);
    }

    // --- Staging a show for a test ---------------------------------------------------------

    /// <summary>
    /// A treatment with the club's music in place, on this test's own data
    /// folder. With an opener there is an opener-alone wait to cancel inside;
    /// without one, StartAsync comes straight back.
    /// </summary>
    private ShowTreatment Treatment(bool withOpener)
    {
        ShowMusicFile("bed.wav", Music(10));

        if (withOpener)
        {
            ShowMusicFile("opener.wav", Music(3));
        }

        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

        return new ShowTreatment(new AppSettings(), announcer);
    }

    private static double Seconds(int frames) => (double)frames / Rate;

    /// <summary>
    /// Music-shaped audio, as ShowAudioTests makes it: a tone with a little
    /// level variation, because a perfectly constant signal is not what the
    /// measurement meets.
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

    /// <summary>
    /// Writes a 16-bit PCM wav into the club's show music folder, by hand
    /// rather than through NAudio - a wav header is nine lines, and none of
    /// this then depends on an mp3 decoder.
    /// </summary>
    private string ShowMusicFile(string name, float[] samples)
    {
        Directory.CreateDirectory(ShowMusic.Folder);

        var path = Path.Combine(ShowMusic.Folder, name);

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

    /// <summary>
    /// Reads the bed until it runs short, or until <paramref name="longest"/>
    /// frames have come out - a bed still looping would never run short, and
    /// a test must fail rather than read for ever.
    /// </summary>
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
