using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// What a tile of floor is.
///
/// An RCP track is a continuous field of identical interlocking tiles with
/// the circuit built ON TOP of it out of rails - the floor under the racing
/// line and the floor under the infield are the same tile. So this says what
/// is PRINTED on a tile, not what shape of track it is.
///
/// Stored as integers: new kinds go on the end, existing ones never move.
/// The three curve-shaped kinds are left in place for layouts drawn before
/// the barriers became rails in their own right.
/// </summary>
public enum RcpTileKind
{
    /// <summary>Plain floor. Nearly every tile is one of these.</summary>
    Straight,

    /// <summary>Legacy: a tile that drew its own start/finish chequer.</summary>
    StartFinish,

    /// <summary>Legacy shape, kept so old plans still open.</summary>
    OuterCurve,

    /// <summary>Legacy shape, kept so old plans still open.</summary>
    InnerCorner,

    /// <summary>Floor you can't use - a table leg, a pillar, the bit you didn't tile.</summary>
    Infield,

    /// <summary>Legacy.</summary>
    Bridge,

    /// <summary>Legacy.</summary>
    Banked,

    /// <summary>Legacy.</summary>
    Diagonal,

    /// <summary>Legacy.</summary>
    InnerCornerWalled,
}

/// <summary>Which edge of a tile something sits on.</summary>
public enum RcpEdge
{
    North,
    East,
    South,
    West,
}

/// <summary>Which way a corner turns, named by the cell the arc bulges into.</summary>
public enum RcpQuadrant
{
    NorthEast,
    SouthEast,
    SouthWest,
    NorthWest,
}

/// <summary>The flat things stuck on the floor rather than standing on it.</summary>
public enum RcpDecalKind
{
    /// <summary>The chequered band across the track at the line.</summary>
    FinishLine,

    /// <summary>One staggered grid box.</summary>
    GridBox,

    /// <summary>
    /// A rail post - the pink disc that sits on the corner where four
    /// tiles meet, bolted down with one mount on each of them. Its Turns
    /// choose WHICH corner of its tile, since the post itself is round.
    /// </summary>
    Post,

    /// <summary>A red-and-white apex kerb.</summary>
    Kerb,

    /// <summary>The rostrum, the table, the timing gear.</summary>
    Furniture,
}

/// <summary>One tile of floor, at a place on the grid.</summary>
public sealed class RcpTile
{
    public int Col { get; set; }
    public int Row { get; set; }
    public RcpTileKind Kind { get; set; }

    /// <summary>Quarter turns clockwise, 0-3.</summary>
    public int Turns { get; set; }

    public string Label { get; set; } = string.Empty;

    public RcpTile Clone() => new() { Col = Col, Row = Row, Kind = Kind, Turns = Turns, Label = Label };
}

/// <summary>
/// A straight rail, standing on the floor along one tile seam.
///
/// This is the piece the whole circuit is made of. It is indexed by the grid
/// LINE it sits on rather than by a tile, because a rail down the middle of
/// the floor has track on both sides and belongs to neither tile - which is
/// exactly what the old model could not express.
/// </summary>
public sealed class RcpWall
{
    /// <summary>The grid point the segment starts at.</summary>
    public int Col { get; set; }
    public int Row { get; set; }

    /// <summary>True: runs down from (Col,Row) to (Col,Row+1). False: runs right to (Col+1,Row).</summary>
    public bool Vertical { get; set; }

    /// <summary>
    /// Two rails back to back, which is what you build when there is track on
    /// both sides of it. A perimeter wall only needs one.
    /// </summary>
    public bool Double { get; set; }

    public RcpWall Clone() => new() { Col = Col, Row = Row, Vertical = Vertical, Double = Double };

    [JsonIgnore]
    public Point From => new(Col, Row);

    [JsonIgnore]
    public Point To => Vertical ? new Point(Col, Row + 1) : new Point(Col + 1, Row);

    public bool Touches(Point gridPoint) => From == gridPoint || To == gridPoint;
}

/// <summary>
/// A pink turn rail: an arc at a grid point, swinging a wall run through 90
/// degrees.
///
/// Held as a point, a quadrant and a radius, so the same piece describes a
/// gentle sweeper and a tight corner - which on a real track is a different
/// rail out of the box, but on a plan is one number.
/// </summary>
public sealed class RcpCurve
{
    public int Col { get; set; }
    public int Row { get; set; }

    /// <summary>The cell the arc bulges into.</summary>
    public RcpQuadrant Quadrant { get; set; }

    /// <summary>Radius in tiles. Half a tile is a hairpin; two tiles is a sweeper.</summary>
    public float Radius { get; set; } = 1f;

    public bool Double { get; set; }

    public RcpCurve Clone() => new() { Col = Col, Row = Row, Quadrant = Quadrant, Radius = Radius, Double = Double };

