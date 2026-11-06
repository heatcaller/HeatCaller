namespace HeatCaller;

/// <summary>
/// The round the night has just finished.
///
/// Its own tiny record rather than a RoundDefinition, for the reason
/// UpcomingRound is not one either: the look-back wants a label and a
/// class, and handing the continuity lines the programme store's own type
/// would tie a sentence to the shape of the programme.
///
/// Both fields are what somebody typed on the Programme page, so both go
/// through CommentaryFacts.Clean before they are said - see Subject.
/// </summary>
/// <param name="Label">"B-Main", "Q2 Heat 1" - as the programme calls it.</param>
/// <param name="ClassName">Whose round it was. Blank where the club files nothing against a class.</param>
public readonly record struct RoundJustRun(string Label, string ClassName);

/// <summary>
/// Whose turn each phrasing is, and what has already been heard tonight.
///
/// WHY THERE IS A ROTATION AT ALL
///
/// Continuity is the one thing in the commentary layer that goes out every
/// single gap, all night, and it is templated on purpose - it has to work
/// with the AI switched off and the month's allowance spent, which is
/// exactly when a club most needs their evening to still sound like a
/// programme. The price of a template is that it is the same words every
/// time, and "still to come, the A-main" in those six words every twenty
/// minutes is worse than saying nothing: a room stops hearing it, and then
/// it stops hearing the announcer.
///
/// So each line has a handful of phrasings and this decides which. Two
/// rules, and both are load-bearing:
///
///   1. A BAG, not a dice. Phrasings come out of a shuffled bag and the bag
///      is only refilled when it is empty, so every phrasing is heard once
///      before any is heard twice. A dice rolls the same number twice often
///      enough that a crowd notices, and "it is random" is no comfort to
///      the club who heard it.
///
///   2. NOTHING IS SAID TWICE IN ONE NIGHT. The exact sentence is
///      remembered, not the phrasing - because the phrasing crossed with
///      the subject and the clock is what a room actually hears. Once every
///      way of saying a thing has been heard, the line is simply not said,
///      which ShowBlock.Fit was built to cope with.
///
/// WHY NEW MEETING IS GUARDED
///
/// The same reason CommentaryDirector.NewMeeting's own guard exists, and it
/// is worth saying twice: the host calls it from a tick that runs twice a
/// second, not once a night. Cleared unguarded, the bag would be refilled
/// sixty times a minute, every bridge of the evening would be drawn from a
/// fresh shuffle, and the club would get the same six words over and over -
/// the exact failure this class exists to prevent, with nothing in any log
/// to say so. Two detectors were lost that way in one afternoon.
///
/// ONE THREAD, like the rest of the director
///
/// Nothing here is locked and nothing needs to be. It is touched from the
/// announcer screen's own tick, and from the Say inside a running order -
/// which runs on whichever thread started the block, and ShowTreatment
/// awaits with ConfigureAwait(true) the whole way through, so that is the
/// same thread. Random is not thread-safe either; it does not have to be
/// for the same reason.
/// </summary>
public sealed class ContinuityVoice
{
    /// <summary>Which phrasings of a pool are still to come, popped from the END. See Refill.</summary>
    private readonly Dictionary<string, List<int>> _bags = new(StringComparer.Ordinal);

    /// <summary>The phrasing each pool last handed out, so a refill cannot repeat it. See Refill.</summary>
    private readonly Dictionary<string, int> _last = new(StringComparer.Ordinal);

    /// <summary>Every continuity line the room has actually heard tonight.</summary>
    private readonly HashSet<string> _heard = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The line each pool last handed out through <see cref="PickOrRepeat"/>, so a repeat is never the same words twice running.</summary>
    private readonly Dictionary<string, string> _lastOut = new(StringComparer.Ordinal);

    private readonly Random _shuffle;

    private string _dayId = string.Empty;

    /// <param name="seed">
    /// Fixes the shuffle, for a test that wants the same line twice.
    /// Nothing in the app passes one: a club's evening should not open with
    /// the same phrasing as every other club's.
    /// </param>
    public ContinuityVoice(int? seed = null) =>
        _shuffle = seed is { } fixedSeed ? new Random(fixedSeed) : new Random();

    /// <summary>How many continuity lines the room has heard tonight. For the console and the tests.</summary>
    public int Heard => _heard.Count;

    /// <summary>
    /// Told each time the room hears a line it had not heard tonight, so the
    /// owner can write the night down (CommentaryDirector.Save). Continuity
    /// goes out with the AI switched off, when no paid segment ever runs to
    /// save anything - so waiting for one would lose the night on a restart.
    /// On the thread <see cref="Said"/> is called on, which is the UI's.
    /// </summary>
    public Action? LineHeard { get; set; }

