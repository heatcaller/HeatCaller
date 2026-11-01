namespace HeatCaller.Tests;

/// <summary>
/// The horizon: what a gap is DOING and how much road is left.
///
/// BattleWatch answers whether two cars are having a fight, which is a
/// fact about the past and the right one. This answers the question a room
/// actually has while it watches them, and the whole risk is that an
/// answer to that question becomes a PREDICTION. Most of this file is the
/// arithmetic staying arithmetic.
/// </summary>
public sealed class RaceHorizonTests
{
    private static RaceHorizon Watching(DateTime? now = null)
    {
        var clock = now ?? new DateTime(2026, 10, 14, 20, 0, 0, DateTimeKind.Local);

        return new RaceHorizon(() => clock);
    }

    private static PairGap Pair(string ahead, string behind, double gap, int position = 1, int lapsDown = 0) =>
        new(ahead, behind, position, gap, lapsDown);

    private static Horizon? Tick(RaceHorizon watch, int lapsRun, int lapsLeft, params PairGap[] pairs)
    {
        watch.Observe(pairs, lapsRun, lapsLeft);

        return watch.Pick();
    }

    // ------------------------------------------------------------------
    //  What it reads
    // ------------------------------------------------------------------

    [Fact]
    public void A_gap_coming_down_is_closing()
    {
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", 1.20));

        var horizon = Tick(watch, 6, 4, Pair("carl", "dave", 1.00));

        Assert.NotNull(horizon);
        Assert.Equal(HorizonKind.Closing, horizon!.Kind);
        Assert.Equal("carl", horizon.AheadId);
        Assert.Equal("dave", horizon.BehindId);
        Assert.Equal(1.00, horizon.GapSeconds, 3);
        Assert.Equal(0.20, horizon.PerLap, 3);
        Assert.Equal(4, horizon.LapsLeft);
    }

    [Fact]
    public void A_gap_the_laps_left_would_cover_is_within_reach()
    {
        // The strongest thing this class will say, and it means exactly
        // what it is defined as: this rate, over these laps, adds up to
        // this gap. That is a sum, not a claim.
        var watch = Watching();

        Tick(watch, 5, 3, Pair("carl", "dave", 0.80));

        var horizon = Tick(watch, 6, 3, Pair("carl", "dave", 0.40));

        Assert.NotNull(horizon);
        Assert.Equal(HorizonKind.WithinReach, horizon!.Kind);
        Assert.True(horizon.Buys >= horizon.GapSeconds);
    }

    [Fact]
    public void A_gap_going_out_is_slipping_away()
    {
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", 0.40));

        var horizon = Tick(watch, 6, 4, Pair("carl", "dave", 0.70));

        Assert.NotNull(horizon);
        Assert.Equal(HorizonKind.SlippingAway, horizon!.Kind);
        Assert.True(horizon.PerLap < 0);
    }

    [Fact]
    public void A_steady_gap_is_left_to_BattleWatch()
    {
        // Two cars locked together is a fight, and saying so is the other
        // detector's line. This one only has something to say when the
        // gap is MOVING.
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", 0.40));
        Tick(watch, 6, 4, Pair("carl", "dave", 0.41));

        Assert.Null(Tick(watch, 7, 3, Pair("carl", "dave", 0.40)));
    }

    // ------------------------------------------------------------------
    //  The silence
    // ------------------------------------------------------------------

    [Fact]
    public void A_rate_needs_laps_to_be_a_rate()
    {
        // The ticks are a half-second UI timer. Several samples on one lap
        // are ONE sample, or the rate would be taken over no laps at all
        // and the figure said out loud would be one nobody could check.
        var watch = Watching();

        Tick(watch, 5, 3, Pair("carl", "dave", 0.90));
        Tick(watch, 5, 3, Pair("carl", "dave", 0.70));

        Assert.Null(Tick(watch, 5, 3, Pair("carl", "dave", 0.50)));
    }

    [Fact]
    public void Nothing_is_offered_once_there_is_no_road_left()
    {
        var watch = Watching();

        Tick(watch, 9, 2, Pair("carl", "dave", 0.90));

        Assert.NotNull(Tick(watch, 10, 1, Pair("carl", "dave", 0.60)));
        Assert.Null(Tick(watch, 11, 0, Pair("carl", "dave", 0.30)));
    }

