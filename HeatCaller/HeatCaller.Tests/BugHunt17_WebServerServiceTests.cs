using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

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

    // The page keeps the line up only for an answer marked notSent, so the
    // laptop's two flag-gate refusals are checked as it really sends them.

    [Fact]
    public void A_flag_turned_away_while_another_is_carried_out_is_answered_not_sent()
    {
        var server = ManagerServer();

        // A GREEN from the flag remote is still being carried out.
        Assert.Equal(RemoteCommandGate.Answer.Go, GateOf(server).TryBegin("flag-remote", "GREEN", out _));

        var (head, answer) = AskForFlag(server, "GREEN");

        Assert.StartsWith("HTTP/1.1 503", head, StringComparison.Ordinal);
        AssertNotSent(answer);
    }

    [Fact]
    public void A_flag_turned_away_for_too_many_commands_is_answered_not_sent()
    {
        var server = ManagerServer();
        var gate = GateOf(server);

        // Every device together has had its share of this minute.
        for (var i = 0; i < RemoteCommandGate.AllDevicesPerMinute; i++)
        {
            Assert.Equal(RemoteCommandGate.Answer.Go, gate.TryBegin($"phone-{i}", "GREEN", out _));
            gate.End();
        }

        var (head, answer) = AskForFlag(server, "GREEN");

        Assert.StartsWith("HTTP/1.1 429", head, StringComparison.Ordinal);
        AssertNotSent(answer);
    }

    private const string TestManagerPin = "482913";

    /// <summary>
    /// A server /api/manage will answer: a manager PIN, a host that is up,
    /// and a race-day manager that is there but never built - the gate turns
    /// both of these flags away before it is asked anything.
    /// </summary>
    private static WebServerService ManagerServer() =>
        new(new RaceTiming(new RaceControlSession()), ProfileStore.Load())
        {
            ManagerPin = TestManagerPin,
            HostReady = true,
            Manager = (RaceDayService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(RaceDayService)),
        };

    private static RemoteCommandGate GateOf(WebServerService server) =>
        (RemoteCommandGate)typeof(WebServerService)
            .GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(server)!;

    /// <summary>A race.flag from the manager page's Race tab, and the laptop's answer to it.</summary>
    private static (string Head, string Answer) AskForFlag(WebServerService server, string flag)
    {
        using var listener = Listener(out var port);

        var body = "{\"id\":\"f1\",\"op\":\"race.flag\",\"args\":{\"flag\":\"" + flag + "\"}}";

        using var client = Ask(port,
            "POST /api/manage HTTP/1.1\r\nHost: localhost:" + port + "\r\n"
            + "X-HeatCaller: 1\r\nX-Pin: " + TestManagerPin + "\r\n"
            + "Content-Type: application/json\r\nContent-Length: " + Encoding.ASCII.GetByteCount(body) + "\r\n\r\n" + body);

        var context = listener.GetContext();
        var handled = (Task)Call(server, "HandleManageAsync", context)!;

        Assert.True(handled.Wait(TimeSpan.FromSeconds(15)), "/api/manage never answered the flag.");

        var (head, bytes) = ReadAnswer(client.GetStream());
        return (head, Encoding.UTF8.GetString(bytes));
    }

    private static void AssertNotSent(string answer)
    {
        using var document = JsonDocument.Parse(answer);
        var root = document.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean(), answer);
        Assert.True(root.TryGetProperty("notSent", out var notSent) && notSent.ValueKind == JsonValueKind.True, answer);
        Assert.Equal("f1", root.GetProperty("id").GetString());
        Assert.StartsWith("NOT sent", root.GetProperty("error").GetString());
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

    // --- Helpers ----------------------------------------------------------------------------

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
