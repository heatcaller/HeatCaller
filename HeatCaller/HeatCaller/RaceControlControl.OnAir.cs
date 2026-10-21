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
///
/// THE NORMAL START (17 Oct 2026). Only the Race Program's demo came through
/// here; Start Next, the Program's Start, F1, the phone and the day schedule
/// all staged directly, so the top of the race was heard only when somebody
/// remembered the button. With "Start races On Air" ticked (beside the
/// button, on by default) they all go through On Air first and its hand-over
/// is the Stage they would have called - see StartThroughOnAir. The Stage
/// button itself still stages directly, and On Air pressed again still
/// skips to Stage.
/// </summary>
public sealed partial class RaceControlControl
{
    private readonly FlagButton _btnOnAir = new() { FlagColour = Color.FromArgb(200, 32, 44), FlagText = Color.White };

    /// <summary>The sequence On Air is running, or null when it is not running.</summary>
    private CancellationTokenSource? _onAirCts;

    /// <summary>
    /// On Air is running - opening a race (17 Oct 2026, Bug Hunt 8, A03-1).
    /// Start Next and MainForm.StartRoundNow refuse a start while it is: a
    /// second start would pick the round after the one being opened, and
    /// then stage that one under this one's opening.
    /// </summary>
    internal bool OnAirRunning => _onAirCts is not null;

    /// <summary>On Air was pressed again while it ran: skip what is left and stage now.</summary>
    private bool _onAirStageNow;

    /// <summary>
    /// On Air has said the round intro already, so the Stage it hands over
    /// to must not ask the model for another. Taken - and cleared - by that
    /// Stage.
    /// </summary>
    private bool _openingSaidByOnAir;

    /// <summary>
    /// "Start races On Air" - see <see cref="StartWithOnAir"/>. Under the
    /// flag row, at On Air's end of it.
    /// </summary>
    private readonly ThemedCheckBox _chkStartWithOnAir = new();

    /// <summary>
    /// Whether a race started any way but the Stage button goes through On
    /// Air first (17 Oct 2026) - Start Next, the Program's Start, F1, the
    /// phone and the day schedule. On by default: On Air is the normal
    /// start. Remembered between launches - AppSettings.RaceStartWithOnAir.
    /// </summary>
    internal bool StartWithOnAir
    {
        get => _settings.RaceStartWithOnAir;
        set
        {
            if (_settings.RaceStartWithOnAir != value)
            {
                _settings.RaceStartWithOnAir = value;
                _settings.Save();
            }

            if (_chkStartWithOnAir.Checked != value)
            {
                _chkStartWithOnAir.Checked = value;
            }
        }
    }

    /// <summary>What On Air calls itself while it holds the broadcast floor - see ShowFloor.</summary>
    private const string OnAirHoldsTheFloor = "On Air opening the next race";

    /// <summary>The button's own caption, and what it says while the opening is still being got ready.</summary>
    private const string OnAirCaption = "On Air";
    private const string OnAirWaitingCaption = "Waiting\u2026";

    /// <summary>Wires the button. Called from BuildLayout beside Stage.</summary>
    private void BuildOnAirButton()
    {
        _btnOnAir.Text = OnAirCaption;
        _btnOnAir.Click += (_, _) => PressOnAir();

        _chkStartWithOnAir.Text = "Start races On Air";
        _chkStartWithOnAir.AutoSize = true;
        _chkStartWithOnAir.Checked = _settings.RaceStartWithOnAir;
        _chkStartWithOnAir.CheckedChanged += (_, _) => StartWithOnAir = _chkStartWithOnAir.Checked;

        var tip = new ToolTip();
        AppTheme.StyleToolTip(tip, keep: true);
        tip.SetToolTip(_btnOnAir,
            "Kicks the race off like a radio show: the soundscape's jingle or sweeper, the opening, a bumper, then "
            + "Stage. Press it again to skip straight to Stage. The pieces are set on Sounds -> Imaging. Shortcut: O");
        tip.SetToolTip(_chkStartWithOnAir,
            "Ticked, Start Next, the program's Start, F1, the phone and the day schedule all go through On Air first. "
            + "Unticked, they stage straight away. The Stage button always stages straight away.");
    }

    /// <summary>The On Air button, pressed - by a click or the O key.</summary>
    private async void PressOnAir() => await RunOnAirAsync();

