namespace HeatCaller;

/// <summary>
/// Who got what, and how many badges the read actually names.
/// </summary>
/// <param name="Said">
/// The read itself - "Billy Bob picks up Nail-Biter. Jane Doe takes
/// Personal Best and Hat-Trick." Never blank.
/// </param>
/// <param name="Badges">
/// How many DISTINCT badges <paramref name="Said"/> names. NOT how many the
/// race unlocked: the read is capped (see RaceHonours.MostDrivers and
/// MostEach), and the throw says how many are "to hand out", so it has to
/// be told the number the room is about to hear rather than the number in
/// the store. Saying "four to hand out" and then reading three is the one
/// way this segment could be caught lying by anybody listening.
///
/// DISTINCT, counted across the whole read, since 11 Oct 2026. This used to
/// count driver-and-badge PAIRS, which is the number of awards rather than
/// the number of honours, and the two differ whenever one badge goes to
/// more than one driver. The date-based badges do that constantly - Weekend
/// Warrior and Back to Back fire for everybody who raced both days - so on
/// 11 Oct the throw promised "3 to hand out" and the room then heard two
/// badge names: Back to Back and Weekend Warrior, the second of them twice,
/// for two different drivers. A listener counts honours, not awards.
/// </param>
public readonly record struct HonoursRead(string Said, int Badges);

/// <summary>
/// The honours: the badges a race unlocked, read out as a segment of the
/// recap's broadcast - the announcer throws to a third voice, that voice
/// goes over who got what, and the announcer takes the programme back.
///
/// WHY THERE IS A FILE FOR THIS AND NOT A METHOD ON RACE CONTROL
///
/// The same bargain CommentaryFacts, CommentaryReaders and ContinuityShow
/// strike: every judgement in here is a sentence a room hears, and on a
/// Form it would be reachable only by building a Form and finishing a race.
/// Here it is a table test.
///
/// WHY IT IS NOT IN CommentaryContinuity
///
/// It is the same machinery - phrasings out of a shuffled bag, nothing said
/// twice in a night, no model anywhere near it - and it very nearly went in
/// there. It is here instead because that file is about the PROGRAMME's
/// joinery: "that was the B-main, still to come the A-main", built out of
/// rounds. This is built out of what a race unlocked, which is the
/// Achievements store's business, and a file that read both would be the
/// one place in the app where the programme and the badge catalogue met for
/// no reason. The rotation itself is shared (ContinuityVoice), which is the
/// part that actually had to be.
///
/// THE MODEL IS NOWHERE IN HERE, AND THAT IS THE POINT
///
/// Two reasons, and the second is the real one.
///
///   1. It has to work with the AI switched off at the wall and the month's
///      allowance spent - the same reason continuity is templated. A club
///      who never turn the model on still hear their badges read out as a
///      piece of programme rather than as six announcements in a row.
///
///   2. A badge is a thing that happened to a NAMED PERSON standing in the
///      room. The one unforgivable way to get this segment wrong is to say
///      the wrong name or the wrong badge, and string concatenation cannot
///      do that. A model handed "Billy Bob - Nail-Biter" can, and it only
///      has to do it once, in front of Billy Bob.
///
/// So every name and every badge comes from AchievementEvaluator's own
/// answer and from nowhere else, cleaned the way every other piece of typed
/// text that reaches a PA is cleaned (CommentaryFacts.Clean) - a badge
/// somebody renamed with a line break in it is read out exactly as typed
/// otherwise.
///
/// ONE THREAD, like the rotation it uses. See ContinuityVoice: nothing in
/// here locks anything, and the caller is responsible for picking on the
/// thread that owns the voice - RaceControlControl.PlanHonours does it in
/// the recap's UI-thread prologue for exactly that reason.
/// </summary>
public static class RaceHonours
{
    /// <summary>
    /// The announcer throwing to the honours reader by name.
    ///
    /// Eight, multiplied by the two tails below - see <see cref="Throw"/>
    /// for why the multiplication is load-bearing rather than decorative.
    /// </summary>
    private static readonly string[] ThrowsByName =
    {
        "And for tonight's honours, over to {0}.",
        "Time for the honours - over to {0}.",
        "{0} has tonight's honours.",
        "Over to {0} with the honours.",
        "And the honours from that one - over to {0}.",
        "{0}, over to you for the honours.",
        "Let us have the honours - over to {0}.",
        "The honours next, with {0}.",
    };

