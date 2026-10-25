using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>Which way the angled view looks (Track Designer overhaul, Phase 8).</summary>
/// <param name="Yaw">Turned round, in degrees: 0 looks up the plan from its bottom edge.</param>
/// <param name="Pitch">Tipped, in degrees from straight down: 0 is the flat plan, AngledView.MostPitch the most it tips.</param>
/// <param name="Zoom">How close: 1 fits the whole plan.</param>
public readonly record struct RcpAngle(float Yaw, float Pitch, float Zoom)
{
    /// <summary>Where the view starts: a little round, tipped as a person standing at the end of the hall sees it.</summary>
    public static RcpAngle Start => new(-20f, 52f, 1f);

    /// <summary>Turned and tipped by a drag of <paramref name="dx"/>, <paramref name="dy"/> pixels.</summary>
    public RcpAngle Dragged(float dx, float dy) => this with
    {
        Yaw = ((Yaw + dx * 0.4f) % 360f + 360f) % 360f,
        Pitch = Math.Clamp(Pitch - dy * 0.3f, 0f, RcpAngledView.MostPitch),
    };

    /// <summary>Closer (a positive step) or further away, held between half and four times.</summary>
    public RcpAngle Zoomed(float steps) => this with { Zoom = Math.Clamp(Zoom * MathF.Pow(1.15f, steps), 0.5f, 4f) };
}

/// <summary>
/// The angled view (Track Designer overhaul, Phase 8, 25 Oct 2026): the
/// same plan seen tipped and turned round, in perspective - the floor with
/// its joints, the rails standing up with their height and their shadows,
/// the turn rails, the markings, the racing line, and the car while Play
/// runs. Drawn here with nothing but GDI+ (no 3D engine to ship), so it is
/// a way to look and not a place to draw: the board goes back to the flat
/// plan for that.
///
/// HOW. Every point of the plan is in tiles, with height in tiles too (an
/// RCP rail stands 1 1/8 in, so 0.057 of a 50 cm tile). The camera circles
/// the plan's middle: turned round it by Yaw, tipped back by Pitch, and
/// far enough off that the perspective is gentle - a person at the end of
/// the hall, not a fish-eye. The floor and everything flat on it are drawn
/// first, as they all lie under the rails; then every face of every rail
/// and of the car is sorted far to near and filled in that order, lit from
/// one side, with only the faces turned towards the camera drawn.
/// </summary>
public static class RcpAngledView
{
    /// <summary>The most the view tips back from straight down, in degrees - past this the far end is a sliver.</summary>
    public const float MostPitch = 75f;

    /// <summary>An RCP rail's height: 1 1/8 in (shops' listings of the 50 cm kits).</summary>
    public const double RailHeightMm = 28.6;

    /// <summary>A Mini-Z's height, near enough, for the car while Play runs.</summary>
    private const double CarHeightMm = 45;

    // The painter's colours (RcpTrackPainter), so the two views agree.
    private static readonly Color Foam = ColorTranslator.FromHtml("#3A3A35");
    private static readonly Color FoamSeam = ColorTranslator.FromHtml("#1B1B19");
    private static readonly Color Infield = ColorTranslator.FromHtml("#1E1E20");
    private static readonly Color Rail = ColorTranslator.FromHtml("#DEDED8");
    private static readonly Color Turn = ColorTranslator.FromHtml("#F4557E");
    private static readonly Color Loop = ColorTranslator.FromHtml("#2D9CDB");
    private static readonly Color ChequerDark = ColorTranslator.FromHtml("#8E9094");
    private static readonly Color ChequerLight = ColorTranslator.FromHtml("#E9E9E4");
    private static readonly Color KerbRed = ColorTranslator.FromHtml("#E23B3B");
    private static readonly Color Furniture = ColorTranslator.FromHtml("#26262A");
    private static readonly Color Shadow = Color.FromArgb(80, 0, 0, 0);

    /// <summary>Where the light comes from: up, and over the top-left - as the flat plan's shadows fall.</summary>
    private static readonly (float X, float Y, float Z) Light = Normalised(-0.35f, -0.5f, 0.8f);

