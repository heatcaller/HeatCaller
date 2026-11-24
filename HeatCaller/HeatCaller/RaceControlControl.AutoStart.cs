namespace HeatCaller;

/// <summary>
/// AUTO START BETWEEN RACES (11 Nov 2026, race-night build plan, Phase 1 -
/// "keep the night moving"). See AutoStartClock for the rule.
///
/// What it adds to Race Control:
///  - "Auto start: Off | On" at the foot of the board, beside "Start races".
///    Off is for tonight only; its right-click menu picks the gap, and can
///    turn it off until it is switched back on.
///  - The Next Up bar says when the next race starts ("starts in 1:42"), and
///    a Hold button beside Start Next holds and resumes the clock. Its
///    right-click menu has +1 min and Start now.
///  - At zero, a car the loop hasn't heard puts up a banner: "Frank not seen
///    by the loop - Start without them / Wait". The announcer calls the
///    driver once. A car turning up clears it.
///  - Every clock counts to the green, not to Auto start's own Start press
///    (bug hunt 21, 15 Nov 2026). The Big Screen, the Next Up bar and the
///    phones already did (ToTheGreen); the PA's countdown marks and its
///    "{minutes}" (DueFor) and the night plan (NoteNextStart) counted to the
///    press, a warm-up, staging and a countdown earlier - two numbers for
///    one race. They now read AutoGreenAtUtc. When Auto start presses Start
///    is unchanged.
///  - The start itself is Start Next's own, so On Air, the call-up to the
///    grid and every refusal Start Next has still apply.
/// </summary>
public sealed partial class RaceControlControl
{
    private readonly AutoStartClock _autoStart = new();

    /// <summary>"Auto start: Off | On" - at the foot of the board.</summary>
    // "Auto:" rather than "Auto start:" (15 Nov 2026), so the foot of the
    // board fits on one row at 1280 wide, with "Start:" and "Warm-up:".
    private readonly SegmentedSwitch _swAutoStart = new("Auto:", "Off", "On") { Compact = true };

    /// <summary>Hold / Resume, beside Start Next on the Next Up bar.</summary>
    private readonly Button _btnAutoHold = new();

    /// <summary>The not-seen banner: "Frank not seen by the loop".</summary>
    private readonly TableLayoutPanel _carsBanner = new();

    private readonly Label _lblCarsBanner = new();

    /// <summary>What the clock said last second - read by the Next Up bar.</summary>
    private AutoStartState _autoStartState = AutoStartState.Off;

    /// <summary>Whose missing car has been called this gap, so the call goes out once.</summary>
    private string? _carsCalledFor;

    /// <summary>
    /// The race (session generation) whose gap Auto start may count: set
    /// when a real program race is filed, so a demo, a Quick Race or a race
    /// kept on its own never leads to an automatic start.
    /// </summary>
    private int? _autoStartAfter;

    private const string HoldCaption = "Hold";
    private const string ResumeCaption = "Resume";

    /// <summary>Tonight, as the off-for-tonight setting keeps it.</summary>
    private static string TonightKey => NightEmail.NightKey(TonightAtTheTrack.NightOf(DateTime.Now));

    /// <summary>Whether Auto start is on right now: switched on, and not turned off for tonight.</summary>
    internal bool AutoStartOn => !AutoStartClock.OffTonight(_settings.AutoStartBetweenRaces, _settings.AutoStartOffForNight, TonightKey);

    /// <summary>Tonight's planned breaks (the race day's - setup plan D5). Set by MainForm; null for none.</summary>
    internal Func<IReadOnlyList<PlannedBreak>?>? PlannedBreaks { get; set; }

    /// <summary>"Hand the Desk to a Phone..." on the Auto start switch's menu (setup plan D8) - MainForm shows the remote's QR code.</summary>
    public event Action? HandDeskRequested;

    /// <summary>
    /// Something the desk did in the gap before a race (12 Nov 2026, setup
    /// plan step 8, build plan 0.2): Hold and how long, +1 min, Start now, a
    /// driver's five minutes, a wait for the promised time. The race's
    /// round id. MainForm keeps it on the round (RoundDefinition.GapNotes)
    /// for NightPace, so the report can say "held 3 min at the desk".
    /// </summary>
    public event Action<string, GapNote>? GapNoted;

    /// <summary>A race was called up to the grid (12 Nov 2026, setup plan step 8): its round id, for RoundDefinition.CalledAtUtc.</summary>
    public event Action<string>? RaceCalledUp;

