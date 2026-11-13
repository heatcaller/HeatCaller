namespace HeatCaller.Tests;

/// <summary>
/// Build me a track (Track Designer overhaul, Phase 5, 25 Oct 2026): every
/// layout offered fits the room and the cupboard, and its line closes.
/// </summary>
public sealed class RcpLayoutGeneratorTests
{
    private static readonly (double Width, double Depth, double Tile)[] Rooms =
    {
        (6100, 4300, 500),
        (9000, 6000, 500),
        (3700, 2800, 300),
        (4300, 6100, 500),
        (14000, 8000, 500),
    };

    private static RcpTrackDesign Room(double width, double depth, double tile) =>
        new() { TileSizeMm = tile, RoomWidthMm = width, RoomDepthMm = depth, RoomInFeet = false };

    [Fact]
    public void Every_layout_offered_fits_the_room_with_room_to_walk_round_it()
    {
        foreach (var (width, depth, tile) in Rooms)
        {
            var room = Room(width, depth, tile);
            var offered = RcpLayoutGenerator.For(room, null, 2);

            Assert.Contains(offered, l => !l.RcpOwn);

            foreach (var layout in offered)
            {
                var bounds = layout.Design.BoundsInTiles(includeRoute: false);
                var fit = RoomFit.Check(bounds.Width * tile, bounds.Height * tile, width, depth);

                Assert.True(fit.Verdict == RoomFit.Verdict.Comfortable, $"{layout.Name} in {width} x {depth}: {fit.Says}");
            }
        }
    }

    [Fact]
    public void Every_worked_out_layout_is_a_lane_with_a_closed_line_and_a_finish_line()
    {
        foreach (var (width, depth, tile) in Rooms)
        {
            foreach (var layout in RcpLayoutGenerator.For(Room(width, depth, tile), null, 2).Where(l => !l.RcpOwn))
            {
                var design = layout.Design;

                Assert.True(design.RouteClosed && design.RouteAuto, layout.Name);
                Assert.True(design.Route.Count >= 4, layout.Name);
                Assert.True(layout.LapTiles > 0, layout.Name);
                Assert.Contains(design.Decals, d => d.Kind == RcpDecalKind.FinishLine);

                Assert.All(design.Tiles, t => Assert.True(t.Lane > 0));
                Assert.All(design.Walls, w => Assert.True(w.Auto));
                Assert.All(design.Curves, c => Assert.True(c.Auto));

                // Traced again, the line comes out the same: nothing about it
                // was drawn by hand.
                var again = RcpPlan.AutoRoute(design, out var problem);

                Assert.Equal(string.Empty, problem);
                Assert.Equal(design.Route.Select(p => p.Point).ToArray(), again!.ToArray());

                // And the rails are what the lane says they are.
                Assert.False(RcpAutoRails.Apply(design).Changed, layout.Name);
            }
        }
    }

    [Fact]
    public void Every_layout_uses_no_more_than_the_club_owns()
    {
        var stock = new RcpStock();
        stock.Sets["50cm Wide-L / Oblong"] = 1;
        stock.LooseFor(500).Tiles = 20;
        stock.LooseFor(500).RailTiles = 20;

        var room = Room(9000, 6000, 500);
        var worked = RcpLayoutGenerator.For(room, stock, 2).Where(l => !l.RcpOwn).ToList();

        Assert.NotEmpty(worked);

        foreach (var layout in worked)
        {
            Assert.DoesNotContain(stock.Check(layout.Used, 500), line => line.Short);
        }

        // Smaller than the same room with nothing entered: it was shrunk to fit.
        var unlimited = RcpLayoutGenerator.For(room, null, 2).Where(l => !l.RcpOwn).Max(l => l.Used.Tiles);
        Assert.True(worked.Max(l => l.Used.Tiles) < unlimited);
    }

    [Fact]
    public void The_worked_out_layouts_come_longest_lap_first()
    {
        var laps = RcpLayoutGenerator.For(Room(9000, 6000, 500), null, 2)
            .Where(l => !l.RcpOwn)
            .Select(l => l.LapTiles)
            .ToList();

        Assert.Equal(laps.OrderByDescending(l => l).ToList(), laps);
    }

