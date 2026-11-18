using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Sponsors' two lists (17 Nov 2026, page jazz-up): the slides in the
/// order they are shown, and the sponsors the announcer reads. One card
/// per row, as Venues' list draws them (VenueList): a round badge - the
/// slide's place in the order, or the sponsor's initials - a bold title,
/// a muted line under it, and a chip at the end (VIDEO, OFF TONIGHT).
///
/// An owner-drawn ListBox, so the keyboard, the selection, double-click
/// and the scrolling are Windows' own; the items stay whatever the page
/// put in (a slide's file name, a sponsor's row) and are asked how they
/// look at paint time (Describe), so nothing about what is stored changes.
/// </summary>
internal sealed class SponsorCardList : ListBox
{
    /// <summary>How one row looks: its badge, title, the line under it, and a chip (null for none) - warn for one in the warning colour.</summary>
    public readonly record struct Look(string Badge, string Title, string Line, string? Chip, bool Warn);

    /// <summary>Asked for each row as it is drawn: the item and its place in the list.</summary>
    public Func<object, int, Look>? Describe { get; set; }

    public SponsorCardList()
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

    /// <summary>The text size changed: the rows are as tall as a title and a line under it.</summary>
    public void Remeasure()
    {
        var height = Math.Max(UiLayout.AtDpi(48), AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(20));

        if (ItemHeight != height)
        {
            ItemHeight = height;
        }

        Invalidate();
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

        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not { } item)
        {
            return;
        }

        var look = Describe?.Invoke(item, e.Index) ?? new Look((e.Index + 1).ToString(), item.ToString() ?? string.Empty, string.Empty, null, false);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var selected = (e.State & DrawItemState.Selected) != 0;
        var row = Rectangle.Inflate(e.Bounds, -UiLayout.AtDpi(2), -UiLayout.AtDpi(2));
        var radius = UiLayout.AtDpi(8);

        if (selected)
        {
            using var path = AppTheme.RoundedRect(new RectangleF(row.X + 0.5f, row.Y + 0.5f, row.Width - 1f, row.Height - 1f), radius);
            using var fill = new SolidBrush(AppTheme.SecondaryButton);
            g.FillPath(fill, path);
            using var pen = new Pen(AppTheme.Primary, Math.Max(1f, UiLayout.AtDpi(1)));
            g.DrawPath(pen, path);
        }
        else if (e.Index > 0)
        {
            // A hairline between rows, so a list of cards reads as one.
            using var rule = new Pen(RosterInk.Blend(AppTheme.TextMuted, AppTheme.Surface, 0.82f));
            g.DrawLine(rule, row.X + UiLayout.AtDpi(8), e.Bounds.Y, row.Right - UiLayout.AtDpi(8), e.Bounds.Y);
        }

        // TextRenderer ignores the buffer's translate unless told - see LayoutList.
        const TextFormatFlags keep = TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
        const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | keep;
        const TextFormatFlags centred = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | keep;

        // The badge: a ring of the look's colour.
        var pad = UiLayout.AtDpi(7);
        var size = row.Height - pad * 2;
        var badge = new RectangleF(row.X + pad, row.Y + pad, size, size);

        using (var fill = new SolidBrush(RosterInk.Blend(AppTheme.Primary, AppTheme.Surface, selected ? 0.55f : 0.8f)))
        {
            g.FillEllipse(fill, badge);
        }

        using (var ring = new Pen(AppTheme.Primary, Math.Max(1f, UiLayout.AtDpi(selected ? 2 : 1))))
        {
            g.DrawEllipse(ring, badge.X + 0.5f, badge.Y + 0.5f, badge.Width - 1f, badge.Height - 1f);
        }

        TextRenderer.DrawText(g, look.Badge, AppTheme.BoldFont, Rectangle.Round(badge), selected ? AppTheme.TextDark : AppTheme.Primary, centred);

