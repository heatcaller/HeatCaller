using System.Drawing;

namespace HeatCaller;

/// <summary>
/// Handing the desk over (12 Nov 2026, setup plan D8).
///
/// WHY: at most clubs the race director races too. While they're on the
/// rostrum somebody has to run the starts, and somebody else is often
/// minding the sign-on desk on the laptop, ticking in late arrivals, taking
/// money, and well able to press the wrong thing on Race Control by
/// accident. Two pieces:
///  - Hand the Desk to a Phone... (Auto start's menu on Race Control): the
///    phone remote's QR code, so a deputy can run Hold, +1 min, Start now and
///    Marshals in from their phone, with the Remote PIN.
///  - Desk View... (Who's Racing): the laptop locked to Who's Racing -
///    check-ins, papers and money - until somebody types the PIN chosen when
///    it was locked. Other pages, the other Race Day tabs and the shortcut
///    keys do nothing meanwhile. The PIN is kept only while the app is open.
/// </summary>
public partial class MainForm
{
    /// <summary>True while the laptop is locked to Who's Racing.</summary>
    private bool _deskOnly;

    /// <summary>The PIN that unlocks it, chosen when it was locked. Never saved.</summary>
    private string? _deskPin;

    /// <summary>The small window that says the desk view is on, with Unlock.</summary>
    private Form? _deskBanner;

    /// <summary>Shows Who's Racing, set by the Race Day page when it is built.</summary>
    private Action? _showWhosRacing;

    /// <summary>The Race Day tabs a desk view may not leave for, so their Selecting can refuse them.</summary>
    internal bool DeskViewAllowsTab(TabPage? page, TabPage whosRacing) => !_deskOnly || ReferenceEquals(page, whosRacing);

    /// <summary>Who's Racing's "Desk View...": choose a PIN, then lock.</summary>
    private void StartDeskView()
    {
        if (_deskOnly)
        {
            return;
        }

        using var ask = new DeskPinForm(choosing: true);

        if (ask.ShowDialog(this) != DialogResult.OK || ask.Pin is not { Length: >= 4 } pin)
        {
            return;
        }

        _deskPin = pin;
        _deskOnly = true;
        _showWhosRacing?.Invoke();

        lstNav.Enabled = false;
        txtNavSearch.Enabled = false;

        OnClientRawMessage($"{AppTime.LogStampNow()}  [desk] Desk view on: this laptop is on Who's Racing until it's unlocked.");
        ShowDeskBanner();
    }

