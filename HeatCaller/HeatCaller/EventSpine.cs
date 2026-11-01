namespace HeatCaller;

/// <summary>
/// Makes the event the thing everything else hangs off.
///
/// RaceEvent has been in this app for a while, but only rounds ever
/// belonged to one. Check-in was a global list cleared by hand, tech was
/// judged by calendar day, the timetable was one list that never got
/// emptied, and a championship was a set of hand-picked sessions. Five
/// features, five different ideas of what "this race day" meant, and no
/// way to answer "who was at the June round" at all.
///
/// Two jobs here, both run once at startup:
///
///   Bind    - keeps every scoped store pointed at the current event, so
///             switching events switches the whole app rather than four
///             pages having to remember to be told.
///
///   Migrate - gives a home to everything recorded before any of this
///             existed. Nothing is deleted and nothing is reinterpreted;
///             orphans are attached to one clearly-labelled legacy event
///             and left alone.
///
/// Migrate is safe to run on every launch. It finds its own previous work
/// by a fixed id rather than by creating something new each time, so the
/// second run is a no-op.
/// </summary>
public static class EventSpine
{
    /// <summary>
    /// Points the scoped stores at the current event and keeps them there.
    ///
    /// Subscribing rather than passing the event store into each one: the
    /// stores load independently and in no guaranteed order, and a store
    /// that has to be constructed with another store is a store that can
    /// only be loaded in one order for ever.
    /// </summary>
    public static void Bind(
        RaceEventStore events,
        CheckInStore checkIns,
        TechInspectionStore tech,
        DayScheduleStore schedule,
        RoundDefinitionStore rounds)
    {
        void Point()
        {
            var current = events.CurrentId;

            checkIns.CurrentEventId = current;
            tech.CurrentEventId = current;
            schedule.CurrentEventId = current;
        }

        events.CurrentChanged += (_, _) => Point();

        Point();

        // The timetable no longer keeps its own running order - it merges the
        // programme's with the club's own lines, and this is how it reads the
        // programme. A hook rather than a constructor argument for the same
        // reason as everything else here: neither store knows the other's load
        // order, and one that has to be built with the other can only ever be
        // built in one order.
        schedule.ProgramOrder = () => Programme(events, rounds).Select(r => r.Id).ToList();

        // So a deleted race day's inspections - kept for its undo - stop
        // showing in a by-date report. See TechInspectionStore.EventExists.
        tech.EventExists = id => events.Get(id) is not null;
    }

    /// <summary>
    /// The current event's rounds, in programme order.
    ///
    /// One implementation, because there were two: Race Control's Next Up bar
    /// and the timetable each worked out "this day's rounds" for themselves,
    /// and the timetable is now sorted by this, so a second reading of it would
    /// be a second running order - which is the thing being removed.
    ///
    /// No real event current means no filtering: a club that has never started
    /// a race day sees everything it has, which is the only useful answer.
    /// </summary>
    public static List<RoundDefinition> Programme(RaceEventStore events, RoundDefinitionStore rounds)
    {
        var all = rounds.All;

        if (events.Current is { IsLegacy: false } current)
        {
            var members = new HashSet<string>(current.RoundIds, StringComparer.Ordinal);

            return all.Where(r => members.Contains(r.Id)).ToList();
        }

        return all.ToList();
    }

