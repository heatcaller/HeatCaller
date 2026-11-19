using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// The app's own tick box: a rounded square in the theme's colours - the
/// page's surface with a muted edge when it is empty, the theme's pink with a
/// drawn tick when it is ticked - the size of Windows' own (13px) at the
/// normal text size, and growing with the text.
///
/// Windows' glyph stayed 13px at every text size, so at 200% a check box was a
/// speck beside a caption twice its height, and it was drawn in Windows' own
/// colours - a white box on the dark looks (Asher, 30 Sep: "theme all tick
/// boxes"). Every check box, every list of them and every tick column in a
/// grid is drawn with this: <see cref="ThemedCheckBox"/>,
/// <see cref="ThemedCheckedListBox"/>, AppTheme's grid painting, and the lists
/// that draw their own rows (Manage Navigation, Choose columns). The menus'
/// ticks (AppTheme's menu renderer) are the same shape and colours, and a
/// radio button's dot (<see cref="RadioDot"/>) is its round sister, the same
/// size and a gap the same width before its caption.
/// </summary>
internal static class TickBox
{
    /// <summary>The box's side: Windows' own 13px at the normal text size, at the text size in use.</summary>
    public static int Side => UiLayout.AtTextSize(13);

    /// <summary>Between the box and its caption.</summary>
    public static int Gap => UiLayout.AtTextSize(4);

    /// <summary>
    /// How far in a part that belongs to the tick box or radio button above
    /// it sits - a voice's settings under its choice, a note under its tick:
    /// the app's usual step in (UiLayout.Indent), or, once the box and the
    /// gap after it have grown past that with the text (150%, 200%), as far
    /// in as the caption starts, so the part never sits under the box itself.
    /// 24 at the normal text size, as it always was.
    /// </summary>
    public static int CaptionIndent => UnderCaption(UiLayout.Indent);

    /// <summary>
    /// <paramref name="least"/> - a step in, in pixels, that was worked out
    /// for Windows' own 13px box, which never grew - or, once the app's box
    /// and the gap after it take more (at the larger text sizes), as far in
    /// as the caption starts. So what belongs to a tick box's words stays
    /// under them at every text size, and nothing moves at the normal one.
    /// </summary>
    public static int UnderCaption(int least) => Math.Max(least, Side + Gap);

    /// <summary>
    /// Draws the box in <paramref name="box"/> (a square, or it is drawn in
    /// the square that fits it, centred). <paramref name="hot"/> is the mouse
    /// over it: the empty box's edge takes the theme's pink, as a hint that
    /// it can be ticked.
    /// </summary>
    public static void Draw(Graphics g, Rectangle box, CheckState state, bool enabled, bool hot = false)
    {
        var side = Math.Min(box.Width, box.Height);

        if (side < 4)
        {
            return;
        }

        var square = new Rectangle(box.X + (box.Width - side) / 2, box.Y + (box.Height - side) / 2, side, side);

        var smoothing = g.SmoothingMode;
        var offset = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        try
        {
            var ticked = state != CheckState.Unchecked;

            // Greyed by mixing towards the surface rather than by a second
            // set of colours, so a disabled box reads as the same box, asleep.
            var ink = enabled ? AppTheme.Primary : Mix(AppTheme.Primary, AppTheme.Surface, 0.5f);
            var edge = ticked
                ? ink
                : enabled
                    ? hot ? AppTheme.Primary : AppTheme.TextMuted
                    : Mix(AppTheme.TextMuted, AppTheme.Surface, 0.55f);

            // Half a pixel in, so the 1px edge sits on whole pixels.
            var outline = new RectangleF(square.X + 0.5f, square.Y + 0.5f, square.Width - 1f, square.Height - 1f);
            var radius = Math.Max(2, (int)Math.Round(side * 0.24f));

            using (var path = AppTheme.RoundedRect(outline, radius))
            {
                using (var fill = new SolidBrush(ticked ? ink : AppTheme.Surface))
                {
                    g.FillPath(fill, path);
                }

                using var pen = new Pen(edge, Math.Max(1f, UiLayout.AtDpi(1)));
                g.DrawPath(pen, path);
            }

            if (state == CheckState.Checked)
            {
                // The menus' tick (AppTheme's menu renderer), so the two read
                // as one mark.
                using var pen = new Pen(AppTheme.OnPrimary, Math.Max(1.6f, side / 7f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                };

                g.DrawLines(pen, new[]
                {
                    new PointF(square.Left + side * 0.26f, square.Top + side * 0.52f),
                    new PointF(square.Left + side * 0.44f, square.Top + side * 0.70f),
                    new PointF(square.Left + side * 0.76f, square.Top + side * 0.32f),
                });
            }
            else if (state == CheckState.Indeterminate)
            {
                // Some, not all: a bar across the middle.
                var bar = Math.Max(2f, side / 6f);

                using var fill = new SolidBrush(AppTheme.OnPrimary);
                g.FillRectangle(fill, square.Left + side * 0.24f, square.Top + (side - bar) / 2f, side * 0.52f, bar);
            }
        }
        finally
        {
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
        }
    }

    /// <summary><paramref name="a"/> moved <paramref name="amount"/> of the way to <paramref name="b"/>.</summary>
    internal static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * amount),
        (int)Math.Round(a.G + (b.G - a.G) * amount),
        (int)Math.Round(a.B + (b.B - a.B) * amount));
}

