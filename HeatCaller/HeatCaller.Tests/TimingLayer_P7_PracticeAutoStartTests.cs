namespace HeatCaller.Tests;

/// <summary>
/// P7 of the timing layer (24 Oct 2026): in threaded mode practice starts at
/// the gate on the timing thread. The UI arms a plan beforehand
/// (RaceTiming.PracticeAutoStart); a real line pass at Idle with no warm-up
/// then starts practice and is counted as the driver's start lap without
/// the window, which takes the running practice over when it gets to the
/// notice - delivered in order with the race's events. A pass at Idle never
/// goes round by the window any more.
/// </summary>
public sealed class TimingLayer_P7_PracticeAutoStartTests : IDisposable
{
    private readonly TimingHost _host = new();
    private readonly QueueingSynchronizationContext _ui = new();

    public void Dispose() => _host.Dispose();

    /// <summary>A threaded facade made on this test's thread - which is then its "UI thread".</summary>
    private (RaceTiming Race, RaceControlSession Engine) Threaded()
    {
        var engine = new RaceControlSession();
        return (new RaceTiming(engine, _host, _ui, tickOnHost: false), engine);
    }

    private static DirectLapEvent Pass(string car, uint deviceTimeMs, bool synthetic = false) => new()
    {
        TransponderId = car,
        DeviceTimeMs = deviceTimeMs,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
        IsSynthetic = synthetic,
    };

    /// <summary>Waits until everything already handed to the timing thread has run.</summary>
    private void Drain() => _host.Invoke(() => { });

    /// <summary>The race's next format, as Race Control would leave it between races.</summary>
    private static void NextRaceFormat(RaceTiming race)
    {
        race.LapLimit = 7;
        race.TimeLimit = TimeSpan.FromMinutes(3);
        race.StartType = RaceStartType.StaggeredGrid;
    }

    /// <summary>
    /// What the inline gate-pass start did - PracticeModeControl.StartPractice's
    /// engine part with the triggering crossing, then that crossing and the
    /// rest counted - on an engine of its own: the standings it left.
    /// </summary>
    private static IReadOnlyList<RacerStanding> InlineGatePassStart(params uint[] crossings)
    {
        var engine = new RaceControlSession();
        engine.Reset();
        engine.IsPracticeMode = true;
        engine.LapLimit = 20;
        engine.TimeLimit = TimeSpan.FromMinutes(5);
        engine.StartType = RaceStartType.Standard;
        engine.GreenFlagBefore(crossings[0]);

        foreach (var crossing in crossings)
        {
            engine.ProcessLapEvent(Pass("101", crossing));
        }

        return engine.GetOrderedStandings();
    }

    [Fact]
    public void An_armed_plan_starts_practice_on_an_idle_pass_with_the_ui_frozen_and_counts_it_as_the_start_lap()
    {
        var (race, engine) = Threaded();
        NextRaceFormat(race);
        var adopted = new List<PracticeAutoStarted>();
        race.PracticeAutoStart = new PracticeAutoStartPlan(20, TimeSpan.FromMinutes(5), adopted.Add);
        _ui.Pump();

        // Straight from the decoder's thread; the UI is never pumped below.
        race.PostLinePass(Pass("101", 9_000), _ => { });
        race.PostLinePass(Pass("101", 21_000), _ => { });
        Drain();

        _host.Invoke(() =>
        {
            Assert.Equal(RaceState.Racing, engine.State);
            Assert.True(engine.IsPracticeMode);
            Assert.Equal(20, engine.LapLimit);
            Assert.Equal(TimeSpan.FromMinutes(5), engine.TimeLimit);
            Assert.Equal(RaceStartType.Standard, engine.StartType);

            // The crossing that started it counts, exactly as when the Practice
            // page started practice on the UI thread and then counted it.
            var standing = engine.GetOrderedStandings().Single();
            var expected = InlineGatePassStart(9_000, 21_000).Single();
            Assert.Equal("101", standing.TransponderId);
            Assert.Equal(expected.Laps, standing.Laps);
            Assert.Equal(expected.LapTimesMs, standing.LapTimesMs);
            Assert.Equal(expected.ElapsedMs, standing.ElapsedMs);
            Assert.Equal(new long[] { 12_000 }, standing.LapTimesMs.Skip(standing.LapTimesMs.Count - 1));
        });

        // Used once, and the window has heard nothing yet.
        Assert.Null(race.PracticeAutoStart);
        Assert.Empty(adopted);
    }

    [Fact]
    public void An_unarmed_idle_pass_starts_nothing_and_is_not_counted_nor_sent_round_by_the_ui()
    {
        var (race, engine) = Threaded();
        var uiSide = new List<string>();

        race.PostLinePass(Pass("101", 9_000), p => uiSide.Add(p.TransponderId));
        Drain();

        Assert.Equal(RaceState.Idle, race.State);
        Assert.False(race.IsPracticeMode);
        Assert.Empty(race.GetOrderedStandings());

        // The screens' share is still the UI's, and that is all it gets.
        _ui.Pump();
        Assert.Equal(new[] { "101" }, uiSide);
        Assert.Equal(RaceState.Idle, race.State);
    }

