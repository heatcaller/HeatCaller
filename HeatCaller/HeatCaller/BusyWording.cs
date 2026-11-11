using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// Shorter wording when the track is busy (10 Nov 2026, voice speed plan,
/// phase 2).
/// </summary>
/// <remarks>
/// WHY. Calls averaged four seconds of words in the v4 Turbo races, and the
/// slowest ones - a minutes-left call, a running order - waited four to six
/// seconds behind a line of six or seven. A real announcer with the field
/// packed says "Dee Ploy, six eight seven" and moves on; with the track
/// quiet, the flourish.
///
/// FROM THE CLUB'S OWN LINES, NOT NEW ONES. Every call already has several
/// templates in every theme, and they differ in length. So when it is busy
/// a call picks from the SHORTER HALF of its own templates, as they will be
/// said - names and times counted, delivery tags not - and the optional
/// tail ("That's the fifth lead change, among four drivers") is left off.
/// No theme needs a second set of words, and a club's edits on the Voice
/// page still decide everything that is said.
///
/// Instructions keep their wording whatever the track is doing - a flag,
/// the grid, a release, a reminder (AnnouncementService decides which).
/// </remarks>
internal static partial class BusyWording
{
    /// <summary>How far back routine calls are counted to tell a busy track - see IsBusy.</summary>
    internal static readonly TimeSpan Window = TimeSpan.FromSeconds(6);

    /// <summary>Routine calls offered within <see cref="Window"/> that make it busy, with nothing on the speaker.</summary>
    internal const int CallsInWindow = 2;

    /// <summary>
    /// Whether it is busy: a line speaking or waiting, a call kept for the
    /// end of one, or <see cref="CallsInWindow"/> routine calls offered in
    /// the last <see cref="Window"/>.
    /// </summary>
    internal static bool IsBusy(bool speaking, bool waiting, int recentCalls) =>
        speaking || waiting || recentCalls >= CallsInWindow;

    /// <summary>
    /// The shorter half of <paramref name="templates"/> by how long each
    /// will be to say (<see cref="SpokenLength"/>) - the shortest first,
    /// rounding up, so one template stays one and two give one. Order kept
    /// otherwise. Never empty for a non-empty list.
    /// </summary>
    internal static IReadOnlyList<string> ShorterHalf(IReadOnlyList<string> templates)
    {
        if (templates.Count <= 1)
        {
            return templates;
        }

        var keep = (templates.Count + 1) / 2;

        var shortest = templates
            .Select((template, at) => (template, at, length: SpokenLength(template)))
            .OrderBy(t => t.length)
            .ThenBy(t => t.at)
            .Take(keep)
            .OrderBy(t => t.at)
            .Select(t => t.template)
            .ToList();

        return shortest;
    }

    /// <summary>
    /// About how many characters a template is once said: delivery tags
    /// ("[excited]") count for nothing, a time for the twenty-odd letters
    /// of "six eight seventy-five", a name for a dozen, any other
    /// placeholder for eight.
    /// </summary>
    internal static int SpokenLength(string template)
    {
        var said = Tags().Replace(template, string.Empty);

        said = Placeholders().Replace(said, match => match.Groups[1].Value.ToLowerInvariant() switch
        {
            "time" or "laptime" => new string('x', 22),
            "driver" => new string('x', 12),
            _ => new string('x', 8),
        });

        return said.Trim().Length;
    }

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\{([a-zA-Z]+)\}")]
    private static partial Regex Placeholders();
}
