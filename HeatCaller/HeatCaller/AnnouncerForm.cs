namespace HeatCaller;

/// <summary>
/// A window around <see cref="AnnouncerControl"/> - the running order, the
/// talking points and the spotlight, for whoever is on the microphone.
/// </summary>
/// <remarks>
/// A WINDOW RATHER THAN A TAB, because of who is looking at it.
///
/// It used to be a tab under Voice, reached by a button on Race Control that
/// switched the whole app over to it. During a race those are the only two
/// screens anybody is on, and one of them replacing the other is the wrong
/// shape: whoever is calling the race wants the running order in front of
/// them WHILE the director works the flags. On one machine this can sit
/// beside the main window; on a second monitor it can own that screen. As a
/// tab it could do neither, and every glance at the announcer's notes took
/// Race Control off the screen.
///
/// CLOSING IT HIDES IT. It is not destroyed and rebuilt, and the control
/// inside it is handed in rather than owned - see OnFormClosing for both
/// reasons. The short version is that the announcer's talking points live on
/// that control and nowhere else.
///
/// SIZED TO ITS SCREEN. It opens 1000x860 inside its frame at the normal
/// text size - bigger with the text - where MainForm puts it, beside the main
/// window when there is room (MainForm.OpenAnnouncerWindow, which reads its
/// width to decide). Then, as it first opens (OnLoad), it is made to fit the
/// screen it is on: never bigger than the screen's working area, moved back
/// onto it if it hangs off an edge, and never made smaller than the page can
/// do with - its least height at the width it opened at, and a least width
/// at which the two columns of cards still fit (AnnouncerControl) - nor than
/// the screen, when the page wants more. A short window scrolls the page.
/// It is an ordinary window otherwise: resizable, in the taskbar, not owned.
/// It used to be a fixed ClientSize of 1000x860 and a MinimumSize of 900x620
/// whatever the screen - taller than a 1366x768 or 1280x720 screen has room
/// for, so the bottom of the page was off the screen and could not be
/// reached.
/// </remarks>
public sealed class AnnouncerForm : Form
{
    /// <summary>How big it opens inside its frame, at the normal text size - the size it always opened at.</summary>
    private const int OpenWidth = 1000;
    private const int OpenHeight = 860;

    /// <summary>
    /// The narrowest it can be made inside its frame, at the normal text
    /// size: two columns of cards with room for a long driver name in the
    /// spotlight's picker and the Repeat button's longer caption beside it.
    /// Narrower than that, a column would be squeezed under what is in it.
    /// </summary>
    private const int LeastClientWidth = 720;

    /// <summary>The page inside, asked how short it can be - see OnLoad.</summary>
    private readonly AnnouncerControl _announcer;

    public AnnouncerForm(AnnouncerControl announcer)
    {
        // Seen only once painted - see DialogFit.RevealWhenPainted.
        DialogFit.RevealWhenPainted(this);

        _announcer = announcer;

        Text = "Announcer";
        StartPosition = FormStartPosition.Manual;

        // At the text size in use, so a larger text size opens a larger
        // window rather than cramming the page - the screen is the limit,
        // and that is seen to on Load. MainForm reads the width before then
        // to decide where the window goes.
        ClientSize = new Size(UiLayout.AtTextSize(OpenWidth), UiLayout.AtTextSize(OpenHeight));

        // Not an owned window: an owned one cannot be put behind its owner
        // or left on another monitor while the owner is used, which is
        // exactly what this is for.
        Controls.Add(announcer);

        AppTheme.ApplyRecursively(this);
    }

    /// <summary>
    /// Fits the window to its screen as it first opens - once, since Load
    /// comes only with the first Show, and after that it stays wherever and
    /// however big it was left. By then MainForm has placed it, and it is
    /// not on the screen yet, so it is never seen at another size.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToScreen();
    }

    /// <summary>
    /// No bigger than the screen's working area, and on it: moved in from
    /// an edge it hangs off. Its least size is the least width at the text
    /// size in use and the page's least height at the width it opens at -
    /// never more than the screen, for a page that wants more; the page then
    /// scrolls. Widened to its least first, since how short the page can be
    /// depends on how wide it is.
    /// </summary>
    private void FitToScreen()
    {
        var area = Screen.FromControl(this).WorkingArea;
        var frame = Size - ClientSize;

        var leastWidth = Math.Min(area.Width, frame.Width + UiLayout.AtTextSize(LeastClientWidth));
        var width = Math.Clamp(Width, leastWidth, area.Width);

        if (Width != width)
        {
            Width = width;
        }

        // Laid out at that width, so the page is measured as it will be.
        PerformLayout();

        var least = new Size(
            leastWidth,
            Math.Min(area.Height, frame.Height + _announcer.LeastHeight(ClientSize.Width)));

        // May make the window taller, when it was shorter than the page's
        // least - still no taller than the screen.
        MinimumSize = least;

        var size = new Size(Math.Min(Width, area.Width), Math.Clamp(Height, least.Height, area.Height));

        Bounds = new Rectangle(
            Math.Clamp(Left, area.Left, area.Right - size.Width),
            Math.Clamp(Top, area.Top, area.Bottom - size.Height),
            size.Width,
            size.Height);
    }

    /// <summary>
    /// The X hides this window instead of destroying it.
    /// </summary>
    /// <remarks>
    /// Two things would be lost by letting it close for real.
    ///
    /// The talking points are held in the control itself - there is no store
    /// behind them - so a window that took the control down with it would
    /// throw away every point gathered so far and come back empty.
    ///
    /// Less obviously, the control would stop COLLECTING them. New points
    /// arrive from a background thread and are marshalled in only while the
    /// control has a window handle; unparenting it destroys that handle, so
    /// a closed-and-reopened window would show only what arrived after it
    /// was reopened. As a tab this never came up - the control kept its
    /// handle for the life of the app - and hiding keeps that true.
    ///
    /// A real shutdown is let through: the close reason is then the app
    /// exiting rather than somebody pressing the X, and there is nothing
    /// left to preserve.
    /// </remarks>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason is CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
}
