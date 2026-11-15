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

    private readonly Button _doneToggle = new() { AutoSize = true };
    private readonly DataGridView _gridDone = new();
    private readonly Button _btnRemoveDone = new() { Text = "Remove Note" };
    private readonly ButtonBar _doneButtons = new();

    private bool _showDone;
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

        var handled = new Label
        {
            Text = "Battery packs, cars, photos and setups sent from phones are applied by themselves - they never wait here.",
            AutoSize = true,
            MaximumSize = new Size(UiLayout.AtTextSize(900), 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _btnQuickReplies.Click += (_, _) => EditQuickReplies();

        _rules.Add(_chkSwapsNeedDesk, stretch: false, gapBelow: UiLayout.HintGap);
        _rules.Add(_chkSignupsOn, stretch: false, gapBelow: UiLayout.HintGap);
        _rules.Add(handled, gapBelow: UiLayout.HintGap);
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
        _doneButtons.With(_btnRemoveDone);

        ShowDone();
    }

    private void ShowDone()
    {
        _gridDone.Visible = _showDone;
        _doneButtons.Visible = _showDone;
        FillDone();
        _page.PerformLayout();
    }

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

    /// <summary>The rules' ticks again from their desks - another window may have changed one.</summary>
    private void RefreshRules()
    {
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
