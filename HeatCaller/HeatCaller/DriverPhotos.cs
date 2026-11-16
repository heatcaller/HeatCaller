using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HeatCaller;

/// <summary>
/// Driver photos: imported once, owned by the app from then on.
///
/// The file the user picks is copied - resized, re-encoded - into the
/// app's own folder, the same rule the sponsor slides follow: a photo that
/// stayed on a USB stick or in a Downloads folder would quietly vanish
/// from the roster the day that folder was tidied. Phone photos also
/// arrive at several megabytes and sideways; both are fixed at the door,
/// once, rather than at every paint.
///
/// Every import gets a fresh file name, so a replaced photo is a new file
/// rather than new bytes under an old name - which is what lets the
/// thumbnail cache below never revalidate anything.
/// </summary>
public static class DriverPhotos
{
    /// <summary>Longest edge a stored photo keeps. Cards draw at ~270px, so this is 2x for sharpness and nothing more.</summary>
    private const int StoredMaxEdge = 640;

    // The most a STORED photo may be before it is decoded (see Thumb).
    // Import only ever makes one StoredMaxEdge pixels on its longest side,
    // as a JPEG of well under a megabyte (or a PNG of under two, on a PC
    // with no JPEG encoder), so this is twice that each way and far more
    // bytes than such a file can come to. It is only there so that a photo
    // planted in a backup can't have GDI+ build an enormous bitmap every
    // time a driver is drawn.
    private const long MaxStoredBytes = 16L * 1024 * 1024;
    private const int MaxStoredSide = StoredMaxEdge * 2;

    private static string Folder => Path.Combine(
        AppPaths.Root,
        "DriverPhotos");

    /// <summary>Thumbnails already scaled, keyed by (file, size). Never invalidated - see the class comment.</summary>
    private static readonly Dictionary<(string Name, int Size), Image> ThumbCache = new();

    /// <summary>
    /// Stored photos that failed Thumb's size check, or passed it but could
    /// not be decoded (Bug Hunt 8, A27-5). Remembered for the same
    /// reason thumbnails are - a name is never given to a different photo -
    /// so a bad file is read once rather than on every paint of every
    /// avatar that shows it.
    /// </summary>
    private static readonly HashSet<string> Refused = new();

    private static readonly object Gate = new();

    /// <summary>
    /// Copies a picked image in: rotated the way the phone meant it,
    /// scaled down, saved as JPEG. Returns the stored file name, or null
    /// with the reason in <paramref name="error"/>.
    /// </summary>
    public static string? Import(string sourcePath, out string? error)
    {
        error = null;

        try
        {
            // Sized up from its length and its header before anything is
            // decoded (see ImageProbe). This used to go straight to
            // Image.FromStream, which builds the whole bitmap there and then
            // only for it to be shrunk to StoredMaxEdge - so a photo from a
            // phone's highest-resolution mode, or a small file whose header
            // claims tens of thousands of pixels each way, froze the app
            // while GDI+ tried, or failed with an out-of-memory error. Phone
            // photos are the likeliest pictures in the app to be that big.
            if (ImageProbe.PickedProblem(sourcePath, "use as a driver photo", "Choose a smaller copy.", out var bytes,
                    ImageProbe.MaxShrunkSourcePixels) is { } problem)
            {
                error = problem;
                return null;
            }

            Directory.CreateDirectory(Folder);

            // From bytes, not FromFile - FromFile keeps the source file
            // locked for the image's whole lifetime, and this one belongs
            // to the user.
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

    /// <summary>Best-effort removal of a photo nothing references any more.</summary>
    public static void Delete(string? fileName)
    {
        // A name from the profiles file, which a restored backup writes -
        // see PlainFileName for what a crafted one could otherwise delete.
        if (!PlainFileName.IsPlain(fileName))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(Folder, fileName));
        }
        catch
        {
            // An orphaned file is untidy; a crash over one is worse.
        }
    }

