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
    private const int Margin = 70;

    /// <param name="accent">
    /// The class's colour. Printed as a band across the head of the sheet
    /// and a chip beside every round, so four classes' worth of paper on a
    /// pit table can be sorted by eye from across it. Everything else stays
    /// black on white - the colour is for finding the right sheet, not for
    /// reading off.
    /// </param>
    public static Bitmap Render(
        string className,
        IReadOnlyList<RoundDefinition> rounds,
        ProfileStore profiles,
        Color accent)
    {
        var bitmap = new Bitmap(PageWidth, PageHeight);
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
        // colours are chosen at a lightness that separates when desaturated.
        g.FillRectangle(accentBrush, Margin, y - 22, PageWidth - Margin * 2, 10);

        g.DrawString(className, titleFont, black, Margin, y);
        y += 40;
        g.DrawString($"Heat sheet  -  {DateTime.Now:dddd, MMM d yyyy}", footFont, grey, Margin, y);
        y += 28;
        g.DrawLine(heavyPen, Margin, y, PageWidth - Margin, y);
        y += 20;

        // Column layout: slot / driver / car / transponder, then two blank
        // hand-write columns for laps and time.
        int[] colX = { Margin, Margin + 70, Margin + 400, Margin + 660, Margin + 850, Margin + 1000 };
        string[] headers = { "#", "Driver", "Car", "Transponder", "Laps", "Time" };

        foreach (var round in rounds)
        {
            // Start a new page's worth of vertical space check - if this
            // round won't fit, stop rather than overflowing off the page.
            var neededHeight = 60 + round.AssignedTransponders.Count * 34 + 30;
            if (y + neededHeight > PageHeight - Margin)
            {
                g.DrawString("(continued on additional sheets - print one class at a time for full listings)", footFont, grey, Margin, y);
                break;
            }

            var trackPart = string.IsNullOrWhiteSpace(round.TrackLayoutName) ? "" : $"   -   {round.TrackLayoutName}";

            // A chip against each round, so a sheet stays identifiable
            // after it has been folded to the middle.
            g.FillRectangle(accentBrush, Margin, y + 4, 8, 20);

            g.DrawString($"{round.RoundLabel}{trackPart}", roundFont, black, Margin + 18, y);
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

        using var footerPen = new Pen(Color.FromArgb(200, 200, 200));
        g.DrawLine(footerPen, Margin, PageHeight - Margin, PageWidth - Margin, PageHeight - Margin);
        g.DrawString("HeatCaller", footFont, grey, Margin, PageHeight - Margin + 8);

        return bitmap;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "\u2026";
}
