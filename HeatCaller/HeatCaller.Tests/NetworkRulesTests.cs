namespace HeatCaller.Tests;

/// <summary>
/// Two rules about where things may go on a network (25 Sep security audit):
/// the relay's publish token never goes over plain http to a NAME anyone on
/// the Wi-Fi can answer to, and check-in never opens because the laptop's
/// saved PINs could not be unlocked.
/// </summary>
public sealed class NetworkRulesTests
{
    // --- The relay token over plain http -------------------------------------------------

    [Theory]
    [InlineData("http://clubrelay.local")]
    [InlineData("http://clubrelay.lan:8787")]
    [InlineData("http://relay.home.arpa")]
    [InlineData("http://timing-laptop")]
    [InlineData("http://relay.example.com")]
    public void The_token_never_goes_over_plain_http_to_a_name(string root) =>
        Assert.False(RelayPublisher.IsSafeForToken(root));

    [Theory]
    [InlineData("http://localhost:8787")]
    [InlineData("http://127.0.0.1:8787")]
    [InlineData("http://192.168.1.20:8787")]
    [InlineData("http://10.0.0.5")]
    [InlineData("https://clubrelay.local")]
    [InlineData("https://heatcaller-relay.fly.dev")]
    public void It_may_go_over_https_anywhere_or_plain_http_to_this_laptop_or_a_private_address(string root) =>
        Assert.True(RelayPublisher.IsSafeForToken(root));

    [Fact]
    public void A_local_name_typed_without_a_scheme_gets_https_not_a_silent_http()
    {
        Assert.Equal("https://clubrelay.local", RelayPublisher.NormaliseBaseUrl("clubrelay.local"));
        Assert.Equal("http://localhost:8787", RelayPublisher.NormaliseBaseUrl("localhost:8787"));
        Assert.Equal("http://192.168.1.20:8787", RelayPublisher.NormaliseBaseUrl("192.168.1.20:8787"));
    }

    // --- Check-in while the saved PINs are locked -----------------------------------------

    [Fact]
    public void Check_in_stays_shut_while_the_saved_PINs_are_locked_and_no_PIN_is_set()
    {
        Assert.NotNull(WebServerService.CheckInLockedReason(secretsLocked: true, checkInPin: string.Empty));
    }

    [Fact]
    public void A_PIN_entered_again_opens_it_behind_that_PIN()
    {
        Assert.Null(WebServerService.CheckInLockedReason(secretsLocked: true, checkInPin: "2468"));
    }

    [Fact]
    public void A_blank_PIN_the_club_chose_still_means_open()
    {
        Assert.Null(WebServerService.CheckInLockedReason(secretsLocked: false, checkInPin: string.Empty));
    }
}
