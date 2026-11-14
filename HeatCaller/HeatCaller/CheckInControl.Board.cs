namespace HeatCaller;

/// <summary>
/// Who's Racing as a pit board (13 Nov 2026, the rework Asher picked from four
/// mock-ups - A, "Pit Board"): where each driver is through the night's gates,
/// said once at the top for everybody and once on each row.
///
/// The gates are checked in, tech (when the club runs it), paid (on a night
/// that charges) and papers (while the club checks them). A driver through
/// every gate the night has is ready to race.
///
/// The roster's filters live here too: a chip per class in place of the old
/// drop-down, a chip per gate in place of "Only those not through tech", and
/// the search box, which is also where a USB scanner types.
/// </summary>
public sealed partial class CheckInControl
{
    /// <summary>One gate, for one driver: not run tonight, through, still to do, or failed (tech only).</summary>
    internal enum Gate { NotRun, Done, Open, Failed }

    /// <summary>Where one driver is through tonight's gates.</summary>
    internal readonly record struct Readiness(bool Here, Gate Tech, Gate Paid, Gate Papers)
    {
        private IEnumerable<Gate> Run => new[] { Tech, Paid, Papers }.Where(g => g != Gate.NotRun);

        /// <summary>Checked in and through every gate tonight has.</summary>
        public bool Ready => Here && Run.All(g => g == Gate.Done);

        /// <summary>How many gates tonight has, checking in included.</summary>
        public int Gates => 1 + Run.Count();

        /// <summary>How many of them this driver is through.</summary>
        public int Through => (Here ? 1 : 0) + Run.Count(g => g == Gate.Done);
    }

    /// <summary>What the roster shows, besides the class: everybody, or the drivers still waiting at one gate.</summary>
    internal enum RosterShow { Everyone, NotHere, NeedsTech, Owes, Papers, Ready }

    /// <summary>The class picked on the chips, or null for every class.</summary>
    private string? _classFilter;

    private RosterShow _show = RosterShow.Everyone;

    /// <summary>What the search box is filtering by, once the typing has paused - see _searchPause.</summary>
    private string _search = string.Empty;

    private readonly ChipRow _classChips = new() { AccessibleDescription = "Class" };
    private readonly ChipRow _showChips = new() { AccessibleDescription = "Show" };
    private readonly ReadinessStrip _strip = new();

    /// <summary>
    /// The search waits for the typing to pause. A USB scanner types a whole
    /// QR code and Enter in a few milliseconds, so the list does not flicker
    /// through a filter for every character of a code nobody typed.
    /// </summary>
    private readonly System.Windows.Forms.Timer _searchPause = new() { Interval = 200 };

    /// <summary>The class filter, for the rest of the page.</summary>
    private string? ClassFilter => _classFilter;

    /// <summary>For the tests: the class and gate picked, and the search.</summary>
    internal (string? Class, RosterShow Show, string Search) Filters => (_classFilter, _show, _search);

    /// <summary>Where a driver is through tonight's gates.</summary>
    internal Readiness ReadinessOf(DriverProfile profile)
    {
        var here = _checkIns.IsCheckedIn(profile.Transponder);

        var tech = _tech is null
            ? Gate.NotRun
            : TechFor(profile) switch
            {
                { IsCleared: true } => Gate.Done,
                not null => Gate.Failed,
                _ => Gate.Open,
            };

        var paid = Gate.NotRun;

        if (_payments is not null && TonightsFees() is { Charges: true } fees)
        {
            var payment = _payments.Get(fees.Day.Id, profile.Transponder);
            var settled = (payment?.How ?? PaidHow.Owing) != PaidHow.Owing || FeeFor(profile, fees.Day, ClassNamed) <= 0;
            paid = settled ? Gate.Done : Gate.Open;
        }

        var papers = ChecksPapers
            ? PapersProblems(profile).Count == 0 ? Gate.Done : Gate.Open
            : Gate.NotRun;

        return new Readiness(here, tech, paid, papers);
    }

