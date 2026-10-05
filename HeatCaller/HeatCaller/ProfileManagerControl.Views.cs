using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// What a driver's page needs for their garage, transponders and setups.
/// Given by MainForm, which owns the stores and builds the same garage the
/// Garage page shows - one set of cars, two views of it.
/// </summary>
public sealed class DriverGarageSources
{
    public required CarStore Cars { get; init; }

    public required SetupLogStore Setups { get; init; }

    public required ChassisTemplateStore Templates { get; init; }

    /// <summary>A garage for one driver's cars (GarageControl in its driver mode).</summary>
    public required Func<GarageControl> MakeGarage { get; init; }

    /// <summary>Opens a car's own window - photos, details, setups - over the given window.</summary>
    public required Action<Car, IWin32Window> OpenCar { get; init; }

    /// <summary>The battery packs, for the Batteries tab - none, and there is no such tab.</summary>
    public BatteryPackStore? Batteries { get; init; }

    /// <summary>The race history, for the sessions and pace on the Batteries tab.</summary>
    public SessionHistoryStore? History { get; init; }

    /// <summary>
    /// The transponders the split loop has read lately that are nobody's
    /// split transponder, the most recent first - so Add Split Transponder
    /// fills itself in when a car is driven over the loop. Null without a
    /// split loop.
    /// </summary>
    public Func<IReadOnlyList<string>>? SplitSeenRecently { get; init; }
}

/// <summary>
/// The driver's side of Driver Hub: their header, the Drivers | Garage
/// switch, and the tabs.
///
/// The roster runs down the left, names and numbers only. Beside it, the
/// driver shown: a header with their picture, name and chips (number,
/// class, checked in), the Drivers | Garage switch and Edit Profile; and
/// under it either the Drivers view - tabs for Profile, Garage,
/// Transponders, Setups, Batteries and Stats - or the Garage view, their cars big with
/// their transponders beside them. The page opens on the view used last
/// (AppSettings.DriversPageView).
/// </summary>
public sealed partial class ProfileManagerControl
{
    /// <summary>The card's tabs, in the order they are shown.</summary>
    private enum CardTab
    {
        Profile,
        Garage,
        Transponders,
        Setups,
        Batteries,
        Stats,
    }

    /// <summary>The setting's value for the roster-and-card view - "Hub" since before its button read Drivers, so a saved choice still reads.</summary>
    private const string HubView = "Hub";
    private const string GarageView = "Garage";

    /// <summary>The picture in the header at the normal text size - big enough to know the driver by.</summary>
    private const int HeaderAvatar = 144;

    /// <summary>Null on a page built without them: then there are no views or tabs, and the card is the whole driver, as before.</summary>
    private readonly DriverGarageSources? _garageSources;

    private CardTab _cardTab = CardTab.Profile;

    private UnderlineTabs? _tabs;

    /// <summary>The tabs shown, in order - every CardTab, but Batteries only with the stores it needs.</summary>
    private List<CardTab> _tabOrder = Enum.GetValues<CardTab>().ToList();

    /// <summary>The header over the views: the driver's picture, name and chips, and the switch and Edit Profile.</summary>
    private DriverHeader? _header;

    /// <summary>Where the header drew the picture - clicked, it changes the photo.</summary>
    private Rectangle _headerAvatar;

    /// <summary>The card's body: the painted card, or the garage, transponders or setups.</summary>
    private Panel? _cardBody;

    private GarageControl? _hubGarage;
    private DriverTransponderPanel? _hubTransponders;
    private DriverSetupsPanel? _hubSetups;
    private DriverBatteriesPanel? _hubBatteries;

    private Button? _btnHub;
    private Button? _btnGarageView;

    /// <summary>The Drivers view: the tabs over the card's body.</summary>
    private Control? _driversView;

    /// <summary>The Garage view: the driver's cars, filling the page.</summary>
    private Control? _garageView;

    private GarageControl? _bigGarage;

    /// <summary>The Drivers view's roster beside the card - the least it can do with is its.</summary>
    private Beside? _band;

    /// <summary>The Drivers view's buttons along the foot of the page - hidden in the Garage view, which has its own.</summary>
    private Control? _bottomBar;

    /// <summary>In the Garage view, where the Drivers view's search box is: whose cars it shows.</summary>
    private readonly ComboBox _cmbGarageDriver = new();
    private readonly Label _lblGarageDriver = new();

    /// <summary>
    /// What the Garage view shows in place of one driver - everyone's cars
    /// or the club's own (GarageControl.EveryoneKey, ClubKey) - or null for
    /// the driver on the card. Picked in its picker; a driver picked there,
    /// or the Drivers view, puts it back to the driver.
    /// </summary>
    private string? _garageGroup;

