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
/// Only DropDownList combos are skinned. An editable one has a real child
/// edit window with a caret and a selection, and painting over it would
/// leave you typing into something you cannot see.
/// </summary>
internal sealed class ComboBoxSkin : NativeWindow
{
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;

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

    protected override void WndProc(ref Message m)
    {
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
        using (var fill = new SolidBrush(enabled ? AppTheme.Surface : AppTheme.Background))
        using (var pen = new Pen(border, active ? 1.4f : 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        // Chevron, in its own zone on the right. Drawn rather than glyphed
        // so it scales with the control and picks up the accent when open.
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

        var textRect = new Rectangle(10, 0, Math.Max(4, width - arrowZone - 12), height);
        TextRenderer.DrawText(g, text, _combo.Font, textRect,
            enabled ? AppTheme.TextDark : AppTheme.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
