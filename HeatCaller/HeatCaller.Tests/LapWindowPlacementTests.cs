namespace HeatCaller.Tests;

/// <summary>
/// Where Race Control's lap windows go (30 Oct 2026, asked for by Asher:
/// several lap breakdowns open at once, dragged wherever they are wanted).
/// See LapWindowPlacement and RaceControlControl.LapWindows.
/// </summary>
public sealed class LapWindowPlacementTests
{
    private static readonly Rectangle Laptop = new(0, 0, 1920, 1040);
    private static readonly Rectangle SecondMonitor = new(1920, 0, 1920, 1040);
    private static readonly Size Card = new(470, 480);

    [Fact]
    public void Each_new_window_takes_the_lowest_spot_free()
    {
        Assert.Equal(0, LapWindowPlacement.FreeSlot(Array.Empty<int>()));
        Assert.Equal(2, LapWindowPlacement.FreeSlot(new[] { 0, 1, 3 }));

        // The first window closed: the next one opened takes its spot back.
        Assert.Equal(0, LapWindowPlacement.FreeSlot(new[] { 1, 2 }));
    }

    [Fact]
    public void A_spot_left_on_the_second_monitor_opens_there_again()
    {
        var restored = LapWindowPlacement.Restore(new Point(2400, 300), Card, new[] { Laptop, SecondMonitor });

        Assert.Equal(new Rectangle(new Point(2400, 300), Card), restored);
    }

    [Fact]
    public void A_spot_on_a_monitor_that_is_gone_is_not_restored()
    {
        // The laptop taken home from the track: the second monitor is not
        // there, and a window put back on it would open off the edge.
        Assert.Null(LapWindowPlacement.Restore(new Point(2400, 300), Card, new[] { Laptop }));
    }

    [Fact]
    public void A_spot_hanging_off_the_bottom_is_pulled_back_onto_the_screen()
    {
        var restored = LapWindowPlacement.Restore(new Point(100, 900), Card, new[] { Laptop });

        Assert.NotNull(restored);
        Assert.True(Laptop.Contains(restored!.Value), $"{restored} is not wholly on the screen");
        Assert.Equal(100, restored.Value.X);
    }

    [Fact]
    public void A_window_that_would_open_across_two_screens_lands_on_the_one_it_is_mostly_on()
    {
        var fitted = LapWindowPlacement.Fit(new Rectangle(new Point(1800, 100), Card), new[] { Laptop, SecondMonitor });

        Assert.True(SecondMonitor.Contains(fitted), $"{fitted} is not wholly on the second monitor");
    }

    [Fact]
    public void Line_up_fills_a_column_then_starts_the_next()
    {
        var placed = LapWindowPlacement.LineUp(new[] { Card, Card, Card }, Laptop, 10);

        Assert.Equal(new Point(10, 10), placed[0].Location);
        Assert.Equal(new Point(10, 500), placed[1].Location);

        // A third would run off the bottom - a new column beside the first.
        Assert.Equal(new Point(490, 10), placed[2].Location);
        Assert.All(placed, r => Assert.True(Laptop.Contains(r), $"{r} is off the screen"));
    }

    [Fact]
    public void Line_up_keeps_a_full_heat_on_the_screen()
    {
        var sizes = Enumerable.Repeat(Card, 10).ToList();

        var placed = LapWindowPlacement.LineUp(sizes, Laptop, 10);

        Assert.Equal(10, placed.Count);
        Assert.All(placed, r => Assert.True(Laptop.Contains(r), $"{r} is off the screen"));
    }

    [Fact]
    public void New_windows_step_down_and_right_of_the_last_one()
    {
        Assert.Equal(new Point(128, 228), LapWindowPlacement.Cascade(new Point(100, 200), 28));
    }
}

/// <summary>The lap window itself: sized in whole laps, within its limits.</summary>
public sealed class LapDetailWindowTests
{
    [Fact]
    public void It_is_sized_in_whole_laps_and_kept_within_its_limits()
    {
        var (asked, tooMany, tooFew, sameHeight, mostOnScreen) = StaThread.Run(() =>
        {
            using var window = new LapDetailWindow("T1", "Alice", "#7  ·  T1", 3, slot: 0, rows: 12);
            var twelve = window.Rows;
            var height = window.ClientSize.Height;

            window.SizeFor(500);
            var most = window.Rows;

            window.SizeFor(1);
            var fewest = window.Rows;

            window.SizeFor(12);

            return (twelve, most, fewest, window.ClientSize.Height == height, LapDetailWindow.MostRows);
        }, TimeSpan.FromSeconds(30), () => "sizing a lap window");

        Assert.Equal(12, asked);

        // As many as the limit allows, or as fit on this machine's tallest
        // screen when that is fewer - 33 on a 1080p screen with a taskbar.
        Assert.Equal(mostOnScreen, tooMany);
        Assert.InRange(tooMany, LapDetailWindow.MinRows, LapDetailWindow.MaxRows);
        Assert.Equal(LapDetailWindow.MinRows, tooFew);
        Assert.True(sameHeight);
    }
}
