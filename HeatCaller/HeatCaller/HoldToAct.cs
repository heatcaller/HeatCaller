using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// The timing of a press-and-hold (25 Oct 2026), with no window in it, so
/// it can be tested on its own: pressed at a moment, done once it has been
/// held for long enough, and called off by letting go before then.
///
/// Completes once only. A hold that has completed is not "cancelled" by the
/// let-go that follows it, and needs a fresh press to go again.
/// </summary>
internal sealed class HoldGesture
{
    private long? _pressedAt;
    private bool _done;

    public HoldGesture(long durationMs)
    {
        DurationMs = Math.Max(1, durationMs);
    }

    /// <summary>How long it has to be held, in milliseconds.</summary>
    public long DurationMs { get; }

    /// <summary>Pressed, and not yet done or let go.</summary>
    public bool Holding => _pressedAt is not null && !_done;

    /// <summary>Starts a hold at <paramref name="nowMs"/>. A press during a hold starts it again.</summary>
    public void Press(long nowMs)
    {
        _pressedAt = nowMs;
        _done = false;
    }

    /// <summary>
    /// Let go. True when that called a hold off - it was being held and had
    /// not yet done its job; false when there was nothing to call off.
    /// </summary>
    public bool Release()
    {
        var calledOff = Holding;

        _pressedAt = null;
        _done = false;

        return calledOff;
    }

    /// <summary>How far through the hold, 0 to 1, or null when nothing is being held.</summary>
    public float? ProgressAt(long nowMs)
    {
        if (!Holding || _pressedAt is not { } pressed)
        {
            return null;
        }

        return Math.Clamp((float)(nowMs - pressed) / DurationMs, 0f, 1f);
    }

    /// <summary>True exactly once: the first time it is asked after being held for long enough.</summary>
    public bool TryComplete(long nowMs)
    {
        if (!Holding || _pressedAt is not { } pressed || nowMs - pressed < DurationMs)
        {
            return false;
        }

        _done = true;
        return true;
    }
}

/// <summary>
/// HOLD TO ACT (25 Oct 2026). Turns a button into one that has to be pressed
/// and held for about a second before it does anything - for the three on
/// Race Control's flag row that cannot be taken back in the middle of a
/// race: Red, Checkered and Reset. Let go early and nothing happens.
///
/// The button's Click is NOT wired to the action: a click is a hold that
/// was let go too soon. The keys (F5, F6, F7) and the phone remote call the
/// same action straight away, as before - holding a key down repeats it,
/// which is no way to time a hold.
///
/// Draws a small clock beside the caption so the button says it wants
/// holding, and a bar along its foot while it is held. The strip under the
/// row says the rest - see <see cref="HoldStrip"/>.
/// </summary>
internal sealed class HoldToAct : IDisposable, IShownHold
{
    /// <summary>How long a hold takes.</summary>
    internal static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(1);

    private readonly Button _button;
    private readonly Action _act;
    private readonly HoldGesture _gesture = new((long)HoldFor.TotalMilliseconds);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private bool _disposed;

    /// <param name="doing">What the hold does, in the words "Keep holding to …" finishes with: "throw the red flag".</param>
    public HoldToAct(Button button, string doing, Action act)
    {
        _button = button;
        Doing = doing;
        _act = act;

        _timer.Tick += (_, _) => Tick();

        button.MouseDown += OnMouseDown;
        button.MouseUp += OnMouseUp;
        button.MouseMove += OnMouseMove;
        button.MouseCaptureChanged += OnCaptureChanged;
        button.KeyDown += OnKeyDown;
        button.VisibleChanged += OnGoneAway;
        button.EnabledChanged += OnGoneAway;
        button.ParentChanged += OnGoneAway;
        button.HandleCreated += OnHandleCreated;

        if (button.IsHandleCreated)
        {
            TurnOffPressAndHold(button.Handle);
        }

        if (string.IsNullOrWhiteSpace(button.AccessibleDescription))
        {
            button.AccessibleDescription = "Press and hold for about a second.";
        }
    }

    /// <summary>What the hold does: "throw the red flag".</summary>
    public string Doing { get; }

    /// <summary>How far through the hold, 0 to 1, or null when it is not being held.</summary>
    public float? Progress => _gesture.ProgressAt(Environment.TickCount64);

    /// <summary>The hold has moved on, started, finished or been let go. On the UI thread.</summary>
    public event Action<HoldToAct>? Changed;