/// <summary>
/// The app's own radio button dot: a circle in the theme's colours - the
/// page's surface with a muted edge when it is not the choice, the theme's
/// pink with a dot in the middle when it is - the size of Windows' own (13px)
/// at the normal text size, and growing with the text, as
/// <see cref="TickBox"/> does for a tick box (Asher, 30 Sep: "Theme the radio
/// buttons as well"). Drawn by <see cref="ThemedRadioButton"/>.
/// </summary>
internal static class RadioDot
{
    /// <summary>
    /// Draws the dot in <paramref name="box"/> (a square, or it is drawn in
    /// the square that fits it, centred). <paramref name="hot"/> is the mouse
    /// over it: the empty circle's edge takes the theme's pink, as a hint
    /// that it can be picked - as the tick box does.
    /// </summary>
    public static void Draw(Graphics g, Rectangle box, bool chosen, bool enabled, bool hot = false)
    {
        var side = Math.Min(box.Width, box.Height);

        if (side < 4)
        {
            return;
        }

        var square = new Rectangle(box.X + (box.Width - side) / 2, box.Y + (box.Height - side) / 2, side, side);

        var smoothing = g.SmoothingMode;
        var offset = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        try
        {
            // The tick box's colours, greyed the same way (TickBox.Draw), so
            // the two read as one family on a page that has both.
            var ink = enabled ? AppTheme.Primary : TickBox.Mix(AppTheme.Primary, AppTheme.Surface, 0.5f);
            var edge = chosen
                ? ink
                : enabled
                    ? hot ? AppTheme.Primary : AppTheme.TextMuted
                    : TickBox.Mix(AppTheme.TextMuted, AppTheme.Surface, 0.55f);

            // Half a pixel in, so the 1px edge sits on whole pixels.
            var outline = new RectangleF(square.X + 0.5f, square.Y + 0.5f, square.Width - 1f, square.Height - 1f);

            using (var fill = new SolidBrush(chosen ? ink : AppTheme.Surface))
            {
                g.FillEllipse(fill, outline);
            }

            using (var pen = new Pen(edge, Math.Max(1f, UiLayout.AtDpi(1))))
            {
                g.DrawEllipse(pen, outline);
            }

            if (chosen)
            {
                // The dot: two fifths of the circle, in the colour the tick
                // is drawn in.
                var dot = side * 0.4f;

                using var mark = new SolidBrush(AppTheme.OnPrimary);
                g.FillEllipse(mark, square.X + (side - dot) / 2f, square.Y + (side - dot) / 2f, dot, dot);
            }
        }
        finally
        {
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
        }
    }
}

