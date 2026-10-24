namespace HeatCaller.Tests;

/// <summary>
/// The pass recorder (23 Oct 2026, timing layer, P0): what it writes, that a
/// recorded race plays back to the same result, and that it is off unless
/// asked for.
/// </summary>
public sealed class TimingLayer_P0_RecorderTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static (ReplayResult Live, List<string> Lines) RecordedRun(ReplayScript script, Action<RaceControlSession> setup)
    {
        var lines = new List<string>();
        var recorder = new TimingRecorder(lines.Add, clockMs: 0, utc: ReplayClock.DefaultEpoch);
        var live = ReplayHarness.Run(ReplayDelivery.OnTime(script.Inputs, 200_000), setup, recorder: recorder);
        return (live, lines);
    }

    [Fact]
    public void Recording_is_off_unless_the_setting_is_ticked()
    {
        Assert.False(new AppSettings().RecordTimingPasses);
        Assert.Null(new RaceControlSession().Recorder);
    }

    [Theory]
    [InlineData("alternates")]
    [InlineData("jump")]
    [InlineData("red")]
    [InlineData("timelimit")]
    [InlineData("staggered")]
    [InlineData("warnings")]
    [InlineData("undo")]
    public void A_recorded_race_plays_back_to_the_same_result(string race)
    {
        var (script, setup) = TimingLayer_P0_FreezeTests.Golden(race);
        var (live, lines) = RecordedRun(script, setup);

        // Played from the file alone: the rules come from its Config lines and
        // the alternates from its passes, not from the setup.
        var (inputs, clock, recordedSetup) = ReplayHarness.FromRecording(TimingRecorder.ReadAll(lines));
        var replayed = ReplayHarness.Run(ReplayDelivery.OnTime(inputs, 200_000), clock: clock, setupWithContext: recordedSetup);

        Assert.Empty(replayed.Skipped);
        Assert.Equal(live.Describe(), replayed.Describe());
    }

    [Fact]
    public void A_pass_is_recorded_with_its_decoder_time_its_arrival_and_whose_spare_it_is()
    {
        var (_, lines) = RecordedRun(TimingLayer_P0_GoldenTests.AlternatesHeat(), TimingLayer_P0_GoldenTests.AlternatesRules);
        var records = TimingRecorder.ReadAll(lines);

        var header = records[0];
        Assert.Equal(TimingRecord.HeaderKind, header.Kind);
        Assert.Equal(0, header.Seq);
        Assert.Equal(TimingRecord.CurrentVersion, header.Version);

        // 200's first crossing at 21,000, reaching the app at 21,003.
        var pass = records.First(r => r.Kind == TimingRecord.PassKind && r.Transponder == "200");
        Assert.Equal((long)ReplayScript.DecoderTime(21_000), pass.DeviceTimeMs);
        Assert.Equal(21_003, pass.ReceivedMs);
        Assert.Equal(21_003, pass.ClockMs);
        Assert.Equal("100", pass.AlternateOf);
        Assert.Equal(DecoderConnection.EasyLap, pass.Decoder);
        Assert.False(pass.Synthetic ?? true);
        Assert.Equal(ReplayClock.DefaultEpoch.AddMilliseconds(21_003), pass.Utc);

        // A main transponder is nobody's spare, and the line says nothing.
        var main = records.First(r => r.Kind == TimingRecord.PassKind && r.Transponder == "300");
        Assert.Null(main.AlternateOf);
        Assert.DoesNotContain("alternateOf", lines.First(l => l.Contains("\"transponder\":\"300\"", StringComparison.Ordinal)), StringComparison.Ordinal);

        // Numbered in order, one line each.
        Assert.Equal(Enumerable.Range(1, records.Count - 1).Select(i => (long)i), records.Skip(1).Select(r => r.Seq));
    }

    [Fact]
    public void The_commands_are_recorded_in_order_with_the_rules_and_the_green_s_decoder_time()
    {
        var (_, lines) = RecordedRun(TimingLayer_P0_GoldenTests.RedFlagHeat(), TimingLayer_P0_GoldenTests.FiveLaps);
        var commands = TimingRecorder.ReadAll(lines).Where(r => r.Kind == TimingRecord.CommandKind).ToList();

        Assert.Equal(
            new[] { "Stage", "Config", "Countdown", "Config", "Green", "Red", "Resume" },
            commands.Select(c => c.Command));

        var config = commands.First(c => c.Command == "Config").Config!;
        Assert.Equal("5", config["LapLimit"]);
        Assert.Equal(string.Empty, config["TimeLimitMs"]);

        var green = commands.Single(c => c.Command == "Green");
        Assert.Equal((long)ReplayScript.DecoderTime(TimingLayer_P0_GoldenTests.Green), green.DeviceTimeMs);
        Assert.Equal(TimingLayer_P0_GoldenTests.Green, green.ClockMs);
        Assert.Equal("Countdown", green.State);

        var red = commands.Single(c => c.Command == "Red");
        Assert.Equal("Racing", red.State);
        Assert.Equal("Green", red.Flag);
    }

    [Fact]
    public void Release_all_and_an_undo_after_a_gap_are_recorded_once_each()
    {
        var lines = new List<string>();
        var race = new RaceControlSession
        {
            StartType = RaceStartType.HandReleased,
            Recorder = new TimingRecorder(lines.Add, clockMs: 0, utc: ReplayClock.DefaultEpoch),
        };
        var clock = new ReplayClock();
        clock.Bind(race);

        race.SetGrid(new[] { "100", "300" });
        race.Stage();
        race.StartCountdown();
        clock.AdvanceTo(20_000);
        race.GreenFlag(ReplayScript.DecoderTime(20_000));
        clock.AdvanceTo(22_000);
        race.ReleaseAll();

        var snapshot = race.CreateSnapshot();
        var takenUtc = clock.UtcNow;
        race.Reset();
        clock.AdvanceTo(30_000);
        race.RestoreSnapshotAfterGap(snapshot, takenUtc);

        var commands = TimingRecorder.ReadAll(lines).Where(r => r.Kind == TimingRecord.CommandKind).Select(r => r.Command).ToList();

        Assert.Single(commands, c => c == "ReleaseAll");
        Assert.DoesNotContain("Release", commands);
        Assert.Single(commands, c => c == "RestoreAfterGap");
        Assert.DoesNotContain("Restore", commands);

        var records = TimingRecorder.ReadAll(lines);
        var snapshotLine = records.Single(r => r.Command == "Snapshot");
        var restoreLine = records.Single(r => r.Command == "RestoreAfterGap");
        Assert.Equal(snapshotLine.Seq, restoreLine.SnapshotId);
        Assert.Equal(takenUtc, restoreLine.TakenUtc);
    }

    [Fact]
    public void Lines_from_many_threads_come_out_whole_and_numbered_once_each()
    {
        var lines = new List<string>();
        var recorder = new TimingRecorder(line =>
        {
            // The sink is called under the recorder's lock, so a plain list is safe.
            lines.Add(line);
        });

        Parallel.For(0, 2_000, i =>
            recorder.Pass(new DirectLapEvent { TransponderId = $"T{i % 7}", DeviceTimeMs = (uint)i }, null, i, ReplayClock.DefaultEpoch));

        var records = TimingRecorder.ReadAll(lines);
        Assert.Equal(2_001, records.Count);
        Assert.Equal(Enumerable.Range(0, 2_001).Select(i => (long)i), records.Select(r => r.Seq));
    }

    [Fact]
    public void A_line_cut_short_is_skipped_and_the_rest_still_reads()
    {
        var records = TimingRecorder.ReadAll(new[]
        {
            "{\"kind\":\"header\",\"seq\":0,\"clockMs\":5,\"utc\":\"2026-10-23T23:00:00Z\",\"version\":1}",
            "{\"kind\":\"pass\",\"seq\":1,\"clockMs\":10,\"utc\":\"2026-10-23T23:00:00.005Z\",\"transponder\":\"100\",\"deviceTimeMs\":7",
            "",
            "{\"kind\":\"command\",\"seq\":2,\"clockMs\":12,\"utc\":\"2026-10-23T23:00:00.007Z\",\"command\":\"Red\"}",
        });

        Assert.Equal(new[] { "header", "command" }, records.Select(r => r.Kind));
        Assert.Equal("Red", records[1].Command);
    }

    [Fact]
    public void A_recorder_that_throws_never_stops_a_lap_being_counted()
    {
        var race = new RaceControlSession { Recorder = new TimingRecorder(_ => throw new IOException("disk gone")) };
        var clock = new ReplayClock();
        clock.Bind(race);

        race.Stage();
        race.StartCountdown();
        clock.AdvanceTo(20_000);
        race.GreenFlag(ReplayScript.DecoderTime(20_000));
        clock.AdvanceTo(21_003);
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = "100", DeviceTimeMs = ReplayScript.DecoderTime(21_000), ReceivedMs = 21_003 });

        Assert.Equal(1, race.GetOrderedStandings().Single().Laps);
    }

    [Fact]
    public void The_file_recorder_writes_one_line_per_record_under_this_pc_s_data()
    {
        var folder = Path.Combine(AppPaths.LocalRoot, "timing-recordings");
        var recorder = TimingRecorder.StartFile(folder);

        Assert.NotNull(recorder);
        Assert.StartsWith(folder, recorder!.FilePath!, StringComparison.OrdinalIgnoreCase);

        recorder.Pass(new DirectLapEvent { TransponderId = "100", DeviceTimeMs = 7, ReceivedMs = 5 }, null, 10, ReplayClock.DefaultEpoch);
        recorder.Command(new TimingRecord { Command = "Red" }, 12, ReplayClock.DefaultEpoch);
        recorder.Dispose();

        // The writer is on its own thread; give it a moment to close the file.
        List<TimingRecord> records = new();

        for (var wait = 0; wait < 50 && records.Count < 3; wait++)
        {
            Thread.Sleep(100);

            try
            {
                using var stream = new FileStream(recorder.FilePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                records = TimingRecorder.ReadAll(reader.ReadToEnd().Split('\n'));
            }
            catch (IOException)
            {
                // Still being written.
            }
        }

        Assert.Equal(new[] { "header", "pass", "command" }, records.Select(r => r.Kind));
        Assert.Equal(5, records[1].ReceivedMs);
    }
}
