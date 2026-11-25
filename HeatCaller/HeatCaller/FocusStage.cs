using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>Where the moving parts are, in the main window's client area, for one Focus animation.</summary>
/// <param name="NavFrom">The sidebar's width before.</param>
/// <param name="NavTo">The sidebar's width after - known before the change, as the sidebar's two widths are fixed.</param>
/// <param name="Inbox">The docked inbox, splitter and all: in the picture after when focusing, in the picture before when restoring. Empty when there is none.</param>
/// <param name="BoardTop">The top edge of the leaderboard card after - where the light runs. Empty for none.</param>
/// <param name="Button">The Focus button where it was pressed - where the ring of light grows from.</param>
internal readonly record struct FocusScene(int NavFrom, int NavTo, Rectangle Inbox, Rectangle BoardTop, Rectangle Button);

/// <summary>
/// THE FOCUS STAGE (24 Nov 2026, asked for by Asher: "that should be animated
/// in a way that looks like it costs $10,000"). A borderless window laid over
/// the main window's client area for the 650 ms of a Focus (420 of a
/// Restore), drawing two pictures of it - before and after - moving, cut and
/// faded as FocusChoreography says, with the glow, the light and the ring
/// drawn on top.
///
/// WHY PICTURES. Race Control cannot be laid out sixty times a second - the
/// leaderboard alone takes 30-40 ms (PR #218) - so nothing real moves. The
/// window is changed once, underneath, while the stage covers it, and the
/// stage plays between the two pictures. Each frame is a few GDI blits
/// (BitBlt, and AlphaBlend for the fades), a millisecond or two at full
/// size, so it holds sixty frames a second.
///
/// ON A THREAD OF ITS OWN. Changing the window underneath takes about a
/// second - the inbox built (about 380 ms), the clock column folded (about
/// 340), the sidebar narrowed (about 120), measured 24 Nov 2026 - and on the
/// window's own thread the stage sat frozen on the picture before for all of
/// it. So the stage has its own thread and message loop: it starts moving the
/// moment Focus is pressed, with what needs only the picture before - the
/// ring, the sidebar closing, the old page fading out - and the parts that
/// need the picture after (the new page, the inbox, the light) play from
/// when it arrives (<see cref="Arrive"/>), however long that took.
///
/// It never takes the keyboard or the mouse: clicks and keys go straight to
/// the window under it, already in its new place, so a flag thrown during
/// the animation is thrown.
/// </summary>
internal sealed class FocusStage : Form
{
    /// <summary>
    /// How far into Focus (Restore) the parts that need the picture after are
    /// held, if it has not arrived: the new page has not begun to show and the
    /// sidebar's fade into its narrow self has not begun.
    /// </summary>
    internal const double EnterHoldMs = 130;

    /// <inheritdoc cref="EnterHoldMs"/>
    internal const double LeaveHoldMs = 120;

    private readonly Bitmap _from;
    private Bitmap? _to;
    private IntPtr _fromBits;
    private IntPtr _toBits;
    private FocusScene _scene;
    private readonly bool _entering;
    private readonly Action<Action>? _toMain;
    private Action? _done;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly SlideTime _clock = new();
    private bool _finerTimer;
    private bool _started;
    private double _ms;

    /// <summary>When the picture after arrived, in ms into the animation - null until it has.</summary>
    private double? _afterAt;

    /// <param name="bounds">Where it goes, on the screen: the main window's client area.</param>
    /// <param name="toMain">Runs an action on the main window's thread - the done callback goes through it.</param>
    internal FocusStage(Rectangle bounds, Bitmap before, FocusScene scene, bool entering, Action<Action>? toMain = null, Action? done = null)
    {
        _from = before;
        _fromBits = before.GetHbitmap();
        _scene = scene;
        _entering = entering;
        _toMain = toMain;
        _done = done;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = AppTheme.Background;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        // Layered from the start, so the fade at the end is a change of
        // opacity rather than a switch of window kind mid-animation.
        Opacity = 0.995;

        _timer.Tick += (_, _) => Frame();
    }

