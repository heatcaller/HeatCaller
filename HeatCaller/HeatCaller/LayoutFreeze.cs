using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// Holds the layout of whatever is not on screen - a page, or a tab inside
/// one - and lays it out once when it comes back.
///
/// Why: every page is built up front and kept, and a page built on the
/// layout pieces works out its own sizes - part by part, in the fonts in
/// use - whenever anything in it changes. That is right for what is on
/// screen and pure waste for the thirty pages, and the tabs inside them,
/// that are not. Two things made all of them work at once:
///
///   Building the window. Each part added to a piece laid the piece out
///   again, and the card round it, and the page round that - so a page was
///   laid out once per part it held, all thirty of them, while the startup
///   screen waited.
///
///   A text size change (Ctrl + and -). The new font reaches every control
///   in the window, each one asked its parent to lay out again, and every
///   page did - and then every page and every tab was walked through once
///   more (MainForm.WarmPages).
///
/// Asher, 30 Sep: the startup screen was up for over a minute, and holding
/// Ctrl and pressing + or - left the window "Not Responding" for half a
/// minute at a time (the session log: 51s and 32s frozen).
///
/// So a page that is not showing has every container in it held
/// (SuspendLayout): a change still asks for a layout, and the asking is
/// remembered, but nothing is worked out. When the page is shown it is let
/// go (Thaw), parents before children, and each container that was asked
/// lays itself out once - at the size and in the fonts the page has by
/// then, which is the only layout that was ever going to be seen.
///
/// A tab that is not the one showing in its tab control stays held when its
/// page is let go, as a hold of its own, and is let go the moment it is
/// shown (its VisibleChanged) - after the tab control has given it its
/// size, and before the tab's own refresh (SelectedIndexChanged) runs. So a
/// page with seven tabs lays out the one on screen, not all seven.
///
/// And while the window is being built (BeginBuilding .. EndBuilding) each
/// layout piece holds itself from the moment it is made (Adopt), so a page
/// is not laid out part by part as it is put together; the pieces go on
/// being held with their page, and the page on screen first is laid out
/// when it is shown.
///
/// Each container is held once, by one hold: a page held again takes the
/// holds of its tabs back into its own.
/// </summary>
internal static class LayoutFreeze
{
    /// <summary>What each hold holds - a page's or a tab's containers, parents before children, in the order they are let go.</summary>
    private static readonly ConditionalWeakTable<Control, List<Control>> Held = new();

    /// <summary>Every container held by a hold here, whichever hold it is in - so none is held twice.</summary>
    private static readonly ConditionalWeakTable<Control, object> Holding = new();

    /// <summary>The tabs whose VisibleChanged lets them go - hooked once each.</summary>
    private static readonly ConditionalWeakTable<TabPage, object> Watched = new();

    private static readonly object Mark = new();

    /// <summary>
    /// The pieces made since BeginBuilding that are still holding
    /// themselves - a page held meanwhile (Freeze) takes its own over. Null
    /// when no window is being built.
    /// </summary>
    [ThreadStatic]
    private static HashSet<Control>? _building;

    /// <summary>True between BeginBuilding and EndBuilding, on this thread.</summary>
    public static bool Building => _building is not null;

    /// <summary>From here the layout pieces made on this thread hold themselves (Adopt) until EndBuilding.</summary>
    public static void BeginBuilding() => _building ??= new HashSet<Control>();

    /// <summary>
    /// For a window of its own being built: every layout piece made until
    /// the result is disposed holds its layout (Adopt), and is let go then,
    /// parents first - so the window is laid out once, not once for every
    /// part added to it. Inside a building already under way (MainForm's,
    /// or an outer hold) it does nothing: that one lets them go.
    ///
    /// Why: a window built of many pieces worked itself out again for every
    /// part put in it, and again for every face the theme pass changed - the
    /// Setup Sheet, forty lines of fields, took 3.5 to 4.8 seconds to build
    /// (window report, 1 Oct).
    /// </summary>
    public static IDisposable HoldWhileBuilding() => _building is not null ? NoHold.Instance : new BuildingHold();

    /// <summary>A hold begun by HoldWhileBuilding, ended when disposed.</summary>
    private sealed class BuildingHold : IDisposable
    {
        private bool _ended;

        public BuildingHold() => BeginBuilding();

