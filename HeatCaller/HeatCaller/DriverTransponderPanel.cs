using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// One driver's transponders, on their page (Driver Hub): the main one
/// and every other, which car each is fitted to, and which are retired -
/// with Add, Fitted To, Retire / Bring Back and Remove, each saved at once
/// with its own Undo step.
///
/// The same changes the profile form's Transponders line makes, without
/// opening the form; the rules are the store's (DescribeClash), so a
/// transponder can never end up on two drivers from here either.
/// </summary>
internal sealed class DriverTransponderPanel : UserControl
{
    private readonly ProfileStore _profiles;
    private readonly CarStore _cars;
    private readonly UndoService _undo;

    private readonly ListView _list = new();
    private readonly ComboBox _cmbFitted = new();
    private readonly Button _btnAdd = new() { Text = "Add Transponder..." };
    private readonly Button _btnRetire = new() { Text = "Retire" };
    private readonly Button _btnRemove = new() { Text = "Remove" };
    private readonly Label _lblHint = new() { AutoSize = true };

    /// <summary>The driver shown - their main transponder - or null.</summary>
    private string? _driver;

    /// <summary>True while the fitted-to drop-down is being filled, so filling it changes nothing.</summary>
    private bool _filling;

    /// <summary>Raised after a change was saved - for a host showing more of the driver than this.</summary>
    public event Action? Changed;

    public DriverTransponderPanel(ProfileStore profiles, CarStore cars, UndoService undo)
    {
        _profiles = profiles;
        _cars = cars;
        _undo = undo;

        Dock = DockStyle.Fill;

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.Columns.Add("Transponder");
        _list.Columns.Add("Fitted to");
        _list.Columns.Add("");
        _list.SelectedIndexChanged += (_, _) => OnPicked();
        _list.ClientSizeChanged += (_, _) => FitColumns();

        _cmbFitted.DropDownStyle = ComboBoxStyle.DropDownList;
        // Committed, not merely changed: the wheel or the arrow keys
        // passing over a car would otherwise save, with an Undo step, at
        // every one.
        _cmbFitted.SelectionChangeCommitted += (_, _) => FitToCar();

        _btnAdd.Click += (_, _) => AddTransponder();
        _btnRetire.Click += (_, _) => ToggleRetired();
        _btnRemove.Click += (_, _) => RemoveTransponder();

        _lblHint.Text = "A retired transponder is no longer in use, but it is still theirs - and so is everything timed on it.";

        var fitted = new FieldGrid();
        fitted.AddRow("Fitted to:", _cmbFitted);

        var page = new FillPage();
        page.AddFill(_list, _ => UiLayout.AtTextSize(120), gapBelow: UiLayout.RowGap);
        page.Add(fitted, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar().With(_btnAdd, _btnRetire, _btnRemove), gapBelow: UiLayout.RowGap);
        page.Add(_lblHint, gapBelow: 0);

        Controls.Add(page);

        AppTheme.ApplyRecursively(this);
        _lblHint.ForeColor = AppTheme.TextMuted;
        _cmbFitted.Width = UiLayout.AtTextSize(220);

        ShowDriver(null);
    }

    /// <summary>Shows this driver's transponders - their main transponder, the profile's key - or none.</summary>
    public void ShowDriver(string? driver)
    {
        // The same driver again (drawn again after a change): the
        // highlighted transponder stays highlighted.
        var same = string.Equals(driver, _driver, StringComparison.OrdinalIgnoreCase);
        _driver = driver;
        Fill(keep: same ? Picked()?.Id : null);
    }

    private DriverProfile? Driver() => _driver is null ? null : _profiles.Get(_driver);

    /// <summary>One line of the list.</summary>
    private sealed record Row(string Id, bool IsMain, bool Retired, string CarId);

    /// <summary>One line of the fitted-to drop-down.</summary>
    private sealed record CarChoice(string CarId, string Caption)
    {
        public override string ToString() => Caption;
    }

    private void Fill(string? keep)
    {
        var profile = Driver();

        _list.BeginUpdate();
        _list.Items.Clear();

        if (profile is not null)
        {
            foreach (var id in profile.AllTransponderIds)
            {
                var entry = profile.TransponderEntry(id);
                var isMain = string.Equals(id, profile.Transponder?.Trim(), StringComparison.OrdinalIgnoreCase);
                var row = new Row(id, isMain, entry?.Retired == true, entry?.CarId ?? string.Empty);

                var status = string.Join(", ", new[] { isMain ? "main" : null, row.Retired ? "retired" : null }.Where(s => s is not null));

                var item = new ListViewItem(new[] { id, CarName(row.CarId), status }) { Tag = row };

                if (row.Retired)
                {
                    item.ForeColor = AppTheme.TextMuted;
                }

                _list.Items.Add(item);
            }
        }

        _list.EndUpdate();
        FitColumns();

        var again = _list.Items.Cast<ListViewItem>()
            .FirstOrDefault(i => string.Equals(((Row)i.Tag!).Id, keep, StringComparison.OrdinalIgnoreCase));

        if (again is not null)
        {
            again.Selected = true;
        }
        else if (_list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
        }

        _btnAdd.Enabled = profile is not null;
        OnPicked();
    }

    private string CarName(string carId) =>
        carId.Length == 0 ? "moves between cars" : _cars.Get(carId)?.DisplayName ?? "a car no longer in the garage";

    private Row? Picked() => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as Row : null;

