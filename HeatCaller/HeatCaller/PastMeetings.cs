namespace HeatCaller;

/// <summary>
/// The last time two drivers on this grid actually raced each other, and
/// what happened in it.
///
/// THE LONG CALLBACK (13 Oct 2026) - the item the broadcast plan called
/// "the one that makes an evening feel like a season". Everything else the
/// announcer knows about a rivalry is a COUNT: seven-one in eight
/// meetings, dead level at four apiece. True, and the thing nobody in the
/// room can work out for themselves - but it is arithmetic, and arithmetic
/// does not sound like memory. "The last time these two shared a front row
/// it ended in the fence" does.
///
/// A READER, AND NOT A STORE, and that was decided before this file
/// existed: <see cref="NightMoments"/>'s own summary names this feature as
/// the reason it refuses to be a store - "a store would have had moments
/// only for the nights after it shipped. This has them for every night
/// already on disk, which is exactly what the long callback needs and what
/// a store could never have given it." So this adds no file, no migration
/// and no settings, and a club that installs tonight gets callbacks about
/// races it ran two years ago.
///
/// WHAT IT DOES NOT DO, and this is the honest part. "It ended in the
/// fence" is NOT available and cannot be. The running order between
/// crossings is not kept (see SessionResultEntry.LapsLed), so the app
/// cannot know a car went off - only that it did not finish. The cause of
/// a retirement is recorded in exactly three places and all three are
/// typed by a human: Outcome ("DNF"/"DQ"), a correction's Reason, and the
/// race's Notes. Those are safe to QUOTE and unsafe to paraphrase into a
/// claim, so this file paraphrases nothing: it says the race, how long
/// ago, and - where the saved result proves one - one thing that actually
/// happened in it, in the words DriverCut already uses for that moment.
///
/// WHO FILLS IT IN. <see cref="HeadToHead.LastMetDescribed"/> has existed
/// since the rivalry line was written, is already spoken as "They last met
/// ...", and was populated by NOBODY - a parameter declared, consumed and
/// never set. CommentaryReaders.HeadToHeads already walks every shared
/// past race of a pair, with the right rule for whether they met at all,
/// and throws away WHICH races they were. So the matching stays there,
/// where the rule lives, and only the wording is here. Two
/// implementations of "did these two actually race each other" would be
/// two that eventually disagree.
/// </summary>
internal static class PastMeetings
{
    /// <summary>
    /// How far back a meeting may be and still be worth recalling.
    ///
    /// A little over a year, so last season counts and the one before it
    /// does not. Past this the line stops being memory and starts being
    /// trivia: "they last met in March 2024" about two drivers who have
    /// raced each other weekly since is a claim the room knows is wrong,
    /// and about two who genuinely have not is a sentence nobody wanted.
    /// </summary>
    internal static readonly TimeSpan StillWorthRecalling = TimeSpan.FromDays(400);

    /// <summary>
    /// The clause the rivalry line says, and the one thing that happened
    /// in that race - or null for it, which is the usual answer.
    /// </summary>
    /// <param name="Clause">"in the A-Main three weeks ago" - goes straight into "They last met ...".</param>
    /// <param name="Moment">One thing the saved result proves happened, already worded. Null when it proves nothing.</param>
    /// <param name="FrontRow">Whether they started that race first and second, in either order.</param>
    internal readonly record struct Recalled(string Clause, string? Moment, bool FrontRow);

