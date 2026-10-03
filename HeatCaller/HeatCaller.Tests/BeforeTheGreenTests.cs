namespace HeatCaller.Tests;

/// <summary>
/// Batch one of the race-night features: the transponder health record
/// (TransponderHealthStore), Mission Control's checks (MissionControl) and
/// the Big Screen's grid roll call (RollCall).
/// </summary>
public sealed class TransponderHealthTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static DirectLapEvent Pass(string id, int strength, int hits = 8) =>
        new() { TransponderId = id, SignalStrength = strength, SignalHits = hits };

    /// <summary>A night of <paramref name="passes"/> passes at <paramref name="strength"/>, on the evening of <paramref name="day"/>.</summary>
    private static void Night(TransponderHealthStore store, string id, DateTime day, int strength, int passes = 10, int hits = 8)
    {
        for (var i = 0; i < passes; i++)
        {
            store.Record(Pass(id, strength, hits), day.Date.AddHours(19).AddMinutes(i));
        }
    }

    [Fact]
    public void A_night_past_midnight_is_still_one_night()
    {
        Assert.Equal("2026-10-03", TransponderHealthStore.NightOf(new DateTime(2026, 10, 3, 19, 0, 0)));
        Assert.Equal("2026-10-03", TransponderHealthStore.NightOf(new DateTime(2026, 10, 4, 1, 30, 0)));
        Assert.Equal("2026-10-04", TransponderHealthStore.NightOf(new DateTime(2026, 10, 4, 6, 0, 0)));
    }

    [Fact]
    public void A_transponder_reading_a_third_weaker_than_usual_is_fading()
    {
        var store = TransponderHealthStore.InMemory();
        var day = new DateTime(2026, 9, 1);

        Night(store, "4385919", day, 120);
        Night(store, "4385919", day.AddDays(7), 118);
        Night(store, "4385919", day.AddDays(14), 122);
        Night(store, "4385919", day.AddDays(21), 80);

        var health = store.Assess("4385919");

        Assert.Equal(TransponderHealthStore.Verdict.Fading, health.Verdict);
        Assert.InRange(health.Drop, 0.32, 0.35);
        Assert.Contains("weaker than usual", health.Summary);
    }

    [Fact]
    public void One_that_reads_as_it_always_has_is_good()
    {
        var store = TransponderHealthStore.InMemory();
        var day = new DateTime(2026, 9, 1);

        foreach (var week in new[] { 0, 7, 14, 21 })
        {
            Night(store, "84FBD4", day.AddDays(week), 110 + week % 3);
        }

        Assert.Equal(TransponderHealthStore.Verdict.Good, store.Assess("84FBD4").Verdict);
    }

    [Fact]
    public void Barely_caught_passes_from_a_clean_transponder_count_as_fading()
    {
        var store = TransponderHealthStore.InMemory();
        var day = new DateTime(2026, 9, 1);

        Night(store, "84C1C4", day, 100);
        Night(store, "84C1C4", day.AddDays(7), 100);
        Night(store, "84C1C4", day.AddDays(14), 100);

        // Same strength, but half the passes only just caught.
        for (var i = 0; i < 10; i++)
        {
            store.Record(Pass("84C1C4", 100, hits: i % 2 == 0 ? 1 : 8), day.AddDays(21).AddHours(19).AddMinutes(i));
        }

        var health = store.Assess("84C1C4");

        Assert.Equal(TransponderHealthStore.Verdict.Fading, health.Verdict);
        Assert.Contains("barely caught", health.Summary);
    }

    [Fact]
    public void Too_few_nights_or_readings_say_nothing()
    {
        var store = TransponderHealthStore.InMemory();
        var day = new DateTime(2026, 9, 1);

        Night(store, "A", day, 120);
        Night(store, "A", day.AddDays(7), 60);
        Assert.Equal(TransponderHealthStore.Verdict.NotEnoughData, store.Assess("A").Verdict);

        // Four good nights, then a night of two laps - not enough to judge
        // on, so the last good night is the one judged.
        Night(store, "B", day, 120);
        Night(store, "B", day.AddDays(7), 120);
        Night(store, "B", day.AddDays(14), 120);
        Night(store, "B", day.AddDays(21), 120);
        Night(store, "B", day.AddDays(28), 40, passes: 2);
        Assert.Equal(TransponderHealthStore.Verdict.Good, store.Assess("B").Verdict);
    }

    [Fact]
    public void Passes_with_no_reading_and_ghosts_are_not_recorded()
    {
        var store = TransponderHealthStore.InMemory();
        var day = new DateTime(2026, 9, 1);

        for (var i = 0; i < 10; i++)
        {
            // An I-Lap's pass: no signal reading at all.
            store.Record(new DirectLapEvent { TransponderId = "4", SignalHits = 0 }, day.AddHours(19));
            store.Record(new DirectLapEvent { TransponderId = "G1", SignalHits = 8, SignalStrength = 9, IsSynthetic = true }, day.AddHours(19));
        }

        Assert.Null(store.Assess("4").Latest);
        Assert.Null(store.Assess("G1").Latest);
    }

    [Fact]
    public void Only_recent_fading_transponders_are_listed()
    {
        var store = TransponderHealthStore.InMemory();
        var day = new DateTime(2026, 3, 1);

        foreach (var id in new[] { "OLD", "NEW" })
        {
            var start = id == "OLD" ? day : day.AddMonths(6);
            Night(store, id, start, 120);
            Night(store, id, start.AddDays(7), 120);
            Night(store, id, start.AddDays(14), 120);
            Night(store, id, start.AddDays(21), 60);
        }

        var fading = store.Fading(TimeSpan.FromDays(31), day.AddMonths(6).AddDays(22));

        Assert.Equal(new[] { "NEW" }, fading.Select(h => h.Transponder));
    }

    [Fact]
    public void The_record_survives_a_restart()
    {
        var store = TransponderHealthStore.Load();
        var day = new DateTime(2026, 9, 1);

        Night(store, "4385919", day, 120);
        store.Save();

        var again = TransponderHealthStore.Load();

        Assert.Equal(10, again.Assess("4385919").Latest?.Readings);
        Assert.True(File.Exists(_data.PathOf("transponder-health.json")));
    }

    [Fact]
    public void Each_transponder_keeps_only_its_last_twenty_nights()
    {
        var store = TransponderHealthStore.InMemory();
        var day = new DateTime(2026, 1, 1);

        for (var night = 0; night < 30; night++)
        {
            Night(store, "A", day.AddDays(night), 100);
        }

        Assert.Equal(TransponderHealthStore.NightsKept, store.Assess("A").NightsOnRecord);
    }
}

