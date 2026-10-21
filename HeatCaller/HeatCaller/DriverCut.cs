namespace HeatCaller;

/// <summary>
/// ONE DRIVER'S NIGHT, AS A PIECE FOR THEM (12 Oct 2026) - sixty to ninety
/// seconds about the races they ran, what their pace did, where they
/// finished and what happened to them, written for that driver and nobody
/// else.
///
/// WHY THIS IS THE ONE THAT MATTERS MOST
///
/// Everything else the show produces is consumed in the hall by people who
/// were already there. The night's recap was the first thing that left the
/// building; this is the first thing a DRIVER takes home and shows
/// somebody, and a driver showing somebody is how a club gains a member.
/// It is a growth feature rather than a polish feature and there is nothing
/// else on the broadcast plan that can be said about.
///
/// WHAT IT IS NOT, AND THE DIFFERENCE IS THE WHOLE POINT
///
/// It is not the night's recap with one name in it. The recap moves through
/// an evening for somebody who was not there; this moves through one
/// person's evening for that person, who WAS there - so it refers rather
/// than explains, it never tells them the result of a race they were
/// standing beside, and it is allowed to be about a sixth place. A piece
/// that only works for a winner is a piece most of the club never gets.
///
/// AND IT IS NOT A HIGHLIGHT REEL. <see cref="RaceHighlights.DriverClip"/>
/// already cuts the seconds of each race's recording where this driver was
/// called, and those clips are written per race. Stitching them into this
/// piece is a job in audio rather than in facts - formats, offsets,
/// joins - and it is the obvious next thing to do to this file, not a
/// thing this file pretends to have done.
///
/// EVERY FIGURE COMES FROM THE SAVED RACES, which is the rule the whole
/// commentary layer is built on and matters more here than anywhere: this
/// is handed to the one person in the club who can check every word of it
/// against what they remember. A piece that invents a position is a piece
/// that driver never trusts again, and neither does anybody they showed it
/// to.
/// </summary>
internal static class DriverCut
{
    /// <summary>
    /// How long the piece is, in words.
    ///
    /// A hundred and fifty, which is sixty to ninety seconds spoken - the
    /// length of a radio item about one person. The night's recap is four
    /// hundred because it has a whole evening to get through; this has one
    /// driver's three races, and a hundred and fifty words of real detail
    /// about them beats four hundred words of padding round the same three
    /// results.
    /// </summary>
    internal const int Words = 150;

    /// <summary>
    /// Extra words over the club's own per-line limit, because
    /// CommentaryService.BuildSystemPrompt ADDS its argument rather than
    /// replacing the limit - the trap NightRecap.ExtraWordsOver was written
    /// for, and the same answer.
    ///
    /// Nought where a club already asks for longer lines than this: a club
    /// that set a two-hundred-word limit gets its two hundred rather than
    /// three hundred and fifty.
    /// </summary>
    internal static int ExtraWordsOver(int clubLimit) =>
        Math.Max(0, Words - Math.Max(0, clubLimit));

    /// <summary>
    /// The most of a script that is kept.
    ///
    /// Three thousand characters, which is well above anything a
    /// hundred-and-fifty-word piece produces and is a backstop against a
    /// model that ignored the length rather than a target. NOT
    /// SponsorCopy.Tidy's six hundred, and that is worth saying because
    /// reaching for Tidy here is the exact mistake the night's recap caught
    /// on its way out: Tidy is sized for a sponsor's read and would have
    /// thrown away three quarters of every piece in silence.
    /// </summary>
    internal const int MostChars = 3000;

    /// <summary>
    /// Split into spoken pieces, through <see cref="NightRecap.Chunks"/>.
    ///
    /// THE SAME SPLITTER, not a second one. It splits only at the end of a
    /// sentence, never inside one, and caps the pieces - and two
    /// implementations of "where may a voice take a breath" is the sort of
    /// thing that agrees for a month and then disagrees about one sentence
    /// with a decimal point in it.
    /// </summary>
    internal static IReadOnlyList<string> Chunks(string? script) => NightRecap.Chunks(script);

    /// <summary>What this driver's piece is called in the night's folder, beside the races and The Night.</summary>
    internal static string LabelFor(string? driverName)
    {
        var named = (driverName ?? string.Empty).Trim();

        return named.Length > 0 ? named + " - their night" : "A driver - their night";
    }

