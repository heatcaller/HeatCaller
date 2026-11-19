using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// What a race-night list says when it has nothing to show (18 Nov 2026,
/// dialog jazz-up, third batch): a soft panel with a dashed edge where the
/// list would be, a heading in bold and, under it, what fills the list - as
/// the lists on the pages say it. The qualifying projection's "nothing
/// running" and the qualifying standings' "none yet".
///
/// A Label, so it is placed, themed and measured as the plain label it took
/// the place of was: the body is its Text, in its own Font; the heading is
/// drawn over it in the bold face. LeastHeight is how tall the two are at a
/// width, inside the panel's room.
/// </summary>
internal sealed class ListEmptyNote : Label
{
    /// <summary>Room between the panel's edge and the words, at the text size in use.</summary>
    private static int Room => UiLayout.AtTextSize(16);

    public ListEmptyNote()
    {
        AutoSize = false;
        DoubleBuffered = true;
        ResizeRedraw = true;
        TextAlign = ContentAlignment.MiddleCenter;
    }

    /// <summary>The line in bold over the body.</summary>
    public string Heading { get; set; } = string.Empty;

    /// <summary>How tall the heading and the body are wrapped to <paramref name="width"/>, with the panel's room round them.</summary>
    public int LeastHeight(int width)
    {
        var inside = Math.Max(1, width - Room * 2);

        return Room * 2 + HeadingHeight(inside) + UiLayout.HintGap + BodyHeight(inside);
    }

    private int HeadingHeight(int width) => string.IsNullOrEmpty(Heading)
        ? 0
        : TextRenderer.MeasureText(Heading, AppTheme.BoldFont, new Size(width, int.MaxValue), Flags).Height;

    private int BodyHeight(int width) =>
        TextRenderer.MeasureText(Text, Font, new Size(width, int.MaxValue), Flags).Height;

    private const TextFormatFlags Flags =
        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            g.FillRectangle(behind, ClientRectangle);
        }

        var panel = new RectangleF(1, 1, Math.Max(2, Width - 3), Math.Max(2, Height - 3));

        using (var path = AppTheme.RoundedRect(panel, 8))
        // Half way between the page and a card, so it reads as a place
        // waiting to be filled rather than a card with nothing on it; the
        // dashed edge a little darker than a card's, to show up on the
        // page's own grey.
        using (var fill = new SolidBrush(Blend(AppTheme.Surface, AppTheme.Background, 0.5f)))
        using (var pen = new Pen(Blend(AppTheme.Border, AppTheme.TextMuted, 0.35f)) { DashStyle = DashStyle.Dash })
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        // The heading and the body as one block, in the middle of the panel.
        var inside = Math.Max(1, Width - Room * 2);
        var heading = HeadingHeight(inside);
        var body = BodyHeight(inside);
        var gap = heading > 0 ? UiLayout.HintGap : 0;
        var top = Math.Max(Room, (Height - heading - gap - body) / 2);

        if (heading > 0)
        {
            TextRenderer.DrawText(g, Heading, AppTheme.BoldFont, new Rectangle(Room, top, inside, heading), AppTheme.TextDark, Flags);
        }

        TextRenderer.DrawText(g, Text, Font, new Rectangle(Room, top + heading + gap, inside, body), AppTheme.TextMuted, Flags);
    }

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t),
        (int)Math.Round(a.G + (b.G - a.G) * t),
        (int)Math.Round(a.B + (b.B - a.B) * t));
}

/// <summary>
/// The tiles over a race-night list, in the faces the kit gives them (as the
/// other dialogs do theirs): the title face for the value, the micro one for
/// the caption.
/// </summary>
internal static class RaceNightTiles
{
    public static StatTile[] Make(int count)
    {
        var tiles = new StatTile[count];

        for (var i = 0; i < count; i++)
        {
            tiles[i] = new StatTile();
            tiles[i].Value.Font = AppTheme.TitleFont;
            tiles[i].Caption.Font = AppTheme.MicroFont;
        }

        return tiles;
    }

    /// <summary>
    /// A short bar down the left edge of a row, in <paramref name="colour"/> -
    /// the lap breakdown's mark for the lap that matters, here for a line
    /// that is still moving - so it stands out on a screen washed out by the
    /// pit lights.
    /// </summary>
    public static void MarkRow(Graphics g, Rectangle row, Color colour)
    {
        var bar = new Rectangle(row.Left + 1, row.Top + UiLayout.AtTextSize(4), Math.Max(3, UiLayout.AtTextSize(3)),
            Math.Max(2, row.Height - UiLayout.AtTextSize(8)));

        using var fill = new SolidBrush(colour);
        g.FillRectangle(fill, bar);
    }
}
