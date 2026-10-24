namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): the phone coach page (/coach).
///
/// Polls that overlapped on weak Wi-Fi (an older answer said a lap twice),
/// "Say my lap time" that stayed silent after a reload, a pick left over
/// from before a server restart that said "Not on track right now", and lap
/// numbers one lower than the board's.
///
/// The page's script runs in a phone's browser, which a test here can't
/// drive, so it is checked as the laptop serves it - the way the coach
/// page's other tests are.
/// </summary>
public sealed class BugHunt10_WebServerService_CoachTests : IDisposable
{
    // Letters in it, so it cannot turn up inside a lap time or a hex id by chance.
    private const string Transponder = "C10K";

    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void The_coach_page_polls_one_at_a_time_and_gives_up_on_a_poll_that_hangs()
    {
        var tick = FunctionBody(Server().BuildCoachPage(), "async function tick(");

        Assert.Contains("if (ticking) return;", tick);
        Assert.Contains("fetch('/api/state', { signal: stop.signal })", tick);
        Assert.Contains("ticking = false;", tick);
    }

    [Fact]
    public void The_report_card_fetch_gives_up_and_ignores_an_answer_for_the_last_driver()
    {
        var card = FunctionBody(Server().BuildCoachPage(), "async function card(");

        Assert.Contains("signal: stop.signal", card);
        Assert.Contains("if (asked !== following) return;", card);
    }

    [Fact]
    public void Say_my_lap_time_waits_for_a_tap_after_a_reload_and_keeps_the_page_from_reloading()
    {
        var page = Server().BuildCoachPage();

        Assert.Contains("id=\"sayText\"", page);
        Assert.Contains("': tap to resume'", page);
        Assert.Contains("sayAloud(onRow ? 'Lap times on' : ' ');", page);
        // Tapping the "tap to resume" row itself resumes the voice, not unticks it.
        Assert.Contains("if (onRow) e.preventDefault();", page);
        Assert.Contains("function lookBusy(){ return boardOpen || sayBox.checked; }", page);
        Assert.DoesNotContain("document.addEventListener('click', wakeAudio, { once: true });", page);
    }

    [Fact]
    public void A_pick_nobody_on_the_list_matches_says_to_pick_again()
    {
        var draw = FunctionBody(Server().BuildCoachPage(), "function draw(");

        Assert.Contains("listed.length > 0 && !listed.some(p => p.id === following)", draw);
        Assert.Contains("pick your driver again above", draw);
    }

    [Fact]
    public void The_lap_list_leaves_out_the_start_lap_so_the_page_numbers_from_lap_2_and_counts_the_board_s_laps()
    {
        var server = Server();
        var state = System.Text.Json.Nodes.JsonNode.Parse(server.BuildStateJson())!.AsObject();
        var row = state["standings"]!.AsArray().Single()!;
        var id = row["id"]!.GetValue<string>();

        var flying = row["laps"]!.AsArray().Count;
        var board = state["cards"]![id]!["laps"]!.GetValue<int>();

        // What the page relies on: the list is the board's laps less the start lap.
        Assert.True(flying >= 1);
        Assert.Equal(board, flying + 1);

        var draw = FunctionBody(server.BuildCoachPage(), "function draw(");
        Assert.Contains("n: i + 2", draw);
        Assert.Contains("((d.cards || {})[following] || {}).laps || (laps.length + 1)", draw);
    }

    private static WebServerService Server()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = Transponder, DriverName = "Coach Ten" });

        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);

        foreach (var at in new uint[] { 11_000, 20_000, 29_100 })
        {
            race.ProcessLapEvent(new DirectLapEvent
            {
                TransponderId = Transponder,
                DeviceTimeMs = at,
                SignalHits = 1,
                SignalStrength = 100,
                IsSynthetic = true,
            });
        }

        return new WebServerService(new RaceTiming(race), profiles, settings: new AppSettings { DisplayLook = Looks.MidnightKey });
    }

    private static string FunctionBody(string page, string start)
    {
        var from = page.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"No \"{start}\" in the page.");

        var to = page.IndexOf("\n}", from, StringComparison.Ordinal);
        Assert.True(to > from, $"\"{start}\" never closes.");

        return page[from..to];
    }
}
