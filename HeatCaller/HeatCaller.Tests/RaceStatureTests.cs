namespace HeatCaller.Tests;

/// <summary>
/// How big a race is, and what that buys it (11 Oct 2026).
///
/// The airtime cap was a flat six spoken segments per ten minutes from the
/// first practice session of the afternoon to the last lap of the A-main,
/// and a programme that talks at the same rate all evening teaches a room
/// to stop listening. These tests defend the shape of the pacing and, more
/// importantly, the one case that must not regress: a club with no
/// programme at all keeps exactly the broadcast it had.
/// </summary>
public sealed class RaceStatureTests
{
    private static RoundDefinition Round(RoundKind kind, int order, int mainTier = -1) => new()
    {
        Kind = kind,
        Order = order,
        MainTier = mainTier,
        ClassName = "Stock 17.5",
        RoundLabel = kind == RoundKind.Main ? "A-Main" : "Heat",
    };

    // ------------------------------------------------------------------
    //  A club with no programme loses nothing
    // ------------------------------------------------------------------

    [Fact]
    public void A_race_with_nothing_to_size_it_by_keeps_the_cap_the_app_has_always_had()
    {
        // THE ONE THAT MATTERS. A club that has never opened the Program
        // tab has not asked for a quieter afternoon - it has simply told
        // the app nothing, and taking its announcer down to three lines in
        // ten minutes for that would be this feature breaking the app for
        // the clubs least able to say why.
        Assert.Equal(CommentaryBudget.AirtimeCap, RaceStature.Unknown.AirtimeCap);
        Assert.Equal(CommentaryBudget.AirtimeCap, RaceStature.Of(null, null).AirtimeCap);

        // And it keeps the second voice too.
        Assert.True(RaceStature.Unknown.WorthTwoVoices);
    }

    [Fact]
    public void A_stature_nobody_filled_in_is_the_unknown_one_rather_than_practice()
    {
        // The enum's default value is what an unset field reads as, so
        // Unknown has to be first. A default of Practice would have
        // silenced every club with no programme.
        Assert.Equal(RaceBilling.Unknown, default(RaceBilling));
        Assert.Equal(RaceStature.Unknown, default(RaceStature));
    }

    [Fact]
    public void A_fresh_budget_is_on_the_flat_cap()
    {
        var budget = new CommentaryBudget();

        Assert.Equal(RaceStature.Unknown, budget.Stature);
        Assert.Equal(CommentaryBudget.AirtimeCap, budget.Stature.AirtimeCap);
    }

    // ------------------------------------------------------------------
    //  The shape of the night
    // ------------------------------------------------------------------

    [Fact]
    public void The_night_is_quiet_early_and_loud_late()
    {
        var practice = new RaceStature(RaceBilling.Practice, false).AirtimeCap;
        var heat = new RaceStature(RaceBilling.Heat, false).AirtimeCap;
        var lesser = new RaceStature(RaceBilling.LesserMain, false).AirtimeCap;
        var top = new RaceStature(RaceBilling.TopMain, false).AirtimeCap;

        Assert.True(practice < heat, "Practice is background and should be the quietest the show ever is.");
        Assert.True(heat < lesser, "The heats are not being held back any more.");
        Assert.True(lesser < top, "The top main gets no more of the night's voice than a B-main.");

        // The heats come DOWN from what every race used to get, which is
        // the half of this that costs something, and the top main goes up.
        Assert.True(heat < CommentaryBudget.AirtimeCap);
        Assert.True(top > CommentaryBudget.AirtimeCap);
    }

    [Fact]
    public void The_last_race_on_the_programme_gets_more_whatever_it_is()
    {
        // Everyone still in the hall is watching this one, and after it
        // they go home - so there is no later race the airtime is being
        // saved for.
        foreach (var billing in new[] { RaceBilling.Practice, RaceBilling.Heat, RaceBilling.LesserMain, RaceBilling.TopMain })
        {
            var ordinary = new RaceStature(billing, false).AirtimeCap;
            var finale = new RaceStature(billing, true).AirtimeCap;

            Assert.True(finale > ordinary, $"The last race of the night sounds the same as any other {billing}.");
        }
    }

