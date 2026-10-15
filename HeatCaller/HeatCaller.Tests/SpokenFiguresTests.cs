namespace HeatCaller.Tests;

/// <summary>
/// The figures that were still read like a computer after lap times were
/// fixed (14 Oct 2026): a MARGIN under a tenth, and the gaps and lap times
/// inside the AI's own lines, which reach the speaker as free text and
/// were all read as lap times - "Bill is 0.3s behind" went out as "zero
/// point three zero seconds behind".
/// </summary>
public sealed class SpokenFiguresTests
{
    // ------------------------------------------------------------------
    //  Margins
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0.04, "four hundredths")]
    [InlineData(0.01, "a hundredth")]
    [InlineData(0.004, "four thousandths")]
    [InlineData(0.001, "a thousandth")]
    [InlineData(0.0, "nothing")]
    public void A_margin_under_a_tenth_is_counted_rather_than_called_nothing(double seconds, string expected)
    {
        Assert.Equal(expected, RaceNumbers.MarginSpoken(seconds));
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(1.5)]
    [InlineData(4.0)]
    public void From_a_tenth_up_a_margin_is_said_exactly_as_a_gap_is(double seconds)
    {
        Assert.Equal(RaceNumbers.GapSpoken(seconds), RaceNumbers.MarginSpoken(seconds));
    }

    [Fact]
    public void A_margin_that_rounds_up_to_a_tenth_is_a_tenth()
    {
        Assert.Equal("a tenth", RaceNumbers.MarginSpoken(0.097));
    }

    // ------------------------------------------------------------------
    //  Free text - the AI's lines on the way to the speaker
    // ------------------------------------------------------------------

    [Fact]
    public void A_gap_in_free_text_is_read_as_a_gap()
    {
        Assert.Equal(
            "Bill is three tenths behind Sam!",
            AnnouncementService.SpellOutTimesForSpeech("Bill is 0.3s behind Sam!"));
    }

    [Fact]
    public void A_winning_margin_in_free_text_is_counted_in_hundredths()
    {
        Assert.Equal(
            "Sam wins it by four hundredths!",
            AnnouncementService.SpellOutTimesForSpeech("Sam wins it by 0.04 seconds!"));
    }

    [Fact]
    public void A_gap_over_a_second_is_known_by_the_word_after_it()
    {
        Assert.Equal(
            "Sam is a second and a half clear.",
            AnnouncementService.SpellOutTimesForSpeech("Sam is 1.5s clear."));
    }

    [Fact]
    public void A_lap_time_in_free_text_is_still_a_lap_time()
    {
        Assert.Equal(
            "Fastest lap goes to Sam, five oh six.",
            AnnouncementService.SpellOutTimesForSpeech("Fastest lap goes to Sam, 5.06s."));
    }

    [Fact]
    public void A_lap_introduced_by_of_is_not_mistaken_for_a_gap()
    {
        // "of" and "under" sit in front of laps far more often than gaps,
        // and read as a gap the lap would be rounded to the half second.
        Assert.Equal(
            "A best lap of five oh six for Sam.",
            AnnouncementService.SpellOutTimesForSpeech("A best lap of 5.06s for Sam."));
    }

    [Fact]
    public void A_decimal_without_a_unit_is_left_alone()
    {
        Assert.Equal("The 2.4GHz radio", AnnouncementService.SpellOutTimesForSpeech("The 2.4GHz radio"));
    }

    [Fact]
    public void The_words_either_side_of_a_figure_are_kept_exactly_as_written()
    {
        Assert.Equal(
            "Within half a second of the record!",
            AnnouncementService.SpellOutTimesForSpeech("Within 0.5s of the record!"));
    }

    // ------------------------------------------------------------------
    //  The running order and the split loop
    // ------------------------------------------------------------------

    [Fact]
    public void Laps_down_are_said_in_words()
    {
        var said = AnnouncementText.SpokenGap(new RaceGap(2, null));

        Assert.Equal("two laps down", said);
        Assert.DoesNotMatch("[0-9]", said);
    }

    [Fact]
    public void A_lap_time_going_down_is_still_a_lap_time()
    {
        Assert.Equal(
            "Sam went five oh six down from five twenty.",
            AnnouncementService.SpellOutTimesForSpeech("Sam went 5.06s down from 5.2s."));
    }

    [Fact]
    public void Under_a_second_the_same_word_still_makes_it_a_gap()
    {
        Assert.Equal(
            "He's half a second up on Sam.",
            AnnouncementService.SpellOutTimesForSpeech("He's 0.5s up on Sam."));
    }
}