    /// <summary>
    /// Words up the race two drivers last met in.
    ///
    /// The CALLER finds the race, because the caller already knows the rule
    /// for whether two drivers met in one - see the summary above. This is
    /// handed that race and turns it into words.
    ///
    /// Null when the race is too old to be worth recalling, or carries no
    /// date at all. Never null for want of a moment: the clause alone is
    /// worth saying.
    /// </summary>
    internal static Recalled? Describe(
        SessionRecord? race,
        string? aId,
        string? bId,
        Func<string, string?>? nameFor,
        DateTime nowUtc)
    {
        if (race is null || string.IsNullOrWhiteSpace(aId) || string.IsNullOrWhiteSpace(bId))
        {
            return null;
        }

        var ended = race.EndedAtUtc;

        // A race with no end time cannot be placed in the past, and "they
        // last met" with no when is the half of the sentence that was
        // worth having.
        if (ended == default)
        {
            return null;
        }

        if (nowUtc - ended > StillWorthRecalling)
        {
            return null;
        }

        // The moment first, because whether there is one decides whether
        // the clause names the race. DriverCut.Describe already ends its
        // wording with "in the A-Main", so a clause that also said it
        // would have the room told the label twice in one breath.
        var moment = WhatHappened(race, aId, bId, nameFor);

        return new Recalled(
            Clause(race, ended, nowUtc, withLabel: moment is null),
            moment,
            SharedAFrontRow(race, aId, bId));
    }

    /// <summary>
    /// "in the A-Main three weeks ago", or just "three weeks ago".
    ///
    /// Reads as the tail of "They last met ...", which is the sentence it
    /// exists for - see CommentaryHistoryFacts.Rivalry.
    /// </summary>
    /// <param name="withLabel">
    /// False when a moment is going to name the race anyway - see
    /// <see cref="Describe"/>. The label is worth having in the clause
    /// only when it is the one place it appears.
    /// </param>
    internal static string Clause(SessionRecord race, DateTime endedUtc, DateTime nowUtc, bool withLabel = true)
    {
        var ago = HowLongAgo(endedUtc, nowUtc);

        if (!withLabel)
        {
            return ago;
        }

        var label = CommentaryFacts.Clean(race.Label);

        return label.Length > 0 ? $"in {label} {ago}" : ago;
    }

    /// <summary>
    /// How long ago, as a club would say it rather than as a date.
    ///
    /// A DATE IS THE WRONG ANSWER HERE. "They last met on 21 September" is
    /// a fact the room has to do arithmetic on to feel anything about; "a
    /// month ago" is the same fact already felt. The other segments that
    /// name a date are naming an anniversary, where the date IS the point.
    ///
    /// Vague on purpose at the long end: "four months ago" for anything
    /// between about fifteen and eighteen weeks is close enough for a
    /// sentence said out loud, and pretending to more precision than the
    /// room can check is how a true line sounds made up.
    /// </summary>
    internal static string HowLongAgo(DateTime thenUtc, DateTime nowUtc)
    {
        var apart = nowUtc - thenUtc;

        // A race from the future is a clock that has been put back, and
        // there is no honest wording for it - treated as just now, which
        // is the one reading that cannot be wrong by much.
        if (apart <= TimeSpan.Zero)
        {
            return "earlier today";
        }

        var days = (int)apart.TotalDays;

        if (days < 1)
        {
            return "earlier today";
        }

        if (days == 1)
        {
            return "yesterday";
        }

        if (days < 7)
        {
            return "a few days ago";
        }

        if (days < 14)
        {
            return "last week";
        }

        if (days < 35)
        {
            return Plural.Of(days / 7, "week") + " ago";
        }

        if (days < 365)
        {
            return Plural.Of(Math.Max(1, days / 30), "month") + " ago";
        }

        return "last season";
    }

    /// <summary>
    /// Whether these two started that race first and second, in either
    /// order - what makes "the last time these two shared a front row" a
    /// thing the app may say.
    ///
    /// GATED ON RecordsIncidents, because a race saved before the grid was
    /// recorded has StartPosition zero for everybody, and zero there means
    /// UNKNOWN rather than "did not start on the front row". Without this
    /// gate every old race would answer no, which is harmless, and any
    /// future change to the default would answer yes for all of them,
    /// which is not. NightMoments.Charge guards the same field the same
    /// way.
    /// </summary>
    internal static bool SharedAFrontRow(SessionRecord race, string? aId, string? bId)
    {
        if (!race.RecordsIncidents)
        {
            return false;
        }

        var a = Rowed(race, aId)?.StartPosition ?? 0;
        var b = Rowed(race, bId)?.StartPosition ?? 0;

        return a is 1 or 2 && b is 1 or 2 && a != b;
    }

