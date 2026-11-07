namespace HeatCaller.Tests;

/// <summary>
/// The fourth batch of race-night features: the championship "if it ended
/// now" (ChampionshipLive), the best-lap ghost on the track map, track
/// evolution and tonight's fastest laps (TonightAtTheTrack), the Big
/// Screen's between-races cards (AttractMode) and sorting heats by pace
/// (HeatSeeding).
/// </summary>
public sealed class BetweenRacesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static SessionResultEntry Result(int position, string name, int laps = 10, long fastMs = 0, params long[] lapTimes) => new()
    {
        Position = position,
        Transponder = "T-" + name,
        DriverName = name,
        Laps = laps,
        FastLapMs = fastMs,
        LapTimesMs = lapTimes.ToList(),
    };

    private static SessionRecord Race(string? roundId, DateTime startedUtc, params SessionResultEntry[] results) => new()
    {
        SessionType = RaceSessionType.Race,
        RoundId = roundId,
        StartedAtUtc = startedUtc,
        Results = results.ToList(),
    };

    // ------------------------------------------------------------------
    //  If it ended now
    // ------------------------------------------------------------------

    [Fact]
    public void If_it_ended_now_shows_the_lead_changing_hands_and_who_moved()
    {
        var season = new Season { Name = "Winter Series", AutoInclude = true, MainsOnly = false };

        // Before: Alex 10, Sam 9.
        var earlier = Race(null, DateTime.UtcNow.AddDays(-7), Result(1, "Alex"), Result(2, "Sam"));

        // On track now: Sam leads, Dan second, Alex third - Sam 19, Alex 18, Dan 9.
        var live = Race(null, DateTime.UtcNow, Result(1, "Sam"), Result(2, "Dan"), Result(3, "Alex"));

        var table = ChampionshipLive.Table(season, new[] { earlier }, live, null)!;

        Assert.Equal("Sam would take the Winter Series lead from Alex", table.Headline);
        Assert.Equal(new[] { "Sam", "Alex", "Dan" }, table.Rows.Select(r => r.DriverName));
        Assert.Equal(new[] { 19, 18, 9 }, table.Rows.Select(r => r.Points));

        Assert.Equal(1, table.Rows[0].Change);
        Assert.Equal(-1, table.Rows[1].Change);
        Assert.True(table.Rows[2].New);
        Assert.All(table.Rows, r => Assert.True(r.InThisRace));

        Assert.Equal(" ▲1", ChampionshipLive.Arrow(table.Rows[0]));
        Assert.Equal(" ▼1", ChampionshipLive.Arrow(table.Rows[1]));
        Assert.Equal(" NEW", ChampionshipLive.Arrow(table.Rows[2]));
        Assert.StartsWith("IF IT ENDED NOW:  Sam would take", ChampionshipLive.Ticker(table));
    }

    [Fact]
    public void If_it_ended_now_says_the_margin_when_the_leader_stays_on_top()
    {
        var season = new Season { Name = "Summer Cup", AutoInclude = true, MainsOnly = false };
        var earlier = Race(null, DateTime.UtcNow.AddDays(-7), Result(1, "Alex"), Result(2, "Sam"));
        var live = Race(null, DateTime.UtcNow, Result(1, "Alex"), Result(2, "Sam"));

        Assert.Equal("Alex would lead the Summer Cup by 2 points",
            ChampionshipLive.Table(season, new[] { earlier }, live, null)!.Headline);
    }

    [Fact]
    public void If_it_ended_now_only_for_a_race_the_championship_would_count()
    {
        var rounds = RoundDefinitionStore.Load();
        var main = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main };
        var lastWeek = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main };
        var heat = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 1", Kind = RoundKind.Qualifier };
        rounds.Add(main);
        rounds.Add(lastWeek);
        rounds.Add(heat);

        var seasons = SeasonStore.Load();
        seasons.Upsert(new Season { Name = "Stock Series", ClassName = "Stock", AutoInclude = true, MainsOnly = true });

        var history = SessionHistoryStore.FromSessions(new[]
        {
            Race(lastWeek.Id, DateTime.UtcNow.AddDays(-7), Result(1, "Alex"), Result(2, "Sam")),
        });

        var results = new[] { Result(1, "Sam"), Result(2, "Alex") };

        var table = ChampionshipLive.Compute(results, main, seasons, rounds, null, history);

        Assert.NotNull(table);
        Assert.Equal("Stock Series", table!.SeasonName);
        Assert.Equal("Alex and Sam would be level at the top of the Stock Series", table.Headline);

        // A heat doesn't count towards a mains-only series; nor does a race
        // nobody has a lap in yet.
        Assert.Null(ChampionshipLive.Compute(results, heat, seasons, rounds, null, history));
        Assert.Null(ChampionshipLive.Compute(new[] { Result(1, "Sam", laps: 0) }, main, seasons, rounds, null, history));
    }

    [Fact]
    public void A_filed_race_with_no_round_is_not_counted_twice()
    {
        var rounds = RoundDefinitionStore.Load();
        var seasons = SeasonStore.Load();
        seasons.Upsert(new Season { Name = "Club Points", AutoInclude = true, MainsOnly = false });

        // A freeform race, already filed: the championship check scores it
        // again as the "live" one, so it must not also be in the counted table.
        var earlier = Race(null, DateTime.UtcNow.AddDays(-7), Result(1, "Alex"), Result(2, "Sam"));
        var filed = Race(null, DateTime.UtcNow, Result(1, "Sam"), Result(2, "Alex"));
        var history = SessionHistoryStore.FromSessions(new[] { filed, earlier });

        var table = ChampionshipLive.Compute(filed.Results, null, seasons, rounds, null, history, null, filed.Id);

        Assert.NotNull(table);
        Assert.Equal("Alex and Sam would be level at the top of the Club Points", table!.Headline);
    }

    [Fact]
    public void A_heat_run_again_counts_once_in_a_championship()
    {
        var rounds = RoundDefinitionStore.Load();
        var main = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main };
        rounds.Add(main);

        var season = new Season { Name = "Stock Series", ClassName = "Stock", AutoInclude = true, MainsOnly = true };

        // The first run, then the re-run the round now names - the first put
        // back by an undo, still carrying the round's id.
        var first = Race(main.Id, DateTime.UtcNow.AddHours(-1), Result(1, "Alex"), Result(2, "Sam"));
        var again = Race(main.Id, DateTime.UtcNow, Result(1, "Sam"), Result(2, "Alex"));
        rounds.MarkRecorded(main.Id, again.Id);

        var counted = EventSpine.SessionsFor(season, null, rounds, SessionHistoryStore.FromSessions(new[] { first, again }));

        Assert.Equal(again.Id, Assert.Single(counted).Id);

        // Ticked in by hand, it still counts.
        season.IncludedSessionIds.Add(first.Id);
        Assert.Equal(2, EventSpine.SessionsFor(season, null, rounds, SessionHistoryStore.FromSessions(new[] { first, again })).Count);
    }

    [Fact]
    public void Tonights_fastest_lap_is_never_a_disqualified_or_excluded_drivers()
    {
        var q1 = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 1" };
        RoundDefinition? RoundOf(string id) => id == q1.Id ? q1 : null;
        var night = new DateTime(2026, 10, 3, 19, 0, 0, DateTimeKind.Local);

        var dq = Result(1, "Dee", 10, 18_000);
        dq.Outcome = ResultEditor.Dq;

        var history = new[]
        {
            Race(q1.Id, night.ToUniversalTime(), dq, Result(2, "Guest", 10, 18_500), Result(3, "Sam", 10, 19_000)),
        };

        var fastest = Assert.Single(TonightAtTheTrack.FastestLaps(history, RoundOf, night, t => t == "T-Guest"));
        Assert.Equal("Sam", fastest.DriverName);
    }

    // ------------------------------------------------------------------
    //  The best-lap ghost
    // ------------------------------------------------------------------

    [Fact]
    public void The_ghost_runs_at_the_best_lap_and_waits_at_the_line()
    {
        Assert.Equal(0.5f, TrackPositions.GhostAt(lineAt: 1000, bestMs: 8000, nowMs: 5000), 3);
        Assert.Equal(1f, TrackPositions.GhostAt(lineAt: 1000, bestMs: 8000, nowMs: 20_000), 3);
        Assert.Equal(-1f, TrackPositions.GhostAt(lineAt: 1000, bestMs: 0, nowMs: 5000));
    }

    [Fact]
    public void A_car_carries_its_ghost_from_its_best_timed_lap()
    {
        var design = new RcpTrackDesign { RouteClosed = true };

        foreach (var (x, y) in new[] { (0f, 0f), (10f, 0f), (10f, 10f), (0f, 10f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        design.Decals.Add(new RcpDecal { Kind = RcpDecalKind.FinishLine, Col = 4, Row = 0 });
        var line = TrackLine.From(design)!;

        // The start lap (6s, from the green) is not a timed lap: the best is 8s.
        var car = new RacerStanding { TransponderId = "A", Laps = 3 };
        car.LapTimesMs.AddRange(new long[] { 6000, 9000, 8000 });

        var positions = new TrackPositions();
        positions.Crossed("A", TimingPoint.Line, 1000);

        var placed = Assert.Single(positions.Estimate(new[] { car }, line, 3000, racing: true));
        Assert.Equal(0.25f, placed.Ghost, 3);
        Assert.Equal(0.125f, placed.GhostRate, 4);

        // No timed lap yet: no ghost.
        var fresh = new RacerStanding { TransponderId = "B", Laps = 1 };
        fresh.LapTimesMs.Add(6000);
        positions.Crossed("B", TimingPoint.Line, 1000);

        var cars = positions.Estimate(new[] { car, fresh }, line, 3000, racing: true);
        Assert.Equal(-1f, cars.Single(c => c.Transponder == "B").Ghost);
    }

    // ------------------------------------------------------------------
    //  Tonight at the track
    // ------------------------------------------------------------------

    /// <summary>Three drivers' laps around <paramref name="middleMs"/>: a start lap, then six flying laps each. The first is <paramref name="fastest"/>, the others "Driver 2" and "Driver 3".</summary>
    private static SessionResultEntry[] Field(long middleMs, string fastest = "Alex", long fastMs = 0) =>
        Enumerable.Range(1, 3).Select(i => Result(i, i == 1 ? fastest : $"Driver {i}", 7, i == 1 && fastMs > 0 ? fastMs : middleMs - 100,
            new[] { 5000L, middleMs - 200, middleMs - 100, middleMs, middleMs, middleMs + 100, middleMs + 200 })).ToArray();

    [Fact]
    public void Track_evolution_compares_a_class_now_with_its_first_race_tonight()
    {
        var q1 = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1" };
        var q2 = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q2 Heat 1" };
        var yesterday = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1" };
        var rounds = new[] { q1, q2, yesterday }.ToDictionary(r => r.Id);
        RoundDefinition? RoundOf(string id) => rounds.GetValueOrDefault(id);

        var night = new DateTime(2026, 10, 3, 19, 0, 0, DateTimeKind.Local);

        var history = new[]
        {
            Race(yesterday.Id, night.AddDays(-1).ToUniversalTime(), Field(18_000)),
            Race(q1.Id, night.ToUniversalTime(), Field(20_000)),

            // Past midnight - still tonight.
            Race(q2.Id, night.AddHours(5).ToUniversalTime(), Field(19_700, "Sam", 19_100)),
        };

        var evolution = Assert.Single(TonightAtTheTrack.Evolution(history, RoundOf, night.AddHours(6)));

        // Driver 2 and Driver 3 raced both: 0.3s quicker each. Alex and Sam
        // raced once each tonight, so say nothing either way.
        Assert.Equal("Stock", evolution.ClassName);
        Assert.Equal(-300, evolution.DeltaMs);
        Assert.Equal(2, evolution.Drivers);
        Assert.Equal(2, evolution.Races);
        Assert.Equal("Q2 Heat 1", evolution.LatestRace);
        Assert.Equal("Track 0.30s quicker since Q1 Heat 1", evolution.Words);

        var fastest = Assert.Single(TonightAtTheTrack.FastestLaps(history, RoundOf, night.AddHours(6)));
        Assert.Equal("Sam", fastest.DriverName);
        Assert.Equal(19_100, fastest.LapMs);
        Assert.Equal("19.100", TonightAtTheTrack.Lap(fastest.LapMs));

        Assert.Equal("Track 0.12s slower since Q1 Heat 1",
            new TrackEvolution("Stock", "Q1 Heat 1", "Q2 Heat 1", 120, 2, 2).Words);
        Assert.Equal("Track the same as in Q1 Heat 1",
            new TrackEvolution("Stock", "Q1 Heat 1", "Q2 Heat 1", 3, 2, 2).Words);
    }

    [Fact]
    public void One_race_different_drivers_or_too_few_laps_says_nothing_about_the_track()
    {
        var q1 = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 1" };
        var q2 = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 3" };
        RoundDefinition? RoundOf(string id) => id == q1.Id ? q1 : id == q2.Id ? q2 : null;
        var night = new DateTime(2026, 10, 3, 19, 0, 0, DateTimeKind.Local);

        Assert.Empty(TonightAtTheTrack.Evolution(new[] { Race(q1.Id, night.ToUniversalTime(), Field(20_000)) }, RoundOf, night));

        // Two heats with nobody in both - a quicker heat is quicker drivers,
        // not a quicker track.
        var others = Field(19_000).Select(r => { r.Transponder += "-other"; return r; }).ToArray();
        Assert.Empty(TonightAtTheTrack.Evolution(new[]
        {
            Race(q1.Id, night.ToUniversalTime(), Field(20_000)),
            Race(q2.Id, night.AddMinutes(20).ToUniversalTime(), others),
        }, RoundOf, night.AddHours(1)));

        Assert.Equal(0, TonightAtTheTrack.MiddleLap(Result(1, "Alex", 3, 0, 5000, 20_000, 20_000)));
    }

    // ------------------------------------------------------------------
    //  Between races on the Big Screen
    // ------------------------------------------------------------------

    [Fact]
    public void The_cards_come_up_between_races_but_not_over_a_result_just_in()
    {
        Assert.True(AttractMode.Showing(RaceState.Idle, TimeSpan.Zero, false, false, false));
        Assert.False(AttractMode.Showing(RaceState.Finished, TimeSpan.FromSeconds(10), false, false, false));
        Assert.True(AttractMode.Showing(RaceState.Finished, AttractMode.ResultTime, false, false, false));

        Assert.False(AttractMode.Showing(RaceState.Racing, TimeSpan.Zero, false, false, false));
        Assert.False(AttractMode.Showing(RaceState.Staging, TimeSpan.Zero, false, false, false));
        Assert.False(AttractMode.Showing(RaceState.Idle, TimeSpan.Zero, warmingUp: true, false, false));
        Assert.False(AttractMode.Showing(RaceState.Idle, TimeSpan.Zero, false, practice: true, false));
        Assert.False(AttractMode.Showing(RaceState.Idle, TimeSpan.Zero, false, false, demo: true));
    }

    [Fact]
    public void Each_card_is_there_only_when_it_has_something_to_say()
    {
        var upcoming = new[]
        {
            new UpcomingRace("Q1 Heat 1 (Stock)", new[] { "1", "2" }, OnTrack: false),
            new UpcomingRace("Q1 Heat 2 (Stock)", new[] { "3" }, OnTrack: false),
        };

        var cards = AttractMode.Cards(upcoming, t => "Driver " + t,
            new[] { new FastestTonight("Stock", "Sam", 19_100, "Q1 Heat 1") },
            Array.Empty<TrackEvolution>(),
            new ChampionshipTop("Winter Series", new[] { ("Alex", 40), ("Sam", 38) }, 4));

        Assert.Equal(new[] { "UP NEXT - Q1 Heat 1 (Stock)", "TONIGHT'S FASTEST LAPS", "CHAMPIONSHIP - WINTER SERIES" },
            cards.Select(c => c.Title));

        Assert.Equal(new[] { "1.  Driver 1", "2.  Driver 2" }, cards[0].Lines.Select(l => l.Left));
        Assert.Equal("Then: Q1 Heat 2 (Stock)", cards[0].Footer);
        Assert.Equal("19.100", cards[1].Lines[0].Right);
        Assert.Equal("40 pts", cards[2].Lines[0].Right);
        Assert.Equal("After 4 races", cards[2].Footer);

        Assert.Empty(AttractMode.Cards(Array.Empty<UpcomingRace>(), t => t,
            Array.Empty<FastestTonight>(), Array.Empty<TrackEvolution>(), null));
    }

    // ------------------------------------------------------------------
    //  Heats by pace
    // ------------------------------------------------------------------

    private static readonly Dictionary<string, long> Pace = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 20_000, ["B"] = 21_000, ["C"] = 22_000, ["D"] = 23_000, ["E"] = 24_000,
    };

    [Fact]
    public void Group_by_pace_puts_the_fastest_together_in_the_last_heat()
    {
        var heats = HeatSeeding.Group(new[] { 3, 3 }, new[] { "F", "E", "D", "C", "B", "A" }, Pace);

        Assert.Equal(new[] { "D", "E", "F" }, heats[0]);
        Assert.Equal(new[] { "A", "B", "C" }, heats[1]);
    }

    [Fact]
    public void Spread_by_pace_snake_drafts_and_keeps_each_heat_its_size()
    {
        var even = HeatSeeding.Spread(new[] { 3, 3 }, new[] { "F", "E", "D", "C", "B", "A" }, Pace);

        Assert.Equal(new[] { "A", "D", "E" }, even[0]);
        Assert.Equal(new[] { "B", "C", "F" }, even[1]);

        var uneven = HeatSeeding.Spread(new[] { 2, 4 }, new[] { "A", "B", "C", "D", "E", "F" }, Pace);

        Assert.Equal(new[] { "A", "D" }, uneven[0]);
        Assert.Equal(new[] { "B", "C", "E", "F" }, uneven[1]);
    }

    [Fact]
    public void Pace_is_recent_form_in_the_class()
    {
        var stock = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 1" };
        var mod = new RoundDefinition { ClassName = "Mod", RoundLabel = "Heat 1" };
        RoundDefinition? RoundOf(string id) => id == stock.Id ? stock : id == mod.Id ? mod : null;

        var history = new[]
        {
            Race(stock.Id, DateTime.UtcNow.AddDays(-3), Result(1, "Alex", 10, 19_500), Result(2, "Sam", 10, 20_000)),
            Race(stock.Id, DateTime.UtcNow.AddDays(-1), Result(1, "Alex", 10, 19_900)),

            // Another class, and last season: neither counts.
            Race(mod.Id, DateTime.UtcNow.AddDays(-1), Result(1, "Sam", 10, 15_000)),
            Race(stock.Id, DateTime.UtcNow.AddDays(-300), Result(1, "Sam", 10, 18_000)),
        };

        var best = HeatSeeding.BestLaps("Stock", history, RoundOf, DateTime.UtcNow - HeatSeeding.RecentForm);

        Assert.Equal(19_500, best["T-Alex"]);
        Assert.Equal(20_000, best["T-Sam"]);
    }
}
