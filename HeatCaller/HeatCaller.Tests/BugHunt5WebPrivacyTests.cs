using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 5: what the spectator board gives away.
///
/// /api/state is served to any phone on the club's Wi-Fi with no PIN and no
/// gate - it is the spectator board, and that is the point of it. But the LAN
/// copy of that document keyed everything by the driver's transponder: an id
/// on each standings row, an id on each car on the track map, and a "people"
/// list of {id, name} for everyone on the board AND everyone gridded in every
/// race still to come. So one unauthenticated request handed over a
/// transponder next to a full name, for the whole night's entry - the exact
/// mapping /api/roster spends the check-in PIN and a per-run HMAC id to keep
/// off the wire (see RosterView: a transponder beside a name follows somebody
/// from meeting to meeting, and is what a spoofed decoder pass needs).
///
/// Fixed by keying the board, the map, the cards, the call-ups and the follow
/// list by the same opaque RosterId the roster already uses. The pages only
/// ever match one id in a document against another in the same document, so
/// the only consumer that cared which id it was is the broadcast page, which
/// told a ghost from a real car by the GHOST- on its id and now reads a flag
/// on the row instead.
/// </summary>
public sealed class BugHunt5WebPrivacyTests : IDisposable
{
    /// <summary>
    /// The driver whose transponder must not leave the laptop.
    ///
    /// It has letters in it so that the "nowhere in the document" test below
    /// cannot be fooled either way: a run of digits could turn up inside a
    /// lap time by chance, and an upper-case needle cannot match the
    /// lower-case hex an opaque id is made of.
    /// </summary>
    private const string Transponder = "A4821B";

    private const string DriverName = "Priya Raman";

    private const string Ghost = "GHOST-AAAAAA";

    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- web-6: the board nobody has to log in to --------------------------------------------

    [Fact]
    public void The_live_state_any_phone_can_fetch_never_carries_a_driver_s_transponder()
    {
        var document = Board().AsObject();

        // The chosen leaderboard columns are the club's own business - a club
        // that picks the Transponder column for the board on the wall has
        // asked for it, and the relay copy drops it whatever they picked. The
        // ids are not a choice, so they are what this looks at.
        foreach (var row in document["standings"]!.AsArray())
        {
            row!.AsObject().Remove("cells");
        }

        Assert.DoesNotContain(Transponder, document.ToJsonString());
    }

    [Fact]
    public void Everyone_who_can_be_followed_is_named_without_their_transponder()
    {
        var document = Board().AsObject();
        var people = AsArray(document, "people");

        var mine = people.Single(p => p!["name"]!.GetValue<string>() == DriverName);

        // The name is the whole point of the list - it is the picker on the
        // phone. What went with it is what had to change.
        Assert.NotEqual(Transponder, mine!["id"]!.GetValue<string>());
        Assert.Equal(16, mine["id"]!.GetValue<string>().Length);
    }

    [Fact]
    public void The_board_the_map_and_the_follow_list_all_call_one_driver_by_the_same_id()
    {
        var document = Board().AsObject();

        var row = AsArray(document, "standings")
            .Single(s => s!["name"]!.GetValue<string>() == DriverName)!;

        var id = row["id"]!.GetValue<string>();

        Assert.NotEqual(Transponder, id);

        // The page picks its dot out of the map by the id on the row, and its
        // card out of "cards" the same way. An id changed in one place and not
        // the others is a board whose rows and dots stop matching up mid-race,
        // which is worse than the leak it was fixing.
        Assert.Contains(id, AsArray(document, "map", "cars").Select(c => c!["id"]!.GetValue<string>()));
        Assert.NotNull(document["cards"]![id]);

        // And it is the name, not the id, that the follow list shows.
        var person = AsArray(document, "people").Single(p => p!["id"]!.GetValue<string>() == id)!;
        Assert.Equal(DriverName, person["name"]!.GetValue<string>());
    }

    [Fact]
    public void A_driver_s_id_does_not_change_between_one_poll_and_the_next()
    {
        var server = Server();

        var first = Ids(JsonNode.Parse(server.BuildStateJson())!.AsObject());
        var again = Ids(JsonNode.Parse(server.BuildStateJson())!.AsObject());

        Assert.NotEmpty(first);
        Assert.Equal(first, again);
    }

