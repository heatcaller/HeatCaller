namespace HeatCaller;

/// <summary>
/// The rails a lane puts down by itself (Track Designer overhaul, Phase 3,
/// 25 Oct 2026).
///
/// On a real track you think in LANES - "a lane two tiles wide down here,
/// round the end and back" - but the designer used to make you think in
/// SEAMS, one rail at a time. The Lane tool paints the lane (RcpTile.Lane)
/// and this works out the rails round it, every time the lane changes:
///
///   - a rail on every seam between a lane tile and anything that is not
///     the same lane - single where the other side is not a lane, two back
///     to back (a divider, RcpWall.Double) where it is a different lane
///     running alongside;
///   - a turn rail on the OUTSIDE of every bend, at the plan's lane turn
///     radius (RcpTrackDesign.LaneTurnRadius), taking the place of the
///     straights it sweeps across - the same shape the Rails tool's drag
///     and RCP's own layouts make (RcpStockLayout.Perimeter);
///   - the INSIDE of a bend left square: two straights meeting, which is
///     RCP's inner corner piece, as on every one of RCP's own layouts.
///
/// THE RULES IT KEEPS, the same ones the rest of the designer keeps:
///
///   - Hand rails are never touched. Only pieces with Auto set are taken
///     off and worked out again; a hand rail on a seam keeps that seam, a
///     hand turn at a corner keeps that corner (and the seams it sweeps
///     across). An old plan has no lanes and no auto pieces, so nothing in
///     it ever moves.
///   - Two turns cannot share a tile. A turn rail takes its radius of
///     straight on each arm; where the far end of an arm is another bend
///     that will want its own turn, each gets half (the canvas's own rule
///     for a dragged run of rail, _railStraightRun). Too short even for a
///     one-tile turn, the corner stays square.
///   - Whole tiles only: a turn rail meets its straights at a seam.
///
/// Pure - no canvas, no undo - so it is tested on its own
/// (RcpAutoRailsTests). It keeps the very objects of the auto pieces that
/// are still right, so a selection holding them, and the undo snapshots,
/// are not upset by a re-work that changes nothing.
/// </summary>
public static class RcpAutoRails
{
    /// <summary>What a re-work took off the plan, so a selection holding any of it can let go.</summary>
    public sealed record Outcome(IReadOnlyList<RcpWall> RemovedWalls, IReadOnlyList<RcpCurve> RemovedCurves, bool Changed);

    /// <summary>The largest radius a lane will use, in tiles.</summary>
    public const int MostRadius = 2;

    /// <summary>
    /// The lane each cell belongs to - its first tile decides, as it does in
    /// RcpTrackDesign.At. A blocked-out tile (Infield) is never lane: a
    /// pillar in the middle of a lane is something the rails go round.
    /// </summary>
    public static Dictionary<(int Col, int Row), int> LaneCells(RcpTrackDesign design)
    {
        var seen = new HashSet<(int, int)>();
        var lanes = new Dictionary<(int Col, int Row), int>();

        foreach (var tile in design.Tiles)
        {
            if (!seen.Add((tile.Col, tile.Row)))
            {
                continue;
            }

            if (tile.Lane > 0 && tile.Kind != RcpTileKind.Infield)
            {
                lanes[(tile.Col, tile.Row)] = tile.Lane;
            }
        }

        return lanes;
    }

    /// <summary>Whether the plan has anything for this to do: a lane, or auto pieces left over from one.</summary>
    public static bool HasWork(RcpTrackDesign design) =>
        design.Tiles.Any(t => t.Lane > 0)
        || design.Walls.Any(w => w.Auto)
        || design.Curves.Any(c => c.Auto);

