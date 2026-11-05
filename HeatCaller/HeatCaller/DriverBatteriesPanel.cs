namespace HeatCaller;

/// <summary>
/// One driver's batteries, on their page (Driver Hub, the Batteries tab):
/// their packs, with the one in the car now in bold; their recent sessions
/// and the pack each was run on; and how their pace compares across packs.
///
/// The pack in the car is what gets recorded for each session they run
/// when it is filed (BatteryPackStore.AutoTag), so keeping it right is the
/// one thing that matters - Use This One here, the right-click on Race
/// Control, check-in, or the phone. A session's pack can be changed here
/// afterwards, and a pack picked by hand always beats the automatic one.
///
/// A club pack shows here only while it is in their car; the club's packs
/// themselves are kept on the Garage page (Club Batteries).
/// </summary>
internal sealed class DriverBatteriesPanel : UserControl
{
    /// <summary>How many of their sessions are listed, newest first.</summary>
    private const int RecentSessions = 60;

    private readonly ProfileStore _profiles;
    private readonly BatteryPackStore _store;
    private readonly SessionHistoryStore _history;
    private readonly UndoService _undo;

    private readonly Label _lblPacks = new() { AutoSize = true };
    private readonly ListView _packs = new();
    private readonly Button _btnAdd = new() { Text = "Add Pack..." };
    private readonly Button _btnEdit = new() { Text = "Edit..." };
    private readonly Button _btnUse = new() { Text = "Use This One" };
    private readonly Button _btnRetire = new() { Text = "Retire" };
    private readonly Button _btnTransfer = new() { Text = "Transfer..." };
    private readonly Button _btnDelete = new() { Text = "Delete" };

    private readonly Label _lblSessions = new() { AutoSize = true };
    private readonly ListView _sessions = new();
    private readonly Button _btnSetPack = new() { Text = "Set Pack..." };

    private readonly Label _lblPace = new() { AutoSize = true };
    private readonly ComboBox _cmbLayout = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbChart = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListView _pace = new();
    private readonly PackChartControl _chart = new();

    /// <summary>The layout picker's first choice: every layout.</summary>
    private const string AllLayouts = "All layouts";

    /// <summary>The layout picker's width, and the chart picker's, at the normal text size.</summary>
    private const int PickerWidth = 240;

    /// <summary>The least the chart gets at the normal text size.</summary>
    private const int ChartLeastHeight = 200;

    /// <summary>Set while the layout picker is refilled, so that is not taken for a choice.</summary>
    private bool _fillingLayouts;

    private string? _driver;

    /// <summary>Set while this panel itself is changing the store, so its own Changed does not fill it twice.</summary>
    private bool _changing;

    /// <summary>
    /// Set when the store changed from somewhere else while this panel's own change was running.
    /// WHY (20 Oct 2026): a change here can open a dialog, and a pack changed elsewhere meanwhile
    /// (the phone, check-in, Race Control) was skipped because of _changing - and never shown if
    /// the dialog was then cancelled. Volatile because Changed can be raised off the UI thread.
    /// </summary>
    private volatile bool _missedChange;

    /// <summary>Raised after a pack was changed here.</summary>
    public event Action? Changed;

