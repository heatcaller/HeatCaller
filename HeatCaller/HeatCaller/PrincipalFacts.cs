namespace HeatCaller;

/// <summary>
/// One driver's whole EVENING, as the Team Principal knows it.
///
/// WHY THERE IS A SECOND NIGHT RECORD WHEN DriverCut ALREADY BUILDS ONE
///
/// <see cref="DriverCut.FactsFor"/> walks the same races and is not what
/// this is. That one is written to be HANDED TO THE DRIVER: it moves
/// through their evening in order, name by name and lap time by lap time,
/// because the one reader it has was standing there and can check every
/// word of it.
///
/// A team principal is the opposite reader. He is asked one question in
/// front of a room, about a night he is accountable for, and the figures
/// he needs are the ones that add up ACROSS it - three finishes as a
/// shape rather than three sentences, the pace he had against the pace he
/// used, what the night cost him. So this is a handful of counted
/// aggregates rather than a narrative, and every field on it is here
/// because a principal would reach for it and the Technical Lead would
/// not.
///
/// FLAT, COUNTED, AND HANDED OVER - the rule <see cref="DriverForm"/>
/// states and the reason this file is a table test. Whoever fills it in
/// has the stores; <see cref="PrincipalFacts"/> words it and never goes
/// looking.
/// </summary>
/// <param name="DriverId">The transponder.</param>
/// <param name="Name">As the announcer says it - never a transponder.</param>
/// <param name="RacesRun">Races of theirs filed tonight. One means there is no night to speak of yet; see <see cref="PrincipalFacts.LeastRacesForANight"/>.</param>
/// <param name="Finishes">
/// Where they came in each of those races, IN THE ORDER THEY RAN.
///
/// A ZERO IS A RACE THEY DID NOT FINISH, which is
/// <see cref="DriverForm.RecentFinishes"/>' own convention and is kept
/// here rather than improved on: two spellings of "did not finish" across
/// two records in one layer is the sort of difference that agrees all
/// season and then disagrees about a retirement.
/// </param>
/// <param name="FieldTypical">
/// How many cars they were racing against on a normal race tonight - the
/// median of their races' fields.
///
/// Carried because "fourth" is a different night in a field of nineteen
/// than in a field of four, and the median rather than the largest so one
/// well-attended main does not flatter a night of thin heats.
/// </param>
/// <param name="Wins">Races of tonight's they won.</param>
/// <param name="Podiums">Races of tonight's they finished in the top three, wins included.</param>
/// <param name="Retirements">Races of tonight's they did not finish - a DNF or a disqualification, as the result records it.</param>
/// <param name="Warnings">
/// Warnings race control gave them tonight, across every race.
///
/// THE ONLY INCIDENT FIGURE IN HERE, and the reason is worth keeping: a
/// night's crashes are not in the record. SessionResultEntry.RejectedLaps
/// looked like the number and is not - it is zero both on a clean race
/// and on every race saved before the app counted them, and a principal
/// apologising on air for four crashes that were four old results would
/// be the timing system inventing an incident about somebody in the room.
/// Warnings ARE written down when they are given, so warnings are what
/// this counts and the prompt never asks him about crashes.
/// </param>
/// <param name="PaceIsReadable">
/// There were enough counted laps tonight for the two pace figures below
/// to mean anything - <see cref="PaceAnalysis.Row.Thin"/> over the night.
/// False and nothing about pace is said at all.
/// </param>
/// <param name="ConsistencyPercent">How much their lap times varied, as PaceAnalysis works it out, averaged over tonight's races.</param>
/// <param name="PotentialGainMs">
/// What the scrappy laps cost them over the whole night, in milliseconds
/// - PaceAnalysis' own per-race figure, summed across tonight's races.
///
/// A NIGHT TOTAL, NOT A PER-LAP ONE, which is worth saying because
/// PaceAnalysis' figure is already a sum: every lap's distance from that
/// driver's own best, added up. Summed again across races it is still a
/// total, and a line that called it "a second a lap" would be claiming
/// something several times larger than the arithmetic supports.
///
/// AND NEVER SPOKEN AS A TIME. See
/// <see cref="PrincipalFacts.WhereThePaceWent"/>: the principal is the
/// one voice in this broadcast that does not read times out, so the
/// figure comes in as a number of milliseconds and leaves as plain words.
/// </param>
/// <param name="Pronouns">
/// The driver's pronouns as their profile has them (21 Oct 2026), for the
/// sentences below that say "his" - see HeatCaller.Pronouns.For. Blank
/// reads as those sentences always did.
/// </param>
public sealed record TeamNight(
    string DriverId,
    string Name,
    int RacesRun,
    IReadOnlyList<int> Finishes,
    int FieldTypical,
    int Wins,
    int Podiums,
    int Retirements,
    int Warnings,
    bool PaceIsReadable,
    double ConsistencyPercent,
    long PotentialGainMs,
    string Pronouns = "");

