namespace HeatCaller.Tests;

/// <summary>
/// The field read out once before a race, not twice (IntroRepeats): an
/// opening that lists the drivers when the introductions follow, and
/// introductions that go round the grid a second time.
/// </summary>
public sealed class IntroRepeatsTests
{
    private static readonly string[] Grid = { "Sam Smith", "Bill Jones", "Frank Lee", "Pat Kim", "The Flying Dutchman" };

    [Fact]
    public void An_opening_that_lists_the_field_loses_that_sentence_and_keeps_the_rest()
    {
        var notes = new List<string>();

        var opening = IntroRepeats.WithoutRollCall(
            "Heat 2 of Stock is up next! On the grid we have Sam, Bill, Frank, Pat and The Flying Dutchman. "
            + "Sam and Bill renew their rivalry.",
            Grid,
            notes);

        Assert.Equal("Heat 2 of Stock is up next! Sam and Bill renew their rivalry.", opening);
        Assert.Single(notes);
    }

    [Fact]
    public void An_opening_naming_two_drivers_is_left_alone()
    {
        const string Line = "Sam Smith and Bill Jones meet again in the A-Main, eight cars on the grid.";

        Assert.Equal(Line, IntroRepeats.WithoutRollCall(Line, Grid));
    }

    [Fact]
    public void An_opening_that_was_nothing_but_the_list_comes_back_null()
    {
        Assert.Null(IntroRepeats.WithoutRollCall("Sam, Bill, Frank and Pat line up for Heat 2.", Grid));
    }

    [Fact]
    public void A_title_is_never_taken_for_a_drivers_first_name()
    {
        // "The" from "The Flying Dutchman" must not count as naming him.
        const string Line = "The track is fast tonight, the grip is up, and the field is close. Sam Smith and Bill Jones lead the way.";

        Assert.Equal(Line, IntroRepeats.WithoutRollCall(Line, Grid));
    }

    [Fact]
    public void Introductions_that_go_round_again_are_cut_where_the_second_pass_starts()
    {
        var notes = new List<string>();

        var said = IntroRepeats.WithoutSecondPass(
            "On pole, Sam Smith, number seven from Riverside. Alongside him Bill Jones, who won last time out. "
            + "Frank Lee starts third. Pat Kim in fourth. And The Flying Dutchman at the back. "
            + "So that's Sam on pole, Bill second. Let's go racing!",
            Grid,
            notes);

        Assert.Equal(
            "On pole, Sam Smith, number seven from Riverside. Alongside him Bill Jones, who won last time out. "
            + "Frank Lee starts third. Pat Kim in fourth. And The Flying Dutchman at the back. Let's go racing!",
            said);
        Assert.Single(notes);
    }

    [Fact]
    public void Naming_another_driver_inside_an_introduction_is_not_a_second_pass()
    {
        const string Line = "On pole, Sam Smith. Bill Jones, who beat Sam last week, is second. Frank Lee third. "
                            + "Pat Kim fourth. The Flying Dutchman fifth.";

        Assert.Equal(Line, IntroRepeats.WithoutSecondPass(Line, Grid));
    }

    [Fact]
    public void Going_back_to_the_first_driver_part_way_through_is_cut()
    {
        var said = IntroRepeats.WithoutSecondPass(
            "On pole, Sam Smith. Bill Jones second. Frank Lee third. Pat Kim fourth. Sam Smith again leads them away from pole.",
            Grid);

        Assert.Equal("On pole, Sam Smith. Bill Jones second. Frank Lee third. Pat Kim fourth.", said);
    }

    [Fact]
    public void A_first_name_two_drivers_share_is_not_matched_on_its_own()
    {
        string[] grid = { "Sam Smith", "Sam Jones", "Frank Lee" };

        // "Sam" alone could be either, so it names nobody; the sentence lists
        // only Frank by name and stays.
        const string Line = "Sam is back, Sam is ready, and Frank Lee is on pole.";

        Assert.Equal(Line, IntroRepeats.WithoutRollCall(Line, grid));
    }

