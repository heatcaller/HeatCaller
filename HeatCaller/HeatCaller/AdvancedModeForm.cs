namespace HeatCaller;

/// <summary>
/// "Turn on Advanced mode?" (1 Nov 2026, Simple and Advanced mode, round 1):
/// what Advanced shows, what changing it can break, and how to put it back,
/// with a tick box the Turn On button waits for. The words are
/// SettingsModes', as the plan has them.
///
/// Stay in Simple Mode is the default button, so Enter never turns it on;
/// Turn On Advanced Mode is greyed until the box is ticked. While a race is
/// running a second line says changes take effect right away.
///
/// On the layout pieces (DialogFit), like DeleteRaceDayForm: the body wraps
/// to the width and scrolls on a screen too short for it, the buttons stay
/// in view at the foot.
/// </summary>
public sealed class AdvancedModeForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 520;

    private readonly CheckBox _chkUnderstand = new ThemedCheckBox();

    public AdvancedModeForm(bool raceRunning)
    {
        Text = SettingsModes.WarningTitle;
        Font = AppTheme.BodyFont;

        var page = new FillPage();
        var body = DialogFit.Body(page);

        body.Add(new Label
        {
            Text = SettingsModes.WarningTitle,
            AutoSize = true,
            Font = AppTheme.HeadingFont,
        }, gapBelow: UiLayout.LabelGap);

        // 18 Nov 2026, the dialog look: the warning, the race line and the
        // tick in one card under the heading, with a hint line under the tick.
        var card = new Section("Before you turn it on");

        card.Add(new Label
        {
            Text = SettingsModes.WarningText,
            AutoSize = true,
            Font = AppTheme.BodyFont,

            // "Backup & Restore", not "Backup  Restore" with the & taken for
            // a shortcut key.
            UseMnemonic = false,
        });

        if (raceRunning)
        {
            card.Add(new Label
            {
                Text = SettingsModes.WarningRaceLine,
                AutoSize = true,
                ForeColor = AppTheme.Warning,
                Font = AppTheme.BoldFont,
            });
        }

        _chkUnderstand.Text = SettingsModes.WarningTick;
        _chkUnderstand.AutoSize = true;
        _chkUnderstand.Font = AppTheme.BodyFont;

        card.Add(_chkUnderstand, stretch: false, gapBelow: UiLayout.HintGap);

        card.Add(new Label
        {
            Text = "Turn On Advanced Mode works once this is ticked.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: 0);

        // The last part of the body: nothing under it inside the part that
        // scrolls - see DialogFit.Body.
        body.Add(card, gapBelow: 0);

        var btnTurnOn = new Button { Text = SettingsModes.TurnOnButton, Font = AppTheme.BodyFont, DialogResult = DialogResult.OK, Enabled = false };
        var btnStay = new Button { Text = SettingsModes.StayButton, Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        _chkUnderstand.CheckedChanged += (_, _) => btnTurnOn.Enabled = _chkUnderstand.Checked;

        // Against the right-hand edge, Stay furthest right; Tab reaches the
        // tick first, then Turn On.
        page.Add(new ButtonBar(alignRight: true).With(btnStay, btnTurnOn), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnStay;
        CancelButton = btnStay;

        // Start on the tick, not on a button: a focused button outranks
        // AcceptButton, and Turn On is greyed anyway until it is ticked.
        Load += (_, _) => ActiveControl = _chkUnderstand;

        AppTheme.ApplyRecursively(this);

        // The way on, in the primary colour; still greyed until the tick.
        AppTheme.StyleButton(btnTurnOn, AppTheme.ButtonKind.Primary);

        // As wide as it is at the normal text size, or wider when the tick's
        // words need more on one line inside the card - a tick does not wrap.
        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + Section.Inset.Horizontal + _chkUnderstand.GetPreferredSize(Size.Empty).Width + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    /// <summary>
    /// Asks, and says whether Advanced mode was turned on. Saves nothing -
    /// the caller does (MainForm.TurnOnAdvancedMode).
    /// </summary>
    internal static bool Ask(IWin32Window? owner, bool raceRunning)
    {
        using var form = new AdvancedModeForm(raceRunning);
        return form.ShowDialog(owner) == DialogResult.OK;
    }
}

/// <summary>
/// "Manager PIN" (1 Nov 2026, Simple and Advanced mode, round 5): asked
/// before Advanced mode's warning when the club has ticked "Ask for the
/// manager PIN to turn on Advanced mode" (AppSettings.AdvancedNeedsManagerPin).
/// The same PIN the race-day manager's phone page asks for, so there is one
/// thing for a club to remember.
///
/// Cancel is the default button. A wrong PIN says so under the box and
/// stays open for another try.
/// </summary>
public sealed class ManagerPinForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 420;

    internal const string Title = "Manager PIN";

    private readonly TextBox _txtPin = new() { UseSystemPasswordChar = true };

    private readonly Label _lblWrong = new() { AutoSize = true, Text = " " };

    public ManagerPinForm(string expected)
    {
        Text = Title;
        Font = AppTheme.BodyFont;

        var page = new FillPage();
        var body = DialogFit.Body(page);

        body.Add(new Label
        {
            Text = "Enter the manager PIN",
            AutoSize = true,
            Font = AppTheme.HeadingFont,
        }, gapBelow: UiLayout.LabelGap);

        // 18 Nov 2026, the dialog look, as AdvancedModeForm: a heading over
        // one card - where the PIN comes from as its hint line, the box, and
        // the line kept for a wrong PIN. The box, its masking and the check
        // as before.
        var card = new Section("To turn on Advanced mode");

        card.Add(new Label
        {
            Text = "Advanced mode needs the manager PIN on this PC. It's the PIN the race-day manager's phone page asks for.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: UiLayout.RowGap);

        var grid = new FieldGrid();
        grid.AddRow("Manager PIN:", _txtPin);
        card.Add(grid, gapBelow: UiLayout.HintGap);

        _lblWrong.ForeColor = AppTheme.Danger;
        _lblWrong.Font = AppTheme.SmallFont;
        card.Add(_lblWrong, gapBelow: 0);

        // The last part of the body: nothing under it inside the part that
        // scrolls - see DialogFit.Body.
        body.Add(card, gapBelow: 0);

        var btnOk = new Button { Text = "OK", Font = AppTheme.BodyFont };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        btnOk.Click += (_, _) =>
        {
            if (Matches(_txtPin.Text, expected))
            {
                DialogResult = DialogResult.OK;
                return;
            }

            _lblWrong.Text = "That isn't the manager PIN.";
            _txtPin.SelectAll();
            _txtPin.Focus();
        };

        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);

        Controls.Add(page);

        // Enter tries the PIN; Escape cancels. The box has the focus, so
        // Enter reaches OK rather than a focused button.
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Load += (_, _) => ActiveControl = _txtPin;

        AppTheme.ApplyRecursively(this);

        // The way on, in the primary colour, as Turn On Advanced Mode is.
        AppTheme.StyleButton(btnOk, AppTheme.ButtonKind.Primary);

        _txtPin.Width = UiLayout.AtTextSize(140);

        DialogFit.Keep(this, page, UiLayout.AtTextSize(DialogWidth), body: body);
    }

    /// <summary>Whether what was typed is the PIN, compared in a time that does not depend on how much of it was right.</summary>
    internal static bool Matches(string typed, string expected) =>
        expected.Length > 0
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(typed.Trim()),
            System.Text.Encoding.UTF8.GetBytes(expected.Trim()));

    /// <summary>Asks, and says whether the right PIN was given.</summary>
    internal static bool Ask(IWin32Window? owner, string expected)
    {
        using var form = new ManagerPinForm(expected);
        return form.ShowDialog(owner) == DialogResult.OK;
    }
}
