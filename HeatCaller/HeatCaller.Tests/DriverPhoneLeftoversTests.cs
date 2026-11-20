namespace HeatCaller.Tests;

/// <summary>
/// The driver phone plan's leftovers on the laptop (19 Nov 2026): the social
/// links warning, the page counts, the short address check, the time zone
/// pick, the sign-in email line on Mission Control, the poster's addresses
/// and a driver's later races for "Add my races". No window, no network.
/// </summary>
public sealed class DriverPhoneLeftoversTests
{
    [Fact]
    public void The_same_address_for_two_services_is_warned_about()
    {
        var typed = new Dictionary<string, string>
        {
            ["facebook"] = "https://facebook.com/upstateminiz",
            ["website"] = "https://facebook.com/upstateminiz",
        };

        // The website comes first, so Facebook is the repeat.
        Assert.Contains("same address as Website", RelaySocial.SetProblem(typed));
    }

    [Fact]
    public void An_address_on_the_wrong_service_is_warned_about()
    {
        var typed = new Dictionary<string, string> { ["instagram"] = "https://facebook.com/upstateminiz" };
        Assert.Contains("doesn't look like an Instagram address", RelaySocial.SetProblem(typed));
    }

    [Fact]
    public void Links_that_are_fine_say_nothing()
    {
        var typed = new Dictionary<string, string>
        {
            ["facebook"] = "https://facebook.com/upstateminiz",
            ["instagram"] = "@upstateminiz",
            ["website"] = string.Empty,
        };

        Assert.Null(RelaySocial.SetProblem(typed));
    }

    [Fact]
    public void A_repeat_or_a_wrong_service_is_not_sent_to_the_relay()
    {
        var settings = new AppSettings();
        settings.RelaySocialLinks["facebook"] = "https://facebook.com/acme";
        settings.RelaySocialLinks["instagram"] = "https://facebook.com/acme";
        settings.RelaySocialLinks["youtube"] = "https://youtube.com/@acme";

        var sent = System.Text.Json.JsonSerializer.Serialize(RelaySocial.ForSite(settings));

        Assert.Contains("facebook", sent);
        Assert.Contains("youtube", sent);
        Assert.DoesNotContain("instagram", sent);
    }

    [Fact]
    public void The_page_counts_are_read_and_said_in_plain_words()
    {
        var totals = RelayPageCounts.Parse("""
            {"ok":true,"days":{},"last7":{"view_home":10,"view_live":5,"offline_shown":3,"offline_recovered":2,"install_card_shown":4,"install_accepted":1,"alert_opened_race":2,"checkin_tapped":6,"poster_opened":1},"last30":{}}
            """);

        Assert.NotNull(totals);
        var said = RelayPageCounts.Describe(totals)!;
        Assert.Contains("15 pages opened", said);
        Assert.Contains("3 times a phone lost signal (2 back)", said);
        Assert.Contains("1 install from 4 cards shown", said);
        Assert.Contains("2 alerts opened", said);
        Assert.Contains("6 check-ins", said);
        Assert.Contains("1 visit from the poster", said);
        Assert.Contains("nothing says who", said);
    }

    [Fact]
    public void No_counts_yet_say_so_and_an_unreadable_answer_is_nothing()
    {
        Assert.Null(RelayPageCounts.Describe(null));
        Assert.Null(RelayPageCounts.Parse("not json"));
        Assert.Contains("nothing counted yet", RelayPageCounts.Describe(RelayPageCounts.Parse("""{"last7":{},"last30":{}}""")));
    }

    [Theory]
    [InlineData("""{"ok":true,"state":"taken"}""", "Taken")]
    [InlineData("""{"ok":true,"state":"free"}""", "Free")]
    [InlineData("""{"ok":true,"state":"yours"}""", "Yours")]
    [InlineData("""{"ok":true,"state":"bad"}""", "Bad")]
    [InlineData("""{"ok":false}""", "Unknown")]
    [InlineData("<html>", "Unknown")]
    public void The_relays_word_on_a_short_address_is_read(string json, string expected)
    {
        Assert.Equal(Enum.Parse<RelayPublisher.AliasState>(expected), RelayPublisher.ParseAliasState(json));
    }

