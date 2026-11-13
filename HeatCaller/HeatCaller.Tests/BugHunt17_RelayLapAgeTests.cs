using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, #173 (13 Nov 2026). The relay stamped each car's newest lap
/// with when its frame ARRIVED, so a frame that waited in the publisher - a
/// backoff, a request that timed out, a big upload ahead of it - made an old
/// lap look new and a phone read it out late. Now each row of the relay's
/// live frame says how old its lap was when the frame was built ("lapAge",
/// RelayLapAge), and the publish says how long the frame then waited before
/// THIS send ("held", RelayPublisher.HeldMs) - both on clocks that only go
/// forward, and neither ever negative.
/// </summary>
public sealed class BugHunt17_RelayLapAgeTests : IDisposable
{
    private const string CarA = "A4821B";
    private const string CarB = "B9930C";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------ a row's age

    private static RacerStanding Car(string transponder, long lastLapDeviceTimeMs, int crossings = 2)
    {
        var car = new RacerStanding { TransponderId = transponder, LastLapDeviceTimeMs = lastLapDeviceTimeMs };

        for (var i = 0; i < crossings; i++)
        {
            car.LapTimesMs.Add(9_000 + i);
        }

        return car;
    }

    [Fact]
    public void A_rows_age_is_the_decoders_clock_now_less_its_last_counted_crossing()
    {
        Assert.Equal(700L, RelayLapAge.Of(Car(CarA, 31_000), 31_700));
        Assert.Equal(0L, RelayLapAge.Of(Car(CarA, 31_000), 31_000));
    }

    [Fact]
    public void A_negative_age_is_never_sent()
    {
        // The decoder was reconnected and its clock restarted, or the laps
        // came from something that is not the decoder at all.
        Assert.Null(RelayLapAge.Of(Car(CarA, 31_000), 30_999));
        Assert.Null(RelayLapAge.Of(Car(CarA, 31_000), 0));
    }

    [Fact]
    public void An_age_past_the_most_the_relay_believes_is_sent_as_that()
    {
        Assert.Equal(RelayLapAge.MaxMs, RelayLapAge.Of(Car(CarA, 1_000), 1_000 + RelayLapAge.MaxMs + 1));
        Assert.Equal(RelayLapAge.MaxMs, RelayLapAge.Of(Car(CarA, 1_000), long.MaxValue));
    }

    [Fact]
    public void No_age_for_a_ghost_a_car_yet_to_cross_or_a_start_lap_credited_at_the_green()
    {
        Assert.Null(RelayLapAge.Of(Car(Ghosts.Prefix + "AAAAAA", 31_000), 31_700));
        Assert.Null(RelayLapAge.Of(Car(CarA, 0, crossings: 0), 31_700));
        Assert.Null(RelayLapAge.Of(Car(CarA, 0), 31_700));

        // Past the line at the green: the crossing was before it, but the lap
        // is news as of the green. Once they cross again it is an ordinary lap.
        var jumper = Car(CarA, 19_000, crossings: 1);
        jumper.StartLapCredited = true;
        Assert.Null(RelayLapAge.Of(jumper, 31_700));

        jumper.LapTimesMs.Add(9_400);
        jumper.LastLapDeviceTimeMs = 31_000;
        Assert.Equal(700L, RelayLapAge.Of(jumper, 31_700));
    }

    // ------------------------------------------------ on the relay's frame

    /// <summary>A race on a clock the test moves, with the decoder plugged in 20 s before the green.</summary>
    private sealed class Night
    {
        private const long Green = 1_000_000;
        private long _nowMs = Green;

        public Night()
        {
            var utc0 = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc);

            Race = new RaceControlSession
            {
                NowMsSource = () => _nowMs,
                UtcNowSource = () => utc0.AddMilliseconds(_nowMs),
            };

            Race.Stage();
            Race.StartCountdown();
            Race.GreenFlag(20_000);

