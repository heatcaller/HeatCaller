namespace HeatCaller.Tests;

/// <summary>
/// The battle detector: what counts as a fight, what doesn't, and which
/// one gets the line when several are going on.
///
/// The thing being defended here is the announcer's credibility. A segment
/// that calls the leader catching a backmarker "the fight of the night",
/// or that calls the same two cars twenty times in one heat, is worse than
/// no segment at all - so most of these tests are about what it refuses to
/// say.
/// </summary>
public sealed class BattleWatchTests
{
    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 7, 19, 30, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>One pair, nose to tail, fighting over a position.</summary>
    private static PairGap Close(string ahead, string behind, double gap, int position = 1) =>
        new(ahead, behind, position, gap, 0);

    /// <summary>Feeds the same pair in for a while, a tick every five seconds.</summary>
    private static void Circulate(BattleWatch watch, Clock clock, PairGap pair, int ticks, TimeSpan? every = null)
    {
        var step = every ?? TimeSpan.FromSeconds(5);

        for (var i = 0; i < ticks; i++)
        {
            watch.Observe(new[] { pair });
            clock.Advance(step);
        }
    }

    [Fact]
    public void Two_cars_locked_together_long_enough_become_a_fight()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        Assert.Null(watch.Pick());

        Circulate(watch, clock, Close("A", "B", 0.4), ticks: 5);

        var battle = watch.Pick();

