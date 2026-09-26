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
/// </remarks>
public sealed class AnnouncerForm : Form
{
    public AnnouncerForm(AnnouncerControl announcer)
    {
        Text = "Announcer";
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(900, 620);
        ClientSize = new Size(1000, 860);

        // Not an owned window: an owned one cannot be put behind its owner
        // or left on another monitor while the owner is used, which is
        // exactly what this is for.
        Controls.Add(announcer);

        AppTheme.ApplyRecursively(this);
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
