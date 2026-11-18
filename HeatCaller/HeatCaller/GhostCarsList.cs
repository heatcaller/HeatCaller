using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Ghost Cars' list (17 Nov 2026, page jazz-up): one card per ghost - a
/// round badge with its initials, its name, a line under it of its car and
/// where its pace comes from, and an ON or OFF chip. In place of the
/// six-column grid it was. Drawn as Venues' list is (VenueList): an
/// owner-drawn ListBox, so the keyboard, the selection (several at once,
/// as the grid allowed) and the scrolling are Windows' own, the rows drawn
/// from the theme at paint time.
/// </summary>
internal sealed class GhostList : ListBox
{
    public GhostList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        SelectionMode = SelectionMode.MultiExtended;
        Tag = AppTheme.KeepBackColor;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.TextDark;

        // The picked row draws its own outline - see VenueList.
        InputOutline.Exclude(this);
        Remeasure();
    }

    /// <summary>The text size changed: the rows are as tall as a name and a line under it.</summary>
    public void Remeasure()
    {
        ItemHeight = Math.Max(UiLayout.AtDpi(52), AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(22));
        Invalidate();
    }

    /// <summary>The row's ghost - asked of the item at paint time.</summary>
    public interface IGhostRow
    {
        string Id { get; }
        string Name { get; }
        string Line { get; }
        bool On { get; }
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

        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not IGhostRow ghost)
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

        const TextFormatFlags keep = TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
        const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | keep;

        // The badge: the ghost's initials in a ring of the look's colour -
        // paler for a ghost switched off.
        var pad = UiLayout.AtDpi(8);
        var size = row.Height - pad * 2;
        var badge = new RectangleF(row.X + pad, row.Y + pad, size, size);
        var ink = ghost.On ? AppTheme.Primary : AppTheme.TextMuted;

        using (var fill = new SolidBrush(RosterInk.Blend(ink, AppTheme.Surface, selected ? 0.55f : 0.8f)))
        {
            g.FillEllipse(fill, badge);
        }

        using (var ring = new Pen(ink, Math.Max(1f, UiLayout.AtDpi(selected ? 2 : 1))))
        {
            g.DrawEllipse(ring, badge.X + 0.5f, badge.Y + 0.5f, badge.Width - 1f, badge.Height - 1f);
        }

        TextRenderer.DrawText(g, VenueList.Initials(ghost.Name), AppTheme.BoldFont, Rectangle.Round(badge), selected ? AppTheme.TextDark : ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | keep);

        // The name, ON or OFF after it, and its car and pace under it.
        var x = (int)badge.Right + UiLayout.AtDpi(10);
        var width = Math.Max(1, row.Right - x - pad);
        var textHeight = AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(2);
        var y = row.Y + (row.Height - textHeight) / 2;

        var chipText = ghost.On ? "ON" : "OFF";
        var chipInk = ghost.On ? AppTheme.Success : AppTheme.TextMuted;
        var measured = TextRenderer.MeasureText(g, chipText, AppTheme.MicroFont, Size.Empty, TextFormatFlags.NoPadding);
        var chipWidth = measured.Width + UiLayout.AtDpi(18);
        var chip = new RectangleF(row.Right - pad - chipWidth, y, chipWidth, AppTheme.BoldFont.Height);

        using (var path = AppTheme.RoundedRect(chip, (int)(chip.Height / 2)))
        using (var fill = new SolidBrush(RosterInk.Blend(chipInk, AppTheme.Surface, 0.82f)))
        {
            g.FillPath(fill, path);
        }

        TextRenderer.DrawText(g, chipText, AppTheme.MicroFont, Rectangle.Round(chip), chipInk,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | keep);

        var name = string.IsNullOrWhiteSpace(ghost.Name) ? "(unnamed ghost)" : ghost.Name;

        TextRenderer.DrawText(g, name, AppTheme.BoldFont,
            new Rectangle(x, y, Math.Max(1, width - chipWidth - UiLayout.AtDpi(6)), AppTheme.BoldFont.Height),
            ghost.On ? AppTheme.TextDark : AppTheme.TextMuted, line);

        TextRenderer.DrawText(g, ghost.Line, AppTheme.SmallFont,
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

/// <summary>
/// A short state on a soft pill - "ON", "OFF", "RUNNING" - as the Venues
/// list draws its APPLIED chip, but a control of its own for a card to
/// carry beside a switch (Ghost Cars, Demo Mode, Practice; 17 Nov 2026).
/// Sized to its words in its own face, which the page keeps at the text
/// size (TextSize.Wear). Its colours are asked of the theme at paint time,
/// so it follows the look.
/// </summary>
internal sealed class StateChip : Control
{
    private bool _good;

    public StateChip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Tag = AppTheme.KeepBackColor;
        Font = AppTheme.MicroFont;
        AutoSize = true;
        TabStop = false;
    }

    /// <summary>True draws the chip in the success colour; false in the muted one.</summary>
    public bool Good
    {
        get => _good;
        set
        {
            if (_good != value)
            {
                _good = value;
                Invalidate();
            }
        }
    }

    /// <summary>Sets the words and the colour together.</summary>
    public void Show(string text, bool good)
    {
        Text = text;
        Good = good;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
        SizeToWords();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        SizeToWords();
    }

    /// <summary>The room round the words, the dot and the gap after it - from the face, so they grow with the text size.</summary>
    private int Pad => Math.Max(UiLayout.AtDpi(8), (int)(Font.Height * 0.75f));
    private int DotSize => Math.Max(UiLayout.AtDpi(6), (int)(Font.Height * 0.45f));
    private int DotGap => Math.Max(UiLayout.AtDpi(5), (int)(Font.Height * 0.4f));

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text ?? string.Empty, Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(Pad + DotSize + DotGap + text.Width + Pad, Math.Max(text.Height, Font.Height) + Pad);
    }

    private void SizeToWords()
    {
        var size = GetPreferredSize(Size.Empty);

        if (Size != size)
        {
            Size = size;
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Surface))
        {
            g.FillRectangle(behind, ClientRectangle);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var ink = _good ? AppTheme.Success : AppTheme.TextMuted;
        var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        using (var path = AppTheme.RoundedRect(box, (int)(box.Height / 2)))
        using (var fill = new SolidBrush(RosterInk.Blend(ink, AppTheme.Surface, 0.82f)))
        {
            g.FillPath(fill, path);
        }

        // A dot of the state's colour before the words.
        var d = DotSize;
        var dotX = Pad;

        using (var dot = new SolidBrush(ink))
        {
            g.FillEllipse(dot, dotX, (Height - d) / 2f, d, d);
        }

        var left = dotX + d + DotGap;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(left, 0, Math.Max(1, Width - left), Height), ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}
