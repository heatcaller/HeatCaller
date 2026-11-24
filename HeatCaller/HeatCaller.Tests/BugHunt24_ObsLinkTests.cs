using System.Net.WebSockets;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), #217: an OBS PC that never answers ran out the
/// connect timeout and the Race video card read "OBS link: The operation was
/// canceled." instead of saying OBS isn't answering.
/// </summary>
public class BugHunt24_ObsLinkTests
{
    private const string NotAnswering = "OBS isn't answering - is it open, with its WebSocket server on?";

    [Fact]
    public void A_connect_that_times_out_is_said_as_obs_not_answering()
    {
        Assert.Equal(NotAnswering, ObsLink.FailureWords(new TaskCanceledException()));
        Assert.Equal(NotAnswering, ObsLink.FailureWords(new OperationCanceledException()));
    }

    [Fact]
    public void The_other_failures_keep_their_words()
    {
        Assert.Equal(NotAnswering, ObsLink.FailureWords(new WebSocketException()));
        Assert.Equal(NotAnswering, ObsLink.FailureWords(new System.Net.Sockets.SocketException()));
        Assert.Equal(NotAnswering, ObsLink.FailureWords(ObsLink.Closed(WebSocketCloseStatus.NormalClosure)));

        var refused = ObsLink.Closed((WebSocketCloseStatus)4009);
        Assert.Equal(refused.Message, ObsLink.FailureWords(refused));
        Assert.Contains("password", ObsLink.FailureWords(refused));
    }
}