    /// <summary>
    /// Starts a race the normal way (17 Oct 2026): through On Air when
    /// <see cref="StartWithOnAir"/> is ticked, with On Air's hand-over the
    /// very Stage the caller would have pressed - the same confirmation over
    /// a running race and the same undo - so the staging chain, its
    /// auto-advance and the warm-up all run exactly as before.
    ///
    /// Straight to Stage, as before, whenever On Air would not open this
    /// race: practice running (Stage refuses it itself), a warm-up open
    /// (Stage skips it), or a race already staged, counting down or running
    /// (Stage's own question about abandoning it is the right one). On Air
    /// already running is the exception - this is then its second press, and
    /// it skips to Stage.
    /// </summary>
    internal void StartThroughOnAir(bool confirmedOverRunningRace = false, Action? restoreHostOnUndo = null)
    {
        void StageNow() => _actStage?.Invoke(confirmedOverRunningRace, restoreHostOnUndo);

        var onAirCanOpen = _onAirCts is not null
                           || (!_race.IsPracticeMode
                               && !_race.IsWarmingUp
                               && _race.State is not (RaceState.Staging or RaceState.Countdown or RaceState.Racing));

        if (!StartWithOnAir || !onAirCanOpen)
        {
            StageNow();
            return;
        }

        StartOnAir(StageNow);
    }

    /// <summary>On Air with the caller's Stage as its hand-over - async void, as the button's click is, so a fault surfaces the same way.</summary>
    private async void StartOnAir(Action handOver) => await RunOnAirAsync(handOver: handOver);

    /// <summary>
    /// Run as On Air starts, before anything plays (17 Oct 2026): MainForm
    /// opens the session's music and crowd silent here, so the staging call
    /// does not open them on top of the broadcast - opening a sound output
    /// stalls everything else playing for a moment. See
    /// MainForm.ReadySessionAudioForOnAir.
    /// </summary>
    internal Action? OnAirStarting { get; set; }

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
    ///
    /// AND THE DEMO MODE PAGE'S OWN START (17 Oct 2026), which skipped all
    /// of this. Its demo of the checked drivers has no round, and then there
    /// is no opening - the jingle and the demo, never an opening about
    /// whatever the Round box happens to hold. With Start races On Air
    /// unticked, both demos start straight away, like every other start.
    /// </summary>
    internal Task GoOnAirThenAsync(RoundDefinition? round, Action start)
    {
        if (!StartWithOnAir)
        {
            start();
            return Task.CompletedTask;
        }

        return RunOnAirAsync(round, start);
    }

    /// <param name="round">The race to open, or null for the one in the Round box (a demo's: none).</param>
    /// <param name="start">A demo's own start, in place of staging - see GoOnAirThenAsync.</param>
    /// <param name="handOver">
    /// The Stage to call at the end, in place of a plain Stage press - see
    /// StartThroughOnAir. Null for the button itself.
    /// </param>
    private async Task RunOnAirAsync(RoundDefinition? round = null, Action? start = null, Action? handOver = null)
    {
        if (_onAirCts is { } running)
        {
            // A demo asked for while On Air is already running starts the
            // demo straight away; the On Air already going is called off,
            // since it was opening some other race.
            if (start is not null)
            {
                running.Cancel();

                // The On Air being called off was opening another race; what
                // it said is not this demo's (see ShowAtDemoStaging).
                _openingSaidByOnAir = false;
                _introductionsSaidByOnAir = false;
                _principalSaidByOnAir = false;
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

        // 17 Oct 2026, Bug Hunt 8, A03-4: nor over an open warm-up. The
        // opening played over the free laps and its hand-over then cut them
        // short. StartThroughOnAir already sends a warm-up straight to Stage.
        if (start is null && _race.IsWarmingUp)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air is for between races - the warm-up is open. Press Stage to skip it and stage now.");
            return;
        }

        // THE BROADCAST FLOOR, FROM THE PRESS UNTIL THE RACE IS STAGED OR
        // ON AIR IS CALLED OFF (17 Oct 2026). Without it the announcer
        // screen's director, which asks for a segment twice a second, saw a
        // quiet gap while the opening was written and started a segment of
        // its own - over the jingle, or under the opening. Taken as the
        // recap sequence takes it; not failed on, since On Air is the race
        // director's own press. Released on every way out - see the finally.
        //
        // The recap show still running is called off first: On Air is the
        // next race being called, which is what used to end it at Stage.
        EndRecapShow("On Air was pressed for the next race");

        _onAirHasFloor = ShowFloor.TryClaim(OnAirHoldsTheFloor);

        if (!_onAirHasFloor)
        {
            // Usually the recap just called off, still winding down - asked
            // again once the opening is written. See OnAirAsync.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [imaging] On Air - {ShowFloor.HeldBy ?? "something else"} still has the broadcast floor; "
                + "On Air takes it as soon as it is free.");
        }

        try
        {
            await OnAirAsync(round, start, handOver);
        }
        finally
        {
            // After the hand-over, which stages synchronously: by now the
            // race is staging and the director stands down for that itself.
            if (_onAirHasFloor)
            {
                ShowFloor.Release(OnAirHoldsTheFloor);
            }

            _onAirHasFloor = false;
        }
    }

