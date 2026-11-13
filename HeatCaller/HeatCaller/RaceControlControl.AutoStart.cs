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
///  - The start itself is Start Next's own, so On Air, the call-up to the
///    grid and every refusal Start Next has still apply.
/// </summary>
public sealed partial class RaceControlControl
{
    private readonly AutoStartClock _autoStart = new();

    /// <summary>"Auto start: Off | On" - at the foot of the board.</summary>
    private readonly SegmentedSwitch _swAutoStart = new("Auto start:", "Off", "On");

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
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Resumed - the next race starts in {AutoStartClock.Clock(_autoStart.Remaining(now, Gap.PushedBackMinutes) ?? TimeSpan.Zero)}.");
        }
        else if (_autoStart.IsOpen)
        {
            _autoStart.Hold(now);
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Start held - press Resume to carry on.");
        }

        UpdateAutoHoldButton();
    }

    /// <summary>Start now, from the Hold button's menu.</summary>
    private void AutoStartNow()
    {
        _autoStart.StartNow();
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Starting now.");
    }

    /// <summary>The banner's Start without them.</summary>
    private void ApproveMissingCars()
    {
        var who = AutoStartClock.Names(_autoStart.NotSeen);
        _autoStart.ApproveMissingCars();
        _autoStart.Resume(DateTime.UtcNow);
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

        var lastRound = _activeRound?.Invoke();
        var justRan = lastRound?.AssignedTransponders;
        var resting = AutoStartClock.BackToBack(justRan, next.AssignedTransponders).Select(SpokenNameOf).ToList();

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

        _autoStartState = _autoStart.Step(now, floorBusy, NotSeenIn(next, now), Gap.PushedBackMinutes, waitForMarshals);

        ShowMarshalsAsk(next, facts.Marshals, waitForMarshals, now);

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
        if (_autoStart.IsOpen)
        {
            _autoStart.Close();
        }

        _autoStartState = AutoStartState.Off;
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

    /// <summary>What the Next Up bar adds after the next race: "starts in 1:42". Empty with Auto start not counting.</summary>
    private string AutoStartWords(DateTime now) =>
        AutoStartOn && _autoStart.IsOpen
            ? AutoStartClock.Words(_autoStartState, _autoStart.Remaining(now, Gap.PushedBackMinutes), _autoStart.NotSeen, _autoStart.Resting)
            : string.Empty;

    /// <summary>What the Big Screen's countdown band shows between races: "NEXT RACE IN 1:42". Empty with Auto start not counting.</summary>
    private string AutoStartBand(DateTime now) =>
        AutoStartOn && _autoStart.IsOpen
            ? AutoStartClock.Band(_autoStartState, _autoStart.Remaining(now, Gap.PushedBackMinutes))
            : string.Empty;

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

    /// <summary>
    /// A driver asked for more time before their next race, and the desk
    /// said yes: when that race is the next one out, it goes back
    /// <see cref="LateMinutes"/> minutes (Auto start's count moves with it).
    /// Returns what happened, for the console and the driver.
    /// </summary>
    internal string GiveDriverMoreTime(string transponder, string name)
    {
        var next = NextRound();

        if (next is null || !next.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase))
        {
            return $"{name}'s race isn't the next one out - nothing to hold yet. Ask again when it's next.";
        }

        var now = DateTime.UtcNow;
        Gap.NextRace(next.Id);
        Gap.PushBack(now, LateMinutes);
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {next.RoundLabel} goes back {LateMinutes} minutes for {name} (asked from their phone).");
        return $"{next.RoundLabel} goes back {LateMinutes} minutes for you.";
    }

    /// <summary>
    /// A driver's car is broken: they come out of their next race that
    /// hasn't run, after the desk said yes. One undo. Returns what happened.
    /// </summary>
    internal string SkipDriverInNextRace(string transponder, string name)
    {
        var race = OrderedProgram()
            .FirstOrDefault(r => !r.IsRecorded
                                 && !string.Equals(r.Id, _activeRound?.Invoke()?.Id, StringComparison.Ordinal)
                                 && r.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase));

        if (race is null)
        {
            return $"{name} isn't in a race still to run.";
        }

        var before = race.AssignedTransponders.ToList();
        race.AssignedTransponders = before
            .Where(t => !string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _rounds?.Update(race);

        _undo.Capture($"Skip {name} in {race.RoundLabel}", () =>
        {
            race.AssignedTransponders = before;
            _rounds?.Update(race);
        });

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {name} is out of {race.RoundLabel} - car broken (asked from their phone).");
        return $"You're out of {race.RoundLabel}. Tell the desk when your car is fixed.";
    }
}
