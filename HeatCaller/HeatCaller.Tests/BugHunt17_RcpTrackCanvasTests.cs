using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), the Track Designer's board, driven through
/// its own mouse handling as TrackDesignerLaneToolTests does (64 pixels a
/// tile, the plan's corner 40 pixels in): an erase drag takes only the lane
/// tiles the pointer goes over; an edit made straight after a nudge
/// snapshots a landed plan; and a marquee cut short by a tool key picks
/// nothing.
/// </summary>
public sealed class BugHunt17_RcpTrackCanvasTests
{
    private static Point CellCentre(int col, int row) => new(40 + 64 * col + 32, 40 + 64 * row + 32);

    private static void Mouse(RcpTrackCanvas canvas, string what, Point at, MouseButtons button = MouseButtons.Left)
    {
        typeof(RcpTrackCanvas)
            .GetMethod(what, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, new object[] { new MouseEventArgs(button, 1, at.X, at.Y, 0) });
    }

    private static T OnBoard<T>(Func<RcpTrackCanvas, T> test) =>
        StaThread.Run(() =>
        {
            using var canvas = new RcpTrackCanvas { Size = new Size(1200, 900) };
            return test(canvas);
        }, TimeSpan.FromSeconds(30), () => "bug hunt 17, the Track Designer's board");

    private static int TilesAt(RcpTrackDesign design, int col, int row) =>
        design.Tiles.Count(t => t.Col == col && t.Row == row);

    // --- 137: the eraser and the rail a lane puts round the gap it leaves ---------------

