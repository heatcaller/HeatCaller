using System.Globalization;

namespace HeatCaller;

/// <summary>
/// The Desk Inbox (15 Nov 2026): everything the room sends the desk, on one
/// screen - see <see cref="DeskInbox"/> for why and for the list itself.
///
/// It takes the place of four windows: Driver Requests, Tonight's club
/// notes, Tonight's room vote and Online Sign-ups (Asher: "inbox replaces
/// old as long as there's no missing functionality"). Every button those had
/// is here - the parity list is in plans/relay-themes-and-desk-inbox-plan.md
/// and each row has a test - and every answer still goes through the desk
/// that owns it, with the same dialogs, checks and undo.
///
/// HOW IT IS WORKED, AFTER WHAT SUPPORT INBOXES AND DISPATCH SCREENS DO:
/// one queue sorted by what holds up the next race; how long each has
/// waited, going amber then red; one key per answer (A approve, D decline,
/// N noted, S later, Enter the main answer); quick replies for a decline;
/// several of one kind answered at once; chips with counts rather than
/// windows; "All clear" when nothing waits; and a badge on Race Control's
/// Inbox button rather than a window jumping to the front mid-race.
///
/// Not a dialog: the race director keeps it beside Race Control all night,
/// and it keeps itself up to date (every second).
/// </summary>
internal sealed partial class DeskInboxForm : Form
{
    /// <summary>The list shows its headings and at least this many rows before the window scrolls instead.</summary>
    private const int LeastRows = 5;

    private readonly DriverRequestDesk? _requests;
    private readonly ProfileStore _profiles;
    private readonly Action<string>? _speakName;
    private readonly CheckInStore? _checkIns;
    private readonly ClubNotes? _notes;
    private readonly RoomVote? _vote;
    private readonly Func<VoteTarget?>? _voteTarget;
    private readonly RelaySignupDesk? _signups;
    private readonly AppSettings _settings;
    private readonly RaceClassStore? _classes;
    private readonly RentalTransponderStore? _rentals;
    private readonly UndoService? _undo;

