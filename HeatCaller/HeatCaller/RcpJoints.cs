namespace HeatCaller;

/// <summary>
/// The outline of one RCP floor tile, with its interlocking teeth, in tile
/// units (a tile is 1 x 1 at its grid square).
///
/// MEASURED, NOT GUESSED (25 Oct 2026): from Asher's photograph of one of his
/// 50cm tiles against a tape measure, kept as
/// plans\track-designer\rcp-tile-edge.jpg.
/// - Every edge carries SEVEN T-shaped teeth standing OUT from the tile. There
///   are no sockets cut into the tile: the gaps between one tile's teeth are
///   filled by its neighbour's teeth. So a joint is a strip about 2cm wide,
///   centred on the grid line, where the two tiles' teeth interleave.
/// - A tooth is a short NECK (about 2cm across) ending in a wider HEAD (about
///   4cm across) whose lips overhang the neck - which is what locks the
///   joint. A neck and a head together make one pitch (6.2cm), so a
///   neighbour's head fits exactly where this tile's necks leave room.
/// - The photo's neck is a little shorter than its head is deep (0.8cm and
///   1.2cm); here they are half the strip each, because the neighbour's neck
///   has to sit beside this head at the same depth for the two to fit.
/// - Along the photo's bottom edge, left to right: a gap, then a tooth,
///   alternating, ending on a tooth - which is this seam's order (the A
///   side's teeth come last).
/// - SIMPLIFIED: a real tile's corners are solid blocks reaching out into
///   the joint strip, chamfered at 45 degrees. Here the ends of an edge are
///   plain foam up to the grid line (2 to 4cm), with one small clipped
///   corner where the floor has an outside corner (see RcpTrackPainter). At
///   any zoom the designer is used at, a tooth is a few pixels wide and the
///   corner block is less than one, so it is not worth the geometry.
///
/// Built so the outline of a tile and the outline of its neighbour are the
/// SAME line along their shared seam: the tile on the north or west side of
/// a seam (the "A" side) owns the teeth that start the seam, the one on the
/// south or east side (the "B" side) the others. That is why a seam can be
/// drawn by stroking every tile's outline - the two strokes land on each
/// other - and why a tile with no neighbour shows its bare teeth.
///
/// No System.Drawing.Common in here (PointF only), so it is tested on its
/// own: RcpJointTests.
/// </summary>
public static class RcpJoints
{
    /// <summary>Teeth on one edge.</summary>
    public const int TeethPerEdge = 7;

    /// <summary>One tooth and one gap, as a fraction of the tile: 6.2cm of 50cm (238px at 97.5px an inch in the photo).</summary>
    public const float Pitch = 0.124f;

    /// <summary>A tooth's head across, as a fraction of the pitch (about 4cm). Head + neck = one pitch.</summary>
    public const float HeadOfPitch = 0.65f;

    /// <summary>How far a tooth stands proud of the line its neighbour's teeth start from: 2cm of 50cm.</summary>
    public const float StripDepth = 0.04f;

    /// <summary>The clipped corner, along each side: 1cm of 50cm.</summary>
    public const float CornerClip = 0.02f;

    public static float HeadWidth => Pitch * HeadOfPitch;

    public static float NeckWidth => Pitch * (1f - HeadOfPitch);

    /// <summary>
    /// Where the A side's tooth nearest its end of the seam is centred,
    /// measured from that end. NOT centred on the edge: on the photographed
    /// tile the last tooth along the bottom edge is centred 3.75cm from the
    /// corner (0.925 of the way along), so the teeth sit about a centimetre
    /// towards that end and the far end has the longer stretch of plain foam.
    /// </summary>
    public const float FirstToothAt = 0.075f;

    /// <summary>The plain foam at the A side's end of an edge, before its first tooth (about 1.75cm).</summary>
    public static float PlainEnd => FirstToothAt - HeadWidth / 2f;

    /// <summary>The plain foam at the other end, after the B side's last tooth (about 4cm).</summary>
    public static float PlainFarEnd => 1f - (FirstToothAt + (TeethPerEdge - 0.5f) * Pitch + HeadWidth / 2f);

