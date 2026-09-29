using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// Gives text boxes, lists and spinners a themed hairline edge by removing
/// their own border entirely and drawing a ring around them from the PARENT.
///
/// The obvious approach - keep BorderStyle.Fixed3D and repaint the
/// non-client frame on WM_NCPAINT - was tried and does not hold. Some of
/// these controls draw their border inside the client area instead (anything
/// deriving from UpDownBase paints its own, because it sets UserPaint), and
/// the ones that do use the non-client frame repaint it from paths that
/// never send WM_NCPAINT. The result was a white box surviving on most of
/// the app.
///
/// Painting from the parent sidesteps all of it. The ring sits one pixel
/// outside the control's bounds, so the control can repaint its insides as
/// often as it likes and never touch the edge. Nothing native is fought.
/// </summary>
/// <summary>
/// Clears the ghost a skinned control leaves behind when it moves or
/// resizes.
///
/// A skin that answers WM_PAINT can only ever repaint the control's OWN
/// rectangle. The theming pass, though, changes fonts and sets style bits
/// that recreate handles, and both of those resize controls - so the edge
/// the control drew at its PREVIOUS bounds is left sitting on the parent's
/// surface, just outside the new ones. That is the faint bright rectangle
/// that has turned up around dropdowns on page after page: not a border
/// being drawn wrong, but an old border never being erased.
///
/// Invalidating a slightly inflated rectangle ON THE PARENT is what erases
/// it - the same thing InputOutline does for the controls it rings.
/// </summary>
/// <summary>
/// Repaints a DateTimePicker's closed field: the same rounded surface,
/// hairline border and drawn chevron every other field in the app wears.
///
/// This control honours no colour property for its field - BackColor is
/// ignored outright - so the only way off the system's white box is to
/// answer WM_PAINT, exactly as ComboBoxSkin does. Only the pixels change:
/// the dropdown, the calendar, keyboard editing and hit-testing are all
/// still the native control's.
///
/// The one thing lost is the highlight on the date part being edited,
/// which is painted over. That is a deliberate trade: the pickers here are
/// short-format date-range filters chosen from the calendar, and a white
/// box on a dark page was the more visible problem.
/// </summary>
internal sealed class DateTimePickerSkin : NativeWindow
{
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;

    private static readonly ConditionalWeakTable<DateTimePicker, DateTimePickerSkin> Attached = new();

    private readonly DateTimePicker _picker;
    private bool _hot;

    private DateTimePickerSkin(DateTimePicker picker)
    {
        _picker = picker;

        picker.MouseEnter += (_, _) => { _hot = true; Repaint(); };
        picker.MouseLeave += (_, _) => { _hot = false; Repaint(); };
        picker.ValueChanged += (_, _) => Repaint();
        picker.Enter += (_, _) => Repaint();
        picker.Leave += (_, _) => Repaint();
        picker.GotFocus += (_, _) => Repaint();
        picker.LostFocus += (_, _) => Repaint();
        picker.EnabledChanged += (_, _) => Repaint();
        picker.CloseUp += (_, _) => Repaint();
        picker.DropDown += (_, _) => Repaint();
        picker.Resize += (_, _) => Repaint();
        picker.LocationChanged += (_, _) => Repaint();

        picker.HandleCreated += (_, _) => Bind();
        picker.VisibleChanged += (_, _) => Repaint();
        picker.ParentChanged += (_, _) => Bind();
        picker.HandleDestroyed += (_, _) => ReleaseHandle();
        picker.Disposed += (_, _) => ReleaseHandle();

        Bind();
    }

    public static void Attach(DateTimePicker picker)
    {
        if (Attached.TryGetValue(picker, out _))
        {
            picker.Invalidate();
            return;
        }

        Attached.Add(picker, new DateTimePickerSkin(picker));
    }

    private void Bind()
    {
        if (!_picker.IsHandleCreated || Handle == _picker.Handle)
        {
            return;
        }

        if (Handle != IntPtr.Zero)
        {
            ReleaseHandle();
        }

        AssignHandle(_picker.Handle);
    }

    private void Repaint()
    {
        if (_picker.IsDisposed || !_picker.IsHandleCreated)
        {
            return;
        }

        Bind();
        _picker.Invalidate();
        SkinGhost.ClearAround(_picker);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_ERASEBKGND when !_picker.IsDisposed:
                m.Result = (IntPtr)1;
                return;

            case WM_PAINT when !_picker.IsDisposed && _picker.IsHandleCreated:
                if (OnPaint())
                {
                    m.Result = IntPtr.Zero;
                    return;
                }

                break;
        }

