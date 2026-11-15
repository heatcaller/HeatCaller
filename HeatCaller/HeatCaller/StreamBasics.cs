namespace HeatCaller;

/// <summary>
/// "NEW TO RC RACING?" (14 Nov 2026, stream overlay plan 7.6): the cards the
/// stream shows between races, one every twelve seconds - the basics for a
/// family watching from home, and a few racing words.
///
/// The club's own when it writes some (AppSettings.StreamBasics, one card a
/// line, "Heading | words"), and these otherwise. A club can add a line for
/// each of its classes the same way: "Tonight's classes | Junior: under-16s
/// on the 1/28 cars".
/// </summary>
internal static class StreamBasics
{
    /// <summary>The cards HeatCaller comes with.</summary>
    internal static readonly IReadOnlyList<(string Heading, string Words)> BuiltIn = new[]
    {
        ("New to RC racing?", "Races run for a set time. When the clock runs out, everyone finishes the lap they’re on. Most laps wins; on the same laps, the quicker time wins."),
        ("New to RC racing?", "Everyone qualifies in heats. The quickest go through to the A-Main, the next to the B-Main."),
        ("New to RC racing?", "The drivers standing by the track are marshals, from the race before. They put crashed cars back on their wheels."),
        ("New to RC racing?", "Each car carries a transponder that counts its laps as it crosses the line."),
        ("Racing words", "Laps/time, like 23/5:02.341: 23 laps in 5 minutes 2.341 seconds."),
        ("Racing words", "TQ: top qualifier, the quickest driver in qualifying. They start the A-Main from the front."),
        ("Racing words", "Lapped (+1L): the leader has gone a whole lap further. Interval: the gap to the car just ahead."),
        ("Racing words", "A personal best (PB) is a driver’s own fastest lap - often the thing worth cheering most."),
    };

    /// <summary>The built-in cards written the way the club edits them: one a line, "Heading | words".</summary>
    internal static string BuiltInText => string.Join(Environment.NewLine, BuiltIn.Select(c => c.Heading + " | " + c.Words));

    /// <summary>
    /// The cards to show: the club's lines when it has written any that read
    /// as cards, the built-in ones otherwise. A line with no "|" is all words,
    /// under "New to RC racing?". At most 30 cards of 300 characters.
    /// </summary>
    internal static IReadOnlyList<(string Heading, string Words)> Cards(string? clubText)
    {
        var mine = (clubText ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line =>
            {
                var bar = line.IndexOf('|');
                var heading = bar < 0 ? "New to RC racing?" : line[..bar].Trim();
                var words = bar < 0 ? line : line[(bar + 1)..].Trim();
                return (Heading: Clip(heading.Length == 0 ? "New to RC racing?" : heading, 40), Words: Clip(words, 300));
            })
            .Where(c => c.Words.Length > 0)
            .Take(30)
            .ToList();

        return mine.Count > 0 ? mine : BuiltIn;
    }

    /// <summary>The cards as the page's script reads them: [["heading", "words"], ...].</summary>
    internal static string ScriptJson(string? clubText) =>
        System.Text.Json.JsonSerializer.Serialize(Cards(clubText).Select(c => new[] { c.Heading, c.Words }))
            .Replace("</", "<\\/", StringComparison.Ordinal);

    private static string Clip(string text, int most) => text.Length <= most ? text : text[..most].TrimEnd() + "…";
}