    [Fact]
    public void A_gap_too_wide_to_be_decided_is_not_a_horizon()
    {
        // A gap coming down from eight seconds is a car making its way
        // forward over a whole race, not something about to be decided.
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", 9.00));

        Assert.Null(Tick(watch, 6, 4, Pair("carl", "dave", 8.00)));
    }

    [Fact]
    public void A_car_a_lap_down_is_not_in_a_fight_with_the_one_in_front()
    {
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", 1.20, lapsDown: 1));

        Assert.Null(Tick(watch, 6, 4, Pair("carl", "dave", 1.00, lapsDown: 1)));
        Assert.Equal(0, watch.Watching);
    }

    [Fact]
    public void A_gap_that_cannot_be_measured_this_tick_is_skipped()
    {
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", double.NaN));

        Assert.Equal(0, watch.Watching);
    }

    [Fact]
    public void A_pair_that_is_no_longer_adjacent_is_no_longer_a_pair()
    {
        // The gap between two cars that now have somebody between them is
        // not a gap either of them is racing.
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", 1.20));

        Assert.Equal(1, watch.Watching);

        Tick(watch, 6, 4, Pair("carl", "eve", 0.50));

        Assert.Equal(1, watch.Watching);
        Assert.Null(Tick(watch, 6, 4, Pair("carl", "eve", 0.50)));
    }

    // ------------------------------------------------------------------
    //  Which one gets the line
    // ------------------------------------------------------------------

    [Fact]
    public void The_fight_for_the_lead_takes_the_line_from_a_tighter_one_further_back()
    {
        // A room that hears the announcer spend its one forward-looking
        // line on the fight for seventh has been told the lead is worth
        // the same, which is not true.
        var watch = Watching();

        Tick(
            watch,
            5,
            4,
            Pair("carl", "dave", 0.90),
            Pair("eve", "fred", 0.30, position: 7));

        var horizon = Tick(
            watch,
            6,
            3,
            Pair("carl", "dave", 0.70),
            Pair("eve", "fred", 0.20, position: 7));

        Assert.NotNull(horizon);
        Assert.Equal(1, horizon!.Position);
        Assert.Equal("dave", horizon.BehindId);
    }

    [Fact]
    public void A_pair_just_talked_about_is_left_alone_for_a_while()
    {
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", 1.20));

        var horizon = Tick(watch, 6, 4, Pair("carl", "dave", 1.00));

        Assert.NotNull(horizon);

        watch.Said(horizon!);

        Assert.Null(Tick(watch, 7, 3, Pair("carl", "dave", 0.80)));
    }

    [Fact]
    public void Two_cars_trading_a_position_stay_one_story()
    {
        // A key built from "who is ahead" would start a new trail on every
        // swap and never build a rate at all - and two cars trading a
        // place four times are the single best thing happening.
        var watch = Watching();

        //
        // Still one trail - but the RATE restarts on each swap (17 Oct 2026,
        // Bug Hunt 8, A07-2): a rate taken across a pass mixes gaps measured
        // against different cars and means nothing.
        Tick(watch, 5, 4, Pair("carl", "dave", 0.60));
        Tick(watch, 6, 3, Pair("dave", "carl", 0.40));

        Assert.Equal(1, watch.Watching);

        Assert.Null(Tick(watch, 7, 2, Pair("carl", "dave", 0.20)));

        Assert.Equal(1, watch.Watching);

        var horizon = Tick(watch, 8, 2, Pair("carl", "dave", 0.10));

        Assert.NotNull(horizon);
        Assert.Equal("carl", horizon!.AheadId);
        Assert.Equal("dave", horizon.BehindId);
        Assert.Equal(0.10, horizon.PerLap, 3);
    }

    [Fact]
    public void A_rate_is_not_taken_across_a_pass()
    {
        // 17 Oct 2026, Bug Hunt 8, A07-2: carl a second up on dave, then
        // dave half a second up on carl, read as "closing at half a second
        // a lap" for the car that had just lost the place.
        var watch = Watching();

        Tick(watch, 5, 4, Pair("carl", "dave", 1.0));

        Assert.Null(Tick(watch, 6, 3, Pair("dave", "carl", 0.5)));
    }