    [Fact]
    public void A_ghost_s_row_says_so_now_that_its_id_no_longer_starts_with_ghost()
    {
        var rows = AsArray(Board().AsObject(), "standings");

        var real = rows.Single(s => s!["name"]!.GetValue<string>() == DriverName)!;
        var ghost = rows.Single(s => s!["name"]!.GetValue<string>() == Ghost)!;

        Assert.True(ghost["ghost"]!.GetValue<bool>());
        Assert.Null(real["ghost"]);
    }

    [Fact]
    public void The_broadcast_page_tells_a_ghost_by_its_row_rather_than_by_reading_its_id()
    {
        var page = Server().BuildBroadcastPage();

        // Reading the prefix off an opaque id matches nothing, and a Quick
        // Rival's lap would have taken the fastest-lap graphic on the stream.
        Assert.DoesNotContain("/^GHOST-/i", page);
        Assert.Contains("!s.ghost", page);
    }

    [Fact]
    public void The_relay_s_public_copy_still_carries_no_ids_at_all()
    {
        var relay = JsonNode.Parse(Server().BuildStateJson(abbreviateNames: false, forRelay: true))!.AsObject();

        // The relay copy returns before any of this is added, and must go on
        // doing so: it is a public document with a format the relay knows.
        Assert.Null(relay["people"]);
        Assert.Null(relay["cards"]);
        Assert.Null(relay["map"]);
        Assert.Null(relay["standings"]!.AsArray()[0]!["id"]);
        Assert.DoesNotContain(Transponder, relay.ToJsonString());
    }

    // --- Helpers ----------------------------------------------------------------------------

    /// <summary>The LAN copy of the live state, with the follow view and the map in it.</summary>
    private JsonNode Board()
    {
        var document = JsonNode.Parse(Server().BuildStateJson())!;

        // AddFollowing and AddTrackMap are each wrapped in a catch that
        // serves the board without them, so a throw in either shows up here
        // as a missing section rather than as a failure. Said plainly, so the
        // next reader is not left looking at the assertions below.
        Assert.True(document["people"] is JsonArray, "No follow list in the document - AddFollowing threw.");
        Assert.True(document["map"] is JsonObject, "No track map in the document - AddTrackMap threw.");

        return document;
    }

    /// <summary>A server with a named driver and a ghost on the board, and both of them on the map.</summary>
    private static WebServerService Server()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = Transponder, DriverName = DriverName });

        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);

        Cross(race, Transponder, 11_000);
        Cross(race, Ghost, 12_000);
        Cross(race, Transponder, 23_000);
        Cross(race, Ghost, 24_000);

        // Typed as the property's own list type, so the lambda below hands
        // back exactly the tuple TrackMap asks for.
        IReadOnlyList<TrackPositions.Car> cars = new List<TrackPositions.Car>
        {
            new(Transponder, 1, 0.25f, false, string.Empty),
            new(Ghost, 2, 0.5f, false, string.Empty),
        };

        // One of the app's own looks rather than "match the app", for the
        // reason BugHunt4WebTests gives: that reads the running theme, whose
        // fonts need GDI+, and the broadcast page is the same script either
        // way.
        var settings = new AppSettings { DisplayLook = Looks.MidnightKey };

        return new WebServerService(new RaceTiming(race), profiles, settings: settings)
        {
            TrackMap = () => (TrackLine.From(Square()), cars),
        };
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

    /// <summary>A 10x10-tile square lap with a finish line, so there is a map to draw at all.</summary>
    private static RcpTrackDesign Square()
    {
        var design = new RcpTrackDesign { RouteClosed = true };

        foreach (var (x, y) in new[] { (0f, 0f), (10f, 0f), (10f, 10f), (0f, 10f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        design.Decals.Add(new RcpDecal { Kind = RcpDecalKind.FinishLine, Col = 4, Row = 0 });

        return design;
    }

    private static JsonArray AsArray(JsonObject document, string name) => document[name]!.AsArray();

    private static JsonArray AsArray(JsonObject document, string name, string inner) =>
        document[name]![inner]!.AsArray();

    private static List<string> Ids(JsonObject document) =>
        AsArray(document, "standings").Select(s => s!["id"]!.GetValue<string>()).ToList();
}
