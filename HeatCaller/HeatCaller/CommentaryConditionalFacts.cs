namespace HeatCaller;

/// <summary>
/// ONE NAMED FINISHING ORDER, AND WHAT IT WOULD DO TO A CHAMPIONSHIP.
///
/// "If Carl wins this race and Bill finishes second, Carl takes the lead of
/// the Winter Series from Alex." That sentence, and nothing looser than
/// it.
///
/// WHY THE FLAT VERSION WAS REFUSED AND MUST STAY REFUSED (10 Oct 2026)
///
/// "A win here and he leads the championship" was asked for, written down,
/// and deliberately not built. It is not a shorter way of saying the line
/// above; it is a different and usually false claim. What a result does to
/// a table depends on where EVERYONE ELSE finishes, and the drop rule
/// ("best 5 of 7") means there is no arithmetic shortcut that survives it -
/// Championship.SwingFor recomputes the whole table twice rather than
/// subtracting and says so in its own comment. A conditional stated flatly
/// is true for one finishing order out of many, and said over the PA and
/// then falsified forty minutes later it is the timing system telling forty
/// people something untrue. Timing is the one thing a club trusts this app
/// with.
///
/// So the condition is NAMED, and both halves of it are in the sentence.
///
/// WHAT MAKES THIS RECORD DIFFERENT FROM EVERY OTHER ONE IN THIS LAYER
///
/// CommentaryShowFacts.NightThread states the rule the whole fact layer
/// runs on: THE SHAPE IS INVENTED, THE CLAIM INSIDE IT NEVER IS. Every
/// other record here is a count of something that has already happened - a
/// lap record, a field, a career, a run of race days. This one is a CLAIM
/// ABOUT A RACE THAT HAS NOT BEEN RUN, which is the one kind of fact this
/// layer had refused itself until now.
///
/// It is sayable for exactly one reason: it is proved true for EVERY
/// arrangement of the rest of the field before it is offered. Not likely,
/// not usually - every arrangement. CommentaryReaders.ChampionshipConditional
/// does that proof, over SeasonStore.ComputeStandings, the one place in
/// this app that applies the drop rule. Read its remarks before changing a
/// line of this file: THIS BUILDER CANNOT CHECK THE PROOF. It has no
/// stores, no season and no results, so it has no way of knowing whether
/// the record it was handed was proved or made up. A <see cref="RaceConditional"/>
/// built by anything other than that reader is a false sentence waiting to
/// be spoken, and there is no guard here that would catch it.
///
/// THE REFUSALS ARE THE FEATURE. The reader comes back null for a race
/// that counts towards no championship, for a season with too few rounds
/// counted for a conditional to mean anything, for a grid with fewer than
/// two of the table's drivers on it, for a hypothetical that changes
/// nothing, and - most often of all - for a claim it could not prove
/// universal. Every one of those is correct behaviour. Say nothing far
/// sooner than say something you cannot prove.
/// </summary>
/// <param name="SeasonName">
/// The championship, as the club named it. Said, because a lead is only a
/// lead in something - the same scope rule CommentaryRaceFacts states for
/// every record in it. Empty becomes "the championship".
/// </param>
/// <param name="RoundsCounted">
/// How many races the championship already counts, this one not included.
/// Not said out loud; it is in the key, so a table that has not moved
/// cannot offer the same sentence twice and a table that HAS moved offers
/// a fresh one. See <see cref="CommentaryConditionalFacts.Conditional"/>.
/// </param>
/// <param name="WinnerId">
/// The transponder of the driver the condition names as the WINNER - the
/// challenger, the one the sentence is about. Part of the theme, which is
/// the pair rather than either of them; see
/// <see cref="CommentaryConditionalFacts.Conditional"/>.
/// </param>
/// <param name="WinnerName">As it should be SAID - the announcer's name for them, so this line and the grid never name two different drivers.</param>
/// <param name="SecondId">The transponder of the driver the condition names as SECOND - the one the challenger has to finish ahead of.</param>
/// <param name="SecondName">As it should be said.</param>
/// <param name="WinnerPoints">
/// What the challenger's total would be on the named order. Not said out
/// loud - it is in the key, with the rounds counted and the margin, so the
/// sentence is offered once per distinct CLAIM rather than once per race or
/// once per night. A result corrected mid-evening moves this figure and the
/// next intro gets the new sentence; nothing moving it means nothing new to
/// say. See <see cref="CommentaryConditionalFacts.Conditional"/>.
/// </param>
/// <param name="DisplacedName">
/// Who the hypothetical takes the lead FROM, as it should be said - or
/// empty, which means the table's top was LEVEL ON POINTS before this race
/// and there is no one driver to credit with having led.
///
/// Empty is not a missing name, it is a judgement, and it is
/// ChampionshipLive.Headline's: level on points is level, whichever way a
/// tie-break falls. "Carl takes the lead from Alex" said of a table where
/// Alex and Dot were dead level credits Alex with something the standings
/// never gave him, in front of Dot.
/// </param>
/// <param name="LeadBy">
/// The margin the hypothetical leaves, in points - and ONLY when that
/// margin is the same in every arrangement of the rest of the field. Null
/// otherwise, and then the line says the lead changes hands and says no
/// number.
///
/// WHY A NUMBER IS A HARDER CLAIM THAN A LEAD. "Carl takes the lead" needs
/// Carl's hypothetical total to beat everybody's ceiling. "Carl leads by
/// three" needs the SECOND-placed total to be pinned as well, and that is
/// only pinned when no other driver in the race can reach past it - see
/// the reader. Where it is not pinned, the margin would be three on one
/// finishing order and eleven on another, and a figure that moves is
/// exactly the figure a driver disproves after the race.
/// </param>
public sealed record RaceConditional(
    string SeasonName,
    int RoundsCounted,
    string WinnerId,
    string WinnerName,
    string SecondId,
    string SecondName,
    int WinnerPoints,
    string DisplacedName = "",
    int? LeadBy = null);

