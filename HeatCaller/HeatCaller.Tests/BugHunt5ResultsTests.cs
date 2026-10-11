namespace HeatCaller.Tests;

/// <summary>
/// Results, timing and points fixes from the fifth bug hunt (9 Oct 2026),
/// all three in the part of the app that decides and then SAVES a club's
/// race history:
///
/// Undoing a Reset moved the race clock forward by the time the race was
/// gone but left the decoder's clock, and every driver's readings on it,
/// where they were - so the next crossing after the green was booked as a
/// lap as long as the stoppage and the driver's elapsed time jumped with it,
/// both of them into the saved result and the journal.
///
/// Championship standings had no deterministic final tie-break, so two
/// drivers level on points, wins and best finish came out in whatever order
/// the sessions happened to be walked in - which also made the end-of-race
/// swing report a place change for somebody whose points had not moved.
///
/// A second correction to a saved result captured a renumbered place as the
/// one "the race recorded", which then decided a tie the wrong way round.
/// </summary>
public sealed class BugHunt5ResultsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- results-1: the decoder clock after an undone Reset ----------------------

    [Fact]
    public void A_lap_after_an_undone_reset_is_timed_from_the_last_lap_not_from_the_whole_stoppage()
    {
        // A 5:00 heat. One driver, a 1.4s dash to the line and a 10.0s lap.
        var race = RunningRace(TimeSpan.FromMinutes(5));
        Seen(race, "A", 101_400);
        Seen(race, "A", 111_400);

        Assert.Equal(new long[] { 1_400, 10_000 }, Standing(race, "A").LapTimesMs);
        Assert.Equal(11_400, Standing(race, "A").ElapsedMs);

        // Reset, and undone a minute later.
        UndoneAfterAMinute(race, elapsedWhenReset: TimeSpan.FromMilliseconds(11_400));

        // The director gets the cars back in order and goes green. The
        // decoder has been running throughout: it is now a minute and ten
        // seconds past the green of the race that was put back.
        race.GreenFlag(171_400);
        Seen(race, "A", 181_400);

        var a = Standing(race, "A");

        // Another 10.0s lap - not the 70.0s the stoppage made of it.
        Assert.Equal(new long[] { 1_400, 10_000, 10_000 }, a.LapTimesMs);
        Assert.Equal(3, a.Laps);

        // And 21.4s on the clock, not 81.4s. This is the figure
        // RaceFiling saves into the result and RaceJournal into the journal.
        Assert.Equal(21_400, a.ElapsedMs);
        Assert.Equal(a.LapTimesMs.Sum(), a.ElapsedMs);

        // The lap is a real lap, so it is not marked as one run through a
        // stoppage - and with the arithmetic right it does not need to be.
        Assert.Empty(a.RecoveryLapIndexes);

        // It is also still a 10.0s best rather than a 70.0s streak baseline.
        Assert.Equal(10_000, a.FastestLapMs);
        Assert.Equal(10_000, a.StreakBaselineLapMs);
    }

    [Fact]
    public void Every_driver_s_elapsed_time_still_sums_to_their_laps_after_an_undone_reset()
    {
        var race = RunningRace(TimeSpan.FromMinutes(5));

        Seen(race, "A", 101_400);
        Seen(race, "B", 101_900);
        Seen(race, "A", 111_400);
        Seen(race, "B", 112_400);

        UndoneAfterAMinute(race, elapsedWhenReset: TimeSpan.FromMilliseconds(12_400));

        race.GreenFlag(172_400);
        Seen(race, "A", 181_400);
        Seen(race, "B", 182_900);

        var a = Standing(race, "A");
        var b = Standing(race, "B");

        Assert.Equal(new long[] { 1_400, 10_000, 10_000 }, a.LapTimesMs);
        Assert.Equal(new long[] { 1_900, 10_500, 10_500 }, b.LapTimesMs);
        Assert.Equal(21_400, a.ElapsedMs);
        Assert.Equal(22_900, b.ElapsedMs);
        Assert.Equal(a.LapTimesMs.Sum(), a.ElapsedMs);
        Assert.Equal(b.LapTimesMs.Sum(), b.ElapsedMs);

        // The order is unchanged by any of this - the shift was the same
        // for everybody - but it is worth pinning down that it stayed so.
        Assert.Equal(new[] { "A", "B" }, race.GetOrderedStandings().Select(s => s.TransponderId));
    }

    // --- points-1: a championship tie has to be settled the same way every time ---

    [Fact]
    public void Championship_standings_come_back_in_the_same_order_whichever_way_the_races_are_handed_over()
    {
        var season = new Season { Name = "Summer" };

        // A win and a second each: 19 points, one win, a best finish of
        // first and two podiums apiece. Nothing separates them but the name.
        var round1 = Heat("Alice", "Bob");
        var round2 = Heat("Bob", "Alice");

        var oldestFirst = SeasonStore.ComputeStandings(season, new[] { round1, round2 });
        var newestFirst = SeasonStore.ComputeStandings(season, new[] { round2, round1 });

        Assert.Equal(new[] { 19, 19 }, oldestFirst.Select(e => e.TotalPoints));
        Assert.Equal(new[] { "Alice", "Bob" }, oldestFirst.Select(e => e.DriverName));
        Assert.Equal(new[] { "Alice", "Bob" }, newestFirst.Select(e => e.DriverName));
    }

    [Fact]
    public void A_championship_tie_is_settled_by_a_count_back_on_podium_finishes()
    {
        var season = new Season { Name = "Summer" };

        // Both on 22 points, one win and a best finish of first. Bob
        // finished second in one of them, Alice never on the rostrum
        // outside her win - so the count-back puts Bob ahead, which is the
        // opposite of what the name would say.
        var round1 = Heat("Bob", "x1", "x2", "Alice", "x3", "x4", "x5", "x6");
        var round2 = Heat("Alice", "Bob", "y1", "y2", "y3", "y4", "y5", "y6");
        var round3 = Heat("z1", "z2", "z3", "z4", "z5", "Alice", "z6", "Bob");

        // Handed over with Alice's round first, which is the order that used
        // to put her on top of the table.
        var table = SeasonStore.ComputeStandings(season, new[] { round2, round1, round3 });

        var alice = table.Single(e => e.DriverName == "Alice");
        var bob = table.Single(e => e.DriverName == "Bob");

        Assert.Equal(22, alice.TotalPoints);
        Assert.Equal(22, bob.TotalPoints);
        Assert.Equal(1, alice.Wins);
        Assert.Equal(1, bob.Wins);
        Assert.Equal(1, alice.BestFinish);
        Assert.Equal(1, bob.BestFinish);
        Assert.Equal(1, alice.Podiums);
        Assert.Equal(2, bob.Podiums);

        Assert.Equal(new[] { "Bob", "Alice" }, table.Where(e => e.DriverName is "Alice" or "Bob").Select(e => e.DriverName));
    }

    // --- results-2: a tie after two rounds of corrections -------------------------

    [Fact]
    public void A_dead_heat_made_by_two_separate_corrections_keeps_the_order_the_race_recorded()
    {
        // Ten laps each: A 100.000s, B 101.000s, C 102.000s.
        var record = Race(
            Driver("A", 10_000),
            Driver("B", 10_100),
            Driver("C", 10_200));

        // Round one: B picks up two seconds, which puts C second and has
        // every row renumbered.
        Apply(record, new ResultAdjustment("B", 0, 2_000, 0, ""));
        Assert.Equal(new[] { "A", "C", "B" }, record.Results.Select(r => r.Transponder));

        // Round two: C picks up one. B and C are now level - ten laps and
        // 103.000s each - and the race had B ahead of C.
        Apply(record, new ResultAdjustment("C", 0, 1_000, 0, ""));

        Assert.Equal(103_000, record.Results.Single(r => r.Transponder == "B").ElapsedMs);
        Assert.Equal(103_000, record.Results.Single(r => r.Transponder == "C").ElapsedMs);

        // C used to come out second here: its AsRaced.Position was captured
        // AFTER round one had renumbered it, so it carried B's tie-break key
        // of 2 and list order broke the tie. The raced laps and times settle
        // it now, which is why the stored place being wrong no longer
        // decides anything.
        Assert.Equal(new[] { "A", "B", "C" }, record.Results.Select(r => r.Transponder));
        Assert.Equal(new[] { 1, 2, 3 }, record.Results.Select(r => r.Position));
    }

    // --- Helpers -----------------------------------------------------------------

    /// <summary>A pass from the decoder itself - not synthetic, so the start checks see it.</summary>
    private static void Seen(RaceControlSession race, string car, uint deviceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = deviceTimeMs,
            SignalHits = 60,
            SignalStrength = 247,
        });

    private static RaceControlSession RunningRace(TimeSpan? timeLimit = null)
    {
        var race = new RaceControlSession { TimeLimit = timeLimit };
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);
        return race;
    }

    private static RacerStanding Standing(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => s.TransponderId == car);

    /// <summary>
    /// Resets the race and undoes it a minute later, the way Race Control
    /// does: the snapshot's start is put where the race clock actually read
    /// when it was reset, because a test's wall clock has not moved while
    /// the decoder times above were being made up.
    /// </summary>
    private static void UndoneAfterAMinute(RaceControlSession race, TimeSpan elapsedWhenReset)
    {
        var takenUtc = DateTime.UtcNow - TimeSpan.FromSeconds(60);
        var snapshot = race.CreateSnapshot() with
        {
            RaceStartUtc = takenUtc - elapsedWhenReset,
            AccumulatedRedTime = TimeSpan.Zero,
            RedFlagStartedUtc = null,
        };

        race.Reset();

        // A MINUTE EXACTLY, handed over rather than measured.
        //
        // The gap is wall-clock time and the restore moves the decoder's
        // reference by it, so measuring it to DateTime.UtcNow made every
        // lap assertion below depend on how long this method took to run.
        // It passed on one machine for a day and then failed on another by
        // 20ms - "expected 10000, actual 9980" - which is not a race-timing
        // fault at all, it is this test timing itself. The assertions here
        // are exact on purpose (the laps have to add up to the elapsed
        // time), so the gap is what gets pinned.
        Assert.True(race.RestoreSnapshotAfterGap(snapshot, takenUtc, takenUtc + TimeSpan.FromSeconds(60)));
        Assert.Equal(FlagState.Red, race.Flag);
    }

    /// <summary>One race, the drivers given in finishing order - everyone on the same laps.</summary>
    private static SessionRecord Heat(params string[] driversInOrder) => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = DateTime.UtcNow.AddMinutes(-6),
        EndedAtUtc = DateTime.UtcNow,
        Results = driversInOrder.Select((name, index) => new SessionResultEntry
        {
            Transponder = name,
            DriverName = name,
            Position = index + 1,
            Laps = 10,
            ElapsedMs = 100_000 + index * 1_000,
            FastLapMs = 9_000,
        }).ToList(),
    };

    /// <summary>Ten laps of the same time, so the total is exactly ten times it.</summary>
    private static SessionResultEntry Driver(string transponder, long lapMs) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = 10,
        ElapsedMs = lapMs * 10,
        LapTimesMs = Enumerable.Repeat(lapMs, 10).ToList(),
        FastLapMs = lapMs,
        Status = "active",
    };

    /// <summary>A saved race with these results, in the order the race gave them.</summary>
    private static SessionRecord Race(params SessionResultEntry[] results)
    {
        for (var i = 0; i < results.Length; i++)
        {
            results[i].Position = i + 1;
        }

        return new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = DateTime.UtcNow,
            Results = results.ToList(),
        };
    }

    private static void Apply(SessionRecord record, params ResultAdjustment[] adjustments)
    {
        Assert.Null(ResultEditor.Validate(record, adjustments));
        Assert.Equal(adjustments.Length, ResultEditor.Apply(record, adjustments, "bug hunt 5", "test"));
    }
}
