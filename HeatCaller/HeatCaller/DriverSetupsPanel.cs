namespace HeatCaller;

/// <summary>
/// One driver's setups, newest first, on their page (Driver Hub):
/// when, which car, which track and what was set. Open Car takes the
/// highlighted one's car to its own window, where setups are logged and
/// edited - the same window the Garage opens, so there is one place a
/// setup is changed.
///
/// A setup is the driver's when it is linked to one of their cars, or
/// when it carries their name - one logged before setups were linked to
/// cars, or on a car they have since transferred to someone else, which
/// takes its setups with it.
/// </summary>
internal sealed class DriverSetupsPanel : UserControl
{
    private readonly ProfileStore _profiles;
    private readonly CarStore _cars;
    private readonly SetupLogStore _setups;
    private readonly ChassisTemplateStore _templates;
    private readonly Action<Car> _openCar;

    private readonly ListView _list = new();
    private readonly Button _btnOpen = new() { Text = "Open Car..." };
    private readonly Label _lblEmpty = new() { AutoSize = true };

    private string? _driver;

    /// <summary>Raised after Open Car closed - anything may have changed in it.</summary>
    public event Action? Changed;

    public DriverSetupsPanel(ProfileStore profiles, CarStore cars, SetupLogStore setups, ChassisTemplateStore templates, Action<Car> openCar)
    {
        _profiles = profiles;
        _cars = cars;
        _setups = setups;
        _templates = templates;
        _openCar = openCar;

        Dock = DockStyle.Fill;

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.Columns.Add("Date");
        _list.Columns.Add("Car");
        _list.Columns.Add("Track");
        _list.Columns.Add("Setup");
        _list.SelectedIndexChanged += (_, _) => _btnOpen.Enabled = PickedCar() is not null;
        _list.DoubleClick += (_, _) => OpenPicked();
        _list.ClientSizeChanged += (_, _) => FitColumns();

        _btnOpen.Click += (_, _) => OpenPicked();

        var page = new FillPage();
        page.Add(_lblEmpty, gapBelow: UiLayout.HintGap);
        page.AddFill(_list, _ => UiLayout.AtTextSize(140), gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar().With(_btnOpen), gapBelow: 0);

        Controls.Add(page);

        AppTheme.ApplyRecursively(this);
        _lblEmpty.ForeColor = AppTheme.TextMuted;

        ShowDriver(null);
    }

    /// <summary>Shows this driver's setups - their main transponder, the profile's key - or none.</summary>
    public void ShowDriver(string? driver)
    {
        var same = string.Equals(driver, _driver, StringComparison.OrdinalIgnoreCase);
        var kept = same && _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag : null;
        _driver = driver;
        Fill();

        if (kept is not null
            && _list.Items.Cast<ListViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, kept)) is { } again)
        {
            again.Selected = true;
        }
    }

    private void Fill()
    {
        var profile = _driver is null ? null : _profiles.Get(_driver);
        var carIds = profile is null
            ? new HashSet<string>()
            : _cars.OwnedBy(profile).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var name = profile?.DriverName?.Trim() ?? string.Empty;

        var mine = profile is null
            ? new List<SetupEntry>()
            : _setups.All
                .Where(e => (!string.IsNullOrEmpty(e.CarId) && carIds.Contains(e.CarId))
                            || (name.Length > 0
                                && string.Equals(e.DriverName?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => e.RecordedAtUtc)
                .ToList();

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var entry in mine)
        {
            var car = string.IsNullOrEmpty(entry.CarId) ? null : _cars.Get(entry.CarId);
            var carName = car?.DisplayName ?? entry.CarModel ?? string.Empty;
            var summary = entry.Summarise(_templates.Get(entry.ChassisTemplateId));

            _list.Items.Add(new ListViewItem(new[]
            {
                entry.RecordedAtUtc.ToLocalTime().ToString("d MMM yyyy"),
                carName,
                entry.TrackLayoutName ?? string.Empty,
                summary,
            })
            { Tag = entry });
        }

        _list.EndUpdate();
        FitColumns();

        _lblEmpty.Text = profile is null
            ? string.Empty
            : mine.Count == 0
                ? "No setups logged yet. Open one of their cars on the Garage tab and click Log a Setup."
                : $"{Plural.Of(mine.Count, "setup")}, newest first. Double-click one to open its car.";

        _btnOpen.Enabled = PickedCar() is not null;
    }

    private Car? PickedCar() =>
        _list.SelectedItems.Count == 1
        && _list.SelectedItems[0].Tag is SetupEntry { CarId: { Length: > 0 } carId }
            ? _cars.Get(carId)
            : null;

    private void OpenPicked()
    {
        if (PickedCar() is not { } car)
        {
            return;
        }

        _openCar(car);
        Fill();
        Changed?.Invoke();
    }

    private void FitColumns()
    {
        if (_list.Columns.Count < 4)
        {
            return;
        }

        var width = Math.Max(1, _list.ClientSize.Width);
        _list.Columns[0].Width = width * 18 / 100;
        _list.Columns[1].Width = width * 22 / 100;
        _list.Columns[2].Width = width * 20 / 100;
        _list.Columns[3].Width = width - _list.Columns[0].Width - _list.Columns[1].Width - _list.Columns[2].Width;
    }
}
