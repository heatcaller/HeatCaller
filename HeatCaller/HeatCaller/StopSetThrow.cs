using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// The announcer's throw into the stop set (19 Oct 2026): the line that
/// takes the room into the commercial break after a race.
///
/// ASKED FOR: plug the show by name, say the race recap with its host is
/// coming up after the break, written by the AI and a little different
/// every time. It used to be one of the club's fixed teaser lines with
/// "This is Dave Marsh - back after the break." on the end - the same few
/// words race after race, and nothing about the show or who was coming up.
///
/// THE AI WRITES IT, THIS HOLDS IT TO THE BRIEF. The model is told the
/// show's name, the recap host's name and the announcer's own, word for
/// word (CommentaryService.BuildStopSetThrowAsync). A model asked to say a
/// name says it most of the time, so <see cref="Checked"/> makes sure: a
/// line missing any name that is set, that never says a break is coming,
/// that runs long, or that repeats one of the last few throws, is not used.
/// Then <see cref="Fallback"/> says it instead - written here, never asked
/// of the model, rotating through a handful of shapes so even the fallback
/// is not the same line twice running.
///
/// WITH OR WITHOUT A BREAK (19 Oct 2026). With no stop set to go into
/// (the commercial break off, or no Big Screen up with the sponsor slides)
/// it is the same line, written the same way, except that the recap is
/// "coming up next" rather than "after the break" - a night with no
/// sponsors, or a rehearsal at home, still hears the show plugged. A line
/// that promises a break when there is none is not used.
///
/// Pure apart from the short memory of recent throws, so every rule is a
/// table test.
/// </summary>
internal static class StopSetThrow
{
    /// <summary>The longest a throw may run. Two short sentences; a throw that runs on is a speech.</summary>
    internal const int MaxWords = 40;

    /// <summary>How many recent throws the model is shown, and a new one may not repeat.</summary>
    internal const int Remembered = 6;

    // WHY (20 Oct 2026): the prompt (CommentaryService.BuildStopSetThrowAsync)
    // told the model the recap was "coming up next, straight after this" on a
    // night with no break, and Checked then threw the line away because
    // "after this" reads as a break - so a model that did exactly as asked
    // was never heard, and the stock fallback ran all night. The prompt's
    // wording now lives here, beside the check, so the two can't drift apart
    // again; the tests hold that neither no-break phrase trips the check.

    /// <summary>When the recap is, as the prompt words it, with a stop set to go into.</summary>
    internal const string WhenBreak = "after the break";

    /// <summary>When the recap is, as the prompt words it, with no break tonight. Must not match <see cref="SaysBreak"/>.</summary>
    internal const string WhenNoBreak = "coming up next";

    /// <summary>The phrase the prompt uses to say when the recap is.</summary>
    internal static string When(bool breakFollows) => breakFollows ? WhenBreak : WhenNoBreak;

    /// <summary>True when the text promises a break - for the tests that hold the prompt's wording to the check.</summary>
    internal static bool PromisesBreak(string text) => SaysBreak.IsMatch(text);

    /// <summary>
    /// The names the throw says, already cleaned for the prompt
    /// (CommentaryService.Field), so the prompt and the check compare the
    /// same text. Blank for one the club has not set: it is then never
    /// said, and the model is told not to make one up.
    /// </summary>
    internal sealed record Names(string Show, string Host, string Announcer)
    {
        internal static Names Of(string? show, string? host, string? announcer) =>
            new(Clean(show), Clean(host), Clean(announcer));

        private static string Clean(string? name) => (name ?? string.Empty).Trim().TrimEnd('.', '!', '?', ',');
    }

    private static readonly object Gate = new();
    private static readonly Queue<string> RecentThrows = new();
    private static int _lastFallback = -1;

    /// <summary>The last few throws said, oldest first.</summary>
    internal static IReadOnlyList<string> Recent
    {
        get
        {
            lock (Gate)
            {
                return RecentThrows.ToArray();
            }
        }
    }

    /// <summary>Remembers a throw that went out, so the next is told not to say it again.</summary>
    internal static void Said(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (Gate)
        {
            RecentThrows.Enqueue(line.Trim());

            while (RecentThrows.Count > Remembered)
            {
                RecentThrows.Dequeue();
            }
        }
    }

    /// <summary>For the tests.</summary>
    internal static void ForgetRecent()
    {
        lock (Gate)
        {
            RecentThrows.Clear();
            _lastFallback = -1;
        }
    }