    /// <summary>Whether a driver is one the gate filter is showing.</summary>
    private static bool Shows(RosterShow show, Readiness r) => show switch
    {
        RosterShow.NotHere => !r.Here,
        RosterShow.NeedsTech => r.Tech is Gate.Open or Gate.Failed,
        RosterShow.Owes => r.Paid == Gate.Open,
        RosterShow.Papers => r.Papers == Gate.Open,
        RosterShow.Ready => r.Ready,
        _ => true,
    };

    /// <summary>Everyone in the class picked - before the gate filter and the search.</summary>
    private List<DriverProfile> InClass() =>
        _profiles.All.Where(p => _classFilter is null || p.RunsClass(_classFilter)).ToList();

    /// <summary>Whether a driver's name or transponder has the search in it.</summary>
    private static bool Matches(DriverProfile profile, string search) =>
        search.Length == 0
        || (profile.DriverName ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase)
        || profile.Transponder.Contains(search, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------ the filters

    /// <summary>Builds the chips' and the search box's wiring. Called once, from BuildLayout.</summary>
    private void BuildFilters()
    {
        _classChips.PickedChanged += key =>
        {
            _classFilter = key.Length == 0 ? null : key;
            _settings.LastCheckInClassFilter = key;
            _settings.Save();
            RefreshDriverList();
        };

        _showChips.PickedChanged += key =>
        {
            _show = Enum.TryParse<RosterShow>(key, out var show) ? show : RosterShow.Everyone;
            RefreshDriverList();
        };

        // A card on the strip shows the drivers still waiting at its gate; a
        // second click on it shows everyone again.
        _strip.TileClicked += key =>
        {
            if (!Enum.TryParse<RosterShow>(key, out var show))
            {
                return;
            }

            ShowOnly(_show == show ? RosterShow.Everyone : show);
        };

        _searchPause.Tick += (_, _) =>
        {
            _searchPause.Stop();
            ApplySearch();
        };

        _txtScan.TextChanged += (_, _) =>
        {
            _searchPause.Stop();

            // Cleared - by Enter, or by hand: at once, so the whole list is back.
            if (_txtScan.TextLength == 0)
            {
                ApplySearch();
            }
            else
            {
                _searchPause.Start();
            }
        };

        // Escape clears the search.
        _txtScan.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _txtScan.TextLength > 0)
            {
                _txtScan.Clear();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
    }

    /// <summary>Shows the drivers waiting at one gate (or everyone), as the chip for it does.</summary>
    internal void ShowOnly(RosterShow show)
    {
        _show = show;
        _showChips.Picked = show.ToString();
        RefreshDriverList();
    }

    /// <summary>Picks a class on the chips (null for every class), as a click on its chip does.</summary>
    internal void ShowClass(string? className)
    {
        _classFilter = string.IsNullOrEmpty(className) ? null : className;
        _classChips.Picked = _classFilter ?? string.Empty;
        RefreshDriverList();
    }

    /// <summary>
    /// The search as typed - unless it is a QR check-in on its way in, which
    /// is not a name to look for.
    /// </summary>
    private void ApplySearch()
    {
        var text = _txtScan.Text.Trim();

        if (text.Length > 0 && (QrBadgeRenderer.ParsePayload(text) is not null || RelayCheckIn.IdFromScan(text) is not null))
        {
            text = string.Empty;
        }

        if (string.Equals(text, _search, StringComparison.Ordinal))
        {
            return;
        }

        _search = text;
        RefreshDriverList();
    }

    /// <summary>
    /// The class chips: every class that has a driver, with how many, and the
    /// one picked even when it has nobody left in it.
    /// </summary>
    private void RefreshClassChips()
    {
        var chips = new List<ChipRow.Chip> { new(string.Empty, "All classes", _profiles.All.Count) };

        foreach (var raceClass in _raceClasses.All)
        {
            var count = _profiles.All.Count(p => p.RunsClass(raceClass.Name));

            if (count > 0 || string.Equals(raceClass.Name, _classFilter, StringComparison.OrdinalIgnoreCase))
            {
                chips.Add(new ChipRow.Chip(raceClass.Name, raceClass.Name, count));
            }
        }

        // A class that has gone - renamed or deleted elsewhere - is let go of.
        if (_classFilter is not null && chips.All(c => !string.Equals(c.Key, _classFilter, StringComparison.OrdinalIgnoreCase)))
        {
            _classFilter = null;
        }

        _classChips.SetChips(chips);
        _classChips.Picked = chips.FirstOrDefault(c => string.Equals(c.Key, _classFilter ?? string.Empty, StringComparison.OrdinalIgnoreCase))?.Key
            ?? string.Empty;
    }

    /// <summary>
    /// The gate chips, the strip and the summary line - everything that counts
    /// the class's drivers. Every check-in, payment, paper and inspection ends
    /// here (UpdateSummary).
    /// </summary>
    private void RefreshCounts()
    {
        var inClass = InClass();
        var states = inClass.Select(ReadinessOf).ToList();
        var here = states.Count(s => s.Here);

        var chips = new List<ChipRow.Chip>
        {
            new(nameof(RosterShow.Everyone), "Everyone", inClass.Count),
            new(nameof(RosterShow.NotHere), "Not here", states.Count(s => !s.Here)),
        };

        if (_tech is not null)
        {
            chips.Add(new(nameof(RosterShow.NeedsTech), "Needs tech", states.Count(s => s.Tech is Gate.Open or Gate.Failed)));
        }

        var charging = _payments is not null && TonightsFees() is { Charges: true };

        if (charging)
        {
            chips.Add(new(nameof(RosterShow.Owes), "Owes", states.Count(s => s.Paid == Gate.Open)));
        }

        if (ChecksPapers)
        {
            chips.Add(new(nameof(RosterShow.Papers), "Papers", states.Count(s => s.Papers == Gate.Open)));
        }

        chips.Add(new(nameof(RosterShow.Ready), "Ready", states.Count(s => s.Ready)));

        // A gate the night no longer has - the fee taken off, tech switched
        // off - goes back to everyone.
        if (chips.All(c => c.Key != _show.ToString()))
        {
            _show = RosterShow.Everyone;
        }

        _showChips.SetChips(chips);
        _showChips.Picked = _show.ToString();

        // --- The strip ---
        static double Share(int part, int whole) => whole == 0 ? 0 : part / (double)whole;

        var tiles = new List<ReadinessStrip.Tile>
        {
            new(nameof(RosterShow.NotHere), "Checked in", here.ToString(), $"of {inClass.Count}", Share(here, inClass.Count), AppTheme.Accent),
        };

        if (_tech is not null)
        {
            var cleared = states.Count(s => s.Here && s.Tech == Gate.Done);
            var failed = states.Count(s => s.Here && s.Tech == Gate.Failed);
            tiles.Add(new(nameof(RosterShow.NeedsTech), "Through tech", cleared.ToString(),
                failed > 0 ? $"of {here} here · {failed} failed" : $"of {here} here", Share(cleared, here), AppTheme.Success));
        }

        if (charging && TonightsFees() is { } fees)
        {
            var paid = states.Count(s => s.Here && s.Paid == Gate.Done);
            var owed = inClass.Where((p, i) => states[i].Here && states[i].Paid == Gate.Open)
                .Sum(p => FeeFor(p, fees.Day, ClassNamed));
            tiles.Add(new(nameof(RosterShow.Owes), "Paid", paid.ToString(),
                owed > 0 ? $"of {here} · {DeskMoney.Money(owed)} owed" : $"of {here} here", Share(paid, here), AppTheme.Success));
        }

        if (ChecksPapers)
        {
            var inOrder = states.Count(s => s.Here && s.Papers == Gate.Done);
            tiles.Add(new(nameof(RosterShow.Papers), "Papers", inOrder.ToString(), $"of {here} here", Share(inOrder, here), AppTheme.Success));
        }

        var ready = states.Count(s => s.Ready);
        tiles.Add(new(nameof(RosterShow.Ready), "Ready to race", ready.ToString(), ready == 1 ? "driver" : "drivers", null, AppTheme.Success, Stands: true));

        _strip.SetTiles(tiles);
    }

    // ------------------------------------------------------------ the rows

    /// <summary>
    /// A row's Tech and Ready cells, and its name dimmed while the driver is
    /// not here. The Paid and Papers cells are ShowPaid's and ShowPapers'.
    /// </summary>
    private void ShowStatus(DataGridViewRow row)
    {
        if (row.Tag is not DriverProfile profile)
        {
            return;
        }

        var r = ReadinessOf(profile);

        if (_grid.Columns["driver"] is { } driver)
        {
            row.Cells[driver.Index].Style.ForeColor = r.Here ? AppTheme.TextDark : AppTheme.TextMuted;
        }

        if (_tech is not null && _grid.Columns["tech"] is { } techColumn)
        {
            var cell = row.Cells[techColumn.Index];
            var inspection = TechFor(profile);

            (cell.Value, cell.Style.ForeColor, cell.ToolTipText) = r.Tech switch
            {
                Gate.Done => ((object)inspection!.ResultText, AppTheme.Success, string.Empty),
                Gate.Failed => ("Failed", AppTheme.Danger, FailedWhy(inspection)),
                _ when r.Here => ("Waiting", AppTheme.Warning, "Not looked at yet tonight. Double-click to inspect."),
                _ => ("—", AppTheme.TextMuted, string.Empty),
            };
        }

        if (_grid.Columns["ready"] is { } readyColumn)
        {
            var cell = row.Cells[readyColumn.Index];
            cell.Value = r.Ready ? "Ready" : $"{r.Through}/{r.Gates}";
            cell.Tag = r;
            cell.ToolTipText = r.Ready ? "Through every gate tonight: in the heats." : StillToDo(r);
        }
    }

    /// <summary>What a failed inspection found: the items failed, or the note.</summary>
    internal static string FailedWhy(TechInspection? inspection) =>
        inspection is null ? string.Empty
        : inspection.ItemsFailed.Count > 0 ? string.Join(", ", inspection.ItemsFailed)
        : inspection.Notes;

    /// <summary>"Still to do: check in, tech, pay" - the Ready cell's tip, and the desk's list.</summary>
    private static string StillToDo(Readiness r)
    {
        var left = new List<string>();

        if (!r.Here)
        {
            left.Add("check in");
        }

        if (r.Tech == Gate.Failed)
        {
            left.Add("tech (failed)");
        }
        else if (r.Tech == Gate.Open)
        {
            left.Add("tech");
        }

        if (r.Paid == Gate.Open)
        {
            left.Add("pay");
        }

        if (r.Papers == Gate.Open)
        {
            left.Add("papers");
        }

        return left.Count == 0 ? string.Empty : "Still to do: " + string.Join(", ", left);
    }

    /// <summary>
    /// The roster's own drawing: the status cells as chips, the classes as
    /// tags, Ready as a bar per gate. The values stay plain words underneath,
    /// for the tips, copying and the tests.
    /// </summary>
    private void PaintRosterCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is not { } g || e.ColumnIndex >= _grid.Columns.Count)
        {
            return;
        }

