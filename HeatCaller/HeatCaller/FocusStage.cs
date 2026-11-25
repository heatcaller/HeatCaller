using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>What the Focus stage moves, in the main window's client area.</summary>
/// <param name="NavFrom">The sidebar's width before.</param>
/// <param name="NavTo">The sidebar's width after - known before the change, as the sidebar's two widths are fixed.</param>
/// <param name="Button">The Focus button where it was pressed - drawn pressed in for the first 90 ms.</param>
/// <param name="Pieces">The page's pieces: each card's place before (and after, once the view has changed), and the column's parts.</param>
/// <param name="Inbox">The docked inbox, splitter and all: in the picture after when focusing, in the picture before when restoring. Empty for none.</param>
/// <param name="BoardTop">The top edge of the leaderboard's card after - where the landing light runs. Empty for none.</param>
internal sealed record FocusScene(int NavFrom, int NavTo, Rectangle Button, IReadOnlyList<FocusPiece> Pieces, Rectangle Inbox, Rectangle BoardTop);

/// <summary>
/// THE FOCUS STAGE. A borderless window laid over the main window's client
/// area while Focus or Restore plays, drawing the page's pieces from two
/// pictures of the window - before and after - as FocusChoreography says.
///
/// WHY (25 Nov 2026, Asher: "would you pay $10k for that animation?" - no:
/// plans/focus-motion-plan.md). The 24 Nov stage cross-faded the two pictures
/// with a ring, a sweep and a glow over them, so nothing on screen travelled
/// and the eye had nothing to follow. Now every piece moves from where it was
/// to where it goes: the sidebar narrows with the page riding its edge, the
/// column's buttons fold away one by one, each card glides to its new place
/// and size with its contents pinned to its edges (FocusElastic), and the
/// inbox slides in and lands. One pink light along the board as it lands is
/// the only accent.
///
/// WHY PICTURES. Race Control cannot be laid out sixty times a second - the
/// leaderboard alone takes 30-40 ms (PR #218) - so nothing real moves. The
/// window is changed once, underneath, while the stage covers it. Each frame
/// is a few dozen GDI copies (BitBlt, StretchBlt for the plain seams,
/// AlphaBlend for the fades).
///
/// ON A THREAD OF ITS OWN. Changing the window underneath takes about half a
/// second (measured 25 Nov 2026), and on the window's own thread the stage
/// would sit frozen for it. So the stage has its own thread and message loop:
/// it moves the moment Focus is pressed with what needs only the picture
/// before, and the cards follow from when the picture after arrives
/// (<see cref="Arrive"/>).
///
/// It never takes the keyboard or the mouse: clicks and keys go straight to
/// the window under it, already in its new place.
/// </summary>
internal sealed class FocusStage : Form
{
    private Bitmap _from = null!;
    private Bitmap? _to;
    private IntPtr _fromBits;
    private IntPtr _toBits;
    private int[]? _fromPixels;
    private int[]? _toPixels;
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

    /// <summary>Each card's cut, before and after, made once - see FocusElastic.</summary>
    private readonly Dictionary<Rectangle, ElasticPlan> _fromPlans = new();
    private readonly Dictionary<Rectangle, ElasticPlan> _toPlans = new();

    /// <param name="bounds">Where it goes, on the screen: the main window's client area.</param>
    /// <param name="toMain">Runs an action on the main window's thread - the done callback goes through it.</param>
    internal FocusStage(Rectangle bounds, Bitmap before, FocusScene scene, bool entering, Action<Action>? toMain = null, Action? done = null)
        : this(bounds, entering, toMain, done)
    {
        TakeBefore(before, scene);
    }

    /// <summary>A stage with no picture yet - made while the picture before is taken; see <see cref="Begin"/>.</summary>
    private FocusStage(Rectangle bounds, bool entering, Action<Action>? toMain, Action? done)
    {
        _scene = new FocusScene(0, 0, Rectangle.Empty, Array.Empty<FocusPiece>(), Rectangle.Empty, Rectangle.Empty);
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

        // Layered from the start, so the hand-over at the end is a change of
        // opacity rather than a switch of window kind mid-animation.
        Opacity = 0.995;

        _timer.Tick += (_, _) => Frame();
    }

