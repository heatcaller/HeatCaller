namespace HeatCaller;

/// <summary>
/// The Driver Hub page: a searchable roster on the left, and the
/// selected driver writ large on the right.
///
/// This used to be a nine-column table, which answered "what is stored"
/// and nothing else. The drivers are the whole point of a race night, and
/// a page about them should put a face and a name first - so the list
/// carries a photo (or an initials disc that always lands on the same
/// colour for the same name) and only the columns worth scanning, while
/// the card on the right gives one driver the poster treatment: photo,
/// name, and then their story in numbers - races, wins, podiums, form,
/// personal bests, championship places, badges, their rival - before the
/// stored details.
///
/// Editing still happens in ProfileForm; this page decides what a driver
/// LOOKS like, not what they are.
/// </summary>
public sealed partial class ProfileManagerControl : UserControl
{

    /// <summary>The roster's width range at the normal text size. It takes about a quarter of the page between these.</summary>
    private const int MinRosterWidth = 230;
    private const int MaxRosterWidth = 320;

    /// <summary>
    /// Room between what is painted on the card and the canvas's edge. The
    /// card's own inset (Section.Inset) is the rest - this is only a hair,
    /// so a photo's rounded corners never touch the canvas's scrollbar.
    /// </summary>
    private const float CardPad = 4f;

    /// <summary>The least the card's canvas can do with at the normal text size - a photo and a name. Below that the page scrolls instead.</summary>
    private const int LeastCanvasHeight = 200;

    /// <summary>The roster's columns at the normal text size: driver and number. The driver's takes whatever the number leaves instead - see FitColumns.</summary>
    private static readonly int[] ColumnWidths = { 160, 56 };

    /// <summary>The least the driver column gets at the normal text size - a short name. Past that, the list scrolls sideways.</summary>
    private const int DriverNameLeast = 110;

    /// <summary>Set while the roster's columns are being sized - which can bring a scrollbar in or take it away, and so change the room they are being sized to. See FitColumns.</summary>
    private bool _fittingColumns;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    private readonly ProfileStore _store;
    private readonly UndoService _undo;
    private readonly RaceClassStore _classStore;
    private readonly TextBox _txtSearch = new();
    private readonly ListView _list = new();
    /// <summary>No pictures in it: it is there for the roster's row height - see FitRowHeight.</summary>
    private readonly ImageList _thumbs = new() { ImageSize = new Size(1, 32), ColorDepth = ColorDepth.Depth32Bit };

    /// <summary>Where the driver is painted. Scrolls, because a driver with a long record is taller than any window.</summary>
    private readonly CardCanvas _cardScroll = new();

    private readonly Button _btnCardEdit = new();
    private readonly Button _btnCardPhoto = new();

    // ---- Stats ----
    //
    // Worked out once per driver and kept until the data behind them
    // changes, never in the paint handler: a card repaints on every scroll
    // step, and walking the whole race history each time would make it
    // crawl on a club with a few seasons behind it.

    /// <summary>Where the stats come from. Null when the page was built without them - the card then shows the profile alone.</summary>
    private readonly DriverStatsSources? _sources;

    /// <summary>Finished stats by main transponder, for the current version of the data.</summary>
    private readonly Dictionary<string, DriverStats> _statsCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Drivers whose stats are being worked out right now, so a burst of selection events starts one calculation, not five.</summary>
    private readonly HashSet<string> _statsPending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The copy of the stores the sums read, shared by every driver until the data changes.</summary>
    private DriverStatsSnapshot? _statsSnapshot;

    /// <summary>
    /// Bumped whenever the data changes. A background calculation that
    /// comes back carrying an older number is thrown away rather than
    /// cached - it was worked out from history that has since moved on.
    /// </summary>
    private int _statsVersion;

    /// <summary>
    /// The driver the card is showing. Kept for two reasons: choosing a
    /// different one starts them at the top of the card, and - see
    /// CardProfile - the card stays on this driver whenever the roster has
    /// nothing highlighted, rather than going blank.
    /// </summary>
    private string? _shownTransponder;

    /// <summary>Where the last driver viewed is remembered between runs. Null when the page was built without it.</summary>
    private readonly AppSettings? _settings;

    /// <summary>
    /// Writes the remembered driver once the selection has settled.
    ///
    /// Arrowing down a forty-driver roster changes the shown driver forty
    /// times in a couple of seconds, and each of those would otherwise be a
    /// write of the whole settings file. Only the one you stop on matters.
    /// </summary>
    private readonly System.Windows.Forms.Timer _rememberTimer = new() { Interval = 800 };

    private bool _measuring;
    private bool _extentCheckQueued;

    /// <summary>The banner listing transponders nobody has - see UnknownTransponders.</summary>
    private readonly UnknownTransponderBanner _unknownBanner = new();

    /// <summary>Shows the app's list of unknown transponders on this page's banner.</summary>
    public void AttachUnknownTransponders(UnknownTransponders unknowns) => _unknownBanner.Attach(unknowns);

    /// <summary>
    /// Reads a line out in the club's own announcer voice - for Check
    /// Names... and for the Hear It beside a driver's Announcer name.
    ///
    /// A delegate rather than the AnnouncementService itself, because the
    /// speed the club has set lives on the voice settings and this page has
    /// no other use for either of them: the two things the caller knows and
    /// this page does not are the service and that speed, and one line
    /// hands over both. Settable rather than a constructor argument for the
    /// reason VoiceSettingsControl.Announcer is - every other page is built
    /// here with the stores it reads, and the announcer is not a store.
    ///
    /// Null means there is no voice on this page: Check Names... still
    /// lists what will be read out, and Hear It is left off the profile
    /// dialog rather than shown doing nothing.
    ///
    /// Wire it where this page is built:
    ///     driverProfiles.SpeakName = text =&gt; _announcer.SpeakTest(text, _voiceStore.Current.Speed);
    /// </summary>
    public Action<string>? SpeakName { get; set; }

    public ProfileManagerControl(
        ProfileStore store,
        RaceClassStore classStore,
        UndoService undo,
        DriverStatsSources? statsSources = null,
        AppSettings? settings = null,
        DriverGarageSources? garage = null)
    {
        _garageSources = garage;
        _store = store;
        _undo = undo;
        _classStore = classStore;
        _sources = statsSources;
        _settings = settings;

        // Whoever was on the card when the app last closed. The first
        // RefreshList selects them; if they have since been deleted it falls
        // back to the top of the roster - see SelectShownOrFirst.
        _shownTransponder = settings?.LastDriverProfileTransponder is { Length: > 0 } last ? last : null;

        _rememberTimer.Tick += (_, _) => RememberShownNow();

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once: set once, it stayed at
        // the size the page was built at when the text size changed. See
        // TextSize.
        //
        // No MinimumSize. It was 900x480, which WinForms honours over the
        // size the page is given - so on a small screen the page was wider
        // and taller than its window, and the buttons under the roster were
        // off the bottom. A short window scrolls the page instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        _txtSearch.PlaceholderText = "Name, number, car or transponder";
        _txtSearch.TextChanged += (_, _) => RefreshList();

        _list.View = View.Details;
        _list.FullRowSelect = true;
        // Ctrl-click for individual rows, Shift-click for a range.
        _list.MultiSelect = true;
        // No headings: a name and a number say what they are.
        _list.HeaderStyle = ColumnHeaderStyle.None;
        _list.SmallImageList = _thumbs;
        _list.Columns.Add("Driver");
        _list.Columns.Add("#", 50, HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => EditSelected();
        _list.SelectedIndexChanged += (_, _) => OnSelectionChanged();

        // The card is the theme's (a Section, built in BuildLayout); the
        // canvas inside it paints the driver and scrolls. Surface-coloured,
        // and told to keep it, so the theme pass does not turn the inside of
        // the card into page background.
        _cardScroll.AutoScroll = true;
        _cardScroll.BackColor = AppTheme.Surface;
        _cardScroll.Tag = AppTheme.KeepBackColor;
        _cardScroll.Paint += (_, e) => PaintCardCanvas(e.Graphics);
        // Client size rather than size: a scrollbar arriving narrows the
        // canvas without resizing it, and the text has to re-wrap for that.
        _cardScroll.ClientSizeChanged += (_, _) =>
        {
            UpdateCardExtent();
            _cardScroll.Invalidate();
        };
        _cardScroll.HandleCreated += (_, _) => UpdateCardExtent();

        _btnCardEdit.Text = "Edit Profile...";
        _btnCardEdit.Click += (_, _) => EditSelected();
        _btnCardPhoto.Text = "Photo...";
        _btnCardPhoto.Click += (_, _) => QuickChangePhoto();

        // 22 Oct 2026: tonight's report card for the driver - a right-click
        // on the roster. A menu rather than another button on the foot,
        // which is full already. See CoachReport.
        var cardMenu = new ContextMenuStrip();
        var cardItem = new ToolStripMenuItem("Report Card...", null, (_, _) => ShowReportCard());
        cardMenu.Items.Add(cardItem);
        cardMenu.Opening += (_, e) =>
        {
            var profile = CardProfile();
            cardItem.Text = profile is { DriverName.Length: > 0 } ? $"Report Card for {profile.DriverName}..." : "Report Card...";
            e.Cancel = ReportCardFor is null || profile is null;
        };
        _list.ContextMenuStrip = cardMenu;

        BuildLayout();

        // A race finishing while this page is open changes the numbers on
        // the card. Raised on the UI thread today, but posted rather than
        // run inline so the race's own wrap-up (badges are awarded straight
        // after the save) has finished before the card reads its results.
        if (_sources?.History is not null)
        {
            _sources.History.SessionAdded += OnSessionAdded;
        }

        RefreshList();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
        RestyleSegmentsAfterThemePass();
    }

    /// <summary>A new text size, reaching this page through the window's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
        RestyleSegmentsAfterThemePass();
    }

