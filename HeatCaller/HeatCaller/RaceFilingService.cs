namespace HeatCaller;

/// <summary>
/// What filing a finished race needs from the screen that filed it, read
/// there and handed over - and the screen's own parts of the filing (the
/// badges, the recap, the recording, the results card, the "not saved"
/// dialog), which run as callbacks at exactly the points they always ran.
///
/// WHY (23 Oct 2026, timing layer, P2): filing moved out of Race Control into
/// <see cref="RaceFilingService"/>, which holds no WinForms control. Every
/// value the old code read off the page is a field here, and every thing it
/// did to the page is a callback, so nothing in the filing reaches into a
/// window. Each callback is optional: one left out is simply not done (a
/// filing with no page behind it - the tests - has no recap to speak).
/// </summary>
public sealed record RaceFilingInput
{
    // --- Read from the screen as the race is filed -------------------------

    /// <summary>The race was a Quick Race - read and let go by Race Control before filing (_currentRaceIsQuick).</summary>
    public bool WasQuickRace { get; init; }

    /// <summary>The race was a re-run of a recorded round - read and let go by Race Control before filing (_currentRaceIsRerun).</summary>
    public bool WasRerun { get; init; }

    /// <summary>The race was a simulation, as captured at its green (_currentRaceIsDemo).</summary>
    public bool IsDemo { get; init; }

    /// <summary>The track layout chosen for this race, or empty when none is.</summary>
    public string TrackLayoutName { get; init; } = string.Empty;

    /// <summary>
    /// The round this race is for (the Round box's choice, or the round a
    /// restored race was run for) - asked once, at the point the old code
    /// asked, and the page lets go of a restored race's round as it answers.
    /// </summary>
    public Func<RoundDefinition?>? TakeRound { get; init; }

    /// <summary>Which round a finished race should complete when none was picked - see RoundPicker.</summary>
    public Func<IReadOnlyList<RacerStanding>, RoundDefinition?>? PickNextRound { get; init; }

    /// <summary>Names the saved session, from the round it is filed against (BuildSessionLabel).</summary>
    public Func<RoundDefinition?, string>? BuildLabel { get; init; }

    // --- The page's own parts, at the points they always ran ---------------

    /// <summary>No recap of the last race may stay on offer (CommentaryService.ClearRaceSummary).</summary>
    public Action? ClearRaceSummary { get; init; }

    /// <summary>No recap will follow, so the end of the broadcast is signalled now (SignalSessionAudioFinishedAfterFinishCall).</summary>
    public Action? SignalBroadcastOver { get; init; }

    /// <summary>An unsaved demo is over, and no recap will read its round (DemoRound = null).</summary>
    public Action? DemoOver { get; init; }

    /// <summary>Which ghosts each driver finished ahead of, worked out from the board before the ghosts are left out.</summary>
    public Action<IReadOnlyList<RacerStanding>>? NoteGhostsBeaten { get; init; }

    /// <summary>A black flag from the warning ladder becomes a DQ on the record (ApplyBlackFlags).</summary>
    public Action<SessionRecord>? ApplyBlackFlags { get; init; }

    /// <summary>
    /// Just before the history write: the record and the board's rows handed
    /// to the race recording, and the filing whose recap is coming noted.
    /// </summary>
    public Action<SessionRecord, IReadOnlyList<RacerStanding>>? BeforeCommit { get; init; }

    /// <summary>The write landed: the race the packs are recorded against, and each driver's pack tagged.</summary>
    public Action<SessionRecord>? Filed { get; init; }

    /// <summary>A demo was saved: no badges, and no honours from the last real race.</summary>
    public Action? NoBadgesForDemo { get; init; }

    /// <summary>A real race was saved: its achievements.</summary>
    public Action<SessionRecord>? AwardAchievements { get; init; }

    /// <summary>The spoken summary of the race just saved, with the round it was filed against (or null).</summary>
    public Action<SessionRecord, RoundDefinition?>? SpeakSummary { get; init; }

    /// <summary>The shareable results card (TrySaveAutoResultsCard).</summary>
    public Action<SessionRecord>? SaveResultsCard { get; init; }

    /// <summary>The pages showing race-program state refresh (Race Control's SessionSaved).</summary>
    public Action? SessionSaved { get; init; }

