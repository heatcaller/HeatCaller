using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// A timed race's length on the relay's copy of the live state (5 Nov 2026,
/// Asher: "on flag card on relay make a progress bar for a timed race as well
/// as the lap number race"). The countdown says only the time left, so the
/// flag card needs the length too to fill a bar.
/// </summary>
public sealed class RelayRaceLengthTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_timed_race_sends_its_length_to_the_relay_only()
    {
        var timing = Race(time: TimeSpan.FromMinutes(5), laps: null);
        var server = new WebServerService(timing, ProfileStore.Load(), settings: new AppSettings());

        var relay = JsonNode.Parse(server.BuildStateJson(abbreviateNames: false, forRelay: true))!;
        Assert.Equal(300, relay["raceSecs"]!.GetValue<int>());
        Assert.EndsWith("REMAINING", relay["countdown"]!.GetValue<string>());

        Assert.Null(JsonNode.Parse(server.BuildStateJson())!["raceSecs"]);
    }

    [Fact]
    public void A_lap_race_sends_no_length_its_countdown_is_laps()
    {
        var timing = Race(time: null, laps: 10);
        var server = new WebServerService(timing, ProfileStore.Load(), settings: new AppSettings());

        var relay = JsonNode.Parse(server.BuildStateJson(abbreviateNames: false, forRelay: true))!;
        Assert.Null(relay["raceSecs"]);
        Assert.EndsWith("LAPS TO GO", relay["countdown"]!.GetValue<string>());
    }

    private static RaceTiming Race(TimeSpan? time, int? laps)
    {
        var timing = new RaceTiming(new RaceControlSession());
        timing.TimeLimit = time;
        timing.LapLimit = laps;
        timing.Stage();
        timing.GreenFlag(0);
        return timing;
    }
}
