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
    private const float PrintDpi = 150f;
    private const int Margin = 70;

    // Where the first line starts on every page: below the title (40), the
    // date line (28), the rule (16), the column headings (26) and their rule
    // (8). Each page repeats all of that, so every page starts at the same y.
    private const int FirstRowTop = Margin + 40 + 28 + 16 + 26 + 8;

    // A line's box and text, then the rule under it and the gap after.
    private const int RowHeight = 40;
    private const int RowStep = RowHeight + 2;

    /// <summary>
    /// How many lines of the running order fit on one page: as many as can
    /// start at FirstRowTop, a RowStep apart, and still end above the bottom
    /// margin. Worked out from the same numbers the drawing uses, so the two
    /// cannot drift apart.
    /// </summary>
    internal static int RowsPerPage { get; } = CountRowsPerPage();

    private static int CountRowsPerPage()
    {
        var rows = 0;

        for (var y = FirstRowTop; y + RowHeight <= PageHeight - Margin; y += RowStep)
        {
            rows++;
        }

        return rows;
    }

    /// <summary>
    /// Renders the whole running order onto as many pages as it needs, one
    /// bitmap per page, in order. The caller owns the bitmaps and disposes
    /// them.
    ///
    /// The run sheet used to be one page that stopped after about 33 lines
    /// with a "continued" note, and nothing printed the rest - a full day
    /// of four classes with practice, qualifiers and mains runs past that,
    /// so the drivers' stand lost the end of the day. Every line now lands
    /// on a page.
    /// </summary>
    public static IReadOnlyList<Bitmap> RenderPages(string title, IReadOnlyList<ScheduleEntry> entries)
    {
        // Always at least one page, so an empty day still prints its header.
        var pageCount = Math.Max(1, (entries.Count + RowsPerPage - 1) / RowsPerPage);
        var pages = new List<Bitmap>(pageCount);

        try
        {
            for (var page = 0; page < pageCount; page++)
            {
                var slice = entries.Skip(page * RowsPerPage).Take(RowsPerPage).ToList();
                var note = page < pageCount - 1 ? $"(continued on page {page + 2} of {pageCount})" : null;

                pages.Add(RenderPage(title, slice, page + 1, pageCount, note));
            }
        }
        catch
        {
            // A half-made set of pages is no use to the caller, and it could
            // not dispose what it never received.
            foreach (var made in pages)
            {
                made.Dispose();
            }

            throw;
        }

        return pages;
    }

    /// <summary>
    /// The first page only - the shape the run sheet export had before it
    /// could make more than one page. Kept so a caller that saves a single
    /// image still works; RenderPages is the one that prints every line.
    /// When lines are left over, the note at the foot says how many were
    /// left off, rather than "continued" when no continuation is printed.
    /// </summary>
    public static Bitmap Render(string title, IReadOnlyList<ScheduleEntry> entries)
    {
        var leftOver = entries.Count - RowsPerPage;

        string? note = leftOver switch
        {
            <= 0 => null,
            1 => "(1 more line did not fit and is not on this sheet)",
            _ => $"({leftOver} more lines did not fit and are not on this sheet)",
        };

        return RenderPage(title, entries.Take(RowsPerPage).ToList(), 1, 1, note);
    }

    private static Bitmap RenderPage(
        string title,
        IReadOnlyList<ScheduleEntry> entries,
        int pageNumber,
        int pageCount,
        string? continuedNote)
    {
        var bitmap = new Bitmap(PageWidth, PageHeight);

        try
        {
            DrawPage(bitmap, title, entries, pageNumber, pageCount, continuedNote);

            // The page is drawn at 96 DPI so the point-size fonts come out
            // the right size, but the pixels are a Letter page at 150 DPI.
            // The saved PNG carries whatever resolution the bitmap has, and
            // left at 96 it described a 13.3 x 17.2 inch page, so anything
            // that prints an image at its own size (Paint at 100%, for one)
            // cropped the sheet or spread it over several pages. Setting it
            // here, after drawing, changes only that label - not a pixel.
            bitmap.SetResolution(PrintDpi, PrintDpi);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    private static void DrawPage(
        Bitmap bitmap,
        string title,
        IReadOnlyList<ScheduleEntry> entries,
        int pageNumber,
        int pageCount,
        string? continuedNote)
    {
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

        // The page count goes on the date line, so a loose second page at
        // the drivers' stand says that a first page exists somewhere.
        var pagePart = pageCount > 1 ? $"  -  page {pageNumber} of {pageCount}" : "";
        g.DrawString($"Run sheet  -  {DateTime.Now:dddd, MMM d yyyy}{pagePart}", footFont, grey, Margin, y);
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

        // Which lines go on this page was settled by the caller against
        // RowsPerPage, so every line handed in here is drawn.
        foreach (var entry in entries)
        {
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
                // Through AppTime like the Time column beside it, so a club on
                // 12-hour time does not get "2:35 PM ... (started 14:41)" on
                // one row.
                what += $"   (started {AppTime.TimeOfDay(started.ToLocalTime())})";
            }

            g.DrawString(what, font, ink, colX[3], y + 6);

            y += RowHeight;
            g.DrawLine(linePen, Margin, y, PageWidth - Margin, y);
            y += RowStep - RowHeight;
        }

        if (continuedNote is not null)
        {
            g.DrawString(continuedNote, footFont, grey, Margin, y + 6);
        }

        g.DrawString("HeatCaller", footFont, grey, PageWidth - Margin - 90, PageHeight - Margin + 20);
    }
}
