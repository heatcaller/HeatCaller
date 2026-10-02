namespace HeatCaller.Tests;

/// <summary>
/// The Track Designer's plan model: Trace route copes with a plan that has
/// two tiles in one cell, and the plan's extent takes in the markings and
/// the racing line as well as the floor and rails (2 Oct 2026).
/// </summary>
public sealed class BugHuntTrackDesignerTests
{
    [Fact]
    public void AutoRoute_does_not_throw_on_two_tiles_in_one_cell()
    {
        // Three tiles in a row with no rails, and the middle one twice - as
        // a restored or hand-edited track-layouts.json can hold.
        var design = new RcpTrackDesign
        {
            Tiles =
            {
                new RcpTile { Col = 0, Row = 0 },
                new RcpTile { Col = 1, Row = 0 },
                new RcpTile { Col = 1, Row = 0 },
                new RcpTile { Col = 2, Row = 0 },
            },
        };

        var route = RcpPlan.AutoRoute(design, out var problem);

        // A straight strip dead-ends, so the walk reports that - but it gets
        // there rather than throwing on the duplicate.
        Assert.NotNull(route);
        Assert.Equal(3, route!.Count);
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void BoundsInTiles_takes_in_a_line_past_the_floor()
    {
        var design = new RcpTrackDesign
        {
            Tiles = { new RcpTile { Col = 0, Row = 0 } },
            Route =
            {
                new RcpRoutePoint { X = 0.5f, Y = 0.5f },
                new RcpRoutePoint { X = 4f, Y = 3f },
            },
        };

        var bounds = design.BoundsInTiles();

        Assert.Equal(4f, bounds.Right);
        Assert.Equal(3f, bounds.Bottom);

        // Left out when the picture leaves the line out.
        var withoutLine = design.BoundsInTiles(includeRoute: false);

        Assert.Equal(1f, withoutLine.Right);
        Assert.Equal(1f, withoutLine.Bottom);
    }

    [Fact]
    public void BoundsInTiles_takes_in_markings_past_the_floor()
    {
        var design = new RcpTrackDesign
        {
            Tiles = { new RcpTile { Col = 0, Row = 0 } },
            Decals = { new RcpDecal { Col = -2, Row = 0 } },
        };

        var bounds = design.BoundsInTiles();

        Assert.Equal(-2f, bounds.Left);
        Assert.Equal(1f, bounds.Right);
    }
}
