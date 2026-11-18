namespace HeatCaller;

/// <summary>
/// One heat on the Move drivers between heats board (HeatBalanceForm), as a
/// card (18 Nov 2026, dialog jazz-up): the theme's rounded card, a wash of
/// the class's colour behind the heading and the count, and the class's
/// band across the top - the same band the printed heat sheet carries. The
/// spares column has the wash and no band.
///
/// The form's own: it places the heading, the count and the list inside it
/// (HeatBalanceForm.NewColumn), and the list stands in from the edges far
/// enough to clear the rounded corners.
/// </summary>
internal sealed class HeatColumnCard : Panel
{
    private readonly Color? _band;
    private readonly Color _wash;
    private readonly int _washHeight;
    private readonly int _bandHeight;

    public HeatColumnCard(Color? band, Color wash, int washHeight, int bandHeight)
    {
        _band = band;
        _wash = wash;
        _washHeight = washHeight;
        _bandHeight = bandHeight;

        BackColor = AppTheme.Surface;
        Tag = AppTheme.KeepBackColor;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;

        // The board's colour in the corners, as TileCard and Section do.
        using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            g.FillRectangle(behind, ClientRectangle);
        }

        AppTheme.DrawCard(g, new RectangleF(0, 0, Width, Height));

        // The wash and the band, clipped to the card so they take its
        // rounded corners rather than poking square ones out of them.
        using var path = AppTheme.RoundedRect(RectangleF.Inflate(new RectangleF(0, 0, Width, Height), -0.5f, -0.5f), AppTheme.CornerRadius);

        var saved = g.Save();
        g.SetClip(path);

        using (var wash = new SolidBrush(_wash))
        {
            g.FillRectangle(wash, 0, 0, Width, _washHeight);
        }

        using (var rule = new Pen(AppTheme.Border, 1f))
        {
            g.DrawLine(rule, 0, _washHeight, Width, _washHeight);
        }

        if (_band is { } band)
        {
            using var brush = new SolidBrush(band);
            g.FillRectangle(brush, 0, 0, Width, _bandHeight);
        }

        g.Restore(saved);

        // The outline again over the wash, so the card's edge stays one
        // line all the way round.
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var pen = new Pen(AppTheme.Border, 1f);
        g.DrawPath(pen, path);
    }
}

/// <summary>
/// A heat's list of drivers on the board (HeatBalanceForm), which says what
/// fills it when it is empty - "Drag drivers here." - so an empty heat
/// reads as somewhere to drop a driver. A list with no rows never asks for
/// one to be drawn, so the line goes on after Windows has painted the list,
/// on screen and when it is printed to a picture - as BackupFileList's
/// empty line does.
/// </summary>
internal sealed class HeatDropList : ListBox
{
    private const int WmPaint = 0x000F;
    private const int WmPrintClient = 0x0318;

    /// <summary>The line an empty list shows, or empty for none.</summary>
    public string EmptyNote { get; set; } = string.Empty;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (Items.Count > 0 || EmptyNote.Length == 0 || !IsHandleCreated)
        {
            return;
        }

        if (m.Msg == WmPaint)
        {
            using var g = CreateGraphics();
            DrawEmpty(g);
        }
        else if (m.Msg == WmPrintClient && m.WParam != IntPtr.Zero)
        {
            using var g = Graphics.FromHdc(m.WParam);
            DrawEmpty(g);
        }
    }

    /// <summary>The line near the top, where the first driver would go - in the list's own colour, which is tinted while a drag is over it.</summary>
    private void DrawEmpty(Graphics g)
    {
        using (var back = new SolidBrush(BackColor))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        var pad = UiLayout.AtTextSize(8);
        var box = new Rectangle(pad, pad, Math.Max(0, ClientSize.Width - 2 * pad), Math.Max(0, ItemHeight * 2));

        TextRenderer.DrawText(g, EmptyNote, AppTheme.SmallFont, box, AppTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (Items.Count == 0)
        {
            Invalidate();
        }
    }
}
