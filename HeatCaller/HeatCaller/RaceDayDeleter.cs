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
    /// The id of the round on track right now - staged, counting down,
    /// racing, or finished and not yet saved - or null when nothing is.
    /// Supplied once by MainForm, which owns Race Control, exactly as it
    /// supplies RaceProgramControl.RoundOnTrackId; neither page that offers
    /// this delete can see the race.
    ///
    /// Held here, rather than passed in by The Day and the Calendar, so the
    /// two gestures cannot disagree about it - the reason this class exists
    /// at all. A day's delete takes every round on it, and it used to take
    /// the one out on track with them: deleting a single round from the
    /// programme refuses that, and so does the phone, but deleting its
    /// whole day went straight past both, and the result was then saved
    /// against a round and a day that no longer existed. Null (not wired)
    /// keeps the old behavior.
    /// </summary>
    public static Func<string?>? RoundOnTrackId { get; set; }

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
        List<TrackLayout> SectorRecordTracks,
        List<Season> Seasons,
        int BadgeCount);

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

        if (RefuseIfOnTrack(owner, real))
        {
            return 0;
        }

        var gathered = real
            .Select(d => Gather(d, roundStore, history, seasonStore, layouts))
            .ToList();

        var counts = new DeleteRaceDayForm.Counts(
            gathered.Sum(g => g.Rounds.Count),
            gathered.Sum(g => g.Sessions.Count),

            // Distinct: one track's record can only be cleared once, however
            // many of these nights could have set it.
            // A layout with only a sector record from these nights counts
            // too: "Clear records" is what takes it away.
            gathered.SelectMany(g => g.RecordTracks.Concat(g.SectorRecordTracks)).Select(t => t.Id).Distinct(StringComparer.Ordinal).Count(),
            gathered.SelectMany(g => g.Seasons).Select(s => s.Id).Distinct(StringComparer.Ordinal).Count(),
            gathered.Sum(g => g.BadgeCount));

        using var ask = new DeleteRaceDayForm($"{real.Count} race days", counts);

        // Asked again once the answer is in: a heat can be put on track -
        // from the phone, say - while the dialog is up.
        if (ask.ShowDialog(owner) != DialogResult.OK || RefuseIfOnTrack(owner, real))
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

        if (RefuseIfOnTrack(owner, new[] { day }))
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
                gathered.RecordTracks.Concat(gathered.SectorRecordTracks).Select(t => t.Id).Distinct(StringComparer.Ordinal).Count(),
                gathered.Seasons.Count,
                gathered.BadgeCount));

        // Asked again once the answer is in - see AskMany.
        if (ask.ShowDialog(owner) != DialogResult.OK || RefuseIfOnTrack(owner, new[] { day }))
        {
            return false;
        }

        Remove(gathered, events, roundStore, history, seasonStore, layouts, undo,
            ask.DeleteHistory, ask.ClearRecords, ask.ClearSeasons, ask.ClearBadges, onChanged);

        return true;
    }

    /// <summary>
    /// The day among these that holds the round on track right now, or
    /// null. Matched on the day's own list of rounds, not on the rounds
    /// store, so a round on track is found even when the store no longer
    /// has it. Pure, so it can be tested without a window.
    /// </summary>
    internal static RaceEvent? DayWithRoundOnTrack(IEnumerable<RaceEvent> days, string? onTrackId)
    {
        if (string.IsNullOrEmpty(onTrackId))
        {
            return null;
        }

        string id = onTrackId;

        return days.FirstOrDefault(d => d.RoundIds.Contains(id, StringComparer.Ordinal));
    }

    /// <summary>
    /// Says no, and returns true, when one of these days holds the round on
    /// track - the same refusal the programme gives for a single round,
    /// for the same reason: its result would have nothing to be saved
    /// against. Refused outright rather than offered as a tick box, because
    /// there is no answer that makes deleting it mid-race safe; the day can
    /// go once that race has been saved.
    /// </summary>
    private static bool RefuseIfOnTrack(IWin32Window owner, IEnumerable<RaceEvent> days)
    {
        if (DayWithRoundOnTrack(days, RoundOnTrackId?.Invoke()) is not { } busy)
        {
            return false;
        }

        AppDialog.Show(owner,
            $"\"{busy.DisplayName}\" has a round on track - its result would have nothing to be saved against. Delete the day when that race has finished and been saved.",
            "Round on track", MessageBoxButtons.OK, MessageBoxIcon.Warning);

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
        // Read here only to COUNT the badges for the dialog, and then let go.
        // Nothing in this app holds a shared store: Race Control, the badges
        // page, the web server and the relay each load their own from disk
        // and re-read it. achievements.json is the source of truth, so the
        // only instance that can be trusted to be current is one read at the
        // moment of use - which is why Remove reads its own rather than
        // being handed this one. This copy was read before the dialog went
        // up (a heat can finish and award a badge while it is open) and,
        // when several days are deleted, before any of them was removed.
        // Saving it would write back every badge removed since it was read.
        var achievements = AchievementStore.Load();

        var rounds = day.RoundIds
            .Select(roundStore.Get)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        // The sessions that name one of the day's rounds as well as the ones
        // a round links to - either link is enough, as in
        // EventReportBuilder.SessionsFor. A heat run again keeps only the
        // newer run linked, and the earlier one, which still names the round,
        // used to survive the delete: with its round gone it no longer read
        // as superseded (EventSpine.SupersededByRerun), so rule-based
        // championships began scoring the race that had been thrown away.
        // Not a demo (never scored), and not one the history file has not
        // got yet (SessionHistoryStore.IsUnsaved) - that race is still Race
        // Control's to file, and is not one of the "saved races" the dialog
        // offers to delete.
        var dayRoundIds = day.RoundIds.ToHashSet(StringComparer.Ordinal);

        var sessionIds = rounds
            .Select(r => r.LinkedSessionId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Concat(history.Saved
                .Where(s => s.RoundId is not null && dayRoundIds.Contains(s.RoundId) && !s.IsDemoSession())
                .Select(s => s.Id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var sessions = sessionIds
            .Select(history.Get)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        var recordTracks = TracksWithRecordsFrom(layouts, sessions);
        var sectorRecordTracks = SectorRecordsFrom(layouts, sessions);

        var seasons = seasonStore.All
            .Where(s => s.IncludedSessionIds.Any(id => sessionIds.Contains(id, StringComparer.Ordinal)))
            .ToList();

        var badgeCount = achievements.All
            .Sum(d => d.Unlocked.Count(u => u.SessionId is { } id && sessionIds.Contains(id, StringComparer.Ordinal)));

        return new Gathered(day, rounds, sessionIds, sessions, recordTracks, sectorRecordTracks, seasons, badgeCount);
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
        var rounds = gathered.Rounds;
        var sessionIds = gathered.SessionIds;
        var sessions = gathered.Sessions;
        var seasons = gathered.Seasons;

        // Worked out again now, not taken from Gather. Gather runs before the
        // dialog, and a heat of tonight's day can take the record on the same
        // layout while it is up - Race Control writes it the moment the lap
        // is seen. Clearing the layouts gathered then blanked that new record
        // along with the old one, although nothing being deleted had set it.
        // The sector records below were always worked out again here.
        var recordTracks = TracksWithRecordsFrom(layouts, sessions);

        // --- Snapshot, for the undo ---

        var dayId = day.Id;

        // Whether the day was the current one, read before it goes. The undo
        // used to decide from the state at undo time alone - nothing current
        // and the day open - and after Close Day nothing is current, so
        // undoing the delete of an old open day made THAT the current race
        // day, and the next rounds built joined it. A day that was current
        // but closed was, the other way round, never made current again.
        var wasCurrent = string.Equals(events.Active?.Id, dayId, StringComparison.Ordinal);

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
                .Select(t => (Id: t.Id, Ms: t.RecordLapMs, Holder: t.RecordHolderName, SetUtc: t.RecordSetUtc, Sectors: t.RecordSectorsMs?.ToList(), Cut: t.RecordSectorCut))
                .ToList()
            : new List<(string Id, long Ms, string Holder, DateTime? SetUtc, List<long>? Sectors, string? Cut)>();

        // The sector records these sessions set (TrackLayout.SectorRecordsByCut):
        // a record whose holder ran exactly that time through that sector in
        // one of them goes with them, and the undo puts it back - each under
        // the cut it was set for.
        var sectorRecordsBefore = clearRecords
            ? SectorRecordsFrom(layouts, sessions)
                .Select(t => (Id: t.Id, Records: t.SectorRecordsCopy() ?? new Dictionary<string, List<SectorRecord>>()))
                .ToList()
            : new List<(string Id, Dictionary<string, List<SectorRecord>> Records)>();

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

            var unsaved = new List<SessionRecord>();

            // The rounds unlinked below from each unsaved session, so the
            // retry can link them again once it does save that session.
            var relinkOnSave = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

            if (sessionsJson is not null
                && System.Text.Json.JsonSerializer.Deserialize<List<SessionRecord>>(sessionsJson) is { } backSessions)
            {
                foreach (var session in backSessions.Where(s => history.Get(s.Id) is null))
                {
                    if (!history.Add(session))
                    {
                        unsaved.Add(session);
                    }
                }

                // The rounds above came back with the links they had, and
                // RestoreAll wrote them to the rounds file straight away. A
                // session whose Add just failed is listed in memory but held
                // out of race-history.json, so a round still linked to it
                // would, after a restart, claim a result that is not there:
                // shown as run, never offered again, and counted by
                // qualifying with nothing to score. Unlinked, the heat is
                // simply unrun - the state a deleted race leaves its round in
                // elsewhere - and can be run again. Done after the Adds
                // rather than by holding the rounds back, because which
                // sessions fail is only known once they have been tried.
                foreach (var session in unsaved)
                {
                    relinkOnSave[session.Id] = roundStore.ClearRecorded(session.Id);
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

                // With the cut they were taken under, not the loops as they
                // are now - see TrackLayout.RecordSectorsAsCutNow.
                track.KeepRecordSectors(record.Sectors);
                track.RecordSectorCut = record.Cut;
                layouts.Upsert(track);
            }

            // Sector records back as they were - only over a sector that
            // has no record now, or a slower one than was taken away.
            foreach (var (id, taken) in sectorRecordsBefore)
            {
                if (layouts.Get(id) is not { } track)
                {
                    continue;
                }

                track.RestoreSectorRecords(taken);
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

            // Current again only if it was current when it was deleted, and
            // nothing else has been made current since - that later choice
            // is not this undo's to take back. Open or closed as it was then.
            if (wasCurrent && events.Active is null && events.Get(dayId) is not null)
            {
                events.SetActive(dayId);
            }

            onChanged();

            // Last, so everything above is done either way - see
            // SessionHistoryStore.KeepRetryForUnsaved.
            //
            // When a retry does save one of these sessions, its rounds are
            // linked again - unlinked, the race would be back in Race
            // History while its heat still read as unrun. Only a round that
            // is still unlinked, though: one run again since has a newer
            // result, and writing the old link over it would make
            // qualifying score the race that was thrown away. And only a
            // round still in the store: the day may have been deleted again
            // in between.
            history.KeepRetryForUnsaved(undo!, unsaved, saved =>
            {
                if (!relinkOnSave.TryGetValue(saved.Id, out var roundIds))
                {
                    return;
                }

                foreach (var roundId in roundIds)
                {
                    if (roundStore.Get(roundId) is { } round && string.IsNullOrEmpty(round.LinkedSessionId))
                    {
                        roundStore.MarkRecorded(roundId, saved.Id);
                    }
                }

                onChanged();
            });
        });

        // --- Remove, working back out from the traces to the day itself ---

        if (clearBadges)
        {
            // A fresh read, at the moment of removal, for the reason given in
            // Gather. When several days go at once this runs once per day, so
            // each day's read already has the previous day's badges gone and
            // its save cannot put them back - and the undo entry above then
            // records exactly the badges this save really took away.
            var achievements = AchievementStore.Load();
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
                track.KeepRecordSectors(null);
                layouts.Upsert(track);
            }

            foreach (var track in SectorRecordsFrom(layouts, sessions))
            {
                if (track.SectorRecordsByCut is not { } byCut)
                {
                    continue;
                }

                foreach (var (cut, list) in byCut.ToList())
                {
                    var count = TrackLayout.CountOfCut(cut);
                    list?.RemoveAll(r => r is null || SetIn(sessions, track, count, r));

                    if (list is not { Count: > 0 })
                    {
                        byCut.Remove(cut);
                    }
                }

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
    ///
    /// The lap time is matched against the best lap as raced too. Taking a
    /// phantom crossing off a result in Edit Result makes its best lap
    /// slower (ResultEditor.RebuildLapTimes), and a record set by that
    /// crossing then matched nothing here - so deleting the day with
    /// "clear records" ticked left it standing.
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
                                (r.FastLapMs == track.RecordLapMs
                                 || r.AsRaced?.FastLapMs == track.RecordLapMs)
                                && string.Equals(
                                    r.DriverName.Trim(),
                                    track.RecordHolderName.Trim(),
                                    StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    /// <summary>The layouts with a sector record set in one of these sessions - see SetIn.</summary>
    private static List<TrackLayout> SectorRecordsFrom(TrackLayoutStore layouts, IReadOnlyCollection<SessionRecord> sessions) =>
        sessions.Count == 0
            ? new List<TrackLayout>()
            : layouts.All.Where(t => t.AllSectorRecords().Any(x => SetIn(sessions, t, x.Count, x.Record))).ToList();

    /// <summary>
    /// Whether the holder of this sector record, kept for laps of
    /// <paramref name="count"/> sectors, ran exactly that time through that
    /// sector on such a lap in one of these sessions, on this layout.
    ///
    /// The laps as raced count too. Taking a crossing off in Edit Result
    /// folds the last lap into the one before it and leaves the sectors
    /// alone, so that lap's sectors no longer add up to it and SectorsOf
    /// gives none - and the lap folded away is gone from the list. A sector
    /// record set on either was missed here, and left standing.
    /// </summary>
    internal static bool SetIn(IReadOnlyCollection<SessionRecord> sessions, TrackLayout track, int count, SectorRecord record) =>
        record.Sector >= 1 && record.Sector <= count
        && sessions.Any(s => RanOn(s, track) && s.Results.Any(r =>
            string.Equals((r.DriverName ?? string.Empty).Trim(), (record.Holder ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
            && Enumerable.Range(0, Math.Max(r.LapTimesMs.Count, r.AsRaced?.LapTimesMs.Count ?? 0))
                .Any(i => (r.SectorsOf(i) ?? RacedSectorsOf(r, i)) is { } sectors
                          && sectors.Length == count
                          && sectors[record.Sector - 1] == record.Ms)));

    /// <summary>A lap's sectors checked against the lap as the race recorded it - Edit Result never changes the sectors, so they stay in step with that list.</summary>
    private static long[]? RacedSectorsOf(SessionResultEntry result, int lapIndex) =>
        result.AsRaced is { LapTimesMs: { } raced } && result.SectorTimesMs is { } all
        && lapIndex >= 0 && lapIndex < all.Count && lapIndex < raced.Count
            ? Sectors.Checked(all[lapIndex], raced[lapIndex])
            : null;

    /// <summary>
    /// Whether this session could have set that layout's record - it named
    /// the layout, or it named none and so cannot rule itself out.
    /// </summary>
    private static bool RanOn(SessionRecord session, TrackLayout track) =>
        string.IsNullOrWhiteSpace(session.TrackLayoutName)
        || string.Equals(session.TrackLayoutName.Trim(), track.Name.Trim(), StringComparison.OrdinalIgnoreCase);
}