/// <summary>
/// How <see cref="ThemedCheckBox"/> and <see cref="ThemedRadioButton"/> lay
/// out and paint their box (or dot) and caption - one set of rules for both,
/// so a tick box and a radio button beside it line up to the pixel. The two
/// are different Windows classes with nothing in common but ButtonBase, so
/// what only each has (its box's alignment, how the box looks, the protected
/// keyboard and focus cues) is handed in.
///
/// The size: as wide as the box, a gap and the caption (wrapped to the width
/// it is given, as Windows' own wraps), and as tall as the taller of the box
/// and the caption - and never smaller than Windows' own control asked for,
/// so nothing on a page gets less room than it had. Windows counts a little
/// room round the caption that this does not draw, so at the normal text
/// size Windows' own is the larger and nothing moves at all; at the largest
/// text sizes the drawn box is twice Windows' glyph and may take a few
/// pixels more across. The layout test holds every page and window to the
/// size asked for.
/// </summary>
internal static class ToggleCaption
{
    /// <summary>
    /// Whether the control draws itself or leaves it to Windows: shown as a
    /// button (Appearance.Button), drawn by Windows (FlatStyle.System), right
    /// to left, on a see-through background (Windows paints what is behind
    /// it, which this does not), or with its box anywhere but on the left -
    /// none is in the app today.
    /// </summary>
    public static bool DrawnHere(ButtonBase control, Appearance appearance, ContentAlignment boxAlign) =>
        appearance == Appearance.Normal
        && control.FlatStyle != FlatStyle.System
        && control.RightToLeft != RightToLeft.Yes
        && control.BackColor.A == 255
        && boxAlign is ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft;

    /// <summary>
    /// How the caption is measured and drawn - the same flags both times, so
    /// it fits exactly where it was measured to. <paramref name="hidePrefix"/>
    /// is the control's UseMnemonic and not ShowKeyboardCues (a protected
    /// member, so the control works it out - and reads ShowKeyboardCues only
    /// when UseMnemonic is on, as Windows' own does).
    /// </summary>
    public static TextFormatFlags Flags(ButtonBase control, bool hidePrefix)
    {
        // TextBoxControl as Windows' own caption has it: a word too long for
        // the room is broken rather than cut off at the edge.
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;

        if (!control.UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }
        else if (hidePrefix)
        {
            flags |= TextFormatFlags.HidePrefix;
        }

        flags |= control.TextAlign switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => TextFormatFlags.HorizontalCenter,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.Left,
        };

        if (control.AutoEllipsis && !control.AutoSize)
        {
            flags |= TextFormatFlags.EndEllipsis;
        }