/// <summary>
/// How one driver got on against ONE named rival tonight - the
/// evening's half of a head-to-head whose season half is
/// <see cref="HeadToHead"/>.
///
/// Separate from <see cref="TeamNight"/> because it is about a pair and
/// a team night is about one driver, and separate from
/// <see cref="HeadToHead"/> because that record counts everything the two
/// of them have ever raced and cannot be asked about one evening.
/// </summary>
/// <param name="RivalId">The other driver.</param>
/// <param name="Name">The rival, as the announcer says it.</param>
/// <param name="Ahead">Races tonight where our driver finished ahead of them.</param>
/// <param name="Behind">Races tonight where they finished ahead of our driver.</param>
public sealed record NightRival(string RivalId, string Name, int Ahead, int Behind);

/// <summary>
/// The facts that belong to the Team Principal and to nobody else in the
/// show.
///
/// WHY THIS FILE EXISTS (18 Oct 2026)
///
/// The Team Principal shipped on 17 Oct with the facts that were already
/// to hand: the race that had just run, the margin, the standings, the
/// team's other drivers. Every one of those is also in front of the
/// announcer, the recap and - for the race itself - the Technical Lead,
/// and a guest whose material the room has already heard twice is a guest
/// the room waits out. Two people saying the same thing in one programme
/// is one too many; three is a reason to turn the announcer off.
///
/// So the principal gets his own well, and the shape of it is the whole
/// idea: the Technical Lead has the setup sheets and one race, and the
/// principal never touches either. He gets the EVENING and the SEASON -
/// how the night added up, what the pace was worth, what it cost, where
/// it leaves them in the table, how they are doing against the one driver
/// they measure themselves by. None of that is in a setup sheet and none
/// of it is what a caller calls.
///
/// WHAT IS NOT IN HERE, AND WHY IT IS SOMEWHERE ELSE
///
/// Two of the four things a principal would reach for were already built,
/// and building them again here would have been the easiest mistake in
/// this file:
///
///   THE CHAMPIONSHIP is CommentaryService's StakesLines and
///   ChampionshipLines, which the principal's prompt already carries.
///   Those two have a note on them about predicted swings that this file
///   would have had to copy word for word to stay honest.
///
///   THE LAST FEW NIGHTS are CommentaryWeekFacts.OneDriver, which counts
///   the club's race nights rather than calendar weeks and knows that a
///   night a driver missed does not break their run. That is a subtlety
///   earned by being got wrong once, and a second implementation of it
///   would get it wrong again.
///
/// THE THRESHOLDS ARE THE DESIGN, exactly as
/// <see cref="CommentaryHistoryFacts"/> says of its own. A driver who has
/// run one race has not had a night; four-three in a head-to-head is not
/// a rivalry; a handful of laps is not a pace reading. Every builder here
/// returns null more often than it returns a fact, because a segment that
/// dresses up ordinary numbers as drama teaches a room that the
/// superlatives mean nothing.
/// </summary>
public static class PrincipalFacts
{
    /// <summary>
    /// How many races of tonight's it takes before there is an evening to
    /// talk about.
    ///
    /// Two. One race is the race the show has just covered: a "night" line
    /// about it would be the principal reading the result back to a room
    /// that watched it, in the one segment that exists to say something
    /// else. At two the shape starts to mean something - a driver who was
    /// fourth and then second has had a night going one way.
    /// </summary>
    public const int LeastRacesForANight = 2;