    /// <summary>When the hold now on began, and for which race - so its length is logged when it ends, however it ends.</summary>
    private (DateTime SinceUtc, string RoundId)? _holdLogged;

    /// <summary>Logs a note against a race's gap.</summary>
    private void NoteGap(string? roundId, GapNoteKind kind, DateTime nowUtc, TimeSpan length)
    {
        if (!string.IsNullOrEmpty(roundId))
        {
            GapNoted?.Invoke(roundId, new GapNote { Kind = kind, AtUtc = nowUtc, Seconds = (int)Math.Round(Math.Max(0, length.TotalSeconds)) });
        }
    }

    /// <summary>A hold has ended - resumed, started through, or the gap closed: its length goes in the gap's log.</summary>
    private void NoteHoldEnded(DateTime nowUtc)
    {
        if (_holdLogged is { } hold)
        {
            _holdLogged = null;
            NoteGap(hold.RoundId, GapNoteKind.Held, nowUtc, nowUtc - hold.SinceUtc);
        }
    }

    /// <summary>
    /// Asks before Redo Remaining Heats changes posted heats (setup plan D2).
    /// Set by MainForm, from the program page; true to go ahead. Null always
    /// goes ahead.
    /// </summary>
    internal Func<IWin32Window?, string, bool>? ConfirmPostedChange { get; set; }

    /// <summary>The gap Auto start counts, from the setting, kept between 30 s and 15 min.</summary>
    private TimeSpan AutoStartGap => TimeSpan.FromSeconds(Math.Clamp(_settings.AutoStartGapSeconds, 30, 900));

    /// <summary>The choices on the switch's right-click menu, in seconds.</summary>
    private static readonly int[] GapChoices = { 90, 120, 150, 180, 240, 300 };

    /// <summary>Wires the switch, the Hold button and the banner. Called from BuildLayout.</summary>
    private void BuildAutoStart()
    {
        // The set gap, for night pace's "idle director" (setup plan step 8).
        NightPace.ClubGap = AutoStartGap;

        _swAutoStart.On = AutoStartOn;
        _swAutoStart.OnChanged += (_, _) => SetAutoStart(_swAutoStart.On, forTonightOnly: true);

        var menu = new ContextMenuStrip();
        AppTheme.StyleContextMenu(menu);

        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();

            foreach (var seconds in GapChoices)
            {
                var item = new ToolStripMenuItem($"Gap between races: {AutoStartClock.Clock(TimeSpan.FromSeconds(seconds))}")
                {
                    Checked = _settings.AutoStartGapSeconds == seconds,
                };
                var chosen = seconds;
                item.Click += (_, _) =>
                {
                    _settings.AutoStartGapSeconds = chosen;
                    _settings.Save();
                    NightPace.ClubGap = AutoStartGap;
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] The gap between races is {AutoStartClock.Clock(TimeSpan.FromSeconds(chosen))} from now on.");
                };
                menu.Items.Add(item);
            }

