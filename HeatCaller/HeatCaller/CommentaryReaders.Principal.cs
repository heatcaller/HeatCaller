namespace HeatCaller;

/// <summary>
/// The stores read for the TEAM PRINCIPAL - his evening and his season,
/// counted off the club's own saved races.
///
/// WHY IT IS A READER AND NOT PART OF THE PROMPT (18 Oct 2026)
///
/// The split this file sits on is the one <see cref="DriverForm"/> states:
/// the readers walk the stores and hand over something flat, and the fact
/// builders word it and never go looking. <see cref="PrincipalFacts"/> is
/// a table test because of this file, and this file is allowed to be as
/// slow as it likes because of that.
///
/// It is in <see cref="CommentaryReaders"/> rather than beside
/// PrincipalFacts because of what it needs: <c>Placing</c>, <c>RacesIn</c>
/// and <c>MetAcross</c>, the three private rules that decide what counts
/// as a finish, which races belong to a class, and when two drivers have
/// met. A second answer to any of those is a second answer the app would
/// eventually give out loud - see the note on <c>MetAcross</c>.
/// </summary>
public static partial class CommentaryReaders
{
    /// <summary>
    /// How two drivers have got on across a set of races, and which of
    /// them was the last they both finished.
    ///
    /// THE ONE IMPLEMENTATION OF "THESE TWO MET", extracted on 18 Oct 2026
    /// from <c>HeadToHeads</c>, whose own comment said why this had to
    /// happen before anything else asked the same question: two
    /// implementations of the rule are two that eventually disagree about
    /// a seven-one record, and the disagreement is discovered by a room
    /// hearing both numbers in one evening.
    ///
    /// ONLY WHEN BOTH ACTUALLY FINISHED. A retirement says nothing about
    /// who is quicker, and counting it as a win for the other one is how a
    /// seven-one record gets built out of three races and four
    /// breakdowns.
    /// </summary>
    /// <param name="races">The races to count over, NEWEST FIRST - what RacesIn returns - so the first qualifying race is their most recent meeting.</param>
    private static (int AAhead, int BAhead, SessionRecord? LastMet) MetAcross(
        IEnumerable<SessionRecord> races, string a, string b)
    {
        var aAhead = 0;
        var bAhead = 0;
        SessionRecord? lastMet = null;

        foreach (var race in races)
        {
            var placedA = Placing(race, a) ?? 0;
            var placedB = Placing(race, b) ?? 0;

            if (placedA <= 0 || placedB <= 0)
            {
                continue;
            }

            lastMet ??= race;

            if (placedA < placedB)
            {
                aAhead++;
            }
            else if (placedB < placedA)
            {
                bAhead++;
            }
        }

        return (aAhead, bAhead, lastMet);
    }

    /// <summary>
    /// One driver's whole evening and the rival they measure themselves
    /// by, for the Team Principal's interview.
    ///
    /// Null when there is nobody to ask about, no name to say, or no races
    /// of theirs filed tonight - which is the honest answer before a
    /// club's first race of the evening and the one the prompt is written
    /// to cope with.
    ///
    /// THE RIVAL IS CHOSEN BY THE RECORD, NOT BY THE CLUB. RivalryStore
    /// holds the pairs a club has written voice lines for, and it would
    /// have been the obvious place to look - but it is a list of lines to
    /// fire on an overtake, not a statement about who a driver measures
    /// themselves against, and most clubs have none at all. So the rival
    /// is whoever this driver has raced most often: the pair with the
    /// longest shared history among tonight's opponents. That also means
    /// the choice is the same every time it is asked, which a club setting
    /// would not have been.
    ///
    /// NO THRESHOLDS IN HERE. Whether six meetings is a record, and
    /// whether nine-one is too lopsided to ask about, are
    /// <see cref="PrincipalFacts"/>' judgements and are made there. This
    /// hands over what it counted.
    /// </summary>
    /// <param name="transponder">The driver whose principal is being interviewed.</param>
    /// <param name="className">The class to count the season record over; null or blank for everything the club has run.</param>
    /// <param name="nowLocal">The club's wall clock, for which night is tonight. Null for now; passed in by the tests.</param>
    public static (TeamNight Night, (NightRival Tonight, HeadToHead Season)? Rival)? TheTeamNight(
        string? transponder,
        string? className,
        ProfileStore profiles,
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        DateTime? nowLocal = null)
    {
        var who = (transponder ?? string.Empty).Trim();

        if (who.Length == 0)
        {
            return null;
        }

        var name = Typed(profiles.Get(who)?.SpokenName);

        // A driver with no profile has no name to say - the same answer
        // GridHistory and PaceTonight give, for the same reason.
        if (name.Length == 0)
        {
            return null;
        }

        var wanted = (className ?? string.Empty).Trim();

        // TONIGHT'S RACES THROUGH TonightAtTheTrack, never filtered again
        // here. Its night runs 6am to 6am and it is the same answer every
        // pace figure in the app is read from; a second answer to "which
        // races were tonight" is the sort of thing that agrees all season
        // and then disagrees about a race that started at five to
        // midnight. Oldest first, which is what Races returns and what the
        // night's shape needs.
        var tonight = TonightAtTheTrack
            .Races(history.All, id => rounds.Get(id), nowLocal ?? DateTime.Now)
            .Where(race => wanted.Length == 0
                           || string.Equals(race.ClassName, wanted, StringComparison.OrdinalIgnoreCase))
            .Select(race => race.Session)
            .ToList();

        var theirs = tonight
            .Select(race => (Race: race, Placed: Placing(race, who)))
            .Where(x => x.Placed is not null)
            .ToList();

        if (theirs.Count == 0)
        {
            return null;
        }

        var finishes = theirs.Select(x => x.Placed!.Value).ToList();

        var fields = theirs.Select(x => RealCars(x.Race)).Where(count => count > 0).OrderBy(count => count).ToList();

        // ---------------------------------------------------------------
        //  The pace, through PaceAnalysis rather than counted again here
        // ---------------------------------------------------------------
        //
        // One reading of a night's laps, for the reason
        // PrincipalFacts.LeastCostWorthSaying gives: a club hearing the
        // principal say the night went in the mistakes can open Pace
        // Analysis and find it saying the same thing.
        //
        // ONLY THE RACES WITH ENOUGH LAPS TO READ. A two-lap heat
        // contributes a consistency figure of nothing-in-particular, and
        // averaging it in would drag a real reading towards it - so a
        // thin race is left out of both figures rather than counted as a
        // tidy one.
        var pace = theirs
            .Select(x => x.Race.Results.FirstOrDefault(r =>
                string.Equals(r.Transponder, who, StringComparison.OrdinalIgnoreCase)))
            .Where(entry => entry is not null)
            .Select(entry => PaceAnalysis.For(entry!))
            .Where(row => !row.Thin)
            .ToList();

        var warnings = theirs
            .Select(x => x.Race.Results.FirstOrDefault(r =>
                string.Equals(r.Transponder, who, StringComparison.OrdinalIgnoreCase)))
            .Sum(entry => entry?.Warnings?.Count ?? 0);

        var night = new TeamNight(
            who,
            name,
            theirs.Count,
            finishes,
            fields.Count > 0 ? fields[fields.Count / 2] : 0,
            finishes.Count(place => place == 1),
            finishes.Count(place => place is >= 1 and <= 3),
            finishes.Count(place => place == 0),
            warnings,
            pace.Count > 0,
            pace.Count > 0 ? pace.Average(row => row.ConsistencyPercent) : 0,
            pace.Sum(row => row.PotentialGainMs),
            profiles.Get(who)?.Pronouns ?? string.Empty);

        return (night, TheRival(who, name, theirs, wanted, profiles, history, rounds));
    }