    /// <summary>
    /// How many times two drivers have to have met before the season's
    /// record is worth a sentence.
    ///
    /// Six, and higher than it looks because of what the line is FOR. The
    /// intro's rivalry line is colour; this one is a principal being asked
    /// to account for how his driver measures up against a named person in
    /// the room. Four-two is a fortnight's luck. At six the record has
    /// started to be about the drivers.
    /// </summary>
    public const int LeastMeetingsForARecord = 6;

    /// <summary>
    /// How lopsided a head-to-head may be and still be worth asking about.
    ///
    /// A principal asked about a rivalry his driver leads nine-one is
    /// being asked about something that is not a rivalry, and the only
    /// honest answers are smug or embarrassed. Four fifths is the point
    /// where the question stops being a question.
    /// </summary>
    public const double MostOneSidedRecord = 0.8;

    /// <summary>
    /// What the scrappy laps have to have cost before the pace line is
    /// worth saying, in milliseconds across the whole night.
    ///
    /// A second and a half, which is PaceAnalysis' own bar for when
    /// "tidying up beats finding more speed" IN ONE RACE. Borrowed as a
    /// night bar deliberately: a night whose total clears it has lost at
    /// least one race's worth of time to untidiness, which is the point
    /// where a principal has something real to answer for. Borrowed rather
    /// than invented so a club hearing him say the night went in the
    /// mistakes can open Pace Analysis and find it saying the same thing -
    /// two readings of the same laps disagreeing in public is worse than
    /// one of them being missing.
    /// </summary>
    public const long LeastCostWorthSaying = 1500;

    /// <summary>
    /// Lap-time variation at or below which a driver is running at their
    /// own limit - PaceAnalysis' own "tight", for the reason above.
    /// </summary>
    public const double TightConsistency = 1.5;

    /// <summary>
    /// How the night added up: every race of theirs, in the order they
    /// ran, as one line.
    ///
    /// THE ONE FACT THE TECHNICAL LEAD CANNOT HAVE. He is interviewed
    /// about a race, holding a setup sheet for a car; this is the evening,
    /// which is the principal's unit and nobody else's. It is also the
    /// line that makes the rest of the interview possible: a host asking
    /// "it has been an up-and-down night" needs somebody to have
    /// established that it was one.
    ///
    /// FINISHES AS A LIST, NOT AS A VERDICT. "Fourth, second, then the
    /// win" is the night; "an improving night" is a judgement, and the
    /// moment this file starts making those, it is the app telling a room
    /// how to feel about a driver standing in it. The model phrases the
    /// list. It is given nothing to grade.
    ///
    /// Null when there is no name, or when the driver has run fewer than
    /// <see cref="LeastRacesForANight"/> races tonight.
    /// </summary>
    /// <param name="night">What the caller counted. See <see cref="TeamNight"/>.</param>
    /// <param name="key">
    /// Which night, not which race. Everything in this line is true for
    /// the whole evening, so a race-scoped key would have a club running
    /// six heats hear the same sentence in all six - the argument
    /// CommentaryWeekFacts.OneDriver makes about its own key.
    /// </param>
    public static FactBlock? TheNight(TeamNight night, string key)
    {
        var who = CommentaryFacts.Clean(night.Name);

        if (who.Length == 0 || night.RacesRun < LeastRacesForANight || night.Finishes.Count == 0)
        {
            return null;
        }

        var placings = Placings(night.Finishes);

        if (placings.Length == 0)
        {
            return null;
        }

        var text = new System.Text.StringBuilder(
            $"{who}'s night so far, race by race in the order they ran: {placings}.");

        // HOW BIG THE FIELD WAS, once, at the end - not on every placing.
        // "Fourth, second and first, of about twelve" reads as a night;
        // "fourth of twelve, second of eleven, first of twelve" reads as a
        // results table, and the principal is the one guest who is not
        // reading one out.
        if (night.FieldTypical > 1)
        {
            text.Append($" Around {night.FieldTypical} cars in a race tonight.");
        }

        if (night.Wins > 0)
        {
            text.Append(night.Wins == night.RacesRun
                ? " They won every race they ran tonight."
                : $" {Plural.Of(night.Wins, "win")} out of {night.RacesRun} tonight.");
        }
        else if (night.Podiums > 0)
        {
            text.Append(
                $" {Plural.Of(night.Podiums, "top-three finish", "top-three finishes")} and no win tonight.");
        }

        return new FactBlock($"principal:night:{key}:{night.DriverId}", text.ToString(), FactTheme.Driver(night.DriverId));
    }

