namespace HeatCaller;

/// <summary>
/// How the layout test (HeatCaller.Tests\LayoutTests) gets at the pages.
///
/// The test builds this window as the app does, shows it off-screen and
/// never activated, puts each page up in turn and measures it with
/// LayoutAudit. None of this is used when the app runs.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// Set by the layout test before it shows the window.
    ///
    /// While it is set, showing the window does nothing that showing it
    /// normally does - no web server, no relay, no startup questions, no
    /// reveal (the window stays invisible) - and LayoutDoctor is kept off
    /// every page, so what is measured is each page's own layout rather
    /// than the doctor's repairs of it.
    /// </summary>
    internal bool LayoutCheckMode { get; set; }

    /// <summary>
    /// Shown without taking the focus, in layout-check mode. The test runs
    /// while somebody may be typing somewhere else, and an invisible window
    /// that took their keystrokes would be a nasty surprise.
    /// </summary>
    protected override bool ShowWithoutActivation => LayoutCheckMode || base.ShowWithoutActivation;

    /// <summary>Every page in the sidebar, hidden ones included, by the name the sidebar gives it.</summary>
    internal IReadOnlyList<(string Name, Control Page)> LayoutCheckPages =>
        _navItems.Select(item => (item.Name, (Control)item.Page)).ToList();

    /// <summary>Puts a page on screen the way clicking it does - its refresh runs - without the doctor.</summary>
    internal void ShowPageForLayoutCheck(string name) => ShowPage(name);

    /// <summary>
    /// Gives the pages the room they would have in a window with this much
    /// inside its frame.
    ///
    /// The window itself cannot simply be made that size: Windows will not
    /// let a window grow past the screen it is on, and this app runs
    /// without DPI awareness, so on a laptop at 150% scaling a 1920x1080
    /// window is out of reach. The content area is a child of the window,
    /// and a child can be any size - so it is taken off its docking and
    /// sized by hand to exactly what docking would have given it: the
    /// width less the sidebar, the full height.
    /// </summary>
    internal void SizeContentForLayoutCheck(Size client)
    {
        pnlContent.Dock = DockStyle.None;
        pnlContent.Bounds = new Rectangle(
            pnlSidebar.Width,
            0,
            Math.Max(1, client.Width - pnlSidebar.Width),
            Math.Max(1, client.Height));
    }
}
