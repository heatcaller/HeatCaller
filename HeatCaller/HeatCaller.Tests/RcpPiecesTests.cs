namespace HeatCaller.Tests;

/// <summary>
/// Railed tiles, Phase 1 (7 Nov 2026): every rail of a plan put on the tile
/// it is bolted to, and the rails worked back out from the tiles exactly as
/// they were. On a real RCP track a tile carries one rail at most, bolted
/// flush along its one bolt-hole edge; a turn is a quarter-circle tile of its
/// own; a divider is two lanes' rails back to back (Asher's answers and
/// photos - plans\track-designer-railed-tiles-plan.md).
/// </summary>
public sealed class RcpPiecesTests
{
    private static RcpTile Tile(int col, int row, int lane = 0, RcpTileKind kind = RcpTileKind.Straight) =>
        new() { Col = col, Row = row, Lane = lane, Kind = kind };

    private static RcpTile TileAt(RcpTrackDesign design, int col, int row) => design.At(col, row)!;

    private static HashSet<(int, int, bool, bool)> WallSet(IEnumerable<RcpWall> walls) =>
        walls.Select(w => (w.Col, w.Row, w.Vertical, w.Double)).ToHashSet();

    private static HashSet<(int, int, RcpQuadrant)> CurveSet(IEnumerable<RcpCurve> curves) =>
        curves.Select(c => (c.Col, c.Row, c.Quadrant)).ToHashSet();

    [Fact]
    public void A_rail_at_the_edge_of_the_track_is_bolted_to_the_track_tile_facing_out()
    {
        // Track at (0,0), blocked-out floor north of it, the rail between.
        var design = new RcpTrackDesign
        {
            Tiles = { Tile(0, 0), Tile(0, -1, kind: RcpTileKind.Infield) },
            Walls = { new RcpWall { Col = 0, Row = 0, Vertical = false } },
        };

        var report = RcpPieces.Adopt(design);

        Assert.True(report.AllOnTiles);
        Assert.Equal(RcpPiece.Rail, TileAt(design, 0, 0).Piece);
        Assert.Equal(RcpEdge.North, TileAt(design, 0, 0).Facing);
        Assert.Equal(RcpPiece.Plain, TileAt(design, 0, -1).Piece);
    }

    [Fact]
    public void A_divider_is_two_rails_back_to_back_one_on_each_lanes_tile()
    {
        var design = new RcpTrackDesign
        {
            Tiles = { Tile(0, 0, lane: 1), Tile(1, 0, lane: 2) },
            Walls = { new RcpWall { Col = 1, Row = 0, Vertical = true, Double = true } },
        };

        Assert.True(RcpPieces.Adopt(design).AllOnTiles);

        Assert.Equal((RcpPiece.Rail, RcpEdge.East), (TileAt(design, 0, 0).Piece, TileAt(design, 0, 0).Facing));
        Assert.Equal((RcpPiece.Rail, RcpEdge.West), (TileAt(design, 1, 0).Piece, TileAt(design, 1, 0).Facing));

        var (walls, _) = RcpPieces.Project(design);
        Assert.Equal(WallSet(design.Walls), WallSet(walls));
    }

    [Fact]
    public void A_one_tile_turn_is_an_outer_curve_tile_and_comes_back_as_the_same_turn()
    {
        foreach (var quadrant in Enum.GetValues<RcpQuadrant>())
        {
            var curve = new RcpCurve { Col = 5, Row = 5, Quadrant = quadrant, Radius = 1f };
            var (col, row) = RcpPlan.QuadrantCell(curve);

            var design = new RcpTrackDesign { Tiles = { Tile(col, row) }, Curves = { curve } };

            Assert.True(RcpPieces.Adopt(design).AllOnTiles);
            Assert.Equal(RcpPiece.OuterCurve, TileAt(design, col, row).Piece);
            Assert.Equal(quadrant, TileAt(design, col, row).Corner);

            var (_, curves) = RcpPieces.Project(design);
            Assert.Equal(CurveSet(design.Curves), CurveSet(curves));
        }
    }

    [Fact]
    public void The_inside_of_a_bend_is_an_inner_corner_tile_with_its_piece_in_the_corner_round_the_island()
    {
        // Three of the four tiles round the point (1,1) are the lane; the
        // fourth, (1,1), isn't. The tile across from it carries the piece,
        // in the corner at that point - its south-east.
        var design = new RcpTrackDesign
        {
            Tiles = { Tile(0, 0, lane: 1), Tile(1, 0, lane: 1), Tile(0, 1, lane: 1) },
        };

        RcpPieces.Adopt(design);

        Assert.Equal(RcpPiece.InnerCorner, TileAt(design, 0, 0).Piece);
        Assert.Equal(RcpQuadrant.SouthEast, TileAt(design, 0, 0).Corner);
    }

