namespace HeatCaller;

/// <summary>
/// Driver Paid, opened from Who's Racing (11 Nov 2026): a till slip for the
/// desk. While it is open, a double-click on a name in the roster adds that
/// driver here (several picked rows add them all), with what they owe
/// tonight and the total. Paid Cash, Paid Card or Comp then settles everyone
/// on it in one go - the same as the right-click menu, as one undo - and the
/// list empties for the next family at the window.
///
/// Shown non-modally, so the roster behind it can still be clicked. It does
/// not settle anything itself: the page that opened it does (CheckInControl
/// .Settle), and says what it did in its own status line.
/// </summary>
internal sealed class DriverPaidForm : Form
{
    /// <summary>The list shows its headings and at least this many rows before the window scrolls instead.</summary>
    private const int LeastRows = 3;

    private readonly DataGridView _grid = new();
    private readonly Label _lblTotal = new();
    private readonly Button _btnCash = new() { Text = "Paid Cash" };
    private readonly Button _btnCard = new() { Text = "Paid Card" };
    private readonly Button _btnComp = new() { Text = "Comp..." };
    private readonly Button _btnRemove = new() { Text = "Remove" };

    /// <summary>The drivers on the slip, in the order they were added.</summary>
    private readonly List<DriverProfile> _drivers = new();

    private readonly Func<DriverProfile, decimal> _feeFor;
    private readonly Func<DriverProfile, string> _paidWords;
    private readonly Action<IReadOnlyList<DriverProfile>, PaidHow, string> _settle;
    private readonly Func<DriverProfile, DriverProfile> _current;
    private readonly Func<DriverProfile, decimal> _owedFor;

    /// <param name="feeFor">What a driver owes tonight.</param>
    /// <param name="paidWords">How a driver stands now, as the Paid column says it.</param>
    /// <param name="settle">Settles the drivers one way, with a note for a comp.</param>
    /// <param name="current">
    /// The driver's profile on file now. WHY (15 Nov 2026, bug hunt 20): a
    /// phone car pick, an approved edit or Driver Hub saves a new copy of a
    /// profile, and a class ticked after that changes the copy on file, not
    /// the one on the slip - which then showed and settled the old, lower fee.
    /// </param>
    /// <param name="owedFor">
    /// What a driver still owes tonight - the whole fee when they haven't
    /// paid, the rest when they paid before a class was ticked
    /// (DeskMoney.StillOwed). The fee when not given. WHY (15 Nov 2026, bug
    /// hunt 21): the slip's "to take" added up the whole fee, so a driver
    /// owing a $10 extra class read "$25.00 to take".
    /// </param>
    public DriverPaidForm(
        Func<DriverProfile, decimal> feeFor,
        Func<DriverProfile, string> paidWords,
        Action<IReadOnlyList<DriverProfile>, PaidHow, string> settle,
        Func<DriverProfile, DriverProfile>? current = null,
        Func<DriverProfile, decimal>? owedFor = null)
    {
        _feeFor = feeFor;
        _paidWords = paidWords;
        _settle = settle;
        _current = current ?? (profile => profile);
        _owedFor = owedFor ?? feeFor;

        Text = "Driver Paid";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblIntro = new Label
        {
            Text = "Double-click a driver's name on Who's Racing to add them here - Ctrl or Shift to pick several, "
                   + "then double-click one of them. Then mark them all paid at once.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 60, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "fee", HeaderText = "Fee", FillWeight = 25, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "now", HeaderText = "Now", FillWeight = 40, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelected();
                e.Handled = true;
            }
        };

        _lblTotal.AutoSize = true;
        _lblTotal.Font = AppTheme.BoldFont;

        _btnCash.Click += (_, _) => SettleAll(PaidHow.Cash, string.Empty);
        _btnCard.Click += (_, _) => SettleAll(PaidHow.Card, string.Empty);
        _btnRemove.Click += (_, _) => RemoveSelected();

        var compMenu = new ContextMenuStrip();

        foreach (var reason in new[] { "Marshal", "Volunteer", "Junior", "Other" })
        {
            var why = reason == "Other" ? string.Empty : reason;
            var item = new ToolStripMenuItem(reason);
            item.Click += (_, _) => SettleAll(PaidHow.Comp, why);
            compMenu.Items.Add(item);
        }

        AppTheme.StyleContextMenu(compMenu);
        _btnComp.Click += (_, _) => compMenu.Show(_btnComp, new Point(0, _btnComp.Height));
        Disposed += (_, _) => compMenu.Dispose();

        var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();

        var footer = new SplitBar(
            new ButtonBar().With(_btnRemove),
            new ButtonBar(alignRight: true).With(close, _btnComp, _btnCard, _btnCash));

        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.AddFill(_grid, _ => _grid.ColumnHeadersHeight + LeastRows * _grid.RowTemplate.Height + 2, gapBelow: UiLayout.RowGap);
        page.Add(_lblTotal, gapBelow: UiLayout.RowGap);
        page.Add(footer, gapBelow: 0);
        Controls.Add(page);

