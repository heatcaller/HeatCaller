namespace HeatCaller;

/// <summary>
/// A DRIVER'S REPORT CARD ON PAPER (22 Oct 2026). The lines CoachReport
/// works out, drawn on a Letter page black on white like the heat sheet and
/// the results sheet, so a driver can be handed it at the pit table or a
/// club can pin it up - and saved as a picture for a club that sends it on.
///
/// One page: a report card is six short lines, and a line that would run
/// off the foot is left off rather than squeezed (it never happens with
/// CoachReport's six, but a page must not draw under its own footer).
/// </summary>
internal static class ReportCardSheet
{
    // Letter at 150 DPI, the heat sheet's page.
    private const int PageWidth = 1275;
    private const int PageHeight = 1650;
    private const float PrintDpi = 150f;
    private const int Margin = 90;

    /// <summary>
    /// The page, drawn. The caller disposes the bitmap.
    /// </summary>
    /// <param name="driver">Who the card is for.</param>
    /// <param name="when">The night it covers, already worded - "Wed 22 Oct".</param>
    /// <param name="club">The club's name for the foot, or blank.</param>
    /// <param name="lines">CoachReport's lines.</param>
    internal static Bitmap Render(string driver, string when, string? club, IReadOnlyList<string> lines)
    {
        var bitmap = new Bitmap(PageWidth, PageHeight);

        try
        {
            // 96 DPI while drawing, so point sizes come out right, then
            // labelled as the 150 DPI page it is - see HeatSheetRenderer.
            bitmap.SetResolution(96, 96);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using var titleFont = new Font("Segoe UI", 26F, FontStyle.Bold);
                using var subFont = new Font("Segoe UI", 14F);
                using var bodyFont = new Font("Segoe UI", 15F);
                using var numberFont = new Font("Segoe UI", 15F, FontStyle.Bold);
                using var footFont = new Font("Segoe UI", 10F);
                using var rule = new Pen(Color.Black, 2);

                var width = PageWidth - Margin * 2;
                var y = Margin;

                TextRenderer.DrawText(g, "Report card", subFont, new Point(Margin, y), Color.DimGray, TextFormatFlags.NoPadding);
                y += TextRenderer.MeasureText(g, "Report card", subFont).Height + 6;

                var name = string.IsNullOrWhiteSpace(driver) ? "Driver" : driver.Trim();
                TextRenderer.DrawText(g, name, titleFont, new Rectangle(Margin, y, width, titleFont.Height + 8), Color.Black,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y += titleFont.Height + 10;

                TextRenderer.DrawText(g, when, subFont, new Point(Margin, y), Color.Black, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                y += subFont.Height + 18;

                g.DrawLine(rule, Margin, y, PageWidth - Margin, y);
                y += 34;

                var bottom = PageHeight - Margin - 40;
                const TextFormatFlags wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
                const int numberWidth = 50;

                if (lines.Count == 0)
                {
                    TextRenderer.DrawText(g, "Nothing to report - no laps were filed for this driver that night.", bodyFont,
                        new Rectangle(Margin, y, width, 200), Color.DimGray, wrap);
                }

                for (var i = 0; i < lines.Count; i++)
                {
                    var text = lines[i] ?? string.Empty;
                    var size = TextRenderer.MeasureText(g, text, bodyFont, new Size(width - numberWidth, int.MaxValue), wrap);

                    if (y + size.Height > bottom)
                    {
                        break;
                    }

                    TextRenderer.DrawText(g, $"{i + 1}.", numberFont, new Point(Margin, y), Color.Black, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, text, bodyFont, new Rectangle(Margin + numberWidth, y, width - numberWidth, size.Height), Color.Black, wrap);

                    y += size.Height + 28;
                }

                var foot = string.IsNullOrWhiteSpace(club)
                    ? "From the lap times HeatCaller read that night."
                    : $"{club!.Trim()} - from the lap times HeatCaller read that night.";

                TextRenderer.DrawText(g, foot, footFont, new Rectangle(Margin, PageHeight - Margin - 20, width, footFont.Height + 4), Color.DimGray,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                // The shared foot of every printed sheet, in the margin below.
                HeatCallerMark.DrawPrintFooter(g, Margin, PageWidth - Margin, PageHeight - Margin + 10, footFont);
            }

            bitmap.SetResolution(PrintDpi, PrintDpi);

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