    /// <summary>
    /// The driver this one measures themselves by, with tonight's record
    /// and the season's.
    ///
    /// Whoever they have the longest shared history with among tonight's
    /// opponents - see <see cref="TheTeamNight"/> for why the record
    /// chooses rather than the club. Null when nobody they raced tonight
    /// has a name, or when none of those pairs has ever both-finished a
    /// race together.
    /// </summary>
    private static (NightRival Tonight, HeadToHead Season)? TheRival(
        string who,
        string name,
        List<(SessionRecord Race, int? Placed)> theirs,
        string className,
        ProfileStore profiles,
        SessionHistoryStore history,
        RoundDefinitionStore rounds)
    {
        // WHO THEY ACTUALLY RACED TONIGHT, not who was entered. A driver
        // on the sheet who never took the start is not somebody tonight
        // says anything about.
        var opponents = theirs
            .SelectMany(x => x.Race.Results)
            .Select(entry => entry.Transponder)
            .Where(id => !string.IsNullOrWhiteSpace(id)
                         && !string.Equals(id, who, StringComparison.OrdinalIgnoreCase)
                         && !GhostService.IsGhost(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (opponents.Count == 0)
        {
            return null;
        }

        var season = RacesIn(className, history, rounds);
        var races = theirs.Select(x => x.Race).ToList();

        (NightRival Tonight, HeadToHead Season)? best = null;
        var mostMet = 0;

        foreach (var other in opponents)
        {
            var rivalName = Typed(profiles.Get(other)?.SpokenName);

            if (rivalName.Length == 0)
            {
                continue;
            }

            var (ourSeason, theirSeason, _) = MetAcross(season, who, other);
            var met = ourSeason + theirSeason;

            if (met == 0 || met <= mostMet)
            {
                continue;
            }

            // TONIGHT'S HALF, over the same rule - a race where either of
            // them retired is not a meeting here either, which is what
            // stops "he beat him twice tonight" being built out of one
            // race and one breakdown.
            var (ourNight, theirNight, _) = MetAcross(races, who, other);

            if (ourNight + theirNight == 0)
            {
                continue;
            }

            mostMet = met;

            best = (
                new NightRival(other, rivalName, ourNight, theirNight),
                new HeadToHead(who, other, ourSeason, theirSeason));
        }

        return best;
    }

    /// <summary>
    /// How many real cars were in a race - ghosts left out, for the reason
    /// <c>Placing</c> leaves them out of a placing.
    ///
    /// What "fourth of nineteen" counts, and the reason it is counted at
    /// all: fourth in a field of four is a different night, and a line
    /// that only ever says the position reads the same for both.
    /// </summary>
    private static int RealCars(SessionRecord race) =>
        race.Results.Count(entry =>
            !string.IsNullOrWhiteSpace(entry.Transponder) && !GhostService.IsGhost(entry.Transponder));
}