        var x = (int)badge.Right + UiLayout.AtDpi(10);
        var width = Math.Max(1, row.Right - x - pad);
        var textHeight = AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(2);
        var y = row.Y + (row.Height - textHeight) / 2;

        // The chip at the end of the title's line.
        var chipWidth = 0;

        if (!string.IsNullOrEmpty(look.Chip))
        {
            var measured = TextRenderer.MeasureText(g, look.Chip, AppTheme.MicroFont, Size.Empty, TextFormatFlags.NoPadding);
            chipWidth = Math.Min(width / 2, measured.Width + UiLayout.AtDpi(14));
            var chip = new RectangleF(row.Right - pad - chipWidth, y, chipWidth, AppTheme.BoldFont.Height);
            var ink = look.Warn ? AppTheme.Warning : AppTheme.Primary;

            using (var path = AppTheme.RoundedRect(chip, (int)(chip.Height / 2)))
            using (var fill = new SolidBrush(RosterInk.Blend(ink, AppTheme.Surface, 0.82f)))
            {
                g.FillPath(fill, path);
            }

            TextRenderer.DrawText(g, look.Chip, AppTheme.MicroFont, Rectangle.Round(chip), ink, centred | TextFormatFlags.EndEllipsis);
        }

        var titleWidth = Math.Max(1, width - (chipWidth > 0 ? chipWidth + UiLayout.AtDpi(6) : 0));

        TextRenderer.DrawText(g, look.Title, AppTheme.BoldFont, new Rectangle(x, y, titleWidth, AppTheme.BoldFont.Height), AppTheme.TextDark, line);

        TextRenderer.DrawText(g, look.Line, AppTheme.SmallFont,
            new Rectangle(x, y + AppTheme.BoldFont.Height + UiLayout.AtDpi(2), width, AppTheme.SmallFont.Height), AppTheme.TextMuted, line);
    }

    /// <summary>What the list says while it is empty, in a dashed outline where the rows would be. Null or empty for nothing.</summary>
    public string? EmptyText { get; set; }

    /// <summary>No flashing white between rows as the list redraws.</summary>
    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var back = new SolidBrush(AppTheme.Surface);
        pevent.Graphics.FillRectangle(back, ClientRectangle);
    }

    private const int WM_PAINT = 0x000F;
    private const int WM_PRINTCLIENT = 0x0318;

    /// <summary>
    /// An empty list raises no Paint of its own, so its outline is drawn
    /// once Windows has painted it (as EmptyNote does) - and into a
    /// picture of it (WM_PRINTCLIENT) too.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (Items.Count > 0 || string.IsNullOrEmpty(EmptyText) || !IsHandleCreated)
        {
            return;
        }

        if (m.Msg == WM_PAINT)
        {
            using var g = CreateGraphics();
            DrawEmpty(g);
        }
        else if (m.Msg == WM_PRINTCLIENT && m.WParam != IntPtr.Zero)
        {
            using var g = Graphics.FromHdc(m.WParam);
            DrawEmpty(g);
        }
    }

    private void DrawEmpty(Graphics g)
    {
        if (ClientSize.Width < 16 || ClientSize.Height < 16)
        {
            return;
        }

        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var box = new RectangleF(1.5f, 1.5f, ClientSize.Width - 3f, ClientSize.Height - 3f);

        using (var path = AppTheme.RoundedRect(box, UiLayout.AtDpi(8)))
        using (var pen = new Pen(RosterInk.Blend(AppTheme.TextMuted, AppTheme.Surface, 0.45f), Math.Max(1f, UiLayout.AtDpi(1))) { DashStyle = DashStyle.Dash })
        {
            g.DrawPath(pen, path);
        }

        var margin = UiLayout.AtDpi(14);
        var room = Rectangle.Inflate(ClientRectangle, -margin, -margin);

        TextRenderer.DrawText(g, EmptyText, AppTheme.SmallFont, room, AppTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }}
