using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// A dialog built on the layout pieces, and sized to what is in it.
///
/// The dialogs were laid out the way the pages were: a ClientSize chosen by
/// eye at the normal text size and every control placed in pixels inside
/// it. At a larger text size the captions outgrew their places and the
/// buttons ran off the bottom, and a dialog made for a big screen opened
/// taller than a small one, its buttons out of reach.
///
/// Built this way instead: the dialog's content is a FillPage - each part as
/// tall as it needs, the list (when there is one) taking the height left -
/// with the dialog's buttons as its last part, so they stay in view and a
/// window too short for the rest scrolls it instead. <see cref="Keep"/> then
/// sizes the window as it opens: a width chosen at the normal text size and
/// followed at the size in use, and either the height the content needs at
/// that width or, for a dialog with a list, the height asked for - never
/// less than the least the content can do with, and neither ever more than
/// the screen the dialog opens on. And it opens centred over its owner.
///
/// The dialog is built at the text size in use when it opens - it is
/// short-lived, so it is not kept in step with a change made while it is up.
/// It is sized on Load: by then it counts as shown - its parts report their
/// own visibility, and have their handles, so a drop-down is its real
/// height - but it is not on screen yet, so it is never seen at another
/// size.
///
/// A dialog that uses this goes on LayoutAudit.ConvertedWindows, and the
/// layout test (LayoutTests.EveryConvertedWindowFitsAtEverySize) holds it
/// to nothing overlapping or cut off, and to fitting the screen, at every
/// size - measuring it the way this sizes it, through FitForLayoutCheck.
/// </summary>
internal static class DialogFit
{
    /// <summary>Room kept between a dialog and the edges of its screen.</summary>
    private const int ScreenMargin = 24;

    /// <summary>What a dialog given Keep is sized to: its page, and the width and height asked for.</summary>
    private sealed record Fitting(FillPage Page, int Width, int? Height);

    private static readonly ConditionalWeakTable<Form, Fitting> Fittings = new();

    /// <summary>
    /// Sizes <paramref name="dialog"/> to <paramref name="page"/> - which
    /// fills it - each time it opens. Call it once the dialog is built and
    /// themed (AppTheme.ApplyRecursively): the theme pass sets the faces the
    /// buttons are sized in.
    /// </summary>
    /// <param name="width">How wide the dialog is inside its frame, at the normal text size.</param>
    /// <param name="height">
    /// For a dialog with a list that takes the height: how tall it opens
    /// inside its frame, at the normal text size - never less than the least
    /// its content can do with. It can be resized, down to that least. Null
    /// for a dialog as tall as its content, which cannot be.
    /// </param>
    public static void Keep(Form dialog, FillPage page, int width, int? height = null)
    {
        dialog.AutoScaleMode = AutoScaleMode.None;
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.MinimizeBox = false;
        dialog.ShowInTaskbar = false;
        dialog.FormBorderStyle = height is null ? FormBorderStyle.FixedDialog : FormBorderStyle.Sizable;

        // One that can be resized can be made as big as the screen - a list
        // of eleven columns wants all the width there is.
        dialog.MaximizeBox = height is not null;

        // Its own layout, nothing for the doctor to straighten - see
        // LayoutDoctor.Watch, which every dialog is given by the theme pass.
        LayoutDoctor.LeaveAlone(dialog);

        // The page ends at its buttons. The theme pass gives a panel that
        // scrolls room under its last control, so a page scrolled to its end
        // does not stop hard against the window's edge; under a dialog's
        // buttons it was a blank band, the page's own margin and 32px more.
        LayoutDoctor.NoRoomBelow(page);

        Fittings.AddOrUpdate(dialog, new Fitting(page, width, height));
        dialog.Load += (_, _) => Fit(dialog);
    }

    /// <summary>
    /// The window sized and placed. On Load: the window and its handle are
    /// there, and it is not on screen yet - so it is never seen at another
    /// size, and a dialog opened as a modal has already been placed by
    /// WinForms (which Manual stops) before this.
    /// </summary>
    private static void Fit(Form dialog)
    {
        if (!Fittings.TryGetValue(dialog, out var fitting))
        {
            return;
        }

        var area = WorkingArea(dialog);
        var frame = dialog.Size - dialog.ClientSize;
        var most = Most(area, frame);
        var (clientWidth, least) = SizeClient(dialog, fitting, most);

        if (fitting.Height is not null)
        {
            // Resizable, down to the least the content can do with - or to
            // the screen, when the screen is shorter; a page shorter than
            // its least scrolls rather than squash the list (FillPage).
            dialog.MinimumSize = new Size(
                frame.Width + Math.Min(clientWidth, UiLayout.AtTextSize(Math.Min(fitting.Width, 360))),
                frame.Height + Math.Min(least, most.Height));
        }

        Centre(dialog, area);
    }