    /// <summary>
    /// The joint along one seam, from (0,0) to (1,0). The A tile is on the
    /// negative side (v &lt; 0), the B tile on the positive side; u runs
    /// along the seam. Points in order, both ends on the grid line.
    /// </summary>
    public static IReadOnlyList<PointF> Seam { get; } = BuildSeam();

    private static PointF[] BuildSeam()
    {
        var h = HeadWidth / 2f;
        var n = NeckWidth / 2f;
        var d = StripDepth / 2f;
        var x0 = FirstToothAt;

        var points = new List<PointF> { new(0f, 0f) };

        for (var k = 0; k < TeethPerEdge; k++)
        {
            var xc = x0 + k * Pitch;

            if (k == 0)
            {
                // From the plain end straight to the first head: there is
                // no neighbour's tooth before it.
                points.Add(new PointF(xc - h, 0f));
            }
            else
            {
                // Down this tile's side of the strip, past the neighbour's
                // head, to the start of this tooth's head.
                points.Add(new PointF(xc - n, -d));
                points.Add(new PointF(xc - n, 0f));
                points.Add(new PointF(xc - h, 0f));
            }

            // Round this tooth's head, on the neighbour's side of the line.
            points.Add(new PointF(xc - h, d));
            points.Add(new PointF(xc + h, d));
            points.Add(new PointF(xc + h, 0f));
            points.Add(new PointF(xc + n, 0f));
            points.Add(new PointF(xc + n, -d));
        }

        // The neighbour's last head, then plain foam to the corner.
        var lastB = x0 + (TeethPerEdge - 1) * Pitch + Pitch / 2f;
        points.Add(new PointF(lastB + h, -d));
        points.Add(new PointF(lastB + h, 0f));
        points.Add(new PointF(1f, 0f));

        // Mirrored end for end, so the A side's teeth come LAST along the
        // seam, as along the bottom edge of the photographed tile. Still
        // from (0,0) to (1,0).
        return points.Select(p => new PointF(1f - p.X, p.Y)).Reverse().ToArray();
    }

    /// <summary>
    /// The whole outline of the tile at (col, row), clockwise on screen
    /// (y down), in tile units. With clipSouthEast, its bottom-right corner
    /// is cut off at 45 degrees - see CornerClip.
    /// </summary>
    public static List<PointF> Outline(int col, int row, bool clipSouthEast = false)
    {
        var seam = Seam;
        var outline = new List<PointF>(seam.Count * 4 + 2);

        void Add(PointF point)
        {
            if (outline.Count > 0 && outline[^1] == point)
            {
                return;
            }

            outline.Add(point);
        }

        // North edge, left to right. This tile is the B side of that seam.
        foreach (var p in seam)
        {
            Add(new PointF(col + p.X, row + p.Y));
        }

        // East edge, top to bottom. This tile is the A side: v points east.
        var east = seam.Select(p => new PointF(col + 1 + p.Y, row + p.X)).ToList();

        if (clipSouthEast)
        {
            // Stop short of the corner along the east side...
            east[^1] = new PointF(col + 1, row + 1 - CornerClip);
        }

        foreach (var p in east)
        {
            Add(p);
        }

        // South edge, right to left. This tile is the A side: v points south.
        var south = seam.Reverse().Select(p => new PointF(col + p.X, row + 1 + p.Y)).ToList();

        if (clipSouthEast)
        {
            // ...and pick the edge up again the same distance along the south.
            south[0] = new PointF(col + 1 - CornerClip, row + 1);
        }

        foreach (var p in south)
        {
            Add(p);
        }

        // West edge, bottom to top. This tile is the B side of that seam.
        foreach (var p in seam.Reverse())
        {
            Add(new PointF(col + p.Y, row + p.X));
        }

        // The last point is the first one again (the north-west corner).
        if (outline.Count > 1 && outline[^1] == outline[0])
        {
            outline.RemoveAt(outline.Count - 1);
        }

        return outline;
    }

    /// <summary>The area inside an outline (shoelace), in square tiles. A whole tile is exactly 1.</summary>
    public static float Area(IReadOnlyList<PointF> outline)
    {
        var sum = 0.0;

        for (var i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            sum += (double)a.X * b.Y - (double)b.X * a.Y;
        }

        return (float)(sum / 2.0);
    }
}
