namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026): laying a rail tile under a rail that has no
/// tile, or a custom second rail, no longer makes a false divider; and a
/// rail with no floor on either side is counted as a rail with no tile, not
/// a second rail on tile (0,0).
/// </summary>
public sealed class BugHunt16_RcpPiecesTests
{
    [Fact]
    public void Laying_a_rail_tile_under_a_hand_rail_with_no_tile_takes_that_rail_not_a_divider()
    {
        // A hand rail drawn on the seam south of (0,0) before any floor.
        var design = new RcpTrackDesign
        {
            Walls = { new RcpWall { Col = 0, Row = 1, Vertical = false } },
        };

        Assert.Equal(1, RcpPieces.Adopt(design).RailsWithNoTile);

        RcpPieces.Lay(design, 0, 0, RcpPiece.Rail, RcpEdge.South, RcpQuadrant.NorthWest);
        var report = RcpPieces.Adopt(design);

        var wall = Assert.Single(design.Walls);
        Assert.False(wall.Double);
        Assert.True(report.AllOnTiles);
        Assert.Equal((RcpPiece.Rail, RcpEdge.South), (design.At(0, 0)!.Piece, design.At(0, 0)!.Facing));
    }

    [Fact]
    public void Re_laying_a_custom_tile_as_a_rail_facing_its_second_rail_makes_no_divider()
    {
        // (0,0) carries its rail on the north edge and a custom second rail
        // on the east edge, with no floor beyond.
        var design = new RcpTrackDesign();
        RcpPieces.Lay(design, 0, 0, RcpPiece.Rail, RcpEdge.North, RcpQuadrant.NorthWest);
        design.Walls.Add(new RcpWall { Col = 1, Row = 0, Vertical = true });
        Assert.Equal(new[] { (0, 0) }, RcpPieces.Adopt(design).CustomTiles);

        RcpPieces.Lay(design, 0, 0, RcpPiece.Rail, RcpEdge.East, RcpQuadrant.NorthWest);
        var report = RcpPieces.Adopt(design);

        var wall = Assert.Single(design.Walls);
        Assert.Equal((1, 0, true, false), (wall.Col, wall.Row, wall.Vertical, wall.Double));
        Assert.True(report.AllOnTiles);
    }

    [Fact]
    public void A_rail_tile_laid_facing_the_rail_tile_across_is_still_a_divider()
    {
        var design = new RcpTrackDesign();

        RcpPieces.Lay(design, 0, 0, RcpPiece.Rail, RcpEdge.South, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);
        RcpPieces.Lay(design, 0, 1, RcpPiece.Rail, RcpEdge.North, RcpQuadrant.NorthWest);

        Assert.True(Assert.Single(design.Walls).Double);
    }

    [Fact]
    public void A_rail_with_no_floor_either_side_is_a_rail_with_no_tile_not_a_custom_tile_at_the_origin()
    {
        // Floor at (0,0) as plans usually have; a rail between two blocked-out
        // cells well away from it.
        var design = new RcpTrackDesign
        {
            Tiles =
            {
                new RcpTile { Col = 0, Row = 0 },
                new RcpTile { Col = 5, Row = 5, Kind = RcpTileKind.Infield },
                new RcpTile { Col = 5, Row = 6, Kind = RcpTileKind.Infield },
            },
            Walls = { new RcpWall { Col = 5, Row = 6, Vertical = false } },
        };

        var report = RcpPieces.Adopt(design);

        Assert.Empty(report.CustomTiles);
        Assert.Equal(0, report.SecondRails);
        Assert.Equal(1, report.RailsWithNoTile);
        Assert.Equal(RcpPiece.Plain, design.At(0, 0)!.Piece);
    }
}
