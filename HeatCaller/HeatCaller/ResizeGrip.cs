namespace HeatCaller;

/// <summary>
/// A thin bar under a part of a page that is dragged up or down to make the
/// part shorter or taller, and double-clicked to put it back to its own
/// size (17 Oct 2026 - first for the Race Calls list, so more of its tick
/// boxes fit on screen).
///
/// It works in screen pixels from where the drag started, not from where
/// the bar is now: the bar moves with the part it sizes, so measuring from
/// the bar itself would chase its own tail.
/// </summary>
internal sealed class ResizeGrip : Control
{
    private readonly ToolTip _tip = new();

    /// <summary>The screen Y the drag started at; null when not dragging.</summary>
    private int? _downY;

    /// <summary>The part's height when the drag started.</summary>
    private int _downHeight;

    private bool _hot;

    /// <summary>The height the part has now, asked when a drag starts.</summary>
    public Func<int>? CurrentHeight { get; set; }

    /// <summary>Raised as the bar is dragged, with the height asked for.</summary>
    public event Action<int>? Dragged;

    /// <summary>Raised once a drag ends, for the height to be saved.</summary>
    public event Action? Finished;

    /// <summary>Raised on a double-click: back to the part's own size.</summary>
    public event Action? Reset;

    public ResizeGrip()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw,
            true);

        Cursor = Cursors.SizeNS;
        TabStop = false;
        Tag = AppTheme.KeepBackColor;
        Height = Math.Max(10, 14 * DeviceDpi / 96);

        _tip.SetToolTip(this, "Drag to make the list above taller or shorter. Double-click to put it back.");
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Math.Max(1, proposedSize.Width), Height);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left)
        {
            _downY = Cursor.Position.Y;
            _downHeight = CurrentHeight?.Invoke() ?? 0;
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_downY is { } y)
        {
            Dragged?.Invoke(Math.Max(0, _downHeight + Cursor.Position.Y - y));
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    /// <summary>A drag taken away - another window, Alt+Tab - ends it too, so the height is still saved.</summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        EndDrag();
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        Reset?.Invoke();
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

    private void EndDrag()
    {
        if (_downY is null)
        {
            return;
        }

        _downY = null;
        Invalidate();
        Finished?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? BackColor);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var lit = _hot || _downY is not null;
        var width = Math.Min(Width, 64 * DeviceDpi / 96);
        var thick = Math.Max(3, 4 * DeviceDpi / 96);
        var bar = new Rectangle((Width - width) / 2, (Height - thick) / 2, width, thick);

        using var brush = new SolidBrush(lit ? AppTheme.Primary : AppTheme.Border);
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var r = thick;
        path.AddArc(bar.Left, bar.Top, r, r, 90, 180);
        path.AddArc(bar.Right - r, bar.Top, r, r, 270, 180);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
