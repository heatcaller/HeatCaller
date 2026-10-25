namespace HeatCaller.Tests;

/// <summary>
/// The rails a lane puts down by itself (Track Designer overhaul, Phase 3,
/// 25 Oct 2026): RcpAutoRails, on its own, with no window.
/// </summary>
public sealed class RcpAutoRailsTests
{
    /// <summary>A plan with one lane laid through the given cells.</summary>
    private static RcpTrackDesign Lane(IEnumerable<(int Col, int Row)> cells, int lane = 1, int turns = 1)
    {
        var design = new RcpTrackDesign { LaneTurnRadius = turns };
        AddLane(design, cells, lane);
        return design;
    }

    private static void AddLane(RcpTrackDesign design, IEnumerable<(int Col, int Row)> cells, int lane)
    {
        foreach (var (col, row) in cells)
        {
            design.Tiles.Add(new RcpTile { Col = col, Row = row, Lane = lane });
        }
    }

    private static IEnumerable<(int, int)> Row(int row, int fromCol, int toCol) =>
        Enumerable.Range(fromCol, toCol - fromCol + 1).Select(col => (col, row));

    private static IEnumerable<(int, int)> Column(int col, int fromRow, int toRow) =>
        Enumerable.Range(fromRow, toRow - fromRow + 1).Select(row => (col, row));

    private static IEnumerable<(int, int)> RingCells(int left, int top, int right, int bottom, int width) =>
        RcpAutoRails.Ring(new Point(left, top), new Point(right, bottom), width)!.Select(p => (p.X, p.Y));

    private static bool HasWall(RcpTrackDesign design, int col, int row, bool vertical) =>
        design.WallAt(col, row, vertical) is not null;

    [Fact]
    public void A_straight_lane_gets_a_rail_down_each_side_and_across_each_end()
    {
        var design = Lane(Row(0, 0, 5));

        var outcome = RcpAutoRails.Apply(design);

        Assert.True(outcome.Changed);

        // Six along the top, six along the bottom, one across each end.
        Assert.Equal(14, design.Walls.Count);
        Assert.All(design.Walls, wall => Assert.True(wall.Auto));
        Assert.All(design.Walls, wall => Assert.False(wall.Double));

        for (var col = 0; col <= 5; col++)
        {
            Assert.True(HasWall(design, col, 0, vertical: false));
            Assert.True(HasWall(design, col, 1, vertical: false));
        }

        Assert.True(HasWall(design, 0, 0, vertical: true));
        Assert.True(HasWall(design, 6, 0, vertical: true));

        // A one-tile lane's dead end has no room for a turn: square.
        Assert.Empty(design.Curves);
    }

    [Fact]
    public void An_L_gets_a_turn_on_the_outside_of_the_bend_and_a_square_inside()
    {
        // Along the top and down the right.
        var design = Lane(Row(0, 0, 4).Concat(Column(4, 1, 4)));

        RcpAutoRails.Apply(design);

        var turn = Assert.Single(design.Curves);

        Assert.Equal(new Point(5, 0), turn.At);
        Assert.Equal(RcpQuadrant.SouthWest, turn.Quadrant);
        Assert.Equal(1f, turn.Radius);
        Assert.True(turn.Auto);

        // The straights it takes the place of are not there.
        Assert.False(HasWall(design, 4, 0, vertical: false));
        Assert.False(HasWall(design, 5, 0, vertical: true));

        // The inside of the bend: the two straights meeting at (4,1).
        Assert.True(HasWall(design, 3, 1, vertical: false));
        Assert.True(HasWall(design, 4, 1, vertical: true));

        // Nothing left open where the rails meet the turn.
        Assert.Empty(RcpPlan.OpenEnds(design));
    }

    [Fact]
    public void A_U_gets_a_turn_at_both_outside_corners()
    {
        var design = Lane(Row(0, 0, 4).Concat(Column(4, 1, 3)).Concat(Row(3, 0, 3)));

        RcpAutoRails.Apply(design);

        Assert.Equal(2, design.Curves.Count);
        Assert.Contains(design.Curves, c => c.At == new Point(5, 0) && c.Quadrant == RcpQuadrant.SouthWest);
        Assert.Contains(design.Curves, c => c.At == new Point(5, 4) && c.Quadrant == RcpQuadrant.NorthWest);
    }

