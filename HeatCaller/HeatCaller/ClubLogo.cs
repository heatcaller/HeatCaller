using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HeatCaller;

/// <summary>
/// The club's logo, for the header of its online pages and the icon a
/// phone puts on its home screen.
///
/// Imported the way driver photos are: copied into the app's own folder,
/// squared and resized once, so a logo picked from a Downloads folder that
/// later gets tidied does not vanish from the club's pages. Stored as a PNG
/// because a logo usually has a transparent background worth keeping, and
/// because a PNG is the one thing the relay will accept as a logo.
/// </summary>
internal static class ClubLogo
{
    /// <summary>Kept under the relay's 400KB ceiling with room to spare.</summary>
    private const int MaxBytes = 300 * 1024;

    // The most a STORED logo may claim to be, each way, before it is
    // decoded (see ReadStoredLogo). Import only ever makes one 384 or 256
    // pixels square, so this leaves plenty of room and still keeps a decode
    // to a few MB.
    private const int MaxStoredSide = 1024;

    private static readonly object Gate = new();
    private static string? _cachedFile;
    private static string? _cachedUri;

    // The shape of every name Import gives a logo: "club-logo-" then 12
    // lower-case hex digits, then ".png". See IsLogoName.
    private const string NamePrefix = "club-logo-";
    private const int NameIdLength = 12;
    private const string NameExtension = ".png";

    private static string Folder => AppPaths.Root;

