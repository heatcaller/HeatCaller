namespace HeatCaller;

/// <summary>
/// The Desk Inbox's rules and its Done list.
///
/// RULES: the club settings that decide what reaches the desk at all, kept
/// beside the list they decide about - whether marshal swaps from phones
/// need the desk's OK, and whether new drivers can sign up online. What the
/// desk never has to see (packs, cars, photos and setups from phones, which
/// apply by themselves) is said there too, so nobody waits for them.
///
/// DONE: what has been answered, faded under a fold so the race director
/// can check what they did - requests answered (kept 30 days, as Driver
/// Requests kept them) and tonight's club notes approved or said, which can
/// still be removed from here.
/// </summary>
internal sealed partial class DeskInboxForm
{
    private readonly Section _rules = new("Rules");
    private readonly CheckBox _chkSwapsNeedDesk = new ThemedCheckBox();
    private readonly CheckBox _chkSignupsOn = new ThemedCheckBox();
    private readonly Button _btnQuickReplies = new() { Text = "Quick Replies..." };
    private readonly Label _lblHandled = new();

    private readonly Button _doneToggle = new() { AutoSize = true };
    private readonly DataGridView _gridDone = new();
    private readonly Button _btnRemoveDone = new() { Text = "Remove Note" };
    private readonly ButtonBar _doneButtons = new();

    private bool _showDone;

    /// <summary>The waiting list has rows in view - see ShowWaitingList.</summary>
    private bool _anyWaitingShown;
    private bool _loadingRules;

    private void BuildRules()
    {
        // 12 Nov 2026, setup plan step 8: a club setting, beside the requests it decides about.
        _chkSwapsNeedDesk.Text = "Marshal swaps from phones need the desk's OK";
        _chkSwapsNeedDesk.AutoSize = true;
        _chkSwapsNeedDesk.Checked = _requests?.SwapsNeedDesk ?? false;
        _chkSwapsNeedDesk.Visible = _requests is not null;
        _chkSwapsNeedDesk.CheckedChanged += (_, _) =>
        {
            if (_requests is not null && !_loadingRules)
            {
                _requests.SwapsNeedDesk = _chkSwapsNeedDesk.Checked;
            }
        };

        _chkSignupsOn.Text = "Let new drivers sign up online with an invite";
        _chkSignupsOn.AutoSize = true;
        _chkSignupsOn.Visible = _signups is not null;
        _chkSignupsOn.Checked = _signups?.Store.Enabled ?? false;
        _chkSignupsOn.Enabled = _signups?.Held is null;
        _chkSignupsOn.CheckedChanged += (_, _) =>
        {
            if (_signups is not null && !_loadingRules)
            {
                _signups.SetEnabled(_chkSignupsOn.Checked);
            }
        };

        _lblHandled.AutoSize = true;
        _lblHandled.MaximumSize = new Size(UiLayout.AtTextSize(900), 0);
        _lblHandled.ForeColor = AppTheme.TextMuted;
        _lblHandled.Font = AppTheme.SmallFont;
        _lblHandled.Text = HandledWords(0);

        _btnQuickReplies.Click += (_, _) => EditQuickReplies();

        _rules.Add(_chkSwapsNeedDesk, stretch: false, gapBelow: UiLayout.HintGap);
        _rules.Add(_chkSignupsOn, stretch: false, gapBelow: UiLayout.HintGap);
        _rules.Add(_lblHandled, gapBelow: UiLayout.HintGap);
        _rules.Add(new ButtonBar().With(_btnQuickReplies), gapBelow: 0);
    }

