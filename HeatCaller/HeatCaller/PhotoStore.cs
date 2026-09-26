using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HeatCaller;

/// <summary>
/// Pictures the app owns, in a folder of their own.
///
/// The same arrangement <see cref="DriverPhotos"/> makes for the roster,
/// with the folder as a constructor argument so more than one kind of
/// picture can have it: the file the user picks is copied in - rotated the
/// way the phone meant it, scaled down, re-encoded - rather than
/// referenced where it sits, because a photo left on a USB stick or in
/// Downloads quietly vanishes the day that folder is tidied. Phone photos
/// also arrive at several megabytes and sideways, and both are fixed once,
/// at the door, rather than at every paint.
///
/// Every import gets a fresh random name. That is what lets the thumbnail
/// cache below never revalidate anything: a name is never given to
/// different bytes, so a cached thumbnail cannot go stale.
///
/// DriverPhotos predates this and still has its own copy of the same
/// mechanics. It is deliberately left alone for now - it is load-bearing
/// for the roster, the online check-in list and every driver card, and
/// this was written while two other features were in flight. Folding it
/// onto this class is a tidy-up worth doing on its own.
/// </summary>
public sealed class PhotoStore
{
    /// <summary>Longest edge a stored picture keeps. Cars are shown larger than driver avatars, so this is larger than DriverPhotos' 640.</summary>
    private const int StoredMaxEdge = 1024;

    // The most a STORED picture may be before it is decoded. Import only
    // ever writes StoredMaxEdge on the longest side, so this is generous -
    // it is here so that a picture planted in a restored backup cannot have
    // GDI+ build an enormous bitmap every time the page draws.
    private const long MaxStoredBytes = 16L * 1024 * 1024;
    private const int MaxStoredSide = StoredMaxEdge * 2;

    private readonly string _folderName;

    private readonly Dictionary<(string Name, int Size), Image> _thumbs = new();

    /// <summary>
    /// Stored pictures that failed the size check. Remembered for the same
    /// reason thumbnails are - a name is never given to different bytes -
    /// so a bad file is read once rather than on every paint.
    /// </summary>
    private readonly HashSet<string> _refused = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();

    public PhotoStore(string folderName)
    {
        _folderName = folderName;
    }

    /// <summary>The garage's own pictures. One instance, shared, so the thumbnail cache is shared with it.</summary>
    public static PhotoStore Cars { get; } = new("CarPhotos");

    private string Folder => Path.Combine(
        AppPaths.Root,
        _folderName);

    /// <summary>The filter to put on the picker, so the dialog and this agree on what can be imported.</summary>
    public const string PickerFilter =
        "Images (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files (*.*)|*.*";