    /// <summary>On Air holds the broadcast floor - see RunOnAirAsync. Only one On Air runs at a time.</summary>
    private bool _onAirHasFloor;

    /// <summary>The run of show itself, with the floor held - see RunOnAirAsync.</summary>
    private async Task OnAirAsync(RoundDefinition? round, Action? start, Action? handOver)
    {
        var cts = new CancellationTokenSource();
        _onAirCts = cts;
        _onAirStageNow = false;

        // No sponsor read from here until this race's flag - the opening
        // is about to be written, and the quiet while it is is not a gap.
        BroadcastImaging.OnAirBegins();
        _openingSaidByOnAir = false;
        _introductionsSaidByOnAir = false;
        _principalSaidByOnAir = false;
        _btnOnAir.Lit = true;

        // 17 Oct 2026, Bug Hunt 8, A03-1: Start Next greys while On Air runs.
        UpdateNextUpBar();

        // The music and crowd the staging call will bring up, opened now
        // while nothing is on air - see OnAirStarting.
        try
        {
            OnAirStarting?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - couldn't open the session's music and crowd early ({ex.Message}). They start at staging as usual.");
        }

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

            // SAYS SO ON THE BUTTON (16 Oct 2026). Nothing is heard while
            // the opening is written and rendered, so the button reads
            // "Waiting..." until the show actually starts - the race
            // director can see it was pressed and is on its way.
            _btnOnAir.Text = OnAirWaitingCaption;

            // A demo with no round has no race to open - see GoOnAirThenAsync.
            var line = start is not null && round is null ? null : await WriteOpeningAsync(round, cts.Token);

            // THE ANNOUNCER SAYS WHO HE IS (16 Oct 2026), a line into the
            // opening - "Welcome back to the track! I'm Dave Marsh. Heat
            // three..." Put in here rather than asked of the model, so it is
            // there every time and in the same place. See AnnouncerIntro.
            if (line is { Text: { } writtenText } written)
            {
                line = written with { Text = AnnouncerIntro.IntoOpening(writtenText, _settings.RecapAnnouncerName) };
            }

            // THE WHOLE RUN BEFORE STAGING, RENDERED NOW (18 Oct 2026): the
            // opening, then the driver introductions, then the throw to pit
            // lane and the Team Principal (rendered as they were written -
            // see WritePrincipalLinesAsync; after the introductions since
            // 20 Oct 2026). All of it in hand before the
            // jingle, so each part runs straight on from the last.
            AnnouncementService.PreparedSpeech? audio = null;
            AnnouncementService.PreparedSpeech? introductionsAudio = null;

            if (line is { } toRender)
            {
                // No opening when only the pit lane is due - see WriteOpeningAsync.
                if (toRender.Text is { } openingToRender)
                {
                    audio = await _announcer.PrepareReminderAsync(openingToRender);
                }

                if (toRender.Introductions is { } introductionsToRender)
                {
                    introductionsAudio = await _announcer.PrepareReminderAsync(introductionsToRender.Text);
                }
            }

            cts.Token.ThrowIfCancellationRequested();

            // 17 Oct 2026, Bug Hunt 8, A04-2/A03-3: what the announcer is
            // still saying finishes before the jingle - the recap's last
            // line (EndRecapShow lets it finish) or the grid call StartRoundNow
            // queued just before this. The jingle used to land on top of
            // either. Cancelled by a second press or a call-off like the rest.
            // Not over a demo still running (the only race On Air opens over,
            // replacing it): its lap calls would keep the wait going.
            if (_race.State is not RaceState.Racing)
            {
                await _announcer.WaitForAudioAsync(SpokenWait(), cts.Token);
                cts.Token.ThrowIfCancellationRequested();
            }

            // The floor, asked again if it was not free at the press - the
            // recap called off by this press lets go of it as it winds down.
            if (!_onAirHasFloor)
            {
                _onAirHasFloor = ShowFloor.TryClaim(OnAirHoldsTheFloor);
            }

            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - the top of the race.");

            _btnOnAir.Text = OnAirCaption;

            // 2. The jingle, to its post.
            jingle = BroadcastImaging.Start(ImagingCue.TopOfRace);

            if (jingle is not null)
            {
                await jingle.AtPost.WaitAsync(cts.Token);
            }

            // 3. On the post: the bed comes up under the voice, the rest of
            //    the jingle crossfades into it, and the voice hits the post.
            //    The bed is done the moment the race is staged - see
            //    HandTheBedToTheStart.
            bed = ImagingBed.Start(ImagingCue.OpeningBed, _announcer, OpeningBedLevel, OpeningBedDuck);
            jingle?.CrossfadeOut();

            if (line is { } said)
            {
                if (said.Text is { } openingText)
                {
                    if (audio is not null)
                    {
                        _announcer.SpeakPrepared(audio, onAir: OnAirParts.ShowOpen);
                    }
                    else
                    {
                        _announcer.SpeakReminder(openingText, onAir: OnAirParts.ShowOpen);
                    }

                    // As Stage's own intro does: recorded as said, and its facts
                    // spent, only once the line has been queued.
                    _commentary?.IntroSpoken(openingText);
                    _commentary?.HistorySpoken(said.History);
                    _openingSaidByOnAir = true;
                }

                // Then the driver introductions - straight after the opening,
                // which ends by handing to them ("Let's meet the field").
                // Stage leaves them out - and their sponsor is counted, as
                // it is heard.
                if (said.Introductions is { } saidIntroductions)
                {
                    if (introductionsAudio is not null)
                    {
                        _announcer.SpeakPrepared(introductionsAudio, onAir: OnAirParts.DriverIntros);
                    }
                    else
                    {
                        _announcer.SpeakReminder(saidIntroductions.Text, onAir: OnAirParts.DriverIntros);
                    }

                    // WHY (20 Oct 2026): introductions written ahead are not
                    // recorded as written (remember: false), so they become the
                    // Announcer page's last line, and their wording is marked
                    // as used, only now they are actually said.
                    _commentary?.IntroSpoken(saidIntroductions.Text);

                    _introductionsSaidByOnAir = true;

                    if (!_announcer.Muted)
                    {
                        saidIntroductions.Count?.Invoke();
                    }
                }

                // Then pit lane: the throw to the co-host and the Team
                // Principal, LAST before staging.
                //
                // AFTER THE INTRODUCTIONS, NOT BEFORE THEM (20 Oct 2026). It
                // was opening, pit lane, introductions - so the opening said
                // "Let's meet the field" and the announcer threw to pit lane
                // instead, and "Before we go, let's head down to pit lane"
                // came before anything had been introduced. A broadcast sets
                // the race up, meets the field, and then goes to pit lane for
                // the last word before the start - which is what the throw
                // already says.
                //
                // Once On Air has tried for it, Stage leaves it out even
                // when it did not come back in time: the pit lane belongs
                // in front of the staging call, and late it is dropped
                // rather than heard after it.
                if (said.PrincipalTried)
                {
                    _principalSaidByOnAir = true;
                }

                if (said.Principal is { } principalPart)
                {
                    SayPrincipalLines(principalPart.Lines, principalPart.Focus);
                }

                // 4. The bumper, HITTING THE POST (17 Oct 2026): fired while
                //    the opening is still being said, so the bumper's own
                //    voice comes in on the opening's last word - no gap
                //    between the two. The bed dips the moment it starts, and
                //    the staging call comes in at the bumper's out. With no
                //    bumper this simply waits for the opening to finish, as
                //    it always did. See TalkUp.
                if (await BroadcastImaging.TalkUpAsync(ImagingCue.AfterOpening, _announcer, IntroShowLimit, cts.Token) is { } bumper)
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
            _btnOnAir.Text = OnAirCaption;
            cts.Dispose();

            // 17 Oct 2026, Bug Hunt 8, A03-1: and Start Next comes back.
            if (!IsDisposed && !Disposing)
            {
                UpdateNextUpBar();
            }
        }

        if (!stage || IsDisposed || Disposing)
        {
            _openingSaidByOnAir = false;
            _introductionsSaidByOnAir = false;
            _principalSaidByOnAir = false;
            jingle?.CrossfadeOut();
            bed?.Finish();
            return;
        }

        // A demo stages itself - see GoOnAirThenAsync.
        if (start is not null)
        {
            // 17 Oct 2026, Bug Hunt 8, A03-5/A04-9: a second press (or a
            // failure) during the jingle left it playing to its end under
            // the start. CrossfadeOut is safe to call twice.
            jingle?.CrossfadeOut();
            HandTheBedToTheStart(bed);

            // The demo stages inside start(), and its staging reads these
            // (ShowAtDemoStaging) so the opening and the introductions it
            // carried are not said twice (18 Oct 2026). Cleared AFTER, so a
            // demo that did not start leaves nothing for the next real Stage.
            try
            {
                start();
            }
            finally
            {
                _openingSaidByOnAir = false;
                _introductionsSaidByOnAir = false;
                _principalSaidByOnAir = false;
            }

            return;
        }

        // Not when the race has been staged some other way in the meantime -
        // or a warm-up opened (17 Oct 2026, Bug Hunt 8, A03-4), which the
        // hand-over's Stage would skip.
        if (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing || _race.IsWarmingUp)
        {
            _openingSaidByOnAir = false;
            _introductionsSaidByOnAir = false;
            _principalSaidByOnAir = false;
            jingle?.CrossfadeOut();
            bed?.Finish();
            return;
        }

        // 17 Oct 2026, Bug Hunt 8, A03-5/A04-9: see the demo branch above.
        jingle?.CrossfadeOut();
        HandTheBedToTheStart(bed);

        // The caller's own Stage when one was handed over - see
        // StartThroughOnAir - and a plain Stage press otherwise.
        if (handOver is not null)
        {
            handOver();
        }
        else
        {
            _actStage?.Invoke(false, null);
        }
    }

