using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// A line of muted text inside a grid or a list with nothing in it, saying
/// why it is empty and what fills it - in place of a white box that could
/// as well be broken.
///
/// Asher's pictures, 30 Sep: Photos with no photos, Chassis Types with
/// none left, Pace before lap times were kept, a Timetable with nothing on
/// it, Scoring rules with no race days to list, and the Announcer with
/// nothing running each showed a blank box and nothing else.
///
/// The words are asked for each time it is drawn, so one note can say
/// different things as the state changes, and null or empty says nothing.
/// They are in the control's own face, so they follow the text size.
/// </summary>
internal static class EmptyNote
{
    private static readonly ConditionalWeakTable<DataGridView, Func<string?>> GridNotes = new();
    private static readonly ConditionalWeakTable<ListBox, ListNote> ListNotes = new();

    /// <summary>The flags the note is drawn with: centred across, wrapped to the width.</summary>
    private const TextFormatFlags Flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

    /// <summary>
    /// Says <paramref name="text"/> in <paramref name="grid"/> while it has
    /// no rows - none but the empty one for adding, in a grid that has one -
    /// under its headings. Set it again to change the words.
    /// </summary>
    public static void For(DataGridView grid, Func<string?> text)
    {
        if (GridNotes.TryGetValue(grid, out _))
        {
            GridNotes.AddOrUpdate(grid, text);
            grid.Invalidate();
            return;
        }

        GridNotes.Add(grid, text);

        // After the grid has painted itself - a grid raises Paint last.
        grid.Paint += PaintGridNote;

        // The rows coming and going, and a new size, all paint the note
        // in or out again.
        grid.RowsAdded += (_, _) => grid.Invalidate();
        grid.RowsRemoved += (_, _) => grid.Invalidate();
        grid.Resize += (_, _) => grid.Invalidate();
    }

    /// <summary>Says <paramref name="text"/> in <paramref name="list"/> while it has no items. Set it again to change the words.</summary>
    public static void For(ListBox list, Func<string?> text)
    {
        if (ListNotes.TryGetValue(list, out var note))
        {
            note.Text = text;
            list.Invalidate();
            return;
        }

        ListNotes.Add(list, new ListNote(list, text));
    }

    /// <summary>The room under a grid's headings, or the whole of a list, less a margin all round.</summary>
    private static Rectangle Room(Rectangle client, int top)
    {
        var margin = UiLayout.AtTextSize(16);

        return Rectangle.FromLTRB(client.Left + margin, top + margin, client.Right - margin, client.Bottom - margin / 2);
    }

    private static void Draw(Graphics g, string text, Font font, Rectangle room)
    {
        if (room.Width < 8 || room.Height < 4)
        {
            return;
        }

        TextRenderer.DrawText(g, text, font, room, AppTheme.TextMuted, Flags);
    }

    private static void PaintGridNote(object? sender, PaintEventArgs e)
    {
        if (sender is not DataGridView grid || !GridNotes.TryGetValue(grid, out var text))
        {
            return;
        }

        var rows = grid.Rows.Count - (grid.AllowUserToAddRows && grid.Rows.Count > 0 ? 1 : 0);

        if (rows > 0 || text() is not { Length: > 0 } words)
        {
            return;
        }

        var top = grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0;

        // Under the empty row for adding, in a grid that has one.
        if (grid.AllowUserToAddRows && grid.Rows.Count > 0)
        {
            top += grid.Rows[0].Height;
        }

        Draw(e.Graphics, words, grid.DefaultCellStyle.Font ?? grid.Font, Room(grid.ClientRectangle, top));
    }

    /// <summary>
    /// A list's note, drawn over it once Windows has painted it - a list
    /// raises no Paint of its own. Bound to the list's window, and bound
    /// again whenever it is made anew (the theme pass does that, see
    /// PlainList).
    /// </summary>
    private sealed class ListNote : NativeWindow
    {
        private const int WM_PAINT = 0x000F;

        private readonly ListBox _list;

        public Func<string?> Text { get; set; }

        public ListNote(ListBox list, Func<string?> text)
        {
            _list = list;
            Text = text;

            list.HandleCreated += (_, _) => Bind();
            list.HandleDestroyed += (_, _) => ReleaseHandle();
            list.Disposed += (_, _) => ReleaseHandle();

            // The words are centred, so a new size moves them.
            list.Resize += (_, _) =>
            {
                if (list.Items.Count == 0)
                {
                    list.Invalidate();
                }
            };

            Bind();
        }

        private void Bind()
        {
            if (!_list.IsHandleCreated || _list.IsDisposed || Handle == _list.Handle)
            {
                return;
            }

            if (Handle != IntPtr.Zero)
            {
                ReleaseHandle();
            }

            AssignHandle(_list.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg != WM_PAINT || _list.IsDisposed || _list.Items.Count > 0 || Text() is not { Length: > 0 } words)
            {
                return;
            }

            // Windows has painted the empty list and validated it; the words
            // go on top, on the list's own window, clipped to it.
            var dc = GetDC(Handle);

            if (dc == IntPtr.Zero)
            {
                return;
            }

            try
            {
                // The list's colour under them first: a paint that only
                // validated part of the list leaves the words drawn last
                // time where they were, and text drawn again over its own
                // smoothed edges comes out heavier each time. The list is
                // empty, so all of it is ours to fill.
                using var g = Graphics.FromHdc(dc);
                using (var back = new SolidBrush(_list.BackColor))
                {
                    g.FillRectangle(back, _list.ClientRectangle);
                }

                Draw(g, words, _list.Font, Room(_list.ClientRectangle, 0));
            }
            finally
            {
                ReleaseDC(Handle, dc);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    }
}
