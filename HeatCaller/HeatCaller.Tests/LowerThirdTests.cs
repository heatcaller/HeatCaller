namespace HeatCaller.Tests;

/// <summary>
/// Who the band at the bottom of the crowd's screen names, added 11 Oct
/// 2026.
///
/// The band used to say what was on air - ON AIR │ BATTLE WATCH - while
/// the board underneath it went on cycling the standings on its own timer.
/// So when the voice said "Hargrove, three tenths back off Bellamy", the
/// screen was showing row seven of a twelve-car table and whoever was
/// watching had to find the two names themselves. Now the band names them,
/// at the moment the voice does.
///
/// WHAT THIS FILE IS REALLY DEFENDING
///
/// The band is three feet high on a wall in front of forty people. A wrong
/// name on it is worse than no name, and the way a wrong name gets there
/// is a substring: "Sam" inside "Samantha", "Art" inside "started", "Ed"
/// inside "edged". Most of the tests below are that one fault, from every
/// angle it has.
/// </summary>
public sealed class LowerThirdTests
{
    private static LowerThird.Candidate Driver(string said, string? shown = null) =>
        new(said, shown ?? said);

    private static IReadOnlyList<string> In(string line, params LowerThird.Candidate[] who) =>
        LowerThird.NamesIn(line, who);

    // ------------------------------------------------------------------
    //  What it finds
    // ------------------------------------------------------------------

    [Fact]
    public void The_two_drivers_a_battle_call_names_go_on_the_band()
    {
        var found = In(
            "Hargrove is all over the back of Bellamy here, three tenths covering them.",
            Driver("Hargrove"),
            Driver("Bellamy"),
            Driver("Fenwick"));

        Assert.Equal(new[] { "Hargrove", "Bellamy" }, found);
    }

    [Fact]
    public void They_go_up_in_the_order_the_line_says_them()
    {
        // Not the order of the standings and not the order the candidates
        // arrive in. "Hargrove is all over Bellamy" is about Hargrove
        // first whatever the board says, and a band that reversed them
        // would contradict the sentence the room just heard.
        var found = In(
            "Bellamy has Hargrove all over the back of him.",
            Driver("Hargrove"),
            Driver("Bellamy"));

        Assert.Equal(new[] { "Bellamy", "Hargrove" }, found);
    }

    [Fact]
    public void A_name_with_an_apostrophe_s_or_a_comma_after_it_is_still_the_name()
    {
        // How a name actually appears in a spoken sentence.
        Assert.Equal(new[] { "Hargrove" }, In("Hargrove's line through turn three is better.", Driver("Hargrove")));
        Assert.Equal(new[] { "Hargrove" }, In("Three tenths, Hargrove, and closing.", Driver("Hargrove")));
        Assert.Equal(new[] { "Hargrove" }, In("Hargrove.", Driver("Hargrove")));
        Assert.Equal(new[] { "Hargrove" }, In("And here comes Hargrove", Driver("Hargrove")));
        Assert.Equal(new[] { "Hargrove" }, In("Hargrove - half a second now.", Driver("Hargrove")));
    }

    [Fact]
    public void A_model_that_writes_a_name_in_capitals_still_gets_it_right()
    {
        // A model writes a name however it likes; the club typed it once.
        Assert.Equal(new[] { "Hargrove" }, In("HARGROVE takes the lead!", Driver("Hargrove")));
        Assert.Equal(new[] { "Hargrove" }, In("hargrove takes the lead!", Driver("Hargrove")));
    }

    [Fact]
    public void The_screen_shows_the_board_name_even_when_the_announcer_says_another()
    {
        // The announcer reads the club's Announcer name where they typed
        // one. A band saying "Billy Bob" above a board row saying "Bill
        // Hargrove" would read as two drivers.
        var found = In("Billy Bob has the inside line.", Driver("Billy Bob", "Bill Hargrove"));

        Assert.Equal(new[] { "Bill Hargrove" }, found);
    }