    /// <summary>
    /// The theme pass that follows a new text size styles every button by
    /// its caption, which leaves no tab or view looking chosen: once it has
    /// run (the message queue is reached after it), they are marked again.
    /// </summary>
    private void RestyleSegmentsAfterThemePass()
    {
        if (_garageSources is not null && IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed)
                {
                    StyleSegments();
                }
            });
        }
    }

    /// <summary>
    /// Two cards side by side, on the layout pieces (LayoutPieces.cs): the
    /// roster down the left - a heading with Add Driver, the search box,
    /// the list taking the height that is left, and the buttons under it -
    /// and beside it the driver's card, taking the rest of the width.
    /// Nothing is placed by hand.
    ///
    /// The roster is names and numbers only, and narrow (RosterWidth): the
    /// driver is the page, and the list is how you get to them. It was the
    /// wide one once, with class, car and transponder columns, and the card
    /// squeezed into two fifths beside it.
    ///
    /// With the garage (the page MainForm builds), the card is a header -
    /// the driver's picture, name and chips, with the Drivers | Garage
    /// switch and Edit Profile - over the two views (see the Views half of
    /// this class). Without it (the layout catalogue), the painted card is
    /// the whole driver, picture first, with Edit Profile and Photo under it.
    /// </summary>
    private void BuildLayout()
    {
        // --- The roster: names and numbers, and Find over them ---
        var lblFind = new Label
        {
            Text = "Find",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        var roster = new Section();

        roster.Add(lblFind, stretch: false, gapBelow: UiLayout.HintGap);
        roster.Add(_txtSearch);
        roster.AddFill(_list, LeastListHeight, gapBelow: 0);

        // The columns fitted to the roster at the text size in use, and
        // again whenever the roster's width changes; the rows as tall as
        // two lines of it.
        _textSize.Keep(FitColumns);
        _textSize.Keep(FitRowHeight);
        _list.ClientSizeChanged += (_, _) => FitColumns();

        // --- The driver's card ---
        var card = new Section();

        if (_garageSources is not null)
        {
            // Their picture, name and chips, then the tabs.
            card.Add(BuildHeader(), gapBelow: UiLayout.SectionGap);
            card.AddFill(BuildDriversView(), DriversViewLeastHeight, gapBelow: 0);
        }
        else
        {
            // No caption: the driver's name is its heading.
            card.AddFill(_cardScroll, () => UiLayout.AtTextSize(LeastCanvasHeight), gapBelow: 0);
        }

        Beside? band = null;

        band = new Beside(() => RosterWidth(band?.Width ?? 0)) { SideOnLeft = true };

        band.AddFill(card, card.LeastHeight);
        band.AddSideFill(roster, roster.LeastHeight, gapBelow: 0);

        // --- The buttons along the foot, each group under what it is about ---
        //
        // The roster's (a new driver, or the ones picked gone) on the left;
        // the driver shown's, where their card starts; and the ones for the
        // whole list at once - from a spreadsheet, or a blank one to fill
        // in - against the right.
        var btnAdd = new Button { Text = "Add Driver..." };
        btnAdd.Click += (_, _) => AddNew();
        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += (_, _) => DeleteSelected();
        var btnImport = new Button { Text = "Import from CSV..." };
        btnImport.Click += (_, _) => ImportFromCsv();

        // The same import, from a table copied off a web page or out of a
        // spreadsheet rather than saved as a file (PastedTable).
        var btnPaste = new Button { Text = "Paste a Table..." };
        btnPaste.Click += (_, _) => ImportPastedTable();
        var btnTemplate = new Button { Text = "Download Template..." };
        btnTemplate.Click += (_, _) => DownloadTemplate();

        // With the other whole-list buttons, and hard against the right-hand
        // edge where this app puts the things you reach for rather than the
        // things you do (SplitBar's glossary). It is about the roster as a
        // whole, not the driver on the card: the point of it is one pass
        // down every name, which is why it is not a button on the card.
        var btnCheckNames = new Button { Text = "Check Names..." };
        btnCheckNames.Click += (_, _) => CheckNames();

        SpreadBar? bottom = null;

        bottom = new SpreadBar(
            new ButtonBar().With(btnAdd, btnDelete),
            new ButtonBar().With(_btnCardEdit, _btnCardPhoto),

            // Added last so Tab still reaches Import and Template in the
            // order it always did - the bar takes its buttons right to
            // left, so the last one added is the leftmost on screen.
            new ButtonBar(alignRight: true).With(btnImport, btnPaste, btnTemplate, btnCheckNames),
            () => bottom is { IsHandleCreated: true } && card.IsHandleCreated && card.Visible
                ? bottom.PointToClient(card.PointToScreen(Point.Empty)).X + Section.Inset.Left
                : 0);

        // --- The page ---
        var page = new FillPage();

        // Transponders that crossed the line with no driver - see
        // UnknownTransponders. Hidden while there are none.
        page.Add(_unknownBanner);

        if (_garageSources is not null)
        {
            // The Drivers | Garage switch in the upper left; the roster and
            // the card, or one driver's cars filling the page; and the
            // Drivers view's buttons along the foot.
            page.Add(BuildTopRow(), gapBelow: UiLayout.RowGap);
            page.AddFill(BuildViews(band, bottom), ViewsLeastHeight, gapBelow: UiLayout.RowGap);
        }
        else
        {
            page.AddFill(band, band.LeastHeight, gapBelow: UiLayout.RowGap);
        }

        page.Add(bottom, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// The roster's width: about a quarter of the page, between 230px and
    /// 320px at the normal text size - names and numbers need no more - at
    /// the size in use.
    /// </summary>
    private static int RosterWidth(int pageWidth) =>
        Math.Clamp((int)(pageWidth * 0.24f), UiLayout.AtTextSize(MinRosterWidth), UiLayout.AtTextSize(MaxRosterWidth));

    /// <summary>
    /// The roster's rows as tall as two lines of its text - a list of names
    /// to pick from, not a table to scan. A ListView takes its row height
    /// from its small image list, so the list has one, of blank images that
    /// height and a pixel wide.
    /// </summary>
    private void FitRowHeight()
    {
        var height = Math.Clamp((int)Math.Round(_list.Font.Height * 2.1f), 16, 256);

        if (_thumbs.ImageSize.Height != height)
        {
            _thumbs.ImageSize = new Size(1, height);
        }
    }

    /// <summary>
    /// Fits the roster's columns to its width.
    ///
    /// Each column is the width it was at the normal text size, at the size
    /// in use - or its heading's, if that is wider - and the driver's takes
    /// whatever the others leave, never less than the photo and a short name.
    /// When that does not fit, the other four give way first, down to the
    /// width of their headings, before the list scrolls sideways. At fixed
    /// widths the columns overran the roster at 1280x720 and at 200% text -
    /// "Transponder" cut off, and a sideways scrollbar - and on a wide screen
    /// stopped short of it with a blank band down the right (layout
    /// pictures, 29 Sep). A heading is measured, not scaled: at 200% the
    /// 95px the transponder column had at 100% did not hold its heading.
    /// </summary>
    private void FitColumns()
    {
        if (_fittingColumns || _list.Columns.Count < ColumnWidths.Length)
        {
            return;
        }

        _fittingColumns = true;

        try
        {
            var room = _list.ClientSize.Width;
            var driverLeast = UiLayout.AtTextSize(DriverNameLeast);
            var others = ColumnWidths.Length - 1;

            var wanted = new int[others];
            var least = new int[others];

            for (var i = 0; i < others; i++)
            {
                var column = _list.Columns[i + 1];

                // The heading whole, as the header draws it, and the room it
                // keeps either side - see ListViewHeaderSkin.LeastWidth.
                least[i] = ListViewHeaderSkin.LeastWidth(column.Text);
                wanted[i] = Math.Max(least[i], UiLayout.AtTextSize(ColumnWidths[i + 1]));
            }

            var wantedTotal = wanted.Sum();
            var leastTotal = least.Sum();
            var forOthers = room - driverLeast;

            var widths = new int[others];

            for (var i = 0; i < others; i++)
            {
                widths[i] = forOthers >= wantedTotal || wantedTotal == leastTotal
                    ? wanted[i]
                    : forOthers <= leastTotal
                        ? least[i]
                        : least[i] + (wanted[i] - least[i]) * (forOthers - leastTotal) / (wantedTotal - leastTotal);
            }

            for (var i = 0; i < others; i++)
            {
                if (_list.Columns[i + 1].Width != widths[i])
                {
                    _list.Columns[i + 1].Width = widths[i];
                }
            }

            var driver = Math.Max(driverLeast, room - widths.Sum());

            if (_list.Columns[0].Width != driver)
            {
                _list.Columns[0].Width = driver;
            }
        }
        finally
        {
            _fittingColumns = false;
        }
    }

    /// <summary>The least the roster can do with: its headings and three drivers. Below that the page scrolls instead - see FillPage.</summary>
    private int LeastListHeight() =>
        _list.Font.Height * 2 + 3 * (Math.Max(_thumbs.ImageSize.Height, _list.Font.Height) + 4);

    /// <summary>
    /// Called when this page becomes visible - profiles can be created
    /// elsewhere (new-transponder prompt, first-run wizard) while it's
    /// inactive, and races, badges, check-ins and championship tables all
    /// move on while it is out of sight, so the stats start again too.
    /// </summary>
    public void RefreshAll()
    {
        InvalidateStats();
        RefreshList();
        RefreshHeader();
        RefreshGarageDriverPicker();
        ShowDriverInViews(CardProfile()?.Transponder);

        // Scrolled to on arrival. The first pick is made while the page is
        // still being built, before the roster has a window to scroll, so a
        // driver remembered from last time and sitting thirty rows down was
        // highlighted out of sight - card on Sam, roster on Aaron to Hugh.
        var onCard = _list.FocusedItem is { Selected: true } focused
            ? focused
            : _list.SelectedItems.Count > 0 ? _list.SelectedItems[0] : null;

        if (onCard is not null && _list.IsHandleCreated)
        {
            onCard.EnsureVisible();
        }

        // The theme pass run as the page is shown, or at a new text size,
        // styles every button by its caption, which leaves no tab or view
        // looking chosen - so the chosen ones are marked again after it.
        StyleSegments();
    }

    // ==================== The roster ====================

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The history store outlives this page; left subscribed, it
            // would keep the page alive and call into a disposed control.
            if (_sources?.History is not null)
            {
                _sources.History.SessionAdded -= OnSessionAdded;
            }

            // A driver chosen in the last moment before the page went is
            // still waiting on the timer - written now, or it is lost.
            if (_rememberTimer.Enabled)
            {
                RememberShownNow();
            }

            _rememberTimer.Dispose();

            // The ListView goes first (base), then the list it borrowed
            // its row height from.
            base.Dispose(disposing);
            _thumbs.Dispose();
            return;
        }

        base.Dispose(disposing);
    }

    private void RefreshList()
    {
        var filter = _txtSearch.Text.Trim();
        var selected = SelectedTransponders();

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var p in _store.All)
        {
            if (filter.Length > 0 && !Matches(p, filter))
            {
                continue;
            }

            var display = string.IsNullOrWhiteSpace(p.DriverName) ? p.Transponder : p.DriverName;

            // Names and numbers: everything else about them is on the card.
            var item = new ListViewItem(display) { Tag = p.Transponder };
            item.SubItems.Add(string.IsNullOrWhiteSpace(p.RaceNumber) ? string.Empty : "#" + p.RaceNumber.Trim());

            if (selected.Contains(p.Transponder))
            {
                item.Selected = true;
            }

            _list.Items.Add(item);
        }

        _list.EndUpdate();

        // Nobody carried over - the first time the page is built, after the
        // driver on the card was deleted, or when the search has hidden
        // them. Rather than an empty card, the driver last shown (kept
        // across restarts) or else the top of the roster. Done before
        // OnSelectionChanged, so the card is drawn once, on the right
        // driver, instead of blank and then again.
        if (_list.SelectedItems.Count == 0)
        {
            SelectShownOrFirst();
        }

        OnSelectionChanged();
    }

    private static bool Matches(DriverProfile p, string filter) =>
        Has(p.DriverName, filter) || Has(p.AnnouncerName, filter) || p.AllTransponderIds.Any(id => Has(id, filter))
        || Has(p.RaceNumber, filter) || p.AllClasses.Any(c => Has(c, filter)) || Has(p.Team, filter)
        || Has(p.Sponsors, filter) || Has(p.HomeTrack, filter)
        || p.AllCars.Any(car => Has(car, filter));

    private static bool Has(string value, string filter) =>
        value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private HashSet<string> SelectedTransponders() =>
        _list.SelectedItems.Cast<ListViewItem>()
            .Select(i => (string)i.Tag!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A driver's report card for tonight, a line at a time - set by the
    /// host (MainForm.CoachReportFor). Null hides the right-click item.
    /// </summary>
    public Func<string, IReadOnlyList<string>>? ReportCardFor { get; set; }

    /// <summary>Tonight's report card for the driver on the card (22 Oct 2026). See CoachReport.</summary>
    private void ShowReportCard()
    {
        if (ReportCardFor is not { } cardFor || CardProfile() is not { } profile)
        {
            return;
        }

        IReadOnlyList<string> lines;

        try
        {
            lines = cardFor(profile.Transponder);
        }
        catch
        {
            lines = Array.Empty<string>();
        }

        var named = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;

        // A window of its own (22 Oct 2026), with Print and Save Image - see ReportCardForm.
        using var form = new ReportCardForm(named, TonightAtTheTrack.NightOf(DateTime.Now).ToString("ddd d MMM", System.Globalization.CultureInfo.CurrentCulture), lines);
        form.ShowDialog(this);
    }

    private DriverProfile? SelectedProfile() =>
        _list.SelectedItems.Count == 1 ? _store.Get((string)_list.SelectedItems[0].Tag!) : null;

    /// <summary>
    /// The driver the card shows. Somebody, whenever there is anybody.
    /// </summary>
    /// <remarks>
    /// The card used to show exactly one highlighted row and nothing
    /// otherwise, so it went blank far more often than it showed anyone: on
    /// every launch, whenever a click landed on the empty space under the
    /// roster, whenever a second row was Ctrl-clicked, and whenever a search
    /// hid the highlighted driver. The card is the reason for opening this
    /// page, and an empty one is a page with nothing on it.
    ///
    /// So, in order:
    ///   one row highlighted   - that driver
    ///   several highlighted   - the one last clicked (it holds the focus),
    ///                           which is the one someone is looking at
    ///   nothing highlighted   - whoever the card was already showing
    ///
    /// Null only when the roster is empty, or the driver it was showing has
    /// just been deleted and RefreshList has not yet moved on.
    /// </remarks>
    private DriverProfile? CardProfile()
    {
        if (SelectedProfile() is { } one)
        {
            return one;
        }

        if (_list.SelectedItems.Count > 1)
        {
            if (_list.FocusedItem is { Selected: true, Tag: string focused } && _store.Get(focused) is { } lastClicked)
            {
                return lastClicked;
            }

            return _store.Get((string)_list.SelectedItems[0].Tag!);
        }

        return _shownTransponder is null ? null : _store.Get(_shownTransponder);
    }

    /// <summary>Brings the card in line with the roster's selection: buttons, scroll position, stats.</summary>
    private void OnSelectionChanged()
    {
        var profile = CardProfile();

        // The driver's own buttons only while there is a driver to act on.
        _btnCardEdit.Enabled = profile is not null;
        _btnCardPhoto.Enabled = profile is not null;

        // A different driver starts at the top of their card. Only when it
        // really is a different one: the roster clears and refills its
        // selection on every search keystroke and every edit, and jumping
        // back to the photo each time would lose the reader's place.
        if (profile is not null
            && !string.Equals(profile.Transponder, _shownTransponder, StringComparison.OrdinalIgnoreCase))
        {
            _shownTransponder = profile.Transponder;
            _cardScroll.AutoScrollPosition = Point.Empty;

            // Remembered once the choice settles - see _rememberTimer.
            _rememberTimer.Stop();
            _rememberTimer.Start();
        }

        // Nothing highlighted now. Usually a click on the blank space under
        // the last row, which a ListView treats as "clear the selection" -
        // the card has not moved (see CardProfile), but the roster no longer
        // shows WHICH driver it is on, and the two ought to agree.
        //
        // Put back AFTER the click has finished, not here. A ListView
        // passes through "nothing selected" on the way from one row to the
        // next - it drops the old row, raises this, then takes the new one -
        // so re-selecting here, mid-click, would snap the highlight back to
        // the old driver and refuse every click on a different one. Posted,
        // this runs once the click is over: by then a click on another row
        // has selected it and there is nothing to do.
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0 && IsHandleCreated)
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && _list.SelectedItems.Count == 0)
                {
                    SelectShownOrFirst();
                }
            });
        }

        RequestStats(profile);
        UpdateCardExtent();
        _cardScroll.Invalidate();
        RefreshHeader();

        // The garage, transponders and setups follow the driver too.
        ShowDriverInViews(profile?.Transponder);
    }

    /// <summary>
    /// Highlights the driver the card is on, or the top of the roster when
    /// they are not in it - deleted, or hidden by the search.
    /// </summary>
    /// <remarks>
    /// The top of the list rather than nothing when the search has hidden
    /// them: typing a name is choosing somebody, and the first match is the
    /// best guess at who. If the search matches nobody at all, nothing is
    /// highlighted and the card simply stays where it was.
    /// </remarks>
    private void SelectShownOrFirst()
    {
        if (_list.Items.Count == 0)
        {
            return;
        }

        var item = _list.Items.Cast<ListViewItem>().FirstOrDefault(i =>
                       string.Equals((string)i.Tag!, _shownTransponder, StringComparison.OrdinalIgnoreCase))
                   ?? _list.Items[0];

        item.Selected = true;
        item.Focused = true;

        // A remembered driver can be well down a long roster. Only once the
        // roster has a window: the first call comes from the constructor,
        // and RefreshAll scrolls to them when the page is actually opened.
        if (_list.IsHandleCreated)
        {
            item.EnsureVisible();
        }
    }

    /// <summary>Writes the driver the card is on to settings, so the page opens on them next time.</summary>
    private void RememberShownNow()
    {
        _rememberTimer.Stop();

        if (_settings is null || _shownTransponder is null
            || string.Equals(_settings.LastDriverProfileTransponder, _shownTransponder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.LastDriverProfileTransponder = _shownTransponder;
        _settings.Save();
    }

    // ==================== Stats ====================

    private void OnSessionAdded(SessionRecord record)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            // Nothing on screen to update; RefreshAll starts the stats
            // again when the page is next opened.
            return;
        }

        try
        {
            BeginInvoke(new Action(InvalidateStats));
        }
        catch (InvalidOperationException)
        {
            // The handle went between the check and the post - the page is
            // closing, and there is nothing left to update.
        }
    }

    /// <summary>Throws away every driver's worked-out stats and starts the selected one again.</summary>
    private void InvalidateStats()
    {
        if (IsDisposed)
        {
            return;
        }

        _statsVersion++;
        _statsCache.Clear();
        _statsPending.Clear();
        _statsSnapshot = null;

        RequestStats(CardProfile());
        UpdateCardExtent();
        _cardScroll.Invalidate();
    }

    /// <summary>
    /// Makes sure the stats for this driver are cached or on their way.
    ///
    /// A short history is summed on the spot - it takes less time than the
    /// repaint that shows it, and a card that flashes "working it out" for
    /// one frame on every click looks broken. A long one is summed on a
    /// background thread from a snapshot, and the card says so until it
    /// arrives.
    /// </summary>
    private void RequestStats(DriverProfile? profile)
    {
        if (profile is null || _sources is not { History: not null } sources)
        {
            return;
        }

        var key = profile.Transponder;

        if (_statsCache.ContainsKey(key) || !_statsPending.Add(key))
        {
            return;
        }

        var version = _statsVersion;
        DriverStatsSnapshot snapshot;

        try
        {
            snapshot = _statsSnapshot ??= DriverStatsSnapshot.Take(sources, _store);
        }
        catch
        {
            FinishStats(DriverStats.CouldNotRead(), profile, version);
            return;
        }

        var query = BuildQuery(profile);

        if (snapshot.Sessions.Count <= DriverStats.InlineSessionLimit)
        {
            DriverStats stats;

            try
            {
                stats = DriverStats.Compute(query, snapshot);
            }
            catch
            {
                stats = DriverStats.CouldNotRead();
            }

            FinishStats(stats, profile, version);
            return;
        }

        ComputeStatsInBackground(query, snapshot, profile, version);
    }

    /// <summary>The long-history path: the sums on a worker thread, the result handed back to the UI thread.</summary>
    private async void ComputeStatsInBackground(DriverStatsQuery query, DriverStatsSnapshot snapshot, DriverProfile profile, int version)
    {
        DriverStats stats;

        try
        {
            stats = await Task.Run(() => DriverStats.Compute(query, snapshot));
        }
        catch
        {
            stats = DriverStats.CouldNotRead();
        }

        try
        {
            // The await came back through the UI thread's context when
            // there was one. If there was not, post it there.
            if (InvokeRequired)
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() => FinishStats(stats, profile, version)));
                }

                return;
            }

            FinishStats(stats, profile, version);
        }
        catch
        {
            // The page closed while the sums were running. Nothing to show
            // them on, and nothing here may take the app down with it.
        }
    }

    /// <summary>
    /// Settles a driver's stats on the UI thread - the parts that read
    /// live stores (personal-best resets, check-in, tech) - caches them,
    /// and repaints if that driver is still the one on the card.
    /// </summary>
    private void FinishStats(DriverStats stats, DriverProfile profile, int version)
    {
        if (IsDisposed || version != _statsVersion)
        {
            // Worked out from data that has since changed; the newer
            // request already replaced it.
            return;
        }

        try
        {
            if (!stats.Failed)
            {
                stats.ApplyPersonalBests(_sources?.PersonalBestResets);
            }

            stats.RaceDay = DriverStats.RaceDayFor(profile, _sources);
        }
        catch
        {
            // Decoration: a store that could not be read leaves its line
            // off the card rather than the card blank.
        }

        _statsCache[profile.Transponder] = stats;
        _statsPending.Remove(profile.Transponder);

        if (string.Equals(CardProfile()?.Transponder, profile.Transponder, StringComparison.OrdinalIgnoreCase))
        {
            UpdateCardExtent();
            _cardScroll.Invalidate();

            // Checked in is one of the header's chips, and comes with the stats.
            RefreshHeader();
        }
    }

    /// <summary>
    /// Who the sums are for: the driver's main transponder and every other
    /// one the store resolves to them - so another car's races count as
    /// theirs exactly as they do everywhere else.
    /// </summary>
    private DriverStatsQuery BuildQuery(DriverProfile profile)
    {
        var transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { profile.Transponder.Trim() };

        // Retired ones included: what they raced is still the driver's.
        foreach (var entry in profile.ExtraTransponders)
        {
            if (string.Equals(_store.PrimaryForAlternate(entry.Id), profile.Transponder, StringComparison.OrdinalIgnoreCase))
            {
                transponders.Add(entry.Id.Trim());
            }
        }

        return new DriverStatsQuery
        {
            Transponder = profile.Transponder,
            Transponders = transponders,
            DriverName = profile.DriverName?.Trim() ?? string.Empty,
        };
    }

    // ==================== The card ====================

    /// <summary>
    /// Sets how tall the canvas's scrollable area is, by laying the card
    /// out without drawing it.
    ///
    /// Run twice at most: giving the canvas a scroll range can bring its
    /// scrollbar in, which narrows it, which re-wraps the text - so the
    /// height is measured again at the new width.
    /// </summary>
    private void UpdateCardExtent()
    {
        if (_measuring || _cardScroll.IsDisposed || !_cardScroll.IsHandleCreated)
        {
            return;
        }

        _measuring = true;

        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                var width = _cardScroll.ClientSize.Width;
                int height;

                using (var g = _cardScroll.CreateGraphics())
                {
                    height = RenderCard(g, width, draw: false);
                }

                var wanted = new Size(0, height);

                if (_cardScroll.AutoScrollMinSize != wanted)
                {
                    _cardScroll.AutoScrollMinSize = wanted;
                }

                if (_cardScroll.ClientSize.Width == width)
                {
                    break;
                }
            }
        }
        catch
        {
            // Measuring is for the scrollbar. A failure leaves the old range
            // and the card still paints.
        }
        finally
        {
            _measuring = false;
        }
    }

    private void PaintCardCanvas(Graphics g)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(AppTheme.Surface);

        var profile = CardProfile();

        if (profile is null)
        {
            using var muted = new SolidBrush(AppTheme.TextMuted);
            using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            var selectedCount = _list.SelectedItems.Count;
            var message = selectedCount > 1
                ? Plural.Of(selectedCount, "driver") + " selected"
                : "Select a driver to\nsee their card";

            g.DrawString(message, AppTheme.BodyFont, muted, new RectangleF(0, 0, _cardScroll.ClientSize.Width, _cardScroll.ClientSize.Height), centred);
            return;
        }

        var offset = _cardScroll.AutoScrollPosition;
        g.TranslateTransform(offset.X, offset.Y);

        var height = RenderCard(g, _cardScroll.ClientSize.Width, draw: true);

        g.ResetTransform();

        // The text size can change (Ctrl +/-) without this page being told
        // - the window just repaints. When what was drawn no longer matches
        // the scroll range, the range is put right after this paint - to the
        // height just PAINTED rather than a fresh measurement, so a pixel of
        // difference between measuring and painting text can never set the
        // two chasing each other in a repaint loop.
        if (!_measuring && !_extentCheckQueued && Math.Abs(height - _cardScroll.AutoScrollMinSize.Height) > 4 && IsHandleCreated)
        {
            _extentCheckQueued = true;

            BeginInvoke(new Action(() =>
            {
                _extentCheckQueued = false;

                if (_cardScroll.IsDisposed)
                {
                    return;
                }

                _cardScroll.AutoScrollMinSize = new Size(0, height);
                _cardScroll.Invalidate();
            }));
        }
    }

    /// <summary>
    /// Lays out - and, when <paramref name="draw"/> is set, paints - the
    /// whole card for the selected driver, top to bottom, returning how tall
    /// it came out. One routine for both, so the scroll range can never
    /// disagree with what is drawn.
    ///
    /// Every height comes from the font it holds, never a fixed pixel
    /// count, so Ctrl +/- makes the whole card bigger rather than
    /// overlapping it.
    /// </summary>
    private int RenderCard(Graphics g, int width, bool draw)
    {
        var profile = CardProfile();

        if (profile is null)
        {
            return 0;
        }

        var inner = width - CardPad * 2;

        if (inner < 60)
        {
            return 0;
        }

        var c = new CardCursor(g, draw, CardPad, inner) { Y = CardPad };
        var display = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

        var stats = _sources?.History is null ? null : _statsCache.GetValueOrDefault(profile.Transponder);

        // --- The photo is the headline ---
        //
        // Only on a card without the header over it (no garage sources):
        // with it, the picture, name and chips are in the header, and the
        // card starts with what the tab is about.
        if (PaintsHeaderOnCard)
        {
            // Its width is stepped in forties rather than following the card
            // to the pixel: the photo cache keeps one scaled copy per size
            // asked for, and a size per pixel of window width would fill it
            // with near-identical bitmaps as the window is dragged.
            var photoWidth = Math.Min((int)inner, Math.Max(120, Math.Min((int)inner, 480) / 40 * 40));
            var photoHeight = (int)Math.Min(photoWidth * 0.8f, 320f);

            if (draw)
            {
                var photoLeft = (int)(CardPad + (inner - photoWidth) / 2f);
                DriverPhotos.DrawAvatar(g, new Rectangle(photoLeft, (int)c.Y, photoWidth, photoHeight), display, profile.PhotoFileName, cornerRadius: 10);
            }

            c.Y += photoHeight + 12f;

            // --- Name, then who they are at a glance ---
            Paragraph(c, display, AppTheme.TitleFont, AppTheme.TextDark, maxLines: 2);

            if (!string.IsNullOrWhiteSpace(profile.AnnouncerName)
                && !string.Equals(profile.AnnouncerName.Trim(), display, StringComparison.OrdinalIgnoreCase))
            {
                Paragraph(c, $"Announced as \"{profile.AnnouncerName.Trim()}\"", AppTheme.BodyFont, AppTheme.TextMuted, maxLines: 2);
            }

            c.Y += 4f;
            Chips(c, ChipsFor(profile));
        }

        // --- Their record ---
        if (_sources?.History is null || !ShowsStats)
        {
            // Built without the race history - the profile is all there is.
            // Or the Profile tab is showing: the record is on Stats.
        }
        else if (stats is null)
        {
            c.Y += 8f;
            Paragraph(c, $"Working out {display}'s numbers...", AppTheme.BodyFont, AppTheme.TextMuted);
        }
        else if (stats.Failed)
        {
            c.Y += 8f;
            Paragraph(c, "Couldn't read the race history for this driver.", AppTheme.BodyFont, AppTheme.TextMuted);
        }
        else
        {
            RenderRecord(c, stats);
        }

        // With the card's tabs, Profile is who they are and Stats is their
        // record; without them (no garage sources), the card is both.
        if (ShowsProfile)
        {
            RenderOnFile(c, stats, profile);
            RenderRaceDay(c, stats?.RaceDay);
            RenderHonours(c, stats);
            RenderProfileDetails(c, profile);

            if (!string.IsNullOrWhiteSpace(profile.Notes))
            {
                Heading(c, "Notes");
                Paragraph(c, profile.Notes.Trim(), AppTheme.BodyFont, AppTheme.TextDark);
            }
        }

        return (int)Math.Ceiling(c.Y + CardPad);
    }

    /// <summary>The numbers: headline tiles, podium split, recent form, then the career, bests, championships, rival and badges.</summary>
    private void RenderRecord(CardCursor c, DriverStats stats)
    {
        if (stats.Starts == 0 && stats.PracticeSessions == 0)
        {
            c.Y += 10f;
            Paragraph(c, "No races yet - their numbers start the first time they cross the line.", AppTheme.BodyFont, AppTheme.TextMuted);

            // A setup can be logged before they have raced.
            RenderActivity(c, stats);
            return;
        }

        c.Y += 10f;

        if (stats.Starts > 0)
        {
            Tiles(c, new (string Label, string Value)[]
            {
                ("Races", stats.Starts.ToString("N0")),
                ("Wins", stats.Wins.ToString("N0")),
                ("Podiums", stats.Podiums.ToString("N0")),
                ("Win rate", stats.WinRatePercent is { } rate ? $"{rate:0}%" : "-"),
                ("Avg finish", stats.AverageFinish is { } average ? $"P{average:0.0}" : "-"),
                ("Best lap", stats.BestLap is { } best ? Lap(best.LapMs) : "-"),

                // The driver rating (18 Oct 2026) - see DriverRating. Three
                // more tiles rather than one, so the grid stays full at
                // three across, which is how most screens draw it.
                ("Avg rating", stats.AverageRating is { } rating ? DriverRating.Format(rating) : "-"),
                ("Best rating", stats.BestRating is { } top ? DriverRating.Format(top) : "-"),
                ("Laps led", stats.LapsLed.ToString("N0")),
            });

            if (stats.Podiums > 0)
            {
                Paragraph(c, $"1st × {stats.Wins}    2nd × {stats.Seconds}    3rd × {stats.Thirds}", AppTheme.BoldFont, AppTheme.TextDark);
            }

            if (stats.RecentForm.Count > 0)
            {
                c.Y += 6f;
                Caption(c, "Recent form · newest first");

                Chips(c, stats.RecentForm.Select(place => place switch
                {
                    0 => new Chip("DNF", AppTheme.Danger, AppTheme.OnDanger),
                    1 => new Chip("P1", AppTheme.Success, AppTheme.OnSuccess),
                    2 or 3 => new Chip($"P{place}", AppTheme.Accent, AppTheme.OnAccent),
                    _ => new Chip($"P{place}", null, AppTheme.TextDark),
                }).ToList());
            }
        }
        else
        {
            Paragraph(c, $"No races yet - {Plural.Of(stats.PracticeSessions, "practice session")} so far.", AppTheme.BodyFont, AppTheme.TextMuted);
        }

        RenderActivity(c, stats);

        // --- Career ---
        //
        // Gathered before the heading is drawn: a driver with practice runs
        // and no races has none of these lines, and a heading over nothing
        // is worse than no heading.
        var career = new List<(string Label, string Value)>();

        if (stats.BestLap is { } fastest)
        {
            career.Add(("Best lap", $"{Lap(fastest.LapMs)}s  ·  {fastest.Layout}"));
        }

        if (stats.BestFinish is { } bestFinish)
        {
            career.Add(("Best finish", "P" + bestFinish));
        }

        if (stats.Starts > 0)
        {
            career.Add(("Races finished", stats.FinishRatePercent is { } rate
                ? $"{stats.Finishes:N0} of {stats.Starts:N0}  ·  {rate:0}%"
                : $"{stats.Finishes:N0} of {stats.Starts:N0}"));
        }

        if (stats.Podiums > 0 && stats.PodiumRatePercent is { } podiumRate)
        {
            career.Add(("Podium rate", $"{podiumRate:0}%"));
        }

        // Only when it says something the podium count does not - a driver
        // whose every top five was a podium has this line twice over.
        if (stats.TopFives > stats.Podiums)
        {
            career.Add(("Top five finishes", stats.TopFives.ToString("N0")));
        }

        if (stats.LastWinUtc is { } lastWin)
        {
            career.Add(("Last win", DateWithAge(lastWin)));
        }

        if (stats.Starts > 0)
        {
            career.Add(("Race laps", stats.AverageRaceLaps is { } perRace
                ? $"{stats.RaceLaps:N0}  ·  {perRace:0.0} a race"
                : stats.RaceLaps.ToString("N0")));
        }

        if (stats.Dnfs > 0)
        {
            career.Add(("Did not finish", Plural.Of(stats.Dnfs, "race")));
        }

        // The no-races line above already counts their practice, so this
        // only adds anything once they have raced as well.
        if (stats.PracticeSessions > 0 && stats.Starts > 0)
        {
            career.Add(("Practice sessions", stats.PracticeSessions.ToString("N0")));
        }

        if (stats.PracticeLaps > 0)
        {
            career.Add(("Practice laps", stats.PracticeLaps.ToString("N0")));
        }

        if (stats.Classes.Count > 0)
        {
            career.Add((Plural.Word(stats.Classes.Count, "Class raced", "Classes raced"), string.Join(", ", stats.Classes)));
        }

        if (career.Count > 0)
        {
            Heading(c, "Career");

            foreach (var (label, value) in career)
            {
                KeyValue(c, label, value);
            }
        }

        // --- Personal bests, one per layout ---
        if (stats.PersonalBests.Count > 0)
        {
            const int shown = 8;

            Heading(c, "Personal bests");

            foreach (var best in stats.PersonalBests.Take(shown))
            {
                KeyValue(c, best.Layout, $"{Lap(best.LapMs)}s");
            }

            if (stats.PersonalBests.Count > shown)
            {
                Paragraph(c, $"...and {stats.PersonalBests.Count - shown} more on Race Analytics > Personal Bests.", AppTheme.BodyFont, AppTheme.TextMuted);
            }
        }

        // --- Best sectors, one line per layout run with a split loop ---
        if (stats.BestSectors.Count > 0)
        {
            const int shown = 6;

            Heading(c, "Best sectors");

            foreach (var line in stats.BestSectors.Take(shown))
            {
                var sectors = string.Join("  ", line.Sectors.Select((ms, i) => $"S{i + 1} {Sectors.Format(ms)}"));
                KeyValue(c, line.Layout, $"{sectors}  ·  ideal {Sectors.Format(line.IdealMs)}");
            }
        }

        // --- Championships ---
        if (stats.Championships.Count > 0)
        {
            Heading(c, stats.Championships.Count == 1 ? "Championship" : "Championships");

            foreach (var line in stats.Championships.Take(5))
            {
                KeyValue(c, line.Season, $"P{line.Position} of {line.FieldSize}  ·  {Plural.Of(line.Points, "point")}");
            }
        }

        // --- Rival ---
        if (stats.Rival is { } rival)
        {
            Heading(c, "Top rival");

            var meetings = rival.Ahead + rival.Behind;

            KeyValue(c, rival.Name, meetings == 0 ? "Not raced yet" : $"Ahead {rival.Ahead}  ·  behind {rival.Behind}");
            Paragraph(c,
                (rival.Named ? "Set up as a rivalry" : "Raced together most")
                + (meetings == 0 ? string.Empty : $" - {Plural.Of(meetings, "shared session")}"),
                AppTheme.SmallFont, AppTheme.TextMuted);
        }

        // Badges and track records are on the Profile side - see
        // RenderHonours.
    }

    /// <summary>
    /// What they have won that stays won: the lap records that are theirs
    /// now (and the layouts they held one on before), and every badge they
    /// have earned - each badge's medallion, then the list with when and
    /// what for. On the Profile tab (and on the card itself without tabs),
    /// since it is part of who they are rather than a running total.
    /// </summary>
    private static void RenderHonours(CardCursor c, DriverStats? stats)
    {
        if (stats is not { Failed: false })
        {
            return;
        }

        if (stats.RecordsHeld.Count > 0 || stats.RecordsHeldBefore.Count > 0)
        {
            Heading(c, stats.RecordsHeld.Count == 1 ? "Track record" : "Track records");

            foreach (var record in stats.RecordsHeld)
            {
                KeyValue(c, record.Layout, record.SetUtc is { } set
                    ? $"{Lap(record.LapMs)}s  ·  {DateWithAge(set)}"
                    : $"{Lap(record.LapMs)}s",
                    AppTheme.Success);
            }

            if (stats.RecordsHeldBefore.Count > 0)
            {
                Paragraph(c,
                    (stats.RecordsHeld.Count == 0 ? "Has held the record on " : "Has also held the record on ")
                    + string.Join(", ", stats.RecordsHeldBefore) + ".",
                    AppTheme.BodyFont, AppTheme.TextMuted);
            }
        }

        // Sector records - the quickest anyone has gone through a sector.
        if (stats.SectorRecordsHeld.Count > 0)
        {
            Heading(c, stats.SectorRecordsHeld.Count == 1 ? "Sector record" : "Sector records");

            foreach (var layout in stats.SectorRecordsHeld.GroupBy(r => r.Layout))
            {
                KeyValue(c, layout.Key,
                    string.Join("  ", layout.Select(r => $"S{r.Sector} {Sectors.Format(r.Ms)}")),
                    AppTheme.Success);
            }
        }

        if (stats.Badges.Count > 0)
        {
            const int listed = 12;

            Heading(c, $"Achievements  ·  {stats.Badges.Count}");
            MedallionGrid(c, stats.Badges.Select(b => b.Badge).ToList());

            foreach (var line in stats.Badges.Take(listed))
            {
                var when = line.EarnedUtc.ToLocalTime().ToString("d MMM yyyy");
                KeyValue(c, line.Badge.Name, string.IsNullOrWhiteSpace(line.Detail) ? when : $"{line.Detail.Trim()}  ·  {when}");
            }

            if (stats.Badges.Count > listed)
            {
                Paragraph(c, $"...and {stats.Badges.Count - listed} more on the Achievements page.", AppTheme.BodyFont, AppTheme.TextMuted);
            }
        }
    }

    /// <summary>Every badge's medallion, wrapping onto as many rows as it takes.</summary>
    private static void MedallionGrid(CardCursor c, IReadOnlyList<AchievementDefinition> badges)
    {
        if (badges.Count == 0)
        {
            return;
        }

        // Scaled from the body text, so it grows with Ctrl +. The halo
        // DrawMedallion paints sits four pixels outside the box, hence the
        // inset and the spacing.
        var size = AppTheme.BodyFont.GetHeight(c.G) * 2.4f;
        var step = size + 12f;
        var perRow = Math.Max(1, (int)((c.Width - 8f + 12f) / step));
        var rows = (badges.Count + perRow - 1) / perRow;

        if (c.Draw)
        {
            for (var i = 0; i < badges.Count; i++)
            {
                var x = c.Left + 4f + (i % perRow) * step;
                var y = c.Y + 4f + (i / perRow) * step;
                BadgeArt.DrawMedallion(c.G, new RectangleF(x, y, size, size), badges[i], earned: true);
            }
        }

        c.Y += rows * step;
    }

    /// <summary>
    /// What they have been up to lately, newest first (27 Sep 2026): races
    /// with their place and best lap, personal bests, and setups logged for
    /// them with what each one changed. Straight after their form, so the
    /// card answers "what's new with them" before the career totals.
    /// </summary>
    private static void RenderActivity(CardCursor c, DriverStats stats)
    {
        if (stats.RecentActivity.Count == 0)
        {
            return;
        }

        Heading(c, "Recent activity");

        // As wide as the widest date actually shown, in this PC's own
        // month names - "30 sept. 2026" is longer than "30 Sep 2026".
        var dates = stats.RecentActivity.Select(line => ActivityDate(line.WhenUtc)).ToList();
        var dateWidth = dates.Max(date => c.G.MeasureString(date, AppTheme.SmallFont).Width) + 10f;

        for (var i = 0; i < dates.Count; i++)
        {
            var line = stats.RecentActivity[i];
            ActivityRow(c, dates[i], dateWidth, line.Text, line.Kind == ActivityKind.PersonalBest);
        }
    }

    /// <summary>"27 Sep", with the year when it isn't this one.</summary>
    private static string ActivityDate(DateTime utc)
    {
        var local = utc.ToLocalTime();

        return local.Year == DateTime.Now.Year ? local.ToString("d MMM") : local.ToString("d MMM yyyy");
    }

    /// <summary>
    /// One line of recent activity: the date in a narrow muted column, and
    /// what happened beside it, wrapping under itself rather than under the
    /// date. A personal best in the theme's primary colour, as the headings
    /// are - readable on every look, which a fill colour is not.
    /// </summary>
    private static void ActivityRow(CardCursor c, string date, float dateWidth, string text, bool highlight)
    {
        var dateFont = AppTheme.SmallFont;
        var textFont = AppTheme.BodyFont;
        var textWidth = Math.Max(40f, c.Width - dateWidth);

        var textHeight = c.G.MeasureString(text, textFont, (int)textWidth).Height;
        var dateHeight = dateFont.GetHeight(c.G);

        if (c.Draw)
        {
            using var dateBrush = new SolidBrush(AppTheme.TextMuted);
            using var textBrush = new SolidBrush(highlight ? AppTheme.Primary : AppTheme.TextDark);

            // The date sits on the text's first line rather than above it.
            var dateTop = c.Y + Math.Max(0f, (textFont.GetHeight(c.G) - dateHeight) / 2f);

            c.G.DrawString(date, dateFont, dateBrush, c.Left, dateTop);
            c.G.DrawString(text, textFont, textBrush, new RectangleF(c.Left + dateWidth, c.Y, textWidth, textHeight + 1f));
        }

        c.Y += Math.Max(textHeight, dateHeight) + 5f;
    }

    /// <summary>
    /// When they joined, and how much of them the timer has seen.
    ///
    /// Drawn for every driver, races or not. A new member's card used to
    /// stop at "no races yet" and say nothing else at all - and the day
    /// somebody became active is exactly what you want to see on the card of
    /// a driver who has not raced.
    ///
    /// Two dates rather than one "racing since": a driver can be timed in
    /// practice weeks before anyone fills in their profile, or be signed up
    /// months before they first turn up, and averaging the two into a single
    /// line hid whichever one you were looking for.
    /// </summary>
    private static void RenderOnFile(CardCursor c, DriverStats? stats, DriverProfile profile)
    {
        // A failed read has no figures at all - the profile's own date is
        // still worth showing.
        var record = stats is { Failed: false } ? stats : null;
        var rows = new List<(string Label, string Value)>();

        if (profile.FirstSeenUtc is { } added && added != default)
        {
            rows.Add(("Added to the system", DateWithSpan(added)));
        }

        if (record?.FirstSeenUtc is { } firstTimed && firstTimed != default)
        {
            rows.Add(("First timed", DateWithSpan(firstTimed)));
        }

        if (record?.LastRacedUtc is { } lastRaced)
        {
            rows.Add(("Last raced", DateWithAge(lastRaced)));
        }

        // Only when it is a different day from their last race - otherwise
        // the same date twice under two labels.
        if (record?.LastOnTrackUtc is { } lastOnTrack
            && (record.LastRacedUtc is null || lastOnTrack > record.LastRacedUtc))
        {
            rows.Add(("Last on track", DateWithAge(lastOnTrack)));
        }

        if (record is { DaysOnTrack: > 0 })
        {
            rows.Add(("Days at the track", record.DaysOnTrack.ToString("N0")));
        }

        if (record is { Sessions: > 0 })
        {
            rows.Add(("Sessions timed",
                $"{record.Sessions:N0}  ·  {record.Starts:N0} race, {record.PracticeSessions:N0} practice"));
        }

        if (record is { TotalLaps: > 0 })
        {
            rows.Add(("Laps timed", record.TotalLaps.ToString("N0")));
        }

        // No heading over nothing.
        if (rows.Count == 0)
        {
            return;
        }

        Heading(c, "On file");

        foreach (var (label, value) in rows)
        {
            KeyValue(c, label, value);
        }
    }

    /// <summary>Today's paperwork: check-in, tech, handicap, and how often they turn up.</summary>
    private void RenderRaceDay(CardCursor c, RaceDayLine? day)
    {
        if (day is null)
        {
            return;
        }

        Heading(c, "Race day");

        if (day.CheckedIn is { } checkedIn)
        {
            KeyValue(c, "Checked in", checkedIn ? "Yes" : "Not yet", checkedIn ? AppTheme.Success : AppTheme.TextMuted);
        }

        if (day.Tech is { } tech)
        {
            KeyValue(c, "Tech", tech, day.TechFailed ? AppTheme.Danger : null);
        }

        if (day.Handicap is { } handicap)
        {
            KeyValue(c, "Handicap", handicap);
        }

        if (day.EventsAttended > 0)
        {
            KeyValue(c, "Race days attended", day.EventsAttended.ToString("N0"));
        }

        if (day.SetupSheets > 0)
        {
            KeyValue(c, "Setup sheets logged", day.SetupSheets.ToString("N0"));
        }
    }

    /// <summary>What is stored on the profile, blanks skipped - a card full of empty headings is a form, and this is meant to be a poster.</summary>
    private void RenderProfileDetails(CardCursor c, DriverProfile profile)
    {
        Heading(c, "Profile");

        var others = profile.AllCars.Count - 1;

        if (!string.IsNullOrWhiteSpace(profile.CarModel))
        {
            KeyValue(c, "Car", others > 0 ? $"{profile.CarModel}  (+{others} more in the garage)" : profile.CarModel);
        }

        if (!string.IsNullOrWhiteSpace(profile.RaceNumber))
        {
            KeyValue(c, "Race number", "#" + profile.RaceNumber.Trim());
        }

        // Every class they race, the main one first.
        if (profile.AllClasses is { Count: > 0 } classes)
        {
            KeyValue(c, classes.Count > 1 ? "Classes" : "Class", string.Join(", ", classes));
        }

        if (!string.IsNullOrWhiteSpace(profile.Team))
        {
            KeyValue(c, "Team / club", profile.Team);
        }

        if (!string.IsNullOrWhiteSpace(profile.Sponsors))
        {
            KeyValue(c, "Sponsors", profile.Sponsors);
        }

        if (!string.IsNullOrWhiteSpace(profile.HomeTrack))
        {
            KeyValue(c, "Home track", profile.HomeTrack);
        }

        if (!string.IsNullOrWhiteSpace(profile.SkillLevel))
        {
            KeyValue(c, "Skill", profile.SkillLevel);
        }

        // Every one the driver has, the main one first; a retired one is
        // still listed - its laps are still theirs - and says so.
        var ids = profile.AllTransponderIds
            .Select(id => profile.IsRetired(id) ? $"{id} (retired)" : id)
            .ToList();

        KeyValue(c, ids.Count == 1 ? "Transponder" : "Transponders", string.Join(",  ", ids));
    }

    // ---- Card drawing helpers ----
    //
    // Each advances the cursor by exactly what it would draw, and draws
    // only when asked, so measuring and painting share one path.

    /// <summary>Where the next thing on the card goes, and whether this pass paints or only measures.</summary>
    private sealed class CardCursor
    {
        public CardCursor(Graphics g, bool draw, float left, float width)
        {
            G = g;
            Draw = draw;
            Left = left;
            Width = width;
        }

        public Graphics G { get; }
        public bool Draw { get; }
        public float Left { get; }
        public float Width { get; }
        public float Y { get; set; }
    }

    /// <summary>A pill on the card. A null fill draws an outlined chip in the theme's line colour instead.</summary>
    private sealed record Chip(string Text, Color? Fill, Color Ink);

    /// <summary>A section heading: spaced small capitals in the theme's primary colour over a hairline.</summary>
    private static void Heading(CardCursor c, string text)
    {
        c.Y += AppTheme.BodyFont.GetHeight(c.G) * 0.9f;

        var height = AppTheme.MicroFont.GetHeight(c.G);

        if (c.Draw)
        {
            using var brush = new SolidBrush(AppTheme.Primary);
            using var rule = new Pen(AppTheme.Border, 1f);

            c.G.DrawString(AppTheme.Spaced(text.ToUpperInvariant()), AppTheme.MicroFont, brush, c.Left, c.Y);
            c.G.DrawLine(rule, c.Left, c.Y + height + 3f, c.Left + c.Width, c.Y + height + 3f);
        }

        c.Y += height + 9f;
    }

    /// <summary>A small muted caption, for a row of chips that needs a word of explanation.</summary>
    private static void Caption(CardCursor c, string text)
    {
        if (c.Draw)
        {
            using var brush = new SolidBrush(AppTheme.TextMuted);
            c.G.DrawString(AppTheme.Spaced(text.ToUpperInvariant()), AppTheme.MicroFont, brush, c.Left, c.Y);
        }

        c.Y += AppTheme.MicroFont.GetHeight(c.G) + 4f;
    }

    /// <summary>Wrapped text across the card's width, optionally capped at a number of lines.</summary>
    private static void Paragraph(CardCursor c, string text, Font font, Color colour, int maxLines = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var height = c.G.MeasureString(text, font, (int)c.Width).Height;

        if (maxLines > 0)
        {
            height = Math.Min(height, font.GetHeight(c.G) * maxLines);
        }

        if (c.Draw)
        {
            using var brush = new SolidBrush(colour);
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisWord };

            c.G.DrawString(text, font, brush, new RectangleF(c.Left, c.Y, c.Width, height + 1f), format);
        }

        c.Y += height + 4f;
    }

    /// <summary>
    /// A label on the left and its value on the right, on one line when
    /// they fit. When they don't, the value drops under its label and wraps
    /// - a long class list or sponsor line is still read in full.
    /// </summary>
    private static void KeyValue(CardCursor c, string label, string value, Color? valueColour = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var labelFont = AppTheme.BodyFont;
        var valueFont = AppTheme.BoldFont;

        var labelSize = c.G.MeasureString(label, labelFont);
        var valueSize = c.G.MeasureString(value, valueFont);
        var lineHeight = Math.Max(labelFont.GetHeight(c.G), valueFont.GetHeight(c.G));

        using var labelBrush = new SolidBrush(AppTheme.TextMuted);
        using var valueBrush = new SolidBrush(valueColour ?? AppTheme.TextDark);

        if (labelSize.Width + 16f + valueSize.Width <= c.Width)
        {
            if (c.Draw)
            {
                using var right = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap };

                c.G.DrawString(label, labelFont, labelBrush, c.Left, c.Y);
                c.G.DrawString(value, valueFont, valueBrush, new RectangleF(c.Left, c.Y, c.Width, lineHeight + 1f), right);
            }

            c.Y += lineHeight + 5f;
            return;
        }

        var labelHeight = c.G.MeasureString(label, labelFont, (int)c.Width).Height;
        var valueHeight = c.G.MeasureString(value, valueFont, (int)c.Width).Height;

        if (c.Draw)
        {
            c.G.DrawString(label, labelFont, labelBrush, new RectangleF(c.Left, c.Y, c.Width, labelHeight + 1f));
            c.G.DrawString(value, valueFont, valueBrush, new RectangleF(c.Left, c.Y + labelHeight, c.Width, valueHeight + 1f));
        }

        c.Y += labelHeight + valueHeight + 5f;
    }

    /// <summary>A row of pills, wrapping onto a new line when the card runs out of width.</summary>
    private static void Chips(CardCursor c, IReadOnlyList<Chip> chips)
    {
        if (chips.Count == 0)
        {
            return;
        }

        var font = AppTheme.BoldFont;
        var height = font.GetHeight(c.G) + 8f;
        var x = c.Left;

        foreach (var chip in chips)
        {
            var width = Math.Min(c.Width, c.G.MeasureString(chip.Text, font).Width + 20f);

            if (x > c.Left && x + width > c.Left + c.Width)
            {
                x = c.Left;
                c.Y += height + 6f;
            }

            if (c.Draw)
            {
                var bounds = new RectangleF(x, c.Y, width, height);

                if (chip.Fill is { } fill)
                {
                    AppTheme.DrawPill(c.G, bounds, fill, chip.Text, chip.Ink);
                }
                else
                {
                    using var path = AppTheme.RoundedRect(RectangleF.Inflate(bounds, -0.5f, -0.5f), (int)(height / 2));
                    using var edge = new Pen(AppTheme.Border, 1.2f);
                    using var ink = new SolidBrush(chip.Ink);
                    using var centred = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.NoWrap,
                    };

                    c.G.DrawPath(edge, path);
                    c.G.DrawString(chip.Text, font, ink, bounds, centred);
                }
            }

            x += width + 8f;
        }

        c.Y += height + 8f;
    }

    /// <summary>
    /// The headline figures as tiles, three across when there is room and
    /// two when there is not. Each value is drawn in the title face, and
    /// steps down to the heading face only when it would not fit its tile.
    /// </summary>
    private static void Tiles(CardCursor c, IReadOnlyList<(string Label, string Value)> tiles)
    {
        const float gap = 8f;

        var columns = c.Width >= 3 * 100f + 2 * gap ? 3 : 2;
        var tileWidth = (c.Width - gap * (columns - 1)) / columns;
        var labelHeight = AppTheme.MicroFont.GetHeight(c.G);
        var valueHeight = AppTheme.TitleFont.GetHeight(c.G);
        var tileHeight = labelHeight + valueHeight + 22f;

        for (var i = 0; i < tiles.Count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            var bounds = new RectangleF(c.Left + column * (tileWidth + gap), c.Y + row * (tileHeight + gap), tileWidth, tileHeight);

            if (!c.Draw)
            {
                continue;
            }

            var (label, value) = tiles[i];

            AppTheme.DrawCard(c.G, bounds);

            using var labelBrush = new SolidBrush(AppTheme.TextMuted);
            using var valueBrush = new SolidBrush(AppTheme.TextDark);
            using var centred = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };

            var valueFont = c.G.MeasureString(value, AppTheme.TitleFont).Width <= tileWidth - 8f
                ? AppTheme.TitleFont
                : AppTheme.HeadingFont;

            c.G.DrawString(AppTheme.Spaced(label.ToUpperInvariant()), AppTheme.MicroFont, labelBrush,
                new RectangleF(bounds.Left + 4f, bounds.Top + 8f, bounds.Width - 8f, labelHeight + 1f), centred);
            c.G.DrawString(value, valueFont, valueBrush,
                new RectangleF(bounds.Left + 4f, bounds.Top + 8f + labelHeight, bounds.Width - 8f, bounds.Height - labelHeight - 12f), centred);
        }

        var rows = (tiles.Count + columns - 1) / columns;

        c.Y += rows * tileHeight + (rows - 1) * gap + 10f;
    }

    /// <summary>A lap time as people say it: seconds to three places.</summary>
    private static string Lap(long ms) => $"{ms / 1000.0:0.000}";

    /// <summary>"3 Sep 2026 · 5 days ago" - the age only while it is still the useful half.</summary>
    private static string DateWithAge(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var days = (DateTime.Now.Date - local.Date).Days;

        var age = days switch
        {
            < 0 => string.Empty,
            0 => "today",
            1 => "yesterday",
            < 60 => $"{days} days ago",
            _ => string.Empty,
        };

        return age.Length == 0
            ? local.ToString("d MMM yyyy")
            : $"{local:d MMM yyyy}  ·  {age}";
    }

    /// <summary>
    /// "14 Mar 2024 · 2 years ago" - a date and how long ago it was, always.
    ///
    /// DateWithAge stops saying the age past two months, which is right for
    /// "last raced": by then the date itself tells you more than a day
    /// count. The day somebody joined is the other way round - "three years
    /// ago" is the whole point of the line - so this one always says it, in
    /// whichever unit reads plainly.
    /// </summary>
    private static string DateWithSpan(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var days = (DateTime.Now.Date - local.Date).Days;

        var span = days switch
        {
            < 0 => string.Empty,
            0 => "today",
            1 => "yesterday",
            < 31 => $"{days} days ago",

            // Rounded to the nearer month, but never up to twelve - at that
            // point it is a year, and the next line says so.
            < 365 => Plural.Of(Math.Min(11, (int)Math.Round(days / 30.44)), "month") + " ago",

            // Never "0 years ago" on the day it turns one.
            _ => Plural.Of(Math.Max(1, (int)(days / 365.25)), "year") + " ago",
        };

        return span.Length == 0
            ? local.ToString("d MMM yyyy")
            : $"{local:d MMM yyyy}  ·  {span}";
    }

    /// <summary>
    /// Swaps the selected driver's photo without opening the whole form -
    /// race-day photos get taken in a burst at the pit table, and opening
    /// nine dialogs to file nine pictures would see it never happen.
    /// </summary>
    private void QuickChangePhoto()
    {
        var profile = CardProfile();

        if (profile is null)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = $"Choose a photo for {(string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName)}",
            Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var imported = DriverPhotos.Import(dialog.FileName, out var error);

        if (imported is null)
        {
            AppDialog.Show(this, $"Couldn't use that picture: {error}", "Photo not added",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var previous = profile.PhotoFileName;
        var transponder = profile.Transponder;
        var store = _store;

        // Works on the store, not this page, which may be gone by the time
        // Undo is pressed. The old photo was retired rather than deleted
        // below, so it can be fetched back.
        _undo.Capture($"Change photo for {DescribeDeleted(profile)}", () =>
        {
            // This driver, not just whoever is on the transponder now - see
            // CurrentVersionOf.
            if (store.CurrentVersionOf(profile, transponder) is not { } current)
            {
                throw new InvalidOperationException($"the driver on {transponder} has been deleted since, so there is no photo to put back.");
            }

            var replaced = current.PhotoFileName;
            DriverPhotos.Unretire(previous);
            current.PhotoFileName = previous;
            store.Upsert(current);

            if (!string.Equals(replaced, previous, StringComparison.OrdinalIgnoreCase))
            {
                DriverPhotos.Retire(replaced);
            }
        });

        profile.PhotoFileName = imported;
        _store.Upsert(profile);
        DriverPhotos.Retire(previous);
        RefreshList();
    }

    /// <summary>
    /// The card's scrolling surface.
    ///
    /// A plain Panel with double buffering and a full repaint on resize and
    /// scroll: everything on it is painted, offset by the scroll position,
    /// and a partial repaint after a scroll step would leave half of the
    /// old position on screen.
    /// </summary>
    private sealed class CardCanvas : Panel
    {
        public CardCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }
    }

    // ==================== Add / edit / delete ====================

    private void AddNew()
    {
        using var form = new ProfileForm(existing: null, _classStore, undo: _undo, profiles: _store, speakName: SpeakName);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            // TryAdd, not Upsert: Upsert replaces whatever is stored under
            // the same transponder, which is how adding a driver on a
            // transponder already in use silently wiped the other driver.
            // The form has already refused a clash; this is the backstop.
            if (!_store.TryAdd(form.Result, out var refusal))
            {
                AppDialog.Show(this, refusal, "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RefreshAll();
        }
    }

    private void EditSelected()
    {
        // The driver on the card, which is the one this means. It used to be
        // the first highlighted row - so with several highlighted, "Edit
        // Profile..." under Sam's card opened whoever sorted first, and with
        // none highlighted it did nothing at all under a card showing a
        // driver.
        if (CardProfile() is not { } existing)
        {
            return;
        }

        var transponder = existing.Transponder;

        // Copied before the dialog opens: the old values are what an undo
        // puts back, and the live profile can be changed in place elsewhere.
        var before = ProfileStore.Snapshot(existing);

        using var form = new ProfileForm(existing, _classStore, fixedTransponder: transponder, undo: _undo, profiles: _store, speakName: SpeakName);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            var afterPhoto = form.Result.PhotoFileName;
            var store = _store;

            // Saved without a change: nothing for an undo to put back.
            var unchanged = System.Text.Json.JsonSerializer.Serialize(before) == System.Text.Json.JsonSerializer.Serialize(form.Result);

            if (!unchanged)
            {
                _undo.Capture($"Edit driver {DescribeDeleted(before)}", () =>
                {
                    // The alt transponder the old profile had may be somebody
                    // else's by now; putting it back would give one car two owners.
                    // Split transponders too: one put back that another driver
                    // has been given since would leave two drivers claiming it.
                    if ((store.DescribeClash(before, editing: before.Transponder)
                         ?? store.DescribeSplitClash(before, editing: before.Transponder)) is { } clash)
                    {
                        throw new InvalidOperationException($"the old details can't come back - {clash}");
                    }

                    var photoNow = store.Get(before.Transponder)?.PhotoFileName ?? afterPhoto;

                    if (store.PutBack(new[] { before }, Array.Empty<string>()) == 0)
                    {
                        throw new InvalidOperationException($"{DescribeDeleted(before)} has been deleted since, so there is nothing to put back.");
                    }

                    // The form retired the old photo instead of deleting it.
                    if (!string.Equals(photoNow, before.PhotoFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        DriverPhotos.Unretire(before.PhotoFileName);
                        DriverPhotos.Retire(photoNow);
                    }
                });
            }

            _store.Upsert(form.Result);
            RefreshAll();
        }
    }

    /// <summary>
    /// Reads the roster's names out, one at a time, so a name the voice says
    /// wrong is found before a room full of people hears it.
    ///
    /// Handed the drivers in the roster as it stands, in the order it shows
    /// them - so a search typed in the box above it ("Stock", "Novice")
    /// narrows what gets checked, and the window's own tick box narrows it
    /// again to whoever is checked in for the race day that is open. The
    /// whole saved roster would be the wrong list on a club night: nobody is
    /// going to sit through two hundred names to find the three that matter
    /// tonight.
    ///
    /// The profiles themselves rather than their transponders, because the
    /// window reads DriverProfile.SpokenName - the same property the
    /// announcer reads when it fills {driver} on the night.
    /// </summary>
    private void CheckNames()
    {
        var drivers = _list.Items
            .Cast<ListViewItem>()
            .Select(item => _store.Get((string)item.Tag!))
            .ToList();

        NameCheckForm.ShowFor(this, drivers, SpeakName, _sources?.CheckIns);
    }

    private void DeleteSelected()
    {
        var removed = _list.SelectedItems
            .Cast<ListViewItem>()
            .Select(i => _store.Get((string)i.Tag!))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        if (removed.Count == 0)
        {
            return;
        }

        var what = removed.Count == 1
            ? $"Delete the saved profile for transponder {removed[0].Transponder}?"
            : $"Delete these {removed.Count} driver profiles?\r\n\r\n"
              + string.Join("\r\n", removed.Select(p => $"   {p.Transponder}  {p.DriverName}"));

        if (AppDialog.Show(this, what, "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // Photo files are deliberately NOT deleted here: the undo below
        // can bring the profile back, and a profile restored to a photo
        // that no longer exists would be undo in name only. An orphaned
        // image is a few hundred kilobytes; a broken undo is a lie.
        // Nothing else is deleted with a profile - check-ins, rentals,
        // handicaps, inspections and setups are kept (by transponder, or by
        // name for setups) and simply match again when it is back. The one
        // thing that does follow is the check-in PIN, taken by the next PIN
        // check; RacerPins keeps it aside and gives the same PIN back once
        // the driver returns.
        _undo.Capture(
            removed.Count == 1 ? $"Delete driver {DescribeDeleted(removed[0])}" : $"Delete {removed.Count} driver profiles",
            () =>
            {
                // RestoreDeleted rather than Upsert. Upsert replaces whatever
                // is stored on the same transponder, so this undo used to wipe
                // out a driver added on that transponder since the delete, and
                // could leave two drivers on one transponder when somebody had
                // taken one of them as their alt transponder. A profile whose
                // transponder is somebody else's now stays deleted; the others
                // still come back, because the undo is used up either way and
                // refusing the lot would lose every one of them for good.
                var refused = _store.RestoreDeleted(removed);

                if (refused.Count == removed.Count)
                {
                    throw new InvalidOperationException(DescribeRefusedUndo(refused));
                }

                if (!IsDisposed && !Disposing)
                {
                    RefreshAll();
                }

                if (refused.Count > 0)
                {
                    var back = removed.Count - refused.Count;

                    AppDialog.Show(IsDisposed || Disposing ? null : this,
                        $"Brought back {back} of the {removed.Count} driver profiles.\r\n\r\n"
                        + (refused.Count == 1
                            ? "This one stays deleted, because another driver has one of its transponders now:"
                            : "These stay deleted, because other drivers have their transponders now:")
                        + "\r\n\r\n"
                        + string.Join("\r\n", refused.Select(r => $"   {DescribeDeleted(r.Profile)}: {r.Reason}."))
                        + "\r\n\r\nThe other drivers were left as they are. To have one of these back, free the transponder first and then add the driver again.",
                        "Some drivers not restored", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            });

        foreach (var profile in removed)
        {
            _store.Delete(profile.Transponder);
        }

        RefreshAll();
    }

    /// <summary>
    /// Why undoing a delete brought nothing back, for the undo log: each
    /// profile, and whose its transponder is now.
    /// </summary>
    private static string DescribeRefusedUndo(IReadOnlyList<(DriverProfile Profile, string Reason)> refused)
    {
        if (refused.Count == 1)
        {
            return $"{DescribeDeleted(refused[0].Profile)} can't come back - {refused[0].Reason}. Nothing was changed.";
        }

        var shown = refused.Take(3).Select(r => $"{DescribeDeleted(r.Profile)}: {r.Reason}");
        var more = refused.Count > 3 ? $"; and {refused.Count - 3} more" : string.Empty;

        return $"none of them can come back, because other drivers have their transponders now - {string.Join("; ", shown)}{more}. Nothing was changed.";
    }

    /// <summary>A deleted driver as the undo messages name them: "Sam (8467BE)", or the transponder alone.</summary>
    private static string DescribeDeleted(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : $"{profile.DriverName.Trim()} ({profile.Transponder})";

    private void DownloadTemplate()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "driver-roster-template.csv",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var lines = new List<string>
            {
                CsvHelper.BuildRow("Transponder", "DriverName", "CarModel", "ClassName", "Team", "Sponsors", "Email", "Notes"),
                CsvHelper.BuildRow("8467BE", "Sam", "Mazda MX-5 Miata", "Stock Buggy", "HFCC", "Kyosho, PN Racing", "sam@example.com", "example row - delete before importing"),
            };
            File.WriteAllLines(dialog.FileName, lines, CsvHelper.FileEncoding);
            AppDialog.Show(this, $"Template saved to:\n{dialog.FileName}\n\nFill in one row per driver (Transponder is required; everything else is optional), then use \"Import from CSV...\" to load it.",
                "Template saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save the template: {ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Imports drivers - and, when the file has them, their championship
    /// points so far - from any CSV: this app's template, a spreadsheet, or
    /// the club's old timing software's export (27 Sep 2026). The file's
    /// columns are shown first (CsvMappingForm) with a guess at what each
    /// one is, to be put right before anything changes.
    /// </summary>
    private void ImportFromCsv()
    {
        using var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Read as bytes and decoded by CsvHelper.DecodeText, not
        // File.ReadAllLines: that assumes UTF-8, and Excel's plain "CSV"
        // save is in the Windows code page, which turned every accented
        // name into replacement characters. Each line keeps its number in
        // the file so the summary's "Row 12" is still line 12 when blank
        // lines above it have been dropped.
        List<(string Text, int LineNumber)> lines;
        try
        {
            lines = CsvHelper.NonBlankLines(CsvHelper.DecodeText(File.ReadAllBytes(dialog.FileName)));
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't read that file: {ex.Message}", "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (lines.Count < 2)
        {
            AppDialog.Show(this,
                lines.Count == 0 ? "That file is empty." : "That file has a heading row and nothing under it.",
                "Nothing to import", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Comma, semicolon or tab, whichever the file uses - and the first
        // row taken as the headings, which is what every export writes.
        var delimiter = DriverImport.DetectDelimiter(lines[0].Text);
        var headers = DriverImport.ParseLine(lines[0].Text, delimiter);
        var rows = lines.Skip(1).Select(line => DriverImport.ParseLine(line.Text, delimiter)).ToList();
        var rowLineNumbers = lines.Skip(1).Select(line => line.LineNumber).ToList();

        ImportRows(Path.GetFileName(dialog.FileName), headers, rows, rowLineNumbers, fromFile: true);
    }

    /// <summary>
    /// Imports drivers from a table on the clipboard (22 Oct 2026) - copied
    /// off the club's old timing software's web page, or out of a
    /// spreadsheet - through the same column preview and the same import as
    /// a CSV file, so a driver already here, a transponder already taken and
    /// points so far are all dealt with exactly as they are for a file.
    /// </summary>
    private void ImportPastedTable()
    {
        string? html = null;
        string? text = null;

        try
        {
            if (Clipboard.ContainsText(TextDataFormat.Html))
            {
                html = Clipboard.GetText(TextDataFormat.Html);
            }

            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                text = Clipboard.GetText(TextDataFormat.UnicodeText);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            // Another program holding the clipboard open as it is read.
            AppDialog.Show(this, $"Couldn't read the clipboard just now ({ex.Message}). Try Paste a Table again.",
                "Paste a table", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (PastedTable.Read(html, text) is not { } table)
        {
            AppDialog.Show(this,
                "There's no table on the clipboard to import.\n\n"
                + "On the web page or in the spreadsheet, select the whole list - its headings and every driver's row - "
                + "and copy it (Ctrl+C). Then press Paste a Table here.",
                "Nothing to paste", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ImportRows(
            table.FromWebPage ? "table from a web page" : "pasted table",
            table.Headers.ToList(),
            table.Rows.ToList(),
            table.RowNumbers.ToList(),
            fromFile: false);
    }

    /// <summary>
    /// The import itself, once a file or a pasted table has been read into
    /// headings and rows: the column preview, then the drivers and their
    /// points. <paramref name="rowLineNumbers"/> is each row's place in the
    /// file or the table, for the summary's "Row 12".
    /// </summary>
    private void ImportRows(string source, List<string> headers, List<List<string>> rows, List<int> rowLineNumbers, bool fromFile)
    {
        var seasonStore = _sources?.Seasons;
        var seasons = seasonStore?.All ?? Array.Empty<Season>();

        using var mapping = new CsvMappingForm(source, headers, rows, DriverImport.GuessAll(headers), seasons, fromFile ? "in the file" : "in the table");

        if (mapping.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var columns = mapping.Columns;
        int Col(ImportColumn column) => columns.ToList().IndexOf(column);

        var transponderCol = Col(ImportColumn.Transponder);
        var nameCol = Col(ImportColumn.DriverName);
        var firstNameCol = Col(ImportColumn.FirstName);
        var lastNameCol = Col(ImportColumn.LastName);
        var announcerCol = Col(ImportColumn.AnnouncerName);
        var numberCol = Col(ImportColumn.RaceNumber);
        var carCol = Col(ImportColumn.Car);
        var classCol = Col(ImportColumn.ClassName);
        var teamCol = Col(ImportColumn.Team);
        var sponsorsCol = Col(ImportColumn.Sponsors);
        var notesCol = Col(ImportColumn.Notes);
        var pointsCol = Col(ImportColumn.PointsSoFar);

        // For emailing racers their check-in PINs. Organiser use only.
        var emailCol = Col(ImportColumn.Email);

        // The name on a row: its own column, or first and last put together.
        string SheetName(List<string> fields) =>
            nameCol >= 0 ? GetField(fields, nameCol) : DriverImport.JoinName(GetField(fields, firstNameCol), GetField(fields, lastNameCol));

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var skippedDetails = new List<string>();

        // Collected and saved once at the end: Upsert writes the whole
        // profiles file every call, which for a club roster meant the same
        // file rewritten a hundred times in a row.
        var batch = new Dictionary<string, DriverProfile>(StringComparer.OrdinalIgnoreCase);

        // For the undo: copies of the profiles the sheet is about to change
        // (the merge below edits them in place), and the drivers it adds.
        var beforeImport = new List<DriverProfile>();

        // The drivers the import adds, as the profiles it stores: its undo
        // removes each only while that same driver is still on file, not
        // whoever holds the transponder by then (ProfileStore.PutBack).
        var addedProfilesByImport = new List<DriverProfile>();

        // Points so far, by championship and then by driver name - the name
        // the standings go by. A later row for the same driver wins.
        var carried = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var pointsSkipped = new List<string>();
        var unknownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var r = 0; r < rows.Count; r++)
        {
            var fields = rows[r];
            var line = rowLineNumbers[r];
            var sheetName = SheetName(fields);
            DriverProfile? profile = null;

            if (transponderCol >= 0)
            {
                var transponder = GetField(fields, transponderCol);

                if (string.IsNullOrWhiteSpace(transponder))
                {
                    skipped++;
                    skippedDetails.Add($"Row {line}: missing transponder");
                    continue;
                }

                // Another driver's alternate transponder. Get resolves those to
                // the owner, so without this the row would be merged into that
                // driver's profile - renaming them after whoever is on the sheet.
                if (!batch.ContainsKey(transponder) && _store.PrimaryForAlternate(transponder) is { } altOwner)
                {
                    skipped++;
                    skippedDetails.Add($"Row {line}: {transponder} is already {OwnerName(_store.Get(altOwner), altOwner)}'s alt transponder");
                    continue;
                }

                // Merged into the existing profile rather than replacing it. A
                // roster sheet carries a handful of columns; replacing the whole
                // profile wiped everything it didn't mention - photo, race
                // number, announcer name, alternate transponder - for every
                // driver already set up. Only a column that is present AND
                // filled in changes anything.
                var existing = batch.TryGetValue(transponder, out var pending) ? pending : _store.Get(transponder);

                // WHY (20 Oct 2026): an alternate that two drivers both list is
                // left out of the timing index, so PrimaryForAlternate and Get
                // both missed it and the row made a brand-new driver with it as
                // his main transponder - who then took every lap that car did.
                // OwnerOf finds any driver who lists the id, contested or not,
                // so the row is skipped and reported like any other clash.
                if (existing is null && _store.OwnerOf(transponder) is { } claimant)
                {
                    skipped++;
                    skippedDetails.Add($"Row {line}: {transponder} is already one of {OwnerName(claimant, claimant.Transponder)}'s transponders");
                    continue;
                }

                // ...but only when it is the SAME driver. A row naming somebody
                // else on a transponder already in use is a mistake on the
                // sheet (or a transponder that changed hands), and merging it
                // renamed the existing driver - the silent replacement Add used
                // to do. Skipped and reported, so the organiser decides.
                if (existing is not null
                    && sheetName.Length > 0
                    && !string.IsNullOrWhiteSpace(existing.DriverName)
                    && !string.Equals(sheetName, existing.DriverName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    skippedDetails.Add($"Row {line}: {transponder} already belongs to {existing.DriverName.Trim()} (the sheet says {sheetName})");
                    continue;
                }

                profile = existing ?? new DriverProfile { Transponder = transponder };

                if (existing is null)
                {
                    added++;
                    addedProfilesByImport.Add(profile);
                }
                else if (!batch.ContainsKey(transponder))
                {
                    updated++;

                    // Once per driver: a row for their alt transponder and
                    // another for their main one find the same live profile, and
                    // by the second row it holds the first row's changes - a
                    // second copy would have Undo put those back instead.
                    if (!beforeImport.Any(b => string.Equals(b.Transponder, existing.Transponder, StringComparison.OrdinalIgnoreCase)))
                    {
                        beforeImport.Add(ProfileStore.Snapshot(existing));
                    }
                }

                if (sheetName.Length > 0)
                {
                    profile.DriverName = sheetName;
                }

                profile.AnnouncerName = MergeField(profile.AnnouncerName, fields, announcerCol);
                profile.RaceNumber = MergeField(profile.RaceNumber, fields, numberCol);
                // A second row for a driver already read from this sheet, in
                // a different class, is a driver entered in two classes -
                // what a club's export does for someone running two cars.
                // That class and car are added to their others rather than
                // overwriting the first row's (DriverImport.MergeClass and
                // MergeCar). Any other row sets the main class and the active
                // car, as it always did.
                var repeatRow = pending is not null;
                DriverImport.MergeCar(profile, repeatRow, GetField(fields, carCol));
                DriverImport.MergeClass(profile, repeatRow, GetField(fields, classCol));
                profile.Team = MergeField(profile.Team, fields, teamCol);
                profile.Sponsors = MergeField(profile.Sponsors, fields, sponsorsCol);
                profile.Notes = MergeField(profile.Notes, fields, notesCol);
                profile.Email = MergeField(profile.Email, fields, emailCol);

                batch[transponder] = profile;
            }

            // --- Points so far ---
            if (pointsCol < 0)
            {
                continue;
            }

            var pointsText = GetField(fields, pointsCol);

            if (!DriverImport.TryParsePoints(pointsText, out var points))
            {
                // A blank cell means nothing to carry in. Anything else - "DSQ",
                // "n/a" - used to be dropped without a word, so the driver
                // started the championship on nothing and the summary looked
                // complete. Say which rows, as the points dialog would.
                if (!string.IsNullOrWhiteSpace(pointsText))
                {
                    var who = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName.Trim() : sheetName;
                    pointsSkipped.Add(who.Length == 0
                        ? $"Row {line}: points \"{pointsText.Trim()}\" aren't a number"
                        : $"Row {line}: {who}'s points \"{pointsText.Trim()}\" aren't a number");
                }

                continue;
            }

            // The name the standings will know them by: the profile's, which
            // is what their results are saved under.
            var driver = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName.Trim() : sheetName;

            if (driver.Length == 0)
            {
                pointsSkipped.Add($"Row {line}: no driver name for the points");
                continue;
            }

            Season? target;

            if (mapping.SeasonPerClass)
            {
                var className = classCol >= 0 && GetField(fields, classCol) is { Length: > 0 } sheetClass
                    ? sheetClass
                    : (profile?.ClassName ?? string.Empty).Trim();

                var forClass = seasons
                    .Where(s => !string.IsNullOrWhiteSpace(s.ClassName)
                                && string.Equals(s.ClassName.Trim(), className, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Two championships for one class - this season's and last
                // season's, usually. Guessing would put a whole class's
                // points in the wrong one without a word.
                if (forClass.Count != 1)
                {
                    pointsSkipped.Add(className.Length == 0
                        ? $"Row {line}: {driver} has no class, so no championship for the points"
                        : forClass.Count == 0
                            ? $"Row {line}: no championship for {className}"
                            : $"Row {line}: {forClass.Count} championships for {className} - import again and pick the one");
                    continue;
                }

                target = forClass[0];
            }
            else
            {
                target = seasons.FirstOrDefault(s => string.Equals(s.Id, mapping.SeasonId, StringComparison.Ordinal));

                if (target is null)
                {
                    continue;
                }
            }

            // A name no driver goes by scores as somebody new - said at the
            // end, in case it is a spelling of somebody who isn't.
            if (profile is null && !_store.All.Any(d => string.Equals((d.DriverName ?? string.Empty).Trim(), driver, StringComparison.OrdinalIgnoreCase)))
            {
                unknownNames.Add(driver);
            }

            if (!carried.TryGetValue(target.Id, out var forSeason))
            {
                forSeason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                carried[target.Id] = forSeason;
            }

            forSeason[driver] = points;
        }

        // What each championship carried before, for the undo.
        var carriedBefore = new List<(string SeasonId, List<CarriedPointsEntry> Entries)>();

        if (batch.Count > 0 || carried.Count > 0)
        {
            var store = _store;

            foreach (var seasonId in carried.Keys)
            {
                if (seasonStore?.Get(seasonId) is { } season)
                {
                    carriedBefore.Add((seasonId, season.CarriedPoints.Select(CopyOf).ToList()));
                }
            }

            // Targeted rather than a whole-roster snapshot: only the drivers
            // this sheet touched go back, so an edit made to anyone else
            // since the import survives the undo. The championships go back
            // to the points they carried before.
            _undo.Capture($"Import {Plural.Of(Math.Max(batch.Count, carried.Values.Sum(s => s.Count)), "driver")} from {(fromFile ? "CSV" : "a pasted table")}", () =>
            {
                var putBack = batch.Count > 0 ? store.PutBack(beforeImport, addedProfilesByImport) : 0;
                var seasonsPutBack = 0;

                foreach (var (seasonId, entries) in carriedBefore)
                {
                    if (seasonStore?.Get(seasonId) is { } season)
                    {
                        season.CarriedPoints = entries.Select(CopyOf).ToList();
                        seasonsPutBack++;
                    }
                }

                if (seasonsPutBack > 0)
                {
                    seasonStore!.Save();
                }

                if (putBack == 0 && seasonsPutBack == 0)
                {
                    throw new InvalidOperationException("every driver and championship that import touched has been deleted since, so there is nothing to put back.");
                }
            });
        }

        _store.UpsertMany(batch.Values);

        // Points so far: each driver on the sheet replaces what they carried
        // into that championship before; anyone not on it keeps theirs.
        var pointsImported = 0;

        foreach (var (seasonId, byDriver) in carried)
        {
            if (seasonStore?.Get(seasonId) is not { } season)
            {
                continue;
            }

            var entries = season.CarriedPoints
                .Where(entry => !byDriver.ContainsKey((entry.DriverName ?? string.Empty).Trim()))
                .ToList();

            entries.AddRange(byDriver.Select(kv => new CarriedPointsEntry { DriverName = kv.Key, Points = kv.Value }));

            season.CarriedPoints = entries;
            pointsImported += byDriver.Count;
        }

        if (carried.Count > 0)
        {
            seasonStore!.Save();
        }

        RefreshAll();

        var imported = added + updated;
        var summary = transponderCol >= 0 ? $"Imported {Plural.Of(imported, "driver profile")}." : string.Empty;
        if (updated > 0)
        {
            summary += $"\n\n{Plural.Of(added, "new driver")}, {Plural.Of(updated, "existing profile")} updated - only the columns filled in on the sheet were changed.";
        }
        if (pointsCol >= 0)
        {
            summary += $"\n\nPoints so far carried into {Plural.Of(carried.Count, "championship")} for {Plural.Of(pointsImported, "driver")}.";
        }
        if (skipped > 0)
        {
            summary += $"\n\nSkipped {Plural.Of(skipped, "row")} - nothing was changed for these:\n" + string.Join("\n", skippedDetails.Take(10));
            if (skippedDetails.Count > 10)
            {
                summary += $"\n...and {skippedDetails.Count - 10} more.";
            }
        }
        if (unknownNames.Count > 0)
        {
            summary += $"\n\n{Plural.Of(unknownNames.Count, "name")} with points matched no driver here, so each is in the "
                       + "standings on their own - check they're spelled as the driver's results are:\n"
                       + string.Join(", ", unknownNames.Take(10))
                       + (unknownNames.Count > 10 ? $" and {unknownNames.Count - 10} more" : string.Empty);
        }
        if (pointsSkipped.Count > 0)
        {
            summary += $"\n\nPoints not carried in for {Plural.Of(pointsSkipped.Count, "row")}:\n" + string.Join("\n", pointsSkipped.Take(10));
            if (pointsSkipped.Count > 10)
            {
                summary += $"\n...and {pointsSkipped.Count - 10} more.";
            }
        }

        AppDialog.Show(this, summary.Trim(), "Import complete", MessageBoxButtons.OK,
            skipped > 0 || pointsSkipped.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private static CarriedPointsEntry CopyOf(CarriedPointsEntry entry) =>
        new() { DriverName = entry.DriverName, Points = entry.Points };

    /// <summary>A driver's name for an import report, or their transponder when they have no name.</summary>
    private static string OwnerName(DriverProfile? profile, string transponder) =>
        profile is null || string.IsNullOrWhiteSpace(profile.DriverName) ? transponder : profile.DriverName.Trim();

    private static string GetField(List<string> fields, int index) =>
        index >= 0 && index < fields.Count ? fields[index].Trim() : string.Empty;

    /// <summary>The sheet's value for a column if it has one, otherwise what the profile already had.</summary>
    private static string MergeField(string current, List<string> fields, int index)
    {
        var value = GetField(fields, index);
        return string.IsNullOrEmpty(value) ? current : value;
    }
}
