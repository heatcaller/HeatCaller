namespace HeatCaller;

/// <summary>
/// The race clock: elapsed time, what's remaining, and progress toward the
/// limit, all in one custom-painted panel.
///
/// This replaces two stacked group boxes that each showed part of the
/// picture - an "Elapsed" box and a "Remaining" box - which was redundant
/// and gave the most important number on the page the least presence.
/// Drawing it directly means the progress ring, the flag colour and the
/// paused state can all be expressed at once.
///
/// Everything is painted rather than composed from labels, so it needs the
/// full double-buffering style set - and all loops are bounded, since this
/// runs on the paint thread.
/// </summary>
public sealed class RaceClockPanel : Control, ISlidesAsPicture
{
    private readonly RaceTiming _race;

    public RaceClockPanel(RaceTiming race)
    {
        _race = race;

        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        DoubleBuffered = true;
        TabStop = false;
    }

    /// <summary>The warm-up's own colour - the warm-up flag's blue (FlagText.WarmUpColour), as the Warm-up button wears.</summary>
    private static readonly Color WarmUpColor = FlagText.WarmUpColour;

    /// <summary>Colour for the current flag, used for the ring and the glow.</summary>
    private Color FlagColor => _race.IsWarmingUp ? WarmUpColor : _race.Flag switch
    {
        FlagState.Green => ColorTranslator.FromHtml("#3DDC84"),
        FlagState.Yellow => ColorTranslator.FromHtml("#FFD147"),
        FlagState.Red => ColorTranslator.FromHtml("#FF5C5C"),
        FlagState.Checkered => AppTheme.TextDark,
        _ => AppTheme.TextMuted,
    };

    /// <summary>
    /// How far through the race we are, 0-1, or null when there's no limit
    /// to measure against. A lap limit is measured on the leader, since
    /// that's what actually ends the race.
    /// </summary>
    private float? Progress
    {
        get
        {
            // During a warm-up the ring shows the warm-up used up.
            if (_race.IsWarmingUp && _race.WarmUpLength.TotalMilliseconds > 0)
            {
                var used = _race.WarmUpLength - _race.WarmUpRemaining;
                return Math.Clamp((float)(used.TotalMilliseconds / _race.WarmUpLength.TotalMilliseconds), 0f, 1f);
            }

            if (_race.TimeLimit is { } limit && limit.TotalMilliseconds > 0)
            {
                return Math.Clamp((float)(_race.ElapsedRaceTime.TotalMilliseconds / limit.TotalMilliseconds), 0f, 1f);
            }

            if (_race.LapLimit is { } laps && laps > 0)
            {
                // The real leader - a ghost ahead cannot finish the race.
                var leader = _race.RaceLeader();
                return leader is null ? 0f : Math.Clamp(leader.Laps / (float)laps, 0f, 1f);
            }

            return null;
        }
    }

    /// <summary>While Race Control's fold slides the clock to its new size, the picture it shows scaled - see ISlidesAsPicture.</summary>
    public Bitmap? SlidePicture { get; set; }

