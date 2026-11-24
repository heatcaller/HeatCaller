using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// Repaints a ComboBox's closed field from scratch: rounded surface, a
/// hairline border that lights up on focus, and a drawn chevron instead of
/// the grey Windows arrow button.
///
/// Why this and not FlatStyle.Flat: Flat removes the 3D frame but the
/// native control still repaints its own border on every mouse enter and
/// leave, and nothing at the WinForms level stops it - buffering the parent
/// does not help, because the flicker is the combo redrawing itself, not the
/// container erasing behind it. That was tried twice and reverted twice.
///
/// Why this and not a new control: there are several dozen ComboBoxes across
/// the app, all bound to Items/SelectedItem/SelectedIndexChanged and some to
/// DataSource. Swapping the type would mean touching every one of those call
/// sites and re-testing every page. Attaching a NativeWindow to the existing
/// handle changes only the pixels - the control, its API, its events and its
/// data binding are all untouched.
///
/// The flicker is gone because WM_PAINT is answered entirely here: the base
/// implementation never runs, so there is no native frame being drawn and
/// then covered over. WM_ERASEBKGND is swallowed for the same reason, and
/// the frame is composed in a bitmap and blitted once.
///
/// An editable combo's text is left to its real child edit window, which
/// has a caret and a selection - painting over it would leave you typing
/// into something you cannot see. The skin draws the field around it and
/// tells the edit which colours to paint itself in (EditColours).
/// </summary>
internal sealed class ComboBoxSkin : NativeWindow
{
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_PRINT = 0x0317;
    private const int WM_PRINTCLIENT = 0x0318;
    private const int WM_CTLCOLORSTATIC = 0x0138;

    /// <summary>
    /// Keyed weakly so a skin never keeps a disposed page's combo alive, and
    /// so restyling on a theme change finds the existing skin instead of
    /// stacking a second one on the same handle.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, ComboBoxSkin> Attached = new();

    private readonly ComboBox _combo;
    private bool _hot;
    private bool _open;

    /// <summary>Where a left press landed on a closed editable combo - see ArmOpen.</summary>
    private Point? _pressedAt;

    private ComboBoxSkin(ComboBox combo)
    {
        _combo = combo;

        combo.MouseEnter += (_, _) => SetHot(true);
        combo.MouseLeave += (_, _) => SetHot(false);
        combo.DropDown += (_, _) => { _open = true; Repaint(); };
        combo.DropDownClosed += (_, _) => { _open = false; Repaint(); };
        // An editable combo keeps its caret in a child edit window, so the
        // combo's own handle never gains or loses focus - the same trap the
        // spinners fall into. Enter and Leave cross container boundaries.
        combo.Enter += (_, _) => Repaint();
        combo.Leave += (_, _) => Repaint();
        combo.GotFocus += (_, _) => Repaint();
        combo.LostFocus += (_, _) => Repaint();
        combo.SelectedIndexChanged += (_, _) => Repaint();
        combo.TextChanged += (_, _) => Repaint();
        combo.EnabledChanged += (_, _) => Repaint();
        combo.Resize += (_, _) => Repaint();
        combo.LocationChanged += (_, _) => Repaint();

        // A click anywhere on the field opens the list, not only on the
        // chevron. A DropDownList combo does that natively; an editable one
        // only opens from its arrow, because the rest of it is a text box -
        // and it looks exactly like the others, so clicking the words and
        // getting nothing read as broken. WinForms raises the edit window's
        // mouse events on the combo itself, in the combo's coordinates.
        combo.MouseDown += (_, e) => ArmOpen(e);
        combo.MouseUp += (_, e) => OpenIfArmed(e);

        // Binding is attempted from several directions on purpose.
        //
        // The theming pass runs in MainForm's constructor, before the window
        // exists, so at Attach time almost no combo has a handle yet. A
        // control on a page that has never been shown does not get one until
        // it is first made visible, and the handle is destroyed and rebuilt
        // again whenever a style bit changes - which StyleComboBox itself
        // provokes by setting DrawMode. Hooking only HandleCreated leaves
        // the skin on a dead handle, or on none at all, if any of those
        // happen in an order this did not anticipate.
        combo.HandleCreated += (_, _) => Bind();
        // Repaint, not just Bind: a page's controls become visible for the
        // first time long after the theming pass resized them, and that is
        // the moment the stale ring needs erasing.
        combo.VisibleChanged += (_, _) => Repaint();
        combo.ParentChanged += (_, _) => Bind();
        combo.HandleDestroyed += (_, _) => ReleaseHandle();
        combo.Disposed += (_, _) => ReleaseHandle();

        Bind();
    }

