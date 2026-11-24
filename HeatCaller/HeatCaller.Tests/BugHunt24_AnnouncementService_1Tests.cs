using System.Collections.Concurrent;
using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the announcer:
/// the junior note said "Said once" after every cloud-voice line; history
/// and milestone calls showed numbers the PC's regional way; a position
/// report's correction in a shared heat named the car that was PASSED; a
/// split gap over a second was said to the half and shown to the tenth; and
/// a streamed line was cut off 30 seconds after it was asked for, however
/// much of it was still arriving.
/// </summary>
public sealed class BugHunt24_AnnouncementService_1Tests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown, ConcurrentQueue<string> Log) QuietAnnouncer(
        Action<ProfileStore>? drivers = null)
    {
        var profiles = ProfileStore.Load();
        drivers?.Invoke(profiles);

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds);

        var shown = new ConcurrentQueue<string>();
        var log = new ConcurrentQueue<string>();
        announcer.AnnouncementTextReady += shown.Enqueue;
        announcer.StatusMessage += log.Enqueue;

        return (announcer, shown, log);
    }

    // --- #87: the junior note is said once ---------------------------------

    [Fact]
    public void The_junior_note_is_said_once_even_after_a_cloud_voice_answers()
    {
        var (announcer, _, log) = QuietAnnouncer();

        announcer.NoteJuniorLine(null);
        announcer.CloudVoiceAnsweredForTest();
        announcer.NoteJuniorLine(null);
        announcer.CloudVoiceAnsweredForTest();
        announcer.NoteJuniorLine(null);

        Assert.Single(log, line => line.Contains("Lines that name a junior are said by the Kokoro voice", StringComparison.Ordinal));
    }

    [Fact]
    public void A_lost_junior_line_is_said_once_for_each_reason()
    {
        var (announcer, _, log) = QuietAnnouncer();

        announcer.NoteJuniorLine("its files are missing");
        announcer.CloudVoiceAnsweredForTest();
        announcer.NoteJuniorLine("its files are missing");
        announcer.NoteJuniorLine("it is still loading");

        Assert.Equal(2, log.Count(line => line.Contains("A line naming a junior wasn't said", StringComparison.Ordinal)));
        Assert.Single(log, line => line.Contains("its files are missing", StringComparison.Ordinal));
    }

    // --- #84: shown numbers do not follow the PC's region --------------------

    [Fact]
    public void The_margin_and_the_milestone_are_shown_the_same_on_a_decimal_comma_PC()
    {
        var before = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("0.213s", AnnouncementService.MarginShown(0.213));
            Assert.Equal("1,000th lap", AnnouncementService.MilestoneShown(1000, "lap"));
            Assert.Equal("50th race", AnnouncementService.MilestoneShown(50, "race"));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_history_calls_use_the_fixed_formats()
    {
        var history = Source("AnnouncementService.DriverHistory.cs");

        Assert.DoesNotContain("{margin:0.000}", history, StringComparison.Ordinal);
        Assert.DoesNotContain(":N0}", history, StringComparison.Ordinal);
        Assert.Contains("MarginShown(margin)", history, StringComparison.Ordinal);
        Assert.Contains("MilestoneShown(laps, \"lap\")", history, StringComparison.Ordinal);
        Assert.Contains("MilestoneShown(race, \"race\")", history, StringComparison.Ordinal);
    }

    // --- #90: a passed car with no class is not the one that got through ----

    private static readonly Dictionary<string, string> ModAndStock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["102"] = "Mod",
        ["101"] = "Stock",
        ["103"] = "Mod",
        ["104"] = "Stock",
    };

    [Fact]
    public void A_car_with_no_class_that_was_passed_is_not_the_mover()
    {
        // Sam (Stock) past the car with no class: the change is found at
        // that car, third now, but Sam - second on the road - came through.
        string[] read = ["102", "900", "101"];
        string[] now = ["102", "101", "900"];

        var place = AnnouncementService.FirstChangedClassPlace(read, now, ModAndStock);

        Assert.Equal(2, place);
        Assert.Equal(1, AnnouncementService.PasserOfAnUnclassedCar(read, now, ModAndStock, place));

        // And off the top of the board.
        Assert.Equal(1, AnnouncementService.FirstChangedClassPlace(["900", "102"], ["102", "900"], ModAndStock));
        Assert.Equal(0, AnnouncementService.PasserOfAnUnclassedCar(["900", "102"], ["102", "900"], ModAndStock, 1));

        // Two cars of other classes swapping higher up are not the passer:
        // Frank (Mod) and Sam (Stock) swap at the top while Bea (Stock) gets
        // past the car with no class.
        string[] readFour = ["103", "101", "900", "104"];
        string[] nowFour = ["101", "103", "104", "900"];

        var placeFour = AnnouncementService.FirstChangedClassPlace(readFour, nowFour, ModAndStock);

        Assert.Equal(3, placeFour);
        Assert.Equal(2, AnnouncementService.PasserOfAnUnclassedCar(readFour, nowFour, ModAndStock, placeFour));
    }

    [Fact]
    public void A_car_that_moved_up_is_still_the_mover()
    {
        // The car with no class got through itself.
        Assert.Equal(-1, AnnouncementService.PasserOfAnUnclassedCar(["102", "900", "101"], ["900", "102", "101"], ModAndStock, 0));

        // A car with a class, passing in its class.
        Assert.Equal(-1, AnnouncementService.PasserOfAnUnclassedCar(["102", "101", "103", "104"], ["102", "104", "101", "103"], ModAndStock, 1));

        // A car new to the report, and a place past the end of the order.
        Assert.Equal(-1, AnnouncementService.PasserOfAnUnclassedCar(["102", "101", "103"], ["102", "101", "900"], ModAndStock, 2));
        Assert.Equal(-1, AnnouncementService.PasserOfAnUnclassedCar(["102", "101", "103"], ["102", "101"], ModAndStock, 2));
    }

    private static RaceStanding Car(string transponder, int laps, long elapsedMs) => new()
    {
        Transponder = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Status = "active",
        Id = transponder,
    };

    [Fact]
    public void The_correction_does_not_credit_the_car_that_was_passed()
    {
        var (announcer, shown, log) = QuietAnnouncer(profiles =>
        {
            profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });
            profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Bill" });
            profiles.Upsert(new DriverProfile { Transponder = "900", DriverName = "Gus" });
        });

        announcer.SharedHeatClasses = () => ModAndStock;

        // Bill (Mod), Gus (no class) and Sam (Stock) on lap 4 in that order,
        // and the report reading all three.
        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("900", 4, 40_200));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));
        announcer.PutPositionReportOnAirForTest(["102", "900", "101"], 3);

        // Bill crosses, then Sam before Gus: Sam still leads Stock, and Gus
        // is third on the road where the report said second.
        announcer.OnStandingUpdated(Car("102", 5, 50_000));
        announcer.OnStandingUpdated(Car("101", 5, 50_300));
        announcer.Muted = true;

        Assert.Contains(log, line => line.StartsWith("[voice] Position report ", StringComparison.Ordinal)
            && line.Contains("Sam got past Gus", StringComparison.Ordinal));
        Assert.DoesNotContain(log, line => line.Contains("Gus got through", StringComparison.Ordinal));
        Assert.DoesNotContain(shown, line => line.Contains("Gus is through", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "Hold on - so it's Bill leads Mod, Sam leads Stock, Gus third." },
            shown.ToList());
    }

    // --- #92: a split gap is said as it is shown ----------------------------

    [Theory]
    [InlineData(40, "a tenth", "0.1s")]
    [InlineData(200, "two tenths", "0.2s")]
    [InlineData(500, "half a second", "0.5s")]
    [InlineData(1000, "a second", "1.0s")]
    [InlineData(1200, "one point two", "1.2s")]
    [InlineData(1250, "one point three", "1.3s")]
    [InlineData(1300, "one point three", "1.3s")]
    [InlineData(1500, "a second and a half", "1.5s")]
    [InlineData(-1400, "one point four", "1.4s")]
    public void Up_at_the_split_is_said_to_the_tenth_it_is_shown_to(long upMs, string said, string shownAs)
    {
        var gap = AnnouncementService.SplitExtras("the split", upMs)["gap"];

        Assert.Equal(said, gap.Item1);
        Assert.Equal(shownAs, gap.Item2);
    }

    // --- #94, #95: a stream still arriving is not cut off by the clock ------

    /// <summary>A body that sends a few pieces a while apart, then ends or goes quiet.</summary>
    private sealed class TrickleStream(int pieces, int pieceBytes, TimeSpan apart, bool thenGoesQuiet) : Stream
    {
        private int _sent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent >= pieces)
            {
                if (thenGoesQuiet)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                return 0;
            }

            await Task.Delay(apart, cancellationToken);
            _sent++;
            buffer.Span[..pieceBytes].Fill(1);

            return pieceBytes;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_stream_still_arriving_is_read_to_its_end_however_long_it_takes()
    {
        // A second of audio in ten pieces, and a limit of 600 ms: longer in
        // all than the limit, never that long between two pieces.
        var body = new TrickleStream(pieces: 10, pieceBytes: 100, TimeSpan.FromMilliseconds(100), thenGoesQuiet: false);
        var arrived = 0;

        await AnnouncementService.ReadWhileItArrivesAsync(
            _ => Task.FromResult<Stream>(body),
            (_, read) => arrived += read,
            TimeSpan.FromMilliseconds(600),
            CancellationToken.None);

        Assert.Equal(1000, arrived);
    }

    [Fact]
    public async Task A_stream_that_goes_quiet_is_given_up_on_and_says_why()
    {
        var body = new TrickleStream(pieces: 2, pieceBytes: 100, TimeSpan.FromMilliseconds(10), thenGoesQuiet: true);
        var arrived = 0;

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => AnnouncementService.ReadWhileItArrivesAsync(
            _ => Task.FromResult<Stream>(body),
            (_, read) => arrived += read,
            TimeSpan.FromSeconds(1),
            CancellationToken.None));

        Assert.Equal(200, arrived);
        Assert.Contains("nothing came", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stream_cut_off_by_the_announcer_is_a_cut_not_a_time_out()
    {
        var body = new TrickleStream(pieces: 1, pieceBytes: 100, TimeSpan.FromMilliseconds(10), thenGoesQuiet: true);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnnouncementService.ReadWhileItArrivesAsync(
            _ => Task.FromResult<Stream>(body),
            (_, _) => { },
            TimeSpan.FromSeconds(30),
            stop.Token));
    }

    [Fact]
    public void The_download_is_not_stopped_by_the_limit_for_opening_the_stream()
    {
        var streaming = Source("AnnouncementService.Streaming.cs");

        Assert.DoesNotContain("CreateLinkedTokenSource(timeout.Token)", streaming, StringComparison.Ordinal);
        Assert.Contains("using var stopDownload = new CancellationTokenSource();", streaming, StringComparison.Ordinal);
        Assert.Contains("StreamQuietLimit,", streaming, StringComparison.Ordinal);
    }

    private static string Source(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
        {
            folder = folder.Parent;
        }

        Assert.True(folder is not null, "No folder with check.cmd in it above " + AppContext.BaseDirectory);

        return File.ReadAllText(Path.Combine(folder!.FullName, "HeatCaller", "HeatCaller", fileName));
    }
}
