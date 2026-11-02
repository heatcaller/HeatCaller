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
/// <param name="Theme">
/// What the fact is ABOUT, as a tag - see <see cref="FactTheme"/> - or
/// null.
///
/// WHY A KEY IS NOT ENOUGH
///
/// The key stops the SAME fact being said twice. Nothing stopped three
/// DIFFERENT facts about the same driver's season going out in the form
/// guide, the race intro and the recap, each of them inside its own
/// budget - and that is precisely what the club owner heard on 10 Oct
/// 2026 as "a lot of talking but just say much of the same thing". The
/// budget counts segments; only a theme can notice what they were about.
///
/// WHY A NULL THEME MEANS SOMETHING
///
/// A theme is a PERMISSION, not a label. Tagging a block says this fact
/// is colour, and FactPack may leave it out for variety's sake when it
/// has something fresher to carry. The fact a segment is ABOUT carries no
/// theme and is never held back: the winner's first ever win here IS the
/// recap rather than decoration on it, and a variety rule that could drop
/// that line would be a worse fault than the repetition it is fixing.
/// </param>
public sealed record FactBlock(string Key, string Text, string? Theme = null);

/// <summary>
/// The tags a <see cref="FactBlock.Theme"/> is written with.
///
/// STRINGS RATHER THAN AN ENUM, AND ONE DRIVER RATHER THAN "A DRIVER"
///
/// The repetition that is actually heard from the floor is three segments
/// about the same PERSON, not three segments that happen both to be about
/// drivers. An enum of categories would cool every driver on the grid the
/// moment one of them was mentioned, which would silence the nine
/// segments that had something new to say in order to stop the one that
/// did not. "driver:1234" cools that one driver and leaves the rest of
/// the grid free.
///
/// Built here rather than concatenated at each call site so the prefixes
/// cannot drift: two spellings of the same theme is a cooldown that never
/// fires, and nothing would ever say so.
///
/// Five of them, because five are used. A theme nothing tags is a theme
/// nothing cools, so one goes in here when the facts that are about it are
/// written and not before - which is why the venue and the field arrived
/// with CommentaryRaceFacts on 11 Oct 2026 and not with the first three.
///
/// AND WHY THERE IS STILL NO "car". The car facts in
/// CommentaryRaceFacts are tagged <see cref="Driver"/>, because "the car
/// Sam raced is the club's own" is a line about Sam: cooled under a car
/// theme of its own it would collide with nothing, since nothing else in
/// the app is tagged with one, and a theme that can only ever meet itself
/// is a cooldown that never fires. Tagged on the driver it does the job
/// the theme exists for - it stops the recap landing on somebody the
/// night has just talked about.
///
/// AND WHY THE NIGHT'S THREAD HAS NO TAG OF ITS OWN EITHER (11 Oct 2026),
/// which is the same reason read the other way round. A thread line -
/// "Dave has won the last race of the night at each of the club's last
/// three race days" - is tagged <see cref="Driver"/> on its subject,
/// because the thing it has to cool is everything ELSE about that driver:
/// the room has just been told about Dave's run, and the next segment
/// landing on Dave's season as well is precisely the complaint this tag
/// was added for. A "thread:" tag would collide with nothing, since
/// nothing else in the app would ever carry one, and a theme that can only
/// ever meet itself is a cooldown that never fires.
///
/// Note that only the MID-EVENING thread is tagged at all. At the cold
/// open and the sign-off the thread is the segment's own subject, and the
/// fact a segment is ABOUT is never held back for variety's sake - see
/// FactBlock.Theme. So the same storyline carries a theme when it rides
/// along in a race intro and none when it is the point of the segment,
/// which is the distinction that class summary draws rather than an
/// inconsistency. See CommentaryShowFacts.ThreadSoFar.
/// </summary>
public static class FactTheme
{
    /// <summary>One person - the theme that does nearly all the work. See the class summary.</summary>
    public static string Driver(string? driverId) =>
        string.IsNullOrWhiteSpace(driverId) ? "driver" : "driver:" + driverId.Trim();

