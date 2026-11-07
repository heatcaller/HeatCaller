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

    /// <summary>
    /// Where a split loop lies across the track - Split 1, 2 or 3, partway
    /// round from the finish line (the start/finish loop). One of each on a
    /// plan. With the racing line drawn through them, the distance along it
    /// between loops is each sector's length (RcpPlan.MeasureSectors), which
    /// gives sector speeds. Appended at the END - saved as numbers.
    /// </summary>
    SplitLoop1,
    SplitLoop2,
    SplitLoop3,
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

    /// <summary>
    /// Which lane this tile is part of, when it was laid by the Lane tool -
    /// 0 when it is plain floor (every tile in a plan drawn before lanes).
    ///
    /// A number rather than a yes or no, because two lanes can run side by
    /// side: the seam between tiles of the SAME lane is open track, and the
    /// seam between two DIFFERENT lanes is a divider. The rails round a lane
    /// are worked out from this by RcpAutoRails (Track Designer overhaul,
    /// Phase 3, 25 Oct 2026).
    /// </summary>
    public int Lane { get; set; }

    /// <summary>
    /// Which of RCP's pieces this tile is - a plain tile, or a tile with its
    /// rail already bolted on (see RcpPiece). On a real track a rail never
    /// stands on its own: it is bolted to one edge of its tile and stays
    /// there, and the two go down as one piece (Asher, 7 Nov 2026 - plans\
    /// track-designer-railed-tiles-plan.md). Plain in every plan saved before
    /// this existed. For now it is worked out from the rails on the seams
    /// (RcpPieces.Adopt); the editor lays pieces directly from Phase 2.
    /// </summary>
    public RcpPiece Piece { get; set; }

    /// <summary>The edge a rail, finish or arched rail tile has its rail on - its bolt-hole edge.</summary>
    public RcpEdge Facing { get; set; }

    /// <summary>
    /// The corner of the tile its rail is in: where an outer curve tile's
    /// pink arc runs across (the bare room beyond it is that corner), or
    /// where an inner corner tile's small red piece sits. An outer curve
    /// tile's turn rail is anchored at this corner and bulges the opposite
    /// way (RcpCurve.Quadrant is the opposite quadrant).
    /// </summary>
    public RcpQuadrant Corner { get; set; }

    public RcpTile Clone() => new()
    {
        Col = Col,
        Row = Row,
        Kind = Kind,
        Turns = Turns,
        Label = Label,
        Lane = Lane,
        Piece = Piece,
        Facing = Facing,
        Corner = Corner,
    };
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

    /// <summary>
    /// Put down by a lane rather than by hand (see RcpTile.Lane), so it is
    /// worked out again whenever the lane changes - RcpAutoRails. False for
    /// a rail drawn by hand, and for every rail in a plan drawn before lanes,
    /// so nothing in an old plan ever moves.
    /// </summary>
    public bool Auto { get; set; }

    public RcpWall Clone() => new() { Col = Col, Row = Row, Vertical = Vertical, Double = Double, Auto = Auto };

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

    /// <summary>Put down by a lane rather than by hand - see RcpWall.Auto.</summary>
    public bool Auto { get; set; }

    public RcpCurve Clone() => new() { Col = Col, Row = Row, Quadrant = Quadrant, Radius = Radius, Double = Double, Auto = Auto };

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

    /// <summary>
    /// The racing line came from Trace route and has not been touched by
    /// hand since - so it is traced again by itself whenever the corridor
    /// changes. Drawing, moving or taking off a point of it by hand turns
    /// this off. False in every plan saved before it existed.
    /// </summary>
    public bool RouteAuto { get; set; }

    /// <summary>
    /// The radius, in whole tiles, of the turn rails a lane puts round its
    /// bends: 1 or 2, or 0 for square corners. A turn rail has to meet its
    /// straights at a seam, so the lanes only use whole tiles; a half-tile
    /// hairpin is still there, placed by hand with the Turns tool.
    /// </summary>
    public int LaneTurnRadius { get; set; } = 1;

    /// <summary>
    /// When the racing line was last saved changed (UTC) - so the lap raced
    /// on the layout before then is known to be for a different line
    /// (RcpLapModel.Raced). Null on a plan whose line hasn't been changed
    /// since this was recorded (25 Oct 2026), which counts every race.
    /// </summary>
    public DateTime? RouteChangedUtc { get; set; }

    /// <summary>Tile size in millimetres. RCP sells 30cm and 50cm.</summary>
    public double TileSizeMm { get; set; } = 500;

    /// <summary>Which kit to check the tile count against, by name.</summary>
    public string KitName { get; set; } = string.Empty;

    // --- The room it has to go in ---------------------------------------------
    // A plan that is a beautiful circuit and four inches too wide for the
    // hall is a plan nobody can build. The grid itself is unbounded on
    // purpose - you draw where you like - so the room is kept beside it as
    // a measurement rather than as a limit, and the editor says whether the
    // one fits in the other.

    /// <summary>How wide the room is, in millimetres. Zero when nobody has said.</summary>
    public double RoomWidthMm { get; set; }

    /// <summary>How deep the room is, in millimetres. Zero when nobody has said.</summary>
    public double RoomDepthMm { get; set; }

    /// <summary>Whether the room was given in feet and inches rather than metres - how to show it back.</summary>
    public bool RoomInFeet { get; set; } = true;

    [JsonIgnore]
    public bool HasRoom => RoomWidthMm > 0 && RoomDepthMm > 0;

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
        RouteAuto = RouteAuto,
        LaneTurnRadius = LaneTurnRadius,
        RouteChangedUtc = RouteChangedUtc,
        TileSizeMm = TileSizeMm,
        KitName = KitName,
        RoomWidthMm = RoomWidthMm,
        RoomDepthMm = RoomDepthMm,
        RoomInFeet = RoomInFeet,
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

    /// <summary>
    /// The plan's extent in tiles, taking in the floor, the rails, the
    /// markings and the line.
    ///
    /// The markings and the line used to be left out, although both are
    /// drawn and neither has to sit on the floor - so a racing line or a
    /// marking past the edge of the floor was cut off in the PNG export, the
    /// track's thumbnail and Fit. includeRoute is false for a picture that
    /// leaves the line out, so the plan isn't framed round something that
    /// isn't drawn.
    /// </summary>
    public RectangleF BoundsInTiles(bool includeRoute = true)
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

        // A turn rail's arc lies in the one quadrant it sweeps through: from
        // its point, out one radius each way into that quadrant. It used to
        // be taken a radius out on EVERY side (25 Oct 2026, overhaul Phase
        // 3), so a plan with two-tile turns at its corners - which every
        // lane with two-tile turns has - read two tiles too big all round
        // in the size chip, and Fit and the picture framed the empty space.
        foreach (var curve in Curves)
        {
            var (dx, dy) = RcpAutoRails.Step(curve.Quadrant);

            Take(curve.Col, curve.Row);
            Take(curve.Col + dx * curve.Radius, curve.Row + dy * curve.Radius);
        }

        foreach (var decal in Decals)
        {
            Take(decal.Col, decal.Row);
            Take(decal.Col + 1, decal.Row + 1);
        }

        if (includeRoute)
        {
            foreach (var point in Route)
            {
                Take(point.X, point.Y);
            }
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
    string Footprint,
    int TilesInSet = 0)
{
    /// <summary>
    /// Whether RCP publishes what is actually in this box, piece by piece.
    ///
    /// They do for some sets and not for others: the two 96-tile tracks,
    /// the Wide L and the 30cm Oval Wide come with a full table, while the
    /// Ovals and the Supersized say only how many tiles and how big it
    /// comes out. One of them publishes a breakdown that does not add up
    /// to its own advertised total and says it is based on "similar
    /// configurations".
    ///
    /// So the counts here are only ever what RCP prints. Filling the gaps
    /// with plausible-looking numbers would make a shortfall list that
    /// reads exactly as confidently as a true one and sends somebody to
    /// buy the wrong thing.
    /// </summary>
    public bool BreakdownPublished => Straights > 0 || OuterCurves > 0;

    public int TotalTiles => BreakdownPublished
        ? StartFinish + Straights + OuterCurves + InnerCorners
        : TilesInSet;

    /// <summary>Every rail in the box, of any shape.</summary>
    public int TotalStraightRails => StraightRails;

    public int TotalTurnRails => OuterCurveRails + InnerCornerPieces;

    /// <summary>
    /// The sets RCP sells, from rcptracks.com (8 Oct 2026).
    ///
    /// The first two keep the names they have always had, because a saved
    /// plan remembers the kit it was checked against by name and renaming
    /// them would quietly un-check every plan that used one.
    /// </summary>
    public static IReadOnlyList<RcpKit> All { get; } = new[]
    {
        // --- 30 cm (12in) tiles ---
        new RcpKit("30cm Mini-96", 300, 2, 70, 72, 12, 12, 12, 12, "about 12ft x 9ft"),
        new RcpKit("30cm Oval Wide", 300, 2, 28, 30, 12, 12, 12, 12, "9ft x 6ft"),
        new RcpKit("30cm Double Oval Wide", 300, 0, 0, 0, 0, 0, 0, 0, "12ft x 9ft", TilesInSet: 108),

        // --- 50 cm (20in) tiles ---
        new RcpKit("50cm Wide-L / Oblong", 500, 2, 14, 16, 12, 12, 12, 12, "12ft 4in x 9ft 8in as an L"),
        new RcpKit("50cm Oval", 500, 0, 0, 0, 0, 0, 0, 0, "6ft 5in x 11ft 3in", TilesInSet: 28),
        new RcpKit("50cm Short Oval", 500, 0, 0, 0, 0, 0, 0, 0, "9ft 6in x 6ft 4in", TilesInSet: 14),
        new RcpKit("50cm Supersized Oval", 500, 0, 0, 0, 0, 0, 0, 0, "9ft 9in x 19ft 2in", TilesInSet: 52),
        new RcpKit("50cm Large-96", 500, 2, 70, 72, 12, 12, 12, 12, "20ft x 13ft"),
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

    /// <summary>
    /// Tiles that have to be RCP's outer curve tile: the one under each
    /// turn rail round the outside of a bend (RcpPlan.CornerTiles, Track
    /// Designer overhaul Phase 4, 25 Oct 2026). Included in Tiles.
    /// </summary>
    public int OuterCurveTiles { get; set; }

    /// <summary>
    /// Tiles that have to be RCP's inner corner tile, with an inner corner
    /// piece on it: one at the inside of each bend. Included in Tiles.
    /// </summary>
    public int InnerCornerTiles { get; set; }

    /// <summary>
    /// Turn rails two tiles round. RCP's boxes don't say what one of these
    /// takes, so each is counted as one outer curve tile and one turn rail,
    /// and the readout says so.
    /// </summary>
    public int TwoTileTurns { get; set; }

    /// <summary>
    /// The turn rails round the OUTSIDE of bends - RCP's outer curve rails -
    /// counting a back-to-back pair as two. The rest of TurnRails are turns
    /// put round an inside corner, which stand in for an inner corner piece.
    /// </summary>
    public int OuterTurnRails { get; set; }

    /// <summary>
    /// Inner corner pieces: one at the inside of every bend, on its inner
    /// corner tile - whether that corner is drawn square or with a turn.
    /// </summary>
    public int InnerCornerPieces => InnerCornerTiles;

    /// <summary>The plain floor: every tile that is neither corner tile.</summary>
    public int PlainTiles => Math.Max(0, Tiles - OuterCurveTiles - InnerCornerTiles);

    /// <summary>
    /// What this layout needs more of than the kit holds - and nothing
    /// else. Empty means the box genuinely builds it.
    ///
    /// Separate from Shortfalls because Shortfalls also says when it
    /// CANNOT tell, which is worth reading on screen but is not a
    /// shortfall: a caller asking "does this fit?" wants the difference.
    /// </summary>
    public List<string> Missing(RcpKit kit)
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

        // Only where RCP says what is in the box - see
        // RcpKit.BreakdownPublished. "0 straight rails, need 46" would be
        // a shortfall invented out of a number nobody published.
        if (kit.BreakdownPublished)
        {
            // The corner tiles (Phase 4): a box with 12 outer curve tiles
            // can't build 14 outside turns, however many tiles it has in
            // all. Not the other way round - a spare corner tile is still a
            // tile of floor, so plain floor is only checked as tiles.
            Check("outer curve tiles", OuterCurveTiles, kit.OuterCurves);
            Check("inner corner tiles", InnerCornerTiles, kit.InnerCorners);
            Check("straight rails", StraightRails, kit.TotalStraightRails);

            // Outer turns against the box's outer curve rails, and the
            // inside of each bend against its inner corner pieces - the
            // same split the Stock tab makes (review, 25 Oct 2026), so the
            // two never disagree.
            Check("turn rails", OuterTurnRails, kit.OuterCurveRails);
            Check("inner corner pieces", InnerCornerPieces, kit.InnerCornerPieces);
        }

        return lines;
    }

    /// <summary>Whether this kit's rails go unchecked, because RCP never listed them.</summary>
    public bool RailsUnlisted(RcpKit kit) =>
        !kit.BreakdownPublished && (StraightRails > 0 || TurnRails > 0);

    /// <summary>
    /// Everything worth saying about this layout against this kit: what
    /// it is short of, and - where RCP never published a parts list - that
    /// the rails could not be checked at all.
    /// </summary>
    public List<string> Shortfalls(RcpKit kit)
    {
        var lines = Missing(kit);

        if (RailsUnlisted(kit))
        {
            lines.Add($"RCP doesn't publish how many rails come with the {kit.Name}, so only the tiles are checked here");
        }

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

        // Not under a turn rail (25 Oct 2026): the turn already stands in
        // place of the straight there, and a hand rail under a lane's turn
        // squared that corner off for good (RcpAutoRails).
        var blocked = BlockedSeams(design);
        blocked.UnionWith(design.Curves.SelectMany(c => RcpAutoRails.Swept(c)));

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

                if (design.WallAt(c, r, vertical) is not null || blocked.Contains((c, r, vertical)))
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
                var curves = design.Curves.Count(c => c.At == end || ArcEndsAt(c, end));

                if (others == 0 && curves == 0)
                {
                    ends.Add((end, wall.Vertical, wall.Double));
                }
            }
        }

        return ends;
    }

    /// <summary>
    /// Whether one end of a turn rail's arc is at <paramref name="point"/>.
    ///
    /// A turn is anchored at the corner it cuts across, and its arc meets
    /// its straights one radius out along each arm (see
    /// RcpTrackPainter.DrawCurve) - which is where every stock layout and
    /// a dragged rail loop put them. Only the anchor used to count as
    /// joined, so both straights at every turn read as open: phantom end
    /// caps in the inventory, and drawn pink at each end of every turn.
    /// The anchor is still counted too, for plans drawn that way.
    /// </summary>
    private static bool ArcEndsAt(RcpCurve curve, Point point)
    {
        var (a, b) = curve.Arms;

        bool Meets(Point arm) =>
            Math.Abs(curve.Col + arm.X * curve.Radius - point.X) < 0.01f
            && Math.Abs(curve.Row + arm.Y * curve.Radius - point.Y) < 0.01f;

        return Meets(a) || Meets(b);
    }

    /// <summary>
    /// Rails with no tile under either side of them.
    ///
    /// A rail is not a fence - it does not stand on the floor of its own
    /// accord. It is BOLTED THROUGH the tile it runs along, with grey
    /// plastic pins (see RcpTrackPainter, which draws their heads). So a
    /// rail drawn out past the edge of the floor is not a thing anybody
    /// can build, however good the plan looks.
    ///
    /// Counted rather than prevented, which is this editor's way: it
    /// reports what is wrong with a plan - dead ends, a line running
    /// backwards - instead of refusing the stroke. Rails are often drawn
    /// before the floor they will sit on, and a tool that fought that
    /// would be worse than one that mentions it at the end.
    ///
    /// Curves are left out. An arc is anchored at a grid point and sweeps
    /// through a quadrant, so "which tile is under it" has no one answer
    /// worth reporting on.
    /// </summary>
    public static int RailsWithNoFloor(RcpTrackDesign design)
    {
        var floor = new HashSet<(int, int)>();

        foreach (var tile in design.Tiles)
        {
            floor.Add((tile.Col, tile.Row));
        }

        var orphans = 0;

        foreach (var wall in design.Walls)
        {
            // The two cells the seam lies between: one of them has to be
            // floor for there to be anything to bolt to.
            var (a, b) = wall.Vertical
                ? ((wall.Col - 1, wall.Row), (wall.Col, wall.Row))
                : ((wall.Col, wall.Row - 1), (wall.Col, wall.Row));

            if (!floor.Contains(a) && !floor.Contains(b))
            {
                orphans++;
            }
        }

        return orphans;
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

        var corners = CornerTiles(design);
        inventory.OuterCurveTiles = corners.Outer.Count;
        inventory.InnerCornerTiles = corners.Inner.Count;
        inventory.TwoTileTurns = design.Curves.Count(c => c.Radius > 1.01f);
        inventory.OuterTurnRails = design.Curves
            .Where(c => corners.Outer.Contains(QuadrantCell(c)))
            .Sum(c => c.Double ? 2 : 1);

        return inventory;
    }

    /// <summary>
    /// Which tiles of the floor have to be RCP's corner tiles (Track
    /// Designer overhaul, Phase 4, 25 Oct 2026). Worked out from the floor
    /// and the rails - nothing is saved for it.
    ///
    /// RCP's turn rails stand on tiles of their own, which is why every box
    /// lists them apart from the straight tiles, and in equal numbers (12
    /// and 12): a bend in a lane - and a real lane is always at least two
    /// tiles wide (Asher, 25 Oct 2026) - takes
    ///   - an OUTER CURVE TILE in the outside corner, under the turn rail;
    ///   - an INNER CORNER TILE at the inside of the bend: the track's own
    ///     tile diagonally inside the outer one, with the inner corner piece
    ///     on it round the island's corner.
    ///
    /// So: the tile in the quadrant a turn rail bulges into is an outer
    /// curve tile, when it is track; and wherever three of the four tiles
    /// round a grid point are one stretch of track and the fourth is not -
    /// the inside of a bend - the tile across from the fourth is an inner
    /// corner tile. A turn rail put round an inside corner (the Corners
    /// tool can) bulges off the track, so it marks that inside corner, not
    /// an outer one.
    ///
    /// "The track" is the lanes, each on its own, when the plan has lanes;
    /// otherwise the floor a car can reach from the start, as Trace route
    /// finds it. A tile is one kind or the other, outer first.
    /// </summary>
    public static (HashSet<(int Col, int Row)> Outer, HashSet<(int Col, int Row)> Inner) CornerTiles(RcpTrackDesign design)
    {
        var outer = new HashSet<(int, int)>();
        var inner = new HashSet<(int, int)>();

        var track = TrackRegions(design);

        if (track.Count == 0)
        {
            return (outer, inner);
        }

        int RegionAt(int col, int row) => track.TryGetValue((col, row), out var id) ? id : 0;

        foreach (var curve in design.Curves)
        {
            var cell = QuadrantCell(curve);

            if (RegionAt(cell.Item1, cell.Item2) != 0)
            {
                outer.Add(cell);
            }
        }

        // Every grid point at a corner of a track tile, once.
        var points = track.Keys
            .SelectMany(c => new[] { (c.Col, c.Row), (c.Col + 1, c.Row), (c.Col, c.Row + 1), (c.Col + 1, c.Row + 1) })
            .ToHashSet();

        foreach (var (x, y) in points)
        {
            var round = new[] { (x - 1, y - 1), (x, y - 1), (x, y), (x - 1, y) };
            var ids = round.Select(c => RegionAt(c.Item1, c.Item2)).ToArray();

            for (var missing = 0; missing < 4; missing++)
            {
                var across = (missing + 2) % 4;
                var id = ids[across];

                if (id == 0 || ids[missing] == id)
                {
                    continue;
                }

                // The other three all this stretch of track, the fourth not.
                if (ids[(missing + 1) % 4] == id && ids[(missing + 3) % 4] == id)
                {
                    var cell = round[across];

                    if (!outer.Contains(cell))
                    {
                        inner.Add(cell);
                    }
                }
            }
        }

        return (outer, inner);
    }

    /// <summary>The cell a turn rail's arc bulges into: the corner cell of its quadrant.</summary>
    internal static (int Col, int Row) QuadrantCell(RcpCurve curve)
    {
        var (dx, dy) = RcpAutoRails.Step(curve.Quadrant);
        return (curve.Col + (dx > 0 ? 0 : -1), curve.Row + (dy > 0 ? 0 : -1));
    }

    /// <summary>
    /// The track's tiles, each with the stretch of track it belongs to: the
    /// lanes, by lane number, when the plan has any; otherwise the floor a
    /// car can reach from where Trace route starts, as one stretch.
    /// </summary>
    internal static Dictionary<(int Col, int Row), int> TrackRegions(RcpTrackDesign design)
    {
        var lanes = RcpAutoRails.LaneCells(design);

        if (lanes.Count > 0)
        {
            return lanes;
        }

        var cells = new Dictionary<(int Col, int Row), RcpTile>();

        foreach (var tile in design.Driveable)
        {
            cells.TryAdd((tile.Col, tile.Row), tile);
        }

        var track = new Dictionary<(int Col, int Row), int>();

        if (cells.Count == 0)
        {
            return track;
        }

        // Somewhere surely ON the track (review, 25 Oct 2026): the finish
        // line, else the racing line's first point, else the tile a turn
        // rail bulges into. The first tile of the floor came last: laid
        // whole and railed inside its edge, that is floor outside the
        // track, and measured from there every outside corner read as an
        // inside one.
        var candidates = new List<(int Col, int Row)>();

        if (design.Decals.FirstOrDefault(d => d.Kind == RcpDecalKind.FinishLine) is { } finish)
        {
            candidates.Add((finish.Col, finish.Row));
        }

        if (design.Route.Count > 0)
        {
            candidates.Add(((int)MathF.Floor(design.Route[0].X), (int)MathF.Floor(design.Route[0].Y)));
        }

        candidates.AddRange(design.Curves.Select(QuadrantCell));

        var first = design.Driveable.First();
        candidates.Add((first.Col, first.Row));

        var start = candidates.First(cells.ContainsKey);

        var blocked = BlockedSeams(design);
        var frontier = new Queue<(int Col, int Row)>();

        track[start] = 1;
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            var (col, row) = frontier.Dequeue();

            foreach (var edge in new[] { RcpEdge.North, RcpEdge.East, RcpEdge.South, RcpEdge.West })
            {
                if (blocked.Contains(EdgeLine(col, row, edge)))
                {
                    continue;
                }

                var next = Neighbour(col, row, edge);

                if (cells.ContainsKey((next.X, next.Y)) && track.TryAdd((next.X, next.Y), 1))
                {
                    frontier.Enqueue((next.X, next.Y));
                }
            }
        }

        return track;
    }

    /// <summary>
    /// How far a marking may sit from the racing line, in tiles, and still
    /// be measured as being on it.
    /// </summary>
    public const float OnRouteTiles = 1.0f;

    /// <summary>Which split loop a marking is (1-3), or 0 for anything else.</summary>
    public static int SplitNumberOf(RcpDecalKind kind) => kind switch
    {
        RcpDecalKind.SplitLoop1 => 1,
        RcpDecalKind.SplitLoop2 => 2,
        RcpDecalKind.SplitLoop3 => 3,
        _ => 0,
    };

    /// <summary>
    /// The plan's sectors, measured along the racing line: from the finish
    /// line to Split 1, Split 1 to Split 2, and so on, and the last split
    /// back round to the finish line - in tiles, in the order the line is
    /// driven. Null with no split loops on the plan at all; otherwise either
    /// the sectors or what stops them being measured.
    /// </summary>
    public static (float[]? Sectors, string? Problem)? MeasureSectors(RcpTrackDesign design)
    {
        var splits = design.Decals
            .Where(d => d is not null && SplitNumberOf(d.Kind) > 0)
            .GroupBy(d => SplitNumberOf(d.Kind))
            .OrderBy(g => g.Key)
            .Select(g => g.First())
            .ToList();

        if (splits.Count == 0)
        {
            return null;
        }

        // Split 1, then 2, then 3 - none skipped.
        for (var i = 0; i < splits.Count; i++)
        {
            if (SplitNumberOf(splits[i].Kind) != i + 1)
            {
                return (null, $"There's a Split {SplitNumberOf(splits[i].Kind)} but no Split {i + 1}. Number the loops in the order cars reach them.");
            }
        }

        // 5 Nov 2026, Bug hunt 11 review: a finish line across a wide lane is
        // one decal per tile; take them top-left first (lowest Row, then
        // Col - the tile TrackLayout.CutFor keys on) rather than in list
        // order, so the same plan saved in another order measures the same.
        var finishes = design.Decals
            .Where(d => d is not null && d.Kind == RcpDecalKind.FinishLine)
            .OrderBy(d => d.Row)
            .ThenBy(d => d.Col)
            .ToList();

        if (finishes.FirstOrDefault() is not { } finish)
        {
            return (null, "Put the finish line on the plan - sectors are measured from it.");
        }

        if (design.Route.Count < 2)
        {
            return (null, "Draw the racing line (or press Trace route) through the finish line and the split loops.");
        }

        // The last sector runs from the last split loop round to the finish
        // line, which only a lap does.
        if (!design.RouteClosed)
        {
            return (null, "Tick \"The line closes into a lap\" - the last sector runs round to the finish line.");
        }

        var points = design.Route.Select(p => p.Point).ToList();

        if (design.RouteClosed)
        {
            points.Add(points[0]);
        }

        var length = RouteLengthInTiles(design);

        // The first of those tiles the racing line passes over.
        if (finishes.Select(f => Along(points, f)).FirstOrDefault(a => a is not null) is not { } start)
        {
            return (null, "The racing line doesn't pass over the finish line.");
        }

        var at = new List<float>();

        foreach (var split in splits)
        {
            if (Along(points, split) is not { } along)
            {
                return (null, $"The racing line doesn't pass over Split {SplitNumberOf(split.Kind)}.");
            }

            var fromFinish = along - start;

            if (fromFinish <= 0)
            {
                if (!design.RouteClosed)
                {
                    return (null, $"Split {SplitNumberOf(split.Kind)} comes before the finish line on the racing line.");
                }

                fromFinish += length;
            }

            at.Add(fromFinish);
        }

        // Every loop in exactly the reverse order: the line runs against
        // the cars, not the loops numbered wrong.
        if (at.Count >= 2 && at.Zip(at.Skip(1)).All(pair => pair.Second < pair.First))
        {
            return (null, "The racing line runs the wrong way round - click Reverse.");
        }

        for (var i = 1; i < at.Count; i++)
        {
            if (at[i] <= at[i - 1])
            {
                return (null, $"Split {i + 1} comes before Split {i} going round. Number the loops in the order cars reach them.");
            }
        }

        if (at[^1] >= length)
        {
            return (null, $"Split {at.Count} is past the end of the racing line.");
        }

        var sectors = new float[at.Count + 1];
        var from = 0f;

        for (var i = 0; i < at.Count; i++)
        {
            sectors[i] = at[i] - from;
            from = at[i];
        }

        sectors[^1] = length - from;
        return (sectors, null);

        // How far along the line the nearest point to this marking's tile
        // centre is, or null when the line doesn't come near it.
        static float? Along(List<PointF> line, RcpDecal decal)
        {
            var target = new PointF(decal.Col + 0.5f, decal.Row + 0.5f);
            float? best = null;
            var bestGap = float.MaxValue;
            var travelled = 0f;

            for (var i = 1; i < line.Count; i++)
            {
                var a = line[i - 1];
                var b = line[i];
                var segment = Distance(a, b);

                if (segment <= 0)
                {
                    continue;
                }

                var t = ((target.X - a.X) * (b.X - a.X) + (target.Y - a.Y) * (b.Y - a.Y)) / (segment * segment);
                t = Math.Clamp(t, 0f, 1f);

                var nearest = new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                var gap = Distance(nearest, target);

                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = travelled + segment * t;
                }

                travelled += segment;
            }

            return bestGap <= OnRouteTiles ? best : null;
        }
    }

    /// <summary>Tiles to feet, at this plan's tile size.</summary>
    public static double TilesToFeet(RcpTrackDesign design, float tiles) => tiles * design.TileSizeMm / 1000.0 * 3.28084;

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
    ///
    /// WIDE LANES (Track Designer overhaul, Phase 3, 25 Oct 2026). A lane two
    /// or three tiles wide is open on three sides at every tile, so it used
    /// to fail at the first one. Now, when the corridor is wide, the line
    /// goes down its middle (CentreLine): the cells' centres for a lane an
    /// odd number of tiles wide, the seams between the middle two for an
    /// even one. A one-tile lane is walked exactly as it always was.
    ///
    /// A turn rail stops a car too (25 Oct 2026, same phase). The straights
    /// a turn sweeps across are not on the plan - the turn takes their place
    /// (RcpAutoRails.Swept) - so with floor beyond the corner the walk used
    /// to step straight out through it.
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

        // First tile wins at a cell, as it does in At(). The editor never
        // puts two tiles in one cell, but track-layouts.json can come back
        // from a restored backup or a hand edit with two, and ToDictionary
        // threw on the second - Trace route then failed with an error box.
        var cells = new Dictionary<(int Col, int Row), RcpTile>();

        foreach (var tile in floor)
        {
            cells.TryAdd((tile.Col, tile.Row), tile);
        }

        var blocked = BlockedSeams(design);

        List<(int Col, int Row)> Open(int col, int row) =>
            Enum.GetValues<RcpEdge>()
                .Where(e => !blocked.Contains(EdgeLine(col, row, e)))
                .Select(e => Neighbour(col, row, e))
                .Select(p => (p.X, p.Y))
                .Where(cells.ContainsKey)
                .ToList();

        // Start beside the finish line when there is one, so the lap begins
        // where the lap begins.
        var startDecal = design.Decals.FirstOrDefault(d => d.Kind == RcpDecalKind.FinishLine);

        // Otherwise on a lane, when there is one (25 Oct 2026): a lane laid
        // over a whole floor is the track, and the floor round it is not -
        // starting on the first tile of the floor traced the floor outside
        // the lane's rails instead.
        var firstLane = floor
            .Where(t => t.Lane > 0)
            .OrderBy(t => t.Row)
            .ThenBy(t => t.Col)
            .FirstOrDefault();

        var start = startDecal is not null && cells.ContainsKey((startDecal.Col, startDecal.Row))
            ? (startDecal.Col, startDecal.Row)
            : firstLane is not null
                ? (firstLane.Col, firstLane.Row)
                : (floor[0].Col, floor[0].Row);

        // WHY (23 Oct 2026, bug hunt 10): the "open on three sides" test
        // used to run over EVERY floor cell, including the floor inside an
        // island the rails wall off from the corridor. Laying the whole
        // floor first and then standing rails on it is the way the designer
        // tells people to work, so any island two tiles or more across
        // failed Trace route, naming a tile no car can reach. Now only the
        // floor a car can get to from the start is checked.
        var reachable = new HashSet<(int, int)> { start };
        var frontier = new Queue<(int, int)>();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            var (c, r) = frontier.Dequeue();

            foreach (var n in Open(c, r))
            {
                if (reachable.Add(n))
                {
                    frontier.Enqueue(n);
                }
            }
        }

        // Nullable, so "none" can't be mistaken for the tile at column 0,
        // row 0 - which is where the first tile of a plan usually goes, and
        // which the old "!= default" test let through however open it was.
        (int, int)? open3 = reachable
            .Where(k => Open(k.Item1, k.Item2).Count > 2)
            .Select(k => ((int, int)?)k)
            .FirstOrDefault();

        if (open3 is { } wide)
        {
            var from = startDecal is not null
                ? new PointF(startDecal.Col + 0.5f, startDecal.Row + 0.5f)
                : new PointF(start.Item1 + 0.5f, start.Item2 + 0.5f);

            if (CentreLine(reachable, blocked, from) is { } middle)
            {
                return middle;
            }

            // Still names the first tile open on three sides: in a lane that
            // is not one width all the way round, that is often the place.
            problem = $"The floor at column {wide.Item1 + 1}, row {wide.Item2 + 1} is open on three sides, "
                      + "so there's more than one way round. Make the track one width all the way round, wall it in, "
                      + "or draw the line by hand.";
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

    /// <summary>
    /// Every seam a car cannot cross: the rails, and the seams under the
    /// turn rails (which take the place of the straights they sweep across).
    /// </summary>
    public static HashSet<(int Col, int Row, bool Vertical)> BlockedSeams(RcpTrackDesign design)
    {
        var blocked = design.Walls.Select(w => (w.Col, w.Row, w.Vertical)).ToHashSet();

        foreach (var curve in design.Curves)
        {
            // Only the seams it sweeps the whole of: a hand hairpin of half a
            // tile leaves its seam half open, as it always has here.
            blocked.UnionWith(RcpAutoRails.Swept(curve, wholeOnly: true));
        }

        return blocked;
    }

    /// <summary>
    /// The line down the middle of a wide corridor, as one closed lap - or
    /// null when there isn't one.
    ///
    /// Peels the corridor a tile at a time from its rails inwards. After
    /// each peel it looks for a single loop in what is left, two ways:
    ///   - the CELLS, each joined to exactly two others - a lane an odd
    ///     number of tiles wide comes down to this, and the line goes
    ///     through their centres;
    ///   - the GRID POINTS with corridor all round them and no rail on
    ///     any of their four seams, each joined to exactly two others - a
    ///     lane an even number of tiles wide comes down to this, and the
    ///     line runs along the seams between its middle two tiles.
    /// A corridor that is not one width all the way round, or that opens
    /// out into a bay, never comes down to a single loop, so it is refused
    /// rather than guessed at.
    ///
    /// Starts at the point of the loop nearest <paramref name="from"/> (the
    /// finish line, when there is one) and sets off the way the narrow walk
    /// does: the first of north, east, south and west that is open.
    /// </summary>
    internal static List<PointF>? CentreLine(
        IReadOnlyCollection<(int, int)> corridor,
        HashSet<(int Col, int Row, bool Vertical)> blocked,
        PointF from)
    {
        var layer = new HashSet<(int, int)>(corridor);

        // A wide lane is three tiles at most today, so a handful of peels
        // is plenty; the guard is for arithmetic, not for people.
        for (var peel = 0; peel < 32 && layer.Count > 0; peel++)
        {
            var current = layer;

            List<(int, int)> Cells((int Col, int Row) cell) =>
                new[] { RcpEdge.North, RcpEdge.East, RcpEdge.South, RcpEdge.West }
                    .Where(e => !blocked.Contains(EdgeLine(cell.Col, cell.Row, e)))
                    .Select(e => Neighbour(cell.Col, cell.Row, e))
                    .Select(p => (p.X, p.Y))
                    .Where(current.Contains)
                    .ToList();

            if (Loop(corridor, current, Cells, c => new PointF(c.Item1 + 0.5f, c.Item2 + 0.5f), from) is { } cellLine)
            {
                return cellLine;
            }

            // A grid point is in the middle of the corridor when all four
            // cells round it are, and no rail runs out from it.
            bool Inside(int x, int y) =>
                current.Contains((x - 1, y - 1)) && current.Contains((x, y - 1))
                && current.Contains((x - 1, y)) && current.Contains((x, y))
                && !blocked.Contains((x, y - 1, true)) && !blocked.Contains((x, y, true))
                && !blocked.Contains((x - 1, y, false)) && !blocked.Contains((x, y, false));

            var points = current
                .Select(c => (c.Item1 + 1, c.Item2 + 1))
                .Where(p => Inside(p.Item1, p.Item2))
                .ToHashSet();

            List<(int, int)> Points((int X, int Y) p) =>
                new[] { (p.X, p.Y - 1), (p.X + 1, p.Y), (p.X, p.Y + 1), (p.X - 1, p.Y) }
                    .Where(points.Contains)
                    .ToList();

            if (Loop(corridor, points, Points, p => new PointF(p.Item1, p.Item2), from) is { } seamLine)
            {
                return seamLine;
            }

            // Peel: keep only the cells open on all four sides with
            // corridor at all four diagonals too. Without the diagonals the
            // tile on the inside of each bend, which touches the island
            // only at its corner, was left behind as a stub off the loop.
            layer = current
                .Where(c => Cells(c).Count == 4
                            && current.Contains((c.Item1 - 1, c.Item2 - 1))
                            && current.Contains((c.Item1 + 1, c.Item2 - 1))
                            && current.Contains((c.Item1 - 1, c.Item2 + 1))
                            && current.Contains((c.Item1 + 1, c.Item2 + 1)))
                .ToHashSet();
        }

        return null;
    }

    /// <summary>
    /// Whether a closed line goes round at least one tile that is not
    /// corridor - an island - rather than round a patch of open floor.
    /// </summary>
    private static bool GoesRound(List<PointF> line, IReadOnlyCollection<(int, int)> corridor)
    {
        var inCorridor = corridor as ICollection<(int, int)> ?? corridor.ToHashSet();

        var left = (int)MathF.Floor(line.Min(p => p.X));
        var right = (int)MathF.Ceiling(line.Max(p => p.X));
        var top = (int)MathF.Floor(line.Min(p => p.Y));
        var bottom = (int)MathF.Ceiling(line.Max(p => p.Y));

        for (var col = left; col < right; col++)
        {
            for (var row = top; row < bottom; row++)
            {
                if (!inCorridor.Contains((col, row)) && Inside(line, new PointF(col + 0.5f, row + 0.5f)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a point is inside a closed line - the even-odd rule.</summary>
    private static bool Inside(List<PointF> line, PointF p)
    {
        var inside = false;

        for (int i = 0, j = line.Count - 1; i < line.Count; j = i++)
        {
            var a = line[i];
            var b = line[j];

            if ((a.Y > p.Y) != (b.Y > p.Y)
                && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// The places in order round a single loop through ALL of them, each
    /// joined to exactly two others - or null when they are not that.
    /// </summary>
    private static List<PointF>? Loop(
        IReadOnlyCollection<(int, int)> corridor,
        HashSet<(int, int)> places,
        Func<(int, int), List<(int, int)>> joined,
        Func<(int, int), PointF> centre,
        PointF from)
    {
        if (places.Count < 4 || places.Any(p => joined(p).Count != 2))
        {
            return null;
        }

        // Nearest the start; ties to the topmost, then leftmost, so the
        // same plan always starts in the same place.
        var start = places
            .OrderBy(p => Distance(centre(p), from))
            .ThenBy(p => p.Item2)
            .ThenBy(p => p.Item1)
            .First();

        var line = new List<PointF>();
        var previous = ((int, int)?)null;
        var at = start;

        for (var guard = 0; guard <= places.Count; guard++)
        {
            line.Add(centre(at));

            var options = joined(at);
            var next = previous is { } back && options[0] == back ? options[1] : options[0];

            previous = at;
            at = next;

            if (at == start)
            {
                // Round once, and through every place: anything left out is
                // a second loop, and that is not one way round. And round
                // something: peeled far enough, a solid floor comes down to
                // a little ring in the middle of the room, which is not a
                // track (review, 25 Oct 2026).
                return line.Count == places.Count && GoesRound(line, corridor) ? line : null;
            }
        }

        return null;
    }
}
