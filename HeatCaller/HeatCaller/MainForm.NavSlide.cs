using System.Diagnostics;

namespace HeatCaller;

/// <summary>
/// The sidebar sliding open and shut (15 Nov 2026, asked for by Asher: the
/// one-frame switch "just looks like the nav panel disappears").
///
/// Sliding the sidebar's real width would lay the page beside it out again
/// at every frame - every card, the leaderboard, the clock - which no page
/// here can do sixty times a second. So the sidebar is changed in one go,
/// as SetNavIconsOnly always has, and what slides is a picture: the open
/// sidebar drawn on a curtain over the sidebar's place, as wide as the
/// slide has got, with the page itself moved along beside it - moved, not
/// resized, which Windows does by copying what is already drawn. The page
/// is laid out once, at its new width, before the first frame.
/// </summary>
public partial class MainForm
{
    /// <summary>How long the sidebar takes to slide: as long as Race Control's fold (Beside.SlideTo), so the two move together.</summary>
    private const double NavSlideMs = 280;

    private NavCurtain? _navCurtain;
    private System.Windows.Forms.Timer? _navSlideTimer;
    private readonly SlideTime _navSlideClock = new();
    private int _navSlideFrom;
    private int _navSlideTo;
    private bool _navFinerTimer;

    /// <summary>
    /// Narrows the sidebar to its icons, or opens it again, sliding. Where it
    /// cannot be seen sliding - the window not up yet, minimised, or laid out
    /// by hand for the layout test - it simply changes, as it always did.
    /// </summary>
    internal void SlideNavIconsOnly(bool iconsOnly)
    {
        EndNavSlide();

        if (iconsOnly == _navIconsOnly)
        {
            return;
        }

        // Nor with the sidebar hidden - Race Mode keeps its width but not its
        // place, so a slide would only lurch the board 220px and back (bug
        // hunt 21, 15 Nov 2026).
        if (LayoutCheckMode || !IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized
            || pnlContent.Dock != DockStyle.Fill || pnlSidebar.Width <= 0 || !pnlSidebar.Visible)
        {
            SetNavIconsOnly(iconsOnly);
            return;
        }

        var from = pnlSidebar.Width;

        // The open sidebar's picture: now, on the way shut; once opened, on
        // the way open.
        var open = iconsOnly ? Picture(pnlSidebar) : null;

        SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

        try
        {
            SetNavIconsOnly(iconsOnly, holdPainting: false);
            PerformLayout();

            open ??= Picture(pnlSidebar);

            var to = pnlSidebar.Width;
            var page = pnlContent.Bounds;

            // Placed by hand until the slide ends, at its new width, from
            // where the sidebar ends now.
            pnlContent.Dock = DockStyle.None;
            pnlContent.Bounds = new Rectangle(from, page.Top, page.Width, page.Height);

            _navCurtain = new NavCurtain(open)
            {
                Bounds = new Rectangle(0, 0, from, ClientSize.Height),
            };

            Controls.Add(_navCurtain);
            _navCurtain.BringToFront();

            _navSlideFrom = from;
            _navSlideTo = to;
        }
        finally
        {
            SendMessage(Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow | RdwFrame);
        }

        // Timed from the first frame - see Beside.SlideTo.
        _navSlideClock.Reset();
        _navFinerTimer = TimeBeginPeriod(1) == 0;
        _navSlideTimer = new System.Windows.Forms.Timer { Interval = 10 };
        _navSlideTimer.Tick += (_, _) => NextNavFrame();
        _navSlideTimer.Start();
    }

    private void NextNavFrame()
    {
        var t = Math.Clamp(_navSlideClock.Tick() / NavSlideMs, 0, 1);

        if (t >= 1)
        {
            EndNavSlide();
            return;
        }

        // Eased in and out, as Race Control's fold is.
        var e = SlideTime.Ease(t);
        var width = _navSlideFrom + (int)Math.Round((_navSlideTo - _navSlideFrom) * e);

        if (_navCurtain is { } curtain && curtain.Width != width)
        {
            curtain.Width = width;
        }

        if (pnlContent.Left != width)
        {
            pnlContent.Left = width;
        }
    }

    /// <summary>The slide over: the curtain gone and the page docked beside the sidebar again - where the slide left it.</summary>
    private void EndNavSlide()
    {
        if (_navSlideTimer is null && _navCurtain is null)
        {
            return;
        }

        _navSlideTimer?.Stop();
        _navSlideTimer?.Dispose();
        _navSlideTimer = null;
        _navSlideClock.Reset();

        if (_navFinerTimer)
        {
            TimeEndPeriod(1);
            _navFinerTimer = false;
        }

        pnlContent.Dock = DockStyle.Fill;

        if (_navCurtain is { } curtain)
        {
            _navCurtain = null;
            Controls.Remove(curtain);
            curtain.Dispose();
        }
    }

    /// <summary>A control as it is drawn now, children and all.</summary>
    private static Bitmap Picture(Control control)
    {
        var picture = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        control.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
        return picture;
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);

    /// <summary>The open sidebar's picture, shown as wide as the slide has got - its right-hand part cut off, not squeezed.</summary>
    private sealed class NavCurtain : Control
    {
        private readonly Bitmap _picture;

        public NavCurtain(Bitmap picture)
        {
            _picture = picture;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            TabStop = false;
            Tag = AppTheme.KeepBackColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var back = new SolidBrush(AppTheme.SidebarBackground);
            e.Graphics.FillRectangle(back, ClientRectangle);
            e.Graphics.DrawImageUnscaled(_picture, 0, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _picture.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