    /// <summary>True when this exact line has already been heard tonight.</summary>
    public bool AlreadyHeard(string? line) =>
        !string.IsNullOrWhiteSpace(line) && _heard.Contains(line.Trim());

    /// <summary>
    /// A new race day: last week's lines are this week's lines again.
    /// Guarded on the day ACTUALLY changing - see the note on this class,
    /// which is the whole reason the guard is here.
    /// </summary>
    public void NewMeeting(string? dayId)
    {
        var day = dayId ?? string.Empty;

        if (string.Equals(_dayId, day, StringComparison.Ordinal))
        {
            return;
        }

        _dayId = day;

        _bags.Clear();
        _last.Clear();
        _heard.Clear();
        _lastOut.Clear();
    }

    private sealed record SavedNight(string DayId, List<string>? Lines);

    /// <summary>Tonight's heard lines, for AppSettings to hold across a restart - the continuity half of SaidTonight.Save.</summary>
    public string Save() =>
        System.Text.Json.JsonSerializer.Serialize(new SavedNight(_dayId, _heard.ToList()));

    /// <summary>
    /// <see cref="NewMeeting"/>, picking tonight's heard lines back up when
    /// the saved night is the same race day - so a laptop closed between the
    /// heats and the mains does not come back and offer the room the same
    /// sentences again. Anything unreadable is a fresh night, not a throw.
    ///
    /// Only when the day actually changes, which includes the first call
    /// after the app starts: the host calls this twice a second, and the
    /// lines heard since then are already in memory.
    /// </summary>
    public void Restore(string? saved, string? dayId)
    {
        var day = dayId ?? string.Empty;

        if (string.Equals(_dayId, day, StringComparison.Ordinal))
        {
            return;
        }

        NewMeeting(day);

        if (string.IsNullOrWhiteSpace(saved))
        {
            return;
        }

        SavedNight? night;

        try
        {
            night = System.Text.Json.JsonSerializer.Deserialize<SavedNight>(saved);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        if (night is null || !string.Equals(night.DayId, day, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var line in night.Lines ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                _heard.Add(line.Trim());
            }
        }
    }

    /// <summary>
    /// Picks the next way of saying something, out of every way of saying
    /// it - or null when the room has heard all of them tonight.
    ///
    /// Picking is NOT saying. The line is marked as heard by
    /// <see cref="Said"/>, which the running order calls at the point the
    /// words actually go to the announcer: a bridge the gap turned out to
    /// have no room for (ShowBlock.Fit) was never heard, and burning a
    /// phrasing on it would be the same quiet loss SaidTonight.Mark is
    /// written the long way round to avoid.
    /// </summary>
    /// <param name="pool">Which rotation this is. One bag per pool, so a look-back and a sign-off do not share a shuffle.</param>
    /// <param name="candidates">Every phrasing, already filled in. Blanks are ignored.</param>
    public string? Pick(string pool, IReadOnlyList<string> candidates)
    {
        var lines = new List<string>(candidates.Count);

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                lines.Add(candidate.Trim());
            }
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var tried = new bool[lines.Count];
        var left = lines.Count;

        // Bounded rather than "keep going until one is free". A bag that
        // empties part-way through this loop is refilled with a fresh
        // permutation, which can offer an index this call has already
        // tried - so a while loop here would be a hang on a race night
        // rather than a repeated line, and a hang is the one thing worse
        // than a repeat. Three passes is far more than enough for every
        // index to have been offered at least once.
        for (var attempt = 0; attempt < lines.Count * 3 && left > 0; attempt++)
        {
            var index = NextIndex(pool, lines.Count);

            if (tried[index])
            {
                continue;
            }

            tried[index] = true;
            left--;

            if (!_heard.Contains(lines[index]))
            {
                return lines[index];
            }
        }

        // Every phrasing of this line, about this round, with this
        // estimate, has already gone out tonight. Said again it would be
        // the repeat this class exists to prevent, so it is not said: the
        // running order is one item shorter, which is a case ShowBlock.Fit
        // handles for its own reasons anyway.
        return null;
    }

    /// <summary>
    /// A line has actually gone to the announcer, so it is not said again
    /// tonight. Called from the running order's own Say, beside the queue
    /// call and never before it - see <see cref="Pick"/>.
    /// </summary>
    public void Said(string? line)
    {
        // Not written down before a race day is known: the honours can be
        // heard with the commentary switched off, when the director is never
        // told the day, and a save with no day in it would overwrite the
        // night a restart is meant to pick back up.
        if (!string.IsNullOrWhiteSpace(line) && _heard.Add(line.Trim()) && _dayId.Length > 0)
        {
            LineHeard?.Invoke();
        }
    }

