using System.Drawing.Drawing2D;
using System.Drawing.Printing;

namespace HeatCaller;

/// <summary>
/// Prints a track plan TO SCALE, over as many sheets as it takes, and then
/// the parts list and the build list (Track Designer overhaul, Phase 7,
/// 25 Oct 2026).
///
/// Not through PagePrinting, which fits a ready-drawn page inside the
/// margins: fitting is exactly what a plan to scale must not do. Each sheet
/// here is drawn in millimetres (GraphicsUnit.Millimeter) - the plan as a
/// picture made at about 200 dots an inch for the exact part the sheet
/// shows, put down at its true size - so 1:20 on the paper is 1:20 with a
/// ruler. The scale bar on every sheet is drawn from the same number, and
/// says how long it should measure, so a printer set to shrink to fit is
/// caught before anybody marks out a hall from it.
///
/// Each sheet has the grid's letters along its top and numbers down its
/// side - the same A1 the build list names its tiles by (RcpBuildList).
/// </summary>
internal static class RcpPlanPrinter
{
    /// <summary>What to print.</summary>
    /// <param name="Ratio">The N of 1:N, or 0 to fit the plan on one sheet.</param>
    /// <param name="Parts">Whether the parts list and build list follow the plan.</param>
    /// <param name="Stock">What the club owns, to print the parts against; null for none.</param>
    internal sealed record Job(RcpTrackDesign Design, string Name, int Ratio, bool Parts, RcpStock? Stock);

    /// <summary>The printer's margins asked for, in hundredths of an inch: half an inch all round.</summary>
    private const int MarginHundredths = 50;

    /// <summary>The band across the top of a sheet for what it is, in mm.</summary>
    private const float HeaderMm = 14f;

    /// <summary>The band across the foot of a plan sheet for the scale bar, in mm.</summary>
    private const float FooterMm = 13f;

    /// <summary>The strip along the top and left of the drawing for the grid's letters and numbers, in mm.</summary>
    private const float GutterMm = 5f;

    /// <summary>How finely each sheet's picture is made: 8 dots a millimetre, about 200 an inch.</summary>
    private const float DotsPerMm = 8f;

    /// <summary>A US Letter sheet, for when there is no printer to ask.</summary>
    private static readonly SizeF LetterMm = new(215.9f, 279.4f);

    /// <summary>
    /// The space a sheet's drawing has, in mm, on <paramref name="settings"/>'
    /// paper and margins - the margins less the header, footer and grid
    /// strip. Pure arithmetic over the numbers given.
    /// </summary>
    internal static SizeF DrawingBoxMm(SizeF paperMm, bool landscape, float marginMm)
    {
        var width = landscape ? Math.Max(paperMm.Width, paperMm.Height) : Math.Min(paperMm.Width, paperMm.Height);
        var height = landscape ? Math.Min(paperMm.Width, paperMm.Height) : Math.Max(paperMm.Width, paperMm.Height);

        return new SizeF(
            Math.Max(0, width - marginMm * 2 - GutterMm),
            Math.Max(0, height - marginMm * 2 - HeaderMm - FooterMm - GutterMm));
    }

    /// <summary>Whether the plan prints best across the paper: when it is wider than it is deep.</summary>
    internal static bool Landscape(RcpTrackDesign design)
    {
        var area = RcpPrintLayout.Area(design);

        return area.Width > area.Height;
    }

    /// <summary>
    /// The default printer's paper, in mm, and its name - or US Letter when
    /// there is no printer to ask (none installed, or the spooler stopped).
    /// </summary>
    internal static (SizeF PaperMm, string PaperName) DefaultPaper()
    {
        try
        {
            using var document = new PrintDocument();
            var paper = document.DefaultPageSettings.PaperSize;

            if (paper.Width > 0 && paper.Height > 0)
            {
                return (new SizeF(paper.Width * 0.254f, paper.Height * 0.254f), paper.PaperName);
            }
        }
        catch (Exception)
        {
            // InvalidPrinterException and friends: no printer to ask.
        }

        return (LetterMm, "Letter");
    }