    [Fact]
    public void A_tile_carries_one_rail_and_a_second_is_reported_not_lost()
    {
        // A lone tile railed all round: four rails, one tile.
        var design = new RcpTrackDesign { Tiles = { Tile(0, 0) } };
        RcpPlan.WallThePerimeter(design);

        var report = RcpPieces.Adopt(design);

        Assert.Equal(RcpPiece.Rail, TileAt(design, 0, 0).Piece);
        Assert.Equal(3, report.SecondRails);
        Assert.False(report.AllOnTiles);

        // The seams are untouched: nothing a club drew is taken away.
        Assert.Equal(4, design.Walls.Count);
    }

    [Fact]
    public void A_rail_with_no_tile_under_it_is_reported()
    {
        var design = new RcpTrackDesign { Walls = { new RcpWall { Col = 3, Row = 3, Vertical = true } } };

        var report = RcpPieces.Adopt(design);

        Assert.Equal(1, report.RailsWithNoTile);
        Assert.Single(design.Walls);
    }

    [Fact]
    public void Turns_the_club_has_no_piece_for_are_reported_and_kept()
    {
        var design = new RcpTrackDesign
        {
            Tiles = { Tile(0, 0), Tile(1, 0), Tile(0, 1), Tile(1, 1) },
            Curves =
            {
                new RcpCurve { Col = 0, Row = 0, Quadrant = RcpQuadrant.SouthEast, Radius = 2f },
                new RcpCurve { Col = 2, Row = 2, Quadrant = RcpQuadrant.NorthWest, Radius = 1f, Double = true },
            },
        };

        var report = RcpPieces.Adopt(design);

        Assert.Equal(2, report.OtherTurns);
        Assert.Equal(2, design.Curves.Count);
        Assert.DoesNotContain(design.Tiles, t => t.Piece == RcpPiece.OuterCurve);
    }

    [Fact]
    public void Adopting_again_after_an_edit_starts_from_the_rails_not_from_the_last_answer()
    {
        var design = new RcpTrackDesign
        {
            Tiles = { Tile(0, 0), Tile(0, -1, kind: RcpTileKind.Infield) },
            Walls = { new RcpWall { Col = 0, Row = 0, Vertical = false } },
        };

        RcpPieces.Adopt(design);
        design.Walls.Clear();
        RcpPieces.Adopt(design);

        Assert.Equal(RcpPiece.Plain, TileAt(design, 0, 0).Piece);
    }

    [Fact]
    public void Finish_and_arched_tiles_are_kept_and_stand_for_the_rail_on_their_seam()
    {
        var design = new RcpTrackDesign
        {
            Tiles =
            {
                new RcpTile { Col = 0, Row = 0, Piece = RcpPiece.Finish, Facing = RcpEdge.South },
                new RcpTile { Col = 1, Row = 0, Piece = RcpPiece.Arched, Facing = RcpEdge.South },
            },
            Walls =
            {
                new RcpWall { Col = 0, Row = 1, Vertical = false },
                new RcpWall { Col = 1, Row = 1, Vertical = false },
            },
        };

        Assert.True(RcpPieces.Adopt(design).AllOnTiles);
        Assert.Equal(RcpPiece.Finish, TileAt(design, 0, 0).Piece);
        Assert.Equal(RcpPiece.Arched, TileAt(design, 1, 0).Piece);

        var (walls, _) = RcpPieces.Project(design);
        Assert.Equal(WallSet(design.Walls), WallSet(walls));
    }

    [Fact]
    public void Every_rail_of_every_RCP_layout_is_on_a_tile_or_reported_and_none_is_lost()
    {
        // RCP's presets are reconstructions (see RcpStockLayout), and most
        // draw something the club's pieces can't build: the 50cm Oval's
        // lanes are one tile wide, three use two-tile turns, and a divider
        // running into the outside wall leaves its corner tile two rails.
        // So the promise here is not that they are clean - it is that every
        // rail either lands on a tile of its own or is counted, and that the
        // tiles give back nothing that wasn't there.
        foreach (var choice in RcpStockLayout.All)
        {
            var design = RcpStockLayout.Build(choice.Name)!;
            var report = RcpPieces.Adopt(design);

            var straights = design.Walls.Sum(w => w.Double ? 2 : 1);
            var onTiles = design.Tiles.Count(t => RcpPieces.HasStraight(t.Piece));

            Assert.True(straights == onTiles + report.SecondRails + report.RailsWithNoTile, $"{choice.Name}: straight rails lost - {report}");

            var turnsOnTiles = design.Tiles.Count(t => t.Piece == RcpPiece.OuterCurve);
            Assert.True(design.Curves.Count == turnsOnTiles + report.TurnsWithNoTile + report.OtherTurns, $"{choice.Name}: turns lost - {report}");

            var (walls, curves) = RcpPieces.Project(design);
            Assert.True(walls.All(w => design.WallAt(w.Col, w.Row, w.Vertical) is not null), $"{choice.Name}: a rail that wasn't there");
            Assert.True(CurveSet(curves).IsSubsetOf(CurveSet(design.Curves)), $"{choice.Name}: a turn that wasn't there");
        }
    }

