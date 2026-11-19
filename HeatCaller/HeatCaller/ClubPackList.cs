using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Club Batteries' list (18 Nov 2026, dialog jazz-up): one card per club
/// pack - a round badge with its type, its name, a line under it of the
/// sessions run on it, whose car it is in and its notes, and a RETIRED chip
/// on one taken out of use. In place of the five-column grid it was. Drawn
/// as Ghost Cars' list is (GhostList): an owner-drawn ListBox, so the
/// keyboard, the selection and the scrolling are Windows' own, the rows
/// drawn from the theme at paint time. With no club packs, one muted row
/// says what fills it.
/// </summary>
internal sealed class ClubPackList : ListBox
{
    public ClubPackList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        Tag = AppTheme.KeepBackColor;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.TextDark;

        // The picked row draws its own outline - see VenueList.
        InputOutline.Exclude(this);
        Remeasure();
    }

    /// <summary>The rows are as tall as a name and a line under it, at the text size in use.</summary>
    public void Remeasure()
    {
        ItemHeight = Math.Max(UiLayout.AtDpi(52), AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(22));
        Invalidate();
    }

    /// <summary>One row: a pack, or (Pack null) the line saying there are none yet.</summary>
    public sealed record Row(BatteryPack? Pack, string Name, string Badge, string Line, bool Retired)
    {
        public override string ToString() => Name;
    }

    /// <summary>Drawn off-screen and put up in one go - see VenueList.OnDrawItem.</summary>
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0)
        {
            return;
        }

        using var buffer = BufferedGraphicsManager.Current.Allocate(e.Graphics, e.Bounds);
        DrawRow(buffer.Graphics, e);
        buffer.Render(e.Graphics);
    }

    private void DrawRow(Graphics g, DrawItemEventArgs e)
    {
        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillRectangle(back, e.Bounds);
        }

        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not Row pack)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        const TextFormatFlags keep = TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
        const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | keep;

        var row = Rectangle.Inflate(e.Bounds, -UiLayout.AtDpi(2), -UiLayout.AtDpi(3));
        var pad = UiLayout.AtDpi(8);
        var textHeight = AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(2);
        var y = row.Y + (row.Height - textHeight) / 2;

        // Nothing to list: what fills it, in the muted ink, no badge.
        if (pack.Pack is null)
        {
            var x0 = row.X + pad;
            var w0 = Math.Max(1, row.Width - pad * 2);
            TextRenderer.DrawText(g, pack.Name, AppTheme.BoldFont, new Rectangle(x0, y, w0, AppTheme.BoldFont.Height), AppTheme.TextMuted, line);
            TextRenderer.DrawText(g, pack.Line, AppTheme.SmallFont,
                new Rectangle(x0, y + AppTheme.BoldFont.Height + UiLayout.AtDpi(2), w0, AppTheme.SmallFont.Height), AppTheme.TextMuted, line);
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;

        if (selected)
        {
            using var path = AppTheme.RoundedRect(new RectangleF(row.X + 0.5f, row.Y + 0.5f, row.Width - 1f, row.Height - 1f), UiLayout.AtDpi(8));
            using var fill = new SolidBrush(AppTheme.SecondaryButton);
            g.FillPath(fill, path);
            using var pen = new Pen(AppTheme.Primary, Math.Max(1f, UiLayout.AtDpi(1)));
            g.DrawPath(pen, path);
        }

        // The badge: the pack's type, short, in a ring of the look's colour -
        // paler for a retired pack.
        var size = row.Height - pad * 2;
        var badge = new RectangleF(row.X + pad, row.Y + pad, size, size);
        var ink = pack.Retired ? AppTheme.TextMuted : AppTheme.Primary;

        using (var fill = new SolidBrush(RosterInk.Blend(ink, AppTheme.Surface, selected ? 0.55f : 0.8f)))
        {
            g.FillEllipse(fill, badge);
        }

        using (var ring = new Pen(ink, Math.Max(1f, UiLayout.AtDpi(selected ? 2 : 1))))
        {
            g.DrawEllipse(ring, badge.X + 0.5f, badge.Y + 0.5f, badge.Width - 1f, badge.Height - 1f);
        }

        TextRenderer.DrawText(g, pack.Badge, AppTheme.MicroFont, Rectangle.Round(badge), selected ? AppTheme.TextDark : ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | keep);

        var x = (int)badge.Right + UiLayout.AtDpi(10);
        var width = Math.Max(1, row.Right - x - pad);
        var chipWidth = 0;

        if (pack.Retired)
        {
            const string chipText = "RETIRED";
            var measured = TextRenderer.MeasureText(g, chipText, AppTheme.MicroFont, Size.Empty, TextFormatFlags.NoPadding);
            chipWidth = measured.Width + UiLayout.AtDpi(16);
            var chip = new RectangleF(row.Right - pad - chipWidth, y, chipWidth, AppTheme.BoldFont.Height);

            using (var path = AppTheme.RoundedRect(chip, (int)(chip.Height / 2)))
            using (var fill = new SolidBrush(RosterInk.Blend(AppTheme.TextMuted, AppTheme.Surface, 0.82f)))
            {
                g.FillPath(fill, path);
            }

            TextRenderer.DrawText(g, chipText, AppTheme.MicroFont, Rectangle.Round(chip), AppTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | keep);
        }

        TextRenderer.DrawText(g, pack.Name, AppTheme.BoldFont,
            new Rectangle(x, y, Math.Max(1, width - (chipWidth > 0 ? chipWidth + UiLayout.AtDpi(6) : 0)), AppTheme.BoldFont.Height),
            pack.Retired ? AppTheme.TextMuted : AppTheme.TextDark, line);

        TextRenderer.DrawText(g, pack.Line, AppTheme.SmallFont,
            new Rectangle(x, y + AppTheme.BoldFont.Height + UiLayout.AtDpi(2), width, AppTheme.SmallFont.Height), AppTheme.TextMuted, line);
    }

    /// <summary>A pack's type in the badge: the first word, at most four letters - "LiPo", "NiMH", "LiHV".</summary>
    internal static string BadgeOf(string chemistry)
    {
        var word = (chemistry ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(word) ? "?" : word.Length > 4 ? word[..4] : word;
    }

    /// <summary>No flashing white between rows as the list redraws.</summary>
    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var back = new SolidBrush(AppTheme.Surface);
        pevent.Graphics.FillRectangle(back, ClientRectangle);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}
