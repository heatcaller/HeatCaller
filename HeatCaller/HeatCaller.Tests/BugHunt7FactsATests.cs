using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the commentary's facts. What the hunt found:
///
///  - The grid's history counted a DNF or a DQ as a finish, a podium and
///    even a win, because every saved row has a place; and a ghost left in
///    an older file put every real driver behind it a place down.
///  - A driver's "own quickest lap on this layout is" was said after they
///    had already gone quicker tonight.
///  - The championship check said "still leads" at a season's first round,
///    and "takes over the lead from" of two drivers level on points.
///  - The layout record's "races since" counted the race it was set in.
///  - "That was Q1 Heat 1" was said when a newer race filed against no
///    round was the one the room had just watched.
///  - The championship conditional pinned the total of a car on the grid
///    with no profile name, which can in fact score in the race.
///
/// The B-main's recap reading the A-main's run of mains is a one-line fix
/// in Race Control, which needs a whole window to drive, so it has no test
/// here.
/// </summary>
public sealed class BugHunt7FactsATests : IDisposable
{
    // Declared first: every store below loads from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>Seven in the evening, which NightOf reads as that day's race night.</summary>
    private static readonly DateTime Now = new(2026, 10, 11, 19, 0, 0, DateTimeKind.Local);

    private const string Oval = "Club Oval";

