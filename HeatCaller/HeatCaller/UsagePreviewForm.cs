namespace HeatCaller;

/// <summary>
/// See What's Sent (19 Nov 2026, usage totals round 1): every usage total not yet sent that
/// this PC holds, in words and then exactly as the report carries them, from
/// Backup &amp; Restore's Getting help card. Read-only, with Close.
///
/// On the layout pieces (DialogFit), as TermsForm's read-again window is: the
/// heading and the line under it, then the text taking the height the window
/// has and scrolling on its own, the button at the foot.
/// </summary>
public sealed class UsagePreviewForm : Form
{
    /// <summary>The window's title - and its name on LayoutAudit.ConvertedWindows.</summary>
    internal const string Title = "What HeatCaller sends";

    /// <summary>The text shows at least this many lines before the window scrolls instead.</summary>
    private const int LeastLines = 10;

    internal UsagePreviewForm(AppUsage usage, bool on)
    {
        Text = Title;
        Font = AppTheme.BodyFont;

        var lblHeading = new Label
        {
            Text = "Usage totals",
            AutoSize = true,
            Font = AppTheme.TitleFont,
            ForeColor = AppTheme.TextDark,
        };

        var lblWhat = new Label
        {
            Text = on ? OnLine : OffLine,
            AutoSize = true,
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextDark,
        };

        var txtReport = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Text = on ? ReportFor(usage) : "",
            TabStop = false,
        };

        var card = new Section();
        card.Add(lblHeading, gapBelow: 2);
        card.Add(lblWhat, gapBelow: UiLayout.RowGap);
        card.AddFill(txtReport, () => LeastLines * Pieces.LineHeight(Font), gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        Controls.Add(page);
        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 680 + Section.Inset.Horizontal, height: 560);

        Shown += (_, _) =>
        {
            txtReport.SelectionStart = 0;
            txtReport.SelectionLength = 0;
            txtReport.ScrollToCaret();
        };
    }

    internal const string OnLine =
        "Everything HeatCaller has counted on this PC and not sent yet, by day. No driver's name or anything typed is ever in it. "
        + "Every few hours, finished days go to HeatCaller through your club's relay and are then deleted from this PC. "
        + "Without the relay, nothing is sent.";

    internal const string OffLine =
        "Send HeatCaller anonymous usage totals is unticked, so nothing is counted or sent, and anything "
        + "counted before it was unticked has been deleted.";

    /// <summary>When days last went, then the words, then the report exactly as it would go.</summary>
    internal static string ReportFor(AppUsage usage) =>
        LastSent(usage.LastSentUtc)
        + Environment.NewLine + Environment.NewLine
        + usage.ReportText()
        + Environment.NewLine + Environment.NewLine
        + "The report itself:" + Environment.NewLine
        + usage.ReportJson().ReplaceLineEndings(Environment.NewLine);

    /// <summary>"Last sent to your club's relay: ..." in the PC's own time, or that nothing has been yet.</summary>
    internal static string LastSent(DateTime? sentUtc) =>
        sentUtc is { } sent
            ? "Last sent to your club's relay: " + sent.ToLocalTime().ToString("f", System.Globalization.CultureInfo.CurrentCulture) + "."
            : "Nothing has been sent from this PC yet.";

    /// <summary>Opens it, owned, so it centres on the page that asked.</summary>
    internal static void ShowFor(IWin32Window? owner, AppUsage usage, bool on)
    {
        using var form = new UsagePreviewForm(usage, on);
        form.ShowDialog(owner);
    }
}
