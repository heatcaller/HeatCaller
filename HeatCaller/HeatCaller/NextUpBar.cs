namespace HeatCaller;

/// <summary>
/// The strip across the top of Race Control that always knows where the
/// day is: what is running NOW, what is NEXT and whether its drivers are
/// actually here, and how far through - and behind - the DAY is.
///
/// This is the part of running an event that every timing package leaves
/// in the race director's head or on a paper heat sheet. The information
/// all exists in the app already; this puts it where the person calling
/// the races is already looking.
///
/// Draw-only. The bar computes nothing - Race Control feeds it a finished
/// Info four times a second and it paints the latest one, so there is
/// exactly one place deciding what "next" means.
///
/// It lays out its own buttons, at its right-hand end, and is as tall as
/// its two lines of text or its buttons need at the text size in use - so
/// it goes in a page's pieces like any other part (see LayoutPieces.cs),
/// and a larger text size makes the bar taller rather than clipping it.
/// </summary>
public sealed class NextUpBar : Control
{
    /// <summary>One refresh's worth of text. Records compare by value, which is what makes the repaint-only-on-change below cheap.</summary>
    /// <param name="Timing">What is wrong with timing (TimingWatch), shown first and in red; empty when nothing is.</param>
    public sealed record Info(string Now, string Next, string Day, bool Warning, string Timing = "");

    private Info _info = new(string.Empty, string.Empty, string.Empty, false);

    /// <summary>The height the bar has at the normal text size, and never less.</summary>
    private const int LeastHeight = 52;

    /// <summary>Room between the buttons and the bar's edge - above, below and to the right - and between the text and the buttons.</summary>
    private const int Edge = 8;

    /// <summary>Where each segment's caption sits, down from the top.</summary>
    private const int CaptionTop = 7;

    /// <summary>The buttons at the right-hand end, left to right, each with the captions it is sized for.</summary>
    private readonly List<(Button Button, string[] Captions)> _buttons = new();

    /// <summary>Set while a button is being sized, so the size change it causes is not taken for a caption change.</summary>
    private bool _fitting;

    /// <summary>How much of the right end the buttons cover, so the text stops before them. Worked out as they are laid out.</summary>
    public int ReservedRight { get; private set; }

    public NextUpBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        DoubleBuffered = true;
        TabStop = false;
        Height = LeastHeight;
    }

    /// <summary>
    /// Puts a button at the right-hand end, to the right of any already
    /// there. Sized for the widest of <paramref name="captions"/> - every
    /// caption it will wear, so that swapping one for another never moves
    /// anything - and for the one it wears now, should that be wider. Sized
    /// again when its caption or its font changes.
    /// </summary>
    public void AddButton(Button button, params string[] captions)
    {
        _buttons.Add((button, captions));
        Controls.Add(button);

        button.TextChanged += ButtonChanged;
        button.FontChanged += ButtonChanged;

        Fit(button, captions);
    }

    /// <summary>Where the text under each caption starts.</summary>
    private static int TextTop => CaptionTop + AppTheme.MicroFont.Height + 1;

    private void ButtonChanged(object? sender, EventArgs e)
    {
        foreach (var (button, captions) in _buttons)
        {
            if (ReferenceEquals(button, sender))
            {
                Fit(button, captions);
                return;
            }
        }
    }

    private void Fit(Button button, string[] captions)
    {
        if (_fitting)
        {
            return;
        }

        _fitting = true;

        try
        {
            var size = UiLayout.ButtonSizeFor(button.Text);

            foreach (var caption in captions)
            {
                var fits = UiLayout.ButtonSizeFor(caption);
                size = new Size(Math.Max(size.Width, fits.Width), Math.Max(size.Height, fits.Height));
            }

            button.AutoSize = false;
            button.Size = size;
        }
        finally
        {
            _fitting = false;
        }

        UpdateHeight();
        PerformLayout();
    }

    /// <summary>
    /// As tall as the two lines of text - a caption over its text - or the
    /// tallest button, with room round them, and never under the height the
    /// bar has always had.
    ///
    /// Set as the bar's own Height rather than reported as a preferred size:
    /// the page's table takes a part that does not size itself at the
    /// height it was given, which is this.
    /// </summary>
    private void UpdateHeight()
    {
        var text = TextTop + AppTheme.BodyFont.Height + CaptionTop;
        var buttons = _buttons.Count == 0 ? 0 : _buttons.Max(entry => entry.Button.Height) + 2 * Edge;
        var wanted = Math.Max(LeastHeight, Math.Max(text, buttons));

        if (Height != wanted)
        {
            Height = wanted;
        }
    }

    /// <summary>A new text size: the window's font change reaches the bar, which has none of its own, and is the cue.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        UpdateHeight();
        Invalidate();
    }

    /// <summary>The buttons against the right-hand end, centred up and down, and the text told where to stop.</summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        var right = Width - Edge;
        var left = right;

        for (var i = _buttons.Count - 1; i >= 0; i--)
        {
            var button = _buttons[i].Button;

            left = right - button.Width;
            button.Location = new Point(left, Math.Max(0, (Height - button.Height) / 2));
            right = left - UiLayout.ButtonGap;
        }

        var reserved = _buttons.Count == 0 ? 0 : Width - left + Edge;

        if (ReservedRight != reserved)
        {
            ReservedRight = reserved;
            Invalidate();
        }
    }

    public void ShowInfo(Info info)
    {
        if (info == _info)
        {
            return;
        }

        _info = info;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        // A card, like the rest of the page's parts. The corners show
        // whatever the bar sits on, or the rounding reads as a notch.
        using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            g.FillRectangle(behind, ClientRectangle);
        }

        AppTheme.DrawCard(g, new RectangleF(0, 0, Width, Height));

        var right = Width - Math.Max(0, ReservedRight) - 12;

        // On the same left edge as the captions of the cards below it.
        var x = Section.Inset.Left;

        // First, and in red: laps not being counted outranks what is next.
        // It crowds the rest along the bar while it lasts, which is the
        // point - it goes when timing is right again.
        x = Block(g, x, right, "TIMING", _info.Timing, AppTheme.Danger, AppTheme.Danger);

        x = Block(g, x, right, "NOW", _info.Now, AppTheme.TextDark);
        x = Block(g, x, right, "NEXT", _info.Next, _info.Warning ? AppTheme.Warning : AppTheme.TextDark);
        Block(g, x, right, "DAY", _info.Day, AppTheme.TextMuted);
    }

    /// <summary>
    /// One captioned segment: a micro label over its text. Returns where
    /// the next segment starts, and simply stops drawing at the right
    /// boundary - a bar that wraps is a bar that pushed the flags off the
    /// page.
    /// </summary>
    private int Block(Graphics g, int x, int right, string caption, string text, Color textColour, Color? captionColour = null)
    {
        if (string.IsNullOrEmpty(text) || x >= right)
        {
            return x;
        }

        TextRenderer.DrawText(
            g, AppTheme.Spaced(caption), AppTheme.MicroFont, new Point(x, CaptionTop), captionColour ?? AppTheme.Primary,
            TextFormatFlags.NoPadding);

        var bounds = new Rectangle(x, TextTop, right - x, Math.Max(1, Height - TextTop - 2));

        TextRenderer.DrawText(
            g, text, AppTheme.BodyFont, bounds, textColour,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        var width = TextRenderer.MeasureText(
            g, text, AppTheme.BodyFont, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

        var captionWidth = TextRenderer.MeasureText(
            g, AppTheme.Spaced(caption), AppTheme.MicroFont, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding).Width;

        return Math.Min(right, x + Math.Max(width, captionWidth) + 28);
    }
}
