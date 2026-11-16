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

    /// <summary>
    /// <see cref="ForStream(IEnumerable{string}, string?)"/>, with every
    /// junior whose profile says "On stream: Not at all" (15 Nov 2026,
    /// children's privacy plan 4.2) shown as <see cref="OffStreamName"/>
    /// instead - whatever the rule, "full" included, and a one-word name too.
    /// </summary>
    /// <param name="offStream">Each such junior's full name and what the stream shows for them ("Car 4").</param>
    public static IReadOnlyDictionary<string, string> ForStream(
        IEnumerable<string> juniorNames,
        string? setting,
        IReadOnlyDictionary<string, string>? offStream)
    {
        var names = juniorNames.ToList();
        var map = new Dictionary<string, string>(ForStream(names, setting), StringComparer.Ordinal);

        if (offStream is null || offStream.Count == 0)
        {
            return map;
        }

        foreach (var name in names.Select(n => (n ?? string.Empty).Trim()).Where(n => n.Length > 0))
        {
            if (offStream.TryGetValue(name, out var shown))
            {
                map[name] = shown;
            }
        }

        return map;
    }

    /// <summary>
    /// What the stream shows for a junior kept off it: their race number as
    /// "Car 4", or "A driver" when they have none - never any part of their
    /// name (15 Nov 2026, children's privacy plan 4.2).
    /// </summary>
    public static string OffStreamName(string? raceNumber) =>
        string.IsNullOrWhiteSpace(raceNumber) ? "A driver" : $"Car {raceNumber.Trim()}";
}
