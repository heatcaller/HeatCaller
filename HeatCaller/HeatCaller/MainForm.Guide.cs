namespace HeatCaller;

/// <summary>
/// THE USER GUIDE BUTTON at the foot of the sidebar.
///
/// WHY (25 Nov 2026, Asher: "put the guide online and link the app to it,
/// then find a better place in the app for people to find"): the only way to
/// the guide was a link in the About box, two clicks from Backup &amp;
/// Restore, to the home page. The sidebar's footer is on every page, open
/// or narrowed to its icons, beside the other buttons that are always there
/// (Manage Navigation, Mute and Undo), so the guide goes there. A press opens
/// the guide at the page you're looking at (GuidePages). Desk View turns the
/// whole footer off, as it does for the rest.
///
/// Not F1, the usual help key: on Race Control F1 starts the race.
/// </summary>
public partial class MainForm
{
    private readonly Button btnGuide = new() { AccessibleName = "User Guide", Cursor = Cursors.Hand, TabStop = true };

    /// <summary>What the User Guide button's tooltip says.</summary>
    internal const string GuideButtonTip = "User Guide: how this page works";

    /// <summary>Wired once; the look is set again with the other footer buttons on every theme change. Added to the footer in MainForm.Designer.cs.</summary>
    private bool _guideButtonWired;

    private void SetUpGuideButton()
    {
        if (!_guideButtonWired)
        {
            _guideButtonWired = true;
            btnGuide.Click += (_, _) => AppSupport.OpenGuide(GuidePages.For(_shownPage));
        }

        SetUpFooterButton(btnGuide, DrawGuideIcon);
        _footerTooltip.SetToolTip(btnGuide, GuideButtonTip);
    }

    /// <summary>The address the User Guide button opens now - the guide's page for the page showing.</summary>
    internal string GuideAddressNow => GuidePages.For(_shownPage);

    /// <summary>An open book: two pages from a spine down the middle, on the footer's 24-unit grid.</summary>
    private static void DrawGuideIcon(Graphics g, Rectangle b, Color color) => DrawOnIconGrid(g, b, grid =>
    {
        using var pen = IconPen(color);

        // The left page and the right page, each curving down into the spine.
        grid.DrawBezier(pen, 12f, 7f, 9f, 5f, 6f, 5f, 3f, 5.5f);
        grid.DrawLine(pen, 3f, 5.5f, 3f, 18f);
        grid.DrawBezier(pen, 3f, 18f, 6f, 17.5f, 9f, 17.5f, 12f, 19.5f);

        grid.DrawBezier(pen, 12f, 7f, 15f, 5f, 18f, 5f, 21f, 5.5f);
        grid.DrawLine(pen, 21f, 5.5f, 21f, 18f);
        grid.DrawBezier(pen, 21f, 18f, 18f, 17.5f, 15f, 17.5f, 12f, 19.5f);

        // The spine.
        grid.DrawLine(pen, 12f, 7f, 12f, 19.5f);
    });
}
