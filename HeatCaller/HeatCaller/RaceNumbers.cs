namespace HeatCaller;

/// <summary>
/// Numbers in the words a commentator would use for them, rather than the
/// words a computer reaches for.
///
/// WHY (14 Oct 2026)
///
/// Out of a club's own console log, verbatim: "Lap time: 5 point zero six
/// seconds." Nobody who has ever called a race has said that sentence. The
/// lap was a five-oh-six, the gap was half a second rather than nought
/// point five seconds, and the car was in P4 and not in fourth position.
///
/// It is the single cheapest thing on the list of what makes the announcer
/// sound like software, because it changes nothing about what the app
/// knows - only how it reads it out. A number read wrongly is the
/// giveaway the room notices first and the one a better sentence around it
/// cannot hide.
///
/// EVERYTHING HERE IS WORDS, WITH NO DIGIT CHARACTERS IN IT, and that is
/// not a style choice. The note on AnnouncementService.FormatSecondsForSpeech
/// records what happened when digits were merely separated by spaces:
/// ElevenLabs' own text normalisation re-collapsed "8 8" into
/// "eighty-eight" some of the time and not others, so the same lap time
/// was read two different ways on the same night. Spelling every digit as
/// a word leaves nothing number-shaped for an engine to reinterpret. Any
/// new method added here keeps that rule.
///
/// AND THE DISPLAY FORMS ARE SOMEWHERE ELSE. A screen wants "5.060s" and
/// a lap chart wants the milliseconds - see FormatSecondsForDisplay, which
/// exists because the spoken text was once shown on the Big Screen and a
/// projector read out "5 point eight eight seconds" to a room that could
/// see the clock. Nothing in this class is for a screen.
/// </summary>
internal static class RaceNumbers
{
    private static readonly string[] Small =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    };

    private static readonly string[] Tens =
    {
        string.Empty, string.Empty, "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
    };

    /// <summary>
    /// A whole number nought to nine hundred and ninety nine, in words.
    ///
    /// Hyphenated in the twenties and up - "thirty-four" - because that is
    /// how it is written and because the hyphen is what stops a voice
    /// putting a beat between the two halves of one number.
    ///
    /// Anything outside the range comes back as its own digits, which is
    /// the one place this class breaks its own words-only rule. It is
    /// unreachable from a lap time or a position and is here so a future
    /// caller gets a number rather than an exception.
    /// </summary>
    internal static string Words(long n)
    {
        if (n < 0)
        {
            return "minus " + Words(-n);
        }

        if (n < Small.Length)
        {
            return Small[n];
        }

        if (n < 100)
        {
            var tens = Tens[n / 10];
            var ones = n % 10;

            return ones == 0 ? tens : $"{tens}-{Small[ones]}";
        }

        if (n < 1000)
        {
            var hundreds = $"{Small[n / 100]} hundred";
            var rest = n % 100;

            return rest == 0 ? hundreds : $"{hundreds} and {Words(rest)}";
        }

        return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A lap time the way it gets called: "five oh six", "five sixty",
    /// "twelve thirty-four", "five flat".
    ///
    /// THE HUNDREDTHS CARRY THE SENTENCE and the word "seconds" is gone,
    /// because a commentator does not say it. "Lap time: five oh six" is
    /// the line; "Lap time: five oh six seconds" is a computer that has
    /// learned one new trick.
    ///
    /// FLAT, for a time that lands on the second, is the term - "a five
    /// flat" - and it is worth having rather than "five oh oh" because it
    /// is the one case where the plain reading is actively wrong: nobody
    /// says "five point zero zero".
    ///
    /// Rounded to hundredths, as the old spoken form was and for the same
    /// reason: nobody wants to hear a thousandth. The display form keeps
    /// the full precision.
    ///
    /// OUTSIDE A PLAUSIBLE LAP it falls back to <see cref="Plainly"/>. A
    /// "lap" under a second or over ninety-nine is a towed car, a decoder
    /// fault or a session clock being read as a lap, and the oh-six form
    /// stops making sense there - "zero forty-five" is not a lap time in
    /// any language.
    /// </summary>
    internal static string LapSpoken(long ms)
    {
        var centiseconds = (long)Math.Round(ms / 10.0);
        var whole = centiseconds / 100;
        var hundredths = centiseconds % 100;

        // A MINUTE IS WHERE THE CONVENTION STOPS. "Sixty fifty" for a
        // 60.5s lap reads as six thousand and fifty, and a lap that long
        // is given in minutes by anybody saying it out loud. Under a
        // second it is a decoder fault or a session clock being read as a
        // lap, and "zero forty-five" is not a lap time in any language.
        if (whole is <= 0 or >= 60)
        {
            return Plainly(ms);
        }

        if (hundredths == 0)
        {
            return $"{Words(whole)} flat";
        }

        // Under ten hundredths the "oh" is doing real work: "five oh six"
        // against "five six", which is a different lap time read aloud.
        return hundredths < 10
            ? $"{Words(whole)} oh {Words(hundredths)}"
            : $"{Words(whole)} {Words(hundredths)}";
    }

    /// <summary>
    /// The old explicit reading - "five point zero six seconds" - kept for
    /// the durations that are not lap times.
    ///
    /// A sector time, a session clock and a penalty are all read this way
    /// and should be: "point two one" is unambiguous, and the oh-six form
    /// is a convention about LAP TIMES specifically. Still words-only, for
    /// the reason at the top of this class.
    /// </summary>
    internal static string Plainly(long ms)
    {
        var centiseconds = (long)Math.Round(ms / 10.0);
        var whole = centiseconds / 100;
        var hundredths = centiseconds % 100;

        var digits = hundredths
            .ToString("00", System.Globalization.CultureInfo.InvariantCulture)
            .Select(ch => ch >= '0' && ch <= '9' ? Small[ch - '0'] : ch.ToString());

        return $"{Words(whole)} point {string.Join(" ", digits)} seconds";
    }

    /// <summary>
    /// A gap the way it gets called: "a tenth", "half a second", "two
    /// tenths", "a second and a half", "four seconds".
    ///
    /// NOTHING, under a twentieth, and the word is deliberate. The note on
    /// AnnouncementText records what the old reading did here: "nought
    /// point nought seconds" over two cars crossing together told a
    /// listener there was nothing between them in the least exciting way
    /// available. "Nothing in it" is the call.
    ///
    /// Tenths below a second and halves above it, which is how a gap is
    /// actually spoken: "three tenths" and then "a second and a half", and
    /// never "one point five seconds". Past ten seconds the halves go too -
    /// a twelve second gap is twelve seconds.
    /// </summary>
    internal static string GapSpoken(double seconds)
    {
        var gap = Math.Abs(seconds);

        if (gap < 0.05)
        {
            return "nothing";
        }

        if (gap < 0.95)
        {
            var tenths = (int)Math.Round(gap * 10, MidpointRounding.AwayFromZero);

            return tenths switch
            {
                <= 1 => "a tenth",
                5 => "half a second",
                _ => $"{Words(tenths)} tenths",
            };
        }

        if (gap < 1.05)
        {
            return "a second";
        }

        // Past ten the half is noise - a gap that big is being given as a
        // round figure by anyone saying it out loud.
        if (gap >= 10)
        {
            return $"{Words((long)Math.Round(gap, MidpointRounding.AwayFromZero))} seconds";
        }

        var halves = (long)Math.Round(gap * 2, MidpointRounding.AwayFromZero);
        var whole = halves / 2;
        var andAHalf = (halves % 2) != 0;

        if (!andAHalf)
        {
            // "one seconds" was the first draft of this line. A gap that
            // rounds to a flat second is "a second", the same words the
            // branch above gives it - the two readings of the same gap must
            // not differ by which side of 1.05 it fell on.
            return whole == 1 ? "a second" : $"{Words(whole)} seconds";
        }

        return whole == 1 ? "a second and a half" : $"{Words(whole)} and a half seconds";
    }

    /// <summary>
    /// A MARGIN - how much one thing beat another by - which is a gap that
    /// is allowed to be smaller than a tenth: "four hundredths", "two
    /// thousandths", "a hundredth".
    ///
    /// WHY NOT GapSpoken. A running gap under a twentieth is "nothing in
    /// it", and that is the right call for two cars nose to tail. A margin
    /// is the opposite case: a race won by four hundredths was won by four
    /// hundredths, and "won by nothing" is a sentence that tells a room the
    /// timing system has broken. This is what free text - the AI's own
    /// lines - is spoken through when a figure reads as a margin or a gap
    /// (see AnnouncementService.SpellOutTimesForSpeech), because a model
    /// handed "won by 0.004s" writes exactly that.
    ///
    /// From a tenth up it is <see cref="GapSpoken"/>, word for word, so the
    /// two can never disagree about a gap they both cover.
    /// </summary>
    internal static string MarginSpoken(double seconds)
    {
        var gap = Math.Abs(seconds);

        if (gap < 0.0005)
        {
            return "nothing";
        }

        if (gap < 0.0095)
        {
            var thousandths = Math.Max(1, (long)Math.Round(gap * 1000, MidpointRounding.AwayFromZero));

            return thousandths == 1 ? "a thousandth" : $"{Words(thousandths)} thousandths";
        }

        if (gap < 0.095)
        {
            var hundredths = Math.Max(1, (long)Math.Round(gap * 100, MidpointRounding.AwayFromZero));

            return hundredths == 1 ? "a hundredth" : $"{Words(hundredths)} hundredths";
        }

        return GapSpoken(gap);
    }

    /// <summary>
    /// A gap with the direction on it - "half a second back", "nothing in
    /// it" - for the chasing car.
    /// </summary>
    internal static string Behind(double seconds)
    {
        var gap = GapSpoken(seconds);

        return gap == "nothing" ? "nothing in it" : $"{gap} back";
    }

    /// <summary>
    /// A gap with the direction the other way - "half a second up the
    /// road" - for the car in front.
    /// </summary>
    internal static string Ahead(double seconds)
    {
        var gap = GapSpoken(seconds);

        return gap == "nothing" ? "nothing in it" : $"{gap} up the road";
    }

    /// <summary>
    /// A position the way a broadcast says it - "P4".
    ///
    /// For a screen and a caption. The spoken form is
    /// <see cref="PositionSpoken"/>, which is a different string for the
    /// reason everything in this class is: the letter and the digit
    /// together are what a voice engine is being asked to read, and "P4"
    /// is exactly the shape that comes out as "pee-four" on one engine and
    /// "P, four" on another.
    /// </summary>
    internal static string PositionShort(int position) =>
        position > 0 ? $"P{position.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : string.Empty;

    /// <summary>
    /// The same position to be SAID - "P four".
    ///
    /// Not "fourth position", which is the phrasing the templates reach
    /// for and which no commentator uses, and not "fourth" on its own,
    /// which is ambiguous beside a lap count. The letter stays a letter
    /// and the number becomes a word.
    /// </summary>
    internal static string PositionSpoken(int position) =>
        position > 0 ? $"P {Words(position)}" : string.Empty;
}