    /// <summary>However the filing went, once it is over: the recap's filing let go, and a Quick Race's stamp released.</summary>
    public Action? FilingFinished { get; init; }

    /// <summary>The race could not be written: the dialog that says so (text, caption). It holds the UI thread until closed.</summary>
    public Action<string, string>? ShowNotSaved { get; init; }
}

/// <summary>
/// Files a finished race into the race history, and keeps the races that
/// could not be filed until they are.
///
/// WHY (23 Oct 2026, timing layer, P2): this was Race Control's
/// SaveCompletedSessionToHistory and its unfiled-race retry list, moved here
/// so filing lives with MainForm, which owns it, rather than in a window
/// control. Moved verbatim: the same order of operations (the history write,
/// MarkRecorded, SessionSaved, the journal let go, then the retries), the
/// same messages and the same retry rules. What the old code read from the
/// screen comes in through <see cref="RaceFilingInput"/>. The order of the
/// write itself is still RaceFiling.Commit's, where it is tested.
///
/// UI thread only, like everything else in the filing: the stores it writes
/// are UI-thread stores.
/// </summary>
public sealed class RaceFilingService
{
    private readonly RaceTiming _race;
    private readonly RaceJournalRecorder _journalRecorder;
    private readonly SessionHistoryStore _sessionHistory;
    private readonly ProfileStore _profiles;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly AppSettings _settings;
    private readonly RoundDefinitionStore? _rounds;
    private readonly RaceEventStore? _events;

    public RaceFilingService(
        RaceTiming race,
        RaceJournalRecorder journalRecorder,
        SessionHistoryStore sessionHistory,
        ProfileStore profiles,
        TrackLayoutStore trackLayouts,
        AppSettings settings,
        RoundDefinitionStore? rounds = null,
        RaceEventStore? events = null)
    {
        _race = race;
        _journalRecorder = journalRecorder;
        _sessionHistory = sessionHistory;
        _profiles = profiles;
        _trackLayouts = trackLayouts;
        _settings = settings;
        _rounds = rounds;
        _events = events;
    }

    /// <summary>Where the filing's lines go - the session log and console (MainForm's OnClientRawMessage).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>The crash journal this files against, and its recorder - one for the app.</summary>
    public RaceJournalRecorder JournalRecorder => _journalRecorder;

    private RaceJournal _journal => _journalRecorder.Journal;

    private void StatusMessage(string message) => Log?.Invoke(message);

    /// <summary>
    /// Whether finishing a race should arm the next one.
    ///
    /// The open race day's own answer, falling back to the club-wide
    /// setting for a day that never gave one - which is every day written
    /// before this moved onto the race day. See RaceEvent.AutoAdvanceOn.
    /// </summary>
    /// <remarks>Race Control's AutoAdvanceWanted, moved here (23 Oct 2026, timing layer, P2) so a retry with no page behind it reads the same answer.</remarks>
    internal static bool AutoAdvanceWantedFor(RaceEventStore? events, AppSettings settings) =>
        events?.Active is { IsLegacy: false } day
            ? day.AutoAdvanceOn(settings.AutoAdvanceRounds)
            : settings.AutoAdvanceRounds;

    private bool AutoAdvanceWanted => AutoAdvanceWantedFor(_events, _settings);

