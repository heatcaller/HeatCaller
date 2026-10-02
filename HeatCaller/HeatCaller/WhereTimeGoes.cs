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
    /// Starts timing something done to a window that is up already - its
    /// pages' windows being made (MainForm.WarmPages) - watching the windows
    /// made meanwhile as Clock.MadeWhileShowing does. Ended by Take.
    /// </summary>
    public static void StartOnShownWindow(string name)
    {
        _timing?.StopWatchingWindows();
        _timing = new Clock(name, window: null);
        _timing.WatchWindowsMadeFromNow();
    }

    /// <summary>
    /// The window being timed is built - its constructor is done. Windows
    /// made from here on are the ones showing it makes; until here, any
    /// made were made early (see Clock.WatchWindowsMade).
    /// </summary>
    public static void Built() => _timing?.BuildingDone();

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
                }

                window.HandleCreated += Made;
                window.Disposed += (_, _) => StopWatchingWindows();

                WatchWindowsMade();
            }
        }

        /// <summary>
        /// Watches for windows made on this thread while the window is being
        /// built - before its own handle, and before its constructor is done
        /// (Built) - and keeps who made the first few. And then, until it is
        /// shown, how long each kind of control took to get its window, and
        /// which messages were sent meanwhile, and how often (see Showing).
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

                _onMessageSent = MessageSent;
                _messageHook = SetWindowsHookEx(WhCallWndProc, _onMessageSent, IntPtr.Zero, GetCurrentThreadId());

                _onMessageHandled = MessageHandled;
                _handledHook = SetWindowsHookEx(WhCallWndProcRet, _onMessageHandled, IntPtr.Zero, GetCurrentThreadId());
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Not on Windows: nothing to watch.
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr WindowMade(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code == HcbtCreateWnd && !_building)
            {
                try
                {
                    MadeWhileShowing(wParam);
                }
                catch (Exception)
                {
                    // Only a note for the log.
                }
            }
            else if (code == HcbtCreateWnd)
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

        /// <summary>
        /// A window made while the window is being shown: the time since the
        /// one before was made is put down to that one's kind of control -
        /// its window, and whatever its window being made set off, until the
        /// next was begun. Known only now: Windows Forms ties a window to its
        /// control after this hook has been told of it.
        ///
        /// Why: once nothing was made early, showing the Setup Sheet took 4.5
        /// to 5 seconds, none of it in laying it out (1 Oct) - so what does
        /// making its 364 windows cost, and where?
        /// </summary>
        private void MadeWhileShowing(IntPtr window)
        {
            var now = Stopwatch.GetTimestamp();

            if (_lastMade is { } last)
            {
                var took = Stopwatch.GetElapsedTime(last.At, now);
                var kind = KindOf(last.Window);

                _madeByKind[kind] = _madeByKind.TryGetValue(kind, out var before)
                    ? (before.Took + took, before.Count + 1)
                    : (took, 1);

                _longestMakes.Add((took, kind, last.Trace));
                _longestMakes.Sort((a, b) => b.Took.CompareTo(a.Took));

                if (_longestMakes.Count > 3)
                {
                    _longestMakes.RemoveAt(3);
                }
            }
            else
            {
                _firstMadeShowing = now;
            }

            _windowsMadeShowing++;
            _lastMade = (window, now, new StackTrace(2, false));
        }

        /// <summary>
        /// The kind of control a window is - or, for a window a control makes
        /// for itself (a drop-down's list), its control's, as its own - and
        /// the page it is on: the nearest UserControl round it.
        /// </summary>
        private static string KindOf(IntPtr window)
        {
            var control = Control.FromHandle(window);
            var own = string.Empty;

            if (control is null && Control.FromHandle(GetParent(window)) is { } maker)
            {
                control = maker;
                own = "'s own window";
            }

            if (control is null)
            {
                return "(not a control)";
            }

            for (var at = control.Parent; at is not null; at = at.Parent)
            {
                if (at is UserControl page)
                {
                    return $"{control.GetType().Name}{own} in {page.GetType().Name}";
                }
            }

            return control.GetType().Name + own;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        /// <summary>Counts each message sent to a window on this thread while the window is being shown.</summary>
        private IntPtr MessageSent(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !_building && lParam != IntPtr.Zero)
            {
                // CWPSTRUCT: lParam, wParam, message, hwnd.
                var message = Marshal.ReadInt32(lParam, 2 * IntPtr.Size);

                _messages[message] = _messages.TryGetValue(message, out var count) ? count + 1 : 1;

                if (message == WmWindowPosChanging)
                {
                    try
                    {
                        Moving(Marshal.ReadIntPtr(lParam, 3 * IntPtr.Size));
                    }
                    catch (Exception)
                    {
                        // Only a note for the log.
                    }
                }

                _inMessages.Push((message, Stopwatch.GetTimestamp(), 0));
            }

            return CallNextHookEx(_messageHook, code, wParam, lParam);
        }

        private const int WmWindowPosChanging = 0x0046;

        /// <summary>
        /// A window about to be moved, sized or put in front: counted by the
        /// kind of control it is, and every 100th, who is moving it - a
        /// sample, as a profiler takes one, of what all the moving is for.
        ///
        /// Why: with the Setup Sheet's layout held while its windows were
        /// made, there were still 7,300 of these as they were made, three
        /// seconds of handling (1 Oct) - none of them from its layout.
        /// </summary>
        private void Moving(IntPtr window)
        {
            var kind = Control.FromHandle(window)?.GetType().Name ?? "(not a control)";
            _movedByKind[kind] = _movedByKind.TryGetValue(kind, out var count) ? count + 1 : 1;

            if (++_moves % 100 != 0)
            {
                return;
            }

            var by = $"{kind}: {MadeBy(new StackTrace(2, false))}";
            _movedBy[by] = _movedBy.TryGetValue(by, out var seen) ? seen + 1 : 1;
        }

        private int _moves;
        private readonly Dictionary<string, int> _movedByKind = new();
        private readonly Dictionary<string, int> _movedBy = new();

        /// <summary>Watches the windows made from now on as made in showing a window (StartOnShownWindow).</summary>
        public void WatchWindowsMadeFromNow()
        {
            _building = false;
            WatchWindowsMade();
        }

        /// <summary>
        /// A sent message handled: its time, less the time of the messages
        /// sent while it was handled (which count as their own), added to
        /// its kind's. So the time a message's own handling took - Windows'
        /// and this app's code run for it - is put down to that message.
        ///
        /// Why: making each drop-down's window took a tenth of a second as
        /// the Setup Sheet was shown, and three-quarters of a second all
        /// told for the pages - none of it in this app's layout, and no
        /// message sent often enough to account for it (1 Oct).
        /// </summary>
        private IntPtr MessageHandled(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !_building && lParam != IntPtr.Zero && _inMessages.Count > 0)
            {
                // CWPRETSTRUCT: lResult, lParam, wParam, message, hwnd.
                var message = Marshal.ReadInt32(lParam, 3 * IntPtr.Size);
                var now = Stopwatch.GetTimestamp();

                // Normally the one on top; a message whose start was missed
                // leaves the ones above it, which are let go.
                while (_inMessages.Count > 0)
                {
                    var (sent, at, inner) = _inMessages.Pop();

                    if (sent != message)
                    {
                        continue;
                    }

                    var whole = now - at;
                    var own = TimeSpan.FromSeconds((double)Math.Max(0, whole - inner) / Stopwatch.Frequency);

                    _messageTime[message] = _messageTime.TryGetValue(message, out var before) ? before + own : own;

                    if (_inMessages.Count > 0)
                    {
                        var (outer, outerAt, outerInner) = _inMessages.Pop();
                        _inMessages.Push((outer, outerAt, outerInner + whole));
                    }

                    break;
                }
            }

            return CallNextHookEx(_handledHook, code, wParam, lParam);
        }

        /// <summary>The messages being handled now, innermost on top: which, since when, and the time of those sent while it was.</summary>
        private readonly Stack<(int Message, long At, long Inner)> _inMessages = new();

        /// <summary>The time each kind of message's own handling took - see MessageHandled.</summary>
        private readonly Dictionary<int, TimeSpan> _messageTime = new();

        private IntPtr _handledHook;
        private HookProc? _onMessageHandled;

        /// <summary>The constructor is done: what is made from here is made in showing the window.</summary>
        public void BuildingDone() => _building = false;

        /// <summary>Stops watching for windows made (WatchWindowsMade). Safe to call more than once.</summary>
        public void StopWatchingWindows()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            if (_messageHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_messageHook);
                _messageHook = IntPtr.Zero;
            }

            if (_handledHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_handledHook);
                _handledHook = IntPtr.Zero;
            }

            if (_lastMade is { } last && _firstMadeShowing != 0)
            {
                _madeShowingSpan = Stopwatch.GetElapsedTime(_firstMadeShowing, last.At);
            }
        }

        private IntPtr _hook;
        private IntPtr _messageHook;

        /// <summary>True until the window's constructor is done (BuildingDone).</summary>
        private bool _building = true;

        private HookProc? _onMessageSent;
        private int _windowsMadeShowing;
        private long _firstMadeShowing;
        private TimeSpan _madeShowingSpan;
        private (IntPtr Window, long At, StackTrace Trace)? _lastMade;
        private readonly Dictionary<string, (TimeSpan Took, int Count)> _madeByKind = new();
        private readonly List<(TimeSpan Took, string Kind, StackTrace Trace)> _longestMakes = new();
        private readonly Dictionary<int, int> _messages = new();

        /// <summary>Kept for as long as the hook is in: Windows calls it, and nothing else holds it.</summary>
        private HookProc? _onWindowMade;

        private int _windowsMadeEarly;
        private readonly List<string> _earlyMadeBy = new();

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        private const int WhCbt = 5;
        private const int WhCallWndProc = 4;
        private const int WhCallWndProcRet = 12;
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

        private static string MadeBy() => MadeBy(new StackTrace(2, false));

        private static string MadeBy(StackTrace trace)
        {
            // Every frame, Windows Forms' own included, from the one that
            // asked for the handle out to this app's first: which call it was
            // is in Windows Forms' frames, as often as not (1 Oct: the frames
            // of this app alone said only "the constructor").
            var frames = trace.GetFrames();
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

        private static string MessageName(int message) => message switch
        {
            0x0001 => "WM_CREATE",
            0x0003 => "WM_MOVE",
            0x0005 => "WM_SIZE",
            0x0006 => "WM_ACTIVATE",
            0x0007 => "WM_SETFOCUS",
            0x0008 => "WM_KILLFOCUS",
            0x000B => "WM_SETREDRAW",
            0x000C => "WM_SETTEXT",
            0x000D => "WM_GETTEXT",
            0x000E => "WM_GETTEXTLENGTH",
            0x000F => "WM_PAINT",
            0x0014 => "WM_ERASEBKGND",
            0x0018 => "WM_SHOWWINDOW",
            0x001C => "WM_ACTIVATEAPP",
            0x0020 => "WM_SETCURSOR",
            0x0024 => "WM_GETMINMAXINFO",
            0x002B => "WM_DRAWITEM",
            0x002C => "WM_MEASUREITEM",
            0x0030 => "WM_SETFONT",
            0x0031 => "WM_GETFONT",
            0x003D => "WM_GETOBJECT",
            0x0046 => "WM_WINDOWPOSCHANGING",
            0x0047 => "WM_WINDOWPOSCHANGED",
            0x004E => "WM_NOTIFY",
            0x007C => "WM_STYLECHANGING",
            0x007D => "WM_STYLECHANGED",
            0x0081 => "WM_NCCREATE",
            0x0082 => "WM_NCDESTROY",
            0x0083 => "WM_NCCALCSIZE",
            0x0085 => "WM_NCPAINT",
            0x0086 => "WM_NCACTIVATE",
            0x00B1 => "EM_SETSEL",
            0x00D3 => "EM_SETMARGINS",
            0x00F1 => "BM_SETCHECK",
            0x00F4 => "BM_SETSTYLE",
            0x0111 => "WM_COMMAND",
            0x0114 => "WM_HSCROLL",
            0x0115 => "WM_VSCROLL",
            0x0127 => "WM_CHANGEUISTATE",
            0x0128 => "WM_UPDATEUISTATE",
            0x0129 => "WM_QUERYUISTATE",
            0x0133 => "WM_CTLCOLOREDIT",
            0x0134 => "WM_CTLCOLORLISTBOX",
            0x0138 => "WM_CTLCOLORSTATIC",
            0x0142 => "CB_SETEDITSEL",
            0x0143 => "CB_ADDSTRING",
            0x0146 => "CB_GETCOUNT",
            0x0147 => "CB_GETCURSEL",
            0x0148 => "CB_GETLBTEXT",
            0x0149 => "CB_GETLBTEXTLEN",
            0x014A => "CB_INSERTSTRING",
            0x014B => "CB_RESETCONTENT",
            0x0150 => "CB_GETITEMDATA",
            0x0188 => "LB_GETCURSEL",
            0x0189 => "LB_GETTEXT",
            0x018A => "LB_GETTEXTLEN",
            0x018B => "LB_GETCOUNT",
            0x014E => "CB_SETCURSEL",
            0x0153 => "CB_SETITEMHEIGHT",
            0x0154 => "CB_GETITEMHEIGHT",
            0x0210 => "WM_PARENTNOTIFY",
            0x0281 => "WM_IME_SETCONTEXT",
            0x0282 => "WM_IME_NOTIFY",
            0x02E0 => "WM_DPICHANGED",
            0x02E2 => "WM_DPICHANGED_BEFOREPARENT",
            0x02E3 => "WM_DPICHANGED_AFTERPARENT",
            0x031A => "WM_THEMECHANGED",
            0x1501 => "EM_SETCUEBANNER",
            _ => $"0x{message:X4}",
        };

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

            var showing = _windowsMadeShowing == 0
                ? string.Empty
                : $"; {_windowsMadeShowing} windows made in showing it (or meanwhile), the first to the last {_madeShowingSpan.TotalSeconds:0.00}s - by kind (the time to the next being begun): "
                    + string.Join(", ", _madeByKind
                        .OrderByDescending(kind => kind.Value.Took)
                        .Take(8)
                        .Select(kind => $"{kind.Key} {kind.Value.Took.TotalSeconds:0.00} x{kind.Value.Count}"))
                    + "; the longest: "
                    + string.Join("; then: ", _longestMakes.Select(make => $"{make.Kind} {make.Took.TotalSeconds:0.000}s, made by {MadeBy(make.Trace)}"))
                    + "; messages sent meanwhile: "
                    + string.Join(", ", _messages
                        .OrderByDescending(message => message.Value)
                        .Take(10)
                        .Select(message => $"{MessageName(message.Key)} x{message.Value}"))
                    + "; the messages' own handling, by time: "
                    + string.Join(", ", _messageTime
                        .OrderByDescending(message => message.Value)
                        .Take(10)
                        .Select(message => $"{MessageName(message.Key)} {message.Value.TotalSeconds:0.00} x{(_messages.TryGetValue(message.Key, out var count) ? count : 0)}"))
                    + "; windows moved, by kind: "
                    + string.Join(", ", _movedByKind
                        .OrderByDescending(kind => kind.Value)
                        .Take(8)
                        .Select(kind => $"{kind.Key} x{kind.Value}"))
                    + "; who moved them (one in every 100): "
                    + string.Join(" ;; ", _movedBy
                        .OrderByDescending(by => by.Value)
                        .Take(5)
                        .Select(by => $"x{by.Value} {by.Key}"));

            return $"{_name} took {_total.Elapsed.TotalSeconds:0.0}s to open - {parts}{styled}{called}{made}{early}{showing}.";
        }
    }
}
