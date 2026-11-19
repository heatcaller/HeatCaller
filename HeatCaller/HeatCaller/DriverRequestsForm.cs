using System.Globalization;

namespace HeatCaller;

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
            $"Sent from their phone {request.AskedUtc.ToLocalTime().ToString("ddd d MMM ", CultureInfo.CurrentCulture)}{AppTime.TimeOfDay(request.AskedUtc.ToLocalTime())}, "
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

        // The changes in two cards (18 Nov 2026, dialog pass): the text ones,
        // each with its box, and the yes-or-no ones. Each change goes in its
        // card in the order the desk lists them, so Tab goes down them as it
        // did.
        var texts = new FieldGrid();
        var flags = new FieldGrid();

        foreach (var field in desk.FieldsOf(request, profile))
        {
            var tick = new ThemedCheckBox { Text = field.Caption, AutoSize = true, Checked = true };
            tick.CheckedChanged += (_, _) => UpdateApprove();

            if (field.IsFlag)
            {
                tick.Text = $"{field.Caption}: {(field.ProposedFlag ? "yes" : "no")} (now {(field.CurrentFlag ? "yes" : "no")})";
                flags.AddWide(tick, stretch: false);
                _rows.Add((field, tick, null));
                continue;
            }

            var box = new TextBox { Text = field.Proposed, MaxLength = field.MaxChars > 0 ? field.MaxChars : 120 };
            box.TextChanged += (_, _) => UpdateApprove();

            texts.AddWide(tick, stretch: false);
            texts.AddRow("Change to", box, stretch: true);
            texts.AddHint(field.Current.Length == 0 ? "Now: (empty). Leave the box empty to clear it." : $"Now: {field.Current}");

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

                texts.AddField(FieldGrid.Inline(buttons.ToArray()), stretch: true);
            }

            if (field.Key == "email")
            {
                texts.AddHint("Their old address is sent a note saying it was changed.");
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

        // Only the cards with something in them; the last with no gap under
        // it, as the last part of the body (DialogFit.Body).
        var cards = new List<Section>();

        if (_rows.Any(r => r.Box is not null))
        {
            cards.Add(Card(new Section("Their details"), "Each box can be changed before you approve.", texts));
        }

        if (_rows.Any(r => r.Box is null))
        {
            cards.Add(Card(new Section("Online and email"), "What they asked for, and what it is now.", flags));
        }

        for (var i = 0; i < cards.Count; i++)
        {
            body.Add(cards[i], gapBelow: i == cards.Count - 1 ? 0 : UiLayout.RowGap);
        }

        page.Add(_lblError, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnOk), gapBelow: 0);
        _btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = _btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass: Approve in the app's colour.
        AppTheme.StyleButton(_btnOk, AppTheme.ButtonKind.Primary);

        // Two lines kept for what is wrong or still to do, so a message
        // appearing takes nothing from the fields above it.
        _lblError.MinimumSize = new Size(0, 2 * Pieces.LineHeight(_lblError.Font) + _lblError.Padding.Vertical);

        UpdateApprove();

        DialogFit.Keep(this, page, width: 600, body: body);
    }

    /// <summary>A captioned card: its hint line, and the grid of changes under it.</summary>
    private static Section Card(Section card, string hint, FieldGrid grid)
    {
        card.Add(new Label
        {
            Text = hint,
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        }, gapBelow: UiLayout.RowGap);

        card.Add(grid, gapBelow: 0);
        return card;
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

        // THE LOOK (18 Nov 2026, dialog pass): the question and its box in
        // one card, as SignupReasonForm, and Decline in the app's colour.
        // The window is wider by the card's edges, so the question wraps
        // where it did.
        var card = new Section("Your words to them");
        card.Add(lbl, gapBelow: UiLayout.HintGap);
        card.Add(_txt, gapBelow: 0);

        var page = new FillPage();
        page.Add(card, gapBelow: UiLayout.SectionGap);

        // A pixel either side for the ring the theme draws round the box -
        // as SignupReasonForm.
        _txt.Margin = new Padding(1, 0, 1, 0);
        Pieces.KeepHeightTold(_txt);

        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass: Decline in the app's colour.
        AppTheme.StyleButton(btnOk, AppTheme.ButtonKind.Primary);

        DialogFit.Keep(this, page, width: 472 + Section.Inset.Horizontal);
    }
}