    /// <summary>
    /// Gives everything that pre-dates events a home. Returns a one-line
    /// summary of what moved, or null when there was nothing to do - the
    /// normal result on every launch after the first.
    /// </summary>
    public static string? Migrate(
        RaceEventStore events,
        CheckInStore checkIns,
        TechInspectionStore tech,
        DayScheduleStore schedule,
        RoundDefinitionStore rounds,
        SeasonStore seasons)
    {
        var moved = new List<string>();

        // The holder has to exist before anything can be attached to it.
        var legacy = events.EnsureLegacy();

        // --- Rounds with no event ---
        //
        // These are the ones that caused the original problem this class is
        // named after: qualifying gathered every recorded round for a
        // class, so a second race day folded in the first day's heats.
        // Putting the old ones in the legacy event is what stops them
        // joining a real event's qualifying.
        var orphanRounds = rounds.All
            .Where(r => events.EventForRound(r.Id) is null)
            .Select(r => r.Id)
            .ToList();

        if (orphanRounds.Count > 0)
        {
            foreach (var roundId in orphanRounds.Where(id => !legacy.RoundIds.Contains(id)))
            {
                legacy.RoundIds.Add(roundId);
            }

            events.Upsert(legacy);
            moved.Add($"{Plural.Of(orphanRounds.Count, "round")}");
        }

        // --- Check-in sheets and timetable lines with no event ---
        //
        // Only what was written before events existed (no event id at all).
        // Anything filed under an event that has since been deleted stays
        // under that id: this runs on every launch, and adopting it made a
        // deleted event's check-ins and run sheet reappear in the legacy
        // event. Left alone it is invisible, and back again if the delete
        // is undone.
        checkIns.AdoptOrphans();

        var orphanLines = schedule.AdoptOrphans();

        if (orphanLines > 0)
        {
            moved.Add($"{Plural.Of(orphanLines, "schedule line")}");
        }

        // --- Seasons that pick sessions ---
        //
        // Deliberately left as they are. A season's IncludedSessionIds
        // still scores exactly as it did, and rewriting somebody's
        // standings during an upgrade - even into something tidier - is
        // not a change they asked for. New seasons pick events; old ones
        // keep working until their owner chooses otherwise.
        _ = seasons;

        // --- Tech inspections with no event ---
        //
        // Also left alone, and for a sharper reason. An inspection with no
        // event falls back to the old "same calendar day" rule, which is
        // what it was recorded under. Stamping them with the legacy event
        // would take them out of that fallback without putting them into a
        // real one, and every car inspected this morning would come up
        // un-inspected this afternoon.
        _ = tech;

        return moved.Count == 0 ? null : $"Filed under \"{legacy.DisplayName}\": {string.Join(", ", moved)}.";
    }

