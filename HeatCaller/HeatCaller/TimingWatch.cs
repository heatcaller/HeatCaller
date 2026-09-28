namespace HeatCaller;

/// <summary>
/// Notices, during a race, when timing has quietly gone wrong - the kind of
/// fault nobody sees until the results are wrong - and says so in one line
/// for the TIMING block of the Next Up bar and the console.
/// </summary>
/// <remarks>
/// After-the-freeze item 5 in ROADMAP.md. Before this, a decoder that
/// dropped off USB mid-race turned one dot red on the Connection page, and
/// a loop cable pulled out of the decoder, or a transponder going flat,
/// showed nowhere at all: laps simply stopped being counted.
///
/// Four checks, most serious first; only the first that fails is reported:
///
///  1. <b>The decoder isn't connected.</b>
///  2. <b>The decoder has gone quiet</b>: an EasyLap sends a heartbeat once
///     a second (found in rehearsal 3's capture - see EasyLapFramer). None
///     for <see cref="HeartbeatGap"/> means the decoder or its USB link has
///     stopped, even when Windows hasn't said so. Only once this connection
///     has sent one, so a decoder that never sends them (the I-Lap, as far
///     as is known) is never called quiet.
///  3. <b>No car has crossed</b> for three of the field's typical laps, and
///     never less than <see cref="FieldQuietAtLeast"/>: the decoder is alive
///     but the loop isn't reaching it. Not while the checkered is out - cars
///     that have finished stop crossing, and that is not a fault.
///  4. <b>One car hasn't been seen</b> for three of its own typical laps, and
///     never less than <see cref="CarQuietAtLeast"/>, while others still
///     cross: stopped on track, or a flat transponder. A car with no timed
///     lap yet is measured against the field's typical lap. Only cars that
///     have crossed in this race are expected (the host's choice - see
///     MainForm.TimingSituation): the grid can't be trusted to say who is
///     racing, since with no round selected it is the LAST race's field.
///
/// Only during a real race that is running: not practice, not a demo, not
/// under red, and not before anyone has been released. Every silence is
/// measured from when racing (re)started at the earliest, so a red flag or
/// a wait for the first release is never counted as a car gone missing -
/// and from when each car was first expected, so a car released late in a
/// hand-released or staggered start isn't missing from the moment it goes.
/// While the decoder is off or quiet, nothing can be counted, so the counts
/// start again when it is back.
///
/// Times come from the host's clock, which should be monotonic (the host
/// uses Environment.TickCount64): the wall clock can step, and a step
/// forward would read as silence.
///
/// Passes are fed in by the host as the race counts them (LapProcessed), so
/// an alternate transponder's crossing is its driver's. Heartbeats arrive
/// on the decoder's own thread and are stored atomically; everything else
/// is the UI thread's.
/// </remarks>
internal sealed class TimingWatch
{
    internal static readonly TimeSpan HeartbeatGap = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan FieldQuietAtLeast = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan CarQuietAtLeast = TimeSpan.FromSeconds(20);

    /// <summary>How many typical laps of silence count as gone.</summary>
    internal const int QuietLaps = 3;

    /// <summary>A car the race expects to keep crossing.</summary>
    /// <param name="TypicalLapMs">Its usual lap so far; 0 before it has a timed lap.</param>
    internal sealed record Car(string Transponder, string Name, long TypicalLapMs);

    /// <param name="Racing">A real race is running and someone has been released, under green or yellow.</param>
    /// <param name="Finishing">The checkered is out.</param>
    /// <param name="DecoderConnected">The chosen decoder says it is connected.</param>
    /// <param name="Cars">The real cars still expected to cross - ghosts and those that have taken the flag left out.</param>
    /// <param name="HeartbeatsExpected">The chosen decoder is one that sends heartbeats (the EasyLap). False for the I-Lap, so an EasyLap heartbeat from before a switch is never read as the I-Lap going quiet.</param>
    internal sealed record Situation(bool Racing, bool Finishing, bool DecoderConnected, IReadOnlyList<Car> Cars, bool HeartbeatsExpected = true);

    /// <param name="Key">Which problem it is - the same while it lasts, however the seconds in the text change. The host logs when it changes.</param>
    internal sealed record Problem(string Key, string Text);

    private long _connectedTicks;
    private long _heartbeatTicks;

    private readonly Dictionary<string, DateTime> _lastPassUtc = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _racingSinceUtc;

    /// <summary>When each car was first expected in this spell of racing - its release, in a held or staggered start. Cleared when racing stops.</summary>
    private readonly Dictionary<string, DateTime> _expectedSinceUtc = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The decoder has just connected. Any thread.</summary>
    public void DecoderConnected(DateTime utc) => Interlocked.Exchange(ref _connectedTicks, utc.Ticks);

    /// <summary>A heartbeat has arrived from the decoder. Any thread.</summary>
    public void Heartbeat(DateTime utc) => Interlocked.Exchange(ref _heartbeatTicks, utc.Ticks);