    public DriverBatteriesPanel(ProfileStore profiles, BatteryPackStore store, SessionHistoryStore history, UndoService undo)
    {
        _profiles = profiles;
        _store = store;
        _history = history;
        _undo = undo;

        Dock = DockStyle.Fill;

        SetUp(_packs, ("Pack", 26, HorizontalAlignment.Left), ("Type", 14, HorizontalAlignment.Left),
            ("Sessions", 12, HorizontalAlignment.Right), ("Avg best lap", 16, HorizontalAlignment.Right), ("Notes", 32, HorizontalAlignment.Left));
        SetUp(_sessions, ("When", 20, HorizontalAlignment.Left), ("Session", 30, HorizontalAlignment.Left),
            ("Best lap", 14, HorizontalAlignment.Right), ("Pack", 18, HorizontalAlignment.Left), ("Recorded by", 18, HorizontalAlignment.Left));
        SetUp(_pace, ("Pack", 25, HorizontalAlignment.Left), ("Races", 9, HorizontalAlignment.Right),
            ("Best", 13, HorizontalAlignment.Right), ("Median", 13, HorizontalAlignment.Right),
            ("Consistency", 14, HorizontalAlignment.Right), ("Fade", 13, HorizontalAlignment.Right), ("Gap", 13, HorizontalAlignment.Right));

        _cmbChart.Items.AddRange(new object[] { "Fade through a race", "Median lap over time" });
        _cmbChart.SelectedIndex = 0;
        _cmbChart.SelectedIndexChanged += (_, _) =>
            _chart.Mode = _cmbChart.SelectedIndex == 1 ? PackChartMode.Trend : PackChartMode.Fade;

        _cmbLayout.Items.Add(AllLayouts);
        _cmbLayout.SelectedIndex = 0;
        _cmbLayout.SelectedIndexChanged += (_, _) =>
        {
            if (!_fillingLayouts)
            {
                Fill();
            }
        };

        _packs.SelectedIndexChanged += (_, _) => UpdateButtons();
        _packs.DoubleClick += (_, _) => EditPicked();
        _sessions.SelectedIndexChanged += (_, _) => UpdateButtons();
        _sessions.DoubleClick += (_, _) => SetPackForPicked();

        _btnAdd.Click += (_, _) => AddPack();
        _btnEdit.Click += (_, _) => EditPicked();
        _btnUse.Click += (_, _) => UsePicked();
        _btnRetire.Click += (_, _) => RetirePicked();
        _btnTransfer.Click += (_, _) => TransferPicked();
        _btnDelete.Click += (_, _) => DeletePicked();
        _btnSetPack.Click += (_, _) => SetPackForPicked();

        var page = new FillPage();
        page.Add(Heading("Packs"), stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(_lblPacks, stretch: false, gapBelow: UiLayout.HintGap);
        page.AddFill(_packs, _ => ListLeast(_packs, 3), gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar().With(_btnAdd, _btnEdit, _btnUse, _btnRetire, _btnTransfer, _btnDelete), gapBelow: UiLayout.SectionGap);

        page.Add(Heading("Recent sessions"), stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(_lblSessions, stretch: false, gapBelow: UiLayout.HintGap);
        page.AddFill(_sessions, _ => ListLeast(_sessions, 3), gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar().With(_btnSetPack), gapBelow: UiLayout.SectionGap);

        // The layout looked at and the chart's mode, on a line above the list -
        // each on a line of its own when they no longer fit side by side.
        var pick = new FieldRow();
        pick.Add(new Label { Text = "Layout:" }, _cmbLayout, () => UiLayout.AtTextSize(PickerWidth));
        pick.Add(new Label { Text = "Chart:" }, _cmbChart, () => UiLayout.AtTextSize(PickerWidth));

        page.Add(Heading("Pace by pack"), stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(_lblPace, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(pick, gapBelow: UiLayout.RowGap);
        page.AddFill(_pace, _ => ListLeast(_pace, 2), gapBelow: UiLayout.RowGap);
        page.AddFill(_chart, _ => UiLayout.AtTextSize(ChartLeastHeight), gapBelow: 0);

        Controls.Add(page);

        AppTheme.ApplyRecursively(this);
        _lblPacks.ForeColor = AppTheme.TextMuted;
        _lblSessions.ForeColor = AppTheme.TextMuted;
        _lblPace.ForeColor = AppTheme.TextMuted;

        // A pack changed anywhere else - Race Control, check-in, the phone,
        // the Garage page, an undo - shows here. The phone's change arrives
        // on the UI thread, but nothing promises that, so it is posted.
        _store.Changed += OnStoreChanged;

        ShowDriver(null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _store.Changed -= OnStoreChanged;
        }

        base.Dispose(disposing);
    }

    private void OnStoreChanged()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        // WHY (20 Oct 2026): remembered rather than dropped, so Change() fills
        // the panel afterwards even when its own change was cancelled.
        if (_changing)
        {
            _missedChange = true;
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed && Visible)
                {
                    Fill();
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The window went between the check and the post.
        }
    }

    /// <summary>Filled again when shown, for whatever changed while it was hidden.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            Fill();
        }
    }

    private static Label Heading(string text) => new() { Text = text, AutoSize = true, Font = AppTheme.BoldFont };

    private static void SetUp(ListView list, params (string Caption, int Share, HorizontalAlignment Align)[] columns)
    {
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;

        foreach (var (caption, share, align) in columns)
        {
            list.Columns.Add(new ColumnHeader { Text = caption, TextAlign = align, Tag = share });
        }

        list.ClientSizeChanged += (_, _) => FitColumns(list);
    }

    /// <summary>Each column its share of the width, the last taking what is left.</summary>
    private static void FitColumns(ListView list)
    {
        var width = Math.Max(1, list.ClientSize.Width);
        var used = 0;

        for (var i = 0; i < list.Columns.Count; i++)
        {
            var column = list.Columns[i];
            var share = column.Tag is int s ? s : 20;
            column.Width = i == list.Columns.Count - 1 ? Math.Max(20, width - used) : width * share / 100;
            used += column.Width;
        }
    }

    /// <summary>The least a list can do with: its headings and <paramref name="rows"/> rows.</summary>
    private static int ListLeast(ListView list, int rows) =>
        (rows + 1) * (Pieces.LineHeight(list.Font) + UiLayout.AtTextSize(6)) + 4;

    /// <summary>Shows this driver's batteries - their main transponder, the profile's key - or none.</summary>
    public void ShowDriver(string? driver)
    {
        _driver = driver;
        Fill();
    }

    private DriverProfile? Profile => _driver is null ? null : _profiles.Get(_driver);

    private string Key => BatteryPackStore.DriverKey(_profiles, _driver);

    private string DriverName => Profile is { } p ? BatteryActions.NameOf(p) : _driver ?? string.Empty;

    private void Fill()
    {
        var profile = Profile;
        var keptPack = (PickedPack()?.Id);
        var keptSession = PickedSession()?.Id;

        _packs.BeginUpdate();
        _sessions.BeginUpdate();
        _pace.BeginUpdate();

        _packs.Items.Clear();
        _sessions.Items.Clear();
        _pace.Items.Clear();

        try
        {
            if (profile is null)
            {
                _lblPacks.Text = string.Empty;
                _lblSessions.Text = string.Empty;
                _lblPace.Text = string.Empty;
                _chart.Packs = Array.Empty<PackStats>();
                FillLayouts(Array.Empty<string>());
                return;
            }

            var key = Key;
            var pace = _store.ComparePace(key, _history, 1, _profiles);
            var paceOf = pace.ToDictionary(p => p.Pack.Id, StringComparer.Ordinal);
            var inCar = _store.CurrentPack(key);

            // Theirs, and the club pack in their car when it is one.
            var shown = _store.OwnedBy(profile).ToList();

            if (inCar is not null && shown.All(p => p.Id != inCar.Id))
            {
                shown.Insert(0, inCar);
            }

            foreach (var pack in shown)
            {
                var name = pack.DisplayName
                           + (pack.IsClubPack ? "  (club)" : string.Empty)
                           + (pack.Id == inCar?.Id ? "  - in the car" : string.Empty)
                           + (pack.Retired ? "  (retired)" : string.Empty);

                paceOf.TryGetValue(pack.Id, out var row);

                var item = new ListViewItem(new[]
                {
                    name,
                    pack.Chemistry,
                    row is null ? "0" : row.Sessions.ToString(),
                    row is null ? "-" : $"{row.AverageBestLapSeconds:0.000}s",
                    pack.Notes.Replace('\r', ' ').Replace('\n', ' '),
                })
                { Tag = pack };

                if (pack.Id == inCar?.Id)
                {
                    item.Font = AppTheme.BoldFont;
                }
                else if (pack.Retired)
                {
                    item.ForeColor = AppTheme.TextMuted;
                }

                _packs.Items.Add(item);
            }

            _lblPacks.Text = shown.Count == 0
                ? $"No packs yet. Add {DriverName}'s packs, then Use This One for the pack in the car - each session they run is recorded on it."
                : inCar is null
                    ? "No pack in the car. Pick one and click Use This One - each session they run is then recorded on it."
                    : $"Pack {inCar.DisplayName} is in the car: each session they run is recorded on it. The club's own packs are on the Garage page.";

            // Their sessions, newest first, with the pack each was run on.
            // Demo and imported races left out: no pack is recorded on them (AutoTag), and their laps count for nothing.
            var mine = _history.All
                .Where(s => !s.IsNotScored() && s.Results.Any(r => string.Equals(BatteryPackStore.DriverKey(_profiles, r.Transponder), key, StringComparison.OrdinalIgnoreCase)))
                .Take(RecentSessions)
                .ToList();

            foreach (var session in mine)
            {
                var result = session.Results.First(r => string.Equals(BatteryPackStore.DriverKey(_profiles, r.Transponder), key, StringComparison.OrdinalIgnoreCase));
                var pack = _store.PackFor(session.Id, key);

                _sessions.Items.Add(new ListViewItem(new[]
                {
                    AppTime.DateAndTime(session.StartedAtUtc.ToLocalTime()),
                    string.IsNullOrWhiteSpace(session.Label) ? "-" : session.Label,
                    result.FastLapMs > 0 ? $"{result.FastLapMs / 1000.0:0.000}s" : "-",
                    pack?.DisplayName ?? "-",
                    pack is null ? string.Empty : HowRecorded(session.Id, key),
                })
                { Tag = session });
            }

            _lblSessions.Text = mine.Count == 0
                ? "No sessions yet."
                : "Double-click a session to change the pack it was run on.";

            FillPace(key);
        }
        finally
        {
            _packs.EndUpdate();
            _sessions.EndUpdate();
            _pace.EndUpdate();

            FitColumns(_packs);
            FitColumns(_sessions);
            FitColumns(_pace);

            Reselect(_packs, item => item.Tag is BatteryPack p && p.Id == keptPack);
            Reselect(_sessions, item => item.Tag is SessionRecord s && s.Id == keptSession);

            UpdateButtons();
        }
    }

    /// <summary>
    /// Pace by pack (PackAnalytics), on the layout picked: each pack's races,
    /// best lap, median clean lap, consistency and fade, and how far its
    /// median is off the best pack's - with the chart under it.
    /// </summary>
    private void FillPace(string key)
    {
        var layout = _cmbLayout.SelectedIndex > 0 ? _cmbLayout.SelectedItem as string : null;
        var report = PackAnalytics.For(key, _store, _history, _profiles, layout is null ? null : new PackFilter(Layout: layout));

        FillLayouts(report.Layouts);

        // The layout picked may have gone - no race left on it: every layout again.
        if (layout is not null && _cmbLayout.SelectedIndex <= 0)
        {
            layout = null;
            report = PackAnalytics.For(key, _store, _history, _profiles);
        }

        var packs = report.Packs;

        foreach (var row in packs)
        {
            var item = new ListViewItem(new[]
            {
                row.Pack.DisplayName + (row.Enough ? string.Empty : "  (few races)"),
                row.Sessions.ToString(),
                $"{row.BestLap:0.000}s",
                row.MedianLap > 0 ? $"{row.MedianLap:0.000}s" : "-",
                row.MedianLap > 0 ? $"±{row.Spread:0.000}s" : "-",
                row.MedianLap > 0 ? Signed(row.Fade) : "-",
                row.MedianLap <= 0 || Math.Abs(row.GapToBestPack) < 0.0005 ? "-" : Signed(row.GapToBestPack),
            });

            // Only a gap worth acting on stands out. Flagging every
            // difference would make normal run-to-run variation look
            // like a tired pack. A pack with too few races to trust is
            // muted rather than called good or bad.
            if (!row.Enough)
            {
                item.ForeColor = AppTheme.TextMuted;
            }
            else if (row.GapToBestPack >= 0.1)
            {
                item.ForeColor = AppTheme.Danger;
            }
            else if (row.MedianLap > 0 && row.GapToBestPack <= 0 && packs.Count > 1)
            {
                item.ForeColor = AppTheme.Success;
            }

            _pace.Items.Add(item);
        }

        _chart.Packs = packs;

        _lblPace.Text = packs.Count == 0
            ? layout is null
                ? "Shown once sessions are recorded on their packs: the median clean lap on each, how steady it is, and how much it fades."
                : "No races on their packs on this layout yet."
            : packs.Count == 1
                ? "One pack so far - a second shows whether either is costing time. Median of clean laps; fade is the last third against the first."
                : $"Median of clean laps, quickest first. Fade is the last third of a race against the first. A gap of a tenth or more is in red; under {PackAnalytics.EnoughSessions} races is too few to trust.";
    }

    private static string Signed(double seconds) => seconds >= 0 ? $"+{seconds:0.000}s" : $"-{-seconds:0.000}s";

    /// <summary>The layout picker: every layout, then each one they have raced their packs on, the one picked kept.</summary>
    private void FillLayouts(IReadOnlyList<string> layouts)
    {
        var picked = _cmbLayout.SelectedIndex > 0 ? _cmbLayout.SelectedItem as string : null;
        var now = _cmbLayout.Items.Cast<object>().Skip(1).Select(o => o as string ?? string.Empty).ToList();

        if (now.SequenceEqual(layouts, StringComparer.Ordinal))
        {
            return;
        }

        _fillingLayouts = true;

        try
        {
            _cmbLayout.BeginUpdate();
            _cmbLayout.Items.Clear();
            _cmbLayout.Items.Add(AllLayouts);

            foreach (var layout in layouts)
            {
                _cmbLayout.Items.Add(layout);
            }

            var again = picked is null ? -1 : _cmbLayout.Items.IndexOf(picked);
            _cmbLayout.SelectedIndex = again > 0 ? again : 0;
        }
        finally
        {
            _cmbLayout.EndUpdate();
            _fillingLayouts = false;
        }
    }

    private static void Reselect(ListView list, Func<ListViewItem, bool> match)
    {
        if (list.Items.Cast<ListViewItem>().FirstOrDefault(match) is { } again)
        {
            again.Selected = true;
        }
    }

    /// <summary>How a session's pack came to be recorded, in a few words.</summary>
    private string HowRecorded(string sessionId, string key)
    {
        if (_store.IsSessionWideTag(sessionId, key))
        {
            return "Whole session";
        }

        return _store.TagFor(sessionId, key)?.Source switch
        {
            PackTagSource.InTheCar => "In the car",
            PackTagSource.RaceControl => "Race Control",
            PackTagSource.DriverHub => "By hand",
            PackTagSource.Converted => "Before",
            PackTagSource.Phone => "Phone",
            _ => string.Empty,
        };
    }

    private BatteryPack? PickedPack() =>
        _packs.SelectedItems.Count == 1 && _packs.SelectedItems[0].Tag is BatteryPack pack ? _store.Get(pack.Id) : null;

    private SessionRecord? PickedSession() =>
        _sessions.SelectedItems.Count == 1 ? _sessions.SelectedItems[0].Tag as SessionRecord : null;

    private void UpdateButtons()
    {
        var hasDriver = Profile is not null;
        var pack = PickedPack();
        var own = pack is { IsClubPack: false };
        var inCar = pack is not null && _store.CurrentPackId(Key) == pack.Id;

        _btnAdd.Enabled = hasDriver;
        _btnEdit.Enabled = own;
        _btnRetire.Enabled = own;
        _btnTransfer.Enabled = own;
        _btnDelete.Enabled = own;
        _btnUse.Enabled = hasDriver && pack is not null && (inCar || !pack.Retired);
        _btnSetPack.Enabled = hasDriver && PickedSession() is not null;

        var retireText = pack is { Retired: true } ? "Bring Back" : "Retire";
        var useText = inCar ? "Take Out of the Car" : "Use This One";

        Restyle(_btnRetire, retireText);
        Restyle(_btnUse, useText);
    }

    /// <summary>A button's caption changed - styled again, as the theme sets captions (AppTheme.Caption).</summary>
    private static void Restyle(Button button, string text)
    {
        if (!string.Equals(button.Text, AppTheme.Caption(text), StringComparison.Ordinal))
        {
            button.Text = text;
            AppTheme.StyleButton(button, AppTheme.ButtonKind.Secondary);
        }
    }

    /// <summary>Runs a change from here: the panel filled once, after, rather than once more by its own Changed.</summary>
    private void Change(Func<bool> change)
    {
        bool changed;
        _missedChange = false;
        _changing = true;

        try
        {
            changed = change();
        }
        finally
        {
            _changing = false;
        }

        // WHY (20 Oct 2026): filled too when something else changed the store while this
        // change ran (say, while its dialog was open), even if this change was cancelled.
        // Changed is still raised only for this panel's own change.
        if (changed || _missedChange)
        {
            _missedChange = false;
            Fill();
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private void AddPack()
    {
        if (Profile is not { } profile)
        {
            return;
        }

        Change(() =>
        {
            var added = BatteryActions.AddOrEdit(this, _store, _undo, null, profile.Transponder);

            // Their first pack goes straight in the car: there is nothing
            // else it could be, and nobody then has to know to click Use
            // This One before the first race.
            if (added is not null && !added.Retired && _store.CurrentPack(Key) is null && _store.OwnedBy(profile).Count(p => !p.Retired) == 1)
            {
                BatteryActions.PutInCar(_store, _undo, Key, DriverName, added.Id);
            }

            return added is not null;
        });
    }

    private void EditPicked()
    {
        if (PickedPack() is not { IsClubPack: false } pack)
        {
            return;
        }

        Change(() => BatteryActions.AddOrEdit(this, _store, _undo, pack, pack.OwnerTransponder) is not null);
    }

    private void UsePicked()
    {
        if (Profile is null || PickedPack() is not { } pack)
        {
            return;
        }

        var inCar = _store.CurrentPackId(Key) == pack.Id;

        Change(() => BatteryActions.PutInCar(_store, _undo, Key, DriverName, inCar ? null : pack.Id));
    }

    private void RetirePicked()
    {
        if (PickedPack() is not { IsClubPack: false } pack)
        {
            return;
        }

        Change(() =>
        {
            BatteryActions.SetRetired(_store, _undo, pack, !pack.Retired);
            return true;
        });
    }

    private void TransferPicked()
    {
        if (PickedPack() is not { IsClubPack: false } pack)
        {
            return;
        }

        Change(() => BatteryActions.Transfer(this, _store, _profiles, _undo, pack));
    }

    private void DeletePicked()
    {
        if (PickedPack() is not { IsClubPack: false } pack)
        {
            return;
        }

        Change(() => BatteryActions.Delete(this, _store, _undo, pack));
    }

    private void SetPackForPicked()
    {
        if (Profile is not { } profile || PickedSession() is not { } session)
        {
            return;
        }

        var key = Key;
        var now = _store.PackFor(session.Id, key);
        var choices = _store.ChoicesFor(profile, now?.Id);

        if (choices.Count == 0)
        {
            AppDialog.Show(this, $"{DriverName} has no packs yet, and the club has none. Add one first.", "No packs",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new PackPickerForm(choices, now);

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var chosen = picker.Chosen;

        if (chosen?.Id == now?.Id && !_store.IsSessionWideTag(session.Id, key))
        {
            return;
        }

        Change(() =>
        {
            BatteryActions.TagSession(_store, _undo, session.Id, session.Label, key, DriverName, chosen?.Id, PackTagSource.DriverHub, intoCar: false);
            return true;
        });
    }
}
