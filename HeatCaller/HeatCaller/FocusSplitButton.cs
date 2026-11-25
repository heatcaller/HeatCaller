using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// RACE CONTROL'S FOCUS BUTTON (24 Nov 2026, option B of six, asked for by
/// Asher: "I want to have one button press that expands the leaderboard and
/// loads in the docked inbox with a click"). A split button at the right of
/// the foot of the board, in place of Expand and the dock square:
///
/// - the main part, with a little drawing of the board and the docked inbox,
///   is <b>Focus</b> - the wide board and the inbox docked, in one press - and
///   reads <b>Restore</b> while focused;
/// - the arrow (or a right-click anywhere on it) opens the menu: Focus, Wide
///   board only, and Inbox only, each with a line saying what it does, the
///   last two ticked while they are on.
///
/// What each press does is the host's (FocusView, MainForm.Focus). Words the
/// room sees are set by <see cref="ShowState"/>.
/// </summary>
internal sealed class FocusSplitButton : Control
{
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _miFocus = new();
    private readonly ToolStripMenuItem _miWide = new();
    private readonly ToolStripMenuItem _miInbox = new();
    private readonly ToolTip _tip = new();

    /// <summary>Which part the mouse is over: 0 the main part, 1 the arrow, -1 neither.</summary>
    private int _hot = -1;

    /// <summary>Which part is held down, the same way.</summary>
    private int _down = -1;

    public FocusSplitButton()
    {
        Text = "Focus";

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.SplitButton;
        AccessibleName = "Focus";

        AppTheme.SetMenuNote(_miFocus, "Focus", "Wide board and the inbox docked");
        AppTheme.SetMenuNote(_miWide, "Wide board only", "Sidebar and clock column folded");
        AppTheme.SetMenuNote(_miInbox, "Inbox only", "Dock the inbox, keep the board as it is");

        _miFocus.Click += (_, _) => Asked?.Invoke(FocusAsk.Focus);
        _miWide.Click += (_, _) => Asked?.Invoke(FocusAsk.WideOnly);
        _miInbox.Click += (_, _) => Asked?.Invoke(FocusAsk.InboxOnly);

        _menu.Items.AddRange(new ToolStripItem[] { _miFocus, _miWide, _miInbox });
        AppTheme.StyleContextMenu(_menu);
        ContextMenuStrip = _menu;

        AppTheme.StyleToolTip(_tip, keep: true);
        ShowState(focused: false, wide: false, inboxDocked: false);
    }

    /// <summary>Pressed: the main part (Focus), or one of the menu's three.</summary>
    public event Action<FocusAsk>? Asked;

    /// <summary>The menu - for the tests.</summary>
    internal ContextMenuStrip Menu => _menu;

    /// <summary>Puts the words and ticks right for the view as it is now.</summary>
    public void ShowState(bool focused, bool wide, bool inboxDocked)
    {

        var caption = focused ? "Restore" : "Focus";

        if (Text != caption)
        {
            Text = caption;
        }

        AppTheme.SetMenuNote(_miFocus, caption, focused ? "Put the board and the inbox back as they were" : "Wide board and the inbox docked");
        _miWide.Checked = wide && !focused;
        _miInbox.Checked = inboxDocked && !focused;
        AppTheme.SetMenuNote(_miInbox, "Inbox only", inboxDocked ? "Undock the inbox, keep the board as it is" : "Dock the inbox, keep the board as it is");

        AccessibleName = caption;
        AccessibleDescription = "More views: press the arrow, or right-click.";
        _tip.SetToolTip(this, focused
            ? "Restore: the sidebar, the clock column and the inbox back as they were before Focus. The arrow has Wide board only and Inbox only."
            : "Focus: the sidebar narrowed, the clock column folded and the inbox docked down the right, in one press. The arrow has Wide board only and Inbox only.");

        Invalidate();
    }

    private int ArrowWidth => UiLayout.AtDpi(40);
    private int IconWidth => UiLayout.AtDpi(22);
    private int Pad => UiLayout.AtDpi(16);
    private int IconGap => UiLayout.AtDpi(10);

    private Font? _bold;
    private Font? _boldOf;

    private Font BoldFont
    {
        get
        {
            if (_bold is null || !ReferenceEquals(_boldOf, Font))
            {
                _bold?.Dispose();
                _bold = new Font(Font, FontStyle.Bold);
                _boldOf = Font;
            }

            return _bold;
        }
    }

    /// <summary>The main part is as wide as the wider of its two captions, so it doesn't jump between Focus and Restore.</summary>
    private int MainWidth
    {
        get
        {
            var widest = Math.Max(Measure("Focus").Width, Measure("Restore").Width);
            return Pad + IconWidth + IconGap + widest + Pad;
        }
    }

