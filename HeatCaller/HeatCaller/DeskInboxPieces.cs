using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// The pieces the Desk Inbox is drawn with since its triage-split rework
/// (16 Nov 2026, Asher picked "A and C together" from four mockups): a
/// filter rail, a pill, key hints on buttons, a key strip, answers laid out
/// two to a row, quick-reply chips and a pop-over. Each draws from the
/// theme's colours at paint time, so every look gets them right.
/// </summary>
internal static class InboxInk
{
    /// <summary>A colour <paramref name="amount"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    internal static Color Mix(Color from, Color to, float amount) => RosterInk.Blend(from, to, amount);

    /// <summary>The rail's ground: a shade between the page and a card.</summary>
    internal static Color RailBack => Mix(AppTheme.Background, AppTheme.Surface, 0.45f);

    /// <summary>The soft fill of something picked.</summary>
    internal static Color PickedFill(Color behind) => Mix(behind, AppTheme.Primary, 0.16f);

    /// <summary>Its edge.</summary>
    internal static Color PickedEdge(Color behind) => Mix(behind, AppTheme.Primary, 0.55f);

    /// <summary>A hairline between regions.</summary>
    internal static Color Hairline => AppTheme.Border;

    /// <summary>Faint text - key letters, captions under captions.</summary>
    internal static Color Faint => Mix(AppTheme.TextMuted, AppTheme.Background, 0.35f);

    internal static GraphicsPath Rounded(RectangleF box, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1f, Math.Min(radius * 2, Math.Min(box.Width, box.Height)));
        path.AddArc(box.Left, box.Top, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>One key drawn as a small rounded box with its letter, its left edge at <paramref name="x"/>. Returns its width.</summary>
    internal static int DrawKey(Graphics g, string key, Font font, int x, int middleY, Color ink)
    {
        var text = TextRenderer.MeasureText(g, key, font, Size.Empty, TextFormatFlags.NoPadding);
        var pad = UiLayout.AtDpi(4);
        var box = new Rectangle(x, middleY - text.Height / 2 - UiLayout.AtDpi(1), text.Width + pad * 2, text.Height + UiLayout.AtDpi(2));

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Rounded(box, UiLayout.AtDpi(3)))
        using (var pen = new Pen(Color.FromArgb(110, ink)))
        {
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, key, font, box, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return box.Width;
    }

    /// <summary>The width <see cref="DrawKey"/> takes.</summary>
    internal static int KeyWidth(string key, Font font) =>
        TextRenderer.MeasureText(key, font, Size.Empty, TextFormatFlags.NoPadding).Width + UiLayout.AtDpi(8);
}

/// <summary>What colour a pill is.</summary>
internal enum PillTone
{
    Plain,
    Good,
    Warning,
    Danger,
    Accent,
}

/// <summary>
/// A short state on a soft rounded pill, with a dot: "On", "Race-blocking ·
/// waited 12m". Sized to its words.
/// </summary>
internal sealed class InboxPill : Control
{
    private PillTone _tone;
    private bool _dot = true;

    public InboxPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Tag = AppTheme.KeepBackColor;
        Font = AppTheme.BoldFont;
        Margin = new Padding(0, UiLayout.AtDpi(2), UiLayout.AtDpi(8), UiLayout.AtDpi(2));
        TabStop = false;
    }

    public PillTone Tone
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

    public bool Dot
    {
        get => _dot;
        set
        {
            if (_dot != value)
            {
                _dot = value;
                SizeToWords();
            }
        }
    }

    /// <summary>The ink the tone draws in.</summary>
    internal Color Ink => _tone switch
    {
        PillTone.Good => AppTheme.Success,
        PillTone.Warning => AppTheme.Warning,
        PillTone.Danger => AppTheme.Danger,
        PillTone.Accent => AppTheme.Primary,
        _ => AppTheme.TextMuted,
    };

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

    private int DotRoom => _dot ? UiLayout.AtDpi(13) : 0;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(text.Width + DotRoom + UiLayout.AtDpi(20), text.Height + UiLayout.AtDpi(6));
    }

    private void SizeToWords()
    {
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var behind = Parent?.BackColor ?? AppTheme.Background;

        using (var back = new SolidBrush(behind))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var ink = Ink;
        var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        using (var path = InboxInk.Rounded(box, box.Height / 2))
        using (var fill = new SolidBrush(InboxInk.Mix(behind, ink, _tone == PillTone.Plain ? 0.14f : 0.2f)))
        {
            g.FillPath(fill, path);
        }

        var x = UiLayout.AtDpi(10);

        if (_dot)
        {
            var d = UiLayout.AtDpi(7);
            using var dot = new SolidBrush(ink);
            g.FillEllipse(dot, x, (Height - d) / 2f, d, d);
            x += DotRoom;
        }

        var text = _tone == PillTone.Plain ? AppTheme.TextMuted : InboxInk.Mix(ink, Color.White, 0.25f);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x, Height), text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
}

/// <summary>What a rail entry's count means, which is its colour.</summary>
internal enum RailTone
{
    Normal,
    Blocking,
    Live,
}

/// <summary>One entry in the inbox's rail.</summary>
internal sealed class RailEntry
{
    public RailEntry(object id, string text, string? key)
    {
        Id = id;
        Text = text;
        Key = key;
    }

    public object Id { get; }
    public string Text { get; set; }
    public string? Key { get; }
    public string Count { get; set; } = string.Empty;
    public RailTone Tone { get; set; }

    /// <summary>The words on its chip when docked, where room is short ("Queries" for "Result queries").</summary>
    public string? Short { get; init; }

    /// <summary>Only in the list down the left: docked, it is reached another way (the vote from the title bar).</summary>
    public bool ListOnly { get; init; }

