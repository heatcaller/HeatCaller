using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// Recording the race, and cutting each driver their own clip of it.
///
/// Nothing here opens a sound device or encodes anything: AudioBus.Silent is
/// on for the whole run (TestSetup), and an mp3 needs Windows Media
/// Foundation, which is not something a test run should depend on. What is
/// tested is everything that can be wrong without a speaker - the timing
/// arithmetic, where each clip starts and stops, the names, the off switch,
/// the disk floor, and the file a recording cut short leaves behind - plus
/// the one claim the whole feature rests on: that the moments come out of
/// the record the app already keeps, and come out the same after a crash.
///
/// The recorder is fed by hand through Mixed, which is exactly what AudioBus
/// does to it on a real night, so the file-writing half runs for real here
/// with no device anywhere near it.
/// </summary>
public sealed class RaceAudioTests : IDisposable
{
    private readonly TempAppData _data = new();

    /// <summary>
    /// Recording is off for the whole test run (TestSetup.NoRaceRecordings,
    /// so that no other test writes a wav into the real Documents folder) -
    /// and this is the one class that has to have it on, because it is the
    /// class that tests it. Turned on here and off again in Dispose, which
    /// is safe because the suite runs one test at a time (TestSetup's
    /// DisableTestParallelization).
    ///
    /// Every test below points the store's folder at _data before it records
    /// anything, so the files land in %TEMP% with the rest of the run.
    /// RaceAudioStore.Load is what the flag guards, and these tests call it,
    /// which is how six of them failed the first time this flag existed.
    /// </summary>
    public RaceAudioTests() => RaceAudioStore.OffForTests = false;

    public void Dispose()
    {
        RaceAudioStore.OffForTests = true;
        _data.Dispose();
    }

    // --- The moments come out of the record the app already keeps -----------

    [Fact]
    public void Each_crossing_is_the_running_total_of_the_laps_before_it()
    {
        var crossings = RaceHighlights.CrossingTimesMs(new long[] { 5000, 4000, 3500, 4200 });

        Assert.Equal(new long[] { 5000, 9000, 12500, 16700 }, crossings);
    }

    [Fact]
    public void A_staggered_cars_release_shifts_its_whole_timeline()
    {
        // The release offset is how long after the green this driver was let
        // go, so every one of their crossings is that much later.
        var crossings = RaceHighlights.CrossingTimesMs(new long[] { 5000, 4000 }, offsetMs: 30_000);

        Assert.Equal(new long[] { 35_000, 39_000 }, crossings);
    }

    [Fact]
    public void A_staggered_grids_moments_come_from_each_drivers_own_release()
    {
        // Bob was let go thirty seconds after the green. His lap times run
        // from his own release, so every moment of his is thirty seconds
        // later in the recording than the lap times alone would say - and
        // without this his clip would be cut from the wrong half-minute.
        var record = RaceOf(
            ("A", 1, "Ann", new long[] { 10_000, 9_000, 9_500 }),
            ("B", 2, "Bob", new long[] { 10_000, 9_200, 9_500 }));

        var released = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["B"] = 30_000,
        };

        var marks = RaceHighlights.MarksFrom(record, released);

        // Ann went at the green: her best lap is her second, ending 19s in.
        Assert.Contains(marks, mark =>
            mark.Transponder == "A" && mark.What == RaceHighlights.Moment.FastestLap && mark.RaceTimeMs == 19_000);

