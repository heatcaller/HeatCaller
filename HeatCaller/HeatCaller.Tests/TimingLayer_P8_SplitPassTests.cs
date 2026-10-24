namespace HeatCaller.Tests;

/// <summary>
/// P8 of the timing layer, A (24 Oct 2026): with timing on its own thread a
/// split loop's pass goes straight to the timing thread, on the same queue
/// as the line's (RaceTiming.PostSplitPass). It is understood there
/// (SplitLoop.Understand, with ProfileStore's frozen split owners), put into
/// the race on the row SplitRows.RowFor finds (ProfileStore.
/// RowOwnerForAnyThread), and the split loop's own share (SplitLoop.Keep)
/// is queued for the UI. A frozen window used to lose the sector times of
/// every lap run while it was frozen.
/// </summary>
public sealed class TimingLayer_P8_SplitPassTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly TimingHost _host = new();

    /// <summary>The window: never pumped unless a test says so.</summary>
    private readonly QueueingSynchronizationContext _ui = new();

    public void Dispose()
    {
        _host.Dispose();
        _data.Dispose();
    }

    // ------------------------------------------------------------ helpers

    /// <summary>A split read as the app would understand it, for the facade tests: the row it goes on and its line time, or no row.</summary>
    private sealed class StubReading
    {
        public StubReading(string name, string? row, long lineMs)
        {
            Name = name;
            Row = row;
            LineMs = lineMs;
        }

        public string Name { get; }

        public string? Row { get; }

        public long LineMs { get; }
    }

    private static (string Row, TimingPoint Point, long LineMs)? IntoRace(StubReading reading, IReadOnlyList<string> rows) =>
        reading.Row is { } row && rows.Contains(row) ? (row, TimingPoint.Split1, reading.LineMs) : null;

    private static RaceControlSession Engine()
    {
        // Demo mode leaves the start reference alone: these crossings come
        // far faster than the wall clock (as SectorTimingTests).
        return new RaceControlSession { IsDemoMode = true, SplitCount = 1 };
    }

    private (RaceTiming Race, RaceControlSession Engine) Threaded()
    {
        var engine = Engine();
        var race = new RaceTiming(engine, _host, _ui, tickOnHost: false);
        race.Stage();
        race.GreenFlag(0);
        return (race, engine);
    }

    private static DirectLapEvent Line(string car, uint at) =>
        new() { TransponderId = car, DeviceTimeMs = at, SignalHits = 5, SignalStrength = 100, Decoder = DecoderConnection.EasyLap };

    /// <summary>One night: (car, line crossing) or (car, split read, its row known or not), in arrival order.</summary>
    private static readonly (string Car, bool IsSplit, uint At, bool RowKnown)[] Night =
    {
        ("P2", true, 1_000, true),      // a split before any crossing: not used
        ("P1", false, 5_000, true),
        ("P2", false, 5_400, true),
        ("P1", true, 9_000, true),
        ("P2", true, 9_900, true),
        ("P1", false, 15_000, true),
        ("P1", true, 19_100, true),
        ("P2", false, 15_600, true),    // the line's crossing read late
        ("P2", true, 20_000, false),    // a read whose row can't be told
        ("P1", false, 25_000, true),
        ("P2", false, 25_800, true),
        ("P2", true, 30_100, true),
        ("P1", true, 29_000, true),
        ("P1", false, 35_000, true),
        ("P2", false, 36_000, true),
    };

    private static void Barrier(TimingHost host) => host.Invoke(() => { });

    // ------------------------------------------- 1. Understand + Keep = Record

    private static ProfileStore SplitProfiles()
    {
        var store = ProfileStore.Load();

        var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
        maya.SplitTransponders.Add(new OwnedTransponder { Id = "7001", CarId = "red" });
        maya.SplitTransponders.Add(new OwnedTransponder { Id = "7002" });
        store.Upsert(maya);

        // 7002 claimed by two: nobody's, and the console says why.
        var dev = new DriverProfile { Transponder = "200", DriverName = "Dev" };
        dev.SplitTransponders.Add(new OwnedTransponder { Id = " 7002 " });
        store.Upsert(dev);

        return store;
    }

    private static readonly DirectLapEvent[] SplitReads =
    {
        new() { TransponderId = "7001", ReceivedMs = 20_030, Decoder = DecoderConnection.ILap, Point = TimingPoint.Split1 },
        new() { TransponderId = " 7002", ReceivedMs = 21_000, Decoder = DecoderConnection.ILap, Point = TimingPoint.Split1 },
        new() { TransponderId = "8888", ReceivedMs = 22_000, Decoder = DecoderConnection.ILap, Point = TimingPoint.Split1 },
        new() { TransponderId = "7001", ReceivedMs = 23_000, Decoder = DecoderConnection.ILap, Point = TimingPoint.Line },

        // The clock can't be placed for this one (AppTimeOf null below).
        new() { TransponderId = "7001", ReceivedMs = 24_000, Decoder = DecoderConnection.EasyLap, Point = TimingPoint.Split1 },
    };

    private static SplitLoop Loop(SplitLoop.SplitOwnerLookup owners, Func<string, int> claimants, List<string> log, List<SplitPass> recorded)
    {
        var loop = new SplitLoop(owners)
        {
            ClaimantsOf = claimants,
            AppTimeOf = pass => pass.Decoder == DecoderConnection.EasyLap ? null : pass.ReceivedMs,
            LineTimeOf = app => app - 5_000,
            OffsetMs = () => 30,
        };

        loop.Log += log.Add;
        loop.PassRecorded += recorded.Add;
        return loop;
    }

    [Fact]
    public void Understand_then_keep_does_exactly_what_record_does()
    {
        var profiles = SplitProfiles();

        var recordLog = new List<string>();
        var recordPasses = new List<SplitPass>();
        var byRecord = Loop(profiles.SplitOwnerOf, t => profiles.SplitOwnersOf(t).Count, recordLog, recordPasses);

        var keepLog = new List<string>();
        var keepPasses = new List<SplitPass>();
        var byKeep = Loop(profiles.SplitOwnerOf, t => profiles.SplitOwnersOf(t).Count, keepLog, keepPasses);

        // And the timing thread's reader, on the frozen lookups: the same
        // readings, kept by a third loop.
        var frozenLog = new List<string>();
        var frozenPasses = new List<SplitPass>();
        var reader = Loop(profiles.SplitOwnerForAnyThread, profiles.SplitClaimantsForAnyThread, new List<string>(), new List<SplitPass>());
        var keeper = Loop(profiles.SplitOwnerOf, t => profiles.SplitOwnersOf(t).Count, frozenLog, frozenPasses);

        var returned = new List<(SplitPass A, SplitPass B, SplitPass C)>();

        foreach (var read in SplitReads)
        {
            returned.Add((byRecord.Record(read), byKeep.Keep(byKeep.Understand(read)), keeper.Keep(reader.Understand(read))));
        }

        foreach (var (a, b, c) in returned)
        {
            Assert.Equal(a, b);
            Assert.Equal(a, c);
        }

        Assert.Equal(byRecord.Recent.ToList(), byKeep.Recent.ToList());
        Assert.Equal(byRecord.Recent.ToList(), keeper.Recent.ToList());
        Assert.Equal(recordLog, keepLog);
        Assert.Equal(recordLog, frozenLog);
        Assert.Equal(recordPasses, keepPasses);
        Assert.Equal(recordPasses, frozenPasses);
        Assert.Equal(byRecord.LastPassAt, byKeep.LastPassAt);
        Assert.Equal(byRecord.LastPassAt, keeper.LastPassAt);
        Assert.Equal(byRecord.UnknownRecently(25_000), byKeep.UnknownRecently(25_000));
        Assert.Equal(byRecord.UnknownRecently(25_000), keeper.UnknownRecently(25_000));

        // The cases the reads were picked for.
        Assert.Equal("100", recordPasses[0].Driver);
        Assert.Equal("red", recordPasses[0].CarId);
        Assert.Equal(15_000, recordPasses[0].LineMs);
        Assert.Contains("claimed by 2 drivers", recordLog[1]);
        Assert.Null(recordPasses[2].Driver);
        Assert.Equal(TimingPoint.Split1, recordPasses[3].Point);
        Assert.Null(recordPasses[4].LineMs);
        Assert.Equal(new[] { "8888", "7002" }, byRecord.UnknownRecently(25_000).ToArray());
    }

    // ------------------------------------------------- 2. SplitRows.RowFor

    private static SplitPass SplitOf(string? driver, string carId = "") =>
        new("7001", TimingPoint.Split1, driver, carId, 10_000, 10_000, 10_000);

    [Fact]
    public void Row_for_a_split_follows_row_for_split()
    {
        var owners = new Dictionary<string, (string Main, string? CarId)>(StringComparer.OrdinalIgnoreCase)
        {
            ["100"] = ("100", "red"),
            ["101"] = ("100", "blue"),
            ["102"] = ("100", null),
            ["200"] = ("200", null),
            [Ghosts.Prefix + "1"] = ("300", null),
        };

        (string Main, string? CarId)? OwnerOf(string row) => owners.TryGetValue(row, out var owner) ? owner : null;

        // One row: that one, whatever the car.
        Assert.Equal("100", SplitRows.RowFor(SplitOf("100", "blue"), new[] { "100", "200" }, OwnerOf));
        Assert.Equal("100", SplitRows.RowFor(SplitOf("100"), new[] { "200", "100" }, OwnerOf));

        // The driver compared ignoring case, as RowForSplit does.
        Assert.Equal("200", SplitRows.RowFor(SplitOf("200"), new[] { "100", "200" }, OwnerOf));

        // Two cars out: the one the split's car is fitted to.
        Assert.Equal("101", SplitRows.RowFor(SplitOf("100", "blue"), new[] { "100", "101", "200" }, OwnerOf));
        Assert.Equal("100", SplitRows.RowFor(SplitOf("100", "red"), new[] { "100", "101" }, OwnerOf));

        // Two cars and the split's car not said, or not told apart: none.
        Assert.Null(SplitRows.RowFor(SplitOf("100"), new[] { "100", "101" }, OwnerOf));
        Assert.Null(SplitRows.RowFor(SplitOf("100", "green"), new[] { "100", "101" }, OwnerOf));

        // CarId compared exactly, as RowForSplit does.
        Assert.Null(SplitRows.RowFor(SplitOf("100", "BLUE"), new[] { "100", "101" }, OwnerOf));

        // A ghost row is never the split's, even owned.
        Assert.Equal("200", SplitRows.RowFor(SplitOf("200"), new[] { Ghosts.Prefix + "1", "200" }, r => r.StartsWith(Ghosts.Prefix) ? ("200", null) : OwnerOf(r)));

        // Nobody's split transponder, or no row of theirs: none.
        Assert.Null(SplitRows.RowFor(SplitOf(null), new[] { "100" }, OwnerOf));
        Assert.Null(SplitRows.RowFor(SplitOf("999"), new[] { "100", "200" }, OwnerOf));
    }

    // ------------------------------------- 3. frozen lookups = the live ones

    private static ProfileStore RowProfiles()
    {
        var store = ProfileStore.Load();

        var maya = new DriverProfile { Transponder = "AB10", DriverName = "Maya" };
        maya.Transponders.Add(new OwnedTransponder { Id = "AB10", CarId = "red" });
        maya.Transponders.Add(new OwnedTransponder { Id = " ab11 ", CarId = "blue" });
        maya.Transponders.Add(new OwnedTransponder { Id = "300" });
        maya.SplitTransponders.Add(new OwnedTransponder { Id = "S1", CarId = "red" });
        maya.SplitTransponders.Add(new OwnedTransponder { Id = "s1", CarId = "second entry, never used" });
        maya.SplitTransponders.Add(new OwnedTransponder { Id = "S2" });
        store.Upsert(maya);

        // Dev's alternate is Maya's main one (the main one wins), and 300 is
        // both theirs (nobody's).
        var dev = new DriverProfile { Transponder = "200", DriverName = "Dev" };
        dev.Transponders.Add(new OwnedTransponder { Id = "ab10", CarId = "dev's" });
        dev.Transponders.Add(new OwnedTransponder { Id = "300" });
        dev.Transponders.Add(new OwnedTransponder { Id = "201", CarId = "kart" });
        dev.SplitTransponders.Add(new OwnedTransponder { Id = " s2 " });
        dev.SplitTransponders.Add(new OwnedTransponder { Id = "S3", CarId = "kart" });
        store.Upsert(dev);

        return store;
    }

    private static readonly string[] Queries =
    {
        "AB10", "ab10", " AB10", "AB10 ", "ab11", "AB11", " ab11 ", "300", "200", " 200", "201", "201 ",
        "S1", "s1", " S1 ", "S2", "s2", "S3", "s3 ", "nobody", "", "   ",
    };

    private static void AssertFrozenAgreesWithLive(ProfileStore store)
    {
        foreach (var q in Queries)
        {
            var live = store.SplitOwnerOf(q);
            var frozen = store.SplitOwnerForAnyThread(q);

            Assert.Equal(live is null, frozen is null);

            if (live is { } l && frozen is { } f)
            {
                Assert.Same(l.Driver, f.Driver);
                Assert.Same(l.Entry, f.Entry);
            }

            Assert.Equal(store.SplitOwnersOf(q).Count, store.SplitClaimantsForAnyThread(q));

            var profile = store.Get(q);
            (string Main, string? CarId)? expected = profile is null ? null : (profile.Transponder, profile.TransponderEntry(q)?.CarId);
            Assert.Equal(expected, store.RowOwnerForAnyThread(q));
        }
    }

    [Fact]
    public void The_frozen_split_and_row_lookups_say_what_the_live_ones_say()
    {
        var store = RowProfiles();

        AssertFrozenAgreesWithLive(store);

        // Spot checks of the awkward cases.
        Assert.Equal(("AB10", "red"), store.RowOwnerForAnyThread("ab10"));
        Assert.Equal(("AB10", "blue"), store.RowOwnerForAnyThread(" AB11"));
        Assert.Null(store.RowOwnerForAnyThread("300"));
        Assert.Null(store.RowOwnerForAnyThread(" AB10"));
        Assert.Null(store.SplitOwnerForAnyThread("s2"));
        Assert.Equal(2, store.SplitClaimantsForAnyThread("S2 "));
        Assert.Equal("red", store.SplitOwnerForAnyThread(" s1")!.Value.Entry.CarId);

        // A change: Dev gives up S2 and takes S1; Maya's second car moves.
        var dev = ProfileStore.Snapshot(store.Get("200")!);
        dev.SplitTransponders.RemoveAll(t => t.Id.Trim() == "s2");
        dev.SplitTransponders.Add(new OwnedTransponder { Id = "S1" });
        store.Upsert(dev);

        var maya = ProfileStore.Snapshot(store.Get("AB10")!);
        maya.TransponderEntry("ab11")!.CarId = "green";
        store.Upsert(maya);

        AssertFrozenAgreesWithLive(store);
        Assert.Null(store.SplitOwnerForAnyThread("S1"));
        Assert.Equal("Maya", store.SplitOwnerForAnyThread("S2")!.Value.Driver.DriverName);
        Assert.Equal(("AB10", "green"), store.RowOwnerForAnyThread("AB11"));

        // Maya gone: her transponders are nobody's, and Dev's ab10 and 300
        // are his now.
        store.Delete("AB10");

        AssertFrozenAgreesWithLive(store);
        Assert.Equal(("200", "dev's"), store.RowOwnerForAnyThread("AB10"));
        Assert.Equal("Dev", store.SplitOwnerForAnyThread("s1")!.Value.Driver.DriverName);
    }

    // --------------------------------------------------- 4. a frozen window

    [Fact]
    public void Split_reads_go_into_the_race_with_the_window_frozen()
    {
        var (race, engine) = Threaded();
        var sectors = new List<(string Car, int Lap, long[] Sectors)>();
        var onTiming = new List<bool>();

        race.AddTimingSideHandler(e => e.SectorsRecorded += (standing, lap, cut) =>
        {
            onTiming.Add(_host.IsTimingThread);
            sectors.Add((standing.TransponderId, lap, cut.ToArray()));
        });

        race.PostLinePass(Line("P1", 5_000), _ => { });
        race.PostSplitPass(() => new StubReading("a", "P1", 9_000), _ => { }, IntoRace);
        race.PostLinePass(Line("P1", 15_000), _ => { });
        race.PostSplitPass(() => new StubReading("b", "P1", 19_500), _ => { }, IntoRace);
        race.PostLinePass(Line("P1", 25_000), _ => { });

        // The window is never pumped: the laps were cut on the timing side.
        Barrier(_host);
        _host.Invoke(() =>
        {
            Assert.Equal(2, sectors.Count);
            Assert.DoesNotContain(false, onTiming);
            Assert.Equal(new long[] { 4_000, 6_000 }, sectors[0].Sectors);
            Assert.Equal(new long[] { 4_500, 5_500 }, sectors[1].Sectors);
            Assert.Equal(new long[] { 4_000, 6_000 }, engine.GetOrderedStandings().Single(s => s.TransponderId == "P1").SectorsOf(1));
        });
    }

    // -------------------------------------------- 5. the order passes arrive in

    [Fact]
    public void A_night_of_line_and_split_passes_cuts_the_same_sectors_threaded_as_inline()
    {
        var inline = Engine();
        inline.Stage();
        inline.GreenFlag(0);

        var (race, engine) = Threaded();

        foreach (var (car, isSplit, at, rowKnown) in Night)
        {
            if (isSplit)
            {
                if (rowKnown)
                {
                    inline.ProcessSplitPass(car, TimingPoint.Split1, at);
                }

                race.PostSplitPass(() => new StubReading(car, rowKnown ? car : null, at), _ => { }, IntoRace);
            }
            else
            {
                inline.ProcessLapEvent(Line(car, at));
                race.PostLinePass(Line(car, at), _ => { });
            }
        }

        Barrier(_host);

        var threaded = _host.Invoke(() => engine.GetOrderedStandings()
            .Select(s => (s.TransponderId, Laps: Enumerable.Range(0, s.LapTimesMs.Count).Select(i => s.SectorsOf(i) is { } c ? string.Join("+", c) : "-").ToList()))
            .ToList());

        var expected = inline.GetOrderedStandings()
            .Select(s => (s.TransponderId, Laps: Enumerable.Range(0, s.LapTimesMs.Count).Select(i => s.SectorsOf(i) is { } c ? string.Join("+", c) : "-").ToList()))
            .ToList();

        Assert.Equal(expected.Count, threaded.Count);

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].TransponderId, threaded[i].TransponderId);
            Assert.Equal(expected[i].Laps, threaded[i].Laps);
        }

        // The night had sectors to compare.
        Assert.Contains(expected.SelectMany(e => e.Laps), lap => lap != "-");
    }

    // --------------------------------------------------------- 6. the UI share

    [Fact]
    public void The_ui_shares_run_in_arrival_order_with_the_split_before_what_it_caused()
    {
        var (race, _) = Threaded();
        var seen = new List<string>();

        race.SplitCrossed += (standing, _, _) => seen.Add("crossed:" + standing.TransponderId);

        race.PostLinePass(Line("P1", 5_000), pass => seen.Add("line:" + pass.DeviceTimeMs));
        race.PostSplitPass(() => new StubReading("s9000", "P1", 9_000), r => seen.Add("split:" + r.Name), IntoRace);
        race.PostSplitPass(() => new StubReading("unknown", null, 9_500), r => seen.Add("split:" + r.Name), IntoRace);
        race.PostLinePass(Line("P1", 15_000), pass => seen.Add("line:" + pass.DeviceTimeMs));
        race.PostSplitPass(() => new StubReading("s19000", "P1", 19_000), r => seen.Add("split:" + r.Name), IntoRace);

        Barrier(_host);
        Assert.Empty(seen);

        _ui.Pump();

        Assert.Equal(
            new[] { "line:5000", "split:s9000", "crossed:P1", "split:unknown", "line:15000", "split:s19000", "crossed:P1" },
            seen.ToArray());
    }

    [Fact]
    public void A_read_that_fails_is_reported_and_the_passes_after_it_still_count()
    {
        var (race, engine) = Threaded();
        var log = new List<string>();
        var shares = new List<string>();
        race.LogMessage += log.Add;

        race.PostLinePass(Line("P1", 5_000), _ => { });
        race.PostSplitPass<StubReading>(() => throw new InvalidOperationException("no lookups"), r => shares.Add(r.Name), IntoRace);
        race.PostSplitPass(() => new StubReading("row fails", "P1", 9_000), r => shares.Add(r.Name), (_, _) => throw new InvalidOperationException("no rows"));
        race.PostLinePass(Line("P1", 15_000), _ => { });

        Barrier(_host);
        _ui.Pump();

        Assert.Equal(2, _host.Invoke(() => engine.GetOrderedStandings().Single().LapTimesMs.Count));
        Assert.Equal(new[] { "row fails" }, shares.ToArray());
        Assert.Contains(log, l => l.Contains("A split pass could not be read") && l.Contains("no lookups"));
        Assert.Contains(log, l => l.Contains("could not be matched to a row") && l.Contains("no rows"));
    }

    [Fact]
    public void Not_racing_or_no_split_loops_the_read_is_kept_and_nothing_is_asked_of_the_race()
    {
        var engine = Engine();
        var race = new RaceTiming(engine, _host, _ui, tickOnHost: false);
        var asked = 0;
        var shares = 0;

        // Idle: the UI still keeps the read (Line Up runs between races).
        race.PostSplitPass(() => new StubReading("idle", "P1", 9_000), _ => shares++, (r, rows) => { asked++; return IntoRace(r, rows); });
        Barrier(_host);
        _ui.Pump();

        Assert.Equal(1, shares);
        Assert.Equal(0, asked);

        // Racing with no split loops in use.
        race.SplitCount = 0;
        race.Stage();
        race.GreenFlag(0);
        race.PostSplitPass(() => new StubReading("none", "P1", 9_000), _ => shares++, (r, rows) => { asked++; return IntoRace(r, rows); });
        Barrier(_host);
        _ui.Pump();

        Assert.Equal(2, shares);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Inline_a_split_pass_is_never_posted()
    {
        var race = new RaceTiming(Engine());

        Assert.Throws<InvalidOperationException>(() =>
            race.PostSplitPass(() => new StubReading("x", "P1", 1), _ => { }, IntoRace));
    }
}
