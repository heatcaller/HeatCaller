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

    /// <summary>
    /// The battery packs, for the Pack column: which pack is in each
    /// driver's car tonight - what each race they run is recorded on
    /// (BatteryPackStore.AutoTag). None, and there is no such column.
    /// </summary>
    private readonly BatteryPackStore? _batteries;

    /// <summary>Who paid tonight (11 Nov 2026, Phase 4 - see DeskMoney). Null: no Paid column.</summary>
    private readonly EntryPaymentStore? _payments;

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

    /// <summary>
    /// Narrows the roster to the cars that still have to be looked at.
    ///
    /// The other half of folding Scrutineering in: "who is left to inspect"
    /// was the one question the old tab answered that this page could not, and
    /// it is a filter over the roster rather than a page of its own. Only
    /// built when there is a tech store to ask.
    /// </summary>
    private readonly CheckBox _chkNeedsTech = new ThemedCheckBox();

    /// <summary>Opens the scrutineer's full record list. Not on the page when there is no tech store.</summary>
    private readonly Button _btnTechLog = new() { Text = "Scrutineering Log..." };

    /// <summary>The Online Sign-ups button, held so its caption can say how many are waiting.</summary>
    private readonly Button _btnSignups = new() { Text = "Online Sign-ups (00)" };

    /// <summary>
    /// The Driver Requests button, beside Online Sign-ups, held so its
    /// caption can say how many are waiting. Hidden until the host says the
    /// club takes requests or some are waiting (22 Oct 2026, driver experience).
    /// </summary>
    private readonly Button _btnDriverRequests = new() { Text = "Driver Requests (00)", Visible = false };
    private readonly UndoService _undo;

    private readonly ComboBox _cmbClassFilter = new();
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

    /// <summary>The class filter, its caption and the tech filter - held so a rebuilt class list can re-measure it.</summary>
    private FieldRow? _filterRow;

    /// <summary>The class filter's width, measured from its choices - see Pieces.PickerWidth. Forgotten when the list is rebuilt.</summary>
    private (Font Font, int Count, int Width)? _classPickerWidth;

    /// <summary>The width the scan card was designed at, at the normal text size - see ScanColumnWidth.</summary>
    private const int ScanWidth = 300;

    private bool _suppressGridEvents;

    public CheckInControl(ProfileStore profiles, CheckInStore checkIns, RaceClassStore raceClasses, AppSettings settings, UndoService undo,
        TechInspectionStore? tech = null, WebServerService? web = null, RaceEventStore? events = null,
        BatteryPackStore? batteries = null, EntryPaymentStore? payments = null)
    {
        _batteries = batteries;
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

        // A pack put in a car anywhere else - Driver Hub, Race Control, the
        // phone, an undo - shows in the column.
        if (_batteries is not null)
        {
            _batteries.Changed += OnBatteriesChanged;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _batteries is not null)
        {
            _batteries.Changed -= OnBatteriesChanged;
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
        var lblIntro = new Label
        {
            Text = "Mark who's here today. Whether tonight's program uses checked-in drivers only is set on the Setup tab. " +
                   "Scrutineering is here too: the Tech column is the gate, the filter beside the class list shows who is " +
                   "still to be looked at, and Scrutineering Log holds the full record.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _pnlNoDay = BuildNoDayBanner();

        // --- The filter row ---
        var lblFilter = new Label { Text = "Class:" };

        _cmbClassFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClassFilter.SelectedIndexChanged += (_, _) =>
        {
            RefreshDriverList();
            var selected = _cmbClassFilter.SelectedItem as string;
            _settings.LastCheckInClassFilter = selected is null || selected.StartsWith("(all") ? string.Empty : selected;
            _settings.Save();
        };

        _chkNeedsTech.Text = "Only those not through tech";
        _chkNeedsTech.AutoSize = true;
        _chkNeedsTech.CheckedChanged += (_, _) => RefreshDriverList();

        var filterRow = new FieldRow();

        filterRow.Add(lblFilter, _cmbClassFilter, ClassFilterWidth);

        // Only with a tech store to ask - see _chkNeedsTech.
        if (_tech is not null)
        {
            filterRow.Add(null, _chkNeedsTech);
        }

        _filterRow = filterRow;

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
        _grid.SelectionChanged += (_, _) => ShowInspectCaption();
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewCheckBoxColumn { Name = "checkedIn", HeaderText = "Checked In", FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "class", HeaderText = "Class", FillWeight = 40, ReadOnly = true }
        );

        if (_tech is not null)
        {
            // A driver running two classes can be legal in one car and not
            // the other, so this reads per (driver, class) rather than
            // showing one status per person - the same scoping tech
            // inspection itself uses, so the two pages can't disagree.
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "tech", HeaderText = "Tech", FillWeight = 35, ReadOnly = true });
        }

        if (_batteries is not null)
        {
            // Click it to pick: the pack in the car tonight is what each
            // race they run is recorded on.
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "pack", HeaderText = "Pack", FillWeight = 35, ReadOnly = true });
        }

        if (_payments is not null)
        {
            // Money at the desk (11 Nov 2026): shown only on a race day with an
            // entry fee - see RefreshDriverList. Right-click to settle.
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "paid", HeaderText = "Paid", FillWeight = 35, ReadOnly = true, Visible = false });
        }

        // Papers (12 Nov 2026, setup plan D9): shown only while the club
        // checks waivers or memberships - see RefreshDriverList. Click to record.
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "papers", HeaderText = "Papers", FillWeight = 45, ReadOnly = true, Visible = false });
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

        var btnCheckInAll = new Button { Text = "Check In All Shown" };
        btnCheckInAll.Click += (_, _) => CheckInEveryoneShown();

        // Somebody at the desk who is not on file yet - the same small window
        // the Set Up Tonight wizard's Drivers step opens.
        var btnAddDriver = new Button { Text = "Add Driver..." };
        btnAddDriver.Click += (_, _) => AddDriver();

        // Tonight's entry list - RCSignup, LiveTime, LiveRC or any CSV -
        // matched onto the drivers here and checked in (11 Nov 2026, build
        // plan 7.1). A menu under the button: from a file, or pasted. See
        // CheckInControl.Entries.cs.
        var btnEntries = new Button { Text = "Import Entries..." };
        var entriesMenu = BuildEntriesMenu();
        btnEntries.Click += (_, _) => entriesMenu.Show(btnEntries, new Point(0, btnEntries.Height));

        var btnCheckOutAll = new Button { Text = "Check Out Everyone" };

        // Named for what it acts on, like the two buttons beside it. It was
        // "Inspect...", sitting next to "Check In All Shown" and "Check Out
        // Everyone" - both of which say their scope - and leaving the one
        // button that works on a single row the only one that did not.
        //
        // "Cars" while more than one row is picked: then it logs one
        // inspection for all of them - see RequestInspection.
        _btnInspect.Click += (_, _) => RequestInspection();

        _btnTechLog.Click += (_, _) => TechLogRequested?.Invoke();
        btnCheckOutAll.Click += (_, _) =>
        {
            // Not on a closed day: its sheet is the record of who was at
            // that meeting, and this used to wipe it - see
            // CheckInStore.ClosedCurrentDay.
            if (RefusedOnClosedDay())
            {
                return;
            }

            if (AppDialog.Show(this, "Clear all check-ins? Do this at the start of a new event day.",
                "Confirm check-out", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                // Which event, as well as who: an undo made after switching
                // events must put them back where they were checked out.
                var snapshot = _checkIns.SnapshotCheckedIn();
                var eventId = _checkIns.CurrentEventId;
                _undo.Capture($"Check out everyone ({Plural.Of(snapshot.Count, "driver")})", () =>
                {
                    _checkIns.RestoreCheckedIn(eventId, snapshot);

                    // Only when the check-ins put back are the ones on show.
                    // Undone after switching to another race day, they went
                    // back to the day they came from - and a desk change
                    // raised for them here blocked pending phone check-ins
                    // for the same drivers at the race day now open.
                    if (string.Equals(eventId, _checkIns.CurrentEventId, StringComparison.Ordinal))
                    {
                        RaiseDeskChanges(snapshot);
                    }

                    // The page may be gone by the time Undo is pressed; the
                    // store is what matters, and the page re-reads it when
                    // it is next shown.
                    if (!IsDisposed && !Disposing)
                    {
                        RefreshDriverList();
                    }
                });

                _checkIns.CheckOutAll();
                RaiseDeskChanges(snapshot);
                RefreshDriverList();
            }
        };

        _btnSignups.Click += (_, _) => SignupsRequested?.Invoke();
        _btnDriverRequests.Click += (_, _) => DriverRequestsRequested?.Invoke();

        // Its opening caption. The bar it goes in sizes it to whatever it
        // says, and again every time the count changes.
        SetSignupCount(0);

        // Only the buttons that exist: a club with no tech store gets neither
        // Inspect nor the log.
        var actions = new ButtonBar().With(btnAddDriver, btnEntries, btnCheckInAll, btnCheckOutAll);

        if (_tech is not null)
        {
            actions.With(_btnInspect, _btnTechLog);
        }

        actions.With(_btnSignups, _btnDriverRequests);

        // Money at the desk: the slip a double-click on a name adds to.
        if (_payments is not null)
        {
            _btnDriverPaid.Click += (_, _) => OpenDriverPaid();
            actions.With(_btnDriverPaid);
        }

        var roster = new Section("Drivers");

        roster.Add(filterRow);
        roster.Add(_lblSummary);
        roster.AddFill(_grid, LeastGridHeight);
        roster.Add(actions, gapBelow: 0);

        // --- The scan card ---
        //
        // A card beside the roster, as tall as what is in it at the column's
        // width: the help wraps to it, and the status line keeps two lines
        // of its own so the buttons under it do not hop as drivers are
        // scanned in.
        var lblScanHelp = _textSize.Wear(
            new Label
            {
                Text = "Click the box below, then scan a driver's QR check-in with any USB barcode scanner (no setup needed). " +
                       "They're checked in instantly.\r\n\r\n" +
                       "Or let drivers sign themselves on: start the web server and put the poster below on the wall. " +
                       "They tap their own name and it appears here.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            },
            () => AppTheme.SmallFont);

        // In the page's own face now. It was a fixed 11pt, which neither
        // grew with the text size nor matched anything else on the page.
        _txtScan.PlaceholderText = "Waiting for scan...";
        _txtScan.KeyDown += TxtScan_KeyDown;

        _lblScanStatus.AutoSize = true;
        _lblScanStatus.Text = string.Empty;
        _textSize.Wear(_lblScanStatus, () => AppTheme.BoldFont);
        _textSize.ReserveLines(_lblScanStatus, 2);

        var btnBadges = new Button { Text = "Print QR Check-ins..." };
        btnBadges.Click += (_, _) => GenerateBadges();

        var btnPoster = new Button { Text = "Self Check-In Poster..." };
        btnPoster.Click += (_, _) => ShowSelfCheckInPoster();

        var scan = new Section("Scan to Check In");

        scan.Add(lblScanHelp);
        scan.Add(_txtScan, gapBelow: UiLayout.HintGap);
        scan.Add(_lblScanStatus);

        // Desk view (12 Nov 2026, setup plan D8): this laptop locked to
        // check-ins and money, for a volunteer, while the director races.
        var btnDeskView = new Button { Text = "Desk View..." };
        btnDeskView.Click += (_, _) => DeskViewRequested?.Invoke();

        var scanButtons = scan.Add(new ButtonBar().With(btnBadges, btnPoster, btnDeskView), gapBelow: 0);

        var body = new Beside(() => ScanColumnWidth(scanButtons));

        body.AddFill(roster, roster.LeastHeight);
        body.AddSideCard(scan);

        // --- The page ---
        var page = new FillPage();

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
        page.AddFill(body, body.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// The class filter: wide enough for the longest class name, and never
    /// under the 220px it always had at the normal text size.
    /// </summary>
    private int ClassFilterWidth() =>
        Math.Max(UiLayout.AtTextSize(220), Pieces.PickerWidth(_cmbClassFilter, ref _classPickerWidth, "(all classes)"));

    /// <summary>The least height the roster's grid can do with: its headings and three drivers. Below that the page scrolls instead - see FillPage.</summary>
    private int LeastGridHeight() =>
        _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height;

    /// <summary>
    /// The scan card's width: the 300px it was designed at, at the text size
    /// in use - so its help wraps to a column about as many words wide
    /// whatever the size, not to a sliver at 200% - and never too narrow for
    /// the wider of its two buttons, whose captions the column cannot see.
    /// </summary>
    private static int ScanColumnWidth(ButtonBar buttons) =>
        Math.Max(
            UiLayout.AtTextSize(ScanWidth),
            buttons.Controls.OfType<Button>().Select(button => button.Width).DefaultIfEmpty(0).Max() + Section.Inset.Horizontal);

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

        _checkIns.CheckInAll(shown);
        RaiseDeskChanges(shown);
        RefreshDriverList();
    }

    /// <summary>Shows how many online sign-ups are waiting for the desk on the button that opens them.</summary>
    public void SetSignupCount(int waiting)
    {
        // The bar it is in re-measures it: the caption gets longer or
        // shorter with the count, and the button with it.
        _btnSignups.Text = waiting > 0 ? $"Online Sign-ups ({Math.Min(waiting, 99)})" : "Online Sign-ups";
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

        var scanned = _txtScan.Text.Trim();
        _txtScan.Clear();

        if (string.IsNullOrWhiteSpace(scanned))
        {
            return;
        }

        var transponder = QrBadgeRenderer.ParsePayload(scanned);
        if (transponder is null)
        {
            // An online badge whose id no driver here has any more - printed
            // before Reissue All Badges on the Club Website page, with another
            // laptop's key, or for a driver since deleted. This used to say
            // it didn't look like a HeatCaller badge, which it plainly is.
            SetScanStatus(
                RelayCheckIn.IdFromScan(scanned) is not null
                    ? "That QR check-in is out of date - print a new one."
                    : "That doesn't look like a HeatCaller QR check-in.",
                AppTheme.Danger);
            return;
        }

        var profile = _profiles.All.FirstOrDefault(p => string.Equals(p.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            SetScanStatus($"No driver profile found for transponder {transponder}.", AppTheme.Danger);
            return;
        }

        if (_checkIns.IsCheckedIn(profile.Transponder))
        {
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
        var startClass = _cmbClassFilter.SelectedIndex > 0 ? _cmbClassFilter.SelectedItem as string : null;

        using var form = new QuickDriverForm(classes, startClass, QuickDriverForm.OwnerNameIn(_profiles));

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // TryAdd, not Upsert: a create, refused rather than saved over
        // anybody - the backstop if the window's check let a clash by.
        if (!_profiles.TryAdd(new DriverProfile
            {
                Transponder = form.Transponder,
                DriverName = form.DriverName,
                ClassName = form.ClassName,
            }, out var refusal))
        {
            AppDialog.Show(this, refusal, "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _checkIns.SetCheckedIn(form.Transponder, true);
        CheckInChangedAtDesk?.Invoke(form.Transponder);

        // A class filter that would hide them is taken off, so the person
        // just added is on screen. Changing it refreshes the list itself.
        if (_cmbClassFilter.SelectedIndex > 0
            && !string.Equals(_cmbClassFilter.SelectedItem as string, form.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            _cmbClassFilter.SelectedIndex = 0;
        }
        else
        {
            RefreshDriverList();
        }

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

    /// <summary>Text for the Tech column - what state, and what today's inspection actually said if there was one.</summary>
    private string TechCellText(DriverProfile profile)
    {
        // Latest, the same inspection Race Control's tech warning goes by -
        // by race day, not calendar date - so the desk and the gate never
        // disagree about whether a car is cleared.
        var inspection = TechFor(profile);
        return inspection is null ? "-" : inspection.ResultText;
    }

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
    /// </summary>
    private bool JudgesTechFor(string className) =>
        RaceControlControl.DeskJudgesTechFor(className, _raceClasses.All, _tech!, _events?.Active,
            _cmbClassFilter.SelectedIndex > 0 ? _cmbClassFilter.SelectedItem as string : null);

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

    private void RefreshClassFilter()
    {
        var previous = _cmbClassFilter.SelectedItem as string ?? (_settings.LastCheckInClassFilter.Length > 0 ? _settings.LastCheckInClassFilter : null);
        _cmbClassFilter.Items.Clear();
        _cmbClassFilter.Items.Add("(all classes)");
        foreach (var c in _raceClasses.All)
        {
            _cmbClassFilter.Items.Add(c.Name);
        }
        var idx = previous is not null ? _cmbClassFilter.Items.IndexOf(previous) : -1;
        _cmbClassFilter.SelectedIndex = idx >= 0 ? idx : 0;

        // The longest class may have come or gone: measured again, and the
        // row with it.
        _classPickerWidth = null;
        _filterRow?.PerformLayout();
    }

    private List<DriverProfile> CurrentlyShownProfiles()
    {
        var classFilter = _cmbClassFilter.SelectedIndex > 0 ? (string)_cmbClassFilter.SelectedItem! : null;

        // "Not through tech" means not cleared, which covers both the car
        // nobody has looked at and the car that was looked at and failed. Read
        // per (driver, class), the same scoping the Tech column and tech
        // inspection itself use, so a driver legal in one car and not the
        // other shows up for the car that needs work.
        var needsTech = _tech is not null && _chkNeedsTech.Checked;

        return _profiles.All
            .Where(p => classFilter is null || p.RunsClass(classFilter))
            .Where(p => !needsTech || TechFor(p) is not { IsCleared: true })
            .ToList();
    }

    private void RefreshDriverList()
    {
        _suppressGridEvents = true;
        try
        {
            _grid.Rows.Clear();
            foreach (var profile in CurrentlyShownProfiles())
            {
                // New to racing (setup plan D12): " · first night", " · novice, mentor Al".
                var displayName = (string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName) + NewDrivers.Badge(profile, DateTime.Now);
                var rowIndex = _tech is null
                    ? _grid.Rows.Add(_checkIns.IsCheckedIn(profile.Transponder), displayName, ClassCellText(profile))
                    : _grid.Rows.Add(_checkIns.IsCheckedIn(profile.Transponder), displayName, ClassCellText(profile),
                        TechCellText(profile));

                _grid.Rows[rowIndex].Tag = profile;
                ShowPack(_grid.Rows[rowIndex]);
                ShowPaid(_grid.Rows[rowIndex]);
                ShowPapers(_grid.Rows[rowIndex]);

                if (_tech is not null)
                {
                    var techCol = _grid.Columns["tech"]!.Index;
                    var inspection = TechFor(profile);

                    _grid.Rows[rowIndex].Cells[techCol].Style.ForeColor = inspection switch
                    {
                        { IsCleared: true } => AppTheme.Success,
                        not null => AppTheme.Danger,
                        _ => AppTheme.TextMuted,
                    };
                }
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

        if (rows.Count > 1)
        {
            ActOnMany(rows);
            return;
        }

        // One row: as it always was - the Tech cell inspects, anywhere else
        // does nothing.
        if (_tech is null || e.ColumnIndex != _grid.Columns["tech"]?.Index)
        {
            return;
        }

        if (_grid.Rows[e.RowIndex].Tag is DriverProfile profile)
        {
            InspectRequested?.Invoke(profile);
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
            RefreshCheckInMarks();
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

        var drivers = DriversIn(rows);

        if (drivers.Count == 0)
        {
            return;
        }

        var menu = new ContextMenuStrip();

        var checkIn = new ToolStripMenuItem($"Check In {Plural.Of(drivers.Count, "Driver")}");
        checkIn.Click += (_, _) => CheckInDrivers(drivers);
        menu.Items.Add(checkIn);

        AddPaymentItems(menu, drivers);

        if (_tech is not null)
        {
            var inspect = new ToolStripMenuItem($"Log Inspection for {Plural.Of(drivers.Count, "Driver")}...");
            inspect.Click += (_, _) => InspectManyRequested?.Invoke(drivers);
            menu.Items.Add(inspect);
        }

        AppTheme.StyleContextMenu(menu);

        // Let go once it has closed and its click has run.
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));

        menu.Show(_grid, _grid.PointToClient(Cursor.Position));
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
        RefreshCheckInMarks();

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

        if (e.RowIndex >= 0 && _batteries is not null && e.ColumnIndex == _grid.Columns["pack"]?.Index)
        {
            PickPack(e.RowIndex);
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
                BeginInvoke(new Action(RefreshCheckInMarks));
                return;
            }

            var isChecked = (bool)(_grid.Rows[e.RowIndex].Cells[0].Value ?? false);
            _checkIns.SetCheckedIn(profile.Transponder, isChecked);
            CheckInChangedAtDesk?.Invoke(profile.Transponder);
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

        // Every check-in ends here - a tick, a scan, a phone - and only
        // RefreshAll used to bring these two lines up to date, so "0 here"
        // and its still-to-come names stayed as they were while the desk
        // ticked everybody in. Neither touches its label when nothing has
        // changed.
        RefreshEntriesClosed();
        RefreshComing();
    }
    // ------------------------------------------------------------ packs

    /// <summary>
    /// A row's Pack cell: the pack in the driver's car, "Pick..." when they
    /// have packs to choose from (theirs or the club's) and none is in, or
    /// blank when there is nothing to pick.
    /// </summary>
    private void ShowPack(DataGridViewRow row)
    {
        if (_batteries is null || _grid.Columns["pack"] is not { } column || row.Tag is not DriverProfile profile)
        {
            return;
        }

        var key = BatteryPackStore.DriverKey(_profiles, profile.Transponder);
        var inCar = _batteries.CurrentPack(key);
        var cell = row.Cells[column.Index];

        if (inCar is not null)
        {
            cell.Value = inCar.DisplayName + (inCar.IsClubPack ? " (club)" : string.Empty);
            cell.Style.ForeColor = AppTheme.TextDark;
        }
        else
        {
            cell.Value = _batteries.ChoicesFor(profile).Count > 0 ? "Pick..." : string.Empty;
            cell.Style.ForeColor = AppTheme.TextMuted;
        }
    }

    /// <summary>Posted, since the store does not promise which thread it changes on. Only the Pack cells change.</summary>
    private void OnBatteriesChanged()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed)
                {
                    return;
                }

                _suppressGridEvents = true;

                try
                {
                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        ShowPack(row);
                    }
                }
                finally
                {
                    _suppressGridEvents = false;
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The window went between the check and the post.
        }
    }

    /// <summary>
    /// The Pack cell clicked: the driver's packs in rotation and the club's,
    /// the one in the car ticked, and - while one is in - "No pack in the
    /// car". Picked, it goes in their car, with an undo.
    /// </summary>
    private void PickPack(int rowIndex)
    {
        if (_batteries is not { } store || _grid.Rows[rowIndex].Tag is not DriverProfile profile)
        {
            return;
        }

        var key = BatteryPackStore.DriverKey(_profiles, profile.Transponder);
        var inCar = store.CurrentPackId(key);
        var choices = store.ChoicesFor(profile);
        var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

        if (choices.Count == 0 && inCar is null)
        {
            return;
        }

        var menu = new ContextMenuStrip();

        void Add(string text, string? packId, bool ticked)
        {
            var item = new ToolStripMenuItem(text) { Checked = ticked };
            item.Click += (_, _) =>
            {
                BatteryActions.PutInCar(store, _undo, key, name, packId);

                if (!IsDisposed && rowIndex < _grid.Rows.Count && ReferenceEquals(_grid.Rows[rowIndex].Tag, profile))
                {
                    ShowPack(_grid.Rows[rowIndex]);
                }
            };
            menu.Items.Add(item);
        }

        foreach (var pack in choices)
        {
            Add(pack.DisplayName + (pack.IsClubPack ? "  (club)" : string.Empty), pack.Id, pack.Id == inCar);
        }

        if (inCar is not null)
        {
            menu.Items.Add(new ToolStripSeparator());
            Add("No pack in the car", null, ticked: false);
        }

        AppTheme.StyleContextMenu(menu);

        // Let go once it has closed and its click has run.
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));

        var cell = _grid.GetCellDisplayRectangle(_grid.Columns["pack"]!.Index, rowIndex, cutOverflow: true);
        menu.Show(_grid, new Point(cell.Left, cell.Bottom));
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

    /// <summary>Takes the selection as it stands as the one before the click. OnMouseDown calls it; the tests call it to stand in for a press.</summary>
    internal void RememberSelection() =>
        SelectedBeforeClick = SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index).ToList();
}