    /// <summary>The margins asked for, in mm.</summary>
    internal static float MarginMm => MarginHundredths * 0.254f;

    /// <summary>
    /// Asks for the printer, then shows every sheet in a print preview,
    /// where Print sends them. Says in a dialog when it can't.
    /// </summary>
    /// <returns>False when nothing was printed: nothing to print, no printer chosen, too many sheets, or a failure.</returns>
    internal static bool Print(IWin32Window owner, Job job)
    {
        var area = RcpPrintLayout.Area(job.Design);

        if (area.IsEmpty)
        {
            return false;
        }

        var origin = RcpBuildList.Origin(job.Design);
        var tileMm = job.Design.TileSizeMm;
        var title = string.IsNullOrWhiteSpace(job.Name) ? "Track plan" : job.Name.Trim();

        try
        {
            using var document = new PrintDocument { DocumentName = $"{title} - track plan" };

            document.DefaultPageSettings.Landscape = Landscape(job.Design);
            document.DefaultPageSettings.Margins = new Margins(MarginHundredths, MarginHundredths, MarginHundredths, MarginHundredths);

            using (var choose = new PrintDialog { Document = document, UseEXDialog = true, AllowSomePages = false })
            {
                if (choose.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }
            }

            // The sheets on the paper chosen, worked out before the preview
            // opens, so a scale that won't go is said straight away rather
            // than shown as an empty preview (review, 25 Oct 2026).
            (double MmPerTile, int Across, int Down, IReadOnlyList<RcpPrintSheet> Sheets) Work()
            {
                var settings = document.DefaultPageSettings;
                var paper = new SizeF(settings.PaperSize.Width * 0.254f, settings.PaperSize.Height * 0.254f);
                var margins = settings.Margins;
                var marginMm = Math.Max(Math.Max(margins.Left, margins.Right), Math.Max(margins.Top, margins.Bottom)) * 0.254f;
                var box = DrawingBoxMm(paper, settings.Landscape, marginMm);
                var mm = job.Ratio > 0 ? RcpPrintLayout.MmPerTile(tileMm, job.Ratio) : RcpPrintLayout.FitMmPerTile(area, box);
                var (a, d) = RcpPrintLayout.Count(area, mm, box);

                return (mm, a, d, RcpPrintLayout.Sheets(area, mm, box));
            }

            var work = Work();

            if (work.Sheets.Count == 0)
            {
                AppDialog.Show(owner,
                    work.MmPerTile > 0
                        ? $"At that scale on that paper the plan takes more than {RcpPrintLayout.MostSheets} sheets. Choose a smaller scale - 1:50, or fit on one sheet."
                        : "That paper leaves no room to draw the plan inside its margins. Choose a bigger paper size.",
                    "Print", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            var next = 0;
            var text = new Queue<PrintLine>();
            Exception? failed = null;

            // Again at the start of every run through the pages - the
            // preview's and then the printer's.
            document.BeginPrint += (_, e) =>
            {
                work = Work();

                if (work.Sheets.Count == 0 || failed is not null)
                {
                    e.Cancel = true;
                    return;
                }

                next = 0;
                text = new Queue<PrintLine>(job.Parts ? PartsLines(job, title) : Array.Empty<PrintLine>());
            };

            // The pages are drawn inside the preview's own message loop, where
            // a failure would never reach the catch below: it is kept, the
            // run stops, and it is said once the preview closes.
            document.PrintPage += (_, e) =>
            {
                try
                {
                    if (e.Graphics is not { } g)
                    {
                        e.HasMorePages = false;
                        return;
                    }

                    var page = PageMm(e, document);

                    if (next < work.Sheets.Count)
                    {
                        // Half as fine in the preview, which keeps every
                        // sheet in memory at once; full on the paper.
                        var dots = document.PrintController.IsPreview ? DotsPerMm / 2 : DotsPerMm;

                        DrawSheet(g, page, job.Design, work.Sheets[next], work.Sheets.Count, work.Across, work.Down,
                            work.MmPerTile, tileMm, origin, title, dots);
                        next++;
                        e.HasMorePages = next < work.Sheets.Count || text.Count > 0;
                        return;
                    }

                    DrawText(g, page, text);
                    e.HasMorePages = text.Count > 0;
                }
                catch (Exception ex)
                {
                    failed ??= ex;
                    e.HasMorePages = false;
                }
            };

            using var preview = new PrintPreviewDialog
            {
                Document = document,
                UseAntiAlias = true,
                Text = $"Print preview - {title}",
                StartPosition = FormStartPosition.CenterParent,
            };

            var screen = owner is Control control
                ? Screen.FromControl(control).WorkingArea
                : Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);

            preview.Size = new Size(screen.Width * 3 / 4, screen.Height * 9 / 10);
            preview.ShowDialog(owner);

            if (failed is not null)
            {
                AppDialog.Show(owner, $"Couldn't print: {failed.Message}", "Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AppDialog.Show(owner, $"Couldn't print: {ex.Message}", "Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    /// <summary>
    /// The margins of the sheet being printed, in mm, in the drawing's own
    /// terms. A real printer's drawing starts at the edge of what it can
    /// print (its hard margin), not the paper's edge, so that is taken off;
    /// the preview draws from the paper's edge, so it isn't.
    /// </summary>
    private static RectangleF PageMm(PrintPageEventArgs e, PrintDocument document)
    {
        var bounds = e.MarginBounds;
        float shiftX = 0, shiftY = 0;

        if (!document.PrintController.IsPreview)
        {
            shiftX = e.PageSettings.HardMarginX;
            shiftY = e.PageSettings.HardMarginY;
        }

        // Everything on a sheet is drawn in millimetres from here on.
        e.Graphics!.PageUnit = GraphicsUnit.Millimeter;
        e.Graphics.PageScale = 1f;

        return new RectangleF(
            (bounds.Left - shiftX) * 0.254f,
            (bounds.Top - shiftY) * 0.254f,
            bounds.Width * 0.254f,
            bounds.Height * 0.254f);
    }

    /// <summary>One sheet of the plan, at its true size, with what it is above and the scale bar below.</summary>
    private static void DrawSheet(Graphics g, RectangleF page, RcpTrackDesign design, RcpPrintSheet sheet, int count,
        int across, int down, double mmPerTile, double tileMm, (int Col, int Row) origin, string title, float dotsPerMm)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var titleFont = Type(12f, FontStyle.Bold);
        using var font = Type(8.5f, FontStyle.Regular);
        using var small = Type(7f, FontStyle.Regular);
        using var ink = new SolidBrush(Color.Black);
        using var grey = new SolidBrush(Color.FromArgb(90, 90, 90));

        // What it is.
        var where = count == 1
            ? "the whole plan on one sheet"
            : $"sheet {sheet.Number} of {count} - row {sheet.Row + 1} of {down}, {Ordinal(sheet.Column + 1)} from the left of {across}";

        g.DrawString(title, titleFont, ink, new RectangleF(page.Left, page.Top, page.Width, 7f));
        g.DrawString(
            $"Track plan, {where}. Scale {RcpPrintLayout.RatioText(tileMm, mmPerTile)}: one {tileMm / 10:0} cm tile is {mmPerTile:0.#} mm.",
            font, grey, new RectangleF(page.Left, page.Top + 7f, page.Width, HeaderMm - 7f));

        // The drawing, at its true size.
        var box = new RectangleF(
            page.Left + GutterMm,
            page.Top + HeaderMm + GutterMm,
            (float)(sheet.Tiles.Width * mmPerTile),
            (float)(sheet.Tiles.Height * mmPerTile));

        var dotsPerTile = (float)(mmPerTile * dotsPerMm);

        using (var picture = RcpTrackPainter.RenderRegion(design, sheet.Tiles, dotsPerTile, Color.White, opaque: true))
        {
            var interpolation = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // The picture is whole pixels and the box is exact: drawn into
            // the picture's own share of the box, so its scale is the box's.
            g.DrawImage(picture, new RectangleF(box.Left, box.Top, picture.Width / dotsPerMm, picture.Height / dotsPerMm),
                new RectangleF(0, 0, picture.Width, picture.Height), GraphicsUnit.Pixel);

            g.InterpolationMode = interpolation;
        }

        using (var edge = new Pen(Color.FromArgb(150, 150, 150), 0.2f))
        {
            g.DrawRectangle(edge, box.Left, box.Top, box.Width, box.Height);
        }

        DrawGridNames(g, box, sheet.Tiles, mmPerTile, origin, small, grey);

        // The scale bar, and how long it should measure.
        var footTop = box.Bottom + 3f;
        var (metres, paperMm) = RcpPrintLayout.ScaleBar(mmPerTile, tileMm, Math.Min(100, page.Width / 2));

        if (paperMm > 0)
        {
            var bar = new RectangleF(page.Left + GutterMm, footTop, (float)paperMm, 2.5f);
            var halves = 4;

            for (var i = 0; i < halves; i++)
            {
                var part = new RectangleF(bar.Left + bar.Width * i / halves, bar.Top, bar.Width / halves, bar.Height);

                if (i % 2 == 0)
                {
                    g.FillRectangle(ink, part);
                }
            }

            using (var outline = new Pen(Color.Black, 0.25f))
            {
                g.DrawRectangle(outline, bar.Left, bar.Top, bar.Width, bar.Height);
            }

            var length = metres < 1 ? $"{metres * 100:0} cm" : $"{metres:0.#} m";

            g.DrawString(
                $"{length} on the floor. This bar should measure {paperMm:0} mm - if it doesn't, the printer has scaled the page: turn off \"fit to page\".",
                small, grey, new RectangleF(bar.Right + 3f, bar.Top - 0.8f, Math.Max(10f, page.Right - bar.Right - 3f), FooterMm - 2f));
        }
    }

    /// <summary>The grid's column letters above the drawing and row numbers beside it - spaced out when the tiles are too small to name every one.</summary>
    private static void DrawGridNames(Graphics g, RectangleF box, RectangleF tiles, double mmPerTile, (int Col, int Row) origin, Font font, Brush brush)
    {
        // At least 6 mm between names.
        var every = Math.Max(1, (int)Math.Ceiling(6 / mmPerTile));
        using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (var col = (int)Math.Floor(tiles.Left); col < tiles.Right; col++)
        {
            var middle = col + 0.5f;

            if (col < origin.Col || middle < tiles.Left || middle > tiles.Right || (col - origin.Col) % every != 0)
            {
                continue;
            }

            var x = box.Left + (float)((middle - tiles.Left) * mmPerTile);
            g.DrawString(RcpPrintLayout.ColumnName(col - origin.Col), font, brush, new RectangleF(x - 5, box.Top - GutterMm, 10, GutterMm), centred);
        }

        for (var row = (int)Math.Floor(tiles.Top); row < tiles.Bottom; row++)
        {
            var middle = row + 0.5f;

            if (row < origin.Row || middle < tiles.Top || middle > tiles.Bottom || (row - origin.Row) % every != 0)
            {
                continue;
            }

            var y = box.Top + (float)((middle - tiles.Top) * mmPerTile);
            g.DrawString((row - origin.Row + 1).ToString(), font, brush, new RectangleF(box.Left - GutterMm, y - 3, GutterMm, 6), centred);
        }
    }

    /// <summary>
    /// The sheets' type, sized in millimetres as everything on them is: the
    /// drawing's world units are millimetres (PageMm), and a font in World
    /// units goes through the same mapping as the lines - so it comes out
    /// at <paramref name="points"/> on the paper however the page is mapped.
    /// </summary>
    private static Font Type(float points, FontStyle style) =>
        new("Segoe UI", points * 25.4f / 72f, style, GraphicsUnit.World);

    internal static string Ordinal(int n) => (n % 100) is 11 or 12 or 13
        ? $"{n}th"
        : (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        };

    /// <summary>A line of the parts and build pages: a heading, a line of text, or a gap.</summary>
    internal sealed record PrintLine(string Text, bool Heading = false, bool Title = false, bool Short = false);

    /// <summary>The parts list against what the club owns, then how to build it - line by line, for the pages after the plan.</summary>
    internal static IReadOnlyList<PrintLine> PartsLines(Job job, string title)
    {
        var lines = new List<PrintLine> { new($"{title} - parts and how to build it", Title: true) };
        var used = RcpPlan.Count(job.Design);
        var tileMm = job.Design.TileSizeMm;
        var check = job.Stock?.Check(used, tileMm) ?? Array.Empty<RcpStockLine>();

        lines.Add(new PrintLine($"Parts, in {tileMm / 10:0} cm tiles", Heading: true));

        if (check.Count > 0)
        {
            foreach (var line in check)
            {
                lines.Add(new PrintLine(
                    !line.Known
                        ? $"{line.Part}: {line.Used} used, {line.Owned} counted"
                        : line.Short
                            ? $"{line.Part}: {line.Used} needed, {line.Owned} owned - {-line.Left} short"
                            : $"{line.Part}: {line.Used} needed, {line.Owned} owned, {line.Left} left over",
                    Short: line.Short));
            }
        }
        else
        {
            lines.Add(new PrintLine($"Plain tiles: {used.PlainTiles}"));
            lines.Add(new PrintLine($"Outer curve tiles: {used.OuterCurveTiles}"));
            lines.Add(new PrintLine($"Inner corner tiles: {used.InnerCornerTiles}"));
            lines.Add(new PrintLine($"Straight rails: {used.StraightRails}" + (used.Dividers > 0 ? $" ({used.Dividers} dividers, two rails each)" : string.Empty)));
            lines.Add(new PrintLine($"Turn rails: {used.OuterTurnRails}"));
            lines.Add(new PrintLine($"Inner corner pieces: {used.InnerCornerPieces}"));
            lines.Add(new PrintLine($"End caps: {used.EndCaps}"));
            lines.Add(new PrintLine("What the club owns isn't entered, so nothing is checked against it - the Stock tab says what's in the cupboard."));
        }

        if (used.TwoTileTurns > 0)
        {
            lines.Add(new PrintLine($"{Plural.Of(used.TwoTileTurns, "two-tile turn")} counted as one outer curve tile and one turn rail each - RCP's boxes don't say what one takes."));
        }

        foreach (var step in RcpBuildList.For(job.Design))
        {
            lines.Add(new PrintLine(string.Empty));
            lines.Add(new PrintLine(step.Heading, Heading: true));

            foreach (var text in step.Lines)
            {
                lines.Add(new PrintLine(text));
            }
        }

        return lines;
    }

    /// <summary>As many of <paramref name="lines"/> as fit on the page, taken off the queue.</summary>
    private static void DrawText(Graphics g, RectangleF page, Queue<PrintLine> lines)
    {
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var titleFont = Type(13f, FontStyle.Bold);
        using var heading = Type(10.5f, FontStyle.Bold);
        using var body = Type(9.5f, FontStyle.Regular);
        using var ink = new SolidBrush(Color.Black);
        using var red = new SolidBrush(Color.FromArgb(180, 20, 20));

        var y = page.Top;
        var first = true;

        while (lines.Count > 0)
        {
            var line = lines.Peek();
            var font = line.Title ? titleFont : line.Heading ? heading : body;
            var text = line.Text.Length == 0 ? " " : line.Text;
            var height = g.MeasureString(text, font, new SizeF(Math.Max(1f, page.Width), 10000f)).Height + (line.Title ? 3f : 1f);

            // Always one line on a page, however tall, so a page is never
            // printed empty; and a heading never left alone at the foot.
            var keepWithNext = line.Heading ? height * 2 : height;

            if (!first && y + keepWithNext > page.Bottom)
            {
                return;
            }

            // A gap at the top of a page is no use - and isn't something
            // drawn, so the page still takes the line after it whatever its
            // size, and never comes out blank.
            lines.Dequeue();

            if (first && line.Text.Length == 0)
            {
                continue;
            }

            g.DrawString(text, font, line.Short ? red : ink, new RectangleF(page.Left, y, page.Width, height + 2));
            y += height;
            first = false;
        }
    }
}
