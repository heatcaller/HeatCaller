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
}

/// <summary>
/// The Driver Profiles page's two views, and the tabs on the driver's card.
///
/// Hub: the roster on the left and the driver's card on the right, with
/// tabs for Profile, Garage, Transponders, Setups and Stats. Garage: one
/// driver picked at the top and their cars big, with their transponders
/// beside them. A Hub | Garage switch moves between the two, and the page
/// opens on the one used last (AppSettings.DriversPageView).
///
/// Both views show the same driver - picking one in either moves the
/// other - and the same stores, through the same dialogs and undo as the
/// Garage page.
/// </summary>
public sealed partial class ProfileManagerControl
{
    /// <summary>The card's tabs.</summary>
    private enum CardTab
    {
        Profile,
        Garage,
        Transponders,
        Setups,
        Stats,
    }

    private const string HubView = "Hub";
    private const string GarageView = "Garage";

    /// <summary>Null on a page built without them: then there are no views or tabs, and the card is the whole driver, as before.</summary>
    private readonly DriverGarageSources? _garageSources;

    private CardTab _cardTab = CardTab.Profile;
    private readonly Dictionary<CardTab, Button> _tabButtons = new();

    /// <summary>The card's body: the painted card, or the garage, transponders or setups.</summary>
    private Panel? _cardBody;

    private GarageControl? _hubGarage;
    private DriverTransponderPanel? _hubTransponders;
    private DriverSetupsPanel? _hubSetups;

    private Button? _btnHub;
    private Button? _btnGarageView;

    /// <summary>The Hub view: the roster and the card side by side.</summary>
    private Beside? _band;

    /// <summary>The Garage view.</summary>
    private Control? _garageView;

    private readonly ComboBox _cmbGarageDriver = new();
    private GarageControl? _bigGarage;
    private Beside? _garageBeside;
    private DriverTransponderPanel? _bigTransponders;

    /// <summary>Set while the Garage view's driver picker is being filled or moved to follow the roster, so that changes nothing.</summary>
    private bool _syncingPicker;

    /// <summary>The view asked for - kept apart from Visible, which reads false while the page itself is hidden.</summary>
    private bool _garageViewWanted;

    /// <summary>Whether the painted card shows the driver's record (Stats tab, or no tabs at all).</summary>
    private bool ShowsStats => _garageSources is null || _cardTab == CardTab.Stats;

    /// <summary>Whether the painted card shows who they are (Profile tab, or no tabs at all).</summary>
    private bool ShowsProfile => _garageSources is null || _cardTab == CardTab.Profile;

    // ------------------------------------------------------------ the card

    private ButtonBar BuildCardTabs()
    {
        var bar = new ButtonBar(tight: true);

        foreach (var tab in Enum.GetValues<CardTab>())
        {
            var button = new Button { Text = tab.ToString() };
            button.Click += (_, _) => SelectCardTab(tab);
            _tabButtons[tab] = button;
            bar.With(button);
        }

        return bar;
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

        _hubTransponders = new DriverTransponderPanel(_store, sources.Cars, _undo) { Visible = false };
        _hubTransponders.Changed += OnDriverThingsChanged;
        _cardBody.Controls.Add(_hubTransponders);

        _hubSetups = new DriverSetupsPanel(_store, sources.Cars, sources.Setups, sources.Templates, car => sources.OpenCar(car, this)) { Visible = false };
        _hubSetups.Changed += OnDriverThingsChanged;
        _cardBody.Controls.Add(_hubSetups);

        StyleSegments();
        return _cardBody;
    }

    private void SelectCardTab(CardTab tab)
    {
        if (_cardBody is null)
        {
            return;
        }

        _cardTab = tab;

        var painted = tab is CardTab.Profile or CardTab.Stats;
        _cardScroll.Visible = painted;
        _hubGarage!.Visible = tab == CardTab.Garage;
        _hubTransponders!.Visible = tab == CardTab.Transponders;
        _hubSetups!.Visible = tab == CardTab.Setups;

        StyleSegments();

        if (painted)
        {
            _cardScroll.AutoScrollPosition = Point.Empty;
            UpdateCardExtent();
            _cardScroll.Invalidate();
        }

        ShowDriverInViews(CardProfile()?.Transponder);
    }

    // ----------------------------------------------------------- the views

    private ButtonBar BuildViewSwitch()
    {
        _btnHub = new Button { Text = HubView };
        _btnHub.Click += (_, _) => SelectView(garage: false);

        _btnGarageView = new Button { Text = GarageView };
        _btnGarageView.Click += (_, _) => SelectView(garage: true);

        return new ButtonBar(tight: true).With(_btnHub, _btnGarageView);
    }

    private Panel BuildViews(Beside band)
    {
        _band = band;
        _garageView = BuildGarageView();

        var views = new Panel { Tag = AppTheme.KeepBackColor };

        band.Dock = DockStyle.Fill;
        _garageView.Dock = DockStyle.Fill;

        views.Controls.Add(band);
        views.Controls.Add(_garageView);

        // The view used last - the Hub on a new install.
        _garageViewWanted = string.Equals(_settings?.DriversPageView, GarageView, StringComparison.OrdinalIgnoreCase);
        band.Visible = !_garageViewWanted;
        _garageView.Visible = _garageViewWanted;

        StyleSegments();
        return views;
    }

