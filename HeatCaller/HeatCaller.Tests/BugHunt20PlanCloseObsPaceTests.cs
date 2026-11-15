using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 20 (15 Nov 2026).
///
/// The curfew planner's "run two classes together" used the app's 8 as the
/// heat size, not tonight's, so a 6-per-heat club was offered an 8-car heat.
///
/// A morning race day set up the evening before read that morning's close
/// as its own, so Next Steps said entries had closed (and the heats built
/// themselves) the night before.
///
/// The OBS link read its settings only before connecting, so a new host or
/// video switched off did nothing while it was connected.
///
/// The Pace tab's verdict table and grid printed the saved position, which
/// on an older session still counts its ghosts.
/// </summary>
public sealed class BugHunt20PlanCloseObsPaceTests
{
    // --- The curfew planner's heat size ---------------------------------

    private static RoundDefinition Heat(string className, string label, params string[] transponders) =>
        new() { ClassName = className, RoundLabel = label, Kind = RoundKind.Qualifier, AssignedTransponders = transponders.ToList() };

    private static List<RoundDefinition> TwoSmallClasses() => new()
    {
        Heat("Stock", "Q1 Heat 1", "S1", "S2", "S3", "S4"),
        Heat("Mod", "Q1 Heat 1", "M1", "M2", "M3", "M4"),
        Heat("Stock", "Q2 Heat 1", "S1", "S2", "S3", "S4"),
        Heat("Mod", "Q2 Heat 1", "M1", "M2", "M3", "M4"),
    };

    [Fact]
    public void Run_together_is_not_offered_past_tonights_heat_size()
    {
        var now = new DateTime(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);
        var outBy = now.AddMinutes(1).ToLocalTime().TimeOfDay;

        var six = NightPlan.For(TwoSmallClasses(), _ => TimeSpan.FromMinutes(5), null, null, null, null, outBy, now, heatSize: 6)!;
        Assert.NotEmpty(six.Fixes);
        Assert.DoesNotContain(six.Fixes, f => f.Text.Contains("together", StringComparison.Ordinal));

        var eight = NightPlan.For(TwoSmallClasses(), _ => TimeSpan.FromMinutes(5), null, null, null, null, outBy, now, heatSize: 8)!;
        Assert.Contains(eight.Fixes, f => f.Text.StartsWith("run Stock and Mod together", StringComparison.Ordinal));
    }

    // --- Entries close on a day set up the evening before ---------------

    private static DateTime LocalToUtc(int day, int hour, int minute = 0) =>
        new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();

    [Fact]
    public void A_morning_day_set_up_the_evening_before_closes_tomorrow_morning()
    {
        var friday8pm = LocalToUtc(13, 20);
        var day = new RaceEvent { Name = "Saturday", StartedUtc = friday8pm, EntriesCloseLocalTime = TimeSpan.FromHours(9) };

        var close = EntriesClose.AtUtc(day, friday8pm);

        Assert.Equal(LocalToUtc(14, 9), close);
        Assert.StartsWith("Entries close at", EntriesClose.Note(close!.Value, friday8pm));
        Assert.Null(EntriesClose.LateLine(day, friday8pm));

        // Saturday at 09:30 it has closed.
        Assert.Equal(LocalToUtc(14, 9), EntriesClose.AtUtc(day, LocalToUtc(14, 9, 30)));
    }

    [Fact]
    public void A_day_opened_just_after_its_close_is_still_closed()
    {
        var opened = LocalToUtc(13, 18, 45);
        var day = new RaceEvent { Name = "Tonight", StartedUtc = opened, EntriesCloseLocalTime = new TimeSpan(18, 30, 0) };

        Assert.Equal(LocalToUtc(13, 18, 30), EntriesClose.AtUtc(day, opened));
    }

    // --- The OBS link follows its settings -------------------------------

    [Fact]
    public void A_new_host_port_or_password_is_a_different_obs()
    {
        var settings = new AppSettings { VideoEnabled = true, VideoObsHost = "studio-pc", VideoObsPort = 4455, VideoObsPassword = "a" };
        var first = ObsLink.TargetOf(settings);

        Assert.NotNull(first);
        settings.VideoObsPassword = "b";
        Assert.NotEqual(first, ObsLink.TargetOf(settings));
        settings.VideoObsPassword = "a";
        Assert.Equal(first, ObsLink.TargetOf(settings));
        settings.VideoEnabled = false;
        Assert.Null(ObsLink.TargetOf(settings));

        // Scene switching (stream overlay plan 6.1) needs the link too.
        settings.ObsSwitchScenes = true;
        Assert.Equal(first, ObsLink.TargetOf(settings));
    }

    [Fact]
    public async Task Switching_video_off_leaves_an_obs_already_connected()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var settings = new AppSettings { VideoEnabled = true, VideoObsHost = "localhost", VideoObsPort = port };
        using var link = new ObsLink(() => settings, new StreamTonight());
        link.Start();

        var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        using var obs = (await context.AcceptWebSocketAsync("obswebsocket.json")).WebSocket;
        await SendAsync(obs, """{"op":0,"d":{"rpcVersion":1}}""");
        await ReceiveAsync(obs);
        await SendAsync(obs, """{"op":2,"d":{"negotiatedRpcVersion":1}}""");
        await ReceiveAsync(obs);

        Assert.True(await Within(TimeSpan.FromSeconds(5), () => link.State == "connected"), link.State);

        settings.VideoEnabled = false;

        Assert.True(await Within(TimeSpan.FromSeconds(8), () => link.State == "off"), link.State);
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static Task SendAsync(WebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[16 * 1024];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WebSocketReceiveResult got;

        do
        {
            got = await socket.ReceiveAsync(buffer, timeout.Token);
        }
        while (!got.EndOfMessage);
    }

    private static async Task<bool> Within(TimeSpan wait, Func<bool> condition)
    {
        var until = DateTime.UtcNow + wait;

        while (DateTime.UtcNow < until)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }

    // --- The Pace tab's places leave ghosts out --------------------------

    [Fact]
    public void A_ghost_ahead_does_not_push_the_real_drivers_down_a_place()
    {
        SessionResultEntry Row(int position, string transponder) => new() { Position = position, Transponder = transponder, DriverName = transponder };

        var ghost = Row(1, Ghosts.Prefix + "1");
        var sam = Row(2, "1001");
        var leo = Row(3, "1002");
        var unplaced = Row(0, "1003");
        var race = new SessionRecord { SessionType = RaceSessionType.Race, Results = new List<SessionResultEntry> { ghost, sam, leo, unplaced } };

        Assert.Equal(1, PaceAnalysisControl.RealPlace(race, sam));
        Assert.Equal(2, PaceAnalysisControl.RealPlace(race, leo));
        Assert.Equal(0, PaceAnalysisControl.RealPlace(race, unplaced));
        Assert.StartsWith("Won it", PaceAnalysisControl.ResultClause(race, sam));
    }
}