    /// <summary>Copies a picture in as the logo. Returns the new file's name, or null with a reason.</summary>
    public static string? Import(string sourcePath, out string? error)
    {
        try
        {
            // Sized up before anything is decoded, by the file's length and
            // the size its header gives (see ImageProbe, where this check now
            // lives so that every other picture the app takes in can share
            // it). This used to go straight to Image.FromStream, which has
            // GDI+ build the whole bitmap there and then, and only looked at
            // size once the logo had been drawn and saved - so a
            // full-resolution phone photo, or a small PNG whose header claims
            // tens of thousands of pixels each way, froze the app while GDI+
            // tried, or failed with an out-of-memory error that can't be
            // relied on to be caught cleanly. A logo ends up 384 pixels
            // square, so it needs nothing like as much as the check allows;
            // the limits are only there so that GDI+ is never asked to build
            // an enormous bitmap just to shrink it.
            //
            // Bytes first, then a stream: Image.FromFile holds the file open
            // for as long as the image lives, which would lock the user's
            // own copy of their logo.
            if (ImageProbe.PickedProblem(sourcePath, "use as a logo",
                    "A logo is usually well under 1 MB - choose a smaller copy.", out var bytes,
                    ImageProbe.MaxShrunkSourcePixels) is { } problem)
            {
                error = problem;
                return null;
            }

            using var stream = new MemoryStream(bytes);
            using var source = Image.FromStream(stream);

            // A photo-like logo can come out big as a PNG; one step down in
            // size usually fixes that without anyone noticing on a phone.
            var png = Render(source, 384);

            if (png.Length > MaxBytes)
            {
                png = Render(source, 256);
            }

            if (png.Length > MaxBytes)
            {
                error = "That picture is too detailed to use as a logo. A simpler logo, ideally on a plain or transparent background, works best.";
                return null;
            }

            Directory.CreateDirectory(Folder);

            // A new name each time rather than overwriting: the relay's
            // icon is cached by phones for an hour, and the settings file
            // naming a file that is being rewritten is a race for nothing.
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
    /// Square, the picture centred and scaled to fit with a little room
    /// around it. Square because it becomes an app icon, and a phone
    /// crops an icon to its own shape - a logo edge-to-edge would lose
    /// its corners to that.
    /// </summary>
    private static byte[] Render(Image source, int size)
    {
        using var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var room = size * 0.88;
            var scale = Math.Min(room / source.Width, room / source.Height);
            var width = (int)Math.Round(source.Width * scale);
            var height = (int)Math.Round(source.Height * scale);

            g.DrawImage(source, new Rectangle((size - width) / 2, (size - height) / 2, width, height));
        }

        using var output = new MemoryStream();
        canvas.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    /// <summary>The logo as a data: URI for the site document, or null for none. Cached: it is asked for every two seconds.</summary>
    public static string? DataUri(string? fileName)
    {
        // The name comes from settings, which a restored backup writes. A
        // crafted one naming some other file would otherwise have that
        // file's contents published to the relay - see IsLogoName.
        if (!IsLogoName(fileName))
        {
            return null;
        }

        lock (Gate)
        {
            if (_cachedFile == fileName)
            {
                return _cachedUri;
            }

            string? uri;

            try
            {
                // Only ever a real PNG, and only one this app could have
                // made (see ReadStoredLogo). Anything else in the file is not
                // a logo, and it is not going onto a public page.
                uri = ReadStoredLogo(fileName!) is { } bytes
                    ? "data:image/png;base64," + Convert.ToBase64String(bytes)
                    : null;
            }
            catch
            {
                // A logo file that has gone missing costs the club its logo,
                // not its pages.
                uri = null;
            }

            _cachedFile = fileName;
            _cachedUri = uri;
            return uri;
        }
    }

    private static string? _bytesFile;
    private static byte[]? _bytes;

    /// <summary>
    /// The logo's PNG bytes, for the Wi-Fi pages' /logo.png, or null for
    /// none. The same checks as DataUri - a logo name, at most 300KB, a real
    /// PNG no bigger than Import makes (see ReadStoredLogo) - and cached the
    /// same way, because every page load on every phone asks for it. The
    /// array is shared: callers write it out, never change it.
    /// </summary>
    public static byte[]? PngBytes(string? fileName)
    {
        if (!IsLogoName(fileName))
        {
            return null;
        }

        lock (Gate)
        {
            if (_bytesFile == fileName)
            {
                return _bytes;
            }

            byte[]? bytes;

            try
            {
                bytes = ReadStoredLogo(fileName);
            }
            catch
            {
                bytes = null;
            }

            _bytesFile = fileName;
            _bytes = bytes;
            return bytes;
        }
    }

    /// <summary>
    /// Whether a stored name is one Import could have made: "club-logo-",
    /// twelve lower-case hex digits, ".png" - nothing else. Every place that
    /// turns the stored name back into a file asks this first. Stricter than
    /// PlainFileName.IsPlain, which any other file in the data folder would
    /// pass too.
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
    /// The stored logo file's bytes when it is what Import makes - at most
    /// 300KB, a real PNG, and by its header no more than MaxStoredSide
    /// pixels each way - or null. Throws when the file can't be read at
    /// all; every caller catches that.
    ///
    /// One test for every reader (Load, DataUri, PngBytes), because the file
    /// is only as trustworthy as the backup it was restored from. Load used
    /// to decode whatever it found, and a 300KB PNG can claim a picture tens
    /// of thousands of pixels each way. That decode happens as the main
    /// window opens - the sidebar header shows the logo - so one planted in
    /// a backup would have frozen or crashed the app on every launch. The
    /// length is looked at before anything is read, so a huge file under a
    /// logo's name is never pulled into memory either.
    /// </summary>
    private static byte[]? ReadStoredLogo(string fileName)
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

    private static string? _samplesFile;
    private static Color[]? _samples;

    /// <summary>
    /// The logo's solid pixels - every fourth one each way, which is plenty
    /// to judge it by and keeps a 384px logo to about nine thousand reads -
    /// or null when there is no logo or it can't be read. Cached per file:
    /// the Wi-Fi pages ask on every request. The array is shared and never
    /// changed after it is made.
    /// </summary>
    private static Color[]? SolidSamples(string? fileName)
    {
        if (!IsLogoName(fileName))
        {
            return null;
        }

        lock (Gate)
        {
            if (_samplesFile == fileName)
            {
                return _samples;
            }
        }

        Color[]? samples = null;

        try
        {
            using var image = Load(fileName);

            if (image is Bitmap bitmap)
            {
                var list = new List<Color>();

                for (var y = 0; y < bitmap.Height; y += 4)
                {
                    for (var x = 0; x < bitmap.Width; x += 4)
                    {
                        var p = bitmap.GetPixel(x, y);

                        if (p.A >= 128)
                        {
                            list.Add(Color.FromArgb(p.R, p.G, p.B));
                        }
                    }
                }

                samples = list.Count > 0 ? list.ToArray() : null;
            }
        }
        catch
        {
            samples = null;
        }

        lock (Gate)
        {
            _samplesFile = fileName;
            _samples = samples;
        }

        return samples;
    }

    /// <summary>
    /// A plate to put behind the logo so it shows up on this background, or
    /// null when it already shows up fine.
    ///
    /// Logos are kept with their transparent background, which is right
    /// until a black logo lands on a black Big Screen and disappears. Judged
    /// by how much of the logo would vanish rather than by its average
    /// colour: a black-and-white logo averages to a grey that stands out
    /// from anything, while half of it is lost on black. When a good part
    /// of it (two in five of its solid pixels) is too close to the
    /// background, it goes on a plate - white when the vanishing part is
    /// dark, near-black when it is light.
    ///
    /// But a badge with a ring, a border or lettering that already reads
    /// clearly is not vanishing just because the rest of it is a dark
    /// background BY DESIGN - a common look for a badge meant to sit on a
    /// dark screen already. When a full fifth of the logo's solid pixels
    /// are comfortably legible on their own (the same 3:1 this app treats
    /// as "clearly readable" elsewhere, see Looks.ReadabilityWarnings), a
    /// plate is left off: it would only frame art that was never trying to
    /// blend in everywhere, with a box that - for anything round - shows
    /// past the logo's own edge (see SidebarHeader.DrawLogo).
    /// </summary>
    public static Color? PlateFor(string? fileName, Color background)
    {
        if (SolidSamples(fileName) is not { } samples)
        {
            return null;
        }

        // Remembered per background for this set of samples. The Wi-Fi
        // pages ask on every request - /api/look alone is polled every five
        // seconds by every open page - and each answer was some nine
        // thousand contrast sums of six Math.Pow each, for a question whose
        // answer only changes when the logo or the look does (25 Sep
        // security audit). Keyed on the samples array itself: SolidSamples
        // makes a new one whenever the logo changes, so a new logo can never
        // be answered from the old one's plate.
        var key = background.ToArgb();

        lock (Gate)
        {
            if (!ReferenceEquals(_platesFor, samples))
            {
                _platesFor = samples;
                _plates.Clear();
            }
            else if (_plates.TryGetValue(key, out var known))
            {
                return known;
            }
        }

        var plate = PlateFrom(samples, background);

        lock (Gate)
        {
            if (ReferenceEquals(_platesFor, samples))
            {
                _plates[key] = plate;
            }
        }

        return plate;
    }

    /// <summary>The samples PlateFor's answers are remembered for. Guarded by Gate.</summary>
    private static Color[]? _platesFor;

    /// <summary>PlateFor's answers for _platesFor, by background colour. Guarded by Gate.</summary>
    private static readonly Dictionary<int, Color?> _plates = new();

    private static Color? PlateFrom(Color[] samples, Color background)
    {
        var lost = 0;
        var lostDark = 0;
        var clear = 0;

        foreach (var p in samples)
        {
            var contrast = Looks.Contrast(p, background);

            if (contrast < 1.5)
            {
                lost++;

                if (Looks.Luminance(p) < 0.4)
                {
                    lostDark++;
                }
            }
            else if (contrast >= 3.0)
            {
                clear++;
            }
        }

        if (lost * 5 < samples.Length * 2)
        {
            return null;
        }

        if (clear * 5 >= samples.Length)
        {
            return null;
        }

        return lostDark * 2 >= lost ? Color.White : ColorTranslator.FromHtml("#11151C");
    }

    /// <summary>The logo as an image - for the Looks tab preview, the sidebar header and the Big Screen. The caller disposes it.</summary>
    public static Image? Load(string? fileName)
    {
        if (!IsLogoName(fileName))
        {
            return null;
        }

        try
        {
            // Only a file that passes the same test as the pages' copies.
            // This used to decode whatever the file held - see
            // ReadStoredLogo for what that let a restored backup do.
            if (ReadStoredLogo(fileName!) is not { } bytes)
            {
                return null;
            }

            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);

            // Copied out, so the returned image does not depend on a stream
            // that is about to be disposed.
            return new Bitmap(decoded);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes a logo file the app made. Anything else is left alone.
    ///
    /// The name comes from settings.json, which a restored backup writes.
    /// Checking only that it was a plain name let a crafted one say
    /// "profiles.json" - a plain name in this same folder - and pressing
    /// Remove Logo then deleted the driver list.
    /// </summary>
    public static void Delete(string? fileName)
    {
        if (!IsLogoName(fileName))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(Folder, fileName));
        }
        catch
        {
            // Left behind is harmless; it is a few KB in the app's folder.
        }
    }

    /// <summary>
    /// Takes a logo file the app made out of use without destroying it: moved
    /// into the undo holding folder, so <see cref="RestoreFromHolding"/> can
    /// put it back. Returns where it went, or null when there was nothing to
    /// hold (or it couldn't be moved - then it is deleted as before). The same
    /// name check as Delete, for the same reason.
    /// </summary>
    public static string? MoveToHolding(string? fileName)
    {
        if (!IsLogoName(fileName))
        {
            return null;
        }

        var held = UndoHoldingFolder.MoveIn(Path.Combine(Folder, fileName), UndoHoldingFolder.NewFolder());
        if (held is null)
        {
            Delete(fileName);
        }

        return held;
    }

    /// <summary>
    /// Puts a logo held by <see cref="MoveToHolding"/> back under its own
    /// name. True when the file is in place afterwards - including when it
    /// never left.
    /// </summary>
    public static bool RestoreFromHolding(string? held, string? fileName)
    {
        // Not a name the app made: MoveToHolding never touched it, so it is
        // wherever it always was.
        if (!IsLogoName(fileName))
        {
            return true;
        }

        var path = Path.Combine(Folder, fileName);
        if (File.Exists(path))
        {
            return true;
        }

        var restored = UndoHoldingFolder.MoveBack(held, path);

        lock (Gate)
        {
            // The pages' cached copy is keyed by name; a name that has just
            // come back must be read afresh. All three caches: one filled
            // while the file was away holds "no logo" for that name.
            _cachedFile = null;
            _cachedUri = null;
            _bytesFile = null;
            _bytes = null;
            _samplesFile = null;
            _samples = null;
        }

        return restored;
    }
}
