using System.Net.Sockets;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the network decoders: the socket is set to notice a decoder
/// that has gone without closing (power cut, a dropped switch). Nothing is
/// sent after the greeting, so without this the connection read as
/// Connected for good and the reconnect never ran.
/// </summary>
public sealed class BugHunt17_StreamDecoderClientTests
{
    [Fact]
    public void A_network_decoders_socket_is_checked_for_a_dead_peer_within_seconds()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        StreamDecoderClient.WatchForADeadPeer(socket);

        Assert.NotEqual(0, (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!);
        Assert.Equal(5, (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime)!);
        Assert.Equal(1, (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval)!);
        Assert.Equal(5, (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount)!);
    }
}
