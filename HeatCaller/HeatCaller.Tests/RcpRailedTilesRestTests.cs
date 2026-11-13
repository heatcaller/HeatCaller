namespace HeatCaller.Tests;

/// <summary>
/// The rest of railed tiles (13 Nov 2026, plans\track-designer-railed-
/// tiles-plan.md): a selection takes its tiles' rails with them, stretch
/// repeats whole pieces, the board marks what the pieces can't build, and
/// the outer curve and arched rail tiles are drawn in their own shapes.
/// </summary>
public sealed class RcpRailedTilesRestTests
{
    /// <summary>Two tiles side by side, each laid as the piece given.</summary>
    private static RcpTrackDesign Pair(RcpPiece left, RcpEdge leftFacing, RcpPiece right, RcpEdge rightFacing)
    {
        var design = new RcpTrackDesign { TileSizeMm = 500 };
        RcpPieces.Lay(design, 0, 0, left, leftFacing, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 1, 0, right, rightFacing, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);
        return design;
    }

    private static int RailsOn(RcpTrackDesign design, int col, int row, bool vertical) =>
        design.Walls.Count(w => w.Col == col && w.Row == row && w.Vertical == vertical);

    // --- Phase 3: select, copy, stretch ---------------------------------------

    [Fact]
    public void A_picked_tile_brings_its_rail_and_a_rail_bolted_to_a_tile_outside_stays()
    {
        // The rail on the seam between the two is bolted to the right-hand
        // tile. A box round the left-hand tile has the rail on its edge - the
        // old rule took it, both ends inside - but it isn't that tile's.
        var design = Pair(RcpPiece.Plain, RcpEdge.North, RcpPiece.Rail, RcpEdge.West);
        var wall = design.WallAt(1, 0, true)!;

        var left = RcpSelection.In(design, new Rectangle(0, 0, 1, 1));
        Assert.Single(left.Tiles);
        Assert.False(left.Holds(wall));

        var right = RcpSelection.In(design, new Rectangle(1, 0, 1, 1));
        Assert.True(right.Holds(wall));
    }

    [Fact]
    public void A_rail_no_tile_carries_still_goes_by_where_it_lies()
    {
        // Drawn before the floor under it: both ends in the box, it comes.
        var design = new RcpTrackDesign();
        design.Walls.Add(new RcpWall { Col = 3, Row = 3 });
        RcpPieces.Adopt(design);

        Assert.Single(RcpSelection.In(design, new Rectangle(3, 2, 1, 2)).Walls);
    }

    [Fact]
    public void Half_a_divider_goes_with_its_tile_and_comes_back_a_divider()
    {
        var design = Pair(RcpPiece.Rail, RcpEdge.East, RcpPiece.Rail, RcpEdge.West);
        var divider = design.WallAt(1, 0, true)!;
        Assert.True(divider.Double);

        var picked = RcpSelection.In(design, new Rectangle(1, 0, 1, 1));
        Assert.True(picked.HoldsHalfOf(divider));
        Assert.Contains("1 rail", picked.Describe());

        // Moved two rows down: the left-hand tile keeps its rail, the moved
        // tile takes its own with it.
        picked.Detach(design);
        picked.Move(0, 2);
        picked.Settle(design);
        RcpPieces.Adopt(design);

        Assert.False(design.WallAt(1, 0, true)!.Double);
        Assert.Equal(RcpPiece.Rail, design.At(0, 0)!.Piece);
        Assert.Equal(RcpEdge.East, design.At(0, 0)!.Facing);
        Assert.NotNull(design.WallAt(1, 2, true));
        Assert.Equal(RcpPiece.Rail, design.At(1, 2)!.Piece);
        Assert.Equal(RcpEdge.West, design.At(1, 2)!.Facing);

        // And back: two rail tiles back to back on one seam are a divider
        // again, one rail on the seam - not two.
        picked.Move(0, -2);
        picked.Settle(design);
        RcpPieces.Adopt(design);

        Assert.Equal(1, RailsOn(design, 1, 0, true));
        Assert.True(design.WallAt(1, 0, true)!.Double);
        Assert.True(RcpPieces.Adopt(design).AllOnTiles);
    }

    [Fact]
    public void Half_a_divider_copied_is_one_rail_and_deleted_leaves_the_other_lanes_rail()
    {
        var design = Pair(RcpPiece.Rail, RcpEdge.East, RcpPiece.Rail, RcpEdge.West);
        var picked = RcpSelection.In(design, new Rectangle(1, 0, 1, 1));

        var clip = picked.Copy();
        var rail = Assert.Single(clip.Walls);
        Assert.False(rail.Double);

        picked.DeleteFrom(design);

        var left = Assert.Single(design.Walls);
        Assert.False(left.Double);
        Assert.Single(design.Tiles);
    }

