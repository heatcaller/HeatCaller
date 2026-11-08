using System.Text;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// One driver's place in the achievements read: their name as it is read
/// out, and the badges they are read out for - cleaned and capped exactly as
/// RaceHonours.Read caps them, so the AI's read and the built-in one name the
/// same people for the same things.
/// </summary>
/// <param name="Driver">The name as it is read out - CommentaryFacts.Clean'd.</param>
/// <param name="Transponder">Whose they are, for the store's counts.</param>
/// <param name="Badges">The badge names read out for them, cleaned, in the evaluator's order.</param>
/// <param name="Unlocks">The unlocks behind <paramref name="Badges"/>, index for index.</param>
public sealed record HonoursPick(
    string Driver, string Transponder, IReadOnlyList<string> Badges, IReadOnlyList<AchievementUnlock> Unlocks);

/// <summary>
/// The achievements read written by the AI (7 Nov 2026), and the check that
/// holds it to who got what.
///
/// WHY (asked for by Asher: "let's give the achievement segment its due and
/// the same AI treatment as the other segments. full on"). Every other voice
/// in the recap show is written by the model from facts of its own - the
/// Technical Lead from the setup log, the Team Principal from his night and
/// what he said last time, the hand-overs from what was just said. The
/// achievements reader read a card: "Billy Bob picks up Nail-Biter. Jane Doe
/// takes Personal Best and Hat-Trick." Right, and the same shape every race
/// - a list read aloud by somebody who had no idea what a Nail-Biter is, or
/// that nobody at the club had ever earned one before tonight.
///
/// So the reader gets a well of their own, like the Principal: what each
/// badge is FOR (the catalogue's description), how hard it is (its tier),
/// how rare it is at this club (how many drivers hold it, and whether
/// anybody did before tonight), and where the driver is with them (their
/// first ever, or how many they have now). Read on the UI thread off the
/// store by <see cref="Facts"/>, handed to the model as finished sentences.
///
/// WHY RaceHonours' RULE STILL HOLDS. RaceHonours kept the model out of this
/// segment for one reason above all: a badge is a thing that happened to a
/// named person standing in the room, and a model handed "Billy Bob -
/// Nail-Biter" can give it to Jane. That is still the one unforgivable way to
/// get this wrong, so the model's read is not trusted - it is CHECKED
/// (<see cref="Check"/>): every driver named, every badge named, each badge
/// said only after the name of somebody who earned it, nobody else from the
/// race named, no number that is not in the facts. A read that fails any of
/// it is thrown away and the built-in read goes out instead - which is also
/// what goes out with the AI off, the allowance spent, or a model that is
/// late. The segment never depends on the model; it is only better for it.
///
/// Pure, so every rule is a table test.
/// </summary>
internal static class HonoursWriting
{
    /// <summary>Words the read may run to before the drivers - the opening and the closing thought.</summary>
    internal const int BaseWords = 20;

    /// <summary>
    /// And for each driver read out. "One or two sentences a driver" - with
    /// four drivers that is a hundred and twenty words, which is about as long
    /// as a third voice can hold a room between the recap and the colour.
    /// </summary>
    internal const int WordsPerDriver = 26;

    /// <summary>How long the read may run, for this many drivers.</summary>
    internal static int MaxWords(int drivers) => BaseWords + WordsPerDriver * Math.Max(1, drivers);

    /// <summary>The most of tonight's earlier reads the prompt carries, latest first - so a fifth race is not opened like the fourth.</summary>
    internal const int MostEarlierReads = 3;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Square-bracket tags or stage directions the model was told not to write.</summary>
    private static readonly Regex Brackets = new(@"\[[^\]]*\]|\([^)]*\)", RegexOptions.Compiled);

