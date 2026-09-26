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
    private static readonly Regex LeadIn = new(
        @"[,;]?\s*\b(?:in|with|on|at|aboard|driving|behind\s+the\s+wheel\s+of)\s+"
        + @"(?:the|his|her|their|its|a|an)?\s*" + Missing,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
    private static Regex DetailLeadIn(string token) => new(
        @"[,;]?\s*\b(?:(?:and|with)\s+)?"
        + @"(?:brought\s+to\s+you\s+by|(?:sponsored|backed|powered|supported)\s+by|by|thanks\s+to|representing"
        + @"|(?:racing|running|driving|riding)\s+for|for|from|with|of)\s+"
        + @"(?:the|his|her|their|team)?\s*\{" + token + @"\}"
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
        text = RepeatedPunctuation.Replace(text, "$1");
        text = LeadingPunctuation.Replace(text, string.Empty);

        return text.Trim();
    }
}
