namespace HeatCaller;

/// <summary>
/// From a decoder's bytes to passes the race can use, for every decoder
/// StreamDecoderClient runs (3 and up): the protocol reads the bytes, and
/// this puts each pass on the app's clock and drops repeats.
/// </summary>
/// <remarks>
/// <para>
/// WHY (1 Nov 2026): kept apart from the client, with no port or socket, so
/// a test can feed it bytes and check what comes out, as EasyLapFramer is
/// tested. One per connection, on the client's reader thread only.
/// </para>
/// <para>
/// THE CLOCK. Like an I-Lap's, a pass's DeviceTimeMs is on the app's clock
/// (TimingClock), so the race, practice's auto-start and the recorder treat
/// these decoders as they treat the I-Lap. It is better than an I-Lap's:
/// every one of these decoders stamps its records with its own clock, so
/// the decoder's time is mapped onto the app's (DecoderClockMap, as the
/// EasyLap's is for the split loop). A lap then has the decoder's precision
/// rather than whatever a USB adapter or a network added on the way in.
/// </para>
/// <para>
/// When the map has too few readings to trust - the first passes after
/// connecting, or after a quiet spell on a decoder that sends no heartbeat -
/// the last offset it gave is used, and with none yet, the arrival time.
/// A mapped time that lands well away from the arrival (a decoder that has
/// restarted, so its clock jumped) is not believed either: arrival is used
/// and the old offset forgotten.
/// </para>
/// </remarks>
internal sealed class StreamDecoderReader
{
    /// <summary>
    /// Two passes for the same transponder this close together are one
    /// crossing read twice - as the I-Lap's (ILapClient.DuplicateWindowMs).
    /// </summary>
    internal const long DuplicateWindowMs = 800;

    /// <summary>How far before its arrival a mapped pass may land before it is not believed - far beyond any link's delay.</summary>
    internal const long MostEarlyMs = DecoderClockMap.Jump;

    /// <summary>A mapped time may land this far after its arrival - rounding, nothing more.</summary>
    internal const long MostLateMs = 5;

    private readonly int _decoder;
    private readonly IDecoderProtocol _protocol;
    private readonly Action<string> _log;
    private readonly List<DecoderReading> _readings = new();
    private readonly Dictionary<string, long> _lastPassMs = new(StringComparer.Ordinal);

    /// <summary>The decoder's first clock reading, so the rest fit DecoderClockMap's 32-bit milliseconds.</summary>
    private long? _base;

    /// <summary>The last offset the map gave, used while it is short of readings.</summary>
    private long? _lastOffset;

    public StreamDecoderReader(int decoder, IDecoderProtocol protocol, Action<string> log)
    {
        _decoder = decoder;
        _protocol = protocol;
        _log = log;
    }

    /// <summary>This connection's map from the decoder's clock to the app's.</summary>
    public DecoderClockMap Clock { get; } = new();

    /// <summary>When (TimingClock) the decoder last said anything timing can use, or null before it has.</summary>
    public long? LastHeardMs { get; private set; }

    /// <summary>Raised for every reading of the decoder's clock, with its arrival - its heartbeat, for those that send one.</summary>
    public event Action<long>? ClockRead;

    /// <summary>
    /// Bytes that arrived at <paramref name="arrivedMs"/> (TimingClock): the
    /// passes they completed, stamped with this decoder and the line, in
    /// the order the decoder sent them.
    /// </summary>
    public List<DirectLapEvent> Feed(ReadOnlySpan<byte> bytes, long arrivedMs)
    {
        _readings.Clear();
        _protocol.Feed(bytes, _readings, _log);

        var passes = new List<DirectLapEvent>();

        foreach (var reading in _readings)
        {
            LastHeardMs = arrivedMs;
            var relative = reading.DeviceMs is { } device ? Learn(device, arrivedMs) : (long?)null;

            if (reading.TransponderId is not { } car)
            {
                ClockRead?.Invoke(arrivedMs);
                continue;
            }

            var at = AppTimeOf(relative, arrivedMs);

            if (_lastPassMs.TryGetValue(car, out var last) && at - last < DuplicateWindowMs && at >= last)
            {
                _log($"[decoder] Suppressed repeat of {car} ({at - last}ms after the last).");
                continue;
            }

            _lastPassMs[car] = at;

            _log($"[LAP EVENT] transponder={car}  device_time={at}ms  signal_hits={reading.Hits}  signal_strength={reading.Strength}  ({DecoderConnection.ShortName(_decoder)})");

            passes.Add(new DirectLapEvent
            {
                TransponderId = car,
                DeviceTimeMs = (uint)at,
                SignalHits = reading.Hits,
                SignalStrength = reading.Strength,
            }.Stamped(_decoder, arrivedMs, TimingPoint.Line));
        }

        return passes;
    }

    /// <summary>
    /// Teaches the map one reading of the decoder's clock, and hands back
    /// that reading on the map's scale (milliseconds since the first one).
    /// A clock that went backwards, or ran past the map's 32 bits, starts
    /// the map again.
    /// </summary>
    private long Learn(long deviceMs, long arrivedMs)
    {
        if (_base is not { } start || deviceMs < start || deviceMs - start > uint.MaxValue)
        {
            if (_base is not null)
            {
                _log($"[decoder] The {DecoderConnection.ShortName(_decoder)} decoder's clock went from after {_base}ms back to {deviceMs}ms - it has restarted. Learning its clock again; laps are timed as they arrive meanwhile.");
            }

            _base = deviceMs;
            start = deviceMs;
            Clock.Reset();
            _lastOffset = null;
        }

        var relative = deviceMs - start;
        Clock.Add(arrivedMs, (uint)relative);
        return relative;
    }

    /// <summary>The app's time for a pass: mapped from the decoder's clock where that can be believed, otherwise its arrival.</summary>
    private long AppTimeOf(long? relative, long arrivedMs)
    {
        if (relative is not { } device)
        {
            return arrivedMs;
        }

        if (Clock.Estimate(arrivedMs) is { } estimate)
        {
            _lastOffset = estimate.Offset;
        }

        if (_lastOffset is not { } offset)
        {
            return arrivedMs;
        }

        var mapped = device + offset;

        if (mapped > arrivedMs + MostLateMs || mapped < arrivedMs - MostEarlyMs)
        {
            _lastOffset = null;
            return arrivedMs;
        }

        return mapped;
    }
}
