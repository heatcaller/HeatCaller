using System.Diagnostics;

namespace HeatCaller;

/// <summary>
/// Where on the track a pass was seen: the start/finish line, where laps
/// are counted, or a split loop partway round. Only the line's passes ever
/// reach the race (RaceControlSession); a split loop's go to SplitLoop and
/// can never add, take away or change a lap.
/// </summary>
public enum TimingPoint
{
    Line = 0,
    Split1 = 1,
    Split2 = 2,
    Split3 = 3,
}

/// <summary>
/// The app's one monotonic clock for timing: milliseconds since this run of
/// the app started, read from the high-resolution counter. It only ever goes
/// forward - a Windows time sync or a clock change does not move it - and it
/// is the same clock for every decoder, which is what lets a pass seen by
/// one decoder be placed against a pass seen by another.
///
/// The I-Lap times its passes on this clock (ILapClient), and every pass is
/// stamped with it as it reaches the app (DirectLapEvent.ReceivedMs). An
/// EasyLap times its passes on its own clock; DecoderClockMap relates that
/// clock to this one.
/// </summary>
public static class TimingClock
{
    private static readonly long Origin = Stopwatch.GetTimestamp();

    /// <summary>Milliseconds since the app started. Any thread.</summary>
    public static long NowMs => (Stopwatch.GetTimestamp() - Origin) * 1000 / Stopwatch.Frequency;
}

/// <summary>
/// How a decoder's own clock relates to the app's (TimingClock), learned
/// from readings of the decoder's clock and the moment each one arrived.
///
/// Every reading arrives a little after the decoder made it - a USB or
/// serial delay that varies from one to the next but is never negative. So
/// for each reading, arrival minus decoder time is the clocks' true offset
/// plus that delay, and the SMALLEST of them over a recent window is the
/// closest to the true offset there is. That is the offset used: it settles
/// within a few heartbeats (an EasyLap sends its clock once a second), and
/// how far the typical reading sits above it says how jumpy the link is.
///
/// Readings older than <see cref="Window"/> are forgotten, so a slow drift
/// between two crystals (tens of parts per million - a millisecond or two a
/// minute) is followed rather than averaged in.
///
/// A reading that disagrees with the rest by more than <see cref="Jump"/> is
/// held back: a misframed message, whose clock bytes are noise (see
/// EasyLapFramer), or a decoder that has just restarted. While one is held
/// the map isn't trusted. One that the next good reading contradicts is
/// dropped; <see cref="Least"/> in a row that agree with each other mean the
/// decoder really did restart, and the map starts again from them.
///
/// Thread-safe: readings come in on the decoder's own thread, and the split
/// loop asks on the UI thread.
/// </summary>
public sealed class DecoderClockMap
{
    /// <summary>How long a reading is used for.</summary>
    public const long Window = 60_000;

    /// <summary>A reading newer than this is needed for the map to be trusted - the decoder is still talking.</summary>
    public const long Fresh = 5_000;

    /// <summary>Readings needed before the map is used.</summary>
    public const int Least = 3;

    /// <summary>How far a reading may disagree with the others before it is held back - far more than any USB or serial delay.</summary>
    public const long Jump = 2_000;

    private readonly object _gate = new();
    private readonly Queue<(long Arrived, long Device)> _readings = new();
    private readonly List<(long Arrived, long Device)> _held = new();

    /// <summary>Forgets everything - a new connection, or a decoder that was restarted.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _readings.Clear();
            _held.Clear();
        }
    }

    /// <summary>A reading of the decoder's clock, <paramref name="deviceMs"/>, that reached the app at <paramref name="arrivedMs"/> (TimingClock).</summary>
    public void Add(long arrivedMs, uint deviceMs)
    {
        lock (_gate)
        {
            var gap = arrivedMs - (long)deviceMs;

            // Judged against whatever is already here - one reading is
            // enough to judge by. It used to wait for Least, so the first
            // three after every Reset (every connect, every reconnect) went
            // in unchecked: one misframed heartbeat among them and the map
            // trusted a clock 24 days out, and a split was given a line time
            // that never happened. A bad first reading is held back by the
            // good ones that follow it, which then start the map afresh -
            // the same way a decoder restart is recognised.
            if (_readings.Count > 0)
            {
                var best = _readings.Min(r => r.Arrived - r.Device);

                if (Math.Abs(gap - best) > Jump)
                {
                    _held.Add((arrivedMs, deviceMs));

                    if (_held.Count >= Least)
                    {
                        var gaps = _held.Select(h => h.Arrived - h.Device).ToList();

                        if (gaps.Max() - gaps.Min() <= Jump)
                        {
                            // They agree: the decoder restarted. Start again from them.
                            _readings.Clear();

                            foreach (var held in _held)
                            {
                                _readings.Enqueue(held);
                            }

                            _held.Clear();
                        }
                        else
                        {
                            _held.RemoveAt(0);
                        }
                    }

                    Trim(arrivedMs);
                    return;
                }
            }

            // A good reading: anything held back was noise.
            _held.Clear();
            _readings.Enqueue((arrivedMs, deviceMs));
            Trim(arrivedMs);
        }
    }

    /// <summary>Forgets readings older than the window. Caller holds the lock.</summary>
    private void Trim(long nowMs)
    {
        while (_readings.Count > 0 && nowMs - _readings.Peek().Arrived > Window)
        {
            _readings.Dequeue();
        }
    }

    /// <summary>
    /// The offset to add to a decoder time to get app time, and how jumpy
    /// the link has been (the median reading's delay over the quickest one's),
    /// at <paramref name="nowMs"/> - or null while there are too few recent
    /// readings to trust.
    /// </summary>
    public (long Offset, long Jitter)? Estimate(long nowMs)
    {
        lock (_gate)
        {
            // Too few, or something odd is being held back until it is
            // understood.
            if (_readings.Count < Least || _held.Count > 0)
            {
                return null;
            }

            var newest = _readings.Max(r => r.Arrived);

            if (nowMs - newest > Fresh)
            {
                return null;
            }

            var gaps = _readings
                .Where(r => nowMs - r.Arrived <= Window)
                .Select(r => r.Arrived - r.Device)
                .OrderBy(gap => gap)
                .ToList();

            if (gaps.Count < Least)
            {
                return null;
            }

            // Readings that disagree with each other by more than Jump are
            // not a clock to trust, however they got in.
            if (gaps[^1] - gaps[0] > Jump)
            {
                return null;
            }

            return (gaps[0], gaps[gaps.Count / 2] - gaps[0]);
        }
    }

    /// <summary>App time (TimingClock) for a decoder time, or null while the map can't be trusted.</summary>
    public long? ToApp(uint deviceMs, long nowMs) =>
        Estimate(nowMs) is { } estimate ? deviceMs + estimate.Offset : null;

    /// <summary>Decoder time for an app time (TimingClock), or null while the map can't be trusted.</summary>
    public long? ToDevice(long appMs, long nowMs) =>
        Estimate(nowMs) is { } estimate ? appMs - estimate.Offset : null;
}
