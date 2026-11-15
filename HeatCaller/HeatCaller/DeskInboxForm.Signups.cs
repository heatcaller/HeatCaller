namespace HeatCaller;

/// <summary>
/// Online sign-ups in the Desk Inbox (15 Nov 2026, Asher: "online sign ups
/// inbox as well") - what Online Sign-ups did, moved here: the sign-ups
/// waiting are items in the list; the invites, which are not requests, are
/// a section of their own under the Sign-ups chip with every button they
/// had. RelaySignupDesk does the work and owns every undo.
/// </summary>
internal sealed partial class DeskInboxForm
{
    private const string Mask = "••••••••";

    private readonly Section _invites = new("Invites");
    private readonly DataGridView _gridInvites = new();
    private readonly Button _btnInvite = new() { Text = "Invite Drivers..." };
    private readonly Button _btnHere = new() { Text = "Code for Someone Here..." };
    private readonly Button _btnShowCode = new() { Text = "Show Code" };
    private readonly Button _btnResend = new() { Text = "Email Again" };
    private readonly Button _btnCancelInvite = new() { Text = "Cancel Invite" };

    private void BuildInvites()
    {
        SetUpGrid(_gridInvites);
        _gridInvites.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "for", HeaderText = "For", FillWeight = 130 },
            new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "sent", HeaderText = "Made", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "expires", HeaderText = "Works until", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "code", HeaderText = "Code", FillWeight = 50 });
        _gridInvites.Height = _gridInvites.ColumnHeadersHeight + 4 * _gridInvites.RowTemplate.Height + 2;
        _gridInvites.SelectionChanged += (_, _) =>
        {
            HideCodes();
            UpdateInviteButtons();
        };

        _btnInvite.Click += (_, _) => InviteByEmail();
        _btnHere.Click += (_, _) => CodeForSomeoneHere();
        _btnShowCode.Click += (_, _) => ShowSelectedCode();
        _btnResend.Click += (_, _) => ResendSelected();
        _btnCancelInvite.Click += (_, _) => CancelSelected();

        // Nothing can be saved to signups.json this session (the state line
        // says why), so nothing that would make invites is offered.
        if (_signups?.Held is not null)
        {
            _btnInvite.Enabled = false;
            _btnHere.Enabled = false;
        }

        _invites.Add(_gridInvites, gapBelow: UiLayout.HintGap);
        _invites.Add(new ButtonBar().With(_btnInvite, _btnHere, _btnShowCode, _btnResend, _btnCancelInvite), gapBelow: 0);
        _invites.Visible = false;
    }

    private void OnSignupsChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(OnSignupsChanged);
            return;
        }

        FillInvites();
        RefreshAll(force: true);
    }

    /// <summary>Sign-ups waiting for the desk.</summary>
    private IEnumerable<InboxItem> SignupItems()
    {
        if (_signups is not { } desk)
        {
            yield break;
        }

        foreach (var request in desk.Waiting)
        {
            var note = request.Guardian ? "Parent or guardian signed up" : string.Empty;

            if (request.Car.Length > 0)
            {
                note = note.Length > 0 ? $"{note}; car: {request.Car}" : $"Car: {request.Car}";
            }

            // Left waiting rather than turned down automatically - the desk decides.
            if (desk.IsFromUnknownInvite(request))
            {
                note = note.Length > 0 ? $"From an invite this laptop doesn't know; {note}" : "From an invite this laptop doesn't know";
            }

            var what = $"{request.ClassName}; transponder: {request.TransponderText}; {(request.Email.Length == 0 ? "no email" : request.Email)}";

            yield return new InboxItem(
                InboxSource.Signup,
                request.Hash,
                "signup",
                "Sign-up",
                request.Name,
                what,
                note,
                request.SubmittedUtc,
                RaceBlocking: false,
                request);
        }
    }

    private void UpdateSignupButtons(InboxItem item, List<InboxItem>? batch)
    {
        _btnApprove.Text = "Approve";
        _btnDecline.Text = "Turn Down";
        _btnApprove.Enabled = batch is null;
        _btnDecline.Enabled = batch is null;
        _btnNoted.Visible = false;
        _btnDiscard.Visible = false;
        _btnRemove.Visible = false;
    }

    /// <summary>What the sign-up desk's state is, as Online Sign-ups said it.</summary>
    private (string Text, bool Bad) SignupStateLine()
    {
        if (_signups is not { } desk)
        {
            return (string.Empty, false);
        }

        var bad = false;
        string text;

        if (desk.Held is { } held)
        {
            text = held;
            bad = true;
        }
        else if (!_settings.RelayEnabled || SignupStore.PageUrl(_settings) is null)
        {
            text = "Publishing to the relay is off, so there is no sign-up page yet. Set it up on the Club Website page first.";
            bad = true;
        }
        else if (!desk.Store.Enabled)
        {
            text = "Online sign-up is off. Nobody can sign up online; invites already made wait until it is switched on.";
        }
        else if (desk.Problem is { } problem)
        {
            text = problem;
            bad = true;
        }
        else if (desk.LockedUntilUtc is { } locked)
        {
            text = $"Sign-up codes are locked after too many wrong guesses until {AppTime.TimeOfDay(locked.ToLocalTime())}. "
                + "Cancel every open invite and make new ones to reopen it straight away.";
            bad = true;
        }
        else
        {
            var usable = desk.Store.All.Count(i => i.IsUsable);
            text = $"Online sign-up is open - {Plural.Of(usable, "invite")} waiting to be used. New sign-ups appear here within about ten seconds.";
        }

        if (!_settings.RelayCheckInEnabled)
        {
            text += " Online check-in is off, so new drivers get no check-in card or PIN until it is on.";
        }

        if (!PinMailer.IsConfigured(_settings))
        {
            text += " Set up the Email group on the Club Website page to email invites and PINs.";
        }

        if (desk.Store.CodesLocked)
        {
            text += " Invites made on another PC or Windows account can't be used from here: their codes can't be opened, "
                + "so they aren't on the relay from this PC, and sign-ups made with them show as from an invite this laptop doesn't know.";
        }

        return (text, bad);
    }

    // ------------------------------------------------------------ waiting

    private void ApproveSignup(InboxItem item)
    {
        if (_signups is not { } desk || item.Payload is not RelaySignupRequest request || _classes is null || _rentals is null)
        {
            return;
        }

        // Said before the desk types anything in: approving needs the invite.
        if (desk.IsFromUnknownInvite(request))
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
        var current = desk.EventForEntry();
        string question;

        if (current is null)
        {
            question = $"Check {name} in for today?";
        }
        else
        {
            question = $"Add {name} to {current.DisplayName} and check them in?";

            if (desk.ClassNeedsAdding(profile.ClassName))
            {
                question += $"{Environment.NewLine}{Environment.NewLine}{profile.ClassName.Trim()} isn't one of this event's classes yet - it will be added.";
            }
        }

        var enter = AppDialog.Show(this, question, "Add to today's event", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        if (!desk.Approve(request, profile, form.RentalUnit, enter, out var refusal))
        {
            AppDialog.Show(this, refusal, "Not added", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Captured once the approval has gone through: it can still be
        // refused, and what it did is only known afterwards. The desk does
        // the undo. Their phone has been told "you're in", which no undo can
        // take back - the description says so.
        if (_undo is not null && desk.LastApproval is { } receipt)
        {
            _undo.Capture($"Approve {receipt.Name}'s sign-up (their phone was already told they're in)", () => desk.UndoApprove(receipt));
            Toast($"{receipt.Name} is in. Their phone has been told.", UndoFor(NewestUndo()));
        }
    }

    private void TurnDownSignup(InboxItem item)
    {
        if (_signups is not { } desk || item.Payload is not RelaySignupRequest request)
        {
            return;
        }

        using var prompt = new SignupReasonForm(request.Name);

        if (prompt.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Only an invite this laptop knows has anything to put back, and the
        // undo is captured only when the reject went through (5 Nov 2026,
        // bug hunt 11 review).
        var invite = _undo is not null && desk.InviteFor(request) is { State: SignupInviteState.Open } open ? open : null;

        if (desk.Reject(request, prompt.Reason))
        {
            if (_undo is not null && invite is not null)
            {
                var inviteId = invite.Id;
                var name = request.Name;
                _undo.Capture($"Turn down {name}'s sign-up (their phone is told no - they'd sign up again)", () => desk.UndoReject(inviteId, name));
                Toast($"Turned down {name}'s sign-up.", UndoFor(NewestUndo()));
            }
            else
            {
                Toast($"Turned down {request.Name}'s sign-up.");
            }
        }
    }

    // ------------------------------------------------------------ invites

    private void FillInvites()
    {
        if (_signups is not { } desk || IsDisposed)
        {
            return;
        }

        var selected = SelectedInvite()?.Id;
        _gridInvites.Rows.Clear();

        foreach (var invite in desk.Store.All)
        {
            var index = _gridInvites.Rows.Add(
                invite.ForText,
                invite.StateText,
                invite.CreatedUtc.ToLocalTime().ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture),
                invite.State == SignupInviteState.Open ? invite.ExpiresUtc.ToLocalTime().ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture) : string.Empty,
                invite.State == SignupInviteState.Open && invite.Code.Length > 0 ? Mask : string.Empty);
            _gridInvites.Rows[index].Tag = invite;

            if (invite.Id == selected)
            {
                _gridInvites.Rows[index].Selected = true;
            }
        }

        UpdateInviteButtons();
    }

    private SignupInvite? SelectedInvite() =>
        _gridInvites.SelectedRows.Count > 0 ? _gridInvites.SelectedRows[0].Tag as SignupInvite : null;

    private void UpdateInviteButtons()
    {
        var invite = SelectedInvite();
        var open = invite is { State: SignupInviteState.Open };
        _btnShowCode.Enabled = open && invite!.Code.Length > 0;
        _btnResend.Enabled = open && invite!.IsUsable && invite.Email.Length > 0 && invite.Code.Length > 0;
        _btnCancelInvite.Enabled = open;
    }

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

    /// <summary>
    /// Whether to go on making invites while sign-up has a problem. True when
    /// there is none, or the desk says Yes knowing that.
    /// </summary>
    private bool GoAheadDespiteProblem()
    {
        if (_signups?.Problem is not { } problem)
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
        if (_signups is not { } desk || !GoAheadDespiteProblem())
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

        if (!desk.Store.Enabled
            && AppDialog.Show(this, "Online sign-up is switched off, so these invites won't work until it's on. Switch it on now?",
                "Sign-up is off", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            desk.SetEnabled(true);
        }

        IReadOnlyList<SignupInvite> invites;

        try
        {
            invites = desk.CreateInvites(form.People);
        }
        catch (InvalidOperationException ex)
        {
            AppDialog.Show(this, ex.Message, "Invites not made", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _ = SendInvitesAsync(invites);
    }

    private async Task SendInvitesAsync(IReadOnlyList<SignupInvite> invites)
    {
        if (_signups is not { } desk)
        {
            return;
        }

        _btnInvite.Enabled = false;
        _btnResend.Enabled = false;
        var progress = new Progress<string>(text =>
        {
            if (!IsDisposed)
            {
                Toast(text);
            }
        });

        try
        {
            // The desk sends and records them, and finishes the batch even
            // if this window is closed part-way.
            var result = await desk.EmailInvitesAsync(invites, progress);

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
                FillInvites();
            }
        }
    }

    private void CodeForSomeoneHere()
    {
        if (_signups is not { } desk)
        {
            return;
        }

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

        if (!desk.Store.Enabled)
        {
            desk.SetEnabled(true);
        }

        SignupInvite invite;

        try
        {
            invite = desk.CreateInvites(new[] { (string.Empty, prompt.Reason.Length > 0 ? prompt.Reason : "At the desk") })[0];
        }
        catch (InvalidOperationException ex)
        {
            AppDialog.Show(this, ex.Message, "Code not made", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

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
            _ = SendInvitesAsync(new[] { invite });
        }
    }

    private void CancelSelected()
    {
        if (_signups is not { } desk || SelectedInvite() is not { State: SignupInviteState.Open } invite)
        {
            return;
        }

        // A sign-up already made with this invite is told "cancelled" on the
        // spot, and that answer can't be recalled - so the question says so.
        var pending = desk.Waiting.Any(w => string.Equals(w.Hash, invite.Hash, StringComparison.OrdinalIgnoreCase));

        if (AppDialog.Show(this, $"Cancel the invite for {invite.ForText}? Its code stops working straight away."
                + (pending ? $"{Environment.NewLine}{Environment.NewLine}They have already signed up with it: their phone is told it was canceled, and Undo can't take that back." : string.Empty),
                "Cancel invite", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
        {
            var inviteId = invite.Id;
            var forText = invite.ForText;

            if (desk.Cancel(invite) && _undo is not null)
            {
                _undo.Capture($"Cancel the invite for {forText}", () => desk.UndoCancel(inviteId, pending));
                Toast($"Cancelled the invite for {forText}.", UndoFor(NewestUndo()));
            }
        }
    }
}
