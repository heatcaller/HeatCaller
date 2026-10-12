namespace HeatCaller.Tests;

/// <summary>
/// The layout's own history, added 11 Oct 2026: who has been winning on
/// this piece of tarmac, and what each driver on the grid has done round
/// it before tonight.
///
/// WHY THESE TWO ARE DIFFERENT FROM THE OUTRIGHT RECORD
///
/// The record on a layout belongs to one driver, and a club of forty
/// hears it as a fact about somebody else. A driver's own best here
/// belongs to whoever is being talked about - the regular who finishes
/// eighth has one too - and no human announcer holds forty drivers' bests
/// across four layouts in his head. That is the whole claim, and it is
/// also what makes it dangerous: a figure about a named person, read out
/// in front of them, has to be right or they will say so.
///
/// So most of this file is about what is NOT said: a driver on their
/// second visit to a layout, a run of two that is a coincidence, a race
/// somebody turned up to alone, a club in its first weeks. And about the
/// SCOPE, the other half of the same rule - "his quickest ever" without
/// "on this layout" is a claim about a career, which is a different and
/// false thing.
/// </summary>
public sealed class LayoutLegendTests : IDisposable
{
    // Declared first: every store below loads from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>Seven in the evening, which NightOf reads as that day's race night.</summary>
    private static readonly DateTime Now = new(2026, 10, 11, 19, 0, 0, DateTimeKind.Local);

    private const string Oval = "Club Oval";
    private const string Infield = "Infield Twist";

    private static DriverOnLayout Driver(
        string id = "4C1A9",
        string name = "Sam",
        double best = 11.402,
        string when = "August",
        int races = 9) =>
        new(id, name, best, when, races);

    private static LayoutLegend Legend(
        string layout = Oval,
        int onLayout = 14,
        IReadOnlyList<DriverOnLayout>? bests = null,
        string winner = "Bill",
        int run = 4) =>
        new(layout, onLayout, bests ?? new[] { Driver() }, winner, run);

    // ------------------------------------------------------------------
    //  A driver's own best here
    // ------------------------------------------------------------------

    [Fact]
    public void A_drivers_own_best_here_names_the_driver_the_layout_and_what_it_was_counted_over()
    {
        var block = CommentaryRaceFacts.BestOnThisLayout(Driver(), Oval, "night-1");

        Assert.NotNull(block);

        // THE SCOPE. "His quickest ever" without the layout is a claim
        // about a career, which is a different and false thing.
        Assert.Contains("on this layout", block!.Text);
        Assert.Contains(Oval, block.Text);

        Assert.Contains("Sam", block.Text);
        Assert.Contains("11.402s", block.Text);
        Assert.Contains("August", block.Text);

        // And how many races the figure is the best of, which is what
        // makes it a measure of the place rather than a coincidence.
        Assert.Contains("9 races", block.Text);
    }

    [Fact]
    public void A_driver_on_their_second_visit_to_a_layout_has_no_best_worth_stating()
    {
        // Everybody's second lap on a new track is their best. Said as a
        // figure it teaches a crowd that the figures mean nothing.
        for (var races = 0; races < CommentaryRaceFacts.LeastRacesForOwnBest; races++)
        {
            Assert.Null(CommentaryRaceFacts.BestOnThisLayout(Driver(races: races), Oval, "night-1"));
        }

        Assert.NotNull(CommentaryRaceFacts.BestOnThisLayout(
            Driver(races: CommentaryRaceFacts.LeastRacesForOwnBest), Oval, "night-1"));
    }

    [Fact]
    public void A_driver_with_no_sayable_name_or_no_lap_is_not_talked_about()
    {
        Assert.Null(CommentaryRaceFacts.BestOnThisLayout(Driver(name: ""), Oval, "night-1"));
        Assert.Null(CommentaryRaceFacts.BestOnThisLayout(Driver(name: "   "), Oval, "night-1"));
        Assert.Null(CommentaryRaceFacts.BestOnThisLayout(Driver(best: 0), Oval, "night-1"));
        Assert.Null(CommentaryRaceFacts.BestOnThisLayout(Driver(best: -1), Oval, "night-1"));

        // And with no layout there is no scope, and without a scope this
        // line is a claim about a career.
        Assert.Null(CommentaryRaceFacts.BestOnThisLayout(Driver(), "", "night-1"));
    }

