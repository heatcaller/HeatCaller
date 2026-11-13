using System.Drawing;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: an arched rail each side of a divider is two arched tiles
/// facing one seam. The angled view looked arched tiles up by seam and threw
/// on the second, on every paint.
/// </summary>
public sealed class BugHunt17_RcpAngledViewTests
{
    [Fact]
    public void The_angled_view_draws_an_arched_rail_each_side_of_a_divider()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500 };
        RcpPieces.Lay(design, 0, 0, RcpPiece.Arched, RcpEdge.East, RcpQuadrant.NorthWest);
        RcpPieces.Lay(design, 1, 0, RcpPiece.Arched, RcpEdge.West, RcpQuadrant.NorthWest);

        // Both kept as arched, on the same seam.
        Assert.Equal(RcpPiece.Arched, design.At(0, 0)!.Piece);
        Assert.Equal(RcpPiece.Arched, design.At(1, 0)!.Piece);
        Assert.Equal(
            RcpPlan.EdgeLine(0, 0, RcpEdge.East),
            RcpPlan.EdgeLine(1, 0, RcpEdge.West));

        using var bitmap = new Bitmap(400, 300);
        using var g = Graphics.FromImage(bitmap);

        RcpAngledView.Draw(g, design, bitmap.Size, RcpAngle.Start);
    }
}
