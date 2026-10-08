using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// One worked-out fact, ready to be dropped into a prompt, with the key
/// that says what it is about.
///
/// The key is what stops the night repeating itself. "His first win here
/// since March" turning up in the intro, the recap and the sign-off is the
/// single fastest way to make a broadcast sound like a machine, and no
/// amount of prompt wording prevents it - the model is given the fact
/// three times and says it three times. So a fact is spent once and then
/// it is gone for the night. See SaidTonight.
/// </summary>
/// <param name="Key">Stable and specific: "pb:sam:2026-10-07-r4", not "pb".</param>
/// <param name="Text">One line, already cleaned, already worked out. The model phrases it; it never computes it.</param>
public sealed record FactBlock(string Key, string Text);

/// <summary>
/// What has already been said tonight.
///
/// Deliberately a set of keys and nothing else: it holds no race data, no
/// names, no numbers, so it cannot drift out of step with the stores it
/// would otherwise be mirroring. Everything that wants to know "have we
/// said this" asks with the same key it would file it under.
///
/// Survives a restart for the same reason the budget's spend does - a
/// laptop gets closed between the heats and the mains, and an announcer
/// that re-introduces every driver after the break is worse than one that
/// never introduced them.
/// </summary>
public sealed class SaidTonight
{
    private readonly HashSet<string> _spent = new(StringComparer.Ordinal);
    private string _dayId = string.Empty;

    /// <summary>How many facts have been used tonight. For the console and the tests.</summary>
    public int Count => _spent.Count;

    /// <summary>True when this exact fact has already been said tonight.</summary>
    public bool Spent(string key) => _spent.Contains(key);

    /// <summary>
    /// Marks facts as said - AFTER the line has actually been written and
    /// spoken, never before. A write that failed said nothing, and burning
    /// the club's best fact of the night on a request that timed out is
    /// exactly the sort of quiet loss nobody would ever trace.
    /// </summary>
    public void Mark(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                _spent.Add(key);
            }
        }
    }

    /// <summary>Marks one fact as said. See Mark(IEnumerable).</summary>
    public void Mark(string key) => Mark(new[] { key });

    /// <summary>
    /// True when every one of these facts has already been said - the
    /// question CommentaryBudget.NothingNewToSay exists to ask, so a
    /// segment with nothing left to say is dropped before it costs
    /// anything.
    /// </summary>
    public bool NothingLeftIn(IEnumerable<FactBlock> blocks)
    {
        var any = false;

        foreach (var block in blocks)
        {
            any = true;

            if (!Spent(block.Key))
            {
                return false;
            }
        }

        return any;
    }

    /// <summary>A new race day. Last week's facts are this week's news again.</summary>
    public void NewMeeting(string dayId)
    {
        if (string.Equals(_dayId, dayId, StringComparison.Ordinal))
        {
            return;
        }

        _dayId = dayId ?? string.Empty;
        _spent.Clear();
    }

    private sealed record Saved(string DayId, List<string>? Keys);

    /// <summary>Tonight's spent facts, for AppSettings to hold across a restart.</summary>
    public string Save() => JsonSerializer.Serialize(new Saved(_dayId, _spent.ToList()));

    /// <summary>
    /// Picks tonight's facts back up, but only when it is the same race
    /// day. Anything unreadable is a fresh night rather than a throw: a
    /// corrupt setting must not cost a club its announcer.
    /// </summary>
    public void Restore(string? saved, string dayId)
    {
        NewMeeting(dayId);

        if (string.IsNullOrWhiteSpace(saved))
        {
            return;
        }

        Saved? night;

        try
        {
            night = JsonSerializer.Deserialize<Saved>(saved);
        }
        catch (JsonException)
        {
            return;
        }

        if (night is null || !string.Equals(night.DayId, dayId, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var key in night.Keys ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                _spent.Add(key);
            }
        }
    }
}

/// <summary>
/// The facts a segment is about to be written from, and the keys it will
/// spend if the line actually happens.
/// </summary>
/// <param name="Text">The body of the prompt, inside the facts block. Empty when there was nothing to say.</param>
/// <param name="Keys">What to mark as said once the line has been written and spoken.</param>
/// <param name="Dropped">How many blocks were left out for want of room. For the console.</param>
public sealed record PackedFacts(string Text, IReadOnlyList<string> Keys, int Dropped)
{
    /// <summary>True when there is nothing left worth writing about.</summary>
    public bool Empty => Text.Length == 0;
}

