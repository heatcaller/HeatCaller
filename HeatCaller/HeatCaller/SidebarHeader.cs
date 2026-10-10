namespace HeatCaller;

/// <summary>
/// The sidebar's header: the club's logo when one is set (see SetLogo),
/// otherwise either the animated synthwave scene or plain app title text,
/// depending on the look's family. On the rail (Compact) it is none of
/// those three but the app's initials, because sixty pixels across is not
/// a smaller header but a differently shaped one.
///
/// A custom Control rather than a Label with a Paint handler, because the
/// animation repaints roughly twenty times a second and a Label isn't
/// double-buffered - every frame erased the background before redrawing,
/// which showed as flicker on the moving grid lines.
///
/// The scene is split into layers, and the split is what makes the stars
/// work. Rebuilding the whole thing per frame - two gradient brushes with
/// colour blends, a thirty-star field, a clipped sun - was the real source
/// of the uneven motion, so the expensive parts are cached. But caching
/// them all in ONE bitmap meant the starfield was baked in too, and a
/// setting that promises "twinkling stars" delivered a still photograph of
/// some.
///
/// So there are two cached bitmaps rather than one: the sky behind the
/// stars, and everything that has to sit in FRONT of them - sun, mountains,
/// glow, title. Per frame: blit the sky, draw the stars live, blit the
/// foreground, draw the grid. Two blits and thirty small ellipses, which
/// is nothing, and a star behind the sun still stays behind it.
/// </summary>
public sealed class SidebarHeader : Control
{
    private readonly string _titleLine1;
    private readonly string _titleLine2;

    private Bitmap? _sky;
    private Bitmap? _foreground;

    // The club logo: our own full-size copy (so a resize can rescale from
    // the original rather than from an already-shrunk copy), a copy scaled
    // to exactly the size it is drawn at, and the plate colour, if any.
    private Bitmap? _logo;
    private Bitmap? _logoScaled;
    private Color? _logoPlate;

    /// <summary>
    /// Whether the trimmed logo still has a good deal of see-through margin
    /// inside its own frame - the signature of a round badge or seal, whose
    /// bounding box can never be trimmed away. Decided once when the logo is
    /// set (see TrimmedCopy) and used only to pick the plate's shape.
    /// </summary>
    private bool _logoIsBadgeShaped;

    // Room left around the logo, and inside the plate around it. Small on
    // purpose: the logo is zoomed to fill the whole space the Synthwave
    // scene takes, not set in the middle of it like a stamp.
    private const int LogoPadding = 4;
    private const int PlateMargin = 6;

    /// <summary>When true, draws the full animated scene; otherwise just the title text.</summary>
    public bool SynthwaveMode { get; init; }

    /// <summary>Narrowed with the sidebar - see Compact.</summary>
    private bool _compact;

    /// <summary>
    /// The sidebar narrowed to a rail of icons (MainForm.SetNavIconsOnly).
    ///
    /// The header then has about sixty pixels across to work with, which is
    /// about a quarter of the open sidebar's, so what is left of it is the
    /// app's initials in the middle - "HC" - in place of the wordmark, the
    /// club's logo and the Synthwave scene alike.
    ///
    /// The scene goes because a sun, a mountain range and a horizon grid in
    /// a strip that narrow is a smear rather than a scene. The logo used to
    /// be kept, on the reasoning that DrawLogo fits it to whatever room
    /// there is and a badge at rail width still reads as that club's badge -
    /// and that was wrong, because a quarter of the width is not a smaller
    /// header but a differently shaped one: a wide logo squashes to mush in
    /// it and a tall one fills the whole rail, and the club that reported it
    /// (Asher, 10 Oct) leaves the rail up all evening, so what they were
    /// looking at all evening was the worse of the two.
    ///
    /// Neither is merely painted smaller, and neither is painted at all -
    /// see ShowingLogo and ShowingScene, which is what OnPaint asks.
    /// </summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            if (_compact == value)
            {
                return;
            }

            _compact = value;