        var name = _grid.Columns[e.ColumnIndex].Name;

        if (name is not ("class" or "tech" or "paid" or "papers" or "ready"))
        {
            return;
        }

        e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);

        var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        var text = e.FormattedValue as string ?? string.Empty;
        var inner = new Rectangle(e.CellBounds.X + UiLayout.AtDpi(6), e.CellBounds.Y, Math.Max(1, e.CellBounds.Width - UiLayout.AtDpi(8)), e.CellBounds.Height);
        var font = AppTheme.SmallFont;

        switch (name)
        {
            case "class":
                var classes = text.Split(" + ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                RosterInk.DrawClassTags(g, inner, classes, font, AppTheme.TextDark);
                break;

            case "ready" when cell.Tag is Readiness r:
                if (r.Ready)
                {
                    RosterInk.DrawChip(g, inner, "Ready", AppTheme.Success, font, dot: false);
                }
                else
                {
                    RosterInk.DrawGates(g, inner, GateColours(r), text, font);
                }

                break;

            default:
                var tone = cell.Style.ForeColor.IsEmpty ? AppTheme.TextMuted : cell.Style.ForeColor;

                // Nothing to say, or not a state - "—", "Pick..." - in plain muted words.
                if (text.Length == 0 || text == "—" || tone == AppTheme.TextMuted)
                {
                    TextRenderer.DrawText(g, text, font, inner, AppTheme.TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
                else
                {
                    RosterInk.DrawChip(g, inner, text, tone, font);
                }

                break;
        }

        e.Handled = true;
    }

    /// <summary>One colour per gate, in order: checked in, tech, paid, papers.</summary>
    private static List<Color> GateColours(Readiness r)
    {
        static Color Of(Gate gate) => gate switch
        {
            Gate.Done => AppTheme.Success,
            Gate.Failed => AppTheme.Danger,
            _ => AppTheme.Warning,
        };

        var colours = new List<Color> { r.Here ? AppTheme.Success : AppTheme.Border };

        foreach (var gate in new[] { r.Tech, r.Paid, r.Papers }.Where(g => g != Gate.NotRun))
        {
            colours.Add(r.Here ? Of(gate) : gate == Gate.Done ? AppTheme.Success : AppTheme.Border);
        }

        return colours;
    }

    // ------------------------------------------------------------ several picked

    private readonly Label _lblPicked = new() { AutoSize = true };
    private readonly Button _btnPickedIn = new() { Text = "Check In" };
    private readonly Button _btnPickedOut = new() { Text = "Check Out" };
    private readonly Button _btnPickedCash = new() { Text = "Paid Cash" };
    private readonly Button _btnPickedCard = new() { Text = "Paid Card" };
    private readonly Button _btnPickedClear = new() { Text = "Clear" };

    /// <summary>
    /// The bar for the drivers picked, in the bottom-left corner of the
    /// roster: what can be done to all of them at once. Shown while two or
    /// more are picked; the scan line has the corner the rest of the time.
    /// </summary>
    private ButtonBar BuildPickedBar()
    {
        _lblPicked.ForeColor = AppTheme.TextDark;
        _lblPicked.Padding = new Padding(0, UiLayout.AtDpi(9), 0, 0);
        _textSize.Wear(_lblPicked, () => AppTheme.BoldFont);

        _btnPickedIn.Click += (_, _) => CheckInDrivers(SelectedDrivers());
        _btnPickedOut.Click += (_, _) => CheckOutDrivers(SelectedDrivers());
        _btnPickedCash.Click += (_, _) => SettlePicked(PaidHow.Cash);
        _btnPickedCard.Click += (_, _) => SettlePicked(PaidHow.Card);
        _btnPickedClear.Click += (_, _) => _grid.ClearSelection();

        // Clearing the pick takes nothing away: not the red the theme gives "Clear".
        AppTheme.StyleButton(_btnPickedClear, AppTheme.ButtonKind.Secondary);

        var bar = new ButtonBar().With(_lblScanStatus, _lblPicked, _btnPickedIn, _btnPickedOut, _btnPickedCash, _btnPickedCard);

        if (_tech is not null)
        {
            bar.With(_btnInspect);
        }

        bar.With(_btnPickedClear);

        ShowPickedBar();
        return bar;
    }

    private void SettlePicked(PaidHow how)
    {
        if (TonightsFees() is { Charges: true } fees)
        {
            Settle(fees.Day, SelectedDrivers(), how, string.Empty);
        }
    }

    /// <summary>The picked bar shown, or the scan line, as the selection says.</summary>
    private void ShowPickedBar()
    {
        var picked = _grid.SelectedRows.Count;
        var many = picked > 1;

        if (many)
        {
            var names = SelectedDrivers().Select(DisplayName).ToList();
            var list = names.Count <= 3 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(2))} and {names.Count - 2} more";
            _lblPicked.Text = $"{picked} picked: {list}";
        }

        var charging = _payments is not null && TonightsFees() is { Charges: true };

        SetShown(_lblScanStatus, !many);
        SetShown(_lblPicked, many);
        SetShown(_btnPickedIn, many);
        SetShown(_btnPickedOut, many);
        SetShown(_btnPickedCash, many && charging);
        SetShown(_btnPickedCard, many && charging);
        SetShown(_btnInspect, many && _tech is not null);
        SetShown(_btnPickedClear, many);
    }

    private static void SetShown(Control control, bool shown)
    {
        if (control.Visible != shown)
        {
            control.Visible = shown;
        }
    }

    /// <summary>
    /// Takes everybody off tonight's list, after asking - the start of a new
    /// event day. It was a button of its own on the page; it is under More.
    /// </summary>
    private void CheckOutEveryone()
    {
        // Not on a closed day: its sheet is the record of who was at
        // that meeting, and this used to wipe it - see
        // CheckInStore.ClosedCurrentDay.
        if (RefusedOnClosedDay())
        {
            return;
        }

        if (AppDialog.Show(this, "Clear all check-ins? Do this at the start of a new event day.",
            "Confirm check-out", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // Which event, as well as who: an undo made after switching
        // events must put them back where they were checked out.
        var snapshot = _checkIns.SnapshotCheckedIn();
        var eventId = _checkIns.CurrentEventId;
        _undo.Capture($"Check out everyone ({Plural.Of(snapshot.Count, "driver")})", () =>
        {
            _checkIns.RestoreCheckedIn(eventId, snapshot);

            // Only when the check-ins put back are the ones on show.
            // Undone after switching to another race day, they went
            // back to the day they came from - and a desk change
            // raised for them here blocked pending phone check-ins
            // for the same drivers at the race day now open.
            if (string.Equals(eventId, _checkIns.CurrentEventId, StringComparison.Ordinal))
            {
                RaiseDeskChanges(snapshot);
            }

            // The page may be gone by the time Undo is pressed; the
            // store is what matters, and the page re-reads it when
            // it is next shown.
            if (!IsDisposed && !Disposing)
            {
                RefreshDriverList();
            }
        });

        _checkIns.CheckOutAll();
        RaiseDeskChanges(snapshot);
        RefreshDriverList();
        LogDesk("Everyone", "checked out");
    }

    // ------------------------------------------------------------ More

    /// <summary>
    /// The page's less-used commands, under one button: the whole-list check
    /// in and out, the scrutineer's log, printing, the poster and Desk View.
    /// They were six buttons the same size as Add Driver.
    /// </summary>
    private Button BuildMoreButton()
    {
        var more = new Button { Text = "More ▾" };

        more.Click += (_, _) =>
        {
            var menu = new ContextMenuStrip();

            void Item(string text, Action act, bool enabled = true)
            {
                var item = new ToolStripMenuItem(text) { Enabled = enabled };
                item.Click += (_, _) => act();
                menu.Items.Add(item);
            }

            var filtered = _classFilter is not null || _show != RosterShow.Everyone || _search.Length > 0;
            Item(filtered ? "Check In Everyone Shown" : "Check In Everyone", CheckInEveryoneShown);
            Item("Check Out Everyone...", CheckOutEveryone);

            if (_tech is not null)
            {
                menu.Items.Add(new ToolStripSeparator());
                Item("Inspect Selected Car...", RequestInspection, _grid.CurrentRow is not null);
                Item("Scrutineering Log...", () => TechLogRequested?.Invoke());
            }

            menu.Items.Add(new ToolStripSeparator());
            Item("Print QR Check-ins...", GenerateBadges);
            Item("Self Check-In Poster...", ShowSelfCheckInPoster);
            Item("Desk View...", () => DeskViewRequested?.Invoke());

            AppTheme.StyleContextMenu(menu);
            menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(more, new Point(0, more.Height));
        };

        return more;
    }
}
