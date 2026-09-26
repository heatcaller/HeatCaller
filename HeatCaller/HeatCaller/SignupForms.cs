namespace HeatCaller;

/// <summary>
/// Online Sign-ups, opened from Race Day → Entries: whether sign-up is
/// open, the sign-ups waiting for the desk, and the invites sent so far.
///
/// Stays open while the desk works through the queue and keeps itself up
/// to date - a sign-up made while it is open appears in it within about
/// ten seconds.
/// </summary>
internal sealed class SignupsForm : Form
{
    private const string Mask = "••••••••";

    private readonly RelaySignupDesk _desk;
    private readonly AppSettings _settings;
    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _classes;
    private readonly RentalTransponderStore _rentals;
    private readonly UndoService? _undo;

    private readonly CheckBox _chkEnabled = new();
    private readonly Label _lblState = new();
    private readonly DataGridView _gridWaiting = new();
    private readonly DataGridView _gridInvites = new();
    private readonly Button _btnApprove = new() { Text = "Approve..." };
    private readonly Button _btnReject = new() { Text = "Turn Down..." };
    private readonly Button _btnInvite = new() { Text = "Invite Drivers..." };
    private readonly Button _btnHere = new() { Text = "Code for Someone Here..." };
    private readonly Button _btnShowCode = new() { Text = "Show Code" };
    private readonly Button _btnResend = new() { Text = "Email Again" };
    private readonly Button _btnCancelInvite = new() { Text = "Cancel Invite" };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 3000 };

    private bool _loading;

    public SignupsForm(RelaySignupDesk desk, AppSettings settings, ProfileStore profiles, RaceClassStore classes, RentalTransponderStore rentals,
        UndoService? undo = null)
    {
        _desk = desk;
        _settings = settings;
        _profiles = profiles;
        _classes = classes;
        _rentals = rentals;
        _undo = undo;

        Text = "Online Sign-ups";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1080, 700);
        MinimumSize = new Size(820, 560);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        const int left = 12;
        var width = ClientSize.Width - left * 2;
        var y = 12;

        _chkEnabled.Text = "Let new drivers sign up online with an invite";
        _chkEnabled.AutoSize = true;
        _chkEnabled.Location = new Point(left, y);
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            if (!_loading)
            {
                _desk.SetEnabled(_chkEnabled.Checked);
            }
        };

        y = UiLayout.Below(_chkEnabled, UiLayout.HintGap);

        _lblState.AutoSize = true;
        _lblState.MaximumSize = new Size(width, 0);
        _lblState.Font = AppTheme.SmallFont;
        _lblState.Location = new Point(left, y);
        _lblState.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // Room for three lines of state whatever it says, so nothing below hops.
        y += AppTheme.SmallFont.Height * 3 + UiLayout.RowGap;

        var lblWaiting = new Label
        {
            Text = "Waiting for you",
            AutoSize = true,
            Font = AppTheme.BoldFont,
            Location = new Point(left, y),
        };

        y = UiLayout.Below(lblWaiting, UiLayout.HintGap);

        SetUpGrid(_gridWaiting);
        _gridWaiting.Location = new Point(left, y);
        _gridWaiting.Size = new Size(width, 190);
        _gridWaiting.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _gridWaiting.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Name", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "class", HeaderText = "Class", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "transponder", HeaderText = "Transponder", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "email", HeaderText = "Email", FillWeight = 100 },
            new DataGridViewTextBoxColumn { Name = "when", HeaderText = "Signed up", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "note", HeaderText = "Note", FillWeight = 70 });
        _gridWaiting.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                ApproveSelected();
            }
        };
        _gridWaiting.SelectionChanged += (_, _) => UpdateButtons();

        y = UiLayout.Below(_gridWaiting, UiLayout.RowGap);

        _btnApprove.Click += (_, _) => ApproveSelected();
        _btnReject.Click += (_, _) => RejectSelected();
        UiLayout.ButtonRow(left, y, _btnApprove, _btnReject);

        y += UiLayout.ButtonHeight + UiLayout.SectionGap;

        var lblInvites = new Label
        {
            Text = "Invites",
            AutoSize = true,
            Font = AppTheme.BoldFont,
            Location = new Point(left, y),
        };

        y = UiLayout.Below(lblInvites, UiLayout.HintGap);

        var buttonsTop = ClientSize.Height - 12 - UiLayout.ButtonHeight;

        SetUpGrid(_gridInvites);
        _gridInvites.Location = new Point(left, y);
        _gridInvites.Size = new Size(width, buttonsTop - UiLayout.RowGap - y);
        _gridInvites.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _gridInvites.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "for", HeaderText = "For", FillWeight = 130 },
            new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "sent", HeaderText = "Made", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "expires", HeaderText = "Works until", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "code", HeaderText = "Code", FillWeight = 50 });
        _gridInvites.SelectionChanged += (_, _) =>
        {
            HideCodes();
            UpdateButtons();
        };

        _btnInvite.Click += (_, _) => InviteByEmail();
        _btnHere.Click += (_, _) => CodeForSomeoneHere();
        _btnShowCode.Click += (_, _) => ShowSelectedCode();
        _btnResend.Click += (_, _) => ResendSelected();
        _btnCancelInvite.Click += (_, _) => CancelSelected();

        foreach (var button in new[] { _btnInvite, _btnHere, _btnShowCode, _btnResend, _btnCancelInvite })
        {
            button.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        }

        UiLayout.ButtonRow(left, buttonsTop, _btnInvite, _btnHere, _btnShowCode, _btnResend, _btnCancelInvite);

        var btnClose = new Button { Text = "Close", Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.OK };
        btnClose.Size = new Size(UiLayout.WidthFor(btnClose), UiLayout.ButtonHeight);
        btnClose.Location = new Point(ClientSize.Width - 12 - btnClose.Width, buttonsTop);

        // The invite row is five buttons; on a narrow window Close would sit
        // on top of the last of them, so the window starts wide enough.
        var rowRight = _btnCancelInvite.Right + UiLayout.ButtonGap + btnClose.Width + 12;

        if (rowRight > ClientSize.Width)
        {
            var grow = rowRight - ClientSize.Width;
            ClientSize = new Size(rowRight, ClientSize.Height);
            MinimumSize = new Size(Width, MinimumSize.Height);
            btnClose.Left += grow;
            _gridWaiting.Width += grow;
            _gridInvites.Width += grow;
            _lblState.MaximumSize = new Size(_lblState.MaximumSize.Width + grow, 0);
        }

        Controls.Add(_chkEnabled);
        Controls.Add(_lblState);
        Controls.Add(lblWaiting);
        Controls.Add(_gridWaiting);
        Controls.Add(_btnApprove);
        Controls.Add(_btnReject);
        Controls.Add(lblInvites);
        Controls.Add(_gridInvites);
        Controls.Add(_btnInvite);
        Controls.Add(_btnHere);
        Controls.Add(_btnShowCode);
        Controls.Add(_btnResend);
        Controls.Add(_btnCancelInvite);
        Controls.Add(btnClose);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        _desk.Changed += RefreshAll;
        _refresh.Tick += (_, _) => RefreshState();
        _refresh.Start();

        FormClosed += (_, _) =>
        {
            _desk.Changed -= RefreshAll;
            _refresh.Stop();
            _refresh.Dispose();

            // An invite email batch still going carries on: the desk owns
            // it, and records what went (see RelaySignupDesk.EmailInvitesAsync).
        };

        RefreshAll();
    }

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

    // ------------------------------------------------------------ drawing

    private void RefreshAll()
    {
        if (IsDisposed)
        {
            return;
        }

        _loading = true;
        _chkEnabled.Checked = _desk.Store.Enabled;
        _loading = false;

        var selectedWaiting = SelectedWaiting()?.Hash;
        _gridWaiting.Rows.Clear();

        foreach (var request in _desk.Waiting)
        {
            var note = request.Guardian ? "Parent or guardian signed up" : string.Empty;

            if (request.Car.Length > 0)
            {
                note = note.Length > 0 ? $"{note}; car: {request.Car}" : $"Car: {request.Car}";
            }

            // Left waiting rather than turned down automatically - see
            // RelaySignupDesk. The desk decides.
            if (_desk.IsFromUnknownInvite(request))
            {
                note = note.Length > 0
                    ? $"From an invite this laptop doesn't know; {note}"
                    : "From an invite this laptop doesn't know";
            }

            var index = _gridWaiting.Rows.Add(
                request.Name,
                request.ClassName,
                request.TransponderText,
                request.Email.Length == 0 ? "(none)" : request.Email,
                request.SubmittedUtc.ToLocalTime().ToString("ddd HH:mm", System.Globalization.CultureInfo.CurrentCulture),
                note);
            _gridWaiting.Rows[index].Tag = request;

            if (request.Hash == selectedWaiting)
            {
                _gridWaiting.Rows[index].Selected = true;
            }
        }

        var selectedInvite = SelectedInvite()?.Id;
        _gridInvites.Rows.Clear();

        foreach (var invite in _desk.Store.All)
        {
            var index = _gridInvites.Rows.Add(
                invite.ForText,
                invite.StateText,
                invite.CreatedUtc.ToLocalTime().ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture),
                invite.State == SignupInviteState.Open ? invite.ExpiresUtc.ToLocalTime().ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture) : string.Empty,
                invite.State == SignupInviteState.Open && invite.Code.Length > 0 ? Mask : string.Empty);
            _gridInvites.Rows[index].Tag = invite;

            if (invite.Id == selectedInvite)
            {
                _gridInvites.Rows[index].Selected = true;
            }
        }

        RefreshState();
        UpdateButtons();
    }

    private void RefreshState()
    {
        if (IsDisposed)
        {
            return;
        }

        var bad = false;
        string text;

        if (!_settings.RelayEnabled || SignupStore.PageUrl(_settings) is null)
        {
            text = "Publishing to the relay is off, so there is no sign-up page yet. Set it up on the Club Website page first.";
            bad = true;
        }
        else if (!_desk.Store.Enabled)
        {
            text = "Off. Nobody can sign up online; invites already made wait until it is switched on.";
        }
        else if (_desk.Problem is { } problem)
        {
            text = problem;
            bad = true;
        }
        else if (_desk.LockedUntilUtc is { } locked)
        {
            text = $"Sign-up codes are locked after too many wrong guesses until {locked.ToLocalTime():t}. "
                + "Cancel every open invite and make new ones to reopen it straight away.";
            bad = true;
        }
        else
        {
            var usable = _desk.Store.All.Count(i => i.IsUsable);
            text = $"Open - {Plural.Of(usable, "invite")} waiting to be used. New sign-ups appear here within about ten seconds.";
        }

        if (!_settings.RelayCheckInEnabled)
        {
            text += " Online check-in is off, so new drivers get no check-in card or PIN until it is on.";
        }

        if (!PinMailer.IsConfigured(_settings))
        {
            text += " Set up the Email group on the Club Website page to email invites and PINs.";
        }

        if (_desk.Store.CodesLocked)
        {
            // Only the sealed codes are kept - not their hashes (see
            // SignupInvite.Hash) - so this PC can't put them on the relay.
            text += " Invites made on another PC or Windows account can't be used from here: their codes can't be opened, "
                + "so they aren't on the relay from this PC, and sign-ups made with them show as from an invite this laptop doesn't know.";
        }

        _lblState.Text = text;
        _lblState.ForeColor = bad ? AppTheme.Danger : AppTheme.TextMuted;
    }

    private void UpdateButtons()
    {
        var request = SelectedWaiting();
        _btnApprove.Enabled = request is not null;
        _btnReject.Enabled = request is not null;

        var invite = SelectedInvite();
        var open = invite is { State: SignupInviteState.Open };
        _btnShowCode.Enabled = open && invite!.Code.Length > 0;
        _btnResend.Enabled = open && invite!.IsUsable && invite.Email.Length > 0 && invite.Code.Length > 0;
        _btnCancelInvite.Enabled = open;
    }

    private RelaySignupRequest? SelectedWaiting() =>
        _gridWaiting.SelectedRows.Count > 0 ? _gridWaiting.SelectedRows[0].Tag as RelaySignupRequest : null;

    private SignupInvite? SelectedInvite() =>
        _gridInvites.SelectedRows.Count > 0 ? _gridInvites.SelectedRows[0].Tag as SignupInvite : null;

    private void HideCodes()
    {
        foreach (DataGridViewRow row in _gridInvites.Rows)
        {
            if (row.Tag is SignupInvite { State: SignupInviteState.Open } invite && invite.Code.Length > 0)
            {
                row.Cells["code"].Value = Mask;
            }
        }
    }

    // ------------------------------------------------------------ waiting

    private void ApproveSelected()
    {
        if (SelectedWaiting() is not { } request)
        {
            return;
        }

        // Said before the desk types anything in: approving needs the invite.
        if (_desk.IsFromUnknownInvite(request))
        {
            AppDialog.Show(this,
                $"{request.Name} signed up with an invite this laptop doesn't know - one made on another laptop, or lost from this one. "
                + "Approve it on the laptop that sent the invite, or turn it down here.",
                "Invite not known here", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new ApproveSignupForm(request, _profiles, _classes, _rentals);

        if (form.ShowDialog(this) != DialogResult.OK || form.Result is not { } profile)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();
        var current = _desk.EventForEntry();
        string question;

        if (current is null)
        {
            question = $"Check {name} in for today?";
        }
        else
        {
            question = $"Add {name} to {current.DisplayName} and check them in?";

            if (_desk.ClassNeedsAdding(profile.ClassName))
            {
                question += $"{Environment.NewLine}{Environment.NewLine}{profile.ClassName.Trim()} isn't one of this event's classes yet - it will be added.";
            }
        }

        var enter = AppDialog.Show(this, question, "Add to today's event", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        if (!_desk.Approve(request, profile, form.RentalUnit, enter, out var refusal))
        {
            AppDialog.Show(this, refusal, "Not added", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Captured once the approval has gone through rather than before:
        // it can still be refused (a transponder somebody has), and what it
        // did - rental, class, check-in - is only known afterwards. The
        // desk, not this window, does the undo: the window may be closed.
        // Their phone has been told "you're in" by now, which no undo can
        // take back - the description says so, and so does the log.
        if (_undo is not null && _desk.LastApproval is { } receipt)
        {
            var desk = _desk;
            _undo.Capture($"Approve {receipt.Name}'s sign-up (their phone was already told they're in)", () => desk.UndoApprove(receipt));
        }
    }

    private void RejectSelected()
    {
        if (SelectedWaiting() is not { } request)
        {
            return;
        }

        using var prompt = new SignupReasonForm(request.Name);

        if (prompt.ShowDialog(this) == DialogResult.OK)
        {
            // Only an invite this laptop knows has anything here to put back.
            // For one it doesn't, the "no" to the phone is all there is, and
            // that can't be recalled - the prompt says so.
            if (_undo is not null && _desk.InviteFor(request) is { State: SignupInviteState.Open } invite)
            {
                var desk = _desk;
                var inviteId = invite.Id;
                var name = request.Name;
                _undo.Capture($"Turn down {name}'s sign-up (their phone is told no - they'd sign up again)", () => desk.UndoReject(inviteId, name));
            }

            _desk.Reject(request, prompt.Reason);
        }
    }

    // ------------------------------------------------------------ invites

    /// <summary>
    /// Whether to go on making invites while sign-up has a problem - the
    /// relay refusing the sign-up list, most of all, when every invite made
    /// is a link to a page that won't know its code. True when there is no
    /// problem, or the desk says Yes knowing that.
    /// </summary>
    private bool GoAheadDespiteProblem()
    {
        if (_desk.Problem is not { } problem)
        {
            return true;
        }

        var nl = Environment.NewLine;

        return AppDialog.Show(this,
            $"Online sign-up isn't working right now:{nl}{nl}{problem}{nl}{nl}"
            + "An invite made now probably won't work until that's fixed - the driver would get a code the sign-up page doesn't know. "
            + "Make it anyway?",
            "Sign-up isn't working", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void InviteByEmail()
    {
        if (!GoAheadDespiteProblem())
        {
            return;
        }

        if (!PinMailer.IsConfigured(_settings))
        {
            AppDialog.Show(this,
                PinMailer.ConfigurationProblem(_settings) + " Or use Code for Someone Here to give somebody a code in person.",
                "Email isn't set up", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new InviteDriversForm();

        if (form.ShowDialog(this) != DialogResult.OK || form.People.Count == 0)
        {
            return;
        }

        if (!_desk.Store.Enabled
            && AppDialog.Show(this, "Online sign-up is switched off, so these invites won't work until it's on. Switch it on now?",
                "Sign-up is off", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _desk.SetEnabled(true);
        }

        var invites = _desk.CreateInvites(form.People);
        _ = SendAsync(invites);
    }

    private async Task SendAsync(IReadOnlyList<SignupInvite> invites)
    {
        _btnInvite.Enabled = false;
        _btnResend.Enabled = false;
        var progress = new Progress<string>(text =>
        {
            if (!IsDisposed)
            {
                _lblState.Text = text;
            }
        });

        try
        {
            // The desk sends and records them, and finishes the batch even
            // if this window is closed part-way.
            var result = await _desk.EmailInvitesAsync(invites, progress);

            if (IsDisposed)
            {
                return;
            }

            var message = result.Message;

            if (result.Failures.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Failures);
            }

            if (!result.Cancelled)
            {
                AppDialog.Show(this, message, result.Ok ? "Invites sent" : "Invites not sent",
                    MessageBoxButtons.OK, result.Ok && result.Failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                AppDialog.Show(this, $"Couldn't email the invites: {ex.Message}", "Invites not sent", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnInvite.Enabled = true;
                RefreshAll();
            }
        }
    }

    private void CodeForSomeoneHere()
    {
        if (SignupStore.PageUrl(_settings) is null)
        {
            AppDialog.Show(this, "Publishing to the relay isn't set up and switched on yet, so there is no sign-up page to send anyone to.",
                "No sign-up page yet", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!GoAheadDespiteProblem())
        {
            return;
        }

        using var prompt = new SignupReasonForm(null);

        if (prompt.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!_desk.Store.Enabled)
        {
            _desk.SetEnabled(true);
        }

        var invite = _desk.CreateInvites(new[] { (string.Empty, prompt.Reason.Length > 0 ? prompt.Reason : "At the desk") })[0];
        ShowCode(invite);
    }

    private void ShowSelectedCode()
    {
        if (SelectedInvite() is { State: SignupInviteState.Open } invite && invite.Code.Length > 0)
        {
            foreach (DataGridViewRow row in _gridInvites.Rows)
            {
                if (ReferenceEquals(row.Tag, invite))
                {
                    row.Cells["code"].Value = invite.Code;
                }
            }
        }
    }

    private void ShowCode(SignupInvite invite)
    {
        var page = SignupStore.PageUrl(_settings) ?? string.Empty;
        var nl = Environment.NewLine;

        AppDialog.Show(this,
            $"Their sign-up code is:{nl}{nl}    {invite.Code[..4]} {invite.Code[4..]}{nl}{nl}"
            + $"On their phone, open {page} and type the code. It works once, until {invite.ExpiresUtc.ToLocalTime():dddd d MMMM}. "
            + "Their sign-up appears here for you to approve.",
            "Sign-up code", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ResendSelected()
    {
        if (SelectedInvite() is { } invite && invite.IsUsable && invite.Email.Length > 0 && GoAheadDespiteProblem())
        {
            _ = SendAsync(new[] { invite });
        }
    }

    private void CancelSelected()
    {
        if (SelectedInvite() is not { State: SignupInviteState.Open } invite)
        {
            return;
        }

        // A sign-up already made with this invite is told "cancelled" on the
        // spot, and that answer can't be recalled - so the question says so.
        var pending = _desk.Waiting.Any(w => string.Equals(w.Hash, invite.Hash, StringComparison.OrdinalIgnoreCase));

        if (AppDialog.Show(this, $"Cancel the invite for {invite.ForText}? Its code stops working straight away."
                + (pending ? $"{Environment.NewLine}{Environment.NewLine}They have already signed up with it: their phone is told it was cancelled, and Undo can't take that back." : string.Empty),
                "Cancel invite", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
        {
            if (_undo is not null)
            {
                var desk = _desk;
                var inviteId = invite.Id;
                _undo.Capture($"Cancel the invite for {invite.ForText}", () => desk.UndoCancel(inviteId, pending));
            }

            _desk.Cancel(invite);
        }
    }
}

/// <summary>
/// The desk's check of one sign-up before it becomes a driver: everything
/// they typed, editable, and the transponder they will race on - their
/// own, or one of the club's rentals. The profile is only built here;
/// RelaySignupDesk.Approve is what adds it.
/// </summary>
internal sealed class ApproveSignupForm : Form
{
    private readonly RelaySignupRequest _request;
    private readonly ProfileStore _profiles;
    private readonly TextBox _txtName = new();
    private readonly ComboBox _cmbClass = new();
    private readonly TextBox _txtTransponder = new();
    private readonly ComboBox _cmbRental = new();
    private readonly TextBox _txtCar = new();
    private readonly TextBox _txtNumber = new();
    private readonly TextBox _txtEmail = new();
    private readonly Label _lblError = new();

    /// <summary>The driver as the desk approved them, once OK is clicked.</summary>
    public DriverProfile? Result { get; private set; }

    /// <summary>The club rental to lend them, or null for their own transponder.</summary>
    public string? RentalUnit { get; private set; }

    public ApproveSignupForm(RelaySignupRequest request, ProfileStore profiles, RaceClassStore classes, RentalTransponderStore rentals)
    {
        _request = request;
        _profiles = profiles;

        Text = $"Approve {request.Name}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        const int left = 16;
        const int fieldLeft = left + 150;
        const int fieldWidth = 300;
        var y = 16;

        var lblIntro = new Label
        {
            Text = IntroFor(request),
            AutoSize = true,
            MaximumSize = new Size(fieldLeft + fieldWidth - left, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(left, y),
        };
        Controls.Add(lblIntro);
        y = UiLayout.Below(lblIntro, UiLayout.RowGap);

        void Row(string caption, Control field)
        {
            var label = new Label { Text = caption, AutoSize = true };
            field.Location = new Point(fieldLeft, y);
            field.Width = fieldWidth;
            label.Location = new Point(left, UiLayout.CentreOn(label, y, field.Height));
            Controls.Add(label);
            Controls.Add(field);
            y = UiLayout.Below(field, UiLayout.RowGap);
        }

        _txtName.Text = request.Name;
        _txtName.MaxLength = 80;
        Row("Name", _txtName);

        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClass.Items.Add(string.Empty);

        foreach (var name in classes.All.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            _cmbClass.Items.Add(name);
        }

        if (request.ClassName.Length > 0 && !_cmbClass.Items.Contains(request.ClassName))
        {
            _cmbClass.Items.Add(request.ClassName);
        }

        _cmbClass.SelectedItem = request.ClassName.Length > 0 ? request.ClassName : string.Empty;
        Row("Class", _cmbClass);

        _txtTransponder.Text = request.HasOwnTransponder ? request.Transponder : string.Empty;
        _txtTransponder.MaxLength = 40;
        Row("Transponder", _txtTransponder);

        // The club's rentals on the shelf, for somebody who asked for one or
        // doesn't know theirs. Picking one fills the box above with it.
        var shelf = rentals.All.Where(r => !r.Retired && !r.IsOut).ToList();
        _cmbRental.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRental.Items.Add("(their own)");

        foreach (var unit in shelf)
        {
            _cmbRental.Items.Add(new RentalChoice(unit));
        }

        _cmbRental.SelectedIndex = 0;
        _cmbRental.Enabled = shelf.Count > 0;
        _cmbRental.SelectedIndexChanged += (_, _) =>
        {
            if (_cmbRental.SelectedItem is RentalChoice choice)
            {
                _txtTransponder.Text = choice.Unit.Transponder;
                _txtTransponder.ReadOnly = true;
            }
            else
            {
                _txtTransponder.ReadOnly = false;

                if (!request.HasOwnTransponder)
                {
                    _txtTransponder.Text = string.Empty;
                }
            }
        };
        Row("Lend a rental", _cmbRental);

        _txtCar.Text = request.Car;
        _txtCar.MaxLength = 80;
        Row("Car", _txtCar);

        _txtNumber.MaxLength = 20;
        Row("Race number", _txtNumber);

        _txtEmail.Text = request.Email;
        _txtEmail.MaxLength = 254;
        Row("Email", _txtEmail);

        _lblError.AutoSize = true;
        _lblError.MaximumSize = new Size(fieldLeft + fieldWidth - left, 0);
        _lblError.ForeColor = AppTheme.Danger;
        _lblError.Location = new Point(left, y);
        Controls.Add(_lblError);
        y += AppTheme.BodyFont.Height * 3;

        var btnOk = new Button { Text = "Approve" };
        var btnCancel = new Button { Text = "Not Yet", DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) => TryApprove();

        btnOk.Size = new Size(UiLayout.WidthFor(btnOk), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);
        btnCancel.Location = new Point(fieldLeft + fieldWidth - btnCancel.Width, y);
        btnOk.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnOk.Width, y);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        ClientSize = new Size(fieldLeft + fieldWidth + left, y + UiLayout.ButtonHeight + 16);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // Straight to the one thing a rental or "don't know" sign-up still needs.
        Shown += (_, _) =>
        {
            if (!request.HasOwnTransponder)
            {
                (shelf.Count > 0 ? (Control)_cmbRental : _txtTransponder).Focus();
            }
        };
    }

    private static string IntroFor(RelaySignupRequest request)
    {
        var lines = new List<string>
        {
            $"Signed up online {request.SubmittedUtc.ToLocalTime():ddd d MMM HH:mm}. Check the details, then Approve.",
        };

        if (request.WantsRental)
        {
            lines.Add("They'd like to rent a transponder - pick one of the club's below, or type the number of the one you hand them.");
        }
        else if (!request.HasOwnTransponder)
        {
            lines.Add("They don't know their transponder yet. Type it in when they're at the desk, or click Not Yet and approve them then - the sign-up keeps waiting.");
        }

        if (request.Guardian)
        {
            lines.Add("A parent or guardian signed this driver up.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void TryApprove()
    {
        var name = _txtName.Text.Trim();
        var transponder = _txtTransponder.Text.Trim();

        if (name.Length == 0)
        {
            _lblError.Text = "Type their name.";
            return;
        }

        if (transponder.Length == 0)
        {
            _lblError.Text = "Type their transponder, or lend them one of the club's rentals. "
                + "If they haven't got one yet, click Not Yet - the sign-up keeps waiting until they're at the desk.";
            return;
        }

        var car = _txtCar.Text.Trim();
        var rental = _cmbRental.SelectedItem as RentalChoice;

        var profile = new DriverProfile
        {
            Transponder = transponder,
            DriverName = name,
            CarModel = car,
            Cars = car.Length > 0 ? new List<string> { car } : new List<string>(),
            ClassName = (_cmbClass.SelectedItem as string ?? string.Empty).Trim(),
            RaceNumber = _txtNumber.Text.Trim(),
            Email = _txtEmail.Text.Trim(),
            IsRental = rental is not null,
            Notes = _request.Guardian ? "Signed up online by a parent or guardian." : "Signed up online.",
            FirstSeenUtc = DateTime.UtcNow,
        };

        if (_profiles.DescribeClash(profile, editing: null) is { } clash)
        {
            _lblError.Text = clash;
            return;
        }

        Result = profile;
        RentalUnit = rental?.Unit.Transponder;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed class RentalChoice
    {
        public RentalChoice(RentalTransponder unit) => Unit = unit;

        public RentalTransponder Unit { get; }

        public override string ToString() => Unit.DisplayName;
    }
}

/// <summary>Who to invite: one email address per line, each with an optional note after a comma.</summary>
internal sealed class InviteDriversForm : Form
{
    private readonly TextBox _txtPeople = new();
    private readonly Label _lblError = new();

    /// <summary>The addresses and notes to invite, once OK is clicked.</summary>
    public List<(string Email, string Note)> People { get; } = new();

    public InviteDriversForm()
    {
        Text = "Invite Drivers";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        const int left = 16;
        const int width = 520;
        var y = 16;

        var lblIntro = new Label
        {
            Text = "One email address per line. Each person is emailed their own sign-up code and a link to the club's "
                + "sign-up page. Add a note after a comma if it helps you remember who's who - \"sam@example.com, Sam's dad\".",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(left, y),
        };
        y = UiLayout.Below(lblIntro, UiLayout.RowGap);

        _txtPeople.Multiline = true;
        _txtPeople.AcceptsReturn = true;
        _txtPeople.ScrollBars = ScrollBars.Vertical;
        _txtPeople.Location = new Point(left, y);
        _txtPeople.Size = new Size(width, 200);
        y = UiLayout.Below(_txtPeople, UiLayout.RowGap);

        _lblError.AutoSize = true;
        _lblError.MaximumSize = new Size(width, 0);
        _lblError.ForeColor = AppTheme.Danger;
        _lblError.Location = new Point(left, y);
        y += AppTheme.BodyFont.Height * 2 + UiLayout.RowGap;

        var btnOk = new Button { Text = "Send Invites" };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) => TryAccept();
        btnOk.Size = new Size(UiLayout.WidthFor(btnOk), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);
        btnCancel.Location = new Point(left + width - btnCancel.Width, y);
        btnOk.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnOk.Width, y);

        Controls.Add(lblIntro);
        Controls.Add(_txtPeople);
        Controls.Add(_lblError);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        ClientSize = new Size(left * 2 + width, y + UiLayout.ButtonHeight + 16);
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
    }

    private void TryAccept()
    {
        People.Clear();
        var bad = new List<string>();

        foreach (var raw in _txtPeople.Lines)
        {
            var line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            var comma = line.IndexOf(',');
            var email = (comma >= 0 ? line[..comma] : line).Trim();
            var note = comma >= 0 ? line[(comma + 1)..].Trim() : string.Empty;

            if (!System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email || !email.Contains('.'))
            {
                bad.Add(email);
                continue;
            }

            if (!People.Any(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                People.Add((email, note.Length > 60 ? note[..60] : note));
            }
        }

        if (bad.Count > 0)
        {
            _lblError.Text = $"{(bad.Count == 1 ? "This doesn't" : "These don't")} look like an email address: {string.Join(", ", bad.Take(5))}";
            People.Clear();
            return;
        }

        if (People.Count == 0)
        {
            _lblError.Text = "Type at least one email address.";
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// One short line of text: why a sign-up was turned down (shown on their
/// phone), or who a code at the desk is for.
/// </summary>
internal sealed class SignupReasonForm : Form
{
    private readonly TextBox _txt = new();

    public string Reason => _txt.Text.Trim();

    /// <param name="rejecting">The driver being turned down, or null when making a code for somebody at the desk.</param>
    public SignupReasonForm(string? rejecting)
    {
        Text = rejecting is null ? "Code for Someone Here" : $"Turn Down {rejecting}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        const int left = 16;
        const int width = 440;
        var y = 16;

        var lbl = new Label
        {
            Text = rejecting is null
                ? "Who is it for? (optional - just for your list of invites)"
                : "Tell them why, or what to do instead (optional - shown on their phone). Their phone is told straight away, "
                  + "and that can't be taken back - Undo only opens their invite again here, so they can sign up again:",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Location = new Point(left, y),
        };
        y = UiLayout.Below(lbl, UiLayout.HintGap);

        _txt.MaxLength = rejecting is null ? 60 : 300;
        _txt.Location = new Point(left, y);
        _txt.Width = width;
        y = UiLayout.Below(_txt, UiLayout.SectionGap);

        var btnOk = new Button { Text = rejecting is null ? "Make Code" : "Turn Down", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        btnOk.Size = new Size(UiLayout.WidthFor(btnOk), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);
        btnCancel.Location = new Point(left + width - btnCancel.Width, y);
        btnOk.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnOk.Width, y);

        Controls.Add(lbl);
        Controls.Add(_txt);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        ClientSize = new Size(left * 2 + width, y + UiLayout.ButtonHeight + 16);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
    }
}