    private readonly FillPage _page = new();
    private readonly Label _lblState = new();
    private readonly ButtonBar _chips = new();
    private readonly Dictionary<InboxFilter, Button> _chipButtons = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblEmpty = new();
    private readonly Label _lblDetail = new();
    private readonly Button _btnApprove = new() { Text = "Approve..." };
    private readonly Button _btnNoted = new() { Text = "Noted" };
    private readonly Button _btnDecline = new() { Text = "Decline..." };
    private readonly Button _btnLater = new() { Text = "Later" };
    private readonly Button _btnDiscard = new() { Text = "Discard" };
    private readonly Button _btnRemove = new() { Text = "Remove" };
    private readonly ButtonBar _actions = new();
    private readonly Label _lblToast = new();
    private readonly LinkLabel _lnkUndo = new() { Text = "Undo" };
    private readonly ButtonBar _toastBar = new();
    private readonly CheckBox _chkPinned = new ThemedCheckBox();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 10000 };

    private readonly InboxSnooze _snooze = new();

    private InboxFilter _filter = InboxFilter.All;
    private string _drawn = string.Empty;
    private List<InboxItem> _items = new();
    private Action? _toastUndo;

    /// <summary>
    /// Everything the inbox reads and answers through. Any desk can be
    /// missing - a PC with no relay has no sign-ups - and its chip then says
    /// nothing is there.
    /// </summary>
    public DeskInboxForm(
        DriverRequestDesk? requests,
        ProfileStore profiles,
        Action<string>? speakName,
        CheckInStore? checkIns,
        ClubNotes? notes,
        RoomVote? vote,
        Func<VoteTarget?>? voteTarget,
        RelaySignupDesk? signups,
        AppSettings settings,
        RaceClassStore? classes,
        RentalTransponderStore? rentals,
        UndoService? undo)
    {
        _requests = requests;
        _profiles = profiles;
        _speakName = speakName;
        _checkIns = checkIns;
        _notes = notes;
        _vote = vote;
        _voteTarget = voteTarget;
        _signups = signups;
        _settings = settings;
        _classes = classes;
        _rentals = rentals;
        _undo = undo;

        Text = "Inbox";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;
        KeyPreview = true;

        _lblState.AutoSize = true;
        _lblState.Font = AppTheme.SmallFont;
        _lblState.MaximumSize = new Size(UiLayout.AtTextSize(980), 0);

        foreach (var filter in new[] { InboxFilter.All, InboxFilter.RaceBlocking, InboxFilter.Requests, InboxFilter.Queries, InboxFilter.Crew, InboxFilter.Notes, InboxFilter.Signups })
        {
            var chip = new Button { Text = DeskInbox.ChipWords(filter), AutoSize = true, Tag = filter };
            chip.Click += (_, _) => SetFilter(filter);
            _chipButtons[filter] = chip;
            _chips.With(chip);
        }

        BuildVoteCard();

        SetUpGrid(_grid);
        _grid.MultiSelect = true;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "waited", HeaderText = "Waited", FillWeight = 32 },
            new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "Kind", FillWeight = 52 },
            new DataGridViewTextBoxColumn { Name = "from", HeaderText = "From", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "what", HeaderText = "What", FillWeight = 190 },
            new DataGridViewTextBoxColumn { Name = "note", HeaderText = "Note", FillWeight = 100 });

        // A note longer than its column is wrapped, never cut off: the desk
        // is agreeing to words it has to have seen all of (Bug Hunt 8, A29-5).
        _grid.Columns["what"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

        // Kind and From wrap too: narrow (docked) they would be cut to "Mor..." and "Sam P...".
        _grid.Columns["kind"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.Columns["from"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _grid.SelectionChanged += (_, _) => UpdateDetail();

        // Narrow - docked beside Race Control - the Note column goes: the
        // line under the list says the note in full, and Kind and From keep
        // the room to be read.
        _grid.SizeChanged += (_, _) => FitColumns();
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                MainAnswer();
            }
        };
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                MainAnswer();
            }
        };

        _lblEmpty.AutoSize = true;
        _lblEmpty.ForeColor = AppTheme.TextMuted;
        _lblEmpty.Text = "All clear - nothing is waiting for you.";
        _lblEmpty.Visible = false;

        _lblDetail.AutoSize = true;
        _lblDetail.MaximumSize = new Size(UiLayout.AtTextSize(980), 0);
        _lblDetail.ForeColor = AppTheme.TextMuted;

        _btnApprove.Click += (_, _) => Approve();
        _btnNoted.Click += (_, _) => Noted();
        _btnDecline.Click += (_, _) => DeclineMenu();
        _btnLater.Click += (_, _) => Later();
        _btnDiscard.Click += (_, _) => Discard();
        _btnRemove.Click += (_, _) => RemoveNote(Selected() is { Source: InboxSource.Note, Payload: ClubNote waiting } ? waiting : null);
        _actions.With(_btnApprove, _btnNoted, _btnDecline, _btnLater, _btnDiscard, _btnRemove);

        _lblToast.AutoSize = true;
        _lblToast.Font = AppTheme.SmallFont;
        _lnkUndo.AutoSize = true;
        _lnkUndo.LinkClicked += (_, _) => UndoLast();
        _toastBar.With(_lblToast, _lnkUndo);
        _toastBar.Visible = false;
        _toastTimer.Tick += (_, _) => HideToast();

        BuildNoteBox();
        BuildInvites();
        BuildRules();
        BuildDone();

        // Docked: a panel down the right of the main window, beside Race
        // Control, resized by its splitter - the host (MainForm) moves this
        // window in and out of it.
        _chkPinned.Text = "Dock in the main window";
        _chkPinned.AutoSize = true;
        _chkPinned.Checked = _settings.InboxDocked;
        _chkPinned.CheckedChanged += (_, _) =>
        {
            if (_settings.InboxDocked == _chkPinned.Checked)
            {
                return;
            }

            _settings.InboxDocked = _chkPinned.Checked;
            _settings.Save();
            DockWanted?.Invoke(_chkPinned.Checked);
        };

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        _page.Add(_lblState, gapBelow: UiLayout.HintGap);
        _page.Add(_chips, gapBelow: UiLayout.RowGap);
        _page.Add(_voteCard, gapBelow: UiLayout.RowGap);
        _page.AddFill(_grid, _ => LeastListHeight(_grid), gapBelow: UiLayout.HintGap);
        _page.Add(_lblEmpty, stretch: false, gapBelow: UiLayout.HintGap);
        _page.Add(_lblDetail, gapBelow: UiLayout.HintGap);
        _page.Add(_actions, gapBelow: UiLayout.HintGap);
        _page.Add(_toastBar, gapBelow: UiLayout.RowGap);
        _page.Add(_noteBox, gapBelow: UiLayout.RowGap);
        _page.Add(_invites, gapBelow: UiLayout.RowGap);
        _page.Add(_rules, gapBelow: UiLayout.RowGap);
        _page.Add(_doneToggle, stretch: false, gapBelow: UiLayout.HintGap);
        _page.AddFill(_gridDone, _ => LeastListHeight(_gridDone), gapBelow: UiLayout.HintGap);
        _page.Add(_doneButtons, gapBelow: UiLayout.RowGap);
        _page.Add(new SplitBar(new ButtonBar().With(_chkPinned), new ButtonBar(alignRight: true).With(btnClose)), gapBelow: 0);

        Controls.Add(_page);

        // Now the lists have their holders on the page to hide.
        ShowDone();

        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);
        Pieces.KeepHeadingsWhole(_grid);
        Pieces.KeepHeadingsWhole(_gridDone);
        Pieces.KeepHeadingsWhole(_gridInvites);
        Pieces.KeepHeadingsWhole(_tally);

        if (_signups is not null)
        {
            _signups.Changed += OnSignupsChanged;
        }

        _tick.Tick += (_, _) => RefreshAll(force: false);
        _tick.Start();

        void StopWatching()
        {
            if (_signups is not null)
            {
                _signups.Changed -= OnSignupsChanged;
            }

            _tick.Stop();
            _tick.Dispose();
            _toastTimer.Stop();
            _toastTimer.Dispose();
        }

        FormClosed += (_, _) => StopWatching();
        Disposed += (_, _) => StopWatching();

        SetFilter(InboxFilter.All);

        DialogFit.Keep(this, _page, width: UiLayout.AtTextSize(1060), height: 760, widthMeasured: true);

        // Not a dialog, so not dressed as one: Minimize and a taskbar button.
        MinimizeBox = true;
        ShowInTaskbar = true;

    }

    // ------------------------------------------------------------ the list

    /// <summary>Every item waiting at every desk, in the desk's order.</summary>
    internal List<InboxItem> Items => _items;

    /// <summary>The chip showing now.</summary>
    internal InboxFilter Filter => _filter;

    /// <summary>Opens on a chip - the button the race director pressed to get here.</summary>
    internal void SetFilter(InboxFilter filter)
    {
        _filter = filter;

        foreach (var (key, chip) in _chipButtons)
        {
            var on = key == (filter == InboxFilter.Vote ? InboxFilter.All : filter);
            chip.BackColor = on ? AppTheme.Primary : AppTheme.SecondaryButton;
            chip.ForeColor = on ? AppTheme.OnPrimary : AppTheme.TextDark;
            chip.AccessibleDescription = on ? "Showing" : null;
        }

        // What only means something under one chip shows only there.
        _noteBox.Visible = _notes is not null && filter is InboxFilter.All or InboxFilter.Notes or InboxFilter.Vote;
        // By the chip, not by Visible: the window picks its chip before it
        // is first shown, and Visible is false for anything not on screen.
        var invitesOn = _signups is not null && filter == InboxFilter.Signups;
        _invites.Visible = invitesOn;

        if (invitesOn)
        {
            FillInvites();
        }

        RefreshAll(force: true);

        if (filter == InboxFilter.Vote)
        {
            _voteCard.Focus();
        }

        _page.PerformLayout();
    }

    /// <summary>
    /// Reads every desk again and redraws what changed. The waiting times
    /// move every second; the rows are rebuilt only when the items do, so a
    /// selection is never pulled out from under the mouse.
    /// </summary>
    internal void RefreshAll(bool force)
    {
        if (IsDisposed)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var snoozed = _snooze.Snoozed(now);

        _items = DeskInbox.Order(ReadAll(), snoozed);

        RefreshState();
        RefreshChips();
        RefreshRules();
        FillVoteCard();

        var shown = _filter == InboxFilter.Vote ? _items : DeskInbox.Filter(_items, _filter);
        var signature = string.Join("|", shown.Select(i => i.Key + ":" + (snoozed.Contains(i.Key) ? "z" : "") + i.What + i.Note));

        if (force || signature != _drawn)
        {
            _drawn = signature;
            DrawRows(shown, snoozed);
            FillDone();
        }

        // The waited column in place, every second.
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not InboxItem item)
            {
                continue;
            }

            var later = snoozed.Contains(item.Key);
            var cell = row.Cells["waited"];
            var text = later ? "Later" : DeskInbox.Waited(now - item.SinceUtc);

            if (!Equals(cell.Value, text))
            {
                cell.Value = text;
            }

            var age = later ? InboxAge.Fine : DeskInbox.Age(item, now);
            var colour = age switch
            {
                InboxAge.Red => AppTheme.Danger,
                InboxAge.Amber => AppTheme.Warning,
                _ => later ? AppTheme.TextMuted : AppTheme.TextDark,
            };

            // On the picked row too - the selection colour used to hide it.
            cell.Style.ForeColor = colour;
            cell.Style.SelectionForeColor = colour;
        }

        _lblEmpty.Visible = shown.Count == 0;
        _grid.Visible = shown.Count > 0;
        _anyWaitingShown = shown.Count > 0;
        ShowWaitingList();
    }

    /// <summary>
    /// An empty waiting list gives its height up to the Done list when that
    /// is showing. With neither, it keeps it, so Close stays at the bottom.
    /// </summary>
    private void ShowWaitingList()
    {
        // Not _grid.Visible: that reads false while its holder is hidden.
        if (_grid.Parent is { } holder)
        {
            var show = _anyWaitingShown || !_showDone;

            if (holder.Visible != show)
            {
                holder.Visible = show;
                _page.PerformLayout();
            }
        }
    }

    /// <summary>The width under which the list drops its Note column, at the text size in use.</summary>
    internal static int NoteColumnWidth => UiLayout.AtTextSize(760);

    /// <summary>The Note column only where there is room for it - see the grid's SizeChanged.</summary>
    internal void FitColumns()
    {
        var wide = _grid.Width >= NoteColumnWidth;

        if (_grid.Columns["note"] is { } note && note.Visible != wide)
        {
            note.Visible = wide;
        }
    }

    private void DrawRows(List<InboxItem> shown, HashSet<string> snoozed)
    {
        var selected = SelectedItems().Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        var current = (_grid.CurrentRow?.Tag as InboxItem)?.Key;

        _grid.Rows.Clear();

        foreach (var item in shown)
        {
            var at = _grid.Rows.Add(string.Empty, item.Badge, item.From, item.What, item.Note);
            var row = _grid.Rows[at];
            row.Tag = item;

            if (item.RaceBlocking)
            {
                row.Cells["kind"].Style.ForeColor = AppTheme.Danger;
                row.Cells["kind"].Style.SelectionForeColor = AppTheme.Danger;
                row.Cells["kind"].Style.Font = AppTheme.BoldFont;
            }

            if (snoozed.Contains(item.Key))
            {
                row.DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }
        }

        // Back onto the rows that were picked, through the current cell -
        // a DataGridView's CurrentRow follows its current cell, and clearing
        // the rows puts that on the first row (12 Oct 2026, club notes).
        var column = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
        _grid.ClearSelection();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not InboxItem item)
            {
                continue;
            }

            if (item.Key == current && column is not null)
            {
                _grid.CurrentCell = row.Cells[column.Index];
            }

            if (selected.Contains(item.Key))
            {
                row.Selected = true;
            }
        }

        if (_grid.Rows.Count > 0 && _grid.SelectedRows.Count == 0 && column is not null)
        {
            _grid.CurrentCell = _grid.Rows[0].Cells[column.Index];
            _grid.Rows[0].Selected = true;
        }

        UpdateDetail();
    }

    private void RefreshChips()
    {
        foreach (var (filter, chip) in _chipButtons)
        {
            var text = DeskInbox.ChipText(filter, DeskInbox.Filter(_items, filter).Count);

            if (chip.Text != text)
            {
                chip.Text = text;
            }
        }

        // The window's own title says the count too - it is what shows on
        // the taskbar when it is minimised.
        var title = _items.Count == 0 ? "Inbox" : $"Inbox ({_items.Count})";

        if (Text != title)
        {
            Text = title;
        }
    }

    private void RefreshState()
    {
        var lines = new List<string>();
        var bad = false;

        if (_requests is not null)
        {
            var (text, isBad) = _requests.StateLine();
            lines.Add(text);
            bad |= isBad;
        }

        if (_signups is not null && _filter == InboxFilter.Signups)
        {
            var (text, isBad) = SignupStateLine();
            lines.Add(text);
            bad |= isBad;
        }

        var all = string.Join(" ", lines.Where(l => l.Length > 0));

        if (_lblState.Text != all)
        {
            _lblState.Text = all;
        }

        _lblState.ForeColor = bad ? AppTheme.Danger : AppTheme.TextMuted;
        _lblState.Visible = all.Length > 0;
    }

    /// <summary>Every desk's waiting items, unordered.</summary>
    private IEnumerable<InboxItem> ReadAll()
    {
        foreach (var item in RequestItems())
        {
            yield return item;
        }

        foreach (var item in NoteItems())
        {
            yield return item;
        }

        foreach (var item in SignupItems())
        {
            yield return item;
        }
    }

    // ------------------------------------------------------------ the selection

    /// <summary>The rows picked, top to bottom.</summary>
    internal List<InboxItem> SelectedItems() =>
        _grid.SelectedRows.Cast<DataGridViewRow>()
            .OrderBy(r => r.Index)
            .Select(r => r.Tag)
            .OfType<InboxItem>()
            .ToList();

    /// <summary>Picks rows as a click (and Ctrl+click) would - the first becomes the current one. For the tests.</summary>
    internal void Pick(params InboxItem[] items)
    {
        _ = _grid.Handle;
        var keys = items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        var column = _grid.Columns.Cast<DataGridViewColumn>().First(c => c.Visible);
        var first = true;

        _grid.ClearSelection();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is InboxItem item && keys.Contains(item.Key))
            {
                if (first)
                {
                    _grid.CurrentCell = row.Cells[column.Index];
                    first = false;
                }

                row.Selected = true;
            }
        }

        UpdateDetail();
    }

    /// <summary>The one row the answer is about: the current one, else the first picked.</summary>
    private InboxItem? Selected() =>
        _grid.CurrentRow?.Tag as InboxItem is { } current && _grid.CurrentRow.Selected ? current : SelectedItems().FirstOrDefault();

    /// <summary>The picked rows when they are all one desk's one kind - what may be answered together.</summary>
    private List<InboxItem>? Batch()
    {
        var picked = SelectedItems();

        return picked.Count > 1 && picked.All(i => i.Source == picked[0].Source && i.Kind == picked[0].Kind) ? picked : null;
    }

    /// <summary>What the detail line says, and which answers mean anything for what is picked.</summary>
    private void UpdateDetail()
    {
        var picked = SelectedItems();
        var item = Selected();

        if (item is null)
        {
            _lblDetail.Text = _items.Count == 0 ? string.Empty : "Pick one to answer it.";
            foreach (var b in new[] { _btnApprove, _btnNoted, _btnDecline, _btnLater, _btnDiscard, _btnRemove })
            {
                b.Enabled = false;
            }

            return;
        }

        var batch = Batch();

        if (picked.Count > 1 && batch is null)
        {
            _lblDetail.Text = $"{picked.Count} picked, of different kinds - pick ones of one kind to answer them together, or one at a time.";
            _btnApprove.Enabled = _btnNoted.Enabled = _btnDecline.Enabled = _btnDiscard.Enabled = _btnRemove.Enabled = false;
            _btnLater.Enabled = true;
            return;
        }

        _lblDetail.Text = batch is not null
            ? $"{batch.Count} picked: {item.Badge}. One answer goes to each of them."
            : Detail(item);

        switch (item.Source)
        {
            case InboxSource.Request:
                UpdateRequestButtons(item, batch);
                break;
            case InboxSource.Note:
                UpdateNoteButtons(item, batch);
                break;
            case InboxSource.Signup:
                UpdateSignupButtons(item, batch);
                break;
        }

        _btnLater.Enabled = true;
    }

    /// <summary>The selected item in full: who, what, and anything the desk should know.</summary>
    private static string Detail(InboxItem item)
    {
        var text = $"{item.From}: {item.What}";
        return item.Note.Length > 0 ? $"{text}  ({item.Note})" : text;
    }

    // ------------------------------------------------------------ answers

    /// <summary>Enter or a double-click: the main answer for what is picked - Approve where there is one, else Noted.</summary>
    private void MainAnswer()
    {
        if (_btnApprove.Enabled)
        {
            Approve();
        }
        else if (_btnNoted.Enabled)
        {
            Noted();
        }
    }

    private void Approve()
    {
        if (Selected() is not { } item || !_btnApprove.Enabled)
        {
            return;
        }

        var batch = Batch();

        switch (item.Source)
        {
            case InboxSource.Request:
                ApproveRequests(batch ?? new List<InboxItem> { item });
                break;
            case InboxSource.Note:
                ApproveNotes(batch ?? new List<InboxItem> { item });
                break;
            case InboxSource.Signup:
                ApproveSignup(item);
                break;
        }

        RefreshAll(force: true);
    }

    private void Noted()
    {
        if (Selected() is not { } item || !_btnNoted.Enabled || item.Source != InboxSource.Request)
        {
            return;
        }

        NoteRequests(Batch() ?? new List<InboxItem> { item });
        RefreshAll(force: true);
    }

    /// <summary>Decline: the quick replies first, then Other reason... for the desk's own words.</summary>
    private void DeclineMenu()
    {
        if (Selected() is not { } item || !_btnDecline.Enabled)
        {
            return;
        }

        if (item.Source == InboxSource.Signup)
        {
            TurnDownSignup(item);
            RefreshAll(force: true);
            return;
        }

        var menu = new ContextMenuStrip();

        foreach (var reply in DeskInbox.QuickReplies(_settings.InboxQuickReplies))
        {
            menu.Items.Add(reply, null, (_, _) => Decline(reply));
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Other reason...", null, (_, _) => Decline(null));
        menu.Items.Add("Change the quick replies...", null, (_, _) => EditQuickReplies());
        AppTheme.ApplyRecursively(menu);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_btnDecline, new Point(0, _btnDecline.Height));
    }

    /// <summary>Declines what is picked, with a quick reply or (null) a reason asked for.</summary>
    internal void Decline(string? reply)
    {
        if (Selected() is not { } item || item.Source != InboxSource.Request)
        {
            return;
        }

        DeclineRequests(Batch() ?? new List<InboxItem> { item }, reply);
        RefreshAll(force: true);
    }

    /// <summary>Later (S): to the foot of the list until the race on now is over, or ten minutes.</summary>
    private void Later()
    {
        var picked = SelectedItems();

        if (picked.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var item in picked)
        {
            if (_snooze.IsSnoozed(item.Key, now))
            {
                _snooze.Wake(item.Key);
            }
            else
            {
                _snooze.Snooze(item.Key, now);
            }
        }

        RefreshAll(force: true);
    }

    /// <summary>The race on now is over: everything put off with Later comes back up.</summary>
    internal void RaceOver()
    {
        _snooze.WakeAll();
        RefreshAll(force: true);
    }

    // ------------------------------------------------------------ keys

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Letters are answers only while nothing is being typed.
        if (ActiveControl is not TextBoxBase && (keyData & (Keys.Control | Keys.Alt)) == 0)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.A when _btnApprove.Enabled:
                    Approve();
                    return true;
                case Keys.N when _btnNoted.Enabled:
                    Noted();
                    return true;
                case Keys.D when _btnDecline.Enabled:
                    DeclineMenu();
                    return true;
                case Keys.S when _btnLater.Enabled:
                    Later();
                    return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------ the toast

    /// <summary>
    /// What was just done, under the answers, for ten seconds - with Undo
    /// when there is one to give. Undo rather than "are you sure?" wherever
    /// the desk can put it back.
    /// </summary>
    internal void Toast(string text, Action? undo = null)
    {
        _lblToast.Text = text;
        _toastUndo = undo;
        _lnkUndo.Visible = undo is not null;
        _toastBar.Visible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
        _page.PerformLayout();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        _toastBar.Visible = false;
        _toastUndo = null;
        _page.PerformLayout();
    }

    private void UndoLast()
    {
        var undo = _toastUndo;
        HideToast();
        undo?.Invoke();
        RefreshAll(force: true);
    }

    /// <summary>
    /// An undo through the app's own Undo, only while what this captured is
    /// still the newest step - Undo takes the newest, and taking somebody
    /// else's change back from here would be worse than not offering it.
    /// </summary>
    private Action? UndoFor(long id) => _undo is null
        ? null
        : () =>
        {
            if (_undo.PendingIds.FirstOrDefault() == id)
            {
                _undo.Undo();
            }
            else
            {
                AppDialog.Show(this, "Something newer has been done since. Use Undo in the main window to work back to it.",
                    "Not undone", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

    /// <summary>The id of the step the app's Undo would take next, or 0.</summary>
    private long NewestUndo() => _undo?.PendingIds.FirstOrDefault() ?? 0;

    /// <summary>The steps put on the app's Undo since <paramref name="before"/> was the newest, newest first.</summary>
    private List<long> StepsSince(long before) =>
        _undo is null ? new List<long>() : _undo.PendingIds.TakeWhile(id => id != before).ToList();

    /// <summary>
    /// Undo for the steps one answer put on the app's Undo - all of a batch
    /// at once - only while they are still the newest: otherwise null-safe
    /// and it says so rather than taking somebody else's change back.
    /// </summary>
    private Action? UndoForSteps(IReadOnlyList<long> steps) => _undo is null || steps.Count == 0
        ? null
        : () =>
        {
            if (_undo.UndoThrough(steps) is null)
            {
                AppDialog.Show(this, "Something newer has been done since. Use Undo in the main window to work back to it.",
                    "Not undone", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

    // ------------------------------------------------------------ docked

    /// <summary>"Dock in the main window" was ticked (true) or unticked: the host moves the window.</summary>
    internal event Action<bool>? DockWanted;

    /// <summary>The tick, as the host has it now.</summary>
    internal void ShowDocked(bool docked)
    {
        if (_chkPinned.Checked != docked)
        {
            _chkPinned.Checked = docked;
        }
    }

    // ------------------------------------------------------------ pieces

    private static int LeastListHeight(DataGridView grid) =>
        grid.ColumnHeadersHeight + LeastRows * grid.RowTemplate.Height + 2;

    private static void SetUpGrid(DataGridView grid)
    {
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    }

    private static string WhenText(DateTime utc) =>
        utc.ToLocalTime().ToString("ddd ", CultureInfo.CurrentCulture) + AppTime.TimeOfDay(utc.ToLocalTime());

    /// <summary>The club's quick replies, one a line, in a box of their own.</summary>
    private void EditQuickReplies()
    {
        using var form = new QuickRepliesForm(DeskInbox.QuickReplies(_settings.InboxQuickReplies));

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _settings.InboxQuickReplies = form.Replies.ToList();
            _settings.Save();
        }
    }
}

/// <summary>The race the vote card offers to open a vote on - from Race Control, which knows what is staged.</summary>
/// <param name="RaceKey">The round about to run, by its own id. Null when Race Control has no round picked.</param>
/// <param name="RaceName">Its label, for the button and for the announcer.</param>
/// <param name="ClassName">Its class.</param>
/// <param name="Grid">The drivers staged for it.</param>
/// <param name="WhereToVote">The address to read out to the room, when voting from the room is on.</param>
internal sealed record VoteTarget(string? RaceKey, string? RaceName, string? ClassName, IReadOnlyList<VoteOption> Grid, string? WhereToVote);

/// <summary>The club's quick replies for a decline, one a line.</summary>
internal sealed class QuickRepliesForm : Form
{
    private readonly TextBox _box = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };

    public QuickRepliesForm(IReadOnlyList<string> replies)
    {
        Text = "Quick replies";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var blurb = new Label
        {
            Text = "What Decline offers before Other reason..., one a line, up to eight. The driver's phone shows the words as they are. "
                 + "Leave it empty to go back to HeatCaller's own.",
            AutoSize = true,
            MaximumSize = new Size(UiLayout.AtTextSize(460), 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _box.Text = string.Join(Environment.NewLine, replies);
        _box.Height = UiLayout.AtTextSize(160);

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        page.Add(blurb, gapBelow: UiLayout.RowGap);
        page.AddFill(_box, _ => UiLayout.AtTextSize(120), gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        Controls.Add(page);

        CancelButton = cancel;
        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 500, height: 320);
    }

    /// <summary>
    /// The cursor at the end, nothing selected (15 Nov 2026, Asher: "quick
    /// replies load with the text selected"). The box is the first thing
    /// focused, and Windows selects all of a text box it tabs into - one
    /// key pressed then wiped every reply.
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UnselectReplies();
    }

    /// <summary>The box's cursor put after the last reply, with nothing selected.</summary>
    internal void UnselectReplies()
    {
        _box.Select(_box.TextLength, 0);
        _box.ScrollToCaret();
    }

    /// <summary>The replies box, for tests.</summary>
    internal TextBox RepliesBox => _box;

    /// <summary>The lines typed, tidied.</summary>
    internal IReadOnlyList<string> Replies =>
        _box.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(8).ToList();
}
