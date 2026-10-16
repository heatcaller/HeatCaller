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
        Task<string?> generation, (double, double)? delivery)
    {
        var line = await generation;

        if (string.IsNullOrWhiteSpace(line))
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
        var audio = await _announcer.PrepareReminderAsync(spoken, delivery, RecapHostVoice);

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
                StatusMessage?.Invoke("[badges] achievements.json could not be read just now, so badges were not worked out for this race.");
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
                    $"{AppTime.LogStampNow()}  [badge] {unlock.DriverName} unlocked \"{unlock.Badge.Name}\""
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
            _honours = PlanHonours(unlocks);

            if (_honours is not null)
            {
                // BOTH numbers when they differ. The read is capped - four
                // drivers, three badges each - and one badge going to
                // several drivers is named once in the count, so "held
                // back" and "read out" are honestly different figures. With
                // only the first of them logged, a club whose honours read
                // shorter than the log promised had nothing to go on.
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [badge] {Plural.Of(unlocks.Count, "badge")} held back for the recap's honours segment "
                    + "rather than called out one at a time"
                    + (_honours.Badges > 0 && _honours.Badges != unlocks.Count
                        ? $" - the read names {Plural.Of(_honours.Badges, "badge")}."
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
                    $"{AppTime.LogStampNow()}  [badge] {unlocks.Count - MaxSpokenAchievements} more unlocked but not announced - they're on the driver's record.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [badge] Couldn't work out achievements for this race: {ex.Message}");
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

        // The teaser is fixed text and needs nothing from the LLM, so it is
        // NOT behind the commentary guard. It used to be, which meant an
        // unreachable Ollama silently killed the teaser too - the actual
        // reason it was never heard.
        // One of the configured teasers, picked at random - the same line
        // after every race is what makes an announcer sound canned.
        var teaserLines = _settings.EffectiveTeaserLines;
        var wantsTeaser = _settings.SummaryTeaserEnabled && teaserLines.Count > 0;

        var commentaryAvailable = _commentary is not null
                                  && _commentary.IsAvailable
                                  && _settings.CommentaryRaceSummaries;

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
        if (!wantsTeaser && !commentaryAvailable && honours is null)
        {
            // Nothing more to say - but the broadcast is only over once the
            // finish call has been spoken. Returning without the signal left
            // music set to fade or stop "after the recap" playing for the
            // rest of the night whenever both teaser and recap were off.
            SignalSessionAudioFinishedAfterFinishCall();
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

        Action<RaceState> onNextRace = state =>
        {
            if (state is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
            {
                // Async: Cancel() would run whatever is waiting on the token
                // right here, on the thread that is changing the race.
                _ = nextRace.CancelAsync();
            }
        };

        _race.StateChanged += onNextRace;

        // Already called? Then nothing follows the recap at all.
        if (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            nextRace.Cancel();
        }

        // The recording this recap belongs to, taken now on the UI thread.
        // Several waits in the sequence below are not cancelled by the next
        // race being called, so the end of the recap can land after the next
        // green - and must not stop THAT race's recording seconds in, the same
        // guard SignalSessionAudioFinishedAfterFinishCall already has.
        var recapRecording = _raceAudioRecorder;

        _ = Task.Run(async () =>
        {
            // The writing and rendering of the recap, the interview and the
            // sign-off: stopped with the next race, and also once the
            // sequence has given up on them, so nothing goes on costing
            // characters or holding the local model after it is over.
            using var followOnWork = CancellationTokenSource.CreateLinkedTokenSource(nextRace.Token);

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

            async Task EndShowAsync()
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
                        await run.FinishAsync();
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
            // Not conditional, and not failed on: nothing else can hold the
            // floor at the moment a race finishes, and a sequence that
            // somehow could not claim it is still the programme and still
            // goes out. The claim is what makes the FILLER stand down.
            ShowFloor.TryClaim(TheProgramme);

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

                var generation = commentaryAvailable
                    ? _commentary!.BuildRaceSummaryAsync(
                        record, championship, plan?.Context, raceHistory, followOnWork.Token,
                        historyPacked: packed => recapHistory = packed,
                        cars: raceCars,
                        mains: raceMains)
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

                // What follows the recap is written after it, each part as
                // soon as the one before is done: the local model answers one
                // request at a time, and the recap is wanted first. So the
                // interview and the sign-off are written - and rendered -
                // while the teaser and the recap play.
                var interviewTask = generation is not null && plan is { Interview: true }
                    ? WriteInterviewAsync(generation, record, plan, delivery, followOnWork.Token)
                    : null;

                var closingTask = generation is not null && plan is { SeparateClosing: true }
                    ? WriteClosingAsync(interviewTask is not null ? (Task)interviewTask : generation, generation, record, plan, delivery, followOnWork.Token)
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
                var colourTask = commentaryAvailable
                    ? WriteColourAsync(followOnWork.Token)
                    : null;

                var honoursRender = honours is null
                    ? null
                    : RenderHonoursAsync(honours, delivery, followOnWork.Token);

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

                    if (ShowMusic?.Invoke() is { } onTheirOwn && onTheirOwn.Treats(SegmentKind.BadgesEarned))
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

                // 2. Render the teaser DURING the pause, not after it.
                //
                // Synthesis is a network round trip, and it used to land on
                // top of the configured pause rather than inside it. Starting
                // it here means the pause is the only thing being waited for.
                var teaser = wantsTeaser ? teaserLines[Random.Shared.Next(teaserLines.Count)] : null;
                var teaserRender = teaser is null ? null : _announcer.PrepareReminderAsync(teaser);

                var flagPause = Math.Clamp(_settings.SummaryDelaySeconds, 0, 120);
                if (flagPause > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(flagPause));
                }

                // Written AND rendered as one job, so "the recap is ready"
                // means there is audio in hand, not just text.
                var recapTask = generation is null
                    ? null
                    : BuildRecapAsync(generation, delivery);

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
                    var prepared = teaserRender is null ? null : await teaserRender;

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
                            _announcer.SpeakPrepared(prepared);
                        }
                        else
                        {
                            // Voice not configured, muted, or the render failed -
                            // the teaser still speaks, just with the round trip
                            // in front of it.
                            _announcer.SpeakReminder(teaser, showOnScreens: true);
                        }
                    }, nextRace.Token);

                    if (!teaserQueued)
                    {
                        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Teaser and recap dropped - the next race was called to the grid.");
                        ForgetUnheardRecap();
                        return;
                    }

                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] Teaser spoken {(DateTime.UtcNow - sequenceStarted).TotalSeconds:0.0}s after the flag "
                        + $"({flagPause}s pause, {(prepared is null ? "rendered live" : "rendered during the pause")}): \"{teaser}\"");
                }
                else
                {
                    StatusMessage?.Invoke(_settings.SummaryTeaserEnabled
                        ? $"{AppTime.LogStampNow()}  [commentary] No teaser - every teaser line is empty on the Commentary tab."
                        : $"{AppTime.LogStampNow()}  [commentary] No teaser - it's switched off on the Commentary tab.");
                }

                if (!commentaryAvailable)
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] No recap - the local model isn't reachable.");

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
                await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

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
                if (!(_settings.RecapAdBreak && !nextRace.IsCancellationRequested && CommercialBreak.SponsorScreenUp))
                {
                    await PlayImagingAsync(ImagingCue.AfterRaceOver, nextRace.Token);
                }

                await HoldTheCommercialBreakAsync(recapTask!, nextRace.Token);

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
                        ? $"{AppTime.LogStampNow()}  [commentary] Recap called off after {tookSeconds:0.0}s - the next race was called to the grid before it was written."
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
                if (ShowMusic?.Invoke() is { } treatment && treatment.Treats(SegmentKind.RaceRecap))
                {
                    // SegmentKind.RaceRecap, so the show's running order
                    // knows which shape of show this is - the recap gets a
                    // rejoin on a busy night rather than the full opener for
                    // the eleventh time. See ShowRunningOrder.
                    opening = treatment.StartAsync(SegmentKind.RaceRecap, nextRace.Token);
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
                        _announcer.SpeakPrepared(recap.Audio, AnnouncementService.SpeechTail.Long);
                    }
                    else
                    {
                        // Render failed, or voice isn't configured - the recap
                        // still speaks, just with the round trip in front of it.
                        _announcer.SpeakReminder(recap.Spoken, delivery, RecapHostVoice, showOnScreens: true);
                    }
                }, nextRace.Token);

                if (!recapQueued)
                {
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] Recap dropped - the next race was called to the grid before it was ready "
                        + $"(it took {tookSeconds:0.0}s to write and render).");
                    ForgetUnheardRecap();
                    return;
                }

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Recap queued {(DateTime.UtcNow - teaserEnded).TotalSeconds:0.0}s after the teaser finished "
                    + $"({teaserGap}s gap set, {(recap.Audio is null ? "rendered live" : "already rendered")}).");

                // SPENT HERE, and nowhere earlier. The recap is queued to
                // the announcer, which is the same bar the teaser above is
                // held to - from this line on the room hears it. Marking
                // the facts here is what stops the next recap of the same
                // drivers reading out the same records, and leaving it
                // until here is what stops a dropped recap taking them off
                // the night for nothing. See CommentaryService.HistorySaid.
                _commentary!.HistorySpoken(recapHistory);

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
                    await StingBetweenRecapPartsAsync(nextRace.Token);
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
                    await StingBetweenRecapPartsAsync(nextRace.Token);
                    await SpeakColourAsync(colourTask, show, nextRace.Token);
                }

                // 6. After a main, the word with the mechanic; then the
                // sign-off, when it has been kept back to come last. Each
                // only once the line before it has been heard, and neither
                // once the next race has been called to the grid.
                if (interviewTask is not null)
                {
                    await StingBetweenRecapPartsAsync(nextRace.Token);
                }

                var interview = interviewTask is null
                    ? FollowOn.Spoken
                    : await SpeakInterviewAsync(interviewTask, nextRace.Token);

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
                        await StingBetweenRecapPartsAsync(nextRace.Token);
                        await SpeakClosingAsync(closingTask, nextRace.Token);
                    }
                }

                // Nothing still being written or rendered is wanted now.
                followOnWork.Cancel();

                // Music handled AFTER the recap, not at the flag. Waiting
                // for the voice queue means the fade starts when the
                // broadcast actually ends rather than cutting across it -
                // unless the next race has been called, which the deep duck
                // must not sit on top of.
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
                await EndShowAsync();

                // ... -> RACE RECAP BED -> BUMPER: the way out of the recap
                // show, over the bed's fade (16 Oct 2026). Heard out before
                // the floor is given back below, so a filler segment does
                // not start its own music on top of it.
                await PlayImagingAsync(ImagingCue.AfterRecap, nextRace.Token);
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
                ShowFloor.Release(TheProgramme);

                _race.StateChanged -= onNextRace;

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
    private sealed record RecapPlan(
        CommentaryService.RecapContext Context,
        bool Interview,
        bool SeparateClosing,
        string MechanicName,
        IReadOnlyList<string> SetupFacts,
        SecondVoice? MechanicVoice);

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
        var interview = _settings.RecapMechanicInterview && round?.Kind == RoundKind.Main;
        var closing = _settings.RecapBroadcastClosing;

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
        var follows = interview || honoursFollow;

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
            InterviewFollows: interview,

            // The host's own name, for the opening to introduce himself
            // with - see AppSettings.RecapAnnouncerName.
            AnnouncerName: (_settings.RecapAnnouncerName ?? string.Empty).Trim(),

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
                    $"{AppTime.LogStampNow()}  [commentary] The sign-off is held back to come after the honours, so the programme ends on it.");
            }

            return new RecapPlan(context, false, closing && honoursFollow, string.Empty, Array.Empty<string>(), null);
        }

        var mechanicName = (_settings.RecapMechanicName ?? string.Empty).Trim();
        IReadOnlyList<string> setupFacts = Array.Empty<string>();

        try
        {
            setupFacts = MechanicSetupFacts?.Invoke(record, round) ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] The mechanic goes without the Garage's setup log - it could not be read: {ex.Message}");
        }

        // The mechanic's voice for the engine in use. With none picked for
        // it the announcer's own voice answers - and that is said, because
        // both parts in one voice sounds like a mistake.
        var engine = _announcer.VoiceEngineInUse;
        var voices = _settings.MechanicVoice;
        var voice = voices.IsSetFor(engine) ? voices : null;

        var who = mechanicName.Length > 0 ? mechanicName : "the mechanic";

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [commentary] After the recap: an interview with {who}"
            + (voice is not null
                ? $" in the voice {(voices.NameFor(engine) is { Length: > 0 } picked ? $"\"{picked}\"" : "picked for it")}"
                : $" in the announcer's own voice - no mechanic's voice is picked for {EngineName(engine)} on the Commentary page")
            + (setupFacts.Count > 0
                ? ", with the setup the Garage has for the winning car."
                : ". No setup is logged for the winning car, so the mechanic won't talk settings."));

        return new RecapPlan(context, true, closing, mechanicName, setupFacts, voice);
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
    private sealed record HonoursPlan(IReadOnlyList<ExchangeTurn> Turns, SecondVoice? Voice, int Badges);

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
                    $"{AppTime.LogStampNow()}  [badge] No honours segment - there is no announcer screen to take the phrasings "
                    + "from. The badges are called out one at a time instead.");

                return null;
            }

            var turns = RaceHonours.Segment(unlocks, _settings.RecapHonoursName, rotation);

            if (turns.Count == 0)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [badge] No honours segment for this race - either every way of throwing to the "
                    + "honours has been heard tonight, or no badge had both a driver and a name that "
                    + "could be read out. The badges are called out one at a time instead.");

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

            var who = CommentaryService.Field(_settings.RecapHonoursName) is { Length: > 0 } named
                ? named
                : "the honours";

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [badge] In the recap: over to {who}"
                + (voice is not null
                    ? $" in the voice {(voices.NameFor(engine) is { Length: > 0 } picked ? $"\"{picked}\"" : "picked for it")}"
                    : $" in the announcer's own voice - no honours voice is picked for {EngineName(engine)} on the Commentary page")
                + $" - \"{turns[1].Line}\"");

            return new HonoursPlan(turns, voice, RaceHonours.Read(unlocks)?.Badges ?? 0);
        }
        catch (Exception ex)
        {
            // Decoration on the save path, exactly as the standings and the
            // recap's own plan are: a fault here must never be the reason a
            // result goes unsaved. The badges then go out as individual
            // calls, which is the arrangement that has always worked.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [badge] No honours segment - it could not be set up: {ex.Message}. "
                + "The badges are called out one at a time instead.");

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
    /// Writes the interview once the recap has been written, then renders
    /// each line - the questions in the announcer's voice, the answers in
    /// the mechanic's - one at a time, while the recap plays. Null when
    /// there's no recap for it to follow, nothing usable came back, or it
    /// was called off.
    /// </summary>
    private async Task<List<SpokenLine>?> WriteInterviewAsync(
        Task<string?> recapWritten, SessionRecord record, RecapPlan plan, (double Stability, double Style)? delivery, CancellationToken cancel)
    {
        try
        {
            // No recap, no interview: it would follow nothing.
            if (string.IsNullOrWhiteSpace(await recapWritten) || cancel.IsCancellationRequested)
            {
                return null;
            }

            var exchanges = await _commentary!.BuildMechanicInterviewAsync(
                record,
                plan.MechanicName,
                plan.SetupFacts,
                plan.Context.Kind,
                log: note => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {note}"),
                ct: cancel);

            if (exchanges is null)
            {
                return null;
            }

            var lines = new List<SpokenLine>();

            foreach (var exchange in exchanges)
            {
                // Checked before each render: each one costs characters.
                if (cancel.IsCancellationRequested)
                {
                    return null;
                }

                // The host asks - the questions are part of the recap show.
                lines.Add(new SpokenLine(exchange.Question, RecapHostVoice, delivery,
                    await _announcer.PrepareReminderAsync(exchange.Question, delivery, RecapHostVoice)));

                if (cancel.IsCancellationRequested)
                {
                    return null;
                }

                lines.Add(new SpokenLine(exchange.Answer, plan.MechanicVoice, null,
                    await _announcer.PrepareReminderAsync(exchange.Answer, null, plan.MechanicVoice)));
            }

            return lines;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] No interview - {ex.Message}");
            return null;
        }
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

            var line = await _commentary!.BuildBroadcastClosingAsync(record, plan.Context, cancel);

            if (string.IsNullOrWhiteSpace(line) || cancel.IsCancellationRequested)
            {
                return null;
            }

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
    private async Task<FollowOn> SpeakInterviewAsync(Task<List<SpokenLine>?> interviewTask, CancellationToken nextRace)
    {
        const string stopped = "Interview stopped - the next race was called to the grid.";

        if (!await HeardOutAsync(nextRace))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped}");
            return FollowOn.Stopped;
        }

        var (timedOut, lines) = await WithinAsync(interviewTask, FollowOnWait, nextRace);

        if (nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped}");
            return FollowOn.Stopped;
        }

        if (lines is null)
        {
            StatusMessage?.Invoke(timedOut
                ? $"{AppTime.LogStampNow()}  [commentary] Interview skipped - it wasn't ready within {FollowOnWait.TotalSeconds:0}s of the recap ending."
                : $"{AppTime.LogStampNow()}  [commentary] No interview - nothing usable came back from the model. Any line above says why.");

            return timedOut ? FollowOn.Late : FollowOn.Spoken;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0 && !await HeardOutAsync(nextRace))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped}");
                return FollowOn.Stopped;
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
                run.AnotherFollows
                    ? AnnouncementService.SpeechTail.BackToBack
                    : AnnouncementService.SpeechTail.Normal,
                run.FollowsAnother
                    ? AnnouncementService.SpeechLead.FollowsOn
                    : AnnouncementService.SpeechLead.Normal);
        }

        return FollowOn.Spoken;
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
                $"{AppTime.LogStampNow()}  [badge] No honours segment - its lines could not be rendered: {ex.Message}. "
                + "The badges are on the drivers' records.");

            return null;
        }
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
        const string stopped = "Honours stopped - the next race was called to the grid. The badges are on the drivers' records.";

        if (!await HeardOutAsync(nextRace))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [badge] {stopped}");
            return;
        }

        var (timedOut, lines) = await WithinAsync(rendered, FollowOnWait, nextRace);

        if (nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [badge] {stopped}");
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
                ? $"{AppTime.LogStampNow()}  [badge] Honours skipped - they were not rendered within "
                  + $"{FollowOnWait.TotalSeconds:0}s of the recap ending. The badges are on the drivers' records."
                : $"{AppTime.LogStampNow()}  [badge] Honours skipped - their lines could not be rendered. "
                  + "The badges are on the drivers' records.");

            return;
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
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [badge] {stopped}");
                    return;
                }

                if (handoffs.Contains(i) && treatment is not null && !await treatment.HandOverAsync(show, nextRace))
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [badge] {stopped}");
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
                    : AnnouncementService.SpeechLead.Normal);
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [badge] Honours spoken: \"{plan.Turns[1].Line}\"");
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
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] The programme goes without its colour: {ex.GetType().Name}: {ex.Message}");
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
            ? $"{AppTime.LogStampNow()}  [commentary] {Plural.Of(found.Count, "colour segment")} in this programme, after the honours: "
              + string.Join(", ", found.Select(said => ShowBlock.CaptionFor(said.Kind) ?? CommentaryBudget.Spec(said.Kind).Title)) + "."
            : $"{AppTime.LogStampNow()}  [commentary] No colour in this programme - the segment ladder had nothing due. "
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
        const string stopped = "Colour dropped - the next race was called to the grid.";

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
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped}");

                return;
            }

            if (segments is null || segments.Count == 0)
            {
                if (timedOut)
                {
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] The programme's colour was not written within {FollowOnWait.TotalSeconds:0}s of the honours, so it is skipped.");
                }

                return;
            }

            var treatment = ShowMusic?.Invoke();

            foreach (var said in segments)
            {
                if (!await HeardOutAsync(nextRace))
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped}");

                    return;
                }

                // The mark between two items of a programme - the same one a
                // block puts between its own items, so a colour segment inside
                // the recap is punctuated the way one inside a block is. False
                // also means "nothing to play" (show music off, or no sting),
                // which is a reason to go on without the mark, not to drop the
                // colour - so only the next race being called stops it here.
                if (treatment is not null
                    && !await treatment.PunctuateAsync(nextRace)
                    && nextRace.IsCancellationRequested)
                {
                    SayUnlessTheNextRaceStopped(stopped, nextRace);

                    return;
                }

                // Going out now - see the director note above.
                director?.Spoken(said);

                if (said.IsExchange)
                {
                    for (var at = 0; at < said.Turns.Count; at++)
                    {
                        if (at > 0 && !await HeardOutAsync(nextRace))
                        {
                            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {stopped}");

                            return;
                        }

                        // The beat at a change of voice, and the pads trimmed
                        // for a turn with another hard against it - exactly as
                        // the honours do it. See CommentaryExchange.ExchangeRun.
                        if (at > 0 && treatment is not null && !await treatment.HandOverAsync(show, nextRace))
                        {
                            SayUnlessTheNextRaceStopped(stopped, nextRace);

                            return;
                        }

                        var turn = said.Turns[at];
                        var run = CommentaryExchange.ExchangeRun.At(at, said.Turns.Count);

                        _announcer.SpeakReminder(
                            turn.Line,
                            null,
                            turn.FromCoHost ? _settings.CoHostVoice : RecapHostVoice,
                            showOnScreens: true,
                            tail: run.AnotherFollows
                                ? AnnouncementService.SpeechTail.BackToBack
                                : AnnouncementService.SpeechTail.Normal,
                            lead: run.FollowsAnother
                                ? AnnouncementService.SpeechLead.FollowsOn
                                : AnnouncementService.SpeechLead.Normal);
                    }

                    continue;
                }

                _announcer.SpeakReminder(said.Text, null, RecapHostVoice, showOnScreens: true);
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

    /// <summary>Says a line unless the next race is what stopped things, which already has its own line.</summary>
    private void SayUnlessTheNextRaceStopped(string line, CancellationToken nextRace)
    {
        if (!nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {line}");
        }
    }

    /// <summary>Speaks the sign-off kept back for the end, once everything before it has been heard.</summary>
    /// <param name="nextRace">Cancelled when the next race is called to the grid.</param>
    private async Task SpeakClosingAsync(Task<SpokenLine?> closingTask, CancellationToken nextRace)
    {
        const string dropped = "Sign-off dropped - the next race was called to the grid.";

        if (!await HeardOutAsync(nextRace))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {dropped}");
            return;
        }

        var (timedOut, closing) = await WithinAsync(closingTask, FollowOnWait, nextRace);

        if (nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {dropped}");
            return;
        }

        if (closing is null)
        {
            StatusMessage?.Invoke(timedOut
                ? $"{AppTime.LogStampNow()}  [commentary] Sign-off skipped - it wasn't ready within {FollowOnWait.TotalSeconds:0}s."
                : $"{AppTime.LogStampNow()}  [commentary] No sign-off - nothing came back from the model. Any line above says why.");
            return;
        }

        Say(closing, AnnouncementService.SpeechTail.Long);
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
                    + "of the programme is queued behind it rather than waited for any longer.");
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
    private async Task HoldTheCommercialBreakAsync(Task recapReady, CancellationToken nextRace)
    {
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
                $"{AppTime.LogStampNow()}  [commentary] No commercial break - it is switched off on the Commentary page "
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

            while (true)
            {
                var on = DateTime.UtcNow - started;

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
                    // CommercialBreak.VideoOnScreen.
                    CommercialBreak.VideoOnScreen);

                if (ending != BreakEnding.Running)
                {
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] Commercial break ran {on.TotalSeconds:0.0}s - {WhyTheBreakEnded(ending)}");

                    // Marked only where nothing else marks it - see
                    // BreakTransition.ComingBack. The recap's own opener
                    // and a grid call both already do, and a second mark
                    // in front of either is two returns for one break.
                    MarkTheBreak(ending, goingIn: false, nextRace);

                    return;
                }

                try
                {
                    await Task.Delay(CommercialBreak.Look, nextRace);
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
        if (goingIn && BroadcastImaging.Has(ImagingCue.AfterRaceOver))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Commercial break - the bumper in, the sponsors have the crowd's screen.");

            _ = PlayImagingAsync(ImagingCue.AfterRaceOver, nextRace);

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
            _ = treatment
                .MarkOnceAsync(sting, treatment.BoundaryStingLevel, "break sting", nextRace)
                .ContinueWith(static done => _ = done.Exception, TaskScheduler.Default);
        }

        if (said is not null)
        {
            _announcer.SpeakReminder(said, showOnScreens: true);
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
    private async Task PlayImagingAsync(ImagingCue cue, CancellationToken nextRace)
    {
        try
        {
            await BroadcastImaging.PlayAsync(cue, nextRace);
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
    private async Task StingBetweenRecapPartsAsync(CancellationToken nextRace)
    {
        if (!BroadcastImaging.Has(ImagingCue.BetweenRecapParts))
        {
            return;
        }

        if (!await HeardOutAsync(nextRace))
        {
            return;
        }

        await PlayImagingAsync(ImagingCue.BetweenRecapParts, nextRace);
    }

    /// <summary>Queues one line: its audio when it rendered in advance, otherwise rendered as it is spoken.</summary>
    private void Say(
        SpokenLine line,
        AnnouncementService.SpeechTail tail = AnnouncementService.SpeechTail.Normal,
        AnnouncementService.SpeechLead lead = AnnouncementService.SpeechLead.Normal)
    {
        if (line.Audio is not null)
        {
            _announcer.SpeakPrepared(line.Audio, tail, lead);
        }
        else
        {
            // The pads reach the unprepared path too, since 11 Oct 2026.
            // The honours render in advance and so always take the branch
            // above, but a render that failed falls through to here and a
            // hand-over that sounded different depending on whether the
            // audio arrived early would be the worse bug of the two.
            _announcer.SpeakReminder(line.Text, line.Delivery, line.Voice, showOnScreens: true, tail: tail, lead: lead);
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
    /// Keeps the recording in step with the race. Called from WriteJournal,
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

                _raceAudioRecorder = RaceAudioRecorder.StartFor(
                    store,
                    _txtSessionLabel.Text,
                    DateTime.UtcNow,
                    message => StatusMessage?.Invoke(message));
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
            }));
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

        // CAPTURED, not read from the field inside the job. The guard at
        // the top of this method proved it was there on this thread; the
        // job runs later and the field is settable, so reading it again in
        // there would be checking one thing and using another.
        CommentaryService commentary = _commentary;

        _ = Task.Run(async () =>
        {
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
                    remember: false);

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

        var round = _activeRound?.Invoke();
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
                + "this race counts towards has a planned number of rounds set, or too many drivers can still win it "
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
        var writing = _announcer.BeginLineBeingWritten(RoundIntroModelBudget + TimeSpan.FromSeconds(10));

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

    private void SaveCompletedSessionToHistory()
    {
        // Read once and let go on every way out, so whatever runs next is
        // filed (or auto-advanced) as usual - see the auto-advance below.
        var wasQuickRace = _currentRaceIsQuick;
        _currentRaceIsQuick = false;
        var wasRerun = _currentRaceIsRerun;
        _currentRaceIsRerun = false;

        if (_race.IsPracticeMode)
        {
            StatusMessage?.Invoke("[session] Practice session - not auto-saved (use Save Practice Session).");

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
        _commentary?.ClearRaceSummary();

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
        if (_currentRaceIsDemo && !_settings.SaveDemoSessions && !_settings.DemoFillsTheProgramme)
        {
            StatusMessage?.Invoke(
                "[session] Demo race finished but was NOT saved - tick \"Save demo races to Race History\" "
                + "on the Demo Mode page to keep a copy there (marked [DEMO], never scored).");

            // Never reaches the recap, which is what normally signals the end
            // of the broadcast - so it is signalled here, or music set to
            // fade after the recap would play on after every unsaved demo.
            SignalSessionAudioFinishedAfterFinishCall();

            // As practice above: a demo is never journaled, so with an
            // earlier race still unfiled the journal is that race's.
            if (_unfiledRaces.Count == 0)
            {
                _journal.Clear();
            }

            return;
        }

        var ordered = _race.GetOrderedStandings();

        // Ghosts belong on the live board - being there is the entire point
        // of them - but they are a pace reference, not entrants. A saved
        // result is what feeds finishing positions into the programme, the
        // championship and the qualifying figures, so a ghost taking third
        // did not just score the points for third: it pushed every real
        // driver behind it down a place and took a place off each of them.
        var racers = ordered.Where(s => !GhostService.IsGhost(s.TransponderId)).ToList();

        // Which ghosts each driver finished ahead of, for Ghost Buster and
        // Shadow Boxer - worked out here, from the board, because the saved
        // result below leaves the ghosts out.
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
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [badge] Couldn't work out which ghosts were beaten: {ex.Message}");
        }

        // Both of these used to return silently, which made a race that
        // finished but never appeared in history impossible to diagnose.
        if (racers.Count == 0)
        {
            StatusMessage?.Invoke(ordered.Count == 0
                ? "[session] Not saved - no racers had any recorded laps."
                : "[session] Not saved - only ghost cars were on track, and a ghost is not a result.");

            // Same as the unsaved demo above: no recap will follow, so the
            // end of the broadcast is signalled here instead.
            SignalSessionAudioFinishedAfterFinishCall();

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
            StatusMessage?.Invoke(
                $"[session] {Plural.Of(ordered.Count - racers.Count, "ghost car")} left out of the saved result - "
                + "they set the pace, they don't score.");
        }

        if (_sessionStartedAtUtc is null)
        {
            // The start time is stamped when the race enters the Racing
            // state. If it's missing, this control never saw that
            // transition - so fall back to the session's own start rather
            // than throwing the results away.
            _sessionStartedAtUtc = _race.RaceStartUtc ?? DateTime.UtcNow;
            StatusMessage?.Invoke(
                $"[session] Start time was missing - fell back to {AppTime.LogStamp(_sessionStartedAtUtc.Value.ToLocalTime())}.");
        }

        // The rows themselves - see RaceFiling.ResultsFrom, which the
        // race-night test files races with too.
        var results = RaceFiling.ResultsFrom(racers, _profiles);

        // The Round box's choice, or the round a restored race was run for.
        var round = RoundForThisRace();
        _restoredRoundId = null;

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
        if (round is null && _rounds is not null && AutoAdvanceWanted && !_currentRaceIsDemo && !wasQuickRace && !wasRerun)
        {
            round = PickNextRound(racers);
            autoAdvanced = round is not null;
        }
        else if (round is null && wasQuickRace && !_currentRaceIsDemo)
        {
            StatusMessage?.Invoke(
                "[session] Quick Race - kept in Race History on its own. A Quick Race is never filed against a heat "
                + "by itself; pick the round in the Round box first if one is meant to count.");
        }
        else if (round is null && wasRerun && !_currentRaceIsDemo)
        {
            StatusMessage?.Invoke(
                "[session] Re-run of a round that was already recorded - kept in Race History on its own. "
                + "The round keeps its earlier result, and no other heat was marked as run.");
        }

        var record = new SessionRecord
        {
            SessionType = RaceSessionType.Race,

            // A demo keeps the round's name in its label (so it is clear
            // what was being rehearsed) but is never linked to the round.
            RoundId = _currentRaceIsDemo ? null : round?.Id,
            IsDemo = _currentRaceIsDemo,
            TrackLayoutName = _cmbTrackLayout.SelectedItem as string == "(none)" ? string.Empty : (string)(_cmbTrackLayout.SelectedItem ?? string.Empty),
            Label = BuildSessionLabel(round),
            StartedAtUtc = _sessionStartedAtUtc.Value,
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
        ApplyBlackFlags(record);

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

        // The history write, and only if it landed, the round linked and the
        // journal let go - in RaceFiling, where the order is tested. What
        // this page does around it runs through the two callbacks, at the
        // points it always ran.
        var filed = RaceFiling.Commit(
            record,
            round,
            autoAdvanced,
            AutoAdvanceWanted,
            _sessionHistory,
            _rounds,
            _journal,
            afterSaved: () =>
            {
                _sessionStartedAtUtc = null;

                // What a Pack picked on a row's right-click is recorded
                // against from now until the next race: this one. Matched
                // by the race's start, so a later race whose save failed is
                // never mistaken for it. Not a demo: its laps are invented.
                _filedSession = record.IsDemo ? null : (record.Id, record.Label, _race.RaceStartUtc);

                // Each driver's pack in the car, recorded for them on this
                // race or practice - one picked on a row's right-click during
                // it is already there, and stays. Here, at the flag, rather
                // than on every session added to the history: an Undo that
                // puts back a deleted session from last month must not tag
                // it with tonight's packs. Nor is a race saved late (see
                // FileUnfiledRaces) - a pack swapped since would be wrong,
                // and no pack is better than the wrong one.
                BatteryPacks?.AutoTag(record, _profiles);

                if (record.IsDemo)
                {
                    // Cleared rather than left over from the last real race, so the
                    // results card never shows somebody else's badges. The honours
                    // segment too, for the same reason and in the same breath: this
                    // branch does not call AwardAchievements, so its own clearing of
                    // the plan never runs.
                    _lastUnlocks = new List<AchievementUnlock>();
                    _honours = null;

                    StatusMessage?.Invoke(
                        "[session] Demo race - kept in Race History marked [DEMO] only. It is not linked to a heat and "
                        + "earns no points, badges, personal bests or records.");
                }
                else
                {
                    AwardAchievements(record);
                }

                SpeakRaceSummary(record, round);
            },
            beforeJournalCleared: () =>
            {
                TrySaveAutoResultsCard(record);

                // Once, on every path that saved: a demo race and a race with
                // no round both still need the page refreshed. After the round
                // is linked - see RaceFiling.Commit.
                SessionSaved?.Invoke();
            },
            log: message => StatusMessage?.Invoke(message),
            raceDayOpen: RoundPicker.RaceDayOpen(_events),
            demoMayFillTheProgramme: _settings.DemoFillsTheProgramme);

        if (filed == RaceFiling.Outcome.NotSaved)
        {
            StatusMessage?.Invoke(
                $"[session] COULD NOT SAVE \"{record.Label}\" - race-history.json could not be written "
                + "(see the [storage] line above). The race is still in the crash journal and has NOT been lost.");

            // The session start is cleared either way: the race is over
            // whatever the disk did.
            _sessionStartedAtUtc = null;

            // Kept, to be filed again - see _unfiledRaces. The store holds the
            // record back from every other save, so without this nothing
            // ever wrote it once the next race had taken over the journal.
            _unfiledRaces.Add(new UnfiledRace(record, round, autoAdvanced));

            // Commit stopped before afterSaved, so the recap never runs - and
            // the recap is what normally signals the end of the broadcast.
            // Without this, music set to fade or stop after the summary
            // played on through the dialog and into the gap between races,
            // as it once did after every unsaved demo (see above). Before
            // the dialog, which holds this thread until it is closed.
            SignalSessionAudioFinishedAfterFinishCall();

            AppDialog.Show(
                this,
                $"\"{record.Label}\" could not be written to the race history - something else on this PC has the "
                + "file open, the disk is full, or the history could not be read when HeatCaller started (and so "
                + "is not being written over).\r\n\r\nThe race has NOT been lost: it is still in the crash "
                + "journal, and HeatCaller will offer it back the next time it starts. Close anything that might be "
                + "reading the HeatCaller folder (a backup or sync tool is the usual cause) and restart HeatCaller "
                + "before running the next race.\r\n\r\nIf you carry on instead, HeatCaller tries to save it again "
                + "as the next race starts and after each race - but until it is saved, closing HeatCaller or a "
                + "crash during the next race loses it.",
                "Race not saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        else
        {
            // The journal has just been let go, so an earlier race that
            // could not be filed can be tried again without touching this
            // one's safety net - and the pages are refreshed for it.
            FileUnfiledRaces(refreshPages: true);
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
    /// journal on Stop (PracticeModeControl.HasUnfiledRaces).
    /// </summary>
    public bool HasUnfiledRaces => _unfiledRaces.Count > 0;

    /// <summary>The session generation FileUnfiledRaces was last tried for from WriteJournal - once per race is enough.</summary>
    private int _unfiledRetryGeneration = -1;

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
    /// False while the next race is starting: SessionSaved rebuilds the Round
    /// box, which must not move under a race being staged. The race's own
    /// filing raises it afterwards anyway.
    /// </param>
    private void FileUnfiledRaces(bool refreshPages)
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
                StatusMessage?.Invoke(
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
                    if (refreshPages)
                    {
                        SessionSaved?.Invoke();
                    }
                },
                log: message => StatusMessage?.Invoke(message),
                raceDayOpen: RoundPicker.RaceDayOpen(_events),
                demoMayFillTheProgramme: _settings.DemoFillsTheProgramme);

            if (outcome == RaceFiling.Outcome.Saved)
            {
                _unfiledRaces.Remove(unfiled);
                StatusMessage?.Invoke(
                    $"[session] \"{record.Label}\", which could not be saved earlier, is now in the race history. "
                    + "Its badges and personal bests were not worked out and its summary was not spoken.");
            }
        }
    }

    /// <summary>
    /// Writes the crash journal, with the round and the typed race name the
    /// race is being run under, so a restore can put those back too.
    ///
    /// At a new race's first write, an earlier race that could not be filed
    /// is tried again first: the journal still holds that race, and the
    /// write below is what replaces it. Once per race, not on every lap -
    /// each try writes the whole history file.
    /// </summary>
    private void WriteJournal(bool force = false)
    {
        if (_unfiledRaces.Count > 0
            && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing
            && _unfiledRetryGeneration != _race.SessionGeneration)
        {
            _unfiledRetryGeneration = _race.SessionGeneration;
            FileUnfiledRaces(refreshPages: false);

            if (_unfiledRaces.Count > 0 && !_race.IsDemoMode && !_race.IsPracticeMode)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [session] WARNING: {Plural.Of(_unfiledRaces.Count, "earlier race")} still could "
                    + "not be saved to race-history.json, and this race's crash journal is about to replace it. It is "
                    + "kept in memory and tried again after this race - do not close HeatCaller until the log says it "
                    + "is in the race history.");
            }
        }

        _journal.Record(_race, _sessionStartedAtUtc, SelectedTrackName(), force,
            RoundForThisRace()?.Id, _txtSessionLabel.Text, _currentRaceIsRerun);

        // Here because this is already the one place every crossing, every
        // flag and every correction passes through - the journal's own
        // cadence. It means the recording needs no hook of its own on the
        // session, and nothing about how a lap or a flag is handled changes.
        // Unlike the write above it is never throttled, so the green - which
        // is a flag change, and so a forced write - is always noted the
        // moment it drops.
        FollowRaceAudio();
    }

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
    public string? RestoredRoundId =>
        _restoredRoundId is { } id && _restoredRoundGeneration == _race.SessionGeneration ? id : null;

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

        return _restoredRoundId is { } id && _restoredRoundGeneration == _race.SessionGeneration
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
