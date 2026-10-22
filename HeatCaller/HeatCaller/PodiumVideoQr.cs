using System.Drawing.Drawing2D;
using System.Drawing.Text;
using QRCoder;

namespace HeatCaller;

/// <summary>
/// THE PODIUM'S VIDEOS, ON THE BIG SCREEN (21 Oct 2026).
///
/// A strip down the right of the board after a race: a QR code to the
/// race's page on the club's website, where the top three can watch their
/// highlight video and download it (RelayPublisher.Videos, and the relay's
/// videos.ts). Shown once the first of them is up, and gone the moment the
/// next race stages - a code on the screen during a race is a code nobody
/// is looking at, over a board everybody is.
///
/// Only the podium's videos are ever on that page; nothing here can put
/// anything else there.
/// </summary>
internal sealed class PodiumVideoQr : Control
{
    private Bitmap? _code;
    private string? _url;
    private string _label = string.Empty;
    private Control? _watchedParent;

    public PodiumVideoQr()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        Visible = false;
    }

    /// <summary>The page now on the screen, or null.</summary>
    public string? Url => _url;

    /// <summary>Puts the code up for a race's page. The same page again only refreshes its label.</summary>
    public void ShowCode(string url, string label)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!string.Equals(url, _url, StringComparison.Ordinal))
        {
            var code = Render(url);

            if (code is null)
            {
                return;
            }

            _code?.Dispose();
            _code = code;
            _url = url;
        }

        _label = label ?? string.Empty;

        WatchParent();
        FitWidth();
        // Not BringToFront: docking goes by z-order, and in front of the
        // board it would be docked after the board had filled everything.
        Visible = true;
        Invalidate();
    }

    /// <summary>Takes the code down - the next race is on its way.</summary>
    public void HideCode()
    {
        if (!Visible && _url is null)
        {
            return;
        }

        Visible = false;
        _url = null;
        _code?.Dispose();
        _code = null;
    }

    /// <summary>A third of the board, never less than a phone can scan from the back of a room.</summary>
    private void FitWidth()
    {
        if (Parent is not { } parent)
        {
            return;
        }

        var width = Math.Clamp(parent.ClientSize.Width / 3, 260, 720);

        if (Width != width)
        {
            Width = width;
        }
    }

    private void WatchParent()
    {
        if (ReferenceEquals(_watchedParent, Parent))
        {
            return;
        }

        if (_watchedParent is not null)
        {
            _watchedParent.Resize -= OnParentResize;
        }

        _watchedParent = Parent;

        if (_watchedParent is not null)
        {
            _watchedParent.Resize += OnParentResize;
        }
    }

    private void OnParentResize(object? sender, EventArgs e)
    {
        if (Visible)
        {
            FitWidth();
        }
    }

    /// <summary>The code as a plain black-on-white bitmap - its own copy, so no stream has to outlive it.</summary>
    private static Bitmap? Render(string url)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            using var renderer = new PngByteQRCode(data);
            using var stream = new MemoryStream(renderer.GetGraphic(20));
            using var image = Image.FromStream(stream);

            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var background = Parent?.BackColor ?? Color.FromArgb(14, 17, 22);
        g.Clear(background);

        if (_code is null)
        {
            return;
        }

        var pad = Math.Max(16, Width / 18);
        var card = new Rectangle(pad / 2, pad, Width - pad, Height - pad * 2);

        if (card.Width < 80 || card.Height < 120)
        {
            return;
        }

        using (var path = Rounded(card, Math.Max(12, card.Width / 16)))
        using (var fill = new SolidBrush(Color.FromArgb(28, 33, 42)))
        using (var edge = new Pen(Color.FromArgb(255, 196, 0), 3f))
        {
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }

        var inner = Rectangle.Inflate(card, -pad, -pad);
        var y = inner.Top;

        using var headFont = new Font("Segoe UI", Math.Max(14f, inner.Width / 13f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelFont = new Font("Segoe UI", Math.Max(12f, inner.Width / 17f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var footFont = new Font("Segoe UI", Math.Max(11f, inner.Width / 22f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var gold = new SolidBrush(Color.FromArgb(255, 196, 0));
        using var white = new SolidBrush(Color.White);
        using var muted = new SolidBrush(Color.FromArgb(170, 178, 190));
        using var centre = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

        y += Line(g, "PODIUM VIDEOS", headFont, gold, inner, y, centre);
        y += Line(g, _label.Length == 0 ? "Watch & download" : $"{_label} · watch & download", labelFont, white, inner, y, centre);
        y += pad / 2;

        var foot = (int)Math.Ceiling(footFont.GetHeight(g) * 2.4f);
        var side = Math.Min(inner.Width, inner.Bottom - y - foot);

        if (side >= 60)
        {
            var plate = new Rectangle(inner.Left + (inner.Width - side) / 2, y, side, side);
            g.FillRectangle(Brushes.White, plate);

            var quiet = Math.Max(6, side / 14);
            var qr = Rectangle.Inflate(plate, -quiet, -quiet);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_code, qr);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            y = plate.Bottom + pad / 2;
        }

        y += Line(g, "Scan with your phone camera", footFont, muted, inner, y, centre);
        Line(g, "1st, 2nd & 3rd · kept for a week", footFont, muted, inner, y, centre);
    }

    private static int Line(Graphics g, string text, Font font, Brush brush, Rectangle inner, int y, StringFormat format)
    {
        var height = (int)Math.Ceiling(font.GetHeight(g) * 1.2f);
        g.DrawString(text, font, brush, new RectangleF(inner.Left, y, inner.Width, height), format);
        return height;
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_watchedParent is not null)
            {
                _watchedParent.Resize -= OnParentResize;
                _watchedParent = null;
            }

            _code?.Dispose();
            _code = null;
        }

        base.Dispose(disposing);
    }
}
