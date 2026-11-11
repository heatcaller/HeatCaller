using System.Globalization;

namespace HeatCaller;

/// <summary>
/// Driver Requests, opened from Race Day → Who's Racing (22 Oct 2026, driver
/// experience): what drivers have asked for from their own phones - a change
/// to their details, the battery pack they're running, a query about a
/// result - waiting for the desk, and the ones answered.
///
/// Modelled on Online Sign-ups (SignupsForm): not a dialog, so the desk can
/// keep it open beside Race Control while it works through them; it keeps
/// itself up to date (a request appears within about fifteen seconds); the
/// waiting list and the answered list share the height, each with its
/// buttons under it. "Result queries only" narrows both lists to queries -
/// with each race's own notes in Race History, that is where the desk reads
/// what drivers queried tonight.
///
/// Every answer reaches the driver's phone through their live document; the
/// desk (DriverRequestDesk) does the work, and owns every undo.
/// </summary>
internal sealed class DriverRequestsForm : Form
{
    /// <summary>Each list shows its headings and at least this many rows before the window scrolls instead.</summary>
    private const int LeastRows = 3;

    private readonly DriverRequestDesk _desk;
    private readonly ProfileStore _profiles;
    private readonly Action<string>? _speakName;
    private readonly CheckInStore? _checkIns;

