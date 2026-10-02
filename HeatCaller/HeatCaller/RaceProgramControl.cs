namespace HeatCaller;

/// <summary>
/// The day's running order, and the one thing to do next.
///
/// WHAT THIS REPLACED, and why, because the shape of this file only makes
/// sense against it:
///
/// The old Program tab was a class-at-a-time editor. You picked a class
/// from a dropdown, and everything below - a "Generate Qualifying Heats"
/// box, a wrapping row of five more buttons, an auto-advance row, a grid,
/// and a second wrapping row of five buttons under that - operated on
/// whichever class happened to be selected. Eleven buttons, all the same
/// size, all the same colour, none of them saying which one you wanted.
/// Nothing on the screen could answer "what runs next?", because the
/// screen only ever knew about one class and a race day has several.
///
/// Three things changed.
///
/// FIRST, the list is the whole day. Rounds are stored with a single
/// running Order across every class already, so the day has always had a
/// real running order - the old tab just filtered it away. The class
/// dropdown is still here, as a FILTER, but the default is everything,
/// in the order it goes out.
///
/// SECOND, the day is built in one press. Which classes are racing, how
/// many qualifying rounds, how many drivers per heat and whether to count
/// only checked-in drivers are all properties of the race day, and they
/// now live on the Event tab where the rest of the day's shape is set. So
/// "Generate Heats" and "Generate All Rounds" - which differed only in
/// whether they read the round count - collapse into one command that
/// builds the whole programme for every class at once.
///
/// THIRD, and this is the point of the exercise: the next step. One
/// sentence saying where the day has got to, and one button that does the
/// next thing. The logic behind it is in ProgramNextStep, kept separate
/// because it has to be right rather than merely tidy. This page used to
/// show it as a banner over the list; it is Next Steps' card now, above
/// this page (see TodayControl), and this page works it out for that card
/// and for Race Control's Next Up bar.
///
/// The buttons that remain are in two groups with a real distinction
/// between them: the left group acts on the SELECTED round, the right
/// group on the day. Everything else went into the right-click menu.
///
/// A "More" button opened that menu too, until it came off (29 Sep) to
/// give Next Steps' top half the room - Asher's call, looking at it. The
/// status line says where the menu is until the first command's message
/// takes its place.
/// </summary>
public sealed class RaceProgramControl : UserControl
{
    private readonly RaceClassStore _raceClasses;
    private readonly ProfileStore _profiles;
    private readonly RoundDefinitionStore _rounds;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly SessionHistoryStore _sessionHistory;
    private readonly CheckInStore _checkIns;
    private readonly AppSettings _settings;
    private readonly UndoService _undo;
    private readonly RaceEventStore? _events;

    // --- The page ---

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The class filter and its caption - held so a rebuilt class list can re-measure it.</summary>
    private FieldRow? _filterRow;

    /// <summary>The page the card fills - held so Next Steps can ask how short it can go. See LeastPaneHeight.</summary>
    private FillPage? _page;

    /// <summary>Set while a LeastPaneHeightChanged is waiting to be raised - see PostLeastPaneHeightChanged.</summary>
    private bool _leastPosted;

    /// <summary>The class filter's width, measured from its choices - see Pieces.PickerWidth. Forgotten when the list is rebuilt.</summary>
    private (Font Font, int Count, int Width)? _filterWidth;

    /// <summary>The class filter's width at the normal text size, before it is measured - see FilterWidth.</summary>
    private const int FilterPickerWidth = 190;

    // --- The list ---
    private readonly DataGridView _grid = new();

    // --- Dragging rounds into a new order ---
    //
    // Three pieces of state and no more: where a press landed, which row
    // it landed on, and - once a drag is under way - where a drop would
    // put things. _dropBefore is -1 whenever nothing is being dragged,
    // which is also what tells the paint handler to draw no line.
    private Point _pressAt;
    private int _pressRow = -1;
    private int _dropBefore = -1;
    private int _dropY;

    // --- The footer ---
    private readonly Button _btnStart = new();
    private readonly Button _btnEditDrivers = new();
    private readonly ComboBox _cmbFilter = new();
    private readonly Button _btnStandings = new();
    private readonly Button _btnMarshals = new();
    private readonly Button _btnBumpUps = new();

    /// <summary>
    /// Opens the glossary.
    ///
    /// On this page rather than behind a help menu, because this is the page
    /// the words are on. The buttons here now say what they DO in plain
    /// English, but the grid beside them is full of A-Mains and qualifiers -
    /// the sport's own words, deliberately left alone, for the reasons set
    /// out at the top of RacingWordsForm. This is where they get explained.
    /// </summary>
    private readonly Button _btnWords = new();

    /// <summary>
    /// Opens the timetable drawer. Down here with the other day tools - it
    /// was a second row of buttons at the top of Next Steps, beside a second
    /// copy of the glossary button.
    /// </summary>
    private readonly Button _btnTimetable = new();

    /// <summary>Raised when the timetable is asked for. The host owns the window it opens in - see MainForm.</summary>
    public event Action? TimetableRequested;

    /// <summary>
    /// Raised when the least height this page can do with has changed - the
    /// buttons wrapping onto another line, a new text size - after the
    /// layout that changed it. Next Steps' split keeps the page at least
    /// that tall. See LeastPaneHeight.
    /// </summary>
    public event Action? LeastPaneHeightChanged;

    /// <summary>The timetable, read-only - only for the grid's Due column. Null when the host did not pass one.</summary>
    private readonly DayScheduleStore? _schedule;

    /// <summary>
    /// What the buttons and the round menu were last set up for.
    ///
    /// A field rather than a read of the day, because the buttons and the
    /// menu's Opening handler both have to agree about which commands
    /// exist, and asking the store twice invites them to disagree halfway
    /// through a rebuild. Set only by ApplyDayShape.
    /// </summary>
    private bool _clubNight;

    /// <summary>Whether ApplyDayShape has run once - before it has, the buttons have never been set up for any kind of night.</summary>
    private bool _shapeApplied;

    private readonly Label _lblStatus = new();

    /// <summary>The next step, as last worked out by RefreshNextStep. Run by RunPrimaryAction.</summary>
    private ProgramStep _step = new(ProgramStage.NoEvent, string.Empty, string.Empty, "...");

    /// <summary>Raised when the director wants a round on track now. MainForm applies it and starts the countdown.</summary>
    public event Action<RoundDefinition>? RoundStartRequested;

    /// <summary>Raised to run a round as a simulated race - nothing is recorded against it.</summary>
    public event Action<RoundDefinition>? RoundDemoRequested;

    /// <summary>Everything this tab does, echoed to the developer console.</summary>
    public event Action<string>? StatusMessage;

    /// <summary>
    /// Raised when the next step is something that has to be done on the
    /// Event tab. Next Steps' card offers the button; MainForm knows how to
    /// switch tabs, and this control deliberately does not.
    /// </summary>
    public event Action? GoToEventTab;

    /// <summary>Raised when the next step is "go and put drivers in a class".</summary>
    public event Action? ManageDriversRequested;

    /// <summary>Raised when the next step is "check somebody in" - the same journey, aimed at the entries tab.</summary>
    public event Action? GoToEntriesTab;

    /// <summary>
    /// Raised after anything changes which rounds are on today's
    /// programme, or their order - so the timetable can follow rather than
    /// waiting to be rebuilt by hand.
    /// </summary>
    public event Action? ProgrammeChanged;

    /// <summary>Raised when the club has no classes at all yet, which is fixed on its own page rather than on this one.</summary>
    public event Action? SetUpClassesRequested;

    /// <summary>
    /// Raised when there is no race day to build a programme against.
    ///
    /// The button used to say "Go to the Event Tab", which left the one
    /// press that mattered still to be found once you got there. It starts
    /// the day now, and the host takes you to it to be named.
    /// </summary>
    public event Action? StartRaceDayRequested;

    /// <summary>
    /// The id of the round on track right now - staged, counting down,
    /// racing, or finished and not yet saved - or null when nothing is.
    /// Supplied by MainForm, which owns Race Control; this control cannot
    /// see the race.
    ///
    /// Read by the undo of a build or a seeding: a heat that is out on track
    /// has not been recorded YET, and the undo used to delete it from under
    /// the race, so its result was saved against a round that no longer
    /// existed. Null (not wired) keeps the old behaviour.
    /// </summary>
    public Func<string?>? RoundOnTrackId { get; set; }

    public RaceProgramControl(
        RaceClassStore raceClasses,
        ProfileStore profiles,
        RoundDefinitionStore rounds,
        TrackLayoutStore trackLayouts,
        SessionHistoryStore sessionHistory,
        CheckInStore checkIns,
        AppSettings settings,
        UndoService undo,
        RaceEventStore? events = null,

        // Optional, and read-only here: the grid shows each round's due time
        // off the timetable, and nothing on this page writes one back. Null
        // simply leaves the Due column hidden.
        DayScheduleStore? schedule = null)
    {
        _schedule = schedule;
        _raceClasses = raceClasses;
        _profiles = profiles;
        _rounds = rounds;
        _trackLayouts = trackLayouts;
        _sessionHistory = sessionHistory;
        _checkIns = checkIns;
        _settings = settings;
        _undo = undo;
        _events = events;

        Dock = DockStyle.Fill;

        // The body face, worn rather than inherited: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise take.
        //
        // No MinimumSize. This page is the lower half of Next Steps' split,
        // and WinForms honours MinimumSize over the size its parent hands
        // out - the 560px floor it once had pushed the buttons off the
        // bottom of the pane. A pane too short for the list's least scrolls
        // instead (FillPage).
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

    /// <summary>
    /// The title of one particular refusal, named rather than written out
    /// twice.
    ///
    /// TakeSeedOffer decides what "yes" means by comparing the refusal's
    /// title against this, which means the wording on screen was also a
    /// branch in the code. Renaming the heading - which is exactly what the
    /// plain-English pass did - silently sent the wrong offer, and nothing
    /// would have complained. A const makes the rename impossible to get
    /// half-done.
    /// </summary>
    private const string NothingToSeedFromTitle = "Nothing to build the finals from";

    /// <summary>The other seeding refusal, named for the same reason - it picks the dialog's icon.</summary>
    private const string MainsAlreadyRunTitle = "Mains already run";

    // ------------------------------------------------------------------
    //  Layout
    // ------------------------------------------------------------------

    /// <summary>
    /// The page is one card, filling the lower half of Next Steps: the
    /// class filter, the list of rounds taking whatever height is left,
    /// the buttons under it - the ones for the selected round on the left,
    /// the ones for the day on the right - and the status line. Built on
    /// the layout pieces (LayoutPieces.cs); nothing is placed by hand.
    ///
    /// Heats &amp; Mains is no longer a tab of its own, and this page no
    /// longer carries the next-step banner it had then: Next Steps' card
    /// says the same sentence, because both ask ProgramNextStep, and
    /// stacked one above the other they read as two different
    /// instructions. The banner was hidden for as long as this page has
    /// been Next Steps' lower half, and has gone; the step it showed is
    /// still worked out here (RefreshNextStep).
    ///
    /// It used to be three bands docked top and bottom, each placing its
    /// own children on every resize - the strip over the list, the list,
    /// and a two-row footer that decided for itself whether the day tools
    /// fitted beside the round commands.
    /// </summary>
    private void BuildLayout()
    {
        BuildGrid();
        BuildButtons();

        // --- The class filter, over the list it narrows ---
        _cmbFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbFilter.SelectedIndexChanged += (_, _) =>
        {
            _settings.LastRaceProgramClass = FilterClass;
            _settings.Save();
            RefreshGrid();
        };

        var filterRow = new FieldRow();

        filterRow.Add(new Label { Text = "Class:" }, _cmbFilter, FilterWidth);
        _filterRow = filterRow;

        // --- The status line ---
        //
        // What the last command did. Two lines kept for it whatever it says,
        // so the buttons above it do not hop as the messages come and go.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblStatus, () => AppTheme.SmallFont);
        _textSize.ReserveLines(_lblStatus, 2);

        // Until a command says what it did: where everything that is not a
        // button is. The menu has no button of its own - see the summary.
        _lblStatus.Text = "Right-click the list for everything else - building tonight's program, the heat sheet, "
            + "moving and deleting rounds, and more.";

        // --- The card ---
        //
        // The split is the point of the two bars. Every button on the left
        // needs a round chosen and is disabled without one; nothing on the
        // right does. That is the distinction the old single wrapping row
        // of ten buttons had no way to express. The glossary - the one thing
        // here that is not an action on the day - ends up hard against the
        // right-hand edge, where help is looked for.
        var roundCommands = new ButtonBar().With(_btnStart, _btnEditDrivers);
        var dayTools = new ButtonBar(alignRight: true).With(_btnWords, _btnBumpUps, _btnMarshals, _btnStandings, _btnTimetable);

        var list = new Section("Tonight's program");

        list.Add(filterRow);
        list.AddFill(_grid, LeastGridHeight);
        list.Add(new SplitBar(roundCommands, dayTools));
        list.Add(_lblStatus, gapBelow: 0);

        var page = new FillPage();

        page.AddFill(list, list.LeastHeight, gapBelow: 0);
        page.LeastHeightChanged += PostLeastPaneHeightChanged;

        // No blank band under the card: this pane's bottom edge is the
        // window's, and the room the theme leaves under a scrolling page
        // was 32px taken from the half above, which gives way to this one's
        // least. See ScrollTheming.NoRoomBelow.
        ScrollTheming.NoRoomBelow(page);

        _page = page;
        Controls.Add(page);
    }

    /// <summary>
    /// The least height this page can do with at <paramref name="width"/>:
    /// the class filter, the headings and three rounds, the buttons and the
    /// status line, in the card and its margins. Any shorter and it scrolls,
    /// and the buttons are the first thing to go below the fold - so Next
    /// Steps keeps its lower half at least this tall, and scrolls its top
    /// half instead.
    /// </summary>
    public int LeastPaneHeight(int width) => _page?.LeastHeight(width) ?? 0;

    /// <summary>
    /// Tells the host the least height has changed - after the layout that
    /// changed it, not during, for the reason TodayControl gives for its
    /// own ContentHeightChanged: the host answers by moving the splitter,
    /// which resizes this page. Once however many layouts in a row changed
    /// it.
    /// </summary>
    private void PostLeastPaneHeightChanged()
    {
        if (_leastPosted || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        _leastPosted = true;

        BeginInvoke(new Action(() =>
        {
            _leastPosted = false;

            if (!IsDisposed)
            {
                LeastPaneHeightChanged?.Invoke();
            }
        }));
    }

    /// <summary>
    /// The class filter: wide enough for the longest class name, and never
    /// under the 190px it had at the normal text size.
    /// </summary>
    private int FilterWidth() =>
        Math.Max(UiLayout.AtTextSize(FilterPickerWidth), Pieces.PickerWidth(_cmbFilter, ref _filterWidth, AllClasses));

    /// <summary>The least height the list can do with: its headings and three rounds. Below that the pane scrolls instead - see FillPage.</summary>
    private int LeastGridHeight() =>
        _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height;

    private void BuildGrid()
    {
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;

        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                StartSelectedRound();
            }
        };

        _grid.SelectionChanged += (_, _) => UpdateSelectionActions();

        BuildRoundMenu();
        EnableDragReorder();

        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // The Drivers cell is only a count, and "3" never says which three.
        // Hovering it lists them - see DriversTip - so a heat can be checked
        // without opening Edit Drivers.
        //
        // A HoverNote rather than the grid's tooltip: switching that on put
        // pop-ups over every other column as well (see HoverNote). The text
        // rides on the cell's Tag, not its ToolTipText, for the same reason.
        var note = new HoverNote(_grid);
        _driversNote = note;
        _grid.CellMouseEnter += (_, e) => ShowDriversNote(e.RowIndex, e.ColumnIndex);
        _grid.CellMouseLeave += (_, _) => note.Hide();
        _grid.MouseLeave += (_, _) => note.Hide();
        _grid.MouseDown += (_, _) => note.Hide();
        _grid.Scroll += (_, _) => note.Hide();
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "order", HeaderText = "#", FillWeight = 22 },

            // When it is due, off the timetable.
            //
            // Here because the timetable is a drawer off Today now rather
            // than a tab, and the times were the one thing on that sheet
            // worth seeing without opening it. Hidden until at least one of
            // today's rounds actually has a time - a club that never sets
            // them would otherwise get a column of dashes it cannot fill
            // from this page. See RefreshGrid.
            new DataGridViewTextBoxColumn { Name = "due", HeaderText = "Due", FillWeight = 42, Visible = false },

            new DataGridViewTextBoxColumn { Name = "round", HeaderText = "Round", FillWeight = 95 },

