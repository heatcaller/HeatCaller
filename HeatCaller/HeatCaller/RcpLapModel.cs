namespace HeatCaller;

/// <summary>Where the lap time on the board comes from.</summary>
public enum RcpLapSource
{
    /// <summary>No racing line, so no lap.</summary>
    None,

    /// <summary>The real thing: laps raced on this layout since its line last changed.</summary>
    Raced,

    /// <summary>The average lap typed in on Track Layouts.</summary>
    Entered,

    /// <summary>Worked out from the plan (RcpLapModel.Estimate).</summary>
    Estimate,
}

/// <summary>A lap time and where it came from.</summary>
/// <param name="Seconds">The lap, in seconds; 0 with no source.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="Laps">For Raced: how many laps it is worked out from.</param>
/// <param name="Tuned">For Estimate: how many raced layouts the estimate is tuned against (0 = the model's own numbers).</param>
public sealed record RcpLapTime(double Seconds, RcpLapSource Source, int Laps = 0, int Tuned = 0)
{
    public static RcpLapTime Nothing { get; } = new(0, RcpLapSource.None);

    /// <summary>What the board's lap chip says after the time: "from 42 laps raced", "as entered", "estimate".</summary>
    public string Says => Source switch
    {
        RcpLapSource.Raced => $"from {Laps} laps raced",
        RcpLapSource.Entered => "as entered",
        RcpLapSource.Estimate => "estimate",
        _ => string.Empty,
    };
}

/// <summary>
/// The lap, always on screen (Track Designer overhaul, Phase 6, 25 Oct
/// 2026): how long a lap of a plan takes, and where that number comes from.
///
/// In order of trust:
///   1. RACED - the median lap from this layout's saved races (sessions
///      filed on its track layout name, demo races left out), counting only
///      races since its racing line last changed, and only with enough laps
///      to mean something;
///   2. AS ENTERED - the average lap the club typed on Track Layouts;
///   3. ESTIMATE - worked out from the plan with a simple car model.
/// A line changed in the designer since it was opened takes the estimate:
/// the raced and entered times are for the track as it was.
///
/// THE ESTIMATE, stated plainly as simple:
///   - the racing line is cut at its corners into straights;
///   - each corner is taken at the speed its radius allows (the line's
///     distance from the turn rail's centre, or a tight half tile at a
///     square corner): v = sqrt(grip x radius);
///   - each straight is driven flat out between its two corners -
///     accelerating out of one, braking for the next, never past top
///     speed.
/// Its numbers are a stock Mini-Z on foam; nobody's car is that car. So
/// it TUNES ITSELF: every layout the club has both a racing line for and
/// races filed on gives a ratio of real to estimated, and the median ratio
/// scales every estimate (Calibrate). The more the club races, the closer
/// it gets - rather than numbers guessed here pretending to be measured.
/// </summary>
public static class RcpLapModel
{
    /// <summary>The car the estimate drives - a stock Mini-Z on RCP foam, to start from.</summary>
    public sealed record Car(double TopSpeed, double Accelerate, double Brake, double Grip)
    {
        /// <summary>Metres a second, metres a second squared - a stock Mini-Z: about 18 km/h flat out.</summary>
        public static Car MiniZ { get; } = new(TopSpeed: 5.0, Accelerate: 5.0, Brake: 7.0, Grip: 6.0);
    }

    /// <summary>The fewest laps a raced time is worked out from.</summary>
    public const int EnoughLaps = 10;

    /// <summary>How far the line has to turn at a point to be a corner, in radians - 12 degrees.</summary>
    private const double CornerAngle = 12 * Math.PI / 180;

    /// <summary>
    /// Seconds a lap of the plan's racing line takes the car, scaled by the
    /// club's tuning (1 = the model's own numbers). Null with no closed line.
    /// </summary>
    public static double? Estimate(RcpTrackDesign design, Car? car = null, double tuning = 1.0)
    {
        if (design is null || design.Route.Count < 3 || !design.RouteClosed || design.TileSizeMm <= 0)
        {
            return null;
        }

        car ??= Car.MiniZ;

        var metresPerTile = design.TileSizeMm / 1000.0;

        // A point put down twice (a double-click) is one point: a leg of no
        // length would hide the corner at it.
        var points = new List<PointF>();

        foreach (var point in design.Route.Select(p => p.Point))
        {
            if (points.Count == 0 || Length(Minus(point, points[^1])) > 1e-4)
            {
                points.Add(point);
            }
        }

        while (points.Count > 1 && Length(Minus(points[0], points[^1])) <= 1e-4)
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count < 3)
        {
            return null;
        }

        // The corners: points where the line turns by more than a wobble -
        // a hand-drawn straight is never quite straight. Measured as an
        // angle, so turning straight back (180 degrees) is a corner too.
        var corners = new List<int>();