    /// <summary>The least height the views can do with at this width: the Hub's, or a garage wall's.</summary>
    private int ViewsLeastHeight(int width)
    {
        if (!_garageViewWanted)
        {
            return _band?.LeastHeight(width) ?? UiLayout.AtTextSize(360);
        }

        // The picker's row, and under it the garage beside its transponders.
        var picker = Math.Max(_cmbGarageDriver.PreferredHeight, _cmbGarageDriver.Height) + UiLayout.RowGap;
        return picker + (_garageBeside?.LeastHeight(width) ?? UiLayout.AtTextSize(360));
    }

    private Control BuildGarageView()
    {
        var sources = _garageSources!;

        _cmbGarageDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbGarageDriver.SelectionChangeCommitted += (_, _) =>
        {
            if (!_syncingPicker && _cmbGarageDriver.SelectedItem is DriverChoice choice)
            {
                SelectDriver(choice.Transponder);
            }
        };

        var pickRow = new FieldRow();
        pickRow.Add(new Label { Text = "Driver:" }, _cmbGarageDriver, () => UiLayout.AtTextSize(260));

        _bigGarage = sources.MakeGarage();
        _bigGarage.Changed += OnDriverThingsChanged;

        _bigTransponders = new DriverTransponderPanel(_store, sources.Cars, _undo);
        _bigTransponders.Changed += OnDriverThingsChanged;

        var transponders = new Section("Transponders");
        transponders.AddFill(_bigTransponders, () => UiLayout.AtTextSize(220));

        var beside = new Beside(() => UiLayout.AtTextSize(300));
        _garageBeside = beside;
        beside.AddFill(_bigGarage, _ => UiLayout.AtTextSize(300));
        beside.AddSideFill(transponders, transponders.LeastHeight, gapBelow: 0);
        beside.Dock = DockStyle.Fill;

        var table = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Tag = AppTheme.KeepBackColor,
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        pickRow.Dock = DockStyle.Fill;
        pickRow.Margin = new Padding(0, 0, 0, UiLayout.RowGap);
        table.Controls.Add(pickRow, 0, 0);
        table.Controls.Add(beside, 0, 1);

        return table;
    }

    /// <summary>Hub or Garage, remembered for next time.</summary>
    private void SelectView(bool garage)
    {
        if (_band is null || _garageView is null)
        {
            return;
        }

        _garageViewWanted = garage;
        _band.Visible = !garage;
        _garageView.Visible = garage;
        StyleSegments();

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

    /// <summary>The chosen tab and view as the accent buttons, the rest plain.</summary>
    private void StyleSegments()
    {
        foreach (var (tab, button) in _tabButtons)
        {
            AppTheme.StyleButton(button, tab == _cardTab ? AppTheme.ButtonKind.Primary : AppTheme.ButtonKind.Secondary);
        }

        if (_btnHub is not null && _btnGarageView is not null)
        {
            AppTheme.StyleButton(_btnHub, _garageViewWanted ? AppTheme.ButtonKind.Secondary : AppTheme.ButtonKind.Primary);
            AppTheme.StyleButton(_btnGarageView, _garageViewWanted ? AppTheme.ButtonKind.Primary : AppTheme.ButtonKind.Secondary);
        }
    }

    // ------------------------------------------------------ the driver shown

    /// <summary>One driver in the Garage view's picker.</summary>
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

            foreach (var profile in _store.All.OrderBy(p => p.DriverName, StringComparer.CurrentCultureIgnoreCase))
            {
                var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();
                var number = string.IsNullOrWhiteSpace(profile.RaceNumber) ? string.Empty : $"  #{profile.RaceNumber.Trim()}";
                _cmbGarageDriver.Items.Add(new DriverChoice(profile.Transponder, name + number));
            }

            _cmbGarageDriver.EndUpdate();
            PointPickerAt(CardProfile()?.Transponder);
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
    /// card and both views. A search hiding them is cleared first.
    /// </summary>
    private void SelectDriver(string transponder)
    {
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
            return;
        }

        _list.SelectedItems.Clear();
        item.Selected = true;
        item.Focused = true;

        if (_list.IsHandleCreated)
        {
            item.EnsureVisible();
        }
        else
        {
            // The roster has never been on screen (the page opened on the
            // Garage view): a list without its window keeps the selection
            // but raises no event for it, so the page is moved here.
            OnSelectionChanged();
        }
    }

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

            try
            {
                PointPickerAt(transponder);
            }
            finally
            {
                _syncingPicker = false;
            }

            _bigGarage?.ShowDriver(transponder);
            _bigTransponders?.ShowDriver(transponder);
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
        }
    }

    /// <summary>
    /// A car, a transponder or a setup changed from one of the views: the
    /// roster's car column, the card and the other view all read the same
    /// stores, so they are drawn again.
    /// </summary>
    private void OnDriverThingsChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        RefreshList();
        UpdateCardExtent();
        _cardScroll.Invalidate();

        // The garage view's transponder list shows which car each is in,
        // and the car cards which transponders they carry: both follow.
        ShowDriverInViews(CardProfile()?.Transponder);
    }
}
