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
/// Everything is measured from the control above it, so the page holds at
/// any text size - see the layout pass at the bottom.
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


    /// <summary>
    /// How tall the page's content is, so the host can give this half of
    /// the split enough room to show all of it without scrolling.
    /// </summary>
    public int ContentHeight { get; private set; }

    /// <summary>Raised when <see cref="ContentHeight"/> changes.</summary>
    public event Action? ContentHeightChanged;

    // --- The parts that get rewritten on every refresh ---

    private readonly Label _lblDayName = new();
    private readonly Label _lblDayLine = new();

    private readonly Panel _pnlStep = new();
    private readonly Label _lblKicker = new();
    private readonly Label _lblHeadline = new();
    private readonly Label _lblDetail = new();
    private readonly Button _btnPrimary = new();

    private readonly Label _lblGlance = new();
    private readonly List<Tile> _tiles = new();

    private readonly Label _lblReady = new();

    // --- Ready to race: a bar, a count, what is left, and one button ---
    private readonly Panel _barReady = new();
    private readonly Label _lblReadyCount = new();
    private readonly Label _lblTodo = new();
    private readonly Button _btnFinish = new();

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

    /// <summary>
    /// The narrowest this page's content is laid out at. Below it the page
    /// scrolls sideways rather than squashing five tiles into nothing.
    ///
    /// This was MinimumSize.Width, which also constrained the control itself -
    /// see the note in the constructor for why that had to go.
    /// </summary>
    private const int MinContentWidth = 900;

    /// <summary>How many tiles are built. Fixed, so the layout never has to grow or shrink the page.</summary>
    private const int TileCount = 5;

    private const int ReadyBarWidth = 240;
    private const int ReadyBarHeight = 8;

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
        // pane now - the programme grid is underneath it - and WinForms
        // honours MinimumSize over the size the parent hands out, so a height
        // floor here would have made the control taller than its pane, put its
        // own scrollbar below the visible area, and left the bottom of the
        // checklist unreachable. AutoScroll is what handles a small pane, and
        // it only works if the control is allowed to be small.
        //
        // The layout still needs a width to measure against - see
        // MinContentWidth, which is what MinimumSize.Width used to supply.
        Font = AppTheme.BodyFont;
        AutoScroll = true;

        BuildLayout();
        RefreshAll();
    }

    // ------------------------------------------------------------------
    //  Building
    // ------------------------------------------------------------------

    private void BuildLayout()
    {
        _lblDayName.AutoSize = true;
        _lblDayName.Font = AppTheme.TitleFont;
        _lblDayName.ForeColor = AppTheme.TextDark;

        _lblDayLine.AutoSize = true;
        _lblDayLine.Font = AppTheme.BodyFont;
        _lblDayLine.ForeColor = AppTheme.TextMuted;

        // --- The next-step card ---
        _pnlStep.BackColor = AppTheme.Surface;

        // Kept through the theme pass, which gives every Panel the page
        // background - the card and the tiles were drawn, and invisible.
        _pnlStep.Tag = AppTheme.KeepBackColor;

        _lblKicker.AutoSize = true;
        _lblKicker.Font = AppTheme.MicroFont;
        _lblKicker.ForeColor = AppTheme.Primary;

        _lblHeadline.AutoSize = true;
        _lblHeadline.Font = AppTheme.HeadingFont;
        _lblHeadline.ForeColor = AppTheme.TextDark;

        _lblDetail.AutoSize = true;
        _lblDetail.Font = AppTheme.BodyFont;
        _lblDetail.ForeColor = AppTheme.TextMuted;

        _btnPrimary.Click += (_, _) => RunStep();

        _pnlStep.Controls.Add(_lblKicker);
        _pnlStep.Controls.Add(_lblHeadline);
        _pnlStep.Controls.Add(_lblDetail);
        _pnlStep.Controls.Add(_btnPrimary);

        // --- Section captions ---
        _lblGlance.AutoSize = true;
        _lblGlance.Font = AppTheme.MicroFont;
        _lblGlance.ForeColor = AppTheme.TextMuted;
        _lblGlance.Text = AppTheme.Spaced("THE DAY SO FAR");

        _lblReady.AutoSize = true;
        _lblReady.Font = AppTheme.MicroFont;
        _lblReady.ForeColor = AppTheme.TextMuted;
        _lblReady.Text = AppTheme.Spaced("READY TO RACE");

        // --- Tiles: built once, filled on refresh ---
        //
        // Rebuilding controls on every refresh is what makes a page flicker,
        // and this one refreshes every time the tab is opened and every time
        // a round is recorded.
        for (var i = 0; i < TileCount; i++)
        {
            var tile = new Tile();
            _tiles.Add(tile);
            Controls.Add(tile.Panel);
        }

        // --- Ready to race ---
        _barReady.Size = new Size(ReadyBarWidth, ReadyBarHeight);
        _barReady.Paint += (_, e) => PaintReadyBar(e.Graphics);
        AppTheme.EnableDoubleBuffering(_barReady);

        _lblReadyCount.AutoSize = true;
        _lblReadyCount.Font = AppTheme.BoldFont;
        _lblReadyCount.ForeColor = AppTheme.TextDark;

        _lblTodo.AutoSize = true;
        _lblTodo.Font = AppTheme.BodyFont;
        _lblTodo.ForeColor = AppTheme.TextMuted;

        _btnFinish.Click += (_, _) => _onFinish?.Invoke();

        Controls.Add(_barReady);
        Controls.Add(_lblReadyCount);
        Controls.Add(_lblTodo);
        Controls.Add(_btnFinish);

        Controls.Add(_lblDayName);
        Controls.Add(_lblDayLine);
        Controls.Add(_pnlStep);
        Controls.Add(_lblGlance);
        Controls.Add(_lblReady);

        Resize += (_, _) => LayoutPage();
    }

    /// <summary>One number with a caption under it.</summary>
    private sealed class Tile
    {
        public readonly Panel Panel = new() { BackColor = AppTheme.Surface, Tag = AppTheme.KeepBackColor };

        // Fixed-size, one line each, cut with an ellipsis rather than
        // wrapped. A round label in the last tile used to wrap to a second
        // line, the tile's contents then overran the tile, and the layout
        // pass turned scrolling on INSIDE the tile - the stray bars that
        // showed across "classes racing".
        public readonly Label Value = new() { AutoSize = false, AutoEllipsis = true, Font = AppTheme.TitleFont, ForeColor = AppTheme.TextDark };
        public readonly Label Caption = new() { AutoSize = false, AutoEllipsis = true, Font = AppTheme.MicroFont, ForeColor = AppTheme.TextMuted };

        public Tile()
        {
            Panel.Controls.Add(Value);
            Panel.Controls.Add(Caption);
        }

        public void Set(string value, string caption)
        {
            Value.Text = value;
            Caption.Text = AppTheme.Spaced(caption.ToUpperInvariant());
        }
    }

    /// <summary>
    /// The bar: a track, and the done share of it filled - in the accent
    /// while anything is left, in the success colour once it is all done.
    /// </summary>
    private void PaintReadyBar(Graphics g)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var track = new RectangleF(0, 0, _barReady.Width - 1, _barReady.Height - 1);
        var radius = Math.Max(1, _barReady.Height / 2);

        using (var path = AppTheme.RoundedRect(track, radius))
        using (var brush = new SolidBrush(AppTheme.Surface))
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
    /// Re-reads everything and lays the page out again.
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

        RefreshDayHeader(day);
        RefreshStep();
        RefreshTiles();
        RefreshChecks(day);

        LayoutPage();
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

    private void RefreshStep()
    {
        var step = _program.NextStep();
        _step = step;

        _lblKicker.Text = AppTheme.Spaced(step.Stage switch
        {
            ProgramStage.NoEvent or ProgramStage.DayClosed or ProgramStage.NoClassesOnFile
                or ProgramStage.NoClasses or ProgramStage.NoDrivers
                or ProgramStage.NobodyCheckedIn => "BEFORE YOU START",
            ProgramStage.Complete => "ALL DONE",
            _ => "NEXT STEP",
        });

        _lblHeadline.Text = step.Headline;
        _lblDetail.Text = step.Detail;
        _btnPrimary.Text = step.ActionCaption;

        // Re-styled every time, because the theme reads a button's colour
        // from its caption and this caption changes with the day.
        AppTheme.StyleButton(_btnPrimary, AppTheme.ButtonKind.Primary);
        _btnPrimary.Size = new Size(UiLayout.WidthFor(_btnPrimary), UiLayout.ButtonHeight);
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
        _btnFinish.Visible = _finishShown;

        if (!_finishShown)
        {
            return;
        }

        _btnFinish.Text = caption!;
        AppTheme.StyleButton(_btnFinish, AppTheme.ButtonKind.Secondary);
        _btnFinish.Size = new Size(UiLayout.WidthFor(_btnFinish), UiLayout.ButtonHeight);
    }

    /// <summary>
    /// The line the bar says once everything is done. It names what it is
    /// claiming - a bare "All set" leaves somebody wondering set for what.
    /// </summary>
    private static string ReadyLine(int rounds) =>
        rounds == 1
            ? "Everything's ready, and the one round on tonight's program is below."
            : $"Everything's ready. All {rounds} rounds of tonight's program are below.";

    // ------------------------------------------------------------------
    //  Layout
    // ------------------------------------------------------------------

    /// <summary>
    /// Three bands, laid out to fit the top half of the tab without
    /// scrolling:
    ///
    ///   the day's name;
    ///   the next-step card, with Ready to Race beside it;
    ///   the tiles, one short row.
    ///
    /// It used to be one tall column - card, then tiles, then the
    /// checklist, then the two buttons - and in the top half of a split
    /// page most of that was below the fold. Every size is still measured
    /// from the fonts, so Ctrl +/- grows the bands rather than overlapping
    /// them.
    /// </summary>
    private void LayoutPage()
    {
        if (_tiles.Count == 0)
        {
            return;
        }

        // Positions are client coordinates, which move with the scroll -
        // so everything is offset by it, or a relayout while scrolled
        // would put the page back at the top under a scrolled view.
        var origin = AutoScrollPosition;

        var width = Math.Max(MinContentWidth, ClientSize.Width);
        var content = width - UiLayout.PageLeft - UiLayout.PageRight;
        var left = UiLayout.PageLeft + origin.X;
        var top = UiLayout.PageTop + origin.Y;
        var y = top;

        // --- Band 1: the day ---
        _lblDayName.MaximumSize = new Size(content, 0);
        _lblDayName.Location = new Point(left, y);
        y = UiLayout.Below(_lblDayName, UiLayout.HintGap);

        _lblDayLine.MaximumSize = new Size(content, 0);
        _lblDayLine.Location = new Point(left, y);
        y = UiLayout.Below(_lblDayLine, UiLayout.SectionGap);

        // --- Band 2: the card, and Ready to Race beside it ---
        var countWidth = TextRenderer.MeasureText("6 of 6 ready", AppTheme.BoldFont).Width;
        var readyWidth = Math.Max(360, ReadyBarWidth + UiLayout.RowGap + countWidth + UiLayout.RowGap);
        var cardWidth = content - readyWidth - UiLayout.SectionGap;
        var bandTop = y;

        _pnlStep.Location = new Point(left, bandTop);
        _pnlStep.Width = cardWidth;

        var inner = UiLayout.PageLeft;
        var cardY = UiLayout.RowGap;

        _lblKicker.Location = new Point(inner, cardY);
        cardY = UiLayout.Below(_lblKicker, UiLayout.HintGap);

        _lblHeadline.MaximumSize = new Size(Math.Max(200, cardWidth - inner * 2), 0);
        _lblHeadline.Location = new Point(inner, cardY);
        cardY = UiLayout.Below(_lblHeadline, UiLayout.HintGap);

        _lblDetail.MaximumSize = new Size(Math.Max(200, cardWidth - inner * 2), 0);
        _lblDetail.Location = new Point(inner, cardY);
        cardY = UiLayout.Below(_lblDetail, UiLayout.RowGap);

        _btnPrimary.Location = new Point(inner, cardY);

        _pnlStep.Height = UiLayout.Below(_btnPrimary, UiLayout.RowGap);

        // Ready to Race, level with the top of the card.
        var readyLeft = left + cardWidth + UiLayout.SectionGap;
        var ry = bandTop;

        _lblReady.Location = new Point(readyLeft, ry);
        ry = UiLayout.Below(_lblReady, UiLayout.RowGap);

        var readyRow = Math.Max(ReadyBarHeight, UiLayout.HeightOf(_lblReadyCount));

        _barReady.Location = new Point(readyLeft, ry + (readyRow - ReadyBarHeight) / 2);
        _lblReadyCount.Location = new Point(
            readyLeft + ReadyBarWidth + UiLayout.RowGap,
            UiLayout.CentreOn(_lblReadyCount, ry, readyRow));

        ry += readyRow + UiLayout.RowGap;

        _lblTodo.MaximumSize = new Size(readyWidth, 0);
        _lblTodo.Location = new Point(readyLeft, ry);
        ry = UiLayout.Below(_lblTodo, UiLayout.RowGap);

        if (_finishShown)
        {
            _btnFinish.Location = new Point(readyLeft, ry);
            ry = _btnFinish.Bottom;
        }

        y = Math.Max(_pnlStep.Bottom, ry) + UiLayout.SectionGap;

        // --- Band 3: the tiles, one short row ---
        _lblGlance.Location = new Point(left, y);
        y = UiLayout.Below(_lblGlance, UiLayout.RowGap);

        const int gap = UiLayout.RowGap;
        const int pad = UiLayout.RowGap;

        var tileWidth = Math.Max(120, (content - gap * (TileCount - 1)) / TileCount);
        var valueHeight = AppTheme.TitleFont.Height + 2;
        var captionHeight = AppTheme.MicroFont.Height + 2;
        var tileHeight = pad + valueHeight + UiLayout.HintGap + captionHeight + pad;

        for (var i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];

            tile.Panel.Location = new Point(left + i * (tileWidth + gap), y);
            tile.Panel.Size = new Size(tileWidth, tileHeight);

            tile.Value.Bounds = new Rectangle(pad, pad, tileWidth - pad * 2, valueHeight);
            tile.Caption.Bounds = new Rectangle(pad, pad + valueHeight + UiLayout.HintGap, tileWidth - pad * 2, captionHeight);
        }

        y += tileHeight;

        // Room under the last line, so the page does not stop hard against
        // the window's edge.
        //
        // The width as well as the height: without it a pane narrower than
        // the content clips the right-hand tiles instead of offering a
        // scrollbar. The CONSTANT, not the measured width - using the measured
        // one would set the scroll range to exactly the client width, and then
        // the vertical scrollbar appearing would be enough to bring the
        // horizontal one in after it, which brings the vertical one back.
        //
        // Assigned only when it changed, because a scrollbar arriving or
        // leaving re-fires Resize and lands back here.
        var needed = y - origin.Y + UiLayout.PageTop;
        var scrollTo = new Size(MinContentWidth, needed);

        if (AutoScrollMinSize != scrollTo)
        {
            AutoScrollMinSize = scrollTo;
        }

        if (ContentHeight != needed)
        {
            ContentHeight = needed;
            ContentHeightChanged?.Invoke();
        }
    }
}
