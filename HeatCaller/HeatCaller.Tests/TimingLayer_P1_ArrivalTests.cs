namespace HeatCaller.Tests;

/// <summary>
/// P1 of the timing layer (23 Oct 2026): every pass is judged by the moment
/// it arrived (DirectLapEvent.ReceivedMs), not the moment the race got round
/// to it. The freeze tests (TimingLayer_P0_FreezeTests) are the main proof;
/// these pin down the parts of it one at a time.
/// </summary>
public sealed class TimingLayer_P1_ArrivalTests
{
    private static RaceControlSession RunningRace(ReplayClock clock, Action<RaceControlSession>? setup = null)
    {
        var race = new RaceControlSession();
        clock.Bind(race);
        setup?.Invoke(race);
        clock.AdvanceTo(10_000);
        race.Stage();
        clock.AdvanceTo(15_000);
        race.StartCountdown();
        clock.AdvanceTo(20_000);
        race.GreenFlag(ReplayScript.DecoderTime(20_000));
        return race;
    }

    private static DirectLapEvent Crossing(string car, long crossedMs, long? receivedMs = null) => new()
    {
        TransponderId = car,
        DeviceTimeMs = ReplayScript.DecoderTime(crossedMs),
        SignalHits = 40,
        SignalStrength = 120,
        ReceivedMs = receivedMs ?? crossedMs + ReplayScript.LatencyMs,
        Decoder = DecoderConnection.EasyLap,
    };

    /// <summary>The same pass, with no arrival stamp - as every test before P1 made them.</summary>
    private static List<ReplayInput> WithoutArrivalStamps(IEnumerable<ReplayInput> inputs) =>
        inputs.Select(input => input.Kind != ReplayKind.Pass
            ? input.Copy()
            : new ReplayInput
            {
                Kind = input.Kind,
                ArrivedMs = input.ArrivedMs,
                ProcessedMs = input.ProcessedMs,
                Name = input.Name,
                Order = input.Order,
                Pass = new DirectLapEvent
                {
                    TransponderId = input.Pass!.TransponderId,
                    DeviceTimeMs = input.Pass.DeviceTimeMs,
                    SignalHits = input.Pass.SignalHits,
                    SignalStrength = input.Pass.SignalStrength,
                    Decoder = input.Pass.Decoder,
                },
            }).ToList();

    [Theory]
    [InlineData("alternates")]
    [InlineData("jump")]
    [InlineData("red")]
    [InlineData("timelimit")]
    [InlineData("staggered")]
    [InlineData("warnings")]
    [InlineData("undo")]
    public void A_pass_got_to_on_time_is_judged_exactly_as_one_with_no_arrival_stamp(string name)
    {
        // An unstamped pass is judged at "now" - what every pass was judged
        // at before P1 - so this is the old behaviour against the new.
        var (script, setup) = TimingLayer_P0_FreezeTests.Golden(name);
        var onTime = ReplayDelivery.OnTime(script.Inputs, 200_000);

        var stamped = ReplayHarness.Run(onTime, setup);
        var unstamped = ReplayHarness.Run(WithoutArrivalStamps(onTime), setup);

        Assert.Equal(unstamped.Describe(), stamped.Describe());
    }

    [Fact]
    public void A_late_first_pass_does_not_throw_out_a_start_reference_that_was_right()
    {
        var clock = new ReplayClock();
        var race = RunningRace(clock);
        var log = new List<string>();
        race.LogMessage += log.Add;

        // The first crossing, 1 s after the green, reaches the race 7 s late.
        // Judged at 28,000 the clock said 8 s against the decoder's 1 s, and
        // the reference was "corrected" by 7 s.
        clock.AdvanceTo(28_000);
        race.ProcessLapEvent(Crossing("100", 21_000));
        clock.AdvanceTo(29_003);
        race.ProcessLapEvent(Crossing("100", 29_000));

        Assert.DoesNotContain(log, line => line.StartsWith("Start reference corrected", StringComparison.Ordinal));
        var standing = race.GetOrderedStandings().Single();
        Assert.Equal(new long[] { 1_000, 8_000 }, standing.LapTimesMs);
        Assert.Equal(9_000, standing.ElapsedMs);
    }

    [Fact]
    public void A_pass_that_arrived_before_the_checkered_came_out_is_an_ordinary_lap_whenever_it_is_got_to()
    {
        var clock = new ReplayClock();
        var race = RunningRace(clock, r => r.TimeLimit = TimeSpan.FromSeconds(60));

        clock.AdvanceTo(21_003);
        race.ProcessLapEvent(Crossing("100", 21_000));

        // The clock runs out at 80,000 and the tick puts the checkered out.
        clock.AdvanceTo(80_000);
        race.Tick();
        Assert.True(race.IsFinishing);

        // A crossing at 79,600, got to only now.
        clock.AdvanceTo(81_000);
        race.ProcessLapEvent(Crossing("100", 79_600));
        Assert.False(race.HasTakenFlag("100"));
        Assert.Equal(RaceState.Racing, race.State);

        // Its next crossing, after the limit, takes the flag and ends the race.
        clock.AdvanceTo(87_603);
        race.ProcessLapEvent(Crossing("100", 87_600));
        Assert.Equal(RaceState.Finished, race.State);
        Assert.Equal(3, race.GetOrderedStandings().Single().Laps);
    }

