namespace HeatCaller;

/// <summary>
/// Which of RCP's pieces a tile is: a plain tile, or a tile with its rail
/// bolted on.
///
/// On a real RCP track a rail never stands on its own. It is bolted along
/// the one edge of its tile that has the bolt holes, flush with that edge,
/// and it stays on between race nights - so a tile and its rail go down as
/// one piece, and a tile carries one rail at most (Asher, 7 Nov 2026; his
/// photos are in plans\track-designer, the plan is plans\track-designer-
/// railed-tiles-plan.md).
///
/// Saved as numbers: new pieces go on the end, existing ones never move.
/// </summary>
public enum RcpPiece
{
    /// <summary>Nothing on it.</summary>
    Plain,

    /// <summary>A straight rail on its bolt-hole edge (RcpTile.Facing), held by two bolts.</summary>
    Rail,

    /// <summary>The finish-line straight rail on its bolt-hole edge.</summary>
    Finish,

    /// <summary>A rail on its bolt-hole edge that arches into the lane - it goes on a straight.</summary>
    Arched,

    /// <summary>
    /// The outer curve tile: a QUARTER CIRCLE, not a square, with the
    /// one-tile turn rail on its curved side (three bolts) and bare room in
    /// the square's corner beyond the arc. RcpTile.Corner is that corner.
    /// </summary>
    OuterCurve,

    /// <summary>
    /// The inner corner tile: a square tile with a small quarter circle of
    /// red rail in one corner (RcpTile.Corner), held by one bolt, at the
    /// inside of a bend.
    /// </summary>
    InnerCorner,

    /// <summary>A tile with no bolt holes - it can never take a rail. Infield and lane widening.</summary>
    Blank,
}

/// <summary>
/// Puts the rails of a plan onto the tiles they are bolted to, and works the
/// rails back out from the tiles (Track Designer, railed tiles, Phase 1,
/// 7 Nov 2026).
///
/// The rest of the designer - the painter, the 3D view, the lap, Trace
/// route, the Big Screen map, sector lengths, the stock check - reads the
/// rails as seams (RcpTrackDesign.Walls and Curves), and goes on doing so.
/// In this phase the seams are still what the editor draws and saves; Adopt
/// works out which tile each rail is on after every edit, and Project is
/// proven (RcpPiecesTests) to give back exactly the seams it was handed. From
/// Phase 2 the editor lays the pieces and the seams are worked out from them.
///
/// Nothing here refuses anything, which is the designer's way: a rail that
/// can't be put on a tile of its own - a second rail on a tile, a rail with
/// no tile under it - is counted in the report, and stays on its seam.
/// </summary>
public static class RcpPieces
{
    /// <summary>What adopting a plan's rails onto its tiles couldn't do.</summary>
    public sealed record Report(int RailsWithNoTile, int SecondRails, int TurnsWithNoTile, int OtherTurns)
    {
        /// <summary>Every rail and turn is on a tile of its own: the plan can be built from the club's pieces.</summary>
        public bool AllOnTiles => RailsWithNoTile == 0 && SecondRails == 0 && TurnsWithNoTile == 0 && OtherTurns == 0;
    }

    /// <summary>Whether this piece is worked out from the rails, rather than laid as itself.</summary>
    private static bool Worked(RcpPiece piece) =>
        piece is RcpPiece.Rail or RcpPiece.OuterCurve or RcpPiece.InnerCorner;

    /// <summary>Whether this piece carries a straight rail on its Facing edge.</summary>
    public static bool HasStraight(RcpPiece piece) =>
        piece is RcpPiece.Rail or RcpPiece.Finish or RcpPiece.Arched;