    private static SessionResultEntry Row(string transponder, int position, int laps = 12, string outcome = "", long fastLapMs = 0) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        Laps = laps,
        Outcome = outcome,
        FastLapMs = fastLapMs,
    };

    private static SessionRecord Race(DateTime endedLocal, string? roundId = null, params SessionResultEntry[] results) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionType = RaceSessionType.Race,
        RoundId = roundId,
        TrackLayoutName = Oval,
        StartedAtUtc = endedLocal.ToUniversalTime().AddMinutes(-6),
        EndedAtUtc = endedLocal.ToUniversalTime(),
        Results = results.ToList(),
    };

    private static ProfileStore Named(params (string Transponder, string Name)[] drivers)
    {
        var profiles = ProfileStore.Load();

        foreach (var (transponder, name) in drivers)
        {
            profiles.Upsert(new DriverProfile { Transponder = transponder, DriverName = name });
        }

        return profiles;
    }

    // ------------------------------------------------------------------
    // The grid's history: a DNF or a DQ is not a finish
    // ------------------------------------------------------------------

    [Fact]
    public void A_dnf_or_a_dq_counts_as_a_start_but_never_as_a_finish_a_podium_or_a_win()
    {
        var profiles = Named(("A", "Sam"), ("B", "Bill"), ("C", "Carl"));

        var history = SessionHistoryStore.FromSessions(new[]
        {
            // Carl retired after 12 laps and Edit Result put him third.
            Race(Now.AddDays(-7), null, Row("A", 1), Row("B", 2), Row("C", 3, outcome: "DNF")),

            // And a week before, Carl was disqualified from the win.
            Race(Now.AddDays(-14), null, Row("C", 1, laps: 0, outcome: "DQ"), Row("A", 2), Row("B", 3)),
        });

        var (grid, rivalries) = CommentaryReaders.GridHistory(
            new[] { "A", "B", "C" }, null, profiles, history, RoundDefinitionStore.Load(), DateTime.UtcNow);

        var carl = grid.Single(d => d.DriverId == "C");

        Assert.Equal(2, carl.RacesHere);
        Assert.Equal(0, carl.WinsHere);
        Assert.Equal(0, carl.PodiumsHere);
        Assert.Equal(new[] { 0, 0 }, carl.RecentFinishes!.ToArray());

        // A retirement says nothing about who is quicker, so Carl has no
        // head-to-head record against anybody.
        Assert.DoesNotContain(rivalries, pair => pair.AId == "C" || pair.BId == "C");
    }

    [Fact]
    public void A_ghost_left_in_an_old_result_does_not_put_the_real_drivers_a_place_down()
    {
        var profiles = Named(("A", "Sam"), ("B", "Bill"));

        var history = SessionHistoryStore.FromSessions(new[]
        {
            Race(Now.AddDays(-7), null, Row(Ghosts.Prefix + "1", 1), Row("A", 2), Row("B", 3)),
        });

        var (grid, _) = CommentaryReaders.GridHistory(
            new[] { "A", "B" }, null, profiles, history, RoundDefinitionStore.Load(), DateTime.UtcNow);

        Assert.Equal(1, grid.Single(d => d.DriverId == "A").WinsHere);
        Assert.Equal(new[] { 2 }, grid.Single(d => d.DriverId == "B").RecentFinishes!.ToArray());
    }

    [Fact]
    public void A_driver_stored_first_in_a_race_every_car_retired_from_is_neither_a_winner_nor_beaten_by_themselves()
    {
        // Dave has won the last race of the club's last five nights.
        var races = new List<SessionRecord>();
        var field = new[] { "dave", "a", "b", "c", "d", "e" };

        for (var week = 1; week <= 5; week++)
        {
            races.Add(Race(Now.AddDays(-7 * week), null, field.Select((t, i) => Row(t, i + 1)).ToArray()));
        }

        // Tonight every car retired, so Dave is still stored first.
        races.Add(Race(Now.AddHours(-1), null, field.Select((t, i) => Row(t, i + 1, laps: 3, outcome: "DNF")).ToArray()));

        var rounds = RoundDefinitionStore.Load();

        rounds.Add(new RoundDefinition
        {
            ClassName = "Box Stock",
            RoundLabel = "A-Main",
            Kind = RoundKind.Main,
            AssignedTransponders = new[] { "dave", "a", "b" }.ToList(),
        });

        var story = CommentaryReaders.NightStory(
            rounds,
            SessionHistoryStore.FromSessions(races),
            Named(("dave", "Dave"), ("a", "Ann"), ("b", "Bill")),
            nowLocal: Now);

        Assert.NotNull(story);
        Assert.Equal(1, story!.StartedTonight);
        Assert.Equal(0, story.WonTonight);

        // Not "Dave has already beaten them tonight" said of Dave.
        Assert.Null(story.BeatenBy);
        Assert.Null(CommentaryShowFacts.ThreadSoFar(story, "night-1"));
    }

    // ------------------------------------------------------------------
    // A driver's own best on a layout, already beaten tonight
    // ------------------------------------------------------------------

    private static SessionHistoryStore FourNightsAndTonight(long tonightsLapMs) =>
        SessionHistoryStore.FromSessions(new[]
        {
            Race(Now.AddDays(-28), null, Row("A", 1, fastLapMs: 11_500), Row("B", 2, fastLapMs: 11_600)),
            Race(Now.AddDays(-21), null, Row("A", 1, fastLapMs: 11_450), Row("B", 2, fastLapMs: 11_600)),
            Race(Now.AddDays(-14), null, Row("A", 1, fastLapMs: 11_402), Row("B", 2, fastLapMs: 11_600)),
            Race(Now.AddDays(-7), null, Row("A", 1, fastLapMs: 11_480), Row("B", 2, fastLapMs: 11_600)),
            Race(Now.AddHours(-1), null, Row("A", 1, fastLapMs: tonightsLapMs), Row("B", 2, fastLapMs: 11_600)),
        });

    [Fact]
    public void A_best_already_beaten_tonight_keeps_its_figure_but_is_not_said()
    {
        var legend = CommentaryReaders.LayoutStory(
            FourNightsAndTonight(11_300), Named(("A", "Sam"), ("B", "Bill")), Oval, new[] { "A" }, Now);

        Assert.NotNull(legend);

        var sam = legend!.Bests.Single();

        // Still what he had to beat when he arrived - tonight is not history.
        Assert.Equal(11.402, sam.BestSeconds, 3);
        Assert.True(sam.BeatenTonight);

        // And "his quickest lap here is 11.402" after a 11.300 is false.
        Assert.Null(CommentaryRaceFacts.BestOnThisLayout(sam, Oval, "night-1"));
    }

    [Fact]
    public void A_best_not_beaten_tonight_is_still_said()
    {
        var legend = CommentaryReaders.LayoutStory(
            FourNightsAndTonight(11_402), Named(("A", "Sam"), ("B", "Bill")), Oval, new[] { "A" }, Now);

        var sam = legend!.Bests.Single();

        // Equalled is not beaten.
        Assert.False(sam.BeatenTonight);
        Assert.NotNull(CommentaryRaceFacts.BestOnThisLayout(sam, Oval, "night-1"));
    }

    // ------------------------------------------------------------------
    // The championship check
    // ------------------------------------------------------------------

    private static ChampionshipStanding C(string name, int now, int before, int points, int behind) =>
        new(name, name, PositionNow: now, PositionBefore: before, Points: points, BehindLeader: behind);

    [Fact]
    public void Two_drivers_level_on_points_are_level_whichever_way_the_tie_break_fell()
    {
        var blocks = CommentaryReportFacts.ChampionshipCheck(
            new[] { C("Carl", 1, 2, 40, 0), C("Alex", 2, 1, 40, 0) },
            "Stock",
            "r-1");

        Assert.Equal("Carl and Alex are level at the top of the Stock championship on 40 points.", blocks[0].Text);
        Assert.DoesNotContain(blocks, block => block.Text.Contains("takes over", StringComparison.Ordinal));
    }

    [Fact]
    public void Three_level_at_the_top_are_counted_rather_than_two_named()
    {
        var blocks = CommentaryReportFacts.ChampionshipCheck(
            new[] { C("Carl", 1, 1, 40, 0), C("Alex", 2, 2, 40, 0), C("Dot", 3, 3, 40, 0) },
            "Stock",
            "r-1");

        Assert.Equal("3 drivers are level at the top of the Stock championship on 40 points.", blocks[0].Text);
    }

    [Fact]
    public void The_seasons_first_round_has_a_leader_but_nobody_still_leading()
    {
        var blocks = CommentaryReportFacts.ChampionshipCheck(
            new[] { C("Sam", 1, 0, 25, 0), C("Bill", 2, 0, 20, 5) },
            "Stock",
            "r-1");

        Assert.Equal("Sam leads the Stock championship on 25 points, 5 clear of Bill.", blocks[0].Text);
    }

    [Fact]
    public void A_driver_new_to_the_table_who_goes_top_takes_over_from_the_leader()
    {
        var blocks = CommentaryReportFacts.ChampionshipCheck(
            new[] { C("Sam", 1, 0, 30, 0), C("Bill", 2, 1, 28, 2) },
            "Stock",
            "r-1");

        Assert.Equal("Sam takes over the lead of the Stock championship from Bill, on 30 points.", blocks[0].Text);
    }

    [Fact]
    public void The_reader_says_a_driver_new_to_the_table_stood_nowhere_before()
    {
        var table = new IfItEndedNow(
            "Winter Series",
            new[]
            {
                new LiveChampionRow(1, "Sam", 30, 0, true, true),
                new LiveChampionRow(2, "Bill", 28, -1, false, true),
            },
            string.Empty);

        var standings = CommentaryReaders.Championship(table);

        Assert.Equal(0, standings.Single(s => s.Name == "Sam").PositionBefore);
        Assert.Equal(1, standings.Single(s => s.Name == "Bill").PositionBefore);
    }

    // ------------------------------------------------------------------
    // The layout record's races since
    // ------------------------------------------------------------------

    [Fact]
    public void The_race_the_record_was_set_in_is_not_a_race_since()
    {
        var setIn = Race(Now.AddDays(-14), null, Row("A", 1));

        var layouts = TrackLayoutStore.Load();

        layouts.Upsert(new TrackLayout
        {
            Name = Oval,
            RecordLapMs = 11_150,
            RecordHolderName = "Bill",

            // Stamped live, on lap 7, while the race was still running.
            RecordSetUtc = setIn.StartedAtUtc.AddMinutes(3),
        });

        var history = SessionHistoryStore.FromSessions(new[]
        {
            Race(Now.AddDays(-21), null, Row("A", 1)),
            setIn,
            Race(Now.AddDays(-7), null, Row("A", 1)),
            Race(Now.AddDays(-1), null, Row("A", 1)),
        });

        var record = CommentaryReaders.LayoutRecord(layouts, history, Oval, Now);

        Assert.NotNull(record);
        Assert.Equal(4, record!.RacesOnLayout);
        Assert.Equal(2, record.RacesSince);
    }

    // ------------------------------------------------------------------
    // What has just run
    // ------------------------------------------------------------------

    [Fact]
    public void A_newer_race_filed_against_no_round_silences_what_has_just_run()
    {
        var rounds = RoundDefinitionStore.Load();
        var heat = new RoundDefinition { ClassName = "Open", RoundLabel = "Q1 Heat 1" };

        rounds.Add(heat);

        var nowUtc = Now.ToUniversalTime();

        var history = SessionHistoryStore.FromSessions(new[]
        {
            Race(Now.AddMinutes(-12), heat.Id, Row("A", 1)),

            // The freeform race the room has just watched.
            Race(Now.AddMinutes(-2), null, Row("B", 1)),
        });

        Assert.Null(CommentaryReaders.JustRan(rounds, history, now: nowUtc));

        // Without it, the heat is what has just run.
        var heatOnly = SessionHistoryStore.FromSessions(new[] { Race(Now.AddMinutes(-12), heat.Id, Row("A", 1)) });

        Assert.Equal("Q1 Heat 1", CommentaryReaders.JustRan(rounds, heatOnly, now: nowUtc)?.Label);
    }

    // ------------------------------------------------------------------
    // The championship conditional and a car nobody has named
    // ------------------------------------------------------------------

    /// <summary>
    /// CommentaryConditionalTests' club: Bill 61, Carl 58, Dot 55, Eve 36
    /// after three counted mains, with Carl winning and Bill second the
    /// order that is proved. The roster is the four plus whatever is added.
    /// </summary>
    private static (RoundDefinition Next, SeasonStore Seasons, RoundDefinitionStore Rounds, SessionHistoryStore History, ProfileStore Profiles) Club(
        params string[] extraOnGrid)
    {
        var profiles = ProfileStore.Load();

        foreach (var name in new[] { "Carl", "Bill", "Dot", "Eve" })
        {
            profiles.Upsert(new DriverProfile { Transponder = name.ToLowerInvariant(), DriverName = name });
        }

        var counted = new[]
        {
            new[] { "Bill", "Carl", "Dot", "Eve" },
            new[] { "Carl", "Bill", "Dot", "Eve" },
            new[] { "Dot", "Bill", "Carl", "Eve" },
        };

        var rounds = RoundDefinitionStore.Load();
        var races = new List<SessionRecord>();
        var when = new DateTime(2026, 5, 1, 19, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < counted.Length; i++)
        {
            var past = new RoundDefinition { ClassName = "Box Stock", RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0 };

            var race = new SessionRecord
            {
                Id = "r" + i.ToString(CultureInfo.InvariantCulture),
                SessionType = RaceSessionType.Race,
                Label = "A-Main",
                RoundId = past.Id,
                StartedAtUtc = when.AddDays(7 * i),
                EndedAtUtc = when.AddDays(7 * i).AddMinutes(8),
                Results = counted[i]
                    .Select((name, place) => new SessionResultEntry
                    {
                        Transponder = name.ToLowerInvariant(),
                        DriverName = name,
                        Position = place + 1,
                        Laps = 12,
                    })
                    .ToList(),
            };

            past.LinkedSessionId = race.Id;

            rounds.Add(past);
            races.Add(race);
        }

        var next = new RoundDefinition
        {
            ClassName = "Box Stock",
            RoundLabel = "A-Main",
            Kind = RoundKind.Main,
            MainTier = 0,
            AssignedTransponders = new[] { "carl", "bill", "dot", "eve" }.Concat(extraOnGrid).ToList(),
        };

        rounds.Add(next);

        var seasons = SeasonStore.Load();

        seasons.Upsert(new Season
        {
            Name = "Winter Series",
            ClassName = "Box Stock",
            AutoInclude = true,
            MainsOnly = true,
            PointsScheme = new[] { (1, 25), (2, 18), (3, 15), (4, 12), (5, 10), (6, 8) }
                .Select(row => new PointsSchemeEntry { Position = row.Item1, Points = row.Item2 })
                .ToList(),
        });

        return (next, seasons, rounds, SessionHistoryStore.FromSessions(races), profiles);
    }

    [Fact]
    public void The_same_grid_with_every_car_named_proves_its_conditional()
    {
        // The proof without it, so the refusal below is the unnamed car's.
        var named = Club();

        Assert.NotNull(CommentaryReaders.ChampionshipConditional(
            named.Next, named.Next.AssignedTransponders, named.Seasons, named.Rounds, null, named.History, named.Profiles));
    }

    [Fact]
    public void A_car_on_the_grid_with_no_profile_name_is_not_pinned_where_it_stands()
    {
        // T999 scores under its transponder, so it is in the race even
        // though the proof could not price it.
        var club = Club("T999");

        Assert.Null(CommentaryReaders.ChampionshipConditional(
            club.Next, club.Next.AssignedTransponders, club.Seasons, club.Rounds, null, club.History, club.Profiles));
    }
}
