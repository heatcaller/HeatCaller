using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// One car on the Garage wall: its photograph, big, with its name over it.
///
/// Drawn rather than assembled out of a PictureBox and three labels. A
/// wall holds dozens of these, and a card built from four controls is four
/// windows each, four handles, four hit-tests and a theme pass that has to
/// reach all of them - which is how a page that should feel like flicking
/// through photographs ends up flickering when it scrolls. Owner-drawn,
/// one control, double-buffered.
///
/// A car with no photograph is not a broken card. It gets a plate in a
/// colour hashed from its own name with its race number on it, the same
/// trick DriverPhotos plays with initials - so a garage nobody has
/// photographed yet still looks deliberate, and the same car is the same
/// colour every time.
/// </summary>
internal sealed class CarCard : Control
{
    public const int CardWidth = 208;
    public const int CardHeight = 172;

    /// <summary>How much of the card is the picture. The rest is the name band.</summary>
    private const int PhotoHeight = 116;

    private readonly string _ownerName;
    private readonly string _chassisName;

    private bool _hot;

    public Car Car { get; }

    /// <summary>The thumbnail, owned by PhotoStore's cache - drawn, never disposed here.</summary>
    private readonly Image? _photo;

    /// <summary>Raised on a click. The wall decides what opening a car means.</summary>
    public event Action<Car>? Opened;

    public bool IsSelected { get; set; }

    public CarCard(Car car, string ownerName, string chassisName)
    {
        Car = car;
        _ownerName = ownerName;
        _chassisName = chassisName;

        Size = new Size(CardWidth, CardHeight);
        Cursor = Cursors.Hand;
        TabStop = true;

        // The first photo is the one it is shown by - see CarPhotosForm.
        _photo = car.PhotoFileNames.Count > 0
            ? PhotoStore.Cars.Thumb(car.PhotoFileNames[0], PhotoHeight * 2)
            : null;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnClick(EventArgs e)
    {
        Opened?.Invoke(Car);
        base.OnClick(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Reachable by keyboard as well as by mouse. A wall of pictures
        // that can only be used with a pointer is a wall half the club
        // cannot use.
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Opened?.Invoke(Car);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var body = new Rectangle(0, 0, Width - 1, Height - 1);

        using var shape = Rounded(body, 10);

        // The card's own background, so a retired car reads as dimmed
        // rather than as a different kind of thing.
        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillPath(back, shape);
        }

        var photoArea = new Rectangle(0, 0, Width - 1, PhotoHeight);

        var clip = g.Clip;
        g.SetClip(shape);

        if (_photo is not null)
        {
            DrawCover(g, _photo, photoArea);
        }
        else
        {
            DrawPlate(g, photoArea);
        }

        g.Clip = clip;

        // The name band, under the picture.
        var bandTop = PhotoHeight;

        using (var band = new SolidBrush(AppTheme.Surface))
        {
            g.SetClip(shape);
            g.FillRectangle(band, 0, bandTop, Width, Height - bandTop);
            g.Clip = clip;
        }

        var text = new Rectangle(10, bandTop + 6, Width - 20, Height - bandTop - 10);

        TextRenderer.DrawText(
            g, Car.DisplayName, AppTheme.BoldFont,
            new Rectangle(text.X, text.Y, text.Width, AppTheme.BoldFont.Height),
            AppTheme.TextDark,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var underline = string.IsNullOrWhiteSpace(_chassisName)
            ? _ownerName
            : string.IsNullOrWhiteSpace(_ownerName) ? _chassisName : $"{_ownerName}  ·  {_chassisName}";

        TextRenderer.DrawText(
            g, underline, AppTheme.SmallFont,
            new Rectangle(text.X, text.Y + AppTheme.BoldFont.Height + 1, text.Width, AppTheme.SmallFont.Height),
            AppTheme.TextMuted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // The race number, over the picture's top corner where it cannot
        // collide with the name.
        if (!string.IsNullOrWhiteSpace(Car.RaceNumber))
        {
            DrawBadge(g, Car.RaceNumber.Trim(), new Point(Width - 12, 10));
        }

        if (Car.Retired)
        {
            // Not hidden, and not crossed out - just quiet. A retired car
            // is still the club's history.
            using var veil = new SolidBrush(Color.FromArgb(130, AppTheme.Background));
            g.SetClip(shape);
            g.FillPath(veil, shape);
            g.Clip = clip;
        }

        var edge = IsSelected
            ? AppTheme.Accent
            : _hot || Focused ? AppTheme.TextMuted : AppTheme.Border;

        using var pen = new Pen(edge, IsSelected ? 2f : 1f);
        g.DrawPath(pen, shape);
    }

    /// <summary>Fills the box with the picture, cropping rather than squashing - a squashed car looks like a different car.</summary>
    private static void DrawCover(Graphics g, Image photo, Rectangle box)
    {
        var scale = Math.Max(box.Width / (float)photo.Width, box.Height / (float)photo.Height);
        var w = photo.Width * scale;
        var h = photo.Height * scale;

        g.DrawImage(photo, box.X + (box.Width - w) / 2f, box.Y + (box.Height - h) / 2f, w, h);
    }

    /// <summary>
    /// The stand-in for a car with no photograph: a flat plate in a colour
    /// taken from the car's own name, carrying its number, or its initials
    /// when it has no number.
    /// </summary>
    private void DrawPlate(Graphics g, Rectangle box)
    {
        var colour = PlateColour(Car.DisplayName);

        using (var fill = new SolidBrush(colour))
        {
            g.FillRectangle(fill, box);
        }

        var mark = string.IsNullOrWhiteSpace(Car.RaceNumber)
            ? Initials(Car.DisplayName)
            : Car.RaceNumber.Trim();

        using var font = new Font(AppTheme.BoldFont.FontFamily, 34f, FontStyle.Bold, GraphicsUnit.Pixel);

        TextRenderer.DrawText(
            g, mark, font, box, Color.FromArgb(235, 255, 255, 255),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private static void DrawBadge(Graphics g, string text, Point topRight)
    {
        var size = TextRenderer.MeasureText(text, AppTheme.BoldFont);
        var box = new Rectangle(topRight.X - size.Width - 12, topRight.Y, size.Width + 12, size.Height + 4);

        using (var back = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
        using (var shape = Rounded(box, 6))
        {
            g.FillPath(back, shape);
        }

        TextRenderer.DrawText(
            g, text, AppTheme.BoldFont, box, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// A stable colour for a name.
    ///
    /// Written out by hand rather than using string.GetHashCode, which is
    /// randomised per process in .NET - the same car would be a different
    /// colour on every launch, which is the opposite of what a recognisable
    /// plate is for. Same reasoning as RaceClass.DefaultColourFor.
    /// </summary>
    private static Color PlateColour(string name)
    {
        var hash = 17;

        foreach (var c in name.Trim().ToLowerInvariant())
        {
            hash = unchecked(hash * 31 + c);
        }

        var hue = (uint)hash % 360;

        return FromHsv(hue, 0.42f, AppTheme.Active.Light ? 0.72f : 0.46f);
    }

    private static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[^1][0])}",
        };
    }

    private static Color FromHsv(float hue, float saturation, float value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60f % 2 - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return Color.FromArgb(255, (int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    private static GraphicsPath Rounded(Rectangle box, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;

        path.AddArc(box.X, box.Y, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
