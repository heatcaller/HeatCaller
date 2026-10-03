using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace HeatCaller;

/// <summary>
/// Draws an RCP track plan.
///
/// The thing being drawn is a continuous floor of interlocking foam tiles
/// with the circuit standing on top of it: silver straight rails along the
/// tile seams, pink turn rails swinging them through corners, and a rounded
/// pink cap wherever a run of rail simply stops. Under all of it the floor
/// is the same tile everywhere - there is no such thing as a "corner tile",
/// which is what an earlier version of this got wrong.
///
/// All of it is drawn in code, down to the slot screws along the rails and
/// the interlocking tabs on the tiles, so it stays sharp at any size and
/// there is nothing to ship.
/// </summary>
public static class RcpTrackPainter
{
    // Sampled from photographs of a built track.
    private static readonly Color FoamLight = ColorTranslator.FromHtml("#42423C");
    private static readonly Color FoamDark = ColorTranslator.FromHtml("#2B2B28");
    private static readonly Color FoamSeam = ColorTranslator.FromHtml("#1B1B19");
    private static readonly Color FoamPin = ColorTranslator.FromHtml("#232320");
    private static readonly Color FoamGrain = Color.FromArgb(20, 255, 255, 255);

    /// <summary>Straight rails: light grey moulded plastic, almost silver under strip lights.</summary>
    private static readonly Color RailFace = ColorTranslator.FromHtml("#DEDED8");
    private static readonly Color RailEdge = ColorTranslator.FromHtml("#9E9E97");
    private static readonly Color RailScrew = ColorTranslator.FromHtml("#8A8A84");

    /// <summary>Turn rails and end caps: the RCP pink.</summary>
    private static readonly Color TurnFace = ColorTranslator.FromHtml("#F4557E");
    private static readonly Color TurnEdge = ColorTranslator.FromHtml("#BE3A5D");

    // A split loop: the colour timing loops are taped in, so it never reads
    // as the chequered finish line or a kerb.
    private static readonly Color LoopFace = ColorTranslator.FromHtml("#2D9CDB");
    private static readonly Color LoopEdge = ColorTranslator.FromHtml("#1B6FA0");

    private static readonly Color RailShadow = Color.FromArgb(90, 0, 0, 0);
    private static readonly Color InfieldFill = ColorTranslator.FromHtml("#1E1E20");

    /// <summary>Chequer on this track is grey and white, not black and white.</summary>
    private static readonly Color ChequerDark = ColorTranslator.FromHtml("#8E9094");
    private static readonly Color ChequerLight = ColorTranslator.FromHtml("#E9E9E4");

    private const float TabDepth = 0.055f;

    /// <summary>A rail's width as a fraction of a tile, measured off a photograph.</summary>
    public const float RailThickness = 0.12f;

    /// <summary>The gap between two rails stood back to back.</summary>
    private const float DividerGap = 0.05f;

    public static void Draw(
        Graphics g,
        RcpTrackDesign design,
        float scale,
        PointF origin,
        bool showRoute = true,
        bool showLabels = true,
        float detailThreshold = 26f,
        Color? offTrack = null,
        Image? backdrop = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var detailed = scale >= detailThreshold;

        if (backdrop is not null && !design.BackdropHidden)
        {
            DrawBackdrop(g, design, backdrop, scale, origin);
        }

        foreach (var tile in design.Tiles)
        {
            DrawTileFloor(g, tile, scale, origin, detailed);
        }

        foreach (var decal in design.Decals)
        {
            DrawDecal(g, decal, scale, origin, detailed);
        }

        if (showLabels && detailed)
        {
            foreach (var tile in design.Tiles.Where(t => !string.IsNullOrWhiteSpace(t.Label)))
            {
                DrawLabel(g, tile, scale, origin);
            }
        }

        // Rails last and in one pass, so a run reads as one continuous
        // barrier standing on the floor rather than as a tile decoration.
        foreach (var wall in design.Walls)
        {
            DrawWall(g, wall, scale, origin, detailed);
        }

        foreach (var curve in design.Curves)
        {
            DrawCurve(g, curve, scale, origin, detailed);
        }

        foreach (var (at, vertical, twin) in RcpPlan.OpenEnds(design))
        {
            DrawEndCap(g, at, vertical, twin, scale, origin);
        }

        if (showRoute)
        {
            DrawRoute(g, design, scale, origin);
        }
    }

