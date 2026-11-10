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
        Decoder = Decoder,
        Point = Point,
    };

    /// <summary>A copy saying which decoder saw it, when it arrived and where it was seen.</summary>
    public DirectLapEvent Stamped(int decoder, long receivedMs, TimingPoint point) => new()
    {
        TransponderId = TransponderId,
        DeviceTimeMs = DeviceTimeMs,
        SignalHits = SignalHits,
        SignalStrength = SignalStrength,
        IsSynthetic = IsSynthetic,
        IsSimulated = IsSimulated,
        ReceivedMs = receivedMs,
        Decoder = decoder,
        Point = point,
    };
}
