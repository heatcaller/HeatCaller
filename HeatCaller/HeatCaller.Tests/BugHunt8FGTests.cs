using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 8 (17 Oct 2026), group FG - timing, decoders, the race
/// programme, race day and results:
///
/// A01-1 a black-flagged car no longer holds the finish open for the whole
/// straggler grace. A01-2 undoing a Reset leaves a ghost's readings alone.
/// A01-3 the last car's window counts only cars that raced. A02-1 a hung
/// I-Lap close is forgotten after one timed-out connect. A02-4 a failed
/// transponder-health write is tried again. A24-1 a bump-up pairs a main
/// with the feeder that runs before it, not last week's. A24-3 Add a Main by
/// Hand offers the lowest free tier. A25-2 a day-schedule.json of "null" is
/// not written over. A25-3 MinutesLate is null when the running line has no
/// time. A26-1 suggested handicaps use only the class's races. A26-2 a
/// Unicode minus is read as a minus, and CSV numbers are invariant.
/// </summary>
public sealed class BugHunt8FGTests : IDisposable
{
    private const string Ghost = "GHOST-AAAAAA";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- A01-1: a black-flagged car is not waited on ----------------------------

    [Fact]
    public void A_black_flagged_car_does_not_hold_the_finish_open()
    {
        var race = Racing(lapLimit: 3);
        race.WarningLadder = new List<WarningStep> { new(WarningAction.BlackFlag) };

        Cross(race, "A", 5_000);
        Cross(race, "B", 6_000);
        Cross(race, "A", 15_000);
        Cross(race, "B", 16_000);

        Assert.Equal(WarningAction.BlackFlag, race.IssueWarning("B", "contact")!.Action);

        // A runs on to the limit and takes the flag; B has been told to come in.
        for (uint t = 25_000; t <= 95_000 && race.State == RaceState.Racing; t += 10_000)
        {
            Cross(race, "A", t);
        }

        Assert.Equal(RaceState.Finished, race.State);
    }

    [Fact]
    public void A_black_flag_after_the_last_other_car_is_in_finishes_the_race()
    {
        var race = Racing(lapLimit: 3);
        race.WarningLadder = new List<WarningStep> { new(WarningAction.BlackFlag) };

        Cross(race, "A", 5_000);
        Cross(race, "B", 6_000);

        for (uint t = 15_000; t <= 95_000 && !race.IsFinishing; t += 10_000)
        {
            Cross(race, "A", t);
        }

        Assert.True(race.IsFinishing);
        Assert.True(race.HasTakenFlag("A"));
        Assert.Equal(RaceState.Racing, race.State);

        race.IssueWarning("B", "ignored the black flag");

        Assert.Equal(RaceState.Finished, race.State);
    }

    // --- A01-2: a ghost's readings through an undone Reset ----------------------

    [Fact]
    public void Undoing_a_reset_moves_a_real_car_s_readings_but_not_a_ghost_s()
    {
        var race = Racing();
        Cross(race, Ghost, 5_000);
        Cross(race, "101", 6_000);
        Cross(race, Ghost, 15_000);
        Cross(race, "101", 16_000);

        var ghostBefore = Standing(race, Ghost);
        var realBefore = Standing(race, "101");
        var (ghostLap, ghostSeen) = (ghostBefore.LastLapDeviceTimeMs, ghostBefore.LastSeenDeviceTimeMs);
        var (realLap, realSeen) = (realBefore.LastLapDeviceTimeMs, realBefore.LastSeenDeviceTimeMs);

        Assert.True(ghostSeen > 0);
        Assert.True(realSeen > 0);

        var takenUtc = DateTime.UtcNow - TimeSpan.FromSeconds(60);
        var snapshot = race.CreateSnapshot() with
        {
            RaceStartUtc = takenUtc - TimeSpan.FromSeconds(16),
            AccumulatedRedTime = TimeSpan.Zero,
            RedFlagStartedUtc = null,
        };

        race.Reset();
        Assert.True(race.RestoreSnapshotAfterGap(snapshot, takenUtc, takenUtc + TimeSpan.FromSeconds(60)));

        var ghost = Standing(race, Ghost);
        var real = Standing(race, "101");

        Assert.Equal(ghostLap, ghost.LastLapDeviceTimeMs);
        Assert.Equal(ghostSeen, ghost.LastSeenDeviceTimeMs);
        Assert.Equal(realLap > 0 ? realLap + 60_000 : 0, real.LastLapDeviceTimeMs);
        Assert.Equal(realSeen + 60_000, real.LastSeenDeviceTimeMs);
    }

    // --- A01-3: the last window counts only cars on the board -------------------

