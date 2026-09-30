namespace HeatCaller;

/// <summary>
/// Mark which driver profiles are actually present for today's event.
/// The Day can
/// optionally limit heat generation to only checked-in drivers, so a
/// roster that includes everyone who's ever raced a class doesn't get used
/// wholesale on a day when half of them didn't show up.
/// </summary>
public sealed class CheckInControl : UserControl
{
    private readonly ProfileStore _profiles;
    private readonly CheckInStore _checkIns;
    private readonly RaceClassStore _raceClasses;
    private readonly AppSettings _settings;
    private readonly TechInspectionStore? _tech;

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

    /// <summary>
    /// Optional, and only ever used to build the self check-in poster - a
    /// club that never starts the web server still gets the whole of this
    /// page, minus one button that would have nothing to point at.
    /// </summary>
    private readonly WebServerService? _web;

    /// <summary>Raised when the user asks to inspect a driver from this page. The host owns opening the actual Tech Inspection form.</summary>
    public event Action<DriverProfile>? InspectRequested;

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
    private readonly UndoService _undo;

    private readonly ComboBox _cmbClassFilter = new();
    private readonly DataGridView _grid = new();

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
        TechInspectionStore? tech = null, WebServerService? web = null, RaceEventStore? events = null)
    {
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
            Text = "Mark who's actually here today. The Setup tab can optionally build tonight's program from checked-in drivers only, " +
                   "so a class roster that includes everyone who's ever raced doesn't get used wholesale on a day when half of " +
                   "them are missing. Scrutineering is here too: the Tech column is the gate, the filter beside the class list " +
                   "shows who is still to be looked at, and Scrutineering Log holds the full record.",
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
        _grid.MultiSelect = false;
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
        _grid.CellClick += Grid_CellClick;
        _grid.CellDoubleClick += Grid_CellDoubleClick;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
            {
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

        var btnCheckOutAll = new Button { Text = "Check Out Everyone" };

        // Named for what it acts on, like the two buttons beside it. It was
        // "Inspect...", sitting next to "Check In All Shown" and "Check Out
        // Everyone" - both of which say their scope - and leaving the one
        // button that works on a single row the only one that did not.
        var btnInspect = new Button { Text = "Inspect Selected Car..." };
        btnInspect.Click += (_, _) => RequestInspection();

        _btnTechLog.Click += (_, _) => TechLogRequested?.Invoke();
        btnCheckOutAll.Click += (_, _) =>
        {
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

        // Its opening caption. The bar it goes in sizes it to whatever it
        // says, and again every time the count changes.
        SetSignupCount(0);

        // Only the buttons that exist: a club with no tech store gets neither
        // Inspect nor the log.
        var actions = new ButtonBar().With(btnAddDriver, btnCheckInAll, btnCheckOutAll);

        if (_tech is not null)
        {
            actions.With(btnInspect, _btnTechLog);
        }

        actions.With(_btnSignups);

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
                Text = "Click the box below, then scan a driver's QR check-in with any USB barcode scanner - they plug in and " +
                       "type like a keyboard, so no extra setup is needed. The driver is checked in instantly.\r\n\r\n" +
                       "Or let drivers sign themselves on: start the web server and put the poster below on the wall. " +
                       "They find their own name and tap it, and it appears here.",
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

        var scanButtons = scan.Add(new ButtonBar().With(btnBadges, btnPoster), gapBelow: 0);

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
                Text = "No race day is open.",
                AutoSize = true,
                ForeColor = AppTheme.Warning,
            },
            () => AppTheme.HeadingFont);

        var detail = new Label
        {
            Text = "Check-ins are filed against a race day, so ticks made now are not kept with tonight's racing "
                 + "and the heats cannot be built from them. Start one and this page starts counting.",
            AutoSize = true,
            ForeColor = AppTheme.TextDark,
        };

        var start = new Button { Text = "Start a Race Day" };
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
    /// Shows the banner only when check-ins have nowhere real to go.
    ///
    /// The legacy holder counts as nowhere: it is the cabinet everything
    /// recorded before race days existed lives in, not a day anybody is
    /// running.
    /// </summary>
    private void RefreshNoDayBanner()
    {
        if (_pnlNoDay is not { } panel)
        {
            return;
        }

        var open = _events?.Active is { IsLegacy: false };
        var wanted = _events is not null && !open;

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
        var classes = _raceClasses.All.Select(c => c.Name).ToList();

        // Starts on the class being filtered to - somebody working through
        // Stock who meets a new face is adding a Stock driver.
        var startClass = _cmbClassFilter.SelectedIndex > 0 ? _cmbClassFilter.SelectedItem as string : null;

        using var form = new QuickDriverForm(classes, startClass, transponder =>
            _profiles.Get(transponder) is { } existing
                ? (string.IsNullOrWhiteSpace(existing.DriverName) ? "someone else" : existing.DriverName)
                : null);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _profiles.Upsert(new DriverProfile
        {
            Transponder = form.Transponder,
            DriverName = form.DriverName,
            ClassName = form.ClassName,
        });

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

            for (var page = 0; page < pageCount; page++)
            {
                var slice = drivers
                    .Skip(page * QrBadgeRenderer.BadgesPerPage)
                    .Take(QrBadgeRenderer.BadgesPerPage)
                    .ToList();

                // One file per page rather than one enormous image, so each
                // maps directly onto a sheet of paper when printed.
                var path = pageCount == 1
                    ? dialog.FileName
                    : Path.Combine(directory, $"{baseName}-page{page + 1}.png");

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
        var inspection = _tech!.Latest(profile.Transponder, profile.ClassName);
        return inspection is null ? "-" : inspection.ResultText;
    }

    public void RefreshAll()
    {
        RefreshNoDayBanner();
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
            .Where(p => classFilter is null || string.Equals(p.ClassName, classFilter, StringComparison.OrdinalIgnoreCase))
            .Where(p => !needsTech || _tech!.Latest(p.Transponder, p.ClassName) is not { IsCleared: true })
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
                var displayName = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;
                var rowIndex = _tech is null
                    ? _grid.Rows.Add(_checkIns.IsCheckedIn(profile.Transponder), displayName, profile.ClassName)
                    : _grid.Rows.Add(_checkIns.IsCheckedIn(profile.Transponder), displayName, profile.ClassName,
                        TechCellText(profile));

                _grid.Rows[rowIndex].Tag = profile;

                if (_tech is not null)
                {
                    var techCol = _grid.Columns["tech"]!.Index;
                    var inspection = _tech.Latest(profile.Transponder, profile.ClassName);

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

        UpdateSummary();
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (_tech is null || e.RowIndex < 0 || e.ColumnIndex != _grid.Columns["tech"]?.Index)
        {
            return;
        }

        if (_grid.Rows[e.RowIndex].Tag is DriverProfile profile)
        {
            InspectRequested?.Invoke(profile);
        }
    }

    /// <summary>Fires the inspect request for whichever row is selected.</summary>
    private void RequestInspection()
    {
        if (_grid.CurrentRow?.Tag is DriverProfile profile)
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
        if (e.RowIndex < 0 || e.ColumnIndex != 0)
        {
            return;
        }

        // Clicking directly toggles the checkbox and commits immediately -
        // without this, a single click only starts editing and needs a
        // second click/Enter to actually register, which reads as broken.
        var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        cell.Value = !(bool)(cell.Value ?? false);
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
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
    }
}
