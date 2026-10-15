using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// A slim themed scrollbar that sits on top of a control's native one -
/// vertical OR horizontal, decided at attach time.
///
/// Native scrollbars cannot be recoloured. They are drawn by the system
/// theme for the whole desktop, and the two usual ways at them both end
/// badly: SetWindowTheme strips visual styles and gives you the Windows 95
/// grey bar instead of a white one, and owner-drawing them through WM_NCPAINT
/// means reimplementing hit-testing, thumb dragging, auto-repeat and the
/// arrow buttons by hand, in the non-client area, for every control type.
///
/// This does neither. The native bar is left exactly where it is, still
/// working - mouse wheel, keyboard, and the control's own scrolling logic are
/// untouched - and a plain sibling control is parked on top of it. Covering
/// it is far more robust than hiding it, because controls re-show their own
/// scrollbars whenever content changes and there is no way to make that stop.
///
/// Driving it goes through the scrollbar API rather than any one control's
/// members, so the same code scrolls a multiline text box, a list box, a
/// checked list, a list view and an auto-scrolling panel without knowing or
/// caring which it is - with one exception: dragging the thumb. Panels and
/// list views do not take a thumb position from the message, so those two
/// are moved directly (see Send).
/// </summary>
internal sealed class ThinScrollBar : Control
{
    private const int SB_HORZ = 0;
    private const int SB_VERT = 1;

    private const int SIF_RANGE = 0x0001;
    private const int SIF_PAGE = 0x0002;
    private const int SIF_POS = 0x0004;
    private const int SIF_ALL = SIF_RANGE | SIF_PAGE | SIF_POS;

    private const int WM_HSCROLL = 0x0114;
    private const int WM_VSCROLL = 0x0115;

    private const int SB_LINEUP = 0;
    private const int SB_LINEDOWN = 1;
    private const int SB_PAGEUP = 2;
    private const int SB_PAGEDOWN = 3;
    private const int SB_THUMBPOSITION = 4;
    private const int SB_THUMBTRACK = 5;
    private const int SB_ENDSCROLL = 8;

    /// <summary>
    /// The shortest the thumb gets, at the screen's DPI - as the bar's own
    /// thickness is (SystemInformation's scroll bar size already is).
    /// </summary>
    private static int MinimumThumb => UiLayout.AtDpi(26);

    /// <summary>The thumb's thickness across the bar, at the screen's DPI for the same reason.</summary>
    private static int ThumbThickness => UiLayout.AtDpi(7);

    private static readonly ConditionalWeakTable<Control, ThinScrollBar> AttachedV = new();
    private static readonly ConditionalWeakTable<Control, ThinScrollBar> AttachedH = new();
    /// <summary>
    /// The bars made on this thread - see _sync for why per thread.
    /// </summary>
    [ThreadStatic]
    private static List<WeakReference<ThinScrollBar>>? _live;

    private static List<WeakReference<ThinScrollBar>> Live => _live ??= new();

    /// <summary>
    /// One shared poll for every bar in the app.
    ///
    /// There is no notification when a control's scroll range changes -
    /// typing a line into a text box or adding a row to a list simply makes
    /// the native bar appear - so the range has to be read back. One timer
    /// for all of them keeps that to a single tick rather than one per
    /// control, and 150ms is under the threshold where a scrollbar appearing
    /// feels late.
    ///
    /// One per thread, with its list. A window timer ticks only on the
    /// thread that made it, and dies with it. The app has one UI thread, so
    /// this changes nothing there; the tests build windows on a thread per
    /// test, and with one timer for the process every test after the first
    /// had bars nothing re-synced - or, had an earlier test's thread still
    /// been running, a timer on it walking this thread's bars.
    /// </summary>
    [ThreadStatic]
    private static System.Windows.Forms.Timer? _sync;

    private readonly Control _target;

    /// <summary>
    /// The tables and flow panels between the target and where the bar is
    /// parked (HostFor passes over them), heard moving - see WatchBetween.
    /// </summary>
    private readonly List<Control> _between = new();