    private void OnPicked()
    {
        var row = Picked();
        var profile = Driver();

        _btnRetire.Enabled = row is not null;
        _btnRetire.Text = row is { Retired: true } ? "Bring Back" : "Retire";
        _btnRemove.Enabled = row is { IsMain: false };

        _filling = true;

        try
        {
            _cmbFitted.Items.Clear();
            _cmbFitted.Items.Add(new CarChoice(string.Empty, "Moves between cars"));

            if (profile is not null)
            {
                foreach (var car in _cars.OwnedBy(profile).Where(c => !c.Retired))
                {
                    _cmbFitted.Items.Add(new CarChoice(car.Id, car.DisplayName));
                }
            }

            var match = _cmbFitted.Items.Cast<CarChoice>()
                .FirstOrDefault(c => string.Equals(c.CarId, row?.CarId ?? string.Empty, StringComparison.Ordinal));

            // A car no longer in the garage: shown as itself, so nothing
            // changes until somebody picks something.
            if (match is null && row is not null)
            {
                match = new CarChoice(row.CarId, CarName(row.CarId));
                _cmbFitted.Items.Add(match);
            }

            _cmbFitted.SelectedItem = match ?? _cmbFitted.Items[0];
            _cmbFitted.Enabled = row is not null;
        }
        finally
        {
            _filling = false;
        }
    }

    private void FitColumns()
    {
        if (_list.Columns.Count < 3)
        {
            return;
        }

        var width = Math.Max(1, _list.ClientSize.Width);
        _list.Columns[0].Width = width * 30 / 100;
        _list.Columns[1].Width = width * 45 / 100;
        _list.Columns[2].Width = width - _list.Columns[0].Width - _list.Columns[1].Width;
    }

    private static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

    /// <summary>
    /// Saves a change to the driver's transponder list, with an undo that
    /// puts the whole list back - while it is still as this left it, and
    /// never onto a transponder somebody else has taken since. False when
    /// refused (and the reason shown).
    /// </summary>
    private bool Change(string undoName, Action<DriverProfile> change, string? keep)
    {
        if (Driver() is not { } current)
        {
            return false;
        }

        var before = JsonSerializer.Serialize(current.Transponders ?? new List<OwnedTransponder>());
        var updated = ProfileStore.Snapshot(current);
        change(updated);

        if (_profiles.DescribeClash(updated, editing: updated.Transponder) is { } clash)
        {
            AppDialog.Show(this, clash, "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var after = JsonSerializer.Serialize(updated.Transponders);
        var key = updated.Transponder;
        var who = NameOf(updated);
        var profiles = _profiles;

        _undo.Capture(undoName, () =>
        {
            if (profiles.Get(key) is not { } now
                || JsonSerializer.Serialize(now.Transponders ?? new List<OwnedTransponder>()) != after)
            {
                throw new InvalidOperationException($"{who}'s transponders have been changed again since, and undoing this now would undo that too. Nothing was changed.");
            }

            var restored = ProfileStore.Snapshot(now);
            restored.Transponders = JsonSerializer.Deserialize<List<OwnedTransponder>>(before) ?? new List<OwnedTransponder>();

            if (profiles.DescribeClash(restored, editing: key) is { } taken)
            {
                throw new InvalidOperationException($"{taken} Nothing was changed.");
            }

            profiles.Upsert(restored);

            if (!IsDisposed)
            {
                Fill(keep);
            }

            Changed?.Invoke();
        });

        _profiles.Upsert(updated);
        Fill(keep);
        Changed?.Invoke();
        return true;
    }

    private void AddTransponder()
    {
        if (Driver() is not { } profile)
        {
            return;
        }

        using var ask = new RentalPromptForm(
            "Add Transponder",
            $"Another transponder for {NameOf(profile)} - a second car's, a spare or a replacement. Its number:",
            string.Empty);

        if (ask.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var id = ask.Answer;

        if (id.Length == 0)
        {
            return;
        }

        if (id.Length > 40)
        {
            AppDialog.Show(this, "That transponder number is too long.", "Add Transponder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (profile.AllTransponderIds.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            Fill(keep: id);
            return;
        }

        Change($"Add transponder {id} to {NameOf(profile)}",
            p => p.Transponders.Add(new OwnedTransponder { Id = id, AddedUtc = DateTime.UtcNow }),
            keep: id);
    }

    private void ToggleRetired()
    {
        if (Picked() is not { } row || Driver() is not { } profile)
        {
            return;
        }

        var retire = !row.Retired;

        Change(retire ? $"Retire transponder {row.Id}" : $"Bring back transponder {row.Id}",
            p =>
            {
                var entry = p.EntryToChange(row.Id);
                entry.Retired = retire;
                entry.RetiredUtc = retire ? DateTime.UtcNow : null;
            },
            keep: row.Id);
    }

    private void RemoveTransponder()
    {
        if (Picked() is not { IsMain: false } row || Driver() is not { } profile)
        {
            return;
        }

        if (AppDialog.Show(this,
                $"Remove transponder {row.Id} from {NameOf(profile)}?\n\nLaps already timed on it will no longer count as theirs. "
                + "If it is just no longer used, Retire it instead - that keeps them.",
                "Remove transponder", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        Change($"Remove transponder {row.Id} from {NameOf(profile)}",
            p => p.Transponders.RemoveAll(t => string.Equals(t.Id?.Trim(), row.Id, StringComparison.OrdinalIgnoreCase)),
            keep: null);
    }

    /// <summary>The fitted-to drop-down changed: which car the picked transponder is in.</summary>
    private void FitToCar()
    {
        if (_filling || Picked() is not { } row || _cmbFitted.SelectedItem is not CarChoice choice)
        {
            return;
        }

        if (string.Equals(choice.CarId, row.CarId, StringComparison.Ordinal))
        {
            return;
        }

        Change(choice.CarId.Length == 0
                ? $"Transponder {row.Id} moves between cars"
                : $"Fit transponder {row.Id} to {choice.Caption}",
            p => p.EntryToChange(row.Id).CarId = choice.CarId,
            keep: row.Id);
    }
}
