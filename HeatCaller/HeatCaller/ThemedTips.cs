using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// Switches off the pop-ups Windows puts up by itself - the ones that
/// appear over a cut-off cell, item or node and show the same words that
/// are already under the mouse.
///
/// Run over EVERY control the theme pass walks, rather than page by page.
/// Each control type has its own switch for these, one of them buried in a
/// native extended style that the obvious property does not cover, and a
/// page cannot be expected to remember all of them - the pages that did
/// remember were the only quiet ones, which is why these kept turning up
/// somewhere new.
///
/// What the app deliberately says stays: a note a page wrote on a cell or a
/// menu item still shows, through GridTips and MenuTips, in the app's own
/// colours. This only stops Windows speaking for itself.
/// </summary>
internal static class NativeTips
{
    private const int LvmSetExtendedListViewStyle = 0x1036;

    /// <summary>A list view's pop-up for an item carrying an info tip.</summary>
    private const int LvsExInfoTip = 0x0400;

    /// <summary>
    /// A list view's "label tip" - the one that unfolds the full text of an
    /// entry too narrow to show it. NOT covered by ShowItemToolTips, which
    /// is why a list could still pop up after being told not to.
    /// </summary>
    private const int LvsExLabelTip = 0x4000;

    [DllImport("user32", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void Silence(Control control)
    {
        switch (control)
        {
            case DataGridView grid:
                grid.ShowCellToolTips = false;
                break;

            case ListView listView:
                // A list that asked for its item notes (ShowItemToolTips on)
                // still shows them, in the app's own colours - Track
                // Layouts' who-holds-each-sector-record is one. See ListTips.
                if (listView.ShowItemToolTips)
                {
                    ListTips.Attach(listView);
                }

                listView.ShowItemToolTips = false;
                ClearListViewTips(listView);

                // A list view rebuilds its extended styles from scratch
                // every time its handle comes back - and a look change, a
                // font change and a re-parent all bring it back. Without
                // this the tips return the first time the look is switched.
                listView.HandleCreated -= ListViewHandleCreated;
                listView.HandleCreated += ListViewHandleCreated;
                break;

            case TreeView treeView:
                treeView.ShowNodeToolTips = false;
                break;

            case TabControl tabControl:
                tabControl.ShowToolTips = false;
                break;
        }
    }

    private static void ListViewHandleCreated(object? sender, EventArgs e)
    {
        if (sender is ListView listView)
        {
            ClearListViewTips(listView);
        }
    }

    /// <summary>
    /// Clears both tip bits and leaves every other extended style alone -
    /// the first argument is the mask of bits to touch, the second what to
    /// set those bits to.
    /// </summary>
    private static void ClearListViewTips(ListView listView)
    {
        if (!listView.IsHandleCreated)
        {
            // Done again from HandleCreated, hooked above.
            return;
        }

        try
        {
            SendMessage(listView.Handle, LvmSetExtendedListViewStyle, (IntPtr)(LvsExInfoTip | LvsExLabelTip), IntPtr.Zero);
        }
        catch
        {
            // A cosmetic tidy-up is never worth taking a window down for.
        }
    }
}

/// <summary>
/// Themed pop-ups for a grid, in place of its own.
///
/// A DataGridView's built-in pop-up is drawn by Windows in its pale
/// yellow-white, which no theme reaches, and it appears on its own for any
/// cell whose text is cut off - showing nothing but what is already under
/// the mouse. So the grid's own is switched off, and this shows only a note
/// the page deliberately gave a cell (its ToolTipText) or a column heading
/// (the column's ToolTipText) - a driver's warnings, what a column means.
/// </summary>
internal static class GridTips
{
    private static readonly ConditionalWeakTable<DataGridView, ToolTip> Attached = new();

    /// <summary>Grids whose notes are still wanted - see <see cref="Keep"/>.</summary>
    private static readonly ConditionalWeakTable<DataGridView, object> Wanted = new();

    private static readonly object WantedMarker = new();

    /// <summary>
    /// Keeps this grid's notes showing while the rest of the app's pop-ups
    /// are off.
    ///
    /// Only the leaderboard asks for this. It writes real notes on its
    /// cells - a jump start, a held release, a warning and its reason - and
    /// mid-race there is nowhere else on screen that says them.
    ///
    /// Safe either side of the theme pass: called first, the grid is marked
    /// and Attach honours the mark; called after, the tip already made is
    /// woken here.
    /// </summary>
    public static void Keep(DataGridView grid)
    {
        Wanted.AddOrUpdate(grid, WantedMarker);

        if (Attached.TryGetValue(grid, out var existing))
        {
            AppTheme.StyleToolTip(existing, keep: true);
        }
    }

    public static void Attach(DataGridView grid)
    {
        grid.ShowCellToolTips = false;

        if (Attached.TryGetValue(grid, out _))
        {
            return;
        }

        var tip = new ToolTip();
        AppTheme.StyleToolTip(tip, keep: Wanted.TryGetValue(grid, out _));
        Attached.Add(grid, tip);

        grid.CellMouseEnter += (_, e) =>
        {
            // Said again at the moment it would matter. Whatever put it
            // back - a page rebuilding its columns, a look change, a
            // control that resets itself when its handle returns - it is
            // off again before anything can appear.
            grid.ShowCellToolTips = false;
            Show(grid, tip, e.RowIndex, e.ColumnIndex);
        };
        grid.CellMouseLeave += (_, _) => tip.Hide(grid);
        grid.MouseLeave += (_, _) => tip.Hide(grid);
        grid.Scroll += (_, _) => tip.Hide(grid);
        grid.Disposed += (_, _) => tip.Dispose();
    }

    private static void Show(DataGridView grid, ToolTip tip, int rowIndex, int columnIndex)
    {
        tip.Hide(grid);

        if (columnIndex < 0 || columnIndex >= grid.Columns.Count || rowIndex >= grid.Rows.Count)
        {
            return;
        }

        string? text;

        try
        {
            text = rowIndex < 0
                ? grid.Columns[columnIndex].ToolTipText
                : grid.Rows[rowIndex].Cells[columnIndex].ToolTipText;
        }
        catch
        {
            // A row removed as the mouse arrived - nothing to show.
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var cell = grid.GetCellDisplayRectangle(columnIndex, rowIndex, false);
        AppTheme.ShowToolTip(tip, text, grid, cell.Left + 8, cell.Bottom + 2);
    }
}

/// <summary>
/// Themed pop-ups for menu items that carry a note (ToolTipText), in place
/// of the menu's own pale Windows ones.
/// </summary>
internal static class MenuTips
{
    private static readonly ConditionalWeakTable<ToolStrip, ToolTip> Attached = new();

    public static void Attach(ToolStrip menu)
    {
        var wanted = menu.ShowItemToolTips;
        menu.ShowItemToolTips = false;

        if (!wanted || Attached.TryGetValue(menu, out _))
        {
            return;
        }

        var tip = new ToolTip();

        // Kept, because getting this far means the menu asked for its item
        // notes (ShowItemToolTips was on) - the Undo list's "also undoes the
        // newer steps" note is one. A tip styled without keep is switched off
        // for good and ShowToolTip refuses it, and the menu's own Windows
        // tips were just turned off above, so the notes would show nowhere.
        AppTheme.StyleToolTip(tip, keep: true);
        Attached.Add(menu, tip);

        foreach (ToolStripItem item in menu.Items)
        {
            item.MouseEnter += (_, _) =>
            {
                tip.Hide(menu);

                if (string.IsNullOrWhiteSpace(item.ToolTipText))
                {
                    return;
                }

                // Beside the item rather than under the mouse, so it doesn't
                // cover the next item down.
                AppTheme.ShowToolTip(tip, item.ToolTipText, menu, item.Bounds.Right + 4, item.Bounds.Top);
            };

            item.MouseLeave += (_, _) => tip.Hide(menu);
        }

        menu.Disposed += (_, _) => tip.Dispose();
    }
}

/// <summary>
/// Themed pop-ups for list items that carry a note (ToolTipText), in place
/// of the list's own pale Windows ones - which NativeTips switches off for
/// every list. Only for a list that asked for its notes.
/// </summary>
internal static class ListTips
{
    private static readonly ConditionalWeakTable<ListView, ToolTip> Attached = new();

    public static bool IsAttached(ListView list) => Attached.TryGetValue(list, out _);

    /// <summary>The note to show over an item: what the page wrote on it, or none.</summary>
    public static string? NoteFor(ListViewItem? item) =>
        item is null || string.IsNullOrWhiteSpace(item.ToolTipText) ? null : item.ToolTipText;

    public static void Attach(ListView list)
    {
        if (Attached.TryGetValue(list, out _))
        {
            return;
        }

        var tip = new ToolTip();

        // Kept, for the same reason as MenuTips: the list asked for its item
        // notes, and its own Windows tips are off.
        AppTheme.StyleToolTip(tip, keep: true);
        Attached.Add(list, tip);

        // The item the note now showing belongs to, so moving the mouse
        // along one row doesn't put the same note up again and again.
        ListViewItem? under = null;

        list.MouseMove += (_, e) =>
        {
            ListViewItem? item;

            try
            {
                item = list.HitTest(e.Location).Item;
            }
            catch
            {
                // The list emptied as the mouse moved - nothing to show.
                item = null;
            }

            if (ReferenceEquals(item, under))
            {
                return;
            }

            under = item;
            tip.Hide(list);

            if (NoteFor(item) is { } note)
            {
                AppTheme.ShowToolTip(tip, note, list, item!.Bounds.Left + 8, item.Bounds.Bottom + 2);
            }
        };

        list.MouseLeave += (_, _) =>
        {
            under = null;
            tip.Hide(list);
        };
        list.Disposed += (_, _) => tip.Dispose();
    }
}

/// <summary>
/// A label that ends in "..." when its text doesn't fit - without the pop-up.
///
/// WinForms' AutoEllipsis comes with a Windows tooltip that appears
/// whenever the text is cut off, showing the same text again in the pale
/// system style. It can't be themed or switched off on its own, so the
/// label's own ellipsis is turned off and the label is drawn with one here
/// instead: same look, no pop-up.
/// </summary>
internal static class QuietEllipsis
{
    private static readonly ConditionalWeakTable<Label, object> Attached = new();

    public static void Attach(Label label)
    {
        label.AutoEllipsis = false;

        if (Attached.TryGetValue(label, out _))
        {
            return;
        }

        Attached.Add(label, new object());
        label.Paint += (_, e) => Repaint(label, e.Graphics);
        label.TextChanged += (_, _) => label.Invalidate();
    }

    /// <summary>
    /// Whether a label ends in "..." this way - chose to be cut short when
    /// its text does not fit, as one with AutoEllipsis does. The theme pass
    /// swaps every AutoEllipsis label over to this, so LayoutAudit asks here
    /// as well as there.
    /// </summary>
    public static bool IsAttached(Label label) => Attached.TryGetValue(label, out _);

    private static void Repaint(Label label, Graphics g)
    {
        var text = label.Text;
        var bounds = new Rectangle(
            label.Padding.Left,
            label.Padding.Top,
            Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal),
            Math.Max(1, label.ClientSize.Height - label.Padding.Vertical));

        var font = label.Font;
        var flags = TextFormatFlags.EndEllipsis | FlagsFor(label.TextAlign);

        if (!label.UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }

        // More than one line of room: wrap, and end the last line with "...".
        // TextBoxControl is what makes Windows end the last whole line with
        // it - without it, a line cut in half by the bottom edge was drawn,
        // and no "..." at all.
        if (bounds.Height >= font.Height * 2)
        {
            flags |= TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
        }

        // Only when it doesn't fit - otherwise the label's own drawing is
        // already exactly right.
        var needed = TextRenderer.MeasureText(g, text, font, new Size(bounds.Width, int.MaxValue),
            (flags & ~TextFormatFlags.EndEllipsis) | TextFormatFlags.WordBreak);
        var oneLine = (flags & TextFormatFlags.WordBreak) == 0
            ? TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding).Width > bounds.Width
            : needed.Height > bounds.Height;

        if (!oneLine)
        {
            return;
        }

        var back = label.BackColor.A == 255 ? label.BackColor : label.Parent?.BackColor ?? AppTheme.Background;
        using (var brush = new SolidBrush(back))
        {
            g.FillRectangle(brush, label.ClientRectangle);
        }

        var colour = label is LinkLabel link ? link.LinkColor : label.ForeColor;
        if (!label.Enabled)
        {
            colour = AppTheme.TextMuted;
        }

        using var drawFont = label is LinkLabel ? new Font(font, font.Style | FontStyle.Underline) : null;
        TextRenderer.DrawText(g, text, drawFont ?? font, bounds, colour, flags);
    }

    private static TextFormatFlags FlagsFor(ContentAlignment align)
    {
        var flags = align switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => TextFormatFlags.HorizontalCenter,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.Left,
        };

        flags |= align switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight => TextFormatFlags.VerticalCenter,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
            _ => TextFormatFlags.Top,
        };