            // What KIND of round this is - and it was not on screen at all.
            //
            // A row said "A-Main" or "Q2 Heat 3" and nothing else, so
            // whether a row was a qualifier or a main could only be read
            // out of its label, which is free text somebody can rename. A
            // main's tier was never shown anywhere, even though bump-ups
            // are entirely about tiers and a hand-added main has its tier
            // read from its label. The phone has been given both all along
            // (RoundRow.kind and .tier); the desk had neither.
            new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "Kind", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "class", HeaderText = "Class", FillWeight = 105 },
            new DataGridViewTextBoxColumn { Name = "drivers", HeaderText = "Drivers", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "track", HeaderText = "Track", FillWeight = 80 },
            new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", FillWeight = 70 });
    }

    /// <summary>The buttons under the list, and what each does. Their bars size them to their captions - see BuildLayout.</summary>
    private void BuildButtons()
    {
        _btnStart.Text = "Start Selected";
        _btnStart.Click += (_, _) => StartSelectedRound();

        _btnEditDrivers.Text = "Edit Drivers...";
        _btnEditDrivers.Click += (_, _) => EditRoundRoster();

        _btnStandings.Text = "Standings...";
        _btnStandings.Click += (_, _) => ShowStandings();

        _btnMarshals.Text = "Marshals...";
        _btnMarshals.Click += (_, _) => ShowMarshalAssignments();

        // Was "Bump-Ups...". A bump-up is a real word at a real track and it
        // is still what the glossary and the grid call the thing; a BUTTON is
        // read once, under pressure, by somebody deciding whether to press
        // it, so the button says what pressing it does.
        _btnBumpUps.Text = "Move the Top Finishers Up...";
        _btnBumpUps.Click += (_, _) => ApplyBumpUps(null);

        // Short, so the day tools fit beside the round commands where they
        // can. The window it opens is titled "What these words mean", which
        // says the rest.
        _btnWords.Text = "Glossary...";
        _btnWords.Click += (_, _) => RacingWordsForm.ShowFor(FindForm());

        _btnTimetable.Text = "Timetable...";
        _btnTimetable.Click += (_, _) => TimetableRequested?.Invoke();
    }

    /// <summary>
    /// Brings the buttons and the round menu into line with what kind of
    /// night the open day is. Cheap, and safe to call on every refresh: it
    /// returns without touching anything when nothing has changed.
    ///
    /// "Move the Top Finishers Up..." goes on a club night, because there
    /// is nothing above for anyone to move up into; its bar closes up
    /// behind it.
    /// </summary>
    private void ApplyDayShape()
    {
        var wanted = ActiveDay?.IsClubNight == true;

        if (_shapeApplied && wanted == _clubNight)
        {
            return;
        }

        _shapeApplied = true;
        _clubNight = wanted;

        _btnBumpUps.Visible = !_clubNight;

        // On a club night there is no TQ, so the same list is simply the
        // night's order.
        _btnStandings.Text = _clubNight ? "Tonight's Standings..." : "Standings...";
    }

    /// <summary>
    /// The round menu, on the grid's right-click - one list of what can be
    /// done to a round, rather than a button strip and a menu drifting apart
    /// from each other.
    /// </summary>
    private void BuildRoundMenu()
    {
        var menu = new ContextMenuStrip { Font = AppTheme.BodyFont, ShowImageMargin = false };

        var miStart = new ToolStripMenuItem("Start This Round", null, (_, _) => StartSelectedRound())
        {
            Font = new Font(AppTheme.BodyFont, FontStyle.Bold),
        };

        var miDemo = new ToolStripMenuItem("Start as Demo...", null, (_, _) => StartRoundAsDemo());
        var miEdit = new ToolStripMenuItem("Edit Drivers...", null, (_, _) => EditRoundRoster());

        // About the CLASS rather than one round, so it stays available with
        // nothing selected - which is the state you are in when you look at
        // the programme and think "those heats are lopsided".
        var miBalance = new ToolStripMenuItem("Move Drivers Between Heats...", null, (_, _) => BalanceHeats());
        var miUp = new ToolStripMenuItem("Move Up", null, (_, _) => MoveSelected(up: true));
        var miDown = new ToolStripMenuItem("Move Down", null, (_, _) => MoveSelected(up: false));
        // --- The day-level commands ---
        //
        // THE BANNER IS A SHORTCUT, NEVER THE ONLY DOOR. That is the rule,
        // and both of these are here because it was broken twice.
        //
        // The next step offers exactly one thing: whatever is due next. It has
        // to, or it is not a next step - it is a toolbar with an opinion.
        // But "due next" is a judgement about the usual shape of a race
        // day, and a race day that is not shaped that way still needs the
        // command. Build only ever appeared on a programme with nothing in
        // it, so a day already holding rounds had no way to rebuild them;
        // Seed only appeared once every scheduled round was run, so a club
        // running one class right through could not seed its mains while
        // another class still had heats to go.
        //
        // Both are safe to reach for at any time: each one says exactly
        // what it will replace, and neither touches a recorded round.
        //
        // Both captions say what the command does rather than naming the
        // operation: "Build the Qualifying Programme" and "Seed the Mains"
        // are correct and were meaningless to anybody who had not run a race
        // day before. What they build is unchanged, and the rounds they
        // produce are still labelled in the sport's own words.
        var miBuild = new ToolStripMenuItem("Build Tonight's Heats...", null,
            (_, _) => BuildQualifyingProgramme());

        var miSeed = new ToolStripMenuItem("Build the Finals from Qualifying...", null,
            (_, _) => SeedMains(string.Empty));

        // Left as "Main" on purpose. This is a noun the sport uses, it is
        // what the grid, the heat sheet and the announcer say, and the
        // glossary explains it. Only the verbs were reworded.
        var miAddMain = new ToolStripMenuItem("Add a Main by Hand...", null, (_, _) => AddMainByHand());
        var miPrint = new ToolStripMenuItem("Save Heat Sheet...", null, (_, _) => PrintHeatSheet());
        var miDelete = new ToolStripMenuItem("Delete", null, (_, _) => DeleteSelectedRounds());

        menu.Items.AddRange(new ToolStripItem[]
        {
            miStart,
            miDemo,
            miEdit,
            miBalance,
            new ToolStripSeparator(),
            miUp,
            miDown,
            new ToolStripSeparator(),
            miBuild,
            miSeed,
            miAddMain,
            miPrint,
            new ToolStripSeparator(),
            miDelete,
        });

        menu.Opening += (_, args) =>
        {
            var selected = _grid.SelectedRows.Count;

            // Add Main and the heat sheet are about the CLASS, not about a
            // round, so they stay available with nothing selected - which
            // is also what makes the menu useful on an empty program: it
            // opens wherever the list is right-clicked, rows or not.
            var single = selected == 1;

            miStart.Enabled = single;
            miDemo.Enabled = single;
            miEdit.Enabled = single;
            miUp.Enabled = single;
            miDown.Enabled = single;
            miDelete.Enabled = selected > 0;
            miDelete.Text = selected > 1 ? $"Delete {selected} Rounds" : "Delete";

            // A club night has no finals. Both of these build one, so on a
            // club night they are not disabled - they are not there. Read
            // from _clubNight, the same field the footer is built from, so
            // the menu and the buttons can never disagree about what kind of
            // night it is.
            miSeed.Visible = !_clubNight;
            miAddMain.Visible = !_clubNight;
            miBuild.Text = _clubNight ? "Build Tonight's Races..." : "Build Tonight's Heats...";

            _ = args;
        };

        AppTheme.StyleContextMenu(menu);

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

            // A selection the person made on purpose - Ctrl+A, a Shift-range,
            // Ctrl-clicked rows - is left ALONE when the clicked row is part
            // of it. Right-clicking inside a selection is how you reach the
            // menu for it, not how you throw it away.
            if (_grid.Rows[hit.RowIndex].Selected)
            {
                return;
            }

            _grid.ClearSelection();
            _grid.Rows[hit.RowIndex].Selected = true;

            // INSIDE the guard, not after it. Assigning CurrentCell collapses
            // the selection to that one row - so this line, sitting outside,
            // undid the check above it on every right-click: select all,
            // right-click, and the menu opened on a single row with "Delete"
            // where "Delete 12 Rounds" should have been.
            _grid.CurrentCell = _grid.Rows[hit.RowIndex].Cells[Math.Max(0, hit.ColumnIndex)];
        };
    }

    // ------------------------------------------------------------------
    //  Reading the day
    // ------------------------------------------------------------------

    /// <summary>The race day being worked on, or null when it is the legacy holder (which is not a race day).</summary>
    private RaceEvent? ActiveDay
    {
        get
        {
            var active = _events?.Active;
            return active is null || active.IsLegacy ? null : active;
        }
    }

    /// <summary>
    /// Every round on today's programme, in running order.
    ///
    /// Scoped to the race day when there is one, so last week's heats do
    /// not appear in this week's list. With no race day open it falls back
    /// to the whole store rather than showing nothing - a club that has
    /// never made an event still has a programme, and the next step is what
    /// tells them to start filing it properly.
    /// </summary>
    private List<RoundDefinition> DayRounds()
    {
        var day = ActiveDay;

        if (day is null)
        {
            return _rounds.All.ToList();
        }

        var ids = day.RoundIds.ToHashSet(StringComparer.Ordinal);

        return _rounds.All.Where(r => ids.Contains(r.Id)).ToList();
    }

    /// <summary>
    /// One class's rounds on today's programme, in running order.
    ///
    /// Seeding, bump-ups, marshals and the heat sheet all used to ask the
    /// store for EVERY round the class has ever had, so last week's A-main
    /// could block tonight's seeding as "already run", or be deleted as an
    /// "unrun main" when tonight's were seeded. They are questions about
    /// today, so they read today's programme - the same one the grid shows.
    ///
    /// "Today" is the race day's own list of rounds, not a calendar date,
    /// so a meeting that runs past midnight is still one day and needs no
    /// special handling here.
    /// </summary>
    private List<RoundDefinition> DayRoundsForClass(string className) =>
        DayRounds()
            .Where(r => string.Equals(r.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Which classes are racing today.
    ///
    /// The race day says so when it has been told; otherwise this reads it
    /// back from whatever is already scheduled, and falls back to every
    /// class that has a driver in it. The fallbacks matter: they are what
    /// stop the page being useless for a club that never fills the Event
    /// tab in.
    /// </summary>
    private List<string> RacingClassNames()
    {
        var day = ActiveDay;

        if (day is not null && day.ClassNames.Count > 0)
        {
            return _raceClasses.All
                .Where(c => day.ClassNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();
        }

        var scheduled = DayRounds()
            .Select(r => r.ClassName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (scheduled.Count > 0)
        {
            return _raceClasses.All
                .Where(c => scheduled.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();
        }

        return _raceClasses.All
            .Where(c => RosterFor(c.Name).Count > 0)
            .Select(c => c.Name)
            .ToList();
    }

    // --- The same readings, for the phone (RaceDayService) ---
    //
    // Public views of the private helpers above rather than copies of
    // them, so the phone's programme is by construction the one this tab
    // shows. UI thread only, like everything else here.

    /// <summary>Every round on today's programme, in running order. See DayRounds.</summary>
    public IReadOnlyList<RoundDefinition> TodaysRounds() => DayRounds();

    /// <summary>Which classes are racing today. See RacingClassNames.</summary>
    public IReadOnlyList<string> TodaysClassNames() => RacingClassNames();

    /// <summary>The drivers a class can put on track today, after the checked-in-only rule. See RosterFor.</summary>
    public IReadOnlyList<DriverProfile> ClassRoster(string className) => RosterFor(className);

    /// <summary>True when a real race day is open - the programme is filed against it.</summary>
    public bool HasRaceDay => ActiveDay is not null;

    /// <summary>Which rounds qualifying counts - the race day's, or none when no race day is open. See QualifyingRoundFilter.</summary>
    public IReadOnlyCollection<string> QualifyingRoundsCounted() => QualifyingRoundFilter();

    /// <summary>
    /// Runs a step somebody else is showing - the Today tab's button.
    ///
    /// The step is handed IN rather than read from this control's own
    /// field. The two tabs refresh at different moments, and a button
    /// captioned "Start Q1 Heat 2" must run that round and not whatever
    /// this tab last happened to work out. Setting it first also keeps the
    /// one rule the next step has: the command and the sentence describing
    /// it always come from the same ProgramStep.
    /// </summary>
    internal void RunStep(ProgramStep step)
    {
        _step = step;
        RunPrimaryAction();
    }

    /// <summary>What Next Steps' card says right now: the one next thing to do.</summary>
    internal ProgramStep NextStep() =>
        ProgramNextStep.Evaluate(_events?.Active, DayRounds(), RacingClasses(), PlannedQualifyingRounds(), _sessionHistory, SetupFacts());

    /// <summary>
    /// Moves rounds in the running order, in front of
    /// <paramref name="anchorId"/> (null = the end), and redraws. Undoable,
    /// the same as a drag - see MoveWithUndo.
    /// </summary>
    public void MoveRoundsBefore(IReadOnlyList<string> ids, string? anchorId)
    {
        MoveWithUndo(ids, anchorId, "Phone: ");
        RefreshGrid();
    }

    /// <summary>
    /// A reorder, with an undo entry that puts the whole running order back.
    ///
    /// These used to have no undo entry, because the stack was three deep
    /// and a reorder can be dragged back by hand. But a drag renumbers the
    /// whole programme, and a slip that drops a block of heats in the wrong
    /// place is exactly what somebody wants one press to put right. The
    /// entry is pushed only once the move has actually changed something
    /// (all on this thread, so nothing can happen in between), so a drop
    /// that goes nowhere does not use up a place on the stack.
    /// </summary>
    private void MoveWithUndo(IReadOnlyList<string> ids, string? anchorId, string undoPrefix = "")
    {
        var rounds = _rounds;
        var before = rounds.OrderSnapshot();

        rounds.MoveBefore(ids, anchorId);

        var after = rounds.OrderSnapshot();

        if (after.Count == before.Count && after.All(kv => before.TryGetValue(kv.Key, out var order) && order == kv.Value))
        {
            return;
        }

        var what = ids.Count == 1
            ? $"Move {rounds.Get(ids[0])?.RoundLabel ?? "a round"} in the running order"
            : $"Move {ids.Count} rounds in the running order";

        _undo.Capture(undoPrefix + what, () =>
        {
            rounds.RestoreOrder(before);

            if (!IsDisposed)
            {
                RefreshGrid();
            }
        });
    }

    private List<ProgramNextStep.RacingClass> RacingClasses() =>
        RacingClassNames()
            .Select(name => new ProgramNextStep.RacingClass(name, RosterFor(name).Count))
            .ToList();

    /// <summary>
    /// What exists in the app at all, for the next step to tell four different
    /// kinds of empty apart - see ProgramNextStep.SetupFacts.
    ///
    /// A driver counts as "in a class" only when the class they name still
    /// exists. A profile left pointing at a renamed or deleted class is
    /// exactly as unable to race as one with no class at all, and saying
    /// "they have a class" about it would send the director looking at a
    /// list that appears correct.
    /// </summary>
    private ProgramNextStep.SetupFacts SetupFacts()
    {
        var real = _raceClasses.All.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drivers = _profiles.AllRaceable;

        var inAClass = drivers
            .Where(p => !string.IsNullOrWhiteSpace(p.ClassName) && real.Contains(p.ClassName.Trim()))
            .ToList();

        return new ProgramNextStep.SetupFacts(
            real.Count,
            drivers.Count,
            inAClass.Count,
            ActiveDay?.CheckedInDriversOnly == true,
            inAClass.Count(p => _checkIns.IsCheckedIn(p.Transponder)));
    }

    /// <summary>
    /// The drivers a class can put on track, after the race day's
    /// checked-in-only rule.
    /// </summary>
    private List<DriverProfile> RosterFor(string className)
    {
        var roster = _profiles.AllRaceable
            .Where(p => string.Equals(p.ClassName, className, StringComparison.OrdinalIgnoreCase));

        if (ActiveDay?.CheckedInDriversOnly == true)
        {
            roster = roster.Where(p => _checkIns.IsCheckedIn(p.Transponder));
        }

        return roster.ToList();
    }

    /// <summary>How many drivers share a heat. The race day's number when it has one, otherwise the last race day's (RaceEventStore.HeatSizeFor).</summary>
    private int HeatSize()
    {
        if (_events is not null)
        {
            return _events.HeatSizeFor(ActiveDay);
        }

        var wanted = ActiveDay?.DriversPerHeat ?? 0;

        return Math.Clamp(wanted > 0 ? wanted : RaceEventStore.DefaultDriversPerHeat, 2, 20);
    }

    private int PlannedQualifyingRounds() => Math.Clamp(ActiveDay?.QualifyingRounds ?? 3, 1, 10);

    /// <summary>The track every generated round starts on - the race day's layout.</summary>
    private string DayTrack() => ActiveDay?.TrackLayoutName?.Trim() ?? string.Empty;

    /// <summary>
    /// A class's colour, brightened for a dark background.
    ///
    /// The stored colour is chosen to print on white paper, where a light
    /// tint would vanish; on the app's near-black it needs lifting or every
    /// class reads as the same dark smudge.
    /// </summary>
    private Color ClassColourFor(string className) =>
        RaceClass.OnScreenColourFor(ClassByName(className), className);

    private RaceClass? ClassByName(string className) =>
        _raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase));

    /// <summary>The class the filter is pinned to, or empty for all of them.</summary>
    private string FilterClass => _cmbFilter.SelectedItem as string is { } name && name != AllClasses
        ? name
        : string.Empty;

    private const string AllClasses = "All classes";

    /// <summary>
    /// Which class a class-scoped tool should act on.
    ///
    /// In order: whatever the filter is pinned to, then the class of the
    /// selected round, then the only class racing if there is only one.
    /// Asking is the last resort rather than the first, because on a
    /// single-class club night the answer is never in doubt.
    /// </summary>
    /// <summary>
    /// The class an action should act on: the one it was handed, or -
    /// when it was handed none - whatever the page can work out, with
    /// TargetClass asking the question when it cannot work one out either.
    ///
    /// Standings, Seed the Mains and Bump-Ups all used to return in
    /// silence on a blank class. That is reachable from more than one
    /// place - the next step's "programme complete" state with no class to
    /// name, a menu item pressed on an empty programme - and every one of
    /// them looked like a button that did nothing at all. One of the
    /// refusals written for this, PlanSeedMains's own "Pick a class", was
    /// unreachable from the desk entirely: SeedMains returned before ever
    /// calling it, so only the phone could see it.
    /// </summary>
    /// <summary>
    /// A refusal that carries the way out of it.
    ///
    /// Most of the "you cannot do that" messages on this page named a
    /// remedy in prose and then offered a single OK - so a director who
    /// had just been told to build the qualifying programme first had to
    /// dismiss the box, find the menu they had come out of, and read the
    /// sentence again from memory. The two that always got this right are
    /// the on-track refusals, because waiting IS the path forward and they
    /// say so.
    /// </summary>
    /// <summary>The title both club-night refusals carry. A constant because comparing against a heading as control flow is how the seed offer once picked the wrong branch.</summary>
    public const string ClubNightTitle = "That's a race day thing";

    /// <summary>
    /// Why a finals command cannot run on a club night, or null when the
    /// open day is not one.
    ///
    /// Lives here, in the shared plan layer, rather than beside the buttons:
    /// the buttons and menu items are already gone on a club night, so the
    /// paths that still reach these are the ones that are not buttons - the
    /// phone, a card action on a day whose shape changed while the page was
    /// open. Both the desk and the phone read the plan, so both get the same
    /// sentence.
    ///
    /// It names the setting and where to change it rather than just
    /// refusing: "that does nothing here" with no reason given is how a
    /// feature gets reported as broken.
    /// </summary>
    /// <summary>The title a closed-day refusal carries. A constant, like the club-night one - comparing against a heading as control flow is a bug waiting to be written.</summary>
    public const string ClosedDayTitle = "That day is closed";

    /// <summary>The title every "no race day is open" refusal carries - building the programme, seeding the finals, the standings.</summary>
    public const string NoRaceDayTitle = "No race day";

    /// <summary>Why there are no standings with no race day open. Shared with the phone's standings.</summary>
    public const string NoRaceDayStandings =
        "No race day is open, so there are no standings to show - qualifying counts one race day's heats. "
        + "Open the race day, or start tonight's, to see them.";

    /// <summary>
    /// The title PlanQualifyingBuild gives "no drivers to build from" - so
    /// the set-up wizard can tell that refusal (the Drivers step fixes it)
    /// from the others without matching on the words of the message.
    /// </summary>
    public const string NothingToBuildTitle = "Nothing to build";

    /// <summary>
    /// Why no round can be added to the open day, or null when it can.
    ///
    /// Closing a day is supposed to mean "no new rounds join it" - that is
    /// the sentence on The Day - and nothing enforced it. RaceEventStore
    /// .AddRoundsToActive files rounds against whatever is current without
    /// asking whether it is still open, so a closed day that was current
    /// took new rounds exactly like an open one and the promise was a
    /// caption rather than a rule.
    ///
    /// It matters more now that picking a day in the box on The Day is what
    /// switches to it: making a closed day current used to take a deliberate
    /// press of "Switch to This Day", and it is now one click in a list.
    /// Refused here, at the three places a round is created, rather than in
    /// the store - a store that quietly dropped the round would leave it
    /// belonging to no event at all, which is the fault race days exist to
    /// prevent.
    /// </summary>
    private ProgramAnswer? ClosedDayRefusal()
    {
        if (ActiveDay is not { IsOpen: false } closed)
        {
            return null;
        }

        return new ProgramAnswer(false, ClosedDayTitle,
            $"\"{closed.DisplayName}\" has been closed, so nothing new is added to it - that is what closing a day "
            + "means, and its results and standings keep reading exactly as they do now.\r\n\r\n"
            + "Reopen it on the Setup tab, or start a new day for tonight.");
    }

    private ProgramAnswer? ClubNightRefusal(string what, string missing)
    {
        if (ActiveDay is not { IsClubNight: true } day)
        {
            return null;
        }

        return new ProgramAnswer(false, ClubNightTitle,
            $"\"{day.DisplayName}\" is set up as a club night, so it has no {missing}. "
            + $"Change it to a race day on the Setup tab - under \"What kind of night\" - and {what} becomes "
            + "available again. Nothing already built is lost either way.");
    }

    private void RefuseWithOffer(string title, string message, string offer, Action take,
        MessageBoxIcon icon = MessageBoxIcon.Information)
    {
        if (AppDialog.Show(this, message + "\r\n\r\n" + offer, title, MessageBoxButtons.YesNo, icon) == DialogResult.Yes)
        {
            take();
        }
    }

    private string? Resolve(string? className) =>
        string.IsNullOrWhiteSpace(className) ? TargetClass() : className.Trim();

    private string? TargetClass()
    {
        if (FilterClass.Length > 0)
        {
            return FilterClass;
        }

        if (SelectedRound() is { } round && !string.IsNullOrWhiteSpace(round.ClassName))
        {
            return round.ClassName;
        }

        var racing = RacingClassNames();

        if (racing.Count == 1)
        {
            return racing[0];
        }

        if (racing.Count == 0)
        {
            RefuseWithOffer("Pick a class",
                "There are no classes to work with yet. A class is the rulebook a group of drivers races under, "
                + "and every heat belongs to one.",
                "Set up classes now?",
                () => SetUpClassesRequested?.Invoke());

            return null;
        }

        AppDialog.Show(this,
            "Which class? Pick one in the \"Class\" filter, or select a round from that class first.",
            "Pick a class", MessageBoxButtons.OK, MessageBoxIcon.Information);

        return null;
    }

    /// <summary>
    /// "Heat", "A-Main", "Practice" - what kind of round this is, in the
    /// words the rest of the app uses for it.
    ///
    /// A main is named by its tier, because that is the thing that decides
    /// which main feeds which: the B-main's top finishers move up into the
    /// A. A main with no tier says so plainly rather than being shown as
    /// an A-main it is not - that is the state a hand-added main lands in
    /// when its label is not one the tier can be read from, and the
    /// bump-up refusal that follows is otherwise unexplained.
    /// </summary>
    /// <summary>
    /// The day's own word for a round that is not a final - "heat" on a
    /// race day, "race" on a club night.
    ///
    /// A heat is a heat because it qualifies you for something. On a club
    /// night there is nothing above it, so calling it a heat is just the
    /// app's internal RoundKind leaking onto the screen: the rounds get
    /// built as Qualifier either way, because that is what the store and
    /// the scoring understand, and the person never asked for a qualifier.
    /// </summary>
    /// <summary>
    /// Read from the DAY, not from _clubNight.
    ///
    /// That field is what the footer and the menu were last BUILT for, and
    /// it only changes when this page refreshes. The phone can ask for a
    /// build without this page having refreshed since the shape changed,
    /// and a round named from a stale flag is named wrong forever - the
    /// label is written into the store.
    /// </summary>
    private bool DayIsClubNight => ActiveDay?.IsClubNight == true;

    private string RoundWord => DayIsClubNight ? "race" : "heat";

    /// <summary>Capitalised, for a heading or a column.</summary>
    private string RoundWordCapital => DayIsClubNight ? "Race" : "Heat";

    private string DescribeKind(RoundDefinition round) => round.Kind switch
    {
        RoundKind.Qualifier => RoundWordCapital,
        RoundKind.Practice => "Practice",
        RoundKind.Main => round.MainTier >= 0 ? $"{round.TierLetter}-Main" : "Main (no tier)",
        _ => string.Empty,
    };

    private RoundDefinition? SelectedRound() =>
        _grid.SelectedRows.Count == 1 && _grid.SelectedRows[0].Tag is string id
            ? _rounds.Get(id)
            : null;

    // ------------------------------------------------------------------
    //  Refreshing
    // ------------------------------------------------------------------

    /// <summary>Called when the tab is opened - classes, drivers, check-ins and results all move while it is hidden.</summary>
    public void RefreshAll()
    {
        // First: it decides which buttons the footer has, and the two below
        // both read the same day.
        ApplyDayShape();

        RefreshFilter();
        RefreshGrid();
    }

    private void RefreshFilter()
    {
        var previous = FilterClass.Length > 0 ? FilterClass : _settings.LastRaceProgramClass;

        _cmbFilter.Items.Clear();
        _cmbFilter.Items.Add(AllClasses);

        foreach (var name in RacingClassNames())
        {
            _cmbFilter.Items.Add(name);
        }

        // Any class at all, not just today's, so a filter pinned to a class
        // that is not racing today still resolves instead of silently
        // jumping back to "all".
        foreach (var raceClass in _raceClasses.All)
        {
            if (!_cmbFilter.Items.Contains(raceClass.Name))
            {
                _cmbFilter.Items.Add(raceClass.Name);
            }
        }

        var index = string.IsNullOrEmpty(previous) ? 0 : _cmbFilter.Items.IndexOf(previous);
        _cmbFilter.SelectedIndex = index >= 0 ? index : 0;

        // The longest class may have come or gone: measured again, and the
        // row with it.
        _filterWidth = null;
        _filterRow?.PerformLayout();
    }

    private void RefreshGrid()
    {
        var day = DayRounds();
        var filter = FilterClass;

        var shown = filter.Length == 0
            ? day
            : day.Where(r => string.Equals(r.ClassName, filter, StringComparison.OrdinalIgnoreCase)).ToList();

        // Worked out against the WHOLE day, not the filtered view - the
        // next round is the next round whether or not you are looking at
        // its class.
        var next = day.FirstOrDefault(r => !r.IsRecorded);

        // A note left up would describe a row that is about to go.
        _driversNote?.Hide();
        _grid.Rows.Clear();

        // Due times, by round, from the timetable - and the column is only
        // shown when at least one of today's rounds has one. A DataGridView
        // column's Visible is its own state and not effective visibility, so
        // this is safe to read back, unlike Control.Visible.
        var due = _schedule is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : _schedule.All
                .Where(e => !string.IsNullOrWhiteSpace(e.RoundId) && !string.IsNullOrWhiteSpace(e.ScheduledTime))
                .GroupBy(e => e.RoundId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().ScheduledTime.Trim(), StringComparer.Ordinal);

        // Made with the grid, so always there; asked for by name, which
        // .NET says can come back empty.
        if (_grid.Columns["due"] is { } dueColumn)
        {
            dueColumn.Visible = shown.Any(r => due.ContainsKey(r.Id));
        }

        var position = 1;

        foreach (var round in shown)
        {
            var isNext = next is not null && string.Equals(round.Id, next.Id, StringComparison.Ordinal);

            var index = _grid.Rows.Add(
                position++,
                due.TryGetValue(round.Id, out var at) ? at : string.Empty,
                round.RoundLabel,
                DescribeKind(round),
                round.ClassName,
                round.AssignedTransponders.Count,
                string.IsNullOrWhiteSpace(round.TrackLayoutName) ? "-" : round.TrackLayoutName,
                round.IsRecorded ? "Recorded" : isNext ? "NEXT" : "Scheduled");

            var row = _grid.Rows[index];
            row.Tag = round.Id;
            row.Cells["drivers"].Tag = DriversTip(round);

            // The class cell carries the class's colour, the same one the
            // heat sheet is banded with - so the paper on the pit table and
            // the row on the screen are recognisably the same thing.
            row.Cells["class"].Style.ForeColor = ClassColourFor(round.ClassName);

            if (round.IsRecorded)
            {
                // Done is quiet. It is still there to be looked at, but it
                // is not what the director is scanning the list for.
                row.DefaultCellStyle.ForeColor = AppTheme.TextMuted;
                row.Cells["class"].Style.ForeColor = AppTheme.TextMuted;
            }
            else if (isNext)
            {
                row.DefaultCellStyle.BackColor = AppTheme.PrimaryTint;
                row.DefaultCellStyle.ForeColor = AppTheme.TextDark;
                row.DefaultCellStyle.Font = AppTheme.BoldFont;
            }
        }

        RefreshNextStep(day);
        UpdateSelectionActions();
        NoteProgrammeChanged(day);

        var recorded = day.Count(r => r.IsRecorded);
        StatusMessage?.Invoke(
            $"[program] {Plural.Of(day.Count, "round")} on the program, {recorded} recorded"
            + (filter.Length == 0 ? "." : $"; showing \"{filter}\" ({shown.Count})."));
    }

    /// <summary>The list of drivers shown on hovering a Drivers count. Made in BuildGrid.</summary>
    private HoverNote? _driversNote;

    /// <summary>Shows the drivers in a round when the mouse is over its Drivers count; nothing anywhere else.</summary>
    private void ShowDriversNote(int rowIndex, int columnIndex)
    {
        _driversNote?.Hide();

        if (_driversNote is null
            || rowIndex < 0 || rowIndex >= _grid.Rows.Count
            || columnIndex < 0 || columnIndex >= _grid.Columns.Count
            || _grid.Columns[columnIndex].Name != "drivers"
            || _grid.Rows[rowIndex].Cells[columnIndex].Tag is not string text)
        {
            return;
        }

        var cell = _grid.GetCellDisplayRectangle(columnIndex, rowIndex, false);
        _driversNote.Show(text, new Point(cell.Left + 8, cell.Bottom + 2));
    }

    /// <summary>
    /// Who is in a round, in running order - one to a line, numbered, the
    /// way the heat sheet lists them. Shown on hovering the Drivers count.
    /// </summary>
    private string DriversTip(RoundDefinition round)
    {
        if (round.AssignedTransponders.Count == 0)
        {
            return "Nobody in this round yet";
        }

        return string.Join("\r\n", round.AssignedTransponders.Select((transponder, i) =>
        {
            var name = _profiles.Get(transponder)?.DriverName;

            if (string.IsNullOrWhiteSpace(name))
            {
                // A ghost's transponder is a meaningless id - say what it is.
                name = GhostService.IsGhost(transponder) ? "Ghost" : transponder;
            }

            return $"{i + 1}. {name}";
        }));
    }

    /// <summary>The programme as it was the last time anybody was told it changed - ids, in order.</summary>
    private string _programmeStamp = string.Empty;

    /// <summary>
    /// Tells anything following the programme that it moved.
    ///
    /// Hooked to the redraw rather than to each of the nine commands that
    /// changes the programme, because every one of them redraws and a
    /// tenth would be added one day without anybody remembering this. The
    /// stamp keeps it honest: an ordinary refresh - opening the tab,
    /// changing the class filter - produces the same ids in the same order
    /// and raises nothing.
    /// </summary>
    private void NoteProgrammeChanged(List<RoundDefinition> day)
    {
        var stamp = string.Join("|", day.Select(r => r.Id));

        if (string.Equals(stamp, _programmeStamp, StringComparison.Ordinal))
        {
            return;
        }

        _programmeStamp = stamp;
        ProgrammeChanged?.Invoke();
    }

    /// <summary>
    /// Works out the next step for the day on show - see _step. Next Steps'
    /// card asks for it fresh (NextStep); this is the one the page runs
    /// when it is handed none.
    /// </summary>
    private void RefreshNextStep(List<RoundDefinition> day)
    {
        _step = ProgramNextStep.Evaluate(
            _events?.Active,
            day,
            RacingClasses(),
            PlannedQualifyingRounds(),
            _sessionHistory,
            SetupFacts());
    }

    private void UpdateSelectionActions()
    {
        var single = _grid.SelectedRows.Count == 1;

        _btnStart.Enabled = single;
        _btnEditDrivers.Enabled = single;

        // More is never disabled: two of its items - adding a main by hand
        // and saving the heat sheet - are about the class, not about a
        // selected round, and they are exactly what is wanted when the
        // programme is still empty.
    }

    // ------------------------------------------------------------------
    //  The next step's one command
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs the next step - Next Steps' card's button, by way of RunStep.
    ///
    /// A switch over the stage rather than a stored delegate, so the
    /// command and the sentence that describes it cannot drift apart: both
    /// are read from the same ProgramStep.
    /// </summary>
    private void RunPrimaryAction()
    {
        switch (_step.Stage)
        {
            case ProgramStage.NoEvent:
            case ProgramStage.DayClosed:
                StartRaceDayRequested?.Invoke();
                break;

            case ProgramStage.NoClasses:
                GoToEventTab?.Invoke();
                break;

            case ProgramStage.NoDrivers:
                ManageDriversRequested?.Invoke();
                break;

            case ProgramStage.NoClassesOnFile:
                SetUpClassesRequested?.Invoke();
                break;

            case ProgramStage.NobodyCheckedIn:
                GoToEntriesTab?.Invoke();
                break;

            case ProgramStage.BuildQualifying:
                BuildQualifyingProgramme();
                break;

            case ProgramStage.BumpUps:
                ApplyBumpUps(_step.ClassName);
                break;

            case ProgramStage.RunNext:
                if (_step.Round is { } round)
                {
                    StartRound(round);
                }

                break;

            case ProgramStage.SeedMains:
                SeedMains(_step.ClassName);
                break;

            case ProgramStage.Complete:
                ShowStandings(_step.ClassName);
                break;
        }
    }

    // ------------------------------------------------------------------
    //  Building the day
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the whole qualifying programme - every racing class, every
    /// planned round - in one press.
    ///
    /// This is the command that replaced "Generate Heats" and "Generate All
    /// Rounds". Those two differed only in whether they read the round
    /// count off the race day, which is not a difference worth a second
    /// button, and neither of them could build more than one class.
    ///
    /// Rounds are created round-major - Q1 for every class, then Q2 for
    /// every class - because the store hands out its running order in the
    /// sequence things are added, and that IS the order a club runs a day
    /// in. The rotation within each round is the cheap, honest seeding for
    /// races that happen before any times exist to seed from: shift the
    /// grid by one heat's worth each round so the same handful of drivers
    /// is not first out on a green track every single time.
    /// </summary>
    private void BuildQualifyingProgramme()
    {
        if (ActiveDay is null)
        {
            // Start one, rather than walking them to the page that has the
            // button. There is nothing to decide here - a programme needs a
            // day and there isn't one - so landing somebody on The Day and
            // leaving them to find "New Race Day" is a step that exists only
            // because the code was easier to write that way. The same event
            // the "Start a Race Day" card uses, so both open the same dialog.
            StartRaceDayRequested?.Invoke();
            return;
        }

        var plan = PlanQualifyingBuild();

        if (!plan.Ok)
        {
            AppDialog.Show(this, plan.Message, plan.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (AppDialog.Show(this, plan.Message, plan.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        BuildQualifying(string.Empty);
    }

    /// <summary>
    /// What building the qualifying programme would do, as the question
    /// the Build button asks - or, when Ok is false, why it cannot be done.
    /// Changes nothing. Shared by the banner and the phone (RaceDayService),
    /// which shows the same text before its second, confirming call.
    /// </summary>
    public ProgramAnswer PlanQualifyingBuild()
    {
        if (ActiveDay is null)
        {
            return new ProgramAnswer(false, NoRaceDayTitle,
                "No race day is open. Open or start one first - the program is built for a race day.");
        }

        if (ClosedDayRefusal() is { } shut)
        {
            return shut;
        }

        var classes = RacingClasses().Where(c => c.RosterCount > 0).ToList();

        if (classes.Count == 0)
        {
            // Said as it is. With check-in required, a class full of drivers
            // on file still builds nothing until they are ticked in - and
            // "has no drivers" sent people looking for drivers they had.
            return new ProgramAnswer(false, NothingToBuildTitle, ActiveDay?.CheckedInDriversOnly == true
                ? "Nobody in tonight's classes has checked in yet - races are built from checked-in drivers only."
                : "None of tonight's classes has any drivers in it yet.");
        }

        var roundCount = PlannedQualifyingRounds();
        var heatSize = HeatSize();
        var track = DayTrack();

        // The rounds still to build for each class: the planned ones that
        // have no recorded heat today. See QualifyingRoundsAlreadyRun - a
        // rebuild used to make every round again, so a late check-in fitted
        // in after Q1 put a second Q1 on the program beside the recorded one.
        var plan = classes
            .Select(c =>
            {
                var run = QualifyingRoundsAlreadyRun(DayRoundsForClass(c.Name));
                var toBuild = Enumerable.Range(1, roundCount).Count(n => !run.Contains(n));
                return (c.Name, c.RosterCount, Heats: (int)Math.Ceiling(c.RosterCount / (double)heatSize), ToBuild: toBuild, Run: run);
            })
            .ToList();

        var total = plan.Sum(p => p.Heats * p.ToBuild);

        if (total == 0)
        {
            // Every planned round has been run in every class. Building
            // again could only repeat a round that already counts, so it
            // is refused, and the director is pointed at the tool that does
            // fit a late driver into what is left.
            return new ProgramAnswer(false, QualifyingAlreadyRunTitle,
                $"Every {(DayIsClubNight ? "race" : "qualifying round")} planned for today has already been run, so there is nothing left to build. "
                + "To fit a driver into a heat that has not been run yet, use Move Drivers Between Heats.");
        }

        // "Stock: 3 x 2 heats (14 drivers)" on a race day. On a club night a
        // single group is just "Stock: 3 races (3 drivers)" - "3 x 1 race"
        // is arithmetic where a sentence will do. A round already run is
        // named on the end, so the count is the count of what will really
        // be made and the director can see why it is short.
        var summary = string.Join("\r\n", plan.Select(p =>
            (p.ToBuild == 0
                ? $"   {p.Name}: nothing to build"
                : DayIsClubNight && p.Heats == 1
                    ? $"   {p.Name}: {Plural.Of(p.ToBuild, "race")}  ({Plural.Of(p.RosterCount, "driver")})"
                    : $"   {p.Name}: {p.ToBuild} x {Plural.Of(p.Heats, RoundWord)}  ({Plural.Of(p.RosterCount, "driver")})")
            + RunRoundsNote(p.Run, roundCount)));

        // Today's rounds only - see BuildQualifying, which works from the
        // same set so the count and the delete can never disagree.
        var replaceable = classes
            .SelectMany(c => ReplaceableQualifiers(DayRoundsForClass(c.Name)))
            .ToList();

        // The heat out on track is unrecorded, so the build would delete it
        // from under the race and its result would be saved against a round
        // that no longer exists. The phone refuses while a race is live;
        // the desk has to refuse too.
        if (OnTrackRound(replaceable) is { } busy)
        {
            return new ProgramAnswer(false, $"A {RoundWord} is on track",
                $"\"{busy.RoundLabel}\" ({busy.ClassName}) is on track now, and building the program would replace it. "
                + "Build again once it has been recorded.");
        }

        var replacing = replaceable.Count;

        var note = replacing > 0
            ? $"\r\n\r\n{Plural.Of(replacing, DayIsClubNight ? "race" : "qualifier")} that {(replacing == 1 ? "has" : "have")} not been run will be replaced. Recorded rounds are untouched."
            : string.Empty;

        // Said outright when some rounds are skipped: "Recorded rounds are
        // untouched" alone read as if the whole set would be built again
        // around them, which is exactly what used to happen.
        if (plan.Any(p => RunRoundsNote(p.Run, roundCount).Length > 0))
        {
            note += "\r\n\r\nRounds that already have a recorded heat are not built again, and any of their heats still to run stay as they are.";
        }

        // Titled with the same words as the button that opened it: a
        // confirmation whose heading renames the thing you just pressed reads
        // as a different command.
        return new ProgramAnswer(true, DayIsClubNight ? ProgramCaptions.BuildRaces : ProgramCaptions.BuildQualifying,
            $"Build {Plural.Of(total, RoundWord)} for {Plural.Of(classes.Count, "class", "classes")}?\r\n\r\n{summary}\r\n\r\n"
            + $"{Plural.Of(heatSize, "driver")} per {RoundWord}"
            + (track.Length > 0 ? $", on {track}" : string.Empty)
            + (ActiveDay?.CheckedInDriversOnly == true ? ", checked-in drivers only" : string.Empty)
            + "."
            + note);
    }

    /// <summary>
    /// Builds the qualifying programme - no questions asked; the caller has
    /// already asked (PlanQualifyingBuild). Worked out afresh here rather
    /// than carried over from the plan, so a change made while the question
    /// was open (a phone, another tab) is built from, not built over.
    /// Captures one undo entry, labelled with <paramref name="undoPrefix"/>
    /// in front ("Phone: " from the phone, empty from the desk). Ok false,
    /// and nothing changed, when there turns out to be nothing to build.
    /// </summary>
    public ProgramAnswer BuildQualifying(string undoPrefix)
    {
        if (ActiveDay is null)
        {
            return PlanQualifyingBuild();
        }

        // Closed while the question was open - from the other page, or from
        // a phone. Re-asked here for the same reason the on-track check
        // below is: the plan was true when it was shown and the day is what
        // it is now.
        if (ClosedDayRefusal() is { } shut)
        {
            return shut;
        }

        var classes = RacingClasses().Where(c => c.RosterCount > 0).ToList();

        if (classes.Count == 0)
        {
            return PlanQualifyingBuild();
        }

        var roundCount = PlannedQualifyingRounds();
        var heatSize = HeatSize();
        var track = DayTrack();

        // Which rounds each class has already run today, worked out before
        // anything is deleted. Those rounds are not built again, and their
        // heats still to run are not replaced either: rebuilding "Q1 Heat 2"
        // after "Q1 Heat 1" was recorded would mean building Q1 again, which
        // is the duplicate this is here to prevent.
        var runByClass = classes.ToDictionary(
            c => c.Name,
            c => QualifyingRoundsAlreadyRun(DayRoundsForClass(c.Name)),
            StringComparer.OrdinalIgnoreCase);

        // Every planned round already run everywhere: nothing to build, and
        // nothing is touched. Refused with the same words the plan gives.
        if (classes.All(c => Enumerable.Range(1, roundCount).All(runByClass[c.Name].Contains)))
        {
            return PlanQualifyingBuild();
        }

        var replaced = classes
            .SelectMany(c => ReplaceableQualifiers(DayRoundsForClass(c.Name)))
            .ToList();

        // Started while the question was open: refused, for the reason
        // PlanQualifyingBuild gives.
        if (OnTrackRound(replaced) is not null)
        {
            return PlanQualifyingBuild();
        }

        // Today's rounds only. This used to read every round each class
        // has ever had, so an unrun heat left over from an earlier race day
        // was counted here and then deleted below - the build reached back
        // into another day's programme. The count, the delete and the undo
        // all work from this one set so they can never disagree. It is now
        // narrowed further to exactly the heats being replaced, so the unrun
        // heats of a round already under way stay on the program too.
        var replacedIds = replaced.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var owners = replaced.ToDictionary(
            r => r.Id,
            r => _events?.EventForRound(r.Id)?.Id,
            StringComparer.Ordinal);

        var created = new List<RoundDefinition>();

        // Captured before anything is touched, and closing over the lists
        // rather than over copies of them - `created` is filled in below
        // and the closure sees the filled version.
        _undo.Capture(undoPrefix + ProgramCaptions.BuildQualifying, () =>
        {
            var kept = RemoveUnrunRounds(created);

            RestoreRounds(replaced, owners);

            RefreshAll();
            ReportKeptRounds(kept, "heat", replaced.Count > 0
                    ? "The heats the build replaced are back as well - check the program for any you no longer need."
                    : string.Empty);
        });

        foreach (var raceClass in classes)
        {
            _rounds.ClearUnrecordedQualifiers(raceClass.Name, replacedIds);
        }

        // The deleted heats come off the race day as well, the way the
        // finals build and every other delete here does it. Clearing them
        // from the round store alone left their ids on the day for good, so
        // each rebuild added another set to the day's round count - six heats
        // rebuilt once read as twelve on Setup, the Championship Calendar and
        // the phone's Today summary. `replaced` is exactly what the clear
        // above removed (today's unrecorded qualifiers of these classes), and
        // the undo puts them back on their day through `owners`.
        foreach (var gone in replaced)
        {
            _events?.RemoveRound(gone.Id);
        }

        for (var round = 0; round < roundCount; round++)
        {
            foreach (var raceClass in classes)
            {
                // A round this class has already run is left alone - see
                // runByClass above.
                if (runByClass[raceClass.Name].Contains(round + 1))
                {
                    continue;
                }

                var roster = RosterFor(raceClass.Name);

                if (roster.Count == 0)
                {
                    continue;
                }

                var heats = DealQualifyingHeats(roster.Select(d => d.Transponder).ToList(), heatSize, round);

                for (var heat = 0; heat < heats.Count; heat++)
                {
                    // What the round is CALLED, and it has to follow the kind
                    // of night.
                    //
                    // On a race day: "Heat 3" on a single-round night, "Q2
                    // Heat 3" when there is more than one round to tell
                    // apart. Q is for qualifying, and the heat is a group of
                    // the field.
                    //
                    // On a club night there is nothing to qualify for, so a
                    // round called "Q1 Heat 1" is naming a thing that is not
                    // happening - which is exactly what it looked like. It
                    // is "Race 1". The word heat comes back only when the
                    // field is too big for one go and really is split into
                    // groups: "Race 1 Heat 2". That sense of the word is
                    // true at any track, club night or not.
                    var label = DayIsClubNight
                        ? heats.Count == 1
                            ? $"Race {round + 1}"
                            : $"Race {round + 1} Heat {heat + 1}"
                        : roundCount == 1
                            ? $"Heat {heat + 1}"
                            : $"Q{round + 1} Heat {heat + 1}";

                    var definition = new RoundDefinition
                    {
                        ClassName = raceClass.Name,
                        RoundLabel = label,
                        Kind = RoundKind.Qualifier,
                        TrackLayoutName = track,
                        AssignedTransponders = heats[heat],
                    };

                    _rounds.Add(definition);
                    created.Add(definition);
                }
            }
        }

        _events?.AddRoundsToActive(created.Select(r => r.Id));

        var done = $"Built {Plural.Of(created.Count, RoundWord)} across {Plural.Of(classes.Count, "class", "classes")}.";
        _lblStatus.Text = done;
        RefreshAll();

        return new ProgramAnswer(true, DayIsClubNight ? "Tonight's races built" : "Tonight's heats built", done);
    }

    /// <summary>
    /// The title PlanQualifyingBuild gives when every planned qualifying
    /// round has already been run - kept apart from NothingToBuildTitle so
    /// the set-up wizard does not send anyone to the Drivers step for it.
    /// </summary>
    public const string QualifyingAlreadyRunTitle = "Qualifying already run";

    /// <summary>
    /// The qualifying round (1, 2, 3 ...) a heat belongs to, read from its
    /// label the same way Move Drivers Between Heats reads it ("Q2 Heat 1",
    /// "Race 2 Heat 1", "Round 2"), or null for anything that is not a
    /// qualifier or belongs to no particular round.
    ///
    /// An unnumbered qualifier ("Heat 3") is round 1 when every qualifier
    /// the class has today is unnumbered - that is what a one-round night
    /// builds. Beside numbered rounds it is a heat added by hand and counts
    /// towards none of them (HeatBalanceForm.RoundKeyFor says the same).
    /// </summary>
    public static int? QualifyingRoundNumber(RoundDefinition round, IReadOnlyList<RoundDefinition> classDayRounds)
    {
        var key = HeatBalanceForm.RoundKeyFor(round, classDayRounds);

        if (key is null)
        {
            return null;
        }

        if (key.Length == 0)
        {
            return 1;
        }

        return int.TryParse(key.AsSpan(1), out var number) ? number : null;
    }

    /// <summary>
    /// The qualifying rounds a class has already run today: every round
    /// with at least one recorded heat. <paramref name="classDayRounds"/>
    /// is the class's rounds on today's program.
    ///
    /// Building qualifying used to delete the unrun heats and then build
    /// every planned round again from Q1, so pressing Build after Q1 was
    /// recorded - to fit in a late check-in, say - put a second Q1 on the
    /// program. Each of those heats has its own round, so a driver who ran
    /// both was scored for one heat more than the night planned, and under
    /// best-N scoring could drop one more bad result than everyone else.
    /// Rounds in this set are not built again.
    /// </summary>
    public static HashSet<int> QualifyingRoundsAlreadyRun(IReadOnlyList<RoundDefinition> classDayRounds) =>
        classDayRounds
            .Where(r => r.Kind == RoundKind.Qualifier && r.IsRecorded)
            .Select(r => BuildRoundOf(r, classDayRounds))
            .OfType<int>()
            .ToHashSet();

    /// <summary>
    /// The round a build counts a qualifier towards - QualifyingRoundNumber,
    /// with one addition. A night that started with one qualifying round
    /// built unnumbered heats ("Heat 1"); when the round count is then raised
    /// and Build pressed, numbered Q2 and Q3 heats land beside them, and
    /// from then on QualifyingRoundNumber counts the unnumbered ones as
    /// hand-added heats belonging to no round. The next Build then saw no
    /// round run, deleted the unrun "Heat 2" and built Q1 again beside the
    /// recorded "Heat 1" - a second Q1. So while the class has no numbered
    /// first round ("Q1" or "Race 1"), an unnumbered qualifier is still
    /// round 1 for the build.
    /// </summary>
    private static int? BuildRoundOf(RoundDefinition round, IReadOnlyList<RoundDefinition> classDayRounds)
    {
        if (QualifyingRoundNumber(round, classDayRounds) is { } number)
        {
            return number;
        }

        if (round.Kind != RoundKind.Qualifier || !IsUnnumberedQualifier(round))
        {
            return null;
        }

        var hasNumberedFirstRound = classDayRounds.Any(r =>
            r.Kind == RoundKind.Qualifier
            && !IsUnnumberedQualifier(r)
            && QualifyingRoundNumber(r, classDayRounds) == 1);

        return hasNumberedFirstRound ? null : 1;
    }

    /// <summary>True for a qualifier whose label names no round ("Heat 3") - what a one-round build makes.</summary>
    private static bool IsUnnumberedQualifier(RoundDefinition round) =>
        HeatBalanceForm.RoundKeyFor(round, new[] { round }) is { Length: 0 };

    /// <summary>
    /// The heats a qualifying build replaces for one class: its unrun
    /// qualifiers today, except those in a round that already has a
    /// recorded heat. Those stay - their round is not built again (see
    /// QualifyingRoundsAlreadyRun), so deleting them would take the round
    /// away from the drivers who have not raced it yet.
    /// </summary>
    public static List<RoundDefinition> ReplaceableQualifiers(IReadOnlyList<RoundDefinition> classDayRounds)
    {
        var run = QualifyingRoundsAlreadyRun(classDayRounds);

        return classDayRounds
            .Where(r => r.Kind == RoundKind.Qualifier && !r.IsRecorded)
            .Where(r => BuildRoundOf(r, classDayRounds) is not { } number || !run.Contains(number))
            .ToList();
    }

    /// <summary>
    /// " - Q1 already run" for the build question's summary line, naming
    /// the planned rounds a class will skip; empty when it skips none.
    /// </summary>
    private string RunRoundsNote(IReadOnlySet<int> run, int roundCount)
    {
        var skipped = run.Where(n => n >= 1 && n <= roundCount).OrderBy(n => n).ToList();

        if (skipped.Count == 0)
        {
            return string.Empty;
        }

        var names = string.Join(", ", skipped.Select(n => roundCount == 1
            ? "the heats"
            : DayIsClubNight ? $"Race {n}" : $"Q{n}"));

        return $"  - {names} already run";
    }

    /// <summary>
    /// Splits one class's roster into the heats of one qualifying round,
    /// as transponders in grid order. Pure, so the tests can hold it to
    /// what it promises without a race day.
    ///
    /// The deal is round-robin across the heats, which keeps them level -
    /// never more than one driver apart - and each round starts one heat's
    /// worth further round the roster, so the grid order changes too.
    ///
    /// That rotation on its own is not enough, though. When the number of
    /// heats divides the class size - every even class split into two
    /// heats, which is the commonest night there is - a round-robin deal
    /// puts a driver in heat (place - shift) mod heats, and the groups
    /// are just the roster's places taken every other one (or every
    /// third, and so on). Rotating can only change which of those same
    /// groups is called Heat 1, and with an even shift it does not even
    /// do that. So a class of 16 at 8 a heat ran the same eight drivers
    /// together, first out on a green track, in Q1, Q2 and Q3.
    ///
    /// So in that case, from Q2 on, each row of the deal (one driver per
    /// heat) is also moved along by one heat every `round` rows: Q2 moves
    /// every row, Q3 every second row, Q4 every third. Each row still
    /// gives every heat exactly one driver, so the heats stay level, and
    /// every round's groups differ from every other round's as long as
    /// the class has more rows than the round number - a class of 4 at 2
    /// a heat only has two rows, so its Q3 matches Q1 again, which is the
    /// best two rows can do. The other case - heats that do not divide
    /// the class - is left exactly as it was, because there the rotation
    /// already moves drivers between heats.
    /// </summary>
    internal static List<List<string>> DealQualifyingHeats(IReadOnlyList<string> roster, int heatSize, int round)
    {
        var heatCount = roster.Count == 0
            ? 1
            : Math.Max(1, (int)Math.Ceiling(roster.Count / (double)Math.Max(1, heatSize)));

        var heats = Enumerable.Range(0, heatCount).Select(_ => new List<string>()).ToList();

        if (roster.Count == 0)
        {
            return heats;
        }

        var shift = round * heatSize % roster.Count;

        var rotated = roster.Skip(shift).Concat(roster.Take(shift)).ToList();

        var regroup = heatCount > 1 && round > 0 && roster.Count % heatCount == 0;

        for (var i = 0; i < rotated.Count; i++)
        {
            var heat = i % heatCount;

            if (regroup)
            {
                var row = i / heatCount;
                heat = (heat + row / round) % heatCount;
            }

            heats[heat].Add(rotated[i]);
        }

        return heats;
    }

    /// <summary>The one of these rounds that is on track right now, or null.</summary>
    private RoundDefinition? OnTrackRound(IEnumerable<RoundDefinition> rounds)
    {
        var onTrackId = RoundOnTrackId?.Invoke();

        return onTrackId is null
            ? null
            : rounds.FirstOrDefault(r => string.Equals(r.Id, onTrackId, StringComparison.Ordinal));
    }

    /// <summary>
    /// The undo half of a build or a seeding: takes out the rounds it made,
    /// EXCEPT any that have been run since. Returns those, which are left
    /// exactly where they are, still on their race day.
    ///
    /// Undo used to delete every round it had made, run or not - so undoing
    /// a build after the first heat had gone out threw away that heat, and
    /// with it the only link between its result and the programme. A
    /// recorded round is history; no undo gets to remove it.
    ///
    /// Read from the store rather than from the list: the list holds the
    /// rounds as they were made, and a round is marked as run on the copy
    /// in the store.
    /// </summary>
    private List<RoundDefinition> RemoveUnrunRounds(IEnumerable<RoundDefinition> made)
    {
        var kept = new List<RoundDefinition>();

        // The heat out on track now is kept too: it is about to be run, and
        // deleting it mid-race left its result nothing to be recorded
        // against. Read once - the undo does not start or stop a race.
        var onTrackId = RoundOnTrackId?.Invoke();

        foreach (var round in made)
        {
            if (_rounds.Get(round.Id) is { } stored
                && (stored.IsRecorded || string.Equals(stored.Id, onTrackId, StringComparison.Ordinal)))
            {
                kept.Add(stored);
                continue;
            }

            _rounds.Delete(round.Id);
            _events?.RemoveRound(round.Id);
        }

        return kept;
    }

    /// <summary>
    /// Puts rounds back in their old places in the running order, and back
    /// on the race day each one belonged to. Add would have put them at the
    /// end of the day - see RoundDefinitionStore.Restore.
    /// </summary>
    private void RestoreRounds(IReadOnlyList<RoundDefinition> rounds, IReadOnlyDictionary<string, string?> owners)
    {
        _rounds.Restore(rounds);

        foreach (var round in rounds)
        {
            if (owners.TryGetValue(round.Id, out var ownerId) && ownerId is not null)
            {
                _events?.AttachRound(ownerId, round.Id);
            }
        }
    }

    /// <summary>
    /// Tells the director an undo left run rounds in place. Said out loud
    /// rather than only logged, because the programme they are looking at
    /// is not the one they asked to go back to. Owned by nothing, since an
    /// undo can run while a different page is on screen.
    /// </summary>
    private void ReportKeptRounds(IReadOnlyList<RoundDefinition> kept, string what, string afterwards)
    {
        if (kept.Count == 0)
        {
            return;
        }

        var names = string.Join("\r\n", kept.Select(r => $"   {r.RoundLabel}  ({r.ClassName})"));

        _lblStatus.Text = $"Undo kept {Plural.Of(kept.Count, what)} that {(kept.Count == 1 ? "has" : "have")} already been run.";

        AppDialog.Show(
            $"{Plural.Of(kept.Count, what)} {(kept.Count == 1 ? "has" : "have")} been run since (or {(kept.Count == 1 ? "is" : "are")} on track now), "
            + $"so {(kept.Count == 1 ? "it was" : "they were")} kept, "
            + $"with {(kept.Count == 1 ? "its result" : "their results")}:\r\n\r\n{names}\r\n\r\n"
            + $"Everything else was undone. {afterwards}",
            "Some rounds were kept", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Seeds tiered mains straight from the qualifying order, lowest tier
    /// first so bump-ups are possible.
    /// </summary>
    private void SeedMains(string className)
    {
        if (Resolve(className) is not { } target)
        {
            return;
        }

        className = target;

        var plan = PlanSeedMains(className);

        if (!plan.Ok)
        {
            var icon = plan.Title == MainsAlreadyRunTitle ? MessageBoxIcon.Warning : MessageBoxIcon.Information;

            if (plan.Offer is { } offer)
            {
                RefuseWithOffer(plan.Title, plan.Message, offer, () => TakeSeedOffer(className, plan.Title), icon);
                return;
            }

            AppDialog.Show(this, plan.Message, plan.Title, MessageBoxButtons.OK, icon);
            return;
        }

        if (AppDialog.Show(this, plan.Message, plan.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        SeedMainsFor(className, string.Empty);
    }

    /// <summary>
    /// What seeding a class's mains would do, as the question the desk asks
    /// - or, when Ok is false, why it cannot be done yet. Changes nothing.
    /// Shared with the phone (RaceDayService).
    /// </summary>
    public ProgramAnswer PlanSeedMains(string className)
    {
        // First, so the answer is the real reason. With no race day open
        // qualifying counts nothing (QualifyingRoundFilter), and "no
        // qualifying results yet" would send somebody off to run heats they
        // have already run.
        if (ActiveDay is null)
        {
            return new ProgramAnswer(false, NoRaceDayTitle,
                "No race day is open, so there is no qualifying to build the finals from - they are seeded from "
                + "one race day's heats. Open the race day, or start tonight's, first.");
        }

        if (ClosedDayRefusal() is { } shut)
        {
            return shut;
        }

        if (ClubNightRefusal("building the finals", "finals") is { } notTonight)
        {
            return notTonight;
        }

        if (string.IsNullOrWhiteSpace(className))
        {
            return new ProgramAnswer(false, "Pick a class", "Which class? Name the class whose finals to build.");
        }

        var raceClass = ClassByName(className);

        var ranked = QualifyingCalculator
            .Compute(className, _rounds, _sessionHistory, _profiles, raceClass, QualifyingRoundFilter())
            .Select(e => e.Transponder)
            .ToList();

        if (ranked.Count == 0)
        {
            return new ProgramAnswer(false, NothingToSeedFromTitle,
                "No qualifying results for this class yet - the finals are built from the qualifying order, "
                + "so there has to be one. Run and record at least one heat first.",
                Offer: "Add a main by hand instead?");
        }

        var mainSize = Math.Max(2, raceClass?.MainSize ?? 8);
        var bumpUp = Math.Max(0, raceClass?.BumpUpCount ?? 0);
        var plan = MainsBuilder.PlanSeeding(ranked, mainSize);

        // Today's mains only. A main from an earlier race day is finished
        // business: it must neither block tonight's seeding as "already
        // run" nor be deleted as one of the mains being replaced.
        var existing = DayRoundsForClass(className).Where(r => r.Kind == RoundKind.Main).ToList();
        var alreadyRun = existing.Where(r => r.IsRecorded).ToList();

        if (alreadyRun.Count > 0)
        {
            return new ProgramAnswer(false, MainsAlreadyRunTitle,
                $"This class already has {Plural.Of(alreadyRun.Count, "main")} that {(alreadyRun.Count == 1 ? "has" : "have")} been run, "
                + "and building the finals again would replace them - which would leave their results filed against "
                + "rounds that no longer exist. Take the ones you do not want off the program first, and their "
                + "results go with them knowingly rather than by accident.",
                Offer: "Show this class's program so you can pick them out?");
        }

        // An unrun main that is out on track now would be deleted by the
        // re-seed from under the race - see PlanQualifyingBuild.
        if (OnTrackRound(existing) is { } busy)
        {
            return new ProgramAnswer(false, "A main is on track",
                $"\"{busy.RoundLabel}\" is on track now, and building the finals again would replace it. "
                + "Try again once it has been recorded.");
        }

        var summary = string.Join("\r\n", Enumerable.Range(0, plan.TierCount)
            .Select(t => $"   {MainsBuilder.SeedPlan.LetterFor(t)}-Main: {Plural.Of(plan.Tiers[t].Count, "driver")}"));

        var bumpNote = bumpUp > 0
            ? $"\r\n\r\nTop {bumpUp} from each main will be able to bump up into the tier above."
            : "\r\n\r\nBump-ups are disabled for this class.";

        return new ProgramAnswer(true, ProgramCaptions.SeedMains,
            $"Put {Plural.Of(ranked.Count, "qualified driver")} in \"{className}\" into {Plural.Of(plan.TierCount, "main")}, fastest first?"
            + $"\r\n\r\n{summary}{bumpNote}"
            + "\r\n\r\nLower mains are scheduled first so bump-ups can happen before the tier above runs."
            + (existing.Count > 0 ? $"\r\n\r\n{Plural.Of(existing.Count, "existing unrun main")} will be replaced." : string.Empty));
    }

    /// <summary>
    /// Seeds a class's mains straight from the qualifying order, lowest tier
    /// first so bump-ups are possible - no questions asked; the caller has
    /// asked (PlanSeedMains). Re-checked here, so a qualifier run or a main
    /// recorded while the question was open is not seeded over. One undo
    /// entry, labelled with <paramref name="undoPrefix"/> in front.
    /// </summary>
    public ProgramAnswer SeedMainsFor(string className, string undoPrefix)
    {
        var check = PlanSeedMains(className);

        if (!check.Ok)
        {
            return check;
        }

        var raceClass = ClassByName(className);

        var ranked = QualifyingCalculator
            .Compute(className, _rounds, _sessionHistory, _profiles, raceClass, QualifyingRoundFilter())
            .Select(e => e.Transponder)
            .ToList();

        var mainSize = Math.Max(2, raceClass?.MainSize ?? 8);
        var bumpUp = Math.Max(0, raceClass?.BumpUpCount ?? 0);
        var plan = MainsBuilder.PlanSeeding(ranked, mainSize);

        var existing = DayRoundsForClass(className).Where(r => r.Kind == RoundKind.Main).ToList();

        var replaced = existing.ToList();
        var created = new List<RoundDefinition>();

        // Which race day each replaced main belonged to, so the undo can
        // put it back there. Without this an undone main came back with no
        // race day, and - now that seeding only looks at today's rounds -
        // it would drop off the programme and out of the next seeding.
        var owners = replaced.ToDictionary(
            r => r.Id,
            r => _events?.EventForRound(r.Id)?.Id,
            StringComparer.Ordinal);

        _undo.Capture($"{undoPrefix}Build the finals for {className}", () =>
        {
            var kept = RemoveUnrunRounds(created);

            RestoreRounds(replaced, owners);

            RefreshAll();
            ReportKeptRounds(kept, "main", replaced.Count > 0
                    ? "The mains the build replaced are back as well - check the program for any you no longer need."
                    : string.Empty);
        });

        foreach (var round in existing)
        {
            _rounds.Delete(round.Id);
            _events?.RemoveRound(round.Id);
        }

        created.AddRange(MainsBuilder.BuildRounds(className, plan, DayTrack(), bumpUp));

        foreach (var round in created)
        {
            _rounds.Add(round);
        }

        _events?.AddRoundsToActive(created.Select(r => r.Id));

        var done = $"Built {Plural.Of(created.Count, "main")} for {className}.";
        _lblStatus.Text = done;
        RefreshAll();

        return new ProgramAnswer(true, "Finals built", done);
    }

    private void AddMainByHand()
    {
        if (ClosedDayRefusal() is { } shut)
        {
            AppDialog.Show(this, shut.Message, shut.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Said as well as hidden. The menu item is kept off a club night by
        // _clubNight, which is read when the page is laid out - so a day
        // switched to a club night while this page was up still has the
        // item on it. Everything else that builds a final answers for
        // itself rather than trusting the menu; this does too.
        if (ClubNightRefusal("adding a final by hand", "finals") is { } notTonight)
        {
            AppDialog.Show(this, notTonight.Message, notTonight.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (TargetClass() is not { } className)
        {
            return;
        }

        var roster = RosterFor(className);

        if (roster.Count == 0)
        {
            AppDialog.Show(this, $"\"{className}\" has no drivers yet.",
                "No drivers in class", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var ranked = QualifyingCalculator
            .Compute(className, _rounds, _sessionHistory, _profiles, ClassByName(className), QualifyingRoundFilter())
            .Select(e => e.Transponder)
            .ToList();

        // Counted over today's programme, so the first main added by hand
        // tonight is offered as "A-Main" even if the class ran mains last week.
        var existingMains = DayRoundsForClass(className).Count(r => r.Kind == RoundKind.Main);

        var defaultLabel = existingMains == 0 ? "A-Main"
            : existingMains == 1 ? "B-Main"
            : $"Main {existingMains + 1}";

        using var form = new MainRoundForm(className, roster, ranked, _trackLayouts, defaultLabel);

        if (form.ShowDialog(this) == DialogResult.OK && form.Result is not null)
        {
            var added = form.Result;

            // A tiered main ("A-Main", "B-Main") takes the class's bump-up
            // count, the way a main built from qualifying does (see
            // MainsBuilder.BuildRounds). The form left it at 0, so a main
            // added by hand - which is what the "No tiered mains" answer in
            // PlanBumpUps tells the director to do - was refused every
            // bump-up as "not set up to receive" them, and nothing on screen
            // could change that. It is set on every tier, the lowest
            // included: which main ends up lowest is not known until the
            // director has finished adding them, and on the lowest one the
            // count is never read, because the blocker check stops first at
            // "no main below this one".
            if (added.MainTier >= 0)
            {
                added.BumpUpCount = Math.Max(0, ClassByName(className)?.BumpUpCount ?? 0);
            }

            // An undo entry like every other change to the program. Without
            // one, Undo straight after adding a main reversed whatever came
            // before it - seeding the finals, say - and left the main that
            // was added by mistake in place. Taken out the same way a build
            // is undone, so a main that has been run (or is on track) since
            // stays, with its result.
            _undo.Capture($"Add {added.RoundLabel} ({className})", () =>
            {
                var kept = RemoveUnrunRounds(new[] { added });

                RefreshAll();
                ReportKeptRounds(kept, "main", string.Empty);
            });

            _rounds.Add(added);
            _events?.AddRoundsToActive(new[] { added.Id });
            RefreshAll();
        }
    }

    // ------------------------------------------------------------------
    //  Acting on a round
    // ------------------------------------------------------------------

    private void StartSelectedRound()
    {
        if (SelectedRound() is not { } round)
        {
            return;
        }

        StartRound(round);
    }

    private void StartRound(RoundDefinition round)
    {
        if (round.AssignedTransponders.Count == 0
            && AppDialog.Show(this,
                $"\"{round.RoundLabel}\" has no drivers assigned. Start it anyway?",
                "No drivers", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        if (round.IsRecorded
            && AppDialog.Show(this,
                $"\"{round.RoundLabel}\" has already been run. Starting it again records a new session "
                + "and won't replace the existing result. Continue?",
                "Round already recorded", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // Starting a race is not something to do by accident on a
        // double-click, so it is confirmed - the countdown begins at once.
        if (AppDialog.Show(this,
                $"Start \"{round.RoundLabel}\" ({round.ClassName}) now?\r\n\r\n"
                + $"{Plural.Of(round.AssignedTransponders.Count, "driver")}. Staging and the countdown begin immediately.",
                "Start round", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        RoundStartRequested?.Invoke(round);
        _lblStatus.Text = $"Starting \"{round.RoundLabel}\"...";
    }

    /// <summary>
    /// Runs the selected round as a demo. Deliberately lighter-touch than a
    /// real start: nothing is recorded against the round, so the "already
    /// run" and "no drivers" warnings that guard a real start are not
    /// needed - a demo on an empty or already-raced round is a perfectly
    /// reasonable thing to want.
    /// </summary>
    private void StartRoundAsDemo()
    {
        if (SelectedRound() is not { } round)
        {
            return;
        }

        if (AppDialog.Show(this,
                $"Run \"{round.RoundLabel}\" as a DEMO?\r\n\r\n"
                + $"{Plural.Of(round.AssignedTransponders.Count, "driver")} will be simulated. "
                + "Nothing is recorded against this round and no real timing hardware is used.",
                "Start as demo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        RoundDemoRequested?.Invoke(round);
        _lblStatus.Text = $"Running \"{round.RoundLabel}\" as a demo...";
    }

    private void EditRoundRoster()
    {
        if (SelectedRound() is not { } round)
        {
            return;
        }

        // The phone refuses this too: the race on track is running against
        // the roster it started with.
        if (OnTrackRound(new[] { round }) is not null)
        {
            AppDialog.Show(this, $"\"{round.RoundLabel}\" is on track - change its drivers when it has finished.",
                "Round on track", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (round.IsRecorded
            && AppDialog.Show(this,
                $"\"{round.RoundLabel}\" has already been run. Changing its drivers won't change the recorded "
                + "results - it only affects the program. Continue?",
                "Round already run", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        using var form = new RoundRosterForm(round, _profiles, RosterFor(round.ClassName));

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // By id, onto the round as it is on file now - a round deleted since
        // stays deleted - and only if it has not been run since and still
        // holds the drivers this edit gave it (see CheckRosterRestore): the
        // undo is still on the stack after the heat has raced, and putting
        // the old drivers back then made its roster disagree with its
        // result. A round that had already run when it was edited (the
        // question above allows that) can still be put back.
        var restore = new RosterRestore(round.Id, round.LinkedSessionId, form.Result.ToList(), round.AssignedTransponders.ToList());
        var label = $"Edit drivers in {round.RoundLabel}";

        Action? undoEdit = null;
        undoEdit = () =>
        {
            RefuseRosterUndoWhileOnTrack(label, undoEdit!, new[] { restore.RoundId });

            var verdict = CheckRosterUndo(new[] { restore }, _rounds.Get);

            if (verdict.Blocked.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{verdict.Blocked[0]} has been run or had its drivers changed since, so its old drivers were not put back. Nothing was changed.");
            }

            WriteRosters(new[] { restore });

            if (!IsDisposed)
            {
                RefreshGrid();
            }
        };

        _undo.Capture(label, undoEdit);

        round.AssignedTransponders = form.Result;
        _rounds.Update(round);
        RefreshGrid();

        _lblStatus.Text = $"{round.RoundLabel} now has {Plural.Of(form.Result.Count, "driver")}.";
    }

    /// <summary>
    /// What a drag inside the programme grid carries.
    ///
    /// A type of its own so that nothing else dropped onto the grid - a
    /// file from Explorer, text from another window - can be mistaken for
    /// a reorder. The grid accepts this and nothing else.
    /// </summary>
    private sealed record RoundDrag(List<string> Ids);

    /// <summary>
    /// Lets the running order be dragged into shape.
    ///
    /// Move Up and Move Down are still in the menu and still do the same
    /// thing - this is the shortcut, not the only door. It earns its place
    /// on a long programme, where walking a main from the bottom to the
    /// top is a dozen presses and one drag.
    ///
    /// A press does NOT start a drag. It records where it landed and waits
    /// for the pointer to travel past the system's drag threshold, so that
    /// an ordinary click still selects a row and a double-click still
    /// starts the round - both of which a drag-on-press would eat.
    /// </summary>
    private void EnableDragReorder()
    {
        _grid.AllowDrop = true;

        _grid.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                _pressRow = -1;
                return;
            }

            _pressRow = _grid.HitTest(e.X, e.Y).RowIndex;
            _pressAt = e.Location;
        };

        _grid.MouseMove += (_, e) =>
        {
            if (_pressRow < 0 || (e.Button & MouseButtons.Left) == 0)
            {
                return;
            }

            var threshold = SystemInformation.DragSize;

            if (Math.Abs(e.X - _pressAt.X) < threshold.Width
                && Math.Abs(e.Y - _pressAt.Y) < threshold.Height)
            {
                return;
            }

            var row = _pressRow;

            // Cleared before the drag rather than after it: DoDragDrop
            // blocks until the drop is over, and a second drag starting
            // from inside the first is the one way this gets confusing.
            _pressRow = -1;

            if (row >= _grid.Rows.Count)
            {
                return;
            }

            // Dragging a row that is not in the selection moves THAT row.
            // Anything else means a drag can quietly move rounds that are
            // nowhere near the pointer.
            if (!_grid.Rows[row].Selected)
            {
                _grid.ClearSelection();
                _grid.Rows[row].Selected = true;
            }

            var ids = _grid.SelectedRows
                .Cast<DataGridViewRow>()
                .OrderBy(r => r.Index)
                .Select(r => r.Tag as string)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .ToList();

            if (ids.Count == 0)
            {
                return;
            }

            _grid.DoDragDrop(new RoundDrag(ids), DragDropEffects.Move);
        };

        _grid.MouseUp += (_, _) => _pressRow = -1;

        _grid.DragOver += (_, e) =>
        {
            if (e.Data?.GetDataPresent(typeof(RoundDrag)) != true)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Move;

            var point = _grid.PointToClient(new Point(e.X, e.Y));

            ScrollDuringDrag(point.Y);

            var (before, y) = DropAt(point.Y);

            if (before == _dropBefore && y == _dropY)
            {
                return;
            }

            _dropBefore = before;
            _dropY = y;
            _grid.Invalidate();
        };

        _grid.DragLeave += (_, _) =>
        {
            _dropBefore = -1;
            _grid.Invalidate();
        };

        _grid.DragDrop += (_, e) =>
        {
            var payload = e.Data?.GetData(typeof(RoundDrag)) as RoundDrag;
            var before = _dropBefore;

            _dropBefore = -1;
            _grid.Invalidate();

            if (payload is not null && before >= 0)
            {
                DropRounds(payload.Ids, before);
            }
        };

        // DataGridView paints its cells and THEN raises Paint, so the line
        // lands on top of the rows rather than under them.
        _grid.Paint += (_, e) =>
        {
            if (_dropBefore < 0)
            {
                return;
            }

            using var pen = new Pen(AppTheme.Primary, 2f);

            e.Graphics.DrawLine(pen, 0, _dropY, _grid.ClientSize.Width, _dropY);

            // A stub at each end, so the line reads as an insertion point
            // rather than as a row border that has gone odd.
            using var cap = new SolidBrush(AppTheme.Primary);

            e.Graphics.FillRectangle(cap, 0, _dropY - 3, 3, 7);
            e.Graphics.FillRectangle(cap, _grid.ClientSize.Width - 3, _dropY - 3, 3, 7);
        };
    }

    /// <summary>
    /// Where a drop at this height would land: the row index to insert in
    /// front of, and the y to draw the line at.
    ///
    /// Both come back together because the caller needs them to agree.
    /// Working the y out again at paint time meant asking the grid for a
    /// row rectangle that may since have scrolled out of view, which
    /// returns an empty rectangle and drew the line at the top of the
    /// control.
    /// </summary>
    private (int Before, int Y) DropAt(int y)
    {
        if (_grid.Rows.Count == 0)
        {
            return (0, _grid.ColumnHeadersHeight);
        }

        var hit = _grid.HitTest(2, y);

        if (hit.RowIndex >= 0)
        {
            var bounds = _grid.GetRowDisplayRectangle(hit.RowIndex, false);

            // Past the middle of a row means after it, which is what makes
            // the line follow the pointer instead of jumping a whole row
            // late.
            return y > bounds.Top + bounds.Height / 2
                ? (hit.RowIndex + 1, bounds.Bottom - 1)
                : (hit.RowIndex, bounds.Top);
        }

        if (y <= _grid.ColumnHeadersHeight)
        {
            var first = Math.Max(0, _grid.FirstDisplayedScrollingRowIndex);
            var bounds = _grid.GetRowDisplayRectangle(first, false);

            return (first, bounds.Height > 0 ? bounds.Top : _grid.ColumnHeadersHeight);
        }

        var last = _grid.GetRowDisplayRectangle(_grid.Rows.Count - 1, false);

        return (_grid.Rows.Count, last.Height > 0 ? last.Bottom - 1 : _grid.ClientSize.Height - 1);
    }

    /// <summary>
    /// Creeps the list along when the pointer is held near an edge, so a
    /// round can be dragged somewhere that is not currently on screen.
    /// One row per DragOver, which is slow enough to aim with.
    /// </summary>
    private void ScrollDuringDrag(int y)
    {
        var first = _grid.FirstDisplayedScrollingRowIndex;

        if (first < 0)
        {
            return;
        }

        var edge = Math.Max(20, _grid.RowTemplate.Height);

        if (y < _grid.ColumnHeadersHeight + edge && first > 0)
        {
            _grid.FirstDisplayedScrollingRowIndex = first - 1;
        }
        else if (y > _grid.ClientSize.Height - edge && first < _grid.Rows.Count - 1)
        {
            _grid.FirstDisplayedScrollingRowIndex = first + 1;
        }
    }

    /// <summary>
    /// Carries out a drop: moves the dragged rounds in front of whatever
    /// row <paramref name="insertBefore"/> points at.
    ///
    /// Undoable - see MoveWithUndo. (It had no undo entry while the stack
    /// was only three deep.)
    /// </summary>
    private void DropRounds(List<string> ids, int insertBefore)
    {
        var anchor = AnchorFor(ShownRoundIds(), insertBefore);

        MoveWithUndo(ids, anchor);

        RefreshGrid();

        // The moved rounds stay selected, so the eye can find them again
        // and a second drag does not start from nothing.
        var moved = ids.ToHashSet(StringComparer.Ordinal);

        DataGridViewRow? first = null;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is string id && moved.Contains(id))
            {
                first = row;
                break;
            }
        }

        if (first is not null)
        {
            // Current cell FIRST. Assigning it resets the selection to that
            // one row, so doing it afterwards would collapse a multi-row
            // move back down to a single row.
            _grid.CurrentCell = first.Cells[0];
            _grid.ClearSelection();

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is string id && moved.Contains(id))
                {
                    row.Selected = true;
                }
            }
        }

        _lblStatus.Text = ids.Count == 1
            ? $"Moved {_rounds.Get(ids[0])?.RoundLabel ?? "the round"} in the running order."
            : $"Moved {ids.Count} rounds in the running order.";
    }

    /// <summary>
    /// The rounds currently on screen, in the order they are shown.
    ///
    /// Read off the grid rather than re-derived from the day and the class
    /// filter. The grid IS the shown order, and a second derivation is a
    /// second thing that can disagree with what the user is looking at.
    /// Index-aligned with the rows, so a row index means the same thing in
    /// both.
    /// </summary>
    private List<string> ShownRoundIds()
    {
        var ids = new List<string>();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            ids.Add(row.Tag as string ?? string.Empty);
        }

        return ids;
    }

    /// <summary>
    /// The round a dropped block should land in front of, or null for the
    /// end of the programme.
    ///
    /// The subtle case is a drop past the last row while the view is
    /// FILTERED to one class: the end of what you can see is not the end
    /// of the day, so the block goes in front of whatever follows that
    /// last visible round in the full running order. With no filter on
    /// there is nothing after it, and null sends the block to the end,
    /// which is what it looks like it should do.
    /// </summary>
    private string? AnchorFor(List<string> shown, int insertBefore)
    {
        if (insertBefore < shown.Count)
        {
            return shown[insertBefore];
        }

        if (shown.Count == 0)
        {
            return null;
        }

        var all = _rounds.All.ToList();
        var last = all.FindIndex(r => r.Id == shown[^1]);

        return last >= 0 && last + 1 < all.Count ? all[last + 1].Id : null;
    }

    /// <summary>
    /// Opens the class's heats side by side so drivers can be moved
    /// between them.
    ///
    /// The form writes nothing itself - it hands back what changed, and
    /// the whole session's work is applied here as ONE change with ONE
    /// undo entry. That is the difference between this and the drag in the
    /// grid: a running order is undone by dragging it back, whereas a
    /// roster that has had six drivers shuffled around it is not something
    /// anybody can reconstruct from memory.
    /// </summary>
    private void BalanceHeats()
    {
        if (TargetClass() is not { } className)
        {
            return;
        }

        var rounds = DayRounds()
            .Where(r => string.Equals(r.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (rounds.Count == 0)
        {
            RefuseWithOffer("No heats",
                $"\"{className}\" has no heats yet, so there is nothing to balance.",
                "Build tonight's heats now?",
                BuildQualifyingProgramme);

            return;
        }

        using var form = new HeatBalanceForm(
            className,
            rounds,
            RosterFor(className),
            _profiles,
            ClassColourFor(className),

            // DayRounds is only the race day's rounds when there is one, so
            // a heat already run there is tonight's and counts towards "one
            // heat per qualifying round".
            roundsAreOneRaceDay: ActiveDay is not null,

            // The heat out on track is locked on the form, as the phone
            // refuses to change it: its race is running against that roster.
            onTrackRoundId: RoundOnTrackId?.Invoke());

        if (form.ShowDialog(this) != DialogResult.OK || form.Changes.Count == 0)
        {
            return;
        }

        ApplyRosterChanges(className, form.Changes.ToList(), string.Empty);
    }

    /// <summary>
    /// One round's roster as an undo of a roster change wants to put it
    /// back: the session the round was filed under when the change was
    /// made (null when it had not been run), the drivers the change gave
    /// it, and the drivers it had before.
    /// </summary>
    internal sealed record RosterRestore(string RoundId, string? SessionThen, List<string> Given, List<string> Old);

    /// <summary>Why an undo would leave a round's roster alone - or None when it may put the old one back.</summary>
    internal enum RosterRestoreBlock
    {
        None,

        /// <summary>The round has been run (or run again) since the change: its roster is what raced.</summary>
        RunSince,

        /// <summary>The round's drivers have been changed again since, by hand or by another command.</summary>
        EditedSince,
    }

    /// <summary>
    /// Whether an undo of a roster change may put <paramref name="restore"/>'s
    /// old drivers back onto <paramref name="current"/>, the round as it is
    /// on file now.
    ///
    /// Not if it has been run since: filing a heat adds nothing to the undo
    /// stack, so a roster undo can still be reached after the heat has
    /// raced, and handing a run heat its old drivers made its roster
    /// disagree with its result (the marshal list, the published program
    /// and the event report read the roster) and put a driver who had
    /// already raced back into an unrun heat. "Since" is judged by the
    /// session it is filed under, not by IsRecorded alone, so a round that
    /// had already run when its drivers were changed (Edit Drivers asks
    /// first and allows it; a bump-up always changes the main below, which
    /// has run) can still be put back as long as it has not been run again.
    ///
    /// Not if its drivers have changed again since either: undoing this
    /// change must not quietly undo that later one as well.
    /// </summary>
    internal static RosterRestoreBlock CheckRosterRestore(RoundDefinition current, RosterRestore restore)
    {
        if (!string.Equals(current.LinkedSessionId ?? string.Empty, restore.SessionThen ?? string.Empty, StringComparison.Ordinal))
        {
            return RosterRestoreBlock.RunSince;
        }

        return current.AssignedTransponders.SequenceEqual(restore.Given, StringComparer.Ordinal)
            ? RosterRestoreBlock.None
            : RosterRestoreBlock.EditedSince;
    }

    /// <summary>
    /// The guard every roster undo runs first: not while one of the rounds
    /// it would change is on track. That race is running against the
    /// roster it started with, and the forward commands refuse to change it
    /// for the same reason; the undo used to change it mid-race. The entry
    /// goes back on the stack before refusing (the stack pops it before
    /// running it), so it can still be undone once the race is saved - as
    /// Redo Remaining Heats' undo does.
    /// </summary>
    private void RefuseRosterUndoWhileOnTrack(string label, Action undo, IEnumerable<string> roundIds)
    {
        if (RoundOnTrackId?.Invoke() is not { } onTrackId
            || !roundIds.Contains(onTrackId, StringComparer.Ordinal))
        {
            return;
        }

        var name = _rounds.Get(onTrackId)?.RoundLabel ?? "a heat it changed";

        _undo.Capture(label, undo);

        throw new InvalidOperationException(
            $"{name} is on track - finish and save its race, then undo again. Nothing was changed.");
    }

    /// <summary>
    /// What a roster undo found when it checked every round it would
    /// change: the labels of those it may not put back (run or edited
    /// since - see CheckRosterRestore), and the drivers that putting the
    /// rest back would leave in no round at all because the round they
    /// would go back into has been deleted since. Only when both are empty
    /// may the undo go ahead.
    /// </summary>
    internal sealed record RosterUndoVerdict(List<string> Blocked, List<string> Stranded)
    {
        public bool CanRestore => Blocked.Count == 0 && Stranded.Count == 0;
    }

    /// <summary>
    /// Checks an undo that changes several rounds' rosters as one unit -
    /// a move between heats, or a bump-up (the main and its feeder) -
    /// before anything is written.
    ///
    /// Those rounds swapped drivers with each other, so they only go back
    /// together. The undo used to put back each round on its own and skip
    /// the ones that had run since, and that split the move in half: move
    /// X from Heat 1 to Heat 2, run Heat 2, undo, and Heat 2 kept X (it
    /// had raced) while Heat 1 got X back, so X was called to race the
    /// round twice. The other way round - the source run, the destination
    /// not - X came out of Heat 2 and went into no heat at all. So one
    /// blocked round now blocks them all.
    ///
    /// A round deleted since is skipped, since there is nothing to put
    /// back into it - unless one of the rounds that is still there would
    /// hand back a driver who was going back into the deleted one. Then
    /// that driver would end up in no round at all, so that refuses too.
    /// <paramref name="currentOf"/> looks a round up by id as it is on
    /// file now (null when it has been deleted).
    /// </summary>
    internal static RosterUndoVerdict CheckRosterUndo(IReadOnlyList<RosterRestore> restores, Func<string, RoundDefinition?> currentOf)
    {
        var blocked = new List<string>();
        var deletedOld = new HashSet<string>(StringComparer.Ordinal);
        var takenOut = new List<string>();
        var putIn = new HashSet<string>(StringComparer.Ordinal);

        foreach (var restore in restores)
        {
            if (currentOf(restore.RoundId) is not { } current)
            {
                deletedOld.UnionWith(restore.Old);
                continue;
            }

            if (CheckRosterRestore(current, restore) != RosterRestoreBlock.None)
            {
                blocked.Add(current.RoundLabel);
            }

            // Who putting this round back would take out of it, and who it
            // would put back in.
            takenOut.AddRange(restore.Given.Except(restore.Old, StringComparer.Ordinal));
            putIn.UnionWith(restore.Old.Except(restore.Given, StringComparer.Ordinal));
        }

        // A driver taken out of a round that is still there, put back into
        // none of the others that are still there, and whose way home was
        // a round that has gone.
        var stranded = takenOut
            .Where(t => !putIn.Contains(t) && deletedOld.Contains(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new RosterUndoVerdict(blocked, stranded);
    }

    /// <summary>
    /// Says why a roster undo was refused whole, for the exception the undo
    /// throws (UndoService reports it). <paramref name="unit"/> names what
    /// goes back together - "a move" or "bump-ups" - and
    /// <paramref name="noun"/> what it changed - "heat" or "main".
    /// </summary>
    private static string DescribeRosterUndoRefusal(RosterUndoVerdict verdict, string unit, string noun)
    {
        if (verdict.Blocked.Count > 0)
        {
            var one = verdict.Blocked.Count == 1;

            return $"{string.Join(" and ", verdict.Blocked)} {(one ? "has" : "have")} been run or had "
                   + $"{(one ? "its" : "their")} drivers changed since. The {noun}s in {unit} only go back together - "
                   + $"putting the others back on their own would leave a driver in two {noun}s or in none - "
                   + "so none were put back. Nothing was changed.";
        }

        return $"a {noun} changed by {unit} has been deleted since, so putting the others back would leave "
               + $"{Plural.Of(verdict.Stranded.Count, "driver")} in no {noun} at all. Nothing was changed.";
    }

    /// <summary>
    /// Puts back the old roster of every round in <paramref name="restores"/>
    /// that is still on file, looked up by id; a round deleted since stays
    /// deleted. Only call this once CheckRosterUndo has said yes.
    /// </summary>
    private void WriteRosters(IEnumerable<RosterRestore> restores)
    {
        foreach (var restore in restores)
        {
            if (_rounds.Get(restore.RoundId) is not { } current)
            {
                continue;
            }

            current.AssignedTransponders = restore.Old.ToList();
            _rounds.Update(current);
        }
    }

    /// <summary>
    /// Writes a set of heat roster changes as ONE change with ONE undo entry
    /// (labelled with <paramref name="undoPrefix"/> in front). The desk's
    /// Move Drivers Between Heats and the phone's "move a driver" both end
    /// here; the rules about which moves are allowed are the caller's - see
    /// HeatBalanceForm.DescribeMoveBlocker.
    /// </summary>
    public ProgramAnswer ApplyRosterChanges(string className, IReadOnlyList<HeatBalanceForm.RosterChange> changes, string undoPrefix)
    {
        if (changes.Count == 0)
        {
            return new ProgramAnswer(false, "Nothing changed", "No heat's drivers changed.");
        }

        // Captured before the change is made, the way every other undoable
        // command on this page does it.
        //
        // The undo puts a heat's old drivers back only onto a heat that has
        // not been run since and still holds exactly what this move gave it
        // - the same rules as Redo Remaining Heats' undo, and for the same
        // reason: filing a heat adds nothing to the undo stack, so "move X
        // from Heat 1 to Heat 2, run Heat 2, Ctrl+Z" used to take X out of
        // a heat X had just raced in and put X back into unrun Heat 1, so X
        // was called to the grid to race the same round twice. Which
        // session each heat was filed under is noted now, so a heat that
        // had already run when the move was made can still be put back,
        // while one run SINCE cannot. The heats go back together or not at
        // all (CheckRosterUndo): putting back only the ones still allowed
        // split the move and left X in two heats, or in none.
        var label = $"{undoPrefix}Move drivers between heats in {className}";
        var restores = changes
            .Select(c => new RosterRestore(c.RoundId, _rounds.Get(c.RoundId)?.LinkedSessionId, c.After.ToList(), c.Before.ToList()))
            .ToList();

        Action? undoMove = null;
        undoMove = () =>
        {
            RefuseRosterUndoWhileOnTrack(label, undoMove!, restores.Select(r => r.RoundId));

            // Every heat is checked before any is written, and one that may
            // not go back stops them all (see CheckRosterUndo). Not put back
            // on the stack: a heat run or edited since stays that way, so
            // trying again would only refuse again - as Edit Drivers' undo.
            var verdict = CheckRosterUndo(restores, _rounds.Get);

            if (!verdict.CanRestore)
            {
                throw new InvalidOperationException(DescribeRosterUndoRefusal(verdict, "a move", "heat"));
            }

            WriteRosters(restores);
            RefreshAll();
        };

        _undo.Capture(label, undoMove);

        foreach (var change in changes)
        {
            if (_rounds.Get(change.RoundId) is { } round)
            {
                round.AssignedTransponders = change.After.ToList();
                _rounds.Update(round);
            }
        }

        var done = changes.Count == 1
            ? $"Updated the drivers in one heat of {className}."
            : $"Updated the drivers in {changes.Count} heats of {className}.";

        _lblStatus.Text = done;
        RefreshAll();

        return new ProgramAnswer(true, "Heats updated", done);
    }

    private void MoveSelected(bool up)
    {
        if (SelectedRound() is not { } round)
        {
            return;
        }

        // Measured against what is ON SCREEN, not against the whole store.
        // With the class filter on, the round above this one in the
        // programme is often another class's heat that is not being shown -
        // and swapping with something invisible looks, from here, exactly
        // like the press did nothing. This is also what makes Move Up and a
        // one-row drag agree with each other: they now go through the same
        // reorder, from the same list.
        var shown = ShownRoundIds();
        var at = shown.IndexOf(round.Id);

        if (at < 0)
        {
            return;
        }

        if (up)
        {
            if (at == 0)
            {
                return;
            }

            // Through MoveWithUndo, like a drag and the phone. Calling the
            // store directly left no undo entry, so Undo after a mistaken
            // Move Down skipped the move and reversed whatever came before
            // it - often the whole Build Qualifying.
            MoveWithUndo(new[] { round.Id }, shown[at - 1]);
        }
        else
        {
            if (at >= shown.Count - 1)
            {
                return;
            }

            // In front of the round AFTER the next one - which is what
            // "down one place" means once it is expressed as an anchor.
            MoveWithUndo(new[] { round.Id }, AnchorFor(shown, at + 2));
        }

        RefreshGrid();

        // Keep the moved round selected, so a run of presses walks it up
        // the list instead of moving it once and then losing it.
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag as string == round.Id)
            {
                _grid.ClearSelection();
                row.Selected = true;
                _grid.CurrentCell = row.Cells[0];
                break;
            }
        }
    }

    /// <summary>
    /// Moves a named round one place up (-1) or down (+1) the programme.
    /// Returns false when it did not move - it is already at that end, or the
    /// round is not on today's programme.
    ///
    /// Public because the timetable needs it. That page used to have a Move Up
    /// of its own, working on a sequence stored in the timetable's file, so
    /// moving a heat there changed nothing about when it actually ran and the
    /// next rebuild discarded the move. There is one running order now and this
    /// is the one method that changes it.
    ///
    /// Measured against the WHOLE day, unlike MoveSelected above, which is
    /// measured against what is on screen. The caller is not looking at this
    /// page's class filter and should not be subject to it.
    /// </summary>
    public bool MoveRound(string roundId, int direction)
    {
        if (direction == 0 || _rounds.Get(roundId) is null)
        {
            return false;
        }

        var order = TodaysRounds().Select(r => r.Id).ToList();
        var at = order.IndexOf(roundId);

        if (at < 0)
        {
            return false;
        }

        if (direction < 0)
        {
            if (at == 0)
            {
                return false;
            }

            // Undoable, the same as Move Up on this page - see MoveWithUndo.
            MoveWithUndo(new[] { roundId }, order[at - 1]);
        }
        else
        {
            if (at >= order.Count - 1)
            {
                return false;
            }

            // In front of the round AFTER the next one - which is what "down
            // one place" means once it is expressed as an anchor. AnchorFor
            // handles the end of the day, where what follows may belong to
            // another event entirely.
            MoveWithUndo(new[] { roundId }, AnchorFor(order, at + 2));
        }

        // Which raises ProgrammeChanged through the stamp, and that is what
        // tells the timetable to redraw - see NoteProgrammeChanged.
        RefreshGrid();

        return true;
    }

    private void DeleteSelectedRounds()
    {
        var removed = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(r => r.Index)
            .Select(r => r.Tag as string)
            .Where(id => id is not null)
            .Select(id => _rounds.Get(id!))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        if (removed.Count == 0)
        {
            return;
        }

        // The same refusal the phone gives: the round out on track has not
        // been recorded yet, and deleting it leaves its result nothing to be
        // saved against.
        if (OnTrackRound(removed) is { } busy)
        {
            AppDialog.Show(this,
                $"\"{busy.RoundLabel}\" is on track - its result would have nothing to be saved against. Delete it when it has finished.",
                "Round on track", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var what = removed.Count == 1
            ? $"Delete round \"{removed[0].RoundLabel}\" from the program?"
            : $"Delete these {removed.Count} rounds from the program?\r\n\r\n"
              + string.Join("\r\n", removed.Select(r => $"   {r.RoundLabel}  ({r.ClassName})"));

        if (AppDialog.Show(this, $"{what}\r\n\r\nThis does not delete any recorded session history.",
                "Delete round", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        DeleteRounds(removed, string.Empty);
    }

    /// <summary>
    /// Takes rounds off the programme - no question asked; the caller has
    /// asked. One undo entry for the batch, labelled with
    /// <paramref name="undoPrefix"/> in front. Recorded session history is
    /// never touched: this deletes the programme's slots, not results.
    /// </summary>
    public ProgramAnswer DeleteRounds(IReadOnlyList<RoundDefinition> removed, string undoPrefix)
    {
        if (removed.Count == 0)
        {
            return new ProgramAnswer(false, "Nothing to delete", "No rounds were given to delete.");
        }

        // Which race day each round belonged to, captured BEFORE the delete
        // strips the membership - so the undo puts a round back into its
        // day as well as back into the programme. Without this a restored
        // round drifted into the legacy holder at the next launch.
        var owners = removed.ToDictionary(
            r => r.Id,
            r => _events?.EventForRound(r.Id)?.Id,
            StringComparer.Ordinal);

        // One undo entry for the whole batch - undoing a multi-delete a row
        // at a time would be worse than not having undo.
        _undo.Capture(
            undoPrefix + (removed.Count == 1 ? $"Delete round '{removed[0].RoundLabel}'" : $"Delete {removed.Count} rounds"),
            () =>
            {
                RestoreRounds(removed, owners);
                RefreshAll();
            });

        foreach (var round in removed)
        {
            _rounds.Delete(round.Id);
            _events?.RemoveRound(round.Id);
        }

        RefreshAll();

        return new ProgramAnswer(true, "Deleted",
            removed.Count == 1 ? $"Deleted {removed[0].RoundLabel} from the program." : $"Deleted {removed.Count} rounds from the program.");
    }

    // ------------------------------------------------------------------
    //  Day tools
    // ------------------------------------------------------------------

    // Blank, so the resolution happens once inside - asking twice would
    // put the "Pick a class" dialog up twice.
    private void ShowStandings() => ShowStandings(string.Empty);

    private void ShowStandings(string className)
    {
        // Before the class is asked for - there is nothing to show for any
        // class. See QualifyingRoundFilter.
        if (ActiveDay is null)
        {
            AppDialog.Show(this, NoRaceDayStandings, NoRaceDayTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (Resolve(className) is not { } target)
        {
            return;
        }

        var entries = QualifyingCalculator.Compute(
            target, _rounds, _sessionHistory, _profiles, ClassByName(target), QualifyingRoundFilter());

        using var form = new QualifyingStandingsForm(target, entries, ClassByName(target));
        form.ShowDialog(this);
    }

    /// <summary>
    /// What "yes" means on each of the two refusals seeding can give.
    ///
    /// Neither one seeds. "Mains already run" deliberately does NOT delete
    /// anything: the rounds it would have to remove have results filed
    /// against them, and an app that quietly throws those away to make a
    /// button work is worse than one that says no. It shows the class's
    /// programme instead, with the mains in it, so the decision is made by
    /// the person who can see what it costs.
    /// </summary>
    private void TakeSeedOffer(string className, string title)
    {
        if (title == NothingToSeedFromTitle)
        {
            AddMainByHand();
            return;
        }

        // Mains already run: put the class in front of them.
        for (var i = 0; i < _cmbFilter.Items.Count; i++)
        {
            if (_cmbFilter.Items[i] as string is { } name
                && string.Equals(name, className, StringComparison.OrdinalIgnoreCase))
            {
                _cmbFilter.SelectedIndex = i;
                break;
            }
        }

        RefreshGrid();
    }

    /// <summary>The corner marshal roster for a class - the previous heat's drivers marshal the next one.</summary>
    private void ShowMarshalAssignments()
    {
        if (TargetClass() is not { } className)
        {
            return;
        }

        // Today's rounds only: the roster pairs each heat with the one
        // before it, and a heat from last week is nobody's previous heat.
        var rounds = DayRoundsForClass(className);

        if (rounds.Count == 0)
        {
            RefuseWithOffer("Nothing to assign",
                $"\"{className}\" has no rounds on today's program yet, so there is nobody to put on the posts.",
                "Build tonight's heats now?",
                BuildQualifyingProgramme);

            return;
        }

        // Posts belong to the track, so the layout comes from the rounds
        // themselves rather than from whatever the race day says.
        var layoutName = rounds
            .Select(r => r.TrackLayoutName)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        var layout = layoutName is null
            ? null
            : _trackLayouts.All.FirstOrDefault(l => string.Equals(l.Name, layoutName, StringComparison.OrdinalIgnoreCase));

        var assignments = MarshalAssigner.Assign(rounds, layout, _profiles);

        using var form = new MarshalAssignmentsForm(className, assignments);
        form.ShowDialog(this);
    }

    /// <summary>
    /// Promotes the top finishers of a completed main into the tier above.
    /// Offers whichever main is actually ready, and explains why the others
    /// are not rather than just omitting them.
    /// </summary>
    private void ApplyBumpUps(string? className)
    {
        // Not "target" - this method already has one for the main being
        // bumped into, further down.
        if (Resolve(className) is not { } forClass)
        {
            return;
        }

        className = forClass;

        var plan = PlanBumpUps(className);

        if (!plan.Ok || plan.Target is not { } target)
        {
            if (plan.Offer is { } offer)
            {
                RefuseWithOffer(plan.Title, plan.Message, offer, AddMainByHand);
                return;
            }

            AppDialog.Show(this, plan.Message, plan.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Staged or racing but not recorded yet, so the blocker check lets it
        // through - the phone refuses this, and so does the desk.
        if (OnTrackRound(new[] { target }) is not null)
        {
            AppDialog.Show(this, $"{target.RoundLabel} is on track - bump-ups have to happen before it runs.",
                plan.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (plan.NothingToMove)
        {
            AppDialog.Show(this, plan.Message, plan.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Marked done even though nobody moved. Only an actual bump-up
            // used to set it, so a main with nobody left to promote kept the
            // banner on "Apply Bump-Ups" for the rest of the day - ahead of
            // the very main it was waiting on.
            SkipBumpUps(target);
            return;
        }

        // Three answers, not two. "No" used to be the only way to decline,
        // and it declined for now only: the banner stayed on "Apply
        // Bump-Ups" and there was no way to tell it the club was not doing
        // them today.
        var answer = AppDialog.Show(this,
            plan.Message
            + "\r\n\r\nYes - move them up now"
            + $"\r\nNo - skip bump-ups for {target.RoundLabel} and carry on"
            + "\r\nCancel - decide later",
            plan.Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        if (answer == DialogResult.No)
        {
            SkipBumpUps(target);
            _lblStatus.Text = $"Skipped bump-ups into {target.RoundLabel}.";
            return;
        }

        if (answer != DialogResult.Yes)
        {
            return;
        }

        var result = ApplyBumpUpsInto(target, string.Empty);

        AppDialog.Show(this, result.Message, result.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// What a class's bump-ups would do right now. Offers whichever main is
    /// actually ready, and explains why the others are not rather than just
    /// omitting them. Changes nothing - shared with the phone.
    ///
    /// Ok false: no main is ready (Message says why). Ok true with
    /// NothingToMove: the main is ready but every qualifying finisher is in
    /// it already, and the only thing left is to mark it done
    /// (SkipBumpUps). Otherwise Message is the question to ask before
    /// ApplyBumpUpsInto.
    /// </summary>
    public BumpUpsAnswer PlanBumpUps(string className)
    {
        // Before the mains are even gathered: a club night has none by
        // design, and "no tiered mains" below would be true but would read
        // as something to go and fix. See ClubNightRefusal.
        if (ClubNightRefusal("moving the top finishers up", "finals to move anybody up into") is { } notTonight)
        {
            return new BumpUpsAnswer(false, notTonight.Title, notTonight.Message, null, null, false);
        }

        // Today's mains only. Mixing in an earlier day's could pair
        // tonight's A-main with last week's B-main as its feeder, or offer
        // to bump into a main that ran days ago.
        var mains = DayMainsForClass(className);

        if (mains.Count == 0)
        {
            return new BumpUpsAnswer(false, "No tiered mains",
                "This class has no finals to bump anybody up into.\r\n\r\n"
                + "Bump-ups move the top finishers of one final up into the one above it, so there have to be at "
                + "least two. Build them from qualifying, or add them by hand named \"A-Main\", \"B-Main\" and so "
                + "on - which is how the app knows which feeds which.",
                null, null, false,
                Offer: "Add a main by hand now?");
        }

        var ready = mains.Where(m => MainsBuilder.DescribeBumpUpBlocker(m, mains, _sessionHistory) is null).ToList();

        if (ready.Count == 0)
        {
            // Explain the nearest blocker rather than a bare "can't do that".
            var reasons = mains
                .Select(m => (m, Reason: MainsBuilder.DescribeBumpUpBlocker(m, mains, _sessionHistory)))
                .Where(x => x.Reason is not null)
                .Select(x => $"   {x.m.RoundLabel}: {x.Reason}");

            return new BumpUpsAnswer(false, "Nothing to bump",
                "No main is ready for bump-ups right now.\r\n\r\n" + string.Join("\r\n", reasons),
                null, null, false);
        }

        var target = ready[0];
        var feeder = mains.First(m => m.MainTier == target.MainTier + 1);
        var feederResults = _sessionHistory.Get(feeder.LinkedSessionId!)!;

        // Anyone already in the target cannot be promoted again - usually
        // because they were moved up by hand. The confirmation used to list
        // the raw top finishers while the apply silently skipped these, so
        // it promised a promotion and then reported zero. Worked out by
        // MainsBuilder, the same call ApplyBumpUp makes, so the names asked
        // about are the drivers moved - ghosts left out, spare transponders
        // counted as their driver.
        var plan = MainsBuilder.PlanBumpUp(target, feederResults, _profiles);
        var movable = plan.Movable;
        var alreadyThere = plan.AlreadyThere;

        if (movable.Count == 0)
        {
            var who = alreadyThere.Count > 0
                ? $"\r\n\r\nAlready in {target.RoundLabel}:\r\n"
                  + string.Join("\r\n", alreadyThere.Select(r => $"   {r.DriverName}"))
                : string.Empty;

            return new BumpUpsAnswer(true, "Nothing to bump",
                $"Nothing to bump up - every qualifying finisher from {feeder.RoundLabel} is already in {target.RoundLabel}.{who}"
                + $"\r\n\r\nBump-ups for {target.RoundLabel} are marked as done, so the program can move on.",
                target, feeder, true);
        }

        var names = string.Join("\r\n", movable.Select(r => $"   P{r.Position}  {r.DriverName}"));

        var skipped = alreadyThere.Count > 0
            ? $"\r\n\r\n{alreadyThere.Count} of the top finishers are already in {target.RoundLabel} and will be left alone."
            : string.Empty;

        return new BumpUpsAnswer(true, ProgramCaptions.BumpUps.TrimEnd('.'),
            $"Bump {Plural.Of(movable.Count, "driver")} from {feeder.RoundLabel} up into {target.RoundLabel}?\r\n\r\n{names}{skipped}"
            + $"\r\n\r\n{target.RoundLabel} grows to take them - nobody is moved down into {feeder.RoundLabel}, "
            + "which has already run.",
            target, feeder, false);
    }

    /// <summary>Today's tiered mains for a class, lowest tier number (the A-main) first.</summary>
    private List<RoundDefinition> DayMainsForClass(string className) =>
        DayRoundsForClass(className)
            .Where(r => r.Kind == RoundKind.Main && r.MainTier >= 0)
            .OrderBy(r => r.MainTier)
            .ToList();

    /// <summary>
    /// Promotes the top finishers of the main below <paramref name="target"/>
    /// into it - no question asked; the caller has asked (PlanBumpUps).
    /// Re-checked here, so a main that has been run or bumped meanwhile is
    /// refused rather than bumped twice. One undo entry, labelled with
    /// <paramref name="undoPrefix"/> in front.
    /// </summary>
    public ProgramAnswer ApplyBumpUpsInto(RoundDefinition target, string undoPrefix)
    {
        var mains = DayMainsForClass(target.ClassName);

        if (MainsBuilder.DescribeBumpUpBlocker(target, mains, _sessionHistory) is { } blocker)
        {
            return new ProgramAnswer(false, "Nothing to bump", $"{target.RoundLabel}: {blocker}");
        }

        var feeder = mains.First(m => m.MainTier == target.MainTier + 1);
        var feederResults = _sessionHistory.Get(feeder.LinkedSessionId!)!;

        // Snapshot both rosters - a bump-up changes two rounds at once.
        var targetBefore = target.AssignedTransponders.ToList();
        var feederBefore = feeder.AssignedTransponders.ToList();

        // What the bump-up gives each main, filled in once it has been
        // applied below, so the undo can tell whether each still holds it.
        RosterRestore? targetRestore = null;
        RosterRestore? feederRestore = null;

        var label = $"{undoPrefix}Bump-ups into {target.RoundLabel}";

        // The undo works on the mains as they are on file when it runs,
        // looked up by id, and under the same rules as the other roster
        // undos (CheckRosterRestore). It used to write the old rosters back
        // and re-open the bump-ups unconditionally - so undoing after the
        // A-main had run took the promoted drivers out of a main they had
        // just raced in (its marshal list, published program and event
        // report read that roster) and offered its bump-ups again. Once the
        // target has been run, or its drivers changed by hand, there is
        // nothing this undo can honestly put back, so it refuses whole and
        // leaves the bump-ups marked as done. The feeder has always run by
        // now; it goes back with the main, and if it has been run again or
        // edited since, the main stays as it is too (CheckRosterUndo).
        Action? undoBumpUps = null;
        undoBumpUps = () =>
        {
            RefuseRosterUndoWhileOnTrack(label, undoBumpUps!, new[] { target.Id, feeder.Id });

            if (targetRestore is null || feederRestore is null || _rounds.Get(target.Id) is not { } currentTarget)
            {
                // The bump-up never got as far as being applied, or the main
                // has been deleted since: nothing to put back.
                return;
            }

            // Both mains are checked before either is written: putting the
            // main back while the feeder stayed as it is took the promoted
            // drivers out of the main and into no race at all.
            var verdict = CheckRosterUndo(new[] { targetRestore, feederRestore }, _rounds.Get);

            if (!verdict.CanRestore)
            {
                throw new InvalidOperationException(DescribeRosterUndoRefusal(verdict, "bump-ups", "main"));
            }

            currentTarget.AssignedTransponders = targetRestore.Old.ToList();
            currentTarget.BumpUpsApplied = false;
            _rounds.Update(currentTarget);

            WriteRosters(new[] { feederRestore });
            RefreshAll();
        };

        _undo.Capture(label, undoBumpUps);

        var result = MainsBuilder.ApplyBumpUp(target, feeder, feederResults, _profiles);

        _rounds.Update(target);
        _rounds.Update(feeder);

        targetRestore = new RosterRestore(target.Id, target.LinkedSessionId, target.AssignedTransponders.ToList(), targetBefore);
        feederRestore = new RosterRestore(feeder.Id, feeder.LinkedSessionId, feeder.AssignedTransponders.ToList(), feederBefore);

        RefreshAll();

        return new ProgramAnswer(true, "Bump-ups applied",
            $"Promoted {Plural.Of(result.Promoted.Count, "driver")} into {target.RoundLabel}"
            + (result.Demoted.Count > 0 ? $", and moved {result.Demoted.Count} down to {feeder.RoundLabel}." : "."));
    }

    /// <summary>
    /// Marks a main's bump-ups as dealt with without moving anybody - for
    /// "nobody to move" and for "we are not doing bump-ups today". Either
    /// way the banner has to be able to move past it, and it only does that
    /// once the main says its bump-ups are done. Undoable, like the real
    /// thing; the undo entry is labelled with <paramref name="undoPrefix"/>
    /// in front.
    /// </summary>
    public void SkipBumpUps(RoundDefinition target, string undoPrefix = "")
    {
        _undo.Capture($"{undoPrefix}Skip bump-ups into {target.RoundLabel}", () =>
        {
            target.BumpUpsApplied = false;
            _rounds.Update(target);
            RefreshAll();
        });

        target.BumpUpsApplied = true;
        _rounds.Update(target);
        RefreshAll();
    }

    /// <summary>
    /// Which rounds qualifying should count: the open race day's, and none
    /// when no race day is open. Never null - to the calculator null means
    /// "no filter", and that is the bug this closes.
    ///
    /// It used to return null with no day open, meaning every recorded
    /// round. After End Race Day that put every heat the class had ever run
    /// into the standings as tonight's order, and Seed Mains built an A-main
    /// from drivers who were not at the track (25 Sep audit). The programme
    /// already needs a race day to be built; qualifying now needs one to be
    /// counted, and the Standings and Seed Mains refusals say so rather than
    /// showing an empty table.
    ///
    /// A race day with no rounds on it yet is still a race day: it counts
    /// nothing. The rule lives in RaceEventStore.QualifyingScope, where it
    /// is tested.
    /// </summary>
    private IReadOnlyCollection<string> QualifyingRoundFilter() =>
        (IReadOnlyCollection<string>?)_events?.QualifyingScope() ?? Array.Empty<string>();

    /// <summary>Renders a class's rounds as a printable sheet for the pit table.</summary>
    private void PrintHeatSheet()
    {
        if (TargetClass() is not { } className)
        {
            return;
        }

        // Today's rounds only - the sheet is for tonight's pit table, and
        // the file is named with today's date.
        var rounds = DayRoundsForClass(className);

        if (rounds.Count == 0)
        {
            RefuseWithOffer("Nothing to print",
                $"\"{className}\" has no rounds on today's program yet, so a heat sheet would be blank.",
                "Build tonight's heats now?",
                BuildQualifyingProgramme);

            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save heat sheet",
            Filter = "PNG image (*.png)|*.png",
            // Only spaces used to be dealt with here, so a class named with a "/" or ":"
            // got a suggested file name Windows won't take. See PlainFileName.SafePart.
            FileName = $"heat-sheet-{PlainFileName.SafePart(className, "class").Replace(' ', '-')}-{DateTime.Now:yyyy-MM-dd}.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            // Every round, over as many pages as it takes - one image used to
            // hold what fit on a page and leave the rest off without a word.
            var pages = HeatSheetRenderer.RenderPages(
                className,
                rounds,
                _profiles,
                ClassByName(className)?.Colour ?? RaceClass.DefaultColourFor(className));

            if (PageFiles.SavePages(this, pages, dialog.FileName) is not { } written)
            {
                return;
            }

            AppDialog.Show(this, $"{PageFiles.Describe(written)}\n\nOpen and print {(written.Count == 1 ? "it" : "each")} at full page size.",
                "Heat sheet saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't create the heat sheet: {ex.Message}", "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>
/// The Program tab's answer to "can this be done, and what would it do?" -
/// or, after doing it, what was done. The desk shows Message in a dialog
/// titled Title; the phone (RaceDayService) shows the same words. Ok false
/// means nothing was, or would be, changed.
/// </summary>
/// <param name="Offer">
/// On a refusal, the one thing that WOULD get the user moving, phrased as
/// a question - "Add a final by hand instead?". The desk turns it into a
/// Yes/No; a refusal with no offer is still shown with a plain OK, which
/// is right when there is genuinely nothing to press (waiting for a race
/// to finish). Null by default so every existing answer is unaffected.
/// </param>
public sealed record ProgramAnswer(bool Ok, string Title, string Message, string? Offer = null);

/// <summary>
/// What bump-ups would do for a class. Ok false: nothing is ready (Message
/// says why). NothingToMove: Target is ready but has nobody left to promote,
/// so the only step is to mark it done. Otherwise Message is the question
/// to ask before bumping Feeder's top finishers up into Target.
/// </summary>
public sealed record BumpUpsAnswer(bool Ok, string Title, string Message, RoundDefinition? Target, RoundDefinition? Feeder, bool NothingToMove, string? Offer = null);
