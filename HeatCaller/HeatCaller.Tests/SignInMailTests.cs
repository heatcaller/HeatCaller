namespace HeatCaller.Tests;

/// <summary>
/// "Email me a link to my page" (14 Nov 2026, driver phone plan item 17):
/// the laptop's half - collected only while the club has it on and its mail
/// can send, the relay's inbox read narrowly, and the email itself saying
/// what it is and that ignoring it changes nothing.
/// </summary>
public sealed class SignInMailTests
{
    private static AppSettings Mailing(bool on) => new()
    {
        RelayEmailSignInLink = on,
        RelayCheckInEnabled = true,
        PublishDriverNightsToTheRelay = true,
    };

    [Fact]
    public void Is_off_by_default()
    {
        Assert.False(new AppSettings().RelayEmailSignInLink);
        Assert.False(RelayPublisher.SignInMailWanted(new AppSettings()));
    }

    [Fact]
    public void Is_collected_only_while_the_club_has_it_on_and_check_in_and_nights_are_on()
    {
        Assert.False(RelayPublisher.SignInMailWanted(Mailing(on: false)));

        var noNights = Mailing(on: true);
        noNights.PublishDriverNightsToTheRelay = false;
        Assert.False(RelayPublisher.SignInMailWanted(noNights));

        var noCheckIn = Mailing(on: true);
        noCheckIn.RelayCheckInEnabled = false;
        Assert.False(RelayPublisher.SignInMailWanted(noCheckIn));
    }

    [Fact]
    public void Reads_only_well_formed_asks_from_the_inbox()
    {
        var asks = RelayPublisher.ParseSignInMailInbox("""
            {"ok":true,"items":[
              {"n":3,"email":"sam@example.com","at":"2026-11-14T12:00:00Z"},
              {"n":0,"email":"zero@example.com"},
              {"n":4},
              {"n":"5","email":"text@example.com"},
              7
            ]}
            """);

        var one = Assert.Single(asks);
        Assert.Equal(3, one.N);
        Assert.Equal("sam@example.com", one.Email);
        Assert.Empty(RelayPublisher.ParseSignInMailInbox("not json"));
        Assert.Empty(RelayPublisher.ParseSignInMailInbox("""{"items":"nope"}"""));
    }

    [Fact]
    public void The_email_names_the_club_carries_the_link_and_says_ignoring_it_is_fine()
    {
        var (subject, body) = RelayPublisher.SignInMailMessage("Upstate Mini Z", "https://relay.test/c/umz/night#d=0123456789abcdef0123");

        Assert.Equal("Your page at Upstate Mini Z", subject);
        Assert.Contains("https://relay.test/c/umz/night#d=0123456789abcdef0123", body);
        Assert.Contains("you can ignore it", body);
    }
}
