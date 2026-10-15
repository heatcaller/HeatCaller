namespace HeatCaller.Tests;

/// <summary>
/// A SEASON, AS A PIECE READ TO THE ROOM (13 Oct 2026) - see
/// <see cref="SeasonPiece"/>.
///
/// WHAT THIS FILE IS GUARDING
///
/// Every other spoken segment in this app is handed one race or one night
/// and summarises it. This one is handed a year and has to decide what the
/// year was about - so most of what can go wrong here is a claim about a
/// season that somebody in the room knows is false, and the rest is the
/// piece quietly becoming about the champion.
///
/// The two worth reading first are
/// <see cref="A_season_that_went_to_a_tie_names_the_champion_the_table_names"/>
/// - because re-sorting a standings table on points alone would name the
/// wrong driver in exactly the season a club would most want this piece for
/// - and <see cref="The_fastest_lap_is_never_a_moment_of_the_season"/>,
/// which is the PastMeetings lesson arriving here before it could cost
/// anything.
///
/// The facts are the testable half. What the model does with them is not.
/// </summary>
public sealed class SeasonPieceTests
{
    private static readonly DateTime March = new(2026, 3, 14, 19, 0, 0, DateTimeKind.Utc);

    // --- What the season was -----------------------------------------------

    [Fact]
    public void A_season_says_how_many_races_and_how_many_nights_it_took()
    {
        // Two races on one night and one on another: three races, two
        // nights. A season counted in races alone tells a club nothing
        // about how often it opened the doors.
        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord>
            {
                Race("a", "Heat 1", 0, Row("Bill", 1), Row("Sam", 2)),
                Race("b", "A-Main", 90, Row("Sam", 1), Row("Bill", 2)),
                Race("c", "Heat 1", 60 * 24 * 7, Row("Bill", 1), Row("Sam", 2)),
            },
            Standings(("Bill", 40, 2), ("Sam", 35, 1)));

