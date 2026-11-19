namespace HeatCaller;

/// <summary>
/// "Restore default settings?" (1 Nov 2026, Simple and Advanced mode, round
/// 4): what goes back and what is kept, in plain words, the one option to
/// reset what Simple mode shows as well, and that a copy is saved first.
/// The words are SettingsRestore's.
///
/// Cancel is the default button, so Enter never restores. While a race or
/// practice is running, Restore Defaults is greyed and a line says why: the
/// restore ends in a restart.
///
/// On the layout pieces (DialogFit), like AdvancedModeForm.
/// </summary>
public sealed class RestoreDefaultsForm : Form
{
    /// <summary>
    /// How wide the window is inside its frame at the normal text size.
    /// Wider since the 18 Nov 2026 card: narrower, the list wrapped onto so
    /// many lines that, in a card, the tick went below the fold at 1280x720.
    /// </summary>
    private const int DialogWidth = 840;

    private readonly CheckBox _chkAlsoShown = new ThemedCheckBox();

    /// <summary>Whether what Simple mode shows is to go back too. Only meaningful after DialogResult.OK.</summary>
    public bool AlsoShown => _chkAlsoShown.Checked;

    public RestoreDefaultsForm(bool raceRunning)
    {
        // Built with its layout held - see LayoutFreeze.HoldWhileBuilding
        // (18 Nov 2026): built live, most parts had their windows before the
        // theme pass, which made many of them again.
        using var building = LayoutFreeze.HoldWhileBuilding();

        Text = SettingsRestore.ConfirmTitle;
        Font = AppTheme.BodyFont;

        var page = new FillPage();
        var body = DialogFit.Body(page);

        body.Add(new Label
        {
            Text = SettingsRestore.ConfirmTitle,
            AutoSize = true,
            Font = AppTheme.HeadingFont,
        }, gapBelow: UiLayout.LabelGap);

        // 18 Nov 2026, the dialog look: what goes back, what is kept, the
        // tick and what happens next in one card under the heading - the
        // tick is one more thing that can go back. A second card for the
        // tick put it below the fold at 1280x720. Every word, the tick and
        // the gating as before.
        var goesBack = new Section("What goes back");

        goesBack.Add(new Label
        {
            Text = "Every setting Simple mode hides goes back to how HeatCaller ships:",
            AutoSize = true,
            Font = AppTheme.BodyFont,
        }, gapBelow: UiLayout.HintGap);

        foreach (var area in SettingsRestore.WhatGoesBack)
        {
            goesBack.Add(new Label
            {
                Text = "• " + area,
                AutoSize = true,
                Font = AppTheme.BodyFont,

                // "Room & Web", not "Room  Web" with the & taken for a
                // shortcut key.
                UseMnemonic = false,
            }, gapBelow: UiLayout.HintGap);
        }

        goesBack.Add(new Label
        {
            Text = SettingsRestore.WhatIsKept,
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            UseMnemonic = false,
        }, gapBelow: UiLayout.SectionGap);

        _chkAlsoShown.Text = SettingsRestore.AlsoShownTick;
        _chkAlsoShown.AutoSize = true;
        _chkAlsoShown.Font = AppTheme.BodyFont;

        goesBack.Add(_chkAlsoShown, stretch: false, gapBelow: UiLayout.HintGap);

        goesBack.Add(new Label
        {
            Text = SettingsRestore.CopyLine + " " + SettingsRestore.RestartLine,
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        }, gapBelow: raceRunning ? UiLayout.RowGap : 0);

        if (raceRunning)
        {
            goesBack.Add(new Label
            {
                Text = SettingsRestore.RaceLine,
                AutoSize = true,
                ForeColor = AppTheme.Warning,
                Font = AppTheme.BoldFont,
            }, gapBelow: 0);
        }

        // The last part of the body: nothing under it inside the part that
        // scrolls - see DialogFit.Body.
        body.Add(goesBack, gapBelow: 0);

        var btnRestore = new Button { Text = "Restore Defaults", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK, Enabled = !raceRunning };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        // Against the right-hand edge, Cancel furthest right.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnRestore), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnCancel;
        CancelButton = btnCancel;

        // Start on Cancel: a focused button outranks AcceptButton.
        Load += (_, _) => ActiveControl = btnCancel;

        AppTheme.ApplyRecursively(this);

        // The way on, in the primary colour; greyed as before during a race.
        AppTheme.StyleButton(btnRestore, AppTheme.ButtonKind.Primary);

        // As wide as it is at the normal text size, or wider when the tick's
        // words need more on one line inside its card - a tick does not wrap.
        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + Section.Inset.Horizontal + _chkAlsoShown.GetPreferredSize(Size.Empty).Width + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }
}

/// <summary>
/// "Put Back Earlier Settings" (1 Nov 2026, round 4): the copies Restore
/// default settings saved before each restore, newest first, by date - one
/// picked goes back over the settings as they are, after a copy of those is
/// saved too, and HeatCaller restarts.
/// </summary>
public sealed class PutBackSettingsForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 460;

    private readonly ListBox _lstCopies = new();

    private readonly IReadOnlyList<(string Path, DateTime When)> _copies;

    /// <summary>The copy picked. Only meaningful after DialogResult.OK.</summary>
    public string? Picked => _lstCopies.SelectedIndex >= 0 && _lstCopies.SelectedIndex < _copies.Count ? _copies[_lstCopies.SelectedIndex].Path : null;

    internal PutBackSettingsForm(IReadOnlyList<(string Path, DateTime When)> copies, bool raceRunning)
    {
        _copies = copies;

        Text = "Put back earlier settings";
        Font = AppTheme.BodyFont;

        var page = new FillPage();

        page.Add(new Label
        {
            Text = copies.Count == 0
                ? "There are no saved settings yet. A copy is saved each time default settings are restored."
                : "Each is the settings as they were just before a restore. The one you pick goes back over the settings now - "
                  + "which are saved as a copy first - and HeatCaller restarts.",
            AutoSize = true,
            Font = AppTheme.BodyFont,
        });

        foreach (var copy in copies)
        {
            // In the club's own 12- or 24-hour time (AppTime).
            _lstCopies.Items.Add($"{copy.When:dddd d MMMM yyyy}, {AppTime.TimeOfDay(copy.When)}");
        }

        _lstCopies.IntegralHeight = false;

        if (copies.Count > 0)
        {
            _lstCopies.SelectedIndex = 0;
        }

        page.AddFill(_lstCopies, _ => _lstCopies.ItemHeight * 5 + 4);

        if (raceRunning)
        {
            page.Add(new Label
            {
                Text = SettingsRestore.RaceLine,
                AutoSize = true,
                ForeColor = AppTheme.Warning,
                Font = AppTheme.BoldFont,
            });
        }

        var btnPutBack = new Button { Text = "Put Back", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK, Enabled = copies.Count > 0 && !raceRunning };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnPutBack), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnCancel;
        CancelButton = btnCancel;
        Load += (_, _) => ActiveControl = copies.Count > 0 ? _lstCopies : btnCancel;

        AppTheme.ApplyRecursively(this);

        DialogFit.Keep(this, page, UiLayout.AtTextSize(DialogWidth));
    }
}