        for (var i = 0; i < points.Count; i++)
        {
            var into = Minus(points[i], points[(i - 1 + points.Count) % points.Count]);
            var outOf = Minus(points[(i + 1) % points.Count], points[i]);

            var angle = Math.Atan2(Math.Abs(Cross(into, outOf)), Dot(into, outOf));

            if (angle > CornerAngle)
            {
                corners.Add(i);
            }
        }

        var total = 0.0;

        // No corners at all - a line drawn round and round on itself - is
        // one long straight at top speed.
        if (corners.Count == 0)
        {
            return RcpPlan.RouteLengthInTiles(design) * metresPerTile / car.TopSpeed * tuning;
        }

        var cornerSpeed = corners
            .Select(i => Math.Min(car.TopSpeed, Math.Sqrt(car.Grip * CornerRadiusTiles(design, points[i]) * metresPerTile)))
            .ToList();

        for (var c = 0; c < corners.Count; c++)
        {
            var from = corners[c];
            var to = corners[(c + 1) % corners.Count];

            // The straight's length, along the line through any stations on it.
            // At least one step: with one corner, the straight from it is
            // the whole way round back to it.
            var length = 0.0;
            var i = from;

            do
            {
                length += Length(Minus(points[(i + 1) % points.Count], points[i]));
                i = (i + 1) % points.Count;
            }
            while (i != to);

            total += Straight(length * metresPerTile, cornerSpeed[c], cornerSpeed[(c + 1) % corners.Count], car);
        }

