using QRCoder;

namespace HeatCaller;

/// <summary>
/// A "scan this" poster for one of the app's web pages - the QR code, the
/// URL underneath it in plain text, and a line saying what it is.
///
/// Rendered as a bitmap rather than drawn straight onto a control, so the
/// same picture can go on screen, into a print job, or onto the Big Screen
/// without three near-identical bits of drawing code drifting apart.
///
/// The URL is printed in full below the code on purpose. Phone cameras fail
/// on glare, angle and cheap projectors, and someone who can't scan it can
/// still type it - which at a club night is the difference between joining
/// and giving up.
/// </summary>
public static class JoinPosterRenderer
{
    /// <param name="url">The address to encode. Empty produces a "server not running" card rather than a broken code.</param>
    /// <param name="heading">"LIVE RESULTS", "RACE REMOTE" - what they get by scanning.</param>
    /// <param name="subtitle">The one line of instruction underneath.</param>
    /// <param name="size">Overall square size in pixels. Print wants 1000+; a screen panel is happy at 300.</param>
    public static Bitmap Render(string url, string heading, string subtitle, int size = 640)
    {
        size = Math.Clamp(size, 220, 4000);

        var bitmap = new Bitmap(size, size);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Always on white, whatever theme the app is wearing. A QR code
        // needs contrast far more than it needs to match the UI, and a dark
        // code on a dark card is one a camera won't read.
        g.Clear(Color.White);

        var margin = size / 16f;
        var headingSize = Math.Max(11f, size / 26f);
        var urlSize = Math.Max(8f, size / 40f);
        var subSize = Math.Max(7f, size / 48f);

        using var headingFont = new Font(AppTheme.BoldFont.FontFamily, headingSize, FontStyle.Bold);
        using var urlFont = new Font(AppTheme.BodyFont.FontFamily, urlSize, FontStyle.Bold);
        using var subFont = new Font(AppTheme.BodyFont.FontFamily, subSize);
        using var black = new SolidBrush(Color.Black);
        using var grey = new SolidBrush(Color.FromArgb(110, 110, 110));
        using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        var headingRect = new RectangleF(margin, margin * 0.6f, size - margin * 2, headingSize * 2f);
        g.DrawString(heading, headingFont, black, headingRect, centred);

        // Laid out from the bottom up: the text blocks claim what they need
        // and the code takes whatever square is left, so a long URL shrinks
        // the code rather than overprinting it.
        var subRect = new RectangleF(margin, size - margin - subSize * 3f, size - margin * 2, subSize * 3f);
        var urlRect = new RectangleF(margin, subRect.Top - urlSize * 3f, size - margin * 2, urlSize * 3f);

        var codeTop = headingRect.Bottom + margin * 0.4f;
        var codeSize = Math.Max(40f, Math.Min(size - margin * 2, urlRect.Top - codeTop - margin * 0.4f));
        var codeRect = new RectangleF((size - codeSize) / 2f, codeTop, codeSize, codeSize);

        if (string.IsNullOrWhiteSpace(url))
        {
            using var borderPen = new Pen(Color.FromArgb(200, 200, 200), 2f);
            g.DrawRectangle(borderPen, codeRect.X, codeRect.Y, codeRect.Width, codeRect.Height);
            g.DrawString("Start the web server\nto get a code", subFont, grey, codeRect, centred);
        }
        else
        {
            try
            {
                // Q correction - a quarter of the code can be lost and it
                // still reads. Worth it on something that will be printed,
                // taped to a wall and photographed at an angle.
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);

                using var renderer = new PngByteQRCode(data);
                var png = renderer.GetGraphic(10);

                using var stream = new MemoryStream(png);
                using var code = Image.FromStream(stream);

                // NearestNeighbor: a QR code is a grid of hard squares, and
                // smoothing it blurs the module edges that a scanner is
                // looking for.
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(code, codeRect);
            }
            catch (Exception ex)
            {
                g.DrawString($"Couldn't build a code:\n{ex.Message}", subFont, grey, codeRect, centred);
            }
        }

        g.DrawString(string.IsNullOrWhiteSpace(url) ? "-" : url, urlFont, black, urlRect, centred);
        g.DrawString(subtitle, subFont, grey, subRect, centred);

        return bitmap;
    }
}
