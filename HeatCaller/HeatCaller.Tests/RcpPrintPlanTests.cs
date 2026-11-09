using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Export, print and share (Track Designer overhaul, Phase 7, 25 Oct 2026):
/// the plan prints to scale over the sheets it takes, the build list names
/// its tiles by the printed grid, and the plan opened from a file replaces
/// what is drawn in one undo step.
/// </summary>
public sealed class RcpPrintPlanTests
{
    /// <summary>A two-tile-wide ring of lanes 12 x 8, railed and with its line traced.</summary>
    private static RcpTrackDesign Ring()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500, RoomInFeet = false };

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(11, 7), 2)!)
        {
            design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Lane = 1 });
        }

        RcpAutoRails.Apply(design);
        design.Route = RcpPlan.AutoRoute(design, out _)!.Select(p => new RcpRoutePoint { X = p.X, Y = p.Y }).ToList();
        design.Decals.Add(new RcpDecal { Col = 4, Row = 0, Kind = RcpDecalKind.FinishLine });

        return design;
    }

    [Fact]
    public void At_one_to_twenty_a_fifty_centimetre_tile_is_twenty_five_millimetres()
    {
        Assert.Equal(25, RcpPrintLayout.MmPerTile(500, 20), 6);
        Assert.Equal(15, RcpPrintLayout.MmPerTile(300, 20), 6);
        Assert.Equal("1:20", RcpPrintLayout.RatioText(500, 25));
        Assert.Equal(0, RcpPrintLayout.MmPerTile(500, 0));
    }

    [Fact]
    public void The_sheets_cover_the_plan_exactly_with_no_gaps_and_no_overlap()
    {
        var area = new RectangleF(-0.3f, -0.3f, 12.6f, 8.6f);
        var box = new SizeF(180, 120);

        var sheets = RcpPrintLayout.Sheets(area, 25, box);

        // 12.6 tiles x 25 mm = 315 mm across: two sheets; 8.6 x 25 = 215 down: two.
        Assert.Equal(4, sheets.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, sheets.Select(s => s.Number));
        Assert.Equal((2, 2), RcpPrintLayout.Count(area, 25, box));

        Assert.Equal(area.Width, sheets.Where(s => s.Row == 0).Sum(s => s.Tiles.Width), 3);
        Assert.Equal(area.Height, sheets.Where(s => s.Column == 0).Sum(s => s.Tiles.Height), 3);

        // Each sheet's drawing fits its box at that scale.
        Assert.All(sheets, s => Assert.True(s.Tiles.Width * 25 <= box.Width + 0.01 && s.Tiles.Height * 25 <= box.Height + 0.01));

        // The first sheet of the second row starts where the first row's stops.
        Assert.Equal(sheets[0].Tiles.Bottom, sheets[2].Tiles.Top, 3);
        Assert.Equal(sheets[0].Tiles.Right, sheets[1].Tiles.Left, 3);
    }

    [Fact]
    public void A_plan_that_exactly_fills_a_sheet_takes_one_not_two()
    {
        var area = new RectangleF(0, 0, 8, 4);

        Assert.Equal((1, 1), RcpPrintLayout.Count(area, 25, new SizeF(200, 100)));
        Assert.Single(RcpPrintLayout.Sheets(area, 25, new SizeF(200, 100)));
    }

    [Fact]
    public void Fit_on_one_sheet_is_one_sheet_and_far_too_many_is_none()
    {
        var area = new RectangleF(0, 0, 60, 40);
        var box = new SizeF(250, 160);

        var fit = RcpPrintLayout.FitMmPerTile(area, box);

        Assert.Equal(4, fit, 6);
        Assert.Single(RcpPrintLayout.Sheets(area, fit, box));

        // A hall at 1:10 on a sheet a few centimetres square.
        var (across, down) = RcpPrintLayout.Count(area, 50, new SizeF(20, 20));
        Assert.True(across * down > RcpPrintLayout.MostSheets);
        Assert.Empty(RcpPrintLayout.Sheets(area, 50, new SizeF(20, 20)));

        Assert.Empty(RcpPrintLayout.Sheets(RectangleF.Empty, 25, box));
        Assert.Empty(RcpPrintLayout.Sheets(area, 0, box));
    }

    [Fact]
    public void The_scale_bar_is_the_longest_round_length_that_fits()
    {
        // 25 mm a tile, 2 tiles a metre: 50 mm a metre. 100 mm room: 2 m.
        var (metres, paper) = RcpPrintLayout.ScaleBar(25, 500, 100);

        Assert.Equal(2, metres);
        Assert.Equal(100, paper, 6);

        Assert.Equal(0.5, RcpPrintLayout.ScaleBar(25, 500, 40).Metres);
        Assert.Equal(0, RcpPrintLayout.ScaleBar(25, 500, 10).Metres);
    }

    [Fact]
    public void Columns_are_lettered_as_a_spreadsheet_letters_them()
    {
        Assert.Equal("A", RcpPrintLayout.ColumnName(0));
        Assert.Equal("Z", RcpPrintLayout.ColumnName(25));
        Assert.Equal("AA", RcpPrintLayout.ColumnName(26));
        Assert.Equal("AZ", RcpPrintLayout.ColumnName(51));
        Assert.Equal("BA", RcpPrintLayout.ColumnName(52));
    }

    [Fact]
    public void The_print_area_is_the_plan_with_a_little_room_round_it()
    {
        var area = RcpPrintLayout.Area(Ring());

        Assert.Equal(-RcpPrintLayout.MarginTiles, area.Left, 3);
        Assert.Equal(12 + 2 * RcpPrintLayout.MarginTiles, area.Width, 3);
        Assert.True(RcpPrintLayout.Area(new RcpTrackDesign()).IsEmpty);
    }

    [Fact]
    public void The_build_list_goes_floor_rails_dividers_turns_markings_by_the_printed_grid()
    {
        var design = Ring();
        var steps = RcpBuildList.For(design);

        Assert.Equal("1. Lay the floor", steps[0].Heading);
        Assert.StartsWith("2. Put the rails round it", steps[1].Heading);
        Assert.Contains(steps, s => s.Heading.EndsWith("Put the turns in"));
        Assert.EndsWith("Put the markings down", steps[^1].Heading);

        // The top row: all twelve across, A to L, with outer curve tiles at
        // both ends of a ring with turns.
        var floor = steps[0].Lines;
        Assert.Contains(floor, l => l.StartsWith("Row 1: A to L - 12 tiles"));

        // The middle rows of a two-wide ring are two runs.
        Assert.Contains(floor, l => l.StartsWith("Row 4: A to B, K to L - 4 tiles"));

        // Every floor row is listed, top to bottom.
        Assert.Equal(8, floor.Count(l => l.StartsWith("Row ")));

        Assert.Contains(steps[^1].Lines, l => l == "The finish line at E1.");

        var used = RcpPlan.Count(design);
        Assert.StartsWith($"{used.Tiles} tiles: {used.PlainTiles} plain", floor[0]);
    }

    [Fact]
    public void The_build_list_names_cells_from_the_plans_top_left_wherever_it_is_drawn()
    {
        var design = new RcpTrackDesign();

        design.Tiles.Add(new RcpTile { Col = -5, Row = 3 });
        design.Tiles.Add(new RcpTile { Col = -4, Row = 3 });
        design.Tiles.Add(new RcpTile { Col = -2, Row = 4 });

        var origin = RcpBuildList.Origin(design);

        Assert.Equal((-5, 3), origin);
        Assert.Equal("A1", RcpBuildList.Cell(origin, -5, 3));
        Assert.Equal("D2", RcpBuildList.Cell(origin, -2, 4));

        var floor = RcpBuildList.For(design)[0].Lines;
        Assert.Contains(floor, l => l.StartsWith("Row 1: A to B - 2 tiles"));
        Assert.Contains(floor, l => l.StartsWith("Row 2: D - 1 tile"));
    }

    [Fact]
    public void A_turn_round_an_inside_corner_is_left_to_the_inner_corner_line_as_the_parts_list_counts_it()
    {
        var design = Ring();
        var outside = RcpPlan.Count(design).OuterTurnRails;

        // A turn round the inner corner of the top-left bend: it bulges off
        // the track, into the island.
        design.Curves.Add(new RcpCurve { Col = 2, Row = 2, Quadrant = RcpQuadrant.SouthEast, Radius = 1 });

        Assert.Equal(outside, RcpPlan.Count(design).OuterTurnRails);

        var turns = RcpBuildList.For(design).Single(s => s.Heading.EndsWith("Put the turns in")).Lines;
        var listed = turns.Where(l => l.Contains("turn rail")).Sum(l => int.Parse(l.Split(' ')[0]));

        Assert.Equal(outside, listed);
    }

    [Fact]
    public void Sheets_are_counted_in_plain_english()
    {
        Assert.Equal(new[] { "1st", "2nd", "3rd", "4th", "11th", "12th", "13th", "21st", "22nd", "23rd", "101st", "111th" },
            new[] { 1, 2, 3, 4, 11, 12, 13, 21, 22, 23, 101, 111 }.Select(RcpPlanPrinter.Ordinal));
    }

    [Fact]
    public void An_empty_plan_has_nothing_to_build()
    {
        Assert.Empty(RcpBuildList.For(new RcpTrackDesign()));
    }

    [Fact]
    public void A_list_of_many_places_names_some_and_counts_the_rest()
    {
        var design = new RcpTrackDesign();

        for (var col = 0; col < 30; col++)
        {
            design.Tiles.Add(new RcpTile { Col = col, Row = 0 });
            design.Decals.Add(new RcpDecal { Col = col, Row = 0, Kind = RcpDecalKind.GridBox });
        }

        var markings = RcpBuildList.For(design)[^1].Lines.Single();

        Assert.StartsWith("30 grid boxes at A1, B1", markings);
        Assert.EndsWith($"and {30 - RcpBuildList.MostNamed} more.", markings);
    }

    [Fact]
    public void The_parts_pages_say_what_the_plan_takes_and_how_to_build_it()
    {
        var lines = RcpPlanPrinter.PartsLines(new RcpPlanPrinter.Job(Ring(), "Club Hall", 20, true, null), "Club Hall");

        Assert.True(lines[0].Title);
        Assert.Contains(lines, l => l.Heading && l.Text == "Parts, in 50 cm tiles");
        Assert.Contains(lines, l => l.Text.StartsWith("Plain tiles: "));
        Assert.Contains(lines, l => l.Text.StartsWith("What the club owns isn't entered"));
        Assert.Contains(lines, l => l.Heading && l.Text == "1. Lay the floor");
    }

    [Fact]
    public void Against_what_the_club_owns_a_short_part_is_marked()
    {
        var stock = new RcpStock();
        stock.Loose.Add(new RcpLooseParts { TileSizeMm = 500, Tiles = 10, StraightRails = 500, TurnRails = 50, InnerCornerPieces = 50, OuterCurveTiles = 5, InnerCornerTiles = 5 });

        var lines = RcpPlanPrinter.PartsLines(new RcpPlanPrinter.Job(Ring(), "Club Hall", 20, true, stock), "Club Hall");

        Assert.Contains(lines, l => l.Short && l.Text.StartsWith("Tiles, all kinds:") && l.Text.EndsWith("short"));
        Assert.DoesNotContain(lines, l => l.Text.StartsWith("What the club owns isn't entered"));
    }

    [Fact]
    public void The_drawing_box_is_the_paper_less_margins_bands_and_grid_strip_either_way_round()
    {
        var portrait = RcpPlanPrinter.DrawingBoxMm(new SizeF(215.9f, 279.4f), landscape: false, marginMm: 12.7f);
        var landscape = RcpPlanPrinter.DrawingBoxMm(new SizeF(215.9f, 279.4f), landscape: true, marginMm: 12.7f);

        Assert.True(portrait.Height > portrait.Width);
        Assert.True(landscape.Width > landscape.Height);
        Assert.Equal(279.4f - 25.4f - 5f, landscape.Width, 2);
    }

    [Fact]
    public void A_region_render_is_the_regions_size_at_the_scale_asked()
    {
        using var bitmap = RcpTrackPainter.RenderRegion(Ring(), new RectangleF(0, 0, 6, 4), 50, Color.White);

        Assert.Equal(300, bitmap.Width);
        Assert.Equal(200, bitmap.Height);
    }

    [Fact]
    public void A_plan_file_opened_replaces_the_board_in_one_undo_step_and_keeps_the_room()
    {
        var seen = StaThread.Run(() =>
        {
            var mine = new RcpTrackDesign { TileSizeMm = 300, RoomWidthMm = 9000, RoomDepthMm = 6000 };
            mine.Tiles.Add(new RcpTile { Col = 0, Row = 0 });

            using var form = new RcpTrackEditorForm(mine, "Club Hall", 11.0);
            var board = (RcpTrackCanvas)typeof(RcpTrackEditorForm)
                .GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form)!;

            var file = Ring();
            file.RoomWidthMm = 1000;
            file.RoomDepthMm = 1000;

            var roomFromFile = board.LoadPlanFile(file);
            var opened = (tiles: board.Design.Tiles.Count, tile: board.Design.TileSizeMm, room: board.Design.RoomWidthMm);

            var stamped = board.Design.RouteChangedUtc is not null;

            board.Undo();

            return (roomFromFile, opened, back: board.Design.Tiles.Count, backTile: board.Design.TileSizeMm, stamped, unstamped: board.Design.RouteChangedUtc is null);
        }, TimeSpan.FromSeconds(30), () => "opening a plan file");

        Assert.False(seen.roomFromFile);
        Assert.Equal(Ring().Tiles.Count, seen.opened.tiles);
        Assert.Equal(500, seen.opened.tile);
        Assert.Equal(9000, seen.opened.room);
        Assert.Equal(1, seen.back);
        Assert.Equal(300, seen.backTile);

        // A different track: the laps raced here stop counting - until Undo.
        Assert.True(seen.stamped);
        Assert.True(seen.unstamped);
    }

    [Fact]
    public void The_file_menu_offers_the_picture_print_plan_files_and_clear()
    {
        var items = StaThread.Run(() =>
        {
            using var form = new RcpTrackEditorForm(Ring(), "Club Hall", 11.0);
            var menu = (ContextMenuStrip)typeof(RcpTrackEditorForm)
                .GetField("_exportMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form)!;

            return menu.Items.OfType<ToolStripMenuItem>()
                .Select(i => i.Text + (i.DropDownItems.Count > 0 ? $" ({i.DropDownItems.Count})" : string.Empty))
                .ToList();
        }, TimeSpan.FromSeconds(30), () => "opening the Track Designer");

        Assert.Equal(new[] { "Save picture (4)", "Copy picture", "Print track and parts list...", "Open plan file...", "Save plan file...", "Clear the plan..." }, items);
    }
}
