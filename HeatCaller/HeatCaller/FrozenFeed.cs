namespace HeatCaller;

/// <summary>
/// A callback's answer, worked out on the UI thread and read from any
/// thread: Refresh asks the callback (UI thread), Get hands back the last
/// answer it gave. Before the first good answer - or after Forget - Get
/// throws, which every place the live state reads one already turns into
/// "left out".
/// </summary>
/// <remarks>
/// WHY (24 Oct 2026, timing layer, P8): with timing on its own thread the
/// live state for the phones and the relay is built off the UI thread
/// (MainForm.LiveState), and some of what goes into it - the race in hand,
/// the call-ups, the track map, the championship, the lead changes - is
/// only the window's to read. Those are read here on the window's 500 ms
/// tick instead; during a freeze they simply stay as they were, while the
/// board itself (from the timing thread) stays live. The answer is boxed
/// and swapped in whole (one volatile write), so a reader gets one answer
/// or the next, never half of either - and a null answer is still an
/// answer.
/// </remarks>
internal sealed class FrozenFeed<T>
{
    private readonly Func<T> _source;
    private volatile Box? _last;

    private sealed record Box(T Value);

    public FrozenFeed(Func<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>Asks the callback again. UI thread. A callback that throws leaves the last answer standing.</summary>
    public void Refresh()
    {
        try
        {
            _last = new Box(_source());
        }
        catch
        {
            // The last answer stands.
        }
    }

    /// <summary>Drops the last answer, so Get throws until the next good Refresh - for a feed nobody is reading, rather than an answer from long ago.</summary>
    public void Forget() => _last = null;

    /// <summary>The last answer. Any thread. Throws before the first good Refresh.</summary>
    public T Get() => _last is { } box
        ? box.Value
        : throw new InvalidOperationException("Not read on the UI thread yet.");
}