    private static void DrawBackdrop(Graphics g, RcpTrackDesign design, Image backdrop, float scale, PointF origin)
    {
        var width = Math.Max(1f, design.BackdropWidth) * scale;
        var height = width * backdrop.Height / Math.Max(1, backdrop.Width);

        var target = new RectangleF(
            origin.X + design.BackdropX * scale,
            origin.Y + design.BackdropY * scale,
            width,
            height);

        var opacity = Math.Clamp(design.BackdropOpacity, 5, 100) / 100f;

        using var attributes = new ImageAttributes();

        var matrix = new ColorMatrix { Matrix33 = opacity };
        attributes.SetColorMatrix(matrix);

        g.DrawImage(backdrop,
            Rectangle.Round(target),
            0, 0, backdrop.Width, backdrop.Height,
            GraphicsUnit.Pixel,
            attributes);
    }

    public static RectangleF TileRect(RcpTile tile, float scale, PointF origin) =>
        new(origin.X + tile.Col * scale, origin.Y + tile.Row * scale, scale, scale);

    private static PointF Grid(int col, int row, float scale, PointF origin) =>
        new(origin.X + col * scale, origin.Y + row * scale);

    // --- Floor ---------------------------------------------------------------

    private static void DrawTileFloor(Graphics g, RcpTile tile, float scale, PointF origin, bool detailed)
    {
        var rect = TileRect(tile, scale, origin);

        if (tile.Kind == RcpTileKind.Infield)
        {
            DrawInfield(g, rect, detailed);
            return;
        }

        using var path = FoamPath(rect, detailed);

        using (var fill = new LinearGradientBrush(
                   new RectangleF(rect.X, rect.Y, Math.Max(1f, rect.Width), Math.Max(1f, rect.Height)),
                   FoamLight, FoamDark, LinearGradientMode.ForwardDiagonal))
        {
            g.FillPath(fill, path);
        }

        if (!detailed)
        {
            return;
        }

        using (var seam = new Pen(FoamSeam, Math.Max(1f, scale * 0.012f)))
        {
            g.DrawPath(seam, path);
        }

        var pin = scale * 0.05f;
        var inset = scale * 0.10f;

        using var pinBrush = new SolidBrush(FoamPin);

        foreach (var corner in new[]
                 {
                     new PointF(rect.Left + inset, rect.Top + inset),
                     new PointF(rect.Right - inset, rect.Top + inset),
                     new PointF(rect.Right - inset, rect.Bottom - inset),
                     new PointF(rect.Left + inset, rect.Bottom - inset),
                 })
        {
            g.FillEllipse(pinBrush, corner.X - pin / 2f, corner.Y - pin / 2f, pin, pin);
        }

        if (scale >= 46f)
        {
            using var grain = new Pen(FoamGrain, 1f);

            var clip = g.Save();
            g.SetClip(path);

            for (var i = 1; i < 7; i++)
            {
                var y = rect.Top + rect.Height * i / 7f;
                g.DrawLine(grain, rect.Left + 2, y, rect.Right - 2, y);
            }

            g.Restore(clip);
        }
    }

    /// <summary>A tile outline with a puzzle tab on two edges and a notch on the other two.</summary>
    private static GraphicsPath FoamPath(RectangleF rect, bool detailed)
    {
        var path = new GraphicsPath();

        if (!detailed)
        {
            path.AddRectangle(rect);
            return path;
        }

        var depth = rect.Width * TabDepth;
        var tab = rect.Width * 0.22f;

        var midX = rect.Left + rect.Width / 2f;
        var midY = rect.Top + rect.Height / 2f;

        path.AddLine(rect.Left, rect.Top, midX - tab / 2f, rect.Top);
        path.AddArc(midX - tab / 2f, rect.Top - depth, tab, depth * 2f, 180f, 180f);
        path.AddLine(midX + tab / 2f, rect.Top, rect.Right, rect.Top);

        path.AddLine(rect.Right, rect.Top, rect.Right, midY - tab / 2f);
        path.AddArc(rect.Right - depth, midY - tab / 2f, depth * 2f, tab, 270f, 180f);
        path.AddLine(rect.Right, midY + tab / 2f, rect.Right, rect.Bottom);

        path.AddLine(rect.Right, rect.Bottom, midX + tab / 2f, rect.Bottom);
        path.AddArc(midX - tab / 2f, rect.Bottom - depth, tab, depth * 2f, 0f, 180f);
        path.AddLine(midX - tab / 2f, rect.Bottom, rect.Left, rect.Bottom);

        path.AddLine(rect.Left, rect.Bottom, rect.Left, midY + tab / 2f);
        path.AddArc(rect.Left - depth, midY - tab / 2f, depth * 2f, tab, 90f, 180f);
        path.AddLine(rect.Left, midY - tab / 2f, rect.Left, rect.Top);

        path.CloseFigure();

        return path;
    }