    /// <summary>
    /// The target when it is a DataGridView, which needs its own path.
    ///
    /// A grid does NOT have native scrollbars. WinForms gives it two
    /// ScrollBar CHILD controls and drives them itself, so there is nothing
    /// on the grid's own window handle for GetScrollInfo to read - it
    /// returns a dead range, this bar decides it is not needed, and the
    /// grid's own child bar is left showing in the system colours. That is
    /// why every grid in the app still had a white scrollbar while every
    /// list and text box had a themed one.
    ///
    /// Rows are the honest unit here anyway: a grid scrolls a row at a
    /// time, so the range is the row count and the page is how many rows
    /// are on screen.
    /// </summary>
    private readonly DataGridView? _grid;

    /// <summary>True for the bottom bar, false for the right one. The one field the whole geometry keys off.</summary>
    private readonly bool _horizontal;

    private readonly int _thickness;

    private bool _dragging;
    private int _dragOffset;
    private bool _hot;

    private int _min;
    private int _max;
    private int _page;
    private int _pos;

    private ThinScrollBar(Control target, bool horizontal)
    {
        _target = target;
        _horizontal = horizontal;

        // Vertical only. A grid's columns are almost always Fill-sized in
        // this app, so the horizontal case does not arise, and rows are
        // the only unit the row-based path below can speak in.
        _grid = horizontal ? null : target as DataGridView;

        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        // Never take focus. Clicking the bar must not pull the caret out of
        // the text box it belongs to.
        SetStyle(ControlStyles.Selectable, false);

        DoubleBuffered = true;
        TabStop = false;

        _thickness = Math.Max(10, horizontal
            ? SystemInformation.HorizontalScrollBarHeight
            : SystemInformation.VerticalScrollBarWidth);

        Visible = false;

        // Named handlers rather than lambdas, so Dispose can take them off
        // again. They were lambdas, and the consequence was not only a leak:
        // the target holds the bar alive through its own event list, so a bar
        // disposed with its window stayed subscribed to a target that had
        // outlived that window - and the next ParentChanged put a dead control
        // into a live form.
        target.LocationChanged += TargetMoved;
        target.SizeChanged += TargetMoved;
        target.VisibleChanged += TargetMoved;
        target.ParentChanged += TargetReparented;
        target.Disposed += TargetDisposed;

        Reparent();
    }

    /// <summary>Themes the control's vertical scrollbar. Kept as the bare name for the many existing callers.</summary>
    public static void Attach(Control target) => Attach(target, horizontal: false);

    /// <summary>Themes the control's horizontal scrollbar as well, for lists and grids wider than their box.</summary>
    public static void AttachHorizontal(Control target) => Attach(target, horizontal: true);

    private static void Attach(Control target, bool horizontal)
    {
        // A tab page's parent is its tab control, which can hold nothing but
        // tab pages - there is nowhere to park an overlay. Its contents
        // scroll inside it with their own bars instead.
        if (target is TabPage || target.Parent is TabControl)
        {
            return;
        }

        var table = horizontal ? AttachedH : AttachedV;

        if (table.TryGetValue(target, out var already))
        {
            // A live one: nothing to do.
            if (!already.IsDisposed)
            {
                return;
            }

            // A DEAD one, and the entry has to go or the target never gets
            // another. A bar parks itself as a SIBLING of its target, so it
            // belongs to whatever window the target was in - and a control
            // that outlives that window (the scrutineering log borrows one,
            // and any dialog that hosts a long-lived page does the same) comes
            // back with its bar disposed underneath it. Left in the table, the
            // check above would hand back the corpse for the rest of the
            // session and the target would scroll with the system's own bright
            // white bar.
            table.Remove(target);
        }

        var bar = new ThinScrollBar(target, horizontal);
        table.Add(target, bar);

        Live.Add(new WeakReference<ThinScrollBar>(bar));

        if (_sync is null)
        {
            _sync = new System.Windows.Forms.Timer { Interval = 150 };
            _sync.Tick += (_, _) => SyncAll();
            _sync.Start();
        }
    }

    private static void SyncAll()
    {
        for (var i = Live.Count - 1; i >= 0; i--)
        {
            if (!Live[i].TryGetTarget(out var bar) || bar.IsDisposed)
            {
                Live.RemoveAt(i);
                continue;
            }

            // Parked in the wrong place: nowhere yet, because its target was
            // attached while the tables around it were not in a window, or
            // somewhere those tables have since been moved away from. See
            // HostFor.
            if (!ReferenceEquals(bar.Parent, HostFor(bar._target)))
            {
                bar.Reparent();
            }

            bar.Refresh(fromTimer: true);
        }
    }

