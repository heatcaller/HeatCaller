namespace HeatCaller.Tests;

/// <summary>
/// What the club owns, and the corner tiles a plan needs (Track Designer
/// overhaul, Phase 4, 25 Oct 2026).
/// </summary>
public sealed class RcpStockTests
{
    /// <summary>A two-tile ring lane, 10 x 8: four bends, each an outer curve tile and an inner corner tile.</summary>
    private static RcpTrackDesign TwoWideRing()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500 };

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(9, 7), 2)!)
        {
            design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Lane = 1 });
        }

        RcpAutoRails.Apply(design);
        return design;
    }

    [Fact]
    public void A_bend_in_a_two_tile_lane_takes_an_outer_curve_tile_and_an_inner_corner_tile()
    {
        var design = TwoWideRing();
        var (outer, inner) = RcpPlan.CornerTiles(design);

        Assert.Equal(4, outer.Count);
        Assert.Equal(4, inner.Count);

        // Top-left bend: the outer curve tile in the corner, the inner
        // corner tile diagonally inside it.
        Assert.Contains((0, 0), outer);
        Assert.Contains((1, 1), inner);

        var used = RcpPlan.Count(design);

        Assert.Equal(4, used.OuterCurveTiles);
        Assert.Equal(4, used.InnerCornerTiles);
        Assert.Equal(used.Tiles - 8, used.PlainTiles);
    }

    [Fact]
    public void A_plan_with_no_turns_needs_no_outer_curve_tiles()
    {
        var design = TwoWideRing();
        design.LaneTurnRadius = 0;
        RcpAutoRails.Apply(design);

        var used = RcpPlan.Count(design);

        Assert.Equal(0, used.OuterCurveTiles);

        // Still a bend on the inside of each corner.
        Assert.Equal(4, used.InnerCornerTiles);
    }

    [Fact]
    public void Sets_and_loose_parts_add_up()
    {
        var stock = new RcpStock();
        stock.Sets["50cm Wide-L / Oblong"] = 2;
        stock.Sets["50cm Oval"] = 1;
        stock.Sets["30cm Mini-96"] = 1;

        var loose = stock.LooseFor(500);
        loose.Tiles = 10;
        loose.OuterCurveTiles = 2;
        loose.StraightRails = 5;
        loose.EndCaps = 8;

        var owned = stock.OwnedFor(500);

        // Two Wide-Ls of 40 tiles, an Oval of 28, and 12 loose; the 30 cm
        // box counts only toward 30 cm plans.
        Assert.Equal(2 * 40 + 28 + 10 + 2, owned.Tiles);
        Assert.Equal(2 * 12 + 2, owned.OuterCurveTiles);
        Assert.Equal(2 * 12, owned.InnerCornerTiles);
        Assert.Equal(2 * 16 + 5, owned.StraightRails);
        Assert.Equal(2 * 12, owned.TurnRails);
        Assert.Equal(2 * 12, owned.InnerCornerPieces);
        Assert.Equal(8, owned.EndCaps);

        // The Oval's parts aren't published: its tiles count, nothing else.
        Assert.Equal(new[] { "50cm Oval" }, owned.Unlisted);

        Assert.Equal(96, stock.OwnedFor(300).Tiles);
    }

    [Fact]
    public void A_plan_is_checked_part_by_part_and_short_parts_are_said()
    {
        var used = RcpPlan.Count(TwoWideRing());

        var stock = new RcpStock();
        var loose = stock.LooseFor(500);
        loose.Tiles = 100;
        loose.OuterCurveTiles = 3;
        loose.InnerCornerTiles = 4;
        loose.StraightRails = 100;
        loose.TurnRails = 4;
        loose.InnerCornerPieces = 4;

        var lines = stock.Check(used, 500);

        var outer = Assert.Single(lines, l => l.Part == "Outer curve tiles");
        Assert.True(outer.Short);
        Assert.Equal(-1, outer.Left);

        Assert.Equal(new[] { "Outer curve tiles" }, lines.Where(l => l.Short).Select(l => l.Part).ToArray());

        // No end caps counted, so they aren't checked.
        Assert.DoesNotContain(lines, l => l.Part == "End caps");
    }

    [Fact]
    public void Nothing_entered_is_never_short()
    {
        var used = RcpPlan.Count(TwoWideRing());

        Assert.Empty(new RcpStock().Check(used, 500));

        // Owning only 30 cm parts says nothing about a 50 cm plan.
        var other = new RcpStock();
        other.Sets["30cm Mini-96"] = 3;
        Assert.Empty(other.Check(used, 500));
    }

    [Fact]
    public void A_kit_too_small_in_corner_tiles_is_caught()
    {
        // A plan needing 14 outer curve tiles, where the Wide-L box has 12:
        // it has tiles enough in all, but not of that kind.
        var design = new RcpTrackDesign { TileSizeMm = 500 };

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(9, 7), 2)!)
        {
            design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Lane = 1 });
        }

        var used = RcpPlan.Count(design);
        used.OuterCurveTiles = 14;

        var kit = RcpKit.ByName("50cm Wide-L / Oblong")!;

        Assert.Contains(used.Missing(kit), line => line.StartsWith("2 more outer curve tiles"));
    }

    [Fact]
    public void A_cupboard_from_a_hand_edited_file_is_tidied()
    {
        var stock = new RcpStock
        {
            Sets = { ["50cm Oval"] = -3, [""] = 2 },
            Loose =
            {
                new RcpLooseParts { TileSizeMm = 500, Tiles = 4 },
                new RcpLooseParts { TileSizeMm = 500, Tiles = 6, EndCaps = -1 },
            },
        };

        var tidied = stock.Tidied();

        Assert.Empty(tidied.Sets);
        var loose = Assert.Single(tidied.Loose);
        Assert.Equal(10, loose.Tiles);
        Assert.Equal(0, loose.EndCaps);
    }

    [Fact]
    public void A_box_RCP_lists_only_tiles_for_is_never_called_short_of_its_other_parts()
    {
        // RCP's own 50cm Oval layout, against one 50cm Oval box.
        var design = RcpStockLayout.Build("50cm Oval")!;
        var stock = new RcpStock();
        stock.Sets["50cm Oval"] = 1;

        var lines = stock.Check(RcpPlan.Count(design), design.TileSizeMm);

        Assert.DoesNotContain(lines, l => l.Short);
        Assert.All(lines.Where(l => l.Part != "Tiles, all kinds"), l => Assert.False(l.Known));
    }

    [Fact]
    public void An_inside_turn_is_an_inner_corner_piece_not_a_turn_rail()
    {
        var design = TwoWideRing();

        // A turn put round the island's top-left corner with the Corners tool.
        var step = RcpCorners.Next(design, new Point(2, 2), out _)!;
        RcpCorners.Apply(design, new Point(2, 2), step);
        RcpAutoRails.Apply(design);

        var used = RcpPlan.Count(design);

        Assert.Equal(5, used.TurnRails);
        Assert.Equal(4, used.OuterTurnRails);
        Assert.Equal(4, used.InnerCornerPieces);
    }

    [Fact]
    public void A_whole_floor_railed_inside_its_edge_is_measured_from_the_track()
    {
        // The floor laid whole, a two-tile ring of rails stood inside it, no
        // finish line: measured from the floor's first tile (outside the
        // rails) every outside corner read as an inside one.
        var design = new RcpTrackDesign { TileSizeMm = 500 };

        for (var col = -2; col <= 11; col++)
        {
            for (var row = -2; row <= 9; row++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row });
            }
        }

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(9, 7), 2)!)
        {
            design.At(cell.X, cell.Y)!.Lane = 1;
        }

        RcpAutoRails.Apply(design);

        // Made hand rails on bare floor: no lanes to go by.
        design.Tiles.ForEach(t => t.Lane = 0);
        design.Walls.ForEach(w => w.Auto = false);
        design.Curves.ForEach(c => c.Auto = false);

        var used = RcpPlan.Count(design);

        Assert.Equal(4, used.OuterCurveTiles);
        Assert.Equal(4, used.InnerCornerTiles);
    }
}
