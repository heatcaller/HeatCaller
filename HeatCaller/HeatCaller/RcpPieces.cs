namespace HeatCaller;

/// <summary>
/// Which of RCP's pieces a tile is: a plain tile, or a tile with its rail
/// bolted on.
///
/// On a real RCP track a rail never stands on its own. It is bolted along
/// the one edge of its tile that has the bolt holes, flush with that edge,
/// and it stays on between race nights - so a tile and its rail go down as
/// one piece, and a tile carries one rail (Asher, 7 Nov 2026; his photos are
/// in plans\track-designer, the plan is plans\track-designer-railed-tiles-
/// plan.md). A second rail on a tile is custom work - two more holes cut and
/// a rail trimmed to fit - which is what a divider meeting the outside wall
/// takes.
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
    /// inside of a bend. Worked out from the bend, not laid by hand.
    /// </summary>
    InnerCorner,

    /// <summary>A tile with no bolt holes - it can never take a rail. Infield and lane widening.</summary>
    Blank,
}

/// <summary>
/// Puts the rails of a plan onto the tiles they are bolted to, works the
/// rails back out from the tiles, and lays and takes up a whole piece - a
/// tile with its rail - in one go (Track Designer, railed tiles, Phases 1
/// and 2, 7 Nov 2026).
///
/// The rest of the designer - the painter, the 3D view, the lap, Trace
/// route, the Big Screen map, sector lengths, the stock check - reads the
/// rails as seams (RcpTrackDesign.Walls and Curves), and goes on doing so.
/// The seams are what is saved; Lay and TakeUp change a tile and its seams
/// together, Adopt works out which tile each rail is on after every edit,
/// and Project is proven (RcpPiecesTests) to give back exactly the seams.
///
/// Nothing here refuses anything, which is the designer's way: a second rail
/// on a tile is a custom tile, counted and shown; a rail with no tile under
/// it is counted, and stays on its seam.
/// </summary>
public static class RcpPieces
{
    /// <summary>What adopting a plan's rails onto its tiles couldn't do, and where the custom tiles are.</summary>
    public sealed record Report(
        int RailsWithNoTile,
        int SecondRails,
        int TurnsWithNoTile,
        int OtherTurns,
        IReadOnlyList<(int Col, int Row)> CustomTiles,
        IReadOnlyList<(int Col, int Row, bool Vertical)>? LooseSeams = null,
        IReadOnlyList<RcpCurve>? LooseTurns = null)
    {
        public static Report None { get; } = new(0, 0, 0, 0, Array.Empty<(int, int)>());

        /// <summary>Where each rail with no tile under it is (railed tiles, 13 Nov 2026) - shown on the board.</summary>
        public IReadOnlyList<(int Col, int Row, bool Vertical)> RailsWithNoTileAt => LooseSeams ?? Array.Empty<(int, int, bool)>();

        /// <summary>Each turn that isn't one of the club's pieces - a two-tile turn, a back-to-back pair, or one with no tile under it.</summary>
        public IReadOnlyList<RcpCurve> TurnsNotPieces => LooseTurns ?? Array.Empty<RcpCurve>();

        /// <summary>Every rail and turn is on a tile of its own: the plan can be built from the club's pieces with nothing custom.</summary>
        public bool AllOnTiles => RailsWithNoTile == 0 && SecondRails == 0 && TurnsWithNoTile == 0 && OtherTurns == 0;

        public override string ToString() =>
            $"rails with no tile {RailsWithNoTile}, second rails {SecondRails}, turns with no tile {TurnsWithNoTile}, other turns {OtherTurns}";
    }

    /// <summary>Whether this piece is worked out from the rails, rather than laid as itself.</summary>
    private static bool Worked(RcpPiece piece) =>
        piece is RcpPiece.Rail or RcpPiece.OuterCurve or RcpPiece.InnerCorner;

    /// <summary>Whether this piece carries a straight rail on its Facing edge.</summary>
    public static bool HasStraight(RcpPiece piece) =>
        piece is RcpPiece.Rail or RcpPiece.Finish or RcpPiece.Arched;

    /// <summary>The corner across a tile from this one.</summary>
    public static RcpQuadrant Opposite(RcpQuadrant corner) => corner switch
    {
        RcpQuadrant.NorthEast => RcpQuadrant.SouthWest,
        RcpQuadrant.SouthEast => RcpQuadrant.NorthWest,
        RcpQuadrant.SouthWest => RcpQuadrant.NorthEast,
        _ => RcpQuadrant.SouthEast,
    };

