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
/// window too short for the rest scrolls it instead. A dialog with no list
/// but a lot to say puts what it says in a <see cref="Body"/>, which takes
/// the list's place: the window opens as tall as all of it, and on a screen
/// too short for that, the body scrolls and the buttons stay in view.
/// <see cref="Keep"/> then sizes the window as it opens: a width chosen at
/// the normal text size and followed at the size in use (or one the dialog
/// measured itself), and the height the content needs at that width - or,
/// for a dialog with a list, the height asked for - never less than the
/// least the content can do with, and neither ever more than the screen the
/// dialog opens on. And it opens centred over its owner.
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

    /// <summary>A body's least height at the normal text size - a few lines - below which the window scrolls instead.</summary>
    private const int BodyLeast = 120;

    /// <summary>What a dialog given Keep is sized to: its page, the width and height asked for, its body if it has one, and whether the width is already at the text size in use.</summary>
    private sealed record Fitting(FillPage Page, int Width, int? Height, PageStack? Body, bool WidthMeasured);

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
    /// <param name="body">
    /// For a dialog as tall as its content, the <see cref="Body"/> the
    /// content is in: the window is sized to show all of it.
    /// </param>
    /// <param name="widthMeasured">
    /// True when <paramref name="width"/> is already at the text size in
    /// use - measured by the dialog from what it holds - rather than a width
    /// at the normal text size to be followed.
    /// </param>
    /// <param name="frameless">
    /// No frame at all - a dialog that draws its own edge and title, as the
    /// message box does. It cannot be resized.
    /// </param>
    public static void Keep(
        Form dialog,
        FillPage page,
        int width,
        int? height = null,
        PageStack? body = null,
        bool widthMeasured = false,
        bool frameless = false)
    {
        dialog.AutoScaleMode = AutoScaleMode.None;
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.MinimizeBox = false;
        dialog.ShowInTaskbar = false;
        dialog.FormBorderStyle = frameless
            ? FormBorderStyle.None
            : height is null ? FormBorderStyle.FixedDialog : FormBorderStyle.Sizable;

        // One that can be resized can be made as big as the screen - a list
        // of eleven columns wants all the width there is.
        dialog.MaximizeBox = height is not null && !frameless;

        // The page ends at its buttons. The theme pass gives a panel that
        // scrolls room under its last control, so a page scrolled to its end
        // does not stop hard against the window's edge; under a dialog's
        // buttons it was a blank band, the page's own margin and 32px more.
        ScrollTheming.NoRoomBelow(page);

        Fittings.AddOrUpdate(dialog, new Fitting(page, width, height, body, widthMeasured));
        dialog.Load += (_, _) => Fit(dialog);
    }

    /// <summary>
    /// A column for what a dialog says, taking the page's height above its
    /// buttons: give it to <see cref="Keep"/> as the body, and the window
    /// opens as tall as all of it. On a screen too short for that, it
    /// scrolls on its own - down to a few lines - and the buttons under it
    /// stay in view. Add the dialog's parts to it as to any PageStack, and
    /// the buttons to the page after it.
    /// </summary>
    public static PageStack Body(FillPage page, int gapBelow = UiLayout.SectionGap)
    {
        // The page gives the margins; nothing under the last part, for the
        // reason Keep gives the page none.
        var body = new PageStack { Padding = Padding.Empty };
        ScrollTheming.NoRoomBelow(body);

        page.AddFill(
            body,
            _ => body.ContentHeight > 0
                ? Math.Min(body.ContentHeight, UiLayout.AtTextSize(BodyLeast))
                : UiLayout.AtTextSize(BodyLeast),
            gapBelow);

        return body;
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

        // The window's handles made, up to here (WhereTimeGoes).
        WhereTimeGoes.Mark("handles");

        var area = WorkingArea(dialog);
        var frame = dialog.Size - dialog.ClientSize;
        var most = Most(area, frame);
        var (clientWidth, least) = SizeClient(dialog, fitting, most);
        LetGo(fitting.Page);

        if (fitting.Height is not null && dialog.FormBorderStyle != FormBorderStyle.None)
        {
            // Resizable: wider, and taller or shorter - down to the least
            // the content can do with, or to the screen when the screen is
            // shorter (a page shorter than its least scrolls rather than
            // squash the list; FillPage). Never narrower than it opened:
            // what is in it was measured for that width, and a field grid
            // or a rail of steps made narrower runs past the edge - the
            // page scrolls down, never sideways.
            dialog.MinimumSize = new Size(
                frame.Width + clientWidth,
                frame.Height + Math.Min(least, most.Height));
        }

        Centre(dialog, area);
    }

    /// <summary>
    /// A page its dialog held while its windows were made
    /// (SetupEntryForm) let go - children first, see LayoutFreeze.Thaw -
    /// once the dialog is sized, so it is laid out once, at the size it
    /// opens at, rather than at the size it was built at and then again.
    /// Measured while held, so a held page must be one whose least height
    /// is worked out without laying it out (no field grid in it). Nothing
    /// for a page not held.
    /// </summary>
    private static void LetGo(FillPage page)
    {
        if (!LayoutFreeze.IsFrozen(page))
        {
            return;
        }

        LayoutFreeze.Thaw(page, childrenFirst: true);
        WhereTimeGoes.Mark("let go, sized");
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
        var clientWidth = Math.Min(
            fitting.WidthMeasured ? fitting.Width : UiLayout.AtTextSize(fitting.Width),
            most.Width);

        // Room the dialog keeps round the page - the message box's, for the
        // rim it paints round its edge. The page is that much smaller than
        // the inside of the window.
        var inset = client.Padding;

        // The width first, at the most height there is, and laid out: a
        // field grid learns the height its rows really come to only by
        // laying them out (FieldGrid.GetPreferredSize), and the least the
        // page can do with - and a body, how tall its content is - is worked
        // out from them. Then the height, and again until it stays put -
        // three times at most, so a page that could not settle cannot hang
        // the dialog opening.
        client.ClientSize = new Size(clientWidth, most.Height);
        client.PerformLayout();
        WhereTimeGoes.Mark("first layout");

        var least = fitting.Page.LeastHeight(clientWidth - inset.Horizontal) + inset.Vertical;
        WhereTimeGoes.Mark("least height");

        for (var again = 0; again < 3; again++)
        {
            var wanted = fitting.Height is int asked
                ? Math.Max(UiLayout.AtTextSize(asked), least)
                : fitting.Body is { } body
                    // All of the body: the room the rest of the page leaves
                    // it now, less what it has, plus what it needs.
                    ? Math.Max(least, client.ClientSize.Height - body.Height + body.ContentHeight)
                    : least;

            var clientHeight = Math.Min(wanted, most.Height);

            if (clientHeight == client.ClientSize.Height && again > 0)
            {
                break;
            }

            client.ClientSize = new Size(clientWidth, clientHeight);
            client.PerformLayout();
            WhereTimeGoes.Mark($"layout {again + 2}");

            least = fitting.Page.LeastHeight(clientWidth - inset.Horizontal) + inset.Vertical;
            WhereTimeGoes.Mark($"least height {again + 2}");
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
        client.Padding = dialog.Padding;
        client.Controls.Add(fitting.Page);

        // The frame for its border, worked out the way Windows works it out
        // (Form.SizeFromClientSize) - asked for a small inside, well under
        // any screen, so nothing trims it. With the page out of it, so the
        // page is not laid out at that size for nothing.
        var inside = new Size(200, 200);
        dialog.ClientSize = inside;
        var frame = dialog.Size - inside;

        SizeClient(client, fitting, Most(area, frame));
        LetGo(fitting.Page);

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
