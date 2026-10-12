namespace HeatCaller;

/// <summary>
/// Who the line on air is about - the names that go on the band at the
/// bottom of the crowd's screen beside what is playing.
///
/// WHY (11 Oct 2026)
///
/// The Big Screen and the announcer barely knew about each other. The band
/// said what was on air - ON AIR │ BATTLE WATCH - and the board underneath
/// it went on cycling the standings on its own timer, so when the voice
/// said "Hargrove, three tenths back off Bellamy" the screen was showing
/// row seven of a twelve-car table and whoever was watching had to find
/// the two names for themselves.
///
/// Putting those two names up AT THAT MOMENT is the single most
/// demonstrable thing on this whole feature list. It is what makes a
/// twenty-second phone video of a club's screen look like television
/// instead of a scoreboard, and a club that can film its own screen is a
/// club that sells HeatCaller to the next club.
///
/// HOW IT KNOWS WHO IS BEING TALKED ABOUT
///
/// It reads the spoken line. The alternative - having each segment declare
/// its subjects - sounds tidier and is worse: the facts go to a model and
/// what comes back is the model's sentence, so a segment that was HANDED
/// three drivers may well have named one of them. The band has to agree
/// with what the room is hearing, not with what the app intended, and the
/// only thing that knows what the room is hearing is the line itself.
///
/// WHAT IT WILL NOT DO
///
/// Guess. A name goes up only where the spoken line contains it, whole,
/// with nothing but non-letters either side - so "Sam" is not found inside
/// "Samantha", and a club with a driver called Art does not get a caption
/// every time the announcer says "started". Nothing is inferred from a
/// position, a car number or a nickname the club has not typed.
///
/// A PURE FUNCTION over text and a list of names, so it can be tested
/// without a screen, a race or a voice.
/// </summary>
internal static class LowerThird
{
    /// <summary>
    /// How many names the band will carry.
    ///
    /// Three. The band is one line across the bottom of a screen that is
    /// mostly leaderboard, and a recap that names eight finishers would
    /// turn it into a second leaderboard in a smaller font - which is the
    /// one thing it must not become. Two is the common case and the one
    /// this is really for: a battle, a pair, a gap.
    /// </summary>
    internal const int MostNames = 3;

    /// <summary>
    /// The shortest a name may be and still be looked for.
    ///
    /// Three letters. A club with a driver entered as "Al" or "Jo" would
    /// otherwise have the band light up on half the sentences in the
    /// evening - "also", "job", "join" - because a two-letter name sits
    /// inside far too many English words for the boundary check to save
    /// it. Their name is simply not matched, which costs them a caption
    /// and never puts a wrong one up.
    /// </summary>
    internal const int ShortestName = 3;

    /// <summary>
    /// One driver the band can name: what the announcer called them, and
    /// what the screen should show.
    ///
    /// TWO NAMES, because they are often different and both are needed.
    /// The announcer reads <see cref="DriverProfile.SpokenName"/>, which is
    /// the club's Announcer name where they have typed one - "Billy Bob" -
    /// so that is what has to be LOOKED FOR in the line. The screen shows
    /// the driver's own name, which is what the board beside the band shows
    /// and what the room reads everywhere else. A band that said "Billy
    /// Bob" above a board row saying "Bill Hargrove" would read as two
    /// drivers.
    /// </summary>
    /// <param name="Said">What the announcer would call them - matched against the line.</param>
    /// <param name="Shown">What the screen calls them - what goes on the band.</param>
    internal sealed record Candidate(string Said, string Shown);

