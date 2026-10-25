using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The lap on the Track Designer's board says where its time comes from,
/// and a line changed in the window takes the estimate (Track Designer
/// overhaul, Phase 6, 25 Oct 2026).
/// </summary>
public sealed class TrackDesignerLapTests
{
    private static RcpTrackDesign TracedRing()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500, RoomInFeet = false };

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(11, 7), 2)!)
        {
            design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Lane = 1 });
        }

        RcpAutoRails.Apply(design);
        design.Route = RcpPlan.AutoRoute(design, out _)!.Select(p => new RcpRoutePoint { X = p.X, Y = p.Y }).ToList();
        design.RouteClosed = true;
        design.RouteAuto = true;

        return design;
    }

    private static RcpTrackCanvas Board(RcpTrackEditorForm form) =>
        (RcpTrackCanvas)typeof(RcpTrackEditorForm)
            .GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

    [Fact]
    public void The_chip_says_laps_raced_then_estimate_once_the_line_changes()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new RcpTrackEditorForm(TracedRing(), "Club Hall", 11.0, raced: (9.4, 120));
            var board = Board(form);

            var raced = board.Chips[1];
            var lapForPlay = board.LapSeconds;

            // Take a point off the line: it is a different line now.
            board.Tool = RcpTool.Route;
            var design = board.Design;
            design.Route.RemoveAt(3);
            typeof(RcpTrackEditorForm).GetMethod("RefreshReadouts", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);

            return (raced, lapForPlay, changed: board.Chips[1]);
        }, TimeSpan.FromSeconds(30), () => "opening the Track Designer");

        Assert.EndsWith("9.4s from 120 laps raced", seen.raced);
        Assert.Equal(9.4, seen.lapForPlay);
        Assert.EndsWith("s estimate", seen.changed);
    }

    [Fact]
    public void With_no_races_the_entered_lap_is_said_to_be_entered()
    {
        var chip = StaThread.Run(() =>
        {
            using var form = new RcpTrackEditorForm(TracedRing(), "Club Hall", 11.0);
            return Board(form).Chips[1];
        }, TimeSpan.FromSeconds(30), () => "opening the Track Designer");

        Assert.EndsWith("11.0s as entered", chip);
    }
}
