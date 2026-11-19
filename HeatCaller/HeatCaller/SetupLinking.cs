namespace HeatCaller;

/// <summary>
/// Setups belong to cars (SetupEntry.CarId). Ones logged before they did
/// carry only a driver's name and a car as somebody typed it; this links
/// them to the garage car they were for - once, automatically, where that
/// can be told for certain (LinkOnce), and by hand for the rest
/// (UnlinkedSetupsForm, a driver's Setups tab).
/// </summary>
internal static class SetupLinking
{
    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether an entry has a car in the garage: linked, and the car still there.</summary>
    public static bool HasCar(SetupEntry entry, CarStore cars) =>
        !string.IsNullOrEmpty(entry.CarId) && cars.Get(entry.CarId) is not null;

    /// <summary>The setups with no car in the garage - never linked, or linked to a car since deleted. Newest first.</summary>
    public static IReadOnlyList<SetupEntry> Unlinked(SetupLogStore setups, CarStore cars) =>
        setups.All.Where(e => !HasCar(e, cars)).ToList();

    /// <summary>
    /// The car an unlinked entry was for, when only one can be meant: the
    /// one driver with the entry's name owns exactly one car whose name or
    /// shell is what was typed. Null whenever there is any doubt - a wrong
    /// guess files one driver's numbers under another car, which is worse
    /// than asking.
    /// </summary>
    public static Car? CertainCarFor(SetupEntry entry, ProfileStore profiles, CarStore cars)
    {
        var typed = entry.CarModel?.Trim() ?? string.Empty;

        if (typed.Length == 0)
        {
            return null;
        }

        var drivers = profiles.All.Where(p => Same(p.DriverName, entry.DriverName)).ToList();

        if (drivers.Count != 1)
        {
            return null;
        }

        var matches = cars.OwnedBy(drivers[0])
            .Where(c => Same(c.DisplayName, typed) || Same(c.Name, typed) || Same(c.Bodyshell, typed))
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Links every never-linked setup it can be certain of (CertainCarFor),
    /// once. Not while the setups, the garage or the drivers could not be
    /// read - a pass over an empty one would find nothing, and the flag
    /// would stop it ever running.
    /// Returns how many were linked and how many are left to do by hand.
    /// </summary>
    public static (int Linked, int Left) LinkOnce(AppSettings settings, SetupLogStore setups, ProfileStore profiles, CarStore cars)
    {
        if (settings.HasLinkedSetupsToCars || setups.LoadedUnreadable || cars.Unreadable || profiles.LoadedUnreadable)
        {
            return (0, 0);
        }

        var links = new List<(string Id, string CarId, string CarName)>();

        foreach (var entry in setups.All.Where(e => string.IsNullOrEmpty(e.CarId)))
        {
            if (CertainCarFor(entry, profiles, cars) is { } car)
            {
                links.Add((entry.Id, car.Id, car.DisplayName));
            }
        }

        var linked = setups.LinkToCars(links);

        settings.HasLinkedSetupsToCars = true;
        settings.Save();

        return (linked, Unlinked(setups, cars).Count);
    }

    /// <summary>
    /// Asks which car a setup was for (PickCarForm) and links it, with an
    /// Undo step that unlinks it again - while it is still as this left it.
    /// True when it was linked.
    /// </summary>
    public static bool LinkByHand(IWin32Window owner, SetupEntry entry, SetupLogStore setups, CarStore cars, ProfileStore profiles, UndoService undo)
    {
        using var pick = new PickCarForm(entry, cars, profiles);

        if (pick.ShowDialog(owner) != DialogResult.OK || pick.Picked is not { } car)
        {
            return false;
        }

        if (setups.Get(entry.Id) is not { } stored)
        {
            AppDialog.Show(owner, "That setup is no longer in the log.", "Link to Car", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var before = SetupLogStore.CopyOf(stored);
        var after = SetupLogStore.CopyOf(stored);
        after.CarId = car.Id;
        after.CarModel = car.DisplayName;
        var afterJson = System.Text.Json.JsonSerializer.Serialize(after);

        undo.Capture($"Link a setup to {car.DisplayName}", () =>
        {
            if (setups.Get(before.Id) is not { } now || System.Text.Json.JsonSerializer.Serialize(now) != afterJson)
            {
                throw new InvalidOperationException("That setup has been changed since, and undoing this now would undo that too. Nothing was changed.");
            }

            if (!setups.UndoUpsert(before.Id, SetupLogStore.CopyOf(before)))
            {
                throw new InvalidOperationException("That setup is no longer in the log. Nothing was changed.");
            }
        });

        setups.Upsert(after);
        return true;
    }
}

/// <summary>
/// "Which car was this setup for?" - the garage's cars, the driver's own
/// first, to link a setup to. Only asks; SetupLinking.LinkByHand links it.
/// </summary>
internal sealed class PickCarForm : Form
{
    private const int LeastRows = 6;

    private readonly ListBox _lstCars = new();
    private readonly Button _btnLink = new() { Text = "Link to Car" };

    /// <summary>The car picked. Set on OK.</summary>
    public Car? Picked { get; private set; }

    private sealed record CarChoice(Car Car, string Label)
    {
        public override string ToString() => Label;
    }

    public PickCarForm(SetupEntry entry, CarStore cars, ProfileStore profiles)
    {
        Text = "Which car was this setup for?";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var typed = string.IsNullOrWhiteSpace(entry.CarModel) ? "no car given" : $"\"{entry.CarModel.Trim()}\"";
        var driver = string.IsNullOrWhiteSpace(entry.DriverName) ? "no driver given" : entry.DriverName.Trim();

        var lblWhat = new Label
        {
            Text = $"Logged {entry.RecordedAtUtc.ToLocalTime():d MMM yyyy} for {driver}, car {typed}. Pick the car it was for - it stays with that car from now on.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // The driver named on it first: almost always it is one of theirs.
        var owner = profiles.All.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.DriverName)
            && string.Equals(p.DriverName.Trim(), entry.DriverName?.Trim(), StringComparison.OrdinalIgnoreCase));

        var theirs = owner is null ? new List<Car>() : cars.OwnedBy(owner).ToList();
        var others = cars.All.Where(c => !theirs.Any(t => t.Id == c.Id)).ToList();

        string OwnerName(Car car) =>
            car.IsClubCar ? "the club"
            : profiles.Get(car.OwnerTransponder) is { } p && !string.IsNullOrWhiteSpace(p.DriverName) ? p.DriverName.Trim()
            : car.OwnerTransponder;

        foreach (var car in theirs.Concat(others))
        {
            var retired = car.Retired ? "  (retired)" : string.Empty;
            _lstCars.Items.Add(new CarChoice(car, $"{car.DisplayName}  ·  {OwnerName(car)}{retired}"));
        }

        // The car of theirs it names, when one does: otherwise nothing is
        // picked, so Enter can't link it to a guess.
        var named = theirs.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(entry.CarModel)
            && (string.Equals(c.DisplayName, entry.CarModel.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Name?.Trim(), entry.CarModel.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Bodyshell?.Trim(), entry.CarModel.Trim(), StringComparison.OrdinalIgnoreCase)));

        if (named is not null)
        {
            _lstCars.SelectedItem = _lstCars.Items.Cast<CarChoice>().First(i => i.Car.Id == named.Id);
        }

        _lstCars.IntegralHeight = false;
        _lstCars.DoubleClick += (_, _) => Link();
        _lstCars.SelectedIndexChanged += (_, _) => _btnLink.Enabled = _lstCars.SelectedItem is CarChoice;
        _btnLink.Enabled = _lstCars.SelectedItem is CarChoice;
        _btnLink.Click += (_, _) => Link();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // THE LOOK (18 Nov 2026, dialog jazz-up): the garage's cars in one
        // card, what the setup says as its line on what it is for, and Link
        // to Car in the app's colour.
        var card = new Section("Cars in the garage");
        card.Add(lblWhat, gapBelow: UiLayout.RowGap);
        card.AddFill(_lstCars, () => LeastRows * _lstCars.ItemHeight + 4, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnLink), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnCancel;
        AcceptButton = _btnLink;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(_btnLink, AppTheme.ButtonKind.Primary);

        // Wider and taller than the 520x440 it was made at by the card's
        // edges and caption, so the list is the size it was.
        DialogFit.Keep(this, page, width: 552, height: 470);
    }

    private void Link()
    {
        if (_lstCars.SelectedItem is not CarChoice choice)
        {
            return;
        }

        Picked = choice.Car;
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// The setups that belong to no car in the garage - logged before setups
/// belonged to cars and not certain enough to link by themselves, or left
/// behind by a car since deleted - to link by hand or delete. Opened from
/// the Garage page.
///
/// On the layout pieces (DialogFit): one card, why they have no car as its
/// hint line, the setups taking the height and how many under them; Link to
/// Car, Delete and Close at the foot.
/// </summary>
internal sealed class UnlinkedSetupsForm : Form
{
    private readonly SetupLogStore _setups;
    private readonly CarStore _cars;
    private readonly ProfileStore _profiles;
    private readonly UndoService _undo;

    private readonly ListView _list = new();
    private readonly Button _btnLink = new() { Text = "Link to Car..." };
    private readonly Button _btnDelete = new() { Text = "Delete" };
    private readonly Label _lblCount = new() { AutoSize = true };

    public UnlinkedSetupsForm(SetupLogStore setups, CarStore cars, ProfileStore profiles, UndoService undo)
    {
        _setups = setups;
        _cars = cars;
        _profiles = profiles;
        _undo = undo;

        Text = "Setups Without a Car";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblWhat = new Label
        {
            Text = "These setups aren't linked to a car in the garage - logged before setups belonged to cars, or for a "
                   + "car since deleted. Link each to its car to show it there and on its driver's Setups tab.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.Columns.Add("Date", 110);
        _list.Columns.Add("Driver", 150);
        _list.Columns.Add("Car as logged", 160);
        _list.Columns.Add("Track", 140);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => LinkPicked();

        _btnLink.Click += (_, _) => LinkPicked();
        _btnDelete.Click += (_, _) => DeletePicked();

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        // THE LOOK (18 Nov 2026, dialog jazz-up): the setups in one card,
        // why they have no car as its hint line and the count under the
        // list; Link to Car in the app's colour.
        var card = new Section("Setups to link");
        card.Add(lblWhat, gapBelow: UiLayout.RowGap);
        card.AddFill(_list, () => UiLayout.AtTextSize(180), gapBelow: UiLayout.HintGap);
        card.Add(_lblCount, stretch: false, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(new SplitBar(new ButtonBar().With(_btnLink, _btnDelete), new ButtonBar(alignRight: true).With(btnClose)), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);
        _lblCount.ForeColor = AppTheme.TextMuted;
        _lblCount.Font = AppTheme.SmallFont;
        AppTheme.StyleButton(_btnLink, AppTheme.ButtonKind.Primary);

        Fill();

        // Wider and taller than the 640x480 it was made at by the card's
        // edges and caption, so the list keeps its size.
        DialogFit.Keep(this, page, width: 672, height: 500);
    }

    private void Fill()
    {
        var keep = _list.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag!).ToHashSet();

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var entry in SetupLinking.Unlinked(_setups, _cars))
        {
            var item = new ListViewItem(new[]
            {
                entry.RecordedAtUtc.ToLocalTime().ToString("d MMM yyyy"),
                entry.DriverName ?? string.Empty,
                string.IsNullOrWhiteSpace(entry.CarModel) ? "-" : entry.CarModel.Trim(),
                entry.TrackLayoutName ?? string.Empty,
            })
            { Tag = entry.Id, Selected = keep.Contains(entry.Id) };

            _list.Items.Add(item);
        }

        _list.EndUpdate();

        _lblCount.Text = _list.Items.Count == 0
            ? "Every setup is linked to a car."
            : Plural.Of(_list.Items.Count, "setup") + " without a car.";

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _btnLink.Enabled = _list.SelectedItems.Count == 1;
        _btnDelete.Enabled = _list.SelectedItems.Count > 0;
    }

    private void LinkPicked()
    {
        if (_list.SelectedItems.Count != 1 || _setups.Get((string)_list.SelectedItems[0].Tag!) is not { } entry)
        {
            return;
        }

        if (SetupLinking.LinkByHand(this, entry, _setups, _cars, _profiles, _undo))
        {
            Fill();
        }
    }

    private void DeletePicked()
    {
        var picked = _list.SelectedItems.Cast<ListViewItem>()
            .Select(i => _setups.Get((string)i.Tag!))
            .OfType<SetupEntry>()
            .ToList();

        if (picked.Count == 0
            || AppDialog.Show(this, $"Delete {Plural.Of(picked.Count, "setup")}? Undo brings them back.", "Delete setups",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var removed = picked.Select(SetupLogStore.CopyOf).ToList();
        var setups = _setups;

        _undo.Capture($"Delete {Plural.Of(removed.Count, "setup")}", () => setups.RestoreDeleted(removed));

        foreach (var entry in picked)
        {
            _setups.Delete(entry.Id);
        }

        Fill();
    }
}
