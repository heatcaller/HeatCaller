using System.Drawing.Printing;

namespace HeatCaller;

/// <summary>
/// PRINTING PAGES THE APP HAS ALREADY DRAWN, WITH A PREVIEW (22 Oct 2026).
///
/// The heat sheet and the results sheet are both drawn as page-sized
/// bitmaps (HeatSheetRenderer, ResultsSheetRenderer) - black on white, made
/// to come out of a printer. Until now the only way to paper was to save
/// them as PNGs and print those from something else, and a race director
/// at the pit table with a queue behind him does not want a second program.
/// This is the one way they reach a printer: pick the printer, see every
/// page as it will come out, then print from the preview.
///
/// Each page is fitted inside the printer's margins at its own shape -
/// never stretched - and centred, so a Letter-shaped page lands on A4 with
/// a little more white at the foot rather than squashed.
/// </summary>
internal static class PagePrinting
{
    /// <summary>
    /// Where a page of <paramref name="page"/>'s shape goes inside
    /// <paramref name="room"/>: as big as fits, its shape kept, centred.
    /// Pure, so it is tested without a printer.
    /// </summary>
    internal static Rectangle FitInto(Size page, Rectangle room)
    {
        if (page.Width <= 0 || page.Height <= 0 || room.Width <= 0 || room.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var scale = Math.Min(room.Width / (double)page.Width, room.Height / (double)page.Height);
        // Rounded, then held inside the room: the limiting side comes out
        // exactly the room's, never a dot over it through rounding.
        var width = Math.Clamp((int)Math.Round(page.Width * scale), 1, room.Width);
        var height = Math.Clamp((int)Math.Round(page.Height * scale), 1, room.Height);

        return new Rectangle(
            room.Left + (room.Width - width) / 2,
            room.Top + (room.Height - height) / 2,
            width,
            height);
    }

    /// <summary>
    /// Asks for a printer, then shows the pages in a print preview, where
    /// Print sends them. The caller keeps ownership of the bitmaps and
    /// disposes them after this returns - the preview has finished with
    /// them by then.
    /// </summary>
    /// <returns>False when there was nothing to print, the printer was not chosen, or printing failed (said in a dialog).</returns>
    internal static bool PrintWithPreview(IWin32Window owner, string documentName, IReadOnlyList<Bitmap> pages)
    {
        if (pages.Count == 0)
        {
            return false;
        }

        try
        {
            using var document = new PrintDocument { DocumentName = documentName };
            var next = 0;

            // The preview and the real print each run through the pages
            // from the first, so the count starts again every time.
            document.BeginPrint += (_, _) => next = 0;

            document.PrintPage += (_, e) =>
            {
                if (e.Graphics is { } graphics && next < pages.Count)
                {
                    var page = pages[next];
                    var where = FitInto(page.Size, e.MarginBounds);

                    if (!where.IsEmpty)
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(page, where);
                    }
                }

                next++;
                e.HasMorePages = next < pages.Count;
            };

            using (var choose = new PrintDialog { Document = document, UseEXDialog = true, AllowSomePages = false })
            {
                if (choose.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }
            }

            using var preview = new PrintPreviewDialog
            {
                Document = document,
                UseAntiAlias = true,
                Text = $"Print preview - {documentName}",
                StartPosition = FormStartPosition.CenterParent,
            };

            // Most of the screen it opens on, so a page is readable without
            // zooming first.
            var screen = owner is Control control
                ? Screen.FromControl(control).WorkingArea
                : Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);

            preview.Size = new Size(screen.Width * 3 / 4, screen.Height * 9 / 10);
            preview.ShowDialog(owner);

            return true;
        }
        catch (Exception ex)
        {
            AppDialog.Show(owner, $"Couldn't print: {ex.Message}", "Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }
}
