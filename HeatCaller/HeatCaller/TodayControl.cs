namespace HeatCaller;

/// <summary>
/// The Race Day page's landing tab, Next Steps: the night's run sheet with
/// the next race opened up on it, what's wrong before it can go, and - when
/// the night is running late - what would bring it back.
///
/// REDESIGNED 17 Nov 2026 (mockup A+B, "the run sheet in charge", in
/// plans/race-day-mockups.html; the plan is plans/race-day-redesign-plan.md).
/// The old page was a next-step card, a Ready to Race card and five tiles
/// over a splitter, with the program grid under it. Everything on it was
/// right, and all of it was drawn about the same size: the worst news on
/// the page - "4 h 02 min past the out-by time" - was grey text half way
/// through a sentence, the Start button was the loudest thing on screen
/// while the card beside it said the timer wasn't connected, and "Q1 Heat 1"
/// named four different races. A race director looks at this page between
/// races, standing up, with somebody asking them a question. So now:
///
///   - a strip across the top, only when the night is late (amber at 10
///     minutes past the out-by time, red at 30), with the finish and, when
///     fixes are ticked, what it becomes;
///   - the run sheet, the night as a timetable, taking most of the page
///     (RaceProgramControl.RunSheet), with this page's card for the next race
///     sat over its row - the clock, the race and the Start button in one
///     line of sight;
///   - a column beside it: the fixes as tick boxes that preview on the sheet
///     before Apply, and a short go/no-go list where only problems are loud;
///   - the buttons that were under the grid, across the foot of the page.
///
/// WHAT IT IS NOT. Still not a second way to do anything. Start runs the
/// program's step (ProgramNextStep), the fixes are the plan's own
/// (NightPlan.Fixes), the checks are the ones Mission Control and the setup
/// wizard already make. Every state is told by a shape and a word as well as
/// a colour (RunSheet.Mark).
/// </summary>
public sealed class TodayControl : UserControl
{
    private readonly RaceProgramControl _program;
    private readonly RaceEventStore _events;
    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _raceClasses;
    private readonly CheckInStore _checkIns;
    private readonly DayScheduleStore? _schedule;

    /// <summary>Whether the timer is connected, when the host can say. No line is shown when it cannot.</summary>
    private readonly Func<bool>? _timerConnected;

    /// <summary>Mission Control's checks - the phones and the caller come from these. Null when the host has none.</summary>
    private readonly Func<IReadOnlyList<MissionControl.Tile>>? _lights;

    // --- Where the fix-it buttons go. The host owns navigation. ---

    /// <summary>Raised to open the Set Up Tonight wizard at a step - one of the RaceDaySetupWizard.Step constants.</summary>
    public event Action<int>? SetUpRequested;

    /// <summary>The footer's "Ready to Race...", once everything is set up and nothing has raced yet (12 Nov 2026, setup plan B3).</summary>
    public event Action? ReadyToRaceRequested;

    /// <summary>The footer's "Drivers' Meeting...", any time a day is open (15 Nov 2026, setup plan D10).</summary>
    public event Action? DriversMeetingRequested;

    /// <summary>Raised when the timer needs connecting.</summary>
    public event Action? ConnectTimerRequested;

    /// <summary>Raised when the Start button is held for a demo run of its round - see BuildDemoHold.</summary>
    public event Action<RoundDefinition>? RoundDemoRequested;

    /// <summary>The footer's Undo: the app's own undo, one step, as Ctrl+Z.</summary>
    public event Action? UndoRequested;

    /// <summary>
    /// The clock and the day's line, for the host to put in the Race Day tab
    /// row - beside the tabs, where the mockup has it, rather than a row of
    /// the page's own. Its WidthChanged says when the day's words changed.
    /// </summary>
    internal TodayHeader Clock => _header;

    /// <summary>"Call to the Stand" on the card: Race Control's Call Up. What it says back is shown under the buttons.</summary>
    public Func<string?>? CallUp { get; set; }

    /// <summary>Whether a demo race files its result against its heat ("Let a demo fill in the race program" on Demo Mode).</summary>
    public Func<bool>? DemoFillsTheProgram { get; set; }

    /// <summary>The round last run as a demo while demos were not filed against the program - or null.</summary>
    public Func<string?>? UnfiledDemoRoundId { get; set; }

    /// <summary>Raised by the card's "Open Demo Mode" - see BlockerFor.</summary>
    public event Action? DemoModeRequested;

    /// <summary>Whether the strip's button opens Demo Mode rather than Connection, for the card's race as last shown.</summary>
    private bool _blockerOpensDemoMode;

    internal const string ConnectTimerCaption = "Connect Timer";
    internal const string OpenDemoModeCaption = "Open Demo Mode";
    internal const string TimerDownWords = "Timer not connected. Laps won't be counted.";
    internal const string TimerDownShortWords = "Timer not connected.";
    internal const string DemoLapsOnlyWords = "Demo laps only - connect the timer for real cars.";
    internal const string DemoNotFiledWords = "Demos don't move the night on: tick \"Let a demo fill in the race program\" on Demo Mode.";

    /// <summary>
    /// The strip above Start, and its button - or null for none.
    ///
    /// WHY (20 Nov 2026, head-to-head baseline): on a demo night the strip
    /// said "Laps won't be counted" over races whose demo laps were counted,
    /// and a demo run with "Let a demo fill in the race program" off left
    /// the same heat as next up for ever with nothing saying why.
    /// </summary>
    internal static (string Text, string Button, bool OpensDemoMode)? BlockerFor(bool timerDown, bool demoFillsTheProgram, bool demoOfThisRaceNotFiled)
    {
        if (demoOfThisRaceNotFiled)
        {
            // The timer said first when it is down too (21 Nov 2026, bug
            // hunt 23): a heat demoed as a sound check kept this strip all
            // night, and the real race then had no word that its laps
            // would not be counted.
            return (timerDown ? TimerDownShortWords + " " + DemoNotFiledWords : DemoNotFiledWords, OpenDemoModeCaption, true);
        }

        if (!timerDown)
        {
            return null;
        }

        return demoFillsTheProgram
            ? (DemoLapsOnlyWords, ConnectTimerCaption, false)
            : (TimerDownWords, ConnectTimerCaption, false);
    }

    /// <summary>Swallows the click a completed hold's let-go makes - see HeldClickFilter.</summary>
    private readonly HeldClickFilter _heldClicks = new();

    private readonly ToolTip _tips = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    // --- The parts ---
    private readonly TodayHeader _header = new();
    private readonly PaceStrip _strip = new();
    private readonly NowCard _card;
    private readonly Section _fixesCard = new Section("Get back on time");
    private readonly FixList _fixList = new();
    private readonly Label _lblAdvice = new();
    private readonly Button _btnApply = new() { Text = "Apply" };
    private readonly Button _btnTell = new() { Text = "Tell the Drivers" };
    private readonly Section _checksCard = new Section("Go / no-go");
    private readonly CheckList _checkList = new();

    // --- The footer ---
    private readonly Button _btnMeeting = new() { Text = "Drivers' Meeting..." };
    private readonly Button _btnReady = new() { Text = "Ready to Race..." };
    private readonly Button _btnEditDrivers = new() { Text = "Edit Drivers..." };
    private readonly Button _btnAddRace = new() { Text = "+ Add Race..." };
    private readonly Button _btnStandings = new() { Text = "Standings..." };
    private readonly Button _btnMarshals = new() { Text = "Marshals..." };
    private readonly Button _btnMore = new() { Text = "More ▾" };
    private readonly Button _btnUndo = new() { Text = "↶ Undo" };
    private ButtonBar? _leftBar;