    // ------------------------------------------------------------------
    //  Read off the club's own programme
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(RoundKind.Practice, -1, RaceBilling.Practice)]
    [InlineData(RoundKind.Qualifier, -1, RaceBilling.Heat)]
    [InlineData(RoundKind.Main, 0, RaceBilling.TopMain)]
    [InlineData(RoundKind.Main, 1, RaceBilling.LesserMain)]
    [InlineData(RoundKind.Main, 3, RaceBilling.LesserMain)]
    public void A_round_is_sized_by_its_kind_and_its_tier(RoundKind kind, int tier, RaceBilling expected)
    {
        Assert.Equal(expected, RaceStature.Of(Round(kind, 1, tier), null).Billing);
    }

    [Fact]
    public void An_untiered_main_is_the_top_main_rather_than_a_lesser_one()
    {
        // A club that runs one final and never set a tier on it has run
        // its A-main. MainTier is -1 for mains made before tiers existed.
        Assert.Equal(RaceBilling.TopMain, RaceStature.Of(Round(RoundKind.Main, 1), null).Billing);
    }

    [Fact]
    public void Practice_is_never_the_climax_of_the_night()
    {
        // A club that puts an open practice session at the end of the
        // evening has not made it the finale, and a club whose last entry
        // is practice should still have its final main treated as one.
        var practice = Round(RoundKind.Practice, 99);

        Assert.False(RaceStature.Of(practice, null).LastOfTheNight);
    }

    // ------------------------------------------------------------------
    //  What it buys the second voice
    // ------------------------------------------------------------------

    [Fact]
    public void Two_voices_are_held_for_the_races_that_earn_them()
    {
        Assert.True(new RaceStature(RaceBilling.TopMain, false).WorthTwoVoices);
        Assert.True(new RaceStature(RaceBilling.LesserMain, false).WorthTwoVoices);

        // The signal is only worth something if it is not spent on
        // everything.
        Assert.False(new RaceStature(RaceBilling.Heat, false).WorthTwoVoices);
        Assert.False(new RaceStature(RaceBilling.Practice, false).WorthTwoVoices);

        // Except for the race the whole room stayed for.
        Assert.True(new RaceStature(RaceBilling.Heat, true).WorthTwoVoices);
    }

    // ------------------------------------------------------------------
    //  The budget actually acts on it
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(RaceBilling.Practice)]
    [InlineData(RaceBilling.Heat)]
    [InlineData(RaceBilling.LesserMain)]
    [InlineData(RaceBilling.TopMain)]
    public void The_cap_the_budget_enforces_follows_the_race_in_front_of_it(RaceBilling billing)
    {
        var clock = new StatureClock();
        var budget = Fresh(clock, new RaceStature(billing, false));

        var said = 0;

        // Far more asks than any cap would allow, each far enough apart
        // that the minimum gap is never what refuses one.
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

        Assert.Equal(new RaceStature(billing, false).AirtimeCap, said);
    }

    [Fact]
    public void A_new_night_forgets_how_big_the_last_race_was()
    {
        // Without this, a new day opened straight after an A-main would
        // pace its first practice session as a final.
        var budget = new CommentaryBudget
        {
            Stature = new RaceStature(RaceBilling.TopMain, true),
        };

        budget.NewMeeting("a-different-night");

        Assert.Equal(RaceStature.Unknown, budget.Stature);
    }

    [Fact]
    public void A_refusal_says_which_cap_it_was_refused_against()
    {
        // A quiet heat has to be explainable at midnight rather than
        // looking like a fault.
        var clock = new StatureClock();
        var budget = Fresh(clock, new RaceStature(RaceBilling.Practice, false));

        for (var i = 0; i < new RaceStature(RaceBilling.Practice, false).AirtimeCap; i++)
        {
            var verdict = budget.Reserve(SegmentKind.ComingUpNext);
            Assert.True(verdict.Allowed);
            budget.Settle(verdict.Ticket);
            clock.Advance(TimeSpan.FromSeconds(25));
        }

        var refused = budget.Peek(SegmentKind.ComingUpNext);

        Assert.False(refused.Allowed);
        Assert.Contains("practice", refused.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("which allows", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_club_on_the_flat_cap_gets_no_extra_words_in_its_refusal()
    {
        // Nothing to explain, so nothing is said - the refusal a club has
        // always read stays the one it reads.
        var clock = new StatureClock();
        var budget = Fresh(clock, RaceStature.Unknown);

        for (var i = 0; i < CommentaryBudget.AirtimeCap; i++)
        {
            var verdict = budget.Reserve(SegmentKind.ComingUpNext);
            Assert.True(verdict.Allowed);
            budget.Settle(verdict.Ticket);
            clock.Advance(TimeSpan.FromSeconds(25));
        }

        var refused = budget.Peek(SegmentKind.ComingUpNext);

        Assert.False(refused.Allowed);
        Assert.DoesNotContain("which allows", refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A budget with no allowance heard from the relay at all - so there
    /// is no money gate whatsoever and airtime is the only thing that can
    /// refuse a line. The same shape the airtime tests in
    /// CommentaryBudgetTests use, and for the same reason.
    ///
    /// The stature is set AFTER NewMeeting, because NewMeeting forgets it:
    /// a new night knows nothing about what is on it yet.
    /// </summary>
    private static CommentaryBudget Fresh(StatureClock clock, RaceStature stature)
    {
        var budget = new CommentaryBudget(clock.Now);

        budget.NewMeeting("stature-night");
        budget.Stature = stature;

        Assert.False(budget.AllowanceKnown);

        return budget;
    }

    /// <summary>
    /// A clock the test winds on, so a ten-minute window can be filled in
    /// a few microseconds. Its own rather than shared, for the reason the
    /// source-reading helpers give: a test class that can be broken by a
    /// change made for another one is a test class nobody trusts.
    /// </summary>
    private sealed class StatureClock
    {
        private DateTime _at = new(2026, 10, 11, 19, 0, 0, DateTimeKind.Local);

        internal Func<DateTime> Now => () => _at;

        internal void Advance(TimeSpan by) => _at = _at.Add(by);
    }
}
