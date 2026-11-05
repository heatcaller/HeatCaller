using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// The club's sponsors on the relay's Live page, and their views and taps in
/// the sponsor report (5 Nov 2026, Asher: "add sponsorships to the live tab at
/// the botton. then the report from the plan"). See RelaySponsors.
/// </summary>
public sealed class SponsorsOnLiveTests
{
    private static SponsorBooking Hut() => new()
    {
        Id = "s1",
        Name = "Hobby Hut",
        Tagline = "Kits and spares",
        Website = "hobbyhut.example.com",
    };

    private static SponsorBooking Volt() => new() { Id = "s2", Name = "Volt and co", WebName = "Volt & Co." };

    [Fact]
    public void Only_sponsors_switched_on_are_published_with_their_written_name_and_link()
    {
        var off = new SponsorBooking { Id = "s3", Name = "Last Season Ltd", Enabled = false };

        var doc = JsonNode.Parse(RelaySponsors.Document(new[] { Hut(), Volt(), off }, _ => null))!;
        var list = doc["sponsors"]!.AsArray();

        Assert.Equal(new[] { "Hobby Hut", "Volt & Co." }, list.Select(s => s!["name"]!.GetValue<string>()));
        Assert.Equal("https://hobbyhut.example.com/", list[0]!["url"]!.GetValue<string>());
        Assert.Equal(string.Empty, list[1]!["url"]!.GetValue<string>());
        Assert.Equal("Kits and spares", list[0]!["tagline"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("ftp://files.example.com", "")]
    [InlineData("not a site", "")]
    [InlineData("localhost", "")]
    [InlineData("http://shop.example.com/rc", "http://shop.example.com/rc")]
    public void A_website_is_only_ever_an_http_link(string typed, string expected)
    {
        Assert.Equal(expected, RelaySponsors.WebsiteOf(new SponsorBooking { Website = typed }));
    }

    [Fact]
    public void The_relay_s_hours_are_added_up_for_the_race_night_only()
    {
        var night = new DateTime(2026, 11, 5);

        // 8pm local on the night, 2am local after it (still the same night),
        // and 8pm the next evening (another night).
        string Hour(DateTime local) => local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH", System.Globalization.CultureInfo.InvariantCulture);

        var hours = new Dictionary<string, IReadOnlyDictionary<string, (int Seen, int Taps)>>
        {
            [Hour(night.AddHours(20))] = new Dictionary<string, (int, int)> { ["s1"] = (10, 1) },
            [Hour(night.AddHours(26))] = new Dictionary<string, (int, int)> { ["s1"] = (2, 0), ["s2"] = (4, 0) },
            [Hour(night.AddHours(44))] = new Dictionary<string, (int, int)> { ["s1"] = (99, 9) },
        };

        var totals = RelaySponsors.ForTheNight(hours, night);

        Assert.Equal((12, 1), totals["s1"]);
        Assert.Equal((4, 0), totals["s2"]);
    }

    [Fact]
    public void The_relay_s_answer_is_read_and_a_bad_one_is_not()
    {
        var views = RelaySponsors.ParseViews("{\"ok\":true,\"hours\":{\"2026-11-06T01\":{\"s1\":[5,2],\"bad\":[\"x\",1]}}}")!;

        Assert.Equal((5, 2), views["2026-11-06T01"]["s1"]);
        Assert.False(views["2026-11-06T01"].ContainsKey("bad"));
        Assert.Null(RelaySponsors.ParseViews("not json"));
    }

    [Fact]
    public void The_report_says_what_each_sponsor_s_card_on_Live_got()
    {
        var web = new Dictionary<string, (int Seen, int Taps)> { ["s1"] = (42, 3) };

        var report = SponsorReport.ForTheNight(
            new[] { Hut(), Volt() },
            new Dictionary<string, int> { ["s1"] = 2, ["s2"] = 2 },
            SponsorPlacement.Everywhere,
            "Club Night",
            new DateTime(2026, 11, 5),
            webTonight: web);

        Assert.Contains("plus their card on the club's Live page: seen 42 times on phones, 3 taps through to their website", report);
        Assert.Contains("plus their card on the club's Live page: not seen on a phone this night", report);
        Assert.Contains("at most once per phone every half hour", report);
    }

    [Fact]
    public void Without_the_relay_s_counts_the_report_is_as_it_was()
    {
        var report = SponsorReport.ForTheNight(
            new[] { Hut() },
            new Dictionary<string, int> { ["s1"] = 2 },
            SponsorPlacement.Everywhere,
            "Club Night",
            new DateTime(2026, 11, 5));

        Assert.DoesNotContain("Live page", report);
    }
}