    /// <summary>
    /// How the model is told to write it.
    ///
    /// FOUR THINGS, and each one is a way this piece could be worse than
    /// nothing. Write to them, because this is theirs. Refer rather than
    /// explain, because they were standing there. Be about the racing
    /// rather than about the result, because most of the club does not win.
    /// And nothing that is not in the facts, because they can check every
    /// word.
    /// </summary>
    internal const string Instruction =
        "Write this as a short piece for ONE DRIVER about their own night at the track - it is being handed to "
        + "them. Speak about them by name in the third person, the way a broadcast would. They were there, so "
        + "refer to their races rather than explaining what a heat is, and never tell them a result as though it "
        + "were news. Move through their evening in order. "
        + "Make it about the DRIVING - how their pace went, what happened to them, what they did about it - "
        + "rather than about where they finished: most drivers do not win, and a piece that only works for a "
        + "winner is a piece most of the club never gets. Do not console them, flatter them, or tell them what "
        + "to work on. "
        + "Everything you say must come from the facts above - every name, time, position and count. Do not add "
        + "a result, a lap time, an incident or a mood that is not there, and where the facts are thin, write a "
        + "shorter piece rather than filling it out.";

    /// <summary>
    /// The facts one driver's night comes to, or an empty pack when there
    /// is nothing worth a piece.
    ///
    /// SAID: NULL, like the night's recap and for its reason. Every other
    /// segment passes the night's "already said" filter so the evening does
    /// not repeat itself; this one must not, because everything in it has
    /// already been said - over the PA, to a room, hours ago. A driver's
    /// own piece built from only what the announcer forgot to mention would
    /// be a strange document.
    ///
    /// NO BUDGET AND NO COOLDOWN either: it is asked for by somebody
    /// pressing a button, once, after the racing.
    /// </summary>
    /// <param name="transponder">The driver, as their results key on.</param>
    /// <param name="driverName">Their name, as it should be said.</param>
    /// <param name="tonight">The night's saved races, in the order they ran.</param>
    /// <param name="layouts">For a record falling - see NightMoments.ForRace.</param>
    internal static PackedFacts FactsFor(
        string? transponder,
        string? driverName,
        IReadOnlyList<SessionRecord>? tonight,
        TrackLayoutStore? layouts = null)
    {
        var who = (transponder ?? string.Empty).Trim();
        var named = CommentaryFacts.Clean(driverName);

        if (who.Length == 0 || named.Length == 0 || tonight is null)
        {
            return FactPack.Build(Array.Empty<FactBlock>(), said: null, MostChars);
        }

        var blocks = new List<FactBlock>();

        var theirs = tonight
            .Where(race => race is not null
                           && race.SessionType == RaceSessionType.Race
                           && !race.IsDemoSession()
                           && Rowed(race, who) is not null)
            .OrderBy(race => race.StartedAtUtc)
            .ToList();

        if (theirs.Count == 0)
        {
            return FactPack.Build(Array.Empty<FactBlock>(), said: null, MostChars);
        }

        var theme = FactTheme.Driver(who);

        // ---------------------------------------------------------------
        //  Each race, in the order they ran
        // ---------------------------------------------------------------

        var bestLapMs = 0L;
        var bestLapIn = string.Empty;
        var wins = 0;
        var bestPlace = int.MaxValue;

        for (var at = 0; at < theirs.Count; at++)
        {
            var race = theirs[at];
            var row = Rowed(race, who)!;

            var label = CommentaryFacts.Clean(race.Label);
            var ran = label.Length > 0 ? label : "a race";

            // HOW MANY THEY BEAT, not just where they came: "sixth of
            // nineteen" is a different night from "sixth of six", and a
            // piece that only ever says the position is a piece that reads
            // the same for both.
            var field = race.Results.Count(one =>
                !string.IsNullOrWhiteSpace(one.Transponder) && !GhostService.IsGhost(one.Transponder));

            var said = new System.Text.StringBuilder($"{named} raced {ran}");

            if (row.Position > 0)
            {
                said.Append($" and finished {Ordinal(row.Position)}");

                if (field > 1)
                {
                    said.Append($" of {field}");
                }

                if (row.Position == 1)
                {
                    wins++;
                }

                bestPlace = Math.Min(bestPlace, row.Position);
            }

            // Only a slot that could be one in THIS race. A freeform race
            // used to take its grid from the previous race's whole field, so
            // results already saved can carry "8th on the grid" in a five-car
            // heat - and "up from 8th" is a climb that never happened.
            if (row.StartPosition > 0 && row.Position > 0 && row.StartPosition != row.Position
                && row.StartPosition <= field)
            {
                said.Append(row.StartPosition > row.Position
                    ? $", up from {Ordinal(row.StartPosition)} on the grid"
                    : $", having started {Ordinal(row.StartPosition)}");
            }

            said.Append('.');

            if (row.Laps > 0)
            {
                said.Append($" {Plural.Of(row.Laps, "lap")}.");
            }

            // THEIR OWN BEST LAP IN THAT RACE, through the same index the
            // moments use - so it is the lap a sentence can name rather
            // than a figure off FastLapMs with no lap behind it.
            var best = RaceHighlights.BestLapIndex(row.LapTimesMs);

            if (best >= 0 && row.LapTimesMs[best] > 0)
            {
                var lap = row.LapTimesMs[best];

                said.Append($" Their best lap of it was {Seconds(lap)}s, on lap {best + 1}.");

                if (bestLapMs == 0 || lap < bestLapMs)
                {
                    bestLapMs = lap;
                    bestLapIn = ran;
                }
            }

            blocks.Add(new FactBlock($"cut:{who}:race:{race.Id}", said.ToString(), theme));
        }

        // ---------------------------------------------------------------
        //  What happened to them - the night's own moments
        // ---------------------------------------------------------------

        // THIS IS WHAT NightMoments WAS BUILT FOR. Without it a driver's
        // piece is three finishing positions and a lap time; with it the
        // piece can say they took the lead on the last lap, or came through
        // four places, or that the record fell to them.
        //
        // THEIRS FIRST, THEN THE CAP. The races in `theirs` carry every
        // driver's result, so capping before filtering would spend the
        // night's twelve on everybody else's lead changes and charges and
        // could leave this driver's own piece with none of theirs.
        var mine = NightMoments.Over(theirs, layouts, int.MaxValue)
            .Where(moment => string.Equals(moment.Transponder, who, StringComparison.OrdinalIgnoreCase))
            .Where(moment => moment.What != NightMoments.Kind.Charge || ChargeFromARealSlot(moment, theirs, who))
            .Take(NightMoments.MostForTheNight);

        foreach (var moment in mine)
        {
            if (Describe(moment, named) is { Length: > 0 } sentence)
            {
                // WHY (20 Oct 2026): the key was only the race and the kind
                // of moment, so a driver who took the lead twice in one race
                // lost the second lead change as a duplicate. The lap makes
                // each one its own, as SeasonPiece now does.
                blocks.Add(new FactBlock(
                    $"cut:{who}:moment:{moment.SessionId}:{moment.What}:{moment.Lap}",
                    sentence,
                    theme));
            }
        }

        // ---------------------------------------------------------------
        //  And the night as a whole
        // ---------------------------------------------------------------

        var whole = new System.Text.StringBuilder(
            $"Over the night {named} raced {Plural.Of(theirs.Count, "time")}");

        if (wins > 0)
        {
            // Not through Plural, which would make "1 of thems" of it: the
            // thing being counted here is the races and the noun is already
            // plural in the phrase.
            whole.Append(wins == theirs.Count
                ? theirs.Count == 1 ? ", and won it" : ", winning every one of them"
                : $", winning {wins} of them");
        }
        else if (bestPlace != int.MaxValue)
        {
            whole.Append($", with {Ordinal(bestPlace)} their best finish");
        }

        whole.Append('.');

        if (bestLapMs > 0)
        {
            whole.Append($" Their quickest lap of the whole night was {Seconds(bestLapMs)}s");

            if (bestLapIn.Length > 0)
            {
                whole.Append($", in {bestLapIn}");
            }

            whole.Append('.');
        }

        blocks.Add(new FactBlock($"cut:{who}:night", whole.ToString(), theme));

        return FactPack.Build(blocks, said: null, MostChars);
    }