public sealed class MissionControlTests
{
    private static MissionControl.Inputs AllWell() => new(
        LineDecoder: "EasyLap",
        LineState: DecoderConnectionState.Connected,
        HeartbeatAge: TimeSpan.FromMilliseconds(400),
        LastPassAge: TimeSpan.FromSeconds(3),
        SplitState: null,
        CarsHeard: 6,
        WeakCars: Array.Empty<string>(),
        Roster: new[] { "Asher", "Frank" },
        RosterMissing: Array.Empty<string>(),
        TimingProblem: null,
        Fading: Array.Empty<string>(),
        Relay: null,
        VoiceMuted: false);

    private static MissionControl.Tile Tile(MissionControl.Inputs inputs, string key) =>
        MissionControl.Assess(inputs).Single(t => t.Key == key);

    [Fact]
    public void A_healthy_night_is_all_clear()
    {
        var tiles = MissionControl.Assess(AllWell());

        Assert.DoesNotContain(tiles, t => t.Level is MissionControl.Level.Problem or MissionControl.Level.Watch);
        Assert.Equal("All clear. Ready to race.", MissionControl.Headline(tiles));
    }

    [Fact]
    public void An_unplugged_decoder_is_a_problem_and_its_heartbeat_is_not_judged()
    {
        var inputs = AllWell() with { LineState = DecoderConnectionState.Disconnected, HeartbeatAge = null };

        Assert.Equal(MissionControl.Level.Problem, Tile(inputs, "decoder").Level);
        Assert.Equal(MissionControl.Level.Off, Tile(inputs, "heartbeat").Level);
        Assert.StartsWith("1 problem", MissionControl.Headline(MissionControl.Assess(inputs)));
    }

    [Fact]
    public void A_late_heartbeat_is_a_problem()
    {
        var inputs = AllWell() with { HeartbeatAge = TimeSpan.FromSeconds(7) };

        Assert.Equal(MissionControl.Level.Problem, Tile(inputs, "heartbeat").Level);
        Assert.Contains("7s", Tile(inputs, "heartbeat").Value);
    }

    [Fact]
    public void An_ilap_sends_no_heartbeat_and_is_not_blamed_for_it()
    {
        var inputs = AllWell() with { LineDecoder = "I-Lap", HeartbeatAge = null };

        Assert.Equal(MissionControl.Level.Off, Tile(inputs, "heartbeat").Level);
    }

