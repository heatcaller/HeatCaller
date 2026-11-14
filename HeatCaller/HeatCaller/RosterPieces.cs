using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// A row of filter chips, one of them picked (13 Nov 2026, Who's Racing
/// rework, mock-up A "Pit Board"). Each chip says what it shows and how many:
/// "Stock 5". The chips wrap onto another line when the row runs out of room,
/// rather than being cut off - a class list is as long as the club makes it.
///
/// Click a chip, or with the row focused use the arrow keys. Colours are read
/// at paint time, so it follows a theme change.
///
/// Sizes itself: put it in a Section, which gives it the width it wraps to.
/// </summary>
internal sealed class ChipRow : Control
{
    /// <summary>One chip: what it stands for, what it says, and the count beside it (null for none).</summary>
    public sealed record Chip(string Key, string Text, int? Count = null);

    private IReadOnlyList<Chip> _chips = Array.Empty<Chip>();
    private string _picked = string.Empty;
    private int _hot = -1;

    public ChipRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        Tag = AppTheme.KeepBackColor;
        TabStop = true;
        Cursor = Cursors.Hand;
        AutoSize = true;
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.PageTabList;
    }

    /// <summary>Raised when a chip is picked from the row (not when <see cref="Picked"/> is set in code).</summary>
    public event Action<string>? PickedChanged;

    /// <summary>The key of the chip picked.</summary>
    public string Picked
    {
        get => _picked;
        set
        {
            if (string.Equals(_picked, value, StringComparison.Ordinal))
            {
                return;
            }

            _picked = value;
            AccessibleName = _chips.FirstOrDefault(c => c.Key == value)?.Text ?? string.Empty;
            Invalidate();
        }
    }

    /// <summary>The chips, in order. Nothing happens when they are the same as before.</summary>
    public void SetChips(IReadOnlyList<Chip> chips)
    {
        if (_chips.SequenceEqual(chips))
        {
            return;
        }

        var heightBefore = Height;
        _chips = chips.ToList();
        _hot = -1;

        // A row that wraps onto one more or one fewer line tells the card it is in.
        if (PreferredHeight(Width) != heightBefore)
        {
            Parent?.PerformLayout();
        }

        Invalidate();
    }

    public IReadOnlyList<Chip> Chips => _chips;

    private int Gap => UiLayout.AtDpi(8);
    private int PadX => UiLayout.AtDpi(12);
    private int ChipHeight => Font.Height + UiLayout.AtDpi(12);

    private static Font CountFont => AppTheme.SmallFont;

    private int ChipWidth(Chip chip)
    {
        var width = TextRenderer.MeasureText(chip.Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

        if (chip.Count is { } count)
        {
            width += UiLayout.AtDpi(6) + TextRenderer.MeasureText(count.ToString(), CountFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        }

        return width + PadX * 2;
    }

    /// <summary>Where each chip goes in a row <paramref name="width"/> wide, wrapping as it must.</summary>
    private List<Rectangle> Arrange(int width)
    {
        var boxes = new List<Rectangle>(_chips.Count);
        int x = 0, y = 0, h = ChipHeight;

        foreach (var chip in _chips)
        {
            var w = ChipWidth(chip);

            if (x > 0 && x + w > width)
            {
                x = 0;
                y += h + Gap;
            }

            boxes.Add(new Rectangle(x, y, w, h));
            x += w + Gap;
        }

        return boxes;
    }

    private int OneLineWidth => _chips.Sum(ChipWidth) + Gap * Math.Max(0, _chips.Count - 1);

    private int PreferredHeight(int width)
    {
        var boxes = Arrange(Math.Max(1, width));
        return boxes.Count == 0 ? ChipHeight : boxes.Max(b => b.Bottom) + 1;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : OneLineWidth;
        return new Size(width, PreferredHeight(width));
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Parent?.PerformLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var boxes = Arrange(Math.Max(1, Width));

        for (var i = 0; i < _chips.Count; i++)
        {
            var chip = _chips[i];
            var box = boxes[i];
            var picked = string.Equals(chip.Key, _picked, StringComparison.Ordinal);
            var rect = new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1f, box.Height - 1f);

            var fill = picked ? RosterInk.Blend(AppTheme.Primary, AppTheme.Surface, 0.82f)
                : i == _hot ? AppTheme.SecondaryButtonHover : AppTheme.Surface;
            var edge = picked ? AppTheme.Primary : AppTheme.Border;
            var ink = picked ? AppTheme.Primary : AppTheme.TextDark;

            using (var path = AppTheme.RoundedRect(rect, box.Height / 2))
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
                using var pen = new Pen(edge, Math.Max(1f, UiLayout.AtDpi(1)));
                g.DrawPath(pen, path);
            }

            var textWidth = TextRenderer.MeasureText(chip.Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            var textBox = new Rectangle(box.X + PadX, box.Y, textWidth + 2, box.Height);

            TextRenderer.DrawText(g, chip.Text, Font, textBox, ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            if (chip.Count is { } count)
            {
                var countBox = new Rectangle(textBox.Right + UiLayout.AtDpi(4), box.Y, box.Right - textBox.Right - UiLayout.AtDpi(4), box.Height);
                TextRenderer.DrawText(g, count.ToString(), CountFont, countBox, picked ? AppTheme.Primary : AppTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            if (picked && Focused && ShowFocusCues)
            {
                var ring = Rectangle.Inflate(box, -2, -2);
                ControlPaint.DrawFocusRectangle(g, ring);
            }
        }
    }

    private int ChipAt(Point p)
    {
        var boxes = Arrange(Math.Max(1, Width));
        return boxes.FindIndex(b => b.Contains(p));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = ChipAt(e.Location);

        if (hot != _hot)
        {
            _hot = hot;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left && ChipAt(e.Location) is var i and >= 0)
        {
            Pick(i);
        }
    }

    private void Pick(int index)
    {
        var key = _chips[index].Key;

        if (string.Equals(key, _picked, StringComparison.Ordinal))
        {
            return;
        }

        Picked = key;
        PickedChanged?.Invoke(key);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var at = _chips.ToList().FindIndex(c => c.Key == _picked);

        if (e.KeyCode == Keys.Left && at > 0)
        {
            Pick(at - 1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Right && at < _chips.Count - 1)
        {
            Pick(at + 1);
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}

/// <summary>
/// The readiness strip across the top of Who's Racing (13 Nov 2026, mock-up
/// A): one card per gate a driver goes through - checked in, tech, paid,
/// papers - each with its count and a bar, and last the number that matters,
/// how many are ready to race. A card can be clicked to show just the drivers
/// still waiting at that gate.
/// </summary>
internal sealed class ReadinessStrip : Control
{
    /// <summary>One card. <paramref name="Fill"/> is how far along the bar is, 0 to 1, or null for no bar.</summary>
    public sealed record Tile(string Key, string Caption, string Value, string Detail, double? Fill, Color Tone, bool Stands = false);

    private IReadOnlyList<Tile> _tiles = Array.Empty<Tile>();
    private int _hot = -1;

    public ReadinessStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        Tag = AppTheme.KeepBackColor;
        AutoSize = true;
        Margin = Padding.Empty;
    }

    /// <summary>Raised with a card's key when it is clicked.</summary>
    public event Action<string>? TileClicked;

    public IReadOnlyList<Tile> Tiles => _tiles;

    public void SetTiles(IReadOnlyList<Tile> tiles)
    {
        if (_tiles.SequenceEqual(tiles))
        {
            return;
        }

        _tiles = tiles.ToList();
        AccessibleName = string.Join(", ", _tiles.Select(t => $"{t.Caption} {t.Value} {t.Detail}"));
        Invalidate();
    }

    private static int Pad => UiLayout.AtDpi(12);
    private static int BarHeight => UiLayout.AtDpi(5);

    private static int TileHeight =>
        Pad + AppTheme.MicroFont.Height + UiLayout.AtDpi(4) + AppTheme.TitleFont.Height + UiLayout.AtDpi(6) + BarHeight + Pad;

    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width, TileHeight);

    /// <summary>The text size changed: the strip is as tall as its fonts.</summary>
    public void Remeasure() => Parent?.PerformLayout();

    private List<Rectangle> Arrange()
    {
        var gap = UiLayout.RowGap;
        var count = Math.Max(1, _tiles.Count);
        var room = Math.Max(0, Width - gap * (count - 1));
        var boxes = new List<Rectangle>();

        for (var i = 0; i < _tiles.Count; i++)
        {
            var left = i * (room / count) + i * gap;
            var width = i == _tiles.Count - 1 ? Width - left : room / count;
            boxes.Add(new Rectangle(left, 0, Math.Max(1, width), Height));
        }

        return boxes;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var boxes = Arrange();

        for (var i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];
            var box = boxes[i];
            var rect = new RectangleF(box.X, box.Y, box.Width, box.Height);

            AppTheme.DrawCard(g, rect);

            // The card that stands for the whole page, and the one under the
            // mouse, wear an outline in their own colour.
            if (tile.Stands || i == _hot)
            {
                using var path = AppTheme.RoundedRect(RectangleF.Inflate(rect, -0.5f, -0.5f), UiLayout.AtDpi(10));
                using var pen = new Pen(i == _hot ? AppTheme.Primary : RosterInk.Blend(tile.Tone, AppTheme.Surface, 0.45f), Math.Max(1f, UiLayout.AtDpi(1)));
                g.DrawPath(pen, path);
            }

            var x = box.X + Pad;
            var width = Math.Max(1, box.Width - Pad * 2);
            var y = box.Y + Pad;

            var caption = AppTheme.Spaced(tile.Caption.ToUpperInvariant());
            TextRenderer.DrawText(g, caption, AppTheme.MicroFont, new Rectangle(x, y, width, AppTheme.MicroFont.Height),
                tile.Stands ? tile.Tone : AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            y += AppTheme.MicroFont.Height + UiLayout.AtDpi(4);

            var valueFlags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            var valueWidth = TextRenderer.MeasureText(g, tile.Value, AppTheme.TitleFont, Size.Empty, valueFlags).Width;

            TextRenderer.DrawText(g, tile.Value, AppTheme.TitleFont, new Rectangle(x, y, width, AppTheme.TitleFont.Height),
                tile.Stands ? tile.Tone : AppTheme.TextDark, valueFlags | TextFormatFlags.EndEllipsis);

            // The detail beside the value, on its baseline: "/ 9", "$29 owed".
            if (tile.Detail.Length > 0 && valueWidth + UiLayout.AtDpi(8) < width)
            {
                var detailBox = new Rectangle(x + valueWidth + UiLayout.AtDpi(6), y, width - valueWidth - UiLayout.AtDpi(6), AppTheme.TitleFont.Height - UiLayout.AtDpi(3));
                TextRenderer.DrawText(g, tile.Detail, AppTheme.SmallFont, detailBox, AppTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            y += AppTheme.TitleFont.Height + UiLayout.AtDpi(6);

            if (tile.Fill is { } fill)
            {
                var track = new RectangleF(x, y, width, BarHeight);

                using (var path = AppTheme.RoundedRect(track, BarHeight / 2))
                using (var brush = new SolidBrush(AppTheme.Border))
                {
                    g.FillPath(brush, path);
                }

                var done = (float)(width * Math.Clamp(fill, 0, 1));

                if (done >= 1)
                {
                    using var path = AppTheme.RoundedRect(new RectangleF(x, y, Math.Max(BarHeight, done), BarHeight), BarHeight / 2);
                    using var brush = new SolidBrush(tile.Tone);
                    g.FillPath(brush, path);
                }
            }
        }
    }

    private int TileAt(Point p) => Arrange().FindIndex(b => b.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = TileAt(e.Location);

        if (hot != _hot)
        {
            _hot = hot;
            Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (e.Button == MouseButtons.Left && TileAt(e.Location) is var i and >= 0)
        {
            TileClicked?.Invoke(_tiles[i].Key);
        }
    }
}

/// <summary>
/// The drawing the Who's Racing roster's cells share (13 Nov 2026): a status
/// chip - a soft pill in the state's colour, a dot and the words - and the
/// class tags.
/// </summary>
internal static class RosterInk
{
    public static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    private const TextFormatFlags OneLine =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

    /// <summary>Draws a chip at the left of <paramref name="cell"/>, as wide as its words or the cell, whichever is less. Returns its right edge.</summary>
    public static int DrawChip(Graphics g, Rectangle cell, string text, Color tone, Font font, bool dot = true)
    {
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            var padX = UiLayout.AtDpi(9);
            var dotSide = dot ? UiLayout.AtDpi(7) : 0;
            var dotGap = dot ? UiLayout.AtDpi(6) : 0;
            var height = Math.Min(cell.Height - UiLayout.AtDpi(6), font.Height + UiLayout.AtDpi(8));
            var textWidth = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            var width = Math.Min(cell.Width - UiLayout.AtDpi(4), padX * 2 + dotSide + dotGap + textWidth);

            if (width <= padX * 2 || height <= 0)
            {
                return cell.Left;
            }

            var box = new RectangleF(cell.Left + UiLayout.AtDpi(2), cell.Top + (cell.Height - height) / 2f, width, height);

            using (var path = AppTheme.RoundedRect(box, (int)(height / 2)))
            using (var fill = new SolidBrush(Blend(tone, AppTheme.Surface, 0.84f)))
            {
                g.FillPath(fill, path);
            }

            var x = box.Left + padX;

            if (dot)
            {
                using var brush = new SolidBrush(tone);
                g.FillEllipse(brush, x, box.Top + (height - dotSide) / 2f, dotSide, dotSide);
                x += dotSide + dotGap;
            }

            var textBox = Rectangle.Round(new RectangleF(x, box.Top, box.Right - padX - x + 1, height));
            TextRenderer.DrawText(g, text, font, textBox, tone, OneLine);

            return (int)box.Right;
        }
        finally
        {
            g.SmoothingMode = smoothing;
        }
    }

    /// <summary>Draws the classes as small outlined tags, as many as fit, then "+2".</summary>
    public static void DrawClassTags(Graphics g, Rectangle cell, IReadOnlyList<string> classes, Font font, Color ink)
    {
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            var gap = UiLayout.AtDpi(5);
            var padX = UiLayout.AtDpi(7);
            var height = Math.Min(cell.Height - UiLayout.AtDpi(8), font.Height + UiLayout.AtDpi(6));
            var top = cell.Top + (cell.Height - height) / 2f;
            var x = cell.Left + UiLayout.AtDpi(2);
            var right = cell.Right - UiLayout.AtDpi(4);

            for (var i = 0; i < classes.Count; i++)
            {
                var text = classes[i];
                var width = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + padX * 2;
                var rest = classes.Count - i - 1;
                var moreWidth = rest > 0 ? TextRenderer.MeasureText(g, $"+{rest}", font, Size.Empty, TextFormatFlags.NoPadding).Width + gap : 0;

                // Not room for this one and a "+n" after it: the "+n" for all that are left.
                if (x + width + moreWidth > right && i > 0)
                {
                    TextRenderer.DrawText(g, $"+{classes.Count - i}", font, new Rectangle(x, (int)top, right - x, height), AppTheme.TextMuted, OneLine);
                    return;
                }

                var box = new RectangleF(x, top, Math.Min(width, right - x), height);

                using (var path = AppTheme.RoundedRect(box, UiLayout.AtDpi(5)))
                {
                    using var fill = new SolidBrush(AppTheme.RowAlt);
                    g.FillPath(fill, path);
                    using var pen = new Pen(AppTheme.Border, Math.Max(1f, UiLayout.AtDpi(1)));
                    g.DrawPath(pen, path);
                }

                TextRenderer.DrawText(g, text, font, Rectangle.Round(RectangleF.Inflate(box, -padX + 1, 0)), ink, OneLine | TextFormatFlags.HorizontalCenter);

                x += (int)box.Width + gap;
            }
        }
        finally
        {
            g.SmoothingMode = smoothing;
        }
    }

    /// <summary>Draws one short bar per gate - green done, amber waiting, red failed, grey not yet - then "2/4".</summary>
    public static void DrawGates(Graphics g, Rectangle cell, IReadOnlyList<Color> gates, string count, Font font)
    {
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            var w = UiLayout.AtDpi(14);
            var h = UiLayout.AtDpi(6);
            var gap = UiLayout.AtDpi(3);
            var x = cell.Left + UiLayout.AtDpi(2);
            var y = cell.Top + (cell.Height - h) / 2f;

            foreach (var tone in gates)
            {
                using var path = AppTheme.RoundedRect(new RectangleF(x, y, w, h), UiLayout.AtDpi(2));
                using var brush = new SolidBrush(tone);
                g.FillPath(brush, path);
                x += w + gap;
            }

            // The count only where it fits whole: "2..." says nothing.
            var room = cell.Right - x - UiLayout.AtDpi(5);

            if (TextRenderer.MeasureText(g, count, font, Size.Empty, TextFormatFlags.NoPadding).Width <= room)
            {
                TextRenderer.DrawText(g, count, font, new Rectangle(x + UiLayout.AtDpi(5), cell.Top, room, cell.Height), AppTheme.TextMuted, OneLine);
            }
        }
        finally
        {
            g.SmoothingMode = smoothing;
        }
    }
}