    /// <summary>
    /// One thing the saved result PROVES happened in that race, about one
    /// of these two - or null, which is the usual answer and is correct.
    ///
    /// Through <see cref="NightMoments.ForRace"/>, which is the app's one
    /// answer to "what happened in this race", and worded by
    /// <see cref="DriverCut.Describe"/>, which is its one answer to "say
    /// that out loud". Neither is reimplemented here: two wordings for one
    /// moment are two wordings that drift, which is the reason Describe is
    /// internal in the first place.
    ///
    /// ABOUT ONE OF THESE TWO, not whatever the race's best moment was.
    /// That moment may belong to a driver who is not on tonight's grid,
    /// and a callback about a rivalry that then names a third person is a
    /// sentence that has lost its subject.
    ///
    /// A FASTEST LAP IS NOT A MEMORY, and this was wrong first time round
    /// (13 Oct 2026). Every race has a fastest lap, so NightMoments always
    /// produces one - which meant this reached for the weakest thing
    /// available and a callback came out as "they last met three weeks
    /// ago, and Bill set the fastest lap in Heat 2". True, and it says
    /// nothing: the same shape of sentence would be available about every
    /// race ever run, which is the exact repetition the depth plan was
    /// written against. Something has to have HAPPENED.
    ///
    /// NightMoments.OfTheNight had already made this judgement in those
    /// words and this now follows it rather than making it again. A race
    /// whose only moment is its fastest lap proves nothing, and then the
    /// clause names the race instead - which is the honest answer and is
    /// most races.
    ///
    /// STRONGEST FIRST, through Over rather than ForRace: Over ranks, and
    /// its ranking is the app's own judgement about which moment is worth
    /// most - a last-lap lead ahead of a photo finish ahead of a charge.
    /// Taking whichever came out of the walk first would have had a
    /// mid-race lead change beat a photo finish by accident of discovery
    /// order.
    ///
    /// NO LAYOUT STORE, so the one moment that needs one - the outright
    /// lap record falling - is not among these. Deliberate: the record
    /// falling is its own segment with its own facts, and threading a
    /// store through the rivalry reader to reach it would buy a duplicate.
    /// </summary>
    internal static string? WhatHappened(
        SessionRecord race,
        string? aId,
        string? bId,
        Func<string, string?>? nameFor)
    {
        var moments = NightMoments.Over(new[] { race });

        if (moments.Count == 0)
        {
            return null;
        }

        foreach (var moment in moments)
        {
            // See the summary: every race has a fastest lap, so it is the
            // one moment that cannot tell two races apart.
            if (moment.What == NightMoments.Kind.FastestLap)
            {
                continue;
            }

            if (!Is(moment.Transponder, aId) && !Is(moment.Transponder, bId))
            {
                continue;
            }

            // Their name as the club has it now, falling back to the name
            // the race was saved with - a driver renamed since is still
            // the person in the room.
            var named = CommentaryFacts.Clean(
                nameFor?.Invoke(moment.Transponder) is { Length: > 0 } known ? known : moment.DriverName);

            if (named.Length == 0)
            {
                continue;
            }

            if (DriverCut.Describe(moment, named) is { Length: > 0 } said)
            {
                return said;
            }
        }

        return null;
    }

    /// <summary>A driver's row in a saved race, or null when they were not in it.</summary>
    private static SessionResultEntry? Rowed(SessionRecord race, string? transponder) =>
        string.IsNullOrWhiteSpace(transponder)
            ? null
            : race.Results.FirstOrDefault(row => Is(row.Transponder, transponder));

    private static bool Is(string? one, string? other) =>
        !string.IsNullOrWhiteSpace(one)
        && !string.IsNullOrWhiteSpace(other)
        && string.Equals(one.Trim(), other.Trim(), StringComparison.OrdinalIgnoreCase);
}