    /// <summary>
    /// The same throw where the club has named nobody.
    ///
    /// A separate pool and not the named one with the name left out: "over
    /// to , who has the honours" is what a format string does with a blank,
    /// and a sentence that reads as a hand-over to nobody is worse than one
    /// that simply changes subject. Nothing here invents a name - the same
    /// rule the mechanic's interview follows.
    /// </summary>
    private static readonly string[] Throws =
    {
        "And now for tonight's honours.",
        "Time for the honours.",
        "And the honours from that one.",
        "Which brings us to tonight's honours.",
        "And so to the honours.",
        "That brings us to the honours.",
        "The honours next.",
        "Now, the honours.",
    };

    /// <summary>How many are coming, where it is one. A sentence nobody ever reads back is how "about 1 minutes away" shipped.</summary>
    private const string OneToHandOut = " Just the one to hand out.";

    /// <summary>And where it is more than one.</summary>
    private const string SomeToHandOut = " {0} to hand out.";

    /// <summary>
    /// The announcer taking the programme back, thanking the reader by
    /// name. Crossed with <see cref="WellEarneds"/> - four and four is
    /// sixteen ways to come back from one hand-over.
    /// </summary>
    private static readonly string[] ThanksByName =
    {
        "Thank you, {0}.",
        "Our thanks to {0}.",
        "{0}, thank you.",
        "Thanks to {0} there.",
    };

    /// <summary>
    /// What the announcer adds on the way back, the bare thanks included -
    /// so a club whose reader is named still gets the short version in the
    /// rotation rather than four variations on one sentence.
    /// </summary>
    private static readonly string[] WellEarneds =
    {
        "",
        " Congratulations to all of them.",
        " Well earned, all of them.",
        " Nicely done, all of them.",
    };

    /// <summary>
    /// The hand-back where there is nobody to thank. Twelve of them,
    /// because with no name to cross them against there is nothing to
    /// multiply - see <see cref="HandBack"/>.
    /// </summary>
    private static readonly string[] HandBacks =
    {
        "Congratulations to all of them.",
        "Well earned, all of them.",
        "Nicely done, all of them.",
        "Well done to all of them.",
        "All well earned.",
        "Good stuff from all of them.",
        "That is the honours, then.",
        "And that is the honours - well done, all of you.",
        "Take a bow, all of you.",
        "Every one of those is earned.",
        "Well done indeed, all of them.",
        "That is them all - congratulations.",
    };

    /// <summary>
    /// How a driver comes by a badge, taken in turn down the read.
    ///
    /// BY POSITION IN THE SENTENCE and not out of the bag, which is the one
    /// thing in this file that does not use the rotation. What a room
    /// notices about a read is the same three words four times in one
    /// breath - "picks up, picks up, picks up, picks up" - and that is
    /// fixed by varying them WITHIN the sentence, which a bag keyed on the
    /// whole night cannot do. The bag is for a line said once a race, where
    /// what a room notices is the same sentence twice in an evening: a
    /// different complaint with a different answer.
    /// </summary>
    private static readonly string[] Verbs = { "picks up", "takes", "adds", "collects" };

    /// <summary>
    /// How many drivers one read names.
    ///
    /// A driver's first ever race can unlock several badges at once and a
    /// busy main can unlock them for half the grid; past four names the
    /// segment stops being a celebration and becomes a list read aloud,
    /// which is what RaceControlControl.MaxSpokenAchievements already
    /// decided for the individual calls and for the same reason. The rest
    /// are still earned, still on the driver's record, still on the results
    /// card and still in the log - they are not read out.
    /// </summary>
    public const int MostDrivers = 4;

    /// <summary>How many badges one driver is named for in one read. Three in a row about one person is already a lot.</summary>
    public const int MostEach = 3;

    /// <summary>Which rotation the throw comes out of. One bag per pool - see ContinuityVoice.Pick.</summary>
    private const string ThrowPool = "honoursthrow";

    /// <summary>And the hand-back's.</summary>
    private const string BackPool = "honoursback";

