namespace HeatCaller;

/// <summary>
/// Mark which driver profiles are actually present for today's event.
/// The Day can
/// optionally limit heat generation to only checked-in drivers, so a
/// roster that includes everyone who's ever raced a class doesn't get used
/// wholesale on a day when half of them didn't show up.
/// </summary>
public sealed partial class CheckInControl : UserControl
{
    private readonly ProfileStore _profiles;
    private readonly CheckInStore _checkIns;
    private readonly RaceClassStore _raceClasses;
    private readonly AppSettings _settings;
    private readonly TechInspectionStore? _tech;

    /// <summary>Who paid tonight (11 Nov 2026, Phase 4 - see DeskMoney). Null: no Paid column.</summary>
    private readonly EntryPaymentStore? _payments;

    /// <summary>
    /// Who had raced here before tonight, for the "first night" badge (15 Nov
    /// 2026, bug hunt 21 - see NewDrivers.FirstNight). Null with no history
    /// given: nobody is badged as on their first night.
    /// </summary>
    private readonly NewDrivers.RacedBeforeKept? _racedBefore;

    /// <summary>
    /// The race days, so this page can say when it is filing check-ins
    /// against nothing.
    ///
    /// Optional only so a caller that has no store still gets the page;
    /// the app always passes one. Without it this page was happy to take
    /// twelve ticks for a race day that did not exist and say nothing
    /// about it - the Program tab's banner was the only warning, and it is
    /// a tab further on than this one.
    /// </summary>
    private readonly RaceEventStore? _events;

    /// <summary>The "no race day is open" banner, hidden whenever one is.</summary>
    private Section? _pnlNoDay;

    /// <summary>"Entries closed at 18:30..." once the race day's entries have closed (EntriesClose.LateLine).</summary>
    private readonly Label _lblEntriesClosed = new();

    /// <summary>Whether that line is shown - kept here for the reason _noDayBannerShown is.</summary>
    private bool _entriesClosedShown;

    /// <summary>"12 said they're coming · 9 here · still to come: ..." (setup plan C1) - ComingTonight.Line.</summary>
    private readonly Label _lblComing = new();

    private bool _comingShown;

    /// <summary>Who said from their phone they're coming tonight (setup plan C1). Set by MainForm; null shows nothing.</summary>
    internal Func<IReadOnlyList<PreEntry>>? ComingTonightList { get; set; }

    /// <summary>
    /// The banner's two lines, held so it can say which of its two things
    /// is wrong: no race day at all, or a current day that is closed - see
    /// RefreshNoDayBanner.
    /// </summary>
    private Label? _lblNoDayHeadline;
    private Label? _lblNoDayDetail;

    private const string NoDayHeadline = "No race day is open.";
    private const string NoDayDetail =
        "Check-ins are filed against a race day, so ticks made now are not kept with tonight's racing "
        + "and the heats cannot be built from them. Start one and this page starts counting.";
    private const string ClosedDayHeadline = "This day is closed.";

    /// <summary>
    /// Whether the banner is MEANT to be showing.
    ///
    /// Held separately because Control.Visible reports effective
    /// visibility: on a page that is not the one being shown it answers
    /// false however the banner was set, and deciding whether to show it
    /// from that got the answer wrong. Starts false, which is how
    /// BuildNoDayBanner builds the card.
    /// </summary>
    private bool _noDayBannerShown;

    /// <summary>Raised when the desk asks, from the banner, for a race day to start. The host owns making one.</summary>
    public event Action? StartRaceDayRequested;

    /// <summary>"Desk View..." - lock this laptop to Who's Racing for a volunteer (setup plan D8).</summary>
    public event Action? DeskViewRequested;

    /// <summary>
    /// Optional, and only ever used to build the self check-in poster - a
    /// club that never starts the web server still gets the whole of this
    /// page, minus one button that would have nothing to point at.
    /// </summary>
    private readonly WebServerService? _web;

    /// <summary>Raised when the user asks to inspect a driver from this page. The host owns opening the actual Tech Inspection form.</summary>
    public event Action<DriverProfile>? InspectRequested;

    /// <summary>
    /// Raised when the user asks to log one inspection for several drivers at
    /// once - picked with Ctrl or Shift, then a double-click on one of them or
    /// Inspect Selected Cars (1 Nov 2026). In list order. The host owns the
    /// window and filing the inspections, as it does for one.
    /// </summary>
    public event Action<IReadOnlyList<DriverProfile>>? InspectManyRequested;

    /// <summary>
    /// Raised for each driver whose check-in was changed HERE, at the desk -
    /// a tick, a scan, a bulk button, an undo.
    ///
    /// The host keeps the time, so a tap from a driver's phone that was made
    /// before the desk's change but arrives after it (the relay is polled
    /// every few seconds) does not quietly undo what the desk just did.
    /// </summary>
    public event Action<string>? CheckInChangedAtDesk;

    /// <summary>Raised when the desk asks to see the drivers who have signed up online. The host owns the Online Sign-ups window.</summary>
    public event Action? SignupsRequested;

    /// <summary>
    /// Raised when the desk asks to see the requests drivers have sent from
    /// their own phones (22 Oct 2026, driver experience). The host owns the
    /// Driver Requests window (MainForm.DriverRequests).
    /// </summary>
    public event Action? DriverRequestsRequested;

    /// <summary>
    /// Raised when the scrutineer asks for the full inspection log. The host
    /// owns the window.
    ///
    /// Scrutineering used to be a tab of its own, sitting at the end of Race
    /// Day. The gate itself was never there - it is the Tech column on this
    /// page, per driver and per class - so what that tab held was a record
    /// list that a scrutineer opens a few times an evening and nobody else
    /// opens at all. It is a drawer off this page now, and Race Day is four
    /// tabs instead of six.
    /// </summary>
    public event Action? TechLogRequested;

    /// <summary>The Online Sign-ups button, held so its caption can say how many are waiting.</summary>
    private readonly Button _btnSignups = new() { Text = "Online Sign-ups (00)" };

    /// <summary>
    /// The Driver Requests button, beside Online Sign-ups, held so its
    /// caption can say how many are waiting. Hidden until the host says the
    /// club takes requests or some are waiting (22 Oct 2026, driver experience).
    /// </summary>
    private readonly Button _btnDriverRequests = new() { Text = "Driver Requests (00)", Visible = false };
    private readonly UndoService _undo;

    private readonly RosterGrid _grid = new();

    /// <summary>Inspect Selected Car, held so its caption can say "Cars" while more than one row is picked.</summary>
    private readonly Button _btnInspect = new() { Text = InspectOne };

    // No "..." on the end: the button layer takes it off every caption
    // (AppTheme.Caption), so with it these never matched what the button
    // said and each change of selection set the caption again.
    private const string InspectOne = "Inspect Selected Car";
    private const string InspectMany = "Inspect Selected Cars";

    /// <summary>
    /// The driver under the first click of the latest click or double-click,
    /// and whether they were checked in before it - see <see cref="ActOnMany"/>,
    /// which puts the tick back when that click landed on a Checked In cell.
    /// </summary>
    private (string Transponder, bool WasIn)? _tickBeforeClick;

    /// <summary>
    /// The count, and the rule the heats are built by - see UpdateSummary.
    /// As tall as its longest form at the width it has, so the list under
    /// it does not hop as drivers are ticked in.
    /// </summary>
    private readonly SteadyLabel _lblSummary = new()
    {
        Wordings = new[]
        {
            LongestCount + RuleClubNightCheckedIn,
            LongestCount + RuleCheckedIn,
            LongestCount + RuleClubNightEveryone,
            LongestCount + RuleEveryone,
        },
    };

    // What the summary says after the count - which way the day builds its
    // heats. See UpdateSummary.
    private const string RuleClubNightCheckedIn = "   —   tonight's races are built from checked-in drivers only";
    private const string RuleCheckedIn = "   —   heats are built from checked-in drivers only";
    private const string RuleClubNightEveryone = "   —   everyone in a racing class is in tonight's races, checked in or not";
    private const string RuleEveryone = "   —   everyone in a racing class is in the heats, checked in or not";

    /// <summary>The count at its longest, for the summary's height - see _lblSummary.</summary>
    private const string LongestCount = "999 of 999 checked in (999 total checked in across all classes)";

    private readonly TextBox _txtScan = new();
    private readonly Label _lblScanStatus = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    private bool _suppressGridEvents;