    /// <summary>
    /// Works the plan's auto rails out again from its lanes, in place. Hand
    /// pieces are left exactly as they are.
    /// </summary>
    public static Outcome Apply(RcpTrackDesign design)
    {
        var none = new Outcome(Array.Empty<RcpWall>(), Array.Empty<RcpCurve>(), false);

        if (design is null || !HasWork(design))
        {
            return none;
        }

        var (walls, curves) = Want(design);

        // Keep the auto pieces that are already right - the same objects -
        // and take off the rest.
        var removedWalls = new List<RcpWall>();
        var removedCurves = new List<RcpCurve>();

        var wallsLeft = new Dictionary<(int, int, bool), bool>(walls);

        foreach (var wall in design.Walls.Where(w => w.Auto).ToList())
        {
            var key = (wall.Col, wall.Row, wall.Vertical);

            if (wallsLeft.TryGetValue(key, out var dbl) && dbl == wall.Double)
            {
                wallsLeft.Remove(key);
                continue;
            }

            design.Walls.Remove(wall);
            removedWalls.Add(wall);
        }

        var curvesLeft = new Dictionary<(int, int), RcpCurve>(curves);

        foreach (var curve in design.Curves.Where(c => c.Auto).ToList())
        {
            var key = (curve.Col, curve.Row);

            if (curvesLeft.TryGetValue(key, out var want)
                && want.Quadrant == curve.Quadrant
                && Math.Abs(want.Radius - curve.Radius) < 0.01f
                && want.Double == curve.Double)
            {
                curvesLeft.Remove(key);
                continue;
            }

            design.Curves.Remove(curve);
            removedCurves.Add(curve);
        }

        // Added in a fixed order, so the same lanes always give the same
        // plan - row by row, then column, horizontal before vertical.
        foreach (var (key, dbl) in wallsLeft.OrderBy(p => p.Key.Item2).ThenBy(p => p.Key.Item1).ThenBy(p => p.Key.Item3))
        {
            design.Walls.Add(new RcpWall { Col = key.Item1, Row = key.Item2, Vertical = key.Item3, Double = dbl, Auto = true });
        }

        foreach (var (_, curve) in curvesLeft.OrderBy(p => p.Key.Item2).ThenBy(p => p.Key.Item1))
        {
            design.Curves.Add(curve);
        }

        var changed = removedWalls.Count > 0 || removedCurves.Count > 0 || wallsLeft.Count > 0 || curvesLeft.Count > 0;

        return new Outcome(removedWalls, removedCurves, changed);
    }

