namespace HeatCaller.Tests;

/// <summary>
/// P8 of the timing layer, D (24 Oct 2026): the pieces the live state is
/// built from off the UI thread with timing on its own thread - the frozen
/// feeds of the window's callbacks (FrozenFeed), the frozen profile view
/// (ProfileStore.GetForAnyThread / AnyThread) and the race read from one
/// moment (RaceTiming.ReadLiveView). The web server's own side is in
/// TimingLayer_P8_LiveStateTests.
/// </summary>
public sealed class TimingLayer_P8_LiveStatePieceTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly TimingHost _host = new();
    private readonly QueueingSynchronizationContext _ui = new();

    public void Dispose()
    {
        _host.Dispose();
        _data.Dispose();
    }

    // ------------------------------------------------------------ FrozenFeed

    [Fact]
    public void A_frozen_feed_throws_until_it_is_first_read_then_hands_back_the_last_answer()
    {
        var answer = "first";
        var asked = 0;
        var feed = new FrozenFeed<string>(() =>
        {
            asked++;
            return answer;
        });

        Assert.Throws<InvalidOperationException>(() => feed.Get());
        Assert.Equal(0, asked);

        feed.Refresh();
        answer = "second";

        // Get never asks the source: only Refresh does.
        Assert.Equal("first", feed.Get());
        Assert.Equal("first", feed.Get());
        Assert.Equal(1, asked);

        feed.Refresh();
        Assert.Equal("second", feed.Get());
    }

    [Fact]
    public void A_source_that_throws_leaves_the_last_answer_and_forget_drops_it()
    {
        var fail = false;
        var feed = new FrozenFeed<int>(() => fail ? throw new InvalidOperationException("gone") : 7);

        // Throwing before any good answer: still nothing to hand back.
        fail = true;
        feed.Refresh();
        Assert.Throws<InvalidOperationException>(() => feed.Get());

        fail = false;
        feed.Refresh();
        Assert.Equal(7, feed.Get());

        fail = true;
        feed.Refresh();
        Assert.Equal(7, feed.Get());

        feed.Forget();
        Assert.Throws<InvalidOperationException>(() => feed.Get());
    }

    [Fact]
    public void A_null_answer_is_an_answer()
    {
        var feed = new FrozenFeed<string?>(() => null);
        feed.Refresh();
        Assert.Null(feed.Get());
    }

    [Fact]
    public void A_frozen_feed_is_read_whole_from_another_thread_while_it_is_refreshed()
    {
        var n = 0;
        var feed = new FrozenFeed<(int A, int B)>(() =>
        {
            n++;
            return (n, -n);
        });
        feed.Refresh();

        var stop = false;
        var torn = 0;
        Exception? fault = null;

        var reader = new Thread(() =>
        {
            try
            {
                while (!Volatile.Read(ref stop))
                {
                    var (a, b) = feed.Get();

                    if (a != -b)
                    {
                        Interlocked.Increment(ref torn);
                    }
                }
            }
            catch (Exception ex)
            {
                fault = ex;
            }
        });

        reader.Start();

        for (var i = 0; i < 20_000; i++)
        {
            feed.Refresh();
        }

        Volatile.Write(ref stop, true);
        reader.Join();

        Assert.Null(fault);
        Assert.Equal(0, torn);
    }

    // ------------------------------------------------ ProfileStore.GetForAnyThread

    private static DriverProfile Driver(string main, string name, params string[] alternates)
    {
        var profile = new DriverProfile { Transponder = main, DriverName = name };

        foreach (var alternate in alternates)
        {
            profile.Transponders.Add(new OwnedTransponder { Id = alternate });
        }

        return profile;
    }

    private static readonly string[] Asked =
    {
        "100", "200", "300", "777", "999", "", " ", "a1", "A1", " A1 ", "B2", "b2", "X9", " 100 ", "c3",
    };

    private static void SameAnswers(ProfileStore store)
    {
        foreach (var transponder in Asked)
        {
            var onUi = store.Get(transponder);

            Assert.True(ReferenceEquals(onUi, store.GetForAnyThread(transponder)), $"GetForAnyThread(\"{transponder}\") differs from Get");
            Assert.True(ReferenceEquals(onUi, store.AnyThread.Get(transponder)), $"AnyThread.Get(\"{transponder}\") differs from Get");
        }
    }

    [Fact]
    public void The_any_thread_lookup_answers_as_get_for_mains_alternates_contested_unknown_case_and_spaces()
    {
        var store = ProfileStore.Load();
        store.Upsert(Driver("100", "Maya", "A1"));
        store.Upsert(Driver("200", "Dev", "B2", "X9"));

        // X9 claimed by two: nobody's, for both lookups.
        store.Upsert(Driver("300", "Lee", "X9"));

        // An alternate that is somebody's main: the main wins.
        store.Upsert(Driver("777", "Sam", "100"));

        SameAnswers(store);

        Assert.Equal("Maya", store.GetForAnyThread("a1")?.DriverName);
        Assert.Equal("Maya", store.GetForAnyThread(" A1 ")?.DriverName);
        Assert.Equal("Maya", store.GetForAnyThread("100")?.DriverName);
        Assert.Null(store.GetForAnyThread("X9"));
        Assert.Null(store.GetForAnyThread("999"));

        // The store itself is a lookup too - what every screen passes.
        IDriverLookup lookup = store;
        Assert.Same(store.Get("B2"), lookup.Get("B2"));
        Assert.Same(store.AnyThread, store.AnyThread);
    }

    [Fact]
    public void The_any_thread_lookup_follows_every_change_to_the_profiles()
    {
        var store = ProfileStore.Load();
        store.Upsert(Driver("100", "Maya", "A1"));
        store.Upsert(Driver("200", "Dev", "B2"));
        SameAnswers(store);

        // A replaced profile object: the new one, by main and alternate.
        var renamed = Driver("100", "Maya P", "A1", "C3");
        store.Upsert(renamed);
        SameAnswers(store);
        Assert.Same(renamed, store.GetForAnyThread("c3"));

        store.Delete("200");
        SameAnswers(store);
        Assert.Null(store.GetForAnyThread("B2"));
        Assert.Null(store.GetForAnyThread("200"));
    }

    [Fact]
    public void The_any_thread_lookup_is_safe_while_the_ui_changes_the_profiles()
    {
        var store = ProfileStore.Load();
        store.Upsert(Driver("100", "Maya", "A1"));

        var stop = false;
        var wrong = 0;
        Exception? fault = null;

        var reader = new Thread(() =>
        {
            try
            {
                while (!Volatile.Read(ref stop))
                {
                    if (store.GetForAnyThread("A1")?.Transponder != "100")
                    {
                        Interlocked.Increment(ref wrong);
                    }

                    store.GetForAnyThread("2");
                }
            }
            catch (Exception ex)
            {
                fault = ex;
            }
        });

        reader.Start();

        for (var i = 0; i < 300; i++)
        {
            store.Upsert(Driver((i % 7).ToString(), "Driver " + i));
            store.Delete(((i + 3) % 7).ToString());
        }

        Volatile.Write(ref stop, true);
        reader.Join();

        Assert.Null(fault);
        Assert.Equal(0, wrong);
    }

    // --------------------------------------------------- RaceTiming.ReadLiveView

    private static DirectLapEvent Pass(string car, uint at) => new()
    {
        TransponderId = car,
        DeviceTimeMs = at,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
    };

    private static void Laps(RaceTiming race)
    {
        race.Stage();
        race.GreenFlag(10_000);
        race.ProcessLapEvent(Pass("101", 21_000));
        race.ProcessLapEvent(Pass("202", 22_000));
        race.ProcessLapEvent(Pass("101", 33_000));
    }

    private static void SameView(LiveRaceView view, RaceTiming race)
    {
        Assert.Equal(race.GetOrderedStandings().Select(s => s.TransponderId), view.Ordered.Select(s => s.TransponderId));
        Assert.Equal(race.GetOrderedStandings().Select(s => s.Laps), view.Ordered.Select(s => s.Laps));
        Assert.Equal(race.State, view.State);
        Assert.Equal(race.Flag, view.Flag);
        Assert.Equal(race.GetCountdownText(), view.Countdown);
        Assert.Equal(race.SessionGeneration, view.Generation);
        Assert.Equal(race.StreakThreshold, view.StreakThreshold);
        Assert.Equal(race.SectorLengthsFeet, view.SectorLengthsFeet);
    }

    [Fact]
    public void Inline_the_live_view_is_the_engine_s_own_reads()
    {
        var lengths = new[] { 40.0, 60.0 };
        var engine = new RaceControlSession { SectorLengthsFeetProvider = () => lengths };
        var race = new RaceTiming(engine);
        Laps(race);

        var view = race.ReadLiveView();

        SameView(view, race);
        Assert.Equal(RaceState.Racing, view.State);
        Assert.Equal(2, view.Ordered.Count);

        // The engine's own objects, as GetOrderedStandings inline.
        Assert.Same(engine.GetOrderedStandings()[0], view.Ordered[0]);
        Assert.Same(lengths, view.SectorLengthsFeet);
    }

    [Fact]
    public void Threaded_the_live_view_is_one_read_of_copies_with_the_window_frozen()
    {
        var lengths = new[] { 40.0, 60.0 };
        var engine = new RaceControlSession { SectorLengthsFeetProvider = () => lengths };
        var race = new RaceTiming(engine, _host, _ui, tickOnHost: false);
        Laps(race);

        LiveRaceView? view = null;
        var thread = new Thread(() => view = race.ReadLiveView());
        thread.Start();
        thread.Join();

        Assert.NotNull(view);
        SameView(view!, race);
        Assert.Equal(RaceState.Racing, view!.State);
        Assert.Equal(new[] { 2, 1 }, view.Ordered.Select(s => s.Laps));

        var live = _host.Invoke(() => engine.GetOrderedStandings());

        for (var i = 0; i < live.Count; i++)
        {
            Assert.NotSame(live[i], view.Ordered[i]);
        }

        Assert.NotSame(lengths, view.SectorLengthsFeet);
    }
}