    /// <summary>
    /// Where the bar is parked: beside its target, in the target's parent -
    /// unless that parent is a table or a flow panel (the layout pieces,
    /// LayoutPieces.cs). Those place every child they hold, so a bar added to
    /// one becomes one more cell in the table, laid out in a row of its own
    /// rather than over the target. It goes in the nearest container above
    /// them that leaves its children where they are put.
    /// </summary>
    private static Control? HostFor(Control target)
    {
        var host = target.Parent;

        while (host is TableLayoutPanel or FlowLayoutPanel)
        {
            host = host.Parent;
        }

        return host;
    }

    /// <summary>
    /// The target's bounds in the host's coordinates. The same as its own
    /// bounds when the host is its parent; otherwise moved by the position of
    /// each table or flow panel in between - those have no border, so where
    /// one sits is where its children's coordinates start.
    /// </summary>
    private Rectangle TargetBounds()
    {
        var bounds = _target.Bounds;

        for (var between = _target.Parent; between is not null; between = between.Parent)
        {
            if (ReferenceEquals(between, Parent))
            {
                return bounds;
            }

            bounds.Offset(between.Location);
        }

        // The bar is not above the target at all - its tables were moved
        // somewhere else since it was parked, and the next sync re-parks it.
        // The target's own bounds until then, rather than every ancestor's
        // position summed into nonsense.
        return _target.Bounds;
    }

    private void Reparent()
    {
        // Ours first, and this is the one that mattered. A bar is disposed with
        // the window it was parked in, but it is still subscribed to its
        // target's ParentChanged - and the target can outlive that window. So
        // when the target was put into a second window, a DISPOSED control
        // added itself to that window's Controls, and the first WM_SHOWWINDOW
        // walked the children calling CreateControl on it:
        //
        //   ObjectDisposedException ... at Control.CreateHandle()
        //   at Control.CreateControl(Boolean) ... at Form.WndProc
        //
        // which is thrown from a message pump, so nothing in the code that
        // opened the window is anywhere near the stack.
        if (IsDisposed || Disposing)
        {
            return;
        }

        var parent = HostFor(_target);

        if (parent is null || parent.IsDisposed)
        {
            return;
        }

        // See Attach: a tab control takes only tab pages.
        if (parent is TabControl)
        {
            return;
        }

        if (Parent != parent)
        {
            Parent?.Controls.Remove(this);
            parent.Controls.Add(this);
        }

        WatchBetween();
        Reposition();
    }

    /// <summary>
    /// Hears each table between the target and the bar's host move, and
    /// moves the bar with it there and then.
    ///
    /// A table moving carries the target with it without the target itself
    /// moving, which raises nothing on the target; the timer caught it up to
    /// 150ms later (Refresh). But a bar parked in a panel that scrolls - a
    /// notes box's, in a field grid on a dialog's page - counts towards that
    /// panel's scrolling range while it is where the box used to be. A page
    /// made shorter moved the grid up and left the bar below the new bottom:
    /// the page took a scrollbar it did not need, which took the width of
    /// everything on it, and kept both once the bar caught up - the layout
    /// that would have put them back was asked for while one was running.
    /// Tech Inspection's class drop-down came out 17px too narrow at
    /// 1920x1080 (layout test, 30 Sep), where the window is sized at the
    /// screen's full height first and then at its own. Moved as the table
    /// moves, the bar is in its place before the page works out its range.
    /// </summary>
    private void WatchBetween()
    {
        UnwatchBetween();

        for (var between = _target.Parent; between is TableLayoutPanel or FlowLayoutPanel; between = between.Parent)
        {
            between.LocationChanged += BetweenMoved;
            _between.Add(between);
        }
    }

    private void UnwatchBetween()
    {
        foreach (var between in _between)
        {
            between.LocationChanged -= BetweenMoved;
        }

        _between.Clear();
    }

