namespace HeatCaller.Tests;

/// <summary>
/// The Team Principal's own facts (18 Oct 2026) - and, mostly, the
/// thresholds that keep him from saying anything.
///
/// Every builder in PrincipalFacts has a bar, and the bars are the design:
/// one race is not a night, four-two is not a rivalry, a handful of laps
/// is not a pace reading. So most of this file is about what comes back
/// null - the same shape CommentaryHistoryFactsTests takes, and for the
/// same reason.
///
/// The three claims worth watching, because each of them was a real
/// mistake caught while this was built: that a retirement is never said as
/// "0th", that the night's cost is never read out as a lap time, and that
/// a head-to-head is read from OUR driver's side whichever way round the
/// caller filled the record in.
/// </summary>
public sealed class PrincipalFactsTests
{
    private static TeamNight Night(
        string id = "sam",
        string name = "Sam",
        int races = 3,
        int[]? finishes = null,
        int field = 12,
        int wins = 0,
        int podiums = 1,
        int retirements = 0,
        int warnings = 0,
        bool paceReadable = true,
        double consistency = 2.5,
        long cost = 0,
        string pronouns = "") =>
        new(id, name, races, finishes ?? new[] { 4, 2, 5 }, field, wins, podiums, retirements, warnings,
            paceReadable, consistency, cost, pronouns);

    // ------------------------------------------------------------------
    // The night
    // ------------------------------------------------------------------

    [Fact]
    public void The_night_is_every_race_in_the_order_they_ran()
    {
        var block = PrincipalFacts.TheNight(Night(finishes: new[] { 4, 2, 1 }, wins: 1), "night-1");

        Assert.NotNull(block);
        Assert.Contains("4th, 2nd and then 1st", block!.Text);
        Assert.Contains("Sam", block.Text);
    }

    [Fact]
    public void One_race_is_not_a_night()
    {
        // The race the show has just covered. A "night" line about it would
        // be the principal reading the result back to a room that watched
        // it, in the one segment that exists to say something else.
        Assert.Null(PrincipalFacts.TheNight(Night(races: 1, finishes: new[] { 2 }), "night-1"));
    }

    [Fact]
    public void A_race_they_did_not_finish_is_never_said_as_a_position()
    {
        var block = PrincipalFacts.TheNight(
            Night(finishes: new[] { 3, 0, 2 }, retirements: 1), "night-1");

        Assert.NotNull(block);
        Assert.Contains("did not finish", block!.Text);
        Assert.DoesNotContain("0th", block.Text);
    }

    [Fact]
    public void How_big_the_field_was_is_said_once_and_not_per_placing()
    {
        var block = PrincipalFacts.TheNight(Night(field: 19), "night-1");

        Assert.NotNull(block);

        // "fourth of nineteen, second of nineteen, fifth of nineteen" reads
        // as a results table, and the principal is the one guest who is not
        // reading one out.
        var mentions = block!.Text.Split("19").Length - 1;
        Assert.Equal(1, mentions);
    }

    [Fact]
    public void Winning_every_race_is_said_as_that_rather_than_counted()
    {
        var block = PrincipalFacts.TheNight(
            Night(races: 3, finishes: new[] { 1, 1, 1 }, wins: 3, podiums: 3), "night-1");

        Assert.NotNull(block);
        Assert.Contains("won every race", block!.Text);
    }

    [Fact]
    public void A_night_with_no_name_to_say_it_about_says_nothing()
    {
        Assert.Null(PrincipalFacts.TheNight(Night(name: "   "), "night-1"));
    }

    // ------------------------------------------------------------------
    // Where the pace went
    // ------------------------------------------------------------------

    [Fact]
    public void Time_lost_to_untidy_laps_is_worth_saying()
    {
        var block = PrincipalFacts.WhereThePaceWent(Night(cost: 4200, consistency: 5.0), "night-1");

        Assert.NotNull(block);
        Assert.Contains("more pace", block!.Text);
    }

    [Fact]
    public void What_the_night_cost_is_never_read_out_as_a_lap_time()
    {
        // THE RULE THE WHOLE SEGMENT TURNS ON. The principal is the one
        // voice in the broadcast that does not read times out - the room
        // heard them called hours ago. A figure in seconds-and-thousandths
        // here would be the principal becoming the announcer.
        var block = PrincipalFacts.WhereThePaceWent(Night(cost: 4200, consistency: 5.0), "night-1");

        Assert.NotNull(block);
        Assert.DoesNotContain("4.2", block!.Text);
        Assert.DoesNotContain("4200", block.Text);
        Assert.DoesNotContain("ms", block.Text);
        Assert.Contains("four seconds", block.Text);
    }

