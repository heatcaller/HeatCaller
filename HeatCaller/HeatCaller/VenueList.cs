using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Venues' list (14 Nov 2026, page jazz-up): one card per venue - a round
/// badge with its initials, its name, a line of what it sets under the
/// name (decoder, soundscape, track), and "Applied" on the venue applied
/// last. Drawn as Track Layouts' list is (LayoutList): an owner-drawn
/// ListBox, so the keyboard, the selection and the scrolling are Windows'
/// own, the rows drawn from the theme at paint time.
/// </summary>
internal sealed class VenueList : ListBox
{
    public VenueList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        Tag = AppTheme.KeepBackColor;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.TextDark;

        // The picked row draws its own outline - see LayoutList.
        InputOutline.Exclude(this);
        Remeasure();
    }

    /// <summary>The venue applied last, which wears the "Applied" chip. Null for none.</summary>
    public string? AppliedId
    {
        get => _appliedId;
        set
        {
            if (_appliedId != value)
            {
                _appliedId = value;
                Invalidate();
            }
        }
    }

    private string? _appliedId;

    /// <summary>The text size changed: the rows are as tall as a name and a line under it.</summary>
    public void Remeasure()
    {
        ItemHeight = Math.Max(UiLayout.AtDpi(52), AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(22));
        Invalidate();
    }

    /// <summary>The row's venue, and the line of what it sets - asked of the item at paint time.</summary>
    public interface IVenueRow
    {
        string Id { get; }
        string Name { get; }
        string Line { get; }
    }

    /// <summary>Drawn off-screen and put up in one go - see LayoutList.OnDrawItem.</summary>
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
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

        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not IVenueRow venue)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var selected = (e.State & DrawItemState.Selected) != 0;
        var row = Rectangle.Inflate(e.Bounds, -UiLayout.AtDpi(2), -UiLayout.AtDpi(3));
        var radius = UiLayout.AtDpi(8);

        if (selected)
        {
            using var path = AppTheme.RoundedRect(new RectangleF(row.X + 0.5f, row.Y + 0.5f, row.Width - 1f, row.Height - 1f), radius);
            using var fill = new SolidBrush(AppTheme.SecondaryButton);
            g.FillPath(fill, path);
            using var pen = new Pen(AppTheme.Primary, Math.Max(1f, UiLayout.AtDpi(1)));
            g.DrawPath(pen, path);
        }

        // TextRenderer ignores the buffer's translate unless told - see LayoutList.
        const TextFormatFlags keep = TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
        const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | keep;

        // The badge: the venue's initials in a ring of the look's colour.
        var pad = UiLayout.AtDpi(8);
        var size = row.Height - pad * 2;
        var badge = new RectangleF(row.X + pad, row.Y + pad, size, size);

        using (var fill = new SolidBrush(RosterInk.Blend(AppTheme.Primary, AppTheme.Surface, selected ? 0.55f : 0.78f)))
        {
            g.FillEllipse(fill, badge);
        }

        using (var ring = new Pen(AppTheme.Primary, Math.Max(1f, UiLayout.AtDpi(selected ? 2 : 1))))
        {
            g.DrawEllipse(ring, badge.X + 0.5f, badge.Y + 0.5f, badge.Width - 1f, badge.Height - 1f);
        }

        TextRenderer.DrawText(g, Initials(venue.Name), AppTheme.BoldFont, Rectangle.Round(badge), selected ? AppTheme.TextDark : AppTheme.Primary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | keep);

        // The name, "Applied" after it, and what it sets under it.
        var x = (int)badge.Right + UiLayout.AtDpi(10);
        var width = Math.Max(1, row.Right - x - pad);
        var textHeight = AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(2);
        var y = row.Y + (row.Height - textHeight) / 2;

        var chipWidth = 0;

        if (venue.Id == _appliedId)
        {
            const string applied = "APPLIED";
            var measured = TextRenderer.MeasureText(g, applied, AppTheme.MicroFont, Size.Empty, TextFormatFlags.NoPadding);
            chipWidth = measured.Width + UiLayout.AtDpi(16);
            var chip = new RectangleF(row.Right - pad - chipWidth, y, chipWidth, AppTheme.BoldFont.Height);

            using (var path = AppTheme.RoundedRect(chip, (int)(chip.Height / 2)))
            using (var fill = new SolidBrush(RosterInk.Blend(AppTheme.Success, AppTheme.Surface, 0.82f)))
            {
                g.FillPath(fill, path);
            }

            TextRenderer.DrawText(g, applied, AppTheme.MicroFont, Rectangle.Round(chip), AppTheme.Success,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | keep);
        }

        var name = string.IsNullOrWhiteSpace(venue.Name) ? "(unnamed venue)" : venue.Name;

        TextRenderer.DrawText(g, name, AppTheme.BoldFont,
            new Rectangle(x, y, Math.Max(1, width - (chipWidth > 0 ? chipWidth + UiLayout.AtDpi(6) : 0)), AppTheme.BoldFont.Height),
            AppTheme.TextDark, line);

        TextRenderer.DrawText(g, venue.Line, AppTheme.SmallFont,
            new Rectangle(x, y + AppTheme.BoldFont.Height + UiLayout.AtDpi(2), width, AppTheme.SmallFont.Height), AppTheme.TextMuted, line);
    }

    /// <summary>Up to two letters: the first of each of the first two words, or the first two of one word.</summary>
    internal static string Initials(string name)
    {
        var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .ToArray();

        if (words.Length == 0)
        {
            return "?";
        }

        if (words.Length == 1)
        {
            var word = words[0];
            return word.Length >= 2 ? char.ToUpperInvariant(word[0]) + word[1].ToString().ToLowerInvariant() : word.ToUpperInvariant();
        }

        return string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[1][0]));
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