    /// <summary>
    /// Puts every rail of the plan on the tile it is bolted to, setting each
    /// tile's Piece, Facing and Corner. Returns what couldn't be put on a
    /// tile. The seams themselves are left exactly as they are.
    ///
    /// <list type="bullet">
    /// <item>A one-tile turn rail goes on the tile it bulges into: an outer curve tile.</item>
    /// <item>The inside of a bend - three of the four tiles round a point one stretch of track, the fourth not - is an inner corner tile, as RcpPlan.CornerTiles finds it.</item>
    /// <item>A divider (a Double rail) is two rails back to back: one on the tile either side of it, each facing its own lane.</item>
    /// <item>A single rail goes on the TRACK tile beside it - the one the car drives on - and on the other when the track's is taken or isn't there.</item>
    /// </list>
    ///
    /// Finish, arched and blank tiles are laid as themselves, so they are
    /// kept; a finish or arched rail already on a seam stands for the rail
    /// there.
    /// </summary>
    public static Report Adopt(RcpTrackDesign design)
    {
        var tiles = new Dictionary<(int, int), RcpTile>();

        foreach (var tile in design.Tiles)
        {
            if (Worked(tile.Piece))
            {
                tile.Piece = RcpPiece.Plain;
            }

            tiles[(tile.Col, tile.Row)] = tile;
        }

        // Floor a rail can be bolted to: not blocked out, not a blank.
        RcpTile? Free(int col, int row) =>
            tiles.TryGetValue((col, row), out var tile)
            && tile.Kind != RcpTileKind.Infield
            && tile.Piece == RcpPiece.Plain
                ? tile
                : null;

        bool Floor(int col, int row) =>
            tiles.TryGetValue((col, row), out var tile) && tile.Kind != RcpTileKind.Infield && tile.Piece != RcpPiece.Blank;

        var railsWithNoTile = 0;
        var secondRails = 0;
        var turnsWithNoTile = 0;
        var otherTurns = 0;

        // --- turns: the outer curve tiles -----------------------------------
        foreach (var curve in design.Curves.OrderBy(c => c.Row).ThenBy(c => c.Col).ThenBy(c => c.Quadrant))
        {
            // The club has only the one-tile turn, and one turn rail is one
            // tile: two back to back at a divider's end is not a piece.
            if (Math.Abs(curve.Radius - 1f) > 0.01f || curve.Double)
            {
                otherTurns++;
                continue;
            }

            var (col, row) = RcpPlan.QuadrantCell(curve);

            if (Free(col, row) is { } tile)
            {
                tile.Piece = RcpPiece.OuterCurve;
                tile.Corner = curve.Quadrant;
            }
            else
            {
                turnsWithNoTile++;
            }
        }

        // --- the inside of every bend: the inner corner tiles ----------------
        foreach (var (col, row, corner) in InnerCorners(design))
        {
            if (Free(col, row) is { } tile)
            {
                tile.Piece = RcpPiece.InnerCorner;
                tile.Corner = corner;
            }
        }

        // --- straight rails -----------------------------------------------------
        var laid = new HashSet<(int, int, bool)>();

        foreach (var tile in design.Tiles.Where(t => HasStraight(t.Piece)))
        {
            laid.Add(RcpPlan.EdgeLine(tile.Col, tile.Row, tile.Facing));
        }

        var track = RcpPlan.TrackRegions(design);
        bool IsTrack(int col, int row) => track.ContainsKey((col, row));

        // Dividers first: they need the tile on BOTH sides, so they are
        // given their pick before a single rail takes one of them.
        foreach (var wall in design.Walls.OrderBy(w => w.Double ? 0 : 1).ThenBy(w => w.Row).ThenBy(w => w.Col).ThenBy(w => w.Vertical))
        {
            if (laid.Contains((wall.Col, wall.Row, wall.Vertical)))
            {
                continue;
            }

            // The two cells either side of the seam, and the edge of each
            // that the seam is: A is north or west of it, B south or east.
            var (aCol, aRow, aEdge) = wall.Vertical ? (wall.Col - 1, wall.Row, RcpEdge.East) : (wall.Col, wall.Row - 1, RcpEdge.South);
            var (bCol, bRow, bEdge) = wall.Vertical ? (wall.Col, wall.Row, RcpEdge.West) : (wall.Col, wall.Row, RcpEdge.North);

            if (wall.Double)
            {
                foreach (var (col, row, edge) in new[] { (aCol, aRow, aEdge), (bCol, bRow, bEdge) })
                {
                    if (Free(col, row) is { } tile)
                    {
                        Bolt(tile, edge);
                    }
                    else if (Floor(col, row))
                    {
                        secondRails++;
                    }
                    else
                    {
                        railsWithNoTile++;
                    }
                }

                continue;
            }

            // The track's own tile first: a rail at the edge of the track is
            // bolted to the tile the car drives on, not the infield beyond.
            var sides = new[] { (aCol, aRow, aEdge), (bCol, bRow, bEdge) }
                .OrderBy(s => IsTrack(s.Item1, s.Item2) ? 0 : 1)
                .ToArray();

            var placed = false;

            foreach (var (col, row, edge) in sides)
            {
                if (Free(col, row) is { } tile)
                {
                    Bolt(tile, edge);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                if (sides.Any(s => Floor(s.Item1, s.Item2)))
                {
                    secondRails++;
                }
                else
                {
                    railsWithNoTile++;
                }
            }
        }

        return new Report(railsWithNoTile, secondRails, turnsWithNoTile, otherTurns);
    }

    private static void Bolt(RcpTile tile, RcpEdge edge)
    {
        tile.Piece = RcpPiece.Rail;
        tile.Facing = edge;
    }

    /// <summary>
    /// The rails the tiles carry, as the seams the rest of the designer
    /// reads: a straight rail on its edge's seam - two on one seam, back to
    /// back, are a divider (Double) - and an outer curve tile's turn rail
    /// as a one-tile turn at the tile's inside corner. An inner corner
    /// tile's piece is not a seam (it is counted, as today).
    /// </summary>
    public static (List<RcpWall> Walls, List<RcpCurve> Curves) Project(RcpTrackDesign design)
    {
        var seams = new Dictionary<(int Col, int Row, bool Vertical), int>();
        var curves = new List<RcpCurve>();

        foreach (var tile in design.Tiles.OrderBy(t => t.Row).ThenBy(t => t.Col))
        {
            if (HasStraight(tile.Piece))
            {
                var seam = RcpPlan.EdgeLine(tile.Col, tile.Row, tile.Facing);
                seams[seam] = seams.TryGetValue(seam, out var n) ? n + 1 : 1;
            }
            else if (tile.Piece == RcpPiece.OuterCurve)
            {
                var (dx, dy) = RcpAutoRails.Step(tile.Corner);

                curves.Add(new RcpCurve
                {
                    // The turn's point is the tile's inside corner - across
                    // the tile from the corner the arc is in.
                    Col = dx > 0 ? tile.Col : tile.Col + 1,
                    Row = dy > 0 ? tile.Row : tile.Row + 1,
                    Quadrant = tile.Corner,
                    Radius = 1f,
                });
            }
        }

        var walls = seams
            .Select(s => new RcpWall { Col = s.Key.Col, Row = s.Key.Row, Vertical = s.Key.Vertical, Double = s.Value > 1 })
            .ToList();

        return (walls, curves);
    }

    /// <summary>
    /// The inside of every bend: each inner corner tile with the corner of
    /// it the piece sits in - the one at the grid point where the track goes
    /// round the island. The same rule as RcpPlan.CornerTiles, which finds
    /// the tiles; this also says which corner.
    /// </summary>
    internal static IEnumerable<(int Col, int Row, RcpQuadrant Corner)> InnerCorners(RcpTrackDesign design)
    {
        var inner = RcpPlan.CornerTiles(design).Inner;

        if (inner.Count == 0)
        {
            yield break;
        }

        var track = RcpPlan.TrackRegions(design);
        int RegionAt(int col, int row) => track.TryGetValue((col, row), out var id) ? id : 0;

        foreach (var (col, row) in inner.OrderBy(c => c.Row).ThenBy(c => c.Col))
        {
            var id = RegionAt(col, row);

            // Each corner of the tile, with the cell across that point from
            // it and the two beside it there: the piece is at the corner
            // whose far cell is not this stretch of track and whose two side
            // cells are.
            foreach (var corner in Enum.GetValues<RcpQuadrant>())
            {
                var (dx, dy) = RcpAutoRails.Step(corner);

                if (RegionAt(col + dx, row + dy) != id
                    && RegionAt(col + dx, row) == id
                    && RegionAt(col, row + dy) == id)
                {
                    yield return (col, row, corner);
                    break;
                }
            }
        }
    }
}