    /// <summary>
    /// Two people against each other - a head-to-head, a fight on track.
    ///
    /// Through Pair, so the theme is the same whichever way round the
    /// couple is handed over. A rivalry cooled under "A+B" and offered
    /// back as "B+A" is a cooldown that never fires.
    /// </summary>
    public static string Rivalry(string a, string b) => "rivalry:" + CommentaryFacts.Pair(a, b);

    /// <summary>The club's own past - a race day run on this date in another year.</summary>
    public static string PastDay(int yearsAgo) =>
        "pastday:" + yearsAgo.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// One track plan - the subject of a venue record.
    ///
    /// The LAYOUT and not the driver who holds the record, which is the
    /// whole reason this tag exists. "The outright record on this layout,
    /// set by Billy in March" is a line about the track; cooled under
    /// Billy it would silence the venue for a night and a half because a
    /// form guide happened to mention him, and would leave two lines about
    /// the same track free to go out back to back. See
    /// CommentaryRaceFacts.LayoutRecord.
    ///
    /// Trimmed and lower-cased because a layout is found by its NAME
    /// everywhere in this app - the Race Control picker lists names, and
    /// TrackLayoutStore.IsNameTaken compares them without regard to case -
    /// so "Club Oval" and "club oval" are one track and must be one theme.
    /// Two spellings of a theme is a cooldown that never fires, and
    /// nothing would ever say so.
    /// </summary>
    public static string Venue(string? layoutName) =>
        string.IsNullOrWhiteSpace(layoutName)
            ? "venue"
            : "venue:" + layoutName.Trim().ToLowerInvariant();

