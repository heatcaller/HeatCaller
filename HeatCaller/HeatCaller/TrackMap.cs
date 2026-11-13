namespace HeatCaller;

/// <summary>
/// The racing line of a track plan, ready to put cars on: closed into a lap,
/// starting at the finish line, with the split loops as fractions of the
/// lap. Built from the Track Designer's plan (RcpTrackDesign) - its drawn
/// racing line, its finish line and its Split 1-3 markings - so a club that
/// has drawn its track gets a live map for nothing more.
/// </summary>
public sealed class TrackLine
{
    private readonly float[] _travelled;

    private TrackLine(PointF[] points, float[] splitAt)
    {
        Points = points;
        SplitAt = splitAt;

        _travelled = new float[points.Length];

        for (var i = 1; i < points.Length; i++)
        {
            _travelled[i] = _travelled[i - 1] + RcpPlan.Distance(points[i - 1], points[i]);
        }

        Length = _travelled[^1];

        var left = points.Min(p => p.X);
        var top = points.Min(p => p.Y);
        Bounds = new RectangleF(left, top, Math.Max(0.01f, points.Max(p => p.X) - left), Math.Max(0.01f, points.Max(p => p.Y) - top));
    }

    /// <summary>The line, in tiles, from the finish line round and back to it - the first point and the last are the same place.</summary>
    public IReadOnlyList<PointF> Points { get; }

    /// <summary>Where each split loop is, as a fraction of the lap from the finish line, in the order cars reach them.</summary>
    public IReadOnlyList<float> SplitAt { get; }

    public float Length { get; }

    public RectangleF Bounds { get; }

    /// <summary>
    /// The line of <paramref name="design"/>, or null when it can't carry
    /// cars: no racing line, one that doesn't close into a lap, or no finish
    /// line on it. Split loops that can't be placed (RcpPlan.MeasureSectors
    /// says why) are left off; the map still works from the finish line.
    /// </summary>
    public static TrackLine? From(RcpTrackDesign? design)
    {
        if (design is null || design.Route.Count < 3 || !design.RouteClosed)
        {
            return null;
        }

        var loop = design.Route.Select(p => p.Point).ToList();
        loop.Add(loop[0]);

        // Where the finish line crosses the line, and the lap rebuilt to
        // start there. 6 Nov 2026: a finish line across a wide lane is one
        // decal per tile; try them top-left first (lowest Row, then Col) and
        // take the first the line passes over, as RcpPlan.MeasureSectors
        // does - not just the first in the list, which may be an outer tile
        // the line never comes near.
        var start = design.Decals
            .Where(d => d is not null && d.Kind == RcpDecalKind.FinishLine)
            .OrderBy(d => d.Row)
            .ThenBy(d => d.Col)
            .Select(f => Nearest(loop, new PointF(f.Col + 0.5f, f.Row + 0.5f)))
            .FirstOrDefault(s => s is not null);

        if (start is not { } found)
        {
            return null;
        }

        var startPoint = found.Point;
        var startSegment = found.Segment;

        var points = new List<PointF> { startPoint };

        for (var i = startSegment + 1; i < loop.Count; i++)
        {
            points.Add(loop[i]);
        }

        for (var i = 1; i <= startSegment; i++)
        {
            points.Add(loop[i]);
        }

        points.Add(startPoint);

        var splits = Array.Empty<float>();

        if (RcpPlan.MeasureSectors(design) is { Sectors: { Length: >= 2 } sectors })
        {
            var total = sectors.Sum();
            var running = 0f;
            var at = new List<float>();

            for (var i = 0; i < sectors.Length - 1; i++)
            {
                running += sectors[i];
                at.Add(total > 0 ? running / total : 0);
            }

            splits = at.ToArray();
        }

        var line = new TrackLine(points.ToArray(), splits);
        return line.Length > 0 ? line : null;
    }

    /// <summary>The point <paramref name="fraction"/> of the way round from the finish line (wrapped into 0..1).</summary>
    public PointF At(float fraction)
    {
        fraction -= MathF.Floor(fraction);
        var distance = fraction * Length;

        for (var i = 1; i < _travelled.Length; i++)
        {
            if (_travelled[i] >= distance)
            {
                var segment = _travelled[i] - _travelled[i - 1];
                var t = segment <= 0 ? 0 : (distance - _travelled[i - 1]) / segment;
                var a = Points[i - 1];
                var b = Points[i];
                return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }
        }

        return Points[^1];
    }

