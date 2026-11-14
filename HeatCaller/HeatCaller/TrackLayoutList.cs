using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Track Layouts' list (13 Nov 2026, mock-up B): one row per layout, a
/// picture of its plan beside its name, one line of facts under the name,
/// and "Tonight" on the layout the open race day races on. A club has a
/// handful of layouts, and what tells them apart is what they look like.
///
/// An owner-drawn ListBox, so the keyboard, the selection and the scrolling
/// are Windows' own; the rows are drawn here from the theme at paint time.
/// </summary>
internal sealed class LayoutList : ListBox
{
    /// <summary>One layout's row. <paramref name="Track"/> is drawn into the picture, once, and kept.</summary>
    public sealed record Entry(string Id, string Name, string Line, bool Tonight, TrackLayout Track)
    {
        public override string ToString() => Name;
    }

    /// <summary>Each layout's picture, drawn once per refresh.</summary>
    private readonly Dictionary<string, Image?> _pictures = new();

    public LayoutList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        Tag = AppTheme.KeepBackColor;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.TextDark;

        // The picked row draws its own outline; the theme's focus ring round
        // the whole list read as if the list itself were selected.
        InputOutline.Exclude(this);
        Remeasure();
    }

    /// <summary>The text size changed: the rows are as tall as a name and a line under it, and the picture with them.</summary>
    public void Remeasure()
    {
        ItemHeight = Math.Max(UiLayout.AtDpi(56), AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(22));
        ClearPictures();
        Invalidate();
    }

    private int PictureWidth => (int)(ItemHeight * 1.5f);

    /// <summary>The rows, replacing what was there. The selection is the caller's to put back.</summary>
    public void SetEntries(IReadOnlyList<Entry> entries)
    {
        BeginUpdate();

        try
        {
            ClearPictures();
            Items.Clear();

            foreach (var entry in entries)
            {
                Items.Add(entry);
            }
        }
        finally
        {
            EndUpdate();
        }
    }

    public void ClearPictures()
    {
        foreach (var picture in _pictures.Values)
        {
            picture?.Dispose();
        }

        _pictures.Clear();
    }

    private Image? PictureFor(Entry entry)
    {
        if (!_pictures.TryGetValue(entry.Id, out var picture))
        {
            try
            {
                picture = TrackImagery.MainPicture(entry.Track, new Size(PictureWidth * 2, (ItemHeight - UiLayout.AtDpi(12)) * 2), AppTheme.Background);
            }
            catch (Exception)
            {
                // A photo that won't load leaves the row's picture empty, not the page broken.
                picture = null;
            }

            _pictures[entry.Id] = picture;
        }

        return picture;
    }

    /// <summary>
    /// Drawn off-screen and put up in one go: Windows redraws the picked row
    /// whenever the focus comes or goes, and drawn straight to the screen the
    /// row showed blank between its fill and its text - a flash on every
    /// click elsewhere (Championship's cards, 14 Nov 2026).
    /// </summary>
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

        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not Entry entry)
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

        // The picture, or a dashed box where it would be.
        var pad = UiLayout.AtDpi(6);
        var box = new Rectangle(row.X + pad, row.Y + pad, PictureWidth, row.Height - pad * 2);

        using (var path = AppTheme.RoundedRect(box, UiLayout.AtDpi(5)))
        {
            using var fill = new SolidBrush(AppTheme.Background);
            g.FillPath(fill, path);

            if (PictureFor(entry) is { } picture)
            {
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                var scale = Math.Min(box.Width / (float)picture.Width, box.Height / (float)picture.Height);
                var w = picture.Width * scale;
                var h = picture.Height * scale;
                g.DrawImage(picture, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                g.Clip = clip;
            }
            else
            {
                using var dashed = new Pen(AppTheme.TextMuted, Math.Max(1f, UiLayout.AtDpi(1))) { DashStyle = DashStyle.Dash };
                g.DrawPath(dashed, path);
            }
        }

        // The name, "Tonight" after it, and the facts under it.
        var x = box.Right + UiLayout.AtDpi(10);
        var width = Math.Max(1, row.Right - x - pad);
        var textHeight = AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(2);
        var y = row.Y + (row.Height - textHeight) / 2;
        // The off-screen buffer moves its drawing by the row's top-left;
        // TextRenderer ignores that unless told, and every row but the first
        // drew its text off the buffer - blank (14 Nov 2026).
        const TextFormatFlags keep = TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
        const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | keep;

        var tonightWidth = 0;

        if (entry.Tonight)
        {
            const string tonight = "TONIGHT";
            var size = TextRenderer.MeasureText(g, tonight, AppTheme.MicroFont, Size.Empty, TextFormatFlags.NoPadding);
            tonightWidth = size.Width + UiLayout.AtDpi(16);
            var chip = new RectangleF(row.Right - pad - tonightWidth, y, tonightWidth, AppTheme.BoldFont.Height);

            using (var path = AppTheme.RoundedRect(chip, (int)(chip.Height / 2)))
            using (var fill = new SolidBrush(RosterInk.Blend(AppTheme.Success, AppTheme.Surface, 0.82f)))
            {
                g.FillPath(fill, path);
            }

            TextRenderer.DrawText(g, tonight, AppTheme.MicroFont, Rectangle.Round(chip), AppTheme.Success,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | keep);
        }

        TextRenderer.DrawText(g, entry.Name, AppTheme.BoldFont,
            new Rectangle(x, y, Math.Max(1, width - (tonightWidth > 0 ? tonightWidth + UiLayout.AtDpi(6) : 0)), AppTheme.BoldFont.Height),
            AppTheme.TextDark, line);

        TextRenderer.DrawText(g, entry.Line, AppTheme.SmallFont,
            new Rectangle(x, y + AppTheme.BoldFont.Height + UiLayout.AtDpi(2), width, AppTheme.SmallFont.Height), AppTheme.TextMuted, line);
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
