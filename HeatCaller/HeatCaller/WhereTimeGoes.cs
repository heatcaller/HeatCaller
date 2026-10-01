using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// Notes how long each part of opening a slow window takes, for the session
/// log and the layout test's report - so a window that is slow to open says
/// which part is slow, rather than leaving it to be guessed.
///
/// Why: the Setup Sheet took 4 to 5 seconds to open, and holding its layout
/// while it was built (LayoutFreeze.HoldWhileBuilding) changed nothing - the
/// time was not where it was thought to be (Asher, 1 Oct). A window started
/// here (Start) marks the end of each part of its building (Mark); DialogFit
/// marks its sizing; the theme pass adds up its time by the kind of control
/// it styled while one is being timed (Styled). The whole is written to the
/// session log once the window is on screen (LogWhenShown), when it took
/// half a second or more, and handed to the layout test (Take).
///
/// One window at a time, on the thread that builds it. Nothing is kept once
/// it has been taken.
/// </summary>
internal static class WhereTimeGoes
{
    /// <summary>How long a window may take to open before the session log says how the time went.</summary>
    private const double SlowSeconds = 0.5;

    [ThreadStatic]
    private static Clock? _timing;

    /// <summary>True while a window is being timed on this thread.</summary>
    public static bool Active => _timing is not null;

    /// <summary>
    /// Starts timing a window called <paramref name="name"/> - ending any
    /// before it. Each part then also says how many of
    /// <paramref name="window"/>'s controls have a window handle by its end:
    /// a handle made early is made again whenever a style it was made with
    /// changes, which the theme pass does to every drop-down.
    /// </summary>
    public static void Start(string name, Control? window = null)
    {
        _timing?.StopWatchingWindows();
        _timing = new Clock(name, window);
    }

    /// <summary>
    /// The window being timed is built - its constructor is done. Windows
    /// made from here on are the ones showing it makes; until here, any
    /// made were made early (see Clock.WatchWindowsMade).
    /// </summary>
    public static void Built() => _timing?.StopWatchingWindows();

    /// <summary>Notes that the part called <paramref name="part"/> has just ended.</summary>
    public static void Mark(string part) => _timing?.Mark(part);

    /// <summary>Adds <paramref name="elapsed"/> to the theme pass's time for controls of <paramref name="kind"/> (AppTheme.ApplyRecursively).</summary>
    public static void Styled(Type kind, TimeSpan elapsed) => _timing?.Styled(kind.Name, elapsed);

    /// <summary>Adds <paramref name="elapsed"/> to the time noted under <paramref name="what"/> - a step inside styling one kind of control, say.</summary>
    public static void Styled(string what, TimeSpan elapsed) => _timing?.Styled(what, elapsed);

    /// <summary>
    /// Times what runs until the result is disposed, and adds it, and one
    /// call, under <paramref name="what"/> - for the layout pieces' own
    /// layouts and measuring, which call one another, so each counts the
    /// time of what it called too. Nothing while no window is being timed.
    /// </summary>
    public static Span Measure(string what) => _timing is null ? default : new Span(what, Stopwatch.GetTimestamp());

    /// <summary>A stretch of time being measured - see Measure.</summary>
    public readonly struct Span : IDisposable
    {
        private readonly string? _what;
        private readonly long _started;

        public Span(string what, long started)
        {
            _what = what;
            _started = started;
        }

        public void Dispose()
        {
            if (_what is not null)
            {
                _timing?.Called(_what, Stopwatch.GetElapsedTime(_started));
            }
        }
    }

    /// <summary>
    /// Writes the timing to the session log once <paramref name="window"/> is
    /// first shown - its sizing (DialogFit, on Load) and first painting
    /// counted - when it took long enough to be worth saying.
    /// </summary>
    public static void LogWhenShown(Form window)
    {
        if (_timing is not { } timing)
        {
            return;
        }

        window.Shown += (_, _) =>
        {
            if (!ReferenceEquals(_timing, timing))
            {
                return;
            }

            timing.Mark("showing");
            timing.StopWatchingWindows();

            if (timing.Total.TotalSeconds >= SlowSeconds)
            {
                SessionLog.Write($"[ui] {timing.Describe()}");
            }

            _timing = null;
        };
    }

