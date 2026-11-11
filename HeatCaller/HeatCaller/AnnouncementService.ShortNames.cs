using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// "Shorten published names to a first name and an initial", carried to the
/// voices that send a line OFF this PC (22 Oct 2026, known issues).
///
/// WHY: the setting shortened names on the club's pages, but every spoken
/// line still carried the driver's full Announcer name - or driver name - to
/// the relay and on to ElevenLabs, where the rendered audio is kept for that
/// club. A club that ticked the box for its children's names would not
/// expect that. So with the box ticked, a line for the HeatCaller voice or
/// the club's own ElevenLabs account has each driver's full name swapped
/// for the same short form the pages use (RelayPublisher.DisplayNamesForTheRoom,
/// the pages' shortening without the online initials-only rule, so two
/// drivers who would share one are told apart the same way).
///
/// NOT FOR KOKORO. It renders on this PC, so the announcer there keeps
/// saying names in full, as it always has. That audio is NOT kept off the
/// internet, though: listen-along sends what the room hears. So while the
/// box is ticked, a line whose names this would shorten goes to listeners
/// only when its audio was rendered from the shortened words, and a Kokoro
/// (or stand-in, or soundscape) take of it is held back - see
/// OfferToListenersOnly and AudioFromShortenedText (corrected 23 Oct 2026,
/// bug hunt 10: this note said Kokoro sends nothing anywhere).
///
/// The swap is of whole names only, longest first, so "Sam Smith-Jones" is
/// never cut into "Sam S-Jones" by a shorter "Sam Smith".
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// Whether to shorten names on a line for a cloud voice - read live, so
    /// ticking the box takes effect on the next line. Null (the default, and
    /// every test) leaves every line exactly as written.
    /// </summary>
    public Func<bool>? ShortenNamesForCloudVoices { get; set; }

    /// <summary>
    /// "Say full names aloud with the online voices" (24 Oct 2026): the
    /// HeatCaller voice and ElevenLabs are sent each line with names in full,
    /// as Kokoro always is, even while the club shortens published names.
    /// Read live. Null or false (the default) shortens as before.
    ///
    /// WHY: the club wanted the announcer to call drivers by their full names
    /// but still keep them short on its pages. The pages, the public captions
    /// and listen-along still shorten (see TextForPublicPages): a line whose
    /// audio says a full name is held back from listeners at home exactly as
    /// a Kokoro line is - see AudioFromShortenedText.
    /// </summary>
    public Func<bool>? SayFullNamesWithCloudVoices { get; set; }

    /// <summary>Whether a line for a cloud voice is shortened just now - the club shortens and has not asked for full names aloud.</summary>
    private bool ShortensForCloudVoice() =>
        ShortenNamesForCloudVoices?.Invoke() == true && SayFullNamesWithCloudVoices?.Invoke() != true;

    private readonly object _shortNamesLock = new();
    private string? _shortNamesKey;
    private (Regex Pattern, Dictionary<string, string> Map)? _shortNames;

    /// <summary>
    /// The words a cloud voice's take of this line is saved under - for
    /// Voice Settings' Prepare Lines, so "already saved" and the cost it
    /// quotes go by the same text the race looks up.
    /// </summary>
    internal string TextForCloudVoice(string text) => ForCloudVoice(text);

    /// <summary>
    /// The line as the club's public pages show it - names shortened while
    /// "Shorten published names" is ticked, whatever the voices are sent. For
    /// the public captions and listen-along (24 Oct 2026).
    /// </summary>
    internal string TextForPublicPages(string text) =>
        ShortenNamesForCloudVoices?.Invoke() == true ? Shortened(text) : text;

    /// <summary>The line as a cloud voice should be sent it - see the class note and SayFullNamesWithCloudVoices.</summary>
    /// "A-Main" goes as "Ay-Main" (9 Nov 2026) - see RaceNumbers.SpokenRoundNames.
    private string ForCloudVoice(string text) =>
        RaceNumbers.SpokenRoundNames(ShortensForCloudVoice() ? Shortened(text) : text, cloudVoice: true);

    /// <summary>The line with every driver's full name in its short form.</summary>
    private string Shortened(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        List<string> names;

        try
        {
            names = NamesToShorten();
        }
        catch (InvalidOperationException)
        {
            // The roster changed under the read - read it once more.
            names = NamesToShorten();
        }

        var key = string.Join('\n', names);
        (Regex Pattern, Dictionary<string, string> Map)? built;

        lock (_shortNamesLock)
        {
            if (!string.Equals(key, _shortNamesKey, StringComparison.Ordinal))
            {
                _shortNamesKey = key;
                _shortNames = ShortNamePattern(ShortNameMap(names));
            }

            built = _shortNames;
        }

        return built is { } shortNames ? ShortenNames(text, shortNames.Pattern, shortNames.Map) : text;
    }

    private List<string> NamesToShorten() =>
        _profiles.All.ToList()
            .SelectMany(profile => new[] { profile.SpokenName, profile.DriverName })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Each full name (two words or more) and the short form the club's
    /// pages give it. A one-word name is already as short as it goes.
    /// </summary>
    internal static Dictionary<string, string> ShortNameMap(IReadOnlyList<string> names)
    {
        var full = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // The room's form (22 Oct 2026, driver experience): these lines are
        // said over the PA, which is in the building, so a driver who asked
        // for initials only ONLINE is still called by name here - the same
        // shortening as ever, nobody's privacy choice applied.
        var shortened = RelayPublisher.DisplayNamesForTheRoom(full, abbreviate: true);
        var map = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);

        for (var i = 0; i < full.Count && i < shortened.Count; i++)
        {
            if (full[i].Contains(' ') && !string.Equals(full[i], shortened[i], StringComparison.Ordinal))
            {
                map[full[i]] = shortened[i];
            }
        }

        return map;
    }

    /// <summary>One pattern for every name in the map, longest first, whole names only. Null for an empty map.</summary>
    private static (Regex Pattern, Dictionary<string, string> Map)? ShortNamePattern(Dictionary<string, string> map)
    {
        if (map.Count == 0)
        {
            return null;
        }

        var alternatives = map.Keys
            .OrderByDescending(name => name.Length)
            .Select(Regex.Escape);

        var pattern = new Regex(
            $@"(?<![\w])(?:{string.Join('|', alternatives)})(?![\w])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return (pattern, map);
    }

    /// <summary>The text with every full name in the map replaced by its short form.</summary>
    internal static string ShortenNames(string text, Dictionary<string, string> map)
    {
        if (string.IsNullOrEmpty(text) || ShortNamePattern(map) is not { } built)
        {
            return text;
        }

        return ShortenNames(text, built.Pattern, built.Map);
    }

    private static string ShortenNames(string text, Regex pattern, Dictionary<string, string> map) =>
        pattern.Replace(text, match => map.TryGetValue(match.Value, out var shortName) ? shortName : match.Value);
}