    private readonly Label _lblState = new();
    private readonly CheckBox _chkQueriesOnly = new ThemedCheckBox();
    private readonly DataGridView _gridWaiting = new();
    private readonly DataGridView _gridAnswered = new();
    private readonly Button _btnApprove = new() { Text = "Approve..." };
    private readonly Button _btnNoted = new() { Text = "Noted" };
    private readonly Button _btnDecline = new() { Text = "Decline..." };
    private readonly Button _btnDiscard = new() { Text = "Discard" };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 3000 };

    /// <summary>The store's version last drawn, so the three-second refresh redraws only on a change.</summary>
    private int _drawnVersion = -1;

    public DriverRequestsForm(DriverRequestDesk desk, ProfileStore profiles, Action<string>? speakName = null, CheckInStore? checkIns = null)
    {
        _desk = desk;
        _profiles = profiles;
        _speakName = speakName;
        _checkIns = checkIns;

        Text = "Driver Requests";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        _lblState.AutoSize = true;
        _lblState.Font = AppTheme.SmallFont;

        // Three lines kept for the state whatever it says, so the lists under
        // it don't hop as it is reworded - as Online Sign-ups does.
        _lblState.MinimumSize = new Size(0, 3 * Pieces.LineHeight(_lblState.Font) + _lblState.Padding.Vertical);

        _chkQueriesOnly.Text = "Result queries only";
        _chkQueriesOnly.AutoSize = true;
        _chkQueriesOnly.CheckedChanged += (_, _) => RefreshAll(force: true);

        var lblWaiting = new Label { Text = "Waiting for you", AutoSize = true, Font = AppTheme.BoldFont };
        var lblAnswered = new Label { Text = "Answered (kept 30 days)", AutoSize = true, Font = AppTheme.BoldFont };

        SetUpGrid(_gridWaiting);
        _gridWaiting.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "when", HeaderText = "Sent", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "Asked about", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "what", HeaderText = "What they asked for", FillWeight = 170 },
            new DataGridViewTextBoxColumn { Name = "note", HeaderText = "Note", FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "ref", HeaderText = "Ref", FillWeight = 35 });
        _gridWaiting.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                ApproveOrNoteSelected();
            }
        };
        _gridWaiting.SelectionChanged += (_, _) => UpdateButtons();

        SetUpGrid(_gridAnswered);
        _gridAnswered.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "when", HeaderText = "Sent", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "Asked about", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "what", HeaderText = "What they asked for", FillWeight = 170 },
            new DataGridViewTextBoxColumn { Name = "answer", HeaderText = "Answer", FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "ref", HeaderText = "Ref", FillWeight = 35 });

        _btnApprove.Click += (_, _) => ApproveOrNoteSelected();
        _btnNoted.Click += (_, _) => ApproveOrNoteSelected();
        _btnDecline.Click += (_, _) => DeclineSelected();
        _btnDiscard.Click += (_, _) => DiscardSelected();

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        // Not a dialog (Main Window shows it beside itself), so Close closes
        // it itself - as Online Sign-ups does.
        btnClose.Click += (_, _) => Close();

        var page = new FillPage();
        page.Add(_lblState, gapBelow: UiLayout.RowGap);
        page.Add(_chkQueriesOnly, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(lblWaiting, stretch: false, gapBelow: UiLayout.HintGap);
        page.AddFill(_gridWaiting, _ => LeastListHeight(_gridWaiting), gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar().With(_btnApprove, _btnNoted, _btnDecline, _btnDiscard));
        page.Add(lblAnswered, stretch: false, gapBelow: UiLayout.HintGap);
        page.AddFill(_gridAnswered, _ => LeastListHeight(_gridAnswered), gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        Pieces.KeepHeadingsWhole(_gridWaiting);
        Pieces.KeepHeadingsWhole(_gridAnswered);

        _refresh.Tick += (_, _) => RefreshAll(force: false);
        _refresh.Start();

        // Stopped when it closes - or when it is thrown away without ever
        // having been shown. Stopping twice does no harm.
        void StopWatching()
        {
            _refresh.Stop();
            _refresh.Dispose();
        }

        FormClosed += (_, _) => StopWatching();
        Disposed += (_, _) => StopWatching();

        RefreshAll(force: true);

        DialogFit.Keep(this, page, width: UiLayout.AtTextSize(1040), height: 680, widthMeasured: true);

        // Not a dialog, so not dressed as one - as Online Sign-ups.
        MinimizeBox = true;
        ShowInTaskbar = true;
    }

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
        utc.ToLocalTime().ToString("ddd HH:mm", CultureInfo.CurrentCulture);

    // ------------------------------------------------------------ drawing

    /// <summary>Redraws both lists - on every call when forced, otherwise only when the store has changed.</summary>
    private void RefreshAll(bool force)
    {
        if (IsDisposed)
        {
            return;
        }

        RefreshState();

        var version = _desk.Store.Version;

        if (!force && version == _drawnVersion)
        {
            return;
        }

        _drawnVersion = version;

        var queriesOnly = _chkQueriesOnly.Checked;
        var all = _desk.Store.All.Where(r => !queriesOnly || r.Kind == DriverAsk.Query).ToList();

        var selectedWaiting = SelectedWaiting()?.Id;
        _gridWaiting.Rows.Clear();

        foreach (var request in all.Where(r => r.IsWaiting))
        {
            var index = _gridWaiting.Rows.Add(
                WhenText(request.AskedUtc),
                _desk.NameFor(request),
                DriverRequestDesk.KindWords(request.Kind),
                _desk.Summary(request),
                _desk.NoteFor(request),
                request.Ref);
            _gridWaiting.Rows[index].Tag = request;

            if (request.Id == selectedWaiting)
            {
                _gridWaiting.Rows[index].Selected = true;
            }
        }

        _gridAnswered.Rows.Clear();

        // Newest first: the one just answered is at the top.
        foreach (var request in all.Where(r => !r.IsWaiting).OrderByDescending(r => r.AnsweredUtc ?? r.FiledUtc))
        {
            var index = _gridAnswered.Rows.Add(
                WhenText(request.AskedUtc),
                _desk.NameFor(request),
                DriverRequestDesk.KindWords(request.Kind),
                _desk.Summary(request),
                DriverRequestDesk.AnswerWords(request),
                request.Ref);
            _gridAnswered.Rows[index].Tag = request;
        }

        UpdateButtons();
    }

    private void RefreshState()
    {
        var (text, bad) = _desk.StateLine();

        if (!string.Equals(_lblState.Text, text, StringComparison.Ordinal))
        {
            _lblState.Text = text;
        }

        _lblState.ForeColor = bad ? AppTheme.Danger : AppTheme.TextMuted;
    }

    private void UpdateButtons()
    {
        var request = SelectedWaiting();
        var known = request is not null && _desk.DriverOf(request) is not null;

        _btnApprove.Enabled = known && request!.Kind is DriverAsk.Details or DriverAsk.Pack or DriverAsk.Late or DriverAsk.Skip;
        _btnNoted.Enabled = request is { Kind: DriverAsk.Query };
        _btnDecline.Enabled = request is not null;
        _btnDiscard.Enabled = request is not null && !known;
    }

    private DriverRequest? SelectedWaiting() =>
        _gridWaiting.SelectedRows.Count > 0 ? _gridWaiting.SelectedRows[0].Tag as DriverRequest : null;

    // ------------------------------------------------------------ answers

    private void ApproveOrNoteSelected()
    {
        if (SelectedWaiting() is not { } request)
        {
            return;
        }

        // Read again: the list is up to three seconds old, and a newer
        // request may have replaced this one since.
        if (_desk.Store.Get(request.Id) is not { IsWaiting: true } current)
        {
            RefreshAll(force: true);
            return;
        }

        string refusal;

        switch (current.Kind)
        {
            case DriverAsk.Details:
                if (_desk.DriverOf(current) is not { } profile)
                {
                    return;
                }

                using (var form = new ApproveDriverRequestForm(_desk, current, profile, _profiles, _speakName, _checkIns))
                {
                    if (form.ShowDialog(this) != DialogResult.OK || form.Result is not { } choice)
                    {
                        return;
                    }

                    if (!_desk.ApplyDetails(current, choice, out refusal))
                    {
                        AppDialog.Show(this, refusal, "Not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }

                break;

            case DriverAsk.Pack:
                if (_desk.DriverOf(current) is null)
                {
                    return;
                }

                if (AppDialog.Show(this, $"{_desk.Summary(current)} - for {_desk.NameFor(current)}. Approve it?",
                        "Battery pack", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                if (!_desk.ApprovePack(current, out refusal))
                {
                    AppDialog.Show(this, refusal, "Not changed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                break;

            case DriverAsk.Query:
                if (!_desk.Note(current, out refusal))
                {
                    AppDialog.Show(this, refusal, "Not noted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                break;

            // More time, or skip my race (11 Nov 2026): Race Control acts on it.
            case DriverAsk.Late:
            case DriverAsk.Skip:
                if (_desk.DriverOf(current) is null)
                {
                    return;
                }

                if (AppDialog.Show(this, $"{_desk.Summary(current)} - for {_desk.NameFor(current)}. Approve it?",
                        DriverRequestDesk.KindWords(current.Kind), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                if (!_desk.ApproveRaceAsk(current, out refusal))
                {
                    AppDialog.Show(this, refusal, "Not changed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                break;
        }

        RefreshAll(force: true);
    }

    private void DeclineSelected()
    {
        if (SelectedWaiting() is not { } request)
        {
            return;
        }

        using var prompt = new DriverRequestReasonForm(_desk.NameFor(request));

        if (prompt.ShowDialog(this) == DialogResult.OK && !_desk.Decline(request, prompt.Reason))
        {
            AppDialog.Show(this, "That request has already been answered, or replaced by a newer one.", "Not declined",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        RefreshAll(force: true);
    }

    private void DiscardSelected()
    {
        if (SelectedWaiting() is not { } request || _desk.DriverOf(request) is not null)
        {
            return;
        }

        if (AppDialog.Show(this,
                "Nobody on this laptop has the badge this request came from - a driver deleted since, or a badge from before "
                + "the check-in key was changed. Throw the request away? Nothing is sent to anyone's phone.",
                "Discard request", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
        {
            _desk.Discard(request);
        }

        RefreshAll(force: true);
    }
}

/// <summary>
/// The desk's check of a driver's own details request before anything
/// changes (22 Oct 2026, driver experience), modelled on ApproveSignupForm:
/// each change they asked for has its own tick and - for text - a box the
/// desk can edit. Nothing is changed here; DriverRequestDesk.ApplyDetails
/// does that with what this hands back.
///
/// A CHANGED ANNOUNCER NAME IS HEARD BEFORE IT IS APPROVED. The announcer
/// will say it to a room tonight, so Approve stays off until Hear It has
/// spoken the name as it stands in the box - and again after it is edited.
/// Only where there is a voice to hear it in: without one there is no Hear
/// It button, and nothing to wait for.
/// </summary>
internal sealed class ApproveDriverRequestForm : Form
{
    private readonly DriverProfile _profile;
    private readonly ProfileStore _profiles;
    private readonly Action<string>? _speakName;
    private readonly CheckInStore? _checkIns;

    private readonly List<(DriverRequestDesk.DetailField Field, CheckBox Tick, TextBox? Box)> _rows = new();
    private readonly Button _btnOk = new() { Text = "Approve" };
    private readonly Label _lblError = new();

    private CheckBox? _announcerTick;
    private TextBox? _announcerBox;

    /// <summary>The announcer name as it last was when Hear It spoke it; null until it has.</summary>
    private string? _heard;

    /// <summary>What the desk ticked, once Approve is clicked.</summary>
    public DriverRequestDesk.DetailsChoice? Result { get; private set; }

    public ApproveDriverRequestForm(
        DriverRequestDesk desk,
        DriverRequest request,
        DriverProfile profile,
        ProfileStore profiles,
        Action<string>? speakName,
        CheckInStore? checkIns)
    {
        _profile = profile;
        _profiles = profiles;
        _speakName = speakName;
        _checkIns = checkIns;

        var name = string.IsNullOrWhiteSpace(profile.DriverName) ? "(no name)" : profile.DriverName.Trim();

        Text = $"Approve {name}'s Changes";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var intro = new List<string>
        {
            $"Sent from their phone {request.AskedUtc.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.CurrentCulture)}, "
            + (request.Proof == DriverAsk.ByPin ? "signed in with their personal PIN." : "with their badge code."),
            "Untick anything you don't want to change. Their phone shows your answer.",
        };

        if (request.Guardian)
        {
            intro.Add("A parent or guardian sent this for them.");
        }

        var lblIntro = new Label
        {
            Text = string.Join(Environment.NewLine, intro),
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var fields = new FieldGrid();

        foreach (var field in desk.FieldsOf(request, profile))
        {
            var tick = new ThemedCheckBox { Text = field.Caption, AutoSize = true, Checked = true };
            tick.CheckedChanged += (_, _) => UpdateApprove();

            if (field.IsFlag)
            {
                tick.Text = $"{field.Caption}: {(field.ProposedFlag ? "yes" : "no")} (now {(field.CurrentFlag ? "yes" : "no")})";
                fields.AddWide(tick, stretch: false);
                _rows.Add((field, tick, null));
                continue;
            }

            var box = new TextBox { Text = field.Proposed, MaxLength = field.MaxChars > 0 ? field.MaxChars : 120 };
            box.TextChanged += (_, _) => UpdateApprove();

            fields.AddWide(tick, stretch: false);
            fields.AddRow("Change to", box, stretch: true);
            fields.AddHint(field.Current.Length == 0 ? "Now: (empty). Leave the box empty to clear it." : $"Now: {field.Current}");

            if (field.Key == "announcer")
            {
                _announcerTick = tick;
                _announcerBox = box;

                var buttons = new List<Control>();

                if (_speakName is not null)
                {
                    var btnHear = new Button { Text = "Hear It" };
                    btnHear.Click += (_, _) => HearIt();
                    buttons.Add(btnHear);
                }

                var btnCheck = new Button { Text = "Check with Tonight's Names..." };
                btnCheck.Click += (_, _) => CheckWithTonightsNames();
                buttons.Add(btnCheck);

                fields.AddField(FieldGrid.Inline(buttons.ToArray()), stretch: true);
            }

            if (field.Key == "email")
            {
                fields.AddHint("Their old address is sent a note saying it was changed.");
            }

            _rows.Add((field, tick, box));
        }

        _lblError.AutoSize = true;
        _lblError.ForeColor = AppTheme.Danger;

        var btnCancel = new Button { Text = "Not Yet", DialogResult = DialogResult.Cancel };
        _btnOk.Click += (_, _) => TryApprove();

        var page = new FillPage();
        var body = DialogFit.Body(page, gapBelow: UiLayout.RowGap);
        body.Add(lblIntro, gapBelow: UiLayout.RowGap);
        body.Add(fields, gapBelow: 0);
        page.Add(_lblError, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnOk), gapBelow: 0);
        _btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = _btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // Two lines kept for what is wrong or still to do, so a message
        // appearing takes nothing from the fields above it.
        _lblError.MinimumSize = new Size(0, 2 * Pieces.LineHeight(_lblError.Font) + _lblError.Padding.Vertical);

        UpdateApprove();

        DialogFit.Keep(this, page, width: 600, body: body);
    }

    /// <summary>Whether the announcer name is ticked, changed from what they have, and not yet heard as it stands.</summary>
    private bool NameStillToHear() =>
        _announcerBox is not null
        && NameStillToHear(_speakName is not null, _announcerTick is { Checked: true }, _announcerBox.Text, _profile.AnnouncerName, _heard);

    /// <summary>
    /// The Hear It rule, on its own so it can be tested: only where there is a
    /// voice to hear it in, only for a name that is ticked and actually
    /// changed, and until the name exactly as it stands has been heard.
    /// </summary>
    internal static bool NameStillToHear(bool canSpeak, bool ticked, string? boxText, string? currentName, string? heard)
    {
        if (!canSpeak || !ticked)
        {
            return false;
        }

        var text = (boxText ?? string.Empty).Trim();

        return text.Length > 0
               && !string.Equals(text, (currentName ?? string.Empty).Trim(), StringComparison.Ordinal)
               && !string.Equals(text, heard, StringComparison.Ordinal);
    }

    private void UpdateApprove()
    {
        var hear = NameStillToHear();
        var any = _rows.Any(r => r.Tick.Checked);

        _btnOk.Enabled = any && !hear;
        _lblError.ForeColor = hear ? AppTheme.TextMuted : AppTheme.Danger;
        _lblError.Text = hear
            ? "Press Hear It to check how the announcer will say the new name before approving it."
            : any ? string.Empty : "Nothing is ticked. Tick a change to approve, or close this and decline the request.";
    }

    private void HearIt()
    {
        if (_speakName is null || _announcerBox is null)
        {
            return;
        }

        var text = _announcerBox.Text.Trim();

        if (text.Length == 0)
        {
            return;
        }

        try
        {
            _speakName(text);
            _heard = text;
        }
        catch (Exception ex)
        {
            _lblError.Text = $"Couldn't say it: {ex.Message}";
            return;
        }

        UpdateApprove();
    }

    /// <summary>Name Check, with tonight's drivers and this one under the name in the box - so the desk hears it among the rest.</summary>
    private void CheckWithTonightsNames()
    {
        var copy = ProfileStore.Snapshot(_profile);

        if (_announcerBox is not null)
        {
            copy.AnnouncerName = _announcerBox.Text.Trim();
        }

        var everyone = _profiles.All
            .Select(p => string.Equals(p.Transponder, _profile.Transponder, StringComparison.OrdinalIgnoreCase) ? copy : p)
            .ToList();

        NameCheckForm.ShowFor(this, everyone, _speakName, _checkIns);
    }

    private void TryApprove()
    {
        if (NameStillToHear())
        {
            UpdateApprove();
            return;
        }

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var (field, tick, box) in _rows)
        {
            if (!tick.Checked)
            {
                continue;
            }

            if (field.IsFlag)
            {
                flags[field.Key] = field.ProposedFlag;
            }
            else if (box is not null)
            {
                texts[field.Key] = box.Text.Trim();
            }
        }

        if (texts.Count == 0 && flags.Count == 0)
        {
            UpdateApprove();
            return;
        }

        if (texts.TryGetValue("email", out var email) && email.Length > 0
            && (!System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email))
        {
            _lblError.ForeColor = AppTheme.Danger;
            _lblError.Text = $"\"{email}\" doesn't look like an email address. Fix it, or untick it.";
            return;
        }

        Result = new DriverRequestDesk.DetailsChoice(texts, flags);
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// Why a driver's request is declined - one short line, shown on their phone.
/// Modelled on SignupReasonForm, whose wording is about sign-ups (and whose
/// undo note doesn't apply here), and capped at DriverAsk.SaidMaxChars.
/// (22 Oct 2026, driver experience.)
/// </summary>
internal sealed class DriverRequestReasonForm : Form
{
    private readonly TextBox _txt = new();

    public string Reason => _txt.Text.Trim();

    public DriverRequestReasonForm(string driverName)
    {
        Text = $"Decline {driverName}'s Request";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lbl = new Label
        {
            Text = "Tell them why, or what to do instead (optional - shown on their phone). Nothing about them is changed:",
            AutoSize = true,
        };

        _txt.MaxLength = DriverAsk.SaidMaxChars;

        var btnOk = new Button { Text = "Decline", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        page.Add(lbl, gapBelow: UiLayout.HintGap);
        page.Add(_txt, gapBelow: UiLayout.SectionGap);

        // A pixel either side for the ring the theme draws round the box -
        // as SignupReasonForm.
        _txt.Margin = new Padding(1, 0, 1, UiLayout.SectionGap);
        Pieces.KeepHeightTold(_txt);

        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        DialogFit.Keep(this, page, width: 472);
    }
}
