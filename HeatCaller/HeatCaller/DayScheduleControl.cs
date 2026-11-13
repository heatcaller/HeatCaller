namespace HeatCaller;

/// <summary>
/// The run sheet: the day's running order, live.
///
/// It is a VIEW of the programme, not a second copy of it. It used to be a
/// second copy: a hand-typed timeline with its own sequence, its own Move Up
/// and its own Move Down, working against an ordering that had nothing to do
/// with the programme's. Moving a heat on one page did not move its line on the
/// other, and the next rebuild silently discarded whichever of the two
/// orderings was not the programme's.
///
/// So the programme owns the order. Every round has a line, in programme order,
/// and a round moves by being moved on the programme - Move Up on a generated
/// line here does exactly that, and the sheet follows because it is reading the
/// same list. What this page adds on top is the two things the programme does
/// not have: a clock time against each round, and the club's own lines - doors
/// open, lunch, presentation - interleaved between them and anchored so they
/// stay where they were put.
///
/// The lines look after themselves: starting a round stamps its line with the
/// real start time, recording the round ticks it off, and a class rename or a
/// rebuilt final updates its wording. None of that survives a line being
/// deleted and recreated, which is why DayScheduleStore.SyncToProgram
/// reconciles rather than replaces.
///
/// The page reads as a status board: what is done, what is on track
/// right now (and how far off its slot it started), what is up next.
/// </summary>
public sealed class DayScheduleControl : UserControl
{
    private readonly DayScheduleStore _schedule;
    private readonly RaceClassStore _raceClasses;
    private readonly AnnouncementService _announcer;
    private readonly UndoService _undo;
    private readonly RoundDefinitionStore _rounds;
    private readonly RaceEventStore _events;

    private readonly DataGridView _grid = new();
    private readonly Label _lblNextUp = new();
    private readonly Label _lblCountdown = new();
    private readonly NumericUpDown _numMinutes = new();
    private readonly Button _btnStartCountdown = new();
    private readonly Button _btnStopCountdown = new();

    /// <summary>
    /// The Between races card's "Start the countdown by itself from the run sheet"
    /// (31 Oct 2026), here too, beside the countdown it does for you.
    /// Hidden until the host hands over the settings.
    /// </summary>
    private readonly CheckBox _chkAutoCountdown = new ThemedCheckBox
    {
        Text = "Count down by itself from the run sheet",
        AutoSize = true,
        Visible = false,
    };

    private AppSettings? _settings;

