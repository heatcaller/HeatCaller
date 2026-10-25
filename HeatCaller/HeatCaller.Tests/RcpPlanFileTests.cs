namespace HeatCaller.Tests;

/// <summary>
/// The .hctrack plan file (Track Designer overhaul, Phase 7, 25 Oct 2026):
/// a plan saved comes back as it went, a file from anywhere is mended or
/// refused with why, and nothing about this club's PC goes in it.
/// </summary>
public sealed class RcpPlanFileTests
{
    private const string Head = "{\"Format\":\"HeatCaller track plan\",\"Version\":1,";

    [Fact]
    public void A_plan_saved_and_opened_comes_back_as_it_went()
    {
        var design = new RcpTrackDesign { TileSizeMm = 300, LaneTurnRadius = 2, KitName = "Starter", RoomWidthMm = 6000, RoomDepthMm = 4000 };
        design.Tiles.Add(new RcpTile { Col = 1, Row = 2, Lane = 1, Turns = 1, Label = "Pit" });
        design.Walls.Add(new RcpWall { Col = 1, Row = 2, Vertical = true, Double = true, Auto = true });
        design.Curves.Add(new RcpCurve { Col = 1, Row = 1, Quadrant = RcpQuadrant.SouthWest, Radius = 2, Auto = true });
        design.Decals.Add(new RcpDecal { Col = 1, Row = 2, Kind = RcpDecalKind.FinishLine, Turns = 1 });
        design.Route.Add(new RcpRoutePoint { X = 1.5f, Y = 2.5f });

        var (back, name, problem) = RcpPlanFile.Read(RcpPlanFile.Write(design, "  Club Oval "));

        Assert.Null(problem);
        Assert.Equal("Club Oval", name);
        Assert.NotNull(back);
        Assert.Equal(300, back!.TileSizeMm);
        Assert.Equal(2, back.LaneTurnRadius);
        Assert.Equal("Starter", back.KitName);
        Assert.Equal(6000, back.RoomWidthMm);
        Assert.Equal("Pit", back.Tiles.Single().Label);
        Assert.Equal(1, back.Tiles.Single().Lane);
        Assert.True(back.Walls.Single().Double);
        Assert.Equal(RcpQuadrant.SouthWest, back.Curves.Single().Quadrant);
        Assert.Equal(2f, back.Curves.Single().Radius);
        Assert.Equal(RcpDecalKind.FinishLine, back.Decals.Single().Kind);
        Assert.Equal(1.5f, back.Route.Single().X);
    }

    [Fact]
    public void The_photo_and_when_the_line_changed_stay_on_this_pc()
    {
        var design = new RcpTrackDesign { BackdropPath = @"C:\Users\club\Pictures\hall.jpg", RouteChangedUtc = DateTime.UtcNow };
        design.Tiles.Add(new RcpTile());

        var json = RcpPlanFile.Write(design, "x");

        Assert.DoesNotContain("hall.jpg", json);
        Assert.Equal(string.Empty, RcpPlanFile.Read(json).Design!.BackdropPath);
        Assert.Null(RcpPlanFile.Read(json).Design!.RouteChangedUtc);

        // And not taken from a file that has them anyway.
        var sent = RcpPlanFile.Read(Head + "\"Design\":{\"BackdropPath\":\"C:\\\\x.png\",\"RouteChangedUtc\":\"2026-01-01T00:00:00Z\"}}").Design!;
        Assert.Equal(string.Empty, sent.BackdropPath);
        Assert.Null(sent.RouteChangedUtc);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("{}")]
    [InlineData("{\"Design\":{}}")]
    [InlineData("{\"Format\":\"HeatCaller track plan\",\"Design\":{}}")]
    [InlineData("{\"Format\":\"Something else\",\"Version\":1,\"Design\":{}}")]
    [InlineData(Head + "\"Design\":{\"TileSizeMm\":\"x\"}}")]
    public void Something_that_is_not_a_plan_is_refused(string json)
    {
        var (design, _, problem) = RcpPlanFile.Read(json);

        Assert.Null(design);
        Assert.StartsWith("That file isn't a HeatCaller track plan", problem);
    }

    [Fact]
    public void A_plan_from_a_newer_heatcaller_is_refused_with_why()
    {
        var (design, _, problem) = RcpPlanFile.Read("{\"Format\":\"HeatCaller track plan\",\"Version\":2,\"Design\":{}}");

        Assert.Null(design);
        Assert.Contains("newer HeatCaller", problem);
    }

