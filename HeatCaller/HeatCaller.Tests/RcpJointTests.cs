using System.Drawing;

namespace HeatCaller.Tests;

/// <summary>
/// The tile joints (Track Designer overhaul, Phase 1, 25 Oct 2026): the
/// shape measured off Asher's photo of a real RCP tile
/// (plans\track-designer\rcp-tile-edge.jpg), and the painter drawing it so
/// that neighbouring tiles lock together with no hole and no overlap.
/// </summary>
public sealed class RcpJointTests
{
    private const float Close = 1e-5f;

    [Fact]
    public void A_whole_tile_has_exactly_the_area_of_its_square()
    {
        // Every tooth that stands out past the square is matched by a gap
        // cut into it, so the foam is one tile's worth - which is what makes
        // a floor with no holes and no overlaps possible at all.
        Assert.Equal(1f, RcpJoints.Area(RcpJoints.Outline(0, 0)), 4);
        Assert.Equal(1f, RcpJoints.Area(RcpJoints.Outline(7, -3)), 4);
    }

    [Fact]
    public void The_clipped_corner_takes_off_only_the_corner()
    {
        var clip = RcpJoints.CornerClip;

        Assert.Equal(1f - clip * clip / 2f, RcpJoints.Area(RcpJoints.Outline(0, 0, clipSouthEast: true)), 5);
    }

    [Fact]
    public void A_tile_and_its_east_neighbour_share_one_seam()
    {
        var seam = SeamPoints(RcpJoints.Outline(0, 0), vertical: true, at: 1f);
        var theirs = SeamPoints(RcpJoints.Outline(1, 0), vertical: true, at: 1f);

        Assert.NotEmpty(seam);
        Assert.Equal(seam, theirs);
    }

    [Fact]
    public void A_tile_and_its_south_neighbour_share_one_seam()
    {
        var seam = SeamPoints(RcpJoints.Outline(0, 0), vertical: false, at: 1f);
        var theirs = SeamPoints(RcpJoints.Outline(0, 1), vertical: false, at: 1f);

        Assert.NotEmpty(seam);
        Assert.Equal(seam, theirs);
    }

    [Theory]
    [InlineData("north")]
    [InlineData("east")]
    [InlineData("south")]
    [InlineData("west")]
    public void Every_edge_has_seven_teeth_standing_out(string edge)
    {
        var outline = RcpJoints.Outline(0, 0);
        var past = RcpJoints.StripDepth / 2f - 1e-4f;

        Func<PointF, bool> beyond = edge switch
        {
            "north" => p => p.Y < -past,
            "east" => p => p.X > 1f + past,
            "south" => p => p.Y > 1f + past,
            _ => p => p.X < -past,
        };

        // Count the separate runs of the outline that are out past the edge.
        var heads = 0;
        var inHead = false;

        foreach (var point in outline.Append(outline[0]))
        {
            var outside = beyond(point);

            if (outside && !inHead)
            {
                heads++;
            }

            inHead = outside;
        }

        Assert.Equal(RcpJoints.TeethPerEdge, heads);
        Assert.Equal(7, heads);
    }

    [Fact]
    public void The_outline_never_crosses_itself()
    {
        foreach (var outline in new[] { RcpJoints.Outline(0, 0), RcpJoints.Outline(0, 0, clipSouthEast: true) })
        {
            for (var i = 0; i < outline.Count; i++)
            {
                for (var j = i + 2; j < outline.Count; j++)
                {
                    if (i == 0 && j == outline.Count - 1)
                    {
                        continue;
                    }

                    Assert.False(
                        Crosses(outline[i], outline[(i + 1) % outline.Count], outline[j], outline[(j + 1) % outline.Count]),
                        $"Edge {i} crosses edge {j}.");
                }
            }
        }
    }

    [Fact]
    public void The_teeth_are_the_size_measured_off_a_real_tile()
    {
        // On a 50cm tile: a 6.2cm pitch, a 4cm head on a 2.2cm neck, standing
        // 2cm proud, the last tooth along an edge 3.75cm from the corner.
        const float cm = 1f / 50f;

        Assert.InRange(RcpJoints.Pitch / cm, 6.1f, 6.3f);
        Assert.InRange(RcpJoints.HeadWidth / cm, 3.9f, 4.1f);
        Assert.InRange(RcpJoints.NeckWidth / cm, 2.1f, 2.3f);
        Assert.InRange(RcpJoints.StripDepth / cm, 1.9f, 2.1f);
        Assert.InRange(RcpJoints.FirstToothAt / cm, 3.7f, 3.8f);

        // A head and a neck make one pitch - the neighbour's head fills
        // exactly the room this tile's necks leave.
        Assert.Equal(RcpJoints.Pitch, RcpJoints.HeadWidth + RcpJoints.NeckWidth, 5);

        // Plain foam at both ends, so the teeth never run into the corner.
        Assert.True(RcpJoints.PlainEnd > 0f);
        Assert.True(RcpJoints.PlainFarEnd > 0f);
    }