    public static void Attach(ComboBox combo)
    {
        if (Attached.TryGetValue(combo, out _))
        {
            combo.Invalidate();
            return;
        }

        Attached.Add(combo, new ComboBoxSkin(combo));
    }

    private void Bind()
    {
        if (!_combo.IsHandleCreated || Handle == _combo.Handle)
        {
            return;
        }

        if (Handle != IntPtr.Zero)
        {
            ReleaseHandle();
        }

        AssignHandle(_combo.Handle);
    }

    /// <summary>
    /// Notes a left press on a CLOSED editable combo. Only a press seen
    /// while closed can open it on release, so the click that closes an open
    /// list - on the arrow, or anywhere outside the list - never opens it
    /// straight back up.
    /// </summary>
    private void ArmOpen(MouseEventArgs e)
    {
        _pressedAt = e.Button == MouseButtons.Left
                     && _combo.DropDownStyle == ComboBoxStyle.DropDown
                     && _combo.Enabled
                     && _combo.Items.Count > 0
                     && !_combo.DroppedDown
            ? e.Location
            : null;
    }

    private void OpenIfArmed(MouseEventArgs e)
    {
        if (_pressedAt is not { } down)
        {
            return;
        }

        _pressedAt = null;

        if (e.Button != MouseButtons.Left || _combo.DroppedDown)
        {
            return;
        }

        // A drag is someone selecting the typed text, not asking for the list.
        var drag = SystemInformation.DragSize;

        if (Math.Abs(e.X - down.X) > drag.Width || Math.Abs(e.Y - down.Y) > drag.Height)
        {
            return;
        }

        // After the mouse-up has finished, not during it. WinForms captures
        // the mouse for a press in the edit and lets it go once the release
        // is handled; the open list holds the capture itself, and losing it
        // to that release would close the list the moment it appeared.
        if (_combo.IsHandleCreated && !_combo.IsDisposed)
        {
            _combo.BeginInvoke(new Action(() =>
            {
                if (!_combo.IsDisposed && _combo.Enabled && !_combo.DroppedDown)
                {
                    _combo.DroppedDown = true;
                }
            }));
        }
    }

    private void SetHot(bool hot)
    {
        if (_hot == hot)
        {
            return;
        }

        _hot = hot;
        Repaint();
    }

    private void Repaint()
    {
        if (_combo.IsDisposed || !_combo.IsHandleCreated)
        {
            return;
        }

        // Last line of defence: anything that provokes a repaint also gets
        // a chance to bind, so a combo whose handle appeared through a path
        // none of the events above cover still ends up skinned rather than
        // silently staying native.
        Bind();

        _combo.Invalidate();

        // The control's own rectangle is all Invalidate can reach, and the
        // theming pass resizes combos (font changes, and the handle
        // recreation that setting DrawMode provokes). The edge drawn at the
        // OLD bounds is left on the parent - the faint bright ring around
        // dropdowns. See SkinGhost.
        SkinGhost.ClearAround(_combo);
    }

    /// <summary>
    /// Both kinds of combo are ours now. An editable one owns a child edit
    /// window, but a child paints after its parent, so drawing the whole
    /// field underneath it is safe as long as the fill matches the edit's own
    /// background - which it does, because StyleComboBox sets both to
    /// Surface. What is NOT safe is drawing the text, so that is skipped for
    /// editable combos and left to the edit window.
    /// </summary>
    private bool Skinnable => !_combo.IsDisposed && _combo.IsHandleCreated;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int CB_SHOWDROPDOWN = 0x014F;
    private const int CB_GETDROPPEDSTATE = 0x0157;
    private const int VK_F4 = 0x73;
    private const int VK_UP = 0x26;
    private const int VK_DOWN = 0x28;

