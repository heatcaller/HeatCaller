using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// Finishing a filled-in announcement line so it reads like a sentence.
///
/// Announcement lines are templates a club writes: "{driver} takes the
/// lead in his {car}!". Some placeholders have nothing behind them - a
/// driver with no car model set, a lap with no time yet, a session with no
/// round - and substituting an empty string leaves the words that were
/// only there to introduce it:
///
///     "driving the Ghost of Christmas Present, in his !"
///
/// That is what a spectator hears, and what the voice reads out.
///
/// The templates cannot be fixed instead: they are user-editable data, and
/// a club that writes its own lines would hit this the same way. So the
/// fill marks a placeholder it could not fill, and this takes the marker
/// out along with its lead-in.
/// </summary>
internal static class AnnouncementText
{
    /// <summary>
    /// Stands in for a placeholder with nothing to put in it.
    ///
    /// A control character rather than a word, so it cannot collide with
    /// anything a club might legitimately type into a template.
    /// </summary>
    internal const string Missing = "\u0001";

    /// <summary>Marks an empty value so <see cref="TidyTemplate"/> can take its clause out.</summary>
    internal static string OrMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Missing : value;

    /// <summary>
    /// The little words that exist only to introduce the thing that turned
    /// out to be missing. "in his {car}", "with the {car}", "on lap
    /// {lap}". Dropped whole, together with a comma in front of them if
    /// there was one.
    /// </summary>
    // WHY (23 Nov 2026, bug hunt 24): an aside set off by two commas -
    // "{driver}, in the {car}, takes the lead!" - lost only the comma in
    // front, so a driver with no car model was read as "Sam, takes the
    // lead!", with a pause and a stray comma on screen. The comma AFTER the
    // clause now goes too, but only when there was one in front (so "takes
    // the lead in his {car}, ahead of Bill" keeps its comma) and only when
    // the words after it carry straight on in lower case, as a verb does.
    // "Sam leads, in the {car}, Bill second" is a list, not an aside, and
    // keeps the comma between its two halves.
    private static readonly Regex LeadIn = new(
        @"(?:(?<lead>,)|;)?\s*\b(?:in|with|on|at|aboard|driving|behind\s+the\s+wheel\s+of)\s+"
        + @"(?:the|his|her|their|its|a|an)?\s*" + Missing
        + AsideCloser,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The closing comma of an aside whose opening comma (the "lead" group)
    /// was taken: only when the sentence carries on in lower case. Case
    /// sensitive on purpose, inside patterns that are not.
    /// </summary>
    private const string AsideCloser = @"(?(lead)(?:\s*,(?=\s*(?-i:\p{Ll})))?)";

    // WHY (23 Nov 2026, bug hunt 24): "[excited] In the {car}, Sam leads!"
    // with no car model came out "[excited], Sam leads!". The repair for a
    // line that starts with punctuation is anchored at the very start, and
    // here the audio tag comes first. This takes punctuation left straight
    // after the opening tag or tags.
    private static readonly Regex PunctuationAfterOpeningTag = new(
        @"^(\s*(?:\[[^\]]*\]\s*)+)[,;:][\s,;:]*", RegexOptions.Compiled);

    private static readonly Regex Marker = new(Missing, RegexOptions.Compiled);
    private static readonly Regex RunOfSpaces = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,.;:!?])", RegexOptions.Compiled);
    private static readonly Regex DanglingComma = new(@"([,;:])\s*([.!?])", RegexOptions.Compiled);
    private static readonly Regex LeadingPunctuation = new(@"^[\s,;:]+", RegexOptions.Compiled);
    private static readonly Regex RepeatedPunctuation = new(@"([.!?]){2,}", RegexOptions.Compiled);

    /// <summary>
    /// "Kyosho, PN Racing\nDad" as it would be said: "Kyosho, PN Racing
    /// and Dad". Commas, semicolons and line breaks all separate - the
    /// Sponsors box is multi-line, and people type lists both ways. Empty
    /// when there is nothing to say.
    /// </summary>
    internal static string SpokenList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var items = raw
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return SpokenList(items);
    }

    /// <summary>
    /// Names already split up - "Sam", "Bill", "Frank" - said as "Sam, Bill
    /// and Frank". Taken as a list rather than one string so a name that
    /// happens to contain a comma is not broken in two.
    /// </summary>
    internal static string SpokenList(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
        };
    }

    /// <summary>
    /// The words a club would put in front of a team or a sponsor list -
    /// "sponsored by {sponsors}", "racing for {team}", "({team})". When
    /// the driver has nothing to fill it, the phrase goes with it.
    ///
    /// Kept apart from <see cref="LeadIn"/> on purpose. Widening that one
    /// with "for" or "from" would also reach every OTHER placeholder, and
    /// change lines that already work: a default grid call reads "Drivers
    /// for {class} {round}", which would lose its "for" on the night a
    /// class name was blank. Scoped to these two tokens, nothing that
    /// shipped before them can read differently.
    /// </summary>
    // WHY (23 Nov 2026, bug hunt 24): three changes to the first
    // alternative.
    //  - "Fast lap for {team}'s {driver}!" with no team was read as "Fast
    //    lap's Sam!": " for {team}" matched here first and left the "'s"
    //    behind, so the possessive alternative below never got its turn.
    //    A token followed by 's is now left to that alternative, giving
    //    "Fast lap for Sam!".
    //  - A bare "and" in front of the token ("for {team} and {sponsors}")
    //    was not recognised, so a driver with no sponsors was read as
    //    "takes the win and!". It now goes with the token.
    //  - An aside between two commas ("{driver}, racing for {team}, takes
    //    the lead!") lost only the comma in front: "Sam, takes the lead!".
    //    The closing comma goes too, on the same terms as LeadIn.
    private static Regex DetailLeadIn(string token) => new(
        @"(?:(?<lead>,)|;)?\s*\b(?:(?:and|with)\s+)?"
        + @"(?:brought\s+to\s+you\s+by|(?:sponsored|backed|powered|supported)\s+by|by|thanks\s+to|representing"
        + @"|(?:racing|running|driving|riding)\s+for|for|from|with|of|and)\s+"
        + @"(?:the|his|her|their|team)?\s*\{" + token + @"\}(?!'s|’s)" + AsideCloser
        + @"|\s*[(\[]\s*\{" + token + @"\}\s*[)\]]"
        + @"|\{" + token + @"\}(?:'s|\u2019s)\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TeamLeadIn = DetailLeadIn("team");
    private static readonly Regex SponsorsLeadIn = DetailLeadIn("sponsors");

    /// <summary>
    /// Fills {team} and {sponsors} from the driver's profile. Chained in
    /// before <see cref="TidyTemplate"/> at every place a driver's line is
    /// written, so a template can use them wherever it can use {car}.
    /// </summary>
    internal static string WithDriverDetails(this string text, DriverProfile? profile)
    {
        if (text.IndexOf('{') < 0)
        {
            return text;
        }

        var team = profile?.Team?.Trim() ?? string.Empty;
        var sponsors = SpokenList(profile?.Sponsors);

        if (team.Length == 0)
        {
            text = TeamLeadIn.Replace(text, string.Empty);
        }

        if (sponsors.Length == 0)
        {
            text = SponsorsLeadIn.Replace(text, string.Empty);
        }

        // Anything the patterns above did not recognise falls through to
        // the general tidy, same as every other empty placeholder.
        return text
            .Replace("{team}", OrMissing(team), StringComparison.OrdinalIgnoreCase)
            .Replace("{sponsors}", OrMissing(sponsors), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The words a club would put in front of the marshal list on a grid
    /// call - "Marshals: {marshals}.", "marshalled by {marshals}",
    /// "corner marshals are {marshals}" - or the bare placeholder. When
    /// nobody is assigned, the whole phrase goes, along with a full stop
    /// straight after it, so "grid up! Marshals: {marshals}." becomes
    /// "grid up!" rather than "grid up! Marshals.".
    ///
    /// Scoped to {marshals} for the same reason the team and sponsor
    /// patterns are: none of these words may start eating the lead-ins of
    /// placeholders that already read correctly.
    ///
    /// The closing full stop is only taken when the phrase was a sentence
    /// of its own. When it hung off the end of another sentence ("to the
    /// grid, marshals are {marshals}.") that full stop belongs to the
    /// sentence that is left, so only the comma in front goes.
    /// </summary>
    private const string MarshalsPhrase =
        @"(?:\b(?:marshall?ed\s+by|(?:corner\s+)?marshall?ing(?:\s+(?:is|are|today|tonight))?"
        + @"|(?:corner\s+)?marshals?(?:\s+(?:are|is|today|tonight))?|thanks\s+to|with|by)\s*:?\s*)?"
        + @"\{marshals\}";

    private static readonly Regex MarshalsLeadIn = new(
        @"(?:^|(?<=[.!?]))\s*" + MarshalsPhrase + @"(?:\s*[.!?])?"
        + @"|[,;]?\s*" + MarshalsPhrase,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Fills {marshals} on a grid call. Chained in before
    /// <see cref="TidyTemplate"/>, like <see cref="WithDriverDetails"/>.
    /// </summary>
    internal static string WithMarshals(this string text, string? marshals)
    {
        if (text.IndexOf('{') < 0)
        {
            return text;
        }

        var value = marshals?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            text = MarshalsLeadIn.Replace(text, string.Empty);
        }

        // Anything the pattern above did not recognise falls through to the
        // general tidy, same as every other empty placeholder.
        return text.Replace("{marshals}", OrMissing(value), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// How far a car is behind the leader (the board's "Gap to P1"), said
    /// rather than shown.
    ///
    /// IN WORDS AND EXACTLY (1 Nov 2026): "one point two three four back",
    /// to the thousandth, never rounded - see RaceNumbers.GapExactly. From
    /// 15 Oct it was rounded the way a gap is called ("three tenths back",
    /// "a second and a half back"), to keep the report short; clubs heard
    /// the rounding as the voice getting the gap wrong under a board that
    /// shows it to the hundredth. Never "1.5 back" in digits, which a voice
    /// reads its own way.
    ///
    /// A car down a lap is NEVER given in seconds. It is not "four point
    /// two seconds behind"; it is a lap behind, and the seconds between
    /// their last two crossings are an accident of where on track each of
    /// them happens to be. Same distinction, and the same answer, as the
    /// leaderboard's "+1L".
    ///
    /// Empty when there is nothing honest to say: no crossing recorded for
    /// the car ahead at this car's lap (a lap added by hand has no crossing
    /// time), or a gap that came out negative. The caller leaves the car
    /// out rather than invent a figure for it.
    /// </summary>
    internal static string SpokenGap(RaceGap gap)
    {
        if (gap.LapsDown == 1)
        {
            return "a lap down";
        }

        if (gap.LapsDown > 1)
        {
            // In words, for the reason RaceNumbers gives: a digit is a
            // number-shaped thing a voice engine may read its own way.
            return $"{RaceNumbers.Words(gap.LapsDown)} laps down";
        }

        if (gap.GapMs is not { } ms)
        {
            return string.Empty;
        }

        // RaceGaps.Compute withholds a gap that came out negative rather
        // than pass it on, and nothing here may invent one in its place.
        if (ms < 0)
        {
            return string.Empty;
        }

        // EXACTLY AS TIMED (1 Nov 2026) - "one point two three four back",
        // "point oh four back" - the figure the board shows, to the
        // thousandth. It was rounded the way a gap is called ("a second and
        // a half back", "under a tenth"), and a club heard the rounding as
        // the voice getting the gap wrong. Still all words: see
        // RaceNumbers.GapExactly.
        return RaceNumbers.BehindExactly(ms);
    }

    /// <summary>
    /// The gaps down the order as the report says them - "Bill a second
    /// and a half back, Frank two and a half seconds back and Dana a lap
    /// down".
    ///
    /// <paramref name="order"/> runs from the LEADER down, and the leader is
    /// left out of what comes back. Nobody is in front of them, so they have
    /// no gap: measured against themselves it is zero, and a voice reading
    /// "nought point nought" over the car that is winning the race is how a
    /// report starts sounding like a dead heat.
    ///
    /// A car whose gap could not be measured is left out too, rather than
    /// given a made-up one - see <see cref="SpokenGap"/>. With nobody left
    /// to name the result is empty, and the clause the club wrote around
    /// {gaps} goes with it (see <see cref="WithPositionGaps"/>).
    /// </summary>
    internal static string SpokenGaps(IReadOnlyList<(string Name, RaceGap Gap)> order)
    {
        var said = new List<string>();

        for (var i = 1; i < order.Count; i++)
        {
            var words = SpokenGap(order[i].Gap);

            if (words.Length == 0)
            {
                continue;
            }

            said.Add($"{order[i].Name} {words}");
        }

        return SpokenList(said);
    }

    /// <summary>
    /// The words a club would put in front of the gaps on a position report
    /// - "Gaps: {gaps}", "and the gaps - {gaps}", "with {gaps}" - or the
    /// bare placeholder. With the gaps switched off, or with nothing
    /// measurable to say, the whole phrase goes, along with a full stop
    /// straight after it, so "The order - {positions}. Gaps: {gaps}."
    /// becomes "The order - ..." rather than "... Gaps.".
    ///
    /// Scoped to {gaps} for the same reason the team, sponsor and marshal
    /// patterns are: none of these words may start eating the lead-ins of
    /// placeholders that already read correctly. Widening the general
    /// <see cref="LeadIn"/> with "gaps" would be harmless, but widening it
    /// with the "with" and "and" in front of them would reach every other
    /// placeholder in the app.
    ///
    /// A dash is matched as well as a comma because every built-in line in
    /// this app separates its clauses with " - ", so that is what a club
    /// copying one of them will type.
    /// </summary>
    private const string GapsPhrase =
        @"(?:\b(?:and|with)\s+)?(?:the\s+)?"
        + @"(?:(?:gaps?|intervals?|margins?)(?:\s+between\s+them)?(?:\s+(?:are|is))?\s*[-:]?\s*)?"
        + @"\{gaps\}";

    // A closing bracket counts as the end of a sentence here, which the
    // marshal pattern has no need of: every built-in position-report line
    // opens with an audio tag, so a club writing "Gaps: {gaps}." as its own
    // sentence has "[excited]" and not a full stop in front of it, and the
    // full stop at the end of the phrase was left stranded against the tag.
    private static readonly Regex GapsLeadIn = new(
        @"(?:^|(?<=[.!?])|(?<=\]))\s*" + GapsPhrase + @"(?:\s*[.!?])?"
        + @"|[,;]?\s*(?:-\s*)?" + GapsPhrase,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Fills {gaps} on a position report. Chained in before
    /// <see cref="TidyTemplate"/>, like <see cref="WithMarshals"/>.
    /// </summary>
    internal static string WithPositionGaps(this string text, string? gaps)
    {
        if (text.IndexOf('{') < 0)
        {
            return text;
        }

        var value = gaps?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            text = GapsLeadIn.Replace(text, string.Empty);
        }

        // Anything the pattern above did not recognise falls through to the
        // general tidy, same as every other empty placeholder.
        return text.Replace("{gaps}", OrMissing(value), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Trims, and repairs whatever an empty placeholder left behind.
    ///
    /// Replaces the plain .Trim() every template fill used to end with, so
    /// the tidy cannot be forgotten at one call site and not another -
    /// which is how this went unnoticed in the first place.
    /// </summary>
    internal static string TidyTemplate(this string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        if (text.Contains(Missing, StringComparison.Ordinal))
        {
            text = LeadIn.Replace(text, string.Empty);

            // Anything the lead-in pattern did not recognise: take the
            // marker out on its own rather than letting it reach a voice.
            text = Marker.Replace(text, string.Empty);
        }

        text = RunOfSpaces.Replace(text, " ");
        text = SpaceBeforePunctuation.Replace(text, "$1");
        text = DanglingComma.Replace(text, "$2");
        text = RepeatedPunctuation.Replace(text, OneMark);
        text = LeadingPunctuation.Replace(text, string.Empty);
        text = PunctuationAfterOpeningTag.Replace(text, "$1 ");

        return text.Trim();
    }

    /// <summary>
    /// A run of full stops, question and exclamation marks down to one -
    /// the "race.." an empty placeholder leaves - but NOT what an author
    /// typed on purpose: an ellipsis is a pause to the voice ("And
    /// they're off... GO!"), and "?!" is a question said with surprise.
    /// Cutting either changes how the line is spoken.
    /// </summary>
    private static string OneMark(Match run)
    {
        var marks = run.Value;

        if (marks.Length >= 3 && marks.All(c => c == '.'))
        {
            return "...";
        }

        if (marks.Contains('?') && marks.Contains('!') && !marks.Contains('.'))
        {
            return marks;
        }

        return marks[^1].ToString();
    }
}