    /// <summary>
    /// The camera for one picture: the plan's middle, the turn and tip,
    /// and the scale and offset that fit the plan to the picture.
    /// </summary>
    public sealed class Camera
    {
        private readonly float _cx, _cy, _cos, _sin, _cosP, _sinP, _distance, _scale;
        private readonly PointF _offset;

        internal Camera(PointF centre, RcpAngle angle, float distance, float scale, PointF offset)
        {
            _cx = centre.X;
            _cy = centre.Y;
            _cos = MathF.Cos(angle.Yaw * MathF.PI / 180f);
            _sin = MathF.Sin(angle.Yaw * MathF.PI / 180f);
            _cosP = MathF.Cos(angle.Pitch * MathF.PI / 180f);
            _sinP = MathF.Sin(angle.Pitch * MathF.PI / 180f);
            _distance = distance;
            _scale = scale;
            _offset = offset;

            Position = (
                _cx + distance * _sinP * _sin,
                _cy + distance * _sinP * _cos,
                distance * _cosP);
        }

        /// <summary>Where the camera is, in tiles.</summary>
        public (float X, float Y, float Z) Position { get; }

        /// <summary>
        /// Where a point of the plan (in tiles, height up) lands in the
        /// picture, and how far from the camera it is along the view. Null
        /// for a point at or behind the camera.
        /// </summary>
        public (PointF At, float Depth)? Project(float x, float y, float z)
        {
            var qx = x - _cx;
            var qy = y - _cy;

            var x1 = qx * _cos - qy * _sin;
            var y1 = qx * _sin + qy * _cos;

            var depth = _distance - (y1 * _sinP + z * _cosP);

            if (depth < _distance * 0.05f)
            {
                return null;
            }

            var sx = x1;
            var sy = y1 * _cosP - z * _sinP;
            var perspective = _distance / depth;

            return (new PointF(_offset.X + _scale * sx * perspective, _offset.Y + _scale * sy * perspective), depth);
        }
    }

    /// <summary>
    /// The camera that shows <paramref name="design"/> whole in a picture of
    /// <paramref name="size"/> at <paramref name="angle"/>, nearer with Zoom.
    /// </summary>
    public static Camera CameraFor(RcpTrackDesign design, Size size, RcpAngle angle)
    {
        // The plan, its line and its markings - all of what is drawn.
        var bounds = design.IsEmpty && design.Route.Count == 0 ? RectangleF.Empty : design.BoundsInTiles(includeRoute: true);

        foreach (var decal in design.Decals)
        {
            var cell = new RectangleF(decal.Col, decal.Row, 1, 1);
            bounds = bounds.IsEmpty ? cell : RectangleF.Union(bounds, cell);
        }

        if (bounds.IsEmpty)
        {
            bounds = new RectangleF(0, 0, 4, 3);
        }
        bounds.Inflate(0.5f, 0.5f);

        var centre = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        var extent = Math.Max(bounds.Width, bounds.Height);
        var distance = extent * 2.2f + 2f;

        // Where the plan's corners land at a scale of one, centred on
        // nothing - then scaled and moved so they fill the picture.
        var probe = new Camera(centre, angle, distance, 1f, PointF.Empty);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        foreach (var (x, y) in new[] { (bounds.Left, bounds.Top), (bounds.Right, bounds.Top), (bounds.Left, bounds.Bottom), (bounds.Right, bounds.Bottom) })
        {
            if (probe.Project(x, y, 0) is { } p)
            {
                minX = Math.Min(minX, p.At.X);
                minY = Math.Min(minY, p.At.Y);
                maxX = Math.Max(maxX, p.At.X);
                maxY = Math.Max(maxY, p.At.Y);
            }
        }

        if (minX > maxX)
        {
            return new Camera(centre, angle, distance, 1f, new PointF(size.Width / 2f, size.Height / 2f));
        }

        var scale = Math.Min(size.Width * 0.9f / Math.Max(0.01f, maxX - minX), size.Height * 0.86f / Math.Max(0.01f, maxY - minY)) * angle.Zoom;
        var offset = new PointF(
            size.Width / 2f - scale * (minX + maxX) / 2f,
            size.Height / 2f - scale * (minY + maxY) / 2f);

        return new Camera(centre, angle, distance, scale, offset);
    }