    [JsonIgnore]
    public Point At => new(Col, Row);

    /// <summary>The two grid directions this curve joins, as unit steps.</summary>
    public (Point A, Point B) Arms => Quadrant switch
    {
        RcpQuadrant.NorthEast => (new Point(0, -1), new Point(1, 0)),
        RcpQuadrant.SouthEast => (new Point(0, 1), new Point(1, 0)),
        RcpQuadrant.SouthWest => (new Point(0, 1), new Point(-1, 0)),
        _ => (new Point(0, -1), new Point(-1, 0)),
    };
}

/// <summary>Something flat on the floor: a grid box, a kerb, the finish line.</summary>
public sealed class RcpDecal
{
    public int Col { get; set; }
    public int Row { get; set; }
    public int Turns { get; set; }
    public RcpDecalKind Kind { get; set; }

    public RcpDecal Clone() => new() { Col = Col, Row = Row, Turns = Turns, Kind = Kind };
}

/// <summary>A point on the racing line the user has drawn, in tiles.</summary>
public sealed class RcpRoutePoint
{
    public float X { get; set; }
    public float Y { get; set; }

    [JsonIgnore]
    public PointF Point => new(X, Y);
}

/// <summary>A whole track plan: the floor, the rails on it, and the line round them.</summary>
public sealed class RcpTrackDesign
{
    public List<RcpTile> Tiles { get; set; } = new();
    public List<RcpWall> Walls { get; set; } = new();
    public List<RcpCurve> Curves { get; set; } = new();
    public List<RcpDecal> Decals { get; set; } = new();

    /// <summary>The racing line, drawn by the user.</summary>
    public List<RcpRoutePoint> Route { get; set; } = new();

    public bool RouteClosed { get; set; } = true;

    /// <summary>Tile size in millimetres. RCP sells 30cm and 50cm.</summary>
    public double TileSizeMm { get; set; } = 500;

    /// <summary>Which kit to check the tile count against, by name.</summary>
    public string KitName { get; set; } = string.Empty;

    // --- Tracing a photograph ------------------------------------------------
    // The fastest way to get a layout into the editor exactly is to put a
    // photo of it underneath and draw on top, so the picture and where it
    // sits are kept with the plan.

    public string BackdropPath { get; set; } = string.Empty;
    public float BackdropX { get; set; }
    public float BackdropY { get; set; }

    /// <summary>Tiles across that the backdrop image spans.</summary>
    public float BackdropWidth { get; set; } = 10f;

    public int BackdropOpacity { get; set; } = 45;

    /// <summary>Kept out of the drawing once the tracing is done, without losing the placement.</summary>
    public bool BackdropHidden { get; set; }

    public RcpTrackDesign Clone() => new()
    {
        Tiles = Tiles.Select(t => t.Clone()).ToList(),
        Walls = Walls.Select(w => w.Clone()).ToList(),
        Curves = Curves.Select(c => c.Clone()).ToList(),
        Decals = Decals.Select(d => d.Clone()).ToList(),
        Route = Route.Select(p => new RcpRoutePoint { X = p.X, Y = p.Y }).ToList(),
        RouteClosed = RouteClosed,
        TileSizeMm = TileSizeMm,
        KitName = KitName,
        BackdropPath = BackdropPath,
        BackdropX = BackdropX,
        BackdropY = BackdropY,
        BackdropWidth = BackdropWidth,
        BackdropOpacity = BackdropOpacity,
        BackdropHidden = BackdropHidden,
    };

    [JsonIgnore]
    public bool IsEmpty => Tiles.Count == 0 && Walls.Count == 0 && Curves.Count == 0;

    public RcpTile? At(int col, int row) =>
        Tiles.FirstOrDefault(t => t.Col == col && t.Row == row);

    public RcpWall? WallAt(int col, int row, bool vertical) =>
        Walls.FirstOrDefault(w => w.Col == col && w.Row == row && w.Vertical == vertical);

    public RcpCurve? CurveAt(int col, int row) =>
        Curves.FirstOrDefault(c => c.Col == col && c.Row == row);

    /// <summary>Floor you can drive on - everything except the blocked-out tiles.</summary>
    [JsonIgnore]
    public IEnumerable<RcpTile> Driveable => Tiles.Where(t => t.Kind != RcpTileKind.Infield);

