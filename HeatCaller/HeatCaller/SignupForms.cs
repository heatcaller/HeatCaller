namespace HeatCaller;

/// <summary>
/// The desk's check of one sign-up before it becomes a driver: everything
/// they typed, editable, and the transponder they will race on - their
/// own, or one of the club's rentals. The profile is only built here;
/// RelaySignupDesk.Approve is what adds it.
///
/// On the layout pieces (DialogFit): what they signed up with and the
/// fields lined up in a card, in a body that scrolls on a screen too
/// short for them; under it, room for what is wrong, and Approve and Not
/// Yet at the foot. It used to be placed in pixels, the captions in a
/// column of 150px and every field 300px beside it, with three lines of
/// Font.Height kept for what is wrong - short of three lines as drawn.
/// </summary>
internal sealed class ApproveSignupForm : Form
{
    private readonly RelaySignupRequest _request;
    private readonly ProfileStore _profiles;
    private readonly RentalTransponderStore _rentals;
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
        _rentals = rentals;

        Text = $"Approve {request.Name}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lblIntro = new Label
        {
            Text = IntroFor(request),
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _txtName.Text = request.Name;
        _txtName.MaxLength = 80;

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

        _txtTransponder.Text = request.HasOwnTransponder ? request.Transponder : string.Empty;
        _txtTransponder.MaxLength = 40;

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

                // Back to what they asked with - their own number, or an empty
                // box. Leaving the rental's number in it (as this used to for
                // somebody with their own) lent them the rental on Approve and
                // left their real transponder unassigned.
                _txtTransponder.Text = request.HasOwnTransponder ? request.Transponder : string.Empty;
            }
        };

        _txtCar.Text = request.Car;
        _txtCar.MaxLength = 80;

        _txtNumber.MaxLength = 20;

        _txtEmail.Text = request.Email;
        _txtEmail.MaxLength = 254;

        // Every field runs to the right-hand edge, where the buttons end, as
        // the 300px fields did beside their 150px captions. In one card with
        // a hint line (18 Nov 2026, dialog pass): three cards made the window
        // too tall for a 1366x768 screen, and it is opened on race night.
        var fields = new FieldGrid();
        var captions = new[]
        {
            fields.AddRow("Name", _txtName, stretch: true),
            fields.AddRow("Class", _cmbClass, stretch: true),
            fields.AddRow("Transponder", _txtTransponder, stretch: true),
            fields.AddRow("Lend a rental", _cmbRental, stretch: true),
            fields.AddRow("Car", _txtCar, stretch: true),
            fields.AddRow("Race number", _txtNumber, stretch: true),
            fields.AddRow("Email", _txtEmail, stretch: true),
        };

        var card = new Section("Their details");
        card.Add(new Label
        {
            Text = "As they typed it. Put anything right here before you approve.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        }, gapBelow: UiLayout.RowGap);
        card.Add(fields, gapBelow: 0);

        _lblError.AutoSize = true;
        _lblError.ForeColor = AppTheme.Danger;

        var btnOk = new Button { Text = "Approve" };
        var btnCancel = new Button { Text = "Not Yet", DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) => TryApprove();

        // Seven fields and up to three lines of what they signed up with: at
        // the largest text size, more than a small screen has room for, so
        // they are in a body that scrolls there. What is wrong is under the
        // body, not in it - so it is in view, beside the buttons, however
        // far the body is scrolled.
        var page = new FillPage();
        var body = DialogFit.Body(page, gapBelow: UiLayout.RowGap);
        body.Add(lblIntro, gapBelow: UiLayout.RowGap);
        body.Add(card, gapBelow: 0);
        page.Add(_lblError, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Not Yet furthest right, as it was -
        // and Tab still reaches Approve first: the bar takes them
        // right to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass: Approve in the app's colour.
        AppTheme.StyleButton(btnOk, AppTheme.ButtonKind.Primary);

        // Three lines kept for what is wrong, as the label draws them
        // (Pieces.LineHeight), so a message appearing takes nothing from the
        // fields above it. The longest the dialog words itself (a
        // transponder missing) comes to three at the width it opens at; a
        // longer one - a clash naming a driver with a long name - grows it,
        // and the body gives way, scrolling if it must.
        _lblError.MinimumSize = new Size(0, 3 * Pieces.LineHeight(_lblError.Font) + _lblError.Padding.Vertical);

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the fields' column needs more: never narrower than the
        // 300px the fields had, and wide enough for each drop-down's longest
        // choice, which a long class name can make wider than that. The
        // caption column beside it as the grid works it out: its widest
        // caption and the gap after it.
        (Font Font, int Count, int Width)? classWidth = null;
        (Font Font, int Count, int Width)? rentalWidth = null;

        var fieldColumn = Math.Max(
            UiLayout.AtTextSize(300),
            Math.Max(Pieces.PickerWidth(_cmbClass, ref classWidth), Pieces.PickerWidth(_cmbRental, ref rentalWidth)));

        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        // With the cards' insets beside the grids, and room for the body's
        // scrollbar, which takes its width on a screen too short for it all.
        var width = Math.Max(
            UiLayout.AtTextSize(482),
            UiLayout.PageLeft + Section.Inset.Horizontal + captionColumn + fieldColumn
            + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight);

        // Straight to the one thing a rental or "don't know" sign-up still needs.
        Shown += (_, _) =>
        {
            if (!request.HasOwnTransponder)
            {
                (shelf.Count > 0 ? (Control)_cmbRental : _txtTransponder).Focus();
            }
        };

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    private static string IntroFor(RelaySignupRequest request)
    {
        var lines = new List<string>
        {
            $"Signed up online {request.SubmittedUtc.ToLocalTime():ddd d MMM} {AppTime.TimeOfDay(request.SubmittedUtc.ToLocalTime())}. Check the details, then Approve.",
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

        // A club rental whether it was picked below or its number typed in
        // the box, as the introduction invites. It is lent by name, as at
        // the counter, and the driver is given no profile on it: profiles
        // are keyed by transponder, and one on the club's unit used to name
        // every later borrower's laps after this driver, refuse the next
        // sign-up lent the same unit, and keep this driver out of records
        // for good. A unit registered as a Club Rental already has its own
        // "Rental R4" profile, which is not a clash here.
        var rental = (_cmbRental.SelectedItem as RentalChoice)?.Unit ?? _rentals.Get(transponder);

        if (rental is { Retired: true })
        {
            _lblError.Text = $"{rental.DisplayName} is one of the club's rentals and is retired. Pick another, or type their own transponder.";
            return;
        }

        if (rental is { IsOut: true })
        {
            _lblError.Text = $"{rental.DisplayName} is one of the club's rentals and is out"
                + (rental.CurrentHolderName.Length > 0 ? $" with {rental.CurrentHolderName}" : string.Empty)
                + ". Check it in on the Rentals page first, or pick another.";
            return;
        }

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

        if (rental is null && _profiles.DescribeClash(profile, editing: null) is { } clash)
        {
            _lblError.Text = clash;
            return;
        }

        Result = profile;
        RentalUnit = rental?.Transponder;
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

/// <summary>
/// Who to invite: one email address per line, each with an optional note after a comma.
///
/// On the layout pieces (DialogFit): the explanation, the box of addresses
/// taking the height - the window can be made taller for a long list - room
/// for what is wrong under it and the buttons at the foot. It used to be
/// placed in pixels in a window of a fixed size, the box a fixed 200px and
/// two lines of Font.Height kept for what is wrong - short of two lines as
/// drawn.
/// </summary>
internal sealed class InviteDriversForm : Form
{
    /// <summary>The box shows at least this many addresses before the window scrolls instead.</summary>
    private const int LeastLines = 4;

    /// <summary>How much taller the window is for the card round the box - its caption and edges - at the normal text size.</summary>
    private const int CardRoom = 56;

    private readonly TextBox _txtPeople = new();
    private readonly Label _lblError = new();

    /// <summary>The addresses and notes to invite, once OK is clicked.</summary>
    public List<(string Email, string Note)> People { get; } = new();

    public InviteDriversForm()
    {
        Text = "Invite Drivers";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lblIntro = new Label
        {
            Text = "One email address per line. Each person is emailed their own sign-up code and a link to the club's "
                + "sign-up page. Add a note after a comma if it helps you remember who's who - \"sam@example.com, Sam's dad\".",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _txtPeople.Multiline = true;
        _txtPeople.AcceptsReturn = true;
        _txtPeople.ScrollBars = ScrollBars.Vertical;

        _lblError.AutoSize = true;
        _lblError.ForeColor = AppTheme.Danger;

        var btnOk = new Button { Text = "Send Invites" };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) => TryAccept();

        // THE LOOK (18 Nov 2026, dialog pass): the explanation and the box
        // of addresses in one card, the explanation as its line on what it
        // is for, the card taking the height; what is wrong under the card,
        // beside the buttons, and Send Invites in the app's colour.
        var card = new Section("Who to invite");
        card.Add(lblIntro, gapBelow: UiLayout.RowGap);
        card.AddFill(_txtPeople, () => LeastLines * Pieces.LineHeight(_txtPeople.Font) + UiLayout.HintGap * 2, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(_lblError, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as it was - and
        // Tab still reaches Send Invites first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass: Send Invites in the app's colour.
        AppTheme.StyleButton(btnOk, AppTheme.ButtonKind.Primary);

        // Two lines kept for what is wrong, as the label draws them
        // (Pieces.LineHeight), so a message appearing takes nothing from the
        // box. A longer list of addresses that don't look right grows it,
        // and the box gives way.
        _lblError.MinimumSize = new Size(0, 2 * Pieces.LineHeight(_lblError.Font) + _lblError.Padding.Vertical);

        // The size it was at the normal text size - 552x390 inside, the box
        // about 200px of that height - and wider and taller by the card's
        // edges and caption, so the box is the size it was.
        DialogFit.Keep(this, page, width: 552 + Section.Inset.Horizontal, height: 390 + CardRoom);
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
///
/// On the layout pieces (DialogFit): the question wrapping across the top,
/// the box under it and the buttons at the foot, the window as tall as
/// they are. It used to be placed in pixels, the question wrapped at a
/// fixed 440px.
/// </summary>
internal sealed class SignupReasonForm : Form
{
    private readonly TextBox _txt = new();

    public string Reason => _txt.Text.Trim();

    /// <param name="rejecting">The driver being turned down, or null when making a code for somebody at the desk.</param>
    public SignupReasonForm(string? rejecting)
    {
        Text = rejecting is null ? "Code for Someone Here" : $"Turn Down {rejecting}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lbl = new Label
        {
            Text = rejecting is null
                ? "Who is it for? (optional - just for your list of invites)"
                : "Tell them why, or what to do instead (optional - shown on their phone). Their phone is told straight away, "
                  + "and that can't be taken back - Undo only opens their invite again here, under a new code, so they can sign up again:",
            AutoSize = true,
        };

        _txt.MaxLength = rejecting is null ? 60 : 300;

        var btnOk = new Button { Text = rejecting is null ? "Make Code" : "Turn Down", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // THE LOOK (18 Nov 2026, dialog pass): the question and its box in
        // one card, captioned with what the words are for, and the button
        // that does it in the app's colour. The window is wider by the
        // card's edges, so the question wraps where it did.
        var card = rejecting is null ? new Section("For your list") : new Section("Your words to them");
        card.Add(lbl, gapBelow: UiLayout.HintGap);
        card.Add(_txt, gapBelow: 0);

        var page = new FillPage();
        page.Add(card, gapBelow: UiLayout.SectionGap);

        // A pixel either side of the box for the ring the theme draws round
        // it, outside it, on what holds it (Pieces.RingRoom): stretched across
        // the card, the box's sides would be the card's inner edges, and the
        // ring's sides would be drawn past them, where nothing shows.
        _txt.Margin = new Padding(1, 0, 1, 0);

        // Its height is the box's own, and changes when the theme takes its
        // border off - see Pieces.KeepHeightTold.
        Pieces.KeepHeightTold(_txt);

        // Against the right-hand edge, Cancel furthest right, as it was - and
        // Tab still reaches Make Code or Turn Down first: the bar takes
        // them right to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass: Make Code or Turn Down in the app's colour.
        AppTheme.StyleButton(btnOk, AppTheme.ButtonKind.Primary);

        DialogFit.Keep(this, page, width: 472 + Section.Inset.Horizontal);
    }
}
