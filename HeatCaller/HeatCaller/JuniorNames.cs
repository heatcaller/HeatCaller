namespace HeatCaller;

/// <summary>
/// A junior's name on the stream (14 Nov 2026, stream overlay plan 7.5).
///
/// A stream goes to anyone on YouTube, and a 10-year-old's full name there
/// is the thing a family worries about most. So by default a junior shows
/// as a first name and an initial - "Mia K" - shortened the way the relay
/// already shortens names (RelayPublisher.DisplayNamesForTheRoom), so two
/// juniors who would both be "Sam T" are told apart ("Sam T", "Sam Tr").
/// The room, the PA and the driver's own night still use the full name.
/// </summary>
internal static class JuniorNames
{
    public const string Initial = "initial";
    public const string First = "first";
    public const string Full = "full";

    /// <summary>The setting as one of the three, "initial" for anything else.</summary>
    public static string Rule(string? setting) => setting switch
    {
        First => First,
        Full => Full,
        _ => Initial,
    };

    /// <summary>
    /// Each junior's full name and what the stream calls them instead, for
    /// every junior whose name changes under the rule. Empty under "full".
    /// </summary>
    public static IReadOnlyDictionary<string, string> ForStream(IEnumerable<string> juniorNames, string? setting)
    {
        var rule = Rule(setting);
        var names = juniorNames
            .Select(n => (n ?? string.Empty).Trim())
            .Where(n => n.Length > 0 && n.Contains(' ', StringComparison.Ordinal) && n != RelayDocuments.UnnamedDriver)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (rule == Full || names.Count == 0)
        {
            return map;
        }

        var shortened = rule == First
            ? names.Select(n => n.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]).ToList()
            : RelayPublisher.DisplayNamesForTheRoom(names, abbreviate: true).ToList();

        for (var i = 0; i < names.Count; i++)
        {
            if (!string.Equals(names[i], shortened[i], StringComparison.Ordinal))
            {
                map[names[i]] = shortened[i];
            }
        }

        return map;
    }
}