    private Size Measure(string text) =>
        TextRenderer.MeasureText(text, BoldFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

    public override Size GetPreferredSize(Size proposedSize) =>
        new(MainWidth + ArrowWidth, Measure("Focus").Height + UiLayout.AtDpi(11) * 2);

    private void AutoSizeNow()
    {
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        AutoSizeNow();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AutoSizeNow();
    }

    private Rectangle MainPart => new(0, 0, Width - ArrowWidth, Height);
    private Rectangle ArrowPart => new(Width - ArrowWidth, 0, ArrowWidth, Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            var radius = UiLayout.AtDpi(11);
            var all = new RectangleF(0, 0, Width, Height);

            using var path = AppTheme.RoundedRect(all, radius);
            var clip = g.Clip;
            g.SetClip(path, CombineMode.Intersect);

            var main = Enabled ? AppTheme.Primary : Blend(AppTheme.Primary, AppTheme.Surface, 0.5f);
            var arrow = Blend(main, Color.Black, 0.12f);

            if (_hot == 0 || _down == 0)
            {
                main = Blend(main, Color.White, _down == 0 ? 0.04f : 0.10f);
            }

            if (_hot == 1 || _down == 1)
            {
                arrow = Blend(arrow, Color.White, _down == 1 ? 0.04f : 0.10f);
            }

            using (var brush = new SolidBrush(main))
            {
                g.FillRectangle(brush, MainPart);
            }

            using (var brush = new SolidBrush(arrow))
            {
                g.FillRectangle(brush, ArrowPart);
            }

            // The hairline between the two parts.
            using (var divider = new Pen(Color.FromArgb(76, Color.White), Math.Max(1f, UiLayout.DpiScale)))
            {
                g.DrawLine(divider, ArrowPart.X, 0, ArrowPart.X, Height);
            }

            g.Clip = clip;

            if (Focused && ShowFocusCues)
            {
                using var ring = new Pen(AppTheme.Accent, Math.Max(1.5f, 2f * UiLayout.DpiScale));
                using var inner = AppTheme.RoundedRect(new RectangleF(1, 1, Width - 2, Height - 2), radius - 1);
                g.DrawPath(ring, inner);
            }

            var ink = AppTheme.OnPrimary;

            // The drawing: a window with a panel down its right-hand side - the
            // board and the docked inbox.
            var nudge = _down == 0 ? 1 : 0;
            var iconHeight = (int)Math.Round(IconWidth * 16 / 22f);
            var icon = new RectangleF(Pad, (Height - iconHeight) / 2f + nudge, IconWidth, iconHeight);
            var stroke = Math.Max(1.4f, 1.6f * UiLayout.DpiScale);

            using (var pen = new Pen(ink, stroke))
            using (var frame = AppTheme.RoundedRect(RectangleF.Inflate(icon, -stroke / 2, -stroke / 2), Math.Max(2, UiLayout.AtDpi(3))))
            {
                g.DrawPath(pen, frame);
            }

            using (var fill = new SolidBrush(ink))
            using (var panel = AppTheme.RoundedRect(new RectangleF(icon.Right - icon.Width * 0.36f, icon.Y + icon.Height * 0.19f, icon.Width * 0.23f, icon.Height * 0.62f), Math.Max(1, UiLayout.AtDpi(1))))
            {
                g.FillPath(fill, panel);
            }

            var textBox = new Rectangle(Pad + IconWidth + IconGap, nudge, MainPart.Width - (Pad + IconWidth + IconGap), Height);
            TextRenderer.DrawText(g, Text, BoldFont, textBox, ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            // The arrow: a small chevron, pointing up - the menu opens above,
            // into the board.
            var c = new PointF(ArrowPart.X + ArrowPart.Width / 2f, Height / 2f + (_down == 1 ? 1 : 0));
            var w = UiLayout.AtDpi(4);

            using (var pen = new Pen(ink, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new[] { new PointF(c.X - w, c.Y + w / 2f), new PointF(c.X, c.Y - w / 2f), new PointF(c.X + w, c.Y + w / 2f) });
            }
        }
        finally
        {
            g.SmoothingMode = smoothing;
        }
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    private int PartAt(Point p) => MainPart.Contains(p) ? 0 : ArrowPart.Contains(p) ? 1 : -1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = PartAt(e.Location);

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
        _down = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left && Enabled)
        {
            _down = PartAt(e.Location);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        var down = _down;
        _down = -1;
        Invalidate();

        if (e.Button != MouseButtons.Left || !Enabled || down < 0 || PartAt(e.Location) != down)
        {
            return;
        }

        if (down == 0)
        {
            Asked?.Invoke(FocusAsk.Focus);
        }
        else
        {
            OpenMenu();
        }
    }

    /// <summary>The menu, above the button and against its right-hand edge.</summary>
    internal void OpenMenu()
    {
        var size = _menu.GetPreferredSize(Size.Empty);
        _menu.Show(this, new Point(Width - size.Width, -size.Height - UiLayout.AtDpi(6)));
    }

    /// <summary>The main part's press, from the keyboard or a test.</summary>
    internal void PerformPress() => Asked?.Invoke(FocusAsk.Focus);

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Down or Keys.Up || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!Enabled)
        {
            return;
        }

        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Asked?.Invoke(FocusAsk.Focus);
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Down or Keys.Up)
        {
            OpenMenu();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bold?.Dispose();
            _menu.Dispose();
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