        CancelButton = close;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(_btnCash, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(_btnCard, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(_btnComp, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnRemove, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(close, AppTheme.ButtonKind.Secondary);
        Pieces.KeepHeadingsWhole(_grid);

        DialogFit.Keep(this, page, width: 560, height: 340);

        RefreshRows();
    }

    /// <summary>The drivers on the slip now, in the order they were added. For the tests.</summary>
    internal IReadOnlyList<DriverProfile> Drivers => _drivers;

    /// <summary>Adds the drivers not already on the slip, and brings the window forward.</summary>
    public void AddDrivers(IEnumerable<DriverProfile> drivers)
    {
        foreach (var profile in drivers)
        {
            if (!_drivers.Any(d => string.Equals(d.Transponder, profile.Transponder, StringComparison.OrdinalIgnoreCase)))
            {
                _drivers.Add(profile);
            }
        }

        RefreshRows();
    }

    /// <summary>Redraws the slip from the drivers on it - their fees and how they stand now.</summary>
    public void RefreshRows()
    {
        TakeCurrentProfiles();
        _grid.Rows.Clear();
        decimal total = 0;
        var settled = 0;

        foreach (var profile in _drivers)
        {
            var fee = _feeFor(profile);
            var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;
            var words = _paidWords(profile);

            // Only what is still owed is money to take - see ToSettle.
            if (AlreadySettled(words))
            {
                settled++;
            }
            else
            {
                total += _owedFor(profile);
            }

            var row = _grid.Rows[_grid.Rows.Add(name, DeskMoney.Money(fee), words)];
            row.Cells["now"].Style.ForeColor = AlreadySettled(words) ? AppTheme.Success : AppTheme.Warning;
        }

        _lblTotal.Text = TotalWords(_drivers.Count, settled, total);

        var any = _drivers.Count > 0;
        _btnCash.Enabled = any;
        _btnCard.Enabled = any;
        _btnComp.Enabled = any;
        _btnRemove.Enabled = any;
    }

    /// <summary>Swaps each driver on the slip for their profile on file now - see the constructor's current.</summary>
    private void TakeCurrentProfiles()
    {
        for (var i = 0; i < _drivers.Count; i++)
        {
            _drivers[i] = _current(_drivers[i]);
        }
    }

    private void RemoveSelected()
    {
        var gone = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).OrderByDescending(i => i).ToList();

        foreach (var index in gone)
        {
            if (index < _drivers.Count)
            {
                _drivers.RemoveAt(index);
            }
        }

        RefreshRows();
    }

    /// <summary>Settles everyone on the slip one way, then empties it for the next.</summary>
    internal void SettleAll(PaidHow how, string note)
    {
        if (_drivers.Count == 0)
        {
            return;
        }

        TakeCurrentProfiles();
        _settle(ToSettle(_drivers, _paidWords), how, note);
        _drivers.Clear();
        RefreshRows();
    }

    /// <summary>Whether the Paid column's words say a driver has settled already: paid by cash or card, or comped. "Owes ..." and nothing at all are not.</summary>
    internal static bool AlreadySettled(string paidWords) =>
        paidWords.Length > 0 && !paidWords.StartsWith("Owes", StringComparison.Ordinal);

    /// <summary>
    /// Who a press of Paid Cash, Paid Card or Comp settles: the drivers on
    /// the slip who have not settled yet.
    ///
    /// WHY (13 Nov 2026, bug hunt 17): it used to be everyone. A family of
    /// three with dad comped earlier as a marshal read "$45.00 to take" with
    /// $30 owed, and Paid Cash then wrote Cash over his comp - a fee more in
    /// the night's takings than was in the till, and the comp reason gone.
    ///
    /// A slip holding ONLY settled drivers is the desk putting right how
    /// somebody paid (cash that was really card), so that still changes them.
    /// </summary>
    internal static IReadOnlyList<DriverProfile> ToSettle(IReadOnlyList<DriverProfile> onTheSlip, Func<DriverProfile, string> paidWords)
    {
        var owing = onTheSlip.Where(profile => !AlreadySettled(paidWords(profile))).ToList();

        return owing.Count > 0 ? owing : onTheSlip.ToList();
    }

    /// <summary>The line under the slip: how many drivers, what there is to take from the ones still owing, and what a press does to the rest.</summary>
    internal static string TotalWords(int drivers, int settled, decimal toTake)
    {
        if (drivers == 0)
        {
            return "Nobody on the slip yet.";
        }

        if (settled == 0)
        {
            return $"{Plural.Of(drivers, "driver")} - {DeskMoney.Money(toTake)} to take";
        }

        return settled >= drivers
            ? $"{Plural.Of(drivers, "driver")} - nothing to take, already settled. Paid or Comp changes how."
            : $"{Plural.Of(drivers, "driver")} - {DeskMoney.Money(toTake)} to take ({settled} already settled, left as {(settled == 1 ? "it is" : "they are")})";
    }
}
