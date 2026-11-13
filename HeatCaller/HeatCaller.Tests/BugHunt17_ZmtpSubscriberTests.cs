using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): an OpenStint whose PC lost power, or whose
/// network dropped, never closed the connection - so the subscriber waited
/// on a dead socket for ever, the status stayed Connected and nothing
/// reconnected. A publisher that has finished its handshake and then says
/// nothing for too long is now a failed link.
///
/// The publisher here is a plain TcpListener speaking just enough ZMTP by
/// hand, because the point is one that stops talking without closing.
/// </summary>
public sealed class BugHunt17_ZmtpSubscriberTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    [Fact]
    public void A_publisher_that_goes_silent_after_its_handshake_is_a_failed_link()
    {
        var (listener, port) = Listen();
        using var _ = listener;

        using var subscriber = ZmtpSubscriber.Connect("127.0.0.1", port, 3000, silenceLimitMs: 400);
        using var publisher = listener.AcceptTcpClient();
        publisher.GetStream().Write(Handshake());

        // Still connected, nothing closed - only quiet.
        var failure = Assert.Throws<IOException>(() =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            while (watch.Elapsed < Patience)
            {
                subscriber.Receive(100);
            }
        });

        Assert.Contains("nothing from OpenStint", failure.Message, StringComparison.Ordinal);
        Assert.Equal("PUB", subscriber.PeerSocketType);
    }

    [Fact]
    public void A_publisher_that_keeps_talking_is_kept()
    {
        var (listener, port) = Listen();
        using var _ = listener;

        using var subscriber = ZmtpSubscriber.Connect("127.0.0.1", port, 3000, silenceLimitMs: 600);
        using var publisher = listener.AcceptTcpClient();
        var stream = publisher.GetStream();
        stream.Write(Handshake());

        // Well past the limit in all, but never that long between messages.
        var heard = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < 2000)
        {
            stream.Write(Message("S 2000 -41.0 5.08 0 0 3.01"));

            if (subscriber.Receive(100) is not null)
            {
                heard++;
            }

            Thread.Sleep(50);
        }

        Assert.True(heard > 0);
    }

    [Fact]
    public void Silence_before_the_handshake_is_left_to_the_nothing_received_warning()
    {
        var (listener, port) = Listen();
        using var _ = listener;

        using var subscriber = ZmtpSubscriber.Connect("127.0.0.1", port, 3000, silenceLimitMs: 200);
        using var publisher = listener.AcceptTcpClient();

        var watch = System.Diagnostics.Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < 800)
        {
            Assert.Null(subscriber.Receive(100));
        }
    }

    private static (TcpListener Listener, int Port) Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, ((IPEndPoint)listener.LocalEndpoint).Port);
    }

    /// <summary>A publisher's greeting (ZMTP 3.0, NULL) and its READY, "Socket-Type" = "PUB".</summary>
    private static byte[] Handshake()
    {
        var greeting = new byte[64];
        greeting[0] = 0xFF;
        greeting[9] = 0x7F;
        greeting[10] = 3;
        Encoding.ASCII.GetBytes("NULL").CopyTo(greeting, 12);

        var body = new List<byte> { 5 };
        body.AddRange(Encoding.ASCII.GetBytes("READY"));
        body.Add(11);
        body.AddRange(Encoding.ASCII.GetBytes("Socket-Type"));
        body.AddRange(new byte[] { 0, 0, 0, 3 });
        body.AddRange(Encoding.ASCII.GetBytes("PUB"));

        var bytes = new List<byte>(greeting) { 0x04, (byte)body.Count };
        bytes.AddRange(body);
        return bytes.ToArray();
    }

    /// <summary>One short message of a single frame.</summary>
    private static byte[] Message(string text)
    {
        var body = Encoding.UTF8.GetBytes(text);
        var frame = new List<byte> { 0x00, (byte)body.Length };
        frame.AddRange(body);
        return frame.ToArray();
    }
}
