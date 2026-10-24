namespace HeatCaller;

/// <summary>
/// The two history figures the pace detector needs, read off things the
/// club already keeps.
///
/// WHY THIS EXISTS (15 Oct 2026)
///
/// <see cref="PaceWatch"/> takes its history as three callbacks -
/// LayoutRecord, NightBest and PersonalBestBefore - so the whole detector
/// is a table test. All three were assigned ONLY in PaceWatchTests and
/// never once in the app.
///
/// So in the field the detector could only ever report "fastest of the
/// race so far", and the AI commentary could never say what a lap beat or
/// who held it - which PaceWatch's own class comment calls the entire
/// point: "a record is only a story if you can say what it beat and who
/// held it - 'fastest lap of the night' on its own is a number, and 'that
/// takes two tenths off Dave's mark from the second heat' is commentary."
///
/// It is the same shape of fault as the two record announcements that had
/// no caller, found the day before: a detector that passes its own tests
/// and is wired to nothing.
///
/// A CLASS RATHER THAN TWO LAMBDAS at the wiring site, because both have a
/// rule in them that is easy to get wrong and worth a test - see the
/// layout note on each.
/// </summary>
internal static class PaceHistory
{
    /// <summary>
    /// The all-time record for a named layout, in seconds, and who holds
    /// it - or null when there is no layout, no such layout, or no mark
    /// standing on it.
    ///
    /// Null means NO RECORD IS CLAIMED rather than a guess at one, the
    /// rule NightMoments.ForRace states for the same figure.
    /// </summary>
    /// <remarks>
    /// TAKES THE LAYOUTS AND NOT THE STORE, which is a correction to the
    /// first draft. TrackLayoutStore has a private constructor and its
    /// Upsert writes to %AppData% - so a signature taking the store cannot
    /// be tested without a test touching the club's real file. The same
    /// reasoning PaceWatch gives for taking its history as callbacks:
    /// everything here is arithmetic on plain records, and the store stays
    /// at the wiring site.
    /// </remarks>
    internal static (double Seconds, string Holder)? LayoutRecordFor(
        IReadOnlyList<TrackLayout>? layouts,
        string? layoutName)
    {
        if (layouts is null || string.IsNullOrWhiteSpace(layoutName))
        {
            return null;
        }

        var wanted = layoutName.Trim();

        foreach (var layout in layouts)
        {
            if (layout is { RecordLapMs: > 0 }
                && string.Equals(layout.Name?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return (layout.RecordLapMs / 1000.0, layout.RecordHolderName?.Trim() ?? string.Empty);
            }
        }

        return null;
    }

    /// <summary>
    /// The quickest lap anybody has turned tonight, in seconds, and who
    /// turned it - or null when nothing has been run yet.
    ///
    /// ON THIS LAYOUT ONLY, and that is the rule worth the test. Tonight's
    /// races can be on more than one layout - a club that moves the track
    /// between classes, or runs two configurations in an evening - and a
    /// lap set on a different layout is not a figure the one in front of
    /// the announcer can be compared to. Saying "that's the fastest of the
    /// night" against a different track is the announcer being wrong about
    /// something the room watched.
    ///
    /// "BEFORE THIS RACE" FALLS OUT OF WHERE THIS IS READ FROM rather than
    /// being filtered here: tonight's races are the SAVED ones, and the
    /// race being run has not been filed yet. A club that re-files a
    /// correction mid-evening gets the corrected figure, which is right.
    ///
    /// A GHOST IS NOT A DRIVER. A ghost is a replay of an old lap put on
    /// the track for somebody to race, so it must not hold the night's
    /// best - the same exclusion RacingField makes for the running order.
    ///
    /// NOR IS A DEMO RACE. A club rehearsing with Demo Mode produces
    /// invented lap times, and a club that has ticked "save demo races to
    /// history" would otherwise have an invented lap holding the night's
    /// best for every real race after it. Skipped through IsDemoSession
    /// rather than IsDemo, which is what NightMoments and SeasonPiece use
    /// and which also catches the older saves made before that flag was
    /// recorded.
    /// </summary>
    internal static (double Seconds, string Holder)? NightBestFor(
        IReadOnlyList<SessionRecord>? tonight,
        string? layoutName)
    {
        if (tonight is null || tonight.Count == 0 || string.IsNullOrWhiteSpace(layoutName))
        {
            return null;
        }

        var wanted = layoutName.Trim();

        long bestMs = 0;
        var holder = string.Empty;

        foreach (var race in tonight)
        {
            if (race is null
                || race.IsDemoSession()
                || !string.Equals(race.TrackLayoutName?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var entry in race.Results)
            {
                if (entry is null || entry.FastLapMs <= 0 || Ghosts.IsGhost(entry.Transponder))
                {
                    continue;
                }

                if (bestMs <= 0 || entry.FastLapMs < bestMs)
                {
                    bestMs = entry.FastLapMs;
                    holder = entry.DriverName?.Trim() ?? string.Empty;
                }
            }
        }

        return bestMs > 0 ? (bestMs / 1000.0, holder) : null;
    }

    /// <summary>
    /// Every driver's best lap on a named layout from races BEFORE THIS ONE,
    /// in seconds, keyed by transponder - the index PaceWatch.PersonalBestBefore
    /// reads (22 Oct 2026, known issues).
    ///
    /// WHY AN INDEX: the callback is asked once per driver per lap, and the
    /// only thing that knows a driver's history is the whole session store.
    /// Walking it every lap was the reason the callback was left unset for
    /// a week. This is built ONCE - per layout, per night, per change to the
    /// history - and each lap is then a dictionary look-up.
    ///
    /// The same exclusions as NightBestFor, for the same reasons: another
    /// layout's laps are not a figure to beat, a ghost is not a driver and a
    /// demo race's laps are invented. By transponder, as AchievementEvaluator
    /// counts a driver's personal bests.
    ///
    /// BEFORE THIS RACE, TONIGHT'S SAVED RACES INCLUDED (22 Oct 2026, bug
    /// hunt 9). This first skipped every race from tonight, on the belief
    /// that PaceWatch counts the driver's laps tonight itself. It does not:
    /// PaceWatch.Reset clears its per-driver bests at every race start, so
    /// it only knows the race being run. A driver who turned 7.80 in the
    /// heat and 7.90 in the main, against 8.00 from last week, was called a
    /// "personal best" for a lap slower than the one the room saw an hour
    /// earlier. Every saved race now counts, tonight's included - the race
    /// being run is not saved until it ends, the same fact NightBestFor
    /// relies on. Only a race dated AFTER tonight (a PC clock that was
    /// wrong) is still left out. The parameter and the callers keep the
    /// "tonight" names.
    ///
    /// A PERSONAL-BEST RESET COUNTS HERE TOO (23 Oct 2026, bug hunt 10).
    /// <paramref name="cutoffFor"/> is asked (driver name as saved, layout as
    /// saved) and a race at or before the answer is skipped for that driver -
    /// the same rule PersonalBestsControl.ComputeBests and DriverStats apply
    /// through PersonalBestResetStore.EffectiveCutoff. Null means no resets,
    /// which is what every caller got before.
    /// </summary>
    internal static Dictionary<string, double> PersonalBestsBefore(
        IEnumerable<SessionRecord>? sessions,
        string? layoutName,
        DateTime tonight,
        Func<string, string, DateTime?>? cutoffFor = null)
    {
        var bests = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (sessions is null || string.IsNullOrWhiteSpace(layoutName))
        {
            return bests;
        }

        var wanted = layoutName.Trim();
        var night = tonight.Date;

        foreach (var race in sessions)
        {
            if (race is null
                || race.IsDemoSession()
                || !string.Equals(race.TrackLayoutName?.Trim(), wanted, StringComparison.OrdinalIgnoreCase)
                // WHY (22 Oct 2026): was >= night, which dropped tonight's earlier
                // heats; > keeps them and still drops anything dated after tonight.
                || TonightAtTheTrack.NightOf(race.StartedAtUtc.ToLocalTime()) > night)
            {
                continue;
            }

            foreach (var entry in race.Results)
            {
                if (entry is null
                    || entry.FastLapMs <= 0
                    || string.IsNullOrWhiteSpace(entry.Transponder)
                    || Ghosts.IsGhost(entry.Transponder))
                {
                    continue;
                }

                // WHY (23 Oct 2026, bug hunt 10): this ignored the club's
                // personal-best resets, so the announcer measured a reset
                // driver against the lap the club had told the app to stop
                // counting and never called their new best. A race at or
                // before the driver's cutoff is now skipped, as on the
                // Personal Bests page.
                if (cutoffFor?.Invoke(entry.DriverName ?? string.Empty, race.TrackLayoutName ?? string.Empty) is { } cutoff
                    && race.StartedAtUtc <= cutoff)
                {
                    continue;
                }

                var seconds = entry.FastLapMs / 1000.0;
                var key = entry.Transponder.Trim();

                if (!bests.TryGetValue(key, out var held) || seconds < held)
                {
                    bests[key] = seconds;
                }
            }
        }

        return bests;
    }
}
