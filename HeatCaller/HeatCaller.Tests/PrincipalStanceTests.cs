namespace HeatCaller.Tests;

/// <summary>
/// The position the Team Principal comes in with (18 Oct 2026).
///
/// PrincipalStance.For is a pure function of a night, which is most of why
/// it is worth testing at all: the stance is what makes the segment sound
/// like somebody rather than a press release, and it is picked without a
/// model being involved.
///
/// The claims that matter here are the ORDER - several of these are true
/// at once on a real night, and the question is which a principal would
/// lead with - and the loyalty rule, which goes out with every stance and
/// is the one thing that makes a frustrated principal safe to put on a PA
/// in a room of forty people who all know each other.
/// </summary>
public sealed class PrincipalStanceTests
{
    private static TeamNight Night(
        int races = 3,
        int wins = 0,
        int podiums = 0,
        int retirements = 0,
        bool paceReadable = true,
        double consistency = 2.5,
        long cost = 0) =>
        new("sam", "Sam", races, new[] { 4, 5, 6 }, 12, wins, podiums, retirements, 0,
            paceReadable, consistency, cost);

    [Fact]
    public void A_car_that_did_not_finish_leaves_him_defending_whatever_else_is_on_the_sheet()
    {
        // Checked first and on its own: it is the thing the room watched
        // happen and the thing the host is going to ask about. A principal
        // who opened on the championship with a car still in the fence
        // would be a principal nobody listens to.
        Assert.Equal(
            PrincipalStance.Defensive,
            PrincipalStance.For(Night(retirements: 1, wins: 1, podiums: 1), bestFinishTonight: 1, championshipPosition: 1));
    }

    [Fact]
    public void A_night_well_above_the_table_vindicates_him()
    {
        Assert.Equal(
            PrincipalStance.Vindicated,
            PrincipalStance.For(Night(podiums: 1), bestFinishTonight: 2, championshipPosition: 8));
    }

    [Fact]
    public void A_night_well_below_the_table_leaves_him_defending_it()
    {
        Assert.Equal(
            PrincipalStance.Defensive,
            PrincipalStance.For(Night(), bestFinishTonight: 9, championshipPosition: 2));
    }

    [Fact]
    public void The_two_table_stances_are_the_same_measurement_in_opposite_directions()
    {
        // A different bar for each would mean a club heard "vindicated"
        // more often than "defensive" for reasons nobody could point at.
        Assert.Equal(PrincipalStance.PlacesAboveToClaim, PrincipalStance.PlacesBelowToDefend);
    }

    [Fact]
    public void A_night_near_where_the_table_says_they_belong_is_not_either_of_those()
    {
        var stance = PrincipalStance.For(Night(podiums: 1), bestFinishTonight: 3, championshipPosition: 4);

        Assert.NotEqual(PrincipalStance.Vindicated, stance);
        Assert.NotEqual(PrincipalStance.Defensive, stance);
    }

    [Fact]
    public void A_club_with_no_championship_still_gets_the_other_stances()
    {
        Assert.Equal(
            PrincipalStance.Pleased,
            PrincipalStance.For(Night(podiums: 1), bestFinishTonight: 2, championshipPosition: null));
    }

    [Fact]
    public void Pace_without_a_result_makes_him_frustrated()
    {
        Assert.Equal(
            PrincipalStance.Frustrated,
            PrincipalStance.For(Night(cost: 4000, consistency: 5.0), bestFinishTonight: 6, championshipPosition: null));
    }

    [Fact]
    public void Frustration_needs_the_same_evidence_the_pace_fact_needs()
    {
        // A principal sounding frustrated about a gap the facts do not
        // mention is a principal frustrated about nothing - so the stance
        // asks the question the same way PrincipalFacts.WhereThePaceWent
        // does, and the two cannot contradict each other on air.
        var thin = PrincipalStance.For(
            Night(paceReadable: false, cost: 9000), bestFinishTonight: 6, championshipPosition: null);

        Assert.NotEqual(PrincipalStance.Frustrated, thin);

        var cheap = PrincipalStance.For(
            Night(cost: PrincipalFacts.LeastCostWorthSaying - 1), bestFinishTonight: 6, championshipPosition: null);

        Assert.NotEqual(PrincipalStance.Frustrated, cheap);
    }

    [Fact]
    public void Pace_lost_on_a_night_that_still_ended_on_the_podium_is_not_frustration()
    {
        Assert.Equal(
            PrincipalStance.Pleased,
            PrincipalStance.For(Night(podiums: 1, cost: 4000, consistency: 5.0), bestFinishTonight: 2, championshipPosition: null));
    }

    [Fact]
    public void A_podium_leaves_him_cautiously_pleased()
    {
        Assert.Equal(
            PrincipalStance.Pleased,
            PrincipalStance.For(Night(podiums: 1), bestFinishTonight: 3, championshipPosition: null));
    }

    [Fact]
    public void A_mid_pack_night_with_nothing_in_it_is_blunt()
    {
        // Blunt IS the honest position on a night like that, and it is the
        // one stance that makes a segment shorter rather than longer.
        Assert.Equal(
            PrincipalStance.Blunt,
            PrincipalStance.For(Night(), bestFinishTonight: 6, championshipPosition: null));
    }

    [Fact]
    public void One_race_is_not_enough_of_a_night_for_a_stance_at_all()
    {
        Assert.Equal(
            PrincipalStance.Plain,
            PrincipalStance.For(Night(races: 1), bestFinishTonight: 6, championshipPosition: null));
    }

    // ------------------------------------------------------------------
    // The brief
    // ------------------------------------------------------------------

    [Fact]
    public void Every_stance_that_speaks_carries_the_loyalty_rule()
    {
        // NeverHisOwnDriver is not optional, including on the two unhappy
        // stances - a principal has somebody specific to blame and a real
        // one would do it.
        foreach (var stance in PrincipalStance.All.Where(s => s.Brief.Length > 0))
        {
            var brief = PrincipalStance.BriefFor(stance);

            Assert.Contains("never criticizes his own driver", brief);
            Assert.Contains("family in the room", brief);
        }
    }

    [Fact]
    public void The_plain_principal_spends_no_words_in_the_brief_at_all()
    {
        // An instruction to have no position is still an instruction, and
        // it spends words the model reads instead of a fact.
        Assert.Equal(string.Empty, PrincipalStance.BriefFor(PrincipalStance.Plain));
    }

    [Fact]
    public void No_stance_tells_him_how_the_race_will_go()
    {
        // The affirmative forms only: Vindicated's brief says he "never
        // claims he predicted it", and a test that banned the word
        // "predict" outright would fail on the sentence that exists to
        // stop him predicting.
        foreach (var stance in PrincipalStance.All)
        {
            Assert.DoesNotContain("will win", stance.Brief, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("will finish", stance.Brief, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("expect to beat", stance.Brief, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_one_stance_allowed_to_be_pleased_with_itself_is_told_not_to_claim_it_saw_it_coming()
    {
        Assert.Contains("never claims he predicted it", PrincipalStance.Vindicated.Brief);
    }
}
