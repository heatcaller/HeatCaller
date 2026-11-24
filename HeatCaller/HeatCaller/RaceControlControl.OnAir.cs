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
///
/// NO BUTTON OF ITS OWN (25 Oct 2026, asked for by Asher: "use the press
/// and hold animation to denote the writing and voicing delay instead of
/// the on air button which you can remove"). On Air is started by every
/// start that goes through it (above) and by its key - O then, 0 since 1 Nov
/// 2026, held to skip to Stage (see OnAirKey). While the opening is written and voiced, the strip under
/// the flag row shows it the way it shows a hold - what is being done, a bar
/// filling, and how long is left (ShowOnAirWork); while the show is out it
/// says so in cyan (ShowOnAirLive).
/// </summary>
public sealed partial class RaceControlControl
{
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
    /// "Start races: Stage | On Air" - see <see cref="StartWithOnAir"/>. At
    /// the foot of the board. A segmented switch since 9 Nov 2026, in place
    /// of the "Start races On Air" tick box, so both choices are on show.
    /// </summary>
    // "Start:" rather than "Start races:" (15 Nov 2026, asked for by Asher), so
    // the foot of the board fits on one row at 1280 wide.
    private readonly SegmentedSwitch _chkStartWithOnAir = new("Start:", "Stage", "On Air") { Compact = true };

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

