using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Spoken laps on My Night (29 Oct 2026, round two of the relay's "Lap times
/// in my ear"). The driver's own night page finds their row on the relay's
/// public board by a tag, reads the gap to the car ahead off the row, and
/// says how each lap went against their target. So the laptop now puts:
///  - on each row of the relay's public board, an opaque "tag" (never on a
///    ghost) and the "ahead" gap (never on the leader);
///  - in each driver's private live entry, "cars": the tags of their own
///    transponders, retired ones left out, with the target lap when race
///    control set one.
/// </summary>
public sealed class SpokenLaps_MyNightTests : IDisposable
{
    private const string Transponder = "A4821B";
    private const string DriverName = "Priya Raman";
    private const string Ghost = "GHOST-AAAAAA";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- the relay's public board --------------------------------------------------------

    [Fact]
    public void Each_real_row_on_the_relay_board_carries_a_tag_that_is_not_its_transponder()
    {
        var server = Server();
        var rows = RelayRows(server);

        // The driver crossed first both times, so leads; the ghost is second.
        var tag = rows[0]!["tag"]!.GetValue<string>();

        Assert.Equal(16, tag.Length);
        Assert.Matches("^[0-9a-f]{16}$", tag);
        Assert.Equal(server.RelayRowTag(Transponder), tag);
        Assert.DoesNotContain(Transponder, rows.ToJsonString());
    }

    [Fact]
    public void A_ghost_gets_no_tag()
    {
        var rows = RelayRows(Server());

        Assert.NotNull(rows[0]!["tag"]);
        Assert.Null(rows[1]!["tag"]);
    }

    [Fact]
    public void The_tag_holds_from_one_publish_to_the_next()
    {
        var server = Server();

        var first = RelayRows(server).Select(r => r!["tag"]?.GetValue<string>()).ToList();
        var again = RelayRows(server).Select(r => r!["tag"]?.GetValue<string>()).ToList();

        Assert.Equal(first, again);
    }

    [Fact]
    public void The_tag_is_not_the_check_in_id()
    {
        // Two keys, so the public board's tag can never be matched against
        // the roster's id for the same driver.
        var server = Server();
        var lan = JsonNode.Parse(server.BuildStateJson())!["standings"]!.AsArray()
            .Single(r => r!["name"]!.GetValue<string>() == DriverName)!;

        Assert.NotEqual(lan["id"]!.GetValue<string>(), server.RelayRowTag(Transponder));
    }

    [Fact]
    public void Every_row_but_the_leader_carries_the_gap_to_the_car_ahead()
    {
        var rows = RelayRows(Server());

        Assert.Null(rows[0]!["ahead"]);
        Assert.StartsWith("+", rows[1]!["ahead"]!.GetValue<string>());
    }

    [Fact]
    public void A_blank_transponder_has_no_tag()
    {
        Assert.Null(Server().RelayRowTag(" "));
        Assert.Null(Server().RelayRowTag(null));
    }

    // --- the driver's private live entry -----------------------------------------------

    [Fact]
    public void A_drivers_entry_lists_their_cars_tags_with_any_target_and_no_retired_car()
    {
        var profile = new DriverProfile { Transponder = "1001", DriverName = "Sam Parker" };
        profile.AlternateTransponder = "2002";
        profile.Transponders.Add(new OwnedTransponder { Id = "3003", Retired = true });

        var json = DriverLive.Document(Inputs(profile, rowTagOf: t => "tag" + t, targetOf: t => t == "2002" ? 9_400 : null));

        var cars = Entry(json).GetProperty("cars").EnumerateArray().ToList();

        Assert.Equal(2, cars.Count);
        Assert.Equal("tag1001", cars[0].GetProperty("tag").GetString());
        Assert.False(cars[0].TryGetProperty("target", out _));
        Assert.Equal("tag2002", cars[1].GetProperty("tag").GetString());
        Assert.Equal(9_400, cars[1].GetProperty("target").GetInt64());
        Assert.DoesNotContain("3003", json);
    }

    [Fact]
    public void The_entry_never_carries_a_transponder_for_the_cars()
    {
        var profile = new DriverProfile { Transponder = "1001", DriverName = "Sam Parker" };

        var json = DriverLive.Document(Inputs(profile, rowTagOf: _ => "0123456789abcdef", targetOf: _ => 9_000));

        var car = Entry(json).GetProperty("cars")[0];
        Assert.Equal("0123456789abcdef", car.GetProperty("tag").GetString());
        Assert.DoesNotContain("1001", car.GetRawText());
    }

    [Fact]
    public void No_tags_to_give_means_no_cars_at_all()
    {
        var profile = new DriverProfile { Transponder = "1001", DriverName = "Sam Parker" };

        var json = DriverLive.Document(Inputs(profile, rowTagOf: null, targetOf: _ => 9_000));

        Assert.False(Entry(json).TryGetProperty("cars", out _));
    }

    // --- helpers -------------------------------------------------------------------------

    private static readonly DateTime Now = new(2026, 10, 29, 19, 40, 0, DateTimeKind.Utc);

    private static readonly string BadgeId = 1.ToString("x20");

    private static DriverLive.LiveInputs Inputs(DriverProfile profile, Func<string, string?>? rowTagOf, Func<string, long?>? targetOf) => new()
    {
        NowUtc = Now,
        CheckedIn = new[] { profile.Transponder },
        ProfileOf = t => profile.AllTransponderIds.Contains(t, StringComparer.OrdinalIgnoreCase) ? profile : null,
        BadgeOf = t => string.Equals(t, profile.Transponder, StringComparison.OrdinalIgnoreCase) ? BadgeId : null,
        RowTagOf = rowTagOf,
        TargetOf = targetOf,
    };

    private static JsonElement Entry(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("drivers").GetProperty(BadgeId);

    private static JsonArray RelayRows(WebServerService server) =>
        JsonNode.Parse(server.BuildStateJson(abbreviateNames: false, forRelay: true))!["standings"]!.AsArray();

    /// <summary>A named driver and a ghost on the board, the driver in front.</summary>
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

        var settings = new AppSettings { DisplayLook = Looks.MidnightKey };

        return new WebServerService(new RaceTiming(race), profiles, settings: settings);
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