    /// <summary>
    /// <see cref="Pick"/>, except that once every phrasing has been heard
    /// tonight one is said again rather than none - the bag still turning,
    /// and never the same words as this pool's last.
    ///
    /// ONLY FOR A LINE WHOSE SILENCE COSTS MORE THAN A REPEAT. The honours
    /// hand-back is one: without it the whole segment is dropped (see
    /// RaceHonours.Segment), so a pool of sixteen fixed sentences was
    /// silencing the honours for the rest of a busy night. A bridge is not
    /// one - a gap with one item fewer is fine, which is why Pick refuses.
    /// </summary>
    public string? PickOrRepeat(string pool, IReadOnlyList<string> candidates)
    {
        var picked = Pick(pool, candidates);

        if (picked is null)
        {
            var lines = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Select(candidate => candidate.Trim())
                .ToList();

            _lastOut.TryGetValue(pool, out var before);

            // Bounded, for Pick's reason: a refilled bag can offer the same
            // index again, and a hang is worse than a repeat.
            for (var attempt = 0; attempt < lines.Count * 3 && picked is null; attempt++)
            {
                var line = lines[NextIndex(pool, lines.Count)];

                if (lines.Count == 1 || !string.Equals(line, before, StringComparison.OrdinalIgnoreCase))
                {
                    picked = line;
                }
            }
        }

        if (picked is not null)
        {
            _lastOut[pool] = picked;
        }

        return picked;
    }

    /// <summary>Which phrasing of <paramref name="pool"/> is next out of the bag.</summary>
    private int NextIndex(string pool, int choices)
    {
        if (choices <= 1)
        {
            return 0;
        }

        // The count is part of the key. Nothing in the app changes the
        // number of phrasings a pool has mid-night, but a bag holding an
        // index from a longer list would be an IndexOutOfRange over the PA,
        // and keying it costs one string.
        var key = $"{pool}:{choices}";

        if (!_bags.TryGetValue(key, out var bag) || bag.Count == 0)
        {
            bag = Refill(key, choices);
            _bags[key] = bag;
        }

        var index = bag[^1];

        bag.RemoveAt(bag.Count - 1);

        _last[key] = index;

        return index;
    }

    /// <summary>Every phrasing, shuffled, with the one just used kept off the front.</summary>
    private List<int> Refill(string key, int choices)
    {
        var bag = new List<int>(choices);

        for (var i = 0; i < choices; i++)
        {
            bag.Add(i);
        }

        // Fisher-Yates, which is the only shuffle that is actually uniform.
        for (var i = bag.Count - 1; i > 0; i--)
        {
            var j = _shuffle.Next(i + 1);

            (bag[i], bag[j]) = (bag[j], bag[i]);
        }

        // The bag is popped from the END, so bag[^1] is the next one out. A
        // fresh bag whose next one out is the phrasing the LAST bag ended
        // on would say the same words twice running, which is the one thing
        // a bag is for - and it happens once every few cycles, which is
        // often enough for a club to hear it on a long evening. Swapped
        // with the far end rather than reshuffled: a reshuffle can land on
        // the same answer again.
        if (bag.Count > 1 && _last.TryGetValue(key, out var last) && bag[^1] == last)
        {
            (bag[^1], bag[0]) = (bag[0], bag[^1]);
        }

        return bag;
    }
}

/// <summary>
/// The continuity lines: the bridges a broadcast puts between the things it
/// says.
///
/// WHAT CONTINUITY IS FOR
///
/// A broadcast tells a room where it is in the evening. "That was the
/// B-main. Still to come, the A-main for Stock." Neither half is news -
/// everybody watched the B-main and the programme is pinned to the wall -
/// and that is not the point: it is what turns a sequence of announcements
/// into something a room follows. The segments are the content; this is the
/// joinery, and a bracket is noticed mostly when it is missing.
///
/// TEMPLATED, AND THAT IS THE WHOLE POINT
///
/// Every line in here is built from facts the app already holds, by string
/// concatenation, with no model and no budget anywhere near it. Continuity
/// is the one thing that must still be there when the month's allowance has
/// gone and the AI is switched off at the wall - which is precisely the
/// evening a club most needs their night to still sound like a programme.
/// A model-written bridge would be the first thing to disappear.
///
/// NOTHING IS INVENTED, AND THAT IS NOT NEGOTIABLE
///
/// A "still to come" for a round that is not on the programme is the worst
/// thing this file could do: it is a promise, made over a PA, in the club's
/// own voice, that the evening then breaks. So every round named here comes
/// from the programme store by way of director.Upcoming, the round looked
/// back on comes from what has actually been filed, and the estimate is
/// handed in rather than worked out - the rule CommentaryFacts.ComingUpNext
/// already follows, because how long a club night runs against its schedule
/// is WhereTimeGoes's question and a bridge that guessed at it would be
/// inventing the one number an announcer gets held to.
///
/// Where a fact is missing the line is simply not said. Every method here
/// returns null for that, and a running order one item shorter is a case
/// ShowBlock.Fit handles for its own reasons anyway.
/// </summary>
public static class CommentaryContinuity
{
    /// <summary>
    /// Looking back at what has just run. Said BEFORE the segment it
    /// belongs to - "That was the B-main" and then the recap of it - which
    /// is the order a broadcast uses and the order a room expects.
    /// </summary>
    private static readonly string[] LookBacks =
    {
        "That was {0}.",
        "So that is {0} done.",
        "That is {0} in the books.",
        "And that wraps up {0}.",
        "That was {0}, then.",
    };

