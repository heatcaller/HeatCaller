namespace HeatCaller;

/// <summary>
/// THE NIGHT'S MOMENTS - what actually happened, worked out from the races
/// the app has already saved (12 Oct 2026).
///
/// A last-lap lead change, a photo finish, a charge from the back of the
/// grid, the fastest lap of the race, the night a layout's record fell. The
/// app has always NOTICED these - it calls them over the PA as they happen -
/// and has never been able to refer BACK to one. So the recap leads with the
/// finishing order, the sign-off has no "moment of the night" to give, and
/// the night's thread has nothing concrete to turn on.
///
/// WHY THIS IS A READER AND NOT A STORE, which is the whole design
///
/// The obvious build is a store: catch each moment on the live path as it is
/// announced, keep it for the night, clear it with the race day - the shape
/// <see cref="ClubNotes"/> and <see cref="RoomVote"/> both use. It was
/// planned that way and it was wrong, and RaceHighlights says why in its own
/// first paragraph:
///
///   "There is deliberately NO second log of what the announcer said and
///   when. One was the obvious thing to build and it would have been wrong
///   twice over: it would have had to be written from the audio path (which
///   AudioBus exists to keep clear of anything that can block), and it would
///   have been a second account of the race that could drift from the real
///   one the moment anybody changed a rule about laps. Everything here is
///   arithmetic on the record the app already keeps."
///
/// So this is arithmetic on the saved result too, and it is mostly
/// RaceHighlights' own arithmetic - <see cref="RaceHighlights.MarksFrom"/>
/// already puts every driver's crossings on one timeline and says who took
/// the lead on which of them. Three things follow and all three are better
/// than a store would have been:
///
///   IT CANNOT DRIFT. There is one account of the race and the moments are
///   read off it. A rule about what counts as a lap changes both at once.
///
///   IT WORKS BACKWARDS. A store would have had moments only for the nights
///   after it shipped. This has them for every night already on disk, which
///   is exactly what the long callback - "the last time these two shared a
///   front row" - needs and what a store could never have given it.
///
///   ONE KIND OF MOMENT IS NOT HERE, AND SHOULD NOT BE. A leader going off,
///   retiring, or losing a lot of time is the only kind on the list that
///   cannot be derived honestly. SessionResultEntry.LapsLed's own comment
///   is the reason: "the running order between crossings isn't stored, so
///   'were they ahead at the moment they finished that lap' is the only
///   question a saved result can answer honestly." A guess at it would be a
///   Tier 3 claim - something a driver who was there knows is false - which
///   is the one thing the commentary layer never does.
///
/// EVERY FIGURE HERE IS THE ONE ANOTHER PART OF THE APP ALREADY USES, on
/// purpose. The photo-finish margin is the badge's margin, the charge is the
/// badge's four places, the fastest lap is RaceHighlights' own best-lap
/// index, and the record is the one TrackLayout.RecordLapMs holds. Two
/// answers to "was that a photo finish" is how a club gets a badge for one
/// and no mention of it from the announcer.
/// </summary>
internal static class NightMoments
{
    /// <summary>What kind of moment it was.</summary>
    internal enum Kind
    {
        /// <summary>The lead changed hands on the last lap.</summary>
        LastLapLead,

        /// <summary>Won by less than <see cref="PhotoFinishMs"/>, on the same lap.</summary>
        PhotoFinish,

        /// <summary>The layout's outright lap record fell in this race.</summary>
        LapRecord,

        /// <summary>Made up <see cref="LeastPlacesForACharge"/> places or more from the grid.</summary>
        Charge,

        /// <summary>The lead changed hands, on any other lap.</summary>
        LeadChange,

        /// <summary>The fastest lap of the race.</summary>
        FastestLap,
    }

    /// <summary>
    /// The margin that makes a finish a photo finish.
    ///
    /// One second, which is AchievementEvaluator's own figure for the
    /// PhotoFinish badge, taken from there rather than chosen again: a club
    /// that got the badge and no mention of it from the announcer would be
    /// right to ask which of the two was wrong.
    ///
    /// Same lap only, and for the badge's reason - comparing elapsed times
    /// across different lap counts would call every lapped car a photo
    /// finish.
    /// </summary>
    internal const long PhotoFinishMs = 1000;

    /// <summary>
    /// How many places make a charge.
    ///
    /// Four, which is the Charge badge's figure, for PhotoFinishMs' reason.
    /// </summary>
    internal const int LeastPlacesForACharge = 4;

    /// <summary>The most moments one night offers, strongest first - a list nobody can read is not a list.</summary>
    internal const int MostForTheNight = 12;