        Assert.Contains("3 races", facts.Text);
        Assert.Contains("2 race nights", facts.Text);
    }

    [Fact]
    public void Races_hours_apart_on_one_evening_are_one_night()
    {
        // Through TonightAtTheTrack.NightOf, which is the one answer to
        // "which night was that" in this app and which has its own tests
        // for the meeting that runs past midnight. Four hours rather than
        // six, and asserted on the count rather than the date, so this does
        // not become a test about the time zone the suite happens to run in.
        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord>
            {
                Race("a", "Heat 1", 0, Row("Bill", 1)),
                Race("b", "A-Main", 60 * 4, Row("Bill", 1)),
            },
            Standings(("Bill", 40, 2)));

        Assert.Contains("1 race night", facts.Text);
    }

    [Fact]
    public void A_season_with_nothing_in_it_is_not_a_piece()
    {
        Assert.True(SeasonPiece.FactsFor(Championship("Winter Series"), new List<SessionRecord>(), Standings()).Empty);
        Assert.True(SeasonPiece.FactsFor(Championship("Winter Series"), null, null).Empty);
        Assert.True(SeasonPiece.FactsFor(null, null, null).Empty);
    }

    [Fact]
    public void A_demo_race_is_never_part_of_a_season()
    {
        // Its results are invented. EventSpine.SessionsFor already keeps
        // them out, and this keeps them out again rather than trusting its
        // one caller.
        var demo = Race("demo", "Demo", 0, Row("Bill", 1));
        demo.IsDemo = true;

        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord> { demo },
            Standings(("Bill", 40, 1)));

        Assert.True(facts.Empty);
    }

    [Fact]
    public void A_season_the_club_never_named_still_reads()
    {
        var facts = SeasonPiece.FactsFor(
            Championship(string.Empty),
            new List<SessionRecord> { Race("a", "Heat 1", 0, Row("Bill", 1), Row("Sam", 2)) },
            Standings(("Bill", 40, 1), ("Sam", 30, 0)));

        Assert.False(facts.Empty);
        Assert.Contains("The season ran to", facts.Text);
    }

    // --- Who won it --------------------------------------------------------

    [Fact]
    public void The_champion_is_named_with_their_points_and_their_margin()
    {
        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord> { Race("a", "Heat 1", 0, Row("Bill", 1), Row("Sam", 2)) },
            Standings(("Bill", 40, 2), ("Sam", 33, 1)));

        Assert.Contains("Bill won Winter Series on 40 points", facts.Text);
        Assert.Contains("Sam was second, 7 points behind", facts.Text);
    }

    [Fact]
    public void A_margin_is_the_one_figure_that_says_whether_it_was_a_contest()
    {
        // Two points and two hundred are the same sentence without it.
        var close = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord> { Race("a", "Heat 1", 0, Row("Bill", 1), Row("Sam", 2)) },
            Standings(("Bill", 40, 2), ("Sam", 38, 1)));

        Assert.Contains("2 points behind", close.Text);
    }

    [Fact]
    public void A_season_that_went_to_a_tie_names_the_champion_the_table_names()
    {
        // THE TEST THIS FILE EXISTS FOR. ComputeStandings orders by points,
        // then wins, then best finish, then podiums, then name - so on
        // level points the champion is the one it put first. Re-sorting on
        // points alone here would name whichever of them LINQ happened to
        // reach first, in exactly the season a club would most want this
        // piece for.
        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord> { Race("a", "Heat 1", 0, Row("Sam", 1), Row("Bill", 2)) },

            // Sam first, on the table's own tie-break, on equal points.
            Standings(("Sam", 40, 3), ("Bill", 40, 1)));

        Assert.Contains("Sam won Winter Series", facts.Text);
        Assert.DoesNotContain("Bill won", facts.Text);
        Assert.Contains("level with them on points", facts.Text);
        Assert.Contains("tie-break", facts.Text);
    }

    [Fact]
    public void Third_is_named_too_and_nobody_below_them_is()
    {
        // Naming the top three is a podium. Naming the top eight implies an
        // order for the twenty below them, which is the one thing a piece
        // read to a whole room must not do.
        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord> { Race("a", "Heat 1", 0, Row("Bill", 1), Row("Sam", 2)) },
            Standings(("Bill", 40, 2), ("Sam", 33, 1), ("Ann", 28, 0), ("Joe", 20, 0), ("Pat", 11, 0)));

        Assert.Contains("Ann was third", facts.Text);
        Assert.DoesNotContain("Joe", facts.Text);
        Assert.DoesNotContain("Pat", facts.Text);
    }

    [Fact]
    public void A_one_driver_championship_claims_no_margin()
    {
        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord> { Race("a", "Heat 1", 0, Row("Bill", 1)) },
            Standings(("Bill", 40, 1)));

        Assert.Contains("Bill won Winter Series", facts.Text);
        Assert.DoesNotContain("behind", facts.Text);
        Assert.DoesNotContain("second", facts.Text);
    }

    [Fact]
    public void A_best_rounds_rule_is_said_only_when_it_dropped_something()
    {
        // The one season rule a driver asks about, and the reason a total
        // is lower than their results look. Claimed when it is off, it is
        // a rule the club does not have.
        var races = new List<SessionRecord> { Race("a", "Heat 1", 0, Row("Bill", 1), Row("Sam", 2)) };

        var dropped = Standings(("Bill", 40, 2), ("Sam", 30, 1));
        dropped[0].DroppedRounds = 3;

        Assert.Contains(
            "best 8 rounds",
            SeasonPiece.FactsFor(Championship("Winter Series", countBest: 8), races, dropped).Text);

        // The rule is set but nothing was actually dropped.
        Assert.DoesNotContain(
            "best 8 rounds",
            SeasonPiece.FactsFor(Championship("Winter Series", countBest: 8), races, Standings(("Bill", 40, 2))).Text);

        // And the rule is off.
        Assert.DoesNotContain(
            "best",
            SeasonPiece.FactsFor(Championship("Winter Series"), races, dropped).Text);
    }

    // --- Who turned up -----------------------------------------------------

    [Fact]
    public void The_driver_who_raced_most_is_named_when_it_is_not_the_champion()
    {
        // The one honour a club awards by attendance, and the only claim
        // here that is not about being fast.
        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord> { Race("a", "Heat 1", 0, Row("Bill", 1), Row("Sam", 2)) },
            WithRounds(("Bill", 40, 2, 6), ("Sam", 33, 1, 11)));

        Assert.Contains("Sam raced the most rounds of anybody: 11 rounds", facts.Text);
    }

    [Fact]
    public void The_champion_is_not_given_a_second_sentence_for_turning_up()
    {
        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord> { Race("a", "Heat 1", 0, Row("Bill", 1), Row("Sam", 2)) },
            WithRounds(("Bill", 40, 2, 11), ("Sam", 33, 1, 6)));

        Assert.DoesNotContain("the most rounds of anybody", facts.Text);
    }

    // --- The moments -------------------------------------------------------

    [Fact]
    public void The_fastest_lap_is_never_a_moment_of_the_season()
    {
        // THE PastMeetings LESSON, arriving here before it could cost
        // anything. Every race has a fastest lap, so over a season the pack
        // would fill with them and say nothing about the year.
        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord>
            {
                Race("a", "Heat 1", 0,
                    Row("Bill", 1, new long[] { 10000, 9000, 8000 }),
                    Row("Sam", 2, new long[] { 10200, 9400, 9300 })),
            },
            Standings(("Bill", 40, 1), ("Sam", 30, 0)));

        Assert.DoesNotContain("fastest lap", facts.Text);
    }

    [Fact]
    public void A_moment_is_worded_the_way_a_drivers_own_piece_words_it()
    {
        // Through DriverCut.Describe, whose own comment asks for exactly
        // this: two wordings for one moment is two wordings that drift, and
        // a club would hear one of them at a prize night and the other in a
        // callback about the same race.
        var moment = new NightMoments.Moment(
            NightMoments.Kind.Charge, "a", "Heat 1", "bill", "Bill", 3, 0, 4, string.Empty, March);

        Assert.Equal("Bill made up 4 places from the grid in Heat 1.", DriverCut.Describe(moment, "Bill"));
    }

    [Fact]
    public void A_moment_says_roughly_when_in_the_year_it_was()
    {
        // The month and not the date: a room wants reminding roughly when,
        // and a month survives being slightly wrong about a race that
        // started at five to midnight.
        var when = SeasonPiece.WhenIt(March);

        Assert.StartsWith("in ", when);
        Assert.Equal(string.Empty, SeasonPiece.WhenIt(default));
    }

    // --- How big the club got ----------------------------------------------

    [Fact]
    public void The_busiest_night_is_counted_in_cars_and_not_in_entries()
    {
        // Four heats of the same ten drivers is ten cars, not forty - the
        // mistake the night-before preview had to be careful of too, and
        // the other way round has a club advertising a field four times the
        // size of the one that turned up.
        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord>
            {
                Race("a", "Heat 1", 0, Row("Bill", 1), Row("Sam", 2)),
                Race("b", "Heat 2", 30, Row("Bill", 1), Row("Sam", 2)),
                Race("c", "A-Main", 60, Row("Bill", 1), Row("Sam", 2)),
            },
            Standings(("Bill", 40, 3), ("Sam", 30, 0)));

        Assert.Contains("2 cars out", facts.Text);
    }

    [Fact]
    public void A_ghost_car_is_never_counted_as_a_car_that_turned_up()
    {
        // A pace reference is not an entrant, which is the rule
        // ComputeStandings and NightMoments both keep.
        var withGhost = Race("a", "Heat 1", 0, Row("Bill", 1), Row("Sam", 2));
        withGhost.Results.Add(Row("Ghost", 3));
        withGhost.Results[2].Transponder = Ghosts.Prefix + "pace";

        var facts = SeasonPiece.FactsFor(
            Championship("Winter Series"),
            new List<SessionRecord> { withGhost },
            Standings(("Bill", 40, 1), ("Sam", 30, 0)));

        Assert.Contains("2 cars out", facts.Text);
        Assert.DoesNotContain("3 cars", facts.Text);
    }

    // --- The piece's own shape ---------------------------------------------

    [Fact]
    public void It_borrows_the_recaps_chunker_and_its_ceiling()
    {
        // The piece is read through NightRecap's chunker, so its own
        // ceiling has to be that one's or the chunker throws away words
        // this class thought it had kept.
        var mostChars = SeasonPiece.MostChars;

        Assert.Equal(NightRecap.MostChars, mostChars);
        Assert.Equal(NightRecap.LongestRead, SeasonPiece.LongestRead);
        Assert.Equal(NightRecap.Chunks("One. Two. Three."), SeasonPiece.Chunks("One. Two. Three."));
    }

    [Fact]
    public void The_word_count_fits_what_the_chunker_will_carry()
    {
        // Fifteen chunks of twenty seconds is five minutes, which is about
        // seven hundred and fifty words at a broadcast pace. Asked for more
        // than that and the end of the piece is simply discarded.
        var words = SeasonPiece.Words;

        Assert.InRange(words, NightRecap.Words, 750);
    }

    [Fact]
    public void The_recording_is_named_after_the_season()
    {
        // A club running a Winter and a Summer series has two of these, and
        // the files have to tell themselves apart.
        Assert.Equal("Winter Series - the season", SeasonPiece.LabelFor("Winter Series"));
        Assert.Equal("The season", SeasonPiece.LabelFor(null));
        Assert.Equal("The season", SeasonPiece.LabelFor("   "));
    }

    [Fact]
    public void The_band_says_the_seasons_name_rather_than_what_it_borrowed()
    {
        // The 13 Oct lesson: the night's recap and a driver's cut both took
        // the sign-off's treatment AND its name, and the Big Screen read
        // "Sign-Off" for four minutes of something else.
        Assert.Equal("Winter Series", SeasonPiece.OnTheBand("Winter Series"));
        Assert.Equal("The Season", SeasonPiece.OnTheBand(null));
    }

    [Fact]
    public void The_instruction_tells_the_model_the_facts_are_all_of_it()
    {
        var instruction = SeasonPiece.Instruction;

        Assert.Contains("must come from the facts above", instruction);
        Assert.Contains("shorter piece rather than filling it out", instruction);
    }

    [Fact]
    public void The_instruction_refuses_a_piece_about_the_champion()
    {
        // One driver won it and everybody else in the room did not.
        var instruction = SeasonPiece.Instruction;

        Assert.Contains("only works for the champion", instruction);
        Assert.Contains("do not imply any order among the drivers you do not name", instruction);
    }

    // --- Scaffolding -------------------------------------------------------

    private static Season Championship(string? name, int countBest = 0) =>
        new() { Name = name ?? string.Empty, CountBestRounds = countBest };

    private static List<SeasonStandingEntry> Standings(
        params (string Name, int Points, int Wins)[] rows) =>
        rows.Select(row => new SeasonStandingEntry
        {
            DriverName = row.Name,
            TotalPoints = row.Points,
            Wins = row.Wins,
            SessionsCounted = 1,
        }).ToList();

    /// <summary>The same, with each driver's rounds counted - a name of its own because two params overloads make Standings() ambiguous.</summary>
    private static List<SeasonStandingEntry> WithRounds(
        params (string Name, int Points, int Wins, int Rounds)[] rows) =>
        rows.Select(row => new SeasonStandingEntry
        {
            DriverName = row.Name,
            TotalPoints = row.Points,
            Wins = row.Wins,
            SessionsCounted = row.Rounds,
        }).ToList();

    private static SessionResultEntry Row(string name, int position, long[]? laps = null)
    {
        var times = (laps ?? new long[] { 10000, 9500, 9400 }).ToList();

        return new SessionResultEntry
        {
            Transponder = name.ToLowerInvariant(),
            DriverName = name,
            Position = position,
            Laps = times.Count,
            LapTimesMs = times,
            StartPosition = position,
            ElapsedMs = times.Sum(),
        };
    }

    private static SessionRecord Race(
        string id,
        string label,
        int minutesIn,
        params SessionResultEntry[] rows) =>
        new()
        {
            Id = id,
            SessionType = RaceSessionType.Race,
            Label = label,
            TrackLayoutName = "Winter Indoor",
            StartedAtUtc = March.AddMinutes(minutesIn),
            EndedAtUtc = March.AddMinutes(minutesIn + 8),
            Results = rows.ToList(),
        };
}
