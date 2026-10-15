namespace HeatCaller;

/// <summary>
/// What the second voice in the booth is LIKE.
///
/// WHY THIS EXISTS (11 Oct 2026)
///
/// The two-voice exchange already works: one request, split into turns,
/// read in two voices with a beat between them. But what it produced was
/// two people agreeing. A asks, B confirms. A says the gap is four tenths,
/// B says it certainly is. That is one commentator cut in half, not two
/// commentators.
///
/// Every broadcast pair that works, works because the two of them want
/// different things. One does facts and the other does feelings; one is
/// loyal and the other is cynical; one has been there thirty years and the
/// other arrived last season. The audience does not remember the lap time.
/// They remember that the second one never believes anything until it has
/// happened twice.
///
/// So a club picks a disposition for its co-host and the model is told it,
/// in the same breath as the two names. It costs nothing - no extra call,
/// no extra allowance, a sentence in a brief that was already being sent -
/// and it is the single cheapest change to how the show SOUNDS rather than
/// what it knows.
///
/// WHAT A STANCE IS NOT
///
/// It is not a licence to invent. The brief's last word is still that both
/// of them stick to the facts they were given, and every stance below is
/// written as a way of REACTING to those facts rather than as a source of
/// new ones. A sceptic who doubts a lap time does not get to claim the
/// timing loop misfired; he says he wants to see it again.
///
/// And it is not a licence to be unkind. This is a club of forty people
/// who all know each other, and a driver hearing the PA take a dig at him
/// in front of his kids is the fastest way for a club to switch the
/// announcer off. <see cref="NeverCruel"/> goes out with every stance,
/// including the needling one, and it is not optional.
///
/// WHY A TABLE AND NOT AN ENUM
///
/// Each stance carries two strings that have to be written down somewhere -
/// what a club sees on the page, and what the model is told - and the same
/// reasoning SoundPacks gives for its five packs applies: a name and a
/// description per entry means a table. The KEY is what is stored, so the
/// wording of either can be improved without a club's settings quietly
/// changing their co-host.
/// </summary>
internal static class CoHostStance
{
    /// <summary>
    /// One disposition the co-host can have.
    /// </summary>
    /// <param name="Key">What is stored in settings. Never changes once shipped, so the two strings below can.</param>
    /// <param name="Name">What the club picks it by on the Commentary page.</param>
    /// <param name="Blurb">The one-line explanation under the picker, in a club's words rather than a writer's.</param>
    /// <param name="Brief">What the model is told, in the second person. One or two sentences; any longer and it crowds out the facts.</param>
    internal sealed record Stance(string Key, string Name, string Blurb, string Brief);

    /// <summary>
    /// The sentence that goes out with EVERY stance, including the needling
    /// one.
    ///
    /// A broadcast pair can disagree about a car, a line, a strategy or a
    /// lap time all night. What they cannot do is say a driver is not much
    /// good, because this is a small room and that driver is standing in
    /// it. The distinction - needle the SITUATION, never the PERSON - is
    /// the whole of what keeps a bit of edge from becoming the reason a
    /// club turns the announcer off.
    /// </summary>
    internal const string NeverCruel =
        "Whatever they disagree about, neither of them is ever unkind about a driver: no mocking, no "
        + "doubting anyone's ability, nothing a driver would mind hearing over a PA with their family in "
        + "the room. The edge is always about the racing, never about the person.";

    /// <summary>
    /// The co-host with no stance at all - what every club has had until
    /// now, and what a club that wants a plain second voice keeps.
    ///
    /// Its Brief is EMPTY rather than a sentence saying "be even-handed":
    /// an instruction to have no character is still an instruction, and it
    /// spends words in the brief telling the model to do what it already
    /// does. Nothing added means nothing changed.
    /// </summary>
    internal static readonly Stance EvenHanded = new(
        string.Empty,
        "Even-handed",
        "No particular point of view - the second voice as it has always been.",
        string.Empty);