    /// <summary>The grid point at a corner of a tile.</summary>
    public static Point CornerPoint(int col, int row, RcpQuadrant corner)
    {
        var (dx, dy) = RcpAutoRails.Step(corner);
        return new Point(dx > 0 ? col + 1 : col, dy > 0 ? row + 1 : row);
    }

    /// <summary>The one-tile turn rail an outer curve tile carries, with its arc across <paramref name="corner"/>.</summary>
    public static RcpCurve TurnFor(int col, int row, RcpQuadrant corner)
    {
        var at = CornerPoint(col, row, corner);
        return new RcpCurve { Col = at.X, Row = at.Y, Quadrant = Opposite(corner), Radius = 1f };
    }

    /// <summary>The corner of its tile a one-tile turn rail's arc runs across.</summary>
    public static RcpQuadrant CornerOf(RcpCurve curve) => Opposite(curve.Quadrant);

    /// <summary>
    /// The outer curve tile a turn rail is part of, when it is one tile's own
    /// turn - null for a two-tile turn, a back-to-back pair, or a turn with
    /// no outer curve tile under it (as the pieces were last worked out).
    /// </summary>
    public static RcpTile? TurnTile(RcpTrackDesign design, RcpCurve curve)
    {
        if (curve.Double || Math.Abs(curve.Radius - 1f) > 0.01f)
        {
            return null;
        }

        var (col, row) = RcpPlan.QuadrantCell(curve);

        return design.At(col, row) is { Piece: RcpPiece.OuterCurve } tile && tile.Corner == CornerOf(curve)
            ? tile
            : null;
    }