/// <summary>
/// The championship conditional, worded for a race intro.
///
/// One builder and one sentence. Everything hard about this feature is in
/// CommentaryReaders.ChampionshipConditional, which proves the claim, and
/// in CommentaryService.BuildRoundIntroAsync, which tells the model it may
/// not shorten it. This only writes it down.
///
/// A table test, like every builder in this layer: nothing it touches
/// knows what a store is.
/// </summary>
public static class CommentaryConditionalFacts
{
    /// <summary>
    /// How many rounds a championship has to have counted before a
    /// conditional about it means anything.
    ///
    /// THREE, and the argument is not about confidence, it is about whether
    /// the sentence is worth its risk at all.
    ///
    /// With one round counted the table IS the last race's result, and "if
    /// Carl wins and Bill is second, Carl takes the lead" is a sentence
    /// about one race said before the second one. With two, a lead is still
    /// mostly whoever won more recently, and - the part that decides it -
    /// the drop rule cannot have bitten yet: no club runs a best-two-of-two,
    /// so every total is a straight sum and the conditional is arithmetic
    /// anybody in the room can do in their head while the cars are being
    /// put on the grid.
    ///
    /// That is the whole justification for this feature existing: it is
    /// arithmetic nobody in the room can do, about a table nobody has
    /// worked out, and it is the only reason a claim about a race that has
    /// not been run is worth making. Below three rounds it is not that, and
    /// a flourish that is not that is a risk taken for nothing. The same
    /// kind of judgement CommentaryShowFacts.LeastDaysOnFile makes about a
    /// club in its first week and CommentaryRaceFacts.LeastRacesForField
    /// makes about an entry.
    /// </summary>
    public const int LeastRoundsCounted = 3;