    [Fact]
    public void A_best_with_no_month_on_it_is_still_a_true_figure()
    {
        // Unlike the OUTRIGHT record, which cannot be told apart from one
        // set an hour ago without a date. A driver's own best is a figure
        // either way, so it is said without the month rather than dropped.
        var block = CommentaryRaceFacts.BestOnThisLayout(Driver(when: ""), Oval, "night-1");

        Assert.NotNull(block);
        Assert.Contains("11.402s", block!.Text);
        Assert.DoesNotContain("set in", block.Text);
    }

    [Fact]
    public void A_drivers_own_best_is_themed_on_the_driver_and_the_run_of_wins_on_the_track()
    {
        // Opposite ways round, and deliberately. The subject of "Sam's own
        // quickest here" is Sam: two of them about him must not go out
        // back to back, and it must not be silenced because somebody
        // else's record was read out about the same tarmac. The subject of
        // "the last four here went to Bill" is the track.
        var best = CommentaryRaceFacts.BestOnThisLayout(Driver(), Oval, "night-1");
        var run = CommentaryRaceFacts.WhoWinsHere(Legend(), "night-1");

        Assert.Equal(FactTheme.Driver("4C1A9"), best!.Theme);
        Assert.Equal(FactTheme.Venue(Oval), run!.Theme);
    }

    [Fact]
    public void Two_drivers_bests_on_the_same_layout_are_separate_facts()
    {
        // Keyed per driver, so the room can hear about two of them - and
        // so the same driver's is not said twice in an evening.
        var one = CommentaryRaceFacts.BestOnThisLayout(Driver(id: "AAA", name: "Sam"), Oval, "night-1");
        var two = CommentaryRaceFacts.BestOnThisLayout(Driver(id: "BBB", name: "Bill"), Oval, "night-1");
        var again = CommentaryRaceFacts.BestOnThisLayout(Driver(id: "AAA", name: "Sam"), Oval, "night-1");

        Assert.NotEqual(one!.Key, two!.Key);
        Assert.Equal(one.Key, again!.Key);
    }

    [Fact]
    public void A_best_is_the_same_fact_all_evening_and_news_again_on_another_night()
    {
        var tonight = CommentaryRaceFacts.BestOnThisLayout(Driver(), Oval, "night-1");
        var nextWeek = CommentaryRaceFacts.BestOnThisLayout(Driver(), Oval, "night-2");

        Assert.NotEqual(tonight!.Key, nextWeek!.Key);
    }

    [Fact]
    public void The_same_driver_on_two_layouts_is_two_facts()
    {
        // A best is per layout, so a club that runs two tracks in an
        // evening can say both without one silencing the other.
        var oval = CommentaryRaceFacts.BestOnThisLayout(Driver(), Oval, "night-1");
        var infield = CommentaryRaceFacts.BestOnThisLayout(Driver(), Infield, "night-1");

        Assert.NotEqual(oval!.Key, infield!.Key);
        Assert.Contains(Infield, infield.Text);
    }

    // ------------------------------------------------------------------
    //  Who has been winning here
    // ------------------------------------------------------------------

    [Fact]
    public void A_run_of_wins_says_how_many_who_and_on_which_layout()
    {
        var block = CommentaryRaceFacts.WhoWinsHere(Legend(winner: "Bill", run: 5), "night-1");

        Assert.NotNull(block);
        Assert.Contains("Bill", block!.Text);
        Assert.Contains(Oval, block.Text);
        Assert.Contains("on this layout", block.Text);

        // Spelled, because these lines are written to be spoken.
        Assert.Contains("five", block.Text);
    }