    /// <summary>
    /// What the night's pace was worth, and what it cost.
    ///
    /// THE SENTENCE THIS WHOLE FILE WAS WORTH BUILDING FOR. "He had the
    /// car to be on the podium and we put it in the fence twice" is the
    /// line a principal says and that nobody else in the broadcast can:
    /// the announcer calls what happened, the Technical Lead explains the
    /// car, and the gap between the pace a team HAD and the result it got
    /// is the principal's own subject and the thing he is accountable for.
    ///
    /// AND NOT ONE LAP TIME IN IT. PaceAnalysis hands over milliseconds
    /// and this hands on "most of a second a lap", deliberately: a
    /// principal who reads lap times out loud has become the announcer,
    /// and the announcer has already said them. The figure is turned into
    /// what it MEANT before the model ever sees it, which is also the only
    /// way to be sure the model does not round it wrongly on air.
    ///
    /// Null unless the laps support a reading and the night clears one of
    /// the two bars - the mistakes cost real time, or the driver was at
    /// their own limit all evening. A night in between those is an
    /// ordinary night, and a principal invited to account for an ordinary
    /// night says the ordinary thing.
    /// </summary>
    public static FactBlock? WhereThePaceWent(TeamNight night, string key)
    {
        var who = CommentaryFacts.Clean(night.Name);

        if (who.Length == 0 || !night.PaceIsReadable)
        {
            return null;
        }

        string text;

        // Their own pronouns when their profile has them (21 Oct 2026) -
        // the model follows what the facts say, so a fact that says "his"
        // about a driver who is "she" is a fact that misgenders her.
        var them = Pronouns.For(night.Pronouns);

        if (night.PotentialGainMs >= LeastCostWorthSaying)
        {
            text = $"{who} had more pace tonight than the results show: {them.Possessive} untidy laps cost {them.Object} about "
                   + $"{Spent(night.PotentialGainMs)} across {them.Possessive} races tonight, measured against {them.Possessive} own best lap. "
                   + "The pace was there; the repetition was not.";
        }
        else if (night.ConsistencyPercent is > 0 and <= TightConsistency)
        {
            text = $"{who} ran within {them.Reflexive} all night - lap after lap at {them.Possessive} own limit, with very little left on "
                   + "the table. Any more speed has to come from the car or the line, not from tidying up.";
        }
        else
        {
            return null;
        }

        // THEMED ON THE DRIVER so this and the night line above do not
        // both go out about the same person in one segment - the repetition
        // FactBlock.Theme exists to catch.
        return new FactBlock($"principal:pace:{key}:{night.DriverId}", text, FactTheme.Driver(night.DriverId));
    }

    /// <summary>
    /// What the night cost them: races they did not finish, and warnings
    /// they were given.
    ///
    /// WHY THERE IS NO WORD FOR "CRASH" ANYWHERE IN HERE, and it is the
    /// most important restraint in this file. A night's crashes are not
    /// written down - see the note on <see cref="TeamNight.Warnings"/> -
    /// and the figure that looks like them is zero on a clean race and
    /// zero on every result saved before the app counted it. So this says
    /// only the two things the record actually holds: that a race was not
    /// finished, and that race control gave a warning. Both are facts a
    /// driver can check, which is the test every line in this layer has to
    /// pass.
    ///
    /// AND IT IS NEVER ABOUT THE DRIVER. "He retired from two" is a
    /// result; "he threw two away" is a verdict about somebody standing in
    /// the room, and the principal's brief forbids him reaching for it -
    /// see <see cref="PrincipalStance.NeverHisOwnDriver"/>. This hands
    /// over the count and nothing else.
    ///
    /// Null on a night with neither, which is most nights.
    /// </summary>
    public static FactBlock? WhatItCost(TeamNight night, string key)
    {
        var who = CommentaryFacts.Clean(night.Name);

        if (who.Length == 0 || (night.Retirements == 0 && night.Warnings == 0))
        {
            return null;
        }

        var parts = new List<string>();

        if (night.Retirements > 0)
        {
            parts.Add(night.Retirements >= night.RacesRun && night.RacesRun > 0
                ? $"did not finish any of {Pronouns.For(night.Pronouns).Possessive} {Plural.Of(night.RacesRun, "race")} tonight"
                : $"did not finish {OfTonights(night.Retirements)}");
        }

        if (night.Warnings > 0)
        {
            parts.Add($"was given {Plural.Of(night.Warnings, "warning")} by race control tonight");
        }

        return new FactBlock(
            $"principal:cost:{key}:{night.DriverId}",
            $"{who} {string.Join(", and ", parts)}.",
            FactTheme.Driver(night.DriverId));
    }

