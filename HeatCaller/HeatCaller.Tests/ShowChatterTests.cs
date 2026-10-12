namespace HeatCaller.Tests;

/// <summary>
/// The chattiness dial (12 Oct 2026).
///
/// The pacing gives an evening a shape - quiet through practice and the
/// heats, everything it held back spent on the top main. The shape is right
/// for every club; the level it sits at is taste. So this scales it, and the
/// two promises these tests exist to keep are that the SHAPE survives every
/// stop on the dial, and that a club which has never touched it hears
/// exactly what it heard before the dial existed.
/// </summary>
public sealed class ShowChatterTests
{
    private static readonly RaceBilling[] QuietestFirst =
    {
        RaceBilling.Practice, RaceBilling.Heat, RaceBilling.LesserMain, RaceBilling.TopMain,
    };

    private static int Cap(RaceBilling billing, string? key, bool lastOfTheNight = false) =>
        ShowChatter.Scaled(new RaceStature(billing, lastOfTheNight).AirtimeCap, key);

    // ------------------------------------------------------------------
    //  A club that never touches it hears no change
    // ------------------------------------------------------------------

    [Fact]
    public void A_club_that_has_never_set_it_is_on_the_pacing_exactly_as_it_shipped()
    {
        foreach (var billing in QuietestFirst)
        {
            var shape = new RaceStature(billing, false).AirtimeCap;

            Assert.Equal(shape, Cap(billing, null));
            Assert.Equal(shape, Cap(billing, string.Empty));
            Assert.Equal(shape, Cap(billing, "   "));
            Assert.Equal(shape, Cap(billing, ShowChatter.Standard.Key));
        }
    }

    [Fact]
    public void A_setting_from_a_newer_build_is_standard_rather_than_a_guess_at_the_nearest()
    {
        // A club whose settings came back from a backup written by a later
        // version gets the pacing it has always had, not a volume nobody
        // in the room chose.
        Assert.Equal(ShowChatter.Standard, ShowChatter.For("deafening"));
        Assert.Equal(ShowChatter.Standard, ShowChatter.For(null));

        Assert.Equal(
            new RaceStature(RaceBilling.TopMain, false).AirtimeCap,
            Cap(RaceBilling.TopMain, "deafening"));
    }

    [Fact]
    public void A_fresh_budget_is_on_standard()
    {
        Assert.Equal(string.Empty, new CommentaryBudget().Chatter);
    }

    // ------------------------------------------------------------------
    //  The shape survives the volume
    // ------------------------------------------------------------------

    [Fact]
    public void Every_stop_on_the_dial_is_still_quiet_early_and_loud_late()
    {
        // THE ONE THAT MATTERS. Non-stop in a heat must still be quieter
        // than Non-stop in the top main, or the dial has flattened the
        // very curve the pacing exists to create.
        foreach (var level in ShowChatter.All)
        {
            var caps = QuietestFirst.Select(billing => Cap(billing, level.Key)).ToList();

            for (var i = 1; i < caps.Count; i++)
            {
                Assert.True(
                    caps[i] >= caps[i - 1],
                    $"At {level.Name} the pacing is no longer quiet early and loud late: {string.Join(" / ", caps)}");
            }

            // And the two ends are properly apart, not merely non-decreasing.
            Assert.True(
                caps[^1] > caps[0],
                $"At {level.Name} the top main gets no more of the night's voice than a practice session.");
        }
    }

    [Fact]
    public void Turning_the_dial_up_never_makes_a_race_quieter()
    {
        foreach (var billing in QuietestFirst)
        {
            var caps = ShowChatter.All.Select(level => Cap(billing, level.Key)).ToList();

            for (var i = 1; i < caps.Count; i++)
            {
                Assert.True(
                    caps[i] >= caps[i - 1],
                    $"For {billing} the dial is not monotonic: {string.Join(" / ", caps)}");
            }

            // Quietest and loudest are genuinely different, or the dial is
            // a setting that does nothing.
            Assert.True(caps[^1] > caps[0], $"For {billing} the dial makes no difference at all.");
        }
    }