    /// <summary>
    /// The auto rails the plan's lanes call for: seam to divider-or-not, and
    /// corner to turn. What hand pieces already cover is left out.
    /// </summary>
    internal static (Dictionary<(int, int, bool), bool> Walls, Dictionary<(int, int), RcpCurve> Curves) Want(RcpTrackDesign design)
    {
        var lanes = LaneCells(design);
        var radius = Math.Clamp(design.LaneTurnRadius, 0, MostRadius);

        int LaneAt(int col, int row) => lanes.TryGetValue((col, row), out var id) ? id : 0;

        // Seams a hand rail stands on, and seams a hand turn sweeps across.
        var handSeams = design.Walls
            .Where(w => !w.Auto)
            .Select(w => (w.Col, w.Row, w.Vertical))
            .ToHashSet();

        var handCorners = design.Curves
            .Where(c => !c.Auto)
            .Select(c => (c.Col, c.Row))
            .ToHashSet();

        var underHandTurns = design.Curves
            .Where(c => !c.Auto)
            .SelectMany(Swept)
            .ToHashSet();

        // --- The straights ---
        var walls = new Dictionary<(int, int, bool), bool>();

        foreach (var ((col, row), id) in lanes)
        {
            foreach (var edge in new[] { RcpEdge.North, RcpEdge.East, RcpEdge.South, RcpEdge.West })
            {
                var next = RcpPlan.Neighbour(col, row, edge);
                var other = LaneAt(next.X, next.Y);

                if (other == id)
                {
                    continue;
                }

                var seam = RcpPlan.EdgeLine(col, row, edge);

                // Two rails back to back where the other side is a lane of
                // its own. Seen from both of its lanes, so it is "or".
                walls[seam] = (walls.TryGetValue(seam, out var was) && was) || other != 0;
            }
        }

        // --- The turns ---
        var curves = new Dictionary<(int, int), RcpCurve>();

        if (radius > 0)
        {
            foreach (var ((col, row), id) in lanes.OrderBy(p => p.Key.Row).ThenBy(p => p.Key.Col))
            {
                // Each corner of the cell, and the quadrant of that corner
                // the cell is in.
                foreach (var (point, quadrant) in new[]
                         {
                             (new Point(col, row), RcpQuadrant.SouthEast),
                             (new Point(col + 1, row), RcpQuadrant.SouthWest),
                             (new Point(col + 1, row + 1), RcpQuadrant.NorthWest),
                             (new Point(col, row + 1), RcpQuadrant.NorthEast),
                         })
                {
                    if (curves.ContainsKey((point.X, point.Y)) || handCorners.Contains((point.X, point.Y)))
                    {
                        continue;
                    }

                    if (TurnAt(point, quadrant, id, radius, LaneAt) is not { } turn)
                    {
                        continue;
                    }

                    var swept = Swept(turn).ToList();

                    // A hand rail across where the turn would sweep is a
                    // corner somebody squared off by hand (the Corners
                    // tool does exactly that): it stays square.
                    if (swept.Any(handSeams.Contains))
                    {
                        continue;
                    }

                    // Nor across a seam a hand turn already sweeps: two arcs
                    // on the one stretch of rail is nothing anybody can build.
                    if (swept.Any(underHandTurns.Contains))
                    {
                        continue;
                    }

                    turn.Double = swept.Any(seam => walls.TryGetValue(seam, out var dbl) && dbl);

                    curves[(point.X, point.Y)] = turn;
                }
            }
        }

        // Nothing auto under a turn, or where a hand piece already is.
        foreach (var seam in curves.Values.SelectMany(Swept).Concat(underHandTurns).Concat(handSeams).ToList())
        {
            walls.Remove(seam);
        }

        return (walls, curves);
    }

    /// <summary>
    /// The turn at a corner of a lane, if it is the outside of a bend and
    /// there is the straight for one: null for anything else.
    /// </summary>
    private static RcpCurve? TurnAt(Point at, RcpQuadrant quadrant, int lane, int radius, Func<int, int, int> laneAt)
    {
        var (dx, dy) = Step(quadrant);

        // The cell in the corner, and the three round the point beside it.
        var cx = at.X + (dx > 0 ? 0 : -1);
        var cy = at.Y + (dy > 0 ? 0 : -1);

        bool Is(int col, int row) => laneAt(col, row) == lane;
        bool Any(int col, int row) => laneAt(col, row) != 0;

        // The outside of a bend: the lane's cell is the only one of the four
        // round the point that is any lane at all. Two of this lane
        // diagonally is a pinch, three is the inside of a bend - neither
        // gets a turn. And where ANOTHER lane is round the point, the rails
        // there are a divider both lanes race beside: an arc for this one
        // would sweep away the other's side and leave it a hole (review, 25
        // Oct 2026), so that corner stays square.
        if (Any(cx - dx, cy) || Any(cx, cy - dy) || Any(cx - dx, cy - dy))
        {
            return null;
        }

        var across = Room(radius, k => (cx + dx * (k - 1), cy), k => (cx + dx * (k - 1), cy - dy), Is, Any);
        var down = Room(radius, k => (cx, cy + dy * (k - 1)), k => (cx - dx, cy + dy * (k - 1)), Is, Any);

        var fits = Math.Min(radius, Math.Min(across, down));

        return fits < 1
            ? null
            : new RcpCurve { Col = at.X, Row = at.Y, Quadrant = quadrant, Radius = fits, Auto = true };
    }

