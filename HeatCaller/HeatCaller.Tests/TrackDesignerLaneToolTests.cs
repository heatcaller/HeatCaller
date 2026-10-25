using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The Lane, Ring and Corners tools and the self-tracing line, driven
/// through the board's own mouse handling (Track Designer overhaul, Phase
/// 3, 25 Oct 2026). The board opens at 64 pixels a tile with the plan's
/// corner 40 pixels in, so cell (c, r) is at 40 + 64c + 32 across.
/// </summary>
public sealed class TrackDesignerLaneToolTests
{
    private static Point CellCentre(int col, int row) => new(40 + 64 * col + 32, 40 + 64 * row + 32);

    private static Point GridPoint(int col, int row) => new(40 + 64 * col, 40 + 64 * row);

    private static void Mouse(RcpTrackCanvas canvas, string what, Point at, MouseButtons button = MouseButtons.Left)
    {
        typeof(RcpTrackCanvas)
            .GetMethod(what, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, new object[] { new MouseEventArgs(button, 1, at.X, at.Y, 0) });
    }

    private static void Drag(RcpTrackCanvas canvas, params Point[] path)
    {
        Mouse(canvas, "OnMouseDown", path[0]);

        foreach (var point in path.Skip(1))
        {
            Mouse(canvas, "OnMouseMove", point);
        }

        Mouse(canvas, "OnMouseUp", path[^1]);
    }

    private static void Click(RcpTrackCanvas canvas, Point at, MouseButtons button = MouseButtons.Left)
    {
        Mouse(canvas, "OnMouseDown", at, button);
        Mouse(canvas, "OnMouseUp", at, button);
    }

    private static T OnBoard<T>(Func<RcpTrackCanvas, T> test) =>
        StaThread.Run(() =>
        {
            using var canvas = new RcpTrackCanvas { Size = new Size(1200, 900) };
            return test(canvas);
        }, TimeSpan.FromSeconds(30), () => "drawing on the Track Designer's board");

    [Fact]
    public void A_dragged_lane_is_floor_with_rails_and_one_undo_takes_it_back()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Lane;
            canvas.LaneWidth = 1;

            // Along the top and down the right: an L.
            Drag(canvas, CellCentre(0, 0), CellCentre(4, 0), CellCentre(4, 4));

            var design = canvas.Design;
            var tiles = design.Tiles.Count;
            var lanes = design.Tiles.Select(t => t.Lane).Distinct().ToList();
            var autoWalls = design.Walls.All(w => w.Auto) && design.Walls.Count > 0;
            var turns = design.Curves.Count;

            canvas.Undo();