    [Fact]
    public void The_quietest_stop_is_sparing_rather_than_mute()
    {
        // A ceiling of zero is not a quiet announcer, it is a broken one.
        foreach (var billing in QuietestFirst)
        {
            Assert.True(Cap(billing, "sparing") >= ShowChatter.Least);
        }
    }

    [Fact]
    public void Nothing_the_dial_can_do_goes_past_the_hard_ceiling()
    {
        // The loudest case there is: the last race of the night, the top
        // main, turned all the way up.
        Assert.InRange(
            Cap(RaceBilling.TopMain, "nonstop", lastOfTheNight: true),
            ShowChatter.Least,
            ShowChatter.Most);

        Assert.Equal(ShowChatter.Most, ShowChatter.Scaled(1000, "nonstop"));
        Assert.Equal(ShowChatter.Least, ShowChatter.Scaled(0, "sparing"));
    }

    [Fact]
    public void The_last_race_of_the_night_keeps_its_extra_at_every_volume()
    {
        foreach (var level in ShowChatter.All)
        {
            var ordinary = Cap(RaceBilling.Heat, level.Key);
            var finale = Cap(RaceBilling.Heat, level.Key, lastOfTheNight: true);

            Assert.True(finale > ordinary, $"At {level.Name} the last race of the night sounds like any other heat.");
        }
    }

    // ------------------------------------------------------------------
    //  The second voice
    // ------------------------------------------------------------------

    [Fact]
    public void The_co_host_only_comes_out_in_the_heats_when_the_club_asks_for_a_chatty_night()
    {
        // Held for the mains by default, because a signal spent on
        // everything is not a signal. A club that asks for a chatty
        // evening means the two voices as much as it means the count.
        Assert.False(ShowChatter.TwoVoicesEverywhere(null));
        Assert.False(ShowChatter.TwoVoicesEverywhere("sparing"));
        Assert.False(ShowChatter.TwoVoicesEverywhere("measured"));
        Assert.False(ShowChatter.TwoVoicesEverywhere(ShowChatter.Standard.Key));

        Assert.True(ShowChatter.TwoVoicesEverywhere("lively"));
        Assert.True(ShowChatter.TwoVoicesEverywhere("nonstop"));
    }

    // ------------------------------------------------------------------
    //  The table itself
    // ------------------------------------------------------------------

