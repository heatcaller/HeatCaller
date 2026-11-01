using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// The gaps the position report can be asked to read out
/// (AnnouncementText.SpokenGap, SpokenGaps and WithPositionGaps), and the
/// two cases that decide whether the figure is one a spectator can trust
/// or one that is simply wrong:
///
///  - A car down a lap is a LAP behind, not a number of seconds. The time
///    between its last crossing and the car's in front is an accident of
///    where the two of them happen to be on track, so reading it out would
///    tell the room a lapped car was four seconds off the lead. Said in
///    laps, in the same words the leaderboard uses for "+1L".
///  - The leader has nobody in front of them. Measured against themselves
///    the gap is zero, and "nought point nought" over the car winning the
///    race is a dead heat as far as anyone listening can tell - so the
///    leader is named in the running order and left out of the gaps.
///
/// Also that a gap is said EXACTLY, to the thousandth (1 Nov 2026) - it was
/// rounded to tenths and halves, and a club heard the rounding as the voice
/// getting the gap wrong - and the clause a club wrote
/// around {gaps} going with it when there is nothing to say - "... Gaps."
/// being what they would have heard if only the placeholder came out.
/// </summary>
public sealed class PositionGapTests
{
    [Fact]
    public void A_gap_under_a_second_is_said_to_the_thousandth()
    {
        Assert.Equal("point three one two back", AnnouncementText.SpokenGap(new RaceGap(0, 312)));
        Assert.Equal("point four eight back", AnnouncementText.SpokenGap(new RaceGap(0, 480)));
    }

    [Fact]
    public void A_gap_over_a_second_is_said_exactly_and_in_words()
    {
        // In words, never "1.523", which a voice engine reads its own way.
        var said = AnnouncementText.SpokenGap(new RaceGap(0, 1_523));

        Assert.Equal("one point five two three back", said);
        Assert.DoesNotContain("1.5", said);
        Assert.DoesNotContain("half", said);
    }

    [Fact]
    public void A_spoken_gap_is_never_rounded()
    {
        Assert.Equal("point six six back", AnnouncementText.SpokenGap(new RaceGap(0, 660)));
        Assert.Equal("two point three six one back", AnnouncementText.SpokenGap(new RaceGap(0, 2_361)));
    }

    [Fact]
    public void A_gap_under_a_tenth_is_said_exactly_too()
    {
        var said = AnnouncementText.SpokenGap(new RaceGap(0, 40));

        Assert.Equal("point oh four back", said);
        Assert.DoesNotContain("under", said);
    }

    [Fact]
    public void A_car_a_lap_down_is_a_lap_down_and_never_a_number_of_seconds()
    {
        Assert.Equal("a lap down", AnnouncementText.SpokenGap(new RaceGap(1, null)));

        // And with a time alongside it even so: being down a lap is the
        // whole answer, and the seconds are not an answer to anything.
        var said = AnnouncementText.SpokenGap(new RaceGap(1, 4_200));

        Assert.Equal("a lap down", said);
        Assert.DoesNotContain("4.2", said);
        Assert.DoesNotContain("back", said);
    }

    [Fact]
    public void A_car_more_than_a_lap_down_is_given_the_number_of_laps()
    {
        Assert.Equal("three laps down", AnnouncementText.SpokenGap(new RaceGap(3, null)));
    }

    [Fact]
    public void A_gap_that_was_never_measured_is_left_unsaid()
    {
        // A lap added by hand has no crossing time to measure against -
        // see RaceGaps.CrossingMs. Silent beats invented.
        Assert.Equal(string.Empty, AnnouncementText.SpokenGap(new RaceGap(0, null)));
    }

    [Fact]
    public void The_leader_is_left_out_of_the_gaps_because_nobody_is_in_front()
    {
        var said = AnnouncementText.SpokenGaps(new[]
        {
            ("Sam", new RaceGap(0, null)),
            ("Bill", new RaceGap(0, 1_500)),
        });

        Assert.Equal("Bill one point five back", said);
        Assert.DoesNotContain("Sam", said);
    }

    [Fact]
    public void The_gaps_read_as_a_spoken_list_with_an_and_before_the_last_one()
    {
        var said = AnnouncementText.SpokenGaps(new[]
        {
            ("Sam", new RaceGap(0, null)),
            ("Bill", new RaceGap(0, 1_500)),
            ("Frank", new RaceGap(0, 2_300)),
            ("Dana", new RaceGap(1, null)),
        });

        Assert.Equal("Bill one point five back, Frank two point three back and Dana a lap down", said);
    }

    [Fact]
    public void A_car_with_nothing_measurable_drops_out_and_the_rest_still_read()
    {
        var said = AnnouncementText.SpokenGaps(new[]
        {
            ("Sam", new RaceGap(0, null)),
            ("Bill", new RaceGap(0, null)),
            ("Frank", new RaceGap(0, 2_300)),
        });

        Assert.Equal("Frank two point three back", said);
    }

    [Fact]
    public void With_nobody_left_to_name_the_gaps_come_back_empty()
    {
        Assert.Equal(string.Empty, AnnouncementText.SpokenGaps(new[] { ("Sam", new RaceGap(0, null)) }));
    }

    [Fact]
    public void The_gaps_go_where_the_club_put_the_placeholder()
    {
        var text = AnnouncementText.WithPositionGaps(
            "[excited] As it stands - {positions}. Gaps: {gaps}.", "Bill 1.5 back");

        Assert.Equal("[excited] As it stands - {positions}. Gaps: Bill 1.5 back.", text);
    }

    [Fact]
    public void With_nothing_to_say_the_clause_the_club_wrote_around_the_placeholder_goes_too()
    {
        var text = AnnouncementText.TidyTemplate(
            AnnouncementText.WithPositionGaps("[excited] As it stands - {positions}. Gaps: {gaps}.", string.Empty));

        Assert.Equal("[excited] As it stands - {positions}.", text);
    }

    [Fact]
    public void A_gap_clause_hung_off_a_sentence_takes_the_comma_in_front_of_it_too()
    {
        var text = AnnouncementText.TidyTemplate(
            AnnouncementText.WithPositionGaps("[excited] The order - {positions}, and the gaps - {gaps}.", null));

        Assert.Equal("[excited] The order - {positions}.", text);
    }

    [Fact]
    public void A_line_that_never_asked_for_the_gaps_is_left_exactly_as_it_was()
    {
        const string line = "[excited] As it stands - {positions}.";

        Assert.Equal(line, AnnouncementText.WithPositionGaps(line, "Bill 1.5 back"));
        Assert.Equal(line, AnnouncementText.WithPositionGaps(line, string.Empty));
    }

    [Fact]
    public void A_gap_is_written_the_same_way_on_a_comma_decimal_PC()
    {
        // Written "1,5" the voice reads it as "one, five" - the bug this
        // app has already had in the fast-lap and commentary figures. All
        // words now, so there is no decimal for a region to change.
        InCommaDecimal(() => Assert.Equal("one point five back", AnnouncementText.SpokenGap(new RaceGap(0, 1_500))));
    }

    // --- Helpers --------------------------------------------------------------------

    /// <summary>Runs the work on a PC whose region writes one and a half as 1,5.</summary>
    private static void InCommaDecimal(Action work)
    {
        var was = CultureInfo.CurrentCulture;

        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            work();
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }
}