    [Fact]
    public void A_pass_that_arrived_after_the_limit_takes_the_flag_even_before_the_tick_has_noticed()
    {
        var clock = new ReplayClock();
        var race = RunningRace(clock, r => r.TimeLimit = TimeSpan.FromSeconds(60));

        clock.AdvanceTo(21_003);
        race.ProcessLapEvent(Crossing("100", 21_000));

        // 0.2 s past the limit, before any tick: the pass itself puts the
        // checkered out and takes it - as it always has.
        clock.AdvanceTo(80_203);
        race.ProcessLapEvent(Crossing("100", 80_200));

        Assert.True(race.HasTakenFlag("100"));
        Assert.Equal(RaceState.Finished, race.State);
    }

    [Fact]
    public void The_race_clock_when_the_last_car_comes_home_late_is_when_it_came_home()
    {
        var clock = new ReplayClock();
        var race = RunningRace(clock, r => r.LapLimit = 2);

        clock.AdvanceTo(21_003);
        race.ProcessLapEvent(Crossing("100", 21_000));

        // The finishing crossing reaches the app at 29,003 and the race gets
        // to it at 33,000: the race finished at 9,003, not 13,000.
        clock.AdvanceTo(33_000);
        race.ProcessLapEvent(Crossing("100", 29_000));

        Assert.Equal(RaceState.Finished, race.State);
        Assert.Equal(TimeSpan.FromMilliseconds(9_003), race.ElapsedRaceTime);
    }

    [Fact]
    public void Listeners_and_other_threads_see_the_present_while_a_late_pass_is_judged()
    {
        var clock = new ReplayClock();
        long judgingSaw = -1, otherThreadSaw = -1, listenerSaw = -1;
        RaceControlSession? session = null;

        var race = RunningRace(clock, r =>
        {
            session = r;

            // Asked from inside the judging (ResolveAlternateTransponder).
            r.AlternateTransponderOwner = _ =>
            {
                judgingSaw = session!.MsSinceGreen;
                otherThreadSaw = OnAnotherThread(() => session!.MsSinceGreen);
                return null;
            };
        });

        race.LapProcessed += (_, _, _) => listenerSaw = race.MsSinceGreen;

        // Crossed at 21,000, arrived 21,003, got to at 25,000.
        clock.AdvanceTo(25_000);
        race.ProcessLapEvent(Crossing("100", 21_000));

        Assert.Equal(1_003, judgingSaw);
        Assert.Equal(5_000, otherThreadSaw);
        Assert.Equal(5_000, listenerSaw);
        Assert.Equal(5_000, race.MsSinceGreen);
    }

    [Fact]
    public void The_decoder_clock_estimate_runs_from_when_the_last_pass_arrived()
    {
        var clock = new ReplayClock();
        var race = new RaceControlSession();
        clock.Bind(race);

        // Read by the decoder at 10,000, arrived 10,003, got to at 15,000.
        clock.AdvanceTo(15_000);
        race.ProcessLapEvent(Crossing("100", 10_000));

        // Since it arrived 4,997 ms have gone by, so the decoder now reads
        // 4,997 more than it did - not 0 more, as when it was stamped on
        // being got to. A green dropped now is stamped with this.
        Assert.Equal((long)ReplayScript.DecoderTime(10_000) + 4_997, race.EstimatedDeviceTimeNowMs);
    }

    [Fact]
    public void A_pass_is_never_judged_as_coming_before_a_green_the_race_has_already_acted_on()
    {
        var clock = new ReplayClock();
        long judgedAtMsSinceGreen = long.MinValue;
        RaceControlSession? session = null;

        var race = RunningRace(clock, r =>
        {
            session = r;
            r.AlternateTransponderOwner = _ =>
            {
                judgedAtMsSinceGreen = session!.MsSinceGreen;
                return null;
            };
        });

        // Arrived at 19,903, a tenth before the green at 20,000, and only got
        // to after it: judged at the green the race has already started
        // from, never 97 ms before it.
        clock.AdvanceTo(20_500);
        race.ProcessLapEvent(Crossing("100", 19_900));

        Assert.Equal(0, judgedAtMsSinceGreen);
    }

    [Fact]
    public void A_pass_s_arrival_is_its_stamp_or_now_when_it_has_none_and_never_later_than_now()
    {
        var clock = new ReplayClock();
        var race = new RaceControlSession();
        clock.Bind(race);
        clock.AdvanceTo(20_500);

        Assert.Equal(19_903, race.ArrivalOf(Crossing("100", 19_900)));
        Assert.Equal(20_500, race.ArrivalOf(Crossing("100", 19_900, receivedMs: 0)));
        Assert.Equal(20_500, race.ArrivalOf(Crossing("100", 19_900, receivedMs: 99_999)));
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a new thread and waits. Not Task.Run:
    /// waited on from a thread-pool thread (where xUnit runs tests with
    /// parallelization off), Task.Run's task can be run inline on the very
    /// same thread, which is then not "another thread" at all (24 Oct 2026,
    /// timing bug hunt).
    /// </summary>
    private static T OnAnotherThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
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

        return result;
    }

    private static void OnAnotherThread(Action work) => OnAnotherThread(() => { work(); return 0; });
}