    /// <summary>
    /// The opening bed on its own - the imaging bed's level, set on Sounds ->
    /// Imaging with the rest of the imaging (17 Oct 2026). It used to read the
    /// Commentary page's show bed, which is the recap's music, not this.
    /// </summary>
    private static float OpeningBedLevel => BroadcastImaging.BedLevel;

    /// <summary>How far the opening bed drops under the voice - the imaging bed's own duck.</summary>
    private static float OpeningBedDuck => BroadcastImaging.BedDuck;

    /// <summary>
    /// The longest the opening bed may run after On Air hands over - a start
    /// that never reaches its countdown (a warm-up, a race director who is
    /// not ready) must not leave it looping under the rest of the night.
    /// </summary>
    private static readonly TimeSpan LongestOpeningBed = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Takes the opening bed away when the race is staged (16 Oct 2026,
    /// asked for from the floor: "the bed from the open should be done at
    /// staging, not later"). It used to run on under the staging call until
    /// the countdown; now the open is finished the moment Stage takes hold,
    /// and the staging call has the room. Any other move - a reset - or
    /// <see cref="LongestOpeningBed"/> ends it too.
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

        // Any move at all, staging included - see the summary.
        watcher = _ => End();

        _race.StateChanged += watcher;

        _ = Task.Delay(LongestOpeningBed).ContinueWith(_ => End(), TaskScheduler.Default);
    }