    /// <summary>ApplyBounds, never Reposition: this runs inside the host's layout, and Refresh reads the target's scroll range, which is not wanted there. A hidden bar counts towards nothing, and is placed as it is shown.</summary>
    private void BetweenMoved(object? sender, EventArgs e)
    {
        if (_shown)
        {
            ApplyBounds();
        }
    }

    /// <summary>
    /// Our OWN idea of whether the bar is showing.
    ///
    /// Control.Visible's getter does not return what the setter was given -
    /// it returns EFFECTIVE visibility, which is false whenever any ancestor
    /// is hidden or being destroyed. Writing true and reading back false is
    /// therefore normal during teardown, and code that loops until the read
    /// agrees with the write never finishes. That is exactly what killed the
    /// app: applying a soundscape disposes the sound pages, the target's
    /// VisibleChanged fired mid-DestroyHandle, and Reposition/Refresh bounced
    /// off each other until the stack ran out - 0xc00000fd, no dialog, no
    /// crash.log, because a stack overflow cannot be caught.
    /// </summary>
    private bool _shown;

    private void SetShown(bool shown)
    {
        _shown = shown;

        if (Visible != shown)
        {
            Visible = shown;
        }
    }

    /// <summary>
    /// Moves the bar over its target and nothing else.
    ///
    /// Split out so Refresh has something to call that cannot call Refresh
    /// back. The two used to call each other, which is only safe while every
    /// exit condition holds - and one of them did not.
    /// </summary>
    private void ApplyBounds()
    {
        if (IsDisposed || Disposing || _target.IsDisposed || Parent is null)
        {
            return;
        }

        var bounds = PlacedBounds();

        if (Bounds != bounds)
        {
            Bounds = bounds;
        }
    }

    /// <summary>
    /// Where the bar belongs: straight over the native bar - the bottom strip
    /// for a horizontal bar, the right strip for a vertical one.
    /// </summary>
    private Rectangle PlacedBounds()
    {
        var target = TargetBounds();

        return _horizontal
            ? new Rectangle(
                target.Left,
                target.Bottom - _thickness,
                Math.Max(1, target.Width),
                _thickness)
            : new Rectangle(
                target.Right - _thickness,
                target.Top,
                _thickness,
                Math.Max(1, target.Height));
    }

    private void Reposition()
    {
        ApplyBounds();
        Refresh(fromTimer: false);
    }

    private void Refresh(bool fromTimer)
    {
        // Disposing as well as IsDisposed: the window is torn down long
        // before the flag flips, and this runs from the target's own
        // VisibleChanged while that is happening.
        if (IsDisposed || Disposing || _target.IsDisposed || _target.Disposing
            || !_target.IsHandleCreated || Parent is null || Parent.IsDisposed)
        {
            return;
        }

        if (!_target.Visible)
        {
            SetShown(false);
            return;
        }

        int min;
        int max;
        int page;
        int pos;
        bool scrollable;

        if (_grid is not null)
        {
            var rows = _grid.RowCount;

            // DisplayedRowCount(false) counts only rows fully on screen,
            // which is the page size a wheel notch or a page-down moves by.
            var shown = Math.Max(1, _grid.DisplayedRowCount(false));

            min = 0;
            max = Math.Max(0, rows - 1);
            page = shown;
            pos = Math.Max(0, _grid.FirstDisplayedScrollingRowIndex);
            scrollable = rows > shown;
        }
        else
        {
            // A panel that scrolls by itself (AutoScroll) says whether it
            // scrolls this way, and that is the answer. Its window's scroll
            // information is not: one that has never needed to scroll this
            // way still reports Windows' blank 0..100 with no page - which
            // read as "a hundred to scroll" and showed a bar that never went
            // away. Six of them stood round Driver Hub's garage, two on
            // every scrolling part of it (3 Oct).
            if (_target is ScrollableControl { AutoScroll: true } scroller
                && !(_horizontal ? scroller.HorizontalScroll.Visible : scroller.VerticalScroll.Visible))
            {
                SetShown(false);
                return;
            }

            var info = new SCROLLINFO
            {
                cbSize = Marshal.SizeOf<SCROLLINFO>(),
                fMask = SIF_ALL,
            };

            if (!GetScrollInfo(_target.Handle, _horizontal ? SB_HORZ : SB_VERT, ref info))
            {
                SetShown(false);
                return;
            }

            min = info.nMin;
            max = info.nMax;
            page = info.nPage;
            pos = info.nPos;

            var range = info.nMax - info.nMin + 1;
            scrollable = info.nPage > 0 ? range > info.nPage : range > 1;
        }

        // Nothing to scroll, but Windows is still drawing its bar - a text
        // box set to always show one (the Demo Mode log, empty at the start)
        // keeps a greyed-out white bar with its arrows. Hiding this bar then
        // uncovered it. Covered instead with an empty track, no thumb.
        var idle = !scrollable && NativeBarShown();

        if (!scrollable && !idle)
        {
            SetShown(false);
            return;
        }

        var changed = _idle != idle || _min != min || _max != max || _page != page || _pos != pos;

        _idle = idle;

        _min = min;
        _max = max;
        _page = page;
        _pos = pos;

        // _shown, never Visible - see the field. And ApplyBounds, never
        // Reposition: Refresh must not be able to re-enter itself.
        if (!_shown)
        {
            SetShown(true);

            // Only on the transition. Doing this every tick would restack the
            // parent's children 7 times a second.
            BringToFront();
            ApplyBounds();
        }
        else if (fromTimer && Bounds != PlacedBounds())
        {
            // Also catches a table above the target moving it without the
            // target itself moving - which raises nothing the bar hears.
            ApplyBounds();
        }

        if (changed)
        {
            Invalidate();
        }
    }

