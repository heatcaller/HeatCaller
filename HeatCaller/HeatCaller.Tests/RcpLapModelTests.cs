namespace HeatCaller.Tests;

/// <summary>
/// The lap, always on screen (Track Designer overhaul, Phase 6, 25 Oct
/// 2026): the estimate, laps raced, and which the board shows.
/// </summary>
public sealed class RcpLapModelTests
{
    /// <summary>A two-tile ring lane cols x rows, traced, on 50cm tiles.</summary>
    private static RcpTrackDesign Ring(int cols, int rows, int turns = 1)
    {
        var design = new RcpTrackDesign { TileSizeMm = 500, LaneTurnRadius = turns };

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(cols - 1, rows - 1), 2)!)
        {
            design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Lane = 1 });
        }

        RcpAutoRails.Apply(design);

        var route = RcpPlan.AutoRoute(design, out _)!;
        design.Route = route.Select(p => new RcpRoutePoint { X = p.X, Y = p.Y }).ToList();
        design.RouteClosed = true;

        return design;
    }

    private static SessionRecord Session(string track, DateTime startedUtc, params long[] lapsMs) => new()
    {
        TrackLayoutName = track,
        StartedAtUtc = startedUtc,
        Results = { new SessionResultEntry { LapTimesMs = lapsMs.ToList() } },
    };

    private static long[] Laps(int count, long ms) => Enumerable.Repeat(ms, count).ToArray();

    [Fact]
    public void A_longer_lap_of_the_same_shape_is_slower()
    {
        var shorter = RcpLapModel.Estimate(Ring(10, 8))!.Value;
        var longer = RcpLapModel.Estimate(Ring(16, 8))!.Value;

        Assert.True(longer > shorter, $"{longer} vs {shorter}");
    }

    [Fact]
    public void A_tighter_turn_is_slower()
    {
        var sweepers = RcpLapModel.Estimate(Ring(12, 8, turns: 2))!.Value;
        var square = RcpLapModel.Estimate(Ring(12, 8, turns: 0))!.Value;

        Assert.True(square > sweepers, $"{square} vs {sweepers}");
    }

    [Fact]
    public void A_lap_is_a_believable_time_for_a_Mini_Z()
    {
        // 12 x 8 tiles of 50cm, two wide: about 32 m of line - a Mini-Z
        // lap of somewhere around ten seconds, not one or a hundred.
        var seconds = RcpLapModel.Estimate(Ring(12, 8))!.Value;

        Assert.InRange(seconds, 5.0, 20.0);
    }

    [Fact]
    public void A_straight_long_enough_is_mostly_at_top_speed()
    {
        var car = RcpLapModel.Car.MiniZ;
        var seconds = RcpLapModel.Straight(100, 1, 1, car);

        Assert.InRange(seconds, 100 / car.TopSpeed, 100 / car.TopSpeed + 1.5);
    }

    [Fact]
    public void The_real_time_beats_the_estimate_and_the_entered_one()
    {
        var design = Ring(12, 8);

        var raced = RcpLapModel.Choose(design, (9.4, 120), entered: 11, lineChanged: false);
        var entered = RcpLapModel.Choose(design, null, entered: 11, lineChanged: false);
        var estimate = RcpLapModel.Choose(design, null, entered: null, lineChanged: false);

        Assert.Equal(RcpLapSource.Raced, raced.Source);
        Assert.Equal(9.4, raced.Seconds);
        Assert.Equal("from 120 laps raced", raced.Says);

        Assert.Equal(RcpLapSource.Entered, entered.Source);
        Assert.Equal(RcpLapSource.Estimate, estimate.Source);
    }

    [Fact]
    public void A_line_changed_since_it_was_raced_takes_the_estimate()
    {
        var design = Ring(12, 8);

        var changed = RcpLapModel.Choose(design, (9.4, 120), entered: 11, lineChanged: true);

        Assert.Equal(RcpLapSource.Estimate, changed.Source);

        // And a line changed since races were filed leaves those races out.
        var changedAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

        var sessions = new[]
        {
            Session("Club Hall", changedAt.AddDays(-3), Laps(30, 8000)),
            Session("Club Hall", changedAt.AddDays(2), Laps(12, 10000)),
        };

        Assert.Equal(10.0, RcpLapModel.Raced(sessions, "Club Hall", changedAt)!.Value.Seconds);
        Assert.Equal(42, RcpLapModel.Raced(sessions, "Club Hall", null)!.Value.Laps);
    }

    [Fact]
    public void Raced_laps_leave_out_other_tracks_demo_races_crashes_and_too_few()
    {
        var when = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var sessions = new[]
        {
            Session("club hall ", when, Laps(10, 9000).Concat(new long[] { 60000 }).ToArray()),
            Session("Garage", when, Laps(50, 5000)),
            new SessionRecord { TrackLayoutName = "Club Hall", StartedAtUtc = when, IsDemo = true, Results = { new SessionResultEntry { LapTimesMs = Laps(50, 1000).ToList() } } },
        };

        var raced = RcpLapModel.Raced(sessions, "Club Hall", null)!.Value;

        Assert.Equal(9.0, raced.Seconds);
        Assert.Equal(10, raced.Laps);

        Assert.Null(RcpLapModel.Raced(new[] { Session("Club Hall", when, Laps(9, 9000)) }, "Club Hall", null));
    }

    [Fact]
    public void The_estimate_tunes_itself_to_the_clubs_real_laps()
    {
        var a = Ring(12, 8);
        var b = Ring(16, 10);

        var aEstimate = RcpLapModel.Estimate(a)!.Value;
        var bEstimate = RcpLapModel.Estimate(b)!.Value;

        // The club's cars are 20% slower than the model on both.
        var (tuning, layouts) = RcpLapModel.Calibrate(new[] { (a, aEstimate * 1.2), (b, bEstimate * 1.2) });

        Assert.Equal(2, layouts);
        Assert.Equal(1.2, tuning, 6);

        var tuned = RcpLapModel.Choose(Ring(14, 8), null, null, lineChanged: false, tuning, layouts);

        Assert.Equal(RcpLapModel.Estimate(Ring(14, 8))!.Value * 1.2, tuned.Seconds, 6);
        Assert.Equal(2, tuned.Tuned);

        Assert.Equal((1.0, 0), RcpLapModel.Calibrate(Array.Empty<(RcpTrackDesign, double)>()));
    }

    [Fact]
    public void No_closed_line_no_lap()
    {
        var open = Ring(12, 8);
        open.RouteClosed = false;

        Assert.Null(RcpLapModel.Estimate(open));
        Assert.Equal(RcpLapSource.None, RcpLapModel.Choose(new RcpTrackDesign(), (9, 20), 10, false).Source);
    }

    [Fact]
    public void A_line_with_one_corner_or_a_turn_back_still_has_a_lap()
    {
        // Out along a row, then turned straight back: no right angle at
        // all, and a single kink.
        var design = new RcpTrackDesign
        {
            TileSizeMm = 500,
            RouteClosed = true,
            Route =
            {
                new RcpRoutePoint { X = 0, Y = 0 },
                new RcpRoutePoint { X = 5, Y = 0 },
                new RcpRoutePoint { X = 5, Y = 0 },
                new RcpRoutePoint { X = 10, Y = 0.5f },
            },
        };

        Assert.True(RcpLapModel.Estimate(design) > 0);
    }

    [Fact]
    public void The_same_lap_from_another_start_or_the_other_way_is_the_same_line()
    {
        var line = Ring(12, 8).Route;
        var turned = line.Skip(5).Concat(line.Take(5)).ToList();
        var reversed = new[] { line[0] }.Concat(line.Skip(1).Reverse()).ToList();
        var moved = line.Select(p => new RcpRoutePoint { X = p.X + 1, Y = p.Y }).ToList();

        Assert.True(RcpLapModel.SameLine(line, turned));
        Assert.True(RcpLapModel.SameLine(line, reversed));
        Assert.False(RcpLapModel.SameLine(line, moved));
        Assert.False(RcpLapModel.SameLine(line, turned, closed: false));
        Assert.True(RcpLapModel.SameLine(null, new List<RcpRoutePoint>()));
    }

    [Fact]
    public void After_a_changed_line_is_saved_the_entered_lap_gives_way_to_the_estimate()
    {
        var design = Ring(12, 8);
        design.RouteChangedUtc = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(RcpLapSource.Estimate, RcpLapModel.Choose(design, null, entered: 11, lineChanged: false).Source);
    }

    [Fact]
    public void One_wild_layout_cannot_drag_the_tuning_past_three_times()
    {
        var a = Ring(12, 8);
        var (tuning, _) = RcpLapModel.Calibrate(new[] { (a, RcpLapModel.Estimate(a)!.Value * 50) });

        Assert.Equal(3.0, tuning, 6);
    }

    [Fact]
    public void A_corner_at_top_speed_is_not_taken_for_a_short_straight()
    {
        var car = new RcpLapModel.Car(TopSpeed: 4, Accelerate: 5, Brake: 7, Grip: 6);

        Assert.Equal(100 / 4.0, RcpLapModel.Straight(100, 4, 4, car), 6);
    }
}
