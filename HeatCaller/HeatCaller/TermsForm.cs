namespace HeatCaller;

/// <summary>
/// The beta license agreement and the privacy notice (Terms), in a window.
///
/// Two ways it opens:
///
///  - As the gate (<see cref="Gate"/>), before anything else at startup,
///    whenever the text on this PC has not been accepted: on the first
///    start after installing, and again whenever the terms change. The
///    whole of both parts in a box that scrolls, one tick for each part -
///    greyed out until the box has been scrolled to its end - and Accept
///    and Continue only when both are ticked.
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
    private readonly Label? _lblScroll;

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

        // 18 Nov 2026, the dialog look: the heading, the line under it and
        // the text in one card, the ticks under it on the page as before. A
        // second card round the ticks took two lines of the text at
        // 1280x720, where the window is as tall as the screen allows, so
        // there is one; the window is wider by the card's sides so the line
        // under the heading wraps as it did. Only the window round the text
        // changed: every word, tick, button and what is recorded is as it was.
        var card = new Section();
        card.Add(lblHeading, gapBelow: 2);
        card.Add(lblWhat, gapBelow: UiLayout.RowGap);
        card.AddFill(txtTerms, () => LeastLines * Pieces.LineHeight(Font), gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);

        if (asking)
        {
            // Both ticks stay greyed out until the text has been scrolled
            // to its end - Part 2 is the second half of it - so neither can
            // be ticked without at least passing the whole of both parts
            // (Asher, 28 Oct 2026). See WatchForTheEnd.
            _chkLicense = new CheckBox
            {
                Text = "I accept the license agreement (Part 1)",
                AutoSize = true,
                Enabled = false,
            };

            _chkPrivacy = new CheckBox
            {
                Text = "I have read and accept the privacy notice (Part 2)",
                AutoSize = true,
                Enabled = false,
            };

            _lblScroll = new Label
            {
                Text = ScrollFirst,
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
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

            page.Add(_lblScroll, gapBelow: UiLayout.HintGap);
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

        if (_btnAccept is not null)
        {
            // The way on, in the primary colour; greyed as before until both
            // are ticked.
            AppTheme.StyleButton(_btnAccept, AppTheme.ButtonKind.Primary);
        }

        DialogFit.Keep(this, page, width: 680 + Section.Inset.Horizontal, height: 660);

        if (asking)
        {
            // DialogFit keeps a dialog off the taskbar; the gate has no
            // window behind it - it opens before the startup screen, centred
            // on the screen - and one that cannot be found on the taskbar
            // looks like HeatCaller failed to start.
            ShowInTaskbar = true;

            WatchForTheEnd(txtTerms);
        }

        // The text opens at its top, not with all of it selected.
        Shown += (_, _) =>
        {
            txtTerms.SelectionStart = 0;
            txtTerms.SelectionLength = 0;
            txtTerms.ScrollToCaret();
        };
    }

    private const string ScrollFirst = "Scroll to the end of both parts to tick the boxes.";
    private const string ScrolledToEnd = "You've reached the end. Tick both boxes to continue.";

    /// <summary>
    /// Lets the ticks be ticked once the end of the text has been shown.
    ///
    /// A TextBox raises nothing when it scrolls - not for the wheel, the
    /// keys or a drag of its scrollbar - so it is looked at four times a
    /// second instead, for as long as the window is up and the end has not
    /// been reached: the last character in view is the end reached. A
    /// window big enough to show all of it at once counts at once.
    /// </summary>
    private void WatchForTheEnd(TextBox text)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 250 };

        timer.Tick += (_, _) =>
        {
            // The last character that shows: the text ends in a line break,
            // which has no place on screen of its own.
            var end = text.Text.TrimEnd().Length - 1;

            if (text.IsDisposed || !text.IsHandleCreated || end < 0)
            {
                return;
            }

            var last = text.GetPositionFromCharIndex(end);

            if (last.Y + text.Font.Height > text.ClientSize.Height)
            {
                return;
            }

            timer.Stop();

            _chkLicense!.Enabled = true;
            _chkPrivacy!.Enabled = true;
            _lblScroll!.Text = ScrolledToEnd;
        };

        Shown += (_, _) => timer.Start();
        FormClosed += (_, _) => timer.Dispose();
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
