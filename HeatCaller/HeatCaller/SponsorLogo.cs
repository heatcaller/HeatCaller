using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HeatCaller;

/// <summary>
/// A sponsor's own logo (14 Nov 2026, stream overlay plan section 1, Asher:
/// "add in the ability to upload a sponsor logo via sponsor page").
///
/// NOT THE PICTURE FOR THEIR READ. That is a 16:9 slide made to fill the Big
/// Screen. A logo is a small mark for the corner of a stream graphic and the
/// sponsor's card on the club's Live page, where a slide shrunk to fit is a
/// thin strip and a JPEG of it loses the transparent background.
///
/// Imported the way the club logo is (ClubLogo): the file is checked before
/// it is decoded (ImageProbe), drawn once into a PNG that keeps its
/// transparency, and saved in the Sponsors folder under a name only this
/// class makes - "sponsor-logo-" then twelve hex digits then ".png" - so a
/// crafted name in a restored backup can never serve some other file.
/// Unlike the club logo it KEEPS ITS SHAPE rather than being squared: a
/// sponsor's logo is usually wide, and it is never an app icon.
///
/// A replaced or removed logo's file is left where it is, so undo can put
/// it back; files no sponsor names any more are cleared a week on (Prune).
/// </summary>
internal static class SponsorLogo
{
    /// <summary>The most a stored logo may be - the club logo's ceiling.</summary>
    internal const int MaxBytes = 300 * 1024;

    /// <summary>The box a logo is fitted into on import, then the smaller one if it comes out too big.</summary>
    internal static readonly Size ImportBox = new(800, 400);
    internal static readonly Size SmallerBox = new(512, 256);

    /// <summary>The most a STORED logo may claim to be each way before it is decoded.</summary>
    private const int MaxStoredSide = 1024;

    /// <summary>On a Live page card and in a stream graphic, never bigger than this.</summary>
    internal static readonly Size CardBox = new(360, 160);

    private const string NamePrefix = "sponsor-logo-";
    private const int NameIdLength = 12;
    private const string NameExtension = ".png";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, byte[]?> BytesCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string?> CardCache = new(StringComparer.Ordinal);

    /// <summary>Where logos live - the folder the sponsor slides are kept in. Settable for the tests.</summary>
    internal static string Folder { get; set; } = Path.Combine(AppPaths.Root, "Sponsors");

