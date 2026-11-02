namespace HeatCaller;

/// <summary>
/// The amber "Advanced" chip at the foot of the sidebar (1 Nov 2026, Simple
/// and Advanced mode): shown only while Advanced mode is on, so whoever is
/// at the PC can see every setting is showing, and a click goes back to
/// Simple (MainForm.TurnOnSimpleMode). Simple mode shows nothing there - the
/// switch on is a card on Backup &amp; Restore, not something in the footer.
///
/// In the narrow rail (Ctrl+B) there is no room for the word, so it is an
/// amber dot, the word on hover. Drawn and placed like UsageChip, beside
/// which it sits, for the same reasons (MainForm.PlaceWideFooter /
/// PlaceRailFooter).
/// </summary>
internal sealed class AdvancedChip : Control
{
    /// <summary>The dot's side in the rail, at the screen's scale - as UsageChip's.</summary>
    private const int DotSide = 10;

    private bool _rail;
    private bool _hover;
    private Font? _underlined;

    public AdvancedChip()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw
                 | ControlStyles.Selectable, true);

        DoubleBuffered = true;

        // Reachable by keyboard like the footer buttons: Enter or Space goes
        // back to Simple.
        TabStop = true;
        Cursor = Cursors.Hand;
        Tag = AppTheme.KeepBackColor;
        Text = SettingsModes.ChipText;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "Advanced mode is on";
        AccessibleDescription = "Goes back to Simple mode";
        Visible = false;
    }

    /// <summary>The rail's dot, rather than the word.</summary>
    public bool Rail
    {
        get => _rail;
        set
        {
            if (_rail != value)
            {
                _rail = value;
                Invalidate();
            }
        }
    }

    /// <summary>The height the chip needs - as UsageChip's, so the two lines match.</summary>
    internal int WantedHeight => _rail
        ? UiLayout.AtDpi(DotSide + 10)
        : Pieces.LineHeight(AppTheme.SmallFont) + UiLayout.AtDpi(6);

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
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

    /// <summary>Shown as the pointer shows it while the keyboard is on it.</summary>
    private bool Lit => _hover || (Focused && ShowFocusCues);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        g.Clear(AppTheme.SidebarBackground);

        var ink = AppTheme.Warning;

        if (_rail)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var side = UiLayout.AtDpi(DotSide);
            var dot = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);

            using (var brush = new SolidBrush(ink))
            {
                g.FillEllipse(brush, dot);
            }

            if (Focused && ShowFocusCues)
            {
                var ring = Rectangle.Inflate(dot, UiLayout.AtDpi(3), UiLayout.AtDpi(3));

                using var pen = new Pen(AppTheme.SidebarText, Math.Max(1, UiLayout.AtDpi(1)));
                g.DrawEllipse(pen, ring);
            }

            return;
        }

        var font = AppTheme.SmallFont;

        TextRenderer.DrawText(g, Text, Lit ? Underlined(font) : font, ClientRectangle, ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>The small face underlined, made once for each small face (it is replaced when the text size changes).</summary>
    private Font Underlined(Font font)
    {
        if (_underlined is null || !Equals(_underlined.FontFamily, font.FontFamily) || _underlined.Size != font.Size)
        {
            _underlined?.Dispose();
            _underlined = new Font(font, FontStyle.Underline);
        }

        return _underlined;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _underlined?.Dispose();
            _underlined = null;
        }

        base.Dispose(disposing);
    }
}
