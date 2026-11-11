namespace HeatCaller;

/// <summary>
/// Renders a printable heat sheet - the running order for a class, with a
/// grid slot for each driver and blank columns for writing results by hand
/// at the pit table. Deliberately plain black-on-white rather than themed:
/// this is meant to come out of a printer and be legible under shop
/// lighting, not to match the app's on-screen look.
/// </summary>
public static class HeatSheetRenderer
{
    // Letter paper at 150 DPI.
    private const int PageWidth = 1275;
    private const int PageHeight = 1650;
    private const float PrintDpi = 150f;
    private const int Margin = 70;

    // Where the first round starts on every page, below the class name, the
    // date line and the rule under them (Margin + 40 + 28 + 20). Each page
    // repeats that header, so every page starts its rounds at the same place.
    private const int FirstRoundTop = Margin + 40 + 28 + 20;

    /// <summary>
    /// Renders a class's rounds onto as many pages as they need, one bitmap
    /// per page, in order. The caller owns the bitmaps and disposes them.
    ///
    /// The sheet used to be a single page that stopped at the first round
    /// that did not fit, with a note telling the club to "print one class at
    /// a time" - which the program already does, so there was no way to get
    /// the rest. A class of 16 in two heats of 8 over three qualifying
    /// rounds is six rounds, and only four fit on a page, so the pit table
    /// never saw a sheet for the last two. Every round now lands on a page.
    /// </summary>
    /// <param name="accent">
    /// The class's color. Printed as a band across the head of each page
    /// and a chip beside every round, so four classes' worth of paper on a
    /// pit table can be sorted by eye from across it. Everything else stays
    /// black on white - the color is for finding the right sheet, not for
    /// reading off.
    /// </param>
    public static IReadOnlyList<Bitmap> RenderPages(
        string className,
        IReadOnlyList<RoundDefinition> rounds,
        ProfileStore profiles,
        Color accent,
        Func<RoundDefinition, string?>? startOf = null)
    {
        var plan = PlanPages(rounds.Select(r => r.AssignedTransponders.Count).ToList());
        var pages = new List<Bitmap>(plan.Count);

        try
        {
            var first = 0;

            for (var page = 0; page < plan.Count; page++)
            {
                var slice = rounds.Skip(first).Take(plan[page]).ToList();
                var note = page < plan.Count - 1 ? $"(continued on page {page + 2} of {plan.Count})" : null;

                pages.Add(RenderPage(className, slice, profiles, accent, page + 1, plan.Count, note, startOf));
                first += plan[page];
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
    /// The first page only - the shape the heat sheet export had before it
    /// could make more than one page. Kept so a caller that saves a single
    /// image still works; RenderPages is the one that prints every round.
    /// When rounds are left over, the note at the foot says how many were
    /// left off rather than pointing at a way to print them that does not
    /// exist.
    /// </summary>
    public static Bitmap Render(
        string className,
        IReadOnlyList<RoundDefinition> rounds,
        ProfileStore profiles,
        Color accent,
        Func<RoundDefinition, string?>? startOf = null)
    {
        var plan = PlanPages(rounds.Select(r => r.AssignedTransponders.Count).ToList());
        var onFirst = plan[0];
        var leftOver = rounds.Count - onFirst;

        string? note = leftOver switch
        {
            <= 0 => null,
            1 => "(1 more round did not fit and is not on this sheet)",
            _ => $"({leftOver} more rounds did not fit and are not on this sheet)",
        };

        return RenderPage(className, rounds.Take(onFirst).ToList(), profiles, accent, 1, 1, note, startOf);
    }

    /// <summary>
    /// How many rounds go on each page, given each round's number of drivers.
    /// Always at least one page (an empty class still gets its header), and
    /// always at least one round on a page, so a single round too tall for
    /// any page - forty drivers in one heat - still prints, clipped at the
    /// foot, instead of the paging never moving on.
    /// </summary>
    internal static IReadOnlyList<int> PlanPages(IReadOnlyList<int> driversPerRound)
    {
        var plan = new List<int>();
        var onThisPage = 0;
        var y = FirstRoundTop;

        foreach (var drivers in driversPerRound)
        {
            if (onThisPage > 0 && y + RoundRoomNeeded(drivers) > PageHeight - Margin)
            {
                plan.Add(onThisPage);
                onThisPage = 0;
                y = FirstRoundTop;
            }

            y += RoundHeight(drivers);
            onThisPage++;
        }

        if (onThisPage > 0 || plan.Count == 0)
        {
            plan.Add(onThisPage);
        }

        return plan;
    }

    // What a round takes on the page as RenderPage draws it: the label (28),
    // the column headings (20), the rule (6), a 34 px line per driver - or
    // one line for "(no drivers assigned)" - and 22 below.
    private static int RoundHeight(int drivers) => 28 + 20 + 6 + Math.Max(1, drivers) * 34 + 22;

    // The room asked for before a round is placed: the round itself plus a
    // little over, so the "continued" note below the last round on a page
    // has somewhere to sit above the footer rule. This is the old check's
    // 60 + 34n + 30, kept, with the empty round counted as the line it draws.
    private static int RoundRoomNeeded(int drivers) => 60 + Math.Max(1, drivers) * 34 + 30;

    private static Bitmap RenderPage(
        string className,
        IReadOnlyList<RoundDefinition> rounds,
        ProfileStore profiles,
        Color accent,
        int pageNumber,
        int pageCount,
        string? continuedNote,
        Func<RoundDefinition, string?>? startOf)
    {
        var bitmap = new Bitmap(PageWidth, PageHeight);

        try
        {
            DrawPage(bitmap, className, rounds, profiles, accent, pageNumber, pageCount, continuedNote, startOf);

            // The page is drawn at 96 DPI so the point-size fonts come out
            // the right size, but the pixels are a Letter page at 150 DPI.
            // The saved PNG carries whatever resolution the bitmap has, and
            // left at 96 it described a 13.3 x 17.2 inch page, so anything
            // that prints an image at its own size (Paint at 100%, for one)
            // cut off the Laps and Time columns and the lower rounds, or
            // spread the sheet over several pages. Setting it here, after
            // drawing, changes only that label - not a pixel.
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
        string className,
        IReadOnlyList<RoundDefinition> rounds,
        ProfileStore profiles,
        Color accent,
        int pageNumber,
        int pageCount,
        string? continuedNote,
        Func<RoundDefinition, string?>? startOf)
    {
        // 96 DPI, not the screen's: under --dpi-aware at 150% the point-size text would draw half as big again.
        bitmap.SetResolution(96, 96);

        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var titleFont = new Font("Segoe UI", 22F, FontStyle.Bold);
        using var roundFont = new Font("Segoe UI", 14F, FontStyle.Bold);
        using var headerFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 12F);
        using var footFont = new Font("Segoe UI", 9F);
        using var black = new SolidBrush(Color.Black);
        using var grey = new SolidBrush(Color.FromArgb(110, 110, 110));
        using var linePen = new Pen(Color.FromArgb(170, 170, 170));
        using var heavyPen = new Pen(Color.Black, 1.6f);

        using var accentBrush = new SolidBrush(accent);

        var y = Margin;

        // A solid band across the top. On a monochrome printer this comes
        // out as a grey bar, which still distinguishes the sheets - the
        // colors are chosen at a lightness that separates when desaturated.
        g.FillRectangle(accentBrush, Margin, y - 22, PageWidth - Margin * 2, 10);

        g.DrawString(className, titleFont, black, Margin, y);
        y += 40;

        // The page count goes on the date line, so a loose second page on
        // the pit table says which class it belongs to and that a first
        // page exists somewhere.
        var pagePart = pageCount > 1 ? $"  -  page {pageNumber} of {pageCount}" : "";
        g.DrawString($"Heat sheet  -  {DateTime.Now:dddd, MMM d yyyy}{pagePart}", footFont, grey, Margin, y);
        y += 28;
        g.DrawLine(heavyPen, Margin, y, PageWidth - Margin, y);
        y += 20;

        // Column layout: slot / driver / car / transponder, then two blank
        // hand-write columns for laps and time.
        int[] colX = { Margin, Margin + 70, Margin + 400, Margin + 660, Margin + 850, Margin + 1000 };
        string[] headers = { "#", "Driver", "Car", "Transponder", "Laps", "Time" };

        // Which rounds go on this page was settled by PlanPages, so every
        // round handed in here is drawn - the fitting is done there.
        foreach (var round in rounds)
        {
            var trackPart = string.IsNullOrWhiteSpace(round.TrackLayoutName) ? "" : $"   -   {round.TrackLayoutName}";

            // A chip against each round, so a sheet stays identifiable
            // after it has been folded to the middle.
            g.FillRectangle(accentBrush, Margin, y + 4, 8, 20);

            // A shared heat (11 Nov 2026, Phase 4.1) says both classes on
            // its title, and each driver's class in the car column, so the
            // paper shows who is scored against whom.
            var shared = MixedHeats.IsShared(round);
            var classPart = shared ? $"   ({MixedHeats.ClassLabel(round)})" : string.Empty;

            // When it should start (11 Nov 2026), from NightPlan: "about
            // 20:42", for a round still to run. A sheet printed again later
            // in the night has the times as they stand then.
            var aboutPart = RoundTitleTime(startOf?.Invoke(round));

            g.DrawString($"{round.RoundLabel}{classPart}{trackPart}{aboutPart}", roundFont, black, Margin + 18, y);
            y += 28;

            for (var i = 0; i < headers.Length; i++)
            {
                g.DrawString(headers[i], headerFont, grey, colX[i], y);
            }
            y += 20;
            g.DrawLine(linePen, Margin, y, PageWidth - Margin, y);
            y += 6;

            var slot = 1;
            foreach (var transponder in round.AssignedTransponders)
            {
                var profile = profiles.Get(transponder);
                var name = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName : "(unnamed)";
                var car = string.IsNullOrWhiteSpace(profile?.CarModel) ? "-" : profile!.CarModel;

                if (shared)
                {
                    car = $"{MixedHeats.ClassOfDriver(round, transponder, profiles)} - {car}";
                }

                g.DrawString(slot.ToString(), bodyFont, black, colX[0], y);
                g.DrawString(Truncate(name, 26), bodyFont, black, colX[1], y);
                g.DrawString(Truncate(car, 20), bodyFont, black, colX[2], y);
                g.DrawString(transponder, bodyFont, grey, colX[3], y);

                // Blank boxes for writing results in by hand.
                g.DrawLine(linePen, colX[4], y + 22, colX[4] + 120, y + 22);
                g.DrawLine(linePen, colX[5], y + 22, colX[5] + 145, y + 22);

                y += 34;
                slot++;
            }

            if (round.AssignedTransponders.Count == 0)
            {
                g.DrawString("(no drivers assigned)", bodyFont, grey, colX[1], y);
                y += 34;
            }

            y += 22;
        }

        if (continuedNote is not null)
        {
            g.DrawString(continuedNote, footFont, grey, Margin, y);
        }

        HeatCallerMark.DrawPrintFooter(g, Margin, PageWidth - Margin, PageHeight - Margin, footFont);
    }

    /// <summary>The estimated start as it follows a round's title: "   -   about 20:42", or nothing.</summary>
    internal static string RoundTitleTime(string? about) =>
        string.IsNullOrWhiteSpace(about) ? string.Empty : $"   -   {about.Trim()}";

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "\u2026";
}