    [Fact]
    public void A_qualifying_line_listing_the_front_of_the_field_before_the_introductions_goes()
    {
        string[] grid = { "Annie Wayout", "Al Gorithim", "Terry Byte", "Ela Vayter", "Dee Ploy" };
        var notes = new List<string>();

        var said = IntroRepeats.WithoutSecondPass(
            "Annie Wayout secured pole, edging out Al Gorithim by two tenths with Terry Byte third. "
            + "Taking the green from P1 is Annie Wayout. Alongside her sits Al Gorithim. Terry Byte starts third. "
            + "Fourth is Ela Vayter. And in P5, Dee Ploy.",
            grid,
            notes);

        Assert.Equal(
            "Taking the green from P1 is Annie Wayout. Alongside her sits Al Gorithim. Terry Byte starts third. "
            + "Fourth is Ela Vayter. And in P5, Dee Ploy.",
            said);
        Assert.Single(notes);
    }

    [Fact]
    public void A_two_letter_first_name_counts()
    {
        string[] grid = { "Annie Wayout", "Al Gorithim", "Terry Byte", "Ela Vayter", "Dee Ploy" };

        Assert.Equal(
            "We want a clean run.",
            IntroRepeats.WithoutRollCall("We want a clean run. The competition's tough with Al, Terry, and Dee all bringing their A-game.", grid));
    }

    [Fact]
    public void A_two_letter_word_at_the_start_of_a_sentence_is_not_a_name()
    {
        string[] grid = { "So Lee", "Bill Jones", "Frank Lee" };
        const string Line = "So the grid is set, and Bill Jones is on pole.";

        Assert.Equal(Line, IntroRepeats.WithoutRollCall(Line, grid));
    }

    [Fact]
    public void A_team_principal_answer_loses_its_roll_call_but_keeps_the_rest()
    {
        string[] grid = { "Annie Wayout", "Al Gorithim", "Terry Byte", "Ela Vayter", "Dee Ploy" };
        var interview = new[]
        {
            new CommentaryService.InterviewExchange("How's the team feeling?", "We're ready."),
            new CommentaryService.InterviewExchange(
                "What's the goal?",
                "We want a clean run. The competition's tough with Al, Terry, Ela, and Dee all bringing their A-game."),
        };

        var kept = CommentaryService.WithoutGridRollCalls(interview, grid);

        Assert.Equal("We're ready.", kept[0].Answer);
        Assert.Equal("We want a clean run.", kept[1].Answer);
    }

    [Fact]
    public void An_answer_that_was_nothing_but_the_roll_call_is_left_whole()
    {
        string[] grid = { "Annie Wayout", "Al Gorithim", "Terry Byte" };
        var interview = new[] { new CommentaryService.InterviewExchange("Who worries you?", "Annie, Al and Terry, all of them.") };

        Assert.Equal("Annie, Al and Terry, all of them.", CommentaryService.WithoutGridRollCalls(interview, grid)[0].Answer);
    }

    [Fact]
    public void A_car_with_no_profile_takes_the_name_the_timing_last_recorded()
    {
        var older = new SessionRecord { EndedAtUtc = new DateTime(2026, 10, 1), Results = { new SessionResultEntry { Transponder = "123", DriverName = "T. Byte" } } };
        var newer = new SessionRecord { EndedAtUtc = new DateTime(2026, 10, 19), Results = { new SessionResultEntry { Transponder = "123", DriverName = "Terry Byte" } } };
        var asNumber = new SessionRecord { EndedAtUtc = new DateTime(2026, 10, 20), Results = { new SessionResultEntry { Transponder = "123", DriverName = "123" } } };

        Assert.Equal("Terry Byte", DriverIntros.NameFromHistory(new[] { older, newer, asNumber }, "123"));
        Assert.Null(DriverIntros.NameFromHistory(new[] { older }, "999"));
    }
}