    [Fact]
    public void RCPs_own_come_first_and_only_those_that_fit()
    {
        var offered = RcpLayoutGenerator.For(Room(6100, 4300, 500), null, 2);
        var own = offered.TakeWhile(l => l.RcpOwn).Count();

        Assert.True(own > 0);
        Assert.Equal(own, offered.Count(l => l.RcpOwn));

        // The 96-tile track is 20ft x 13ft: not in this room.
        Assert.DoesNotContain(offered, l => l.Name.Contains("Large-96"));
    }

    [Fact]
    public void Three_tile_lanes_work_too()
    {
        var worked = RcpLayoutGenerator.For(Room(14000, 8000, 500), null, 3).Where(l => !l.RcpOwn).ToList();

        Assert.True(worked.Count >= 4);
        Assert.All(worked, l => Assert.True(l.Design.RouteClosed, l.Name));
    }

    [Fact]
    public void No_room_given_offers_nothing()
    {
        Assert.Empty(RcpLayoutGenerator.For(new RcpTrackDesign { TileSizeMm = 500 }, null, 2));
    }

    [Fact]
    public void A_room_too_small_for_any_shape_offers_none_of_its_own()
    {
        Assert.DoesNotContain(RcpLayoutGenerator.For(Room(2500, 2500, 500), null, 2), l => !l.RcpOwn);
    }

    [Fact]
    public void Using_a_layout_is_one_undo_step_and_keeps_the_room()
    {
        var seen = StaThread.Run(() =>
        {
            using var canvas = new RcpTrackCanvas { Size = new Size(1200, 900) };

            var room = Room(9000, 6000, 500);
            room.Tiles.Add(new RcpTile { Col = 40, Row = 40 });
            canvas.Design = room;

            var layout = RcpLayoutGenerator.For(canvas.Design, null, 2).First(l => !l.RcpOwn);
            canvas.LoadBuilt(layout.Design);

            var loaded = (tiles: canvas.Design.Tiles.Count, auto: canvas.Design.RouteAuto, roomKept: canvas.Design.RoomWidthMm);

            canvas.Undo();

            return (loaded, expected: layout.Design.Tiles.Count, after: canvas.Design.Tiles.Count);
        }, TimeSpan.FromSeconds(30), () => "laying out a built track");

        Assert.Equal(seen.expected, seen.loaded.tiles);
        Assert.True(seen.loaded.auto);
        Assert.Equal(9000, seen.loaded.roomKept);
        Assert.Equal(1, seen.after);
    }

    [Fact]
    public void The_finish_line_lies_across_the_track()
    {
        foreach (var layout in RcpLayoutGenerator.For(Room(9000, 6000, 500), null, 2).Where(l => !l.RcpOwn))
        {
            var finish = layout.Design.Decals.Where(d => d.Kind == RcpDecalKind.FinishLine).ToList();

            // Across a lane two wide: two tiles side by side across the
            // direction of travel, each turned to lie across it.
            Assert.Equal(2, finish.Count);

            var alongRow = finish[0].Col == finish[1].Col;

            Assert.All(finish, d => Assert.Equal(alongRow ? 1 : 0, d.Turns));
        }
    }

    [Fact]
    public void RCPs_own_keep_the_plans_lane_turns()
    {
        var room = Room(6100, 4300, 500);
        room.LaneTurnRadius = 2;

        Assert.All(RcpLayoutGenerator.For(room, null, 2).Where(l => l.RcpOwn), l => Assert.Equal(2, l.Design.LaneTurnRadius));
    }

    [Fact]
    public void A_big_hall_and_a_small_cupboard_is_quick_and_keeps_the_halls_shape()
    {
        var stock = new RcpStock();
        stock.Sets["50cm Wide-L / Oblong"] = 2;

        var room = Room(20000, 8000, 500);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var worked = RcpLayoutGenerator.For(room, stock, 2).Where(l => !l.RcpOwn).ToList();

        Assert.True(clock.ElapsedMilliseconds < 3000, $"took {clock.ElapsedMilliseconds}ms");
        Assert.NotEmpty(worked);

        // Shrunk to the cupboard, but still longer than it is deep, as the hall is.
        Assert.All(worked, l =>
        {
            var bounds = l.Design.BoundsInTiles(includeRoute: false);
            Assert.True(bounds.Width > bounds.Height, $"{l.Name} came out {bounds.Width} x {bounds.Height}");
        });
    }
}