    /// <summary>Handing over to the next thing on the programme.</summary>
    private static readonly string[] StillToComes =
    {
        "Still to come, {0}.",
        "Next up, {0}.",
        "Coming up, {0}.",
        "We go to {0} next.",
        "Still on tonight's program, {0}.",
        "Ahead of us, {0}.",
    };

    /// <summary>The top of the night, where "still to come" would be wrong - nothing has gone yet.</summary>
    private static readonly string[] FirstUps =
    {
        "First up tonight, {0}.",
        "We start with {0}.",
        "Opening tonight's program, {0}.",
        "We get under way with {0}.",
    };

    /// <summary>
    /// A race is coming and the programme cannot name it - a freeform race,
    /// or a club who never entered a programme. Promises nothing it does
    /// not know.
    /// </summary>
    private static readonly string[] BackShortlys =
    {
        "Back in a moment.",
        "More in a moment.",
        "We will be back before the next race.",
        "Back shortly.",
        "Stay with us.",
    };

    /// <summary>
    /// The hand-back with no gap to name (22 Oct 2026, known issues): after
    /// a freeform race, with no programme and no timetable to say when the
    /// next one is. Promises nothing about when - not even "a moment" -
    /// only that the room should not go anywhere.
    /// </summary>
    private static readonly string[] StayWithUses =
    {
        "Stay with us.",
        "Keep it right here.",
        "Do not go anywhere.",
        "Stay right where you are.",
    };

    /// <summary>The way out. Only where the app has already decided the night is over - see ContinuityShow.Tail.</summary>
    private static readonly string[] TheLots =
    {
        "That is the lot for tonight.",
        "And that is the lot for tonight.",
        "That is everything for tonight.",
        "That is us done for tonight.",
    };

    /// <summary>
    /// How long until the next one, where the host knows. Appended to the
    /// opening rather than being lines of their own, so the two rotations
    /// multiply: six openings and three of these is eighteen ways to say
    /// one thing.
    /// </summary>
    private static readonly string[] Soons =
    {
        " About {0} minutes away.",
        " That is about {0} minutes away.",
        " Roughly {0} minutes from now.",
    };

    /// <summary>How many cars are due out in it, where the programme says.</summary>
    private const string CarsTail = " {0} cars in that one.";

    /// <summary>
    /// The shortest estimate worth saying out loud, in minutes.
    ///
    /// One minute is not said at all, and not because of the arithmetic:
    /// "about 1 minutes away" is what a template produces for it, and a
    /// special case for the singular would be a sentence nobody ever reads
    /// back. Under two minutes the room can see the grid forming anyway.
    /// </summary>
    private const double LeastMinutes = 2;

    /// <summary>
    /// Past this the estimate is not an estimate. Two hours out it is the
    /// schedule's opinion of a night that has not happened yet, and the
    /// rule CommentaryFacts.ComingUpNext applies to the same number.
    /// </summary>
    private const double MostMinutes = 120;

    /// <summary>
    /// "That was the B-main for Stock" - or null when nothing has been
    /// filed that this could be about.
    /// </summary>
    public static string? LookBack(RoundJustRun? ran, ContinuityVoice voice)
    {
        if (ran is not { } round)
        {
            return null;
        }

        if (Subject(round.Label, round.ClassName) is not { } subject)
        {
            return null;
        }

        return voice.Pick("lookback", Filled(LookBacks, subject));
    }

    /// <summary>
    /// "Still to come, the A-main for Stock. About four minutes away." -
    /// or null when the programme has nothing left to name.
    /// </summary>
    public static string? StillToCome(IReadOnlyList<UpcomingRound>? next, TimeSpan? inAbout, ContinuityVoice voice) =>
        Ahead("stilltocome", StillToComes, next, inAbout, voice);