        Assert.NotNull(battle);
        Assert.Equal("A", battle!.AheadId);
        Assert.Equal("B", battle.BehindId);
        Assert.Equal(1, battle.Position);
        Assert.Equal(0.4, battle.NowSeconds, 3);
    }

    [Fact]
    public void A_moment_is_not_a_fight()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        // Three ticks over ten seconds: they were briefly near each other.
        Circulate(watch, clock, Close("A", "B", 0.3), ticks: 3);

        Assert.Null(watch.Pick());
    }

    [Fact]
    public void A_fast_polling_loop_cannot_turn_two_seconds_into_a_battle()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        // Plenty of samples, almost no elapsed time. Without the duration
        // rule a half-second poll would call a fight four ticks after the
        // cars happened to line up.
        Circulate(watch, clock, Close("A", "B", 0.2), ticks: 10, every: TimeSpan.FromMilliseconds(200));

        Assert.Null(watch.Pick());
    }

    [Fact]
    public void The_leader_catching_a_lapped_car_is_not_a_fight()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        for (var i = 0; i < 10; i++)
        {
            // Nose to tail all race - and a lap apart.
            watch.Observe(new[] { new PairGap("Leader", "Backmarker", 1, 0.2, 1) });
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Null(watch.Pick());
        Assert.Equal(0, watch.Watching);
    }

    [Fact]
    public void A_gap_that_opens_up_ends_the_fight_and_starts_it_again_from_nothing()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        Circulate(watch, clock, Close("A", "B", 0.4), ticks: 5);

        Assert.NotNull(watch.Pick());

        // He got away.
        watch.Observe(new[] { Close("A", "B", 2.4) });
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Null(watch.Pick());

        // And then it came back - but it is a new fight, not the old one
        // with its clock still running.
        Circulate(watch, clock, Close("A", "B", 0.4), ticks: 2);

        Assert.Null(watch.Pick());
    }

    [Fact]
    public void A_third_car_coming_between_them_ends_the_fight()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        Circulate(watch, clock, Close("A", "B", 0.3), ticks: 5);

        Assert.NotNull(watch.Pick());

        // A and B are no longer adjacent: C is between them now, and the
        // story being followed is over.
        watch.Observe(new[] { Close("A", "C", 0.9), Close("C", "B", 0.9) });

        Assert.Equal(0, watch.Watching);
        Assert.Null(watch.Pick());
    }

    [Fact]
    public void Swapping_places_without_separating_is_the_same_fight_and_is_worth_more()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        Circulate(watch, clock, Close("A", "B", 0.3), ticks: 3);

        // B gets by. Same two cars, same fight, now the other way round.
        Circulate(watch, clock, Close("B", "A", 0.2), ticks: 3);

        var battle = watch.Pick();

        Assert.NotNull(battle);
        Assert.Equal("B", battle!.AheadId);
        Assert.Equal("A", battle.BehindId);
        Assert.Equal(1, battle.LeadChanges);
        Assert.Equal(6, battle.Samples);
        Assert.Equal(0.2, battle.ClosestSeconds, 3);
    }

    [Fact]
    public void Places_actually_changing_hands_beats_a_closer_gap_that_never_moves()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        for (var i = 0; i < 6; i++)
        {
            watch.Observe(new[]
            {
                // A processional fight for the lead, very close, nothing happening.
                Close("A", "B", 0.1, position: 1),

                // A scrap for fourth that keeps swapping.
                i % 2 == 0 ? Close("C", "D", 0.5, position: 4) : Close("D", "C", 0.5, position: 4),
            });

            clock.Advance(TimeSpan.FromSeconds(5));
        }

        var battle = watch.Pick();

        Assert.NotNull(battle);
        Assert.True(battle!.LeadChanges > 0, "the fight that keeps swapping should win");
        Assert.Equal(4, battle.Position);
    }

    [Fact]
    public void Between_two_equally_dull_fights_the_one_further_up_the_order_wins()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        for (var i = 0; i < 6; i++)
        {
            watch.Observe(new[]
            {
                Close("E", "F", 0.3, position: 7),
                Close("A", "B", 0.3, position: 2),
            });

            clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(2, watch.Pick()!.Position);
    }

    [Fact]
    public void The_same_two_cars_nose_to_tail_all_heat_are_one_story_and_not_twenty()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        Circulate(watch, clock, Close("A", "B", 0.3), ticks: 5);

        var battle = watch.Pick();

        Assert.NotNull(battle);

        watch.Said(battle!);

        Circulate(watch, clock, Close("A", "B", 0.3), ticks: 5);

        Assert.Null(watch.Pick());

        // Once the cooldown is out they are fair game again - a fight that
        // has lasted four minutes is worth coming back to.
        clock.Advance(TimeSpan.FromMinutes(3));
        Circulate(watch, clock, Close("A", "B", 0.3), ticks: 2);

        Assert.NotNull(watch.Pick());
    }

    [Fact]
    public void A_new_race_is_a_clean_sheet_even_for_drivers_who_fought_in_their_heat()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        Circulate(watch, clock, Close("A", "B", 0.3), ticks: 5);
        watch.Said(watch.Pick()!);

        watch.Reset();

        Assert.Equal(0, watch.Watching);

        Circulate(watch, clock, Close("A", "B", 0.3), ticks: 5);

        Assert.NotNull(watch.Pick());
    }

    [Fact]
    public void A_missing_transponder_is_ignored_rather_than_followed()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        for (var i = 0; i < 6; i++)
        {
            watch.Observe(new[] { new PairGap("A", "", 1, 0.3, 0), new PairGap("", "B", 2, 0.3, 0) });
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(0, watch.Watching);
        Assert.Null(watch.Pick());
    }

    [Fact]
    public void Picking_a_fight_does_not_use_it_up()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        Circulate(watch, clock, Close("A", "B", 0.3), ticks: 5);

        // Pick is free and repeatable: a caller that then can't afford the
        // line, or whose write fails, must not have silently spent the
        // fight by asking about it. Only Said spends it.
        Assert.NotNull(watch.Pick());
        Assert.NotNull(watch.Pick());
        Assert.NotNull(watch.Pick());
    }

    [Fact]
    public void A_penalty_that_swaps_them_is_not_a_lead_change()
    {
        // 8 Nov 2026. The gap is race time, which carries penalties: half a
        // second given to A put B "ahead" with nothing happening on track,
        // and the fight was called as having changed hands.
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        Circulate(watch, clock, Close("A", "B", 0.3), ticks: 5);

        Assert.NotNull(watch.Pick());

        Circulate(watch, clock, new PairGap("B", "A", 1, 0.2, 0, 0, 0, 500), ticks: 5);

        var battle = watch.Pick();

        Assert.NotNull(battle);
        Assert.Equal(0, battle!.LeadChanges);
        Assert.Equal(5, battle.Samples);
    }

    [Fact]
    public void A_penalty_starts_the_fight_again_from_where_it_now_stands()
    {
        var clock = new Clock();
        var watch = new BattleWatch(clock.Read);

        Circulate(watch, clock, Close("A", "B", 0.1), ticks: 5);

        // B takes a half second: the gap they are racing at is now 0.6, and
        // a closest of 0.1 from before it is not a gap anyone can see.
        Circulate(watch, clock, new PairGap("A", "B", 1, 0.6, 0, 0, 0, 500), ticks: 2);

        Assert.Null(watch.Pick());

        Circulate(watch, clock, new PairGap("A", "B", 1, 0.6, 0, 0, 0, 500), ticks: 3);

        var battle = watch.Pick();

        Assert.NotNull(battle);
        Assert.Equal(0.6, battle!.ClosestSeconds, 3);
    }
}
