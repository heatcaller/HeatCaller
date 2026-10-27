using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// The two lines either side of a hand-over between the show host and
/// another member of the cast: the host's throw to them, and their hand
/// back to the host.
///
/// WHY (26 Oct 2026, asked for by Asher: "When the show host throws it to
/// other cast member it sounds robotic. reference the show host name when
/// sending it back. add in a line or two. make it AI and prerender."): every
/// hand-over in the show was a card read out. "Let's head down to pit lane,
/// where Jess is with the Technical Lead from Sam's team. Jess?" - the same
/// sentence after every race, and then the guest finished and either said
/// "That's the word from pit lane - back to you, Rick" or said nothing at
/// all and the host simply carried on. The achievements reader never
/// handed back at all. Two colleagues who can hear each other do not talk
/// like that: the host picks up from what was just said and passes it on,
/// and the guest reacts to what they covered and passes it back BY NAME.
///
/// THE MODEL WRITES BOTH LINES, THIS HOLDS THEM TO THE BRIEF - the
/// arrangement <see cref="StopSetThrow"/> already has for the throw into
/// the break. CommentaryService.BuildHandOffAsync asks for the pair in one
/// request; <see cref="Split"/> takes the two lines apart and
/// <see cref="Checked"/> turns down one that runs long or leaves out the
/// name it has to say - the guest's in the throw, the host's in the hand
/// back. A line turned down is replaced by the one written here, so the
/// hand back names the host whatever the model did, and on a night with the
/// AI off, a model that is late or a reply that will not do, the show still
/// hands over and still hands back.
///
/// VOICED AHEAD, NEVER ON THE SPOT. The callers in Race Control write and
/// render the pair while something else is playing, and only use it if it
/// is ready (audio and all) by the moment it is wanted - otherwise the
/// built-in line, already rendered, goes out instead. A hand-over is the
/// one place a gap is loudest: a second of the voice service between
/// "Jess?" and Jess is the room hearing a machine.
///
/// Pure, so every rule is a table test.
/// </summary>
internal static class HandOff
{
    /// <summary>The longest either line may run. "A line or two" - past this it is a speech, not a hand-over.</summary>
    internal const int MaxWords = 40;

    /// <summary>How the model labels the host's line.</summary>
    internal const string ThrowTag = "THROW:";

    /// <summary>And the guest's.</summary>
    internal const string BackTag = "BACK:";

    /// <summary>
    /// What the model is told about one hand-over. Every name is cleaned for
    /// the prompt by CommentaryService (Field) when the prompt is built, and
    /// the check compares against the same cleaned names.
    /// </summary>
    /// <param name="Host">The show host's name as set - the one who throws, and is handed back to. Blank: never named.</param>
    /// <param name="HostRole">"the race announcer, up in the booth" - who the host is, in words.</param>
    /// <param name="Guest">The cast member's name as set. Blank: thrown to without a name.</param>
    /// <param name="GuestRole">"the co-host, down in pit lane" - who the guest is, in words.</param>
    /// <param name="About">What the guest does between the two lines.</param>
    /// <param name="ThrowMustSay">
    /// What the throw has to carry, when what follows it was written to pick
    /// up from it - the pit lane interview is told the throw has named the
    /// guest's role and whose team it is (CommentaryService.ThrownDownTo).
    /// </param>
    /// <param name="JustSaid">What was said on air just before the throw, for the host to pick up from.</param>
    /// <param name="GuestCovers">What the guest covers, for the hand back to react to.</param>
    /// <param name="Next">What comes after the hand back, if anything is known.</param>
    internal sealed record Brief(
        string Host,
        string HostRole,
        string Guest,
        string GuestRole,
        string About,
        string? ThrowMustSay = null,
        string? JustSaid = null,
        string? GuestCovers = null,
        string? Next = null);

    /// <summary>The model's two lines once checked - either may be null, and then the built-in one is said for that half.</summary>
    internal sealed record Written(string? Throw, string? Back);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Square-bracket tags or stage directions the model was told not to write.</summary>
    private static readonly Regex Brackets = new(@"\[[^\]]*\]|\([^)]*\)", RegexOptions.Compiled);