    [Fact]
    public void The_cost_is_always_rounded_down()
    {
        // Rounded down so the principal never makes his own driver's
        // evening sound worse than the record says it was.
        var block = PrincipalFacts.WhereThePaceWent(Night(cost: 3990, consistency: 5.0), "night-1");

        Assert.NotNull(block);
        Assert.Contains("three seconds", block!.Text);
    }

    [Fact]
    public void A_driver_at_their_own_limit_gets_the_other_sentence()
    {
        var block = PrincipalFacts.WhereThePaceWent(Night(cost: 200, consistency: 1.1), "night-1");

        Assert.NotNull(block);
        Assert.Contains("own limit", block!.Text);
    }

    [Fact]
    public void An_ordinary_night_says_nothing_about_pace()
    {
        Assert.Null(PrincipalFacts.WhereThePaceWent(Night(cost: 400, consistency: 2.6), "night-1"));
    }

    [Fact]
    public void Too_few_laps_to_read_says_nothing_about_pace()
    {
        Assert.Null(PrincipalFacts.WhereThePaceWent(
            Night(paceReadable: false, cost: 9000, consistency: 6.0), "night-1"));
    }

    // ------------------------------------------------------------------
    // What it cost
    // ------------------------------------------------------------------

    [Fact]
    public void A_retirement_is_counted_and_a_warning_is_counted()
    {
        var block = PrincipalFacts.WhatItCost(Night(retirements: 1, warnings: 2), "night-1");

        Assert.NotNull(block);
        Assert.Contains("did not finish one of tonight's races", block!.Text);
        Assert.Contains("2 warnings", block.Text);
    }

    [Fact]
    public void Retiring_from_everything_is_said_as_that()
    {
        var block = PrincipalFacts.WhatItCost(Night(races: 2, retirements: 2), "night-1");

        Assert.NotNull(block);
        Assert.Contains("did not finish any of his 2 races", block!.Text);
    }

    // ------------------------------------------------------------------
    // Their own pronouns (21 Oct 2026)
    // ------------------------------------------------------------------

    [Fact]
    public void A_driver_whose_profile_says_she_her_is_never_his_in_the_facts()
    {
        var pace = PrincipalFacts.WhereThePaceWent(Night(cost: 4200, consistency: 5.0, pronouns: "she/her"), "night-1");
        var limit = PrincipalFacts.WhereThePaceWent(Night(cost: 200, consistency: 1.1, pronouns: "she/her"), "night-1");
        var cost = PrincipalFacts.WhatItCost(Night(races: 2, retirements: 2, pronouns: "she/her"), "night-1");

        Assert.Contains("her untidy laps cost her", pace!.Text);
        Assert.Contains("within herself", limit!.Text);
        Assert.Contains("did not finish any of her 2 races", cost!.Text);

        foreach (var text in new[] { pace.Text, limit.Text, cost.Text })
        {
            Assert.DoesNotMatch(@"\b(his|him|himself)\b", text);
        }
    }

    [Fact]
    public void They_them_reads_as_they_them_and_blank_reads_as_it_always_did()
    {
        var them = PrincipalFacts.WhereThePaceWent(Night(cost: 200, consistency: 1.1, pronouns: "they/them"), "night-1");
        var blank = PrincipalFacts.WhereThePaceWent(Night(cost: 200, consistency: 1.1), "night-1");

        Assert.Contains("within themselves all night - lap after lap at their own limit", them!.Text);
        Assert.Contains("within himself all night - lap after lap at his own limit", blank!.Text);
    }

    [Fact]
    public void A_clean_night_costs_nothing_and_says_nothing()
    {
        Assert.Null(PrincipalFacts.WhatItCost(Night(), "night-1"));
    }