    /// <summary>
    /// One class's field - the subject of how big an entry is and how many
    /// of it have won.
    ///
    /// ToLowerInvariant rather than the current culture: on a Turkish PC
    /// ToLower turns an I into a dotless one, so "Indoor" and "indoor"
    /// would be two themes there and one everywhere else - a cooldown that
    /// fires on the developer's machine and not in the clubhouse.
    /// </summary>
    public static string Field(string? className) =>
        string.IsNullOrWhiteSpace(className)
            ? "field"
            : "field:" + className.Trim().ToLowerInvariant();
}

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

    /// <summary>
    /// How many spoken segments a theme stays cool for. See
    /// <see cref="ThemeCooled"/>.
    ///
    /// Two, and deliberately short: this is a variety rule, not a ban. At
    /// the airtime cap two segments is about a minute and a half, which is
    /// long enough that nobody in the room hears the same subject twice in
    /// a row. Longer and a ten-driver grid runs out of fresh themes before
    /// the heats are done, and segments with something true to say start
    /// saying nothing - which is worse than saying it twice.
    /// </summary>
    public const int ThemeCooldown = 2;

    /// <summary>Which spoken segment each theme was last carried in. See ThemeCooled.</summary>
    private readonly Dictionary<string, int> _themeAt = new(StringComparer.Ordinal);

    /// <summary>How many packs have actually been SPOKEN tonight - the clock the cooldown is measured on.</summary>
    private int _spokenPacks;

    /// <summary>
    /// The one lock in this class.
    ///
    /// WHY IT IS NEEDED NOW AND WAS NOT BEFORE (10 Oct 2026)
    ///
    /// This had exactly one user: CommentaryDirector's own pass, which
    /// gathers a segment's facts and then marks them, one segment at a
    /// time. On a thread-pool thread from the second segment onwards, but
    /// never two at once.
    ///
    /// The race recap and the race intro now share it - see
    /// CommentaryService.HistorySaid - and each runs on a sequence of its
    /// own: the recap's follow-on task and the intro's staging job in
    /// RaceControlControl, neither of which knows anything about the
    /// director's half-second tick. Two threads adding to a HashSet at
    /// once do not merely lose an entry; they can leave it corrupt and
    /// throw from an unrelated read minutes later, which on a race night
    /// surfaces as the announcer going quiet with a stack trace nobody can
    /// place.
    ///
    /// A lock rather than concurrent collections because every method here
    /// is a handful of hash lookups: the contention is not measurable and
    /// the class stays readable, which for the thing that decides whether
    /// the night repeats itself matters more.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>How many facts have been used tonight. For the console and the tests.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _spent.Count;
            }
        }
    }

    /// <summary>
    /// True when this theme went out in one of the last
    /// <see cref="ThemeCooldown"/> spoken segments, so a pack with
    /// anything fresher should carry that instead.
    ///
    /// COUNTED IN SEGMENTS SPOKEN, NOT IN MINUTES. That is what the room
    /// actually hears - three segments in ninety seconds and three in ten
    /// minutes are the same amount of talking about one driver as far as a
    /// crowd is concerned - and it keeps the promise this class's summary
    /// makes: it holds keys and nothing else, no clock and no race data to
    /// drift out of step with.
    ///
    /// An untagged fact is never cool. See FactBlock.Theme for why a null
    /// theme is a promise rather than an omission.
    /// </summary>
    public bool ThemeCooled(string? theme)
    {
        if (string.IsNullOrEmpty(theme))
        {
            return false;
        }

        lock (_gate)
        {
            return _themeAt.TryGetValue(theme, out var at) && _spokenPacks - at < ThemeCooldown;
        }
    }

    /// <summary>True when this exact fact has already been said tonight.</summary>
    public bool Spent(string key)
    {
        lock (_gate)
        {
            return _spent.Contains(key);
        }
    }

    /// <summary>
    /// Marks facts as said - AFTER the line has actually been written and
    /// spoken, never before. A write that failed said nothing, and burning
    /// the club's best fact of the night on a request that timed out is
    /// exactly the sort of quiet loss nobody would ever trace.
    /// </summary>
    public void Mark(IEnumerable<string> keys)
    {
        lock (_gate)
        {
            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _spent.Add(key);
                }
            }
        }
    }

    /// <summary>Marks one fact as said. See Mark(IEnumerable).</summary>
    public void Mark(string key) => Mark(new[] { key });

    /// <summary>
    /// Marks a whole pack as said - its keys AND its themes, together,
    /// and after the line has actually been written and spoken.
    ///
    /// One call rather than two because the two must not come apart. A
    /// caller that marked the keys and forgot the themes would get back
    /// exactly the repetition the theme exists to stop, and would get it
    /// silently - there is nothing to see in a log when a variety rule
    /// quietly never fires. The theme clock moves ONLY here, which is also
    /// what makes a written-and-dropped segment cost the night neither its
    /// facts nor its variety.
    /// </summary>
    public void Mark(PackedFacts? pack)
    {
        if (pack is null || pack.Empty)
        {
            return;
        }

        // One lock for the keys and the themes together, so no other
        // thread can see a pack half spent - the point of them being one
        // call in the first place. Reentrant, so Mark(keys) below takes it
        // again harmlessly.
        lock (_gate)
        {
            Mark(pack.Keys);

            _spokenPacks++;

            foreach (var theme in pack.Themes)
            {
                if (!string.IsNullOrEmpty(theme))
                {
                    _themeAt[theme] = _spokenPacks;
                }
            }
        }
    }

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
        lock (_gate)
        {
            if (string.Equals(_dayId, dayId, StringComparison.Ordinal))
            {
                return;
            }

            _dayId = dayId ?? string.Empty;
            _spent.Clear();

            // The theme clock with them: last week's subjects are this
            // week's again, exactly as last week's facts are.
            _themeAt.Clear();
            _spokenPacks = 0;
        }
    }

    private sealed record Saved(string DayId, List<string>? Keys);

    /// <summary>Tonight's spent facts, for AppSettings to hold across a restart.</summary>
    public string Save()
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(new Saved(_dayId, _spent.ToList()));
        }
    }

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

        // Through Mark, so the one lock covers this too. The themes are
        // deliberately NOT restored: the cooldown is about what the room
        // heard in the last minute and a half, and a laptop that has been
        // closed and reopened has had a longer gap than that by definition.
        Mark(night.Keys ?? new List<string>());
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

    /// <summary>
    /// What this pack is ABOUT, for the cooldown to mark once the line has
    /// been spoken - see SaidTonight.Mark(PackedFacts).
    ///
    /// Empty when every fact in it is the segment's own subject rather
    /// than colour, which is the usual answer for a battle call or a
    /// record lap. See FactBlock.Theme.
    ///
    /// An init property rather than a fourth positional field so that
    /// every PackedFacts already built anywhere in the app - and in the
    /// tests - still compiles and still means what it meant.
    /// </summary>
    public IReadOnlyList<string> Themes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// How many blocks were left out because the night had just talked
    /// about them. NOT the same as Dropped, which is for want of room -
    /// kept apart so a thin segment can be accounted for in the console
    /// rather than looking like a cap that bit.
    /// </summary>
    public int HeldBack { get; init; }
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
    ///
    /// AND PREFERS A SUBJECT THE NIGHT HAS NOT JUST TALKED ABOUT
    ///
    /// Two passes, and the second one is the whole of the variety rule
    /// added on 10 Oct 2026. A block whose theme went out in the last
    /// couple of spoken segments is held aside, and is used only if
    /// nothing fresher survived - because a segment that went silent for
    /// having only one true fact, about a driver mentioned a minute ago,
    /// is a worse fault than saying it twice. So this can thin a pack and
    /// can never empty one.
    ///
    /// Here rather than in each builder on purpose: it is the one place
    /// every segment's facts already pass through, so the eight segments
    /// that worked before this got the benefit without being touched.
    /// </summary>
    public static PackedFacts Build(IEnumerable<FactBlock> blocks, SaidTonight? said = null, int maxChars = MaxFactChars)
    {
        var fresh = new List<FactBlock>();
        var cooled = new List<FactBlock>();

        // A fact offered twice in one pack - two builders that both know
        // about the same record - is one fact. Held as a set rather than
        // searched for in the packed keys, because a block can now be set
        // aside instead of packed and a duplicate of it must still go.
        var seen = new HashSet<string>(StringComparer.Ordinal);

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

            if (!seen.Add(block.Key))
            {
                continue;
            }

            if (said?.ThemeCooled(block.Theme) == true)
            {
                cooled.Add(block);
            }
            else
            {
                fresh.Add(block);
            }
        }

        var packed = Lay(fresh, maxChars);

        if (!packed.Empty || cooled.Count == 0)
        {
            return packed with { HeldBack = cooled.Count };
        }

        // Nothing fresh left to say. What the night has just talked about
        // beats silence, every time - see the remarks.
        return Lay(cooled, maxChars);
    }

    /// <summary>
    /// Lays blocks into the body of a prompt in the order given, stopping
    /// at the character cap - so a builder's best fact first is the one
    /// that survives a squeeze.
    /// </summary>
    private static PackedFacts Lay(List<FactBlock> blocks, int maxChars)
    {
        var text = new StringBuilder();
        var keys = new List<string>();
        var themes = new List<string>();
        var dropped = 0;

        foreach (var block in blocks)
        {
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

            // Only the themes of the lines that actually went in. A theme
            // marked for a block the cap dropped would cool a subject the
            // room never heard about.
            if (!string.IsNullOrEmpty(block.Theme) && !themes.Contains(block.Theme, StringComparer.Ordinal))
            {
                themes.Add(block.Theme);
            }
        }

        return new PackedFacts(text.ToString(), keys, dropped) { Themes = themes };
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
    /// A lap time, a gap or a margin written with a decimal POINT whatever
    /// the PC's region.
    ///
    /// The same guard the recap has had since it was written, and for the
    /// same reason: these numbers go into the prompt, the model repeats
    /// them, and the line is spoken - where on a comma-decimal PC (German,
    /// French, Dutch, Spanish, all common at a club) "12,345s" escapes the
    /// spoken-time rewrite, which looks for a point, and is read out as
    /// "twelve thousand three hundred forty-five seconds". The whole newer
    /// fact layer formatted on the current culture and had the bug back.
    /// A copy rather than a shared call because the service's own one is
    /// private to it.
    /// </summary>
    internal static string Invariant(double value, string format) =>
        value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

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
            ? $" The next one is about {Invariant(inAbout.Value.TotalMinutes, "0")} minutes away."
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

        text.Append($"{behind} has been within {Invariant(battle.NowSeconds, "0.00")}s of {ahead} for {Invariant(battle.For.TotalSeconds, "0")} seconds, fighting for {where}. ");
        text.Append($"Closest they have been: {Invariant(battle.ClosestSeconds, "0.00")}s.");

        if (battle.LeadChanges == 1)
        {
            text.Append(" They have swapped places once already.");
        }
        else if (battle.LeadChanges > 1)
        {
            text.Append($" They have swapped places {battle.LeadChanges} times already.");
        }

        // Themed on the pair, so a form guide or an intro that was about
        // to talk about these two again reaches for somebody else first -
        // see FactBlock.Theme. A battle call is never held back itself:
        // this is the only fact in its pack, and the variety rule hands a
        // pack back rather than empty it.
        return new FactBlock(
            $"battle:{raceKey}:{Pair(battle.AheadId, battle.BehindId)}",
            text.ToString(),
            FactTheme.Rivalry(battle.AheadId, battle.BehindId));
    }

    /// <summary>
    /// A gap and where it is going, as RaceHorizon read it - the facts the
    /// Horizon segment is written from.
    ///
    /// ARITHMETIC AND NOTHING ELSE, which is RaceHorizon's rule carried
    /// into the words the model is handed. The gap, the rate per lap, the
    /// laps left and what that rate over those laps adds up to - and, for a
    /// gap the laps would cover, that it would cover it IF THE RATE HOLDS,
    /// which is the sum stated rather than a claim made. Nothing here says
    /// who will win, and the instruction for the segment says not to.
    ///
    /// WHAT IS LEFT IS GIVEN IN THE RACE'S OWN TERMS (1 Nov 2026). A race
    /// the clock ends is handed the time left and told it is timed; it
    /// used to be handed "About four laps left, going by the clock", and
    /// the model said "four circuits to run" in a race with no lap count.
    /// Only a race a lap count ends is handed laps. See RaceHorizon.Remaining.
    ///
    /// Keyed on the race and the pair, like a battle, and THEMED on the
    /// pair - which is what makes it a promise: NightPromises reads the
    /// theme back, and the pay-off is whatever the race then did to these
    /// two. See NightPromises.PayOff for the one case where that pay-off
    /// may come from the same race.
    /// </summary>
    public static FactBlock Horizon(Horizon horizon, Func<string, string?> nameFor, string raceKey)
    {
        var ahead = Clean(nameFor(horizon.AheadId));
        var behind = Clean(nameFor(horizon.BehindId));

        if (ahead.Length == 0)
        {
            ahead = "the car ahead";
        }

        if (behind.Length == 0)
        {
            behind = "the car behind";
        }

        var where = horizon.Position == 1 ? "the lead" : Ordinal(horizon.Position);
        var rate = Math.Abs(horizon.PerLap);
        var laps = RoadFact(horizon);
        var sum = horizon.Estimated ? "what is left of the race adds up" : "the laps left add up";

        var text = new StringBuilder();

        text.Append($"{behind} is {Invariant(horizon.GapSeconds, "0.000")}s behind {ahead}, fighting for {where}. ");

        switch (horizon.Kind)
        {
            case HorizonKind.WithinReach:
                text.Append($"The gap is coming down by {Invariant(rate, "0.00")}s a lap. {laps} ");
                text.Append($"At that rate {sum} to {Invariant(horizon.Buys, "0.00")}s - enough to cover the gap, if the rate holds.");
                break;

            case HorizonKind.Closing:
                text.Append($"The gap is coming down by {Invariant(rate, "0.00")}s a lap. {laps} ");
                text.Append($"At that rate {sum} to {Invariant(horizon.Buys, "0.00")}s - not yet enough to cover it.");
                break;

            case HorizonKind.SlippingAway:
                text.Append($"The gap is going out by {Invariant(rate, "0.00")}s a lap. {laps}");
                break;

            default:
                text.Append(laps);
                break;
        }

        return new FactBlock(
            $"horizon:{raceKey}:{Pair(horizon.AheadId, horizon.BehindId)}:{horizon.Kind}",
            text.ToString().TrimEnd(),
            FactTheme.Rivalry(horizon.AheadId, horizon.BehindId));
    }

    /// <summary>
    /// How much of the race is left, as a fact - in time for a race the
    /// clock ends, in laps for one a lap count ends, and saying which, so
    /// the model has no reason to say it the other way (1 Nov 2026).
    /// </summary>
    internal static string RoadFact(Horizon horizon)
    {
        if (!horizon.Estimated)
        {
            return $"This race is run to a lap count: {Plural.Of(horizon.LapsLeft, "lap")} left. Say what is left in laps.";
        }

        const string timedRule = "Say what is left as time, never as a count of laps.";

        if (horizon.TimeLeft is not { } left)
        {
            return $"This is a timed race - the clock ends it, not a lap count. {timedRule}";
        }

        if (left <= TimeSpan.Zero)
        {
            return "This is a timed race and the clock has run out: this is the final lap, and the leader takes the flag "
                   + "next time across the line. Do not give a number of laps.";
        }

        var clock = $"{(int)left.TotalMinutes}:{left.Seconds:00}";

        return $"This is a timed race - the clock ends it, not a lap count. About {clock} left on the clock "
               + $"({RaceNumbers.TimeLeftSpoken(left)}). {timedRule}";
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

        var text = new StringBuilder($"{who} has just set {what}: {Invariant(note.LapSeconds, "0.000")}s.");

        if (note.Beaten is { } beaten && beaten > note.LapSeconds)
        {
            // WHY (20 Oct 2026): when the mark being beaten was set in THIS
            // race, PaceWatch hands over the holder's driver ID - a
            // transponder number - not a name, and that now happens for the
            // night's best and the layout record too, not just the race's
            // fastest lap. Read raw, the announcer said "takes 0.2s off
            // 1234567's 10.5s". The holder goes through the same name lookup
            // as the driver; a saved holder is already a name and the lookup
            // hands it back as it is.
            //
            // KEPT AS WRITTEN WHEN THE LOOKUP SAYS IT IS THIS DRIVER (20 Oct
            // 2026, final review). A lookup that turns somebody ELSE's holder
            // into the driver's own name did not know the holder at all - a
            // stand-in that names everyone the same, as the facts tests use -
            // and taking its word lost "off Jen's" for "better than the
            // previous". A holder that is all digits is a transponder, never
            // a name, so it is not kept: two profiles sharing a name fall to
            // "better than the previous" rather than reading a number out.
            var holder = Clean(note.PreviousHolder is { Length: > 0 } h ? HolderName(h) : null);

            string HolderName(string raw)
            {
                var looked = nameFor(raw);

                if (string.IsNullOrWhiteSpace(looked))
                {
                    return raw;
                }

                return string.Equals(Clean(looked), who, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(raw, note.DriverId, StringComparison.OrdinalIgnoreCase)
                    && !raw.All(char.IsDigit)
                        ? raw
                        : looked;
            }

            var by = beaten - note.LapSeconds;

            text.Append(holder.Length > 0 && !string.Equals(holder, who, StringComparison.OrdinalIgnoreCase)
                ? $" It takes {Invariant(by, "0.000")}s off {holder}'s {Invariant(beaten, "0.000")}s."
                : $" That is {Invariant(by, "0.000")}s better than the previous {Invariant(beaten, "0.000")}s.");
        }

        // The kind is in the key, not just the driver: a personal best and
        // then the fastest lap of the night from the same car in the same
        // race are two different things to say.
        return new FactBlock(
            $"pace:{raceKey}:{note.Kind}:{note.DriverId}",
            text.ToString(),
            FactTheme.Driver(note.DriverId));
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