    /// <summary>
    /// Everything On Air says before staging (18 Oct 2026), in the order it
    /// is said: the opening, then the driver introductions, then the Team
    /// Principal with the throw to pit lane (20 Oct 2026 - the pit lane used
    /// to come before the introductions). Each part after the opening is
    /// null when it is not due or did not come back in time - Stage then
    /// does it as before.
    /// </summary>
    /// <param name="Text">
    /// The opening itself, or null when no round intro is due (or none came
    /// back) but the pit lane is (18 Oct 2026) - the pit lane still goes out
    /// after the jingle and before staging, never after the staging call.
    /// </param>
    /// <param name="PrincipalTried">
    /// A Team Principal was due and On Air asked for one - so the Stage it
    /// hands over to does not ask again after the staging call, whether or
    /// not it came back in time.
    /// </param>
    private sealed record OpeningRun(
        string? Text,
        PackedFacts? History,
        (string Text, Action? Count)? Introductions,
        (List<SpokenLine> Lines, string Focus)? Principal,
        bool PrincipalTried = false);

    /// <summary>
    /// The opening: the round intro for the race in the Round box, written
    /// exactly as Stage writes one - the written-ahead line when it is ready
    /// and still true, otherwise the model, inside the same budget. Null when
    /// no intro is due or none came back, which On Air reads as "no opening".
    ///
    /// Read on the UI thread up to the model call, as Stage's is - see
    /// ReadIntroFacts.
    /// </summary>
    private async Task<OpeningRun?> WriteOpeningAsync(RoundDefinition? forRound, CancellationToken ct)
    {
        try
        {
            if (_commentary is null || !_commentary.IsAvailable)
            {
                return null;
            }

            var round = forRound ?? _activeRound?.Invoke();

            if (round is null)
            {
                return null;
            }

            if (!_settings.CommentaryRaceIntros)
            {
                return await PitLaneOnlyAsync(round, ct);
            }

            if (_settings.CommentaryIntrosMainsOrRivalriesOnly
                && round.Kind != RoundKind.Main
                && !_commentary.HasRivalryAmong(round.AssignedTransponders))
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [imaging] No opening for {round.RoundLabel} - intros are set to mains and rivalries only, "
                    + "and this race is neither.");

                return await PitLaneOnlyAsync(round, ct);
            }