    /// <summary>The race has counted - or rejected - a crossing by this car. UI thread.</summary>
    public void Pass(string transponder, DateTime utc) => _lastPassUtc[transponder] = utc;

    /// <summary>What is wrong now, or null. Call it regularly (the race tick): it notices racing starting and stopping as it goes.</summary>
    public Problem? Check(Situation situation, DateTime nowUtc)
    {
        if (!situation.Racing)
        {
            _racingSinceUtc = null;
            _expectedSinceUtc.Clear();
            return null;
        }

        _racingSinceUtc ??= nowUtc;

        foreach (var car in situation.Cars)
        {
            _expectedSinceUtc.TryAdd(car.Transponder, nowUtc);
        }

        // --- 1. Not connected ---
        if (!situation.DecoderConnected)
        {
            // Nothing can cross while it's off: every count starts again
            // once it is back, rather than blaming the loop or the cars.
            _racingSinceUtc = nowUtc;
            return new Problem("decoder-off", "The decoder isn't connected - no laps are being counted. Connect it on the Connection page.");
        }

        // --- 2. Connected, but quiet ---
        var connected = Interlocked.Read(ref _connectedTicks);
        var heartbeat = Interlocked.Read(ref _heartbeatTicks);

        if (situation.HeartbeatsExpected && heartbeat > connected && heartbeat > 0)
        {
            var quiet = nowUtc - new DateTime(heartbeat, DateTimeKind.Utc);

            if (quiet >= HeartbeatGap)
            {
                _racingSinceUtc = nowUtc;
                return new Problem("decoder-quiet", $"Nothing from the decoder for {Duration(quiet)} - check its USB cable.");
            }
        }

        var since = _racingSinceUtc.Value;

        var fieldTypical = Median(situation.Cars.Select(c => c.TypicalLapMs).Where(ms => ms > 0).ToList());

        // --- 3. Nobody crossing ---
        if (!situation.Finishing)
        {
            var fieldLimit = Longer(FieldQuietAtLeast, TimeSpan.FromMilliseconds(fieldTypical * QuietLaps));

            var lastAny = situation.Cars
                .Select(c => LastSeen(c.Transponder, since))
                .DefaultIfEmpty(since)
                .Max();

            var fieldQuiet = nowUtc - lastAny;

            if (fieldQuiet >= fieldLimit)
            {
                return new Problem("no-crossings", $"No car has crossed the line for {Duration(fieldQuiet)} - check the loop and its cable.");
            }
        }

        // --- 4. One car missing ---
        var missing = new List<(Car Car, TimeSpan Quiet)>();

        foreach (var car in situation.Cars)
        {
            var typical = car.TypicalLapMs > 0 ? car.TypicalLapMs : fieldTypical;

            if (typical <= 0)
            {
                continue; // nobody has a timed lap yet - nothing to measure against
            }

            var last = LastSeen(car.Transponder, since);
            var quiet = nowUtc - last;
            var limit = Longer(CarQuietAtLeast, TimeSpan.FromMilliseconds(typical * QuietLaps));

            if (quiet < limit)
            {
                continue;
            }

            // Others still crossing - otherwise it is check 3's problem, not
            // this car's. Under the checkered the others have stopped on
            // purpose, so a car still out there and silent is worth saying.
            var othersCrossing = situation.Finishing || situation.Cars.Any(other =>
                !string.Equals(other.Transponder, car.Transponder, StringComparison.OrdinalIgnoreCase)
                && nowUtc - LastSeen(other.Transponder, since) < limit);

            if (othersCrossing)
            {
                missing.Add((car, quiet));
            }
        }

        if (missing.Count == 0)
        {
            return null;
        }

        var ordered = missing.OrderByDescending(m => m.Quiet).ToList();
        var named = string.Join(", ", ordered.Take(2).Select(m => $"{m.Car.Name} ({Duration(m.Quiet)})"));
        var more = ordered.Count > 2 ? $" and {ordered.Count - 2} more" : string.Empty;
        var key = "missing:" + string.Join(",", ordered.Select(m => m.Car.Transponder).OrderBy(t => t, StringComparer.OrdinalIgnoreCase));

        return new Problem(key, ordered.Count == 1
            ? $"Not seen: {named} - stopped, or a flat transponder?"
            : $"Not seen: {named}{more} - stopped, or flat transponders?");
    }

    /// <summary>When this car last crossed - but never before racing (re)started, or before it was first expected.</summary>
    private DateTime LastSeen(string transponder, DateTime since)
    {
        var from = _expectedSinceUtc.TryGetValue(transponder, out var expected) && expected > since ? expected : since;

        return _lastPassUtc.TryGetValue(transponder, out var last) && last > from ? last : from;
    }

    private static TimeSpan Longer(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    private static long Median(List<long> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>"45s", or "1:05" from a minute up.</summary>
    internal static string Duration(TimeSpan span) =>
        span.TotalSeconds < 60
            ? $"{(int)span.TotalSeconds}s"
            : $"{(int)span.TotalMinutes}:{span.Seconds:00}";
}