    /// <summary>Ticks the clock in the header and the "now" line on the sheet.</summary>
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };

    /// <summary>The minute the sheet's "now" line was last drawn for.</summary>
    private int _clockMinute = -1;

    /// <summary>The step the card is offering. Held so the caption and the command cannot drift apart.</summary>
    private ProgramStep? _step;

    /// <summary>The fixes on offer, as last read from the plan.</summary>
    private IReadOnlyList<RunSheet.Choice> _choices = Array.Empty<RunSheet.Choice>();

    /// <summary>What the last Apply changed, for "Tell the drivers". Cleared once told.</summary>
    private string _lastAnnouncement = string.Empty;

    /// <summary>The race day _lastAnnouncement belongs to, so another day's change is never told.</summary>
    private string? _lastAnnouncementDayId;

    /// <summary>Set while RefreshAll runs, so the sheet's own refresh (SheetRebuilt) doesn't start a second one inside it.</summary>
    private bool _refreshing;

    internal TodayControl(
        RaceProgramControl program,
        RaceEventStore events,
        ProfileStore profiles,
        RaceClassStore raceClasses,
        CheckInStore checkIns,
        DayScheduleStore? schedule = null,
        Func<bool>? timerConnected = null,
        Func<IReadOnlyList<MissionControl.Tile>>? lights = null)
    {
        _program = program;
        _events = events;
        _profiles = profiles;
        _raceClasses = raceClasses;
        _checkIns = checkIns;
        _schedule = schedule;
        _timerConnected = timerConnected;
        _lights = lights;
        _card = new NowCard(() => _program.SheetGutterParts());

        Dock = DockStyle.Fill;

        // The body face, worn rather than inherited: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise take.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        BuildDemoHold();

        _clock.Tick += (_, _) => TickClock();
        _clock.Start();
        Disposed += (_, _) => _clock.Dispose();

        RefreshAll();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    // ------------------------------------------------------------------
    //  Building
    // ------------------------------------------------------------------

    private void BuildLayout()
    {
        // --- The card for the next race, handed to the sheet ---
        _card.StartClicked += () =>
        {
            if (_heldClicks.ClickActs())
            {
                RunStep();
            }
        };
        _card.ConnectTimerClicked += () =>
        {
            if (_blockerOpensDemoMode)
            {
                DemoModeRequested?.Invoke();
                return;
            }

            ConnectTimerRequested?.Invoke();
        };
        _card.CallClicked += () => _card.ShowNote(CallUp?.Invoke() ?? string.Empty);
        _card.SkipChosen += toTheEnd => _program.PutOffNext(toTheEnd);
        _card.SizeWanted += () => _program.NowCardResized();
        _program.HostNowCard(_card);

        // The sheet rebuilt itself - a command on its menu, a drag - so the
        // card's race may have moved on.
        _program.SheetRebuilt += () =>
        {
            if (!_refreshing)
            {
                RefreshAll();
            }
        };

        // --- Get back on time ---
        _fixList.Changed += () =>
        {
            _program.PreviewFixes(_fixList.Ticked);
            RefreshPace();
            RefreshFixButtons();
            FitParts();
        };

        _textSize.Keep(FitParts);

        _lblAdvice.AutoSize = true;
        _lblAdvice.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblAdvice, () => AppTheme.SmallFont);

        _btnApply.Click += (_, _) => ApplyFixes();
        _btnTell.Click += (_, _) =>
        {
            _program.Announce(_lastAnnouncement);
            _lastAnnouncement = string.Empty;
            RefreshFixes();
            FitParts();
        };

        _fixesCard.Add(_fixList);
        _fixesCard.Add(_lblAdvice);
        _fixesCard.Add(new ButtonBar(tight: true).With(_btnApply, _btnTell), gapBelow: 0);

        // --- Go / no-go ---
        _checkList.Acted += key => ActOnCheck(key);
        _checksCard.Add(_checkList, gapBelow: 0);

        // --- The sheet with the column beside it ---
        var middle = new Beside(() => UiLayout.AtTextSize(300));
        middle.AddFill(_program, width => _program.LeastPaneHeight(width));
        middle.AddSideCard(_fixesCard);
        middle.AddSideCard(_checksCard, gapBelow: 0);

        // --- The footer ---
        _btnMeeting.Click += (_, _) => DriversMeetingRequested?.Invoke();
        _btnReady.Click += (_, _) => ReadyToRaceRequested?.Invoke();
        _btnEditDrivers.Click += (_, _) => _program.EditDriversFromFooter();
        _btnAddRace.Click += (_, _) => _program.AddRaceFromFooter();
        _btnStandings.Click += (_, _) => _program.ShowStandingsFromFooter();
        _btnMarshals.Click += (_, _) => _program.ShowMarshalsFromFooter();
        _btnMore.Click += (_, _) => _program.ShowMoreMenu(_btnMore);
        _btnUndo.Click += (_, _) => UndoRequested?.Invoke();

        foreach (var button in new[] { _btnMeeting, _btnReady, _btnEditDrivers, _btnAddRace, _btnStandings, _btnMarshals, _btnMore, _btnUndo, _btnApply, _btnTell })
        {
            AppTheme.StyleButton(button, AppTheme.ButtonKind.Secondary);
        }

        _tips.SetToolTip(_btnMore, "Timetable, the glossary, building and printing the program - everything else is also on the sheet's right-click.");
        _tips.SetToolTip(_btnUndo, "Takes back the last change, as Ctrl+Z does.");

        _leftBar = new ButtonBar(tight: true).With(_btnMeeting, _btnReady, _btnEditDrivers, _btnAddRace);
        // A right-aligned bar fills from the right, so the last given is
        // the left-most: on screen, Standings, Marshals, More, Undo.
        var rightBar = new ButtonBar(alignRight: true, tight: true).With(_btnUndo, _btnMore, _btnMarshals, _btnStandings);

        // --- The page ---
        var page = new FillPage();
        // The clock isn't on the page: the host puts it in the Race Day tab
        // row, beside "Next Steps", where the mockup has it (Clock).
        page.Add(_strip, gapBelow: UiLayout.RowGap);
        page.AddFill(middle, middle.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(new SplitBar(_leftBar, rightBar), gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// HOLD START FOR A DEMO RUN (11 Nov 2026), as Race Control's Start Next
    /// has (StartNextHold): while the card offers a round, a press held for
    /// about a second runs it as a demo instead. A click still starts it for
    /// real. On any other step the button is a plain button.
    /// </summary>
    private void BuildDemoHold()
    {
        AppTheme.StyleToolTip(_tips, keep: true);

        var start = _card.StartButton;

        var hold = new HoldToAct(start, StartNextHold.Doing, () =>
        {
            if (DemoRound() is { } round)
            {
                _heldClicks.Held();
                RoundDemoRequested?.Invoke(round);
            }
        });

        start.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _heldClicks.Pressed();
            }
        };

        start.MouseUp += (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(_heldClicks.Pressed);
            }
        };

        start.Paint += (_, e) =>
        {
            if (DemoRound() is not null)
            {
                hold.PaintOver(e.Graphics, start.ClientRectangle, start.ForeColor, null);
            }
        };

        Disposed += (_, _) =>
        {
            hold.Dispose();
            _tips.Dispose();
        };
    }

    private RoundDefinition? DemoRound() =>
        _step is { Stage: ProgramStage.RunNext, Round: { } round } ? round : null;

    // ------------------------------------------------------------------
    //  Reading the day
    // ------------------------------------------------------------------

    /// <summary>Re-reads everything. Cheap: in-memory reads the program already makes.</summary>
    public void RefreshAll()
    {
        if (IsDisposed || _refreshing)
        {
            return;
        }

        _refreshing = true;

        try
        {
            var day = _events.Active is { IsLegacy: false } open ? open : null;

            RefreshHeader(day);
            RefreshStep();
            RefreshFixes();
            RefreshPace();
            RefreshChecks(day);
            RefreshFooter(day);
            FitParts();
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// The drawn parts' heights, from what they hold and the text size. They
    /// are not AutoSize - a column lays a part out at the height it HAS, so
    /// each is told its own (Pieces.PlaceInColumn).
    /// </summary>
    private void FitParts()
    {
        static void Fit(Control part)
        {
            var height = part.GetPreferredSize(new Size(Math.Max(1, part.Width), 0)).Height;

            if (part.Height != height)
            {
                part.Height = height;
            }
        }

        Fit(_strip);
        Fit(_fixList);
        Fit(_checkList);
    }

    private void RefreshHeader(RaceEvent? day)
    {
        if (day is null)
        {
            _header.DayLine = "No race day is open";
            return;
        }

        var parts = new List<string> { day.StartedUtc.ToLocalTime().ToString("ddd d MMM") };

        if (day.IsClubNight)
        {
            parts.Add("club night");
        }

        if (!string.IsNullOrWhiteSpace(day.VenueName))
        {
            parts.Add(day.VenueName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(day.TrackLayoutName))
        {
            parts.Add(day.TrackLayoutName.Trim());
        }

        if (!day.IsOpen)
        {
            parts.Add("closed");
        }

        _header.DayLine = string.Join(" · ", parts);
    }

    private void TickClock()
    {
        if (IsDisposed || !Visible)
        {
            return;
        }

        _header.Invalidate();

        var minute = DateTime.Now.Minute;

        if (minute != _clockMinute)
        {
            _clockMinute = minute;
            _program.RepaintSheet();
        }
    }

    /// <summary>The card: the next race, or whatever the next step is when there's no race to start.</summary>
    private void RefreshStep()
    {
        var step = _program.NextStep();
        _step = step;

        if (step is { Stage: ProgramStage.RunNext, Round: { } round })
        {
            var facts = _program.NextFacts(round);
            var start = _program.StartOfNext();
            var timerDown = _timerConnected is { } connected && !connected();
            var demoFills = DemoFillsTheProgram?.Invoke() == true;
            var demoNotFiled = !demoFills && string.Equals(UnfiledDemoRoundId?.Invoke(), round.Id, StringComparison.Ordinal);
            var blocker = BlockerFor(timerDown, demoFills, demoNotFiled);
            _blockerOpensDemoMode = blocker?.OpensDemoMode == true;

            _card.ShowRace(
                MixedHeats.ClassLabel(round),
                round.RoundLabel,
                start is { } at ? RunSheet.ShortClock(AppTime.TimeOfDay(at.ToLocalTime())) : string.Empty,
                FactsOf(round, facts),
                blocker?.Text,
                $"Start {RunSheet.RaceName(MixedHeats.ClassLabel(round), round.RoundLabel).Replace(" · ", " ")}",
                blocker?.Button ?? ConnectTimerCaption);

            _tips.SetToolTip(_card.StartButton, StartNextHold.Tip);
            _card.StartButton.AccessibleDescription = StartNextHold.AccessibleDescription;
        }
        else
        {
            var kicker = step.Stage switch
            {
                ProgramStage.NoEvent or ProgramStage.DayClosed or ProgramStage.StartLikeLast or ProgramStage.StartFromTemplate or ProgramStage.NoClassesOnFile
                    or ProgramStage.NoClasses or ProgramStage.NoDrivers or ProgramStage.NobodyCheckedIn => "FIRST",
                ProgramStage.Complete => "DONE",
                _ => "NEXT",
            };

            _card.ShowStep(kicker, step.Headline, WithoutPace(step.Detail), step.ActionCaption);
            _tips.SetToolTip(_card.StartButton, null);
            _card.StartButton.AccessibleDescription = null;
        }

        AppTheme.StyleButton(_card.StartButton, AppTheme.ButtonKind.Primary);
        _card.StartButton.Invalidate();
        _program.NowCardResized();
    }

    /// <summary>The four facts under the race's name: drivers, who's here, how long, which track.</summary>
    private static List<(string Value, string Caption)> FactsOf(RoundDefinition round, (TimeSpan Length, int Drivers, int Here, bool Counting)? facts)
    {
        var list = new List<(string, string)>();

        if (facts is not { } f)
        {
            return list;
        }

        list.Add((f.Drivers.ToString(), f.Drivers == 1 ? "driver" : "drivers"));

        if (f.Counting && f.Drivers > 0)
        {
            list.Add(($"{Math.Min(f.Here, f.Drivers)} of {f.Drivers}", "checked in"));
        }

        list.Add((RunSheet.LengthWords(f.Length), "race"));

        if (!string.IsNullOrWhiteSpace(round.TrackLayoutName))
        {
            list.Add((round.TrackLayoutName.Trim(), string.Empty));
        }

        return list;
    }

    /// <summary>
    /// A step's words without the night's plan on the end ("Finishes about
    /// ... To finish in time: ..."): the strip and the sheet say that now.
    /// </summary>
    private string WithoutPace(string detail)
    {
        if (_program.Plan() is { } plan)
        {
            var note = NightPlan.Note(plan);

            if (note.Length > 0 && detail.EndsWith(note, StringComparison.Ordinal))
            {
                return detail[..^note.Length].TrimEnd();
            }
        }

        return detail;
    }

    private void RunStep()
    {
        if (_step is { } step)
        {
            _program.RunStep(step);
            RefreshAll();
        }
    }

    // ------------------------------------------------------------------
    //  The pace strip and the fixes
    // ------------------------------------------------------------------

    private void RefreshFixes()
    {
        var (plan, _) = _program.SheetPlan();
        var choices = RunSheet.ChoicesFor(plan);

        // The same fixes as last time keep their ticks; new ones start clear.
        if (!choices.Select(c => (c.Kind, c.Label)).SequenceEqual(_choices.Select(c => (c.Kind, c.Label))))
        {
            _choices = choices;
            _fixList.SetChoices(choices.Where(c => c.CanTick).ToList());
            _program.PreviewFixes(_fixList.Ticked);
        }

        var advice = choices.Where(c => !c.CanTick).ToList();
        _lblAdvice.Text = advice.Count == 0
            ? string.Empty
            : "Also: " + string.Join("; ", advice.Select(c => $"{char.ToLowerInvariant(c.Label[0])}{c.Label[1..]} ({c.SavesWords})")) + ".";
        _lblAdvice.Visible = advice.Count > 0;

        // A change from another race day is never told to tonight's room.
        if (_lastAnnouncement.Length > 0 && !string.Equals(_lastAnnouncementDayId, _program.ActiveDayId, StringComparison.Ordinal))
        {
            _lastAnnouncement = string.Empty;
        }

        // The card stays while a change is waiting to be told (20 Nov 2026).
        //
        // WHY: a fix that brings the night back on time leaves no fixes on
        // offer, and the card used to hide with them - taking Tell the
        // Drivers with it, at the very moment there was something to tell.
        // Now it stays with just that button until it's pressed (or the
        // night needs fixing again, when the fixes come back above it).
        _fixesCard.Visible = choices.Count > 0 || _lastAnnouncement.Length > 0;
        _btnApply.Visible = choices.Any(c => c.CanTick);
        RefreshFixButtons();
    }

    private void RefreshFixButtons()
    {
        var ticked = _fixList.Ticked.Count;

        _btnApply.Text = ticked switch
        {
            0 => "Apply",
            1 => "Apply 1 Change",
            _ => $"Apply {ticked} Changes",
        };
        _btnApply.Enabled = ticked > 0;
        _btnTell.Enabled = _lastAnnouncement.Length > 0;
    }

    private void RefreshPace()
    {
        var (plan, preview) = _program.SheetPlan();
        var pace = RunSheet.PaceOf(plan?.Over);

        if (plan is not { OutByUtc: { } outBy, Over: { } over } || pace == RunSheet.Pace.OnTime)
        {
            _strip.Visible = false;
            return;
        }

        var left = plan.Slots.Count;

        _strip.Show(
            pace,
            RunSheet.LateWords(over),
            "Ends " + AppTime.TimeOfDay(plan.FinishUtc.ToLocalTime()),
            $" · out by {AppTime.TimeOfDay(outBy.ToLocalTime())} · {Plural.Of(left, "race")} left",
            preview is { } then ? (_fixList.Ticked.Count == 1 ? "With the fix ticked: " : "With the fixes ticked: ", "ends " + AppTime.TimeOfDay(then.ToLocalTime())) : null);
        _strip.Visible = true;
    }

    private void ApplyFixes()
    {
        var ticked = _fixList.Ticked;

        if (ticked.Count == 0)
        {
            return;
        }

        var (ok, message, announcement) = _program.ApplyFixes(ticked);

        if (!ok)
        {
            AppDialog.Show(this, message, "Get back on time", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _lastAnnouncement = announcement;
        _lastAnnouncementDayId = _program.ActiveDayId;
        _choices = Array.Empty<RunSheet.Choice>();
        RefreshAll();
        _card.ShowNote(message);
    }

    // ------------------------------------------------------------------
    //  Go / no-go
    // ------------------------------------------------------------------

    /// <summary>One thing that has to be true before a heat can go out, with the wizard step that does it - null for the timer.</summary>
    private sealed record Check(bool Done, string Todo, int? WizardStep);

    private void RefreshChecks(RaceEvent? day)
    {
        var rows = new List<CheckList.Row>();

        // The timer, when the host can say.
        if (_timerConnected is { } connected)
        {
            rows.Add(connected()
                ? new CheckList.Row("timer", RunSheet.Mark.Tick, MissionControl.Level.Good, "Timer", " connected", null)
                : new CheckList.Row("timer", RunSheet.Mark.Square, MissionControl.Level.Problem, "Timer", " not connected", "Connect"));
        }

        // Who's here.
        var classes = _program.TodaysClassNames();
        var drivers = classes.SelectMany(name => _program.ClassRoster(name)).Select(p => p.Transponder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var here = drivers.Count(t => _checkIns.IsCheckedIn(t));
        var counting = day?.CheckedInDriversOnly == true || here > 0;

        if (drivers.Count > 0)
        {
            rows.Add(!counting
                ? new CheckList.Row("checkin", RunSheet.Mark.None, MissionControl.Level.Off, "Check-in", " not used tonight", null)
                : here >= drivers.Count
                    ? new CheckList.Row("checkin", RunSheet.Mark.Tick, MissionControl.Level.Good, $"{here} of {drivers.Count}", " checked in", null)
                    : new CheckList.Row("checkin", RunSheet.Mark.Triangle, MissionControl.Level.Watch, $"{here} of {drivers.Count}", " checked in", "Who's Out"));
        }

        // The night set up: what the setup wizard covers.
        var left = BuildChecks(day).Where(c => !c.Done).ToList();

        rows.Add(left.Count == 0
            ? new CheckList.Row("setup", RunSheet.Mark.Tick, MissionControl.Level.Good, "Night", " set up", null)
            : new CheckList.Row("setup", RunSheet.Mark.Square, MissionControl.Level.Problem, Capitalised(left[0].Todo), left.Count > 1 ? $" (+{left.Count - 1} more)" : string.Empty, "Set Up"));

        // The phones and the caller, from Mission Control.
        if (_lights?.Invoke() is { } lights)
        {
            if (lights.FirstOrDefault(t => t.Key == "website") is { } web)
            {
                rows.Add(web.Level switch
                {
                    MissionControl.Level.Good => new CheckList.Row("phones", RunSheet.Mark.Tick, web.Level, "Phones", " · relay live", null),
                    MissionControl.Level.Off => new CheckList.Row("phones", RunSheet.Mark.None, web.Level, "Phones", " · off", null),
                    _ => new CheckList.Row("phones", RunSheet.Mark.Triangle, MissionControl.Level.Watch, "Phones", " · " + web.Value.ToLowerInvariant(), null),
                });
            }

            if (lights.FirstOrDefault(t => t.Key == "sound") is { } sound)
            {
                rows.Add(sound.Level == MissionControl.Level.Good
                    ? new CheckList.Row("sound", RunSheet.Mark.Tick, sound.Level, "Caller", " on", null)
                    : new CheckList.Row("sound", RunSheet.Mark.Triangle, MissionControl.Level.Watch, "Caller", " muted", null));
            }
        }

        _checkList.SetRows(rows);
    }

    private void ActOnCheck(string key)
    {
        switch (key)
        {
            case "timer":
                ConnectTimerRequested?.Invoke();
                break;

            case "checkin":
                _program.ShowEntriesTab();
                break;

            case "setup":
                SetUpRequested?.Invoke(FirstSetupStep());
                break;
        }
    }

    private static string Capitalised(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void RefreshFooter(RaceEvent? day)
    {
        _btnMeeting.Visible = day is not null && DriversMeetingRequested is not null;

        // Set up, and nothing raced yet: the start-of-night checklist.
        var rounds = _program.TodaysRounds();
        _btnReady.Visible = ReadyToRaceRequested is not null
                            && rounds.Count > 0 && !rounds.Any(r => r.IsRecorded)
                            && BuildChecks(day).All(c => c.Done);
        _btnAddRace.Visible = day is not null;
    }

    /// <summary>
    /// The wizard step to open at for whatever is not done yet - the
    /// EARLIEST, in the wizard's own order. The day step when nothing the
    /// wizard covers is left.
    ///
    /// Public for Set Up Tonight opened with no day: once Start Tonight has
    /// started one, the wizard opens at this step (setup plan step 8).
    /// </summary>
    public int FirstSetupStep()
    {
        var day = _events.Active is { IsLegacy: false } open ? open : null;
        var step = BuildChecks(day).Where(c => !c.Done && c.WizardStep is not null).Select(c => c.WizardStep!.Value).DefaultIfEmpty(-1).Min();

        return step >= 0 ? step : RaceDaySetupWizard.StepDay;
    }

    /// <summary>The things the setup wizard covers that have to be true before a heat can go out.</summary>
    private List<Check> BuildChecks(RaceEvent? day)
    {
        var classesOnFile = _raceClasses.All.Count;
        var classes = _program.TodaysClassNames();
        var drivers = classes.Sum(name => _program.ClassRoster(name).Count);
        var rounds = _program.TodaysRounds();
        var checkedInOnly = day?.CheckedInDriversOnly == true;

        var driversStep = classes.Count == 0
            ? RaceDaySetupWizard.StepRacing
            : checkedInOnly
                ? RaceDaySetupWizard.StepWhosHere
                : RaceDaySetupWizard.StepDrivers;

        return new List<Check>
        {
            new(day is { IsOpen: true },
                day is null ? "open tonight's race day" : "start a new race day (this one is closed)",
                RaceDaySetupWizard.StepDay),
            new(classesOnFile > 0, "add your classes", RaceDaySetupWizard.StepClasses),
            new(classes.Count > 0, "choose tonight's classes", RaceDaySetupWizard.StepRacing),
            new(drivers > 0, checkedInOnly ? "check drivers in" : "add drivers to tonight's classes", driversStep),
            new(rounds.Count > 0,
                day?.IsClubNight == true ? "build the races" : "build the program",
                RaceDaySetupWizard.StepProgramme),
        };
    }
}

/// <summary>The line across the top of Next Steps: the clock, large, and the day beside it.</summary>
internal sealed class TodayHeader : Control
{
    private string _dayLine = string.Empty;

    public TodayHeader()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Tag = AppTheme.KeepBackColor;
        AccessibleRole = AccessibleRole.StaticText;
    }

    public string DayLine
    {
        get => _dayLine;
        set
        {
            if (_dayLine != value)
            {
                _dayLine = value;
                AccessibleName = value;
                Invalidate();
                WidthChanged?.Invoke();
            }
        }
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(1, proposedSize.Width), AppTheme.HeadingFont.Height + 4);

    /// <summary>Raised when the day's words change, so a host that sizes it to them can.</summary>
    public event Action? WidthChanged;

    /// <summary>As wide as the clock and the day need, with room for the clock's longest time.</summary>
    public int WidthWanted()
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        return TextRenderer.MeasureText("12:59 PM", AppTheme.HeadingFont, Size.Empty, flags).Width
               + UiLayout.AtTextSize(10)
               + TextRenderer.MeasureText(_dayLine, AppTheme.SmallFont, Size.Empty, flags).Width
               + 4;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? AppTheme.Background);

        var clock = AppTime.TimeOfDay(DateTime.Now);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.Bottom;
        var dayWidth = TextRenderer.MeasureText(g, _dayLine, AppTheme.SmallFont, Size.Empty, flags).Width;
        var clockSize = TextRenderer.MeasureText(g, clock, AppTheme.HeadingFont, Size.Empty, flags);
        var gap = UiLayout.AtTextSize(10);

        // The clock and the day on one baseline, the pair centred in the
        // height it's given - a row of its own, or the tab row (see
        // MainForm's Race Day tabs).
        var line = AppTheme.HeadingFont.Height + 4;
        var top = Math.Max(0, (Height - line) / 2);
        var dayRect = new Rectangle(Width - dayWidth, top, dayWidth, line - 3);
        var clockRect = new Rectangle(dayRect.Left - gap - clockSize.Width, top, clockSize.Width, line);

        TextRenderer.DrawText(g, clock, AppTheme.HeadingFont, clockRect, AppTheme.TextDark, flags);
        TextRenderer.DrawText(g, _dayLine, AppTheme.SmallFont, dayRect, AppTheme.TextMuted, flags);
    }
}

/// <summary>
/// The strip across the top of Next Steps when the night is running late:
/// "■ 4 h 02 min late   Ends 9:01 PM · out by 5:00 PM · 8 races left", and
/// what the finish becomes with the ticked fixes. Amber, or red past
/// RunSheet.ProblemAt. Hidden when the night is on time.
/// </summary>
internal sealed class PaceStrip : Control
{
    private RunSheet.Pace _pace;
    private string _late = string.Empty;
    private string _ends = string.Empty;
    private string _rest = string.Empty;
    private (string Lead, string Then)? _preview;

    public PaceStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Tag = AppTheme.KeepBackColor;
        AccessibleRole = AccessibleRole.Alert;
        Visible = false;
    }

    public void Show(RunSheet.Pace pace, string late, string ends, string rest, (string Lead, string Then)? preview)
    {
        _pace = pace;
        _late = late;
        _ends = ends;
        _rest = rest;
        _preview = preview;
        AccessibleName = $"{late}. {ends}{rest}." + (preview is { } p ? $" {p.Lead}{p.Then}." : string.Empty);
        Invalidate();
    }

    private static int Pad => UiLayout.AtTextSize(10);

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(1, proposedSize.Width), AppTheme.HeadingFont.Height + 2 * Pad);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? AppTheme.Background);

        var ink = _pace == RunSheet.Pace.Problem ? AppTheme.Danger : AppTheme.Warning;
        var tint = RaceProgramControl.Blend(ink, AppTheme.Surface, 0.84f);
        var box = new Rectangle(0, 0, Width - 1, Height - 1);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var path = AppTheme.RoundedRect(box, AppTheme.CornerRadius))
        using (var back = new SolidBrush(tint))
        using (var edge = new Pen(ink))
        {
            g.FillPath(back, path);
            g.DrawPath(edge, path);
        }

        // The thick bar down the left edge.
        using (var bar = new SolidBrush(ink))
        using (var path = AppTheme.RoundedRect(new RectangleF(0, 0, UiLayout.AtTextSize(8) + AppTheme.CornerRadius, Height - 1), AppTheme.CornerRadius))
        {
            var clip = g.Clip;
            g.SetClip(new Rectangle(0, 0, UiLayout.AtTextSize(8), Height));
            g.FillPath(bar, path);
            g.Clip = clip;
        }

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;

        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        var x = UiLayout.AtTextSize(8) + Pad + 4;
        var mark = (int)(AppTheme.HeadingFont.Height * 0.5f);

        RaceProgramControl.DrawMark(g, new Rectangle(x, (Height - mark) / 2, mark, mark), _pace == RunSheet.Pace.Problem ? RunSheet.Mark.Square : RunSheet.Mark.Triangle, ink);
        x += mark + UiLayout.AtTextSize(8);

        x = Draw(g, _late, AppTheme.HeadingFont, ink, x, flags) + UiLayout.AtTextSize(18);
        x = Draw(g, _ends, AppTheme.BoldFont, AppTheme.TextDark, x, flags);
        var restEnd = Draw(g, _rest, AppTheme.BodyFont, AppTheme.TextMuted, x, flags);

        if (_preview is { } preview)
        {
            var then = TextRenderer.MeasureText(g, preview.Then, AppTheme.BoldFont, Size.Empty, flags).Width;
            var lead = TextRenderer.MeasureText(g, preview.Lead, AppTheme.BodyFont, Size.Empty, flags).Width;
            var right = Width - Pad - 4;
            var from = right - then - lead;

            if (from > restEnd + UiLayout.AtTextSize(16))
            {
                Draw(g, preview.Lead, AppTheme.BodyFont, AppTheme.TextMuted, from, flags);
                Draw(g, preview.Then, AppTheme.BoldFont, AppTheme.TextDark, from + lead, flags);
            }
        }
    }

    private int Draw(Graphics g, string text, Font font, Color colour, int x, TextFormatFlags flags)
    {
        var width = TextRenderer.MeasureText(g, text, font, Size.Empty, flags).Width;
        TextRenderer.DrawText(g, text, font, new Rectangle(x, 0, width + 2, Height), colour, flags);
        return x + width;
    }
}