        // Bob's best is his second too, which reads 19.2s from his own
        // release and really happened 49.2s after the green.
        Assert.Contains(marks, mark =>
            mark.Transponder == "B" && mark.What == RaceHighlights.Moment.FastestLap && mark.RaceTimeMs == 49_200);
    }

    [Fact]
    public void A_driver_with_no_laps_has_no_moments_and_does_not_throw()
    {
        Assert.Empty(RaceHighlights.CrossingTimesMs(new List<long>()));
        Assert.Empty(RaceHighlights.CrossingTimesMs(null));
        Assert.Equal(-1, RaceHighlights.BestLapIndex(null));
    }

    [Fact]
    public void The_first_counted_pass_is_never_the_best_lap()
    {
        // The session's own rule: the first crossing runs from the green to
        // the line, which on a tight grid is a second or two, and letting it
        // win made a 1.4s dash the fastest lap of the race. The fastest-lap
        // moment has to skip it for the same reason.
        var best = RaceHighlights.BestLapIndex(new long[] { 1400, 9000, 8800 });

        Assert.Equal(2, best);
    }

    [Fact]
    public void A_driver_who_only_crossed_once_has_no_timed_lap_to_point_at()
    {
        Assert.Equal(-1, RaceHighlights.BestLapIndex(new long[] { 1400 }));
    }

    [Fact]
    public void The_crossing_times_are_the_same_laps_the_crash_journal_keeps()
    {
        // This is the claim the highlights stand on: the moments are not a
        // second account of the race written alongside it, they are the lap
        // times the app already records - the same list in the live
        // standing, in the crash journal, and in the saved result. So a race
        // recovered after a crash cuts to exactly the same moments.
        var race = new RaceControlSession();

        // Two guards in ProcessLapEvent have to be threaded between here,
        // and between them they leave a narrow window a test can work in.
        //
        // The clock check: the session compares time since the green on the
        // decoder's clock against the wall clock and, more than five seconds
        // apart, decides the decoder is wrong and corrects the start
        // reference ("Start reference corrected"). A test does all of this
        // inside one instant of wall time, so the LAST crossing has to be
        // under five seconds after the green - ten-second laps had the first
        // lap come back as 1ms.
        //
        // The minimum lap time: a pass less than MinLapTimeMs after the
        // previous one is thrown out as a double read, and it defaults to
        // three seconds - so one-second laps left a single lap on the board.
        //
        // Lowered here, which is what a club with a short indoor track does
        // anyway, so 1.2s laps count and all three crossings still land
        // inside the clock check's tolerance.
        race.MinLapTimeMs = 500;

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);

        Seen(race, "A", 101_200);
        Seen(race, "A", 102_400);
        Seen(race, "A", 103_600);

        var live = race.GetOrderedStandings().Single();
        Assert.Equal(new long[] { 1_200, 1_200, 1_200 }, live.LapTimesMs);

        // Each crossing, as ms after the green - which is what the decoder
        // clock says it was: 101_200 - 100_000 and so on.
        Assert.Equal(new long[] { 1_200, 2_400, 3_600 }, RaceHighlights.CrossingTimesMs(live.LapTimesMs));

        var journal = new RaceJournal();
        journal.Record(new RaceTiming(race), DateTime.UtcNow, string.Empty, force: true);

        var entry = RaceJournal.Unfinished();
        Assert.NotNull(entry);

        var saved = entry!.Standings.Single();
        Assert.Equal(live.LapTimesMs, saved.LapTimesMs);
        Assert.Equal(new long[] { 1_200, 2_400, 3_600 }, RaceHighlights.CrossingTimesMs(saved.LapTimesMs));

        // And the other way round the same answer, from the two decoder-clock
        // readings the journal also keeps: the last counted crossing minus
        // the green.
        Assert.Equal(100_000L, entry.RaceStartDeviceTimeMs!.Value);
        Assert.Equal(3_600L, saved.LastLapDeviceTimeMs - entry.RaceStartDeviceTimeMs!.Value);
    }

    [Fact]
    public void Taking_a_place_on_a_crossing_is_a_moment_for_the_driver_who_took_it()
    {
        // A leads from the first lap on ten-second laps. B is a second
        // behind at the end of lap one, puts in an eight-second lap, and is
        // ahead when they next cross - on lap two, at 19 seconds.
        var record = RaceOf(
            ("A", 1, "Ann", new long[] { 10_000, 10_000, 10_000 }),
            ("B", 2, "Bob", new long[] { 11_000, 8_000, 10_000 }));

        var marks = RaceHighlights.MarksFrom(record);

        Assert.Contains(marks, mark =>
            mark.Transponder == "B" && mark.What == RaceHighlights.Moment.Pass && mark.RaceTimeMs == 19_000);

        Assert.Contains(marks, mark =>
            mark.Transponder == "B" && mark.What == RaceHighlights.Moment.Lead && mark.RaceTimeMs == 19_000);

        // Being first across on lap one is the grid doing what the grid
        // does, and gets no call.
        Assert.DoesNotContain(marks, mark =>
            mark.Transponder == "A" && mark.What == RaceHighlights.Moment.Lead);
    }

    [Fact]
    public void A_warning_is_already_timestamped_so_it_is_taken_as_it_stands()
    {
        var record = RaceOf(("A", 1, "Ann", new long[] { 10_000, 10_000 }));
        record.Results[0].Warnings.Add(new RaceWarning { RaceTimeMs = 14_500, Number = 1, Reason = "Blocking" });

        // Zero means "before the green" on a RaceWarning, which is not a
        // moment in the recording.
        record.Results[0].Warnings.Add(new RaceWarning { RaceTimeMs = 0, Number = 2, Reason = "Rough driving" });

        var warnings = RaceHighlights.MarksFrom(record)
            .Where(mark => mark.What == RaceHighlights.Moment.Warning)
            .ToList();

        Assert.Single(warnings);
        Assert.Equal(14_500L, warnings[0].RaceTimeMs);
    }

    [Fact]
    public void Only_the_top_three_get_the_end_of_the_broadcast()
    {
        var record = RaceOf(
            ("A", 1, "Ann", new long[] { 10_000, 10_000 }),
            ("B", 2, "Bob", new long[] { 11_000, 10_000 }),
            ("C", 3, "Cal", new long[] { 12_000, 10_000 }),
            ("D", 4, "Dee", new long[] { 13_000, 10_000 }));

        var podium = RaceHighlights.MarksFrom(record)
            .Where(mark => mark.What == RaceHighlights.Moment.Podium)
            .Select(mark => mark.Transponder)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "A", "B", "C" }, podium);

        // From the flag, which is the last car to be counted: D at 23s.
        Assert.Equal(23_000L, RaceHighlights.FlagAtMsFrom(record));
    }

    // --- Where each clip starts and stops -----------------------------------

    [Fact]
    public void A_moment_lands_where_the_green_is_in_the_recording()
    {
        // The recording starts at the countdown, so the green is six seconds
        // into the file and everything in race time is six seconds later in
        // it.
        var record = RaceOf(("A", 1, "Ann", new long[] { 5_000, 9_000, 8_000 }));

        var marks = new[] { new RaceHighlights.Mark("A", RaceHighlights.Moment.FastestLap, 22_000) };
        var clips = RaceHighlights.ClipsFor(record, marks, greenAtMs: 6_000, lengthMs: 300_000);

        var clip = Assert.Single(clips);
        var window = Assert.Single(clip.Windows);

        Assert.Equal(6_000 + 22_000 - RaceHighlights.LeadInMs, window.StartMs);
        Assert.Equal(6_000 + 22_000 + RaceHighlights.TailMs, window.EndMs);
    }

    [Fact]
    public void A_clip_never_asks_for_audio_that_is_not_in_the_file()
    {
        // The race was cut short - the app went down, or the disk filled -
        // so the recording is forty seconds long and the moment is near the
        // end of it. Nothing may run past the end, and nothing before zero.
        var record = RaceOf(("A", 1, "Ann", new long[] { 5_000 }));

        var marks = new[] { new RaceHighlights.Mark("A", RaceHighlights.Moment.FastestLap, 2_000) };
        var clips = RaceHighlights.ClipsFor(record, marks, greenAtMs: 1_000, lengthMs: 9_000);

        var window = Assert.Single(Assert.Single(clips).Windows);

        Assert.Equal(0L, window.StartMs);
        Assert.Equal(9_000L, window.EndMs);
    }

    [Fact]
    public void Nothing_is_cut_at_all_from_a_recording_that_is_not_there()
    {
        var record = RaceOf(("A", 1, "Ann", new long[] { 5_000, 9_000 }));
        var marks = RaceHighlights.MarksFrom(record);

        Assert.Empty(RaceHighlights.ClipsFor(record, marks, greenAtMs: 0, lengthMs: 0));
    }

    [Fact]
    public void Two_moments_a_few_seconds_apart_become_one_stretch()
    {
        var merged = RaceHighlights.Merge(
            new[]
            {
                new RaceHighlights.Window(10_000, 28_000),
                new RaceHighlights.Window(20_000, 38_000),
            },
            RaceHighlights.JoinGapMs);

        var window = Assert.Single(merged);
        Assert.Equal(10_000L, window.StartMs);
        Assert.Equal(38_000L, window.EndMs);
    }

    [Fact]
    public void A_stretch_inside_another_one_does_not_shorten_it()
    {
        var merged = RaceHighlights.Merge(
            new[]
            {
                new RaceHighlights.Window(10_000, 60_000),
                new RaceHighlights.Window(20_000, 30_000),
            },
            RaceHighlights.JoinGapMs);

        var window = Assert.Single(merged);
        Assert.Equal(60_000L, window.EndMs);
    }

    [Fact]
    public void Moments_minutes_apart_stay_as_separate_stretches()
    {
        var merged = RaceHighlights.Merge(
            new[]
            {
                new RaceHighlights.Window(10_000, 28_000),
                new RaceHighlights.Window(200_000, 218_000),
            },
            RaceHighlights.JoinGapMs);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void A_driver_who_passed_somebody_every_lap_does_not_get_the_whole_race_back()
    {
        // Whole stretches are dropped from the back rather than every
        // stretch being trimmed: half a call is worse than not having it.
        var kept = RaceHighlights.WithinBudget(
            new[]
            {
                new RaceHighlights.Window(0, 10_000),
                new RaceHighlights.Window(20_000, 30_000),
                new RaceHighlights.Window(40_000, 50_000),
            },
            budgetMs: 25_000);

        Assert.Equal(2, kept.Count);
        Assert.Equal(20_000L, kept[0].LengthMs + kept[1].LengthMs);
    }

    [Fact]
    public void One_very_long_stretch_is_trimmed_rather_than_dropped_outright()
    {
        var kept = RaceHighlights.WithinBudget(
            new[] { new RaceHighlights.Window(0, 400_000) },
            budgetMs: 25_000);

        var window = Assert.Single(kept);
        Assert.Equal(25_000L, window.LengthMs);
    }

    [Fact]
    public void A_fragment_with_no_call_in_it_is_not_written_at_all()
    {
        // One second of recording, and the moment is in it - but a one-second
        // clip is a fragment, not a highlight.
        var record = RaceOf(("A", 1, "Ann", new long[] { 500 }));
        var marks = new[] { new RaceHighlights.Mark("A", RaceHighlights.Moment.FastestLap, 400) };

        Assert.Empty(RaceHighlights.ClipsFor(record, marks, greenAtMs: 0, lengthMs: 1_000));
    }

    [Fact]
    public void A_driver_the_announcer_never_named_gets_no_clip()
    {
        var record = RaceOf(
            ("A", 1, "Ann", new long[] { 10_000, 9_000 }),
            ("B", 2, "Bob", new long[] { 11_000, 9_500 }));

        var marks = new[] { new RaceHighlights.Mark("A", RaceHighlights.Moment.FastestLap, 19_000) };
        var clips = RaceHighlights.ClipsFor(record, marks, greenAtMs: 0, lengthMs: 300_000);

        Assert.Equal("Ann", Assert.Single(clips).DriverName);
    }

    // --- The names on the files ---------------------------------------------

    [Fact]
    public void A_file_from_three_weeks_ago_says_when_it_is_from_and_what_it_was()
    {
        var name = RaceHighlights.BaseNameFor(new DateTime(2026, 10, 10, 19, 42, 0), "A Main - 2wd Stock");

        Assert.Equal("2026-10-10 1942 - A Main - 2wd Stock", name);
    }

    [Fact]
    public void A_class_name_with_a_slash_in_it_does_not_make_a_folder()
    {
        var name = RaceHighlights.BaseNameFor(new DateTime(2026, 10, 10, 19, 42, 0), "Heat 1 - 2wd/4wd");

        Assert.Equal("2026-10-10 1942 - Heat 1 - 2wd_4wd", name);
        Assert.DoesNotContain("/", name, StringComparison.Ordinal);
    }

    [Fact]
    public void A_race_nobody_named_still_gets_a_name()
    {
        Assert.Equal(
            "2026-10-10 1942 - Race",
            RaceHighlights.BaseNameFor(new DateTime(2026, 10, 10, 19, 42, 0), "   "));
    }

    [Fact]
    public void A_drivers_clip_says_where_they_finished_and_who_they_are()
    {
        Assert.Equal(
            "2026-10-10 1942 - A Main - P1 Dave Smith",
            RaceHighlights.ClipNameFor("2026-10-10 1942 - A Main", 1, "Dave Smith"));
    }

    [Fact]
    public void A_second_race_of_the_same_name_in_the_same_minute_does_not_write_over_the_first()
    {
        var folder = Path.Combine(_data.Folder, "names");
        Directory.CreateDirectory(folder);

        var first = RaceHighlights.UnusedPath(folder, "2026-10-10 1942 - Heat 1", ".mp3");
        File.WriteAllText(first, "not really an mp3");

        var second = RaceHighlights.UnusedPath(folder, "2026-10-10 1942 - Heat 1", ".mp3");

        Assert.NotEqual(first, second);
        Assert.EndsWith("2026-10-10 1942 - Heat 1 (2).mp3", second, StringComparison.Ordinal);
    }

    // --- The arithmetic the cutting stands on -------------------------------

    [Fact]
    public void A_moment_is_never_cut_half_way_through_a_sample()
    {
        // A byte offset in the middle of a sample plays the rest of the file
        // as noise, so the offset is always rounded down to a whole one.
        var at = RaceHighlights.ByteOffsetFor(3, 44_100, bytesPerSample: 4);

        Assert.Equal(528L, at);
        Assert.Equal(0L, at % 4);
        Assert.Equal(0L, RaceHighlights.ByteOffsetFor(-5, 44_100, 4));
    }

    [Fact]
    public void A_file_in_step_with_the_clock_is_left_alone()
    {
        Assert.Equal(0L, RaceAudioRecorder.SilenceSamplesNeeded(1_000, 44_100, 44_100, slackMs: 400));
    }

    [Fact]
    public void A_little_behind_the_clock_is_not_worth_catching_up()
    {
        // Three hundred milliseconds adrift is one buffer of latency, not a
        // sound device that went away.
        Assert.Equal(0L, RaceAudioRecorder.SilenceSamplesNeeded(1_300, 44_100, 44_100, slackMs: 400));
    }

    [Fact]
    public void A_second_of_audio_missing_is_caught_up_with_silence()
    {
        // The speaker was switched off for a second mid-race. Without the
        // catch-up every highlight after it would be cut a second early.
        Assert.Equal(44_100L, RaceAudioRecorder.SilenceSamplesNeeded(2_000, 44_100, 44_100, slackMs: 400));
    }

    // --- The off switch, and the rails --------------------------------------

    [Fact]
    public void Nothing_is_recorded_when_the_club_has_turned_it_off()
    {
        File.WriteAllText(_data.PathOf("race-audio.json"), "{\"Enabled\":false}");

        var store = RaceAudioStore.Load();
        store.FolderSetting = Path.Combine(_data.Folder, "audio");

        Assert.False(store.Enabled);

        var said = new List<string>();
        var recorder = RaceAudioRecorder.StartFor(store, "Heat 1", DateTime.UtcNow, said.Add);

        Assert.Null(recorder);

        // Off is off, not off-with-an-explanation-every-race.
        Assert.Empty(said);
        Assert.False(Directory.Exists(Path.Combine(_data.Folder, "audio")));
    }

    [Fact]
    public void Recording_stays_off_once_it_has_been_turned_off()
    {
        var store = RaceAudioStore.Load();
        Assert.True(store.Enabled);

        store.Enabled = false;

        Assert.False(RaceAudioStore.Load().Enabled);
    }

    [Fact]
    public void A_disk_with_no_room_left_is_not_recorded_to()
    {
        // The race's own files - the crash journal and the race history -
        // need that disk far more than a recording does.
        File.WriteAllText(_data.PathOf("race-audio.json"), "{\"Enabled\":true,\"MinFreeMb\":1000000000}");

        var store = RaceAudioStore.Load();
        store.FolderSetting = Path.Combine(_data.Folder, "audio");

        var said = new List<string>();
        var recorder = RaceAudioRecorder.StartFor(store, "Heat 1", DateTime.UtcNow, said.Add);

        Assert.Null(recorder);
        Assert.Contains(said, line => line.Contains("NOT being recorded", StringComparison.Ordinal));
    }

    [Fact]
    public void The_free_disk_check_answers_yes_when_Windows_will_not_say()
    {
        // A recording must not be refused because the drive could not be
        // asked about - the rest of the app would carry on writing there.
        Assert.True(RaceAudioRecorder.HasRoom("\u0000:\\nowhere at all", 1));
        Assert.False(RaceAudioRecorder.HasRoom(_data.Folder, long.MaxValue));
    }

    [Fact]
    public void A_hand_edited_file_cannot_take_the_length_ceiling_off()
    {
        File.WriteAllText(_data.PathOf("race-audio.json"), "{\"MaxMinutes\":0,\"MinFreeMb\":-1}");

        var store = RaceAudioStore.Load();

        Assert.InRange(store.MaxMinutes, 1, 180);
        Assert.True(store.MinFreeMb >= 100);
    }

    // --- Where the files go -------------------------------------------------

    [Fact]
    public void The_recordings_never_land_in_the_folder_a_backup_zips()
    {
        // The fifth bug hunt found photo-finish frames being copied into
        // every one of fourteen kept backups until they filled the disk.
        // Race audio is several times the size, so it is kept outside the
        // data folder altogether rather than being left out of the zip.
        var store = RaceAudioStore.Load();

        Assert.False(PlainFileName.IsInside(AppPaths.Root, store.Folder));
        Assert.False(PlainFileName.IsInside(AppPaths.Root, store.FolderForNight(new DateTime(2026, 10, 10))));
    }

    [Fact]
    public void A_folder_on_another_machine_is_refused()
    {
        // Asking Windows whether \\somewhere\share exists makes it log on
        // with this account - see BackupService.SafeBackupFolder.
        var store = RaceAudioStore.Load();
        store.FolderSetting = @"\\somebody-elses-pc\share\audio";

        Assert.Equal(RaceAudioStore.DefaultFolder, store.Folder);

        store.FolderSetting = @"audio\tonight";
        Assert.Equal(RaceAudioStore.DefaultFolder, store.Folder);
    }

    [Fact]
    public void Each_night_gets_its_own_folder()
    {
        var store = RaceAudioStore.Load();
        store.FolderSetting = Path.Combine(_data.Folder, "audio");

        Assert.Equal(
            Path.Combine(_data.Folder, "audio", "2026-10-10"),
            store.FolderForNight(new DateTime(2026, 10, 10, 19, 42, 0)));
    }

    // --- The file a recording leaves behind ---------------------------------

    [Fact]
    public void What_was_recorded_is_a_playable_file_even_when_the_race_was_not_finished()
    {
        var store = RaceAudioStore.Load();
        store.FolderSetting = Path.Combine(_data.Folder, "audio");

        var startedUtc = DateTime.UtcNow;
        var said = new List<string>();
        var recorder = RaceAudioRecorder.StartFor(store, "Heat 1 - 2wd Stock", startedUtc, said.Add);

        Assert.NotNull(recorder);

        try
        {
            recorder!.NoteGreen(startedUtc.AddSeconds(6));

            // Two seconds of mix, handed over exactly as AudioBus hands it
            // over: one reused buffer, a tenth of a second of interleaved
            // stereo at a time.
            var mix = Tenth();

            for (var chunk = 0; chunk < 20; chunk++)
            {
                recorder.Mixed(mix, 0, mix.Length);
            }

            // The app being closed, or the window going, mid-race. Nothing
            // more is taken in from here.
            recorder.BeginStop();

            for (var chunk = 0; chunk < 20; chunk++)
            {
                recorder.Mixed(mix, 0, mix.Length);
            }

            var recording = recorder.Stop();

            Assert.Null(recording.Problem);
            Assert.Equal(0, recording.DroppedChunks);
            Assert.Equal(6_000L, recording.GreenAtMs);

            // The two seconds that were handed over, and not the twenty
            // chunks offered after the stop. A range rather than exactly two
            // seconds only because of the catch-up: on a machine so loaded
            // that feeding two seconds of audio took longer than that, the
            // file is padded to the wall clock on purpose (see
            // SilenceSamplesNeeded). Four seconds would mean the stop was
            // ignored, which is what this is really asking.
            Assert.InRange(recording.LengthMs, 2_000L, 2_500L);
            Assert.EndsWith(RaceAudioRecorder.InProgressExtension, recording.WavPath, StringComparison.Ordinal);

            // And it opens and plays as it stands, which is the whole point
            // of writing a wav while the race is on.
            using var reader = new WaveFileReader(recording.WavPath);

            Assert.Equal(RaceAudioRecorder.Channels, reader.WaveFormat.Channels);
            Assert.Equal(RaceAudioRecorder.SampleRate, reader.WaveFormat.SampleRate);
            Assert.InRange(reader.TotalTime.TotalSeconds, 1.95, 2.55);
        }
        finally
        {
            recorder?.Dispose();
        }
    }

    [Fact]
    public void A_piece_recorded_after_the_race_leaves_the_races_replay_alone()
    {
        // 6 Nov 2026: the night recap or a driver's piece, played after the
        // flag, used to wipe the ring and fill it with itself, so Replay
        // played the piece as "the last seconds of the race".
        var store = RaceAudioStore.Load();
        store.FolderSetting = Path.Combine(_data.Folder, "audio");

        var replay = RaceReplayBuffer.Shared;
        var race = RaceAudioRecorder.StartFor(store, "Heat 1", DateTime.UtcNow, _ => { }, isRace: true);

        Assert.NotNull(race);

        var mix = Tenth();

        try
        {
            for (var chunk = 0; chunk < 40; chunk++)
            {
                race!.Mixed(mix, 0, mix.Length);
            }

            race!.Stop();
        }
        finally
        {
            race?.Dispose();
        }

        var held = replay.HeldSeconds;
        var finish = replay.Last(RaceReplayBuffer.Seconds);

        Assert.True(held >= 3, "The race recording no longer feeds the replay.");

        var piece = RaceAudioRecorder.StartFor(store, "Night recap", DateTime.UtcNow, _ => { });

        Assert.NotNull(piece);

        try
        {
            var quiet = new float[mix.Length];

            for (var chunk = 0; chunk < 20; chunk++)
            {
                piece!.Mixed(quiet, 0, quiet.Length);
            }

            piece!.Stop();
        }
        finally
        {
            piece?.Dispose();
        }

        Assert.Equal(held, replay.HeldSeconds);
        Assert.Equal(finish, replay.Last(RaceReplayBuffer.Seconds));
    }

    [Fact]
    public void A_recording_in_progress_is_named_so_a_crash_can_be_told_from_a_finish()
    {
        var store = RaceAudioStore.Load();
        store.FolderSetting = Path.Combine(_data.Folder, "audio");

        var recorder = RaceAudioRecorder.StartFor(store, "Heat 1", new DateTime(2026, 10, 10, 18, 42, 0, DateTimeKind.Utc), _ => { });

        Assert.NotNull(recorder);

        try
        {
            var name = Path.GetFileName(recorder!.Stop().WavPath);

            // The date and time are local, so only the stable parts are
            // asserted here - the name itself is covered by BaseNameFor.
            Assert.StartsWith("2026-10-1", name, StringComparison.Ordinal);
            Assert.EndsWith("Heat 1" + RaceAudioRecorder.InProgressExtension, name, StringComparison.Ordinal);
        }
        finally
        {
            recorder?.Dispose();
        }
    }

    [Fact]
    public void The_header_is_rewritten_as_the_recording_runs_so_a_crash_leaves_a_whole_file()
    {
        // A wav's length lives in its header, and a file whose header still
        // says nothing plays as nothing. WaveFileWriter.Flush rewrites it,
        // so the file on disk is complete at every moment - which is what
        // makes a force-close or a power cut cost seconds rather than the
        // whole race. There is no way to kill a process from inside a test,
        // so the shape is pinned by reading the source, as the audio-lock
        // tests do for the same reason.
        var source = SourceOf("RaceAudioRecorder.cs");
        var flush = FlushSectionOf(source);

        Assert.Contains("_wav.Flush();", flush, StringComparison.Ordinal);
        Assert.Contains("PadForDrift();", flush, StringComparison.Ordinal);
    }

    [Fact]
    public void The_recording_is_taken_off_the_bus_before_anything_waits_on_a_disk()
    {
        // BeginStop runs on whatever thread the end of the broadcast lands
        // on, which is sometimes the UI thread. Everything that waits -
        // draining, closing, encoding - belongs to Stop and Publish, which
        // run on a background task.
        var source = SourceOf("RaceControlControl.AfterRace.cs");
        var finish = MethodBodyOf(source, "private void FinishRaceAudio()");

        Assert.Contains("recorder.BeginStop();", finish, StringComparison.Ordinal);
        Assert.Contains("RaceAudioRecorder.Publish(", finish, StringComparison.Ordinal);

        var tapOff = finish.IndexOf("recorder.BeginStop();", StringComparison.Ordinal);
        var background = finish.IndexOf("Task.Run(", StringComparison.Ordinal);

        Assert.True(
            tapOff >= 0 && background > tapOff,
            "The tap has to come off the bus before anything is handed to a thread that waits on a disk.");
    }

    [Fact]
    public void A_demo_a_practice_and_a_rehearsal_are_never_recorded()
    {
        // The same races the crash journal leaves alone, for its reasons: a
        // demo's laps are invented, practice has its own save, and a
        // rehearsal must never drop files among a club's real ones.
        var follow = MethodBodyOf(SourceOf("RaceControlControl.AfterRace.cs"), "private void FollowRaceAudio()");

        Assert.Contains("_race.IsDemoMode || _race.IsPracticeMode || AppPaths.IsRehearsal", follow, StringComparison.Ordinal);
    }

    // --- Helpers ------------------------------------------------------------

    /// <summary>A pass from the decoder itself - not synthetic, so the start checks see it.</summary>
    private static void Seen(RaceControlSession race, string car, uint deviceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = deviceTimeMs,
            SignalHits = 60,
            SignalStrength = 247,
        });

    /// <summary>A tenth of a second of the bus's mix: interleaved stereo floats at its own rate.</summary>
    private static float[] Tenth()
    {
        var frames = RaceAudioRecorder.SampleRate / 10;
        var buffer = new float[frames * AudioBus.MixChannels];

        for (var i = 0; i < buffer.Length; i++)
        {
            // Something other than silence, so a file of zeros could not
            // pass for a recording.
            buffer[i] = 0.25f * MathF.Sin(i * 0.05f);
        }

        return buffer;
    }

    /// <summary>A finished race as it was filed: finishing position, name and every counted lap.</summary>
    private static SessionRecord RaceOf(params (string Transponder, int Position, string Name, long[] Laps)[] drivers) =>
        new()
        {
            SessionType = RaceSessionType.Race,
            Label = "A Main - 2wd Stock",
            StartedAtUtc = new DateTime(2026, 10, 10, 18, 42, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 10, 10, 18, 50, 0, DateTimeKind.Utc),
            Results = drivers.Select(driver => new SessionResultEntry
            {
                Position = driver.Position,
                Transponder = driver.Transponder,
                DriverName = driver.Name,
                Laps = driver.Laps.Length,
                LapTimesMs = driver.Laps.ToList(),
            }).ToList(),
        };

    private static string SourceOf(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null)
        {
            var candidate = Path.Combine(folder.FullName, "HeatCaller", "HeatCaller", fileName);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            folder = folder.Parent;
        }

        throw new FileNotFoundException($"No HeatCaller\\HeatCaller\\{fileName} above {AppContext.BaseDirectory}.");
    }

    /// <summary>The text of FlushAndCheck, braces matched from the one that opens it.</summary>
    private static string FlushSectionOf(string source) =>
        MethodBodyOf(source, "private void FlushAndCheck()");

    /// <summary>
    /// The text of one method, braces matched from the one that opens it.
    ///
    /// Not a parser: it counts braces, which is enough for the methods it is
    /// pointed at because every brace in them is part of a matched pair, the
    /// interpolated strings' holes included. A method that stopped matching
    /// would fail the test rather than pass it quietly.
    /// </summary>
    private static string MethodBodyOf(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"\"{signature}\" is no longer in the source.");

        var open = source.IndexOf('{', at);
        Assert.True(open >= 0, $"\"{signature}\" has no body.");

        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return source[open..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"\"{signature}\" has no matching closing brace.");
    }
}