    /// <summary>
    /// Draws <paramref name="design"/> seen at <paramref name="angle"/> into
    /// a picture of <paramref name="size"/> - over whatever is already there,
    /// so the caller fills the background. <paramref name="car"/> is the car
    /// while Play runs: where it is, in tiles, and its heading in degrees
    /// clockwise from along the rows.
    /// </summary>
    public static void Draw(Graphics g, RcpTrackDesign design, Size size, RcpAngle angle, (PointF Point, float Heading)? car = null)
    {
        // Markings and a line on their own are still something to see.
        if (design.IsEmpty && design.Decals.Count == 0 && design.Route.Count == 0)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var camera = CameraFor(design, size, angle);
        var tileMm = design.TileSizeMm > 0 ? design.TileSizeMm : 500;
        var railHeight = (float)(RailHeightMm / tileMm);

        DrawFloor(g, design, camera);
        DrawMarkings(g, design, camera);
        DrawLine(g, design, camera);

        var faces = new List<Face>();
        var shadows = new List<PointF[]>();

        foreach (var wall in design.Walls)
        {
            var (a, b) = (wall.From, wall.To);
            var offsets = wall.Double ? new[] { -0.085f, 0.085f } : new[] { 0f };

            foreach (var offset in offsets)
            {
                var (ox, oy) = wall.Vertical ? (offset, 0f) : (0f, offset);
                AddBar(faces, shadows, camera, a.X + ox, a.Y + oy, b.X + ox, b.Y + oy, RcpTrackPainter.RailThickness, railHeight, Rail);
            }
        }

        foreach (var curve in design.Curves)
        {
            var radii = curve.Double ? new[] { curve.Radius - 0.085f, curve.Radius + 0.085f } : new[] { curve.Radius };

            foreach (var radius in radii)
            {
                var points = Arc(curve, Math.Max(0.1f, radius));

                for (var i = 0; i + 1 < points.Count; i++)
                {
                    AddBar(faces, shadows, camera, points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, RcpTrackPainter.RailThickness, railHeight, Turn);
                }
            }
        }

        if (car is { } pace)
        {
            AddCar(faces, shadows, camera, pace.Point, pace.Heading, (float)(CarHeightMm / tileMm));
        }

        // The shadows first, all under everything that stands up.
        using (var shade = new SolidBrush(Shadow))
        {
            foreach (var shadow in shadows)
            {
                g.FillPolygon(shade, shadow);
            }
        }

        // Far to near: each face drawn over whatever is behind it.
        foreach (var face in faces.OrderByDescending(f => f.Depth))
        {
            using var fill = new SolidBrush(face.Colour);
            g.FillPolygon(fill, face.Points);

            using var edge = new Pen(Color.FromArgb(70, 0, 0, 0), 0.6f);
            g.DrawPolygon(edge, face.Points);
        }
    }

    /// <summary>A face of something standing up, as it lands in the picture: its corners, its colour lit, and how far it is.</summary>
    private sealed record Face(PointF[] Points, Color Colour, float Depth);

    /// <summary>
    /// The turn rail's arc in tiles, as the painter draws it
    /// (RcpTrackPainter.DrawCurve): centred one radius into its quarter, a
    /// quarter circle from one grid line to the other.
    /// </summary>
    internal static List<PointF> Arc(RcpCurve curve, float radius)
    {
        var (dx, dy) = RcpAutoRails.Step(curve.Quadrant);
        var centre = new PointF(curve.Col + dx * curve.Radius, curve.Row + dy * curve.Radius);

        var start = curve.Quadrant switch
        {
            RcpQuadrant.NorthEast => 90f,
            RcpQuadrant.SouthEast => 180f,
            RcpQuadrant.SouthWest => 270f,
            _ => 0f,
        };

        var steps = Math.Max(4, (int)MathF.Ceiling(radius * 8));
        var points = new List<PointF>(steps + 1);

        for (var i = 0; i <= steps; i++)
        {
            var a = (start + 90f * i / steps) * MathF.PI / 180f;
            points.Add(new PointF(centre.X + radius * MathF.Cos(a), centre.Y + radius * MathF.Sin(a)));
        }

        return points;
    }