    /// <summary>Set while the Garage view's driver picker is being filled or moved to follow the roster, so that changes nothing.</summary>
    private bool _syncingPicker;

    /// <summary>The view asked for - kept apart from Visible, which reads false while the page itself is hidden.</summary>
    private bool _garageViewWanted;

    /// <summary>Whether the painted card shows the driver's record (Stats tab, or no tabs at all).</summary>
    private bool ShowsStats => _garageSources is null || _cardTab == CardTab.Stats;

    /// <summary>Whether the painted card shows who they are (Profile tab, or no tabs at all).</summary>
    private bool ShowsProfile => _garageSources is null || _cardTab == CardTab.Profile;

    /// <summary>Whether the painted card starts with the driver's picture, name and chips - only without the header (no garage sources).</summary>
    private bool PaintsHeaderOnCard => _garageSources is null;

    // ------------------------------------------------------------ the header

    /// <summary>The header over the driver's card: their picture (click it for the photo), name and chips.</summary>
    private DriverHeader BuildHeader()
    {
        _header = new DriverHeader(RenderHeader) { AvatarBounds = () => _headerAvatar };
        _header.AvatarClicked += QuickChangePhoto;

        return _header;
    }

    /// <summary>
    /// The row across the top of the page: the Drivers | Garage switch in
    /// the upper left.
    /// </summary>
    private ButtonBar BuildTopRow()
    {
        _btnHub = new Button { Text = "Drivers" };
        _btnHub.Click += (_, _) => SelectView(garage: false);

        _btnGarageView = new Button { Text = GarageView };
        _btnGarageView.Click += (_, _) => SelectView(garage: true);

        return new ButtonBar(tight: true).With(_btnHub, _btnGarageView);
    }

    private (Font Font, int Count, int Width)? _garageDriverWidth;

    /// <summary>The picker as wide as the roster's search box; its list as wide as the longest name.</summary>
    private void FitGarageDriverPicker() =>
        _cmbGarageDriver.DropDownWidth = Math.Max(Math.Max(1, _cmbGarageDriver.Width), Pieces.PickerWidth(_cmbGarageDriver, ref _garageDriverWidth));

    /// <summary>
    /// Lays out - and, when <paramref name="draw"/> is set, paints - the
    /// header's driver part in <paramref name="area"/>: the picture as a
    /// ringed disc, the name, how they are announced when it differs, and
    /// the chips. Returns how tall it came out.
    /// </summary>
    private int RenderHeader(Graphics g, Rectangle area, bool draw)
    {
        var profile = CardProfile();

        if (profile is null)
        {
            _headerAvatar = Rectangle.Empty;

            var message = _list.Items.Count == 0 ? "No drivers yet. Click Add Driver." : "Pick a driver on the left.";
            var height = (int)Math.Ceiling(g.MeasureString(message, AppTheme.BodyFont, Math.Max(1, area.Width)).Height);

            if (draw)
            {
                using var muted = new SolidBrush(AppTheme.TextMuted);
                g.DrawString(message, AppTheme.BodyFont, muted, new RectangleF(area.Left, area.Top, area.Width, height + 1));
            }

            return height + 4;
        }

        var display = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();
        var size = UiLayout.AtTextSize(HeaderAvatar);
        var ring = Math.Max(2, UiLayout.AtTextSize(3));

        _headerAvatar = new Rectangle(area.Left + ring, area.Top + ring, size, size);

        if (draw)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DriverPhotos.DrawAvatar(g, _headerAvatar, display, profile.PhotoFileName, cornerRadius: size / 2);

            using var pen = new Pen(AppTheme.Accent, ring);
            g.DrawEllipse(pen, RectangleF.Inflate(_headerAvatar, ring / 2f, ring / 2f));
        }

        var left = _headerAvatar.Right + ring + UiLayout.AtTextSize(18);
        var width = Math.Max(60, area.Right - left);

        // The name, how they are announced and the chips, from this top -
        // returns where they end.
        float Words(bool paint, float top)
        {
            var c = new CardCursor(g, paint, left, width) { Y = top };

            Paragraph(c, display, AppTheme.TitleFont, AppTheme.TextDark, maxLines: 2);

            if (!string.IsNullOrWhiteSpace(profile.AnnouncerName)
                && !string.Equals(profile.AnnouncerName.Trim(), display, StringComparison.OrdinalIgnoreCase))
            {
                Paragraph(c, $"Announced as \"{profile.AnnouncerName.Trim()}\"", AppTheme.SmallFont, AppTheme.TextMuted, maxLines: 2);
            }

            c.Y += 2f;
            Chips(c, ChipsFor(profile));
            return c.Y;
        }