    /// <summary>
    /// Starts a new race day and makes it current.
    ///
    /// Everything scoped follows immediately - an empty check-in sheet, an
    /// empty timetable, no inspections - which is the behaviour the old
    /// "remember to press Check Out Everyone" workflow was standing in for.
    /// </summary>
    /// <param name="shape">What kind of night it is. See <see cref="RaceDayShape"/>; it also sets the check-in rule.</param>
    /// <param name="classNames">The classes racing. Null leaves the day with none picked, as before.</param>
    /// <param name="qualifyingRounds">Rounds per class, or 0 to keep the default.</param>
    /// <param name="driversPerHeat">Heat size, or 0 to keep the default ("whatever was used last").</param>
    public static RaceEvent StartEvent(
        RaceEventStore events,
        string name,
        string venueName = "",
        string trackLayoutName = "",
        string championshipId = "",
        RaceDayShape shape = RaceDayShape.RaceDay,
        IEnumerable<string>? classNames = null,
        int qualifyingRounds = 0,
        int driversPerHeat = 0)
    {
        var raceEvent = new RaceEvent
        {
            Name = name,
            VenueName = venueName,
            TrackLayoutName = trackLayoutName,
            ChampionshipId = championshipId,
            Shape = shape,
            StartedUtc = DateTime.UtcNow,

            // A race day has a door; a club night does not. Set HERE rather
            // than left to the person, because this is the one place every
            // way of starting a day goes through - the Day tab, the Today
            // checklist, the banner on Who's Racing, the phone - and a
            // default that has to be found on another tab is not a default.
            // See RaceEvent.CheckedInDriversOnly.
            CheckedInDriversOnly = shape == RaceDayShape.RaceDay,
        };

        if (classNames is not null)
        {
            raceEvent.ClassNames = classNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Zero means "leave the default alone", so a caller that knows
        // neither - the phone, the first-launch day - gets exactly what it
        // got before this took arguments.
        if (qualifyingRounds > 0)
        {
            raceEvent.QualifyingRounds = qualifyingRounds;
        }

        if (driversPerHeat > 0)
        {
            raceEvent.DriversPerHeat = driversPerHeat;
        }

        events.Upsert(raceEvent);
        events.SetActive(raceEvent.Id);

        return raceEvent;
    }

    /// <summary>
    /// The name a day gets when nobody has given it one - and it follows
    /// the kind of night, because "Race Day 25 Sep" at the top of a club
    /// night is the app telling the club it is having a different evening
    /// from the one it said it was having.
    /// </summary>
    public static string DefaultDayName(DateTime localDate, RaceDayShape shape = RaceDayShape.RaceDay) =>
        shape == RaceDayShape.ClubNight
            ? $"Club Night {localDate:d MMM yyyy}"
            : $"Race Day {localDate:d MMM yyyy}";

    /// <summary>
    /// Starts a first race day for a club that has never had one. Returns
    /// the day it made, or null when there was nothing to do.
    ///
    /// WHY THIS EXISTS. A fresh install has exactly one event: the legacy
    /// holder, which is where everything recorded before race days existed
    /// lives. Nothing used to create a real one - not the first-run
    /// wizard, not the first race - so a club could run for months with
    /// Active null. That state is not harmless. Rounds are only filed
    /// against a day while one is active, so qualifying had no boundary:
    /// the second race day folded in the first day's heats and seeded its
    /// mains from a mixture of both, which is the exact fault race days
    /// were introduced to fix.
    ///
    /// WHY "NEVER" AND NOT "NOT RIGHT NOW". Closing a race day clears the
    /// current one (see RaceEventStore.Close), so a club that finishes the
    /// night properly has no day open until the next one starts. Starting
    /// one whenever none was open would hand them a new empty "Race Day 3
    /// Oct 2026" every morning they opened the app to look at last week's
    /// results. Having NEVER had one is a different thing: a fresh
    /// install, where leaving it that way is the trap.
    ///
    /// Anything else that needs a day - the Today checklist, the banner on
    /// Who's Racing - goes through RaceDayEventControl's own New Race Day
    /// instead, so there is one implementation with one undo entry.
    /// </summary>
    public static RaceEvent? StartFirstDayIfNever(RaceEventStore events)
    {
        if (events.Real.Count > 0)
        {
            return null;
        }

        return StartEvent(events, DefaultDayName(DateTime.Now));
    }

    /// <summary>
    /// Every session that counts towards a championship. THE answer to
    /// that question - there is deliberately only one.
    ///
    /// There were briefly two. This one existed first, written against a
    /// model that had not been added yet, so it had never compiled and
    /// nothing called it; the scoring rules were then built a second time
    /// in SeasonStore without noticing. Two implementations of "what
    /// counts" is the sort of thing that agrees for a season and then
    /// quietly disagrees about one round in October, so the rule lives
    /// here and the other one is gone.
    ///
    /// The shape: whatever the rule takes, minus what a person took out,
    /// plus whatever they put in by hand, de-duplicated - a season
    /// part-way through being converted from hand-picked to ruled must not
    /// score a driver twice for the same race.
    /// </summary>
    public static IReadOnlyList<SessionRecord> SessionsFor(
        Season season,
        RaceEventStore? events,
        RoundDefinitionStore rounds,
        SessionHistoryStore history)
    {
        var byId = new Dictionary<string, SessionRecord>(StringComparer.Ordinal);

        void Take(string? sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || byId.ContainsKey(sessionId))
            {
                return;
            }

            // A saved demo race never counts, even ticked in by hand: its
            // results are invented, and a championship that counted it
            // would show it in "rounds counted" even though it scores
            // nothing.
            if (history.Get(sessionId) is { } session && !session.IsNotScored())
            {
                byId[sessionId] = session;
            }
        }

        if (season.AutoInclude)
        {
            foreach (var session in history.All)
            {
                if (!RuleAccepts(season, session, rounds, events))
                {
                    continue;
                }

                // Taken out by hand - the rained-off round the hand-picked
                // design was built around. Checked here rather than inside
                // the rule so that an exclusion survives every future
                // change to the rule itself.
                if (season.ExcludedSessionIds.Contains(session.Id, StringComparer.Ordinal))
                {
                    continue;
                }

                // Ticked in by hand, below, a race run over still counts.
                if (SupersededByRerun(session, rounds, history))
                {
                    continue;
                }

                // The race in hand, rather than looked up again by its id:
                // a lookup that walks the whole history, for every race in
                // it, every time a table is worked out.
                if (!session.IsNotScored())
                {
                    byId.TryAdd(session.Id, session);
                }
            }
        }

        // Always, rule or no rule: this is both the whole story for a
        // hand-picked season and the force-in half of the override for a
        // ruled one.
        foreach (var sessionId in season.IncludedSessionIds)
        {
            Take(sessionId);
        }

        return byId.Values.OrderBy(s => s.StartedAtUtc).ToList();
    }