    [Fact]
    public void Both_halves_picked_one_after_the_other_is_the_whole_divider()
    {
        var design = Pair(RcpPiece.Rail, RcpEdge.East, RcpPiece.Rail, RcpEdge.West);
        var divider = design.WallAt(1, 0, true)!;

        var picked = RcpSelection.In(design, new Rectangle(1, 0, 1, 1));
        picked.Add(RcpSelection.In(design, new Rectangle(0, 0, 1, 1)));

        Assert.True(picked.Holds(divider));
        Assert.False(picked.HoldsHalfOf(divider));
    }

    [Fact]
    public void A_turned_selection_keeps_every_rail_on_the_tile_it_was_on()
    {
        // A small bay: three rail tiles and an outer curve tile, picked,
        // turned a quarter and moved off - every piece still whole.
        var design = new RcpTrackDesign();
        RcpPieces.Lay(design, 0, 0, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 1, 0, RcpPiece.Rail, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 0, 1, RcpPiece.Rail, RcpEdge.West, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 1, 1, RcpPiece.Plain, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);

        var picked = RcpSelection.In(design, new Rectangle(0, 0, 2, 2));
        picked.Detach(design);
        picked.TurnClockwise();
        picked.Move(5, 5);
        picked.Settle(design);

        var report = RcpPieces.Adopt(design);

        Assert.True(report.AllOnTiles, report.ToString());
        Assert.Equal(2, design.Tiles.Count(t => t.Piece == RcpPiece.Rail));
        Assert.Single(design.Tiles, t => t.Piece == RcpPiece.OuterCurve);
    }