    /// <summary>The clock as it draws itself now, at its size now - without the fold button inside it.</summary>
    public Bitmap TakeSlidePicture()
    {
        var picture = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

        using (var g = Graphics.FromImage(picture))
        {
            OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, picture.Size)));
        }

        return picture;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (SlidePicture is { } picture)
        {
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            e.Graphics.DrawImage(picture, ClientRectangle);
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var synth = AppTheme.CurrentMode == AppTheme.Mode.Synthwave;
        var accent = FlagColor;

        DrawBackdrop(g, synth);

        var ringRect = RingBounds();
        DrawProgressRing(g, ringRect, accent, synth);
        DrawElapsed(g, ringRect, accent, synth);
        DrawRemaining(g, accent);
        DrawStateStrip(g, accent);
    }

    private Rectangle RingBounds()
    {
        // Square, centred horizontally, leaving room for the state strip at
        // the bottom and the remaining text beneath the ring. The room is at
        // the screen's DPI, as the point-size text in it is.
        var size = Math.Max(UiLayout.AtDpi(40), Math.Min(Width - UiLayout.AtDpi(88), Height - UiLayout.AtDpi(58)));
        return new Rectangle((Width - size) / 2, UiLayout.AtDpi(10), size, size);
    }

    private void DrawBackdrop(Graphics g, bool synth)
    {
        // The clock is a card like every other panel now, so it has to be
        // cleared to the page canvas first - otherwise the rounded corners
        // show the old square fill through them.
        using (var canvas = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            g.FillRectangle(canvas, ClientRectangle);
        }

        if (!synth)
        {
            AppTheme.DrawCard(g, new RectangleF(0, 0, Width, Height));
            return;
        }

        using (var bg = new SolidBrush(ColorTranslator.FromHtml("#1A0B33")))
        using (var shape = AppTheme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), AppTheme.CornerRadius))
        {
            g.FillPath(bg, shape);
        }

        // Synthwave: horizontal scanlines and a neon frame. Bounded loop -
        // this is paint-thread code.
        using (var clip = AppTheme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), AppTheme.CornerRadius))
        using (var scan = new Pen(Color.FromArgb(18, AppTheme.SynthCyan)))
        {
            var saved = g.Save();
            g.SetClip(clip);

            for (var y = 0; y < Height; y += 3)
            {
                g.DrawLine(scan, 0, y, Width, y);
            }

            g.Restore(saved);
        }

        using var outline = AppTheme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), AppTheme.CornerRadius);
        using var glow = new Pen(Color.FromArgb(70, AppTheme.SynthCyan), 3f);
        g.DrawPath(glow, outline);
        using var edge = new Pen(AppTheme.SynthCyan);
        g.DrawPath(edge, outline);
    }

    /// <summary>
    /// Largest font of the mono family whose rendering of <paramref name="text"/>
    /// fits <paramref name="available"/> pixels wide. Bounded loop - this runs
    /// on the paint thread, four times a second.
    /// </summary>
    private Font FitFont(Graphics g, string text, float available, float start)
    {
        // The size found last time, while nothing it depends on has moved.
        // The family is monospaced, so a time of the same length measures
        // the same whatever its digits - and without this the search ran on
        // every paint, four times a second on each clock: dozens of fonts
        // created, measured and thrown away each time, on the race screen.
        if (_fitCache is { } cached
            && cached.Length == text.Length
            && cached.Available == available
            && cached.Start == start)
        {
            return FontLibrary.Create(AppTheme.MonoFamily, cached.Points, FontStyle.Bold);
        }

        // The same sizes as ever - start, a point less, and so on down to 12 -
        // but halved towards the answer rather than stepped through one by
        // one (15 Nov 2026): while Race Control's fold slides the clock to a
        // new size, every frame is a new width, and stepping down a point at
        // a time measured a dozen fonts or more per frame. Wider never fits
        // where narrower does not, so the halving finds the same size.
        bool Fits(float points)
        {
            using var candidate = FontLibrary.Create(AppTheme.MonoFamily, points, FontStyle.Bold);
            return g.MeasureString(text, candidate).Width <= available;
        }

        // Steps below start: 0 is start itself; steps is the first past 12.
        var steps = Math.Max(0, (int)Math.Ceiling(start - 12f));
        int low = 0, high = steps;

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (Fits(start - middle))
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        var found = low < steps ? start - low : 12f;

        _fitCache = (text.Length, available, start, found);
        return FontLibrary.Create(AppTheme.MonoFamily, found, FontStyle.Bold);
    }

    /// <summary>The last size FitFont settled on, and what it was fitted to.</summary>
    private (int Length, float Available, float Start, float Points)? _fitCache;

    private void DrawProgressRing(Graphics g, Rectangle rect, Color accent, bool synth)
    {
        // At the screen's DPI, like the ring's room in RingBounds.
        float thickness = UiLayout.AtDpi(7);
        var inset = rect;
        inset.Inflate(-(int)thickness, -(int)thickness);

        // Track.
        using (var track = new Pen(Color.FromArgb(synth ? 60 : 40, AppTheme.TextMuted), thickness))
        {
            track.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            track.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            g.DrawArc(track, inset, 135, 270);
        }

        var progress = Progress;
        if (progress is null)
        {
            // No limit set, so there's nothing to fill toward. Tick marks
            // instead, so the ring doesn't just look broken. At the screen's
            // DPI, as the ring is, so they still stand proud of its track.
            using var ticks = new Pen(Color.FromArgb(90, AppTheme.TextMuted), UiLayout.AtDpi(2));
            var tick = UiLayout.AtDpi(4);
            var cx = inset.Left + inset.Width / 2f;
            var cy = inset.Top + inset.Height / 2f;
            var r = inset.Width / 2f;
            for (var i = 0; i <= 8; i++)
            {
                var a = (135 + 270 * i / 8.0) * Math.PI / 180.0;
                g.DrawLine(ticks,
                    (float)(cx + Math.Cos(a) * (r - tick)), (float)(cy + Math.Sin(a) * (r - tick)),
                    (float)(cx + Math.Cos(a) * (r + tick)), (float)(cy + Math.Sin(a) * (r + tick)));
            }
            return;
        }

        var sweep = 270f * progress.Value;
        if (sweep <= 0.5f)
        {
            return;
        }

        // Glow pass underneath, then the solid arc over it.
        if (synth)
        {
            using var halo = new Pen(Color.FromArgb(80, accent), thickness + UiLayout.AtDpi(7));
            halo.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            halo.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            g.DrawArc(halo, inset, 135, sweep);
        }

        using var arc = new Pen(accent, thickness);
        arc.StartCap = System.Drawing.Drawing2D.LineCap.Round;
        arc.EndCap = System.Drawing.Drawing2D.LineCap.Round;
        g.DrawArc(arc, inset, 135, sweep);
    }

    private void DrawElapsed(Graphics g, Rectangle ring, Color accent, bool synth)
    {
        var warmingUp = _race.IsWarmingUp;

        // The warm-up counts DOWN - how long is left is the only thing
        // anyone at the track wants from the clock then.
        var elapsed = warmingUp ? _race.WarmUpRemaining : _race.ElapsedRaceTime;
        var text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

        // Sized against the PANEL, not the ring - the time is the thing you
        // read from across the room, and the arc behind it is only context.
        // It is deliberately allowed to grow wider than the ring and overhang
        // it on both sides; the arc is open at the bottom and thin enough
        // that the digits crossing it read as layering rather than collision.
        //
        // Measured down to fit rather than set by a ratio, because the
        // string changes width between "9:59" and "59:59" and a fixed ratio
        // would either clip the long one or waste the short one.
        //
        // The size to start from is a share of the width in pixels, taken
        // as points - PointsForPixels keeps it the same share on a screen
        // the app draws at 150%, rather than starting half as big again and
        // stepping down from there. The 16 is a point size of its own.
        var available = Width * 0.88f;
        var font = FitFont(g, text, available, Math.Max(16f, UiLayout.PointsForPixels(Width * 0.40f, g)));
        var measured = g.MeasureString(text, font);
        var x = ring.Left + (ring.Width - measured.Width) / 2f;
        var y = ring.Top + (ring.Height - measured.Height) / 2f - ring.Height * 0.045f;

        if (synth)
        {
            // Offset copies in the theme's two accents - a cheap chromatic
            // shift that reads as neon without needing real bloom. The shift
            // is at the screen's DPI, as the digits it offsets are.
            var shift = 1.6f * UiLayout.DpiScale;
            using var pink = new SolidBrush(Color.FromArgb(110, AppTheme.Primary));
            using var cyan = new SolidBrush(Color.FromArgb(110, AppTheme.SynthCyan));
            g.DrawString(text, font, pink, x - shift, y);
            g.DrawString(text, font, cyan, x + shift, y);
        }

        using var main = new SolidBrush(synth ? Color.White : AppTheme.TextDark);
        g.DrawString(text, font, main, x, y);

        font.Dispose();

        // Caption under the time, inside the ring.
        var caption = warmingUp ? "WARM-UP"
            : _race.Flag == FlagState.Red ? "PAUSED"
            : "ELAPSED";
        // A share of the ring's width in pixels, taken as points - see the
        // time's own size above. The 6.5 is a point size of its own, the
        // smallest the caption is still read at.
        using var capFont = FontLibrary.Create(AppTheme.DisplayFamily, Math.Max(6.5f, UiLayout.PointsForPixels(ring.Width * 0.055f, g)), FontStyle.Bold);
        var capSize = g.MeasureString(AppTheme.Spaced(caption), capFont);
        using var capBrush = new SolidBrush(_race.Flag == FlagState.Red ? accent : AppTheme.TextMuted);
        g.DrawString(AppTheme.Spaced(caption), capFont, capBrush,
            ring.Left + (ring.Width - capSize.Width) / 2f,
            y + measured.Height - ring.Height * 0.02f);
    }

    private void DrawRemaining(Graphics g, Color accent)
    {
        // The big number is already the warm-up's time left, so this line
        // says what the time is for instead of repeating it.
        var text = _race.IsWarmingUp ? "FREE LAPS - NOT COUNTED" : _race.GetCountdownText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Its band above the state strip is at the screen's DPI, as the
        // point-size text in it is.
        using var font = FontLibrary.Create(AppTheme.DisplayFamily, 11F, FontStyle.Bold);
        var size = g.MeasureString(text, font);
        using var brush = new SolidBrush(accent);
        g.DrawString(text, font, brush, (Width - size.Width) / 2f, Height - UiLayout.AtDpi(46));
    }

    /// <summary>State and flag along the bottom, so the clock also answers "what's happening right now".</summary>
    private void DrawStateStrip(Graphics g, Color accent)
    {
        var label = _race.IsWarmingUp ? "WARM-UP"
            : _race.State == RaceState.Racing
            ? FlagText.Shouted(_race.Flag)
            : _race.State.ToString().ToUpperInvariant();

        // Sized to the text rather than spanning the panel: a full-width
        // tinted bar competed with the ring above it for the eye, and the
        // flag state is a chip, not a header. The chip's margins and height
        // are at the screen's DPI, as the point-size text in it is.
        using var font = FontLibrary.Create(AppTheme.DisplayFamily, 8.5F, FontStyle.Bold);
        var spaced = AppTheme.Spaced(label);
        var measured = TextRenderer.MeasureText(spaced, font);
        var pillWidth = Math.Min(Width - UiLayout.AtDpi(16), measured.Width + UiLayout.AtDpi(26));
        var pill = new RectangleF((Width - pillWidth) / 2f, Height - UiLayout.AtDpi(28), pillWidth, UiLayout.AtDpi(22));

        AppTheme.DrawPill(g, pill, Color.FromArgb(52, accent), string.Empty, accent);

        TextRenderer.DrawText(g, spaced, font, Rectangle.Round(pill),
            AppTheme.CurrentMode == AppTheme.Mode.Synthwave ? Color.White : AppTheme.TextDark,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
