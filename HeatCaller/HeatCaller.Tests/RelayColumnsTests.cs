using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// The live board's columns on the club's website, picked on Club Website
/// (5 Nov 2026, Asher: "add column picker for the live board on the relay in
/// website settings"). See RelayColumns.
/// </summary>
public sealed class RelayColumnsTests
{
    private static List<string> Keys(IEnumerable<KpiDefinition> columns) => columns.Select(k => k.Key).ToList();

    [Fact]
    public void With_nothing_picked_the_relay_follows_the_Live_Results_page()
    {
        var web = new List<string> { "pos", "driver", "best" };

        Assert.Equal(web, Keys(RelayColumns.Resolve(web, new List<string>(), forRelay: true)));
    }

    [Fact]
    public void A_pick_of_its_own_is_the_relays_and_leaves_the_LAN_page_alone()
    {
        var web = new List<string> { "pos", "driver", "best" };
        var relay = new List<string> { "driver", "laps", "last" };

        Assert.Equal(relay, Keys(RelayColumns.Resolve(web, relay, forRelay: true)));
        Assert.Equal(web, Keys(RelayColumns.Resolve(web, relay, forRelay: false)));
    }

    [Fact]
    public void The_transponder_never_goes_online_and_a_pick_of_only_it_falls_back()
    {
        var web = new List<string> { "pos", "transponder", "driver" };

        Assert.Equal(new[] { "pos", "driver" }, Keys(RelayColumns.Resolve(web, new List<string>(), forRelay: true)));
        Assert.Equal(new[] { "pos", "driver" }, Keys(RelayColumns.Resolve(web, new List<string> { "transponder" }, forRelay: true)));
        Assert.Equal(new[] { "driver", "best" }, Keys(RelayColumns.Resolve(web, new List<string> { "driver", "transponder", "best" }, forRelay: true)));
    }

    [Fact]
    public void Columns_that_no_longer_exist_are_skipped()
    {
        var web = new List<string> { "pos", "driver" };

        Assert.Equal(new[] { "best" }, Keys(RelayColumns.Resolve(web, new List<string> { "gone", "best" }, forRelay: true)));
        Assert.Equal(web, Keys(RelayColumns.Resolve(web, new List<string> { "gone" }, forRelay: true)));
    }

    [Fact]
    public void An_older_settings_file_has_no_pick_and_a_saved_one_comes_back()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"WebKpis\":[\"pos\",\"driver\"]}");
        Assert.NotNull(old);
        Assert.Empty(old!.RelayKpis);

        var saved = JsonSerializer.Serialize(new AppSettings { RelayKpis = new List<string> { "driver", "laps" } });
        Assert.Equal(new[] { "driver", "laps" }, JsonSerializer.Deserialize<AppSettings>(saved)!.RelayKpis);
    }

    [Fact]
    public void The_picker_opens_on_what_the_relay_shows_now() =>
        Assert.Equal(new[] { "pos", "driver" },
            RelayColumns.Current(new List<string> { "pos", "transponder", "driver" }, new List<string>()));
}