    internal string ChipText => Short ?? Text;

    /// <summary>A small caption drawn above this entry in the rail ("Waiting", "Tonight").</summary>
    public string? CaptionAbove { get; init; }
}

/// <summary>
/// The inbox's filters, down the left as a list (wide) or as a row of chips
/// that wraps (docked): the name, its count, and the key that picks it.
/// One is picked. A click raises <see cref="Picked"/>.
/// </summary>
internal sealed class InboxRail : Control
{
    private readonly List<RailEntry> _entries = new();
    private readonly List<(RailEntry Entry, Rectangle Box)> _boxes = new();
    private readonly List<(string Text, Rectangle Box)> _captions = new();
    private object? _picked;
    private RailEntry? _hot;
    private bool _across;
    private Font? _keyFont;

    public InboxRail()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Tag = AppTheme.KeepBackColor;
        TabStop = false;
        AccessibleRole = AccessibleRole.PageTabList;
        AccessibleName = "Inbox filters";
    }

    internal event Action<object>? Picked;

    internal IReadOnlyList<RailEntry> Entries => _entries;

    internal RailEntry Add(RailEntry entry)
    {
        _entries.Add(entry);
        Arrange();
        return entry;
    }

    /// <summary>The entry shown as picked.</summary>
    internal object? PickedId
    {
        get => _picked;
        set
        {
            if (!Equals(_picked, value))
            {
                _picked = value;
                Invalidate();
            }
        }
    }

    /// <summary>Chips across (docked) rather than a list down.</summary>
    internal bool Across
    {
        get => _across;
        set
        {
            if (_across != value)
            {
                _across = value;
                Arrange();
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Folded to its keys and counts, for a window too narrow for the names
    /// beside the list and the picked item - the name shows on hover.
    /// </summary>
    internal bool Folded
    {
        get => _folded;
        set
        {
            if (_folded != value)
            {
                _folded = value;
                Arrange();
                Invalidate();
            }
        }
    }

    private bool _folded;
    private readonly ToolTip _tip = new() { InitialDelay = 250 };

    /// <summary>The width of the folded rail.</summary>
    internal static int FoldedWidth => UiLayout.AtTextSize(64);

    /// <summary>The entries' words or counts have changed.</summary>
    internal void Changed()
    {
        Arrange();
        Invalidate();
    }

    private Font KeyFont => _keyFont ??= FontLibrary.Create(AppTheme.MonoFamily, 8F * AppTheme.FontScale, FontStyle.Regular);

    private int RowHeight => AppTheme.NavFont.Height + UiLayout.AtDpi(14);

    private int ChipHeight => AppTheme.NavFont.Height + UiLayout.AtDpi(10);

    private int Inset => UiLayout.AtDpi(8);

    /// <summary>How tall the rail needs to be at <paramref name="width"/>.</summary>
    internal int HeightAt(int width)
    {
        var (_, _, height) = Measure(width);
        return height;
    }

    private (List<(RailEntry, Rectangle)> Boxes, List<(string, Rectangle)> Captions, int Height) Measure(int width)
    {
        var boxes = new List<(RailEntry, Rectangle)>();
        var captions = new List<(string, Rectangle)>();
        var inset = Inset;

        if (_across)
        {
            int x = inset, y = inset, h = ChipHeight, gap = UiLayout.AtDpi(6);

            foreach (var entry in _entries.Where(e => !e.ListOnly))
            {
                var w = ChipWidth(entry);

                if (x > inset && x + w > width - inset)
                {
                    x = inset;
                    y += h + gap;
                }

                boxes.Add((entry, new Rectangle(x, y, w, h)));
                x += w + gap;
            }

            return (boxes, captions, _entries.Count == 0 ? 0 : y + h + inset);
        }

        var top = UiLayout.AtDpi(10);
        var capH = AppTheme.MicroFont.Height + UiLayout.AtDpi(8);

        foreach (var entry in _entries)
        {
            if (entry.CaptionAbove is { } caption)
            {
                if (boxes.Count > 0)
                {
                    top += UiLayout.AtDpi(10);
                }

                captions.Add((caption, new Rectangle(inset + UiLayout.AtDpi(10), top, width - inset * 2, capH)));
                top += capH;
            }

            boxes.Add((entry, new Rectangle(inset, top, width - inset * 2, RowHeight)));
            top += RowHeight + UiLayout.AtDpi(2);
        }

        return (boxes, captions, top + UiLayout.AtDpi(10));
    }

    private int ChipWidth(RailEntry entry)
    {
        var words = TextRenderer.MeasureText(entry.ChipText, AppTheme.NavFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        var count = entry.Count.Length == 0 ? 0 : TextRenderer.MeasureText(entry.Count, KeyFont, Size.Empty, TextFormatFlags.NoPadding).Width + UiLayout.AtDpi(7);
        return words + count + UiLayout.AtDpi(22);
    }

    /// <summary>The narrowest the rail can be as a list - its longest entry with its key and count.</summary>
    internal int LeastWidth()
    {
        var widest = 0;

        foreach (var entry in _entries)
        {
            var words = TextRenderer.MeasureText(entry.Text, AppTheme.NavFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            widest = Math.Max(widest, words + UiLayout.AtDpi(26) + UiLayout.AtDpi(34) + Inset * 2 + UiLayout.AtDpi(20));
        }

        return widest;
    }

    private void Arrange()
    {
        var (boxes, captions, _) = Measure(Math.Max(1, Width));
        _boxes.Clear();
        _boxes.AddRange(boxes);
        _captions.Clear();
        _captions.AddRange(captions);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Arrange();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = _boxes.FirstOrDefault(b => b.Box.Contains(e.Location)).Entry;

        if (!ReferenceEquals(hot, _hot))
        {
            _hot = hot;
            Cursor = hot is null ? Cursors.Default : Cursors.Hand;
            Invalidate();

            // Folded, the name is only a hover away.
            _tip.SetToolTip(this, _folded && hot is not null ? (hot.Count.Length > 0 && hot.Count != "●" ? $"{hot.Text} ({hot.Count})" : hot.Text) : null);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hot is not null)
        {
            _hot = null;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (e.Button == MouseButtons.Left && _boxes.FirstOrDefault(b => b.Box.Contains(e.Location)).Entry is { } entry)
        {
            Picked?.Invoke(entry.Id);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var back = _across ? AppTheme.Background : InboxInk.RailBack;

        using (var fill = new SolidBrush(back))
        {
            g.FillRectangle(fill, ClientRectangle);
        }

        foreach (var (text, box) in _captions)
        {
            if (_folded)
            {
                // No room for the words: a short rule where they would be.
                using var rule = new Pen(InboxInk.Hairline);
                var y = box.Y + box.Height / 2;
                g.DrawLine(rule, Inset + UiLayout.AtDpi(6), y, Width - Inset - UiLayout.AtDpi(6), y);
                continue;
            }

            TextRenderer.DrawText(g, Spaced(text), AppTheme.MicroFont, box, AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        foreach (var (entry, box) in _boxes)
        {
            var picked = Equals(entry.Id, _picked);
            var hot = ReferenceEquals(entry, _hot);
            var rect = new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1f, box.Height - 1f);
            var radius = _across ? rect.Height / 2 : UiLayout.AtDpi(7);

            if (picked && _across)
            {
                using var path = InboxInk.Rounded(rect, radius);
                using var fill = new SolidBrush(AppTheme.Primary);
                g.FillPath(fill, path);
            }
            else if (picked || hot || _across)
            {
                using var path = InboxInk.Rounded(rect, radius);
                using var fill = new SolidBrush(picked ? InboxInk.PickedFill(back) : _across ? AppTheme.SecondaryButton : InboxInk.Mix(back, AppTheme.TextMuted, 0.08f));
                g.FillPath(fill, path);

                if (picked)
                {
                    using var pen = new Pen(InboxInk.PickedEdge(back));
                    g.DrawPath(pen, path);
                }
            }

            var words = picked && _across ? AppTheme.OnPrimary : picked || hot ? AppTheme.TextDark : AppTheme.TextMuted;
            var countInk = entry.Tone switch
            {
                RailTone.Blocking when !(picked && _across) => AppTheme.Danger,
                RailTone.Live when !(picked && _across) => AppTheme.Success,
                _ => words,
            };

            if (_across)
            {
                var x = box.X + UiLayout.AtDpi(11);
                var w = TextRenderer.MeasureText(g, entry.ChipText, AppTheme.NavFont, Size.Empty, TextFormatFlags.NoPadding).Width;
                TextRenderer.DrawText(g, entry.ChipText, AppTheme.NavFont, new Rectangle(x, box.Y, w + 2, box.Height), words,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

                if (entry.Count.Length > 0)
                {
                    TextRenderer.DrawText(g, entry.Count, KeyFont, new Rectangle(x + w + UiLayout.AtDpi(7), box.Y, box.Right - x - w, box.Height), countInk,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }

                continue;
            }

            // Folded: the key, large, and its count beside it.
            if (_folded)
            {
                var keyText = entry.Key ?? entry.Text[..1];
                var half = entry.Count.Length > 0 ? box.Width * 3 / 5 : box.Width;
                TextRenderer.DrawText(g, keyText, AppTheme.NavFont, new Rectangle(box.X, box.Y, half, box.Height), words,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

                if (entry.Count.Length > 0)
                {
                    TextRenderer.DrawText(g, entry.Count, KeyFont, new Rectangle(box.X + half - UiLayout.AtDpi(4), box.Y, box.Width - half + UiLayout.AtDpi(4), box.Height), countInk,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }

                continue;
            }

            var left = box.X + UiLayout.AtDpi(10);

            if (entry.Key is { } key)
            {
                TextRenderer.DrawText(g, key, KeyFont, new Rectangle(left, box.Y, UiLayout.AtDpi(18), box.Height), InboxInk.Faint,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            left += UiLayout.AtDpi(22);
            var countWidth = entry.Count.Length == 0 ? 0 : TextRenderer.MeasureText(g, entry.Count, KeyFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            var right = box.Right - UiLayout.AtDpi(10);

            TextRenderer.DrawText(g, entry.Text, AppTheme.NavFont, new Rectangle(left, box.Y, Math.Max(1, right - countWidth - left - UiLayout.AtDpi(6)), box.Height), words,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            if (entry.Count.Length > 0)
            {
                TextRenderer.DrawText(g, entry.Count, KeyFont, new Rectangle(right - countWidth, box.Y, countWidth + 1, box.Height), countInk,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        if (!_across)
        {
            using var line = new Pen(InboxInk.Hairline);
            g.SmoothingMode = SmoothingMode.None;
            g.DrawLine(line, Width - 1, 0, Width - 1, Height);
        }
    }

    /// <summary>Small capitals, letter-spaced, as the app's section captions are.</summary>
    internal static string Spaced(string caption) => string.Join(" ", caption.ToUpperInvariant().ToCharArray());

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keyFont?.Dispose();
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A button's key, drawn in a small box at its right-hand end when the
/// button has room for it beside its caption: "Approve [A]".
/// </summary>
internal static class KeyHint
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, string> Keys = new();
    private static Font? _font;

    private static Font Font => _font ??= FontLibrary.Create(AppTheme.MonoFamily, 8F * AppTheme.FontScale, FontStyle.Regular);

    internal static void Attach(Button button, string key)
    {
        if (Keys.TryGetValue(button, out _))
        {
            Keys.AddOrUpdate(button, key);
            return;
        }

        Keys.AddOrUpdate(button, key);
        button.Paint += (_, e) =>
        {
            if (!Keys.TryGetValue(button, out var k) || k.Length == 0)
            {
                return;
            }

            var caption = TextRenderer.MeasureText(e.Graphics, button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            var keyWidth = InboxInk.KeyWidth(k, Font);
            var spare = (button.Width - caption) / 2;

            if (spare < keyWidth + UiLayout.AtDpi(14))
            {
                return;
            }

            var ink = button.Enabled ? button.ForeColor : InboxInk.Mix(button.ForeColor, button.BackColor, 0.5f);
            InboxInk.DrawKey(e.Graphics, k, Font, button.Width - keyWidth - UiLayout.AtDpi(10), button.Height / 2, ink);
        };
    }

    /// <summary>How much wider a button wants to be to show its key.</summary>
    internal static int Room(string key) => (InboxInk.KeyWidth(key, Font) + UiLayout.AtDpi(14)) * 2;
}

/// <summary>
/// The answer buttons: in a card (wide), the main answer the full width and
/// the rest two to a row; in a bar (docked), all on one row, wrapping.
/// Hidden buttons take no room; disabled ones keep theirs, so nothing jumps.
/// </summary>
internal sealed class AnswerGrid : Panel
{
    private readonly List<Button> _buttons = new();
    private bool _bar;

    public AnswerGrid()
    {
        Tag = AppTheme.KeepBackColor;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    internal AnswerGrid With(params Button[] buttons)
    {
        foreach (var button in buttons)
        {
            _buttons.Add(button);
            Controls.Add(button);
            button.VisibleChanged += (_, _) => Parent?.PerformLayout();
            button.EnabledChanged += (_, _) =>
            {
                if (_bar)
                {
                    PerformLayout();
                    Parent?.PerformLayout();
                }
            };
        }

        return this;
    }

    /// <summary>The buttons in a new order - the first is the full-width one.</summary>
    internal void Order(IEnumerable<Button> buttons)
    {
        var order = buttons.Where(_buttons.Contains).ToList();
        order.AddRange(_buttons.Where(b => !order.Contains(b)));
        _buttons.Clear();
        _buttons.AddRange(order);
        PerformLayout();
    }

    /// <summary>All on one row rather than two to a row.</summary>
    internal bool Bar
    {
        get => _bar;
        set
        {
            if (_bar != value)
            {
                _bar = value;
                PerformLayout();
            }
        }
    }

    private int Gap => UiLayout.AtDpi(8);

    private static int ButtonHeight(Button b) => Math.Max(b.Height, b.GetPreferredSize(Size.Empty).Height);

    // As FillPage asks: hidden in its own right leaves a button out, which
    // can only be asked while this is on screen - before, every child
    // reports itself hidden.
    private IEnumerable<Button> Shown => _buttons.Where(b => !Visible || b.Visible);

    private List<(Button Button, Rectangle Box)> Arrange(int width)
    {
        var placed = new List<(Button, Rectangle)>();
        var shown = Shown.ToList();
        int x = 0, y = 0, rowHeight = 0;

        if (_bar)
        {
            // A bar keeps to one row where it can: answers that mean nothing
            // for what is picked step out of it (their keys say so anyway),
            // all but the main one, which holds the bar's place.
            foreach (var b in shown.Where((b, i) => i == 0 || b.Enabled))
            {
                var w = UiLayout.TightButtonSizeFor(b).Width;
                var h = ButtonHeight(b);

                if (x > 0 && x + w > width)
                {
                    x = 0;
                    y += rowHeight + Gap;
                    rowHeight = 0;
                }

                placed.Add((b, new Rectangle(x, y, w, h)));
                x += w + Gap;
                rowHeight = Math.Max(rowHeight, h);
            }

            return placed;
        }

        var half = Math.Max(1, (width - Gap) / 2);

        for (var i = 0; i < shown.Count; i++)
        {
            var b = shown[i];
            var h = ButtonHeight(b);

            if (i == 0)
            {
                placed.Add((b, new Rectangle(0, 0, width, h)));
                y = h + Gap;
                continue;
            }

            var column = (i - 1) % 2;
            placed.Add((b, new Rectangle(column == 0 ? 0 : half + Gap, y, column == 0 && i == shown.Count - 1 ? width : half, h)));

            if (column == 1)
            {
                y += h + Gap;
            }
        }

        return placed;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : UiLayout.AtTextSize(300);
        var placed = Arrange(width);
        var bottom = placed.Count == 0 ? 0 : placed.Max(p => p.Box.Bottom);
        return new Size(width, bottom);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        var placed = Arrange(Width);

        foreach (var (button, box) in placed)
        {
            if (button.Bounds != box)
            {
                button.Bounds = box;
            }
        }

        // Left out of a bar: no room at all, so nothing of it shows.
        foreach (var button in _buttons.Where(b => placed.All(p => p.Button != b)))
        {
            if (button.Bounds != Rectangle.Empty)
            {
                button.Bounds = Rectangle.Empty;
            }
        }
    }
}

/// <summary>
/// The quick replies a decline offers, as small dashed chips: a click
/// declines with those words, as picking them from Decline's menu does.
/// </summary>
internal sealed class ReplyChips : Control
{
    private readonly List<(string Text, Rectangle Box)> _boxes = new();
    private IReadOnlyList<string> _replies = Array.Empty<string>();
    private int _hot = -1;

    public ReplyChips()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Tag = AppTheme.KeepBackColor;
        Font = AppTheme.SmallFont;
        TabStop = false;
        AutoSize = true;
    }

    internal event Action<string>? Picked;

    internal IReadOnlyList<string> Replies
    {
        get => _replies;
        set
        {
            if (!_replies.SequenceEqual(value))
            {
                _replies = value.ToList();
                Arrange(Width);
                Invalidate();
                Parent?.PerformLayout();
            }
        }
    }

    private List<(string, Rectangle)> Boxes(int width)
    {
        var list = new List<(string, Rectangle)>();
        int x = 0, y = 0, gap = UiLayout.AtDpi(6);
        var h = Font.Height + UiLayout.AtDpi(8);

        foreach (var reply in _replies)
        {
            var w = Math.Min(width, TextRenderer.MeasureText(reply, Font, Size.Empty, TextFormatFlags.NoPadding).Width + UiLayout.AtDpi(16));

            if (x > 0 && x + w > width)
            {
                x = 0;
                y += h + gap;
            }

            list.Add((reply, new Rectangle(x, y, w, h)));
            x += w + gap;
        }

        return list;
    }

    private void Arrange(int width)
    {
        _boxes.Clear();
        _boxes.AddRange(Boxes(Math.Max(1, width)));
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : UiLayout.AtTextSize(300);
        var boxes = Boxes(width);
        return new Size(width, boxes.Count == 0 ? 0 : boxes.Max(b => b.Item2.Bottom) + 1);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Arrange(Width);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = _boxes.FindIndex(b => b.Box.Contains(e.Location));

        if (hot != _hot)
        {
            _hot = hot;
            Cursor = hot < 0 ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var at = _boxes.FindIndex(b => b.Box.Contains(e.Location));

        if (at >= 0 && Enabled)
        {
            Picked?.Invoke(_boxes[at].Text);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var behind = Parent?.BackColor ?? AppTheme.Surface;

        using (var back = new SolidBrush(behind))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        for (var i = 0; i < _boxes.Count; i++)
        {
            var (text, box) = _boxes[i];
            var rect = new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1f, box.Height - 1f);
            using var path = InboxInk.Rounded(rect, UiLayout.AtDpi(5));

            if (i == _hot)
            {
                using var fill = new SolidBrush(InboxInk.Mix(behind, AppTheme.Primary, 0.14f));
                g.FillPath(fill, path);
            }

            using (var pen = new Pen(i == _hot ? InboxInk.PickedEdge(behind) : AppTheme.Border) { DashStyle = DashStyle.Dash })
            {
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, text, Font, box, i == _hot ? AppTheme.TextDark : AppTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>
/// The keys the inbox answers to, along its foot, each in a small box:
/// "[↑][↓] move · [Enter] main answer · [1]-[7] filters".
/// </summary>
internal sealed class KeyStrip : Control
{
    private readonly (string[] Keys, string Words)[] _parts;
    private Font? _keyFont;

    public KeyStrip(params (string[] Keys, string Words)[] parts)
    {
        _parts = parts;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Tag = AppTheme.KeepBackColor;
        Font = AppTheme.SmallFont;
        TabStop = false;
        AccessibleName = string.Join(", ", parts.Select(p => $"{string.Join(" ", p.Keys)} {p.Words}"));
        Margin = Padding.Empty;
        AutoSize = true;
    }

    private Font KeyFont => _keyFont ??= FontLibrary.Create(AppTheme.MonoFamily, 8F * AppTheme.FontScale, FontStyle.Regular);

    private int Gap => UiLayout.AtDpi(16);

    private int PartWidth((string[] Keys, string Words) part) =>
        part.Keys.Sum(k => InboxInk.KeyWidth(k, KeyFont) + UiLayout.AtDpi(3))
        + UiLayout.AtDpi(3)
        + TextRenderer.MeasureText(part.Words, Font, Size.Empty, TextFormatFlags.NoPadding).Width;

    private int LineHeight => Math.Max(Font.Height, KeyFont.Height) + UiLayout.AtDpi(6);

    private List<(int Part, Point At)> Arrange(int width)
    {
        var list = new List<(int, Point)>();
        int x = 0, y = 0;

        for (var i = 0; i < _parts.Length; i++)
        {
            var w = PartWidth(_parts[i]);

            if (x > 0 && x + w > width)
            {
                x = 0;
                y += LineHeight;
            }

            list.Add((i, new Point(x, y)));
            x += w + Gap;
        }

        return list;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4
            ? proposedSize.Width
            : _parts.Sum(PartWidth) + Gap * Math.Max(0, _parts.Length - 1);
        var arranged = Arrange(width);
        return new Size(width, (arranged.Count == 0 ? 0 : arranged.Max(a => a.At.Y)) + LineHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        using (var back = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        foreach (var (index, at) in Arrange(Width))
        {
            var part = _parts[index];
            var x = at.X;
            var middle = at.Y + LineHeight / 2;

            foreach (var key in part.Keys)
            {
                x += InboxInk.DrawKey(g, key, KeyFont, x, middle, AppTheme.TextMuted) + UiLayout.AtDpi(3);
            }

            x += UiLayout.AtDpi(3);
            TextRenderer.DrawText(g, part.Words, Font, new Rectangle(x, at.Y, Width - x, LineHeight), AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keyFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A card that opens under a button and closes on Esc or a click away - the
/// inbox's room vote and rules. The card keeps its controls between
/// openings, so what is in it (the tally) stays up to date while it is shut.
/// </summary>
internal sealed class InboxPopover : IDisposable
{
    private readonly ToolStripDropDown _drop;
    private readonly ToolStripControlHost _host;
    private readonly Panel _holder;
    private readonly Section _card;
    private readonly int _width;

    public InboxPopover(Section card, int width)
    {
        _card = card;
        _width = width;

        _holder = new Panel { Tag = AppTheme.KeepBackColor, BackColor = AppTheme.Surface, Margin = Padding.Empty, Padding = Padding.Empty };
        _holder.Controls.Add(card);
        card.Location = Point.Empty;

        // The card is the pop-over's width, not its own: a card that sizes
        // itself grew back to one line of its longest words.
        card.AutoSize = false;
        card.Dock = DockStyle.None;

        _host = new ToolStripControlHost(_holder)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        _drop = new ToolStripDropDown
        {
            AutoClose = true,
            DropShadowEnabled = true,
            Padding = new Padding(1),
            BackColor = AppTheme.Border,
        };
        _drop.Items.Add(_host);
        _drop.Closed += (_, _) => Closed?.Invoke();

        Fit();
    }

    internal event Action? Closed;

    internal bool IsOpen => _drop.Visible;

    /// <summary>The card sized to what is in it now - call when it has changed.</summary>
    internal void Fit()
    {
        // Its words wrap to the pop-over's width.
        var inside = _width - _card.Padding.Horizontal - UiLayout.AtDpi(8);

        foreach (var label in Labels(_card))
        {
            var most = new Size(Math.Max(1, inside - label.Margin.Horizontal), 0);

            if (label.MaximumSize != most)
            {
                label.MaximumSize = most;
            }
        }

        // A little more than the card measures, for its own edge at the foot.
        var height = _card.GetPreferredSize(new Size(_width, 0)).Height + UiLayout.AtDpi(6);
        var size = new Size(_width, height);

        if (_card.Size != size)
        {
            _card.Size = size;
        }

        if (_holder.Size != size)
        {
            _holder.Size = size;
            _host.Size = size;
        }
    }

    /// <summary>Opens it under <paramref name="anchor"/>, its right-hand edge lined up with the anchor's.</summary>
    internal void Show(Control anchor)
    {
        Fit();
        _drop.Show(anchor, new Point(anchor.Width - _width - 2, anchor.Height + UiLayout.AtDpi(6)));
    }

    internal void Hide() => _drop.Close();

    private static IEnumerable<Label> Labels(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Label { AutoSize: true } label)
            {
                yield return label;
            }

            foreach (var inner in Labels(child))
            {
                yield return inner;
            }
        }
    }

    public void Dispose()
    {
        _drop.Dispose();
    }
}

/// <summary>Where a band's hairline goes.</summary>
internal enum InboxBandLine
{
    Below,
    Above,
}

/// <summary>
/// A full-width strip along the top or foot of the inbox with a hairline
/// between it and the middle - the state bar, the key strip - holding one
/// part with the page's side margins inside it.
/// </summary>
internal sealed class InboxBand : Panel
{
    private readonly Control _part;
    private readonly InboxBandLine _line;
    private bool _shade;

    public InboxBand(Control part, InboxBandLine line)
    {
        LayoutFreeze.Adopt(this);
        _part = part;
        _line = line;
        Tag = AppTheme.KeepBackColor;
        BackColor = AppTheme.Background;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = new Padding(UiLayout.PageLeft, UiLayout.AtDpi(8), UiLayout.PageRight, UiLayout.AtDpi(8));
        SetStyle(ControlStyles.ResizeRedraw, true);
        Controls.Add(part);
    }

    /// <summary>A shade darker than the page, as the key strip is.</summary>
    internal bool Shade
    {
        get => _shade;
        set
        {
            _shade = value;
            BackColor = value ? InboxInk.Mix(AppTheme.Background, Color.Black, 0.25f) : AppTheme.Background;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : _part.GetPreferredSize(Size.Empty).Width + Padding.Horizontal;
        var inside = _part.Visible || !Visible ? _part.GetPreferredSize(new Size(Math.Max(1, width - Padding.Horizontal), 0)).Height : 0;
        return new Size(width, inside + Padding.Vertical + 1);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var width = Math.Max(1, Width - Padding.Horizontal);
        var height = _part.GetPreferredSize(new Size(width, 0)).Height;
        var top = Padding.Top + (_line == InboxBandLine.Above ? 1 : 0);
        _part.Bounds = new Rectangle(Padding.Left, top, width, height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var line = new Pen(InboxInk.Hairline);
        var y = _line == InboxBandLine.Below ? Height - 1 : 0;
        e.Graphics.DrawLine(line, 0, y, Width, y);
    }
}

/// <summary>
/// The inbox's middle: the rail, the list and the picked item - three
/// columns with room, stacked when docked or narrow (rail as a row of chips
/// on top, the picked item as a bar along the foot).
/// </summary>
internal sealed class InboxBody : Panel
{
    private readonly InboxRail _rail;
    private readonly FillPage _waiting;
    private readonly FillPage _done;
    private readonly FillPage _detail;
    private bool _wide = true;
    private bool _showingDone;

    public InboxBody(InboxRail rail, FillPage waiting, FillPage done, FillPage detail)
    {
        LayoutFreeze.Adopt(this);
        Tag = AppTheme.KeepBackColor;
        Margin = Padding.Empty;
        Padding = Padding.Empty;

        _rail = rail;
        _waiting = waiting;
        _done = done;
        _detail = detail;

        _waiting.Dock = DockStyle.None;
        _done.Dock = DockStyle.None;
        _detail.Dock = DockStyle.None;
        _done.Visible = false;

        Controls.Add(_rail);
        Controls.Add(_waiting);
        Controls.Add(_done);
        Controls.Add(_detail);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    /// <summary>The Done list in the middle in place of what is waiting.</summary>
    internal bool ShowingDone
    {
        get => _showingDone;
        set
        {
            if (_showingDone != value)
            {
                _showingDone = value;
                ShowWaiting(!value);
                _done.Visible = value;
                PerformLayout();
            }
        }
    }

    private FillPage List => _showingDone ? _done : _waiting;

    /// <summary>Docked: the picked item opened out over the list, which steps aside for it.</summary>
    internal bool SheetOpen
    {
        get => _sheetOpen;
        set
        {
            if (_sheetOpen != value)
            {
                _sheetOpen = value;
                PerformLayout();
                Invalidate();
            }
        }
    }

    private bool _sheetOpen;

    /// <summary>
    /// The waiting list shown or not, by what was asked rather than by
    /// Visible - which reads false for every child while this is off screen,
    /// so comparing with it set it again on every layout.
    /// </summary>
    private void ShowWaiting(bool show)
    {
        if (_waitingShown != show)
        {
            _waitingShown = show;
            _waiting.Visible = show;
        }
    }

    private bool _waitingShown = true;

    /// <summary>The layout changed between wide and narrow.</summary>
    internal event Action<bool>? WideChanged;

    /// <summary>Three columns, or stacked.</summary>
    internal bool Wide => _wide;

    /// <summary>The width from which the three columns are laid side by side.</summary>
    internal static int WideFrom => UiLayout.AtTextSize(760);

    /// <summary>The width from which the rail shows its names; between this and WideFrom it folds to keys and counts.</summary>
    internal static int NamesFrom => UiLayout.AtTextSize(1100);

    internal int RailWidth => RailWidthAt(Width);

    private int RailWidthAt(int width) =>
        width < NamesFrom ? InboxRail.FoldedWidth : Math.Max(UiLayout.AtTextSize(196), _rail.LeastWidth());

    internal static int DetailWidth => UiLayout.AtTextSize(340);

    /// <summary>The least height the middle can do with at <paramref name="width"/>.</summary>
    internal int LeastHeight(int width)
    {
        // The Done list has no item to answer, so it takes the picked item's room too.
        var detail = _showingDone ? 0 : 1;

        if (width >= WideFrom)
        {
            // Not the picked item's: beside the list it scrolls itself when
            // short (it is a FillPage), rather than making the whole window
            // scroll on a laptop screen.
            var railWidth = RailWidthAt(width);
            var listWidth = Math.Max(1, width - railWidth - DetailWidth * detail);
            return Math.Max(_rail.HeightAt(railWidth), List.LeastHeight(listWidth));
        }

        // Docked Details: the list steps aside, so only the picked item counts.
        if (_sheetOpen && !_showingDone)
        {
            return _rail.HeightAt(width) + _detail.LeastHeight(width);
        }

        return _rail.HeightAt(width) + List.LeastHeight(width) + detail * _detail.LeastHeight(width);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        var wide = Width >= WideFrom;
        _rail.Folded = wide && Width < NamesFrom;

        if (wide != _wide)
        {
            _wide = wide;
            _rail.Across = !wide;
            WideChanged?.Invoke(wide);
        }

        _rail.Across = !wide;

        if (_detail.Visible == _showingDone)
        {
            _detail.Visible = !_showingDone;
        }

        if (wide)
        {
            ShowWaiting(!_showingDone);

            var railWidth = RailWidth;
            var detailWidth = _showingDone ? 0 : DetailWidth;
            Place(_rail, new Rectangle(0, 0, railWidth, Height));
            Place(_detail, new Rectangle(Width - DetailWidth, 0, DetailWidth, Height));
            var middle = new Rectangle(railWidth, 0, Math.Max(1, Width - railWidth - detailWidth - (_showingDone ? 0 : 1)), Height);
            Place(_waiting, middle);
            Place(_done, middle);
        }
        else
        {
            var railHeight = _rail.HeightAt(Width);
            var sheet = _sheetOpen && !_showingDone;
            var detailHeight = _showingDone ? 0 : sheet ? Math.Max(1, Height - railHeight) : _detail.LeastHeight(Width);

            ShowWaiting(!sheet && !_showingDone);
            Place(_rail, new Rectangle(0, 0, Width, railHeight));
            Place(_detail, new Rectangle(0, Math.Max(railHeight, Height - detailHeight), Width, Math.Max(1, detailHeight)));
            var middle = new Rectangle(0, railHeight, Width, Math.Max(1, Height - railHeight - detailHeight));
            Place(_waiting, middle);
            Place(_done, middle);
        }
    }

    private static void Place(Control control, Rectangle box)
    {
        if (control.Bounds != box)
        {
            control.Bounds = box;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var line = new Pen(InboxInk.Hairline);

        if (_wide)
        {
            if (!_showingDone)
            {
                e.Graphics.DrawLine(line, _detail.Left - 1, 0, _detail.Left - 1, Height);
            }
        }
        else
        {
            if (!_showingDone)
            {
                e.Graphics.DrawLine(line, 0, _detail.Top - 1, Width, _detail.Top - 1);
            }

            e.Graphics.DrawLine(line, 0, _rail.Bottom, Width, _rail.Bottom);
        }
    }
}

/// <summary>
/// What the driver's phone will say, in a small phone-shaped card: the
/// main answer's headline, and under it what a decline would say instead.
/// </summary>
internal sealed class PhonePreview : Control
{
    private string _headline = string.Empty;
    private string _otherwise = string.Empty;

    public PhonePreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Tag = AppTheme.KeepBackColor;
        TabStop = false;
        AutoSize = true;
        AccessibleRole = AccessibleRole.StaticText;
    }

    internal void Show(string headline, string otherwise)
    {
        if (_headline == headline && _otherwise == otherwise)
        {
            return;
        }

        _headline = headline;
        _otherwise = otherwise;
        AccessibleName = $"Their phone will say: {headline}. {otherwise}";
        Invalidate();
        Parent?.PerformLayout();
    }

    private int Pad => UiLayout.AtDpi(12);

    private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Left;

    private (int Caption, int Headline, int Otherwise) Heights(int width)
    {
        var inside = Math.Max(1, width - Pad * 2);
        var caption = AppTheme.MicroFont.Height;
        var headline = TextRenderer.MeasureText(_headline, AppTheme.BoldFont, new Size(inside, 0), Wrap).Height;
        var otherwise = _otherwise.Length == 0 ? 0 : TextRenderer.MeasureText(_otherwise, AppTheme.SmallFont, new Size(inside, 0), Wrap).Height;
        return (caption, headline, otherwise);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : UiLayout.AtTextSize(300);
        var (caption, headline, otherwise) = Heights(width);
        return new Size(width, Pad * 2 + caption + UiLayout.AtDpi(4) + headline + (otherwise > 0 ? UiLayout.AtDpi(4) + otherwise : 0));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var behind = Parent?.BackColor ?? AppTheme.Surface;

        using (var back = new SolidBrush(behind))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        using (var path = InboxInk.Rounded(box, UiLayout.AtDpi(14)))
        {
            using (var fill = new SolidBrush(InboxInk.Mix(AppTheme.Background, Color.Black, 0.2f)))
            {
                g.FillPath(fill, path);
            }

            using var pen = new Pen(AppTheme.Border);
            g.DrawPath(pen, path);
        }

        var (caption, headline, _) = Heights(Width);
        var inside = Math.Max(1, Width - Pad * 2);
        var y = Pad;
        TextRenderer.DrawText(g, InboxRail.Spaced("Their phone will say"), AppTheme.MicroFont, new Rectangle(Pad, y, inside, caption), AppTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        y += caption + UiLayout.AtDpi(4);
        TextRenderer.DrawText(g, _headline, AppTheme.BoldFont, new Rectangle(Pad, y, inside, headline), AppTheme.TextDark, Wrap);
        y += headline + UiLayout.AtDpi(4);

        if (_otherwise.Length > 0)
        {
            TextRenderer.DrawText(g, _otherwise, AppTheme.SmallFont, new Rectangle(Pad, y, inside, Height - y), AppTheme.TextMuted, Wrap);
        }
    }
}

/// <summary>
/// The room vote's tally as bars - each answer, its share as a bar, its
/// count - redrawn as votes come in, so "has the room finished" is a glance.
/// </summary>
internal sealed class TallyBars : Control
{
    private IReadOnlyList<VoteStanding> _standings = Array.Empty<VoteStanding>();

    public TallyBars()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Tag = AppTheme.KeepBackColor;
        TabStop = false;
        AutoSize = true;
        AccessibleRole = AccessibleRole.Chart;
    }

    internal void Show(IReadOnlyList<VoteStanding> standings)
    {
        if (_standings.SequenceEqual(standings))
        {
            return;
        }

        var resized = _standings.Count != standings.Count;
        _standings = standings.ToList();
        AccessibleName = string.Join(", ", _standings.Select(s => $"{s.Name} {s.Votes}"));
        Invalidate();

        if (resized)
        {
            Parent?.PerformLayout();
        }
    }

    private int Row => AppTheme.SmallFont.Height + UiLayout.AtDpi(8);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : UiLayout.AtTextSize(300);
        return new Size(width, _standings.Count * Row);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var behind = Parent?.BackColor ?? AppTheme.Surface;

        using (var back = new SolidBrush(behind))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        var nameWidth = Math.Min(Width / 3, UiLayout.AtTextSize(120));
        var countWidth = UiLayout.AtTextSize(30);
        var trackLeft = nameWidth + UiLayout.AtDpi(10);
        var trackWidth = Math.Max(1, Width - trackLeft - countWidth - UiLayout.AtDpi(10));
        var most = Math.Max(1, _standings.Count == 0 ? 1 : _standings.Max(s => s.Votes));
        var barHeight = UiLayout.AtDpi(8);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        for (var i = 0; i < _standings.Count; i++)
        {
            var s = _standings[i];
            var y = i * Row;
            var lead = s.Votes > 0 && s.Votes == most;
            TextRenderer.DrawText(g, s.Name, AppTheme.SmallFont, new Rectangle(0, y, nameWidth, Row), lead ? AppTheme.TextDark : AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            var middle = y + (Row - barHeight) / 2f;

            using (var track = InboxInk.Rounded(new RectangleF(trackLeft, middle, trackWidth, barHeight), barHeight / 2f))
            using (var trackFill = new SolidBrush(AppTheme.SecondaryButton))
            {
                g.FillPath(trackFill, track);
            }

            if (s.Votes > 0)
            {
                var w = Math.Max(barHeight, trackWidth * (s.Votes / (float)most));
                using var bar = InboxInk.Rounded(new RectangleF(trackLeft, middle, w, barHeight), barHeight / 2f);
                using var barFill = new SolidBrush(lead ? AppTheme.Primary : InboxInk.Mix(behind, AppTheme.Primary, 0.55f));
                g.FillPath(barFill, bar);
            }

            TextRenderer.DrawText(g, s.Votes.ToString(System.Globalization.CultureInfo.CurrentCulture), AppTheme.SmallFont,
                new Rectangle(Width - countWidth, y, countWidth, Row), AppTheme.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }
}
