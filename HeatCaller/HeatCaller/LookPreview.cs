using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// A painted miniature of the app wearing a look: the dark sidebar, a page,
/// a card holding a small results table, a primary button, a link, the
/// good/bad chips and a hint.
///
/// A look is a big thing to imagine from eleven hex codes and a name - "Pit
/// Lane" means nothing until you have seen yellow buttons with black text on
/// them - so the Looks tab and the look editor both show one of these rather
/// than a row of swatches.
///
/// Painted from the ResolvedLook it is given, never from AppTheme's own
/// colours, so it shows the look being chosen and not the look the app is
/// wearing. Fonts are AppTheme's shared ones, read at paint time, so it
/// follows a text-size change without being rebuilt.
///
/// A plain Control rather than a Panel on purpose: the theming pass repaints
/// every Panel in the content colours, and this must keep its own.
/// </summary>
public sealed class LookPreview : Control
{
    private ResolvedLook? _look;

    public LookPreview()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        // A picture, not something to tab to.
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;

        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Preview of the look";
    }

    /// <summary>The look to draw. Null draws an empty page in the app's own background.</summary>
    public ResolvedLook? Look
    {
        get => _look;
        set
        {
            _look = value;
            AccessibleDescription = value is null ? null : $"Preview of {value.Name}";
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        var bounds = ClientRectangle;

        if (bounds.Width < 40 || bounds.Height < 40)
        {
            return;
        }

        var look = _look;

        if (look is null)
        {
            using var empty = new SolidBrush(AppTheme.Background);
            g.FillRectangle(empty, bounds);
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var small = AppTheme.SmallFont;
        var micro = AppTheme.MicroFont;
        var bold = AppTheme.BoldFont;

        // --- The page ---
        using (var page = new SolidBrush(look.Background))
        {
            g.FillRectangle(page, bounds);
        }

        // --- The sidebar: dark chrome in every look, as in the app ---
        var sidebarWidth = Math.Clamp(bounds.Width / 6, 36, 90);
        var sidebar = new Rectangle(bounds.Left, bounds.Top, sidebarWidth, bounds.Height);

        using (var rail = new SolidBrush(look.Sidebar))
        {
            g.FillRectangle(rail, sidebar);
        }

        var headerHeight = Math.Min(28, bounds.Height / 7);

        if (look.Mode == AppTheme.Mode.Synthwave)
        {
            // A hint of the sunset header the Synthwave family brings with it.
            var header = new Rectangle(sidebar.Left, sidebar.Top, sidebar.Width, headerHeight);

            using var sunset = new LinearGradientBrush(header, look.Sidebar, look.Primary, LinearGradientMode.Vertical);
            g.FillRectangle(sunset, header);
        }

        // Nav rows: faint bars, the selected one in the primary colour -
        // SidebarSelected is Primary in every look.
        var navTop = sidebar.Top + headerHeight + 8;
        var navHeight = 6;

        using (var faint = new SolidBrush(Color.FromArgb(70, AppTheme.SidebarText)))
        using (var selected = new SolidBrush(look.Primary))
        using (var hover = new SolidBrush(look.SidebarHover))
        {
            for (var i = 0; i < 6; i++)
            {
                var y = navTop + i * (navHeight + 9);

                if (y + navHeight > sidebar.Bottom - 6)
                {
                    break;
                }

                if (i == 1)
                {
                    g.FillRectangle(hover, sidebar.Left, y - 4, sidebar.Width, navHeight + 8);
                    g.FillRectangle(selected, sidebar.Left, y - 4, 3, navHeight + 8);
                }

                var width = sidebar.Width - 16 - (i % 3) * 6;
                g.FillRectangle(i == 1 ? selected : faint, sidebar.Left + 8, y, Math.Max(8, width), navHeight);
            }
        }

        // --- The content ---
        //
        // Its paddings, insets and corners at the text size in use, as its
        // faces and the preview's own height are (LooksControl and the look
        // editor size it with AtTextSize): at fixed pixels the text doubled
        // at 200% inside margins that did not. Worked out on every paint, so
        // a new text size is picked up by the repaint it brings. The sidebar
        // above is left as it was - it draws no text, and its width is a
        // share of the preview's, not a size that follows the text.
        var pad = UiLayout.AtTextSize(10);
        var left = sidebar.Right + pad;
        var right = bounds.Right - pad;
        var contentWidth = right - left;

        if (contentWidth < 60)
        {
            return;
        }

        var saved = g.Save();
        g.SetClip(new Rectangle(left - 2, bounds.Top, contentWidth + 4, bounds.Height));

        var y0 = bounds.Top + pad;
        var textFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;

        // Page title, in the look's own text colour.
        TextRenderer.DrawText(g, "Race Control", bold, new Rectangle(left, y0, contentWidth, bold.Height), look.Text, textFlags);
        y0 += bold.Height + UiLayout.AtTextSize(6);

        // --- A card with a header rule and three rows ---
        var rowHeight = small.Height + UiLayout.AtTextSize(6);
        var cardHeight = micro.Height + UiLayout.AtTextSize(10) + rowHeight * 3 + UiLayout.AtTextSize(8);
        var card = new RectangleF(left, y0, contentWidth, cardHeight);

        using (var path = AppTheme.RoundedRect(RectangleF.Inflate(card, -0.5f, -0.5f), UiLayout.AtTextSize(6)))
        using (var surface = new SolidBrush(look.Surface))
        using (var border = new Pen(look.Border))
        {
            g.FillPath(surface, path);
            g.DrawPath(border, path);
        }

        var inner = Rectangle.Round(RectangleF.Inflate(card, -UiLayout.AtTextSize(8), 0));
        var headerY = (int)card.Top + UiLayout.AtTextSize(5);

        // Column captions in the primary colour over a thin rule - the way
        // every grid header in the app is drawn.
        // At the text size in use, as the letters in them are.
        var posWidth = Math.Min(UiLayout.AtTextSize(34), inner.Width / 6);
        var lapWidth = Math.Min(UiLayout.AtTextSize(80), inner.Width / 3);
        TextRenderer.DrawText(g, "POS", micro, new Rectangle(inner.Left, headerY, posWidth, micro.Height), look.Primary, textFlags);
        TextRenderer.DrawText(g, "DRIVER", micro, new Rectangle(inner.Left + posWidth, headerY, inner.Width - posWidth - lapWidth, micro.Height), look.Primary, textFlags);
        TextRenderer.DrawText(g, "BEST", micro, new Rectangle(inner.Right - lapWidth, headerY, lapWidth, micro.Height), look.Primary,
            textFlags | TextFormatFlags.Right);

        var ruleY = headerY + micro.Height + UiLayout.AtTextSize(4);

        using (var rule = new Pen(look.Border))
        {
            g.DrawLine(rule, card.Left + 1, ruleY, card.Right - 1, ruleY);
        }

        var rows = new[] { ("1", "Sam Kerr", "12.482"), ("2", "Alex Moreno", "12.610"), ("3", "Jo Park", "12.797") };
        var rowY = ruleY + 1;

        for (var i = 0; i < rows.Length; i++)
        {
            var rowRect = new Rectangle((int)card.Left + 1, rowY, (int)card.Width - 2, rowHeight);

            if (i % 2 == 1)
            {
                using var alt = new SolidBrush(look.RowAlt);
                g.FillRectangle(alt, rowRect);
            }

            var (pos, name, lap) = rows[i];
            TextRenderer.DrawText(g, pos, small, new Rectangle(inner.Left, rowY, posWidth, rowHeight), look.Muted, textFlags);
            TextRenderer.DrawText(g, name, small, new Rectangle(inner.Left + posWidth, rowY, inner.Width - posWidth - lapWidth, rowHeight), look.Text, textFlags);

            // The leader's best lap in the "good" colour, as the app marks a
            // personal best.
            TextRenderer.DrawText(g, lap, small, new Rectangle(inner.Right - lapWidth, rowY, lapWidth, rowHeight), i == 0 ? look.Good : look.Text,
                textFlags | TextFormatFlags.Right);

            rowY += rowHeight;
        }

        y0 = (int)card.Bottom + UiLayout.AtTextSize(10);

        // --- A primary button, a link and the two signal chips ---
        var controlHeight = Math.Max(bold.Height, small.Height) + UiLayout.AtTextSize(8);
        var x = left;

        var buttonText = "Start";
        var buttonWidth = TextRenderer.MeasureText(g, buttonText, bold, Size.Empty, TextFormatFlags.NoPadding).Width + UiLayout.AtTextSize(24);
        var button = new RectangleF(x, y0, buttonWidth, controlHeight);

        using (var path = AppTheme.RoundedRect(button, UiLayout.AtTextSize(5)))
        using (var fill = new SolidBrush(look.Primary))
        {
            g.FillPath(fill, path);
        }

        TextRenderer.DrawText(g, buttonText, bold, Rectangle.Round(button), look.OnPrimary,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        x = (int)button.Right + UiLayout.AtTextSize(12);

        var linkText = "Results";
        var linkSize = TextRenderer.MeasureText(g, linkText, small, Size.Empty, TextFormatFlags.NoPadding);
        var linkRect = new Rectangle(x, y0, linkSize.Width, controlHeight);
        TextRenderer.DrawText(g, linkText, small, linkRect, look.Accent,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);

        using (var underline = new Pen(look.Accent))
        {
            var baseline = y0 + (controlHeight + linkSize.Height) / 2 - 1;
            g.DrawLine(underline, x, baseline, x + linkSize.Width, baseline);
        }

        x = linkRect.Right + UiLayout.AtTextSize(12);
        x = DrawChip(g, x, y0, controlHeight, "+2", look.Good, look.OnGood, micro);
        DrawChip(g, x + UiLayout.AtTextSize(6), y0, controlHeight, "DNF", look.Bad, look.OnBad, micro);

        y0 += controlHeight + UiLayout.AtTextSize(8);

        // --- A hint ---
        TextRenderer.DrawText(g, "Hints and captions look like this.", small, new Rectangle(left, y0, contentWidth, small.Height), look.Muted, textFlags);

        g.Restore(saved);
    }

    /// <summary>A filled, rounded chip with its caption. Returns the x just past it. Its room round the caption, and its inset from the line's top and foot, at the text size in use, as the caption is.</summary>
    private static int DrawChip(Graphics g, int x, int y, int height, string text, Color fill, Color ink, Font font)
    {
        var inset = UiLayout.AtTextSize(2);
        var width = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding).Width + UiLayout.AtTextSize(16);
        var chip = new RectangleF(x, y + inset, width, height - inset * 2);

        using (var path = AppTheme.RoundedRect(chip, (int)(chip.Height / 2)))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        TextRenderer.DrawText(g, text, font, Rectangle.Round(chip), ink,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        return (int)chip.Right;
    }
}
