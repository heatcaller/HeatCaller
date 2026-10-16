namespace HeatCaller;

/// <summary>
/// ON AIR (16 Oct 2026) - the button in front of Stage that kicks a race off
/// the way a station opens a segment: the soundscape's jingle or sweeper,
/// the opening, a bumper, and then the race is staged.
///
/// The club's run of show: jingle/sweeper -> opening -> bumper -> staging.
/// Stage on its own still runs a race exactly as it always has - the staging
/// call, then the round intro if one is due - so a club that never presses
/// On Air hears nothing new.
///
/// THE OPENING IS THE ROUND INTRO, written while the jingle plays rather
/// than after it, so the model's wait is spent under the music. It is the
/// same intro Stage would have asked for - the same facts, the same written-
/// ahead line when one is ready - so Stage is told not to ask for another.
/// With no intro due (commentary off, intros kept for mains and this is a
/// heat, the model not answering) it is the jingle and then the race: no
/// bumper with nothing to come out of.
///
/// Pressing On Air again while it runs skips the rest and stages straight
/// away. Anything else that moves the race on - Stage pressed by hand, the
/// remote, the day schedule - calls it off, and staging is theirs.
/// </summary>
public sealed partial class RaceControlControl
{
    private readonly FlagButton _btnOnAir = new() { FlagColour = Color.FromArgb(200, 32, 44), FlagText = Color.White };

    /// <summary>The sequence On Air is running, or null when it is not running.</summary>
    private CancellationTokenSource? _onAirCts;

    /// <summary>On Air was pressed again while it ran: skip what is left and stage now.</summary>
    private bool _onAirStageNow;

    /// <summary>
    /// On Air has said the round intro already, so the Stage it hands over
    /// to must not ask the model for another. Taken - and cleared - by that
    /// Stage.
    /// </summary>
    private bool _openingSaidByOnAir;

    /// <summary>Wires the button. Called from BuildLayout beside Stage.</summary>
    private void BuildOnAirButton()
    {
        _btnOnAir.Text = "On Air";
        _btnOnAir.Click += async (_, _) => await RunOnAirAsync();

        var tip = new ToolTip();
        AppTheme.StyleToolTip(tip, keep: true);
        tip.SetToolTip(_btnOnAir,
            "Kicks the race off like a radio show: the soundscape's jingle or sweeper, the opening, a bumper, then "
            + "Stage. Press it again to skip straight to Stage. The pieces are set on Sounds -> Imaging.");
    }

    /// <summary>Takes On Air's word that the opening has been said, for the Stage it hands over to. See <see cref="_openingSaidByOnAir"/>.</summary>
    private bool TakeOpeningSaidByOnAir()
    {
        var said = _openingSaidByOnAir;
        _openingSaidByOnAir = false;
        return said;
    }

    /// <summary>
    /// On Air for a demo (16 Oct 2026): the Race Program's "Run as Demo"
    /// comes through here, so a demo opens the way a real race does - the
    /// jingle or sweeper, the opening for <paramref name="round"/>, a bumper
    /// - and then <paramref name="start"/> starts the demo, which stages the
    /// race itself, in place of On Air pressing Stage.
    ///
    /// The round is handed over rather than read from the Round box: a demo
    /// that is not allowed to fill the programme leaves the box on freeform,
    /// and the opening would then have no race to be about.
    /// </summary>
    internal Task GoOnAirThenAsync(RoundDefinition round, Action start) => RunOnAirAsync(round, start);

