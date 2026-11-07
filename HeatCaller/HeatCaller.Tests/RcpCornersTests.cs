namespace HeatCaller.Tests;

/// <summary>
/// The Corners tool's rule (Track Designer overhaul, Phase 3, 25 Oct 2026):
/// a one-tile turn, square, and round again. It went one tile, two tiles,
/// square until railed tiles, Phase 2 (7 Nov 2026): the club has only the
/// one-tile turn.
/// </summary>
public sealed class RcpCornersTests
{
    private static RcpTrackDesign RingLane(int turns = 1)
    {
        var design = new RcpTrackDesign { LaneTurnRadius = turns };

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(9, 7), 1)!)
        {
            design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Lane = 1 });
        }

        RcpAutoRails.Apply(design);
        return design;
    }

    private static RcpCorners.Step Click(RcpTrackDesign design, Point at)
    {
        var step = RcpCorners.Next(design, at, out var why);

        Assert.True(step is not null, why);
        RcpCorners.Apply(design, at, step!);
        RcpAutoRails.Apply(design);

        return step!;
    }

    [Fact]
    public void A_lanes_turn_goes_square_and_back_to_a_one_tile_turn()
    {
        // No two-tile step (railed tiles, Phase 2, 7 Nov 2026): the one-tile
        // outer curve tile is the only turn a club has.
        var design = RingLane();
        var corner = new Point(0, 0);

        Assert.Equal(1f, design.CurveAt(0, 0)!.Radius);

        Assert.Equal(0, Click(design, corner).Radius);
        Assert.Null(design.CurveAt(0, 0));

        // Square, by hand: the straights are back and stay back.
        foreach (var (col, row, vertical) in new[] { (0, 0, false), (0, 0, true) })
        {
            var wall = design.WallAt(col, row, vertical);

            Assert.NotNull(wall);
            Assert.False(wall!.Auto);
        }

        Assert.Equal(1, Click(design, corner).Radius);
        Assert.Equal(1f, design.CurveAt(0, 0)!.Radius);
        Assert.Null(design.WallAt(0, 0, false));
        Assert.Null(design.WallAt(0, 0, true));

        Assert.Empty(RcpPlan.OpenEnds(design));
    }

    [Fact]
    public void A_two_tile_turn_from_an_old_plan_goes_square_on_a_click()
    {
        // Kept as it was when the plan opens (railed tiles, Phase 1); the
        // Corners tool then takes it to square, and from there to the
        // one-tile turn, like any other.
        var design = RingLane(turns: 2);
        var corner = new Point(0, 0);

        Assert.Equal(2f, design.CurveAt(0, 0)!.Radius);
        Assert.Equal(0, Click(design, corner).Radius);
        Assert.Equal(1, Click(design, corner).Radius);
    }

    [Fact]
    public void A_square_corner_of_hand_rails_becomes_a_turn()
    {
        var design = new RcpTrackDesign();
        design.Walls.Add(new RcpWall { Col = 3, Row = 3 });
        design.Walls.Add(new RcpWall { Col = 4, Row = 3 });
        design.Walls.Add(new RcpWall { Col = 3, Row = 3, Vertical = true });
        design.Walls.Add(new RcpWall { Col = 3, Row = 4, Vertical = true });

        Click(design, new Point(3, 3));

        var turn = Assert.Single(design.Curves);

        Assert.Equal(RcpQuadrant.SouthEast, turn.Quadrant);
        Assert.Equal(1f, turn.Radius);
        Assert.Equal(2, design.Walls.Count);
    }

    [Fact]
    public void Somewhere_that_is_not_a_corner_is_said_so()
    {
        var design = new RcpTrackDesign();
        design.Walls.Add(new RcpWall { Col = 3, Row = 3 });
        design.Walls.Add(new RcpWall { Col = 4, Row = 3 });

        Assert.Null(RcpCorners.Next(design, new Point(4, 3), out var why));
        Assert.Contains("isn't a corner", why);
    }

    [Fact]
    public void A_half_tile_hairpin_goes_up_to_a_whole_tile()
    {
        var design = new RcpTrackDesign();
        design.Walls.Add(new RcpWall { Col = 4, Row = 3 });
        design.Walls.Add(new RcpWall { Col = 3, Row = 4, Vertical = true });
        design.Curves.Add(new RcpCurve { Col = 3, Row = 3, Quadrant = RcpQuadrant.SouthEast, Radius = 0.5f });

        var step = RcpCorners.Next(design, new Point(3, 3), out _);

        Assert.NotNull(step);
        Assert.Equal(1, step!.Radius);
    }
}
