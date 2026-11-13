using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17. #143: a pasted "Me" page link (/c/{club}/mynight - the link
/// the app itself emails to drivers) was not taken back to the relay, so the
/// connection test asked for .../mynight/healthz and failed. #195: the
/// relay's fullest driver/inbox answer was bigger than the megabyte this
/// read of any answer, and an inbox that cannot be read is never emptied.
/// </summary>
public sealed class BugHunt17_RelayPublisherTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Theory]
    [InlineData("https://relay.example.com/c/acme-rc/mynight")]
    [InlineData("https://relay.example.com/c/acme-rc/MyNight/")]
    [InlineData("relay.example.com/c/acme-rc/mynight?pin=1")]
    [InlineData("https://relay.example.com/c/acme-rc/live")]
    public void A_pasted_club_page_link_is_taken_back_to_the_relay(string pasted)
    {
        Assert.Equal("https://relay.example.com", RelayPublisher.NormaliseBaseUrl(pasted));
    }

    [Fact]
    public void A_path_after_the_club_that_is_not_a_relay_page_is_left_alone()
    {
        Assert.Equal(
            "https://example.com/c/acme-rc/somewhere",
            RelayPublisher.NormaliseBaseUrl("https://example.com/c/acme-rc/somewhere"));
    }

    [Fact]
    public void The_fullest_inbox_the_relay_can_hold_fits_in_one_answer()
    {
        // relay/src/driverasks.ts: DRIVER_ASKS_PER_CLUB asks held, the
        // biggest a setup sheet at DRIVER_ASK_SETUP_MAX_BODY, each with its
        // id and a little JSON round it - all given in one driver/inbox answer.
        const long fullest = 200L * (12 * 1024 + 256);

        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });

        var http = (HttpClient)typeof(RelayPublisher)
            .GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(publisher)!;

        Assert.True(http.MaxResponseContentBufferSize > fullest);

        // And still bounded: a wrong address must not be read in whole.
        Assert.Equal(RelayPublisher.MaxAnswerBytes, http.MaxResponseContentBufferSize);
        Assert.True(RelayPublisher.MaxAnswerBytes <= 8 * 1024 * 1024);
    }
}
