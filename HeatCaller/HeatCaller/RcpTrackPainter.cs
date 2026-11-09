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
/// pink cap wherever a run of rail simply stops. The floor is drawn as the
/// same tile everywhere. Corner tiles ARE real (Asher, 25 Oct 2026): RCP's
/// turn rails stand on an outer curve tile, with an inner corner tile at the
/// inside of the bend. They are worked out from the rails and counted
/// (RcpPlan.CornerTiles), but drawn plain until a photo of what is printed
/// on them is to hand. An earlier version drew the shape of a corner into
/// the tile itself, which no real tile has.
///
/// All of it is drawn in code, down to the slot screws along the rails and
/// the T-shaped teeth the tiles lock together with (RcpJoints, measured off
/// a real tile), so it stays sharp at any size and there is nothing to ship.
/// </summary>
public static class RcpTrackPainter
{
    // Sampled from photographs of a built track.
    private static readonly Color FoamLight = ColorTranslator.FromHtml("#42423C");
    private static readonly Color FoamDark = ColorTranslator.FromHtml("#2B2B28");
    private static readonly Color FoamSeam = ColorTranslator.FromHtml("#1B1B19");

    // The foam's mottle at close zoom: a real tile is plain, slightly
    // blotchy foam - no pin holes, no grain (Asher's photo, 25 Oct 2026).
    private static readonly Color FoamFleckLight = Color.FromArgb(7, 255, 255, 255);
    private static readonly Color FoamFleckDark = Color.FromArgb(12, 0, 0, 0);

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

    /// <summary>The walls of the room, when the club has said how big it is.</summary>
    private static readonly Color RoomLine = ColorTranslator.FromHtml("#6B7A8F");

    /// <summary>The same, when what is drawn does not go in it.</summary>
    private static readonly Color RoomTooSmall = ColorTranslator.FromHtml("#C2524C");
    private static readonly Color InfieldFill = ColorTranslator.FromHtml("#1E1E20");

    /// <summary>Chequer on this track is grey and white, not black and white.</summary>
    private static readonly Color ChequerDark = ColorTranslator.FromHtml("#8E9094");
    private static readonly Color ChequerLight = ColorTranslator.FromHtml("#E9E9E4");

    /// <summary>
    /// How wide a tooth's head has to be on screen, in pixels, before the
    /// teeth are drawn. Narrower than this they are a smudge, and a straight
    /// seam reads better.
    /// </summary>
    private const float LeastToothPixels = 4f;

    /// <summary>A whole tile's outline at (0,0), with and without the clipped corner - built once.</summary>
    private static readonly PointF[] UnitOutline = RcpJoints.Outline(0, 0).ToArray();
    private static readonly PointF[] UnitOutlineClipped = RcpJoints.Outline(0, 0, clipSouthEast: true).ToArray();

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

        // Behind everything, because it is the thing everything has to fit
        // inside rather than a part of the track.
        DrawRoom(g, design, scale, origin);

        // Blocked-out squares first, then the floor over them: a tile's teeth
        // reach a centimetre past its square, and on a real track they would
        // lie over whatever is next to them.
        foreach (var tile in design.Tiles.Where(t => t.Kind == RcpTileKind.Infield))
        {
            DrawInfield(g, TileRect(tile, scale, origin), detailed);
        }

        var floor = new HashSet<(int, int)>(
            design.Tiles.Where(t => t.Kind != RcpTileKind.Infield).Select(t => (t.Col, t.Row)));

        foreach (var tile in design.Tiles.Where(t => t.Kind != RcpTileKind.Infield))
        {
            // The floor's outside corner at the bottom right gets the clipped
            // corner the photographed tile has there.
            var outsideCorner = !floor.Contains((tile.Col + 1, tile.Row))
                                && !floor.Contains((tile.Col, tile.Row + 1))
                                && !floor.Contains((tile.Col + 1, tile.Row + 1));

            DrawTileFloor(g, tile, scale, origin, detailed, outsideCorner);
        }

        // Rail posts are drawn with the rails, below: a rail part, not a
        // marking on the floor.
        foreach (var decal in design.Decals.Where(d => d.Kind != RcpDecalKind.Post))
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
        //
        // Each on the tile it is bolted to (railed tiles, 8 Nov 2026): a
        // rail sits flush on its own tile's edge, not across the seam - so
        // a divider's two rails stand one on each lane's tile, and two
        // turns that meet at one point end a rail's width apart, as they
        // do on the floor, rather than in a single point.
        var sides = RailSides(design);

