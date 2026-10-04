using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A car changing hands for good - sold or given, which is the two
/// drivers' business and not asked: Transfer. Not a loan (Lend Out), which
/// is for one race day and ends by itself.
///
/// What goes with the car: its photos, details, notes and setups, which
/// all belong to the car, and any of the old owner's transponders fitted
/// to it that they say go too. What stays: the races already run, filed
/// under the driver who ran them, and the old owner's main transponder,
/// which is their key (DriverProfile.Transponder) and never moves.
///
/// The split-loop transponders fitted to it (DriverProfile.SplitTransponders)
/// go with it to a driver without asking - they only ever tell the split
/// loop which car crossed it. Going to the club, they stay with the old
/// owner, moving between their cars.
/// </summary>
internal static class CarTransferring
{
    /// <summary>What to do: the car, the new owner (their main transponder, or empty for the club) and the old owner's transponders that go with it.</summary>
    public sealed record Plan(string CarId, string ToTransponder, IReadOnlyList<string> Moving);

    /// <summary>The old owner's transponders fitted to this car that can go with it - in use, and not their main one.</summary>
    public static IReadOnlyList<string> MovableTransponders(Car car, DriverProfile? owner) =>
        owner is null
            ? Array.Empty<string>()
            : (owner.Transponders ?? new List<OwnedTransponder>())
                .Where(t => t is not null
                            && !t.Retired
                            && string.Equals(t.CarId, car.Id, StringComparison.Ordinal)
                            && !string.Equals(t.Id?.Trim(), owner.Transponder?.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Id.Trim())
                .ToList();

    private static string NameOf(DriverProfile? profile, string fallback) =>
        profile is null ? fallback
        : string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder
        : profile.DriverName.Trim();

    /// <summary>The fields a transfer changes on a driver, for its undo to check and put back.</summary>
    private sealed record DriverPart(string Transponders, string Split, string CarModel)
    {
        public static DriverPart Of(DriverProfile profile) => new(
            JsonSerializer.Serialize(profile.Transponders ?? new List<OwnedTransponder>()),
            JsonSerializer.Serialize(profile.SplitTransponders ?? new List<OwnedTransponder>()),
            profile.CarModel ?? string.Empty);

        /// <summary>Both transponder lists as they are saved - what the undo checks are unchanged.</summary>
        public string Lists => Transponders + "|" + Split;

        public void PutBack(DriverProfile profile)
        {
            profile.Transponders = JsonSerializer.Deserialize<List<OwnedTransponder>>(Transponders) ?? new List<OwnedTransponder>();
            profile.SplitTransponders = JsonSerializer.Deserialize<List<OwnedTransponder>>(Split) ?? new List<OwnedTransponder>();

            if (CarModel.Length > 0)
            {
                profile.SetActiveCar(CarModel);
            }
            else
            {
                profile.CarModel = string.Empty;
            }
        }
    }

    /// <summary>
    /// Transfers the car, with one Undo step for all of it. Null when done;
    /// otherwise why it was refused, and nothing has changed.
    /// </summary>
    public static string? Apply(Plan plan, CarStore cars, ProfileStore profiles, UndoService? undo)
    {
        if (cars.Get(plan.CarId) is not { } car)
        {
            return "That car is no longer in the garage.";
        }

        var from = car.IsClubCar ? null : profiles.Get(car.OwnerTransponder);
        var fromKey = from?.Transponder;

        DriverProfile? to = null;

        if (plan.ToTransponder.Length > 0)
        {
            to = profiles.Get(plan.ToTransponder);

            if (to is null)
            {
                return "That driver is no longer on file.";
            }
        }

        var toKey = to?.Transponder ?? string.Empty;

        if (to is null ? car.IsClubCar : string.Equals(toKey, fromKey, StringComparison.OrdinalIgnoreCase))
        {
            return $"{car.DisplayName} is already theirs.";
        }

        // Only the old owner's own transponders, fitted to this car, and
        // only to a driver: the club has no transponders of its own to keep
        // them under.
        var movable = MovableTransponders(car, from);
        var moving = to is null
            ? new List<string>()
            : plan.Moving
                .Select(id => id.Trim())
                .Where(id => movable.Contains(id, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        foreach (var id in moving)
        {
            if (!string.Equals(profiles.OwnerOf(id)?.Transponder, fromKey, StringComparison.OrdinalIgnoreCase))
            {
                return $"Transponder {id} isn't {NameOf(from, "the old owner")}'s any more. Nothing was changed.";
            }
        }

        var fromName = NameOf(from, car.IsClubCar ? "the club" : car.OwnerTransponder);
        var toName = NameOf(to, "the club");

        var carBefore = car.CloneForUndo();
        var carAfter = car.CloneForUndo();
        carAfter.OwnerTransponder = toKey;

        // A loan was the old owner's to give; the new owner lends it, or not.
        carAfter.LoanedToTransponder = string.Empty;
        carAfter.LoanedForEventId = string.Empty;
        carAfter.Transfers.Add(new CarTransfer
        {
            FromTransponder = car.OwnerTransponder?.Trim() ?? string.Empty,
            ToTransponder = toKey,
            FromName = car.IsClubCar ? string.Empty : fromName,
            ToName = to is null ? string.Empty : toName,
            AtUtc = DateTime.UtcNow,
        });

        // What the car's change is, for the two drivers' car names to follow
        // it (GarageSync.SyncNames) - and the other way round, for going back.
        var forward = new[] { new CarChange(car.Id, CarFacts.Of(carBefore), CarFacts.Of(carAfter)) };
        var back = new[] { new CarChange(car.Id, CarFacts.Of(carAfter), CarFacts.Of(carBefore)) };

        var fromBefore = from is null ? null : DriverPart.Of(from);
        var toBefore = to is null ? null : DriverPart.Of(to);

        // The split-loop transponders fitted to the car: they go with it to
        // a driver (copies, so the old owner's list isn't touched by it).
        var splitMoving = to is null || from is null
            ? new List<OwnedTransponder>()
            : (from.SplitTransponders ?? new List<OwnedTransponder>())
                .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Id) && string.Equals(t.CarId, car.Id, StringComparison.Ordinal))
                .Select(t => new OwnedTransponder { Id = t.Id.Trim(), CarId = car.Id, Retired = t.Retired, RetiredUtc = t.RetiredUtc, AddedUtc = DateTime.UtcNow })
                .ToList();
        // 1. The car changes hands. In the app this alone puts both drivers'
        //    car names in step (CarStore.Changed); the steps below read the
        //    profiles afresh for that reason.
        cars.Upsert(carAfter);

        // 2. The old owner: the transponders that went are no longer theirs,
        //    and any that stayed behind move between their cars now.
        if (fromKey is not null && profiles.Get(fromKey) is { } fromNow)
        {
            var updated = ProfileStore.Snapshot(fromNow);
            updated.Transponders ??= new List<OwnedTransponder>();
            updated.Transponders.RemoveAll(t => t is not null && moving.Contains(t.Id?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase));

            foreach (var entry in updated.Transponders.Where(t => t is not null && string.Equals(t.CarId, car.Id, StringComparison.Ordinal)))
            {
                entry.CarId = string.Empty;
            }

            // Split transponders: gone with the car to a driver; kept,
            // moving between their cars, when it went to the club.
            updated.SplitTransponders ??= new List<OwnedTransponder>();
            updated.SplitTransponders.RemoveAll(t => t is not null && splitMoving.Any(m => string.Equals(m.Id, t.Id?.Trim(), StringComparison.OrdinalIgnoreCase)));

            foreach (var entry in updated.SplitTransponders.Where(t => t is not null && string.Equals(t.CarId, car.Id, StringComparison.Ordinal)))
            {
                entry.CarId = string.Empty;
            }

            profiles.Upsert(updated);
        }

        // 3. The new owner: the transponders that came with it, fitted to it,
        //    and - when they had no car to be announced in - this one.
        if (to is not null && profiles.Get(toKey) is { } toNow)
        {
            var updated = ProfileStore.Snapshot(toNow);
            updated.Transponders ??= new List<OwnedTransponder>();

            foreach (var id in moving)
            {
                updated.Transponders.Add(new OwnedTransponder { Id = id, CarId = car.Id, AddedUtc = DateTime.UtcNow });
            }

            updated.SplitTransponders ??= new List<OwnedTransponder>();

            foreach (var entry in splitMoving.Where(m => updated.SplitEntry(m.Id) is null))
            {
                updated.SplitTransponders.Add(entry);
            }

            if (string.IsNullOrWhiteSpace(updated.CarModel))
            {
                updated.SetActiveCar(carAfter.DisplayName);
            }

            if (profiles.DescribeClash(updated, editing: toKey) is { } clash)
            {
                // Checked above, so only ever a change made in between: put
                // the car and the old owner back as they were.
                cars.Upsert(carBefore.CloneForUndo());

                if (fromKey is not null && profiles.Get(fromKey) is { } oldOwner)
                {
                    var restored = ProfileStore.Snapshot(oldOwner);
                    fromBefore!.PutBack(restored);
                    profiles.Upsert(restored);
                }

                SyncBoth(fromKey, toKey, profiles, cars, back);
                return $"{clash} Nothing was changed.";
            }

            profiles.Upsert(updated);
        }

        SyncBoth(fromKey, toKey, profiles, cars, forward);

        var carAfterJson = JsonSerializer.Serialize(cars.Get(carBefore.Id));
        var fromAfter = fromKey is not null && profiles.Get(fromKey) is { } fa ? DriverPart.Of(fa) : null;
        var toAfter = to is not null && profiles.Get(toKey) is { } ta ? DriverPart.Of(ta) : null;

        // Captured once it has all been written - a transfer refused half
        // way (below) has put everything back itself and leaves no step.
        undo?.Capture($"Transfer {car.DisplayName} to {toName}", () =>
        {
            if (cars.Get(carBefore.Id) is not { } nowCar
                || JsonSerializer.Serialize(nowCar) != carAfterJson
                || (fromKey is not null && (profiles.Get(fromKey) is not { } f || DriverPart.Of(f).Lists != fromAfter?.Lists))
                || (to is not null && (profiles.Get(toKey) is not { } t || DriverPart.Of(t).Lists != toAfter?.Lists)))
            {
                throw new InvalidOperationException(
                    $"{carBefore.DisplayName}, or a transponder that went with it, has been changed since, and undoing the transfer now would undo that too. Nothing was changed.");
            }

            // The car first: once it is the old owner's again, giving them
            // back its name can't make a second car of it (GarageSync.AddCars).
            cars.Upsert(carBefore.CloneForUndo());

            if (to is not null && profiles.Get(toKey) is { } toNow)
            {
                var restored = ProfileStore.Snapshot(toNow);
                toBefore!.PutBack(restored);
                profiles.Upsert(restored);
            }

            if (fromKey is not null && profiles.Get(fromKey) is { } fromNow)
            {
                var restored = ProfileStore.Snapshot(fromNow);
                fromBefore!.PutBack(restored);
                profiles.Upsert(restored);
            }

            SyncBoth(fromKey, toKey, profiles, cars, back);
        });

        return null;
    }

    /// <summary>Both drivers' car names in step with the garage - done here as well as by the app's own listener, so it holds wherever this is called from.</summary>
    private static void SyncBoth(string? fromKey, string toKey, ProfileStore profiles, CarStore cars, IReadOnlyList<CarChange> changes)
    {
        // Nothing changed that the lists follow: an empty list means "make
        // them again from the garage" to SyncNames, which is for startup only.
        if (changes.Count == 0)
        {
            return;
        }

        foreach (var key in new[] { fromKey, toKey })
        {
            if (!string.IsNullOrEmpty(key)
                && profiles.Get(key) is { } profile
                && GarageSync.SyncNames(profile, cars, changes) is { } updated)
            {
                profiles.Upsert(updated);
            }
        }
    }

    /// <summary>Asks who it goes to, and transfers it. True when it was.</summary>
    public static bool Run(IWin32Window owner, Car car, CarStore cars, ProfileStore profiles, UndoService undo)
    {
        using var form = new CarTransferForm(car, profiles);

        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            return false;
        }

        if (Apply(new Plan(car.Id, form.ToTransponder, form.Moving), cars, profiles, undo) is { } refusal)
        {
            AppDialog.Show(owner, refusal, "Transfer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }
}

/// <summary>
/// "Transfer Maya's MR-03": who it goes to - a driver on file, or the club -
/// and which of the transponders fitted to it go too. Only asks;
/// CarTransferring.Apply makes the change.
///
/// On the layout pieces (DialogFit), as BelongsToForm: the question at the
/// top, the search and the drivers taking the height, the transponders and
/// what will happen under them, the buttons at the foot.
/// </summary>
internal sealed class CarTransferForm : Form
{
    /// <summary>The list shows at least this many before the window scrolls instead.</summary>
    private const int LeastRows = 5;

    /// <summary>The club's line in the list.</summary>
    private const string ClubKey = "\u0001club";

    private readonly Car _car;
    private readonly ProfileStore _profiles;
    private readonly DriverProfile? _owner;

    private readonly TextBox _txtFind = new();
    private readonly ListBox _lstTo = new();
    // The app's own list, like every other checked list: its tick boxes, and
    // a drag down it ticking or unticking each row it passes.
    private readonly CheckedListBox _lstTransponders = new ThemedCheckedListBox();
    private readonly Label _lblOutcome = new() { AutoSize = true };
    private readonly Button _btnTransfer = new();

    /// <summary>The new owner's main transponder, or empty for the club. Set on OK.</summary>
    public string ToTransponder { get; private set; } = string.Empty;

    /// <summary>The old owner's transponders that go with the car. Set on OK.</summary>
    public IReadOnlyList<string> Moving { get; private set; } = Array.Empty<string>();

    public CarTransferForm(Car car, ProfileStore profiles)
    {
        _car = car;
        _profiles = profiles;
        _owner = car.IsClubCar ? null : profiles.Get(car.OwnerTransponder);

        var ownerName = _owner is null
            ? car.IsClubCar ? "the club" : car.OwnerTransponder
            : NameOf(_owner);

        Text = $"Transfer {car.DisplayName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblWhat = new Label
        {
            Text = $"Give {car.DisplayName} to someone else for good, sold or given. It's {ownerName}'s now. "
                   + "To lend it for one race day, use Lend Out instead.",
            AutoSize = true,
        };

        _txtFind.PlaceholderText = "name, number or transponder";
        _txtFind.TextChanged += (_, _) => FillList();

        _lstTo.IntegralHeight = false;
        _lstTo.SelectedIndexChanged += (_, _) => OnPicked();
        _lstTo.DoubleClick += (_, _) => Transfer();

        _lstTransponders.CheckOnClick = true;
        _lstTransponders.IntegralHeight = false;
        var movable = CarTransferring.MovableTransponders(car, _owner);

        foreach (var id in movable)
        {
            _lstTransponders.Items.Add(id, isChecked: true);
        }

        // After the fill, which raises it for every line. Raised before the
        // tick changes, so the count is worked out from the new state.
        _lstTransponders.ItemCheck += (_, e) =>
        {
            var going = _lstTransponders.CheckedItems.Count
                        + (e.NewValue == CheckState.Checked ? 1 : 0)
                        - (e.CurrentValue == CheckState.Checked ? 1 : 0);
            UpdateOutcome(going);
        };

        _btnTransfer.Text = "Transfer";
        _btnTransfer.Click += (_, _) => Transfer();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var find = new FieldGrid();
        find.AddRow("Find:", _txtFind, stretch: true);

        var page = new FillPage();
        page.Add(lblWhat, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new Label { Text = "Who gets it?", AutoSize = true, Font = AppTheme.BoldFont }, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(find, gapBelow: UiLayout.HintGap);
        page.AddFill(_lstTo, _ => LeastRows * _lstTo.ItemHeight + 4, gapBelow: UiLayout.RowGap);

        if (movable.Count > 0)
        {
            page.Add(new Label { Text = "Transponders fitted to it - tick the ones that go with it:", AutoSize = true, Font = AppTheme.BoldFont },
                stretch: false, gapBelow: UiLayout.HintGap);
            page.Add(_lstTransponders, gapBelow: UiLayout.HintGap);
            _lstTransponders.Height = Math.Min(movable.Count, 4) * _lstTransponders.ItemHeight + 6;
            page.Add(new Label
            {
                Text = $"One left unticked stays {ownerName}'s, and moves between their cars from now on.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            }, stretch: false, gapBelow: UiLayout.RowGap);
        }

        // Their main transponder is how their results are filed, so it never
        // goes - said, so nobody wonders why it isn't on the list.
        if (_owner is not null
            && _owner.TransponderEntry(_owner.Transponder) is { Retired: false } main
            && string.Equals(main.CarId, car.Id, StringComparison.Ordinal))
        {
            page.Add(new Label
            {
                Text = $"{_owner.Transponder} is {ownerName}'s main transponder, which their results are filed under, so it stays theirs.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            }, stretch: false, gapBelow: UiLayout.RowGap);
        }

        page.Add(_lblOutcome, stretch: false);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnTransfer), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnCancel;
        AcceptButton = _btnTransfer;

        FillList();

        AppTheme.ApplyRecursively(this);
        _lblOutcome.ForeColor = AppTheme.TextMuted;

        DialogFit.Keep(this, page, width: 520, height: 560);
    }

    /// <summary>One line of the list: a driver, or the club.</summary>
    private sealed record Choice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

    private Choice? Picked => _lstTo.SelectedItem as Choice;

    private bool ToClub => Picked is { Key: ClubKey };

    /// <summary>The club, then every driver but the owner, matching what is typed.</summary>
    private void FillList()
    {
        var find = _txtFind.Text.Trim();
        var picked = Picked?.Key;

        bool Has(string? value) => !string.IsNullOrEmpty(value) && value.Contains(find, StringComparison.OrdinalIgnoreCase);

        _lstTo.BeginUpdate();
        _lstTo.Items.Clear();

        if (!_car.IsClubCar && (find.Length == 0 || "the club".Contains(find, StringComparison.OrdinalIgnoreCase)))
        {
            _lstTo.Items.Add(new Choice(ClubKey, "The club"));
        }

        foreach (var profile in _profiles.All
                     .Where(p => !string.Equals(p.Transponder, _owner?.Transponder, StringComparison.OrdinalIgnoreCase))
                     .Where(p => find.Length == 0
                                 || Has(p.DriverName) || Has(p.AnnouncerName) || Has(p.RaceNumber)
                                 || p.AllTransponderIds.Any(Has))
                     .OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase))
        {
            var number = string.IsNullOrWhiteSpace(profile.RaceNumber) ? string.Empty : $"  #{profile.RaceNumber.Trim()}";
            _lstTo.Items.Add(new Choice(profile.Transponder, $"{NameOf(profile)}{number}  ·  {profile.Transponder}"));
        }

        _lstTo.EndUpdate();

        var again = _lstTo.Items.Cast<Choice>().FirstOrDefault(c => string.Equals(c.Key, picked, StringComparison.OrdinalIgnoreCase));

        if (again is not null)
        {
            _lstTo.SelectedItem = again;
        }
        else if (_lstTo.Items.Count == 1)
        {
            _lstTo.SelectedIndex = 0;
        }

        OnPicked();
    }

    private void OnPicked()
    {
        var choice = Picked;

        // The club keeps no transponders: any fitted to the car stay with
        // the old owner.
        _lstTransponders.Enabled = choice is not null && !ToClub;

        _btnTransfer.Enabled = choice is not null;
        _btnTransfer.Text = choice is null ? "Transfer" : ToClub ? "Transfer to the Club" : $"Transfer to {NameOf(_profiles.Get(choice.Key)!)}";

        UpdateOutcome();
    }

    /// <summary>The line saying what pressing the button will do.</summary>
    private void UpdateOutcome(int? goingNow = null)
    {
        if (IsDisposed)
        {
            return;
        }

        if (Picked is not { } choice)
        {
            _lblOutcome.Text = "Pick who it goes to.";
            return;
        }

        var who = ToClub ? "the club's" : $"{NameOf(_profiles.Get(choice.Key)!)}'s";
        var going = ToClub ? 0 : goingNow ?? _lstTransponders.CheckedItems.Count;
        var transponders = going == 0 ? string.Empty : going == 1 ? ", and so does its transponder" : $", and so do {going} transponders";

        _lblOutcome.Text = $"{_car.DisplayName} becomes {who}. Its photos, notes and setups go with it{transponders}. "
                           + "Races already run stay with whoever ran them.";
    }

    private void Transfer()
    {
        if (Picked is not { } choice || (choice.Key != ClubKey && _profiles.Get(choice.Key) is null))
        {
            return;
        }

        ToTransponder = ToClub ? string.Empty : choice.Key;
        Moving = ToClub ? Array.Empty<string>() : _lstTransponders.CheckedItems.Cast<string>().ToList();

        DialogResult = DialogResult.OK;
        Close();
    }
}