    /// <summary>The timing so far, described, and finished with - null when nothing was being timed.</summary>
    public static string? Take()
    {
        var timing = _timing;
        _timing = null;

        timing?.StopWatchingWindows();
        return timing?.Describe();
    }

    private sealed class Clock
    {
        private readonly string _name;
        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly Stopwatch _part = Stopwatch.StartNew();
        private readonly List<(string Part, TimeSpan Took)> _parts = new();
        private readonly Dictionary<string, TimeSpan> _styled = new();
        private readonly Dictionary<string, (TimeSpan Took, int Calls)> _called = new();

        /// <summary>The same as _called, for the part under way only - so a slow part says what its time went on.</summary>
        private readonly Dictionary<string, (TimeSpan Took, int Calls)> _calledInPart = new();
        private readonly Control? _window;

        public Clock(string name, Control? window)
        {
            _name = name;
            _window = window;

            // Who made the window's handle, when it is made before the
            // window is shown: everything added to a window that has its
            // handle gets one at once, and every style change after that
            // makes it again - the Setup Sheet's 360 handles were all made
            // while it was being built (1 Oct).
            if (window is { IsHandleCreated: false })
            {
                void Made(object? sender, EventArgs e)
                {
                    window.HandleCreated -= Made;
                    _madeBy = MadeBy();
                    StopWatchingWindows();
                }

                window.HandleCreated += Made;
                window.Disposed += (_, _) => StopWatchingWindows();

                WatchWindowsMade();
            }
        }