    // ------------------------------------------------------------------
    //  What it will not do, which is the point
    // ------------------------------------------------------------------

    [Fact]
    public void A_name_inside_a_longer_word_is_not_a_name()
    {
        // THE FAULT THIS WHOLE FILE IS ABOUT. A wrong name three feet high
        // on a wall is worse than no name.
        Assert.Empty(In("Samantha has the inside line.", Driver("Sam")));
        Assert.Empty(In("They started from the back.", Driver("Art")));
        Assert.Empty(In("He edged ahead on the last lap.", Driver("Ed")));
        Assert.Empty(In("A brilliant recovery drive.", Driver("Rec")));
        Assert.Empty(In("Three tenths covering them.", Driver("Ten")));
    }

    [Fact]
    public void A_name_too_short_to_tell_from_a_word_is_never_looked_for()
    {
        // A club with a driver entered as "Al" or "Jo" would otherwise
        // have the band light up on half the sentences in the evening -
        // "also", "job", "join" - because a two-letter name sits inside
        // far too many words for the boundary check to save it. It costs
        // them a caption and never puts a wrong one up.
        Assert.Empty(In("Al is leading.", Driver("Al")));
        Assert.Empty(In("Jo has the fastest lap.", Driver("Jo")));

        // Three letters is the bar, and a three-letter name IS matched -
        // on a word boundary, like every other.
        Assert.Equal(new[] { "Bob" }, In("Bob is leading.", Driver("Bob")));
        Assert.Empty(In("A bobbing car at the back.", Driver("Bob")));
    }

    [Fact]
    public void The_longer_of_two_overlapping_names_wins_and_the_shorter_does_not_double_up()
    {
        // A club with both "Bill" and "Bill Hargrove" on the books, and a
        // line that says "Bill Hargrove". Without the overlap check the
        // band would show two drivers where the line named one.
        var found = In(
            "Bill Hargrove takes it.",
            Driver("Bill"),
            Driver("Bill Hargrove"));

        Assert.Equal(new[] { "Bill Hargrove" }, found);
    }

    [Fact]
    public void A_shorter_name_elsewhere_in_the_line_is_still_found()
    {
        // The overlap check must only claim the letters the longer name
        // actually took - not shut the shorter name out of the sentence.
        var found = In(
            "Bill Hargrove leads, and Bill Fenwick is second.",
            Driver("Bill Fenwick"),
            Driver("Bill Hargrove"));

        Assert.Equal(new[] { "Bill Hargrove", "Bill Fenwick" }, found);
    }

    [Fact]
    public void One_row_per_person_however_many_transponders_they_have()
    {
        // A spare car, or a transponder handed over mid-season, is the
        // same driver twice in the candidate list.
        var found = In(
            "Hargrove takes the lead.",
            Driver("Hargrove"),
            Driver("Hargrove"));

        Assert.Equal(new[] { "Hargrove" }, found);
    }

    [Fact]
    public void A_recap_that_names_the_whole_field_does_not_turn_the_band_into_a_leaderboard()
    {
        var found = In(
            "Hargrove wins it from Bellamy, with Fenwick third, Marsh fourth and Doyle fifth.",
            Driver("Hargrove"),
            Driver("Bellamy"),
            Driver("Fenwick"),
            Driver("Marsh"),
            Driver("Doyle"));

        Assert.Equal(LowerThird.MostNames, found.Count);
        Assert.Equal(new[] { "Hargrove", "Bellamy", "Fenwick" }, found);
    }

    [Fact]
    public void A_line_that_names_nobody_puts_nobody_up()
    {
        // Most of a continuity announcement, all of a flag call, and a
        // good deal of a recap of a twelve-car race. The band then says
        // what is playing and no more.
        Assert.Empty(In("Green flag, and we are racing.", Driver("Hargrove"), Driver("Bellamy")));
        Assert.Empty(In("Coming up next, the Box Stock A-main.", Driver("Hargrove")));
    }

