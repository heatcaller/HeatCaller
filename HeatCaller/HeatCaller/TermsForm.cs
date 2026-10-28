namespace HeatCaller;

/// <summary>
/// The beta license agreement and the privacy notice (Terms), in a window.
///
/// Two ways it opens:
///
///  - As the gate (<see cref="Gate"/>), before anything else at startup,
///    whenever the text on this PC has not been accepted: on the first
///    start after installing, and again whenever the terms change. One tick
///    for each part, and Accept and Continue only when both are ticked.
///    Decline, or closing the window, and HeatCaller does not start.
///  - To read again (<see cref="ShowFor"/>), from Backup &amp; Restore's
///    Getting help card and the About box: the same text, when it was
///    accepted, and Close.
///
/// On the layout pieces (DialogFit), the way the About box is: the heading
/// and the line under it across the top, the text taking the height the
/// window has and scrolling on its own, and the ticks and buttons at the
/// foot where they stay in view.
/// </summary>
public sealed class TermsForm : Form
{
    /// <summary>The window's title - and its name on LayoutAudit.ConvertedWindows.</summary>
    internal const string Title = "HeatCaller license and privacy notice";

    /// <summary>The text shows at least this many lines before the window scrolls instead.</summary>
    private const int LeastLines = 10;

    private readonly CheckBox? _chkLicense;
    private readonly CheckBox? _chkPrivacy;
    private readonly Button? _btnAccept;

    /// <param name="asking">True for the gate: the ticks, Accept and Continue and Decline. False to read it again, with Close.</param>
    internal TermsForm(bool asking)
    {
        Text = Title;
        Font = AppTheme.BodyFont;

        var lblHeading = new Label
        {
            Text = asking ? "Before you use HeatCaller" : "License and privacy notice",
            AutoSize = true,
            Font = AppTheme.TitleFont,
            ForeColor = AppTheme.TextDark,
        };

        var lblWhat = new Label
        {
            Text = asking
                ? "Please read both parts below: the license agreement for HeatCaller, and the privacy notice, "
                  + "which says what happens to drivers' information, including the names sent to ElevenLabs "
                  + "for the announcer's voice. HeatCaller can't be used until both are accepted."
                : AcceptedLine(),
            AutoSize = true,
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextDark,
        };

        var txtTerms = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Text = Terms.ForReading(Terms.Text),
            TabStop = asking,
        };

        var page = new FillPage();
        page.Add(lblHeading, gapBelow: 2);
        page.Add(lblWhat);
        page.AddFill(txtTerms, _ => LeastLines * Pieces.LineHeight(Font), gapBelow: UiLayout.RowGap);

        if (asking)
        {
            _chkLicense = new CheckBox
            {
                Text = "I accept the license agreement (Part 1)",
                AutoSize = true,
            };

            _chkPrivacy = new CheckBox
            {
                Text = "I have read and accept the privacy notice (Part 2)",
                AutoSize = true,
            };

            _btnAccept = new Button { Text = "Accept and Continue", Enabled = false };
            _btnAccept.Click += (_, _) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };

            var btnDecline = new Button { Text = "Decline" };
            btnDecline.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            _chkLicense.CheckedChanged += (_, _) => ShowWhetherReady();
            _chkPrivacy.CheckedChanged += (_, _) => ShowWhetherReady();

            page.Add(_chkLicense, gapBelow: UiLayout.HintGap);
            page.Add(_chkPrivacy, gapBelow: UiLayout.RowGap);

            // Against the right-hand edge, as a dialog's buttons go, the
            // first one furthest right.
            page.Add(new ButtonBar(alignRight: true).With(_btnAccept, btnDecline), gapBelow: 0);

            // Enter never accepts on its own: the two ticks are the point.
            CancelButton = btnDecline;
        }
        else
        {
            var btnClose = new Button { Text = "Close" };
            btnClose.Click += (_, _) => Close();

            page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

            AcceptButton = btnClose;
            CancelButton = btnClose;
        }

        Controls.Add(page);

        AppTheme.ApplyRecursively(this);

        DialogFit.Keep(this, page, width: 680, height: 620);

        if (asking)
        {
            // DialogFit keeps a dialog off the taskbar; the gate has no
            // window behind it - it opens before the startup screen, centred
            // on the screen - and one that cannot be found on the taskbar
            // looks like HeatCaller failed to start.
            ShowInTaskbar = true;
        }

        // The text opens at its top, not with all of it selected.
        Shown += (_, _) =>
        {
            txtTerms.SelectionStart = 0;
            txtTerms.SelectionLength = 0;
            txtTerms.ScrollToCaret();
        };
    }

    /// <summary>Accept and Continue only once both parts are ticked.</summary>
    private void ShowWhetherReady()
    {
        if (_btnAccept is not null)
        {
            _btnAccept.Enabled = _chkLicense is { Checked: true } && _chkPrivacy is { Checked: true };
        }
    }

    /// <summary>When the terms in force were accepted on this PC, for the read-again window.</summary>
    private static string AcceptedLine()
    {
        var accepted = Terms.Read(Path.Combine(AppPaths.Root, Terms.AcceptanceFileName));

        return accepted is not null && accepted.Hash == Terms.Hash
            ? $"These terms ({Terms.Edition}) were accepted on this PC on {accepted.AcceptedUtc.ToLocalTime():d MMMM yyyy} by {accepted.WindowsUser}. "
              + $"Questions: {AppSupport.Email}."
            : $"These are the terms in force ({Terms.Edition}). Questions: {AppSupport.Email}.";
    }

    /// <summary>
    /// The gate, at startup: true when HeatCaller may carry on - the terms
    /// were accepted before, or are accepted now - and false when it must
    /// not start. Records an acceptance made here. Call on the UI thread,
    /// before any other window is built.
    /// </summary>
    internal static bool Gate()
    {
        if (Terms.Text.Length == 0)
        {
            // A build without the terms in it is a broken build - TermsTests
            // holds the resource to existing - and it must not be used.
            SessionLog.Write("[terms] The license and privacy text is missing from this build; HeatCaller has not started.");
            MessageBox.Show(
                "This copy of HeatCaller is missing its license and privacy notice, so it can't start.\r\n\r\n"
                + $"Install it again, or email {AppSupport.Email}.",
                "HeatCaller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (Terms.IsAccepted())
        {
            return true;
        }

        using var form = new TermsForm(asking: true);

        if (form.ShowDialog() != DialogResult.OK)
        {
            SessionLog.Write($"[terms] The license and privacy notice ({Terms.Edition}) were declined; HeatCaller has not started.");
            return false;
        }

        SessionLog.Write(Terms.RecordAcceptance()
            ? $"[terms] The license and privacy notice ({Terms.Edition}) were accepted."
            : $"[terms] The license and privacy notice ({Terms.Edition}) were accepted, but the record couldn't be saved; they will be asked again next time.");

        return true;
    }

    /// <summary>Opens it to read again, owned, so it centres on the page that asked.</summary>
    public static void ShowFor(IWin32Window? owner)
    {
        using var form = new TermsForm(asking: false);
        form.ShowDialog(owner);
    }
}