        // Measured first, then set level with the middle of the picture
        // when shorter than it, rather than along its top edge.
        var wordsHeight = Words(paint: false, area.Top) - area.Top;
        var pictureHeight = size + ring * 2;
        var top = area.Top + Math.Max(0f, (pictureHeight - wordsHeight) / 2f);
        var end = draw ? Words(paint: true, top) : top + wordsHeight;

        var bottom = Math.Max(_headerAvatar.Bottom + ring, (int)Math.Ceiling(end));
        return bottom - area.Top;
    }

    /// <summary>The driver at a glance: number, class (in its colour), checked in, rental, outside records.</summary>
    private List<Chip> ChipsFor(DriverProfile profile)
    {
        var chips = new List<Chip>();

        if (!string.IsNullOrWhiteSpace(profile.RaceNumber))
        {
            chips.Add(new Chip("#" + profile.RaceNumber.Trim(), AppTheme.Accent, AppTheme.OnAccent));
        }

        // A chip for every class they race, the main one first.
        foreach (var className in profile.AllClasses)
        {
            var raceClass = _classStore.All.FirstOrDefault(cls =>
                string.Equals(cls.Name, className, StringComparison.OrdinalIgnoreCase));

            // A class colour is the club's own, not a theme fill, so it
            // keeps the white text it has everywhere else in the app.
            chips.Add(new Chip(className, raceClass?.Colour ?? RaceClass.DefaultColourFor(className), Color.White));
        }

        var stats = _sources?.History is null ? null : _statsCache.GetValueOrDefault(profile.Transponder);

        if (stats?.RaceDay?.CheckedIn == true)
        {
            chips.Add(new Chip("Checked in", AppTheme.Success, AppTheme.OnSuccess));
        }

        if (profile.IsRental)
        {
            chips.Add(new Chip("Rental", AppTheme.Warning, AppTheme.OnWarning));
        }

        if (profile.ExcludeFromRecords)
        {
            chips.Add(new Chip("Outside records and points", AppTheme.Warning, AppTheme.OnWarning));
        }

        return chips;
    }

    /// <summary>The header drawn again for the driver shown - and laid out again, since chips can wrap onto another line.</summary>
    private void RefreshHeader()
    {
        if (_header is null || _header.IsDisposed)
        {
            return;
        }

        _btnCardEdit.Enabled = CardProfile() is not null;
        _btnCardPhoto.Enabled = CardProfile() is not null;
        _header.Changed();
    }

    // ------------------------------------------------------------ the card

    /// <summary>The Drivers view: the tabs, and under them the body that shows whichever is chosen.</summary>
    private Control BuildDriversView()
    {
        _tabOrder = Enum.GetValues<CardTab>()
            .Where(t => t != CardTab.Batteries || (_garageSources?.Batteries is not null && _garageSources.History is not null))
            .ToList();

        _tabs = new UnderlineTabs(_tabOrder.Select(t => t.ToString()).ToArray());
        _tabs.SelectedChanged += () => SelectCardTab(_tabOrder[Math.Clamp(_tabs.Selected, 0, _tabOrder.Count - 1)]);

        var table = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Tag = AppTheme.KeepBackColor,
            BackColor = AppTheme.Surface,
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _tabs.Dock = DockStyle.Fill;
        _tabs.Margin = new Padding(0, 0, 0, UiLayout.RowGap);
        table.Controls.Add(_tabs, 0, 0);

        var body = BuildCardBody();
        body.Dock = DockStyle.Fill;
        body.Margin = Padding.Empty;
        table.Controls.Add(body, 0, 1);

        return table;
    }

    private Panel BuildCardBody()
    {
        var sources = _garageSources!;

        _cardBody = new Panel { Tag = AppTheme.KeepBackColor, BackColor = AppTheme.Surface };

        _cardScroll.Dock = DockStyle.Fill;
        _cardBody.Controls.Add(_cardScroll);

        _hubGarage = sources.MakeGarage();
        _hubGarage.Dock = DockStyle.Fill;
        _hubGarage.Visible = false;
        _hubGarage.Changed += OnDriverThingsChanged;
        _cardBody.Controls.Add(_hubGarage);

        _hubTransponders = new DriverTransponderPanel(_store, sources.Cars, sources.History, _undo) { Visible = false, SplitSeenRecently = sources.SplitSeenRecently };
        _hubTransponders.Changed += OnDriverThingsChanged;
        _cardBody.Controls.Add(_hubTransponders);

        _hubSetups = new DriverSetupsPanel(_store, sources.Cars, sources.Setups, sources.Templates, car => sources.OpenCar(car, this), _undo) { Visible = false };
        _hubSetups.Changed += OnDriverThingsChanged;
        _cardBody.Controls.Add(_hubSetups);

        if (sources.Batteries is not null && sources.History is not null)
        {
            _hubBatteries = new DriverBatteriesPanel(_store, sources.Batteries, sources.History, _undo) { Visible = false };
            _hubBatteries.Changed += OnDriverThingsChanged;
            _cardBody.Controls.Add(_hubBatteries);
        }

        // The garage's Batteries strip opens this driver's Batteries tab.
        _hubGarage.OpenBatteries += () => SelectCardTab(CardTab.Batteries);

        return _cardBody;
    }

    private void SelectCardTab(CardTab tab)
    {
        if (_cardBody is null || !_tabOrder.Contains(tab))
        {
            return;
        }

        _cardTab = tab;

        if (_tabs is not null && _tabs.Selected != _tabOrder.IndexOf(tab))
        {
            _tabs.Selected = _tabOrder.IndexOf(tab);
        }

        var painted = tab is CardTab.Profile or CardTab.Stats;
        _cardScroll.Visible = painted;
        _hubGarage!.Visible = tab == CardTab.Garage;
        _hubTransponders!.Visible = tab == CardTab.Transponders;
        _hubSetups!.Visible = tab == CardTab.Setups;

        if (_hubBatteries is not null)
        {
            _hubBatteries.Visible = tab == CardTab.Batteries;
        }

        if (painted)
        {
            _cardScroll.AutoScrollPosition = Point.Empty;
            UpdateCardExtent();
            _cardScroll.Invalidate();
        }

        ShowDriverInViews(CardProfile()?.Transponder);
    }

    // ----------------------------------------------------------- the views

    /// <summary>The two views, one showing: the roster beside the driver's card, or the Garage view.</summary>
    private Panel BuildViews(Beside band, Control bottomBar)
    {
        _band = band;
        _bottomBar = bottomBar;
        _driversView = band;
        _garageView = BuildGarageView();

        var views = new Panel { Tag = AppTheme.KeepBackColor };

        _driversView.Dock = DockStyle.Fill;
        _garageView.Dock = DockStyle.Fill;

        views.Controls.Add(_driversView);
        views.Controls.Add(_garageView);

        // The view used last - the Drivers view on a new install.
        _garageViewWanted = string.Equals(_settings?.DriversPageView, GarageView, StringComparison.OrdinalIgnoreCase);
        ApplyView();

        return views;
    }

    /// <summary>The least the Drivers view's card body can do with: the tabs over the card's least.</summary>
    private int DriversViewLeastHeight()
    {
        var tabs = (_tabs?.GetPreferredSize(Size.Empty).Height ?? 0) + UiLayout.RowGap;
        return tabs + UiLayout.AtTextSize(LeastCanvasHeight);
    }

    /// <summary>The least height the views can do with at this width: the roster and card's, or a garage wall's.</summary>
    private int ViewsLeastHeight(int width) =>
        _garageViewWanted || _band is null ? UiLayout.AtTextSize(300) : _band.LeastHeight(width);

    /// <summary>
    /// The Garage view: the driver picked where the Drivers view has its
    /// search box - a card as wide as the roster, "Driver" over the drop-down
    /// as "Find" is over the search - and under it their cars across the
    /// whole page. Their transponders are on the Drivers view's Transponders
    /// tab.
    /// </summary>
    private Control BuildGarageView()
    {
        _bigGarage = _garageSources!.MakeGarage();
        _bigGarage.Changed += OnDriverThingsChanged;

        // From the Garage view, the strip goes to the Drivers view, on the
        // Batteries tab.
        _bigGarage.OpenBatteries += () =>
        {
            SelectView(garage: false);
            SelectCardTab(CardTab.Batteries);
        };

        _cmbGarageDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbGarageDriver.SelectionChangeCommitted += (_, _) =>
        {
            if (_syncingPicker || _cmbGarageDriver.SelectedItem is not DriverChoice choice)
            {
                return;
            }

            if (choice.Transponder is GarageControl.EveryoneKey or GarageControl.ClubKey)
            {
                // Not a driver: the roster and the card stay where they are.
                _garageGroup = choice.Transponder;
                _bigGarage?.ShowDriver(choice.Transponder);
            }
            else
            {
                SelectDriver(choice.Transponder);
            }
        };
        _cmbGarageDriver.SizeChanged += (_, _) => FitGarageDriverPicker();
        _textSize.Keep(() =>
        {
            _garageDriverWidth = null;
            FitGarageDriverPicker();
        });

        _lblGarageDriver.Text = "Driver";
        _lblGarageDriver.AutoSize = true;
        _lblGarageDriver.Font = AppTheme.SmallFont;
        _lblGarageDriver.ForeColor = AppTheme.TextMuted;

        var picker = new Section();
        picker.Add(_lblGarageDriver, stretch: false, gapBelow: UiLayout.HintGap);
        picker.Add(_cmbGarageDriver, gapBelow: 0);

        return new GarageViewHost(picker, _bigGarage, RosterWidth);
    }

    /// <summary>
    /// The Garage view's two parts, placed by hand: the picker card in the
    /// top left, as wide as the Drivers view's roster (so it sits exactly
    /// where the search box does) and as tall as it needs; the garage under
    /// it, across the whole width and the rest of the height.
    ///
    /// By hand rather than in a table: the garage is a page of its own that
    /// scrolls, with themed scrollbars parked round it, and a table with a
    /// row that sizes itself and a column changed while it lays out is the
    /// kind of arrangement that set those ranges chasing one another - six
    /// bars round a garage of one car (3 Oct).
    /// </summary>
    private sealed class GarageViewHost : Panel
    {
        private readonly Section _picker;
        private readonly Control _garage;
        private readonly Func<int, int> _pickerWidth;
        private bool _placing;

        public GarageViewHost(Section picker, Control garage, Func<int, int> pickerWidth)
        {
            _picker = picker;
            _garage = garage;
            _pickerWidth = pickerWidth;

            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Tag = AppTheme.KeepBackColor;

            // Placed here, never sized by themselves.
            picker.AutoSize = false;
            picker.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            garage.Dock = DockStyle.None;
            garage.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            garage.Margin = Padding.Empty;

            Controls.Add(picker);
            Controls.Add(garage);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            if (_placing)
            {
                return;
            }

            _placing = true;

            try
            {
                var width = Math.Max(1, ClientSize.Width);
                var height = Math.Max(1, ClientSize.Height);
                var pickerWidth = Math.Min(width, _pickerWidth(width));
                var pickerHeight = _picker.LeastHeight(pickerWidth);
                var top = pickerHeight + UiLayout.RowGap;

                var pickerBounds = new Rectangle(0, 0, pickerWidth, pickerHeight);
                var garageBounds = new Rectangle(0, top, width, Math.Max(1, height - top));

                if (_picker.Bounds != pickerBounds)
                {
                    _picker.Bounds = pickerBounds;
                }

                if (_garage.Bounds != garageBounds)
                {
                    _garage.Bounds = garageBounds;
                }
            }
            finally
            {
                _placing = false;
            }
        }
    }

    /// <summary>Shows the view asked for, and the Drivers view's buttons along the foot only with it.</summary>
    private void ApplyView()
    {
        if (_driversView is null || _garageView is null)
        {
            return;
        }

        _driversView.Visible = !_garageViewWanted;
        _garageView.Visible = _garageViewWanted;

        if (_bottomBar is not null)
        {
            _bottomBar.Visible = !_garageViewWanted;
        }

        StyleSegments();
    }

    /// <summary>Drivers or Garage, remembered for next time.</summary>
    private void SelectView(bool garage)
    {
        if (_driversView is null || _garageView is null)
        {
            return;
        }

        _garageViewWanted = garage;
        ApplyView();

        // The Drivers view is about one driver: coming back to the Garage
        // view from it shows theirs.
        if (!garage)
        {
            _garageGroup = null;
        }

        if (_settings is not null)
        {
            var name = garage ? GarageView : HubView;

            if (!string.Equals(_settings.DriversPageView, name, StringComparison.Ordinal))
            {
                _settings.DriversPageView = name;
                _settings.Save();
            }
        }

        RefreshGarageDriverPicker();
        ShowDriverInViews(CardProfile()?.Transponder);
    }

    /// <summary>The chosen view as the accent button, the other plain.</summary>
    private void StyleSegments()
    {
        if (_btnHub is not null && _btnGarageView is not null)
        {
            AppTheme.StyleButton(_btnHub, _garageViewWanted ? AppTheme.ButtonKind.Secondary : AppTheme.ButtonKind.Primary);
            AppTheme.StyleButton(_btnGarageView, _garageViewWanted ? AppTheme.ButtonKind.Primary : AppTheme.ButtonKind.Secondary);
        }

        AppTheme.StyleButton(_btnCardEdit, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnCardPhoto, AppTheme.ButtonKind.Secondary);
    }

    // ------------------------------------------------------ the driver shown

    /// <summary>
    /// The garage, transponders and setups on whatever is showing, for this
    /// driver. Only what is on screen is filled - the rest catch up when
    /// they are shown (SelectCardTab, SelectView).
    /// </summary>
    private void ShowDriverInViews(string? transponder)
    {
        if (_garageSources is null)
        {
            return;
        }

        if (_garageViewWanted)
        {
            _syncingPicker = true;

            var shown = _garageGroup ?? transponder;

            try
            {
                PointPickerAt(shown);
            }
            finally
            {
                _syncingPicker = false;
            }

            _bigGarage?.ShowDriver(shown);
            return;
        }

        switch (_cardTab)
        {
            case CardTab.Garage:
                _hubGarage?.ShowDriver(transponder);
                break;
            case CardTab.Transponders:
                _hubTransponders?.ShowDriver(transponder);
                break;
            case CardTab.Setups:
                _hubSetups?.ShowDriver(transponder);
                break;
            case CardTab.Batteries:
                _hubBatteries?.ShowDriver(transponder);
                break;
        }
    }

    /// <summary>One driver in the Garage view's picker - or everyone's cars, or the club's own.</summary>
    private sealed record DriverChoice(string Transponder, string Caption)
    {
        public override string ToString() => Caption;
    }

    /// <summary>Fills the Garage view's driver picker and puts it on the driver shown.</summary>
    private void RefreshGarageDriverPicker()
    {
        if (_garageSources is null)
        {
            return;
        }

        _syncingPicker = true;

        try
        {
            _cmbGarageDriver.BeginUpdate();
            _cmbGarageDriver.Items.Clear();

            // The whole garage first, as the Garage page's Show has them.
            _cmbGarageDriver.Items.Add(new DriverChoice(GarageControl.EveryoneKey, "Everyone's cars"));
            _cmbGarageDriver.Items.Add(new DriverChoice(GarageControl.ClubKey, "The club's own cars"));

            foreach (var profile in _store.All.OrderBy(p => p.DriverName, StringComparer.CurrentCultureIgnoreCase))
            {
                var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();
                var number = string.IsNullOrWhiteSpace(profile.RaceNumber) ? string.Empty : $"  #{profile.RaceNumber.Trim()}";
                _cmbGarageDriver.Items.Add(new DriverChoice(profile.Transponder, name + number));
            }

            _cmbGarageDriver.EndUpdate();
            _garageDriverWidth = null;
            FitGarageDriverPicker();
            PointPickerAt(_garageGroup ?? CardProfile()?.Transponder);
        }
        finally
        {
            _syncingPicker = false;
        }
    }

    private void PointPickerAt(string? transponder)
    {
        var match = _cmbGarageDriver.Items.Cast<DriverChoice>()
            .FirstOrDefault(c => string.Equals(c.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

        if (!ReferenceEquals(_cmbGarageDriver.SelectedItem, match))
        {
            _cmbGarageDriver.SelectedItem = match;
        }
    }

    /// <summary>
    /// Moves the page to this driver - the roster's highlight, and so the
    /// card and the Garage view. A search hiding them is cleared first.
    /// </summary>
    private void SelectDriver(string transponder)
    {
        // A driver, so the Garage view shows theirs rather than a group.
        var wasGroup = _garageGroup is not null;
        _garageGroup = null;

        bool InList() => _list.Items.Cast<ListViewItem>()
            .Any(i => string.Equals((string)i.Tag!, transponder, StringComparison.OrdinalIgnoreCase));

        if (!InList() && _txtSearch.Text.Length > 0)
        {
            _txtSearch.Text = string.Empty;
        }

        var item = _list.Items.Cast<ListViewItem>()
            .FirstOrDefault(i => string.Equals((string)i.Tag!, transponder, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            // Not on the roster any more: the Garage view back on the card's driver.
            if (wasGroup)
            {
                ShowDriverInViews(CardProfile()?.Transponder);
            }

            return;
        }

        _list.SelectedItems.Clear();
        item.Selected = true;
        item.Focused = true;

        // The roster is hidden in the Garage view, and a list that is not
        // on screen - or has never been, when the page opened on the Garage
        // view - may raise nothing for it: the page is moved here.
        if (_list.IsHandleCreated && _list.Visible)
        {
            item.EnsureVisible();
        }

        OnSelectionChanged();
    }

    /// <summary>
    /// A car, a transponder or a setup changed from one of the views: the
    /// roster, the header, the card and the other view all read the same
    /// stores, so they are drawn again.
    /// </summary>
    private void OnDriverThingsChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        RefreshList();
        RefreshHeader();
        UpdateCardExtent();
        _cardScroll.Invalidate();

        // The garage view's transponder list shows which car each is in,
        // and the car cards which transponders they carry: both follow.
        ShowDriverInViews(CardProfile()?.Transponder);
    }

    // ------------------------------------------------------------ the pieces

    /// <summary>
    /// The header over the driver's card: the driver painted on the left (a
    /// render callback, so the card's own chip and text helpers draw it),
    /// and the buttons against the right-hand edge - under the driver when
    /// the card is too narrow for both on one line. As tall as it needs at
    /// the width it is given (GetPreferredSize), for the card to lay out.
    /// </summary>
    private sealed class DriverHeader : Panel
    {
        private readonly Func<Graphics, Rectangle, bool, int> _render;
        private readonly ToolTip _tip = new();
        private bool _overAvatar;

        /// <summary>Where the picture is, as the last paint left it, for the click.</summary>
        public Func<Rectangle>? AvatarBounds { get; set; }

        public event Action? AvatarClicked;

        public DriverHeader(Func<Graphics, Rectangle, bool, int> render)
        {
            _render = render;

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            BackColor = AppTheme.Surface;
            Tag = AppTheme.KeepBackColor;
        }

        /// <summary>Drawn again, and laid out again by the card - the driver changed.</summary>
        public void Changed()
        {
            Invalidate();
            Parent?.PerformLayout();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Math.Max(Width, 1);

            // The screen's own context: measured at the DPI it is drawn at,
            // and without making a window before the page has one.
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return new Size(width, _render(g, new Rectangle(0, 0, Math.Max(1, width), 10000), false));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _render(e.Graphics, new Rectangle(0, 0, Math.Max(1, ClientSize.Width), ClientSize.Height), true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(AppTheme.Surface);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var over = AvatarBounds?.Invoke().Contains(e.Location) == true;

            if (over != _overAvatar)
            {
                _overAvatar = over;
                Cursor = over ? Cursors.Hand : Cursors.Default;
                _tip.SetToolTip(this, over ? "Click to change the photo" : string.Empty);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (e.Button == MouseButtons.Left && AvatarBounds?.Invoke().Contains(e.Location) == true)
            {
                AvatarClicked?.Invoke();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tip.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A row of three groups of buttons across the foot of the page, each
    /// under what it is about: the roster's on the left (under the roster),
    /// the driver's starting where the driver's card starts, and the
    /// club-wide ones against the right-hand edge. When the page is too
    /// narrow for that, the three follow one another and wrap, left to
    /// right, the right-hand group still against the edge.
    /// </summary>
    internal sealed class SpreadBar : Panel
    {
        private readonly ButtonBar _left;
        private readonly ButtonBar _middle;
        private readonly ButtonBar _right;
        private readonly Func<int> _middleStart;

        public SpreadBar(ButtonBar left, ButtonBar middle, ButtonBar right, Func<int> middleStart)
        {
            _left = left;
            _middle = middle;
            _right = right;
            _middleStart = middleStart;

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Tag = AppTheme.KeepBackColor;

            Controls.Add(left);
            Controls.Add(middle);
            Controls.Add(right);
        }

        private (Rectangle Left, Rectangle Middle, Rectangle Right) Arrange(int width)
        {
            var gap = UiLayout.SectionGap;
            var l = _left.GetPreferredSize(Size.Empty);
            var m = _middle.GetPreferredSize(Size.Empty);
            var r = _right.GetPreferredSize(Size.Empty);
            var start = Math.Max(_middleStart(), l.Width + gap);

            // All three on one line, each in its place.
            if (start + m.Width + gap + r.Width <= width)
            {
                var height = Math.Max(l.Height, Math.Max(m.Height, r.Height));
                return (new Rectangle(0, 0, l.Width, height),
                        new Rectangle(start, 0, m.Width, height),
                        new Rectangle(width - r.Width, 0, r.Width, height));
            }

            // Left and middle on the first line, the right-hand group under
            // them against the edge - or each on a line of its own.
            if (l.Width + gap + m.Width <= width)
            {
                var top = Math.Max(l.Height, m.Height);
                return (new Rectangle(0, 0, l.Width, top),
                        new Rectangle(l.Width + gap, 0, m.Width, top),
                        new Rectangle(Math.Max(0, width - r.Width), top + UiLayout.RowGap, Math.Min(width, r.Width), r.Height));
            }

            var lw = _left.GetPreferredSize(new Size(width, 0));
            var mw = _middle.GetPreferredSize(new Size(width, 0));
            var rw = _right.GetPreferredSize(new Size(width, 0));

            return (new Rectangle(0, 0, width, lw.Height),
                    new Rectangle(0, lw.Height + UiLayout.RowGap, width, mw.Height),
                    new Rectangle(0, lw.Height + mw.Height + UiLayout.RowGap * 2, width, rw.Height));
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4
                ? proposedSize.Width
                : _left.GetPreferredSize(Size.Empty).Width + _middle.GetPreferredSize(Size.Empty).Width + _right.GetPreferredSize(Size.Empty).Width + UiLayout.SectionGap * 2;

            var (l, m, r) = Arrange(width);
            return new Size(width, Math.Max(l.Bottom, Math.Max(m.Bottom, r.Bottom)));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            var (l, m, r) = Arrange(Math.Max(1, ClientSize.Width));

            if (_left.Bounds != l) _left.Bounds = l;
            if (_middle.Bounds != m) _middle.Bounds = m;
            if (_right.Bounds != r) _right.Bounds = r;
        }
    }

    /// <summary>
    /// Tabs drawn as words over a line, the chosen one in bold with the
    /// accent under it - the card's tabs. Arrow keys move between them when
    /// it has the focus, and Tab reaches it like any other control.
    /// </summary>
    private sealed class UnderlineTabs : Control
    {
        private readonly string[] _items;
        private int _selected;
        private int _hot = -1;

        public event Action? SelectedChanged;

        public UnderlineTabs(string[] items)
        {
            _items = items;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Tag = AppTheme.KeepBackColor;
            AccessibleRole = AccessibleRole.PageTabList;

            // As tall as its words need: asked by the table it sits in.
            AutoSize = true;
        }

        public int Selected
        {
            get => _selected;
            set
            {
                var next = Math.Clamp(value, 0, _items.Length - 1);

                if (next == _selected)
                {
                    return;
                }

                _selected = next;
                Invalidate();
                SelectedChanged?.Invoke();
            }
        }

        /// <summary>
        /// The room either side of each word: as much as at the normal text
        /// size when the tabs fit, less - down to a few pixels - when the
        /// card is too narrow, so the last tab is not cut off.
        /// </summary>
        private int Pad
        {
            get
            {
                var full = UiLayout.AtTextSize(14);
                var gaps = UiLayout.AtTextSize(6) * (_items.Length - 1);
                var words = _items.Sum(item => TextRenderer.MeasureText(item, Bold).Width);

                if (Width <= 0 || words + gaps + full * 2 * _items.Length <= Width)
                {
                    return full;
                }

                return Math.Max(3, (Width - words - gaps) / (2 * _items.Length));
            }
        }

        private Font Bold => AppTheme.BoldFont;

        /// <summary>Each tab's bounds, measured in the bold face so the chosen one does not grow.</summary>
        private List<Rectangle> Bounds_()
        {
            var list = new List<Rectangle>();
            var x = 0;
            var height = Height;
            var pad = Pad;

            foreach (var item in _items)
            {
                var width = TextRenderer.MeasureText(item, Bold).Width + pad * 2;
                list.Add(new Rectangle(x, 0, width, height));
                x += width + UiLayout.AtTextSize(6);
            }

            return list;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var height = Bold.Height + UiLayout.AtTextSize(18);
            var width = _items.Sum(item => TextRenderer.MeasureText(item, Bold).Width + UiLayout.AtTextSize(14) * 2 + UiLayout.AtTextSize(6));

            return new Size(proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : width, height);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Height = GetPreferredSize(Size.Empty).Height;
            Parent?.PerformLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Height = GetPreferredSize(Size.Empty).Height;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(AppTheme.Surface);

            var bounds = Bounds_();
            var line = Math.Max(2, UiLayout.AtTextSize(3));

            using (var rule = new Pen(AppTheme.Border, 1f))
            {
                g.DrawLine(rule, 0, Height - 1, Width, Height - 1);
            }

            for (var i = 0; i < _items.Length; i++)
            {
                var chosen = i == _selected;
                var colour = chosen || i == _hot ? AppTheme.TextDark : AppTheme.TextMuted;
                var text = new Rectangle(bounds[i].X, 0, bounds[i].Width, Height - line);

                TextRenderer.DrawText(g, _items[i], chosen ? Bold : AppTheme.BodyFont, text, colour,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                if (chosen)
                {
                    using var accent = new SolidBrush(AppTheme.Accent);
                    g.FillRectangle(accent, bounds[i].X, Height - line, bounds[i].Width, line);

                    if (Focused && ShowFocusCues)
                    {
                        var focus = Rectangle.Inflate(text, -3, -3);
                        ControlPaint.DrawFocusRectangle(g, focus, AppTheme.TextDark, AppTheme.Surface);
                    }
                }
            }
        }

        private int HitTest(Point point) => Bounds_().FindIndex(b => b.Contains(point));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hot = HitTest(e.Location);

            if (hot != _hot)
            {
                _hot = hot;
                Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left && HitTest(e.Location) is var hit and >= 0)
            {
                Focus();
                Selected = hit;
            }
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.KeyCode)
            {
                case Keys.Left:
                    Selected = (_selected + _items.Length - 1) % _items.Length;
                    e.Handled = true;
                    break;
                case Keys.Right:
                    Selected = (_selected + 1) % _items.Length;
                    e.Handled = true;
                    break;
                case Keys.Home:
                    Selected = 0;
                    e.Handled = true;
                    break;
                case Keys.End:
                    Selected = _items.Length - 1;
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }
}
