namespace HeatCaller;

// Moved out of SplitLineUpForm.cs (4 Oct 2026): the pairing is timing
// logic with no window in it, and the timing code is being moved out
// into a layer of its own a piece at a time. The form uses it as before.

/// <summary>
/// Pairs the line's passes with the split loop's for Line Up: with both
/// loops at the same spot, one car crossing is read by both, and the gap
/// between the two readings is how late the split loop reads (its decoder's
/// own delay, the serial line's, and whatever the clock tie leaves).
///
/// A pass is paired with the other loop's nearest unpaired pass by the same
/// driver within <see cref="Window"/>, whichever arrives first. The answer
/// is the median gap, so one odd reading can't move it.
///
/// Two guards on top, because Line Up sees raw passes - before the race's
/// minimum lap time and parked-car checks. A pass from the same driver on
/// the same loop within <see cref="RepeatMs"/> of their last one is the
/// decoder reading a car sat over it again, not a crossing, and is ignored:
/// held over both loops, the two trains of repeats paired with each other
/// every two seconds and gave one confident, made-up gap. And there is no
/// answer until at least <see cref="Least"/> gaps agree to within
/// <see cref="AgreeMs"/> of each other's middle, so scattered readings
/// don't come out as a number to save.
/// </summary>
public sealed class LineUpPairing
{
    /// <summary>The furthest apart the two readings of one crossing can be.</summary>
    public const long Window = 1500;

    /// <summary>Gaps needed before there is an answer.</summary>
    public const int Least = 3;

    /// <summary>
    /// A pass by the same driver on the same loop sooner than this after
    /// their last is a repeat read of a car sat over it - the ~2 s retrigger
    /// the race's minimum lap time is there for - and is ignored.
    /// </summary>
    public const long RepeatMs = 2500;

    /// <summary>How close to the middle gap the others must be to count as agreeing with it.</summary>
    public const long AgreeMs = 100;

    private readonly Dictionary<(bool IsLine, string Driver), long> _lastSeen = new();

    private readonly List<(string Driver, long Ms)> _line = new();
    private readonly List<(string Driver, long Ms)> _split = new();
    private readonly List<(string Driver, long GapMs)> _gaps = new();

    /// <summary>Every gap found so far, oldest first: positive when the split loop read the car later than the line.</summary>
    public IReadOnlyList<(string Driver, long GapMs)> Gaps => _gaps;

    /// <summary>The median gap, rounded to a millisecond, or null with fewer than <see cref="Least"/>.</summary>
    public int? Answer
    {
        get
        {
            if (_gaps.Count < Least || Median() is not { } median || Agreeing < Least)
            {
                return null;
            }

            return (int)Math.Round(median, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>How many gaps are within <see cref="AgreeMs"/> of the middle one.</summary>
    public int Agreeing => Median() is { } median
        ? _gaps.Count(g => Math.Abs(g.GapMs - median) <= AgreeMs)
        : 0;

    private double? Median()
    {
        if (_gaps.Count == 0)
        {
            return null;
        }

        var sorted = _gaps.Select(g => g.GapMs).OrderBy(ms => ms).ToList();
        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>A line pass by this driver at this time on the line's clock. Returns the gap when it paired.</summary>
    public long? AddLine(string driver, long lineMs) => Add(driver, lineMs, isLine: true);

    /// <summary>A split pass by this driver at this time on the line's clock (before calibration). Returns the gap when it paired.</summary>
    public long? AddSplit(string driver, long lineMs) => Add(driver, lineMs, isLine: false);

    private long? Add(string driver, long ms, bool isLine)
    {
        var mine = isLine ? _line : _split;
        var theirs = isLine ? _split : _line;

        // The same car read again on the same loop, too soon to be another
        // crossing: a car sat over it. Its time is kept, so a whole train of
        // repeats is ignored, not just the first of them.
        var key = (isLine, driver.ToUpperInvariant());

        if (_lastSeen.TryGetValue(key, out var last) && ms >= last && ms - last < RepeatMs)
        {
            _lastSeen[key] = ms;
            return null;
        }

        _lastSeen[key] = ms;

        // Forget what is too old to pair with anything any more.
        theirs.RemoveAll(p => Math.Abs(ms - p.Ms) > Window * 4);

        var match = theirs
            .Where(p => string.Equals(p.Driver, driver, StringComparison.OrdinalIgnoreCase) && Math.Abs(ms - p.Ms) <= Window)
            .OrderBy(p => Math.Abs(ms - p.Ms))
            .Select(p => ((string Driver, long Ms)?)p)
            .FirstOrDefault();

        if (match is not { } other)
        {
            mine.Add((driver, ms));
            return null;
        }

        theirs.Remove(other);

        var gap = isLine ? other.Ms - ms : ms - other.Ms;
        _gaps.Add((driver, gap));
        return gap;
    }
}
