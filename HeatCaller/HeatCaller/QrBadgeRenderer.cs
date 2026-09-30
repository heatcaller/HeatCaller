using QRCoder;

namespace HeatCaller;

/// <summary>
/// Renders printable sheets of QR check-in badges - one badge per driver,
/// encoding that driver's transponder ID. Scanning a badge with an
/// ordinary USB barcode/QR scanner checks that driver in.
///
/// The QR payload is prefixed (see BadgePrefix) rather than being a bare
/// transponder ID, so the scan handler can tell a real badge apart from
/// someone accidentally scanning an unrelated barcode - a product label,
/// a shipping box, whatever else is lying around a shop counter.
/// </summary>
public static class QrBadgeRenderer
{
    /// <summary>Marker that identifies a scan as one of this app's badges.</summary>
    public const string BadgePrefix = "MZRD:";

    // Letter paper at 150 DPI - high enough to print crisply, small enough
    // to stay a manageable file size.
    private const int PageWidth = 1275;
    private const int PageHeight = 1650;
    private const int Columns = 3;
    private const int Rows = 4;
    public const int BadgesPerPage = Columns * Rows;

    private const int MarginX = 60;
    private const int MarginY = 60;

    /// <summary>
    /// The online check-in link for a transponder, or null when online
    /// check-in is off. Set once by MainForm, which owns the settings;
    /// null here simply means badges carry the old MZRD: form.
    /// </summary>
    public static Func<string, string?>? OnlineBadgeUrl { get; set; }

    /// <summary>
    /// Maps an online id back to its transponder. Set by MainForm and
    /// safe from any thread - the LAN check-in page calls ParsePayload on a
    /// request thread.
    /// </summary>
    public static Func<string, string?>? ResolveOnlineId { get; set; }

    /// <summary>
    /// What a driver's badge encodes.
    ///
    /// With online check-in on, a web link: the camera on any phone,
    /// iPhones included, opens a link from a QR code with no app and no
    /// setup, which the MZRD: form could never do. With it off, the old
    /// form - a link to a check-in page that is not open would only
    /// confuse whoever scanned it.
    /// </summary>
    public static string BuildPayload(string transponder) =>
        OnlineBadgeUrl?.Invoke(transponder) ?? $"{BadgePrefix}{transponder}";

    /// <summary>
    /// Extracts the transponder from a scanned string, or null if it isn't
    /// one of our badges. Reads both forms, so the USB scanner at the desk
    /// and the LAN check-in page keep working with every badge ever
    /// printed, old or new.
    /// </summary>
    public static string? ParsePayload(string scanned)
    {
        var trimmed = scanned.Trim();

        if (trimmed.StartsWith(BadgePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[BadgePrefix.Length..].Trim();
        }

        var id = RelayCheckIn.IdFromScan(trimmed);

        return id is null ? null : ResolveOnlineId?.Invoke(id);
    }

    /// <summary>
    /// Renders one page of badges. Caller is responsible for slicing the
    /// driver list into BadgesPerPage-sized chunks and disposing each
    /// returned bitmap.
    /// </summary>
    public static Bitmap RenderPage(IReadOnlyList<DriverProfile> driversOnThisPage)
    {
        var bitmap = new Bitmap(PageWidth, PageHeight);

        // 96 DPI, not the screen's: under --dpi-aware at 150% the point-size text would draw half as big again.
        bitmap.SetResolution(96, 96);

        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var cellWidth = (PageWidth - MarginX * 2) / Columns;
        var cellHeight = (PageHeight - MarginY * 2) / Rows;

        using var nameFont = new Font(AppTheme.BoldFont.FontFamily, 15F, FontStyle.Bold);
        using var detailFont = new Font(AppTheme.BodyFont.FontFamily, 10F);
        using var blackBrush = new SolidBrush(Color.Black);
        using var greyBrush = new SolidBrush(Color.FromArgb(110, 110, 110));
        using var cutPen = new Pen(Color.FromArgb(210, 210, 210)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };

        using var generator = new QRCodeGenerator();

        for (var i = 0; i < driversOnThisPage.Count && i < BadgesPerPage; i++)
        {
            var driver = driversOnThisPage[i];
            var col = i % Columns;
            var row = i / Columns;
            var cellX = MarginX + col * cellWidth;
            var cellY = MarginY + row * cellHeight;

            g.DrawRectangle(cutPen, cellX, cellY, cellWidth, cellHeight);

            // QR code, centered in the upper portion of the cell.
            var qrSize = Math.Min(cellWidth, cellHeight) - 110;
            try
            {
                // Both disposed: QRCoder's data and renderer are IDisposable,
                // and a sheet makes a dozen of each per page.
                using var qrData = generator.CreateQrCode(BuildPayload(driver.Transponder), QRCodeGenerator.ECCLevel.Q);
                using var renderer = new PngByteQRCode(qrData);
                var pngBytes = renderer.GetGraphic(10);
                using var stream = new MemoryStream(pngBytes);
                using var qrImage = Image.FromStream(stream);
                g.DrawImage(qrImage, cellX + (cellWidth - qrSize) / 2, cellY + 20, qrSize, qrSize);
            }
            catch
            {
                // If a single QR fails to generate, leave that cell's code
                // blank rather than losing the whole sheet - the name and
                // transponder printed below still identify the driver.
            }

            var textY = cellY + 20 + qrSize + 8;
            var name = string.IsNullOrWhiteSpace(driver.DriverName) ? driver.Transponder : driver.DriverName;
            DrawCentered(g, name, nameFont, blackBrush, cellX, textY, cellWidth);

            var detail = string.IsNullOrWhiteSpace(driver.ClassName)
                ? driver.Transponder
                : $"{driver.ClassName}  -  {driver.Transponder}";
            DrawCentered(g, detail, detailFont, greyBrush, cellX, textY + 26, cellWidth);

            // Said on the badge itself, because nobody would guess that the
            // phone in their pocket is all it takes.
            if (OnlineBadgeUrl?.Invoke(driver.Transponder) is not null)
            {
                DrawCentered(g, "Scan with your phone camera to check in", detailFont, greyBrush, cellX, textY + 48, cellWidth);
            }
        }

        return bitmap;
    }

    private static void DrawCentered(Graphics g, string text, Font font, Brush brush, int cellX, int y, int cellWidth)
    {
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, cellX + (cellWidth - size.Width) / 2, y);
    }
}