            var (clubNight, form, stake, scene) = ReadIntroFacts(round);

            // THE DRIVER INTRODUCTIONS, written alongside the intro rather
            // than after it (18 Oct 2026), so they are rendered with the
            // opening and follow it with no wait. Started here, on the UI
            // thread where their facts are read, and awaited below.
            var introductions = IntroductionsForOpeningAsync(round, ct);

            // And the Team Principal (18 Oct 2026), after the introductions
            // (20 Oct 2026) - written alongside both, and rendered as it
            // is written, so it costs the run no wait of its own.
            var principalJob = PreparePrincipal(round);
            var principal = principalJob is null
                ? Task.FromResult<List<SpokenLine>?>(null)
                : Task.Run(() => WritePrincipalLinesAsync(principalJob, ct));

            PackedFacts? history = null;
            string? answer;

            // Whether the driver introductions follow this opening - when they
            // do, the opening leaves the roll call to them (18 Oct 2026). Not
            // due is answered at once, as a finished task with nothing in it.
            var introductionsFollow = !(introductions.IsCompletedSuccessfully && introductions.Result is null);

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
                    scene: scene,
                    introductionsFollow: introductionsFollow);
            }

            if (string.IsNullOrWhiteSpace(answer))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] No opening - no intro came back in time.");

                // The pit lane already being written still goes out before
                // staging (18 Oct 2026) - see OpeningRun.Text.
                return await PitLaneFrom(principalJob, principal);
            }

            // Never longer than their own budget, which runs from when they
            // were started - alongside the intro, so usually no wait at all.
            var withIntroductions = await introductions;

            if (withIntroductions is not null)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] The driver introductions go out at the end of the opening run, rendered with it.");
            }

            List<SpokenLine>? principalLines = null;

            try
            {
                principalLines = await principal;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Team Principal skipped - {ex.Message}");
            }

            if (principalLines is { Count: > 0 })
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] The Team Principal goes out after the driver introductions, last before staging.");
            }

            return new OpeningRun(
                answer,
                history,
                withIntroductions,
                principalLines is { Count: > 0 } && principalJob is not null ? (principalLines, principalJob.Focus) : null,
                PrincipalTried: principalJob is not null);
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

    /// <summary>
    /// The pit lane on its own (18 Oct 2026), for a race with no opening -
    /// race intros off, or kept for mains and rivalries and this race is
    /// neither. It used to be left to Stage, which put it after the staging
    /// call; now it goes out after the jingle and before staging, like it
    /// does after an opening. Null when no Team Principal is due. On the UI
    /// thread up to the model call.
    /// </summary>
    private async Task<OpeningRun?> PitLaneOnlyAsync(RoundDefinition round, CancellationToken ct)
    {
        var job = PreparePrincipal(round);

        if (job is null)
        {
            return null;
        }

        return await PitLaneFrom(job, Task.Run(() => WritePrincipalLinesAsync(job, ct)));
    }

    /// <summary>An <see cref="OpeningRun"/> carrying the pit lane alone, once its lines are written - null when none was due.</summary>
    private async Task<OpeningRun?> PitLaneFrom(PrincipalJob? job, Task<List<SpokenLine>?> writing)
    {
        if (job is null)
        {
            return null;
        }

        List<SpokenLine>? lines = null;

        try
        {
            lines = await writing;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Team Principal skipped - {ex.Message}");
        }

        if (lines is { Count: > 0 })
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] No opening for this race - the throw to pit lane goes out after the jingle, before staging.");
        }

        return new OpeningRun(null, null, null, lines is { Count: > 0 } ? (lines, job.Focus) : null, PrincipalTried: true);
    }
}