        /// <summary>
        /// Watches for windows made on this thread while the window is being
        /// built - before its own handle, and before its constructor is done
        /// (Built) - and keeps who made the first few.
        ///
        /// Why: with the Setup Sheet's own handle no longer made early, its
        /// 26 controls still had handles by the time its fields were added,
        /// and the 330 controls added after got theirs as they went in (1
        /// Oct) - made by something that ran before they were in the window,
        /// so watching the window itself (Made, above) could not say what.
        /// Windows says, as each window is made (a WH_CBT hook, this thread
        /// only, for as long as the building lasts).
        /// </summary>
        private void WatchWindowsMade()
        {
            try
            {
                _onWindowMade = WindowMade;
                _hook = SetWindowsHookEx(WhCbt, _onWindowMade, IntPtr.Zero, GetCurrentThreadId());
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Not on Windows: nothing to watch.
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr WindowMade(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code == HcbtCreateWnd)
            {
                _windowsMadeEarly++;

                if (_earlyMadeBy.Count < 3)
                {
                    try
                    {
                        var by = MadeBy();

                        if (!_earlyMadeBy.Contains(by))
                        {
                            _earlyMadeBy.Add(by);
                        }
                    }
                    catch (Exception)
                    {
                        // Only a note for the log; never worth breaking a
                        // window being made.
                    }
                }
            }

            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        /// <summary>Stops watching for windows made (WatchWindowsMade). Safe to call more than once.</summary>
        public void StopWatchingWindows()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr _hook;

        /// <summary>Kept for as long as the hook is in: Windows calls it, and nothing else holds it.</summary>
        private HookProc? _onWindowMade;

        private int _windowsMadeEarly;
        private readonly List<string> _earlyMadeBy = new();

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        private const int WhCbt = 5;
        private const int HcbtCreateWnd = 3;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        /// <summary>The methods of this app that led to the window's handle being made, innermost first - null until it is.</summary>
        private string? _madeBy;

        private static string MadeBy()
        {
            // Every frame, Windows Forms' own included, from the one that
            // asked for the handle out to this app's first: which call it was
            // is in Windows Forms' frames, as often as not (1 Oct: the frames
            // of this app alone said only "the constructor").
            var frames = new StackTrace(2, false).GetFrames();
            var names = new List<string>();
            var ours = 0;
            var repeats = 1;

            foreach (var frame in frames)
            {
                if (frame.GetMethod() is not { DeclaringType: { } type } method
                    || type == typeof(WhereTimeGoes) || type.DeclaringType == typeof(WhereTimeGoes))
                {
                    continue;
                }

                var name = $"{type.Name}.{method.Name}";

                // A control making its children's windows calls itself once
                // for each level down: said once, with how many times.
                if (names.Count > 0 && names[^1].StartsWith(name, StringComparison.Ordinal)
                    && (names[^1].Length == name.Length || names[^1][name.Length] == ' '))
                {
                    names[^1] = $"{name} x{++repeats}";
                }
                else
                {
                    repeats = 1;
                    names.Add(name);
                }

                if (type.Namespace?.StartsWith("HeatCaller", StringComparison.Ordinal) == true && ++ours == 3)
                {
                    break;
                }

                if (names.Count == 40)
                {
                    break;
                }
            }

            return string.Join(" <- ", names);
        }

        public TimeSpan Total => _total.Elapsed;

        public void Mark(string part)
        {
            var took = _part.Elapsed;

            // What a slow part's time went on, among the layout pieces.
            var spent = took.TotalSeconds < 0.1 || _calledInPart.Count == 0
                ? string.Empty
                : " [" + string.Join(", ", _calledInPart
                    .OrderByDescending(what => what.Value.Took)
                    .Take(4)
                    .Select(what => $"{what.Key} {what.Value.Took.TotalSeconds:0.00} x{what.Value.Calls}")) + "]";

            _calledInPart.Clear();

            _parts.Add((_window is null ? $"{part}{spent}" : $"{part} ({Handles(_window)} handles){spent}", took));

            // Counting the handles is not this part's time.
            _part.Restart();
        }

        public void Styled(string what, TimeSpan elapsed)
        {
            _styled[what] = _styled.TryGetValue(what, out var before) ? before + elapsed : elapsed;
        }

        public void Called(string what, TimeSpan elapsed)
        {
            _called[what] = _called.TryGetValue(what, out var before)
                ? (before.Took + elapsed, before.Calls + 1)
                : (elapsed, 1);

            _calledInPart[what] = _calledInPart.TryGetValue(what, out var inPart)
                ? (inPart.Took + elapsed, inPart.Calls + 1)
                : (elapsed, 1);
        }

        private static int Handles(Control control)
        {
            var count = control.IsHandleCreated ? 1 : 0;

            foreach (Control child in control.Controls)
            {
                count += Handles(child);
            }

            return count;
        }

        public string Describe()
        {
            var parts = string.Join(", ", _parts.Select(part => $"{part.Part} {part.Took.TotalSeconds:0.00}"));

            var styled = _styled.Count == 0
                ? string.Empty
                : "; theme pass by kind: " + string.Join(", ", _styled
                    .OrderByDescending(kind => kind.Value)
                    .Take(10)
                    .Select(kind => $"{kind.Key} {kind.Value.TotalSeconds:0.00}"));

            var called = _called.Count == 0
                ? string.Empty
                : "; layout by piece (time with what it called, calls): " + string.Join(", ", _called
                    .OrderByDescending(what => what.Value.Took)
                    .Take(16)
                    .Select(what => $"{what.Key} {what.Value.Took.TotalSeconds:0.00} x{what.Value.Calls}"));

            var made = _madeBy is null ? string.Empty : $"; the window's handle was made by: {_madeBy}";

            var early = _windowsMadeEarly == 0
                ? string.Empty
                : $"; {_windowsMadeEarly} windows made while it was being built, before its own - "
                    + string.Join("; and by: ", _earlyMadeBy.Select((by, i) => i == 0 ? $"the first by: {by}" : by));

            return $"{_name} took {_total.Elapsed.TotalSeconds:0.0}s to open - {parts}{styled}{called}{made}{early}.";
        }
    }
}
