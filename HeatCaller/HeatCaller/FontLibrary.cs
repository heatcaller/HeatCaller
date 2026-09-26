using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// Loads the app's own typefaces from <c>Assets\Fonts</c> next to the exe, so
/// HeatCaller looks the same on a track PC where nobody can install fonts.
///
/// Every font is registered TWICE, and that is the whole trick.
/// PrivateFontCollection is a GDI+ object, and GDI+ is what
/// <c>Graphics.DrawString</c> uses - so a privately loaded font paints
/// correctly on the clock, the stat tiles and every other custom-drawn
/// surface. But WinForms draws control text through TextRenderer, which is
/// GDI, and GDI cannot see a GDI+ private collection at all: labels, buttons
/// and grid cells would silently fall back to the system font while the
/// painted parts of the same window used the new one. AddFontMemResourceEx
/// registers the same bytes with GDI for the life of the process, which is
/// what makes the two halves of the app agree.
///
/// Nothing here is required: with no Fonts folder the app resolves to
/// installed families exactly as before.
/// </summary>
internal static class FontLibrary
{
    private static readonly PrivateFontCollection Collection = new();
    private static readonly HashSet<string> Available = new(StringComparer.OrdinalIgnoreCase);

    private static bool _scanned;

    /// <summary>
    /// The collection's families, read once. Families builds a new array of
    /// new FontFamily objects on every read, and Create is called on every
    /// paint of the race clocks - so each paint allocated (and left for the
    /// finaliser) one per bundled family. Only read after Available has a
    /// name in it, which EnsureLoaded fills once the collection is complete,
    /// so the cached list is always the full one.
    /// </summary>
    private static readonly Lazy<FontFamily[]> LoadedFamilies =
        new(() => Collection.Families, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Folder scanned for .ttf/.otf, relative to the executable.</summary>
    public const string FolderName = @"Assets\Fonts";

    public static void EnsureLoaded()
    {
        if (_scanned)
        {
            return;
        }

        _scanned = true;

        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, FolderName);

            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var extension = Path.GetExtension(file);

                if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TryLoad(file);
            }

            foreach (var family in Collection.Families)
            {
                Available.Add(family.Name);
            }
        }
        catch
        {
            // A font that will not load is a cosmetic problem, never a
            // reason to fail startup - the resolver simply falls through to
            // whatever is installed.
        }
    }

    /// <summary>True when the named family was loaded from the Fonts folder.</summary>
    public static bool Has(string family)
    {
        EnsureLoaded();
        return Available.Contains(family);
    }

    /// <summary>
    /// Builds a font from the bundled family when it is present, and from the
    /// installed one otherwise.
    /// </summary>
    public static Font Create(string family, float size, FontStyle style)
    {
        EnsureLoaded();

        if (Available.Contains(family))
        {
            foreach (var candidate in LoadedFamilies.Value)
            {
                if (!string.Equals(candidate.Name, family, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Asking for a style the file does not carry throws rather
                // than synthesising it, so a family shipped without its bold
                // cut degrades to regular instead of taking the app down.
                var usable = candidate.IsStyleAvailable(style) ? style : FontStyle.Regular;

                return new Font(candidate, size, usable);
            }
        }

        return new Font(family, size, style);
    }

    private static void TryLoad(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var buffer = Marshal.AllocCoTaskMem(bytes.Length);

            Marshal.Copy(bytes, 0, buffer, bytes.Length);

            // GDI+ (Graphics.DrawString) and then GDI (TextRenderer, and so
            // every WinForms control).
            Collection.AddMemoryFont(buffer, bytes.Length);

            uint installed = 0;
            AddFontMemResourceEx(buffer, (uint)bytes.Length, IntPtr.Zero, ref installed);

            // The buffer is deliberately never freed. Both APIs keep reading
            // from it for as long as the fonts are in use, and "in use" here
            // means the whole run of the app - freeing it is a documented way
            // to get garbage glyphs or an access violation much later, in a
            // place that looks nothing like this one.
        }
        catch
        {
            // Skip the bad file, keep the rest.
        }
    }

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr AddFontMemResourceEx(IntPtr fontData, uint length, IntPtr reserved, ref uint fontCount);
}