/// <summary>
/// The announcer saying his own name (16 Oct 2026) - a line into each race's
/// opening, and at the end of the line that takes the show into the stop set.
///
/// The name is the one typed as "Announcer's name" on the Commentary page
/// (AppSettings.RecapAnnouncerName - the setting's name is older than this).
/// It is the race announcer's, NOT the recap show's: the recap has a host of
/// its own.
///
/// WRITTEN IN HERE, NOT ASKED OF THE MODEL. A model asked to "say your name
/// near the top" does it most of the time and somewhere different each time;
/// a station's presenter says it in the same place every time. Blank name:
/// nothing changes. A line that already has the name in it (the model used
/// it on its own) is left alone, so he is never introduced twice.
/// </summary>
internal static class AnnouncerIntro
{
    /// <summary>
    /// The opening with "I'm &lt;name&gt;." put in after its first sentence -
    /// "Welcome back to the track! I'm Dave Marsh. Heat three is..." A
    /// one-sentence opening gets it on the end.
    /// </summary>
    internal static string IntoOpening(string text, string? name)
    {
        var who = Who(name);

        if (who.Length == 0 || string.IsNullOrWhiteSpace(text) || Names(text, who))
        {
            return text;
        }

        var said = $"I'm {who}.";
        var cut = FirstSentenceEnds(text);

        return cut < 0
            ? $"{text.TrimEnd()} {said}"
            : $"{text[..cut].TrimEnd()} {said} {text[cut..].TrimStart()}";
    }

    /// <summary>
    /// The line into the stop set with the announcer's sign-off on the end:
    /// "This is Dave Marsh - back after the break." With no break to go to
    /// (the commercial break off, or no sponsors up) it is "This is Dave
    /// Marsh - stay with us." - never his bare name as the last words
    /// (16 Oct 2026): a name as the very end of a line is the word a voice
    /// clips, and a sign-off that stops dead on it sounds cut off, which is
    /// what the room heard. A closing phrase puts the name safely inside the
    /// line and gives the bumper something to hit.
    /// </summary>
    internal static string IntoStopSet(string line, string? name, bool breakFollows)
    {
        var who = Who(name);

        if (who.Length == 0 || string.IsNullOrWhiteSpace(line) || Names(line, who))
        {
            return line;
        }

        var signOff = breakFollows ? $"This is {who} - back after the break." : $"This is {who} - stay with us.";

        return $"{line.TrimEnd()} {signOff}";
    }

    private static string Who(string? name) => (name ?? string.Empty).Trim().TrimEnd('.', '!', '?', ',');

    private static bool Names(string text, string who) => text.Contains(who, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where the first sentence ends - just past its full stop, exclamation
    /// or question mark (and any closing quote), where the next sentence
    /// starts. -1 when the whole thing is one sentence. A stop with no space
    /// after it - "4.2 seconds" - is not the end of a sentence.
    /// </summary>
    private static int FirstSentenceEnds(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?'))
            {
                continue;
            }

            var after = i + 1;

            while (after < text.Length && text[after] is '"' or '\'' or '”' or '’' or ')')
            {
                after++;
            }

            if (after >= text.Length)
            {
                return -1;
            }

            if (char.IsWhiteSpace(text[after]))
            {
                return after;
            }
        }

        return -1;
    }
}