    /// <summary>
    /// Puts a stage up over <paramref name="bounds"/> on a thread of its own
    /// and starts it at once. Null when it could not be put up - the caller
    /// then simply changes the view. <paramref name="done"/> runs on the main
    /// window's thread, through <paramref name="toMain"/>, once it has gone.
    /// </summary>
    internal static FocusStage? Begin(Rectangle bounds, Bitmap before, FocusScene scene, bool entering, Action<Action> toMain, Action done)
    {
        FocusStage? stage = null;
        // Not disposed: the stage's thread signals it again as it ends, after
        // this has returned - disposed, that threw on the stage's thread.
        var up = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
                stage = new FocusStage(bounds, before, scene, entering, toMain, done);
                stage.Shown += (_, _) => up.Set();
                Application.Run(stage);
            }
            catch
            {
                // No stage: the caller changes the view without one.
            }
            finally
            {
                up.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Focus stage",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return up.Wait(TimeSpan.FromSeconds(2)) && stage is { IsDisposed: false } shown ? shown : null;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80;
            const int WS_EX_TRANSPARENT = 0x20;
            const int WS_EX_NOACTIVATE = 0x08000000;

            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Start();
    }

    /// <summary>The clock and the frames - from the moment it is up.</summary>
    private void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _clock.Reset();
        _ms = 0;

        if (TimeBeginPeriod(1) == 0)
        {
            _finerTimer = true;
        }

