namespace HeatCaller;

/// <summary>
/// The month's allowances at a glance, for the foot of the sidebar and
/// Mission Control: which meters have a figure, what they come to as one
/// short line ("AI 47% · Voice 34%"), and the worst of them. Pure, so the
/// wording and the colours are tested without a window.
/// </summary>
internal static class UsageGlance
{
    /// <summary>The one-word name a meter goes by on the chip.</summary>
    internal static string ShortName(MeterKind kind) => kind switch
    {
        MeterKind.Commentary => "AI",
        MeterKind.HeatCallerVoice => "Voice",
        _ => "ElevenLabs",
    };

    /// <summary>
    /// The meters with a real figure: read, and on the club's plan. One we
    /// could not read, or one this plan does not include, has no
    /// percentage to put on the chip.
    /// </summary>
    internal static IReadOnlyList<Meter> Measured(IEnumerable<Meter> meters) =>
        meters.Where(m => m.Level != MeterLevel.Unknown).ToList();

    /// <summary>
    /// "AI 47% · Voice 34%" - or empty when nothing is measured, and the chip
    /// is hidden. A meter that could not be READ is there too, as
    /// "ElevenLabs ?" (19 Oct 2026): left out, a key that can't read its
    /// own plan looked like a meter that had never been built, and the
    /// reason was a click away in a window nobody had a cue to open. One
    /// that is simply not on the plan is still left out - there is nothing
    /// to read.
    /// </summary>
    internal static string ChipText(IEnumerable<Meter> meters) =>
        string.Join(" · ", meters
            .Where(m => m.Level != MeterLevel.Unknown || Unread(m))
            .Select(m => Unread(m) ? $"{ShortName(m.Kind)} ?" : $"{ShortName(m.Kind)} {m.ShortPercent()}"));

    /// <summary>A meter there is a reason for not having a figure for - ElevenLabs refusing to show the plan, say.</summary>
    private static bool Unread(Meter meter) => !meter.Known && !string.IsNullOrWhiteSpace(meter.Note);

    /// <summary>The worst level among the measured meters; Unknown when there are none.</summary>
    internal static MeterLevel Worst(IEnumerable<Meter> meters) =>
        Measured(meters).Select(m => m.Level).DefaultIfEmpty(MeterLevel.Unknown).Max();

    /// <summary>What the chip says on hover: each meter in full, and what a click does.</summary>
    internal static string Tooltip(IEnumerable<Meter> meters)
    {
        var all = meters.ToList();
        var lines = Measured(all).Select(m => $"{UsageLine.TitleFor(m)} - {m.Summary()}").ToList();

        lines.AddRange(all.Where(Unread).Select(m => $"{m.Name} - {m.Note}"));

        lines.Add("Click for this month's allowances.");

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// The chip at the foot of the sidebar: "AI 47% · Voice 34%", in the
/// sidebar's muted text while there is plenty left, amber once any meter is
/// at 80% and red once one is used up. In the narrow rail (Ctrl+B) there is
/// no room for words, so it is a dot in the same colour - green while all
/// is well - with the words on hover. Either way a click opens the This
/// Month window (MainForm.ShowThisMonth).
///
/// Drawn rather than a Label so it keeps the sidebar's colours whatever the
/// theme pass does to the content's (the sidebar is dark chrome in every
/// look - MainForm.RestyleSidebarChrome), and placed by hand with the
/// footer buttons under it (MainForm.PlaceWideFooter / PlaceRailFooter).
/// </summary>
internal sealed class UsageChip : Control
{
    /// <summary>The dot's side in the rail, at the screen's scale - the footer is sized at DPI, not the text size.</summary>
    private const int DotSide = 10;

    private MeterLevel _level = MeterLevel.Unknown;
    private bool _rail;
    private bool _hover;

    public UsageChip()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw
                 | ControlStyles.Selectable, true);

        DoubleBuffered = true;

        // Reachable by keyboard like the footer buttons under it: Tab to
        // it, Enter or Space opens This Month.
        TabStop = true;
        Cursor = Cursors.Hand;
        Tag = AppTheme.KeepBackColor;
        AccessibleRole = AccessibleRole.Link;
        Visible = false;
    }

    /// <summary>Shows the meters. Says nothing when none has a figure - the sidebar hides it then (MainForm.ShowUsageChip). UI thread.</summary>
    public void Show(IReadOnlyList<Meter> meters)
    {
        var text = UsageGlance.ChipText(meters);
        var level = UsageGlance.Worst(meters);

        if (Text != text)
        {
            Text = text;
        }

        AccessibleName = text.Length > 0 ? "This month: " + text : "This month";

        _level = level;
        Invalidate();
    }

    /// <summary>The colour of what the chip says, for the sidebar's dark background.</summary>
    internal static Color InkFor(MeterLevel level, bool rail) => level switch
    {
        MeterLevel.Spent => AppTheme.Danger,
        MeterLevel.Watch => AppTheme.Warning,
        _ => rail ? AppTheme.Success : AppTheme.SidebarTextMuted,
    };

    /// <summary>The rail's dot, rather than the words.</summary>
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

    /// <summary>The height the chip needs: a line of the small face in the open sidebar, the dot and room for its focus ring in the rail.</summary>
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

    /// <summary>Shown as the pointer shows it - brighter and underlined, or a ring round the dot - while the keyboard is on it.</summary>
    private bool Lit => _hover || (Focused && ShowFocusCues);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        g.Clear(AppTheme.SidebarBackground);

        var ink = InkFor(_level, _rail);

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

        // Brighter under the pointer, so it reads as something to click.
        var colour = Lit && (_level is MeterLevel.Ok or MeterLevel.Unknown) ? AppTheme.SidebarText : ink;
        var font = AppTheme.SmallFont;

        TextRenderer.DrawText(g, Text, Lit ? Underlined(font) : font, ClientRectangle, colour,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private Font? _underlined;

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
