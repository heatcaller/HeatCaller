namespace HeatCaller.Tests;

/// <summary>
/// Trace route on lanes more than a tile wide, and through turn rails
/// (Track Designer overhaul, Phase 3, 25 Oct 2026). A one-tile lane is
/// still walked exactly as it always was.
/// </summary>
public sealed class RcpWideRouteTests
{
    private static RcpTrackDesign RingLane(int right, int bottom, int width, int turns = 1, bool floorEverywhere = false)
    {
        var design = new RcpTrackDesign { LaneTurnRadius = turns };

        if (floorEverywhere)
        {
            for (var col = -2; col <= right + 2; col++)
            {
                for (var row = -2; row <= bottom + 2; row++)
                {
                    design.Tiles.Add(new RcpTile { Col = col, Row = row });
                }
            }
        }

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(right, bottom), width)!)
        {
            if (design.At(cell.X, cell.Y) is { } tile)
            {
                tile.Lane = 1;
            }
            else
            {
                design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Lane = 1 });
            }
        }

        RcpAutoRails.Apply(design);

        return design;
    }

    [Fact]
    public void A_two_tile_lane_is_traced_down_the_seam_in_its_middle()
    {
        // 12 x 10, two wide: the middle runs one tile in from the outside.
        var design = RingLane(11, 9, width: 2, turns: 2);

        var route = RcpPlan.AutoRoute(design, out var problem);

        Assert.NotNull(route);
        Assert.Equal(string.Empty, problem);

        // Round the box from (1,1) to (11,9): 2 x (10 + 8) grid points.
        Assert.Equal(36, route!.Count);
        Assert.All(route, p => Assert.True(p.X == 1 || p.X == 11 || p.Y == 1 || p.Y == 9, $"{p} is off the middle"));
        Assert.Equal(36f, RcpPlan.RouteLengthInTiles(new RcpTrackDesign
        {
            Route = route.Select(p => new RcpRoutePoint { X = p.X, Y = p.Y }).ToList(),
        }), 3);
    }

    [Fact]
    public void A_three_tile_lane_is_traced_through_its_middle_tiles()
    {
        var design = RingLane(12, 10, width: 3, turns: 2);

        var route = RcpPlan.AutoRoute(design, out var problem);

        Assert.NotNull(route);
        Assert.Equal(string.Empty, problem);

        // The middle tiles: the ring of cells from (1,1) to (11,9).
        Assert.Equal(2 * 11 + 2 * 7, route!.Count);
        Assert.All(route, p => Assert.True(p.X == 1.5f || p.X == 11.5f || p.Y == 1.5f || p.Y == 9.5f, $"{p} is off the middle"));
    }

    [Fact]
    public void A_wide_lane_laid_over_a_whole_floor_still_traces()
    {
        // The floor down first, the lane painted on it - the way the
        // designer says to work. The floor outside and in the island is
        // walled off by the lane's rails.
        var design = RingLane(11, 9, width: 2, turns: 1, floorEverywhere: true);

        var route = RcpPlan.AutoRoute(design, out var problem);

        Assert.NotNull(route);
        Assert.Equal(string.Empty, problem);
        Assert.Equal(36, route!.Count);
    }

    [Fact]
    public void It_starts_at_the_finish_line()
    {
        var design = RingLane(11, 9, width: 2);
        design.Decals.Add(new RcpDecal { Col = 0, Row = 4, Kind = RcpDecalKind.FinishLine });

        var route = RcpPlan.AutoRoute(design, out _);

        Assert.NotNull(route);

        // The finish line's tile is (0,4); the nearest point of the middle
        // line is (1,4) or (1,5), and the top one is taken.
        Assert.Equal(new PointF(1f, 4f), route![0]);
    }

    [Fact]
    public void A_one_tile_lane_gives_exactly_the_line_a_railed_corridor_does()
    {
        // The same ring, once as a lane and once railed by hand round bare
        // floor under it - Trace route has always walked the second.
        var lane = RingLane(9, 7, width: 1, turns: 1);
        lane.Decals.Add(new RcpDecal { Col = 0, Row = 3, Kind = RcpDecalKind.FinishLine });

        var byHand = new RcpTrackDesign();

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(9, 7), 1)!)
        {
            byHand.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y });
        }

        RcpPlan.WallThePerimeter(byHand);
        byHand.Decals.Add(new RcpDecal { Col = 0, Row = 3, Kind = RcpDecalKind.FinishLine });

        var fromLane = RcpPlan.AutoRoute(lane, out var laneProblem);
        var fromHand = RcpPlan.AutoRoute(byHand, out var handProblem);

        Assert.NotNull(fromLane);
        Assert.Equal(handProblem, laneProblem);
        Assert.Equal(fromHand!, fromLane!);
        Assert.Equal(32, fromLane!.Count);
    }

    [Fact]
    public void A_turn_rail_keeps_the_line_in_with_floor_beyond_it()
    {
        // A one-tile corridor railed by hand with turn rails at its outside
        // corners, on a floor that runs on past them. The turn rails take
        // the place of the straights at each corner, and the walk used to
        // step out through the gap.
        var design = new RcpTrackDesign();

        for (var col = -1; col <= 6; col++)
        {
            for (var row = -1; row <= 5; row++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row });
            }
        }

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(5, 4), 1)!)
        {
            design.At(cell.X, cell.Y)!.Lane = 1;
        }

        RcpAutoRails.Apply(design);

        // Now made hand rails, so it is the corridor, not the lane, that
        // is being walked.
        foreach (var tile in design.Tiles)
        {
            tile.Lane = 0;
        }

        foreach (var wall in design.Walls)
        {
            wall.Auto = false;
        }

        foreach (var curve in design.Curves)
        {
            curve.Auto = false;
        }

        Assert.Equal(4, design.Curves.Count);

        // Somewhere in the corridor to start: without a lane to go by, the
        // walk starts at the finish line, or the first tile of the floor.
        design.Decals.Add(new RcpDecal { Col = 0, Row = 2, Kind = RcpDecalKind.FinishLine });

        var route = RcpPlan.AutoRoute(design, out var problem);

        Assert.Equal(string.Empty, problem);
        Assert.Equal(2 * 6 + 2 * 3, route!.Count);
    }

    [Fact]
    public void A_lane_that_changes_width_is_refused_rather_than_guessed()
    {
        var design = RingLane(11, 9, width: 1);

        // A stretch of the top straight made two wide.
        for (var col = 3; col <= 7; col++)
        {
            design.Tiles.Add(new RcpTile { Col = col, Row = 1, Lane = 1 });
        }

        RcpAutoRails.Apply(design);

        var route = RcpPlan.AutoRoute(design, out var problem);

        Assert.Null(route);
        Assert.Contains("open on three sides", problem);
    }

    [Fact]
    public void An_open_floor_with_no_rails_is_still_refused()
    {
        var design = new RcpTrackDesign();

        for (var col = 0; col < 8; col++)
        {
            for (var row = 0; row < 6; row++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row });
            }
        }

        var route = RcpPlan.AutoRoute(design, out var problem);

        Assert.Null(route);
        Assert.Contains("open on three sides", problem);
    }

    [Fact]
    public void A_solid_walled_floor_is_still_refused_not_traced_round_its_middle()
    {
        foreach (var size in new[] { 5, 6, 7 })
        {
            var design = new RcpTrackDesign();

            for (var col = 0; col < size; col++)
            {
                for (var row = 0; row < size; row++)
                {
                    design.Tiles.Add(new RcpTile { Col = col, Row = row });
                }
            }

            RcpPlan.WallThePerimeter(design);

            Assert.Null(RcpPlan.AutoRoute(design, out var problem));
            Assert.Contains("open on three sides", problem);
        }
    }

    [Fact]
    public void A_half_tile_hairpin_leaves_its_seam_open_as_before()
    {
        var curve = new RcpCurve { Col = 3, Row = 3, Quadrant = RcpQuadrant.SouthEast, Radius = 0.5f };
        var design = new RcpTrackDesign { Curves = { curve } };

        Assert.Empty(RcpPlan.BlockedSeams(design));
    }
}
