using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026): with a racing line of fewer than 3 points
/// there is no lap time (RcpLapModel.Choose), and the chip says so - but the
/// Lap card took the 12s stand-in Play drives at and said "At a 12.0s lap
/// that's ...", even over a track's entered lap time.
/// </summary>
public sealed class BugHunt16_RcpTrackEditorFormLapReadoutTests
{
    private static RcpTrackDesign TwoPointLine()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500, RoomInFeet = false };

        for (var col = 0; col < 6; col++)
        {
            design.Tiles.Add(new RcpTile { Col = col, Row = 0, Lane = 1 });
        }

        design.Route = new List<RcpRoutePoint>
        {
            new() { X = 0.5f, Y = 0.5f },
            new() { X = 5.5f, Y = 0.5f },
        };

        return design;
    }

    private static string LapCard(RcpTrackEditorForm form)
    {
        var label = typeof(RcpTrackEditorForm)
            .GetField("_lblStats", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

        return (string)label.GetType().GetProperty("Text")!.GetValue(label)!;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(11.0)]
    public void A_line_too_short_for_a_lap_time_gives_no_lap_speed(double? entered)
    {
        var card = StaThread.Run(() =>
        {
            using var form = new RcpTrackEditorForm(TwoPointLine(), "Club Hall", entered);
            typeof(RcpTrackEditorForm).GetMethod("RefreshReadouts", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            return LapCard(form);
        }, TimeSpan.FromSeconds(30), () => "opening the Track Designer");

        // The line is measured...
        Assert.Matches(@"Lap: \d+\.\d\d m", card);

        // ...but no lap time is made up for it.
        Assert.DoesNotContain("At a ", card);
        Assert.DoesNotContain("12.0s", card);
    }
}
