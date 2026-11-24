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

    // WHY (23 Nov 2026, bug hunt 24): how far from the label's kept middle
    // the press landed, so a drag carries the label by the spot that was
    // grabbed. Without it the label's middle was put under the mouse on the
    // first move, so a long label grabbed by its end leapt half its width.
    private PointF _dragGrab;

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

            // Room above for "FRONT", and below for the hint in the editor,
            // in the small face at the text size in use. A fixed 16px either
            // side cut the hint off at 200% (layout pictures, 30 Sep).
            var marginTop = Math.Max(16, Pieces.LineHeight(AppTheme.SmallFont) + 2);
            var marginBottom = Math.Max(16, HintRoom);

            var availW = Math.Max(20, Width - marginX * 2);
            var availH = Math.Max(20, Height - marginTop - marginBottom);

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
                marginTop + (availH - h) / 2,
                Math.Max(20, w),
                Math.Max(20, h));
        }
    }

    /// <summary>What the editor says under the diagram.</summary>
    private const string Hint = "Drag a dot to move it. Drag its label to reposition the text.";

    /// <summary>The flags the hint is measured and drawn with: wrapped to the width, should it come to that.</summary>
    private const TextFormatFlags HintFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Bottom;

    /// <summary>How tall the hint is at the bottom, in the small face at the text size in use - none outside the editor.</summary>
    private int HintRoom => EditMode
        ? TextRenderer.MeasureText(Hint, AppTheme.SmallFont, new Size(Math.Max(1, Width - 12), int.MaxValue), HintFlags).Height + 6
        : 0;

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

        var boxes = LabelBoxes();

        // The leaders first, then the labels and the dots over them, so a
        // leader never crosses a label it does not belong to.
        foreach (var point in _template.DiagramFields)
        {
            if (boxes.TryGetValue(point.Id ?? string.Empty, out var box))
            {
                DrawLeader(g, point, box);
            }
        }

        foreach (var point in _template.DiagramFields)
        {
            if (boxes.TryGetValue(point.Id ?? string.Empty, out var box))
            {
                DrawCallout(g, point, box);
            }
        }

        if (EditMode)
        {
            var hint = new Rectangle(6, 0, Math.Max(1, Width - 12), Height - 3);
            TextRenderer.DrawText(g, Hint, AppTheme.SmallFont, hint, AppTheme.TextMuted, HintFlags);
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
        const float deck = GenericDeck;

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
        foreach (var (text, at) in GenericCaptions(b))
        {
            g.DrawString(text, AppTheme.SmallFont, labelBrush, at.Location);
        }
    }

    /// <summary>The deck's half-width in the outline, as a share of its width (DrawGenericChassis).</summary>
    private const float GenericDeck = 0.33f;

    /// <summary>
    /// The outline's two words and where they go - the wheelbase beside its
    /// dimension line, and "FRONT" above the nose - placed from their own
    /// size, which follows the text size: a fixed 8px and 14px put "FRONT"
    /// on the deck at 200%. The labels keep off them (LabelBoxes).
    /// </summary>
    private static IEnumerable<(string Text, Rectangle At)> GenericCaptions(Rectangle b)
    {
        var wheelbase = $"{WheelbaseMm:0}mm";
        var w = TextRenderer.MeasureText(wheelbase, AppTheme.SmallFont);
        var dimX = b.Left + b.Width * (0.5f + GenericDeck + 0.055f);
        var middle = b.Top + b.Height * ((FrontAxleY + RearAxleY) / 2f);

        yield return (wheelbase, new Rectangle((int)(dimX + 2), (int)(middle - w.Height / 2f), w.Width, w.Height));

        var f = TextRenderer.MeasureText("FRONT", AppTheme.SmallFont);

        yield return ("FRONT", new Rectangle((int)(b.Left + b.Width * 0.5f - f.Width / 2f), b.Top - f.Height, f.Width, f.Height));
    }

    /// <summary>What a point's label says: its name, and its value when it has one.</summary>
    private string CalloutText(SetupField point) =>
        Values.TryGetValue(point.Id, out var value) && !string.IsNullOrWhiteSpace(value) ? $"{point.Label}: {value}" : point.Label;

    /// <summary>A dot's size: 7px, or 9 for the one selected, at the text size in use.</summary>
    private static float DotSize(bool selected) => UiLayout.AtTextSize(selected ? 9 : 7);

    /// <summary>How near a dot a press still picks it: 8px, at the text size in use.</summary>
    private static int DotReach => UiLayout.AtTextSize(8);

    /// <summary>
    /// Where each label is drawn, by point id - and so where it is picked.
    ///
    /// Each starts where its point puts it (LabelPixel), kept inside the
    /// control and clear of the hint. Then, top to bottom, any that lands on
    /// a label already placed, or on any dot - its own or another's - is
    /// moved up or down off it, as little as it can be, and on in the same
    /// direction past anything it meets next - whichever way is the shorter
    /// move - and left where its point put it if there is no room either
    /// way. A label being dragged is placed first and never moved. Where the
    /// labels are kept is not changed - a drag still moves the label the
    /// editor saves - only where they are shown.
    ///
    /// The labels grow with the text and the diagram does not: at 200%
    /// "Rear tyre width" covered "Motor", and dots sat on the words of
    /// "Upper suspension shaft" and "Suspension plate (T-bar)" (Chassis
    /// Diagram, Setup Sheet, layout pictures, 30 Sep).
    /// </summary>
    private Dictionary<string, Rectangle> LabelBoxes()
    {
        var boxes = new Dictionary<string, Rectangle>(StringComparer.Ordinal);

        if (_template is null)
        {
            return boxes;
        }

        var room = Rectangle.FromLTRB(2, 2, Math.Max(3, Width - 2), Math.Max(3, Height - 2 - HintRoom));
        var gap = UiLayout.AtTextSize(3);

        // Every dot, with a little room round it, is somewhere no label goes.
        var dots = _template.DiagramFields
            .Select(point =>
            {
                var anchor = AnchorPixel(point);
                var reach = (int)Math.Ceiling(DotSize(selected: true) / 2f) + gap;

                return new Rectangle((int)anchor.X - reach, (int)anchor.Y - reach, reach * 2, reach * 2);
            })
            .ToList();

        // And the outline's own words, when it is the outline that is drawn.
        if (_diagram is null)
        {
            dots.AddRange(GenericCaptions(DiagramBounds).Select(caption => caption.At));
        }

        var placed = new List<Rectangle>();

        // A label being dragged goes exactly where it is dragged to, and the
        // others make way for it - it does not jump aside under the mouse.
        var dragged = _dragKind == DragKind.Label ? _dragPointId : null;

        var order = _template.DiagramFields
            .Where(p => p.Id is not null)
            .OrderBy(p => string.Equals(p.Id, dragged, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(p => LabelPixel(p).Y)
            .ThenBy(p => LabelPixel(p).X);

        foreach (var point in order)
        {
            var wanted = Keep(Wanted(point), room);
            Rectangle box;

            if (string.Equals(point.Id, dragged, StringComparison.Ordinal))
            {
                box = wanted;
            }
            else
            {
                box = Settle(wanted);
            }

            boxes[point.Id] = box;
            placed.Add(box);

            // The nearest place clear of every label placed and every dot:
            // straight up or down from where it is wanted, or from a little
            // to either side of it when a column of labels has no room left
            // (a sheet with many long names, at a larger text size - layout
            // pictures, 1 Oct, where up and down alone left labels on each
            // other and on dots). A move sideways counts for a bit more than
            // the same move up or down, so a label keeps to its own side.
            // When nowhere is clear, LeastCovered.
            Rectangle Settle(Rectangle want)
            {
                Rectangle? best = null;
                var bestCost = int.MaxValue;

                foreach (var dx in Sideways(want.Width))
                {
                    // Nothing this far across can beat what is found already.
                    if (Math.Abs(dx) * 3 / 2 >= bestCost)
                    {
                        continue;
                    }

                    var start = Keep(new Rectangle(want.X + dx, want.Y, want.Width, want.Height), room);

                    foreach (var down in new[] { true, false })
                    {
                        if (Clear(start, down) is { } found)
                        {
                            var cost = Math.Abs(found.X - want.X) * 3 / 2 + Math.Abs(found.Y - want.Y);

                            if (cost < bestCost)
                            {
                                best = found;
                                bestCost = cost;
                            }
                        }
                    }

                    // Where it is wanted, and clear: the usual case, and the
                    // end of it (this runs at every paint and mouse move).
                    if (bestCost == 0)
                    {
                        break;
                    }
                }

                return best ?? LeastCovered(want);
            }

            // When nowhere near is clear: the place that best weighs how
            // much of the others and the dots the label covers (with the
            // same gap round it as Clear keeps) against how far it moves -
            // a pixel moved counting as half a column of the label covered,
            // so it does not cross the diagram to save a sliver.
            Rectangle LeastCovered(Rectangle want)
            {
                var best = want;
                var bestScore = long.MaxValue;
                var step = Math.Max(4, want.Height / 2);
                var perPixel = Math.Max(1, want.Height / 2);

                foreach (var dx in Sideways(want.Width))
                {
                    for (var y = room.Top; y + want.Height <= room.Bottom; y += step)
                    {
                        var at = Keep(new Rectangle(want.X + dx, y, want.Width, want.Height), room);
                        var near = Inflated(at, gap);
                        long covered = 0;

                        foreach (var other in placed.Concat(dots))
                        {
                            var overlap = Rectangle.Intersect(near, other);

                            if (overlap.Width > 0 && overlap.Height > 0)
                            {
                                covered += (long)overlap.Width * overlap.Height;
                            }
                        }

                        var moved = Math.Abs(at.X - want.X) * 3 / 2 + Math.Abs(at.Y - want.Y);
                        var score = covered + (long)moved * perPixel;

                        if (score < bestScore)
                        {
                            best = at;
                            bestScore = score;
                        }
                    }
                }

                return best;
            }

            // Moved one way only, so it cannot be bounced between two
            // things for ever: null when it runs out of room that way.
            Rectangle? Clear(Rectangle start, bool down)
            {
                var at = start;

                for (var step = 0; step < 64; step++)
                {
                    var hit = placed.Concat(dots).Where(other => other.IntersectsWith(Inflated(at, gap))).ToList();

                    if (hit.Count == 0)
                    {
                        return at;
                    }

                    var y = down ? hit.Max(other => other.Bottom) + gap : hit.Min(other => other.Top) - gap - at.Height;

                    if (y < room.Top || y + at.Height > room.Bottom)
                    {
                        return null;
                    }

                    at.Y = y;
                }

                return null;
            }
        }

        return boxes;

        Rectangle Wanted(SetupField point)
        {
            var labelPos = LabelPixel(point);
            var size = TextRenderer.MeasureText(CalloutText(point), AppTheme.SmallFont);

            return new Rectangle((int)labelPos.X - size.Width / 2, (int)labelPos.Y - size.Height / 2 - 2, size.Width + 10, size.Height + 6);
        }

        // Where a label is tried, across from where it is wanted: there
        // first, then half its width either way, then its whole width, then
        // half as much again.
        static IEnumerable<int> Sideways(int width)
        {
            yield return 0;

            foreach (var share in new[] { 1, 2, 3 })
            {
                yield return -width * share / 2;
                yield return width * share / 2;
            }
        }

        // Kept inside the control rather than clipped off an edge.
        static Rectangle Keep(Rectangle box, Rectangle room)
        {
            box.X = Math.Clamp(box.X, room.Left, Math.Max(room.Left, room.Right - box.Width));
            box.Y = Math.Clamp(box.Y, room.Top, Math.Max(room.Top, room.Bottom - box.Height));
            return box;
        }

        // Only what actually overlaps counts: a box touching another, edge
        // to edge, is not on it.
        static Rectangle Inflated(Rectangle box, int by) => Rectangle.Inflate(box, Math.Max(0, by - 1), Math.Max(0, by - 1));
    }

    /// <summary>The line from a point's dot to the nearest edge of its label.</summary>
    private void DrawLeader(Graphics g, SetupField point, Rectangle box)
    {
        var anchor = AnchorPixel(point);
        var selected = point.Id == SelectedPointId;

        var target = new PointF(
            Math.Clamp(anchor.X, box.Left, box.Right),
            Math.Clamp(anchor.Y, box.Top, box.Bottom));

        using var linePen = new Pen(selected ? AppTheme.Primary : AppTheme.Border, selected ? 1.8f : 1.2f);
        g.DrawLine(linePen, anchor, target);
    }

    private void DrawCallout(Graphics g, SetupField point, Rectangle box)
    {
        var anchor = AnchorPixel(point);
        var selected = point.Id == SelectedPointId;

        var hasValue = Values.TryGetValue(point.Id, out var value) && !string.IsNullOrWhiteSpace(value);
        var text = CalloutText(point);

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
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        // Anchor dot.
        var dotSize = DotSize(selected);
        using var dotBrush = new SolidBrush(selected ? AppTheme.Primary : (hasValue ? AppTheme.Success : AppTheme.TextMuted));
        g.FillEllipse(dotBrush, anchor.X - dotSize / 2, anchor.Y - dotSize / 2, dotSize, dotSize);

        using var dotRing = new Pen(AppTheme.Surface, 1.5f);
        g.DrawEllipse(dotRing, anchor.X - dotSize / 2, anchor.Y - dotSize / 2, dotSize, dotSize);
    }

    /// <summary>Moves where <paramref name="point"/>'s label is kept to where <paramref name="box"/> shows it - the reverse of LabelBoxes' Wanted.</summary>
    private void KeepLabelAt(SetupField point, Rectangle box)
    {
        var size = TextRenderer.MeasureText(CalloutText(point), AppTheme.SmallFont);
        var b = DiagramBounds;

        var x = box.X + size.Width / 2f;
        var y = box.Y + size.Height / 2f + 2;

        point.LabelOffsetX = Math.Clamp((x - b.Left) / Math.Max(1, b.Width) - point.X, -1f, 1f);
        point.LabelOffsetY = Math.Clamp((y - b.Top) / Math.Max(1, b.Height) - point.Y, -1f, 1f);
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
            if (Math.Abs(a.X - location.X) <= DotReach && Math.Abs(a.Y - location.Y) <= DotReach)
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

        // Where the labels are drawn, which is not always where their points
        // put them (LabelBoxes) - so what is picked is what was pressed.
        var boxes = LabelBoxes();

        foreach (var p in _template.DiagramFields)
        {
            if (p.Id is not null && boxes.TryGetValue(p.Id, out var box) && box.Contains(location))
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
            // A label shown moved aside (LabelBoxes) is taken up from where
            // it is shown: kept where it is seen, it does not jump back to
            // where its point puts it the moment it is pressed.
            if (anchorHit is null && _template?.DiagramFields.FirstOrDefault(p => p.Id == hit) is { } point
                && LabelBoxes().TryGetValue(hit, out var shown))
            {
                KeepLabelAt(point, shown);
            }

            // WHY (23 Nov 2026, bug hunt 24): remember where in the label the
            // press landed (see _dragGrab); an anchor drag keeps none.
            _dragGrab = PointF.Empty;
            if (anchorHit is null && _template?.DiagramFields.FirstOrDefault(p => p.Id == hit) is { } grabbed)
            {
                var kept = LabelPixel(grabbed);
                _dragGrab = new PointF(e.X - kept.X, e.Y - kept.Y);
            }

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
            // WHY (23 Nov 2026, bug hunt 24): the grab offset is taken off the
            // mouse first. The old code set the offset from the mouse alone,
            // which put the label's middle under the cursor and made it jump.
            var (ox, oy) = LabelDragOffset(e.X, e.Y, _dragGrab, b, point.X, point.Y);
            point.LabelOffsetX = ox;
            point.LabelOffsetY = oy;
        }

        Invalidate();
    }

    /// <summary>
    /// The label offset a drag gives: the label's middle goes where the mouse
    /// is less <paramref name="grab"/> (how far from the middle the press
    /// landed), as a fraction of <paramref name="bounds"/> measured from the
    /// point at (<paramref name="pointX"/>, <paramref name="pointY"/>),
    /// clamped to -1..1 as before.
    /// </summary>
    internal static (float X, float Y) LabelDragOffset(float mouseX, float mouseY, PointF grab, Rectangle bounds, float pointX, float pointY)
    {
        var nx = (mouseX - grab.X - bounds.Left) / Math.Max(1, bounds.Width);
        var ny = (mouseY - grab.Y - bounds.Top) / Math.Max(1, bounds.Height);
        return (Math.Clamp(nx - pointX, -1f, 1f), Math.Clamp(ny - pointY, -1f, 1f));
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
