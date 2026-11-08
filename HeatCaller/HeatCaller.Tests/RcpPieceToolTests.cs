using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Railed tiles, Phase 2 (7 Nov 2026): the designer lays a tile and its rail
/// as one piece, as they go down on a real RCP track, and takes them up the
/// same way. The pure half (RcpPieces.Lay, TakeUp, RoundOutsideCorners) and
/// the board's own mouse handling. The board opens at 64 pixels a tile with
/// the plan's corner 40 pixels in, so cell (c, r) is at 40 + 64c + 32 across.
/// </summary>
public sealed class RcpPieceToolTests
{
    // --- the pure half --------------------------------------------------------

    [Fact]
    public void Laying_a_rail_tile_puts_the_tile_and_its_rail_down_together()
    {
        var design = new RcpTrackDesign();

        RcpPieces.Lay(design, 2, 3, RcpPiece.Rail, RcpEdge.South, RcpQuadrant.NorthWest);
        var report = RcpPieces.Adopt(design);

        Assert.Single(design.Tiles);
        Assert.Single(design.Walls);
        Assert.Equal((2, 4, false), (design.Walls[0].Col, design.Walls[0].Row, design.Walls[0].Vertical));
        Assert.Equal((RcpPiece.Rail, RcpEdge.South), (design.At(2, 3)!.Piece, design.At(2, 3)!.Facing));
        Assert.True(report.AllOnTiles);
    }

    [Fact]
    public void Two_rail_tiles_laid_back_to_back_are_a_divider()
    {
        var design = new RcpTrackDesign();

        RcpPieces.Lay(design, 0, 0, RcpPiece.Rail, RcpEdge.South, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 0, 1, RcpPiece.Rail, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);

        Assert.Single(design.Walls);
        Assert.True(design.Walls[0].Double);
        Assert.Equal(RcpPiece.Rail, design.At(0, 0)!.Piece);
        Assert.Equal(RcpPiece.Rail, design.At(0, 1)!.Piece);
    }

    [Fact]
    public void Laying_an_outer_curve_tile_puts_a_one_tile_turn_across_its_corner()
    {
        var design = new RcpTrackDesign();

        RcpPieces.Lay(design, 4, 4, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);

        var turn = Assert.Single(design.Curves);
        Assert.Equal((4, 4, RcpQuadrant.SouthEast, 1f), (turn.Col, turn.Row, turn.Quadrant, turn.Radius));
        Assert.Equal((RcpPiece.OuterCurve, RcpQuadrant.NorthWest), (design.At(4, 4)!.Piece, design.At(4, 4)!.Corner));
    }

    [Fact]
    public void A_new_piece_on_a_tile_replaces_the_rail_it_had()
    {
        var design = new RcpTrackDesign();

        RcpPieces.Lay(design, 0, 0, RcpPiece.Rail, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 0, 0, RcpPiece.Rail, RcpEdge.East, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 0, 0, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.SouthEast);
        RcpPieces.Lay(design, 0, 0, RcpPiece.Plain, RcpEdge.North, RcpQuadrant.NorthWest);

        Assert.Single(design.Tiles);
        Assert.Empty(design.Walls);
        Assert.Empty(design.Curves);
    }

    [Fact]
    public void Taking_up_a_tile_takes_its_rail_and_leaves_the_other_half_of_a_divider()
    {
        var design = new RcpTrackDesign();

        RcpPieces.Lay(design, 0, 0, RcpPiece.Rail, RcpEdge.South, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 0, 1, RcpPiece.Rail, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 3, 3, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);

        RcpPieces.TakeUp(design, design.At(0, 0)!);
        RcpPieces.TakeUp(design, design.At(3, 3)!);

        var left = Assert.Single(design.Walls);
        Assert.False(left.Double);
        Assert.Empty(design.Curves);
        Assert.Single(design.Tiles);
    }

    [Fact]
    public void Taking_up_a_custom_tile_takes_its_second_rail_too()
    {
        var design = new RcpTrackDesign { Tiles = { new RcpTile { Col = 0, Row = 0 } } };
        RcpPlan.WallThePerimeter(design);

        RcpPieces.TakeUp(design, design.At(0, 0)!);

        Assert.Empty(design.Tiles);
        Assert.Empty(design.Walls);
    }