    [Fact]
    public void A_disarmed_plan_starts_nothing()
    {
        var (race, _) = Threaded();
        race.PracticeAutoStart = new PracticeAutoStartPlan(null, null, _ => throw new InvalidOperationException("never"));
        race.PracticeAutoStart = null;

        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();

        Assert.Equal(RaceState.Idle, race.State);
        Assert.False(race.IsPracticeMode);
    }

    [Fact]
    public void Nothing_starts_during_a_warm_up()
    {
        var (race, _) = Threaded();
        Assert.True(race.BeginWarmUp(TimeSpan.FromMinutes(2)));
        race.PracticeAutoStart = new PracticeAutoStartPlan(null, null, _ => { });

        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();

        Assert.True(race.IsWarmingUp);
        Assert.False(race.IsPracticeMode);
        Assert.Equal(RaceState.Idle, race.State);

        // Left armed for when it would be allowed.
        Assert.NotNull(race.PracticeAutoStart);
    }

    [Fact]
    public void Nothing_starts_on_a_pass_the_app_made_up()
    {
        var (race, _) = Threaded();
        race.PracticeAutoStart = new PracticeAutoStartPlan(null, null, _ => { });

        race.PostLinePass(Pass("101", 9_000, synthetic: true), _ => { });
        Drain();

        Assert.Equal(RaceState.Idle, race.State);
        Assert.NotNull(race.PracticeAutoStart);
    }

    [Fact]
    public void Nothing_starts_while_a_race_is_on_and_a_later_pass_never_restarts_the_practice()
    {
        var (race, engine) = Threaded();
        race.PracticeAutoStart = new PracticeAutoStartPlan(null, null, _ => { });

        race.PostLinePass(Pass("101", 9_000), _ => { });
        race.PostLinePass(Pass("202", 10_000), _ => { });

        // Armed again while racing (the UI's tick can do that): still no restart.
        Drain();
        var generation = race.SessionGeneration;
        race.PracticeAutoStart = new PracticeAutoStartPlan(null, null, _ => { });
        race.PostLinePass(Pass("101", 21_000), _ => { });
        Drain();

        var standings = race.GetOrderedStandings();
        Assert.Equal(2, standings.Count);
        Assert.Equal(InlineGatePassStart(9_000, 21_000).Single().Laps, standings.Single(s => s.TransponderId == "101").Laps);
        Assert.Equal(generation, _host.Invoke(() => engine.SessionGeneration));
        Assert.NotNull(race.PracticeAutoStart);
    }

    [Fact]
    public void No_second_practice_starts_until_the_ui_has_taken_over_the_first()
    {
        var (race, engine) = Threaded();
        NextRaceFormat(race);
        var adopted = new List<PracticeAutoStarted>();
        race.PracticeAutoStart = new PracticeAutoStartPlan(20, null, adopted.Add);

        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();

        // The plan armed again and the session reset on the timing side
        // before the window has caught up: a second start would capture the
        // first practice's targets as "before".
        race.PracticeAutoStart = new PracticeAutoStartPlan(20, null, adopted.Add);
        _host.Invoke(() => engine.Reset());
        race.PostLinePass(Pass("101", 30_000), _ => { });
        Drain();

        Assert.Equal(RaceState.Idle, race.State);

        // The window takes the first one over; then the plan works again.
        _ui.Pump();
        var notice = Assert.Single(adopted);
        Assert.Equal(7, notice.LapLimitBefore);

        race.PostLinePass(Pass("101", 40_000), _ => { });
        Drain();
        Assert.Equal(RaceState.Racing, race.State);
    }

