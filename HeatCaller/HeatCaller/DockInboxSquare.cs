using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// THE DOCK INBOX BUTTON, ON ANY PAGE (24 Nov 2026, asked for by Asher:
/// "add the same button to dock the inbox on practice like we have on race
/// control"). Race Control's button (22 Nov 2026), moved here unchanged so
/// Practice can have the very same one: a square with a window drawn in it,
/// a panel down its right-hand side - filled while the inbox is docked -
/// and the words as its tooltip and its name for a screen reader. A picture
/// and not words, because the row it stands on beside Expand has no room
/// for "Dock Inbox" at a 1280-wide window. The rules are in
/// <see cref="InboxDockButton"/>.
/// </summary>
internal sealed class DockInboxSquare : Button
{
    private readonly ToolTip _tip = new();

    /// <summary>The inbox is docked on screen now - told by MainForm whenever it moves, opens or closes.</summary>
    private bool _dockedOnScreen;

    public DockInboxSquare()
    {
        Text = string.Empty;
        AccessibleName = InboxDockButton.Caption(dockedOnScreen: false);

        Click += (_, _) => DockRequested?.Invoke(InboxDockButton.DockOnPress(_dockedOnScreen));

        AppTheme.StyleToolTip(_tip, keep: true);
        _tip.SetToolTip(this, InboxDockButton.Tip(dockedOnScreen: false));
    }

    /// <summary>Pressed: dock the inbox (true) or make it a window of its own again (false).</summary>
    public event Action<bool>? DockRequested;

    /// <summary>Whether it shows the inbox docked - for the tests.</summary>
    internal bool DockedOnScreen => _dockedOnScreen;

    /// <summary>The inbox docked, undocked or closed, from anywhere: the name, the tooltip and the picture follow.</summary>
    public void ShowDocked(bool dockedOnScreen)
    {
        _dockedOnScreen = dockedOnScreen;

        var caption = InboxDockButton.Caption(dockedOnScreen);

        if (AccessibleName != caption)
        {
            AccessibleName = caption;
            _tip.SetToolTip(this, InboxDockButton.Tip(dockedOnScreen));
            Invalidate();
        }
    }

    /// <summary>
    /// As wide as it is tall. A row sizes a button to its caption, and this
    /// one has none - left to the row it is a sliver. So whenever it is
    /// sized (on going in, and at a new text size) it is squared off again.
    /// </summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);

        if (Width != Height)
        {
            Width = Height;
        }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        if (Width != Height)
        {
            Width = Height;
        }
    }

    /// <summary>The picture, over whatever the theme painted: a window with a panel down its right-hand side, filled while the inbox is docked there.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var width = Math.Min(UiLayout.AtDpi(18), Width - UiLayout.AtDpi(12));
        var height = width * 3 / 4;
        var box = new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
        var split = box.Right - width * 2 / 5;
        var ink = Enabled ? ForeColor : AppTheme.TextMuted;

        using (var pen = new Pen(ink, UiLayout.AtDpi(1) * 1.5f))
        using (var path = AppTheme.RoundedRect(box, UiLayout.AtDpi(2)))
        {
            if (_dockedOnScreen)
            {
                using var fill = new SolidBrush(ink);
                var clip = g.Clip;
                g.SetClip(new Rectangle(split, box.Top, box.Right - split + 1, box.Height + 1));
                g.FillPath(fill, path);
                g.Clip = clip;
            }

            g.DrawPath(pen, path);
            g.DrawLine(pen, split, box.Top, split, box.Bottom);
        }

        g.Restore(saved);
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
