namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): laying an outer curve tile under a turn that
/// is already round that corner takes that turn as the tile's own, rather
/// than adding a second one on top of it.
/// </summary>
public sealed class BugHunt17_RcpPiecesTests
{
    [Fact]
    public void Laying_an_outer_curve_tile_under_a_hand_turn_takes_that_turn_not_a_second()
    {
        // A one-tile turn drawn by hand on bare grid: anchored at grid point
        // (4,4), bulging into cell (4,4).
        var design = new RcpTrackDesign
        {
            Curves = { new RcpCurve { Col = 4, Row = 4, Quadrant = RcpQuadrant.SouthEast, Radius = 1f } },
        };

        Assert.Equal(1, RcpPieces.Adopt(design).TurnsWithNoTile);

        RcpPieces.Lay(design, 4, 4, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthWest);
        var report = RcpPieces.Adopt(design);

        Assert.Single(design.Curves);
        Assert.True(report.AllOnTiles);
        Assert.Equal((RcpPiece.OuterCurve, RcpQuadrant.NorthWest), (design.At(4, 4)!.Piece, design.At(4, 4)!.Corner));

        // And taking the tile up leaves no turn behind.
        RcpPieces.TakeUp(design, design.At(4, 4)!);

        Assert.Empty(design.Curves);
    }

    [Fact]
    public void Laying_an_outer_curve_tile_under_a_turn_that_is_not_a_piece_makes_it_the_tiles_one_tile_turn()
    {
        // A back-to-back two-tile turn at the same point, round the same
        // quarter, on a plain tile.
        var design = new RcpTrackDesign
        {
            Tiles = { new RcpTile { Col = 4, Row = 4 } },
            Curves = { new RcpCurve { Col = 4, Row = 4, Quadrant = RcpQuadrant.SouthEast, Radius = 2f, Double = true, Auto = true } },
        };

        RcpPieces.Lay(design, 4, 4, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthWest);

        var turn = Assert.Single(design.Curves);
        Assert.Equal((1f, false, false), (turn.Radius, turn.Double, turn.Auto));
        Assert.True(RcpPieces.Adopt(design).AllOnTiles);
    }

    [Fact]
    public void An_outer_curve_tile_laid_where_another_turns_round_a_different_quarter_of_the_point_adds_its_own()
    {
        // Where a divider meets the wall two outer curve tiles share one
        // grid point, each round its own quarter.
        var design = new RcpTrackDesign();

        RcpPieces.Lay(design, 0, 1, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.SouthWest);
        RcpPieces.Lay(design, 0, 2, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthWest);

        Assert.Equal(2, design.Curves.Count);
        Assert.True(RcpPieces.Adopt(design).AllOnTiles);
    }
}
