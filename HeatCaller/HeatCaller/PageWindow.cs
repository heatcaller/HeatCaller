namespace HeatCaller;

/// <summary>
/// A page in a window of its own, with Close under it: the two drawers
/// MainForm opens off its pages - the scrutineering log off Who's Racing,
/// and the timetable off the program's footer (MainForm.Pages,
/// ShowScrutineeringLog and ShowTimetable). Modal, as they always were: the
/// caller shows it with ShowDialog.
///
/// On the layout pieces (DialogFit): the page filling the window, and Close
/// at the foot against the right-hand edge. The window opens at the size
/// the drawer always had at the normal text size - bigger with the text,
/// never bigger than the screen - and can be resized. The page keeps its
/// own margins, as the Classes page does in its window (RaceClassManagerForm).
/// It used to be placed in pixels: each drawer was a Form built inline in
/// MainForm, a ClientSize and a MinimumSize chosen by hand and the page
/// docked over a footer panel ButtonHeight + 24 tall, with Close at 12,12 in
/// it at the standard button size - windows taller than a 1280x720 screen
/// has room for.
/// </summary>
internal sealed class PageWindow : Form
{
    /// <summary>The page shown - see <see cref="GiveBackPage"/>.</summary>
    private readonly Control _page;

    /// <summary>The panel the page fills, which fills the window's place for it.</summary>
    private readonly Panel _slot;

    /// <param name="title">The window's caption.</param>
    /// <param name="page">The page, filling the window above Close. It is themed with the window.</param>
    /// <param name="width">How wide the window opens inside its frame, at the normal text size.</param>
    /// <param name="height">How tall it opens inside its frame, at the normal text size.</param>
    /// <param name="leastHeight">The least the page can do with at the width it is given - its own FillPage's (LeastHeight).</param>
    private PageWindow(string title, Control page, int width, int height, Func<int, int> leastHeight)
    {
        _page = page;

        Text = title;
        ShowIcon = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        // The page goes in a panel of this window's own, and the panel takes
        // the height (FillPage.AddFill) - not the page itself. The fill hooks
        // the part it is given (its Layout event, to hear it grow), and the
        // timetable is one page that outlives every window it is shown in:
        // given straight to the fill, it would carry a hook into each window
        // it had ever been in, long after that window was gone. The panel
        // goes with its window.
        _slot = new Panel { Tag = AppTheme.KeepBackColor };

        page.Dock = DockStyle.Fill;
        _slot.Controls.Add(page);

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        // No margins round the page: it keeps its own, and this page's on
        // top of them would put it twice as far from the window's edges as
        // from Close. Only under Close, the page's usual bottom margin.
        var fill = new FillPage { Padding = new Padding(0, 0, 0, UiLayout.PageTop) };

        // The least the page can do with at the width it has, as the page
        // works it out (its LeastHeight) - below that, the window scrolls
        // rather than squash the page's list.
        fill.AddFill(_slot, leastHeight, gapBelow: 0);

        // Against the right-hand edge, in from it by the page's margin.
        var closeBar = fill.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);
        closeBar.Margin = new Padding(UiLayout.PageLeft, 0, UiLayout.PageRight, 0);

        Controls.Add(fill);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        DialogFit.Keep(this, fill, width: width, height: height);
    }

    /// <summary>
    /// The scrutineering log - every inspection on file - from Who's
    /// Racing's Scrutineering Log... button, a fresh page each time it is
    /// opened. 1040x660 inside the frame, the size it always opened at.
    /// </summary>
    public static PageWindow ScrutineeringLog(TechInspectionControl log) =>
        new("Scrutineering log", log, width: 1040, height: 660, leastHeight: log.LeastHeight);

    /// <summary>
    /// The timetable from the program's footer: the one page MainForm keeps
    /// for it, lent to the window - so given back (<see cref="GiveBackPage"/>)
    /// before the window is disposed, or it is disposed with it. 1040x700
    /// inside the frame, the size it always opened at.
    /// </summary>
    public static PageWindow Timetable(DayScheduleControl timetable) =>
        new("Timetable", timetable, width: 1040, height: 700, leastHeight: timetable.LeastHeight);

    /// <summary>
    /// Takes the page back out of the window, for a page that outlives it:
    /// disposing a window disposes everything still in it. Call it once the
    /// window has closed (FormClosed). Nothing happens if the page is not in
    /// the window any more.
    /// </summary>
    public void GiveBackPage()
    {
        if (ReferenceEquals(_page.Parent, _slot))
        {
            _slot.Controls.Remove(_page);
        }
    }
}