    private static void DrawFloor(Graphics g, RcpTrackDesign design, Camera camera)
    {
        using var foam = new SolidBrush(Foam);
        using var infield = new SolidBrush(Infield);
        using var seam = new Pen(FoamSeam, 1f);

        foreach (var tile in design.Tiles)
        {
            var square = Square(camera, tile.Col, tile.Row, 0);

            if (square is null)
            {
                continue;
            }

            if (tile.Kind == RcpTileKind.Infield)
            {
                g.FillPolygon(infield, square);
                continue;
            }

            // The teeth only where a tile is big enough on screen to show
            // them; further off, a plain square with its seam.
            var big = Distance(square[0], square[2]) > 60f;
            var outline = big ? Projected(camera, RcpJoints.Outline(tile.Col, tile.Row), 0) : null;

            g.FillPolygon(foam, outline ?? square);
            g.DrawPolygon(seam, outline ?? square);
        }
    }

    private static void DrawMarkings(Graphics g, RcpTrackDesign design, Camera camera)
    {
        const float lift = 0.002f;

        foreach (var decal in design.Decals)
        {
            // In the tile's own terms, 0 to 1 each way, turned about its middle
            // as the painter turns it.
            PointF[]? Rect(float u, float v, float w, float h) =>
                Projected(camera, new[]
                {
                    Local(decal, u, v), Local(decal, u + w, v), Local(decal, u + w, v + h), Local(decal, u, v + h),
                }, lift);

            void Chequer(float u, float v, float w, float h, int columns, int rows)
            {
                using var light = new SolidBrush(ChequerLight);
                using var dark = new SolidBrush(ChequerDark);

                for (var c = 0; c < columns; c++)
                {
                    for (var r = 0; r < rows; r++)
                    {
                        if (Rect(u + w * c / columns, v + h * r / rows, w / columns, h / rows) is { } cell)
                        {
                            g.FillPolygon((c + r) % 2 == 0 ? light : dark, cell);
                        }
                    }
                }
            }

            switch (decal.Kind)
            {
                case RcpDecalKind.FinishLine:
                    Chequer(0, 0.36f, 1, 0.28f, 8, 2);
                    break;

                case RcpDecalKind.GridBox:
                    Chequer(0.18f, 0.3f, 0.3f, 0.4f, 2, 4);
                    break;

                case RcpDecalKind.SplitLoop1:
                case RcpDecalKind.SplitLoop2:
                case RcpDecalKind.SplitLoop3:
                    if (Rect(0, 0.38f, 1, 0.24f) is { } band)
                    {
                        using var blue = new SolidBrush(Loop);
                        g.FillPolygon(blue, band);
                    }

                    break;

                case RcpDecalKind.Kerb:
                    if (Rect(0.28f, 0.45f, 0.44f, 0.12f) is { } kerb)
                    {
                        using var red = new SolidBrush(KerbRed);
                        g.FillPolygon(red, kerb);
                    }

                    break;

                case RcpDecalKind.Post:
                    // On the tile's top-left corner, turned to the corner it is on.
                    var corner = Local(decal, 0, 0);
                    var disc = Projected(camera, Enumerable.Range(0, 16).Select(i =>
                        new PointF(corner.X + 0.17f * MathF.Cos(i * MathF.PI / 8), corner.Y + 0.17f * MathF.Sin(i * MathF.PI / 8))).ToArray(), lift);

                    if (disc is not null)
                    {
                        using var pink = new SolidBrush(Turn);
                        g.FillPolygon(pink, disc);
                    }

                    break;

                case RcpDecalKind.Furniture:
                    if (Rect(0.18f, 0.22f, 0.64f, 0.56f) is { } table)
                    {
                        using var dark = new SolidBrush(Furniture);
                        g.FillPolygon(dark, table);
                    }

                    break;
            }
        }
    }