        base.WndProc(ref m);
    }

    private bool OnPaint()
    {
        var ps = new PAINTSTRUCT();
        var hdc = BeginPaint(Handle, ref ps);

        if (hdc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var width = Math.Max(1, _picker.Width);
            var height = Math.Max(1, _picker.Height);

            using var buffer = new Bitmap(width, height);
            using (var g = Graphics.FromImage(buffer))
            {
                Draw(g, width, height);
            }

            using var target = Graphics.FromHdc(hdc);
            target.DrawImageUnscaled(buffer, 0, 0);
        }
        finally
        {
            EndPaint(Handle, ref ps);
        }

        return true;
    }

    private void Draw(Graphics g, int width, int height)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var enabled = _picker.Enabled;

        // Clear, not FillRectangle - the buffer must start OPAQUE or the
        // blit blends with whatever the native control left in the device
        // context. See the long note in ComboBoxSkin.Draw.
        g.Clear(_picker.Parent?.BackColor ?? AppTheme.Background);

        var active = enabled && _picker.Focused;
        var border = !enabled ? AppTheme.Border
            : active ? AppTheme.Primary
            : _hot ? AppTheme.TextMuted
            : AppTheme.Border;

        var bounds = new RectangleF(0.5f, 0.5f, width - 1f, height - 1f);
        var radius = Math.Max(1, Math.Min(7, height / 2));

        using (var path = AppTheme.RoundedRect(bounds, radius))
        using (var fill = new SolidBrush(enabled ? AppTheme.Surface : AppTheme.Background))
        using (var pen = new Pen(border, active ? 1.4f : 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        const int arrowZone = 26;
        var cx = width - arrowZone / 2f - 2;
        var cy = height / 2f - 1;

        using (var arrow = new Pen(!enabled ? AppTheme.TextMuted : active ? AppTheme.Primary : AppTheme.TextMuted, 1.8f))
        {
            arrow.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            arrow.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            arrow.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;

            g.DrawLines(arrow, new[]
            {
                new PointF(cx - 4.5f, cy - 2f),
                new PointF(cx, cy + 2.5f),
                new PointF(cx + 4.5f, cy - 2f),
            });
        }

        var textRect = new Rectangle(10, 0, Math.Max(4, width - arrowZone - 12), height);

        TextRenderer.DrawText(g, _picker.Text, _picker.Font, textRect,
            enabled ? AppTheme.TextDark : AppTheme.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DtpRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public DtpRect rcPaint;
        public int fRestore;
        public int fIncUpdate;

        public int reserved0;
        public int reserved1;
        public int reserved2;
        public int reserved3;
        public int reserved4;
        public int reserved5;
        public int reserved6;
        public int reserved7;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT paint);
}

internal static class SkinGhost
{
    public static void ClearAround(Control control)
    {
        var parent = control.Parent;

        if (parent is null || parent.IsDisposed || !parent.IsHandleCreated)
        {
            return;
        }

        var ring = control.Bounds;
        ring.Inflate(3, 3);
        parent.Invalidate(ring, false);
    }
}

internal static class InputOutline
{
    private static readonly ConditionalWeakTable<Control, object> Tracked = new();
    private static readonly ConditionalWeakTable<Control, object> HookedParents = new();

    private static readonly object Marker = new();

    /// <summary>Inputs that draw their own edge (the sidebar's page search) and are left alone here.</summary>
    private static readonly ConditionalWeakTable<Control, object> Excluded = new();

    /// <summary>
    /// Keeps the square ring off a control that is drawn as part of a
    /// shape of its own - so a later theme pass cannot put it back either.
    /// </summary>
    public static void Exclude(Control control)
    {
        if (!Excluded.TryGetValue(control, out _))
        {
            Excluded.Add(control, Marker);
        }

        control.Parent?.Invalidate();
    }

    public static void Attach(Control control)
    {
        if (Tracked.TryGetValue(control, out _) || Excluded.TryGetValue(control, out _))
        {
            return;
        }

        Tracked.Add(control, Marker);

        // The native border goes, whichever kind it is. That is what removes
        // the white - everything after this is just drawing an edge back.
        switch (control)
        {
            case TextBoxBase textBox:
                textBox.BorderStyle = BorderStyle.None;
                break;
            case ListBox listBox:
                listBox.BorderStyle = BorderStyle.None;
                break;
            case UpDownBase upDown:
                upDown.BorderStyle = BorderStyle.None;
                break;
            case PictureBox pictureBox:
                pictureBox.BorderStyle = BorderStyle.None;
                break;
            case ListView listView:
                // The white rectangle around every list. ListView's border
                // is a real window style, so BorderStyle.None is what drops
                // it; the themed edge, when a page wants one, is drawn by
                // whatever contains the list.
                listView.BorderStyle = BorderStyle.None;
                break;
        }

        // Enter and Leave, not GotFocus and LostFocus.
        //
        // A NumericUpDown is a container: the caret actually lives in a child
        // edit control, so the spinner's own handle never gains or loses
        // focus and those two events simply do not fire for it. The ring
        // would light up and then stay lit after you clicked away, because
        // nothing ever told it to repaint. Enter and Leave are the
        // container-aware pair - they fire when focus crosses into or out of
        // the control OR any of its children, which is the question being
        // asked here. Kept alongside the direct pair because a plain text box
        // raises both and a redundant repaint is free.
        control.Enter += (_, _) => Refresh(control);
        control.Leave += (_, _) => Refresh(control);
        control.GotFocus += (_, _) => Refresh(control);
        control.LostFocus += (_, _) => Refresh(control);
        control.EnabledChanged += (_, _) => Refresh(control);
        control.LocationChanged += (_, _) => Refresh(control);
        control.SizeChanged += (_, _) => Refresh(control);
        control.VisibleChanged += (_, _) => Refresh(control);
        control.ParentChanged += (_, _) => HookParent(control.Parent);

        HookParent(control.Parent);
    }

    private static void HookParent(Control? parent)
    {
        if (parent is null || HookedParents.TryGetValue(parent, out _))
        {
            return;
        }

        HookedParents.Add(parent, Marker);

        // Subscribed after whatever the parent already draws for itself - a
        // GroupBox paints its card here too, and the rings have to land on
        // top of that rather than underneath it.
        parent.Paint += ParentPaint;
        parent.Invalidate();
    }

    private static void Refresh(Control control)
    {
        var parent = control.Parent;

        if (parent is null || parent.IsDisposed || !parent.IsHandleCreated)
        {
            return;
        }

        var ring = control.Bounds;
        ring.Inflate(2, 2);
        parent.Invalidate(ring, false);
    }

    private static void ParentPaint(object? sender, PaintEventArgs e)
    {
        if (sender is not Control parent)
        {
            return;
        }

        foreach (Control child in parent.Controls)
        {
            if (!child.Visible || !Tracked.TryGetValue(child, out _) || Excluded.TryGetValue(child, out _))
            {
                continue;
            }

            var bounds = child.Bounds;

            if (bounds.Width < 2 || bounds.Height < 2)
            {
                continue;
            }

            // Taken from what the ring is being drawn ON, not from the
            // content palette. The sidebar is dark chrome in every mode, so
            // in Light Mode the content palette's near-white hairline put a
            // white box around the nav search field.
            var edge = child.Focused && child.Enabled
                ? AppTheme.Primary
                : AppTheme.EdgeOn(parent.BackColor);

            using var pen = new Pen(edge);

            // One pixel outside the control on every side. Inflate widens by
            // one each way, and DrawRectangle treats width as the distance
            // to the last painted pixel, hence the -1.
            bounds.Inflate(1, 1);
            e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }
    }
}

/// <summary>
/// Drops keyboard focus when a click lands on empty space.
///
/// Clicking a label, a card, or the blank part of a page does not move focus
/// in WinForms - the control you were last in keeps it. That is correct
/// behaviour and normally invisible, but as soon as focus is drawn as a
/// coloured ring it stops looking like "this is where typing goes" and
/// starts looking like a box that is stuck on. The field genuinely still had
/// focus; nothing had taken it away.
///
/// So background clicks now take it away. Only the backgrounds - clicking
/// another field still hands focus over the normal way, and this never fires
/// for a click that landed on a control.
/// </summary>
/// <summary>
/// Replaces a panel's FixedSingle border with one in the theme's colour.
///
/// BorderStyle.FixedSingle is drawn by Windows in a system colour, and on a
/// dark theme that colour is near-white - so every bordered panel in the app
/// was outlined in a bright box that belonged to no palette. There is no way
/// to recolour it: the style is drawn in the non-client area before any
/// managed paint runs. The only fix is to turn it off and draw the line.
/// </summary>
internal static class PanelOutline
{
    private static readonly ConditionalWeakTable<Control, object> Hooked = new();
    private static readonly object Marker = new();

    public static void Attach(Panel panel)
    {
        if (Hooked.TryGetValue(panel, out _))
        {
            return;
        }

        Hooked.Add(panel, Marker);

        // Off first, or Windows keeps drawing its own on top of ours.
        panel.BorderStyle = BorderStyle.None;

        panel.Paint += (_, e) =>
        {
            if (panel.ClientSize.Width < 2 || panel.ClientSize.Height < 2)
            {
                return;
            }

            using var pen = new Pen(AppTheme.Border);

            e.Graphics.DrawRectangle(
                pen, 0, 0, panel.ClientSize.Width - 1, panel.ClientSize.Height - 1);
        };

        // The outline is drawn on the VIEWPORT - the same four edges however
        // far the panel is scrolled - but a scrolling panel does not repaint
        // on scroll. See ScrollRepaint.
        ScrollRepaint.Attach(panel);

        panel.Invalidate();
    }
}

/// <summary>
/// Repaints a scrolling panel in full whenever its content moves.
///
/// Windows scrolls a panel by copying its pixels up or down (ScrollWindowEx)
/// and repainting only the strip that came into view. That is correct for
/// anything that moves WITH the content - the child controls, and anything
/// painted at a child's position. It is wrong for anything painted at a fixed
/// place in the viewport: the themed outline's bottom edge was copied upward
/// by one scroll step, a fresh one drawn at the bottom, copied up again, and
/// so on - a ladder of evenly spaced full-width hairlines behind every row
/// of the Announcements list. The same applies to a page header painted at
/// the top of a scrolling page.
///
/// So after any move the whole panel is invalidated. Only the panel's own
/// surface repaints - its children are separate windows and were copied
/// correctly - so this is a background fill and one rectangle per step.
/// </summary>
internal static class ScrollRepaint
{
    private const uint RDW_ALLCHILDREN = 0x0080;
    private const uint RDW_UPDATENOW = 0x0100;

    private static readonly ConditionalWeakTable<ScrollableControl, StrongBox<Point>> Hooked = new();

    public static void Attach(ScrollableControl control)
    {
        if (Hooked.TryGetValue(control, out _))
        {
            return;
        }

        var last = new StrongBox<Point>(control.AutoScrollPosition);
        Hooked.Add(control, last);

        // The panel's own surface - its background, the outline, the rings
        // round its fields - is drawn off-screen and put up in one go.
        // Without it, repainting the whole surface on every scroll step
        // showed as the outline and rings blinking out and back. Children
        // are separate windows and are not affected.
        AppTheme.EnableDoubleBuffering(control);

        // The scrollbar, the keyboard, and the themed bar's own messages all
        // arrive as WM_VSCROLL / WM_HSCROLL, which raise Scroll.
        control.Scroll += (_, _) => Repaint(control, last, now: true);

        // The wheel scrolls without raising Scroll.
        control.MouseWheel += (_, _) => Repaint(control, last, now: true);

        // The catch-all. ScrollControlIntoView (tabbing to a field below the
        // fold) and a direct AutoScrollPosition assignment move the content
        // with no event at all. Either one still repaints the strip it
        // exposed, so the first paint after a move notices the position
        // changed and asks for the rest. Not painted NOW - this is already
        // inside a paint.
        control.Paint += (_, _) => Repaint(control, last, now: false);

        // A Panel does not have ResizeRedraw either: growing it repaints only
        // the new sliver and leaves the old bottom and right edges behind.
        control.SizeChanged += (_, _) =>
        {
            if (!control.IsDisposed && control.IsHandleCreated)
            {
                control.Invalidate();
            }
        };
    }

    private static void Repaint(ScrollableControl control, StrongBox<Point> last, bool now)
    {
        if (control.IsDisposed || !control.IsHandleCreated)
        {
            return;
        }

        // Only when the content actually moved. The wheel and the scroll
        // event can both report the same step, and a paint with nothing
        // moved has nothing to fix.
        var position = control.AutoScrollPosition;

        if (position == last.Value)
        {
            return;
        }

        last.Value = position;
        control.Invalidate(false);

        if (!now)
        {
            return;
        }

        // Painted before the next step is taken, not whenever Windows gets
        // round to it.
        //
        // WM_PAINT is the lowest-priority message there is, so while the
        // wheel keeps turning, step after step is copied up the screen
        // before any of them is painted. That is what was still visible:
        // a brief ladder of outline edges before the paint caught up, and
        // rows that came into view blank - their fields "disappearing" -
        // until the scrolling stopped. UPDATENOW paints the panel and every
        // child with anything outstanding right here, so each step is
        // finished before the next one starts.
        RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, RDW_UPDATENOW | RDW_ALLCHILDREN);
    }

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);
}