    /// <summary>
    /// Whether the rule takes a saved race: <see cref="RuleAccepts"/>, and
    /// not a race its round has since been run again for. The one test the
    /// championship's own list (SessionsFor) and the Seasons page's ticks
    /// both use, so a tick never disagrees with what counts.
    /// </summary>
    public static bool RuleTakes(
        Season season,
        SessionRecord session,
        RoundDefinitionStore rounds,
        RaceEventStore? events,
        SessionHistoryStore history) =>
        RuleAccepts(season, session, rounds, events) && !SupersededByRerun(session, rounds, history);

    /// <summary>
    /// Whether a saved race's round has been run again since: the round now
    /// names a later race. The earlier one still carries the round's id - put
    /// back by an undo, say - and counting it as well scored the heat twice.
    ///
    /// Only for a later race that is really there: a round naming a race
    /// that has gone, or an earlier one, leaves this race counting - which
    /// is also what keeps a race being filed now (saved a moment before its
    /// round is pointed at it) in the table straight after the flag.
    /// </summary>
    public static bool SupersededByRerun(SessionRecord session, RoundDefinitionStore rounds, SessionHistoryStore history) =>
        !string.IsNullOrEmpty(session.RoundId)
        && rounds.Get(session.RoundId) is { LinkedSessionId: { Length: > 0 } linked }
        && !string.Equals(linked, session.Id, StringComparison.Ordinal)
        && history.Get(linked) is { } later
        && later.StartedAtUtc > session.StartedAtUtc;

    /// <summary>
    /// Whether a recorded race is one this championship's rule would take.
    ///
    /// A session with no round behind it - a freeform race started without
    /// picking one off the programme - has no class and no kind, so it
    /// cannot satisfy a rule that asks for either. It is still reachable
    /// by ticking it, which is what the override is for.
    /// </summary>
    public static bool RuleAccepts(
        Season season,
        SessionRecord session,
        RoundDefinitionStore rounds,
        RaceEventStore? events)
    {
        // A saved demo race is simulated, so no rule ever takes it.
        if (session.SessionType != RaceSessionType.Race || session.IsNotScored())
        {
            return false;
        }

        var round = string.IsNullOrEmpty(session.RoundId) ? null : rounds.Get(session.RoundId);

        // Class-filtered here rather than afterwards, so a series for one
        // class does not have to sift a mixed day's results back apart.
        if (season.ClassName.Length > 0
            && !string.Equals(round?.ClassName, season.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (season.MainsOnly && round?.Kind != RoundKind.Main)
        {
            return false;
        }

        if (season.IncludedEventIds.Count > 0)
        {
            var owner = round is null ? null : events?.EventForRound(round.Id);

            // Two ways to say a race day belongs to a championship, and
            // both are honoured.
            //
            // The list is the season's own - ticked in Scoring Rules - and
            // was the only one scoring read. The other is the race day's
            // "Championship:" field, set on The Day, which was written,
            // saved, shown back on reopening, and then contributed exactly
            // nothing to any standings table. It read as the obvious way to
            // enter a day into a series and it was the one that did not
            // work.
            //
            // Answered here rather than by making the two ends write to
            // each other: an empty list means "count every race day", so
            // tagging one day cannot be stored as a one-entry list without
            // silently narrowing a season that was counting everything.
            // Reading both leaves every existing season scoring exactly as
            // it did, and makes the field on The Day mean what it says.
            var listed = owner is not null
                && (season.IncludedEventIds.Contains(owner.Id, StringComparer.Ordinal)
                    || string.Equals(owner.ChampionshipId, season.Id, StringComparison.Ordinal));

            if (!listed)
            {
                return false;
            }
        }

        return true;
    }
}
