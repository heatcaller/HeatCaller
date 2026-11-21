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

    /// <summary>
    /// Holds the layout pieces' layouts while the window is built, let go
    /// at the end of the constructor (LayoutFreeze.HoldWhileBuilding) -
    /// first among the fields, so the pages, the rail and the bars made
    /// below are made under it too. Why (19 Nov 2026): built without one,
    /// the window was laid out again for every part put in it.
    /// </summary>
    private readonly IDisposable _building = LayoutFreeze.HoldWhileBuilding();

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
    private readonly InboxPill _pillState = new() { Text = "On", Tone = PillTone.Good };
    private readonly Button _btnVoteTop = new() { Text = "Room vote" };
    private readonly Button _btnRulesTop = new() { Text = "Rules" };

    /// <summary>The filters down the left (wide) or across the top (docked), with Room vote and Done under them.</summary>
    private readonly InboxRail _rail = new();
    private readonly Dictionary<InboxFilter, RailEntry> _chipButtons = new();
    private RailEntry? _railVote;
    private RailEntry? _railDone;

    /// <summary>The middle column: the list, the invites, the Done list and the note box.</summary>
    private readonly FillPage _list = new();

    /// <summary>The Done list, shown in the middle in the list's place.</summary>
    private readonly FillPage _doneView = new();

    /// <summary>The right-hand column (wide) or the foot (docked): the picked item and its answers.</summary>
    private readonly FillPage _detail = new();
    private InboxBody _body = null!;
    private KeyStrip _keys = null!;

    private readonly DataGridView _grid = new();
    private readonly Label _lblEmpty = new();
    private readonly InboxPill _pillWaited = new() { Tone = PillTone.Plain };
    private readonly Label _lblKind = new();
    private readonly Label _lblWhat = new();
    private readonly Label _lblDetail = new();
    private readonly Label _lblReplies = new();

    /// <summary>Where the picked one is in the list: "1 of 7".</summary>
    private readonly Label _lblPosition = new();

    /// <summary>Beside the toast's Undo: the key that does the same.</summary>
    private readonly Label _lblUndoKey = new();
    private readonly ReplyChips _replyChips = new();

    /// <summary>The caption above each group of rows in the list, by the row it is drawn over.</summary>
    private readonly Dictionary<int, (string Text, bool Blocking)> _groupCaptions = new();
    private readonly Button _btnApprove = new() { Text = "Approve..." };
    private readonly Button _btnNoted = new() { Text = "Noted" };
    private readonly Button _btnDecline = new() { Text = "Decline..." };
    private readonly Button _btnLater = new() { Text = "Later" };
    private readonly Button _btnDiscard = new() { Text = "Discard" };
    private readonly Button _btnRemove = new() { Text = "Remove" };
    private readonly AnswerGrid _actions = new();
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
        using var building = _building;

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
        _lblState.MaximumSize = new Size(UiLayout.AtTextSize(760), 0);
        _lblState.Margin = new Padding(0, UiLayout.AtDpi(4), 0, 0);

        // The rail: the seven filters, each with the number key that picks
        // it, then tonight's room vote and what has been answered.
        var key = 1;

        foreach (var filter in new[] { InboxFilter.All, InboxFilter.RaceBlocking, InboxFilter.Requests, InboxFilter.Queries, InboxFilter.Crew, InboxFilter.Notes, InboxFilter.Signups })
        {
            _chipButtons[filter] = _rail.Add(new RailEntry(filter, DeskInbox.ChipWords(filter), (key++).ToString())
            {
                CaptionAbove = filter == InboxFilter.All ? "Waiting" : null,
                Tone = filter == InboxFilter.RaceBlocking ? RailTone.Blocking : RailTone.Normal,

                // Docked, the chips are short so they wrap onto fewer rows.
                Short = filter switch
                {
                    InboxFilter.RaceBlocking => "Blocking",
                    InboxFilter.Queries => "Queries",
                    InboxFilter.Crew => "Crew",
                    InboxFilter.Notes => "Club",
                    _ => null,
                },
            });
        }

        // Docked, the vote is the top bar's button alone.
        if (vote is not null)
        {
            _railVote = _rail.Add(new RailEntry(InboxFilter.Vote, "Room vote", "V") { CaptionAbove = "Tonight", Tone = RailTone.Live, ListOnly = true });
        }

        _railDone = _rail.Add(new RailEntry("done", "Done", "H") { CaptionAbove = vote is null ? "Tonight" : null });

        _rail.Picked += id =>
        {
            if (id is InboxFilter.Vote)
            {
                ShowVotePopover();
            }
            else if (id is InboxFilter filter)
            {
                SetFilter(filter);
            }
            else
            {
                _showDone = !_showDone;
                ShowDone();
            }
        };

        BuildVoteCard();

        SetUpGrid(_grid);
        _grid.MultiSelect = true;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "waited", HeaderText = "Waited", FillWeight = 36 },
            new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "Kind", FillWeight = 64 },
            new DataGridViewTextBoxColumn { Name = "from", HeaderText = "From", FillWeight = 62 },
            new DataGridViewTextBoxColumn { Name = "what", HeaderText = "What", FillWeight = 150 },
            new DataGridViewTextBoxColumn { Name = "note", HeaderText = "Note", FillWeight = 96 });

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

        // Each group of rows under a caption of its own - what holds up the
        // next race, what can wait, what was put off - drawn in room left
        // over the group's first row, so the rows are still only the items.
        _grid.RowPostPaint += (_, e) => PaintRowExtras(e);
        _grid.CellPainting += (_, e) =>
        {
            PaintWaitedCell(e);
            PaintStackedCell(e);
        };

        // Docked, the What column says who and what kind on a line of its
        // own above what - for the height; PaintStackedCell draws it.
        _grid.CellFormatting += (_, e) =>
        {
            if (_stackedRows && e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "what" && _grid.Rows[e.RowIndex].Tag is InboxItem item)
            {
                e.Value = $"{item.From} · {item.Badge}{Environment.NewLine}{item.What}";
                e.FormattingApplied = true;
            }
        };

        _lblEmpty.AutoSize = true;
        _lblEmpty.ForeColor = AppTheme.TextMuted;
        _lblEmpty.Font = AppTheme.HeadingFont;
        _lblEmpty.Text = "All clear - nothing is waiting for you.";
        _lblEmpty.Visible = false;
        _lblEmpty.Margin = new Padding(UiLayout.AtDpi(4), UiLayout.AtDpi(24), 0, 0);

        // The picked item, in full: how long it has waited, what kind, who
        // and what in one sentence, then what the desk should know.
        _lblKind.AutoSize = true;
        _lblKind.Font = AppTheme.MicroFont;
        _lblKind.ForeColor = AppTheme.TextMuted;

        _lblWhat.AutoSize = true;
        _lblWhat.Font = AppTheme.HeadingFont;
        _lblWhat.UseMnemonic = false;

        _lblDetail.AutoSize = true;
        _lblDetail.MaximumSize = new Size(UiLayout.AtTextSize(980), 0);
        _lblDetail.ForeColor = AppTheme.TextMuted;
        _lblDetail.UseMnemonic = false;

        _lblReplies.AutoSize = true;
        _lblReplies.Font = AppTheme.MicroFont;
        _lblReplies.ForeColor = AppTheme.TextMuted;
        _lblReplies.Text = InboxRail.Spaced("Decline replies");
        _replyChips.Picked += reply => Decline(reply);

        _btnApprove.Click += (_, _) => Approve();
        _btnNoted.Click += (_, _) => Noted();
        _btnDecline.Click += (_, _) => DeclineMenu();
        _btnLater.Click += (_, _) => Later();
        _btnDiscard.Click += (_, _) => Discard();
        _btnRemove.Click += (_, _) => RemoveNote(Selected() is { Source: InboxSource.Note, Payload: ClubNote waiting } ? waiting : null);
        _actions.With(_btnApprove, _btnNoted, _btnDecline, _btnLater, _btnDiscard, _btnRemove);
        KeyHint.Attach(_btnApprove, "A");
        KeyHint.Attach(_btnNoted, "N");
        KeyHint.Attach(_btnDecline, "D");
        KeyHint.Attach(_btnLater, "S");
        KeyHint.Attach(_btnDiscard, "Del");
        KeyHint.Attach(_btnRemove, "Del");

        _lblToast.AutoSize = true;
        _lblToast.Font = AppTheme.SmallFont;
        _lblToast.UseMnemonic = false;
        _lblToast.MaximumSize = new Size(UiLayout.AtTextSize(250), 0);
        _lnkUndo.AutoSize = true;
        _lnkUndo.LinkClicked += (_, _) => UndoLast();
        _lblUndoKey.AutoSize = true;
        _lblUndoKey.Font = AppTheme.SmallFont;
        _lblUndoKey.Text = "Ctrl+Z";
        _toastBar.With(_lblToast, _lnkUndo, _lblUndoKey);
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

        // ---- the top bar: the state, and the room vote and rules, which open
        // as cards under their buttons rather than taking room in the window.
        _btnVoteTop.Click += (_, _) => ShowVotePopover();
        _btnVoteTop.Visible = _vote is not null;
        _btnRulesTop.Click += (_, _) => ShowRulesPopover();
        var top = new InboxBand(new SplitBar(
            new ButtonBar().With(_pillState, _lblState),
            new ButtonBar(alignRight: true, tight: true).With(_btnRulesTop, _btnVoteTop)), line: InboxBandLine.Below);

        // ---- the middle: the list...
        _list.Padding = new Padding(UiLayout.AtDpi(12), UiLayout.AtDpi(10), UiLayout.AtDpi(12), UiLayout.AtDpi(10));
        _list.AddFill(_grid, _ => LeastListHeight(_grid), gapBelow: UiLayout.RowGap);
        _list.Add(_invites, gapBelow: UiLayout.RowGap);
        _list.Add(_noteBox, gapBelow: 0);

        // The Done list is a view of its own, in the list's place.
        _doneView.Padding = _list.Padding;
        _doneView.AddFill(_gridDone, _ => LeastListHeight(_gridDone), gapBelow: UiLayout.RowGap);
        _doneView.Add(_doneButtons, gapBelow: 0);

        // ...and the picked item beside it.
        _detail.Tag = AppTheme.KeepBackColor;
        _detail.BackColor = AppTheme.Surface;
        _detail.Padding = new Padding(UiLayout.AtDpi(16), UiLayout.AtDpi(14), UiLayout.AtDpi(16), UiLayout.AtDpi(14));
        _lblPosition.AutoSize = true;
        _lblPosition.Font = AppTheme.SmallFont;
        _lblPosition.Margin = new Padding(UiLayout.AtDpi(4), UiLayout.AtDpi(6), 0, 0);
        _lnkSheet.Margin = new Padding(UiLayout.AtDpi(10), UiLayout.AtDpi(6), 0, 0);
        _lnkSheet.Visible = false;
        _lnkSheet.LinkClicked += (_, _) => ToggleSheet();
        _detail.Add(new ButtonBar().With(_pillWaited, _lblPosition, _lnkSheet), gapBelow: UiLayout.RowGap);
        _detail.Add(_lblKind, gapBelow: UiLayout.HintGap);
        _detail.Add(_lblWhat, gapBelow: UiLayout.HintGap);
        _detail.Add(_lblDetail, gapBelow: UiLayout.RowGap);
        _detail.Add(_phone, gapBelow: UiLayout.RowGap);
        _detail.Add(_actions, gapBelow: UiLayout.RowGap);
        _detail.Add(_lblReplies, gapBelow: UiLayout.HintGap);
        _detail.Add(_replyChips, gapBelow: UiLayout.RowGap);
        _detail.Add(_toastBar, gapBelow: 0);

        _body = new InboxBody(_rail, _list, _doneView, _detail);
        _body.WideChanged += wide => FitToWidth(wide);

        // ---- the foot: the keys, then Dock and Close where they always were.
        _keys = new KeyStrip(
            (new[] { "↑", "↓" }, "move"),
            (new[] { "Ctrl" }, "+ click several of one kind"),
            (new[] { "Enter" }, "main answer"),
            (new[] { "1–7" }, "filters"),
            (new[] { "V" }, "vote"),
            (new[] { "H" }, "done"));
        var foot = new InboxBand(new SplitBar(
            new ButtonBar().With(_keys),
            new ButtonBar(alignRight: true, tight: true).With(btnClose, _chkPinned)), line: InboxBandLine.Above)
        {
            Shade = true,
        };

        _page.Padding = Padding.Empty;
        _page.Add(top, gapBelow: 0);
        _page.AddFill(_body, w => _body.LeastHeight(w), gapBelow: 0);
        _page.Add(foot, gapBelow: 0);

        Controls.Add(_page);

        // Now the lists have their holders on the page to hide.
        ShowDone();

        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // The cards that open under the top bar's buttons are not in the
        // window, so the theme pass above never reached them.
        AppTheme.ApplyRecursively(_voteCard);
        AppTheme.ApplyRecursively(_rules);
        _votePop = new InboxPopover(_voteCard, UiLayout.AtTextSize(420));
        _rulesPop = new InboxPopover(_rules, UiLayout.AtTextSize(420));

        // Grounds the theme pass flattened back to the page's.
        _detail.BackColor = AppTheme.Surface;
        _lblWhat.Font = AppTheme.HeadingFont;
        _lblKind.Font = AppTheme.MicroFont;
        _lblReplies.Font = AppTheme.MicroFont;
        _lblEmpty.Font = AppTheme.HeadingFont;
        _lblPosition.ForeColor = AppTheme.TextMuted;
        _lblUndoKey.ForeColor = InboxInk.Faint;
        _lnkSheet.LinkColor = _lnkSheet.ActiveLinkColor = _lnkSheet.VisitedLinkColor = AppTheme.Primary;
        _lnkSheet.LinkBehavior = LinkBehavior.HoverUnderline;

        foreach (var b in new[] { _btnApprove, _btnNoted, _btnDecline, _btnLater, _btnDiscard, _btnRemove })
        {
            b.MinimumSize = Size.Empty;
            b.AutoSize = false;
        }

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
            _votePop?.Dispose();
            _rulesPop?.Dispose();
        }

        FormClosed += (_, _) => StopWatching();
        Disposed += (_, _) => StopWatching();

        SetFilter(InboxFilter.All);

        DialogFit.Keep(this, _page, width: UiLayout.AtTextSize(1200), height: 760, widthMeasured: true);

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

        // A filter picked is back to what is waiting.
        if (_showDone)
        {
            _showDone = false;
            ShowDone();
        }

        _rail.PickedId = filter == InboxFilter.Vote ? InboxFilter.All : filter;

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

        // Race Control's vote button opens on the list with the vote's card
        // open over it - once there is a window for it to open over.
        if (filter == InboxFilter.Vote)
        {
            if (Visible && IsHandleCreated)
            {
                BeginInvoke(ShowVotePopover);
            }
            else
            {
                _voteWhenShown = true;
            }
        }

        _list.PerformLayout();
        _page.PerformLayout();
    }

    /// <summary>Opened on the vote before the window was up: its card opens once it is.</summary>
    private bool _voteWhenShown;

    private InboxPopover? _votePop;
    private InboxPopover? _rulesPop;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_voteWhenShown)
        {
            _voteWhenShown = false;
            BeginInvoke(ShowVotePopover);
        }
    }

    /// <summary>The room vote's card, under the top bar's vote button.</summary>
    private void ShowVotePopover()
    {
        if (_vote is null || _votePop is null || IsDisposed)
        {
            return;
        }

        _rulesPop?.Hide();
        FillVoteCard();
        _votePop.Show(_btnVoteTop.Visible && _btnVoteTop.IsHandleCreated ? _btnVoteTop : this);
    }

    /// <summary>The rules' card, under the top bar's Rules button.</summary>
    private void ShowRulesPopover()
    {
        if (_rulesPop is null || IsDisposed)
        {
            return;
        }

        _votePop?.Hide();
        RefreshRules();
        _rulesPop.Show(_btnRulesTop);
    }

    /// <summary>
    /// The state line as wide as the top bar has beside its pill and the
    /// vote and rules buttons, so all of it stays on one row and wraps
    /// itself rather than pushing the buttons under it.
    /// </summary>
    private void FitTopBar()
    {
        var buttons = (_btnVoteTop.Visible || !Visible ? _btnVoteTop.Width + _btnVoteTop.Margin.Horizontal : 0)
            + _btnRulesTop.Width + _btnRulesTop.Margin.Horizontal;
        var pill = _pillState.Width + _pillState.Margin.Horizontal;
        var room = ClientSize.Width - UiLayout.PageLeft - UiLayout.PageRight - buttons - pill - UiLayout.SectionGap - _lblState.Margin.Horizontal;
        var width = Math.Max(UiLayout.AtTextSize(220), room);

        if (_lblState.MaximumSize.Width != width)
        {
            _lblState.MaximumSize = new Size(width, 0);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitTopBar();
    }

    /// <summary>
    /// Wide or docked: the key strip only where there is room to read it -
    /// every key still works without it - and the answers in a card or a bar.
    /// </summary>
    private void FitToWidth(bool wide)
    {
        // Back to the list whenever the width changes.
        if (wide && _sheetOpen)
        {
            _sheetOpen = false;
            _body.SheetOpen = false;
        }

        _keys.Visible = wide;
        _actions.Bar = !wide && !_sheetOpen;
        _lblWhat.Font = wide ? AppTheme.HeadingFont : AppTheme.BoldFont;

        // Docked, the picked item is a bar: who and what on a line or two,
        // then the answers - the kind and the note are in its row above.
        // Details opens it out over the list, everything the wide panel has.
        var full = wide || _sheetOpen;
        _lblKind.Visible = full;
        _lblDetail.Visible = full;
        _noteCaption.Visible = wide;
        _lnkSheet.Visible = !wide;
        _lnkSheet.Text = _sheetOpen ? "Back to the list" : "Details";
        RefreshState();
        FitColumns();
        FitTopBar();
        _lblReplies.Visible = full && _replyChips.Replies.Count > 0;
        _replyChips.Visible = full && _replyChips.Replies.Count > 0;
        _pillWaited.Visible = full && Selected() is not null;
        ShowPhone();

        // Asked from inside the middle's own layout - WinForms drops a
        // layout asked for while one is running, so it waits its turn.
        if (IsHandleCreated)
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed)
                {
                    _detail.PerformLayout();
                    _page.PerformLayout();
                }
            });
        }
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

        // "All clear" in the list's own place, where the rows would be.
        _lblEmpty.Visible = shown.Count == 0;
        _grid.Visible = shown.Count > 0;
        _anyWaitingShown = shown.Count > 0;
        ShowWaitingList();
        ShowWaited(now, snoozed);
    }

    /// <summary>The picked item's pill: how long it has waited, in the list's colours, every second.</summary>
    private void ShowWaited(DateTime now, HashSet<string> snoozed)
    {
        var picked = SelectedItems();
        var position = picked.Count switch
        {
            0 => string.Empty,
            1 => _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag == picked[0]) is { } row ? $"{row.Index + 1} of {_grid.Rows.Count}" : string.Empty,
            _ => $"{picked.Count} of {_grid.Rows.Count} picked",
        };

        if (_lblPosition.Text != position)
        {
            _lblPosition.Text = position;
        }

        if (Selected() is not { } item || picked.Count > 1)
        {
            _pillWaited.Visible = false;
            return;
        }

        var later = snoozed.Contains(item.Key);
        var waited = DeskInbox.Waited(now - item.SinceUtc);
        var age = later ? InboxAge.Fine : DeskInbox.Age(item, now);
        var text = later
            ? "Later - back after this race"
            : item.RaceBlocking
                ? $"Race-blocking · waited {waited}"
                : waited == "now" ? "Just in" : $"Waited {waited}";

        _pillWaited.Text = text;
        _pillWaited.Tone = later ? PillTone.Plain : item.RaceBlocking || age == InboxAge.Red ? PillTone.Danger : age == InboxAge.Amber ? PillTone.Warning : PillTone.Plain;

        var show = _body is null || _body.Wide || _sheetOpen;

        if (_pillWaited.Visible != show)
        {
            _pillWaited.Visible = show;
            _detail.PerformLayout();
        }
    }

    /// <summary>
    /// The list's place keeps its height when nothing waits, and says "All
    /// clear" at the top of it - the list's holder never hides (a hidden
    /// filling part still kept its share of the height, which pushed the
    /// words to the foot of the column).
    /// </summary>
    private void ShowWaitingList()
    {
        if (_grid.Parent is { } holder && _lblEmpty.Parent != holder)
        {
            holder.Controls.Add(_lblEmpty);
            _lblEmpty.Location = new Point(UiLayout.AtDpi(8), UiLayout.AtDpi(28));
            _lblEmpty.BringToFront();

            // Wrapped to the list's width, never past its edge (at a large text size).
            void Wrap() => _lblEmpty.MaximumSize = new Size(Math.Max(1, holder.ClientSize.Width - _lblEmpty.Left - UiLayout.AtDpi(8)), 0);
            holder.SizeChanged += (_, _) => Wrap();
            Wrap();
        }
    }

    /// <summary>The width under which the list drops its Note column, at the text size in use.</summary>
    internal static int NoteColumnWidth => UiLayout.AtTextSize(600);

    /// <summary>The Note column only where there is room for it - see the grid's SizeChanged.</summary>
    internal void FitColumns()
    {
        var wide = _grid.Width >= NoteColumnWidth;

        if (_grid.Columns["note"] is { } note && note.Visible != wide)
        {
            note.Visible = wide;
        }

        // Docked, each row is two lines in one column - who and what kind,
        // then what - rather than four columns too narrow to read.
        var stacked = _body is { Wide: false };

        foreach (var name in new[] { "kind", "from" })
        {
            if (_grid.Columns[name] is { } column && column.Visible == stacked)
            {
                column.Visible = !stacked;
            }
        }

        if (_stackedRows != stacked)
        {
            _stackedRows = stacked;
            _grid.AutoResizeRows();
            _grid.Invalidate();
        }
    }

    /// <summary>The list's rows are two lines in one column (docked) - see FitColumns.</summary>
    private bool _stackedRows;

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

        CaptionGroups(shown, snoozed);

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

    /// <summary>What a group of rows is: holding up the next race, any time tonight, or put off.</summary>
    private enum RowGroup
    {
        Blocking,
        AnyTime,
        Later,
    }

    /// <summary>
    /// The caption over the first row of each group, and the room for it
    /// above that row's words. The list is in this order already (see
    /// DeskInbox.Order): race-blocking first, then the rest, then Later.
    /// </summary>
    private void CaptionGroups(List<InboxItem> shown, HashSet<string> snoozed)
    {
        _groupCaptions.Clear();

        RowGroup GroupOf(InboxItem item) =>
            snoozed.Contains(item.Key) ? RowGroup.Later : item.RaceBlocking ? RowGroup.Blocking : RowGroup.AnyTime;

        var counts = shown.GroupBy(GroupOf).ToDictionary(g => g.Key, g => g.Count());
        var race = _voteTarget?.Invoke()?.RaceName;
        RowGroup? last = null;
        var room = CaptionRoom;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not InboxItem item)
            {
                continue;
            }

            var group = GroupOf(item);

            if (group != last)
            {
                var count = counts[group];
                var text = group switch
                {
                    RowGroup.Blocking => race is { Length: > 0 } ? $"Holding up {race} · {count}" : $"Holding up the next race · {count}",
                    RowGroup.Later => $"Later · {count}",
                    _ => $"Any time tonight · {count}",
                };

                _groupCaptions[row.Index] = (text, group == RowGroup.Blocking);
                row.DefaultCellStyle.Padding = new Padding(0, room, 0, 0);
                row.MinimumHeight = room + _grid.RowTemplate.Height;
                last = group;
            }
        }
    }

    /// <summary>The room a group's caption takes over its first row.</summary>
    private static int CaptionRoom => AppTheme.MicroFont.Height + UiLayout.AtDpi(14);

    /// <summary>A group's caption over its first row, and the picked rows' edge down the left.</summary>
    private void PaintRowExtras(DataGridViewRowPostPaintEventArgs e)
    {
        var g = e.Graphics;
        var row = e.RowBounds;
        var width = _grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);

        if (_groupCaptions.TryGetValue(e.RowIndex, out var caption))
        {
            var band = new Rectangle(row.X, row.Y, width, CaptionRoom);

            using (var back = new SolidBrush(_grid.BackgroundColor))
            {
                g.FillRectangle(back, band);
            }

            TextRenderer.DrawText(g, InboxRail.Spaced(caption.Text), AppTheme.MicroFont,
                new Rectangle(band.X + UiLayout.AtDpi(8), band.Y + UiLayout.AtDpi(4), band.Width, band.Height - UiLayout.AtDpi(4)),
                caption.Blocking ? AppTheme.Danger : AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        if (e.RowIndex >= 0 && e.RowIndex < _grid.Rows.Count && _grid.Rows[e.RowIndex].Selected)
        {
            var top = row.Y + (_groupCaptions.ContainsKey(e.RowIndex) ? CaptionRoom : 0);
            using var edge = new SolidBrush(AppTheme.Primary);
            g.FillRectangle(edge, row.X, top, UiLayout.AtDpi(3), row.Bottom - top);
        }
    }

    /// <summary>Docked: the What cell as two lines - who (bold) and what kind, then what.</summary>
    private void PaintStackedCell(DataGridViewCellPaintingEventArgs e)
    {
        if (!_stackedRows || e.Handled || e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "what"
            || _grid.Rows[e.RowIndex].Tag is not InboxItem item || e.Graphics is not { } g)
        {
            return;
        }

        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        e.PaintBackground(e.CellBounds, selected);

        var pad = e.CellStyle?.Padding ?? Padding.Empty;
        var box = new Rectangle(e.CellBounds.X + UiLayout.AtDpi(6), e.CellBounds.Y + pad.Top + UiLayout.AtDpi(4),
            e.CellBounds.Width - UiLayout.AtDpi(12), e.CellBounds.Height - pad.Top - UiLayout.AtDpi(6));
        var ink = selected ? e.CellStyle?.SelectionForeColor ?? AppTheme.TextDark : e.CellStyle?.ForeColor ?? AppTheme.TextDark;

        var name = TextRenderer.MeasureText(g, item.From, AppTheme.BoldFont, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, item.From, AppTheme.BoldFont, new Rectangle(box.X, box.Y, Math.Min(name.Width + 2, box.Width), name.Height), ink,
            TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, " · " + item.Badge, AppTheme.SmallFont, new Rectangle(box.X + name.Width, box.Y, Math.Max(1, box.Width - name.Width), name.Height),
            item.RaceBlocking ? AppTheme.Danger : AppTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, item.What, e.CellStyle?.Font ?? AppTheme.BodyFont, new Rectangle(box.X, box.Y + name.Height + UiLayout.AtDpi(2), box.Width, Math.Max(1, box.Height - name.Height)),
            selected ? ink : AppTheme.TextMuted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        e.Handled = true;
    }

    /// <summary>The Waited column as a tag: tinted amber, then red, as it gets late.</summary>
    private void PaintWaitedCell(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "waited"
            || _grid.Rows[e.RowIndex].Tag is not InboxItem item || e.Graphics is not { } g)
        {
            return;
        }

        e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);

        var text = e.FormattedValue as string ?? string.Empty;
        var later = text == "Later";
        var age = later ? InboxAge.Fine : DeskInbox.Age(item, DateTime.UtcNow);
        var ink = age switch
        {
            InboxAge.Red => AppTheme.Danger,
            InboxAge.Amber => AppTheme.Warning,
            _ => AppTheme.TextMuted,
        };

        var top = e.CellBounds.Y + (_groupCaptions.ContainsKey(e.RowIndex) ? CaptionRoom : 0);
        var font = later ? AppTheme.SmallFont : AppTheme.BoldFont;
        var size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
        // Level with the row's first line of words, never past its foot.
        var tall = size.Height + UiLayout.AtDpi(4);
        var down = Math.Clamp(e.CellBounds.Bottom - top - tall - UiLayout.AtDpi(2), 0, UiLayout.AtDpi(5));
        var box = new Rectangle(e.CellBounds.X + UiLayout.AtDpi(6), top + down,
            Math.Min(e.CellBounds.Width - UiLayout.AtDpi(10), Math.Max(size.Width + UiLayout.AtDpi(14), UiLayout.AtTextSize(44))),
            tall);

        if (age != InboxAge.Fine)
        {
            var behind = (e.State & DataGridViewElementStates.Selected) != 0 ? e.CellStyle?.SelectionBackColor ?? _grid.BackgroundColor : e.CellStyle?.BackColor ?? _grid.BackgroundColor;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = InboxInk.Rounded(new RectangleF(box.X, box.Y, box.Width, box.Height), UiLayout.AtDpi(5));
            using var tint = new SolidBrush(InboxInk.Mix(behind, ink, 0.22f));
            g.FillPath(tint, path);
        }

        TextRenderer.DrawText(g, text, font, box, age == InboxAge.Fine ? (later ? InboxInk.Faint : AppTheme.TextMuted) : InboxInk.Mix(ink, Color.White, 0.2f),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        e.Handled = true;
    }

    private void RefreshChips()
    {
        var changed = false;

        foreach (var (filter, chip) in _chipButtons)
        {
            var count = DeskInbox.Filter(_items, filter).Count;
            var text = count == 0 ? string.Empty : Math.Min(count, 99).ToString(System.Globalization.CultureInfo.CurrentCulture);

            if (chip.Count != text)
            {
                chip.Count = text;
                changed = true;
            }
        }

        if (_railDone is { } done)
        {
            var count = DoneCount();
            var text = count == 0 ? string.Empty : count.ToString(System.Globalization.CultureInfo.CurrentCulture);

            if (done.Count != text)
            {
                done.Count = text;
                changed = true;
            }
        }

        if (_railVote is { } voteEntry && _vote is { } vote)
        {
            var text = vote.Taking ? "●" : string.Empty;

            if (voteEntry.Count != text)
            {
                voteEntry.Count = text;
                changed = true;
            }
        }

        if (changed)
        {
            _rail.Changed();
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

        // The state's first word goes on the pill: "On" in green, "Off" plain,
        // anything wrong in red - the rest of the line says why.
        var (pill, tone) = all.StartsWith("On. ", StringComparison.Ordinal) ? ("On", PillTone.Good)
            : all.StartsWith("Off. ", StringComparison.Ordinal) ? ("Off", PillTone.Plain)
            : bad ? ("Needs a look", PillTone.Danger)
            : (string.Empty, PillTone.Plain);

        if (pill is "On" or "Off")
        {
            all = all[(pill.Length + 2)..];
        }

        if (_lblState.Text != all)
        {
            _lblState.Text = all;
        }

        if (_pillState.Text != pill)
        {
            _pillState.Text = pill;
        }

        _pillState.Tone = tone;
        _pillState.Visible = pill.Length > 0;
        _lblState.ForeColor = bad ? AppTheme.Danger : AppTheme.TextMuted;

        // Docked, the pill alone says all is well; the words only when it isn't.
        _lblState.Visible = all.Length > 0 && (bad || _body is null || _body.Wide);
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
        UpdateDetailCore();
        ShowAnswersInOrder();
        ShowPhone();
        ShowReplies();
        ShowWaited(DateTime.UtcNow, _snooze.Snoozed(DateTime.UtcNow));
        _detail.PerformLayout();
    }

    private void UpdateDetailCore()
    {
        var picked = SelectedItems();
        var item = Selected();
        var width = Math.Max(UiLayout.AtTextSize(200), (_body is { Wide: false } ? _detail.Width : InboxBody.DetailWidth) - _detail.Padding.Horizontal - UiLayout.AtDpi(4));
        _lblWhat.MaximumSize = new Size(width, 0);
        _lblDetail.MaximumSize = new Size(width, 0);
        _lblKind.MaximumSize = new Size(width, 0);

        if (item is null)
        {
            ShowNothingPicked();

            foreach (var b in new[] { _btnApprove, _btnNoted, _btnDecline, _btnLater, _btnDiscard, _btnRemove })
            {
                b.Enabled = false;
            }

            _actions.Visible = _grid.Rows.Count > 0;
            return;
        }

        _actions.Visible = true;
        var batch = Batch();

        if (picked.Count > 1 && batch is null)
        {
            _lblKind.Text = InboxRail.Spaced($"{picked.Count} picked");
            _lblWhat.Text = string.Join(Environment.NewLine, picked.Take(6).Select(i => $"{i.Badge}: {i.From}"));
            _lblDetail.Text = $"{picked.Count} picked, of different kinds - pick ones of one kind to answer them together, or one at a time.";
            _btnApprove.Enabled = _btnNoted.Enabled = _btnDecline.Enabled = _btnDiscard.Enabled = _btnRemove.Enabled = false;
            _btnLater.Enabled = true;
            return;
        }

        if (batch is not null)
        {
            _lblKind.Text = InboxRail.Spaced($"{batch.Count} × {item.Badge}");
            _lblWhat.Text = string.Join(Environment.NewLine, batch.Take(6).Select(i => $"{i.From}: {i.What}"))
                + (batch.Count > 6 ? $"{Environment.NewLine}and {batch.Count - 6} more" : string.Empty);
            _lblDetail.Text = $"{batch.Count} picked: {item.Badge}. One answer goes to each of them.";
        }
        else
        {
            _lblKind.Text = InboxRail.Spaced(item.Badge);
            _lblWhat.Text = $"{item.From}: {item.What}";
            _lblDetail.Text = item.Note.Length > 0 ? item.Note : $"Sent {AppTime.TimeOfDay(item.SinceUtc.ToLocalTime())}.";
        }

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

        // Several of one kind: the answers say they go to all of them.
        if (batch is not null && item.Source == InboxSource.Request)
        {
            var all = batch.Count == 2 ? "both" : $"all {batch.Count}";
            _btnApprove.Text = $"Approve {all}";
            _btnDecline.Text = $"Decline {all}";
            _btnNoted.Text = $"Noted, {all}";
        }
        else if (_btnNoted.Text != "Noted")
        {
            _btnNoted.Text = "Noted";
        }

        _btnLater.Enabled = true;
    }

    /// <summary>
    /// Nothing picked: with nothing waiting, the last few answers, so "what
    /// did I just do" is a glance; with something waiting, where to start.
    /// </summary>
    private void ShowNothingPicked()
    {
        if (_grid.Rows.Count > 0)
        {
            _lblKind.Text = string.Empty;
            _lblWhat.Text = "Pick one to answer it.";
            _lblDetail.Text = string.Empty;
            return;
        }

        if (_items.Count > 0)
        {
            _lblKind.Text = string.Empty;
            _lblWhat.Text = $"Nothing under {DeskInbox.ChipWords(_filter)}.";
            _lblDetail.Text = $"{Plural.Of(_items.Count, "thing")} waiting under All (press 1).";
            return;
        }

        var recent = _requests?.Store.All.Where(r => !r.IsWaiting).OrderByDescending(r => r.AnsweredUtc ?? r.FiledUtc).Take(3).ToList()
            ?? new List<DriverRequest>();

        _lblKind.Text = recent.Count > 0 ? InboxRail.Spaced("Last answered") : string.Empty;
        _lblWhat.Text = string.Empty;
        _lblDetail.Text = _requests is { } desk && recent.Count > 0
            ? string.Join(Environment.NewLine, recent.Select(r =>
                $"{DriverRequestDesk.AnswerWords(r)} · {desk.NameFor(r)}, {DriverRequestDesk.KindWords(r.Kind).ToLowerInvariant()} · {AppTime.TimeOfDay((r.AnsweredUtc ?? r.FiledUtc).ToLocalTime())}"))
            : "New requests appear here within about 15 seconds.";
    }

    /// <summary>
    /// The main answer first and the full width, in the theme's main colour;
    /// the others under it. Approve where it means something, else Noted.
    /// </summary>
    private void ShowAnswersInOrder()
    {
        var main = _btnApprove.Enabled || !_btnNoted.Enabled || !_btnNoted.Visible ? _btnApprove : _btnNoted;

        if (ReferenceEquals(main, _mainAnswer))
        {
            return;
        }

        _mainAnswer = main;
        _actions.Order(main == _btnApprove
            ? new[] { _btnApprove, _btnDecline, _btnLater, _btnNoted, _btnDiscard, _btnRemove }
            : new[] { _btnNoted, _btnDecline, _btnLater, _btnApprove, _btnDiscard, _btnRemove });

        AppTheme.StyleButton(main, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(main == _btnApprove ? _btnNoted : _btnApprove, AppTheme.ButtonKind.Secondary);

        foreach (var b in new[] { _btnApprove, _btnNoted })
        {
            b.MinimumSize = Size.Empty;
            b.AutoSize = false;
        }
    }

    private Button? _mainAnswer;

    private readonly PhonePreview _phone = new();

    /// <summary>Docked: the picked item opened out over the list (Details), as the mockup's sheet.</summary>
    private bool _sheetOpen;
    private readonly LinkLabel _lnkSheet = new() { Text = "Details", AutoSize = true };

    /// <summary>
    /// Details and Back to the list, with painting held until the inbox is
    /// laid out the new way. Unheld, for a frame the middle had the new
    /// shape with the old parts in it - the list's place with the sheet's
    /// answers still showing - and both the picked item and the whole page
    /// overflowed: Windows drew its own white bars down each, there before
    /// the themed bars' 150ms sync covered them (21 Nov 2026, Asher's
    /// recording). The parts are shown or hidden first, then the middle
    /// rearranged, then - after FitToWidth's queued layout - the themed bars
    /// brought up to date and the whole thing drawn once.
    /// </summary>
    private void ToggleSheet()
    {
        var held = IsHandleCreated && Visible;

        if (held)
        {
            SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        }

        try
        {
            _sheetOpen = !_sheetOpen && _body is { Wide: false };
            FitToWidth(_body.Wide);
            _body.SheetOpen = _sheetOpen;

            // Settled here and now - a click is not inside a layout, so
            // nothing drops these, as FitToWidth has to allow for.
            _detail.PerformLayout();
            _page.PerformLayout();
        }
        finally
        {
            if (held)
            {
                // Queued behind FitToWidth's own layout, so it runs after it.
                BeginInvoke(() =>
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    _page.PerformLayout();
                    ThinScrollBar.SyncNow();
                    SendMessage(Handle, WmSetRedraw, 1, IntPtr.Zero);

                    // Frames too: a scrollbar is drawn in its window's frame.
                    RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwFrame | RdwAllChildren | RdwUpdateNow);
                });
            }
        }
    }

    private const int WmSetRedraw = 0x000B;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr update, IntPtr region, uint flags);

    /// <summary>
    /// What the driver's phone will say to the main answer, and what a
    /// decline would say instead - for a request, where the phone shows it.
    /// </summary>
    private void ShowPhone()
    {
        var item = Selected();
        var request = item is { Source: InboxSource.Request } && SelectedItems().Count <= 1 || Batch() is not null;
        var answer = ReferenceEquals(_mainAnswer, _btnNoted) ? DriverAsk.Noted : DriverAsk.Approved;
        var mainOn = ReferenceEquals(_mainAnswer, _btnNoted) ? _btnNoted.Enabled : _btnApprove.Enabled;
        var show = item is { Source: InboxSource.Request } && request && (mainOn || _btnDecline.Enabled);

        if (show)
        {
            var yes = DeskInbox.PhoneHeadline(item!.Kind, answer, item.Badge);
            var no = DeskInbox.PhoneHeadline(item.Kind, DriverAsk.Declined, item.Badge);
            _phone.Show(mainOn ? yes : no, mainOn && _btnDecline.Enabled ? $"If you decline: {no}, with your reply under it." : string.Empty);
        }

        var visible = show && (_body is null || _body.Wide || _sheetOpen);

        if (_phone.Visible != visible || !Visible)
        {
            _phone.Visible = visible;
        }
    }

    /// <summary>The quick replies as chips under the answers, for a request that can be declined.</summary>
    private void ShowReplies()
    {
        var item = Selected();
        var replies = item is { Source: InboxSource.Request } && _btnDecline.Enabled
            ? DeclineReplies(SetRepliesOnly(Batch() ?? new List<InboxItem> { item })).ToList()
            : new List<string>();

        _replyChips.Replies = replies;
        var show = replies.Count > 0 && (_body is null || _body.Wide || _sheetOpen);
        _replyChips.Visible = show;
        _lblReplies.Visible = show;
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

        if (item.Source == InboxSource.Signup && SignupSetRepliesOnly(item))
        {
            TurnDownSignupMenu(item);
            return;
        }

        if (item.Source == InboxSource.Signup)
        {
            TurnDownSignup(item);
            RefreshAll(force: true);
            return;
        }

        var menu = new ContextMenuStrip();

        // 15 Nov 2026, plan 1.4: a junior (or a badge nobody here has) in
        // what is picked gets HeatCaller's own fixed replies only - not the
        // club's edited ones, and no "Other reason...". See DeskInbox.JuniorReplyHint.
        var setOnly = SetRepliesOnly(Batch() ?? new List<InboxItem> { item });

        // Each reply on its number key while the menu is open, shown at the
        // item's right as a menu shows its shortcut; O for Other reason.
        var numbered = new List<ToolStripItem>();

        foreach (var reply in DeclineReplies(setOnly))
        {
            var entry = new ToolStripMenuItem(reply, null, (_, _) => Decline(reply));

            if (numbered.Count < 9)
            {
                entry.ShortcutKeyDisplayString = (numbered.Count + 1).ToString(CultureInfo.CurrentCulture);
                numbered.Add(entry);
            }

            menu.Items.Add(entry);
        }

        menu.Items.Add(new ToolStripSeparator());
        var other = menu.Items.Add("Other reason...", null, (_, _) => Decline(null));
        ((ToolStripMenuItem)other).ShortcutKeyDisplayString = "O";

        menu.KeyDown += (_, e) =>
        {
            var at = e.KeyCode is >= Keys.D1 and <= Keys.D9 ? e.KeyCode - Keys.D1 : e.KeyCode is >= Keys.NumPad1 and <= Keys.NumPad9 ? e.KeyCode - Keys.NumPad1 : -1;

            if (at >= 0 && at < numbered.Count)
            {
                e.Handled = true;
                menu.Close();
                numbered[at].PerformClick();
            }
            else if (e.KeyCode == Keys.O && other.Enabled)
            {
                e.Handled = true;
                menu.Close();
                other.PerformClick();
            }
        };

        if (setOnly)
        {
            other.Enabled = false;
            other.ToolTipText = DeskInbox.JuniorReplyHint;
            menu.Items.Add(new ToolStripMenuItem(DeskInbox.JuniorReplyHint) { Enabled = false });
        }

        menu.Items.Add("Change the quick replies...", null, (_, _) => EditQuickReplies());
        // StyleContextMenu, not ApplyRecursively: that gave the items the
        // theme's light text on the stock white menu, all but unreadable.
        AppTheme.StyleContextMenu(menu);
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
        var typing = ActiveControl is TextBoxBase;

        // Ctrl+Z takes back what the toast offers to, while it offers.
        if (!typing && keyData == (Keys.Control | Keys.Z) && _toastBar.Visible && _toastUndo is not null)
        {
            UndoLast();
            return true;
        }

        // Esc in the docked Details goes back to the list.
        if (keyData == Keys.Escape && _sheetOpen)
        {
            ToggleSheet();
            return true;
        }

        // Esc in the Done list goes back to what is waiting, rather than shutting the window.
        if (keyData == Keys.Escape && _showDone)
        {
            _showDone = false;
            ShowDone();
            return true;
        }

        // Letters are answers only while nothing is being typed - and not
        // in the Done list, which hides the picked item they would answer.
        if (!typing && (keyData & (Keys.Control | Keys.Alt)) == 0)
        {
            var code = keyData & Keys.KeyCode;
            var answering = !_showDone;

            switch (code)
            {
                case Keys.A or Keys.N or Keys.D or Keys.S or Keys.Delete when !answering:
                    return true;
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
                case Keys.Delete when _btnDiscard.Visible && _btnDiscard.Enabled:
                    Discard();
                    return true;
                case Keys.Delete when _btnRemove.Visible && _btnRemove.Enabled:
                    _btnRemove.PerformClick();
                    return true;
                case Keys.V when _vote is not null:
                    ShowVotePopover();
                    return true;
                case Keys.H:
                    _showDone = !_showDone;
                    ShowDone();
                    return true;
            }

            // 1 to 7: the filters, in the rail's order.
            var digit = code is >= Keys.D1 and <= Keys.D7 ? code - Keys.D1 : code is >= Keys.NumPad1 and <= Keys.NumPad7 ? code - Keys.NumPad1 : -1;

            if (digit >= 0 && (keyData & Keys.Shift) == 0)
            {
                SetFilter(_chipButtons.Keys.ElementAt(digit));
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
        _lblUndoKey.Visible = undo is not null;
        _toastBar.Visible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
        _detail.PerformLayout();
        _page.PerformLayout();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        _toastBar.Visible = false;
        _toastUndo = null;
        _detail.PerformLayout();
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
                 + "Leave it empty to go back to HeatCaller's own. Juniors always get HeatCaller's own.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _box.Text = string.Join(Environment.NewLine, replies);
        _box.Height = UiLayout.AtTextSize(160);

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // THE LOOK (18 Nov 2026, dialog pass): the replies in one card, as
        // the inbox's own cards, what they are for as its line over the box
        // (wrapped by the card now, not at a fixed width), the card taking
        // the height, and Save in the app's colour. Wider and taller by the
        // card's edges and caption, so the box is the size it was.
        var card = new Section("Decline replies");
        card.Add(blurb, gapBelow: UiLayout.RowGap);
        card.AddFill(_box, () => UiLayout.AtTextSize(120), gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        Controls.Add(page);

        CancelButton = cancel;
        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(ok, AppTheme.ButtonKind.Primary);
        DialogFit.Keep(this, page, width: 500 + Section.Inset.Horizontal, height: 320 + 56);
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