    /// <summary>
    /// Forget this driver (15 Nov 2026, privacy plan 2.5): the photo gone
    /// for good - in use, and the copy Undo keeps in Retired. Best-effort,
    /// like Delete.
    /// </summary>
    public static void Forget(string? fileName)
    {
        Delete(fileName);

        if (!PlainFileName.IsPlain(fileName))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(RetiredFolder, fileName));
        }
        catch
        {
            // As Delete.
        }
    }

    /// <summary>
    /// Where a replaced or removed photo waits instead of being deleted,
    /// so Undo can put it back. A photo deleted outright left an undone
    /// profile pointing at a file that no longer existed - its initials
    /// came back, its face didn't.
    /// </summary>
    private static string RetiredFolder => Path.Combine(Folder, "Retired");

    /// <summary>
    /// How many retired photos are kept before the oldest are deleted for
    /// good. Comfortably past the undo depth, and at well under a megabyte
    /// each it is a few tens of megabytes at the very most.
    /// </summary>
    private const int MaxRetired = 60;

    /// <summary>
    /// Takes a photo out of use without deleting it: moved aside, where
    /// <see cref="Unretire"/> can fetch it back for an undo. Best-effort,
    /// like Delete.
    /// </summary>
    public static void Retire(string? fileName)
    {
        if (!PlainFileName.IsPlain(fileName))
        {
            return;
        }

        try
        {
            var current = Path.Combine(Folder, fileName);

            if (!File.Exists(current))
            {
                return;
            }

            Directory.CreateDirectory(RetiredFolder);
            var held = Path.Combine(RetiredFolder, fileName);
            File.Move(current, held, overwrite: true);

            // Stamped now, so pruning drops the photos retired longest ago
            // rather than the ones that happened to be taken longest ago.
            File.SetLastWriteTimeUtc(held, DateTime.UtcNow);
            PruneRetired();
        }
        catch
        {
            // Left where it is - untidy, but nothing is lost.
        }
    }

    /// <summary>
    /// Brings a retired photo back into use, for an undo. Does nothing when
    /// the photo is in use already or has aged out of the retired folder.
    /// </summary>
    public static void Unretire(string? fileName)
    {
        if (!PlainFileName.IsPlain(fileName))
        {
            return;
        }

        try
        {
            var held = Path.Combine(RetiredFolder, fileName);
            var current = Path.Combine(Folder, fileName);

            if (File.Exists(held) && !File.Exists(current))
            {
                File.Move(held, current);
            }
        }
        catch
        {
            // The profile still comes back, with its initials instead.
        }
    }

    /// <summary>
    /// Deletes the oldest retired photos beyond MaxRetired, and any retired
    /// longer ago than the undo holding folder keeps its files. The undo
    /// list doesn't outlive the app, so an old one can never be asked for
    /// again - and a photo somebody replaced or had taken down shouldn't
    /// linger on the laptop (or ride along in its backups) indefinitely.
    /// </summary>
    private static void PruneRetired()
    {
        var cutoff = DateTime.UtcNow.AddDays(-UndoHoldingFolder.KeepDays);
        var pastTheCap = new DirectoryInfo(RetiredFolder)
            .GetFiles()
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Where((f, i) => i >= MaxRetired || f.LastWriteTimeUtc < cutoff);

        foreach (var file in pastTheCap)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Try again at the next retire.
            }
        }
    }

    /// <summary>
    /// The photo scaled for a given box, from cache after the first ask,
    /// or null when there is no photo to show - the caller draws the
    /// initials avatar instead, which is a drawing job, not an image.
    /// </summary>
    public static Image? Thumb(string? fileName, int size)
    {
        if (!PlainFileName.IsPlain(fileName))
        {
            return null;
        }

        lock (Gate)
        {
            if (ThumbCache.TryGetValue((fileName, size), out var cached))
            {
                return cached;
            }

            if (Refused.Contains(fileName))
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
                // a photo no bigger than Import could have made gets through
                // (see MaxStoredSide). This used to decode whatever the file
                // held, and the name comes from profiles.json, which a
                // restored backup writes - so a picture planted there, too
                // big to decode, froze or crashed the app every time the
                // roster, a driver card or the online check-in list drew
                // that driver. It gets the initials disc instead, as an
                // unreadable photo always has.
                if (ImageProbe.ReadStored(path, MaxStoredBytes, MaxStoredSide) is not { } bytes)
                {
                    Refused.Add(fileName);
                    return null;
                }

                var thumb = CoverCrop(bytes, size);

                ThumbCache[(fileName, size)] = thumb;
                return thumb;
            }
            catch (IOException)
            {
                // Briefly locked or unreadable: worth another try next paint.
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch
            {
                // 17 Oct 2026, Bug Hunt 8, A27-5: a file that passes the
                // header check but GDI+ can't decode used to be re-read and
                // re-decoded on every paint. Refused like PhotoStore does.
                Refused.Add(fileName);
                return null;
            }
        }
    }

    /// <summary>
    /// The photo as <see cref="Thumb"/> makes it, but a bitmap of the
    /// caller's own: never cached and never handed to anyone else, so a
    /// background thread can draw from it, and the caller disposes it. Null
    /// when there is no photo to show. Same name check, folder and size
    /// limits as Thumb.
    /// </summary>
    // WHY (22 Oct 2026, bug hunt 9): the highlight videos used to copy from
    // Thumb's shared cache on their own thread, which GDI+ does not allow
    // while the UI draws the same image; they then kept a private copy of
    // these checks (HighlightVideo's OwnPhoto). One copy now, here. Decoded
    // outside the lock, so a video never holds up the UI's paints.
    public static Bitmap? OwnedThumb(string? fileName, int size)
    {
        if (!PlainFileName.IsPlain(fileName))
        {
            return null;
        }

        lock (Gate)
        {
            if (Refused.Contains(fileName))
            {
                return null;
            }
        }

        try
        {
            var path = Path.Combine(Folder, fileName);

            if (!File.Exists(path)
                || ImageProbe.ReadStored(path, MaxStoredBytes, MaxStoredSide) is not { } bytes)
            {
                return null;
            }

            return CoverCrop(bytes, size);
        }
        catch
        {
            // Locked, unreadable or not a picture: no photo. Not added to
            // Refused here - Thumb decides that on its own read.
            return null;
        }
    }

    /// <summary>A stored photo's bytes, cover-cropped to a new square bitmap of <paramref name="size"/>.</summary>
    private static Bitmap CoverCrop(byte[] bytes, int size)
    {
        using var full = Image.FromStream(new MemoryStream(bytes));

        // Cover-crop to a square, because every place a thumbnail
        // goes is square-ish and letterboxing a face wastes most
        // of an already-small box.
        var edge = Math.Min(full.Width, full.Height);
        var src = new Rectangle((full.Width - edge) / 2, (full.Height - edge) / 2, edge, edge);

        var thumb = new Bitmap(size, size);

        try
        {
            using var g = Graphics.FromImage(thumb);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(full, new Rectangle(0, 0, size, size), src, GraphicsUnit.Pixel);
        }
        catch
        {
            thumb.Dispose();
            throw;
        }

        return thumb;
    }

    /// <summary>
    /// Draws the driver into a box: their photo cover-cropped under
    /// rounded corners, or - for the many drivers who will never add one -
    /// an initials disc in a colour their name always hashes to, so the
    /// roster is a wall of distinct faces either way.
    /// </summary>
    public static void DrawAvatar(Graphics g, Rectangle bounds, string displayName, string? photoFileName, int cornerRadius = 8)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = AppTheme.RoundedRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), cornerRadius);

        var photo = Thumb(photoFileName, Math.Max(bounds.Width, bounds.Height));

        if (photo is not null)
        {
            // Save/Restore rather than reading g.Clip: that getter hands back
            // a fresh GDI Region every call, which would leak once per paint.
            var state = g.Save();
            g.SetClip(path);

            // Cover, centred: scale so the shorter side fills the box.
            var scale = Math.Max(bounds.Width / (float)photo.Width, bounds.Height / (float)photo.Height);
            var w = photo.Width * scale;
            var h = photo.Height * scale;
            g.DrawImage(photo, bounds.X + (bounds.Width - w) / 2f, bounds.Y + (bounds.Height - h) / 2f, w, h);

            g.Restore(state);
            return;
        }

        using (var fill = new SolidBrush(ColourFor(displayName)))
        {
            g.FillPath(fill, path);
        }

        var initials = InitialsFor(displayName);
        var fontSize = Math.Max(8f, bounds.Height * 0.34f);

        using var font = new Font(AppTheme.TitleFont.FontFamily, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.White);
        using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.DrawString(initials, font, text, new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), centred);
    }

    /// <summary>Up to two initials - "Sam Carter" is SC, "Sam" is S, a bare transponder shows its first two characters.</summary>
    public static string InitialsFor(string displayName)
    {
        var words = (displayName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[^1][0])}",
        };
    }

    /// <summary>
    /// A saturated, dark-enough colour the same name always lands on, so
    /// white initials read and a driver's disc never changes between
    /// visits. A hash, not a palette index, so two similar names still
    /// usually differ.
    /// </summary>
    public static Color ColourFor(string displayName)
    {
        var hash = 17;

        foreach (var c in (displayName ?? string.Empty).ToUpperInvariant())
        {
            hash = unchecked(hash * 31 + c);
        }

        // Hue from the hash; saturation and brightness pinned where white
        // text stays legible on every hue.
        //
        // Cast to unsigned rather than Math.Abs. Math.Abs(int.MinValue)
        // THROWS - there is no positive int to return it as - and the hash
        // above is a plain h*31+c over the name, which is easy to solve
        // backwards. A driver name landing on that one value took the app
        // down at startup and kept it down: this runs from the profile list,
        // which is built inside MainForm's constructor, so no window ever
        // appeared and the only way back was hand-editing profiles.json.
        // Names arrive from a CSV import and from public online sign-ups, so
        // "nobody would type that" is not a defence.
        //
        // RaceClassModels.DefaultColourFor already does it this way.
        var hue = (int)((uint)hash % 360u);
        return FromHsv(hue, 0.55f, 0.62f);
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

    /// <summary>
    /// Phone JPEGs are usually stored sideways with a note saying which
    /// way is up (EXIF tag 274); GDI+ reads the pixels and ignores the
    /// note. Applied once at import so nothing downstream ever thinks
    /// about it again.
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
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        image.Save(path, codec, parameters);
    }
}