    [Fact]
    public void A_ring_gets_a_turn_at_each_outside_corner_at_the_plans_radius()
    {
        var design = Lane(RingCells(0, 0, 9, 7, width: 1), turns: 2);

        RcpAutoRails.Apply(design);

        Assert.Equal(4, design.Curves.Count);
        Assert.All(design.Curves, c => Assert.Equal(2f, c.Radius));

        Assert.Contains(design.Curves, c => c.At == new Point(0, 0) && c.Quadrant == RcpQuadrant.SouthEast);
        Assert.Contains(design.Curves, c => c.At == new Point(10, 0) && c.Quadrant == RcpQuadrant.SouthWest);
        Assert.Contains(design.Curves, c => c.At == new Point(10, 8) && c.Quadrant == RcpQuadrant.NorthWest);
        Assert.Contains(design.Curves, c => c.At == new Point(0, 8) && c.Quadrant == RcpQuadrant.NorthEast);

        // A two-tile turn sweeps across two seams each way.
        Assert.False(HasWall(design, 0, 0, vertical: false));
        Assert.False(HasWall(design, 1, 0, vertical: false));
        Assert.True(HasWall(design, 2, 0, vertical: false));

        Assert.Empty(RcpPlan.OpenEnds(design));
    }

    [Fact]
    public void Square_turns_leave_every_corner_square()
    {
        var design = Lane(RingCells(0, 0, 9, 7, width: 1), turns: 0);

        RcpAutoRails.Apply(design);

        Assert.Empty(design.Curves);
        Assert.True(HasWall(design, 0, 0, vertical: false));
        Assert.True(HasWall(design, 0, 0, vertical: true));
    }

    [Fact]
    public void Two_lanes_side_by_side_share_a_divider()
    {
        var design = Lane(Row(0, 0, 5), lane: 1);
        AddLane(design, Row(1, 0, 5), lane: 2);

        RcpAutoRails.Apply(design);

        for (var col = 0; col <= 5; col++)
        {
            var between = design.WallAt(col, 1, vertical: false);

            Assert.NotNull(between);
            Assert.True(between!.Double);
        }

        // The outside edges are single rails, and the shared seam is one
        // piece, not one from each lane.
        Assert.False(design.WallAt(0, 0, vertical: false)!.Double);
        Assert.False(design.WallAt(0, 2, vertical: false)!.Double);
        Assert.Equal(6, design.Walls.Count(w => w.Row == 1 && !w.Vertical));
    }

    [Fact]
    public void The_same_lane_drawn_twice_over_is_still_one_lane()
    {
        // Two strokes that met: tiles of the same lane either side of a
        // seam are open track, not a divider.
        var design = Lane(Row(0, 0, 5));
        AddLane(design, Row(1, 0, 5), lane: 1);

        RcpAutoRails.Apply(design);

        for (var col = 0; col <= 5; col++)
        {
            Assert.False(HasWall(design, col, 1, vertical: false));
        }
    }

    [Fact]
    public void A_two_tile_lane_has_a_rounded_end()
    {
        var design = Lane(Row(0, 0, 7).Concat(Row(1, 0, 7)), turns: 2);

        RcpAutoRails.Apply(design);

        // Two tiles across the end: room for a one-tile turn at each of
        // its corners, which meet in the middle of it.
        Assert.Contains(design.Curves, c => c.At == new Point(0, 0) && c.Radius == 1f);
        Assert.Contains(design.Curves, c => c.At == new Point(0, 2) && c.Radius == 1f);
        Assert.False(HasWall(design, 0, 0, vertical: true));
        Assert.False(HasWall(design, 0, 1, vertical: true));

        Assert.Empty(RcpPlan.OpenEnds(design));
    }

    [Fact]
    public void Hand_rails_are_never_touched()
    {
        var design = Lane(RingCells(0, 0, 9, 7, width: 1));

        // A rail by hand on a seam of the lane, and one out on its own.
        var onTheLane = new RcpWall { Col = 4, Row = 0, Vertical = false, Double = true };
        var onItsOwn = new RcpWall { Col = 20, Row = 20, Vertical = true };
        var handTurn = new RcpCurve { Col = 30, Row = 30, Quadrant = RcpQuadrant.NorthEast, Radius = 1.5f };

        design.Walls.Add(onTheLane);
        design.Walls.Add(onItsOwn);
        design.Curves.Add(handTurn);

        RcpAutoRails.Apply(design);

        Assert.Contains(onTheLane, design.Walls);
        Assert.Contains(onItsOwn, design.Walls);
        Assert.Contains(handTurn, design.Curves);
        Assert.True(onTheLane.Double);
        Assert.False(onTheLane.Auto);

        // And no auto rail stacked on the hand one.
        Assert.Single(design.Walls, w => w.Col == 4 && w.Row == 0 && !w.Vertical);
    }