    [Fact]
    public void The_seam_starts_and_ends_on_the_grid_line()
    {
        var seam = RcpJoints.Seam;

        Assert.Equal(new PointF(0f, 0f), seam[0]);
        Assert.Equal(new PointF(1f, 0f), seam[^1]);
        Assert.All(seam, p => Assert.InRange(p.Y, -RcpJoints.StripDepth / 2f - Close, RcpJoints.StripDepth / 2f + Close));
    }

    // --- The painter ------------------------------------------------------

    private static readonly Color Background = Color.FromArgb(255, 0, 255);

    private const float Scale = 400f;
    private static readonly PointF Origin = new(40f, 40f);

    private static Bitmap TwoTilesSideBySide()
    {
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        design.Tiles.Add(new RcpTile { Col = 1, Row = 0 });

        var bitmap = new Bitmap(900, 500);

        using var g = Graphics.FromImage(bitmap);
        g.Clear(Background);
        RcpTrackPainter.Draw(g, design, Scale, Origin, showRoute: false, showLabels: false);

        return bitmap;
    }

    private static Color At(Bitmap bitmap, float u, float v) =>
        bitmap.GetPixel((int)Math.Round(Origin.X + u * Scale), (int)Math.Round(Origin.Y + v * Scale));

    private static bool IsBackground(Color colour) =>
        Math.Abs(colour.R - Background.R) + Math.Abs(colour.G - Background.G) + Math.Abs(colour.B - Background.B) < 60;

    [Fact]
    public void The_seam_between_two_tiles_has_no_hole_in_it()
    {
        using var bitmap = TwoTilesSideBySide();

        // Right across the joint strip, all the way down the seam - stopping
        // short of the top and bottom edges, whose own teeth and gaps are
        // there (no neighbours above or below).
        for (var v = 0.03f; v <= 0.97f; v += 0.005f)
        {
            for (var u = 1f - RcpJoints.StripDepth; u <= 1f + RcpJoints.StripDepth; u += 0.0025f)
            {
                Assert.False(IsBackground(At(bitmap, u, v)), $"A hole in the seam at ({u:0.000}, {v:0.000}).");
            }
        }
    }

    [Fact]
    public void An_edge_with_no_neighbour_shows_its_bare_teeth()
    {
        using var bitmap = TwoTilesSideBySide();

        // The south edge of the left tile: this tile's teeth come last along
        // the seam (RcpJoints.Seam), the last one centred FirstToothAt from
        // the far corner. Half a pitch back is the gap a neighbour's tooth
        // would fill.
        var depth = RcpJoints.StripDepth / 4f;
        var tooth = 1f - RcpJoints.FirstToothAt;
        var gap = tooth - RcpJoints.Pitch / 2f;

        Assert.False(IsBackground(At(bitmap, tooth, 1f + depth)), "The tooth's head should stand out past the edge.");
        Assert.True(IsBackground(At(bitmap, gap, 1f + depth)), "Past the edge, between the teeth, is floor.");
        Assert.True(IsBackground(At(bitmap, gap, 1f - depth)), "The gap is cut into the tile, where a neighbour's head would sit.");
    }

    // --- Helpers ----------------------------------------------------------

    /// <summary>The outline's points along one grid line, in order, leaving out the two corners.</summary>
    private static List<PointF> SeamPoints(IReadOnlyList<PointF> outline, bool vertical, float at)
    {
        var reach = RcpJoints.StripDepth / 2f + 1e-4f;

        return outline
            .Where(p => vertical
                ? Math.Abs(p.X - at) <= reach && p.Y > 1e-3f && p.Y < 1f - 1e-3f
                : Math.Abs(p.Y - at) <= reach && p.X > 1e-3f && p.X < 1f - 1e-3f)
            .OrderBy(p => vertical ? p.Y : p.X)
            .ThenBy(p => vertical ? p.X : p.Y)
            .ToList();
    }

    private static bool Crosses(PointF p1, PointF p2, PointF p3, PointF p4)
    {
        static double Side(PointF a, PointF b, PointF c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        var d1 = Side(p3, p4, p1);
        var d2 = Side(p3, p4, p2);
        var d3 = Side(p1, p2, p3);
        var d4 = Side(p1, p2, p4);

        return ((d1 > 1e-9 && d2 < -1e-9) || (d1 < -1e-9 && d2 > 1e-9))
               && ((d3 > 1e-9 && d4 < -1e-9) || (d3 < -1e-9 && d4 > 1e-9));
    }
}
