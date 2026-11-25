using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// SHOWING SOMETHING WITHOUT THE WHITE SCROLL BAR FLASHING (24 Nov 2026,
/// Asher: "theres a lot of times i see a native scroll bar when first
/// clicking onto a screen ... only the first time it loads"; plan in
/// plans/native-scrollbar-flash.md).
///
/// WHY: a part of a page built while hidden is never laid out, so it keeps
/// the size it was made at - 150x150 for a UserControl, 200x100 for a
/// Panel. Shown for the first time, it is laid out at that size before its
/// parent gives it its real one, its content overflows, and Windows draws
/// its own white scroll bar there and then. The slim bar over it
/// (ThinScrollBar) only caught up on its 150 ms timer, after that frame was
/// already on screen. Driver Hub's Garage, Transponders, Setups and
/// Batteries tabs did it on every first visit, and so did tabs on Race Day,
/// Sounds and Race Analytics.
///
/// So painting is held on the container while the change is made: the new
/// part is shown and laid out, every slim bar is put over its native one,
/// and the container is drawn once, frames and all. Nothing is drawn in
/// between, so whatever the native bar did there is never seen. The Inbox's
/// Details (DeskInboxForm.ToggleSheet, 21 Nov 2026) has done this since it
/// first flashed the same way.
/// </summary>
internal static class ShowWithoutFlash
{
    private const int WmSetRedraw = 0x000B;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;
    private const int SwHide = 0;

    /// <summary>
    /// Makes <paramref name="change"/> with painting held on
    /// <paramref name="container"/>, then draws it once. A container with no
    /// window yet, or not on screen, has nothing to hold: the change is made
    /// as it is.
    /// </summary>
    public static void Hold(Control container, Action change)
    {
        if (!container.IsHandleCreated || container.IsDisposed || !IsWindowVisible(container.Handle))
        {
            change();
            return;
        }

        // Inside a hold on the whole window: that one is let go, and draws, at the end.
        if (IsHeldWhole(container))
        {
            change();
            ThinScrollBar.SyncNow();
            return;
        }

        var handle = container.Handle;
        SendMessage(handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);

        try
        {
            change();

            if (!container.IsDisposed)
            {
                container.PerformLayout();
            }

            // Every slim bar over its native one before anything is drawn -
            // left to the timer, the native bar showed first.
            ThinScrollBar.SyncNow();
        }
        finally
        {
            Release(container, handle);
        }
    }

