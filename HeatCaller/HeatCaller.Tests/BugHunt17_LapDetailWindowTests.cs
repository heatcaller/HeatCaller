using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), a lap window that stopped following the race.
///
/// Scrolled back a couple of laps and then dragged tall enough to show
/// every lap, the window had no scrollbar and said "Updates live" - but the
/// laps it had been scrolled by were still remembered, so the next lap in
/// counted it as scrolled back and pinned it to the oldest laps for the
/// rest of the race.
/// </summary>
public sealed class BugHunt17_LapDetailWindowTests
{
    private static RacerStanding Car(int laps)
    {
        var car = new RacerStanding { TransponderId = "T1", Laps = laps, ElapsedMs = laps * 10_000L };
        car.LapTimesMs.AddRange(Enumerable.Repeat(10_000L, laps));
        return car;
    }

    [Fact]
    public void Scrolled_back_and_then_made_tall_enough_for_every_lap_it_follows_the_newest_again()
    {
        var (scrolled, afterGrowing, afterNextLap, afterAnother) = StaThread.Run(() =>
        {
            using var window = new LapDetailWindow("T1", "Alice", "#7  ·  T1", 3, slot: 0, rows: 4);

            window.Apply(Car(6), 1);
            window.ScrollKey(Keys.Up);
            window.ScrollKey(Keys.Up);
            var back = window.ScrolledBack;

            // Every lap fits now, so there is nothing to be scrolled back in.
            window.SizeFor(6);
            var grown = window.ScrolledBack;

            window.Apply(Car(7), 1);
            var next = window.ScrolledBack;

            window.Apply(Car(8), 1);

            return (back, grown, next, window.ScrolledBack);
        }, TimeSpan.FromSeconds(30), () => "scrolling a lap window");

        Assert.Equal(2, scrolled);
        Assert.Equal(0, afterGrowing);
        Assert.Equal(0, afterNextLap);
        Assert.Equal(0, afterAnother);
    }

    [Fact]
    public void Still_scrolled_back_it_keeps_the_same_laps_on_screen_as_new_ones_come_in()
    {
        var (before, after) = StaThread.Run(() =>
        {
            using var window = new LapDetailWindow("T1", "Alice", "#7  ·  T1", 3, slot: 0, rows: 4);

            window.Apply(Car(8), 1);
            window.ScrollKey(Keys.Up);
            window.ScrollKey(Keys.Up);
            var back = window.ScrolledBack;

            window.Apply(Car(9), 1);

            return (back, window.ScrolledBack);
        }, TimeSpan.FromSeconds(30), () => "scrolling a lap window");

        Assert.Equal(2, before);
        Assert.Equal(3, after);
    }
}