    /// <summary>A speaker's label in front of the read - "READER:", "**Achievements:**".</summary>
    private static readonly Regex Label = new(
        @"^[*_\s]*[\p{L} ']{2,40}[*_]*\s*:[*_]*\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>A number as the read might say it - "12", "4.586", "1,000".</summary>
    private static readonly Regex Number = new(@"\d+(?:[.,]\d+)*", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The hand back is a turn of its own (HandOff.HonoursBack, or the AI's),
    /// said straight after the read - a read that hands back as well is the
    /// reader saying it twice.
    /// </summary>
    private static readonly string[] HandsBack = { "back to you", "over to you", "back to the booth", "back to the show" };

    /// <summary>
    /// The read's facts: one block per driver, every badge with what it is
    /// for, how hard it is, how rare it is at this club, and where the driver
    /// is with them. ON THE UI THREAD - it reads the store.
    /// </summary>
    /// <param name="picks">Who is read out, for what - RaceHonours.Picked.</param>
    /// <param name="allUnlocks">
    /// Everything the race unlocked, not only what the read names: how many
    /// drivers earned a badge TONIGHT, and how many a driver earned in this
    /// race, are counted over all of it - a badge capped out of the read was
    /// still earned.
    /// </param>
    /// <param name="store">The achievements as they stand, with this race's already awarded. Null: no counts.</param>
    internal static IReadOnlyList<string> Facts(
        IReadOnlyList<HonoursPick> picks, IReadOnlyList<AchievementUnlock> allUnlocks, AchievementStore? store)
    {
        var lines = new List<string>();
        var held = store?.All ?? Array.Empty<DriverAchievements>();

        foreach (var pick in picks)
        {
            var block = new StringBuilder();
            block.Append($"{pick.Driver} earned {(pick.Badges.Count == 1 ? "one achievement" : $"{Words(pick.Badges.Count)} achievements")} in this race:");

            for (var i = 0; i < pick.Badges.Count && i < pick.Unlocks.Count; i++)
            {
                var unlock = pick.Unlocks[i];
                var badge = unlock.Badge;
                var what = CommentaryService.Field(badge.Description, 200).TrimEnd('.');
                var detail = CommentaryService.Field(unlock.Detail, 60);

                block.Append($"\n- {pick.Badges[i]}");
                block.Append(what.Length > 0 ? $": {what}." : ".");
                block.Append($" {TierWords(badge.Tier)} {CategoryWords(badge.Category)} achievement.");

                if (detail.Length > 0)
                {
                    block.Append($" What it was earned with: {detail}.");
                }

                // RARITY AT THIS CLUB. The holders are counted with this
                // race's awards in, and tonight's own winners are taken back
                // out to say whether anybody had it BEFORE - "the first at the
                // club" is the line a room looks up for, and the one that must
                // not be said of a badge half the club already has.
                var holders = held.Count(d => d.Has(badge.Id));
                var tonight = allUnlocks
                    .Where(u => string.Equals(u.Badge.Id, badge.Id, StringComparison.Ordinal))
                    .Select(u => u.Transponder)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                if (store is not null && holders > 0)
                {
                    block.Append(holders - tonight <= 0
                        ? " Nobody at the club had earned it before tonight."
                        : $" {Words(holders)} drivers at the club hold it now.");
                }
            }

            if (store is not null
                && held.FirstOrDefault(d => string.Equals(d.Transponder, pick.Transponder, StringComparison.OrdinalIgnoreCase))
                    is { } theirs)
            {
                var all = theirs.Unlocked.Count;
                var now = allUnlocks.Count(u => string.Equals(u.Transponder, pick.Transponder, StringComparison.OrdinalIgnoreCase));

                if (all > 0 && all <= now)
                {
                    block.Append($"\n{(now == 1 ? $"This is {pick.Driver}'s first achievement ever." : $"These are {pick.Driver}'s first achievements ever.")}");
                }
                else if (all > now)
                {
                    block.Append($"\n{pick.Driver} now has {Words(all)} achievements in all.");
                }
            }

            lines.Add(block.ToString());
        }

        return lines;
    }

    /// <summary>
    /// The model's read, tidied - or null when it will not do and the
    /// built-in read should go out instead. Why it was turned down goes in
    /// <paramref name="notes"/>.
    ///
    /// HOW "WHO GOT WHAT" IS CHECKED. The read is walked as a run of names:
    /// the drivers' and the badges', in the order they are said. The drivers
    /// named since the last badge are who the next badge is being given to -
    /// "Jane and Bob both take Weekend Warrior" gives it to both, and "Jane
    /// takes Hat-Trick and adds Clean Sweep" gives both to Jane. Every badge
    /// must go only to drivers who earned it, and every driver must be given
    /// every badge the read is for. A badge said before anybody's name, or
    /// given to somebody who did not earn it, is the read turned down.
    /// </summary>
    /// <param name="others">Everybody else in the race, who must not be named in a read they earned nothing in.</param>
    /// <param name="facts">The facts the model was given - any number in the read has to be in here.</param>
    internal static string? Check(
        string? reply, IReadOnlyList<HonoursPick> picks, IEnumerable<string>? others, string facts, List<string>? notes = null)
    {
        if (string.IsNullOrWhiteSpace(reply) || picks.Count == 0)
        {
            notes?.Add("no read came back");
            return null;
        }

        var text = Brackets.Replace(reply, " ");
        text = Whitespace.Replace(text, " ").Trim();
        text = Label.Replace(text, string.Empty, 1);
        text = text.Trim().Trim('"', '“', '”', '*', '_', ' ').Trim();

        if (text.Length == 0)
        {
            notes?.Add("no read came back");
            return null;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (words > MaxWords(picks.Count))
        {
            notes?.Add($"it ran to {words} words");
            return null;
        }

        foreach (var phrase in HandsBack)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                notes?.Add($"it handed back itself (\"{phrase}\") - the hand back is its own line");
                return null;
            }
        }

        foreach (Match number in Number.Matches(text))
        {
            if (!Regex.IsMatch(facts, $@"(?<![\d.,]){Regex.Escape(number.Value)}(?![\d])"))
            {
                notes?.Add($"it said \"{number.Value}\", which is not in the facts");
                return null;
            }
        }

        // Every name the read could say, normalised the same way the read
        // is: lower case, and anything that is not a letter or a digit a
        // space - so "Nail-Biter" and "nail biter" are one badge, and the TTS
        // says either the same.
        var mentions = new List<(int At, int Length, string Kind, string Key)>();
        var said = Normalise(text);

        void Find(string name, string kind, string key)
        {
            var needle = Normalise(name);

            if (needle.Trim().Length == 0)
            {
                return;
            }

            for (var at = said.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = said.IndexOf(needle, at + 1, StringComparison.Ordinal))
            {
                // The words only, without the padding either side: two names
                // said back to back - "Billy Bob, Nail-Biter" - share the
                // space between them, and are not one overlapping the other.
                mentions.Add((at + 1, needle.Length - 2, kind, key));
            }
        }

        var drivers = picks.Select(p => p.Driver).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var badges = picks.SelectMany(p => p.Badges).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var driver in drivers)
        {
            Find(driver, "driver", driver);
        }

        foreach (var badge in badges)
        {
            Find(badge, "badge", badge);
        }

        foreach (var other in (others ?? Array.Empty<string>())
                     .Select(CommentaryFacts.Clean)
                     .Where(o => o.Length > 0 && !drivers.Contains(o))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Find(other, "other", other);
        }

        // Longest first where two overlap - a badge called "Jane's Cup" is the
        // badge, not Jane - then in the order they are said.
        var walk = new List<(int At, int Length, string Kind, string Key)>();

        foreach (var mention in mentions.OrderByDescending(m => m.Length).ThenBy(m => m.At))
        {
            if (!walk.Any(w => mention.At < w.At + w.Length && w.At < mention.At + mention.Length))
            {
                walk.Add(mention);
            }
        }

        var holds = picks.ToDictionary(p => p.Driver, p => p.Badges.ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var given = new HashSet<(string, string)>();
        var subject = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastWasBadge = false;

        foreach (var (_, _, kind, key) in walk.OrderBy(w => w.At))
        {
            switch (kind)
            {
                case "other":
                    notes?.Add($"it named {key}, who is not in the read");
                    return null;

                case "driver":
                    if (lastWasBadge)
                    {
                        subject.Clear();
                    }

                    subject.Add(key);
                    named.Add(key);
                    lastWasBadge = false;
                    break;

                default:
                    if (subject.Count == 0)
                    {
                        notes?.Add($"it said \"{key}\" before saying whose it was");
                        return null;
                    }

                    foreach (var who in subject)
                    {
                        if (!holds[who].Contains(key))
                        {
                            notes?.Add($"it gave \"{key}\" to {who}, who did not earn it");
                            return null;
                        }

                        given.Add((who.ToLowerInvariant(), key.ToLowerInvariant()));
                    }

                    lastWasBadge = true;
                    break;
            }
        }

        foreach (var pick in picks)
        {
            if (!named.Contains(pick.Driver))
            {
                notes?.Add($"it left out {pick.Driver}");
                return null;
            }

            foreach (var badge in pick.Badges)
            {
                if (!given.Contains((pick.Driver.ToLowerInvariant(), badge.ToLowerInvariant())))
                {
                    notes?.Add($"it did not give {pick.Driver} \"{badge}\"");
                    return null;
                }
            }
        }

        if (text[^1] is not ('.' or '!' or '?'))
        {
            text += ".";
        }

        return text;
    }

    /// <summary>" nail biter " for "Nail-Biter" - padded, so a match is always whole words.</summary>
    private static string Normalise(string text)
    {
        var sb = new StringBuilder(text.Length + 2).Append(' ');
        var space = true;

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (c is '\'' or '’')
            {
                // "Jane's" is still Jane: the apostrophe goes and the "s"
                // becomes its own word, so " jane " is found in " jane s ".
                if (!space)
                {
                    sb.Append(' ');
                    space = true;
                }
            }
            else if (!space)
            {
                sb.Append(' ');
                space = true;
            }
        }

        if (!space)
        {
            sb.Append(' ');
        }

        return sb.ToString();
    }

    /// <summary>"Two", "eleven", "23" - a count as the facts give it. Words up to twelve, so the read says them as a person would.</summary>
    internal static string Words(int n) => n switch
    {
        1 => "one",
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        7 => "seven",
        8 => "eight",
        9 => "nine",
        10 => "ten",
        11 => "eleven",
        12 => "twelve",
        _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static string TierWords(AchievementTier tier) => tier switch
    {
        AchievementTier.Legend => "A legend-tier - the rarest kind -",
        AchievementTier.Gold => "A gold-tier - hard to come by -",
        AchievementTier.Silver => "A silver-tier",
        _ => "A bronze-tier",
    };

    private static string CategoryWords(AchievementCategory category) => category switch
    {
        AchievementCategory.Milestone => "career milestone",
        AchievementCategory.Progress => "personal progress",
        AchievementCategory.Rivalry => "head-to-head",
        _ => "single-race",
    };
}