/// <summary>
/// Turns a segment's candidate facts into the body of its prompt.
///
/// THE ORDER EVERY SEGMENT FOLLOWS
///
///   1. A detector fires, or a between-races moment arrives.
///   2. Build the candidate FactBlocks. Costs nothing - it is arithmetic
///      over stores this laptop already has open.
///   3. FactPack.Build. If it comes back Empty, stop here: every fact has
///      already been said and the segment has nothing to add.
///   4. CommentaryBudget.Reserve. If refused, stop - still free.
///   5. Write the line.
///   6. Written: Settle the ticket, then Mark the keys.
///      Failed, or too late to be worth saying: Release the ticket and
///      mark nothing.
///
/// Nothing is paid for before step 5, and nothing is spent before step 6.
/// Both halves matter: the budget must not lose a line to a request that
/// failed, and the night must not lose its best fact to one either.
/// </summary>
public static class FactPack
{
    /// <summary>
    /// How much of a prompt the facts may be.
    ///
    /// The relay refuses a prompt over eight thousand characters outright
    /// (MAX_PROMPT_CHARS on its side). This leaves room for the system
    /// prompt and the instruction under the facts, and the gap is
    /// deliberately generous: a segment that silently loses its last fact
    /// to a cap is worse than one that drops a block and says so.
    /// </summary>
    public const int MaxFactChars = 5_000;

    /// <summary>
    /// Assembles what is left to say, in the order given - so a builder
    /// puts its best fact first and that is the one that survives a
    /// squeeze.
    /// </summary>
    public static PackedFacts Build(IEnumerable<FactBlock> blocks, SaidTonight? said = null, int maxChars = MaxFactChars)
    {
        var text = new StringBuilder();
        var keys = new List<string>();
        var dropped = 0;

        foreach (var block in blocks)
        {
            if (block is null || string.IsNullOrWhiteSpace(block.Text))
            {
                continue;
            }

            if (said?.Spent(block.Key) == true)
            {
                continue;
            }

            // A fact offered twice in one pack - two builders that both
            // know about the same record - is one fact.
            if (keys.Contains(block.Key, StringComparer.Ordinal))
            {
                continue;
            }

            var line = Safe(block.Text);

            if (line.Length == 0)
            {
                continue;
            }

            if (text.Length + line.Length + 1 > maxChars)
            {
                dropped++;
                continue;
            }

            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(line);
            keys.Add(block.Key);
        }

        return new PackedFacts(text.ToString(), keys, dropped);
    }

    /// <summary>
    /// The backstop on what reaches a prompt.
    ///
    /// Every builder already cleans the typed text it uses through
    /// CommentaryService.Field, and this does not replace that - it is
    /// here so that a builder written later, by somebody who did not read
    /// that rule, still cannot put a bracket or a line break into a
    /// prompt. A fact that could close the facts block or fake an audio
    /// tag would be read out over the PA.
    /// </summary>
    private static string Safe(string text)
    {
        var cleaned = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if (ch is '[' or ']' or '{' or '}' or '<' or '>')
            {
                continue;
            }

            cleaned.Append(char.IsControl(ch) ? ' ' : ch);
        }

        // Several spaces, a stray tab, a line break mid-fact: one space.
        var squeezed = new StringBuilder(cleaned.Length);
        var lastWasSpace = false;

        foreach (var ch in cleaned.ToString())
        {
            var isSpace = ch == ' ';

            if (isSpace && lastWasSpace)
            {
                continue;
            }

            squeezed.Append(ch);
            lastWasSpace = isSpace;
        }

        return squeezed.ToString().Trim();
    }
}

/// <summary>
/// A round the programme has coming, as the "coming up next" segment needs
/// it. Deliberately not a RoundDefinition: the segment wants a label, a
/// class and how many cars, and handing it the store's own type would tie
/// a fact builder to the shape of the programme.
/// </summary>
/// <param name="Label">"Q3 Heat 2", "A-Main" - as the programme calls it.</param>
/// <param name="ClassName">Whose round it is.</param>
/// <param name="Entries">How many cars are due out in it.</param>
public readonly record struct UpcomingRound(string Label, string ClassName, int Entries);

/// <summary>
/// The fact builders shared by more than one segment.
///
/// Each one returns worked-out prose, cleaned through CommentaryService's
/// own Field - the same guard the rest of the prompts use, not a second
/// copy of it. Every name and label in here was typed by somebody, a
/// driver filling in their own profile at check-in included, and goes to
/// the model next to the instructions.
/// </summary>
public static class CommentaryFacts
{
    /// <summary>
    /// Cleaned to one short line, like every other piece of typed text in
    /// a prompt. Shared with the other fact builders rather than copied
    /// into each of them: it is the injection guard, and two copies of a
    /// guard are one guard and one liability.
    /// </summary>
    internal static string Clean(string? value) => CommentaryService.Field(value);

