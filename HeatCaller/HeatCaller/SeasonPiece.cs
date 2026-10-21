namespace HeatCaller;

/// <summary>
/// A SEASON, as something the announcer reads to a room that has sat down
/// to listen - a prize night, a presentation evening, the last meeting of
/// the year.
///
/// IT IS NOT A FILM, AND THE NAME IT HAD WAS THE FIRST THING TO FIX. The
/// broadcast-value plan called this "the season film" from the start, and
/// video is out everywhere in this app by standing decision - on that page,
/// on the driver-night page, and in the 12 Oct decisions. A name that
/// promises a club something the app will never make is worse than no
/// feature. This is the night's recap over a year: words, read aloud,
/// recorded like every other piece.
///
/// WHAT MAKES IT DIFFERENT FROM EVERY OTHER SEGMENT HERE
///
/// Every one of them is handed one race or one night and SUMMARISES it.
/// This one is handed twenty nights and has to decide what the year was
/// about, which is a different job: it has to leave things out. NightRecap
/// is four hundred words for a single night, so a season at that density
/// would be eight thousand - three quarters of an hour nobody will sit
/// through and no model will write well.
///
/// So what this class really is, is a SELECTOR. Five things are offered and
/// nothing else, each chosen because it is a claim a season can make that a
/// night cannot, and each one already computed somewhere else:
///
///   what the season was - its rounds, its nights and how many raced;
///   who won it, by how much, and who was behind them;
///   who turned up, which is the one honour a club awards by attendance;
///   the moments, ranked across the whole year by NightMoments' own order;
///   and how big the club got, which is the committee's story rather than
///   any driver's.
///
/// NOTHING IS COMPUTED HERE THAT IS NOT ALREADY COMPUTED SOMEWHERE. The
/// standings come from SeasonStore.ComputeStandings, the moments from
/// NightMoments.Over, the sessions from EventSpine.SessionsFor, and each
/// moment's sentence from DriverCut.Describe - whose own comment asks for
/// exactly this, because two wordings for one moment is two wordings that
/// drift.
///
/// THE THREE FREEDOMS, which are why this is worth building at all. It is
/// for a room that chose to listen, once a year: so it may be long, it need
/// not be quick, and nobody is waiting on a race. Every other segment in
/// this app is written against a deadline measured in seconds.
/// </summary>
internal static class SeasonPiece
{
    /// <summary>
    /// How many words the model is asked for.
    ///
    /// Nearly twice the night's recap (NightRecap.Words is 400) and still
    /// inside what NightRecap.Chunks will carry: fifteen chunks of twenty
    /// seconds is five minutes of speech, which is about seven hundred and
    /// fifty words said at a broadcast pace. So the chunking, the ceiling
    /// and the treatment are all the recap's, unchanged - see Chunks below.
    /// </summary>
    internal const int Words = 700;

    /// <summary>
    /// The most of the model's answer that is kept.
    ///
    /// The recap's own MostChars, because the piece is read through the
    /// recap's chunker and anything past what that will carry is thrown
    /// away by it anyway.
    /// </summary>
    internal const int MostChars = NightRecap.MostChars;

    /// <summary>
    /// How many facts are offered. Generous, because the whole job here is
    /// selection and the pack's own character cap is the real limit.
    /// </summary>
    internal const int MostMoments = 12;

    /// <summary>
    /// What the recording is called, and what the Big Screen's band says.
    ///
    /// The season's own name when it has one, because a club that runs a
    /// Winter Series and a Summer Series will have two of these and the
    /// files have to tell themselves apart.
    /// </summary>
    internal static string LabelFor(string? seasonName)
    {
        var named = CommentaryFacts.Clean(seasonName);

        return named.Length > 0 ? $"{named} - the season" : "The season";
    }

    /// <summary>What the crowd band reads while this is on - see ShowTreatment's caption.</summary>
    internal static string OnTheBand(string? seasonName)
    {
        var named = CommentaryFacts.Clean(seasonName);

        return named.Length > 0 ? named : "The Season";
    }

