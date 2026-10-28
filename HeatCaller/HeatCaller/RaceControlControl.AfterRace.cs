namespace HeatCaller;

/// <summary>
/// After the flag: achievements, the spoken race summary, saving the race to
/// history, the results card, the crash journal and race recovery.
///
/// Moved here unchanged from RaceControlControl.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>
    /// Writes the recap AND renders it to audio as one job, so "ready"
    /// means there is something to play rather than something still to
    /// synthesise. Returns nulls when the model gave nothing back.
    /// </summary>
    private async Task<(string? Spoken, AnnouncementService.PreparedSpeech? Audio)> BuildRecapAsync(
        Task<string?> generation, (double, double)? delivery, CancellationToken nextRace = default)
    {
        var line = await generation;

        // Not rendered once the next race is called (22 Oct 2026, bug hunt):
        // nobody will hear it, and a long recap's render held one of the two
        // synthesis slots - and spent the characters - well into that race.
        if (string.IsNullOrWhiteSpace(line) || nextRace.IsCancellationRequested)
        {
            return (null, null);
        }

        // Tags added here rather than by the model, so an Enthusiastic
        // recap is excited every time instead of only when the model
        // decides the race deserved it.
        var spoken = _commentary!.PrepareSummaryForSpeech(line);

        // Null when voice isn't configured or the render failed - the
        // caller falls back to speaking it live.
        // In the recap host's voice - the recap is the host's (16 Oct 2026).
        // The token too (22 Oct 2026, known issues): a render already under
        // way when the next race is called is stopped, not run to the end.
        var audio = await _announcer.PrepareReminderAsync(spoken, delivery, RecapHostVoice, nextRace);

        return (spoken, audio);
    }

    /// <summary>
    /// How many badge calls a single race may make out loud.
    ///
    /// A driver's first ever race can unlock several at once, and reading
    /// out six in a row turns a celebration into a list. The rest are still
    /// earned and still shown - they just aren't announced.
    /// </summary>
    private const int MaxSpokenAchievements = 3;

    /// <summary>
    /// True from a Quick Race press (_actQuickRace) until that race is saved,
    /// staged over or reset - not by a Stage that only restarts its start
    /// (see RaceKindAfterStage). A Quick Race is filed on its own, never against
    /// the next heat by auto-advance - see SaveCompletedSessionToHistory.
    /// Nothing else about the race says it was one: the Round box is simply
    /// freeform, as it is for any race nobody picked a round for.
    /// </summary>
    private bool _currentRaceIsQuick;

    /// <summary>
    /// True from the Stage of a race started as a re-run of a round that has
    /// already been recorded (see MarkNextStageAsRerun) until that race is
    /// saved, staged over or reset - as for _currentRaceIsQuick. Like a Quick
    /// Race it is never filed by auto-advance's guess.
    ///
    /// The Round box only lists unrun rounds, so a re-run races with the box
    /// on freeform - and the guess then filed it against the same drivers'
    /// NEXT unrun heat: Q1 Heat 1 run again became Q2 Heat 1, marked as run
    /// with the re-run's laps, gone from Next Up and counted in qualifying.
    /// The director had just been told a re-run "records a new session and
    /// won't replace the existing result", so it is kept on its own instead.
    /// </summary>
    private bool _currentRaceIsRerun;

    /// <summary>
    /// Set by MarkNextStageAsRerun and taken up by the next Stage, which is
    /// where a race begins - a program start can open a warm-up first, so
    /// the Stage may come a while after the start was asked for. Or by a
    /// Countdown or first Green pressed during that warm-up instead, which
    /// start the race without a Stage. Dropped by Quick Race and Reset,
    /// which mean some other race is wanted.
    /// </summary>
    private bool _rerunPending;

    /// <summary>
    /// Says the race about to be staged is a re-run of a round that has
    /// already been recorded, so it is kept in Race History on its own
    /// rather than filed against the next heat by auto-advance. For the
    /// host to call just before TriggerAutoStart.
    /// </summary>
    public void MarkNextStageAsRerun() => _rerunPending = true;

    /// <summary>
    /// Whether the race a Stage leaves on the board is a Quick Race, and
    /// whether it is a re-run of a recorded round.
    ///
    /// A Stage that begins a new race starts it as neither, unless the host
    /// asked for a re-run (<paramref name="rerunPending"/>). A Stage pressed
    /// again during a start's staging or countdown restarts that same start
    /// - as the log says to after a caution - so it keeps what it was. It
    /// used to clear both every time, and a Quick Race or a re-run restaged
    /// before its green was filed against the drivers' next heat by
    /// auto-advance at the flag. Taking over a demo is a new race, whatever
    /// state the demo was in.
    /// </summary>
    /// <param name="stateBefore">The race's state when Stage was pressed, before any demo was ended.</param>
    internal static (bool IsQuick, bool IsRerun) RaceKindAfterStage(
        RaceState stateBefore, bool tookOverDemo, bool isQuick, bool isRerun, bool rerunPending)
    {
        if (!tookOverDemo && stateBefore is RaceState.Staging or RaceState.Countdown)
        {
            return (isQuick, isRerun || rerunPending);
        }

        return (false, rerunPending);
    }

    /// <summary>
    /// Works out what the race just earned everyone and says the best of it.
    ///
    /// Deliberately after the result is saved and before the wrap-up: the
    /// evaluator needs the finished result, and the calls should land with
    /// the finish rather than a minute later behind the recap.
    ///
    /// Never allowed to interrupt the end of a race - badges are decoration,
    /// and a bad one must not be the reason a result goes unsaved.
    /// </summary>
    private void AwardAchievements(SessionRecord record)
    {
        // Cleared FIRST, before any of the ways out below. A plan left over
        // from the last race is a recap that reads out somebody else's
        // badges, and the ways out are the quiet ones: an unreadable store,
        // a race that unlocked nothing, a fault in the evaluator. Taken
        // again by SpeakRaceSummary, so this is the second of two guards
        // rather than the only one.
        _honours = null;

        try
        {
            // Loaded per race rather than held: this is the only place in
            // Race Control that touches it, and once a race is not a cost
            // worth widening the constructor for.
            var store = AchievementStore.Load();

            // A file that could not be read this time loads as no badges at
            // all, and every driver would be told over the PA they had just
            // won their First Race badge again. Nothing is lost by skipping:
            // the store will not save while it is held (LoadedUnreadable).
            if (store.LoadedUnreadable)
            {
                StatusMessage?.Invoke("[achievements] achievements.json could not be read just now, so achievements were not worked out for this race.");
                return;
            }

            // The profiles too, so a guest marked "Exclude from records", a
            // rental or a ghost does not take the Track Record badge.
            // Where the app is used, the garage, the packs and the ghosts
            // beaten - for the home and garage badges.
            // And the track layouts, so Record Holder goes with the record
            // they show - found by name the way CheckAndAnnounceTrackRecord
            // finds the one it sets.
            var context = (AchievementStores ?? new AchievementContext()) with
            {
                RacingAt = _settings.RacingAt,
                Profiles = _profiles,
                GhostsBeaten = _ghostsBeatenAtFlag,
                TrackLayoutNamed = name => _trackLayouts.All.FirstOrDefault(t => t.Name == name),
            };

            var unlocks = AchievementEvaluator.EvaluateFinishedRace(
                record, _sessionHistory.All, store, RivalryStore.Load(), _profiles, context);

            if (unlocks.Count == 0)
            {
                return;
            }

            foreach (var unlock in unlocks)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [achievements] {unlock.DriverName} unlocked \"{unlock.Badge.Name}\""
                    + (string.IsNullOrWhiteSpace(unlock.Detail) ? "." : $" ({unlock.Detail})."));
            }

            _lastUnlocks = unlocks;
            _achievements = store;

            AchievementsUnlocked?.Invoke(unlocks);

            // THE BADGES ARE SAID ONCE, AND THIS IS THE PLACE THAT DECIDES.
            //
            // There are two ways a club can hear them now: these calls, one
            // per badge, right behind the finish call - and the honours
            // segment in the recap, where the announcer throws to a third
            // voice who goes over who got what. Both, and the room hears
            // every badge twice four minutes apart, which is the single most
            // noticeable thing an announcer can do wrong.
            //
            // So it is one or the other, decided here where the unlocks are
            // in hand, and decided by BUILDING the segment rather than by
            // asking whether it is switched on. That distinction is the
            // whole safety of this: a plan in hand is a segment that is
            // going to be spoken, where "the box is ticked" is not. The
            // rotation can have nothing left to say tonight, the page can be
            // running without an announcer screen, a badge can have no name
            // that could be read out - and on any of those the badges would
            // have been suppressed here and then never read at all.
            // PlanHonours answers null for every one of them, says so in the
            // console, and the individual calls go out as they always did.
            //
            // The club's own switch for these calls is on Voice Settings ->
            // Announcements and is NOT touched. A second switch for the same
            // thing, in another place, silently rewritten from here, is
            // worse than either arrangement on its own.
            //
            // 17 Oct 2026, Bug Hunt 8, A04-7: not while segments are held.
            // Hold at the flag calls the whole recap sequence off, honours
            // and all, so badges kept back for it were never heard. Held,
            // they are called one at a time as they always were.
            if (Commentary?.Invoke()?.Held != true)
            {
                _honours = PlanHonours(unlocks);
            }
            else
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [achievements] Segments are held, so the achievements are called one at a time rather than kept for the recap's achievements segment.");
            }

            if (_honours is not null)
            {
                // BOTH numbers when they differ. The read is capped - four
                // drivers, three badges each - and one badge going to
                // several drivers is named once in the count, so "held
                // back" and "read out" are honestly different figures. With
                // only the first of them logged, a club whose honours read
                // shorter than the log promised had nothing to go on.
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [achievements] {Plural.Of(unlocks.Count, "achievement")} held back for the recap's achievements segment "
                    + "rather than called out one at a time"
                    + (_honours.Badges > 0 && _honours.Badges != unlocks.Count
                        ? $" - the read names {Plural.Of(_honours.Badges, "achievement")}."
                        : "."));

                return;
            }

            foreach (var unlock in unlocks.Take(MaxSpokenAchievements))
            {
                _announcer.NotifyAchievement(unlock.Transponder, unlock.Badge.Name);
            }

            if (unlocks.Count > MaxSpokenAchievements)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [achievements] {unlocks.Count - MaxSpokenAchievements} more unlocked but not announced - they're on the driver's record.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [achievements] Couldn't work out achievements for this race: {ex.Message}");
        }
    }

    /// <summary>
    /// The stores the home and garage badges read (setups, cars, packs, the
    /// ghosts as set up) - set by MainForm. Without it those badges are
    /// simply not judged.
    /// </summary>
    public AchievementContext? AchievementStores { get; set; }

    /// <summary>The ghosts each driver beat in the race just filed - see SaveCompletedSessionToHistory.</summary>
    private IReadOnlyDictionary<string, IReadOnlyList<GhostCar>> _ghostsBeatenAtFlag =
        new Dictionary<string, IReadOnlyList<GhostCar>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised with everything a finished race unlocked, for anything that wants to show it.</summary>
    public event Action<List<AchievementUnlock>>? AchievementsUnlocked;

    /// <summary>
    /// What the race just finished unlocked, held so the results card can
    /// print it. Cleared at each green, or a card would carry the last
    /// race's badges.
    /// </summary>
    private List<AchievementUnlock> _lastUnlocks = new();

    /// <summary>
    /// The honours segment for the race just filed - or null when the
    /// badges went out as individual calls instead, which is every race at
    /// a club that has not switched the segment on.
    ///
    /// Held for ONE HOP between two methods that always run one after the
    /// other on the UI thread (see the save path in
    /// SaveCompletedSessionToHistory: AwardAchievements, then
    /// SpeakRaceSummary). TAKEN and not read, and cleared at the top of
    /// AwardAchievements as well - a plan left lying about is the next
    /// race's recap reading out this race's badges, the same stale-state
    /// fault <see cref="_lastUnlocks"/> is cleared at each green to avoid.
    /// </summary>
    private HonoursPlan? _honours;

    /// <summary>
    /// The recap show running now - teaser, recap, honours, colour, the
    /// mechanic, the sign-off - or null when none is.
    ///
    /// SKIP AND HOLD STOP IT (17 Oct 2026). The sequence only ever stopped
    /// when the next race was called to the grid; Skip on the announcer
    /// strip asked ShowTreatment to abandon a segment block, and the recap
    /// opens its show without one, so Skip said "none is on air" over a
    /// recap the whole room could hear. Held here so Skip and Hold can cancel
    /// the same token the next race does, and the sequence then ends by the
    /// one way out it already has for that: the show ended, the club's
    /// music given back, the deep duck and the floor released. See
    /// <see cref="EndRecapShow"/>.
    /// </summary>
    private RecapShowRun? _recapShow;

    /// <summary>
    /// The race whose recap was put on air (its SessionGeneration), or -1.
    /// Read by <see cref="OnAirFollowsTheRecap"/>.
    /// </summary>
    private int _recapAiredGeneration = -1;

    /// <summary>The token a recap show stops on, and who stopped it when it was not the next race.</summary>
    private sealed class RecapShowRun
    {
        public RecapShowRun(CancellationTokenSource stop) => Stop = stop;

        public CancellationTokenSource Stop { get; }

        /// <summary>
        /// Why it was stopped, in words for the console, when Skip, Hold or
        /// On Air stopped it. Null when the next race's own staging stopped
        /// it. Not a test for "the next race was called" - On Air sets it
        /// too; see <see cref="NextRaceCalled"/>.
        /// </summary>
        public volatile string? StoppedBy;

        /// <summary>
        /// WHY (22 Oct 2026): the next race was called while this show ran -
        /// staged, counting down, started, or On Air pressed for it. The
        /// sequence's finally used to take "StoppedBy is set" to mean Skip or
        /// Hold, but On Air sets StoppedBy too, so a recap stopped by On Air
        /// read the last race's held badges over the next race's opening.
        /// Kept apart from StoppedBy, and set even after Skip or Hold had
        /// already stopped the show, so the badges stay unsaid either way.
        /// </summary>
        public volatile bool NextRaceCalled;
    }

    /// <summary>
    /// The reason On Air gives <see cref="EndRecapShow"/> (RaceControlControl.OnAir).
    /// Recognised there as the next race being called - see
    /// <see cref="RecapShowRun.NextRaceCalled"/>.
    /// </summary>
    internal const string OnAirPressedForNextRace = "On Air was pressed for the next race";

    /// <summary>
    /// Ends the recap show that is running, as the next race being called
    /// would (17 Oct 2026) - see <see cref="_recapShow"/>. What has already
    /// been queued to the voice finishes its sentence, as Skip's own note
    /// says it must: the queue also holds the flag calls. Nothing after it
    /// starts. False when no recap show is running.
    /// </summary>
    private bool EndRecapShow(string why, bool nextRace = false)
    {
        if (Volatile.Read(ref _recapShow) is not { } run)
        {
            return false;
        }

        // WHY (22 Oct 2026): On Air calling off the recap is the next race
        // being called (its own comment says so), and the held badges must
        // not then be read over that race's opening. Recorded before the
        // already-stopped check, so a Skip pressed first still leaves the
        // flag set. On Air's reason is matched as well as the flag, so this
        // holds whether or not its call passes nextRace.
        if (nextRace || why == OnAirPressedForNextRace)
        {
            run.NextRaceCalled = true;
        }

        if (run.Stop.IsCancellationRequested)
        {
            return false;
        }

        run.StoppedBy ??= why;

        // Async, as the next race's own cancel is: Cancel() would run the
        // sequence's continuations here, on the UI thread, inside a click.
        try
        {
            _ = run.Stop.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The sequence had already ended.
        }

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [show] Recap show stopped - {why}. The line being spoken finishes; nothing after it starts.");

        return true;
    }

    /// <summary>
    /// The recap show has just been on air for the race on the board and
    /// the next race opens through On Air (17 Oct 2026) - so the gap from
    /// here to On Air's jingle is On Air's, and the announcer screen's
    /// director should not put its own hand-over or "coming up" into it.
    /// The room had the recap's way out, then "still to come", then the
    /// opening saying the same thing again: three hand-overs for one race.
    ///
    /// As narrow as it can be: only while the race the recap was about is
    /// still the one on the board and finished. A reset, a Stage or a new
    /// green ends it, and so does unticking Start races On Air.
    /// </summary>
    internal bool OnAirFollowsTheRecap =>
        StartWithOnAir
        && _race.State == RaceState.Finished
        && _race.SessionGeneration == Volatile.Read(ref _recapAiredGeneration);

    /// <summary>
    /// Who won, for the room's vote: position one and an actual finish - a
    /// DNF or a DQ placed first is not a win, the same rule the records
    /// use. Null when nobody won, and always null for a demo.
    /// </summary>
    /// <remarks>
    /// Read off the OUTCOME, not the Status. Every saved row's Status is
    /// "active" (RaceFiling.ResultsFrom), so a test for an empty Status
    /// found no winner in any race and no vote was ever settled.
    ///
    /// NOT A DEMO. A demo keeps the Round box's round, so a demo run with
    /// the voted-on round picked would close the vote and settle it with
    /// an invented winner - and the real race after it would then be
    /// turned away as a second call.
    /// </remarks>
    internal static SessionResultEntry? RoomVoteWinner(SessionRecord record) =>
        record.IsDemoSession()
            ? null
            : record.Results.FirstOrDefault(entry =>
                entry.Position == 1 && string.IsNullOrWhiteSpace(entry.Outcome) && entry.Laps > 0);

    /// <summary>
    /// Files the race's winner against the room's prediction.
    ///
    /// HERE, by the code that has just saved the result, rather than asked
    /// for later by the announcer: the segments are picked on a tick
    /// minutes after the flag, and asking "who won the last race" at that
    /// moment means asking whichever part of the app happens to still be
    /// holding it. See RoomVote.Finished.
    ///
    /// Keyed on the ROUND'S ID, which is why a club running two classes
    /// cannot have its Stock prediction read out over the Mod A-main.
    ///
    /// A DEMO RACE SETTLES NOTHING, and that was the other half of this
    /// bug. The demo's own record carries no round - SessionRecord.RoundId
    /// is deliberately null for one - but this call passed <c>round?.Id</c>
    /// rather than the record's, so a demo run while a vote was open on
    /// that round matched it and settled a real vote with an invented
    /// winner. The room would then have heard its verdict read out about a
    /// race that never happened.
    /// </summary>
    private void SettleTheRoomsVote(SessionRecord record, RoundDefinition? round)
    {
        // IsDemoSession rather than IsDemo, so a demo saved before the flag
        // was recorded (only its "[DEMO]" label) settles nothing either.
        if (record.IsDemoSession())
        {
            return;
        }

        if (TonightsVote?.Invoke() is not { } roomVote)
        {
            return;
        }

        try
        {
            // Position one and an actual finish - see RoomVoteWinner, which
            // reads the Outcome: every saved row's Status is "active", so a
            // test on an empty Status found no winner in any race.
            if (RoomVoteWinner(record) is { } won)
            {
                roomVote.Finished(round?.Id, won.Transponder, won.DriverName);
            }
        }
        catch (Exception ex)
        {
            // A vote is the one thing on a race night that is purely for
            // fun. It must never be the reason a result is not filed.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] The room's vote goes unsettled - the result could not be filed against it: {ex.Message}");
        }
    }

    /// <summary>
    /// Speaks an LLM-written wrap-up line for a finished race.
    ///
    /// Deliberately fire-and-forget and deliberately AFTER the save: the
    /// results are already safely in history before this runs, so a slow or
    /// broken model can't delay or endanger them. The announcer's own
    /// "race completed" call has already gone out too, meaning this is
    /// strictly additive - if it never arrives, nothing is missing.
    ///
    /// Since 27 Sep 2026 the recap can be a small broadcast of its own - a
    /// TV-style opening and sign-off, and after a main a word with the
    /// winner's mechanic in a second voice - all set on the Commentary page.
    /// See PlanRecap for what is asked for, and the steps below for when
    /// each part is written and spoken.
    /// </summary>
    /// <param name="round">The round the race was run for - kept for a demo too, which isn't linked to it - or null for a freeform race.</param>
    private void SpeakRaceSummary(SessionRecord record, RoundDefinition? round)
    {
        // THE ROOM'S OWN CALL FIRST, before any of the decisions below can
        // return (12 Oct 2026, moved and guarded 13 Oct).
        //
        // IT WAS HALFWAY DOWN THIS METHOD, past the return for a club with
        // the teaser off and the AI announcer off. Those clubs voted and
        // the verdict was never filed, so the room never heard who it had
        // picked and nothing anywhere said why - the vote simply stayed
        // open until it timed out. Settling it needs no model, no teaser
        // and no honours; it needs the result, which this method is handed.
        // So it goes where nothing can skip it.
        SettleTheRoomsVote(record, round);

        // The race the coach was picked for is over (22 Oct 2026) - see
        // EndSplitCoaching.
        EndSplitCoaching("the race is over");

        // The gate the next race's write-aheads are waiting at (20 Oct
        // 2026), taken here on the UI thread - opened on every way out of
        // this method, and by the job once the flag lines have been asked
        // for. See _flagLinesAsked.
        var flagLinesAsked = _flagLinesAsked;

        // The teaser is fixed text and needs nothing from the LLM, so it is
        // NOT behind the commentary guard. It used to be, which meant an
        // unreachable Ollama silently killed the teaser too - the actual
        // reason it was never heard.
        // One of the configured teasers, picked at random - the same line
        // after every race is what makes an announcer sound canned.
        var teaserLines = _settings.EffectiveTeaserLines;

        var commentaryAvailable = _commentary is not null
                                  && _commentary.IsAvailable
                                  && _settings.CommentaryRaceSummaries;

        // ONLY WITH A RECAP TO FOLLOW IT (17 Oct 2026). The teaser is the
        // line that says the recap is coming, so with recaps switched off -
        // or nothing able to write one - the room heard "stand by for the
        // recap" and then nothing. Still not behind the model's REACHABILITY
        // on the night, which is what the note above is about: a recap that
        // was asked for and then failed is the model's fault, and the
        // teaser has already gone by then.
        var wantsTeaser = _settings.SummaryTeaserEnabled && teaserLines.Count > 0 && commentaryAvailable;

        // Why there will be no recap, in words, for the console line below.
        var noRecapBecause = !_settings.CommentaryRaceSummaries
            ? "race recaps are switched off on the AI Commentary tab"
            : "no commentary writer is available (the AI announcer is switched off, or neither the relay nor a local model is set up)";

        // The honours, built by AwardAchievements a moment ago on this same
        // thread - TAKEN, so nothing of this race's badges can be read out
        // after the next one. Null when the race unlocked nothing, when the
        // segment is switched off, or when the badges went out as individual
        // calls instead. See _honours and PlanHonours.
        var honours = _honours;
        _honours = null;

        // The honours ALONE are reason enough to run the sequence, which is
        // the one addition to this condition. They need no model and no
        // teaser - the words were written before any of this - and this
        // return is the one that would otherwise lose them: the badges were
        // held back from the individual calls on the strength of the segment
        // running, so a club with the AI off and the teaser switched off
        // would have heard nothing about them at all.
        // The unofficial result at the flag (18 Oct 2026) needs no model
        // either, so it too is reason enough to run the sequence.
        // Not under "Lap times only" (22 Oct 2026, bug hunt): that mode is
        // the lap times and the calls a race cannot do without, nothing more.
        var resultRead = _settings.FlagResultRead && !LapTimesOnly.IsOn(_settings) ? FlagResultFor(record) : null;

        // THE SECTORS AT THE FLAG (22 Oct 2026): who was quickest through
        // each sector, said in the same breath as the result - so it is
        // said once, in order, and goes wherever the result goes. Its own
        // switch (Sectors at the flag, off until a club ticks it), so it
        // speaks with the result read off too. Not under "Lap times only",
        // for the same reason the result is not. Not handed to the AI
        // recap: the room has just heard it, and the recap is about the
        // race rather than its table.
        if (!LapTimesOnly.IsOn(_settings) && _announcer.WillCall(AnnouncementEventType.SectorsRecap))
        {
            // Guarded like the save-path work below: this runs before the
            // heat is linked to its round and the journal is cleared.
            try
            {
                if (SectorsAtTheFlag(record) is { } sectorsLine)
                {
                    resultRead = resultRead is null ? sectorsLine : $"{resultRead} {sectorsLine}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [split] The sectors at the flag could not be worked out: {ex.Message}");
            }
        }

        if (!wantsTeaser && !commentaryAvailable && honours is null && resultRead is null)
        {
            // Nothing more to say - but the broadcast is only over once the
            // finish call has been spoken. Returning without the signal left
            // music set to fade or stop "after the recap" playing for the
            // rest of the night whenever both teaser and recap were off.
            SignalSessionAudioFinishedAfterFinishCall();

            // No flag lines to wait for (20 Oct 2026) - see _flagLinesAsked.
            flagLinesAsked.TrySetResult();
            return;
        }

        // Worked out HERE, on the UI thread, rather than inside the
        // background job below. It reads the history, the programme and the
        // event stores, and the caller writes to the programme the moment
        // this method returns - which it does immediately, since everything
        // after this line is queued work.
        // Decoration for the recap, and worked out over every stored season -
        // so guarded. This runs on the save path BEFORE the heat is linked to
        // its round and the crash journal is cleared; a fault here used to
        // skip both, leaving a saved race that looked unrun and was offered
        // back for "recovery" (and a second save) at the next launch.
        ChampionshipSwing? championship = null;

        if (commentaryAvailable)
        {
            try
            {
                championship = Championship.SwingFor(record, _seasons, _rounds, _events, _sessionHistory, _profiles);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Recap goes without the standings - they could not be worked out: {ex.Message}");
            }
        }

        if (championship is not null)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Recap has the {championship.SeasonName} standings: "
                + string.Join(", ", championship.Moves.Select(m => $"P{m.Position} {m.DriverName}")));
        }

        // The drivers' own records at this club, as they stood BEFORE this
        // race (10 Oct 2026) - the recap's half of the depth, and the one
        // thing the longest segment of the night has never had. Here, on
        // the UI thread, beside the standings and for the same reason: it
        // walks the session history, the programme and the profiles.
        //
        // WITH THIS RACE LEFT OUT, WHICH IS THE WHOLE CARE OF IT. This
        // runs from RaceFiling's afterSaved callback, so the result is
        // already in the history; counted in, a driver's fourth win here
        // would be announced as their fifth and a first win as no first at
        // all. See CommentaryReaders.GridHistory's remarks.
        //
        // Guarded like the standings above: a fault here used to skip the
        // round being linked and the crash journal being cleared.
        RaceForm? raceHistory = null;

        if (commentaryAvailable && _rounds is not null)
        {
            try
            {
                var (grid, rivalries) = CommentaryReaders.GridHistory(
                    record.Results.Select(r => r.Transponder).ToList(),
                    round?.ClassName,
                    _profiles,
                    _sessionHistory,
                    _rounds,
                    exceptSessionId: record.Id);

                // The race day goes with it, so the one fact in the
                // recap's history that is true for the whole evening - the
                // winner's career before tonight - is spent once a night
                // rather than once a race. See RaceForm.NightKey.
                raceHistory = new RaceForm(grid, rivalries, round?.ClassName, _events?.Active?.Id);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Recap goes without the drivers' records - they could not be worked out: {ex.Message}");
            }
        }

        if (raceHistory is { Grid.Count: > 0 })
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Recap has the club's history for {Plural.Of(raceHistory.Grid.Count, "finisher")}.");
        }

        // And what the club's garage knows about the cars they raced (11
        // Oct 2026) - the one thing the main voice has never mentioned.
        // Here on the UI thread with the two above, and guarded the same
        // way: the host reads the Garage, the session history, the
        // programme and the profiles for this, and this is the save path.
        //
        // The race day goes with it, so a car line - "that car is the
        // club's own", "his first Open race here" - is said once an
        // evening rather than once in the heat's recap and again in the
        // main's. See RaceCars.NightKey.
        RaceCars? raceCars = null;

        if (commentaryAvailable && RecapCarFacts is not null)
        {
            try
            {
                var found = RecapCarFacts.Invoke(record, round);

                // Nothing found is null rather than an empty RaceCars, so
                // the recap's prompt leaves the whole section out instead
                // of carrying a heading over nothing - which is a fact the
                // model would be tempted to fill in.
                raceCars = found.Count > 0 ? new RaceCars(found, _events?.Active?.Id) : null;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Recap goes without the cars - they could not be worked out: {ex.Message}");
            }
        }

        if (raceCars is { Cars.Count: > 0 })
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Recap has the garage's own record of {Plural.Of(raceCars.Cars.Count, "car")}.");
        }

        // AND WHO HAS BEEN WINNING THE MAINS ON THIS LAYOUT (11 Oct 2026) -
        // the one claim in the broadcast no human announcer could make.
        // Nobody standing at a club track holds "who won the main here at
        // each of the last fourteen meetings" in their head, across three
        // classes and four layouts and two years of Thursdays.
        //
        // ONLY FOR A MAIN. A heat cannot end a run of mains, and reading
        // the mains' history out after one would be the announcer talking
        // about a race that has not happened yet.
        //
        // And only for the TOP main (untiered counts as the top, the same
        // reading MainsHere and RaceStature use). The run is the A-main's,
        // so a B-main winner neither ends it nor extends it.
        //
        // Here on the UI thread with the two above, and guarded the same
        // way: this walks the session history, the programme and the
        // profiles, and this is the save path.
        MainsOnLayout? raceMains = null;

        if (commentaryAvailable && round is { Kind: RoundKind.Main, MainTier: <= 0 })
        {
            try
            {
                // The layout the race was actually RUN on, which is what
                // the session recorded; the round's own name is the
                // fallback for an old file that never carried one.
                var ranOn = string.IsNullOrWhiteSpace(record.TrackLayoutName)
                    ? round?.TrackLayoutName
                    : record.TrackLayoutName;

                raceMains = CommentaryReaders.MainsHere(
                    _sessionHistory,
                    _rounds,
                    _profiles,
                    ranOn,
                    round?.ClassName);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Recap goes without this layout's run of mains - it could not be worked out: {ex.Message}");
            }
        }

        // SAID EITHER WAY, which is the lesson the colour segment cost
        // three rounds of testing to learn: "the reader found nothing" and
        // "the reader never ran" look identical in a race-night log unless
        // one of them says so.
        if (raceMains is { } mainsHere)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] On this layout, {mainsHere.HolderName} has won the "
                + $"{mainsHere.ClassName} main at the last {mainsHere.HolderMeetings} of "
                + $"{Plural.Of(mainsHere.Meetings, "meeting")} filed on it. The recap says whether tonight ended that.");
        }
        else if (commentaryAvailable && round is { Kind: RoundKind.Main, MainTier: <= 0 })
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] No run of mains to call on this layout - either nobody has "
                + "won two in a row here, or the club has not filed enough meetings on it yet.");
        }

        // Heat or main, the opening and sign-off, the mechanic - from the
        // settings, the program and the Garage, so worked out here on the
        // UI thread for the same reason as the standings, and guarded the
        // same way: this is the save path.
        RecapPlan? plan = null;

        if (commentaryAvailable)
        {
            try
            {
                // The honours are taken above, so whether they are coming is
                // already known here - and the sign-off's placement depends
                // on it. See PlanRecap.
                plan = PlanRecap(record, round, honours is not null);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Recap goes out plain, without its opening, sign-off or interview - they could not be set up: {ex.Message}");
            }
        }

        // WHY (20 Oct 2026): the booth Team Principal's night - the pass
        // over the history, the rounds, the race day and the profiles - is
        // read HERE, on the UI thread that owns those stores. It used to be
        // read inside WritePrincipalAsync, which runs on the thread pool
        // once the recap is written, so an Edit Result or a session saved in
        // that minute could change the history under it ("Collection was
        // modified") and the principal lost his night or was briefed off a
        // torn read. Guarded like everything else here: this is the save path.
        var boothBrief = BoothBrief.Nothing;

        if (plan is { Principal: true })
        {
            var boothWinner = record.Results.OrderBy(r => r.Position).FirstOrDefault()?.Transponder;
            boothBrief = BoothBrief.Nothing with { WinnerTransponder = boothWinner };

            try
            {
                var className = string.IsNullOrEmpty(record.RoundId) ? null : _rounds?.Get(record.RoundId)?.ClassName;
                var (nightFacts, stance) = PrincipalNightBrief(
                    boothWinner, className, PrincipalTablePosition(boothWinner, championship?.Moves));

                boothBrief = new BoothBrief(
                    nightFacts, stance, boothWinner, _profiles.Get(boothWinner ?? string.Empty)?.SpokenName);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Could not read the Team Principal's night: {ex.Message}");
            }
        }

        // Cancelled the moment the next race is called to the grid - staged,
        // counting down or started. What follows the recap stops there,
        // whether it is being written, rendered or waiting to be spoken: it
        // queues as reminders, which staging doesn't clear, and a mechanic
        // talking over the next start is worse than no mechanic. Told by the
        // race's own event rather than read from its state in the
        // background, which is the UI thread's to read.
        //
        // Not disposed: it has no timer, and the handler below may still be
        // running on the UI thread as the sequence ends.
        var nextRace = new CancellationTokenSource();

        // Skip and Hold stop it too - see _recapShow. Made here, before the
        // handler below, so the handler can mark it (22 Oct 2026); published
        // to _recapShow just after, as before.
        var recapShow = new RecapShowRun(nextRace);

        Action<RaceState> onNextRace = state =>
        {
            if (state is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
            {
                // WHY (22 Oct 2026): recorded, so the sequence's finally
                // knows not to read this race's held badges into the next
                // one - even when Skip or Hold had stopped the show first.
                recapShow.NextRaceCalled = true;

                // Async: Cancel() would run whatever is waiting on the token
                // right here, on the thread that is changing the race.
                _ = nextRace.CancelAsync();
            }
        };

        _race.StateChanged += onNextRace;

        // Already called? Then nothing follows the recap at all.
        if (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            recapShow.NextRaceCalled = true;
            nextRace.Cancel();
        }

        // Taken over from any earlier sequence still finishing, which only
        // clears its own.
        Volatile.Write(ref _recapShow, recapShow);

        // The race this recap is about, for OnAirFollowsTheRecap. Read here
        // on the UI thread, before anything can stage the next one.
        var recapGeneration = _race.SessionGeneration;

        // WHY (20 Oct 2026): this sequence's own name on the broadcast
        // floor. Every sequence used to take and release the floor as the
        // same TheProgramme, so an earlier one coming out of an uncancelled
        // wait after the next race's recap had started released THAT
        // sequence's claim, and the announcer screen's director opened a
        // segment over it. Numbered, a late Release matches nothing.
        var floorOwner = $"{TheProgramme} #{Interlocked.Increment(ref _recapSequences)}";

        // HOLD ON AT THE FLAG (17 Oct 2026): the race director has the mic,
        // and Hold is the button that says nothing new starts - the recap
        // show included. Stopped before its first part, the same way a Hold
        // pressed halfway through stops it. See RaceControlControl.Layout's
        // HoldChanged.
        if (Commentary?.Invoke()?.Held == true)
        {
            recapShow.StoppedBy = "the segments were held from Race Control";
            nextRace.Cancel();

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Recap show not started - the segments are held. Release them on the announcer strip.");
        }

        // The recording this recap belongs to, taken now on the UI thread.
        // Several waits in the sequence below are not cancelled by the next
        // race being called, so the end of the recap can land after the next
        // green - and must not stop THAT race's recording seconds in, the same
        // guard SignalSessionAudioFinishedAfterFinishCall already has.
        var recapRecording = _raceAudioRecorder;

        // WHY (22 Oct 2026): the race's drama for the show's opening is
        // worked out HERE, on the UI thread, like boothBrief above. It used
        // to be read inside the sequence below, on the thread pool, where a
        // layout added in the Track Layouts editor at that moment could
        // change the store under it ("Collection was modified") and the
        // written recap was lost. ShowDrama is a value, so the sequence gets
        // its own copy.
        var recapDrama = ShowDrama.None;

        try
        {
            recapDrama = ShowDrama.Of(record, _trackLayouts.All.FirstOrDefault(t => !string.IsNullOrEmpty(record.TrackLayoutName) && t.Name == record.TrackLayoutName));
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Could not read the race's drama: {ex.Message}");
        }

        _ = Task.Run(async () =>
        {
            // The writing and rendering of the recap, the interview and the
            // sign-off: stopped with the next race, and also once the
            // sequence has given up on them, so nothing goes on costing
            // characters or holding the local model after it is over.
            using var followOnWork = CancellationTokenSource.CreateLinkedTokenSource(nextRace.Token);

            // What called this sequence off, for its console lines - the
            // next race unless Skip or Hold did (17 Oct 2026).
            string CalledOff() => recapShow.StoppedBy ?? "the next race was called to the grid";

            // Whether this sequence raised the recap's deep duck and has yet
            // to release it: 0 never raised, 1 raised, 2 released. Released
            // only if it was raised, and only once - so a sequence ending
            // late never clears a deep duck the NEXT race's recap has just
            // raised.
            var deepDuck = 0;

            void ReleaseDeepDuck()
            {
                if (Interlocked.CompareExchange(ref deepDuck, 2, 1) == 1)
                {
                    DeepDuckRequested?.Invoke(false);
                }
            }

            // The show's music, if this club scores its recaps - see
            // ShowTreatment. Null when they don't, when there are no files
            // in, or when the audio device is gone, and then everything
            // below runs exactly as it did before there was any of this.
            //
            // Held like the deep duck above and ended the same way: once,
            // however the sequence ends. Until it is ended the club's
            // music stays silenced, so a path out that skipped it would
            // leave the room quiet for the rest of the night.
            ShowRun? show = null;

            // The show while it is still OPENING - the opener landing, a
            // couple of seconds. Held here rather than as a local down in
            // the sequence because there are ways out between starting it
            // and taking hold of it (the model returning nothing, the next
            // race being called), and a show abandoned half-open has
            // already silenced the club's music with nothing left holding
            // the handle that gives it back.
            Task<ShowRun?>? opening = null;

            // THE AFTER-THE-FLAG BED (18 Oct 2026) - under the race
            // announcer from the finish call to the bumper into the stop
            // set. Held here so every way out of the sequence takes it away
            // (see the finally), and the next race being called does too.
            ImagingBed? flagBed = null;
            using var flagBedOff = nextRace.Token.Register(() => flagBed?.Finish());

            // crossfade: a bumper is being fired over the end of the show,
            // so the bed crossfades out under it rather than lifting into an
            // ending of its own (16 Oct 2026).
            async Task EndShowAsync(bool crossfade = false)
            {
                // Plainly, not interlocked: the natural end and the finally
                // run one after the other on this one sequence, never at
                // once. FinishAsync is idempotent as well, so even a double
                // call would only end the show once.
                var starting = opening;
                opening = null;

                if (starting is not null)
                {
                    try
                    {
                        // Whatever it opened is ours to close, even if
                        // nobody was left to want it.
                        show ??= await starting;
                    }
                    catch
                    {
                        // StartAsync gives the music back itself when it
                        // fails before handing anything over.
                    }
                }

                var run = show;
                show = null;

                if (run is not null)
                {
                    try
                    {
                        await run.FinishAsync(cutShort: crossfade);
                    }
                    catch
                    {
                        // FinishAsync gives the music back in its own
                        // finally; nothing here is worth failing a recap for.
                    }
                }
            }

            // THE BROADCAST FLOOR, CLAIMED FOR THE WHOLE SEQUENCE (11 Oct
            // 2026). This is the programme - the race has just finished and
            // the room is waiting to be told about it - and from here until
            // the finally it is the only thing allowed to open a show.
            //
            // Without this the announcer screen's director, which asks for a
            // segment twice a second, would open a second show over the top:
            // on 11 Oct it opened the night's SIGN-OFF nine seconds into the
            // recap's own bed, so the hall heard two openers and two beds
            // summed together and the programme signed off before the
            // honours had been read out. See ShowFloor for the whole story.
            //
            // Not conditional, and not failed on: a sequence that could not
            // claim it is still the programme and still goes out. The claim
            // is what makes the FILLER stand down.
            //
            // 17 Oct 2026, Bug Hunt 8, A04-3/A04-4: TAKEN, not asked for. A
            // Play again can hold the floor at the flag, and its Release
            // then freed it mid-recap; taken over, that Release does
            // nothing. And with a ceiling of its own, since a long main's
            // programme can pass ShowFloor's six minutes.
            ShowFloor.TakeOver(floorOwner, TheProgrammeLongest);

            // WHY (20 Oct 2026): declared out here rather than where it is
            // started, so the finally can give back colour segments that were
            // written but never reached - see the finally.
            Task<List<SpokenSegment>?>? colourTask = null;

            // WHY (20 Oct 2026): the principal's words, once the model has
            // written them. Kept in the diary only after every line has been
            // queued to air (see the booth interview below) - they used to be
            // kept the moment they were written, so a dropped recap or a
            // failed render left the host quoting back a sentence the room
            // never heard.
            IReadOnlyList<CommentaryService.InterviewExchange>? principalWords = null;

            try
            {
                var sequenceStarted = DateTime.UtcNow;

                // Punchier delivery for the recap's own lines. The words were
                // already broadcast-shaped; the flatness came from the
                // voice settings, where style exaggeration sits at 0 by
                // default and stability at a middling 0.5.
                //
                // Applied as-is, not merged with the profile. Merging took
                // the more EXPRESSIVE of the two, which pulled a cranked
                // announcer profile straight back in - and an
                // over-expressive recap is the cheesiness this setting now
                // exists to avoid.
                //
                // Worked out up here, before anything is written: the
                // interview's questions and the sign-off are rendered in it
                // too, while the recap is still being written or spoken.
                (double, double)? delivery = _settings.SummaryUseBroadcastVoice
                    ? (_settings.SummaryVoiceStability, _settings.SummaryVoiceStyle)
                    : null;

                // The model starts work at the flag, not after the teaser.
                // Everything below - the finish call, the pause, the teaser -
                // is time the recap can be generating in, and it used to sit
                // idle through all of it.
                //
                // On followOnWork, like the interview and the sign-off. It
                // used to run on no token at all, so a recap the sequence had
                // dropped for the next race went on generating in the local
                // model for up to a minute - and the next heat's round intro,
                // queued behind it, ran out of time and was logged as the
                // model being slow. Nothing cancels followOnWork until the
                // recap has been awaited, except the next race or the
                // sequence giving up.
                // Which of the club's own history facts the recap's prompt
                // ended up carrying, so they can be marked as spent once
                // the recap has actually been SPOKEN.
                //
                // Held here and marked far below rather than inside the
                // service, and that is the whole care of it: FactPack's
                // discipline is "written AND spoken", a recap gets written
                // and then dropped often enough to have its own method for
                // it (ForgetRaceSummary, just below), and marking on
                // written would burn the club's best fact of the night on a
                // request nobody heard. Every way out between here and the
                // queueing leaves this unmarked, which is correct.
                PackedFacts? recapHistory = null;

                // The broadcast figures (18 Oct 2026): the lead changes the
                // voice and the pylon counted, and the result's sponsor -
                // counted for the report only once the recap is queued,
                // beside HistorySpoken below, for the same reason.
                var recapLeads = (_announcer.Leads.Changes, _announcer.Leads.Leaders);
                var recapSponsor = commentaryAvailable ? _announcer.TakeSponsor(SponsoredMoment.RaceResult) : null;

                // THE TECHNICAL LEAD, AT THE FLAG (18 Oct 2026). Moved out of
                // the recap show: the race announcer throws down to pit lane
                // straight after the finish call, the co-host has a word with
                // the winner's Technical Lead and hands back, and only then
                // does the teaser take the room into the break. Asked for
                // FIRST, before the recap - it is wanted in seconds, and the
                // recap has the whole break to be written in.
                var interviewTask = plan is { Interview: true }
                    ? WriteFlagInterviewAsync(record, plan, followOnWork.Token)
                    : null;

                // WHY (26 Oct 2026, asked for by Asher: "When the show host
                // throws it to other cast member it sounds robotic. reference
                // the show host name when sending it back."): the throw down
                // to pit lane and the co-host's hand back, written by the AI -
                // asked for second, straight behind the interview, so it is
                // written and voiced while the interview is being rendered
                // and the result read plays. The built-in pair below is
                // still rendered and goes out if this is not in hand by then.
                // Only with a co-host to throw to: with none the announcer
                // asks the questions himself and there is nobody to hand back.
                var flagHandOff = interviewTask is not null && CoHostInterviews
                    ? StartHandOff(
                        FlagHandOffBrief(record, resultRead), null, null, _settings.CoHostVoice, null, followOnWork.Token)
                    : null;

                // THE THROW INTO THE STOP SET, WRITTEN (19 Oct 2026): the
                // teaser that takes the room into the break plugs the show
                // and says the recap, with its host, is up after the break -
                // written by the model and different every race. Asked for
                // here, after the interview and before the recap, for the
                // reason the interview is asked for first: it is wanted in
                // seconds. With no stop set tonight it is the same line with
                // the recap "coming up next" instead (see StopSetThrow).
                // Its own token, so a line that is not in by the time it is
                // wanted can be given up on and stop holding up the recap on
                // the model on this PC.
                var throwWriting = CancellationTokenSource.CreateLinkedTokenSource(followOnWork.Token);
                var stopSetNames = StopSetNames();

                // IS THERE A BREAK? Decided ONCE, here, for everything after
                // the flag (22 Oct 2026, known issues): the throw's words,
                // the bumper, the teaser's tail and the break itself. It was
                // asked afresh at each of the four, so a Big Screen opened or
                // closed during the finish call could have the throw promise
                // "back after the break" and then no break run - or the
                // bumper play here AND again as the break opened.
                var breakFollows = _settings.RecapAdBreak && CommercialBreak.SponsorScreenUp;
                var throwTask = wantsTeaser
                    ? _commentary!.BuildStopSetThrowAsync(
                        record, stopSetNames, StopSetThrow.Recent, teaserLines,
                        breakFollows: breakFollows, ct: throwWriting.Token)
                    : null;

                var generation = commentaryAvailable
                    ? _commentary!.BuildRaceSummaryAsync(
                        record, championship, plan?.Context, raceHistory, followOnWork.Token,
                        historyPacked: packed => recapHistory = packed,
                        cars: raceCars,
                        mains: raceMains,
                        leadChanges: recapLeads,
                        presentedBy: recapSponsor?.Name)
                    : null;

                // A recap that was written but never spoken is not "the last
                // race summary". It used to be kept as one regardless, and
                // during the next heat the Announcer page offered to repeat
                // over the PA a recap the club had never heard. Waits for the
                // writing to finish, since it may still be on its way out.
                void ForgetUnheardRecap()
                {
                    generation?.ContinueWith(
                        written => _commentary!.ForgetRaceSummary(written.Result),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion,
                        TaskScheduler.Default);
                }

                if (generation is not null)
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Asking the model for a recap...");
                }

                // WHY (20 Oct 2026): the next race's write-aheads wait until
                // the interview, the throw and the recap are done with the
                // model, so the lines wanted in seconds are asked first.
                // Never awaited here - it only opens the gate. See _flagLinesAsked.
                // The hand-over's MODEL call only (26 Oct 2026, review): its
                // voicing is not the model's business and must not hold the gate.
                _ = OpenWhenAskedAsync(flagLinesAsked, interviewTask, flagHandOff?.Written, throwTask, generation);

                // What follows the recap is written after it, each part as
                // soon as the one before is done: the local model answers one
                // request at a time, and the recap is wanted first. So the
                // interview and the sign-off are written - and rendered -
                // while the teaser and the recap play.
                // THE BOOTH INTERVIEW (18 Oct 2026): the winner's Team
                // Principal, in the booth with the recap show's host later
                // that night - no longer down in pit lane. Written once the
                // recap has been, one request at a time.
                var principalTask = generation is not null && plan is { Principal: true }
                    ? WritePrincipalAsync(
                        generation, generation, record, plan, championship, delivery, boothBrief,
                        written => Volatile.Write(ref principalWords, written), followOnWork.Token)
                    : null;

                // The race announcer's throw to pit lane, and the co-host's
                // hand back - rendered now, so they are ready at the flag.
                var flagThrowTask = interviewTask is not null
                    ? RenderFlagThrowAsync(record, followOnWork.Token)
                    : null;

                var flagHandBackTask = interviewTask is not null && CoHostInterviews
                    ? RenderFlagHandBackAsync(followOnWork.Token)
                    : null;

                Task closingAfter = principalTask is not null ? (Task)principalTask : (Task)generation!;

                var closingTask = generation is not null && plan is { SeparateClosing: true }
                    ? WriteClosingAsync(closingAfter, generation, record, plan, delivery, followOnWork.Token)
                    : null;

                // The honours' three lines, rendered while the teaser and
                // the recap play - three round trips paid for during a wait
                // that is happening anyway, exactly as the teaser's own
                // render is. Started here with the rest rather than when the
                // segment is reached, and it does NOT queue behind the model
                // the way the interview and the sign-off do: nothing in it
                // asks the model at all.
                // THE NIGHT'S COLOUR, WRITTEN INTO THIS PROGRAMME (11 Oct
                // 2026) - the form guide, the grid history, whatever the
                // ladder has due. These used to arrive AFTER the programme
                // had signed off, each opening a show of its own: on 11 Oct
                // the room heard the recap, the honours and "that's a wrap",
                // and then two more mini-broadcasts with their own openers
                // and their own beds. Three shows where there was one
                // programme.
                //
                // Started HERE, with the interview and the sign-off, and for
                // the same reason: the recap takes a minute and a half to
                // speak, so these write during a wait that is happening
                // anyway and are in hand by the time the honours are done.
                // Cancelled with followOnWork like the rest, so a race
                // called to the grid stops them being written at all.
                //
                // IN-RACE AND PRE-RACE SEGMENTS ARE NOT TOUCHED by any of
                // this. The ladder is only asked once the flag is out and
                // the programme is open; a battle call over a running race
                // and a round intro before one are staged exactly where
                // they always were.
                colourTask = commentaryAvailable
                    ? WriteColourAsync(followOnWork.Token)
                    : null;

                var honoursRender = honours is null
                    ? null
                    : RenderHonoursAsync(honours, delivery, followOnWork.Token);

                // WHY (26 Oct 2026, asked for by Asher: "When the show host
                // throws it to other cast member it sounds robotic ... make it
                // AI and prerender."): the throw to the achievements reader
                // and the reader's hand back to the host, written by the AI
                // once the recap has been (so the throw can pick up from how
                // the recap ended) and voiced while the recap plays. Only
                // with a recap to follow and a reader's voice of their own -
                // see PlanHonours.
                if (honours is { Voice: not null } honoursAhead && generation is not null)
                {
                    honoursAhead.HandOff = WriteHonoursHandOff(
                        honoursAhead, generation, (Task?)closingTask ?? principalTask, delivery, followOnWork.Token);
                }

                // The honours with no recap for them to be a segment OF -
                // the AI switched off at the wall, the allowance spent, or
                // the model with nothing to say. They get a show of their
                // own here and only here: on an ordinary night the recap's
                // show is already open under them, and a second bed over
                // the top of the first is two pieces of music at once.
                //
                // A local function rather than a copy at each of the two
                // ways out below, and it sets the sequence's own `show` so
                // that the finally gives the club's music back however this
                // ends - the one thing in here that must not be got wrong.
                // Nothing holds `opening`, because there is no way out
                // between starting it and taking hold of it: it is awaited
                // on the next line, inside a try.
                async Task HonoursOnTheirOwnAsync()
                {
                    // Taken into locals, so what is passed below is plainly
                    // not null rather than narrowed by flow analysis across
                    // a closure - which is a thing the compiler is entitled
                    // to give up on and a reader certainly is.
                    if (honours is not { } segment || honoursRender is not { } rendering)
                    {
                        return;
                    }

                    // 17 Oct 2026, Bug Hunt 8, A04-1: not once the next race
                    // is called - a show opened on a cancelled token still
                    // blipped its opener over the staging call.
                    if (!nextRace.IsCancellationRequested
                        && ShowMusic?.Invoke() is { } onTheirOwn && onTheirOwn.Treats(SegmentKind.BadgesEarned))
                    {
                        try
                        {
                            show = await onTheirOwn.StartAsync(SegmentKind.BadgesEarned, nextRace.Token);
                        }
                        catch
                        {
                            // StartAsync gives the club's music back itself
                            // when it fails before handing anything over.
                            // The honours still go out, dry - which is the
                            // whole point of a segment that needs nothing.
                        }
                    }

                    await SpeakHonoursAsync(segment, rendering, show, nextRace.Token);
                }

                var generationStarted = DateTime.UtcNow;

                // WHY (26 Oct 2026, asked for by Asher: "make sure everything
                // ... is prerendered so there's a quick back and forth with no
                // pausing"): the result read at the flag is voiced NOW, while
                // the finish call is still being said, rather than once it has
                // been heard - it was the one line after the flag still made
                // on the spot, a round trip of silence between the finish
                // call and the result.
                var resultVoiced = resultRead is null
                    ? null
                    : _announcer.PrepareReminderAsync(resultRead, cancel: followOnWork.Token);

                // 1. Let the finish call and the cheering actually FINISH.
                //
                // The configured pause is a pause after the announcer stops
                // talking - that is what it reads as on the Commentary tab.
                // It used to start the instant the race ended, so it ran out
                // while the finish line was still being spoken and the teaser
                // simply queued up behind it. A 4 second setting could
                // comfortably play twenty seconds later without anything
                // being wrong except the thing being measured.
                await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

                // The bed comes up under what the announcer says next - only
                // when he has something to say before the break.
                if ((resultRead is not null || interviewTask is not null || wantsTeaser) && !nextRace.IsCancellationRequested)
                {
                    flagBed = ImagingBed.Start(ImagingCue.FlagBed, _announcer, BroadcastImaging.BedLevel, BroadcastImaging.BedDuck);
                }

                // INTO THE STOP SET (19 Oct 2026): the model's throw - the
                // show, the recap and its host, his name - and the line
                // written in StopSetThrow when it is late or will not do.
                // Asked the same question 4a below asks about whether a
                // break runs, so "after the break" is only said when one
                // does; with none, the recap is "coming up next". The
                // club's teaser lines are the model's feel for it, and
                // with the teaser off nothing is said here at all.
                //
                // MADE READY UNDER THE INTERVIEW, NOT AFTER IT (19 Oct
                // 2026). It used to be settled and turned into speech only
                // once the co-host had handed back, and the room sat
                // through three seconds of the voice service between "back
                // to you" and the throw. It is started here, so its audio
                // is made while the pit lane plays; the model's line is
                // still waited for until the hand back has been heard and
                // the pause after the flag has run, plus a few seconds.
                var teaser = wantsTeaser ? teaserLines[Random.Shared.Next(teaserLines.Count)] : null;
                var flagPause = Math.Clamp(_settings.SummaryDelaySeconds, 0, 120);
                var pitLaneHeard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var teaserRender = teaser is null
                    ? null
                    : RenderTeaserAsync(ThrowToBreakAsync(
                        throwTask, throwWriting, stopSetNames, breakFollows,
                        pitLaneHeard.Task, TimeSpan.FromSeconds(flagPause + ThrowGraceSeconds), nextRace.Token));

                // 1a. AT THE FLAG (18 Oct 2026) - the race announcer, before
                // the teaser takes the room into the break. The unofficial
                // result first: the finish call names nobody.
                if (resultRead is not null && !nextRace.IsCancellationRequested)
                {
                    // Voiced since the flag - see resultVoiced. Not in hand
                    // within HandOffGrace, it is made as it is said, as before.
                    AnnouncementService.PreparedSpeech? resultAudio = null;

                    if (resultVoiced is not null)
                    {
                        (_, resultAudio) = await WithinAsync(resultVoiced, HandOffGrace, nextRace.Token);
                    }

                    var resultQueued = await QueueUnlessNextRaceCalledAsync(
                        () =>
                        {
                            if (resultAudio is not null)
                            {
                                _announcer.SpeakPrepared(resultAudio, onAir: OnAirParts.Result);
                            }
                            else
                            {
                                _announcer.SpeakReminder(resultRead, showOnScreens: true, onAir: OnAirParts.Result);
                            }
                        },
                        nextRace.Token);

                    if (resultQueued)
                    {
                        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] At the flag: \"{resultRead}\"");
                    }
                }

                // 1b. Then down to pit lane for the winner's Technical Lead -
                // the throw, the questions and answers, and the co-host's
                // hand back up to the booth.
                if (interviewTask is not null && !nextRace.IsCancellationRequested)
                {
                    SpokenLine? throwLine = null;
                    SpokenLine? handBack = null;

                    try
                    {
                        throwLine = flagThrowTask is null ? null : await flagThrowTask;
                        handBack = flagHandBackTask is null ? null : await flagHandBackTask;
                    }
                    catch
                    {
                        // The interview still goes out, without them.
                    }

                    // WHY (26 Oct 2026, asked for by Asher): the AI's throw and
                    // hand back go in for the built-in pair above when they are
                    // in hand - resolved inside SpeakInterviewAsync, once its
                    // one wait for the interview is over, so the interview is
                    // never waited for twice and no grace is spent on one that
                    // did not come.
                    await SpeakInterviewAsync(
                        interviewTask, nextRace.Token, throwLine, handBack, "the finish call", OnAirParts.TechnicalLead,
                        anotherFollows: teaser is not null && flagPause == 0,
                        handOff: flagHandOff, handOffWhere: "Pit lane at the flag");

                    await _announcer.WaitForAudioAsync(SpokenWait(), CancellationToken.None);
                }

                // The result read heard out when nothing followed it at the
                // flag (22 Oct 2026, bug hunt): it was queued and not waited
                // for, so the "pause after the flag" ran under it and the
                // teaser followed the result with no gap at all.
                if (resultRead is not null && interviewTask is null && !nextRace.IsCancellationRequested)
                {
                    await _announcer.WaitForAudioAsync(SpokenWait(), CancellationToken.None);
                }

                // The pit lane is done (or there was none): the throw's
                // wait for the model starts counting from here.
                pitLaneHeard.TrySetResult();

                // 2. The teaser's audio has been in the making since before
                // the pit lane (see INTO THE STOP SET above), so the pause
                // is the only thing waited for here.

                // THE BUMPER HITS ON HIS LAST WORD (16 Oct 2026). With no
                // stop set to go into, the bumper below follows the teaser
                // the moment it is heard out - so the teaser ends on the
                // short back-to-back tail rather than the usual 0.9s of
                // silence, which the room heard as dead air waiting for the
                // bumper. The same question the bumper asks below.
                var bumperAfterTeaser = !breakFollows
                    && BroadcastImaging.Has(ImagingCue.AfterRaceOver);
                var teaserTail = bumperAfterTeaser
                    ? AnnouncementService.SpeechTail.BackToBack
                    : AnnouncementService.SpeechTail.Normal;

                if (flagPause > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(flagPause));
                }

                // Written AND rendered as one job, so "the recap is ready"
                // means there is audio in hand, not just text.
                var recapTask = generation is null
                    ? null
                    : BuildRecapAsync(generation, delivery, nextRace.Token);

                // 3. The teaser, straight after the finish call.
                //
                // Deliberately NOT held back until the recap is ready. That
                // was tried: it does make teaser and recap run back to back,
                // but it buys that by moving the silence in FRONT of the
                // teaser, and a teaser arriving long after the checkered
                // flag reads as the app having stalled. Right after the
                // finish call is where it belongs - the wait for the model
                // then sits behind it, which is what a "stand by" line is
                // there to cover in the first place.
                if (teaser is not null)
                {
                    AnnouncementService.PreparedSpeech? prepared = null;

                    if (teaserRender is not null)
                    {
                        (teaser, prepared) = await teaserRender;
                    }

                    // Not once the next race has been called to the grid -
                    // see QueueUnlessNextRaceCalledAsync. A slow render, or
                    // a long pause, could otherwise put "stand by for the
                    // recap" over the next heat's staging call. Nothing of
                    // this race's sequence is wanted after that, so the
                    // recap goes too.
                    var teaserQueued = await QueueUnlessNextRaceCalledAsync(() =>
                    {
                        if (prepared is not null)
                        {
                            _announcer.SpeakPrepared(prepared, teaserTail, onAir: OnAirParts.Teaser);
                        }
                        else
                        {
                            // Voice not configured, muted, or the render failed -
                            // the teaser still speaks, just with the round trip
                            // in front of it.
                            _announcer.SpeakReminder(teaser, showOnScreens: true, tail: teaserTail, onAir: OnAirParts.Teaser);
                        }
                    }, nextRace.Token);

                    if (!teaserQueued)
                    {
                        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Teaser and recap dropped - {CalledOff()}.");
                        ForgetUnheardRecap();
                        return;
                    }

                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] Teaser spoken {(DateTime.UtcNow - sequenceStarted).TotalSeconds:0.0}s after the flag "
                        + $"({flagPause}s pause, {(prepared is null ? "rendered live" : "rendered during the pause")}): \"{teaser}\"");
                }
                else
                {
                    StatusMessage?.Invoke(!_settings.SummaryTeaserEnabled
                        ? $"{AppTime.LogStampNow()}  [commentary] No teaser - it's switched off on the AI Commentary tab."
                        : teaserLines.Count == 0
                            ? $"{AppTime.LogStampNow()}  [commentary] No teaser - every teaser line is empty on the AI Commentary tab."
                            : $"{AppTime.LogStampNow()}  [commentary] No teaser - no recap would follow it ({noRecapBecause}).");
                }

                if (!commentaryAvailable)
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] No recap - {noRecapBecause}.");

                    // WHY (20 Oct 2026): the after-the-flag bed goes once the
                    // result read is heard out. This way out returned before
                    // the line that takes it away, so the honours below
                    // opened their own show and bed on top of it - two
                    // pieces of music at once until the finally.
                    await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                    flagBed?.Finish();

                    // The honours do not need one. This is the evening the
                    // segment is written the way it is for - see
                    // RaceHonours.
                    await HonoursOnTheirOwnAsync();

                    // The end is signalled in the finally below, once the
                    // teaser has actually been spoken.
                    await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                    return;
                }

                // 4. A beat after the teaser has been SPOKEN, not after it
                // was queued. Queuing it only means the audio exists; the
                // words still have to play, and starting the gap at the queue
                // meant the gap ran out during the teaser and the recap
                // followed it with no air at all.
                //
                // RACE OVER -> BUMPER, HITTING THE POST (17 Oct 2026). When
                // there is a bumper into the stop set, it is fired WHILE the
                // teaser is still being said, so its own voice comes in on
                // the teaser's last word - no gap between them - and this
                // returns as it starts. Below, the break waits for it to play
                // out before the first sponsor video; with no break it plays
                // out over the wait for the recap. See TalkUp.
                ImagingPlay? intoTheStopSet = null;

                // NOT ONLY OFF THE TEASER (17 Oct 2026). With the teaser
                // unticked this used to wait for the finish call and play no
                // bumper here at all, and the stop set lost its way in.
                // TalkUp waits for whatever is still being said - the finish
                // call, the badges - and fires the bumper on its last word,
                // which is just as right without a teaser in front of it.
                if (BroadcastImaging.Has(ImagingCue.AfterRaceOver) && !nextRace.IsCancellationRequested)
                {
                    intoTheStopSet = await BroadcastImaging.TalkUpAsync(
                        ImagingCue.AfterRaceOver, _announcer, TimeSpan.FromSeconds(30), nextRace.Token);
                }
                else
                {
                    await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                }

                // The after-the-flag bed goes as the bumper comes in - or,
                // with no bumper, as the teaser's last word is heard.
                flagBed?.Finish();

                // Started, not awaited. Anything still outstanding on the
                // recap runs alongside the gap rather than after it - the
                // gap is the only thing actually waited for.
                var teaserEnded = DateTime.UtcNow;
                var teaserGap = Math.Clamp(_settings.SummaryTeaserGapSeconds, 0, 60);
                var gap = teaser is not null && teaserGap > 0
                    ? Task.Delay(TimeSpan.FromSeconds(teaserGap))
                    : Task.CompletedTask;

                // 4a. THE COMMERCIAL BREAK - the sponsors, over the wait for
                // the model. The club owner's words: "I want the sponsors to
                // play as a commercial break after the race and before the
                // race show recap. This way we can fill the time it takes to
                // generate the content."
                //
                // HERE, and nowhere else. The teaser has been heard - that
                // is the line into the break - and the recap has not been
                // awaited yet, so what this holds is exactly the time the
                // model is taking. See HoldTheCommercialBreakAsync for what
                // ends it, and for the floor and the ceiling.
                //
                // The GAP is started above and awaited below, unchanged. It
                // is the beat between the teaser and the recap, and the
                // break covers the same wait, so the two overlap rather than
                // stacking - a six-second beat does not become six seconds
                // of silence in front of forty seconds of adverts. On a
                // night with no break this method returns at once and the
                // timing of everything here is exactly what it was.
                // RACE OVER -> BUMPER -> STOP SET (16 Oct 2026). The bumper
                // into the break is played by MarkTheBreak as the break
                // opens; on a night with no break there is no stop set to
                // go into, so the bumper plays here, after the teaser, and
                // nothing comes back from a break that never ran. See
                // BroadcastImaging for the run of show.
                // Asked again here as well as at the flag: a Big Screen
                // closed since then means no break, so the bumper plays here
                // rather than nowhere.
                if (!(breakFollows && CommercialBreak.SponsorScreenUp && !nextRace.IsCancellationRequested))
                {
                    if (intoTheStopSet is not null)
                    {
                        // Already playing off the teaser - held to its out,
                        // and left to play out whole, exactly as the bumper
                        // played here before it was talked up.
                        try
                        {
                            await intoTheStopSet.AtPost.WaitAsync(nextRace.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            intoTheStopSet.CrossfadeOut();
                        }
                    }
                    else
                    {
                        await PlayImagingAsync(ImagingCue.AfterRaceOver, nextRace.Token, crossfadeAtPost: false);
                    }
                }
                else
                {
                    // The break plays it out whole before its first video -
                    // see PlayTheWayIntoTheBreakAsync.
                    _intoTheStopSet = intoTheStopSet;
                }

                await HoldTheCommercialBreakAsync(recapTask!, nextRace.Token, breakFollows);

                // Never carried into the next race's break: a break that did
                // not open leaves the bumper playing out on its own, and the
                // handle is no use to anything after this.
                _intoTheStopSet = null;

                // ... -> STOP SET -> JINGLE/SWEEPER -> RECAP OPEN. Started by
                // MarkTheBreak as the break ended, and heard out here so the
                // recap's own open does not land on top of it.
                await BackFromTheBreakAsync();

                var recap = await recapTask!;
                var tookSeconds = (DateTime.UtcNow - generationStarted).TotalSeconds;

                if (recap.Spoken is null)
                {
                    // Silence was the only symptom before: the recap simply
                    // never arrived and nothing said why. Called off by the
                    // next race is said as such, not blamed on the model.
                    StatusMessage?.Invoke(nextRace.IsCancellationRequested
                        ? $"{AppTime.LogStampNow()}  [commentary] Recap called off after {tookSeconds:0.0}s - {CalledOff()} before it was written."
                        : $"{AppTime.LogStampNow()}  [commentary] No recap after {tookSeconds:0.0}s - the model returned nothing. "
                          + "Any [llm] line above says whether it timed out, refused, or wasn't reachable.");

                    // The honours did not need the model either, so a race
                    // the model had nothing to say about still hands out
                    // what it earned.
                    await HonoursOnTheirOwnAsync();

                    // The end is signalled in the finally below, once the
                    // teaser has actually been spoken.
                    await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                    return;
                }

                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Recap written and rendered in {tookSeconds:0.0}s.");

                // 4b. On air - the opener goes HERE: after the recap is
                // written and rendered, in what is left of the gap, and
                // never before the teaser.
                //
                // The teaser is the line before the break ("stand by for
                // the recap"); the recap is the one coming back from it,
                // and it says so - "AND WE'RE BACK LIVE HERE AT THE RC
                // TRACK". A sting in front of the teaser is three lines
                // early and lands on nothing. Coming back from the break
                // it is the return, which is what a sting is for.
                //
                // AFTER THE MODEL, and this is the whole point of the
                // position. It used to start before `await recapTask`, on
                // the reasoning that the opener would play ACROSS the gap
                // rather than being added to it and the wait would be for
                // whichever was longer. That reasoning assumed the recap
                // arrives at about the same time as the gap ends. It does
                // not: rehearsal 5 (10 Oct 2026) had the model take 41.6s
                // and 37.5s to write and render a recap against a 6s gap,
                // so the club heard the opener land, ring out, and then
                // eighteen seconds of bed with nobody talking over it
                // before the recap arrived - twice in twelve minutes. A
                // show is an announcement with music under it, so there is
                // no show to open until there is a line to open for.
                //
                // Nothing else moves. Started rather than awaited, so what
                // is left of the gap and the opener's own run-up still
                // overlap; and it is still after the finish call and the
                // race's badges with nothing special needed, since those
                // are queued ahead of the teaser and the teaser has been
                // heard by the time this runs. An opener landing on top of
                // "Billy Bob has earned Nail-Biter" was an earlier
                // arrangement's own doing.
                //
                // It also means the "no recap" return above opens no show
                // at all, rather than opening one and relying on the
                // finally to take the club's music back off the air.
                // 17 Oct 2026, Bug Hunt 8, A04-1: and none once the next race
                // has been called - see HonoursOnTheirOwnAsync.
                if (!nextRace.IsCancellationRequested && ShowMusic?.Invoke() is { } treatment && treatment.Treats(SegmentKind.RaceRecap))
                {
                    // SegmentKind.RaceRecap, so the show's running order
                    // knows which shape of show this is - the recap gets a
                    // rejoin on a busy night rather than the full opener for
                    // the eleventh time. See ShowRunningOrder.
                    //
                    // 22 Oct 2026: with the race's drama - a photo finish or a
                    // track record set in this race earns the recap a beat
                    // more air (ShowDrama, ShowTreatment.BiggerAir). Read on
                    // the UI thread before the sequence began - see recapDrama.
                    opening = treatment.StartAsync(SegmentKind.RaceRecap, recapDrama, nextRace.Token);
                }

                await gap;

                // The opener has landed and is ringing out; the recap is
                // queued below over its tail, exactly as a cold open's
                // first words are.
                if (opening is not null)
                {
                    var starting = opening;
                    opening = null;

                    show = await starting;
                }

                // Not once the next race has been called to the grid. The
                // model can take a minute or more, and the heats can turn
                // round faster than that: the recap of Heat 3 then went out
                // over Heat 4's countdown or opening laps, its text on the
                // Announcer feed and the Big Screen, and the lap calls under
                // it dropped while it spoke. Staging clears no reminders,
                // and the countdown cut only catches lines dequeued before
                // the countdown began, so this is the one place it can be
                // stopped. Checked and queued in one step on the UI thread -
                // see QueueUnlessNextRaceCalledAsync - so a Stage cannot land
                // between the two.
                var recapQueued = await QueueUnlessNextRaceCalledAsync(() =>
                {
                    // Deep duck for the recap. Raised BEFORE it's queued and
                    // released after the voice queue falls quiet, so it covers
                    // the whole segment rather than flapping per sentence.
                    // In here, so a dropped recap does not duck the next
                    // race's music. Noted first, so the release below can
                    // never miss it.
                    Volatile.Write(ref deepDuck, 1);
                    DeepDuckRequested?.Invoke(true);

                    if (recap.Audio is not null)
                    {
                        // Long tail: this is the longest line of the night and
                        // its last words were being clipped.
                        _announcer.SpeakPrepared(recap.Audio, AnnouncementService.SpeechTail.Long, onAir: OnAirParts.Recap);
                    }
                    else
                    {
                        // Render failed, or voice isn't configured - the recap
                        // still speaks, just with the round trip in front of it.
                        _announcer.SpeakReminder(recap.Spoken, delivery, RecapHostVoice, showOnScreens: true, onAir: OnAirParts.Recap);
                    }
                }, nextRace.Token);

                if (!recapQueued)
                {
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] Recap dropped - {CalledOff()} before it was ready "
                        + $"(it took {tookSeconds:0.0}s to write and render).");
                    ForgetUnheardRecap();
                    return;
                }

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Recap queued {(DateTime.UtcNow - teaserEnded).TotalSeconds:0.0}s after the teaser finished "
                    + $"({teaserGap}s gap set, {(recap.Audio is null ? "rendered live" : "already rendered")}).");

                // The recap show is on air for this race - see
                // OnAirFollowsTheRecap.
                Volatile.Write(ref _recapAiredGeneration, recapGeneration);

                // SPENT HERE, and nowhere earlier. The recap is queued to
                // the announcer, which is the same bar the teaser above is
                // held to - from this line on the room hears it. Marking
                // the facts here is what stops the next recap of the same
                // drivers reading out the same records, and leaving it
                // until here is what stops a dropped recap taking them off
                // the night for nothing. See CommentaryService.HistorySaid.
                _commentary!.HistorySpoken(recapHistory);
                recapSponsor?.Count();

                // 4c. The honours, as a segment OF the show the recap has
                // just opened: the announcer throws to a third voice, that
                // voice goes over who got what, and the announcer takes the
                // programme back. No second bed and no second opener - it is
                // part of this show, which is what makes it a segment rather
                // than an announcement with music behind it.
                //
                // HERE, IN FRONT OF THE INTERVIEW, which is the one ordering
                // decision in this method that is not obvious. The interview
                // and the sign-off are written by the model and may never
                // arrive - SpeakInterviewAsync can answer Late, and then the
                // sign-off is skipped too. The honours were written before
                // the sequence started and always will arrive, and the
                // badges were held back from the individual calls on the
                // strength of this running. Behind a part that can be late
                // they would be lost on exactly the nights the model is
                // slow; in front of it they go out whenever the recap did.
                //
                // It also reads in the right order: the recap talks about
                // the race, the honours hand out what the race earned, and
                // then the pit lane and the sign-off close the programme.
                if (honours is { } honoursSegment && honoursRender is { } honoursLines)
                {
                    await StingBetweenRecapPartsAsync(show, nextRace.Token);
                    await SpeakHonoursAsync(honoursSegment, honoursLines, show, nextRace.Token);
                }

                // 5. The night's colour, INSIDE this show and RIGHT AFTER
                // THE HONOURS (asked for 11 Oct 2026, moved from after the
                // interview). The programme reads result, honours, colour,
                // pit lane, goodnight: the three things about the night
                // together, and then the pit lane and the sign-off close
                // it. Never after the sign-off, which is the whole point of
                // it being in here at all. See WriteColourAsync.
                //
                // In front of the interview, which means a colour segment
                // the model is slow with can hold the pit lane up - bounded
                // at FollowOnWait, after which it is dropped and the
                // interview carries on. The honours take the same risk in
                // front of it for the same reason and it has been fine.
                if (colourTask is not null)
                {
                    await StingBetweenRecapPartsAsync(show, nextRace.Token);
                    await SpeakColourAsync(colourTask, show, nextRace.Token);
                }

                // 6. The booth interview with the winner's Team Principal;
                // then the sign-off, when it has been kept back to come last.
                // Each only once the line before it has been heard, and
                // neither once the next race has been called to the grid.
                //
                // IN THE BOOTH (18 Oct 2026): the recap host has the Team
                // Principal in with them, later that night - no throw to pit
                // lane. The Technical Lead's pit lane word is at the flag now.
                var interview = FollowOn.Spoken;

                if (principalTask is not null)
                {
                    await StingBetweenRecapPartsAsync(show, nextRace.Token);
                    (interview, var principalSaid) = await SpeakInterviewAsync(principalTask, nextRace.Token, onAir: OnAirParts.Booth);

                    // WHY (20 Oct 2026): into the diary only now, once every
                    // line has been queued to air - see principalWords.
                    // Late, stopped, a failed render or a cut-off partway
                    // through keep nothing.
                    if (interview == FollowOn.Spoken && principalSaid
                        && Volatile.Read(ref principalWords) is { } heard && plan is not null)
                    {
                        KeepWhatThePrincipalSaid(record, plan, heard, boothBrief.WinnerSpokenName);
                    }
                }

                if (closingTask is not null)
                {
                    if (interview == FollowOn.Late)
                    {
                        // Written after the interview, so later still -
                        // rather than hold the silence open a second time.
                        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Sign-off skipped - it's written after the interview, which was too late.");
                    }
                    else if (interview != FollowOn.Stopped)
                    {
                        await StingBetweenRecapPartsAsync(show, nextRace.Token);
                        await SpeakClosingAsync(closingTask, nextRace.Token);
                    }
                }

                // Nothing still being written or rendered is wanted now.
                followOnWork.Cancel();

                // ... -> RACE RECAP BED -> BUMPER: the way out of the recap
                // show (16 Oct 2026), and since 17 Oct HITTING THE POST: the
                // bumper is fired while the host is still on his last line,
                // so its own voice comes in on his last word - no gap - and
                // the bed crossfades out underneath it rather than playing an
                // ending of its own. The bumper is the ending. See TalkUp.
                // With no bumper this simply waits for the host to finish,
                // as it always did.
                ImagingPlay? wayOut = null;

                if (BroadcastImaging.Has(ImagingCue.AfterRecap) && !nextRace.IsCancellationRequested)
                {
                    wayOut = await BroadcastImaging.TalkUpAsync(ImagingCue.AfterRecap, _announcer, SpokenWait(), nextRace.Token);
                }

                if (wayOut is not null)
                {
                    // The bed goes under the bumper as it starts...
                    var showEnding = EndShowAsync(crossfade: true);

                    // ...and the music handling waits for the host's last
                    // word, as it always has - see below.
                    try
                    {
                        await _announcer.WaitForAudioAsync(SpokenWait(), nextRace.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // The next race was called while the broadcast finished.
                    }

                    ReleaseDeepDuck();

                    await showEnding;

                    // Heard to its out before the floor is given back below,
                    // so a filler segment does not start its own music on top
                    // of it; the rest of it plays out whole.
                    try
                    {
                        await wayOut.AtPost.WaitAsync(nextRace.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        wayOut.CrossfadeOut();
                    }
                }
                else
                {
                    // Music handled AFTER the recap, not at the flag. Waiting
                    // for the voice queue means the fade starts when the
                    // broadcast actually ends rather than cutting across it -
                    // unless the next race has been called, which the deep
                    // duck must not sit on top of.
                    try
                    {
                        await _announcer.WaitForAudioAsync(SpokenWait(), nextRace.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // The next race was called while the broadcast finished.
                    }

                    ReleaseDeepDuck();

                    // The broadcast is over and the queue is drained, so the
                    // bed hands over to its own fade now - over the quiet
                    // after the sign-off, which is what that fade is for.
                    await EndShowAsync(crossfade: false);
                }
            }
            catch
            {
                // Released here as well: a synthesis failure mid-recap would
                // otherwise leave the bed ducked for the rest of the night.
                ReleaseDeepDuck();

                // Colour commentary is decoration - never let it surface as
                // an error on a page that just finished a race.
            }
            finally
            {
                // The after-the-flag bed, if a way out above skipped taking
                // it away. Safe twice.
                flagBed?.Finish();

                // And the write-aheads' gate, for a way out that never got as
                // far as asking (20 Oct 2026). Safe twice - see _flagLinesAsked.
                flagLinesAsked.TrySetResult();

                // Exactly once, however the sequence above ended - recap
                // spoken, no recap, or a failure part-way. Music set to fade
                // after the recap has nothing else to wait for, so any path
                // that skipped this left it playing for the rest of the
                // night; and raising it in more than one place risked a
                // second fade or stop landing on the next race's music.
                // First, so nothing below can keep it from being raised.
                // Only this race's recording is finished - see recapRecording.
                RaiseSessionAudioFinished(onlyThisRecording: true, recapRecording);

                // And here, for any way out of the sequence that raised the
                // deep duck but skipped the release above - an early return
                // added later, say. Does nothing if it was never raised or
                // has already been released.
                try
                {
                    ReleaseDeepDuck();
                }
                catch
                {
                    // A failing handler must not stop the clean-up below.
                }

                // And the same for the show: any way out of the sequence
                // that opened it and skipped the end above - the recap
                // dropped because the next race was called, a synthesis
                // failure part-way - must still give the club's music
                // back. Does nothing if it was never opened or has already
                // ended.
                await EndShowAsync();

                // AFTER the show has ended, not before. Ending it rides the
                // bed down over the quiet after the sign-off, and that fade
                // is part of the programme: a filler segment started the
                // instant the floor came free would play its own opener
                // across it, which is the same two-pieces-of-music fault
                // the claim was added to stop. See ShowFloor.
                ShowFloor.Release(floorOwner);

                _race.StateChanged -= onNextRace;

                // BADGES KEPT FOR A SEGMENT THAT NEVER CAME (22 Oct 2026, bug
                // hunt): Skip or Hold anywhere after the flag - the pit lane,
                // the teaser, the break - stopped the sequence before the
                // achievements segment, and the badges held back for it were
                // never heard at all. Called one at a time instead, as they
                // would have been with the segment off. Not when the next race
                // stopped it: that race must not hear the last one's badges.
                // WHY (22 Oct 2026): "the next race" is NextRaceCalled, not a
                // null StoppedBy - On Air sets StoppedBy too, and the badges
                // were read over the next race's opening.
                try
                {
                    if (honours is { Started: false, Unlocks: { Count: > 0 } held } && recapShow.StoppedBy is not null && !recapShow.NextRaceCalled)
                    {
                        foreach (var unlock in held.Take(MaxSpokenAchievements))
                        {
                            _announcer.NotifyAchievement(unlock.Transponder, unlock.Badge.Name);
                        }

                        StatusMessage?.Invoke(
                            $"{AppTime.LogStampNow()}  [achievements] The recap was stopped ({recapShow.StoppedBy}) before its achievements segment, so they are called one at a time.");
                    }
                }
                catch
                {
                    // A failing handler must not stop the clean-up below.
                }

                // No longer the recap show Skip and Hold would stop - unless
                // a newer one has already taken its place.
                Interlocked.CompareExchange(ref _recapShow, null, recapShow);

                // WHY (20 Oct 2026): colour segments written but never
                // reached - the teaser or recap dropped, no recap, a failure
                // part-way - are given back to the director as Dropped once
                // they land. They used to sit as awaiting for its 15-minute
                // ceiling, holding their kinds and facts off the next gap.
                // Dropped does nothing for one already Spoken or Dropped by
                // SpeakColourAsync, so this is safe on every way out.
                try
                {
                    if (colourTask is not null && Commentary?.Invoke() is { } colourDirector)
                    {
                        _ = colourTask.ContinueWith(
                            written =>
                            {
                                foreach (var unsaid in written.Result ?? new List<SpokenSegment>())
                                {
                                    colourDirector.Dropped(unsaid);
                                }
                            },
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnRanToCompletion,
                            TaskScheduler.Default);
                    }
                }
                catch
                {
                    // A failing handler must not stop the clean-up below.
                }

                // However the sequence ended, nothing it started carries on
                // writing or rendering. Before the using disposes it.
                try
                {
                    followOnWork.Cancel();
                }
                catch
                {
                    // Whatever was waiting on it has its own handling; the
                    // broadcast is over either way.
                }
            }
        });
    }

    // ==================== The recap as a broadcast ====================

    /// <summary>
    /// What the recap is wrapped in this time. Worked out on the UI thread
    /// by PlanRecap, with everything else the recap reads, before the
    /// sequence goes to the background.
    /// </summary>
    /// <param name="Context">What the recap itself is told: heat or main, the opening, an inline sign-off, the show, what's next.</param>
    /// <param name="Interview">A word with the winner's mechanic follows the recap.</param>
    /// <param name="SeparateClosing">The sign-off is a line of its own, spoken last - after the interview.</param>
    /// <param name="MechanicName">What the announcer calls the mechanic; blank for "the team's mechanic".</param>
    /// <param name="SetupFacts">What the Garage has on the winning car - see MechanicBrief.</param>
    /// <param name="MechanicVoice">The mechanic's voice, or null for the announcer's own.</param>
    /// <param name="Principal">A word with the winner's Team Principal follows - after the Technical Lead when both are on (17 Oct 2026).</param>
    /// <param name="PrincipalName">What the announcer calls the Team Principal; blank and no name is used.</param>
    /// <param name="PrincipalVoice">The Team Principal's voice, or null for the announcer's own.</param>
    private sealed record RecapPlan(
        CommentaryService.RecapContext Context,
        bool Interview,
        bool SeparateClosing,
        string MechanicName,
        IReadOnlyList<string> SetupFacts,
        SecondVoice? MechanicVoice,
        bool Principal = false,
        string PrincipalName = "",
        SecondVoice? PrincipalVoice = null);

    /// <summary>A line the recap sequence will speak: its words, who says them, and its audio when it rendered in advance.</summary>
    /// <param name="Voice">The mechanic's voice, or null for the announcer.</param>
    /// <param name="Delivery">The recap's delivery for the announcer's lines; null for the mechanic, who talks the way the voice does.</param>
    /// <param name="Audio">Rendered while the recap played; null when it couldn't be, and the line is rendered as it is spoken.</param>
    private sealed record SpokenLine(
        string Text,
        SecondVoice? Voice,
        (double Stability, double Style)? Delivery,
        AnnouncementService.PreparedSpeech? Audio);

    /// <summary>
    /// How long, once the recap has been heard, the interview may still be
    /// waited for - and the sign-off once the interview has. Past it the
    /// part is dropped: a silence any longer reads as the broadcast having
    /// ended, and a line that then starts up reads as a mistake.
    /// </summary>
    private static readonly TimeSpan FollowOnWait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a spoken line of the broadcast is waited on, worked out
    /// from what the club asked for rather than fixed.
    ///
    /// IT USED TO BE FIXED, AND THAT OVERRULED THE CLUB (14 Oct 2026). A
    /// flat 60 seconds waited on the broadcast and a flat 90 on each line
    /// of it - set Max length to 300 words and the recap is legitimately
    /// two minutes of speech, so the wait expired part way through, the
    /// block was abandoned, and the music bed and the "coming up" segment
    /// went with it. A club that asks for a long recap has asked to hear
    /// it play.
    ///
    /// A hundred words a minute, deliberately slower than conversation:
    /// the announcer voices are measured, and on v3 the delivery tags add
    /// a beat of their own. Trebled, because the number on the page is what
    /// ONE line may run to and the recap gets its own extra allowance on
    /// top, plus room for an opening and a closing.
    ///
    /// Floored so a terse setting still waits a sensible time, and capped
    /// so a runaway answer cannot hold the floor all evening. Nothing here
    /// is a hard stop anyway: both callers pass the next race's token, so
    /// a race being called ends the wait whatever this says.
    /// </summary>
    private TimeSpan SpokenWait()
    {
        var words = Math.Max(8, _settings.CommentaryMaxWords) * 3;

        return TimeSpan.FromSeconds(Math.Clamp(words * 60.0 / 100.0, 60, 300));
    }

    /// <summary>
    /// What this sequence calls itself while it holds the broadcast floor -
    /// see <see cref="ShowFloor"/>. It reaches the developer console only
    /// when a claim has to be broken, so it reads as a sentence fragment.
    /// </summary>
    private const string TheProgramme = "the race recap sequence";

    /// <summary>
    /// How long the sequence's floor claim is honoured (17 Oct 2026, Bug
    /// Hunt 8, A04-3). Generous: the finally always releases it, so the
    /// ceiling only matters when something dies without its finally.
    /// </summary>
    private static readonly TimeSpan TheProgrammeLongest = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How many recap sequences have started, for each one's own name on
    /// the broadcast floor (20 Oct 2026) - see floorOwner in
    /// SpeakRaceSummary. No initializer: this is a part file.
    /// </summary>
    private int _recapSequences;

    /// <summary>
    /// The booth Team Principal's night, read on the UI thread by
    /// SpeakRaceSummary before the sequence goes to the background (20 Oct
    /// 2026), and handed to WritePrincipalAsync and KeepWhatThePrincipalSaid
    /// so neither reads the stores from the thread pool.
    /// </summary>
    /// <param name="Facts">The night's facts - see PrincipalNightBrief.</param>
    /// <param name="Stance">The position he comes in with.</param>
    /// <param name="WinnerTransponder">The winner's transponder, for the diary.</param>
    /// <param name="WinnerSpokenName">The winner's spoken name, or null.</param>
    private sealed record BoothBrief(
        IReadOnlyList<FactBlock> Facts, PrincipalStance.Stance Stance, string? WinnerTransponder, string? WinnerSpokenName)
    {
        /// <summary>Nothing to say - empty facts, the plain stance, no winner.</summary>
        internal static BoothBrief Nothing => new(Array.Empty<FactBlock>(), PrincipalStance.Plain, null, null);
    }

    /// <summary>
    /// Heat or main, the opening and sign-off, and the mechanic - what the
    /// Commentary page asks for, for this race.
    /// </summary>
    /// <param name="honoursFollow">
    /// Whether the honours segment will be read out after the recap - see
    /// the sign-off's own note below, which is what this is for.
    /// </param>
    private RecapPlan PlanRecap(SessionRecord record, RoundDefinition? round, bool honoursFollow)
    {
        var clubNight = round is not null && _events?.EventForRound(round.Id)?.IsClubNight == true;
        var kind = CommentaryService.DescribeKind(round, clubNight);

        // Mains only, as asked for on 27 Sep 2026: a word with the mechanic
        // is an occasion, and after every heat it would be a routine.
        // Unless the club has asked for it after heats too (21 Oct 2026,
        // Cast > Technical Lead).
        var interview = _settings.RecapMechanicInterview
            && (round?.Kind == RoundKind.Main || _settings.RecapMechanicInterviewHeats);
        var closing = _settings.RecapBroadcastClosing;

        // THE TEAM PRINCIPAL (17 Oct 2026): after the Technical Lead when
        // both are on, and after heats too unless the Cast tab says mains
        // only. See PrincipalAfter.
        var principal = PrincipalAfter(record, round);

        // ANYTHING AT ALL AFTER THE RECAP MAKES THE SIGN-OFF A LINE OF ITS
        // OWN (11 Oct 2026).
        //
        // This used to ask only about the interview, and the honours were
        // invisible to it. So on every heat - and on every main at a club
        // with the interview switched off - the recap was told to sign the
        // programme off itself, "and that's us, next up the A-Main,
        // goodnight", and then the honours were read out afterwards. From
        // the floor on 11 Oct that is exactly what it sounded like: the
        // badges arriving after the show had ended, outside the programme.
        //
        // A sign-off is the last thing or it is not a sign-off.
        //
        // AND IT IS ALWAYS A LINE OF ITS OWN NOW (16 Oct 2026). Left
        // inline at the end of the recap, it was the first thing lost when
        // the model's answer ran long and was cut back to its last whole
        // sentence - which is how the 16 Oct recap ended on "first ever
        // win here" with no goodbye at all. Written apart from the recap,
        // in the recap host's voice, with a fixed line of its own when the
        // model gives nothing back (see SignOffFallback), the programme
        // ends on a sign-off every time.
        var follows = true;

        var context = new CommentaryService.RecapContext(
            kind,
            _settings.RecapBroadcastOpening,

            // Inline only when the recap really is the end of it. Otherwise
            // the sign-off is written separately and spoken last - see
            // `follows` above.
            closing && !follows,
            (_settings.RecapShowName ?? string.Empty).Trim(),

            // A demo leaves its round unrun, so that round is still next.
            closing ? UpNextAfter(record.IsDemo ? null : round) : null,
            InterviewFollows: principal,

            // The host's own name, for the opening to introduce himself
            // with: the recap host's own name, never the race announcer's
            // - see AppSettings.HostNameForRecapShow.
            AnnouncerName: _settings.HostNameForRecapShow,

            // Anything following the recap stops it signing off - the
            // honours as much as the mechanic. See RecapContext.MoreFollows.
            MoreFollows: follows);

        if (!interview)
        {
            // No mechanic, but the honours may still be coming - and when
            // they are, the sign-off is held back to follow them rather
            // than being baked into the recap above.
            if (closing && honoursFollow)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] The sign-off is held back to come after the achievements, so the program ends on it.");
            }

            return WithPrincipal(new RecapPlan(context, false, closing, string.Empty, Array.Empty<string>(), null), principal);
        }

        var mechanicName = (_settings.RecapMechanicName ?? string.Empty).Trim();
        IReadOnlyList<string> setupFacts = Array.Empty<string>();

        try
        {
            setupFacts = MechanicSetupFacts?.Invoke(record, round) ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] The Technical Lead goes without the Garage's setup log - it could not be read: {ex.Message}");
        }

        // The mechanic's voice for the engine in use. With none picked for
        // it the announcer's own voice answers - and that is said, because
        // both parts in one voice sounds like a mistake.
        var engine = _announcer.VoiceEngineInUse;
        var voices = _settings.MechanicVoice;
        var voice = voices.IsSetFor(engine) ? voices : null;

        var who = mechanicName.Length > 0 ? mechanicName : "the Technical Lead";

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [commentary] At the flag: down to pit lane for a word with {who}"
            + (voice is not null
                ? $" in the voice {(voices.NameFor(engine) is { Length: > 0 } picked ? $"\"{picked}\"" : "picked for it")}"
                : $" in the announcer's own voice - no Technical Lead's voice is picked for {EngineName(engine)} on Voice > Cast")
            + (setupFacts.Count > 0
                ? ", with the setup the Garage has for the winning car."
                : ". No setup is logged for the winning car, so the Technical Lead won't talk settings."));

        return WithPrincipal(new RecapPlan(context, true, closing, mechanicName, setupFacts, voice), principal);
    }

    private static string EngineName(VoiceEngine engine) => engine switch
    {
        VoiceEngine.HeatCaller => "the HeatCaller voice",
        VoiceEngine.Kokoro => "the Kokoro voice",
        _ => "ElevenLabs",
    };

    // ==================== The honours ====================

    /// <summary>
    /// The honours as the recap's sequence will speak them.
    /// </summary>
    /// <param name="Turns">
    /// The announcer's throw, the reader's read, the announcer's hand-back
    /// - see RaceHonours.Segment. Never empty: a plan that exists is a
    /// segment that is going to be said.
    /// </param>
    /// <param name="Voice">The honours reader's voice, or null for the announcer's own - which is said in the console.</param>
    /// <param name="Badges">
    /// How many distinct badges the read in the middle actually names -
    /// see HonoursRead.Badges. Carried so the console can say when it
    /// differs from the number held back, which is the only way a club
    /// can tell a capped read from a short one.
    /// </param>
    private sealed record HonoursPlan(
        IReadOnlyList<ExchangeTurn> Turns, SecondVoice? Voice, int Badges, IReadOnlyList<AchievementUnlock>? Unlocks = null)
    {
        /// <summary>Set once the achievements segment has begun to go out - see the sequence's finally (22 Oct 2026).</summary>
        public volatile bool Started;

        /// <summary>
        /// WHY (26 Oct 2026, asked for by Asher): the AI's throw to the
        /// reader and the reader's hand back to the host, written once the
        /// recap has been and voiced while it plays - swapped in for the
        /// built-in turns by SpeakHonoursAsync when they are in hand. Null
        /// when there is no recap to follow, no reader's voice, or no AI.
        /// </summary>
        public HandOffJob? HandOff { get; set; }
    }

    /// <summary>
    /// Builds the honours segment for a race's badges - the throw, the read
    /// and the hand-back - or answers null when there is not going to be
    /// one.
    ///
    /// ON THE UI THREAD, AND THAT IS NOT A DETAIL. The phrasings come out of
    /// the director's rotation (ContinuityVoice), which is deliberately not
    /// locked and is touched from the announcer screen's own tick - and the
    /// sequence that speaks the honours runs on a thread-pool thread. So the
    /// picking happens here, on the save path, where every other fact the
    /// recap needs is gathered for the same reason.
    ///
    /// WHICH IS ALSO WHY THE LINES ARE MARKED AS HEARD HERE, at the pick
    /// rather than at the queue - the one place in the app that does not
    /// follow ContinuityVoice.Pick's own rule. That rule exists so a bridge
    /// the gap had no room for does not burn a phrasing, and it is right;
    /// the cost of breaking it here is one phrasing of sixteen, on a race
    /// whose honours were then dropped because the next race was called.
    /// The alternative is a HashSet written from whichever thread the
    /// sequence is on and read from the UI tick, which is a torn read over
    /// a PA.
    ///
    /// Null for every reason there might be no segment, and each of them is
    /// said in the console - because the badges were either read out here or
    /// called out one at a time, and "neither" must never be something a
    /// club has to work out for themselves.
    /// </summary>
    private HonoursPlan? PlanHonours(IReadOnlyList<AchievementUnlock> unlocks)
    {
        // Nothing said and nothing logged when the club has not asked for
        // the segment: that is not a club missing out on something, it is
        // the badge calls working exactly as they always have.
        if (!_settings.RecapBadgeHonours)
        {
            return null;
        }

        try
        {
            // The DIRECTOR's rotation, not one of this page's own, so that
            // "nothing twice in one night" counts the whole night. Built on
            // first use by the announcer screen; null only on a page put
            // together without a commentary service, which is a preview
            // pass and not a race night.
            if (Commentary?.Invoke()?.Continuity is not { } rotation)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [achievements] No achievements segment - there is no announcer screen to take the phrasings "
                    + "from. The achievements are called out one at a time instead.");

                return null;
            }

            var turns = RaceHonours.Segment(unlocks, _settings.RecapHonoursName, rotation);

            if (turns.Count == 0)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [achievements] No achievements segment for this race - either every way of throwing to the "
                    + "achievements has been heard tonight, or no achievement had both a driver and a name that "
                    + "could be read out. The achievements are called out one at a time instead.");

                return null;
            }

            // Marked as heard NOW - see the remarks. Only the announcer's
            // own turns: the read in the middle is built from the badges and
            // is different every race, so there is no phrasing to rotate and
            // nothing worth remembering about it.
            foreach (var turn in turns)
            {
                if (!turn.FromCoHost)
                {
                    rotation.Said(turn.Line);
                }
            }

            // The reader's voice for the engine in use. With none picked the
            // announcer's own voice reads the honours as well - the
            // mechanic's own answer to the same question - and that is said,
            // because a hand-over that comes back in the same voice sounds
            // like a fault rather than like a setting nobody filled in.
            var engine = _announcer.VoiceEngineInUse;
            var voices = _settings.HonoursVoice;
            var voice = voices.IsSetFor(engine) ? voices : null;

            // WHY (26 Oct 2026, asked for by Asher: "reference the show host
            // name when sending it back"): the reader hands back to the
            // recap show's host by name before the host thanks them - the
            // reader used to stop and the host carry on, as if the read had
            // been a recording. Only with a reader's voice of their own: in
            // the announcer's own voice it would be the host handing back to
            // himself. The fourth turn is the host's thanks, as before; the
            // AI's version of the throw and of this line is swapped in when
            // ready (see HonoursPlan.HandOff).
            if (voice is not null && turns.Count == 3)
            {
                turns = new[]
                {
                    turns[0],
                    turns[1],
                    new ExchangeTurn(true, HandOff.HonoursBack(_settings.HostNameForRecapShow)),
                    turns[2],
                };
            }

            var who = CommentaryService.Field(_settings.RecapHonoursName) is { Length: > 0 } named
                ? named
                : "the achievements";

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [achievements] In the recap: over to {who}"
                + (voice is not null
                    ? $" in the voice {(voices.NameFor(engine) is { Length: > 0 } picked ? $"\"{picked}\"" : "picked for it")}"
                    : $" in the announcer's own voice - no achievements reader's voice is picked for {EngineName(engine)} on Voice > Cast")
                + $" - \"{turns[1].Line}\"");

            return new HonoursPlan(turns, voice, RaceHonours.Read(unlocks)?.Badges ?? 0, unlocks.ToList());
        }
        catch (Exception ex)
        {
            // Decoration on the save path, exactly as the standings and the
            // recap's own plan are: a fault here must never be the reason a
            // result goes unsaved. The badges then go out as individual
            // calls, which is the arrangement that has always worked.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [achievements] No achievements segment - it could not be set up: {ex.Message}. "
                + "The achievements are called out one at a time instead.");

            return null;
        }
    }

    /// <summary>
    /// What is next on the program after the race just run - "A-Main
    /// (Stock Buggy)" - for the sign-off to tee up. The first round not yet
    /// run, in program order, as the Next Up bar has it. Null when the
    /// program has nothing left, or there isn't one.
    /// </summary>
    private string? UpNextAfter(RoundDefinition? justRun)
    {
        var next = OrderedProgram().FirstOrDefault(r =>
            !r.IsRecorded && !string.Equals(r.Id, justRun?.Id, StringComparison.Ordinal));

        return next is null ? null : RoundTitle(next);
    }

    /// <summary>How a part of the recap sequence ended.</summary>
    private enum FollowOn
    {
        /// <summary>Spoken - or there was nothing usable to speak, which has been said.</summary>
        Spoken,

        /// <summary>Not ready in time, and skipped.</summary>
        Late,

        /// <summary>The next race was called to the grid.</summary>
        Stopped,
    }

    /// <summary>
    /// Whether the winner's Team Principal is interviewed after this race
    /// (17 Oct 2026): switched on, a main - or a heat unless the Cast tab
    /// says mains only - and, when the club asked for teams only, a winner
    /// with a team on their profile.
    /// </summary>
    private bool PrincipalAfter(SessionRecord record, RoundDefinition? round)
    {
        if (!_settings.PrincipalAfterRace || round is null || record.Results.Count == 0)
        {
            return false;
        }

        if (round.Kind != RoundKind.Main && !_settings.PrincipalInHeats)
        {
            return false;
        }

        if (_settings.PrincipalTeamsOnly)
        {
            var winner = record.Results.OrderBy(r => r.Position).First();

            if (string.IsNullOrWhiteSpace(_profiles.Get(winner.Transponder)?.Team))
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] No Team Principal after this race - the winner has no team "
                    + "on their profile, and the Cast tab says teams only.");
                return false;
            }
        }

        return true;
    }

    /// <summary>The recap's plan with the Team Principal added when one follows, and the voice they speak in said.</summary>
    private RecapPlan WithPrincipal(RecapPlan plan, bool principal)
    {
        if (!principal)
        {
            return plan;
        }

        var name = (_settings.PrincipalName ?? string.Empty).Trim();
        var engine = _announcer.VoiceEngineInUse;
        var voices = _settings.PrincipalVoice;
        var voice = voices.IsSetFor(engine) ? voices : null;

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [commentary] On the recap show: {(name.Length > 0 ? name : "the winner's Team Principal")} in the booth"
            + (voice is not null
                ? $" in the voice {(voices.NameFor(engine) is { Length: > 0 } picked ? $"\"{picked}\"" : "picked for it")}."
                : $" in the announcer's own voice - no Team Principal's voice is picked for {EngineName(engine)} on Voice > Cast."));

        return plan with { Principal = true, PrincipalName = name, PrincipalVoice = voice };
    }

    /// <summary>
    /// Writes the winner's Team Principal interview once the part before it
    /// has been written - the Technical Lead's, or the recap - then renders
    /// each line in its voice while the recap plays. Null when there is no
    /// recap for it to follow, nothing usable came back, or it was called
    /// off (17 Oct 2026).
    /// </summary>
    /// <param name="before">The part written just before - the local model takes one request at a time.</param>
    /// <param name="brief">The principal's night, read by the caller on the UI thread - see BoothBrief.</param>
    /// <param name="written">Handed the exchanges once written, so the caller can keep them in the diary once they have been aired.</param>
    private async Task<List<SpokenLine>?> WritePrincipalAsync(
        Task before, Task<string?> recapWritten, SessionRecord record, RecapPlan plan, ChampionshipSwing? championship,
        (double Stability, double Style)? delivery, BoothBrief brief,
        Action<IReadOnlyList<CommentaryService.InterviewExchange>> written, CancellationToken cancel)
    {
        try
        {
            try
            {
                await before;
            }
            catch
            {
                // Its own failure is its own to report; this still follows.
            }

            if (string.IsNullOrWhiteSpace(await recapWritten) || cancel.IsCancellationRequested)
            {
                return null;
            }

            // WHY (20 Oct 2026): READ BY THE CALLER, on the UI thread that
            // owns the stores, before the sequence went to the background.
            // This runs on the thread pool, and used to walk the history,
            // the rounds and the profiles itself while the UI could be
            // writing to them. Only the diary is read here - its own file.
            var (nightFacts, stance) = (brief.Facts, brief.Stance);

            // WHY (20 Oct 2026): with the principal's name as it is now, so a
            // sentence the club's previous principal said is not quoted back
            // to the new one as his own (the diary keeps plan.PrincipalName).
            var lastSaid = PrincipalDiary.FactLines(PrincipalLastSaid(brief.WinnerTransponder, plan.PrincipalName));

            if (stance.Brief.Length > 0)
            {
                var booth = BoothTreatment.Describe(delivery);

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] The Team Principal comes in {stance.Name.ToLowerInvariant()} - "
                    + $"read off {brief.WinnerSpokenName ?? "the winner"}'s night"
                    + (booth.Length > 0 ? $", {booth}." : "."));
            }

            var exchanges = await _commentary!.BuildPrincipalAfterAsync(
                record,
                plan.PrincipalName,
                plan.Context.Kind,
                championship,
                log: note => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {note}"),
                ct: cancel,
                nightFacts: nightFacts,
                stanceBrief: PrincipalStance.BriefFor(stance),
                lastSaid: lastSaid);

            if (exchanges is null)
            {
                return null;
            }

            // Not kept here - only once aired. See principalWords in the caller.
            written(exchanges);

            // The recap host asks, in the booth (18 Oct 2026) - not the
            // co-host, who is down in pit lane.
            //
            // AND IN THE BOOTH'S DELIVERY, NOT THE RECAP'S. The recap's is
            // the punchiest in the app, worked out once up front and handed
            // to everything downstream - so a host told to settle into a
            // chair was asking his questions in the voice he calls a photo
            // finish in. See BoothTreatment for the one lever this app has
            // for the difference, and for the three things it deliberately
            // does NOT rebuild because the recap's show already has them.
            //
            // THE GUEST'S OWN VOICE IS LEFT ALONE, as the Technical Lead's
            // is: his voice is the club's choice on Voice > Cast, and the
            // words the model gives him are already measured. What was
            // wrong here was a delivery borrowed from trackside, and that
            // was only ever the host's.
            return await RenderExchangesAsync(
                exchanges, RecapHostVoice, BoothTreatment.Delivery(delivery), plan.PrincipalVoice, cancel);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] No Team Principal interview - {ex.Message}");
            return null;
        }
    }

    // ==================== The Principal's own well (18 Oct 2026) ====================
    //
    // The facts that belong to the Team Principal and to nobody else in
    // the show, read off the stores and turned into the position he comes
    // in with. See PrincipalFacts for why a guest whose material the room
    // has already heard twice is a guest the room waits out, and
    // PrincipalStance for why the position is derived from the night
    // rather than set by the club.
    //
    // READ ON THE UI THREAD BY THE CALLER, like every other store read in
    // this file - the pass over the history, the rounds and the profiles
    // happens here and the model gets handed finished sentences. See
    // CommentaryService's note on RaceScene for the same arrangement.

    /// <summary>
    /// One driver's evening and the stance it puts their principal in, or
    /// nothing at all on a night too thin to say anything about.
    ///
    /// Empty facts and the plain stance is a real answer and the common
    /// one early in an evening: a club's first heat, a driver in their
    /// only race of the night, a round with no class filed against it. The
    /// prompts are written to ask a different set of questions when this
    /// comes back empty rather than to fill them in.
    /// </summary>
    /// <param name="transponder">The driver whose principal is being interviewed.</param>
    /// <param name="className">The class to count the season record over; blank for everything the club has run.</param>
    /// <param name="standing">Where that driver is in the championship, or null when nothing could say - see <see cref="PrincipalTablePosition"/>.</param>
    private (IReadOnlyList<FactBlock> Facts, PrincipalStance.Stance Stance) PrincipalNightBrief(
        string? transponder, string? className, int? standing)
    {
        var nothing = ((IReadOnlyList<FactBlock>)Array.Empty<FactBlock>(), PrincipalStance.Plain);

        // A LOCAL, so the null check below is the one the compiler trusts:
        // _rounds is a field, and a field narrowed before a call is not
        // narrowed after one.
        var rounds = _rounds;

        if (rounds is null || string.IsNullOrWhiteSpace(transponder))
        {
            return nothing;
        }

        try
        {
            if (CommentaryReaders.TheTeamNight(transponder, className, _profiles, _sessionHistory, rounds)
                is not { } read)
            {
                return nothing;
            }

            var (night, rival) = read;

            // KEYED ON THE NIGHT, not the race. Everything in these facts
            // is true for the whole evening, so a race-scoped key would
            // have a club running six heats hear the same sentence in all
            // six - the argument CommentaryWeekFacts.OneDriver makes about
            // its own key. The event's id when there is one, and the night
            // itself when the club filed no race day.
            var key = PrincipalNightKey(_events?.Active?.Id, DateTime.Now);

            // THEIR BEST FINISH, counting only races they finished - a
            // zero in the list is a retirement (see TeamNight.Finishes)
            // and Min over the raw list would make every retirement the
            // best night anybody ever had.
            var best = night.Finishes.Where(place => place > 0).DefaultIfEmpty(0).Min();

            return (PrincipalFacts.ForThePrincipal(night, rival, key), PrincipalStance.For(night, best, standing));
        }
        catch (Exception ex)
        {
            // A reader that threw costs the interview its depth and
            // nothing else - the questions are still asked off the race
            // itself. Said once, because a club seeing this every race has
            // something worth reporting.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Could not read the Team Principal's night: {ex.Message}");
            return nothing;
        }
    }

    /// <summary>
    /// What this team's principal said the last time he was in the booth,
    /// or null when there is nothing worth putting back to him - which is
    /// the first time, and every night the club has no diary for.
    ///
    /// Read rather than held, so a file written by another copy of the app
    /// or restored from a backup between races is picked up rather than
    /// quietly ignored - the rule every reader on this page follows.
    /// </summary>
    private PrincipalDiary.Entry? PrincipalLastSaid(string? transponder, string? principalName)
    {
        try
        {
            return PrincipalDiary.Load().LastSaidBy(transponder, principalName, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            // Costs the callback and nothing else; the interview is still
            // asked about tonight.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Could not read what the Team Principal said last: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Where a driver stands in the championship, for
    /// <see cref="PrincipalStance.For"/> - or null when nothing can say so
    /// for certain.
    ///
    /// MATCHED ON THE NAME, because that is the only key a
    /// <see cref="ChampionshipMove"/> carries, and NULL UNLESS EXACTLY ONE
    /// ROW MATCHES. Two drivers a club has named the same thing would
    /// otherwise pick whichever row came first and hand the principal a
    /// stance earned by somebody else's season - and a club with two Daves
    /// is a club, not an edge case. One ambiguous name costs the segment
    /// its two table-comparing stances and keeps the other three.
    /// </summary>
    private int? PrincipalTablePosition(string? transponder, IEnumerable<ChampionshipMove>? moves)
    {
        if (moves is null || string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        var profile = _profiles.Get(transponder);

        var names = new[] { profile?.DriverName, profile?.SpokenName }
            .Select(name => (name ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            return null;
        }

        var matched = moves
            .Where(move => move.Position > 0)
            .Where(move => names.Contains((move.DriverName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
            .Select(move => move.Position)
            .Distinct()
            .ToList();

        return matched.Count == 1 ? matched[0] : null;
    }

    /// <summary>
    /// Keeps the one commitment the principal made, so the next time this
    /// team is in the booth the host can put it back to him.
    ///
    /// THE LAST ANSWER AND ONLY THE LAST ANSWER, which is the third beat -
    /// see CommentaryService.PrincipalExchanges. The first two are a man
    /// being asked how his night went; the third is the only one that ends
    /// on something he has undertaken, and it is the only one worth
    /// quoting back.
    ///
    /// NOTHING IS KEPT FROM A TWO-BEAT INTERVIEW. A model that wrote only
    /// two exchanges never reached the third question, so its last answer
    /// is an answer about tonight rather than a commitment about next time
    /// - and quoting that back in a fortnight would be the host inventing
    /// a promise out of a remark. See PrincipalDiary, which refuses the
    /// same thing one layer down.
    /// </summary>
    /// <param name="spokenName">The winner's spoken name, read by the caller on the UI thread - see BoothBrief.</param>
    private void KeepWhatThePrincipalSaid(
        SessionRecord record, RecapPlan plan, IReadOnlyList<CommentaryService.InterviewExchange>? exchanges, string? spokenName)
    {
        // WHY (20 Oct 2026): never from a demo. A demo races the round's
        // real transponders, so its invented commitment replaced the real
        // team's last one and was quoted back at the next race night as a
        // promise made after a race that never happened. IsDemoSession, not
        // IsDemo, to catch older demo saves too - as SettleTheRoomsVote does.
        // The demo interview still airs; only the diary is left alone.
        if (record.IsDemoSession())
        {
            return;
        }

        if (exchanges is null || exchanges.Count < CommentaryService.PrincipalExchanges)
        {
            return;
        }

        var winner = record.Results.OrderBy(r => r.Position).FirstOrDefault();

        if (winner is null || string.IsNullOrWhiteSpace(winner.Transponder))
        {
            return;
        }

        try
        {
            var diary = PrincipalDiary.Load();

            diary.Keep(
                winner.Transponder,
                spokenName,
                plan.PrincipalName,
                record.Label,
                exchanges[^1].Answer,
                DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            // Costs next week's callback and nothing tonight.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Could not keep what the Team Principal said: {ex.Message}");
        }
    }

    // ==================== The co-host asks (18 Oct 2026) ====================

    /// <summary>
    /// Whether the co-host asks the interviews' questions - the Team
    /// Principal's and the Technical Lead's, after the race and before it.
    /// Whenever the club has a co-host (Commentary page); otherwise the host
    /// asks, as before.
    /// </summary>
    private bool CoHostInterviews => _settings.CommentaryCoHost;

    /// <summary>The co-host's name for the interview prompts (blank for none), or null when the announcer asks - see CommentaryService.AskedBy.</summary>
    private string? InterviewerName => CoHostInterviews ? (_settings.CommentaryCoHostName ?? string.Empty).Trim() : null;

    /// <summary>A driver's name for a line the voice reads: the announcer name when set, else their name.</summary>
    private string SpokenDriverName(string transponder)
    {
        var profile = _profiles.Get(transponder);

        return !string.IsNullOrWhiteSpace(profile?.AnnouncerName) ? profile!.AnnouncerName.Trim()
            : !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName.Trim()
            : string.Empty;
    }

    /// <summary>
    /// How long past the pause after the flag the throw into the stop set
    /// is waited for before the line written in StopSetThrow goes instead.
    /// The pause is usually long enough on its own; this is the margin for
    /// a slow night, short enough that the room never hears it as a stall.
    /// </summary>
    private const int ThrowGraceSeconds = 4;

    /// <summary>The names the throw into the stop set says, cleaned once for both the prompt and the check.</summary>
    private StopSetThrow.Names StopSetNames() => StopSetThrow.Names.Of(
        CommentaryService.Field(_settings.RecapShowName, CommentaryService.MaxShowNameChars),
        CommentaryService.Field(_settings.HostNameForRecapShow, CommentaryService.MaxShowNameChars),
        CommentaryService.Field(_settings.RecapAnnouncerName, CommentaryService.MaxShowNameChars));

    /// <summary>
    /// The throw into the stop set (19 Oct 2026): the model's line once it
    /// has passed StopSetThrow.Checked, or StopSetThrow.Fallback when it is
    /// not in within <paramref name="wait"/>, failed, or will not do. Never
    /// throws and never returns nothing - the break always gets its way in.
    /// A line given up on is cancelled, so it stops holding up the recap
    /// on the model on this PC.
    /// </summary>
    private async Task<string> ThrowToBreakAsync(
        Task<string?>? written, CancellationTokenSource writing, StopSetThrow.Names names, bool breakFollows,
        Task notBefore, TimeSpan wait, CancellationToken ct)
    {
        string? line = null;
        var why = "the AI wasn't asked for one";

        if (written is not null)
        {
            try
            {
                var first = await Task.WhenAny(written, DeadlineAsync(notBefore, wait, ct));

                if (first == written)
                {
                    line = await written;
                    why = "nothing came back";
                }
                else
                {
                    writing.Cancel();
                    why = $"it wasn't written in time ({wait.TotalSeconds:0}s)";
                }
            }
            catch (Exception ex)
            {
                why = ex is OperationCanceledException ? "the writing was stopped" : ex.Message;
            }
        }

        var notes = new List<string>();
        var checkedLine = line is null ? null : StopSetThrow.Checked(line, names, StopSetThrow.Recent, notes, breakFollows);

        if (line is not null && checkedLine is null)
        {
            why = $"the AI's line wasn't used - {string.Join("; ", notes)}: \"{line}\"";
        }

        var said = checkedLine ?? StopSetThrow.Fallback(names, breakFollows: breakFollows);
        StopSetThrow.Said(said);

        StatusMessage?.Invoke(checkedLine is not null
            ? $"{AppTime.LogStampNow()}  [commentary] Throw to the {(breakFollows ? "break" : "recap (no break tonight)")} written by the AI."
            : $"{AppTime.LogStampNow()}  [commentary] Throw to the {(breakFollows ? "break" : "recap (no break tonight)")}: the built-in line, because {why}.");

        return said;
    }

    /// <summary>
    /// <paramref name="wait"/> after <paramref name="notBefore"/> has
    /// finished - the pit lane heard out - or straight away once
    /// <paramref name="ct"/> is cancelled. Never throws.
    /// </summary>
    private static async Task DeadlineAsync(Task notBefore, TimeSpan wait, CancellationToken ct)
    {
        try
        {
            await Task.WhenAny(notBefore, Task.Delay(Timeout.Infinite, ct));
            await Task.Delay(wait, ct);
        }
        catch (OperationCanceledException)
        {
            // The next race is called - the throw is not waited for.
        }
    }

    /// <summary>The teaser's words, once they are settled, and its audio made ready - null audio when the voice is muted or the render failed.</summary>
    private async Task<(string Text, AnnouncementService.PreparedSpeech? Audio)> RenderTeaserAsync(Task<string> words)
    {
        var text = await words;
        AnnouncementService.PreparedSpeech? audio = null;

        try
        {
            audio = await _announcer.PrepareReminderAsync(text);
        }
        catch
        {
            // Spoken live instead - see the teaser below.
        }

        return (text, audio);
    }

    /// <summary>The words of a throw to pit lane - the race announcer's at the flag, see RenderFlagThrowAsync. Pure, for the tests.</summary>
    internal static string PitLaneThrow(string coHostName, bool principal, bool technicalLead, string winner)
    {
        var guests = principal && technicalLead ? "the Team Principal and the Technical Lead"
            : principal ? "the Team Principal"
            : "the Technical Lead";

        var whose = winner.Length > 0 ? $" from {winner}'s team" : " from the winning team";

        return coHostName.Length > 0
            ? $"Let's head down to pit lane, where {coHostName} is with {guests}{whose}. {coHostName}?"
            : $"Let's head down to pit lane for a word with {guests}{whose}.";
    }

    /// <summary>
    /// The announcer's throw to the co-host before a race, in front of the
    /// Team Principal's interview: "Jess, you're down in pit lane with Sam's
    /// team." Pure, for the tests.
    /// </summary>
    internal static string PitLaneThrowBefore(string coHostName, string driver)
    {
        var whose = driver.Length > 0 ? $"{driver}'s team" : "the team";

        return coHostName.Length > 0
            ? $"Before we go, let's head down to pit lane. {coHostName}, you're with {whose}."
            : $"Before we go, let's head down to pit lane for a word with {whose}.";
    }

    /// <summary>
    /// An interview's lines, rendered one at a time in the voices that say
    /// them: each question in the host's, with the host's delivery, and each
    /// answer in the guest's. Null once it is called off - each render costs
    /// characters, so that is checked before every one.
    /// </summary>
    private async Task<List<SpokenLine>?> RenderExchangesAsync(
        IReadOnlyList<CommentaryService.InterviewExchange> exchanges,
        SecondVoice? hostVoice,
        (double Stability, double Style)? delivery,
        SecondVoice? guestVoice,
        CancellationToken cancel)
    {
        var lines = new List<SpokenLine>();

        foreach (var exchange in exchanges)
        {
            if (cancel.IsCancellationRequested)
            {
                return null;
            }

            lines.Add(new SpokenLine(exchange.Question, hostVoice, delivery,
                await _announcer.PrepareReminderAsync(exchange.Question, delivery, hostVoice)));

            if (cancel.IsCancellationRequested)
            {
                return null;
            }

            lines.Add(new SpokenLine(exchange.Answer, guestVoice, null,
                await _announcer.PrepareReminderAsync(exchange.Answer, null, guestVoice)));
        }

        return lines;
    }

    /// <summary>
    /// The Technical Lead's pit lane interview AT THE FLAG (18 Oct 2026):
    /// written the moment the race is filed - it no longer waits for the
    /// recap - then rendered a line at a time, the questions in the
    /// co-host's voice (or the race announcer's own when the club has no
    /// co-host) and the answers in the Technical Lead's. Null when nothing
    /// usable came back or it was called off.
    /// </summary>
    private async Task<List<SpokenLine>?> WriteFlagInterviewAsync(
        SessionRecord record, RecapPlan plan, CancellationToken cancel)
    {
        try
        {
            if (_commentary is null || cancel.IsCancellationRequested)
            {
                return null;
            }

            var exchanges = await _commentary.BuildMechanicInterviewAsync(
                record,
                plan.MechanicName,
                plan.SetupFacts,
                plan.Context.Kind,
                log: note => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {note}"),
                ct: cancel,
                interviewer: InterviewerName);

            if (exchanges is null)
            {
                return null;
            }

            // The co-host asks when the club has one; otherwise the race
            // announcer himself, in his own voice - this is his part of the
            // night, not the recap show's.
            SecondVoice? askVoice = CoHostInterviews ? _settings.CoHostVoice : null;

            return await RenderExchangesAsync(exchanges, askVoice, null, plan.MechanicVoice, cancel);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] No Technical Lead interview - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The race announcer's throw down to pit lane at the flag: "Let's head
    /// down to pit lane, where Jess is with the Technical Lead from Sam's
    /// team. Jess?" In his own voice. See PitLaneThrow.
    /// </summary>
    private async Task<SpokenLine?> RenderFlagThrowAsync(SessionRecord record, CancellationToken cancel)
    {
        var line = PitLaneThrow(
            CoHostInterviews ? (_settings.CommentaryCoHostName ?? string.Empty).Trim() : string.Empty,
            principal: false,
            technicalLead: true,
            record.Results.OrderBy(r => r.Position).FirstOrDefault() is { } winner ? WinnerName(winner) : string.Empty);

        if (cancel.IsCancellationRequested)
        {
            return null;
        }

        return new SpokenLine(line, null, null, await _announcer.PrepareReminderAsync(line));
    }

    /// <summary>
    /// The winner as the throw says them: the spoken name, or - for a car
    /// with no profile - the name the race recorded (19 Oct 2026), so the
    /// throw names the team the recap is about to name instead of "the
    /// winning team".
    /// </summary>
    private string WinnerName(SessionResultEntry winner) =>
        SpokenDriverName(winner.Transponder) is { Length: > 0 } spoken ? spoken
        : DriverIntros.LooksLikeAName(winner.DriverName) ? winner.DriverName.Trim()
        : string.Empty;

    /// <summary>
    /// The co-host handing back up to the booth once the Technical Lead has
    /// spoken - in the co-host's voice. See FlagHandBack.
    /// </summary>
    private async Task<SpokenLine?> RenderFlagHandBackAsync(CancellationToken cancel)
    {
        var line = FlagHandBack(_settings.RecapAnnouncerName);

        if (cancel.IsCancellationRequested)
        {
            return null;
        }

        var voice = _settings.CoHostVoice;

        return new SpokenLine(line, voice, null, await _announcer.PrepareReminderAsync(line, null, voice));
    }

    /// <summary>
    /// The co-host's hand back to the race announcer: "Back to you, Dave."
    /// His FIRST name (19 Oct 2026) - "back to you, Rick Marshal" was a
    /// colleague reading his name off a card, and it landed right in front
    /// of his own "I'm Rick Marshal" in the throw to the break. Pure, for
    /// the tests.
    /// </summary>
    internal static string FlagHandBack(string? announcerName)
    {
        var who = CommentaryService.FirstName(announcerName);

        return who.Length > 0 ? $"That's the word from pit lane - back to you, {who}." : "That's the word from pit lane - back up to the booth.";
    }

    // ==================== The hand-overs, written and voiced ahead (26 Oct 2026) ====================
    //
    // WHY (26 Oct 2026, asked for by Asher: "When the show host throws it to
    // other cast member it sounds robotic. reference the show host name when
    // sending it back. add in a line or two. make it AI and prerender."):
    // the throw to pit lane, the pit lane hand back, the pre-race throw and
    // the achievements' throw were fixed cards, and the pre-race word and
    // the achievements never handed back at all. Now the model writes the
    // pair (CommentaryService.BuildHandOffAsync, checked by HandOff) WHILE
    // something else is playing, and each line is turned into speech as soon
    // as it is written - so when the moment comes it is bytes in memory, the
    // same as every other line of the show. The built-in lines are still
    // rendered alongside, and they go out for any half the model has not
    // finished, both writing and voicing, by the time it is wanted: the show
    // never waits on a hand-over by more than HandOffGrace.

    /// <summary>The model's hand-over, voiced: either half null when it was not written, turned down, or could not be rendered.</summary>
    private sealed record HandOffLines(SpokenLine? Throw, SpokenLine? Back);

    /// <summary>
    /// How long past the moment it is wanted a written hand-over may still be
    /// waited for. Short: the built-in line is already rendered and waiting,
    /// and a pause in front of a hand-over is the robotic sound this exists
    /// to get rid of.
    /// </summary>
    private static readonly TimeSpan HandOffGrace = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Writes a hand-over with the model and voices both lines - the throw in
    /// the host's voice, the hand back in the guest's. Off the UI thread: the
    /// caller has read everything the brief needs from the settings and the
    /// stores already.
    ///
    /// A line is only handed back WITH its audio. One the voice could not
    /// render is left out, so the built-in line - rendered already - goes
    /// instead of a written one that would be synthesised as it is spoken,
    /// which is the gap this is here to close.
    /// </summary>
    /// <param name="after">Waited for first, when the local model should finish something more urgent - null for none.</param>
    /// <param name="modelDone">
    /// Settled the moment the model has answered (or will not be asked), BEFORE
    /// the lines are voiced (26 Oct 2026, review) - so what waits on "done with
    /// the model" (the next race's write-aheads, see _flagLinesAsked) is not
    /// held up for the voicing, and a caller can tell a hand-over still being
    /// written from one only being voiced. See HandOffJob.
    /// </param>
    private async Task<HandOffLines?> WriteHandOffAsync(
        HandOff.Brief brief,
        SecondVoice? hostVoice,
        (double Stability, double Style)? hostDelivery,
        SecondVoice? guestVoice,
        Task? after,
        TaskCompletionSource modelDone,
        CancellationToken cancel)
    {
        try
        {
            if (after is not null)
            {
                try
                {
                    await after;
                }
                catch
                {
                    // Its own failure is its own to report; this still follows.
                }
            }

            if (_commentary is not { IsAvailable: true } commentary || cancel.IsCancellationRequested)
            {
                return null;
            }

            HandOff.Written? written;

            try
            {
                written = await commentary.BuildHandOffAsync(
                    brief, note => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {note}"), cancel);
            }
            finally
            {
                // Done with the model - the voicing below is not its business.
                modelDone.TrySetResult();
            }

            if (written is null || cancel.IsCancellationRequested)
            {
                return null;
            }

            SpokenLine? thrown = null;
            SpokenLine? back = null;

            if (written.Throw is { } throwText
                && await _announcer.PrepareReminderAsync(throwText, hostDelivery, hostVoice, cancel) is { } throwAudio)
            {
                thrown = new SpokenLine(throwText, hostVoice, hostDelivery, throwAudio);
            }

            if (!cancel.IsCancellationRequested
                && written.Back is { } backText
                && await _announcer.PrepareReminderAsync(backText, null, guestVoice, cancel) is { } backAudio)
            {
                back = new SpokenLine(backText, guestVoice, null, backAudio);
            }

            return cancel.IsCancellationRequested || (thrown is null && back is null) ? null : new HandOffLines(thrown, back);
        }
        catch (Exception ex)
        {
            if (!cancel.IsCancellationRequested)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] The hand-over goes out as the built-in lines - {ex.Message}");
            }

            return null;
        }
        finally
        {
            // Every way out that never reached the model settles it too.
            modelDone.TrySetResult();
        }
    }

    /// <summary>
    /// A hand-over being written and voiced: <paramref name="Voiced"/> is the
    /// lines with their audio (see WriteHandOffAsync), <paramref name="Written"/>
    /// settles as soon as the model has answered - see its modelDone.
    /// </summary>
    private sealed record HandOffJob(Task<HandOffLines?> Voiced, Task Written);

    /// <summary>Starts WriteHandOffAsync and hands back both halves of it - see HandOffJob.</summary>
    private HandOffJob StartHandOff(
        HandOff.Brief brief,
        SecondVoice? hostVoice,
        (double Stability, double Style)? hostDelivery,
        SecondVoice? guestVoice,
        Task? after,
        CancellationToken cancel)
    {
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        return new HandOffJob(
            WriteHandOffAsync(brief, hostVoice, hostDelivery, guestVoice, after, written, cancel),
            written.Task);
    }

    /// <summary>
    /// The written hand-over if it is in hand within <see cref="HandOffGrace"/>,
    /// or null - and then the built-in lines go. Said in the console either way,
    /// so a club can tell a written hand-over from a stock one.
    ///
    /// NO GRACE FOR ONE STILL BEING WRITTEN (26 Oct 2026, review): the grace is
    /// for the voicing of lines the model has already given. A hand-over the
    /// model has not answered yet is not waited for at all - on the model on
    /// this PC, which answers one request at a time, it would otherwise add
    /// its three seconds of silence to every race and still miss.
    /// </summary>
    private async Task<HandOffLines?> HandOffInTimeAsync(HandOffJob? job, string where, CancellationToken cancel)
    {
        if (job is null)
        {
            return null;
        }

        var (timedOut, lines) = job.Written.IsCompleted
            ? await WithinAsync(job.Voiced, HandOffGrace, cancel)
            : (true, job.Voiced.IsCompletedSuccessfully ? job.Voiced.Result : null);

        if (!cancel.IsCancellationRequested)
        {
            StatusMessage?.Invoke(lines is not null
                ? $"{AppTime.LogStampNow()}  [commentary] {where}: the hand-over written by the AI"
                  + (lines.Throw is null || lines.Back is null ? " (with one built-in line)." : ".")
                : $"{AppTime.LogStampNow()}  [commentary] {where}: the built-in hand-over, "
                  + (timedOut ? "because the AI's was not written and voiced in time." : "because the AI's would not do."));
        }

        return lines;
    }

    /// <summary>
    /// What the AI is told about the throw down to pit lane at the flag, and
    /// the co-host's hand back. The throw must still name the guest's role
    /// and whose team - the interview is written to pick up from exactly
    /// that (CommentaryService.ThrownDownTo). Read on the UI thread.
    /// </summary>
    private HandOff.Brief FlagHandOffBrief(SessionRecord record, string? resultRead)
    {
        var coHost = (_settings.CommentaryCoHostName ?? string.Empty).Trim();
        var winner = record.Results.OrderBy(r => r.Position).FirstOrDefault() is { } first ? WinnerName(first) : string.Empty;
        var team = winner.Length > 0 ? $"{winner}'s team" : "the winning team";

        return new HandOff.Brief(
            Host: _settings.RecapAnnouncerName ?? string.Empty,
            HostRole: "the race announcer, up in the booth, who has just called the finish",
            Guest: coHost,
            GuestRole: "the co-host, down in pit lane",
            About: $"a quick word with the Technical Lead from {team} about the win and the car",
            ThrowMustSay: $"that {(coHost.Length > 0 ? coHost : "the co-host")} is with the Technical Lead from {team}",
            JustSaid: resultRead,
            GuestCovers: $"how {team} won it and what they did to the car",
            Next: "the race announcer takes the room on towards the race recap");
    }

    /// <summary>
    /// The unofficial result at the flag (18 Oct 2026), in the race
    /// announcer's voice straight after the finish call, which names nobody:
    /// the winner, who they beat, and the fastest lap. Null when the winner
    /// has no name to say.
    /// </summary>
    private string? FlagResultFor(SessionRecord record)
    {
        var ordered = record.Results.OrderBy(r => r.Position).ToList();

        if (ordered.Count == 0)
        {
            return null;
        }

        var podium = ordered.Take(3).Select(r => SpokenDriverName(r.Transponder)).ToList();
        var fastest = ordered.Where(r => r.FastLapMs > 0).OrderBy(r => r.FastLapMs).FirstOrDefault();

        return FlagResult(podium, fastest is null ? null : SpokenDriverName(fastest.Transponder));
    }

    /// <summary>
    /// The sectors line for the flag (22 Oct 2026), or null: switched off,
    /// no split loop in this race, or fewer than two sectors with a time.
    /// The quickest through each sector from the saved results' own best
    /// sectors (Sectors.Bests), cut the way most of the field's were; ghosts
    /// and drivers kept out of records are left out, as they are of the
    /// sector records.
    /// </summary>
    private string? SectorsAtTheFlag(SessionRecord record)
    {
        var bests = record.Results
            .Where(r => !GhostService.IsGhost(r.Transponder)
                        && _profiles.Get(r.Transponder) is not { ExcludeFromRecords: true })
            .Select(r => (Result: r, Bests: Sectors.Bests(r)))
            .Where(x => x.Bests is { Length: >= 2 })
            .ToList();

        if (bests.Count == 0)
        {
            return null;
        }

        var cut = bests.GroupBy(x => x.Bests!.Length).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        var field = bests.Where(x => x.Bests!.Length == cut).ToList();
        var names = QuickestThroughSectors(field.Select(x => (x.Result.Position, x.Bests!)).ToList(), cut)
            .Select(i => i < 0 ? string.Empty : SpokenDriverName(field[i].Result.Transponder))
            .ToList();

        return _announcer.SectorsRecapLine(names);
    }

    /// <summary>
    /// For each sector, which of <paramref name="field"/> was quickest
    /// through it (its index), or -1 for none. A tie goes to the higher
    /// finisher. Pure, for the tests.
    /// </summary>
    internal static List<int> QuickestThroughSectors(IReadOnlyList<(int Position, long[] Bests)> field, int sectors)
    {
        var quickest = new List<int>(sectors);

        for (var sector = 0; sector < sectors; sector++)
        {
            var best = -1;

            for (var i = 0; i < field.Count; i++)
            {
                if (sector >= field[i].Bests.Length || field[i].Bests[sector] <= 0)
                {
                    continue;
                }

                if (best < 0
                    || field[i].Bests[sector] < field[best].Bests[sector]
                    || (field[i].Bests[sector] == field[best].Bests[sector] && field[i].Position < field[best].Position))
                {
                    best = i;
                }
            }

            quickest.Add(best);
        }

        return quickest;
    }

    /// <summary>The words of the unofficial result - see FlagResultFor. Pure, for the tests.</summary>
    /// <param name="podium">The top three's spoken names, in finishing order; a blank name is left out.</param>
    /// <param name="fastestLap">Who set the race's fastest lap, or null/blank for nobody.</param>
    internal static string? FlagResult(IReadOnlyList<string> podium, string? fastestLap)
    {
        if (podium.Count == 0 || string.IsNullOrWhiteSpace(podium[0]))
        {
            return null;
        }

        var winner = podium[0].Trim();
        var beaten = podium.Skip(1).Select(n => (n ?? string.Empty).Trim()).Where(n => n.Length > 0).ToList();

        var line = beaten.Count switch
        {
            0 => $"Unofficially, that's {winner} taking the win.",
            1 => $"Unofficially, that's {winner} taking the win, ahead of {beaten[0]}.",
            _ => $"Unofficially, that's {winner} taking the win, ahead of {beaten[0]} and {beaten[1]}.",
        };

        var fast = (fastestLap ?? string.Empty).Trim();

        if (fast.Length > 0)
        {
            line += string.Equals(fast, winner, StringComparison.OrdinalIgnoreCase)
                ? $" And {winner} had the fastest lap of the race too."
                : $" The fastest lap of the race went to {fast}.";
        }

        return line;
    }

    /// <summary>
    /// Writes and renders the sign-off once what comes before it has been
    /// written - the interview, or the recap. Null when there's no recap
    /// for it to close, nothing came back, or it was called off.
    /// </summary>
    /// <param name="before">The part written just before - the local model takes one request at a time.</param>
    private async Task<SpokenLine?> WriteClosingAsync(
        Task before, Task<string?> recapWritten, SessionRecord record, RecapPlan plan, (double Stability, double Style)? delivery, CancellationToken cancel)
    {
        try
        {
            try
            {
                await before;
            }
            catch
            {
                // Its own failure is its own to report; the sign-off still follows.
            }

            if (string.IsNullOrWhiteSpace(await recapWritten) || cancel.IsCancellationRequested)
            {
                return null;
            }

            string? line = null;

            try
            {
                line = await _commentary!.BuildBroadcastClosingAsync(record, plan.Context, cancel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] The model's sign-off failed ({ex.Message}) - the show's own sign-off goes out instead.");
            }

            if (cancel.IsCancellationRequested)
            {
                return null;
            }

            // THE SHOW SIGNS OFF EVERY TIME (16 Oct 2026). Nothing back from
            // the model used to mean no goodbye; now it means the fixed one.
            if (string.IsNullOrWhiteSpace(line))
            {
                line = SignOffFallback(plan.Context);
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] No sign-off came back from the model - the show's own goes out: \"{line}\"");
            }

            // THE SHOW IS NAMED EVERY TIME (17 Oct 2026). The model is asked
            // to say the show's name and does not always: a sign-off that
            // came back without it gets it on the end. See WithShowName.
            line = WithShowName(line, plan.Context.ShowName);

            // Tagged for delivery like the recap, and rendered in its voice.
            var spoken = _commentary!.PrepareSummaryForSpeech(line);

            return new SpokenLine(spoken, RecapHostVoice, delivery, await _announcer.PrepareReminderAsync(spoken, delivery, RecapHostVoice));
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] No sign-off - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Speaks the interview once the recap has been heard: a line at a time,
    /// each queued once the one before it has played, so that calling the
    /// next race to the grid stops it between lines rather than leaving the
    /// mechanic talking over the staging call.
    /// </summary>
    /// <param name="nextRace">Cancelled when the next race is called to the grid.</param>
    /// <param name="leadIn">A line said first when there is an interview to follow it - the host's throw to the co-host - or null.</param>
    /// <returns>How it went, and whether any of it was said.</returns>
    /// <param name="handBack">A line said last when the interview was said - the co-host handing back to the booth - or null.</param>
    /// <param name="after">What the wait is counted from, for the console.</param>
    private async Task<(FollowOn Outcome, bool Said)> SpeakInterviewAsync(
        Task<List<SpokenLine>?> interviewTask, CancellationToken nextRace, SpokenLine? leadIn = null,
        SpokenLine? handBack = null, string after = "the recap ending", string? onAir = null, bool anotherFollows = false,
        HandOffJob? handOff = null, string handOffWhere = "Pit lane")
    {
        // Why it stopped, read when it is said (22 Oct 2026, bug hunt): a Skip or a
        // Hold stops it too, and the log blamed the next race for those.
        string stopped() => $"Interview stopped - {Volatile.Read(ref _recapShow)?.StoppedBy ?? "the next race was called to the grid"}.";

        if (!await HeardOutAsync(nextRace))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped()}");
            return (FollowOn.Stopped, false);
        }

        var (timedOut, lines) = await WithinAsync(interviewTask, FollowOnWait, nextRace);

        if (nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped()}");
            return (FollowOn.Stopped, false);
        }

        if (lines is null)
        {
            StatusMessage?.Invoke(timedOut
                ? $"{AppTime.LogStampNow()}  [commentary] Interview skipped - it wasn't ready within {FollowOnWait.TotalSeconds:0}s of {after}."
                : $"{AppTime.LogStampNow()}  [commentary] No interview - nothing usable came back from the model. Any line above says why.");

            return (timedOut ? FollowOn.Late : FollowOn.Spoken, false);
        }

        // The AI's hand-over in for the built-in lead-in and hand back (26 Oct
        // 2026) - only now the interview is here, and with no grace at all for
        // one the model has not answered yet. See HandOffInTimeAsync.
        if (handOff is not null && await HandOffInTimeAsync(handOff, handOffWhere, nextRace) is { } written)
        {
            leadIn = written.Throw ?? leadIn;
            handBack = written.Back ?? handBack;
        }

        if (nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped()}");
            return (FollowOn.Stopped, false);
        }

        if (leadIn is not null || handBack is not null)
        {
            var whole = new List<SpokenLine>(lines.Count + 2);

            if (leadIn is not null)
            {
                whole.Add(leadIn);
            }

            whole.AddRange(lines);

            if (handBack is not null)
            {
                whole.Add(handBack);
            }

            lines = whole;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0 && !await HeardOutAsync(nextRace))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped()}");
                return (FollowOn.Stopped, i > 0);
            }

            // The same trim the honours got (11 Oct 2026), and the
            // interview was paying more for it than they were: it has no
            // hand-over beat of its own, so the whole gap between a
            // question and its answer was the two pads - 0.9s behind the
            // question and 0.15s in front of the answer - and every bit of
            // it was silence nobody had chosen.
            var run = CommentaryExchange.ExchangeRun.At(i, lines.Count);

            Say(
                lines[i],
                // The last line hard up against what follows it too, when
                // the caller has something straight after (19 Oct 2026):
                // the hand back at the flag, with the throw to the break
                // behind it.
                run.AnotherFollows || (anotherFollows && i == lines.Count - 1)
                    ? AnnouncementService.SpeechTail.BackToBack
                    : AnnouncementService.SpeechTail.Normal,
                run.FollowsAnother
                    ? AnnouncementService.SpeechLead.FollowsOn
                    : AnnouncementService.SpeechLead.Normal,
                onAir);
        }

        return (FollowOn.Spoken, true);
    }

    /// <summary>
    /// Renders the honours segment's lines while the teaser and the recap
    /// play, each in the voice that says it: the throw and the hand-back in
    /// the announcer's with the recap's delivery, the read in the honours
    /// reader's.
    ///
    /// Rendered AHEAD and not as each line is spoken, for the reason
    /// PrepareReminderAsync exists at all: a hand-over is three lines and
    /// so three network round trips, and three round trips in the middle of
    /// a broadcast are three silences nobody asked for. Nothing in here
    /// asks the model - the words were written before the sequence started
    /// - so this is the one part of the sequence that is ready whatever the
    /// model is doing.
    ///
    /// Index for index with the plan's turns, which is what lets
    /// SpeakHonoursAsync put the hand-over beat in the right two places.
    /// Null when it was called off or the rendering failed, and then the
    /// segment is skipped rather than half said.
    /// </summary>
    private async Task<List<SpokenLine>?> RenderHonoursAsync(
        HonoursPlan plan, (double Stability, double Style)? delivery, CancellationToken cancel)
    {
        try
        {
            var lines = new List<SpokenLine>(plan.Turns.Count);

            foreach (var turn in plan.Turns)
            {
                if (cancel.IsCancellationRequested)
                {
                    return null;
                }

                // The reader talks the way their voice does, like the
                // mechanic: the recap's punchier delivery is the
                // announcer's own setting, and handing it to a second
                // speaker would make both of them sound like the recap.
                var voice = turn.FromCoHost ? plan.Voice : RecapHostVoice;
                var say = turn.FromCoHost ? null : delivery;

                lines.Add(new SpokenLine(
                    turn.Line, voice, say, await _announcer.PrepareReminderAsync(turn.Line, say, voice)));
            }

            return lines;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [achievements] No achievements segment - its lines could not be rendered: {ex.Message}. "
                + "The achievements are on the drivers' records.");

            return null;
        }
    }

    /// <summary>
    /// The achievements' hand-over written by the AI (26 Oct 2026): the
    /// recap host's throw to the reader, picking up from how the recap
    /// ended, and the reader's hand back to the host by name, reacting to the
    /// read. Written once the recap has been - the local model takes one
    /// request at a time and the recap is wanted first - and voiced while the
    /// recap plays. Null when there is no recap or the AI's will not do; the
    /// built-in turns then go, already rendered. See HandOff.
    /// </summary>
    /// <param name="behind">
    /// The booth interview or the sign-off, when either is coming (26 Oct 2026,
    /// review): written behind them, so on the model on this PC - which answers
    /// one request at a time - the hand-over never makes either of them late.
    /// It is the one with a built-in line to fall back on.
    /// </param>
    private HandOffJob WriteHonoursHandOff(
        HonoursPlan plan, Task<string?> recapWritten, Task? behind, (double Stability, double Style)? delivery, CancellationToken cancel)
    {
        var modelDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        return new HandOffJob(WriteHonoursHandOffAsync(plan, recapWritten, behind, delivery, modelDone, cancel), modelDone.Task);
    }

    private async Task<HandOffLines?> WriteHonoursHandOffAsync(
        HonoursPlan plan, Task<string?> recapWritten, Task? behind, (double Stability, double Style)? delivery,
        TaskCompletionSource modelDone, CancellationToken cancel)
    {
        string? recap;

        try
        {
            recap = await recapWritten;
        }
        catch
        {
            // The recap's own failure is its own to report.
            modelDone.TrySetResult();
            return null;
        }

        if (string.IsNullOrWhiteSpace(recap) || cancel.IsCancellationRequested)
        {
            modelDone.TrySetResult();
            return null;
        }

        var reader = (_settings.RecapHonoursName ?? string.Empty).Trim();
        var badges = plan.Badges <= 1 ? "one achievement" : $"{plan.Badges} achievements";

        var brief = new HandOff.Brief(
            Host: _settings.HostNameForRecapShow,
            HostRole: "the host of the race recap show, who has just finished the recap",
            Guest: reader,
            GuestRole: "reads out the achievements the drivers earned in that race",
            About: $"reading out the achievements from that race - {badges} to hand out",
            JustSaid: recap,
            GuestCovers: plan.Turns.Count > 1 ? plan.Turns[1].Line : null,
            Next: "the host carries on with the rest of the recap show");

        return await WriteHandOffAsync(brief, RecapHostVoice, delivery, plan.Voice, behind, modelDone, cancel);
    }

    /// <summary>
    /// Speaks the honours: the announcer's throw, the read in the third
    /// voice, and the announcer's hand-back - a line at a time, each queued
    /// once the one before it has been heard, with the hand-over beat and
    /// the nudge of the bed wherever the voice changes hands.
    ///
    /// WHY THE BEAT IS THE SEGMENT. The turns were always going to be said
    /// in two voices, and queued back to back they come out concatenated,
    /// because the announcer's queue speaks what it is given with no gap at
    /// all. Two people in a booth do not do that: the one about to answer
    /// waits for the other to finish the thought, and that wait is what
    /// tells a room there are two of them rather than one voice with a
    /// setting changed halfway through. So it is ShowTreatment's own
    /// HandOverAsync and not a copy of it - the same beat, the same nudge,
    /// the same numbers a two-hander inside a block gets
    /// (CommentaryExchange.HandoffBeat and HandoffNudge). With no bed under
    /// it the nudge does nothing and the beat is still there, which is
    /// exactly right.
    ///
    /// With no treatment wired at all there is no beat, which is a page
    /// built without an announcer screen rather than a race night.
    ///
    /// Stops between lines when the next race is called to the grid, as the
    /// interview does, rather than leaving a third voice reading out badges
    /// over a staging call.
    /// </summary>
    /// <param name="plan">The segment, as PlanHonours built it.</param>
    /// <param name="rendered">Its lines, rendered while the teaser and the recap played.</param>
    /// <param name="show">The show the honours are a segment of, for the bed's nudge. Null when there is no music.</param>
    /// <param name="nextRace">Cancelled when the next race is called to the grid.</param>
    private async Task SpeakHonoursAsync(
        HonoursPlan plan, Task<List<SpokenLine>?> rendered, ShowRun? show, CancellationToken nextRace)
    {
        // 17 Oct 2026, Bug Hunt 8, A04-8: who stopped it - Skip, Hold or On
        // Air stop the recap show too, and this said "the next race" for all
        // of them. Read when it is said, since that is when it is known.
        string Stopped() =>
            $"Achievements stopped - {Volatile.Read(ref _recapShow)?.StoppedBy ?? "the next race was called to the grid"}. "
            + "The achievements are on the drivers' records.";

        if (!await HeardOutAsync(nextRace))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [achievements] {Stopped()}");
            return;
        }

        plan.Started = true;

        var (timedOut, lines) = await WithinAsync(rendered, FollowOnWait, nextRace);

        if (nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [achievements] {Stopped()}");
            return;
        }

        if (lines is null)
        {
            // Said either way, and said as a [badge] line rather than a
            // [commentary] one: the badges were held back from the
            // individual calls on the strength of this segment running, so
            // a club whose room heard nothing at all about them has to be
            // able to find out why from the console. They are still on
            // every driver's record, on the results card, and in the
            // [badge] lines above.
            StatusMessage?.Invoke(timedOut
                ? $"{AppTime.LogStampNow()}  [achievements] Achievements skipped - they were not rendered within "
                  + $"{FollowOnWait.TotalSeconds:0}s of the recap ending. The achievements are on the drivers' records."
                : $"{AppTime.LogStampNow()}  [achievements] Achievements skipped - their lines could not be rendered. "
                  + "The achievements are on the drivers' records.");

            return;
        }

        // WHY (26 Oct 2026, asked for by Asher): the AI's throw to the reader
        // and the reader's hand back to the host, written and voiced while
        // the recap played - in for the built-in turns, which are already
        // rendered and go out for any half that is not in hand by now.
        if (plan.HandOff is { } handOff && plan.Turns.Count == 4 && lines.Count == plan.Turns.Count)
        {
            var written = await HandOffInTimeAsync(handOff, "Achievements", nextRace);

            if (nextRace.IsCancellationRequested)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [achievements] {Stopped()}");
                return;
            }

            if (written is not null)
            {
                lines = new List<SpokenLine>(lines);

                if (written.Throw is { } thrown)
                {
                    lines[0] = thrown;
                }

                if (written.Back is { } back)
                {
                    lines[2] = back;
                }
            }
        }

        // Where the voice changes hands, worked out by the file whose
        // business that is rather than by comparing turns here - see
        // CommentaryExchange.Handoffs, whose own note is that a caller
        // comparing FromCoHost for itself is a caller that will get it
        // wrong the day a three-way exchange exists. This is that day.
        var handoffs = CommentaryExchange.Handoffs(plan.Turns).ToHashSet();
        var treatment = ShowMusic?.Invoke();

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                if (!await HeardOutAsync(nextRace))
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [achievements] {Stopped()}");
                    return;
                }

                if (handoffs.Contains(i) && treatment is not null && !await treatment.HandOverAsync(show, nextRace))
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [achievements] {Stopped()}");
                    return;
                }
            }

            // THE PADS, TRIMMED FOR A LINE WITH ANOTHER HARD AGAINST IT
            // (11 Oct 2026). This took the default 0.9s tail and the full
            // lead on every turn, and the hand-over's beat sat on top of
            // both - so "over to Eric" and Eric starting were a second and
            // a half apart, which is what killed the two-hander from the
            // floor. SpeechTail.BackToBack has existed for exactly this
            // since long before the honours did and had never been used.
            //
            // The run is worked out from the position rather than from
            // handoffs: the pads are about one line following another at
            // all, where the beat is about the VOICE changing, and the
            // three honours turns happen to be the same either way only
            // because the reader has the middle one to herself.
            var run = CommentaryExchange.ExchangeRun.At(i, lines.Count);

            Say(
                lines[i],
                run.AnotherFollows
                    ? AnnouncementService.SpeechTail.BackToBack
                    : AnnouncementService.SpeechTail.Normal,
                run.FollowsAnother
                    ? AnnouncementService.SpeechLead.FollowsOn
                    : AnnouncementService.SpeechLead.Normal,
                OnAirParts.Achievements);
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [achievements] Achievements spoken: \"{plan.Turns[1].Line}\"");
    }

    /// <summary>
    /// How many of the night's colour segments one programme carries.
    ///
    /// Two. The ladder would happily give more and a club would hear them
    /// all, which is how a four-minute recap becomes an eight-minute one:
    /// past two, a programme about a race that lasted ninety seconds stops
    /// being a programme and becomes the reason the next heat is late. Two
    /// is also what the night of 11 Oct actually produced on its own, which
    /// is a fair guide to what the ladder thinks is due after a race.
    /// </summary>
    private const int MostColourSegments = 2;

    /// <summary>
    /// The night's colour for this programme - the form guide, the grid
    /// history, whatever the segment ladder has due - written while the
    /// recap plays.
    ///
    /// ON THE UI THREAD, MARSHALLED. This is the one thing in here that
    /// cannot simply be called: CommentaryDirector.NextAsync works out
    /// every segment's facts BEFORE its first await, deliberately, because
    /// that is the last point in the pass that is certainly still on the
    /// thread the host called from - and every store those readers walk
    /// belongs to the UI thread. This method runs on a pool thread inside
    /// the post-race task, so it hands the START of each call back to the
    /// UI thread and awaits the task that comes out. The awaits inside
    /// NextAsync then land wherever they land, which is what it is built
    /// for.
    ///
    /// Answers an empty list rather than null for every way of having
    /// nothing: the AI off, the allowance spent, the ladder with nothing
    /// due, a page with no director behind it. The programme then reads
    /// exactly as it did before there was any of this.
    /// </summary>
    /// <returns>
    /// The segments, or an empty list. Declared NULLABLE to match
    /// WriteInterviewAsync and WriteClosingAsync, because WithinAsync takes
    /// a Task&lt;T?&gt; and Task&lt;T&gt; is invariant - a non-nullable one
    /// warns at the call rather than converting. It never actually answers
    /// null; empty is how it says there was nothing.
    /// </returns>
    private async Task<List<SpokenSegment>?> WriteColourAsync(CancellationToken cancel)
    {
        var found = new List<SpokenSegment>();

        for (var i = 0; i < MostColourSegments; i++)
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            SpokenSegment? said;

            try
            {
                said = await NextColourAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] The program goes without its color: {ex.GetType().Name}: {ex.Message}");
                break;
            }

            // Nothing due. Asking again would get the same answer - the
            // ladder's cooldowns do not move while this loop runs - so it
            // stops rather than spending two more round trips finding out.
            if (said is null)
            {
                break;
            }

            found.Add(said);
        }

        // SAID EITHER WAY, which it was not at first and which cost a
        // round of testing. With a line only on success, a night where the
        // colour did not appear looked identical in the log to a night
        // where this method never ran - and those have completely
        // different causes. The ladder says why it refused on its own
        // [brain] line; this says the pass happened at all.
        StatusMessage?.Invoke(found.Count > 0
            ? $"{AppTime.LogStampNow()}  [commentary] {Plural.Of(found.Count, "color segment")} in this program, after the achievements: "
              + string.Join(", ", found.Select(said => ShowBlock.CaptionFor(said.Kind) ?? CommentaryBudget.Spec(said.Kind).Title)) + "."
            : $"{AppTime.LogStampNow()}  [commentary] No color in this program - the segment ladder had nothing due. "
              + "Any [brain] line above says which segment it turned down and why.");

        return found;
    }

    /// <summary>
    /// One segment from the ladder, with the call STARTED on the UI thread
    /// - see WriteColourAsync for why that matters.
    /// </summary>
    private Task<SpokenSegment?> NextColourAsync(CancellationToken cancel)
    {
        var done = new TaskCompletionSource<SpokenSegment?>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (Commentary?.Invoke() is not { } director || IsDisposed)
                    {
                        done.TrySetResult(null);

                        return;
                    }

                    // ForTheProgramme, not Next: this is a segment to speak
                    // INSIDE the recap's show, and the ladder's
                    // between-races gap must not refuse it for the
                    // programme's own previous line. See
                    // CommentaryDirector.ForTheProgrammeAsync.
                    done.TrySetResult(await director.ForTheProgrammeAsync(cancel).ConfigureAwait(true));
                }
                catch (Exception ex)
                {
                    done.TrySetException(ex);
                }
            }));
        }
        catch (Exception)
        {
            // The window went away between the post-race task starting and
            // this being posted - the club closed HeatCaller during a
            // recap. No colour, and nothing worth reporting.
            done.TrySetResult(null);
        }

        return done.Task;
    }

    /// <summary>
    /// Speaks the night's colour inside the programme's own show: a mark
    /// between each one, then the segment, in two voices where it came back
    /// as two.
    ///
    /// NO SHOW OF ITS OWN, which is the entire point. The bed is already
    /// running and the opener was heard four minutes ago; these are further
    /// items in the same programme, so they get the same punctuation
    /// between them that a block's items get and nothing else. See
    /// ShowTreatment.PunctuateAsync.
    /// </summary>
    private async Task SpeakColourAsync(Task<List<SpokenSegment>?> written, ShowRun? show, CancellationToken nextRace)
    {
        // Why it stopped, read when it is said (22 Oct 2026, bug hunt): a Skip or a
        // Hold stops it too, and the log blamed the next race for those.
        string stopped() => $"Color dropped - {Volatile.Read(ref _recapShow)?.StoppedBy ?? "the next race was called to the grid"}.";

        var (timedOut, segments) = await WithinAsync(written, FollowOnWait, nextRace);

        // THE DIRECTOR IS TOLD WHICH WENT OUT (14 Oct 2026). It settles a
        // segment only when told Spoken - see
        // CommentaryDirector.CommitWhenSpoken - so every colour segment not
        // reached below is answered for as Dropped in the finally.
        var director = Commentary?.Invoke();

        // Not waited for - it came back too late, or the next race went
        // first - so whatever it writes later is dropped as it lands rather
        // than held until the director's own ceiling gives it back.
        if (timedOut && director is { } lateDirector)
        {
            _ = written.ContinueWith(
                late =>
                {
                    foreach (var unsaid in late.Result ?? new List<SpokenSegment>())
                    {
                        lateDirector.Dropped(unsaid);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        try
        {
            if (nextRace.IsCancellationRequested)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped()}");

                return;
            }

            if (segments is null || segments.Count == 0)
            {
                if (timedOut)
                {
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] The program's color was not written within {FollowOnWait.TotalSeconds:0}s of the achievements, so it is skipped.");
                }

                return;
            }

            var treatment = ShowMusic?.Invoke();

            // ONE ELEMENT BETWEEN TWO VOICES (16 Oct 2026). With the
            // soundscape's "between recap parts" stinger in the run of show,
            // that stinger has already played in front of the colour - so the
            // first colour segment gets nothing more, and the ones after it
            // get that same stinger rather than the club's show sting. Two
            // stingers back to back was what the room was hearing. Only a
            // soundscape with no stinger there falls back to the show sting.
            var imagingStinger = BroadcastImaging.Has(ImagingCue.BetweenRecapParts);
            var index = 0;

            // THE TWO-HANDERS RENDERED AHEAD (19 Oct 2026). Each turn used to
            // be synthesised only once the turn before it had been heard out,
            // so every change of voice carried a whole render's round trip -
            // a second or two of dead air between the announcer and the
            // co-host, on top of the pads - and the back and forth sounded
            // like what it was: one line asked for after another. Now every
            // exchange's turns are rendered in order the moment the colour
            // is in hand, while the stinger, the punctuation and the segments
            // in front of them play, the way the honours and the interviews
            // already were. A turn whose render failed or never arrived is
            // spoken as before, rendered as it is said.
            var renders = RenderColourExchangesAhead(segments, nextRace);

            foreach (var said in segments)
            {
                var position = index;
                var firstSegment = index++ == 0;

                if (!await HeardOutAsync(nextRace))
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped()}");

                    return;
                }

                if (imagingStinger)
                {
                    if (!firstSegment)
                    {
                        await StingBetweenRecapPartsAsync(show, nextRace);
                    }

                    if (nextRace.IsCancellationRequested)
                    {
                        SayUnlessTheNextRaceStopped(stopped(), nextRace);

                        return;
                    }
                }

                // The mark between two items of a programme - the same one a
                // block puts between its own items, so a colour segment inside
                // the recap is punctuated the way one inside a block is. False
                // also means "nothing to play" (show music off, or no sting),
                // which is a reason to go on without the mark, not to drop the
                // colour - so only the next race being called stops it here.
                else if (treatment is not null
                    && !await treatment.PunctuateAsync(nextRace)
                    && nextRace.IsCancellationRequested)
                {
                    SayUnlessTheNextRaceStopped(stopped(), nextRace);

                    return;
                }

                if (said.IsExchange)
                {
                    // The turns' audio, rendered while what came before this
                    // segment played - usually long finished by now.
                    IReadOnlyList<AnnouncementService.PreparedSpeech?>? audio = null;

                    if (renders[position] is { } rendering)
                    {
                        (_, audio) = await WithinAsync(rendering, FollowOnWait, nextRace);

                        if (nextRace.IsCancellationRequested)
                        {
                            SayUnlessTheNextRaceStopped(stopped(), nextRace);

                            return;
                        }
                    }

                    // WHY (20 Oct 2026): going out now - see the director
                    // note above. Told Spoken here, after the wait for the
                    // turns' render, rather than before it: a call-off in
                    // that wait used to leave a two-hander nobody heard
                    // marked as said. Turn 0 is queued with no await between.
                    director?.Spoken(said);

                    for (var at = 0; at < said.Turns.Count; at++)
                    {
                        if (at > 0 && !await HeardOutAsync(nextRace))
                        {
                            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped()}");

                            return;
                        }

                        // The beat at a change of voice, and the pads trimmed
                        // for a turn with another hard against it - exactly as
                        // the honours do it. See CommentaryExchange.ExchangeRun.
                        if (at > 0 && treatment is not null && !await treatment.HandOverAsync(show, nextRace))
                        {
                            SayUnlessTheNextRaceStopped(stopped(), nextRace);

                            return;
                        }

                        var turn = said.Turns[at];
                        var run = CommentaryExchange.ExchangeRun.At(at, said.Turns.Count);
                        var tail = run.AnotherFollows
                            ? AnnouncementService.SpeechTail.BackToBack
                            : AnnouncementService.SpeechTail.Normal;
                        var lead = run.FollowsAnother
                            ? AnnouncementService.SpeechLead.FollowsOn
                            : AnnouncementService.SpeechLead.Normal;

                        if (audio is not null && at < audio.Count && audio[at] is { } prepared)
                        {
                            _announcer.SpeakPrepared(prepared, tail, lead, ShowBlock.NameFor(said.Kind));
                        }
                        else
                        {
                            _announcer.SpeakReminder(
                                turn.Line,
                                null,
                                turn.FromCoHost ? _settings.CoHostVoice : RecapHostVoice,
                                showOnScreens: true,
                                tail: tail,
                                lead: lead,
                                onAir: ShowBlock.NameFor(said.Kind));
                        }
                    }

                    continue;
                }

                // WHY (26 Oct 2026, asked for by Asher): voiced ahead with the
                // two-handers - see RenderColourExchangesAhead - so it follows
                // the stinger with no round trip in front of it. Not in hand,
                // it is made as it is said, as before.
                AnnouncementService.PreparedSpeech? alone = null;

                if (renders[position] is { } renderingAlone)
                {
                    var (_, voicedAlone) = await WithinAsync(renderingAlone, FollowOnWait, nextRace);

                    if (nextRace.IsCancellationRequested)
                    {
                        SayUnlessTheNextRaceStopped(stopped(), nextRace);

                        return;
                    }

                    alone = voicedAlone is { Count: > 0 } ? voicedAlone[0] : null;
                }

                // Going out now - see the director note above.
                director?.Spoken(said);

                if (alone is not null)
                {
                    _announcer.SpeakPrepared(alone, onAir: ShowBlock.NameFor(said.Kind));
                }
                else
                {
                    _announcer.SpeakReminder(said.Text, null, RecapHostVoice, showOnScreens: true, onAir: ShowBlock.NameFor(said.Kind));
                }
            }
        }
        finally
        {
            // Nothing for the ones already Spoken - see the note above.
            foreach (var unsaid in segments ?? new List<SpokenSegment>())
            {
                director?.Dropped(unsaid);
            }
        }
    }

    /// <summary>
    /// Every two-voice colour segment's turns, rendered one after another in
    /// the voices that say them, starting now - index for index with
    /// <paramref name="segments"/>; a segment in one voice is one turn in
    /// the host's (26 Oct 2026 - it used to be null and made as said). Each
    /// segment's renders wait for the one before it, so the first exchange
    /// to be spoken is the first one ready, and the relay is never asked for
    /// a whole programme's lines at once.
    ///
    /// The voices are read HERE, on the calling thread, where the settings
    /// belong; only the rendering runs off it. Each render costs characters,
    /// so the next race being called stops the rest - see
    /// RenderExchangesAsync, which does the same for the interviews.
    /// </summary>
    private Task<IReadOnlyList<AnnouncementService.PreparedSpeech?>?>?[] RenderColourExchangesAhead(
        IReadOnlyList<SpokenSegment> segments, CancellationToken nextRace)
    {
        var renders = new Task<IReadOnlyList<AnnouncementService.PreparedSpeech?>?>?[segments.Count];
        Task previous = Task.CompletedTask;

        var hostVoice = RecapHostVoice;
        var coHostVoice = _settings.CoHostVoice;

        for (var i = 0; i < segments.Count; i++)
        {
            // WHY (26 Oct 2026, asked for by Asher: "make sure everything ai
            // generated is prerendered so there's a quick back and forth with
            // no pausing"): a colour segment in ONE voice is voiced ahead too,
            // as a single turn in the host's voice. It used to be the one AI
            // line in the programme still made as it was said - a round trip
            // of silence after the stinger, every time.
            var turns = segments[i].IsExchange
                ? segments[i].Turns
                    .Select(turn => (turn.Line, Voice: turn.FromCoHost ? coHostVoice : hostVoice))
                    .ToList()
                : new List<(string Line, SecondVoice? Voice)> { (segments[i].Text, hostVoice) };
            var after = previous;

            var rendering = Task.Run<IReadOnlyList<AnnouncementService.PreparedSpeech?>?>(async () =>
            {
                try
                {
                    await after.ConfigureAwait(false);
                }
                catch
                {
                    // The segment before failing to render is no reason for
                    // this one not to.
                }

                var audio = new List<AnnouncementService.PreparedSpeech?>(turns.Count);

                foreach (var (line, voice) in turns)
                {
                    if (nextRace.IsCancellationRequested)
                    {
                        return null;
                    }

                    // Null when it could not be rendered - spoken as it is
                    // said instead, as before.
                    audio.Add(await _announcer.PrepareReminderAsync(line, null, voice).ConfigureAwait(false));
                }

                return audio;
            });

            renders[i] = rendering;
            previous = rendering;
        }

        return renders;
    }

    /// <summary>Says a line unless the next race is what stopped things, which already has its own line.</summary>
    private void SayUnlessTheNextRaceStopped(string line, CancellationToken nextRace)
    {
        if (!nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {line}");
        }
    }

    /// <summary>
    /// The sign-off with the show's name in it (17 Oct 2026): unchanged when
    /// it already says it, or when the club has typed no show name, and
    /// otherwise with "This has been &lt;show&gt;!" on the end.
    /// </summary>
    internal static string WithShowName(string line, string? showName)
    {
        var show = (showName ?? string.Empty).Trim().TrimEnd('.', '!', '?');

        if (show.Length == 0 || string.IsNullOrWhiteSpace(line) || line.Contains(show, StringComparison.OrdinalIgnoreCase))
        {
            return line;
        }

        var said = line.TrimEnd();

        // A line the model left without an ending stop gets one, so the tag
        // reads as a sentence of its own.
        if (said.Length > 0 && said[^1] is not ('.' or '!' or '?'))
        {
            said += ".";
        }

        return $"{said} This has been {show}!";
    }

    /// <summary>
    /// The sign-off the show says when the model gives it nothing: the
    /// show's name, the host's name and what is up next, whichever of them
    /// the club has. Never empty.
    /// </summary>
    internal static string SignOffFallback(CommentaryService.RecapContext context)
    {
        var show = (context.ShowName ?? string.Empty).Trim();
        var host = (context.AnnouncerName ?? string.Empty).Trim();
        var next = (context.UpNext ?? string.Empty).Trim().TrimEnd('.', '!');

        var line = show.Length > 0 ? $"And that's the recap here on {show}." : "And that's the recap.";

        if (host.Length > 0)
        {
            line += $" I'm {host}.";
        }

        line += next.Length > 0
            ? $" Up next, {next} - stay with us!"
            : " Stay with us - there's more racing on the way!";

        return line;
    }

    /// <summary>Speaks the sign-off kept back for the end, once everything before it has been heard.</summary>
    /// <param name="nextRace">Cancelled when the next race is called to the grid.</param>
    private async Task SpeakClosingAsync(Task<SpokenLine?> closingTask, CancellationToken nextRace)
    {
        // Why it stopped, read when it is said (22 Oct 2026, bug hunt): a Skip or a
        // Hold stops it too, and the log blamed the next race for those.
        string dropped() => $"Sign-off dropped - {Volatile.Read(ref _recapShow)?.StoppedBy ?? "the next race was called to the grid"}.";

        if (!await HeardOutAsync(nextRace))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {dropped()}");
            return;
        }

        var (timedOut, closing) = await WithinAsync(closingTask, FollowOnWait, nextRace);

        if (nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {dropped()}");
            return;
        }

        if (closing is null)
        {
            StatusMessage?.Invoke(timedOut
                ? $"{AppTime.LogStampNow()}  [commentary] Sign-off skipped - it wasn't ready within {FollowOnWait.TotalSeconds:0}s."
                : $"{AppTime.LogStampNow()}  [commentary] No sign-off - nothing came back from the model. Any line above says why.");
            return;
        }

        // The long tail is for a sign-off the bed's own ending follows.
        // With a bumper out of the show it is the bumper that follows, fired
        // the instant the last word is heard - so the short tail, or the
        // room hears two seconds of nothing before it (16 Oct 2026).
        Say(closing, BroadcastImaging.Has(ImagingCue.AfterRecap)
            ? AnnouncementService.SpeechTail.BackToBack
            : AnnouncementService.SpeechTail.Long, onAir: OnAirParts.SignOff);
    }

    /// <summary>
    /// Waits for whatever is being said to finish. False when the next race
    /// was called to the grid first - or already had been.
    /// </summary>
    private async Task<bool> HeardOutAsync(CancellationToken nextRace)
    {
        if (nextRace.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            // REPORTED, since 11 Oct 2026. This waits up to a minute and a
            // half and then carries on regardless, which is the right
            // behaviour - the honours and the sign-off must not be lost to
            // a long recap - but it used to do it silently. A night where
            // the recap overran and everything after it arrived bunched up
            // behind looked, in the log, exactly like a night where each
            // part was heard out properly.
            var lineLimit = SpokenWait();

            if (!await _announcer.WaitForAudioAsync(lineLimit, nextRace))
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Still speaking after {lineLimit.TotalSeconds:0}s, so the next part "
                    + "of the program is queued behind it rather than waited for any longer.");
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return !nextRace.IsCancellationRequested;
    }

    /// <summary>
    /// The longest the recap sequence waits for the UI thread to queue one of
    /// its lines. Reached when the page is going away and the posted work
    /// never runs, or when the UI thread is held up that long - without a
    /// limit the sequence would wait forever and never raise
    /// SessionAudioFinished. Once it gives up, the posted work does nothing
    /// if it runs later (see QueueUnlessNextRaceCalledAsync).
    /// </summary>
    private static readonly TimeSpan QueueOnUiThreadLimit = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs <paramref name="queue"/> - which queues a line of the recap
    /// sequence - unless the next race has been called to the grid. True
    /// when it ran.
    ///
    /// The test and the queueing happen together on the UI thread, as the
    /// round intro's do, because that is where staging happens: tested in
    /// the background and queued a moment later, a Stage pressed in between
    /// still let the line go out over the staging call. The race's state is
    /// read there as well as the token, since the session can raise the
    /// state change that cancels the token from another thread.
    ///
    /// Before the page has a window handle there is no UI thread to post to,
    /// so the token alone decides, as it did before.
    /// </summary>
    /// <param name="nextRace">Cancelled when the next race is called to the grid.</param>
    private async Task<bool> QueueUnlessNextRaceCalledAsync(Action queue, CancellationToken nextRace)
    {
        if (nextRace.IsCancellationRequested || IsDisposed || Disposing)
        {
            return false;
        }

        if (!IsHandleCreated)
        {
            queue();
            return true;
        }

        var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Who gets the last word: the posted work (it goes on to decide) or
        // the wait below (it has given up). Whichever claims it first wins,
        // and the other side then does nothing of its own. Without this,
        // giving up was not final - a UI thread that was merely busy for ten
        // seconds still ran the posted work later, queuing a line the
        // sequence had already abandoned. For the recap that also raised the
        // deep duck with nothing left to release it, and the music stayed
        // deep-ducked for the rest of the night.
        const int Open = 0, Claimed = 1, GaveUp = 2;
        var claim = Open;

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (Interlocked.CompareExchange(ref claim, Claimed, Open) != Open)
                {
                    // The sequence stopped waiting before this got its turn
                    // and has moved on. Too late to queue anything.
                    return;
                }

                try
                {
                    if (IsDisposed || Disposing
                        || nextRace.IsCancellationRequested
                        || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
                    {
                        queued.TrySetResult(false);
                        return;
                    }

                    queue();
                    queued.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    // Into the sequence's own catch, as a failure queued from
                    // the background always went.
                    queued.TrySetException(ex);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The handle went between the test above and the post.
            return false;
        }

        if (await Task.WhenAny(queued.Task, Task.Delay(QueueOnUiThreadLimit)) != queued.Task
            && Interlocked.CompareExchange(ref claim, GaveUp, Open) == Open)
        {
            // Out of time, and the posted work has not started: it never
            // will now, so nothing is queued behind the sequence's back.
            return false;
        }

        // Either it finished in time, or it claimed its turn just as the
        // limit ran out and is running on the UI thread now. Its answer -
        // queued, not queued, or the failure it hit - is the real one, and
        // it comes moments later.
        return await queued.Task;
    }

    // ==================== The commercial break ====================
    //
    // The club owner's ask, in his words: "I want the sponsors to play as a
    // commercial break after the race and before the race show recap. This
    // way we can fill the time it takes to generate the content." And: "big
    // screen should align with that."
    //
    // WHAT A BREAK IS is in CommercialBreak - the count the Big Screen
    // follows, the floor, the ceiling, and the one pure function that says
    // whether a break is over and why. WHEN one runs is here, because that
    // is a step of this sequence and nothing else in the app has any
    // business starting one.

    /// <summary>
    /// Holds a commercial break over the wait for the recap: the Big Screen
    /// runs the club's sponsors one slide straight into the next, the paid
    /// sales reads ride the video tails exactly as they already did, and the
    /// on-air band says COMMERCIAL BREAK. Returns when the break is over,
    /// whether or not one ran.
    ///
    /// WHERE IT SITS, AND WHY. After the teaser has been HEARD and before
    /// `await recapTask`. The teaser is the line INTO the break - "give us a
    /// second and we'll tell you how that one unfolded" is a station going
    /// to the adverts - and the recap is the one coming back from it, which
    /// is what it already says ("and we're back live here at the RC track").
    /// The recap's own opener still waits for the model, where rehearsal 5
    /// moved it, and nothing about that order changes: see step 4b.
    ///
    /// THE TEASER PLAYS EITHER WAY. It is not conditional on there being a
    /// break: it is the lead-in, it is the one part of this sequence that
    /// needs no model at all, and a club with no sponsors still wants to be
    /// told to stand by.
    ///
    /// WHAT ENDS IT, and in what order, is CommercialBreak.Over - read the
    /// note there, because the order of the four is the design and the floor
    /// is the condition on two of them.
    ///
    /// HOW THE SCREEN IS ALWAYS GIVEN BACK. One Begin, on the line before
    /// the try, with nothing between the two that can throw; one End, in the
    /// finally. There is no path out of this method - a return, the token, a
    /// failure nobody expected - that does not run it, and
    /// CommercialBreak.End is idempotent and never counts below zero on top
    /// of that. The shape EndShowAsync and ShowLifetime use for the show's
    /// music, and the one BigScreenForm uses for AdBreak.
    /// </summary>
    /// <param name="recapReady">The recap being written AND rendered - see BuildRecapAsync.</param>
    /// <param name="nextRace">Cancelled when the next race is called to the grid.</param>
    /// <param name="breakFollows">
    /// What was decided at the flag (22 Oct 2026, known issues) - and what
    /// the throw has already told the room. False runs no break even if a
    /// Big Screen has come up since: the room was told "coming up next".
    /// Null for the old way, asking the settings and the screen here.
    /// </param>
    private async Task HoldTheCommercialBreakAsync(Task recapReady, CancellationToken nextRace, bool? breakFollows = null)
    {
        // Decided already, and no: said as it always was, by the reason it was no.
        if (breakFollows == false && _settings.RecapAdBreak && CommercialBreak.SponsorScreenUp)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] No commercial break - there was no Big Screen with the sponsor slides up at the flag, "
                + "so the throw had already said the recap was coming up next.");

            return;
        }

        // Off by default, like every other part of the broadcast: a club
        // that never opens the Commentary page hears the recap it always
        // has. See AppSettings.RecapAdBreak.
        if (!_settings.RecapAdBreak)
        {
            // SAID, since 11 Oct 2026. This was the one way out of this
            // method with no line in the console, and it is also the most
            // likely one - the setting is off until a club turns it on. So
            // a club asking "why didn't the sponsors' video play after that
            // race?" had a log with nothing about a commercial break in it
            // at all: no break, no refusal, no reason. Every other way out
            // of here says what happened; this one now does too.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] No commercial break - it is switched off on the AI Commentary tab "
                + "(\"Play the sponsors as a commercial break while the recap is being written\").");

            return;
        }

        // Already called to the grid - the finish call and the teaser took
        // longer than the turnaround. Nothing of this race's sequence is
        // wanted now, and the break would be the first thing in the way.
        if (nextRace.IsCancellationRequested)
        {
            return;
        }

        // THE GATE THAT KEEPS THIS ADDITIVE - see
        // CommercialBreak.SponsorScreenUp. A club with no sponsor slides,
        // with them switched off, or with no Big Screen open has nothing to
        // put in a break, and a break with nothing in it is silence added in
        // front of the recap. That is a regression and not a feature, so
        // those clubs hear tonight exactly what they heard last week:
        // teaser, wait, recap.
        //
        // Asked here rather than at the flag with the standings and the
        // recap's plan, which is where this method's siblings are worked
        // out, because this one answer can change during the finish call: a
        // volunteer who opens the Big Screen while the winner is being
        // called should get their adverts.
        if (!CommercialBreak.SponsorScreenUp)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] No commercial break - no Big Screen is up with the club's sponsor slides switched on.");

            return;
        }

        // THE BUMPER INTO THE STOP SET PLAYS OUT BEFORE THE BREAK BEGINS
        // (16 Oct 2026). It used to be started inside the break, not
        // awaited, and the break's first sponsor video - which has a
        // soundtrack of its own - came up over the top of it. The bumper is
        // the door into the adverts; the adverts go after it, not through
        // it. Heard out WHOLE rather than to its post, because what follows
        // is somebody else's audio and not the programme's own voice, so
        // there is nothing to crossfade under it. The break's clock starts
        // after it, so the bumper does not eat the sponsors' time.
        //
        // Before Begin() and not between it and the try, which
        // CommercialBreakTests keeps empty - see the note inside the try.
        //
        // AND THE SHOW'S STING THE SAME WAY (16 Oct 2026, the same night).
        // A soundscape with no bumpers - Classic, Pit Lane before its set
        // came in - marks the way in with the club's show sting instead,
        // and that was still started inside the break and not awaited, so
        // it ran straight into the first video's soundtrack: two pieces of
        // audio train-wrecking over the top of a paid advert.
        await PlayTheWayIntoTheBreakAsync(nextRace);

        var started = DateTime.UtcNow;

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [commentary] Commercial break - the sponsors have the crowd's screen until the recap is ready.");

        CommercialBreak.Begin();

        try
        {
            // THE SOUND AT THE EDGE OF THE BREAK (15 Oct 2026). The teaser
            // is already the line into the adverts and the recap is the
            // line coming back - this method's own note says so, and
            // neither is being replaced. What was missing is the
            // PUNCTUATION: a station marks a boundary with a sound as well
            // as a sentence. See BreakTransition, which also says why no
            // new audio asset was added for it.
            //
            // INSIDE THE TRY, AS ITS FIRST STATEMENT, and that is not
            // tidiness. It was first written on the line after Begin() and
            // before the try, where CommercialBreakTests'
            // Whatever_starts_a_break_ends_it_from_a_finally caught it:
            // that test asserts there is NOTHING between the two, because
            // anything there that throws leaves the break open, and a
            // break left open keeps the standings off the crowd's wall for
            // the rest of the night, leaves the band reading COMMERCIAL
            // BREAK over every heat, and has a video rotation start
            // another advert every pass. MarkTheBreak is written not to
            // throw - but the invariant is structural on purpose, so that
            // it holds whatever a later hand puts here. In the try, the
            // finally covers it like every other path out.
            //
            // NOT AWAITED. The sting plays over the start of the break,
            // which is what a sting into the adverts is, and the loop below
            // must not be held up behind it - a club's sting can be long,
            // and the first thing that loop asks is whether the next race
            // has been called. The token fades it if that happens.
            MarkTheBreak(BreakEnding.Running, goingIn: true, nextRace);

            // The sponsor's copy, read INSIDE the stop set (16 Oct 2026) -
            // once, after the video advert, and before the break can end.
            var readDone = false;
            var sawVideo = false;

            // Whether the advert's own sound has been heard playing - see the
            // video test below.
            var heardVideo = false;

            while (true)
            {
                var on = DateTime.UtcNow - started;

                // AdBreak.Playing as well as the screen's report: the report
                // is a heartbeat that stays true for a few seconds after the
                // video ends, and the read should follow the advert at once
                // rather than after three seconds of nothing.
                sawVideo |= CommercialBreak.VideoOnScreen || AdBreak.Playing;
                heardVideo |= AdBreak.Playing;

                // NO DEAD AIR AFTER THE ADVERT (18 Oct 2026). The screen's
                // report is a heartbeat good for three seconds, so the break
                // went on holding for three and a half seconds of silence
                // after every video, every race. Once the advert's sound has
                // been heard, its stopping is the end of it; only a video
                // whose sound never reached us falls back on the heartbeat.
                var videoHolds = heardVideo ? AdBreak.Playing : CommercialBreak.VideoOnScreen;

                // THE COPY GOES IN THE BREAK, AFTER THE VIDEO (16 Oct 2026).
                // It used to wait for a quiet the director found on its own
                // tick, and with the recap show holding the floor the first
                // quiet was after the sign-off - so the club heard the
                // sponsor's words at the end of the recap, out of the break
                // and away from the sponsor's video. Now the break reads it:
                // once the video has played (or, with only stills, once the
                // break is past its floor), and before the break can end on
                // the recap being ready. Not once the next race is called or
                // the ceiling is reached - those end the break as before.
                if (!readDone
                    && !nextRace.IsCancellationRequested
                    && on < CommercialBreak.Longest
                    && !AdBreak.Playing
                    && (sawVideo || on >= CommercialBreak.Shortest))
                {
                    readDone = true;
                    await ReadTheSponsorsInTheBreakAsync(nextRace);
                    continue;
                }

                // IsCompleted and not awaited: a recap that failed or was
                // called off is just as much a reason to stop the adverts,
                // and what it came back with is the business of the line
                // after this method returns.
                var ending = CommercialBreak.Over(
                    on,
                    recapReady.IsCompleted,
                    CommercialBreak.ShownEverything,
                    nextRace.IsCancellationRequested,

                    // An advert actually playing on the crowd's screen
                    // keeps the break, up to the ceiling - see
                    // CommercialBreak.VideoOnScreen and videoHolds above.
                    videoHolds);

                if (ending != BreakEnding.Running)
                {
                    // 17 Oct 2026, Bug Hunt 8, A04-8: Skip, Hold or On Air end
                    // the break on the same token as the next race, and it was
                    // reported as the next race for all of them.
                    var why = ending == BreakEnding.NextRaceCalled && Volatile.Read(ref _recapShow)?.StoppedBy is { } by
                        ? $"abandoned - {by}."
                        : WhyTheBreakEnded(ending);

                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] Commercial break ran {on.TotalSeconds:0.0}s - {why}");

                    // Marked only where nothing else marks it - see
                    // BreakTransition.ComingBack. The recap's own opener
                    // and a grid call both already do, and a second mark
                    // in front of either is two returns for one break.
                    MarkTheBreak(ending, goingIn: false, nextRace);

                    return;
                }

                try
                {
                    // Looked at more often than CommercialBreak.Look while an
                    // advert is up (18 Oct 2026), so the way back comes in on
                    // the video's last frame rather than up to half a second
                    // after it.
                    if (videoHolds)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100), nextRace);
                    }
                    else
                    {
                        await Task.Delay(CommercialBreak.Look, nextRace);
                    }
                }
                catch (OperationCanceledException)
                {
                    // The next race, which the top of the loop says in
                    // words - so there is one place that reports an
                    // abandoned break rather than two that could come to
                    // disagree about how long it ran.
                }
            }
        }
        finally
        {
            // Exactly once, however the loop above ended: the recap ready,
            // the ceiling, the sponsors spent, the next race called to the
            // grid, or a failure nobody expected. Until this runs the Big
            // Screen keeps the sponsors over the standings and the band
            // keeps saying COMMERCIAL BREAK - and on a club with video
            // sponsors the rotation keeps starting another one, which is how
            // a break left open ends up muting the club's music for the
            // evening by the back door. See the note on CommercialBreak.
            CommercialBreak.End();
        }
    }

    /// <summary>
    /// Why the break ended, in the words the console says. A break that went
    /// wrong is otherwise impossible to account for after the night: the
    /// four endings look identical from the room and only one of them is a
    /// problem.
    /// </summary>
    private static string WhyTheBreakEnded(BreakEnding ending) => ending switch
    {
        BreakEnding.NextRaceCalled => "abandoned, the next race was called to the grid.",
        BreakEnding.Ceiling => "its ceiling, and the recap is still being written. The standings go back up and it goes out when it arrives.",
        BreakEnding.RecapReady => "the recap is written and rendered.",
        BreakEnding.EverySponsorShown => "it has been round every sponsor, so it ends rather than playing them again.",

        // Running, which the caller does not report, and anything a later
        // hand adds without coming back here. A default rather than an
        // exhaustive switch because a missing word must never be the thing
        // that takes a recap down.
        _ => "over.",
    };

    // MarkTheBreak sits AFTER WhyTheBreakEnded rather than before it, which
    // is a test's doing and worth the line. CommercialBreakTests extracts
    // HoldTheCommercialBreakAsync's body by reading between its signature
    // and WhyTheBreakEnded's - so a method placed between those two is read
    // as part of the break's own body. Nothing asserted against that range
    // failed, but it would have been a trap set for the next person to add
    // an assertion to it.

    /// <summary>
    /// Marks an edge of the break: the club's own show sting, and - coming
    /// back out of the two endings where nothing else covers the return -
    /// a line saying where the room is.
    ///
    /// WHAT DECIDES, AND WHERE. Whether this does anything at all is
    /// BreakTransition's, not this method's: a rule about what the room
    /// hears belongs with the other rules about that, and this is the step
    /// of the sequence that runs it. The same split CommercialBreak makes
    /// for whether a break is over.
    ///
    /// NOTHING HERE CAN COST A RECAP. The sting is fire-and-forget through
    /// ShowTreatment.MarkOnceAsync, which has already swallowed a failure
    /// to play; a club with no sting set, no show music at all or no
    /// treatment built yet gets silence and a console line. The spoken
    /// line goes through SpeakReminder, which is a no-op while muted. No
    /// path through here throws, and none of it is awaited, so the break's
    /// own loop and the recap behind it are untouched either way.
    /// </summary>
    private void MarkTheBreak(BreakEnding ending, bool goingIn, CancellationToken nextRace)
    {
        var said = goingIn ? null : BreakTransition.ComingBack(ending);

        // THE SOUNDSCAPE'S IMAGING MARKS THE EDGES WHEN IT HAS ANY (16 Oct
        // 2026): a bumper going into the stop set, and a jingle or sweeper
        // coming back out of it. Coming back it plays whatever ended the
        // break - the recap being ready included, because the run of show
        // puts the jingle between the stop set and the recap open - except
        // the next race being called, where the grid call is the way back.
        // With nothing set for the edge, the club's show sting marks it as
        // it always did.
        if (goingIn)
        {
            // Whatever marks the way in - the bumper or the show's sting -
            // has already played out whole, BEFORE the break began (see
            // PlayTheWayIntoTheBreakAsync), so the first sponsor video
            // never starts over it. Nothing is played from here.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Commercial break - the sponsors have the crowd's screen.");

            return;
        }

        if (!goingIn && ending != BreakEnding.NextRaceCalled && BroadcastImaging.Has(ImagingCue.BackFromBreak))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Break over - the station's jingle or sweeper marks the way back to the recap.");

            _backFromBreak = BackFromBreakThenSayAsync(said, nextRace);

            return;
        }

        if (!BreakTransition.StingMarks(ending, goingIn))
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] {BreakTransition.Describe(ending, goingIn, hadSting: false)}");

            return;
        }

        var treatment = ShowMusic?.Invoke();
        var sting = treatment?.StingForABoundary();

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [commentary] {BreakTransition.Describe(ending, goingIn, sting is not null)}");

        if (treatment is not null && sting is not null)
        {
            // Discarded deliberately, like the mark inside a block - see
            // ShowTreatment.MarkOnceAsync, which holds for the clip itself
            // and fades it if the next race is called.
            //
            // AND OBSERVED, which the continuation is for and is not
            // ceremony. MarkOnceAsync is an async method, so anything that
            // throws in it before its first await - loading a clip the
            // decoder cannot read - is captured into the task rather than
            // thrown here. Un-awaited and unobserved, that is a faulted
            // task left on the finalizer's doorstep. The cancellation case
            // needs none of this (a cancelled task is not a faulted one),
            // and MarkOnceAsync must keep throwing it either way because
            // MarkAsync reads that throw as "the block was abandoned".
            //
            // 17 Oct 2026, Bug Hunt 8, A05-3/A05-5: at the level that goes
            // with the sting picked, and faded on a soundscape piece's Out.
            _ = treatment
                .MarkOnceAsync(sting.Clip, sting.Level, "break sting", nextRace, sting.Post)
                .ContinueWith(static done => _ = done.Exception, TaskScheduler.Default);
        }

        if (said is not null)
        {
            _announcer.SpeakReminder(said, showOnScreens: true);
        }
    }

    /// <summary>
    /// Plays whatever marks the way into the stop set - the soundscape's
    /// bumper, or with none the club's show sting - and returns when the
    /// whole of it has played, so the break and its first video start
    /// after it and never over it (16 Oct 2026). Never throws.
    /// </summary>
    private async Task PlayTheWayIntoTheBreakAsync(CancellationToken nextRace)
    {
        try
        {
            // Talked up off the teaser and already playing (17 Oct 2026) -
            // heard out here, not played a second time.
            if (Interlocked.Exchange(ref _intoTheStopSet, null) is { } started)
            {
                // TO ITS OUT, NOT TO ITS LAST SAMPLE (18 Oct 2026): the
                // advert comes in where the bumper's own fade begins, and the
                // tail rides down under it over the crossfade - rather than
                // the room hearing the whole ring-out and then a beat of
                // nothing before the video.
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Into the break - the bumper, off the teaser, to its out.");
                await started.AtPost.WaitAsync(nextRace);
                started.CrossfadeOut();
                return;
            }

            if (BroadcastImaging.Has(ImagingCue.AfterRaceOver))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Into the break - the bumper plays out first.");
                await BroadcastImaging.PlayWholeAsync(ImagingCue.AfterRaceOver, nextRace);
                return;
            }

            if (!BreakTransition.StingMarks(BreakEnding.Running, goingIn: true))
            {
                return;
            }

            var treatment = ShowMusic?.Invoke();

            if (treatment?.StingForABoundary() is not { } sting)
            {
                return;
            }

            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Into the break - the sting plays out first.");

            // 17 Oct 2026, Bug Hunt 8, A05-3/A05-5: see MarkTheBreak.
            await treatment.MarkOnceAsync(sting.Clip, sting.Level, "break sting", nextRace, sting.Post);
        }
        catch (OperationCanceledException)
        {
            // The next race was called - the break's own loop says so.
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] The way into the break didn't play - {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the sponsor's copy inside the commercial break, with the
    /// sponsor on the crowd's screen and no video started under it, and
    /// returns once it has been heard (16 Oct 2026). Does nothing when the
    /// director has no read for this break - none due, a hold on, the club
    /// selling none - or the announcer is muted. Never throws.
    /// </summary>
    private async Task ReadTheSponsorsInTheBreakAsync(CancellationToken nextRace)
    {
        try
        {
            if (_announcer.Muted || Commentary?.Invoke() is not { } director)
            {
                return;
            }

            // On the UI thread, where the director's other reads are asked
            // - its rotation is not meant to be touched from two at once.
            //
            // 17 Oct 2026, Bug Hunt 8, A04-6: the gap and the sponsor on
            // screen are read in Pick too. This runs on the pool thread, and
            // both go through the director's readers into stores the UI
            // thread changes.
            SponsorRead? read = null;
            TimeSpan? gap = null;
            SponsorOnScreen? onScreen = null;
            SponsorRead? previous = null;

            void Pick()
            {
                gap ??= director.Upcoming?.Invoke().InAbout ?? SponsorCopy.NoLimit;

                // The first part, or the rest of the same copy straight on.
                read = previous is null ? director.NextBreakRead(gap.Value) : director.MoreOfTheBreakRead(previous, gap.Value);

                if (read is not null)
                {
                    // Counted before it is said, for the reason
                    // AnnouncerControl.SayTheSponsorsReadIfThereIsOne gives.
                    director.SalesReadSaid(read);
                }

                onScreen = read is null ? null : director.SponsorOnScreenFor?.Invoke(read.SponsorId);
            }

            CommercialBreak.ReadBegins();

            try
            {
                // THE WHOLE COPY, PART AFTER PART (16 Oct 2026). One read is
                // capped at SponsorCopy.LongestRead, so longer copy comes in
                // parts - and on 16 Oct the first part went out in the break
                // and the rest was left for "the next gap", which is now the
                // next race's break. Read through to the end here instead.
                // Six parts at most: a club's copy is capped at
                // SponsorCopy.MaxChars, so this is a guard and not a limit.
                for (var part = 0; part < 6 && !nextRace.IsCancellationRequested; part++)
                {
                    if (InvokeRequired)
                    {
                        Invoke((Action)Pick);
                    }
                    else
                    {
                        Pick();
                    }

                    if (read is null)
                    {
                        return;
                    }

                    if (onScreen is { } shown)
                    {
                        SponsorOnAir.Begin(shown.PictureFileName, shown.Name, SponsorCopy.SpokenLength(read.Text));
                    }

                    StatusMessage?.Invoke(previous is null
                        ? $"{AppTime.LogStampNow()}  [commentary] Commercial break - the sponsor's copy, read after the advert."
                        : $"{AppTime.LogStampNow()}  [commentary] Commercial break - the rest of the sponsor's copy, straight on.");

                    _announcer.SpeakReminder(read.Text, showOnScreens: true, onAir: OnAirParts.StopSet);

                    await _announcer.WaitForAudioAsync(SpokenWait(), nextRace);

                    if (!read.MoreToCome)
                    {
                        return;
                    }

                    previous = read;
                }
            }
            finally
            {
                CommercialBreak.ReadEnds();
            }
        }
        catch (OperationCanceledException)
        {
            // The next race - the break's own loop says so.
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] The sponsor's read in the break didn't go out - {ex.Message}");
        }
    }

    // ==================== Broadcast imaging (16 Oct 2026) ====================

    /// <summary>
    /// The jingle or sweeper MarkTheBreak started coming back out of a stop
    /// set, until the recap sequence has heard it out - see
    /// BackFromTheBreakAsync. Null when the break ended without one.
    /// </summary>
    private Task? _backFromBreak;

    /// <summary>
    /// The bumper into the stop set, when it was talked up off the teaser
    /// and is already playing as the break opens (17 Oct 2026) - taken by
    /// PlayTheWayIntoTheBreakAsync, which waits for it rather than playing
    /// another. Null otherwise.
    /// </summary>
    private ImagingPlay? _intoTheStopSet;

    /// <summary>
    /// The race recap host's voice for everything the host says in the recap
    /// show - the recap, the colour, the questions to the mechanic, the
    /// sign-off - or null when no host voice is picked for any engine, and
    /// the announcer reads the show as before. See AppSettings.RecapHostVoice.
    /// </summary>
    private SecondVoice? RecapHostVoice
    {
        get
        {
            var host = _settings.RecapHostVoice;

            return string.IsNullOrWhiteSpace(host.ElevenLabsVoiceId)
                && string.IsNullOrWhiteSpace(host.HeatCallerVoice)
                && string.IsNullOrWhiteSpace(host.KokoroVoiceName)
                ? null
                : host;
        }
    }

    /// <summary>
    /// Plays the soundscape's piece for one point in the run of show and
    /// holds until it has played - see BroadcastImaging.PlayAsync. Never
    /// throws: imaging is decoration, and a race night carries on without it.
    /// </summary>
    /// <param name="crossfadeAtPost">
    /// True when something starts at the piece's post and the rest of the
    /// piece crossfades into it; false when nothing follows at once and the
    /// piece plays out whole. Either way this returns at the post.
    /// </param>
    private async Task PlayImagingAsync(ImagingCue cue, CancellationToken nextRace, bool crossfadeAtPost = true)
    {
        try
        {
            await BroadcastImaging.PlayAsync(cue, nextRace, crossfadeAtPost);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] {BroadcastImaging.Describe(cue)} didn't play - {ex.Message}");
        }
    }

    /// <summary>
    /// The jingle back from the break, then - only on the endings where
    /// nothing else covers the return - the line saying where the room is.
    /// After the jingle, not over it.
    /// </summary>
    private async Task BackFromBreakThenSayAsync(string? said, CancellationToken nextRace)
    {
        await PlayImagingAsync(ImagingCue.BackFromBreak, nextRace);

        if (said is not null && !nextRace.IsCancellationRequested)
        {
            _announcer.SpeakReminder(said, showOnScreens: true);
        }
    }

    /// <summary>Waits for the jingle back from the break, if one is playing. Never throws.</summary>
    private async Task BackFromTheBreakAsync()
    {
        var playing = Interlocked.Exchange(ref _backFromBreak, null);

        if (playing is null)
        {
            return;
        }

        try
        {
            await playing;
        }
        catch
        {
            // Reported where it played.
        }
    }

    /// <summary>
    /// A stinger between two parts of the recap show, once the part before
    /// has been heard out. Nothing when the run of show has no stinger for
    /// the soundscape in use, or the next race has been called.
    /// </summary>
    private async Task StingBetweenRecapPartsAsync(ShowRun? show, CancellationToken nextRace)
    {
        if (!BroadcastImaging.Has(ImagingCue.BetweenRecapParts))
        {
            return;
        }

        if (nextRace.IsCancellationRequested)
        {
            return;
        }

        // HITTING THE POST (17 Oct 2026): the stinger is fired while the
        // part before it is still being said, so a stinger with a voice in
        // it comes in on the last word. One with no voice in it - its post
        // left at zero - fires as the last word ends, with no gap. See
        // TalkUp. This wait is also what used to be HeardOutAsync's: the
        // part before has been heard by the time it returns.
        //
        // The recap bed ducks under it the moment it starts and is HELD
        // there until its out - the host stopping must not bring the bed
        // back up on top of it (16 Oct 2026's trainwreck). See
        // ShowRun.DuckForSting.
        try
        {
            // ONE STINGER PER GAP (16 Oct 2026). A part that turns out to
            // have nothing to say - no colour due, say - still had its
            // stinger, and then the next part had its own, so the room heard
            // two stingers back to back with nobody talking between them.
            // Nothing said since the last one means this is the same gap.
            // Asked at the moment to fire rather than up front, because the
            // part in front may only just have been queued - by then it has
            // been heard. See BroadcastImaging.TalkUpAsync.
            await BroadcastImaging.PlayTalkedUpAsync(
                ImagingCue.BetweenRecapParts,
                _announcer,
                SpokenWait(),
                nextRace,
                onFire: () => show?.DuckForSting(),
                stillWanted: () => _stingSpeakingMark is not { } mark || mark != _announcer.TimesStartedSpeaking);
        }
        finally
        {
            show?.LetUp();
        }

        _stingSpeakingMark = _announcer.TimesStartedSpeaking;
    }

    /// <summary>
    /// The announcer's TimesStartedSpeaking when the last stinger between
    /// recap parts played - see StingBetweenRecapPartsAsync.
    /// </summary>
    private long? _stingSpeakingMark;

    /// <summary>Queues one line: its audio when it rendered in advance, otherwise rendered as it is spoken.</summary>
    /// <param name="onAir">Which part of the broadcast this is, for the race director's strip - see OnAirParts.</param>
    private void Say(
        SpokenLine line,
        AnnouncementService.SpeechTail tail = AnnouncementService.SpeechTail.Normal,
        AnnouncementService.SpeechLead lead = AnnouncementService.SpeechLead.Normal,
        string? onAir = null)
    {
        if (line.Audio is not null)
        {
            _announcer.SpeakPrepared(line.Audio, tail, lead, onAir);
        }
        else
        {
            // The pads reach the unprepared path too, since 11 Oct 2026.
            // The honours render in advance and so always take the branch
            // above, but a render that failed falls through to here and a
            // hand-over that sounded different depending on whether the
            // audio arrived early would be the worse bug of the two.
            _announcer.SpeakReminder(line.Text, line.Delivery, line.Voice, showOnScreens: true, tail: tail, lead: lead, onAir: onAir);
        }
    }

    /// <summary>
    /// The task's result if it finishes within <paramref name="limit"/>.
    /// Otherwise - or when <paramref name="cancel"/> goes first - TimedOut,
    /// and the task carries on unwatched (its own work stops with its own
    /// token).
    /// </summary>
    private static async Task<(bool TimedOut, T? Result)> WithinAsync<T>(Task<T?> task, TimeSpan limit, CancellationToken cancel)
        where T : class
    {
        if (!task.IsCompleted && await Task.WhenAny(task, Task.Delay(limit, cancel)) != task)
        {
            return (true, null);
        }

        try
        {
            return (false, await task);
        }
        catch
        {
            // Both tasks this waits on report their own failures.
            return (false, null);
        }
    }

    /// <summary>
    /// Signals the end of the broadcast for a race that has no recap
    /// sequence to run - teaser and recap both off, or a race that finished
    /// without being saved. Waits for the finish call to be spoken first, so
    /// music set to fade afterwards doesn't cut across it. Runs in the
    /// background: the caller is on the UI thread and must not block.
    /// </summary>
    private void SignalSessionAudioFinishedAfterFinishCall()
    {
        // The recording this end of broadcast belongs to, taken now. The
        // wait below is not cancelled by the next race being called, so on a
        // quick re-stage the signal can land after the next green - and must
        // not stop that race's recording a few seconds in.
        var recording = _raceAudioRecorder;

        _ = Task.Run(async () =>
        {
            try
            {
                await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            }
            catch
            {
                // The wait is only there to be polite to the finish call -
                // if it fails, the music still has to be told.
            }
            finally
            {
                RaiseSessionAudioFinished(onlyThisRecording: true, recording);
            }
        });
    }

    /// <summary>
    /// Raises SessionAudioFinished without letting a failing handler escape
    /// into a background task, where nobody would ever see it.
    /// </summary>
    /// <param name="onlyThisRecording">True to finish the race recording only when it is still <paramref name="recording"/> - for a signal that can arrive after the next race has started its own.</param>
    /// <param name="recording">The recording the signal belongs to; null when nothing was recording.</param>
    private void RaiseSessionAudioFinished(bool onlyThisRecording = false, RaceAudioRecorder? recording = null)
    {
        try
        {
            SessionAudioFinished?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [audio] End-of-broadcast signal failed: {ex.Message}");
        }

        // This is the one place every end of a broadcast meets - the recap
        // finishing, and the finish call on each of the paths that has no
        // recap - so it is where the recording of the race stops. Not at the
        // flag: the winner's call, the result and the interview all come
        // after it, and they are the part a driver actually wants.
        //
        // After the signal above rather than before it, so nothing here can
        // delay what the music is waiting for.
        try
        {
            // The recorder is the UI thread's, like everything else in the
            // filing, and this runs on a background task on most paths.
            Action finish = onlyThisRecording ? () => FinishRaceAudioIfStill(recording) : FinishRaceAudio;

            if (InvokeRequired)
            {
                BeginInvoke(finish);
            }
            else
            {
                finish();
            }
        }
        catch (Exception ex)
        {
            // Usually a window that has been closed since, so there is no
            // thread left to post to. The recording on disk is a complete,
            // playable wav at every moment by design (see RaceAudioRecorder),
            // and the next recording turns one left behind into an mp3 - so
            // this costs the mp3 and the clips tonight, not the audio.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [audio] Could not finish off the race recording "
                + $"({ex.Message}). It is still on disk as a .recording.wav and plays as it is; HeatCaller turns it "
                + "into an mp3 the next time it records a race.");
        }
    }

    // --- Recording the race -------------------------------------------------
    //
    // UI thread only, all of it, like the rest of the filing. The heavy work
    // - encoding, cutting the clips - is handed to a background task at the
    // one point where it happens (FinishRaceAudio), after the broadcast has
    // ended, and that task touches nothing on this control.

    /// <summary>
    /// The store as it was read at this race's green, for the recording
    /// running now. Read again at every green rather than once a run, so a
    /// change on the Commentary page - recording off, a new folder, more
    /// nights kept - is in force from the next race, as that page says.
    /// Once a race and not once a lap: FollowRaceAudio runs at every
    /// journal write.
    /// </summary>
    private RaceAudioStore? _raceAudioStore;

    /// <summary>The recording of the race on the board, or null when nothing is being recorded.</summary>
    private RaceAudioRecorder? _raceAudioRecorder;

    /// <summary>
    /// The session generation a recording was last started for - or tried
    /// for and refused.
    ///
    /// Keyed on the generation rather than on a bool because that is what
    /// names a race here: a reset, a re-stage and a fresh green all move it
    /// on (StartNewGeneration), so this is what tells "the race I am already
    /// recording" from "a new race that wants its own file". It is set even
    /// when the recording could not start, so a disk with no room is said
    /// once a race and not four times a second.
    /// </summary>
    private int _raceAudioGeneration = -1;

    /// <summary>
    /// The result as it was filed, kept only until the broadcast ends.
    ///
    /// This is where the drivers' names, their finishing positions and their
    /// lap times come from - the very rows the club's results are made of.
    /// Null when the race was never saved (a ghost-only race, a demo, a
    /// history write that failed): then the race's own mp3 is still written
    /// and only the per-driver clips are skipped, because there is nobody to
    /// name them after.
    /// </summary>
    private SessionRecord? _raceAudioRecord;

    /// <summary>Whether this race's recording has already complained. One line a race, not one a lap.</summary>
    private bool _raceAudioProblemSaid;

    /// <summary>
    /// Each driver's release offset, read off the board at the flag,
    /// alongside <see cref="_raceAudioRecord"/>.
    ///
    /// Zero for everybody in an ordinary race. In a staggered or qualifying
    /// start each driver is let go at their own moment and their lap times
    /// run from there, so their moments in the recording are that much later
    /// than the lap times alone say - up to a minute for the back of a
    /// staggered grid. The saved result does not carry the offset (only the
    /// crash journal does, as ReleaseOffsetsMs), so it is taken from the
    /// standings here, where they are still the ones that ran the race.
    /// </summary>
    private Dictionary<string, long>? _raceAudioReleaseOffsets;

    /// <summary>
    /// Keeps the recording in step with the race. Called after every crash
    /// journal write (the journal recorder's Written - see WireRaceControl),
    /// which is already the one place every crossing, every flag and every
    /// correction meets - so this needs no hook of its own on the session,
    /// and nothing about how a lap or a flag is handled changes.
    /// </summary>
    private void FollowRaceAudio()
    {
        try
        {
            // The next race is on the board and the last one's broadcast was
            // never signalled as over - a director who staged and greened
            // again straight away. The old recording is finished off here so
            // this race gets a file of its own instead of being recorded
            // onto the end of the last one.
            //
            // A reset leaves the session Idle with a recording running, for
            // the same reasons and with the same answer.
            if (_raceAudioRecorder is not null
                && (_raceAudioGeneration != _race.SessionGeneration || _race.State is RaceState.Idle))
            {
                FinishRaceAudio();
            }

            // After FinishRaceAudio, so a race greened straight after the
            // last one is still marked live - see FollowRaceLive.
            FollowRaceLive();

            // Once a race at its green, for every race including the ones
            // returned from just below - see _replayRestartGeneration
            // (22 Oct 2026). A recorded race empties it again in StartFor.
            if (_race.State is RaceState.Racing && _replayRestartGeneration != _race.SessionGeneration)
            {
                _replayRestartGeneration = _race.SessionGeneration;
                RaceReplayBuffer.Shared.Restart();
            }

            // Exactly the races the crash journal leaves alone, for exactly
            // its reasons: a demo's laps are invented, practice runs on this
            // same session and has its own save, and a rehearsal must never
            // drop files in among a club's real recordings.
            if (_race.IsDemoMode || _race.IsPracticeMode || AppPaths.IsRehearsal)
            {
                return;
            }

            if (_raceAudioRecorder is null
                && _race.State is RaceState.Racing
                && _raceAudioGeneration != _race.SessionGeneration)
            {
                // At the green, which is the one moment every start path
                // reaches and writes the journal at: the session puts the
                // state to Racing and THEN the flag to Green (GreenFlag), and
                // the flag change is a forced journal write. The countdown
                // would be nicer to have in the file but there is no journal
                // write of its own at Countdown, so catching it would be
                // luck on some paths and not others. Staging would be worse
                // again - it can sit there for ten minutes while the grid is
                // sorted out, recording an empty room.
                _raceAudioGeneration = _race.SessionGeneration;
                _raceAudioRecord = null;
                _raceAudioReleaseOffsets = null;
                _raceAudioProblemSaid = false;

                // Fresh at each green - see _raceAudioStore. With recording
                // off, StartFor answers null, and the generation set above
                // keeps this from reading the file again until the next race.
                var store = RaceAudioStore.Load();
                _raceAudioStore = store;

                // With the music and crowd kept for the videos (26 Oct 2026,
                // asked for by Asher: "video recaps should include the music
                // that is playing as well.") - see RecapBed. A race is the
                // only recording that has videos, so the only one that asks,
                // and only while "Music and crowd under the videos" is ticked
                // on Club Website > Recording ("add option to switch off. on
                // by default.") - StartFor checks it again.
                _raceAudioRecorder = RaceAudioRecorder.StartFor(
                    store,
                    _txtSessionLabel.Text,
                    DateTime.UtcNow,
                    message => StatusMessage?.Invoke(message),
                    withVideoBed: store.MusicUnderVideos);

                // The highlight videos' captions (21 Oct 2026): every line
                // the announcer is about to say, noted against whichever
                // recording is running. Hooked once; a race with no
                // recording simply has nothing to note it on.
                if (!_captionsHooked)
                {
                    _captionsHooked = true;
                    _announcer.LineHeard += text => _raceAudioRecorder?.NoteLine(text);
                }
            }

            if (_raceAudioRecorder is not { } recorder)
            {
                return;
            }

            // Where the green is in the file, which is what every highlight
            // is measured from. WriteJournal is forced at the green so this
            // lands within a frame of it - and it is worked out from the
            // wall clock rather than from the file's length anyway, so even
            // a late call puts the mark in the right place.
            if (_race.RaceStartUtc is { } green)
            {
                recorder.NoteGreen(green);
            }

            if (!_raceAudioProblemSaid && recorder.Problem is { } problem)
            {
                _raceAudioProblemSaid = true;

                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [audio] The recording of this race stopped early: "
                    + $"{problem}. What was recorded is kept and still becomes an mp3 when the race ends. The race "
                    + "itself is unaffected.");
            }
        }
        catch (Exception ex)
        {
            // The recording is the one thing on this page nobody would miss.
            // It is never allowed to be the reason a crossing, a flag or a
            // journal write goes wrong - the same rule RaceJournal.Record
            // keeps for the safety net itself.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [audio] Race recording has stopped following this race "
                + $"({ex.Message}). The race is running as normal.");
        }
    }

    /// <summary>
    /// Keeps <see cref="HighlightVideo.RaceLive"/> in step with the race on
    /// track: set while a race the recorder would record is Racing, clear in
    /// every other state.
    /// </summary>
    // WHY (22 Oct 2026, bug hunt 9): nothing set RaceLive, so the videos'
    // pause during a live race never happened. Worked out from the state
    // rather than set at the green and cleared at the end, so a reset, an
    // abandon, a restore or a demo taking over can never leave it stuck on
    // and hold the video queue (HighlightVideo.VideoGate) behind it. Not
    // cleared in FinishRaceAudio: that also runs when the next race has
    // already been greened, and that race is live. Only written when it
    // changes, so its 30-minute stale guard runs from the green. Demo,
    // practice and rehearsal never count - the same races FollowRaceAudio
    // does not record. Called from OnRaceStateChanged and FollowRaceAudio.
    private void FollowRaceLive()
    {
        var live = _race.State is RaceState.Racing
            && !_race.IsDemoMode
            && !_race.IsPracticeMode
            && !AppPaths.IsRehearsal;

        if (live != HighlightVideo.RaceLive)
        {
            HighlightVideo.RaceLive = live;
        }
    }

    /// <summary>
    /// <see cref="FinishRaceAudio"/>, but only while the recording running
    /// is still <paramref name="recording"/> - see
    /// SignalSessionAudioFinishedAfterFinishCall.
    /// </summary>
    private void FinishRaceAudioIfStill(RaceAudioRecorder? recording)
    {
        if (recording is null || !ReferenceEquals(_raceAudioRecorder, recording))
        {
            return;
        }

        FinishRaceAudio();
    }

    /// <summary>
    /// Takes the recording off the bus now, and hands the mp3 and the clips
    /// to a background thread.
    ///
    /// Split that way because this is sometimes the UI thread and encoding a
    /// twenty-minute race takes seconds. Nothing on this control is touched
    /// by the background work: the store, the result and the log are read
    /// out into locals first, so a Race Control closed while it runs cannot
    /// be reached from it.
    /// </summary>
    private void FinishRaceAudio()
    {
        var recorder = _raceAudioRecorder;

        if (recorder is null)
        {
            return;
        }

        _raceAudioRecorder = null;

        var record = _raceAudioRecord;
        _raceAudioRecord = null;

        var releaseOffsets = _raceAudioReleaseOffsets;
        _raceAudioReleaseOffsets = null;

        // Instant - it unhooks the tap and stops anything more being taken
        // in. Everything that waits is below.
        recorder.BeginStop();

        if (_raceAudioStore is not { } store)
        {
            // Cannot happen: a recorder only exists because the store said
            // so. The file is still closed off properly rather than left
            // open for the rest of the night.
            _ = Task.Run(() => recorder.Stop());
            return;
        }

        // Read again now, so the nights kept and the clips switch are what
        // the Commentary page says at the end of the race and not at its
        // green - a club that has just raised the nights kept must not lose
        // them to the old number. The green's copy stands in when the file
        // cannot be read this moment, rather than the defaults.
        var latest = RaceAudioStore.Load();

        if (!latest.LoadedUnreadable)
        {
            store = latest;
        }

        var log = StatusMessage;

        // Read here, on the UI thread, for the highlight videos made on the
        // background one: the Look, the logo, and each driver's photo and
        // number. See HighlightVideo.
        var video = VideoOptionsFor(store, record);

        // And where the drivers' own clips go (22 Oct 2026, driver
        // experience) - see ClipShare. Read here for the same reason.
        var clipShare = ClipShareFor(store, record);

        _ = Task.Run(() => RaceAudioRecorder.Publish(
            recorder.Stop(),
            store,
            record,
            releaseOffsets,
            message =>
            {
                try
                {
                    log?.Invoke(message);
                }
                catch
                {
                    // A console that has gone. Saying where the file went is
                    // not worth an unobserved exception on a pool thread.
                }
            },
            video,
            clipShare));
    }

    /// <summary>
    /// Where each driver's mp3 clip goes (22 Oct 2026, driver experience):
    /// their own night on the relay, behind their badge. Null unless the
    /// club ticked "share drivers' clips" on Club Website > Recording, offers
    /// drivers their nights, and the club's page is set up - or when nobody
    /// in the race has a badge id. Badge ids come only from a key that already
    /// exists.
    /// </summary>
    private ClipShare? ClipShareFor(RaceAudioStore store, SessionRecord? record)
    {
        var publisher = RelayPublisher.ForClips;

        if (record is null || !store.DriverHighlights || !store.ShareDriverClips || publisher is null || !publisher.CanShareDriverClips)
        {
            return null;
        }

        try
        {
            var ids = ClipShare.DriverIdsFor(record.Results, _profiles.Get, _settings.RelayCheckInSecret);

            if (ids.Count == 0)
            {
                return null;
            }

            var label = string.IsNullOrWhiteSpace(record.Label) ? "Race" : record.Label.Trim();

            return new ClipShare(
                RelayPublisher.IsAnUploadKey(record.Id) ? record.Id : string.Empty,
                label,
                ids,
                publisher);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [audio] Drivers' clips stay on this PC this race ({ex.Message}).");
            return null;
        }
    }

    // PodiumShareFor - where the podium's videos went online, with the Big
    // Screen's QR code as each one landed (21 Oct 2026) - is gone: 28 Oct
    // 2026, asked for by Asher, "Remove the podium videos for downloading
    // from the relay and just have locally." The videos stay on this PC.

    /// <summary>The race being filed by SaveCompletedSessionToHistory right now, whose recap follows - or null.</summary>
    private string? _filingForSummary;

    /// <summary>The highlight videos' captions are hooked to the announcer - once.</summary>
    private bool _captionsHooked;

    /// <summary>
    /// What the highlight videos need from this side (21 Oct 2026): which
    /// drivers get one, the club's handle and logo, the Look's colours, and
    /// each driver's photo and number. Null when no video is wanted. On the
    /// UI thread, because the theme and the profiles live there.
    /// </summary>
    private HighlightVideoOptions? VideoOptionsFor(RaceAudioStore store, SessionRecord? record)
    {
        if (record is null || !store.DriverHighlights || store.HighlightVideos == HighlightVideo.Who.Off)
        {
            return null;
        }

        try
        {
            var drivers = new Dictionary<string, HighlightDriver>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in record.Results)
            {
                if (string.IsNullOrWhiteSpace(row.Transponder) || drivers.ContainsKey(row.Transponder))
                {
                    continue;
                }

                var profile = _profiles.Get(row.Transponder);

                // Their own photo, always: the video stays on this PC (28
                // Oct 2026), so "no photo online" has nothing to keep it off.
                drivers[row.Transponder] = new HighlightDriver(profile?.PhotoFileName ?? string.Empty, profile?.RaceNumber ?? string.Empty);
            }

            var palette = new HighlightPalette(
                AppTheme.Background,
                AppTheme.Surface,
                AppTheme.Accent,
                AppTheme.Primary,
                AppTheme.TextDark,
                AppTheme.TextMuted,
                AppTheme.Success);

            return new HighlightVideoOptions(
                store.HighlightVideos,
                store.VideoHandle,
                _settings.RelayLogoFile ?? string.Empty,
                palette,
                drivers);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [video] No highlight videos this race ({ex.Message}).");
            return null;
        }
    }

    /// <summary>
    /// The longest the model gets to write a round intro. Past it the intro
    /// is skipped and the start carries on - the staging chain waits for
    /// the intro (see SpeakRoundIntro), so this is how long a slow or cold
    /// model can hold a start up. Deliberately shorter than the between-races
    /// budget the recap gets: nobody is waiting on a recap.
    /// </summary>
    private static readonly TimeSpan RoundIntroModelBudget = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long the staging chain waits for audio when an intro is coming:
    /// the model's budget, then the staging call and the intro spoken. The
    /// usual 20 seconds covered the staging call alone, and a start that
    /// waited that long went on to the countdown while the intro was still
    /// being written - which is what made rehearsal 1 play its countdown
    /// twice.
    /// </summary>
    private static readonly TimeSpan StagingWaitWithIntro = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The longest an intro's music may run before it ends itself.
    ///
    /// Normally the countdown takes it away, or the intro finishes being
    /// read and it ends on its own fade. This is for the night where
    /// neither happens - auto-advance off and the race director called
    /// away between staging and the countdown - so a bed cannot loop under
    /// an empty rostrum for the rest of the evening.
    /// </summary>
    private static readonly TimeSpan IntroShowLimit = TimeSpan.FromSeconds(120);


    /// <summary>
    /// The next round's intro, written before anybody presses Stage. See
    /// WrittenAhead, which states at length when a held line stops being
    /// true.
    ///
    /// On this control rather than on the director because this is where
    /// the intro lives: SpeakRoundIntro writes it, ReadIntroFacts reads its
    /// facts, and NextRound knows what is coming.
    /// </summary>
    private readonly WrittenAhead _writtenAhead = new();

    /// <summary>
    /// How long the model gets to write an intro ahead of time.
    ///
    /// Three times RoundIntroModelBudget, because nothing is waiting on
    /// this one. The whole point of writing ahead is that the clock has
    /// stopped being the constraint - a club gets the minutes between the
    /// flag and the next call to the grid - so a model that would have been
    /// too slow to use live can be used here. There is still a ceiling:
    /// a request left running for the rest of the evening is a request
    /// nobody will ever hear the answer to.
    /// </summary>
    private static readonly TimeSpan WriteAheadModelBudget = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Writes the NEXT round's intro now, so the start it belongs to waits
    /// for no model.
    ///
    /// CALLED WHEN A RESULT IS FILED, AND THAT TIMING IS THE WHOLE
    /// CORRECTNESS OF IT. The plan this came from says the next race's
    /// intro is knowable the moment the current race goes green. For the
    /// field and the venue that is true. For the STAKES and the FORM it is
    /// false, and the false half is the half that would put an untrue
    /// championship claim over a PA: an intro written at the green of round
    /// four, saying Bill leads by four points, is wrong by the time Bill
    /// has won round four.
    ///
    /// Nothing in WrittenAhead can catch that - a points total is not a key
    /// that gets spent, it is simply a different number - so it is
    /// prevented here, by WHEN this runs, and nowhere else. Do not move
    /// this call earlier, however tempting the green flag is.
    ///
    /// Everything about it is best-effort. It takes no pending-audio
    /// handle, so it can never hold a start up; it is never awaited; and a
    /// failure costs the night nothing at all, because the live path still
    /// writes its own intro exactly as it always did. The only thing a
    /// wasted write costs is a model call nobody heard, which WrittenAhead
    /// counts.
    ///
    /// AND A CORRECTED RESULT IS A FILING TOO. Whatever is held, and any
    /// write still in flight, was written from the results before this
    /// call, so both go first - a time penalty given in Edit Result left
    /// the old points total held, and it was read over the PA at staging.
    /// </summary>
    /// <param name="filed">
    /// The result just filed or corrected. Its round is skipped: the store
    /// announces a new result before the round is linked to it, so with the
    /// Round box empty (auto-advance, a restored race) the round just run
    /// still looked unrun and its own intro was written again.
    /// </param>
    /// <param name="corrected">True for a correction rather than a new filing.</param>
    public void WriteTheNextIntroAhead(SessionRecord? filed = null, bool corrected = false)
    {
        // Before anything can return: a held line is stale whether or not
        // a new one is wanted.
        var asOf = _writtenAhead.ResultsChanged();

        // WHY (20 Oct 2026): a new filing is followed by SpeakRaceSummary
        // asking for the flag lines, so the write-aheads below wait for
        // those to be asked first (at most 45s). The facts are still read
        // now, on this thread - that timing is what makes them true. A
        // correction has no flag lines behind it, so it shares whatever
        // gate is current. See _flagLinesAsked.
        //
        // ONLY A FILING THAT REACHES SpeakRaceSummary (22 Oct 2026, bug
        // hunt): every history add arrives here - a practice save, a race
        // re-filed late - and each one closed the gate, which nothing then
        // opened, so the write-aheads sat out the whole 45 seconds.
        if (!corrected && filed is not null && string.Equals(filed.Id, _filingForSummary, StringComparison.Ordinal))
        {
            _flagLinesAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var gate = _flagLinesAsked.Task;

        // The Team Principal's pre-race interview, written ahead the same
        // way and at the same moment (17 Oct 2026) - first, because the
        // intro's own switches below must not decide it.
        WriteThePrincipalAhead(filed, corrected, gate);

        // And the driver introductions (18 Oct 2026), for the same reason
        // and under their own switch - see WriteTheIntroductionsAhead.
        WriteTheIntroductionsAhead(filed, corrected, gate);

        // A CORRECTION MADE WHILE A RACE IS ON is not written for. The
        // round it would pick is the one after the race on track, whose own
        // filing throws the line away again - and the write would take the
        // model away from that race's live commentary for nothing. The
        // filing writes the next intro from the corrected results anyway.
        if (corrected && _race.State is (RaceState.Staging or RaceState.Countdown or RaceState.Racing))
        {
            return;
        }

        if (_commentary is null || !_commentary.IsAvailable || !_settings.CommentaryRaceIntros)
        {
            return;
        }

        // Last week's held line is not wanted this week. Guarded on the day
        // actually changing inside WrittenAhead, so this is free to call on
        // every result.
        _writtenAhead.NewMeeting(_events?.Active?.Id);

        if (NextRound(filed?.RoundId) is not { } next || string.IsNullOrWhiteSpace(next.Id))
        {
            return;
        }

        // NOT SKIPPED WHEN THE SAME ROUND IS ALREADY IN HAND, as it was:
        // a result filed or corrected since may have changed the points or
        // the form the held words state, and ResultsChanged above has
        // already let them go. One more model call is the price.

        // THE SAME FILTER THE LIVE PATH APPLIES. A club that only wants
        // intros for mains and rivalries must not have the model asked for
        // the heats either - the saving would be spent on lines that are
        // never said.
        if (_settings.CommentaryIntrosMainsOrRivalriesOnly
            && next.Kind != RoundKind.Main
            && !_commentary.HasRivalryAmong(next.AssignedTransponders))
        {
            return;
        }

        // On this thread, where the stores belong - see ReadIntroFacts.
        var (clubNight, form, stake, scene) = ReadIntroFacts(next);

        // The grid as it stands NOW, kept with the words: if somebody
        // scratches between here and the call to the grid, the held line
        // names a front row that is not there and has to go. See
        // WrittenAhead.Fingerprint.
        var grid = next.AssignedTransponders.ToList();
        var label = next.RoundLabel;

        // Whether the driver introductions will follow this intro - then it
        // leaves the roll call to them (18 Oct 2026). Read here, on the UI
        // thread, with the rest of the facts.
        var introductionsFollow = ReadIntroductionFacts(next, null, quiet: true) is not null;

        // CAPTURED, not read from the field inside the job. The guard at
        // the top of this method proved it was there on this thread; the
        // job runs later and the field is settable, so reading it again in
        // there would be checking one thing and using another.
        CommentaryService commentary = _commentary;

        _ = Task.Run(async () =>
        {
            // Behind the flag lines, and only then the budget starts - the
            // wait is not the model's time (20 Oct 2026, see _flagLinesAsked).
            await WaitForFlagLinesAsync(gate);

            try
            {
                using var budget = new CancellationTokenSource(WriteAheadModelBudget);

                PackedFacts? packed = null;

                var line = await commentary.BuildRoundIntroAsync(
                    next,
                    budget.Token,
                    log: null,
                    clubNight: clubNight,
                    form: form,
                    stake: stake,
                    historyPacked: pack => packed = pack,
                    scene: scene,
                    remember: false,
                    introductionsFollow: introductionsFollow);

                if (string.IsNullOrWhiteSpace(line))
                {
                    // Not said. A model that wrote nothing ahead of time
                    // has cost the night nothing, and the live path will
                    // ask again when the race is staged - so a line here
                    // would be a report of something that did not matter.
                    return;
                }

                // Not held if another result came in while this was being
                // written - see ResultsChanged.
                if (!_writtenAhead.Hold(next.Id, grid, line, packed, DateTime.Now, asOf))
                {
                    return;
                }

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Wrote {label}'s intro ahead of time - its start will "
                    + "wait for no model.");
            }
            catch (Exception ex)
            {
                // Best-effort, and said quietly. The live path is
                // untouched by this failing.
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Could not write {label}'s intro ahead: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Everything a round intro is written FROM, read on the UI thread.
    ///
    /// LIFTED OUT OF SpeakRoundIntro UNCHANGED (12 Oct 2026) so a round
    /// intro can also be written BEFORE it is wanted - see WrittenAhead.
    /// The write-ahead needs these same readers for a DIFFERENT round than
    /// the one being called to the grid, and a second copy of them would be
    /// a second answer to "what is this race's setting": the two would
    /// agree all season and then disagree about one race, which is the
    /// shape of fault this whole layer is written against.
    ///
    /// A pure move. Its only input from the caller is the round; everything
    /// else it touches is a field or a static on this control. The logging
    /// deliberately did NOT come with it and stayed where it was: the live
    /// path says all seven of its lines, and a write-ahead that said them
    /// again would double every intro's console noise for a line that may
    /// never be heard.
    ///
    /// ON THE UI THREAD, which is the reason it exists as a step of its own
    /// rather than being read where it is used. Every reader here walks the
    /// session history, the programme, the seasons and the event stores,
    /// all of which belong to that thread - the rule
    /// BugHunt5CommentaryTests pins for the director's own readers. Both
    /// callers must be on it.
    ///
    /// A reader that throws costs the intro its facts and nothing else: the
    /// catch says so and the intro goes out plainer, which is how it read
    /// before any of this existed.
    /// </summary>
    /// <param name="round">The round being introduced - not necessarily the active one.</param>
    private (bool ClubNight, RaceForm? Form, ChampionshipAtStake? Stake, RaceScene? Scene) ReadIntroFacts(RoundDefinition round)
    {
        // On a club night a race is just a race - it isn't called a heat.
        var clubNight = _events?.EventForRound(round.Id)?.IsClubNight == true;

        // The club's own records for this grid, and what the race decides
        // in the championship (10 Oct 2026) - the two things that make two
        // races with the same ten entries sound different.
        //
        // Worked out HERE and handed over, not read inside the job below:
        // both walk the session history, the programme, the seasons and the
        // event stores, and from the Task.Run onwards this is on a pool
        // thread - the rule BugHunt5CommentaryTests pins for the director's
        // own readers.
        //
        // Before the pending-audio handle is taken, so a fault cannot leave
        // the staging chain waiting on an intro that is never asked for.
        RaceForm? form = null;
        ChampionshipAtStake? stake = null;
        RaceScene? scene = null;

        // Which layout the venue's record is read for. The round's own
        // where it has one, because that is the name the intro's facts
        // print as "Track:" - a record line naming a different layout
        // than the line above it would have the prompt holding two tracks
        // for one race. Otherwise the picker's, which is the layout the
        // record is actually kept against (see
        // CheckAndAnnounceTrackRecord), and the venue line names it
        // itself.
        var layoutName = string.IsNullOrWhiteSpace(round.TrackLayoutName)
            ? SelectedTrackName()
            : round.TrackLayoutName;

        try
        {
            if (_rounds is not null)
            {
                var (grid, rivalries) = CommentaryReaders.GridHistory(
                    round.AssignedTransponders,
                    round.ClassName,
                    _profiles,
                    _sessionHistory,
                    _rounds);

                // The race day goes with it, and for the intro this is the
                // fix rather than a refinement: these facts are true for
                // the whole evening, an intro runs before every race, and
                // without a night key a club running six heats with the
                // same ten entries heard the same debut announced six
                // times. See RaceForm.NightKey and ForIntro's remarks.
                form = new RaceForm(grid, rivalries, round.ClassName, _events?.Active?.Id);
            }

            stake = CommentaryReaders.ChampionshipBefore(
                round,
                round.AssignedTransponders,
                _seasons,
                _rounds,
                _events,
                _sessionHistory,
                _profiles);

            // THE RACE'S OWN SETTING (11 Oct 2026): the venue's outright
            // lap record, how strong this field is against the club's
            // season, and what the night has done to these drivers' pace.
            // The three things that make two races with the same ten
            // entries sound different even before anybody's career is
            // mentioned - and the intro is where the app had least to say.
            //
            // The venue's record needs no programme, so it is read whether
            // or not there is a round store; the other two are counted
            // over the class's rounds and cannot be.
            //
            // THE WHOLE ROSTER, not the eight the facts name. The field's
            // counts are about the field - a ten-car race in which eight
            // are named is still a ten-car race - and the service filters
            // the per-driver lines to the drivers it names. See
            // FieldStrength.
            scene = new RaceScene(
                CommentaryReaders.LayoutRecord(_trackLayouts, _sessionHistory, layoutName),
                _rounds is null
                    ? null
                    : CommentaryReaders.Field(
                        round.AssignedTransponders,
                        round.ClassName,
                        _sessionHistory,
                        _rounds),
                _rounds is null
                    ? null
                    : CommentaryReaders.PaceTonight(
                        round.AssignedTransponders,
                        round.ClassName,
                        _profiles,
                        _sessionHistory,
                        _rounds),

                // Keyed on the night, for the same reason the history is:
                // the record and the entry are the same all evening, and
                // an intro runs before every race. See RaceScene.NightKey.
                _events?.Active?.Id,

                // AND THE NIGHT'S THREAD (11 Oct 2026): the one storyline
                // the evening is being told as, so a race intro can refer
                // to what the cold open set up. Read here with the rest of
                // the setting because it is the same UI-thread pass over
                // the same stores - see RaceScene.Thread for why it
                // travels with a race's setting without being one.
                //
                // No _rounds guard, unlike the field and the pace above:
                // the reader takes a null programme itself and answers
                // null, because a club with no programme has nobody it can
                // say is racing tonight.
                CommentaryReaders.NightStory(
                    _rounds,
                    _sessionHistory,
                    _profiles,
                    _events?.ActiveRoundScope()),

                // AND THE CHAMPIONSHIP CONDITIONAL (11 Oct 2026): one NAMED
                // finishing order and what the club's own results prove it
                // would do to the table - "if Carl wins this race and Bill
                // finishes second, Carl takes the lead of the Winter
                // Series".
                //
                // Read here with the rest of the setting because it is the
                // same UI-thread pass over the same stores the stakes above
                // already walked - the seasons, the programme, the events
                // and the history. From the Task.Run below onwards this is
                // on a pool thread, and those stores belong to the UI
                // thread; the rule BugHunt5CommentaryTests pins for the
                // director's own readers is the same rule here.
                //
                // THE WHOLE ROSTER, like the field above it and unlike the
                // per-driver lines. The proof has to price every driver who
                // could take points off the two it names, and a proof over
                // the eight the prompt happens to name is not a proof. See
                // CommentaryReaders.ChampionshipConditional.
                //
                // No _rounds guard: the reader is handed the round store and
                // answers null without one, because a season's rule cannot
                // take a race whose round it cannot see.
                CommentaryReaders.ChampionshipConditional(
                    round,
                    round.AssignedTransponders,
                    _seasons,
                    _rounds,
                    _events,
                    _sessionHistory,
                    _profiles),

                // AND WHAT THE GRID HAS FOUND SINCE PRACTICE (11 Oct 2026):
                // "he was a second off this in practice". The Tier 1 item
                // the depth plan asked for and which the pace figure above
                // is NOT - that one compares a race with a race. Read here
                // with the rest of the setting for the same UI-thread
                // reason, over the same history store.
                //
                // _rounds guard like the field and the pace: the RACES this
                // is measured against are scoped by the class, and a club
                // with no programme has no class to scope them by. The
                // practice side needs no round and never gets one - see
                // CommentaryReaders.AgainstPractice.
                _rounds is null
                    ? null
                    : CommentaryReaders.AgainstPractice(
                        round.AssignedTransponders,
                        round.ClassName,
                        _profiles,
                        _sessionHistory,
                        _rounds),

                // AND THE LAYOUT'S OWN HISTORY (11 Oct 2026): who has been
                // winning on this piece of tarmac, and what each driver on
                // this grid has done round it before tonight. The outright
                // record above belongs to one driver and the room hears it
                // as somebody else's news; a driver's own best here belongs
                // to whoever is being talked about, and the regular who
                // finishes eighth has one too.
                //
                // NO _rounds GUARD, unlike the field, the pace and the
                // practice figures. A layout is not a class: what has been
                // run on this tarmac is a fact about the tarmac, and a club
                // with no programme at all has it. That is deliberate -
                // it is the one depth fact a club gets on its first night
                // with HeatCaller and no Program tab filled in.
                //
                // Read here with the rest of the setting for the same
                // UI-thread reason as everything above it: from the
                // Task.Run below onwards this is on a pool thread, and
                // these stores belong to the UI thread.
                CommentaryReaders.LayoutStory(
                    _sessionHistory,
                    _profiles,
                    layoutName,
                    round.AssignedTransponders),

                // AND WHAT THE TITLE STILL HAS RIDING ON IT (12 Oct 2026):
                // the rounds that remain and who can still win it. The
                // richest seam the depth plan named and the one it listed
                // first; it sat blocked for two days for want of a single
                // figure - how long a season is - which nothing in this app
                // could derive and a club had to type. See
                // Season.PlannedRounds.
                //
                // Read here with the rest of the setting for the UI-thread
                // reason every reader above gives, and it is the most
                // expensive of them: the proof recomputes the whole season
                // once per rival who might still catch the leader, through
                // SeasonStore.ComputeStandings, because the drop rule means
                // the gap and the points available cannot answer it. The
                // reader's own ceilings - a table of forty, and giving up
                // the moment more than three drivers are still in it - are
                // what keep that off a race night's critical path.
                //
                // No _rounds guard, like the conditional: the reader takes
                // the round store and answers null without one.
                CommentaryReaders.TitleOnTheLine(
                    round,
                    round.AssignedTransponders,
                    _seasons,
                    _rounds,
                    _events,
                    _sessionHistory,
                    _profiles));
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Round intro goes without the history and the standings - they could not be worked out: {ex.Message}");
        }

        return (clubNight, form, stake, scene);
    }


    /// <summary>
    /// Speaks an LLM-written "coming up" line for a round about to start.
    /// Called from the Stage button, straight after the race is staged.
    ///
    /// Counted as pending audio from the moment the request goes out (see
    /// AnnouncementService.BeginLineBeingWritten), so the staging chain
    /// waits for it rather than starting the countdown while the model is
    /// still thinking. Spoken only if the race is still staging, and still
    /// the race it was written for, when the answer arrives - an intro that
    /// lands in the countdown, or after a reset, is dropped.
    /// </summary>
    /// <param name="stagedGeneration">The session the intro is for - _race.SessionGeneration straight after Stage.</param>
    /// <returns>True if an intro was asked for, so the caller knows to wait for it.</returns>
    private bool SpeakRoundIntro(int stagedGeneration)
    {
        if (_commentary is null || !_commentary.IsAvailable || !_settings.CommentaryRaceIntros)
        {
            return false;
        }

        var round = ShowRound();
        if (round is null)
        {
            return false;
        }

        // Intros kept for the races that earn one, when the Commentary page
        // says so (27 Sep 2026): a main, or a race with a known rivalry on
        // its grid. Said, so an intro that doesn't come isn't taken for the
        // model having failed.
        if (_settings.CommentaryIntrosMainsOrRivalriesOnly
            && round.Kind != RoundKind.Main
            && !_commentary.HasRivalryAmong(round.AssignedTransponders))
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] No round intro for {round.RoundLabel} - intros are set to mains and rivalries only, "
                + "and this race is neither.");
            return false;
        }

        // Read in one pass, on this thread - see ReadIntroFacts.
        var (clubNight, form, stake, scene) = ReadIntroFacts(round);

        // SAID EITHER WAY. The reader refuses far more often than it
        // answers - a season with no planned length, a title four drivers
        // can still win - and "it found nothing" and "it never ran" look
        // identical in a race-night log unless one of them says so.
        if (scene?.Stakes is { } riding)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Round intro has what {riding.SeasonName} has riding on it: "
                + $"{Plural.Of(riding.RoundsLeft, "round")} left including this one, "
                + (riding.CannotBeCaught
                    ? $"and {riding.LeaderName} cannot be caught."
                    : $"and {Plural.Of(riding.StillInIt.Count, "driver")} can still win it."));
        }
        else
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Round intro goes without the title's stakes - no championship "
                + "this race counts toward has a planned number of rounds set, or too many drivers can still win it "
                + "for that to be worth saying. Set the rounds on the season's own rules to hear it.");
        }

        if (scene?.Venue is { } venueRecord)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Round intro has the {venueRecord.LayoutName} lap record: "
                + $"{venueRecord.LapSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}s, "
                + $"{venueRecord.Holder}, {venueRecord.SetDescribed}.");
        }

        // AND WHAT THE LAYOUT'S OWN HISTORY CAME TO. Said either way, which
        // is the lesson the colour segment cost three rounds of testing to
        // learn: "the reader found nothing" and "the reader never ran" look
        // identical in a race-night log unless one of them says so.
        if (scene?.Legend is { } legend)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Round intro has {legend.LayoutName}'s own history: "
                + $"{Plural.Of(legend.RacesOnLayout, "race")} filed on it, "
                + (legend.WinnerRun > 0
                    ? $"the last {legend.WinnerRun} of them to {legend.WinnerName}, "
                    : "no run of wins on it, ")
                + $"and {Plural.Of(legend.Bests.Count, "driver")} on this grid with a best lap here worth stating.");
        }
        else
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Round intro has no layout history - nothing filed on "
                + "this layout before tonight, or no layout named for the race.");
        }

        if (stake is not null)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Round intro has what this race is worth in {stake.SeasonName}: "
                + string.Join(", ", stake.OnThisGrid.Select(r => $"P{r.PositionNow} {r.Name}")));
        }

        // SAID IN THE LOG EVERY TIME IT IS CARRIED, because this is the one
        // claim in the intro about a race that has not been run - so when a
        // club brings a night's console log back and asks why the announcer
        // said what it said, the condition it was proved for is in the file
        // beside the sentence. The margin too, or "no margin" where the
        // reader could not prove one was the same whatever the rest of the
        // field did. The silence is not logged: this reader answers null far
        // more often than not and a line for every refusal would bury the
        // night's real messages.
        if (scene?.Conditional is { } decides)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Round intro has a {decides.SeasonName} conditional: "
                + $"{decides.WinnerName} first and {decides.SecondName} second takes the lead"
                + (decides.DisplacedName.Length > 0 ? $" from {decides.DisplacedName}" : string.Empty)
                + (decides.LeadBy is { } margin
                    ? $", by {margin.ToString(System.Globalization.CultureInfo.InvariantCulture)}."
                    : " - no margin that holds for every other finishing order, so none is said.")
                + $" Proved over {Plural.Of(decides.RoundsCounted, "counted round")}.");
        }

        // Released however this ends - spoken, dropped, failed - and in any
        // case after the budget plus a margin, or when a countdown starts.
        //
        // And settles _introQueued when it is (17 Oct 2026), which is how
        // the Team Principal before a race knows to follow the intro rather
        // than go out in front of it. Every way out of the job below
        // disposes this, so it always settles.
        var introSettled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _introQueued = introSettled.Task;

        var writing = new SettlesWhenDisposed(
            _announcer.BeginLineBeingWritten(RoundIntroModelBudget + TimeSpan.FromSeconds(10)), introSettled);

        // The show's music, if this club scores its intros. Opened inside
        // the job below, so the opener plays ACROSS the wait for the model
        // rather than adding to it.
        //
        // IT CANNOT HOLD A START UP. The staging chain waits on the
        // announcer's own audio (ChainAfterAudio), and the bed plays
        // through AudioBus, which is not that - so a bed still fading
        // never delays a countdown. What it must not do is play OVER one,
        // which is what the cut below is for.
        //
        // A cut can be asked for before StartAsync has handed the run over,
        // so the show's lifetime is kept straight in ShowLifetime rather
        // than in a latch here - see its remarks for the bug that cost the
        // club its music for a whole evening.
        var showLife = new ShowLifetime();

        // The countdown has started - or the race has been reset, or moved
        // on in any way. The bed goes at once rather than playing its own
        // ending over the beeps and the horn.
        Action<RaceState>? onLeftStaging = null;

        async Task EndShowAsync(bool cutShort)
        {
            // Taken off exactly once however many calls are in here at a
            // time, and before the end below - which does nothing at all
            // when the show is still opening, and must not leave the
            // watcher attached for the rest of the night on the way past.
            var watcher = Interlocked.Exchange(ref onLeftStaging, null);

            if (watcher is not null)
            {
                _race.StateChanged -= watcher;
            }

            await showLife.EndAsync(cutShort);
        }

        _ = Task.Run(async () =>
        {
            string line;

            // Which of the club's own history facts this intro's prompt
            // carried, marked as spent only once the line has been queued
            // to the announcer - see the recap's own copy of this above,
            // and CommentaryService.HistorySaid. An intro that arrives
            // after the race has left staging is dropped a few lines
            // below, and a dropped intro must cost the night nothing.
            PackedFacts? introHistory = null;

            try
            {
                if (ShowMusic?.Invoke() is { } treatment && treatment.Treats(SegmentKind.RaceIntro))
                {
                    // Watched BEFORE the show opens: opening it waits out
                    // the opener, and a countdown started during that must
                    // still take the bed away.
                    onLeftStaging = state =>
                    {
                        if (state != RaceState.Staging)
                        {
                            _ = EndShowAsync(cutShort: true);
                        }
                    };

                    _race.StateChanged += onLeftStaging;

                    // The state as well as the lifetime: a show already
                    // called off - the race left staging before the opener
                    // was even asked for - should not be opened at all.
                    if (_race.State == RaceState.Staging && !showLife.CutAlreadyWanted)
                    {
                        // True when a cut was asked for while this was
                        // opening, which makes ending the show OURS: the
                        // watcher could not do it, because the run it
                        // needed had not been handed over yet.
                        // SegmentKind.RaceIntro - see the recap's call above.
                        var cutDuringOpen = showLife.Opened(await treatment.StartAsync(SegmentKind.RaceIntro));

                        // That, or it left staging while the opener was
                        // landing.
                        if (cutDuringOpen || _race.State != RaceState.Staging)
                        {
                            await EndShowAsync(cutShort: true);
                        }
                    }
                }

                using var budget = new CancellationTokenSource(RoundIntroModelBudget);

                // Set when the intro was put right or thrown out after it
                // came back (CommentaryService.TidyIntro), which has already
                // said so - so an intro it dropped is not then reported as
                // one that never arrived.
                var tidied = false;

                // WRITTEN AHEAD? If this round's intro was written when
                // the last result was filed and is still true, the model is
                // not asked at all and the start waits for nothing.
                //
                // EVERYTHING ELSE ON THIS PATH IS UNCHANGED, deliberately.
                // The show's music, the staging watcher, the cut, the
                // pending-audio handle and the UI-thread hop below all run
                // exactly as they do for a line written here and now - the
                // only difference is where the words came from. That is
                // what keeps this out of the lifetime logic that cost
                // rehearsal 1 its countdown twice.
                //
                // The held pack goes into introHistory, which is the same
                // local the model's own callback fills, so the facts are
                // marked spent where they always were: once the line has
                // actually been queued to the announcer, and never if it is
                // dropped. See WrittenAhead.TakeFor.
                var ready = _writtenAhead.TakeFor(
                    round.Id, round.AssignedTransponders, _commentary.HistorySaid, DateTime.Now);

                string? answer;

                if (ready.Why == WrittenAhead.Why.Used && ready.Line is { Length: > 0 } already)
                {
                    introHistory = ready.Facts;
                    answer = already;

                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] Round intro was written ahead, so the start waits "
                        + "for no model.");
                }
                else
                {
                    // Said whenever there WAS something and it went, so a
                    // club can see the write-ahead working rather than
                    // wondering why it never seems to. Nothing is said when
                    // nothing had been written - that is the ordinary case
                    // for the first race of a night and a line every time
                    // would bury the night's real messages.
                    if (ready.Why != WrittenAhead.Why.NothingHeld)
                    {
                        StatusMessage?.Invoke(
                            $"{AppTime.LogStampNow()}  [commentary] The intro written ahead was thrown away - "
                            + $"{WrittenAhead.Because(ready.Why)}. Writing a fresh one.");
                    }

                    answer = await _commentary.BuildRoundIntroAsync(
                        round,
                        budget.Token,
                        log: note =>
                        {
                            tidied = true;
                            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {note}");
                        },
                        clubNight: clubNight,
                        form: form,
                        stake: stake,
                        historyPacked: packed => introHistory = packed,
                        scene: scene);
                }

                if (string.IsNullOrWhiteSpace(answer))
                {
                    StatusMessage?.Invoke(
                        tidied ? $"{AppTime.LogStampNow()}  [commentary] Round intro skipped - see the line above."
                        : budget.IsCancellationRequested ? $"{AppTime.LogStampNow()}  [commentary] Round intro skipped - the model took longer than {RoundIntroModelBudget.TotalSeconds:0}s, so the start went on without it."
                        : $"{AppTime.LogStampNow()}  [commentary] Round intro skipped - no line came back.");
                    writing.Dispose();

                    // Nothing is going to be said over it.
                    await EndShowAsync(cutShort: true);
                    return;
                }

                line = answer;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Round intro skipped - {ex.Message}");
                writing.Dispose();

                await EndShowAsync(cutShort: true);
                return;
            }

            // On the UI thread, where the race's state belongs. Released
            // AFTER SpeakReminder has queued the line, so the audio never
            // looks finished in between.
            try
            {
                if (IsDisposed || Disposing || !IsHandleCreated)
                {
                    writing.Dispose();

                    await EndShowAsync(cutShort: true);
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (IsDisposed || Disposing)
                        {
                            return;
                        }

                        if (_race.State != RaceState.Staging || _race.SessionGeneration != stagedGeneration)
                        {
                            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Round intro dropped - the race had moved on from staging before it was written.");
                            return;
                        }

                        _announcer.SpeakReminder(line);

                        // The last line only now, for the same reason - an
                        // intro written ahead is not recorded when written.
                        _commentary?.IntroSpoken(line);

                        // Spent only now: the line is queued and the room
                        // is about to hear it. The return just above - the
                        // race having moved on from staging - leaves the
                        // facts unmarked, so the next intro for these
                        // drivers still has them.
                        _commentary?.HistorySpoken(introHistory);
                    }
                    finally
                    {
                        writing.Dispose();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // The window handle went away between the check and the
                // post - the page is closing.
                writing.Dispose();
            }

            // The line is posted. The marker above keeps the announcer
            // counted as busy until it has been queued, so this waits
            // through the writing, the queueing AND the reading of it -
            // and returns on the quiet afterwards, which is when the
            // show is over.
            //
            // Normally the countdown has already taken the bed away by
            // then (see onLeftStaging). When it has not - auto-advance
            // off, the race director not yet ready - this ends it on the
            // piece's own fade, into the gap before the start.
            try
            {
                await _announcer.WaitForAudioAsync(IntroShowLimit, CancellationToken.None);
            }
            catch
            {
                // Decoration. Never an error on the page that starts races.
            }

            await EndShowAsync(cutShort: false);
        });

        return true;
    }

    // ==================== The Team Principal before a race (17 Oct 2026) ====================

    /// <summary>Settles once this race's round intro has been queued or given up on - see SpeakRoundIntro.</summary>
    private Task _introQueued = Task.CompletedTask;

    /// <summary>The drivers whose Team Principal has been heard before a race tonight - each once a night.</summary>
    private readonly HashSet<string> _principalHeard = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The night _principalHeard is for; a new one starts it again.</summary>
    private string? _principalNight;

    /// <summary>How long the model gets to write the Team Principal's interview before a race. The countdown waits for it.</summary>
    private static readonly TimeSpan PrincipalModelBudget = TimeSpan.FromSeconds(25);

    /// <summary>
    /// How long the staging chain waits for audio when the Team Principal is
    /// coming: the model's budget, the intro, and four lines of interview.
    /// </summary>
    private static readonly TimeSpan StagingWaitWithPrincipal = TimeSpan.FromSeconds(150);

    /// <summary>How far behind its timetable the night may be running before the interview is left out.</summary>
    private const int PrincipalLateMinutes = 10;

    /// <summary>A handle that also settles a task when it is disposed - see SpeakRoundIntro.</summary>
    private sealed class SettlesWhenDisposed : IDisposable
    {
        private readonly IDisposable _inner;
        private readonly TaskCompletionSource _settled;

        public SettlesWhenDisposed(IDisposable inner, TaskCompletionSource settled)
        {
            _inner = inner;
            _settled = settled;
        }

        public void Dispose()
        {
            _inner.Dispose();
            _settled.TrySetResult();
        }
    }

    /// <summary>
    /// The key for "tonight" in the Team Principal's memory: the race day's
    /// id when one is filed, otherwise the race night itself - 6am to 6am
    /// (TonightAtTheTrack.NightOf), so a night that runs past midnight is
    /// still one night. Bug hunt 10 (23 Oct 2026): shared so the pre-race
    /// "once a night" list and PrincipalNightBrief cannot disagree again.
    /// </summary>
    internal static string PrincipalNightKey(string? activeEventId, DateTime now)
        => string.IsNullOrWhiteSpace(activeEventId)
            ? TonightAtTheTrack.NightOf(now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : activeEventId;

    /// <summary>
    /// Who the Team Principal before <paramref name="round"/> speaks for, and
    /// what their interview is written from - shared by Stage and by the
    /// write-ahead, so both pick the same driver. Null when the Cast tab has
    /// it switched off for this kind of race, or there is nobody left to ask
    /// tonight (said in the log unless <paramref name="quiet"/>).
    ///
    /// On the UI thread: it reads the stores, as ReadIntroFacts does.
    /// </summary>
    private (string Focus, string? Kind, ChampionshipAtStake? Stake)? ChoosePrincipal(RoundDefinition round, bool quiet)
    {
        if (_commentary is null || (round.Kind != RoundKind.Main && !_settings.PrincipalInHeats))
        {
            return null;
        }

        // WHY (23 Oct 2026, bug hunt 10): with no race day filed this was keyed
        // on the calendar date, so the "each once a night" list cleared at
        // midnight and a night racing past 12 replayed (and paid again for)
        // Team Principal interviews already heard. It now uses the same
        // 6am-to-6am night as PrincipalNightBrief, and a blank event id
        // counts as no race day, as it does there.
        var night = PrincipalNightKey(_events?.Active?.Id, DateTime.Now);

        if (!string.Equals(_principalNight, night, StringComparison.Ordinal))
        {
            _principalHeard.Clear();
            _principalNight = night;
        }

        var (clubNight, _, stake, _) = ReadIntroFacts(round);

        var grid = round.AssignedTransponders.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        var commentary = _commentary;

        bool HasTeam(string id) => !string.IsNullOrWhiteSpace(_profiles.Get(id)?.Team);

        bool InRivalry(string id) => grid.Any(other =>
            !string.Equals(other, id, StringComparison.OrdinalIgnoreCase)
            && commentary.HasRivalryAmong(new[] { id, other }));

        int? Standing(string id) =>
            stake?.OnThisGrid.FirstOrDefault(r => string.Equals(r.DriverId, id, StringComparison.OrdinalIgnoreCase)) is { PositionNow: > 0 } row
                ? row.PositionNow
                : null;

        var focus = CommentaryService.PickPrincipalFocus(
            grid, HasTeam, InRivalry, Standing, _settings.PrincipalTeamsOnly, _principalHeard);

        if (focus is null)
        {
            if (!quiet)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] No Team Principal before {round.RoundLabel} - "
                    + (_settings.PrincipalTeamsOnly
                        ? "nobody on the grid with a team on their profile is left to hear from tonight."
                        : "every driver on the grid has had theirs heard already tonight."));
            }

            return null;
        }

        return (focus, CommentaryService.DescribeKind(round, clubNight), stake);
    }

    /// <summary>The Team Principal's pre-race interview, written ahead for one round - see WriteThePrincipalAhead.</summary>
    private sealed record PrincipalAhead(
        string RoundId,
        string Grid,
        string Focus,
        string Name,
        string? Interviewer,
        int AsOf,
        IReadOnlyList<CommentaryService.InterviewExchange> Exchanges);

    /// <summary>Guards the held interview and its version: written from a pool thread, taken on the UI thread.</summary>
    private readonly object _principalAheadLock = new();

    /// <summary>The interview written ahead for the next round, or null.</summary>
    private PrincipalAhead? _principalAhead;

    /// <summary>Moved on by every result filed or corrected, so an interview written before it is never used after.</summary>
    private int _principalAheadVersion;

    /// <summary>
    /// Writes the NEXT round's Team Principal interview now, so its start
    /// waits for no model - the round intro's write-ahead, for the Team
    /// Principal (17 Oct 2026). Called from WriteTheNextIntroAhead, so at the
    /// same moment and for the same reason: when a result is filed, which is
    /// when the standings it states are true. A result filed or corrected
    /// since throws it away, and so does a change of grid or driver.
    ///
    /// Best-effort like the intro's: never awaited, holding no start up,
    /// and a failure costs the night nothing - Stage writes its own as before.
    /// </summary>
    /// <param name="gate">Waited for before the model is asked - see _flagLinesAsked.</param>
    private void WriteThePrincipalAhead(SessionRecord? filed, bool corrected, Task gate)
    {
        int asOf;

        lock (_principalAheadLock)
        {
            asOf = ++_principalAheadVersion;
            _principalAhead = null;
        }

        // As the intro's: a correction during a race is written for by the
        // filing that follows it.
        if (corrected && _race.State is (RaceState.Staging or RaceState.Countdown or RaceState.Racing))
        {
            return;
        }

        if (!_settings.PrincipalBeforeRace || _commentary is null || !_commentary.IsAvailable)
        {
            return;
        }

        if (NextRound(filed?.RoundId) is not { } next || string.IsNullOrWhiteSpace(next.Id))
        {
            return;
        }

        if (ChoosePrincipal(next, quiet: true) is not { } chosen)
        {
            return;
        }

        var (focus, kind, stake) = chosen;
        var interviewer = InterviewerName;
        var grid = WrittenAhead.Fingerprint(next.AssignedTransponders);
        var name = (_settings.PrincipalName ?? string.Empty).Trim();
        var label = next.RoundLabel;
        CommentaryService commentary = _commentary;

        // READ HERE, BEFORE THE TASK. WriteThePrincipalAhead is called on
        // the UI thread when a result is filed, and the stores belong to
        // it - a reader called from inside the Task.Run below would be
        // walking the history while the next race writes to it. The same
        // arrangement every other write-ahead on this page uses: read on
        // the thread that owns the stores, hand the model finished
        // sentences.
        var standing = stake?.OnThisGrid
            .Where(row => string.Equals(row.DriverId, focus, StringComparison.OrdinalIgnoreCase))
            .Select(row => (int?)row.PositionNow)
            .FirstOrDefault();

        var (nightFacts, stance) = PrincipalNightBrief(focus, next.ClassName, standing);
        var brief = PrincipalStance.BriefFor(stance);

        _ = Task.Run(async () =>
        {
            // Behind the flag lines, before the budget (20 Oct 2026) - see _flagLinesAsked.
            await WaitForFlagLinesAsync(gate);

            try
            {
                using var budget = new CancellationTokenSource(WriteAheadModelBudget);

                var exchanges = await commentary.BuildPrincipalBeforeAsync(
                    next, focus, name, kind, stake, log: null, ct: budget.Token, interviewer: interviewer,
                    nightFacts: nightFacts, stanceBrief: brief);

                if (exchanges is null)
                {
                    // Not said: Stage asks again, so nothing was lost.
                    return;
                }

                lock (_principalAheadLock)
                {
                    if (_principalAheadVersion != asOf)
                    {
                        return;
                    }

                    _principalAhead = new PrincipalAhead(next.Id, grid, focus, name, interviewer, asOf, exchanges);
                }

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Wrote the Team Principal's interview for {label} ahead of "
                    + "time - its start will wait for no model.");
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Could not write the Team Principal's interview for {label} ahead: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// A word with a Team Principal before the race just staged, for the
    /// driver on its grid with most riding on it (CommentaryService.
    /// PickPrincipalFocus): two questions and two answers, spoken after the
    /// round intro and before the countdown, which waits for them.
    ///
    /// Left out - and said in the log - when the night is running ten
    /// minutes or more behind its timetable, when every driver's principal
    /// has already been heard tonight, or when the model is too slow. Spoken
    /// only if the race is still staging, and still the race it was written
    /// for, when it is ready.
    /// </summary>
    /// <param name="stagedGeneration">The session the interview is for - _race.SessionGeneration straight after Stage.</param>
    /// <param name="introComing">A round intro is being written for this race too, and goes first.</param>
    /// <returns>True if an interview was asked for, so the caller knows to wait for it.</returns>
    private bool SpeakPrincipalBefore(int stagedGeneration, bool introComing)
    {
        _principalQueued = Task.CompletedTask;

        // Already said in On Air's opening, rendered with it (18 Oct 2026) -
        // see WriteOpeningAsync. Taken first, so it is used up whatever
        // happens below.
        if (TakePrincipalSaidByOnAir())
        {
            return false;
        }

        if (_race.State != RaceState.Staging)
        {
            return false;
        }

        if (PreparePrincipal(ShowRound()) is not { } job)
        {
            return false;
        }

        // After the round intro, so the room hears the race set up first and
        // then the team - and the driver introductions now come AFTER this
        // (18 Oct 2026), waiting on _principalQueued.
        var introQueued = introComing ? _introQueued : Task.CompletedTask;

        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _principalQueued = settled.Task;

        // Held from now, so the staging chain waits for the interview - and
        // released however this ends, and in any case after the budget, the
        // renders and the intro, or when a countdown starts.
        var writing = new SettlesWhenDisposed(
            _announcer.BeginLineBeingWritten(PrincipalModelBudget + TimeSpan.FromSeconds(60)),
            settled);

        _ = Task.Run(async () =>
        {
            List<SpokenLine>? lines = null;

            try
            {
                lines = await WritePrincipalLinesAsync(job, CancellationToken.None);

                if (lines is not null)
                {
                    await Task.WhenAny(introQueued, Task.Delay(RoundIntroModelBudget + TimeSpan.FromSeconds(10)));
                }
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Team Principal skipped - {ex.Message}");
                lines = null;
            }

            if (lines is null || lines.Count == 0 || IsDisposed || Disposing || !IsHandleCreated)
            {
                writing.Dispose();
                return;
            }

            try
            {
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (IsDisposed || Disposing)
                        {
                            return;
                        }

                        if (_race.State != RaceState.Staging || _race.SessionGeneration != stagedGeneration)
                        {
                            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Team Principal dropped - the race had moved on from staging before it was ready.");
                            return;
                        }

                        SayPrincipalLines(lines, job.Focus);
                    }
                    finally
                    {
                        writing.Dispose();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // The window handle went away between the check and the post.
                writing.Dispose();
            }
        });

        return true;
    }

    /// <summary>Settles once this race's pre-race Team Principal has been queued or given up on - the driver introductions follow it.</summary>
    private Task _principalQueued = Task.CompletedTask;

    /// <summary>
    /// Set when On Air's opening carried the pre-race Team Principal (18 Oct
    /// 2026), so the Stage it hands over to does not ask again. Cleared
    /// wherever On Air's own "opening said" is.
    /// </summary>
    private bool _principalSaidByOnAir;

    /// <summary>Takes On Air's word that the Team Principal has been heard - see <see cref="_principalSaidByOnAir"/>.</summary>
    private bool TakePrincipalSaidByOnAir()
    {
        var said = _principalSaidByOnAir;
        _principalSaidByOnAir = false;
        return said;
    }

    /// <summary>Everything the pre-race Team Principal is written and spoken from, read on the UI thread.</summary>
    private sealed record PrincipalJob(
        RoundDefinition Round,
        string Focus,
        string? Kind,
        ChampionshipAtStake? Stake,
        string Name,
        IReadOnlyList<CommentaryService.InterviewExchange>? Ready,
        SecondVoice? Voice,
        bool CoHostAsks,
        string SpokenFocus,
        string? Interviewer,
        SecondVoice CoHostVoice,
        string CoHostName,
        CommentaryService Commentary,

        // THE PRINCIPAL'S OWN WELL (18 Oct 2026), read with the rest of
        // the job on the UI thread - see PrincipalNightBrief. Empty facts
        // and the plain stance is what a club's first heat of the evening
        // gets, and the prompt copes with it.
        IReadOnlyList<FactBlock> NightFacts,
        PrincipalStance.Stance Stance,

        // WHY (26 Oct 2026, asked for by Asher): the race announcer's name,
        // read on the UI thread with the rest, for the co-host's hand back
        // to him by name - see WritePrincipalLinesAsync.
        string AnnouncerName = "");

    /// <summary>
    /// Whether a Team Principal is due before <paramref name="round"/>, and
    /// everything it is written from - including an interview written ahead,
    /// taken (used once) when still true. Null, with the reason said, when
    /// none is due. On the UI thread: it reads the stores.
    /// </summary>
    private PrincipalJob? PreparePrincipal(RoundDefinition? round)
    {
        if (!_settings.PrincipalBeforeRace || _commentary is null || !_commentary.IsAvailable)
        {
            return null;
        }

        if (round is null)
        {
            // Said, as the other reasons are: nothing else told the club why
            // the interview never came.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] No Team Principal - this race is not tied to a round on the program, "
                + "so there is no grid to pick a team from.");
            return null;
        }

        if (_daySchedule?.MinutesLate() is { } late && late >= PrincipalLateMinutes)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] No Team Principal before {round.RoundLabel} - the night is running "
                + $"{late} minutes behind, so the start goes on without it.");
            return null;
        }

        if (ChoosePrincipal(round, quiet: false) is not { } chosen)
        {
            return null;
        }

        var (focus, kind, stake) = chosen;
        var name = (_settings.PrincipalName ?? string.Empty).Trim();
        var interviewer = InterviewerName;

        // WRITTEN AHEAD? Taken whatever happens - a held interview is for
        // one staging - and used only when it is still true: the same round,
        // the same grid, the same driver, the same name and asker, and no
        // result filed or corrected since it was written. See
        // WriteThePrincipalAhead.
        PrincipalAhead? held;
        bool fresh;

        lock (_principalAheadLock)
        {
            held = _principalAhead;
            _principalAhead = null;
            fresh = held is not null && held.AsOf == _principalAheadVersion;
        }

        var ready = held is not null
                    && fresh
                    && string.Equals(held.RoundId, round.Id, StringComparison.Ordinal)
                    && string.Equals(held.Grid, WrittenAhead.Fingerprint(round.AssignedTransponders), StringComparison.Ordinal)
                    && string.Equals(held.Focus, focus, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(held.Name, name, StringComparison.Ordinal)
                    && string.Equals(held.Interviewer, interviewer, StringComparison.Ordinal)
            ? held.Exchanges
            : null;

        if (ready is not null)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] The Team Principal's interview was written ahead, so it waits for no model.");
        }
        else if (held is not null)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] The Team Principal's interview written ahead was thrown away - "
                + "the race, its grid, the driver or the results have changed since. Writing a fresh one.");
        }

        var engine = _announcer.VoiceEngineInUse;
        var voices = _settings.PrincipalVoice;
        SecondVoice? voice = voices.IsSetFor(engine) ? voices : null;
        var driver = _profiles.Get(focus)?.DriverName is { Length: > 0 } known ? known : focus;

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [commentary] Before {round.RoundLabel}: a word with {(name.Length > 0 ? name : "the Team Principal")} "
            + $"for {driver}"
            + (voice is not null ? "." : $", in the announcer's own voice - no Team Principal's voice is picked for {EngineName(engine)} on Voice > Cast."));

        // THE TABLE POSITION BY ID HERE, not by name: a
        // ChampionshipStanding carries the driver it is about, so the
        // ambiguity guard PrincipalTablePosition needs for a
        // ChampionshipMove is not needed on this path.
        var standing = stake?.OnThisGrid
            .Where(row => string.Equals(row.DriverId, focus, StringComparison.OrdinalIgnoreCase))
            .Select(row => (int?)row.PositionNow)
            .FirstOrDefault();

        var (nightFacts, stance) = PrincipalNightBrief(focus, round.ClassName, standing);

        return new PrincipalJob(
            round,
            focus,
            kind,
            stake,
            name,
            ready,
            voice,
            CoHostInterviews,
            SpokenDriverName(focus),
            interviewer,
            _settings.CoHostVoice,
            (_settings.CommentaryCoHostName ?? string.Empty).Trim(),
            _commentary,
            nightFacts,
            stance,
            (_settings.RecapAnnouncerName ?? string.Empty).Trim());
    }

    /// <summary>
    /// The pre-race Team Principal's lines, written (unless written ahead)
    /// and rendered: the announcer's throw to the co-host first when the
    /// co-host asks, then the questions and answers. Null when nothing
    /// usable came back in <see cref="PrincipalModelBudget"/>, which is said.
    /// Off the UI thread.
    /// </summary>
    private async Task<List<SpokenLine>?> WritePrincipalLinesAsync(PrincipalJob job, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(PrincipalModelBudget);

        // WHY (26 Oct 2026, asked for by Asher: "When the show host throws it
        // to other cast member it sounds robotic. reference the show host name
        // when sending it back. add in a line or two. make it AI and
        // prerender."): the announcer's throw down to pit lane and - new - the
        // co-host's hand back to him by name, written by the AI in the same
        // budget as the interview and voiced while the interview is, and used
        // only if it is in hand once the interview has been rendered - the
        // built-in pair goes otherwise. The INTERVIEW IS ASKED FOR FIRST and
        // held (26 Oct 2026, review), so on the model on this PC - which
        // answers one request at a time - the interview gets the model first
        // and it is the hand-over that waits, as at the flag.
        var exchangesTask = job.Ready is { } ready
            ? Task.FromResult<IReadOnlyList<CommentaryService.InterviewExchange>?>(ready)
            : job.Commentary.BuildPrincipalBeforeAsync(
                job.Round, job.Focus, job.Name, job.Kind, job.Stake,
                log: note => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {note}"),
                ct: budget.Token,
                interviewer: job.Interviewer,
                nightFacts: job.NightFacts,
                stanceBrief: PrincipalStance.BriefFor(job.Stance));

        var handOff = job.CoHostAsks
            ? StartHandOff(
                new HandOff.Brief(
                    Host: job.AnnouncerName,
                    HostRole: "the race announcer, up in the booth, before the start",
                    Guest: job.CoHostName,
                    GuestRole: "the co-host, down in pit lane",
                    About: $"a word with {(job.Name.Length > 0 ? job.Name + ", " : string.Empty)}the Team Principal from "
                           + (job.SpokenFocus.Length > 0 ? $"{job.SpokenFocus}'s team" : "one of the teams") + ", before the race",
                    ThrowMustSay: job.SpokenFocus.Length > 0
                        ? $"that {(job.CoHostName.Length > 0 ? job.CoHostName : "the co-host")} is with {job.SpokenFocus}'s team"
                        : null,
                    GuestCovers: "how the team sees the race ahead",
                    Next: "the race announcer takes the room on to the start"),
                null, null, job.CoHostVoice, null, budget.Token)
            : null;

        var exchanges = await exchangesTask;

        if (exchanges is null)
        {
            StatusMessage?.Invoke(budget.IsCancellationRequested
                ? $"{AppTime.LogStampNow()}  [commentary] Team Principal skipped - the model took longer than {PrincipalModelBudget.TotalSeconds:0}s, so the start went on without it."
                : $"{AppTime.LogStampNow()}  [commentary] Team Principal skipped - nothing usable came back from the model. Any line above says why.");

            // No interview, so no hand-over either - see the end of this method.
            budget.Cancel();
            return null;
        }

        // The questions in the co-host's voice when the club has one, thrown
        // to by the race announcer first (18 Oct 2026); otherwise in the race
        // announcer's own - this is the start of a race, not the recap show.
        var lines = await RenderExchangesAsync(
            exchanges, job.CoHostAsks ? job.CoHostVoice : null, null, job.Voice, ct);

        if (lines is not null && job.CoHostAsks)
        {
            // The AI's pair if it is in hand by now (see handOff), each
            // half on its own: a half it did not manage is the built-in line,
            // rendered here before anything is spoken.
            var written = await HandOffInTimeAsync(handOff, "Pit lane before the race", ct);

            var throwLine = written?.Throw;

            if (throwLine is null)
            {
                var thrown = PitLaneThrowBefore(job.CoHostName, job.SpokenFocus);
                throwLine = new SpokenLine(thrown, null, null, await _announcer.PrepareReminderAsync(thrown));
            }

            var backLine = written?.Back;

            if (backLine is null)
            {
                var back = HandOff.PitLaneBackBefore(job.AnnouncerName, job.SpokenFocus);
                backLine = new SpokenLine(back, job.CoHostVoice, null, await _announcer.PrepareReminderAsync(back, null, job.CoHostVoice));
            }

            lines.Insert(0, throwLine);
            lines.Add(backLine);
        }

        // A hand-over still being written or voiced is not wanted any more -
        // stopped here, before the budget it runs on is disposed.
        if (handOff is { Voiced.IsCompleted: false })
        {
            budget.Cancel();
        }

        return lines;
    }

    /// <summary>Queues the pre-race Team Principal's lines back to back, and marks the driver's principal heard tonight. On the UI thread.</summary>
    private void SayPrincipalLines(IReadOnlyList<SpokenLine> lines, string focus)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var run = CommentaryExchange.ExchangeRun.At(i, lines.Count);

            Say(
                lines[i],
                run.AnotherFollows
                    ? AnnouncementService.SpeechTail.BackToBack
                    : AnnouncementService.SpeechTail.Normal,
                run.FollowsAnother
                    ? AnnouncementService.SpeechLead.FollowsOn
                    : AnnouncementService.SpeechLead.Normal,
                OnAirParts.PitLane);
        }

        _principalHeard.Add(focus);
    }

    /// <summary>
    /// Files the race that has just finished, through the app's filing
    /// service (RaceFilingService, owned by MainForm).
    ///
    /// WHY (23 Oct 2026, timing layer, P2): the filing itself - the order of
    /// the history write, the round link, the journal, the unfiled-race
    /// retries, every message - moved out of this page into RaceFilingService,
    /// verbatim. What it needs from this page is read here, and what this page
    /// does around the filing (the badges, the recap, the recording, the card,
    /// the dialog) is handed over as callbacks that run at the points they
    /// always ran. See RaceFilingInput.
    /// </summary>
    private void SaveCompletedSessionToHistory()
    {
        // Read once and let go on every way out, so whatever runs next is
        // filed (or auto-advanced) as usual - see the auto-advance in
        // RaceFilingService.FileCompletedRace.
        var wasQuickRace = _currentRaceIsQuick;
        _currentRaceIsQuick = false;
        var wasRerun = _currentRaceIsRerun;
        _currentRaceIsRerun = false;

        _filing.FileCompletedRace(new RaceFilingInput
        {
            WasQuickRace = wasQuickRace,
            WasRerun = wasRerun,
            IsDemo = _currentRaceIsDemo,
            TrackLayoutName = _cmbTrackLayout.SelectedItem as string == "(none)" ? string.Empty : (string)(_cmbTrackLayout.SelectedItem ?? string.Empty),

            // The Round box's choice, or the round a restored race was run for.
            TakeRound = () =>
            {
                var round = RoundForThisRace();
                _restoredRoundId = null;
                return round;
            },
            PickNextRound = PickNextRound,
            BuildLabel = BuildSessionLabel,

            ClearRaceSummary = () => _commentary?.ClearRaceSummary(),
            SignalBroadcastOver = SignalSessionAudioFinishedAfterFinishCall,
            DemoOver = () => DemoRound = null,

            NoteGhostsBeaten = ordered =>
            {
                // Badges never stop a save: anything going wrong here means no ghost
                // badges for this race, and the race is filed as ever.
                try
                {
                    // Not a ghost switched on part-way through - see GhostsFromTheGreen.
                    _ghostsBeatenAtFlag = AchievementEvaluator.GhostsBeatenBy(
                        ordered.Select(s => (s.TransponderId, s.Laps, s.FastestLapMs)).ToList(),
                        AchievementEvaluator.GhostsFromTheGreen(
                            AchievementStores?.GhostCars?.Invoke() ?? Array.Empty<GhostCar>(),
                            AchievementStores?.GhostsJoinedLate?.Invoke() ?? Array.Empty<string>()),
                        byBestLap: false);
                }
                catch (Exception ex)
                {
                    _ghostsBeatenAtFlag = new Dictionary<string, IReadOnlyList<GhostCar>>(StringComparer.OrdinalIgnoreCase);
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [achievements] Couldn't work out which ghosts were beaten: {ex.Message}");
                }
            },

            ApplyBlackFlags = ApplyBlackFlags,

            BeforeCommit = (record, racers) =>
            {
                // Handed to the recording, which needs the drivers' names, their
                // finishing positions and their lap times to cut each one's clip -
                // and these are the very rows that go into the race history, not a
                // second account of the race. Before the filing rather than inside
                // it, so a history write that fails still leaves the race with its
                // mp3; only the clips depend on this being here.
                _raceAudioRecord = record;

                // Off the board rather than out of the record, because the saved
                // result does not carry it - see _raceAudioReleaseOffsets. Read here,
                // where these rows are still the ones that ran the race: by the time
                // the recording is written out the board has moved on.
                _raceAudioReleaseOffsets = racers.ToDictionary(
                    s => s.TransponderId,
                    s => s.ReleaseOffsetMs,
                    StringComparer.OrdinalIgnoreCase);

                // The filing whose recap is coming - see WriteTheNextIntroAhead,
                // which the history store calls back in the middle of this.
                _filingForSummary = record.Id;
            },

            Filed = record =>
            {
                // What a Pack picked on a row's right-click is recorded
                // against from now until the next race: this one. Matched
                // by the race's start, so a later race whose save failed is
                // never mistaken for it. Not a demo: its laps are invented.
                //
                // The start as the race finished with it (23 Oct 2026, timing
                // layer, P4b): threaded, the race has moved on since it did -
                // see RaceTiming.DeliveringFinishedRace. Inline, the race's own.
                _filedSession = record.IsDemo ? null : (record.Id, record.Label,
                    _race.DeliveringFinishedRace is { } finished ? finished.RaceStartUtc : _race.RaceStartUtc);

                // Each driver's pack in the car, recorded for them on this
                // race or practice - one picked on a row's right-click during
                // it is already there, and stays. Here, at the flag, rather
                // than on every session added to the history: an Undo that
                // puts back a deleted session from last month must not tag
                // it with tonight's packs. Nor is a race saved late (see
                // RaceFilingService.FileUnfiledRaces) - a pack swapped since
                // would be wrong, and no pack is better than the wrong one.
                BatteryPacks?.AutoTag(record, _profiles);
            },

            NoBadgesForDemo = () =>
            {
                _lastUnlocks = new List<AchievementUnlock>();
                _honours = null;
            },

            AwardAchievements = AwardAchievements,

            SpeakSummary = (record, round) =>
            {
                // A demo is not filed against its round, but the show is
                // still the round's - the Technical Lead after a main, the
                // Team Principal - so the recap is told which it was. See
                // DemoRound.
                SpeakRaceSummary(record, round ?? (record.IsDemoSession() ? DemoRoundFor(record) : null));
                DemoRound = null;
            },

            SaveResultsCard = TrySaveAutoResultsCard,
            SessionSaved = () => SessionSaved?.Invoke(),

            FilingFinished = () =>
            {
                _filingForSummary = null;

                // WHY (23 Oct 2026, bug hunt 10): the Quick Race's stamp outlived
                // it and the next freeform race was saved under it. Let go once
                // this race is filed (its record already carries the name),
                // however the filing went; a name the director typed is kept.
                if (wasQuickRace && QuickRaceStampToRelease(_txtSessionLabel.Text) is not null)
                {
                    _txtSessionLabel.Text = string.Empty;
                }
            },

            ShowNotSaved = (text, caption) => AppDialog.Show(
                this,
                text,
                caption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error),
        });
    }

    /// <summary>
    /// Whether a finished race is still waiting to get into the race
    /// history - and so its crash journal is its only copy on disk, which
    /// nothing may clear. The filing service's answer (RaceFilingService.HasUnfiledRaces).
    /// </summary>
    public bool HasUnfiledRaces => _filing.HasUnfiledRaces;

    /// <summary>
    /// Writes the crash journal now, with the round and the typed race name
    /// the race is being run under (captured from this page first), so a
    /// restore can put those back too - for this page's own writes: a
    /// restore, an undo, the window closing, and a write the journal's
    /// throttle held back.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P2): the journal is written by
    /// MainForm's RaceJournalRecorder now, which the session's events drive
    /// whether or not this page is there - every crossing, flag and
    /// correction. The retry of an earlier unfiled race moved with it
    /// (RaceFilingService.RetryUnfiledBeforeJournal), and the race recording
    /// still follows every write (see the recorder's Written, wired in
    /// WireRaceControl).
    /// </remarks>
    private void WriteJournal(bool force = false)
    {
        _journalRecorder.UseDetails(JournalDetailsNow());
        _journalRecorder.Write(force);
    }

    /// <summary>The track, round, typed name and re-run flag the crash journal records for this race, as they are on this page now.</summary>
    private RaceJournalDetails JournalDetailsNow() =>
        new(SelectedTrackName(), RoundForThisRace()?.Id, _txtSessionLabel.Text, _currentRaceIsRerun);

    /// <summary>
    /// The round a race brought back from the crash journal was being run
    /// for, and the session generation it came back in. The Round box
    /// belongs to the host, which knows nothing of the restore, so after a
    /// crash it is usually empty - and the result was then filed against
    /// whichever round auto-advance guessed. Only honoured while that
    /// restored race is the one on the board.
    /// </summary>
    private string? _restoredRoundId;
    private int _restoredRoundGeneration = -1;

    /// <summary>
    /// The round a race brought back from the crash journal was being run
    /// for, while that race is still the one on the board - so the host can
    /// show it in its Round box. Null when there is none.
    /// </summary>
    /// <remarks>
    /// While a finished race is being filed (threaded mode), its generation
    /// as it finished - see RaceTiming.DeliveringFinishedRace (23 Oct 2026,
    /// timing layer, P4b). Otherwise, and always inline, the race's own.
    /// </remarks>
    public string? RestoredRoundId =>
        _restoredRoundId is { } id
        && _restoredRoundGeneration == (_race.DeliveringFinishedRace?.SessionGeneration ?? _race.SessionGeneration) ? id : null;

    /// <summary>
    /// The round this race is for: the one picked in the Round box, or -
    /// when none is picked - the one a restored race was being run for.
    /// </summary>
    private RoundDefinition? RoundForThisRace()
    {
        if (_activeRound?.Invoke() is { } picked)
        {
            return picked;
        }

        // The finished race's generation while it is being filed, threaded -
        // see RestoredRoundId (23 Oct 2026, timing layer, P4b).
        return _restoredRoundId is { } id
            && _restoredRoundGeneration == (_race.DeliveringFinishedRace?.SessionGeneration ?? _race.SessionGeneration)
            ? _rounds?.Get(id)
            : null;
    }

    /// <summary>The track layout chosen for this race, or empty when none is.</summary>
    private string SelectedTrackName() =>
        _cmbTrackLayout.SelectedItem as string is { } name && name != "(none)" ? name : string.Empty;

    /// <summary>
    /// Offers back a race that was running when the app last stopped.
    ///
    /// Called once, after the window is up. The journal file existing at all
    /// means the last race never reached the history - so if there is one,
    /// something went wrong, and the race is still recoverable.
    /// </summary>
    public void OfferRaceRecovery()
    {
        if (RaceJournal.Unfinished() is not { } entry)
        {
            return;
        }

        if (_race.State != RaceState.Idle)
        {
            // Something is already running - never interrupt that.
            return;
        }

        // A practice is not offered back as a race. Restored here it came
        // back in practice mode with the Practice page not running it - so
        // nothing could stop it, and Race Control refused every start
        // because "practice is running". Set aside rather than deleted, as
        // a declined race is, so the laps are still on disk.
        if (entry.IsPractice)
        {
            RaceJournal.Discard();
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [practice] The app closed during practice - that session was set aside, not restored.");
            return;
        }

        var answer = AppDialog.Show(this,
            entry.Describe()
            + "\r\n\r\nIt never reached the race history, so it can still be recovered. Restore it?"
            + "\r\n\r\nLaps, penalties, positions and the fastest lap come back as they were, and the race "
            + "clock picks up where it stopped rather than counting the time the app was shut."
            + "\r\n\r\nIt comes back STOPPED, under a red flag, so nothing counts while the cars are being "
            + "put back on the track. Throw the green flag when everyone is ready."
            + "\r\n\r\nOne thing to know: each car's first lap after the green is timed from its last "
            + "crossing before the app closed, so it includes the stoppage and looks long. It counts as a "
            + "lap, but never as a best lap."
            + "\r\n\r\nChoosing No keeps a copy on disk either way, so this is not your last chance.",
            "Restore the unfinished race?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
        {
            RaceJournal.Discard();
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Unfinished race set aside, not restored.");
            return;
        }

        try
        {
            _suppressFlagAnnouncements = true;

            try
            {
                RaceJournal.Restore(_race, entry);
            }
            finally
            {
                _suppressFlagAnnouncements = false;
            }

            _sessionStartedAtUtc = entry.SessionStartedAtUtc ?? entry.RaceStartUtc ?? DateTime.UtcNow;
            _previousFlag = _race.Flag;

            // The round and the typed name the race was run under. Null in a
            // journal written before they were stored, which leaves both as
            // they are now.
            _restoredRoundId = string.IsNullOrWhiteSpace(entry.RoundId) ? null : entry.RoundId;
            _restoredRoundGeneration = _race.SessionGeneration;

            if (entry.RaceLabel is { } raceLabel)
            {
                _txtSessionLabel.Text = raceLabel;
            }

            // A Quick Race is still one after a crash. The journal does not
            // say so in as many words, but a race with no round and Quick
            // Race's own "Quick Race HH:mm" name is one - and brought back
            // as any freeform race, auto-advance filed the shakedown
            // against the next heat at the flag.
            _currentRaceIsQuick = _restoredRoundId is null
                && entry.RaceLabel is { } quickLabel
                && IsQuickRaceStamp(quickLabel.Trim());

            // So is a re-run of a recorded round, which nothing else in the
            // journal gives away - see RaceJournal.Entry.IsRerun. Set before
            // the journal is written again below, so a second crash keeps it.
            _currentRaceIsRerun = entry.IsRerun;

            if (!string.IsNullOrWhiteSpace(entry.TrackLayoutName))
            {
                var index = _cmbTrackLayout.Items.IndexOf(entry.TrackLayoutName);

                if (index >= 0)
                {
                    _cmbTrackLayout.SelectedIndex = index;
                }
            }

            // Written again now that the start time and the track are the
            // restored race's own. RestoreSnapshot raises Racing and the red
            // flag before either was set, and the journal written on that
            // flag carried whatever start time and track were here before -
            // so a second crash before the next crossing came back with the
            // wrong ones.
            WriteJournal(force: true);

            RenderLeaderboard();
            UpdateClockDisplay(force: true);

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [race] Restored the unfinished race - {Plural.Of(entry.Standings.Count, "driver")}, "
                + $"{Plural.Of(entry.LapCount, "lap")}, held under a red flag. Green when the cars are back on the track, "
                + "or checkered to save it as it stands.");
        }
        catch (Exception ex)
        {
            AppDialog.Show(this,
                $"That race couldn't be restored: {ex.Message}",
                "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Automatically renders and saves a shareable results card PNG the
    /// moment a race completes, so there's always one ready to share
    /// without needing a manual trip to Race History afterward. Silently
    /// skipped if it fails for any reason - this is a convenience, not
    /// something that should ever block or interrupt finishing a race.
    /// </summary>
    private void TrySaveAutoResultsCard(SessionRecord record)
    {
        try
        {
            // Documents\HeatCaller Results - or the rehearsal copy's own
            // folder, so a rehearsal's cards never mix with the club's.
            var folder = Rehearsal.DocumentsFolder("HeatCaller Results");
            Directory.CreateDirectory(folder);

            var safeLabel = string.Concat(record.Label.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_')).Trim();
            var fileName = $"{record.StartedAtUtc.ToLocalTime():yyyy-MM-dd_HH-mm}_{(string.IsNullOrWhiteSpace(safeLabel) ? "Race" : safeLabel)}_results.png";
            var path = Path.Combine(folder, fileName);

            using var bitmap = ResultsCardRenderer.Render(record, _lastUnlocks, ChartPalette.For(_settings.ResultsCardStyle));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [results] Shareable results card saved to {path}");
        }
        catch
        {
            // Never let a failure here interrupt finishing a race - the
            // same card can still be generated manually from Race History.
        }
    }
}

/// <summary>
/// One segment's show, where the thing that ends it can be asked for before
/// the thing that starts it has finished starting.
///
/// ShowTreatment.StartAsync takes 0.8 to 3 seconds - it waits out the
/// opener's ring-out - and only then hands back the ShowRun that ends the
/// show. A race can leave staging inside that window (a countdown started
/// by hand, a reset, a stop, a red flag), and whoever notices asks for the
/// show to be cut while there is nothing yet to cut.
///
/// One latch could not carry that. The cut spent it, finished nothing
/// because the run had not been handed over, and the live run that landed a
/// moment later was then never finished at all - so ShowOnAir stayed
/// counted up, MainForm kept the club's music muted for the rest of the
/// evening, and the bed looped under the countdown, the horn and every heat
/// after it, which is exactly what cutting it short exists to prevent. So a
/// cut asked for too early is REMEMBERED here instead, and whoever opens
/// the show reads it back and ends the run itself.
///
/// Both halves can run at once, on different threads, and exactly one
/// FinishAsync comes out either way: each writes with a fence before
/// reading what the other wrote, so at least one of them sees it, and the
/// latch lets only the first of them end the show.
/// </summary>
internal sealed class ShowLifetime
{
    /// <summary>What StartAsync handed over, once it has. Null until then, and when there was nothing to open.</summary>
    private ShowRun? _run;

    /// <summary>0 running, 1 ended. The one FinishAsync.</summary>
    private int _ended;

    /// <summary>1 once a cut has been asked for, whether or not there was anything to cut at the time.</summary>
    private int _cutWanted;

    /// <summary>
    /// Whether a cut has already been asked for. For the caller to check
    /// before opening a show at all: one called off before it started
    /// should not be started.
    /// </summary>
    internal bool CutAlreadyWanted => Volatile.Read(ref _cutWanted) == 1;

    /// <summary>
    /// Takes the run StartAsync handed over - null when there was nothing
    /// to open. Returns true when a cut was asked for while it was opening,
    /// which makes ending the show the caller's own job: see the remarks on
    /// this class.
    /// </summary>
    internal bool Opened(ShowRun? run)
    {
        // Interlocked for the fence: this write must not slide past the
        // read below. EndAsync does the mirror of it, so of the two of them
        // running at once at least one sees what the other wrote.
        Interlocked.Exchange(ref _run, run);

        return Volatile.Read(ref _cutWanted) == 1;
    }

    /// <summary>
    /// Ends the show, once, if there is one to end. A cut asked for before
    /// the run has been handed over is remembered rather than carried out,
    /// and leaves the latch unspent for whoever does have the run.
    /// </summary>
    internal async Task EndAsync(bool cutShort)
    {
        if (cutShort)
        {
            // Fenced, and ahead of the read of _run below - see Opened.
            Interlocked.Exchange(ref _cutWanted, 1);
        }

        var run = Volatile.Read(ref _run);

        if (run is null)
        {
            // Nothing to end: either no show ever opened, or it is still
            // opening and the handle that ends it has not been handed over
            // yet. The latch is left alone either way.
            return;
        }

        if (Interlocked.Exchange(ref _ended, 1) == 1)
        {
            return;
        }

        try
        {
            await run.FinishAsync(cutShort);
        }
        catch
        {
            // FinishAsync gives the music back in its own finally.
        }
    }
}
