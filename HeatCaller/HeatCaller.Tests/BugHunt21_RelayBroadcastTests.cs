namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 21 #26 (15 Nov 2026): the relay now answers the "racing's off"
/// push once it has counted the phones ("queued") and sends after, so the
/// laptop's 8 s wait no longer reports "no push" over a slow send. The
/// laptop says the push is going, and still reads an older relay's "sent".
/// </summary>
public sealed class BugHunt21_RelayBroadcastTests
{
    [Fact]
    public void A_queued_answer_says_the_push_is_going_to_the_phones_counted()
    {
        Assert.Equal(
            "a push is going to 7 phones (5 drivers signed up for alerts).",
            RelayPublisher.BroadcastAnswer(200, "{\"ok\":true,\"badges\":5,\"queued\":7}"));
        Assert.Equal(
            "a push is going to 1 phone (1 driver signed up for alerts).",
            RelayPublisher.BroadcastAnswer(200, "{\"ok\":true,\"badges\":1,\"queued\":1}"));
    }

    [Fact]
    public void Nobody_signed_up_and_an_older_relays_answer_read_as_before()
    {
        Assert.StartsWith("a push went out, but no phone is signed up", RelayPublisher.BroadcastAnswer(200, "{\"ok\":true,\"badges\":0,\"queued\":0}"));
        Assert.Equal(
            "a push went to 7 phones (5 drivers signed up for alerts).",
            RelayPublisher.BroadcastAnswer(200, "{\"ok\":true,\"badges\":5,\"sent\":7,\"failed\":0}"));
    }
}