    /// <summary>"THROW:", "**Throw:**", "Back :" - at the start of a line or after the end of the other one.</summary>
    private static readonly Regex Tags = new(
        @"(?<![\p{L}\p{N}])[*_]*(?<tag>throw|back)[*_]*\s*:[*_]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The model's reply taken apart into the throw and the hand back, raw.
    /// Null for a half that is missing. Only the first of each is taken.
    /// </summary>
    internal static (string? Throw, string? Back) Split(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return (null, null);
        }

        var tags = Tags.Matches(reply);
        string? thrown = null;
        string? back = null;

        for (var i = 0; i < tags.Count; i++)
        {
            var start = tags[i].Index + tags[i].Length;
            var end = i + 1 < tags.Count ? tags[i + 1].Index : reply.Length;
            var text = reply[start..end].Trim();

            if (string.Equals(tags[i].Groups["tag"].Value, "throw", StringComparison.OrdinalIgnoreCase))
            {
                thrown ??= text;
            }
            else
            {
                back ??= text;
            }
        }

        return (thrown, back);
    }

    /// <summary>
    /// One of the model's lines, tidied - or null when it will not do and the
    /// built-in line should be said instead. Why it was turned down goes in
    /// <paramref name="notes"/>.
    /// </summary>
    /// <param name="mustName">Names the line has to say - blank ones are skipped.</param>
    internal static string? Checked(string? line, IEnumerable<string> mustName, List<string>? notes = null, string what = "line")
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            notes?.Add($"no {what} came back");
            return null;
        }

        var text = Brackets.Replace(line, " ");
        text = Whitespace.Replace(text, " ").Trim().Trim('"', '“', '”', '*', '_', ' ').Trim();

        if (text.Length == 0)
        {
            notes?.Add($"no {what} came back");
            return null;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (words > MaxWords)
        {
            notes?.Add($"the {what} ran to {words} words");
            return null;
        }

        foreach (var name in mustName)
        {
            var said = (name ?? string.Empty).Trim().TrimEnd('.', '!', '?', ',');

            if (said.Length > 0 && !Says(text, said))
            {
                notes?.Add($"the {what} left out \"{said}\"");
                return null;
            }
        }

        if (text[^1] is not ('.' or '!' or '?'))
        {
            text += ".";
        }

        return text;
    }

    /// <summary>
    /// The model's reply, split and checked: the throw must name the guest
    /// (when they have a name), and the hand back must name the host by
    /// first name (when they have one) - the whole point of it.
    /// </summary>
    internal static Written Check(string? reply, Brief brief, List<string>? notes = null)
    {
        var (thrown, back) = Split(reply);
        var guest = CommentaryService.Field(brief.Guest, CommentaryService.MaxShowNameChars);
        var host = CommentaryService.FirstName(brief.Host);

        return new Written(
            Checked(thrown, new[] { guest }, notes, "throw"),
            Checked(back, new[] { host }, notes, "hand back"));
    }

    /// <summary>
    /// The co-host's hand back to the race announcer after the pre-race word
    /// with a Team Principal - there was none before 26 Oct 2026, and the
    /// announcer simply carried on over the end of the interview. His first
    /// name, for the reason <see cref="RaceControlControl.FlagHandBack"/> gives.
    /// </summary>
    internal static string PitLaneBackBefore(string? announcerName, string? driver)
    {
        var who = CommentaryService.FirstName(announcerName);
        var team = (driver ?? string.Empty).Trim();
        var opening = team.Length > 0 ? $"Big race ahead for {team}'s team" : "Big race ahead for the team";

        return who.Length > 0
            ? $"{opening} - back to you, {who}."
            : $"{opening} - back up to the booth.";
    }

    /// <summary>
    /// The achievements reader handing back to the recap show's host once the
    /// read is done (26 Oct 2026) - the reader used to go quiet and the host
    /// thank them, as if the read had been a recording.
    /// </summary>
    internal static string HonoursBack(string? hostName)
    {
        var who = CommentaryService.FirstName(hostName);

        return who.Length > 0
            ? $"And those are tonight's achievements - back to you, {who}."
            : "And those are tonight's achievements - back to the show.";
    }

    /// <summary>
    /// The last whole sentences of what was just said, up to
    /// <paramref name="maxChars"/>, for the host to pick up from. A throw
    /// reacts to the END of what came before it - a cut from the front, which
    /// is what Field does, would hand the model the recap's opening instead.
    /// </summary>
    internal static string Tail(string? said, int maxChars)
    {
        var text = Whitespace.Replace(said ?? string.Empty, " ").Trim();

        if (text.Length <= maxChars)
        {
            return text;
        }

        var cut = text[^maxChars..];
        var start = cut.IndexOfAny(new[] { '.', '!', '?' });

        return start >= 0 && start + 1 < cut.Length ? cut[(start + 1)..].Trim() : cut.Trim();
    }

    private static bool Says(string text, string name) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