    [Fact]
    public void An_edit_elsewhere_never_moves_a_rail_to_the_tile_across_its_seam()
    {
        // A rail tile facing west, with track on both sides of its rail: the
        // rail stays on the tile it was laid on, edit after edit.
        var design = new RcpTrackDesign();

        RcpPieces.Lay(design, 0, 0, RcpPiece.Plain, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 1, 0, RcpPiece.Rail, RcpEdge.West, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);

        RcpPieces.Lay(design, 5, 5, RcpPiece.Plain, RcpEdge.North, RcpQuadrant.NorthWest);
        RcpPieces.Adopt(design);

        Assert.Equal((RcpPiece.Rail, RcpEdge.West), (design.At(1, 0)!.Piece, design.At(1, 0)!.Facing));
        Assert.Equal(RcpPiece.Plain, design.At(0, 0)!.Piece);
    }

    [Fact]
    public void A_divider_running_into_the_outside_wall_makes_custom_tiles_where_they_meet()
    {
        // Two lanes, rows 0-1 and 2-3, with the outside wall down the west
        // edge and the divider along row 2 from it: the tile in each corner
        // where the two meet carries two rails.
        var design = new RcpTrackDesign();

        for (var row = 0; row < 4; row++)
        {
            for (var col = 0; col < 4; col++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row });
            }
        }

        for (var row = 0; row < 4; row++)
        {
            design.Walls.Add(new RcpWall { Col = 0, Row = row, Vertical = true });
        }

        for (var col = 0; col < 3; col++)
        {
            design.Walls.Add(new RcpWall { Col = col, Row = 2, Double = true });
        }

        var report = RcpPieces.Adopt(design);

        Assert.Equal(new[] { (0, 1), (0, 2) }, report.CustomTiles.OrderBy(c => c.Row).ToArray());
        Assert.Equal(2, report.SecondRails);
    }

    [Fact]
    public void Rail_the_edge_rounds_the_floors_outside_corners_with_outer_curve_tiles()
    {
        var design = new RcpTrackDesign();

        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 4; col++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row });
            }
        }

        RcpPlan.WallThePerimeter(design);
        var rounded = RcpPieces.RoundOutsideCorners(design);
        var report = RcpPieces.Adopt(design);

        Assert.Equal(4, rounded);
        Assert.Equal(4, design.Curves.Count);
        Assert.All(design.Curves, c => Assert.Equal(1f, c.Radius));
        Assert.Equal(4, design.Tiles.Count(t => t.Piece == RcpPiece.OuterCurve));

        // Every other edge tile a rail tile, nothing custom.
        Assert.True(report.AllOnTiles, report.ToString());
        Assert.Empty(report.CustomTiles);
    }

    [Fact]
    public void The_Corners_tool_goes_between_a_one_tile_turn_and_square_with_no_two_tile_step()
    {
        var design = new RcpTrackDesign
        {
            Walls =
            {
                new RcpWall { Col = 0, Row = 0, Vertical = false },
                new RcpWall { Col = 0, Row = 0, Vertical = true },
            },
        };

        var first = RcpCorners.Next(design, new Point(0, 0), out _)!;
        Assert.Equal(1, first.Radius);
        RcpCorners.Apply(design, new Point(0, 0), first);

        var second = RcpCorners.Next(design, new Point(0, 0), out _)!;
        Assert.Equal(0, second.Radius);
    }

    // --- on the board -------------------------------------------------------

    private static Point CellCentre(int col, int row) => new(40 + 64 * col + 32, 40 + 64 * row + 32);

    /// <summary>A point in a cell, a fraction of the way across and down it.</summary>
    private static Point InCell(int col, int row, float across, float down) =>
        new(40 + 64 * col + (int)(64 * across), 40 + 64 * row + (int)(64 * down));

    private static void Mouse(RcpTrackCanvas canvas, string what, Point at, MouseButtons button = MouseButtons.Left)
    {
        typeof(RcpTrackCanvas)
            .GetMethod(what, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, new object[] { new MouseEventArgs(button, 1, at.X, at.Y, 0) });
    }

    private static void Drag(RcpTrackCanvas canvas, params Point[] path)
    {
        Mouse(canvas, "OnMouseDown", path[0]);

        foreach (var point in path.Skip(1))
        {
            Mouse(canvas, "OnMouseMove", point);
        }

        Mouse(canvas, "OnMouseUp", path[^1]);
    }

    private static void Click(RcpTrackCanvas canvas, Point at, MouseButtons button = MouseButtons.Left)
    {
        Mouse(canvas, "OnMouseDown", at, button);
        Mouse(canvas, "OnMouseUp", at, button);
    }

    private static T OnBoard<T>(Func<RcpTrackCanvas, T> test) =>
        StaThread.Run(() =>
        {
            using var canvas = new RcpTrackCanvas { Size = new Size(1200, 900) };
            return test(canvas);
        }, TimeSpan.FromSeconds(30), () => "laying pieces on the Track Designer's board");

    [Fact]
    public void A_click_near_an_edge_lays_a_rail_tile_with_its_rail_on_that_edge_and_one_undo_takes_both()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Piece;
            canvas.Item = RcpPaletteItem.RailTile;

            Click(canvas, InCell(2, 2, 0.5f, 0.1f));

            var tile = canvas.Design.At(2, 2);
            var walls = canvas.Design.Walls.Select(w => (w.Col, w.Row, w.Vertical)).ToList();

            canvas.Undo();

            return (tile?.Piece, tile?.Facing, walls, after: canvas.Design.Tiles.Count + canvas.Design.Walls.Count);
        });

        Assert.Equal(RcpPiece.Rail, seen.Piece);
        Assert.Equal(RcpEdge.North, seen.Facing);
        Assert.Equal(new[] { (2, 2, false) }, seen.walls);
        Assert.Equal(0, seen.after);
    }

    [Fact]
    public void A_drag_lays_a_row_of_rail_tiles_on_the_side_it_was_pressed_near()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Piece;
            canvas.Item = RcpPaletteItem.RailTile;

            // Pressed near the south edge of (0,3), dragged along the row.
            Drag(canvas, InCell(0, 3, 0.5f, 0.9f), CellCentre(3, 3), CellCentre(5, 3));

            return canvas.Design.Tiles
                .OrderBy(t => t.Col)
                .Select(t => (t.Col, t.Piece, t.Facing))
                .ToList();
        });

        Assert.Equal(6, seen.Count);
        Assert.All(seen, t => Assert.Equal((RcpPiece.Rail, RcpEdge.South), (t.Piece, t.Facing)));
    }

    [Fact]
    public void R_turns_the_next_piece_and_a_second_click_makes_it_plain_again()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Piece;
            canvas.Item = RcpPaletteItem.RailTile;
            canvas.PieceFacing = RcpEdge.North;

            typeof(RcpTrackCanvas)
                .GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(canvas, new object[] { new KeyEventArgs(Keys.R) });

            Click(canvas, CellCentre(1, 1));
            var laid = (canvas.Design.At(1, 1)!.Piece, canvas.Design.At(1, 1)!.Facing);

            Click(canvas, CellCentre(1, 1));
            var again = canvas.Design.At(1, 1)!.Piece;

            return (laid, again, walls: canvas.Design.Walls.Count);
        });

        Assert.Equal((RcpPiece.Rail, RcpEdge.East), seen.laid);
        Assert.Equal(RcpPiece.Plain, seen.again);
        Assert.Equal(0, seen.walls);
    }

    [Fact]
    public void A_click_in_a_corner_lays_an_outer_curve_tile_with_its_turn_across_that_corner()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Piece;
            canvas.Item = RcpPaletteItem.CurveTile;

            Click(canvas, InCell(3, 3, 0.85f, 0.85f));

            return (canvas.Design.At(3, 3)!.Piece, canvas.Design.At(3, 3)!.Corner, canvas.Design.Curves.Single().At);
        });

        Assert.Equal(RcpPiece.OuterCurve, seen.Piece);
        Assert.Equal(RcpQuadrant.SouthEast, seen.Corner);
        Assert.Equal(new Point(4, 4), seen.At);
    }

    [Fact]
    public void Erasing_a_tile_takes_it_up_with_its_rail()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Piece;
            canvas.Item = RcpPaletteItem.RailTile;
            Click(canvas, InCell(2, 2, 0.5f, 0.1f));

            canvas.Tool = RcpTool.Erase;
            Click(canvas, CellCentre(2, 2));

            return canvas.Design.Tiles.Count + canvas.Design.Walls.Count;
        });

        Assert.Equal(0, seen);
    }

    [Fact]
    public void A_custom_tile_is_reported_by_the_board()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Piece;
            canvas.Item = RcpPaletteItem.RailTile;

            // A rail tile, and a second rail on its west edge with no floor
            // beyond it - as an old plan's square corner has. The next edit
            // works the pieces out again, and the tile is a custom one.
            Click(canvas, InCell(2, 2, 0.5f, 0.1f));
            canvas.Design.Walls.Add(new RcpWall { Col = 2, Row = 2, Vertical = true });
            canvas.Tool = RcpTool.Floor;
            Click(canvas, CellCentre(5, 5));

            return canvas.PieceReport.CustomTiles.ToList();
        });

        Assert.Equal(new[] { (2, 2) }, seen);
    }
}
