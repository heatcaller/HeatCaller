using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// The themed scrollbars on a container that scrolls, and the room below
/// its last control.
///
/// Moved here unchanged from the layout repair pass - the runtime pass that
/// used to nudge hand-placed controls apart - when that was deleted (30 Sep):
/// every page and dialog lays itself out now, and this was the part of it
/// the theme pass still needed.
/// </summary>
internal static class ScrollTheming
{
    /// <summary>Space left below the last control on a page that scrolls, once scrolled to the end.</summary>
    public const int ScrollBottomRoom = 32;

    /// <summary>
    /// Themed bars on a scrolling container, both ways (each only shows
    /// when there is something to scroll), and room below the last
    /// control so a page doesn't stop hard against the window's edge.
    /// Safe to call more than once.
    /// </summary>
    internal static void Apply(ScrollableControl scrollable)
    {
        ThinScrollBar.Attach(scrollable);
        ThinScrollBar.AttachHorizontal(scrollable);

        // Every scrolling container, not only the bordered ones: a step
        // that is not painted before the next one starts shows as rows
        // coming into view blank until the scrolling stops.
        ScrollRepaint.Attach(scrollable);

        if (scrollable.AutoScrollMargin.Height < ScrollBottomRoom && !WithoutRoomBelow.TryGetValue(scrollable, out _))
        {
            scrollable.AutoScrollMargin = new Size(scrollable.AutoScrollMargin.Width, ScrollBottomRoom);
        }
    }

    /// <summary>The scrolling panels that keep no room under their last control - see NoRoomBelow.</summary>
    private static readonly ConditionalWeakTable<ScrollableControl, object> WithoutRoomBelow = new();

    /// <summary>
    /// Takes the room under the last control off a panel that scrolls, and
    /// keeps it off through every theme pass after this (Apply).
    ///
    /// For the two halves of Next Steps' split. The room is there so a page
    /// scrolled to its end does not stop hard against the window's edge; in
    /// a half of a split, the bottom edge is the splitter or the window's,
    /// and the room was a blank band 32px tall under each half - paid for
    /// in what the top half could show, since the program below keeps its
    /// least and the top gives way (29 Sep). The page's own margin stays.
    /// </summary>
    internal static void NoRoomBelow(ScrollableControl scrollable)
    {
        if (!WithoutRoomBelow.TryGetValue(scrollable, out _))
        {
            WithoutRoomBelow.Add(scrollable, new object());
        }

        if (scrollable.AutoScrollMargin.Height != 0)
        {
            scrollable.AutoScrollMargin = new Size(scrollable.AutoScrollMargin.Width, 0);
        }
    }
}