    [Fact]
    public void A_no_show_s_late_release_does_not_stretch_the_last_window()
    {
        var race = new RaceControlSession
        {
            StartType = RaceStartType.StaggeredGrid,
            StaggerClockMode = StaggerClock.OwnRelease,
            FinishOnNextCrossing = false,
            TimeLimit = TimeSpan.FromMinutes(5),
        };
        race.Stage();
        race.SetReleaseSchedule(new Dictionary<string, long> { ["A"] = 0, ["B"] = 5_000, ["C"] = 10_000 });
        race.GreenFlag(0);

        Assert.Equal(TimeSpan.Zero, race.LastWindowExtension);

        Cross(race, "A", 5_000);
        Cross(race, "B", 11_000);

        // C never turned up, so its 10s slot is nobody's window.
        Assert.Equal(TimeSpan.FromSeconds(5), race.LastWindowExtension);
    }

    // --- A02-1: a hung I-Lap close ----------------------------------------------

    [Fact]
    public void A_close_still_hung_after_one_wait_is_not_waited_on_again()
    {
        using var client = new ILapClient(Path.Combine(_data.Folder, "captures"));
        using var release = new ManualResetEventSlim(false);
        var hung = new Thread(() => release.Wait()) { IsBackground = true };

        try
        {
            hung.Start();

            lock (client._closing)
            {
                client._closing.Add(("COM9", hung));
            }

            Assert.False(client.WaitForClosing("COM9", TimeSpan.FromMilliseconds(50)));
            Assert.True(client.WaitForClosing("COM9", TimeSpan.FromMilliseconds(50)));

            lock (client._closing)
            {
                Assert.DoesNotContain(client._closing, c => ReferenceEquals(c.Closer, hung));
            }
        }
        finally
        {
            release.Set();
            hung.Join(TimeSpan.FromSeconds(5));
        }
    }

    // --- A02-4: a failed transponder-health write -------------------------------

    [Fact]
    public void A_transponder_health_write_that_failed_is_tried_again()
    {
        var path = _data.PathOf("transponder-health.json");
        var store = TransponderHealthStore.Load();

        // A folder where the file goes: the write fails, as a held file would.
        Directory.CreateDirectory(path);

        store.Record(new DirectLapEvent { TransponderId = "101", SignalHits = 60, SignalStrength = 200 });
        store.Save();
        Assert.False(File.Exists(path));

        Directory.Delete(path);
        store.Save();

        Assert.True(File.Exists(path));
        Assert.Contains("101", File.ReadAllText(path));
    }

    // --- A24-1: the feeder for a bump-up ----------------------------------------

