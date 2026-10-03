namespace HeatCaller;

/// <summary>
/// Club Batteries, from the Garage page: the club's own packs, kept with
/// the club's cars. Any driver can run one - put it in their car on their
/// Batteries tab, on Race Control or at check-in - and no loan is recorded:
/// the sessions run on it say who had it. Transfer gives one to a driver
/// for good.
///
/// On the layout pieces (DialogFit): the list taking the height, the
/// buttons under it and Close at the foot.
/// </summary>
internal sealed class ClubBatteriesForm : Form
{
    private readonly BatteryPackStore _store;
    private readonly ProfileStore _profiles;
    private readonly UndoService _undo;

    private readonly Label _lblIntro = new() { AutoSize = true };
    private readonly ListView _list = new();
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

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.Columns.Add("Pack");
        _list.Columns.Add("Type");
        _list.Columns.Add(new ColumnHeader { Text = "Sessions", TextAlign = HorizontalAlignment.Right });
        _list.Columns.Add("In the car of");
        _list.Columns.Add("Notes");
        _list.ClientSizeChanged += (_, _) => FitColumns();
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
            BatteryActions.SetRetired(_store, _undo, pack, !pack.Retired);
            return true;
        });
        _btnTransfer.Click += (_, _) => Run(pack => BatteryActions.Transfer(this, _store, _profiles, _undo, pack));
        _btnDelete.Click += (_, _) => Run(pack => BatteryActions.Delete(this, _store, _undo, pack));

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        page.Add(_lblIntro, stretch: false, gapBelow: UiLayout.RowGap);
        page.AddFill(_list, _ => UiLayout.AtTextSize(160), gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar().With(_btnAdd, _btnEdit, _btnRetire, _btnTransfer, _btnDelete), gapBelow: UiLayout.SectionGap);
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);
        _lblIntro.ForeColor = AppTheme.TextMuted;

        Fill(null);

        DialogFit.Keep(this, page, width: 640, height: 440);
    }

    private BatteryPack? Picked() =>
        _list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is BatteryPack pack ? _store.Get(pack.Id) : null;

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

            var item = new ListViewItem(new[]
            {
                pack.DisplayName + (pack.Retired ? "  (retired)" : string.Empty),
                pack.Chemistry,
                _store.SessionsOn(pack.Id).ToString(),
                drivers.Count == 0 ? "-" : string.Join(", ", drivers),
                pack.Notes.Replace('\r', ' ').Replace('\n', ' '),
            })
            { Tag = pack };

            if (pack.Retired)
            {
                item.ForeColor = AppTheme.TextMuted;
            }

            _list.Items.Add(item);

            if (pack.Id == select)
            {
                item.Selected = true;
            }
        }

        if (_list.Items.Count == 0)
        {
            _list.Items.Add(new ListViewItem(new[] { "No club packs yet - click Add Pack.", string.Empty, string.Empty, string.Empty, string.Empty }));
        }

        _list.EndUpdate();
        FitColumns();
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

    private void FitColumns()
    {
        if (_list.Columns.Count < 5)
        {
            return;
        }

        var width = Math.Max(1, _list.ClientSize.Width);
        _list.Columns[0].Width = width * 22 / 100;
        _list.Columns[1].Width = width * 13 / 100;
        _list.Columns[2].Width = width * 12 / 100;
        _list.Columns[3].Width = width * 25 / 100;
        _list.Columns[4].Width = Math.Max(20, width - _list.Columns[0].Width - _list.Columns[1].Width - _list.Columns[2].Width - _list.Columns[3].Width);
    }
}
