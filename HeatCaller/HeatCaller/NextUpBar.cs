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
    /// <param name="Timing">What is wrong with timing (TimingWatch); empty when nothing is. While it is not, the whole bar is an alarm - see PaintAlarm.</param>
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

    /// <summary>
    /// Drives the alarm's motion - the pulse and the moving stripes - while
    /// a timing problem is showing, and only then. Stopped the moment the
    /// problem clears, so a healthy race costs nothing.
    /// </summary>
    private readonly System.Windows.Forms.Timer _alarmTimer = new() { Interval = 40 };

    /// <summary>When the alarm started, for the pulse's phase and the stripes' travel.</summary>
    private long _alarmStartTicks;

    /// <summary>One full pulse, dim to bright and back. Slow on purpose: a glow, not a strobe - well under the three flashes a second that can trigger seizures.</summary>
    private const double PulseSeconds = 1.1;

    /// <summary>How wide the hazard band down the left end is, and how far apart its stripes.</summary>
    private const int HazardWidth = 34;
    private const int StripeGap = 14;

    /// <summary>True while a timing problem is showing and the bar is an alarm.</summary>
    public bool Alarming => !string.IsNullOrEmpty(_info.Timing);

    public NextUpBar()
    {
        _alarmTimer.Tick += (_, _) => Invalidate();

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

    /// <summary>
    /// Shows or hides a button already added - for one that is only there
    /// some of the time (11 Nov 2026: Score by Hand, while the decoder is
    /// down - see HandScoring). A hidden button takes no room, so the text
    /// keeps the width it always had the rest of the night.
    /// </summary>
    public void ShowButton(Button button, bool shown)
    {
        if (shown ? _hidden.Remove(button) : _hidden.Add(button))
        {
            button.Visible = shown;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>The buttons hidden with <see cref="ShowButton"/>. Kept here rather than read from Visible, which is false for every child while the bar itself is not showing.</summary>
    private readonly HashSet<Button> _hidden = new();

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

        var anyShown = false;

        for (var i = _buttons.Count - 1; i >= 0; i--)
        {
            var button = _buttons[i].Button;

            if (_hidden.Contains(button))
            {
                continue;
            }

            anyShown = true;
            left = right - button.Width;
            button.Location = new Point(left, Math.Max(0, (Height - button.Height) / 2));
            right = left - UiLayout.ButtonGap;
        }

        var reserved = !anyShown ? 0 : Width - left + Edge;

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

        var wasAlarming = Alarming;
        _info = info;

        // The motion runs only while there is something to shout about.
        if (Alarming && !wasAlarming)
        {
            _alarmStartTicks = Environment.TickCount64;
            _alarmTimer.Start();
        }
        else if (!Alarming && wasAlarming)
        {
            _alarmTimer.Stop();
        }

        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _alarmTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Where the alarm's pulse is, 0 (its deepest red) to 1 (its brightest),
    /// <paramref name="elapsedMs"/> into it. A smooth swell rather than an
    /// on/off flash.
    /// </summary>
    internal static double PulseAt(long elapsedMs)
    {
        var phase = elapsedMs / 1000.0 / PulseSeconds * 2 * Math.PI;
        return (1 - Math.Cos(phase)) / 2;
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

        if (Alarming)
        {
            PaintAlarm(g);
            return;
        }

        AppTheme.DrawCard(g, new RectangleF(0, 0, Width, Height));

        var right = Width - Math.Max(0, ReservedRight) - 12;

        // On the same left edge as the captions of the cards below it.
        var x = Section.Inset.Left;

        x = Block(g, x, right, "NOW", _info.Now, AppTheme.TextDark);
        x = Block(g, x, right, "NEXT", _info.Next, _info.Warning ? AppTheme.Warning : AppTheme.TextDark);
        Block(g, x, right, "DAY", _info.Day, AppTheme.TextMuted);
    }

    /// <summary>
    /// The bar as an alarm: laps are not being counted, and a line of red
    /// text among the others was too easy to miss - rehearsal 4 staged a
    /// heat with the decoder unplugged and nobody noticed. So the whole bar
    /// turns red and swells, with hazard stripes running down its left
    /// end, and says what is wrong in white. NOW and NEXT give way to it
    /// until timing is right again; they come straight back after.
    /// </summary>
    private void PaintAlarm(Graphics g)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var elapsed = Environment.TickCount64 - _alarmStartTicks;
        var pulse = PulseAt(elapsed);

        // From the theme's deep red up to its red lifted toward white, so it
        // reads as an alarm in every look, light or dark.
        var deep = AppTheme.DangerDark;
        var bright = Looks.Mix(AppTheme.Danger, Color.White, 0.18);
        var fill = Looks.Mix(deep, bright, pulse);
        // One ink for the whole swell: chosen per frame it could flip between
        // black and white halfway through a pulse.
        var ink = AppTheme.OnDanger;

        var bounds = RectangleF.Inflate(new RectangleF(0, 0, Width, Height), -0.5f, -0.5f);
        using var card = AppTheme.RoundedRect(bounds, AppTheme.CornerRadius);

        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, card);
        }

        // Hazard stripes down the left end, sliding along as they go.
        var saved = g.Save();
        g.SetClip(card);
        g.SetClip(new RectangleF(0, 0, HazardWidth, Height), System.Drawing.Drawing2D.CombineMode.Intersect);

        using (var yellow = new SolidBrush(Color.FromArgb(255, 214, 0)))
        using (var black = new SolidBrush(Color.FromArgb(20, 20, 20)))
        {
            g.FillRectangle(black, 0, 0, HazardWidth, Height);

            var travel = (int)(elapsed / 30 % (StripeGap * 2));

            for (var x = -Height - StripeGap * 2 + travel; x < HazardWidth + Height; x += StripeGap * 2)
            {
                g.FillPolygon(yellow, new[]
                {
                    new PointF(x, Height),
                    new PointF(x + StripeGap, Height),
                    new PointF(x + StripeGap + Height, 0),
                    new PointF(x + Height, 0),
                });
            }
        }

        g.Restore(saved);

        using (var edge = new Pen(Looks.Mix(fill, Color.White, 0.5), 2f))
        {
            g.DrawPath(edge, card);
        }

        var left = HazardWidth + 14;
        var right = Width - Math.Max(0, ReservedRight) - 12;

        TextRenderer.DrawText(
            g, AppTheme.Spaced("TIMING"), AppTheme.MicroFont, new Point(left, CaptionTop), ink,
            TextFormatFlags.NoPadding);

        var text = new Rectangle(left, TextTop, Math.Max(1, right - left), Math.Max(1, Height - TextTop - 2));

        TextRenderer.DrawText(
            g, _info.Timing, AppTheme.BoldFont, text, ink,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
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
