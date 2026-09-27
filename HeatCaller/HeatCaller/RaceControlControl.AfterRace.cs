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

            var unlocks = AchievementEvaluator.EvaluateFinishedRace(
                record, _sessionHistory.All, store, RivalryStore.Load());

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
    /// </summary>
    private void SpeakRaceSummary(SessionRecord record)
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

        _ = Task.Run(async () =>
        {
            try
            {
                var sequenceStarted = DateTime.UtcNow;

                // The model starts work at the flag, not after the teaser.
                // Everything below - the finish call, the pause, the teaser -
                // is time the recap can be generating in, and it used to sit
                // idle through all of it.
                var generation = commentaryAvailable
                    ? _commentary!.BuildRaceSummaryAsync(record, championship)
                    : null;

                if (generation is not null)
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Asking the model for a recap...");
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

                // Punchier delivery for this one line. The words were
                // already broadcast-shaped; the flatness came from the
                // voice settings, where style exaggeration sits at 0 by
                // default and stability at a middling 0.5.
                //
                // Applied as-is, not merged with the profile. Merging took
                // the more EXPRESSIVE of the two, which pulled a cranked
                // announcer profile straight back in - and an
                // over-expressive recap is the cheesiness this setting now
                // exists to avoid.
                (double, double)? delivery = _settings.SummaryUseBroadcastVoice
                    ? (_settings.SummaryVoiceStability, _settings.SummaryVoiceStyle)
                    : null;

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

                // Deep duck for the recap. Raised BEFORE it's queued and
                // released after the voice queue falls quiet, so it covers
                // the whole segment rather than flapping per sentence.
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

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Recap queued {(DateTime.UtcNow - teaserEnded).TotalSeconds:0.0}s after the teaser finished "
                    + $"({teaserGap}s gap set, {(recap.Audio is null ? "rendered live" : "already rendered")}).");

                // Music handled AFTER the recap, not at the flag. Waiting
                // for the voice queue means the fade starts when the
                // broadcast actually ends rather than cutting across it.
                await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(60), CancellationToken.None);

                DeepDuckRequested?.Invoke(false);
            }
            catch
            {
                // Released here as well: a synthesis failure mid-recap would
                // otherwise leave the bed ducked for the rest of the night.
                DeepDuckRequested?.Invoke(false);

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
                RaiseSessionAudioFinished();
            }
        });
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

        // Released however this ends - spoken, dropped, failed - and in any
        // case after the budget plus a margin, or when a countdown starts.
        var writing = _announcer.BeginLineBeingWritten(RoundIntroModelBudget + TimeSpan.FromSeconds(10));

        _ = Task.Run(async () =>
        {
            string line;

            try
            {
                using var budget = new CancellationTokenSource(RoundIntroModelBudget);
                var answer = await _commentary.BuildRoundIntroAsync(round, budget.Token);

                if (string.IsNullOrWhiteSpace(answer))
                {
                    StatusMessage?.Invoke(budget.IsCancellationRequested
                        ? $"{AppTime.LogStampNow()}  [commentary] Round intro skipped - the model took longer than {RoundIntroModelBudget.TotalSeconds:0}s, so the start went on without it."
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
        if (_race.IsPracticeMode)
        {
            StatusMessage?.Invoke("[session] Practice session - not auto-saved (use Save Practice Session).");

            // Every way out of here clears the journal: the race is over
            // either way, and one left behind is offered back for recovery
            // on the next launch as if it had never finished.
            _journal.Clear();
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
                "[session] Demo race finished but was NOT saved - tick \"Save demo races to history\" "
                + "on the Demo Mode page to keep a copy in Race History (marked [DEMO], never scored).");

            // Never reaches the recap, which is what normally signals the end
            // of the broadcast - so it is signalled here, or music set to
            // fade after the recap would play on after every unsaved demo.
            SignalSessionAudioFinishedAfterFinishCall();
            _journal.Clear();
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
        var autoAdvanced = false;
        if (round is null && _rounds is not null && AutoAdvanceWanted && !_currentRaceIsDemo)
        {
            round = PickNextRound(racers);
            autoAdvanced = round is not null;
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

                SpeakRaceSummary(record);
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

            AppDialog.Show(
                this,
                $"\"{record.Label}\" could not be written to the race history - something else on this PC has the "
                + "file open, the disk is full, or the history could not be read when HeatCaller started (and so "
                + "is not being written over).\r\n\r\nThe race has NOT been lost: it is still in the crash "
                + "journal, and HeatCaller will offer it back the next time it starts. Close anything that might be "
                + "reading the HeatCaller folder (a backup or sync tool is the usual cause) and restart HeatCaller "
                + "before running the next race.",
                "Race not saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Writes the crash journal, with the round and the typed race name the
    /// race is being run under, so a restore can put those back too.
    /// </summary>
    private void WriteJournal(bool force = false) =>
        _journal.Record(_race, _sessionStartedAtUtc, SelectedTrackName(), force,
            RoundForThisRace()?.Id, _txtSessionLabel.Text);

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
