using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Driver experience W1 (22 Oct 2026): the club Wi-Fi pages -
/// captions on the OBS overlay and the broadcast graphics (item 9). The
/// follow card's penalties (item 1), call-up timing (item 3) and
/// transponder check (item 4) went with Live Results, and the pit board
/// (item 10) with the phone coach page (13 Nov 2026).
///
/// The LAN state is checked as JSON, the way BugHunt5WebPrivacyTests checks
/// it; the pages are checked by what their scripts say, by what they contain.
/// </summary>
public sealed class DriverExp_W1_WiFiTests : IDisposable
{
    // Letters in it, so it cannot turn up inside a lap time or a hex id by chance.
    private const string Transponder = "W1D7K";

    private const string DriverName = "Sam Parker";

    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- Items 1, 3 and 4: gone with Live Results (13 Nov 2026) --------------------------

    [Fact]
    public void The_lan_state_carries_only_what_the_broadcast_graphics_read()
    {
        var server = Server();
        var state = State(server);
        var id = IdOf(state);

        // Laps and the gap ahead, for the tower and the battle bar - nothing
        // the follow card alone read: no position, last or best lap, gap
        // behind, warnings or penalty time.
        var card = state["cards"]![id]!.AsObject();
        Assert.Equal(new[] { "ahead", "laps" }, card.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal));

        // No laptop clock (it was for the follow card's "about 7 min"), no
        // penalty lines and no transponder sightings.
        Assert.Null(state["now"]);
        Assert.Null(state["pens"]);
        Assert.Null(state["seen"]);
        Assert.Null(state["map"]);

        // Nor the sponsor slides, which only Live Results showed.
        Assert.Null(state["sponsorCount"]);
        Assert.Null(state["sponsorVideos"]);
    }

    [Fact]
    public void The_overlay_is_the_table_and_captions_and_nothing_of_the_follow_view()
    {
        var page = Server().BuildOverlayPage();

        Assert.Contains("<body class=\"overlay\"", page);
        Assert.Contains("id=\"rows\"", page);

        foreach (var gone in new[] { "followPick", "speakBtn", "tcheckBtn", "/api/sponsor", "lapChart", "mapChart", "id=\"champ\"" })
        {
            Assert.DoesNotContain(gone, page);
        }
    }

    // --- Item 9: captions -------------------------------------------------------------------

    [Fact]
    public void The_lan_state_carries_the_last_four_captions_and_none_when_nothing_was_said()
    {
        var server = Server();
        var log = new CaptionLog();
        server.Captions = () => log.Last();

        Assert.Null(State(server)["captions"]);

        for (var i = 1; i <= 6; i++)
        {
            log.Add($"Line {i}");
        }

        var captions = State(server)["captions"]!.AsArray();

        Assert.Equal(WebServerService.CaptionsInState, captions.Count);
        Assert.Equal(6L, captions[^1]!["n"]!.GetValue<long>());
        Assert.Equal("Line 6", captions[^1]!["t"]!.GetValue<string>());
        Assert.Equal("Line 3", captions[0]!["t"]!.GetValue<string>());

        // The relay has its own, redacted, captions document.
        Assert.Null(JsonNode.Parse(server.BuildStateJson(abbreviateNames: false, forRelay: true))!["captions"]);
    }

    [Fact]
    public void The_overlay_and_the_broadcast_graphics_show_captions_only_when_asked()
    {
        var server = Server();

        foreach (var page in new[] { server.BuildOverlayPage(), server.BuildBroadcastPage() })
        {
            Assert.Contains("get('captions') === '1'", page);
            Assert.Contains("id=\"captions\"", page);
            Assert.Contains("const CAPTION_MS = 8000;", page);
            Assert.Contains("showCaptions(d.captions);", page);
            Assert.Contains("newest.n === captionSeen", page);
        }
    }

    // --- Helpers ----------------------------------------------------------------------------

    private static JsonObject State(WebServerService server)
    {
        var document = JsonNode.Parse(server.BuildStateJson())!.AsObject();

        // AddIdsAndGrid is wrapped in a catch that serves the board without
        // it, so a throw in it shows up here rather than as a missing field.
        Assert.True(document["people"] is JsonArray, "No people list in the document - AddIdsAndGrid threw.");

        return document;
    }

    /// <summary>The driver's opaque id, from the people list.</summary>
    private static string IdOf(JsonObject state) =>
        state["people"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == DriverName)!["id"]!.GetValue<string>();

    private static RaceControlSession Racing()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);

        Cross(race, Transponder, 11_000);
        Cross(race, Transponder, 23_000);

        return race;
    }

    private static WebServerService Server(RaceControlSession? race = null)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = Transponder, DriverName = DriverName });

        // One of the app's own looks rather than "match the app", for the
        // reason BugHunt4WebTests gives.
        return new WebServerService(new RaceTiming(race ?? Racing()), profiles, settings: new AppSettings { DisplayLook = Looks.MidnightKey });
    }

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = raceTimeMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });
}