    public CheckInControl(ProfileStore profiles, CheckInStore checkIns, RaceClassStore raceClasses, AppSettings settings, UndoService undo,
        TechInspectionStore? tech = null, WebServerService? web = null, RaceEventStore? events = null,
        BatteryPackStore? batteries = null, EntryPaymentStore? payments = null, SessionHistoryStore? history = null)
    {
        _racedBefore = history is null ? null : new NewDrivers.RacedBeforeKept(() => history.All);
        // The packs are not this page's any more (13 Nov 2026): Asher had the
        // Pack column taken off - which pack is in a car is nothing to do with
        // who is here and ready. The store is still taken, so the callers
        // need not change; Driver Hub and Race Control still pick packs.
        _ = batteries;
        _payments = payments;
        _web = web;
        _events = events;
        _profiles = profiles;
        _checkIns = checkIns;
        _raceClasses = raceClasses;
        _settings = settings;
        _tech = tech;
        _undo = undo;

        Dock = DockStyle.Fill;

        // The body face, worn rather than inherited: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise take.
        // No MinimumSize: the page scrolls when the window is too short for
        // it (FillPage), rather than running off the bottom of its tab.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchPause.Dispose();
        }

        base.Dispose(disposing);
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
    /// Builds the page on the layout pieces (LayoutPieces.cs): the intro,
    /// the no-day banner when there is no race day, and then the roster
    /// card - its filter row, the grid taking the height that is left, the
    /// buttons under it - beside the scan card. Nothing here is placed by
    /// hand.
    ///
    /// It used to be. Every row flowed from the one above it by measuring,
    /// the grid, the scan panel and the button row were pinned to the edges
    /// on every resize, and the whole filter row was moved down by hand
    /// when the banner appeared - three authorities for one page, and a
    /// 910x560 floor under it that ran the buttons off the bottom of a
    /// short window. A short window now scrolls the page instead, and the
    /// grid never goes below its headings and three drivers.
    /// </summary>
    private void BuildLayout()
    {
        // "Race Program" was a page name that no longer exists - the
        // programme is the lower half of Today, and the setting that limits
        // the build to checked-in drivers is on The Day.
        //
        // "what it builds", not "heat building": this page is the same page
        // on a club night, where the programme is races rather than heats,
        // and this text is written once for both.
        //
        // 13 Nov 2026 (the pit board rework): one line. It used to explain
        // the Tech column, the tech filter and the log in a paragraph; the
        // strip and the chips say that now by being there.
        var lblIntro = new Label
        {
            Text = "Who's here tonight, and who's ready to race. Click a card to see who is still waiting at that gate. " +
                   "Whether tonight's program uses checked-in drivers only is set on the Setup tab.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _pnlNoDay = BuildNoDayBanner();

        // --- The filters ---
        //
        // A chip per class with how many race it, in place of the drop-down
        // that hid them; a chip per gate, in place of "Only those not through
        // tech" (which only knew about tech); and the search box at the end
        // of the gate chips, which is where a USB scanner types too - a scan
        // checks the driver in, anything else typed narrows the list.
        BuildFilters();

        _txtScan.PlaceholderText = "Find a driver, or scan a QR check-in";
        _txtScan.KeyDown += TxtScan_KeyDown;

        var gateRow = new FieldRow();

        gateRow.Add(null, _showChips, () => _showChips.GetPreferredSize(Size.Empty).Width);
        gateRow.AddLast(null, _txtScan, () => UiLayout.AtTextSize(240));

        // The scan line: what the last scan, tick or bulk change did. In the
        // roster's bottom-left corner, which the picked bar takes while two
        // or more drivers are picked.
        _lblScanStatus.AutoSize = true;
        _lblScanStatus.Text = string.Empty;
        _lblScanStatus.Padding = new Padding(0, UiLayout.AtDpi(9), 0, 0);
        _textSize.Wear(_lblScanStatus, () => AppTheme.BoldFont);

        // On a line of its own under the filter, wrapping to the card.
        //
        // It carries the check-in RULE as well as the count - more than fits
        // beside the filter at almost any size - and it used to sit there
        // anyway, bounded by what was left of the row and ended with "...":
        // the rule is the half that was cut, and a rule cut off is not said.
        _lblSummary.ForeColor = AppTheme.TextMuted;

        // --- The roster ---
        _grid.ReadOnly = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        // Ctrl+click adds a driver to the selection and Shift+click a run of
        // them, so a double-click on any of the picked rows can check them
        // all in, or log one inspection for all of them (1 Nov 2026). With
        // Ctrl or Shift held, a click only picks - see Grid_CellClick.
        _grid.MultiSelect = true;
        _grid.ClickStarting += OnGridClickStarting;

        // Right-click anywhere on the list: the menu for the drivers picked
        // (13 Nov 2026) - see OnRowMenuRequested.
        _grid.RowMenuRequested += OnRowMenuRequested;
        _grid.SelectionChanged += (_, _) =>
        {
            ShowInspectCaption();
            ShowPickedBar();
        };
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewCheckBoxColumn { Name = "checkedIn", HeaderText = "Here", FillWeight = 16 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "class", HeaderText = "Classes", FillWeight = 55, ReadOnly = true }
        );

        if (_tech is not null)
        {
            // A driver running two classes can be legal in one car and not
            // the other, so this reads per (driver, class) rather than
            // showing one status per person - the same scoping tech
            // inspection itself uses, so the two pages can't disagree.
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "tech", HeaderText = "Tech", FillWeight = 32, ReadOnly = true });
        }

        if (_payments is not null)
        {
            // Money at the desk (11 Nov 2026): shown only on a race day with an
            // entry fee - see RefreshDriverList. Right-click to settle.
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "paid", HeaderText = "Payment", FillWeight = 38, ReadOnly = true, Visible = false });
        }

        // Papers (12 Nov 2026, setup plan D9): shown only while the club
        // checks waivers or memberships - see RefreshDriverList. Click to record.
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "papers", HeaderText = "Papers", FillWeight = 42, ReadOnly = true, Visible = false });

        // Ready (13 Nov 2026): a bar per gate and how many are done, or
        // "Ready" once the driver is through them all - see ShowStatus.
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ready", HeaderText = "Ready", FillWeight = 30, ReadOnly = true });
        _grid.CellPainting += PaintRosterCell;
        _grid.CellClick += Grid_CellClick;
        _grid.CellDoubleClick += Grid_CellDoubleClick;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                // A Ctrl or Shift click on the box itself only picks the row,
                // as anywhere else in the cell does (Grid_CellClick): the
                // box's own toggle is thrown away rather than saved.
                if ((ModifierKeys & (Keys.Control | Keys.Shift)) != 0)
                {
                    _grid.CancelEdit();
                    _grid.RefreshEdit();
                    return;
                }

                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _grid.CellValueChanged += Grid_CellValueChanged;

        // Somebody at the desk who is not on file yet - the same small window
        // the Set Up Tonight wizard's Drivers step opens.
        var btnAddDriver = new Button { Text = "Add Driver..." };
        btnAddDriver.Click += (_, _) => AddDriver();
        AppTheme.StyleButton(btnAddDriver, AppTheme.ButtonKind.Primary);

        // Tonight's entry list - RCSignup, LiveTime, LiveRC or any CSV -
        // matched onto the drivers here and checked in (11 Nov 2026, build
        // plan 7.1). A menu under the button: from a file, or pasted. See
        // CheckInControl.Entries.cs.
        var btnEntries = new Button { Text = "Import Entries..." };
        var entriesMenu = BuildEntriesMenu();
        btnEntries.Click += (_, _) => entriesMenu.Show(btnEntries, new Point(0, btnEntries.Height));

        // One pink button on the page, Add Driver (13 Nov 2026): the theme's
        // own rule made this one pink too.
        AppTheme.StyleButton(btnEntries, AppTheme.ButtonKind.Secondary);

        // Named for what it acts on. "Cars" while more than one row is
        // picked: then it logs one inspection for all of them - see
        // RequestInspection. It lives on the picked bar now; one car is
        // inspected from its Tech cell, or from More.
        _btnInspect.Click += (_, _) => RequestInspection();

        _btnSignups.Click += (_, _) => SignupsRequested?.Invoke();
        _btnDriverRequests.Click += (_, _) => DriverRequestsRequested?.Invoke();

        // Its opening caption. The bar it goes in sizes it to whatever it
        // says, and again every time the count changes.
        SetSignupCount(0);

        // The commands for the night, against the right-hand edge: Add Driver
        // furthest right, then the ways drivers arrive, then More for the
        // rest (13 Nov 2026 - nine buttons of one size became three, a count
        // or two, and a menu).
        var actions = new ButtonBar(alignRight: true).With(btnAddDriver, btnEntries, _btnSignups, _btnDriverRequests);

        // Money at the desk: the slip a double-click on a name adds to.
        if (_payments is not null)
        {
            _btnDriverPaid.Click += (_, _) => OpenDriverPaid();
            actions.With(_btnDriverPaid);
        }

        actions.With(BuildMoreButton());

        var roster = new Section("Drivers");

        roster.Add(_classChips);
        roster.Add(gateRow);
        roster.Add(_lblSummary);
        roster.AddFill(_grid, LeastGridHeight);
        roster.Add(new SplitBar(BuildPickedBar(), actions), gapBelow: 0);

        // --- Desk mode (mock-up D) ---
        var desk = BuildDesk();

        // The two views share the room under the strip; the switch shows one.
        _views.Controls.Add(roster);
        _views.Controls.Add(desk);
        roster.Dock = DockStyle.Fill;
        desk.Dock = DockStyle.Fill;
        desk.Visible = false;
        _rosterView = roster;
        _deskView = desk;

        // --- The page ---
        var page = new FillPage();

        // Roster or Desk mode (13 Nov 2026): the switch is the first thing
        // on the page, so it is where it always is in both.
        _modeSwitch.OnChanged += (_, _) => ShowDeskMode(_modeSwitch.On);
        _modeSwitch.AutoSize = true;
        _modeSwitch.AccessibleDescription = "Who's Racing view";

        page.Add(_modeSwitch, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);

        // In the flow rather than floating over the page - a warning that
        // covered the class filter would be its own bug - and taking no room
        // at all while it is hidden.
        page.Add(_pnlNoDay, gapBelow: UiLayout.RowGap);

        // Entries closed (12 Nov 2026, setup plan D1): anyone ticked in now
        // is a late entry. Hidden until the close has passed.
        _lblEntriesClosed.AutoSize = true;
        _lblEntriesClosed.ForeColor = AppTheme.Warning;
        _lblEntriesClosed.Visible = false;
        page.Add(_lblEntriesClosed, gapBelow: UiLayout.RowGap);

        // Who said they're coming tonight (12 Nov 2026, setup plan C1).
        _lblComing.AutoSize = true;
        _lblComing.ForeColor = AppTheme.TextMuted;
        _lblComing.Visible = false;
        page.Add(_lblComing, gapBelow: UiLayout.RowGap);

        // The readiness strip (13 Nov 2026): one card per gate, over both views.
        _textSize.Keep(_strip.Remeasure);
        page.Add(_strip, gapBelow: UiLayout.RowGap);
        page.AddFill(_views, ViewsLeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>The least height the roster's grid can do with: its headings and three drivers. Below that the page scrolls instead - see FillPage.</summary>
    private int LeastGridHeight() =>
        _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height;

    /// <summary>
    /// "No race day is open", and one press that opens one.
    ///
    /// This page has no empty state of its own otherwise: whatever the
    /// day's state it shows a full, working grid, and the only thing that
    /// ever said a race day was missing was the next-step card, one tab away.
    /// So the order of a first evening was: find Entries, tick everyone in, go
    /// one tab further, and read that none of it had been filed anywhere.
    /// </summary>
    private Section BuildNoDayBanner()
    {
        var headline = _textSize.Wear(
            new Label
            {
                Text = NoDayHeadline,
                AutoSize = true,
                ForeColor = AppTheme.Warning,
            },
            () => AppTheme.HeadingFont);

        var detail = new Label
        {
            Text = NoDayDetail,
            AutoSize = true,
            ForeColor = AppTheme.TextDark,
        };

        // Rewritten by RefreshNoDayBanner when it is a closed day showing.
        _lblNoDayHeadline = headline;
        _lblNoDayDetail = detail;

        var start = new Button { Text = "Start Tonight..." };
        start.Click += (_, _) => StartRaceDayRequested?.Invoke();

        AppTheme.StyleButton(start, AppTheme.ButtonKind.Primary);

        // Hidden until RefreshNoDayBanner says otherwise - see _noDayBannerShown.
        var banner = new Section { Visible = false };

        banner.Add(headline, gapBelow: UiLayout.HintGap);
        banner.Add(detail);
        banner.Add(new ButtonBar().With(start), gapBelow: 0);

        return banner;
    }

    /// <summary>
    /// Shows the banner only when check-ins have nowhere real to go - or
    /// when the current day is closed.
    ///
    /// The legacy holder counts as nowhere: it is the cabinet everything
    /// recorded before race days existed lives in, not a day anybody is
    /// running.
    ///
    /// A closed day used to count as somewhere. Picking last week's day to
    /// look at its results leaves it current, and this page then showed a
    /// full working grid with no warning: tonight's arrivals were ticked
    /// onto last week's sheet. It now says the day is closed, and the desk,
    /// the phones and the relay refuse check-ins onto it.
    /// </summary>
    private void RefreshNoDayBanner()
    {
        if (_pnlNoDay is not { } panel)
        {
            return;
        }

        var closed = CheckInStore.ClosedCurrentDay(_events);
        var open = _events?.Active is { IsLegacy: false };
        var wanted = _events is not null && (!open || closed is not null);

        // Which of the two it says, every time - the banner can stay up
        // while what is wrong changes from one to the other.
        if (_lblNoDayHeadline is not null && _lblNoDayDetail is not null)
        {
            _lblNoDayHeadline.Text = closed is null ? NoDayHeadline : ClosedDayHeadline;
            _lblNoDayDetail.Text = closed is null ? NoDayDetail : ClosedDayDetail(closed);
        }

        // And the ticks cannot be changed by hand while it is closed. The
        // click handler refuses too (Grid_CellClick); this stops the space
        // bar getting round it.
        if (_grid.Columns["checkedIn"] is { } tickColumn)
        {
            tickColumn.ReadOnly = closed is not null;
        }

        // Against the field, not against panel.Visible. That getter answers
        // EFFECTIVE visibility - false for everything on a page that is not
        // the one being shown - so on a hidden page it read "hidden" however
        // the banner was set. Turning it on then fell through to a reflow
        // that read the same false and moved the filter row back UP over the
        // banner, and the next call, once the page was showing, saw
        // Visible == wanted and returned without ever putting it right.
        if (_noDayBannerShown == wanted)
        {
            return;
        }

        _noDayBannerShown = wanted;

        // Everything under it moves with it: the banner is one of the page's
        // parts, and a part hidden takes no room (FillPage).
        panel.Visible = wanted;
    }

    /// <summary>
    /// Checks in everyone the class filter is currently showing.
    ///
    /// Public because the Today tab's checklist offers the same thing, and
    /// a second implementation would be a second place to forget that a
    /// desk tick has to be timed - see CheckInChangedAtDesk, which is what
    /// stops a slower tap from a phone undoing it.
    /// </summary>
    public void CheckInEveryoneShown()
    {
        // Not onto a closed day - see CheckInStore.ClosedCurrentDay.
        if (RefusedOnClosedDay())
        {
            return;
        }

        var shown = CurrentlyShownProfiles().Select(p => p.Transponder).ToList();

        var newlyIn = shown.Count(t => !_checkIns.IsCheckedIn(t));

        // A bulk tick, often before anyone has come - not doors open (bug
        // hunt 20, see CheckInStore.CheckInAll).
        _checkIns.CheckInAll(shown, opensDoors: false);
        RaiseDeskChanges(shown);
        RefreshDriverList();

        if (newlyIn > 0)
        {
            LogDesk(Plural.Of(newlyIn, "driver"), "checked in together");
        }
    }

    /// <summary>Shows how many online sign-ups are waiting for the desk on the button that opens them.</summary>
    public void SetSignupCount(int waiting)
    {
        // The bar it is in re-measures it: the caption gets longer or
        // shorter with the count, and the button with it.
        _btnSignups.Text = waiting > 0 ? $"Online Sign-ups ({Math.Min(waiting, 99)})" : "Online Sign-ups";
        _btnDeskSignups.Text = _btnSignups.Text;
    }

    /// <summary>
    /// How many driver requests are waiting, on the button that opens them,
    /// and whether the button is on the page at all - only while the club
    /// takes requests or some are still waiting (22 Oct 2026, driver
    /// experience). Called on every roster tick; changes nothing when
    /// nothing changed.
    /// </summary>
    public void SetDriverRequestCount(int waiting, bool shown)
    {
        var text = waiting > 0 ? $"Driver Requests ({Math.Min(waiting, 99)})" : "Driver Requests";

        if (!string.Equals(_btnDriverRequests.Text, text, StringComparison.Ordinal))
        {
            _btnDriverRequests.Text = text;
        }

        if (_btnDriverRequests.Visible != shown)
        {
            _btnDriverRequests.Visible = shown;
        }
    }

    /// <summary>
    /// USB barcode/QR scanners act as keyboards: they type the code's
    /// contents then press Enter. So handling Enter here is all that's
    /// needed to support any standard scanner, with no drivers, SDK, or
    /// device configuration involved.
    /// </summary>
    private void TxtScan_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true; // stop the Windows "ding" on Enter in a single-line textbox

        HandleEntry(_txtScan, fromDesk: false);
    }

    /// <summary>
    /// Enter in a scan box - the roster's search, or Desk mode's big box. A
    /// QR check-in checks its driver in. Anything else is a name: one driver
    /// it can only mean is checked in; more than one, and the box keeps the
    /// words so more can be typed (13 Nov 2026 - the search box and the
    /// scanner are one box now).
    /// </summary>
    internal void HandleEntry(TextBox box, bool fromDesk)
    {
        var typed = box.Text.Trim();

        if (string.IsNullOrWhiteSpace(typed))
        {
            return;
        }

        var transponder = QrBadgeRenderer.ParsePayload(typed);

        if (transponder is not null)
        {
            box.Clear();

            var scanned = _profiles.All.FirstOrDefault(p => string.Equals(p.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

            if (scanned is null)
            {
                SetScanStatus($"No driver profile found for transponder {transponder}.", AppTheme.Danger);
                return;
            }

            CheckInAtDesk(scanned, "by scan");
            return;
        }

        // An online badge whose id no driver here has any more - printed
        // before Reissue All Badges on the Club Website page, with another
        // laptop's key, or for a driver since deleted.
        if (RelayCheckIn.IdFromScan(typed) is not null)
        {
            box.Clear();
            SetScanStatus("That QR check-in is out of date - print a new one.", AppTheme.Danger);
            return;
        }

        var matches = DriversNamed(typed);

        switch (matches.Count)
        {
            case 0:
                SetScanStatus($"No driver called \"{typed}\". Add Driver puts somebody new on the list.", AppTheme.Danger);
                break;

            case 1:
                box.Clear();
                CheckInAtDesk(matches[0], "at the desk");
                break;

            default:
                SetScanStatus($"{matches.Count} drivers match \"{typed}\" - type more of the name.", AppTheme.Warning);

                // The roster shows them at once, rather than after the pause.
                if (!fromDesk)
                {
                    _searchPause.Stop();
                    ApplySearch();
                }

                break;
        }
    }

    /// <summary>
    /// The drivers a typed name can mean: the one whose name it is, or else
    /// everyone whose name or transponder has it in it.
    /// </summary>
    internal List<DriverProfile> DriversNamed(string typed)
    {
        var exact = _profiles.All
            .Where(p => string.Equals((p.DriverName ?? string.Empty).Trim(), typed, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p.Transponder, typed, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return exact.Count > 0 ? exact : _profiles.All.Where(p => Matches(p, typed)).ToList();
    }

    /// <summary>
    /// Checks one driver in from the desk - a scan, or a name typed - and
    /// puts them on Desk mode's card, with what they still need.
    /// </summary>
    private void CheckInAtDesk(DriverProfile profile, string how)
    {
        if (_checkIns.IsCheckedIn(profile.Transponder))
        {
            ShowOnDesk(profile, "Already checked in", AppTheme.Accent);
            SetScanStatus($"{DisplayName(profile)} was already checked in.", AppTheme.TextMuted);
            return;
        }

        // Said on the status line rather than in a window, so a queue at the
        // scanner is not held up by a box to click - see ClosedCurrentDay.
        if (CheckInStore.ClosedCurrentDay(_events) is { } closedDay)
        {
            SetScanStatus($"Not checked in: this day is closed (\"{closedDay.DisplayName}\"). Start tonight's race day first.", AppTheme.Danger);
            return;
        }

        _checkIns.SetCheckedIn(profile.Transponder, true);
        CheckInChangedAtDesk?.Invoke(profile.Transponder);
        LogDesk(DisplayName(profile), $"checked in {how}");
        ShowOnDesk(profile, $"Checked in · {DateTime.Now:t}", AppTheme.Success);
        RefreshDriverList();
        SetScanStatus($"Checked in: {DisplayName(profile)}", AppTheme.Success);
    }
    /// <summary>
    /// Adds somebody new and checks them in, from the small window the
    /// setup wizard uses - name, transponder and class, checked before it
    /// closes. Checked in straight away, the same as a scan: they are
    /// standing at the desk.
    /// </summary>
    private void AddDriver()
    {
        // Asked before the window opens: the add checks them in, and not
        // onto a closed day - see CheckInStore.ClosedCurrentDay.
        if (RefusedOnClosedDay())
        {
            return;
        }

        var classes = _raceClasses.All.Select(c => c.Name).ToList();

        // Starts on the class being filtered to - somebody working through
        // Stock who meets a new face is adding a Stock driver.
        var startClass = ClassFilter;

        using var form = new QuickDriverForm(classes, startClass, QuickDriverForm.OwnerNameIn(_profiles));

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // TryAdd, not Upsert: a create, refused rather than saved over
        // anybody - the backstop if the window's check let a clash by.
        if (!_profiles.TryAdd(QuickDriverForm.NewProfile(form), out var refusal))
        {
            AppDialog.Show(this, refusal, "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _checkIns.SetCheckedIn(form.Transponder, true);
        CheckInChangedAtDesk?.Invoke(form.Transponder);

        // A filter that would hide them is taken off, so the person just
        // added is on screen: a class they are not in, a gate they are
        // through, or a search for somebody else.
        if (ClassFilter is { } filter && !string.Equals(filter, form.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            _classFilter = null;
            _classChips.Picked = string.Empty;
        }

        if (_show is RosterShow.NotHere or RosterShow.Ready)
        {
            _show = RosterShow.Everyone;
        }

        if (_search.Length > 0)
        {
            _txtScan.Clear();
            _search = string.Empty;
        }

        RefreshAll();
        LogDesk(form.DriverName, "added and checked in");

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is DriverProfile shown
                && string.Equals(shown.Transponder, form.Transponder, StringComparison.OrdinalIgnoreCase))
            {
                _grid.ClearSelection();
                row.Selected = true;

                if (row.Index >= 0 && row.Index < _grid.RowCount)
                {
                    _grid.FirstDisplayedScrollingRowIndex = row.Index;
                }

                break;
            }
        }

        SetScanStatus($"Added and checked in: {form.DriverName}", AppTheme.Success);
    }

    /// <summary>What the banner says under "This day is closed."</summary>
    private static string ClosedDayDetail(RaceEvent day) =>
        $"\"{day.DisplayName}\" is closed. This page is showing who was checked in that day, and it will not take "
        + "check-ins or check-outs: start tonight's race day, or reopen this one on Race Day > Setup, to change them.";

    /// <summary>
    /// Says no, in a window, when the current day is closed - see
    /// CheckInStore.ClosedCurrentDay. True when it refused.
    ///
    /// Owned by the window this page is in rather than the page itself: the
    /// Today tab calls CheckInEveryoneShown while this page is not on screen.
    /// </summary>
    private bool RefusedOnClosedDay()
    {
        if (CheckInStore.ClosedCurrentDay(_events) is not { } day)
        {
            return false;
        }

        AppDialog.Show(FindForm() ?? (IWin32Window)this,
            $"\"{day.DisplayName}\" is closed, so its check-in sheet can't be changed - it is the record of who was there that day.\r\n\r\n"
            + "For tonight, start a new race day. To change that day's sheet, reopen it on Race Day > Setup first.",
            "This day is closed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }

    private static string DisplayName(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;

    private void SetScanStatus(string message, Color color)
    {
        _lblScanStatus.Text = message;
        _lblScanStatus.ForeColor = color;

        // Desk mode's own line, under its scan box.
        _lblDeskStatus.Text = message;
        _lblDeskStatus.ForeColor = color;
    }

    /// <summary>
    /// The wall poster that sends a phone to the self check-in page.
    ///
    /// Refuses on a stopped server rather than printing a code that leads
    /// nowhere, the same way the Local Screens page's posters do - a QR that
    /// fails at the door is worse than a missing one, because somebody has
    /// to work out why.
    /// </summary>
    private void ShowSelfCheckInPoster()
    {
        if (_web is null || !_web.IsRunning)
        {
            AppDialog.Show(this,
                "Start the web server first - on the Local Screens page - and there will be an address to put in the code. "
                + "Everyone's phone has to be on the same Wi-Fi as this laptop.",
                "Web server is not running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Shown non-modally, so it can sit open on a second screen at the
        // door while the race carries on behind it.
        var poster = new JoinPosterForm(
            _web.LocalUrl() + "checkin",
            "CHECK IN",
            "Scan, find your name, tap it. Same Wi-Fi as the timing laptop.");

        poster.Show(this);
    }

    private void GenerateBadges()
    {
        var drivers = CurrentlyShownProfiles();
        if (drivers.Count == 0)
        {
            AppDialog.Show(this, "No drivers to make QR check-ins for - add driver profiles first, or widen the class filter.",
                "Nothing to print", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var pageCount = (int)Math.Ceiling(drivers.Count / (double)QrBadgeRenderer.BadgesPerPage);

        using var dialog = new SaveFileDialog
        {
            Title = "Save QR check-in sheet",
            Filter = "PNG image (*.png)|*.png",
            FileName = "qr-check-ins.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(dialog.FileName)!;
            var baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
            var written = new List<string>();

            // The dialog only asked about the file it was given; the pages
            // named from it are checked here - see PageFiles.
            var paths = Enumerable.Range(0, pageCount)
                .Select(page => pageCount == 1 ? dialog.FileName : Path.Combine(directory, $"{baseName}-page{page + 1}.png"))
                .ToList();

            if (!PageFiles.ConfirmReplacing(this, paths, dialog.FileName))
            {
                return;
            }

            for (var page = 0; page < pageCount; page++)
            {
                var slice = drivers
                    .Skip(page * QrBadgeRenderer.BadgesPerPage)
                    .Take(QrBadgeRenderer.BadgesPerPage)
                    .ToList();

                // One file per page rather than one enormous image, so each
                // maps directly onto a sheet of paper when printed.
                var path = paths[page];

                using var bitmap = QrBadgeRenderer.RenderPage(slice);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                written.Add(Path.GetFileName(path));
            }

            AppDialog.Show(this,
                $"Saved {Plural.Of(written.Count, "QR check-in sheet")} ({Plural.Of(drivers.Count, "driver")}) to:\n{directory}\n\n" +
                $"{string.Join("\n", written)}\n\nPrint these, cut them up, and hand them out - scanning one checks that driver in.",
                "QR check-ins saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't create the QR check-in sheets: {ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Call whenever this tab becomes visible - profiles and classes can change elsewhere while it's not active.</summary>

    /// <summary>
    /// The inspection the desk goes by for a driver: across every class they
    /// race that runs tech (TechInspectionStore.LatestForDriver), judged
    /// class by class as Race Control judges a heat. It used to be their
    /// main class only, so a driver cleared there but not in their second
    /// class read as cleared here and was then flagged at the start.
    /// </summary>
    private TechInspection? TechFor(DriverProfile profile) =>
        _tech!.LatestForDriver(profile, JudgesTechFor);

    /// <summary>
    /// The class Inspect Selected Car opens for a driver: the one the Tech
    /// column is showing (TechInspectionStore.TechClassForDriver). It used
    /// to be their main class whatever the column said.
    /// </summary>
    public string InspectClassFor(DriverProfile profile) =>
        _tech?.TechClassForDriver(profile, JudgesTechFor) ?? profile.ClassName;

    /// <summary>
    /// The classes a bulk log inspects a driver in: every one the Tech
    /// column is waiting on (TechInspectionStore.TechClassesForDriver), so
    /// the column reads cleared afterwards rather than moving on to the next.
    /// </summary>
    public IReadOnlyList<string> InspectClassesFor(DriverProfile profile) =>
        _tech?.TechClassesForDriver(profile, JudgesTechFor) ?? new[] { profile.ClassName };

    /// <summary>Shows "Entries closed at..." once the open day's entries have closed, so a tick now is known to be a late entry.</summary>
    private void RefreshEntriesClosed()
    {
        var day = _events?.Active is { IsLegacy: false, IsOpen: true } open ? open : null;
        var line = EntriesClose.LateLine(day, DateTime.UtcNow);
        var wanted = line is not null;

        if (line is not null && _lblEntriesClosed.Text != line)
        {
            _lblEntriesClosed.Text = line;
        }

        if (_entriesClosedShown == wanted)
        {
            return;
        }

        _entriesClosedShown = wanted;
        _lblEntriesClosed.Visible = wanted;
    }

    /// <summary>
    /// Only the classes racing tonight, and only the one picked in Class:
    /// when one is - see RaceControlControl.DeskJudgesTechFor.
    ///
    /// Not in Desk mode (bug hunt 20), where the chips are out of sight: a
    /// chip left picked on the roster, and remembered after a restart, had
    /// the desk say "ready to race" to a driver with a second car not yet
    /// looked at. The roster is redrawn on the way back - see ShowDeskMode.
    /// </summary>
    private bool JudgesTechFor(string className) =>
        RaceControlControl.DeskJudgesTechFor(className, _raceClasses.All, _tech!, _events?.Active,
            _modeSwitch.On ? null : ClassFilter);

    public void RefreshAll()
    {
        RefreshNoDayBanner();
        RefreshEntriesClosed();
        RefreshComing();
        RefreshClassFilter();
        RefreshDriverList();
    }

    /// <summary>
    /// Brings the Checked In ticks and the summary up to date, and nothing
    /// else.
    ///
    /// For a check-in arriving from a phone. RefreshAll rebuilds the class
    /// list and every row, which put the grid back at the top and could
    /// reset the class filter under whoever was working down the list at
    /// the desk - once per phone tap, in the busiest ten minutes of the
    /// day. This touches only the cells whose value actually changed.
    /// </summary>
    public void RefreshCheckInMarks()
    {
        if (IsDisposed)
        {
            return;
        }

        // The race day moved on since the ticks were drawn: they are the old
        // day's, so a difference is the new day, not a phone.
        var newDay = MarksDayChanged();

        _suppressGridEvents = true;

        try
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not DriverProfile profile)
                {
                    continue;
                }

                var isIn = _checkIns.IsCheckedIn(profile.Transponder);

                if (row.Cells[0].Value is not bool shown || shown != isIn)
                {
                    row.Cells[0].Value = isIn;
                    ShowStatus(row);

                    // Not the desk's own change (CheckInDrivers and the rest
                    // say theirs): a driver's phone, or the self check-in page.
                    if (!_deskMarking && !newDay)
                    {
                        LogDesk(DisplayName(profile), isIn ? "checked in from a phone" : "checked out from a phone");
                    }
                }
            }

            // WHY (20 Oct 2026, Bug Hunt 8, #45): the current tick cell is in
            // edit mode and paints from its editing flags, not Value (see
            // Grid_CellClick, A28-X). Writing Value above left those flags
            // stale, so a phone check-in, or the closed-day put-back, kept
            // showing the old tick on that row and the next glyph click wrote
            // the store twice. RefreshEdit re-reads the flags from the new
            // Value without leaving edit mode or raising CellValueChanged.
            if (_grid.IsCurrentCellInEditMode && _grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _grid.RefreshEdit();
            }
        }
        finally
        {
            _suppressGridEvents = false;
        }

        UpdateSummary();
    }

    private void RaiseDeskChanges(IEnumerable<string> transponders)
    {
        if (CheckInChangedAtDesk is null)
        {
            return;
        }

        foreach (var transponder in transponders)
        {
            CheckInChangedAtDesk(transponder);
        }
    }

    /// <summary>Whether the class picked last time has been put back yet - once, when the page is first filled.</summary>
    private bool _classFilterRestored;

    /// <summary>The class chips, rebuilt from the club's classes - and, the first time, the class picked last time put back.</summary>
    private void RefreshClassFilter()
    {
        if (!_classFilterRestored)
        {
            _classFilterRestored = true;
            _classFilter = _settings.LastCheckInClassFilter.Length > 0 ? _settings.LastCheckInClassFilter : null;
        }

        RefreshClassChips();
    }

    /// <summary>
    /// The drivers the roster shows: in the class picked, waiting at the gate
    /// picked, and matching the search.
    ///
    /// "Needs tech" means not cleared, which covers both the car nobody has
    /// looked at and the car that was looked at and failed. Read per (driver,
    /// class), the same scoping the Tech column and tech inspection itself
    /// use, so a driver legal in one car and not the other shows up for the
    /// car that needs work.
    /// </summary>
    private List<DriverProfile> CurrentlyShownProfiles() =>
        InClass()
            .Where(p => _show == RosterShow.Everyone || Shows(_show, ReadinessOf(p)))
            .Where(p => Matches(p, _search))
            .ToList();

    /// <summary>The race day the list's ticks were last drawn for (see MarksDayChanged).</summary>
    private string? _marksDrawnFor;

    /// <summary>
    /// Whether the race day has changed since the list's ticks were last
    /// drawn - and if so, notes the new one and starts the desk's log afresh.
    ///
    /// WHY (14 Nov 2026, bug hunt 19): starting tonight from another tab
    /// moves the check-ins to the new day without redrawing Who's Racing, so
    /// the next phone check-in found every row's tick "changed" and logged a
    /// "checked in/out from a phone" line for almost every driver - under
    /// the last day's lines, which nothing cleared.
    /// </summary>
    private bool MarksDayChanged()
    {
        var day = _checkIns.CurrentEventId;

        if (string.Equals(day, _marksDrawnFor, StringComparison.Ordinal))
        {
            return false;
        }

        if (_marksDrawnFor is not null)
        {
            _deskLog.Clear();
        }

        _marksDrawnFor = day;
        return true;
    }

    private void RefreshDriverList()
    {
        MarksDayChanged();
        _suppressGridEvents = true;
        try
        {
            _grid.Rows.Clear();
            var before = _racedBefore?.Now(DateTime.Now);

            foreach (var profile in CurrentlyShownProfiles())
            {
                // New to racing (setup plan D12): " · first night", " · novice, mentor Al".
                var displayName = (string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName) + NewDrivers.Badge(profile, before);
                var rowIndex = _grid.Rows.Add(_checkIns.IsCheckedIn(profile.Transponder), displayName, ClassCellText(profile));

                _grid.Rows[rowIndex].Tag = profile;
                ShowPaid(_grid.Rows[rowIndex]);
                ShowPapers(_grid.Rows[rowIndex]);
                ShowStatus(_grid.Rows[rowIndex]);
            }
        }
        finally
        {
            _suppressGridEvents = false;
        }

        // The Paid column only on a race day that charges (11 Nov 2026).
        // Papers only while the club checks them (setup plan D9).
        if (_grid.Columns["papers"] is { } papersColumn)
        {
            papersColumn.Visible = ChecksPapers;
        }

        if (_grid.Columns["paid"] is { } paidColumn)
        {
            paidColumn.Visible = TonightsFees() is { Charges: true };
        }

        RefreshPaidSlip(_payments is not null && TonightsFees() is { Charges: true });

        UpdateSummary();
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        // A double-click on one of several picked rows is about all of them -
        // see BulkTech.RowsToActOn - whichever column it lands in.
        var rows = RowsForDoubleClick(e.RowIndex, ModifierKeys);

        // The Driver Paid slip open: a double-click on a name (or on one of
        // several picked) adds them to it - except on a single row's Tech
        // cell, which still inspects.
        var onTech = _tech is not null && e.ColumnIndex == _grid.Columns["tech"]?.Index;

        if (_paidForm is { IsDisposed: false } && (rows.Count > 1 || !onTech))
        {
            UndoFirstClickTick();
            Reselect(rows);
            AddToPaidSlip(DriversIn(rows));
            return;
        }

        var column = _grid.Columns[e.ColumnIndex].Name;

        if (rows.Count > 1)
        {
            // Several picked: the columns with a window for many open it -
            // one inspection for all the cars, or all of them on the Driver
            // Paid slip. Anywhere else, the menu of what can be done to all
            // of them (the right-click menu).
            if (column is "tech" && _tech is not null)
            {
                UndoFirstClickTick();
                Reselect(rows);
                InspectManyRequested?.Invoke(DriversIn(rows));
            }
            else if (column is "paid" && TonightsFees() is { Charges: true })
            {
                UndoFirstClickTick();
                Reselect(rows);
                OpenDriverPaid();
                AddToPaidSlip(DriversIn(rows));
            }
            else
            {
                ActOnMany(rows);
            }

            return;
        }

        if (_grid.Rows[e.RowIndex].Tag is not DriverProfile profile)
        {
            return;
        }

        // One row (13 Nov 2026): the window for the column double-clicked.
        // Class and Pack open their picker on a single click already, and the
        // second click of the double-click shut it again - so it is opened
        // once more, after that click has been dealt with. Papers' window is
        // up from the first click, and the Checked In box has no window: its
        // click is the tick.
        var rowIndex = e.RowIndex;

        switch (column)
        {
            case "driver":
                if (DriverEditDialog.Edit(this, profile.Transponder, _raceClasses, _undo, _profiles))
                {
                    RefreshDriverList();
                }

                break;

            case "class":
                BeginInvoke(new Action(() => ReopenPicker(rowIndex, profile, PickClasses)));
                break;

            case "tech" when _tech is not null:
                InspectRequested?.Invoke(profile);
                break;

            case "paid" when TonightsFees() is { Charges: true }:
                OpenDriverPaid();
                AddToPaidSlip(new[] { profile });
                break;
        }
    }

    /// <summary>Opens a cell's picker again, while the row is still the driver's.</summary>
    private void ReopenPicker(int rowIndex, DriverProfile profile, Action<int> pick)
    {
        if (!IsDisposed && rowIndex < _grid.Rows.Count && ReferenceEquals(_grid.Rows[rowIndex].Tag, profile))
        {
            pick(rowIndex);
        }
    }

    /// <summary>
    /// The rows a double-click on <paramref name="rowIndex"/> acts on, with
    /// <paramref name="modifiers"/> held: the picked rows as they stood before
    /// its first click, and what Ctrl or Shift added - see BulkTech.RowsToActOn.
    /// </summary>
    internal IReadOnlyList<int> RowsForDoubleClick(int rowIndex, Keys modifiers) =>
        BulkTech.RowsToActOn(
            _grid.SelectedBeforeClick,
            _grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index).ToList(),
            rowIndex,
            ctrl: (modifiers & Keys.Control) != 0,
            shift: (modifiers & Keys.Shift) != 0);

    /// <summary>
    /// The drivers a double-click on <paramref name="rowIndex"/> acts on, in
    /// list order, and those rows picked again - what the menu it opens is
    /// for. For the tests, which cannot double-click.
    /// </summary>
    internal IReadOnlyList<DriverProfile> PickForDoubleClick(int rowIndex, Keys modifiers)
    {
        var rows = RowsForDoubleClick(rowIndex, modifiers);
        Reselect(rows);
        return DriversIn(rows);
    }

    /// <summary>Picks exactly these rows.</summary>
    private void Reselect(IReadOnlyList<int> rows)
    {
        _grid.ClearSelection();

        foreach (var index in rows)
        {
            if (index < _grid.Rows.Count)
            {
                _grid.Rows[index].Selected = true;
            }
        }
    }

    private List<DriverProfile> DriversIn(IReadOnlyList<int> rows) =>
        rows
            .Where(index => index < _grid.Rows.Count)
            .Select(index => _grid.Rows[index].Tag)
            .OfType<DriverProfile>()
            .ToList();

    /// <summary>Remembers who the first click of a click or double-click lands on, and their tick - see _tickBeforeClick.</summary>
    private void OnGridClickStarting(int rowIndex)
    {
        _tickBeforeClick = rowIndex >= 0 && rowIndex < _grid.Rows.Count && _grid.Rows[rowIndex].Tag is DriverProfile profile
            ? (profile.Transponder, _checkIns.IsCheckedIn(profile.Transponder))
            : null;
    }

    /// <summary>
    /// The first click of a double-click is an ordinary click, and on a
    /// Checked In cell an ordinary click ticks or unticks. Whatever it did
    /// is put back: the double-click was about the picked drivers, not
    /// that one tick.
    /// </summary>
    private void UndoFirstClickTick()
    {
        if (_tickBeforeClick is { } before
            && _checkIns.IsCheckedIn(before.Transponder) != before.WasIn
            && CheckInStore.ClosedCurrentDay(_events) is null)
        {
            _checkIns.SetCheckedIn(before.Transponder, before.WasIn);
            CheckInChangedAtDesk?.Invoke(before.Transponder);

            // The desk's own put-back, not a phone's (Bug hunt 18) - and the
            // first click's "checked in/out" line comes off the log with it,
            // since that tick never stood.
            RefreshMarksFromDesk();

            if (_profiles.Get(before.Transponder) is { } profile)
            {
                _deskLog.RemoveNewest(DisplayName(profile), before.WasIn ? "checked out" : "checked in");
            }
        }
    }

    /// <summary>
    /// A double-click on one of several picked drivers: the picked rows
    /// selected again - the double-click's own first click narrowed the
    /// selection to its row - and a menu at the pointer offering what can be
    /// done to all of them.
    /// </summary>
    private void ActOnMany(IReadOnlyList<int> rows)
    {
        UndoFirstClickTick();
        Reselect(rows);
        ShowDriverMenu(DriversIn(rows), _grid.PointToClient(Cursor.Position));
    }

    /// <summary>
    /// A right-click on the list (13 Nov 2026): the row under the pointer is
    /// picked - or, when it is one of several picked already, they all stay
    /// picked; Ctrl adds it to them and Shift the run from the current row -
    /// and the menu for the picked drivers opens at <paramref name="at"/>.
    /// Below the last row, the menu is for whoever is picked.
    /// </summary>
    private void OnRowMenuRequested(int rowIndex, Point at, Keys modifiers) =>
        ShowDriverMenu(PickForMenu(rowIndex, modifiers), at);

    /// <summary>The picking a right-click on <paramref name="rowIndex"/> does, and the drivers its menu is for, in list order.</summary>
    internal List<DriverProfile> PickForMenu(int rowIndex, Keys modifiers)
    {
        if (rowIndex >= 0 && rowIndex < _grid.Rows.Count)
        {
            var row = _grid.Rows[rowIndex];

            if ((modifiers & Keys.Shift) != 0 && _grid.CurrentRow is { } anchor)
            {
                for (var i = Math.Min(anchor.Index, rowIndex); i <= Math.Max(anchor.Index, rowIndex); i++)
                {
                    _grid.Rows[i].Selected = true;
                }
            }
            else if ((modifiers & Keys.Control) != 0)
            {
                row.Selected = true;
            }
            else if (!row.Selected)
            {
                // The Driver cell, not the Checked In box: a box made current
                // goes into edit mode (see Grid_CellClick).
                if (_grid.Columns["driver"] is { } driver)
                {
                    _grid.CurrentCell = row.Cells[driver.Index];
                }

                Reselect(new[] { rowIndex });
            }
        }

        return SelectedDrivers();
    }

    /// <summary>
    /// The menu of what can be done to the drivers picked - one or many -
    /// at <paramref name="at"/> on the list: check in (or out, when they all
    /// are), settle their entry money, and log an inspection.
    /// </summary>
    private void ShowDriverMenu(IReadOnlyList<DriverProfile> drivers, Point at)
    {
        if (drivers.Count == 0)
        {
            return;
        }

        var menu = new ContextMenuStrip();
        var one = drivers.Count == 1;
        var who = one ? string.Empty : $" {Plural.Of(drivers.Count, "Driver")}";

        if (drivers.All(p => _checkIns.IsCheckedIn(p.Transponder)))
        {
            var checkOut = new ToolStripMenuItem($"Check Out{who}");
            checkOut.Click += (_, _) => CheckOutDrivers(drivers);
            menu.Items.Add(checkOut);
        }
        else
        {
            var checkIn = new ToolStripMenuItem($"Check In{who}");
            checkIn.Click += (_, _) => CheckInDrivers(drivers);
            menu.Items.Add(checkIn);
        }

        AddPaymentItems(menu, drivers);

        // Papers (14 Nov 2026): one driver's papers window, or one for all of
        // them to mark their waivers and memberships in order at once.
        if (ChecksPapers)
        {
            var papers = new ToolStripMenuItem(one ? "Papers..." : $"Papers for{who}...");
            papers.Click += (_, _) =>
            {
                if (one)
                {
                    EditPapersFor(drivers[0]);
                }
                else
                {
                    EditPapersForMany(drivers);
                }
            };
            menu.Items.Add(papers);
        }

        if (_tech is not null)
        {
            var inspect = new ToolStripMenuItem(one ? "Log Inspection..." : $"Log Inspection for{who}...");
            inspect.Click += (_, _) =>
            {
                if (one)
                {
                    InspectRequested?.Invoke(drivers[0]);
                }
                else
                {
                    InspectManyRequested?.Invoke(drivers);
                }
            };
            menu.Items.Add(inspect);
        }

        AppTheme.StyleContextMenu(menu);

        // Let go once it has closed and its click has run.
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));

        menu.Show(_grid, at);
    }

    /// <summary>Takes the drivers picked off tonight's list, from the right-click menu when they are all checked in.</summary>
    internal void CheckOutDrivers(IReadOnlyList<DriverProfile> drivers)
    {
        // Not on a closed day - see CheckInStore.ClosedCurrentDay.
        if (RefusedOnClosedDay())
        {
            return;
        }

        var transponders = drivers.Select(p => p.Transponder).ToList();

        foreach (var transponder in transponders)
        {
            _checkIns.SetCheckedIn(transponder, false);
        }

        RaiseDeskChanges(transponders);
        RefreshMarksFromDesk();
        LogDesk(drivers.Count == 1 ? DisplayName(drivers[0]) : Plural.Of(drivers.Count, "driver"), "checked out");
        SetScanStatus($"Checked out {Plural.Of(transponders.Count, "driver")}.", AppTheme.Success);
    }

    /// <summary>
    /// Checks in the drivers picked, timed like every other desk change (see
    /// CheckInChangedAtDesk). The rows stay as they are, still picked, so the
    /// next thing - an inspection for the same cars - is one double-click
    /// away.
    /// </summary>
    internal void CheckInDrivers(IReadOnlyList<DriverProfile> drivers)
    {
        // Not onto a closed day - see CheckInStore.ClosedCurrentDay.
        if (RefusedOnClosedDay())
        {
            return;
        }

        var transponders = drivers.Select(p => p.Transponder).ToList();
        var newlyIn = transponders.Count(t => !_checkIns.IsCheckedIn(t));

        _checkIns.CheckInAll(transponders);
        RaiseDeskChanges(transponders);
        RefreshMarksFromDesk();

        if (newlyIn > 0)
        {
            LogDesk(drivers.Count == 1 ? DisplayName(drivers[0]) : Plural.Of(newlyIn, "driver"), "checked in");
        }

        SetScanStatus(
            newlyIn == transponders.Count
                ? $"Checked in {Plural.Of(newlyIn, "driver")}."
                : $"Checked in {Plural.Of(newlyIn, "driver")} ({transponders.Count - newlyIn} already were).",
            AppTheme.Success);
    }

    /// <summary>The drivers picked in the list, in list order.</summary>
    private List<DriverProfile> SelectedDrivers() =>
        _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => row.Tag)
            .OfType<DriverProfile>()
            .ToList();

    /// <summary>Inspect Selected Car says "Cars" while more than one row is picked.</summary>
    private void ShowInspectCaption()
    {
        var text = _grid.SelectedRows.Count > 1 ? InspectMany : InspectOne;

        if (!string.Equals(_btnInspect.Text, text, StringComparison.Ordinal))
        {
            _btnInspect.Text = text;
        }
    }

    /// <summary>
    /// Fires the inspect request for whichever row is selected - or, with
    /// more than one picked, the request to log one inspection for all of
    /// them.
    /// </summary>
    private void RequestInspection()
    {
        var picked = SelectedDrivers();

        if (picked.Count > 1)
        {
            InspectManyRequested?.Invoke(picked);
        }
        else if (_grid.CurrentRow?.Tag is DriverProfile profile)
        {
            InspectRequested?.Invoke(profile);
        }
        else
        {
            AppDialog.Show(this, "Select a driver first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        // Ctrl or Shift held: the click picks the row, nothing more. Without
        // this, Ctrl+clicking down the Checked In column to pick drivers
        // ticked each one as it went, and a Pack cell opened its menu.
        if (e.RowIndex >= 0 && (ModifierKeys & (Keys.Control | Keys.Shift)) != 0)
        {
            return;
        }

        // The Papers cell: the waiver and membership (setup plan D9).
        if (e.RowIndex >= 0 && e.ColumnIndex == _grid.Columns["papers"]?.Index)
        {
            EditPapers(e.RowIndex);
            return;
        }

        // The Class cell: tonight's classes, to tick (setup plan A3).
        if (e.RowIndex >= 0 && e.ColumnIndex == _grid.Columns["class"]?.Index)
        {
            PickClasses(e.RowIndex);
            return;
        }

        if (e.RowIndex < 0 || e.ColumnIndex != 0)
        {
            return;
        }

        // Not on a closed day - see CheckInStore.ClosedCurrentDay.
        if (RefusedOnClosedDay())
        {
            return;
        }

        // A click anywhere in the cell toggles the tick and saves it - the
        // native checkbox only toggles on a click on the small glyph itself,
        // and the column is far wider than the box.
        //
        // 17 Oct 2026, Bug Hunt 8, A28-X: edit mode is ended first. A checkbox
        // cell is in edit mode whenever it is current, and paints from its
        // editing flags rather than Value; setting Value here left those flags
        // stale, so a click beside the box saved the change but kept showing
        // the old tick, and the next glyph click wrote the store twice with
        // opposite values. Out of edit mode the native content-click toggle is
        // a no-op and the cell paints from Value, so nothing is pending to
        // commit. (Space still lands here: the cell's OnKeyUp raises CellClick.)
        var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        _grid.EndEdit();
        cell.Value = !(bool)(cell.Value ?? false);
    }

    private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressGridEvents || e.RowIndex < 0)
        {
            return;
        }

        if (_grid.Rows[e.RowIndex].Tag is not DriverProfile profile)
        {
            return;
        }

        if (e.ColumnIndex == 0)
        {
            // A tick that got past the click handler and the read-only
            // column (the day closed while the page was up): not written,
            // and put back to what the sheet says once this event is done.
            if (CheckInStore.ClosedCurrentDay(_events) is { } closedDay)
            {
                SetScanStatus($"Not changed: this day is closed (\"{closedDay.DisplayName}\").", AppTheme.Danger);
                BeginInvoke(new Action(RefreshMarksFromDesk));
                return;
            }

            var isChecked = (bool)(_grid.Rows[e.RowIndex].Cells[0].Value ?? false);
            _checkIns.SetCheckedIn(profile.Transponder, isChecked);
            CheckInChangedAtDesk?.Invoke(profile.Transponder);
            ShowStatus(_grid.Rows[e.RowIndex]);
            LogDesk(DisplayName(profile), isChecked ? "checked in" : "checked out");
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var shown = CurrentlyShownProfiles();
        var checkedInShown = shown.Count(p => _checkIns.IsCheckedIn(p.Transponder));

        // What the ticking is FOR, said on the page that does the ticking.
        //
        // This page's whole purpose is marking who is here, and until now
        // nothing on it said whether that changed anything: the setting that
        // gives a tick meaning lives on another tab, below three other
        // fields, and was off on every new day. It is now set from the kind
        // of night when the day is started - see RaceEvent.CheckedInDriversOnly
        // - which only helps if the page says which way it went.
        var rule = _events?.Active switch
        {
            null or { IsLegacy: true } => string.Empty,
            { CheckedInDriversOnly: true, IsClubNight: true } => RuleClubNightCheckedIn,
            { CheckedInDriversOnly: true } => RuleCheckedIn,
            { IsClubNight: true } => RuleClubNightEveryone,
            _ => RuleEveryone,
        };

        _lblSummary.Text =
            $"{checkedInShown} of {shown.Count} checked in ({_checkIns.Count} total checked in across all classes){rule}";

        // The chips, the strip, and the driver on the desk (13 Nov 2026).
        RefreshCounts();
        RefreshDeskCard();

        // Every check-in ends here - a tick, a scan, a phone - and only
        // RefreshAll used to bring these two lines up to date, so "0 here"
        // and its still-to-come names stayed as they were while the desk
        // ticked everybody in. Neither touches its label when nothing has
        // changed.
        RefreshEntriesClosed();
        RefreshComing();
    }
}

/// <summary>
/// The Who's Racing roster's grid: a DataGridView that remembers which rows
/// were selected just before each click (1 Nov 2026).
///
/// A double-click is two clicks, and the first is an ordinary one: it narrows
/// the selection to its row, or with Ctrl held takes its row out. So by the
/// time the double-click is raised, the drivers it was about are no longer
/// picked. The selection is read here, before the grid's own handling of the
/// press changes it - which a MouseDown handler cannot promise to do.
/// </summary>
internal sealed class RosterGrid : DataGridView
{
    /// <summary>The rows selected just before the latest single click - and so before the first click of a double-click.</summary>
    public IReadOnlyCollection<int> SelectedBeforeClick { get; private set; } = Array.Empty<int>();

    /// <summary>Raised on the first press of a click or double-click, before the selection changes, with the row under the pointer (-1 for none).</summary>
    public event Action<int>? ClickStarting;

    /// <summary>When and where the last press was, to tell the second press of a double-click from a new click - see BulkTech.IsSecondPress.</summary>
    private long _lastPressAt = long.MinValue / 2;
    private Point _lastPressPoint = Point.Empty;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var now = Environment.TickCount64;
        var second = BulkTech.IsSecondPress(e.Clicks, now - _lastPressAt, e.Location, _lastPressPoint,
            SystemInformation.DoubleClickTime, SystemInformation.DoubleClickSize);

        _lastPressAt = second ? long.MinValue / 2 : now;
        _lastPressPoint = e.Location;

        // Only the first press: the second press of a double-click must not
        // overwrite what the first one found.
        if (!second)
        {
            RememberSelection();
            ClickStarting?.Invoke(HitTest(e.X, e.Y).RowIndex);
        }

        base.OnMouseDown(e);
    }

    /// <summary>
    /// Raised on a right-click, with the row under the pointer (-1 for none),
    /// where to open a menu, and the Ctrl or Shift held - or on the menu key
    /// or Shift+F10, for the current row.
    /// </summary>
    public event Action<int, Point, Keys>? RowMenuRequested;

    private const int WmContextMenu = 0x007B;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmRButtonDblClk = 0x0206;

    /// <summary>The row the right button went down on, so the menu opens on its release - -2 while it is up.</summary>
    private int _rightPressRow = -2;

    /// <summary>
    /// The right button never reaches the grid's own handling (13 Nov 2026):
    /// a right-click there raised the cell's click on its release, which
    /// ticked a Checked In box or opened a Pack picker under the menu. It
    /// picks and asks for the menu instead - see RowMenuRequested.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmRButtonDown:
            case WmRButtonDblClk:
                if (!Focused)
                {
                    Focus();
                }

                var down = PointFrom(m.LParam);
                _rightPressRow = HitTest(down.X, down.Y).RowIndex;
                return;

            case WmRButtonUp:
                if (_rightPressRow != -2)
                {
                    var row = _rightPressRow;
                    _rightPressRow = -2;
                    RowMenuRequested?.Invoke(row, PointFrom(m.LParam), ModifierKeys & (Keys.Control | Keys.Shift));
                }

                return;

            case WmContextMenu when m.LParam == (IntPtr)(-1):
                // From the keyboard: under the current row's cell.
                if (CurrentCell is { } cell)
                {
                    var rect = GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, cutOverflow: true);
                    RowMenuRequested?.Invoke(cell.RowIndex, new Point(rect.Left, rect.Bottom), Keys.None);
                }

                return;
        }

        base.WndProc(ref m);
    }

    /// <summary>The client point packed in a mouse message's LParam.</summary>
    private static Point PointFrom(IntPtr lParam)
    {
        var value = unchecked((int)(long)lParam);
        return new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
    }

    /// <summary>Takes the selection as it stands as the one before the click. OnMouseDown calls it; the tests call it to stand in for a press.</summary>
    internal void RememberSelection() =>
        SelectedBeforeClick = SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index).ToList();
}
