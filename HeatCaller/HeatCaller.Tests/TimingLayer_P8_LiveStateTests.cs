using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// P8 of the timing layer, D (24 Oct 2026): with timing on its own thread
/// the live state (/api/state, and the relay's copy) is built off the UI
/// thread (MainForm.LiveState), so the phones and the relay no longer freeze
/// when the window does. The board comes live from the timing thread; what
/// only the window may read comes from what it last captured
/// (WebServerService.CaptureUiSide). Inline nothing changes: every build
/// reads the window's side there and then.
/// </summary>
public sealed class TimingLayer_P8_LiveStateTests : IDisposable
{
    // Letters in it, so it cannot turn up inside a lap time or a hex id by chance.
    private const string Car = "L8VE";

    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();
    private readonly TimingHost _host = new();

    /// <summary>The window: never pumped.</summary>
    private readonly QueueingSynchronizationContext _ui = new();

    public void Dispose()
    {
        _host.Dispose();
        _data.Dispose();
    }

    // ------------------------------------------------------------ helpers

    private static DirectLapEvent Pass(string car, uint at) => new()
    {
        TransponderId = car,
        DeviceTimeMs = at,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
    };

    private static ProfileStore Profiles()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = Car, DriverName = "Live Eight" });
        return profiles;
    }

    /// <summary>A threaded facade made on this test's thread - which is then its "UI thread" - racing, and a server on it.</summary>
    private (RaceTiming Race, WebServerService Server, AppSettings Settings) Threaded()
    {
        var race = new RaceTiming(new RaceControlSession(), _host, _ui, tickOnHost: false);
        race.Stage();
        race.GreenFlag(10_000);

        var settings = new AppSettings { WebKpis = new List<string> { "pos", "driver", "laps" } };
        return (race, new WebServerService(race, Profiles(), settings: settings), settings);
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static List<string> Columns(JsonObject state) =>
        state["columns"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();

    private static List<string> Labels(params string[] keys) =>
        keys.Select(k => KpiCatalog.Find(k)!.Label).ToList();

    /// <summary>
    /// Runs <paramref name="work"/> on a new thread and waits. Not Task.Run,
    /// which can run inline on the waiting thread (see P4b's tests).
    /// </summary>
    private static void OnAnotherThread(Action work)
    {
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                failed = ex;
            }
        });

        thread.Start();
        thread.Join();

        if (failed is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failed).Throw();
        }
    }

    // ------------------------------------------------ 1. a frozen window

    [Fact]
    public void Threaded_the_published_board_is_built_off_the_ui_and_carries_the_laps_with_the_window_frozen()
    {
        var (race, server, _) = Threaded();
        var problems = new List<string>();
        server.StatusMessage += line => { lock (problems) { problems.Add(line); } };

        // The window's tick, once.
        server.CaptureUiSide();

        foreach (var at in new uint[] { 21_000, 33_000, 45_000 })
        {
            race.PostLinePass(Pass(Car, at), _ => { });
        }

        _host.Invoke(() => { });
        var waiting = _ui.Pending;

        // Off the UI, as MainForm.LiveState's timer builds it - the UI never pumped.
        OnAnotherThread(() => server.PublishState(force: true));

        Assert.Empty(problems);
        Assert.Equal(waiting, _ui.Pending);

        var state = Parse(server.PublishedState);
        Assert.Equal("Racing", state["state"]!.GetValue<string>());
        Assert.Equal(Labels("pos", "driver", "laps"), Columns(state));

        var row = state["standings"]!.AsArray().Single()!.AsObject();
        Assert.Equal("Live Eight", row["name"]!.GetValue<string>());

        var id = row["id"]!.GetValue<string>();
        Assert.Equal(3, state["cards"]![id]!["laps"]!.GetValue<int>());

        // The relay's copy, off the UI too.
        string relay = string.Empty;
        OnAnotherThread(() => relay = server.BuildStateJson(abbreviateNames: false, forRelay: true));
        Assert.Equal("Racing", Parse(relay)["state"]!.GetValue<string>());
    }

    // ------------------------------------------------ 2. inline reads live

    [Fact]
    public void Inline_every_build_reads_the_window_s_side_there_and_then()
    {
        var engine = new RaceControlSession();
        engine.Stage();
        engine.GreenFlag(10_000);
        engine.ProcessLapEvent(Pass(Car, 21_000));

        var settings = new AppSettings { WebKpis = new List<string> { "pos", "driver" } };
        var server = new WebServerService(new RaceTiming(engine), Profiles(), settings: settings);
        var heading = "Q1 Heat 1";
        server.RaceInHand = () => heading;

        // Never captured, and CaptureUiSide is never needed inline.
        var first = Parse(server.BuildStateJson());
        Assert.Equal("Q1 Heat 1", first["race"]!.GetValue<string>());
        Assert.Equal(Labels("pos", "driver"), Columns(first));

        heading = "Q1 Heat 2";
        settings.WebKpis.Add("laps");

        var second = Parse(server.BuildStateJson());
        Assert.Equal("Q1 Heat 2", second["race"]!.GetValue<string>());
        Assert.Equal(Labels("pos", "driver", "laps"), Columns(second));
    }

    // ------------------------------------------------ 3. threaded reads the capture

    [Fact]
    public void Threaded_the_columns_stay_as_captured_until_the_next_capture()
    {
        var (_, server, settings) = Threaded();
        server.CaptureUiSide();

        // Edited in place, as the Web page's column picker does.
        settings.WebKpis.Remove("laps");

        Assert.Equal(Labels("pos", "driver", "laps"), Columns(Parse(server.BuildStateJson())));

        server.CaptureUiSide();
        Assert.Equal(Labels("pos", "driver"), Columns(Parse(server.BuildStateJson())));
    }

    // ------------------------------------------------ 6. concurrency

    [Fact]
    public void Two_pool_threads_building_while_laps_arrive_publish_only_whole_documents()
    {
        var (race, server, _) = Threaded();
        server.CaptureUiSide();

        var problems = new List<string>();
        server.StatusMessage += line => { lock (problems) { problems.Add(line); } };

        var stop = false;
        Exception? fault = null;
        var unparsed = 0;

        void Build(bool relay)
        {
            for (var i = 0; i < 100; i++)
            {
                try
                {
                    if (relay && i % 2 == 0)
                    {
                        JsonNode.Parse(server.BuildStateJson(abbreviateNames: true, forRelay: true));
                    }
                    else
                    {
                        server.PublishState(force: true);
                    }

                    JsonNode.Parse(server.PublishedState);
                }
                catch (System.Text.Json.JsonException)
                {
                    Interlocked.Increment(ref unparsed);
                }
                catch (Exception ex)
                {
                    fault ??= ex;
                }
            }
        }

        var feeder = new Thread(() =>
        {
            var at = 21_000u;

            while (!Volatile.Read(ref stop))
            {
                race.PostLinePass(Pass(Car, at), _ => { });
                race.PostLinePass(Pass("2" + (at % 3), at + 500), _ => { });
                at += 3_000;
                Thread.Sleep(1);
            }
        });

        feeder.Start();

        var a = new Thread(() => Build(relay: false));
        var b = new Thread(() => Build(relay: true));
        a.Start();
        b.Start();
        a.Join();
        b.Join();

        Volatile.Write(ref stop, true);
        feeder.Join();

        Assert.Null(fault);
        Assert.Equal(0, unparsed);
        Assert.Empty(problems);

        var state = Parse(server.PublishedState);
        Assert.Equal("Racing", state["state"]!.GetValue<string>());
        Assert.NotEmpty(state["standings"]!.AsArray());
    }
}
