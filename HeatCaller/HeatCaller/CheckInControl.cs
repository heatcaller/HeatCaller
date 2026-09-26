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
    private Panel? _pnlNoDay;

    /// <summary>
    /// Whether the banner is MEANT to be showing.
    ///
    /// Held separately because Control.Visible reports effective
    /// visibility: on a page that is not the one being shown it answers
    /// false however the banner was set, and both the decision to show it
    /// and the layout underneath it were reading that. Starts false, which
    /// is how BuildNoDayBanner builds the panel.
    /// </summary>
    private bool _noDayBannerShown;

    /// <summary>The "Class:" caption, held so it moves with its combo when the banner appears.</summary>
    private Label? _lblFilter;

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
    private readonly CheckBox _chkNeedsTech = new();

    /// <summary>Opens the scrutineer's full record list. Hidden when there is no tech store.</summary>
    private readonly Button _btnTechLog = new() { Text = "Scrutineering Log..." };

    /// <summary>The Online Sign-ups button, held so its caption can say how many are waiting.</summary>
    private readonly Button _btnSignups = new() { Text = "Online Sign-ups (00)" };
    private readonly UndoService _undo;

    private readonly ComboBox _cmbClassFilter = new();
    private readonly DataGridView _grid = new();

    /// <summary>
    /// The row under the grid, held so the stretch pass can flow it against
    /// the real width and pin it to the bottom edge.
    ///
    /// Only the buttons that actually exist: a club with no tech store gets
    /// neither Inspect nor the log, and a hidden button left in here would be
    /// flowed like any other and leave a hole in the row. Membership is decided
    /// at build time rather than from Visible, because Control.Visible reports
    /// EFFECTIVE visibility - false for everything on a page that has not been
    /// shown yet, which is exactly when this is built.
    /// </summary>
    private Button[] _actionButtons = Array.Empty<Button>();

    /// <summary>The scan panel, held so the stretch pass can pin it to the right edge.</summary>
    private GroupBox? _grpScan;
    private readonly Label _lblSummary = new();
    private readonly TextBox _txtScan = new();
    private readonly Label _lblScanStatus = new();

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
        MinimumSize = new Size(910, 560);
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshAll();
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            // "Race Program" was a page name that no longer exists - the
            // programme is the lower half of Today, and the setting that
            // limits the build to checked-in drivers is on The Day.
            //
            // "what it builds", not "heat building": this page is the same
            // page on a club night, where the programme is races rather
            // than heats, and this text is written once for both.
            Text = "Mark who's actually here today. The Setup tab can optionally build tonight's program from checked-in drivers only, " +
                   "so a class roster that includes everyone who's ever raced doesn't get used wholesale on a day when half of " +
                   "them are missing. Scrutineering is here too: the Tech column is the gate, the filter beside the class list " +
                   "shows who is still to be looked at, and Scrutineering Log holds the full record.",
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            ForeColor = AppTheme.TextMuted,

            // Set here, not inherited.
            //
            // Everything below is positioned by measuring the control above
            // it, and a control measures itself in the font it currently
            // has. Until it is added to this page it does not have this
            // page's font - it has the WinForms default, which is smaller.
            // So every measurement taken in this method would be of a
            // control that is about to grow, which is the same class of
            // mistake as the hardcoded coordinates being replaced.
            Font = AppTheme.BodyFont,
        };

        // Every row on this page now flows from the one above it.
        //
        // The coordinates that used to be here were picked against a
        // smaller font, and the intro is the one that gave way first: it
        // was allowed two lines and takes three, so its last line came down
        // across the class filter and the summary beside it. The scan panel
        // had the same fault a second time over, with its instructions
        // sitting on top of the scan box they describe.
        var y = UiLayout.PageTop;

        lblIntro.Location = new Point(UiLayout.PageLeft, y);

        y = UiLayout.Below(lblIntro, UiLayout.RowGap);

        _pnlNoDay = BuildNoDayBanner();
        _pnlNoDay.Location = new Point(UiLayout.PageLeft, y);

        // The rows below start under the banner when it is showing and
        // where it is when it is not, so the page does not leave a hole on
        // the ordinary day when everything is set up.
        y = UiLayout.Below(_pnlNoDay, UiLayout.RowGap);

        var lblFilter = new Label { Text = "Class:", AutoSize = true, Font = AppTheme.BodyFont };
        _lblFilter = lblFilter;

        _cmbClassFilter.Font = AppTheme.BodyFont;
        _cmbClassFilter.Width = 220;
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
        _chkNeedsTech.Font = AppTheme.BodyFont;
        _chkNeedsTech.Visible = _tech is not null;
        _chkNeedsTech.CheckedChanged += (_, _) => RefreshDriverList();

        // Sized by the room left on the row rather than by its own text.
        //
        // It now carries the check-in RULE as well as the count, which is
        // more than will fit on a narrow window - and an AutoSize label
        // simply runs off the right-hand edge, where nothing reports it.
        // Bounded here and re-bounded on every resize by LayoutStretch, with
        // AutoEllipsis so the tail degrades to "..." instead of vanishing.
        _lblSummary.AutoSize = false;
        _lblSummary.AutoEllipsis = true;
        _lblSummary.Font = AppTheme.BodyFont;
        _lblSummary.ForeColor = AppTheme.TextMuted;
        _lblSummary.Height = AppTheme.BodyFont.Height + 2;

        lblFilter.Location = new Point(
            UiLayout.PageLeft, UiLayout.CentreOn(lblFilter, y, _cmbClassFilter.Height));

        _cmbClassFilter.Location = new Point(UiLayout.After(lblFilter), y);

        _chkNeedsTech.Location = new Point(
            UiLayout.After(_cmbClassFilter) + UiLayout.SectionGap,
            UiLayout.CentreOn(_chkNeedsTech, y, _cmbClassFilter.Height));

        // Centred on the font rather than on itself: the summary has no
        // text yet at this point - RefreshAll fills it - so measuring the
        // label would centre an empty box.
        //
        // Placed after whichever control is actually to its left, so a club
        // with no tech store does not get a gap where the filter would be.
        _lblSummary.Location = new Point(
            (_chkNeedsTech.Visible ? UiLayout.After(_chkNeedsTech) : UiLayout.After(_cmbClassFilter)) + UiLayout.SectionGap,
            y + Math.Max(0, (_cmbClassFilter.Height - AppTheme.BodyFont.Height) / 2));

        y = UiLayout.Below(_cmbClassFilter, UiLayout.RowGap);

        _grid.Location = new Point(UiLayout.PageLeft, y);

        // A starting size only. The stretch pass below owns the real one,
        // recomputed on every resize - see LayoutStretch.
        _grid.Size = new Size(560, 400);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left;
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
        btnInspect.Visible = _tech is not null;

        _btnTechLog.Click += (_, _) => TechLogRequested?.Invoke();
        _btnTechLog.Visible = _tech is not null;
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

        // --- The row under the grid ---
        //
        // Not laid out here: LayoutStretch owns it, so there is one authority
        // on this row rather than a build-time placement and a resize-time one
        // disagreeing about it. All this does is give the sign-ups button its
        // opening caption.
        SetSignupCount(0);

        // --- QR scan panel ---
        //
        // Beside the grid and on the same top line as it, with its height
        // taken from what ends up inside rather than guessed at - see the
        // end of this block.
        var grpScan = new GroupBox
        {
            Text = "Scan to Check In",
            Location = new Point(_grid.Right + UiLayout.SectionGap, y),
            Width = 300,

            // For the same reason as the intro above - InsideGroup works
            // off the group's own font to clear its caption.
            Font = AppTheme.BodyFont,
        };

        const int scanInset = 12;
        var scanWidth = grpScan.Width - scanInset * 2;

        var lblScanHelp = new Label
        {
            Text = "Click the box below, then scan a driver's QR check-in with any USB barcode scanner - they plug in and " +
                   "type like a keyboard, so no extra setup is needed. The driver is checked in instantly.\r\n\r\n" +
                   "Or let drivers sign themselves on: start the web server and put the poster below on the wall. " +
                   "They find their own name and tap it, and it appears here.",
            AutoSize = true,
            MaximumSize = new Size(scanWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var scanY = UiLayout.InsideGroup(grpScan);

        lblScanHelp.Location = new Point(scanInset, scanY);

        scanY = UiLayout.Below(lblScanHelp, UiLayout.RowGap);

        // Width and font first, then the position: a text box sizes its own
        // height off its font, and placing it before that is set measures
        // the wrong box.
        _txtScan.Width = scanWidth;
        _txtScan.Font = new Font(AppTheme.BodyFont.FontFamily, 11F);
        _txtScan.PlaceholderText = "Waiting for scan...";
        _txtScan.KeyDown += TxtScan_KeyDown;
        _txtScan.Location = new Point(scanInset, scanY);

        scanY = UiLayout.Below(_txtScan, UiLayout.HintGap);

        // Two lines reserved whether or not there is a message, so the
        // button underneath does not hop as drivers are scanned in.
        _lblScanStatus.AutoSize = false;
        _lblScanStatus.Font = AppTheme.BoldFont;
        _lblScanStatus.Text = string.Empty;
        _lblScanStatus.Location = new Point(scanInset, scanY);
        _lblScanStatus.Size = new Size(scanWidth, AppTheme.BoldFont.Height * 2);

        scanY = UiLayout.Below(_lblScanStatus, UiLayout.RowGap);

        var btnBadges = new Button
        {
            Text = "Print QR Check-ins...",
            Location = new Point(scanInset, scanY),
            Font = AppTheme.BodyFont,
        };

        btnBadges.Size = new Size(UiLayout.WidthFor(btnBadges), UiLayout.ButtonHeight);
        btnBadges.Click += (_, _) => GenerateBadges();

        scanY = UiLayout.Below(btnBadges, UiLayout.RowGap);

        var btnPoster = new Button
        {
            Text = "Self Check-In Poster...",
            Location = new Point(scanInset, scanY),
            Font = AppTheme.BodyFont,
        };

        btnPoster.Size = new Size(UiLayout.WidthFor(btnPoster), UiLayout.ButtonHeight);
        btnPoster.Click += (_, _) => ShowSelfCheckInPoster();

        // The group is as tall as its contents. 250 was a guess, and the
        // Print button was falling out of the bottom of it.
        grpScan.Height = UiLayout.Below(btnPoster, UiLayout.RowGap);

        grpScan.Controls.Add(lblScanHelp);
        grpScan.Controls.Add(_txtScan);
        grpScan.Controls.Add(_lblScanStatus);
        grpScan.Controls.Add(btnBadges);
        grpScan.Controls.Add(btnPoster);

        // Everything the stretch pass moves, captured now that it exists.
        _grpScan = grpScan;

        _actionButtons = _tech is not null
            ? new[] { btnAddDriver, btnCheckInAll, btnCheckOutAll, btnInspect, _btnTechLog, _btnSignups }
            : new[] { btnAddDriver, btnCheckInAll, btnCheckOutAll, _btnSignups };

        Controls.Add(lblIntro);
        Controls.Add(_pnlNoDay);
        Controls.Add(lblFilter);
        Controls.Add(_cmbClassFilter);
        Controls.Add(_chkNeedsTech);
        Controls.Add(_lblSummary);
        Controls.Add(_grid);
        Controls.Add(grpScan);
        Controls.Add(btnAddDriver);
        Controls.Add(btnCheckInAll);
        Controls.Add(btnCheckOutAll);
        Controls.Add(btnInspect);
        Controls.Add(_btnTechLog);
        Controls.Add(_btnSignups);

        LayoutStretch();

        // This page places the grid and every button on itself, on every
        // resize - LayoutStretch is the authority, which the notes above
        // say twice. The repair pass was never told, so it measured the
        // grid at its STARTING 560x400 before the stretch had run and
        // shoved the buttons clear of a size that was never real: "moved
        // Inspect Selected Car 175px right", "moved Scan to Check In 185px
        // right", every launch.
        //
        // Two things moving the same control is worse than neither doing
        // it - the same reason Race History and Race Control keep it out.
        LayoutDoctor.LeaveAlone(this);
    }

    /// <summary>
    /// "No race day is open", and one press that opens one.
    ///
    /// This page has no empty state of its own otherwise: whatever the
    /// day's state it shows a full, working grid, and the only thing that
    /// ever said a race day was missing was the next-step card, one tab away.
    /// So the order of a first evening was: find Entries, tick everyone in, go
    /// one tab further, and read that none of it had been filed anywhere.
    /// </summary>
    private Panel BuildNoDayBanner()
    {
        var panel = new Panel
        {
            Width = 880,
            BackColor = AppTheme.Surface,
            Visible = false,
        };

        var headline = new Label
        {
            Text = "No race day is open.",
            AutoSize = true,
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.Warning,
        };

        var detail = new Label
        {
            Text = "Check-ins are filed against a race day, so ticks made now are not kept with tonight's racing "
                 + "and the heats cannot be built from them. Start one and this page starts counting.",
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextDark,
        };

        var start = new Button { Text = "Start a Race Day" };
        start.Click += (_, _) => StartRaceDayRequested?.Invoke();

        panel.Controls.Add(headline);
        panel.Controls.Add(detail);
        panel.Controls.Add(start);

        headline.Location = new Point(UiLayout.PageLeft, UiLayout.RowGap);
        detail.Location = new Point(UiLayout.PageLeft, UiLayout.Below(headline, UiLayout.HintGap));

        AppTheme.StyleButton(start, AppTheme.ButtonKind.Primary);
        start.Size = new Size(UiLayout.WidthFor(start), UiLayout.ButtonHeight);
        start.Location = new Point(UiLayout.PageLeft, UiLayout.Below(detail, UiLayout.RowGap));

        panel.Height = UiLayout.Below(start, UiLayout.RowGap);

        return panel;
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
        panel.Visible = wanted;

        // Everything under it moves, because the banner is in the flow
        // rather than floating over the page - a warning that covered the
        // class filter would be its own bug.
        ReflowUnderBanner();
    }

    /// <summary>
    /// Puts the filter row, the summary and the grid back under the banner
    /// - or back up where it was - after it has appeared or gone.
    /// </summary>
    private void ReflowUnderBanner()
    {
        if (_pnlNoDay is not { } panel || _cmbClassFilter.Parent is null)
        {
            return;
        }

        // The field, for the reason given in RefreshNoDayBanner: on a page
        // that is not being shown, panel.Visible is false whatever it was
        // set to, and this would lay the page out as if the banner were not
        // there while it was.
        var top = _noDayBannerShown
            ? UiLayout.Below(panel, UiLayout.RowGap)
            : panel.Top;

        var shift = top - _cmbClassFilter.Top;

        if (shift == 0)
        {
            return;
        }

        _cmbClassFilter.Top += shift;
        _chkNeedsTech.Top += shift;
        _lblSummary.Top += shift;
        _grid.Top += shift;

        if (_lblFilter is { } caption)
        {
            caption.Top += shift;
        }

        LayoutStretch();
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
        _btnSignups.Text = waiting > 0 ? $"Online Sign-ups ({Math.Min(waiting, 99)})" : "Online Sign-ups";

        // The caption just got longer or shorter, and the row is measured from
        // its captions. It used to be sized once, with room for a two-digit
        // count reserved up front; now that the row flows, the honest answer is
        // to flow it again.
        LayoutStretch();
    }

    /// <summary>
    /// The page against its real size: the scan panel pinned to the right
    /// edge, the action row pinned to the bottom, and the roster grid given
    /// everything in between. On the wide monitor this app now starts
    /// maximised on, the fixed 560x400 grid left half the page as empty
    /// background - the roster IS this page, so the roster gets the room.
    /// </summary>
    private void LayoutStretch()
    {
        if (_grpScan is not { } scan || _actionButtons.Length == 0)
        {
            return;
        }

        // Clamped to the minimum size, so a page measured before it is
        // docked lays out sanely rather than at its default 150px.
        var width = Math.Max(MinimumSize.Width, ClientSize.Width);
        var height = Math.Max(MinimumSize.Height, ClientSize.Height);

        scan.Left = width - UiLayout.PageRight - scan.Width;

        // Flowed against the real width, then the whole block dropped so its
        // last row rests on the bottom margin - rather than placed at a
        // guessed bottom, which either clips a second row or leaves a gap
        // under a single one.
        //
        // Flowed rather than laid in one row, because it is five buttons now.
        // Five fit across the page at the default text size and stop fitting a
        // notch or two up, and a single-row layout gets that wrong silently,
        // by running the last button off the right-hand edge where nothing
        // reports it.
        var flowBottom = UiLayout.FlowButtons(
            UiLayout.PageLeft, 0, width - UiLayout.PageRight, _actionButtons);

        var buttonsTop = height - UiLayout.PageTop - flowBottom;

        foreach (var button in _actionButtons)
        {
            button.Top += buttonsTop;
        }

        _grid.Width = Math.Max(400, scan.Left - UiLayout.SectionGap - _grid.Left);
        _grid.Height = Math.Max(200, buttonsTop - UiLayout.RowGap - _grid.Top);

        // Whatever is left of the filter row. See where it is built: it is
        // the only control on this page whose text can outgrow the window.
        _lblSummary.Width = Math.Max(160, width - UiLayout.PageRight - _lblSummary.Left);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutStretch();
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
            { CheckedInDriversOnly: true, IsClubNight: true } => "   —   tonight's races are built from checked-in drivers only",
            { CheckedInDriversOnly: true } => "   —   heats are built from checked-in drivers only",
            { IsClubNight: true } => "   —   everyone in a racing class is in tonight's races, checked in or not",
            _ => "   —   everyone in a racing class is in the heats, checked in or not",
        };

        _lblSummary.Text =
            $"{checkedInShown} of {shown.Count} checked in ({_checkIns.Count} total checked in across all classes){rule}";
    }
}