    [Fact]
    public void A_corner_squared_by_hand_stays_square()
    {
        var design = Lane(RingCells(0, 0, 9, 7, width: 1));

        // What the Corners tool leaves: hand straights where the turn was.
        design.Walls.Add(new RcpWall { Col = 0, Row = 0, Vertical = false });
        design.Walls.Add(new RcpWall { Col = 0, Row = 0, Vertical = true });

        RcpAutoRails.Apply(design);

        Assert.Null(design.CurveAt(0, 0));
        Assert.Equal(3, design.Curves.Count);
    }

    [Fact]
    public void A_hand_turn_keeps_its_corner_and_the_seams_it_sweeps()
    {
        var design = Lane(RingCells(0, 0, 9, 7, width: 1), turns: 1);
        var hand = new RcpCurve { Col = 0, Row = 0, Quadrant = RcpQuadrant.SouthEast, Radius = 2f };

        design.Curves.Add(hand);

        RcpAutoRails.Apply(design);

        Assert.Same(hand, design.CurveAt(0, 0));
        Assert.False(HasWall(design, 1, 0, vertical: false));
        Assert.False(HasWall(design, 0, 1, vertical: true));
        Assert.Equal(4, design.Curves.Count);
    }

    [Fact]
    public void Auto_rails_are_worked_out_again_after_an_edit()
    {
        var design = Lane(RingCells(0, 0, 9, 7, width: 1));
        RcpAutoRails.Apply(design);

        var before = design.Walls.ToList();

        // A bay off the top of the ring: one more tile of the lane above it.
        design.Tiles.Add(new RcpTile { Col = 5, Row = -1, Lane = 1 });

        var outcome = RcpAutoRails.Apply(design);

        Assert.True(outcome.Changed);

        // The rail that ran across the top of that tile has gone, and the
        // bay has rails round its three open sides.
        Assert.False(HasWall(design, 5, 0, vertical: false));
        Assert.True(HasWall(design, 5, -1, vertical: false));
        Assert.True(HasWall(design, 5, -1, vertical: true));
        Assert.True(HasWall(design, 6, -1, vertical: true));
        Assert.Contains(outcome.RemovedWalls, w => w.Col == 5 && w.Row == 0 && !w.Vertical);

        // Every other rail is the very same piece as before.
        foreach (var wall in before.Where(w => !(w.Col == 5 && w.Row == 0 && !w.Vertical)))
        {
            Assert.Contains(wall, design.Walls);
        }
    }

    [Fact]
    public void Working_it_out_again_with_nothing_changed_changes_nothing()
    {
        var design = Lane(RingCells(0, 0, 9, 7, width: 2), turns: 2);
        RcpAutoRails.Apply(design);

        var walls = design.Walls.ToList();
        var curves = design.Curves.ToList();

        var again = RcpAutoRails.Apply(design);

        Assert.False(again.Changed);
        Assert.Equal(walls, design.Walls);
        Assert.Equal(curves, design.Curves);
    }

    [Fact]
    public void Taking_a_lane_away_takes_its_rails_with_it()
    {
        var design = Lane(Row(0, 0, 5));
        design.Walls.Add(new RcpWall { Col = 9, Row = 9 });
        RcpAutoRails.Apply(design);

        design.Tiles.Clear();
        RcpAutoRails.Apply(design);

        var left = Assert.Single(design.Walls);
        Assert.False(left.Auto);
    }

    [Fact]
    public void A_blocked_out_tile_in_a_lane_is_railed_round()
    {
        var design = Lane(Row(0, 0, 5));
        design.Tiles.Single(t => t.Col == 2).Kind = RcpTileKind.Infield;

        RcpAutoRails.Apply(design);

        Assert.True(HasWall(design, 2, 0, vertical: true));
        Assert.True(HasWall(design, 3, 0, vertical: true));
    }

    [Fact]
    public void A_plan_drawn_before_lanes_is_left_exactly_as_it_was()
    {
        foreach (var choice in RcpStockLayout.All)
        {
            var design = RcpStockLayout.Build(choice.Name)!;
            var walls = design.Walls.ToList();
            var curves = design.Curves.ToList();

            var outcome = RcpAutoRails.Apply(design);

            Assert.False(outcome.Changed);
            Assert.Equal(walls, design.Walls);
            Assert.Equal(curves, design.Curves);
        }
    }