        foreach (var wall in design.Walls)
        {
            DrawWall(g, wall, scale, origin, detailed, sides.GetValueOrDefault((wall.Col, wall.Row, wall.Vertical)));
        }

        foreach (var curve in design.Curves)
        {
            DrawCurve(g, curve, scale, origin, detailed);
        }

        DrawInnerCornerPieces(g, design, scale, origin, detailed);

        // The rail post quarters laid by hand: the same corner rail piece,
        // over the rail ends it joins (Asher, 8 Nov 2026).
        foreach (var post in design.Decals.Where(d => d.Kind == RcpDecalKind.Post))
        {
            DrawDecal(g, post, scale, origin, detailed);
        }

        foreach (var (at, vertical, twin) in RcpPlan.OpenEnds(design))
        {
            // Which way the rail runs from the end: the cap bulges the
            // other way, out past the end - it used to face one fixed way,
            // so at a run's right-hand or top end it sat back over the rails.
            var forward = design.WallAt(at.X, at.Y, vertical) is not null;
            var seam = forward ? (at.X, at.Y, vertical) : vertical ? (at.X, at.Y - 1, true) : (at.X - 1, at.Y, false);

            DrawEndCap(g, at, vertical, twin, outward: !forward, sides.GetValueOrDefault(seam), scale, origin);
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

    /// <summary>
    /// One floor tile: its foam, and its outline with the teeth on.
    ///
    /// Each tile is drawn whole, teeth and all, from RcpJoints - and because
    /// a tile's outline and its neighbour's are the same line along their
    /// seam, stroking every outline draws each seam exactly once over (the
    /// two strokes land on each other), and an edge with no neighbour shows
    /// its bare teeth with the floor between them. It used to give every
    /// edge one round tab bulging outwards, all four of them, so each tab
    /// spilled over the tile beside it and was half painted out.
    /// </summary>
    private static void DrawTileFloor(Graphics g, RcpTile tile, float scale, PointF origin, bool detailed, bool outsideCorner)
    {
        var rect = TileRect(tile, scale, origin);

        // The brush covers the teeth too, which reach past the square.
        var reach = RcpJoints.StripDepth * scale;
        var shade = new RectangleF(rect.X - reach, rect.Y - reach, Math.Max(1f, rect.Width + reach * 2f), Math.Max(1f, rect.Height + reach * 2f));

        using var fill = new LinearGradientBrush(shade, FoamLight, FoamDark, LinearGradientMode.ForwardDiagonal);

        if (!detailed)
        {
            g.FillRectangle(fill, rect);
            return;
        }

        using var path = new GraphicsPath();

        if (scale * RcpJoints.HeadWidth >= LeastToothPixels)
        {
            var unit = outsideCorner ? UnitOutlineClipped : UnitOutline;
            var points = new PointF[unit.Length];

            for (var i = 0; i < unit.Length; i++)
            {
                points[i] = new PointF(rect.X + unit[i].X * scale, rect.Y + unit[i].Y * scale);
            }

            path.AddPolygon(points);
        }
        else
        {
            path.AddRectangle(rect);
        }

        g.FillPath(fill, path);

        if (scale >= 46f)
        {
            DrawMottle(g, tile, rect, path);
        }

        using var seam = new Pen(FoamSeam, Math.Max(1f, scale * 0.012f)) { LineJoin = LineJoin.Round };
        g.DrawPath(seam, path);
    }

    /// <summary>
    /// A few soft flecks, so close up the foam reads as foam rather than as
    /// a flat colour. Placed from the tile's grid position, never at random,
    /// so they stay put as the board is panned and redrawn.
    /// </summary>
    private static void DrawMottle(Graphics g, RcpTile tile, RectangleF rect, GraphicsPath outline)
    {
        var state = g.Save();
        g.SetClip(outline);

        using var light = new SolidBrush(FoamFleckLight);
        using var dark = new SolidBrush(FoamFleckDark);

        var seed = unchecked((uint)(tile.Col * 73856093) ^ (uint)(tile.Row * 19349663));

        float Next()
        {
            seed = unchecked(seed * 1664525u + 1013904223u);
            return (seed >> 8) / 16777216f;
        }

        for (var i = 0; i < 14; i++)
        {
            var size = rect.Width * (0.05f + Next() * 0.12f);
            var x = rect.X + Next() * rect.Width - size / 2f;
            var y = rect.Y + Next() * rect.Height - size / 2f;
            g.FillEllipse(i % 3 == 0 ? light : dark, x, y, size, size * (0.6f + Next() * 0.5f));
        }

        g.Restore(state);
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

    /// <summary>
    /// The room the track has to go in, as a dashed outline round it.
    ///
    /// ANCHORED AT THE FLOOR'S TOP-LEFT, not centred on it. A club lays a
    /// track into the corner of a hall and works outwards, so the spare
    /// space wants to be shown where it really is - along two sides - and
    /// not split into a decorative margin all round. It also means the
    /// outline stops moving about as the plan is drawn: the corner it is
    /// pinned to is the corner the first tile went down in.
    ///
    /// Drawn in the warning colour when the layout does not fit, so the
    /// answer is the same whether somebody reads the side panel or just
    /// looks at the picture.
    /// </summary>
    private static void DrawRoom(Graphics g, RcpTrackDesign design, float scale, PointF origin)
    {
        if (!design.HasRoom || design.TileSizeMm <= 0)
        {
            return;
        }

        var floor = design.BoundsInTiles(includeRoute: false);

        if (floor.Width <= 0 || floor.Height <= 0)
        {
            return;
        }

        // The room in tiles, which is the unit everything here is drawn in.
        var wide = (float)(design.RoomWidthMm / design.TileSizeMm);
        var deep = (float)(design.RoomDepthMm / design.TileSizeMm);

        // Asked of RoomFit, so the picture and the side panel cannot
        // disagree. This used to test the straight-on orientation only,
        // while RoomFit counts a quarter turn as a fit - so a layout that
        // went in the hall turned was outlined in the "won't fit" red while
        // the panel beside it said it fitted turned, and a club reading the
        // picture concluded it did not go in at all.
        var fits = RoomFit.Check(
            floor.Width * design.TileSizeMm,
            floor.Height * design.TileSizeMm,
            design.RoomWidthMm,
            design.RoomDepthMm).Fits;

        var room = new RectangleF(
            origin.X + floor.X * scale,
            origin.Y + floor.Y * scale,
            wide * scale,
            deep * scale);

        // DashPattern alone: setting it puts the pen into Custom anyway,
        // so naming a DashStyle beside it only reads as if it did something.
        using var pen = new Pen(fits ? RoomLine : RoomTooSmall, Math.Max(1.5f, scale * 0.035f))
        {
            DashPattern = new[] { 6f, 4f },
        };

        g.DrawRectangle(pen, room.X, room.Y, room.Width, room.Height);
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
    /// <summary>
    /// Which side of its seam each single rail stands on: -1 on the tile
    /// north or west of it, +1 on the tile south or east, 0 across the
    /// seam. The tile it is bolted to (RcpTile.Piece, worked out by
    /// RcpPieces.Adopt) says; a rail no tile claims - a custom second rail,
    /// or a plan never opened since pieces existed - goes on the floor's
    /// side when only one side is floor, and across the seam otherwise.
    /// </summary>
    internal static Dictionary<(int Col, int Row, bool Vertical), int> RailSides(RcpTrackDesign design)
    {
        var sides = new Dictionary<(int, int, bool), int>();

        foreach (var tile in design.Tiles.Where(t => RcpPieces.HasStraight(t.Piece)))
        {
            // A tile facing east or south is the west or north one of the seam.
            var side = tile.Facing is RcpEdge.East or RcpEdge.South ? -1 : 1;
            sides.TryAdd(RcpPlan.EdgeLine(tile.Col, tile.Row, tile.Facing), side);
        }

        bool Floor(int col, int row) => design.At(col, row) is { } t && t.Kind != RcpTileKind.Infield;

        foreach (var wall in design.Walls.Where(w => !w.Double))
        {
            var seam = (wall.Col, wall.Row, wall.Vertical);

            if (sides.ContainsKey(seam))
            {
                continue;
            }

            var (aCol, aRow) = wall.Vertical ? (wall.Col - 1, wall.Row) : (wall.Col, wall.Row - 1);
            var before = Floor(aCol, aRow);
            var after = Floor(wall.Col, wall.Row);

            sides[seam] = before && !after ? -1 : after && !before ? 1 : 0;
        }

        return sides;
    }

    /// <summary>How far a rail on its own tile sits from the seam, in pixels: the same as each of a divider's two rails.</summary>
    private static float RailInset(float scale) => (Math.Max(2f, scale * RailThickness) + scale * DividerGap) / 2f;

    private static void DrawWall(Graphics g, RcpWall wall, float scale, PointF origin, bool detailed, int side)
    {
        var from = Grid(wall.Col, wall.Row, scale, origin);
        var to = Grid(wall.To.X, wall.To.Y, scale, origin);

        var thickness = Math.Max(2f, scale * RailThickness);

        if (!wall.Double)
        {
            DrawRailBar(g, from, to, wall.Vertical, side * RailInset(scale), thickness, RailFace, RailEdge, detailed, scale);
            return;
        }

        var offset = RailInset(scale);

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

    /// <summary>
    /// How far apart the bolts sit along a rail, in tiles. A straight gets
    /// one every third of a tile; an arc is given more room, because the
    /// real curved rails carry three across a quarter turn where that
    /// spacing would draw five.
    /// </summary>
    private const float BoltSpacing = 0.34f;

    /// <inheritdoc cref="BoltSpacing"/>
    private const float BoltSpacingOnArc = 0.5f;

    /// <summary>
    /// One bolt head: a grey plastic disc with a slot across it.
    ///
    /// These are not decoration. A rail does not stand on the floor of its
    /// own accord - it is BOLTED THROUGH the tile it runs along, and on a
    /// real tile the heads are the most obvious thing about it. Drawing
    /// them is what stops a plan reading as lines on a grid and starts it
    /// reading as the parts in the box.
    /// </summary>
    private static void DrawBolt(Graphics g, Brush head, Pen slot, PointF at, float size)
    {
        g.FillEllipse(head, at.X - size / 2f, at.Y - size / 2f, size, size);
        g.DrawLine(slot, at.X - size * 0.3f, at.Y - size * 0.2f, at.X + size * 0.3f, at.Y + size * 0.2f);
    }

    /// <summary>The bolts that hold a straight rail down - see DrawBolt.</summary>
    private static void DrawScrews(Graphics g, PointF from, PointF to, bool vertical, float offset, float thickness, float scale)
    {
        var length = vertical ? to.Y - from.Y : to.X - from.X;
        var count = Math.Max(2, (int)Math.Round(Math.Abs(length) / (scale * BoltSpacing)));

        var head = thickness * 0.52f;

        using var brush = new SolidBrush(RailScrew);
        using var slot = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(1f, head * 0.18f));

        for (var i = 0; i < count; i++)
        {
            var t = (i + 0.5f) / count;

            var centre = vertical
                ? new PointF(from.X + offset, from.Y + length * t)
                : new PointF(from.X + length * t, from.Y + offset);

            DrawBolt(g, brush, slot, centre, head);
        }
    }

    /// <summary>
    /// The bolts along a turn rail, spaced round the arc.
    ///
    /// The same heads as a straight's, because they are the same bolts
    /// through the same kind of tile. They used to be plain white dots at
    /// four fixed angles, which read as nothing in particular and did not
    /// follow the rail when a turn was doubled.
    /// </summary>
    private static void DrawArcBolts(Graphics g, PointF centre, float radius, float start, float thickness, float scale)
    {
        // A quarter turn, so the arc is a quarter of the circumference.
        var along = radius * MathF.PI / 2f;
        var count = Math.Max(2, (int)Math.Round(along / (scale * BoltSpacingOnArc)));

        var head = thickness * 0.52f;

        using var brush = new SolidBrush(RailScrew);
        using var slot = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(1f, head * 0.18f));

        for (var i = 0; i < count; i++)
        {
            // Inset by half a step at each end, as the straights are, so no
            // bolt sits on the join where one rail meets the next.
            var angle = (start + 90f * (i + 0.5f) / count) * MathF.PI / 180f;

            DrawBolt(
                g,
                brush,
                slot,
                new PointF(centre.X + MathF.Cos(angle) * radius, centre.Y + MathF.Sin(angle) * radius),
                head);
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

        // A single turn rail sits on its own tile - the quarter-circle outer
        // curve tile - flush with its curved edge, so a rail's inset inside
        // the arc the grid lines make (8 Nov 2026). Its ends then land on
        // the rails beside it, which sit on their own tiles too; drawn on
        // the grid lines, two turns sharing a point met in a cusp.
        var inset = curve.Double || radius <= RailInset(scale) * 2f ? 0f : RailInset(scale);
        var drawn = radius - inset;

        if (detailed)
        {
            Arc(drawn, RailShadow, thickness * 1.45f);
        }

        if (curve.Double)
        {
            var offset = RailInset(scale);

            Arc(radius - offset, TurnFace, thickness);
            Arc(radius + offset, TurnFace, thickness);
            Arc(radius - offset, Color.FromArgb(120, TurnEdge), thickness * 0.3f);
            Arc(radius + offset, Color.FromArgb(120, TurnEdge), thickness * 0.3f);
        }
        else
        {
            Arc(drawn, TurnFace, thickness);
            Arc(drawn, Color.FromArgb(120, TurnEdge), thickness * 0.3f);
        }

        if (!detailed || scale < 40f)
        {
            return;
        }

        // Bolts round the arc, the same ones the straights get - and on
        // the rails themselves, so a doubled turn shows the bolts of both
        // rather than a row down the gap between them.
        if (curve.Double)
        {
            var boltOffset = (thickness + scale * DividerGap) / 2f;

            DrawArcBolts(g, centre, radius - boltOffset, start, thickness, scale);
            DrawArcBolts(g, centre, radius + boltOffset, start, thickness, scale);
        }
        else
        {
            DrawArcBolts(g, centre, drawn, start, thickness, scale);
        }
    }

    /// <summary>
    /// The rounded cap on the end of a run of rail.
    ///
    /// Every wall that stops in the middle of the floor has one - it is what
    /// turns two rails back to back into a hairpin you can drive round
    /// instead of a blunt end you hit.
    /// </summary>
    /// <param name="outward">The cap bulges east or south of the end (the rail runs away west or north); false, the other way.</param>
    /// <param name="side">Which side of its seam a single rail stands on - see RailSides.</param>
    private static void DrawEndCap(Graphics g, Point at, bool vertical, bool twin, bool outward, int side, float scale, PointF origin)
    {
        var centre = Grid(at.X, at.Y, scale, origin);
        var thickness = Math.Max(2f, scale * RailThickness);

        // A single rail's cap on the rail, on its own tile.
        if (!twin && side != 0)
        {
            var shift = side * RailInset(scale);
            centre = vertical ? new PointF(centre.X + shift, centre.Y) : new PointF(centre.X, centre.Y + shift);
        }

        var span = twin ? thickness * 2f + scale * DividerGap : thickness;
        var radius = span / 2f;

        using var face = new Pen(TurnFace, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        // A half circle capping the rail, bulging out past its end. Angles
        // run clockwise from east: 90 is south, 180 west, 270 north.
        var box = new RectangleF(centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f);

        var start = (vertical, outward) switch
        {
            (true, true) => 0f,      // a rail ending at its south end: round below it
            (true, false) => 180f,   // ending at its north end: round above it
            (false, true) => 270f,   // ending at its east end: round to the right
            _ => 90f,                // ending at its west end: round to the left
        };

        g.DrawArc(face, box, start, 180f);

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
    /// A rail post's quarter: the pink quarter disc in one corner of this
    /// tile, with its bolt through its middle and its quarter of the square
    /// socket at the corner.
    ///
    /// A rail post on the floor is four separate quarters, each bolted to
    /// its own tile round the corner where four tiles meet - so a post laid
    /// on a tile is that tile's quarter only, and four of them, one on each
    /// tile, make the whole disc (Asher, 8 Nov 2026). It used to draw the
    /// whole disc from one tile, hanging over three tiles that might not be
    /// there.
    ///
    /// Drawn at (0, 0), this tile's top-left corner, into the tile. The
    /// decal's quarter turns carry it round to the other corners - the
    /// transform turns about the tile's centre.
    /// </summary>
    private static void DrawPost(Graphics g, RectangleF rect, bool detailed)
    {
        var radius = rect.Width * PostRadius;
        var corner = new PointF(0f, 0f);

        // The quarter south-east of the corner: the one on this tile.
        DrawPostQuarter(g, corner, radius, 0f, detailed, shadow: detailed);

        // Its quarter of the square socket, at the corner.
        using var socket = new SolidBrush(Color.FromArgb(220, 20, 20, 22));
        g.FillRectangle(socket, corner.X, corner.Y, radius * 0.2f, radius * 0.2f);
    }

    /// <summary>The rail post's radius as a share of a tile, measured off Asher's floor photos - the inner corner piece is a quarter of it.</summary>
    private const float PostRadius = 0.2f;

    /// <summary>
    /// One quarter of a rail post: a pink quarter disc on one tile, with its
    /// edges along the seams and one bolt through its middle. Four make the
    /// post; one on its own is the inner corner piece, filling the gap where
    /// two rails meet square round the inside of a bend (Asher, 8 Nov 2026).
    /// </summary>
    /// <param name="start">The quarter's first edge, in degrees clockwise from east - 0 for the quarter south-east of the point.</param>
    private static void DrawPostQuarter(Graphics g, PointF centre, float radius, float start, bool detailed, bool shadow)
    {
        var box = new RectangleF(centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f);

        if (shadow)
        {
            using var shade = new SolidBrush(RailShadow);
            g.FillPie(shade, box.X + radius * 0.12f, box.Y + radius * 0.12f, box.Width, box.Height, start, 90f);
        }

        using (var fill = new SolidBrush(TurnFace))
        {
            g.FillPie(fill, box.X, box.Y, box.Width, box.Height, start, 90f);
        }

        // Its rim and the split along the seams.
        using (var rim = new Pen(TurnEdge, Math.Max(1f, radius * 0.08f)))
        {
            g.DrawPie(rim, box.X, box.Y, box.Width, box.Height, start, 90f);
        }

        if (!detailed)
        {
            return;
        }

        // The bolt, in the middle of the quarter.
        var angle = (start + 45f) * MathF.PI / 180f;
        var at = new PointF(centre.X + MathF.Cos(angle) * radius * 0.56f, centre.Y + MathF.Sin(angle) * radius * 0.56f);
        var head = radius * 0.34f;

        using var brush = new SolidBrush(RailScrew);
        using var slot = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(1f, head * 0.18f));

        DrawBolt(g, brush, slot, at, head);
    }

    /// <summary>
    /// The inner corner pieces: on each inner corner tile, a quarter of a
    /// rail post in the corner where the bend's two rails meet square
    /// (RcpTile.Corner, worked out by RcpPieces.Adopt).
    /// </summary>
    private static void DrawInnerCornerPieces(Graphics g, RcpTrackDesign design, float scale, PointF origin, bool detailed)
    {
        foreach (var tile in design.Tiles.Where(t => t.Piece == RcpPiece.InnerCorner))
        {
            var point = RcpPieces.CornerPoint(tile.Col, tile.Row, tile.Corner);
            var centre = Grid(point.X, point.Y, scale, origin);

            // The quarter that lies on this tile: the one facing back from
            // the corner into the tile.
            var start = RcpPieces.Opposite(tile.Corner) switch
            {
                RcpQuadrant.SouthEast => 0f,
                RcpQuadrant.SouthWest => 90f,
                RcpQuadrant.NorthWest => 180f,
                _ => 270f,
            };

            DrawPostQuarter(g, centre, scale * PostRadius, start, detailed, shadow: detailed);
        }
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

    /// <summary>
    /// The part of the plan in <paramref name="region"/> (in tiles) at
    /// exactly <paramref name="pixelsPerTile"/> - for a picture saved at a
    /// size chosen, and for each sheet of a plan printed to scale (Track
    /// Designer overhaul, Phase 7). Render, by contrast, fits the plan to a
    /// size. 96 DPI, as Render is. The caller keeps the size sensible: the
    /// bitmap is the region's size at that scale, at least 16 pixels a side.
    /// </summary>
    /// <param name="opaque">No transparency, at three bytes a pixel rather than four - for a print, where a sheet's pictures are held in memory together.</param>
    public static Bitmap RenderRegion(RcpTrackDesign design, RectangleF region, float pixelsPerTile, Color background, bool showRoute = true, bool opaque = false)
    {
        var width = Math.Max(16, (int)Math.Ceiling(region.Width * pixelsPerTile));
        var height = Math.Max(16, (int)Math.Ceiling(region.Height * pixelsPerTile));

        var bitmap = opaque
            ? new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb)
            : new Bitmap(width, height);
        bitmap.SetResolution(96, 96);

        using var g = Graphics.FromImage(bitmap);

        g.Clear(background);

        if (!design.IsEmpty)
        {
            var origin = new PointF(-region.Left * pixelsPerTile, -region.Top * pixelsPerTile);

            Draw(g, design, pixelsPerTile, origin, showRoute, showLabels: pixelsPerTile > 40f, offTrack: background);
        }

        return bitmap;
    }
}
