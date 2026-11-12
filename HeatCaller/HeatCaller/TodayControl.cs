namespace HeatCaller;

/// <summary>
/// The Race Day page's landing tab: what to do next, where the day has got
/// to, and what is still missing.
///
/// WHY THIS EXISTS. The app already worked out the one next thing a race
/// director should do - ProgramNextStep does it, and does it well - but the
/// only place that said so was a banner on the third sub-tab of a page a
/// new user had to find first. The phone has had this screen for a while
/// (RaceDayOpsToday's today.summary, which the manager protocol describes
/// as "the one-screen overview... poll this for a Today home tab"); the
/// laptop, the one machine that is definitely in the room, never got it.
///
/// WHAT IT IS NOT. Not a second way to do anything. Every button here runs
/// something another part of the app already owns - the next-step button
/// hands the step straight back to the programme tab, and the Ready to Race
/// button opens the Set Up Tonight wizard at the first step not done yet.
/// There is deliberately no state of its own to get out of step with the
/// rest of the page.
///
/// Built on the layout pieces (LayoutPieces.cs), so the page holds at any
/// text size and window size - see BuildLayout.
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

    // --- Where the fix-it links go. The host owns navigation; this control
    //     deliberately knows nothing about tabs or pages. ---

    // The checklist that was here had a fix-it button per line, each one a
    // trip to a different page. It is a progress bar now, and the one
    // button under it opens the Set Up Tonight wizard at the first step
    // that is not done - the wizard is where setting up happens, and a
    // list below the fold that nobody scrolled down to was not.

    /// <summary>
    /// Raised to open the Set Up Tonight wizard at a step - one of the
    /// RaceDaySetupWizard.Step constants. The host owns the wizard.
    /// </summary>
    public event Action<int>? SetUpRequested;

    /// <summary>Raised when the timer needs connecting.</summary>
    public event Action? ConnectTimerRequested;

    /// <summary>Raised when the Start button is held for a demo run of its round - see BuildDemoHold. The host runs it as the programme's Demo does.</summary>
    public event Action<RoundDefinition>? RoundDemoRequested;

    /// <summary>Swallows the click a completed hold's let-go makes - see HeldClickFilter.</summary>
    private readonly HeldClickFilter _heldClicks = new();

    /// <summary>The Start button's "hold for a demo" tip, on a round step only.</summary>
    private readonly ToolTip _tips = new();


    /// <summary>
    /// How tall the page's content is, so the host can give this half of
    /// the split enough room to show all of it without scrolling.
    /// </summary>
    public int ContentHeight => _page.ContentHeight;

    /// <summary>
    /// Raised when <see cref="ContentHeight"/> changes - once the layout
    /// that changed it is over, since the host answers by moving the
    /// splitter, which resizes this page. See PostContentHeightChanged.
    /// </summary>
    public event Action? ContentHeightChanged;

    /// <summary>The page the parts go on. Scrolls down, never sideways.</summary>
    private readonly PageStack _page = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>Set while a ContentHeightChanged is on its way, so a burst of layouts sends one.</summary>
    private bool _contentHeightPosted;

    // --- The parts that get rewritten on every refresh ---

    private readonly Label _lblDayName = new();
    private readonly Label _lblDayLine = new();

    private readonly Label _lblKicker = new();
    private readonly Label _lblHeadline = new();
    private readonly Label _lblDetail = new();
    private readonly Button _btnPrimary = new();

    private readonly Label _lblGlance = new();
    private readonly List<StatTile> _tiles = new();

    // --- Ready to race: a bar, a count, what is left, and one button ---
    private readonly Panel _barReady = new();
    private readonly Label _lblReadyCount = new();
    private readonly Label _lblTodo = new();
    private readonly Button _btnFinish = new();

    /// <summary>The bar the button is in, hidden with it so it leaves no gap under what is left to do.</summary>
    private ButtonBar? _finishBar;

    private int _readyDone;
    private int _readyTotal;

    /// <summary>
    /// Whether the button is showing. Our own record, never Control.Visible,
    /// which reads false for everything on a page that is not on screen yet.
    /// </summary>
    private bool _finishShown;

    /// <summary>What the button does, swapped on each refresh so the handler is wired once.</summary>
    private Action? _onFinish;

    // The glossary and the timetable buttons were here too, as a row at the
    // top of the page. Both are on the programme's footer underneath - the
    // glossary already was, so this was a second copy of it.

    /// <summary>The step the button is currently offering. Held so the caption and the command cannot drift apart.</summary>
    private ProgramStep? _step;

    /// <summary>How many tiles are built. Fixed, so the layout never has to grow or shrink the page.</summary>
    private const int TileCount = 5;

    /// <summary>The progress bar's size at the normal text size. It grows with the text beside it.</summary>
    private const int ReadyBarWidth = 240;
    private const int ReadyBarHeight = 8;

    /// <summary>The Ready to Race card's width at the normal text size, before it is measured - see ReadyWidth.</summary>
    private const int ReadyCardWidth = 360;

    public TodayControl(
        RaceProgramControl program,
        RaceEventStore events,
        ProfileStore profiles,
        RaceClassStore raceClasses,
        CheckInStore checkIns,
        DayScheduleStore? schedule = null,
        Func<bool>? timerConnected = null)
    {
        _program = program;
        _events = events;
        _profiles = profiles;
        _raceClasses = raceClasses;
        _checkIns = checkIns;
        _schedule = schedule;
        _timerConnected = timerConnected;

        Dock = DockStyle.Fill;

        // No MinimumSize, on purpose. This page is the upper half of a split
        // pane - the programme grid is underneath it - and WinForms honours
        // MinimumSize over the size the parent hands out, so a height floor
        // here would have made the control taller than its pane and left the
        // bottom of it unreachable. The page it is built on scrolls instead
        // (PageStack), and only ever down: nothing on it is wider than the
        // pane, the tiles included.
        //
        // The body face, worn rather than inherited: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise take.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// A new text size, reaching this page through its tab's bold face -
    /// the only way it does, since the page wears a face of its own. See
    /// TextSize.
    /// </summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    // ------------------------------------------------------------------
    //  Building
    // ------------------------------------------------------------------

    /// <summary>
    /// Three bands, top to bottom, on the layout pieces (LayoutPieces.cs):
    ///
    ///   the day's name, and the line under it;
    ///   the next-step card, with the Ready to Race card beside it;
    ///   the day so far - five tiles across.
    ///
    /// It used to be one tall column - card, then tiles, then the
    /// checklist, then the two buttons - and in the top half of a split
    /// page most of that was below the fold. Then it was these three bands
    /// placed by hand on every resize, against a floor of 900px across that
    /// scrolled the pane sideways below it. Now nothing is placed by hand,
    /// the page only scrolls down, and the two cards share a height so they
    /// read as a pair.
    /// </summary>
    private void BuildLayout()
    {
        // --- The day ---
        _lblDayName.AutoSize = true;
        _lblDayName.ForeColor = AppTheme.TextDark;
        _textSize.Wear(_lblDayName, () => AppTheme.TitleFont);

        _lblDayLine.AutoSize = true;
        _lblDayLine.ForeColor = AppTheme.TextMuted;

        // --- The next-step card ---
        _lblKicker.AutoSize = true;
        _lblKicker.ForeColor = AppTheme.Primary;
        _textSize.Wear(_lblKicker, () => AppTheme.MicroFont);

        _lblHeadline.AutoSize = true;
        _lblHeadline.ForeColor = AppTheme.TextDark;
        _textSize.Wear(_lblHeadline, () => AppTheme.HeadingFont);

        _lblDetail.AutoSize = true;
        _lblDetail.ForeColor = AppTheme.TextMuted;

        // A click runs the step; a let-go that has just finished a hold
        // (below) does not run it for real as well.
        _btnPrimary.Click += (_, _) =>
        {
            if (_heldClicks.ClickActs())
            {
                RunStep();
            }
        };

        BuildDemoHold();

        // No caption of its own: the kicker is its caption, and it changes
        // with the day ("BEFORE YOU START", "NEXT STEP", "ALL DONE").
        var step = new Section();

        step.Add(_lblKicker, gapBelow: UiLayout.HintGap);
        step.Add(_lblHeadline, gapBelow: UiLayout.HintGap);
        step.Add(_lblDetail);
        step.Add(new ButtonBar().With(_btnPrimary), gapBelow: 0);

        // --- Ready to race: a bar, a count, what is left, and one button ---
        //
        // The bar sits on the card now, so it draws its track in the page's
        // colour - a groove in the card - rather than the card's, which it
        // would vanish into. See PaintReadyBar.
        _barReady.Tag = AppTheme.KeepBackColor;
        _barReady.Paint += (_, e) => PaintReadyBar(e.Graphics);
        AppTheme.EnableDoubleBuffering(_barReady);

        _textSize.Keep(() =>
        {
            var size = new Size(UiLayout.AtTextSize(ReadyBarWidth), UiLayout.AtTextSize(ReadyBarHeight));

            if (_barReady.Size != size)
            {
                _barReady.Size = size;
            }
        });

        _lblReadyCount.AutoSize = true;
        _lblReadyCount.ForeColor = AppTheme.TextDark;
        _textSize.Wear(_lblReadyCount, () => AppTheme.BoldFont);

        _lblTodo.AutoSize = true;
        _lblTodo.ForeColor = AppTheme.TextMuted;

        _btnFinish.Click += (_, _) => _onFinish?.Invoke();

        var ready = new Section("Ready to race");

        ready.Add(FieldGrid.Inline(_barReady, _lblReadyCount));
        ready.Add(_lblTodo);

        _finishBar = ready.Add(new ButtonBar().With(_btnFinish), gapBelow: 0);

        var band = new Beside(ReadyWidth)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        band.AddFill(step, width => step.GetPreferredSize(new Size(width, 0)).Height);
        band.AddSideCard(ready);

        // --- The day so far ---
        //
        // Tiles built once, filled on refresh. Rebuilding controls on every
        // refresh is what makes a page flicker, and this one refreshes every
        // time the tab is opened and every time a round is recorded.
        _lblGlance.AutoSize = true;
        _lblGlance.ForeColor = AppTheme.TextMuted;
        _lblGlance.Text = AppTheme.Spaced("THE DAY SO FAR");
        _textSize.Wear(_lblGlance, () => AppTheme.MicroFont);

        for (var i = 0; i < TileCount; i++)
        {
            var tile = new StatTile();

            _textSize.Wear(tile.Value, () => AppTheme.TitleFont);
            _textSize.Wear(tile.Caption, () => AppTheme.MicroFont);
            _tiles.Add(tile);
        }

        // --- The page ---
        _page.Add(_lblDayName, gapBelow: UiLayout.HintGap);
        _page.Add(_lblDayLine, gapBelow: UiLayout.SectionGap);
        _page.Add(band);
        _page.Add(_lblGlance, gapBelow: UiLayout.RowGap);
        _page.Add(new TileStrip(_tiles), gapBelow: 0);

        _page.ContentHeightChanged += PostContentHeightChanged;

        // No blank band under the tiles either: the splitter is right under
        // them, and ContentHeight counts the room the theme leaves under a
        // scrolling page - so the split gave 32px to nothing, where the page's
        // own 16px margin will do. See ScrollTheming.NoRoomBelow.
        ScrollTheming.NoRoomBelow(_page);

        Controls.Add(_page);
    }

    /// <summary>
    /// The Ready to Race card's width: the 360px it was at the normal text
    /// size, at the size in use, and never too narrow for the bar and the
    /// longest count beside it ("6 of 6 ready"), or for its button - all
    /// inside the card, where the column beside the step card cannot see
    /// them.
    /// </summary>
    private int ReadyWidth()
    {
        var count = TextRenderer.MeasureText("6 of 6 ready", AppTheme.BoldFont).Width;
        var bar = _barReady.Width + UiLayout.ButtonGap + count;
        var button = _finishShown ? _btnFinish.Width : 0;

        return Math.Max(UiLayout.AtTextSize(ReadyCardWidth), Math.Max(bar, button) + Section.Inset.Horizontal);
    }

    /// <summary>
    /// Tells the host the content's height has changed - after the layout
    /// that changed it, not during. The page raises it from inside its own
    /// layout, and the host answers by moving the splitter, which resizes
    /// this page; WinForms drops a layout asked for while one is running,
    /// so the resize would have left the page laid out at its old size.
    /// Once however many layouts in a row changed it.
    /// </summary>
    private void PostContentHeightChanged()
    {
        if (_contentHeightPosted || IsDisposed)
        {
            return;
        }

        // Nothing is on screen to fit yet. The host reads ContentHeight when
        // it first places the splitter, which is after this has a handle.
        if (!IsHandleCreated)
        {
            return;
        }

        _contentHeightPosted = true;

        BeginInvoke(new Action(() =>
        {
            _contentHeightPosted = false;

            if (!IsDisposed)
            {
                ContentHeightChanged?.Invoke();
            }
        }));
    }

    /// <summary>
    /// The bar: a track, and the done share of it filled - in the accent
    /// while anything is left, in the success colour once it is all done.
    /// The track is the page's colour, a groove in the card it sits on.
    /// </summary>
    private void PaintReadyBar(Graphics g)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(_barReady.Parent?.BackColor ?? AppTheme.Surface);

        var track = new RectangleF(0, 0, _barReady.Width - 1, _barReady.Height - 1);
        var radius = Math.Max(1, _barReady.Height / 2);

        using (var path = AppTheme.RoundedRect(track, radius))
        using (var brush = new SolidBrush(AppTheme.Background))
        {
            g.FillPath(brush, path);
        }

        if (_readyTotal <= 0 || _readyDone <= 0)
        {
            return;
        }

        var share = Math.Clamp(_readyDone / (float)_readyTotal, 0f, 1f);

        // Never narrower than it is tall, or the rounded ends meet and a
        // single step done draws as nothing.
        var fill = new RectangleF(0, 0, Math.Max(_barReady.Height, track.Width * share), track.Height);
        var done = _readyDone >= _readyTotal;

        using (var path = AppTheme.RoundedRect(fill, radius))
        using (var brush = new SolidBrush(done ? AppTheme.Success : AppTheme.Primary))
        {
            g.FillPath(brush, path);
        }
    }

    // ------------------------------------------------------------------
    //  Reading the day
    // ------------------------------------------------------------------

    /// <summary>
    /// Re-reads everything.
    ///
    /// Cheap enough to call on every tab change: the counts are all
    /// in-memory reads the Program tab already makes for its own banner.
    /// </summary>
    public void RefreshAll()
    {
        if (IsDisposed)
        {
            return;
        }

        var day = _events.Active is { IsLegacy: false } open ? open : null;

        // No layout pass: every part is on a piece that measures it again
        // when its text changes.
        RefreshDayHeader(day);
        RefreshStep();
        RefreshTiles();
        RefreshChecks(day);
    }

    private void RefreshDayHeader(RaceEvent? day)
    {
        if (day is null)
        {
            _lblDayName.Text = "No race day is open";
            _lblDayLine.Text = "Nothing is being filed against a day yet.";
            return;
        }

        _lblDayName.Text = string.IsNullOrWhiteSpace(day.Name) ? "Race day" : day.Name.Trim();

        // Only the parts that were filled in. A line of separators with
        // nothing between them says less than no line at all.
        var parts = new List<string>
        {
            day.StartedUtc.ToLocalTime().ToString("dddd d MMMM"),

            // Said out loud, because it decides what the rest of this page
            // offers and there is otherwise no way to tell by looking which
            // kind of night the app thinks you are having.
            day.IsClubNight ? "club night" : "race day",
        };

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

        // Running late, on the screen the director is already watching.
        //
        // This was only ever on the Timetable, which is now a drawer off
        // this page - and "are we behind" is the one thing off that sheet
        // that is worth knowing without opening it. Five minutes either way
        // is not news; a club night is never that precise, and a line that
        // flickers between "2 late" and "1 early" all evening teaches people
        // to stop reading it.
        if (_schedule?.MinutesLate() is { } drift && Math.Abs(drift) >= 5)
        {
            parts.Add(drift > 0
                ? $"running {drift} min late"
                : $"running {-drift} min early");
        }

        _lblDayLine.Text = string.Join("  ·  ", parts);
    }

    /// <summary>
    /// HOLD START FOR A DEMO RUN (11 Nov 2026), as Race Control's Start Next
    /// has (StartNextHold): while the card offers "Start Q1 Heat 1", a press
    /// held for about a second runs that round as a demo instead - simulated
    /// laps, never filed as a result, never scored - through MainForm's one
    /// demo path (RoundDemoRequested). A click still starts it for real.
    /// Only a round to run can be held: on any other step the button is a
    /// plain button, with no clock on it and no bar.
    /// </summary>
    private void BuildDemoHold()
    {
        AppTheme.StyleToolTip(_tips, keep: true);

        var hold = new HoldToAct(_btnPrimary, StartNextHold.Doing, () =>
        {
            if (DemoRound() is { } round)
            {
                _heldClicks.Held();
                RoundDemoRequested?.Invoke(round);
            }
        });

        _btnPrimary.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _heldClicks.Pressed();
            }
        };

        // Once the let-go - and the click it makes - has been handled, the
        // next click is a click again whatever happened to this one.
        _btnPrimary.MouseUp += (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(_heldClicks.Pressed);
            }
        };

        _btnPrimary.Paint += (_, e) =>
        {
            if (DemoRound() is not null)
            {
                hold.PaintOver(e.Graphics, _btnPrimary.ClientRectangle, _btnPrimary.ForeColor, null);
            }
        };

        Disposed += (_, _) =>
        {
            hold.Dispose();
            _tips.Dispose();
        };
    }

    /// <summary>The round a hold would run as a demo: the one the card offers to start, or null on any other step.</summary>
    private RoundDefinition? DemoRound() =>
        _step is { Stage: ProgramStage.RunNext, Round: { } round } ? round : null;

    private void RefreshStep()
    {
        var step = _program.NextStep();
        _step = step;

        _lblKicker.Text = AppTheme.Spaced(step.Stage switch
        {
            ProgramStage.NoEvent or ProgramStage.DayClosed or ProgramStage.StartLikeLast or ProgramStage.NoClassesOnFile
                or ProgramStage.NoClasses or ProgramStage.NoDrivers
                or ProgramStage.NobodyCheckedIn => "BEFORE YOU START",
            ProgramStage.Complete => "ALL DONE",
            _ => "NEXT STEP",
        });

        _lblHeadline.Text = step.Headline;
        _lblDetail.Text = step.Detail;
        _btnPrimary.Text = step.ActionCaption;
        _tips.SetToolTip(_btnPrimary, DemoRound() is null ? null : StartNextHold.Tip);
        _btnPrimary.AccessibleDescription = DemoRound() is null ? null : StartNextHold.AccessibleDescription;
        _btnPrimary.Invalidate();

        // Re-styled every time, because the theme reads a button's colour
        // from its caption and this caption changes with the day. The bar it
        // is in sizes it to the caption.
        AppTheme.StyleButton(_btnPrimary, AppTheme.ButtonKind.Primary);
    }

    /// <summary>
    /// Hands the step back to the programme tab to run.
    ///
    /// The step goes WITH the request rather than being looked up again
    /// there: the two tabs refresh at different moments, and a button that
    /// says "Start Q1 Heat 2" must never run whatever the other tab last
    /// happened to compute.
    /// </summary>
    private void RunStep()
    {
        if (_step is { } step)
        {
            _program.RunStep(step);

            // Re-read straight away. The step that was due has just been
            // done, so the card is now describing the past - and this page
            // shares its tab with the programme grid, which has already
            // redrawn itself. A card and a grid disagreeing about what has
            // happened, eight inches apart, is worse than either being a
            // moment late on its own.
            RefreshAll();
        }
    }

    private void RefreshTiles()
    {
        var classes = _program.TodaysClassNames();
        var rounds = _program.TodaysRounds();

        var drivers = classes.Sum(name => _program.ClassRoster(name).Count);
        var checkedIn = _profiles.AllRaceable.Count(p => _checkIns.IsCheckedIn(p.Transponder));
        var run = rounds.Count(r => r.IsRecorded);

        _tiles[0].Set(classes.Count.ToString("N0"), Plural.Word(classes.Count, "class racing", "classes racing"));
        _tiles[1].Set(drivers.ToString("N0"), Plural.Word(drivers, "driver in", "drivers in"));
        _tiles[2].Set(checkedIn.ToString("N0"), "checked in");
        _tiles[3].Set($"{run} / {rounds.Count}", "rounds run");

        // The last tile is the one that changes character through the day:
        // what is coming, then what is left, then nothing.
        var next = rounds.FirstOrDefault(r => !r.IsRecorded);

        if (next is not null)
        {
            _tiles[4].Set(next.RoundLabel, "next up");
        }
        else if (rounds.Count > 0)
        {
            _tiles[4].Set("Done", "program");
        }
        else if (_schedule?.NextUp is { } line && !string.IsNullOrWhiteSpace(line.Description))
        {
            _tiles[4].Set(line.Description, "on the timetable");
        }
        else
        {
            _tiles[4].Set("-", "next up");
        }
    }

    /// <summary>
    /// One thing that has to be true before a heat can go out: whether it
    /// is, the few words that say what is left when it is not, and the
    /// wizard step that does it - null for the timer, which the wizard has
    /// no step for.
    /// </summary>
    private sealed record Check(bool Done, string Todo, int? WizardStep);

    /// <summary>
    /// How ready the day is, as a bar and a count, what is still to do, and
    /// the one button that goes and does it.
    ///
    /// This was a six-line checklist with a button on every line, under the
    /// tiles - below the fold in the top half of a split page, where it went
    /// unseen. The same questions are asked here; the answer is a bar at the
    /// top of the page, and the button opens the Set Up Tonight wizard at
    /// the first step that is not done yet.
    /// </summary>
    private void RefreshChecks(RaceEvent? day)
    {
        var rounds = _program.TodaysRounds();
        var checks = BuildChecks(day);

        _readyTotal = checks.Count;
        _readyDone = checks.Count(c => c.Done);
        _barReady.Invalidate();

        var left = checks.Where(c => !c.Done).ToList();

        if (left.Count == 0)
        {
            _lblReadyCount.Text = "All set";
            _lblReadyCount.ForeColor = AppTheme.Success;
            _lblTodo.Text = ReadyLine(rounds.Count);
            SetFinish(null, null);
            return;
        }

        _lblReadyCount.Text = $"{_readyDone} of {_readyTotal} ready";
        _lblReadyCount.ForeColor = AppTheme.TextDark;
        _lblTodo.Text = "Still to do: " + string.Join("  ·  ", left.Select(c => c.Todo));

        var firstStep = FirstStepOf(left);

        if (firstStep >= 0)
        {
            SetFinish("Finish Setting Up...", () => SetUpRequested?.Invoke(firstStep));
        }
        else
        {
            // Only the timer left, and the wizard has no step for it.
            SetFinish("Connect Timer...", () => ConnectTimerRequested?.Invoke());
        }
    }

    /// <summary>
    /// The wizard step to open at for whatever is not done yet - the
    /// EARLIEST, in the wizard's own order (classes before drivers before
    /// the day) rather than in the order of the list, so it walks forward
    /// from there. The day step when nothing the wizard covers is left.
    ///
    /// Public for "Start a Race Day", wherever it is pressed: it opens the
    /// wizard too, at the same step this bar's button would.
    /// </summary>
    public int FirstSetupStep()
    {
        var day = _events.Active is { IsLegacy: false } open ? open : null;
        var step = FirstStepOf(BuildChecks(day).Where(c => !c.Done));

        return step >= 0 ? step : RaceDaySetupWizard.StepDay;
    }

    private static int FirstStepOf(IEnumerable<Check> left) =>
        left.Where(c => c.WizardStep is not null).Select(c => c.WizardStep!.Value).DefaultIfEmpty(-1).Min();

    /// <summary>The things that have to be true before a heat can go out, each with the wizard step that does it.</summary>
    private List<Check> BuildChecks(RaceEvent? day)
    {
        var classesOnFile = _raceClasses.All.Count;
        var classes = _program.TodaysClassNames();
        var drivers = classes.Sum(name => _program.ClassRoster(name).Count);
        var rounds = _program.TodaysRounds();

        // Which step fixes an empty roster depends on why it is empty.
        // With no classes racing it is only empty because of that, so the
        // classes step comes first; with check-in required it is the
        // Who's here step; otherwise nobody has been put in the classes.
        var checkedInOnly = day?.CheckedInDriversOnly == true;

        var driversStep = classes.Count == 0
            ? RaceDaySetupWizard.StepRacing
            : checkedInOnly
                ? RaceDaySetupWizard.StepWhosHere
                : RaceDaySetupWizard.StepDrivers;

        var checks = new List<Check>
        {
            // Closed counts as not ready - nothing more can be added to it.
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

        // The timer only when the host can actually answer. A club running
        // the whole evening off Demo Mode has no timer to connect.
        if (_timerConnected is { } connectedNow)
        {
            checks.Add(new Check(connectedNow(), "connect the timer", null));
        }

        return checks;
    }

    private void SetFinish(string? caption, Action? onClick)
    {
        _onFinish = onClick;
        _finishShown = caption is not null && onClick is not null;

        if (_finishShown)
        {
            // Captioned before it is shown, so it is never on screen at the
            // last caption's size. The bar it is in sizes it to the caption.
            _btnFinish.Text = caption!;
            AppTheme.StyleButton(_btnFinish, AppTheme.ButtonKind.Secondary);
        }

        // The bar, not the button: a bar with nothing showing in it would
        // still keep its row, and a row's gap, under what is left to do.
        if (_finishBar is not null)
        {
            _finishBar.Visible = _finishShown;
        }
    }

    /// <summary>
    /// The line the bar says once everything is done. It names what it is
    /// claiming - a bare "All set" leaves somebody wondering set for what.
    /// </summary>
    private static string ReadyLine(int rounds) =>
        rounds == 1
            ? "Everything's ready, and the one round on tonight's program is below."
            : $"Everything's ready. All {rounds} rounds of tonight's program are below.";
}
