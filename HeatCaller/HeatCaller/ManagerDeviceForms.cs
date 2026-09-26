using QRCoder;

namespace HeatCaller;

/// <summary>
/// The phones allowed to drive the race-day manager through the relay:
/// which ones, enrolling another, and taking one back.
///
/// Enrolling a phone is a thing done at the desk, on purpose. The key is
/// shown as a QR code once, the phone reads it off the screen in the room,
/// and it is never shown again - because a key that can be looked up later
/// is a key that can be taken later. Losing one costs a fresh enrolment,
/// which is a thirty-second job; being unable to lose one would cost
/// rather more.
///
/// The first enrolment is also the switch. With no phone enrolled the
/// tunnel behaves as it always has and the relay is taken at its word;
/// with one, every request has to be signed by a phone this club knows.
/// That is said plainly on the page rather than hidden in a tickbox: it is
/// the one consequence somebody needs to have understood before they press
/// the button.
/// </summary>
internal sealed class ManagerDevicesForm : Form
{
    private readonly AppSettings _settings;
    private readonly string _managePageUrl;
    private readonly ListView _list = new();
    private readonly Label _lblState = new();
    private readonly Button _btnEnrol = new();
    private readonly Button _btnRemove = new();

    public ManagerDevicesForm(AppSettings settings, string managePageUrl)
    {
        _settings = settings;
        _managePageUrl = managePageUrl;

        Text = "Manager Phones";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = AppTheme.BodyFont;

        const int width = 640;

        var lblIntro = new Label
        {
            Text = "A phone enrolled here signs every race-day manager request with a key only it and this laptop hold, "
                 + "so the relay cannot make a request up. Enrol each phone at the desk by scanning the code this shows.",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(UiLayout.PageLeft, UiLayout.PageTop),
        };

        Controls.Add(lblIntro);

        var y = UiLayout.Below(lblIntro, UiLayout.RowGap);

        _list.Location = new Point(UiLayout.PageLeft, y);
        _list.Size = new Size(width, 200);
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.Columns.Add("Phone", 260);
        _list.Columns.Add("Enrolled", 170);
        _list.Columns.Add("Last used", 170);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

        Controls.Add(_list);

        y = UiLayout.Below(_list, UiLayout.HintGap);

        // Fixed two lines, so what it says cannot change the dialog's height.
        _lblState.Location = new Point(UiLayout.PageLeft, y);
        _lblState.AutoSize = false;
        _lblState.Size = new Size(width, AppTheme.SmallFont.Height * 2 + 4);
        _lblState.Font = AppTheme.SmallFont;

        Controls.Add(_lblState);

        y = UiLayout.Below(_lblState, UiLayout.RowGap);

        _btnEnrol.Text = "Enrol a Phone...";
        _btnEnrol.Font = AppTheme.BodyFont;
        _btnEnrol.Click += (_, _) => Enrol();

        _btnRemove.Text = "Remove";
        _btnRemove.Font = AppTheme.BodyFont;
        _btnRemove.Click += (_, _) => Remove();

        var btnClose = new Button { Text = "Close", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };

        foreach (var button in new[] { _btnEnrol, _btnRemove, btnClose })
        {
            button.Size = new Size(UiLayout.WidthFor(button), UiLayout.ButtonHeight);
        }

        var right = UiLayout.PageLeft + width;

        btnClose.Location = new Point(right - btnClose.Width, y);
        _btnEnrol.Location = new Point(UiLayout.PageLeft, y);
        _btnRemove.Location = new Point(_btnEnrol.Right + UiLayout.ButtonGap, y);

        Controls.Add(_btnEnrol);
        Controls.Add(_btnRemove);
        Controls.Add(btnClose);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        ClientSize = new Size(right + UiLayout.PageLeft, UiLayout.Below(btnClose, UiLayout.PageTop));

        RefreshList();
    }

    private void RefreshList()
    {
        var selectedId = _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as string : null;

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var device in _settings.ManagerDevices)
        {
            var item = _list.Items.Add(device.Name.Length > 0 ? device.Name : "(unnamed phone)");

            item.SubItems.Add(AppTime.DateAndTime(device.EnrolledUtc.ToLocalTime()));
            item.SubItems.Add(device.LastSeenUtc is { } seen ? AppTime.DateAndTime(seen.ToLocalTime()) : "never");
            item.Tag = device.Id;

            if (string.Equals(device.Id, selectedId, StringComparison.Ordinal))
            {
                item.Selected = true;
            }
        }