    /// <summary>
    /// The same, for the top of the night: "First up tonight, Q1 Heat 1
    /// for Stock."
    /// </summary>
    public static string? FirstUp(IReadOnlyList<UpcomingRound>? next, TimeSpan? inAbout, ContinuityVoice voice) =>
        Ahead("firstup", FirstUps, next, inAbout, voice);

    /// <summary>"Back in a moment" - a hand-back that names nothing, because there is nothing to name.</summary>
    public static string? BackShortly(ContinuityVoice voice) => voice.Pick("backshortly", BackShortlys);

    /// <summary>"That is the lot for tonight."</summary>
    public static string? ThatIsTheLot(ContinuityVoice voice) => voice.Pick("thatisthelot", TheLots);

    /// <summary>A hand-back that names no gap - see StayWithUses. Null only when every phrasing has been heard too recently.</summary>
    public static string? StayWithUs(ContinuityVoice voice) => voice.Pick("staywithus", StayWithUses);

    /// <summary>
    /// One of the forward-looking lines, with every tail the facts allow.
    ///
    /// The NEXT round and no more. "Coming up: Q3 Heat 2; then Q3 Heat 3;
    /// then the A-Main" is a list, and there is already a segment whose job
    /// that is (CommentaryFacts.ComingUpNext, which takes three). A bridge
    /// is one short sentence between two other things, and a bridge that
    /// reads out the programme stops being one.
    /// </summary>
    private static string? Ahead(
        string pool,
        string[] openings,
        IReadOnlyList<UpcomingRound>? next,
        TimeSpan? inAbout,
        ContinuityVoice voice)
    {
        if (next is null or { Count: 0 })
        {
            return null;
        }

        var round = next[0];

        if (Subject(round.Label, round.ClassName) is not { } subject)
        {
            return null;
        }

        // The bare sentence is always one of the choices, so a club whose
        // host knows no estimate still gets the whole rotation.
        var tails = new List<string> { string.Empty };

        // 6 Nov 2026, Bug hunt 11b: the limits are checked on the minutes as
        // they will be said, not the raw estimate. 119.6 minutes passed the
        // raw "< 120" check and was then said as "about 120 minutes", past the
        // cap - the same rule CommentaryFacts.ComingUpNext now applies.
        if (inAbout is { } until
            && Math.Round(until.TotalMinutes, MidpointRounding.AwayFromZero) is var rounded
            && rounded >= LeastMinutes && rounded < MostMinutes)
        {
            // A decimal POINT whatever the PC's region, like every other
            // number that reaches the voice. On a comma-decimal laptop the
            // fact layer wrote "12,345s" and the PA read out "twelve
            // thousand three hundred forty-five seconds" - see
            // CommentaryFacts.Invariant, which is the one guard for this.
            var minutes = CommentaryFacts.Invariant(rounded, "0");

            foreach (var soon in Soons)
            {
                tails.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, soon, minutes));
            }
        }

        // Two cars or more. "1 cars in that one" is the same singular trap
        // the estimate has, and a one-car round is not news anyway.
        if (round.Entries > 1)
        {
            tails.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                CarsTail,
                CommentaryFacts.Invariant(round.Entries, "0")));
        }

        var candidates = new List<string>(openings.Length * tails.Count);

        foreach (var opening in openings)
        {
            var said = string.Format(System.Globalization.CultureInfo.InvariantCulture, opening, subject);

            foreach (var tail in tails)
            {
                candidates.Add(said + tail);
            }
        }

        return voice.Pick(pool, candidates);
    }

    /// <summary>Every phrasing in a pool, with the subject dropped in.</summary>
    private static List<string> Filled(string[] templates, string subject)
    {
        var lines = new List<string>(templates.Length);

        foreach (var template in templates)
        {
            lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, subject));
        }

        return lines;
    }

    /// <summary>
    /// What a round is called in a sentence: "the A-Main for Stock",
    /// "Q2 Heat 1 for Box Stock", or null when there is no label worth
    /// saying.
    ///
    /// Both halves go through the same cleaning every other piece of typed
    /// text in this layer does. These lines do not reach a model, so an
    /// injected instruction is not the risk here - but a round label with a
    /// line break in it, or forty words of one, is read out over a PA
    /// exactly as typed, and Field is already the one guard for that.
    /// </summary>
    private static string? Subject(string? label, string? className)
    {
        var name = CommentaryFacts.CleanLong(label);

        if (name.Length == 0)
        {
            return null;
        }

        var cls = CommentaryFacts.Clean(className);

        return cls.Length > 0 ? $"{Named(name)} for {cls}" : Named(name);
    }

    /// <summary>
    /// The article, where the club's own label wants one.
    ///
    /// "The A-Main" is how a main is spoken and "the Heat 1" is not, so the
    /// article cannot simply be glued on - the first reads as a broadcast
    /// and the second as a machine. This is GRAMMAR read off the label the
    /// club typed, not a fact worked out about the round: nothing is
    /// claimed about what kind of race it is, and a label this does not
    /// recognise is said exactly as it was typed.
    ///
    /// Deliberately not RoundDefinition.Kind or MainTier, which would be
    /// the real answer: UpcomingRound carries a label, a class and a count,
    /// and widening it to settle a definite article would put the shape of
    /// the programme into a sentence - which is the thing that record
    /// exists to keep out.
    /// </summary>
    private static string Named(string label) =>
        label.Contains("main", StringComparison.OrdinalIgnoreCase)
        || label.Contains("final", StringComparison.OrdinalIgnoreCase)
            ? $"the {label}"
            : label;
}