    private void BuildDone()
    {
        SetUpGrid(_gridDone);
        _gridDone.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "when", HeaderText = "Sent", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "Kind", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "from", HeaderText = "From", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "what", HeaderText = "What", FillWeight = 170 },
            new DataGridViewTextBoxColumn { Name = "answer", HeaderText = "Answer", FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "ref", HeaderText = "Ref", FillWeight = 35 });
        _gridDone.DefaultCellStyle.ForeColor = AppTheme.TextMuted;
        _gridDone.SelectionChanged += (_, _) => _btnRemoveDone.Enabled = SelectedDoneNote() is not null;

        _doneToggle.Click += (_, _) =>
        {
            _showDone = !_showDone;
            ShowDone();
        };

        _btnRemoveDone.Enabled = false;
        _btnRemoveDone.Click += (_, _) => RemoveNote(SelectedDoneNote());

        // The Done list is a view of the list's own (the rail's Done, or H):
        // "Hide done" under it goes back to what is waiting, as Esc does.
        _doneButtons.With(_btnRemoveDone, _doneToggle);

        ShowDone();
    }

    private void ShowDone()
    {
        // The holder, not the grid: a hidden grid left its empty holder
        // keeping half the spare height (FillPage.AddFill).
        // Before the page is built there is no holder yet; the constructor
        // calls this again once there is.
        // In place of the waiting list, as a view of its own - see InboxBody.
        if (_body is not null)
        {
            _body.ShowingDone = _showDone;
        }

        _rail.PickedId = _showDone ? "done" : _filter == InboxFilter.Vote ? InboxFilter.All : _filter;

        ShowWaitingList();
        FillDone();
        _doneView.PerformLayout();
        _page.PerformLayout();
    }

    /// <summary>How many are in the Done list: requests answered and tonight's notes approved or said.</summary>
    private int DoneCount() =>
        (_requests?.Store.All.Count(r => !r.IsWaiting) ?? 0) + (_notes?.All.Count(n => n.Approved) ?? 0);

    /// <summary>The Done list: answered requests newest first, then tonight's notes approved or said.</summary>
    private void FillDone()
    {
        var answered = _requests?.Store.All.Where(r => !r.IsWaiting).OrderByDescending(r => r.AnsweredUtc ?? r.FiledUtc).ToList()
            ?? new List<DriverRequest>();
        var notesDone = _notes?.All.Where(n => n.Approved).OrderByDescending(n => n.SaidAtUtc ?? n.AddedAtUtc).ToList()
            ?? new List<ClubNote>();

        var count = answered.Count + notesDone.Count;
        var toggle = $"{(_showDone ? "Hide" : "Show")} done ({count})";

        if (_doneToggle.Text != toggle)
        {
            _doneToggle.Text = toggle;
        }

        if (!_showDone)
        {
            return;
        }

        var selected = _gridDone.SelectedRows.Count > 0 ? _gridDone.SelectedRows[0].Tag : null;
        _gridDone.Rows.Clear();

        foreach (var note in notesDone)
        {
            var at = _gridDone.Rows.Add(WhenText(note.AddedAtUtc), "Club note", NoteFrom(note), note.Text,
                note.SaidAtUtc is null ? "Ready to be said" : "Said", string.Empty);
            _gridDone.Rows[at].Tag = note;
        }

        if (_requests is { } desk)
        {
            foreach (var request in answered)
            {
                var at = _gridDone.Rows.Add(WhenText(request.AskedUtc), DriverRequestDesk.KindWords(request.Kind), desk.NameFor(request),
                    desk.Summary(request), DriverRequestDesk.AnswerWords(request), request.Ref);
                _gridDone.Rows[at].Tag = request;

                // How it was answered, at a glance: yes in green, no in red.
                var answer = _gridDone.Rows[at].Cells["answer"].Style;
                answer.ForeColor = answer.SelectionForeColor = request.State switch
                {
                    DriverAsk.Approved => AppTheme.Success,
                    DriverAsk.Declined => AppTheme.Danger,
                    _ => AppTheme.TextMuted,
                };
            }
        }

        foreach (DataGridViewRow row in _gridDone.Rows)
        {
            if (ReferenceEquals(row.Tag, selected) || (row.Tag is ClubNote a && selected is ClubNote b && a.Id == b.Id))
            {
                row.Selected = true;
            }
        }

        _btnRemoveDone.Enabled = SelectedDoneNote() is not null;
    }

    /// <summary>A note picked in the Done list - one approved or said, which may still be taken off tonight's list.</summary>
    private ClubNote? SelectedDoneNote() =>
        _showDone && _gridDone.SelectedRows.Count > 0 && _gridDone.SelectedRows[0].Tag is ClubNote note ? note : null;

    /// <summary>What the rules say about the changes from phones that never wait here, with tonight's count.</summary>
    internal static string HandledWords(int tonight) =>
        "Battery packs, cars, photos and setups sent from phones are applied by themselves - they never wait here."
        + (tonight > 0 ? $" {Plural.Of(tonight, "change")} handled for you tonight." : string.Empty);

    /// <summary>How many changes from phones were applied by themselves today (the club's day, on this PC's clock).</summary>
    internal static int HandledToday(IEnumerable<DriverRequest> all, DateTime nowLocal) =>
        all.Count(r => DriverAsk.IsWrite(r.Kind) && r.Kind != DriverAsk.Cover && !r.IsWaiting && (r.AnsweredUtc ?? r.FiledUtc).ToLocalTime().Date == nowLocal.Date);

    /// <summary>The rules' ticks again from their desks - another window may have changed one.</summary>
    private void RefreshRules()
    {
        var handled = HandledWords(_requests is null ? 0 : HandledToday(_requests.Store.All, DateTime.Now));

        if (_lblHandled.Text != handled)
        {
            _lblHandled.Text = handled;
        }

        _loadingRules = true;

        try
        {
            if (_requests is not null && _chkSwapsNeedDesk.Checked != _requests.SwapsNeedDesk)
            {
                _chkSwapsNeedDesk.Checked = _requests.SwapsNeedDesk;
            }

            if (_signups is not null && _chkSignupsOn.Checked != _signups.Store.Enabled)
            {
                _chkSignupsOn.Checked = _signups.Store.Enabled;
            }
        }
        finally
        {
            _loadingRules = false;
        }
    }
}
