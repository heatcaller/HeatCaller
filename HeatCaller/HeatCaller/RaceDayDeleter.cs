namespace HeatCaller;

/// <summary>
/// Deleting a race day, asked and carried out in ONE place.
///
/// It lives here rather than on the page that happened to grow the button
/// first. The Day deletes the day in its picker and the Calendar deletes
/// the one picked in its list, and those are two gestures onto the same
/// act - a delete that reached further from one page than the other is the
/// kind of difference nobody discovers until the evening it matters. The
/// Calendar's used to: it removed the race day and left its rounds, its
/// results, its records and its badges exactly where they were.
///
/// The dialog it shows, and the reasoning behind everything it does and
/// deliberately does not do, is in DeleteRaceDayForm.
/// </summary>
internal static class RaceDayDeleter
{
    /// <summary>
    /// Everything one race day would take with it, read before anything is
    /// removed. Gathering and removing are separate so that deleting SEVERAL
    /// days can ask its question once, over the totals, instead of putting
    /// the same dialog up five times - see AskMany.
    /// </summary>
    private sealed record Gathered(
        RaceEvent Day,
        List<RoundDefinition> Rounds,
        List<string> SessionIds,
        List<SessionRecord> Sessions,
        List<TrackLayout> RecordTracks,
        List<Season> Seasons,
        int BadgeCount,
        AchievementStore Achievements);

    /// <summary>
    /// Deletes several race days after one question, asked over the totals.
    ///
    /// The tick boxes apply to all of them together, which is the only
    /// answer that makes sense: somebody clearing out a season is not going
    /// to make a different call about records on the third night than the
    /// second. Each day still gets its own undo entry, so an undo takes
    /// back one night at a time - the alternative would be a single entry
    /// holding an unbounded amount of history.
    /// </summary>
    public static int AskMany(
        IWin32Window owner,
        IReadOnlyList<RaceEvent> days,
        RaceEventStore events,
        RoundDefinitionStore roundStore,
        SessionHistoryStore history,
        SeasonStore seasonStore,
        TrackLayoutStore layouts,
        UndoService? undo,
        Action onChanged)
    {
        var real = days.Where(d => !d.IsLegacy).ToList();

        if (real.Count == 0)
        {
            return 0;
        }

        if (real.Count == 1)
        {
            return Ask(owner, real[0], events, roundStore, history, seasonStore, layouts, undo, onChanged) ? 1 : 0;
        }

        var gathered = real
            .Select(d => Gather(d, roundStore, history, seasonStore, layouts))
            .ToList();

        var counts = new DeleteRaceDayForm.Counts(
            gathered.Sum(g => g.Rounds.Count),
            gathered.Sum(g => g.Sessions.Count),

            // Distinct: one track's record can only be cleared once, however
            // many of these nights could have set it.
            gathered.SelectMany(g => g.RecordTracks).Select(t => t.Id).Distinct(StringComparer.Ordinal).Count(),
            gathered.SelectMany(g => g.Seasons).Select(s => s.Id).Distinct(StringComparer.Ordinal).Count(),
            gathered.Sum(g => g.BadgeCount));

        using var ask = new DeleteRaceDayForm($"{real.Count} race days", counts);

        if (ask.ShowDialog(owner) != DialogResult.OK)
        {
            return 0;
        }

        foreach (var one in gathered)
        {
            Remove(one, events, roundStore, history, seasonStore, layouts, undo,
                ask.DeleteHistory, ask.ClearRecords, ask.ClearSeasons, ask.ClearBadges, onChanged);
        }

        return gathered.Count;
    }

    public static bool Ask(
        IWin32Window owner,
        RaceEvent day,
        RaceEventStore events,
        RoundDefinitionStore roundStore,
        SessionHistoryStore history,
        SeasonStore seasonStore,
        TrackLayoutStore layouts,
        UndoService? undo,
        Action onChanged)
    {
        if (day.IsLegacy)
        {
            return false;
        }

        var gathered = Gather(day, roundStore, history, seasonStore, layouts);

        // --- Ask ---

        using var ask = new DeleteRaceDayForm(
            day.DisplayName,
            new DeleteRaceDayForm.Counts(
                gathered.Rounds.Count,
                gathered.Sessions.Count,
                gathered.RecordTracks.Count,
                gathered.Seasons.Count,
                gathered.BadgeCount));

        if (ask.ShowDialog(owner) != DialogResult.OK)
        {
            return false;
        }

        Remove(gathered, events, roundStore, history, seasonStore, layouts, undo,
            ask.DeleteHistory, ask.ClearRecords, ask.ClearSeasons, ask.ClearBadges, onChanged);

        return true;
    }