    private static void DrawLine(Graphics g, RcpTrackDesign design, Camera camera)
    {
        if (design.Route.Count < 2)
        {
            return;
        }

        var points = design.Route.Select(p => new PointF(p.X, p.Y)).ToList();

        if (design.RouteClosed)
        {
            points.Add(points[0]);
        }

        using var pen = new Pen(Color.FromArgb(170, 45, 120, 230), 3f) { LineJoin = LineJoin.Round, DashStyle = DashStyle.Dash };

        for (var i = 0; i + 1 < points.Count; i++)
        {
            if (camera.Project(points[i].X, points[i].Y, 0.003f) is { } a && camera.Project(points[i + 1].X, points[i + 1].Y, 0.003f) is { } b)
            {
                g.DrawLine(pen, a.At, b.At);
            }
        }
    }

    /// <summary>
    /// A bar from (x1,y1) to (x2,y2) - a run of rail, or one step of a turn
    /// rail's arc - <paramref name="thickness"/> wide and
    /// <paramref name="height"/> tall: its faces towards the camera, and its
    /// shadow on the floor.
    /// </summary>
    private static void AddBar(List<Face> faces, List<PointF[]> shadows, Camera camera,
        float x1, float y1, float x2, float y2, float thickness, float height, Color colour)
    {
        var length = MathF.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

        if (length < 1e-4f)
        {
            return;
        }

        // Across the bar, half its thickness each side.
        var nx = -(y2 - y1) / length * thickness / 2f;
        var ny = (x2 - x1) / length * thickness / 2f;

        var foot = new[]
        {
            new PointF(x1 + nx, y1 + ny), new PointF(x2 + nx, y2 + ny),
            new PointF(x2 - nx, y2 - ny), new PointF(x1 - nx, y1 - ny),
        };

        AddBlock(faces, shadows, camera, foot, height, colour);
    }

    /// <summary>The car while Play runs: a small block, its nose a lighter face.</summary>
    private static void AddCar(List<Face> faces, List<PointF[]> shadows, Camera camera, PointF at, float heading, float height)
    {
        var a = heading * MathF.PI / 180f;
        var (cos, sin) = (MathF.Cos(a), MathF.Sin(a));
        const float length = 0.34f;
        const float width = 0.2f;

        PointF Corner(float along, float across) =>
            new(at.X + along * cos - across * sin, at.Y + along * sin + across * cos);

        var foot = new[]
        {
            Corner(-length / 2, -width / 2), Corner(length / 2, -width / 2),
            Corner(length / 2, width / 2), Corner(-length / 2, width / 2),
        };

        AddBlock(faces, shadows, camera, foot, height, AppTheme.Primary);
    }

    /// <summary>
    /// A block standing on <paramref name="foot"/> (four corners, in tiles,
    /// going round) up to <paramref name="height"/>: its top and its sides
    /// facing the camera, lit, and its shadow.
    /// </summary>
    private static void AddBlock(List<Face> faces, List<PointF[]> shadows, Camera camera, PointF[] foot, float height, Color colour)
    {
        // The shadow: the foot, pushed away from the light by the height.
        var push = height / Math.Max(0.2f, Light.Z);
        var shadow = Projected(camera, foot.Select(p => new PointF(p.X - Light.X * push, p.Y - Light.Y * push)).ToArray(), 0.001f);

        if (shadow is not null)
        {
            // The footprint and the pushed copy together: their hull, near
            // enough, is the two drawn as one.
            var under = Projected(camera, foot, 0.001f);

            if (under is not null)
            {
                shadows.Add(Hull(under.Concat(shadow).ToArray()));
            }
        }

        var cam = camera.Position;

        void Add(IReadOnlyList<(float X, float Y, float Z)> corners, (float X, float Y, float Z) normal)
        {
            var cx = corners.Average(c => c.X);
            var cy = corners.Average(c => c.Y);
            var cz = corners.Average(c => c.Z);

            // Only what is turned towards the camera.
            if (normal.X * (cam.X - cx) + normal.Y * (cam.Y - cy) + normal.Z * (cam.Z - cz) <= 0)
            {
                return;
            }

            var points = new PointF[corners.Count];
            var depth = 0f;

            for (var i = 0; i < corners.Count; i++)
            {
                if (camera.Project(corners[i].X, corners[i].Y, corners[i].Z) is not { } p)
                {
                    return;
                }

                points[i] = p.At;
                depth += p.Depth;
            }

            var lit = 0.55f + 0.45f * Math.Max(0f, normal.X * Light.X + normal.Y * Light.Y + normal.Z * Light.Z);

            faces.Add(new Face(points, Shade(colour, lit), depth / corners.Count));
        }

        // The top.
        Add(foot.Select(p => (p.X, p.Y, height)).ToArray(), (0f, 0f, 1f));

        // The sides: each edge of the foot, its normal pointing out of the block.
        var mx = foot.Average(p => p.X);
        var my = foot.Average(p => p.Y);

        for (var i = 0; i < foot.Length; i++)
        {
            var a = foot[i];
            var b = foot[(i + 1) % foot.Length];

            var (ex, ey) = (b.X - a.X, b.Y - a.Y);
            var length = MathF.Sqrt(ex * ex + ey * ey);

            if (length < 1e-5f)
            {
                continue;
            }

            var (nx, ny) = (ey / length, -ex / length);

            // Pointing out of the block, whichever way round the foot goes.
            if (nx * ((a.X + b.X) / 2 - mx) + ny * ((a.Y + b.Y) / 2 - my) < 0)
            {
                (nx, ny) = (-nx, -ny);
            }

            Add(new[] { (a.X, a.Y, 0f), (b.X, b.Y, 0f), (b.X, b.Y, height), (a.X, a.Y, height) }, (nx, ny, 0f));
        }
    }