    /// <summary>The most the screen leaves for inside the frame.</summary>
    private static Size Most(Rectangle area, Size frame) => new(
        Math.Max(1, area.Width - 2 * ScreenMargin - frame.Width),
        Math.Max(1, area.Height - 2 * ScreenMargin - frame.Height));

    /// <summary>
    /// Sizes <paramref name="client"/> - the dialog, or the layout test's
    /// stand-in for its inside - to the page in it: the width asked for at
    /// the text size in use, and the height the page needs at that width
    /// (or the height asked for, never less than that), neither more than
    /// <paramref name="most"/>. Returns the width, and the least the page
    /// can do with at it.
    /// </summary>
    private static (int Width, int Least) SizeClient(Control client, Fitting fitting, Size most)
    {
        var clientWidth = Math.Min(UiLayout.AtTextSize(fitting.Width), most.Width);

        // The width first, at the most height there is, and laid out: a
        // field grid learns the height its rows really come to only by
        // laying them out (FieldGrid.GetPreferredSize), and the least the
        // page can do with is worked out from them. Then the height, and
        // again until it stays put - three times at most, so a page that
        // could not settle cannot hang the dialog opening.
        client.ClientSize = new Size(clientWidth, most.Height);
        client.PerformLayout();

        var least = fitting.Page.LeastHeight(clientWidth);

        for (var again = 0; again < 3; again++)
        {
            var clientHeight = Math.Min(
                fitting.Height is int asked ? Math.Max(UiLayout.AtTextSize(asked), least) : least,
                most.Height);

            client.ClientSize = new Size(clientWidth, clientHeight);
            client.PerformLayout();

            var now = fitting.Page.LeastHeight(clientWidth);

            if (now == least)
            {
                break;
            }

            least = now;
        }

        return (clientWidth, least);
    }

    /// <summary>
    /// For the layout test (LayoutTests.EveryConvertedWindowFitsAtEverySize):
    /// the dialog's page taken out of it and put in <paramref name="client"/>,
    /// sized as the dialog's inside would be on a screen with
    /// <paramref name="area"/> to open in. Returns how big the dialog would
    /// be, frame and all.
    ///
    /// Not the dialog itself: Windows will not let a window - one inside
    /// another included (Form.SetBoundsCore) - be bigger than the screen the
    /// test runs on, which is smaller than the screens it checks for; a
    /// panel can be any size. Call it with the client on screen, as the
    /// dialog counts as shown when it is sized (see the class notes).
    /// </summary>
    internal static Size FitForLayoutCheck(Form dialog, Control client, Rectangle area)
    {
        if (!Fittings.TryGetValue(dialog, out var fitting))
        {
            throw new InvalidOperationException($"{dialog.GetType().Name} was not given DialogFit.Keep.");
        }

        client.Font = dialog.Font;
        client.BackColor = dialog.BackColor;
        client.ForeColor = dialog.ForeColor;
        client.Controls.Add(fitting.Page);

        // The frame for its border, worked out the way Windows works it out
        // (Form.SizeFromClientSize) - asked for a small inside, well under
        // any screen, so nothing trims it. With the page out of it, so the
        // page is not laid out at that size for nothing.
        var inside = new Size(200, 200);
        dialog.ClientSize = inside;
        var frame = dialog.Size - inside;

        SizeClient(client, fitting, Most(area, frame));

        return client.Size + frame;
    }

    /// <summary>
    /// Where the dialog is opening: its owner's screen, or the screen of
    /// the app's active window, or the one the pointer is on.
    /// </summary>
    private static Rectangle WorkingArea(Form dialog) =>
        OverWhat(dialog) is { } over
            ? Screen.FromControl(over).WorkingArea
            : Screen.FromPoint(Cursor.Position).WorkingArea;

    /// <summary>
    /// The window the dialog opens over. ShowDialog with an owner that is a
    /// window of the app makes it the Owner for as long as the dialog is up;
    /// without one, the dialog is owned by the active window, which is not
    /// said anywhere a dialog can ask - so that is asked for instead.
    /// </summary>
    private static Form? OverWhat(Form dialog)
    {
        if (dialog.Owner is { IsDisposed: false } owner)
        {
            return owner;
        }

        return Form.ActiveForm is { IsDisposed: false } active && !ReferenceEquals(active, dialog)
            ? active
            : null;
    }

    /// <summary>Centred over the window it opens over when that is showing, else on the screen - and kept on the screen either way.</summary>
    private static void Centre(Form dialog, Rectangle area)
    {
        var over = OverWhat(dialog) is { Visible: true, WindowState: not FormWindowState.Minimized } shown
            ? shown.Bounds
            : area;

        var x = over.Left + (over.Width - dialog.Width) / 2;
        var y = over.Top + (over.Height - dialog.Height) / 2;

        dialog.Location = new Point(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - dialog.Width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - dialog.Height)));
    }
}