    /// <summary>
    /// How big a turn one arm of a corner has room for, in whole tiles: the
    /// run of straight boundary from the corner, all of it when the run
    /// ends going into the lane, half of it when it ends at another outside
    /// bend that will want a turn of its own.
    /// </summary>
    private static int Room(
        int radius,
        Func<int, (int Col, int Row)> inside,
        Func<int, (int Col, int Row)> outside,
        Func<int, int, bool> isLane,
        Func<int, int, bool> anyLane)
    {
        bool Boundary(int k)
        {
            var (ic, ir) = inside(k);
            var (oc, or) = outside(k);

            // Beyond the seam must be no lane at all - a divider is not
            // straight a turn can sweep across.
            return isLane(ic, ir) && !isLane(oc, or) && !anyLane(oc, or);
        }

        // Twice the radius is as far as ever needs looking: a run that long
        // has room for the turn whatever is at its far end.
        var most = radius * 2;
        var run = 0;

        while (run < most && Boundary(run + 1))
        {
            run++;
        }

        if (run >= most)
        {
            return radius;
        }

        var (nextIn, nextInRow) = inside(run + 1);
        var (nextOut, nextOutRow) = outside(run + 1);

        var farBendIsOutside = !isLane(nextIn, nextInRow) && !isLane(nextOut, nextOutRow);

        return farBendIsOutside ? run / 2 : run;
    }

    /// <summary>
    /// The seams a turn rail sweeps across - one radius along each of its
    /// arms, rounded up to a whole seam - which are the straights it takes
    /// the place of.
    /// </summary>
    public static IEnumerable<(int Col, int Row, bool Vertical)> Swept(RcpCurve curve) => Swept(curve, wholeOnly: false);

    /// <summary>
    /// The seams a turn rail sweeps across - with <paramref name="wholeOnly"/>,
    /// only the ones it sweeps the WHOLE of: a half-tile hairpin covers half
    /// a seam, which a car can still get past at the other end.
    /// </summary>
    public static IEnumerable<(int Col, int Row, bool Vertical)> Swept(RcpCurve curve, bool wholeOnly)
    {
        var (a, b) = curve.Arms;
        var seams = wholeOnly
            ? (int)MathF.Floor(curve.Radius + 0.01f)
            : Math.Max(1, (int)MathF.Ceiling(curve.Radius - 0.01f));

        foreach (var arm in new[] { a, b })
        {
            for (var k = 0; k < seams; k++)
            {
                var from = new Point(curve.Col + arm.X * k, curve.Row + arm.Y * k);
                var to = new Point(from.X + arm.X, from.Y + arm.Y);

                yield return (Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), arm.X == 0);
            }
        }
    }

    /// <summary>Which way a quadrant lies from its point, as a step in each direction.</summary>
    internal static (int Dx, int Dy) Step(RcpQuadrant quadrant) => quadrant switch
    {
        RcpQuadrant.NorthEast => (1, -1),
        RcpQuadrant.SouthEast => (1, 1),
        RcpQuadrant.SouthWest => (-1, 1),
        _ => (-1, -1),
    };

    /// <summary>
    /// The cells a lane brush covers at a cell: a square the lane's width
    /// on a side - centred on the pointer for an odd width, with the pointer
    /// at its top-left of the middle four for an even one.
    /// </summary>
    public static IEnumerable<Point> Brush(Point at, int width)
    {
        var w = Math.Clamp(width, 1, 3);
        var back = (w - 1) / 2;

        for (var row = 0; row < w; row++)
        {
            for (var col = 0; col < w; col++)
            {
                yield return new Point(at.X - back + col, at.Y - back + row);
            }
        }
    }

    /// <summary>
    /// The cells of a ring: a band <paramref name="width"/> tiles wide just
    /// inside the box from one cell to the other, corners included. Null
    /// when the box is too small to leave an island in the middle - a ring
    /// with no island is a block of floor, not a track.
    /// </summary>
    public static List<Point>? Ring(Point from, Point to, int width)
    {
        var w = Math.Clamp(width, 1, 3);

        var left = Math.Min(from.X, to.X);
        var top = Math.Min(from.Y, to.Y);
        var right = Math.Max(from.X, to.X);
        var bottom = Math.Max(from.Y, to.Y);

        var cols = right - left + 1;
        var rows = bottom - top + 1;

        if (cols < w * 2 + 1 || rows < w * 2 + 1)
        {
            return null;
        }

        var cells = new List<Point>();

        for (var row = top; row <= bottom; row++)
        {
            for (var col = left; col <= right; col++)
            {
                var inBand = col - left < w || right - col < w || row - top < w || bottom - row < w;

                if (inBand)
                {
                    cells.Add(new Point(col, row));
                }
            }
        }

        return cells;
    }

    /// <summary>The number for a new lane: one more than any on the plan.</summary>
    public static int NewLane(RcpTrackDesign design) =>
        design.Tiles.Count == 0 ? 1 : Math.Max(0, design.Tiles.Max(t => t.Lane)) + 1;
}