    /// <summary>
    /// Who got what, as one or more short sentences - or null when the race
    /// unlocked nothing worth reading out.
    ///
    /// Grouped by DRIVER and not read badge by badge, which is the whole
    /// difference between a segment and the list of individual calls it
    /// replaces: "Billy Bob picks up Nail-Biter and Hat-Trick" is one
    /// thought, where the same two badges as two announcements is the room
    /// hearing Billy Bob's name twice in six seconds.
    ///
    /// In the order the evaluator found them, which is finishing order - so
    /// the read opens with the winner's badge on a race where the winner
    /// earned one, and a Dictionary's own ordering is never relied on.
    /// </summary>
    public static HonoursRead? Read(IReadOnlyList<AchievementUnlock>? unlocks)
    {
        if (unlocks is null || unlocks.Count == 0)
        {
            return null;
        }

        var order = new List<string>();
        var byDriver = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var unlock in unlocks)
        {
            var who = CommentaryFacts.Clean(unlock.DriverName);
            var badge = CommentaryFacts.Clean(unlock.Badge.Name);

            // Nothing is read out that cannot be said properly. A badge
            // with nobody's name against it would be "unlocks Nail-Biter"
            // with no subject, and a driver with no badge name would be
            // "Billy Bob picks up" - either one read over a PA sounds like
            // the app has broken rather than like somebody has won
            // something.
            if (who.Length == 0 || badge.Length == 0)
            {
                continue;
            }

            if (!byDriver.TryGetValue(who, out var badges))
            {
                // Past the cap, the driver is not named at all rather than
                // named with no badges - see MostDrivers.
                if (order.Count == MostDrivers)
                {
                    continue;
                }

                badges = new List<string>();
                byDriver[who] = badges;
                order.Add(who);
            }

            // The same badge twice for one driver cannot happen through the
            // evaluator, and is dropped here anyway: it would be read out
            // as "Nail-Biter and Nail-Biter", and a read that says that is
            // the thing a room remembers about the evening.
            if (badges.Count < MostEach && !badges.Contains(badge, StringComparer.OrdinalIgnoreCase))
            {
                badges.Add(badge);
            }
        }

        if (order.Count == 0)
        {
            return null;
        }

        var said = new List<string>(order.Count);

