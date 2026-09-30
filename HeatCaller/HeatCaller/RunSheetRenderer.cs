namespace HeatCaller;

/// <summary>
/// Renders the run sheet as a printable page - the day's running order
/// with a tick column for the drivers' stand. Plain black-on-white like
/// the heat sheet, for the same reason: it comes out of a printer and
/// lives under shop lighting, not on the screen.
/// </summary>
public static class RunSheetRenderer
{
    // Letter paper at 150 DPI, matching HeatSheetRenderer.
    private const int PageWidth = 1275;
    private const int PageHeight = 1650;
    private const int Margin = 70;

    public static Bitmap Render(string title, IReadOnlyList<ScheduleEntry> entries)
    {
        var bitmap = new Bitmap(PageWidth, PageHeight);

        // 96 DPI, not the screen's: under --dpi-aware at 150% the point-size text would draw half as big again.
        bitmap.SetResolution(96, 96);

        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var titleFont = new Font("Segoe UI", 22F, FontStyle.Bold);
        using var headerFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 12F);
        using var doneFont = new Font("Segoe UI", 12F, FontStyle.Strikeout);
        using var footFont = new Font("Segoe UI", 9F);
        using var black = new SolidBrush(Color.Black);
        using var grey = new SolidBrush(Color.FromArgb(110, 110, 110));
        using var linePen = new Pen(Color.FromArgb(170, 170, 170));
        using var heavyPen = new Pen(Color.Black, 1.6f);

        var y = Margin;

        g.DrawString(title, titleFont, black, Margin, y);
        y += 40;
        g.DrawString($"Run sheet  -  {DateTime.Now:dddd, MMM d yyyy}", footFont, grey, Margin, y);
        y += 28;
        g.DrawLine(heavyPen, Margin, y, PageWidth - Margin, y);
        y += 16;

        // Tick box / time / class / what. Started times are printed when a
        // line has one - a sheet printed mid-day is a record, not a plan.
        int[] colX = { Margin, Margin + 90, Margin + 260, Margin + 560 };
        string[] headers = { "Done", "Time", "Class", "What" };

        for (var i = 0; i < headers.Length; i++)
        {
            g.DrawString(headers[i], headerFont, grey, colX[i], y);
        }

        y += 26;
        g.DrawLine(linePen, Margin, y, PageWidth - Margin, y);
        y += 8;

        foreach (var entry in entries)
        {
            const int rowHeight = 40;

            if (y + rowHeight > PageHeight - Margin)
            {
                g.DrawString("(continued - the rest of the day did not fit this page)", footFont, grey, Margin, y + 6);
                break;
            }

            // The tick box - pre-filled for lines already done, blank for
            // the pen at the stand otherwise.
            var box = new Rectangle(colX[0] + 6, y + 8, 20, 20);
            g.DrawRectangle(heavyPen, box);

            if (entry.IsComplete)
            {
                g.DrawLine(heavyPen, box.Left + 3, box.Top + 10, box.Left + 8, box.Bottom - 4);
                g.DrawLine(heavyPen, box.Left + 8, box.Bottom - 4, box.Right - 3, box.Top + 3);
            }

            var font = entry.IsComplete ? doneFont : bodyFont;
            var ink = entry.IsComplete ? grey : black;

            g.DrawString(entry.ScheduledTime, font, ink, colX[1], y + 6);
            g.DrawString(entry.ClassName, font, ink, colX[2], y + 6);

            var what = entry.Description;

            if (entry.ActualStartUtc is { } started)
            {
                what += $"   (started {started.ToLocalTime():HH:mm})";
            }

            g.DrawString(what, font, ink, colX[3], y + 6);

            y += rowHeight;
            g.DrawLine(linePen, Margin, y, PageWidth - Margin, y);
            y += 2;
        }

        g.DrawString("HeatCaller", footFont, grey, PageWidth - Margin - 90, PageHeight - Margin + 20);

        return bitmap;
    }
}