    /// <summary>
    /// What the Big Screen's band says while a driver's own piece is read.
    ///
    /// Whose night it is, because that is what the piece is and what the
    /// room wants to know. The piece borrows the sign-off's treatment, and
    /// until 13 Oct 2026 it borrowed the sign-off's name with it - so a
    /// piece with one driver's name in every sentence played under a band
    /// reading "Sign-Off".
    ///
    /// Falls back to a name of its own for a driver the profiles cannot
    /// name, rather than an empty band.
    /// </summary>
    internal static string OnTheBand(string? driverName) =>
        string.IsNullOrWhiteSpace(driverName) ? "A Driver's Night" : driverName.Trim() + "'s Night";

    /// <summary>One moment as a fact about this driver, or empty where it is not sayable about them.</summary>
    /// <summary>
    /// One moment as a sentence, with this driver named in it.
    ///
    /// INTERNAL RATHER THAN PRIVATE because NightPromises' callback needs
    /// the same sentence, and two wordings for one moment is two wordings
    /// that drift: a club would hear "made up four places from the grid"
    /// in a driver's own piece and something slightly different in the
    /// callback about the same race on the same night.
    /// </summary>
    internal static string Describe(NightMoments.Moment moment, string named)
    {
        var race = CommentaryFacts.Clean(moment.RaceLabel);
        var inThat = race.Length > 0 ? $" in {race}" : string.Empty;

        return moment.What switch
        {
            NightMoments.Kind.LastLapLead =>
                $"{named} took the lead on the last lap{inThat}.",
            NightMoments.Kind.LeadChange =>
                $"{named} took the lead on lap {moment.Lap}{inThat}.",
            NightMoments.Kind.PhotoFinish =>
                $"{named} won{inThat} by {Seconds(moment.FigureMs)}s.",
            NightMoments.Kind.Charge =>
                $"{named} made up {Plural.Of(moment.Places, "place")} from the grid{inThat}.",
            NightMoments.Kind.FastestLap =>
                $"{named} set the fastest lap{inThat}: {Seconds(moment.FigureMs)}s on lap {moment.Lap}.",
            NightMoments.Kind.LapRecord =>
                $"The outright lap record for this track layout fell to {named}{inThat}: {Seconds(moment.FigureMs)}s.",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Whether a charge moment for this driver rests on a grid slot that
    /// could exist in that race.
    ///
    /// WHY (20 Oct 2026): the per-race sentence above already refuses a
    /// StartPosition bigger than the race's field (a freeform race used to
    /// take its grid from the previous race's whole field), but the moments
    /// came straight from NightMoments, which never checks the slot against
    /// the field - so a five-car heat could still say "made up 7 places from
    /// the grid" (or, with the grid ranked over the real cars, 4 places
    /// from a slot nobody can say they really started in).
    /// This applies the same field count and the same test to the charge,
    /// so the two halves of the piece agree.
    /// </summary>
    internal static bool ChargeFromARealSlot(
        NightMoments.Moment moment, IEnumerable<SessionRecord> races, string transponder)
    {
        var race = races.FirstOrDefault(one => string.Equals(one.Id, moment.SessionId, StringComparison.Ordinal));

        if (race is null || Rowed(race, transponder) is not { } row)
        {
            return false;
        }

        var field = race.Results.Count(one =>
            !string.IsNullOrWhiteSpace(one.Transponder) && !GhostService.IsGhost(one.Transponder));

        return row.StartPosition > 0 && row.StartPosition <= field;
    }

    /// <summary>This driver's row in a race, or null when they were not in it.</summary>
    private static SessionResultEntry? Rowed(SessionRecord race, string transponder) =>
        race.Results.FirstOrDefault(row =>
            string.Equals(row.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    /// <summary>Seconds to the thousandth, with a decimal POINT whatever the PC's region.</summary>
    private static string Seconds(long ms) =>
        (ms / 1000.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A position as a person says it. Past the twenties it is a number with a suffix, which is fine for a grid of forty.</summary>
    private static string Ordinal(int position) => position switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        5 => "fifth",
        6 => "sixth",
        7 => "seventh",
        8 => "eighth",
        9 => "ninth",
        10 => "tenth",
        _ => position + Suffix(position),
    };

    private static string Suffix(int position) =>
        (position % 100) is >= 11 and <= 13
            ? "th"
            : (position % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
}
