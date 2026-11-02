using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// HEATCALLER'S MARK ON WHAT THE APP MAKES (31 Oct 2026, Asher: "Build it
/// all", and the mark is always on everywhere, with no switch for a club).
///
/// The relay's pages already sign off with a small sun and the HEATCALLER
/// wordmark (relay/src/pages.ts, HC_SUN and HC_WORD), and their share
/// cards say "Timed and called by HeatCaller" beside the full-colour sun.
/// Everything the app makes for people to see now does the same: the
/// highlight video's end card, the Big Screen, the stream graphics, the
/// pages on the club's Wi-Fi, every printed sheet, the results picture,
/// the event report and the driver emails.
///
/// THE RULE, as on the relay: the club's name, logo and colours stay in
/// charge. The mark is small, at the edges, and in the club's own colours
/// (or plain black on paper), the way a timing system's name shows on a
/// race broadcast. HeatCaller's own synthwave colours appear only where
/// the thing is already partly HeatCaller's - the video's end card and
/// the results picture, as on the relay's cards.
///
/// TWO DRAWINGS, both the logo in Assets\HeatCaller.svg:
///  - <see cref="DrawSun"/>: the sun over its horizon in one colour, the
///    relay's HC_SUN on the same 24-unit square, for small places.
///  - <see cref="DrawLogo"/>: the logo itself in HeatCaller's colours, the
///    relay's HC_LOGO on the 256-unit square, with the floor's grid thinned
///    to the lines that read at a small size.
///
/// Every method draws with brushes and pens of its own and takes the font
/// it is given, so the highlight video's background thread can use them:
/// nothing here holds a GDI+ object another thread could touch.
/// </summary>
internal static class HeatCallerMark
{
    /// <summary>The wordmark, as the app's own title bars spell it.</summary>
    public const string Word = "HEATCALLER";

    /// <summary>The share line, word for word the relay's share cards' (pages.ts).</summary>
    public const string Credit = "Timed and called by HeatCaller";

    /// <summary>The website, as printed under a sheet.</summary>
    public const string Site = AppSupport.DocsText;

    /// <summary>The one line at the foot of every email to a driver.</summary>
    public const string EmailLine = "Timing by HeatCaller - " + AppSupport.DocsText;

    // HeatCaller's own colours: heatcaller.com's ink and magenta for the
    // wordmark, and the logo's own (Assets\HeatCaller.svg).
    public static readonly Color Ink = Color.FromArgb(0xF4, 0xED, 0xFF);
    public static readonly Color Magenta = Color.FromArgb(0xFF, 0x2E, 0x88);
    private static readonly Color Night = Color.FromArgb(0x14, 0x08, 0x29);
    private static readonly Color Floor = Color.FromArgb(0x3B, 0x10, 0x63);
    private static readonly Color Grid = Color.FromArgb(0xFF, 0x2E, 0x9A);
    private static readonly Color Horizon = Color.FromArgb(0xFF, 0xD9, 0xF0);
    private static readonly Color SunTop = Color.FromArgb(0xFF, 0xE9, 0xA0);
    private static readonly Color SunMiddle = Color.FromArgb(0xFF, 0xB3, 0x47);
    private static readonly Color SunBottom = Color.FromArgb(0xFF, 0x5F, 0xA2);

    // The one-colour sun on Synthwave (1 Nov 2026, Asher: "when classic theme
    // is selected the sun should be yellow and orange not pink"): Classic
    // wears Synthwave, whose primary is pink, so on that look the sun is a
    // sunset from the logo's yellow to its orange instead, and only the
    // horizon and floor keep the primary. The relay's HC_SUNSET, colour for
    // colour.
    private static readonly Color SunsetTop = SunTop;
    private static readonly Color SunsetMiddle = SunMiddle;
    private static readonly Color SunsetBottom = Color.FromArgb(0xFF, 0x8A, 0x2A);
    private static readonly Color SynthwavePrimary = Color.FromArgb(0xFF, 0x2E, 0x9A);