    /// <summary>
    /// One thing that happened, with what a sentence about it needs.
    /// </summary>
    /// <param name="What">Which kind - see <see cref="Kind"/>.</param>
    /// <param name="SessionId">The race it happened in, so a caller can get back to the result.</param>
    /// <param name="RaceLabel">The race as the program named it - "A-Main", "Heat 2".</param>
    /// <param name="Transponder">Whose moment it was.</param>
    /// <param name="DriverName">Their name as the result saved it. The caller says it the announcer's way.</param>
    /// <param name="Lap">Which lap, where the moment has one. Zero where it does not.</param>
    /// <param name="FigureMs">The margin, or the lap time - whichever the kind is about. Zero where there is no figure.</param>
    /// <param name="Places">Places made up, for a charge. Zero otherwise.</param>
    /// <param name="Beaten">Who they took the lead from, or whose record fell. Empty where nobody was.</param>
    /// <param name="AtUtc">When the race was, so a night can be told from a season. The flag where the result saved one, falling back to the green.</param>
    /// <param name="GreenAtUtc">
    /// When the race went green, which is a different question from AtUtc
    /// above and is asked by exactly one caller.
    ///
    /// NightPromises needs to know whether a race had STARTED when the
    /// broadcast pointed the room at somebody: a pace note called on lap
    /// five - "Carl has just gone quickest" - must not be paid off by the
    /// moment that says Carl set the fastest lap, because they are the same
    /// lap and the announcer would be congratulating itself on reading out
    /// its own line. AtUtc cannot answer it: it is the flag, so the race the
    /// note was called during finished AFTER the note and looks like a later
    /// race.
    ///
    /// DEFAULT MEANS UNKNOWN, and a promise is never paid off from a race
    /// whose green flag is unknown - a hand-entered result with no start
    /// time gets no callbacks rather than a guessed one.
    /// </param>
    internal sealed record Moment(
        Kind What,
        string SessionId,
        string RaceLabel,
        string Transponder,
        string DriverName,
        int Lap,
        long FigureMs,
        int Places,
        string Beaten,
        DateTime AtUtc,
        DateTime GreenAtUtc = default);