/// <summary>
/// Next Steps' card for the next race, sat over its row on the run sheet
/// (RaceProgramControl.HostNowCard): "NEXT 5:00" and the timeline's dot in
/// the sheet's own time and dot columns, the race with its class first, the
/// facts the director checks before starting, a strip above Start when
/// something would stop it, and the buttons. When there's no race to start,
/// the step instead - its headline, its words and its one button.
/// </summary>
internal sealed class NowCard : Panel
{
    private readonly Func<(int Time, int Dot)> _gutter;
    private readonly Panel _blocker = new();
    private readonly Label _lblBlocker = new();
    private readonly Button _btnConnect = new() { Text = "Connect Timer" };
    private readonly Button _btnStart = new();
    private readonly Button _btnCall = new() { Text = "Call to the Stand" };
    private readonly Button _btnSkip = new() { Text = "Skip ▾" };
    private readonly ButtonBar _buttons;
    private readonly Label _lblNote = new();
    private readonly System.Windows.Forms.Timer _noteTimer = new() { Interval = 8000 };
    private readonly ContextMenuStrip _skipMenu;

    private bool _race;
    private string _kicker = "NEXT";
    private string _time = string.Empty;
    private string _classPart = string.Empty;
    private string _roundPart = string.Empty;
    private string _detail = string.Empty;
    private List<(string Value, string Caption)> _facts = new();