    /// <summary>True while covering a native bar that has nothing to scroll - painted as an empty track.</summary>
    private bool _idle;

    private const int GWL_STYLE = -16;
    private const int WS_HSCROLL = 0x00100000;
    private const int WS_VSCROLL = 0x00200000;

    /// <summary>Whether Windows is showing the native bar this one sits over.</summary>
    private bool NativeBarShown()
    {
        if (_grid is not null || !_target.IsHandleCreated)
        {
            return false;
        }

        var style = GetWindowLong(_target.Handle, GWL_STYLE);
        return (style & (_horizontal ? WS_HSCROLL : WS_VSCROLL)) != 0;
    }

    private int MaxPos => Math.Max(_min, _max - Math.Max(0, _page - 1));

    /// <summary>The length of the sliding surface - the control's long side, whichever way it runs.</summary>
    private int TrackLength => Math.Max(1, (_horizontal ? Width : Height) - 4);

    private int ThumbLength
    {
        get
        {
            var range = Math.Max(1, _max - _min + 1);
            var proportion = _page <= 0 ? 0.2f : Math.Clamp(_page / (float)range, 0.05f, 1f);

            // The least never above the track: Math.Clamp throws when it is,
            // and a bar shorter than the least thumb (a short list, or any
            // bar at a high DPI) would have thrown from paint.
            return Math.Clamp((int)(TrackLength * proportion), Math.Min(MinimumThumb, TrackLength), TrackLength);
        }
    }

    private int ThumbStart
    {
        get
        {
            var travel = Math.Max(0, TrackLength - ThumbLength);
            var span = Math.Max(1, MaxPos - _min);

            return 2 + (int)(travel * Math.Clamp((_pos - _min) / (float)span, 0f, 1f));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // The track takes the control's own background rather than the page
        // canvas: the bar sits inside the field's edge, not beside it.
        using (var back = new SolidBrush(_target.BackColor))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        // Nothing to scroll yet: just the track, covering Windows' bar.
        if (_idle)
        {
            return;
        }

        // 7px thick at 96 DPI (ThumbThickness), centred on the short axis;
        // long axis follows the thumb. Its rounding scales with it.
        var across = ThumbThickness;
        var thumb = _horizontal
            ? new RectangleF(ThumbStart, Height / 2f - across / 2f, Math.Max(MinimumThumb, ThumbLength), across)
            : new RectangleF(Width / 2f - across / 2f, ThumbStart, across, Math.Max(MinimumThumb, ThumbLength));

        using var path = AppTheme.RoundedRect(thumb, UiLayout.AtDpi(3));
        using var brush = new SolidBrush(_dragging || _hot ? AppTheme.Primary : AppTheme.TextMuted);

        g.FillPath(brush, path);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }

    /// <summary>The pointer position along the bar's long axis.</summary>
    private int Along(MouseEventArgs e) => _horizontal ? e.X : e.Y;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || _idle)
        {
            return;
        }