        return flags;
    }

    /// <summary>
    /// The size the control asks for (see the class notes).
    /// <paramref name="flags"/> are the caption's (Flags), and go unused when
    /// there is no caption. <paramref name="windows"/> is what Windows' own
    /// control asks for at the same proposed size (base.GetPreferredSize,
    /// which only the control can call).
    /// </summary>
    /// <summary>
    /// What a control measured to, by the size it was asked at, and what the
    /// measuring depended on - see Measured.
    /// </summary>
    private sealed class MeasureCache
    {
        public (string Text, Font Font, Padding Padding, TextFormatFlags Flags, Size Min, Size Max, int Side, int Gap) Basis;
        public readonly Dictionary<Size, Size> Sizes = new();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ButtonBase, MeasureCache> Measures = new();

    /// <summary>
    /// PreferredSize, worked out once for each size asked while nothing it
    /// depends on - the caption, font, padding, flags, least and most sizes,
    /// the box's own size - has changed. <paramref name="windows"/> is only
    /// asked when it has to be worked out.
    ///
    /// Why (18 Nov 2026): layout asks a tick box its size many times over,
    /// and each time the caption was measured afresh, and Windows' own size
    /// asked as well - 41% of Import Entries' opening (a CPU profile), and
    /// the radio buttons a good part of Print's.
    /// </summary>
    public static Size Measured(ButtonBase control, Size proposedSize, TextFormatFlags flags, Func<Size> windows)
    {
        var basis = (control.Text ?? string.Empty, control.Font, control.Padding, flags, control.MinimumSize, control.MaximumSize, TickBox.Side, TickBox.Gap);
        var cache = Measures.GetOrCreateValue(control);

        if (cache.Basis != basis)
        {
            cache.Sizes.Clear();
            cache.Basis = basis;
        }

        if (cache.Sizes.TryGetValue(proposedSize, out var known))
        {
            return known;
        }

        // A box being resized by hand can ask at every width there is.
        if (cache.Sizes.Count > 64)
        {
            cache.Sizes.Clear();
        }

        var size = PreferredSize(control, proposedSize, flags, windows());
        cache.Sizes[proposedSize] = size;
        return size;
    }

    public static Size PreferredSize(ButtonBase control, Size proposedSize, TextFormatFlags flags, Size windows)
    {
        var hasCaption = !string.IsNullOrEmpty(control.Text);

        // The box, and the gap after it when there is a caption.
        var chrome = control.Padding.Horizontal + TickBox.Side + (hasCaption ? TickBox.Gap : 0);

        // A width of 0 or 1, or a huge one, is WinForms asking without a
        // limit.
        int? limit = proposedSize.Width is > 1 and < int.MaxValue / 2 ? proposedSize.Width : null;

        if (control.MaximumSize.Width > 0)
        {
            limit = limit is { } given ? Math.Min(given, control.MaximumSize.Width) : control.MaximumSize.Width;
        }

        var caption = Size.Empty;

        if (hasCaption)
        {
            var room = limit is { } width ? Math.Max(1, width - chrome) : int.MaxValue;

            caption = TextRenderer.MeasureText(control.Text, control.Font, new Size(room, int.MaxValue), flags);

            // A pixel of slack: the caption is drawn in exactly this width,
            // and a rounding pixel short of it would put its last word on a
            // line of its own.
            caption.Width += 1;
        }

        var size = new Size(chrome + caption.Width, Math.Max(TickBox.Side, caption.Height) + control.Padding.Vertical);

        size = new Size(Math.Max(size.Width, windows.Width), Math.Max(size.Height, windows.Height));

        // Never wider than it was given: a caption that fills its room to the
        // pixel came back a pixel over with the slack, and it already fits.
        if (limit is { } most)
        {
            size.Width = Math.Min(size.Width, Math.Max(most, chrome));
        }

        if (control.MaximumSize.Width > 0)
        {
            size.Width = Math.Min(size.Width, control.MaximumSize.Width);
        }

        if (control.MaximumSize.Height > 0)
        {
            size.Height = Math.Min(size.Height, control.MaximumSize.Height);
        }

        return new Size(Math.Max(size.Width, control.MinimumSize.Width), Math.Max(size.Height, control.MinimumSize.Height));
    }

    /// <summary>
    /// Paints the control: its background, the box (by <paramref name="drawBox"/>,
    /// in the square it is given) and the caption after it.
    /// <paramref name="showFocus"/> is the control's Focused and ShowFocusCues
    /// (protected) together.
    /// </summary>
    public static void Paint(ButtonBase control, Graphics g, ContentAlignment boxAlign, TextFormatFlags flags, bool showFocus,
        Action<Graphics, Rectangle> drawBox)
    {
        using (var back = new SolidBrush(control.BackColor))
        {
            g.FillRectangle(back, control.ClientRectangle);
        }

        var padding = control.Padding;
        var inner = new Rectangle(
            padding.Left,
            padding.Top,
            Math.Max(0, control.ClientSize.Width - padding.Horizontal),
            Math.Max(0, control.ClientSize.Height - padding.Vertical));

        // Never taller than the room it has: a control sized by hand, shorter
        // than the text size's box, gets a smaller box rather than a clipped
        // one.
        var side = Math.Max(0, Math.Min(TickBox.Side, inner.Height));
        var text = control.Text;
        var hasCaption = !string.IsNullOrEmpty(text);
        var textLeft = inner.X + side + (hasCaption ? TickBox.Gap : 0);
        var textWidth = Math.Max(0, inner.Right - textLeft);

        // Where the caption goes, and how tall it comes out at that width:
        // wrapped text cannot be centred by TextRenderer, so it is placed by
        // hand, as the caption's TextAlign says.
        var measured = hasCaption && textWidth > 0
            ? TextRenderer.MeasureText(g, text, control.Font, new Size(textWidth, int.MaxValue), flags)
            : Size.Empty;
        var textHeight = Math.Min(inner.Height, measured.Height);

        var textTop = control.TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => inner.Y,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => inner.Bottom - textHeight,
            _ => inner.Y + (inner.Height - textHeight) / 2,
        };

        // The box beside the caption's first line when it sits at the top, at
        // the foot when at the bottom, and in the middle otherwise - as
        // Windows places its own. Except a caption that has wrapped: its box
        // goes beside the first line, where the sentence starts, not in the
        // gap between two lines (Manage Navigation's "While a race day is
        // open..." at every text size - layout pictures, 30 Sep).
        var lineHeight = Pieces.LineHeight(control.Font);
        var firstLine = hasCaption ? Math.Min(textHeight, lineHeight) : side;
        var wrapped = hasCaption && textHeight > lineHeight * 3 / 2;

        var boxTop = boxAlign switch
        {
            ContentAlignment.TopLeft => (hasCaption ? textTop : inner.Y) + Math.Max(0, (firstLine - side) / 2),
            ContentAlignment.BottomLeft => inner.Bottom - side,
            _ when wrapped => textTop + Math.Max(0, (firstLine - side) / 2),
            _ => inner.Y + (inner.Height - side) / 2,
        };

        drawBox(g, new Rectangle(inner.X, boxTop, side, side));

        var ink = control.Enabled ? control.ForeColor : AppTheme.TextMuted;

        if (hasCaption && textWidth > 0 && textHeight > 0)
        {
            TextRenderer.DrawText(g, text, control.Font, new Rectangle(textLeft, textTop, textWidth, textHeight), ink, flags);

            if (showFocus)
            {
                // Round the caption, and inside the control's own edge.
                var focus = Rectangle.Intersect(
                    new Rectangle(textLeft - 1, textTop, Math.Min(textWidth, measured.Width) + 2, textHeight),
                    control.ClientRectangle);

                ControlPaint.DrawFocusRectangle(g, focus, ink, control.BackColor);
            }
        }
        else if (showFocus && side > 0)
        {
            // No caption to ring (a yes/no field whose caption sits in the
            // grid beside it): the box itself, a pixel out, as far as the
            // control's edge allows.
            var focus = Rectangle.Intersect(
                new Rectangle(inner.X - 1, boxTop - 1, side + 2, side + 2),
                control.ClientRectangle);

            ControlPaint.DrawFocusRectangle(g, focus, ink, control.BackColor);
        }
    }
}