    /// <summary>
    /// Every moment in one saved race.
    /// </summary>
    /// <param name="record">The race, as it was saved.</param>
    /// <param name="layouts">
    /// The club's layouts, for the one moment that needs a store: a lap
    /// record is only a record because TrackLayout.RecordLapMs says so, and
    /// RaceControlControl.CheckAndAnnounceTrackRecord is the one place in
    /// the app allowed to set it. Null means no record moments rather than a
    /// guess at one.
    /// </param>
    internal static IReadOnlyList<Moment> ForRace(SessionRecord? record, TrackLayoutStore? layouts = null)
    {
        var found = new List<Moment>();

        // A race, not a practice run, and never a demo: the whole point is
        // that these are things that really happened.
        if (record is null
            || record.SessionType != RaceSessionType.Race
            || record.IsDemoSession()
            || record.Results.Count == 0)
        {
            return found;
        }

        var label = (record.Label ?? string.Empty).Trim();
        var at = record.EndedAtUtc != default ? record.EndedAtUtc : record.StartedAtUtc;

        // Never a disqualified driver. A DQ keeps its lap times (the timing
        // saw them; ResultEditor only takes the laps to nought), so without
        // this a driver thrown out at tech could still be credited with the
        // last-lap lead or the fastest lap - which TonightAtTheTrack already
        // refuses to give them.
        var scored = record.Results
            .Where(row => !string.IsNullOrWhiteSpace(row.Transponder) && !GhostService.IsGhost(row.Transponder))
            .Where(row => !IsDisqualified(row))
            .ToList();

        if (scored.Count == 0)
        {
            return found;
        }

        var mostLaps = scored.Max(row => row.Laps);

        string Named(string transponder) =>
            scored.FirstOrDefault(row =>
                string.Equals(row.Transponder, transponder, StringComparison.OrdinalIgnoreCase)) is { } row
            && !string.IsNullOrWhiteSpace(row.DriverName)
                ? row.DriverName.Trim()
                : transponder;

        Moment Made(Kind what, string transponder, int lap = 0, long figure = 0, int places = 0, string beaten = "") =>
            new(what, record.Id, label, transponder, Named(transponder), lap, figure, places, beaten, at,
                GreenAtUtc: record.StartedAtUtc);

        // ---------------------------------------------------------------
        //  The lead changing hands - RaceHighlights' own timeline
        // ---------------------------------------------------------------

        // Through MarksFrom rather than walked again here. It puts every
        // driver's crossings on one timeline and already knows the rule for
        // who was ahead after each one; a second walk would be a second
        // answer to the same question. The LAP is what tells a last-lap pass
        // from any other, which is why Mark carries one.
        //
        // The timeline is drawn WITHOUT the disqualified, not just filtered
        // of them afterwards: a DQ'd car left on it would still be "ahead"
        // of everybody, so the driver who really won would be read as taking
        // the lead from a car that no longer counts, or never at all.
        // MarksFrom reads nothing but Results, so a bare record carrying
        // them is enough.
        var timeline = record.Results.Where(row => !IsDisqualified(row)).ToList();
        var marks = RaceHighlights.MarksFrom(new SessionRecord { Results = timeline });

        // ONLY THE DRIVERS WHO SCORED, which MarksFrom does not filter and
        // is right not to: its job is handing a driver the seconds of the
        // recording that are theirs, and a ghost has no clip to be handed,
        // so a ghost mark there has never cost anything. Here it would: a
        // pace reference getting ahead of the leader would be read out as a
        // change of lead, and the room can see there is no car. Ghosts come
        // out of the timeline for the same reason SeasonStore takes them out
        // of a standings table.
        //
        // Matched against the rows above rather than tested for a ghost id,
        // so a driver the result does not score - excluded from records, a
        // row with no transponder - cannot be named either.
        var scoring = new HashSet<string>(
            scored.Select(row => row.Transponder),
            StringComparer.OrdinalIgnoreCase);

        var leads = marks
            .Where(mark => mark.What == RaceHighlights.Moment.Lead && scoring.Contains(mark.Transponder))
            .OrderBy(mark => mark.RaceTimeMs)
            .ToList();

        var held = string.Empty;

        foreach (var lead in leads)
        {
            // The driver it was taken FROM: whoever the previous lead mark
            // handed it to, and on the first change of the race, the driver
            // who led from the grid. That one is not on a mark at all - the
            // first leader is deliberately not a lead change - so it is read
            // off the same timeline: whoever crossed the line first.
            var from = held.Length > 0 ? held : LedFirst(timeline, scored, lead.Transponder);

            found.Add(Made(
                lead.Lap >= mostLaps && mostLaps > 0 ? Kind.LastLapLead : Kind.LeadChange,
                lead.Transponder,
                lap: lead.Lap,
                beaten: from));

            held = lead.Transponder;
        }

        // A PLAIN PASS IS NOT ONE OF THESE, deliberately. MarksFrom gives a
        // mark for every crossing on which anybody moved up the order, which
        // on a twenty-minute main is dozens - and a list of dozens is not a
        // list of moments. What a room remembers is the aggregate, and that
        // is the charge below.

        // ---------------------------------------------------------------
        //  The fastest lap of the race
        // ---------------------------------------------------------------

        // Worked out from the lap times rather than read off FastLapMs, for
        // RaceHighlights.BestLapIndex' reason: the answer is then an INDEX,
        // so the moment knows WHICH lap it was and a sentence can say so.
        var quickest = 0L;
        var quickestBy = string.Empty;
        var quickestLap = 0;

        foreach (var row in scored)
        {
            var best = RaceHighlights.BestLapIndex(row.LapTimesMs);

            if (best < 0)
            {
                continue;
            }

            var lap = row.LapTimesMs[best];

            if (lap > 0 && (quickest == 0 || lap < quickest))
            {
                quickest = lap;
                quickestBy = row.Transponder;
                quickestLap = best + 1;
            }
        }

        if (quickest > 0 && quickestBy.Length > 0)
        {
            found.Add(Made(Kind.FastestLap, quickestBy, lap: quickestLap, figure: quickest));
        }

        // ---------------------------------------------------------------
        //  The finish, and the drive through the field
        // ---------------------------------------------------------------

        if (scored.FirstOrDefault(row => row.Position == 1) is { } winner
            && scored.FirstOrDefault(row => row.Position == 2) is { } second
            && winner.Laps == second.Laps
            && winner.Laps > 0)
        {
            var margin = second.ElapsedMs - winner.ElapsedMs;

            // ElapsedMs, which carries penalties - and here that is right
            // rather than a slip. RaceHighlights uses the lap times instead
            // because it is placing a moment on a RECORDING, and a ten-second
            // penalty must not move a driver's timeline. This is the official
            // MARGIN, and the official margin is the one with the penalties
            // in it: it is what the result sheet says and what the room will
            // check it against.
            if (margin is > 0 and <= PhotoFinishMs)
            {
                found.Add(Made(Kind.PhotoFinish, winner.Transponder, figure: margin, beaten: second.Transponder));
            }
        }

        // WHY (20 Oct 2026): this used to be StartPosition - Position, and
        // those two are not counted the same way. Position is closed up over
        // the real cars; StartPosition is the raw grid slot, which counts
        // ghosts dragged to the front and no-shows on a stated roster. So a
        // driver from slot 6 (really 4th of the real cars) to P2 was "four
        // places" here while the Charge badge said P4 to P2 and gave nothing
        // - the two answers the summary above says must never happen. The
        // grid and the finish are now worked out exactly as
        // AchievementEvaluator's GridOf and PlaceOf do it: the grid counts
        // only real cars that turned a lap and lined up ahead, and the
        // finish is the place in the closed-up order. `scored` already has
        // no ghosts and no DQs (a DQ's laps are taken to nought, so GridOf
        // never counts one either, and it sorts to the bottom, so the
        // finishing place matches PlaceOf for everyone who raced).
        var finishOrder = scored.OrderBy(row => row.Position).ToList();

        // WHY (20 Oct 2026): a saved race can carry a starting slot left
        // over from a bigger race - slot 8 in a five-car heat. The count
        // above ranks it among the real cars, so it still came out as a
        // charge from a slot nobody really started in, and the season piece
        // and the other readers of these moments repeated it. A slot past
        // the field is not a grid position at all. The field here is every
        // car that held a slot - ghosts and no-shows included, because they
        // do hold one - so a real car lined up behind a ghost on pole keeps
        // its charge. DriverCut.ChargeFromARealSlot counts real cars only and
        // so is the stricter of the two; a driver's own piece never says
        // more than this does.
        var field = record.Results.Count(one => !string.IsNullOrWhiteSpace(one.Transponder));

        foreach (var row in scored)
        {
            // StartPosition of nought means the app was never told where
            // they started - a race run without a grid - and "up from
            // nowhere" is not a fact. A car that never turned a lap made up
            // nothing, and the badge does not look at it either.
            if (row.StartPosition <= 0 || row.StartPosition > field || row.Position <= 0 || row.Laps < 1)
            {
                continue;
            }

            var grid = AchievementEvaluator.GridPlace(scored, row);

            var place = finishOrder.IndexOf(row) + 1;

            var places = grid - place;

            if (places >= LeastPlacesForACharge)
            {
                found.Add(Made(Kind.Charge, row.Transponder, places: places));
            }
        }

        // ---------------------------------------------------------------
        //  A record falling, which is the only one that needs a store
        // ---------------------------------------------------------------

        if (RecordFellIn(record, layouts) is { } fell)
        {
            found.Add(fell);
        }

        return found;
    }

