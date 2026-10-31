namespace HeatCaller;

/// <summary>One race on a printed results sheet: its heading, the line under it, and its results in finishing order.</summary>
public sealed record ResultsSheetSection(string Title, string Subtitle, IReadOnlyList<SessionResultEntry> Results);

/// <summary>
/// PRINTED RESULTS (22 Oct 2026). Race History's results drawn as Letter
/// pages for the pit board - one race after another, as many pages as they
/// take, black on white like the heat sheet (HeatSheetRenderer), because a
/// sheet pinned to a board under shop lights is read from a step back.
///
/// A lap count with laps counted by hand in it carries the mark and the
/// race's block says what the mark means (HandCountedLaps) - a result
/// pinned up for the drivers to check is exactly where a hand-counted lap
/// has to be visible.
/// </summary>
public static class ResultsSheetRenderer
{
    // Letter at 150 DPI, the heat sheet's page.
    private const int PageWidth = 1275;
    private const int PageHeight = 1650;
    private const float PrintDpi = 150f;
    private const int Margin = 70;

    // The heading every page repeats: the sheet's title and the rule under it.
    private const int SheetHeaderHeight = 40 + 28 + 20;
    private const int FirstTop = Margin + SheetHeaderHeight;

    // What one race's block takes, as DrawPage draws it.
    internal const int TitleHeight = 32;
    internal const int SubtitleHeight = 24;
    internal const int ColumnsHeight = 20 + 6;
    internal const int RowHeight = 30;
    internal const int NoteHeight = 26;
    internal const int GapBelow = 24;

    // The foot of the usable page: the page number sits below it.
    private const int Bottom = PageHeight - Margin - 20;

    /// <summary>
    /// A run of one race's rows on one page. <paramref name="Continued"/>
    /// is true on every part after a race's first, whose heading says so.
    /// </summary>
    internal readonly record struct Part(int Section, int FirstRow, int RowCount, bool Continued, bool LastOfSection);

    /// <summary>
    /// Which rows of which race go on which page, given each race's number
    /// of rows and whether it carries the hand-count note. A race starts on
    /// a fresh page when its whole block will not fit below the last one
    /// but would fit on a page of its own; a race too long for any page
    /// runs on, its heading repeated with "continued". Always at least one
    /// page, and every page holds at least one row of something, so the
    /// paging always moves on.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<Part>> PlanPages(IReadOnlyList<int> rowsPerSection, IReadOnlyList<bool> hasNote)
    {
        var pages = new List<IReadOnlyList<Part>>();
        var current = new List<Part>();
        var y = FirstTop;

        int Head() => TitleHeight + SubtitleHeight + ColumnsHeight;
        int Foot(int section) => (hasNote.Count > section && hasNote[section] ? NoteHeight : 0) + GapBelow;

        for (var section = 0; section < rowsPerSection.Count; section++)
        {
            var rows = Math.Max(0, rowsPerSection[section]);
            var whole = Head() + Math.Max(1, rows) * RowHeight + Foot(section);
            var fitsOnAPage = FirstTop + whole <= Bottom;

            // Start this race on a fresh page rather than split it, when a
            // fresh page would hold all of it.
            if (current.Count > 0 && y + whole > Bottom && fitsOnAPage)
            {
                pages.Add(current);
                current = new List<Part>();
                y = FirstTop;
            }

            var next = 0;
            var continued = false;

            while (true)
            {
                var room = Bottom - y - Head() - Foot(section);

                // Not even one row and its heading below what is already
                // on this page: on to the next.
                if (current.Count > 0 && room < RowHeight)
                {
                    pages.Add(current);
                    current = new List<Part>();
                    y = FirstTop;
                    continue;
                }

                var fit = Math.Max(1, room / RowHeight);
                var take = rows == 0 ? 0 : Math.Min(fit, rows - next);
                var last = next + take >= rows;

                current.Add(new Part(section, next, take, continued, last));
                y += Head() + Math.Max(1, take) * RowHeight + (last ? Foot(section) : 0);
                next += take;
                continued = true;

                if (last)
                {
                    break;
                }

                pages.Add(current);
                current = new List<Part>();
                y = FirstTop;
            }
        }

        if (current.Count > 0 || pages.Count == 0)
        {
            pages.Add(current);
        }

        return pages;
    }

    /// <summary>
    /// The sections as Letter pages, one bitmap a page, in order. The
    /// caller owns the bitmaps and disposes them.
    /// </summary>
    public static IReadOnlyList<Bitmap> RenderPages(string sheetTitle, IReadOnlyList<ResultsSheetSection> sections)
    {
        var plan = PlanPages(
            sections.Select(s => s.Results.Count).ToList(),
            sections.Select(s => s.Results.Any(r => HandCountedLaps.In(r) > 0)).ToList());

        var pages = new List<Bitmap>(plan.Count);

        try
        {
            for (var page = 0; page < plan.Count; page++)
            {
                var bitmap = new Bitmap(PageWidth, PageHeight);
                pages.Add(bitmap);
                DrawPage(bitmap, sheetTitle, sections, plan[page], page + 1, plan.Count);

                // As the heat sheet: drawn at 96 DPI so point sizes come out
                // right, labelled as the 150 DPI Letter page it is.
                bitmap.SetResolution(PrintDpi, PrintDpi);
            }
        }
        catch
        {
            foreach (var made in pages)
            {
                made.Dispose();
            }

            throw;
        }

        return pages;
    }