/// <summary>
/// A check box drawn with the app's own <see cref="TickBox"/> - see there for
/// why. Everything else about it is Windows' CheckBox: its events, its
/// keyboard handling, its three states, its caption and mnemonic. It lays
/// itself out and paints as <see cref="ToggleCaption"/> says, since the box
/// is bigger than Windows' own at a larger text size and the caption has to
/// start after it.
/// </summary>
internal class ThemedCheckBox : CheckBox
{
    /// <summary>The mouse is over it - see TickBox.Draw.</summary>
    private bool _hot;

    public ThemedCheckBox()
    {
        // Painted here in full, off screen, so nothing of Windows' own box
        // shows through for a frame.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Whether this draws itself, or leaves it to Windows - see ToggleCaption.DrawnHere.</summary>
    private bool DrawnHere => ToggleCaption.DrawnHere(this, Appearance, CheckAlign);

    /// <summary>The caption's flags - see ToggleCaption.Flags.</summary>
    private TextFormatFlags CaptionFlags() => ToggleCaption.Flags(this, UseMnemonic && !ShowKeyboardCues);

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!DrawnHere)
        {
            return base.GetPreferredSize(proposedSize);
        }

        // With no caption there is nothing to measure, and nothing to read
        // the keyboard cues for.
        var flags = string.IsNullOrEmpty(Text) ? TextFormatFlags.Default : CaptionFlags();

        return ToggleCaption.Measured(this, proposedSize, flags, () => base.GetPreferredSize(proposedSize));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!DrawnHere)
        {
            base.OnPaint(e);
            return;
        }

        ToggleCaption.Paint(this, e.Graphics, CheckAlign, CaptionFlags(), Focused && ShowFocusCues,
            (g, box) => TickBox.Draw(g, box, CheckState, Enabled, _hot && Enabled));
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        // Hidden with the mouse over it, it hears no MouseLeave.
        if (!Visible)
        {
            _hot = false;
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }
}

/// <summary>
/// The tick box at the top of a column of them: ticks every box in the
/// column, or clears them all when they are all ticked already. It shows
/// the column - ticked when every box is, empty when none is, and the
/// part-way mark when some are - and follows it as the boxes change
/// (<see cref="Follow"/>).
///
/// It never ticks itself (AutoCheck off): a click sets the column, and the
/// column sets this. The column is changed inside <c>together</c>, so a page
/// that saves on every tick can save once for the lot.
/// </summary>
internal sealed class ColumnTickBox : ThemedCheckBox
{
    private readonly Func<IEnumerable<CheckBox>> _column;
    private readonly Action<Action> _together;