    [Fact]
    public void The_take_over_notice_reaches_the_ui_in_order_with_the_before_values_and_the_crossing()
    {
        var (race, _) = Threaded();
        NextRaceFormat(race);
        _ui.Pump();

        var heard = new List<string>();
        PracticeAutoStarted? notice = null;
        race.StateChanged += state => heard.Add($"state {state}");
        race.LapProcessed += (standing, _, _) => heard.Add($"lap {standing.TransponderId}");

        var crossing = Pass("101", 9_000);
        race.PracticeAutoStart = new PracticeAutoStartPlan(20, TimeSpan.FromMinutes(5), n =>
        {
            notice = n;
            heard.Add("adopt");
        });

        race.PostLinePass(crossing, p => heard.Add($"ui-side {p.TransponderId}"));
        race.PostLinePass(Pass("101", 21_000), p => heard.Add($"ui-side {p.TransponderId}"));
        Drain();
        Assert.Empty(heard);

        _ui.Pump();

        // The screens' share, the start's own raises, the notice, then laps.
        var adopt = heard.IndexOf("adopt");
        Assert.True(adopt > 0, string.Join(" | ", heard));
        Assert.Equal("ui-side 101", heard[0]);
        Assert.True(heard.IndexOf("state Racing") is var racing && racing > 0 && racing < adopt, string.Join(" | ", heard));
        Assert.True(heard.IndexOf("lap 101") > adopt, string.Join(" | ", heard));
        Assert.Equal(1, heard.Count(h => h == "adopt"));

        Assert.NotNull(notice);
        Assert.Same(crossing, notice!.Crossing);
        Assert.Equal(7, notice.LapLimitBefore);
        Assert.Equal(TimeSpan.FromMinutes(3), notice.TimeLimitBefore);
        Assert.Equal(RaceStartType.StaggeredGrid, notice.StartTypeBefore);
    }

    [Fact]
    public void A_ui_command_delivers_the_notice_before_it_runs()
    {
        var (race, _) = Threaded();
        RaceState? seenAtAdopt = null;
        var order = new List<string>();
        race.PracticeAutoStart = new PracticeAutoStartPlan(null, null, _ =>
        {
            seenAtAdopt = race.State;
            order.Add("adopt");
        });

        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();

        // A click on the UI thread, the window not having pumped meanwhile.
        race.YellowFlag();
        order.Add("yellow returned");

        Assert.Equal(new[] { "adopt", "yellow returned" }, order);
        Assert.Equal(RaceState.Racing, seenAtAdopt);
        Assert.Equal(FlagState.Yellow, race.Flag);
    }

    [Fact]
    public void Handing_the_before_values_back_restores_the_next_races_format()
    {
        // What PracticeModeControl.StopPractice does with the values the
        // notice carried: the race after practice gets its own format back.
        var (race, _) = Threaded();
        NextRaceFormat(race);
        PracticeAutoStarted? notice = null;
        race.PracticeAutoStart = new PracticeAutoStartPlan(20, TimeSpan.FromMinutes(5), n => notice = n);

        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();
        _ui.Pump();
        Assert.NotNull(notice);

        race.Reset();
        race.IsPracticeMode = false;
        race.LapLimit = notice!.LapLimitBefore;
        race.TimeLimit = notice.TimeLimitBefore;
        race.StartType = notice.StartTypeBefore;

        Assert.Equal(7, race.LapLimit);
        Assert.Equal(TimeSpan.FromMinutes(3), race.TimeLimit);
        Assert.Equal(RaceStartType.StaggeredGrid, race.StartType);
        Assert.False(race.IsPracticeMode);
    }

    [Fact]
    public void A_take_over_that_throws_is_logged_and_does_not_stop_later_plans()
    {
        var (race, _) = Threaded();
        var log = new List<string>();
        race.LogMessage += log.Add;
        race.PracticeAutoStart = new PracticeAutoStartPlan(null, null, _ => throw new InvalidOperationException("page gone"));

        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();
        _ui.Pump();

        Assert.Contains(log, l => l.Contains("page gone"));

        // The count of notices on their way went back down: a fresh plan works.
        race.Reset();
        race.PracticeAutoStart = new PracticeAutoStartPlan(null, null, _ => { });
        race.PostLinePass(Pass("101", 30_000), _ => { });
        Drain();
        Assert.Equal(RaceState.Racing, race.State);
    }

    [Fact]
    public void Plans_compare_by_targets_and_page_and_inline_has_none()
    {
        Action<PracticeAutoStarted> adopt = _ => { };
        var a = new PracticeAutoStartPlan(10, TimeSpan.FromMinutes(1), adopt);

        Assert.True(a.SameAs(new PracticeAutoStartPlan(10, TimeSpan.FromMinutes(1), adopt)));
        Assert.False(a.SameAs(new PracticeAutoStartPlan(11, TimeSpan.FromMinutes(1), adopt)));
        Assert.False(a.SameAs(new PracticeAutoStartPlan(10, null, adopt)));
        Assert.False(a.SameAs(new PracticeAutoStartPlan(10, TimeSpan.FromMinutes(1), _ => { })));
        Assert.False(a.SameAs(null));

        var inline = new RaceTiming(new RaceControlSession());
        Assert.Throws<InvalidOperationException>(() => inline.PracticeAutoStart = a);
        Assert.Null(inline.PracticeAutoStart);
    }

    // --- A race start wins over a gate practice the window has not taken over ---

