namespace HeatCaller;

/// <summary>
/// WHAT IS STILL POSSIBLE IN A CHAMPIONSHIP, with the rounds that remain
/// taken into account.
///
/// "With two rounds still to run, only Bill Hargrove and Sam Teague can
/// still win the Winter Series." "Bill Hargrove has already won it -
/// nobody left can reach him." That, and nothing looser.
///
/// WHY THIS IS A DIFFERENT CLAIM FROM EVERY OTHER ONE IN THIS LAYER
///
/// CommentaryConditionalFacts states the rule that made IT sayable: a
/// claim about a race that has not been run is only allowed when it has
/// been proved true for EVERY arrangement of the rest of the field. This
/// goes a step further again - it is a claim about every race that has not
/// been run, all the way to the end of the season - and it is allowed for
/// exactly the same reason and no other: CommentaryReaders.TitleOnTheLine
/// proves each half of it by playing the remaining rounds out at their
/// extremes through SeasonStore.ComputeStandings, the one place in this app
/// that applies the drop rule.
///
/// THIS BUILDER CANNOT CHECK THE PROOF. It has no stores, no season and no
/// results. A <see cref="TitleStakes"/> built by anything other than that
/// reader is a false sentence waiting to be spoken over a PA, and there is
/// no guard here that would catch it. Read the reader's remarks before
/// changing a line of this file.
///
/// WHY IT NEEDED A PLANNED SEASON LENGTH, which the app did not have until
/// 12 Oct 2026. Every sentence here is about the rounds REMAINING, and a
/// Season was a set of races that had happened plus a rule that took more
/// as they were recorded - so there was no remainder to reason about. The
/// ROADMAP recorded this as blocked for two days and that was correct; the
/// answer was to ask the club, because nothing could derive it. A season
/// whose length nobody has typed says none of this rather than guessing at
/// it. See Season.PlannedRounds.
/// </summary>
/// <param name="SeasonName">The championship as the club named it. Empty becomes "the championship".</param>
/// <param name="RoundsCounted">How many ROUNDS it already counts - race DAYS, not races; see CommentaryReaders.RoundsCountedSoFar, which is the difference. This race not included. In the key, so a table that has moved offers a fresh sentence.</param>
/// <param name="RoundsLeft">How many rounds remain INCLUDING the one about to run. One means the title is settled today.</param>
/// <param name="LeaderName">Who leads, as it should be SAID.</param>
/// <param name="LeaderId">Their transponder, for the theme - the claim is about them.</param>
/// <param name="LeaderPoints">Their total. Said, because "nobody can reach 148" is the shape of the sentence.</param>
/// <param name="LeadBy">
/// Points between the leader and the best-placed driver who can still WIN
/// it - not between them and whoever is second in the table, which is a
/// different driver on a season where the drop rule has shut a near rival
/// out. The copy names the contenders, so the margin has to be to one of
/// them; see the reader. Zero when the top two are level, which is worth
/// saying as itself. Where nobody can catch the leader at all there is no
/// contender and this is the gap to second, which the copy does not use.
/// </param>
/// <param name="CannotBeCaught">
/// PROVED: no other driver in the table can reach the leader even taking
/// maximum points in every remaining round while the leader takes none.
/// See the reader.
/// </param>
/// <param name="StillInIt">
/// Everybody who can still win it, leader first, as they should be said.
/// PROVED one driver at a time, each in their own best case.
/// </param>
public sealed record TitleStakes(
    string SeasonName,
    int RoundsCounted,
    int RoundsLeft,
    string LeaderName,
    string LeaderId,
    int LeaderPoints,
    int LeadBy,
    bool CannotBeCaught,
    IReadOnlyList<string> StillInIt);

/// <summary>
/// The stakes of a championship as a fact the announcer can be handed -
/// see <see cref="TitleStakes"/> for the whole argument about why a claim
/// this shape is allowed at all.
/// </summary>
public static class CommentaryStakesFacts
{
    /// <summary>
    /// The most drivers that can still win it and still be worth saying.
    ///
    /// Three. "Only Bill and Sam can win it" is the stakes of a season;
    /// "nine people can still win it" is the absence of stakes dressed up
    /// as a fact, and the standings segment already says where everybody
    /// is. So past this the reader is asked for nothing and this builder
    /// answers null - which, two rounds into a nine-round season, is the
    /// honest answer every time.
    /// </summary>
    public const int MostStillInIt = 3;