    /// <summary>
    /// The drivers this line names, in the order the line names them, at
    /// most <see cref="MostNames"/> of them.
    ///
    /// IN THE ORDER THE LINE SAYS THEM, which is not the order of the
    /// standings and not the order the candidates arrive in. "Hargrove is
    /// all over the back of Bellamy" is about Hargrove first whatever the
    /// board says, and a band that reversed them would be contradicting
    /// the sentence the room just heard.
    ///
    /// LONGEST CANDIDATE FIRST, so a club with both "Bill" and "Bill
    /// Hargrove" on the books gets the longer one where the line used it,
    /// rather than matching "Bill" inside it and putting the wrong driver
    /// up. The same rule CommentaryService.CalmShouting keeps for the same
    /// reason.
    ///
    /// Empty whenever there is nothing honest to show: no line, no
    /// candidates, or a line that names nobody - which is most lines in a
    /// recap of a twelve-car race and all of a continuity announcement.
    /// The band then says what is playing and no more, exactly as it did
    /// before any of this.
    /// </summary>
    internal static IReadOnlyList<string> NamesIn(
        string? line,
        IReadOnlyList<Candidate>? candidates,
        int most = MostNames)
    {
        if (string.IsNullOrWhiteSpace(line) || candidates is null || candidates.Count == 0)
        {
            return Array.Empty<string>();
        }

        var cap = Math.Max(1, most);

        // Where each candidate was found, longest name first so a shorter
        // name inside a longer one is never the match.
        var hits = new List<(int At, string Shown)>();
        var taken = new List<(int Start, int End)>();

        foreach (var candidate in candidates
                     .Where(one => Trimmed(one.Said).Length >= ShortestName && Trimmed(one.Shown).Length > 0)
                     .OrderByDescending(one => Trimmed(one.Said).Length))
        {
            var said = Trimmed(candidate.Said);
            var shown = Trimmed(candidate.Shown);

            var at = IndexOfWhole(line, said, taken);

            if (at < 0)
            {
                continue;
            }

            taken.Add((at, at + said.Length));
            hits.Add((at, shown));
        }

        return hits
            .OrderBy(hit => hit.At)
            .Select(hit => hit.Shown)

            // ONE ROW PER PERSON. A club that has typed the same driver
            // name against two transponders - a spare car, a transponder
            // handed over mid-season - would otherwise get them on the
            // band twice.
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(cap)
            .ToList();
    }

    /// <summary>
    /// Where <paramref name="name"/> appears in <paramref name="line"/> as
    /// a whole word, not inside a longer one and not inside a stretch some
    /// longer name has already claimed - or -1.
    ///
    /// WHOLE WORD MEANS LETTERS AND DIGITS EITHER SIDE, nothing else. A
    /// name at the start or the end of the line counts, as does one
    /// followed by a comma, a full stop, an apostrophe-s or a dash - all of
    /// which is how a name actually appears in a spoken sentence
    /// ("Hargrove's", "Bellamy,"). What it stops is the thing that would
    /// otherwise put a wrong driver on the wall: "Sam" inside "Samantha",
    /// "Art" inside "started", "Ed" inside "edged".
    ///
    /// Case is ignored, because a model writes a name however it likes and
    /// the club typed it once.
    /// </summary>
    private static int IndexOfWhole(string line, string name, IReadOnlyList<(int Start, int End)> taken)
    {
        var from = 0;

        while (from <= line.Length - name.Length)
        {
            var at = line.IndexOf(name, from, StringComparison.CurrentCultureIgnoreCase);

            if (at < 0)
            {
                return -1;
            }

            var end = at + name.Length;

            var clear = (at == 0 || !IsNameish(line[at - 1]))
                        && (end >= line.Length || !IsNameish(line[end]));

            if (clear && !Overlaps(at, end, taken))
            {
                return at;
            }

            from = at + 1;
        }

        return -1;
    }

    /// <summary>
    /// Whether a stretch of the line has already been claimed by a longer
    /// name.
    ///
    /// The case this exists for: a club with "Bill" and "Bill Hargrove"
    /// both on the books, and a line that says "Bill Hargrove". The longer
    /// name matches first and claims those letters; without this, "Bill"
    /// would then match inside what is already Bill Hargrove's name and
    /// the band would show two drivers where the line named one.
    /// </summary>
    private static bool Overlaps(int start, int end, IReadOnlyList<(int Start, int End)> taken)
    {
        foreach (var (from, to) in taken)
        {
            if (start < to && from < end)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether this character could be part of a name - a letter or a
    /// digit, in any alphabet.
    ///
    /// Letters AND digits, because a club's driver names include things
    /// like "Dave 2" and a decoder id can end up as a name on a profile
    /// with nothing typed in it. An apostrophe is deliberately NOT
    /// name-ish: "Hargrove's line" has to match Hargrove.
    /// </summary>
    private static bool IsNameish(char c) => char.IsLetterOrDigit(c);

    private static string Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    /// <summary>
    /// The names as the band writes them - "Hargrove · Bellamy" - or empty
    /// where there are none.
    ///
    /// A middle dot rather than a comma: a comma inside a caption reads as
    /// part of a sentence, where this is a pair of labels. The same
    /// separator the Announcer screen's own strip uses.
    /// </summary>
    internal static string Line(IReadOnlyList<string>? names) =>
        names is null || names.Count == 0 ? string.Empty : string.Join(" · ", names);
}
