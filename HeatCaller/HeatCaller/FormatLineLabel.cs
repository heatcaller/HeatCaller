namespace HeatCaller;

/// <summary>
/// The format on the foot of Race Control's board (24 Nov 2026, option B):
/// "12 laps · all away together", on one line - the length in the text
/// colour, semibold, and the rest muted, as the mockup has it - shortened
/// with "…" when the row is short of room. A Label, so it is set by its Text
/// as the line above the row always was, and read by a screen reader; drawn
/// here so it never wraps onto a second line.
/// </summary>
internal sealed class FormatLineLabel : Label
{
    private Font? _bold;
    private Font? _boldOf;

    public FormatLineLabel()
    {
        UseMnemonic = false;
        AutoSize = false;
    }

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

    /// <summary>The first part, and the rest with its " · " - "12 laps" and " · all away together". Pure, for the tests.</summary>
    internal static (string Lead, string Tail) Split(string text)
    {
        var at = text.IndexOf(" · ", StringComparison.Ordinal);
        return at < 0 ? (text, string.Empty) : (text[..at], text[at..]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var (lead, rest) = Split(Text);
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.Left;

        var leadWidth = TextRenderer.MeasureText(g, lead, BoldFont, Size.Empty, flags).Width;
        var box = ClientRectangle;

        if (leadWidth >= box.Width)
        {
            TextRenderer.DrawText(g, lead, BoldFont, box, AppTheme.TextDark, flags | TextFormatFlags.EndEllipsis);
            return;
        }

        TextRenderer.DrawText(g, lead, BoldFont, new Rectangle(box.X, box.Y, leadWidth, box.Height), AppTheme.TextDark, flags);
        TextRenderer.DrawText(g, rest, Font, new Rectangle(box.X + leadWidth, box.Y, box.Width - leadWidth, box.Height), AppTheme.TextMuted, flags | TextFormatFlags.EndEllipsis);
    }

    /// <summary>As wide as the whole line wants - the row gives it that, or what is left.</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var (lead, rest) = Split(Text);
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var a = TextRenderer.MeasureText(lead, BoldFont, Size.Empty, flags);
        var b = TextRenderer.MeasureText(rest, Font, Size.Empty, flags);
        return new Size(a.Width + b.Width + UiLayout.AtDpi(4), Math.Max(a.Height, b.Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bold?.Dispose();
        }

        base.Dispose(disposing);
    }
}