    /// <summary>
    /// Every stance a club can pick, in the order they are offered. The
    /// plain one first, because it is what a club already has.
    /// </summary>
    internal static readonly IReadOnlyList<Stance> All = new[]
    {
        EvenHanded,

        new Stance(
            "analyst",
            "Explains why",
            "Not what happened - why it happened. The one who tells you what the lap times mean.",

            // THE AXIS EVERY OTHER STANCE HERE MISSED (14 Oct 2026). Read
            // the six below: each gives the co-host somebody to root for or
            // a temperament to have - the underdog, the sceptic, the
            // archivist, the needler, the softie, the old hand. Every one
            // of them is a DISPOSITION, and a disposition is not a job.
            //
            // A broadcast pair is a caller and an ANALYST. The caller says
            // what is happening; the analyst says what it means and why it
            // would do that, and that second job is the one a club with a
            // second voice was still not getting. "Carl is quicker through
            // the infield" is a fact the app already has. "Carl is quicker
            // through the infield, and that is where this layout decides
            // itself" is the sentence a room stays quiet for.
            //
            // AND THE SENTENCE THAT MAKES IT SAFE is the third one. An
            // analyst that invents a reason is the worst failure available
            // to this whole layer: it would be telling a room why a driver
            // was slow, wrongly, about somebody standing in it. So the
            // brief says plainly that "nothing here says why" is a real
            // answer - the same refusal NightPromises makes about what the
            // voices said, and RecordWatch about what the next lap will do.
            "B's job is the reason rather than the result. A says what happened; B says what it means and "
            + "why it would do that, off the pattern in the facts above - the lap times, where a gap "
            + "changed, a difference of class or setup. Where the facts hold no reason B says so instead of "
            + "supplying one."),

        new Stance(
            "underdog",
            "Backs the underdog",
            "Delighted by anyone beating a faster car, and never that impressed by the favourite winning.",
            "B is the one in the booth who is always pulling for whoever is further down the order. B gets "
            + "genuinely excited about a driver running better than their car or their grid slot deserves, "
            + "and treats the fastest driver doing exactly what was expected as the least interesting thing "
            + "on the sheet. A is the one who keeps bringing it back to who is actually winning."),

        new Stance(
            "sceptic",
            "Takes nothing on one lap",
            "Wants to see it twice before it counts. The one who says \"let's see him do that again\".",
            "B does not accept one lap as proof of anything. A quick lap, a good start, a sudden gain - B "
            + "wants to see it repeated before calling it, and says so. B is never sour about it and never "
            + "questions the timing, just unconvinced until it happens again; and when it does happen "
            + "again, B says plainly that it has."),

        new Stance(
            "archivist",
            "Remembers everything",
            "Reaches for the night this happened before, and for the number behind what just happened.",
            "B is the one who remembers. Where a fact above gives a record, a previous meeting or a run of "
            + "results, B is the one who reaches for it and puts what just happened next to it. B never "
            + "invents a past race or a figure: if the facts above do not contain the history, B has "
            + "nothing to reach for and simply answers."),

        new Stance(
            "needler",
            "Winds up the front-runner",
            "Gently ribs whoever is out front, and enjoys it when the order gets shaken up.",
            "B gently winds up whoever is leading or has been dominating, in the affectionate way regulars "
            + "rib each other in the pits, and is openly pleased when the established order gets "
            + "disturbed. The ribbing is about the situation - the streak, the run of wins, the quiet "
            + "afternoon out front - and never about the driver."),

        new Stance(
            "softie",
            "Looks after the newcomers",
            "Notices the first-timer, the youngest driver and anyone who has just improved.",
            "B watches for the people the result sheet does not flatter: a first race, a youngest driver, a "
            + "personal best, somebody finally finishing a run clean. B brings those up where the facts "
            + "above support them, and makes a point of them rather than treating them as filler. Where "
            + "the facts hold nothing of that kind, B never manufactures one."),

        new Stance(
            "oldhand",
            "Been coming here for years",
            "Talks about this track and this club as somewhere they know, not somewhere they are visiting.",
            "B talks about this track and this club as somewhere they have been coming for years - the way "
            + "this surface behaves, what a night like this usually does, the kind of thing that tends to "
            + "happen in the closing laps here. B is specific only where the facts above are specific, and "
            + "never claims a memory of a race that is not in them."),
    };

    /// <summary>
    /// The stance a stored key means, or <see cref="EvenHanded"/> when it
    /// is blank, unknown, or from a newer version of the app.
    ///
    /// An unknown key is the plain co-host rather than a guess: a club
    /// whose settings came back from a backup written by a later build
    /// gets the second voice it has always had, not a character nobody in
    /// the room chose.
    /// </summary>
    internal static Stance For(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return EvenHanded;
        }

        var wanted = key.Trim();

        foreach (var stance in All)
        {
            if (stance.Key.Length > 0 && string.Equals(stance.Key, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return stance;
            }
        }

        return EvenHanded;
    }

    /// <summary>
    /// What goes into the exchange brief for this stance - the stance's own
    /// sentences and the kindness rule, or NOTHING at all for the plain
    /// co-host.
    ///
    /// Nothing, for the plain one, because a brief is a budget: every
    /// sentence in it is a sentence the model reads instead of a fact, and
    /// "these two have no particular character and are never unkind" buys
    /// a club with no stance set exactly nothing. The kindness rule rides
    /// with the stance because it is the stance that introduces any risk
    /// of an edge in the first place.
    /// </summary>
    internal static string BriefFor(string? key)
    {
        var stance = For(key);

        return stance.Brief.Length == 0 ? string.Empty : stance.Brief + " " + NeverCruel;
    }

    /// <summary>
    /// How the Commentary page says what is set, for the line under the
    /// picker. Names the stance and what it does, so a club reads the
    /// consequence rather than the label.
    /// </summary>
    internal static string Describe(string? key)
    {
        var stance = For(key);

        return stance.Brief.Length == 0
            ? "The second voice has no particular point of view - it asks and answers, which is how the "
              + "show has always sounded."
            : $"{stance.Name}: {stance.Blurb} The two of them will disagree more than they used to, which "
              + "is the point. Neither of them is ever unkind about a driver - that never changes, whatever "
              + "stance you pick.";
    }
}