    /// <summary>
    /// How the model is told to write it.
    ///
    /// DriverCut.Instruction is the model to follow and four of its rules
    /// transfer nearly unchanged. The two that are this piece's own are the
    /// last two, and both are about a season rather than a night:
    ///
    ///   do not rank the people you do not name, because a piece that says
    ///   who improved most implies an order for everybody else in the room;
    ///
    ///   and do not make it about the champion, which matters more over a
    ///   season than over a night - a night has several winners and a
    ///   season has one, so a piece built round the champion is a piece the
    ///   rest of the club sits through.
    /// </summary>
    internal const string Instruction =
        "Write this as a short spoken piece about a club's whole SEASON of racing, to be read out to the "
        + "members at a prize night - they are sitting down, they were there for most of it, and they have "
        + "chosen to listen. Move through the season roughly in order and end with the championship. "
        + "Refer to races and nights rather than explaining what a heat or a championship is. "
        + "Make it about the RACING and about the club - how the year went, what happened in it, who turned "
        + "up - rather than about the points table: one driver won the championship and everybody else in "
        + "the room did not, so a piece that only works for the champion is a piece most of the club sits "
        + "through. "
        + "Name only the drivers in the facts above, and do not imply any order among the drivers you do "
        + "not name. Do not say who improved, who declined, or who should have done better. "
        + "Everything you say must come from the facts above - every name, time, position, point and count. "
        + "Do not add a result, a record, a margin or a mood that is not there, and where the facts are thin, "
        + "write a shorter piece rather than filling it out.";

    /// <summary>
    /// Broken into pieces for the voice queue - the recap's own chunker,
    /// unchanged, for the reason <see cref="MostChars"/> gives.
    /// </summary>
    internal static IReadOnlyList<string> Chunks(string? script) => NightRecap.Chunks(script);

    /// <summary>
    /// How long the read may take - the recap's own ceiling, because it is
    /// the same chunker carrying the same number of chunks.
    /// </summary>
    internal static System.TimeSpan LongestRead => NightRecap.LongestRead;