    [Fact]
    public void A_run_is_worded_as_races_and_never_as_mains_or_a_championship()
    {
        // The count is over every race filed on this layout, which is what
        // the history alone can prove. "The last five A-mains" would be a
        // claim the store cannot support, and the driver who won the heat
        // in between would be the first to say so.
        var block = CommentaryRaceFacts.WhoWinsHere(Legend(), "night-1");

        Assert.Contains("races", block!.Text);
        Assert.DoesNotContain("main", block.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("championship", block.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_wins_in_a_row_is_a_coincidence_rather_than_a_run()
    {
        // At a club where ten drivers share the wins, two in a row is
        // chance - and the driver who won the race before those two is
        // standing right there.
        for (var run = 0; run < CommentaryRaceFacts.LeastRunToCall; run++)
        {
            Assert.Null(CommentaryRaceFacts.WhoWinsHere(Legend(run: run), "night-1"));
        }

        Assert.NotNull(CommentaryRaceFacts.WhoWinsHere(
            Legend(run: CommentaryRaceFacts.LeastRunToCall), "night-1"));
    }

    [Fact]
    public void A_layout_with_almost_nothing_run_on_it_has_no_run_to_talk_about()
    {
        // Three races on a layout, all won by the same driver, is not "the
        // last three here have all gone to Bill" - it is the whole of its
        // history, and the superlative has nothing behind it.
        Assert.Null(CommentaryRaceFacts.WhoWinsHere(
            Legend(onLayout: CommentaryRaceFacts.LeastRacesOnLayout - 1), "night-1"));
    }

    [Fact]
    public void A_run_with_nobody_or_nowhere_on_it_is_not_said()
    {
        Assert.Null(CommentaryRaceFacts.WhoWinsHere(Legend(winner: ""), "night-1"));
        Assert.Null(CommentaryRaceFacts.WhoWinsHere(Legend(winner: "  "), "night-1"));
        Assert.Null(CommentaryRaceFacts.WhoWinsHere(Legend(layout: ""), "night-1"));
    }

    // ------------------------------------------------------------------
    //  The section, and what survives it
    // ------------------------------------------------------------------

    [Fact]
    public void The_run_comes_first_because_it_gives_the_race_a_shape()
    {
        // "The last five here have all gone to Bill" tells the room what
        // would be remarkable about the next twenty minutes. A lap time is
        // a figure. If only one line survives the cap, it is the run.
        var built = CommentaryRaceFacts.ForTheLayout(
            Legend(bests: new[] { Driver(id: "A", name: "Sam"), Driver(id: "B", name: "Carl") }),
            "night-1",
            most: 1);

        Assert.Single(built);
        Assert.Contains("gone to Bill", built[0].Text);
    }

    [Fact]
    public void At_most_two_drivers_bests_go_into_one_intro()
    {
        // Each one is a sentence about a person and the prompt names eight
        // drivers. Unchecked, this section alone would be eight sentences
        // of lap times.
        var many = new[]
        {
            Driver(id: "A", name: "Sam"),
            Driver(id: "B", name: "Carl"),
            Driver(id: "C", name: "Bill"),
            Driver(id: "D", name: "Dave"),
        };

        var built = CommentaryRaceFacts.ForTheLayout(Legend(bests: many), "night-1", most: 99);

        Assert.Equal(1 + CommentaryRaceFacts.OwnBestFacts, built.Count);
    }

    [Fact]
    public void A_club_with_no_layout_history_gets_an_empty_section_rather_than_a_padded_one()
    {
        Assert.Empty(CommentaryRaceFacts.ForTheLayout(null, "night-1"));

        // A new layout, nobody with four races on it, and the wins going
        // round: every one of those is an honest nothing, and the intro
        // reads as it did before any of this existed.
        Assert.Empty(CommentaryRaceFacts.ForTheLayout(
            Legend(onLayout: 2, bests: new[] { Driver(races: 1) }, winner: "", run: 0),
            "night-1"));
    }

    [Fact]
    public void A_section_of_its_own_is_the_point_so_nothing_here_is_in_the_race_scenes_cap()
    {
        // ForIntro's cap is three and the field, the record and the pace
        // figures already compete for them. Appended there, every line in
        // this file would have been built on every race of the evening and
        // heard on none of them - so ForIntro must not be building them.
        var scene = CommentaryRaceFacts.ForIntro(null, null, null, "race-1");

        Assert.Empty(scene);
    }

    // ------------------------------------------------------------------
    //  The reader, over a real history
    // ------------------------------------------------------------------

    /// <summary>
    /// A race on a layout, with each driver's finishing position and their
    /// fastest lap in milliseconds.
    /// </summary>
    private static SessionRecord Raced(
        DateTime endedLocal,
        string layout = Oval,
        RaceSessionType type = RaceSessionType.Race,
        bool demo = false,
        params (string Transponder, int Position, long FastLapMs)[] results) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionType = type,
            IsDemo = demo,
            TrackLayoutName = layout,
            StartedAtUtc = endedLocal.ToUniversalTime().AddMinutes(-6),
            EndedAtUtc = endedLocal.ToUniversalTime(),
            Results = results
                .Select(r => new SessionResultEntry
                {
                    Transponder = r.Transponder,
                    DriverName = r.Transponder,
                    Position = r.Position,
                    FastLapMs = r.FastLapMs,
                    Laps = 12,
                })
                .ToList(),
        };

    /// <summary>Three named drivers with profiles, so every one of them has a name the announcer can say.</summary>
    private static ProfileStore ThreeDrivers()
    {
        var profiles = ProfileStore.Load();

        profiles.Upsert(new DriverProfile { Transponder = "A", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "B", DriverName = "Bill" });
        profiles.Upsert(new DriverProfile { Transponder = "C", DriverName = "Carl" });

        return profiles;
    }

    /// <summary>Six weekly race nights on the Oval, Bill winning the last four of them.</summary>
    private static SessionHistoryStore SixNights() =>
        SessionHistoryStore.FromSessions(new[]
        {
            Raced(Now.AddDays(-42), results: new[] { ("A", 1, 11_402L), ("B", 2, 11_600L), ("C", 3, 11_900L) }),
            Raced(Now.AddDays(-35), results: new[] { ("C", 1, 11_500L), ("A", 2, 11_450L), ("B", 3, 11_700L) }),
            Raced(Now.AddDays(-28), results: new[] { ("B", 1, 11_300L), ("A", 2, 11_480L), ("C", 3, 11_850L) }),
            Raced(Now.AddDays(-21), results: new[] { ("B", 1, 11_250L), ("A", 2, 11_390L), ("C", 3, 11_800L) }),
            Raced(Now.AddDays(-14), results: new[] { ("B", 1, 11_200L), ("A", 2, 11_420L), ("C", 3, 11_780L) }),
            Raced(Now.AddDays(-7), results: new[] { ("B", 1, 11_180L), ("A", 2, 11_410L), ("C", 3, 11_760L) }),
        });

    [Fact]
    public void The_reader_finds_each_drivers_own_best_and_the_run_of_wins()
    {
        var legend = CommentaryReaders.LayoutStory(SixNights(), ThreeDrivers(), Oval, new[] { "A", "B", "C" }, Now);

        Assert.NotNull(legend);
        Assert.Equal(Oval, legend!.LayoutName);
        Assert.Equal(6, legend.RacesOnLayout);

        // Bill has won the last four; Carl won the one before those.
        Assert.Equal("Bill", legend.WinnerName);
        Assert.Equal(4, legend.WinnerRun);

        // Quickest first, so an intro that can carry two carries the two
        // figures worth stating rather than the first two it was handed.
        Assert.Equal(new[] { "Bill", "Sam", "Carl" }, legend.Bests.Select(b => b.Name).ToArray());

        var sam = legend.Bests.Single(b => b.Name == "Sam");

        Assert.Equal(11.390, sam.BestSeconds, 3);
        Assert.Equal(6, sam.RacesOnLayout);
    }

    [Fact]
    public void Tonights_own_races_are_not_the_clubs_history_yet()
    {
        // "His best ever here is 11.1" said about a lap set in the heat an
        // hour ago is the timing system describing this evening as club
        // history, in front of the man who did it.
        var history = SessionHistoryStore.FromSessions(SixNights().All
            .Append(Raced(Now.AddHours(-1), results: new[] { ("A", 1, 10_900L), ("B", 2, 11_500L), ("C", 3, 11_900L) }))
            .ToList());

        var legend = CommentaryReaders.LayoutStory(history, ThreeDrivers(), Oval, new[] { "A" }, Now);

        Assert.NotNull(legend);
        Assert.Equal(6, legend!.RacesOnLayout);

        // The 10.900 from tonight is not his best "ever"; 11.390 is.
        Assert.Equal(11.390, legend.Bests.Single().BestSeconds, 3);

        // And tonight's win has not extended anybody's run.
        Assert.Equal("Bill", legend.WinnerName);
        Assert.Equal(4, legend.WinnerRun);
    }

    [Fact]
    public void Practice_and_demo_races_count_for_nothing()
    {
        var history = SessionHistoryStore.FromSessions(new[]
        {
            Raced(Now.AddDays(-30), type: RaceSessionType.Practice, results: new[] { ("A", 1, 9_000L) }),
            Raced(Now.AddDays(-29), demo: true, results: new[] { ("A", 1, 8_000L) }),
            Raced(Now.AddDays(-28), results: new[] { ("A", 1, 11_402L), ("B", 2, 11_600L), ("C", 3, 11_900L) }),
        });

        var legend = CommentaryReaders.LayoutStory(history, ThreeDrivers(), Oval, new[] { "A" }, Now);

        Assert.NotNull(legend);
        Assert.Equal(1, legend!.RacesOnLayout);

        // The invented 8.000 and the practice 9.000 are nowhere near it.
        Assert.Equal(11.402, legend.Bests.Single().BestSeconds, 3);
    }

    [Fact]
    public void Another_layouts_races_are_another_layouts_history()
    {
        var history = SessionHistoryStore.FromSessions(SixNights().All
            .Append(Raced(Now.AddDays(-3), layout: Infield, results: new[] { ("A", 1, 9_500L), ("B", 2, 9_900L), ("C", 3, 10_100L) }))
            .ToList());

        var oval = CommentaryReaders.LayoutStory(history, ThreeDrivers(), Oval, new[] { "A" }, Now);

        Assert.Equal(6, oval!.RacesOnLayout);
        Assert.Equal(11.390, oval.Bests.Single().BestSeconds, 3);

        var infield = CommentaryReaders.LayoutStory(history, ThreeDrivers(), Infield, new[] { "A" }, Now);

        Assert.Equal(1, infield!.RacesOnLayout);
        Assert.Equal(9.500, infield.Bests.Single().BestSeconds, 3);
    }

    [Fact]
    public void A_race_somebody_turned_up_to_alone_neither_makes_a_run_nor_ends_one()
    {
        // "The last five races here have gone to Bill" said about five
        // evenings Bill turned up to on his own is the kind of true a club
        // laughs at. A two-car race is passed over rather than counted.
        var history = SessionHistoryStore.FromSessions(SixNights().All
            .Append(Raced(Now.AddDays(-1), results: new[] { ("A", 1, 11_000L), ("B", 2, 11_500L) }))
            .ToList());

        var legend = CommentaryReaders.LayoutStory(history, ThreeDrivers(), Oval, new[] { "A" }, Now);

        // Sam's two-car win did not end Bill's run, and did not start one
        // of his own.
        Assert.Equal("Bill", legend!.WinnerName);
        Assert.Equal(4, legend.WinnerRun);
    }

    [Fact]
    public void A_driver_with_no_name_on_their_profile_is_not_named_over_the_PA()
    {
        // SpokenName falls back to the TRANSPONDER, which is right for a
        // race board and wrong for a sentence - "the quickest lap went to
        // 4C1A9" is the PA reading a hardware id out to a room.
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "A", DriverName = string.Empty });

        var legend = CommentaryReaders.LayoutStory(SixNights(), profiles, Oval, new[] { "A" }, Now);

        Assert.NotNull(legend);
        Assert.Empty(legend!.Bests);
    }

