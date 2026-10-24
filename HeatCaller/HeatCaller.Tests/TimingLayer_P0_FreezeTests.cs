namespace HeatCaller.Tests;

/// <summary>
/// The freeze test (23 Oct 2026, timing layer, P0): the golden races played
/// again with passes held up for up to ten seconds around the time limit -
/// with their own decoder times and arrival stamps - and the result must be
/// the race played on time, to the millisecond.
///
/// WHY. Passes reach the race through the window's message loop. A window
/// busy for ten seconds (a dialog, a slow disk, a stuck sound card) holds
/// them, and the race used to judge each one by the clock when it was got
/// to: the 60 s heat in TimeLimitHeat has 300 crossing at 79,600, four tenths
/// before the limit, and held until 85,000 that crossing was judged after
/// the checkered - 400, 100 and 300 all "took the flag" on the laps before
/// their real last ones and the race ended there, a lap short. These tests
/// FAILED before P1 (judge each pass by its arrival): see the reasoning on
/// each. They pass once every pass is judged at the moment it arrived.
/// </summary>
public sealed class TimingLayer_P0_FreezeTests
{
    private const long Until = 200_000;

    private static ReplayResult Play(ReplayScript script, Action<RaceControlSession> setup, Func<List<ReplayInput>, List<ReplayInput>>? holdUp = null)
    {
        var onTime = ReplayDelivery.OnTime(script.Inputs, Until);
        return ReplayHarness.Run(holdUp is null ? onTime : holdUp(onTime), setup);
    }

    /// <summary>
    /// The window frozen from 75,000 to 85,000 - across the 80,000 limit.
    /// Before P1: at 85,000 the first held pass (400, arrived 76,503) found
    /// the clock past the limit, put the checkered out and took it on lap 7;
    /// 100 (77,003) and 300 (79,603) then took it on lap 8, every car was in,
    /// and the race ended with three cars a lap short.
    /// </summary>
    [Fact]
    public void A_window_frozen_across_the_time_limit_does_not_change_the_result()
    {
        var script = TimingLayer_P0_GoldenTests.TimeLimitHeat();
        var onTime = Play(script, TimingLayer_P0_GoldenTests.SixtySeconds);
        var frozen = Play(script, TimingLayer_P0_GoldenTests.SixtySeconds, all => ReplayDelivery.WindowFrozen(all, 75_000, 85_000));

        Assert.Equal(onTime.Describe(), frozen.Describe());
        Assert.Equal(9, frozen.Row("300").Laps);
    }

    /// <summary>
    /// Only the passes held - by ten seconds, from 70,000 to 85,000 - while the
    /// tick runs on time and puts the checkered out at 80,000 before they are
    /// got to. Before P1 the first of them (300 at 71,603, handed over at
    /// 81,603) took the flag on lap 7. Now a pass that arrived before the
    /// checkered came out is an ordinary lap, wherever it is got to.
    /// </summary>
    [Fact]
    public void Passes_ten_seconds_late_while_the_clock_puts_the_checkered_out_do_not_change_the_result()
    {
        var script = TimingLayer_P0_GoldenTests.TimeLimitHeat();
        var onTime = Play(script, TimingLayer_P0_GoldenTests.SixtySeconds);
        var late = Play(script, TimingLayer_P0_GoldenTests.SixtySeconds, all => ReplayDelivery.PassesLate(all, 70_000, 85_000, 10_000));

        Assert.Equal(onTime.Describe(), late.Describe());
    }

    /// <summary>
    /// Stopping dead at the limit (finish on next crossing off). Before P1 the
    /// first held pass at 85,000 found the time up and ended the race, so the
    /// three laps run just before the limit were never counted. The race's
    /// own clock does stop later - it stops when the frozen tick finally
    /// runs, as it always has - so the standings are compared, not the clock.
    /// </summary>
    [Fact]
    public void A_window_frozen_across_the_limit_of_a_race_that_stops_dead_counts_the_laps_before_it()
    {
        void StopDead(RaceControlSession race)
        {
            TimingLayer_P0_GoldenTests.SixtySeconds(race);
            race.FinishOnNextCrossing = false;
        }

        var script = TimingLayer_P0_GoldenTests.TimeLimitHeat();
        var onTime = Play(script, StopDead);
        var frozen = Play(script, StopDead, all => ReplayDelivery.WindowFrozen(all, 75_000, 85_000));

        Assert.Equal(onTime.Describe(withRaceClock: false), frozen.Describe(withRaceClock: false));
        Assert.Equal(new[] { 8, 8, 7 }, frozen.Rows.Select(r => r.Laps));
    }

    /// <summary>
    /// Every golden race with the window frozen for ten seconds through the
    /// middle of it, and again through the end of it. No window holds a
    /// command: a frozen window takes no button presses, and a green that is
    /// itself late is the start's business (P5), not a pass's.
    /// </summary>
    /// <remarks>
    /// Before P1, "jump" from 21,000 failed as well: the first real pass of
    /// the race (100, arrived 21,003) was got to at 31,000, the start
    /// reference check read 11 s on the clock against 1 s on the decoder, and
    /// "corrected" a reference that was right - every lap of the race after
    /// it was measured from the wrong zero.
    /// </remarks>
    [Theory]
    [InlineData("alternates", 40_000L, 50_000L)]
    [InlineData("alternates", 52_000L, 62_000L)]
    [InlineData("jump", 21_000L, 31_000L)]
    [InlineData("jump", 40_000L, 50_000L)]
    [InlineData("red", 52_000L, 62_000L)]
    [InlineData("red", 62_000L, 72_000L)]
    [InlineData("staggered", 45_000L, 55_000L)]
    [InlineData("staggered", 50_000L, 60_000L)]
    [InlineData("warnings", 50_000L, 60_000L)]
    [InlineData("undo", 58_000L, 68_000L)]
    public void A_frozen_window_changes_no_golden_result(string race, long fromMs, long toMs)
    {
        var (script, setup) = Golden(race);
        var onTime = Play(script, setup);
        var frozen = Play(script, setup, all => ReplayDelivery.WindowFrozen(all, fromMs, toMs));

        Assert.Equal(onTime.Describe(), frozen.Describe());
    }

    internal static (ReplayScript Script, Action<RaceControlSession> Setup) Golden(string race) => race switch
    {
        "alternates" => (TimingLayer_P0_GoldenTests.AlternatesHeat(), TimingLayer_P0_GoldenTests.AlternatesRules),
        "jump" => (TimingLayer_P0_GoldenTests.JumpStartHeat(), TimingLayer_P0_GoldenTests.JumpStartRules),
        "red" => (TimingLayer_P0_GoldenTests.RedFlagHeat(), TimingLayer_P0_GoldenTests.FiveLaps),
        "timelimit" => (TimingLayer_P0_GoldenTests.TimeLimitHeat(), TimingLayer_P0_GoldenTests.SixtySeconds),
        "staggered" => (TimingLayer_P0_GoldenTests.StaggeredHeat(), TimingLayer_P0_GoldenTests.StaggeredRules),
        "warnings" => (TimingLayer_P0_GoldenTests.WarningsHeat(), TimingLayer_P0_GoldenTests.WarningsRules),
        "undo" => (TimingLayer_P0_GoldenTests.UndoHeat(), TimingLayer_P0_GoldenTests.FiveLaps),
        _ => throw new ArgumentOutOfRangeException(nameof(race)),
    };
}
