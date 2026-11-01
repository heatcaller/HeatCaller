using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// A hold the On Air bar can show - a button's (HoldToAct) or a key's
/// (KeyHoldToAct): what it does, and how far through it is.
/// </summary>
internal interface IShownHold
{
    /// <summary>What the hold does, in the words "Keep holding to …" finishes with: "skip On Air and stage now".</summary>
    string Doing { get; }

    /// <summary>How far through the hold, 0 to 1, or null when it is not being held.</summary>
    float? Progress { get; }
}

/// <summary>
/// The timing of a KEY held down (1 Nov 2026), with no window in it, so it
/// can be tested on its own - HoldGesture, with the one thing a key adds:
/// Windows repeats a key that is held down, so every KeyDown after the first
/// is the same press, not a new one. HoldGesture.Press restarts a hold, so
/// fed the repeats directly it would never complete; this lets only the
/// first one through, until the key comes up.
/// </summary>
internal sealed class KeyHoldGesture
{
    private readonly HoldGesture _gesture;
    private bool _down;

    public KeyHoldGesture(long durationMs)
    {
        _gesture = new HoldGesture(durationMs);
    }

    /// <summary>How long it has to be held, in milliseconds.</summary>
    public long DurationMs => _gesture.DurationMs;

    /// <summary>The key is down - held, or pressed for something that was not a hold.</summary>
    public bool Down => _down;

    /// <summary>Held, and not yet done or let go.</summary>
    public bool Holding => _gesture.Holding;

    /// <summary>
    /// The key came down. True when this is a new press - the key was up -
    /// and false for Windows repeating a key already down, which is ignored.
    /// <paramref name="hold"/>: start timing a hold. False for a press that
    /// does its job at once (a tap); it is still remembered as down, so its
    /// repeats do not start a hold either.
    /// </summary>
    public bool Press(long nowMs, bool hold)
    {
        if (_down)
        {
            return false;
        }

        _down = true;

        if (hold)
        {
            _gesture.Press(nowMs);
        }

        return true;
    }

    /// <summary>
    /// The key came up. True when that called a hold off - it was held and
    /// had not yet done its job.
    /// </summary>
    public bool Release()
    {
        _down = false;
        return _gesture.Release();
    }

    /// <summary>
    /// The hold is dropped without the key coming up - the thing it was for
    /// is over. The key is still down, so its repeats still start nothing.
    /// </summary>
    public void Drop() => _gesture.Release();

    /// <summary>How far through the hold, 0 to 1, or null when nothing is being held.</summary>
    public float? ProgressAt(long nowMs) => _gesture.ProgressAt(nowMs);

    /// <summary>True exactly once: the first time it is asked after being held for long enough.</summary>
    public bool TryComplete(long nowMs) => _gesture.TryComplete(nowMs);
}

/// <summary>
/// HOLD A KEY TO ACT (1 Nov 2026, asked for by Asher: "I think you should
/// have to press and hold to skip"). The key's half of HoldToAct: a key that
/// has to be held for <see cref="HoldToAct.HoldFor"/> - the same second as
/// Red, Checkered and Reset - before it does anything. Let go early and
/// nothing happens, and the On Air bar says so.
///
/// Fed by the form's KeyDown and KeyUp (MainForm.Keyboard), which is why it
/// times the hold itself rather than counting repeats: how fast a key
/// repeats is a Windows setting.
/// </summary>
internal sealed class KeyHoldToAct : IShownHold, IDisposable
{
    private readonly KeyHoldGesture _gesture = new((long)HoldToAct.HoldFor.TotalMilliseconds);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Func<bool> _stillWanted;
    private readonly Action _act;
    private readonly Keys[] _keys;
    private bool _disposed;

    /// <param name="doing">What the hold does, in the words "Keep holding to …" finishes with.</param>
    /// <param name="stillWanted">Whether there is still something for the hold to do. Checked as it is timed: once false, the hold is dropped quietly.</param>
    /// <param name="act">Done once the key has been held long enough.</param>
    /// <param name="keys">The keys that count as this one - checked as it is timed, in case the let-go never reached the form.</param>
    public KeyHoldToAct(string doing, Func<bool> stillWanted, Action act, params Keys[] keys)
    {
        Doing = doing;
        _stillWanted = stillWanted;
        _act = act;
        _keys = keys;
        _timer.Tick += (_, _) => Tick();
    }

    public string Doing { get; }

    public float? Progress => _gesture.ProgressAt(Environment.TickCount64);

    /// <summary>The hold has moved on, started, finished or been let go. On the UI thread.</summary>
    public event Action<KeyHoldToAct>? Changed;

    /// <summary>Let go too soon, so nothing was done. On the UI thread.</summary>
    public event Action<KeyHoldToAct>? CalledOff;

    /// <summary>
    /// The key came down. <paramref name="hold"/>: this press is to be held
    /// (there is something to skip). True when it was a new press, false for
    /// a repeat of one already down.
    /// </summary>
    public bool KeyDown(bool hold)
    {
        if (_disposed || !_gesture.Press(Environment.TickCount64, hold))
        {
            return false;
        }

        if (hold)
        {
            _timer.Start();
            Moved();
        }

        return true;
    }

    /// <summary>The key came up - early, it calls the hold off. Safe at any time.</summary>
    public void KeyUp() => LetGo();

    private void Tick()
    {
        if (_disposed)
        {
            _timer.Stop();
            return;
        }

        // Nothing left to skip (On Air finished by itself mid-hold): dropped
        // quietly - the race director did nothing wrong.
        if (_gesture.Holding && !_stillWanted())
        {
            _gesture.Drop();
            _timer.Stop();
            Moved();
            return;
        }

        // Belt and braces, as HoldToAct does with the mouse: a let-go that
        // never reached the form (another window took the keyboard) must
        // not complete a hold.
        if (_gesture.Holding && !AnyKeyDown())
        {
            LetGo(quietly: true);
            return;
        }

        if (_gesture.TryComplete(Environment.TickCount64))
        {
            _timer.Stop();
            Moved();

            // After the bar has gone, as HoldToAct does.
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
    /// The key is up, and any hold under way is called off - raising
    /// <see cref="CalledOff"/> unless <paramref name="quietly"/>. Safe at any
    /// time; the next press is a new one.
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
        if (!_disposed)
        {
            Changed?.Invoke(this);
        }
    }

    private bool AnyKeyDown()
    {
        try
        {
            return _keys.Any(key => (GetAsyncKeyState((int)key) & 0x8000) != 0);
        }
        catch (Exception)
        {
            // Not Windows, or the call failed: trust the form's KeyUp.
            return true;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