/// <summary>
/// What the night around a block looks like: what has just run, what is
/// still to come, and roughly how long until it.
///
/// Gathered once, on the thread the host called from, and handed in - the
/// rule CommentaryDirector.NextAsync follows for the same reason. Every
/// reader behind these walks a store that belongs to the UI thread, and a
/// running order assembled after an await would be asking the programme
/// store for tonight's rounds from a thread-pool thread.
/// </summary>
/// <param name="JustRan">The round just filed, or null when nothing has been.</param>
/// <param name="Upcoming">What is left on the programme, in running order. Empty when nothing is.</param>
/// <param name="InAbout">Roughly how long until the next one, or null when the host does not know. THE GAP a block is fitted to.</param>
/// <param name="Freeform">
/// Whether the race just run was off the programme - a freeform race, or a
/// club with no programme at all (22 Oct 2026). The one case where nothing
/// can be named AND a race is plausibly coming, so the show can hand back
/// with "stay with us" rather than nothing - see ContinuityShow.Tail.
/// </param>
internal readonly record struct ContinuityFacts(
    RoundJustRun? JustRan,
    IReadOnlyList<UpcomingRound> Upcoming,
    TimeSpan? InAbout,
    bool Freeform = false)
{
    /// <summary>A night nothing is known about - what an unwired host gives, and what a preview pass gives.</summary>
    internal static readonly ContinuityFacts Unknown =
        new(null, Array.Empty<UpcomingRound>(), null);
}

/// <summary>
/// The running orders: what belongs inside one show.
///
/// WHY THERE IS A FILE FOR THIS AND NOT A METHOD ON THE ANNOUNCER SCREEN
///
/// Deciding what goes in a block is a judgement with half a dozen cases in
/// it - the top of the night, the way out, the segment that already says
/// what the bridge would, the club with no programme entered - and each one
/// is a sentence a room hears. On a Form it would be reachable only by
/// building a Form; here it is a table test, which is the same bargain
/// CommentaryFacts and CommentaryReaders strike and for the same reason.
///
/// THE ORDERS, AND WHY EACH ONE IS SHAPED THAT WAY
///
///   THE COLD OPEN.   headlines, then "first up tonight, Q1 Heat 1".
///                    No look-back: nothing has run, and an opener that
///                    said "that was" would be lying in its first breath.
///
///   BETWEEN RACES.   "That was the B-main" | the segment | "still to
///                    come, the A-main". The look-back goes in FRONT of
///                    the segment because that is the order a broadcast
///                    uses - you say what it was and then talk about it -
///                    and because a bridge left at the END of a block is
///                    taken off by ShowBlock.Fit, so a trailing one would
///                    never be heard at all.
///
///   THE HAND-OVER.   "That was the B-main" | "still to come, the A-main",
///                    with no segment between them. The order for an
///                    evening with the AI off and the allowance spent, and
///                    the reason the lines here are templated: it is the
///                    whole of what some clubs will ever hear, and it is
///                    still a programme. See <see cref="HandOver"/>.
///
///   THE WAY OUT.     "That was the A-main" | the sign-off | "that is the
///                    lot for tonight".
///
/// WHAT IS NEVER IN ONE
///
/// A still-to-come after the "coming up next" segment. That segment IS
/// this line, written by the model from the same rounds, and a block that
/// said both would say the same thing twice in a row in two voices - which
/// is the single most noticeable thing an announcer can do wrong.
/// </summary>
internal static class ContinuityShow
{
    /// <summary>
    /// How near a race has to be for "back in a moment" to be a hand-back
    /// rather than a guess. See <see cref="Tail"/>.
    /// </summary>
    private static readonly TimeSpan SoonEnough = TimeSpan.FromMinutes(20);