    /// <summary>"1:23.456" for a total time; "-" for none.</summary>
    internal static string TotalTime(long ms) =>
        ms <= 0 ? "-" : $"{ms / 60000}:{ms / 1000 % 60:00}.{ms % 1000:000}";

    /// <summary>What the Status column says: the outcome a correction set (DNF, DQ), or nothing for an ordinary finisher.</summary>
    internal static string StatusText(SessionResultEntry r) =>
        !string.IsNullOrWhiteSpace(r.Outcome) ? r.Outcome : string.Empty;

    private static void DrawPage(
        Bitmap bitmap,
        string sheetTitle,
        IReadOnlyList<ResultsSheetSection> sections,
        IReadOnlyList<Part> parts,
        int pageNumber,
        int pageCount)
    {
        // 96 DPI, not the screen's - see HeatSheetRenderer.DrawPage.
        bitmap.SetResolution(96, 96);

        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var titleFont = new Font("Segoe UI", 22F, FontStyle.Bold);
        using var raceFont = new Font("Segoe UI", 14F, FontStyle.Bold);
        using var headerFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 12F);
        using var footFont = new Font("Segoe UI", 9F);
        using var black = new SolidBrush(Color.Black);
        using var grey = new SolidBrush(Color.FromArgb(110, 110, 110));
        using var linePen = new Pen(Color.FromArgb(170, 170, 170));
        using var heavyPen = new Pen(Color.Black, 1.6f);
        using var clipped = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        var y = Margin;
        var width = PageWidth - Margin * 2;

        g.DrawString(sheetTitle, titleFont, black, new RectangleF(Margin, y, width, 40), clipped);
        y += 40;

        var pagePart = pageCount > 1 ? $"  -  page {pageNumber} of {pageCount}" : "";
        g.DrawString($"Results  -  printed {DateTime.Now:dddd, MMM d yyyy}{pagePart}", footFont, grey, Margin, y);
        y += 28;
        g.DrawLine(heavyPen, Margin, y, PageWidth - Margin, y);
        y += 20;

        // Pos / Driver / Car / Laps / Best lap / Total / Status.
        int[] colX = { Margin, Margin + 70, Margin + 410, Margin + 700, Margin + 790, Margin + 920, Margin + 1060 };
        string[] headers = { "Pos", "Driver", "Car", "Laps", "Best lap", "Total", "Status" };

        float ColumnWidth(int i) => (i + 1 < colX.Length ? colX[i + 1] : PageWidth - Margin) - colX[i] - 8;

        foreach (var part in parts)
        {
            var section = sections[part.Section];
            var title = part.Continued ? $"{section.Title}  (continued)" : section.Title;

            g.DrawString(title, raceFont, black, new RectangleF(Margin, y, width, TitleHeight), clipped);
            y += TitleHeight;
            g.DrawString(section.Subtitle, footFont, grey, new RectangleF(Margin, y, width, SubtitleHeight), clipped);
            y += SubtitleHeight;

            for (var i = 0; i < headers.Length; i++)
            {
                g.DrawString(headers[i], headerFont, grey, colX[i], y);
            }

            y += 20;
            g.DrawLine(linePen, Margin, y, PageWidth - Margin, y);
            y += 6;

            if (section.Results.Count == 0)
            {
                g.DrawString("(no results recorded)", bodyFont, grey, colX[1], y + 4);
                y += RowHeight;
            }

            foreach (var r in section.Results.Skip(part.FirstRow).Take(part.RowCount))
            {
                string[] cells =
                {
                    r.Position.ToString(),
                    r.DriverName,
                    string.IsNullOrWhiteSpace(r.CarModel) ? "-" : r.CarModel,
                    HandCountedLaps.LapsText(r.Laps, HandCountedLaps.In(r)),
                    r.FastLapMs > 0 ? $"{r.FastLapMs / 1000.0:0.000}s" : "-",
                    TotalTime(r.ElapsedMs),
                    StatusText(r),
                };

                for (var i = 0; i < cells.Length; i++)
                {
                    g.DrawString(cells[i], bodyFont, black, new RectangleF(colX[i], y + 4, ColumnWidth(i), RowHeight), clipped);
                }

                y += RowHeight;
                g.DrawLine(linePen, Margin, y, PageWidth - Margin, y);
            }

            if (part.LastOfSection)
            {
                if (section.Results.Any(r => HandCountedLaps.In(r) > 0))
                {
                    g.DrawString(HandCountedLaps.Legend, footFont, grey, Margin, y + 6);
                    y += NoteHeight;
                }

                y += GapBelow;
            }
        }

        // Under Bottom, which no row or note is drawn past.
        HeatCallerMark.DrawPrintFooter(g, Margin, PageWidth - Margin, PageHeight - Margin + 4, footFont);
    }
}