    /// <summary>
    /// Files the race that has just finished. Called by Race Control when the
    /// session raises RaceCompleted (after the finish call - see
    /// RaceFiling.AfterTheFlag), on the UI thread.
    /// </summary>
    public void FileCompletedRace(RaceFilingInput input)
    {
        // Read once and let go on every way out, so whatever runs next is
        // filed (or auto-advanced) as usual - see the auto-advance below.
        // (Race Control reads and lets go of its own flags as it builds the
        // input; these are those readings.)
        var wasQuickRace = input.WasQuickRace;
        var wasRerun = input.WasRerun;
        var currentRaceIsDemo = input.IsDemo;

        // WHY (23 Oct 2026, timing layer, P4b): threaded, the race as it
        // finished, frozen on the timing thread when it finished - the race
        // there has carried on since (a pass, a tick, a correction), and what
        // is filed must be what finished. Null inline, where the race read
        // below IS the race as it finished, and every read is as it was.
        var finished = _race.DeliveringFinishedRace;

        if (finished?.IsPracticeMode ?? _race.IsPracticeMode)
        {
            StatusMessage("[session] Practice session - not auto-saved (use Save Practice Session).");

            // Every way out of here clears the journal: the race is over
            // either way, and one left behind is offered back for recovery
            // on the next launch as if it had never finished.
            //
            // Unless an earlier race is still waiting to be filed: practice
            // is never journaled, so what is in the journal is that race,
            // and its only copy on disk. See _unfiledRaces.
            if (_unfiledRaces.Count == 0)
            {
                _journal.Clear();
            }

            return;
        }

        // Every race from here has its own ending - a recap, no recap, or
        // not saved at all - and none of them is the last race's. Cleared in
        // this one place rather than in each, so no path can leave the race
        // before's recap on offer (see CommentaryService.ClearRaceSummary).
        input.ClearRaceSummary?.Invoke();

        // Demo races are simulated, so they stay out of history unless the
        // user opts in. Even then the saved copy is marked as a demo and
        // scores nothing - no round marked as run, no championship points,
        // no badges, no personal bests or records. Every lap time in it was
        // invented, and a rehearsal quietly filling in a real heat (or
        // handing out a real driver's win) is worse than it not being saved.
        // A DEMO NIGHT IMPLIES SAVING. There is nothing to tie a round to
        // otherwise, so a club that asked to rehearse a whole night and
        // left this box unticked would watch every heat finish and the
        // program never move - with the reason buried in a setting on
        // another page. See AppSettings.DemoFillsTheProgramme.
        if (currentRaceIsDemo && !_settings.SaveDemoSessions && !_settings.DemoFillsTheProgramme)
        {
            StatusMessage(
                "[session] Demo race finished but was NOT saved - tick \"Save demo races to Race History\" "
                + "on the Demo Mode page to keep a copy there (marked [DEMO], never scored).");

            // Never reaches the recap, which is what normally signals the end
            // of the broadcast - so it is signalled here, or music set to
            // fade after the recap would play on after every unsaved demo.
            input.SignalBroadcastOver?.Invoke();

            // The demo is over, and no recap will read its round.
            input.DemoOver?.Invoke();

            // As practice above: a demo is never journaled, so with an
            // earlier race still unfiled the journal is that race's.
            if (_unfiledRaces.Count == 0)
            {
                _journal.Clear();
            }

            return;
        }

        var ordered = finished?.Standings ?? _race.GetOrderedStandings();

        // Ghosts belong on the live board - being there is the entire point
        // of them - but they are a pace reference, not entrants. A saved
        // result is what feeds finishing positions into the programme, the
        // championship and the qualifying figures, so a ghost taking third
        // did not just score the points for third: it pushed every real
        // driver behind it down a place and took a place off each of them.
        var racers = ordered.Where(s => !GhostService.IsGhost(s.TransponderId)).ToList();

        // Which ghosts each driver finished ahead of, for Ghost Buster and
        // Shadow Boxer - worked out here, from the board, because the saved
        // result below leaves the ghosts out. (The page's part - see
        // RaceFilingInput.NoteGhostsBeaten.)
        input.NoteGhostsBeaten?.Invoke(ordered);

        // Both of these used to return silently, which made a race that
        // finished but never appeared in history impossible to diagnose.
        if (racers.Count == 0)
        {
            StatusMessage(ordered.Count == 0
                ? "[session] Not saved - no racers had any recorded laps."
                : "[session] Not saved - only ghost cars were on track, and a ghost is not a result.");

            // Same as the unsaved demo above: no recap will follow, so the
            // end of the broadcast is signalled here instead.
            input.SignalBroadcastOver?.Invoke();

            // Finished, and nothing to save - a ghost-only race's journal
            // was left behind and offered back on the next launch. Not while
            // an earlier race is still unfiled, though: the journal is then
            // that race's only copy on disk, as at the other clears here.
            if (_unfiledRaces.Count == 0)
            {
                _journal.Clear();
            }

            return;
        }

        if (racers.Count != ordered.Count)
        {
            StatusMessage(
                $"[session] {Plural.Of(ordered.Count - racers.Count, "ghost car")} left out of the saved result - "
                + "they set the pace, they don't score.");
        }

        if (_journalRecorder.SessionStartedAtUtc is null)
        {
            // The start time is stamped when the race enters the Racing
            // state. If it's missing, this control never saw that
            // transition - so fall back to the session's own start rather
            // than throwing the results away.
            _journalRecorder.SessionStartedAtUtc = (finished is null ? _race.RaceStartUtc : finished.RaceStartUtc) ?? DateTime.UtcNow;
            StatusMessage(
                $"[session] Start time was missing - fell back to {AppTime.LogStamp(_journalRecorder.SessionStartedAtUtc.Value.ToLocalTime())}.");
        }

        // The rows themselves - see RaceFiling.ResultsFrom, which the
        // race-night test files races with too.
        //
        // Who jumped the start goes with each row (22 Oct 2026, driver
        // experience), so a driver's night can say "+5 s - jump start"
        // rather than a bare five seconds. The set is only cleared when the
        // next race is set up, so it is still this race's here.
        var results = RaceFiling.ResultsFrom(racers, _profiles, finished is null ? _race.HasJumpStarted : finished.HasJumpStarted);

        // The Round box's choice, or the round a restored race was run for.
        // (The page lets go of the restored race's round as it answers.)
        var round = input.TakeRound?.Invoke();

        // Nothing picked by hand? Take the next unrecorded round in program
        // order, so running races simply works through the heats. Without
        // this, a finished race quietly attached to nothing unless the user
        // remembered to choose a round first.
        //
        // Never for a demo: it would mark a real heat as run with invented
        // results, and seeding and the mains would then be built from them.
        //
        // Never for a Quick Race either. It drops the Round box to freeform
        // precisely so it is not filed against a heat, and without this the
        // fallback filed it against the next one anyway: a shakedown with
        // the Heat 1 drivers, run under the app's default format, became
        // Heat 1 - marked as run, gone from Next Up, and counted in
        // qualifying and on the club's website. A round picked by hand
        // after pressing Quick Race still counts; only the guess is skipped.
        var autoAdvanced = false;

        // Nor for a re-run of a round already recorded - see
        // _currentRaceIsRerun.
        if (round is null && _rounds is not null && AutoAdvanceWanted && !currentRaceIsDemo && !wasQuickRace && !wasRerun)
        {
            round = input.PickNextRound?.Invoke(racers);
            autoAdvanced = round is not null;
        }
        else if (round is null && wasQuickRace && !currentRaceIsDemo)
        {
            StatusMessage(
                "[session] Quick Race - kept in Race History on its own. A Quick Race is never filed against a heat "
                + "by itself; pick the round in the Round box first if one is meant to count.");
        }
        else if (round is null && wasRerun && !currentRaceIsDemo)
        {
            StatusMessage(
                "[session] Re-run of a round that was already recorded - kept in Race History on its own. "
                + "The round keeps its earlier result, and no other heat was marked as run.");
        }

        var record = new SessionRecord
        {
            SessionType = RaceSessionType.Race,

            // A demo keeps the round's name in its label (so it is clear
            // what was being rehearsed) but is never linked to the round.
            RoundId = currentRaceIsDemo ? null : round?.Id,
            IsDemo = currentRaceIsDemo,
            TrackLayoutName = input.TrackLayoutName,
            Label = input.BuildLabel?.Invoke(round) ?? "Race",
            StartedAtUtc = _journalRecorder.SessionStartedAtUtc.Value,
            EndedAtUtc = DateTime.UtcNow,
            Results = results,

            // Says this result carries rejected laps, laps led and grid
            // positions. Without it an older session looks identical to a
            // race nobody put a wheel wrong in.
            RecordsIncidents = true,
        };

        record.SectorCut = TrackLayout.CutOfResults(
            _trackLayouts.All.FirstOrDefault(t => !string.IsNullOrEmpty(record.TrackLayoutName) && t.Name == record.TrackLayoutName),
            results);

        // A black flag from the warning ladder becomes a DQ on the saved
        // result, logged as a correction so it can be seen and reversed in
        // Race History like any other.
        input.ApplyBlackFlags?.Invoke(record);

        // The race recording's copy of the result, and the filing whose
        // recap is coming - the page's part, see RaceFilingInput.BeforeCommit.
        input.BeforeCommit?.Invoke(record, racers);

        // The history write, and only if it landed, the round linked and the
        // journal let go - in RaceFiling, where the order is tested. What
        // this page does around it runs through the two callbacks, at the
        // points it always ran.
        RaceFiling.Outcome filed;

        try
        {
        filed = RaceFiling.Commit(
            record,
            round,
            autoAdvanced,
            AutoAdvanceWanted,
            _sessionHistory,
            _rounds,
            _journal,
            afterSaved: () =>
            {
                _journalRecorder.SessionStartedAtUtc = null;

                // What a Pack picked on a row's right-click is recorded
                // against, and each driver's pack in the car - the page's
                // part, see RaceFilingInput.Filed.
                input.Filed?.Invoke(record);

                if (record.IsDemo)
                {
                    // Cleared rather than left over from the last real race, so the
                    // results card never shows somebody else's badges. The honours
                    // segment too, for the same reason and in the same breath: this
                    // branch does not call AwardAchievements, so its own clearing of
                    // the plan never runs.
                    input.NoBadgesForDemo?.Invoke();

                    StatusMessage(
                        "[session] Demo race - kept in Race History marked [DEMO] only. It is not linked to a heat and "
                        + "earns no points, achievements, personal bests or records.");
                }
                else
                {
                    input.AwardAchievements?.Invoke(record);
                }

                // A demo is not filed against its round, but the show is
                // still the round's - the Technical Lead after a main, the
                // Team Principal - so the recap is told which it was. See
                // DemoRound.
                input.SpeakSummary?.Invoke(record, round);
            },
            beforeJournalCleared: () =>
            {
                input.SaveResultsCard?.Invoke(record);

                // Once, on every path that saved: a demo race and a race with
                // no round both still need the page refreshed. After the round
                // is linked - see RaceFiling.Commit.
                input.SessionSaved?.Invoke();
            },
            log: message => StatusMessage(message),
            raceDayOpen: RoundPicker.RaceDayOpen(_events),
            demoMayFillTheProgramme: _settings.DemoFillsTheProgramme);
        }
        finally
        {
            // The recap's filing let go, and (bug hunt 10) a Quick Race's
            // stamp released once this race is filed, however the filing
            // went - the page's part, see RaceFilingInput.FilingFinished.
            input.FilingFinished?.Invoke();
        }

        if (filed == RaceFiling.Outcome.NotSaved)
        {
            StatusMessage(
                $"[session] COULD NOT SAVE \"{record.Label}\" - race-history.json could not be written "
                + "(see the [storage] line above). The race is still in the crash journal and has NOT been lost.");

            // The session start is cleared either way: the race is over
            // whatever the disk did.
            _journalRecorder.SessionStartedAtUtc = null;

            // Kept, to be filed again - see _unfiledRaces. The store holds the
            // record back from every other save, so without this nothing
            // ever wrote it once the next race had taken over the journal.
            _unfiledRaces.Add(new UnfiledRace(record, round, autoAdvanced));
            _unfiledCount = _unfiledRaces.Count;

            // Commit stopped before afterSaved, so the recap never runs - and
            // the recap is what normally signals the end of the broadcast.
            // Without this, music set to fade or stop after the summary
            // played on through the dialog and into the gap between races,
            // as it once did after every unsaved demo (see above). Before
            // the dialog, which holds this thread until it is closed.
            input.SignalBroadcastOver?.Invoke();

            input.ShowNotSaved?.Invoke(
                $"\"{record.Label}\" could not be written to the race history - something else on this PC has the "
                + "file open, the disk is full, or the history could not be read when HeatCaller started (and so "
                + "is not being written over).\r\n\r\nThe race has NOT been lost: it is still in the crash "
                + "journal, and HeatCaller will offer it back the next time it starts. Close anything that might be "
                + "reading the HeatCaller folder (a backup or sync tool is the usual cause) and restart HeatCaller "
                + "before running the next race.\r\n\r\nIf you carry on instead, HeatCaller tries to save it again "
                + "as the next race starts and after each race - but until it is saved, closing HeatCaller or a "
                + "crash during the next race loses it.",
                "Race not saved");
        }
        else
        {
            // The journal has just been let go, so an earlier race that
            // could not be filed can be tried again without touching this
            // one's safety net - and the pages are refreshed for it.
            FileUnfiledRaces(refreshPages: input.SessionSaved ?? (() => { }));
        }
    }