            Timing = new RaceTiming(Race);
            Server = new WebServerService(Timing, ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });
        }

        public RaceControlSession Race { get; }

        public RaceTiming Timing { get; }

        public WebServerService Server { get; }

        /// <summary>Moves the clock to this long after the green.</summary>
        public void At(long msSinceGreen) => _nowMs = Green + msSinceGreen;

        /// <summary>A pass from the decoder itself, arriving this long after the green.</summary>
        public void Cross(string car, uint deviceTimeMs, long msSinceGreen)
        {
            At(msSinceGreen);
            Race.ProcessLapEvent(new DirectLapEvent
            {
                TransponderId = car,
                DeviceTimeMs = deviceTimeMs,
                SignalHits = 60,
                SignalStrength = 247,
            });
        }

        public string Frame() => Server.BuildStateJson(abbreviateNames: false, forRelay: true);

        /// <summary>What MainForm.RelayStateJson does with a frame.</summary>
        public string Stamped(string frame)
        {
            var view = Timing.ReadLiveView();

            return RelayLapAge.Stamp(frame, view.Ordered, view.DeviceNowMs, Server.RelayRowTag);
        }

        public JsonObject RowOf(string json, string car) =>
            JsonNode.Parse(json)!["standings"]!.AsArray()
                .Select(row => row!.AsObject())
                .Single(row => row["tag"]?.GetValue<string>() == Server.RelayRowTag(car));
    }

    [Fact]
    public void Each_cars_row_says_how_old_its_newest_lap_is_as_the_frame_is_built()
    {
        var night = new Night();

        night.Cross(CarA, 21_000, 1_000);
        night.Cross(CarB, 21_500, 1_500);
        night.Cross(CarA, 31_000, 11_000);
        night.Cross(CarB, 31_400, 11_400);

        // The frame is built 700 ms after A's lap and 300 ms after B's.
        night.At(11_700);
        var json = night.Stamped(night.Frame());

        Assert.Equal(700L, night.RowOf(json, CarA)["lapAge"]!.GetValue<long>());
        Assert.Equal(300L, night.RowOf(json, CarB)["lapAge"]!.GetValue<long>());

        // A second on with nobody across the line, both are a second older.
        night.At(12_700);
        json = night.Stamped(night.Frame());

        Assert.Equal(1_700L, night.RowOf(json, CarA)["lapAge"]!.GetValue<long>());
        Assert.Equal(1_300L, night.RowOf(json, CarB)["lapAge"]!.GetValue<long>());
    }

    [Fact]
    public void A_lap_that_lands_after_the_frame_was_built_does_not_lend_it_its_age()
    {
        var night = new Night();

        night.Cross(CarA, 21_000, 1_000);
        night.Cross(CarB, 21_500, 1_500);
        night.Cross(CarA, 31_000, 11_000);
        night.Cross(CarB, 31_400, 11_400);

        night.At(11_700);
        var frame = night.Frame();

        // A crosses again before the race is read for the ages: the frame
        // still shows A's lap before, so A's row says nothing.
        night.Cross(CarA, 41_000, 21_000);
        var json = night.Stamped(frame);

        Assert.Null(night.RowOf(json, CarA)["lapAge"]);
        Assert.Equal(9_600L, night.RowOf(json, CarB)["lapAge"]!.GetValue<long>());
    }

    [Fact]
    public void A_frame_with_no_age_to_give_goes_as_it_was_built()
    {
        // Laps fed in by something that is not the decoder (a replay, the
        // tests' own): there is no decoder clock to age them against.
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);

        foreach (var at in new uint[] { 11_000, 23_000 })
        {
            race.ProcessLapEvent(new DirectLapEvent { TransponderId = CarA, DeviceTimeMs = at, SignalHits = 1, SignalStrength = 100, IsSynthetic = true });
        }

        var timing = new RaceTiming(race);
        var server = new WebServerService(timing, ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });
        var frame = server.BuildStateJson(abbreviateNames: false, forRelay: true);
        var view = timing.ReadLiveView();

        Assert.Equal(frame, RelayLapAge.Stamp(frame, view.Ordered, view.DeviceNowMs, server.RelayRowTag));
        Assert.DoesNotContain("lapAge", frame, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"standings\":7}")]
    [InlineData("{\"standings\":[7,null,{\"tag\":5,\"lap\":2},{\"tag\":\"t\",\"lap\":\"2\"},{\"tag\":\"t\"}]}")]
    public void A_frame_that_is_not_the_shape_expected_comes_back_as_it_went_in(string frame)
    {
        var cars = new[] { Car(CarA, 31_000) };

        Assert.Equal(frame, RelayLapAge.Stamp(frame, cars, 31_700, _ => "t"));
    }

    // ------------------------------------------------ how long the frame waited

    [Fact]
    public void Held_is_the_wait_since_the_frame_was_queued_and_never_negative()
    {
        Assert.Equal(0L, RelayPublisher.HeldMs(50_000, 50_000));
        Assert.Equal(5_000L, RelayPublisher.HeldMs(50_000, 55_000));
        Assert.Equal(0L, RelayPublisher.HeldMs(50_000, 49_000));
    }

    [Fact]
    public void Held_rides_beside_seq_on_a_snapshot_and_nowhere_else()
    {
        Assert.Equal(
            "https://relay.example.com/api/v1/c/acme-rc/publish/state?seq=7&held=5000",
            RelayPublisher.PublishPath("https://relay.example.com", "acme-rc", "state", null, 7, 5_000));
        Assert.Equal(
            "https://relay.example.com/api/v1/c/acme-rc/publish/roster?seq=7",
            RelayPublisher.PublishPath("https://relay.example.com", "acme-rc", "roster", null, 7, null));
        Assert.Equal(
            "https://relay.example.com/api/v1/c/acme-rc/publish/results/abc?seq=7",
            RelayPublisher.PublishPath("https://relay.example.com", "acme-rc", "results", "abc", 7, 5_000));
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

    private static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);

    private sealed class Answers(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(answer(request));
        }
    }

    /// <summary>One publish as the relay would have seen it.</summary>
    private sealed record Sent(string Channel, string Query, string Body);

    /// <summary>
    /// A publisher that queues as in a race but whose worker sends nothing:
    /// each pass is run from the test (Drain), on a clock the test moves
    /// (<paramref name="ticks"/>), to a relay that takes every publish while
    /// <paramref name="relayIsUp"/> and cannot be reached while not. Every
    /// publish tried is noted in <paramref name="sent"/>, reached or not.
    /// </summary>
    private static RelayPublisher Publisher(List<Sent> sent, Func<bool> relayIsUp, Func<long> ticks)
    {
        var settings = new AppSettings
        {
            RelayEnabled = false,
            RelayBaseUrl = "http://127.0.0.1:9",
            RelayClub = "acme-rc",
            RelayToken = "hg_test",
        };

        var publisher = new RelayPublisher(settings) { TickSource = ticks };

        var http = FieldOf(publisher, "_http");
        ((HttpClient)http.GetValue(publisher)!).Dispose();
        http.SetValue(publisher, new HttpClient(new Answers(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            const string marker = "/publish/";

            if (path.Contains(marker, StringComparison.Ordinal))
            {
                sent.Add(new Sent(
                    path[(path.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..],
                    request.RequestUri.Query,
                    request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()));

                if (!relayIsUp())
                {
                    throw new HttpRequestException("The relay could not be reached.");
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"applied\":true}", Encoding.UTF8, "application/json") };
        })));

        // Quiesced before publishing is switched on, under the worker's own
        // lock: it then starts no pass of its own, and Drain below is the
        // only thing that sends.
        lock (FieldOf(publisher, "_gate").GetValue(publisher)!)
        {
            FieldOf(publisher, "_quiesced").SetValue(publisher, true);
        }

        settings.RelayEnabled = true;

        return publisher;
    }

    private static string HeldOf(Sent publish) =>
        publish.Query.Split('&').Single(part => part.StartsWith("held=", StringComparison.Ordinal))["held=".Length..];

    private static string SeqOf(Sent publish) =>
        publish.Query.TrimStart('?').Split('&').Single(part => part.StartsWith("seq=", StringComparison.Ordinal))["seq=".Length..];

    [Fact]
    public void A_frame_sent_again_after_a_failure_says_how_long_it_has_waited_by_then()
    {
        var sent = new List<Sent>();
        var up = false;
        long tick = 50_000;

        using var publisher = Publisher(sent, () => up, () => tick);

        publisher.PublishSnapshot(RelayChannel.State, "{\"state\":\"Racing\",\"standings\":[]}");

        // The first try, 300 ms after it was queued, never reaches the relay.
        tick = 50_300;
        Call(publisher, "Drain", CancellationToken.None);

        // The same frame goes again 8 s later - under the same number, and
        // saying 8.3 s, not the 0.3 s of its first try.
        up = true;
        tick = 58_300;
        Call(publisher, "Drain", CancellationToken.None);

        Assert.Equal(2, sent.Count);
        Assert.All(sent, publish => Assert.Equal("state", publish.Channel));
        Assert.Equal("300", HeldOf(sent[0]));
        Assert.Equal("8300", HeldOf(sent[1]));
        Assert.Equal(SeqOf(sent[0]), SeqOf(sent[1]));
        Assert.Equal(sent[0].Body, sent[1].Body);

        // And once it has landed there is nothing left to send.
        Call(publisher, "Drain", CancellationToken.None);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void A_newer_frame_that_replaces_a_waiting_one_is_timed_from_its_own_queueing()
    {
        var sent = new List<Sent>();
        long tick = 50_000;

        using var publisher = Publisher(sent, () => true, () => tick);

        publisher.PublishSnapshot(RelayChannel.State, "{\"state\":\"Racing\",\"standings\":[],\"frame\":1}");

        tick = 53_000;
        publisher.PublishSnapshot(RelayChannel.State, "{\"state\":\"Racing\",\"standings\":[],\"frame\":2}");

        tick = 53_200;
        Call(publisher, "Drain", CancellationToken.None);

        var only = Assert.Single(sent);
        Assert.Contains("\"frame\":2", only.Body, StringComparison.Ordinal);
        Assert.Equal("200", HeldOf(only));
    }

    [Fact]
    public void A_clock_that_reads_earlier_at_the_send_gives_a_wait_of_nothing()
    {
        var sent = new List<Sent>();
        long tick = 50_000;

        using var publisher = Publisher(sent, () => true, () => tick);

        publisher.PublishSnapshot(RelayChannel.State, "{\"state\":\"Racing\",\"standings\":[]}");

        tick = 10_000;
        Call(publisher, "Drain", CancellationToken.None);

        Assert.Equal("0", HeldOf(Assert.Single(sent)));
    }

    [Fact]
    public void Only_the_live_state_says_how_long_it_waited()
    {
        var sent = new List<Sent>();
        long tick = 50_000;

        using var publisher = Publisher(sent, () => true, () => tick);

        publisher.PublishSnapshot(RelayChannel.Standings, "{\"seasons\":[]}");

        tick = 55_000;
        Call(publisher, "Drain", CancellationToken.None);

        var only = Assert.Single(sent);
        Assert.Equal("standings", only.Channel);
        Assert.DoesNotContain("held", only.Query, StringComparison.Ordinal);
        Assert.StartsWith("?seq=", only.Query, StringComparison.Ordinal);
    }
}