    public event Action? StartClicked;
    public event Action? ConnectTimerClicked;
    public event Action? CallClicked;
    public event Action<bool>? SkipChosen;

    /// <summary>The card's height may have changed - its host re-sizes the row.</summary>
    public event Action? SizeWanted;

    public Button StartButton => _btnStart;

    public NowCard(Func<(int Time, int Dot)> gutter)
    {
        _gutter = gutter;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        // Painted here, not by the theme: the card's own gradient and edge.
        Tag = AppTheme.KeepBackColor;

        _btnStart.Click += (_, _) => StartClicked?.Invoke();
        _btnConnect.Click += (_, _) => ConnectTimerClicked?.Invoke();
        _btnCall.Click += (_, _) => CallClicked?.Invoke();

        _skipMenu = new ContextMenuStrip { Font = AppTheme.BodyFont, ShowImageMargin = false };
        _skipMenu.Items.Add(new ToolStripMenuItem("Run It After the Next Race", null, (_, _) => SkipChosen?.Invoke(false)));
        _skipMenu.Items.Add(new ToolStripMenuItem("Move It to the End of the Night", null, (_, _) => SkipChosen?.Invoke(true)));
        AppTheme.StyleContextMenu(_skipMenu);
        _btnSkip.Click += (_, _) => _skipMenu.Show(_btnSkip, new Point(0, _btnSkip.Height));

        AppTheme.StyleButton(_btnConnect, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnCall, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnSkip, AppTheme.ButtonKind.Secondary);

        _blocker.Tag = AppTheme.KeepBackColor;
        _blocker.Paint += (_, e) => PaintBlocker(e.Graphics);
        _lblBlocker.AutoSize = false;
        _lblBlocker.AutoEllipsis = true;
        _lblBlocker.TextAlign = ContentAlignment.MiddleLeft;
        _lblBlocker.BackColor = Color.Transparent;
        _lblBlocker.Tag = AppTheme.KeepBackColor;
        _blocker.Controls.Add(_lblBlocker);
        _blocker.Controls.Add(_btnConnect);
        _blocker.Visible = false;

        _buttons = new ButtonBar(tight: true).With(_btnStart, _btnCall, _btnSkip);

        _lblNote.AutoSize = false;
        _lblNote.ForeColor = AppTheme.TextMuted;
        _lblNote.BackColor = Color.Transparent;
        _lblNote.Tag = AppTheme.KeepBackColor;
        _lblNote.Visible = false;
        _noteTimer.Tick += (_, _) =>
        {
            _noteTimer.Stop();
            _lblNote.Visible = false;
            SizeWanted?.Invoke();
        };

        Controls.Add(_blocker);
        Controls.Add(_buttons);
        Controls.Add(_lblNote);

        Disposed += (_, _) =>
        {
            _noteTimer.Dispose();
            _skipMenu.Dispose();
        };
    }