    /// <summary>Let go too soon, so nothing was done. On the UI thread.</summary>
    public event Action<HoldToAct>? CalledOff;

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_button.Enabled || !_button.Visible)
        {
            return;
        }

        _gesture.Press(Environment.TickCount64);

        // Held for the whole press, so the let-go and a drag off the button
        // reach it wherever the mouse is by then. A button takes the mouse
        // as it is pressed anyway; asked for here so it never depends on that.
        _button.Capture = true;

        _timer.Start();
        Moved();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            LetGo();
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        // Dragged off the button: the same as letting go, as it is for a
        // click - a press can always be backed out of that way.
        if (_gesture.Holding && !_button.ClientRectangle.Contains(e.Location))
        {
            LetGo();
        }
    }

    private void OnCaptureChanged(object? sender, EventArgs e)
    {
        // Only the capture going: a button takes it as it is pressed, and
        // that is not a let-go. A dialog or another window taking the mouse
        // away mid-hold is.
        if (!_button.Capture)
        {
            LetGo();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            LetGo();
        }
    }

    // Hidden, greyed out or taken off the page mid-hold - the row moved on
    // under the press (the race finished while Red was held, say). Called
    // off without "let go too soon": the director did nothing wrong.
    private void OnGoneAway(object? sender, EventArgs e) => LetGo(quietly: true);

    private void OnHandleCreated(object? sender, EventArgs e) => TurnOffPressAndHold(_button.Handle);

    private void Tick()
    {
        if (_disposed)
        {
            _timer.Stop();
            return;
        }

        // Belt and braces, in case a let-go never reached the button (another
        // window took the mouse without a capture change, say): no hold
        // completes once the button is up, or the pointer has left it.
        if (_gesture.Holding
            && ((Control.MouseButtons & MouseButtons.Left) == 0
                || !_button.ClientRectangle.Contains(_button.PointToClient(Cursor.Position))))
        {
            LetGo();
            return;
        }

        if (_gesture.TryComplete(Environment.TickCount64))
        {
            _timer.Stop();
            Moved();

            // After the bar has gone, so a dialog the action puts up (practice
            // is running, say) does not sit over a full bar.
            _act();
            return;
        }

        if (!_gesture.Holding)
        {
            _timer.Stop();
        }

        Moved();
    }

    /// <summary>
    /// Calls a hold off, if one is under way. Safe to call at any time.
    /// <paramref name="quietly"/> leaves <see cref="CalledOff"/> unraised, for
    /// a hold the page took away rather than one let go too soon.
    /// </summary>
    public void LetGo(bool quietly = false)
    {
        var calledOff = _gesture.Release();
        _timer.Stop();

        if (calledOff)
        {
            Moved();

            if (!quietly)
            {
                CalledOff?.Invoke(this);
            }
        }
    }

    private void Moved()
    {
        if (_disposed)
        {
            return;
        }

        _button.Invalidate();
        Changed?.Invoke(this);
    }

    /// <summary>
    /// The clock beside the caption, and the bar along the foot while held -
    /// drawn over whatever the button has painted. <paramref name="ink"/> is
    /// the caption's own colour, so both read on whatever the face is;
    /// <paramref name="halo"/>, when given, goes round them, for a face of
    /// black and white squares.
    /// </summary>
    public void PaintOver(Graphics g, Rectangle bounds, Color ink, Color? halo)
    {
        var font = _button.Font;
        var size = Math.Max(8, (int)Math.Round(font.Height * 0.55));

        var caption = TextRenderer.MeasureText(g, _button.Text ?? string.Empty, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        // To the left of the caption, which stays where it was - centred.
        // Where the button is too narrow for that, in its top right corner.
        var gap = Math.Max(3, size / 2);
        var left = bounds.Left + (bounds.Width - caption.Width) / 2 - gap - size;
        var top = bounds.Top + (bounds.Height - size) / 2;

        if (left < bounds.Left + 3)
        {
            left = bounds.Right - size - 4;
            top = bounds.Top + 4;
        }

        var dial = new Rectangle(left, top, size, size);
        var stroke = Math.Max(1.4f, size / 7f);

        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (halo is { } around)
        {
            using var haloPen = new Pen(around, stroke + 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            DrawClock(g, dial, haloPen);
        }

        using (var pen = new Pen(ink, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            DrawClock(g, dial, pen);
        }

        g.Restore(saved);

        if (Progress is { } done)
        {
            var height = Math.Max(3, font.Height / 5);
            var foot = new Rectangle(bounds.Left, bounds.Bottom - height, bounds.Width, height);

            using (var under = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            {
                g.FillRectangle(under, foot);
            }

            var width = (int)Math.Round(foot.Width * done);

            if (width > 0)
            {
                using var fill = new SolidBrush(ink);
                g.FillRectangle(fill, new Rectangle(foot.Left, foot.Top, width, foot.Height));
            }
        }
    }

    private static void DrawClock(Graphics g, Rectangle dial, Pen pen)
    {
        g.DrawEllipse(pen, dial);

        var cx = dial.Left + dial.Width / 2f;
        var cy = dial.Top + dial.Height / 2f;

        // Hands at ten past twelve, as the clock mark on the mock-up.
        g.DrawLine(pen, cx, cy, cx, dial.Top + dial.Height * 0.22f);
        g.DrawLine(pen, cx, cy, cx + dial.Width * 0.22f, cy + dial.Height * 0.12f);
    }

    // --- Touch screens --------------------------------------------------------
    //
    // On a touch screen, Windows turns a finger held still into a right-click
    // and holds the press back until it knows which it is - so a hold would
    // never reach the button as a press. Turned off for these buttons alone
    // with the window property Windows reads for it (TABLET_DISABLE_PRESSANDHOLD).
    // Harmless on a machine with no touch screen.

    private const string PenServiceProperty = "MicrosoftTabletPenServiceProperty";
    private const int TabletDisablePressAndHold = 0x00000001;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProp(IntPtr hWnd, string lpString, IntPtr hData);

    private static void TurnOffPressAndHold(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            SetProp(handle, PenServiceProperty, new IntPtr(TabletDisablePressAndHold));
        }
        catch
        {
            // No such call on this system - the mouse still works.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();

        _button.MouseDown -= OnMouseDown;
        _button.MouseUp -= OnMouseUp;
        _button.MouseMove -= OnMouseMove;
        _button.MouseCaptureChanged -= OnCaptureChanged;
        _button.KeyDown -= OnKeyDown;
        _button.VisibleChanged -= OnGoneAway;
        _button.EnabledChanged -= OnGoneAway;
        _button.ParentChanged -= OnGoneAway;
        _button.HandleCreated -= OnHandleCreated;
    }
}