    [Fact]
    public void With_every_week_s_mains_in_view_tonight_s_A_main_is_fed_by_tonight_s_B_main()
    {
        var lastB = new RoundDefinition { ClassName = "Stock", RoundLabel = "B-Main", Kind = RoundKind.Main, MainTier = 1, Order = 1 };
        var lastA = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0, Order = 2, BumpUpCount = 1 };
        var tonightB = new RoundDefinition { ClassName = "Stock", RoundLabel = "B-Main", Kind = RoundKind.Main, MainTier = 1, Order = 3 };
        var tonightA = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0, Order = 4, BumpUpCount = 1 };

        var lastWeek = new SessionRecord { Label = "B-Main", SessionType = RaceSessionType.Race, StartedAtUtc = DateTime.UtcNow.AddDays(-7) };
        lastB.LinkedSessionId = lastWeek.Id;
        lastA.LinkedSessionId = "last-week-a";
        var history = SessionHistoryStore.FromSessions(new[] { lastWeek });

        // Ordered by tier, the way DayMainsForClass hands them over.
        var mains = new List<RoundDefinition> { lastA, tonightA, lastB, tonightB };

        Assert.Same(tonightB, MainsBuilder.FeederFor(tonightA, mains));
        Assert.Contains("hasn't been run", MainsBuilder.DescribeBumpUpBlocker(tonightA, mains, history));
    }

    [Fact]
    public void A_feeder_with_no_order_set_is_still_found()
    {
        var a = new RoundDefinition { RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0 };
        var b = new RoundDefinition { RoundLabel = "B-Main", Kind = RoundKind.Main, MainTier = 1 };

        Assert.Same(b, MainsBuilder.FeederFor(a, new[] { a, b }));
        Assert.Null(MainsBuilder.FeederFor(b, new[] { a, b }));
    }

    // --- A24-3: the label Add a Main by Hand offers -----------------------------

    [Fact]
    public void Add_a_main_by_hand_offers_the_lowest_tier_not_in_use()
    {
        static RoundDefinition Main(int tier) => new() { Kind = RoundKind.Main, MainTier = tier };

        Assert.Equal("A-Main", MainsBuilder.DefaultHandMainLabel(Array.Empty<RoundDefinition>()));
        Assert.Equal("B-Main", MainsBuilder.DefaultHandMainLabel(new[] { Main(0), Main(2) }));
        Assert.Equal("D-Main", MainsBuilder.DefaultHandMainLabel(new[] { Main(0), Main(1), Main(2) }));

        // An untiered main and a heat do not move the letter.
        var consolation = new RoundDefinition { Kind = RoundKind.Main, MainTier = -1, RoundLabel = "Consolation" };
        var heat = new RoundDefinition { Kind = RoundKind.Qualifier, MainTier = -1 };
        Assert.Equal("B-Main", MainsBuilder.DefaultHandMainLabel(new[] { Main(0), consolation, heat }));
    }

    // --- A25-2 / A25-3: the run sheet -------------------------------------------

    [Fact]
    public void A_run_sheet_holding_only_null_is_not_replaced_by_the_first_schedule_change()
    {
        File.WriteAllText(_data.PathOf("day-schedule.json"), "null");

        var store = DayScheduleStore.Load();

        Assert.True(store.LoadedUnreadable);

        store.Add(new ScheduleEntry { Description = "Practice", ScheduledTime = "19:00" });
        Assert.Equal("null", File.ReadAllText(_data.PathOf("day-schedule.json")));
    }

    [Fact]
    public void Minutes_late_is_null_while_the_line_running_has_no_time()
    {
        var store = DayScheduleStore.Load();
        var today = DateTime.Today;

        store.Add(new ScheduleEntry { Description = "Heat 1", ScheduledTime = "19:00", RoundId = "r1" });
        store.Add(new ScheduleEntry { Description = "Raffle", ScheduledTime = "", RoundId = "r2" });
        store.Add(new ScheduleEntry { Description = "Heat 2", ScheduledTime = "20:00", RoundId = "r3" });

        store.MarkStarted("r1", today.AddHours(19).AddMinutes(40).ToUniversalTime());
        Assert.Equal(40, store.MinutesLate());

        store.MarkStarted("r2", today.AddHours(19).AddMinutes(50).ToUniversalTime());
        Assert.Null(store.MinutesLate());

        store.MarkStarted("r3", today.AddHours(20).AddMinutes(10).ToUniversalTime());
        Assert.Equal(10, store.MinutesLate());
    }

    // --- A26-1: handicaps from the class's own races ----------------------------

    [Fact]
    public void Suggested_handicaps_use_only_the_class_s_own_races()
    {
        var rounds = RoundDefinitionStore.Load();
        var stock = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0 };
        var modified = new RoundDefinition { ClassName = "Modified", RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0 };
        rounds.Add(stock);
        rounds.Add(modified);

        var history = SessionHistoryStore.FromSessions(new[]
        {
            Race(modified, DateTime.UtcNow.AddHours(-1), ("X", 20_000)),
            Race(stock, DateTime.UtcNow.AddHours(-2), ("X", 30_000), ("Y", 25_000)),
        });

        var drivers = new[]
        {
            new DriverProfile { Transponder = "X", DriverName = "Xavier" },
            new DriverProfile { Transponder = "Y", DriverName = "Yolanda" },
        };

        var suggestions = HandicapStore.Load().Suggest(drivers, history, 5, 20, "Stock", rounds);
        var x = suggestions.Single(s => s.Transponder == "X");

        Assert.Equal(30.0, x.AverageBestLapSeconds);
        Assert.Equal(1, x.SessionsUsed);

        // Without the programme it is every class, as before.
        var everything = HandicapStore.Load().Suggest(drivers, history, 5, 20);
        Assert.Equal(2, everything.Single(s => s.Transponder == "X").SessionsUsed);
    }

    // --- A26-2: the Unicode minus -----------------------------------------------

    [Fact]
    public void Points_written_with_a_unicode_minus_are_read_as_negative()
    {
        Assert.True(DriverImport.TryParsePoints("−5", out var points));
        Assert.Equal(-5, points);
    }

    [Fact]
    public void A_csv_row_writes_a_negative_number_with_a_plain_minus_in_any_culture()
    {
        var before = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("-5", CsvHelper.BuildRow(-5));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // --- Helpers ----------------------------------------------------------------

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = raceTimeMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

    private static RaceControlSession Racing(int? lapLimit = null)
    {
        var race = new RaceControlSession { LapLimit = lapLimit };
        race.Stage();
        race.GreenFlag(0);
        return race;
    }

    private static RacerStanding Standing(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => s.TransponderId == car);

    private static SessionRecord Race(RoundDefinition round, DateTime startedUtc, params (string Transponder, long FastLapMs)[] results) => new()
    {
        Label = round.RoundLabel,
        SessionType = RaceSessionType.Race,
        RoundId = round.Id,
        StartedAtUtc = startedUtc,
        EndedAtUtc = startedUtc.AddMinutes(5),
        Results = results.Select((r, i) => new SessionResultEntry
        {
            Transponder = r.Transponder,
            DriverName = r.Transponder,
            Position = i + 1,
            Laps = 10,
            ElapsedMs = r.FastLapMs * 10,
            FastLapMs = r.FastLapMs,
        }).ToList(),
    };
}
