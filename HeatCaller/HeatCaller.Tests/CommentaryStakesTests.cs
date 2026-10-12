using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// WHAT THE TITLE STILL HAS RIDING ON IT (12 Oct 2026) - the richest seam
/// the commentary depth plan named, listed first in it, and blocked for two
/// days for want of one figure nothing in the app could derive: how long a
/// season is.
///
/// WHY THIS FILE IS MOSTLY THE PROOF, AND MOSTLY REFUSALS
///
/// CommentaryConditionalTests says it for the conditional and it is truer
/// here: this is a claim about every race that has not been run, all the
/// way to the end of a season. "Nobody can catch him" said over a PA and
/// falsified three weeks later is the timing system telling a club
/// something untrue about its own championship.
///
/// AND THE SHORTCUT THAT LOOKS RIGHT, which is the whole reason the proof
/// plays the rounds out rather than doing arithmetic: the gap and the
/// points still available cannot answer it, because "best 5 of 7" means a
/// driver's total is not their points added up. A rival four points behind
/// with two rounds left and twenty points available looks catchable and can
/// be mathematically shut out - see
/// <see cref="The_drop_rule_can_shut_a_rival_out_while_the_gap_says_otherwise"/>,
/// which is the test that matters most in this file.
/// </summary>
public sealed class CommentaryStakesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Stock = "Box Stock";
    private const string Series = "Winter Series";

    /// <summary>The app's own default: 10 down to 1.</summary>
    private static (int Position, int Points)[] Ladder =>
        Enumerable.Range(1, 10).Select(p => (Position: p, Points: 11 - p)).ToArray();

    private static string Id(string name) => name.ToLowerInvariant();

    private sealed record Club(
        SeasonStore Seasons,
        RoundDefinitionStore Rounds,
        RaceEventStore Days,
        SessionHistoryStore History,
        ProfileStore Profiles,
        RoundDefinition Next)
    {
        public TitleStakes? Ask() =>
            CommentaryReaders.TitleOnTheLine(Next, Next.AssignedTransponders, Seasons, Rounds, Days, History, Profiles);

        /// <summary>
        /// The same question asked by a caller with no race days - which is
        /// how a round count stops being knowable. See
        /// <see cref="A_club_with_no_race_days_says_nothing"/>.
        /// </summary>
        public TitleStakes? AskWithNoDays() =>
            CommentaryReaders.TitleOnTheLine(Next, Next.AssignedTransponders, Seasons, Rounds, null, History, Profiles);
    }

    /// <summary>
    /// A club with a championship, some rounds counted, a planned length,
    /// and one more race about to run.
    /// </summary>
    /// <param name="planned">How many rounds the season is scheduled to run. Zero is a club that never said, which is the default and says nothing.</param>
    /// <param name="days">
    /// Which race DAY each counted race was run on, as an index - so two
    /// races can be put on one day, which is what a club running an A and a
    /// B main does and what the round count has to survive. Null gives each
    /// race a day of its own, which is the provable shape and what every
    /// test written before this knob existed assumed.
    /// </param>
    /// <param name="carried">Points brought in from the club's old software - Season.CarriedPoints, which the drop rule never touches.</param>
    /// <remarks>
    /// Built from CommentaryConditionalTests.Setup, whose own remarks warn
    /// that calling it twice gives a club TWO championships rather than one
    /// changed one. The same is true here and for the same reason - every
    /// store loads from one TempAppData folder and SeasonStore.Upsert
    /// matches on Season.Id - so every test below calls it once.
    /// </remarks>
    private Club Setup(
        string[][] counted,
        int planned = 0,
        int best = 0,
        (int Position, int Points)[]? scheme = null,
        string[]? roster = null,
        int[]? days = null,
        (string Name, int Points)[]? carried = null)
    {
        var profiles = ProfileStore.Load();

        foreach (var name in new[] { "Carl", "Bill", "Dot", "Eve", "Sam", "Zed" })
        {
            profiles.Upsert(new DriverProfile { Transponder = Id(name), DriverName = name });
        }

        var rounds = RoundDefinitionStore.Load();
        var races = new List<SessionRecord>();
        var when = new DateTime(2026, 5, 1, 19, 0, 0, DateTimeKind.Utc);

        // Which rounds were run on which race day. A club's "round" of a
        // championship is a DAY, and the reader counts days rather than
        // races for exactly that reason - see
        // CommentaryReaders.RoundsCountedSoFar.
        var roundsOnDay = new Dictionary<int, List<string>>();

        for (var i = 0; i < counted.Length; i++)
        {
            var past = new RoundDefinition
            {
                ClassName = Stock,
                RoundLabel = "A-Main",
                Kind = RoundKind.Main,
                MainTier = 0,
            };

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
                        Transponder = Id(name),
                        DriverName = name,
                        Position = place + 1,
                        Laps = 12,
                    })
                    .ToList(),
            };

            past.LinkedSessionId = race.Id;

            rounds.Add(past);
            races.Add(race);

            var day = days is not null && i < days.Length ? days[i] : i;

            if (!roundsOnDay.TryGetValue(day, out var onThatDay))
            {
                onThatDay = new List<string>();
                roundsOnDay[day] = onThatDay;
            }

            onThatDay.Add(past.Id);
        }

        var events = RaceEventStore.Load();

        foreach (var (day, roundIds) in roundsOnDay.OrderBy(pair => pair.Key))
        {
            events.Upsert(new RaceEvent
            {
                Name = "Round " + (day + 1).ToString(CultureInfo.InvariantCulture),
                StartedUtc = when.AddDays(7 * day),
                FinishedUtc = when.AddDays(7 * day).AddHours(4),
                RoundIds = roundIds.ToList(),
                ClassNames = new List<string> { Stock },
            });
        }

        var next = new RoundDefinition
        {
            ClassName = Stock,
            RoundLabel = "A-Main",
            Kind = RoundKind.Main,
            MainTier = 0,
            AssignedTransponders = (roster ?? new[] { "Bill", "Sam", "Carl", "Dot" }).Select(Id).ToList(),
        };

        rounds.Add(next);

        events.Upsert(new RaceEvent
        {
            Name = "Tonight",
            StartedUtc = when.AddDays(7 * counted.Length),
            RoundIds = new List<string> { next.Id },
            ClassNames = new List<string> { Stock },
        });

        var seasons = SeasonStore.Load();

        seasons.Upsert(new Season
        {
            Name = Series,
            ClassName = Stock,
            AutoInclude = true,
            MainsOnly = true,
            CountBestRounds = best,
            PlannedRounds = planned,
            PointsScheme = (scheme ?? Ladder)
                .Select(row => new PointsSchemeEntry { Position = row.Position, Points = row.Points })
                .ToList(),
            CarriedPoints = (carried ?? Array.Empty<(string Name, int Points)>())
                .Select(row => new CarriedPointsEntry { DriverName = row.Name, Points = row.Points })
                .ToList(),
        });

        return new Club(seasons, rounds, events, SessionHistoryStore.FromSessions(races), profiles, next);
    }

    /// <summary>Four rounds of Bill beating Sam, with Carl and Dot behind.</summary>
    private static string[][] FourRoundsOfBillWinning() =>
        Enumerable.Range(0, 4).Select(_ => new[] { "Bill", "Sam", "Carl", "Dot" }).ToArray();

    // ------------------------------------------------------------------
    //  The refusal that matters most
    // ------------------------------------------------------------------

    [Fact]
    public void A_season_whose_length_nobody_typed_says_nothing()
    {
        // THE ONE THAT MATTERS. Every sentence this feature produces is
        // about the rounds REMAINING, and with no planned length there is
        // no remainder - only rounds already run. Guessing one would put a
        // false "nobody can catch him" over a PA.
        var club = Setup(FourRoundsOfBillWinning());

        Assert.Equal(0, club.Seasons.All.Single().PlannedRounds);
        Assert.Null(club.Ask());
    }

    [Fact]
    public void A_season_already_past_its_planned_length_says_nothing()
    {
        // Four rounds counted of a three-round season: the club ran extra,
        // or corrected the length down. Either way this race counts
        // towards a title that is over, and "one round left" would be a
        // negative number dressed up.
        var club = Setup(FourRoundsOfBillWinning(), planned: 3);

        Assert.Null(club.Ask());
    }

    [Fact]
    public void A_season_too_few_rounds_in_says_nothing()
    {
        // A championship nobody has raced three rounds of is a scoreboard
        // rather than a season - the same bar the conditional uses.
        var club = Setup(
            new[] { new[] { "Bill", "Sam" }, new[] { "Bill", "Sam" } },
            planned: 8);

        Assert.True(club.History.All.Count < CommentaryStakesFacts.LeastRoundsCounted);
        Assert.Null(club.Ask());
    }

    [Fact]
    public void A_scheme_where_nothing_scores_says_nothing()
    {
        // Every position worth nought. "Nobody can catch him" would be
        // true and meaningless, proved off a scheme the club has switched
        // off by accident.
        var club = Setup(
            FourRoundsOfBillWinning(),
            planned: 8,
            scheme: Enumerable.Range(1, 10).Select(p => (Position: p, Points: 0)).ToArray());

        Assert.Null(club.Ask());
    }

    [Fact]
    public void A_title_too_many_can_still_win_says_nothing()
    {
        // Four rounds into a nine-round season everybody is still in it,
        // which is the absence of a story rather than one - and the
        // standings segment already says where everybody is. This is the
        // ordinary answer on most race nights.
        var club = Setup(FourRoundsOfBillWinning(), planned: 9);

        Assert.Null(club.Ask());
    }

    // ------------------------------------------------------------------
    //  The proof
    // ------------------------------------------------------------------

    [Fact]
    public void The_drop_rule_can_shut_a_rival_out_while_the_gap_says_otherwise()
    {
        // THE TEST THAT MATTERS MOST IN THIS FILE, and the whole reason
        // the proof plays the remaining rounds out instead of doing
        // arithmetic.
        //
        // Best 4 of 6. Bill has won four; Sam has been second in four. On
        // the ladder that is Bill 40, Sam 36 - four apart, with two rounds
        // left and ten points a round still available. Every shortcut says
        // Sam can catch him.
        //
        // He cannot. Bill keeps his four wins whatever happens, so he
        // stays on 40; Sam winning both remaining rounds replaces two of
        // his 9s with 10s, which is 38. The gap and the points available
        // are the wrong arithmetic on a total that is not a sum.
        var club = Setup(
            Enumerable.Range(0, 4).Select(_ => new[] { "Bill", "Sam" }).ToArray(),
            planned: 6,
            best: 4);

        var table = SeasonStore.ComputeStandings(club.Seasons.All.Single(), club.History.All, club.Profiles);

        Assert.Equal(40, table[0].TotalPoints);
        Assert.Equal(36, table[1].TotalPoints);

        var stakes = club.Ask();

        Assert.NotNull(stakes);
        Assert.True(
            stakes!.CannotBeCaught,
            "The gap says four points with twenty available, so only playing the rounds out finds that Bill is already champion.");
        Assert.Equal("Bill", stakes.LeaderName);
        Assert.Equal(2, stakes.RoundsLeft);
        Assert.Single(stakes.StillInIt);
    }

    [Fact]
    public void Without_the_drop_rule_the_same_season_is_still_open()
    {
        // The other side of the test above: the same results and the same
        // rounds left, counting every round, and Sam CAN catch him. So
        // the refusal above is the drop rule and not an accident of the
        // fixture.
        var club = Setup(
            Enumerable.Range(0, 4).Select(_ => new[] { "Bill", "Sam" }).ToArray(),
            planned: 6);

        var stakes = club.Ask();

        Assert.NotNull(stakes);
        Assert.False(stakes!.CannotBeCaught);
        Assert.Equal(2, stakes.StillInIt.Count);
    }

    [Fact]
    public void One_round_left_and_a_win_still_decides_it()
    {
        // Bill 40, Sam 36, one round to go and a win worth ten: the title
        // is settled today and both of them can take it. Not a refusal -
        // this is the sentence the feature exists for.
        var club = Setup(
            new[]
            {
                new[] { "Bill", "Sam" },
                new[] { "Bill", "Sam" },
                new[] { "Bill", "Sam" },
                new[] { "Bill", "Sam" },
            },
            planned: 5);

        var stakes = club.Ask();

        Assert.NotNull(stakes);
        Assert.Equal(1, stakes!.RoundsLeft);
        Assert.False(stakes.CannotBeCaught);

        // Bill 40, Sam 36: one win is ten, so Sam can still take it.
        Assert.Equal(2, stakes.StillInIt.Count);
    }

    [Fact]
    public void Level_at_the_top_is_a_lead_of_nothing()
    {
        var club = Setup(
            new[]
            {
                new[] { "Bill", "Sam" },
                new[] { "Sam", "Bill" },
                new[] { "Bill", "Sam" },
                new[] { "Sam", "Bill" },
            },
            planned: 5);

        var stakes = club.Ask();

        Assert.NotNull(stakes);
        Assert.Equal(0, stakes!.LeadBy);
        Assert.Equal(2, stakes.StillInIt.Count);
    }

    [Fact]
    public void One_round_more_of_the_drop_rule_leaves_the_same_title_open()
    {
        // The third side of the drop-rule pair above, and the sharpest:
        // the SAME four results and the same two rounds left, counting
        // best 5 instead of best 4, and Sam is back in it. Bill still
        // keeps 40; Sam winning both now scores 10+10+9+9+9 = 47, because
        // the fifth round he is allowed to count is one of the new ones.
        //
        // So the refusal in the best-4 test is the drop cap itself and
        // not the fixture, and one off the cap changes the answer.
        var club = Setup(
            Enumerable.Range(0, 4).Select(_ => new[] { "Bill", "Sam" }).ToArray(),
            planned: 6,
            best: 5);

        var stakes = club.Ask();

        Assert.NotNull(stakes);
        Assert.False(stakes!.CannotBeCaught);
        Assert.Equal(2, stakes.StillInIt.Count);
    }

    [Fact]
    public void The_leader_is_in_the_list_of_who_can_still_win_it()
    {
        var club = Setup(
            new[]
            {
                new[] { "Bill", "Sam" },
                new[] { "Bill", "Sam" },
                new[] { "Bill", "Sam" },
                new[] { "Bill", "Sam" },
            },
            planned: 5);

        var stakes = club.Ask();

        Assert.NotNull(stakes);
        Assert.Equal("Bill", stakes!.StillInIt[0]);
        Assert.Contains("Sam", stakes.StillInIt);
    }

    // ------------------------------------------------------------------
    //  A race is not a round
    // ------------------------------------------------------------------

    [Fact]
    public void A_club_with_no_race_days_says_nothing()
    {
        // No race days means no way to tell how many ROUNDS the races
        // counting towards this championship come to - and every sentence
        // here is about the rounds remaining, so there is nothing to say.
        var club = Setup(FourRoundsOfBillWinning(), planned: 6, best: 4);

        Assert.NotNull(club.Ask());
        Assert.Null(club.AskWithNoDays());
    }

    [Fact]
    public void Two_counting_races_on_one_day_says_nothing()
    {
        // THE BUG THIS REFUSAL IS: counting RACES as ROUNDS.
        //
        // A club running an A-main and a B-main hands one championship two
        // races on one night, and both carry RoundKind.Main. Four races
        // over three days is three rounds of a six-round season, with three
        // still to come - but counted as races it is four, so two remain,
        // so a rival is priced over two rounds instead of three and can be
        // told over a PA that they are out of a championship they are not
        // out of.
        //
        // Refused rather than guessed at: a season worth two races a
        // meeting has a best case worth two races a meeting, and the proof
        // downstream plays each remaining round out as ONE race. See
        // CommentaryReaders.RoundsCountedSoFar.
        var club = Setup(
            FourRoundsOfBillWinning(),
            planned: 6,
            best: 4,
            days: new[] { 0, 0, 1, 2 });

        // The same four races one to a day ARE the shape it answers - see
        // The_rounds_counted_it_reports_are_days_rather_than_races, which
        // is the same fixture without the doubled-up day. Setup is called
        // once per test on purpose: twice in one test gives a club two
        // championships rather than one changed one.
        Assert.Null(club.Ask());
    }

    [Fact]
    public void The_rounds_counted_it_reports_are_days_rather_than_races()
    {
        var club = Setup(FourRoundsOfBillWinning(), planned: 6, best: 4);

        var stakes = club.Ask();

        Assert.NotNull(stakes);
        Assert.Equal(4, club.History.All.Count);
        Assert.Equal(4, stakes!.RoundsCounted);
        Assert.Equal(2, stakes.RoundsLeft);
    }

    // ------------------------------------------------------------------
    //  The best case is the most POINTS, not the best PLACE
    // ------------------------------------------------------------------

    [Fact]
    public void A_bonus_row_below_the_podium_is_what_a_rivals_best_round_is_worth()
    {
        // THE SECOND TEST THAT MATTERS MOST IN THIS FILE.
        //
        // The scoring scheme is a list the club TYPES. This one pays 1 for
        // a win, 2 for second and 25 for third - a weighting somebody
        // entered on purpose, or a row edited with the one above it
        // forgotten. Either way "the best available result" and "the most
        // points available" are now two different places.
        //
        // Bill leads on 24, Sam has 8, two rounds to go. A proof that has
        // Sam WINNING both gains him two points and declares Bill champion.
        // Sam finishing THIRD twice gains him fifty. The claim "nobody left
        // can reach him" would have been said, in front of the man who
        // could, off arithmetic about the wrong place on the podium.
        //
        // See Season.BestPayingPosition and CommentaryReaders.FutureRound.
        var bonusAtThird = new[] { (Position: 1, Points: 1), (Position: 2, Points: 2), (Position: 3, Points: 25) };

        var club = Setup(
            Enumerable.Range(0, 4).Select(_ => new[] { "Bill", "Sam" }).ToArray(),
            planned: 6,
            scheme: bonusAtThird,
            carried: new[] { (Name: "Bill", Points: 20) });

        var table = SeasonStore.ComputeStandings(club.Seasons.All.Single(), club.History.All, club.Profiles);

        Assert.Equal("Bill", table[0].DriverName);
        Assert.Equal(24, table[0].TotalPoints);
        Assert.Equal(8, table[1].TotalPoints);

        var stakes = club.Ask();

        Assert.NotNull(stakes);
        Assert.False(
            stakes!.CannotBeCaught,
            "Sam can take third twice for fifty points. Proving the best POSITION rather than the best POINTS would have crowned Bill.");
        Assert.Equal(2, stakes.StillInIt.Count);
    }

    [Fact]
    public void A_scheme_that_takes_points_away_says_nothing()
    {
        // With a negative row, a leader's worst case stops being "scores
        // nothing". They can finish somewhere that COSTS them points and
        // end up below where staying at home would have left them - so a
        // proof built on their absence would claim a title that can still
        // be lost. Refused whole rather than priced wrong. See
        // Season.PenalisesAnyone.
        var withAPenalty = Ladder.Concat(new[] { (Position: 11, Points: -5) }).ToArray();

        var club = Setup(
            Enumerable.Range(0, 4).Select(_ => new[] { "Bill", "Sam" }).ToArray(),
            planned: 6,
            best: 4,
            scheme: withAPenalty);

        // The same season on the same ladder WITHOUT that one row answers,
        // and loudly - see
        // The_drop_rule_can_shut_a_rival_out_while_the_gap_says_otherwise,
        // which is this fixture with the ladder untouched. So the refusal
        // is the negative row and not the fixture.
        Assert.Null(club.Ask());
    }

    // ------------------------------------------------------------------
    //  The margin
    // ------------------------------------------------------------------

    [Fact]
    public void The_margin_is_to_the_driver_who_can_still_win_it()
    {
        // Bill 40, Sam 36, Carl 33, best 4 of 6, two rounds left.
        //
        // Sam is four points off and SHUT OUT: his four rounds of nine are
        // already his best four, so winning both remaining rounds swaps two
        // nines for two tens and leaves him on 38. Carl is seven points off
        // and still in it, because twenty-five of his thirty-three were
        // carried in from the club's old software and carried points are
        // not a round - so the rounds he wins are rounds he gets to keep.
        //
        // The copy says "only Bill and Carl can still win it" and then
        // states the margin. Measured to second in the TABLE that margin is
        // four, and the four belongs to Sam, who is not in the sentence.
        // Measured to the first driver who can actually win it, it is
        // seven, which is the number the sentence is about.
        var club = Setup(
            new[]
            {
                new[] { "Bill", "Sam", "Carl" },
                new[] { "Bill", "Sam" },
                new[] { "Bill", "Sam" },
                new[] { "Bill", "Sam" },
            },
            planned: 6,
            best: 4,
            carried: new[] { (Name: "Carl", Points: 25) });

        var table = SeasonStore.ComputeStandings(club.Seasons.All.Single(), club.History.All, club.Profiles);

        Assert.Equal(new[] { "Bill", "Sam", "Carl" }, table.Select(row => row.DriverName).ToArray());
        Assert.Equal(new[] { 40, 36, 33 }, table.Select(row => row.TotalPoints).ToArray());

        var stakes = club.Ask();

        Assert.NotNull(stakes);
        Assert.Equal(new[] { "Bill", "Carl" }, stakes!.StillInIt.ToArray());
        Assert.Equal(7, stakes.LeadBy);
    }

    [Fact]
    public void An_uncatchable_leader_still_reports_the_gap_to_second()
    {
        // Nobody to measure to, so the gap to second in the table is what
        // it carries - and the copy does not use the figure at all when
        // the title is already won.
        var club = Setup(
            Enumerable.Range(0, 4).Select(_ => new[] { "Bill", "Sam" }).ToArray(),
            planned: 6,
            best: 4);

        var stakes = club.Ask();

        Assert.NotNull(stakes);
        Assert.True(stakes!.CannotBeCaught);
        Assert.Equal(4, stakes.LeadBy);
    }

    // ------------------------------------------------------------------
    //  What it says
    // ------------------------------------------------------------------

    private static TitleStakes Riding(
        int roundsLeft,
        bool cannotBeCaught,
        params string[] stillInIt) =>
        new(
            Series,
            RoundsCounted: 4,
            RoundsLeft: roundsLeft,
            LeaderName: "Bill Hargrove",
            LeaderId: "bill",
            LeaderPoints: 148,
            LeadBy: 12,
            CannotBeCaught: cannotBeCaught,
            StillInIt: stillInIt);

    [Fact]
    public void An_uncatchable_leader_is_named_as_champion_with_the_figure_in_it()
    {
        var said = CommentaryStakesFacts.ForTheTitle(Riding(2, true, "Bill Hargrove"), "race-1");

        Assert.NotNull(said);
        Assert.Contains("Bill Hargrove", said!.Text, StringComparison.Ordinal);
        Assert.Contains("148", said.Text, StringComparison.Ordinal);
        Assert.Contains(Series, said.Text, StringComparison.Ordinal);

        // The rounds remaining are the frame for all of it.
        Assert.Contains("2 rounds", said.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_still_in_it_is_said_as_the_two_of_them()
    {
        var said = CommentaryStakesFacts.ForTheTitle(Riding(2, false, "Bill Hargrove", "Sam Teague"), "race-1");

        Assert.NotNull(said);
        Assert.Contains("only Bill Hargrove and Sam Teague", said!.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("12", said.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Three_still_in_it_lists_all_three()
    {
        var said = CommentaryStakesFacts.ForTheTitle(
            Riding(3, false, "Bill Hargrove", "Sam Teague", "Dave Bellamy"),
            "race-1");

        Assert.NotNull(said);
        Assert.Contains("Bill Hargrove, Sam Teague and Dave Bellamy", said!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_round_is_said_as_the_last_round()
    {
        var said = CommentaryStakesFacts.ForTheTitle(Riding(1, false, "Bill Hargrove", "Sam Teague"), "race-1");

        Assert.NotNull(said);
        Assert.Contains("last round", said!.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void More_than_three_still_in_it_builds_nothing()
    {
        // The builder refuses as well as the reader, because a record this
        // shape could be handed to it by anything.
        var said = CommentaryStakesFacts.ForTheTitle(
            Riding(3, false, "Bill Hargrove", "Sam Teague", "Dave Bellamy", "Carl Reed"),
            "race-1");

        Assert.Null(said);
    }

    [Fact]
    public void A_season_with_no_rounds_left_builds_nothing()
    {
        Assert.Null(CommentaryStakesFacts.ForTheTitle(Riding(0, false, "Bill Hargrove", "Sam Teague"), "race-1"));
    }

    [Fact]
    public void A_season_too_few_rounds_in_builds_nothing()
    {
        var thin = new TitleStakes(
            Series,
            RoundsCounted: 1,
            RoundsLeft: 3,
            LeaderName: "Bill Hargrove",
            LeaderId: "bill",
            LeaderPoints: 20,
            LeadBy: 5,
            CannotBeCaught: false,
            StillInIt: new[] { "Bill Hargrove", "Sam Teague" });

        Assert.Null(CommentaryStakesFacts.ForTheTitle(thin, "race-1"));
    }

    [Fact]
    public void A_leader_with_no_name_to_say_builds_nothing()
    {
        var nameless = Riding(2, true, string.Empty) with { LeaderName = string.Empty };

        Assert.Null(CommentaryStakesFacts.ForTheTitle(nameless, "race-1"));
    }

    [Fact]
    public void An_unnamed_championship_is_still_a_championship()
    {
        var said = CommentaryStakesFacts.ForTheTitle(
            Riding(2, true, "Bill Hargrove") with { SeasonName = string.Empty },
            "race-1");

        Assert.NotNull(said);
        Assert.Contains("the championship", said!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_claim_is_about_the_leader_so_it_cools_with_them()
    {
        var said = CommentaryStakesFacts.ForTheTitle(Riding(2, true, "Bill Hargrove"), "race-1");

        Assert.NotNull(said);
        Assert.Equal(FactTheme.Driver("bill"), said!.Theme);
    }

    [Fact]
    public void One_sentence_per_race_rather_than_per_night()
    {
        var first = CommentaryStakesFacts.ForTheTitle(Riding(2, true, "Bill Hargrove"), "race-1");
        var second = CommentaryStakesFacts.ForTheTitle(Riding(2, true, "Bill Hargrove"), "race-2");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.Key, second!.Key);
    }

    // ------------------------------------------------------------------
    //  The season's own arithmetic
    // ------------------------------------------------------------------

    [Fact]
    public void Rounds_remaining_is_never_negative()
    {
        var season = new Season { PlannedRounds = 6 };

        Assert.Equal(2, season.RoundsRemainingAfter(4));
        Assert.Equal(0, season.RoundsRemainingAfter(6));
        Assert.Equal(0, season.RoundsRemainingAfter(9));
        Assert.Equal(6, season.RoundsRemainingAfter(-3));
    }

    [Fact]
    public void A_season_that_never_said_how_long_it_is_answers_null()
    {
        Assert.Null(new Season().RoundsRemainingAfter(4));
        Assert.Null(new Season { PlannedRounds = 0 }.RoundsRemainingAfter(4));
    }

    [Fact]
    public void The_best_a_round_can_pay_is_read_off_the_clubs_own_table()
    {
        // Not the first row's points: a club that typed its own scheme can
        // have the best score anywhere in it, and a bonus row below the
        // podium is a real thing somebody has done.
        var odd = new Season
        {
            PointsScheme = new List<PointsSchemeEntry>
            {
                new() { Position = 1, Points = 9 },
                new() { Position = 2, Points = 6 },
                new() { Position = 3, Points = 12 },
            },
        };

        Assert.Equal(12, odd.MostPointsInARound());
    }

    [Fact]
    public void A_season_with_no_scheme_pays_nothing_rather_than_throwing()
    {
        Assert.Equal(0, new Season { PointsScheme = new List<PointsSchemeEntry>() }.MostPointsInARound());
    }

    [Fact]
    public void The_best_paying_place_need_not_be_the_win()
    {
        var bonusAtThird = new Season
        {
            PointsScheme = new List<PointsSchemeEntry>
            {
                new() { Position = 1, Points = 9 },
                new() { Position = 2, Points = 6 },
                new() { Position = 3, Points = 12 },
            },
        };

        Assert.Equal(3, bonusAtThird.BestPayingPosition());
        Assert.Equal(12, bonusAtThird.PointsForPosition(bonusAtThird.BestPayingPosition()));
    }

    [Fact]
    public void The_best_paying_place_is_read_through_the_lookup_that_scores_a_race()
    {
        // Two rows for first place. PointsForPosition takes the FIRST of
        // them, so this scheme pays five for a win and nobody can ever
        // score the thirty - which makes MostPointsInARound a figure no
        // round is worth, and the reason the proof asks for a PLACE and
        // then prices it through the same lookup that scores a race.
        var typedTwice = new Season
        {
            PointsScheme = new List<PointsSchemeEntry>
            {
                new() { Position = 1, Points = 5 },
                new() { Position = 1, Points = 30 },
                new() { Position = 2, Points = 9 },
            },
        };

        Assert.Equal(5, typedTwice.PointsForPosition(1));
        Assert.Equal(30, typedTwice.MostPointsInARound());

        Assert.Equal(2, typedTwice.BestPayingPosition());
        Assert.Equal(9, typedTwice.PointsForPosition(typedTwice.BestPayingPosition()));
    }

    [Fact]
    public void The_lowest_place_wins_a_tie_so_the_proof_builds_the_smallest_round()
    {
        var level = new Season
        {
            PointsScheme = new List<PointsSchemeEntry>
            {
                new() { Position = 3, Points = 10 },
                new() { Position = 1, Points = 10 },
                new() { Position = 2, Points = 10 },
            },
        };

        Assert.Equal(1, level.BestPayingPosition());
    }

    [Fact]
    public void A_scheme_where_nothing_scores_has_no_best_paying_place()
    {
        Assert.Equal(0, new Season { PointsScheme = new List<PointsSchemeEntry>() }.BestPayingPosition());

        Assert.Equal(
            0,
            new Season
            {
                PointsScheme = Enumerable.Range(1, 5)
                    .Select(p => new PointsSchemeEntry { Position = p, Points = 0 })
                    .ToList(),
            }.BestPayingPosition());
    }

    [Fact]
    public void A_place_numbered_zero_or_less_is_not_a_place_a_round_can_be_built_to()
    {
        var odd = new Season
        {
            PointsScheme = new List<PointsSchemeEntry>
            {
                new() { Position = 0, Points = 99 },
                new() { Position = -2, Points = 50 },
                new() { Position = 2, Points = 7 },
            },
        };

        Assert.Equal(2, odd.BestPayingPosition());
    }

    [Fact]
    public void A_negative_row_anywhere_is_what_makes_a_worst_case_unpriceable()
    {
        Assert.False(new Season().PenalisesAnyone());

        Assert.True(
            new Season
            {
                PointsScheme = Ladder
                    .Select(row => new PointsSchemeEntry { Position = row.Position, Points = row.Points })
                    .Append(new PointsSchemeEntry { Position = 11, Points = -5 })
                    .ToList(),
            }.PenalisesAnyone());
    }

    // ------------------------------------------------------------------
    //  The room it is given
    // ------------------------------------------------------------------

    /// <summary>
    /// Forty characters each - CommentaryService.Field's cap, so these are
    /// the longest names any of this can ever be built from.
    /// </summary>
    private const string LongSeries = "Winter Series Presented By Hobby Hut Ltd";

    private const string LongLeader = "Bartholomew Hargrove-Fitzwilliam, Senior";
    private const string LongSecond = "Konstantin Vasilyevich Abramov-Rowley Jr";
    private const string LongThird = "Maximilian Featherstonehaugh-Lancaster!!";

    private static TitleStakes TheLongestItCanBe(int roundsLeft) =>
        new(
            LongSeries,
            RoundsCounted: 996,
            RoundsLeft: roundsLeft,
            LeaderName: LongLeader,
            LeaderId: "bill",
            LeaderPoints: 99999,
            LeadBy: 99999,
            CannotBeCaught: false,
            StillInIt: new[] { LongLeader, LongSecond, LongThird });

    [Fact]
    public void The_longest_sentence_it_can_build_fits_the_room_it_is_given()
    {
        // WHY THIS TEST EXISTS. FactPack.Lay drops a block that will not
        // fit WHOLE - there is no half a sentence - so a cap below the
        // worst case does not shorten the claim, it deletes it, and the
        // console still reports the block as built. This shipped with a cap
        // of 240 on 12 Oct 2026, and a club with long driver names and a
        // three-way title fight would have heard nothing at all about the
        // championship on the one night it was worth hearing about.
        //
        // Every name here is forty characters, which is
        // CommentaryService.Field's cap and therefore the longest any of
        // them can be.
        foreach (var roundsLeft in new[] { 1, 2, 999 })
        {
            var said = CommentaryStakesFacts.ForTheTitle(TheLongestItCanBe(roundsLeft), "race-1");

            Assert.NotNull(said);
            Assert.True(
                said!.Text.Length <= CommentaryStakesFacts.MostChars,
                $"{said.Text.Length} characters with {roundsLeft} rounds left, "
                + $"against {CommentaryStakesFacts.MostChars} of room: FactPack.Lay would drop the whole claim.");
        }
    }

    [Fact]
    public void The_longest_sentence_it_can_build_would_not_have_fitted_two_hundred_and_forty()
    {
        // The other side of the test above: the cap it shipped with was not
        // merely tight, it was short of the worst case - so this is the
        // bug rather than a margin somebody chose.
        var said = CommentaryStakesFacts.ForTheTitle(TheLongestItCanBe(1), "race-1");

        Assert.NotNull(said);
        Assert.True(said!.Text.Length > 240, $"Only {said.Text.Length} characters - the fixture has stopped being the worst case.");
    }

    [Fact]
    public void An_uncatchable_leader_with_the_longest_names_fits_too()
    {
        var settled = TheLongestItCanBe(2) with
        {
            CannotBeCaught = true,
            StillInIt = new[] { LongLeader },
        };

        var said = CommentaryStakesFacts.ForTheTitle(settled, "race-1");

        Assert.NotNull(said);
        Assert.True(said!.Text.Length <= CommentaryStakesFacts.MostChars, $"{said.Text.Length} characters.");
    }
}