    [Fact]
    public void Nothing_to_read_or_nobody_to_find_is_an_empty_band_rather_than_a_fault()
    {
        Assert.Empty(LowerThird.NamesIn(null, new[] { Driver("Hargrove") }));
        Assert.Empty(LowerThird.NamesIn("   ", new[] { Driver("Hargrove") }));
        Assert.Empty(LowerThird.NamesIn("Hargrove takes it.", null));
        Assert.Empty(LowerThird.NamesIn("Hargrove takes it.", Array.Empty<LowerThird.Candidate>()));

        // A profile with a name the announcer would say but nothing for
        // the board to show is not put up as a blank.
        Assert.Empty(In("Hargrove takes it.", new LowerThird.Candidate("Hargrove", "   ")));
    }

    // ------------------------------------------------------------------
    //  How it reads on the band
    // ------------------------------------------------------------------

    [Fact]
    public void The_band_writes_them_with_a_middle_dot()
    {
        // A comma inside a caption reads as part of a sentence, where this
        // is a pair of labels.
        Assert.Equal("Hargrove · Bellamy", LowerThird.Line(new[] { "Hargrove", "Bellamy" }));
        Assert.Equal("Hargrove", LowerThird.Line(new[] { "Hargrove" }));
        Assert.Equal(string.Empty, LowerThird.Line(Array.Empty<string>()));
        Assert.Equal(string.Empty, LowerThird.Line(null));
    }

    [Fact]
    public void The_band_keeps_the_caption_and_the_names_as_two_different_things()
    {
        // The caption is what is PLAYING and the names are who it is
        // ABOUT. Running them into one line is how a lower third turns
        // into a sentence nobody reads - and keeping them apart is what
        // lets the names be the thing dropped when the band is squeezed.
        //
        // On an STA thread with a message pump, like every other test in
        // this suite that makes a control: the band owns a WinForms timer.
        var seen = StaThread.Run(
            () =>
            {
                using var band = new OnAirBar();

                band.Say("THE RACE RECAP", "Hargrove \u00b7 Bellamy");

                var withNames = (band.Saying, band.About);

                // The one-argument overload is still the old behaviour
                // exactly: a caption and nobody.
                band.Say("COMMERCIAL BREAK");

                return (WithNames: withNames, After: (band.Saying, band.About));
            },
            TimeSpan.FromSeconds(30),
            () => "driving the On Air band");

        Assert.Equal(("THE RACE RECAP", "Hargrove \u00b7 Bellamy"), seen.WithNames);
        Assert.Equal(("COMMERCIAL BREAK", string.Empty), seen.After);
    }

    [Fact]
    public void The_names_can_change_inside_one_segment_and_a_padded_one_is_held_as_empty()
    {
        // A two-hander names one driver in its first turn and another in
        // its second. A band that wiped across the bottom of the wall at
        // each of those would be a flicker for the whole segment - so a
        // change of names repaints and only a change of caption wipes.
        var seen = StaThread.Run(
            () =>
            {
                using var band = new OnAirBar();

                band.Say("BATTLE WATCH", "Hargrove");
                band.Say("BATTLE WATCH", "Hargrove \u00b7 Bellamy");

                var inSegment = (band.Saying, band.About);

                band.Say("THE RACE RECAP", null);
                var nobody = band.About;

                band.Say("THE RACE RECAP", "   ");
                var blank = band.About;

                band.Say("THE RACE RECAP", "  Hargrove  ");
                var padded = band.About;

                return (InSegment: inSegment, Nobody: nobody, Blank: blank, Padded: padded);
            },
            TimeSpan.FromSeconds(30),
            () => "changing the On Air band's names");

        Assert.Equal(("BATTLE WATCH", "Hargrove \u00b7 Bellamy"), seen.InSegment);
        Assert.Equal(string.Empty, seen.Nobody);
        Assert.Equal(string.Empty, seen.Blank);
        Assert.Equal("Hargrove", seen.Padded);
    }
}
