namespace HeatCaller;

/// <summary>
/// Lap calls the window was told of too late to be worth saying - see
/// <see cref="WhileLapCallsAreStale"/>.
/// </summary>
/// <remarks>
/// WHY (23 Oct 2026, timing layer, P4b): with "Run timing on its own thread"
/// on, laps go on being counted while the window is frozen, and when it wakes
/// it is told of every one it missed, in order, all at once. The board, the
/// results and the journal want every lap. The room does not want a backlog
/// of lap calls read out over the race that is running now - after a freeze
/// it should hear the present. Inline nothing comes through here: the screens
/// only use it when RaceTiming.DeliveringIsStale says the lap is old, which
/// it never does inline.
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>How deep this thread is in WhileLapCallsAreStale. Per thread, so a line offered on another thread meanwhile (a per-lap batch flushing on the pool) is never caught by it.</summary>
    [ThreadStatic]
    private static int t_lapCallsStale;

    /// <summary>Calls skipped since the last line said so - UI thread (where the scope is used).</summary>
    private int _staleCallsSkipped;

    /// <summary>How old the oldest of them was, in milliseconds.</summary>
    private long _staleCallsOldestMs;

    /// <summary>When the last "skipped" line was written (TimingClock), so a freeze's catch-up writes a few lines, not one per lap.</summary>
    private long _staleCallsLoggedAtMs;

    /// <summary>How often at most the skipped calls are summed up in the log.</summary>
    private const long StaleCallsLogEveryMs = 2_000;

    /// <summary>True while the calls being offered on this thread are about a lap that is too old to say.</summary>
    private static bool LapCallsAreStale => t_lapCallsStale > 0;

    /// <summary>
    /// Runs <paramref name="work"/> - a lap's handling: the running order, the
    /// streaks, the lead, the battles - with every call and sound it would
    /// offer on this thread left unsaid. The announcer still learns the lap,
    /// so nothing is called late afterwards (a lead taken during the freeze is
    /// not announced as new on the next lap). A per-lap candidate, a voice
    /// line, a sound effect, a driver's lap beep and a coaching tone are all
    /// dropped as they are offered; nothing already queued is touched.
    /// </summary>
    /// <param name="ageMs">How old the lap is, for the log line; null when not known.</param>
    internal void WhileLapCallsAreStale(Action work, long? ageMs = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        t_lapCallsStale++;

        try
        {
            work();
        }
        finally
        {
            t_lapCallsStale--;

            if (t_lapCallsStale == 0)
            {
                ReportStaleCallsSkipped(ageMs);
            }
        }
    }

    /// <summary>
    /// Runs a lap's handling with every call it would offer left unsaid, as
    /// <see cref="WhileLapCallsAreStale"/> does - for a crossing that arrives
    /// while the race is being scored by hand (11 Nov 2026, decoder-down
    /// mode; see HandScoring). The director has said the loop can't be
    /// trusted, so a flicker from it must not be called as a lap time or a
    /// fastest lap; the board and the running order still take it. Nothing
    /// is logged as "told of late": these calls aren't late, they're held,
    /// and the console has already said why.
    /// </summary>
    internal void WhileHandScoring(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var before = _staleCallsSkipped;
        t_lapCallsStale++;

        try
        {
            work();
        }
        finally
        {
            t_lapCallsStale--;

            // Not counted towards the "skipped as late" line.
            _staleCallsSkipped = before;
        }
    }

    /// <summary>
    /// Notes a call left unsaid because its lap is too old, if this thread is
    /// in WhileLapCallsAreStale. True when it was - the caller then offers
    /// nothing.
    /// </summary>
    private bool SkippedAsStale()
    {
        if (!LapCallsAreStale)
        {
            return false;
        }

        _staleCallsSkipped++;
        return true;
    }

    /// <summary>The log line for calls skipped, at most every StaleCallsLogEveryMs.</summary>
    private void ReportStaleCallsSkipped(long? ageMs)
    {
        if (ageMs is { } age && age > _staleCallsOldestMs)
        {
            _staleCallsOldestMs = age;
        }

        if (_staleCallsSkipped == 0)
        {
            return;
        }

        var now = TimingClock.NowMs;

        if (_staleCallsLoggedAtMs != 0 && now - _staleCallsLoggedAtMs < StaleCallsLogEveryMs)
        {
            return;
        }

        _staleCallsLoggedAtMs = now;

        var oldest = _staleCallsOldestMs > 0 ? $", the oldest {(_staleCallsOldestMs / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}s old" : string.Empty;
        StatusMessage?.Invoke(
            $"[voice] Skipped {Plural.Of(_staleCallsSkipped, "lap call")} the window was told of late{oldest} - "
            + "the board has every lap; the room hears the race as it is now.");

        _staleCallsSkipped = 0;
        _staleCallsOldestMs = 0;
    }
}