    [Fact]
    public void A_taken_short_address_is_said_plainly()
    {
        Assert.Contains("taken by another club", RelayPublisher.AliasStateText(RelayPublisher.AliasState.Taken, "acme"));
        Assert.Equal(string.Empty, RelayPublisher.AliasStateText(RelayPublisher.AliasState.Unknown, "acme"));
    }

    [Fact]
    public void The_name_under_the_icon_is_cut_where_the_relay_cuts_it()
    {
        Assert.Equal(("Upstate MZ", false), RelaySettingsControl.NameUnderIcon("Upstate MZ", "Upstate Mini Z Racing"));
        Assert.Equal(("Upstate Mini", true), RelaySettingsControl.NameUnderIcon("", "Upstate Mini Z Racing"));
        Assert.Equal(("Acme RC", false), RelaySettingsControl.NameUnderIcon("  ", "Acme RC"));
    }

    [Fact]
    public void The_poster_counts_its_scans_and_prints_the_short_address()
    {
        var (qr, printed) = RelaySettingsControl.PosterAddresses("https://relay.example/c/horizon-test", "https://relay.example", "upstateminiz");
        Assert.Equal("https://relay.example/c/horizon-test?from=poster", qr);
        Assert.Equal("https://relay.example/c/upstateminiz", printed);

        var (_, plain) = RelaySettingsControl.PosterAddresses("https://relay.example/c/horizon-test", "https://relay.example", "");
        Assert.Equal("https://relay.example/c/horizon-test", plain);
    }

    [Fact]
    public void A_picked_time_zone_is_used_and_an_unknown_one_falls_back_to_this_pc()
    {
        Assert.Equal("America/Chicago", RelayDocuments.ClubTimeZone(RelayDocuments.PickedTimeZone("Central Standard Time")));
        Assert.Equal(TimeZoneInfo.Local.Id, RelayDocuments.PickedTimeZone("Nowhere Standard Time").Id);
        Assert.Equal(TimeZoneInfo.Local.Id, RelayDocuments.PickedTimeZone("").Id);
    }

    [Fact]
    public void A_sign_in_email_that_didnt_send_is_on_Mission_Control_for_six_hours()
    {
        var at = new DateTime(2026, 11, 19, 20, 0, 0, DateTimeKind.Utc);
        var failure = new RelayPublisher.SignInMailFailure(at, "The server refused the password.");

        var line = RelayPublisher.SignInMailProblemText(failure, at.AddMinutes(5));
        Assert.NotNull(line);
        Assert.Contains("sign-in email didn't send", line);
        Assert.Contains("The server refused the password.", line);

        Assert.Null(RelayPublisher.SignInMailProblemText(failure, at.AddHours(7)));
        Assert.Null(RelayPublisher.SignInMailProblemText(null, at));

        var good = new MissionControl.Tile("website", "Website", "Online", "Up to date.", MissionControl.Level.Good);
        var watched = MissionControl.WithSignInMail(good, line);
        Assert.Equal(MissionControl.Level.Watch, watched.Level);
        Assert.Contains("sign-in email", watched.Detail);

        var off = new MissionControl.Tile("website", "Website", "Off", "Results aren't published online.", MissionControl.Level.Off);
        Assert.Same(off, MissionControl.WithSignInMail(off, line));
    }

    [Fact]
    public void A_drivers_later_races_carry_their_estimates_for_Add_my_races()
    {
        var eta = new DateTime(2026, 11, 19, 20, 40, 0, DateTimeKind.Utc);
        var races = new List<UpcomingRace>
        {
            new("Q1 Heat 1", new[] { "A", "B" }, true),
            new("Q1 Heat 2", new[] { "C", "MINE" }, false, EtaUtc: eta),
            new("Q1 Heat 3", new[] { "D" }, false, EtaUtc: eta.AddMinutes(6)),
            new("Q2 Heat 2", new[] { "MINE" }, false, EtaUtc: eta.AddMinutes(24)),
            new("Q3 Heat 2", new[] { "MINE" }, false),
        };

        var later = DriverLive.LaterOf(races, "MINE");

        // Not the next race (that's "next"), not one with no estimate.
        Assert.Single(later);
        Assert.Equal("Q2 Heat 2", later[0]["race"]);
        Assert.Equal("2026-11-19T21:04:00Z", later[0]["eta"]);
        Assert.Empty(DriverLive.LaterOf(races, "NOBODY"));
    }
}