/// <summary>
/// The Corners tool's rule (Track Designer overhaul, Phase 3, 25 Oct 2026):
/// what one click at a grid point makes of the corner there. Round it goes -
/// a one-tile turn, a two-tile turn, square, a one-tile turn again - and
/// whatever it sets is a hand piece, so a lane leaves the corner as it was
/// set (RcpAutoRails).
///
/// Whole tiles only, like the lanes' own turns: a turn rail meets its
/// straights at a seam. A turn of half a tile, or a tile and a half, is
/// still there, placed with the Turns tool; clicked here, it goes up to the
/// next whole tile.
///
/// Pure, so it is tested without a window (RcpCornersTests).
/// </summary>
public static class RcpCorners
{
    /// <summary>What the corner becomes: a turn of Radius tiles in Quadrant, or square when Radius is 0.</summary>
    public sealed record Step(RcpQuadrant Quadrant, int Radius, bool Double, string Said);

    /// <summary>What a square outside corner is on the floor: two rails on one tile, which is custom work.</summary>
    private const string Square =
        "Square: two rails meeting - at an outside corner that's two rails on one tile, a custom tile (two more bolt holes, a rail trimmed to fit).";

    /// <summary>
    /// What a click at <paramref name="at"/> would do - or null, with
    /// <paramref name="why"/> saying why nothing would. Asked by the hover
    /// too, to draw the corner it is about to become.
    /// </summary>
    public static Step? Next(RcpTrackDesign design, Point at, out string why)
    {
        why = string.Empty;

        if (design.CurveAt(at.X, at.Y) is { } curve)
        {
            // A one-tile turn, then square, then back: the one-tile turn is
            // the only turn a club has (Asher, 7 Nov 2026), so the two-tile
            // step is gone. A smaller turn goes up to one tile.
            var wanted = curve.Radius < 0.99f ? 1 : 0;

            if (wanted > 0 && !HasRoom(design, curve, at, curve.Quadrant, wanted))
            {
                return new Step(curve.Quadrant, 0, curve.Double, Square);
            }

            return new Step(curve.Quadrant, wanted, curve.Double, wanted == 1
                ? "An outer curve tile - the one-tile turn that comes in the box."
                : Square);
        }

        // A square corner: one rail out of the point each way, at right angles.
        var across = new[]
            {
                (Wall: design.WallAt(at.X - 1, at.Y, false), Arm: new Point(-1, 0)),
                (Wall: design.WallAt(at.X, at.Y, false), Arm: new Point(1, 0)),
            }
            .Where(p => p.Wall is not null)
            .ToList();

        var down = new[]
            {
                (Wall: design.WallAt(at.X, at.Y - 1, true), Arm: new Point(0, -1)),
                (Wall: design.WallAt(at.X, at.Y, true), Arm: new Point(0, 1)),
            }
            .Where(p => p.Wall is not null)
            .ToList();

        if (across.Count != 1 || down.Count != 1)
        {
            why = "That isn't a corner - click where two rails meet at a right angle, or on a turn rail.";
            return null;
        }

        var quadrant = QuadrantOf(across[0].Arm, down[0].Arm);

        if (!HasRoom(design, null, at, quadrant, 1))
        {
            why = "No room for a turn there - another turn already sweeps across one of those rails.";
            return null;
        }

        return new Step(quadrant, 1, across[0].Wall!.Double || down[0].Wall!.Double,
            "An outer curve tile - the one-tile turn that comes in the box.");
    }

