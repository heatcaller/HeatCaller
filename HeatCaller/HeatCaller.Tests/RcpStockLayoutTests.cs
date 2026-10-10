namespace HeatCaller.Tests;

/// <summary>
/// The layouts a club can start from.
///
/// The thing worth testing is the half that is supposed to be exact: a
/// preset's floor comes from its kit's published tile count, and its
/// rails have to fit inside the kit's published rail counts. A preset
/// that misses either is wrong by construction - it is telling a club
/// they can build something out of a box that does not hold it.
///
/// This caught a real one. The presets began as reconstructions off
/// RCP's photographs, with tight one-tile corners, and every single one
/// came out over budget - the serpentine wanted 86 straight rails against
/// the 72 in the box. The boxes are far more curve-heavy than a render
/// suggests, so the layouts were re-solved against the parts lists. Which
/// is the whole reason to count the rails here and not just the tiles.
/// </summary>
public sealed class RcpStockLayoutTests
{
    [Fact]
    public void Every_layout_names_a_kit_that_exists()
    {
        Assert.NotEmpty(RcpStockLayout.All);

        foreach (var choice in RcpStockLayout.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(choice.Name));
            Assert.False(string.IsNullOrWhiteSpace(choice.What));
            Assert.NotNull(RcpKit.ByName(choice.KitName));
        }
    }

    [Fact]
    public void Every_layout_uses_exactly_the_tiles_its_kit_comes_with()
    {
        // The whole promise of a preset: lay this out and the box you
        // bought holds it. Off by one tile and it does not.
        foreach (var choice in RcpStockLayout.All)
        {
            var design = RcpStockLayout.Build(choice.Name);
            var kit = RcpKit.ByName(choice.KitName)!;

            Assert.NotNull(design);
            Assert.Equal(kit.TotalTiles, design!.Tiles.Count);
        }
    }

    [Fact]
    public void Every_layout_is_short_of_nothing_in_its_own_kit()
    {
        // Counted the way the editor counts it, against the kit it says
        // it is built from.
        foreach (var choice in RcpStockLayout.All)
        {
            var design = RcpStockLayout.Build(choice.Name)!;
            var kit = RcpKit.ByName(choice.KitName)!;

            // Missing rather than Shortfalls: for a kit whose rails RCP
            // never listed, Shortfalls also says so, and "we cannot check
            // the rails" is not the layout being too big for the box.
            var missing = RcpPlan.Count(design).Missing(kit);

            Assert.True(
                missing.Count == 0,
                $"{choice.Name} needs more than the {kit.Name} holds: {string.Join("; ", missing)}");
        }
    }

    [Fact]
    public void Every_layout_comes_with_its_kit_and_tile_size_already_set()
    {
        foreach (var choice in RcpStockLayout.All)
        {
            var design = RcpStockLayout.Build(choice.Name)!;
            var kit = RcpKit.ByName(choice.KitName)!;

            Assert.Equal(choice.KitName, design.KitName);
            Assert.Equal(kit.TileSizeMm, design.TileSizeMm);
        }
    }

    [Fact]
    public void Every_layout_has_rails_round_it_and_a_start_line_on_it()
    {
        foreach (var choice in RcpStockLayout.All)
        {
            var design = RcpStockLayout.Build(choice.Name)!;

            Assert.NotEmpty(design.Walls);

            // Four single corners round the outside. The radius varies -
            // the curve-heavy sets use two-tile sweepers, which is the
            // only way their straights reach - so what is asserted is
            // that there are four of them and that they sweep a corner of
            // the floor rather than cut across it.
            var corners = design.Curves.Where(c => !c.Double).ToList();

            Assert.Equal(4, corners.Count);

            var cols = design.Tiles.Max(t => t.Col) + 1;
            var rows = design.Tiles.Max(t => t.Row) + 1;

            Assert.Contains(corners, c => c.Col == 0 && c.Row == 0 && c.Quadrant == RcpQuadrant.SouthEast);
            Assert.Contains(corners, c => c.Col == cols && c.Row == 0 && c.Quadrant == RcpQuadrant.SouthWest);
            Assert.Contains(corners, c => c.Col == cols && c.Row == rows && c.Quadrant == RcpQuadrant.NorthWest);
            Assert.Contains(corners, c => c.Col == 0 && c.Row == rows && c.Quadrant == RcpQuadrant.NorthEast);

            Assert.Contains(design.Decals, d => d.Kind == RcpDecalKind.FinishLine);
        }
    }

    [Fact]
    public void A_rail_never_stands_where_there_is_no_floor_to_bolt_it_to()
    {
        // A rail is bolted THROUGH the tile it runs along - it does not
        // stand on its own. A preset that put one past the edge of the
        // floor would be describing a piece that cannot be built.
        foreach (var choice in RcpStockLayout.All)
        {
            var design = RcpStockLayout.Build(choice.Name)!;
            var floor = design.Tiles.Select(t => (t.Col, t.Row)).ToHashSet();

            foreach (var wall in design.Walls)
            {
                // The two cells a seam lies between - at least one has to
                // be floor.
                var (a, b) = wall.Vertical
                    ? ((wall.Col - 1, wall.Row), (wall.Col, wall.Row))
                    : ((wall.Col, wall.Row - 1), (wall.Col, wall.Row));

                Assert.True(
                    floor.Contains(a) || floor.Contains(b),
                    $"{choice.Name} has a rail at ({wall.Col},{wall.Row}) with no tile under either side of it");
            }
        }
    }

    [Fact]
    public void The_two_96_tile_sets_are_the_same_layout_at_two_tile_sizes()
    {
        // RCP photograph both with one picture, and the only difference
        // is whether the tiles are 30cm or 50cm.
        var mini = RcpStockLayout.Build("30cm Mini-96 - serpentine")!;
        var large = RcpStockLayout.Build("50cm Large-96 - serpentine")!;

        Assert.Equal(mini.Tiles.Count, large.Tiles.Count);
        Assert.Equal(mini.Walls.Count, large.Walls.Count);
        Assert.Equal(mini.Curves.Count, large.Curves.Count);

        Assert.Equal(300, mini.TileSizeMm);
        Assert.Equal(500, large.TileSizeMm);
    }

    [Fact]
    public void A_start_line_is_laid_on_a_tile_that_exists()
    {
        // And not in a corner, where the sweeper would be drawn over it.
        foreach (var choice in RcpStockLayout.All)
        {
            var design = RcpStockLayout.Build(choice.Name)!;
            var floor = design.Tiles.Select(t => (t.Col, t.Row)).ToHashSet();

            foreach (var decal in design.Decals)
            {
                Assert.True(
                    floor.Contains((decal.Col, decal.Row)),
                    $"{choice.Name} has a marking at ({decal.Col},{decal.Row}) with no tile under it");
            }
        }
    }

    [Fact]
    public void A_kit_whose_rails_RCP_never_listed_says_so_rather_than_passing_quietly()
    {
        // The 50cm Oval's box has a published tile count and nothing
        // else. Missing() stays empty because there is no number to fail
        // against; Shortfalls() says out loud that it could not look.
        var design = RcpStockLayout.Build("50cm Oval")!;
        var kit = RcpKit.ByName("50cm Oval")!;

        var needs = RcpPlan.Count(design);

        Assert.Empty(needs.Missing(kit));
        Assert.True(needs.RailsUnlisted(kit));
        Assert.Single(needs.Shortfalls(kit));
    }

    // --- Rails need a floor to bolt to -------------------------------------

    [Fact]
    public void A_rail_along_a_tile_edge_has_a_floor_under_it()
    {
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });

        // All four edges of the one tile.
        design.Walls.Add(new RcpWall { Col = 0, Row = 0 });
        design.Walls.Add(new RcpWall { Col = 0, Row = 1 });
        design.Walls.Add(new RcpWall { Col = 0, Row = 0, Vertical = true });
        design.Walls.Add(new RcpWall { Col = 1, Row = 0, Vertical = true });

        Assert.Equal(0, RcpPlan.RailsWithNoFloor(design));
    }

    [Fact]
    public void A_rail_out_past_the_floor_is_counted()
    {
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });

        // Two tiles away, with nothing under either side of it.
        design.Walls.Add(new RcpWall { Col = 5, Row = 5 });

        Assert.Equal(1, RcpPlan.RailsWithNoFloor(design));
    }

    [Fact]
    public void A_rail_between_two_tiles_counts_as_floored()
    {
        // The seam down the middle of the floor - a divider, with track on
        // both sides. Bolted to both tiles, not to neither.
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        design.Tiles.Add(new RcpTile { Col = 1, Row = 0 });

        design.Walls.Add(new RcpWall { Col = 1, Row = 0, Vertical = true, Double = true });

        Assert.Equal(0, RcpPlan.RailsWithNoFloor(design));
    }

    [Fact]
    public void Blocked_out_floor_still_counts_as_something_to_bolt_to()
    {
        // Infield is floor you don't race on, not floor that isn't there -
        // the tile is laid, so a rail along its edge is bolted to a tile.
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0, Kind = RcpTileKind.Infield });

        design.Walls.Add(new RcpWall { Col = 0, Row = 0 });

        Assert.Equal(0, RcpPlan.RailsWithNoFloor(design));
    }

    [Fact]
    public void An_empty_plan_has_nothing_to_complain_about()
    {
        Assert.Equal(0, RcpPlan.RailsWithNoFloor(new RcpTrackDesign()));
    }

    [Fact]
    public void A_name_nothing_knows_builds_nothing_rather_than_throwing()
    {
        Assert.Null(RcpStockLayout.Build("The Nürburgring"));
        Assert.Null(RcpStockLayout.Build(string.Empty));
        Assert.Null(RcpStockLayout.ByName("nope"));
    }

    [Fact]
    public void A_layout_is_found_whatever_case_it_is_asked_for_in()
    {
        Assert.NotNull(RcpStockLayout.ByName("50CM OVAL"));
        Assert.NotNull(RcpStockLayout.ByName("50cm oval"));
    }
}
