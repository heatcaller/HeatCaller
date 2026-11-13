namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: the track map waited at every split loop drawn on the plan,
/// whether or not a decoder was behind it. With the split loop switched off
/// or unplugged, every car sat just short of Split 1 for the rest of each
/// lap and was called stopped there, lap after lap. A car now waits only at
/// the split loops that have heard it this race.
/// </summary>
public class BugHunt17_TrackMapTests
{
    /// <summary>A 40-tile square lap with the line on the top side and Split 1 a quarter of the lap after it.</summary>
    private static TrackLine SquareWithSplit()
    {
        var design = new RcpTrackDesign { RouteClosed = true };

        foreach (var (x, y) in new[] { (0f, 0f), (10f, 0f), (10f, 10f), (0f, 10f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        design.Decals.Add(new RcpDecal { Kind = RcpDecalKind.FinishLine, Col = 4, Row = 0 });
        design.Decals.Add(new RcpDecal { Kind = RcpDecalKind.SplitLoop1, Col = 9, Row = 4 });

        var line = TrackLine.From(design)!;
        Assert.Equal(0.25f, Assert.Single(line.SplitAt), 3);
        return line;
    }

    private static RacerStanding TwelveSecondLaps()
    {
        var car = new RacerStanding { TransponderId = "A", Laps = 3, ElapsedMs = 36_000 };
        car.LapTimesMs.AddRange(new long[] { 12_000, 12_000, 12_000 });
        return car;
    }

    [Fact]
    public void A_split_on_the_plan_that_never_hears_the_car_is_not_waited_for()
    {
        var line = SquareWithSplit();
        var positions = new TrackPositions();
        var said = new List<string>();
        positions.CarStopped += (_, where) => said.Add(where);

        var car = TwelveSecondLaps();
        positions.Crossed("A", TimingPoint.Line, 1000);

        // 11.5 seconds into a 12-second lap, nothing heard at Split 1 - there
        // is no decoder there tonight. Nearly round, not stopped short of it.
        var placed = Assert.Single(positions.Estimate(new[] { car }, line, 12_500, racing: true));

        Assert.False(placed.Stopped);
        Assert.Equal(11.5f / 12f, placed.Fraction, 3);
        Assert.Equal(0.99f, placed.Cap, 3);
        Assert.Empty(said);

        // Still found when it really has stopped - somewhere on the lap,
        // which is all the line alone can say.
        Assert.True(Assert.Single(positions.Estimate(new[] { car }, line, 40_000, racing: true)).Stopped);
        Assert.Equal(new[] { "somewhere on the lap" }, said);
    }

    [Fact]
    public void A_split_that_has_heard_the_car_this_race_is_still_waited_for()
    {
        var line = SquareWithSplit();
        var positions = new TrackPositions();

        var car = TwelveSecondLaps();
        positions.Crossed("A", TimingPoint.Line, 1000);
        positions.Crossed("A", TimingPoint.Split1, 4000);
        positions.Crossed("A", TimingPoint.Line, 13_000);

        // Held short of Split 1 until it is heard there again...
        var held = Assert.Single(positions.Estimate(new[] { car }, line, 18_000, racing: true));
        Assert.False(held.Stopped);
        Assert.Equal(0.24f, held.Fraction, 3);

        // ...and well overdue there, stopped in that stretch.
        var stopped = Assert.Single(positions.Estimate(new[] { car }, line, 24_500, racing: true));
        Assert.True(stopped.Stopped);
        Assert.Equal("between the line and Split 1", stopped.Where);
    }

    [Fact]
    public void A_new_race_forgets_which_splits_heard_the_car()
    {
        var line = SquareWithSplit();
        var positions = new TrackPositions();

        var car = TwelveSecondLaps();
        positions.Crossed("A", TimingPoint.Line, 1000);
        positions.Crossed("A", TimingPoint.Split1, 4000);

        positions.Clear();
        positions.Crossed("A", TimingPoint.Line, 100_000);

        Assert.False(Assert.Single(positions.Estimate(new[] { car }, line, 111_500, racing: true)).Stopped);
    }
}
