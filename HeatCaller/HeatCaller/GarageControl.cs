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

    private readonly ComboBox _cmbOwnerFilter = new();
    private readonly CheckBox _chkRetired = new();
    private readonly Label _lblSummary = new();
    private readonly Panel _wall = new();
    private readonly Label _lblEmpty = new();

    /// <summary>One class's heading and the cards under it, kept so a resize can reflow without rebuilding.</summary>
    private readonly List<(Label Band, List<CarCard> Cards)> _groups = new();

    private const int WallPad = 12;
    private const int CardGap = 12;
    private const int BandHeight = 28;

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
        RaceEventStore? events = null)
    {
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
        MinimumSize = new Size(920, 560);
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshAll();
    }

    /// <summary>The race day a loan would be for, or null when none is open.</summary>
    private string? CurrentEventId => _events?.Active is { IsLegacy: false } day ? day.Id : null;

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "The club's cars. Add a photograph and a car starts looking like itself - until then it gets a plate "
                 + "with its number on. Click a car to open it: its photographs, what it is, and every setup logged for it.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            ForeColor = AppTheme.TextMuted,
        };

        var lblFilter = new Label { Text = "Show:", Location = new Point(12, 62), AutoSize = true };

        _cmbOwnerFilter.Location = new Point(70, 59);
        _cmbOwnerFilter.Width = 240;
        _cmbOwnerFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbOwnerFilter.SelectedIndexChanged += (_, _) => BuildWall();

        _chkRetired.Text = "Include retired";
        _chkRetired.Location = new Point(324, 61);
        _chkRetired.AutoSize = true;
        _chkRetired.CheckedChanged += (_, _) => BuildWall();

        _lblSummary.Location = new Point(470, 62);
        _lblSummary.AutoSize = true;
        _lblSummary.ForeColor = AppTheme.TextMuted;

        // The wall. AutoScroll rather than a fixed grid of pages: a club
        // with sixty cars scrolls, and a club with four does not get four
        // cars stranded at the top of an empty page of tabs.
        _wall.Location = new Point(12, 94);
        _wall.Size = new Size(880, 340);
        _wall.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _wall.AutoScroll = true;
        _wall.BackColor = AppTheme.Background;
        _wall.Resize += (_, _) => LayoutWall();

        _lblEmpty.Text = "No cars yet. Add one, and it appears here under its class.";
        _lblEmpty.AutoSize = true;
        _lblEmpty.ForeColor = AppTheme.TextMuted;
        _lblEmpty.Location = new Point(WallPad, WallPad);
        _lblEmpty.Visible = false;

        _wall.Controls.Add(_lblEmpty);

        var btnAdd = new Button { Text = "Add a Car..." };
        btnAdd.Click += (_, _) => AddCar();

        var btnChassis = new Button { Text = "Chassis Types..." };
        btnChassis.Click += (_, _) => ManageChassisTypes();

        var btnSheets = new Button { Text = "Chassis Sheets..." };
        btnSheets.Click += (_, _) => ManageChassisSheets();

        foreach (var button in new[] { btnAdd, btnChassis, btnSheets })
        {
            button.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        }

        var buttonsY = _wall.Bottom + UiLayout.RowGap;

        UiLayout.ButtonRow(12, buttonsY, btnAdd, btnChassis, btnSheets);

        Controls.AddRange(new Control[]
        {
            lblIntro, lblFilter, _cmbOwnerFilter, _chkRetired, _lblSummary, _wall,
            btnAdd, btnChassis, btnSheets,
        });
    }

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

        var match = _cmbOwnerFilter.Items
            .Cast<OwnerChoice>()
            .FirstOrDefault(c => string.Equals(c.Transponder, previous, StringComparison.OrdinalIgnoreCase));

        _cmbOwnerFilter.SelectedItem = match ?? _cmbOwnerFilter.Items[0];
    }

    private IReadOnlyList<Car> VisibleCars()
    {
        var chosen = (_cmbOwnerFilter.SelectedItem as OwnerChoice)?.Transponder ?? EveryoneKey;

        var cars = chosen switch
        {
            EveryoneKey => _cars.All,
            ClubKey => _cars.ClubCars,
            _ => _cars.OwnedBy(chosen),
        };

        return _chkRetired.Checked ? cars : cars.Where(c => !c.Retired).ToList();
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
                var card = new CarCard(car, WhoHasIt(car, eventId), _chassisTypes.NameOf(car.ChassisTypeId));

                card.Opened += OpenCar;

                _wall.Controls.Add(card);
                cards.Add(card);
            }

            _groups.Add((band, cards));
        }

        var total = _cars.InUse.Count;
        var club = _cars.ClubCars.Count(c => !c.Retired);

        _lblSummary.Text = total == 0
            ? "No cars yet."
            : $"{Plural.Of(total, "car")} in the garage" + (club > 0 ? $", {club} the club's own." : ".");

        _lblEmpty.Visible = _groups.Count == 0;
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

        // ClientSize, not Width: once the cards overflow, AutoScroll puts a
        // vertical scrollbar inside the panel and the usable width drops by
        // its thickness. Laying out against Width instead leaves the last
        // column of every row clipped behind the bar.
        var usable = Math.Max(CarCard.CardWidth, _wall.ClientSize.Width - WallPad * 2);
        var perRow = Math.Max(1, (usable + CardGap) / (CarCard.CardWidth + CardGap));

        var y = WallPad;

        foreach (var (band, cards) in _groups)
        {
            band.Location = new Point(WallPad, y);
            band.Width = usable;

            y += BandHeight + 6;

            for (var i = 0; i < cards.Count; i++)
            {
                var column = i % perRow;
                var row = i / perRow;

                cards[i].Location = new Point(
                    WallPad + column * (CarCard.CardWidth + CardGap),
                    y + row * (CarCard.CardHeight + CardGap));
            }

            var rows = (cards.Count + perRow - 1) / perRow;

            y += rows * (CarCard.CardHeight + CardGap) + UiLayout.SectionGap;
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
    }

    private void AddCar()
    {
        var chosenOwner = (_cmbOwnerFilter.SelectedItem as OwnerChoice)?.Transponder;
        var owner = chosenOwner is null or EveryoneKey or ClubKey ? string.Empty : chosenOwner;

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

        _undo.Capture($"Add {saved.DisplayName} to the garage", () =>
        {
            _cars.Delete(saved.Id);
            RefreshAll();
        });

        _cars.Upsert(saved);
        SyncActiveCarString(saved);
        RefreshAll();
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
    /// </summary>
    private void SyncActiveCarString(Car car)
    {
        if (car.IsClubCar || car.Retired || _profiles.Get(car.OwnerTransponder) is not { } profile)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(profile.CarModel))
        {
            return;
        }

        if (profile.SetActiveCar(car.DisplayName))
        {
            _profiles.Upsert(profile);
        }
    }

    // ------------------------------------------------------- the lists

    private void ManageChassisTypes()
    {
        using var form = new ChassisTypesForm(_chassisTypes, _chassisTemplates, _cars);

        form.ShowDialog(this);
        RefreshAll();
    }

    /// <summary>
    /// The setup-sheet manager, carried over from the Tuning page whole.
    ///
    /// Deliberately the same code rather than a fresh version of it: the
    /// undo here has two awkward corners - the editor saves as it goes, so
    /// the entry is captured afterwards by comparing JSON, and deleting the
    /// LAST sheet seeds a replacement that the undo then has to take away
    /// again. Both were got right once. Rewriting them from the outside is
    /// how you get an undo that looks fine and quietly loses a sheet.
    /// </summary>
    private void ManageChassisSheets()
    {
        using var picker = new Form
        {
            Text = "Chassis Setup Sheets",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(460, 340),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
            BackColor = AppTheme.Background,
        };

        var list = new ListBox { Location = new Point(12, 12), Size = new Size(300, 280) };

        void Reload()
        {
            list.Items.Clear();

            foreach (var t in _chassisTemplates.All)
            {
                list.Items.Add(t.Name);
            }

            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        }

        ChassisTemplate? Selected() =>
            list.SelectedIndex >= 0 ? _chassisTemplates.All.ElementAtOrDefault(list.SelectedIndex) : null;

        var btnEdit = new Button { Text = "Edit...", Location = new Point(322, 12), Width = 120 };
        btnEdit.Click += (_, _) =>
        {
            if (Selected() is not { } t) { return; }

            // The editor saves as it goes (a field edit is written straight
            // away), so the undo is captured once it closes, if anything
            // actually changed - whichever button closed it.
            var before = ChassisTemplateStore.CopyOf(t);
            var beforeJson = System.Text.Json.JsonSerializer.Serialize(before);

            using var editor = new ChassisTemplateForm(_chassisTemplates, t);
            editor.ShowDialog(picker);

            if (_chassisTemplates.Get(before.Id) is { } after
                && System.Text.Json.JsonSerializer.Serialize(after) != beforeJson)
            {
                var templates = _chassisTemplates;

                _undo.Capture($"Edit chassis sheet '{before.Name}'", () =>
                {
                    if (!templates.PutBack(before))
                    {
                        throw new InvalidOperationException($"the chassis sheet '{before.Name}' has been deleted since, so there is nothing to put back.");
                    }
                });
            }

            Reload();
        };

        var btnNew = new Button { Text = "New...", Location = new Point(322, 50), Width = 120 };
        btnNew.Click += (_, _) =>
        {
            var created = ChassisTemplate.CreateKyoshoDefault();
            created.Name = "New chassis sheet";
            _chassisTemplates.Upsert(created);

            using var editor = new ChassisTemplateForm(_chassisTemplates, created);
            editor.ShowDialog(picker);
            Reload();
        };

        var btnDuplicate = new Button { Text = "Duplicate", Location = new Point(322, 88), Width = 120 };
        btnDuplicate.Click += (_, _) =>
        {
            if (Selected() is not { } t) { return; }

            _chassisTemplates.Upsert(t.Clone());
            Reload();
        };

        var btnDelete = new Button { Text = "Delete", Location = new Point(322, 126), Width = 120 };
        btnDelete.Click += (_, _) =>
        {
            if (Selected() is not { } t) { return; }

            if (AppDialog.Show(picker,
                    $"Delete the '{t.Name}' sheet?\r\n\r\nSetup entries that used it are kept, but will show as legacy entries. This can be reversed with Undo.",
                    "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                // The template carries every section, field and option, so
                // the whole object goes into the undo capture - back in its
                // old place. The store, not this page, in the undo: the page
                // may be gone by then, and re-reads the store when shown.
                var templates = _chassisTemplates;
                var entries = _setups;
                var index = _chassisTemplates.IndexOf(t.Id);
                var wasLast = _chassisTemplates.All.Count == 1;
                string? seededId = null;

                _undo.Capture($"Delete chassis sheet '{t.Name}'", () =>
                {
                    templates.RestoreDeleted(t, index, seededId, seededId is not null && entries.UsesTemplate(seededId));
                });

                _chassisTemplates.Delete(t.Id);

                // Deleting the last sheet seeds a default in its place; the
                // undo takes that one away again.
                if (wasLast && _chassisTemplates.All.Count == 1)
                {
                    seededId = _chassisTemplates.All[0].Id;
                }

                Reload();
            }
        };

        var btnClose = new Button { Text = "Close", Location = new Point(322, 262), Width = 120, DialogResult = DialogResult.OK };

        picker.Controls.AddRange(new Control[] { list, btnEdit, btnNew, btnDuplicate, btnDelete, btnClose });
        picker.AcceptButton = btnClose;

        Reload();
        AppTheme.ApplyRecursively(picker);
        picker.ShowDialog(this);

        RefreshAll();
    }
}