    /// <summary>Reads everything a day would take with it. Changes nothing.</summary>
    private static Gathered Gather(
        RaceEvent day,
        RoundDefinitionStore roundStore,
        SessionHistoryStore history,
        SeasonStore seasonStore,
        TrackLayoutStore layouts)
    {
        // Loaded here rather than held, because nothing in this app holds a
        // shared one: Race Control, the badges page, the web server and the
        // relay each load their own from disk and re-read it. achievements
        // .json is the source of truth, so the only instance that can be
        // trusted to be current is one read at the moment of use - and the
        // undo below reads its own for the same reason.
        var achievements = AchievementStore.Load();

        var rounds = day.RoundIds
            .Select(roundStore.Get)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        var sessionIds = rounds
            .Select(r => r.LinkedSessionId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var sessions = sessionIds
            .Select(history.Get)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        var recordTracks = TracksWithRecordsFrom(layouts, sessions);

        var seasons = seasonStore.All
            .Where(s => s.IncludedSessionIds.Any(id => sessionIds.Contains(id, StringComparer.Ordinal)))
            .ToList();

        var badgeCount = achievements.All
            .Sum(d => d.Unlocked.Count(u => u.SessionId is { } id && sessionIds.Contains(id, StringComparer.Ordinal)));

        return new Gathered(day, rounds, sessionIds, sessions, recordTracks, seasons, badgeCount, achievements);
    }

    /// <summary>
    /// Carries out one day's delete, with the answers already given. The
    /// undo entry, the ordering and the reasoning are exactly as they were
    /// when this was the tail of Ask.
    /// </summary>
    private static void Remove(
        Gathered gathered,
        RaceEventStore events,
        RoundDefinitionStore roundStore,
        SessionHistoryStore history,
        SeasonStore seasonStore,
        TrackLayoutStore layouts,
        UndoService? undo,
        bool deleteHistory,
        bool clearRecords,
        bool clearSeasons,
        bool clearBadges,
        Action onChanged)
    {
        var day = gathered.Day;
        var achievements = gathered.Achievements;
        var rounds = gathered.Rounds;
        var sessionIds = gathered.SessionIds;
        var sessions = gathered.Sessions;
        var recordTracks = gathered.RecordTracks;
        var seasons = gathered.Seasons;

        // --- Snapshot, for the undo ---

        var dayId = day.Id;

        var dayJson = System.Text.Json.JsonSerializer.Serialize(day);
        var roundsJson = System.Text.Json.JsonSerializer.Serialize(rounds);
        var sessionsJson = deleteHistory ? System.Text.Json.JsonSerializer.Serialize(sessions) : null;

        // Named on BOTH sides. Left to infer, the true branch takes its
        // element names from the properties (RecordLapMs, RecordHolderName,
        // RecordSetUtc) while the false branch carries its own, so the
        // ternary's common type keeps only the names that happen to agree -
        // which was Id and nothing else.
        var recordsBefore = clearRecords
            ? recordTracks
                .Select(t => (Id: t.Id, Ms: t.RecordLapMs, Holder: t.RecordHolderName, SetUtc: t.RecordSetUtc))
                .ToList()
            : new List<(string Id, long Ms, string Holder, DateTime? SetUtc)>();

        var seasonsBefore = clearSeasons
            ? seasons.Select(s => (s.Id, Ids: s.IncludedSessionIds.ToList())).ToList()
            : new List<(string Id, List<string> Ids)>();

        IReadOnlyList<AchievementStore.RemovedAward> badgesRemoved = Array.Empty<AchievementStore.RemovedAward>();

        undo?.Capture($"Delete race day '{day.DisplayName}'", () =>
        {
            // The event first: the rounds below belong to it, and anything
            // reading the store while this runs should find a day that has
            // its programme rather than one that does not yet.
            if (System.Text.Json.JsonSerializer.Deserialize<RaceEvent>(dayJson) is { } backDay
                && events.Get(backDay.Id) is null)
            {
                events.Upsert(backDay);
            }

            if (System.Text.Json.JsonSerializer.Deserialize<List<RoundDefinition>>(roundsJson) is { } backRounds)
            {
                // Keeping their Order - see RoundDefinitionStore.RestoreAll.
                roundStore.RestoreAll(backRounds);
            }

            if (sessionsJson is not null
                && System.Text.Json.JsonSerializer.Deserialize<List<SessionRecord>>(sessionsJson) is { } backSessions)
            {
                foreach (var session in backSessions.Where(s => history.Get(s.Id) is null))
                {
                    history.Add(session);
                }
            }

            foreach (var record in recordsBefore)
            {
                if (layouts.Get(record.Id) is not { } track)
                {
                    continue;
                }

                // Put back only over the cleared field, or over a record
                // slower than the one being restored. A record is the
                // fastest lap ever driven on the layout, so restoring a
                // faster one is always right and taking back a faster one
                // never is - and between the delete and the undo a heat
                // can have run and taken the record for real.
                if (track.RecordLapMs > 0 && track.RecordLapMs <= record.Ms)
                {
                    continue;
                }

                track.RecordLapMs = record.Ms;
                track.RecordHolderName = record.Holder;
                track.RecordSetUtc = record.SetUtc;
                layouts.Upsert(track);
            }

            foreach (var before in seasonsBefore)
            {
                if (seasonStore.Get(before.Id) is not { } season)
                {
                    continue;
                }

                // Merged, not written back over: a race added to the season
                // since the delete - by a round that ran, or by hand - is
                // not this undo's to remove. The old order first, so the
                // list reads as it did, with anything new after it.
                var merged = before.Ids.ToList();

                merged.AddRange(season.IncludedSessionIds
                    .Where(id => !merged.Contains(id, StringComparer.Ordinal)));

                season.IncludedSessionIds = merged;
                seasonStore.Upsert(season);
            }

            if (badgesRemoved.Count > 0)
            {
                // A fresh read: badges may have been earned since, and this
                // must add to that file rather than write back over it.
                var live = AchievementStore.Load();
                live.RestoreAwards(badgesRemoved);
                live.Save();
            }

            // Current again only if nothing else has been made current
            // since - that later choice is not this undo's to take back.
            if (events.Active is null && events.Get(dayId) is { IsOpen: true })
            {
                events.SetActive(dayId);
            }

            onChanged();
        });

        // --- Remove, working back out from the traces to the day itself ---

        if (clearBadges)
        {
            badgesRemoved = achievements.RemoveForSessions(sessionIds);
            achievements.Save();
        }

        if (clearSeasons)
        {
            foreach (var season in seasons)
            {
                season.IncludedSessionIds = season.IncludedSessionIds
                    .Where(id => !sessionIds.Contains(id, StringComparer.Ordinal))
                    .ToList();

                seasonStore.Upsert(season);
            }
        }

        if (clearRecords)
        {
            foreach (var track in recordTracks)
            {
                track.RecordLapMs = 0;
                track.RecordHolderName = string.Empty;
                track.RecordSetUtc = null;
                layouts.Upsert(track);
            }
        }

        if (deleteHistory)
        {
            foreach (var session in sessions)
            {
                history.Delete(session.Id);
            }
        }

        foreach (var round in rounds)
        {
            roundStore.Delete(round.Id);
        }

        // Last, and it is what raises CurrentChanged when this was the
        // current day - so every page re-reads once, against a store that
        // has already finished changing.
        events.Delete(dayId);

        onChanged();
    }

    /// <summary>
    /// Track layouts whose record lap was set in one of these races.
    ///
    /// Matched on the LAYOUT the session ran on, the lap time and the
    /// holder's name, because a record carries no session id - it is three
    /// fields written onto the track layout when the lap was seen. Time
    /// alone would clear a record the same driver set on another night to
    /// the thousandth; name alone would clear their record whenever they
    /// appeared; and without the layout, a lap in tonight's races could
    /// clear an identical record standing on a completely different layout.
    /// Race Control writes both the record and the session's layout name
    /// from the same picker, so the two agree whenever the session names
    /// one at all - and a session that names none (an older one) is matched
    /// on time and name as before rather than being let off entirely.
    ///
    /// Together they are as close as the stored data allows, and the dialog
    /// says how many it found so nobody agrees to a number they cannot see.
    /// </summary>
    private static List<TrackLayout> TracksWithRecordsFrom(TrackLayoutStore layouts, IReadOnlyCollection<SessionRecord> sessions)
    {
        if (sessions.Count == 0)
        {
            return new List<TrackLayout>();
        }

        return layouts.All
            .Where(track => track.RecordLapMs > 0
                            && sessions.Any(s => RanOn(s, track) && s.Results.Any(r =>
                                r.FastLapMs == track.RecordLapMs
                                && string.Equals(
                                    r.DriverName.Trim(),
                                    track.RecordHolderName.Trim(),
                                    StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    /// <summary>
    /// Whether this session could have set that layout's record - it named
    /// the layout, or it named none and so cannot rule itself out.
    /// </summary>
    private static bool RanOn(SessionRecord session, TrackLayout track) =>
        string.IsNullOrWhiteSpace(session.TrackLayoutName)
        || string.Equals(session.TrackLayoutName.Trim(), track.Name.Trim(), StringComparison.OrdinalIgnoreCase);
}