    /// <summary>
    /// The running order for a segment the director has had written, or
    /// null when there is nothing to bridge to.
    ///
    /// Null is not a failure and it is not rare: a club with no programme
    /// entered, or a night whose last race has just run, has nothing to
    /// promise and nothing to look back on. The caller then uses the
    /// single-segment path, which is what it is for - see
    /// AnnouncerControl.RunAsShowAsync, whose fallback is the one property
    /// in this area that must not be broken.
    /// </summary>
    /// <param name="said">The segment. Its Kind bills the block and captions the Big Screen.</param>
    /// <param name="facts">The night around it - see <see cref="ContinuityFacts"/>.</param>
    /// <param name="voice">Whose turn each phrasing is.</param>
    /// <param name="sayWhole">Queues a one-voice segment. Called once, and must set the caller's "it has been said" flag.</param>
    /// <param name="sayTurn">
    /// Queues one turn of a two-hander, so the caller can pick the voice.
    /// Same flag. Handed the turn's place in the run as well, so the caller
    /// can trim the silence either side of one that has another hard
    /// against it - see CommentaryExchange.ExchangeRun.
    /// </param>
    /// <param name="sayLine">Queues a continuity line.</param>
    internal static ShowBlock? Around(
        SpokenSegment said,
        ContinuityFacts facts,
        ContinuityVoice voice,
        Action sayWhole,
        Action<ExchangeTurn, CommentaryExchange.ExchangeRun> sayTurn,
        Action<string> sayLine)
    {
        var items = new List<ShowItem>();

        if (LookBack(said, facts, voice) is { } back)
        {
            items.Add(Bridge(back, voice, sayLine));
        }

        items.Add(said.IsExchange
            ? ShowItem.Exchange(said.Turns, sayTurn, said.Kind)
            : ShowItem.Spoken(said.Text, sayWhole, said.Kind));

        if (Tail(said.Kind, facts, voice) is { } tail)
        {
            // The one mark in the block, and in front of the hand-over
            // rather than in front of the segment. This is the gear change
            // - the evening stops being about the race that has just run
            // and starts being about the next one - and punctuation that
            // happens between every pair stops being punctuation at all
            // (see ShowItem.PunctuateBefore).
            items.Add(Line(tail, voice, sayLine, punctuate: true));
        }

        // One item is not a programme, it is an announcement, and the
        // single-segment path already stages one of those - with a
        // fallback this cannot offer, because a block speaks its items
        // itself either way.
        return items.Count < 2 ? null : Block(items, said.Kind, facts);
    }

    /// <summary>
    /// The hand-over: continuity and nothing else. "That was the B-main for
    /// Stock. Still to come, the A-main for Stock."
    ///
    /// WHY THIS IS THE IMPORTANT ONE
    ///
    /// Everything else in the commentary layer needs a model and an
    /// allowance. This needs neither, so it is what a club hears on the
    /// evening the month has run out, on the evening they never switched
    /// the AI on, and on the evening the relay is down - and it is still
    /// two voices-worth of programme between two races rather than silence
    /// over the top of the playlist.
    ///
    /// BOTH HALVES OR NOTHING. A hand-over with only a forward half is the
    /// "coming up next" segment's job, and one with only a backward half
    /// promises nothing and so is not continuity at all - ShowBlock.Fit
    /// would take the bridge straight back off as a stranded one. So this
    /// is null unless the app knows both what has run and what is next,
    /// which is also the honest bar: nothing here is said unless the
    /// programme says it.
    /// </summary>
    internal static ShowBlock? HandOver(
        ContinuityFacts facts,
        ContinuityVoice voice,
        Action<string> sayLine)
    {
        if (CommentaryContinuity.LookBack(facts.JustRan, voice) is not { } back)
        {
            return null;
        }

        if (CommentaryContinuity.StillToCome(facts.Upcoming, facts.InAbout, voice) is not { } on)
        {
            return null;
        }

        // The forward half is a SPOKEN item and not a bridge, which is the
        // one subtlety in this method. ShowBlock.Fit takes a bridge off the
        // end of what fitted, so a hand-over of two bridges would fit
        // perfectly and then say nothing at all. The forward half is also
        // the half that carries the facts, so it is the half that earns
        // being an item in its own right.
        var items = new[]
        {
            Bridge(back, voice, sayLine),
            Line(on, voice, sayLine, punctuate: true),
        };

        // ComingUpNext bills it and captions it "Coming Up", which is what
        // it is. It does NOT put it on the budget's ladder: nothing here
        // was written or paid for, and a templated line that reserved an
        // allowance would be taking a segment off the night for nothing.
        return Block(items, SegmentKind.ComingUpNext, facts);
    }

