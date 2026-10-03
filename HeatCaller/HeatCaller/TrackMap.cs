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

        if (design.Decals.FirstOrDefault(d => d is not null && d.Kind == RcpDecalKind.FinishLine) is not { } finish)
        {
            return null;
        }

        var loop = design.Route.Select(p => p.Point).ToList();
        loop.Add(loop[0]);

        // Where the finish line crosses the line, and the lap rebuilt to
        // start there.
        if (Nearest(loop, new PointF(finish.Col + 0.5f, finish.Row + 0.5f)) is not { } start)
        {
            return null;
        }

        var points = new List<PointF> { start.Point };

        for (var i = start.Segment + 1; i < loop.Count; i++)
        {
            points.Add(loop[i]);
        }

        for (var i = 1; i <= start.Segment; i++)
        {
            points.Add(loop[i]);
        }

        points.Add(start.Point);

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
    public sealed record Car(string Transponder, int Position, float Fraction, bool Stopped, string Where, float Rate = 0, float Cap = 1);

    /// <summary>When a car last crossed the line and its last split loop (TimingClock).</summary>
    internal sealed class Seen
    {
        public long LineAt;
        public int Split;
        public long SplitAt;
    }

    private readonly Dictionary<string, Seen> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _saidStopped = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A car has just been worked out to have stopped - once per stop. The host says so in the console.</summary>
    public event Action<RacerStanding, string>? CarStopped;

    /// <summary>Follows <paramref name="race"/>: every counted crossing and every split, and a fresh start when a race is staged or reset.</summary>
    public void Attach(RaceControlSession race)
    {
        race.LapProcessed += (standing, _, _) => Crossed(standing.TransponderId, TimingPoint.Line, TimingClock.NowMs);
        race.SplitCrossed += (standing, point, _) => Crossed(standing.TransponderId, point, TimingClock.NowMs);
        race.StateChanged += state =>
        {
            if (state is RaceState.Idle or RaceState.Staging)
            {
                Clear();
            }
        };
    }

    public void Clear()
    {
        _seen.Clear();
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
        }

        _saidStopped.Remove(transponder);
    }

    /// <summary>
    /// Every car on <paramref name="ordered"/> that can be placed on
    /// <paramref name="line"/> now: one that has crossed a loop in this race
    /// and has a usual lap (its own, or the field's). Stopped is only
    /// decided while <paramref name="racing"/>.
    /// </summary>
    public IReadOnlyList<Car> Estimate(IReadOnlyList<RacerStanding> ordered, TrackLine line, long nowMs, bool racing)
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

            var car = Place(standing.TransponderId, i + 1, seen, line.SplitAt, typical, nowMs, racing);
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
        // The last loop it crossed, and the next it should reach.
        var fromSplit = seen.Split > 0 && seen.SplitAt >= seen.LineAt && seen.Split <= splitAt.Count ? seen.Split : 0;
        var from = fromSplit == 0 ? 0f : splitAt[fromSplit - 1];
        var at = fromSplit == 0 ? seen.LineAt : seen.SplitAt;
        var next = fromSplit < splitAt.Count ? splitAt[fromSplit] : 1f;

        var since = Math.Max(0, nowMs - at);
        var expected = typicalMs * (double)(next - from);
        var overdue = Math.Max(expected * OverdueFactor, expected + OverdueAtLeastMs);

        if (racing && since > overdue)
        {
            // Somewhere in that stretch - shown halfway along it.
            var middle = from + (next - from) / 2;
            return new Car(transponder, position, middle, true, Stretch(fromSplit, splitAt.Count), 0, middle);
        }

        var fraction = from + (float)(since / (double)typicalMs);

        // Held just short of the next loop until it is heard there.
        fraction = Math.Min(fraction, next - 0.01f);

        return new Car(transponder, position, Math.Max(from, fraction), false, string.Empty, 1000f / typicalMs, next - 0.01f);
    }

    internal static Car Place(string transponder, int position, long lineAt, int split, long splitAt, IReadOnlyList<float> splitFractions, long typicalMs, long nowMs, bool racing) =>
        Place(transponder, position, new Seen { LineAt = lineAt, Split = split, SplitAt = splitAt }, splitFractions, typicalMs, nowMs, racing);

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
