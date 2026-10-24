using System.Net;
using HeatCaller;
using Xunit;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): a relay "busy" that took as long as the
/// relay's writer timeout is a paid model call that timed out, not the
/// in-flight cap, and must not be asked for again.
/// </summary>
public class BugHunt10_RelayCommentaryClientTests
{
    private const string Busy = "{\"refusal\":\"busy\",\"error\":\"busy\"}";

    [Fact]
    public void QuickBusyIsAskedAgain()
    {
        Assert.True(RelayCommentaryClient.WorthAskingAgain(HttpStatusCode.TooManyRequests, 1, TimeSpan.FromMilliseconds(200), Busy));
    }

    [Fact]
    public void SlowBusyIsFinal()
    {
        Assert.False(RelayCommentaryClient.WorthAskingAgain(HttpStatusCode.TooManyRequests, 1, TimeSpan.FromSeconds(20), Busy));
    }

    [Fact]
    public void LastAttemptIsFinal()
    {
        Assert.False(RelayCommentaryClient.WorthAskingAgain(HttpStatusCode.TooManyRequests, 8, TimeSpan.FromMilliseconds(200), Busy));
    }

    [Fact]
    public void OtherRefusalsAreFinal()
    {
        Assert.False(RelayCommentaryClient.WorthAskingAgain(HttpStatusCode.TooManyRequests, 1, TimeSpan.FromMilliseconds(200), "{\"refusal\":\"allowance\"}"));
        Assert.False(RelayCommentaryClient.WorthAskingAgain(HttpStatusCode.BadGateway, 1, TimeSpan.FromMilliseconds(200), Busy));
        Assert.False(RelayCommentaryClient.WorthAskingAgain(HttpStatusCode.TooManyRequests, 1, TimeSpan.FromMilliseconds(200), "not json"));
    }
}