    [Fact]
    public void An_erase_drag_that_stays_on_one_lane_tile_leaves_the_next_one_down()
    {
        var seen = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Lane;
            canvas.LaneWidth = 1;

            Mouse(canvas, "OnMouseDown", CellCentre(0, 0));
            Mouse(canvas, "OnMouseMove", CellCentre(3, 0));
            Mouse(canvas, "OnMouseUp", CellCentre(3, 0));

            var laid = canvas.Design.Tiles.Count;

            canvas.Tool = RcpTool.Erase;

            // On tile (1,0), six pixels short of its seam with (2,0), then a
            // pixel of jitter with the button still down.
            var press = new Point(40 + 64 * 2 - 6, 40 + 32);

            Mouse(canvas, "OnMouseDown", press);
            Mouse(canvas, "OnMouseMove", new Point(press.X + 1, press.Y));

            var afterJitter = (TilesAt(canvas.Design, 1, 0), TilesAt(canvas.Design, 2, 0));

            // Carried on over the seam, it takes the tile it is now on.
            Mouse(canvas, "OnMouseMove", new Point(press.X + 12, press.Y));
            Mouse(canvas, "OnMouseUp", new Point(press.X + 12, press.Y));

            return (laid, afterJitter, left: canvas.Design.Tiles.Select(t => t.Col).OrderBy(c => c).ToArray());
        });

        Assert.Equal(4, seen.laid);
        Assert.Equal((0, 1), seen.afterJitter);
        Assert.Equal(new[] { 0, 3 }, seen.left);
    }

    [Fact]
    public void A_click_on_the_rail_a_lane_put_round_a_gap_still_takes_the_lane_tile_beside_it()
    {
        var left = OnBoard(canvas =>
        {
            canvas.Tool = RcpTool.Lane;
            canvas.LaneWidth = 1;

            Mouse(canvas, "OnMouseDown", CellCentre(0, 0));
            Mouse(canvas, "OnMouseMove", CellCentre(3, 0));
            Mouse(canvas, "OnMouseUp", CellCentre(3, 0));

            canvas.Tool = RcpTool.Erase;

            // Two separate clicks on the same spot: the second is aimed at
            // the rail now standing there, and takes the tile behind it.
            var at = new Point(40 + 64 * 2 - 6, 40 + 32);

            Mouse(canvas, "OnMouseDown", at);
            Mouse(canvas, "OnMouseUp", at);
            Mouse(canvas, "OnMouseDown", at);
            Mouse(canvas, "OnMouseUp", at);

            return canvas.Design.Tiles.Select(t => t.Col).OrderBy(c => c).ToArray();
        });

        Assert.Equal(new[] { 0, 3 }, left);
    }

    // --- 138: an edit inside a nudge run's 350 ms -----------------------------------------

    /// <summary>A tile nudged onto another, its settle still waiting, with a third tile well away.</summary>
    private static void NudgeOntoATile(RcpTrackCanvas canvas)
    {
        var design = new RcpTrackDesign();

        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        canvas.Design = design;
        canvas.Tool = RcpTool.Select;
        canvas.SelectEverything();

        canvas.Design.Tiles.Add(new RcpTile { Col = 1, Row = 0, Kind = RcpTileKind.Infield });
        canvas.Design.Tiles.Add(new RcpTile { Col = 5, Row = 5 });

        canvas.NudgeSelection(1, 0);
    }

    [Fact]
    public void Undoing_a_right_click_erase_made_straight_after_a_nudge_brings_back_a_landed_plan()
    {
        var seen = OnBoard(canvas =>
        {
            NudgeOntoATile(canvas);

            Mouse(canvas, "OnMouseDown", CellCentre(5, 5), MouseButtons.Right);
            Mouse(canvas, "OnMouseUp", CellCentre(5, 5), MouseButtons.Right);

            var erased = TilesAt(canvas.Design, 5, 5);

            canvas.Undo();

            return (erased, stacked: TilesAt(canvas.Design, 1, 0), back: TilesAt(canvas.Design, 5, 5));
        });

        Assert.Equal(0, seen.erased);
        Assert.Equal(1, seen.stacked);
        Assert.Equal(1, seen.back);
    }

    [Fact]
    public void Any_undo_step_taken_straight_after_a_nudge_is_of_a_landed_plan()
    {
        var stacked = OnBoard(canvas =>
        {
            NudgeOntoATile(canvas);

            // Remember itself, for a caller that takes no press of the mouse.
            typeof(RcpTrackCanvas)
                .GetMethod("Remember", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(canvas, null);

            canvas.Undo();

            return TilesAt(canvas.Design, 1, 0);
        });

        Assert.Equal(1, stacked);
    }

    // --- 139: a tool key with a marquee half drawn ---------------------------------------

    [Fact]
    public void A_marquee_cut_short_by_another_tool_picks_nothing()
    {
        var seen = OnBoard(canvas =>
        {
            var design = new RcpTrackDesign();

            design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
            design.Tiles.Add(new RcpTile { Col = 1, Row = 0 });
            canvas.Design = design;
            canvas.Tool = RcpTool.Select;

            Mouse(canvas, "OnMouseDown", new Point(10, 10));
            Mouse(canvas, "OnMouseMove", CellCentre(1, 1));

            canvas.Tool = RcpTool.Floor;

            Mouse(canvas, "OnMouseMove", CellCentre(2, 2));
            Mouse(canvas, "OnMouseUp", CellCentre(2, 2));

            return (picked: canvas.HasSelection, tiles: canvas.Design.Tiles.Count);
        });

        Assert.False(seen.picked);
        Assert.Equal(2, seen.tiles);
    }

    [Fact]
    public void A_marquee_finished_under_the_Select_tool_still_picks_what_it_covers()
    {
        var picked = OnBoard(canvas =>
        {
            var design = new RcpTrackDesign();

            design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
            canvas.Design = design;
            canvas.Tool = RcpTool.Select;

            Mouse(canvas, "OnMouseDown", new Point(10, 10));
            Mouse(canvas, "OnMouseMove", CellCentre(1, 1));
            Mouse(canvas, "OnMouseUp", CellCentre(1, 1));

            return canvas.HasSelection;
        });

        Assert.True(picked);
    }
}