/// <summary>
/// Column headings that stay put while a list scrolls under them.
///
/// The Announcements and Sound Effects lists had their headings INSIDE the
/// scrolling panel, as the first row, so they lined up with the columns
/// however far the list was scrolled sideways - and scrolled off the top
/// the moment the list moved down, leaving thirty rows of unlabelled
/// ticks and numbers.
///
/// Now the headings sit in a strip above the list, outside it, so they
/// never move vertically. Sideways they follow the list: the headings live
/// on a "track" inside the strip, and the track is kept at the list's
/// horizontal scroll offset. Strip and list are docked in one bordered
/// frame, so the pair still reads as a single box.
/// </summary>
internal static class StickyHeadings
{
    /// <summary>
    /// Puts <paramref name="rows"/> and the headings on <paramref name="track"/>
    /// in one frame and returns the frame, for the caller to dock where the
    /// list used to go. The track's height is the strip's height; its
    /// headings are placed at the same x as the columns in the list.
    /// </summary>
    public static Panel Wrap(Panel rows, Panel track)
    {
        // The frame draws the border (PanelOutline, through the theme pass),
        // and the one-pixel padding keeps the strip and the list off it.
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(1),
        };

        rows.Dock = DockStyle.Fill;
        rows.BorderStyle = BorderStyle.None;

        // Wide enough for every heading, however the strip is sized.
        var widest = track.Controls.Cast<Control>()
            .Select(c => c.Left + Math.Max(c.Width, c.PreferredSize.Width))
            .DefaultIfEmpty(0)
            .Max();

        track.Width = Math.Max(track.Width, widest + UiLayout.PageRight);
        track.Location = Point.Empty;

        // Both reach past their box on purpose - the track is as wide as
        // the rows and slides sideways with them, and the rows scroll
        // sideways - so the layout check is told not to report them.
        LayoutAudit.AllowOverflow(track);
        LayoutAudit.AllowOverflow(rows);

        // A rule under the headings, so they read as a header rather than
        // as a first row. Kept in the border colour through the theme pass.
        var rule = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = AppTheme.Border,
            Tag = AppTheme.KeepBackColor,
        };

        var strip = new Panel { Dock = DockStyle.Top, Height = track.Height + rule.Height };
        strip.Controls.Add(track);
        strip.Controls.Add(rule);

        // Fill first: docking is resolved from the last child backwards.
        frame.Controls.Add(rows);
        frame.Controls.Add(strip);

        void Follow()
        {
            if (rows.IsDisposed || track.IsDisposed)
            {
                return;
            }

            var x = rows.AutoScrollPosition.X;

            if (track.Left != x)
            {
                track.Left = x;
            }
        }

        // Every way the list can move sideways: the bars and the keyboard
        // (Scroll), the wheel, a resize that changes how far it can scroll
        // (Layout), and tabbing to a field off to the side, which scrolls
        // with no event at all - caught by the paint that move causes.
        rows.Scroll += (_, _) => Follow();
        rows.MouseWheel += (_, _) => Follow();
        rows.Layout += (_, _) => Follow();
        rows.Paint += (_, _) => Follow();

        return frame;
    }
}

/// <summary>
/// A text box drawn as the same rounded field a themed drop-down is: the
/// surface fill, a soft border, the accent border while typing, and the
/// text inset from the edge.
///
/// A plain text box can only ever be a rectangle - its edge is native, and
/// InputOutline's ring is square. Next to the rounded drop-downs that looked
/// like two different apps on one form. So the box goes inside a host that
/// paints the shape, with no frame of its own - the way the sidebar's page
/// search is built.
///
/// The caller places and sizes the HOST; the text box follows it.
/// </summary>
internal static class RoundedField
{
    private const int Radius = 7;
    private const int Inset = 12;

    public static Panel Wrap(TextBox box)
    {
        var host = new Panel { Cursor = Cursors.IBeam };
        AppTheme.EnableDoubleBuffering(host);

        // Its edge is the host's rounded one; the square ring would sit
        // inside it.
        InputOutline.Exclude(box);

        box.BorderStyle = BorderStyle.None;
        box.AutoSize = false;
        box.BackColor = AppTheme.Surface;
        box.ForeColor = AppTheme.TextDark;

        host.Controls.Add(box);

        void Fit()
        {
            if (host.IsDisposed)
            {
                return;
            }

            // Tall enough for the letters, descenders included - a
            // single-line box's own height stops short of the "g" and "p"
            // in this app's typeface. See MainForm.FitNavSearchHeight.
            var textHeight = Math.Max(box.PreferredHeight, TextRenderer.MeasureText("Hgyp", box.Font).Height);

            box.SetBounds(
                Inset,
                Math.Max(0, (host.ClientSize.Height - textHeight) / 2),
                Math.Max(20, host.ClientSize.Width - Inset * 2),
                textHeight);

            host.Invalidate();
        }

        host.Resize += (_, _) => Fit();
        box.FontChanged += (_, _) => Fit();

        box.Enter += (_, _) => host.Invalidate();
        box.Leave += (_, _) => host.Invalidate();
        box.EnabledChanged += (_, _) => host.Invalidate();

        // A click on the padding round the text goes into the text. MouseUp,
        // not MouseDown: the theme's FocusRelease clears focus on a panel's
        // MouseDown, and would undo this straight after.
        host.MouseUp += (_, _) => box.Focus();

        host.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // The corners show whatever is behind the field.
            g.Clear(host.Parent?.BackColor ?? AppTheme.Background);

            var enabled = box.Enabled;
            var focused = enabled && box.Focused;

            var bounds = new RectangleF(0.5f, 0.5f, host.Width - 1f, host.Height - 1f);
            var radius = Math.Max(1, Math.Min(Radius, host.Height / 2));

            using var path = AppTheme.RoundedRect(bounds, radius);
            using var fill = new SolidBrush(enabled ? AppTheme.Surface : AppTheme.Background);
            using var pen = new Pen(focused ? AppTheme.Primary : AppTheme.Border, focused ? 1.4f : 1f);

            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        };