            // The cached scene layers are bitmaps the size of the control,
            // and a compact header never draws them - so they are let go
            // here as well as on a resize, rather than being kept at the old
            // width for as long as the rail is up.
            DropCache();
            Invalidate();
        }
    }

    /// <summary>
    /// Whether the header is actually being repainted on a timer.
    ///
    /// The stars are drawn from the animation phase, and with the
    /// animation switched off that phase is frozen - so twinkling would
    /// render one arbitrary moment of the cycle and call it a still image.
    /// Told plainly instead, the still image uses the designed brightness.
    /// </summary>
    public bool Animated { get; set; }

    /// <summary>
    /// True when a club logo has been SET on the header - which is not the
    /// same as its being drawn: on the rail it is not (see ShowingLogo).
    /// MainForm reads this to pick the header's height and to decide
    /// whether the animation is worth running, and both of those already
    /// treat the rail as its own case.
    /// </summary>
    public bool HasLogo => _logo is not null;

    /// <summary>
    /// Whether the club's logo is what the next paint will draw.
    ///
    /// A property rather than a line inside OnPaint because of the bug it
    /// is here to make hard to write again. The logo used to win the paint
    /// outright: its test was the FIRST thing OnPaint did and it returned
    /// there and then, so narrowing the sidebar hid the Synthwave scene -
    /// whose test came after it and does look at Compact - and left the
    /// logo being squeezed into a sixty-pixel rail. Written out here, the
    /// rail is part of the answer rather than something a later branch
    /// happens to remember, and a test can ask the same question OnPaint
    /// asks without having to read pixels off a screenshot.
    /// </summary>
    public bool ShowingLogo => _logo is not null && !_compact;

    /// <summary>
    /// Whether the animated Synthwave scene is what the next paint will
    /// draw: the Synthwave family, no club logo in front of it, and the
    /// sidebar not narrowed to its rail.
    ///
    /// MainForm also stops the animation timer on the way into the rail
    /// (StartOrStopHeaderAnimation), so the frames are not merely thrown
    /// away unseen - they are never drawn. The rail is the state a club
    /// leaves the sidebar in for a whole race night, and twenty frames a
    /// second of a scene nobody can see is a cost a laptop at a track pays
    /// for nothing.
    /// </summary>
    public bool ShowingScene => SynthwaveMode && !_compact && _logo is null;

    /// <summary>
    /// Shows the club logo in place of the title (or the Synthwave scene),
    /// or with null goes back to them.
    ///
    /// Keeps its OWN copy of the image: the caller disposes what it passed
    /// in as soon as this returns, and a header that held on to someone
    /// else's image would paint a disposed one. The plate is drawn behind
    /// the logo when it would otherwise vanish into the sidebar - see
    /// ClubLogo.PlateFor.
    /// </summary>
    public void SetLogo(Image? logo, Color? plate)
    {
        DropLogo();

        if (logo is not null)
        {
            try
            {
                // new Bitmap(Image) always comes out 32bpp ARGB, so a
                // transparent logo stays transparent. Inside the try, sizes
                // included: an image already disposed throws on its Width.
                if (logo.Width > 0 && logo.Height > 0)
                {
                    _logo = TrimmedCopy(logo);
                    _logoPlate = plate;
                    _logoIsBadgeShaped = IsBadgeShaped(_logo);
                }
            }
            catch
            {
                // A logo that can't be copied is no logo: the header falls
                // back to the title rather than throwing into the caller.
                DropLogo();
            }
        }

        Invalidate();
    }

    /// <summary>
    /// A copy of the logo with its empty border cut away.
    ///
    /// A stored logo is square with clear space around the artwork (it is
    /// also the phones' home-screen icon - see ClubLogo.Render), and a wide
    /// logo inside a square has clear space above and below it as well.
    /// Fitted as it is, that border is what fills the header and the logo
    /// itself comes out small. Cut to the artwork, it zooms to fill the
    /// space the Synthwave scene takes. Nearly clear pixels (alpha under
    /// 16) count as empty, so a faint halo doesn't hold the border in.
    /// </summary>
    private static Bitmap TrimmedCopy(Image logo)
    {
        var copy = new Bitmap(logo);

        try
        {
            var width = copy.Width;
            var height = copy.Height;
            var alpha = AlphaOf(copy);
            int left = width, top = height, right = -1, bottom = -1;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (alpha[y * width + x] >= 16)
                    {
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
                }
            }

            // Wholly clear, or nothing to cut: the copy as it is.
            if (right < left || bottom < top
                || (left == 0 && top == 0 && right == width - 1 && bottom == height - 1))
            {
                return copy;
            }

            var cropped = copy.Clone(
                new Rectangle(left, top, right - left + 1, bottom - top + 1),
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            copy.Dispose();
            return cropped;
        }
        catch
        {
            // Trimming is a nicety; the untrimmed logo still works.
            return copy;
        }
    }

    /// <summary>
    /// Whether a TRIMMED logo still leaves a good deal of see-through
    /// margin inside its own bounding box.
    ///
    /// Trimming cuts away any letterboxing around the artwork, so what is
    /// left afterwards is as tight as a rectangle can get - UNLESS the
    /// artwork itself is round or badge-shaped, in which case its corners
    /// stay transparent no matter how tightly it is cropped. A plain
    /// wordmark or rectangular logo comes out of TrimmedCopy close to
    /// fully solid; a circular seal does not, however tightly it is
    /// cropped, because a circle's own bounding square always has
    /// transparent corners. Used only to choose the plate's shape (see
    /// DrawLogo) - not to decide whether a plate is drawn at all, which is
    /// ClubLogo.PlateFor's job.
    /// </summary>
    private static bool IsBadgeShaped(Bitmap logo)
    {
        var width = logo.Width;
        var height = logo.Height;
        var alpha = AlphaOf(logo);
        var solid = 0;
        var total = 0;

        for (var y = 0; y < height; y += 4)
        {
            for (var x = 0; x < width; x += 4)
            {
                total++;

                if (alpha[y * width + x] >= 128)
                {
                    solid++;
                }
            }
        }

        // Comfortably below "basically the whole frame" (a rectangular
        // logo trims to something close to 100%), and comfortably above a
        // circle's own share of its bounding square (~78.5%) - so ordinary
        // rounding error in a rectangular logo's corners cannot cross it.
        return total > 0 && solid < total * 0.9;
    }

    /// <summary>
    /// Every pixel's alpha, top row first - the A that GetPixel would give
    /// for (x, y) is at y * Width + x.
    ///
    /// TrimmedCopy and IsBadgeShaped used to ask GetPixel for one pixel at a
    /// time, and GetPixel is a separate trip into GDI+ for every pixel: a
    /// 384px logo is nearly 150,000 of them, a visible hitch on the UI
    /// thread each time a logo was set. LockBits hands the pixels over in
    /// one go instead. They are asked for as 32bpp ARGB, which the logo
    /// copies already are (new Bitmap(Image) makes that; so does Clone to
    /// Format32bppArgb) and which GDI+ converts anything else into - so each
    /// value is the same byte GetPixel returned, and the thresholds above
    /// see exactly what they saw before.
    /// </summary>
    private static byte[] AlphaOf(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var alpha = new byte[width * height];
        var row = new byte[width * 4];

        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            for (var y = 0; y < height; y++)
            {
                // A row at a time, stepping by the stride GDI+ reports rather
                // than assuming rows are packed end to end.
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + (nint)y * data.Stride, row, 0, row.Length);

                // In memory each pixel is blue, green, red, alpha.
                for (var x = 0; x < width; x++)
                {
                    alpha[y * width + x] = row[x * 4 + 3];
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return alpha;
    }

    public SidebarHeader(string titleLine1, string titleLine2)
    {
        _titleLine1 = titleLine1;
        _titleLine2 = titleLine2;

        // This combination is what makes double buffering actually take
        // effect for a control that draws all of its own pixels.
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        DoubleBuffered = true;
        TabStop = false;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // Size changed, so the cached layer no longer fits - drop it and let
        // the next paint rebuild at the new size.
        DropCache();
    }

    private void DropCache()
    {
        _sky?.Dispose();
        _sky = null;

        _foreground?.Dispose();
        _foreground = null;

        // The scaled logo was made for the old size too. The original is
        // kept - it is what the new one is scaled from.
        _logoScaled?.Dispose();
        _logoScaled = null;
    }

    private void DropLogo()
    {
        _logoScaled?.Dispose();
        _logoScaled = null;

        _logo?.Dispose();
        _logo = null;

        _logoPlate = null;
        _logoIsBadgeShaped = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Both of these are false on the rail whatever is set, so the rail
        // falls through to DrawPlainTitle and its initials - see Compact.
        // Between the two of them the logo wins whatever the family: a club
        // that has set one wants to see it, Synthwave or not.
        if (ShowingLogo)
        {
            DrawLogo(e.Graphics);
            return;
        }

        // The plain looks and the rail both land here. Asking ShowingScene
        // rather than repeating its terms is the point: a later reader
        // cannot add a case to one and forget the other.
        if (!ShowingScene)
        {
            DrawPlainTitle(e.Graphics);
            return;
        }

        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        EnsureBackdrop();

        var bounds = ClientRectangle;

        if (_sky is not null)
        {
            e.Graphics.DrawImageUnscaled(_sky, 0, 0);
        }

        // Live, between the two cached layers - this is the twinkle.
        AppTheme.DrawSynthwaveStars(e.Graphics, bounds, twinkle: Animated);

        if (_foreground is not null)
        {
            e.Graphics.DrawImageUnscaled(_foreground, 0, 0);
        }

        AppTheme.DrawSynthwaveGrid(e.Graphics, bounds);
    }

    private void EnsureBackdrop()
    {
        if (_sky is not null && _foreground is not null
            && _sky.Width == Width && _sky.Height == Height)
        {
            return;
        }

        DropCache();

        Bitmap? sky = null;
        Bitmap? foreground = null;

        try
        {
            var bounds = new Rectangle(0, 0, Width, Height);

            sky = new Bitmap(Width, Height);

            using (var g = Graphics.FromImage(sky))
            {
                AppTheme.DrawSynthwaveSky(g, bounds);
            }

            // Starts fully transparent, which is exactly what is wanted:
            // only the pixels the sun, the mountains, the glow and the
            // title actually cover will hide a star.
            foreground = new Bitmap(Width, Height);

            using (var g = Graphics.FromImage(foreground))
            {
                AppTheme.DrawSynthwaveForeground(g, bounds, _titleLine1, _titleLine2);
            }

            _sky = sky;
            _foreground = foreground;
        }
        catch
        {
            // If a bitmap can't be created, leave the cache empty - the
            // stars and the grid still draw, so the header degrades rather
            // than throwing from a paint handler.
            //
            // Whatever WAS made is let go here. The sky used to be left
            // behind when the foreground failed, and this runs on every
            // paint while the cache is empty - twenty a second with the
            // animation on - so each failure leaked another bitmap.
            sky?.Dispose();
            foreground?.Dispose();
        }
    }

    private void DrawLogo(Graphics g)
    {
        using (var back = new SolidBrush(AppTheme.SidebarBackground))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        if (_logo is null)
        {
            return;
        }

        // Aspect-fit into the padded area, centred.
        var area = Rectangle.Inflate(ClientRectangle, -LogoPadding, -LogoPadding);

        // With a plate, the logo is fitted inside the plate's margin and the
        // plate grows out around it. Fitting the logo first and then
        // shrinking it into the plate would take the same 12px off its
        // width and its height, which stretches any logo that isn't square.
        var inner = _logoPlate is null ? area : Rectangle.Inflate(area, -PlateMargin, -PlateMargin);

        if (inner.Width <= 0 || inner.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(inner.Width / (float)_logo.Width, inner.Height / (float)_logo.Height);
        var fitWidth = Math.Max(1, (int)Math.Round(_logo.Width * scale));
        var fitHeight = Math.Max(1, (int)Math.Round(_logo.Height * scale));
        var fit = new Rectangle(
            inner.Left + (inner.Width - fitWidth) / 2,
            inner.Top + (inner.Height - fitHeight) / 2,
            fitWidth,
            fitHeight);

        if (_logoPlate is Color plate)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // The logo sits a little inside its plate rather than touching
            // its edge. A round or badge-shaped logo gets a round plate for
            // the same reason: a rounded RECTANGLE plate is bigger than the
            // circle it sits behind, so its corners show past the logo's
            // own edge - which reads as exactly the stray white/dark border
            // this is here to avoid. An ordinary rectangular or wordmark
            // logo keeps the rounded-rect, which fits it with no such gap.
            var plateRect = Rectangle.Inflate(fit, PlateMargin, PlateMargin);
            using var fill = new SolidBrush(plate);

            if (_logoIsBadgeShaped)
            {
                g.FillEllipse(fill, plateRect);
            }
            else
            {
                using var path = AppTheme.RoundedRect(plateRect, AppTheme.CornerRadius);
                g.FillPath(fill, path);
            }
        }

        var scaled = EnsureScaledLogo(fit.Size);

        if (scaled is not null)
        {
            // An explicit destination rather than DrawImageUnscaled, which
            // still scales by the bitmap's DPI against the screen's - on a
            // monitor whose DPI differs from the one the bitmap was stamped
            // with, the logo would come out the wrong size.
            g.DrawImage(scaled, fit);
        }
    }

    /// <summary>
    /// The logo scaled to exactly this size, made once and reused.
    ///
    /// Scaled here, once, with high-quality filtering, rather than asking
    /// DrawImage to shrink the 384px original on every paint: that is
    /// slower, and the default filtering makes a small logo look jagged.
    /// Null if it can't be made - the header then shows plain background
    /// rather than throwing from a paint handler.
    /// </summary>
    private Bitmap? EnsureScaledLogo(Size size)
    {
        if (_logo is null)
        {
            return null;
        }

        if (_logoScaled is not null && _logoScaled.Size == size)
        {
            return _logoScaled;
        }

        _logoScaled?.Dispose();
        _logoScaled = null;

        Bitmap? scaled = null;

        try
        {
            scaled = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(scaled))
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                // Without this the bicubic filter samples past the image's
                // edge and leaves a faint dark fringe along it.
                attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);

                g.DrawImage(_logo, new Rectangle(0, 0, size.Width, size.Height),
                    0, 0, _logo.Width, _logo.Height, GraphicsUnit.Pixel, attributes);
            }

            _logoScaled = scaled;
        }
        catch
        {
            // A bitmap made but not drawn into is still a GDI handle.
            scaled?.Dispose();
            _logoScaled = null;
        }

        return _logoScaled;
    }

    private void DrawPlainTitle(Graphics g)
    {
        using (var back = new SolidBrush(AppTheme.SidebarBackground))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        // On the rail the name has nowhere to go, so it becomes its initials
        // in the middle: "HEATCALLER" given the rail's width came out as two
        // letters and an ellipsis, which reads as something broken rather
        // than as a sidebar somebody narrowed on purpose.
        if (_compact)
        {
            using var markFont = new Font(AppTheme.NavFont.FontFamily, 13.5F, FontStyle.Bold);

            TextRenderer.DrawText(g, Initials(), markFont, ClientRectangle, AppTheme.SidebarText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            return;
        }

        using var font = new Font(AppTheme.NavFont.FontFamily, 12.5F, FontStyle.Bold);
        var textRect = new Rectangle(16, 0, Math.Max(10, Width - 20), Height);

        // One line, like the synthwave header and like the startup screen.
        // Two lines here and one there was the other half of why the two
        // titles never looked like the same app.
        TextRenderer.DrawText(g, $"{_titleLine1} {_titleLine2}".Trim(), font, textRect,
            AppTheme.SidebarText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// The mark drawn in place of the title on the rail: one letter from each
    /// of the first two words of the title, or the first two letters when the
    /// title is a single word - "HEATCALLER" is one word, and the app's own
    /// mark is "HC", not "H".
    /// </summary>
    private string Initials()
    {
        var words = $"{_titleLine1} {_titleLine2}".Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return string.Empty;
        }

        if (words.Length == 1)
        {
            var only = words[0];

            return (only.Length >= 2 ? only[..2] : only).ToUpperInvariant();
        }

        return $"{words[0][0]}{words[1][0]}".ToUpperInvariant();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DropCache();
            DropLogo();
        }
        base.Dispose(disposing);
    }
}