    /// <summary>
    /// Copies a picked image in. Returns the stored file name, or null with
    /// the reason in <paramref name="error"/>.
    /// </summary>
    public string? Import(string sourcePath, string purpose, out string? error)
    {
        error = null;

        try
        {
            // Sized up from its length and its header before anything is
            // decoded. Going straight to Image.FromStream builds the whole
            // bitmap there and then, only for it to be shrunk - so a photo
            // from a phone's highest-resolution mode, or a small file whose
            // header claims tens of thousands of pixels each way, freezes
            // the app while GDI+ tries, or fails out of memory.
            if (ImageProbe.PickedProblem(sourcePath, purpose, "Choose a smaller copy.", out var bytes,
                    ImageProbe.MaxShrunkSourcePixels) is { } problem)
            {
                error = problem;
                return null;
            }

            Directory.CreateDirectory(Folder);

            // From bytes, not FromFile - FromFile keeps the source locked
            // for the image's whole lifetime, and that file is the user's.
            using var original = Image.FromStream(new MemoryStream(bytes));

            ApplyExifOrientation(original);

            var scale = Math.Min(1f, StoredMaxEdge / (float)Math.Max(original.Width, original.Height));
            var w = Math.Max(1, (int)(original.Width * scale));
            var h = Math.Max(1, (int)(original.Height * scale));

            using var resized = new Bitmap(w, h);

            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawImage(original, new Rectangle(0, 0, w, h));
            }

            var fileName = $"{Guid.NewGuid().ToString("N")[..12]}.jpg";
            SaveJpeg(resized, Path.Combine(Folder, fileName), quality: 85);

            return fileName;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Best-effort removal of a picture nothing references any more.</summary>
    public void Delete(string? fileName)
    {
        // A name out of a JSON file a restored backup writes - see
        // PlainFileName for what a crafted one could otherwise delete.
        if (!PlainFileName.IsPlain(fileName))
        {
            return;
        }

        try
        {
            var path = Path.Combine(Folder, fileName);

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A picture left behind costs a few kilobytes. Failing a save
            // because one could not be deleted costs the user their edit.
        }
    }

    /// <summary>
    /// A square thumbnail, or null when there is nothing usable to show -
    /// the caller draws its own placeholder, which is a drawing job rather
    /// than an image.
    ///
    /// The returned image belongs to this cache. Callers draw it; they do
    /// not dispose it.
    /// </summary>
    public Image? Thumb(string? fileName, int size)
    {
        if (!PlainFileName.IsPlain(fileName))
        {
            return null;
        }

        lock (_gate)
        {
            if (_thumbs.TryGetValue((fileName, size), out var cached))
            {
                return cached;
            }

            if (_refused.Contains(fileName))
            {
                return null;
            }

            var path = Path.Combine(Folder, fileName);

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                // Sized from its header before anything is decoded, and only
                // a picture no bigger than Import could have made gets
                // through. The name comes out of garage.json, which a
                // restored backup writes.
                if (ImageProbe.ReadStored(path, MaxStoredBytes, MaxStoredSide) is not { } bytes)
                {
                    _refused.Add(fileName);
                    return null;
                }

                using var full = Image.FromStream(new MemoryStream(bytes));

                // Cover-crop to a square: every place these go is square-ish
                // and letterboxing wastes most of an already-small box.
                var edge = Math.Min(full.Width, full.Height);
                var src = new Rectangle((full.Width - edge) / 2, (full.Height - edge) / 2, edge, edge);

                var thumb = new Bitmap(size, size);

                using (var g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(full, new Rectangle(0, 0, size, size), src, GraphicsUnit.Pixel);
                }

                _thumbs[(fileName, size)] = thumb;
                return thumb;
            }
            catch
            {
                _refused.Add(fileName);
                return null;
            }
        }
    }

    /// <summary>
    /// The picture at its own shape rather than cropped square - for the
    /// one big view on the page, where a car's proportions are the point.
    /// Not cached: there is one of these on screen at a time, and caching
    /// every size a resizing panel asks for would grow without end.
    ///
    /// The caller owns and must dispose what comes back.
    /// </summary>
    public Image? Full(string? fileName)
    {
        if (!PlainFileName.IsPlain(fileName))
        {
            return null;
        }

        var path = Path.Combine(Folder, fileName);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            if (ImageProbe.ReadStored(path, MaxStoredBytes, MaxStoredSide) is not { } bytes)
            {
                return null;
            }

            return Image.FromStream(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Phone JPEGs are usually stored sideways with a note saying which way
    /// is up (EXIF tag 274); GDI+ reads the pixels and ignores the note.
    /// Applied once at import so nothing downstream ever thinks about it.
    /// </summary>
    private static void ApplyExifOrientation(Image image)
    {
        const int orientationTag = 0x0112;

        if (Array.IndexOf(image.PropertyIdList, orientationTag) < 0)
        {
            return;
        }

        var value = image.GetPropertyItem(orientationTag)?.Value;

        if (value is null || value.Length == 0)
        {
            return;
        }

        var type = value[0] switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        };

        if (type != RotateFlipType.RotateNoneFlipNone)
        {
            image.RotateFlip(type);
        }
    }

    private static void SaveJpeg(Image image, string path, long quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

        if (codec is null)
        {
            // No JPEG encoder is a broken GDI+ install; PNG still saves.
            image.Save(path, ImageFormat.Png);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);

        image.Save(path, codec, parameters);
    }
}
