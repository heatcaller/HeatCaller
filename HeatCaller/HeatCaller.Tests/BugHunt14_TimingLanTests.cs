using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 14 (7 Nov 2026): a start and three Wi-Fi pages.
///
/// A yellow or red thrown after the finish (or at Idle) stalled the next
/// automatic start begun with Countdown; a phone checking in on a closed
/// race day was told "checked in" while nothing changed; the LAN vote page
/// said "Voting has closed." before any vote was opened; and the Room and
/// Web card said a vote closes when the result comes in, not at the green.
/// </summary>
public sealed class BugHunt14_TimingLanTests : IDisposable
{
    private const string Bill = "A4821B";
    private const string BillPin = "2468";

    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    private readonly TimingHost _host = new();

    /// <summary>The window: never pumped unless a test says so.</summary>
    private readonly QueueingSynchronizationContext _ui = new();

    public void Dispose()
    {
        _host.Dispose();
        _data.Dispose();
    }

    // --- T2: a caution left from before the countdown -------------------------------------

    private static RaceControlSession Finished()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(10_000);
        race.CheckeredFlag();
        Assert.Equal(RaceState.Finished, race.State);
        return race;
    }

    [Fact]
    public void A_yellow_thrown_after_the_finish_goes_with_a_countdown_started_straight_from_it()
    {
        var race = Finished();
        race.YellowFlag();
        Assert.Equal(FlagState.Yellow, race.Flag);

        race.StartCountdown();

        Assert.Equal(RaceState.Countdown, race.State);
        Assert.Equal(FlagState.None, race.Flag);
    }

    [Fact]
    public void A_red_thrown_at_idle_goes_with_a_countdown_started_straight_from_it()
    {
        var race = new RaceControlSession();
        race.RedFlag();
        Assert.Equal(FlagState.Red, race.Flag);

        race.StartCountdown();

        Assert.Equal(RaceState.Countdown, race.State);
        Assert.Equal(FlagState.None, race.Flag);

        race.GreenFlag(10_000);
        Assert.Equal(RaceState.Racing, race.State);
        Assert.Equal(FlagState.Green, race.Flag);
    }

    [Fact]
    public void A_finished_race_s_checkered_is_still_kept_by_a_countdown_from_it()
    {
        var race = Finished();
        var flag = race.Flag;

        race.StartCountdown();

        Assert.Equal(flag, race.Flag);
    }

    [Fact]
    public void A_yellow_thrown_during_the_countdown_still_stays_out_when_the_lights_restart()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.StartCountdown();
        race.YellowFlag();

        race.StartCountdown();

        Assert.Equal(FlagState.Yellow, race.Flag);
    }

    [Fact]
    public void The_threaded_automatic_green_is_not_stalled_by_a_yellow_thrown_after_the_last_finish()
    {
        var engine = new RaceControlSession();
        var race = new RaceTiming(engine, _host, _ui, tickOnHost: true);
        var gate = new TaskCompletionSource();
        gate.SetResult();
        race.BeforeGreenFlagAsync = () => gate.Task;

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(10_000);
        Assert.True(WaitFor(() => _host.Invoke(() => engine.State) == RaceState.Racing, 2_000));
        race.CheckeredFlag();
        Assert.True(WaitFor(() => _host.Invoke(() => engine.State) == RaceState.Finished, 2_000));

        // F4 after the finish, then Countdown pressed straight from Finished.
        race.YellowFlag();
        race.StartCountdown();
        Assert.True(race.ArmGreenAfterCountdown(TimeSpan.Zero, _ => Task.CompletedTask, TimeSpan.FromSeconds(5)));

        Assert.True(WaitFor(() => _host.Invoke(() => engine.State) == RaceState.Racing, 2_000));
    }

    private static bool WaitFor(Func<bool> condition, int milliseconds)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(milliseconds);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    // --- W1: a phone's check-in on a closed day --------------------------------------------

    [Fact]
    public void A_phone_s_check_in_on_a_closed_day_is_refused_with_the_reason_and_never_handed_over()
    {
        const string reason = "Check-in is closed: the laptop is on a race day that has been closed (\"Last week\").";

        var (status, body, handedOver) = PhoneChecksBillIn(reason);

        Assert.Equal(503, status);
        Assert.Contains("Last week", body, StringComparison.Ordinal);
        Assert.False(JsonNode.Parse(body)!["ok"]!.GetValue<bool>());
        Assert.False(handedOver);
    }

    [Fact]
    public void A_phone_s_check_in_on_an_open_day_is_still_handed_over()
    {
        var (status, body, handedOver) = PhoneChecksBillIn(dayClosedReason: null);

        Assert.Equal(200, status);
        Assert.True(JsonNode.Parse(body)!["ok"]!.GetValue<bool>());
        Assert.True(handedOver);
    }

    [Fact]
    public void The_laptop_tells_the_server_when_the_day_is_closed_and_republishes_when_the_day_changes()
    {
        var publish = Between(Source("MainForm.Relay.cs"), "private void PublishCheckInRoster()", "private void PublishCheckInRosterSoon()");
        Assert.Contains("CheckInStore.ClosedCurrentDay(_raceEvents)", publish);
        Assert.Contains("_webServer.PublishRoster(roster, closedDay is null", publish);

        var changed = Between(Source("MainForm.cs"), "_raceEvents.CurrentChanged += (_, _) =>", "EventSpine.Bind(");
        Assert.Contains("TryPost(PublishCheckInRosterSoon);", changed);
    }

    private (int Status, string Body, bool HandedOver) PhoneChecksBillIn(string? dayClosedReason)
    {
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey })
        {
            HostReady = true,
        };

        server.PublishRoster(new[] { new WebServerService.CheckInEntry(Bill, "Bill Hargrove", "Stock", CheckedIn: false) }, dayClosedReason);
        server.PublishRacerPins(new Dictionary<string, string> { [Bill] = BillPin });

        var handedOver = false;
        server.CheckInRequested += (_, _) => handedOver = true;

        var view = typeof(WebServerService).GetField("_rosterView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
        var ids = (IReadOnlyList<string>)view.GetType().GetProperty("Ids")!.GetValue(view)!;

        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var ask = Encoding.ASCII.GetBytes(
            $"POST /api/checkin?t={ids[0]}&state=in HTTP/1.1\r\nHost: localhost:{port}\r\nX-HeatCaller: 1\r\nX-Racer-Pin: {BillPin}\r\n"
            + "Content-Length: 0\r\nConnection: close\r\n\r\n");
        client.GetStream().Write(ask, 0, ask.Length);

        var context = listener.GetContext();
        Call(server, "HandleCheckIn", context);
        context.Response.Close();

        client.ReceiveTimeout = 5_000;
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
        var answer = reader.ReadToEnd();

        var status = int.Parse(answer.Split(' ')[1]);
        var body = answer[(answer.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..];
        body = body[body.IndexOf('{')..(body.LastIndexOf('}') + 1)];

        return (status, body, handedOver);
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

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    // --- W2: the LAN vote page before any vote ---------------------------------------------

    private static VoteOption[] Grid() => new[]
    {
        new VoteOption(Bill, "Bill Hargrove"),
        new VoteOption("B9930C", "Sam Teague"),
    };

    [Fact]
    public void The_ballot_says_whether_a_vote_is_running_as_well_as_whether_it_is_open()
    {
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });
        var vote = RoomVote.Load();
        vote.NewMeeting("night-1");

        var none = JsonNode.Parse(server.BuildVoteBallotJson(vote, "phone-1"))!;
        Assert.True(none["ok"]!.GetValue<bool>());
        Assert.False(none["running"]!.GetValue<bool>());
        Assert.False(none["open"]!.GetValue<bool>());

        Assert.Null(vote.OpenFor("round-a", "Stock A-Main", "Stock", Grid()));
        var open = JsonNode.Parse(server.BuildVoteBallotJson(vote, "phone-1"))!;
        Assert.True(open["running"]!.GetValue<bool>());
        Assert.True(open["open"]!.GetValue<bool>());

        vote.Close();
        var closed = JsonNode.Parse(server.BuildVoteBallotJson(vote, "phone-1"))!;
        Assert.True(closed["running"]!.GetValue<bool>());
        Assert.False(closed["open"]!.GetValue<bool>());
    }

    [Fact]
    public void The_vote_page_says_no_vote_is_running_rather_than_that_voting_has_closed()
    {
        var draw = Between(Source("WebServerService.Vote.cs"), "function draw(d) {", "function ballot() {");
        var notRunning = draw.IndexOf("if (!d.running) {", StringComparison.Ordinal);
        var closedLine = draw.IndexOf("Voting has closed.", StringComparison.Ordinal);

        Assert.True(notRunning >= 0 && closedLine > notRunning, "the page asks whether a vote is running before it says one has closed");
        Assert.Contains("No vote is running at the moment.", draw[notRunning..closedLine]);
    }

    // --- W3: when the vote closes, on the card and in the guide ----------------------------

    [Fact]
    public void The_room_and_web_card_says_the_vote_closes_at_the_green()
    {
        var card = Source("RoomAndWebCard.cs");

        Assert.DoesNotContain("it closes when the result comes in", card);
        Assert.Contains("it closes when that race goes green", card);

        var guide = File.ReadAllText(Path.Combine(RepoRoot, "website", "docs", "announcer", "commentary.md"));
        Assert.DoesNotContain("it closes itself when the result comes in", guide);
        Assert.DoesNotContain("**It closes itself twice over**: when the result comes in", guide);
        Assert.Contains("it closes itself when that race goes green", guide);
    }

    // --- Source helpers ----------------------------------------------------------------------

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string Between(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);

        Assert.True(end > start, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return source[start..end];
    }
}
