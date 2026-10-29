namespace HeatCaller.Tests;

/// <summary>
/// RELAY NAVIGATION, ROUND 4 (29 Oct 2026, Asher: "Yes, do round 4"): the
/// night email names the club's own page and how Me brings a driver back to
/// their night, and the PIN email says the same PIN opens their own races
/// under Me.
/// </summary>
public sealed class RelayNavRound4Tests
{
    private static readonly string[] Facts = { "Q1 Heat 1 (Stock Buggy): Sam Parker finished 2nd of 6." };

    private const string NightLink = "https://relay.example.com/c/acme-rc/night#d=a1b2c3d4e5f60718293a";

    [Fact]
    public void The_night_email_points_at_Me_and_the_clubs_own_page()
    {
        var body = NightEmail.Build("Sam", "Acme RC", new DateTime(2026, 10, 22), null, Facts, null, NightLink);

        Assert.Contains("Open it once on your phone and Me, on the club's pages, brings you back to it from then on.", body);
        Assert.Contains("Live timing, the running order and results:" + Environment.NewLine + "  https://relay.example.com/c/acme-rc" + Environment.NewLine, body);
        Assert.True(body.IndexOf(NightLink, StringComparison.Ordinal) < body.IndexOf("Live timing", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_night_page_the_email_says_nothing_about_Me_or_the_clubs_page()
    {
        var body = NightEmail.Build("Sam", "Acme RC", new DateTime(2026, 10, 22), null, Facts, null, nightLink: null);

        Assert.DoesNotContain("Me,", body);
        Assert.DoesNotContain("Live timing", body);
        Assert.DoesNotContain("http", body);
    }

    [Fact]
    public void The_PIN_email_says_the_same_PIN_opens_their_own_races_under_Me()
    {
        var body = PinMailer.Body("Sam", "Acme RC", "4821", "https://relay.example.com/c/acme-rc/checkin", null);

        Assert.Contains("  1. Open https://relay.example.com/c/acme-rc/checkin", body);
        Assert.Contains("On race day you can also see your own races: open Me on the club's pages and enter the same PIN.", body);
        Assert.Contains("  https://relay.example.com/c/acme-rc/mynight" + Environment.NewLine, body);
        Assert.Contains("Your phone remembers you after that.", body);
    }

    [Fact]
    public void The_PIN_email_names_no_Me_page_it_cannot_work_out()
    {
        var body = PinMailer.Body("Sam", "Acme RC", "4821", "http://192.168.1.42:8080/", null);

        Assert.DoesNotContain("mynight", body);
        Assert.DoesNotContain("open Me", body);
    }
}