        return total * tuning;
    }

    /// <summary>
    /// How tight the line turns at a corner, in tiles: its distance from the
    /// centre of the turn rail's arc there - a turn rail within reach whose
    /// arc the line goes round - or half a tile at a square corner.
    /// </summary>
    private static double CornerRadiusTiles(RcpTrackDesign design, PointF corner)
    {
        var best = (Gap: double.MaxValue, Radius: 0.5);

        foreach (var curve in design.Curves)
        {
            // Where the painter puts the arc's centre (RcpTrackPainter.DrawCurve).
            var radius = Math.Max(0.25, curve.Radius);
            var (dx, dy) = RcpAutoRails.Step(curve.Quadrant);
            var centre = new PointF((float)(curve.Col + dx * radius), (float)(curve.Row + dy * radius));
            var offset = Minus(corner, centre);
            var distance = Length(offset);

            // Only the quarter the rail stands on - towards its anchor from
            // the centre - and within reach of it; the nearest arc wins.
            var inQuarter = Math.Sign(offset.X) != Math.Sign(dx) && Math.Sign(offset.Y) != Math.Sign(dy);
            var gap = Math.Abs(radius - distance);

            if (inQuarter && gap < 2.5 && gap < best.Gap)
            {
                best = (gap, distance);
            }
        }

        return Math.Max(0.25, best.Radius);
    }

    /// <summary>
    /// Seconds to drive a straight from one corner speed to the next:
    /// accelerating out of the first, braking for the second, never past
    /// top speed.
    /// </summary>
    internal static double Straight(double metres, double vIn, double vOut, Car car)
    {
        if (metres <= 0)
        {
            return 0;
        }

        // The peak speed reached where accelerating and braking meet.
        var peakSquared = (2 * car.Accelerate * car.Brake * metres + car.Brake * vIn * vIn + car.Accelerate * vOut * vOut)
                          / (car.Accelerate + car.Brake);

        // A straight too short to get from one corner's speed to the next:
        // taken at the slower of the two (rough, and rare). Told from the
        // peak before it is capped, so a corner already at top speed is not
        // mistaken for one.
        if (peakSquared <= Math.Max(vIn * vIn, vOut * vOut) + 1e-9)
        {
            return metres / Math.Max(0.1, Math.Min(vIn, vOut));
        }

        var peak = Math.Min(car.TopSpeed, Math.Sqrt(peakSquared));

        // Both corners at or over top speed: flat out the whole way. Only
        // both - with one corner at top speed and the other slower, there is
        // still the braking for (or the run up from) the slower one, which
        // the sum below gives.
        if (peak <= Math.Min(vIn, vOut))
        {
            return metres / peak;
        }

        var accelDistance = (peak * peak - vIn * vIn) / (2 * car.Accelerate);
        var brakeDistance = (peak * peak - vOut * vOut) / (2 * car.Brake);
        var cruise = Math.Max(0, metres - accelDistance - brakeDistance);

        return (peak - vIn) / car.Accelerate + (peak - vOut) / car.Brake + cruise / peak;
    }

    /// <summary>
    /// The median lap raced on a layout, from its saved races since a time
    /// (the line's last change; null = all of them). Demo races are left
    /// out, and laps three times the median or more (a crash, a car fetched
    /// off the floor) before the median is taken. Null with fewer than
    /// EnoughLaps.
    /// </summary>
    public static (double Seconds, int Laps)? Raced(IEnumerable<SessionRecord> sessions, string layoutName, DateTime? sinceUtc)
    {
        if (sessions is null || string.IsNullOrWhiteSpace(layoutName))
        {
            return null;
        }

        var laps = sessions
            .Where(s => s is not null
                        && string.Equals(s.TrackLayoutName?.Trim(), layoutName.Trim(), StringComparison.OrdinalIgnoreCase)
                        && !s.IsNotScored()
                        && (sinceUtc is null || s.StartedAtUtc >= sinceUtc))
            .SelectMany(s => s.Results ?? new List<SessionResultEntry>())
            .Where(r => r is not null)
            .SelectMany(r => r.LapTimesMs ?? new List<long>())
            .Where(ms => ms > 0)
            .Select(ms => ms / 1000.0)
            .ToList();

        if (laps.Count < EnoughLaps)
        {
            return null;
        }

        var rough = Median(laps);
        var kept = laps.Where(s => s < rough * 3).ToList();

        return kept.Count < EnoughLaps ? null : (Median(kept), kept.Count);
    }

    /// <summary>
    /// The club's tuning: the median of real lap over estimated lap, across
    /// every layout given with both a racing line and enough laps raced on
    /// it. 1 and 0 layouts with none.
    /// </summary>
    public static (double Tuning, int Layouts) Calibrate(IEnumerable<(RcpTrackDesign Design, double RacedSeconds)> raced, Car? car = null)
    {
        var ratios = new List<double>();

        foreach (var (design, seconds) in raced ?? Array.Empty<(RcpTrackDesign, double)>())
        {
            // Each held to between a third and three times: a layout filed
            // under the wrong name, or a line nothing like the track raced,
            // mustn't set the estimate for every other layout.
            if (seconds > 0 && Estimate(design, car) is { } estimate && estimate > 0)
            {
                ratios.Add(Math.Clamp(seconds / estimate, 1.0 / 3, 3.0));
            }
        }

        return ratios.Count == 0 ? (1.0, 0) : (Median(ratios), ratios.Count);
    }

    /// <summary>
    /// The lap the board shows, by the order of trust in the class notes.
    /// <paramref name="lineChanged"/>: the racing line is not the one the
    /// raced and entered times were for.
    /// </summary>
    public static RcpLapTime Choose(
        RcpTrackDesign design,
        (double Seconds, int Laps)? raced,
        double? entered,
        bool lineChanged,
        double tuning = 1.0,
        int tunedLayouts = 0)
    {
        if (design is null || design.Route.Count < 3)
        {
            return RcpLapTime.Nothing;
        }

        if (!lineChanged && raced is { Seconds: > 0 } real)
        {
            return new RcpLapTime(real.Seconds, RcpLapSource.Raced, Laps: real.Laps);
        }

        // The entered average is for the line as it was: once a changed
        // line has been saved (RouteChangedUtc), it is the estimate until
        // the new line has been raced.
        if (!lineChanged && entered is > 0 && design.RouteChangedUtc is null)
        {
            return new RcpLapTime(entered.Value, RcpLapSource.Entered);
        }

        return Estimate(design, tuning: tuning) is { } estimate
            ? new RcpLapTime(estimate, RcpLapSource.Estimate, Tuned: tunedLayouts)
            : RcpLapTime.Nothing;
    }

    /// <summary>
    /// Whether two racing lines are the same line: the same points in the
    /// same order - for a closed lap, from any point and either way round,
    /// since tracing it again from a moved finish line, or turning it round
    /// with Reverse, is not a different track.
    /// </summary>
    public static bool SameLine(IReadOnlyList<RcpRoutePoint>? a, IReadOnlyList<RcpRoutePoint>? b, bool closed = true)
    {
        a ??= Array.Empty<RcpRoutePoint>();
        b ??= Array.Empty<RcpRoutePoint>();

        if (a.Count != b.Count)
        {
            return false;
        }

        if (a.Count == 0)
        {
            return true;
        }

        bool Same(RcpRoutePoint p, RcpRoutePoint q) =>
            Math.Abs(p.X - q.X) < 0.001f && Math.Abs(p.Y - q.Y) < 0.001f;

        if (!closed)
        {
            return a.Zip(b).All(pair => Same(pair.First, pair.Second));
        }

        var n = a.Count;

        for (var start = 0; start < n; start++)
        {
            if (!Same(a[0], b[start]))
            {
                continue;
            }

            var forward = true;
            var backward = true;

            for (var k = 0; k < n && (forward || backward); k++)
            {
                forward &= Same(a[k], b[(start + k) % n]);
                backward &= Same(a[k], b[(start - k + n) % n]);
            }

            if (forward || backward)
            {
                return true;
            }
        }

        return false;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static PointF Minus(PointF a, PointF b) => new(a.X - b.X, a.Y - b.Y);

    private static double Cross(PointF a, PointF b) => a.X * b.Y - a.Y * b.X;

    private static double Dot(PointF a, PointF b) => a.X * b.X + a.Y * b.Y;

    private static double Length(PointF v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);
}