    /// <summary>
    /// Holds painting on <paramref name="container"/> now, for a change that
    /// is made somewhere else - a tab control's own tab change. Give the
    /// result to <see cref="End"/> once it is made. Null when there was
    /// nothing to hold.
    /// </summary>
    public static IntPtr? Begin(Control container)
    {
        if (!container.IsHandleCreated || container.IsDisposed || !IsWindowVisible(container.Handle) || IsHeldWhole(container))
        {
            return null;
        }

        SendMessage(container.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        return container.Handle;
    }

    /// <summary>Lets go of a <see cref="Begin"/>: the slim bars brought up to date, and the container drawn once.</summary>
    public static void End(Control container, IntPtr? held)
    {
        if (held is not { } handle)
        {
            return;
        }

        try
        {
            ThinScrollBar.SyncNow();
        }
        finally
        {
            Release(container, handle);
        }
    }

    /// <summary>The windows held whole now - see <see cref="HoldWhole"/>.</summary>
    private static readonly HashSet<Form> HeldWhole = new();

    /// <summary>Whether <paramref name="control"/>'s window is held whole now.</summary>
    public static bool IsHeldWhole(Control control)
    {
        if (HeldWhole.Count == 0)
        {
            return false;
        }

        // Up through every parent: the docked inbox is a form inside the main window.
        for (var c = control; c is not null; c = c.Parent)
        {
            if (c is Form form && HeldWhole.Contains(form))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Holds painting on the whole of <paramref name="window"/> until the
    /// result is disposed, and every hold inside it (Hold, Begin, the
    /// sidebar's own) is then a plain change: nothing is drawn until the end,
    /// and then it is only marked to be drawn, not drawn there and then.
    ///
    /// WHY (25 Nov 2026, Asher on the Focus animation: "would you pay $10k for
    /// that animation?"): under the Focus stage the sidebar, the column and the
    /// inbox's docking each held painting and then drew the whole window at
    /// once - three full paints of a window nobody could see, behind a stage
    /// that waits for the window to be ready before half of it can move.
    /// Measured at 1440x900: focus 718-1287 ms, restore 483-733 ms.
    /// </summary>
    public static IDisposable HoldWhole(Form window)
    {
        if (!window.IsHandleCreated || window.IsDisposed || HeldWhole.Contains(window))
        {
            return new Releaser(null);
        }

        SendMessage(window.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        HeldWhole.Add(window);
        return new Releaser(window);
    }

    private sealed class Releaser(Form? window) : IDisposable
    {
        private Form? _window = window;

        public void Dispose()
        {
            if (_window is not { } window)
            {
                return;
            }

            _window = null;
            HeldWhole.Remove(window);

            if (window.IsDisposed || !window.IsHandleCreated || !IsWindow(window.Handle))
            {
                return;
            }

            try
            {
                ThinScrollBar.SyncNow();
            }
            finally
            {
                SendMessage(window.Handle, WmSetRedraw, 1, IntPtr.Zero);

                // Marked to be drawn, frames too - drawn when the caller (or
                // Windows) next paints, once, at the new layout.
                RedrawWindow(window.Handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwFrame | RdwAllChildren);
            }
        }
    }

    /// <summary>The tab controls whose tab changes are held - each hooked once.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TabControl, TabHold> HeldTabs = new();

    /// <summary>
    /// Holds painting on <paramref name="tabs"/> from just before a tab
    /// changes until the change has settled, every time. Safe to call more
    /// than once.
    ///
    /// WHY (24 Nov 2026): a tab the startup warm-up had not reached (it
    /// stops early on a busy PC) has its windows made as it is picked, and a
    /// grid made then draws its own sideways bar at its made size, 200x100,
    /// before the tab is even shown - so a hold that starts when the tab
    /// shows is too late. Sounds' Imaging, Ambience and Music did it on a
    /// copy of the club's own data. Held from Selecting, before any of
    /// that, and let go once everything the change queued has run.
    ///
    /// Held on the tab control's PARENT: Windows' own tab control answers
    /// "hold painting" itself and goes on drawing (tried, 24 Nov 2026), so
    /// held on itself nothing under it was held at all.
    /// </summary>
    public static void HoldTabChanges(TabControl tabs)
    {
        if (HeldTabs.TryGetValue(tabs, out _))
        {
            return;
        }

        var hold = new TabHold(tabs);
        HeldTabs.Add(tabs, hold);
    }

    /// <summary>One tab control's hold: begun on Selecting, ended after SelectedIndexChanged.</summary>
    private sealed class TabHold
    {
        private readonly TabControl _tabs;
        private Control? _place;
        private IntPtr? _held;
        private int _change;

        public TabHold(TabControl tabs)
        {
            _tabs = tabs;
            tabs.Selecting += (_, _) => Starting();
            tabs.SelectedIndexChanged += (_, _) => Changed();
            tabs.Disposed += (_, _) => _held = null;
        }

        private void Starting()
        {
            if (_held is not null || !_tabs.IsHandleCreated)
            {
                return;
            }

            _place = _tabs.Parent;
            _held = _place is null ? null : Begin(_place);

            if (_held is null)
            {
                return;
            }

            // A change called off (another handler cancels Selecting) has no
            // SelectedIndexChanged to end it: this does, unless the change
            // went ahead, which ends it itself.
            var change = ++_change;
            _tabs.BeginInvoke(() =>
            {
                if (change == _change && !_changed)
                {
                    Finish();
                }
            });
            _changed = false;
        }

        private bool _changed;

        private void Changed()
        {
            if (_held is null || _tabs.IsDisposed)
            {
                return;
            }

            _changed = true;

            // Queued, so it runs after what the change itself queued: a
            // layout asked for while one was running, the columns fitted.
            // Its window gone mid-change, there is nothing left to hold.
            try
            {
                _tabs.BeginInvoke(Finish);
            }
            catch (InvalidOperationException)
            {
                _held = null;
            }
        }

        private void Finish()
        {
            var held = _held;
            var place = _place;
            _held = null;
            _place = null;
            _changed = false;

            if (place is { IsDisposed: false })
            {
                End(place, held);
            }
        }
    }

    private static void Release(Control container, IntPtr handle)
    {
        // The window may have gone with the change (a page torn down).
        if (!IsWindow(handle))
        {
            return;
        }

        // In a finally, always: a window left with painting off never draws
        // again.
        SendMessage(handle, WmSetRedraw, 1, IntPtr.Zero);

        // Painting off is Windows hiding the window from painting, and on
        // is it showing it again - so one the change hid has to be hidden
        // again, or it would be on screen while WinForms has it hidden.
        if (container.IsDisposed || !container.Visible)
        {
            ShowWindow(handle, SwHide);
            return;
        }

        // Frames too: a scroll bar is drawn in its window's frame, and a
        // plain Refresh leaves the children's frames as they were.
        RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwFrame | RdwAllChildren | RdwUpdateNow);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr update, IntPtr region, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);
}
