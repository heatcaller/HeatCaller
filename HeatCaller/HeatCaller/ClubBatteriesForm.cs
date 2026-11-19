namespace HeatCaller;

/// <summary>
/// Club Batteries, from the Garage page: the club's own packs, kept with
/// the club's cars. Any driver can run one - put it in their car on their
/// Batteries tab, on Race Control or at check-in - and no loan is recorded:
/// the sessions run on it say who had it. Transfer gives one to a driver
/// for good.
///
/// On the layout pieces (DialogFit): a line on what the window is for, then
/// the packs in a card (18 Nov 2026, dialog jazz-up) - its caption, a short
/// hint, the list drawn as a card per pack (ClubPackList) taking the height,
/// and the buttons that act on the picked pack under it - and Close at the
/// foot. With no club packs, the list's one row says what fills it.
/// </summary>
internal sealed class ClubBatteriesForm : Form
{
    private readonly BatteryPackStore _store;
    private readonly ProfileStore _profiles;
    private readonly UndoService _undo;

    private readonly Label _lblIntro = new() { AutoSize = true };
    private readonly ClubPackList _list = new();
    private readonly Button _btnAdd = new() { Text = "Add Pack..." };
    private readonly Button _btnEdit = new() { Text = "Edit..." };
    private readonly Button _btnRetire = new() { Text = "Retire" };
    private readonly Button _btnTransfer = new() { Text = "Transfer..." };
    private readonly Button _btnDelete = new() { Text = "Delete" };

    public ClubBatteriesForm(BatteryPackStore store, ProfileStore profiles, UndoService undo)
    {
        _store = store;
        _profiles = profiles;
        _undo = undo;

        Text = "Club Batteries";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        _lblIntro.Text = "The club's own packs. Any driver can run one - put it in their car on their Batteries tab, "
                         + "on Race Control or at check-in. Transfer gives one to a driver for good.";

        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => Run(pack => BatteryActions.AddOrEdit(this, _store, _undo, pack, string.Empty) is not null);

        _btnAdd.Click += (_, _) =>
        {
            if (BatteryActions.AddOrEdit(this, _store, _undo, null, string.Empty) is { } added)
            {
                Fill(added.Id);
            }
        };
        _btnEdit.Click += (_, _) => Run(pack => BatteryActions.AddOrEdit(this, _store, _undo, pack, string.Empty) is not null);
        _btnRetire.Click += (_, _) => Run(pack =>
        {
            if (!pack.Retired && !RetireConfirm.Ask(this, $"pack {pack.DisplayName}"))
            {
                return false;
            }

            BatteryActions.SetRetired(_store, _undo, pack, !pack.Retired);
            return true;
        });
        _btnTransfer.Click += (_, _) => Run(pack => BatteryActions.Transfer(this, _store, _profiles, _undo, pack));
        _btnDelete.Click += (_, _) => Run(pack => BatteryActions.Delete(this, _store, _undo, pack));

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.Cancel };

        // The packs and the buttons that act on them in one card, the list
        // taking the height the card has.
        var packsCard = new Section("The club's packs");
        packsCard.Add(new Label
        {
            Text = "Pick one to change it. Each says how many sessions it has run and whose car it's in now.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        });
        packsCard.AddFill(_list, () => UiLayout.AtTextSize(160));
        packsCard.Add(new ButtonBar().With(_btnAdd, _btnEdit, _btnRetire, _btnTransfer, _btnDelete), gapBelow: 0);

        var page = new FillPage();
        page.Add(_lblIntro, stretch: false, gapBelow: UiLayout.RowGap);
        page.AddFill(packsCard, packsCard.LeastHeight);
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);
        _lblIntro.ForeColor = AppTheme.TextMuted;

        // The rows as tall as the faces the theme pass left in use.
        _list.Remeasure();

        Fill(null);

        // 100px taller than it was (440): the card's caption, hint and insets
        // come out of that, and a card row is taller than a grid line.
        DialogFit.Keep(this, page, width: 640, height: 540);
    }

    private BatteryPack? Picked() =>
        _list.SelectedItem is ClubPackList.Row { Pack: { } pack } ? _store.Get(pack.Id) : null;

    /// <summary>Runs an action on the highlighted pack, and fills the list again when it did something.</summary>
    private void Run(Func<BatteryPack, bool> action)
    {
        if (Picked() is not { } pack)
        {
            return;
        }

        if (action(pack))
        {
            Fill(pack.Id);
        }
    }

    private void Fill(string? select)
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var pack in _store.ClubPacks)
        {
            var drivers = _store.InCarsOf(pack.Id)
                .Select(key => _profiles.Get(key) is { } p ? BatteryActions.NameOf(p) : key)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var sessions = _store.SessionsOn(pack.Id);

            // What the grid's columns said, on one line under the name.
            var parts = new List<string>
            {
                pack.Chemistry,
                sessions == 1 ? "1 session" : $"{sessions} sessions",
                drivers.Count == 0 ? "in nobody's car" : "in the car of " + string.Join(", ", drivers),
            };

            if (!string.IsNullOrWhiteSpace(pack.Notes))
            {
                parts.Add(pack.Notes.Replace('\r', ' ').Replace('\n', ' '));
            }

            var row = new ClubPackList.Row(
                pack,
                pack.DisplayName,
                ClubPackList.BadgeOf(pack.Chemistry),
                string.Join("  ·  ", parts.Where(part => !string.IsNullOrWhiteSpace(part))),
                pack.Retired);

            _list.Items.Add(row);

            if (pack.Id == select)
            {
                _list.SelectedItem = row;
            }
        }

        if (_list.Items.Count == 0)
        {
            _list.Items.Add(new ClubPackList.Row(null, "No club packs yet",
                string.Empty, "Click Add Pack. Each one shows here with its sessions and whose car it's in.", false));
        }

        _list.EndUpdate();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var pack = Picked();

        _btnEdit.Enabled = pack is not null;
        _btnRetire.Enabled = pack is not null;
        _btnTransfer.Enabled = pack is not null;
        _btnDelete.Enabled = pack is not null;

        var text = pack is { Retired: true } ? "Bring Back" : "Retire";

        if (!string.Equals(_btnRetire.Text, AppTheme.Caption(text), StringComparison.Ordinal))
        {
            _btnRetire.Text = text;
            AppTheme.StyleButton(_btnRetire, AppTheme.ButtonKind.Secondary);
        }
    }
}