    /// <summary>The nearest point on the line to <paramref name="target"/> and the segment it is on, when within RcpPlan.OnRouteTiles.</summary>
    private static (PointF Point, int Segment)? Nearest(List<PointF> line, PointF target)
    {
        (PointF, int)? best = null;
        var bestGap = float.MaxValue;

        for (var i = 1; i < line.Count; i++)
        {
            var a = line[i - 1];
            var b = line[i];
            var length = RcpPlan.Distance(a, b);

            if (length <= 0)
            {
                continue;
            }

            var t = Math.Clamp(((target.X - a.X) * (b.X - a.X) + (target.Y - a.Y) * (b.Y - a.Y)) / (length * length), 0f, 1f);
            var nearest = new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            var gap = RcpPlan.Distance(nearest, target);

            if (gap < bestGap)
            {
                bestGap = gap;
                best = (nearest, i - 1);
            }
        }

        return bestGap <= RcpPlan.OnRouteTiles ? best : null;
    }
}

/// <summary>
/// Where each car is on the track, worked out from when it last crossed a
/// loop: the finish line or a split loop. Between loops a car is moved along
/// at its usual pace and held just short of the next loop until that loop
/// hears it - so a dot never runs ahead of the timing. A car that is well
/// overdue at the next loop has probably stopped, and is shown as such, in
/// the sector it stopped in (the stopped-car locator).
///
/// Fed by the race's own events (lap counted, split crossed) on the UI
/// thread, and asked on the UI thread (the Big Screen, the web page's state
/// build). Times are TimingClock's - the app's, not a decoder's - since the
/// question is only "how long ago", and that clock never steps.
/// </summary>
public sealed class TrackPositions
{
    /// <summary>How much longer than usual a car can take to reach the next loop before it is called stopped.</summary>
    internal const double OverdueFactor = 2.0;

    /// <summary>And never less than this much late, so a quick sector is not called stopped over one slow corner.</summary>
    internal const long OverdueAtLeastMs = 8_000;

    /// <summary>Where a car is: its place in the race, how far round (0..1), and, when it seems to have stopped, where.</summary>
    /// <param name="Where">The stretch of track it stopped in - "between Split 1 and the line" - or empty when it is moving.</param>
    /// <param name="Rate">How far round it goes a second at its usual pace (0 when stopped), and <paramref name="Cap"/>
    /// how far it may go before the next loop must hear it - so a page polled once a second can move it smoothly between polls.</param>
    /// <param name="Ghost">Where the car would be on this lap if it were running its best lap of the race (0..1), or -1 when it
    /// has no best lap yet or has stopped - the best-lap ghost. <paramref name="GhostRate"/> is how far round the ghost goes a second.</param>
    public sealed record Car(string Transponder, int Position, float Fraction, bool Stopped, string Where, float Rate = 0, float Cap = 1,
        float Ghost = -1, float GhostRate = 0);

    /// <summary>When a car last crossed the line and its last split loop (TimingClock).</summary>
    internal sealed class Seen
    {
        public long LineAt;
        public int Split;
        public long SplitAt;

        /// <summary>
        /// The split loops this car has been heard at this race, a bit each
        /// (bit 0 = Split 1). Only these are waited for: a split drawn on
        /// the plan may have no decoder behind it tonight - the split loop
        /// switched off, unplugged, or deaf to this transponder - and a car
        /// held short of a loop that never hears it was called stopped on
        /// every lap.
        /// </summary>
        public int SplitsHeard;
    }

    private readonly Dictionary<string, Seen> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _saidStopped = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When the red flag came out (TimingClock), while it is out - see Resumed.</summary>
    private long? _redSince;

    /// <summary>A car has just been worked out to have stopped - once per stop. The host says so in the console.</summary>
    public event Action<RacerStanding, string>? CarStopped;