    /// <summary>A point of a marking in the plan's tiles: (u, v) across its tile, turned about the tile's middle as the painter turns it.</summary>
    private static PointF Local(RcpDecal decal, float u, float v)
    {
        var (x, y) = (u - 0.5f, v - 0.5f);

        for (var i = 0; i < ((decal.Turns % 4) + 4) % 4; i++)
        {
            // A quarter clockwise, with rows running down.
            (x, y) = (-y, x);
        }

        return new PointF(decal.Col + 0.5f + x, decal.Row + 0.5f + y);
    }

    private static PointF[]? Square(Camera camera, float col, float row, float z) =>
        Projected(camera, new[] { new PointF(col, row), new PointF(col + 1, row), new PointF(col + 1, row + 1), new PointF(col, row + 1) }, z);

    /// <summary>The points projected, or null when any of them is behind the camera.</summary>
    private static PointF[]? Projected(Camera camera, IReadOnlyList<PointF> points, float z)
    {
        var result = new PointF[points.Count];

        for (var i = 0; i < points.Count; i++)
        {
            if (camera.Project(points[i].X, points[i].Y, z) is not { } p)
            {
                return null;
            }

            result[i] = p.At;
        }

        return result;
    }

    /// <summary>The convex hull of a handful of points, going round (Andrew's monotone chain).</summary>
    internal static PointF[] Hull(PointF[] points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();

        if (sorted.Length < 3)
        {
            return sorted;
        }

        static float Cross(PointF o, PointF a, PointF b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var hull = new PointF[sorted.Length * 2];
        var k = 0;

        foreach (var p in sorted)
        {
            while (k >= 2 && Cross(hull[k - 2], hull[k - 1], p) <= 0)
            {
                k--;
            }

            hull[k++] = p;
        }

        for (int i = sorted.Length - 2, lower = k + 1; i >= 0; i--)
        {
            while (k >= lower && Cross(hull[k - 2], hull[k - 1], sorted[i]) <= 0)
            {
                k--;
            }

            hull[k++] = sorted[i];
        }

        return hull.Take(k - 1).ToArray();
    }

    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Color Shade(Color colour, float by) => Color.FromArgb(
        colour.A,
        (int)Math.Clamp(colour.R * by, 0, 255),
        (int)Math.Clamp(colour.G * by, 0, 255),
        (int)Math.Clamp(colour.B * by, 0, 255));

    private static (float X, float Y, float Z) Normalised(float x, float y, float z)
    {
        var length = MathF.Sqrt(x * x + y * y + z * z);

        return (x / length, y / length, z / length);
    }
}