    /// <summary>The picture before and where the pieces are in it - before the stage is shown.</summary>
    private void TakeBefore(Bitmap before, FocusScene scene)
    {
        _from = before;
        _fromBits = before.GetHbitmap();
        _scene = scene;
    }

    /// <summary>
    /// Puts a stage up over <paramref name="bounds"/> on a thread of its own
    /// and starts it at once. <paramref name="take"/> takes the picture before
    /// and the pieces, on the caller's thread, while the stage's window is
    /// made on its own - so the two take as long as the slower, not both
    /// added (25 Nov 2026: the picture 216-414 ms under load, the stage's
    /// window 58-122 ms after it). Null when it could not be put up, or
    /// <paramref name="take"/> gave nothing - the caller then simply changes the
    /// view. <paramref name="done"/> runs on the main window's thread, through
    /// <paramref name="toMain"/>, once it has gone.
    /// </summary>
    internal static FocusStage? Begin(Rectangle bounds, bool entering, Action<Action> toMain, Action done, Func<(Bitmap Before, FocusScene Scene)?> take)
    {
        FocusStage? stage = null;
        (Bitmap Before, FocusScene Scene)? taken = null;

        // Not disposed: the stage's thread signals them again as it ends,
        // after this has returned - disposed, that threw on the stage's thread.
        var up = new ManualResetEventSlim();
        var go = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
                stage = new FocusStage(bounds, entering, toMain, done);
                _ = stage.Handle;

                if (!go.Wait(TimeSpan.FromSeconds(2)) || taken is not { } picture)
                {
                    stage.Dispose();
                    stage = null;
                    return;
                }

                stage.TakeBefore(picture.Before, picture.Scene);
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

        try
        {
            taken = take();
        }
        finally
        {
            go.Set();
        }

        if (taken is null)
        {
            return null;
        }

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
    /// The picture after has been taken, and where every piece is in it.
    /// From any thread - it is handed to the stage's own.
    /// </summary>
    internal void Arrive(Bitmap after, FocusScene scene)
    {
        try
        {
            BeginInvoke(new Action(() => Prepare(after, scene, _ms)));
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

    /// <summary>The picture after and where the pieces are in it, <paramref name="at"/> ms in - Arrive, and the tests through Render.</summary>
    internal void Prepare(Bitmap after, FocusScene scene, double at)
    {
        _to = after;
        _toBits = after.GetHbitmap();
        _toPixels = PixelsOf(after);
        _scene = scene;
        _afterAt = at;

        // Every card's cut in the picture after, now, rather than in the
        // first frames of the glide.
        foreach (var piece in scene.Pieces)
        {
            if (piece.Kind == FocusPieceKind.Card && !piece.After.IsEmpty)
            {
                PlanOf(_toPlans, _toPixels, after.Width, piece.After);
            }
        }

        Invalidate();
    }

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

    private int FoldParts => _scene.Pieces.Count(p => p.Kind == FocusPieceKind.Folding);

    private void NextFrame()
    {
        _ms = _clock.Tick();

        // The cards' cuts in the picture before, one card a frame while the
        // sidebar moves - they are wanted only once the glide starts.
        if (_fromPixels is null)
        {
            _fromPixels = PixelsOf(_from);
        }
        else if (_scene.Pieces.FirstOrDefault(p => p.Kind == FocusPieceKind.Card && !p.Before.IsEmpty && !_fromPlans.ContainsKey(p.Before)) is { Before.IsEmpty: false } next)
        {
            PlanOf(_fromPlans, _fromPixels, _from.Width, next.Before);
        }

        var moment = FocusChoreography.At(_ms, _entering, _afterAt, FoldParts);
        var opacity = Math.Clamp(moment.StageAlpha, 0, 0.995);

        if (Math.Abs(Opacity - opacity) > 0.001)
        {
            Opacity = opacity;
        }

        // Never longer than a few seconds, whatever happened to the picture after.
        if (moment.Done || _ms > 6000)
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
        var m = FocusChoreography.At(ms, _entering, _afterAt, FoldParts);
        var haveAfter = _to is not null;
        var width = size.Width;
        var height = size.Height;

        // The sidebar's width now, and how far it has moved: the page rides
        // its edge, and the cards keep to it as they glide (FocusElastic.CardAt).
        var sidebar = (int)Math.Round(Lerp(_scene.NavFrom, _scene.NavTo, m.Sidebar));
        var ride = sidebar - _scene.NavFrom;
        var change = _scene.NavTo - _scene.NavFrom;

        // The hand-over is the stage fading over the real window, which is
        // already the window after: the final frame goes on being drawn, so
        // anything that is not a piece - the page's scroll bar, which comes
        // with Focus at 1440x900 - fades in over those 80 ms rather than
        // appearing at once.
        var hdc = g.GetHdc();

        try
        {
            var memory = CreateCompatibleDC(hdc);
            var old = SelectObject(memory, _fromBits);

            try
            {
                // 1. The window's own ground.
                using (var ground = Graphics.FromHdc(hdc))
                using (var fill = new SolidBrush(AppTheme.Background))
                {
                    ground.FillRectangle(fill, 0, 0, width, height);
                }

                // 2. The cards: each at its place now, its contents after under
                //    its contents before, the before fading over the glide.
                foreach (var piece in _scene.Pieces)
                {
                    if (piece.Kind != FocusPieceKind.Card || piece.Before.IsEmpty)
                    {
                        continue;
                    }

                    var at = FocusElastic.CardAt(piece.Before, piece.After, m.Glide, ride, change);

                    if (at.Width <= 0 || at.Height <= 0)
                    {
                        continue;
                    }

                    SelectClipRgnTo(hdc, at);

                    try
                    {
                        if (haveAfter && !piece.After.IsEmpty && m.Glide > 0)
                        {
                            if (m.Cross > 0)
                            {
                                SelectObject(memory, _toBits);
                                DrawElastic(hdc, memory, PlanOf(_toPlans, _toPixels!, _to!.Width, piece.After), at, 1);
                            }

                            if (m.Cross < 1)
                            {
                                SelectObject(memory, _fromBits);
                                DrawElastic(hdc, memory, BeforePlan(piece.Before), at, 1 - m.Cross);
                            }
                        }
                        else
                        {
                            // Only riding the sidebar's edge: the picture before, as it was.
                            SelectObject(memory, _fromBits);
                            BitBlt(hdc, at.X, at.Y, piece.Before.Width, piece.Before.Height, memory, piece.Before.X, piece.Before.Y, SRCCOPY);
                        }
                    }
                    finally
                    {
                        SelectClipRgn(hdc, IntPtr.Zero);
                    }
                }

                // 3. The column's parts: folding away one by one, drifting
                //    right (Focus), or coming back the same way (Restore).
                var drift = UiLayout.AtDpi(FocusChoreography.FoldDrift);

                foreach (var piece in _scene.Pieces)
                {
                    if (piece.Kind != FocusPieceKind.Folding)
                    {
                        continue;
                    }

                    // Going (in the picture before) or coming (in the picture
                    // after) - whichever way the press goes; the column's parts
                    // go on Focus and come back on Restore.
                    var going = !piece.Before.IsEmpty;
                    var gone = FocusChoreography.Fold(ms, piece.Order, entering: going, _afterAt);
                    var source = going ? piece.Before : piece.After;
                    var bits = going ? _fromBits : _toBits;

                    if (gone >= 1 || source.IsEmpty || bits == IntPtr.Zero)
                    {
                        continue;
                    }

                    SelectObject(memory, bits);
                    var shift = (going ? ride : 0) + (int)Math.Round(drift * gone);
                    Copy(hdc, memory, source, new Rectangle(source.X + shift, source.Y, source.Width, source.Height), 1 - gone);
                }

                // 4. The sidebar: the wide one cut off at the width it has got
                //    to, and the narrow one's own picture fading in over it as
                //    it arrives (Focus) or out as the wide one opens (Restore).
                var wideBits = _entering ? _fromBits : _toBits;
                var narrowBits = _entering ? _toBits : _fromBits;
                var narrowWidth = Math.Min(_scene.NavFrom, _scene.NavTo);

                if (sidebar > 0 && wideBits != IntPtr.Zero && (_entering || m.Sidebar > 0))
                {
                    SelectObject(memory, wideBits);
                    BitBlt(hdc, 0, 0, sidebar, height, memory, 0, 0, SRCCOPY);
                }

                if (narrowWidth > 0 && narrowBits != IntPtr.Zero && m.NarrowAlpha > 0)
                {
                    SelectObject(memory, narrowBits);
                    Copy(hdc, memory, new Rectangle(0, 0, narrowWidth, height), new Rectangle(0, 0, narrowWidth, height), m.NarrowAlpha);
                }

                // 5. The inbox, sliding in from the right (or out to it).
                if (!_scene.Inbox.IsEmpty && m.InboxIn > 0 && (!_entering || haveAfter))
                {
                    SelectObject(memory, _entering ? _toBits : _fromBits);
                    DrawInbox(hdc, memory, m, ms, width);
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

        // 6. On top: the inbox's shadow, the press, the landing light.
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (!_scene.Inbox.IsEmpty && m.InboxIn > 0 && (!_entering || haveAfter))
        {
            DrawShadow(g, InboxLeft(m, width), height, Math.Clamp(m.InboxIn, 0, 1));
        }

        if (m.Pressed && !_scene.Button.IsEmpty)
        {
            var button = _scene.Button;
            button.Offset(ride, 0);

            using var dark = new SolidBrush(Color.FromArgb(56, Color.Black));
            using var path = AppTheme.RoundedRect(button, UiLayout.AtDpi(10));
            g.FillPath(dark, path);
        }

        if (m.LightAlpha > 0 && !_scene.BoardTop.IsEmpty)
        {
            DrawLight(g, _scene.BoardTop, m.Light, m.LightAlpha);
        }
    }

    private ElasticPlan BeforePlan(Rectangle before) => PlanOf(_fromPlans, _fromPixels ??= PixelsOf(_from), _from.Width, before);

    private static ElasticPlan PlanOf(Dictionary<Rectangle, ElasticPlan> plans, int[] pixels, int width, Rectangle source)
    {
        if (!plans.TryGetValue(source, out var plan))
        {
            plan = FocusElastic.Analyse(pixels, width, source);
            plans[source] = plan;
        }

        return plan;
    }

    /// <summary>A piece drawn into <paramref name="to"/> as its plan cuts it - solid, or faded.</summary>
    private static void DrawElastic(IntPtr hdc, IntPtr memory, ElasticPlan plan, Rectangle to, double alpha)
    {
        foreach (var blit in FocusElastic.Map(plan, to))
        {
            Copy(hdc, memory, blit.From, blit.To, alpha);
        }
    }

    /// <summary>A part of the picture selected into <paramref name="memory"/>, to <paramref name="to"/> - the same size or stretched, solid or faded.</summary>
    private static void Copy(IntPtr hdc, IntPtr memory, Rectangle from, Rectangle to, double alpha)
    {
        var a = (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);

        if (a == 0 || from.Width <= 0 || from.Height <= 0 || to.Width <= 0 || to.Height <= 0)
        {
            return;
        }

        if (a == 255 && from.Size == to.Size)
        {
            BitBlt(hdc, to.X, to.Y, to.Width, to.Height, memory, from.X, from.Y, SRCCOPY);
        }
        else if (a == 255)
        {
            StretchBlt(hdc, to.X, to.Y, to.Width, to.Height, memory, from.X, from.Y, from.Width, from.Height, SRCCOPY);
        }
        else
        {
            AlphaBlend(hdc, to.X, to.Y, to.Width, to.Height, memory, from.X, from.Y, from.Width, from.Height, Blend(a));
        }
    }

    /// <summary>Where the inbox's left edge is now: off the right edge at 0, in its place at 1, a few pixels past it on the overshoot.</summary>
    private int InboxLeft(FocusMoment m, int width) =>
        _scene.Inbox.Left + (int)Math.Round((width - _scene.Inbox.Left) * (1 - m.InboxIn));

    /// <summary>
    /// The inbox: its ground first, then its contents in bands - entering,
    /// each band rising a few pixels as it fades up, one after another;
    /// leaving, its contents fading together as it goes.
    /// </summary>
    private void DrawInbox(IntPtr hdc, IntPtr memory, FocusMoment m, double ms, int width)
    {
        var inbox = _scene.Inbox;
        var x = InboxLeft(m, width);

        // Wide enough to cover the overshoot's few pixels at the right.
        var visible = Math.Min(inbox.Width + Math.Max(0, inbox.Left - x), width - x);

        if (visible <= 0)
        {
            return;
        }

        using (var ground = Graphics.FromHdc(hdc))
        using (var fill = new SolidBrush(AppTheme.Background))
        {
            ground.FillRectangle(fill, x, inbox.Top, visible, inbox.Height);
        }

        var shown = Math.Min(inbox.Width, width - x);

        if (!_entering)
        {
            Copy(hdc, memory, new Rectangle(inbox.Left, inbox.Top, shown, inbox.Height), new Rectangle(x, inbox.Top, shown, inbox.Height), m.InboxAlpha);
            return;
        }

        var band = Math.Max(UiLayout.AtDpi(56), 1);
        var rise = UiLayout.AtDpi(FocusChoreography.RowRise);

        for (int top = inbox.Top, index = 0; top < inbox.Bottom; top += band, index++)
        {
            var row = FocusChoreography.RowIn(ms, index, _afterAt);

            if (row <= 0)
            {
                continue;
            }

            var h = Math.Min(band, inbox.Bottom - top);
            var lift = (int)Math.Round(rise * (1 - row));
            Copy(hdc, memory, new Rectangle(inbox.Left, top, shown, h), new Rectangle(x, top + lift, shown, h), row);
        }
    }

    /// <summary>A soft shadow cast leftwards by the inbox's edge, as it arrives.</summary>
    private static void DrawShadow(Graphics g, int x, int height, double strength)
    {
        var spread = UiLayout.AtDpi(34);
        var a = (int)Math.Round(Math.Clamp(strength, 0, 1) * 150);
        var area = new Rectangle(x - spread, 0, spread, Math.Max(1, height));

        using var brush = new LinearGradientBrush(new Rectangle(area.X - 1, 0, area.Width + 2, area.Height),
            Color.FromArgb(0, Color.Black), Color.FromArgb(a, Color.Black), LinearGradientMode.Horizontal);
        g.FillRectangle(brush, area);
    }

    /// <summary>The landing light: one pink line, brightest at its leading end, crossing the board's top edge.</summary>
    private static void DrawLight(Graphics g, Rectangle top, double at, double strength)
    {
        var length = UiLayout.AtDpi(240);
        var x = top.Left - length + (int)Math.Round((top.Width + length) * at);
        var a = (int)Math.Round(Math.Clamp(strength, 0, 1) * 255);
        var thick = Math.Max(2, UiLayout.AtDpi(2));
        var state = g.Save();

        g.SetClip(new Rectangle(top.Left, top.Top - UiLayout.AtDpi(10), top.Width, UiLayout.AtDpi(20) + thick));

        var body = new Rectangle(x, top.Top, length, thick);

        using (var brush = new LinearGradientBrush(new Rectangle(body.X - 1, body.Y, body.Width + 2, body.Height),
                   Color.FromArgb(0, AppTheme.Primary), Color.FromArgb(a, AppTheme.Primary), LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, body);
        }

        for (var i = 1; i <= 3; i++)
        {
            var grow = UiLayout.AtDpi(i * 2);
            using var halo = new LinearGradientBrush(new Rectangle(body.X - 1, body.Y - grow, body.Width + 2, body.Height + grow * 2),
                Color.FromArgb(0, AppTheme.Primary), Color.FromArgb(a * 40 / 255, AppTheme.Primary), LinearGradientMode.Horizontal);
            g.FillRectangle(halo, new Rectangle(body.X, body.Y - grow, body.Width, body.Height + grow * 2));
        }

        g.Restore(state);
    }

    /// <summary>A picture's pixels, row by row, for FocusElastic.</summary>
    internal static int[] PixelsOf(Bitmap picture)
    {
        var area = new Rectangle(0, 0, picture.Width, picture.Height);
        var data = picture.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);

        try
        {
            var pixels = new int[picture.Width * picture.Height];

            for (var y = 0; y < picture.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * picture.Width, picture.Width);
            }

            // Only the colour: the fourth byte of a 32bpp RGB picture is not alpha and may be anything.
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] &= 0x00FFFFFF;
            }

            return pixels;
        }
        finally
        {
            picture.UnlockBits(data);
        }
    }

    private static void SelectClipRgnTo(IntPtr hdc, Rectangle area)
    {
        var region = CreateRectRgn(area.Left, area.Top, area.Right, area.Bottom);
        SelectClipRgn(hdc, region);
        DeleteObject(region);
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
            _from?.Dispose();
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

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int SelectClipRgn(IntPtr hdc, IntPtr region);

    [DllImport("msimg32.dll")]
    private static extern bool AlphaBlend(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, BLENDFUNCTION blend);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}