    /// <summary>The next race: its class and label, when it should go, the facts, and what would stop it.</summary>
    public void ShowRace(string classLabel, string roundLabel, string time, List<(string Value, string Caption)> facts, string? blocker, string startCaption,
        string blockerButton = "Connect Timer")
    {
        if (_btnConnect.Text != blockerButton)
        {
            _btnConnect.Text = blockerButton;
        }

        _race = true;
        _kicker = "NEXT";
        _time = time;
        _classPart = classLabel;
        _roundPart = roundLabel;
        _facts = facts;
        _detail = string.Empty;
        _lblBlocker.Text = blocker ?? string.Empty;
        _blocker.Visible = blocker is not null;
        _btnStart.Text = startCaption;
        _btnCall.Visible = true;
        _btnSkip.Visible = true;
        Changed();
    }

    /// <summary>A step that isn't a race to start: its headline, its words and its one button.</summary>
    public void ShowStep(string kicker, string headline, string detail, string caption)
    {
        _race = false;
        _kicker = kicker;
        _time = string.Empty;
        _classPart = headline;
        _roundPart = string.Empty;
        _facts = new();
        _detail = detail;
        _blocker.Visible = false;
        _btnStart.Text = caption;
        _btnCall.Visible = false;
        _btnSkip.Visible = false;
        Changed();
    }