    /// <summary>
    /// How they are doing against the one driver they measure themselves
    /// by - tonight, and across everything the two of them have raced.
    ///
    /// WHY A PRINCIPAL GETS THIS AND THE INTRO'S VERSION IS NOT ENOUGH
    ///
    /// CommentaryHistoryFacts.Rivalry writes the same record as colour
    /// before a race, and it is right for that: a room about to watch two
    /// cars go at each other wants to know the two of them are 7-6. This
    /// is a different question with the same numbers. A principal is being
    /// asked to ACCOUNT for the record - and the pairing of tonight
    /// against the season is what makes the question hard to answer
    /// blandly. Leading all season and beaten twice tonight is a thing a
    /// principal has to say something about.
    ///
    /// BOTH HALVES OR NEITHER. A season record with no "and tonight" is
    /// the intro's line again; tonight with no season is two races dressed
    /// up as a rivalry. So this needs the pair to have met tonight AND to
    /// have a season record that clears <see cref="LeastMeetingsForARecord"/>.
    ///
    /// Null when the record is too thin, too lopsided (see
    /// <see cref="MostOneSidedRecord"/>), or the two of them did not meet
    /// tonight.
    /// </summary>
    /// <param name="night">Our driver.</param>
    /// <param name="tonight">How the pair got on tonight.</param>
    /// <param name="season">
    /// How they have got on across everything, with <c>AId</c> either of
    /// them - this reads the record from our driver's side whichever way
    /// round the caller filled it in.
    /// </param>
    public static FactBlock? AgainstTheRival(TeamNight night, NightRival tonight, HeadToHead season, string key)
    {
        var who = CommentaryFacts.Clean(night.Name);
        var rival = CommentaryFacts.Clean(tonight.Name);

        if (who.Length == 0 || rival.Length == 0 || tonight.Ahead + tonight.Behind == 0)
        {
            return null;
        }

        // WHICHEVER WAY ROUND THE CALLER FILLED IT IN. HeadToHead is a
        // record about a pair and has no notion of whose side it is on, so
        // a builder that assumed AId was our driver would have printed
        // every record backwards for half the grid - silently, and in a
        // sentence naming two people in the room.
        var ours = string.Equals(season.AId, night.DriverId, StringComparison.OrdinalIgnoreCase);
        var ahead = ours ? season.AAhead : season.BAhead;
        var behind = ours ? season.BAhead : season.AAhead;
        var met = ahead + behind;

        if (met < LeastMeetingsForARecord)
        {
            return null;
        }

        if (Math.Max(ahead, behind) / (double)met > MostOneSidedRecord)
        {
            return null;
        }

        var record = ahead == behind
            ? $"level at {ahead} apiece"
            : ahead > behind
                ? $"{who} ahead {ahead}-{behind}"
                : $"{rival} ahead {behind}-{ahead}";

        var evening = tonight.Ahead == tonight.Behind
            ? $"they split tonight's {Plural.Of(tonight.Ahead + tonight.Behind, "meeting")}"
            : tonight.Ahead > tonight.Behind
                // No "him" (22 Oct 2026, bug hunt): our driver by their own
                // pronouns, the rival by name - theirs are not carried.
                ? $"{who} finished ahead of {rival} in {OfTonights(tonight.Ahead)}"
                : $"{rival} finished ahead of {Pronouns.For(night.Pronouns).Object} in {OfTonights(tonight.Behind)}";

        var text = $"{who} against {rival}, across everything the two of them have raced: {record}. And tonight, "
                   + $"{evening}.";

        return new FactBlock(
            $"principal:rival:{key}:{CommentaryFacts.Pair(night.DriverId, tonight.RivalId)}",
            text,
            FactTheme.Rivalry(night.DriverId, tonight.RivalId));
    }