    public ColumnTickBox(Func<IEnumerable<CheckBox>> column, Action<Action>? together = null)
    {
        _column = column;
        _together = together ?? (change => change());

        AutoSize = true;
        AutoCheck = false;
        ThreeState = true;
    }

    /// <summary>The boxes in the column that can be changed now.</summary>
    private List<CheckBox> Boxes() => _column().Where(box => !box.IsDisposed).ToList();

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);

        var boxes = Boxes().Where(box => box.Enabled).ToList();

        if (boxes.Count == 0)
        {
            return;
        }

        var tick = !boxes.All(box => box.Checked);

        _together(() =>
        {
            foreach (var box in boxes)
            {
                box.Checked = tick;
            }
        });

        Sync();
    }

    /// <summary>Shows how much of the column is ticked.</summary>
    public void Sync()
    {
        if (IsDisposed)
        {
            return;
        }

        var boxes = Boxes();
        var ticked = boxes.Count(box => box.Checked);

        var state = ticked == 0
            ? CheckState.Unchecked
            : ticked == boxes.Count
                ? CheckState.Checked
                : CheckState.Indeterminate;

        if (CheckState != state)
        {
            CheckState = state;
        }
    }

    /// <summary>Keeps this in step with one of the column's boxes, however it is changed.</summary>
    public void Follow(CheckBox box) => box.CheckedChanged += (_, _) => Sync();
}

/// <summary>
/// A radio button drawn with the app's own <see cref="RadioDot"/> - see
/// there for why. Everything else about it is Windows' RadioButton: picking
/// it clears the others in the same container, its events, its arrow keys
/// and tab stop, its caption and mnemonic. It lays itself out and paints as
/// <see cref="ToggleCaption"/> says - exactly as a <see cref="ThemedCheckBox"/>
/// does, so the two line up.
/// </summary>
internal class ThemedRadioButton : RadioButton
{
    /// <summary>The mouse is over it - see RadioDot.Draw.</summary>
    private bool _hot;

    public ThemedRadioButton()
    {
        // Painted here in full, off screen, so nothing of Windows' own circle
        // shows through for a frame.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Whether this draws itself, or leaves it to Windows - see ToggleCaption.DrawnHere.</summary>
    private bool DrawnHere => ToggleCaption.DrawnHere(this, Appearance, CheckAlign);

    /// <summary>The caption's flags - see ToggleCaption.Flags.</summary>
    private TextFormatFlags CaptionFlags() => ToggleCaption.Flags(this, UseMnemonic && !ShowKeyboardCues);

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!DrawnHere)
        {
            return base.GetPreferredSize(proposedSize);
        }

        // With no caption there is nothing to measure, and nothing to read
        // the keyboard cues for.
        var flags = string.IsNullOrEmpty(Text) ? TextFormatFlags.Default : CaptionFlags();

        return ToggleCaption.Measured(this, proposedSize, flags, () => base.GetPreferredSize(proposedSize));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!DrawnHere)
        {
            base.OnPaint(e);
            return;
        }

        ToggleCaption.Paint(this, e.Graphics, CheckAlign, CaptionFlags(), Focused && ShowFocusCues,
            (g, box) => RadioDot.Draw(g, box, Checked, Enabled, _hot && Enabled));
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        // Hidden with the mouse over it, it hears no MouseLeave.
        if (!Visible)
        {
            _hot = false;
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }
}

/// <summary>
/// A list of check boxes drawn with the app's own <see cref="TickBox"/>, one
/// per row, the item's text after it - see TickBox for why. Everything else is
/// Windows' CheckedListBox: its checked items, its events, its keyboard
/// handling. Every list in the app ticks on a click anywhere in a row
/// (CheckOnClick), so the bigger box at a larger text size needs no hit test
/// of its own.
/// </summary>
internal class ThemedCheckedListBox : CheckedListBox
{
    /// <summary>The row a press landed on, while a drag from it may tick others; -1 when none.</summary>
    private int _sweepFrom = -1;

    /// <summary>What a drag from <see cref="_sweepFrom"/> sets every row it passes to: the opposite of how that first row was.</summary>
    private CheckState _sweepTo;

    /// <summary>The last row the drag reached, so a fast flick fills in the rows it skipped.</summary>
    private int _sweepAt = -1;

