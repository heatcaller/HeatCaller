using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// The last of the stream overlay plan (14 Nov 2026, Asher: "build and fix
/// the rest"): banners in turn, tonight's bests, "presented by" graphics in
/// the report, club-written basics cards, overlaps, the size of a whole
/// layout, and juniors on relay pages when a club shows names in full.
/// </summary>
[Collection(SponsorStreamCollection.Name)]
public sealed class OverlayFinishTests
{
    [Fact]
    public void Several_moments_at_once_are_all_kept_for_the_page_to_show_in_turn()
    {
        var now = new DateTime(2026, 11, 14, 21, 0, 0, DateTimeKind.Utc);
        var feed = new BroadcastMomentFeed(() => now);

        feed.Post(new BroadcastMoment("milestone", "First podium", "Mia Kent · On the Box"));
        feed.Post(new BroadcastMoment("milestone", "Best lap ever", "Sam Trent · New Heights"));
        feed.Post(new BroadcastMoment("lead", "Lead change", "Sam takes the lead"));

        Assert.Equal(new[] { "First podium", "Best lap ever", "Lead change" }, feed.Recent().Select(m => m.Moment.Title));
        Assert.Equal(new[] { 1, 2, 3 }, feed.Recent().Select(m => m.Seq));
        Assert.Equal(new[] { "First podium", "Best lap ever" }, feed.Tonight().Select(m => m.Title));

        now = now.AddSeconds(30);
        Assert.Empty(feed.Recent());
        Assert.Equal(2, feed.Tonight().Count);
    }

    [Fact]
    public void A_presented_by_graphic_counts_once_however_many_sources_say_so()
    {
        SponsorStream.ResetForTests();
        var at = new DateTime(2026, 11, 14, 20, 0, 0, DateTimeKind.Local).ToUniversalTime();

        SponsorStream.GraphicShown("s1", "FastestLap", at);
        SponsorStream.GraphicShown("s1", "FastestLap", at.AddSeconds(1));
        SponsorStream.GraphicShown("s1", "Battle", at.AddSeconds(2));
        SponsorStream.GraphicShown("s1", "FastestLap", at.AddSeconds(40));

        Assert.Equal(3, SponsorStream.GraphicsOn(TonightAtTheTrack.NightOf(at.ToLocalTime()))["s1"]);
    }

    [Fact]
    public void The_report_says_how_many_presented_by_graphics_they_had()
    {
        var hut = new SponsorBooking { Id = "s1", Name = "Hobby Hut" };

        Assert.Equal("  Their logo was on the stream for about 41 minutes, and their name on 23 \"presented by\" graphics.",
            SponsorReport.StreamLine(hut, new Dictionary<string, int> { ["s1"] = 41 * 60 }, new Dictionary<string, int> { ["s1"] = 23 }));
        Assert.Equal("  Their name was on 1 \"presented by\" graphic.",
            SponsorReport.StreamLine(hut, null, new Dictionary<string, int> { ["s1"] = 1 }));
    }

    [Fact]
    public void A_club_s_own_basics_cards_replace_heatcaller_s_and_an_empty_box_brings_them_back()
    {
        var mine = StreamBasics.Cards("Tonight's classes | Junior: under-16s on 1/28 cars\n\njust words\n | \n");

        Assert.Equal(("Tonight's classes", "Junior: under-16s on 1/28 cars"), mine[0]);
        Assert.Equal(("New to RC racing?", "just words"), mine[1]);
        Assert.Equal(2, mine.Count);

        Assert.Same(StreamBasics.BuiltIn, StreamBasics.Cards("   "));
        Assert.DoesNotContain("</script>", StreamBasics.ScriptJson("x | </script><script>alert(1)"));
    }