    /// <summary>
    /// A finished race whose history write failed (RaceFiling.Outcome.NotSaved),
    /// with the round it was to be filed against.
    /// </summary>
    private sealed record UnfiledRace(SessionRecord Record, RoundDefinition? Round, bool AutoAdvanced);

    /// <summary>
    /// Races Add could not write, still waiting to be filed.
    ///
    /// SessionHistoryStore keeps such a record out of every later save (see
    /// its _unsaved) so it cannot ride along on an unrelated write and come
    /// back as a duplicate from its crash journal. That makes Add the only
    /// way it ever reaches disk - and nothing called Add again. The dialog
    /// says to restart before the next race, but nothing enforces it: a
    /// director who clicked OK and staged the next heat had the journal
    /// overwritten by that heat, which then filed fine and cleared it, and
    /// the failed A-main was in neither the history nor the journal at the
    /// next launch. So each one is kept here and handed to RaceFiling.Commit
    /// again - the same object, which Add treats as a retry - as the next
    /// race starts (before its journal replaces this one's) and after each
    /// race that files.
    ///
    /// UI thread only, like everything else in the filing.
    /// </summary>
    private readonly List<UnfiledRace> _unfiledRaces = new();

    /// <summary>
    /// Whether a finished race is still waiting to get into the race
    /// history - and so its crash journal is its only copy on disk, which
    /// nothing may clear. Asked by the Practice page before it clears the
    /// journal on Stop (PracticeModeControl.HasUnfiledRaces), and by Race
    /// Control's Reset before it clears it.
    /// </summary>
    public bool HasUnfiledRaces => _unfiledRaces.Count > 0;