    /// <summary>The plan's extent in tiles, taking in the floor, the rails and the line.</summary>
    public RectangleF BoundsInTiles()
    {
        var minCol = float.MaxValue;
        var minRow = float.MaxValue;
        var maxCol = float.MinValue;
        var maxRow = float.MinValue;

        void Take(float x, float y)
        {
            minCol = Math.Min(minCol, x);
            minRow = Math.Min(minRow, y);
            maxCol = Math.Max(maxCol, x);
            maxRow = Math.Max(maxRow, y);
        }

        foreach (var tile in Tiles)
        {
            Take(tile.Col, tile.Row);
            Take(tile.Col + 1, tile.Row + 1);
        }

        foreach (var wall in Walls)
        {
            Take(wall.From.X, wall.From.Y);
            Take(wall.To.X, wall.To.Y);
        }

        foreach (var curve in Curves)
        {
            Take(curve.Col - curve.Radius, curve.Row - curve.Radius);
            Take(curve.Col + curve.Radius, curve.Row + curve.Radius);
        }

        if (minCol > maxCol)
        {
            return new RectangleF(0, 0, 1, 1);
        }

        return RectangleF.FromLTRB(minCol, minRow, maxCol, maxRow);
    }
}

/// <summary>
/// What is in a box of RCP track, as the manufacturer lists it.
/// </summary>
public sealed record RcpKit(
    string Name,
    double TileSizeMm,
    int StartFinish,
    int Straights,
    int StraightRails,
    int OuterCurves,
    int OuterCurveRails,
    int InnerCorners,
    int InnerCornerPieces,
    string Footprint)
{
    public int TotalTiles => StartFinish + Straights + OuterCurves + InnerCorners;

    /// <summary>Every rail in the box, of any shape.</summary>
    public int TotalStraightRails => StraightRails;

    public int TotalTurnRails => OuterCurveRails + InnerCornerPieces;

    public static IReadOnlyList<RcpKit> All { get; } = new[]
    {
        new RcpKit("30cm Mini-96", 300, 2, 70, 72, 12, 12, 12, 12, "about 12ft x 9ft"),
        new RcpKit("50cm Wide-L / Oblong", 500, 2, 14, 16, 12, 12, 12, 12, "12ft 4in x 9ft 8in as an L"),
    };

    public static RcpKit? ByName(string name) =>
        All.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What a layout needs, counted up.</summary>
public sealed class RcpInventory
{
    public int Tiles { get; set; }
    public int Infield { get; set; }

    /// <summary>Straight rails, counting a back-to-back divider as the two rails it is.</summary>
    public int StraightRails { get; set; }

    public int TurnRails { get; set; }
    public int Dividers { get; set; }
    public int EndCaps { get; set; }

    public List<string> Shortfalls(RcpKit kit)
    {
        var lines = new List<string>();

        void Check(string what, int needed, int have)
        {
            if (needed > have)
            {
                lines.Add($"{needed - have} more {what} (need {needed}, kit has {have})");
            }
        }

        Check("tiles", Tiles, kit.TotalTiles);
        Check("straight rails", StraightRails, kit.TotalStraightRails);
        Check("turn rails", TurnRails, kit.TotalTurnRails);

        return lines;
    }
}

/// <summary>
/// The rules that turn a floor and a pile of rails into a track.
/// </summary>
public static class RcpPlan
{
    public static Point Neighbour(int col, int row, RcpEdge edge) => edge switch
    {
        RcpEdge.North => new Point(col, row - 1),
        RcpEdge.East => new Point(col + 1, row),
        RcpEdge.South => new Point(col, row + 1),
        _ => new Point(col - 1, row),
    };

    /// <summary>The grid edge a tile edge corresponds to: which line, and which way it runs.</summary>
    public static (int Col, int Row, bool Vertical) EdgeLine(int col, int row, RcpEdge edge) => edge switch
    {
        RcpEdge.North => (col, row, false),
        RcpEdge.South => (col, row + 1, false),
        RcpEdge.West => (col, row, true),
        _ => (col + 1, row, true),
    };

    /// <summary>Whether a rail runs along this tile edge.</summary>
    public static bool HasWall(RcpTrackDesign design, int col, int row, RcpEdge edge)
    {
        var (c, r, vertical) = EdgeLine(col, row, edge);
        return design.WallAt(c, r, vertical) is not null;
    }

    /// <summary>
    /// Rails around the outside of the floor, in one go.
    ///
    /// Walling the perimeter by hand is forty clicks of the least
    /// interesting part of a layout, so this does it: every edge of the
    /// tiled floor that has nothing beyond it gets a rail, and anything
    /// already placed is left alone.
    /// </summary>
    public static int WallThePerimeter(RcpTrackDesign design)
    {
        var added = 0;

        foreach (var tile in design.Tiles.Where(t => t.Kind != RcpTileKind.Infield).ToList())
        {
            foreach (var edge in Enum.GetValues<RcpEdge>())
            {
                var next = Neighbour(tile.Col, tile.Row, edge);
                var neighbour = design.At(next.X, next.Y);

                if (neighbour is not null && neighbour.Kind != RcpTileKind.Infield)
                {
                    continue;
                }

                var (c, r, vertical) = EdgeLine(tile.Col, tile.Row, edge);

                if (design.WallAt(c, r, vertical) is not null)
                {
                    continue;
                }

                design.Walls.Add(new RcpWall { Col = c, Row = r, Vertical = vertical });
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// The loose ends: grid points where a rail run stops with nothing
    /// carrying on, which is where an end cap goes on the real thing.
    /// </summary>
    public static List<(Point At, bool Vertical, bool Double)> OpenEnds(RcpTrackDesign design)
    {
        var ends = new List<(Point, bool, bool)>();

        foreach (var wall in design.Walls)
        {
            foreach (var end in new[] { wall.From, wall.To })
            {
                var others = design.Walls.Count(w => !ReferenceEquals(w, wall) && w.Touches(end));
                var curves = design.Curves.Count(c => c.At == end);

                if (others == 0 && curves == 0)
                {
                    ends.Add((end, wall.Vertical, wall.Double));
                }
            }
        }

        return ends;
    }

    public static RcpInventory Count(RcpTrackDesign design)
    {
        var inventory = new RcpInventory
        {
            Tiles = design.Tiles.Count(t => t.Kind != RcpTileKind.Infield),
            Infield = design.Tiles.Count(t => t.Kind == RcpTileKind.Infield),
            Dividers = design.Walls.Count(w => w.Double),
            TurnRails = design.Curves.Sum(c => c.Double ? 2 : 1),
        };

        inventory.StraightRails = design.Walls.Sum(w => w.Double ? 2 : 1);
        inventory.EndCaps = OpenEnds(design).Count;

        return inventory;
    }

    public static float RouteLengthInTiles(RcpTrackDesign design)
    {
        if (design.Route.Count < 2)
        {
            return 0f;
        }

        var total = 0f;

        for (var i = 1; i < design.Route.Count; i++)
        {
            total += Distance(design.Route[i - 1].Point, design.Route[i].Point);
        }

        if (design.RouteClosed)
        {
            total += Distance(design.Route[^1].Point, design.Route[0].Point);
        }

        return total;
    }

    public static float Distance(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Follows the corridor between the rails and lays the racing line down
    /// the middle of it.
    ///
    /// Works on the CELLS, stepping only where no rail stands between one
    /// cell and the next - which is exactly what a car can do. Where the
    /// corridor opens out and there is more than one way to go, it stops and
    /// says so rather than picking one.
    /// </summary>
    public static List<PointF>? AutoRoute(RcpTrackDesign design, out string problem)
    {
        problem = string.Empty;

        var floor = design.Driveable.ToList();

        if (floor.Count < 3)
        {
            problem = "Lay some floor tiles first.";
            return null;
        }

        var cells = floor.ToDictionary(t => (t.Col, t.Row));

        List<(int Col, int Row)> Open(int col, int row) =>
            Enum.GetValues<RcpEdge>()
                .Where(e => !HasWall(design, col, row, e))
                .Select(e => Neighbour(col, row, e))
                .Select(p => (p.X, p.Y))
                .Where(cells.ContainsKey)
                .ToList();

        // Start beside the finish line when there is one, so the lap begins
        // where the lap begins.
        var startDecal = design.Decals.FirstOrDefault(d => d.Kind == RcpDecalKind.FinishLine);

        var start = startDecal is not null && cells.ContainsKey((startDecal.Col, startDecal.Row))
            ? (startDecal.Col, startDecal.Row)
            : (floor[0].Col, floor[0].Row);

        // Nullable, so "none" can't be mistaken for the tile at column 0,
        // row 0 - which is where the first tile of a plan usually goes, and
        // which the old "!= default" test let through however open it was.
        (int, int)? open3 = cells.Keys
            .Where(k => Open(k.Item1, k.Item2).Count > 2)
            .Select(k => ((int, int)?)k)
            .FirstOrDefault();

        if (open3 is { } wide)
        {
            problem = $"The floor at column {wide.Item1 + 1}, row {wide.Item2 + 1} is open on three sides, "
                      + "so there's more than one way round. Wall it in, or draw the line by hand.";
            return null;
        }

        var route = new List<PointF>();
        var visited = new HashSet<(int, int)>();
        var current = start;
        var previous = (Col: int.MinValue, Row: int.MinValue);

        while (true)
        {
            visited.Add(current);
            route.Add(new PointF(current.Item1 + 0.5f, current.Item2 + 0.5f));

            var options = Open(current.Item1, current.Item2)
                .Where(n => n != (previous.Col, previous.Row))
                .ToList();

            if (options.Count == 0)
            {
                problem = "The corridor stops rather than joining back up.";
                return route.Count > 2 ? route : null;
            }

            var next = options[0];

            if (next == start)
            {
                return route;
            }

            if (visited.Contains(next))
            {
                problem = "The corridor doubles back before closing.";
                return route;
            }

            previous = current;
            current = next;
        }
    }
}