    /// <summary>
    /// Makes the corner what <paramref name="step"/> says. Returns what it
    /// took off the plan, so a selection holding any of it can let go.
    /// </summary>
    public static (List<RcpWall> RemovedWalls, List<RcpCurve> RemovedCurves) Apply(RcpTrackDesign design, Point at, Step step)
    {
        var removedWalls = new List<RcpWall>();
        var removedCurves = new List<RcpCurve>();
        var existing = design.CurveAt(at.X, at.Y);

        if (step.Radius == 0)
        {
            if (existing is null)
            {
                return (removedWalls, removedCurves);
            }

            design.Curves.Remove(existing);
            removedCurves.Add(existing);

            // The straights the turn stood in place of, back - by hand, so a
            // lane does not round the corner off again.
            foreach (var (col, row, vertical) in RcpAutoRails.Swept(existing))
            {
                if (design.WallAt(col, row, vertical) is { } wall)
                {
                    wall.Auto = false;
                }
                else
                {
                    design.Walls.Add(new RcpWall { Col = col, Row = row, Vertical = vertical, Double = existing.Double });
                }
            }

            return (removedWalls, removedCurves);
        }

        if (existing is null)
        {
            existing = new RcpCurve { Col = at.X, Row = at.Y };
            design.Curves.Add(existing);
        }

        existing.Auto = false;
        existing.Quadrant = step.Quadrant;
        existing.Radius = step.Radius;
        existing.Double = step.Double;

        // Nothing straight left standing where the turn now sweeps.
        foreach (var (col, row, vertical) in RcpAutoRails.Swept(existing))
        {
            if (design.WallAt(col, row, vertical) is { } wall)
            {
                design.Walls.Remove(wall);
                removedWalls.Add(wall);
            }
        }

        return (removedWalls, removedCurves);
    }

    /// <summary>
    /// Whether a turn of <paramref name="radius"/> tiles fits: every seam it
    /// would sweep across has a rail on it (or is already swept by the turn
    /// that is there), and no other turn sweeps across any of them.
    /// </summary>
    private static bool HasRoom(RcpTrackDesign design, RcpCurve? existing, Point at, RcpQuadrant quadrant, int radius)
    {
        var probe = new RcpCurve { Col = at.X, Row = at.Y, Quadrant = quadrant, Radius = radius };

        var mine = existing is null
            ? new HashSet<(int, int, bool)>()
            : RcpAutoRails.Swept(existing).ToHashSet();

        var others = design.Curves
            .Where(c => !ReferenceEquals(c, existing))
            .SelectMany(RcpAutoRails.Swept)
            .ToHashSet();

        return RcpAutoRails.Swept(probe).All(seam =>
            !others.Contains(seam)
            && (mine.Contains(seam) || design.WallAt(seam.Col, seam.Row, seam.Vertical) is not null));
    }

    /// <summary>Which way a corner bulges, from the two directions its arms leave in.</summary>
    private static RcpQuadrant QuadrantOf(Point armA, Point armB)
    {
        var east = armA.X > 0 || armB.X > 0;
        var south = armA.Y > 0 || armB.Y > 0;

        return east
            ? south ? RcpQuadrant.SouthEast : RcpQuadrant.NorthEast
            : south ? RcpQuadrant.SouthWest : RcpQuadrant.NorthWest;
    }
}