        _timer.Start();
    }

    /// <summary>
    /// The picture after has been taken, and where the inbox and the board
    /// are in it. From any thread - it is handed to the stage's own.
    /// </summary>
    internal void Arrive(Bitmap after, Rectangle inbox, Rectangle boardTop)
    {
        try
        {
            BeginInvoke(new Action(() => Prepare(after, _scene with { Inbox = inbox, BoardTop = boardTop }, _ms)));
        }
        catch (InvalidOperationException)
        {
            // Gone already.
            after.Dispose();
        }
    }

    /// <summary>Called off, from any thread: it goes at once.</summary>
    internal void CallOff()
    {
        try
        {
            BeginInvoke(new Action(() => Finish()));
        }
        catch (InvalidOperationException)
        {
            // Gone already.
        }
    }

    /// <summary>The picture after and where the parts are in it, <paramref name="at"/> ms in - Arrive, and the tests through Render.</summary>
    internal void Prepare(Bitmap after, FocusScene scene, double at)
    {
        _to = after;
        _toBits = after.GetHbitmap();
        _scene = scene;
        _afterAt = at;
        Invalidate();
    }

    /// <summary>
    /// How far behind the parts that need the picture after run: nothing
    /// when it came before their hold, else the time it was late by - and,
    /// still waiting, as long as it has waited past the hold.
    /// </summary>
    internal static double Lateness(double ms, double? afterAt, double hold) =>
        afterAt is { } at ? Math.Max(0, at - hold) : Math.Max(0, ms - hold);

    private double Hold => _entering ? EnterHoldMs : LeaveHoldMs;

    private void Frame()
    {
        try
        {
            NextFrame();
        }
        catch (Exception)
        {
            Finish();
        }
    }

    private void NextFrame()
    {
        _ms = _clock.Tick();

        var late = _ms - Lateness(_ms, _afterAt, Hold);
        var frame = _entering ? FocusChoreography.Enter(late) : FocusChoreography.Leave(late);
        var opacity = _afterAt is null ? 0.995 : Math.Clamp(frame.StageAlpha, 0, 0.995);

        if (Math.Abs(Opacity - opacity) > 0.001)
        {
            Opacity = opacity;
        }

        // Never longer than a few seconds, whatever happened to the picture after.
        if ((_afterAt is not null && FocusChoreography.Done(late, _entering)) || _ms > 6000)
        {
            Finish();
            return;
        }

        Invalidate();
    }

    private void Finish()
    {
        _timer.Stop();

        if (_finerTimer)
        {
            TimeEndPeriod(1);
            _finerTimer = false;
        }

        var done = _done;
        _done = null;

        if (!IsDisposed)
        {
            Close();
        }

        if (done is not null)
        {
            if (_toMain is not null)
            {
                try
                {
                    _toMain(done);
                }
                catch (Exception)
                {
                    // The main window has gone: nothing to tell.
                }
            }
            else
            {
                done();
            }
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Every pixel is drawn in OnPaint.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // An animation must never take the app down: anything that goes
        // wrong drawing it ends it, and the window under it is already right.
        try
        {
            Render(e.Graphics, _ms, ClientSize);
        }
        catch (Exception)
        {
            BeginInvoke(new Action(Finish));
        }
    }

    /// <summary>
    /// The moment <paramref name="ms"/> into the animation, drawn onto
    /// <paramref name="g"/> at <paramref name="size"/> - by OnPaint each frame,
    /// and by the tests, which draw chosen moments to pictures to look at.
    /// </summary>
    internal void Render(Graphics g, double ms, Size size)
    {
        // What needs only the picture before runs on the stage's own clock;
        // what needs the picture after, from when it arrived.
        var own = _entering ? FocusChoreography.Enter(ms) : FocusChoreography.Leave(ms);
        var lateMs = ms - Lateness(ms, _afterAt, Hold);
        var after = _entering ? FocusChoreography.Enter(lateMs) : FocusChoreography.Leave(lateMs);
        var haveAfter = _to is not null;

        var width = size.Width;
        var height = size.Height;

        // How open the sidebar is. Focusing, it closes on its own clock, from
        // the picture before; restoring, it opens with the picture after.
        var curtain = _entering ? own.Curtain : haveAfter ? after.Curtain : 0;
        var sidebar = (int)Math.Round(Lerp(_scene.NavFrom, _scene.NavTo, curtain));

        // The inbox: in the picture after when focusing, before when restoring.
        var inbox = _scene.Inbox;
        var inboxFrame = _entering ? after : own;
        var travel = UiLayout.AtDpi(FocusChoreography.InboxTravel);
        var inboxShift = (int)Math.Round(travel * (1 - inboxFrame.InboxIn));
        var showInbox = !inbox.IsEmpty && inboxFrame.InboxAlpha > 0 && (!_entering || haveAfter);

        // Both pages on the clock of the picture after: until it arrives the
        // page before stays, riding along with the closing sidebar - waiting
        // on a slow change looks like the page settling, not an empty window.
        var (fromAlpha, toAlpha) = FocusChoreography.Dip(after.Cross);

        if (!haveAfter)
        {
            toAlpha = 0;
        }

        var hdc = g.GetHdc();

        try
        {
            var memory = CreateCompatibleDC(hdc);
            var old = SelectObject(memory, _fromBits);

            try
            {
                // 1. The window's own background, and over it the page after,
                //    riding along with the sidebar's edge into its place and
                //    fading up as the page before has nearly gone (3) - a dip,
                //    not a cross-fade: two pages of the same words, offset,
                //    read as double vision. Focusing, it stops short of the
                //    inbox's place: the inbox comes in by itself (4).
                using (var ground = Graphics.FromHdc(hdc))
                using (var fill = new SolidBrush(AppTheme.Background))
                {
                    ground.FillRectangle(fill, 0, 0, width, height);
                }

                if (haveAfter && toAlpha > 0)
                {
                    SelectObject(memory, _toBits);

                    var afterShift = sidebar - _scene.NavTo;
                    var afterRight = _entering && !inbox.IsEmpty ? inbox.Left + afterShift : width;
                    var afterWidth = Math.Min(Math.Min(afterRight, width) - sidebar, width - _scene.NavTo);

                    if (afterWidth > 0)
                    {
                        Copy(hdc, sidebar, afterWidth, height, memory, _scene.NavTo, toAlpha);
                    }
                }

                // 2. The sidebar: the narrow one underneath, and the wide one
                //    over it cut off at the width it has got to - the open one
                //    closing (focusing) or opening (restoring) - fading into the
                //    narrow one's own icons as it reaches it, so the hand-over
                //    is not a jump.
                var narrowing = _scene.NavFrom >= _scene.NavTo;
                var narrowWidth = Math.Min(_scene.NavFrom, _scene.NavTo);
                var narrowBits = narrowing ? _toBits : _fromBits;
                var wideBits = narrowing ? _fromBits : _toBits;

                if (narrowWidth > 0 && narrowBits != IntPtr.Zero)
                {
                    SelectObject(memory, narrowBits);
                    BitBlt(hdc, 0, 0, narrowWidth, height, memory, 0, 0, SRCCOPY);
                }

                if (_scene.NavFrom != _scene.NavTo && sidebar > 0 && wideBits != IntPtr.Zero)
                {
                    // Focusing, solid until the narrow sidebar is there to fade into.
                    var curtainAlpha = narrowing
                        ? haveAfter ? FocusChoreography.CurtainAlpha(after.Curtain, narrowing: true) : 1
                        : FocusChoreography.CurtainAlpha(curtain, narrowing: false);

                    if (curtainAlpha > 0)
                    {
                        SelectObject(memory, wideBits);
                        Copy(hdc, 0, sidebar, height, memory, 0, curtainAlpha);
                    }
                }

                // 3. The page before, fading out as it rides along with the
                //    sidebar's edge, so the old page slides as it goes.
                //    Restoring, it stops short of the inbox, which leaves on
                //    its own (4).
                if (fromAlpha > 0)
                {
                    SelectObject(memory, _fromBits);

                    var srcX = _scene.NavFrom;
                    var pageRight = !_entering && !inbox.IsEmpty ? inbox.Left : width;
                    var visible = Math.Min(Math.Max(0, pageRight - srcX), width - sidebar);

                    if (visible > 0)
                    {
                        Copy(hdc, sidebar, visible, height, memory, srcX, fromAlpha);
                    }
                }

                // 4. The inbox, sliding in from the right (or out to it).
                if (showInbox)
                {
                    SelectObject(memory, _entering ? _toBits : _fromBits);
                    DrawInbox(hdc, memory, inbox, inboxShift, inboxFrame, width, _entering ? lateMs : ms);
                }
            }
            finally
            {
                SelectObject(memory, old);
                DeleteDC(memory);
            }
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }

        // 5. Light on top: the inbox's glowing edge, the run along the board,
        //    the ring from the button.
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (showInbox && _entering && after.Edge > 0)
        {
            DrawEdgeGlow(g, new Rectangle(inbox.Left + inboxShift, inbox.Top, inbox.Width, inbox.Height), after.Edge);
        }

        if (haveAfter && !_scene.BoardTop.IsEmpty && after.SweepAlpha > 0)
        {
            DrawSweep(g, _scene.BoardTop, after.Sweep, after.SweepAlpha);
        }
        else if (!haveAfter && _entering && !_scene.BoardTop.IsEmpty && FocusChoreography.Waiting(ms) is { } waiting)
        {
            // Still waiting for the picture after (the inbox being built): the
            // light runs along the board before, riding with its page, so the
            // wait reads as the board getting ready rather than a stall.
            var top = _scene.BoardTop;
            top.Offset(sidebar - _scene.NavFrom, 0);
            DrawSweep(g, top, waiting.At, waiting.Alpha);
        }

        // The ring rides with the page before, as the button it grows from does.
        if (!_scene.Button.IsEmpty && own.RingAlpha > 0)
        {
            var button = _scene.Button;
            button.Offset(sidebar - _scene.NavFrom, 0);
            DrawRing(g, button, own.Ring, own.RingAlpha);
        }
    }

    /// <summary>A strip of the picture selected into <paramref name="memory"/>, from x <paramref name="sourceX"/>, onto x <paramref name="x"/> - solid, or faded.</summary>
    private static void Copy(IntPtr hdc, int x, int w, int h, IntPtr memory, int sourceX, double alpha)
    {
        var a = (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);

        if (a == 255)
        {
            BitBlt(hdc, x, 0, w, h, memory, sourceX, 0, SRCCOPY);
        }
        else if (a > 0)
        {
            AlphaBlend(hdc, x, 0, w, h, memory, sourceX, 0, w, h, Blend(a));
        }
    }

    /// <summary>
    /// The inbox, its panel first and then its rows in bands - entering, each
    /// band rising a few pixels as it fades up, 30 ms after the one above it;
    /// leaving, all of it together.
    /// </summary>
    private void DrawInbox(IntPtr hdc, IntPtr memory, Rectangle inbox, int shift, FocusFrame frame, int width, double ms)
    {
        var x = inbox.Left + shift;
        var visible = Math.Min(inbox.Width, width - x);

        if (visible <= 0)
        {
            return;
        }

        var panelAlpha = (byte)Math.Round(Math.Clamp(frame.InboxAlpha, 0, 1) * 255);

        if (!_entering)
        {
            AlphaBlend(hdc, x, inbox.Top, visible, inbox.Height, memory, inbox.Left, inbox.Top, visible, inbox.Height, Blend(panelAlpha));
            return;
        }

        // The panel's ground, the window's own background, so the bands land
        // on the inbox rather than on the page under it.
        using (var ground = Graphics.FromHdc(hdc))
        using (var fill = new SolidBrush(Color.FromArgb(panelAlpha, AppTheme.Background)))
        {
            ground.FillRectangle(fill, x, inbox.Top, visible, inbox.Height);
        }

        var band = Math.Max(UiLayout.AtDpi(56), 1);
        var rise = UiLayout.AtDpi(FocusChoreography.RowRise);

        for (int top = inbox.Top, index = 0; top < inbox.Bottom; top += band, index++)
        {
            var row = FocusChoreography.RowIn(ms, index);
            var a = (byte)Math.Round(Math.Clamp(row * frame.InboxAlpha, 0, 1) * 255);

            if (a == 0)
            {
                continue;
            }

            var h = Math.Min(band, inbox.Bottom - top);
            var lift = (int)Math.Round(rise * (1 - row));
            AlphaBlend(hdc, x, top + lift, visible, h, memory, inbox.Left, top, visible, h, Blend(a));
        }
    }

    private static void DrawEdgeGlow(Graphics g, Rectangle inbox, double strength)
    {
        var a = (int)Math.Round(Math.Clamp(strength, 0, 1) * 255);
        var spread = UiLayout.AtDpi(28);

        // The soft glow, outwards to the left of the edge.
        var glow = new Rectangle(inbox.Left - spread, inbox.Top, spread, inbox.Height);

        using (var brush = new LinearGradientBrush(new Rectangle(glow.X - 1, glow.Y, glow.Width + 2, Math.Max(1, glow.Height)),
                   Color.FromArgb(0, AppTheme.Primary), Color.FromArgb(a * 150 / 255, AppTheme.Primary), LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, glow);
        }

        // The line itself.
        using var line = new SolidBrush(Color.FromArgb(a, AppTheme.Primary));
        g.FillRectangle(line, inbox.Left, inbox.Top, Math.Max(2, UiLayout.AtDpi(2)), inbox.Height);
    }

    private static void DrawSweep(Graphics g, Rectangle top, double at, double strength)
    {
        var length = UiLayout.AtDpi(220);
        var x = top.Left - length + (int)Math.Round((top.Width + length) * at);
        var a = (int)Math.Round(Math.Clamp(strength, 0, 1) * 255);
        var thick = Math.Max(2, UiLayout.AtDpi(2));
        var state = g.Save();

        g.SetClip(new Rectangle(top.Left, top.Top - UiLayout.AtDpi(10), top.Width, UiLayout.AtDpi(20) + thick));

        // A light with a soft tail: brightest at its leading end, just inside
        // the card's top edge.
        var body = new Rectangle(x, top.Top + UiLayout.AtDpi(1), length, thick);

        using (var brush = new LinearGradientBrush(new Rectangle(body.X - 1, body.Y, body.Width + 2, body.Height),
                   Color.FromArgb(0, AppTheme.Accent), Color.FromArgb(a, AppTheme.Accent), LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, body);
        }

        // Its glow, above and below.
        for (var i = 1; i <= 3; i++)
        {
            var grow = UiLayout.AtDpi(i * 2);
            using var halo = new LinearGradientBrush(new Rectangle(body.X - 1, body.Y - grow, body.Width + 2, body.Height + grow * 2),
                Color.FromArgb(0, AppTheme.Accent), Color.FromArgb(a * 40 / 255, AppTheme.Accent), LinearGradientMode.Horizontal);
            g.FillRectangle(halo, new Rectangle(body.X, body.Y - grow, body.Width, body.Height + grow * 2));
        }

        g.Restore(state);
    }

    private static void DrawRing(Graphics g, Rectangle button, double grow, double strength)
    {
        var spread = UiLayout.AtDpi(18) * grow;
        var ring = RectangleF.Inflate(button, (float)spread, (float)spread);
        var a = (int)Math.Round(Math.Clamp(strength, 0, 1) * 230);

        using var pen = new Pen(Color.FromArgb(a, AppTheme.Primary), Math.Max(1.5f, 2f * UiLayout.DpiScale));
        using var path = AppTheme.RoundedRect(ring, UiLayout.AtDpi(11) + (int)Math.Round(spread));
        g.DrawPath(pen, path);
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private static BLENDFUNCTION Blend(byte alpha) => new() { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 0 };

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();

        if (_finerTimer)
        {
            TimeEndPeriod(1);
            _finerTimer = false;
        }

        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _from.Dispose();
            _to?.Dispose();
        }

        if (_fromBits != IntPtr.Zero)
        {
            DeleteObject(_fromBits);
            _fromBits = IntPtr.Zero;
        }

        if (_toBits != IntPtr.Zero)
        {
            DeleteObject(_toBits);
            _toBits = IntPtr.Zero;
        }

        base.Dispose(disposing);
    }

    private const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

    [DllImport("msimg32.dll")]
    private static extern bool AlphaBlend(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, BLENDFUNCTION blend);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}