    /// <summary>
    /// Puts every rail of the plan on the tile it is bolted to, setting each
    /// tile's Piece, Facing and Corner. Returns what couldn't be put on a
    /// tile of its own, and where the custom tiles are. The seams themselves
    /// are left exactly as they are.
    ///
    /// <list type="bullet">
    /// <item>A one-tile turn rail goes on the tile it bulges into: an outer curve tile.</item>
    /// <item>The inside of a bend - three of the four tiles round a point one stretch of track, the fourth not - is an inner corner tile, as RcpPlan.CornerTiles finds it.</item>
    /// <item>A divider (a Double rail) is two rails back to back: one on the tile either side of it, each facing its own lane.</item>
    /// <item>A single rail stays on the tile that had it last time, so an edit never moves a piece; otherwise it goes on the TRACK tile beside it - the one the car drives on - and on the other when the track's is taken or isn't there.</item>
    /// <item>A rail whose tiles are both taken is a second rail on a tile: a custom tile.</item>
    /// </list>
    ///
    /// Finish, arched and blank tiles are laid as themselves, so they are
    /// kept; a finish or arched rail on a seam stands for the rail there.
    /// </summary>
    public static Report Adopt(RcpTrackDesign design)
    {
        var tiles = new Dictionary<(int, int), RcpTile>();

        // Which tile had which seam's rail last time - kept, so a rail
        // doesn't hop to the tile across the seam because of an edit
        // somewhere else.
        var had = new Dictionary<(int, int, bool), RcpTile>();

        foreach (var tile in design.Tiles)
        {
            if (tile.Piece == RcpPiece.Rail)
            {
                had.TryAdd(RcpPlan.EdgeLine(tile.Col, tile.Row, tile.Facing), tile);
            }

            if (Worked(tile.Piece))
            {
                tile.Piece = RcpPiece.Plain;
            }

            tiles[(tile.Col, tile.Row)] = tile;
        }

        // Floor a rail can be bolted to: not blocked out, not a blank, free.
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
        var custom = new List<(int, int)>();
        var looseSeams = new List<(int, int, bool)>();
        var looseTurns = new List<RcpCurve>();

        // --- turns: the outer curve tiles -----------------------------------
        foreach (var curve in design.Curves.OrderBy(c => c.Row).ThenBy(c => c.Col).ThenBy(c => c.Quadrant))
        {
            // The club has only the one-tile turn, and one turn rail is one
            // tile: two back to back at a divider's end is not a piece.
            if (Math.Abs(curve.Radius - 1f) > 0.01f || curve.Double)
            {
                otherTurns++;
                looseTurns.Add(curve);
                continue;
            }

            var (col, row) = RcpPlan.QuadrantCell(curve);

            if (Free(col, row) is { } tile)
            {
                tile.Piece = RcpPiece.OuterCurve;
                tile.Corner = CornerOf(curve);
            }
            else
            {
                turnsWithNoTile++;
                looseTurns.Add(curve);
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

        void Custom(int col, int row)
        {
            secondRails++;

            if (!custom.Contains((col, row)))
            {
                custom.Add((col, row));
            }
        }

        // Dividers first: they need the tile on BOTH sides, so they are
        // given their pick before a single rail takes one of them.
        foreach (var wall in design.Walls.OrderBy(w => w.Double ? 0 : 1).ThenBy(w => w.Row).ThenBy(w => w.Col).ThenBy(w => w.Vertical))
        {
            var seam = (wall.Col, wall.Row, wall.Vertical);

            // A divider with a finish or arched tile on one side still needs
            // the other lane's rail (bug hunt 20), so only a single rail is
            // done here; the Double branch skips just the side that has it.
            if (laid.Contains(seam) && !wall.Double)
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
                    // This lane's rail is already the finish or arched tile's own.
                    if (tiles.TryGetValue((col, row), out var own) && HasStraight(own.Piece) && own.Facing == edge)
                    {
                        continue;
                    }

                    if (Free(col, row) is { } tile)
                    {
                        Bolt(tile, edge);
                    }
                    else if (Floor(col, row))
                    {
                        Custom(col, row);
                    }
                    else
                    {
                        railsWithNoTile++;
                        looseSeams.Add(seam);
                    }
                }

                continue;
            }

            // The tile that had it, then the track's own tile: a rail at the
            // edge of the track is bolted to the tile the car drives on, not
            // the infield beyond.
            var sides = new[] { (aCol, aRow, aEdge), (bCol, bRow, bEdge) }
                .OrderBy(s => had.TryGetValue(seam, out var was) && was.Col == s.Item1 && was.Row == s.Item2 ? 0 : 1)
                .ThenBy(s => IsTrack(s.Item1, s.Item2) ? 0 : 1)
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

            if (placed)
            {
                continue;
            }

            // The first side that is floor - none, and the rail has no tile.
            // (10 Nov 2026, bug hunt 16: FirstOrDefault gave the default
            // (0, 0) side when neither was floor, so tile (0,0) - nothing to
            // do with this rail - was made a custom tile.)
            var onFloor = false;

            foreach (var (col, row, _) in sides)
            {
                if (Floor(col, row))
                {
                    Custom(col, row);
                    onFloor = true;
                    break;
                }
            }

            if (!onFloor)
            {
                railsWithNoTile++;
                looseSeams.Add(seam);
            }
        }

        return new Report(railsWithNoTile, secondRails, turnsWithNoTile, otherTurns, custom, looseSeams, looseTurns);
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
    /// as a one-tile turn anchored at its corner. An inner corner tile's
    /// piece is not a seam (it is counted, as before).
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
                curves.Add(TurnFor(tile.Col, tile.Row, tile.Corner));
            }
        }

        var walls = seams
            .Select(s => new RcpWall { Col = s.Key.Col, Row = s.Key.Row, Vertical = s.Key.Vertical, Double = s.Value > 1 })
            .ToList();

        return (walls, curves);
    }

    /// <summary>
    /// Lays one whole piece on a cell - the tile and its rail together
    /// (railed tiles, Phase 2). Whatever piece was there goes first, with the
    /// rail bolted to it; then the new tile goes down with its own rail.
    /// <list type="bullet">
    /// <item><paramref name="piece"/> Rail, Finish or Arched: a straight rail on <paramref name="facing"/>. If the tile across that seam already has its rail there, the two stand back to back: a divider.</item>
    /// <item>OuterCurve: the one-tile turn rail with its arc across <paramref name="corner"/>, in place of any straight rails on the two edges it sweeps.</item>
    /// <item>Plain or Blank: just the tile.</item>
    /// </list>
    /// Returns what it took off the plan, so a selection holding any of it
    /// can let go. Lanes are left alone: a lane's tile laid over becomes a
    /// tile of its own, and the lane's rails round it are worked out again.
    /// </summary>
    public static (List<RcpWall> RemovedWalls, List<RcpCurve> RemovedCurves) Lay(
        RcpTrackDesign design, int col, int row, RcpPiece piece, RcpEdge facing, RcpQuadrant corner)
    {
        var removedWalls = new List<RcpWall>();
        var removedCurves = new List<RcpCurve>();

        var tile = design.At(col, row);

        if (tile is null)
        {
            tile = new RcpTile { Col = col, Row = row };
            design.Tiles.Add(tile);
        }
        else
        {
            Unbolt(design, tile, removedWalls, removedCurves);
        }

        tile.Kind = RcpTileKind.Straight;
        tile.Lane = 0;
        tile.Piece = piece is RcpPiece.InnerCorner ? RcpPiece.Plain : piece;
        tile.Facing = facing;
        tile.Corner = corner;

        if (HasStraight(piece))
        {
            var (c, r, vertical) = RcpPlan.EdgeLine(col, row, facing);

            // Checked before anything changes: RailAcross reads the seam as
            // it is now.
            var across = RailAcross(design, col, row, c, r, vertical);

            if (design.WallAt(c, r, vertical) is { } there)
            {
                // Only the tile across having its rail on this seam makes two
                // back to back - a divider. A rail with no tile under it (one
                // drawn by hand before the floor), or a custom second rail,
                // becomes this tile's own: the user is putting a tile under
                // the one rail that is there, not adding another (10 Nov 2026,
                // bug hunt 16: it was made a divider every time, so the 3D
                // view and the stock count had a rail too many).
                if (across)
                {
                    there.Double = true;
                }

                there.Auto = false;
            }
            else
            {
                design.Walls.Add(new RcpWall { Col = c, Row = r, Vertical = vertical });
            }
        }
        else if (piece == RcpPiece.OuterCurve)
        {
            var turn = TurnFor(col, row, corner);

            // The two straights it stands in place of, on this tile's edges -
            // not a rail bolted to the tile across the seam, which stands on
            // that tile beside the turn (8 Nov 2026: the rail along the top
            // of a T where a divider drops from another went every time a
            // turn under it was laid).
            foreach (var (c, r, vertical) in RcpAutoRails.Swept(turn))
            {
                if (RailAcross(design, col, row, c, r, vertical))
                {
                    continue;
                }

                if (design.WallAt(c, r, vertical) is { } wall)
                {
                    if (wall.Double)
                    {
                        wall.Double = false;
                    }
                    else
                    {
                        design.Walls.Remove(wall);
                        removedWalls.Add(wall);
                    }
                }
            }

            // A turn already round this corner - drawn by hand before the
            // floor, or one the tile under it couldn't carry - becomes this
            // tile's own, as a hand rail does above: a second one on top of
            // it was a turn too many in the stock count and the 3D view, and
            // taking the tile up left one behind (bug hunt 17).
            if (design.Curves.FirstOrDefault(c => c.Col == turn.Col && c.Row == turn.Row && c.Quadrant == turn.Quadrant) is { } there)
            {
                there.Radius = 1f;
                there.Double = false;
                there.Auto = false;
            }
            else
            {
                design.Curves.Add(turn);
            }
        }

        return (removedWalls, removedCurves);
    }

    /// <summary>Whether the single rail on this seam is the tile across's own - a rail tile on the far side, facing the seam.</summary>
    private static bool RailAcross(RcpTrackDesign design, int col, int row, int seamCol, int seamRow, bool vertical)
    {
        if (design.WallAt(seamCol, seamRow, vertical) is not { Double: false })
        {
            return false;
        }

        foreach (var edge in Enum.GetValues<RcpEdge>())
        {
            if (RcpPlan.EdgeLine(col, row, edge) != (seamCol, seamRow, vertical))
            {
                continue;
            }

            var across = RcpPlan.Neighbour(col, row, edge);

            return design.At(across.X, across.Y) is { } tile
                && HasStraight(tile.Piece)
                && RcpPlan.EdgeLine(tile.Col, tile.Row, tile.Facing) == (seamCol, seamRow, vertical);
        }

        return false;
    }

    /// <summary>
    /// Takes up a whole piece: the tile, and every rail bolted to it - its
    /// own rail or turn, and any custom second rail on its edges that would
    /// be left with no tile under it. A divider loses this tile's half and
    /// keeps the other lane's. Returns what it took off the plan.
    /// </summary>
    public static (List<RcpWall> RemovedWalls, List<RcpCurve> RemovedCurves) TakeUp(RcpTrackDesign design, RcpTile tile)
    {
        var removedWalls = new List<RcpWall>();
        var removedCurves = new List<RcpCurve>();

        Unbolt(design, tile, removedWalls, removedCurves);
        design.Tiles.Remove(tile);

        // A custom rail on another edge, with nothing under it now.
        foreach (var edge in Enum.GetValues<RcpEdge>())
        {
            var (c, r, vertical) = RcpPlan.EdgeLine(tile.Col, tile.Row, edge);
            var beyond = RcpPlan.Neighbour(tile.Col, tile.Row, edge);

            if (design.WallAt(c, r, vertical) is { Auto: false } wall && design.At(beyond.X, beyond.Y) is null)
            {
                design.Walls.Remove(wall);
                removedWalls.Add(wall);
            }
        }

        return (removedWalls, removedCurves);
    }

    /// <summary>Takes a tile's own rail or turn off the plan, as the tile last carried it.</summary>
    private static void Unbolt(RcpTrackDesign design, RcpTile tile, List<RcpWall> removedWalls, List<RcpCurve> removedCurves)
    {
        if (HasStraight(tile.Piece))
        {
            var (c, r, vertical) = RcpPlan.EdgeLine(tile.Col, tile.Row, tile.Facing);

            if (design.WallAt(c, r, vertical) is { } wall)
            {
                if (wall.Double)
                {
                    wall.Double = false;
                }
                else
                {
                    design.Walls.Remove(wall);
                    removedWalls.Add(wall);
                }
            }
        }
        else if (tile.Piece == RcpPiece.OuterCurve)
        {
            var turn = TurnFor(tile.Col, tile.Row, tile.Corner);

            if (design.Curves.FirstOrDefault(c => c.Col == turn.Col && c.Row == turn.Row && c.Quadrant == turn.Quadrant) is { } curve)
            {
                design.Curves.Remove(curve);
                removedCurves.Add(curve);
            }
        }

        tile.Piece = RcpPiece.Plain;
    }

    /// <summary>
    /// Rounds each outside corner of the floor with a one-tile turn: where
    /// two hand rails meet round a tile that has no floor beyond either of
    /// them or across its corner, as Rail the edge leaves the floor's
    /// corners. A square outside corner would be two rails on one tile -
    /// custom work - when the outer curve tile is the piece made for it.
    /// Returns how many it rounded.
    /// </summary>
    public static int RoundOutsideCorners(RcpTrackDesign design)
    {
        var rounded = 0;

        bool Floor(int col, int row) => design.At(col, row) is { } t && t.Kind != RcpTileKind.Infield;

        foreach (var tile in design.Tiles.Where(t => t.Kind != RcpTileKind.Infield).ToList())
        {
            foreach (var corner in Enum.GetValues<RcpQuadrant>())
            {
                var (dx, dy) = RcpAutoRails.Step(corner);

                if (Floor(tile.Col + dx, tile.Row) || Floor(tile.Col, tile.Row + dy) || Floor(tile.Col + dx, tile.Row + dy))
                {
                    continue;
                }

                var across = RcpPlan.EdgeLine(tile.Col, tile.Row, dx > 0 ? RcpEdge.East : RcpEdge.West);
                var down = RcpPlan.EdgeLine(tile.Col, tile.Row, dy > 0 ? RcpEdge.South : RcpEdge.North);

                if (design.WallAt(across.Col, across.Row, across.Vertical) is not { Auto: false, Double: false } a
                    || design.WallAt(down.Col, down.Row, down.Vertical) is not { Auto: false, Double: false } b)
                {
                    continue;
                }

                var turn = TurnFor(tile.Col, tile.Row, corner);

                // Not where a turn is already, or where one would cross it.
                if (design.Curves.Any(c => c.Col == turn.Col && c.Row == turn.Row)
                    || design.Curves.SelectMany(c => RcpAutoRails.Swept(c)).Any(s => RcpAutoRails.Swept(turn).Contains(s)))
                {
                    continue;
                }

                design.Walls.Remove(a);
                design.Walls.Remove(b);
                design.Curves.Add(turn);
                rounded++;
            }
        }

        return rounded;
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
