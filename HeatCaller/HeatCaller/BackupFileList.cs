using System.Drawing.Drawing2D;
using System.Globalization;

namespace HeatCaller;

/// <summary>
/// Backup and Restore's list of backups (17 Nov 2026, page jazz-up): one
/// card per backup - a calendar badge with its day and month, when it was
/// taken, its size and file name under that, and a chip saying whether it
/// was made by itself or by hand. It was a monospaced line of date, size
/// and name. Drawn as Venues' list is (VenueList): an owner-drawn ListBox,
/// so the keyboard, the selection and the scrolling are Windows' own, the
/// rows drawn from the theme at paint time. An empty folder says so in the
/// list rather than showing four blank rows.
/// </summary>
internal sealed class BackupFileList : ListBox
{
    public BackupFileList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        Tag = AppTheme.KeepBackColor;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.TextDark;

        // The picked row draws its own outline - see LayoutList.
        InputOutline.Exclude(this);
        Remeasure();
    }

    /// <summary>What a row shows, asked of the item at paint time.</summary>
    public interface IBackupRow
    {
        DateTime Taken { get; }
        long Bytes { get; }
        string FileName { get; }
        bool Automatic { get; }
    }

    /// <summary>Said in the list when the folder holds no backups.</summary>
    internal const string EmptyText = "No backups in this folder yet. Back Up Now makes the first one.";

    /// <summary>The text size changed: the rows are as tall as a line in bold and a small one under it.</summary>
    public void Remeasure()
    {
        var height = Math.Max(UiLayout.AtDpi(50), AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(22));

        if (ItemHeight != height)
        {
            ItemHeight = height;
        }

        Invalidate();
    }

    /// <summary>Drawn off-screen and put up in one go - see LayoutList.OnDrawItem.</summary>
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var buffer = BufferedGraphicsManager.Current.Allocate(e.Graphics, e.Bounds);
        DrawRow(buffer.Graphics, e);
        buffer.Render(e.Graphics);
    }

    // TextRenderer ignores the buffer's translate unless told - see LayoutList.
    private const TextFormatFlags Keep = TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
    private const TextFormatFlags Line = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | Keep;
    private const TextFormatFlags Centred = TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | Keep;

    private void DrawRow(Graphics g, DrawItemEventArgs e)
    {
        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillRectangle(back, e.Bounds);
        }

        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not IBackupRow backup)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var selected = (e.State & DrawItemState.Selected) != 0;
        var row = Rectangle.Inflate(e.Bounds, -UiLayout.AtDpi(2), -UiLayout.AtDpi(3));
        var radius = UiLayout.AtDpi(8);

        if (selected)
        {
            using var path = AppTheme.RoundedRect(new RectangleF(row.X + 0.5f, row.Y + 0.5f, row.Width - 1f, row.Height - 1f), radius);
            using var fill = new SolidBrush(AppTheme.SecondaryButton);
            g.FillPath(fill, path);
            using var pen = new Pen(AppTheme.Primary, Math.Max(1f, UiLayout.AtDpi(1)));
            g.DrawPath(pen, path);
        }
        else if (e.Index > 0)
        {
            // A hairline between two cards, so a long list still reads as rows.
            using var pen = new Pen(RosterInk.Blend(AppTheme.TextMuted, AppTheme.Surface, 0.8f));
            g.DrawLine(pen, row.X + radius, e.Bounds.Top, row.Right - radius, e.Bounds.Top);
        }

        // The badge: the day over the month, on a tint of the look's colour.
        var pad = UiLayout.AtDpi(8);
        var side = row.Height - pad * 2;
        var badge = new RectangleF(row.X + pad, row.Y + pad, side, side);

        using (var path = AppTheme.RoundedRect(badge, UiLayout.AtDpi(6)))
        using (var fill = new SolidBrush(RosterInk.Blend(AppTheme.Primary, AppTheme.Surface, selected ? 0.6f : 0.82f)))
        {
            g.FillPath(fill, path);
        }

        var taken = backup.Taken;
        var day = taken.Day.ToString(CultureInfo.CurrentCulture);
        var month = taken.ToString("MMM", CultureInfo.CurrentCulture).ToUpperInvariant();
        var dayHeight = AppTheme.BoldFont.Height;
        var monthHeight = AppTheme.MicroFont.Height;
        var top = (int)(badge.Y + (badge.Height - dayHeight - monthHeight) / 2);
        var badgeBox = Rectangle.Round(badge);

        TextRenderer.DrawText(g, day, AppTheme.BoldFont, new Rectangle(badgeBox.X, top, badgeBox.Width, dayHeight), AppTheme.Primary, Centred);
        TextRenderer.DrawText(g, month, AppTheme.MicroFont, new Rectangle(badgeBox.X, top + dayHeight, badgeBox.Width, monthHeight), AppTheme.Primary, Centred);

        // When, the chip after it, and the size and name under it.
        var x = (int)badge.Right + UiLayout.AtDpi(10);
        var width = Math.Max(1, row.Right - x - pad);
        var textHeight = AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(2);
        var y = row.Y + (row.Height - textHeight) / 2;

        var kind = backup.Automatic ? "AUTOMATIC" : "BY HAND";
        var measured = TextRenderer.MeasureText(g, kind, AppTheme.MicroFont, Size.Empty, TextFormatFlags.NoPadding);
        var chipWidth = measured.Width + UiLayout.AtDpi(16);
        var chipRoom = chipWidth + UiLayout.AtDpi(6);

        // No room for the chip beside the words on a narrow list: left off.
        if (width - chipRoom < UiLayout.AtDpi(120))
        {
            chipWidth = 0;
            chipRoom = 0;
        }

        if (chipWidth > 0)
        {
            var tone = backup.Automatic ? AppTheme.Primary : AppTheme.TextMuted;
            var chip = new RectangleF(row.Right - pad - chipWidth, y, chipWidth, AppTheme.BoldFont.Height);

            using (var path = AppTheme.RoundedRect(chip, (int)(chip.Height / 2)))
            using (var fill = new SolidBrush(RosterInk.Blend(tone, AppTheme.Surface, 0.84f)))
            {
                g.FillPath(fill, path);
            }

            TextRenderer.DrawText(g, kind, AppTheme.MicroFont, Rectangle.Round(chip), tone,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | Keep);
        }

        TextRenderer.DrawText(g, When(taken), AppTheme.BoldFont,
            new Rectangle(x, y, Math.Max(1, width - chipRoom), AppTheme.BoldFont.Height), AppTheme.TextDark, Line);

        TextRenderer.DrawText(g, $"{SizeText(backup.Bytes)}  ·  {backup.FileName}", AppTheme.SmallFont,
            new Rectangle(x, y + AppTheme.BoldFont.Height + UiLayout.AtDpi(2), width, AppTheme.SmallFont.Height), AppTheme.TextMuted, Line);
    }

    /// <summary>"Tuesday 17 November 2026, 9:04 PM" - the day spelled out, the time as the app shows times.</summary>
    internal static string When(DateTime taken) =>
        $"{taken.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)}, {AppTime.TimeOfDay(taken)}";

    /// <summary>"640 KB", "12.4 MB".</summary>
    internal static string SizeText(long bytes)
    {
        var kb = bytes / 1024.0;

        return kb < 1024
            ? $"{kb:0.#} KB"
            : kb < 1024 * 1024
                ? $"{kb / 1024:0.#} MB"
                : $"{kb / 1024 / 1024:0.##} GB";
    }

    /// <summary>The empty folder's line, in the middle of the list.</summary>
    private void DrawEmpty(Graphics g)
    {
        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        var box = Rectangle.Inflate(ClientRectangle, -UiLayout.AtDpi(12), -UiLayout.AtDpi(6));

        TextRenderer.DrawText(g, EmptyText, AppTheme.SmallFont, box, AppTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }

    private const int WmPaint = 0x000F;
    private const int WmPrintClient = 0x0318;

    /// <summary>
    /// A list with no rows never asks for one to be drawn, so the empty
    /// line goes on after Windows has painted the list - on screen, and when
    /// the list is printed to a picture (DrawToBitmap).
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (Items.Count > 0 || !IsHandleCreated)
        {
            return;
        }

        if (m.Msg == WmPaint)
        {
            using var g = CreateGraphics();
            DrawEmpty(g);
        }
        else if (m.Msg == WmPrintClient && m.WParam != IntPtr.Zero)
        {
            using var g = Graphics.FromHdc(m.WParam);
            DrawEmpty(g);
        }
    }

    /// <summary>No flashing white between rows as the list redraws.</summary>
    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var back = new SolidBrush(AppTheme.Surface);
        pevent.Graphics.FillRectangle(back, ClientRectangle);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}
