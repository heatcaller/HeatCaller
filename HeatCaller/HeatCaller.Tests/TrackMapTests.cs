namespace HeatCaller.Tests;

/// <summary>
/// Batch three: the live track map (TrackLine, TrackPositions) and the
/// stopped-car locator - where each car is between loops, and when one has
/// probably stopped.
/// </summary>
public sealed class TrackMapTests
{
    /// <summary>
    /// A 10x10-tile square lap, run clockwise from the top-left corner: the
    /// finish line a third of the way along the top, Split 1 halfway down
    /// the right-hand side - a quarter of the lap after the line.
    /// </summary>
    private static RcpTrackDesign Square(bool withSplit = true, bool withFinish = true, bool closed = true)
    {
        var design = new RcpTrackDesign { RouteClosed = closed };

        foreach (var (x, y) in new[] { (0f, 0f), (10f, 0f), (10f, 10f), (0f, 10f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        if (withFinish)
        {
            design.Decals.Add(new RcpDecal { Kind = RcpDecalKind.FinishLine, Col = 4, Row = 0 });
        }

        if (withSplit)
        {
            design.Decals.Add(new RcpDecal { Kind = RcpDecalKind.SplitLoop1, Col = 9, Row = 4 });
        }

        return design;
    }

    [Fact]
    public void The_line_starts_at_the_finish_and_knows_where_the_split_is()
    {
        var line = TrackLine.From(Square())!;

        Assert.Equal(40f, line.Length, 3);
        Assert.Equal(0.25f, Assert.Single(line.SplitAt), 3);

        var start = line.At(0);
        Assert.Equal(4.5f, start.X, 3);
        Assert.Equal(0f, start.Y, 3);

        var split = line.At(0.25f);
        Assert.Equal(10f, split.X, 3);
        Assert.Equal(4.5f, split.Y, 3);

        // Round past the line wraps back to the start of the lap.
        Assert.Equal(line.At(0.1f).X, line.At(1.1f).X, 3);
        Assert.Equal(line.At(0.1f).Y, line.At(1.1f).Y, 3);
    }

    [Fact]
    public void A_plan_without_a_closed_line_or_a_finish_line_has_no_map()
    {
        Assert.Null(TrackLine.From(null));
        Assert.Null(TrackLine.From(Square(closed: false)));
        Assert.Null(TrackLine.From(Square(withFinish: false)));

        // No split loops is fine - the map works from the line alone.
        Assert.Empty(TrackLine.From(Square(withSplit: false))!.SplitAt);
    }

    [Fact]
    public void A_car_moves_round_at_its_usual_pace_and_waits_at_the_next_loop()
    {
        var noSplits = Array.Empty<float>();

        // A quarter of an eight-second lap after the line: a quarter round.
        Assert.Equal(0.25f, TrackPositions.Place("A", 1, lineAt: 1000, 0, 0, noSplits, 8000, nowMs: 3000, racing: true).Fraction, 3);

        // Overdue at the line, but not yet called stopped: held just short of it.
        var held = TrackPositions.Place("A", 1, lineAt: 1000, 0, 0, noSplits, 8000, nowMs: 10_000, racing: true);
        Assert.False(held.Stopped);
        Assert.Equal(0.99f, held.Fraction, 3);
    }

    [Fact]
    public void A_split_moves_the_car_on_from_there()
    {
        var splits = new[] { 0.25f };

        // Held before Split 1 until it is heard there...
        Assert.Equal(0.24f, TrackPositions.Place("A", 1, lineAt: 0, 0, 0, splits, 8000, nowMs: 2500, racing: true).Fraction, 3);

        // ...and moving on from it once it is.
        Assert.Equal(0.5f, TrackPositions.Place("A", 1, lineAt: 0, split: 1, splitAt: 2000, splits, 8000, nowMs: 4000, racing: true).Fraction, 3);
    }

    [Fact]
    public void A_car_long_overdue_at_the_next_loop_is_called_stopped_in_that_stretch()
    {
        var splits = new[] { 0.25f };

        // Two seconds to Split 1 is usual; ten seconds and more is not.
        var stopped = TrackPositions.Place("A", 3, lineAt: 0, 0, 0, splits, 8000, nowMs: 10_001, racing: true);

        Assert.True(stopped.Stopped);
        Assert.Equal("between the line and Split 1", stopped.Where);
        Assert.Equal(0.125f, stopped.Fraction, 3);
        Assert.Equal(0f, stopped.Rate);

        // Not while the race isn't on - a car parked after the flag is not news.
        Assert.False(TrackPositions.Place("A", 3, lineAt: 0, 0, 0, splits, 8000, nowMs: 10_001, racing: false).Stopped);

        Assert.Equal("between Split 1 and the line", TrackPositions.Stretch(1, 1));
        Assert.Equal("somewhere on the lap", TrackPositions.Stretch(0, 0));
    }

    [Fact]
    public void A_stop_is_said_once_until_the_car_is_heard_again()
    {
        var line = TrackLine.From(Square(withSplit: false))!;
        var positions = new TrackPositions();
        var said = new List<string>();
        positions.CarStopped += (standing, where) => said.Add(standing.TransponderId + " " + where);

        var car = new RacerStanding { TransponderId = "A", Laps = 3, ElapsedMs = 24_000 };
        car.LapTimesMs.AddRange(new long[] { 8000, 8000, 8000 });

        positions.Crossed("A", TimingPoint.Line, 1000);

        Assert.False(Assert.Single(positions.Estimate(new[] { car }, line, 5000, racing: true)).Stopped);

        positions.Estimate(new[] { car }, line, 40_000, racing: true);
        positions.Estimate(new[] { car }, line, 41_000, racing: true);
        Assert.Equal(new[] { "A somewhere on the lap" }, said);

        // Heard at the line again, then stopped again: said again.
        positions.Crossed("A", TimingPoint.Line, 42_000);
        positions.Estimate(new[] { car }, line, 80_000, racing: true);
        Assert.Equal(2, said.Count);
    }

    [Fact]
    public void A_car_not_yet_heard_this_race_is_not_placed()
    {
        var line = TrackLine.From(Square())!;
        var car = new RacerStanding { TransponderId = "B", Laps = 0 };

        Assert.Empty(new TrackPositions().Estimate(new[] { car }, line, 5000, racing: true));
    }
}
