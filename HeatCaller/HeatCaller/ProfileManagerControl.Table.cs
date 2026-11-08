namespace HeatCaller;

/// <summary>
/// The Driver Hub rework (7 Nov 2026): the Card view's new layout and the
/// Table view.
///
/// WHY: the page had eight buttons in two rows along its foot - the
/// roster's (Add Driver, Delete), the driver's (Edit Profile, Photo) and the
/// four rare whole-list jobs (Check Names, Download Template, Paste a Table,
/// Import from CSV) - four of them in the accent colour and Delete a big
/// pink button beside Add. Each now sits with what it acts on: Add over the
/// roster, the whole-list jobs in an Import menu at its foot, Edit and Photo
/// beside the driver's name, and Delete in that driver's "..." menu, away
/// from Add.
///
/// The Table view is every driver in a sortable table - number, classes,
/// car, transponder and whether they are checked in tonight - for scanning
/// a big roster on race night, with the driver picked in a drawer beside it.
/// The drawer's buttons open the driver on the Card view at the tab wanted,
/// rather than copying the tabs into it.
/// </summary>
public sealed partial class ProfileManagerControl
{
    /// <summary>The drawer beside the table, at the normal text size.</summary>
    private const int DrawerWidth = 290;

    /// <summary>The picture in the drawer, at the normal text size.</summary>
    private const int DrawerAvatar = 56;

    /// <summary>The whole-list jobs - paste, import, template, check names - one menu where there were four buttons.</summary>
    private readonly ContextMenuStrip _importMenu = new();

    /// <summary>The driver's own less everyday things: the report card, and Delete, set apart.</summary>
    private readonly ContextMenuStrip _driverMenu = new();
    private ToolStripMenuItem? _miReportCard;
    private ToolStripMenuItem? _miDelete;

    /// <summary>
    /// The two Import buttons, kept so they can be styled plain: the theme
    /// colours a caption with "import" in it as the page's main action, and
    /// these are the least used buttons on it.
    /// </summary>
    private readonly Button _btnRosterImport = new() { Text = "Import ▾" };
    private readonly Button _btnTableImport = new() { Text = "Import ▾" };

    /// <summary>How many drivers, and how many are in tonight - under the roster.</summary>
    private readonly Label _lblRosterCount = new();

    /// <summary>Tonight, Tech, Race days and Best lap, under the driver's name.</summary>
    private readonly StatTile[] _headerTiles = { new(), new(), new(), new() };

    // --- the table ---

    /// <summary>The Table view: the table and its bar on the left, the drawer on the right.</summary>
    private Control? _tableView;
    private Beside? _tableBand;

    private readonly TextBox _tableSearch = new();
    private readonly DataGridView _tableGrid = new();
    private readonly Button _btnShowAll = new() { Text = "All" };
    private readonly Button _btnShowIn = new() { Text = "In tonight" };
    private readonly Button _btnShowOut = new() { Text = "Not in" };

    /// <summary>Which drivers the table shows: null for all, true for those in tonight, false for those not.</summary>
    private bool? _tableShowsIn;

    /// <summary>Set while the table is being filled or moved to follow the card, so that picks nobody.</summary>
    private bool _fillingTable;

    private readonly DrawerAvatarBox _drawerAvatar = new();
    private readonly Label _drawerName = new();
    private readonly Label _drawerSub = new();
    private readonly Label _drawerDetails = new();
    private readonly Button _btnDrawerEdit = new() { Text = "Edit..." };
    private readonly Button _btnDrawerPhoto = new() { Text = "Photo..." };
    // "⋯", not three dots: the theme takes a trailing "..." off every
    // caption (AppTheme.Caption), which would leave this one blank.
    private readonly Button _btnDrawerMore = new() { Text = "⋯" };

    // ------------------------------------------------------------ the card

    /// <summary>
    /// The page with the garage: the Card | Table | Garage switch, and under
    /// it the view chosen. No buttons along the foot - see the class notes.
    /// </summary>
    private void BuildRoomyLayout()
    {
        BuildMenus();

        // --- The roster: Drivers and + Add, the search, the list, and the count and Import under it ---
        var lblDrivers = new Label { Text = "Drivers", AutoSize = true, Font = AppTheme.BoldFont, Anchor = AnchorStyles.Left };
        _textSize.Wear(lblDrivers, () => AppTheme.BoldFont);

        var btnAdd = new Button { Text = "+ Add..." };
        btnAdd.Click += (_, _) => AddNew();
        AccessibleNames(btnAdd, "Add a driver");

        var btnImport = _btnRosterImport;
        btnImport.Click += (_, _) => _importMenu.Show(btnImport, new Point(0, btnImport.Height));

        _lblRosterCount.AutoSize = true;
        _lblRosterCount.Anchor = AnchorStyles.Left;
        _lblRosterCount.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblRosterCount, () => AppTheme.SmallFont);