    /// <summary>Every row this drag has set - set again on release, in case Windows' own click handling toggled one back.</summary>
    private readonly HashSet<int> _swept = new();

    /// <summary>
    /// Notes where a press lands and how that row was BEFORE Windows' own
    /// handling of the press ticks or unticks it - only here, ahead of
    /// base.WndProc, is that still knowable.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        const int WM_LBUTTONDOWN = 0x0201;

        if (m.Msg == WM_LBUTTONDOWN)
        {
            _swept.Clear();
            _sweepFrom = -1;
            _sweepAt = -1;

            if (CheckOnClick && Enabled)
            {
                var lParam = m.LParam.ToInt64();
                var index = IndexFromPoint((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));

                if (index >= 0 && index < Items.Count)
                {
                    _sweepFrom = index;
                    _sweepAt = index;
                    _sweepTo = GetItemCheckState(index) == CheckState.Checked ? CheckState.Unchecked : CheckState.Checked;
                }
            }
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// Click and drag down the list to tick - or untick - every row the
    /// pointer passes, the checked list's version of dragging out a
    /// selection (see DragSelect). Each row is set to the opposite of how
    /// the first one was, so a drag that starts on a ticked row unticks.
    /// Nothing happens until the pointer reaches another row, so an
    /// ordinary click is exactly what it always was.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_sweepFrom < 0 || (e.Button & MouseButtons.Left) == 0 || _sweepFrom >= Items.Count)
        {
            return;
        }

        // Held to the list's edge: past the top or bottom it reads the row
        // at the edge, as Windows scrolls the list under it.
        var x = Math.Clamp(e.X, 0, Math.Max(0, ClientSize.Width - 1));
        var y = Math.Clamp(e.Y, 0, Math.Max(0, ClientSize.Height - 1));
        var index = IndexFromPoint(x, y);

        if (index < 0 || index >= Items.Count || (index == _sweepFrom && _swept.Count == 0) || index == _sweepAt)
        {
            return;
        }

        _swept.Add(_sweepFrom);

        var step = index > _sweepAt ? 1 : -1;

        for (var i = _sweepAt; i != index + step; i += step)
        {
            _swept.Add(i);
        }

        _sweepAt = index;

        SetSwept();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        // Set once more: Windows ticks on the press and again as the
        // picked row moves, and whatever it toggled in passing is put back.
        if (_swept.Count > 0)
        {
            SetSwept();
        }

        _swept.Clear();
        _sweepFrom = -1;
        _sweepAt = -1;
    }

    /// <summary>Every swept row to the sweep's state - only those not already in it, so ItemCheck is raised once per real change.</summary>
    private void SetSwept()
    {
        foreach (var i in _swept)
        {
            if (i >= 0 && i < Items.Count && GetItemCheckState(i) != _sweepTo)
            {
                SetItemCheckState(i, _sweepTo);
            }
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        // Windows asks for a row with no item when the list is empty and has
        // the focus - nothing to draw.
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            return;
        }

        var g = e.Graphics;
        var row = e.Bounds;

        using (var back = new SolidBrush(BackColor))
        {
            g.FillRectangle(back, row);
        }

        var side = Math.Max(0, Math.Min(TickBox.Side, row.Height - 2));
        var inset = UiLayout.AtTextSize(2);
        var box = new Rectangle(row.X + inset, row.Y + (row.Height - side) / 2, side, side);

        TickBox.Draw(g, box, GetItemCheckState(e.Index), Enabled);

        var textLeft = box.Right + TickBox.Gap;
        var textBounds = new Rectangle(textLeft, row.Y, Math.Max(0, row.Right - textLeft), row.Height);
        var text = GetItemText(Items[e.Index]);
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        // A picked row is marked behind its text in the theme's soft tint, not
        // Windows' blue (14 Nov 2026); the box keeps the list's background so
        // it reads the same picked or not. The tick is what a checked list
        // means, so the picked row stays quiet, and there is no dotted focus
        // box - the tint already shows which row the keys act on.
        var ink = !Enabled ? AppTheme.TextMuted : ForeColor;

        if (selected)
        {
            using var mark = new SolidBrush(AppTheme.PrimaryTint);
            g.FillRectangle(mark, textBounds);
        }

        const TextFormatFlags Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        TextRenderer.DrawText(g, text, Font, textBounds, ink, Flags);
    }
}
