namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the director's detectors: what the battle and pace watchers
/// are fed from the live race, and how a pass is counted.
/// </summary>
public sealed class BugHunt7DirectorDetectorsTests
{
    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 13, 19, 30, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    private static RacerStanding Car(string id, params long[] lapTimesMs)
    {
        var car = new RacerStanding { TransponderId = id, Laps = lapTimesMs.Length, ElapsedMs = lapTimesMs.Sum() };
        car.LapTimesMs.AddRange(lapTimesMs);
        return car;
    }

    private static long[] Laps(int count, long eachMs) => Enumerable.Repeat(eachMs, count).ToArray();

    // ------------------------------------------------------------------
    //  A pass is a lead change, not the end of the fight
    // ------------------------------------------------------------------

    [Fact]
    public void A_pass_through_an_unmeasurable_tick_counts_as_a_lead_change()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        for (var i = 0; i < 40; i++)
        {
            watch.Observe(new[] { new PairGap("A", "B", 1, 0.3, 0) });
            clock.Advance(TimeSpan.FromSeconds(0.5));
        }

        // B has crossed ahead, A has not crossed yet: no gap to measure.
        watch.Observe(new[] { new PairGap("B", "A", 1, double.NaN, 0) });
        clock.Advance(TimeSpan.FromSeconds(0.5));

        watch.Observe(new[] { new PairGap("B", "A", 1, 0.2, 0) });

        var battle = watch.Pick();

        Assert.NotNull(battle);
        Assert.Equal("B", battle!.AheadId);
        Assert.Equal(1, battle.LeadChanges);
        Assert.Equal(0.2, battle.ClosestSeconds, 3);
    }

    [Fact]
    public void An_unmeasurable_gap_never_starts_a_fight()
    {
        var watch = new BattleWatch(new Clock().Read);

        watch.Observe(new[] { new PairGap("A", "B", 1, double.NaN, 0) });

        Assert.Equal(0, watch.Watching);
    }

    [Fact]
    public void A_gap_that_stays_unmeasurable_ends_the_fight_after_the_hold()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        watch.Observe(new[] { new PairGap("A", "B", 1, 0.3, 0) });
        clock.Advance(TimeSpan.FromSeconds(1));
        watch.Observe(new[] { new PairGap("A", "B", 1, double.NaN, 0) });

        Assert.Equal(1, watch.Watching);

        clock.Advance(watch.UnmeasuredHold);
        watch.Observe(new[] { new PairGap("A", "B", 1, double.NaN, 0) });

        Assert.Equal(0, watch.Watching);
    }

    [Fact]
    public void A_lapped_car_is_still_dropped_when_its_gap_is_unmeasurable()
    {
        var watch = new BattleWatch(new Clock().Read);

        watch.Observe(new[] { new PairGap("A", "B", 1, 0.3, 0) });
        watch.Observe(new[] { new PairGap("A", "B", 1, double.NaN, 1) });

        Assert.Equal(0, watch.Watching);
    }

    [Fact]
    public void The_moment_after_a_pass_is_handed_over_as_unmeasured_not_as_miles_apart()
    {
        // A led at the end of lap 10 by 0.3 s; B passed and has crossed
        // lap 11, A has not yet.
        var a = Car("A", Laps(10, 9_000));
        var b = Car("B", Laps(9, 9_000).Concat(new long[] { 9_300, 8_700 }).ToArray());

        var pairs = AnnouncerControl.PairsFrom(new List<RacerStanding> { b, a });

        var pair = Assert.Single(pairs);
        Assert.Equal("B", pair.AheadId);
        Assert.Equal(0, pair.LapsDown);
        Assert.True(double.IsNaN(pair.GapSeconds));
    }

    [Fact]
    public void A_pair_carries_the_chasing_car_s_laps_and_both_penalties()
    {
        // 8 Nov 2026: what RaceHorizon keys its samples on, and what ends a
        // gap's history when race control changes it.
        var a = Car("A", Laps(10, 9_000));
        a.PenaltyMs = 2_000;
        var b = Car("B", Laps(9, 9_100));
        b.PenaltyMs = 1_000;

        var pair = Assert.Single(AnnouncerControl.PairsFrom(new List<RacerStanding> { a, b }));

        Assert.Equal(9, pair.BehindLaps);
        Assert.Equal(2_000, pair.AheadPenaltyMs);
        Assert.Equal(1_000, pair.BehindPenaltyMs);
    }

    // ------------------------------------------------------------------
    //  Ghosts are not part of the race being called
    // ------------------------------------------------------------------

    [Fact]
    public void Ghosts_are_left_out_of_the_field_while_a_real_car_is_racing()
    {
        var ghost = Car(Ghosts.Prefix + "1", 9_000);
        var real = Car("101", 9_500);

        var field = AnnouncerControl.RacingField(new List<RacerStanding> { ghost, real });

        var only = Assert.Single(field);
        Assert.Equal("101", only.TransponderId);
    }

    [Fact]
    public void A_field_of_nothing_but_ghosts_is_still_called()
    {
        var one = Car(Ghosts.Prefix + "1", 9_000);
        var two = Car(Ghosts.Prefix + "2", 9_500);

        var field = AnnouncerControl.RacingField(new List<RacerStanding> { one, two });

        Assert.Equal(2, field.Count);
    }

    // ------------------------------------------------------------------
    //  Only real laps reach the pace watcher
    // ------------------------------------------------------------------

    [Fact]
    public void The_start_lap_is_not_a_pace_lap()
    {
        var car = Car("101", 1_400, 9_000, 9_100);

        Assert.False(AnnouncerControl.IsPaceLap(car, 0));
        Assert.True(AnnouncerControl.IsPaceLap(car, 1));
        Assert.True(AnnouncerControl.IsPaceLap(car, 2));
    }

    [Fact]
    public void A_credited_start_lap_moves_the_start_lap_to_index_one()
    {
        var car = Car("101", 0, 2_000, 9_000);
        car.StartLapCredited = true;

        Assert.False(AnnouncerControl.IsPaceLap(car, 0));
        Assert.False(AnnouncerControl.IsPaceLap(car, 1));
        Assert.True(AnnouncerControl.IsPaceLap(car, 2));
    }

    [Fact]
    public void A_lap_through_a_crash_recovery_is_not_a_pace_lap()
    {
        var car = Car("101", 1_400, 9_000, 240_000, 9_000);
        car.RecoveryLapIndexes.Add(2);

        Assert.False(AnnouncerControl.IsPaceLap(car, 2));
        Assert.True(AnnouncerControl.IsPaceLap(car, 3));
    }
}