    /// <summary>What the last command said, under the buttons for a few seconds.</summary>
    public void ShowNote(string note)
    {
        _lblNote.Text = note;
        _lblNote.Visible = !string.IsNullOrWhiteSpace(note);
        _noteTimer.Stop();

        if (_lblNote.Visible)
        {
            _noteTimer.Start();
        }

        Changed();
    }

    private void Changed()
    {
        AccessibleName = _race ? $"Next on track: {RunSheet.RaceName(_classPart, _roundPart)}, {_time}" : _classPart;

        // Room for the hold-for-a-demo dial to the left of the caption
        // (HoldToAct.PaintOver): the bar sizes its buttons to their captions
        // alone, which left the dial drawn over the end of the words.
        var font = AppTheme.BoldFont;
        var dial = (int)Math.Round(font.Height * 0.55);
        var wanted = TextRenderer.MeasureText(_btnStart.Text, font).Width + 2 * (dial + Math.Max(3, dial / 2)) + UiLayout.AtTextSize(24);
        _btnStart.MinimumSize = new Size(_race ? wanted : 0, 0);
        PerformLayout();
        Invalidate();
        SizeWanted?.Invoke();
    }

    // --- Measuring and placing ---

    private static int Pad => UiLayout.AtTextSize(10);

    /// <summary>Where the words start: past the sheet's time and dot columns, as the rows' words do.</summary>
    private int TextLeft
    {
        get
        {
            var (time, dot) = _gutter();
            return Math.Max(UiLayout.AtTextSize(60), time + dot - UiLayout.AtTextSize(4));
        }
    }

    private int TextWidth(int width) => Math.Max(60, width - TextLeft - Pad);