    [Fact]
    public void A_layout_with_nothing_filed_on_it_has_no_history_at_all()
    {
        Assert.Null(CommentaryReaders.LayoutStory(SixNights(), ThreeDrivers(), "A Layout Nobody Built", new[] { "A" }, Now));
        Assert.Null(CommentaryReaders.LayoutStory(SixNights(), ThreeDrivers(), null, new[] { "A" }, Now));
        Assert.Null(CommentaryReaders.LayoutStory(SixNights(), ThreeDrivers(), "   ", new[] { "A" }, Now));
    }

    [Fact]
    public void A_grid_nobody_has_a_profile_for_still_gets_the_layouts_own_history()
    {
        // The run of wins needs profiles and the bests need names; a club
        // with neither still gets the race count, and the builders refuse
        // the rest for themselves.
        var legend = CommentaryReaders.LayoutStory(SixNights(), ProfileStore.Load(), Oval, new[] { "A" }, Now);

        Assert.NotNull(legend);
        Assert.Equal(6, legend!.RacesOnLayout);
        Assert.Empty(legend.Bests);
    }

    [Fact]
    public void The_layout_travels_on_the_race_scene_as_its_last_field()
    {
        // Every "new RaceScene(...)" in the app is positional, so a field
        // inserted anywhere but last would silently hand one record's
        // value to another's parameter at every call site.
        var scene = new RaceScene(Legend: Legend());

        Assert.NotNull(scene.Legend);
        Assert.Null(scene.Venue);
        Assert.Null(scene.Field);
        Assert.Null(scene.Practice);
    }
}
