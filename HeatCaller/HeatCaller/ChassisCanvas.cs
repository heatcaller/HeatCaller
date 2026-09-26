namespace HeatCaller;

/// <summary>
/// Draws a chassis diagram with labelled callout points and leader lines,
/// and handles picking and dragging them.
///
/// Used by both the template editor (where points are created and moved)
/// and the setup entry form (where their values are filled in), so the
/// diagram looks identical in both places rather than being drawn twice.
/// </summary>
public sealed class ChassisCanvas : Control
{
    private enum DragKind { None, Anchor, Label }

    private ChassisTemplate? _template;
    private Image? _diagram;
    private string? _loadedImagePath;

    private DragKind _dragKind = DragKind.None;
    private string? _dragPointId;

    /// <summary>Values keyed by point id. The editor leaves this empty; the entry form fills it in.</summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When true, points can be dragged and are drawn with handles. The entry form sets this false.</summary>
    public bool EditMode { get; set; }

    public string? SelectedPointId { get; private set; }

    public event Action? SelectionChanged;
    public event Action? PointMoved;

    /// <summary>Raised when a point is double-clicked - the entry form uses this to prompt for that point's value.</summary>
    public event Action<string>? PointActivated;

    public ChassisCanvas()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        TabStop = false;
    }

    public void SetTemplate(ChassisTemplate? template, string? imagePath)
    {
        _template = template;
        SelectedPointId = null;

        if (imagePath != _loadedImagePath)
        {
            _diagram?.Dispose();
            _diagram = null;
            _loadedImagePath = imagePath;

            if (imagePath is not null)
            {
                try
                {
                    // Sized from its header before anything is decoded, and
                    // no bigger than Set Diagram Image... takes in gets
                    // through (see ImageProbe - a diagram is kept just as it
                    // was picked). This used to decode whatever the file
                    // held as the chassis editor and the setup form opened,
                    // so a diagram planted in a restored backup, too big to
                    // decode, froze or crashed them there every time. It
                    // gets the built-in outline instead, as an unreadable
                    // one always has.
                    //
                    // Read via a stream so the file isn't left locked - the
                    // template editor may want to replace it.
                    // Copied into a Bitmap before the stream closes: an
                    // Image from FromStream keeps reading its stream for as
                    // long as it lives.
                    if (ImageProbe.ReadStored(imagePath) is { } bytes)
                    {
                        using var stream = new MemoryStream(bytes);
                        using var decoded = Image.FromStream(stream);
                        _diagram = new Bitmap(decoded);
                    }
                }
                catch
                {
                    _diagram = null;
                }
            }
        }

        Invalidate();
    }

    // Proportions taken from Kyosho's published MR-03EVO chassis-set data
    // (rc.kyosho.com): length 123.5mm, width 70-80mm, wheelbase 98.0mm,
    // tread 61.5mm, tyres 25mm diameter by 8.5mm front / up to 11mm rear.
    // Width here is tread + tyre width, since the wheels are what set the
    // overall figure. Everything in the drawing derives from these, so the
    // callout positions correspond to where parts actually sit.
    private const float ChassisLengthMm = 123.5f;
    private const float ChassisWidthMm = 72.0f;
    private const float WheelbaseMm = 98.0f;
    private const float TreadMm = 61.5f;
    private const float TyreDiaMm = 25.0f;
    private const float TyreWidthFrontMm = 8.5f;
    private const float TyreWidthRearMm = 11.0f;

    private const float ChassisAspect = ChassisWidthMm / ChassisLengthMm;

    // Derived once rather than sprinkled as magic numbers.
    private const float FrontOverhangMm = (ChassisLengthMm - WheelbaseMm) / 2f - 1f;
    private const float FrontAxleY = FrontOverhangMm / ChassisLengthMm;
    private const float RearAxleY = (FrontOverhangMm + WheelbaseMm) / ChassisLengthMm;
    private const float WheelCentreOffsetX = (TreadMm / 2f) / ChassisWidthMm;
    private const float TyreLengthFrac = TyreDiaMm / ChassisLengthMm;
    private const float TyreWidthFrontFrac = TyreWidthFrontMm / ChassisWidthMm;
    private const float TyreWidthRearFrac = TyreWidthRearMm / ChassisWidthMm;

    /// <summary>
    /// The area the diagram occupies. Labels need room either side, and
    /// whatever is left is fitted to the correct aspect ratio rather than
    /// stretched - so callout positions stay meaningful and a user-supplied
    /// photo isn't distorted.
    /// </summary>
    private Rectangle DiagramBounds
    {
        get
        {
            var marginX = (int)(Width * 0.24f);
            const int marginY = 16;

            var availW = Math.Max(20, Width - marginX * 2);
            var availH = Math.Max(20, Height - marginY * 2);

            // Match the image's own shape when there is one, so photos and
            // drawings both come out undistorted.
            var aspect = _diagram is not null && _diagram.Height > 0
                ? _diagram.Width / (float)_diagram.Height
                : ChassisAspect;

            var w = availW;
            var h = (int)(w / aspect);
            if (h > availH)
            {
                h = availH;
                w = (int)(h * aspect);
            }

            return new Rectangle(
                (Width - w) / 2,
                marginY + (availH - h) / 2,
                Math.Max(20, w),
                Math.Max(20, h));
        }
    }

    private PointF AnchorPixel(SetupField p)
    {
        var b = DiagramBounds;
        return new PointF(b.Left + b.Width * p.X, b.Top + b.Height * p.Y);
    }

    private PointF LabelPixel(SetupField p)
    {
        var b = DiagramBounds;
        return new PointF(
            b.Left + b.Width * (p.X + p.LabelOffsetX),
            b.Top + b.Height * (p.Y + p.LabelOffsetY));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        var bounds = DiagramBounds;

        if (_diagram is not null)
        {
            g.DrawImage(_diagram, bounds);
        }
        else
        {
            DrawGenericChassis(g, bounds);
        }

        if (_template is null)
        {
            return;
        }

        foreach (var point in _template.DiagramFields)
        {
            DrawCallout(g, point);
        }

        if (EditMode)
        {
            using var hintBrush = new SolidBrush(AppTheme.TextMuted);
            g.DrawString("Drag a dot to move it. Drag its label to reposition the text.",
                AppTheme.SmallFont, hintBrush, 6, Height - 18);
        }
    }

    /// <summary>
    /// A simple top-down chassis outline, drawn when no image has been
    /// supplied. Deliberately generic - it's a placeholder so the callouts
    /// have something to sit on, not an attempt at any real chassis.
    /// </summary>
    /// <summary>
    /// A top-down schematic of a generic 1/28 rear-motor chassis, drawn
    /// when no photo has been supplied.
    ///
    /// Proportioned and laid out from the real thing - front wheels
    /// narrower than the rears, battery rails down the sides, motor pod and
    /// spur gear at the back, T-bar and rear damper between pod and plate.
    /// It's a generic reference rather than any specific chassis, but close
    /// enough that callouts land where the part actually is.
    ///
    /// All positions are fractions of the bounds, so it scales cleanly.
    /// </summary>
    /// <summary>
    /// Top-down schematic of a generic MR-03-style rear-motor chassis,
    /// drawn when no photo has been supplied.
    ///
    /// Every position derives from the published measurements above, so the
    /// axles, tread and tyre sizes are to scale relative to each other
    /// rather than eyeballed. It's a generic reference, not any one chassis
    /// variant, but close enough that a callout lands on the right part.
    /// </summary>
    /// <summary>
    /// Top-down schematic of a 98mm rear-motor chassis, drawn when no photo
    /// has been supplied.
    ///
    /// Laid out from an overhead photo of a real MR-03 RWD rather than
    /// guesswork. Three things earlier versions got badly wrong:
    ///  - the deck is broad, roughly two-thirds of the overall width, not a
    ///    narrow spine;
    ///  - the front knuckles sit right out at the deck edges beside the
    ///    wheels, with the axle stubs reaching outward to them;
    ///  - the spur gear is offset to one side of the rear axle, not centred.
    ///
    /// Axle positions and tyre sizes still come from Kyosho's published
    /// figures; the plate widths are measured off the photo, since Kyosho
    /// only publishes overall dimensions.
    /// </summary>
    private static void DrawGenericChassis(Graphics g, Rectangle b)
    {
        float FX(float f) => b.Left + b.Width * f;
        float FY(float f) => b.Top + b.Height * f;
        RectangleF Rect(float x0, float y0, float x1, float y1) =>
            new(FX(x0), FY(y0), b.Width * (x1 - x0), b.Height * (y1 - y0));

        var line = AppTheme.TextMuted;
        using var pen = new Pen(line, 1.5f);
        using var thinPen = new Pen(Color.FromArgb(160, line), 1f);
        using var hairline = new Pen(Color.FromArgb(100, line), 1f);
        using var fill = new SolidBrush(Color.FromArgb(32, line));
        using var midFill = new SolidBrush(Color.FromArgb(62, line));
        using var darkFill = new SolidBrush(Color.FromArgb(100, line));

        // Deck half-width, measured from the photo: the plastic is broad,
        // reaching almost out to the wheels.
        const float deck = 0.33f;

        // --- Main deck: a broad slab, slightly narrower at the nose ---
        var body = new[]
        {
            new PointF(FX(0.5f - deck * 0.78f), FY(0.045f)),
            new PointF(FX(0.5f + deck * 0.78f), FY(0.045f)),
            new PointF(FX(0.5f + deck), FY(0.135f)),
            new PointF(FX(0.5f + deck), FY(0.62f)),
            new PointF(FX(0.5f + deck * 0.94f), FY(0.70f)),
            new PointF(FX(0.5f + deck * 0.94f), FY(0.955f)),
            new PointF(FX(0.5f - deck * 0.94f), FY(0.955f)),
            new PointF(FX(0.5f - deck * 0.94f), FY(0.70f)),
            new PointF(FX(0.5f - deck), FY(0.62f)),
            new PointF(FX(0.5f - deck), FY(0.135f)),
        };
        g.FillPolygon(fill, body);
        g.DrawPolygon(pen, body);

        // --- Front end ---
        // Upper plate across the nose, with its two mounting screws.
        var upperPlate = Rect(0.5f - deck * 0.80f, 0.048f, 0.5f + deck * 0.80f, 0.125f);
        g.FillRectangle(midFill, upperPlate);
        g.DrawRectangle(pen, upperPlate.X, upperPlate.Y, upperPlate.Width, upperPlate.Height);
        foreach (var sx in new[] { -0.45f, 0.45f })
        {
            var screw = Rect(0.5f + deck * sx - 0.016f, 0.072f, 0.5f + deck * sx + 0.016f, 0.100f);
            g.DrawEllipse(thinPen, screw.X, screw.Y, screw.Width, screw.Height);
        }

        // Knuckles at the deck edges, with the king pin ball on top and the
        // axle stub reaching out to the wheel.
        foreach (var side in new[] { -1f, 1f })
        {
            var knuckleX = 0.5f + side * deck;
            var wheelX = 0.5f + side * WheelCentreOffsetX;

            var knuckle = Rect(
                Math.Min(knuckleX - side * 0.035f, knuckleX + side * 0.035f), FrontAxleY - 0.045f,
                Math.Max(knuckleX - side * 0.035f, knuckleX + side * 0.035f), FrontAxleY + 0.045f);
            g.FillRectangle(darkFill, knuckle);
            g.DrawRectangle(pen, knuckle.X, knuckle.Y, knuckle.Width, knuckle.Height);

            // King pin ball, sitting proud at the top of the knuckle.
            var ball = Rect(knuckleX - 0.026f, FrontAxleY - 0.060f, knuckleX + 0.026f, FrontAxleY - 0.024f);
            g.FillEllipse(midFill, ball);
            g.DrawEllipse(pen, ball.X, ball.Y, ball.Width, ball.Height);

            // Axle stub out to the wheel.
            g.DrawLine(pen, FX(knuckleX + side * 0.03f), FY(FrontAxleY), FX(wheelX), FY(FrontAxleY));
            g.DrawLine(thinPen, FX(wheelX), FY(FrontAxleY - 0.012f), FX(wheelX), FY(FrontAxleY + 0.012f));

            // Lower arm running inboard.
            g.DrawLine(thinPen, FX(knuckleX), FY(FrontAxleY + 0.045f), FX(0.5f + side * deck * 0.45f), FY(0.185f));
        }

        // Tie rod behind the knuckles.
        g.DrawLine(pen, FX(0.5f - deck * 0.92f), FY(FrontAxleY + 0.055f), FX(0.5f + deck * 0.92f), FY(FrontAxleY + 0.055f));

        // --- Battery / servo bay and electronics down the middle ---
        var bay = Rect(0.5f - deck * 0.62f, 0.20f, 0.5f + deck * 0.62f, 0.40f);
        g.FillRectangle(midFill, bay);
        g.DrawRectangle(thinPen, bay.X, bay.Y, bay.Width, bay.Height);

        // Receiver board, sitting on the deck behind the bay.
        var board = Rect(0.5f - deck * 0.50f, 0.415f, 0.5f + deck * 0.50f, 0.545f);
        g.FillRectangle(fill, board);
        g.DrawRectangle(pen, board.X, board.Y, board.Width, board.Height);
        for (var k = 1; k <= 2; k++)
        {
            var y = board.Y + board.Height * k / 3f;
            g.DrawLine(hairline, board.X + 2, y, board.Right - 2, y);
        }

        // Battery contact strips down each side.
        foreach (var side in new[] { -1f, 1f })
        {
            var x = 0.5f + side * deck * 0.86f;
            g.DrawLine(pen, FX(x), FY(0.22f), FX(x), FY(0.56f));
        }

        // --- Rear: damper and spring run lengthwise down the centre ---
        var damperTop = 0.555f;
        var damperBottom = 0.735f;
        var springRect = Rect(0.5f - 0.048f, damperTop, 0.5f + 0.048f, damperBottom);
        g.FillRectangle(fill, springRect);
        g.DrawRectangle(pen, springRect.X, springRect.Y, springRect.Width, springRect.Height);

        // Coil turns across the spring.
        const int coils = 9;
        for (var k = 0; k < coils; k++)
        {
            var y = springRect.Y + springRect.Height * (k + 0.5f) / coils;
            g.DrawLine(thinPen, springRect.X, y, springRect.Right, y);
        }

        // T-bar either side of the damper, linking deck to pod.
        foreach (var side in new[] { -1f, 1f })
        {
            var inner = 0.5f + side * 0.06f;
            var outer = 0.5f + side * deck * 0.80f;
            var arm = Rect(Math.Min(inner, outer), 0.60f, Math.Max(inner, outer), 0.655f);
            g.FillRectangle(midFill, arm);
            g.DrawRectangle(thinPen, arm.X, arm.Y, arm.Width, arm.Height);
        }

        // --- Motor pod ---
        var pod = Rect(0.5f - deck * 0.90f, 0.735f, 0.5f + deck * 0.90f, 0.945f);
        g.FillRectangle(fill, pod);
        g.DrawRectangle(pen, pod.X, pod.Y, pod.Width, pod.Height);

        // Motor can, offset from centre as the spur takes the other side.
        var motor = Rect(0.5f - deck * 0.72f, 0.775f, 0.5f + deck * 0.18f, 0.925f);
        g.FillRectangle(midFill, motor);
        g.DrawRectangle(pen, motor.X, motor.Y, motor.Width, motor.Height);

        // Spur gear, offset to one side of the rear axle - not centred.
        var spurCx = FX(0.5f + deck * 0.58f);
        var spurCy = FY(RearAxleY - 0.035f);
        var spurR = b.Height * 0.055f;
        g.FillEllipse(darkFill, spurCx - spurR, spurCy - spurR, spurR * 2, spurR * 2);
        g.DrawEllipse(pen, spurCx - spurR, spurCy - spurR, spurR * 2, spurR * 2);
        for (var k = 0; k < 16; k++)
        {
            var a = k * Math.PI * 2.0 / 16.0;
            g.DrawLine(hairline,
                (float)(spurCx + Math.Cos(a) * spurR * 0.80f), (float)(spurCy + Math.Sin(a) * spurR * 0.80f),
                (float)(spurCx + Math.Cos(a) * spurR), (float)(spurCy + Math.Sin(a) * spurR));
        }

        // --- Rear axle and its outboard stubs ---
        g.DrawLine(pen, FX(0.5f - deck * 0.94f), FY(RearAxleY), FX(0.5f + deck * 0.94f), FY(RearAxleY));
        foreach (var side in new[] { -1f, 1f })
        {
            var from = 0.5f + side * deck * 0.94f;
            var to = 0.5f + side * WheelCentreOffsetX;
            g.DrawLine(pen, FX(from), FY(RearAxleY), FX(to), FY(RearAxleY));

            // Wheel hub / nut on the end.
            var hub = Rect(Math.Min(to - 0.022f, to + 0.022f), RearAxleY - 0.018f,
                Math.Max(to - 0.022f, to + 0.022f), RearAxleY + 0.018f);
            g.FillEllipse(midFill, hub);
            g.DrawEllipse(thinPen, hub.X, hub.Y, hub.Width, hub.Height);
        }

        // --- Wheels last, so they sit over the axle ends ---
        void Wheel(float cx, float cy, float widthFrac)
        {
            var r = new RectangleF(
                FX(cx - widthFrac / 2f), FY(cy - TyreLengthFrac / 2f),
                b.Width * widthFrac, b.Height * TyreLengthFrac);

            g.FillRectangle(darkFill, r);
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

            var rim = RectangleF.Inflate(r, -r.Width * 0.24f, -r.Height * 0.30f);
            g.FillEllipse(midFill, rim);
            g.DrawEllipse(hairline, rim.X, rim.Y, rim.Width, rim.Height);
        }

        Wheel(0.5f - WheelCentreOffsetX, FrontAxleY, TyreWidthFrontFrac);
        Wheel(0.5f + WheelCentreOffsetX, FrontAxleY, TyreWidthFrontFrac);
        Wheel(0.5f - WheelCentreOffsetX, RearAxleY, TyreWidthRearFrac);
        Wheel(0.5f + WheelCentreOffsetX, RearAxleY, TyreWidthRearFrac);

        // --- Wheelbase dimension, the figure people actually quote ---
        using var labelBrush = new SolidBrush(Color.FromArgb(175, line));
        var dimX = FX(0.5f + deck + 0.055f);
        g.DrawLine(hairline, dimX, FY(FrontAxleY), dimX, FY(RearAxleY));
        g.DrawLine(hairline, dimX - 3, FY(FrontAxleY), dimX + 3, FY(FrontAxleY));
        g.DrawLine(hairline, dimX - 3, FY(RearAxleY), dimX + 3, FY(RearAxleY));
        g.DrawString($"{WheelbaseMm:0}mm", AppTheme.SmallFont, labelBrush, dimX + 2, FY((FrontAxleY + RearAxleY) / 2f) - 8);

        var f = TextRenderer.MeasureText("FRONT", AppTheme.SmallFont);
        g.DrawString("FRONT", AppTheme.SmallFont, labelBrush, FX(0.5f) - f.Width / 2f, b.Top - 14);
    }

    private void DrawCallout(Graphics g, SetupField point)
    {
        var anchor = AnchorPixel(point);
        var labelPos = LabelPixel(point);
        var selected = point.Id == SelectedPointId;

        var hasValue = Values.TryGetValue(point.Id, out var value) && !string.IsNullOrWhiteSpace(value);
        var text = hasValue ? $"{point.Label}: {value}" : point.Label;

        var size = TextRenderer.MeasureText(text, AppTheme.SmallFont);
        var box = new Rectangle((int)labelPos.X - size.Width / 2, (int)labelPos.Y - size.Height / 2 - 2,
            size.Width + 10, size.Height + 6);

        // Keep the label inside the control rather than clipped off an edge.
        box.X = Math.Clamp(box.X, 2, Math.Max(2, Width - box.Width - 2));
        box.Y = Math.Clamp(box.Y, 2, Math.Max(2, Height - box.Height - 2));

        // Leader line from the anchor to the nearest edge of the label box.
        var target = new PointF(
            Math.Clamp(anchor.X, box.Left, box.Right),
            Math.Clamp(anchor.Y, box.Top, box.Bottom));

        using (var linePen = new Pen(selected ? AppTheme.Primary : AppTheme.Border, selected ? 1.8f : 1.2f))
        {
            g.DrawLine(linePen, anchor, target);
        }

        // Label chip. Filled when a value is present, outlined when empty -
        // so an unfilled point is obvious at a glance.
        using (var boxBrush = new SolidBrush(hasValue ? AppTheme.PrimaryTint : AppTheme.Surface))
        {
            g.FillRectangle(boxBrush, box);
        }
        using (var boxPen = new Pen(selected ? AppTheme.Primary : AppTheme.Border, selected ? 1.8f : 1f))
        {
            g.DrawRectangle(boxPen, box);
        }

        TextRenderer.DrawText(g, text, AppTheme.SmallFont, box,
            hasValue ? AppTheme.TextDark : AppTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // Anchor dot.
        var dotSize = selected ? 9f : 7f;
        using var dotBrush = new SolidBrush(selected ? AppTheme.Primary : (hasValue ? AppTheme.Success : AppTheme.TextMuted));
        g.FillEllipse(dotBrush, anchor.X - dotSize / 2, anchor.Y - dotSize / 2, dotSize, dotSize);

        using var dotRing = new Pen(AppTheme.Surface, 1.5f);
        g.DrawEllipse(dotRing, anchor.X - dotSize / 2, anchor.Y - dotSize / 2, dotSize, dotSize);
    }

    private string? HitTestAnchor(Point location)
    {
        if (_template is null)
        {
            return null;
        }

        foreach (var p in _template.DiagramFields)
        {
            var a = AnchorPixel(p);
            if (Math.Abs(a.X - location.X) <= 8 && Math.Abs(a.Y - location.Y) <= 8)
            {
                return p.Id;
            }
        }
        return null;
    }

    private string? HitTestLabel(Point location)
    {
        if (_template is null)
        {
            return null;
        }

        foreach (var p in _template.DiagramFields)
        {
            var l = LabelPixel(p);
            var text = Values.TryGetValue(p.Id, out var v) && !string.IsNullOrWhiteSpace(v) ? $"{p.Label}: {v}" : p.Label;
            var size = TextRenderer.MeasureText(text, AppTheme.SmallFont);

            var box = new Rectangle((int)l.X - size.Width / 2, (int)l.Y - size.Height / 2 - 2, size.Width + 10, size.Height + 6);
            if (box.Contains(location))
            {
                return p.Id;
            }
        }
        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        var anchorHit = HitTestAnchor(e.Location);
        var labelHit = anchorHit is null ? HitTestLabel(e.Location) : null;
        var hit = anchorHit ?? labelHit;

        if (hit != SelectedPointId)
        {
            SelectedPointId = hit;
            Invalidate();
            SelectionChanged?.Invoke();
        }

        if (EditMode && hit is not null)
        {
            _dragKind = anchorHit is not null ? DragKind.Anchor : DragKind.Label;
            _dragPointId = hit;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragKind == DragKind.None || _dragPointId is null || _template is null)
        {
            Cursor = EditMode && (HitTestAnchor(e.Location) ?? HitTestLabel(e.Location)) is not null
                ? Cursors.SizeAll
                : Cursors.Default;
            return;
        }

        var point = _template.DiagramFields.FirstOrDefault(p => p.Id == _dragPointId);
        if (point is null)
        {
            return;
        }

        var b = DiagramBounds;
        var nx = (e.X - b.Left) / (float)Math.Max(1, b.Width);
        var ny = (e.Y - b.Top) / (float)Math.Max(1, b.Height);

        if (_dragKind == DragKind.Anchor)
        {
            // Clamped so a point can't be dragged off the diagram entirely.
            point.X = Math.Clamp(nx, 0f, 1f);
            point.Y = Math.Clamp(ny, 0f, 1f);
        }
        else
        {
            point.LabelOffsetX = Math.Clamp(nx - point.X, -1f, 1f);
            point.LabelOffsetY = Math.Clamp(ny - point.Y, -1f, 1f);
        }

        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_dragKind != DragKind.None)
        {
            _dragKind = DragKind.None;
            _dragPointId = null;
            PointMoved?.Invoke();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        var hit = HitTestAnchor(e.Location) ?? HitTestLabel(e.Location);
        if (hit is not null)
        {
            PointActivated?.Invoke(hit);
            return;
        }

        // Adding a field needs a section to put it in, which the canvas
        // doesn't know about - the template editor owns that.
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _diagram?.Dispose();
            _diagram = null;
        }
        base.Dispose(disposing);
    }
}
