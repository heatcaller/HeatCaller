namespace HeatCaller.Tests;

/// <summary>
/// Race-session fixes from the 13 Nov 2026 bug hunt: the lap after one taken
/// off by hand was timed from the crossing taken off and booked as a best;
/// a race put back by Undo or after a crash had its clock rebuilt from the
/// wall clock; and a Stage over a running race kept the abandoned race's
/// call-up time.
/// </summary>
public sealed class BugHunt17_RaceControlSessionTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>A pass from the decoder itself - not synthetic, so the start checks see it.</summary>
    private static void Seen(RaceControlSession race, string car, uint deviceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = deviceTimeMs,
            SignalHits = 60,
            SignalStrength = 247,
        });

    private static RacerStanding StandingOf(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => string.Equals(s.TransponderId, car, StringComparison.OrdinalIgnoreCase));

    /// <summary>A and B two laps in; A is then carried back over the loop 4 s after its last crossing.</summary>
    private static RaceControlSession CarriedBackOverTheLoop()
    {
        var race = new RaceControlSession();

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);

        Seen(race, "A", 100_500);
        Seen(race, "B", 100_700);
        Seen(race, "A", 110_000);
        Seen(race, "B", 110_500);

        Seen(race, "A", 114_000); // the false crossing, counted as a 4 s lap
        Assert.Equal(3, StandingOf(race, "A").Laps);
        return race;
    }

    // --- The lap after -1 Lap ---------------------------------------------------

    [Fact]
    public void The_lap_after_one_taken_off_by_hand_is_not_booked_as_a_best_or_the_fastest_lap()
    {
        var race = CarriedBackOverTheLoop();

        Assert.True(race.AdjustLaps("A", -1));
        Seen(race, "A", 121_000); // the real lap, 11 s - timed 7 s from the false crossing

        var a = StandingOf(race, "A");
        Assert.Equal(3, a.Laps);
        Assert.Equal(new long[] { 500, 13_500, 7_000 }, a.LapTimesMs);
        Assert.Equal(a.ElapsedMs, a.LapTimesMs.Sum());

        // Counted, but not a timed lap: not A's best, not the race's fastest.
        Assert.True(a.IsRecoveryLap(2));
        Assert.Equal(13_500L, a.FastestLapMs);
        Assert.Equal(9_800L, race.RaceFastestLapMs);
        Assert.Equal("B", race.RaceFastestLapTransponder);

        // The lap after that is timed from a real crossing, and counts again.
        Seen(race, "A", 131_000);

        a = StandingOf(race, "A");
        Assert.False(a.IsRecoveryLap(3));
        Assert.Equal(10_000L, a.FastestLapMs);
    }

    [Fact]
    public void Undoing_the_lap_taken_off_lets_the_next_lap_be_a_timed_lap_again()
    {
        var race = CarriedBackOverTheLoop();

        var edit = race.EditStanding("A", () => race.AdjustLaps("A", -1));
        Assert.NotNull(edit);
        race.UndoStandingEdit(edit!);

        Seen(race, "A", 121_000);

        var a = StandingOf(race, "A");
        Assert.Equal(new long[] { 500, 9_500, 4_000, 7_000 }, a.LapTimesMs);
        Assert.False(a.IsRecoveryLap(3));
    }

    [Fact]
    public void A_race_reset_and_put_back_still_knows_the_next_lap_is_not_a_timed_one()
    {
        var race = CarriedBackOverTheLoop();
        race.AdjustLaps("A", -1);

        var snapshot = race.CreateSnapshot();
        var takenUtc = DateTime.UtcNow;
        race.Reset();
        Assert.True(race.RestoreSnapshotAfterGap(snapshot, takenUtc, takenUtc));
        race.GreenFlag(114_000);

        Seen(race, "A", 121_000);

        Assert.True(StandingOf(race, "A").IsRecoveryLap(2));
        Assert.Equal("B", race.RaceFastestLapTransponder);
    }

    // --- The clock of a race put back -------------------------------------------

    [Fact]
    public void A_race_put_back_by_undo_keeps_its_clock_through_an_earlier_time_step()
    {
        long nowMs = 1_000_000;
        var step = TimeSpan.Zero;
        var utc0 = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            TimeLimit = TimeSpan.FromMinutes(5),
            NowMsSource = () => nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(nowMs) + step,
        };

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(0);

        // A minute in, the laptop joins the Wi-Fi and Windows steps the clock on 40 s.
        nowMs += 60_000;
        step = TimeSpan.FromSeconds(40);

        // At 2:00 the director hits Reset by mistake, and Undo ten seconds later.
        nowMs += 60_000;
        Assert.Equal(TimeSpan.FromMinutes(2), race.ElapsedRaceTime);

        var snapshot = race.CreateSnapshot();
        var takenUtc = utc0.AddMilliseconds(nowMs) + step;
        race.Reset();

        nowMs += 10_000;
        Assert.True(race.RestoreSnapshotAfterGap(snapshot, takenUtc));

        // Back at 2:00 under red - not the 2:40 the wall clock made of it.
        Assert.Equal(FlagState.Red, race.Flag);
        Assert.Equal(TimeSpan.FromMinutes(2), race.ElapsedRaceTime);

        // And it runs on from there at the green.
        nowMs += 5_000;
        race.GreenFlag(0);
        nowMs += 30_000;
        Assert.Equal(TimeSpan.FromSeconds(150), race.ElapsedRaceTime);
    }

    [Fact]
    public void A_red_that_was_out_when_the_race_was_reset_is_banked_without_an_earlier_time_step()
    {
        long nowMs = 1_000_000;
        var step = TimeSpan.Zero;
        var utc0 = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            NowMsSource = () => nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(nowMs) + step,
        };

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(0);

        nowMs += 60_000;
        race.RedFlag();

        // The clock is stepped on 40 s while the red is out; reset 30 s into it.
        nowMs += 30_000;
        step = TimeSpan.FromSeconds(40);

        var snapshot = race.CreateSnapshot();
        var takenUtc = utc0.AddMilliseconds(nowMs) + step;
        race.Reset();

        nowMs += 10_000;
        Assert.True(race.RestoreSnapshotAfterGap(snapshot, takenUtc));

        // Frozen where the red caught it, at 1:00.
        Assert.Equal(TimeSpan.FromMinutes(1), race.ElapsedRaceTime);
    }

    [Fact]
    public void A_race_restored_from_the_journal_keeps_its_clock_through_an_earlier_time_step()
    {
        // Green 2:00 ago on the race's own clock - but the wall clock was
        // stepped on 40 s in between, so the start stamp is 2:40 old.
        long nowMs = 1_000_000;
        var startStamp = DateTime.UtcNow - TimeSpan.FromSeconds(160);
        var stampAtGreen = true;
        var race = new RaceControlSession
        {
            NowMsSource = () => nowMs,
            UtcNowSource = () => stampAtGreen ? startStamp : DateTime.UtcNow,
        };

        race.Stage();
        race.GreenFlag(0);
        stampAtGreen = false;
        Seen(race, "A", 5_000);
        nowMs += 120_000;

        new RaceJournal().Record(new RaceTiming(race), DateTime.UtcNow, "Club oval", force: true);

        var entry = RaceJournal.Unfinished();
        Assert.NotNull(entry);
        Assert.Equal(120_000L, entry!.RaceClockMs);

        var restored = new RaceControlSession();
        RaceJournal.Restore(new RaceTiming(restored), entry);

        // 2:00, give or take the moment the test took - not 2:40.
        Assert.InRange(restored.ElapsedRaceTime.TotalSeconds, 119, 125);
    }

    // --- When the race was called to the grid -----------------------------------

    [Fact]
    public void A_stage_over_a_running_race_is_the_next_races_call_up()
    {
        long nowMs = 1_000_000;
        var utc0 = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            NowMsSource = () => nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(nowMs),
        };

        race.Stage();
        var firstCall = race.StagedAtUtc;
        nowMs += 60_000;
        race.GreenFlag(0);

        // A first-corner pile-up: Stage pressed over the race two minutes in.
        nowMs += 120_000;
        var snapshot = race.CreateSnapshot();
        var takenUtc = utc0.AddMilliseconds(nowMs);
        race.Stage();

        var secondCall = race.StagedAtUtc;
        Assert.Equal(utc0.AddMilliseconds(nowMs), secondCall);
        Assert.NotEqual(firstCall, secondCall);

        // The countdown after it keeps that stage's time, as it always did.
        nowMs += 30_000;
        race.StartCountdown();
        Assert.Equal(secondCall, race.StagedAtUtc);

        // Undo puts the abandoned race back, and its own call-up with it.
        race.RestoreSnapshotAfterGap(snapshot, takenUtc);
        Assert.Equal(firstCall, race.StagedAtUtc);
    }

    [Fact]
    public void A_countdown_over_a_running_race_is_the_next_races_call_up()
    {
        long nowMs = 1_000_000;
        var utc0 = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            NowMsSource = () => nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(nowMs),
        };

        race.Stage();
        race.GreenFlag(0);

        nowMs += 180_000;
        race.StartCountdown();

        Assert.Equal(utc0.AddMilliseconds(nowMs), race.StagedAtUtc);
    }
}