    /// <summary>How many finished races are still waiting to get into the race history.</summary>
    public int UnfiledRaceCount => _unfiledRaces.Count;

    /// <summary>The session generation FileUnfiledRaces was last tried for from the journal's write - once per race is enough.</summary>
    /// <remarks>Volatile: threaded, UnfiledRetryDue reads it on the timing thread (23 Oct 2026, timing layer, P4b).</remarks>
    private volatile int _unfiledRetryGeneration = -1;

    /// <summary>
    /// _unfiledRaces.Count, kept beside the list for UnfiledRetryDue, which
    /// is asked on the timing thread (threaded mode) - the list itself is UI
    /// thread only. Set on the UI thread after every change to the list.
    /// </summary>
    private volatile int _unfiledCount;

    /// <summary>
    /// Whether RetryUnfiledBeforeJournal would have work to do now: a race
    /// waits to be filed, the race on the board is under way, and the retry
    /// has not been made for it yet. Safe from any thread - threaded, the
    /// journal's timing side asks it before every write
    /// (RaceJournalRecorder.RetryDueBeforeWrite), and holds the write back
    /// for the UI to retry first when it is true.
    /// </summary>
    /// <remarks>Added 23 Oct 2026 (timing layer, P4b). The same three tests as RetryUnfiledBeforeJournal, read from what the timing thread may read.</remarks>
    public bool UnfiledRetryDue() =>
        _unfiledCount > 0
        && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing
        && _unfiledRetryGeneration != _race.SessionGeneration;

