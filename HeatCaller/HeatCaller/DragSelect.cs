namespace HeatCaller;

/// <summary>
/// Click and drag down a list to select every row the pointer passes over,
/// the way Explorer and every spreadsheet do it - in every list in the app
/// that can hold more than one selection.
///
/// What each kind of list does:
///
///  - <b>ListView</b> (the Driver Hub, Race History, linking setups...):
///    Windows' own list does not do this in its Details view, so it is done
///    here. A drag that starts on a row selects from that row to the one
///    under the pointer, and keeps up as the pointer moves - past the top or
///    bottom edge, the list scrolls and the selection follows. Hold Ctrl as
///    the drag starts to add the sweep to what was already selected. Only
///    lists whose MultiSelect is on: a single-pick list is single-pick for a
///    reason (its buttons act on one thing).
///  - <b>DataGridView</b>: Windows' grid already sweeps its selection along
///    a drag whenever MultiSelect is on, so there is nothing to add.
///  - <b>CheckedListBox</b>: a checked list's "selection" is its ticks, so
///    a drag ticks - or unticks - every row it passes. See
///    ThemedCheckedListBox.
///
/// Attached to every ListView by AppTheme.ApplyRecursively, which every
/// window and page runs over itself - so a list added anywhere in the app
/// gets it without having to ask. Attaching twice is harmless.
///
/// Uses the list's ItemDrag, which Windows raises once the pointer has moved
/// past the drag threshold with a row pressed: an ordinary click and a
/// double-click are never mistaken for a sweep. A list that ever wants
/// ItemDrag for dragging rows somewhere else should turn its MultiSelect
/// off, or be left out here.
/// </summary>
internal static class DragSelect
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ListView, object> Attached = new();

    /// <summary>Gives a list click-and-drag selection. Anything but a ListView is left alone - see the class summary.</summary>
    public static void Attach(Control control)
    {
        if (control is not ListView list || Attached.TryGetValue(list, out _))
        {
            return;
        }

        Attached.Add(list, new object());

        list.ItemDrag += (_, e) =>
        {
            // Read at the moment of the drag rather than when attached, so a
            // list that turns multi-select on later sweeps too.
            if (e.Button != MouseButtons.Left
                || !list.MultiSelect
                || list.View != View.Details
                || e.Item is not ListViewItem item
                || item.Index < 0)
            {
                return;
            }

            new Sweep(list, item.Index).Start();
        };
    }

    /// <summary>
    /// The rows a sweep covers: from the anchor to the row under the pointer,
    /// either way, plus what was kept from before (a Ctrl sweep).
    /// </summary>
    internal static bool Covers(int index, int anchor, int hover, IReadOnlySet<int>? kept) =>
        (index >= Math.Min(anchor, hover) && index <= Math.Max(anchor, hover))
        || (kept?.Contains(index) ?? false);

    /// <summary>
    /// One drag, from press to release. Follows the pointer on a short timer
    /// rather than the list's mouse events: once Windows has decided a drag
    /// began, the list itself stops reporting the mouse, and a timer also
    /// keeps the list scrolling while the pointer is held still past its
    /// edge.
    /// </summary>
    private sealed class Sweep
    {
        private readonly ListView _list;
        private readonly int _anchor;
        private readonly HashSet<int>? _kept;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };

        private int _hover = -1;

        public Sweep(ListView list, int anchor)
        {
            _list = list;
            _anchor = anchor;

            // Ctrl held as the drag starts adds the sweep to the selection
            // already there; otherwise the sweep IS the selection.
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                _kept = list.SelectedIndices.Cast<int>().ToHashSet();
            }
        }

        public void Start()
        {
            _timer.Tick += (_, _) => Step();
            _timer.Start();
            Step();
        }

        private void Stop()
        {
            _timer.Stop();
            _timer.Dispose();
        }

        private void Step()
        {
            if ((Control.MouseButtons & MouseButtons.Left) == 0
                || _list.IsDisposed
                || !_list.Visible
                || _anchor >= _list.Items.Count)
            {
                Stop();
                return;
            }

            var hover = RowUnderPointer();

            if (hover < 0 || hover == _hover)
            {
                return;
            }

            _hover = hover;

            for (var i = 0; i < _list.Items.Count; i++)
            {
                var want = Covers(i, _anchor, hover, _kept);
                var row = _list.Items[i];

                // Only what changes: each change is a SelectedIndexChanged,
                // and the pages listening rebuild their buttons on it.
                if (row.Selected != want)
                {
                    row.Selected = want;
                }
            }

            var under = _list.Items[hover];
            under.Focused = true;
            under.EnsureVisible();
        }

        /// <summary>
        /// The row the pointer is on. Above the first row showing (over the
        /// column headers, or above the list altogether) it is the row before
        /// that, and below the last it is the row after - which, with
        /// EnsureVisible, is what scrolls the list a row a tick while the
        /// pointer is held past its edge.
        /// </summary>
        private int RowUnderPointer()
        {
            var count = _list.Items.Count;

            if (count == 0 || _list.TopItem is not { } top)
            {
                return -1;
            }

            var p = _list.PointToClient(Cursor.Position);
            var rowTop = top.Bounds.Top;

            if (p.Y < rowTop)
            {
                return Math.Max(0, top.Index - 1);
            }

            var client = _list.ClientRectangle;

            // Any x inside the first column finds the row, full-row select or not.
            var x = Math.Max(top.Bounds.Left + 2, 2);

            if (p.Y >= client.Bottom)
            {
                var last = _list.GetItemAt(x, client.Bottom - 2)?.Index ?? count - 1;
                return Math.Min(count - 1, last + 1);
            }

            // In the list but below its last row.
            return _list.GetItemAt(x, p.Y)?.Index ?? count - 1;
        }
    }
}