    /// <summary>
    /// A long list opens in the app's own drop-down rather than Windows' (24
    /// Nov 2026, Asher: "make sure you can't see them" - Windows' list wore
    /// its bright white scroll bar): every way of opening it - a press, F4,
    /// Alt+Up or Down, DroppedDown - is answered here, and DroppedDown reads
    /// true while it is open. See ComboListPopup.
    /// </summary>
    private bool OpensItsOwnList(ref Message m)
    {
        switch (m.Msg)
        {
            case CB_GETDROPPEDSTATE when ComboListPopup.IsOpen(_combo):
                m.Result = (IntPtr)1;
                return true;

            case CB_SHOWDROPDOWN when m.WParam == IntPtr.Zero && ComboListPopup.IsOpen(_combo):
                ComboListPopup.Close(_combo);
                m.Result = (IntPtr)1;
                return true;
        }

        if (!ComboListPopup.Wanted(_combo))
        {
            return false;
        }

        switch (m.Msg)
        {
            case WM_LBUTTONDOWN or WM_LBUTTONDBLCLK:
                if (_combo.CanFocus && !_combo.Focused)
                {
                    _combo.Focus();
                }

                ComboListPopup.Toggle(_combo);
                return true;

            case CB_SHOWDROPDOWN when m.WParam != IntPtr.Zero:
                ComboListPopup.Open(_combo);
                m.Result = (IntPtr)1;
                return true;

            case WM_KEYDOWN when (int)m.WParam == VK_F4:
            case WM_SYSKEYDOWN when (int)m.WParam is VK_DOWN or VK_UP:
                ComboListPopup.Toggle(_combo);
                return true;
        }

        return false;
    }