        return flags;
    }
}

/// <summary>
/// A note that appears beside something on hover - drawn exactly like the
/// app's tooltips, but not a ToolTip.
///
/// A WinForms ToolTip that has once been shown over a control stays tied to
/// it, and while it is switched on the framework uses it for its own pop-ups
/// on that control as well - the keyboard ones that follow the focused cell
/// among them. So switching the grid's tip on for one column's note put
/// boxes over the other columns too, repeating the text already in them.
/// This is a plain window instead: it shows only when told to, and only
/// what it is told.
/// </summary>
internal sealed class HoverNote : IDisposable
{
    // The margins round the note and the width it wraps at are at the
    // screen's DPI, as its point-size text is - on a screen the app draws at
    // 150% (--dpi-aware) the words are half as big again in pixels.
    private static int PadX => UiLayout.AtDpi(10);
    private static int PadY => UiLayout.AtDpi(7);
    private static int MaxWidth => UiLayout.AtDpi(460);

    private const TextFormatFlags Format =
        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

    private readonly Control _owner;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 8000 };
    private NoteWindow? _window;

    public HoverNote(Control owner)
    {
        _owner = owner;
        _timer.Tick += (_, _) => Hide();
        owner.Disposed += (_, _) => Dispose();
        owner.VisibleChanged += (_, _) => Hide();
    }

    /// <summary>Shows the note with its top-left at a point on the owner, kept on screen.</summary>
    public void Show(string text, Point clientPoint)
    {
        if (string.IsNullOrWhiteSpace(text) || _owner.IsDisposed || !_owner.IsHandleCreated || !_owner.Visible
            || _owner.FindForm() is not { IsDisposed: false } form)
        {
            Hide();
            return;
        }

        var measured = TextRenderer.MeasureText(text, AppTheme.BodyFont, new Size(MaxWidth, int.MaxValue), Format);
        var size = new Size(measured.Width + PadX * 2, measured.Height + PadY * 2);

        var at = _owner.PointToScreen(clientPoint);
        var screen = Screen.FromControl(_owner).WorkingArea;

        at.X = Math.Max(screen.Left, Math.Min(at.X, screen.Right - size.Width));
        at.Y = Math.Max(screen.Top, Math.Min(at.Y, screen.Bottom - size.Height));

        if (_window is null || _window.IsDisposed)
        {
            _window = new NoteWindow();
        }

        _window.Note = text;
        _window.Bounds = new Rectangle(at, size);
        _window.Invalidate();

        if (!_window.Visible)
        {
            _window.Show(form);
        }

        _timer.Stop();
        _timer.Start();
    }

    public void Hide()
    {
        _timer.Stop();

        if (_window is { IsDisposed: false, Visible: true })
        {
            _window.Hide();
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _window?.Dispose();
        _window = null;
    }

    /// <summary>The note itself: no frame, never takes focus, and the mouse goes straight through it.</summary>
    private sealed class NoteWindow : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        public string Note { get; set; } = string.Empty;

        public NoteWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = AppTheme.Surface;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            // Clicks and hovering belong to whatever is underneath.
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // The same look as AppTheme's owner-drawn tooltips.
            using (var back = new SolidBrush(AppTheme.Surface))
            {
                e.Graphics.FillRectangle(back, ClientRectangle);
            }

            using (var edge = new Pen(AppTheme.Border))
            {
                e.Graphics.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
            }

            var text = new Rectangle(PadX, PadY, Math.Max(4, Width - PadX * 2), Math.Max(4, Height - PadY * 2));
            TextRenderer.DrawText(e.Graphics, Note, AppTheme.BodyFont, text, AppTheme.TextDark, Format);
        }
    }
}