        public void Dispose()
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            EndBuilding(Array.Empty<Control>(), shown: null);
        }
    }

    /// <summary>HoldWhileBuilding inside a building already under way: nothing to end.</summary>
    private sealed class NoHold : IDisposable
    {
        public static readonly NoHold Instance = new();

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Called by every layout piece as it is made: while a window is being
    /// built, the piece holds its layout from the start - see the class
    /// notes. Otherwise nothing.
    /// </summary>
    public static void Adopt(Control piece)
    {
        if (_building is null)
        {
            return;
        }

        piece.SuspendLayout();
        _building.Add(piece);
    }

    /// <summary>
    /// Ends the building. The page on screen, <paramref name="shown"/>, has
    /// the pieces made in it that are still holding themselves let go,
    /// parents first; every other page in <paramref name="pages"/> is held
    /// (Freeze) and takes over the pieces made in it; and every piece made
    /// that is in none of them - part of a window of its own - is let go too.
    /// </summary>
    public static void EndBuilding(IEnumerable<Control> pages, Control? shown)
    {
        var adopted = _building;
        _building = null;

        if (adopted is null)
        {
            return;
        }

        if (shown is not null)
        {
            // Parents before children: the order a walk from the top meets
            // them in.
            var inShown = new List<Control>();
            TakeAdopted(shown, inShown, adopted);

            foreach (var piece in inShown)
            {
                // Let go for good: no longer one of the holds' (TakeAdopted
                // counted it as held).
                Holding.Remove(piece);

                if (!piece.IsDisposed)
                {
                    piece.ResumeLayout(true);
                }
            }
        }

        foreach (var page in pages)
        {
            if (!ReferenceEquals(page, shown))
            {
                Freeze(page, adopted);
            }
        }

        // What is left was made during the building but is not in a page:
        // let go now, each parent before its children.
        foreach (var piece in adopted.OrderBy(Depth).ToList())
        {
            if (!piece.IsDisposed)
            {
                piece.ResumeLayout(true);
            }
        }
    }

    /// <summary>
    /// Holds the layout of <paramref name="root"/> and every container in
    /// it until Thaw. Holding one already held - by itself, or as part of a
    /// page that is held - does nothing. While the window is being built,
    /// the pieces in it that hold themselves already are taken over rather
    /// than held twice.
    /// </summary>
    public static void Freeze(Control root) => Freeze(root, _building);

    /// <summary>
    /// Lets <paramref name="root"/> go: each container in it, parents first,
    /// lays itself out once if anything asked it to while it was held -
    /// except those in a tab that is not showing, which stay held until it
    /// is (see the class notes). Does nothing for one not held.
    /// </summary>
    public static void Thaw(Control root)
    {
        if (!Held.TryGetValue(root, out var held))
        {
            return;
        }

        Held.Remove(root);

        Dictionary<TabPage, List<Control>>? waiting = null;

        foreach (var control in held)
        {
            if (control.IsDisposed)
            {
                Holding.Remove(control);
                continue;
            }

            if (HiddenTab(control, root) is { } tab)
            {
                waiting ??= new Dictionary<TabPage, List<Control>>();

                if (!waiting.TryGetValue(tab, out var inTab))
                {
                    waiting[tab] = inTab = new List<Control>();
                }

                inTab.Add(control);
                continue;
            }

            Holding.Remove(control);
            control.ResumeLayout(true);
        }

        if (waiting is null)
        {
            return;
        }

        foreach (var (tab, inTab) in waiting)
        {
            Held.AddOrUpdate(tab, inTab);
            LetGoWhenShown(tab);

            // Shown already: something laid out above it, as it was let go,
            // picked it - and no VisibleChanged is coming to let it go.
            if (tab.Visible && !tab.IsDisposed)
            {
                Thaw(tab);
            }
        }
    }

    /// <summary>Whether <paramref name="root"/> is held - as a hold of its own, or as part of one.</summary>
    public static bool IsFrozen(Control root) => Held.TryGetValue(root, out _) || Holding.TryGetValue(root, out _);

    /// <summary>
    /// Holds <paramref name="root"/>'s containers, parents before children.
    /// One in <paramref name="adopted"/> is holding itself already (Adopt):
    /// it is taken from there and kept with this hold rather than held a
    /// second time, which would take two lets-go to undo.
    /// </summary>
    private static void Freeze(Control root, HashSet<Control>? adopted)
    {
        if (Held.TryGetValue(root, out var already))
        {
            // Held already. Pieces made in it since, still holding
            // themselves, go with it.
            if (adopted is { Count: > 0 })
            {
                TakeAdopted(root, already, adopted);
            }

            return;
        }

        // Part of a hold that is not its own - a tab in a page that is held.
        if (Holding.TryGetValue(root, out _))
        {
            return;
        }

        var held = new List<Control>();
        var inner = new List<(Control Root, List<Control> Held)>();

        Collect(root, root, held, adopted, inner);

        // The holds inside this one - its tabs' - are taken into it: what
        // they held is held here now. Anything one held that is no longer
        // in this page has been moved somewhere else since, and is not
        // this hold's to keep: let go.
        if (inner.Count > 0)
        {
            var here = new HashSet<Control>(held);

            foreach (var (innerRoot, innerHeld) in inner)
            {
                Held.Remove(innerRoot);

                foreach (var control in innerHeld)
                {
                    if (!here.Contains(control))
                    {
                        Holding.Remove(control);

                        if (!control.IsDisposed)
                        {
                            control.ResumeLayout(true);
                        }
                    }
                }
            }
        }

        Held.Add(root, held);
    }

    private static void Collect(Control control, Control root, List<Control> held, HashSet<Control>? adopted, List<(Control, List<Control>)> inner)
    {
        if (!ReferenceEquals(control, root) && Held.TryGetValue(control, out var innerHeld))
        {
            inner.Add((control, innerHeld));
        }

        if (Holding.TryGetValue(control, out _))
        {
            // Held already, by a tab inside this - kept, with this hold now.
            held.Add(control);
        }
        else if (adopted is not null && adopted.Remove(control))
        {
            Holding.Add(control, Mark);
            held.Add(control);
        }
        else if (control.HasChildren || control is ScrollableControl)
        {
            // Every container, empty or not: an empty one filled while the
            // page is away (a list of cards built again) is held as well.
            control.SuspendLayout();
            Holding.Add(control, Mark);
            held.Add(control);
        }

        foreach (Control child in control.Controls)
        {
            Collect(child, root, held, adopted, inner);
        }
    }

    /// <summary>The pieces in <paramref name="control"/> that hold themselves (Adopt), taken from <paramref name="adopted"/> into <paramref name="found"/>, parents first.</summary>
    private static void TakeAdopted(Control control, List<Control> found, HashSet<Control> adopted)
    {
        if (adopted.Remove(control))
        {
            if (!Holding.TryGetValue(control, out _))
            {
                Holding.Add(control, Mark);
            }

            found.Add(control);
        }

        foreach (Control child in control.Controls)
        {
            TakeAdopted(child, found, adopted);
        }
    }

    /// <summary>
    /// The outermost tab between <paramref name="control"/> and
    /// <paramref name="root"/> that is not the one its tab control is
    /// showing - null when there is none, or when the control is no longer
    /// in <paramref name="root"/> at all.
    ///
    /// Only a tab control with a window of its own counts. Until it has
    /// one, Windows is not told which tab is showing, the tabs are not
    /// shown and hidden as it changes, and so no VisibleChanged would come
    /// to let a held tab go.
    /// </summary>
    private static TabPage? HiddenTab(Control control, Control root)
    {
        TabPage? outermost = null;

        for (Control? at = control; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, root))
            {
                return outermost;
            }

            if (at is TabPage tab
                && tab.Parent is TabControl tabs
                && tabs.IsHandleCreated
                && !ReferenceEquals(tabs.SelectedTab, tab))
            {
                outermost = tab;
            }
        }

        return null;
    }

    /// <summary>
    /// Lets a held tab go as soon as it is on screen. A tab control shows a
    /// tab by giving it its size and then making it visible, so by then it
    /// is laid out at the size it is shown at; and a tab shown because its
    /// page was is let go the same way, if its page's own hold has not.
    /// </summary>
    private static void LetGoWhenShown(TabPage tab)
    {
        if (Watched.TryGetValue(tab, out _))
        {
            return;
        }

        Watched.Add(tab, Mark);

        tab.VisibleChanged += (_, _) =>
        {
            if (tab.Visible && !tab.IsDisposed)
            {
                Thaw(tab);
            }
        };
    }

    /// <summary>How many parents a control has - so parents are let go before their children.</summary>
    private static int Depth(Control control)
    {
        var depth = 0;

        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            depth++;
        }

        return depth;
    }
}