        return host;
    }
}

/// <summary>
/// Dims a window while a dialog is open over it.
///
/// A window under a modal dialog is disabled, and a disabled button here
/// looks like an enabled one with its text greyed - which is also exactly
/// how a button INSIDE the dialog looks when it has nothing to act on. So
/// the dialog's own unavailable buttons and the whole window behind it read
/// the same, and it was hard to tell at a glance which buttons were the
/// dialog's.
///
/// Now the window behind goes properly dark: a see-through black sheet over
/// it, under the dialog, for as long as the dialog is up.
///
/// Hooked on WM_ENABLE rather than on any one way of opening a dialog.
/// Windows disables the window behind every modal - ShowDialog, the app's
/// own message boxes, the file pickers - and enables it again when it
/// closes, so that one message covers them all, nested ones included: a
/// dialog that opens a second dialog is dimmed behind it the same way.
/// </summary>
internal sealed class ModalScrim : NativeWindow
{
    private const int WM_ENABLE = 0x000A;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>
    /// How dark. Enough that the window behind reads as "behind", not so
    /// much it disappears. Was 0.5, then 0.62 - both left the buttons
    /// behind a dialog bright enough to still pass for the dialog's own.
    /// </summary>
    private const double Darkness = 0.72;

    private static readonly ConditionalWeakTable<Form, ModalScrim> Attached = new();

    private readonly Form _form;
    private Sheet? _sheet;

    public static void Attach(Form form)
    {
        if (form is Sheet || Attached.TryGetValue(form, out _))
        {
            return;
        }

        Attached.Add(form, new ModalScrim(form));
    }

    private ModalScrim(Form form)
    {
        _form = form;

        if (form.IsHandleCreated)
        {
            AssignHandle(form.Handle);
        }

        // A handle can be recreated (a look change, a re-parent); the hook
        // follows it.
        form.HandleCreated += (_, _) =>
        {
            if (Handle != form.Handle)
            {
                if (Handle != IntPtr.Zero)
                {
                    ReleaseHandle();
                }

                AssignHandle(form.Handle);
            }
        };

        form.Disposed += (_, _) =>
        {
            _sheet?.Dispose();
            _sheet = null;
        };
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WM_ENABLE)
        {
            return;
        }