    [Fact]
    public void Nulls_and_nonsense_are_mended_as_the_clubs_own_file_is()
    {
        var (design, name, problem) = RcpPlanFile.Read(Head
            + "\"Name\":null,\"Design\":{\"Tiles\":null,\"Walls\":[null],\"Curves\":[{\"Quadrant\":9},{\"Radius\":-1}],"
            + "\"Decals\":[{\"Kind\":77}],\"TileSizeMm\":-3,\"LaneTurnRadius\":9,\"KitName\":null,\"BackdropOpacity\":500,"
            + "\"RoomWidthMm\":9e12,\"RoomDepthMm\":4000}}");

        Assert.Null(problem);
        Assert.Equal(string.Empty, name);
        Assert.Empty(design!.Tiles);
        Assert.Empty(design.Walls);
        Assert.Equal(1f, design.Curves.Single().Radius);
        Assert.Empty(design.Decals);
        Assert.Equal(500, design.TileSizeMm);
        Assert.Equal(RcpAutoRails.MostRadius, design.LaneTurnRadius);
        Assert.Equal(string.Empty, design.KitName);
        Assert.Equal(100, design.BackdropOpacity);
        Assert.False(design.HasRoom);
    }

    [Fact]
    public void Two_pieces_in_one_place_become_one_the_last()
    {
        var design = RcpPlanFile.Read(Head + "\"Design\":{\"Tiles\":[{\"Col\":1},{\"Col\":1,\"Lane\":3}]}}").Design!;

        Assert.Equal(3, design.Tiles.Single().Lane);
    }

    [Fact]
    public void A_plan_reaching_thousands_of_tiles_away_is_refused()
    {
        var (design, _, problem) = RcpPlanFile.Read(Head + "\"Design\":{\"Tiles\":[{\"Col\":99999}]}}");

        Assert.Null(design);
        Assert.Contains("thousands of tiles away", problem);
    }

    [Fact]
    public void Repair_fills_nulls_and_changes_no_number_a_newer_heatcaller_may_have_written()
    {
        var design = new RcpTrackDesign { LaneTurnRadius = 3, TileSizeMm = 2500, BackdropOpacity = 120, KitName = null! };
        design.Tiles.Add(new RcpTile { Lane = -1, Turns = 7, Label = null! });
        design.Curves.Add(new RcpCurve { Radius = 12 });

        RcpPlanFile.Repair(design);

        Assert.Equal(3, design.LaneTurnRadius);
        Assert.Equal(2500, design.TileSizeMm);
        Assert.Equal(120, design.BackdropOpacity);
        Assert.Equal(-1, design.Tiles[0].Lane);
        Assert.Equal(7, design.Tiles[0].Turns);
        Assert.Equal(12f, design.Curves[0].Radius);
        Assert.Equal(string.Empty, design.Tiles[0].Label);
        Assert.Equal(string.Empty, design.KitName);
    }

    [Fact]
    public void Two_turns_round_one_point_in_different_quarters_both_come_through()
    {
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile());
        design.Curves.Add(new RcpCurve { Col = 4, Row = 2, Quadrant = RcpQuadrant.NorthWest });
        design.Curves.Add(new RcpCurve { Col = 4, Row = 2, Quadrant = RcpQuadrant.SouthWest });

        Assert.Equal(2, RcpPlanFile.Read(RcpPlanFile.Write(design, "x")).Design!.Curves.Count);
    }

    [Fact]
    public void A_name_or_label_from_a_file_is_one_line_and_not_too_long()
    {
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Label = "Pit\nlane " + new string('x', 200) });

        var (back, name, _) = RcpPlanFile.Read(RcpPlanFile.Write(design, "Club\r\nOval" + new string('y', 500)));

        Assert.Equal(RcpPlanFile.MostName, name.Length);
        Assert.StartsWith("Club Oval", name);
        Assert.Equal(RcpPlanFile.MostLabel, back!.Tiles[0].Label.Length);
        Assert.StartsWith("Pit lane", back.Tiles[0].Label);
    }

    [Fact]
    public void A_room_is_held_to_what_the_room_tab_shows_in_its_own_unit()
    {
        // 300 m is under 500 m but over 500 ft.
        var feet = RcpPlanFile.Read(Head + "\"Design\":{\"RoomInFeet\":true,\"RoomWidthMm\":300000,\"RoomDepthMm\":4000}}").Design!;
        var metres = RcpPlanFile.Read(Head + "\"Design\":{\"RoomInFeet\":false,\"RoomWidthMm\":300000,\"RoomDepthMm\":4000}}").Design!;

        Assert.False(feet.HasRoom);
        Assert.True(metres.HasRoom);
    }

    [Fact]
    public void The_clubs_own_file_keeps_a_piece_of_a_kind_this_version_does_not_know()
    {
        // Repair is what TrackLayoutStore runs on every plan it loads: a
        // marking from a newer HeatCaller must survive an older one opening
        // and saving the club's file.
        var design = new RcpTrackDesign();
        design.Decals.Add(new RcpDecal { Kind = (RcpDecalKind)99 });

        RcpPlanFile.Repair(design);

        Assert.Single(design.Decals);
    }
}
