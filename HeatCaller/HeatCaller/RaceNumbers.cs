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
    /// <summary>
    /// "A-Main", "A Main" or "A-Mains" - the capital A on its own in front
    /// of "main". Lowercase "a main event" is the article and is left alone.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex AMain = new(
        @"(?<![\w'])A[\s-]+(?=[Mm]ains?\b)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A round's letter said as a letter (8 Nov 2026, Asher: "when A main is
    /// spoken, it's the letter A, not a soft ah"). Every voice read "A-Main"
    /// as the article - Kokoro's own phonemes for it are "ɐmˌAn" - while
    /// "B-Main" was always "bee main". Written "Eh-Main" it is the letter,
    /// run into "main" as one word - Kokoro's phonemes "ˌAmˈAn" - and "the"
    /// in front of it becomes "thee". Not "A. Main": that was the letter
    /// too, but ElevenLabs v3 took the full stop as the end of a sentence
    /// and left a gap before "main" (Asher: "it needs to sound like it's one
    /// thing"). Not "Ay-Main", which Kokoro says "eye". For the voice only:
    /// the screens keep "A-Main".
    ///
    /// ElevenLabs is different (9 Nov 2026, Asher: "it's saying it weird",
    /// still the letter A): it reads "Eh-Main" as a flat "eh", so the
    /// cloud voices - ElevenLabs and the HeatCaller voice -
    /// get "Ay-Main", <paramref name="cloudVoice"/> (see
    /// AnnouncementService.ForCloudVoice). Kokoro keeps "Eh-".
    /// </summary>
    internal static string SpokenRoundNames(string text, bool cloudVoice = false) =>
        string.IsNullOrEmpty(text) ? text : AMain.Replace(text, cloudVoice ? "Ay-" : "Eh-");

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
    // ------------------------------------------------------------------
    // Times a model spelled out digit by digit (3 Nov 2026)
    // ------------------------------------------------------------------

    private const string DigitWord = "zero|oh|one|two|three|four|five|six|seven|eight|nine";

    private const string TensWord = "twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety";

    // Longest first, so "seventeen" is never read as "seven".
    private const string WholeWord =
        "(?:" + TensWord + ")(?:-(?:one|two|three|four|five|six|seven|eight|nine))?"
        + "|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|ten"
        + "|zero|one|two|three|four|five|six|seven|eight|nine";

    // "three-point-three-zero-four", "eleven point two six four seconds":
    // three or more digits after the point. One or two read well as they are.
    //
    // WHY (10 Nov 2026, bug hunt 16): not after "minutes and" or "laps in".
    // That is a heat's run time in HeatCaller's own words - DriverIntros'
    // "thirty-four laps in five minutes and two point three one five
    // seconds" - and the intro is told to copy it exactly. Put back into
    // "2.315s" it was then read as a lap, "five minutes and two three
    // fifteen", with the seconds gone from the minutes. The whole number
    // starts a word, too, not after a hyphen: otherwise "forty-eight" was
    // skipped and its "eight" taken alone, giving "forty-8.512s".
    private static readonly System.Text.RegularExpressions.Regex SpelledPoint = new(
        $@"(?<!\bminutes?\s+and\s+)(?<!\blaps?\s+in\s+)(?<![\w-])(?<whole>{WholeWord})[- ]point(?<digits>(?:[- ](?:{DigitWord})){{3,}})\b(?:\s+seconds?\b)?",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant
        | System.Text.RegularExpressions.RegexOptions.Compiled);

    // "twelve-nine-four-five": a whole number and three digits, hyphened
    // into one word with no "point" at all.
    private static readonly System.Text.RegularExpressions.Regex SpelledRun = new(
        $@"\b(?<whole>{WholeWord})(?<digits>(?:-(?:{DigitWord})){{3}})\b(?![-\w])",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant
        | System.Text.RegularExpressions.RegexOptions.Compiled);

    // "a thirteen fifty-six-nine": the commentator's form with the
    // thousandth hung off the end as one more digit.
    private static readonly System.Text.RegularExpressions.Regex SpelledTail = new(
        $@"\b(?<whole>{WholeWord}) (?<tens>{TensWord})-(?<unit>one|two|three|four|five|six|seven|eight|nine)-(?<digit>{DigitWord})\b(?![-\w])",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant
        | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Puts a time a model wrote out digit by digit back into figures, so
    /// AnnouncementService.SpellOutTimesForSpeech can say it the way the
    /// rest are said (3 Nov 2026).
    ///
    /// The facts have carried thousandths since 1 Nov, and a recap handed
    /// "3.304s" and told to say numbers in words wrote "three-point-three-
    /// zero-four back" and "a twelve-nine-four-five" - every digit read out,
    /// which on air sounds like the voice glitching. Turned back into
    /// "3.304s back" and "12.945s", the same reader that turns a fact's
    /// "0.3s behind" into "three tenths behind" decides whether it is a gap
    /// or a lap. Only the shapes above are touched: a sentence with "one
    /// two three" in it, or "point five", is left exactly as written.
    /// </summary>
    internal static string DigitsFromSpelledTimes(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = SpelledPoint.Replace(text, m => Figure(m.Groups["whole"].Value, Digits(m.Groups["digits"].Value)) ?? m.Value);
        text = SpelledRun.Replace(text, m => Figure(m.Groups["whole"].Value, Digits(m.Groups["digits"].Value)) ?? m.Value);

        return SpelledTail.Replace(text, m =>
        {
            var tens = Array.IndexOf(Tens, m.Groups["tens"].Value.ToLowerInvariant());
            var unit = Array.IndexOf(Small, m.Groups["unit"].Value.ToLowerInvariant());
            var digit = Digits(m.Groups["digit"].Value);

            return tens < 2 || unit < 1 || digit is null
                ? m.Value
                : Figure(m.Groups["whole"].Value, $"{tens}{unit}{digit}") ?? m.Value;
        });
    }

    /// <summary>"-three-zero-four" as "304", or null when a word is not a digit.</summary>
    private static string? Digits(string spelled)
    {
        var digits = new System.Text.StringBuilder();

        foreach (var word in spelled.Split(new[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var lower = word.ToLowerInvariant();
            var at = lower == "oh" ? 0 : Array.IndexOf(Small, lower);

            if (at is < 0 or > 9)
            {
                return null;
            }

            digits.Append(at);
        }

        return digits.Length == 0 ? null : digits.ToString();
    }

    /// <summary>"thirteen" and "980" as "13.980s" - three places at most, the timing's own precision.</summary>
    private static string? Figure(string spelledWhole, string? fraction)
    {
        if (fraction is null || WholeValue(spelledWhole) is not { } whole)
        {
            return null;
        }

        return $"{whole}.{(fraction.Length > 3 ? fraction[..3] : fraction)}s";
    }

    /// <summary>"thirteen" as 13 and "fifty-six" as 56, or null.</summary>
    private static int? WholeValue(string spelled)
    {
        var parts = spelled.ToLowerInvariant().Split('-');
        var first = Array.IndexOf(Small, parts[0]);

        if (parts.Length == 1)
        {
            if (first >= 0)
            {
                return first;
            }

            var tens = Array.IndexOf(Tens, parts[0]);
            return tens >= 2 ? tens * 10 : null;
        }

        var ten = Array.IndexOf(Tens, parts[0]);
        var unit = Array.IndexOf(Small, parts[1]);

        return ten >= 2 && unit is >= 1 and <= 9 ? (ten * 10) + unit : null;
    }

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
    /// Time left on a race clock, the way an announcer says it (1 Nov 2026):
    /// "forty-five seconds", "a minute and a half", "three minutes".
    ///
    /// ROUNDED, because the clock moves while the line is written and
    /// said. Under a minute to the nearest five seconds (never below five);
    /// from a minute up to the nearest half minute. The caller puts "about"
    /// in front where the figure is the clock's rather than a moment's.
    /// </summary>
    internal static string TimeLeftSpoken(TimeSpan left)
    {
        var seconds = Math.Max(0, left.TotalSeconds);

        if (seconds < 57.5)
        {
            var fives = Math.Max(5, (int)Math.Round(seconds / 5, MidpointRounding.AwayFromZero) * 5);

            return $"{Words(fives)} seconds";
        }

        var halves = (int)Math.Round(seconds / 30, MidpointRounding.AwayFromZero);
        var minutes = halves / 2;
        var half = halves % 2 == 1;

        if (minutes == 1)
        {
            return half ? "a minute and a half" : "a minute";
        }

        return half ? $"{Words(minutes)} and a half minutes" : $"{Words(minutes)} minutes";
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
    /// THE THOUSANDTH IS SAID WHEN THERE IS ONE (31 Oct 2026). This used
    /// to round to hundredths, on the reasoning that nobody wants to hear a
    /// thousandth. But every screen shows the full time - the Big Screen,
    /// the board and the results all read 6.935 - and a voice saying "six
    /// ninety-four" under a screen that says 6.935 reads to the room as the
    /// announcer getting the time wrong. A viewer of the demo video said
    /// exactly that. So the time is read as timed, in the same rhythm: the
    /// tenth on its own, then the last two digits as a pair - "six nine
    /// thirty-five", "five oh oh six", "twelve three forty-one". A time
    /// with nothing in the thousandths keeps the shorter reading ("five oh
    /// six" for 5.060), which is still exactly the time on the screen.
    ///
    /// OUTSIDE A PLAUSIBLE LAP it falls back to <see cref="Plainly"/>. A
    /// "lap" under a second or over ninety-nine is a towed car, a decoder
    /// fault or a session clock being read as a lap, and the oh-six form
    /// stops making sense there - "zero forty-five" is not a lap time in
    /// any language.
    /// </summary>
    internal static string LapSpoken(long ms)
    {
        var whole = ms / 1000;
        var thousandths = ms % 1000;

        // A MINUTE IS WHERE THE CONVENTION STOPS. "Sixty fifty" for a
        // 60.5s lap reads as six thousand and fifty, and a lap that long
        // is given in minutes by anybody saying it out loud. Under a
        // second it is a decoder fault or a session clock being read as a
        // lap, and "zero forty-five" is not a lap time in any language.
        //
        // WHY (8 Nov 2026): to the thousandth when there is one, not
        // Plainly's hundredths. A 61.235 lap was said "sixty-one point two
        // four seconds" under a screen that showed 61.235s - the same
        // mismatch the 31 Oct note above fixed for every shorter lap.
        if (ms < 0 || whole is <= 0 or >= 60)
        {
            return PlainlyToTheThousandth(ms);
        }

        if (thousandths == 0)
        {
            return $"{Words(whole)} flat";
        }

        // Nothing in the thousandths: the hundredths say the whole time, and
        // say it the way it has always been called.
        if (thousandths % 10 == 0)
        {
            var hundredths = thousandths / 10;

            // Under ten hundredths the "oh" is doing real work: "five oh six"
            // against "five six", which is a different lap time read aloud.
            return hundredths < 10
                ? $"{Words(whole)} oh {Words(hundredths)}"
                : $"{Words(whole)} {Words(hundredths)}";
        }

        // A thousandth to say: the tenth on its own, then the last two
        // digits as a pair - "six nine thirty-five" for 6.935. The "oh"s
        // hold every digit's place, as they do above: "five oh oh six" is
        // 5.006 and cannot be heard as 5.06.
        var tenth = thousandths / 100;
        var pair = thousandths % 100;
        var tenthSaid = tenth == 0 ? "oh" : Words(tenth);
        var pairSaid = pair < 10 ? $"oh {Words(pair)}" : Words(pair);

        // A "POINT" AFTER TWENTY, THIRTY, FORTY AND FIFTY when the tenth
        // follows as a lone digit (8 Nov 2026). Heard, the hyphen is not
        // there: 30.425 read "thirty four twenty-five" is exactly 34.250's
        // "thirty-four twenty-five", and 20.506's "twenty five oh six" is
        // 25.060. "Thirty point four twenty-five" cannot be anything else -
        // no other lap reading says "point" - and every other whole number
        // ends in a word that no single digit can be joined onto.
        // RaceNumbersTests checks every lap from 1.000 to 59.999.
        var point = whole >= 20 && whole % 10 == 0 && tenth != 0 ? " point" : string.Empty;

        return $"{Words(whole)}{point} {tenthSaid} {pairSaid}";
    }

    /// <summary>
    /// <see cref="Plainly"/> with the thousandth kept when there is one -
    /// "sixty-one point two three five seconds" - for a time a screen shows
    /// to the thousandth (8 Nov 2026): a lap outside the lap-time
    /// convention, and a heat's run time. With nothing in the thousandth it
    /// is Plainly's reading, word for word.
    /// </summary>
    internal static string PlainlyToTheThousandth(long ms)
    {
        if (ms < 0)
        {
            return "minus " + PlainlyToTheThousandth(-ms);
        }

        if (ms % 10 == 0)
        {
            return Plainly(ms);
        }

        var digits = (ms % 1000)
            .ToString("000", System.Globalization.CultureInfo.InvariantCulture)
            .Select(ch => Small[ch - '0']);

        return $"{Words(ms / 1000)} point {string.Join(" ", digits)} seconds";
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
        // WHY (8 Nov 2026): a negative time gave "point - five zero" - the
        // sign landed in the digits, which are spelled one character at a
        // time. A minus is said as a word, before the figure.
        if (ms < 0)
        {
            return "minus " + Plainly(-ms);
        }

        // WHY (10 Nov 2026, bug hunt 16): Math.Round here rounded a half to
        // the even hundredth, so a 5.125 split was said "five point one two"
        // while the screen showed "5.13s". Half up, in whole numbers, the
        // same as HundredthsShown, so the two never disagree.
        var centiseconds = HundredthsOf(ms);
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
    /// A gap EXACTLY as timed, to the thousandth, in words: "one point two
    /// three four", "point three oh five", "two point three", "a second",
    /// "four seconds". For the running order, where the figure is the one
    /// the board shows and has to match it.
    ///
    /// WHY (1 Nov 2026): the position report gave its gaps the way a gap is
    /// called (GapSpoken) - tenths under a second, halves above, whole
    /// seconds past ten - so 2.361 was "two and a half seconds" under a
    /// board that said +2.36, and a club heard it as the voice getting the
    /// gap wrong. The digits after the point are said one at a time, an
    /// "oh" for a nought, with any noughts at the end left off - 2.300 is
    /// "two point three", which is the same gap. Words only, for the reason
    /// at the top of this class. Nothing (an exact tie) is "nothing".
    /// </summary>
    internal static string GapExactly(long ms)
    {
        ms = Math.Abs(ms);

        if (ms == 0)
        {
            return "nothing";
        }

        var whole = ms / 1000;
        var digits = (ms % 1000)
            .ToString("000", System.Globalization.CultureInfo.InvariantCulture)
            .TrimEnd('0');

        if (digits.Length == 0)
        {
            return whole == 1 ? "a second" : $"{Words(whole)} seconds";
        }

        var after = string.Join(" ", digits.Select(ch => ch == '0' ? "oh" : Small[ch - '0']));

        return whole == 0 ? $"point {after}" : $"{Words(whole)} point {after}";
    }

    /// <summary>
    /// A gap in whole tenths, half up, from the milliseconds - the one
    /// rounding a gap shown as "1.3s" and the same gap said both use
    /// (8 Nov 2026). Integer arithmetic, because "0.0" on ms / 1000.0 rounds
    /// a 1.150 to "1.1" (the double is 1.1499...) and a 1.250 to "1.3".
    /// </summary>
    internal static long TenthsOf(long ms) => (Math.Abs(ms) + 50) / 100;

    /// <summary>
    /// A time in whole hundredths, half up, from the milliseconds - the one
    /// rounding <see cref="Plainly"/> and <see cref="HundredthsShown"/> both
    /// use (10 Nov 2026, bug hunt 16). Integer arithmetic for the reason on
    /// TenthsOf: "0.00" on ms / 1000.0 shows a 5.135 as "5.13".
    /// </summary>
    internal static long HundredthsOf(long ms) => (Math.Abs(ms) + 5) / 10;

    /// <summary>
    /// A split or sector time on a screen, to the hundredth - "5.13s" -
    /// agreeing with what <see cref="Plainly"/> says (10 Nov 2026, bug hunt 16).
    /// </summary>
    internal static string HundredthsShown(long ms)
    {
        var c = HundredthsOf(ms);
        var sign = ms < 0 && c > 0 ? "-" : "";
        return $"{sign}{c / 100}.{c % 100:00}s";
    }

    /// <summary>A gap of <paramref name="tenths"/> tenths on a screen: "1.3s".</summary>
    internal static string TenthsShown(long tenths) =>
        $"{tenths / 10}.{tenths % 10}s";

    /// <summary>
    /// A gap of <paramref name="tenths"/> tenths SAID, agreeing with
    /// <see cref="TenthsShown"/> to the tenth: "a tenth", "half a second",
    /// "a second", "one point three", "a second and a half", "twelve point
    /// three".
    ///
    /// WHY (8 Nov 2026): the split and ghost lines showed "down 1.3s" and
    /// said it through <see cref="GapSpoken"/>, which rounds past a second
    /// to the half - "down a second and a half" under a screen that said
    /// 1.3. That is the mistake the position report made and GapExactly
    /// fixed: rounding is heard as the wrong gap. The tenths are said as
    /// GapExactly says its digits, with no "seconds" after a point.
    /// </summary>
    internal static string GapInTenths(long tenths)
    {
        tenths = Math.Abs(tenths);

        if (tenths == 0)
        {
            return "nothing";
        }

        if (tenths < 10)
        {
            return tenths switch
            {
                1 => "a tenth",
                5 => "half a second",
                _ => $"{Words(tenths)} tenths",
            };
        }

        var whole = tenths / 10;
        var tenth = tenths % 10;

        return tenth switch
        {
            0 => whole == 1 ? "a second" : $"{Words(whole)} seconds",
            5 => whole == 1 ? "a second and a half" : $"{Words(whole)} and a half seconds",
            _ => $"{Words(whole)} point {Small[tenth]}",
        };
    }

    /// <summary>
    /// <see cref="GapExactly"/> with the direction on it - "one point two
    /// three four back" - for the chasing car. A tie to the thousandth is
    /// said as exactly that, "level to the thousandth", not "nothing in it",
    /// which is the rounded call this replaced.
    /// </summary>
    internal static string BehindExactly(long ms)
    {
        var gap = GapExactly(ms);

        return gap == "nothing" ? "level to the thousandth" : $"{gap} back";
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