    /// <summary>
    /// The championship conditional, or null where there is nothing
    /// sayable.
    ///
    /// WHAT THE WORDING IS DOING, WHICH IS NOT DECORATION
    ///
    /// Half of this feature is stopping a model shortening it. A model
    /// handed "if Carl wins this race and Bill finishes second, Carl takes
    /// the lead of the Winter Series" will cheerfully offer "Carl can take
    /// the lead here", which is the one sentence that must never go out: it
    /// is true on one finishing order and false on most of the others, and
    /// the room cannot tell which it has been given.
    ///
    /// The prompt forbids it (see CommentaryService.BuildRoundIntroAsync)
    /// and the prompt is the belt. THIS IS THE BRACES, and it is the half
    /// that does not depend on a 3B model reading an instruction:
    ///
    ///   THE LINE ENDS ON A CLAUSE THAT CANNOT SURVIVE LOSING THE
    ///   CONDITION. "- worked out for that finishing order and no other."
    ///   Drop the two names and the sentence left over is "Carl takes the
    ///   lead of the Winter Series, worked out for that finishing order and
    ///   no other", which is not a plausible lie - it is a sentence with a
    ///   hole in it where its subject should be. A model that shortens this
    ///   line has nothing left to say, and an announcer reading a
    ///   non-sentence is a far better failure than a room believing a false
    ///   one.
    ///
    ///   AND THAT CLAUSE IS ITSELF TRUE, which is why it is worded about
    ///   the WORKING OUT and not about the world. The obvious armour - "and
    ///   on any other finishing order he does not" - is a claim, and a
    ///   false one: Carl winning with Bill THIRD very probably puts Carl
    ///   top as well, and nothing here has checked. Armour that lies is not
    ///   armour. What this app can say about the other orders is that it
    ///   has not worked them out, which is exactly what the clause says.
    ///
    /// THE CONDITION COMES FIRST, for the same reason. A trailing "if"
    /// invites a model to stop at the comma; a leading one makes the
    /// consequence grammatically dependent on it.
    /// </summary>
    /// <param name="story">The proved conditional - see <see cref="RaceConditional"/> for why the reader is its only honest source.</param>
    /// <param name="key">
    /// Which night, when the caller knows it.
    ///
    /// NOT THE NIGHT ON ITS OWN, AND NOT THE RACE EITHER, which is the one
    /// thing to get right here. The venue's record is the same all evening,
    /// so it is keyed on the night. The conditional is not: every race the
    /// championship counts moves the table and makes a different sentence
    /// true. Keyed on the night it would be said once and then be wrong for
    /// the rest of the evening; keyed on the race, a club running six heats
    /// that count towards nothing would hear the same unchanged sentence
    /// six times.
    ///
    /// So the key below carries the night AND the state the claim is made
    /// of - how many rounds are counted, the winner's hypothetical total,
    /// and the pair. A table that has not moved makes the same key and is
    /// said once. A race that counts, or a result corrected on the night,
    /// moves one of those figures and the next intro gets a fresh sentence
    /// because it is a fresh claim.
    /// </param>
    public static FactBlock? Conditional(RaceConditional story, string key)
    {
        // Judged here as well as in the reader, the way
        // CommentaryShowFacts.NoThread is: the builder keeps the promise
        // for a table test and the reader saves the work. What cannot be
        // judged here is the proof itself - see the class summary.
        if (story.RoundsCounted < LeastRoundsCounted)
        {
            return null;
        }

        var winner = CommentaryFacts.Clean(story.WinnerName);
        var second = CommentaryFacts.Clean(story.SecondName);

        // Two names or no sentence. And they have to be two: "if Carl wins
        // and Carl is second" is not a hypothetical, it is a bug reading
        // itself out over the PA. Matched on the name rather than the
        // transponder because the name is what the standings key on and
        // what the room hears.
        if (winner.Length == 0
            || second.Length == 0
            || string.Equals(winner, second, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var series = CommentaryFacts.CleanLong(story.SeasonName);
        var what = series.Length > 0 ? series : "championship";

        // Who is displaced, where the table had one leader to displace.
        // Empty is a judgement and not a gap - see RaceConditional.DisplacedName.
        var displaced = CommentaryFacts.Clean(story.DisplacedName);

        var takes = displaced.Length > 0 && !string.Equals(displaced, winner, StringComparison.OrdinalIgnoreCase)
            ? $"{winner} takes the lead of the {what} from {displaced}"
            : $"{winner} goes top of the {what}";

        // The margin only where the reader proved it is the same margin
        // whatever the rest of the field does. A decimal POINT whatever the
        // PC's region, like every other figure in this layer: on a
        // comma-decimal laptop a number formatted on the current culture
        // escapes the spoken-number rewrite and the PA reads it as
        // something else entirely. See CommentaryFacts.Invariant.
        var by = story.LeadBy is { } margin && margin > 0
            ? $" by {CommentaryFacts.Invariant(margin, "0")} {Plural.Word(margin, "point")}"
            : string.Empty;

        // The state the claim is made OF, written into the key: the rounds
        // counted, the total the sentence turns on, and the margin. See the
        // remarks on the key - this is what makes it once per CLAIM rather
        // than once per race or once per night.
        //
        // Formatted invariantly, as every figure in a key here is
        // (CommentaryShowFacts.Stage does the same): a key is never spoken,
        // but two spellings of one key is a repetition guard that does not
        // guard, and nothing would ever say so.
        var figures = System.Globalization.CultureInfo.InvariantCulture;

        var state = story.RoundsCounted.ToString(figures)
            + ":" + story.WinnerPoints.ToString(figures)
            + ":" + (story.LeadBy?.ToString(figures) ?? "x");

        return new FactBlock(
            $"ifwins:{key}:{state}:{CommentaryFacts.Pair(story.WinnerId, story.SecondId)}",

            // THE CONDITION FIRST, BOTH HALVES OF IT, AND A TAIL THAT
            // CANNOT LOSE IT. See the remarks - this wording is the
            // feature, not a way of phrasing it.
            $"If {winner} wins this race and {second} finishes second, {takes}{by}"
            + " - worked out for that finishing order and no other.",

            // THE PAIR, NOT EITHER DRIVER. The precedent is
            // CommentaryHistoryFacts' head-to-head, which carries the same
            // tag for the same reason stated there: a line about two
            // drivers against each other said once does not mean neither of
            // them may be talked about again. Cooled on the challenger
            // alone, a club whose championship leader happens to be on this
            // grid would lose every other line about him for a segment and
            // a half because the table put him in the top two.
            //
            // AND IT COLLIDES WITH SOMETHING, which is the test FactTheme's
            // own summary sets for a tag earning its place - the reason
            // there is still no "car" and no "thread". It meets the
            // head-to-head line about the same two drivers, which is in
            // THIS VERY PROMPT's history section, and it meets a battle
            // call about the same two later in the race
            // (CommentaryFacts.Battle). Both of those are the 10 Oct 2026
            // complaint exactly: the room has just been told what is
            // between Carl and Bill, and the next segment is about Carl and
            // Bill again.
            //
            // Through FactTheme.Rivalry, so the tag is the same whichever
            // way round the pair arrives - a cooldown filed under "A+B" and
            // offered back as "B+A" is a cooldown that never fires, and
            // nothing would ever say so.
            FactTheme.Rivalry(story.WinnerId, story.SecondId));
    }
}
