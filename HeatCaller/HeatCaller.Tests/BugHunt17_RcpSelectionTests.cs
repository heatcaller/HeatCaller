namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): a turn landing on a grid point only clears
/// another turn round the SAME quarter of it. Two outer curve tiles that
/// meet at a corner each have their turn on that one point.
/// </summary>
public sealed class BugHunt17_RcpSelectionTests
{
    /// <summary>A divider meeting the side wall, as RCP's serpentine has it: tiles (0,1) and (0,2), both turns on point (0,2).</summary>
    private static RcpTrackDesign WallTee()
    {
        var design = new RcpTrackDesign();

        RcpPieces.Lay(design, 0, 1, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.SouthWest);
        RcpPieces.Lay(design, 0, 2, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);

        return design;
    }

    [Fact]
    public void Moving_one_tee_tile_away_and_back_leaves_its_neighbours_turn_alone()
    {
        var design = WallTee();
        var neighbours = design.Curves.Single(c => c.Quadrant == RcpQuadrant.SouthEast);

        var picked = RcpSelection.In(design, new Rectangle(0, 1, 1, 1));

        // The tile and its own turn - not the neighbour's.
        Assert.Equal(2, picked.Pieces);

        picked.Detach(design);
        picked.Move(1, 0);
        picked.Move(-1, 0);
        picked.Settle(design);

        Assert.Equal(2, design.Curves.Count);
        Assert.Contains(neighbours, design.Curves);
        Assert.True(RcpPieces.Adopt(design).AllOnTiles);
        Assert.Equal(RcpPiece.OuterCurve, design.At(0, 2)!.Piece);
    }

    [Fact]
    public void A_turn_landing_on_another_round_the_same_quarter_still_takes_its_place()
    {
        var design = WallTee();

        var picked = RcpSelection.In(design, new Rectangle(0, 1, 1, 1));

        // A hand turn exactly where the picked tile's own will land.
        var under = new RcpCurve { Col = 1, Row = 2, Quadrant = RcpQuadrant.NorthEast, Radius = 1f };
        design.Curves.Add(under);

        picked.Detach(design);
        picked.Move(1, 0);
        picked.Settle(design);

        Assert.DoesNotContain(under, design.Curves);
        Assert.Equal(2, design.Curves.Count);
    }
}