    protected override void WndProc(ref Message m)
    {
        if (Skinnable && OpensItsOwnList(ref m))
        {
            return;
        }

        switch (m.Msg)
        {
            case WM_ERASEBKGND when Skinnable:
                // Claiming the erase is what keeps the field from flashing
                // its background colour before the real paint lands.
                m.Result = (IntPtr)1;
                return;

            case WM_PAINT when Skinnable:
                // Only take the message if the paint actually happened.
                // Returning without validating the window would leave the
                // update region dirty and WM_PAINT would be posted again
                // immediately - a spin that pins a core.
                if (OnPaint())
                {
                    m.Result = IntPtr.Zero;
                    return;
                }

                break;

            // The colours an editable combo's own text box asks for when it
            // is switched off. A disabled edit asks its parent - the combo -
            // with WM_CTLCOLORSTATIC rather than the WM_CTLCOLOREDIT WinForms
            // answers with BackColor, and nothing answered it, so Windows
            // gave it its own white. That is how Venue and Track layout on
            // Race Day > Setup went to two bright white boxes on a closed
            // day: deleting the last race day left the page on the old
            // "Before events" entry, which can't be edited (Asher, 1 Nov).
            // Answered with the same fill the skin draws the field in, so
            // the box and the field around it are one colour in every theme.
            case WM_CTLCOLORSTATIC when Skinnable && _combo.DropDownStyle != ComboBoxStyle.DropDownList && m.WParam != IntPtr.Zero:
                m.Result = EditColours(m.WParam);
                return;

            // A picture of the window (Control.DrawToBitmap - the layout
            // test's pictures) asks each control to draw itself into the
            // picture rather than to paint. Answered with this picture, not
            // Windows' own field: the pictures showed the native grey arrow
            // and a disabled list's text in black, neither of which anybody
            // sees. A drop-down list only - an editable one's text is in a
            // child window, which Windows draws into the picture itself.
            case WM_PRINT or WM_PRINTCLIENT when Skinnable && _combo.DropDownStyle == ComboBoxStyle.DropDownList && m.WParam != IntPtr.Zero:
                PrintTo(m.WParam);
                m.Result = IntPtr.Zero;
                return;
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
            var width = Math.Max(1, _combo.Width);
            var height = Math.Max(1, _combo.Height);

            // Composed off-screen and blitted once. Drawing the fill, the
            // border and the chevron straight onto the DC would show as
            // three separate states on a slow repaint.
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

    /// <summary>The field drawn into a picture of the window - see WndProc. Composed off-screen, as a paint is, and put down at the control's place in it.</summary>
    private void PrintTo(IntPtr hdc)
    {
        var width = Math.Max(1, _combo.Width);
        var height = Math.Max(1, _combo.Height);

        using var buffer = new Bitmap(width, height);
        using (var g = Graphics.FromImage(buffer))
        {
            Draw(g, width, height);
        }

        using var target = Graphics.FromHdc(hdc);
        target.DrawImageUnscaled(buffer, 0, 0);
    }

    private void Draw(Graphics g, int width, int height)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var enabled = _combo.Enabled;

        // Clear, NOT FillRectangle - and this is the whole bug.
        //
        // The buffer starts fully TRANSPARENT, and FillRectangle composites
        // SourceOver, which does not force the outermost pixels opaque. The
        // top row therefore blitted semi-transparent and blended with the
        // native border still sitting in the device context underneath,
        // which is where the pale line came from. Every attempt to fix the
        // border failed because the border was never wrong - the buffer was
        // not opaque. Measured: the skin painted #2A1B4F into row 0 and
        // #5F537A arrived on screen, while an opaque fill of that same row
        // reached it untouched.
        //
        // Graphics.Clear writes opaque pixels regardless of compositing or
        // smoothing, so the blit replaces the device context outright.
        //
        // The corners still show the page behind them: the canvas colour is
        // the parent's own, and the antialiased rounded path is drawn over
        // it, which is what keeps the curve clean without a Region.
        g.Clear(_combo.Parent?.BackColor ?? AppTheme.Background);

        var active = enabled && (_open || _combo.Focused);
        var border = !enabled ? AppTheme.Border
            : active ? AppTheme.Primary
            : _hot ? AppTheme.TextMuted
            : AppTheme.Border;

        var bounds = new RectangleF(0.5f, 0.5f, width - 1f, height - 1f);
        var radius = Math.Max(1, Math.Min(7, height / 2));

        using (var path = AppTheme.RoundedRect(bounds, radius))
        using (var fill = new SolidBrush(FieldFill(_combo)))
        using (var pen = new Pen(border, active ? 1.4f : 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        // Chevron, in its own zone on the right. Drawn rather than glyphed
        // so it scales with the control and picks up the accent when open -
        // and at the text size in use, as the words are (DropArrow).
        //
        // An editable combo keeps it inside the native arrow button: its
        // text is a child edit window that ends where that button starts,
        // and a child paints over its parent, so a chevron wider than the
        // button would be cut off by the edit. A drop-down list has no child
        // over it, and the whole field opens it, so it takes the full zone.
        var ink = !enabled ? AppTheme.TextMuted : active ? AppTheme.Primary : AppTheme.TextMuted;

        if (_combo.DropDownStyle != ComboBoxStyle.DropDownList && NativeButton() is { Width: > 0 } button)
        {
            DropArrow.Draw(g, button, ink);
        }
        else
        {
            DropArrow.Draw(g, DropArrow.ZoneIn(width, height), ink);
        }

        if (_combo.DropDownStyle != ComboBoxStyle.DropDownList)
        {
            return;
        }

        var text = _combo.SelectedIndex >= 0
            ? _combo.GetItemText(_combo.Items[_combo.SelectedIndex])
            : _combo.Text;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var textRect = new Rectangle(DropArrow.TextLeft, 0, Math.Max(4, DropArrow.TextRight(width) - DropArrow.TextLeft), height);
        TextRenderer.DrawText(g, text, _combo.Font, textRect,
            enabled ? AppTheme.TextDark : AppTheme.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// The colour a combo's closed field is filled with: the surface while it
    /// can be used, the page's own background once it is switched off. The
    /// skin draws the field in it, and an editable combo's text box is
    /// given it too (EditColours), so the two always match.
    /// </summary>
    internal static Color FieldFill(ComboBox combo) => combo.Enabled ? AppTheme.Surface : AppTheme.Background;

    /// <summary>The colour of the words in a switched-off editable combo's text box - muted, as a switched-off drop-down list's are.</summary>
    internal static Color DisabledInk => AppTheme.TextMuted;

    /// <summary>
    /// One solid brush per colour, made the first time it is asked for and
    /// kept for the life of the app. Windows uses the brush a WM_CTLCOLOR
    /// answer hands back after the answer returns, so it cannot be made and
    /// thrown away per message; and a handful of colours (a fill per theme)
    /// is all there will ever be.
    /// </summary>
    private static readonly Dictionary<int, IntPtr> FillBrushes = new();

    private static IntPtr BrushFor(Color colour)
    {
        var win32 = ColorTranslator.ToWin32(colour);

        lock (FillBrushes)
        {
            if (!FillBrushes.TryGetValue(win32, out var brush) || brush == IntPtr.Zero)
            {
                brush = CreateSolidBrush(win32);
                FillBrushes[win32] = brush;
            }

            return brush;
        }
    }

    /// <summary>Sets up the text box's device context in the theme's colours and hands back the brush its background is painted with.</summary>
    private IntPtr EditColours(IntPtr hdc)
    {
        var fill = FieldFill(_combo);

        SetBkMode(hdc, OPAQUE);
        SetBkColor(hdc, ColorTranslator.ToWin32(fill));
        SetTextColor(hdc, ColorTranslator.ToWin32(_combo.Enabled ? AppTheme.TextDark : DisabledInk));

        return BrushFor(fill);
    }

    private const int OPAQUE = 2;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int colour);

    [DllImport("gdi32.dll")]
    private static extern int SetBkColor(IntPtr hdc, int colour);

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(IntPtr hdc, int colour);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    /// <summary>
    /// Where the native control keeps its arrow button, in its own
    /// coordinates - null when Windows does not say.
    /// </summary>
    private Rectangle? NativeButton()
    {
        var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf<COMBOBOXINFO>() };

        if (!_combo.IsHandleCreated || !GetComboBoxInfo(_combo.Handle, ref info))
        {
            return null;
        }

        return Rectangle.FromLTRB(info.rcButton.Left, info.rcButton.Top, info.rcButton.Right, info.rcButton.Bottom);
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
    private struct COMBOBOXINFO
    {
        public int cbSize;
        public RECT rcItem;
        public RECT rcButton;
        public int stateButton;
        public IntPtr hwndCombo;
        public IntPtr hwndItem;
        public IntPtr hwndList;
    }

    [DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr hWnd, ref COMBOBOXINFO info);

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
/// The chevron every drop-down in the app wears - a drop-down list, a date
/// picker, a list at the top of a grid's column - drawn at the text size in
/// use, in a zone of its own at the right-hand end.
///
/// At the normal text size it is what it always was: 9px across in a 26px
/// zone, with the text 10px in and ending 12px short of the zone. It grows
/// with the text, and the zone grows by as much as it does, so the room
/// either side of it stays the same. Asher, 30 Sep: at 200% the words
/// doubled and the arrows stayed a 9px sliver, and a number box's arrows
/// and the sliders' handles with them.
/// </summary>
internal static class DropArrow
{
    /// <summary>The text size in use, and the screen's DPI once the app is started --dpi-aware - what a pixel at the normal text size comes to.</summary>
    private static float Scale => UiLayout.AtTextSize(1000) / 1000f;

    /// <summary>How far across the chevron is: 9px at the normal text size.</summary>
    public static float Across => 9f * Scale;

    /// <summary>The zone the chevron is drawn in at the right-hand end: 26px at the normal text size, and wider by as much as the chevron is.</summary>
    public static int Zone => (int)Math.Round(Across) + UiLayout.AtDpi(17);

    /// <summary>How far in a drop-down's text starts.</summary>
    public static int TextLeft => UiLayout.AtDpi(10);

    /// <summary>The zone's place in a drop-down <paramref name="width"/> across: at the right-hand end, 2px in from it.</summary>
    public static Rectangle ZoneIn(int width, int height) => new(width - Zone - 2, 0, Zone, height);

    /// <summary>Where a drop-down's text has to end: where the chevron's zone starts.</summary>
    public static int TextRight(int width) => width - Zone - 2;

    /// <summary>
    /// What a drop-down needs across besides its longest text: the room
    /// before the text, the chevron's zone and the few pixels past it, and
    /// 3px spare - 41px at the normal text size, which is what every
    /// drop-down in the app was sized with (Pieces.PickerWidth).
    /// </summary>
    public static int Room => TextLeft + Zone + 2 + UiLayout.AtDpi(3);

    /// <summary>
    /// Draws the chevron, pointing down, centred in <paramref name="zone"/> -
    /// or as big as fits in it, for a zone narrower than the chevron wants.
    /// </summary>
    public static void Draw(Graphics g, Rectangle zone, Color ink)
    {
        var scale = Scale;

        // 9 across and 4.5 deep, and room for the pen's round ends either
        // side, at the text size - or less, to fit.
        var fits = Math.Max(1f, (zone.Width - 4f * scale) / 9f);
        var size = Math.Min(scale, fits);

        var cx = zone.Left + zone.Width / 2f;
        var cy = zone.Top + zone.Height / 2f - size;

        var smoothing = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var arrow = new Pen(ink, 1.8f * size))
        {
            arrow.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            arrow.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            arrow.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;

            g.DrawLines(arrow, new[]
            {
                new PointF(cx - 4.5f * size, cy - 2f * size),
                new PointF(cx, cy + 2.5f * size),
                new PointF(cx + 4.5f * size, cy - 2f * size),
            });
        }

        g.SmoothingMode = smoothing;
    }
}
