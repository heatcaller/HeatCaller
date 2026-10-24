using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// One driver's transponders, on their page (Driver Hub): the main one
/// and every other, which car each is fitted to, and which are retired -
/// with Add, Fitted To, Retire / Bring Back, Transfer and Remove, each
/// saved at once with its own Undo step. Their split-loop transponders
/// (DriverProfile.SplitTransponders) are listed under them, marked
/// "split loop", with Add Split Transponder.
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

    /// <summary>
    /// The race history, for Transfer to find the races a transponder was
    /// timed in as a car of its own (TransponderTransferring.RacesFiledUnder)
    /// and ask before they start counting for the new owner. Null only where
    /// there is no history to look in.
    /// </summary>
    private readonly SessionHistoryStore? _history;

    private readonly ListView _list = new();
    private readonly ComboBox _cmbFitted = new();
    private readonly Button _btnAdd = new() { Text = "Add Transponder..." };

    /// <summary>A transponder the split loop reads (DriverProfile.SplitTransponders).</summary>
    private readonly Button _btnAddSplit = new() { Text = "Add Split Transponder..." };
    private readonly Button _btnRetire = new() { Text = "Retire" };

    /// <summary>Gives the picked transponder to another driver for good (TransponderTransferring) - not the main one, which is their key.</summary>
    private readonly Button _btnTransfer = new() { Text = "Transfer..." };
    private readonly Button _btnRemove = new() { Text = "Remove" };
    private readonly Label _lblHint = new() { AutoSize = true };

    /// <summary>The fitted-to drop-down's width as last measured - see Pieces.PickerWidth. Forgotten when it is refilled.</summary>
    private (Font Font, int Count, int Width)? _fittedWidth;

    /// <summary>The driver shown - their main transponder - or null.</summary>
    private string? _driver;

    /// <summary>True while the fitted-to drop-down is being filled, so filling it changes nothing.</summary>
    private bool _filling;

    /// <summary>Raised after a change was saved - for a host showing more of the driver than this.</summary>
    public event Action? Changed;

    /// <summary>The split loop's recent unknown reads, most recent first - see DriverGarageSources.SplitSeenRecently.</summary>
    public Func<IReadOnlyList<string>>? SplitSeenRecently { get; init; }

    public DriverTransponderPanel(ProfileStore profiles, CarStore cars, SessionHistoryStore? history, UndoService undo)
    {
        _profiles = profiles;
        _cars = cars;
        _history = history;
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
        _btnAddSplit.Click += (_, _) => AddSplitTransponder();
        _btnRetire.Click += (_, _) => ToggleRetired();
        _btnTransfer.Click += (_, _) => TransferTransponder();
        _btnRemove.Click += (_, _) => RemoveTransponder();

        _lblHint.Text = "A retired transponder is out of use but still theirs, with everything timed on it. "
                        + "A split transponder is read only by the split loop partway round the track: fit it to the car it's in.";

        var fitted = new FieldGrid();
        fitted.AddRow("Fitted to:", _cmbFitted);

        var page = new FillPage();
        page.AddFill(_list, _ => UiLayout.AtTextSize(120), gapBelow: UiLayout.RowGap);
        page.Add(fitted, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar().With(_btnAdd, _btnAddSplit, _btnRetire, _btnTransfer, _btnRemove), gapBelow: UiLayout.RowGap);
        page.Add(_lblHint, gapBelow: 0);

        Controls.Add(page);

        AppTheme.ApplyRecursively(this);
        _lblHint.ForeColor = AppTheme.TextMuted;
        FitFittedPicker();

        ShowDriver(null);
    }

    /// <summary>Shows this driver's transponders - their main transponder, the profile's key - or none.</summary>
    public void ShowDriver(string? driver)
    {
        // The same driver again (drawn again after a change): the
        // highlighted transponder stays highlighted.
        var same = string.Equals(driver, _driver, StringComparison.OrdinalIgnoreCase);
        _driver = driver;
        Fill(keep: same && Picked() is { } picked ? KeyOf(picked) : null);
    }

    /// <summary>
    /// The fitted-to drop-down as wide as its longest car, never narrower
    /// than it was made. (22 Oct 2026, page tidy): it was a fixed 220px, and
    /// a long car name was cut short in it.
    /// </summary>
    private void FitFittedPicker()
    {
        var width = Math.Max(UiLayout.AtTextSize(220), Pieces.PickerWidth(_cmbFitted, ref _fittedWidth));

        if (_cmbFitted.Width != width)
        {
            _cmbFitted.Width = width;
        }
    }

    private DriverProfile? Driver() => _driver is null ? null : _profiles.Get(_driver);

    /// <summary>What keeps a row highlighted across a refill: its number, and which loop it's for.</summary>
    private static string KeyOf(Row row) => KeyOf(row.Id, row.IsSplit);

    private static string KeyOf(string id, bool split) => (split ? "split:" : "line:") + id.Trim().ToUpperInvariant();

    /// <summary>The list of entries a row's transponder is in - the line's or the split loop's.</summary>
    private static List<OwnedTransponder> ListFor(DriverProfile profile, bool split) =>
        split ? profile.SplitTransponders ??= new List<OwnedTransponder>() : profile.Transponders ??= new List<OwnedTransponder>();

    /// <summary>The entry to change for a row - a split one is always listed already; a line one is made when it has none (the main one).</summary>
    private static OwnedTransponder EntryFor(DriverProfile profile, Row row) =>
        row.IsSplit
            ? profile.SplitEntry(row.Id) ?? throw new InvalidOperationException($"Split transponder {row.Id} isn't {NameOf(profile)}'s any more.")
            : profile.EntryToChange(row.Id);

    /// <summary>Both of a driver's transponder lists, as they are saved - what an undo compares and puts back.</summary>
    private static string ListsOf(DriverProfile profile) =>
        JsonSerializer.Serialize(new
        {
            Line = profile.Transponders ?? new List<OwnedTransponder>(),
            Split = profile.SplitTransponders ?? new List<OwnedTransponder>(),
        });

    /// <summary>One line of the list.</summary>
    private sealed record Row(string Id, bool IsMain, bool Retired, string CarId, bool IsSplit = false);

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

            // The split loop's, under them: a different decoder, with
            // numbers of its own.
            foreach (var entry in (profile.SplitTransponders ?? new List<OwnedTransponder>()).Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Id)))
            {
                var row = new Row(entry.Id.Trim(), false, entry.Retired, entry.CarId ?? string.Empty, IsSplit: true);
                var status = row.Retired ? "split loop, retired" : "split loop";

                var item = new ListViewItem(new[] { row.Id, CarName(row.CarId), status }) { Tag = row };

                if (row.Retired)
                {
                    item.ForeColor = AppTheme.TextMuted;
                }

                _list.Items.Add(item);
            }
        }

        _list.EndUpdate();
        FitColumns();

        // Kept by number and kind: a split transponder can have the same
        // number as a line one.
        var again = _list.Items.Cast<ListViewItem>()
            .FirstOrDefault(i => KeyOf((Row)i.Tag!) == keep);

        if (again is not null)
        {
            again.Selected = true;
        }
        else if (_list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
        }

        _btnAdd.Enabled = profile is not null;
        _btnAddSplit.Enabled = profile is not null;
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

        // A split transponder goes with its car, not on its own.
        _btnTransfer.Enabled = row is { IsMain: false, IsSplit: false };

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

            _fittedWidth = null;
            FitFittedPicker();
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

        var before = ListsOf(current);
        var lineBefore = JsonSerializer.Serialize(current.Transponders ?? new List<OwnedTransponder>());
        var splitBefore = JsonSerializer.Serialize(current.SplitTransponders ?? new List<OwnedTransponder>());
        var updated = ProfileStore.Snapshot(current);

        try
        {
            change(updated);
        }
        catch (InvalidOperationException ex)
        {
            AppDialog.Show(this, ex.Message, "Transponders", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Fill(keep);
            return false;
        }

        if ((_profiles.DescribeClash(updated, editing: updated.Transponder)
             ?? _profiles.DescribeSplitClash(updated, editing: updated.Transponder)) is { } clash)
        {
            AppDialog.Show(this, clash, "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            // 17 Oct 2026, Bug Hunt 8, A27-7: put the controls back to what
            // is saved, as the branch above does - otherwise "Fitted to" or
            // a retire tick shows a change that was refused.
            Fill(keep);
            return false;
        }

        var after = ListsOf(updated);
        var key = updated.Transponder;
        var who = NameOf(updated);
        var profiles = _profiles;

        _undo.Capture(undoName, () =>
        {
            if (profiles.Get(key) is not { } now || ListsOf(now) != after)
            {
                throw new InvalidOperationException($"{who}'s transponders have been changed again since, and undoing this now would undo that too. Nothing was changed.");
            }

            var restored = ProfileStore.Snapshot(now);
            restored.Transponders = JsonSerializer.Deserialize<List<OwnedTransponder>>(lineBefore) ?? new List<OwnedTransponder>();
            restored.SplitTransponders = JsonSerializer.Deserialize<List<OwnedTransponder>>(splitBefore) ?? new List<OwnedTransponder>();

            if ((profiles.DescribeClash(restored, editing: key) ?? profiles.DescribeSplitClash(restored, editing: key)) is { } taken)
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
            Fill(keep: KeyOf(id, split: false));
            return;
        }

        Change($"Add transponder {id} to {NameOf(profile)}",
            p => p.Transponders.Add(new OwnedTransponder { Id = id, AddedUtc = DateTime.UtcNow }),
            keep: KeyOf(id, split: false));
    }

    /// <summary>
    /// Add Split Transponder: one the split loop reads, fitted to one of
    /// their cars when they have just one in use. The number fills itself in
    /// when the car has just been driven over the split loop and nobody has
    /// that transponder yet.
    /// </summary>
    private void AddSplitTransponder()
    {
        if (Driver() is not { } profile)
        {
            return;
        }

        var seen = SplitSeenRecently?.Invoke() ?? Array.Empty<string>();
        var suggested = seen.FirstOrDefault() ?? string.Empty;

        var prompt = $"A transponder for {NameOf(profile)} that the split loop reads (its own decoder's - not the line's). Its number:";

        if (suggested.Length > 0)
        {
            prompt += $"\n\nFilled in with the last one the split loop read that nobody has yet.";
        }
        else if (SplitSeenRecently is not null)
        {
            prompt += "\n\nTip: drive the car over the split loop first, and its number is filled in here.";
        }

        using var ask = new RentalPromptForm("Add Split Transponder", prompt, suggested);

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
            AppDialog.Show(this, "That transponder number is too long.", "Add Split Transponder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (profile.SplitEntry(id) is not null)
        {
            Fill(keep: KeyOf(id, split: true));
            return;
        }

        // Fitted to their car when there is only one it could be in - the
        // split loop finds a car's row on the board through its car.
        var cars = _cars.OwnedBy(profile).Where(c => !c.Retired).ToList();
        var carId = cars.Count == 1 ? cars[0].Id : string.Empty;

        Change($"Add split transponder {id} to {NameOf(profile)}",
            p => (p.SplitTransponders ??= new List<OwnedTransponder>()).Add(new OwnedTransponder { Id = id, CarId = carId, AddedUtc = DateTime.UtcNow }),
            keep: KeyOf(id, split: true));
    }

    private void ToggleRetired()
    {
        if (Picked() is not { } row || Driver() is not { } profile)
        {
            return;
        }

        var retire = !row.Retired;

        var kind = row.IsSplit ? "split transponder" : "transponder";

        Change(retire ? $"Retire {kind} {row.Id}" : $"Bring back {kind} {row.Id}",
            p =>
            {
                var entry = EntryFor(p, row);
                entry.Retired = retire;
                entry.RetiredUtc = retire ? DateTime.UtcNow : null;
            },
            keep: KeyOf(row));
    }

    private void RemoveTransponder()
    {
        if (Picked() is not { IsMain: false } row || Driver() is not { } profile)
        {
            return;
        }

        var question = row.IsSplit
            ? $"Remove split transponder {row.Id} from {NameOf(profile)}?\n\nThe split loop won't know whose it is any more."
            : $"Remove transponder {row.Id} from {NameOf(profile)}?\n\nLaps already timed on it will no longer count as theirs. "
              + "If it is just no longer used, Retire it instead - that keeps them.";

        if (AppDialog.Show(this, question, row.IsSplit ? "Remove split transponder" : "Remove transponder",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        Change($"Remove {(row.IsSplit ? "split transponder" : "transponder")} {row.Id} from {NameOf(profile)}",
            p => ListFor(p, row.IsSplit).RemoveAll(t => t is not null && string.Equals(t.Id?.Trim(), row.Id, StringComparison.OrdinalIgnoreCase)),
            keep: null);
    }

    /// <summary>Transfer: the picked transponder to another driver for good, with one Undo step for both.</summary>
    private void TransferTransponder()
    {
        if (Picked() is not { IsMain: false, IsSplit: false } row || Driver() is not { } profile)
        {
            return;
        }

        // With the history, so a transponder timed as a car of its own in
        // races already run is asked about before those races count for the
        // new owner. It was left out, and the question was never asked.
        if (TransponderTransferring.Run(this, row.Id, profile, _profiles, _cars, _undo, _history))
        {
            Fill(keep: null);
            Changed?.Invoke();
        }
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

        var kind = row.IsSplit ? "Split transponder" : "Transponder";

        Change(choice.CarId.Length == 0
                ? $"{kind} {row.Id} moves between cars"
                : $"Fit {kind.ToLowerInvariant()} {row.Id} to {choice.Caption}",
            p => EntryFor(p, row).CarId = choice.CarId,
            keep: KeyOf(row));
    }
}