    /// <summary>
    /// Whether the sun drawn in <paramref name="primary"/> should be the
    /// sunset instead: the look is Synthwave's. Told by its primary, as the
    /// relay has to (it is sent a look's colours, never its name).
    /// </summary>
    public static bool IsSunset(Color primary) => primary.ToArgb() == SynthwavePrimary.ToArgb();

    /// <summary>
    /// The sun over its horizon in one colour, fitted into a square in the
    /// middle of <paramref name="box"/>. The relay's HC_SUN: the top half of
    /// the sun, two bands of its lower half, and the horizon with three
    /// lines of the floor running away from it. In Synthwave's primary the
    /// sun itself is the sunset instead (<see cref="IsSunset"/>), and only
    /// the horizon and floor take <paramref name="colour"/>.
    /// </summary>
    public static void DrawSun(Graphics g, RectangleF box, Color colour)
    {
        var sunset = IsSunset(colour);

        var size = Math.Min(box.Width, box.Height);

        if (size <= 0)
        {
            return;
        }

        var u = size / 24f;
        var x = box.X + (box.Width - size) / 2f;
        var y = box.Y + (box.Height - size) / 2f;

        PointF P(float ux, float uy) => new(x + ux * u, y + uy * u);

        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            // The relay's gradient runs from y = 3 to y = 16.1 on the 24-unit
            // square, the top of the sun to the foot of its lower band.
            using Brush brush = sunset
                ? new LinearGradientBrush(P(0, 2.99f), P(0, 16.1f), SunsetTop, SunsetBottom)
                {
                    InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { SunsetTop, SunsetMiddle, SunsetBottom },
                        Positions = new[] { 0f, 0.55f, 1f },
                    },
                    WrapMode = WrapMode.TileFlipX,
                }
                : new SolidBrush(colour);
            using var pen = new Pen(colour, 1.1f * u);

            // "M4 11a8 8 0 0 1 16 0z" - the sun's top half, centred on (12, 11).
            g.FillPie(brush, x + 4 * u, y + 3 * u, 16 * u, 16 * u, 180f, 180f);

            g.FillRectangle(brush, x + 4.5f * u, y + 12.4f * u, 15f * u, 1.3f * u);
            g.FillRectangle(brush, x + 5.4f * u, y + 15f * u, 13.2f * u, 1.1f * u);