    private async Task RunOnAirAsync(RoundDefinition? round = null, Action? start = null)
    {
        if (_onAirCts is { } running)
        {
            // A demo asked for while On Air is already running starts the
            // demo straight away; the On Air already going is called off,
            // since it was opening some other race.
            if (start is not null)
            {
                running.Cancel();
                start();
                return;
            }

            // Pressed again: the rest of it is skipped and the race staged now.
            _onAirStageNow = true;
            running.Cancel();
            return;
        }

        if (RefuseIfPracticeRunning("go on air"))
        {
            return;
        }

        // Only between races. A race that is staged or running has had its
        // top already, and Stage's own question about abandoning it is the
        // right one to ask - so On Air leaves it to Stage. A demo already
        // running is the exception: the new demo replaces it, as it always
        // has, so the top of the new one is played.
        if (start is null && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air is for between races - this one is already {_race.State.ToString().ToLowerInvariant()}.");
            return;
        }

        var cts = new CancellationTokenSource();
        _onAirCts = cts;
        _onAirStageNow = false;

        // No sponsor read from here until this race's flag - the opening
        // is about to be written, and the quiet while it is is not a gap.
        BroadcastImaging.OnAirBegins();
        _openingSaidByOnAir = false;
        _btnOnAir.Lit = true;

        // Anything else moving the race on calls this off - see the class
        // remarks. Async, because Cancel() would run whatever is waiting on
        // the token on the thread that is changing the race.
        Action<RaceState> onRaceMoved = state =>
        {
            if (state is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
            {
                try
                {
                    _ = cts.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                    // On Air had already finished.
                }
            }
        };

        _race.StateChanged += onRaceMoved;

        var stage = false;

        // What is on air, held so a call-off can take it away again.
        ImagingPlay? jingle = null;
        ImagingBed? bed = null;

        try
        {
            // 1. THE OPENING, WRITTEN AND RENDERED BEFORE ANYTHING PLAYS
            //    (16 Oct 2026). The voice has to HIT THE POST - start talking
            //    the instant the jingle's sung part stops - and it can only
            //    do that with the audio already in hand. Nothing is on air
            //    yet, so the wait for the model is silence before the show,
            //    never dead air in the middle of it.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - getting the opening ready.");

            var line = await WriteOpeningAsync(round, cts.Token);

            AnnouncementService.PreparedSpeech? audio = null;

            if (line is { } toRender)
            {
                audio = await _announcer.PrepareReminderAsync(toRender.Text);
            }

            cts.Token.ThrowIfCancellationRequested();

            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - the top of the race.");

            // 2. The jingle, to its post.
            jingle = BroadcastImaging.Start(ImagingCue.TopOfRace);

            if (jingle is not null)
            {
                await jingle.AtPost.WaitAsync(cts.Token);
            }

            // 3. On the post: the bed comes up under the voice, the rest of
            //    the jingle crossfades into it, and the voice hits the post.
            //    No voice before the race is in the clear from here to the
            //    countdown - see HandTheBedToTheStart.
            bed = ImagingBed.Start(ImagingCue.OpeningBed, _announcer, OpeningBedLevel, OpeningBedDuck);
            jingle?.CrossfadeOut();

            if (line is { } said)
            {
                if (audio is not null)
                {
                    _announcer.SpeakPrepared(audio);
                }
                else
                {
                    _announcer.SpeakReminder(said.Text);
                }

                // As Stage's own intro does: recorded as said, and its facts
                // spent, only once the line has been queued.
                _commentary?.IntroSpoken(said.Text);
                _commentary?.HistorySpoken(said.History);
                _openingSaidByOnAir = true;

                await _announcer.WaitForAudioAsync(IntroShowLimit, cts.Token);

                // 4. The bumper, crushed straight in off the last word, the
                //    bed dipped under it. The staging call hits ITS post.
                if (BroadcastImaging.Start(ImagingCue.AfterOpening) is { } bumper)
                {
                    bed?.Dip(true);

                    try
                    {
                        await bumper.AtPost.WaitAsync(cts.Token);
                    }
                    finally
                    {
                        bumper.CrossfadeOut();
                        bed?.Dip(false);
                    }
                }

                cts.Token.ThrowIfCancellationRequested();
            }

            stage = true;
        }
        catch (OperationCanceledException)
        {
            // Pressed again stages now; anything else moved the race on
            // itself and staging is not ours to do.
            stage = _onAirStageNow;
        }
        catch (Exception ex)
        {
            // Decoration. The race still gets staged.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - {ex.Message}. Staging the race.");
            stage = true;
        }
        finally
        {
            _race.StateChanged -= onRaceMoved;
            _onAirCts = null;
            _onAirStageNow = false;
            _btnOnAir.Lit = false;
            cts.Dispose();
        }

        if (!stage || IsDisposed || Disposing)
        {
            _openingSaidByOnAir = false;
            jingle?.CrossfadeOut();
            bed?.Finish();
            return;
        }

        // A demo stages itself - see GoOnAirThenAsync.
        if (start is not null)
        {
            _openingSaidByOnAir = false;
            HandTheBedToTheStart(bed);
            start();
            return;
        }

        // Not when the race has been staged some other way in the meantime.
        if (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            _openingSaidByOnAir = false;
            bed?.Finish();
            return;
        }

        HandTheBedToTheStart(bed);
        _actStage?.Invoke(false, null);
    }

    /// <summary>The opening bed on its own - the show bed level set on the Commentary page.</summary>
    private float OpeningBedLevel =>
        _settings.ShowBedLevel is >= 0 and <= 100 ? _settings.ShowBedLevel / 100f : 0.22f;

    /// <summary>How far the opening bed drops under the voice - the show bed's own duck.</summary>
    private float OpeningBedDuck => Math.Clamp(_settings.ShowBedDuckPercent / 100f, 0.05f, 1f);

    /// <summary>
    /// The longest the opening bed may run after On Air hands over - a start
    /// that never reaches its countdown (a warm-up, a race director who is
    /// not ready) must not leave it looping under the rest of the night.
    /// </summary>
    private static readonly TimeSpan LongestOpeningBed = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Keeps the opening bed under the staging call and takes it away when
    /// the race moves on: the countdown starting - its beeps and the horn
    /// want the room to themselves - a reset, or <see cref="LongestOpeningBed"/>.
    /// </summary>
    private void HandTheBedToTheStart(ImagingBed? bed)
    {
        if (bed is null)
        {
            return;
        }

        Action<RaceState>? watcher = null;

        void End()
        {
            if (Interlocked.Exchange(ref watcher, null) is { } w)
            {
                _race.StateChanged -= w;
            }

            bed.Finish();
        }

        watcher = state =>
        {
            if (state is not RaceState.Staging)
            {
                End();
            }
        };

        _race.StateChanged += watcher;

        _ = Task.Delay(LongestOpeningBed).ContinueWith(_ => End(), TaskScheduler.Default);
    }

    /// <summary>
    /// The opening: the round intro for the race in the Round box, written
    /// exactly as Stage writes one - the written-ahead line when it is ready
    /// and still true, otherwise the model, inside the same budget. Null when
    /// no intro is due or none came back, which On Air reads as "no opening".
    ///
    /// Read on the UI thread up to the model call, as Stage's is - see
    /// ReadIntroFacts.
    /// </summary>
    private async Task<(string Text, PackedFacts? History)?> WriteOpeningAsync(RoundDefinition? forRound, CancellationToken ct)
    {
        try
        {
            if (_commentary is null || !_commentary.IsAvailable || !_settings.CommentaryRaceIntros)
            {
                return null;
            }

            var round = forRound ?? _activeRound?.Invoke();

            if (round is null)
            {
                return null;
            }

            if (_settings.CommentaryIntrosMainsOrRivalriesOnly
                && round.Kind != RoundKind.Main
                && !_commentary.HasRivalryAmong(round.AssignedTransponders))
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [imaging] No opening for {round.RoundLabel} - intros are set to mains and rivalries only, "
                    + "and this race is neither.");

                return null;
            }

            var (clubNight, form, stake, scene) = ReadIntroFacts(round);

            PackedFacts? history = null;
            string? answer;

            var ready = _writtenAhead.TakeFor(round.Id, round.AssignedTransponders, _commentary.HistorySaid, DateTime.Now);

            if (ready.Why == WrittenAhead.Why.Used && ready.Line is { Length: > 0 } already)
            {
                history = ready.Facts;
                answer = already;
            }
            else
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(RoundIntroModelBudget);

                answer = await _commentary.BuildRoundIntroAsync(
                    round,
                    budget.Token,
                    log: note => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] {note}"),
                    clubNight: clubNight,
                    form: form,
                    stake: stake,
                    historyPacked: packed => history = packed,
                    scene: scene);
            }

            if (string.IsNullOrWhiteSpace(answer))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] No opening - no intro came back in time, so On Air goes straight to the race.");
                return null;
            }

            return (answer, history);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] No opening - {ex.Message}");
            return null;
        }
    }
}
