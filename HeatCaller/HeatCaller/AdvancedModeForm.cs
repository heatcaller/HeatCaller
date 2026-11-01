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

        body.Add(new Label
        {
            Text = SettingsModes.WarningText,
            AutoSize = true,
            Font = AppTheme.BodyFont,
        });

        if (raceRunning)
        {
            body.Add(new Label
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

        // The last part of the body: nothing under it inside the part that
        // scrolls - see DialogFit.Body.
        body.Add(_chkUnderstand, stretch: false, gapBelow: 0);

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

        // As wide as it is at the normal text size, or wider when the tick's
        // words need more on one line - a tick does not wrap.
        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + _chkUnderstand.GetPreferredSize(Size.Empty).Width + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight);

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
