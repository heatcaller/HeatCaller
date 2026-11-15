using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Stream overlay layouts and presets (14 Nov 2026, plans/obs-overlay-plan.md
/// sections 2 and 3, Asher: "I want multiple presets and the ability to roll
/// your own"). See OverlayLayout and WebServerService.BuildBroadcastPage.
/// </summary>
public sealed class OverlayLayoutTests
{
    private static WebServerService Server() =>
        new(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });

    [Fact]
    public void The_broadcast_preset_places_nothing_so_broadcast_draws_as_it_always_has()
    {
        var broadcast = OverlayLayouts.BuiltIn(OverlayLayouts.BroadcastId)!;

        Assert.Equal(string.Empty, OverlayLayouts.SlotCss(broadcast));
        Assert.All(broadcast.Elements, e => Assert.Equal(OverlayAnchor.Default, e.Anchor));

        var page = Server().BuildBroadcastPage();

        Assert.DoesNotContain("id=\"slot-", page);
        Assert.Contains("<div id=\"tower\" class=\"glass\" style=\"opacity:0\">", page);
        Assert.Contains("rgba(10,12,18,.82)", page);
        Assert.Equal(page, Server().BuildBroadcastPage(broadcast));
    }

    [Fact]
    public void Every_preset_has_an_address_a_name_and_the_mark()
    {
        var ids = OverlayLayouts.BuiltIns.Select(l => l.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains("auto", ids);
        Assert.Contains("lower-third", ids);

        foreach (var layout in OverlayLayouts.BuiltIns.Where(l => l.Id != OverlayLayouts.ResultsId))
        {
            Assert.False(string.IsNullOrWhiteSpace(layout.Name));
            Assert.True(layout.Shows(OverlayElementKind.Mark), layout.Id + " keeps HeatCaller's mark");
            Assert.Matches("^[a-z0-9-]+$", layout.Id);
        }
    }

    [Fact]
    public void A_moved_element_is_written_in_a_slot_of_its_own()
    {
        var page = Server().BuildBroadcastPage(OverlayLayouts.BuiltIn("lower-third"));

        Assert.Contains("<div id=\"slot-ticker\" class=\"slot\"><div id=\"ticker\" class=\"inslot glass fade\">", page);
        Assert.Contains("#slot-ticker{position:absolute;left:50%;bottom:2vw;transform:translateX(-50%) scale(1);", page);
        // The status box sits top right, scaled from its own corner.
        Assert.Contains("#slot-status{position:absolute;right:2vw;top:2vw;transform-origin:top right;transform:scale(1);}", page);
    }

    [Fact]
    public void The_page_reads_which_parts_of_the_night_each_element_shows_in()
    {
        var json = JsonNode.Parse(OverlayLayouts.ScriptJson(OverlayLayouts.BuiltIn(OverlayLayouts.AutoId)!))!;
        var elements = json["elements"]!;

        Assert.Equal("auto", json["id"]!.GetValue<string>());
        Assert.Equal(new[] { "staging" }, elements["grid"]!["during"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(new[] { "racing", "finished" }, elements["tower"]!["during"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(new[] { "between" }, elements["schedule"]!["during"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.True(elements["mark"]!["placed"]!.GetValue<bool>());
    }

    [Fact]
    public void Leaving_a_part_out_is_still_possible_with_hide_on_top_of_the_layout()
    {
        var page = Server().BuildBroadcastPage();

        Assert.Contains("if (hide.includes(part)) return false;", page);
        Assert.Contains("const LAYOUT = {", page);
    }

    [Fact]
    public void A_name_in_the_layout_cannot_close_the_script()
    {
        var layout = OverlayLayouts.BuiltIn(OverlayLayouts.BroadcastId)!.Copy();
        layout.Id = "</script><script>alert(1)</script>";

        var page = Server().BuildBroadcastPage(layout);

        Assert.DoesNotContain("</script><script>alert(1)", page);
    }

    [Fact]
    public void The_battle_cam_tower_shows_four_rows_and_the_battle_still_looks_at_the_top_eight()
    {
        var page = Server().BuildBroadcastPage(OverlayLayouts.BuiltIn("battle"));

        Assert.Contains("\"tower\":{\"on\":true,\"during\":[\"racing\",\"finished\"],\"rows\":4", page);
        Assert.Contains("const SHOWN = (PARTS.tower && PARTS.tower.rows) || 8, TOP = 8", page);
        Assert.Contains("rows.slice(1, TOP)", page);
    }

    [Fact]
    public void The_demo_race_is_made_in_the_page_and_never_asks_for_the_night_s_data()
    {
        var page = Server().BuildBroadcastPage();

        Assert.Contains("const DEMO = params.get('demo') === '1';", page);
        Assert.Contains("if (DEMO) d = demoState();", page);
    }

    [Fact]
    public void Each_new_graphic_is_at_least_the_phone_readable_size()
    {
        var page = Server().BuildBroadcastPage(OverlayLayouts.BuiltIn("lower-third"));

        foreach (var rule in new[] { "#status {", "#ticker {", "#bug {", "#mark {" })
        {
            var at = page.IndexOf(rule, StringComparison.Ordinal);
            Assert.True(at >= 0, rule);
            var size = System.Text.RegularExpressions.Regex.Match(page[at..], @"font-size:([0-9.]+)vw").Groups[1].Value;
            Assert.True(double.Parse(size, System.Globalization.CultureInfo.InvariantCulture) >= 1.15, rule + " " + size);
        }
    }

    [Fact]
    public void An_address_finds_a_preset_then_the_club_s_own_and_nothing_else()
    {
        var mine = new OverlayLayout { Id = "my-stream", Name = "Mine" };
        var server = Server();
        server.CustomOverlay = id => id == "my-stream" ? mine : null;

        Assert.Equal("Lower third", server.OverlayLayoutFor("lower-third")!.Name);
        Assert.Same(mine, server.OverlayLayoutFor("my-stream"));
        Assert.Null(server.OverlayLayoutFor("gone"));

        server.CustomOverlay = _ => throw new IOException("held");
        Assert.Null(server.OverlayLayoutFor("my-stream"));
    }

    [Fact]
    public void A_copy_of_a_preset_is_its_own()
    {
        var preset = OverlayLayouts.BuiltIn("corner")!;
        var copy = preset.Copy();

        copy.Elements[0].Anchor = OverlayAnchor.BottomLeft;

        Assert.Equal(OverlayAnchor.TopRight, preset.Elements[0].Anchor);
    }
}
