using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): the laptop's own web server.
///
/// A Race-tab flag the laptop answered "NOT sent" was wiped off the manager
/// page by its next poll; a tab opened while a box on the last one still
/// had the keyboard was never loaded; and a sponsor slide or video held a
/// thread-pool thread for as long as the phone took to read it.
///
/// The page's script runs in a phone's browser, which a test here can't
/// drive, so that is checked as the laptop serves it.
/// </summary>
public sealed class BugHunt17_WebServerServiceTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- A flag that was NOT sent stays on the page ----------------------------------------

    [Fact]
    public void A_flag_the_laptop_turned_away_is_kept_on_the_page_until_the_next_action()
    {
        var manage = Pages().BuildManagePage();
        var sayFailure = FunctionBody(manage, "function sayFailure(");

        // 'error' stays until the next action; 'gate' goes with the next
        // answer that gets through, which a poll brings within four seconds.
        Assert.Contains("(d._status === 200 || d.notSent) ? 'error' : 'gate'", sayFailure);
        Assert.Contains("if (msgKind === 'gate') { say(''); }", FunctionBody(manage, "function checkGate("));
    }

    // --- A tab opened with a box still focused ---------------------------------------------

    [Fact]
    public void Opening_a_tab_lets_go_of_a_box_on_the_tab_it_hides_before_loading_the_new_one()
    {
        var showTab = FunctionBody(Pages().BuildManagePage(), "function showTab(");

        var blur = showTab.IndexOf("page.contains(typing)) { typing.blur(); }", StringComparison.Ordinal);
        var load = showTab.IndexOf("refreshActive();", StringComparison.Ordinal);

        Assert.True(blur >= 0, "showTab no longer blurs a box on a hidden tab.");
        Assert.True(load > blur, "The new tab is loaded before the old tab's box is let go.");

        // Only a box on a tab being hidden: never the PIN box, which is on none.
        Assert.Contains("t !== name && typing", showTab);
    }

    // --- Sponsor slides and videos do not hold a thread ------------------------------------

    [Fact]
    public void A_sponsor_video_nobody_is_reading_does_not_hold_the_request_s_thread()
    {
        var (server, video) = ServerWithVideo(24 * 1024 * 1024);
        using var listener = Listener(out var port);

        // The phone asks and then reads nothing at all.
        using var client = Ask(port, "GET /api/sponsor?i=0 HTTP/1.1\r\nHost: localhost:" + port + "\r\n\r\n");
        var context = listener.GetContext();

        var handled = new Thread(() => Call(server, "HandleSponsorImage", context)) { IsBackground = true };
        handled.Start();

        // Used to sit in a blocking Write until the phone took the bytes.
        Assert.True(handled.Join(TimeSpan.FromSeconds(10)), "The request's thread was held while the phone read nothing.");

        // And the video still arrives whole once the phone does read.
        var (head, body) = ReadAnswer(client.GetStream());

        Assert.StartsWith("HTTP/1.1 200", head, StringComparison.Ordinal);
        Assert.Equal(video, body);
    }

    [Fact]
    public void A_range_of_a_sponsor_video_is_still_answered_with_those_bytes()
    {
        var (server, video) = ServerWithVideo(300 * 1024);
        using var listener = Listener(out var port);

        using var client = Ask(port, "GET /api/sponsor?i=0 HTTP/1.1\r\nHost: localhost:" + port + "\r\nRange: bytes=100000-250000\r\n\r\n");
        var context = listener.GetContext();

        Call(server, "HandleSponsorImage", context);

        var (head, body) = ReadAnswer(client.GetStream());

        Assert.StartsWith("HTTP/1.1 206", head, StringComparison.Ordinal);
        Assert.Contains("Content-Range: bytes 100000-250000/" + video.Length, head, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(video.AsSpan(100000, 150001).ToArray(), body);
    }

    [Fact]
    public void A_sponsor_picture_still_arrives_whole()
    {
        var picked = _data.PathOf("slide.png");
        var png = new byte[2 * 1024 * 1024];
        new Random(17).NextBytes(png);
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0x07, 0x80, 0, 0, 0x04, 0x38, 8, 2, 0, 0, 0 }
            .CopyTo(png, 0);
        File.WriteAllBytes(picked, png);

        var sponsors = SponsorStore.Load();
        Assert.NotNull(sponsors.AddSlide(picked, out var error));
        Assert.Null(error);

        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), sponsors: sponsors);
        using var listener = Listener(out var port);

        using var client = Ask(port, "GET /api/sponsor?i=0 HTTP/1.1\r\nHost: localhost:" + port + "\r\n\r\n");
        var context = listener.GetContext();

        Call(server, "HandleSponsorImage", context);

        var (head, body) = ReadAnswer(client.GetStream());

        Assert.StartsWith("HTTP/1.1 200", head, StringComparison.Ordinal);
        Assert.Contains("image/png", head, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(png, body);
    }

    // --- Helpers ----------------------------------------------------------------------------

    /// <summary>A server with one sponsor video of that many bytes - an MP4's opening box, then noise - and the bytes.</summary>
    private (WebServerService Server, byte[] Video) ServerWithVideo(int length)
    {
        var picked = _data.PathOf("ad.mp4");
        var bytes = new byte[length];
        new Random(17).NextBytes(bytes);
        new byte[] { 0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p' }.CopyTo(bytes, 0);
        Directory.CreateDirectory(Path.GetDirectoryName(picked)!);
        File.WriteAllBytes(picked, bytes);

        var sponsors = SponsorStore.Load();
        Assert.NotNull(sponsors.AddVideo(picked, out var error));
        Assert.Null(error);

        return (new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), sponsors: sponsors), bytes);
    }

    private static HttpListener Listener(out int port)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        return listener;
    }

    private static TcpClient Ask(int port, string request)
    {
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var ask = Encoding.ASCII.GetBytes(request);
        client.GetStream().Write(ask, 0, ask.Length);
        return client;
    }

    /// <summary>An answer's head, and as many body bytes as its Content-Length says.</summary>
    private static (string Head, byte[] Body) ReadAnswer(NetworkStream stream)
    {
        stream.ReadTimeout = 15000;

        var head = new List<byte>();

        while (head.Count < 4 || !(head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n'))
        {
            var b = stream.ReadByte();
            Assert.True(b >= 0, "The answer ended before its headers did.");
            head.Add((byte)b);
        }

        var text = Encoding.ASCII.GetString(head.ToArray());
        var lengthLine = text.Split("\r\n").First(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var body = new byte[int.Parse(lengthLine["Content-Length:".Length..].Trim())];

        stream.ReadExactly(body);
        return (text, body);
    }

    private static object? Call(object owner, string name, params object?[] args)
    {
        var method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(owner.GetType().Name, name);

        try
        {
            return method.Invoke(owner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static WebServerService Pages() =>
        new(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });

    /// <summary>One function of a page's script, from its first line to the brace that closes it at the left margin.</summary>
    private static string FunctionBody(string page, string start)
    {
        var from = page.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"No \"{start}\" in the page.");

        var to = page.IndexOf("\n}", from, StringComparison.Ordinal);
        Assert.True(to > from, $"\"{start}\" never closes.");

        return page[from..to];
    }
}