        _list.EndUpdate();

        if (_settings.ManagerDevices.Count == 0)
        {
            _lblState.Text = "No phone is enrolled, so manager requests are taken on the relay's word alone. "
                           + "Enrolling the first phone is what changes that.";
            _lblState.ForeColor = AppTheme.TextMuted;
        }
        else
        {
            _lblState.Text = _settings.ManagerDevices.Count == 1
                ? "One phone enrolled. A request that is not signed by it is refused."
                : $"{_settings.ManagerDevices.Count} phones enrolled. A request signed by none of them is refused.";
            _lblState.ForeColor = AppTheme.Success;
        }

        UpdateButtons();
    }

    private void UpdateButtons() => _btnRemove.Enabled = _list.SelectedItems.Count == 1;

    private void Enrol()
    {
        if (_managePageUrl.Length == 0)
        {
            AppDialog.Show(this,
                "The club's web address is not set up yet, so there is no manager page for a phone to be enrolled onto. "
                + "Set the relay up on the Club Website page first.",
                "No club address", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var name = new ManagerEnrolNameForm();

        if (name.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var (id, key) = RelayManagerDevices.Mint();

        var device = new ManagerDevice
        {
            Id = id,
            Name = name.PhoneName,
            KeyHex = key,
            EnrolledUtc = DateTime.UtcNow,
        };

        // Saved BEFORE the code is shown, not after. A phone that scans a
        // key this laptop then fails to write down is a phone that is
        // signed out with no way to tell why; the other way round, an
        // enrolment nobody scanned is one dead row in this list.
        _settings.ManagerDevices.Add(device);
        _settings.Save();

        RefreshList();

        using var show = new ManagerEnrolCodeForm(RelayManagerDevices.EnrolUrl(_managePageUrl, id, key), device.Name);
        show.ShowDialog(this);
    }

    private void Remove()
    {
        if (_list.SelectedItems.Count != 1 || _list.SelectedItems[0].Tag is not string id)
        {
            return;
        }

        var device = _settings.ManagerDevices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));

        if (device is null)
        {
            return;
        }

        var last = _settings.ManagerDevices.Count == 1;

        var warning = last
            ? "\r\n\r\nThis is the last one. With no phone enrolled, manager requests go back to being taken on the "
              + "relay's word alone."
            : string.Empty;

        if (AppDialog.Show(this,
                $"Remove \"{(device.Name.Length > 0 ? device.Name : "this phone")}\"?\r\n\r\n"
                + "It stops being able to drive the race-day manager as soon as this laptop next collects a request. "
                + "Enrolling it again means scanning a new code at the desk." + warning,
                "Remove phone", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _settings.ManagerDevices.Remove(device);
        _settings.Save();

        RefreshList();
    }
}

/// <summary>Just the name, asked before a key is minted so the list is readable a month later.</summary>
internal sealed class ManagerEnrolNameForm : Form
{
    private readonly TextBox _name = new();

    /// <summary>What the club calls this phone. Never checked against anything - it is a label.</summary>
    public string PhoneName => _name.Text.Trim();

    public ManagerEnrolNameForm()
    {
        Text = "Enrol a Phone";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = AppTheme.BodyFont;

        const int width = 420;

        var lblWhich = new Label
        {
            Text = "Whose phone is this?",
            AutoSize = true,
            Location = new Point(UiLayout.PageLeft, UiLayout.PageTop),
        };

        Controls.Add(lblWhich);

        _name.Location = new Point(UiLayout.PageLeft, UiLayout.Below(lblWhich, UiLayout.LabelGap));
        _name.Width = width;
        _name.MaxLength = 60;

        Controls.Add(_name);

        var lblHint = new Label
        {
            Text = "For this list only - \"Dave's phone\", \"the desk iPad\". It is never sent anywhere.",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(UiLayout.PageLeft, UiLayout.Below(_name, UiLayout.HintGap)),
        };

        Controls.Add(lblHint);

        var y = UiLayout.Below(lblHint, UiLayout.SectionGap);

        var btnOk = new Button { Text = "Show the Code", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        btnOk.Size = new Size(UiLayout.WidthFor(btnOk), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        var right = UiLayout.PageLeft + width;

        btnCancel.Location = new Point(right - btnCancel.Width, y);
        btnOk.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnOk.Width, y);

        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        ClientSize = new Size(right + UiLayout.PageLeft, UiLayout.Below(btnOk, UiLayout.PageTop));
    }
}

/// <summary>
/// The enrolment code itself, shown once.
///
/// The key is in the URL's FRAGMENT - the part after the "#" - which a
/// browser keeps to itself and never puts in a request. So a phone that
/// scans this reaches the manager page over the relay without the key
/// having gone anywhere near it: not in the request line, not in a log,
/// not in a proxy, not in a Referer header on the way past. The page picks
/// it up, files it, and takes it straight back out of the address bar.
///
/// Nothing on screen spells the key out in text. It would be one screenshot
/// away from being a shared secret, and there is no use for it typed - a
/// phone camera is the only thing that ever needs to read it.
/// </summary>
internal sealed class ManagerEnrolCodeForm : Form
{
    private readonly Bitmap? _qr;

    public ManagerEnrolCodeForm(string enrolUrl, string phoneName)
    {
        Text = "Scan This Now";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = AppTheme.BodyFont;

        const int width = 460;

        var lblWho = new Label
        {
            Text = phoneName.Length > 0
                ? $"Point {phoneName} at this and open the page it offers."
                : "Point the phone at this and open the page it offers.",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Font = AppTheme.BoldFont,
            Location = new Point(UiLayout.PageLeft, UiLayout.PageTop),
        };

        Controls.Add(lblWho);

        _qr = Render(enrolUrl);

        var picture = new PictureBox
        {
            Location = new Point(UiLayout.PageLeft, UiLayout.Below(lblWho, UiLayout.RowGap)),
            Size = new Size(width, width),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = _qr,
            BackColor = Color.White,
        };

        Controls.Add(picture);

        var lblOnce = new Label
        {
            Text = "Shown once. Close this and it is gone - the key is not kept anywhere it can be looked at again. "
                 + "If the scan did not take, remove the phone from the list and enrol it afresh.",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.Warning,
            Font = AppTheme.SmallFont,
            Location = new Point(UiLayout.PageLeft, UiLayout.Below(picture, UiLayout.RowGap)),
        };

        Controls.Add(lblOnce);

        var btnDone = new Button { Text = "Done", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };
        btnDone.Size = new Size(UiLayout.WidthFor(btnDone), UiLayout.ButtonHeight);

        var right = UiLayout.PageLeft + width;

        btnDone.Location = new Point(right - btnDone.Width, UiLayout.Below(lblOnce, UiLayout.SectionGap));

        Controls.Add(btnDone);

        AcceptButton = btnDone;
        CancelButton = btnDone;

        AppTheme.ApplyRecursively(this);

        // After the theme pass: the code has to stay black on white to
        // scan, whatever the app is wearing.
        picture.BackColor = Color.White;

        ClientSize = new Size(right + UiLayout.PageLeft, UiLayout.Below(btnDone, UiLayout.PageTop));
    }

    /// <summary>The code as a bitmap, or null if QRCoder could not make one - the label above still says what to do.</summary>
    private static Bitmap? Render(string enrolUrl)
    {
        try
        {
            using var generator = new QRCodeGenerator();

            // Q, as the badges use: a screen is a clean surface, but a
            // phone held at an angle across a desk is not a clean read.
            using var data = generator.CreateQrCode(enrolUrl, QRCodeGenerator.ECCLevel.Q);
            using var png = new PngByteQRCode(data);
            using var stream = new MemoryStream(png.GetGraphic(10));
            using var loaded = Image.FromStream(stream);

            // Copied off the stream rather than held on it. Image.FromStream
            // keeps the stream for the image's whole life, and this one is
            // about to close.
            return new Bitmap(loaded);
        }
        catch
        {
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _qr?.Dispose();
        }

        base.Dispose(disposing);
    }
}
