namespace HeatCaller;

public enum DecoderConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
}

/// <summary>A single decoded lap-pass event from the raw EasyLap serial protocol.</summary>
public sealed class DirectLapEvent
{
    public required string TransponderId { get; init; }
    public uint DeviceTimeMs { get; init; }
    public int SignalHits { get; init; }
    public int SignalStrength { get; init; }

    /// <summary>
    /// True for a pass generated inside the app (a ghost car) rather than
    /// read from the decoder. For these, DeviceTimeMs is milliseconds since
    /// the green flag, not a decoder clock reading - and they never move
    /// the session's decoder clock estimate.
    /// </summary>
    public bool IsSynthetic { get; init; }

    /// <summary>
    /// True for a pass from a simulated decoder (DecoderTransport.Simulated):
    /// made up, though under the club's real transponder numbers - so it is
    /// raced like any other, but never kept as a night of that transponder's
    /// health (TransponderHealthStore).
    /// </summary>
    public bool IsSimulated { get; init; }

    /// <summary>
    /// When the pass reached the app, on TimingClock - 0 when nobody stamped
    /// it (a ghost, a test). What places a pass from one decoder against a
    /// pass from another; the race itself keeps using DeviceTimeMs.
    /// </summary>
    public long ReceivedMs { get; init; }

    /// <summary>
    /// When the car crossed, on TimingClock, by the decoder's own clock
    /// (DecoderClockMap) - earlier than ReceivedMs by however long the pass
    /// took to reach the app. 0 when the map could not say (too few clock
    /// readings yet, a ghost, a test, a decoder with no map). What a call
    /// about this crossing is timed from, so the link's delay counts against
    /// its say-by too (21 Nov 2026) - see AnnouncementService.LapCrossedAtMs.
    /// </summary>
    public long CrossedMs { get; init; }

    /// <summary>
    /// A decoder-mapped crossing time, kept only when it is plausible: not
    /// after the pass arrived, and not further before it than
    /// DecoderClockMap.Jump (a map that far out is not a clock to trust). 0
    /// otherwise - see CrossedMs.
    /// </summary>
    public static long CrossingOnAppClock(long? mappedMs, long receivedMs) =>
        mappedMs is { } mapped && mapped > 0 && mapped <= receivedMs && receivedMs - mapped <= DecoderClockMap.Jump ? mapped : 0;

    /// <summary>Which decoder saw it (DecoderConnection.EasyLap or ILap), or 0 when none did.</summary>
    public int Decoder { get; init; }

    /// <summary>Where on the track - the line, unless the decoder is a split loop.</summary>
    public TimingPoint Point { get; init; }

    /// <summary>
    /// A copy under another transponder, every other field carried across -
    /// for the race filing an alternate transponder's pass under the main
    /// one. If this class grows a field, copy it here.
    /// </summary>
    public DirectLapEvent WithTransponder(string transponderId) => new()
    {
        TransponderId = transponderId,
        DeviceTimeMs = DeviceTimeMs,
        SignalHits = SignalHits,
        SignalStrength = SignalStrength,
        IsSynthetic = IsSynthetic,
        IsSimulated = IsSimulated,
        ReceivedMs = ReceivedMs,
        CrossedMs = CrossedMs,
        Decoder = Decoder,
        Point = Point,
    };

    /// <summary>A copy marked as from a simulated decoder - see <see cref="IsSimulated"/>.</summary>
    public DirectLapEvent AsSimulated() => new()
    {
        TransponderId = TransponderId,
        DeviceTimeMs = DeviceTimeMs,
        SignalHits = SignalHits,
        SignalStrength = SignalStrength,
        IsSynthetic = IsSynthetic,
        IsSimulated = true,
        ReceivedMs = ReceivedMs,
        CrossedMs = CrossedMs,
        Decoder = Decoder,
        Point = Point,
    };

    /// <summary>A copy saying which decoder saw it, when it arrived and where it was seen.</summary>
    public DirectLapEvent Stamped(int decoder, long receivedMs, TimingPoint point, long? crossedMs = null) => new()
    {
        TransponderId = TransponderId,
        DeviceTimeMs = DeviceTimeMs,
        SignalHits = SignalHits,
        SignalStrength = SignalStrength,
        IsSynthetic = IsSynthetic,
        IsSimulated = IsSimulated,
        ReceivedMs = receivedMs,
        CrossedMs = crossedMs ?? CrossedMs,
        Decoder = decoder,
        Point = point,
    };
}