    /// <summary>Floor four columns by <paramref name="rows"/>.</summary>
    private static RcpTrackDesign Floor(int cols, int rows)
    {
        var design = new RcpTrackDesign();

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row });
            }
        }

        return design;
    }

    [Fact]
    public void A_stretch_beside_a_one_tile_turn_goes_ahead_and_the_turn_stays_whole()
    {
        // The turn is its outer curve tile's, and that tile is on one side
        // of the line: there's nothing to cut (it used to refuse - the
        // turn's end sat on the line).
        var design = Floor(4, 2);
        RcpPieces.Lay(design, 1, 0, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthEast);
        RcpPieces.Adopt(design);

        Assert.Null(RcpStretch.Columns(design, 2, 1));

        Assert.Equal(10, design.Tiles.Count);
        Assert.Single(design.Curves);
        Assert.Equal(RcpPiece.OuterCurve, design.At(1, 0)!.Piece);

        // Its copy is plain floor, not a second turn.
        Assert.Equal(RcpPiece.Plain, design.At(2, 0)!.Piece);
    }

    [Fact]
    public void A_two_tile_turn_across_the_line_still_stops_the_stretch()
    {
        var design = Floor(6, 4);
        design.Curves.Add(new RcpCurve { Col = 1, Row = 3, Quadrant = RcpQuadrant.NorthEast, Radius = 2 });
        RcpPieces.Adopt(design);

        Assert.NotNull(RcpStretch.Columns(design, 2, 1));
    }

    [Fact]
    public void A_row_stretch_repeats_an_arched_tile_with_its_arch_on_the_same_edge()
    {
        // Rows are stretched as columns read across the diagonal - the
        // tile's rail edge has to be read across it too, or an arched tile
        // (kept as laid) came out with its rail on another edge.
        var design = Floor(2, 4);
        RcpPieces.Lay(design, 0, 2, RcpPiece.Arched, RcpEdge.West, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);

        Assert.Null(RcpStretch.Rows(design, 3, 1));

        Assert.Equal(RcpPiece.Arched, design.At(0, 2)!.Piece);
        Assert.Equal(RcpEdge.West, design.At(0, 2)!.Facing);
        Assert.Equal(RcpPiece.Arched, design.At(0, 3)!.Piece);
        Assert.Equal(RcpEdge.West, design.At(0, 3)!.Facing);
        Assert.NotNull(design.WallAt(0, 3, true));
    }

    [Fact]
    public void A_column_stretch_does_not_repeat_a_rail_across_the_track()
    {
        // A rail tile whose rail is ON the line - across the stretch - goes
        // out with the line; its copies are plain floor.
        var design = Floor(4, 1);
        RcpPieces.Lay(design, 1, 0, RcpPiece.Rail, RcpEdge.East, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);

        Assert.Null(RcpStretch.Columns(design, 2, 2));

        Assert.Equal(RcpPiece.Plain, design.At(2, 0)!.Piece);
        Assert.Equal(RcpPiece.Plain, design.At(3, 0)!.Piece);
        Assert.Single(design.Walls, w => w.Vertical);
    }

    // --- The board's report ---------------------------------------------------

    [Fact]
    public void The_report_says_where_each_rail_with_no_tile_and_each_turn_not_a_piece_is()
    {
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        design.Walls.Add(new RcpWall { Col = 5, Row = 5 });
        design.Curves.Add(new RcpCurve { Col = 8, Row = 8, Quadrant = RcpQuadrant.SouthEast, Radius = 2 });

        var report = RcpPieces.Adopt(design);

        Assert.Equal((5, 5, false), Assert.Single(report.RailsWithNoTileAt));
        Assert.Equal(2f, Assert.Single(report.TurnsNotPieces).Radius);
    }

    // --- Phase 4: counted in pieces -------------------------------------------

    [Fact]
    public void A_plan_counted_in_pieces_leaves_the_plan_alone()
    {
        // Count works the pieces out on a copy.
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        design.Walls.Add(new RcpWall { Col = 0, Row = 0 });

        var pieces = RcpPlan.Count(design).Pieces;

        Assert.Equal(1, pieces.Rail);
        Assert.Equal(RcpPiece.Plain, design.Tiles[0].Piece);
    }

    // --- Phase 5: drawn as one piece ------------------------------------------

    [Fact]
    public void The_outer_curve_tile_is_a_quarter_circle_centred_across_from_its_corner()
    {
        var tile = new RcpTile { Col = 3, Row = 2, Piece = RcpPiece.OuterCurve, Corner = RcpQuadrant.NorthEast };
        var disc = RcpTrackPainter.QuarterDisc(tile, 16);

        // Centred on the south-west corner, (3, 3), one tile round.
        Assert.Equal(new PointF(3, 3), disc[0]);
        Assert.All(disc.Skip(1), p => Assert.Equal(1f, MathF.Sqrt((p.X - 3) * (p.X - 3) + (p.Y - 3) * (p.Y - 3)), 3));

        // From one straight edge's end to the other's, through the north-east.
        Assert.Contains(disc, p => Math.Abs(p.X - 4) < 1e-3 && Math.Abs(p.Y - 3) < 1e-3);
        Assert.Contains(disc, p => Math.Abs(p.X - 3) < 1e-3 && Math.Abs(p.Y - 2) < 1e-3);
        Assert.All(disc, p => Assert.True(p.X >= 3 - 1e-3 && p.Y <= 3 + 1e-3));
    }

    [Fact]
    public void The_arched_rail_rises_into_the_lane_from_its_own_edge()
    {
        var south = RcpTrackPainter.ArchOutline(new RcpTile { Col = 0, Row = 0, Piece = RcpPiece.Arched, Facing = RcpEdge.South });

        Assert.All(south, p => Assert.InRange(p.X, -1e-3f, 1.001f));
        Assert.Equal(1f - RcpTrackPainter.RailThickness - RcpTrackPainter.ArchRise, south.Min(p => p.Y), 3);
        Assert.Equal(1f, south.Max(p => p.Y), 3);

        // Turned to the west edge, it rises east from x = 0.
        var west = RcpTrackPainter.ArchOutline(new RcpTile { Col = 0, Row = 0, Piece = RcpPiece.Arched, Facing = RcpEdge.West });

        Assert.Equal(0f, west.Min(p => p.X), 3);
        Assert.Equal(RcpTrackPainter.RailThickness + RcpTrackPainter.ArchRise, west.Max(p => p.X), 3);
    }

    [Fact]
    public void The_corner_beyond_an_outer_curve_tiles_arc_is_bare_room()
    {
        var design = new RcpTrackDesign();
        RcpPieces.Lay(design, 0, 0, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthWest);

        using var picture = RcpTrackPainter.RenderRegion(design, new RectangleF(0, 0, 1, 1), 50, Color.White, opaque: true);

        // The north-west corner, past the arc: the background.
        var corner = picture.GetPixel(2, 2);
        Assert.True(corner.R > 240 && corner.G > 240 && corner.B > 240, corner.ToString());

        // Well inside the disc: foam.
        var inside = picture.GetPixel(40, 40);
        Assert.True(inside.R < 120, inside.ToString());
    }

    [Fact]
    public void The_angled_view_raises_arched_and_curve_tiles_without_trouble()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500 };
        RcpPieces.Lay(design, 0, 0, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 1, 0, RcpPiece.Arched, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 2, 0, RcpPiece.Rail, RcpEdge.North, RcpQuadrant.NorthWest);

        using var bitmap = new Bitmap(400, 300);
        using var g = Graphics.FromImage(bitmap);

        RcpAngledView.Draw(g, design, bitmap.Size, RcpAngle.Start);
    }
}