    /// <summary>An auto-started practice the UI has not heard of yet, the next race's format having been 7 laps / 3 min / staggered.</summary>
    private (RaceTiming Race, List<PracticeAutoStarted> Notices) AutoStartedNotTakenOver()
    {
        var (race, _) = Threaded();
        NextRaceFormat(race);
        _ui.Pump();
        var notices = new List<PracticeAutoStarted>();
        race.PracticeAutoStart = new PracticeAutoStartPlan(20, TimeSpan.FromMinutes(5), notices.Add);

        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();
        Assert.True(race.IsPracticeMode);
        Assert.Empty(notices);
        return (race, notices);
    }

    private static void AssertHandedBack(RaceTiming race)
    {
        Assert.False(race.IsPracticeMode);
        Assert.Equal(7, race.LapLimit);
        Assert.Equal(TimeSpan.FromMinutes(3), race.TimeLimit);
        Assert.Equal(RaceStartType.StaggeredGrid, race.StartType);
    }

    [Fact]
    public void Stage_after_a_stale_idle_check_takes_the_session_back_from_a_gate_practice_not_yet_taken_over()
    {
        var (race, notices) = AutoStartedNotTakenOver();

        // The click checked Idle a moment before the car crossed; now it
        // starts its race (Race Control: RefuseIfPracticeRunningForRaceStart).
        race.TakeBackPracticeAutoStartForRace();

        // The page was told at once, as cancelled - nothing to take over.
        var notice = Assert.Single(notices);
        Assert.True(notice.Cancelled);
        Assert.Equal(RaceState.Idle, race.State);
        AssertHandedBack(race);
        Assert.Empty(race.GetOrderedStandings());

        race.Stage();
        Assert.Equal(RaceState.Staging, race.State);
        Assert.False(race.IsPracticeMode);

        _ui.Pump();
        Assert.Single(notices);
    }

    [Fact]
    public void A_warm_up_after_a_stale_idle_check_takes_the_session_back()
    {
        var (race, notices) = AutoStartedNotTakenOver();

        race.TakeBackPracticeAutoStartForRace();
        Assert.True(race.BeginWarmUp(TimeSpan.FromMinutes(2)));

        Assert.True(race.IsWarmingUp);
        AssertHandedBack(race);
        Assert.True(Assert.Single(notices).Cancelled);
    }

    [Fact]
    public void A_green_after_a_stale_idle_check_takes_the_session_back()
    {
        var (race, notices) = AutoStartedNotTakenOver();

        race.TakeBackPracticeAutoStartForRace();
        race.GreenFlag(20_000);

        Assert.Equal(RaceState.Racing, race.State);
        Assert.False(race.IsPracticeMode);
        Assert.True(Assert.Single(notices).Cancelled);
    }

    [Fact]
    public void No_gate_practice_starts_until_the_race_starting_ui_message_is_over()
    {
        var (race, _) = Threaded();
        race.PracticeAutoStart = new PracticeAutoStartPlan(null, null, _ => { });

        // The warm-up's end: held first, then the session goes Idle with cars
        // still lapping - none of them may start practice before the Stage.
        Assert.True(race.BeginWarmUp(TimeSpan.FromMinutes(2)));
        race.TakeBackPracticeAutoStartForRace();
        race.EndWarmUp();
        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();
        Assert.Equal(RaceState.Idle, race.State);
        Assert.False(race.IsPracticeMode);

        race.Stage();
        Assert.Equal(RaceState.Staging, race.State);

        // Without a race started, once the message is over (the posted
        // release ran) the plan works again.
        race.Reset();
        _ui.Pump();
        race.PostLinePass(Pass("101", 30_000), _ => { });
        Drain();
        Assert.True(race.IsPracticeMode);
    }

    [Fact]
    public void Once_the_window_has_taken_the_practice_over_a_race_start_leaves_it_alone()
    {
        var (race, notices) = AutoStartedNotTakenOver();
        _ui.Pump();
        Assert.False(Assert.Single(notices).Cancelled);

        race.TakeBackPracticeAutoStartForRace();

        // Race Control's own check then sees a running practice, as always.
        Assert.True(race.IsPracticeMode);
        Assert.Equal(RaceState.Racing, race.State);
        Assert.Single(notices);
    }

    [Fact]
    public void The_practice_pages_own_start_catches_up_and_takes_the_gate_practice_over()
    {
        var (race, notices) = AutoStartedNotTakenOver();

        race.CatchUpWithTiming();

        Assert.False(Assert.Single(notices).Cancelled);
        Assert.True(race.IsPracticeMode);

        // A race start afterwards finds it taken over.
        race.TakeBackPracticeAutoStartForRace();
        Assert.True(race.IsPracticeMode);
    }

    [Fact]
    public void Take_back_and_catch_up_do_nothing_inline()
    {
        var engine = new RaceControlSession();
        var inline = new RaceTiming(engine);
        engine.IsPracticeMode = true;

        inline.TakeBackPracticeAutoStartForRace();
        inline.CatchUpWithTiming();

        Assert.True(engine.IsPracticeMode);
    }
}