    [Fact]
    public void The_new_fields_default_to_as_before()
    {
        Assert.Equal(0, new RcpTile().Lane);
        Assert.False(new RcpWall().Auto);
        Assert.False(new RcpCurve().Auto);
        Assert.False(new RcpTrackDesign().RouteAuto);
        Assert.Equal(1, new RcpTrackDesign().LaneTurnRadius);
    }

    [Fact]
    public void A_copy_of_the_plan_keeps_lanes_and_auto_pieces()
    {
        var design = Lane(RingCells(0, 0, 9, 7, width: 1), lane: 3, turns: 2);
        design.RouteAuto = true;
        RcpAutoRails.Apply(design);

        var copy = design.Clone();

        Assert.All(copy.Tiles, t => Assert.Equal(3, t.Lane));
        Assert.All(copy.Walls, w => Assert.True(w.Auto));
        Assert.All(copy.Curves, c => Assert.True(c.Auto));
        Assert.True(copy.RouteAuto);
        Assert.Equal(2, copy.LaneTurnRadius);
    }

    [Fact]
    public void A_brush_and_a_ring_cover_the_cells_they_should()
    {
        Assert.Equal(new[] { new Point(5, 5) }, RcpAutoRails.Brush(new Point(5, 5), 1).ToArray());
        Assert.Equal(4, RcpAutoRails.Brush(new Point(5, 5), 2).Count());
        Assert.Contains(new Point(6, 6), RcpAutoRails.Brush(new Point(5, 5), 2));
        Assert.Contains(new Point(4, 4), RcpAutoRails.Brush(new Point(5, 5), 3));
        Assert.Equal(9, RcpAutoRails.Brush(new Point(5, 5), 3).Count());

        // 10 x 8, two wide: everything but the 6 x 4 island.
        Assert.Equal(80 - 24, RcpAutoRails.Ring(new Point(9, 7), new Point(0, 0), 2)!.Count);

        // Too small to leave an island.
        Assert.Null(RcpAutoRails.Ring(new Point(0, 0), new Point(3, 3), 2));
    }

    [Fact]
    public void A_plan_with_two_tile_turns_is_measured_as_the_floor_it_is()
    {
        // 14 x 10 tiles, two-tile turns at the corners. The turns used to be
        // framed a radius out on every side, reading 18 x 14.
        var design = Lane(RingCells(0, 0, 13, 9, width: 2), turns: 2);
        RcpAutoRails.Apply(design);

        Assert.Equal(new RectangleF(0, 0, 14, 10), design.BoundsInTiles());
    }

    [Fact]
    public void Where_two_lanes_end_flush_the_divider_stays_whole()
    {
        // Lane 1 over lane 2, both ending at column 0. A turn for lane 1 at
        // (0,2) used to sweep the divider away, leaving lane 2 open.
        var design = Lane(Row(0, 0, 5).Concat(Row(1, 0, 5)), lane: 1);
        AddLane(design, Row(2, 0, 5).Concat(Row(3, 0, 5)), lane: 2);

        RcpAutoRails.Apply(design);

        Assert.Null(design.CurveAt(0, 2));

        for (var col = 0; col <= 5; col++)
        {
            Assert.True(design.WallAt(col, 2, vertical: false)?.Double == true, $"divider missing at column {col}");
        }

        Assert.True(HasWall(design, 0, 1, vertical: true));
        Assert.True(HasWall(design, 0, 2, vertical: true));
    }

    [Fact]
    public void Wall_the_edge_leaves_a_lanes_turns_alone()
    {
        var design = Lane(RingCells(0, 0, 9, 7, width: 1));
        RcpAutoRails.Apply(design);

        RcpPlan.WallThePerimeter(design);
        RcpAutoRails.Apply(design);

        Assert.Equal(4, design.Curves.Count);
    }

    [Fact]
    public void A_lanes_turn_never_sweeps_where_a_hand_turn_already_does()
    {
        var design = Lane(RingCells(0, 0, 9, 7, width: 1), turns: 2);

        // A hand turn on the top straight's seams just short of the corner.
        design.Curves.Add(new RcpCurve { Col = 3, Row = 0, Quadrant = RcpQuadrant.SouthWest, Radius = 2f });

        RcpAutoRails.Apply(design);

        var hand = RcpAutoRails.Swept(design.Curves.Single(c => !c.Auto)).ToHashSet();

        Assert.All(design.Curves.Where(c => c.Auto), c =>
            Assert.False(RcpAutoRails.Swept(c).Any(hand.Contains), $"turn at {c.At} crosses the hand turn"));
    }
}