        // DISTINCT BADGE NAMES, not driver-and-badge pairs - see
        // HonoursRead.Badges, which carries the whole reason. The dedupe a
        // few lines up is per driver, because that is the list being read
        // out; this one is across the read, because that is what the throw
        // is about to promise.
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < order.Count; i++)
        {
            var badges = byDriver[order[i]];

            foreach (var badge in badges)
            {
                titles.Add(badge);
            }

            said.Add($"{order[i]} {Verbs[i % Verbs.Length]} {AndList(badges)}.");
        }

        return new HonoursRead(string.Join(" ", said), titles.Count);
    }

    /// <summary>
    /// The whole segment as turns: the announcer's throw, the reader's
    /// read, the announcer's hand-back. Empty when there is nothing to say
    /// - which the caller must treat as "no segment at all".
    ///
    /// NOTHING, WHERE THERE IS NOTHING. A race that unlocked no badges gets
    /// no segment: a throw to a third voice who then has nothing to go over
    /// is the worst single thing this feature could do, worse by far than
    /// saying nothing, and it is the one case that would happen most often.
    ///
    /// BOTH HALVES OF THE HAND-OVER OR NOTHING, which is the subtle one.
    /// Where the room has already heard every way of throwing to the reader
    /// tonight, or every way of coming back from them, the segment is not
    /// run at all - rather than thrown without a throw, or left with the
    /// reader as the last voice heard and the announcer never taking the
    /// programme back. The hand-over IS the segment; the badges are what it
    /// is about. See <see cref="Throw"/> for how unlikely running the bag
    /// dry has been made, and RaceControlControl.SpeakHonoursAsync for what
    /// the console says on the night it happens.
    ///
    /// <paramref name="voice"/> is PICKED FROM and not told: marking a
    /// phrasing as heard is the caller's, at the point the words actually go
    /// out - the rule ContinuityVoice.Pick sets and ContinuityShow.Say
    /// follows.
    /// </summary>
    /// <param name="unlocks">What the race unlocked, in the evaluator's order.</param>
    /// <param name="readerName">What the announcer calls the honours reader. Blank: they are thrown to but never named.</param>
    /// <param name="voice">Whose turn each phrasing is. See ContinuityVoice.</param>
    public static IReadOnlyList<ExchangeTurn> Segment(
        IReadOnlyList<AchievementUnlock>? unlocks, string? readerName, ContinuityVoice voice)
    {
        if (Read(unlocks) is not { } read)
        {
            return Array.Empty<ExchangeTurn>();
        }

        var reader = CommentaryFacts.Clean(readerName);

        if (Throw(read.Badges, reader, voice) is not { } thrown)
        {
            return Array.Empty<ExchangeTurn>();
        }

        if (HandBack(reader, voice) is not { } back)
        {
            return Array.Empty<ExchangeTurn>();
        }

        // FromCoHost is "SOMEBODY ELSE is saying this", not "the co-host
        // is". It is the flag the staging reads as the voice having changed
        // hands, which is what puts the beat and the nudge of the bed in at
        // the two hand-overs - see CommentaryExchange.Handoffs and
        // ShowItem.Exchange, whose own note is that picking the actual voice
        // is the caller's business and not the staging's. The honours reader
        // is a third voice and there is no third flag, because the staging
        // has never needed to know WHO: only that it is no longer the same
        // person.
        return new[]
        {
            new ExchangeTurn(false, thrown),
            new ExchangeTurn(true, read.Said),
            new ExchangeTurn(false, back),
        };
    }

    /// <summary>
    /// "And for tonight's honours, over to Jane. Three to hand out." - or
    /// null when the room has heard every way of saying it tonight.
    ///
    /// WHY THE COUNT IS IN THE SENTENCE
    ///
    /// Partly because a broadcast says it. Mostly because the two tails
    /// MULTIPLY the rotation the way CommentaryContinuity.Ahead's estimate
    /// multiplies its own: eight openings and two tails is sixteen ways to
    /// throw to one person, and because the count is one of the words, the
    /// sixteen differ AGAIN by how many badges the race unlocked.
    ///
    /// That is not tidiness. A pool runs dry and then the line is simply not
    /// said (ContinuityVoice.Pick), and for this segment "not said" means no
    /// honours at all - so a club with nine races and a badge in each would
    /// have heard nothing after the eighth, with nothing on the Commentary
    /// page or in the log that could explain it. Sixteen phrasings crossed
    /// with the count is a bag that does not empty on a club night.
    /// </summary>
    private static string? Throw(int badges, string reader, ContinuityVoice voice)
    {
        var openings = reader.Length > 0 ? ThrowsByName : Throws;

        var tails = new[]
        {
            string.Empty,

            // A decimal POINT whatever the PC's region, like every other
            // number that reaches the voice - see CommentaryFacts.Invariant,
            // which is the one guard for this and the reason a
            // comma-decimal laptop no longer reads out "twelve thousand
            // three hundred forty-five seconds".
            badges <= 1
                ? OneToHandOut
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    SomeToHandOut,
                    CommentaryFacts.Invariant(badges, "0")),
        };

        var candidates = new List<string>(openings.Length * tails.Length);

        foreach (var opening in openings)
        {
            // The unnamed pool has no placeholder in it, so it is not put
            // through Format at all: a club who typed a brace into some
            // other field has no business reaching a format string here,
            // and the shortest way to be sure of that is not to call one.
            var said = reader.Length > 0
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, opening, reader)
                : opening;

            foreach (var tail in tails)
            {
                candidates.Add(said + tail);
            }
        }

        return voice.Pick(ThrowPool, candidates);
    }

    /// <summary>
    /// "Thank you, Jane. Well earned, all of them." - or null when the room
    /// has heard every way of saying it tonight.
    ///
    /// Crossed where there is a name to thank (four thanks by four
    /// additions) and a flat pool where there is not, because with nobody
    /// to thank there is nothing to cross the praise against and "Thank
    /// you." on its own would be the announcer thanking the room.
    /// </summary>
    private static string? HandBack(string reader, ContinuityVoice voice)
    {
        if (reader.Length == 0)
        {
            return voice.Pick(BackPool, HandBacks);
        }

        var candidates = new List<string>(ThanksByName.Length * WellEarneds.Length);

        foreach (var thanks in ThanksByName)
        {
            var said = string.Format(System.Globalization.CultureInfo.InvariantCulture, thanks, reader);

            foreach (var praise in WellEarneds)
            {
                candidates.Add(said + praise);
            }
        }

        return voice.Pick(BackPool, candidates);
    }

    /// <summary>
    /// "Nail-Biter", "Nail-Biter and Hat-Trick", "Nail-Biter, Hat-Trick and
    /// Clean Sweep" - a list as a person would say it, with the "and" where
    /// a comma would be a machine reading out an array.
    /// </summary>
    private static string AndList(IReadOnlyList<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };
}
