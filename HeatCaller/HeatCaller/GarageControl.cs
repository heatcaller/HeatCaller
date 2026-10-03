namespace HeatCaller;

/// <summary>
/// The Garage: the club's cars, shown as cars.
///
/// A wall of photographs banded by class, not a table. The first version
/// of this page was a grid, and a grid is the wrong shape for the subject:
/// people know their cars by looking at them, a club's garage is the most
/// photogenic thing in the app, and "which one is the red one" is a
/// question a row of text cannot answer. The list view it replaced said
/// everything and showed nothing.
///
/// Opening a car opens the car - see CarDetailForm - which is where its
/// photographs, its details and its setups live. That is also where the
/// old Tuning page ended up: a setup belongs to a car, so it is on the
/// car, rather than on a second page that has to be kept pointing at the
/// same row.
/// </summary>
public sealed class GarageControl : UserControl
{
    private readonly CarStore _cars;
    private readonly ChassisTypeStore _chassisTypes;
    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _raceClasses;
    private readonly SetupLogStore _setups;
    private readonly SessionHistoryStore _history;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly ChassisTemplateStore _chassisTemplates;
    private readonly RaceEventStore? _events;
    private readonly UndoService _undo;

    /// <summary>
    /// True for the garage shown on a driver's page (Driver Hub): one
    /// driver's cars, no Show filter, no club-wide lists, and each card
    /// saying whether it is their active car and which transponder is
    /// fitted to it. The same cards, dialogs and undo as the Garage page -
    /// one set of cars, shown two ways.
    /// </summary>
    private readonly bool _forOneDriver;

    /// <summary>Raised after anything here changed a car or a driver - for a host showing more of the driver than this.</summary>
    public event Action? Changed;

    private readonly ComboBox _cmbOwnerFilter = new();
    private readonly CheckBox _chkRetired = new ThemedCheckBox();

    /// <summary>Set while ShowDriver refills and moves the hidden owner filter, so the wall is built once, after.</summary>
    private bool _quietFilter;

    /// <summary>Add a Car - on a driver's page, only while there is a driver to add it for.</summary>
    private Button? _btnAddCar;
    private readonly Label _lblSummary = new();
    private readonly Panel _wall = new();
    private readonly Label _lblEmpty = new();

    /// <summary>One class's heading and the cards under it, kept so a resize can reflow without rebuilding.</summary>
    private readonly List<(Label Band, List<CarCard> Cards)> _groups = new();

    private const int WallPad = 12;
    private const int CardGap = 12;

    /// <summary>A class's heading: a line of the bold face and a little room - it was 28px whatever the text size, and a larger one cut the heading off.</summary>
    private static int BandHeight => AppTheme.BoldFont.Height + UiLayout.AtTextSize(6);

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The owner picker's width at the normal text size - never narrower, and as wide as the longest name.</summary>
    private const int OwnerPickerWidth = 240;

    /// <summary>The owner picker's width as last measured - see Pieces.PickerWidth. Forgotten when the list is refilled.</summary>
    private (Font Font, int Count, int Width)? _ownerWidth;

    /// <summary>The filter's line - held so a refilled list can re-measure it.</summary>
    private FieldRow? _filterRow;

    private const string EveryoneKey = "\u0000everyone";
    private const string ClubKey = "\u0000club";

    /// <summary>Cars with no class at all. A real state - a club car nobody has assigned, or a shelf queen.</summary>
    private const string Unassigned = "Unassigned";

    private sealed record OwnerChoice(string Transponder, string Caption)
    {
        public override string ToString() => Caption;
    }