    [Fact]
    public void Nothing_in_here_calls_anything_a_crash()
    {
        // A night's crashes are not written down - see TeamNight.Warnings.
        // The figure that looks like them is zero on a clean race and zero
        // on every result saved before the app counted it.
        var block = PrincipalFacts.WhatItCost(Night(retirements: 2, warnings: 3), "night-1");

        Assert.NotNull(block);
        Assert.DoesNotContain("crash", block!.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("threw", block.Text, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Against the rival
    // ------------------------------------------------------------------

    private static NightRival Rival(int ahead = 1, int behind = 1) => new("dave", "Dave", ahead, behind);

    [Fact]
    public void A_season_record_and_tonights_go_out_together()
    {
        var block = PrincipalFacts.AgainstTheRival(
            Night(), Rival(ahead: 0, behind: 2), new HeadToHead("sam", "dave", 5, 4), "night-1");

        Assert.NotNull(block);
        Assert.Contains("Sam ahead 5-4", block!.Text);
        Assert.Contains("Dave finished ahead of him in two of tonight's races", block.Text);
    }

    [Fact]
    public void The_record_reads_from_our_drivers_side_whichever_way_round_it_was_filled_in()
    {
        // A builder that assumed AId was our driver would print every
        // record backwards for half the grid - silently, in a sentence
        // naming two people in the room.
        var ours = PrincipalFacts.AgainstTheRival(
            Night(), Rival(), new HeadToHead("sam", "dave", 5, 4), "night-1");

        var reversed = PrincipalFacts.AgainstTheRival(
            Night(), Rival(), new HeadToHead("dave", "sam", 4, 5), "night-1");

        Assert.NotNull(ours);
        Assert.NotNull(reversed);
        Assert.Equal(ours!.Text, reversed!.Text);
        Assert.Contains("Sam ahead 5-4", ours.Text);
    }

    [Fact]
    public void A_thin_record_is_not_a_rivalry()
    {
        Assert.Null(PrincipalFacts.AgainstTheRival(
            Night(), Rival(), new HeadToHead("sam", "dave", 3, 2), "night-1"));
    }

    [Fact]
    public void A_one_sided_record_is_not_worth_asking_about()
    {
        // A principal asked about a rivalry his driver leads nine-one is
        // being asked about something that is not a rivalry.
        Assert.Null(PrincipalFacts.AgainstTheRival(
            Night(), Rival(), new HeadToHead("sam", "dave", 9, 1), "night-1"));
    }

    [Fact]
    public void A_pair_who_did_not_meet_tonight_get_no_line()
    {
        Assert.Null(PrincipalFacts.AgainstTheRival(
            Night(), Rival(ahead: 0, behind: 0), new HeadToHead("sam", "dave", 6, 5), "night-1"));
    }

    [Fact]
    public void A_level_record_is_said_as_level()
    {
        var block = PrincipalFacts.AgainstTheRival(
            Night(), Rival(ahead: 1, behind: 1), new HeadToHead("sam", "dave", 4, 4), "night-1");

        Assert.NotNull(block);
        Assert.Contains("level at 4 apiece", block!.Text);
        Assert.Contains("split tonight's 2 meetings", block.Text);
    }

    // ------------------------------------------------------------------
    // The bundle
    // ------------------------------------------------------------------

    [Fact]
    public void The_bundle_leads_on_the_night_because_that_is_what_the_first_question_asks()
    {
        var blocks = PrincipalFacts.ForThePrincipal(
            Night(cost: 4000, consistency: 5.0, retirements: 1, finishes: new[] { 4, 0, 5 }),
            (Rival(), new HeadToHead("sam", "dave", 6, 5)),
            "night-1");

        Assert.Equal(4, blocks.Count);
        Assert.Contains("race by race", blocks[0].Text);
        Assert.Contains("more pace", blocks[1].Text);
        Assert.Contains("did not finish", blocks[2].Text);
        Assert.Contains("against Dave", blocks[3].Text);
    }

    [Fact]
    public void A_thin_night_comes_back_empty_rather_than_invented()
    {
        var blocks = PrincipalFacts.ForThePrincipal(
            Night(races: 1, finishes: new[] { 3 }, paceReadable: false), rival: null, "night-1");

        Assert.Empty(blocks);
    }

    [Fact]
    public void Each_block_is_themed_on_its_driver_so_one_segment_does_not_say_it_twice()
    {
        var blocks = PrincipalFacts.ForThePrincipal(
            Night(cost: 4000, consistency: 5.0), rival: null, "night-1");

        Assert.NotEmpty(blocks);
        Assert.All(blocks, block => Assert.False(string.IsNullOrWhiteSpace(block.Theme)));
    }

    [Fact]
    public void The_key_is_the_night_rather_than_the_race()
    {
        // A race-scoped key would have a club running six heats hear the
        // same sentence in all six.
        var first = PrincipalFacts.TheNight(Night(), "night-2026-10-18");
        var again = PrincipalFacts.TheNight(Night(), "night-2026-10-18");

        Assert.NotNull(first);
        Assert.NotNull(again);
        Assert.Equal(first!.Key, again!.Key);
        Assert.Contains("night-2026-10-18", first.Key);
    }
}