    /// <summary>Asks for the PIN, and unlocks when it's right. True when unlocked.</summary>
    private bool UnlockDeskView()
    {
        if (!_deskOnly)
        {
            return true;
        }

        using var ask = new DeskPinForm(choosing: false);

        if (ask.ShowDialog(_deskBanner is { IsDisposed: false } banner ? banner : this) != DialogResult.OK)
        {
            return false;
        }

        if (!string.Equals(ask.Pin, _deskPin, StringComparison.Ordinal))
        {
            AppDialog.Show(this, "That isn't the PIN the desk view was locked with.", "Desk view", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _deskOnly = false;
        _deskPin = null;
        lstNav.Enabled = true;
        txtNavSearch.Enabled = true;

        if (_deskBanner is { IsDisposed: false } open)
        {
            open.Close();
        }

        _deskBanner = null;
        OnClientRawMessage($"{AppTime.LogStampNow()}  [desk] Desk view off.");
        return true;
    }

    /// <summary>A page other than Race Day while the desk view is on: refused, with why.</summary>
    private bool DeskViewRefusesPage(string name)
    {
        if (!_deskOnly || name == "Race Day")
        {
            return false;
        }

        _deskBanner?.Activate();
        return true;
    }

    /// <summary>Keys the desk view lets through: typing, and the usual editing shortcuts. No function keys or other shortcuts.</summary>
    internal static bool DeskViewAllowsKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;

        if (key is >= Keys.F1 and <= Keys.F24)
        {
            return false;
        }

        if ((keyData & Keys.Alt) != 0)
        {
            return false;
        }

        if ((keyData & Keys.Control) != 0)
        {
            return key is Keys.C or Keys.V or Keys.X or Keys.A or Keys.Z;
        }

        return true;
    }

    private void ShowDeskBanner()
    {
        var banner = new Form
        {
            Text = "Desk View",
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            ShowInTaskbar = false,
            TopMost = true,
            ControlBox = false,
            StartPosition = FormStartPosition.Manual,
            Font = AppTheme.BodyFont,
            BackColor = AppTheme.Background,
        };

        var lbl = new Label
        {
            Text = "Desk view: check-ins, papers and money only.",
            AutoSize = true,
            ForeColor = AppTheme.Warning,
            Margin = new Padding(0, 6, 8, 0),
        };

        var unlock = new Button { Text = "Unlock...", AutoSize = true };
        unlock.Click += (_, _) => UnlockDeskView();

        var page = new FillPage();
        page.Add(new ButtonBar(tight: true).With(lbl, unlock), gapBelow: 0);
        banner.Controls.Add(page);

        AppTheme.ApplyRecursively(banner);
        DialogFit.Keep(banner, page, width: 420, height: 90);

        var screen = Screen.FromControl(this).WorkingArea;
        banner.Location = new Point(screen.Right - banner.Width - 24, screen.Top + 24);

        _deskBanner = banner;
        banner.Show(this);
    }
}

/// <summary>Choosing the desk view's PIN, or typing it to unlock (setup plan D8).</summary>
internal sealed class DeskPinForm : Form
{
    private readonly TextBox _txtPin = new() { UseSystemPasswordChar = true, Width = 140 };
    private readonly TextBox _txtAgain = new() { UseSystemPasswordChar = true, Width = 140 };
    private readonly Label _lblProblem = new() { AutoSize = true, ForeColor = AppTheme.Warning };
    private readonly bool _choosing;

    public string? Pin { get; private set; }

    public DeskPinForm(bool choosing)
    {
        _choosing = choosing;

        Text = choosing ? "Desk View" : "Unlock the Desk View";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var lbl = new Label
        {
            Text = choosing
                ? "Locks this laptop to Who's Racing - check-ins, papers and money - for a volunteer at the desk. "
                  + "Other pages and the shortcut keys do nothing until it's unlocked. Choose a PIN of four or more "
                  + "digits to unlock it with; it's kept only until HeatCaller closes."
                : "Type the PIN the desk view was locked with.",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
        };

        var grid = new FieldGrid();
        grid.AddRow("PIN:", _txtPin);

        if (choosing)
        {
            grid.AddRow("Again:", _txtAgain);
        }

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnOk = new Button { Text = choosing ? "Lock" : "Unlock", AutoSize = true };
        btnOk.Click += (_, _) => Accept();

        var page = new FillPage();
        page.Add(lbl, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(grid, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(_lblProblem, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 480, height: choosing ? 280 : 200);
    }

    private void Accept()
    {
        var pin = _txtPin.Text.Trim();

        if (_choosing && (pin.Length < 4 || !pin.All(char.IsDigit)))
        {
            _lblProblem.Text = "Four or more digits, please.";
            return;
        }

        if (_choosing && pin != _txtAgain.Text.Trim())
        {
            _lblProblem.Text = "The two PINs aren't the same.";
            return;
        }

        Pin = pin;
        DialogResult = DialogResult.OK;
    }
}

/// <summary>Closing the app while the desk view is on (setup plan D8).</summary>
public partial class MainForm
{
    /// <summary>
    /// A person closing the app while the desk view is on is asked for its
    /// PIN first. Before the FormClosing handlers run, so a refused close
    /// tears nothing down (they leave a cancelled user close alone). Windows
    /// shutting down is never held up.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_deskOnly && e.CloseReason == CloseReason.UserClosing && !UnlockDeskView())
        {
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }
}

/// <summary>Hand the desk to a phone (setup plan D8): the phone remote's QR code, from Race Control.</summary>
public partial class MainForm
{
    /// <summary>The Local Screens page, for the remote's address.</summary>
    private WebServerControl? _localScreens;

    private void HandTheDeskToAPhone() => _localScreens?.ShowDeskHandover();
}
