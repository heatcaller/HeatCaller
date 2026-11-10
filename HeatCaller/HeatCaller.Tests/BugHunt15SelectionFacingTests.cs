namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (B25): a finish or arched tile keeps its Facing as laid -
/// Adopt never works it out again - so turning or mirroring one has to
/// carry its Facing round with the rail, or the tile says its rail is on
/// an edge that now holds some other rail, and taking it up unbolts that.
/// </summary>
public sealed class BugHunt15SelectionFacingTests
{
    /// <summary>A finish tile with its rail on the north seam, over a plain tile with a rail on the south.</summary>
    private static (RcpTrackDesign Design, RcpTile Finish, RcpWall FinishRail) TwoTiles()
    {
        var finish = new RcpTile { Col = 0, Row = 0, Piece = RcpPiece.Finish, Facing = RcpEdge.North };
        var finishRail = new RcpWall { Col = 0, Row = 0 };
        var design = new RcpTrackDesign
        {
            Tiles = { finish, new RcpTile { Col = 0, Row = 1 } },
            Walls = { finishRail, new RcpWall { Col = 0, Row = 2 } },
        };

        return (design, finish, finishRail);
    }

    [Theory]
    [InlineData("half turn")]
    [InlineData("quarter turn")]
    [InlineData("left-right")]
    [InlineData("top-bottom")]
    public void A_finish_tiles_rail_stays_on_the_edge_it_says_after_a_turn_or_mirror(string how)
    {
        var (design, finish, finishRail) = TwoTiles();
        Assert.True(RcpPieces.Adopt(design).AllOnTiles);

        var all = RcpSelection.Everything(design);

        switch (how)
        {
            case "half turn":
                all.TurnClockwise();
                all.TurnClockwise();
                break;
            case "quarter turn":
                all.TurnClockwise();
                break;
            case "left-right":
                // Turned first, so the rail is on an east or west edge.
                all.TurnClockwise();
                all.MirrorLeftRight();
                break;
            default:
                all.MirrorTopBottom();
                break;
        }

        Assert.Equal(
            (finishRail.Col, finishRail.Row, finishRail.Vertical),
            RcpPlan.EdgeLine(finish.Col, finish.Row, finish.Facing));

        var report = RcpPieces.Adopt(design);

        Assert.True(report.AllOnTiles, report.ToString());
        Assert.Equal(0, report.SecondRails);
        Assert.Equal(RcpPiece.Finish, finish.Piece);
    }

    [Fact]
    public void Taking_up_a_turned_finish_tile_takes_its_own_rail()
    {
        var (design, finish, finishRail) = TwoTiles();

        var all = RcpSelection.Everything(design);
        all.TurnClockwise();
        all.TurnClockwise();

        var (removed, _) = RcpPieces.TakeUp(design, finish);

        Assert.Contains(finishRail, removed);
        Assert.DoesNotContain(finishRail, design.Walls);
        Assert.Single(design.Walls);
    }

    [Fact]
    public void A_tiles_corner_goes_round_with_it()
    {
        var tile = new RcpTile { Col = 0, Row = 0, Piece = RcpPiece.Finish, Facing = RcpEdge.West, Corner = RcpQuadrant.NorthWest };
        var design = new RcpTrackDesign { Tiles = { tile } };
        var all = RcpSelection.Everything(design);

        all.TurnClockwise();
        Assert.Equal((RcpEdge.North, RcpQuadrant.NorthEast), (tile.Facing, tile.Corner));

        all.MirrorLeftRight();
        Assert.Equal((RcpEdge.North, RcpQuadrant.NorthWest), (tile.Facing, tile.Corner));

        all.MirrorTopBottom();
        Assert.Equal((RcpEdge.South, RcpQuadrant.SouthWest), (tile.Facing, tile.Corner));
    }
}