    /// <summary>Whether the result was disqualified - ResultEditor's own reading of the outcome.</summary>
    private static bool IsDisqualified(SessionResultEntry row) =>
        ResultEditor.NormaliseOutcome(row.Outcome) == ResultEditor.Dq;

    /// <summary>
    /// Whether this layout's outright record was set during this race, and
    /// the moment if it was.
    ///
    /// BY THE CLOCK, because that is the only join there is. TrackLayout
    /// keeps the record's lap, its holder and when it was set, and nothing
    /// anywhere says which SESSION set it - so a record whose RecordSetUtc
    /// falls between this race's green and its flag is this race's record.
    /// That is exact for every race a club has ever run, because two races
    /// cannot overlap on one timing loop.
    ///
    /// Null, rather than a guess, for: no layout store handed in, a race
    /// that names no layout, a layout the club has since renamed or deleted,
    /// a record never set, and a record with no date on it (which every
    /// record set before the field existed has).
    /// </summary>
    private static Moment? RecordFellIn(SessionRecord record, TrackLayoutStore? layouts)
    {
        if (layouts is null || string.IsNullOrWhiteSpace(record.TrackLayoutName))
        {
            return null;
        }

        var layout = layouts.All.FirstOrDefault(one =>
            string.Equals(one.Name, record.TrackLayoutName, StringComparison.OrdinalIgnoreCase));

        if (layout is null || layout.RecordLapMs <= 0 || layout.RecordSetUtc is not { } setAt)
        {
            return null;
        }

        var green = record.StartedAtUtc;
        var flag = record.EndedAtUtc != default ? record.EndedAtUtc : record.StartedAtUtc;

        if (green == default || setAt < green || setAt > flag)
        {
            return null;
        }

        // The holder by NAME, because that is what the layout keeps - there
        // is no transponder on a record. Matched back to this race's result
        // to get one, and left empty where it cannot be: a moment with no
        // transponder is still sayable, it just cannot be themed to a
        // driver.
        var holder = (layout.RecordHolderName ?? string.Empty).Trim();

        var row = record.Results.FirstOrDefault(one =>
            !string.IsNullOrWhiteSpace(one.DriverName)
            && string.Equals(one.DriverName.Trim(), holder, StringComparison.OrdinalIgnoreCase));

        return new Moment(
            Kind.LapRecord,
            record.Id,
            (record.Label ?? string.Empty).Trim(),
            row?.Transponder ?? string.Empty,
            holder,
            Lap: 0,
            FigureMs: layout.RecordLapMs,
            Places: 0,
            Beaten: string.Empty,
            AtUtc: setAt,
            GreenAtUtc: green);
    }