    private int DetailHeight(int width) =>
        _detail.Length == 0
            ? 0
            : TextRenderer.MeasureText(_detail, AppTheme.BodyFont, new Size(TextWidth(width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

    /// <summary>
    /// The strip's height in a card whose words are <paramref name="width"/> wide: its
    /// words wrapped to the room left of its button, or the button's height.
    /// WHY (20 Nov 2026): one line, cut short with "..." - "Timer not
    /// connected" already was at 200% text, and the demo night's line names a
    /// setting, which needs two lines at 1280x720.
    /// </summary>
    private int BlockerHeight(int width)
    {
        var words = TextRenderer.MeasureText(_lblBlocker.Text, AppTheme.BoldFont, new Size(Math.Max(40, BlockerWordsWidth(width)), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

        return Math.Max(Math.Max(_btnConnect.Height, AppTheme.BodyFont.Height), words) + UiLayout.AtTextSize(8);
    }

    private static int BlockerMarkRoom => UiLayout.AtTextSize(28);

    /// <summary>How wide the strip's words may be in a strip <paramref name="width"/> wide: up to its button.</summary>
    private int BlockerWordsWidth(int width) =>
        width - _btnConnect.GetPreferredSize(Size.Empty).Width - UiLayout.AtTextSize(4) - BlockerMarkRoom - 4;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = Math.Max(200, proposedSize.Width);
        var y = Pad + AppTheme.HeadingFont.Height + UiLayout.AtTextSize(4);

        y += _race ? AppTheme.BoldFont.Height + UiLayout.AtTextSize(8) : DetailHeight(width) + UiLayout.AtTextSize(8);

        if (_blocker.Visible)
        {
            y += BlockerHeight(TextWidth(width)) + UiLayout.AtTextSize(8);
        }

        y += _buttons.GetPreferredSize(new Size(TextWidth(width), 0)).Height;

        if (_lblNote.Visible)
        {
            y += UiLayout.AtTextSize(4) + AppTheme.SmallFont.Height;
        }

        return new Size(width, y + Pad);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        var left = TextLeft;
        var width = TextWidth(Width);
        var y = Pad + AppTheme.HeadingFont.Height + UiLayout.AtTextSize(4);

        y += _race ? AppTheme.BoldFont.Height + UiLayout.AtTextSize(8) : DetailHeight(Width) + UiLayout.AtTextSize(8);

        if (_blocker.Visible)
        {
            var blockerHeight = BlockerHeight(width);
            _blocker.Bounds = new Rectangle(left, y, width, blockerHeight);
            var buttonWidth = _btnConnect.GetPreferredSize(Size.Empty).Width;
            _btnConnect.Bounds = new Rectangle(_blocker.Width - buttonWidth - UiLayout.AtTextSize(4), (_blocker.Height - _btnConnect.Height) / 2, buttonWidth, _btnConnect.Height);
            _lblBlocker.Font = AppTheme.BoldFont;
            _lblBlocker.ForeColor = AppTheme.Warning;
            var markRoom = BlockerMarkRoom;
            _lblBlocker.Bounds = new Rectangle(markRoom, 0, Math.Max(0, _btnConnect.Left - markRoom - 4), _blocker.Height);
            y += blockerHeight + UiLayout.AtTextSize(8);
        }

        var bar = _buttons.GetPreferredSize(new Size(width, 0));
        _buttons.Bounds = new Rectangle(left, y, width, bar.Height);
        y += bar.Height;

        if (_lblNote.Visible)
        {
            _lblNote.Font = AppTheme.SmallFont;
            _lblNote.Bounds = new Rectangle(left, y + UiLayout.AtTextSize(4), width, AppTheme.SmallFont.Height);
        }
    }

    // --- Drawing ---

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var back = Parent?.BackColor ?? AppTheme.Surface;
        g.Clear(back);

        var box = new Rectangle(1, 1, Width - 3, Height - 3);
        var top = RaceProgramControl.Blend(AppTheme.Primary, AppTheme.Surface, 0.86f);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var path = AppTheme.RoundedRect(box, AppTheme.CornerRadius - 2))
        using (var fill = new System.Drawing.Drawing2D.LinearGradientBrush(box, top, AppTheme.Surface, 90f))
        using (var edge = new Pen(AppTheme.Primary, 2))
        {
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }

        var (timeWidth, dotWidth) = _gutter();
        var inset = UiLayout.AtTextSize(4);

        // "NEXT" over the time, right-aligned in the sheet's time column.
        var timeRect = new Rectangle(0, Pad, Math.Max(10, timeWidth - inset - 8), AppTheme.MicroFont.Height);
        TextRenderer.DrawText(g, _kicker, AppTheme.MicroFont, timeRect, AppTheme.Primary, TextFormatFlags.Right | TextFormatFlags.NoPadding);

        if (_time.Length > 0)
        {
            var clock = new Rectangle(0, timeRect.Bottom + 2, timeRect.Width, AppTheme.BoldFont.Height + 4);
            TextRenderer.DrawText(g, _time, AppTheme.BoldFont, clock, AppTheme.TextDark, TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }

        // The timeline's dot, in the sheet's dot column, with its halo.
        var side = Math.Max(10, AppTheme.SmallFont.Height * 2 / 3);
        var cx = timeWidth - inset + dotWidth / 2;
        var cy = Pad + AppTheme.HeadingFont.Height / 2;

        using (var halo = new SolidBrush(Color.FromArgb(70, AppTheme.Primary)))
        {
            g.FillEllipse(halo, cx - side, cy - side, side * 2, side * 2);
        }

        using (var dot = new SolidBrush(AppTheme.Primary))
        {
            g.FillEllipse(dot, cx - side / 2, cy - side / 2, side, side);
        }

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;

        // The race: its class first, the round in the accent.
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var x = TextLeft;
        var y = Pad;
        var width = TextWidth(Width);

        if (_race)
        {
            var first = _classPart.Length > 0 ? _classPart + " · " : string.Empty;
            var firstWidth = TextRenderer.MeasureText(g, first, AppTheme.HeadingFont, Size.Empty, flags).Width;
            TextRenderer.DrawText(g, first, AppTheme.HeadingFont, new Point(x, y), AppTheme.TextDark, flags);
            TextRenderer.DrawText(g, _roundPart, AppTheme.HeadingFont, new Rectangle(x + firstWidth, y, Math.Max(0, width - firstWidth), AppTheme.HeadingFont.Height + 2), AppTheme.SynthCyan, flags | TextFormatFlags.EndEllipsis);

            // The facts: each value bold, its caption muted.
            y += AppTheme.HeadingFont.Height + UiLayout.AtTextSize(4);
            var fx = x;

            foreach (var (value, caption) in _facts)
            {
                var valueWidth = TextRenderer.MeasureText(g, value, AppTheme.BoldFont, Size.Empty, flags).Width;
                TextRenderer.DrawText(g, value, AppTheme.BoldFont, new Point(fx, y), AppTheme.TextDark, flags);
                fx += valueWidth;

                if (caption.Length > 0)
                {
                    var words = " " + caption;
                    TextRenderer.DrawText(g, words, AppTheme.BodyFont, new Point(fx, y + (AppTheme.BoldFont.Height - AppTheme.BodyFont.Height) / 2), AppTheme.TextMuted, flags);
                    fx += TextRenderer.MeasureText(g, words, AppTheme.BodyFont, Size.Empty, flags).Width;
                }

                fx += UiLayout.AtTextSize(22);
            }
        }
        else
        {
            TextRenderer.DrawText(g, _classPart, AppTheme.HeadingFont, new Rectangle(x, y, width, AppTheme.HeadingFont.Height + 2), AppTheme.TextDark, flags | TextFormatFlags.EndEllipsis);
            y += AppTheme.HeadingFont.Height + UiLayout.AtTextSize(4);

            if (_detail.Length > 0)
            {
                TextRenderer.DrawText(g, _detail, AppTheme.BodyFont, new Rectangle(x, y, width, DetailHeight(Width)), AppTheme.TextMuted,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }
    }

    private void PaintBlocker(Graphics g)
    {
        var back = RaceProgramControl.Blend(AppTheme.Warning, AppTheme.Surface, 0.84f);
        g.Clear(RaceProgramControl.Blend(AppTheme.Primary, AppTheme.Surface, 0.9f));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var path = AppTheme.RoundedRect(new RectangleF(0, 0, _blocker.Width - 1, _blocker.Height - 1), 8))
        using (var fill = new SolidBrush(back))
        {
            g.FillPath(fill, path);
        }

        var mark = AppTheme.BodyFont.Height * 2 / 3;
        RaceProgramControl.DrawMark(g, new Rectangle(UiLayout.AtTextSize(10), (_blocker.Height - mark) / 2, mark, mark), RunSheet.Mark.Triangle, AppTheme.Warning);
        _lblBlocker.BackColor = back;
    }
}

/// <summary>
/// The "Get back on time" card's tick boxes: one row per fix the app can
/// make itself, its saving in green beside it. A click ticks or clears it,
/// and the sheet shows it before anything changes (Changed).
/// </summary>
internal sealed class FixList : Control
{
    private List<RunSheet.Choice> _choices = new();
    private readonly HashSet<int> _ticked = new();
    private int _hot = -1;

    public event Action? Changed;

    public FixList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Tag = AppTheme.KeepBackColor;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
    }

    public IReadOnlyList<RunSheet.Choice> Ticked => _ticked.OrderBy(i => i).Where(i => i < _choices.Count).Select(i => _choices[i]).ToList();

    public void SetChoices(List<RunSheet.Choice> choices)
    {
        _choices = choices;
        _ticked.Clear();
        _hot = -1;
        AccessibleName = string.Join(", ", choices.Select(c => $"{c.Label}, saves {c.SavesWords}"));
        Parent?.PerformLayout();
        Invalidate();
    }

    private static int RowHeight => AppTheme.BodyFont.Height + UiLayout.AtTextSize(16);

    private static int Gap => UiLayout.AtTextSize(8);

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(1, proposedSize.Width), _choices.Count == 0 ? 0 : _choices.Count * RowHeight + (_choices.Count - 1) * Gap);

    private int RowAt(int y)
    {
        var index = y / (RowHeight + Gap);
        return index >= 0 && index < _choices.Count && y - index * (RowHeight + Gap) < RowHeight ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = RowAt(e.Y);

        if (hot != _hot)
        {
            _hot = hot;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        Toggle(RowAt(e.Y));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode is Keys.Space && _choices.Count > 0)
        {
            Toggle(_hot >= 0 ? _hot : 0);
            e.Handled = true;
        }
    }

    private void Toggle(int index)
    {
        if (index < 0 || index >= _choices.Count)
        {
            return;
        }

        if (!_ticked.Remove(index))
        {
            // One dropped round at a time: two "drop" choices can't both be ticked.
            _ticked.Add(index);
        }

        Invalidate();
        Changed?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? AppTheme.Surface);

        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        for (var i = 0; i < _choices.Count; i++)
        {
            var choice = _choices[i];
            var ticked = _ticked.Contains(i);
            var row = new Rectangle(0, i * (RowHeight + Gap), Width - 1, RowHeight - 1);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (var path = AppTheme.RoundedRect(row, 8))
            using (var fill = new SolidBrush(i == _hot ? AppTheme.SecondaryButtonHover : RaceProgramControl.Blend(AppTheme.SecondaryButton, AppTheme.Surface, 0.35f)))
            using (var edge = new Pen(ticked ? AppTheme.Success : AppTheme.Border, ticked ? 2 : 1))
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            }

            var side = AppTheme.BodyFont.Height - 2;
            var box = new Rectangle(row.X + UiLayout.AtTextSize(10), row.Y + (row.Height - side) / 2, side, side);

            using (var path = AppTheme.RoundedRect(box, 4))
            {
                if (ticked)
                {
                    using var fill = new SolidBrush(AppTheme.Success);
                    g.FillPath(fill, path);
                    RaceProgramControl.DrawMark(g, Rectangle.Inflate(box, -3, -3), RunSheet.Mark.Tick, AppTheme.OnSuccess);
                }
                else
                {
                    using var edge = new Pen(AppTheme.TextMuted, 2);
                    g.DrawPath(edge, path);
                }
            }

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;

            var saves = choice.SavesWords;
            // Measured and drawn without the ellipsis - it is never cut, the label is.
            var whole = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
            var savesWidth = TextRenderer.MeasureText(g, saves, AppTheme.BoldFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 4;
            var right = row.Right - UiLayout.AtTextSize(10);
            TextRenderer.DrawText(g, saves, AppTheme.BoldFont, new Rectangle(right - savesWidth, row.Y, savesWidth, row.Height), AppTheme.Success, whole);

            var textLeft = box.Right + UiLayout.AtTextSize(10);
            TextRenderer.DrawText(g, choice.Label, AppTheme.BodyFont,
                new Rectangle(textLeft, row.Y, Math.Max(0, right - savesWidth - textLeft - 8), row.Height), AppTheme.TextDark, flags);
        }

        if (Focused && _choices.Count > 0)
        {
            ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width, Math.Min(Height, RowHeight)));
        }
    }
}