    [Fact]
    public void A_rate_under_a_twentieth_is_not_said_as_nothing()
    {
        // 17 Oct 2026, Bug Hunt 8, A07-5: MeaningfulRate is two hundredths,
        // and GapSpoken calls anything under a twentieth "nothing".
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", 0.60));

        var horizon = Tick(watch, 6, 4, Pair("carl", "dave", 0.57));

        Assert.NotNull(horizon);

        var line = RaceHorizon.Say(horizon!, "Carl", "Dave");

        Assert.DoesNotContain("nothing", line);
        Assert.Contains("three hundredths a lap", line);
    }

    [Fact]
    public void A_new_race_clears_every_trail_and_every_cooldown()
    {
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", 1.20));
        Tick(watch, 6, 4, Pair("carl", "dave", 1.00));

        watch.Reset();

        Assert.Equal(0, watch.Watching);
        Assert.Null(watch.Pick());
    }

    // ------------------------------------------------------------------
    //  The wording
    // ------------------------------------------------------------------

    [Fact]
    public void No_line_says_who_is_going_to_win()
    {
        // THE RULE, and it is the reason a forward-looking line is allowed
        // at all. "He'll catch him" is one careless template away from a
        // club's announcer being wrong out loud in front of the people it
        // was wrong about.
        var cases = new List<Horizon>();

        var closing = Watching();
        Tick(closing, 5, 5, Pair("carl", "dave", 1.20));
        cases.Add(Tick(closing, 6, 4, Pair("carl", "dave", 1.00))!);

        var reach = Watching();
        Tick(reach, 5, 3, Pair("carl", "dave", 0.80));
        cases.Add(Tick(reach, 6, 3, Pair("carl", "dave", 0.40))!);

        var slipping = Watching();
        Tick(slipping, 5, 5, Pair("carl", "dave", 0.40));
        cases.Add(Tick(slipping, 6, 4, Pair("carl", "dave", 0.70))!);

        Assert.Equal(3, cases.Count);

        foreach (var horizon in cases)
        {
            Assert.NotNull(horizon);

            var line = RaceHorizon.Say(horizon, "Carl", "Dave");

            Assert.False(string.IsNullOrWhiteSpace(line));

            foreach (var forecast in new[] { "will win", "will catch", "going to catch", "going to win", "has it won" })
            {
                Assert.DoesNotContain(forecast, line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void A_line_gives_the_room_the_gap_the_rate_and_the_laps()
    {
        var watch = Watching();

        Tick(watch, 5, 5, Pair("carl", "dave", 1.20));

        var horizon = Tick(watch, 6, 4, Pair("carl", "dave", 1.00));
        var line = RaceHorizon.Say(horizon!, "Carl", "Dave");

        Assert.Contains("Carl", line);
        Assert.Contains("Dave", line);
        Assert.Contains("a second", line);
        Assert.Contains("two tenths a lap", line);
        Assert.Contains("four laps", line);
        Assert.DoesNotMatch("[0-9]", line);
    }

    [Fact]
    public void A_fight_further_back_is_named_by_its_position_and_the_lead_by_its_name()
    {
        var lead = Watching();
        Tick(lead, 5, 3, Pair("carl", "dave", 0.80));
        var leadLine = RaceHorizon.Say(Tick(lead, 6, 3, Pair("carl", "dave", 0.40))!, "Carl", "Dave");

        Assert.Contains("the lead", leadLine);

        var back = Watching();
        Tick(back, 5, 3, Pair("eve", "fred", 0.80, position: 4));
        var backLine = RaceHorizon.Say(Tick(back, 6, 3, Pair("eve", "fred", 0.40, position: 4))!, "Eve", "Fred");

        Assert.Contains("P four", backLine);
    }

    // ------------------------------------------------------------------
    //  How much road is left - the question the 14 Oct entry left open
    // ------------------------------------------------------------------

    [Fact]
    public void A_lap_limited_race_knows_exactly_how_many_laps_are_left()
    {
        var road = RaceHorizon.LapsLeftIn(10, 8, null, TimeSpan.FromMinutes(1), 5.0);

        Assert.Equal(new RoadLeft(2, Estimated: false), road);
    }

    [Fact]
    public void A_timed_race_works_it_out_from_the_clock_and_says_it_did()
    {
        // Twelve seconds left at five seconds a lap: the leader crosses at
        // five and ten, and the clock runs out on the lap after - which
        // they still finish, because the flag waits for the next crossing.
        var road = RaceHorizon.LapsLeftIn(null, 30, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(288), 5.0);

        Assert.Equal(new RoadLeft(3, Estimated: true, TimeLeft: TimeSpan.FromSeconds(12)), road);
    }

    [Fact]
    public void With_both_limits_the_nearer_end_is_the_road_there_is()
    {
        // Two laps of limit left, but ten laps of clock: two, and exact.
        Assert.Equal(
            new RoadLeft(2, Estimated: false),
            RaceHorizon.LapsLeftIn(20, 18, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(4), 6.0));

        // The other way round: the clock ends it first, and is an estimate.
        Assert.Equal(
            new RoadLeft(2, Estimated: true, TimeLeft: TimeSpan.FromSeconds(10)),
            RaceHorizon.LapsLeftIn(40, 10, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(290), 6.0));
    }

    [Fact]
    public void A_race_with_nothing_to_go_on_has_no_horizon()
    {
        // Open practice, or a timed race before the leader has a lap to
        // give its pace: nothing honest to say about laps left.
        Assert.Null(RaceHorizon.LapsLeftIn(null, 4, null, TimeSpan.FromMinutes(2), 5.0));
        Assert.Null(RaceHorizon.LapsLeftIn(null, 0, TimeSpan.FromMinutes(5), TimeSpan.Zero, null));
    }

    [Fact]
    public void A_clock_that_has_run_out_still_leaves_the_lap_in_progress()
    {
        // The flag waits for the leader's next crossing, so the last lap is
        // still one lap of road - the answer a lap-limited race gives too.
        Assert.Equal(
            new RoadLeft(1, Estimated: true, TimeLeft: TimeSpan.Zero),
            RaceHorizon.LapsLeftIn(null, 50, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5.1), 5.0));
    }

    [Fact]
    public void The_count_is_taken_from_the_leader_s_last_crossing()
    {
        // Twelve seconds left, a five second lap, three seconds into it:
        // fifteen seconds of laps from the line, so three crossings to go.
        // Counted from now it would have been two.
        Assert.Equal(
            new RoadLeft(3, Estimated: true, TimeLeft: TimeSpan.FromSeconds(12)),
            RaceHorizon.LapsLeftIn(null, 30, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(288), 5.0, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void A_timed_road_is_said_as_about_so_much_time_and_never_as_laps()
    {
        // Was "about three laps" (15 Oct 2026) - and a timed race heard
        // "three circuits to run" from the commentary (1 Nov 2026). A race
        // the clock ends is said in time.
        var watch = Watching();

        watch.Observe(new[] { Pair("carl", "dave", 0.80) }, 5, 4, estimated: true, timeLeft: TimeSpan.FromSeconds(20));
        watch.Observe(new[] { Pair("carl", "dave", 0.60) }, 6, 3, estimated: true, timeLeft: TimeSpan.FromSeconds(15));

        var horizon = watch.Pick();

        Assert.NotNull(horizon);
        Assert.True(horizon!.Estimated);
        Assert.Equal(TimeSpan.FromSeconds(15), horizon.TimeLeft);

        var line = RaceHorizon.Say(horizon, "Carl", "Dave");

        Assert.Contains("about fifteen seconds", line);
        Assert.DoesNotContain("three laps", line);
    }

    [Fact]
    public void A_known_road_is_said_without_hedging()
    {
        var watch = Watching();

        watch.Observe(new[] { Pair("carl", "dave", 0.80) }, 5, 4);
        watch.Observe(new[] { Pair("carl", "dave", 0.60) }, 6, 3);

        var line = RaceHorizon.Say(watch.Pick()!, "Carl", "Dave");

        Assert.Contains("three laps", line);
        Assert.DoesNotContain("about", line);
    }
}