    /// <summary>
    /// What a season comes to, or an empty pack when there is nothing worth
    /// a piece.
    ///
    /// SAID: NULL, like the night's recap and a driver's cut and for their
    /// reason: everything in here was said over the PA months ago, and a
    /// piece built from only what the announcer forgot would be a strange
    /// document. No budget and no cooldown either - somebody pressed a
    /// button, once, after the racing.
    ///
    /// THE STANDINGS ARE USED IN THE ORDER THEY ARRIVE and never re-sorted.
    /// SeasonStore.ComputeStandings orders by points, then wins, then best
    /// finish, then podiums, then name - a tie-break chain this class has
    /// no business second-guessing. Re-sorting on points alone would name
    /// the wrong champion in exactly the season a club would most want this
    /// piece for: the one that went to a tie.
    /// </summary>
    /// <param name="season">The championship, for its name and its rules. Null is allowed; the piece then has no name.</param>
    /// <param name="races">Its sessions, as EventSpine.SessionsFor resolves them.</param>
    /// <param name="standings">Its table, as SeasonStore.ComputeStandings returns it - in that order.</param>
    /// <param name="layouts">For a record falling - see NightMoments.ForRace. Null means no record is claimed rather than a guess at one.</param>
    internal static PackedFacts FactsFor(
        Season? season,
        IReadOnlyList<SessionRecord>? races,
        IReadOnlyList<SeasonStandingEntry>? standings,
        TrackLayoutStore? layouts = null)
    {
        var ran = (races ?? System.Array.Empty<SessionRecord>())
            .Where(race => race is not null && !race.IsDemoSession())
            .OrderBy(race => race.StartedAtUtc)
            .ToList();

        if (ran.Count == 0)
        {
            return FactPack.Build(System.Array.Empty<FactBlock>(), said: null, MostChars);
        }

        var named = CommentaryFacts.Clean(season?.Name);
        var called = named.Length > 0 ? named : "the season";

        // In the order ComputeStandings gave them - see the summary.
        var table = (standings ?? System.Array.Empty<SeasonStandingEntry>())
            .Where(one => one is not null && !string.IsNullOrWhiteSpace(one.DriverName))
            .ToList();

        var blocks = new List<FactBlock>();

        // ---------------------------------------------------------------
        //  What the season was
        // ---------------------------------------------------------------

        var nights = ran
            .Select(race => TonightAtTheTrack.NightOf(race.StartedAtUtc.ToLocalTime()))
            .Distinct()
            .Count();

        var shape = new System.Text.StringBuilder(
            $"{Capitalised(called)} ran to {Plural.Of(ran.Count, "race")} across {Plural.Of(nights, "race night")}");

        // 17 Oct 2026, Bug Hunt 8, A09-6: the table holds everybody who
        // raced, a 0-lap DNF and a finish outside the points included, so
        // its length was not how many scored.
        var scorers = table.Count(one => one.TotalPoints > 0);

        if (scorers > 0)
        {
            shape.Append($", with {Plural.Of(scorers, "driver")} scoring");
        }

        shape.Append('.');

        // A best-rounds rule is the one season rule a driver will ask
        // about, and it is the reason somebody's total is lower than their
        // results look. Said only when it actually dropped something.
        if (season is { CountBestRounds: > 0 } rules && table.Any(one => one.DroppedRounds > 0))
        {
            shape.Append(
                $" The championship counted each driver's best {Plural.Of(rules.CountBestRounds, "round")}.");
        }

        blocks.Add(new FactBlock("season:shape", shape.ToString()));

        // ---------------------------------------------------------------
        //  Who won it, and by how much
        // ---------------------------------------------------------------

        if (table.Count > 0)
        {
            var champion = table[0];

            var won = new System.Text.StringBuilder(
                $"{champion.DriverName} won {called} on {Plural.Of(champion.TotalPoints, "point")}");

            // 17 Oct 2026, Bug Hunt 8, A09-3: SessionsCounted is every round
            // scored, the ones the best-N rule dropped included - "from 12
            // rounds counted" straight after "counted the best 8".
            var counted = champion.SessionsCounted - champion.DroppedRounds;

            if (counted > 0)
            {
                won.Append($" from {Plural.Of(counted, "round")} counted");
            }

            if (champion.Wins > 0)
            {
                won.Append($", winning {Plural.Of(champion.Wins, "race")}");
            }

            won.Append('.');

            // THE MARGIN, which is the one figure that says whether the
            // season was a contest. A margin of two points and a margin of
            // two hundred are the same sentence without it.
            if (table.Count > 1)
            {
                var second = table[1];
                var margin = champion.TotalPoints - second.TotalPoints;

                won.Append(margin <= 0
                    ? $" {second.DriverName} finished level with them on points and second on the championship's own tie-break."
                    : $" {second.DriverName} was second, {Plural.Of(margin, "point")} behind.");
            }

            blocks.Add(new FactBlock("season:champion", won.ToString()));

            // Third, on its own block so the cap can drop it before the
            // champion.
            if (table.Count > 2)
            {
                var third = table[2];

                blocks.Add(new FactBlock(
                    "season:third",
                    $"{third.DriverName} was third on {Plural.Of(third.TotalPoints, "point")}."));
            }
        }

        // ---------------------------------------------------------------
        //  Who turned up
        // ---------------------------------------------------------------

        // THE ONE HONOUR A CLUB AWARDS BY ATTENDANCE, and the only claim in
        // here that is not about being fast. It is also unarguable, which is
        // why it is in and "most improved" is not: see the note at the end.
        //
        // Only when somebody actually turned up more than the champion did,
        // because otherwise it is the champion's sentence again.
        if (table.Count > 1)
        {
            var most = table.OrderByDescending(one => one.SessionsCounted).First();

            // 17 Oct 2026, Bug Hunt 8, A09-4: "the most of anybody" only when
            // nobody else raced as many - a tie left the other driver out.
            if (most.SessionsCounted > table[0].SessionsCounted
                && most.SessionsCounted > 1
                && table.Count(one => one.SessionsCounted == most.SessionsCounted) == 1
                && !string.Equals(most.DriverName, table[0].DriverName, System.StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(new FactBlock(
                    "season:turnout",
                    $"{most.DriverName} raced the most rounds of anybody: {Plural.Of(most.SessionsCounted, "round")}."));
            }
        }

        // ---------------------------------------------------------------
        //  The moments, over the whole year
        // ---------------------------------------------------------------

        // NightMoments' OWN ORDER, which is the point of using Over rather
        // than walking the races: Rank puts a last-lap lead change before a
        // photo finish before a record falling before a charge. A season's
        // twelve best moments is that function called with a bigger list.
        //
        // AND THE FASTEST LAP IS SKIPPED, which is the 13 Oct lesson from
        // PastMeetings arriving here before it could cost anything: every
        // race has a fastest lap, so over a season of two hundred races the
        // pack would fill with them and say nothing about the year.
        //
        // AND THIS IS THE EXPENSIVE LINE IN THE FILE, which is fine exactly
        // here and nowhere else. Over calls ForRace per race, and ForRace
        // builds that race's whole lap-by-lap timeline - so a season of two
        // hundred races is two hundred of those. It is also a button
        // pressed once a year, by somebody standing at an idle laptop, who
        // is already waiting the best part of a minute for the model. The
        // same call on the race-filing thread is the cost recorded against
        // a driver's night in the roadmap; the difference is entirely where
        // it is made.
        var moments = NightMoments.Over(ran, layouts, int.MaxValue)
            .Where(moment => moment.What != NightMoments.Kind.FastestLap)
            .Take(MostMoments);

        foreach (var moment in moments)
        {
            var who = CommentaryFacts.Clean(moment.DriverName);

            if (who.Length == 0)
            {
                continue;
            }

            if (DriverCut.Describe(moment, who) is { Length: > 0 } sentence)
            {
                // WHY (20 Oct 2026): the key was only race + kind, but one race
                // can hold several lead changes and several charges. FactPack
                // drops a repeated key, so a second driver's charge vanished
                // and still used up one of the twelve slots. The transponder
                // and lap now make each moment's key its own.
                blocks.Add(new FactBlock(
                    $"season:moment:{moment.SessionId}:{moment.What}:{moment.Transponder}:{moment.Lap}",
                    WhenIt(moment.AtUtc) is { Length: > 0 } when ? $"{sentence} ({when})" : sentence));
            }
        }

        // ---------------------------------------------------------------
        //  How big the club got
        // ---------------------------------------------------------------

        // THE COMMITTEE'S OWN STORY, and the figure they will repeat. Said
        // as the biggest night rather than an average, because an average
        // over a season that grew is a number describing neither end of it.
        var biggest = ran
            .GroupBy(race => TonightAtTheTrack.NightOf(race.StartedAtUtc.ToLocalTime()))
            .Select(night => night
                .SelectMany(race => race.Results)
                .Where(row => !string.IsNullOrWhiteSpace(row.Transponder) && !GhostService.IsGhost(row.Transponder))
                .Select(row => row.Transponder)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .Count())
            .DefaultIfEmpty(0)
            .Max();

        if (biggest > 1)
        {
            blocks.Add(new FactBlock(
                "season:biggest",
                $"The busiest night of {called} had {Plural.Of(biggest, "car")} out."));
        }

        return FactPack.Build(blocks, said: null, MostChars);
    }

    /// <summary>
    /// Roughly when in the year a moment was, for a piece that moves through
    /// a season in order - "in March", "in November".
    ///
    /// THE MONTH AND NOT THE DATE. A room at a prize night does not want
    /// the fourteenth of March; it wants to be reminded roughly when. And a
    /// month is a claim that survives being slightly wrong about a meeting
    /// that started at five to midnight, which a date is not.
    ///
    /// In the club's own culture, like every other line written to be read
    /// by a person.
    /// </summary>
    internal static string WhenIt(System.DateTime atUtc)
    {
        if (atUtc == default)
        {
            return string.Empty;
        }

        return "in " + atUtc.ToLocalTime().ToString("MMMM", System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// A season's name as a sentence starts - "The Winter Series ran to..."
    /// - without touching a name the club typed in capitals or lower case
    /// on purpose.
    /// </summary>
    private static string Capitalised(string value) =>
        value.Length == 0 || char.IsUpper(value[0])
            ? value
            : char.ToUpper(value[0], System.Globalization.CultureInfo.CurrentCulture) + value[1..];
}
