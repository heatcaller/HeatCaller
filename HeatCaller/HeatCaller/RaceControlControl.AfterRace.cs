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
        var audio = await _announcer.PrepareReminderAsync(spoken, delivery);

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
    /// staged over or reset. A Quick Race is filed on its own, never against
    /// the next heat by auto-advance - see SaveCompletedSessionToHistory.
    /// Nothing else about the race says it was one: the Round box is simply
    /// freeform, as it is for any race nobody picked a round for.
    /// </summary>
    private bool _currentRaceIsQuick;

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
            var unlocks = AchievementEvaluator.EvaluateFinishedRace(
                record, _sessionHistory.All, store, RivalryStore.Load(), _profiles);

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

    /// <summary>Raised with everything a finished race unlocked, for anything that wants to show it.</summary>
    public event Action<List<AchievementUnlock>>? AchievementsUnlocked;

    /// <summary>
    /// What the race just finished unlocked, held so the results card can
    /// print it. Cleared at each green, or a card would carry the last
    /// race's badges.
    /// </summary>
    private List<AchievementUnlock> _lastUnlocks = new();

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

        if (!wantsTeaser && !commentaryAvailable)
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

        // Heat or main, the opening and sign-off, the mechanic - from the
        // settings, the program and the Garage, so worked out here on the
        // UI thread for the same reason as the standings, and guarded the
        // same way: this is the save path.
        RecapPlan? plan = null;

        if (commentaryAvailable)
        {
            try
            {
                plan = PlanRecap(record, round);
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

        _ = Task.Run(async () =>
        {
            // The writing and rendering of the interview and the sign-off:
            // stopped with the next race, and also once the sequence has
            // given up on them, so nothing goes on costing characters or
            // holding the local model after it is over.
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
                var generation = commentaryAvailable
                    ? _commentary!.BuildRaceSummaryAsync(record, championship, plan?.Context)
                    : null;

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
                            _announcer.SpeakReminder(teaser);
                        }
                    }, nextRace.Token);

                    if (!teaserQueued)
                    {
                        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Teaser and recap dropped - the next race was called to the grid.");
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

                var recap = await recapTask!;
                var tookSeconds = (DateTime.UtcNow - generationStarted).TotalSeconds;

                if (recap.Spoken is null)
                {
                    // Silence was the only symptom before: the recap simply
                    // never arrived and nothing said why.
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] No recap after {tookSeconds:0.0}s - the model returned nothing. "
                        + "Any [llm] line above says whether it timed out, refused, or wasn't reachable.");

                    // The end is signalled in the finally below, once the
                    // teaser has actually been spoken.
                    await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                    return;
                }

                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Recap written and rendered in {tookSeconds:0.0}s.");

                await gap;

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
                        _announcer.SpeakReminder(recap.Spoken, delivery);
                    }
                }, nextRace.Token);

                if (!recapQueued)
                {
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] Recap dropped - the next race was called to the grid before it was ready "
                        + $"(it took {tookSeconds:0.0}s to write and render).");
                    return;
                }

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Recap queued {(DateTime.UtcNow - teaserEnded).TotalSeconds:0.0}s after the teaser finished "
                    + $"({teaserGap}s gap set, {(recap.Audio is null ? "rendered live" : "already rendered")}).");

                // 5. After a main, the word with the mechanic; then the
                // sign-off, when it has been kept back to come last. Each
                // only once the line before it has been heard, and neither
                // once the next race has been called to the grid.
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
                    await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(60), nextRace.Token);
                }
                catch (OperationCanceledException)
                {
                    // The next race was called while the broadcast finished.
                }

                ReleaseDeepDuck();
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
                RaiseSessionAudioFinished();

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

    /// <summary>The longest a line of the recap sequence is waited on to be heard before the next is queued behind it anyway.</summary>
    private static readonly TimeSpan RecapLineLimit = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Heat or main, the opening and sign-off, and the mechanic - what the
    /// Commentary page asks for, for this race.
    /// </summary>
    private RecapPlan PlanRecap(SessionRecord record, RoundDefinition? round)
    {
        var clubNight = round is not null && _events?.EventForRound(round.Id)?.IsClubNight == true;
        var kind = CommentaryService.DescribeKind(round, clubNight);

        // Mains only, as asked for on 27 Sep 2026: a word with the mechanic
        // is an occasion, and after every heat it would be a routine.
        var interview = _settings.RecapMechanicInterview && round?.Kind == RoundKind.Main;
        var closing = _settings.RecapBroadcastClosing;

        var context = new CommentaryService.RecapContext(
            kind,
            _settings.RecapBroadcastOpening,

            // After an interview the sign-off is a line of its own, spoken
            // last, rather than the end of the recap before it.
            closing && !interview,
            (_settings.RecapShowName ?? string.Empty).Trim(),

            // A demo leaves its round unrun, so that round is still next.
            closing ? UpNextAfter(record.IsDemo ? null : round) : null,
            InterviewFollows: interview);

        if (!interview)
        {
            return new RecapPlan(context, false, false, string.Empty, Array.Empty<string>(), null);
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
        VoiceEngine.Windows => "the Windows voice",
        VoiceEngine.HeatCaller => "the HeatCaller voice",
        _ => "ElevenLabs",
    };

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

                lines.Add(new SpokenLine(exchange.Question, null, delivery,
                    await _announcer.PrepareReminderAsync(exchange.Question, delivery)));

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

            return new SpokenLine(spoken, null, delivery, await _announcer.PrepareReminderAsync(spoken, delivery));
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

            Say(lines[i]);
        }

        return FollowOn.Spoken;
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
            await _announcer.WaitForAudioAsync(RecapLineLimit, nextRace);
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

    /// <summary>Queues one line: its audio when it rendered in advance, otherwise rendered as it is spoken.</summary>
    private void Say(SpokenLine line, AnnouncementService.SpeechTail tail = AnnouncementService.SpeechTail.Normal)
    {
        if (line.Audio is not null)
        {
            _announcer.SpeakPrepared(line.Audio, tail);
        }
        else
        {
            _announcer.SpeakReminder(line.Text, line.Delivery, line.Voice);
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
                RaiseSessionAudioFinished();
            }
        });
    }

    /// <summary>
    /// Raises SessionAudioFinished without letting a failing handler escape
    /// into a background task, where nobody would ever see it.
    /// </summary>
    private void RaiseSessionAudioFinished()
    {
        try
        {
            SessionAudioFinished?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [audio] End-of-broadcast signal failed: {ex.Message}");
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

        // On a club night a race is just a race - it isn't called a heat.
        var clubNight = _events?.EventForRound(round.Id)?.IsClubNight == true;

        // Released however this ends - spoken, dropped, failed - and in any
        // case after the budget plus a margin, or when a countdown starts.
        var writing = _announcer.BeginLineBeingWritten(RoundIntroModelBudget + TimeSpan.FromSeconds(10));

        _ = Task.Run(async () =>
        {
            string line;

            try
            {
                using var budget = new CancellationTokenSource(RoundIntroModelBudget);

                // Set when the intro was put right or thrown out after it
                // came back (CommentaryService.TidyIntro), which has already
                // said so - so an intro it dropped is not then reported as
                // one that never arrived.
                var tidied = false;

                var answer = await _commentary.BuildRoundIntroAsync(
                    round,
                    budget.Token,
                    log: note =>
                    {
                        tidied = true;
                        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {note}");
                    },
                    clubNight: clubNight);

                if (string.IsNullOrWhiteSpace(answer))
                {
                    StatusMessage?.Invoke(
                        tidied ? $"{AppTime.LogStampNow()}  [commentary] Round intro skipped - see the line above."
                        : budget.IsCancellationRequested ? $"{AppTime.LogStampNow()}  [commentary] Round intro skipped - the model took longer than {RoundIntroModelBudget.TotalSeconds:0}s, so the start went on without it."
                        : $"{AppTime.LogStampNow()}  [commentary] Round intro skipped - no line came back.");
                    writing.Dispose();
                    return;
                }

                line = answer;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Round intro skipped - {ex.Message}");
                writing.Dispose();
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
        });

        return true;
    }

    private void SaveCompletedSessionToHistory()
    {
        // Read once and let go on every way out, so whatever runs next is
        // filed (or auto-advanced) as usual - see the auto-advance below.
        var wasQuickRace = _currentRaceIsQuick;
        _currentRaceIsQuick = false;

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

        // Demo races are simulated, so they stay out of history unless the
        // user opts in. Even then the saved copy is marked as a demo and
        // scores nothing - no round marked as run, no championship points,
        // no badges, no personal bests or records. Every lap time in it was
        // invented, and a rehearsal quietly filling in a real heat (or
        // handing out a real driver's win) is worse than it not being saved.
        if (_currentRaceIsDemo && !_settings.SaveDemoSessions)
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
            // was left behind and offered back on the next launch.
            _journal.Clear();
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
        if (round is null && _rounds is not null && AutoAdvanceWanted && !_currentRaceIsDemo && !wasQuickRace)
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

        // A black flag from the warning ladder becomes a DQ on the saved
        // result, logged as a correction so it can be seen and reversed in
        // Race History like any other.
        ApplyBlackFlags(record);

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
                    // results card never shows somebody else's badges.
                    _lastUnlocks = new List<AchievementUnlock>();

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
            raceDayOpen: RoundPicker.RaceDayOpen(_events));

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
                raceDayOpen: RoundPicker.RaceDayOpen(_events));

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
            RoundForThisRace()?.Id, _txtSessionLabel.Text);
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