    [Fact]
    public void The_stops_are_distinct_and_ordered_quietest_first()
    {
        var keys = ShowChatter.All.Select(one => one.Key).ToList();
        var names = ShowChatter.All.Select(one => one.Name).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Quietest first, so the dial reads left to right like a volume
        // control rather than in whatever order somebody added them.
        for (var i = 1; i < ShowChatter.All.Count; i++)
        {
            Assert.True(
                ShowChatter.All[i].Scale > ShowChatter.All[i - 1].Scale,
                "The stops are no longer in order, so the page offers them out of sequence.");
        }

        // Standard is in the list and is the one that changes nothing.
        Assert.Contains(ShowChatter.Standard, ShowChatter.All);
        Assert.Equal(1.0, ShowChatter.Standard.Scale);

        foreach (var level in ShowChatter.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(level.Key));
            Assert.False(string.IsNullOrWhiteSpace(level.Name));
            Assert.False(string.IsNullOrWhiteSpace(level.Blurb));
        }
    }

    [Fact]
    public void The_page_says_what_the_club_actually_gets_and_works_it_out_from_the_real_tables()
    {
        // A line that repeated the figures would end up lying the first
        // time either table moved.
        var said = ShowChatter.Describe("lively");

        Assert.Contains($"{Cap(RaceBilling.Heat, "lively")} spoken segments", said, StringComparison.Ordinal);
        Assert.Contains($"and {Cap(RaceBilling.TopMain, "lively")} in the top main", said, StringComparison.Ordinal);

        // And the two things a club would otherwise conclude the setting
        // was broken over.
        Assert.Contains("Intros and recaps are never held back", said, StringComparison.Ordinal);
        Assert.Contains("allowance", said, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("co-host out in the heats", ShowChatter.Describe("nonstop"), StringComparison.Ordinal);
        Assert.Contains("co-host held for the mains", ShowChatter.Describe("sparing"), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  The budget acts on it
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("sparing")]
    [InlineData("measured")]
    [InlineData("standard")]
    [InlineData("lively")]
    public void The_ceiling_the_budget_enforces_is_the_scaled_one(string key)
    {
        var clock = new ChatterClock();
        var budget = new CommentaryBudget(clock.Now);

        budget.NewMeeting("chatter-night");
        budget.Stature = new RaceStature(RaceBilling.Heat, false);
        budget.Chatter = key;

        // No allowance heard from the relay, so airtime is the only thing
        // that can refuse a line.
        Assert.False(budget.AllowanceKnown);

        var said = 0;

        // Twenty asks at twenty-five seconds is 475 seconds - inside the
        // ten-minute window, so nothing ages out of it mid-test and the
        // count really is the ceiling. The same shape, and the same
        // reasoning, as the airtime tests in CommentaryBudgetTests.
        for (var i = 0; i < 20; i++)
        {
            var verdict = budget.Reserve(SegmentKind.ComingUpNext);

            if (verdict.Allowed)
            {
                budget.Settle(verdict.Ticket);
                said++;
            }

            clock.Advance(TimeSpan.FromSeconds(25));
        }

        Assert.Equal(Cap(RaceBilling.Heat, key), said);
    }

    [Fact]
    public void A_race_is_still_never_sent_out_in_silence_at_the_quietest_setting()
    {
        // Intros and recaps are Essential and are never refused for
        // airtime at all. A club on Sparing loses the colour, never the
        // race.
        var clock = new ChatterClock();
        var budget = new CommentaryBudget(clock.Now);

        budget.NewMeeting("chatter-night");
        budget.Stature = new RaceStature(RaceBilling.Practice, false);
        budget.Chatter = "sparing";

        for (var i = 0; i < 20; i++)
        {
            var filler = budget.Reserve(SegmentKind.ComingUpNext);

            if (filler.Allowed)
            {
                budget.Settle(filler.Ticket);
            }

            clock.Advance(TimeSpan.FromSeconds(25));
        }

        Assert.False(budget.Peek(SegmentKind.ComingUpNext).Allowed);

        var intro = budget.Reserve(SegmentKind.RaceIntro);
        Assert.True(intro.Allowed);
        budget.Settle(intro.Ticket);

        var recap = budget.Reserve(SegmentKind.RaceRecap);
        Assert.True(recap.Allowed);
        budget.Settle(recap.Ticket);
    }

    [Fact]
    public void A_new_night_keeps_the_dial_where_the_club_left_it()
    {
        // It is the club's own setting rather than anything about tonight,
        // and a new day opened mid-evening must not put the announcer
        // quietly back to Standard.
        var budget = new CommentaryBudget { Chatter = "lively" };

        budget.NewMeeting("a-different-night");

        Assert.Equal("lively", budget.Chatter);

        // Where the stature, which IS about tonight, is forgotten.
        Assert.Equal(RaceStature.Unknown, budget.Stature);
    }

    [Fact]
    public void A_refusal_names_the_dial_when_the_dial_is_what_moved_the_ceiling()
    {
        // A club that has turned this down and then wonders why the night
        // is quiet has to be able to read the answer rather than guess.
        var clock = new ChatterClock();
        var budget = new CommentaryBudget(clock.Now);

        budget.NewMeeting("chatter-night");
        budget.Stature = new RaceStature(RaceBilling.Heat, false);
        budget.Chatter = "sparing";

        for (var i = 0; i < Cap(RaceBilling.Heat, "sparing"); i++)
        {
            var verdict = budget.Reserve(SegmentKind.ComingUpNext);
            Assert.True(verdict.Allowed);
            budget.Settle(verdict.Ticket);
            clock.Advance(TimeSpan.FromSeconds(25));
        }

        var refused = budget.Peek(SegmentKind.ComingUpNext);

        Assert.False(refused.Allowed);
        Assert.Contains("Sparing", refused.Reason, StringComparison.Ordinal);
        Assert.Contains("a heat", refused.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A clock the test winds on, so a ten-minute window fills in microseconds.</summary>
    private sealed class ChatterClock
    {
        private DateTime _at = new(2026, 10, 12, 19, 0, 0, DateTimeKind.Local);

        internal Func<DateTime> Now => () => _at;

        internal void Advance(TimeSpan by) => _at = _at.Add(by);
    }
}