    /// <summary>
    /// What goes after the segment, or null.
    ///
    /// The order of the cases is the order they have to be in: the one that
    /// must never fire first, then the top of the night, then the ordinary
    /// hand-over, then the two ways a night can have nothing named left.
    /// </summary>
    private static string? Tail(SegmentKind kind, ContinuityFacts facts, ContinuityVoice voice)
    {
        // The segment that already is this line. See the note on this class.
        if (kind == SegmentKind.ComingUpNext)
        {
            return null;
        }

        if (kind == SegmentKind.ColdOpen)
        {
            return CommentaryContinuity.FirstUp(facts.Upcoming, facts.InAbout, voice);
        }

        if (facts.Upcoming.Count > 0)
        {
            return CommentaryContinuity.StillToCome(facts.Upcoming, facts.InAbout, voice);
        }

        // "That is the lot for tonight" ONLY where the app has already
        // decided the night is over, which is the sign-off and nothing
        // else. The programme having nothing unrun in it is not the same
        // thing: a club adds a bonus main after tea, and an announcer who
        // has already closed the evening has to take it back.
        if (kind == SegmentKind.SignOffNumbers)
        {
            return CommentaryContinuity.ThatIsTheLot(voice);
        }

        // A race is coming and the programme cannot name it - a freeform
        // race, or one of the many clubs who never enter a programme at
        // all. "Back in a moment" is true and promises nothing; without
        // even an estimate, nothing is known and nothing is said.
        //
        // Bounded at the far end as well, by SoonEnough: "back in a moment"
        // with the next race half an hour away is a promise too, and one the
        // club breaks by having tea.
        if (facts.InAbout is { } until && until > TimeSpan.Zero && until < SoonEnough)
        {
            return CommentaryContinuity.BackShortly(voice);
        }

        // NO GAP TO NAME (22 Oct 2026, known issues): a freeform race, with
        // no programme and no timetable, was the exact club this hand-back
        // was written for - and it got nothing, because the line above
        // needs an estimate nobody can give it. Only after a race run off
        // the programme: a programmed night with nothing left unrun is the
        // sign-off's call to make, and a night nothing is known about says
        // nothing. A phrasing that promises no time at all.
        return facts.Freeform && facts.InAbout is null
            ? CommentaryContinuity.StayWithUs(voice)
            : null;
    }

    /// <summary>The look-back, where there is one to make.</summary>
    private static string? LookBack(SpokenSegment said, ContinuityFacts facts, ContinuityVoice voice) =>
        said.Kind == SegmentKind.ColdOpen
            ? null
            : CommentaryContinuity.LookBack(facts.JustRan, voice);

    /// <summary>
    /// A continuity line as a bridge - see ShowItem.IsContinuity for what
    /// that changes about which items are dropped.
    /// </summary>
    private static ShowItem Bridge(string line, ContinuityVoice voice, Action<string> sayLine) =>
        ShowItem.Bridge(line, () => Say(line, voice, sayLine));

    /// <summary>A continuity line as an item in its own right.</summary>
    private static ShowItem Line(string line, ContinuityVoice voice, Action<string> sayLine, bool punctuate) =>
        ShowItem.Spoken(line, () => Say(line, voice, sayLine), kind: null, punctuateBefore: punctuate);

    /// <summary>
    /// Marked as heard and then queued, in that order.
    ///
    /// The queue call is fire and forget, so a line marked afterwards would
    /// be marked by whatever ran next rather than by this - and a line that
    /// went out and was never marked is one the rotation will offer again
    /// in the next gap, which is the repeat all of this exists to stop. The
    /// same reasoning as CommentaryDirector.SalesReadSaid, which is called
    /// before SpeakReminder for the same reason.
    /// </summary>
    private static void Say(string line, ContinuityVoice voice, Action<string> sayLine)
    {
        voice.Said(line);
        sayLine(line);
    }

    /// <summary>
    /// The block itself, told the gap it has.
    ///
    /// InAbout IS the gap - that is what makes a block fit between two
    /// heats instead of running into the start of one. Where the host does
    /// not know it, NoLimit is handed over and the block's own
    /// ShowBlock.LongestBlock still caps it at a minute and a half; the
    /// race-state watcher in RaceControlControl.RaceEvents abandons
    /// whatever is left of it the moment a race moves, which is the real
    /// backstop and the only one that cannot be wrong.
    /// </summary>
    private static ShowBlock Block(IReadOnlyList<ShowItem> items, SegmentKind? kind, ContinuityFacts facts) =>
        new(items)
        {
            Kind = kind,
            Gap = facts.InAbout ?? ShowBlock.NoLimit,
        };
}