            if (_chkStartWithOnAir.On != value)
            {
                _chkStartWithOnAir.On = value;
            }
        }
    }

    /// <summary>"Warm-up first: Off | On" (14 Nov 2026), beside Start races at the foot of the board - see <see cref="StartWithWarmUp"/>.</summary>
    // "Warm-up:" rather than "Warm-up first:" (15 Nov 2026), so the foot of
    // the board fits on one row at 1366 wide - see BuildLayout.
    private readonly SegmentedSwitch _swWarmUpFirst = new("Warm-up:", "Off", "On") { Compact = true };

    /// <summary>
    /// Whether every start opens the warm-up flag first (14 Nov 2026): Start
    /// Next, Auto start, the program's Start, F1, the phone, Stage and Quick
    /// Race - then Stage, then the countdown. Through On Air too: On Air's
    /// opening, then the warm-up. Remembered between launches -
    /// AppSettings.RaceStartWithWarmUp.
    /// </summary>
    internal bool StartWithWarmUp
    {
        get => _settings.RaceStartWithWarmUp;
        set
        {
            if (_settings.RaceStartWithWarmUp != value)
            {
                _settings.RaceStartWithWarmUp = value;
                _settings.Save();
            }

            if (_swWarmUpFirst.On != value)
            {
                _swWarmUpFirst.On = value;
            }
        }
    }

    /// <summary>The lengths on Warm-up first's right-click menu, in seconds.</summary>
    private static readonly int[] WarmUpLengthChoices = { 60, 90, 120, 180, 300, 600 };

    /// <summary>
    /// Right-click "Warm-up first" (15 Nov 2026, asked for by Asher): how long
    /// the warm-up runs. A length picked here is used by the switch and the
    /// Warm-up button in place of the format's own, and switches Warm-up first
    /// on - picking a length is asking for a warm-up. "The race format's"
    /// goes back to the format's (two minutes when it has none).
    /// </summary>
    private ContextMenuStrip BuildWarmUpLengthMenu()
    {
        var menu = new ContextMenuStrip();
        AppTheme.StyleContextMenu(menu);

        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();

            var chosen = _settings.RaceWarmUpFirstSeconds;
            var formatSeconds = EffectiveFormat(out _).WarmUpSeconds;
            var formatWords = formatSeconds > 0
                ? RaceFormat.DescribeWarmUp(formatSeconds)
                : $"none, so {RaceFormat.DescribeWarmUp(WarmUpWhenFormatHasNone)}";

            menu.Items.Add(new ToolStripMenuItem($"Warm-up length: the race format's ({formatWords})", null, (_, _) => SetWarmUpLength(0))
            {
                Checked = chosen <= 0,
            });

            menu.Items.Add(new ToolStripSeparator());

            foreach (var seconds in WarmUpLengthChoices)
            {
                var pick = seconds;
                menu.Items.Add(new ToolStripMenuItem($"Warm-up length: {RaceFormat.DescribeWarmUp(seconds)}", null, (_, _) => SetWarmUpLength(pick))
                {
                    Checked = chosen == seconds,
                });
            }
        };

        return menu;
    }

    /// <summary>Saves the warm-up length picked on the switch's menu; 0 goes back to the format's.</summary>
    private void SetWarmUpLength(int seconds)
    {
        _settings.RaceWarmUpFirstSeconds = Math.Clamp(seconds, 0, RaceFormat.MaxWarmUpSeconds);
        _settings.Save();

        if (seconds > 0)
        {
            StartWithWarmUp = true;
        }

        StatusMessage?.Invoke(seconds > 0
            ? $"{AppTime.LogStampNow()}  [race] Warm-up first is on - {RaceFormat.DescribeWarmUp(seconds)} of free laps before every race from now on."
            : $"{AppTime.LogStampNow()}  [race] The warm-up is the race format's own length again.");
    }

    /// <summary>What On Air calls itself while it holds the broadcast floor - see ShowFloor.</summary>
    private const string OnAirHoldsTheFloor = "On Air opening the next race";

    /// <summary>What the strip says at its right while On Air works: how On Air is skipped, now there is no button.</summary>
    internal const string OnAirSkipNote = "hold 0 to skip to Stage";

    /// <summary>What holding 0 does, as the strip finishes "Keep holding to …".</summary>
    internal const string OnAirHoldDoing = "skip On Air and stage now";

    /// <summary>
    /// The 0 key (1 Nov 2026, asked for by Asher: "i prefer it to be 0
    /// instead of o as that's confusing and I think you should have to press
    /// and hold to skip"). Made on first use - see OnAirKey.
    /// </summary>
    private KeyHoldToAct? _onAirKey;

    /// <summary>
    /// ON AIR'S KEY IS 0, AND SKIPPING IT IS A HOLD (1 Nov 2026). It was O,
    /// which was confusing - the Big Screen's own O switches its ON AIR band
    /// - and a second tap skipped the whole opening, so a stray press cut the
    /// show short. Now a tap of 0 starts On Air, as O did, and while On Air
    /// runs 0 has to be held for a second (HoldToAct.HoldFor, the same as
    /// Red, Checkered and Reset) to skip to Stage: the On Air bar fills as it
    /// is held, and says so if it is let go too soon. The press that starts
    /// On Air never becomes the skip, however long it is held - the skip
    /// takes a fresh press (KeyHoldGesture ignores Windows' repeats).
    /// </summary>
    private KeyHoldToAct OnAirKey => _onAirKey ??= MakeOnAirKey();

    private KeyHoldToAct MakeOnAirKey()
    {
        var key = new KeyHoldToAct(OnAirHoldDoing, () => _onAirCts is not null && !IsDisposed && !Disposing,
            SkipOnAirToStage, Keys.D0, Keys.NumPad0);

        key.Changed += h => _holdStrip.Follow(h);
        key.CalledOff += _ => _holdStrip.CalledOff();
        return key;
    }

    /// <summary>0 came down (MainForm.Keyboard): a tap starts On Air; while it runs, the start of a hold to skip it. See OnAirKey.</summary>
    internal void OnAirKeyDown()
    {
        var running = _onAirCts is not null;

        // False for Windows repeating a key already down.
        if (!OnAirKey.KeyDown(hold: running))
        {
            return;
        }

        if (!running)
        {
            PressOnAir();
        }
    }

    /// <summary>0 came up, or the window lost the keyboard: a hold let go early does nothing. Safe at any time.</summary>
    internal void OnAirKeyUp() => _onAirKey?.KeyUp();

    /// <summary>
    /// 0 held for long enough: On Air's second press, which skips to Stage.
    /// Nothing when On Air has finished in the meantime - a press then would
    /// start a new one.
    /// </summary>
    private void SkipOnAirToStage()
    {
        if (_onAirCts is null || IsDisposed || Disposing)
        {
            return;
        }

        PressOnAir();
    }

    /// <summary>
    /// F1 pressed while On Air runs (1 Nov 2026). It used to skip to Stage, a
    /// tap doing what 0 now has to be held for - so now it only says how.
    /// </summary>
    internal void HintHoldToSkip() =>
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air is going out - hold 0 for a second to skip to Stage.");

    /// <summary>Wires On Air's switch and timers. Called from BuildLayout. The button went on 25 Oct 2026.</summary>
    private void BuildOnAirButton()
    {
        _chkStartWithOnAir.On = _settings.RaceStartWithOnAir;
        _chkStartWithOnAir.OnChanged += (_, _) => StartWithOnAir = _chkStartWithOnAir.On;

        var tip = new ToolTip();
        AppTheme.StyleToolTip(tip, keep: true);
        tip.SetToolTip(_chkStartWithOnAir,
            "On Air: Start Next, the program's Start, F1, the phone and the day schedule all go through On Air first: "
            + "the soundscape's jingle or sweeper, the opening, a bumper, then Stage. 0 starts it by hand; while it runs, "
            + "hold 0 for a second to skip to Stage. Stage: they stage straight away. The Stage button always stages straight away.");

        _swWarmUpFirst.On = _settings.RaceStartWithWarmUp;
        _swWarmUpFirst.OnChanged += (_, _) => StartWithWarmUp = _swWarmUpFirst.On;
        _swWarmUpFirst.ContextMenuStrip = BuildWarmUpLengthMenu();
        tip.SetToolTip(_swWarmUpFirst,
            "On: every race starts with the warm-up flag - free laps, not counted, for the format's warm-up "
            + "(two minutes if it has none, or the length you right-click to choose) - then Stage and the countdown. "
            + "On Air, when it is on, goes first. Off: only a format with a warm-up has one. "
            + "Press Warm-up or Stage during it to skip the rest.");

        // The strip's bar and countdown while the opening is got ready.
        _onAirProgressTimer.Interval = 150;
        _onAirProgressTimer.Tick += (_, _) => PaintOnAirProgress();

        // The next opening voiced ahead, between races. See VoiceAheadTick.
        _voiceAheadTimer.Interval = 2000;
        _voiceAheadTimer.Tick += (_, _) => VoiceAheadTick();
        _voiceAheadTimer.Start();

        Disposed += (_, _) =>
        {
            _onAirProgressTimer.Dispose();
            _voiceAheadTimer.Dispose();
        };
    }

    // --- Getting ready, shown on the button (21 Oct 2026) -------------------
    //
    // ON THE STRIP UNDER THE FLAG ROW since 25 Oct 2026, when the button
    // went: the same steps, fill and countdown, drawn as a hold is drawn
    // ("On Air: voicing the opening…", the bar, "about 8s"). What follows is
    // how it worked on the button, and why - all of it still true of the strip.
    //
    // Pressing On Air used to put "Waiting..." on the button for as long as
    // the opening took to write and voice - twenty seconds on a slow night,
    // with nothing to say anything was happening. Now the button fills with
    // its red from the left as the work gets done, and its caption counts
    // down to the jingle and names the step it is on: "8s · Voicing".
    //
    // The countdown is learned, not guessed: every open is timed from the
    // press to the jingle and the last few are kept (AppSettings.
    // OnAirReadyHistoryMs), so it is this club's model, voice and connection
    // being timed. Until there is a first open to go on - or when one runs
    // past its estimate - the caption just names the step ("Voicing...").
    //
    // The steps are the real ones, in order. The fill never runs ahead of
    // the step that is actually under way: an estimate that says 90% while
    // the model has not answered yet would be a lie the room could see.

    /// <summary>What On Air is doing while the race director waits for the jingle.</summary>
    private enum OnAirStep
    {
        Writing,
        VoicingOpening,
        VoicingIntros,
        Finishing,
    }

    /// <summary>The share of the fill each step takes up, from and to.</summary>
    private static (float From, float To) StepSpan(OnAirStep step) => step switch
    {
        OnAirStep.Writing => (0f, 0.5f),
        OnAirStep.VoicingOpening => (0.5f, 0.8f),
        OnAirStep.VoicingIntros => (0.8f, 0.95f),
        _ => (0.95f, 0.99f),
    };

    /// <summary>The step, as the strip under the flag row says it.</summary>
    private static string StepWords(OnAirStep step) => step switch
    {
        OnAirStep.Writing => "On Air: writing the opening…",
        OnAirStep.VoicingOpening => "On Air: voicing the opening…",
        OnAirStep.VoicingIntros => "On Air: voicing the driver intros…",
        _ => "On Air: almost ready…",
    };

    /// <summary>What the strip says while the show is out, before the race is staged.</summary>
    internal const string OnAirLiveWords = "On air - the show is going out";

    /// <summary>On Air is opening a demo, which starts itself at the end rather than being staged.</summary>
    private bool _onAirForDemo;

    /// <summary>
    /// A flag pressed while On Air is opening a DEMO (21 Oct 2026): skips the
    /// rest of the opening and starts the demo, exactly as pressing On Air
    /// again does. True when it did, and the flag should do nothing more.
    ///
    /// It used to call On Air off - any start counts as the race being moved
    /// on - so the demo never started, and the Green that did it started a
    /// REAL race in its place: no simulator, no cars, and the decoder (not
    /// connected for a demo) counting nothing. A demo stages, counts down
    /// and goes green by itself, so the flag's own job is done by starting it.
    ///
    /// Only for a demo. For a real race a flag pressed during On Air still
    /// does what it says, as it always has.
    /// </summary>
    private bool SkipOnAirToTheDemo(string flag)
    {
        if (_onAirCts is not { } running || !_onAirForDemo)
        {
            return false;
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] {flag} pressed while On Air was opening the demo - "
            + "skipping the rest of the opening and starting the demo, which stages and goes green by itself.");

        _onAirStageNow = true;

        // So the phone is told what really happened, not "Red flag out." (bug hunt 24).
        if (_runningRemoteCommand)
        {
            _remoteSkippedOnAirToDemo = true;
        }

        // CancelAsync, not Cancel (7 Nov 2026, bug hunt 14) - see the same
        // in RunOnAirAsync: Cancel ran On Air's continuation inline, which
        // staged the race, and the SilenceOnAir below then wiped the call
        // that staging had just queued.
        try
        {
            _ = running.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // On Air had already finished.
        }

        SilenceOnAir($"On Air skipped to the demo ({flag} pressed)");
        return true;
    }

    /// <summary>
    /// What On Air has put on air stops NOW, because On Air is being cut
    /// short (1 Nov 2026, asked for by Asher: "hitting o during voice still
    /// doesnt end the voice it keeps talking" - On Air's key is 0 since).
    ///
    /// WHY IT KEPT TALKING. On Air hands its run to the announcer's queue as
    /// it goes - the opening, the driver introductions, the throw to pit lane
    /// and the Team Principal - and every one of them is a Reminder, which the
    /// queue treats as priority. Cancelling On Air only stopped it adding more;
    /// everything already queued played on, and the Stage it handed over to
    /// queued the staging call behind the lot (NotifyRaceStaging only adds to
    /// the back, and staging's purge keeps priority lines). So O, which the
    /// strip says skips to Stage, left the room hearing the rest of the
    /// opening, the introductions and pit lane before the staging call.
    ///
    /// THE SAME CUT AS RESET (AnnouncementService.SilenceNow): the line in
    /// the speakers, every queued line and sound, a line still being written
    /// or voiced, and the long reads' feeders. Safe here because On Air only
    /// runs between races, or over a demo it is replacing - there is no flag
    /// call or release in the queue for it to swallow. It also takes what
    /// the announcer was still finishing when On Air started (the recap's
    /// last sentence, the grid call), which On Air waits for before its
    /// jingle: skipping On Air skips that wait too.
    ///
    /// Called by every way On Air is cut short - 0 held (and the phone's
    /// Stage pressed again, which comes the same way), a flag or Stage during a
    /// demo's On Air (SkipOnAirToTheDemo), a demo started while On Air is
    /// opening some other race (RunOnAirAsync, 10 Nov 2026), and Stage, Countdown, Green, the
    /// checkered, Reset or a warm-up by hand (CancelAutoStart) - and always
    /// BEFORE the Stage that follows, so the staging call is queued after the
    /// cut and is the next thing heard. Not by On Air ending normally: its
    /// hand-over runs once _onAirCts is cleared, with nothing of its own left
    /// to say.
    /// </summary>
    private void SilenceOnAir(string why)
    {
        _announcer.SilenceNow(why);
    }


    /// <summary>On Air's own red, for the strip while the opening is got ready.</summary>
    private static readonly Color OnAirRed = Color.FromArgb(200, 32, 44);

    /// <summary>The cyan On Air turns while the show is live (21 Oct 2026).</summary>
    private static readonly Color OnAirLiveCyan = Color.FromArgb(0x2E, 0xE6, 0xFF);

    /// <summary>
    /// Cyan while the show is live (21 Oct 2026) - from the jingle until the
    /// race is staged - so a glance at the flag row says the room is on air.
    /// On the strip under the row since 25 Oct 2026, with no bar: nothing is
    /// being waited for. Gone the moment it is over.
    /// </summary>
    private void ShowOnAirLive(bool live)
    {
        // What the card says is the segment on air since 26 Oct 2026, not
        // OnAirLiveWords, and it stays up while anything is going out - see
        // UpdateOnAirBar, which this hands to.
        _onAirLive = live;
        UpdateOnAirBar();
    }

    /// <summary>On Air is live - between the jingle and the staging. See ShowOnAirLive and UpdateOnAirBar.</summary>
    private bool _onAirLive;

    /// <summary>How many past opens the countdown averages.</summary>
    private const int OnAirReadyHistoryKept = 5;

    private readonly System.Windows.Forms.Timer _onAirProgressTimer = new();

    private bool _onAirProgressShowing;
    private long _onAirPressTicks;
    private long _onAirStepTicks;
    private OnAirStep _onAirStep;
    private long? _onAirEstimateMs;

    /// <summary>The press: the fill starts empty and the countdown from the average of the last few opens.</summary>
    private void BeginOnAirProgress()
    {
        var history = _settings.OnAirReadyHistoryMs;
        _onAirEstimateMs = history is { Count: > 0 } ? (long)history.Average() : null;

        _onAirPressTicks = Environment.TickCount64;
        _onAirStepTicks = _onAirPressTicks;
        _onAirStep = OnAirStep.Writing;
        _onAirProgressShowing = true;

        PaintOnAirProgress();
        _onAirProgressTimer.Start();
    }

    /// <summary>On to the next step - the fill moves up to where that step starts.</summary>
    private void SetOnAirStep(OnAirStep step)
    {
        if (!_onAirProgressShowing || step <= _onAirStep && step != OnAirStep.Writing)
        {
            return;
        }

        _onAirStep = step;
        _onAirStepTicks = Environment.TickCount64;
        PaintOnAirProgress();
    }

    /// <summary>The fill and the caption, from the step under way and the time gone.</summary>
    private void PaintOnAirProgress()
    {
        if (!_onAirProgressShowing || IsDisposed || Disposing)
        {
            return;
        }

        var now = Environment.TickCount64;
        var elapsed = now - _onAirPressTicks;
        var (from, to) = StepSpan(_onAirStep);

        float fill;

        if (_onAirEstimateMs is { } estimate && estimate > 0)
        {
            // The clock's share of the estimate, kept inside the step under way.
            fill = Math.Clamp((float)elapsed / estimate, from, to - 0.01f);
        }
        else
        {
            // No estimate yet: creep across the step, slowing as it goes, so
            // it always moves but never claims the step is done.
            var inStep = (now - _onAirStepTicks) / 4000.0;
            fill = from + (to - from) * 0.9f * (float)(1 - Math.Exp(-inStep));
        }

        var left = _onAirEstimateMs is { } est ? (int)Math.Ceiling((est - elapsed) / 1000.0) : 0;

        // On the strip under the flag row, as a hold is shown (25 Oct 2026):
        // the step, the bar, and the seconds to the jingle when there is an
        // estimate to count from.
        _holdStrip.ShowWork(StepWords(_onAirStep), fill,
            left >= 1 && left <= 88 ? $"about {left}s · {OnAirSkipNote}" : OnAirSkipNote, OnAirRed);
    }

    /// <summary>
    /// The jingle is going out, or On Air was called off: the button goes
    /// back to itself. Timed only when it reached the jingle - a call-off
    /// says nothing about how long an open takes.
    /// </summary>
    private void EndOnAirProgress(bool reachedTheJingle)
    {
        if (!_onAirProgressShowing)
        {
            return;
        }

        _onAirProgressShowing = false;
        _onAirProgressTimer.Stop();

        // Back to the On Air bar as it stands: the show going out, or rest.
        UpdateOnAirBar();

        if (!reachedTheJingle)
        {
            return;
        }

        var took = Environment.TickCount64 - _onAirPressTicks;

        try
        {
            var history = _settings.OnAirReadyHistoryMs ??= new List<long>();
            history.Add(took);

            while (history.Count > OnAirReadyHistoryKept)
            {
                history.RemoveAt(0);
            }

            _settings.Save();
        }
        catch (Exception ex)
        {
            // Only the countdown's memory - never worth stopping the show.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - couldn't remember how long that open took ({ex.Message}).");
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air was ready in {took / 1000.0:0.0}s.");
    }

    // --- The next opening, voiced ahead (21 Oct 2026) -----------------------
    //
    // The words of the next race's opening and its driver introductions are
    // already written ahead when a result is filed (WrittenAhead and the
    // introductions held in RaceControlControl.Intros). What was left for On
    // Air to wait on was turning them into the announcer's voice - a render
    // each, seconds apiece on a club's connection.
    //
    // So between races, once the broadcast floor is free (the recap and the
    // post-race show are done), those same words are rendered into the voice
    // and kept. At the press, a line On Air is about to voice that matches
    // one already rendered - word for word - is played from what was kept,
    // and On Air goes to the jingle with nothing to wait for.
    //
    // Matched on the exact words, so it can never play the wrong line: a
    // different round, a regrid, a correction that rewrote the intro - any of
    // them gives different words, which miss, and On Air voices them itself
    // as it always did. A render nobody uses costs a render and nothing more.
    // Nothing is taken from the written-ahead store here; it is only looked
    // at (WrittenAhead.PeekLine).

    private readonly System.Windows.Forms.Timer _voiceAheadTimer = new();

    /// <summary>Lines already in the announcer's voice, by their exact words.</summary>
    /// <remarks>
    /// WHY (22 Oct 2026, bug hunt 9): each take now carries whether the free
    /// Kokoro voice may have made it. The voice signature is built from the
    /// saved settings only, so a take rendered while ElevenLabs was resting
    /// looked current once ElevenLabs answered again, and On Air opened in
    /// Kokoro with the rest of the show in the club's voice. A take marked
    /// StandIn is used only while Kokoro is still standing in.
    /// </remarks>
    private readonly Dictionary<string, (AnnouncementService.PreparedSpeech Take, bool StandIn)> _voicedAhead = new(StringComparer.Ordinal);

    /// <summary>
    /// True when Kokoro is standing in for the chosen voice right now - a
    /// render now may come back in the free voice (22 Oct 2026, bug hunt 9).
    /// </summary>
    private bool KokoroStandingIn() => _announcer.DescribeStandIn() is not null;

    /// <summary>Lines a render has been tried for, so a failed one is not tried every two seconds.</summary>
    private readonly HashSet<string> _voiceAheadTried = new(StringComparer.Ordinal);

    /// <summary>
    /// Lines whose stand-in take has already been dropped once to be voiced
    /// again (22 Oct 2026, bug hunt 9 review): a line Kokoro stands in for on
    /// every try, with no rest, would otherwise be dropped and rendered again
    /// every two seconds. One retry each; On Air renders it at the press.
    /// </summary>
    private readonly HashSet<string> _voiceAheadRetried = new(StringComparer.Ordinal);

    /// <summary>
    /// The voice the lines in _voicedAhead were rendered in (22 Oct 2026, bug
    /// hunt). They were matched on their words alone, so a voice or engine
    /// changed on the Voice tab between races still had the opening go out in
    /// the old voice. Any change to the voice settings drops them.
    /// </summary>
    private string _voicedAheadIn = string.Empty;

    /// <summary>Which voice a render now would be in - see AnnouncementService.CurrentVoiceSignature.</summary>
    private string VoiceSignature() => _announcer.CurrentVoiceSignature;

    /// <summary>Forgets what was voiced ahead when the voice has changed since. True when it had.</summary>
    private bool ForgetVoicedAheadIfTheVoiceChanged()
    {
        var now = VoiceSignature();

        if (string.Equals(now, _voicedAheadIn, StringComparison.Ordinal))
        {
            return false;
        }

        _voicedAhead.Clear();
        _voiceAheadTried.Clear();
        _voiceAheadRetried.Clear();
        _voicedAheadIn = now;
        return true;
    }

    /// <summary>The render under way, if any - one at a time, so it never crowds the voice.</summary>
    private (string Text, Task<AnnouncementService.PreparedSpeech?> Render, bool StandInAtStart)? _voicingAhead;

    /// <summary>Every two seconds: renders the next line On Air will want, when nothing else needs the voice.</summary>
    private void VoiceAheadTick()
    {
        if (IsDisposed || Disposing || _onAirCts is not null || _voicingAhead is not null)
        {
            return;
        }

        // Only between races, with the floor free and the voice on. A
        // render during a race or under the recap would compete with the
        // lines actually being said.
        //
        // WHY (23 Oct 2026, bug hunt 10): with "Start races On Air" unticked
        // nothing ever plays these takes - every start goes straight to Stage,
        // and Stage voices no opening or introductions - yet each filed or
        // corrected result paid for two renders (ElevenLabs characters) that
        // were thrown away. Now nothing is voiced ahead while the box is
        // unticked; a manual On Air press still renders at the press.
        if (!StartWithOnAir
            || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing
            || _race.IsWarmingUp
            || _race.IsPracticeMode
            || ShowFloor.Busy
            || _announcer.Muted
            || _commentary is not { IsAvailable: true })
        {
            return;
        }

        var wanted = new List<string>(2);

        if (_writtenAhead.PeekLine() is { Length: > 0 } opening)
        {
            wanted.Add(AnnouncerIntro.IntoOpening(opening, _settings.RecapAnnouncerName));
        }

        lock (_introductionsAheadLock)
        {
            if (_introductionsAhead is { } held && held.AsOf == _introductionsAheadVersion && held.Text.Length > 0)
            {
                wanted.Add(held.Text);
            }
        }

        ForgetVoicedAheadIfTheVoiceChanged();

        // What is no longer wanted goes - its words have been replaced or used.
        foreach (var old in _voicedAhead.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _voicedAhead.Remove(old);
        }

        _voiceAheadTried.RemoveWhere(k => !wanted.Contains(k));
        _voiceAheadRetried.RemoveWhere(k => !wanted.Contains(k));

        // WHY (22 Oct 2026, bug hunt 9): a take Kokoro may have made sat here
        // after ElevenLabs came back, so it was never voiced ahead again and
        // On Air had to render it at the press. Once Kokoro is no longer
        // standing in, such a take goes and is voiced ahead again, in the
        // voice the show will be in.
        if (!KokoroStandingIn())
        {
            foreach (var stale in _voicedAhead.Where(e => e.Value.StandIn).Select(e => e.Key).ToList())
            {
                _voicedAhead.Remove(stale);

                if (_voiceAheadRetried.Add(stale))
                {
                    _voiceAheadTried.Remove(stale);
                }
            }
        }

        var next = wanted.FirstOrDefault(t => !_voicedAhead.ContainsKey(t) && !_voiceAheadTried.Contains(t));

        if (next is null)
        {
            return;
        }

        _voiceAheadTried.Add(next);

        // Read before the render starts: a rest that ends during it still
        // means the take may be in the free voice.
        var standInAtStart = KokoroStandingIn();
        var render = _announcer.PrepareReminderAsync(next);
        _voicingAhead = (next, render, standInAtStart);
        _ = KeepVoicedAheadAsync(next, render, _voicedAheadIn, standInAtStart);
    }

    private async Task KeepVoicedAheadAsync(string text, Task<AnnouncementService.PreparedSpeech?> render, string voice, bool standInAtStart)
    {
        try
        {
            if (await render is { } ready && string.Equals(voice, VoiceSignature(), StringComparison.Ordinal))
            {
                // Stand-in either side of the render marks the take - see _voicedAhead.
                // ready.StandIn (22 Oct 2026) also catches Kokoro stepping in for this one line without a rest.
                _voicedAhead[text] = (ready, ready.StandIn || standInAtStart || KokoroStandingIn());
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] Voiced part of the next opening ahead - On Air will have less to wait for.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] Couldn't voice the next opening ahead ({ex.Message}). On Air will voice it when pressed.");
        }
        finally
        {
            _voicingAhead = null;
        }
    }

    /// <summary>
    /// A line On Air is about to say, in the announcer's voice: the one
    /// voiced ahead when the words match exactly, the render already under
    /// way for those words when there is one, otherwise rendered now as it
    /// always was.
    /// </summary>
    private async Task<AnnouncementService.PreparedSpeech?> VoiceForOnAirAsync(string text)
    {
        // Never a take in a voice that has since been changed.
        var voiceChanged = ForgetVoicedAheadIfTheVoiceChanged();

        // A take Kokoro may have made is used only while Kokoro is still
        // standing in (22 Oct 2026, bug hunt 9) - otherwise it is rendered
        // again below, in the voice the rest of the show will be in.
        if (_voicedAhead.Remove(text, out var ready) && (!ready.StandIn || KokoroStandingIn()))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - that line was voiced ahead.");
            return ready.Take;
        }

        if (!voiceChanged && _voicingAhead is { } inFlight && string.Equals(inFlight.Text, text, StringComparison.Ordinal))
        {
            try
            {
                if (await inFlight.Render is { } finished
                    && (!inFlight.StandInAtStart || KokoroStandingIn())
                    && (!finished.StandIn || KokoroStandingIn()))
                {
                    _voicedAhead.Remove(text);
                    return finished;
                }
            }
            catch (Exception)
            {
                // Rendered again below.
            }
        }

        return await _announcer.PrepareReminderAsync(text);
    }

    /// <summary>The On Air button, pressed - by the 0 key (a tap to start, a hold to skip) or the phone.</summary>
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
                try
                {
                    running.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // On Air had already finished.
                }

                // WHY (10 Nov 2026, bug hunt 16): and what it had already
                // queued stops with it - the other race's opening, its
                // driver introductions and pit lane are priority Reminders a
                // cancel leaves in the queue, so they played on over the demo
                // and its staging call waited behind them. See SilenceOnAir.
                // Before start(), so the demo's staging call is queued after
                // the cut; the cancelled On Air's own catch queues nothing
                // (_onAirStageNow is false), so Cancel running it inline is
                // safe here.
                SilenceOnAir("On Air called off for a demo");

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

            // WHY (7 Nov 2026, bug hunt 14): CancelAsync, not Cancel. On Air
            // awaits on the UI thread, and Cancel runs its continuation
            // INLINE - so On Air's catch staged the race, and its staging
            // call was queued, before Cancel came back. SilenceOnAir below
            // then wiped that call. CancelAsync completes the cancelled
            // waits off this thread, and their continuations come back to
            // the UI thread after this handler, so the silencing goes
            // first, as the note below always meant.
            try
            {
                _ = running.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // On Air had already finished.
            }

            // And what On Air has already queued stops with it - see
            // SilenceOnAir. Before the hand-over's Stage, which runs once
            // On Air's own continuation sees the cancel.
            SilenceOnAir("On Air skipped to Stage");
            return;
        }

        if (RefuseIfPracticeRunningForRaceStart("go on air"))
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

        // WHY (24 Oct 2026, timing layer, P8): threaded, no practice starts at
        // the gate while On Air runs. The session stays Idle through the
        // opening, and a car warming up during it started practice on the
        // timing thread - and On Air was then called off ("the race was moved
        // on"). Let go in the finally below, after the hand-over's Stage,
        // whose own take-back covers the rest of that UI message. Inline: a
        // hold that does nothing (MaybeAutoStartPracticeOnGatePass asks
        // OnAirRunning instead).
        var practiceHold = _race.HoldPracticeAutoStart();

        try
        {
            await RunOnAirHoldingPracticeAsync(round, start, handOver);
        }
        finally
        {
            // Every way out, a throw before the opening included - a hold
            // left on would stop practice starting at the gate until restart.
            practiceHold.Dispose();
        }
    }

    /// <summary>The rest of RunOnAirAsync, with practice at the gate held off - see there.</summary>
    private async Task RunOnAirHoldingPracticeAsync(RoundDefinition? round, Action? start, Action? handOver)
    {
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
        // The shared constant and nextRace: true (22 Oct 2026) - see EndRecapShow.
        EndRecapShow(OnAirPressedForNextRace, nextRace: true);

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
        _onAirForDemo = start is not null;
        _openingSaidByOnAir = false;
        _introductionsSaidByOnAir = false;
        _principalSaidByOnAir = false;

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
            // A demo already running, moving itself on, is not a reason to
            // call off the demo this run is opening (22 Oct 2026, bug hunt):
            // its simulator stages and goes green by itself, and the new demo
            // was silently dropped. A real Stage, Countdown or Green is caught
            // by SkipOnAirToTheDemo, and Reset by CancelAutoStart.
            if (_onAirForDemo && _race.IsDemoMode)
            {
                return;
            }

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

        // The Tonight Open, asked for below, and whether On Air got as far
        // as playing it - a call-off before then gives it back (finally).
        var tonightOpen = Task.FromResult<SpokenSegment?>(null);
        var tonightOpenTaken = false;

        try
        {
            // 1. THE OPENING, WRITTEN AND RENDERED BEFORE ANYTHING PLAYS
            //    (16 Oct 2026). The voice has to HIT THE POST - start talking
            //    the instant the jingle's sung part stops - and it can only
            //    do that with the audio already in hand. Nothing is on air
            //    yet, so the wait for the model is silence before the show,
            //    never dead air in the middle of it.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - getting the opening ready.");

            // SAYS SO (16 Oct 2026). Nothing is heard while the opening is
            // written and rendered, so the race director is shown it is on
            // its way, and how far along it is (21 Oct 2026) - on the strip
            // under the flag row since 25 Oct 2026. See BeginOnAirProgress.
            BeginOnAirProgress();

            // THE TONIGHT OPEN, ASKED FOR NOW (11 Nov 2026) and written while
            // the opening is, so neither waits on the other. Once a night,
            // ticked on Voice -> Commentary, and only in front of a real race:
            // a demo is not the start of the evening. Asked here, on the UI
            // thread, because the director reads its facts before it awaits.
            if (start is null && Commentary?.Invoke() is { } director)
            {
                tonightOpen = director.ForTonightOpenAsync(cts.Token);
            }

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
                    SetOnAirStep(OnAirStep.VoicingOpening);
                    audio = await VoiceForOnAirAsync(openingToRender);
                }

                if (toRender.Introductions is { } introductionsToRender)
                {
                    SetOnAirStep(OnAirStep.VoicingIntros);
                    introductionsAudio = await VoiceForOnAirAsync(introductionsToRender.Text);
                }
            }

            cts.Token.ThrowIfCancellationRequested();

            // The Tonight Open written before the show goes live, so a slow
            // write is On Air getting ready rather than silence on air. A
            // write that failed is said in PlayTonightOpenAsync.
            try
            {
                await tonightOpen.WaitAsync(cts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }

            // Whatever is left is the announcer finishing what he is saying.
            SetOnAirStep(OnAirStep.Finishing);

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

            EndOnAirProgress(reachedTheJingle: true);

            // LIVE, AND SAYS SO (21 Oct 2026): the jingle, the opening, the
            // introductions and the Team Principal are a minute or more, and
            // the race is still to be staged. Holding 0 skips the rest, which
            // the strip says too.
            ShowOnAirLive(true);

            // WHY (7 Nov 2026, bug hunt 14): a sponsor video still running on
            // the Big Screen kept its sound over the jingle and the opening -
            // the screen only stops one once the race is called, which On
            // Air has not done yet. The spot comes off first, as Stop the
            // Broadcast takes it off.
            if (_bigScreen is { IsDisposed: false } screen)
            {
                screen.StopSponsorSpot("On Air");
            }

            // 1b. THE TONIGHT OPEN, THEN THE SHOW OPEN (11 Nov 2026). The
            //     evening is set out first - its own music, its own recording
            //     - and it hands straight on to this race's jingle. It used
            //     to fire by itself in the first quiet gap of the night.
            tonightOpenTaken = true;
            await PlayTonightOpenAsync(tonightOpen, cts.Token);

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
                    // The Big Screen's cards for them (1 Nov 2026) - the
                    // field read again, quietly, as the line is handed over:
                    // the same grid they were written from. See IntroOnAir.
                    //
                    // WHY (10 Nov 2026, bug hunt 16): the round the opening
                    // was written for (OpeningRun.Round), not this method's
                    // own round - that is null for every real start (Start
                    // Next, the Program, F1, the phone, 0), which only a demo
                    // fills in, so a real race's cards were never armed. And
                    // the Stage handed over to skips its introductions, the
                    // only other place they are armed.
                    if (said.Round is { } introRound && ReadIntroductionFacts(introRound, null, quiet: true) is { } introFacts)
                    {
                        ArmIntroCards(introFacts.Grid, saidIntroductions.Text);
                    }

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

            // Said (22 Oct 2026, bug hunt): a call-off used to be silent, so
            // a jingle fading into nothing left nothing in the log to say why.
            if (!stage)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air called off - the race was moved on (Reset, a flag or Stage) before the opening finished.");
            }
        }
        catch (Exception ex)
        {
            // Decoration. The race still gets staged.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - {ex.Message}. Staging the race.");
            stage = true;
        }
        finally
        {
            if (!tonightOpenTaken)
            {
                GiveBackWhenWritten(tonightOpen);
            }

            _race.StateChanged -= onRaceMoved;
            _onAirCts = null;
            _onAirStageNow = false;
            _onAirForDemo = false;
            EndOnAirProgress(reachedTheJingle: false);
            ShowOnAirLive(false);
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
    /// Plays the Tonight Open On Air asked for, if there is one, and waits for
    /// the announcer to finish it so the show open's jingle comes in after
    /// its last word. Calling On Air off (a second press, Reset, a flag)
    /// stops it where it is. Throws OperationCanceledException then, as the
    /// rest of On Air does.
    /// </summary>
    private async Task PlayTonightOpenAsync(Task<SpokenSegment?> asked, CancellationToken ct)
    {
        SpokenSegment? said;

        try
        {
            said = await asked.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            GiveBackWhenWritten(asked);
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - no Tonight Open ({ex.Message}). Straight to the show open.");

            return;
        }

        if (said is null)
        {
            return;
        }

        var director = Commentary?.Invoke();

        if (PlayTonightOpen is not { } play || ct.IsCancellationRequested)
        {
            director?.Dropped(said);
            ct.ThrowIfCancellationRequested();

            return;
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] On Air - the Tonight Open, then the show open.");

        // A block on air does not see the token, so it is abandoned by hand.
        using (ct.Register(() => ShowMusic?.Invoke()?.AbandonBlock()))
        {
            await play(said).WaitAsync(ct);
            await _announcer.WaitForAudioAsync(SpokenWait(), ct);
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// A Tonight Open On Air never played, given back to the director once it
    /// is written - unspent, so the next On Air can ask for it again. On the
    /// UI thread, where the director is answered.
    /// </summary>
    private void GiveBackWhenWritten(Task<SpokenSegment?> asked)
    {
        _ = asked.ContinueWith(
            written =>
            {
                if (written.Result is { } unsaid)
                {
                    Commentary?.Invoke()?.Dropped(unsaid);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
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
    /// <param name="Round">
    /// The race it was all written for (10 Nov 2026, bug hunt 16) - the one
    /// WriteOpeningAsync worked out, from the Round box when it was not given
    /// one. The Big Screen's intro cards are read from it, so they show the
    /// same field the introductions were written from.
    /// </param>
    private sealed record OpeningRun(
        string? Text,
        PackedFacts? History,
        (string Text, Action? Count)? Introductions,
        (List<SpokenLine> Lines, string Focus)? Principal,
        bool PrincipalTried = false,
        RoundDefinition? Round = null);

    /// <summary>
    /// The run marked with the round it was written for (10 Nov 2026, bug
    /// hunt 16) - see <see cref="OpeningRun"/>'s Round.
    /// </summary>
    private static OpeningRun? ForRound(OpeningRun? run, RoundDefinition round) =>
        run is null ? null : run with { Round = round };

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

            // THE DRIVER INTRODUCTIONS, written alongside the intro rather
            // than after it (18 Oct 2026), so they are rendered with the
            // opening and follow it with no wait. Started here, on the UI
            // thread where their facts are read, and awaited below.
            //
            // BEFORE THE RACE-INTRO SWITCHES (22 Oct 2026, bug hunt): Stage
            // no longer says them, so On Air is the only place they go out -
            // and with race intros off, kept for mains, or no intro back in
            // time, they were never started or were thrown away. They have
            // a switch of their own.
            var introductions = IntroductionsForOpeningAsync(round, ct);

            if (!_settings.CommentaryRaceIntros)
            {
                return ForRound(await PitLaneOnlyAsync(round, introductions, ct), round);
            }

            if (_settings.CommentaryIntrosMainsOrRivalriesOnly
                && round.Kind != RoundKind.Main
                && !_commentary.HasRivalryAmong(round.AssignedTransponders))
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [imaging] No opening for {round.RoundLabel} - intros are set to mains and rivalries only, "
                    + "and this race is neither.");

                return ForRound(await PitLaneOnlyAsync(round, introductions, ct), round);
            }

            var (clubNight, form, stake, scene) = ReadIntroFacts(round);

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
                return ForRound(await PitLaneFrom(principalJob, principal, introductions), round);
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
                PrincipalTried: principalJob is not null,
                Round: round);
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
    private async Task<OpeningRun?> PitLaneOnlyAsync(
        RoundDefinition round, Task<(string Text, Action? Count)?> introductions, CancellationToken ct)
    {
        var job = PreparePrincipal(round);

        return await PitLaneFrom(
            job,
            job is null ? Task.FromResult<List<SpokenLine>?>(null) : Task.Run(() => WritePrincipalLinesAsync(job, ct)),
            introductions);
    }

    /// <summary>An <see cref="OpeningRun"/> carrying the pit lane alone, once its lines are written - null when none was due.</summary>
    private async Task<OpeningRun?> PitLaneFrom(
        PrincipalJob? job, Task<List<SpokenLine>?> writing, Task<(string Text, Action? Count)?>? introductions = null)
    {
        // The introductions, with no opening in front of them (22 Oct 2026,
        // bug hunt) - see WriteOpeningAsync.
        (string Text, Action? Count)? withIntroductions = null;

        try
        {
            withIntroductions = introductions is null ? null : await introductions;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Driver introductions skipped - {ex.Message}");
        }

        if (job is null)
        {
            if (withIntroductions is null)
            {
                return null;
            }

            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [imaging] No opening for this race - the driver introductions go out after the jingle, before staging.");
            return new OpeningRun(null, null, withIntroductions, null);
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

        return new OpeningRun(null, null, withIntroductions, lines is { Count: > 0 } ? (lines, job.Focus) : null, PrincipalTried: true);
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
