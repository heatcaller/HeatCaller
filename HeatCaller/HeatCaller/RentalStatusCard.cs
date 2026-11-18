using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Rentals' count of units still out (17 Nov 2026, page jazz-up): a rounded
/// card tinted in the state's colour - green when everything is back, the
/// accent pulsing between two shades while units are out - with a bar of
/// the colour down its left edge. It used to be a square slab the width of
/// the page.
///
/// The tint is the card's BackColor, which the page sets on every pulse
/// (RentalTransponderControl.ApplyBannerColors); it is painted over the
/// surface inside the rounded edge, the page's colour in the corners - as
/// a Section paints itself. The bar is <see cref="Tone"/>.
/// </summary>
internal sealed class RentalStatusCard : Panel
{
    public RentalStatusCard()
    {
        // Held while the window is being built - see LayoutFreeze.
        LayoutFreeze.Adopt(this);

        Tag = AppTheme.KeepBackColor;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    /// <summary>The state's colour: the bar down the left edge and the outline.</summary>
    public Color Tone
    {
        get => _tone;
        set
        {
            if (_tone != value)
            {
                _tone = value;
                Invalidate();
            }
        }
    }

    private Color _tone = AppTheme.Success;

    /// <summary>Room for the bar down the left edge, past the card's own inset.</summary>
    public static int BarWidth => UiLayout.AtDpi(5);

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        Invalidate(true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;

        using (var behind = new SolidBrush(Parent?.BackColor is { A: 255 } page ? page : AppTheme.Background))
        {
            g.FillRectangle(behind, ClientRectangle);
        }

        if (Width < 4 || Height < 4)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var box = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

        using var path = AppTheme.RoundedRect(box, AppTheme.CornerRadius);

        using (var surface = new SolidBrush(AppTheme.Surface))
        {
            g.FillPath(surface, path);
        }

        // The tint: a see-through colour of the state's, over the surface.
        using (var tint = new SolidBrush(BackColor))
        {
            g.FillPath(tint, path);
        }

        var saved = g.Save();
        g.SetClip(path);

        using (var bar = new SolidBrush(_tone))
        {
            g.FillRectangle(bar, 0, 0, BarWidth, Height);
        }

        g.Restore(saved);

        using var pen = new Pen(Color.FromArgb(90, _tone), 1f);
        g.DrawPath(pen, path);
    }
}

/// <summary>
/// What Rentals, Handicaps and Classes share from their jazz-up (17 Nov
/// 2026): the muted hint line under a card's caption, and a grid with
/// nothing in it saying so - a line in the bold face and one in the muted
/// small face, in the middle of the empty rows - rather than a blank white
/// box.
/// </summary>
internal static class GridHints
{
    /// <summary>A hint in the small muted face, worn so it follows the text size.</summary>
    public static Label Hint(TextSize textSize, string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        textSize.Wear(label, () => AppTheme.SmallFont);
        return label;
    }

    /// <summary>
    /// Has <paramref name="grid"/> draw <paramref name="note"/> - a headline
    /// and a line under it - in its empty rows whenever it has none. Null
    /// draws nothing. Asked at paint time, so it follows the page's state.
    /// </summary>
    public static void ShowWhenEmpty(DataGridView grid, Func<(string Headline, string Line)?> note)
    {
        grid.Paint += (_, e) =>
        {
            if (grid.Rows.Count > 0 || note() is not { } words)
            {
                return;
            }

            var top = grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0;
            var room = new Rectangle(UiLayout.AtDpi(12), top, Math.Max(1, grid.ClientSize.Width - UiLayout.AtDpi(24)), Math.Max(1, grid.ClientSize.Height - top));

            const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

            var headSize = TextRenderer.MeasureText(e.Graphics, words.Headline, AppTheme.BoldFont, new Size(room.Width, 0), flags);
            var lineSize = TextRenderer.MeasureText(e.Graphics, words.Line, AppTheme.SmallFont, new Size(room.Width, 0), flags);
            var gap = UiLayout.HintGap;
            var y = room.Top + Math.Max(UiLayout.AtDpi(8), (room.Height - headSize.Height - gap - lineSize.Height) / 2);

            TextRenderer.DrawText(e.Graphics, words.Headline, AppTheme.BoldFont,
                new Rectangle(room.Left, y, room.Width, headSize.Height), AppTheme.TextDark, flags);
            TextRenderer.DrawText(e.Graphics, words.Line, AppTheme.SmallFont,
                new Rectangle(room.Left, y + headSize.Height + gap, room.Width, lineSize.Height), AppTheme.TextMuted, flags);
        };

        // The note is centred in what the grid shows, so a new size draws it afresh.
        grid.Resize += (_, _) => grid.Invalidate();
    }

    /// <summary>
    /// Draws a cell's words as a chip (RosterInk.DrawChip) in place of the
    /// text: the cell's background and selection as the grid draws them,
    /// then the pill. The cell's text stays, for a screen reader and the
    /// tests. Null tone leaves the cell to the grid.
    /// </summary>
    public static void PaintChip(DataGridViewCellPaintingEventArgs e, string? text, Color? tone, bool dot = true)
    {
        if (e.Graphics is null || string.IsNullOrEmpty(text) || tone is not { } colour)
        {
            return;
        }

        e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);

        var cell = Rectangle.Inflate(e.CellBounds, -UiLayout.AtDpi(4), 0);
        RosterInk.DrawChip(e.Graphics, cell, text, colour, AppTheme.SmallFont, dot);

        e.Handled = true;
    }
}
