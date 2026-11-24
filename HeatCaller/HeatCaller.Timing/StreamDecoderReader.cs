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
/// A mapped time that lands after the arrival, or further before it than
/// any link could hold a pass up (a decoder whose clock jumped), is not
/// believed either: arrival is used and the old offset forgotten.
/// </para>
/// <para>
/// WHY (23 Nov 2026, bug hunt 24): a mapped time up to MostEarlyMs BEFORE
/// its arrival is believed. That limit was 2 seconds, on the thought that
/// only a restarted decoder lands further back. But a restart is caught in
/// Learn before it gets here, so what did land further back was a pass held
/// up on the link (a Wi-Fi drop, a Bluetooth hiccup) and delivered late in a
/// burst. It was timed as it arrived while a later car in the same burst
/// kept its true time: the two swapped places, and the late car's next lap
/// came out seconds short.
/// </para>
/// </remarks>
internal sealed class StreamDecoderReader
{
    /// <summary>
    /// Two passes for the same transponder this close together are one
    /// crossing read twice - as the I-Lap's (ILapClient.DuplicateWindowMs).
    /// </summary>
    internal const long DuplicateWindowMs = 800;

    /// <summary>
    /// How far before its arrival a mapped pass may land and still be
    /// believed: a pass the link held up, whose decoder stamp is the truth.
    /// WHY (23 Nov 2026, bug hunt 24): was DecoderClockMap.Jump (2 s), which a
    /// stalled link beats - see the class remarks.
    /// </summary>
    internal const long MostEarlyMs = DecoderClockMap.Window;

    /// <summary>A mapped time may land this far after its arrival - rounding, nothing more.</summary>
    internal const long MostLateMs = 5;

    /// <summary>
    /// How far behind the decoder's latest clock a PASS may be stamped and
    /// still be a car that sat on the loop (stalled, or gridded on it) rather
    /// than a decoder that restarted.
    /// </summary>
    internal const long LongestDwellMs = DecoderClockMap.Window;

    private readonly int _decoder;
    private readonly IDecoderProtocol _protocol;
    private readonly Action<string> _log;
    private readonly List<DecoderReading> _readings = new();
    private readonly Dictionary<string, long> _lastPassMs = new(StringComparer.Ordinal);

    /// <summary>DecoderClockMap.Jump before the decoder's first clock reading, so the rest fit DecoderClockMap's 32-bit milliseconds.</summary>
    private long? _base;

    /// <summary>The latest clock reading since the map last started, so a reading a little behind it (a pass reported after a heartbeat) is not taken for a restart.</summary>
    private long? _highest;

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
            var relative = reading.DeviceMs is { } device ? Learn(device, arrivedMs, reading.TransponderId is not null) : null;

            if (reading.TransponderId is not { } car)
            {
                ClockRead?.Invoke(arrivedMs);
                continue;
            }

            var at = AppTimeOf(relative, arrivedMs);

            // WHY (Bug hunt 17): either side of the last one. The first read
            // of a crossing can be timed as it arrived and its repeat mapped
            // from the decoder's clock, which lands a little EARLIER; taken
            // only when later, that repeat went to the race as a pass before
            // the driver's last one ("the decoder clock went backwards").
            if (_lastPassMs.TryGetValue(car, out var last) && Math.Abs(at - last) < DuplicateWindowMs)
            {
                _log($"[decoder] Suppressed repeat of {car} ({Math.Abs(at - last)}ms {(at >= last ? "after" : "before")} the last).");
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
            }.Stamped(_decoder, arrivedMs, TimingPoint.Line, DirectLapEvent.CrossingOnAppClock(at, arrivedMs)));
        }

        return passes;
    }

    /// <summary>
    /// Teaches the map one reading of the decoder's clock, and hands back
    /// that reading on the map's scale (milliseconds since _base).
    /// A clock that went well backwards, or ran past the map's 32 bits, starts
    /// the map again. Null for a pass stamped well behind the latest clock:
    /// it teaches the map nothing and is timed as it arrived.
    /// </summary>
    private long? Learn(long deviceMs, long arrivedMs, bool isPass)
    {
        // WHY (Bug hunt 17): a car that sat on the loop for more than Jump
        // (stalled, or gridded on it) has its pass stamped that far behind
        // the heartbeats and other cars' passes sent meanwhile. That was
        // taken for a restart: a false "it has restarted" line, and the map
        // thrown away as the race got going. The decoder's clock has not
        // moved, so the map is left alone. A real restart still shows on the
        // next heartbeat, on a pass further back than this, or - on a decoder
        // with neither - to the map itself once its readings stop agreeing.
        if (isPass && _highest is { } latest
            && deviceMs < latest - DecoderClockMap.Jump && deviceMs >= latest - LongestDwellMs)
        {
            return null;
        }

        // WHY (2026-11-05, Bug hunt 11): a pass is only sent once the car has
        // left the loop, so a heartbeat or status that came first can carry a
        // later clock than the pass after it. Every reading was checked
        // against the very first one, so connecting while a car was on the
        // loop logged "it has restarted" and threw the map away. Now only a
        // step back of more than DecoderClockMap.Jump below the highest clock
        // seen is a restart, and the base sits Jump below the first reading so
        // a reading within that reporting delay still fits the map's scale.
        if (_base is not { } start || _highest is not { } highest
            || deviceMs < highest - DecoderClockMap.Jump || deviceMs - start > uint.MaxValue)
        {
            if (_highest is not null)
            {
                _log($"[decoder] The {DecoderConnection.ShortName(_decoder)} decoder's clock went from after {_highest}ms back to {deviceMs}ms - it has restarted. Learning its clock again; laps are timed as they arrive meanwhile.");
            }

            start = deviceMs - DecoderClockMap.Jump;
            _base = start;
            _highest = deviceMs;
            Clock.Reset();
            _lastOffset = null;
        }
        else if (deviceMs > highest)
        {
            _highest = deviceMs;
        }

        var relative = deviceMs - start;

        // WHY (23 Nov 2026, bug hunt 24): the offset is remembered from what
        // the map says BEFORE this reading goes in. A reading held up on the
        // link is held back by the map, which then answers nothing until a
        // good one follows; the offset used to be remembered only at a pass,
        // after its own reading, so the first pass of a night that arrived
        // late had no offset to fall back on and was timed as it arrived.
        if (Clock.Estimate(arrivedMs) is { } before)
        {
            _lastOffset = before.Offset;
        }

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

        // WHY (23 Nov 2026, bug hunt 24): the early side is now MostEarlyMs
        // (a minute), not 2 seconds, so a pass the link held up keeps the
        // decoder's time and its place - see the class remarks. A time before
        // the app's clock began cannot be a crossing, and would not fit
        // DeviceTimeMs.
        if (mapped > arrivedMs + MostLateMs || mapped < arrivedMs - MostEarlyMs || mapped < 0)
        {
            _lastOffset = null;
            return arrivedMs;
        }

        return mapped;
    }
}