    /// <summary>
    /// The settings the automatic countdown tick reads and writes - the same
    /// one as Between races on the AI Commentary tab.
    /// </summary>
    internal AppSettings? Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            _chkAutoCountdown.Visible = value is not null;
            _chkAutoCountdown.Checked = value?.BetweenRacesAutoCountdown ?? false;
        }
    }

    /// <summary>Opens the one list of what can be done to this sheet. See BuildLineMenu.</summary>
    private readonly Button _btnMore = new();

    /// <summary>Shared by the More button and the grid's right-click, so the two can never offer different things.</summary>
    private ContextMenuStrip? _lineMenu;

    private readonly System.Windows.Forms.Timer _countdownTimer = new() { Interval = 1000 };
    /// <summary>
    /// When the countdown runs out, by the clock. Null when none is running.
    ///
    /// A WALL-CLOCK deadline rather than a tally of ticks. It used to be an
    /// int decremented once per Tick on a 1000ms WinForms timer, and that
    /// timer runs on the message pump: it drops ticks whenever the UI thread
    /// is busy, a modal dialog is open, or the machine sleeps - all of which
    /// happen during a race night, which is the only time this is used. A
    /// ten-minute countdown quietly became eleven, and the "5 minutes to..."
    /// call went out when there were three left.
    /// </summary>
    private DateTime? _countdownEndsUtc;
    private string _countdownLabel = string.Empty;
    private bool _announced5Min;
    private bool _announced1Min;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The page the rest is laid out on - see LeastHeight.</summary>
    private readonly FillPage _page = new();

    /// <summary>The countdown's card, and the two rows in it the card's width is measured from - see ReminderWidth.</summary>
    private Section? _reminderCard;
    private FlowLayoutPanel? _minutesRow;
    private ButtonBar? _countdownButtons;

    /// <summary>The countdown's figure, a face this page makes itself at the text size in use - see FitCountdown.</summary>
    private Font? _countdownFont;

    /// <summary>The countdown's figure at the normal text size, in points - the size it always was.</summary>
    private const float CountdownPoints = 16F;

    /// <summary>The longest the countdown's figure comes to: two hours, the most it can be set to.</summary>
    private const string CountdownWidest = "120:00";

    /// <summary>The countdown's column at the normal text size - the narrowest its group box ever was.</summary>
    private const int ReminderLeastWidth = 240;

    /// <summary>
    /// The fewest lines the sheet shows under its headings before the page
    /// scrolls instead - about the 160px the old page never let the grid go
    /// under, at the normal text size.
    /// </summary>
    private const int GridLeastRows = 4;

    /// <summary>
    /// The line to put the cursor back on after the next redraw, or null.
    ///
    /// Set by a move, because a move now redraws the whole sheet - a round
    /// moving means every row below it has moved too. Selecting the row is not
    /// enough on its own: SelectedEntry reads CurrentRow, so without the cursor
    /// following, a second press of Move Up would act on whatever line happened
    /// to be sitting where the cursor was left.
    /// </summary>
    private string? _reselect;

    public DayScheduleControl(
        DayScheduleStore schedule,
        RaceClassStore raceClasses,
        AnnouncementService announcer,
        UndoService undo,
        RoundDefinitionStore rounds,
        RaceEventStore events)
    {
        _schedule = schedule;
        _raceClasses = raceClasses;
        _announcer = announcer;
        _undo = undo;
        _rounds = rounds;
        _events = events;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No MinimumSize. It was 900x560, which WinForms honours over the
        // size the page is given - so a window shorter than that had the
        // buttons under the grid off the bottom of the page. A short window
        // scrolls the page instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();

        _countdownTimer.Tick += CountdownTimer_Tick;
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - one way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// Put into a window, or taken back out of one. This is the one page
    /// MainForm keeps for the timetable, lent to a new window each time the
    /// drawer opens (PageWindow, MainForm's ShowTimetable) and out of every
    /// window in between - where a new text size never reaches it. Nor is
    /// it always told on the way in: a window whose font is already the new
    /// size when the page is put in it changes no font the page sees, since
    /// the page wears a face of its own. So it catches up here, each time it
    /// moves: its faces, its field and figure sizes and its grid's headings
    /// at the text size in use now - and, as it lays out in the new window,
    /// everything measured from them.
    ///
    /// Not while it is being disposed, which takes it out of its window too.
    /// </summary>
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);

        if (!IsDisposed && !Disposing)
        {
            _textSize.Apply();
        }
    }

    /// <summary>
    /// The intro and the Up Next line across the top, then the sheet: the
    /// grid in a card, taking the height that is left, with Add Entry and
    /// More under it - and beside it, down the right-hand side, the reminder
    /// countdown in a card of its own, as wide as its widest row. On the
    /// layout pieces (LayoutPieces.cs), so nothing is placed by hand, and a
    /// window too short for all of it scrolls the page rather than cutting
    /// off the buttons under the grid.
    ///
    /// It used to be placed in pixels: the intro capped at 860px, the grid
    /// and the countdown's group box placed on every resize over a 900x560
    /// floor the page kept however small its window, the two buttons pinned
    /// to its bottom edge - and the countdown's 16pt figure made once, at
    /// the normal text size, whatever the text size was.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "The day's running order, straight from the program. Each heat and final ticks itself off as it is "
                 + "recorded and shows when it started; reorder it here or on Next Steps and both follow. Add your own "
                 + "lines (doors open, lunch, presentation) anywhere; they stay where you put them.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // The line the countdown's hint points at ("Up Next" above). It
        // wraps to the page when a class's name runs long.
        _lblNextUp.AutoSize = true;
        _textSize.Wear(_lblNextUp, () => AppTheme.BoldFont);

        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        // Ctrl-click for individual rows, Shift-click for a range.
        _grid.MultiSelect = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            // Read-only to the grid itself: Grid_CellClick is the one thing
            // that ticks a line. Left editable, a click on the box ticked it
            // AND ran the handler, which flipped it straight back.
            new DataGridViewCheckBoxColumn { Name = "done", HeaderText = "Done", FillWeight = 14, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "time", HeaderText = "Time", FillWeight = 20, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "class", HeaderText = "Class", FillWeight = 34, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "description", HeaderText = "What", FillWeight = 44, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", FillWeight = 42, ReadOnly = true }
        );

        // An empty timetable says where its lines come from.
        EmptyNote.For(_grid, () => "Nothing on the timetable yet. The program's heats and finals appear here as they are made, and Add Entry puts in a line of your own.");

        // Every heading whole, at the text size in use, and the status kept
        // wide enough for a heat on track - see Pieces.KeepHeadingsWhole. It
        // read "on track · started ..." and "done · started 20:..." at the
        // normal text size, with the class and what beside it (layout
        // pictures, 30 Sep). Whether it ran late or early can still be cut,
        // in a narrow window; when it started never is.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid, ("time", "88:88"), ("status", "on track · started 88:88")));

        // Left button only. CellClick fires for either, and the grid now has
        // a right-click menu - so right-clicking a line to reach it, with
        // the pointer over the Done box, would have ticked the line off on
        // the way to opening the menu.
        _grid.CellMouseClick += (_, e) =>
        {
            // Indexes are -1 on a header click, and the args type rejects
            // anything below -1 - so they are checked before it is built,
            // not inside the handler.
            if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                Grid_CellClick(this, new DataGridViewCellEventArgs(e.ColumnIndex, e.RowIndex));
            }
        };

        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        // --- The action row, under the grid ---
        //
        // Two buttons, where there were eight.
        //
        // Eight was already the most on any page in the app, and it stopped
        // being defensible when this became a drawer: a window you open for
        // a couple of minutes to set some times should not open onto a wall
        // of commands. Everything is still here - it is in the menu below,
        // which the More button and the grid's right-click share, so there
        // is ONE list of what can be done to this sheet rather than a
        // button strip and a menu drifting apart from each other. That is
        // the pattern the programme grid already uses.
        //
        // Add Entry stays a button because it is the one thing you come to
        // this page to do that has nothing to right-click on first.
        var btnAdd = new Button { Text = "Add Entry..." };
        btnAdd.Click += (_, _) => AddNew();

        _btnMore.Text = "More...";
        _btnMore.Click += (_, _) => _lineMenu?.Show(_btnMore, new Point(0, _btnMore.Height));

        BuildLineMenu();

        // No caption: the window's title already says "Timetable", and the
        // Up Next line over it says where the sheet has got to.
        var sheet = new Section();

        sheet.AddFill(_grid, LeastGridHeight);
        sheet.Add(new ButtonBar().With(btnAdd, _btnMore), gapBelow: 0);

        // --- The reminder countdown ---
        var lblFor = _textSize.Wear(
            new Label
            {
                Text = "Announces to \"Up Next\" above at 5 minutes, 1 minute, and when time's up.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            },
            () => AppTheme.SmallFont);

        var lblMinutes = new Label { Text = "Minutes until start:", AutoSize = true };

        _numMinutes.Minimum = 1;
        _numMinutes.Maximum = 120;
        _numMinutes.Value = 10;

        _btnStartCountdown.Text = "Start";
        _btnStopCountdown.Text = "Stop";
        _btnStopCountdown.Enabled = false;
        _btnStartCountdown.Click += (_, _) => StartCountdown();
        _btnStopCountdown.Click += (_, _) => StopCountdown(announceNow: false);

        // Sized by the page, not by itself - see FitCountdown.
        _lblCountdown.Text = string.Empty;
        _lblCountdown.AutoSize = false;
        _lblCountdown.ForeColor = AppTheme.Accent;
        _textSize.Keep(FitCountdown);

        // The minutes beside their caption, the box going under it only if
        // the column were ever too narrow for both - it is made wide enough
        // (ReminderWidth).
        var minutesRow = FieldGrid.Inline(lblMinutes, _numMinutes);
        var countdownButtons = new ButtonBar().With(_btnStartCountdown, _btnStopCountdown);

        var reminder = new Section("Reminder countdown");

        // With it ticked, Race Control says the marks itself between races,
        // from the next race's time on this sheet - see Between races.
        // Written and heard through AutoCountdownTick, so the Between races
        // card's own tick follows this one and this one follows it (10 Nov
        // 2026, bug hunt 16).
        _chkAutoCountdown.CheckedChanged += (_, _) =>
        {
            if (_settings is not null)
            {
                AutoCountdownTick.Set(_settings, _chkAutoCountdown.Checked);
            }
        };
        AutoCountdownTick.Changed += AutoCountdownTicked;

        reminder.Add(lblFor);
        reminder.Add(minutesRow);
        reminder.Add(countdownButtons);
        reminder.Add(_chkAutoCountdown);
        reminder.Add(_lblCountdown, gapBelow: 0);

        _reminderCard = reminder;
        _minutesRow = minutesRow;
        _countdownButtons = countdownButtons;

        // --- The page ---
        //
        // The countdown beside the sheet, as tall as it needs to be at the
        // top of its column; the sheet takes the rest of the width and all
        // of the height.
        var band = new Beside(ReminderWidth);

        band.AddFill(sheet, sheet.LeastHeight);
        band.AddSideCard(reminder, gapBelow: 0);

        _page.Add(lblIntro);
        _page.Add(_lblNextUp, gapBelow: UiLayout.RowGap);
        _page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(_page);

        // Once the fields are on the page and wear its face - what they are
        // measured in.
        _textSize.Keep(SizeFields);
    }

    /// <summary>
    /// The least the page can do with at <paramref name="width"/> - its
    /// intro, the Up Next line, and the taller of the countdown's card and
    /// the sheet's (the grid's headings and four lines, and its buttons) -
    /// for the window it is shown in, which gives the page the height it
    /// has (PageWindow.Timetable).
    /// </summary>
    internal int LeastHeight(int width) => _page.LeastHeight(width);

    /// <summary>The least the grid can do with: its headings and a few lines. Below that the page scrolls instead - see FillPage.</summary>
    private int LeastGridHeight() =>
        _grid.ColumnHeadersHeight + GridLeastRows * _grid.RowTemplate.Height + 2;

    /// <summary>
    /// The countdown's column: the width its group box was at the normal
    /// text size, at the size in use - and never narrower than its caption,
    /// the minutes beside their caption, or Start and Stop on one line need.
    /// </summary>
    private int ReminderWidth() =>
        Math.Max(
            UiLayout.AtTextSize(ReminderLeastWidth),
            Math.Max(
                _reminderCard?.CaptionWidth ?? 0,
                Math.Max(
                    Math.Max(_minutesRow?.GetPreferredSize(Size.Empty).Width ?? 0, _countdownButtons?.OneLineWidth ?? 0),
                    _settings is null ? 0 : _chkAutoCountdown.GetPreferredSize(Size.Empty).Width))
            + Section.Inset.Horizontal);

    /// <summary>
    /// The minutes box: wide enough for the most minutes it takes and its
    /// arrows, and never narrower than a number box at the normal text size,
    /// at this size. Run again when the text size changes (TextSize.Keep).
    /// </summary>
    private void SizeFields()
    {
        var digits = ((int)_numMinutes.Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

        var width = Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(new string('0', digits), _numMinutes.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

        // Only when it changed: a new width lays the row out again.
        if (_numMinutes.Width != width)
        {
            _numMinutes.Width = width;
        }
    }

    /// <summary>
    /// The countdown's figure: the 16pt bold it always was, at the text
    /// size in use - a face this page makes itself, so it is made again when
    /// the text size changes and the old one let go - and one line of it
    /// kept whatever it says, so nothing moves as the countdown starts,
    /// ticks and stops.
    ///
    /// Kept by the page rather than sized by the label. A label that sizes
    /// itself asks what it is in to lay out again every time it is
    /// rewritten, which here is every second the countdown runs; one given
    /// its height is only repainted.
    /// </summary>
    private void FitCountdown()
    {
        var points = CountdownPoints * AppTheme.FontScale;

        if (_countdownFont is null || Math.Abs(_countdownFont.SizeInPoints - points) >= 0.01F)
        {
            var old = _countdownFont;

            _countdownFont = new Font(AppTheme.BoldFont.FontFamily, points, FontStyle.Bold);
            _lblCountdown.Font = _countdownFont;
            old?.Dispose();
        }

        // A line of the figure as the label draws it (Pieces.LineHeight), or
        // as the figure measures, whichever is taller.
        var line = Math.Max(
            Pieces.LineHeight(_lblCountdown.Font),
            TextRenderer.MeasureText(CountdownWidest, _lblCountdown.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height);

        var height = line + _lblCountdown.Padding.Vertical;

        if (_lblCountdown.Height != height)
        {
            _lblCountdown.Height = height;
        }
    }

    /// <summary>Call whenever this page becomes visible - the program and check-ins change elsewhere while it's not active.</summary>
    public void RefreshAll()
    {
        // Generated lines tick themselves off once their round is recorded
        // - the whole point of a line knowing which heat it is.
        _schedule.AutoCompleteRecorded(id => _rounds.Get(id)?.IsRecorded == true);

        // A move asks for a particular line back; otherwise whatever the
        // cursor was on.
        var keep = _reselect ?? (_grid.CurrentRow?.Tag as string);
        _reselect = null;

        var entries = _schedule.All;
        var firstPendingId = entries.FirstOrDefault(e => !e.IsComplete)?.Id;

        // The day's lateness and the next race's push-back, read once for
        // the sheet (12 Nov 2026) - see ScheduleExpected.
        var dayLate = _schedule.MinutesLate();
        var next = NextStart?.Invoke();
        var now = DateTime.UtcNow;

        var keepRow = -1;

        _grid.Rows.Clear();
        foreach (var entry in entries)
        {
            var round = entry.IsGenerated ? _rounds.Get(entry.RoundId) : null;
            var status = StatusFor(entry, round, entry.Id == firstPendingId, ScheduleExpected.Words(entries, entry, dayLate, next, now));

            var rowIndex = _grid.Rows.Add(entry.IsComplete, entry.ScheduledTime, entry.ClassName, entry.Description, status.Text);
            var row = _grid.Rows[rowIndex];
            row.Tag = entry.Id;

            if (status.Ink is { } ink)
            {
                row.DefaultCellStyle.ForeColor = ink;
            }

            if (string.Equals(entry.Id, keep, StringComparison.Ordinal))
            {
                keepRow = rowIndex;
            }
        }

        // After the fill, not during it: setting CurrentCell moves the
        // selection with it, and doing that mid-fill fights every row still to
        // be added.
        if (keepRow >= 0)
        {
            _grid.ClearSelection();
            _grid.Rows[keepRow].Selected = true;
            _grid.CurrentCell = _grid.Rows[keepRow].Cells[0];
        }

        // Says which of the two it is - nothing on the timetable, or all of it
        // done - rather than "(schedule is empty or complete)".
        var nextUp = _schedule.NextUp;
        _lblNextUp.Text = nextUp is not null ? $"Up Next: {DescribeEntry(nextUp)}"
            : _schedule.All.Count == 0 ? "Up Next: nothing - the timetable is empty."
            : "Up Next: nothing - every line is done.";
    }

    /// <summary>
    /// One line's status and the ink it earns: done lines fade, the line
    /// on track right now is the loudest thing on the sheet, and the next
    /// one up is picked out in the accent.
    /// </summary>
    /// <param name="expected">For a line not started yet, when it is expected and how late (ScheduleExpected.Words), or empty.</param>
    private (string Text, Color? Ink) StatusFor(ScheduleEntry entry, RoundDefinition? round, bool isNextUp, string expected)
    {
        if (entry.IsGenerated && round is null)
        {
            return ("(round deleted from program)", AppTheme.TextMuted);
        }

        if (entry.IsComplete)
        {
            var finished = "done";

            if (entry.ActualStartUtc is { } startedUtc)
            {
                finished += $" · started {AppTime.TimeOfDay(startedUtc.ToLocalTime())}";
            }

            return (finished, AppTheme.TextMuted);
        }

        if (entry.ActualStartUtc is { } started)
        {
            return ($"on track · started {AppTime.TimeOfDay(started.ToLocalTime())}{Drift(entry)}", AppTheme.Warning);
        }

        if (isNextUp)
        {
            return (expected.Length > 0 ? $"up next · {expected}" : "up next", AppTheme.Primary);
        }

        // 12 Nov 2026: a line still to come says when it is expected once
        // the night is off its times - "expected 20:47 · 7 min late".
        return (expected, null);
    }

    /// <summary>
    /// Race Control's next race and how far it has been put back
    /// (RaceControlControl.NextStartForPlan), for the expected times of the
    /// lines still to come. Null when there is no Race Control.
    /// </summary>
    internal Func<RaceControlControl.NextStartFact?>? NextStart { get; set; }

    /// <summary>The next race moved (a push-back, a Hold, an approved "Need 5 minutes"): redraws the sheet when it is in a window.</summary>
    internal void NextStartMoved()
    {
        if (!IsDisposed && Parent is not null && IsHandleCreated)
        {
            RefreshAll();
        }
    }

    /// <summary>
    /// How far off its slot a line started, when the slot is a parseable
    /// time. Freeform times ("~10:15", "TBD") produce no figure, which is
    /// the right amount of certainty about them.
    /// </summary>
    private static string Drift(ScheduleEntry entry)
    {
        if (entry.ActualStartUtc is not { } startedUtc)
        {
            return string.Empty;
        }

        // The midnight-safe reading, shared with the Next Up bar - see
        // ScheduleDrift.MinutesOff.
        if (ScheduleDrift.MinutesOff(entry.ScheduledTime, startedUtc.ToLocalTime()) is not { } minutes)
        {
            return string.Empty;
        }

        if (minutes >= 3)
        {
            return $" · {minutes} min late";
        }

        if (minutes <= -3)
        {
            return $" · {-minutes} min early";
        }

        return " · on time";
    }

    private static string DescribeEntry(ScheduleEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.ScheduledTime))
        {
            parts.Add(entry.ScheduledTime);
        }
        if (!string.IsNullOrWhiteSpace(entry.ClassName))
        {
            parts.Add(entry.ClassName);
        }
        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            parts.Add(entry.Description);
        }
        return parts.Count > 0 ? string.Join(" - ", parts) : "(untitled entry)";
    }

    /// <summary>
    /// The current event's programme, in programme order.
    ///
    /// The same reading the store sorts this sheet by - see EventSpine.Programme.
    /// It was a private copy of that logic here, which is one reading too many
    /// for a page whose whole job is now to show the programme's order.
    /// </summary>
    private List<RoundDefinition> Program() => EventSpine.Programme(_events, _rounds);

    /// <summary>
    /// The programme as the timetable needs it: a round's id and the words on
    /// its line. A shared heat's line names both classes, "Stock + Mod"
    /// (11 Nov 2026, Phase 4.1) - the timetable is what the Big Screen's next
    /// up and the announcer's "on deck" read.
    /// </summary>
    private static List<DayScheduleStore.ProgramLine> LinesFor(IEnumerable<RoundDefinition> program) =>
        program.Select(r => new DayScheduleStore.ProgramLine(r.Id, MixedHeats.ClassLabel(r), r.RoundLabel)).ToList();

    /// <summary>Raised when the timetable needs the programme that fills it. The host owns getting there - and, now this is a drawer, shutting it on the way.</summary>
    public event Action? GoToProgramTab;

    // Following the programme is NOT done here any more.
    //
    // It used to be SyncGeneratedLines on this control, hung off
    // ProgrammeChanged. That was fine while this was a tab, and stopped
    // being fine when it became a drawer: shut for most of an evening, the
    // sheet would only have caught up at the moment somebody opened it, and
    // the printed run sheet, the countdown and the running-late line on
    // Today would all have been read off a stale order until then.
    //
    // It is a data operation, so it lives with the data - see
    // MainForm.SyncTimetableToProgramme, which also carries the rules that
    // keep it from being presumptuous. "Fill from the Programme" below stays
    // as the manual way back after Clear Schedule.

    /// <summary>
    /// Everything that can be done to this sheet, in one list.
    ///
    /// Shared by the More button and the grid's right-click. The split in
    /// it is the one the two button rows used to carry, and it is the same
    /// rule that decides what greys out: the top group needs a line picked,
    /// the bottom group acts on the whole sheet and never does.
    /// </summary>
    private void BuildLineMenu()
    {
        var menu = new ContextMenuStrip();

        var miEdit = new ToolStripMenuItem("Edit...", null, (_, _) => EditSelected());
        var miDelete = new ToolStripMenuItem("Delete", null, (_, _) => DeleteSelected());
        var miUp = new ToolStripMenuItem("Move Up", null, (_, _) => MoveSelected(-1));
        var miDown = new ToolStripMenuItem("Move Down", null, (_, _) => MoveSelected(1));

        var miTimes = new ToolStripMenuItem("Fill In the Times...", null, (_, _) => FillTimes());

        // The sheet fills and maintains itself from the programme now - see
        // MainForm.SyncTimetableToProgramme - so this is the way back after
        // Clear Schedule rather than the way in.
        var miBuild = new ToolStripMenuItem("Fill from the Program", null, (_, _) => BuildFromProgram());
        var miPrint = new ToolStripMenuItem("Print Run Sheet...", null, (_, _) => PrintRunSheet());
        var miClear = new ToolStripMenuItem("Clear Schedule", null, (_, _) => ClearAll());

        menu.Items.AddRange(new ToolStripItem[]
        {
            miEdit,
            miUp,
            miDown,
            new ToolStripSeparator(),
            miTimes,
            miBuild,
            miPrint,
            new ToolStripSeparator(),
            miDelete,
            miClear,
        });

        menu.Opening += (_, args) =>
        {
            var selected = _grid.SelectedRows.Count;
            var single = selected == 1;

            miEdit.Enabled = single;
            miUp.Enabled = single;
            miDown.Enabled = single;

            miDelete.Enabled = selected > 0;
            miDelete.Text = selected > 1 ? $"Delete {selected} Lines" : "Delete";

            var lines = _schedule.All.Count;

            miTimes.Enabled = lines > 0;
            miPrint.Enabled = lines > 0;
            miClear.Enabled = lines > 0;

            _ = args;
        };

        AppTheme.StyleContextMenu(menu);

        _lineMenu = menu;
        _grid.ContextMenuStrip = menu;

        // Right-clicking a row selects it first, the way Explorer does -
        // otherwise the menu acts on whatever was selected before.
        _grid.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _grid.HitTest(e.X, e.Y);

            if (hit.RowIndex < 0)
            {
                return;
            }

            // Left alone when the clicked row is already part of what is
            // selected: right-clicking inside a selection is how you reach
            // the menu for it, not how you throw it away.
            if (_grid.Rows[hit.RowIndex].Selected)
            {
                return;
            }

            _grid.ClearSelection();
            _grid.Rows[hit.RowIndex].Selected = true;

            // INSIDE the guard. Assigning CurrentCell collapses the selection
            // to that one row, so outside it this line would undo the check
            // above on every right-click.
            _grid.CurrentCell = _grid.Rows[hit.RowIndex].Cells[Math.Max(0, hit.ColumnIndex)];
        };
    }

    private void BuildFromProgram()
    {
        // The same rule MainForm.SyncTimetableToProgramme keeps. With no race
        // day open the programme is the whole store, and this WRITES: every
        // round the club has ever run would go onto the sheet as today's
        // running order - the Run Sheet, Next Up and the published timetable
        // with it (25 Sep audit).
        if (_events.Active is not { IsLegacy: false })
        {
            AppDialog.Show(this,
                "No race day is open, so there is no program to put on the timetable - with no day open it would be "
                + "every round the club has ever run.\r\n\r\nOpen the race day, or start tonight's, and the timetable "
                + "fills itself from its program.",
                "No race day", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var program = Program();

        if (program.Count == 0)
        {
            // The old wording sent people to "Race Day > Program", then to
            // "Heats & Mains". Neither is a tab any more - the programme is
            // the lower half of Today - so this names the tab that exists and
            // offers to open it rather than handing over a single OK.
            if (AppDialog.Show(this,
                    "This race day has no heats or finals yet, so there is nothing to put on the timetable.\r\n\r\n"
                    + "Open Next Steps and build them?",
                    "Nothing to build from", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                GoToProgramTab?.Invoke();
            }

            return;
        }

        var generated = _schedule.Generated.Count;

        var confirm = AppDialog.Show(this,
            $"Put this event's {Plural.Of(program.Count, "round")} on the run sheet, in program order?"
            + (generated > 0
                ? $" {Plural.Of(generated, "round")} already here {(generated == 1 ? "keeps" : "keep")} its time and its tick; "
                : " ")
            + "lines you typed yourself stay where they are.",
            "Fill from the program", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        var snapshot = _schedule.Snapshot();
        _undo.Capture("Fill the run sheet from the program", () =>
        {
            _schedule.Restore(snapshot);
            RefreshAll();
        });

        _schedule.SyncToProgram(LinesFor(program));
        RefreshAll();
    }

    /// <summary>
    /// A plain black-on-white picture of the sheet, saved as a PNG the way
    /// the heat sheet is - made to come out of a printer and live on a
    /// clipboard at the drivers' stand.
    /// </summary>
    private void PrintRunSheet()
    {
        var entries = _schedule.All;

        if (entries.Count == 0)
        {
            AppDialog.Show(this, "The run sheet is empty - build it from the program or add entries first.",
                "Nothing to print", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save run sheet",
            Filter = "PNG image (*.png)|*.png",
            FileName = $"run-sheet-{DateTime.Now:yyyy-MM-dd}.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var title = _events.Current is { IsLegacy: false } current ? current.DisplayName : "Race Day";

            // Every line, over as many pages as it takes - see PageFiles.
            var pages = RunSheetRenderer.RenderPages(title, entries);

            if (PageFiles.SavePages(this, pages, dialog.FileName) is not { } written)
            {
                return;
            }

            AppDialog.Show(this, $"{PageFiles.Describe(written)}\n\nOpen and print {(written.Count == 1 ? "it" : "each")} at full page size.",
                "Run sheet saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't create the run sheet: {ex.Message}", "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0)
        {
            return;
        }

        var cell = _grid.Rows[e.RowIndex].Cells[0];
        var newValue = !(bool)(cell.Value ?? false);
        cell.Value = newValue;
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        if (_grid.Rows[e.RowIndex].Tag is string id)
        {
            _schedule.SetComplete(id, newValue);
            RefreshAll();
        }
    }

    private void AddNew()
    {
        using var form = new ScheduleEntryForm(null, _raceClasses);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Anchored as it is added. A line left un-anchored would sit at the end
        // of the sheet and quietly stay there as the programme grew around it,
        // which is right for "presentation" and wrong for everything else.
        form.Result.AfterRounds = AnchorForNewLine();

        _schedule.Add(form.Result);
        _reselect = form.Result.Id;
        RefreshAll();
    }

    /// <summary>
    /// Where a new line of the club's own goes: straight after whatever is
    /// selected, or at the end of the sheet when nothing is.
    ///
    /// "After the selected line" is the useful default because that is how a
    /// run sheet gets typed - you find the heat lunch comes after and add lunch.
    /// Adding at the end and making somebody press Move Up nine times is the
    /// same answer with the work left to them.
    /// </summary>
    private int AnchorForNewLine()
    {
        var program = Program();

        if (SelectedEntry() is not { } selected)
        {
            return program.Count;
        }

        if (!selected.IsGenerated)
        {
            // Into the same slot. It is added to the end of the store, and the
            // sort inside a slot is the stored order, so it lands just below
            // the line it was added from.
            return selected.AfterRounds < 0
                ? program.Count
                : Math.Clamp(selected.AfterRounds, 0, program.Count);
        }

        var at = program.FindIndex(r => string.Equals(r.Id, selected.RoundId, StringComparison.Ordinal));

        return at < 0 ? program.Count : at + 1;
    }

    private ScheduleEntry? SelectedEntry()
    {
        if (_grid.CurrentRow?.Tag is not string id)
        {
            return null;
        }
        return _schedule.All.FirstOrDefault(e => e.Id == id);
    }

    private void EditSelected()
    {
        var entry = SelectedEntry();
        if (entry is null)
        {
            return;
        }

        // Read BEFORE the dialog opens: its OK writes straight into the
        // line it was given, so afterwards the old values are gone.
        var schedule = _schedule;
        var entryId = entry.Id;
        var (oldTime, oldClass, oldDescription) = (entry.ScheduledTime, entry.ClassName, entry.Description);

        var isRound = entry.IsGenerated;

        using var form = new ScheduleEntryForm(entry, _raceClasses);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            // A round's line takes its class and its wording from the
            // programme, and SyncToProgram writes both back on the next
            // rebuild. Kept, the edit survived until the next time anything
            // touched the programme and then vanished with no sign it had
            // ever been made - the quietly-undone edit Delete and Move on
            // this page already refuse. The TIME is this sheet's own, and
            // the rebuild preserves it, so that part of the edit stands.
            if (isRound && (form.Result.ClassName != oldClass || form.Result.Description != oldDescription))
            {
                form.Result.ClassName = oldClass;
                form.Result.Description = oldDescription;

                AppDialog.Show(this,
                    $"\"{DescribeEntry(form.Result)}\" is a round on the program, so its class and its wording come from there "
                    + "- an edit to them here would be written back over the next time the program changed.\r\n\r\n"
                    + "Its line follows whatever the program says. The time you set here has been kept.",
                    "That belongs to the program", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            var (newTime, newClass, newDescription) = (form.Result.ScheduledTime, form.Result.ClassName, form.Result.Description);

            if ((newTime, newClass, newDescription) != (oldTime, oldClass, oldDescription))
            {
                _undo.Capture($"Edit '{DescribeEntry(form.Result)}'", () =>
                {
                    // Onto the line as it is now, by id, and only what this
                    // edit changed - a tick made since stays ticked.
                    if (schedule.Everything.FirstOrDefault(e => e.Id == entryId) is { } current)
                    {
                        current.ScheduledTime = oldTime;
                        current.ClassName = oldClass;
                        current.Description = oldDescription;
                        schedule.Update(current);
                    }

                    if (!IsDisposed)
                    {
                        RefreshAll();
                    }
                });
            }

            _schedule.Update(form.Result);
            RefreshAll();
        }
    }

    /// <summary>
    /// Which of the lines a delete took should go back when it is undone:
    /// the ones that are not on the sheet now. Pure, so the undo's rule can
    /// be tested without the page.
    ///
    /// By id across every race day, not just the current one, so an undo
    /// made after switching days still reaches the day it was made on - the
    /// line carries its own EventId - and so a line somebody typed again by
    /// hand since is left exactly as it is rather than the sheet ending up
    /// with two of it. The same rule DayScheduleStore.RestoreTimes keeps.
    /// </summary>
    internal static List<ScheduleEntry> LinesToPutBack(
        IEnumerable<ScheduleEntry> deleted,
        IEnumerable<ScheduleEntry> onSheetNow)
    {
        var there = new HashSet<string>(onSheetNow.Select(e => e.Id), StringComparer.Ordinal);

        // Add is false for an id already there, which also stops the same
        // line being put back twice if it is in the list twice.
        return deleted.Where(e => there.Add(e.Id)).ToList();
    }

    private void DeleteSelected()
    {
        var removed = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(r => r.Index)
            .Select(r => r.Tag as string)
            .Where(id => id is not null)
            .Select(id => _schedule.All.FirstOrDefault(x => x.Id == id))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        if (removed.Count == 0)
        {
            return;
        }

        // A round's line is not this page's to delete. The programme owns which
        // rounds there are, so deleting the line here would only have it
        // written back by the next rebuild - which is exactly the kind of
        // quietly-undone edit this whole change is about removing.
        var rounds = removed.Where(e => e.IsGenerated).ToList();
        removed = removed.Where(e => !e.IsGenerated).ToList();

        if (removed.Count == 0)
        {
            AppDialog.Show(this,
                rounds.Count == 1
                    ? $"\"{DescribeEntry(rounds[0])}\" is a round on the program, not a line on this sheet.\r\n\r\n"
                      + "Take the round off the program on Next Steps and its line goes with it."
                    : $"Those {rounds.Count} lines are rounds on the program, not lines on this sheet.\r\n\r\n"
                      + "Take the rounds off the program on Next Steps and their lines go with them.",
                "That belongs to the program", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        var leftAlone = rounds.Count == 0
            ? string.Empty
            : $"\r\n\r\n{Plural.Of(rounds.Count, "round")} in the selection {(rounds.Count == 1 ? "is" : "are")} "
              + "left alone - those belong to the program.";

        var what = removed.Count == 1
            ? $"Delete \"{DescribeEntry(removed[0])}\"?"
            : $"Delete these {removed.Count} schedule entries?\r\n\r\n"
              + string.Join("\r\n", removed.Select(x => $"   {DescribeEntry(x)}"));

        if (AppDialog.Show(this, what + leftAlone, "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // Just these lines, put back by id - not a snapshot of the whole
        // sheet.
        //
        // Restore(snapshot) puts the sheet back as it stood BEFORE the
        // delete, which takes back everything done to it since: a second
        // delete made after this one came back with it (and left a stale
        // entry on the undo stack), a round's line that appeared since went
        // away, and the order of the club's own lines inside a slot was
        // reset. The Edit undo a few lines above already keeps the right
        // rule, and so do CheckInStore.RestoreCheckedIn and
        // RaceDayOpsToday.DayUpdate - an undo takes back the one thing it
        // names and nothing else.
        //
        // Read before the delete, like the Edit undo: _schedule into a local
        // so the undo does not reach through a page that has been disposed.
        var schedule = _schedule;
        var deleted = removed.ToList();

        _undo.Capture(
            removed.Count == 1 ? $"Delete '{DescribeEntry(removed[0])}'" : $"Delete {removed.Count} schedule entries",
            () =>
            {
                // The same objects that were taken out, so each line comes
                // back with its own id, its day, its anchor, its time and its
                // tick. A line typed again by hand since keeps the one that is
                // there - see LinesToPutBack.
                foreach (var entry in LinesToPutBack(deleted, schedule.Everything))
                {
                    schedule.Add(entry);
                }

                if (!IsDisposed)
                {
                    RefreshAll();
                }
            });

        foreach (var entry in removed)
        {
            _schedule.Delete(entry.Id);
        }

        RefreshAll();
    }

    /// <summary>Raised to move a round one place on the programme, which is the only running order there is.</summary>
    public event Action<string, int>? MoveRoundRequested;

    /// <summary>
    /// Moves the selected line one place up or down.
    ///
    /// Which is two different operations, and that is the point. A round's line
    /// is the round, so it moves by moving the round on the programme, and this
    /// sheet redraws because it reads that order rather than because this method
    /// wrote a new one. One of the club's own lines moves its anchor instead -
    /// it is the only thing on this page whose position is this page's to keep.
    ///
    /// The old version called Move on the store for both, against a sequence
    /// stored in the timetable's own file. Moving a heat here therefore did
    /// nothing to the programme, and the next rebuild put it back.
    /// </summary>
    private void MoveSelected(int direction)
    {
        if (SelectedEntry() is not { } entry)
        {
            return;
        }

        if (entry.IsGenerated)
        {
            // The host routes this to the programme, whose redraw tells this
            // page to follow. Nothing to refresh from here.
            _reselect = entry.Id;
            MoveRoundRequested?.Invoke(entry.RoundId, direction);

            return;
        }

        if (_schedule.MoveTypedLine(entry.Id, direction))
        {
            _reselect = entry.Id;
            RefreshAll();
        }
    }

    /// <summary>
    /// Stamps a time on every line, worked down the sheet from the first
    /// race.
    ///
    /// THE RULE, which is one sentence: each of the programme's rounds gets
    /// the clock and moves it on by the minutes allowed; one of your own
    /// lines that already has a time keeps it and RESETS the clock to it;
    /// one without a time gets the clock and does not move it on.
    ///
    /// The reset is what makes it useful rather than merely quick. A club
    /// night does not run at a constant rate through lunch, and a sheet
    /// that says the last main goes out at 15:40 because it added thirty
    /// races' worth of minutes to nine o'clock is a sheet nobody believes.
    /// Typing "13:00" against lunch anchors the afternoon to it.
    ///
    /// A line with no time and no duration - "doors open", "presentation" -
    /// is a marker for a moment, not a slot, so it takes the clock without
    /// spending any of it.
    ///
    /// The start time is written back onto the race day
    /// (RaceEvent.FirstRaceLocalTime), so The Day's field and this dialog
    /// are the same number rather than two that drift.
    /// </summary>
    private void FillTimes()
    {
        var lines = _schedule.All;

        if (lines.Count == 0)
        {
            AppDialog.Show(this,
                "There is nothing on the timetable to put times against yet. Build tonight's program on Next Steps "
                + "and its lines appear here.",
                "Nothing to fill in", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        var day = _events.Active is { IsLegacy: false } active ? active : null;

        // The day's own first-race time when it has one; otherwise ten
        // o'clock, which is a club morning rather than a midnight that
        // nobody meant.
        var startAt = day?.FirstRaceLocalTime ?? TimeSpan.FromHours(10);

        using var ask = new FillTimesForm(startAt, _lastFillMinutes);

        if (ask.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _lastFillMinutes = ask.MinutesEach;

        var wasFirstRace = day?.FirstRaceLocalTime;

        // What each stamped line said before, written down as text as the
        // pass goes - filled in below. Not a Snapshot: that holds the same
        // line objects this pass changes in place, so its undo used to put
        // back only the start time and leave every line restamped. See
        // DayScheduleStore.RestoreTimes.
        var changes = new List<DayScheduleStore.TimeChange>();

        _undo.Capture("Fill in the times", () =>
        {
            _schedule.RestoreTimes(changes);

            if (day is not null && _events.Get(day.Id) is { } live)
            {
                live.FirstRaceLocalTime = wasFirstRace;
                _events.Upsert(live);
            }

            if (!IsDisposed)
            {
                RefreshAll();
            }
        });

        var clock = DateTime.Today.Add(ask.StartAt);
        var step = TimeSpan.FromMinutes(ask.MinutesEach);
        var stamped = new List<ScheduleEntry>();

        foreach (var line in lines)
        {
            // Yours, with something already typed in the time: left exactly
            // as it is. A line you typed is not the programme's to restamp,
            // and the column is deliberately free text - "~10:15", "after
            // lunch", "TBD" are all legitimate entries on a club's sheet.
            // This used to keep only the ones DateTime could parse and
            // write over the rest, so the pass quietly turned "TBD" into a
            // time nobody chose.
            if (!line.IsGenerated && !string.IsNullOrWhiteSpace(line.ScheduledTime))
            {
                // A real time anchors what follows; text that is not a time
                // says nothing about when the next race runs, so the clock
                // carries on from where it was.
                //
                // Only the time of day is taken - the date on `clock` never
                // reaches the sheet, which is stamped "HH:mm".
                if (DateTime.TryParse(line.ScheduledTime, out var pinned))
                {
                    clock = clock.Date.Add(pinned.TimeOfDay);
                }

                continue;
            }

            var was = line.ScheduledTime;
            line.ScheduledTime = clock.ToString("HH:mm");
            changes.Add(new DayScheduleStore.TimeChange(line.Id, was, line.ScheduledTime));
            stamped.Add(line);

            if (line.IsGenerated)
            {
                clock = clock.Add(step);
            }
        }

        // One write for the whole pass - see UpdateMany.
        _schedule.UpdateMany(stamped);

        if (day is not null)
        {
            day.FirstRaceLocalTime = ask.StartAt;
            _events.Upsert(day);
        }

        RefreshAll();
        TimesFilled?.Invoke();
    }

    /// <summary>Raised after the times are filled in, so the pages that show them - Today's Due column - re-read.</summary>
    public event Action? TimesFilled;

    /// <summary>
    /// The gap last used, so filling the sheet a second time after a
    /// reorder opens on the number that was right the first time. Per
    /// session on purpose: it is a property of tonight's format, and a club
    /// that changes format has changed it for a reason.
    /// </summary>
    private int _lastFillMinutes = 10;

    private void ClearAll()
    {
        if (_schedule.All.Count == 0)
        {
            return;
        }

        if (AppDialog.Show(this, "Clear the entire schedule? Undo puts it back - but only until a few more changes are made or the app is closed.", "Confirm clear", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            var snapshot = _schedule.Snapshot();
            _undo.Capture("Clear Schedule", () =>
            {
                _schedule.Restore(snapshot);
                RefreshAll();
            });

            _schedule.ClearAll();
            RefreshAll();
        }
    }

    private void StartCountdown()
    {
        var nextUp = _schedule.NextUp;
        if (nextUp is null)
        {
            AppDialog.Show(this, "Add a schedule entry first, or mark one not-yet-complete - there's nothing to count down to.",
                "Nothing to count down to", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _countdownLabel = string.IsNullOrWhiteSpace(nextUp.ClassName) && string.IsNullOrWhiteSpace(nextUp.Description)
            ? "the next event"
            : string.Join(" ", new[] { nextUp.ClassName, nextUp.Description }.Where(s => !string.IsNullOrWhiteSpace(s)));

        _countdownEndsUtc = DateTime.UtcNow.AddMinutes((double)_numMinutes.Value);

        var remaining = RemainingSeconds();

        // A call already inside the countdown is not made at all. Started at
        // three minutes, "5 minutes to..." on the very first tick is simply
        // wrong; started at exactly five, it would say what the person who
        // pressed Start has just set.
        _announced5Min = remaining <= 300;
        _announced1Min = remaining <= 60;

        _numMinutes.Enabled = false;
        _btnStartCountdown.Enabled = false;
        _btnStopCountdown.Enabled = true;

        UpdateCountdownLabel();
        _countdownTimer.Start();
    }

    private void StopCountdown(bool announceNow)
    {
        _countdownTimer.Stop();
        _countdownEndsUtc = null;
        _numMinutes.Enabled = true;
        _btnStartCountdown.Enabled = true;
        _btnStopCountdown.Enabled = false;
        _lblCountdown.Text = string.Empty;

        if (announceNow)
        {
            _announcer.SpeakReminder($"Time's up for {_countdownLabel}!");
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        var remaining = RemainingSeconds();

        if (remaining <= 0)
        {
            StopCountdown(announceNow: true);
            return;
        }

        // Two separate checks rather than if/else: each call stands on its
        // own. The five-minute one only while there are more than 60
        // seconds left, so the two can never be spoken back to back.
        //
        // Both are latched, so a tick the message pump swallowed - or a
        // laptop lid closed for a minute - still gets the call made on the
        // next one rather than skipping it. A gap that steps clean past a
        // mark makes that call late; a gap that steps past the end of the
        // countdown makes none of them and goes straight to "time's up",
        // which is the honest answer when the minute it was warning about
        // has already gone.
        if (remaining <= 300 && remaining > 60 && !_announced5Min)
        {
            _announced5Min = true;
            _announcer.SpeakReminder($"5 minutes to {_countdownLabel}.");
        }

        if (remaining <= 60 && !_announced1Min)
        {
            _announced1Min = true;
            _announcer.SpeakReminder($"1 minute to {_countdownLabel}.");
        }

        UpdateCountdownLabel();
    }

    /// <summary>What is left, by the clock. Zero when nothing is running or the deadline has gone.</summary>
    private int RemainingSeconds()
    {
        if (_countdownEndsUtc is not { } ends)
        {
            return 0;
        }

        var left = (ends - DateTime.UtcNow).TotalSeconds;

        // Rounded UP, so a countdown started at ten minutes reads 10:00
        // rather than 09:59 on its first draw.
        return left <= 0 ? 0 : (int)Math.Ceiling(left);
    }

    private void UpdateCountdownLabel()
    {
        var span = TimeSpan.FromSeconds(RemainingSeconds());
        _lblCountdown.Text = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
    }

    /// <summary>The automatic countdown was ticked or unticked, here or on the Between races card: shown here too.</summary>
    private void AutoCountdownTicked(AppSettings settings)
    {
        if (!ReferenceEquals(settings, _settings) || IsDisposed)
        {
            return;
        }

        // Its CheckedChanged finds the setting already says so and saves nothing.
        _chkAutoCountdown.Checked = settings.BetweenRacesAutoCountdown;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A static event would keep this control alive (10 Nov 2026, bug hunt 16).
            AutoCountdownTick.Changed -= AutoCountdownTicked;

            // A WinForms timer holds a reference to this control through
            // its Tick handler. Left undisposed it keeps the control alive
            // and can fire against a disposed one, which throws from a
            // place with no useful stack.
            _countdownTimer.Stop();
            _countdownTimer.Tick -= CountdownTimer_Tick;
            _countdownTimer.Dispose();
        }

        base.Dispose(disposing);

        // After the figure that wears it is gone - see FitCountdown.
        if (disposing)
        {
            _countdownFont?.Dispose();
            _countdownFont = null;
        }
    }

}