    /// <summary>
    /// Everything true about this driver's night, in the order a principal
    /// would reach for it, ready to go into the prompt.
    ///
    /// THE ORDER IS THE INTERVIEW'S SPINE. The night first, because it is
    /// what the host's opening question is about; then the pace, which is
    /// the hard one; then what it cost; then the rivalry. A model given
    /// facts in the order the questions want them does not have to be told
    /// twice which is the important one.
    ///
    /// Empty is a real answer and happens on a thin night - one race, a
    /// handful of laps, nobody to measure against. The prompt that reads
    /// this asks a different set of questions when it comes back empty
    /// rather than inventing a night to fill them.
    /// </summary>
    /// <param name="rival">The driver they measure themselves by, and the pair's season record. Null when the caller found nobody.</param>
    public static IReadOnlyList<FactBlock> ForThePrincipal(
        TeamNight night,
        (NightRival Tonight, HeadToHead Season)? rival,
        string key)
    {
        var blocks = new List<FactBlock>();

        void Add(FactBlock? block)
        {
            if (block is not null)
            {
                blocks.Add(block);
            }
        }

        Add(TheNight(night, key));
        Add(WhereThePaceWent(night, key));
        Add(WhatItCost(night, key));

        if (rival is { } pair)
        {
            Add(AgainstTheRival(night, pair.Tonight, pair.Season, key));
        }

        return blocks;
    }

    /// <summary>
    /// The night's finishes as a spoken list - "4th, 2nd and 1st", with a
    /// race they did not finish said as what it was.
    ///
    /// A ZERO IS NOT A POSITION. DriverForm's convention puts a
    /// retirement in the list as zero, and "0th" over a PA is the sort of
    /// thing a room repeats back to you for a season. It is named instead,
    /// which also means the list and
    /// <see cref="WhatItCost"/> cannot disagree about whether a race was
    /// finished.
    /// </summary>
    private static string Placings(IReadOnlyList<int> finishes)
    {
        var said = finishes
            .Where(place => place >= 0)
            .Select(place => place == 0 ? "did not finish" : CommentaryFacts.Ordinal(place))
            .ToList();

        return said.Count switch
        {
            0 => string.Empty,
            1 => said[0],
            _ => string.Join(", ", said.Take(said.Count - 1)) + " and then " + said[^1],
        };
    }

    /// <summary>
    /// "one of tonight's races", "two of tonight's races" - a count of
    /// tonight's races where the noun is in the middle of the phrase.
    ///
    /// Written out here rather than through <see cref="Plural"/>, which
    /// pluralizes by adding an s to the end of what it is given and would
    /// have made "2 of tonight's racess" of this. The phrase has its noun
    /// in the middle, so it is not Plural's shape and pretending otherwise
    /// puts the fault on air.
    /// </summary>
    private static string OfTonights(int count) =>
        count == 1
            ? "one of tonight's races"
            : $"{CommentaryHistoryFacts.Spell(count)} of tonight's races";

    /// <summary>
    /// What a cost in milliseconds is WORTH, in the words a principal
    /// would use for it.
    ///
    /// ROUNDED DOWN, ALWAYS, which is the only rule here that matters.
    /// The figure is a sum of per-lap distances from a best lap, and
    /// "1.47 seconds" claims a precision that arithmetic has not earned -
    /// but more than that, a principal who overstates what the night cost
    /// is making his own driver's evening sound worse than the record
    /// says it was, on air, about somebody in the room. So the floor is
    /// taken at every step and the words stay vague in the direction of
    /// generosity.
    ///
    /// Half seconds below two, whole seconds above, because that is where
    /// a person stops counting halves.
    /// </summary>
    private static string Spent(long ms)
    {
        var halfSeconds = ms / 500;

        if (halfSeconds < 4)
        {
            return "a second and a half";
        }

        var seconds = (int)(halfSeconds / 2);

        return $"{CommentaryHistoryFacts.Spell(seconds)} seconds";
    }
}
