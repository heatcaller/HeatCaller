using System.Diagnostics;

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
    public static void Start(string name, Control? window = null) => _timing = new Clock(name, window);

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
        private readonly Control? _window;

        public Clock(string name, Control? window)
        {
            _name = name;
            _window = window;
        }

        public TimeSpan Total => _total.Elapsed;

        public void Mark(string part)
        {
            var took = _part.Elapsed;

            _parts.Add((_window is null ? part : $"{part} ({Handles(_window)} handles)", took));

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
                    .Take(10)
                    .Select(what => $"{what.Key} {what.Value.Took.TotalSeconds:0.00} x{what.Value.Calls}"));

            return $"{_name} took {_total.Elapsed.TotalSeconds:0.0}s to open - {parts}{styled}{called}.";
        }
    }
}
