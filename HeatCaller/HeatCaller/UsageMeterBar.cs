namespace HeatCaller;

/// <summary>
/// How much of one monthly allowance is used, as a bar: a rounded track
/// and the used share of it filled - in the primary colour while there is
/// plenty left, amber from 80% (MeterLevel.Watch), red once it is all gone
/// (Spent), and an empty grey track when the figure can't be read
/// (Unknown), so a bar is never drawn for a number nobody has.
///
/// Drawn rather than a ProgressBar: the Windows one is green whatever the
/// theme, animates on its own and can't say "used up" in red. The same
/// track-and-fill as Next Steps' ready bar (TodayControl.PaintReadyBar).
///
/// As tall as <see cref="BarHeight"/> at the text size in use, and as
/// wide as it is given - it sizes itself for the pieces (Section,
/// UsageLine) that ask, so it never needs a height set by hand.
/// </summary>
internal sealed class UsageMeterBar : Control
{
    /// <summary>The bar's height at the normal text size.</summary>
    internal const int BarHeight = 8;

    private double _fraction;
    private MeterLevel _level = MeterLevel.Unknown;

    public UsageMeterBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw
                 | ControlStyles.SupportsTransparentBackColor, true);

        DoubleBuffered = true;
        TabStop = false;
        AutoSize = true;
        Margin = Padding.Empty;

        // The theme pass leaves it alone: it paints whatever it sits on.
        Tag = AppTheme.KeepBackColor;
    }

    /// <summary>Shows a meter's share and level. Nothing is drawn but the track for a meter that can't be read.</summary>
    public void Show(double fraction, MeterLevel level)
    {
        // A meter past its allowance (a race that ran over) is a full bar,
        // not one drawn off the end of its track.
        var share = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);

        if (share == _fraction && level == _level)
        {
            return;
        }

        _fraction = share;
        _level = level;
        Invalidate();
    }

    /// <summary>What is shown now - for the tests.</summary>
    internal (double Fraction, MeterLevel Level) Showing => (_fraction, _level);

    /// <summary>The fill's colour for a level - the same rule the sidebar chip colours its text by (UsageChip).</summary>
    internal static Color FillFor(MeterLevel level) => level switch
    {
        MeterLevel.Spent => AppTheme.Danger,
        MeterLevel.Watch => AppTheme.Warning,
        MeterLevel.Ok => AppTheme.Primary,
        _ => AppTheme.TextMuted,
    };

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 1 and < int.MaxValue / 4 ? proposedSize.Width : UiLayout.AtTextSize(UiLayout.FieldWidth);

        return new Size(width, UiLayout.AtTextSize(BarHeight));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? AppTheme.Surface);

        if (Width < 2 || Height < 2)
        {
            return;
        }

        var track = new RectangleF(0, 0, Width - 1, Height - 1);
        var radius = Math.Max(1, Height / 2);

        // The track in the page's colour - a groove in the card it sits on,
        // as the ready bar's is.
        using (var path = AppTheme.RoundedRect(track, radius))
        using (var brush = new SolidBrush(AppTheme.Background))
        {
            g.FillPath(brush, path);
        }

        if (_level == MeterLevel.Unknown || _fraction <= 0)
        {
            return;
        }

        // Never narrower than it is tall, or the rounded ends meet and a
        // month barely started draws as nothing.
        var fill = new RectangleF(0, 0, Math.Max(Height, (float)(track.Width * _fraction)), track.Height);

        using (var path = AppTheme.RoundedRect(fill, radius))
        using (var brush = new SolidBrush(FillFor(_level)))
        {
            g.FillPath(brush, path);
        }
    }
}
