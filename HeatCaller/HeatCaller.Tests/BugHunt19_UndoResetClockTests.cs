namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19 (14 Nov 2026), #16: a reset undone after the Windows clock
/// was stepped between the reset and the undo.
///
/// The gap was measured on the wall clock and the start's monotonic reading
/// moved by it, so a time sync stepping the clock back 30 s during a
/// minute's wait brought the race clock back 30 s ahead, and the checkered
/// came out with 20 s of racing left instead of 50.
/// </summary>
public sealed class BugHunt19_UndoResetClockTests
{
    private static readonly DateTime Utc0 = new(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-30)]
    [InlineData(-90)]
    [InlineData(40)]
    public void A_reset_undone_after_a_time_step_comes_back_where_the_race_clock_stood(int stepSeconds)
    {
        long nowMs = 1_000_000;
        var step = TimeSpan.Zero;
        var race = new RaceControlSession
        {
            TimeLimit = TimeSpan.FromMinutes(5),
            NowMsSource = () => nowMs,
            UtcNowSource = () => Utc0.AddMilliseconds(nowMs) + step,
        };

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(0);

        // Reset at 4:10 of the 5:00 heat.
        nowMs += 250_000;
        var snapshot = race.CreateSnapshot();
        var takenUtc = Utc0.AddMilliseconds(nowMs) + step;
        race.Reset();

        // While the director decides, Windows syncs its clock. Undo a minute later.
        step = TimeSpan.FromSeconds(stepSeconds);
        nowMs += 60_000;
        Assert.True(race.RestoreSnapshotAfterGap(snapshot, takenUtc));

        // Back at 4:10 under red, with the whole stoppage left out.
        Assert.Equal(FlagState.Red, race.Flag);
        Assert.Equal(TimeSpan.FromSeconds(250), race.ElapsedRaceTime);

        // And the limit is still 50 seconds of racing away.
        race.GreenFlag(0);
        nowMs += 45_000;
        race.Tick();
        Assert.False(race.IsFinishing);
        Assert.Equal(TimeSpan.FromSeconds(295), race.ElapsedRaceTime);
    }
}