    [Fact]
    public void The_stream_page_takes_the_club_s_cards_and_adds_tonight_s_bests()
    {
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.MidnightKey, StreamBasics = "Our club | Racing every Friday" });
        var page = server.BuildBroadcastPage(OverlayLayouts.BuiltIn("between"));

        Assert.Contains("const BASICS = [[\"Our club\",\"Racing every Friday\"]];", page);
        Assert.Contains("['Tonight\\u2019s bests',", page);
    }

    [Fact]
    public void No_preset_puts_two_graphics_on_top_of_each_other()
    {
        foreach (var layout in OverlayLayouts.BuiltIns.Where(l => l.Elements.Count > 0))
        {
            Assert.Empty(OverlayOverlap.Warnings(layout, OverlayEditorForm.KindWords));
        }
    }

    [Fact]
    public void Two_graphics_moved_onto_each_other_in_the_same_part_of_the_night_are_warned_of()
    {
        var layout = OverlayLayouts.BuiltIn("corner")!.Copy();
        layout.Element(OverlayElementKind.Sponsor)!.Y = 3;

        var warning = Assert.Single(OverlayOverlap.Warnings(layout, OverlayEditorForm.KindWords));
        Assert.Contains("Corner bug and Sponsor logo overlap", warning);

        // Never both at once: no warning.
        layout.Element(OverlayElementKind.Bug)!.During = OverlayDuring.Racing;
        layout.Element(OverlayElementKind.Sponsor)!.During = OverlayDuring.Between;
        Assert.Empty(OverlayOverlap.Warnings(layout, OverlayEditorForm.KindWords));
    }

    [Fact]
    public void A_layout_made_bigger_or_smaller_scales_every_graphic()
    {
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.MidnightKey });
        var layout = OverlayLayouts.BuiltIn("corner")!.Copy();
        layout.TextScale = 1.3;

        Assert.Contains("body{zoom:1.3}", server.BuildBroadcastPage(layout));
        Assert.DoesNotContain("body{zoom:", server.BuildBroadcastPage(OverlayLayouts.BuiltIn("corner")));
    }

    [Fact]
    public void A_junior_is_shortened_on_relay_pages_even_when_the_club_shows_names_in_full()
    {
        var privacy = PublicPrivacy.From(new[]
        {
            new DriverProfile { Transponder = "1", DriverName = "Mia Kentworth", IsJunior = true },
            new DriverProfile { Transponder = "2", DriverName = "Terry Byteman" },
        });

        var shown = RelayPublisher.DisplayNames(new[] { "Mia Kentworth", "Terry Byteman" }, abbreviate: false, new string?[] { "1", "2" }, privacy);

        Assert.Equal(new[] { "Mia K", "Terry Byteman" }, shown);

        // With shortening on, everybody is shortened as before.
        Assert.Equal(new[] { "Mia K", "Terry B" }, RelayPublisher.DisplayNames(new[] { "Mia Kentworth", "Terry Byteman" }, abbreviate: true, new string?[] { "1", "2" }, privacy));
    }

    [Fact]
    public void Making_a_driver_a_junior_republishes_their_names()
    {
        var before = MainForm.PrivacyKeyOf(new[] { new DriverProfile { Transponder = "1", DriverName = "Mia Kentworth" } });
        var after = MainForm.PrivacyKeyOf(new[] { new DriverProfile { Transponder = "1", DriverName = "Mia Kentworth", IsJunior = true } });

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void The_results_table_overlay_says_provisional_too()
    {
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.MidnightKey });

        Assert.Contains("' - PROVISIONAL'", server.BuildOverlayPage());
    }

    [Fact]
    public void The_stream_says_which_presented_by_graphics_it_showed_on_its_next_poll()
    {
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.MidnightKey });
        var page = server.BuildBroadcastPage();

        Assert.Contains("said.push('shown=' + encodeURIComponent([...graphicsShown].join(',')));", page);
        Assert.Contains("shownBy(d, 'FastestLap');", page);
        Assert.Contains("momentQueue.push(m)", page);
        Assert.True(JsonDocument.Parse(server.BuildStateJson()).RootElement.TryGetProperty("bests", out _));
    }
}