    /// <summary>Copies a picture in as a sponsor's logo. Returns the new file's name, or null with a reason in plain words.</summary>
    public static string? Import(string sourcePath, out string? error)
    {
        try
        {
            if (ImageProbe.PickedProblem(sourcePath, "use as a sponsor's logo",
                    "A logo is usually well under 1 MB - choose a smaller copy.", out var bytes,
                    ImageProbe.MaxShrunkSourcePixels) is { } problem)
            {
                error = problem;
                return null;
            }

            using var stream = new MemoryStream(bytes);
            using var source = Image.FromStream(stream);

            var png = Render(source, ImportBox);

            if (png.Length > MaxBytes)
            {
                png = Render(source, SmallerBox);
            }

            if (png.Length > MaxBytes)
            {
                error = "That picture is too detailed to use as a logo. A simpler logo, ideally on a transparent background, works best.";
                return null;
            }

            Directory.CreateDirectory(Folder);

            var name = NamePrefix + Guid.NewGuid().ToString("N")[..NameIdLength] + NameExtension;
            File.WriteAllBytes(Path.Combine(Folder, name), png);

            error = null;
            return name;
        }
        catch (Exception ex)
        {
            error = $"Could not read that picture: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// The picture drawn into a transparent PNG no bigger than <paramref name="box"/>,
    /// keeping its shape - and never made bigger than it already is.
    /// </summary>
    internal static byte[] Render(Image source, Size box)
    {
        var scale = Math.Min(1.0, Math.Min((double)box.Width / source.Width, (double)box.Height / source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        using var output = new MemoryStream();
        canvas.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    /// <summary>
    /// Whether a stored name is one Import could have made: "sponsor-logo-",
    /// twelve lower-case hex digits, ".png" - nothing else. Asked before any
    /// stored name is turned back into a file.
    /// </summary>
    internal static bool IsLogoName([NotNullWhen(true)] string? fileName)
    {
        if (fileName is null
            || fileName.Length != NamePrefix.Length + NameIdLength + NameExtension.Length
            || !fileName.StartsWith(NamePrefix, StringComparison.Ordinal)
            || !fileName.EndsWith(NameExtension, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = NamePrefix.Length; i < NamePrefix.Length + NameIdLength; i++)
        {
            if (fileName[i] is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return PlainFileName.IsPlain(fileName);
    }

    /// <summary>
    /// The logo's PNG bytes, or null: a logo name, at most 300KB, a real PNG
    /// no bigger than Import makes. Cached by name - every overlay and phone
    /// asks for it - and the array is shared: callers write it out, never
    /// change it. A file held by another program just now is not remembered,
    /// so the next ask reads it again.
    /// </summary>
    public static byte[]? PngBytes(string? fileName)
    {
        if (!IsLogoName(fileName))
        {
            return null;
        }

        lock (Gate)
        {
            if (BytesCache.TryGetValue(fileName, out var held))
            {
                return held;
            }
        }

        byte[]? bytes;

        try
        {
            bytes = ReadStored(fileName);
        }
        catch (Exception ex)
        {
            if (ClubLogo.MayReadLater(ex))
            {
                return null;
            }

            bytes = null;
        }

        lock (Gate)
        {
            if (BytesCache.Count > 64)
            {
                BytesCache.Clear();
            }

            BytesCache[fileName] = bytes;
        }

        return bytes;
    }

    private static byte[]? ReadStored(string fileName)
    {
        var path = Path.Combine(Folder, fileName);

        if (new FileInfo(path).Length > MaxBytes)
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);

        return bytes.Length <= MaxBytes
               && ImageProbe.IsPng(bytes)
               && ImageProbe.PngSize(bytes) is { } size
               && size.Width <= MaxStoredSide
               && size.Height <= MaxStoredSide
            ? bytes
            : null;
    }

    /// <summary>
    /// The logo decoded, for the Sponsors page's preview, or null. The
    /// caller owns the image.
    /// </summary>
    public static Image? Load(string? fileName)
    {
        if (PngBytes(fileName) is not { } bytes)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);
            return new Bitmap(decoded);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The logo as a small PNG data URI for the relay's Live page card -
    /// fitted inside CardBox, transparency kept - or null. Cached by name:
    /// the document is built every two seconds.
    /// </summary>
    public static string? CardDataUri(string? fileName)
    {
        if (!IsLogoName(fileName))
        {
            return null;
        }

        lock (Gate)
        {
            if (CardCache.TryGetValue(fileName, out var held))
            {
                return held;
            }
        }

        string? uri = null;

        if (PngBytes(fileName) is { } bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                using var image = Image.FromStream(stream);
                uri = "data:image/png;base64," + Convert.ToBase64String(Render(image, CardBox));
            }
            catch
            {
                uri = null;
            }
        }
        else
        {
            // Not read just now (held, or not there): asked again next time.
            return null;
        }

        lock (Gate)
        {
            if (CardCache.Count > 64)
            {
                CardCache.Clear();
            }

            CardCache[fileName] = uri;
        }

        return uri;
    }

    /// <summary>
    /// Clears logo files no sponsor names any more, once they are a week
    /// old - long enough for undo, short enough that a season of replaced
    /// logos does not pile up. Quiet on failure: a file that cannot be
    /// deleted now is tried again next time.
    /// </summary>
    public static int Prune(IEnumerable<string?> inUse, DateTime nowUtc)
    {
        var keep = new HashSet<string>(inUse.Where(n => IsLogoName(n)).Select(n => n!), StringComparer.Ordinal);
        var removed = 0;

        try
        {
            if (!Directory.Exists(Folder))
            {
                return 0;
            }

            foreach (var path in Directory.EnumerateFiles(Folder, NamePrefix + "*" + NameExtension, SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);

                if (!IsLogoName(name) || keep.Contains(name) || nowUtc - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(7))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                    removed++;
                }
                catch
                {
                    // Held or read-only: next time.
                }
            }
        }
        catch
        {
            // The folder could not be read: nothing pruned this time.
        }

        return removed;
    }

    /// <summary>Forgets what is cached - for the tests, which point Folder somewhere new.</summary>
    internal static void ForgetCache()
    {
        lock (Gate)
        {
            BytesCache.Clear();
            CardCache.Clear();
        }
    }
}