    private static void DrawInfield(Graphics g, RectangleF rect, bool detailed)
    {
        using (var fill = new SolidBrush(InfieldFill))
        {
            g.FillRectangle(fill, rect);
        }

        if (!detailed)
        {
            return;
        }

        using var hatch = new Pen(Color.FromArgb(38, Color.White), Math.Max(1f, rect.Width * 0.012f));

        var clip = g.Save();
        g.SetClip(rect);

        for (var x = rect.Left - rect.Height; x < rect.Right; x += rect.Width / 5f)
        {
            g.DrawLine(hatch, x, rect.Bottom, x + rect.Height, rect.Top);
        }

        g.Restore(clip);
    }

    private static void DrawLabel(Graphics g, RcpTile tile, float scale, PointF origin)
    {
        var rect = TileRect(tile, scale, origin);

        using var brush = new SolidBrush(Color.FromArgb(230, Color.White));
        using var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Far,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        var box = new RectangleF(rect.Left + 2, rect.Top, rect.Width - 4, rect.Height - scale * 0.08f);

        g.FillRectangle(shade, box.Left, box.Bottom - scale * 0.2f, box.Width, scale * 0.2f);
        g.DrawString(tile.Label, AppTheme.SmallFont, brush, box, format);
    }

    // --- Rails ---------------------------------------------------------------

    /// <summary>
    /// A straight rail, standing along a tile seam.
    ///
    /// A single rail is what goes round the outside of the floor. Two of them
    /// back to back make the dividers that separate one part of the circuit
    /// from the next - which is why a plan of a real track has rails running
    /// down the middle of the floor with racing on both sides.
    /// </summary>
    private static void DrawWall(Graphics g, RcpWall wall, float scale, PointF origin, bool detailed)
    {
        var from = Grid(wall.Col, wall.Row, scale, origin);
        var to = Grid(wall.To.X, wall.To.Y, scale, origin);

        var thickness = Math.Max(2f, scale * RailThickness);

        if (!wall.Double)
        {
            DrawRailBar(g, from, to, wall.Vertical, 0f, thickness, RailFace, RailEdge, detailed, scale);
            return;
        }

        var offset = (thickness + scale * DividerGap) / 2f;

        DrawRailBar(g, from, to, wall.Vertical, -offset, thickness, RailFace, RailEdge, detailed, scale);
        DrawRailBar(g, from, to, wall.Vertical, offset, thickness, RailFace, RailEdge, detailed, scale);
    }

    private static void DrawRailBar(
        Graphics g, PointF from, PointF to, bool vertical, float offset, float thickness,
        Color face, Color edge, bool detailed, float scale)
    {
        var bar = vertical
            ? new RectangleF(from.X + offset - thickness / 2f, from.Y, thickness, to.Y - from.Y)
            : new RectangleF(from.X, from.Y + offset - thickness / 2f, to.X - from.X, thickness);

        if (detailed)
        {
            using var shadow = new SolidBrush(RailShadow);
            g.FillRectangle(shadow, bar.Left + (vertical ? thickness * 0.3f : 0), bar.Top + (vertical ? 0 : thickness * 0.3f), bar.Width, bar.Height);
        }

        using (var brush = new LinearGradientBrush(
                   new RectangleF(bar.X, bar.Y, Math.Max(1f, bar.Width), Math.Max(1f, bar.Height)),
                   face, edge,
                   vertical ? LinearGradientMode.Horizontal : LinearGradientMode.Vertical))
        using (var path = AppTheme.RoundedRect(bar, (int)Math.Max(1, thickness / 2.6f)))
        {
            g.FillPath(brush, path);
        }

        if (!detailed || scale < 40f)
        {
            return;
        }

        DrawScrews(g, from, to, vertical, offset, thickness, scale);
    }