        var start = ThumbStart;
        var length = ThumbLength;
        var at = Along(e);

        if (at >= start && at < start + length)
        {
            _dragging = true;
            _dragOffset = at - start;
            Capture = true;
            Invalidate();
            return;
        }

        Send(at < start ? SB_PAGEUP : SB_PAGEDOWN, 0);
        Refresh(fromTimer: false);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_dragging)
        {
            return;
        }

        var travel = Math.Max(1, TrackLength - ThumbLength);
        var offset = Math.Clamp(Along(e) - _dragOffset - 2, 0, travel);
        var span = MaxPos - _min;
        var target = _min + (int)Math.Round(span * (offset / (float)travel));

        Send(SB_THUMBTRACK, target);
        _pos = target;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Capture = false;

        Send(SB_THUMBPOSITION, _pos);
        Send(SB_ENDSCROLL, 0);

        Refresh(fromTimer: false);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        // The pointer is over the bar, not the control, so the wheel would
        // otherwise do nothing at all.
        var lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        var steps = Math.Abs(e.Delta) / 120 * lines;

        for (var i = 0; i < steps && i < 40; i++)
        {
            Send(e.Delta > 0 ? SB_LINEUP : SB_LINEDOWN, 0);
        }

        Refresh(fromTimer: false);
    }

    private void Send(int code, int position)
    {
        if (_target.IsDisposed || !_target.IsHandleCreated)
        {
            return;
        }

        if (_grid is not null)
        {
            ScrollGrid(code, position);
            return;
        }

        // The two thumb codes cannot go to every control as a message. Only
        // the edit and list box controls (text boxes, rich text, list boxes,
        // checked lists) read the position from the message's high word. A
        // WinForms panel or page (ScrollableControl) and the Windows list
        // view both ignore it and ask GetScrollInfo for the native bar's
        // TRACKING position instead - and the native bar is under this one,
        // never dragged, so that is simply where it already is. Dragging the
        // thumb moved the thumb and nothing else, and the next sync tick
        // snapped it back to the top. Those two are moved directly; the line
        // and page codes (wheel, track clicks) work as messages for all of
        // them and still go that way.
        if (code is SB_THUMBTRACK or SB_THUMBPOSITION)
        {
            if (_target is ScrollableControl scrollable)
            {
                ScrollPanelTo(scrollable, position);
                return;
            }

            if (_target is ListView list)
            {
                ScrollListViewTo(list, position);
                return;
            }
        }

        // The high word carries the position for the two thumb codes and is
        // ignored for the rest.
        var wParam = (IntPtr)((position << 16) | (code & 0xFFFF));

        SendMessage(_target.Handle, _horizontal ? WM_HSCROLL : WM_VSCROLL, wParam, IntPtr.Zero);
    }

    /// <summary>
    /// Moves a grid by rows. The scroll codes are the Win32 ones the rest
    /// of this class speaks in, translated into the one property a
    /// DataGridView actually exposes for this.
    /// </summary>
    private void ScrollGrid(int code, int position)
    {
        if (_grid is null || _grid.RowCount == 0)
        {
            return;
        }

        var shown = Math.Max(1, _grid.DisplayedRowCount(false));
        var last = Math.Max(0, _grid.RowCount - shown);
        var current = Math.Max(0, _grid.FirstDisplayedScrollingRowIndex);

        var wanted = code switch
        {
            SB_LINEUP => current - 1,
            SB_LINEDOWN => current + 1,
            SB_PAGEUP => current - shown,
            SB_PAGEDOWN => current + shown,
            SB_THUMBTRACK or SB_THUMBPOSITION => position,
            _ => current,
        };

        wanted = Math.Clamp(wanted, 0, last);

        if (wanted == current)
        {
            return;
        }

        try
        {
            _grid.FirstDisplayedScrollingRowIndex = wanted;
        }
        catch (ArgumentOutOfRangeException)
        {
            // The row count can change between the read above and this
            // assignment - a refresh on another thread's callback, say.
            // Losing one scroll step is not worth taking the app down for.
        }
        catch (InvalidOperationException)
        {
            // The grid refuses to scroll while a cell edit it can't commit
            // is open (or onto a frozen row). Same answer: skip the step.
        }
    }

    /// <summary>
    /// Moves a panel or page to a thumb position. Its native bar counts in
    /// pixels of content (WinForms keeps nPos equal to the scroll offset), so
    /// the position is the offset as it stands. AutoScrollPosition is read
    /// back negative and written positive, and the axis this bar does not
    /// drive keeps its current offset. The setter clamps to the content.
    ///
    /// No Scroll event comes of this, but ScrollRepaint's Paint catch-all
    /// notices the move on the strip Windows repaints and asks for the rest.
    /// </summary>
    private void ScrollPanelTo(ScrollableControl scrollable, int position)
    {
        var current = scrollable.AutoScrollPosition;

        var wanted = _horizontal
            ? new Point(position, -current.Y)
            : new Point(-current.X, position);

        if (wanted == new Point(-current.X, -current.Y))
        {
            return;
        }

        scrollable.AutoScrollPosition = wanted;
    }

    private const int LVM_SCROLL = 0x1000 + 20;

    /// <summary>
    /// Moves a list view to a thumb position with LVM_SCROLL, which scrolls
    /// by a distance in pixels rather than to a place - so the distance is
    /// worked out from where its native bar says it is now.
    ///
    /// Setting the native bar's position first and then sending the thumb
    /// message does NOT do it for a list view: it compares the position it
    /// reads against the tracking position to decide how far to move, finds
    /// them equal, and moves nothing.
    ///
    /// The units differ by view. In Details (every list view in the app) the
    /// vertical bar counts ROWS, so rows are turned into pixels by the row
    /// height; the horizontal bar there, and both bars in the icon views,
    /// count pixels already. List view's horizontal bar counts columns, which
    /// are as wide as an item.
    /// </summary>
    private void ScrollListViewTo(ListView list, int position)
    {
        var info = new SCROLLINFO
        {
            cbSize = Marshal.SizeOf<SCROLLINFO>(),
            fMask = SIF_POS,
        };

        if (!GetScrollInfo(list.Handle, _horizontal ? SB_HORZ : SB_VERT, ref info))
        {
            return;
        }

        var delta = position - info.nPos;

        if (delta == 0)
        {
            return;
        }

        var unit = 1;
        var rowsOrColumns = _horizontal ? list.View == View.List : list.View == View.Details;

        if (rowsOrColumns)
        {
            if (list.Items.Count == 0)
            {
                return;
            }

            var first = list.GetItemRect(0);
            unit = Math.Max(1, _horizontal ? first.Width : first.Height);
        }

        var pixels = (long)delta * unit;
        var clamped = (int)Math.Clamp(pixels, int.MinValue, int.MaxValue);

        SendMessage(
            list.Handle,
            LVM_SCROLL,
            (IntPtr)(_horizontal ? clamped : 0),
            (IntPtr)(_horizontal ? 0 : clamped));
    }

    private void TargetMoved(object? sender, EventArgs e) => Reposition();

    private void TargetReparented(object? sender, EventArgs e) => Reparent();

    private void TargetDisposed(object? sender, EventArgs e) => Dispose();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Off the target completely. A bar belongs to the window it was
            // parked in, and the control it scrolls can outlive that window -
            // so a bar that is disposed but still listening is a dead control
            // waiting to be handed to whatever window the target lands in
            // next. Detaching is what makes that impossible rather than merely
            // guarded against.
            _target.LocationChanged -= TargetMoved;
            _target.SizeChanged -= TargetMoved;
            _target.VisibleChanged -= TargetMoved;
            _target.ParentChanged -= TargetReparented;
            _target.Disposed -= TargetDisposed;
            UnwatchBetween();

            Parent?.Controls.Remove(this);
        }

        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public int cbSize;
        public int fMask;
        public int nMin;
        public int nMax;
        public int nPage;
        public int nPos;
        public int nTrackPos;
    }

    // The 32-bit call on purpose: the style bits fit in it, and it exists
    // in both 32- and 64-bit Windows, where GetWindowLongPtr does not.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref SCROLLINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