    [Fact]
    public void The_double_oval_is_built_from_the_clubs_pieces_and_comes_back_exactly()
    {
        // The one preset with two-tile lanes, one-tile turns and no divider:
        // every rail on a tile of its own.
        var problems = new List<string>();

        Check("30cm Double Oval Wide", RcpStockLayout.Build("30cm Double Oval Wide")!, problems);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_worked_out_layout_goes_onto_tiles_and_comes_back_exactly()
    {
        var problems = new List<string>();

        foreach (var (width, depth, tile) in new[] { (6100.0, 4300.0, 500.0), (9000.0, 6000.0, 500.0), (3700.0, 2800.0, 300.0) })
        {
            var room = new RcpTrackDesign { TileSizeMm = tile, RoomWidthMm = width, RoomDepthMm = depth, RoomInFeet = false };

            foreach (var layout in RcpLayoutGenerator.For(room, null, 2).Where(l => !l.RcpOwn))
            {
                Check($"{layout.Name} in {width} x {depth}", layout.Design.Clone(), problems);
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Adopts a plan's rails and checks every one is on a tile of its own, and that the tiles give back exactly the rails.</summary>
    private static void Check(string name, RcpTrackDesign design, List<string> problems)
    {
        var report = RcpPieces.Adopt(design);

        if (!report.AllOnTiles)
        {
            problems.Add($"{name}: not all on tiles - {report}");
            return;
        }

        var (walls, curves) = RcpPieces.Project(design);

        var wantWalls = WallSet(design.Walls);
        var gotWalls = WallSet(walls);

        if (!wantWalls.SetEquals(gotWalls))
        {
            problems.Add($"{name}: rails differ - missing {string.Join(" ", wantWalls.Except(gotWalls))}, extra {string.Join(" ", gotWalls.Except(wantWalls))}");
        }

        if (!CurveSet(design.Curves).SetEquals(CurveSet(curves)))
        {
            problems.Add($"{name}: turns differ");
        }
    }

    [Fact]
    public void A_plan_file_keeps_each_tiles_piece()
    {
        var design = new RcpTrackDesign
        {
            Tiles =
            {
                new RcpTile { Col = 0, Row = 0, Piece = RcpPiece.Rail, Facing = RcpEdge.East },
                new RcpTile { Col = 1, Row = 0, Piece = RcpPiece.OuterCurve, Corner = RcpQuadrant.SouthWest },
            },
        };

        var back = RcpPlanFile.Read(RcpPlanFile.Write(design, "pieces")).Design!;

        Assert.Equal((RcpPiece.Rail, RcpEdge.East), (back.At(0, 0)!.Piece, back.At(0, 0)!.Facing));
        Assert.Equal((RcpPiece.OuterCurve, RcpQuadrant.SouthWest), (back.At(1, 0)!.Piece, back.At(1, 0)!.Corner));
    }

    [Fact]
    public void A_piece_this_HeatCaller_does_not_know_opens_as_a_plain_tile()
    {
        var design = new RcpTrackDesign { Tiles = { new RcpTile { Col = 0, Row = 0, Piece = (RcpPiece)99 } } };

        var back = RcpPlanFile.Read(RcpPlanFile.Write(design, "unknown")).Design!;

        Assert.Equal(RcpPiece.Plain, back.At(0, 0)!.Piece);
    }

    [Fact]
    public void A_tile_copied_for_undo_keeps_its_piece()
    {
        var tile = new RcpTile { Col = 2, Row = 3, Piece = RcpPiece.InnerCorner, Facing = RcpEdge.West, Corner = RcpQuadrant.NorthWest };
        var copy = tile.Clone();

        Assert.Equal((tile.Piece, tile.Facing, tile.Corner), (copy.Piece, copy.Facing, copy.Corner));
    }
}
