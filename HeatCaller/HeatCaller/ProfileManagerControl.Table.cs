namespace HeatCaller;

/// <summary>
/// The Driver Hub rework (7 Nov 2026): the Card view's new layout. The
/// Table view it also brought in went on 23 Nov 2026 (Asher: "remove the
/// table tab from the driver hub") - the Card view's roster and the Garage
/// view are the page now.
///
/// WHY: the page had eight buttons in two rows along its foot - the
/// roster's (Add Driver, Delete), the driver's (Edit Profile, Photo) and the
/// four rare whole-list jobs (Check Names, Download Template, Paste a Table,
/// Import from CSV) - four of them in the accent colour and Delete a big
/// pink button beside Add. Each now sits with what it acts on: Add over the
/// roster, the whole-list jobs in an Import menu at its foot, Edit and Photo
/// beside the driver's name, and Delete in that driver's "..." menu, away
/// from Add.
/// </summary>
public sealed partial class ProfileManagerControl
{
    /// <summary>The whole-list jobs - paste, import, template, check names - one menu where there were four buttons.</summary>
    private readonly ContextMenuStrip _importMenu = new();

    /// <summary>The driver's own less everyday things: the report card, and Delete, set apart.</summary>
    private readonly ContextMenuStrip _driverMenu = new();
    private ToolStripMenuItem? _miReportCard;
    private ToolStripMenuItem? _miDelete;

    /// <summary>
    /// The roster's Import button, kept so it can be styled plain: the theme
    /// colours a caption with "import" in it as the page's main action, and
    /// this is the least used button on it.
    /// </summary>
    private readonly Button _btnRosterImport = new() { Text = "Import ▾" };

    /// <summary>How many drivers, and how many are in tonight - under the roster.</summary>
    private readonly Label _lblRosterCount = new();

    /// <summary>Tonight, Tech, Race days and Best lap, under the driver's name.</summary>
    private readonly StatTile[] _headerTiles = { new(), new(), new(), new() };

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

        // Export and Forget (15 Nov 2026, privacy plan 2.5) - see ProfileManagerControl.Forget.
        BuildPrivacyMenuItems();
        _driverMenu.Items.AddRange(new ToolStripItem[] { _miReportCard, _miExport, new ToolStripSeparator(), _miDelete, _miForget });

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

            // The same drivers Delete will act on (10 Nov 2026, bug hunt 16).
            var picked = DeleteTargets().Count;
            _miDelete.Text = picked > 1 ? $"Delete {Plural.Of(picked, "Driver")}..." : $"Delete {named}...";
            ShowPrivacyMenuItems(named);
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

    /// <summary>The roster's Import plain - see _btnRosterImport.</summary>
    private void StyleTableButtons() => AppTheme.StyleButton(_btnRosterImport, AppTheme.ButtonKind.Secondary);
}
