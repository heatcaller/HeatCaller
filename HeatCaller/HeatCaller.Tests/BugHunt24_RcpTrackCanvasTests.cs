using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the Track Designer's board, driven through its
/// own mouse handling as BugHunt17_RcpTrackCanvasTests does (64 pixels a
/// tile, the plan's corner 40 pixels in): a turn, mirror or nudge asked for
/// while a picked block is being dragged no longer lands it where it happens
/// to be and eats what it is passing over.
/// </summary>
public sealed class BugHunt24_RcpTrackCanvasTests
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
        }, TimeSpan.FromSeconds(30), () => "bug hunt 24, the Track Designer's board");

    private static int TilesAt(RcpTrackDesign design, int col, int row) =>
        design.Tiles.Count(t => t.Col == col && t.Row == row);

    /// <summary>
    /// A picked tile at (0,0) dragged over two infield tiles at (1,0) and
    /// (2,0), with <paramref name="midDrag"/> asked for while it is over the
    /// first, then carried on to (4,0) and let go.
    /// </summary>
    private static (int first, int second, int dropped, int tiles) DragPast(Action<RcpTrackCanvas> midDrag) =>
        OnBoard(canvas =>
        {
            var design = new RcpTrackDesign();

            design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
            canvas.Design = design;
            canvas.Tool = RcpTool.Select;
            canvas.SelectEverything();

            canvas.Design.Tiles.Add(new RcpTile { Col = 1, Row = 0, Kind = RcpTileKind.Infield });
            canvas.Design.Tiles.Add(new RcpTile { Col = 2, Row = 0, Kind = RcpTileKind.Infield });

            Mouse(canvas, "OnMouseDown", CellCentre(0, 0));
            Mouse(canvas, "OnMouseMove", CellCentre(1, 0));

            midDrag(canvas);

            // The nudge's settle would have fired 350 ms later, button still down.
            canvas.LandPendingNudge();

            Mouse(canvas, "OnMouseMove", CellCentre(4, 0));
            Mouse(canvas, "OnMouseUp", CellCentre(4, 0));

            return (TilesAt(canvas.Design, 1, 0), TilesAt(canvas.Design, 2, 0), TilesAt(canvas.Design, 4, 0), canvas.Design.Tiles.Count);
        });

    [Fact]
    public void A_turn_asked_for_mid_drag_does_not_eat_what_the_block_is_passing_over()
    {
        Assert.Equal((1, 1, 1, 3), DragPast(canvas => canvas.TurnSelection(1)));
    }

    [Fact]
    public void A_counterclockwise_turn_asked_for_mid_drag_does_not_eat_what_the_block_is_passing_over()
    {
        Assert.Equal((1, 1, 1, 3), DragPast(canvas => canvas.TurnSelection(-1)));
    }

    [Fact]
    public void A_mirror_asked_for_mid_drag_does_not_eat_what_the_block_is_passing_over()
    {
        Assert.Equal((1, 1, 1, 3), DragPast(canvas => canvas.MirrorSelection(leftToRight: true)));
    }

    [Fact]
    public void An_arrow_nudge_asked_for_mid_drag_does_not_eat_what_the_block_is_passing_over()
    {
        Assert.Equal((1, 1, 1, 3), DragPast(canvas => canvas.NudgeSelection(1, 0)));
    }

    [Fact]
    public void A_turn_with_no_drag_held_still_turns_and_lands_the_block()
    {
        var seen = OnBoard(canvas =>
        {
            var design = new RcpTrackDesign();

            design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
            canvas.Design = design;
            canvas.Tool = RcpTool.Select;
            canvas.SelectEverything();

            // Something unpicked under the block: a turn where it stands lands it.
            canvas.Design.Tiles.Add(new RcpTile { Col = 0, Row = 0, Kind = RcpTileKind.Infield });

            canvas.TurnSelection(1);

            return TilesAt(canvas.Design, 0, 0);
        });

        Assert.Equal(1, seen);
    }
}