            g.DrawLine(pen, P(2, 18.5f), P(22, 18.5f));
            g.DrawLine(pen, P(12, 18.5f), P(5, 22.5f));
            g.DrawLine(pen, P(12, 18.5f), P(19, 22.5f));
            g.DrawLine(pen, P(12, 18.5f), P(12, 22.5f));
        }
        finally
        {
            g.SmoothingMode = smoothing;
        }
    }

    /// <summary>
    /// The logo in HeatCaller's own colours, its rounded square fitted into
    /// the middle of <paramref name="box"/>. The relay's HC_LOGO.
    /// </summary>
    public static void DrawLogo(Graphics g, RectangleF box)
    {
        var size = Math.Min(box.Width, box.Height);

        if (size <= 0)
        {
            return;
        }

        var u = size / 256f;
        var x = box.X + (box.Width - size) / 2f;
        var y = box.Y + (box.Height - size) / 2f;

        RectangleF R(float ux, float uy, float uw, float uh) => new(x + ux * u, y + uy * u, uw * u, uh * u);
        PointF P(float ux, float uy) => new(x + ux * u, y + uy * u);

        var smoothing = g.SmoothingMode;
        var clip = g.Clip;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            using var square = RoundedSquare(R(0, 0, 256, 256), 56 * u);
            g.SetClip(square, CombineMode.Intersect);

            using (var night = new SolidBrush(Night))
            {
                g.FillPath(night, square);
            }

            using (var bottom = new SolidBrush(SunBottom))
            using (var middle = new SolidBrush(SunMiddle))
            using (var top = new SolidBrush(SunTop))
            {
                var sun = R(56, 46, 144, 144);
                g.FillEllipse(bottom, sun);
                g.FillPie(middle, sun.X, sun.Y, sun.Width, sun.Height, 180f, 180f);

                // The yellow cap: the chord across the sun at y = 78.
                using var cap = new GraphicsPath();
                cap.AddArc(sun, 213.7f, 112.6f);
                cap.CloseFigure();
                g.FillPath(top, cap);
            }

            using (var night = new SolidBrush(Night))
            {
                g.FillRectangle(night, R(40, 130, 176, 6));
                g.FillRectangle(night, R(40, 146, 176, 8));
                g.FillRectangle(night, R(40, 163, 176, 10));
            }

            using (var floor = new SolidBrush(Floor))
            {
                g.FillRectangle(floor, R(0, 176, 256, 80));
            }

            using (var grid = new Pen(Grid, 3f * u))
            {
                foreach (var (x1, y1) in new (float, float)[] { (0, 214), (20, 256), (74, 256), (128, 256), (182, 256), (236, 256), (256, 214) })
                {
                    g.DrawLine(grid, P(128, 176), P(x1, y1));
                }

                foreach (var line in new float[] { 190, 206, 228 })
                {
                    g.DrawLine(grid, P(0, line), P(256, line));
                }
            }

            using (var horizon = new Pen(Horizon, 4f * u))
            {
                g.DrawLine(horizon, P(0, 176), P(256, 176));
            }
        }
        finally
        {
            g.Clip = clip;
            clip.Dispose();
            g.SmoothingMode = smoothing;
        }
    }

    /// <summary>
    /// HEATCALLER at <paramref name="at"/>, "HEAT" in one colour and
    /// "CALLER" in another, as heatcaller.com writes it. The width it took.
    /// </summary>
    public static float DrawWord(Graphics g, Font font, PointF at, Color heat, Color caller)
    {
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;

        using var heatBrush = new SolidBrush(heat);
        using var callerBrush = new SolidBrush(caller);

        var first = g.MeasureString("HEAT", font, PointF.Empty, format).Width;
        var second = g.MeasureString("CALLER", font, PointF.Empty, format).Width;

        g.DrawString("HEAT", font, heatBrush, at, format);
        g.DrawString("CALLER", font, callerBrush, new PointF(at.X + first, at.Y), format);

        return first + second;
    }

    /// <summary>How wide <see cref="DrawWord"/> draws in <paramref name="font"/>.</summary>
    public static float MeasureWord(Graphics g, Font font)
    {
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;

        return g.MeasureString(Word, font, PointF.Empty, format).Width;
    }

    /// <summary>The gap between the sun and the wordmark, for a mark drawn in a font of this height.</summary>
    private static float Gap(float height) => height * 0.35f;

    /// <summary>
    /// The sign-off: the sun, one colour, and HEATCALLER beside it, with the
    /// sun as tall as the font's line and its middle on the text's middle.
    /// <paramref name="sun"/> null draws the full-colour logo instead.
    /// The width it took.
    /// </summary>
    public static float DrawSignOff(Graphics g, Font font, PointF at, Color? sun, Color heat, Color caller)
    {
        var height = font.GetHeight(g);
        var mark = new RectangleF(at.X, at.Y, height, height);

        if (sun is { } colour)
        {
            DrawSun(g, mark, colour);
        }
        else
        {
            DrawLogo(g, mark);
        }

        var word = DrawWord(g, font, new PointF(at.X + height + Gap(height), at.Y + height * 0.08f), heat, caller);

        return height + Gap(height) + word;
    }

    /// <summary>How wide <see cref="DrawSignOff"/> draws in <paramref name="font"/>.</summary>
    public static float MeasureSignOff(Graphics g, Font font)
    {
        var height = font.GetHeight(g);

        return height + Gap(height) + MeasureWord(g, font);
    }

    /// <summary>
    /// The foot of a printed page (31 Oct 2026): a rule, then the black sun
    /// and HEATCALLER on the left and heatcaller.com, then anything the
    /// page adds (its page number), on the right. Black on white, so it
    /// prints the same on any printer. <paramref name="y"/> is the rule.
    /// </summary>
    public static void DrawPrintFooter(Graphics g, float left, float right, float y, Font font, string? rightText = null)
    {
        using (var rule = new Pen(Color.FromArgb(200, 200, 200)))
        {
            g.DrawLine(rule, left, y, right, y);
        }

        var top = y + font.GetHeight(g) * 0.45f;

        DrawSignOff(g, font, new PointF(left, top), Color.Black, Color.Black, Color.Black);

        var text = string.IsNullOrWhiteSpace(rightText) ? Site : Site + "    " + rightText;

        using var grey = new SolidBrush(Color.FromArgb(110, 110, 110));
        using var format = new StringFormat { Alignment = StringAlignment.Far };

        g.DrawString(text, font, grey, new RectangleF(left, top, right - left, font.GetHeight(g) * 1.4f), format);
    }

    private static GraphicsPath RoundedSquare(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    // --- The web pages -------------------------------------------------------

    /// <summary>
    /// The sun in one colour as inline SVG, word for word the relay's HC_SUN
    /// (pages.ts), drawn in currentColor so it takes the page's look.
    /// </summary>
    public const string SunSvg =
        "<svg class=\"hcsun\" viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M4 11a8 8 0 0 1 16 0z\"/>"
        + "<path d=\"M4.5 12.4h15v1.3h-15zM5.4 15h13.2v1.1H5.4z\"/>"
        + "<path d=\"M2 18.5h20M12 18.5l-7 4M12 18.5l7 4M12 18.5v4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.1\"/></svg>";

    /// <summary>
    /// The Synthwave sunset for <see cref="SunSvg"/>, word for word the
    /// relay's HC_SUNSET: the gradient once a page, in an SVG of no size
    /// rather than a hidden one (a gradient inside display:none is not
    /// drawn). A page's --hcsun is <see cref="SunsetFill"/> on Synthwave.
    /// </summary>
    public const string SunsetSvg =
        "<svg class=\"hcdefs\" aria-hidden=\"true\" focusable=\"false\">"
        + "<linearGradient id=\"hcsunset\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"3\" x2=\"0\" y2=\"16.1\">"
        + "<stop offset=\"0\" stop-color=\"#FFE9A0\"/><stop offset=\".55\" stop-color=\"#FFB347\"/><stop offset=\"1\" stop-color=\"#FF8A2A\"/>"
        + "</linearGradient></svg>";

    /// <summary>The sun's fill on Synthwave: the sunset, or plain orange where the gradient can't be found.</summary>
    public const string SunsetFill = "url(#hcsunset) #FFB347";

    /// <summary>The wordmark as the relay writes it: Heat, then Caller in the look's primary.</summary>
    public const string WordHtml = "<span class=\"hcword\">Heat<span class=\"hcc\">Caller</span></span>";

    /// <summary>
    /// The pages' sign-off on the club's Wi-Fi (31 Oct 2026): the relay's
    /// footer mark, a link to heatcaller.com, so a driver sees one product
    /// on the Wi-Fi and on the relay. Colours from the page's own look.
    /// </summary>
    public const string PageFooterHtml =
        "<footer class=\"hcfoot\"><a class=\"hcmark\" href=\"https://heatcaller.com\" target=\"_blank\" rel=\"noopener\">"
        + SunsetSvg + SunSvg + WordHtml + "</a></footer>";

    /// <summary>The style <see cref="PageFooterHtml"/> needs, from the page's look variables.</summary>
    public const string PageFooterStyle = """
        .hcsun { width:18px; height:18px; flex:none; fill:var(--hcsun, currentColor); color:var(--primary); }
        .hcdefs { position:absolute; width:0; height:0; overflow:hidden; }
        .hcword { font-family:var(--cond, 'Bahnschrift','Roboto Condensed',sans-serif); font-stretch:condensed;
                  font-weight:700; letter-spacing:.05em; text-transform:uppercase; }
        .hcword .hcc { color:var(--primary); }
        .hcfoot { display:flex; justify-content:flex-end; padding:14px 18px 18px; }
        .hcmark { display:inline-flex; align-items:center; gap:7px; color:var(--muted); text-decoration:none; font-size:15px; }
        .hcmark:hover { color:var(--text); }
        """;
}