    public GarageControl(
        CarStore cars,
        ChassisTypeStore chassisTypes,
        ProfileStore profiles,
        RaceClassStore raceClasses,
        SetupLogStore setups,
        SessionHistoryStore history,
        TrackLayoutStore trackLayouts,
        ChassisTemplateStore chassisTemplates,
        UndoService undo,
        RaceEventStore? events = null,
        bool forOneDriver = false)
    {
        _forOneDriver = forOneDriver;
        _cars = cars;
        _chassisTypes = chassisTypes;
        _profiles = profiles;
        _raceClasses = raceClasses;
        _setups = setups;
        _history = history;
        _trackLayouts = trackLayouts;
        _chassisTemplates = chassisTemplates;
        _undo = undo;
        _events = events;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. No MinimumSize: the 920x560 floor was honoured
        // over the size the page was given. A short window scrolls the page
        // instead (FillPage), and the wall keeps its own scrollbar.
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

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>The race day a loan would be for, or null when none is open.</summary>
    private string? CurrentEventId => _events?.Active is { IsLegacy: false } day ? day.Id : null;

    /// <summary>
    /// The intro, the filter along a line with the count at the end of it,
    /// the wall taking the height that is left - scrolling inside itself -
    /// and the buttons under it. On the layout pieces (LayoutPieces.cs).
    /// The wall is on the page itself rather than in a card: the cars are
    /// cards.
    ///
    /// It used to be placed by hand over a 920x560 floor, the intro capped
    /// at 880px and the filter's line at remembered x positions, in faces
    /// set once.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "The club's cars. Add a photograph and a car starts looking like itself - until then it gets a plate "
                 + "with its number on. Click a car to open it: its photographs, what it is, and every setup logged for it.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _cmbOwnerFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbOwnerFilter.SelectedIndexChanged += (_, _) =>
        {
            if (!_quietFilter)
            {
                BuildWall();
            }
        };

        _chkRetired.Text = "Include retired";
        _chkRetired.AutoSize = true;
        _chkRetired.CheckedChanged += (_, _) => BuildWall();

        // How many cars, at the end of the line - or on a line of its own
        // when it no longer fits there (FieldRow).
        _lblSummary.AutoSize = false;
        _lblSummary.AutoEllipsis = true;
        _lblSummary.ForeColor = AppTheme.TextMuted;

        var filterRow = new FieldRow();

        filterRow.Add(new Label { Text = "Show:" }, _cmbOwnerFilter, OwnerWidth);
        filterRow.Add(null, _chkRetired);
        filterRow.AddLast(null, _lblSummary, () => _lblSummary.GetPreferredSize(Size.Empty).Width);
        _filterRow = filterRow;

        // The wall. AutoScroll rather than a fixed grid of pages: a club
        // with sixty cars scrolls, and a club with four does not get four
        // cars stranded at the top of an empty page of tabs.
        _wall.AutoScroll = true;
        _wall.BackColor = AppTheme.Background;
        _wall.Resize += (_, _) => LayoutWall();

        // Worded as the wall is built - see EmptyWallText.
        _lblEmpty.AutoSize = true;
        _lblEmpty.ForeColor = AppTheme.TextMuted;
        _lblEmpty.Location = new Point(WallPad, WallPad);
        _lblEmpty.Visible = false;

        _wall.Controls.Add(_lblEmpty);

        // The class headings are in the bold face at the text size in use,
        // and the cards are sized at it (CarCard.CardWidth), so a new one
        // lays the wall out again.
        _textSize.Keep(LayoutWall);

        var btnAdd = new Button { Text = "Add a Car..." };
        btnAdd.Click += (_, _) => AddCar();
        _btnAddCar = btnAdd;

        var btnChassis = new Button { Text = "Chassis Types..." };
        btnChassis.Click += (_, _) => ManageChassisTypes();

        var btnSheets = new Button { Text = "Chassis Sheets..." };
        btnSheets.Click += (_, _) => ManageChassisSheets();

        var page = new FillPage();

        // On a driver's page the driver is chosen there: no intro, no Show
        // filter (it is kept, set by ShowDriver, and hidden), and no
        // club-wide lists - those stay on the Garage page.
        if (!_forOneDriver)
        {
            page.Add(lblIntro);
        }

        page.Add(filterRow, gapBelow: UiLayout.RowGap);
        filterRow.Visible = !_forOneDriver;

        // At least a row of cars under a heading - below that the page
        // scrolls. Asked each time the page works out its range, so it is a
        // card at the text size in use; a larger one only makes the page's
        // least taller, and a window shorter than that scrolls the page.
        page.AddFill(_wall, _ => WallPad * 2 + BandHeight + 6 + CarCard.CardHeight, gapBelow: UiLayout.RowGap);
        page.Add(_forOneDriver ? new ButtonBar().With(btnAdd) : new ButtonBar().With(btnAdd, btnChassis, btnSheets), gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// What the wall says when it has no cars to show. "No cars yet" only
    /// when there are none: it used to say that whatever emptied the wall,
    /// so a driver with no cars of their own, or a garage of retired ones,
    /// read as an empty garage next to a count of the cars in it.
    /// </summary>
    private string EmptyWallText() =>
        _forOneDriver
            ? "No cars yet. Click Add a Car, and it appears here."
            : _cars.All.Count == 0
            ? "No cars yet. Add one, and it appears here under its class."
            : _chkRetired.Checked
                ? "No cars to show - pick another in Show."
                : "No cars to show - pick another in Show, or tick Include retired.";

    /// <summary>The owner picker: as wide as the longest name, and never under its width at the normal text size.</summary>
    private int OwnerWidth() =>
        Math.Max(UiLayout.AtTextSize(OwnerPickerWidth), Pieces.PickerWidth(_cmbOwnerFilter, ref _ownerWidth));

    /// <summary>Call when this page becomes visible - drivers, classes and the open race day all change elsewhere.</summary>
    public void RefreshAll()
    {
        RefreshOwnerFilter();
        BuildWall();
    }

    private void RefreshOwnerFilter()
    {
        var previous = (_cmbOwnerFilter.SelectedItem as OwnerChoice)?.Transponder;

        _cmbOwnerFilter.BeginUpdate();
        _cmbOwnerFilter.Items.Clear();

        _cmbOwnerFilter.Items.Add(new OwnerChoice(EveryoneKey, "Everyone's cars"));
        _cmbOwnerFilter.Items.Add(new OwnerChoice(ClubKey, "The club's own cars"));

        foreach (var profile in _profiles.All.OrderBy(p => p.DriverName, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;
            _cmbOwnerFilter.Items.Add(new OwnerChoice(profile.Transponder, name));
        }

        _cmbOwnerFilter.EndUpdate();

        // The longest name may have come or gone: measured again, and the
        // line with it.
        _ownerWidth = null;
        _filterRow?.PerformLayout();

        var match = _cmbOwnerFilter.Items
            .Cast<OwnerChoice>()
            .FirstOrDefault(c => string.Equals(c.Transponder, previous, StringComparison.OrdinalIgnoreCase));

        _cmbOwnerFilter.SelectedItem = match ?? _cmbOwnerFilter.Items[0];
    }

    private IReadOnlyList<Car> VisibleCars()
    {
        var chosen = (_cmbOwnerFilter.SelectedItem as OwnerChoice)?.Transponder ?? EveryoneKey;

        // A driver's page shows nobody's cars until it is told whose.
        if (_forOneDriver && chosen is EveryoneKey or ClubKey)
        {
            return Array.Empty<Car>();
        }

        var cars = chosen switch
        {
            EveryoneKey => _cars.All,
            ClubKey => _cars.ClubCars,
            _ => _profiles.Get(chosen) is { } owner ? _cars.OwnedBy(owner) : _cars.OwnedBy(chosen),
        };

        // A driver's page shows their retired cars too, marked Retired on
        // the card: no longer used, but still theirs and still openable.
        return _forOneDriver || _chkRetired.Checked ? cars : cars.Where(c => !c.Retired).ToList();
    }

    /// <summary>
    /// Class headings in the club's own order, with anything unassigned
    /// last.
    ///
    /// The club's order rather than alphabetical: a club that lists Stock
    /// before Modified on every other page should not find them the other
    /// way round here. A class nobody has a car in is left out - an empty
    /// heading says nothing.
    /// </summary>
    private List<string> GroupOrder(IReadOnlyList<Car> cars)
    {
        var present = cars
            .Select(c => string.IsNullOrWhiteSpace(c.ClassName) ? Unassigned : c.ClassName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ordered = _raceClasses.All
            .Select(c => c.Name)
            .Where(name => present.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Classes on a car that the club has since deleted or renamed. Kept
        // rather than dropped: the car is still there and has to go
        // somewhere a person can find it.
        ordered.AddRange(present
            .Where(p => !string.Equals(p, Unassigned, StringComparison.OrdinalIgnoreCase))
            .Where(p => !ordered.Contains(p, StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase));

        if (present.Contains(Unassigned, StringComparer.OrdinalIgnoreCase))
        {
            ordered.Add(Unassigned);
        }

        return ordered;
    }

    private void BuildWall()
    {
        _wall.SuspendLayout();

        foreach (var (band, cards) in _groups)
        {
            band.Dispose();

            foreach (var card in cards)
            {
                // Not disposed with the card it is attached to.
                var menu = card.ContextMenuStrip;
                card.ContextMenuStrip = null;
                menu?.Dispose();
                card.Dispose();
            }
        }

        _groups.Clear();

        var cars = VisibleCars();
        var eventId = CurrentEventId;

        foreach (var className in GroupOrder(cars))
        {
            var inClass = cars
                .Where(c => string.Equals(
                    string.IsNullOrWhiteSpace(c.ClassName) ? Unassigned : c.ClassName.Trim(),
                    className,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Retired)
                .ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (inClass.Count == 0)
            {
                continue;
            }

            var isUnassigned = string.Equals(className, Unassigned, StringComparison.OrdinalIgnoreCase);

            var band = new Label
            {
                Text = AppTheme.Spaced(className.ToUpperInvariant()) + $"   {inClass.Count}",
                AutoSize = false,
                Height = BandHeight,
                TextAlign = ContentAlignment.MiddleLeft,

                // Given the face again as the wall is laid out, at the text
                // size in use - see LayoutWall.
                Font = AppTheme.BoldFont,

                // The class's own colour, the same one the programme's
                // class column and the heat sheet band use - a club knows
                // its classes by these. Unassigned is not a class and does
                // not get one.
                ForeColor = isUnassigned
                    ? AppTheme.TextMuted
                    : RaceClass.OnScreenColourFor(
                        _raceClasses.All.FirstOrDefault(c =>
                            string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase)),
                        className),
            };

            _wall.Controls.Add(band);

            var cards = new List<CarCard>();

            foreach (var car in inClass)
            {
                var card = new CarCard(
                    car,
                    _forOneDriver ? DriverLine(car, eventId) : WhoHasIt(car, eventId),
                    _chassisTypes.NameOf(car.ChassisTypeId));

                card.Opened += OpenCar;

                if (_forOneDriver)
                {
                    card.ContextMenuStrip = CarMenu(card.Car);
                }

                _wall.Controls.Add(card);
                cards.Add(card);
            }

            _groups.Add((band, cards));
        }

        var total = _cars.InUse.Count;
        var club = _cars.ClubCars.Count(c => !c.Retired);

        // Nothing at the end of the line while the garage is empty: the
        // wall says so, and what to do about it. It used to say "No cars
        // yet." here as well, a line above the wall saying it again (layout
        // pictures, 29 Sep).
        _lblSummary.Text = _cars.All.Count == 0
            ? string.Empty
            : total == 0
                ? "Every car in the garage is retired."
                : $"{Plural.Of(total, "car")} in the garage" + (club > 0 ? $", {club} the club's own." : ".");

        _lblEmpty.Text = EmptyWallText();
        _lblEmpty.Visible = _groups.Count == 0;

        // Back to the top when there is nothing to show: the label sits at
        // the top of the view, and a wall scrolled down before a filter
        // emptied it kept its scrolling range of blank space above it.
        if (_groups.Count == 0)
        {
            _wall.AutoScrollPosition = Point.Empty;
        }
        _lblEmpty.BringToFront();

        _wall.ResumeLayout();
        LayoutWall();
    }

    /// <summary>
    /// Whose car it is tonight, for the line under the name: its owner,
    /// or whoever has it on loan for the open race day.
    /// </summary>
    private string WhoHasIt(Car car, string? eventId)
    {
        var driver = car.DriverFor(eventId);

        if (string.IsNullOrWhiteSpace(driver))
        {
            return car.IsClubCar ? "The club" : string.Empty;
        }

        var borrowed = !string.Equals(driver, car.OwnerTransponder, StringComparison.OrdinalIgnoreCase);

        return borrowed ? $"{NameOf(driver)} (on loan)" : NameOf(driver);
    }

    /// <summary>
    /// Flows the cards into whatever width the page has.
    ///
    /// Run on every resize rather than laid out once, because the number of
    /// cards per row is a property of the window and a club plugging the
    /// laptop into the clubhouse screen halves or doubles it. Positions
    /// only - the controls themselves are not rebuilt, so this is cheap
    /// enough to run while a window is being dragged.
    /// </summary>
    /// <summary>
    /// True while LayoutWall is running.
    ///
    /// Moving the cards can push the wall's contents past its height, which
    /// makes AutoScroll add a vertical scrollbar, which changes ClientSize,
    /// which raises Resize - which is what calls this. Without the guard
    /// that is a layout that can chase its own tail on the one resize where
    /// the scrollbar appears or disappears.
    /// </summary>
    private bool _layingOut;

    private void LayoutWall()
    {
        if (_groups.Count == 0 || _layingOut)
        {
            return;
        }

        _layingOut = true;
        _wall.SuspendLayout();

        // The cards' size at the text size in use (see CarCard.CardWidth).
        // Read once here, for the whole wall; this runs again when the text
        // size changes (TextSize.Keep in BuildLayout).
        var cardWidth = CarCard.CardWidth;
        var cardHeight = CarCard.CardHeight;

        // ClientSize, not Width: once the cards overflow, AutoScroll puts a
        // vertical scrollbar inside the panel and the usable width drops by
        // its thickness. Laying out against Width instead leaves the last
        // column of every row clipped behind the bar.
        var usable = Math.Max(cardWidth, _wall.ClientSize.Width - WallPad * 2);
        var perRow = Math.Max(1, (usable + CardGap) / (cardWidth + CardGap));

        var y = WallPad;

        // Scrolled: the top of the wall's contents is above the panel by
        // however far - positions are the panel's, not the contents'.
        var top = _wall.AutoScrollPosition.Y;
        var left = _wall.AutoScrollPosition.X;
        var bandHeight = BandHeight;

        foreach (var (band, cards) in _groups)
        {
            if (!Equals(band.Font, AppTheme.BoldFont))
            {
                band.Font = AppTheme.BoldFont;
            }

            band.Bounds = new Rectangle(left + WallPad, top + y, usable, bandHeight);

            y += bandHeight + 6;

            for (var i = 0; i < cards.Count; i++)
            {
                var column = i % perRow;
                var row = i / perRow;

                // Sized as well as placed: a card is built at the text size
                // in use then, and this is what brings it to a new one.
                cards[i].Bounds = new Rectangle(
                    left + WallPad + column * (cardWidth + CardGap),
                    top + y + row * (cardHeight + CardGap),
                    cardWidth,
                    cardHeight);
            }

            var rows = (cards.Count + perRow - 1) / perRow;

            y += rows * (cardHeight + CardGap) + UiLayout.SectionGap;
        }

        _wall.ResumeLayout();
        _layingOut = false;
    }

    private string NameOf(string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return string.Empty;
        }

        var profile = _profiles.Get(transponder);

        return profile is null || string.IsNullOrWhiteSpace(profile.DriverName)
            ? transponder
            : profile.DriverName;
    }

    // ------------------------------------------------------------- cars

    private void OpenCar(Car car)
    {
        using var form = new CarDetailForm(
            car, _cars, _chassisTypes, _profiles, _raceClasses, _setups, _history,
            _trackLayouts, _chassisTemplates, _undo, _events);

        form.ShowDialog(this);

        // Everything the detail window does - edits, photos, loans,
        // retiring, deleting - is written through the stores by the time it
        // closes, so the wall simply re-reads rather than being told what
        // changed. One path, and no chance of the two disagreeing.
        RefreshAll();
        Changed?.Invoke();
    }

    private void AddCar()
    {
        var chosenOwner = (_cmbOwnerFilter.SelectedItem as OwnerChoice)?.Transponder;
        var owner = chosenOwner is null or EveryoneKey or ClubKey ? string.Empty : chosenOwner;

        // A driver's page adds cars for its driver, never for the club.
        if (_forOneDriver && owner.Length == 0)
        {
            return;
        }

        var car = new Car
        {
            // Opened on whoever the list is filtered to, because that is
            // almost always who the car is being added for.
            OwnerTransponder = owner,

            // And in their class, so a car lands under a real heading
            // rather than in Unassigned for somebody to move by hand.
            ClassName = owner.Length == 0 ? string.Empty : _profiles.Get(owner)?.ClassName?.Trim() ?? string.Empty,
        };

        using var form = new CarForm(car, _chassisTypes, _profiles, _raceClasses, isNew: true);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var saved = form.Result;

        // Adding the car can also name it on the driver's profile (see
        // SyncActiveCarString), so undoing the add has to take that back
        // too - otherwise the car leaves the Garage while the driver is
        // still announced in it. The undo is captured before anything is
        // written, as everywhere else, so the profile half is filled in
        // once we know whether the profile was touched at all.
        Action? undoProfile = null;

        _undo.Capture($"Add {saved.DisplayName} to the garage", () =>
        {
            // The profile first, so that when the car goes the driver is
            // already back on the car they had rather than moved to another.
            undoProfile?.Invoke();
            _cars.Delete(saved.Id);

            if (!IsDisposed)
            {
                RefreshAll();
                Changed?.Invoke();
            }
        });

        _cars.Upsert(saved);
        undoProfile = SyncActiveCarString(saved);
        RefreshAll();
        Changed?.Invoke();
    }

    /// <summary>
    /// Keeps the driver profile's CarModel in step with the garage.
    ///
    /// CarModel is read in dozens of places as "what is this driver in
    /// right now", and announcements, results and records all use it. The
    /// Garage does not replace it - it fills it in: a driver whose profile
    /// names no car gets this one, and a car already named there is left
    /// exactly as it is. Nothing silently changes what a driver is
    /// announced as.
    ///
    /// Returns what puts the profile back, or null when it was not changed,
    /// for AddCar's undo. The reversal only acts while the profile still
    /// names this car: if somebody has picked a different car for the
    /// driver since, that choice is theirs and Undo leaves it alone. It
    /// also only takes the name off the driver's car list when this call
    /// put it there - a name they already had stays.
    /// </summary>
    private Action? SyncActiveCarString(Car car)
    {
        if (car.IsClubCar || car.Retired || _profiles.Get(car.OwnerTransponder) is not { } profile)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.CarModel))
        {
            return null;
        }

        var previousModel = profile.CarModel;
        var name = car.DisplayName.Trim();

        if (!profile.SetActiveCar(car.DisplayName))
        {
            return null;
        }

        _profiles.Upsert(profile);

        // Keyed on the profile's own main transponder rather than the car's
        // owner field, which may be an alternate - and looked up again at
        // undo time, because the store may hold a different object by then.
        var transponder = profile.Transponder;

        return () =>
        {
            if (_profiles.Get(transponder) is not { } current
                || !string.Equals(current.CarModel, name, StringComparison.Ordinal))
            {
                return;
            }

            // The car's name leaves their list with the car itself (the
            // Garage's own change reaches the profile - GarageSync.SyncNames).
            current.CarModel = previousModel;
            _profiles.Upsert(current);
        };
    }

    // ------------------------------------------------- a driver's page

    /// <summary>
    /// Shows one driver's cars - for the garage on a driver's page. Null or
    /// a driver not on file shows none.
    /// </summary>
    public void ShowDriver(string? transponder)
    {
        _quietFilter = true;

        try
        {
            RefreshOwnerFilter();

            var match = _cmbOwnerFilter.Items
                .Cast<OwnerChoice>()
                .FirstOrDefault(c => string.Equals(c.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

            // Everyone, which a driver's page shows as no cars, when there
            // is nobody to show.
            _cmbOwnerFilter.SelectedItem = match ?? _cmbOwnerFilter.Items[0];

            if (_btnAddCar is not null)
            {
                _btnAddCar.Enabled = match is not null;
            }
        }
        finally
        {
            _quietFilter = false;
        }

        BuildWall();
    }

    /// <summary>
    /// The line under a car's name on a driver's page: whether it is the car
    /// they are announced and recorded in, the transponders fitted to it,
    /// and who has it tonight when that is not them.
    /// </summary>
    private string DriverLine(Car car, string? eventId)
    {
        var parts = new List<string>();
        var owner = _profiles.Get(car.OwnerTransponder);

        if (owner is not null && IsActiveCar(owner, car))
        {
            parts.Add("Active");
        }

        if (owner is not null)
        {
            parts.AddRange(owner.Transponders
                .Where(t => t is not null && !t.Retired && string.Equals(t.CarId, car.Id, StringComparison.Ordinal))
                .Select(t => t.Id));
        }

        var driver = car.DriverFor(eventId);

        if (!string.IsNullOrWhiteSpace(driver)
            && !string.Equals(driver, car.OwnerTransponder, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"Lent to {NameOf(driver)}");
        }

        if (car.Retired)
        {
            parts.Add("Retired");
        }

        return string.Join("  ·  ", parts);
    }

    /// <summary>Whether this is the car the driver is announced and recorded in - their profile's active car.</summary>
    private static bool IsActiveCar(DriverProfile profile, Car car) =>
        !string.IsNullOrWhiteSpace(profile.CarModel)
        && (string.Equals(profile.CarModel.Trim(), car.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile.CarModel.Trim(), car.Name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>A car's right-click menu on a driver's page: open it, or make it their active car.</summary>
    private ContextMenuStrip CarMenu(Car car)
    {
        var menu = new ContextMenuStrip();

        var open = new ToolStripMenuItem("Open...");
        open.Click += (_, _) => OpenCar(car);

        var makeActive = new ToolStripMenuItem("Make Active Car");
        makeActive.Click += (_, _) => MakeActiveCar(car);

        var transfer = new ToolStripMenuItem("Transfer...");
        transfer.Click += (_, _) => TransferCar(car);

        menu.Items.AddRange(new ToolStripItem[] { open, makeActive, transfer });

        menu.Opening += (_, _) =>
        {
            var owner = _profiles.Get(car.OwnerTransponder);
            makeActive.Enabled = owner is not null && !car.Retired && !IsActiveCar(owner, car);
        };

        AppTheme.StyleContextMenu(menu);
        return menu;
    }

    /// <summary>
    /// Makes this the car the driver is announced and recorded in (their
    /// profile's CarModel), with an undo that puts back the car they had -
    /// while they still have this one.
    /// </summary>
    private void MakeActiveCar(Car car)
    {
        if (_profiles.Get(car.OwnerTransponder) is not { } profile)
        {
            return;
        }

        var key = profile.Transponder;
        var previousModel = profile.CarModel;
        var name = car.DisplayName.Trim();
        var who = string.IsNullOrWhiteSpace(profile.DriverName) ? key : profile.DriverName.Trim();

        if (!profile.SetActiveCar(car.DisplayName))
        {
            return;
        }

        _undo.Capture($"Make {name} {who}'s active car", () =>
        {
            if (_profiles.Get(key) is not { } current
                || !string.Equals(current.CarModel, name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{who}'s active car has been changed again since. Nothing was changed.");
            }

            current.CarModel = previousModel;
            _profiles.Upsert(current);

            if (!IsDisposed)
            {
                RefreshAll();
                Changed?.Invoke();
            }
        });

        _profiles.Upsert(profile);
        RefreshAll();
        Changed?.Invoke();
    }

    /// <summary>Gives a car to someone else for good (CarTransferring) - from a driver's page, where it then leaves their garage.</summary>
    private void TransferCar(Car car)
    {
        if (!CarTransferring.Run(this, car, _cars, _profiles, _undo))
        {
            return;
        }

        RefreshAll();
        Changed?.Invoke();
    }

    // ------------------------------------------------------- the lists

    private void ManageChassisTypes()
    {
        using var form = new ChassisTypesForm(_chassisTypes, _chassisTemplates, _cars);

        form.ShowDialog(this);
        RefreshAll();
    }

    /// <summary>
    /// The setup-sheet manager, carried over from the Tuning page whole
    /// (ChassisSheetsForm - the window the setup log opens too).
    ///
    /// Deliberately the same code rather than a fresh version of it: the
    /// undo in it has two awkward corners - the editor saves as it goes, so
    /// the entry is captured afterwards by comparing JSON, and deleting the
    /// LAST sheet seeds a replacement that the undo then has to take away
    /// again. Both were got right once. Rewriting them from the outside is
    /// how you get an undo that looks fine and quietly loses a sheet. The
    /// two pages used to build a copy of the window each, line for line;
    /// they open the one class now. The garage is refreshed once it closes.
    /// </summary>
    private void ManageChassisSheets()
    {
        using var picker = new ChassisSheetsForm(_chassisTemplates, _setups, _undo);
        picker.ShowDialog(this);

        RefreshAll();
    }
}