    /// <summary>A longer piece - a round label with a class in it - still on one line and still without brackets.</summary>
    internal static string CleanLong(string? value) => CommentaryService.Field(value, 60);

    /// <summary>
    /// What is coming up, for the gap between races.
    ///
    /// Takes the estimate rather than working one out: how long a club
    /// night actually runs against its schedule is WhereTimeGoes's
    /// question, and a fact builder that guessed at it would be inventing
    /// the one number an announcer gets held to.
    /// </summary>
    public static FactBlock? ComingUpNext(IReadOnlyList<UpcomingRound> next, TimeSpan? inAbout = null, string? raceKey = null)
    {
        if (next.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();

        foreach (var round in next.Take(3))
        {
            var label = CleanLong(round.Label);

            if (label.Length == 0)
            {
                continue;
            }

            var cls = Clean(round.ClassName);
            var who = cls.Length > 0 ? $"{label} ({cls})" : label;

            parts.Add(round.Entries > 0 ? $"{who}, {round.Entries} cars" : who);
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var when = inAbout is { TotalMinutes: >= 1 and < 120 }
            ? $" The next one is about {inAbout.Value.TotalMinutes:0} minutes away."
            : string.Empty;

        return new FactBlock(
            $"upnext:{raceKey ?? parts[0]}",
            $"Coming up: {string.Join("; then ", parts)}.{when}");
    }

    /// <summary>
    /// A battle worth talking about, as BattleWatch found it.
    ///
    /// The key carries the race, so the same two drivers fighting again in
    /// the main is a new story rather than a fact that was used up in
    /// their heat.
    /// </summary>
    public static FactBlock Battle(Battle battle, Func<string, string?> nameFor, string raceKey)
    {
        var ahead = Clean(nameFor(battle.AheadId));
        var behind = Clean(nameFor(battle.BehindId));

        if (ahead.Length == 0)
        {
            ahead = "the car ahead";
        }

        if (behind.Length == 0)
        {
            behind = "the car behind";
        }

        var where = battle.Position == 1 ? "the lead" : Ordinal(battle.Position);

        var text = new StringBuilder();

        text.Append($"{behind} has been within {battle.NowSeconds:0.00}s of {ahead} for {battle.For.TotalSeconds:0} seconds, fighting for {where}. ");
        text.Append($"Closest they have been: {battle.ClosestSeconds:0.00}s.");

        if (battle.LeadChanges == 1)
        {
            text.Append(" They have swapped places once already.");
        }
        else if (battle.LeadChanges > 1)
        {
            text.Append($" They have swapped places {battle.LeadChanges} times already.");
        }

        return new FactBlock($"battle:{raceKey}:{Pair(battle.AheadId, battle.BehindId)}", text.ToString());
    }

    /// <summary>A lap worth stopping for, as PaceWatch found it.</summary>
    public static FactBlock Pace(PaceNote note, Func<string, string?> nameFor, string raceKey)
    {
        var who = Clean(nameFor(note.DriverId));

        if (who.Length == 0)
        {
            who = "that car";
        }

        var what = note.Kind switch
        {
            PaceNoteKind.LayoutRecord => "the fastest lap ever set on this layout",
            PaceNoteKind.NightBest => "the fastest lap of the night so far",
            PaceNoteKind.SessionBest => "the fastest lap of the race so far",
            _ => "a personal best",
        };

        var text = new StringBuilder($"{who} has just set {what}: {note.LapSeconds:0.000}s.");

        if (note.Beaten is { } beaten && beaten > note.LapSeconds)
        {
            var holder = Clean(note.PreviousHolder);
            var by = beaten - note.LapSeconds;

            text.Append(holder.Length > 0 && !string.Equals(holder, who, StringComparison.OrdinalIgnoreCase)
                ? $" It takes {by:0.000}s off {holder}'s {beaten:0.000}s."
                : $" That is {by:0.000}s better than the previous {beaten:0.000}s.");
        }

        // The kind is in the key, not just the driver: a personal best and
        // then the fastest lap of the night from the same car in the same
        // race are two different things to say.
        return new FactBlock($"pace:{raceKey}:{note.Kind}:{note.DriverId}", text.ToString());
    }

    /// <summary>A pair of drivers as one key, whichever way round they currently are.</summary>
    internal static string Pair(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? $"{a}+{b}" : $"{b}+{a}";

    /// <summary>"2nd", "3rd" - for a position in a sentence.</summary>
    internal static string Ordinal(int position)
    {
        if (position is >= 11 and <= 13)
        {
            return $"{position}th";
        }

        return (position % 10) switch
        {
            1 => $"{position}st",
            2 => $"{position}nd",
            3 => $"{position}rd",
            _ => $"{position}th",
        };
    }
}