    /// <summary>
    /// Follows <paramref name="race"/>: every counted crossing and every
    /// split, a fresh start when a race is staged, counted down or reset, and
    /// the time under a red flag taken out (see Resumed).
    /// </summary>
    public void Attach(RaceTiming race)
    {
        race.LapProcessed += (standing, _, _) => Crossed(standing.TransponderId, TimingPoint.Line, TimingClock.NowMs);
        race.SplitCrossed += (standing, point, _) => Crossed(standing.TransponderId, point, TimingClock.NowMs);
        race.StateChanged += state =>
        {
            // A countdown can start straight from a finished race, whose
            // cars would otherwise crawl round the map until the green.
            if (state is RaceState.Idle or RaceState.Staging or RaceState.Countdown)
            {
                Clear();
            }
        };
        race.FlagChanged += flag =>
        {
            if (flag == FlagState.Red)
            {
                _redSince ??= TimingClock.NowMs;
            }
            else if (_redSince is { } since)
            {
                _redSince = null;
                Resumed(TimingClock.NowMs - since);
            }
        };
    }

    public void Clear()
    {
        _seen.Clear();
        _saidStopped.Clear();
        _redSince = null;
    }

    /// <summary>
    /// The race is back on after <paramref name="stoppedMs"/> under a red
    /// flag. Nobody was going round meanwhile, so every car's last loop is
    /// moved on by that much: counted from the stoppage, every car was well
    /// overdue at its next loop, and the whole field was called stopped the
    /// moment the flag went green.
    /// </summary>
    public void Resumed(long stoppedMs)
    {
        if (stoppedMs <= 0)
        {
            return;
        }

        foreach (var seen in _seen.Values)
        {
            if (seen.LineAt != 0)
            {
                seen.LineAt += stoppedMs;
            }

            if (seen.SplitAt != 0)
            {
                seen.SplitAt += stoppedMs;
            }
        }

        _saidStopped.Clear();
    }

    /// <summary>A car crossed <paramref name="point"/> at <paramref name="nowMs"/> (TimingClock).</summary>
    public void Crossed(string transponder, TimingPoint point, long nowMs)
    {
        if (!_seen.TryGetValue(transponder, out var seen))
        {
            seen = new Seen();
            _seen[transponder] = seen;
        }

        if (point == TimingPoint.Line)
        {
            seen.LineAt = nowMs;
            seen.Split = 0;
        }
        else
        {
            seen.Split = (int)point;
            seen.SplitAt = nowMs;

            if (seen.Split is >= 1 and <= 31)
            {
                seen.SplitsHeard |= 1 << (seen.Split - 1);
            }
        }

        _saidStopped.Remove(transponder);
    }

    /// <summary>
    /// Every car on <paramref name="ordered"/> that can be placed on
    /// <paramref name="line"/> now: one that has crossed a loop in this race
    /// and has a usual lap (its own, or the field's). Stopped is only
    /// decided while <paramref name="racing"/>, and never for a car that has
    /// taken the flag (<paramref name="finished"/>) - its crossings after
    /// that are not counted, so it would look overdue while the rest finish.
    /// </summary>
    public IReadOnlyList<Car> Estimate(IReadOnlyList<RacerStanding> ordered, TrackLine line, long nowMs, bool racing, Func<string, bool>? finished = null)
    {
        var cars = new List<Car>();

        var typicals = ordered.ToDictionary(s => s.TransponderId, Typical, StringComparer.OrdinalIgnoreCase);
        var field = Median(typicals.Values.Where(ms => ms > 0));

        for (var i = 0; i < ordered.Count; i++)
        {
            var standing = ordered[i];

            if (!_seen.TryGetValue(standing.TransponderId, out var seen) || seen.LineAt == 0)
            {
                continue;
            }

            var typical = typicals[standing.TransponderId] > 0 ? typicals[standing.TransponderId] : field;

            if (typical <= 0)
            {
                continue;
            }

            // Never a ghost: there is no car to send a marshal to, and it
            // only ever crosses the line - so on a plan with a split loop a
            // running ghost was called stopped on every lap, short of a loop
            // it never crosses.
            var stillRacing = racing && !Ghosts.IsGhost(standing.TransponderId) && finished?.Invoke(standing.TransponderId) != true;
            var car = Place(standing.TransponderId, i + 1, seen, line.SplitAt, typical, nowMs, stillRacing);

            if (!car.Stopped && BestLap(standing) is var best and > 0)
            {
                car = car with { Ghost = GhostAt(seen.LineAt, best, nowMs), GhostRate = 1000f / best };
            }

            cars.Add(car);

            if (car.Stopped && _saidStopped.Add(standing.TransponderId))
            {
                CarStopped?.Invoke(standing, car.Where);
            }
        }

        return cars;
    }