        try
        {
            if (m.WParam == IntPtr.Zero)
            {
                ShowSheet();
            }
            else
            {
                HideSheet();
            }
        }
        catch
        {
            // A dimmer is cosmetic. Never worth a dialog failing to open.
        }
    }

    private void ShowSheet()
    {
        if (_form.IsDisposed || !_form.IsHandleCreated || !_form.Visible
            || _form.WindowState == FormWindowState.Minimized)
        {
            return;
        }

        var area = _form.RectangleToScreen(_form.ClientRectangle);

        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        _sheet ??= new Sheet(Darkness);
        _sheet.Bounds = area;

        if (!_sheet.Visible)
        {
            _sheet.Show(_form);
        }

        // Under the dialog, never over it. The dialog is often not on screen
        // yet at this point - Windows disables the window behind before it
        // shows the dialog - so this is done again once the dialog is up.
        PlaceUnderDialog();
        _form.BeginInvoke(new Action(PlaceUnderDialog));
    }

    private void HideSheet()
    {
        if (_sheet is { IsDisposed: false, Visible: true })
        {
            _sheet.Hide();
        }
    }

    /// <summary>Puts the sheet directly beneath whatever window is active - the dialog.</summary>
    private void PlaceUnderDialog()
    {
        if (_sheet is not { IsDisposed: false, Visible: true } sheet)
        {
            return;
        }

        var active = GetActiveWindow();

        if (active == IntPtr.Zero || active == sheet.Handle || active == _form.Handle)
        {
            return;
        }

        // SetWindowPos puts the window BELOW the one named second.
        SetWindowPos(sheet.Handle, active, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>
    /// The dark sheet: no frame, never takes focus, and clicks go straight
    /// through it to the disabled window underneath - which is what makes
    /// Windows flash the dialog, the usual answer to a click behind one.
    /// </summary>
    private sealed class Sheet : Form
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        public Sheet(double darkness)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Opacity = darkness;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}

internal static class FocusRelease
{
    private static readonly ConditionalWeakTable<Control, object> Hooked = new();
    private static readonly object Marker = new();

    public static void Attach(Control container)
    {
        if (Hooked.TryGetValue(container, out _))
        {
            return;
        }

        Hooked.Add(container, Marker);
        container.MouseDown += Release;
    }

    private static void Release(object? sender, MouseEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        var form = control.FindForm();

        if (form is null || form.ActiveControl is null)
        {
            return;
        }

        // Null is the documented way to deactivate. It raises Leave and
        // Validating on whatever was active, which is what commits a
        // half-typed number in a spinner - so this also stops a value being
        // abandoned unsaved when you click away.
        form.ActiveControl = null;
    }
}

/// <summary>
/// Redraws a NumericUpDown's spin buttons as two small chevrons on the
/// theme's surface, in place of the grey 3D arrows Windows draws.
///
/// The buttons are a child control of the spinner, not part of it, so the
/// parent-drawn outline above never reaches them - they stay bright grey
/// inside an otherwise themed field.
///
/// WM_PAINT is answered entirely here rather than painted over afterwards.
/// Letting the default handler run first and then covering it means the
/// native grey arrows are genuinely drawn to the screen for a frame on every
/// repaint - and the control repaints on mouse enter and leave to show its
/// hot state, so hovering the spinner made the arrows strobe. Composing off
/// screen and blitting once is the only way that stops.
/// </summary>
internal sealed class UpDownButtonsSkin : NativeWindow
{
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_CAPTURECHANGED = 0x0215;

    private static readonly ConditionalWeakTable<Control, UpDownButtonsSkin> Attached = new();

    private readonly Control _buttons;

    /// <summary>
    /// Which half is being held: 1 for up, -1 for down, 0 for neither.
    ///
    /// A spinner's arrows are drawn by us, so nothing was telling anyone a
    /// click had landed - the arrow looked identical held down as it did
    /// untouched, which on a control whose whole job is repeated clicking
    /// reads as an arrow that does not work.
    /// </summary>
    private int _pressed;

    private UpDownButtonsSkin(Control buttons)
    {
        _buttons = buttons;

        buttons.HandleCreated += (_, _) => Bind();
        buttons.VisibleChanged += (_, _) => Bind();
        buttons.HandleDestroyed += (_, _) => ReleaseHandle();
        buttons.Disposed += (_, _) => ReleaseHandle();

        Bind();
    }

    public static void Attach(NumericUpDown spinner)
    {
        if (spinner.Controls.Count == 0)
        {
            return;
        }

        // Controls[0] is the UpDownButtons instance. The type is internal, so
        // it can only be reached positionally.
        var buttons = spinner.Controls[0];

        if (Attached.TryGetValue(buttons, out _))
        {
            return;
        }

        Attached.Add(buttons, new UpDownButtonsSkin(buttons));
    }

    private void Bind()
    {
        if (!_buttons.IsHandleCreated || _buttons.IsDisposed || Handle == _buttons.Handle)
        {
            return;
        }

        if (Handle != IntPtr.Zero)
        {
            ReleaseHandle();
        }

        AssignHandle(_buttons.Handle);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_ERASEBKGND when !_buttons.IsDisposed:
                m.Result = (IntPtr)1;
                return;

            case WM_PAINT when !_buttons.IsDisposed && _buttons.IsHandleCreated:
                // Claimed only when the paint really happened. Returning with
                // the update region still dirty makes Windows re-post
                // WM_PAINT immediately, which is a spin, not a glitch.
                if (OnPaint())
                {
                    m.Result = IntPtr.Zero;
                    return;
                }

                break;

            // These three note the press and then fall THROUGH to the real
            // handler below. Swallowing them would leave a spinner that
            // lights up and never counts.
            case WM_LBUTTONDOWN when !_buttons.IsDisposed:
                SetPressed(HalfAt(m.LParam));
                break;

            case WM_LBUTTONUP when !_buttons.IsDisposed:
            case WM_CAPTURECHANGED when !_buttons.IsDisposed:
                SetPressed(0);
                break;
        }

        base.WndProc(ref m);
    }

    /// <summary>Which half of the control a mouse message landed in.</summary>
    private int HalfAt(IntPtr lParam)
    {
        // The y is the high word of lParam, and it is signed - a drag above
        // the control reports a negative y, which must not read as "top".
        var y = unchecked((short)((long)lParam >> 16));

        return y < _buttons.Height / 2 ? 1 : -1;
    }

    private void SetPressed(int half)
    {
        if (_pressed == half)
        {
            return;
        }

        _pressed = half;

        if (!_buttons.IsDisposed && _buttons.IsHandleCreated)
        {
            _buttons.Invalidate();
        }
    }

    private bool OnPaint()
    {
        var ps = new PAINTSTRUCT();
        var hdc = BeginPaint(Handle, ref ps);

        if (hdc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var width = Math.Max(1, _buttons.Width);
            var height = Math.Max(1, _buttons.Height);

            using var buffer = new Bitmap(width, height);
            using (var g = Graphics.FromImage(buffer))
            {
                Draw(g, width, height);
            }

            using var target = Graphics.FromHdc(hdc);
            target.DrawImageUnscaled(buffer, 0, 0);
        }
        finally
        {
            EndPaint(Handle, ref ps);
        }

        return true;
    }

    private void Draw(Graphics g, int width, int height)
    {
        if (width < 4 || height < 6)
        {
            return;
        }

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillRectangle(back, 0, 0, width, height);
        }

        // The half being held gets a wash of the accent behind it, so a
        // click is visible for as long as it is held - including through an
        // auto-repeat, where the arrow is doing something once every few
        // hundred milliseconds and used to look completely inert.
        if (_pressed != 0)
        {
            var half = _pressed > 0
                ? new RectangleF(0, 0, width, height / 2f)
                : new RectangleF(0, height / 2f, width, height / 2f);

            using var press = new SolidBrush(Color.FromArgb(70, AppTheme.Primary));
            g.FillRectangle(press, half);
        }

        using var pen = new Pen(AppTheme.TextMuted, 1.4f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };

        // The held arrow also brightens and thickens. The wash alone is easy
        // to miss on a control this small.
        using var held = new Pen(AppTheme.Primary, 1.9f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };

        var cx = width / 2f;
        var arm = Math.Min(3.5f, width / 3f);

        var upY = height / 4f;
        g.DrawLines(_pressed > 0 ? held : pen, new[]
        {
            new PointF(cx - arm, upY + arm / 1.6f),
            new PointF(cx, upY - arm / 1.6f),
            new PointF(cx + arm, upY + arm / 1.6f),
        });

        var downY = height * 3f / 4f;
        g.DrawLines(_pressed < 0 ? held : pen, new[]
        {
            new PointF(cx - arm, downY - arm / 1.6f),
            new PointF(cx, downY + arm / 1.6f),
            new PointF(cx + arm, downY - arm / 1.6f),
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;

        public int reserved0;
        public int reserved1;
        public int reserved2;
        public int reserved3;
        public int reserved4;
        public int reserved5;
        public int reserved6;
        public int reserved7;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT paint);
}

/// <summary>
/// Draws a ListView's column header.
///
/// This started as a patch for the strip past the last column, on the
/// assumption that ListView.DrawColumnHeader was handling the columns
/// themselves. It is not: with OwnerDraw on and the rows drawing correctly
/// through DrawSubItem, the header event never fires at all on these lists,
/// and the header came up in the system's white with black text - the one
/// unthemed thing left on the page.
///
/// Chasing WHY the event goes missing is not worth it when the header is
/// reachable directly. It is a separate child window; ask the ListView for
/// its handle and it can be drawn like any other window, which is what this
/// now does. The whole header is painted here - background, every column's
/// caption, and the leftover strip - so WM_PAINT is claimed outright rather
/// than being let through first and covered. Nothing draws twice, so there
/// is nothing to flicker when the mouse crosses a column.
/// </summary>
internal sealed class ListViewHeaderSkin : NativeWindow
{
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;

    /// <summary>"Draw yourself into this DC": what a window is sent when it is printed rather than painted - Control.DrawToBitmap, and so every layout picture.</summary>
    private const int WM_PRINTCLIENT = 0x0318;

    private const int LVM_FIRST = 0x1000;
    private const int LVM_GETHEADER = LVM_FIRST + 31;
    private const int HDM_FIRST = 0x1200;

    /// <summary>Asks the header where a given column sits, in the header's own coordinates - so a horizontally scrolled list still lines up.</summary>
    private const int HDM_GETITEMRECT = HDM_FIRST + 7;

    /// <summary>How far in from the left of its column a caption is drawn.</summary>
    private const int CaptionLeft = 8;

    /// <summary>The room a caption keeps from the right of its column.</summary>
    private const int CaptionRight = 4;

    /// <summary>How a caption is laid out - measured with the same flags it is drawn with, so a fit is a fit.</summary>
    private const TextFormatFlags CaptionFlags = TextFormatFlags.Left | TextFormatFlags.SingleLine;

    private static readonly ConditionalWeakTable<ListView, ListViewHeaderSkin> Attached = new();

    /// <summary>
    /// The narrowest a column can be and still show its whole caption: the
    /// caption in plain capitals - as Draw falls back to when the spaced
    /// ones do not fit - with the inset it is drawn at and a little room.
    /// In the header face in use, so it follows the text size. What
    /// Pieces.FitListColumns and the Driver Profiles roster hold a list's
    /// columns to.
    ///
    /// It used to be measured by each of them in the bold or the body face,
    /// in mixed case - the look of a header this class replaced, and not
    /// the width of the one it draws.
    /// </summary>
    internal static int LeastWidth(string heading) =>
        TextRenderer.MeasureText((heading ?? string.Empty).ToUpperInvariant(), AppTheme.MicroFont, new Size(int.MaxValue, int.MaxValue), CaptionFlags).Width
        + CaptionLeft + CaptionRight + 2;

    private readonly ListView _listView;

    private ListViewHeaderSkin(ListView listView)
    {
        _listView = listView;

        listView.HandleCreated += (_, _) => Bind();
        listView.VisibleChanged += (_, _) => Bind();
        listView.ParentChanged += (_, _) => Bind();
        listView.HandleDestroyed += (_, _) => ReleaseHandle();
        listView.Disposed += (_, _) => ReleaseHandle();

        Bind();
    }

    public static void Attach(ListView listView)
    {
        if (Attached.TryGetValue(listView, out _))
        {
            return;
        }

        Attached.Add(listView, new ListViewHeaderSkin(listView));
    }

    private void Bind()
    {
        if (!_listView.IsHandleCreated || _listView.IsDisposed)
        {
            return;
        }

        var header = SendMessage(_listView.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);

        if (header == IntPtr.Zero || header == Handle)
        {
            return;
        }

        if (Handle != IntPtr.Zero)
        {
            ReleaseHandle();
        }

        AssignHandle(header);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_ERASEBKGND:
                // Claimed and nothing drawn: the paint below covers every
                // pixel of the header, and letting it erase first is a
                // white flash for the length of one frame.
                m.Result = (IntPtr)1;
                return;

            case WM_PAINT when !_listView.IsDisposed:
                Paint();
                return;

            // Drawn into a DC it is handed rather than onto the screen -
            // DrawToBitmap, and so every layout picture. Only WM_PAINT used
            // to be claimed, so a printed header was the system's own white
            // bar with mixed-case captions: the pictures showed a header the
            // screen never does (29 Sep, Track Layouts and Race History).
            case WM_PRINTCLIENT when !_listView.IsDisposed && m.WParam != IntPtr.Zero:
                try
                {
                    using var g = Graphics.FromHdc(m.WParam);
                    Draw(g);
                }
                catch
                {
                    // As in Paint: a window going away mid-print is not
                    // worth taking the app down for.
                }

                m.Result = IntPtr.Zero;
                return;
        }

        base.WndProc(ref m);
    }

    private void Paint()
    {
        var paint = new PAINTSTRUCT();
        var hdc = BeginPaint(Handle, ref paint);

        if (hdc == IntPtr.Zero)
        {
            return;
        }

        try
        {
            using var g = Graphics.FromHdc(hdc);
            Draw(g);
        }
        catch
        {
            // A window going away mid-paint is not worth taking the app
            // down for; the worst case is one frame of the wrong colour.
        }
        finally
        {
            EndPaint(Handle, ref paint);
        }
    }

    /// <summary>
    /// The whole header - background, every column's caption, and the
    /// leftover strip - into whatever it is handed: the screen through
    /// WM_PAINT, or a picture through WM_PRINTCLIENT.
    /// </summary>
    private void Draw(Graphics g)
    {
        // The quiet header, exactly as the grids draw theirs (see
        // AppTheme.QuietHeaderPaint): surface, spaced caps in the
        // accent ink, one hairline underneath. This used to be a solid
        // Primary banner with white bold text - the loudest band on
        // the page, sitting next to grids that had already gone quiet,
        // so every ListView looked like it came from a different app.
        var bounds = GetClientBounds();

        using var back = new SolidBrush(AppTheme.Surface);

        g.FillRectangle(back, bounds);

        using (var rule = new Pen(AppTheme.Border))
        {
            g.DrawLine(rule, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        }

        // A header exists in Details view only; in any other view the
        // window is there but has nothing to say.
        if (_listView.View != View.Details)
        {
            return;
        }

        for (var i = 0; i < _listView.Columns.Count; i++)
        {
            var rect = new RECT();

            if (SendMessage(Handle, HDM_GETITEMRECT, (IntPtr)i, ref rect) == IntPtr.Zero)
            {
                continue;
            }

            var cell = new Rectangle(
                rect.Left,
                rect.Top,
                Math.Max(0, rect.Right - rect.Left),
                Math.Max(0, rect.Bottom - rect.Top));

            if (cell.Width <= 1 || cell.Height <= 1)
            {
                continue;
            }

            var textRect = new Rectangle(cell.X + CaptionLeft, cell.Y, cell.Width - CaptionLeft - CaptionRight, cell.Height);

            // Spacing only on short captions, same rule as the grids -
            // thin spaces push a long one past its column. And only while
            // the spaced one fits, as the grids have it too: at a larger
            // text size, or in a narrow column, the thin spaces were what
            // cut a heading short when the plain capitals had room.
            var label = _listView.Columns[i].Text.ToUpperInvariant();

            if (label.Length <= 10)
            {
                var spaced = AppTheme.Spaced(label);

                if (TextRenderer.MeasureText(g, spaced, AppTheme.MicroFont, new Size(int.MaxValue, int.MaxValue), CaptionFlags).Width <= textRect.Width)
                {
                    label = spaced;
                }
            }

            TextRenderer.DrawText(
                g, label, AppTheme.MicroFont, textRect, AppTheme.Primary,
                CaptionFlags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private Rectangle GetClientBounds()
    {
        var rect = new RECT();
        GetClientRect(Handle, ref rect);

        return new Rectangle(rect.Left, rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>The HDM_GETITEMRECT form, which writes a rectangle through lParam rather than returning a value.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, ref RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;

        public int reserved0;
        public int reserved1;
        public int reserved2;
        public int reserved3;
        public int reserved4;
        public int reserved5;
        public int reserved6;
        public int reserved7;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT paint);
}

/// <summary>
/// Replaces a TrackBar's painting entirely: a thin rounded track, the filled
/// portion in the accent, and a rounded thumb.
///
/// TrackBar has no owner-draw mode at all - no DrawMode, no DrawItem - so
/// there is no supported way to restyle one. Taking WM_PAINT is the whole
/// mechanism. Only the pixels change: hit-testing, dragging, keyboard and
/// mouse wheel are all still the native control's, so the sliders behave
/// exactly as they did.
/// </summary>
internal sealed class TrackBarSkin : NativeWindow
{
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;

    private static readonly ConditionalWeakTable<TrackBar, TrackBarSkin> Attached = new();

    private readonly TrackBar _bar;

    private TrackBarSkin(TrackBar bar)
    {
        _bar = bar;

        bar.ValueChanged += (_, _) => Repaint();
        bar.EnabledChanged += (_, _) => Repaint();
        bar.Resize += (_, _) => Repaint();
        bar.LocationChanged += (_, _) => Repaint();
        bar.HandleCreated += (_, _) => Bind();
        bar.VisibleChanged += (_, _) => Repaint();
        bar.ParentChanged += (_, _) => Bind();
        bar.HandleDestroyed += (_, _) => ReleaseHandle();
        bar.Disposed += (_, _) => ReleaseHandle();

        Bind();
    }

    public static void Attach(TrackBar bar)
    {
        if (Attached.TryGetValue(bar, out _))
        {
            return;
        }

        // Ticks are the one piece that cannot be redrawn convincingly at
        // this size, and the native ones are grey pips that would show
        // through the new track. Turning them off is part of the look.
        bar.TickStyle = TickStyle.None;

        Attached.Add(bar, new TrackBarSkin(bar));
    }

    private void Bind()
    {
        if (!_bar.IsHandleCreated || _bar.IsDisposed || Handle == _bar.Handle)
        {
            return;
        }

        if (Handle != IntPtr.Zero)
        {
            ReleaseHandle();
        }

        AssignHandle(_bar.Handle);
    }

    private void Repaint()
    {
        if (_bar.IsDisposed || !_bar.IsHandleCreated)
        {
            return;
        }

        Bind();
        _bar.Invalidate();

        // And the sliver of parent just outside it - see SkinGhost.
        SkinGhost.ClearAround(_bar);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_ERASEBKGND when !_bar.IsDisposed:
                m.Result = (IntPtr)1;
                return;

            case WM_PAINT when !_bar.IsDisposed && _bar.IsHandleCreated:
                // Claim the message only if the paint really happened -
                // returning with the update region still dirty makes Windows
                // re-post WM_PAINT forever.
                if (OnPaint())
                {
                    m.Result = IntPtr.Zero;
                    return;
                }

                break;
        }

        base.WndProc(ref m);
    }

    private bool OnPaint()
    {
        // Widen the dirty area to the whole control before taking the paint.
        //
        // BeginPaint hands back a device context CLIPPED to whatever
        // Windows thinks needs repainting, and while a thumb is being
        // dragged the native trackbar only marks the little rectangle
        // around the thumb. The full-width image drawn below was therefore
        // arriving clipped to that sliver: the new thumb appeared, the old
        // one was never painted over, and a slow drag left a comb of
        // thumbs strung out along the track.
        //
        // Invalidating here rather than on ValueChanged because the ghost
        // trail also appears while dragging WITHIN a value - the thumb
        // moves smoothly, the value does not. Safe inside WM_PAINT: the
        // BeginPaint immediately below clears the update region again, so
        // this cannot loop.
        InvalidateRect(Handle, IntPtr.Zero, false);

        var ps = new PAINTSTRUCT();
        var hdc = BeginPaint(Handle, ref ps);

        if (hdc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var width = Math.Max(1, _bar.Width);
            var height = Math.Max(1, _bar.Height);

            using var buffer = new Bitmap(width, height);
            using (var g = Graphics.FromImage(buffer))
            {
                Draw(g, width, height);
            }

            using var target = Graphics.FromHdc(hdc);
            target.DrawImageUnscaled(buffer, 0, 0);
        }
        finally
        {
            EndPaint(Handle, ref ps);
        }

        return true;
    }

    private void Draw(Graphics g, int width, int height)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var canvas = new SolidBrush(_bar.Parent?.BackColor ?? AppTheme.Background))
        {
            g.FillRectangle(canvas, 0, 0, width, height);
        }

        var span = Math.Max(1, _bar.Maximum - _bar.Minimum);
        var fraction = Math.Clamp((_bar.Value - _bar.Minimum) / (float)span, 0f, 1f);

        var accent = _bar.Enabled ? AppTheme.Primary : AppTheme.TextMuted;

        const float trackThickness = 5f;
        const float thumbLong = 18f;
        const float thumbShort = 12f;

        if (_bar.Orientation == Orientation.Vertical)
        {
            // The thumb is drawn where the CONTROL actually has it.
            //
            // WinForms puts a vertical TrackBar's Minimum at the TOP and
            // its Maximum at the bottom. This used to draw the opposite way
            // round - value growing upward, which is how a fader ought to
            // read - and the result was a picture that disagreed with the
            // control underneath it. The thumb you could see was never
            // where the thumb you could grab was, so precise dragging was
            // impossible and clicking either side of the visible handle
            // moved the value the wrong way.
            //
            // Drawing follows the control here, and the one page that wants
            // a fader (the Mixer) inverts its own value instead - see
            // MixerControl. That keeps the two in agreement, which is the
            // only thing that makes a skinned control usable.
            var x = width / 2f - trackThickness / 2f;
            var top = thumbLong / 2f;
            var bottom = height - thumbLong / 2f;
            var travel = Math.Max(1f, bottom - top);
            var thumbCentre = top + travel * fraction;

            DrawCapsule(g, new RectangleF(x, top, trackThickness, travel), AppTheme.Border);
            DrawCapsule(g, new RectangleF(x, thumbCentre, trackThickness, bottom - thumbCentre), accent);
            DrawCapsule(g, new RectangleF(width / 2f - thumbShort / 2f, thumbCentre - thumbLong / 2f, thumbShort, thumbLong), accent);
        }
        else
        {
            var y = height / 2f - trackThickness / 2f;
            var left = thumbLong / 2f;
            var right = width - thumbLong / 2f;
            var travel = Math.Max(1f, right - left);
            var thumbCentre = left + travel * fraction;

            DrawCapsule(g, new RectangleF(left, y, travel, trackThickness), AppTheme.Border);
            DrawCapsule(g, new RectangleF(left, y, thumbCentre - left, trackThickness), accent);
            DrawCapsule(g, new RectangleF(thumbCentre - thumbShort / 2f, height / 2f - thumbLong / 2f, thumbShort, thumbLong), accent);
        }
    }

    private static void DrawCapsule(Graphics g, RectangleF bounds, Color color)
    {
        if (bounds.Width < 0.5f || bounds.Height < 0.5f)
        {
            return;
        }

        var radius = (int)(Math.Min(bounds.Width, bounds.Height) / 2f);

        using var path = AppTheme.RoundedRect(bounds, radius);
        using var brush = new SolidBrush(color);

        g.FillPath(brush, path);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;

        public int reserved0;
        public int reserved1;
        public int reserved2;
        public int reserved3;
        public int reserved4;
        public int reserved5;
        public int reserved6;
        public int reserved7;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT paint);

    /// <summary>Marks a region as needing repaint. A null rect means the whole window.</summary>
    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
}

/// <summary>
/// Paints the empty part of a tab control's tab row.
///
/// An owner-drawn TabControl only hands you the tabs. DrawItem fires once
/// per tab with that tab's own rectangle, and the strip of the row BEYOND
/// the last tab is never offered - the native control fills it with the
/// system colour, which on a default Windows theme is white. On a dark page
/// that is a bright band running off to the right of the last tab, which is
/// exactly what it looked like.
///
/// This is a painting-only skin. It does not hit-test anything and does not
/// touch mouse messages, so it cannot get into the argument a skinned
/// trackbar got into - see FaderControl for that story.
/// </summary>
internal sealed class TabStripSkin : NativeWindow
{
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;

    private static readonly ConditionalWeakTable<TabControl, TabStripSkin> Attached = new();

    private readonly TabControl _tabs;

    /// <summary>Which tab the pointer is over, or -1. See SetHot.</summary>
    private int _hot = -1;

    private TabStripSkin(TabControl tabs)
    {
        _tabs = tabs;

        tabs.HandleCreated += (_, _) => Bind();
        tabs.HandleDestroyed += (_, _) => ReleaseHandle();
        tabs.Disposed += (_, _) => ReleaseHandle();
        tabs.Resize += (_, _) => tabs.Invalidate();

        // Hover is worth two handlers here. With no box drawn around a tab,
        // the only thing left to say "this one is clickable" is the caption
        // brightening under the pointer.
        tabs.MouseMove += (_, e) => SetHot(TabAt(e.Location));
        tabs.MouseLeave += (_, _) => SetHot(-1);

        // The control repaints itself when the selection changes, which
        // would be enough on its own - but only just, and the marker bar is
        // ours and sits outside whatever region it decides to invalidate.
        tabs.SelectedIndexChanged += (_, _) => InvalidateRow();

        Bind();
    }

    /// <summary>The bottom of the tab row - everything above this line is this skin's.</summary>
    private int RowHeight => _tabs.TabCount > 0
        ? _tabs.GetTabRect(0).Bottom
        : _tabs.ItemSize.Height;

    private int TabAt(Point point)
    {
        for (var i = 0; i < _tabs.TabCount; i++)
        {
            if (_tabs.GetTabRect(i).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private void SetHot(int index)
    {
        if (_hot == index)
        {
            return;
        }

        _hot = index;
        InvalidateRow();
    }

    /// <summary>
    /// The row only. Invalidating the whole control would repaint the page
    /// inside it as well, on every pixel of mouse movement across the tabs.
    /// </summary>
    private void InvalidateRow()
    {
        if (_tabs.IsDisposed || !_tabs.IsHandleCreated)
        {
            return;
        }

        _tabs.Invalidate(new Rectangle(0, 0, Math.Max(1, _tabs.Width), Math.Max(1, RowHeight)));
    }

    public static void Attach(TabControl tabs)
    {
        if (Attached.TryGetValue(tabs, out _))
        {
            return;
        }

        Attached.Add(tabs, new TabStripSkin(tabs));
    }

    private void Bind()
    {
        if (!_tabs.IsHandleCreated || _tabs.IsDisposed || Handle == _tabs.Handle)
        {
            return;
        }

        if (Handle != IntPtr.Zero)
        {
            ReleaseHandle();
        }

        AssignHandle(_tabs.Handle);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_ERASEBKGND when !_tabs.IsDisposed:
                // Claimed, so the control never gets to lay down its system
                // colour in the first place.
                m.Result = (IntPtr)1;
                return;

            case WM_PAINT when !_tabs.IsDisposed && _tabs.IsHandleCreated:
                // Taken over outright. The base implementation is NOT
                // called.
                //
                // It used to be: the control painted its native row and
                // this covered it afterwards. That works when a repaint is
                // a rare event, and it is exactly wrong when it is not - a
                // tab control hot-tracks, so it invalidates a tab every
                // time the pointer crosses into or out of one, and every
                // one of those repaints put the native light-grey chrome on
                // the screen for a frame before this covered it again.
                // Running the mouse along the row made the whole strip
                // strobe.
                //
                // There is no way to talk the control out of hot-tracking,
                // and no way to make two blits look like one. So the
                // control does not get to paint at all: the row is composed
                // here and reaches the screen once, in one piece, and there
                // is never an intermediate state to see.
                if (OnPaint())
                {
                    m.Result = IntPtr.Zero;
                    return;
                }

                break;
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// The control's whole client area, minus the page - which is a child
    /// window and paints itself, and which WS_CLIPCHILDREN keeps out of
    /// this device context anyway.
    /// </summary>
    private bool OnPaint()
    {
        var ps = new PAINTSTRUCT();
        var hdc = BeginPaint(Handle, ref ps);

        if (hdc == IntPtr.Zero)
        {
            // Nothing was painted and nothing was validated, so hand the
            // message back rather than leaving the update region dirty -
            // WM_PAINT would just be posted again, forever.
            return false;
        }

        try
        {
            using var g = Graphics.FromHdc(hdc);

            PaintRow(g, RowHeight);
            PaintFrame(g);
        }
        catch
        {
            // A window going away mid-paint is not worth taking the app
            // down for; the worst case is one frame of the old colour.
        }
        finally
        {
            EndPaint(Handle, ref ps);
        }

        return true;
    }

    private void PaintFrame(Graphics g)
    {
        try
        {
            using var brush = new SolidBrush(AppTheme.Background);

            var rowBottom = RowHeight;

            // --- The pane frame ---
            //
            // A tab control also draws a raised border around the page
            // area - the themed TAB_PANE part - and that is not offered to
            // DrawItem either. Under visual styles it comes out light grey,
            // so every tabbed page in the app sat inside a white box.
            //
            // There is no property for it. What there is, is the fact that
            // it lives entirely in the band between the control's client
            // rectangle and its DisplayRectangle - the strip of the control
            // that the pages deliberately do NOT reach. Nothing else draws
            // there, so painting the band out removes the frame outright
            // rather than recolouring it, and because the colour is the
            // page's own there is no edge left behind.
            //
            // Read from DisplayRectangle rather than from the usual 4px
            // inset, because that inset is the theme's to change and is not
            // the same on every Windows version.
            var page = _tabs.DisplayRectangle;

            if (page.Width > 0 && page.Height > 0)
            {
                // Between the bottom of the tab row and the top of the page.
                if (page.Top > rowBottom)
                {
                    g.FillRectangle(brush, 0, rowBottom, _tabs.Width, page.Top - rowBottom);
                }

                if (page.Left > 0)
                {
                    g.FillRectangle(brush, 0, page.Top, page.Left, _tabs.Height - page.Top);
                }

                if (page.Right < _tabs.Width)
                {
                    g.FillRectangle(brush, page.Right, page.Top, _tabs.Width - page.Right, _tabs.Height - page.Top);
                }

                if (page.Bottom < _tabs.Height)
                {
                    g.FillRectangle(brush, 0, page.Bottom, _tabs.Width, _tabs.Height - page.Bottom);
                }
            }
        }
        catch
        {
            // A window going away mid-paint is not worth taking the app
            // down for; the worst case is one frame of the old colour.
        }
    }

    /// <summary>
    /// The tab row, composed from scratch and blitted over the control's
    /// own attempt at it.
    ///
    /// The tabs were filled blocks before this - a solid accent one for the
    /// page you were on, dark ones either side - and between every pair sat
    /// the native control's own edge, drawn from the system colours. That
    /// edge is the light vertical line in the middle of each gap and the
    /// pale outline around the selected tab, and no amount of owner-drawing
    /// reaches it: DrawItem is handed the tab's interior and nothing else.
    ///
    /// So the row is replaced rather than decorated, and while that was
    /// happening it was worth making it look like the rest of the app
    /// rather than like five buttons in a line: a hairline under the whole
    /// row, the page you are on named in the accent with a bar under it,
    /// everything else quiet until you point at it.
    /// </summary>
    private void PaintRow(Graphics target, int rowBottom)
    {
        var width = Math.Max(1, _tabs.Width);
        var height = Math.Max(1, rowBottom);

        // Composed off-screen: the rule, five captions and a marker drawn
        // straight onto the device context would show as separate states on
        // a slow repaint, and this runs on every mouse move across the row.
        using var buffer = new Bitmap(width, height);

        using (var g = Graphics.FromImage(buffer))
        {
            // Clear, NOT FillRectangle.
            //
            // A new bitmap starts fully transparent, and FillRectangle
            // composites SourceOver, which does not force the outermost
            // pixels opaque - so the blit BLENDS with what the control
            // already drew there instead of replacing it, and the native
            // edge shows faintly through. That is precisely the bug that
            // put a pale line on every dropdown in the app, and it is the
            // same bitmap-to-DC blit here. Graphics.Clear writes opaque
            // pixels unconditionally.
            g.Clear(AppTheme.Background);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // One rule under the whole row, past the last tab and all the
            // way to the edge. It is what makes the row read as a single
            // band rather than as a handful of controls that happen to be
            // in a line, and it echoes the hairline under the page's name
            // directly above it.
            using (var rule = new Pen(AppTheme.Border))
            {
                g.DrawLine(rule, 0, height - 1, width, height - 1);
            }

            for (var i = 0; i < _tabs.TabCount; i++)
            {
                var slot = _tabs.GetTabRect(i);
                var selected = i == _tabs.SelectedIndex;
                var hot = !selected && i == _hot;

                var ink = selected ? AppTheme.Primary
                    : hot ? AppTheme.TextDark
                    : AppTheme.TextMuted;

                var text = _tabs.TabPages[i].Text;

                // Vertically measured against the ROW, not against the
                // tab's own rectangle: a tab control insets the selected
                // tab by a couple of pixels so that it looks raised, and
                // following that would make the caption hop as you click
                // along the row.
                var caption = new Rectangle(slot.Left, 0, slot.Width, height - 3);

                TextRenderer.DrawText(g, text, _tabs.Font, caption, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                if (!selected && !hot)
                {
                    continue;
                }

                // The marker is sized to the WORD, not to the tab.
                //
                // The tabs in a row are all one width - the widest caption's,
                // and never under 150px (AppTheme.StyleTabControl) - so a bar
                // cut to the tab would be the same width under "Setup" as
                // under "Who's Racing" and would read as a block rather than
                // as an underline. Measured with NoPadding,
                // because the default adds a few pixels of its own on each
                // side and the bar is supposed to sit under the letters.
                var ideal = TextRenderer.MeasureText(g, text, _tabs.Font,
                    new Size(slot.Width, height),
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;

                var barWidth = Math.Max(8, Math.Min(slot.Width - 8, ideal + 10));

                // Sits ON the rule rather than above it, so the two never
                // argue over the same row of pixels - the bar simply
                // replaces that stretch of the hairline.
                var thickness = selected ? 3 : 2;

                var bar = new Rectangle(
                    slot.Left + (slot.Width - barWidth) / 2,
                    height - thickness,
                    barWidth,
                    thickness);

                using var marker = new SolidBrush(selected ? AppTheme.Primary : AppTheme.TextMuted);
                g.FillRectangle(marker, bar);
            }
        }

        target.DrawImageUnscaled(buffer, 0, 0);
    }

    // Own copies, like every other skin in this file has: they are private
    // to the class that paints with them, and the alternative is a shared
    // interop surface that every one of them would then depend on.
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;

        public int reserved0;
        public int reserved1;
        public int reserved2;
        public int reserved3;
        public int reserved4;
        public int reserved5;
        public int reserved6;
        public int reserved7;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT paint);
}