    /// <summary>
    /// Who led the race before the first change of lead - the driver the
    /// first pass for the lead was made ON.
    ///
    /// The first car across the line, on the same timeline MarksFrom walks
    /// and with its tie-break (the transponder): that is who it calls the
    /// first leader. Not the driver with the most laps led, which names
    /// whoever led LONGEST - in a race led by A, then B, then C for most of
    /// it, that is C, who had not led yet when B took it.
    ///
    /// Empty where that car cannot be named - a ghost, the driver taking the
    /// lead themselves - and where the result carries no laps led for them,
    /// which is every race saved before that field existed: the first car
    /// across always led lap one, so a nought there means the result cannot
    /// vouch for the order.
    /// </summary>
    private static string LedFirst(
        IReadOnlyList<SessionResultEntry> timeline,
        IReadOnlyList<SessionResultEntry> scored,
        string notThisOne)
    {
        var first = timeline
            .Where(row => !string.IsNullOrWhiteSpace(row.Transponder))
            .Select(row => (Row: row, Times: RaceHighlights.CrossingTimesMs(row.LapTimesMs)))
            .Where(one => one.Times.Count > 0)
            .OrderBy(one => one.Times[0])
            .ThenBy(one => one.Row.Transponder, StringComparer.OrdinalIgnoreCase)
            .Select(one => one.Row.Transponder)
            .FirstOrDefault();

        if (first is null || string.Equals(first, notThisOne, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return scored.FirstOrDefault(row =>
                   row.LapsLed > 0 && string.Equals(row.Transponder, first, StringComparison.OrdinalIgnoreCase)) is { } led
            ? led.Transponder
            : string.Empty;
    }

    /// <summary>
    /// The night's moments, strongest first, over the races handed in.
    ///
    /// Takes the races rather than the history store, so the caller decides
    /// what "tonight" is - the race day's own sessions, a class's, or a whole
    /// season for the pieces that want one.
    /// </summary>
    internal static IReadOnlyList<Moment> Over(
        IEnumerable<SessionRecord>? races,
        TrackLayoutStore? layouts = null,
        int most = MostForTheNight)
    {
        var found = new List<Moment>();

        foreach (var race in races ?? Enumerable.Empty<SessionRecord>())
        {
            found.AddRange(ForRace(race, layouts));
        }

        return found
            .OrderBy(one => Rank(one.What))
            .ThenByDescending(one => one.AtUtc)
            .Take(Math.Max(0, most))
            .ToList();
    }

    /// <summary>
    /// The one moment of the night, or null when nothing happened that is
    /// worth calling one.
    ///
    /// A fastest lap on its own is NOT the moment of the night. Every race
    /// has one, so a sign-off that called it the moment of the night would
    /// say the same shape of thing every week - which is the exact
    /// repetition the whole depth plan was written against. Something has to
    /// have HAPPENED.
    /// </summary>
    internal static Moment? OfTheNight(
        IEnumerable<SessionRecord>? races,
        TrackLayoutStore? layouts = null) =>
        Over(races, layouts, MostForTheNight)
            .FirstOrDefault(one => one.What != Kind.FastestLap);

    /// <summary>
    /// Strongest moment first.
    ///
    /// A JUDGEMENT, and these are the reasons. A lead changing hands on the
    /// last lap is the best thing that happens in racing and the rarest, so
    /// it goes first. A photo finish is the same drama with the result
    /// already settled. A record falling outlives the night - it is the one
    /// moment here a club will still be talking about next month - but it is
    /// one driver on their own against the clock, so it goes behind the two
    /// that were fights. A charge is a whole race rather than a moment. A
    /// lead change away from the last lap is often given back. And a fastest
    /// lap happens in every race, which is why OfTheNight will not have it.
    /// </summary>
    private static int Rank(Kind what) => what switch
    {
        Kind.LastLapLead => 0,
        Kind.PhotoFinish => 1,
        Kind.LapRecord => 2,
        Kind.Charge => 3,
        Kind.LeadChange => 4,
        _ => 5,
    };
}