    /// <summary>
    /// Tries again to file every race in _unfiledRaces, through the same
    /// RaceFiling.Commit, so the round is linked and the journal let go only
    /// once the write has landed. Only called when the crash journal holds
    /// nothing newer than those races - before the next race's first journal
    /// write, or just after a race has been filed and its journal cleared -
    /// since Commit clears the journal on success.
    ///
    /// Quiet about the result card, the badges and the spoken summary: by now
    /// another race is on the board, and announcing or carding the earlier
    /// one would put it over the race the room is watching.
    /// </summary>
    /// <param name="refreshPages">
    /// Null while the next race is starting: SessionSaved rebuilds the Round
    /// box, which must not move under a race being staged. The race's own
    /// filing raises it afterwards anyway.
    /// </param>
    private void FileUnfiledRaces(Action? refreshPages)
    {
        if (_unfiledRaces.Count == 0)
        {
            return;
        }

        foreach (var unfiled in _unfiledRaces.ToList())
        {
            var record = unfiled.Record;

            // Deleted from Race History meanwhile (Delete forgets it in the
            // store too): filing it now would bring back a race the director
            // threw away.
            if (!ReferenceEquals(_sessionHistory.Get(record.Id), record))
            {
                // Nothing will file it now, so an undo of that delete must
                // write it itself - see SessionHistoryStore.RestoreUnfiled.
                _sessionHistory.ForgetDeletedUnfiled(record);
                _unfiledRaces.Remove(unfiled);
                _unfiledCount = _unfiledRaces.Count;
                continue;
            }

            // The round only if it is still waiting for a result. Another
            // race may have been filed against it since - a rerun, or
            // auto-advance picking the same heat, since this one never marked
            // it as run - and linking it again would take that race's result
            // off the heat.
            var round = unfiled.Round is { } wanted && _rounds?.Get(wanted.Id) is { IsRecorded: false } current
                ? current
                : null;

            if (unfiled.Round is not null && round is null)
            {
                StatusMessage(
                    $"[session] \"{record.Label}\" is being saved now, but not against \"{unfiled.Round.RoundLabel}\" - "
                    + "that heat has been filed by another race (or removed) since. Link it in Race History if it belongs there.");
            }

            var outcome = RaceFiling.Commit(
                record,
                round,
                unfiled.AutoAdvanced && round is not null,
                AutoAdvanceWanted,
                _sessionHistory,
                _rounds,
                _journal,
                afterSaved: () => { },
                beforeJournalCleared: () =>
                {
                    if (refreshPages is not null)
                    {
                        refreshPages();
                    }
                },
                log: message => StatusMessage(message),
                raceDayOpen: RoundPicker.RaceDayOpen(_events),
                demoMayFillTheProgramme: _settings.DemoFillsTheProgramme);

            if (outcome == RaceFiling.Outcome.Saved)
            {
                _unfiledRaces.Remove(unfiled);
                _unfiledCount = _unfiledRaces.Count;
                StatusMessage(
                    $"[session] \"{record.Label}\", which could not be saved earlier, is now in the race history. "
                    + "Its achievements and personal bests were not worked out and its summary was not spoken.");
            }
        }
    }