    [Fact]
    public void Cars_not_heard_on_the_grid_are_named()
    {
        var inputs = AllWell() with { Roster = new[] { "Asher", "Frank", "Bill" }, RosterMissing = new[] { "Bill" } };
        var grid = Tile(inputs, "grid");

        Assert.Equal(MissionControl.Level.Watch, grid.Level);
        Assert.Equal("2 of 3", grid.Value);
        Assert.Contains("Bill", grid.Detail);
    }

    [Fact]
    public void Fading_transponders_and_a_muted_announcer_are_worth_a_look()
    {
        var inputs = AllWell() with { Fading = new[] { "Frank - reading 32% weaker than usual" }, VoiceMuted = true };

        Assert.Equal(MissionControl.Level.Watch, Tile(inputs, "health").Level);
        Assert.Contains("Frank", Tile(inputs, "health").Detail);
        Assert.Equal(MissionControl.Level.Watch, Tile(inputs, "sound").Level);
    }

    [Fact]
    public void A_timing_warning_is_a_problem()
    {
        var inputs = AllWell() with { TimingProblem = "No car has crossed the line for 40s - check the loop and its cable." };

        Assert.Equal(MissionControl.Level.Problem, Tile(inputs, "timing").Level);
    }

    [Fact]
    public void The_website_is_off_unless_the_club_publishes()
    {
        Assert.Equal(MissionControl.Level.Off, Tile(AllWell(), "website").Level);

        var offline = AllWell() with { Relay = new RelayStatus(true, true, false, DateTime.UtcNow.AddMinutes(-10), 3, 0, "Timed out.") };

        Assert.Equal(MissionControl.Level.Watch, Tile(offline, "website").Level);
        Assert.Contains("3 updates waiting", Tile(offline, "website").Detail);
    }

    [Fact]
    public void The_window_shows_every_check()
    {
        var shown = StaThread.Run(() =>
        {
            using var form = new MissionControlForm(() => MissionControl.Assess(AllWell()));
            return form.Text;
        }, TimeSpan.FromSeconds(30), () => "building Mission Control");

        Assert.Equal("Mission Control", shown);
    }
}

public sealed class RollCallTests
{
    private static DirectLapEvent Pass(string id, int hits = 8) => new() { TransponderId = id, SignalHits = hits, SignalStrength = 100 };

    [Theory]
    [InlineData(RaceState.Staging, false, true)]
    [InlineData(RaceState.Countdown, false, true)]
    [InlineData(RaceState.Idle, true, true)]
    [InlineData(RaceState.Racing, false, false)]
    [InlineData(RaceState.Idle, false, false)]
    [InlineData(RaceState.Finished, false, false)]
    public void It_shows_from_staging_until_the_green(RaceState state, bool warmingUp, bool expected) =>
        Assert.Equal(expected, RollCall.Showing(state, warmingUp, practice: false, demo: false, rosterCount: 4));

    [Fact]
    public void Not_in_practice_a_demo_or_with_nobody_to_call()
    {
        Assert.False(RollCall.Showing(RaceState.Staging, false, practice: true, demo: false, rosterCount: 4));
        Assert.False(RollCall.Showing(RaceState.Staging, false, practice: false, demo: true, rosterCount: 4));
        Assert.False(RollCall.Showing(RaceState.Staging, false, practice: false, demo: false, rosterCount: 0));
    }

    [Fact]
    public void Cars_heard_since_the_roll_call_began_are_ticked_off()
    {
        var watch = new TransponderWatch();
        watch.Record(Pass("A"));
        watch.Record(Pass("B", hits: 1));

        var entries = RollCall.Build(new[] { "A", "B", "C", "A" }, watch,
            t => t == "A" ? "Asher" : null, DateTime.UtcNow.AddMinutes(-1));

        Assert.Equal(new[] { "Asher", "B", "C" }, entries.Select(e => e.Name));
        Assert.Equal(new[] { RollCall.State.Heard, RollCall.State.Weak, RollCall.State.Waiting }, entries.Select(e => e.State));
        Assert.Equal("ROLL CALL - 2 OF 3 CARS HEARD", RollCall.Headline(entries));
    }

    [Fact]
    public void A_car_heard_before_the_roll_call_began_is_still_waiting()
    {
        var watch = new TransponderWatch();
        watch.Record(Pass("A"));

        var entries = RollCall.Build(new[] { "A" }, watch, _ => null, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(RollCall.State.Waiting, entries.Single().State);
    }

    [Fact]
    public void The_cards_are_laid_out_as_big_as_the_screen_allows()
    {
        // Eight cards on a 16:9 wall: three to a row makes them biggest,
        // never one long row of slivers.
        Assert.Equal(3, RollCallBoard.BestColumns(8, new Size(1600, 900), 10));
        Assert.Equal(1, RollCallBoard.BestColumns(1, new Size(1600, 900), 10));
    }
}