        var roster = new Section();

        roster.Add(new SplitBar(new ButtonBar().With(lblDrivers), new ButtonBar(tight: true, alignRight: true).With(btnAdd)), gapBelow: UiLayout.RowGap);
        roster.Add(_txtSearch);
        roster.AddFill(_list, LeastListHeight, gapBelow: UiLayout.RowGap);
        roster.Add(new SplitBar(new ButtonBar().With(_lblRosterCount), new ButtonBar(tight: true, alignRight: true).With(btnImport)), gapBelow: 0);

        _textSize.Keep(FitColumns);
        _textSize.Keep(FitRowHeight);
        _list.ClientSizeChanged += (_, _) => FitColumns();

        // --- The driver's card: their picture, name and chips, with Edit, Photo and ... beside them ---
        _btnCardEdit.Text = "Edit...";
        _btnCardPhoto.Text = "Photo...";

        var btnMore = new Button { Text = "⋯" };
        btnMore.Click += (_, _) => _driverMenu.Show(btnMore, new Point(0, btnMore.Height));
        AccessibleNames(btnMore, "More for this driver - report card, delete");

        var actions = new ButtonBar(tight: true).With(_btnCardEdit, _btnCardPhoto, btnMore);

        var headerBand = new Beside(() => actions.GetPreferredSize(new Size(100000, 0)).Width) { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        headerBand.Add(BuildHeader(), gapBelow: 0);
        headerBand.AddSideMeasured(actions, gapBelow: 0);

        // Tonight at a glance, under the name - what a race director opens
        // the page for on the night.
        foreach (var tile in _headerTiles)
        {
            _textSize.Wear(tile.Value, () => AppTheme.TitleFont);
            _textSize.Wear(tile.Caption, () => AppTheme.MicroFont);
        }

        var card = new Section();

        card.Add(headerBand, gapBelow: UiLayout.RowGap);
        card.Add(new TileStrip(_headerTiles), gapBelow: UiLayout.SectionGap);
        card.AddFill(BuildDriversView(), DriversViewLeastHeight, gapBelow: 0);

        Beside? band = null;

        band = new Beside(() => RosterWidth(band?.Width ?? 0)) { SideOnLeft = true };
        band.AddFill(card, card.LeastHeight);
        band.AddSideFill(roster, roster.LeastHeight, gapBelow: 0);

        // --- The page ---
        var page = new FillPage();

        page.Add(_unknownBanner);
        page.Add(BuildTopRow(), gapBelow: UiLayout.RowGap);
        page.AddFill(BuildViews(band, bottomBar: null), ViewsLeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>The Import menu and the driver's ... menu, each built once and shared by the views.</summary>
    private void BuildMenus()
    {
        _importMenu.Items.AddRange(new ToolStripItem[]
        {
            // The same import, from a table copied off a web page or out of
            // a spreadsheet rather than saved as a file (PastedTable).
            new ToolStripMenuItem("Paste a Table...", null, (_, _) => ImportPastedTable()),
            new ToolStripMenuItem("Import from CSV...", null, (_, _) => ImportFromCsv()),
            new ToolStripMenuItem("Download Template...", null, (_, _) => DownloadTemplate()),
            new ToolStripSeparator(),

            // One pass down every name, as the announcer will say it.
            new ToolStripMenuItem("Check Names...", null, (_, _) => CheckNames()),
        });

        _miReportCard = new ToolStripMenuItem("Report Card...", null, (_, _) => ShowReportCard());
        _miDelete = new ToolStripMenuItem("Delete Driver...", null, (_, _) => DeleteSelected()) { Tag = AppTheme.DangerMenuItem };

        _driverMenu.Items.AddRange(new ToolStripItem[] { _miReportCard, new ToolStripSeparator(), _miDelete });

        _driverMenu.Opening += (_, e) =>
        {
            var profile = CardProfile();

            if (profile is null)
            {
                e.Cancel = true;
                return;
            }

            var named = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

            _miReportCard.Visible = ReportCardFor is not null;
            _miReportCard.Text = $"Report Card for {named}...";

            var picked = _list.SelectedItems.Count;
            _miDelete.Text = picked > 1 ? $"Delete {Plural.Of(picked, "Driver")}..." : $"Delete {named}...";
        };

        AppTheme.StyleContextMenu(_importMenu);
        AppTheme.StyleContextMenu(_driverMenu);
    }

    private static void AccessibleNames(Control control, string name) => control.AccessibleName = name;

    /// <summary>
    /// Tonight, Tech, Race days and Best lap for the driver shown - read
    /// live from the race-day stores, the best lap from their worked-out
    /// stats once those are back.
    /// </summary>
    private void RefreshHeaderTiles()
    {
        var profile = CardProfile();

        if (profile is null)
        {
            foreach (var tile in _headerTiles)
            {
                tile.Set("-", " ");
            }

            return;
        }

        var day = DriverStats.RaceDayFor(profile, _sources);
        var stats = _sources?.History is null ? null : _statsCache.GetValueOrDefault(profile.Transponder);

        _headerTiles[0].Set(day?.CheckedIn switch { true => "Checked in", false => "Not in yet", _ => "-" }, "Tonight");
        _headerTiles[0].Value.ForeColor = day?.CheckedIn == true ? AppTheme.Success : AppTheme.TextDark;

        var tech = day?.Tech switch
        {
            null => "-",
            "Not inspected yet" => "Not yet",
            { } text => text.Split(" at ", 2)[0],
        };

        _headerTiles[1].Set(tech, "Tech");
        _headerTiles[1].Value.ForeColor = day?.TechFailed == true ? AppTheme.Danger : AppTheme.TextDark;

        _headerTiles[2].Set(day is null ? "-" : day.EventsAttended.ToString("N0"), "Race days");
        _headerTiles[3].Set(stats?.BestLap is { } best ? Lap(best.LapMs) : "-", "Best lap");
    }

    /// <summary>"10 drivers · 4 in tonight" under the roster.</summary>
    private void RefreshRosterCount()
    {
        var all = _store.All.Count;
        var count = Plural.Of(all, "driver");

        if (_sources?.CheckIns is { } checkIns)
        {
            var inTonight = _store.All.Count(p => checkIns.IsCheckedIn(p.Transponder));
            count += $"  ·  {inTonight} in tonight";
        }

        _lblRosterCount.Text = count;
    }

    // ------------------------------------------------------------ the table

    /// <summary>
    /// The Table view: the search, the All | In tonight | Not in filter,
    /// Import and + Add Driver over the table, taking the height; the drawer
    /// for the driver picked down the right.
    /// </summary>
    private Control BuildTableView()
    {
        _tableSearch.PlaceholderText = "Find a driver - name, number, car or transponder";
        _tableSearch.TextChanged += (_, _) => RefreshTable();

        foreach (var (button, shows) in new[] { (_btnShowAll, (bool?)null), (_btnShowIn, true), (_btnShowOut, false) })
        {
            button.Click += (_, _) =>
            {
                _tableShowsIn = shows;
                StyleTableButtons();
                RefreshTable();
            };
        }

        var btnImport = _btnTableImport;
        btnImport.Click += (_, _) => _importMenu.Show(btnImport, new Point(0, btnImport.Height));

        var btnAdd = new Button { Text = "+ Add Driver..." };
        btnAdd.Click += (_, _) => AddNew();

        // The search as wide as the roster's: the bar's own room is for
        // the filter and the buttons.
        _textSize.Keep(() => _tableSearch.Width = UiLayout.AtTextSize(300));

        var bar = new SplitBar(
            new ButtonBar(tight: true).With(_tableSearch, _btnShowAll, _btnShowIn, _btnShowOut),
            new ButtonBar(tight: true, alignRight: true).With(btnAdd, btnImport));

        // --- The table ---
        _tableGrid.ReadOnly = true;
        _tableGrid.AllowUserToAddRows = false;
        _tableGrid.AllowUserToDeleteRows = false;
        _tableGrid.AllowUserToResizeRows = false;
        _tableGrid.RowHeadersVisible = false;
        _tableGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _tableGrid.MultiSelect = false;
        _tableGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _tableGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "number", HeaderText = "No.", FillWeight = 26 },
            new DataGridViewTextBoxColumn { Name = "classes", HeaderText = "Classes", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "car", HeaderText = "Car", FillWeight = 80 },
            new DataGridViewTextBoxColumn { Name = "transponder", HeaderText = "Transponder", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "tonight", HeaderText = "Tonight", FillWeight = 50 });

        // The number sorted as a number: 7 before 12.
        _tableGrid.SortCompare += (_, e) =>
        {
            if (e.Column.Name != "number")
            {
                return;
            }

            static int Of(object? value) => int.TryParse(value?.ToString(), out var n) ? n : int.MaxValue;

            e.SortResult = Of(e.CellValue1).CompareTo(Of(e.CellValue2));
            e.Handled = true;
        };

        _tableGrid.SelectionChanged += (_, _) =>
        {
            if (_fillingTable || _tableGrid.CurrentRow?.Tag is not string transponder)
            {
                return;
            }

            SelectDriver(transponder);
        };

        _tableGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                EditSelected();
            }
        };

        _tableGrid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelected();
                e.Handled = true;
            }
        };

        Pieces.KeepHeadingsWhole(_tableGrid, ("number", "999"), ("tonight", "Checked in"));

        // --- The drawer ---
        var drawer = BuildDrawer();

        _tableBand = new Beside(() => UiLayout.AtTextSize(DrawerWidth));
        _tableBand.Add(bar, gapBelow: UiLayout.RowGap);
        _tableBand.AddFill(_tableGrid, _ => _tableGrid.ColumnHeadersHeight + 4 * _tableGrid.RowTemplate.Height + 2, gapBelow: 0);
        _tableBand.AddSideFill(drawer, drawer.LeastHeight, gapBelow: 0);

        return _tableBand;
    }

    /// <summary>
    /// The drawer: the driver's picture, name, number and classes, their
    /// Edit, Photo and ..., what is on file and tonight, and a button for
    /// each of the card's tabs that opens them on the Card view there.
    /// </summary>
    private Section BuildDrawer()
    {
        _drawerName.AutoSize = true;
        _textSize.Wear(_drawerName, () => AppTheme.HeadingFont);

        _drawerSub.AutoSize = true;
        _drawerSub.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_drawerSub, () => AppTheme.SmallFont);

        _drawerDetails.AutoSize = true;

        _btnDrawerEdit.Click += (_, _) => EditSelected();
        _btnDrawerPhoto.Click += (_, _) => QuickChangePhoto();
        _btnDrawerMore.Click += (_, _) => _driverMenu.Show(_btnDrawerMore, new Point(0, _btnDrawerMore.Height));
        AccessibleNames(_btnDrawerMore, "More for this driver - report card, delete");

        _textSize.Keep(() => _drawerAvatar.Size = new Size(UiLayout.AtTextSize(DrawerAvatar), UiLayout.AtTextSize(DrawerAvatar)));
        _drawerAvatar.Profile = CardProfile;

        var lblOpen = new Label { Text = "Open on the card", AutoSize = true, ForeColor = AppTheme.TextMuted };
        _textSize.Wear(lblOpen, () => AppTheme.SmallFont);

        var tabButtons = _tabOrder
            .Select(tab =>
            {
                var button = new Button { Text = tab.ToString() };
                button.Click += (_, _) =>
                {
                    SelectView(DriverView.Card);
                    SelectCardTab(tab);
                };

                return (Control)button;
            })
            .ToArray();

        var drawer = new Section();

        drawer.Add(_drawerAvatar, stretch: false, gapBelow: UiLayout.RowGap);
        drawer.Add(_drawerName, gapBelow: UiLayout.HintGap);
        drawer.Add(_drawerSub, gapBelow: UiLayout.RowGap);
        drawer.Add(new ButtonBar(tight: true).With(_btnDrawerEdit, _btnDrawerPhoto, _btnDrawerMore), gapBelow: UiLayout.SectionGap);
        drawer.Add(_drawerDetails, gapBelow: UiLayout.SectionGap);
        drawer.Add(lblOpen, gapBelow: UiLayout.HintGap);
        drawer.Add(new ButtonBar(tight: true).With(tabButtons), gapBelow: 0);

        return drawer;
    }

    /// <summary>The table filled again from the drivers on file, the search and the filter - on the driver the card shows.</summary>
    private void RefreshTable()
    {
        if (_tableView is null || _view != DriverView.Table)
        {
            return;
        }

        var filter = _tableSearch.Text.Trim();
        var checkIns = _sources?.CheckIns;
        var shown = CardProfile()?.Transponder;

        _fillingTable = true;

        try
        {
            var sortedBy = _tableGrid.SortedColumn;
            var direction = _tableGrid.SortOrder;

            _tableGrid.Rows.Clear();

            foreach (var p in _store.All)
            {
                var checkedIn = checkIns?.IsCheckedIn(p.Transponder);

                if ((filter.Length > 0 && !Matches(p, filter))
                    || (_tableShowsIn is { } wanted && (checkedIn ?? false) != wanted))
                {
                    continue;
                }

                var index = _tableGrid.Rows.Add(
                    string.IsNullOrWhiteSpace(p.DriverName) ? p.Transponder : p.DriverName.Trim(),
                    p.RaceNumber?.Trim() ?? string.Empty,
                    string.Join(", ", p.AllClasses),
                    p.CarModel?.Trim() ?? string.Empty,
                    p.Transponder,
                    checkedIn switch { true => "Checked in", false => "Not yet", _ => string.Empty });

                var row = _tableGrid.Rows[index];
                row.Tag = p.Transponder;

                if (checkedIn == true)
                {
                    row.Cells["tonight"].Style.ForeColor = AppTheme.Success;
                }
            }

            if (sortedBy is not null && direction != SortOrder.None)
            {
                _tableGrid.Sort(sortedBy, direction == SortOrder.Descending
                    ? System.ComponentModel.ListSortDirection.Descending
                    : System.ComponentModel.ListSortDirection.Ascending);
            }

            // On the driver the card shows, when the filter leaves them in.
            _tableGrid.ClearSelection();

            foreach (DataGridViewRow row in _tableGrid.Rows)
            {
                if (string.Equals(row.Tag as string, shown, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    _tableGrid.CurrentCell = row.Cells[0];
                    break;
                }
            }
        }
        finally
        {
            _fillingTable = false;
        }

        RefreshDrawer();
    }

    /// <summary>The drawer on the driver the card shows.</summary>
    private void RefreshDrawer()
    {
        if (_tableView is null)
        {
            return;
        }

        var profile = CardProfile();

        _btnDrawerEdit.Enabled = profile is not null;
        _btnDrawerPhoto.Enabled = profile is not null;
        _btnDrawerMore.Enabled = profile is not null;
        _drawerAvatar.Invalidate();

        if (profile is null)
        {
            _drawerName.Text = _store.All.Count == 0 ? "No drivers yet" : "Pick a driver";
            _drawerSub.Text = string.Empty;
            _drawerDetails.Text = string.Empty;
            return;
        }

        _drawerName.Text = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

        var sub = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile.RaceNumber))
        {
            sub.Add("#" + profile.RaceNumber.Trim());
        }

        if (profile.AllClasses.Count > 0)
        {
            sub.Add(string.Join(", ", profile.AllClasses));
        }

        _drawerSub.Text = string.Join("  ·  ", sub);

        var day = DriverStats.RaceDayFor(profile, _sources);
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile.CarModel))
        {
            var others = profile.AllCars.Count - 1;
            lines.Add($"Car:  {profile.CarModel.Trim()}" + (others > 0 ? $"  (+{others} more)" : string.Empty));
        }

        lines.Add($"Transponder:  {string.Join(", ", profile.AllTransponderIds)}");

        if (day?.CheckedIn is { } checkedIn)
        {
            lines.Add($"Tonight:  {(checkedIn ? "checked in" : "not in yet")}");
        }

        if (day?.Tech is { } tech)
        {
            lines.Add($"Tech:  {tech}");
        }

        _drawerDetails.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>The table's filter lit for the one in use; the drawer's buttons and both Imports plain.</summary>
    private void StyleTableButtons()
    {
        AppTheme.StyleButton(_btnRosterImport, AppTheme.ButtonKind.Secondary);

        if (_tableView is null)
        {
            return;
        }

        AppTheme.StyleButton(_btnTableImport, AppTheme.ButtonKind.Secondary);

        static AppTheme.ButtonKind Lit(bool on) => on ? AppTheme.ButtonKind.Primary : AppTheme.ButtonKind.Secondary;

        AppTheme.StyleButton(_btnShowAll, Lit(_tableShowsIn is null));
        AppTheme.StyleButton(_btnShowIn, Lit(_tableShowsIn == true));
        AppTheme.StyleButton(_btnShowOut, Lit(_tableShowsIn == false));
        AppTheme.StyleButton(_btnDrawerEdit, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnDrawerPhoto, AppTheme.ButtonKind.Secondary);
    }

    /// <summary>The drawer's picture: the driver's photo, or their initials disc, round.</summary>
    private sealed class DrawerAvatarBox : Control
    {
        public Func<DriverProfile?>? Profile { get; set; }

        public DrawerAvatarBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = AppTheme.Surface;
            Tag = AppTheme.KeepBackColor;
        }

        public override Size GetPreferredSize(Size proposedSize) => Size;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(AppTheme.Surface);

            if (Profile?.Invoke() is not { } profile)
            {
                return;
            }

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var display = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();
            var side = Math.Min(Width, Height) - 1;

            DriverPhotos.DrawAvatar(e.Graphics, new Rectangle(0, 0, side, side), display, profile.PhotoFileName, cornerRadius: side / 2);
        }
    }
}