    /// <summary>One car's place, from its last loop - the arithmetic, apart for testing.</summary>
    internal static Car Place(string transponder, int position, Seen seen, IReadOnlyList<float> splitAt, long typicalMs, long nowMs, bool racing)
    {
        // The last loop it crossed, and the next it should reach: the next
        // split loop that has heard this car this race, else the line. A
        // split on the plan that has never heard it is not waited for - see
        // Seen.SplitsHeard.
        var fromSplit = seen.Split > 0 && seen.SplitAt >= seen.LineAt && seen.Split <= splitAt.Count ? seen.Split : 0;
        var from = fromSplit == 0 ? 0f : splitAt[fromSplit - 1];
        var at = fromSplit == 0 ? seen.LineAt : seen.SplitAt;
        var nextSplit = 0;

        for (var i = fromSplit + 1; i <= Math.Min(splitAt.Count, 31) && nextSplit == 0; i++)
        {
            if ((seen.SplitsHeard & (1 << (i - 1))) != 0)
            {
                nextSplit = i;
            }
        }

        var next = nextSplit > 0 ? splitAt[nextSplit - 1] : 1f;

        var since = Math.Max(0, nowMs - at);
        var expected = typicalMs * (double)(next - from);
        var overdue = Math.Max(expected * OverdueFactor, expected + OverdueAtLeastMs);

        if (racing && since > overdue)
        {
            // Somewhere in that stretch - shown halfway along it.
            var middle = from + (next - from) / 2;
            var where = fromSplit == 0 && nextSplit == 0
                ? Stretch(0, 0)
                : $"between {(fromSplit == 0 ? "the line" : $"Split {fromSplit}")} and {(nextSplit == 0 ? "the line" : $"Split {nextSplit}")}";

            return new Car(transponder, position, middle, true, where, 0, middle);
        }

        var fraction = from + (float)(since / (double)typicalMs);

        // Held just short of the next loop until it is heard there.
        fraction = Math.Min(fraction, next - 0.01f);

        return new Car(transponder, position, Math.Max(from, fraction), false, string.Empty, 1000f / typicalMs, next - 0.01f);
    }

    /// <summary>The same, for a car that every split loop on the plan has heard this race.</summary>
    internal static Car Place(string transponder, int position, long lineAt, int split, long splitAt, IReadOnlyList<float> splitFractions, long typicalMs, long nowMs, bool racing) =>
        Place(transponder, position, new Seen { LineAt = lineAt, Split = split, SplitAt = splitAt, SplitsHeard = -1 }, splitFractions, typicalMs, nowMs, racing);

    /// <summary>
    /// The best-lap ghost: how far round the car would be by
    /// <paramref name="nowMs"/> had it left the line at
    /// <paramref name="lineAt"/> on its best lap. It waits at the line once
    /// a best lap's time has passed - from there on the car is down on it.
    /// </summary>
    internal static float GhostAt(long lineAt, long bestMs, long nowMs) =>
        bestMs <= 0 ? -1 : (float)Math.Clamp((nowMs - lineAt) / (double)bestMs, 0, 1);

    /// <summary>A car's best timed lap this race - the start lap, from the green, is not one. 0 before it has one.</summary>
    private static long BestLap(RacerStanding standing) =>
        RaceGaps.PaceLaps(standing).Where(ms => ms > 0).DefaultIfEmpty(0).Min();

    /// <summary>The stretch after loop <paramref name="fromSplit"/> (0 = the line), in words.</summary>
    internal static string Stretch(int fromSplit, int splits)
    {
        if (splits == 0)
        {
            return "somewhere on the lap";
        }

        var from = fromSplit == 0 ? "the line" : $"Split {fromSplit}";
        var to = fromSplit >= splits ? "the line" : $"Split {fromSplit + 1}";
        return $"between {from} and {to}";
    }

    /// <summary>A car's usual lap: the median of its timed laps after the first. 0 before it has one.</summary>
    private static long Typical(RacerStanding standing) => (long)Median(RaceGaps.PaceLaps(standing).Where(ms => ms > 0).Select(ms => (double)ms));

    private static long Median(IEnumerable<long> values) => (long)Median(values.Select(v => (double)v));

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }
}