    /// <summary>
    /// What the recap is called on air: "the Friday Night Thunder race
    /// recap with Jess Cole", and shorter for each name that is blank.
    /// </summary>
    internal static string RecapPhrase(Names names) =>
        (names.Show.Length > 0, names.Host.Length > 0) switch
        {
            (true, true) => $"the {names.Show} race recap with {names.Host}",
            (true, false) => $"the {names.Show} race recap",
            (false, true) => $"the race recap with {names.Host}",
            _ => "the race recap",
        };

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>"after the break" in any of the ways a presenter says it - wanted when a break follows, and not when one doesn't.</summary>
    private static readonly Regex SaysBreak = new(
        @"\b(break|sponsors?|these messages|after this|right back|back in a (moment|minute|flash))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// A speaker label the model puts on the front - "ANNOUNCER:", "Host:".
    /// Only a role word or a name in capitals, so "Coming up: the recap"
    /// keeps its words.
    /// </summary>
    private static readonly Regex Label = new(@"^\s*(?:(?i:announcer|host)|[A-Z]{1,20}(?: [A-Z]{1,20})?)\s*:\s+", RegexOptions.Compiled);

    /// <summary>
    /// The model's throw, tidied, or null when it does not do the job and
    /// <see cref="Fallback"/> should be said instead. Why it was turned
    /// down goes in <paramref name="notes"/>.
    /// </summary>
    /// <param name="breakFollows">Whether the stop set runs after it. False: the line must not promise one.</param>
    internal static string? Checked(string? line, Names names, IReadOnlyList<string>? recent = null, List<string>? notes = null, bool breakFollows = true)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            notes?.Add("nothing came back");
            return null;
        }

        var text = Whitespace.Replace(line, " ").Trim().Trim('"', '“', '”');
        text = Label.Replace(text, string.Empty, 1).Trim().Trim('"', '“', '”').Trim();

        if (text.Length == 0)
        {
            notes?.Add("nothing came back");
            return null;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (words > MaxWords)
        {
            notes?.Add($"it ran to {words} words");
            return null;
        }

        foreach (var (name, what) in new[] { (names.Show, "the show's name"), (names.Host, "the recap host's name"), (names.Announcer, "the announcer's name") })
        {
            if (name.Length > 0 && !Says(text, name))
            {
                notes?.Add($"it left out {what}, \"{name}\"");
                return null;
            }
        }

        if (breakFollows && !SaysBreak.IsMatch(text))
        {
            notes?.Add("it never said a break was coming");
            return null;
        }

        if (!breakFollows && SaysBreak.IsMatch(text))
        {
            notes?.Add("it promised a break, and there isn't one tonight");
            return null;
        }

        if (recent is not null && recent.Any(r => Same(r, text)))
        {
            notes?.Add("it repeated a recent throw word for word");
            return null;
        }

        // Never his bare name as the last words (16 Oct 2026, see
        // AnnouncerIntro.IntoStopSet): the last word is the one a voice
        // clips, and a line that stops dead on a name sounds cut off.
        if (names.Announcer.Length > 0 && EndsOn(text, names.Announcer))
        {
            text = $"{text.TrimEnd('.', '!', '?', ',', ' ')} - stay with us.";
        }

        if (text[^1] is not ('.' or '!' or '?'))
        {
            text += ".";
        }

        return text;
    }

    /// <summary>
    /// The throw written here, for when the model's will not do - still
    /// naming the show, the recap host and the announcer, and not the same
    /// shape as last time.
    /// </summary>
    /// <param name="pick">Which shape, for the tests; null picks one at random, never the last one used.</param>
    /// <param name="breakFollows">False: "coming up next" instead of "after the break".</param>
    internal static string Fallback(Names names, int? pick = null, bool breakFollows = true)
    {
        int shape;

        lock (Gate)
        {
            if (pick is { } chosen)
            {
                shape = ((chosen % Shapes) + Shapes) % Shapes;
            }
            else
            {
                do
                {
                    shape = Random.Shared.Next(Shapes);
                }
                while (shape == _lastFallback);
            }

            _lastFallback = shape;
        }

        var recap = RecapPhrase(names);
        var who = names.Announcer;
        var named = who.Length > 0;

        if (!breakFollows)
        {
            return shape switch
            {
                0 => named
                    ? $"Don't go anywhere - {recap} is coming up next. This is {who}, stay with us."
                    : $"Don't go anywhere - {recap} is coming up next.",
                1 => named
                    ? $"Coming up next, it's {recap}. I'm {who} - stay right there."
                    : $"Coming up next, it's {recap}. Stay right there.",
                2 => named
                    ? $"Stay right where you are - {recap} is on its way. This is {who} - don't go anywhere."
                    : $"Stay right where you are - {recap} is on its way.",
                _ => named
                    ? $"Next up, it's {recap}. {who} here - stay with us."
                    : $"Next up, it's {recap}. Stay with us.",
            };
        }

        return shape switch
        {
            0 => named
                ? $"Don't go anywhere - {recap} is coming up after the break. This is {who}, stay with us."
                : $"Don't go anywhere - {recap} is coming up after the break.",
            1 => named
                ? $"Coming up after the break, it's {recap}. I'm {who} - back in a moment."
                : $"Coming up after the break, it's {recap}. Back in a moment.",
            2 => named
                ? $"Stay right there - after the break, {recap}. This is {who} - see you on the other side."
                : $"Stay right there - after the break, {recap}. See you on the other side.",
            _ => named
                ? $"We'll take a quick break, and then it's {recap}. {who} here - stay with us."
                : $"We'll take a quick break, and then it's {recap}. Stay with us.",
        };
    }

    private const int Shapes = 4;

    private static bool Says(string text, string name) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool EndsOn(string text, string name) =>
        text.TrimEnd('.', '!', '?', ',', ' ', '"', '”', '’', '\'').EndsWith(name, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string a, string b) => Bare(a) == Bare(b);

    private static string Bare(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