            // Setup plan D4: the start waits for Marshals in place.
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Wait for the marshals before starting", null, (_, _) => ToggleWaitForMarshals())
            {
                Checked = _settings.AutoStartWaitForMarshals,
            });

            // Setup plan step 8: a race waits for the time drivers were given.
            menu.Items.Add(new ToolStripMenuItem("Never start a race more than 2 minutes before the time drivers were given", null, (_, _) => ToggleNeverEarly())
            {
                Checked = _settings.AutoStartNeverEarly,
            });

            // Setup plan D8: a deputy runs the starts from their phone.
            menu.Items.Add(new ToolStripMenuItem("Hand the Desk to a Phone...", null, (_, _) => HandDeskRequested?.Invoke()));

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Off until I switch it on again", null, (_, _) => SetAutoStart(false, forTonightOnly: false))
            {
                Enabled = _settings.AutoStartBetweenRaces,
            });
        };

        _swAutoStart.ContextMenuStrip = menu;

        var tip = new ToolTip();
        AppTheme.StyleToolTip(tip, keep: true);
        tip.SetToolTip(_swAutoStart,
            "On: after each race, the next one starts by itself once the gap is up (right-click to choose it). "
            + "The call-ups are timed to it, Hold on the Next Up bar stops the clock, and a car the loop hasn't heard "
            + "waits for you to say start without it. Off is for tonight only.");

        _btnAutoHold.Text = HoldCaption;
        _btnAutoHold.Enabled = false;
        _btnAutoHold.Click += (_, _) => ToggleAutoStartHold();

        var holdMenu = new ContextMenuStrip();
        AppTheme.StyleContextMenu(holdMenu);
        var plusOne = new ToolStripMenuItem("+1 min", null, (_, _) => PushNextRaceBack());
        var startNow = new ToolStripMenuItem("Start now", null, (_, _) => AutoStartNow());
        holdMenu.Items.AddRange(new ToolStripItem[] { plusOne, startNow });
        holdMenu.Opening += (_, _) =>
        {
            plusOne.Enabled = _autoStart.IsOpen;
            startNow.Enabled = _autoStart.IsOpen;
        };
        _btnAutoHold.ContextMenuStrip = holdMenu;
        tip.SetToolTip(_btnAutoHold, "Holds the automatic start, and Resume carries on from where it stood. Right-click for +1 min and Start now.");

        // The banner, in the unknown-transponder banner's warning frame.
        _carsBanner.AutoSize = true;
        _carsBanner.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _carsBanner.ColumnCount = 1;
        _carsBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _carsBanner.Padding = new Padding(14, 10, 14, 10);
        _carsBanner.Visible = false;
        _carsBanner.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Warning, 2);
            e.Graphics.DrawRectangle(pen, 1, 1, _carsBanner.Width - 2, _carsBanner.Height - 2);
        };

        _lblCarsBanner.AutoSize = true;
        _lblCarsBanner.Font = AppTheme.BoldFont;
        _lblCarsBanner.Margin = new Padding(0, 6, 12, 0);

        var go = new Button { Text = "Start without them" };
        go.Click += (_, _) => ApproveMissingCars();
        var wait = new Button { Text = "Wait" };
        wait.Click += (_, _) => ToggleAutoStartHold();

        _carsBanner.Controls.Add(new ButtonBar(tight: true).With(_lblCarsBanner, go, wait));
    }

    /// <summary>Switches Auto start on or off - off for tonight from the switch, or until switched on from its menu.</summary>
    private void SetAutoStart(bool on, bool forTonightOnly)
    {
        if (on)
        {
            _settings.AutoStartBetweenRaces = true;
            _settings.AutoStartOffForNight = string.Empty;
        }
        else if (forTonightOnly)
        {
            _settings.AutoStartOffForNight = TonightKey;
        }
        else
        {
            _settings.AutoStartBetweenRaces = false;
        }

        _settings.Save();

        if (!on)
        {
            _autoStart.Close();
            _autoStartState = AutoStartState.Off;
            ShowCarsBanner(false);
        }

        if (_swAutoStart.On != AutoStartOn)
        {
            _swAutoStart.On = AutoStartOn;
        }

        StatusMessage?.Invoke(on
            ? $"{AppTime.LogStampNow()}  [auto] Auto start is on: the next race starts {AutoStartClock.Clock(AutoStartGap)} after each checkered flag."
            : forTonightOnly
                ? $"{AppTime.LogStampNow()}  [auto] Auto start is off for tonight - Start Next starts each race."
                : $"{AppTime.LogStampNow()}  [auto] Auto start is off until it is switched on again.");
        UpdateAutoHoldButton();
    }

    /// <summary>Hold or Resume - the Hold button, the banner's Wait.</summary>
    private void ToggleAutoStartHold()
    {
        var now = DateTime.UtcNow;

        if (_autoStart.IsHeld)
        {
            _autoStart.Resume(now);
            NoteHoldEnded(now);
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Resumed - the next race starts in {AutoStartClock.Clock(_autoStart.Remaining(now, Gap.PushedBackMinutes) ?? TimeSpan.Zero)}.");
        }
        else if (_autoStart.IsOpen)
        {
            _autoStart.Hold(now);
            _holdLogged = _autoStart.RoundId is { } held ? (now, held) : null;
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Start held - press Resume to carry on.");
        }

        UpdateAutoHoldButton();
    }

    /// <summary>Start now, from the Hold button's menu and its key.</summary>
    private void AutoStartNow()
    {
        var now = DateTime.UtcNow;
        NoteHoldEnded(now);
        NoteGap(_autoStart.RoundId, GapNoteKind.StartedNow, now, TimeSpan.Zero);
        _autoStart.StartNow();
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Starting now.");
    }

    /// <summary>
    /// The Hold key (H, 12 Nov 2026, setup plan step 8): Hold or Resume, as
    /// the Hold button - or, with nothing counting, says why in the log.
    /// </summary>
    internal void TriggerAutoStartHold()
    {
        if (!AutoStartOn || !_autoStart.IsOpen)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] H (Hold): Auto start isn't counting down to a race right now.");
            return;
        }

        ToggleAutoStartHold();
    }

    /// <summary>The Start now key (N, 12 Nov 2026, setup plan step 8), as the Hold button's Start now.</summary>
    internal void TriggerAutoStartNow()
    {
        if (!AutoStartOn || !_autoStart.IsOpen)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] N (Start now): Auto start isn't counting down to a race right now - F1 starts one by hand.");
            return;
        }

        AutoStartNow();
    }

    /// <summary>The switch menu's "Never start a race more than 2 minutes before the time drivers were given".</summary>
    private void ToggleNeverEarly()
    {
        _settings.AutoStartNeverEarly = !_settings.AutoStartNeverEarly;
        _settings.Save();
        StatusMessage?.Invoke(_settings.AutoStartNeverEarly
            ? $"{AppTime.LogStampNow()}  [auto] Auto start never starts a race more than {NightPlan.Minutes(StartPromise.Early)} before the time drivers were given."
            : $"{AppTime.LogStampNow()}  [auto] Auto start starts each race when the gap is up, even ahead of the time drivers were given.");
    }

    /// <summary>The banner's Start without them.</summary>
    private void ApproveMissingCars()
    {
        var who = AutoStartClock.Names(_autoStart.NotSeen);
        _autoStart.ApproveMissingCars();
        _autoStart.Resume(DateTime.UtcNow);
        NoteHoldEnded(DateTime.UtcNow);
        ShowCarsBanner(false);
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Starting without {who} - approved at the desk.");
    }

    private void UpdateAutoHoldButton()
    {
        var caption = _autoStart.IsHeld ? ResumeCaption : HoldCaption;

        if (_btnAutoHold.Text != caption)
        {
            _btnAutoHold.Text = caption;
        }

        var enabled = AutoStartOn && _autoStart.IsOpen;

        if (_btnAutoHold.Enabled != enabled)
        {
            _btnAutoHold.Enabled = enabled;
        }
    }

    private void ShowCarsBanner(bool show, string text = "")
    {
        if (show && _lblCarsBanner.Text != text)
        {
            _lblCarsBanner.Text = text;
        }

        // Always assigned, never compared: see UnknownTransponderBanner.RefreshVisibility.
        _carsBanner.Visible = show;
    }

    /// <summary>
    /// The race just run, for Auto start's planned breaks and rest: the
    /// round the finished race was filed under (looked up afresh, so an
    /// edited grid is current), else the Round box's. Pure.
    /// </summary>
    /// <param name="filed">The round the last filed race was filed under, and that race's generation.</param>
    internal static RoundDefinition? AutoStartLastRound(
        (string? RoundId, int Generation)? filed, int generation, RoundDefinition? boxRound, Func<string, RoundDefinition?> lookup) =>
        filed is { } f && f.Generation == generation && f.RoundId is { } id && lookup(id) is { } round
            ? round
            : boxRound;

    /// <summary>
    /// Once a second, from the gap's tick, while the gap is open: counts
    /// down, holds for the floor and for cars, and starts the next race.
    /// </summary>
    private void AutoStartTick(DateTime now, GapFacts facts, bool floorBusy)
    {
        var next = NextRound();

        // Not with it off, with nothing next, or while the program owes a
        // step (finals to build, bump-ups) - those are the director's to do,
        // and Start Next would do them, not start a race.
        if (!AutoStartOn || next is null || _barStep is not null || !facts.HasNext || facts.NextId != next.Id
            || _autoStartAfter != _race.SessionGeneration)
        {
            CloseAutoStart();
            return;
        }

        // WHY (23 Nov 2026, bug hunt 24): this read the race just run from
        // the Round box, but filing the race marks its round recorded and the
        // box falls back to freeform straight away - so it was always empty
        // here, no planned break was ever added and nobody racing two races
        // running got their rest. The round the race was filed under is used.
        var lastRound = AutoStartLastRound(_gapFiled, _race.SessionGeneration, _activeRound?.Invoke(), id => _rounds?.Get(id));
        var justRan = RecentlyRan(next, lastRound);
        var resting = AutoStartClock.BackToBack(justRan, next.AssignedTransponders, _profiles.PrimaryForAlternate).Select(SpokenNameOf).ToList();

        // A break planned between the race just run and this one (12 Nov
        // 2026, setup plan D5) is added to the gap: "water the track after
        // Q2" holds Q3 Heat 1 for those ten minutes too.
        var breaks = NightBreaks.Between(lastRound, next, PlannedBreaks?.Invoke());
        var gap = AutoStartGap + NightBreaks.Length(breaks);

        var wasOpen = _autoStart.IsOpen && _autoStart.RoundId == next.Id;
        _autoStart.Open(_race.SessionGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture), next.Id, now, gap, resting);

        if (!wasOpen && resting.Count > 0)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {AutoStartClock.Names(resting)} raced the last race too - "
                                  + $"{next.RoundLabel} waits an extra {AutoStartClock.Clock(AutoStartClock.RestForBackToBack)}.");
        }

        if (!wasOpen && breaks.Count > 0 && _autoStart.StartsAtUtc(now, Gap.PushedBackMinutes) is { } resumes)
        {
            SayGapLine(NightBreaks.Announcement(breaks, resumes), "Break");
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Planned break before {next.RoundLabel}: "
                                  + $"{NightPlan.Minutes(NightBreaks.Length(breaks))}, {NightPlan.About(resumes)}.");
        }

        // Marshals in place (12 Nov 2026, setup plan D4): only for a club that
        // waits for them, and only for a race with somebody on a post.
        var waitForMarshals = _settings.AutoStartWaitForMarshals && facts.Marshals.Count > 0;

        // Marshals in place from their own phones (12 Nov 2026, setup plan
        // step 8): all of them in opens the gate by itself, and the banner
        // says who's in and who isn't. Worked out only when the club waits.
        IReadOnlyList<bool>? arrived = null;
        IReadOnlyList<string?>? coveringFor = null;
        IReadOnlyList<(string Name, string Post)> shown = facts.Marshals;

        if (waitForMarshals)
        {
            var posts = MarshalPostsFor(next);
            arrived = MarshalsFromPhones(next, posts);
            shown = posts.Select(p => (p.Name, p.Post)).ToList();
            coveringFor = posts.Select(p => CoveringFor(next, p.Transponder)).ToList();
        }

        // Never more than 2 minutes early (12 Nov 2026, setup plan step 8):
        // the time drivers were given holds the start back however early the
        // night is running. Set every second, so a promise made mid-gap, or
        // the setting changed, counts at once.
        var hadNotBefore = _autoStart.NotBeforeUtc;
        _autoStart.NotBefore(StartPromise.NotBefore(next, _settings.AutoStartNeverEarly));

        if (_autoStart.NotBeforeUtc is { } notBefore && hadNotBefore != notBefore && _autoStart.WaitForTime(now, Gap.PushedBackMinutes) > TimeSpan.Zero)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] The night is running early: {next.RoundLabel} waits for its time "
                                  + $"and starts at {AppTime.TimeOfDay(notBefore.ToLocalTime())}, {NightPlan.Minutes(StartPromise.Early)} before the time drivers were given. Start now starts it sooner.");
        }

        var waitedForTime = _autoStart.WaitForTime(now, Gap.PushedBackMinutes);

        _autoStartState = _autoStart.Step(now, floorBusy, NotSeenIn(next, now), Gap.PushedBackMinutes, waitForMarshals);

        ShowMarshalsAsk(next, shown, waitForMarshals, now, arrived, coveringFor);
        UpdatePhoneCountdown(now);

        switch (_autoStartState)
        {
            case AutoStartState.WaitingForCars:
                var names = AutoStartClock.Names(_autoStart.NotSeen);
                ShowCarsBanner(true, $"{names} not seen by the loop - {next.RoundLabel} is waiting.");

                if (_carsCalledFor != next.Id)
                {
                    _carsCalledFor = next.Id;
                    SayGapLine($"{names}, we can't see your car on the loop yet - get it on the track please.", "Roll call");
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {next.RoundLabel} is waiting for {names}: not seen by the loop. Start without them, or Wait.");
                }

                break;

            case AutoStartState.Start:
                ShowCarsBanner(false);
                NoteHoldEnded(now);

                // How long past the gap the race waited for its time, for
                // the night pace's causes - not after Start now, which didn't.
                if (waitedForTime > TimeSpan.Zero)
                {
                    NoteGap(next.Id, GapNoteKind.WaitedForPromise, now, waitedForTime);
                }

                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Starting {next.RoundLabel} - the gap is up.");
                StartNext();
                break;

            default:
                ShowCarsBanner(false);
                break;
        }

        UpdateAutoHoldButton();
    }

    /// <summary>The gap is over (a race went out, or Auto start can't run): the clock and its banner go.</summary>
    private void CloseAutoStart()
    {
        // A hold the race went out through (Start Next by hand) still goes
        // in the gap's log.
        NoteHoldEnded(DateTime.UtcNow);

        if (_autoStart.IsOpen)
        {
            _autoStart.Close();
        }

        _autoStartState = AutoStartState.Off;
        _phoneCountdown = null;
        _carsCalledFor = null;
        ShowCarsBanner(false);
        HideMarshalsAsk();
        UpdateAutoHoldButton();
    }

    /// <summary>
    /// Drivers in the next race the loop hasn't heard since a little before
    /// the last finish (RollCall's rule). None when there is no decoder to
    /// ask: a club timing by hand would otherwise be asked about every car.
    /// </summary>
    private IReadOnlyList<string> NotSeenIn(RoundDefinition next, DateTime now)
    {
        if (_transponderWatch is null || _race.IsDemoMode)
        {
            return Array.Empty<string>();
        }

        var roster = next.AssignedTransponders
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roster.Count == 0)
        {
            return Array.Empty<string>();
        }

        return RollCall.Build(roster, _transponderWatch, t => SpokenNameOf(t), RollCall.Since(now, RollCall.LastFinishUtc))
            .Where(e => e.State == RollCall.State.Waiting)
            .Select(e => e.Name)
            .ToList();
    }

    /// <summary>When the next race starts by Auto start, for the call-ups and the phones - null when Auto start isn't counting for it.</summary>
    private DateTime? AutoStartsAtUtc(string roundId, DateTime now) =>
        AutoStartOn && _autoStart.IsOpen && string.Equals(_autoStart.RoundId, roundId, StringComparison.Ordinal)
            ? _autoStart.StartsAtUtc(now, Gap.PushedBackMinutes)
            : null;

    /// <summary>
    /// When the next race goes green by Auto start - its start carried on to
    /// the green, as the Big Screen and the phones count (bug hunt 21, 15 Nov
    /// 2026) - for the PA's countdown marks and the night plan. Null when
    /// Auto start isn't counting for it.
    /// </summary>
    private DateTime? AutoGreenAtUtc(string roundId, DateTime now) =>
        AutoStartsAtUtc(roundId, now) is { } start ? GreenAt(start, _autoStartState, StartToGreen) : null;

    /// <summary>
    /// Auto start's start carried on to the green while the gap is counting.
    /// A race waiting for the time drivers were given keeps that time, as
    /// ToTheGreen does. The lead is worked out only when it is needed.
    /// </summary>
    internal static DateTime GreenAt(DateTime start, AutoStartState state, Func<TimeSpan> startToGreen) =>
        state == AutoStartState.Counting ? start + startToGreen() : start;

    /// <summary>What the Next Up bar adds after the next race: "starts in 1:42". Empty with Auto start not counting.</summary>
    private string AutoStartWords(DateTime now) =>
        AutoStartOn && _autoStart.IsOpen
            ? AutoStartClock.Words(_autoStartState, ToTheGreen(_autoStart.Remaining(now, Gap.PushedBackMinutes)), _autoStart.NotSeen, _autoStart.Resting,
                _autoStart.StartsAtUtc(now, Gap.PushedBackMinutes))
            : string.Empty;

    /// <summary>What the Big Screen's countdown band shows between races: "NEXT RACE IN 1:42", "NEXT RACE AT 20:40". Empty with Auto start not counting.</summary>
    private string AutoStartBand(DateTime now) =>
        AutoStartOn && _autoStart.IsOpen
            ? AutoStartClock.Band(_autoStartState, ToTheGreen(_autoStart.Remaining(now, Gap.PushedBackMinutes)), _autoStart.StartsAtUtc(now, Gap.PushedBackMinutes))
            : string.Empty;

    /// <summary>
    /// The gap's clock, carried on to the green (14 Nov 2026): Auto start's
    /// clock runs out when it starts the race, and the green comes a warm-up,
    /// a staging and a countdown later. Only while the gap is counting - a
    /// race waiting for the time drivers were given keeps that time ("NEXT
    /// RACE AT 20:40"), which is when its start opens.
    /// </summary>
    private TimeSpan? ToTheGreen(TimeSpan? remaining) =>
        _autoStartState == AutoStartState.Counting && remaining is { } left ? left + StartToGreen() : remaining;

    /// <summary>How long the next race's start runs before its green, by its format - see LeadToTheGreen.</summary>
    private TimeSpan StartToGreen()
    {
        if (_race.IsPracticeMode)
        {
            return TimeSpan.Zero;
        }

        // The race being counted down, not the Round box's: between races
        // the Round box still holds the race just run (it moves on only when
        // the next one starts), and the two can run different formats.
        var next = NextRound();
        var format = FormatFor(next, ClassFor(next), () => RaceFormat.FromSettings(_settings), out _);

        return LeadToTheGreen(
            WarmUpSecondsFor(format.WarmUpSeconds, StartWithWarmUp, _settings.RaceWarmUpFirstSeconds), format.AutoAdvance, format.StagingSeconds, format.CountdownSeconds);
    }

    /// <summary>
    /// From pressing Start to the green, by the format's lengths (14 Nov
    /// 2026): the warm-up, then staging and the countdown - but only the
    /// steps the start runs through by itself. Staging goes on to the
    /// countdown with auto-advance or after a warm-up; the countdown goes on
    /// to the green only with auto-advance. Otherwise the clock counts to the
    /// last step that comes by itself, since nobody can say when a button
    /// will be pressed. With the announcer speaking, staging and the
    /// countdown follow the calls rather than the seconds, so this is the
    /// format's best guess, not a promise.
    /// </summary>
    internal static TimeSpan LeadToTheGreen(int warmUpSeconds, bool autoAdvance, int stagingSeconds, int countdownSeconds)
    {
        var warmUp = Math.Max(0, warmUpSeconds);
        var seconds = warmUp;

        if (autoAdvance || warmUp > 0)
        {
            seconds += Math.Max(0, stagingSeconds);

            if (autoAdvance)
            {
                seconds += Math.Max(0, countdownSeconds);
            }
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Each round's estimated start, "about 20:42" (NightPlan.AboutOf, the
    /// phones' own - promised times included). Set by MainForm; null leaves
    /// the times off the Big Screen's up-next line.
    /// </summary>
    internal Func<string, string?>? EstimatedStart { get; set; }

    /// <summary>How many races to come the Big Screen lists between races.</summary>
    private const int UpNextOnBigScreen = 3;

    /// <summary>
    /// The Big Screen's line of races to come between races (12 Nov 2026,
    /// setup plan step 8, build plan 2.2): "UP NEXT:  Stock Q2 Heat 3 about
    /// 20:42  •  ..." - the same estimates the phones show. Empty while a
    /// race is on, in practice, or with nothing to come.
    /// </summary>
    private string BigScreenUpNext()
    {
        if (_race.State is not (RaceState.Idle or RaceState.Finished) || _race.IsPracticeMode)
        {
            return string.Empty;
        }

        return UpNextLine(OrderedProgram(), EstimatedStart);
    }

    /// <summary>
    /// <see cref="BigScreenUpNext"/>'s line for a program in running order.
    /// A shared heat is named by both its classes - "Stock + Mod Q2 Heat 3"
    /// - as the Next Up bar and the heat sheet name it (bug hunt 19): with
    /// the main class alone, the other class's drivers didn't see their race.
    /// </summary>
    internal static string UpNextLine(IEnumerable<RoundDefinition> orderedProgram, Func<string, string?>? about) =>
        NightPlan.UpNext(orderedProgram
            .Where(r => !r.IsRecorded && r.Kind != RoundKind.Practice)
            .Take(UpNextOnBigScreen)
            .Select(r => (MixedHeats.ClassLabel(r), r.RoundLabel, about?.Invoke(r.Id))));

    // --- From the phone remote (11 Nov 2026) ---

    /// <summary>The remote's "autohold": Hold or Resume. Null when it can't, with the reason in <paramref name="refusal"/>.</summary>
    private string? RemoteAutoHold(out string? refusal)
    {
        if (!AutoStartOn || !_autoStart.IsOpen)
        {
            refusal = "Auto start isn't counting down to a race right now.";
            return null;
        }

        refusal = null;
        ToggleAutoStartHold();
        return _autoStart.IsHeld ? "Start held - press again to resume." : "Resumed.";
    }

    /// <summary>The remote's "autoplus": the next race a minute later, as Call Up's +1 min.</summary>
    private string? RemoteAutoPlus(out string? refusal)
    {
        if (!AutoStartOn || !_autoStart.IsOpen)
        {
            refusal = "Auto start isn't counting down to a race right now.";
            return null;
        }

        refusal = null;
        PushNextRaceBack();
        return "Next race a minute later.";
    }

    // --- From the driver's phone: "Need 5 minutes" and "Skip me" (11 Nov 2026) ---

    /// <summary>How long a driver's "Need 5 minutes" puts the next race back.</summary>
    internal const int LateMinutes = 5;

    /// <summary>"Need 5 minutes" approved for races not next yet, given when each is - see MoreTimeOwed.</summary>
    private readonly MoreTimeOwed _moreTimeOwed = new();

    /// <summary>
    /// A driver asked for more time before their next race, and the desk
    /// said yes: when that race is the next one out, it goes back
    /// <see cref="LateMinutes"/> minutes (Auto start's count moves with it).
    /// Returns what happened, for the console and the driver.
    /// </summary>
    internal string GiveDriverMoreTime(string transponder, string name)
    {
        var next = NextRound();

        if (next is null || SeatOf(next, transponder) is null)
        {
            // 12 Nov 2026 (the first night on step 8): kept for their race
            // rather than refused. Drivers ask while they are still fixing
            // the car, two races out, and "ask again when it's next" meant a
            // second ask and a second yes - or a race that went without them.
            // ApplyOwedMoreTime gives it when that race's gap opens.
            if (NextRaceOf(transponder) is not { } theirs)
            {
                return $"{name} isn't in a race still to run - nothing to hold.";
            }

            // Theirs is the race in the Round box, picked and not started
            // (bug hunt 17, with NextRaceOf): there is no count to move, and
            // minutes owed to it would never be given - it is not "next" at
            // any gap. The desk starts it by hand, so the desk is told.
            if (string.Equals(theirs.Id, _activeRound?.Invoke()?.Id, StringComparison.Ordinal))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {name} asked for more time from their phone - {theirs.RoundLabel} is the race in hand, "
                                      + $"so give them {LateMinutes} minutes before you start it.");
                return $"The desk knows you need {LateMinutes} minutes before {theirs.RoundLabel}.";
            }

            // Owed under the id they are on the grid by, so Keep still finds
            // them there (bug hunt 19: it may be their second car's).
            transponder = SeatOf(theirs, transponder) ?? transponder;
            var owed = _moreTimeOwed.Owe(theirs.Id, transponder, name, LateMinutes);
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {name} asked for more time from their phone - {theirs.RoundLabel} isn't next yet, "
                                  + $"so it goes back {Plural.Of(owed, "minute")} when it is.");
            return $"{theirs.RoundLabel} will go back {Plural.Of(owed, "minute")} for you when it's next.";
        }

        var now = DateTime.UtcNow;
        Gap.NextRace(next.Id);
        Gap.PushBack(now, LateMinutes);
        NoteGap(next.Id, GapNoteKind.DriverAskedForTime, now, TimeSpan.FromMinutes(LateMinutes));
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {next.RoundLabel} goes back {LateMinutes} minutes for {name} (asked from their phone).");
        return $"{next.RoundLabel} goes back {LateMinutes} minutes for you.";
    }

    /// <summary>
    /// The gap tick, once it knows the next race: what drivers were promised
    /// for it while it wasn't next goes on now (12 Nov 2026) - the same
    /// push-back, gap note and console line as a yes given while it was.
    /// What no longer stands is dropped first: a race that ran or was
    /// deleted, a driver who left their race. Returns whether it moved the
    /// race, so the tick reads its facts again.
    /// </summary>
    private bool ApplyOwedMoreTime(DateTime now, string? nextId)
    {
        if (!_moreTimeOwed.Any)
        {
            return false;
        }

        var active = _activeRound?.Invoke()?.Id;
        var program = OrderedProgram();
        _moreTimeOwed.Keep(id => program.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal)
                                                             && !r.IsRecorded
                                                             && !string.Equals(r.Id, active, StringComparison.Ordinal)));

        var asks = _moreTimeOwed.Take(nextId);

        if (asks.Count == 0 || program.FirstOrDefault(r => string.Equals(r.Id, nextId, StringComparison.Ordinal)) is not { } next)
        {
            return false;
        }

        var minutes = asks.Sum(a => a.Minutes);
        Gap.PushBack(now, minutes);
        _gapFacts = null;

        foreach (var ask in asks)
        {
            NoteGap(next.Id, GapNoteKind.DriverAskedForTime, now, TimeSpan.FromMinutes(ask.Minutes));
        }

        var names = string.Join(" and ", asks.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase));
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {next.RoundLabel} goes back {Plural.Of(minutes, "minute")} for {names} (asked from their phone before it was next).");
        return true;
    }

    /// <summary>
    /// A driver's car is broken: they come out of their next race that
    /// hasn't run, after the desk said yes. One undo. Returns what happened.
    /// </summary>
    internal string SkipDriverInNextRace(string transponder, string name)
    {
        // 12 Nov 2026, setup plan step 8: scored DNS for the race, not just
        // taken off its grid - see SkipDns and RaceControlControl.SkipDns.
        if (SkipDriverDns(transponder, name, "car broken (asked from their phone)") is not { } race)
        {
            return $"{name} isn't in a race still to run.";
        }

        return $"You're out of {race.RoundLabel} - it's a DNS. Tell the desk when your car is fixed.";
    }
}