    /// <summary>The slot screws that hold a rail down - small, but they are what makes it read as a real part.</summary>
    private static void DrawScrews(Graphics g, PointF from, PointF to, bool vertical, float offset, float thickness, float scale)
    {
        var length = vertical ? to.Y - from.Y : to.X - from.X;
        var count = Math.Max(2, (int)Math.Round(Math.Abs(length) / (scale * 0.34f)));

        var head = thickness * 0.52f;

        using var brush = new SolidBrush(RailScrew);
        using var slot = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(1f, head * 0.18f));

        for (var i = 0; i < count; i++)
        {
            var t = (i + 0.5f) / count;

            var centre = vertical
                ? new PointF(from.X + offset, from.Y + length * t)
                : new PointF(from.X + length * t, from.Y + offset);

            g.FillEllipse(brush, centre.X - head / 2f, centre.Y - head / 2f, head, head);
            g.DrawLine(slot, centre.X - head * 0.3f, centre.Y - head * 0.2f, centre.X + head * 0.3f, centre.Y + head * 0.2f);
        }
    }

    /// <summary>A pink turn rail: an arc swinging a run of rail through 90 degrees.</summary>
    private static void DrawCurve(Graphics g, RcpCurve curve, float scale, PointF origin, bool detailed)
    {
        var point = Grid(curve.Col, curve.Row, scale, origin);
        var radius = Math.Max(0.25f, curve.Radius) * scale;
        var thickness = Math.Max(2f, scale * RailThickness);

        // The arc's centre sits one radius into the quadrant the corner
        // turns through; the tangent points then land exactly on the two
        // grid lines, so a curve always meets its straights squarely.
        var (dx, dy) = curve.Quadrant switch
        {
            RcpQuadrant.NorthEast => (1f, -1f),
            RcpQuadrant.SouthEast => (1f, 1f),
            RcpQuadrant.SouthWest => (-1f, 1f),
            _ => (-1f, -1f),
        };

        var centre = new PointF(point.X + dx * radius, point.Y + dy * radius);

        var start = curve.Quadrant switch
        {
            RcpQuadrant.NorthEast => 90f,
            RcpQuadrant.SouthEast => 180f,
            RcpQuadrant.SouthWest => 270f,
            _ => 0f,
        };

        void Arc(float r, Color colour, float width)
        {
            using var pen = new Pen(colour, width)
            {
                StartCap = LineCap.Flat,
                EndCap = LineCap.Flat,
            };

            g.DrawArc(pen, centre.X - r, centre.Y - r, r * 2f, r * 2f, start, 90f);
        }

        if (detailed)
        {
            Arc(radius, RailShadow, thickness * 1.45f);
        }

        if (curve.Double)
        {
            var offset = (thickness + scale * DividerGap) / 2f;

            Arc(radius - offset, TurnFace, thickness);
            Arc(radius + offset, TurnFace, thickness);
            Arc(radius - offset, Color.FromArgb(120, TurnEdge), thickness * 0.3f);
            Arc(radius + offset, Color.FromArgb(120, TurnEdge), thickness * 0.3f);
        }
        else
        {
            Arc(radius, TurnFace, thickness);
            Arc(radius, Color.FromArgb(120, TurnEdge), thickness * 0.3f);
        }

        if (!detailed || scale < 40f)
        {
            return;
        }

        // Screws round the arc, same as the straights.
        var head = thickness * 0.52f;

        using var brush = new SolidBrush(Color.FromArgb(200, Color.White));

        for (var i = 0; i < 4; i++)
        {
            var angle = (start + 11f + i * 23f) * MathF.PI / 180f;

            var at = new PointF(
                centre.X + MathF.Cos(angle) * radius,
                centre.Y + MathF.Sin(angle) * radius);

            g.FillEllipse(brush, at.X - head / 2f, at.Y - head / 2f, head, head);
        }
    }

    /// <summary>
    /// The rounded cap on the end of a run of rail.
    ///
    /// Every wall that stops in the middle of the floor has one - it is what
    /// turns two rails back to back into a hairpin you can drive round
    /// instead of a blunt end you hit.
    /// </summary>
    private static void DrawEndCap(Graphics g, Point at, bool vertical, bool twin, float scale, PointF origin)
    {
        var centre = Grid(at.X, at.Y, scale, origin);
        var thickness = Math.Max(2f, scale * RailThickness);

        var span = twin ? thickness * 2f + scale * DividerGap : thickness;
        var radius = span / 2f;

        using var face = new Pen(TurnFace, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        // A half circle capping the rail, turned to face along it.
        var box = new RectangleF(centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f);

        g.DrawArc(face, box, vertical ? 0f : 90f, 180f);

        if (twin)
        {
            return;
        }

        using var dot = new SolidBrush(TurnFace);
        g.FillEllipse(dot, centre.X - thickness / 2f, centre.Y - thickness / 2f, thickness, thickness);
    }

    // --- Flat markings -------------------------------------------------------

    private static void DrawDecal(Graphics g, RcpDecal decal, float scale, PointF origin, bool detailed)
    {
        var rect = new RectangleF(origin.X + decal.Col * scale, origin.Y + decal.Row * scale, scale, scale);

        var state = g.Save();

        g.TranslateTransform(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);
        g.RotateTransform(decal.Turns * 90f);
        g.TranslateTransform(-rect.Width / 2f, -rect.Height / 2f);

        switch (decal.Kind)
        {
            case RcpDecalKind.FinishLine:
                DrawChequer(g, new RectangleF(0, rect.Height * 0.36f, rect.Width, rect.Height * 0.28f), 8, 2);
                break;

            case RcpDecalKind.GridBox:
                DrawChequer(g, new RectangleF(rect.Width * 0.18f, rect.Height * 0.3f, rect.Width * 0.3f, rect.Height * 0.4f), 2, 4);
                break;

            case RcpDecalKind.Post:
                DrawPost(g, rect, detailed);
                break;

            case RcpDecalKind.Kerb:
                DrawKerb(g, rect);
                break;

            case RcpDecalKind.Furniture:
                DrawFurniture(g, rect);
                break;

            case RcpDecalKind.SplitLoop1:
            case RcpDecalKind.SplitLoop2:
            case RcpDecalKind.SplitLoop3:
                DrawSplitLoop(g, rect, RcpPlan.SplitNumberOf(decal.Kind), decal.Turns);
                break;
        }

        g.Restore(state);
    }

    /// <summary>
    /// A split loop: a blue band across the track where the finish line's
    /// chequer would be, and "S1", "S2" or "S3" on it - kept upright
    /// whichever way the band is turned.
    /// </summary>
    private static void DrawSplitLoop(Graphics g, RectangleF rect, int number, int turns)
    {
        var band = new RectangleF(0, rect.Height * 0.38f, rect.Width, rect.Height * 0.24f);

        using (var face = new SolidBrush(LoopFace))
        using (var edge = new Pen(LoopEdge, Math.Max(1f, rect.Height * 0.03f)))
        {
            g.FillRectangle(face, band);
            g.DrawRectangle(edge, band.X, band.Y, band.Width, band.Height);
        }

        if (rect.Height < 18)
        {
            return;
        }

        // Turned back, so the label reads the right way up.
        var state = g.Save();
        g.TranslateTransform(rect.Width / 2f, rect.Height / 2f);
        g.RotateTransform(-turns * 90f);

        using var font = new Font(AppTheme.BoldFont.FontFamily, Math.Max(5f, rect.Height * 0.17f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.White);
        using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.DrawString($"S{number}", font, text, new RectangleF(-rect.Width / 2f, -rect.Height / 2f, rect.Width, rect.Height), centred);
        g.Restore(state);
    }

    private static void DrawChequer(Graphics g, RectangleF band, int columns, int rows)
    {
        using var light = new SolidBrush(ChequerLight);
        using var dark = new SolidBrush(ChequerDark);

        var cellW = band.Width / columns;
        var cellH = band.Height / rows;

        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                var brush = (column + row) % 2 == 0 ? light : dark;
                g.FillRectangle(brush, band.Left + column * cellW, band.Top + row * cellH, cellW, cellH);
            }
        }
    }

    /// <summary>
    /// A rail post, drawn where a rail post actually goes: on the corner
    /// where four tiles meet, not in the middle of one.
    ///
    /// The post is a disc that straddles the join, and it is bolted down
    /// with one piece of hardware on EACH of the four tiles around it -
    /// that is what holds the join together. Drawing it in the centre of a
    /// single tile, with its four screws inside its own disc, made it look
    /// like a thing you drop on the floor rather than a thing that pins
    /// four tiles to each other.
    ///
    /// Drawn at (0, 0), which is this tile's top-left corner, so it
    /// deliberately overhangs into the three neighbours. Decals are not
    /// clipped to their tile, which is what makes that possible.
    ///
    /// The decal's rotation still works and is now useful: the transform
    /// turns about the tile's centre, so each quarter turn carries the post
    /// round to the next corner of this tile. The disc is radially
    /// symmetric, so nothing looks rotated - the turns just choose which
    /// corner.
    /// </summary>
    private static void DrawPost(Graphics g, RectangleF rect, bool detailed)
    {
        var radius = rect.Width * 0.17f;

        // The intersection itself.
        var centre = new PointF(0f, 0f);

        using (var shadow = new SolidBrush(RailShadow))
        {
            g.FillEllipse(shadow, centre.X - radius + radius * 0.16f, centre.Y - radius + radius * 0.16f, radius * 2f, radius * 2f);
        }

        using (var fill = new SolidBrush(TurnFace))
        {
            g.FillEllipse(fill, centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f);
        }

        using (var rim = new Pen(TurnEdge, Math.Max(1f, radius * 0.16f)))
        {
            g.DrawEllipse(rim, centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f);
        }

        if (!detailed)
        {
            return;
        }

        // One mount per tile, pushed far enough out along each diagonal to
        // sit clearly on its own tile rather than on the join.
        var reach = radius * 1.55f;
        var plate = radius * 0.52f;

        using var mount = new SolidBrush(Color.FromArgb(235, 226, 228, 236));
        using var mountEdge = new Pen(Color.FromArgb(200, 40, 34, 58), Math.Max(1f, radius * 0.1f));
        using var bolt = new SolidBrush(Color.FromArgb(210, 20, 20, 22));

        for (var i = 0; i < 4; i++)
        {
            var angle = MathF.PI / 4f + i * MathF.PI / 2f;

            var at = new PointF(
                centre.X + MathF.Cos(angle) * reach,
                centre.Y + MathF.Sin(angle) * reach);

            g.FillEllipse(mount, at.X - plate / 2f, at.Y - plate / 2f, plate, plate);
            g.DrawEllipse(mountEdge, at.X - plate / 2f, at.Y - plate / 2f, plate, plate);

            var head = plate * 0.38f;
            g.FillEllipse(bolt, at.X - head / 2f, at.Y - head / 2f, head, head);
        }

        // The socket in the middle of the post, where the rail drops in.
        using var socket = new SolidBrush(Color.FromArgb(200, 20, 20, 22));
        g.FillRectangle(socket, centre.X - radius * 0.22f, centre.Y - radius * 0.22f, radius * 0.44f, radius * 0.44f);
    }

    /// <summary>A red-and-white apex kerb, of the sort that gets dropped on the floor to mark a line.</summary>
    private static void DrawKerb(Graphics g, RectangleF rect)
    {
        var radius = rect.Width * 0.22f;
        var centre = new PointF(rect.Width / 2f, rect.Height / 2f + radius * 0.4f);
        var thickness = rect.Width * 0.09f;

        using var white = new Pen(ChequerLight, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var red = new Pen(ColorTranslator.FromHtml("#E23B3B"), thickness);

        var box = new RectangleF(centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f);

        g.DrawArc(white, box, 180f, 180f);

        // Two red bands across the white, which is all the pattern needs at
        // this size.
        for (var i = 0; i < 2; i++)
        {
            g.DrawArc(red, box, 205f + i * 60f, 28f);
        }
    }

    private static void DrawFurniture(Graphics g, RectangleF rect)
    {
        var box = new RectangleF(rect.Width * 0.18f, rect.Height * 0.22f, rect.Width * 0.64f, rect.Height * 0.56f);

        using (var shadow = new SolidBrush(RailShadow))
        {
            g.FillRectangle(shadow, box.Left + 3, box.Top + 3, box.Width, box.Height);
        }

        using var fill = new SolidBrush(ColorTranslator.FromHtml("#26262A"));
        using var edge = new Pen(Color.FromArgb(120, Color.White), 1.2f);
        using var path = AppTheme.RoundedRect(box, (int)Math.Max(2, rect.Width * 0.05f));

        g.FillPath(fill, path);
        g.DrawPath(edge, path);
    }

    // --- The racing line -----------------------------------------------------

    public static void DrawRoute(Graphics g, RcpTrackDesign design, float scale, PointF origin)
    {
        if (design.Route.Count < 2)
        {
            return;
        }

        var points = design.Route
            .Select(p => new PointF(origin.X + p.X * scale, origin.Y + p.Y * scale))
            .ToArray();

        using var path = new GraphicsPath();
        path.AddLines(points);

        if (design.RouteClosed)
        {
            path.CloseFigure();
        }

        using (var glow = new Pen(Color.FromArgb(70, AppTheme.Accent), Math.Max(4f, scale * 0.30f))
               {
                   LineJoin = LineJoin.Round,
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round,
               })
        {
            g.DrawPath(glow, path);
        }

        using (var line = new Pen(AppTheme.Accent, Math.Max(1.6f, scale * 0.06f))
               {
                   LineJoin = LineJoin.Round,
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round,
                   DashStyle = DashStyle.Dash,
                   DashPattern = new[] { 4f, 3f },
               })
        {
            g.DrawPath(line, path);
        }

        DrawDirectionArrows(g, points, design.RouteClosed, scale);
    }

    private static void DrawDirectionArrows(Graphics g, PointF[] points, bool closed, float scale)
    {
        using var brush = new SolidBrush(AppTheme.Accent);

        var count = closed ? points.Length : points.Length - 1;

        for (var i = 0; i < count; i++)
        {
            var from = points[i];
            var to = points[(i + 1) % points.Length];

            var mid = new PointF((from.X + to.X) / 2f, (from.Y + to.Y) / 2f);
            var angle = MathF.Atan2(to.Y - from.Y, to.X - from.X);
            var size = Math.Max(3f, scale * 0.12f);

            var state = g.Save();

            g.TranslateTransform(mid.X, mid.Y);
            g.RotateTransform(angle * 180f / MathF.PI);

            g.FillPolygon(brush, new[]
            {
                new PointF(size, 0),
                new PointF(-size * 0.7f, size * 0.7f),
                new PointF(-size * 0.7f, -size * 0.7f),
            });

            g.Restore(state);
        }
    }

    public static void DrawPaceCar(Graphics g, PointF position, float headingDegrees, float scale)
    {
        var state = g.Save();

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(position.X, position.Y);
        g.RotateTransform(headingDegrees);

        var length = Math.Max(8f, scale * 0.34f);
        var width = length * 0.58f;

        using (var glow = new SolidBrush(Color.FromArgb(60, AppTheme.Primary)))
        {
            g.FillEllipse(glow, -length, -width, length * 2f, width * 2f);
        }

        var body = new RectangleF(-length / 2f, -width / 2f, length, width);

        using (var path = AppTheme.RoundedRect(body, (int)Math.Max(2, width * 0.35f)))
        using (var fill = new LinearGradientBrush(body, AppTheme.Primary, AppTheme.PrimaryDark, LinearGradientMode.Vertical))
        {
            g.FillPath(fill, path);
        }

        using (var canopy = new SolidBrush(Color.FromArgb(190, 20, 20, 24)))
        {
            g.FillEllipse(canopy, -length * 0.1f, -width * 0.26f, length * 0.42f, width * 0.52f);
        }

        using (var nose = new SolidBrush(Color.FromArgb(220, Color.White)))
        {
            g.FillRectangle(nose, length * 0.34f, -width * 0.3f, length * 0.12f, width * 0.6f);
        }

        g.Restore(state);
    }

    /// <summary>Renders a plan to a bitmap, for the track page thumbnail and the PNG export.</summary>
    public static Bitmap Render(RcpTrackDesign design, Size size, Color background, bool showRoute = true)
    {
        var bitmap = new Bitmap(Math.Max(16, size.Width), Math.Max(16, size.Height));

        // 96 DPI whatever the screen: a bitmap otherwise takes the screen's
        // DPI, and in a DPI-aware run (--dpi-aware) the tile labels' point-size
        // text would draw half as big again inside the same pixels. Changes
        // nothing at 96 DPI.
        bitmap.SetResolution(96, 96);

        using var g = Graphics.FromImage(bitmap);

        g.Clear(background);

        if (design.IsEmpty)
        {
            return bitmap;
        }

        // Framed round the line only when the line is drawn.
        var bounds = design.BoundsInTiles(includeRoute: showRoute);
        var margin = 0.6f;

        var scale = Math.Min(
            (bitmap.Width - 2) / (bounds.Width + margin * 2),
            (bitmap.Height - 2) / (bounds.Height + margin * 2));

        var origin = new PointF(
            (bitmap.Width - bounds.Width * scale) / 2f - bounds.Left * scale,
            (bitmap.Height - bounds.Height * scale) / 2f - bounds.Top * scale);

        Draw(g, design, scale, origin, showRoute, showLabels: scale > 40f, offTrack: background);

        return bitmap;
    }
}
