namespace HeatCaller.Tests;

/// <summary>
/// Picking a handful of the plan up and putting it down somewhere else.
///
/// The thing that needs testing here is not "does the selection remember
/// what I clicked" - it is the geometry. The three kinds of piece sit on
/// the grid differently: a tile fills a cell, a rail lies along a seam
/// between two grid points, a turn rail is anchored AT a grid point and
/// sweeps into one quadrant of it. A quarter turn has to do the right
/// thing to all three at once, and getting the seam arithmetic off by one
/// leaves rails floating half a tile away from the floor they are bolted
/// to - which looks almost right on screen and is not buildable.
///
/// So the mappings are checked one at a time, and then the invariants a
/// person actually relies on are checked on a whole block: four quarter
/// turns come back to exactly where they started, a mirror twice is a
/// no-op, the top-left corner never wanders, and a corner rail goes on
/// pointing at the two rails it joins.
/// </summary>
public sealed class RcpSelectionTests
{
    /// <summary>A 4x2 block of floor at (3,2), rails all round it, two corners, a marking and two route points.</summary>
    private static RcpTrackDesign Block()
    {
        var design = new RcpTrackDesign();

        for (var col = 3; col < 7; col++)
        {
            for (var row = 2; row < 4; row++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row });
            }
        }

        for (var col = 3; col < 7; col++)
        {
            design.Walls.Add(new RcpWall { Col = col, Row = 2 });
            design.Walls.Add(new RcpWall { Col = col, Row = 4 });
        }

        for (var row = 2; row < 4; row++)
        {
            design.Walls.Add(new RcpWall { Col = 3, Row = row, Vertical = true });
            design.Walls.Add(new RcpWall { Col = 7, Row = row, Vertical = true });
        }

        design.Curves.Add(new RcpCurve { Col = 3, Row = 2, Quadrant = RcpQuadrant.SouthEast });
        design.Curves.Add(new RcpCurve { Col = 7, Row = 4, Quadrant = RcpQuadrant.NorthWest });

        design.Decals.Add(new RcpDecal { Col = 4, Row = 3, Kind = RcpDecalKind.GridBox, Turns = 1 });

        design.Route.Add(new RcpRoutePoint { X = 3.5f, Y = 2.5f });
        design.Route.Add(new RcpRoutePoint { X = 6.5f, Y = 3.5f });

        return design;
    }

    /// <summary>Everything on a plan, written down so two plans can be compared.</summary>
    private static string Snapshot(RcpTrackDesign design)
    {
        var lines = new List<string>();

        lines.AddRange(design.Tiles.Select(t => $"tile {t.Col},{t.Row} k{(int)t.Kind} t{t.Turns}").OrderBy(s => s));
        lines.AddRange(design.Walls.Select(w => $"wall {w.Col},{w.Row} {(w.Vertical ? "v" : "h")}{(w.Double ? "d" : string.Empty)}").OrderBy(s => s));
        lines.AddRange(design.Curves.Select(c => $"curve {c.Col},{c.Row} {c.Quadrant} r{c.Radius}").OrderBy(s => s));
        lines.AddRange(design.Decals.Select(d => $"decal {d.Col},{d.Row} {d.Kind} t{d.Turns}").OrderBy(s => s));
        lines.AddRange(design.Route.Select(p => $"route {p.X},{p.Y}").OrderBy(s => s));

        return string.Join(Environment.NewLine, lines);
    }

    // --- The mappings, one at a time ----------------------------------------

    [Fact]
    public void A_quarter_turn_makes_a_horizontal_rail_vertical_and_the_other_way_round()
    {
        // Worked out from the seam's two ends. Clockwise on screen sends
        // (x,y) to (-y,x), so a rail from (3,4) to (4,4) lands on the ends
        // (-4,3) and (-4,4) - one grid column, one row apart, which is a
        // vertical seam held by its upper end.
        var flat = new RcpTrackDesign();
        flat.Walls.Add(new RcpWall { Col = 3, Row = 4 });

        var picked = RcpSelection.Everything(flat);
        var before = picked.Bounds();

        picked.TurnClockwise();

        Assert.True(flat.Walls[0].Vertical);

        // Re-anchored to the box it came from, so the test says what the
        // user sees: the piece turned where it stood.
        Assert.Equal(before.Left, picked.Bounds().Left);
        Assert.Equal(before.Top, picked.Bounds().Top);

        // And the other way round: a vertical seam runs between two grid
        // lines after the turn, so the end that holds it is the other one.
        var upright = new RcpTrackDesign();
        upright.Walls.Add(new RcpWall { Col = 3, Row = 4, Vertical = true });

        RcpSelection.Everything(upright).TurnClockwise();

        Assert.False(upright.Walls[0].Vertical);

        // Four turns and it is a vertical rail at (3,4) again - checked on
        // a whole block below, but worth saying of the one piece too.
        var there = new RcpTrackDesign();
        there.Walls.Add(new RcpWall { Col = 3, Row = 4, Vertical = true });

        var all = RcpSelection.Everything(there);

        for (var i = 0; i < 4; i++)
        {
            all.TurnClockwise();
        }

        Assert.True(there.Walls[0].Vertical);
        Assert.Equal(3, there.Walls[0].Col);
        Assert.Equal(4, there.Walls[0].Row);
    }

    [Fact]
    public void A_quarter_turn_walks_a_quadrant_round_one_place()
    {
        Assert.Equal(RcpQuadrant.SouthEast, RcpSelection.Turned(RcpQuadrant.NorthEast));
        Assert.Equal(RcpQuadrant.SouthWest, RcpSelection.Turned(RcpQuadrant.SouthEast));
        Assert.Equal(RcpQuadrant.NorthWest, RcpSelection.Turned(RcpQuadrant.SouthWest));
        Assert.Equal(RcpQuadrant.NorthEast, RcpSelection.Turned(RcpQuadrant.NorthWest));
    }

    [Fact]
    public void A_mirror_swaps_only_the_half_of_a_quadrants_name_it_mirrors()
    {
        Assert.Equal(RcpQuadrant.NorthWest, RcpSelection.MirroredLeftRight(RcpQuadrant.NorthEast));
        Assert.Equal(RcpQuadrant.SouthWest, RcpSelection.MirroredLeftRight(RcpQuadrant.SouthEast));
        Assert.Equal(RcpQuadrant.NorthEast, RcpSelection.MirroredLeftRight(RcpQuadrant.NorthWest));
        Assert.Equal(RcpQuadrant.SouthEast, RcpSelection.MirroredLeftRight(RcpQuadrant.SouthWest));

        Assert.Equal(RcpQuadrant.SouthEast, RcpSelection.MirroredTopBottom(RcpQuadrant.NorthEast));
        Assert.Equal(RcpQuadrant.NorthEast, RcpSelection.MirroredTopBottom(RcpQuadrant.SouthEast));
        Assert.Equal(RcpQuadrant.SouthWest, RcpSelection.MirroredTopBottom(RcpQuadrant.NorthWest));
        Assert.Equal(RcpQuadrant.NorthWest, RcpSelection.MirroredTopBottom(RcpQuadrant.SouthWest));
    }

    // --- The invariants somebody relies on ----------------------------------

    [Fact]
    public void Four_quarter_turns_come_back_to_exactly_where_they_started()
    {
        var design = Block();
        var before = Snapshot(design);

        var picked = RcpSelection.Everything(design);

        for (var i = 0; i < 4; i++)
        {
            picked.TurnClockwise();
        }

        Assert.Equal(before, Snapshot(design));
    }

    [Fact]
    public void A_mirror_twice_is_a_no_op_either_way()
    {
        var acrossDesign = Block();
        var acrossBefore = Snapshot(acrossDesign);
        var across = RcpSelection.Everything(acrossDesign);

        across.MirrorLeftRight();
        across.MirrorLeftRight();

        Assert.Equal(acrossBefore, Snapshot(acrossDesign));

        var downDesign = Block();
        var downBefore = Snapshot(downDesign);
        var down = RcpSelection.Everything(downDesign);

        down.MirrorTopBottom();
        down.MirrorTopBottom();

        Assert.Equal(downBefore, Snapshot(downDesign));
    }

    [Fact]
    public void A_turn_keeps_the_top_left_corner_still_and_swaps_the_shape_of_the_box()
    {
        var design = Block();
        var picked = RcpSelection.Everything(design);

        var before = picked.Bounds();

        picked.TurnClockwise();

        var after = picked.Bounds();

        Assert.Equal(before.Left, after.Left);
        Assert.Equal(before.Top, after.Top);
        Assert.Equal(before.Height, after.Width);
        Assert.Equal(before.Width, after.Height);
    }

    [Fact]
    public void A_mirror_leaves_the_box_exactly_where_it_was()
    {
        foreach (var mirror in new Action<RcpSelection>[]
                 {
                     s => s.MirrorLeftRight(),
                     s => s.MirrorTopBottom(),
                 })
        {
            var design = Block();
            var picked = RcpSelection.Everything(design);

            var before = picked.Bounds();

            mirror(picked);

            Assert.Equal(before, picked.Bounds());
        }
    }

    [Fact]
    public void A_rail_still_has_a_floor_under_it_after_every_transform()
    {
        // The one that matters. A rail is bolted THROUGH the tile it runs
        // along, so a transform that moved the rails and the floor by
        // different amounts would leave pieces nobody can fit - and it
        // would look very nearly right.
        var transforms = new (string What, Action<RcpSelection> Do)[]
        {
            ("one turn", s => s.TurnClockwise()),
            ("two turns", s => { s.TurnClockwise(); s.TurnClockwise(); }),
            ("three turns", s => { s.TurnClockwise(); s.TurnClockwise(); s.TurnClockwise(); }),
            ("mirrored left to right", s => s.MirrorLeftRight()),
            ("mirrored top to bottom", s => s.MirrorTopBottom()),
            ("turned then mirrored", s => { s.TurnClockwise(); s.MirrorLeftRight(); }),
            ("moved", s => s.Move(-9, 14)),
        };

        foreach (var (what, transform) in transforms)
        {
            var design = Block();

            transform(RcpSelection.Everything(design));

            Assert.True(
                RcpPlan.RailsWithNoFloor(design) == 0,
                $"{what}: {RcpPlan.RailsWithNoFloor(design)} rails came off the floor");
        }
    }

    [Fact]
    public void A_boxed_in_tile_turns_into_a_boxed_in_tile()
    {
        // One tile with a rail on each of its four edges. Whatever the
        // turn does to the seams, there has to be exactly one rail on each
        // edge afterwards - not three on one side and one off the end.
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        design.Walls.Add(new RcpWall { Col = 0, Row = 0 });
        design.Walls.Add(new RcpWall { Col = 0, Row = 1 });
        design.Walls.Add(new RcpWall { Col = 0, Row = 0, Vertical = true });
        design.Walls.Add(new RcpWall { Col = 1, Row = 0, Vertical = true });

        RcpSelection.Everything(design).TurnClockwise();

        Assert.Single(design.Tiles);
        Assert.Equal(4, design.Walls.Count);

        var tile = design.Tiles[0];

        Assert.NotNull(design.WallAt(tile.Col, tile.Row, false));
        Assert.NotNull(design.WallAt(tile.Col, tile.Row + 1, false));
        Assert.NotNull(design.WallAt(tile.Col, tile.Row, true));
        Assert.NotNull(design.WallAt(tile.Col + 1, tile.Row, true));
    }

    [Fact]
    public void A_corner_goes_on_pointing_at_the_two_rails_it_joins()
    {
        // The strong check on the quadrant mapping. A corner whose
        // quadrant walked the wrong way would still sit on its anchor
        // point, touching both rails - and sweep its arc off into the
        // middle of the floor. So this follows the quadrant's own arms
        // (RcpCurve.Arms) and insists there is a rail leaving the anchor
        // in each of those two directions.
        var quadrants = Enum.GetValues<RcpQuadrant>();

        var transforms = new (string What, Action<RcpSelection> Do)[]
        {
            ("one turn", s => s.TurnClockwise()),
            ("two turns", s => { s.TurnClockwise(); s.TurnClockwise(); }),
            ("three turns", s => { s.TurnClockwise(); s.TurnClockwise(); s.TurnClockwise(); }),
            ("mirrored left to right", s => s.MirrorLeftRight()),
            ("mirrored top to bottom", s => s.MirrorTopBottom()),
            ("turned then mirrored", s => { s.TurnClockwise(); s.MirrorLeftRight(); }),
            ("mirrored then turned", s => { s.MirrorTopBottom(); s.TurnClockwise(); }),
        };

        foreach (var (what, transform) in transforms)
        {
            foreach (var quadrant in quadrants)
            {
                var design = CornerOn(quadrant);

                transform(RcpSelection.Everything(design));

                var curve = design.Curves[0];
                var (armA, armB) = curve.Arms;

                foreach (var arm in new[] { armA, armB })
                {
                    Assert.True(
                        RailLeaving(design, curve.At, arm),
                        $"a {quadrant} corner {what} became {curve.Quadrant} at "
                        + $"({curve.Col},{curve.Row}) with no rail toward ({arm.X},{arm.Y})");
                }
            }
        }
    }

    /// <summary>Four tiles round a grid point, with a corner at it and the two rails its quadrant joins.</summary>
    private static RcpTrackDesign CornerOn(RcpQuadrant quadrant)
    {
        var design = new RcpTrackDesign();

        foreach (var (col, row) in new[] { (0, 0), (-1, 0), (0, -1), (-1, -1) })
        {
            design.Tiles.Add(new RcpTile { Col = col, Row = row });
        }

        var curve = new RcpCurve { Col = 0, Row = 0, Quadrant = quadrant };
        design.Curves.Add(curve);

        var (armA, armB) = curve.Arms;

        foreach (var arm in new[] { armA, armB })
        {
            design.Walls.Add(RailFrom(curve.At, arm));
        }

        return design;
    }

    /// <summary>The rail that leaves <paramref name="at"/> in direction <paramref name="step"/>.</summary>
    private static RcpWall RailFrom(Point at, Point step) => step switch
    {
        { X: 1 } => new RcpWall { Col = at.X, Row = at.Y },
        { X: -1 } => new RcpWall { Col = at.X - 1, Row = at.Y },
        { Y: 1 } => new RcpWall { Col = at.X, Row = at.Y, Vertical = true },
        _ => new RcpWall { Col = at.X, Row = at.Y - 1, Vertical = true },
    };

    private static bool RailLeaving(RcpTrackDesign design, Point at, Point step)
    {
        var want = RailFrom(at, step);

        return design.WallAt(want.Col, want.Row, want.Vertical) is not null;
    }

    // --- Picking -------------------------------------------------------------

    [Fact]
    public void A_marquee_over_a_block_of_tiles_brings_the_rails_round_it_as_well()
    {
        // Which is the whole point. Sliding a layout two tiles left to fit
        // the room and leaving its outside wall behind would be useless.
        var design = Block();

        var picked = RcpSelection.In(design, new Rectangle(3, 2, 4, 2));

        Assert.Equal(8, picked.Tiles.Count);
        Assert.Equal(design.Walls.Count, picked.Walls.Count);
        Assert.Equal(2, picked.Curves.Count);
        Assert.Single(picked.Decals);
        Assert.Equal(2, picked.Route.Count);
    }

    [Fact]
    public void A_rail_with_one_end_outside_the_marquee_stays_where_it_is()
    {
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });

        // Inside: both ends within (0,0)-(1,1).
        design.Walls.Add(new RcpWall { Col = 0, Row = 0 });

        // Half out: runs from (1,0) to (2,0), and (2,0) is past the block.
        design.Walls.Add(new RcpWall { Col = 1, Row = 0 });

        var picked = RcpSelection.In(design, new Rectangle(0, 0, 1, 1));

        Assert.Single(picked.Walls);
        Assert.Equal(0, picked.Walls.First().Col);
    }

    [Fact]
    public void An_empty_marquee_picks_nothing_rather_than_everything()
    {
        var design = Block();

        Assert.True(RcpSelection.In(design, Rectangle.Empty).IsEmpty);
        Assert.True(RcpSelection.In(design, new Rectangle(40, 40, 3, 3)).IsEmpty);
    }

    [Fact]
    public void Shift_dragging_a_second_time_adds_to_what_was_picked()
    {
        var design = Block();

        var left = RcpSelection.In(design, new Rectangle(3, 2, 2, 2));
        var right = RcpSelection.In(design, new Rectangle(5, 2, 2, 2));

        Assert.Equal(4, left.Tiles.Count);

        left.Add(right);

        Assert.Equal(8, left.Tiles.Count);
        Assert.True(left.HoldsAllOf(right));

        left.Remove(right);

        Assert.Equal(4, left.Tiles.Count);
        Assert.False(left.HoldsAllOf(right));
    }

    [Fact]
    public void Nothing_is_picked_out_of_an_empty_plan()
    {
        var picked = RcpSelection.Everything(new RcpTrackDesign());

        Assert.True(picked.IsEmpty);
        Assert.Equal(Rectangle.Empty, picked.Bounds());
        Assert.Equal("Nothing picked.", picked.Describe());

        // And every transform is a no-op rather than a crash.
        picked.TurnClockwise();
        picked.MirrorLeftRight();
        picked.MirrorTopBottom();
        picked.Move(3, 4);

        Assert.True(picked.IsEmpty);
    }

    // --- Landing it ----------------------------------------------------------

    [Fact]
    public void Moving_a_block_onto_another_one_clears_what_was_underneath()
    {
        // The plan holds one tile, one rail and one marking per place -
        // every tool in the editor assumes it. Two tiles in one cell would
        // be counted twice by the inventory and the kit check.
        var design = new RcpTrackDesign();

        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        design.Tiles.Add(new RcpTile { Col = 1, Row = 0, Kind = RcpTileKind.Infield });
        design.Walls.Add(new RcpWall { Col = 1, Row = 0, Vertical = true });

        var picked = RcpSelection.In(design, new Rectangle(0, 0, 1, 1));

        Assert.Single(picked.Tiles);

        picked.Move(1, 0);
        picked.Settle(design);

        Assert.Single(design.Tiles);
        Assert.Equal(RcpTileKind.Straight, design.Tiles[0].Kind);
        Assert.Equal(1, design.Tiles[0].Col);
    }

    [Fact]
    public void Settling_never_eats_the_pieces_that_were_moved()
    {
        var design = Block();
        var picked = RcpSelection.Everything(design);

        var count = design.Tiles.Count + design.Walls.Count + design.Curves.Count + design.Decals.Count;

        picked.Move(0, 0);
        picked.Settle(design);

        Assert.Equal(count, design.Tiles.Count + design.Walls.Count + design.Curves.Count + design.Decals.Count);
    }

    [Fact]
    public void A_pasted_split_loop_replaces_the_one_that_was_already_there()
    {
        // A track has one Split 1, and RcpPlan.MeasureSectors measures
        // between them - two of them would make the sectors meaningless.
        var design = new RcpTrackDesign();
        design.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.SplitLoop1 });

        var clip = new RcpTrackDesign();
        clip.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.SplitLoop1 });

        RcpSelection.Paste(design, clip, new Point(5, 5));

        Assert.Single(design.Decals);
        Assert.Equal(5, design.Decals[0].Col);
    }

    // --- Copying -------------------------------------------------------------

    [Fact]
    public void A_copy_has_its_top_left_corner_at_the_origin_so_it_can_be_dropped_anywhere()
    {
        var design = Block();
        var clip = RcpSelection.Everything(design).Copy();

        var bounds = RcpSelection.Everything(clip).Bounds();

        Assert.Equal(0, bounds.Left);
        Assert.Equal(0, bounds.Top);
    }

    [Fact]
    public void A_copy_goes_on_being_what_was_copied_after_the_plan_moves_on()
    {
        var design = Block();
        var clip = RcpSelection.Everything(design).Copy();

        var before = Snapshot(clip);

        // Everything that was copied, moved and turned afterwards.
        var picked = RcpSelection.Everything(design);
        picked.Move(20, 20);
        picked.TurnClockwise();
        picked.DeleteFrom(design);

        Assert.Equal(before, Snapshot(clip));
        Assert.True(design.IsEmpty);
    }

    [Fact]
    public void Pasting_twice_gives_two_separate_things_rather_than_one_thing_twice()
    {
        var design = new RcpTrackDesign();

        var clip = new RcpTrackDesign();
        clip.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        clip.Walls.Add(new RcpWall { Col = 0, Row = 0 });

        var first = RcpSelection.Paste(design, clip, new Point(0, 0));
        var second = RcpSelection.Paste(design, clip, new Point(4, 0));

        Assert.Equal(2, design.Tiles.Count);
        Assert.Equal(2, design.Walls.Count);

        // And moving one does not move the other.
        second.Move(0, 6);

        Assert.Contains(design.Tiles, t => t is { Col: 0, Row: 0 });
        Assert.Contains(design.Tiles, t => t is { Col: 4, Row: 6 });

        Assert.False(first.HoldsAllOf(second));
    }

    [Fact]
    public void What_was_just_pasted_is_what_is_picked()
    {
        // So it can be dragged, turned or pasted again without hunting
        // for it.
        var design = Block();

        var clip = RcpSelection.Everything(design).Copy();
        var pasted = RcpSelection.Paste(design, clip, new Point(20, 20));

        Assert.Equal(20, pasted.Bounds().Left);
        Assert.Equal(20, pasted.Bounds().Top);
        Assert.Equal(clip.Tiles.Count, pasted.Tiles.Count);
    }

    [Fact]
    public void Deleting_a_selection_takes_exactly_what_was_picked_and_nothing_else()
    {
        var design = Block();

        var tiles = design.Tiles.Count;
        var picked = RcpSelection.In(design, new Rectangle(3, 2, 1, 1));
        var taken = picked.Tiles.Count;

        picked.DeleteFrom(design);

        Assert.Equal(tiles - taken, design.Tiles.Count);
    }

    // --- Saying what it is ---------------------------------------------------

    [Fact]
    public void What_is_picked_is_said_in_the_plans_own_words()
    {
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });

        var one = RcpSelection.Everything(design);

        Assert.StartsWith("1 tile picked", one.Describe());

        design.Tiles.Add(new RcpTile { Col = 1, Row = 0 });
        design.Walls.Add(new RcpWall { Col = 0, Row = 0 });

        var two = RcpSelection.Everything(design);

        Assert.StartsWith("2 tiles and 1 rail picked", two.Describe());
    }
}
