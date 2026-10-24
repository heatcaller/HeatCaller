namespace HeatCaller.Tests;

/// <summary>
/// Trace route on a plan laid the way the designer says to lay it: the
/// whole floor first, then rails standing on it round an island. The floor
/// inside the island can't be reached, so it mustn't stop the route
/// (23 Oct 2026, bug hunt 10).
/// </summary>
public sealed class BugHunt10_RcpTrackModelsTests
{
    private static RcpTrackDesign RingRoundAFlooredIsland()
    {
        var design = new RcpTrackDesign();

        // A 6 x 5 floor, every cell tiled.
        for (var col = 0; col <= 5; col++)
        {
            for (var row = 0; row <= 4; row++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row });
            }
        }

        RcpPlan.WallThePerimeter(design);

        // Rails round the island covering cols 1-4, rows 1-3, leaving a
        // one-tile corridor all the way round.
        for (var col = 1; col <= 4; col++)
        {
            design.Walls.Add(new RcpWall { Col = col, Row = 1, Vertical = false });
            design.Walls.Add(new RcpWall { Col = col, Row = 4, Vertical = false });
        }

        for (var row = 1; row <= 3; row++)
        {
            design.Walls.Add(new RcpWall { Col = 1, Row = row, Vertical = true });
            design.Walls.Add(new RcpWall { Col = 5, Row = row, Vertical = true });
        }

        design.Decals.Add(new RcpDecal { Col = 0, Row = 2, Kind = RcpDecalKind.FinishLine });

        return design;
    }

    [Fact]
    public void AutoRoute_ignores_floor_walled_off_inside_an_island()
    {
        var design = RingRoundAFlooredIsland();

        var route = RcpPlan.AutoRoute(design, out var problem);

        // The ring is 6 + 6 + 3 + 3 = 18 cells, and the walk closes on it.
        Assert.NotNull(route);
        Assert.Equal(18, route!.Count);
        Assert.Equal(string.Empty, problem);
        Assert.Equal(new PointF(0.5f, 2.5f), route[0]);
    }

    [Fact]
    public void AutoRoute_still_refuses_a_corridor_that_opens_out()
    {
        var design = RingRoundAFlooredIsland();

        // Take one island rail away: the corridor now opens into the
        // island floor, so there is more than one way round.
        design.Walls.RemoveAll(w => w.Col == 1 && w.Row == 2 && w.Vertical);

        var route = RcpPlan.AutoRoute(design, out var problem);

        Assert.Null(route);
        Assert.Contains("open on three sides", problem);
    }
}
