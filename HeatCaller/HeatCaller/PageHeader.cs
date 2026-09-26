using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// The line that anchors the top of every page: the page's own name in
/// small spaced caps, in the accent, over a hairline that runs the full
/// width.
///
/// Pages used to begin in mid-air - the first control simply appeared a
/// dozen pixels down with nothing above it, so nothing on the page itself
/// said which page you were on, and no two of them started the same way.
/// This gives all twenty-three one top edge, drawn from one place.
///
/// PAINTED, not a control. A header control on every page would be one
/// more thing for the layout pass to measure and move, one more z-order
/// question on the pages that dock something of their own at the top, and
/// twenty-three more controls in the tree. Reserving the strip with the
/// page's Padding instead costs nothing and is honoured automatically by
/// the docked child every page already has - which is why no page's own
/// layout code had to change.
///
/// The title comes from the nav registry, so it can never drift from what
/// the sidebar says.
/// </summary>
internal static class PageHeader
{
    /// <summary>
    /// Weak on purpose: a page that is disposed takes its entry with it,
    /// and Attach can be called again on the same page without stacking a
    /// second paint handler on it.
    /// </summary>
    private static readonly ConditionalWeakTable<Panel, string> Titles = new();

    private static readonly ConditionalWeakTable<Panel, object> Hooked = new();

    private static readonly object Marker = new();

    /// <summary>
    /// Follows the font scale rather than sitting at a fixed 30px, so the
    /// strip grows with the text the way every other measured row does.
    /// Thirty at 100%, which is what the mock-up was drawn at.
    /// </summary>
    internal static int Height => Math.Max(30, AppTheme.MicroFont.Height + 14);

    /// <summary>
    /// Gives a page its heading. Safe to call more than once - only the
    /// first call hooks anything up.
    /// </summary>
    public static void Attach(Panel page, string title)
    {
        if (page.IsDisposed)
        {
            return;
        }

        Titles.AddOrUpdate(page, title);

        if (!Hooked.TryGetValue(page, out _))
        {
            Hooked.Add(page, Marker);

            page.Paint += Draw;

            // A Panel does not have ResizeRedraw set, so widening the window
            // only repaints the newly uncovered sliver - and narrowing it
            // repaints nothing at all, which would leave the rule drawn past
            // the new right edge until something else forced a repaint. The
            // strip is thirty pixels of a page that is already being laid
            // out, so invalidating just that band is free.
            page.Resize += (s, _) =>
            {
                if (s is Panel p && !p.IsDisposed)
                {
                    p.Invalidate(new Rectangle(0, 0, p.Width, Height));
                }
            };

            // Drawn at the top of the VIEWPORT, so a page that scrolls would
            // copy the heading and its rule down the page on every step.
            ScrollRepaint.Attach(page);
        }

        Reserve(page);
    }

    /// <summary>
    /// Re-measures the reserved strip. Called again after a font-scale
    /// change, when the caps - and so the height - are a different size.
    /// </summary>
    public static void Reserve(Panel page)
    {
        if (page.IsDisposed)
        {
            return;
        }

        var wanted = Height;
        var padding = page.Padding;

        // Only when it actually changed: assigning Padding lays the page out
        // again, and this runs across every page on a scale change.
        if (padding.Top != wanted)
        {
            page.Padding = new Padding(padding.Left, wanted, padding.Right, padding.Bottom);
        }

        page.Invalidate(new Rectangle(0, 0, Math.Max(1, page.Width), wanted));
    }

    private static void Draw(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel page || !Titles.TryGetValue(page, out var title))
        {
            return;
        }

        var height = Height;

        // The hairline sits on the last row of the reserved strip, so the
        // content below starts immediately under it.
        using (var rule = new Pen(AppTheme.Border))
        {
            e.Graphics.DrawLine(rule, 0, height - 1, page.Width, height - 1);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        // Left edge shared with the page's own content, so the heading reads
        // as the top of the column rather than as separate chrome.
        var bounds = new Rectangle(
            UiLayout.PageLeft,
            0,
            Math.Max(10, page.Width - UiLayout.PageLeft * 2),
            height - 1);

        TextRenderer.DrawText(
            e.Graphics,
            AppTheme.Spaced(title.ToUpperInvariant()),
            AppTheme.MicroFont,
            bounds,
            AppTheme.Primary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
