namespace HeatCaller.Tests;

/// <summary>
/// Numbers in the words a commentator would use.
///
/// OUT OF A CLUB'S OWN CONSOLE LOG, verbatim: "Lap time: 5 point zero six
/// seconds." The lap was a five-oh-six. This file is mostly the
/// conventions - where the "oh" goes, where "flat" goes, and the two
/// places the convention stops meaning anything and has to hand back to
/// the plain reading.
/// </summary>
public sealed class RaceNumbersTests
{
    // ------------------------------------------------------------------
    //  Lap times
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(5060, "five oh six")]
    [InlineData(5010, "five oh one")]
    [InlineData(5090, "five oh nine")]
    [InlineData(5100, "five ten")]
    [InlineData(5600, "five sixty")]
    [InlineData(5880, "five eighty-eight")]
    [InlineData(12340, "twelve thirty-four")]
    [InlineData(25990, "twenty-five ninety-nine")]
    public void A_lap_time_is_called_the_way_a_commentator_calls_it(long ms, string expected)
    {
        Assert.Equal(expected, RaceNumbers.LapSpoken(ms));
    }

    [Theory]
    [InlineData(5000, "five flat")]
    [InlineData(9000, "nine flat")]
    [InlineData(20000, "twenty flat")]
    public void A_time_on_the_second_is_flat_because_nobody_says_point_zero_zero(long ms, string expected)
    {
        Assert.Equal(expected, RaceNumbers.LapSpoken(ms));
    }

    [Fact]
    public void The_oh_is_doing_real_work_and_is_not_decoration()
    {
        // "five oh six" against "five six" is a different lap time read
        // aloud, which is why the under-ten case is written out separately.
        Assert.Equal("five oh six", RaceNumbers.LapSpoken(5060));
        Assert.Equal("five sixty", RaceNumbers.LapSpoken(5600));
    }

    [Theory]
    [InlineData(6935, "six nine thirty-five")]
    [InlineData(5006, "five oh oh six")]
    [InlineData(5065, "five oh sixty-five")]
    [InlineData(5901, "five nine oh one")]
    [InlineData(12341, "twelve three forty-one")]
    [InlineData(9999, "nine nine ninety-nine")]
    [InlineData(59999, "fifty-nine nine ninety-nine")]
    public void A_lap_is_said_to_the_thousandth_the_screen_shows(long ms, string expected)
    {
        // 31 Oct 2026: this used to round to hundredths, so 6.935 on the
        // screen was "six ninety-four" from the speaker, and a viewer of the
        // demo heard the announcer get the time wrong. The tenth on its own,
        // then the last two digits as a pair - the same rhythm as before,
        // with nothing rounded away.
        Assert.Equal(expected, RaceNumbers.LapSpoken(ms));
    }

    [Fact]
    public void A_lap_with_nothing_in_the_thousandths_keeps_the_short_reading()
    {
        // 5.060 on the screen is "five oh six", exactly - there is no
        // thousandth to say, so nothing changes for it.
        Assert.Equal("five oh six", RaceNumbers.LapSpoken(5060));
        Assert.Equal("six ninety-four", RaceNumbers.LapSpoken(6940));
    }

    [Fact]
    public void A_lap_said_to_the_thousandth_is_never_rounded()
    {
        // Every millisecond in a plausible lap reads differently from its
        // neighbours - no two times on the screen share one reading.
        var seen = new HashSet<string>();

        for (var ms = 1000L; ms < 60000L; ms++)
        {
            Assert.True(seen.Add(RaceNumbers.LapSpoken(ms)), $"{ms}ms reads the same as another time");
        }
    }

    [Fact]
    public void A_lap_past_a_minute_hands_back_to_the_plain_reading()
    {
        // "sixty fifty" for a 60.5s lap reads as six thousand and fifty,
        // and a lap that long is given in minutes by anybody saying it out
        // loud.
        var said = RaceNumbers.LapSpoken(60500);

        Assert.Contains("point", said);
        Assert.Equal(RaceNumbers.Plainly(60500), said);
    }

    [Fact]
    public void A_lap_under_a_second_hands_back_too()
    {
        // A decoder fault, a towed car, or a session clock being read as a
        // lap. "zero forty-five" is not a lap time in any language.
        var said = RaceNumbers.LapSpoken(450);

        Assert.Contains("point", said);
        Assert.Equal(RaceNumbers.Plainly(450), said);
    }

    [Fact]
    public void No_lap_time_contains_a_digit_character()
    {
        // THE RULE THE WHOLE CLASS KEEPS, and it is not a style choice:
        // ElevenLabs re-collapsed "8 8" into "eighty-eight" some of the
        // time and not others, so the same lap was read two ways on one
        // night. Nothing number-shaped is left for an engine to
        // reinterpret.
        for (var ms = 1000L; ms <= 59990L; ms += 370)
        {
            var said = RaceNumbers.LapSpoken(ms);

            Assert.False(said.Any(char.IsDigit), $"\"{said}\" has a digit character in it");
        }
    }

    [Fact]
    public void The_word_seconds_is_gone_from_a_lap_time()
    {
        // A commentator does not say it. "Lap time: five oh six" is the
        // line; "Lap time: five oh six seconds" is a computer that has
        // learned one new trick.
        Assert.DoesNotContain("seconds", RaceNumbers.LapSpoken(5060));
        Assert.DoesNotContain("seconds", RaceNumbers.LapSpoken(5000));
    }

    // ------------------------------------------------------------------
    //  Gaps
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, "nothing")]
    [InlineData(0.02, "nothing")]
    [InlineData(0.1, "a tenth")]
    [InlineData(0.2, "two tenths")]
    [InlineData(0.3, "three tenths")]
    [InlineData(0.5, "half a second")]
    [InlineData(0.9, "nine tenths")]
    [InlineData(1.0, "a second")]
    [InlineData(1.5, "a second and a half")]
    [InlineData(2.0, "two seconds")]
    [InlineData(2.5, "two and a half seconds")]
    [InlineData(12.3, "twelve seconds")]
    public void A_gap_is_called_the_way_a_commentator_calls_it(double seconds, string expected)
    {
        Assert.Equal(expected, RaceNumbers.GapSpoken(seconds));
    }

    [Fact]
    public void A_gap_that_rounds_to_a_flat_second_is_a_second_from_either_side()
    {
        // "one seconds" was the first draft of that line. The two readings
        // of the same gap must not differ by which side of a threshold it
        // fell on.
        Assert.Equal("a second", RaceNumbers.GapSpoken(1.0));
        Assert.Equal("a second", RaceNumbers.GapSpoken(1.04));
        Assert.Equal("a second", RaceNumbers.GapSpoken(1.2));
    }

    [Fact]
    public void Two_cars_crossing_together_are_nothing_in_it()
    {
        // The note on AnnouncementText records what the old reading did
        // here: "nought point nought seconds" told a listener there was
        // nothing between them in the least exciting way available.
        Assert.Equal("nothing in it", RaceNumbers.Behind(0.01));
        Assert.Equal("nothing in it", RaceNumbers.Ahead(0.01));
    }

    [Fact]
    public void A_gap_carries_its_direction_when_it_needs_to()
    {
        Assert.Equal("half a second back", RaceNumbers.Behind(0.5));
        Assert.Equal("half a second up the road", RaceNumbers.Ahead(0.5));
    }

    [Fact]
    public void A_negative_gap_reads_as_its_size()
    {
        // Whichever way round the subtraction came out, the words are
        // about a distance.
        Assert.Equal(RaceNumbers.GapSpoken(0.4), RaceNumbers.GapSpoken(-0.4));
    }

    // ------------------------------------------------------------------
    //  Positions
    // ------------------------------------------------------------------

    [Fact]
    public void A_position_is_P4_on_a_screen_and_P_four_in_the_ear()
    {
        // The two are different strings on purpose: "P4" is exactly the
        // shape that comes out as "pee-four" on one engine and "P, four"
        // on another.
        Assert.Equal("P4", RaceNumbers.PositionShort(4));
        Assert.Equal("P four", RaceNumbers.PositionSpoken(4));
        Assert.Equal("P twenty-one", RaceNumbers.PositionSpoken(21));
    }

    [Fact]
    public void A_position_nobody_is_running_in_says_nothing()
    {
        Assert.Equal(string.Empty, RaceNumbers.PositionShort(0));
        Assert.Equal(string.Empty, RaceNumbers.PositionSpoken(0));
        Assert.Equal(string.Empty, RaceNumbers.PositionSpoken(-1));
    }

    // ------------------------------------------------------------------
    //  Whole numbers
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0, "zero")]
    [InlineData(7, "seven")]
    [InlineData(13, "thirteen")]
    [InlineData(20, "twenty")]
    [InlineData(34, "thirty-four")]
    [InlineData(99, "ninety-nine")]
    [InlineData(100, "one hundred")]
    [InlineData(104, "one hundred and four")]
    public void A_whole_number_is_spelled_out(long n, string expected)
    {
        Assert.Equal(expected, RaceNumbers.Words(n));
    }

    [Fact]
    public void The_twenties_and_up_are_hyphenated_so_a_voice_keeps_them_as_one_number()
    {
        Assert.Contains("-", RaceNumbers.Words(34));
        Assert.DoesNotContain("-", RaceNumbers.Words(30));
    }
}