    /// <summary>
    /// The room this block needs in a prompt, and therefore the cap the
    /// intro gives it - CommentaryService.MaxStakesChars is this figure,
    /// so the two cannot drift apart.
    ///
    /// Four hundred, against a measured worst case of 319: the longest
    /// sentence <see cref="ForTheTitle"/> can build is "There are 999
    /// rounds of" plus a season name, three driver names and two totals,
    /// with every name capped at forty by CommentaryService.Field.
    ///
    /// WHY THE FIGURE MATTERS AT ALL. FactPack.Lay drops a block that will
    /// not fit WHOLE - there is no half a sentence - so a cap below the
    /// worst case does not shorten the claim, it deletes it, and the log
    /// still says the block was built. This shipped at 240 on 12 Oct 2026
    /// and a club with long driver names and a three-way title fight would
    /// have heard nothing about the championship on the one night it was
    /// worth hearing about. CommentaryStakesTests builds that worst case
    /// and pins it against this.
    /// </summary>
    public const int MostChars = 400;

    /// <summary>
    /// How few rounds a season can have counted and still have stakes
    /// worth stating.
    ///
    /// The same three CommentaryConditionalFacts.LeastRoundsCounted uses,
    /// and for the same reason it gives: a championship nobody has raced
    /// three rounds of is a scoreboard rather than a season, and "only two
    /// drivers can win it" after one round is arithmetic about nothing.
    /// </summary>
    public const int LeastRoundsCounted = CommentaryConditionalFacts.LeastRoundsCounted;

    /// <summary>
    /// What this championship still has riding on it, or null when there
    /// is nothing worth saying.
    ///
    /// Null for: a season too few rounds in, a title too open to be a
    /// story (see <see cref="MostStillInIt"/>), and a leader with no name
    /// to say. Every one of those is correct behaviour.
    /// </summary>
    /// <param name="stakes">Already proved by CommentaryReaders.TitleOnTheLine - this cannot check it.</param>
    /// <param name="key">The race's own key, so the sentence is offered once per race rather than once per night.</param>
    public static FactBlock? ForTheTitle(TitleStakes stakes, string key)
    {
        var leader = CommentaryFacts.Clean(stakes.LeaderName);

        if (leader.Length == 0 || stakes.RoundsCounted < LeastRoundsCounted)
        {
            return null;
        }

        // A round still to run is what every sentence here is about. With
        // none, the season is already over and this race counts towards
        // nothing - the standings segment is the right one for that.
        if (stakes.RoundsLeft < 1)
        {
            return null;
        }

        var series = CommentaryFacts.Clean(stakes.SeasonName);
        var named = series.Length > 0 ? series : "the championship";

        // THE ROUNDS LEFT, said the way a commentator says it. "The last
        // round" rather than "one round remaining", because that is the
        // whole point of it being the last one.
        var left = stakes.RoundsLeft == 1
            ? $"This is the last round of {named}."
            : $"There are {stakes.RoundsLeft} rounds of {named} left, this one included.";

        if (stakes.CannotBeCaught)
        {
            // THE ONLY CLAIM HERE THAT NAMES A CHAMPION, and the one that
            // most needs its proof: it is said in front of the drivers it
            // is about.
            var already = stakes.RoundsLeft == 1
                ? $"{leader} has already won {named} on {stakes.LeaderPoints} points - the last round cannot change it."
                : $"{leader} has already won {named} on {stakes.LeaderPoints} points. Nobody left can reach that, "
                  + $"even winning all {stakes.RoundsLeft} remaining rounds.";

            return new FactBlock($"stakes:{key}", $"{left} {already}", FactTheme.Driver(stakes.LeaderId));
        }

        var others = stakes.StillInIt
            .Select(CommentaryFacts.Clean)
            .Where(name => name.Length > 0 && !string.Equals(name, leader, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        // Nobody else in it and the leader catchable is a contradiction the
        // reader cannot produce - but "by construction" is not a check, and
        // a sentence listing one name as the people who can win it would be
        // the result.
        if (others.Count == 0 || others.Count + 1 > MostStillInIt)
        {
            return null;
        }

        // At most two others, because MostStillInIt is three - so this is
        // the whole of the list-making, rather than a general joiner for a
        // case that cannot arise.
        var who = others.Count == 1
            ? $"{leader} and {others[0]}"
            : $"{leader}, {others[0]} and {others[1]}";

        var margin = stakes.LeadBy == 0
            ? $"{leader} and {others[0]} are level on {stakes.LeaderPoints} points."
            : $"{leader} leads on {stakes.LeaderPoints} points, by {stakes.LeadBy}.";

        var onlyThese = stakes.RoundsLeft == 1
            ? $"It is between {who} today, and nobody else can take it."
            : $"Only {who} can still win it.";

        return new FactBlock($"stakes:{key}", $"{left} {onlyThese} {margin}", FactTheme.Driver(stakes.LeaderId));
    }
}