    /// <summary>
    /// At a new race's first journal write, an earlier race that could not be
    /// filed is tried again first: the journal still holds that race, and the
    /// write that follows is what replaces it. Once per race, not on every
    /// lap - each try writes the whole history file.
    /// </summary>
    /// <remarks>
    /// The first half of Race Control's WriteJournal, moved here (23 Oct 2026,
    /// timing layer, P2) and run by the journal recorder before each write
    /// (RaceJournalRecorder.BeforeEachWrite), so it happens however the
    /// journal comes to be written.
    /// </remarks>
    public void RetryUnfiledBeforeJournal()
    {
        if (_unfiledRaces.Count > 0
            && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing
            && _unfiledRetryGeneration != _race.SessionGeneration)
        {
            _unfiledRetryGeneration = _race.SessionGeneration;
            FileUnfiledRaces(refreshPages: null);

            if (_unfiledRaces.Count > 0 && !_race.IsDemoMode && !_race.IsPracticeMode)
            {
                StatusMessage(
                    $"{AppTime.LogStampNow()}  [session] WARNING: {Plural.Of(_unfiledRaces.Count, "earlier race")} still could "
                    + "not be saved to race-history.json, and this race's crash journal is about to replace it. It is "
                    + "kept in memory and tried again after this race - do not close HeatCaller until the log says it "
                    + "is in the race history.");
            }
        }
    }

    /// <summary>
    /// Lets go of the crash journal after a decision that ends the race
    /// (Reset, or undoing into a reset) - unless an earlier race is still
    /// waiting to be saved, since the journal is then the only copy of it on
    /// disk (see FileUnfiledRaces).
    /// </summary>
    public void ClearJournalUnlessUnfiled()
    {
        if (_unfiledRaces.Count == 0)
        {
            _journal.Clear();
        }
    }
}