/// <summary>
/// The "Go / no-go" card's list: a row per check, a shape and words on each.
/// A problem gets a red fill and a fix button, something to watch an amber
/// fill; a check that's fine is plain grey text. Only problems are loud.
/// </summary>
internal sealed class CheckList : Panel
{
    /// <summary>One check: what it's about (for the button), its shape and level, its bold words and the rest, and its button's caption.</summary>
    public sealed record Row(string Key, RunSheet.Mark Mark, MissionControl.Level Level, string Bold, string Rest, string? Action);

    private List<Row> _rows = new();
    private readonly List<Button> _buttons = new();

    public event Action<string>? Acted;

    public CheckList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Tag = AppTheme.KeepBackColor;
        AccessibleRole = AccessibleRole.List;
    }

    public void SetRows(List<Row> rows)
    {
        if (rows.SequenceEqual(_rows))
        {
            return;
        }

        _rows = rows;
        AccessibleName = string.Join(". ", rows.Select(r => r.Bold + r.Rest));

        foreach (var old in _buttons)
        {
            Controls.Remove(old);
            old.Dispose();
        }

        _buttons.Clear();

        foreach (var row in rows)
        {
            if (row.Action is not { } caption)
            {
                continue;
            }

            var key = row.Key;
            var button = new Button { Text = caption, Tag = key };
            AppTheme.StyleButton(button, AppTheme.ButtonKind.Secondary);
            button.Font = AppTheme.SmallFont;
            button.Click += (_, _) => Acted?.Invoke(key);
            _buttons.Add(button);
            Controls.Add(button);
        }

        Parent?.PerformLayout();
        PerformLayout();
        Invalidate();
    }

    // Tall enough for a fix button: the theme draws buttons in the bold face, whatever they were given (17 Nov 2026 - its words were cut off at the bottom).
    // 18 Nov 2026: the fix buttons were still a few pixels short of their words, so they're sized as the layout check measures a button, and a row grows to hold one.
    private int RowHeight => Math.Max(
        Math.Max(AppTheme.BodyFont.Height + UiLayout.AtTextSize(14), AppTheme.BoldFont.Height + UiLayout.AtTextSize(16)),
        _buttons.Count == 0 ? 0 : _buttons.Max(b => ButtonSize(b).Height) + UiLayout.AtTextSize(4));

    private static int Gap => UiLayout.AtTextSize(6);

    /// <summary>
    /// What a fix button needs for its words, in its own font or the bold one
    /// the theme may draw it in: the caption and 22 across (or the padded
    /// caption and 16), and a line and 12 tall.
    /// </summary>
    private static Size ButtonSize(Button button)
    {
        var width = 0;
        var height = 0;

        foreach (var font in new[] { button.Font, AppTheme.BoldFont })
        {
            var tight = TextRenderer.MeasureText(button.Text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            var padded = TextRenderer.MeasureText(button.Text, font);
            width = Math.Max(width, Math.Max(tight.Width + 22, padded.Width + 16));
            height = Math.Max(height, padded.Height + 12);
        }

        return new Size(width + UiLayout.AtTextSize(4), height);
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(1, proposedSize.Width), _rows.Count == 0 ? 0 : _rows.Count * RowHeight + (_rows.Count - 1) * Gap);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        for (var i = 0; i < _rows.Count; i++)
        {
            if (_buttons.FirstOrDefault(b => Equals(b.Tag, _rows[i].Key)) is not { } button)
            {
                continue;
            }

            var size = ButtonSize(button);
            var (width, height) = (size.Width, size.Height);
            var rowHeight = RowHeight;
            var y = i * (rowHeight + Gap) + (rowHeight - height) / 2;
            button.Bounds = new Rectangle(Width - width - UiLayout.AtTextSize(6), y, width, height);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? AppTheme.Surface);

        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var rect = new Rectangle(0, i * (RowHeight + Gap), Width - 1, RowHeight - 1);
            var ink = row.Level switch
            {
                MissionControl.Level.Problem => AppTheme.Danger,
                MissionControl.Level.Watch => AppTheme.Warning,
                MissionControl.Level.Good => AppTheme.Success,
                _ => AppTheme.TextMuted,
            };
            var loud = row.Level is MissionControl.Level.Problem or MissionControl.Level.Watch;

            if (loud)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                using var path = AppTheme.RoundedRect(rect, 7);
                using var fill = new SolidBrush(RaceProgramControl.Blend(ink, AppTheme.Surface, 0.84f));
                g.FillPath(fill, path);

                if (row.Level == MissionControl.Level.Problem)
                {
                    using var edge = new Pen(ink);
                    g.DrawPath(edge, path);
                }

                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            }

            var mark = AppTheme.BodyFont.Height * 3 / 5;
            var x = rect.X + UiLayout.AtTextSize(10);

            if (row.Mark != RunSheet.Mark.None)
            {
                RaceProgramControl.DrawMark(g, new Rectangle(x, rect.Y + (rect.Height - mark) / 2, mark, mark), row.Mark, ink);
            }

            x += mark + UiLayout.AtTextSize(10);

            var boldInk = loud ? AppTheme.TextDark : AppTheme.TextMuted;
            var boldFont = loud ? AppTheme.BoldFont : AppTheme.BodyFont;
            var boldWidth = TextRenderer.MeasureText(g, row.Bold, boldFont, Size.Empty, flags).Width;
            var right = _buttons.FirstOrDefault(b => Equals(b.Tag, row.Key)) is { } button ? button.Left - 6 : rect.Right - 6;

            TextRenderer.DrawText(g, row.Bold, boldFont, new Rectangle(x, rect.Y, Math.Max(0, right - x), rect.Height), boldInk, flags | TextFormatFlags.EndEllipsis);
            x += boldWidth;
            TextRenderer.DrawText(g, row.Rest, AppTheme.BodyFont, new Rectangle(x, rect.Y, Math.Max(0, right - x), rect.Height), AppTheme.TextMuted, flags | TextFormatFlags.EndEllipsis);
        }
    }
}
