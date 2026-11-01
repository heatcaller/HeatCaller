namespace HeatCaller;

/// <summary>
/// The Big Screen's driver card during the driver introductions (1 Nov 2026,
/// asked for by Asher): the driver being introduced, big - their photo, their
/// name, where they start, and their number and home club.
///
/// A driver with no photo, or one whose photo is kept off the Big Screen
/// (DriverProfile.NoPhotoOnBigScreen - a young driver, say), gets the initials
/// disc the rest of the app draws for them, in the colour their name always
/// hashes to (DriverPhotos.DrawAvatar). Their name is the same size either
/// way: the name is the part the room reads.
///
/// Drawn, like the roll call it takes the place of, in the screen's look:
/// colours are handed in by the Big Screen.
/// </summary>
internal sealed class IntroCardView : Control
{
    /// <summary>How long the card takes to slide in when the driver changes.</summary>
    private const int SlideMs = 260;

    private readonly System.Windows.Forms.Timer _animate = new() { Interval = 30 };

    private long _changedAt;

    /// <summary>The faces made for the last size drawn at - made again only when it changes.</summary>
    private (int Height, Font Name, Font Slot, Font Detail)? _faces;

    public Color Canvas { get; set; } = Color.Black;
    public Color Card { get; set; } = Color.FromArgb(30, 30, 30);
    public Color Ink { get; set; } = Color.White;
    public Color Muted { get; set; } = Color.Gray;
    public Color Highlight { get; set; } = Color.Gold;

    /// <summary>The driver on the card, or null.</summary>
    public IntroCard? Driver { get; private set; }

    public IntroCardView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        DoubleBuffered = true;
        TabStop = false;

        _animate.Tick += (_, _) =>
        {
            Invalidate();

            if (Environment.TickCount64 - _changedAt > SlideMs)
            {
                _animate.Stop();
            }
        };
    }

    /// <summary>Puts <paramref name="driver"/> on the card, sliding in when it is a new one.</summary>
    public void Show(IntroCard driver)
    {
        if (Driver == driver)
        {
            return;
        }

        Driver = driver;
        _changedAt = Environment.TickCount64;
        _animate.Start();
        Invalidate();
    }

    /// <summary>Forgets the driver, so the next card slides in fresh.</summary>
    public void Reset()
    {
        Driver = null;
        _animate.Stop();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var back = new SolidBrush(Canvas))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        if (Driver is not { } driver || Width < 40 || Height < 40)
        {
            return;
        }

        // The card, slid up from a little below and eased in.
        var t = Math.Clamp((Environment.TickCount64 - _changedAt) / (float)SlideMs, 0f, 1f);
        var ease = 1f - (1f - t) * (1f - t);

        var margin = Math.Max(10f, Math.Min(Width, Height) / 16f);
        var card = new RectangleF(margin, margin + (1f - ease) * Height / 12f, Width - 2 * margin, Height - 2 * margin);
        var radius = Math.Max(6, (int)(Math.Min(card.Width, card.Height) / 18));

        using (var path = AppTheme.RoundedRect(card, radius))
        using (var fill = new SolidBrush(Card))
        {
            g.FillPath(fill, path);
        }

        var pad = Math.Max(8f, card.Height / 12f);
        var inner = RectangleF.Inflate(card, -pad, -pad);

        // Side by side on a wide screen, photo over name on a tall one.
        var wide = inner.Width >= inner.Height * 1.3f;
        var side = wide
            ? Math.Min(inner.Height, inner.Width * 0.42f)
            : Math.Min(inner.Width, inner.Height * 0.55f);

        // A whole number of 32-pixel steps: the photo cache keeps one copy per
        // size asked for (DriverPhotos.Thumb), so a window dragged wider must
        // not leave a copy at every width it passed through.
        var photoSide = Math.Max(32, (int)side / 32 * 32);

        var photo = wide
            ? new Rectangle((int)inner.Left, (int)(inner.Top + (inner.Height - photoSide) / 2), photoSide, photoSide)
            : new Rectangle((int)(inner.Left + (inner.Width - photoSide) / 2), (int)inner.Top, photoSide, photoSide);

        var words = wide
            ? new RectangleF(photo.Right + pad, inner.Top, inner.Right - photo.Right - pad, inner.Height)
            : new RectangleF(inner.Left, photo.Bottom + pad / 2, inner.Width, inner.Bottom - photo.Bottom - pad / 2);

        DriverPhotos.DrawAvatar(g, photo, driver.ScreenName, driver.PhotoFileName.Length > 0 ? driver.PhotoFileName : null,
            cornerRadius: Math.Max(6, photoSide / 10));

        if (words.Width < 20 || words.Height < 20)
        {
            return;
        }

        var faces = Faces((int)Math.Min(words.Height, wide ? card.Height : words.Height * 2f));
        var slot = IntroCards.SlotLine(driver.Slot);
        var detail = IntroCards.DetailLine(driver.Number, driver.Hometown);

        // The three lines stacked in the middle of their space: where they
        // start, the name (up to two lines), the number and home club.
        var slotHeight = slot.Length > 0 ? faces.Slot.Height * 1.3f : 0f;
        var detailHeight = detail.Length > 0 ? faces.Detail.Height * 1.4f : 0f;
        var nameHeight = Math.Min(faces.Name.Height * 2.2f, Math.Max(faces.Name.Height * 1.1f, words.Height - slotHeight - detailHeight));
        var top = words.Top + Math.Max(0, (words.Height - slotHeight - nameHeight - detailHeight) / 2);
        var flags = (wide ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.NoPrefix;

        if (slot.Length > 0)
        {
            TextRenderer.DrawText(g, slot, faces.Slot,
                Rectangle.Round(new RectangleF(words.Left, top, words.Width, slotHeight)), Highlight,
                flags | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            top += slotHeight;
        }

        TextRenderer.DrawText(g, driver.ScreenName, faces.Name,
            Rectangle.Round(new RectangleF(words.Left, top, words.Width, nameHeight)), Ink,
            flags | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        top += nameHeight;

        if (detail.Length > 0)
        {
            TextRenderer.DrawText(g, detail, faces.Detail,
                Rectangle.Round(new RectangleF(words.Left, top, words.Width, detailHeight)), Muted,
                flags | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>The name, slot and detail faces for this much height - made once per size, not per paint.</summary>
    private (int Height, Font Name, Font Slot, Font Detail) Faces(int height)
    {
        if (_faces is { } kept && kept.Height == height)
        {
            return kept;
        }

        DisposeFaces();

        var family = AppTheme.BoldFont.FontFamily;

        var made = (
            height,
            new Font(family, Math.Max(10f, height * 0.17f), FontStyle.Bold, GraphicsUnit.Pixel),
            new Font(family, Math.Max(8f, height * 0.075f), FontStyle.Bold, GraphicsUnit.Pixel),
            new Font(family, Math.Max(8f, height * 0.07f), FontStyle.Regular, GraphicsUnit.Pixel));

        _faces = made;
        return made;
    }

    private void DisposeFaces()
    {
        if (_faces is { } faces)
        {
            faces.Name.Dispose();
            faces.Slot.Dispose();
            faces.Detail.Dispose();
            _faces = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animate.Stop();
            _animate.Dispose();
            DisposeFaces();
        }

        base.Dispose(disposing);
    }
}
