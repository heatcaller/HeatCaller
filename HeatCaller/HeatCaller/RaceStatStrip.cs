namespace HeatCaller;

/// <summary>
/// The two numbers the clock doesn't already answer: how many laps the
/// leader has, and the quickest lap anyone has set.
///
/// They used to be available only by reading them out of the leaderboard,
/// which means finding the right row first - fine when you are sat in front
/// of it, useless when you glance up mid-race. Painted as tiles rather than
/// built from labels so the label/value pairing, the spacing and the
/// rounding all come from <see cref="AppTheme"/> and stay in step with the
/// rest of the page when the theme changes.
///
/// Reads the session directly, like <see cref="RaceClockPanel"/> does, so
/// there is nothing to push into it - the caller just invalidates it on the
/// same tick as the clock.
/// </summary>
public sealed class RaceStatStrip : Control
{
    private readonly RaceControlSession _race;

    public RaceStatStrip(RaceControlSession race)
    {
        _race = race;

        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        DoubleBuffered = true;
        TabStop = false;
        Height = WantedHeight;
    }

    /// <summary>
    /// The small caption over each value. It follows the text size, and the
    /// strip grows with it rather than drawing the caption into a strip too
    /// short for it - at 150% the captions were cut through the middle.
    /// Fourteen at the normal text size, as it always was.
    /// </summary>
    private static int CaptionHeight => Math.Max(14, AppTheme.MicroFont.Height - 2);

    /// <summary>The caption, the 24px the value has always had, and room round them - about fifty at the normal text size.</summary>
    private static int WantedHeight => CaptionHeight + 36;

    /// <summary>A new text size. The window's font change reaches the strip, which has none of its own, and is the cue.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        if (Height != WantedHeight)
        {
            Height = WantedHeight;
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var canvas = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            g.FillRectangle(canvas, ClientRectangle);
        }

        if (Width < 40 || Height < 24)
        {
            return;
        }

        var ordered = _race.GetOrderedStandings().ToList();

        // The real leader, as the clock's "laps to go" and its progress ring
        // count from - a ghost out in front cannot end the race, and showing
        // its laps here put a number beside the countdown that disagreed
        // with it.
        var leader = _race.RaceLeader();

        // Fastest of the session, not of the leader - the quickest lap is
        // frequently set by someone two positions back, and showing the
        // leader's best here would quietly contradict the leaderboard's
        // own best-lap highlight.
        //
        // Ghosts out while a real car is on track, as the race's own fastest
        // lap leaves them out (Ghosts.FastestLapMs).
        var fastestMs = Ghosts.FastestLapMs(ordered);

        const int gap = 8;
        var tileWidth = (Width - gap) / 2f;

        DrawTile(g, new RectangleF(0, 0, tileWidth, Height), "Laps",
            leader is null ? "-" : leader.Laps.ToString(), AppTheme.TextDark);

        DrawTile(g, new RectangleF(tileWidth + gap, 0, Width - tileWidth - gap, Height), "Fastest",
            fastestMs > 0 ? $"{fastestMs / 1000.0:0.000}" : "-", AppTheme.Primary);
    }

    /// <summary>
    /// Like <c>AppTheme.DrawStatTile</c>, but the value font is measured
    /// down to fit. These tiles are about a hundred pixels wide, and a lap
    /// time at the shared 26pt stat size would be ellipsised into "4.5...".
    /// </summary>
    private static void DrawTile(Graphics g, RectangleF bounds, string label, string value, Color valueColor)
    {
        AppTheme.DrawCard(g, bounds);

        using var labelBrush = new SolidBrush(AppTheme.TextMuted);
        using var valueBrush = new SolidBrush(valueColor);
        using var centred = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        var labelRect = new RectangleF(bounds.Left + 4, bounds.Top + 7, bounds.Width - 8, CaptionHeight);
        g.DrawString(AppTheme.Spaced(label.ToUpperInvariant()), AppTheme.MicroFont, labelBrush, labelRect, centred);

        var valueRect = new RectangleF(bounds.Left + 4, labelRect.Bottom - 2, bounds.Width - 8, bounds.Height - labelRect.Height - 12);

        // Step down in whole points until it fits. Bounded loop - this runs
        // on the paint thread, and a 250ms refresh timer drives it.
        var available = bounds.Width - 12;
        for (var size = 20f; size >= 10f; size -= 1f)
        {
            using var font = FontLibrary.Create(AppTheme.DisplayFamily, size, FontStyle.Bold);
            if (g.MeasureString(value, font).Width <= available || size <= 10f)
            {
                g.DrawString(value, font, valueBrush, valueRect, centred);
                return;
            }
        }
    }
}