            return (tiles, lanes, autoWalls, turns, after: canvas.Design.Tiles.Count + canvas.Design.Walls.Count + canvas.Design.Curves.Count);
        });

        Assert.Equal(9, seen.tiles);
        Assert.Equal(new[] { 1 }, seen.lanes);
        Assert.True(seen.autoWalls);
        Assert.Equal(1, seen.turns);
        Assert.Equal(0, seen.after);
    }

    [Fact]
    public void A_ring_then_Trace_route_and_the_line_follows_the_track_after()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Lane;
            canvas.LaneRing = true;
            canvas.LaneWidth = 1;

            Drag(canvas, CellCentre(0, 0), CellCentre(5, 3), CellCentre(7, 5));

            var ringTiles = canvas.Design.Tiles.Count;
            var ringTurns = canvas.Design.Curves.Count;

            canvas.AutoRoute();

            var traced = canvas.Design.Route.Count;
            var auto = canvas.Design.RouteAuto;

            // Take the ring away tile by tile with the right button - the
            // line stays as it was while the track is broken - and lay a
            // bigger one: the line is traced round that by itself.
            canvas.Tool = RcpTool.Select;

            foreach (var tile in canvas.Design.Tiles.ToList())
            {
                Click(canvas, CellCentre(tile.Col, tile.Row), MouseButtons.Right);
            }

            var whileBroken = canvas.Design.Route.Count;

            canvas.Tool = RcpTool.Lane;
            Drag(canvas, CellCentre(0, 0), CellCentre(9, 7));

            return (ringTiles, ringTurns, traced, auto, whileBroken, after: canvas.Design.Route.Count, stillAuto: canvas.Design.RouteAuto);
        });

        Assert.Equal(24, seen.ringTiles);
        Assert.Equal(4, seen.ringTurns);
        Assert.Equal(24, seen.traced);
        Assert.True(seen.auto);
        Assert.Equal(24, seen.whileBroken);
        Assert.Equal(32, seen.after);
        Assert.True(seen.stillAuto);
    }

    [Fact]
    public void A_point_moved_by_hand_stops_the_line_tracing_itself()
    {
        var auto = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Lane;
            canvas.LaneRing = true;
            Drag(canvas, CellCentre(0, 0), CellCentre(7, 5));
            canvas.AutoRoute();

            canvas.Tool = RcpTool.Route;
            Click(canvas, CellCentre(3, 3));

            return canvas.Design.RouteAuto;
        });

        Assert.False(auto);
    }

    [Fact]
    public void Corners_turns_a_lanes_turn_into_a_hand_one_and_the_lane_keeps_it()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Lane;
            canvas.LaneRing = true;
            Drag(canvas, CellCentre(0, 0), CellCentre(7, 5));

            canvas.Tool = RcpTool.Corner;
            Click(canvas, GridPoint(0, 0));

            var turn = canvas.Design.CurveAt(0, 0);

            // Another edit of the lane: a tile added to the far side.
            canvas.Tool = RcpTool.Lane;
            canvas.LaneRing = false;
            Drag(canvas, CellCentre(7, 2), CellCentre(8, 2));

            var kept = canvas.Design.CurveAt(0, 0);

            return (radius: turn?.Radius, auto: turn?.Auto, same: ReferenceEquals(turn, kept));
        });

        Assert.Equal(2f, seen.radius);
        Assert.Equal(false, seen.auto);
        Assert.True(seen.same);
    }

    [Fact]
    public void The_eraser_takes_a_lanes_floor_not_its_rails()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Lane;
            canvas.LaneWidth = 1;
            Drag(canvas, CellCentre(0, 0), CellCentre(5, 0));

            canvas.Tool = RcpTool.Erase;

            // On the seam along the top of tile (2,0): a lane's rail is
            // passed over, and the tile under the pointer goes instead.
            Click(canvas, new Point(40 + 64 * 2 + 32, 40 + 2));

            return (tiles: canvas.Design.Tiles.Count, railOver: canvas.Design.WallAt(2, 0, false) is not null);
        });

        Assert.Equal(5, seen.tiles);
        Assert.False(seen.railOver);
    }

    [Fact]
    public void A_lane_is_two_tiles_wide_unless_told_otherwise()
    {
        // A real RCP lane is never narrower (Asher, 25 Oct 2026).
        Assert.Equal(2, OnBoard(canvas => canvas.LaneWidth));
    }

    [Fact]
    public void The_zoom_readout_takes_the_board_back_to_100()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Zoom(2f);
            var zoomed = canvas.ZoomPercent;

            canvas.ZoomHome();

            return (zoomed, home: canvas.ZoomPercent);
        });

        Assert.Equal(200, seen.zoomed);
        Assert.Equal(100, seen.home);
    }

    [Fact]
    public void The_Turns_tool_changes_a_lanes_turn_and_it_stays_changed()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Lane;
            canvas.LaneRing = true;
            Drag(canvas, CellCentre(0, 0), CellCentre(9, 7));

            canvas.Tool = RcpTool.Curve;
            canvas.CurveRadius = 2f;

            // Just inside the corner, so the turn faces into the track.
            Click(canvas, new Point(40 + 8, 40 + 8));

            var turn = canvas.Design.CurveAt(0, 0);
            return (turn?.Radius, turn?.Auto);
        });

        Assert.Equal(2f, seen.Item1);
        Assert.Equal(false, seen.Item2);
    }
}
