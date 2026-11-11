using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Track Designer overhaul, Phase 8: stretching a plan by whole tiles,
/// and naming tiles.
/// </summary>
public sealed class RcpStretchTests
{
    /// <summary>A two-wide ring of lanes 12 x 8, its rails worked out, a finish line on the top straight.</summary>
    private static RcpTrackDesign Ring()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500 };

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(11, 7), 2)!)
        {
            design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Lane = 1 });
        }

        RcpAutoRails.Apply(design);
        design.Decals.Add(new RcpDecal { Col = 9, Row = 0, Kind = RcpDecalKind.FinishLine });

        return design;
    }

    private static int Columns(RcpTrackDesign design) => design.Tiles.Max(t => t.Col) - design.Tiles.Min(t => t.Col) + 1;

    private static int Rows(RcpTrackDesign design) => design.Tiles.Max(t => t.Row) - design.Tiles.Min(t => t.Row) + 1;

    [Fact]
    public void Longer_repeats_the_column_inside_the_line_right_across_the_plan()
    {
        var design = Ring();
        var tiles = design.Tiles.Count;

        Assert.Null(RcpStretch.Columns(design, 6, 3));
        RcpAutoRails.Apply(design);

        Assert.Equal(15, Columns(design));
        Assert.Equal(8, Rows(design));

        // Both straights grew: each repeated column holds the 4 tiles of the
        // top and bottom straights - and only those.
        Assert.Equal(tiles + 3 * 4, design.Tiles.Count);

        // The finish line past the cut moved with the far end of the plan.
        Assert.Equal(12, design.Decals.Single().Col);

        // Still one loop: the line traces round it.
        Assert.NotNull(RcpPlan.AutoRoute(design, out _));
        Assert.Empty(RcpPlan.OpenEnds(design));
    }

    /// <summary>
    /// A repeated column kept only each tile's kind and lane, so a blank
    /// tile came back plain (and took a rail) and an arched rail came back
    /// as a straight one (10 Nov 2026, bug hunt 16).
    /// </summary>
    [Fact]
    public void Longer_repeats_each_tiles_piece_with_it()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500 };

        for (var col = 0; col < 3; col++)
        {
            design.Tiles.Add(new RcpTile { Col = col, Row = 0, Lane = 1, Piece = RcpPiece.Arched, Facing = RcpEdge.North });
            design.Tiles.Add(new RcpTile { Col = col, Row = 1, Lane = 1, Piece = RcpPiece.Blank });
            design.Tiles.Add(new RcpTile { Col = col, Row = 2, Lane = 1, Piece = col == 1 ? RcpPiece.Finish : RcpPiece.Rail, Facing = RcpEdge.South });
            design.Walls.Add(new RcpWall { Col = col, Row = 0, Vertical = false });
            design.Walls.Add(new RcpWall { Col = col, Row = 3, Vertical = false });
        }

        Assert.Null(RcpStretch.Columns(design, 2, 2));
        RcpPieces.Adopt(design);

        foreach (var col in new[] { 2, 3 })
        {
            Assert.Equal((RcpPiece.Arched, RcpEdge.North), (design.At(col, 0)!.Piece, design.At(col, 0)!.Facing));
            Assert.Equal(RcpPiece.Blank, design.At(col, 1)!.Piece);

            // One finish line: its copies are ordinary rails.
            Assert.Equal((RcpPiece.Rail, RcpEdge.South), (design.At(col, 2)!.Piece, design.At(col, 2)!.Facing));
        }

        Assert.Single(design.Tiles, t => t.Piece == RcpPiece.Finish);
        Assert.Equal(RcpPiece.Finish, design.At(1, 2)!.Piece);
    }

    [Fact]
    public void Shorter_takes_the_columns_inside_the_line_out_and_closes_the_gap()
    {
        var design = Ring();

        Assert.Null(RcpStretch.Columns(design, 7, -2));
        RcpAutoRails.Apply(design);

        Assert.Equal(10, Columns(design));
        Assert.Equal(7, design.Decals.Single().Col);
        Assert.NotNull(RcpPlan.AutoRoute(design, out _));
        Assert.Empty(RcpPlan.OpenEnds(design));
    }

    [Fact]
    public void Rows_stretch_the_same_way_down_the_plan()
    {
        var design = Ring();

        Assert.Null(RcpStretch.Rows(design, 4, 2));
        RcpAutoRails.Apply(design);

        Assert.Equal(12, Columns(design));
        Assert.Equal(10, Rows(design));
        Assert.Equal(0, design.Decals.Single().Row);
        Assert.NotNull(RcpPlan.AutoRoute(design, out _));
        Assert.Empty(RcpPlan.OpenEnds(design));
    }

    [Fact]
    public void A_stretch_of_rows_and_back_leaves_the_plan_exactly_as_it_was()
    {
        var design = Ring();
        var before = Describe(design);

        Assert.Null(RcpStretch.Rows(design, 4, 2));
        Assert.Null(RcpStretch.Rows(design, 6, -2));

        Assert.Equal(before, Describe(design));
    }

    private static string Describe(RcpTrackDesign design) => string.Join(";",
        design.Tiles.OrderBy(t => t.Row).ThenBy(t => t.Col).Select(t => $"t{t.Col},{t.Row}")
            .Concat(design.Walls.OrderBy(w => w.Row).ThenBy(w => w.Col).ThenBy(w => w.Vertical).Select(w => $"w{w.Col},{w.Row},{w.Vertical},{w.Double}"))
            .Concat(design.Curves.OrderBy(c => c.Row).ThenBy(c => c.Col).Select(c => $"c{c.Col},{c.Row},{c.Quadrant},{c.Radius}"))
            .Concat(design.Decals.Select(d => $"d{d.Col},{d.Row},{d.Kind},{d.Turns}"))
            .Concat(design.Tiles.OrderBy(t => t.Row).ThenBy(t => t.Col).Select(t => $"l{t.Lane},{t.Label}"))
            .Concat(design.Walls.OrderBy(w => w.Row).ThenBy(w => w.Col).ThenBy(w => w.Vertical).Select(w => $"a{w.Auto}"))
            .Concat(design.Route.Select(p => $"r{p.X},{p.Y}")));

    [Fact]
    public void A_cut_through_the_middle_of_a_hand_turn_is_refused_and_changes_nothing()
    {
        var design = new RcpTrackDesign();

        for (var col = 0; col < 6; col++)
        {
            design.Tiles.Add(new RcpTile { Col = col, Row = 0 });
        }

        // A two-tile turn from grid point (1,0) sweeping east across columns 1 and 2.
        design.Curves.Add(new RcpCurve { Col = 1, Row = 0, Quadrant = RcpQuadrant.SouthEast, Radius = 2 });
        var before = Describe(design);

        var why = RcpStretch.Columns(design, 2, 1);

        Assert.NotNull(why);
        Assert.Contains("turn rail", why);
        Assert.Equal(before, Describe(design));

        // Beside it is fine.
        Assert.Null(RcpStretch.Columns(design, 4, 1));
    }

    [Fact]
    public void A_tiles_name_is_not_repeated_by_a_stretch()
    {
        var design = Ring();
        design.At(5, 0)!.Label = "Pit";

        RcpStretch.Columns(design, 6, 3);

        Assert.Single(design.Tiles, t => t.Label == "Pit");
    }

    [Fact]
    public void The_stretch_handle_dragged_out_makes_the_straights_longer_in_one_undo_step()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new RcpTrackEditorForm(Ring(), "Club Hall", 11.0);
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;
            var board = (RcpTrackCanvas)typeof(RcpTrackEditorForm).GetField("_canvas", Any)!.GetValue(form)!;

            board.Size = new Size(1200, 800);
            board.Tool = RcpTool.Select;

            // Part of the top straight: columns 4 to 6, both rows of the lane.
            typeof(RcpTrackCanvas).GetField("_selection", Any)!.SetValue(board, RcpSelection.In(board.Design, new Rectangle(4, 0, 3, 2)));

            var handle = (RectangleF)typeof(RcpTrackCanvas).GetMethod("StretchHandle", Any)!.Invoke(board, new object[] { true })!;
            var scale = (float)typeof(RcpTrackCanvas).GetField("_scale", Any)!.GetValue(board)!;
            var at = new Point((int)(handle.X + handle.Width / 2), (int)(handle.Y + handle.Height / 2));

            void Mouse(string name, Point where) =>
                typeof(RcpTrackCanvas).GetMethod(name, Any)!.Invoke(board, new object[] { new MouseEventArgs(MouseButtons.Left, 1, where.X, where.Y, 0) });

            var tiles = board.Design.Tiles.Count;

            Mouse("OnMouseDown", at);
            Mouse("OnMouseMove", new Point(at.X + (int)(scale * 2), at.Y));
            Mouse("OnMouseUp", new Point(at.X + (int)(scale * 2), at.Y));

            var longer = (columns: Columns(board.Design), added: board.Design.Tiles.Count - tiles, open: RcpPlan.OpenEnds(board.Design).Count);

            board.Undo();

            return (longer, back: Columns(board.Design));
        }, TimeSpan.FromSeconds(30), () => "stretching in the Track Designer");

        Assert.Equal(14, seen.longer.columns);

        // Two new columns of the top and bottom straights, two tiles deep each.
        Assert.Equal(2 * 4, seen.longer.added);
        Assert.Equal(0, seen.longer.open);
        Assert.Equal(12, seen.back);
    }

    [Fact]
    public void A_cut_at_or_past_the_floors_edge_is_refused()
    {
        var design = Ring();
        var before = Describe(design);

        Assert.NotNull(RcpStretch.Columns(design, 12, 2));
        Assert.NotNull(RcpStretch.Columns(design, 0, 2));
        Assert.NotNull(RcpStretch.Rows(design, 8, 1));
        Assert.Equal(before, Describe(design));
    }

    /// <summary>A hand-railed L: a turn at the top left, a straight along the top, a straight down the left.</summary>
    private static RcpTrackDesign HandL(RcpQuadrant quadrant, int col)
    {
        var design = new RcpTrackDesign();

        for (var c = 0; c < 8; c++)
        {
            for (var r = 0; r < 6; r++)
            {
                design.Tiles.Add(new RcpTile { Col = c, Row = r });
            }
        }

        design.Curves.Add(new RcpCurve { Col = col, Row = 0, Quadrant = quadrant, Radius = 1 });

        for (var c = 1; c < 7; c++)
        {
            design.Walls.Add(new RcpWall { Col = c, Row = 0 });
        }

        return design;
    }

    [Theory]
    [InlineData(RcpQuadrant.SouthEast, 0, 1, 2)]  // its straight starts on the line
    [InlineData(RcpQuadrant.SouthWest, 6, 6, 2)]  // anchored on the line
    [InlineData(RcpQuadrant.SouthEast, 0, 3, -2)] // the strip taken out reaches its end
    public void A_cut_touching_a_hand_turn_at_either_end_is_refused(RcpQuadrant quadrant, int col, int at, int by)
    {
        var design = HandL(quadrant, col);
        var before = Describe(design);

        Assert.Contains("turn rail", RcpStretch.Columns(design, at, by));
        Assert.Equal(before, Describe(design));
    }

    [Fact]
    public void Rows_refuse_a_cut_touching_a_hand_turn_too()
    {
        var design = HandL(RcpQuadrant.SouthEast, 0);

        // The turn's down arm ends on row line 1.
        Assert.Contains("turn rail", RcpStretch.Rows(design, 1, 1));
        Assert.Null(RcpStretch.Rows(design, 3, 1));
    }

    [Fact]
    public void Shorter_keeps_a_divider_or_a_hand_rail_where_two_rails_meet()
    {
        var design = new RcpTrackDesign();

        for (var c = 0; c < 5; c++)
        {
            design.Tiles.Add(new RcpTile { Col = c, Row = 0 });
        }

        design.Walls.Add(new RcpWall { Col = 2, Row = 0, Vertical = true, Auto = true });
        design.Walls.Add(new RcpWall { Col = 3, Row = 0, Vertical = true, Double = true });

        Assert.Null(RcpStretch.Columns(design, 3, -1));

        var wall = Assert.Single(design.Walls);
        Assert.Equal(2, wall.Col);
        Assert.True(wall.Double);
        Assert.False(wall.Auto);
    }

    [Fact]
    public void A_tile_is_named_in_one_undo_step_on_one_line_and_not_too_long()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new RcpTrackEditorForm(Ring(), "Club Hall", 11.0);
            var board = (RcpTrackCanvas)typeof(RcpTrackEditorForm)
                .GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form)!;

            board.SetTileLabel(new Point(3, 0), "  Pit\r\nlane " + new string('x', 80));
            var named = board.Design.At(3, 0)!.Label;

            // Off the floor: nothing.
            board.SetTileLabel(new Point(5, 4), "Nowhere");

            board.Undo();

            return (named, back: board.Design.At(3, 0)!.Label);
        }, TimeSpan.FromSeconds(30), () => "naming a tile");

        Assert.StartsWith("Pit lane x", seen.named);
        Assert.Equal(RcpTrackCanvas.MostLabel, seen.named.Length);
        Assert.Equal(string.Empty, seen.back);
    }

    [Fact]
    public void Named_tiles_go_on_the_build_list()
    {
        var design = Ring();
        design.At(3, 0)!.Label = "Pit lane";

        var markings = RcpBuildList.For(design)[^1];

        Assert.Contains(markings.Lines, l => l == "Named tiles: D1 Pit lane.");
    }

    [Fact]
    public void Nothing_on_the_plan_changes_from_the_keys_while_it_is_seen_angled_and_a_tool_goes_back_to_the_plan()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new RcpTrackEditorForm(Ring(), "Club Hall", 11.0);
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;
            var board = (RcpTrackCanvas)typeof(RcpTrackEditorForm).GetField("_canvas", Any)!.GetValue(form)!;

            board.Tool = RcpTool.Select;
            board.SelectEverything();
            board.ToggleAngled();

            var tiles = board.Design.Tiles.Count;
            typeof(RcpTrackCanvas).GetMethod("OnKeyDown", Any)!.Invoke(board, new object[] { new KeyEventArgs(Keys.Delete) });
            var afterDelete = board.Design.Tiles.Count;

            typeof(RcpTrackEditorForm).GetMethod("SetTool", Any)!.Invoke(form, new object[] { RcpTool.Floor });

            return (tiles, afterDelete, angled: board.Angled);
        }, TimeSpan.FromSeconds(30), () => "the angled view");

        Assert.Equal(seen.tiles, seen.afterDelete);
        Assert.False(seen.angled);
    }

    [Fact]
    public void The_angled_view_draws_a_plan_of_markings_alone_and_an_empty_one_without_failing()
    {
        using var bitmap = new Bitmap(400, 300);
        using var g = Graphics.FromImage(bitmap);

        var marks = new RcpTrackDesign();
        marks.Decals.Add(new RcpDecal { Col = 3, Row = 2, Kind = RcpDecalKind.FinishLine });

        RcpAngledView.Draw(g, marks, bitmap.Size, RcpAngle.Start);
        RcpAngledView.Draw(g, new RcpTrackDesign(), bitmap.Size, RcpAngle.Start);
        RcpAngledView.Draw(g, Ring(), bitmap.Size, new RcpAngle(0, 0, 4), (new PointF(3, 0.5f), 90f));

        // The middle of the finish line lands inside the picture.
        var camera = RcpAngledView.CameraFor(marks, bitmap.Size, RcpAngle.Start);
        var middle = camera.Project(3.5f, 2.5f, 0)!.Value.At;

        Assert.InRange(middle.X, 0, 400);
        Assert.InRange(middle.Y, 0, 300);
    }
}
