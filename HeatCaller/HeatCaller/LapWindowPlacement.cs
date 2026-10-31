namespace HeatCaller;

/// <summary>
/// Where Race Control's lap windows go (30 Oct 2026, asked for by Asher:
/// several lap breakdowns open at once, as windows that can be dragged
/// around). Pure arithmetic on rectangles - which screen, which spot, how
/// they line up - so it can be tested without opening a window.
///
/// The screens are passed in as their working areas, with the Big Screen's
/// monitor already left out when it is running full screen there: a lap
/// window opened over the room's display would be on the one screen the
/// director is not looking at, and in front of everyone else.
/// </summary>
internal static class LapWindowPlacement
{
    /// <summary>
    /// How much of a window's top edge has to be on a screen for a saved
    /// spot to count as reachable: enough of the header to grab and drag.
    /// </summary>
    internal const int GrabStrip = 40;

    /// <summary>
    /// The lowest spot number not already taken by an open window. Spots
    /// are what positions are remembered by: the first window open goes
    /// back where the first window was last left, and so on.
    /// </summary>
    internal static int FreeSlot(IEnumerable<int> taken)
    {
        var used = new HashSet<int>(taken);
        var slot = 0;

        while (used.Contains(slot))
        {
            slot++;
        }

        return slot;
    }

    /// <summary>
    /// A remembered spot, when it can still be reached: its top strip on a
    /// screen that is still connected. A spot on a monitor that has since
    /// been unplugged - the laptop taken home from the track - is null, so
    /// the window opens where a new one would instead of off the edge.
    /// </summary>
    internal static Rectangle? Restore(Point savedLocation, Size size, IReadOnlyList<Rectangle> screens)
    {
        var strip = new Rectangle(savedLocation.X, savedLocation.Y, size.Width, Math.Min(GrabStrip, size.Height));

        foreach (var screen in screens)
        {
            var overlap = Rectangle.Intersect(strip, screen);

            if (overlap.Width >= Math.Min(size.Width, GrabStrip * 2) && overlap.Height >= Math.Min(strip.Height, GrabStrip / 2))
            {
                return Fit(new Rectangle(savedLocation, size), screens);
            }
        }

        return null;
    }

    /// <summary>
    /// The wanted bounds moved just enough to sit wholly on the screen they
    /// are mostly on - or on the first screen, when they are on none of
    /// them. Taller than the screen, the top stays on it, since that is
    /// where it is dragged from.
    /// </summary>
    internal static Rectangle Fit(Rectangle wanted, IReadOnlyList<Rectangle> screens)
    {
        if (screens.Count == 0)
        {
            return wanted;
        }

        var screen = ScreenFor(wanted, screens);

        var x = Math.Max(screen.Left, Math.Min(wanted.X, screen.Right - wanted.Width));
        var y = Math.Max(screen.Top, Math.Min(wanted.Y, screen.Bottom - wanted.Height));

        return new Rectangle(x, y, wanted.Width, wanted.Height);
    }

    /// <summary>The screen holding most of the rectangle, or the first one when none does.</summary>
    internal static Rectangle ScreenFor(Rectangle bounds, IReadOnlyList<Rectangle> screens)
    {
        var best = screens[0];
        long bestArea = -1;

        foreach (var screen in screens)
        {
            var overlap = Rectangle.Intersect(bounds, screen);
            long area = overlap.IsEmpty ? 0 : (long)overlap.Width * overlap.Height;

            if (area > bestArea)
            {
                best = screen;
                bestArea = area;
            }
        }

        return bestArea > 0 ? best : screens[0];
    }

    /// <summary>
    /// The next new window's spot: a step down and to the right of the one
    /// opened last, so a run of double-clicks fans them out instead of
    /// stacking every window exactly on the one before.
    /// </summary>
    internal static Point Cascade(Point previous, int step) => new(previous.X + step, previous.Y + step);

    /// <summary>
    /// Line up: the windows, in the order given (the running order), down
    /// the screen in columns from its top-left corner, a new column when
    /// the next one would run off the bottom. Each keeps its own height.
    /// Past the screen's right edge they start again from the left a little
    /// lower, overlapping rather than going off the screen.
    /// </summary>
    internal static List<Rectangle> LineUp(IReadOnlyList<Size> sizes, Rectangle screen, int gap)
    {
        var placed = new List<Rectangle>(sizes.Count);

        var x = screen.Left + gap;
        var y = screen.Top + gap;
        var columnWidth = 0;
        var wrap = 0;

        foreach (var size in sizes)
        {
            if (y > screen.Top + gap + wrap && y + size.Height > screen.Bottom - gap)
            {
                x += columnWidth + gap;
                y = screen.Top + gap + wrap;
                columnWidth = 0;
            }

            if (x + size.Width > screen.Right - gap && x > screen.Left + gap)
            {
                wrap += gap * 3;
                x = screen.Left + gap + wrap;
                y = screen.Top + gap + wrap;
                columnWidth = 0;
            }

            placed.Add(Fit(new Rectangle(x, y, size.Width, size.Height), new[] { screen }));

            y += size.Height + gap;
            columnWidth = Math.Max(columnWidth, size.Width);
        }

        return placed;
    }
}
