using System.Diagnostics.CodeAnalysis;

namespace HeatCaller;

/// <summary>
/// Whether a file name that came out of a settings or profiles file is
/// just a file name.
///
/// Photo and logo names are stored as bare names inside the app's own
/// folder, and turned back into paths with Path.Combine. That is only safe
/// while the name really is bare: "..\..\Documents\thing" combines into a
/// path outside the folder, and "C:\Windows\x" replaces the folder
/// altogether. Those files are the app's own, but a backup restored from
/// somewhere else writes them - so a crafted backup could otherwise have
/// the app delete, read or publish any file this Windows account can
/// reach. Everything that turns a stored name into a path asks here first.
///
/// SafePart goes the other way: it makes text someone typed fit to be part
/// of a file name the app suggests when saving something.
/// </summary>
internal static class PlainFileName
{
    public static bool IsPlain([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Contains(".."))
        {
            return false;
        }

        // Letters, digits, dot, dash, underscore - every name this app has
        // ever generated, and nothing that can mean a directory or a drive.
        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            {
                return false;
            }
        }

        // Plain letters can still spell a device. "COM3.jpg" as a photo
        // name, planted in a restored backup, has Windows open the serial
        // port - possibly the decoder's own - every time that driver is
        // drawn, and "CON" waits for keyboard input that never comes. The
        // app never generates such a name, so it is refused as IsSafeLeaf
        // refuses it.
        var dot = name.IndexOf('.');
        if (ReservedDeviceNames.Contains(dot >= 0 ? name[..dot] : name))
        {
            return false;
        }

        return string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal);
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// A looser test than <see cref="IsPlain"/>, for names the user chose
    /// themselves - a sponsor slide keeps its original file name, spaces
    /// and all. Still only ever a single name inside a folder: no
    /// separators, no drive, no "." or "..", nothing Windows would treat as
    /// a device ("NUL.png" opens the null device, not a file).
    /// </summary>
    public static bool IsSafeLeaf([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            return false;
        }

        if (name is "." or ".." || name.Contains('/') || name.Contains('\\') || name.Contains(':'))
        {
            return false;
        }

        // Windows quietly drops trailing dots and spaces, so "x." and "x"
        // are the same file - and a name that isn't what it looks like is
        // how a check like this gets walked around. A leading space is
        // legal but never something this app wrote.
        if (name[0] == ' ' || name[^1] == ' ' || name[^1] == '.')
        {
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        // "CON", "con.txt", "Lpt1.png": the part before the first dot is
        // what Windows checks.
        var dot = name.IndexOf('.');
        var stem = (dot >= 0 ? name[..dot] : name).TrimEnd(' ');
        if (ReservedDeviceNames.Contains(stem))
        {
            return false;
        }

        return string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal);
    }

    /// <summary>The longest piece of typed text <see cref="SafePart"/> puts into a suggested file name.</summary>
    private const int MaxPartLength = 60;

    /// <summary>
    /// Text someone typed - a driver's, class's or season's name - made fit
    /// to go into the file name the app suggests in a Save dialog, between
    /// the app's own words: "trend-{this}-2026-09-22.png".
    ///
    /// Every character Windows won't have in a file name (/ \ : * ? " &lt; &gt; |
    /// and the control characters) becomes "_", the same as the race
    /// history's exports have always done. Blank text gives
    /// <paramref name="fallback"/>, and anything past 60 characters is cut,
    /// so a long name can't push the whole path past what Windows allows.
    /// Not for a name on its own: a device name such as "CON" only matters
    /// as a whole name, and every caller puts words around this.
    /// </summary>
    public static string SafePart(string? text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = text.Trim().ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        var part = new string(chars);

        if (part.Length > MaxPartLength)
        {
            part = part[..MaxPartLength];

            // Not half a pair: an emoji cut down the middle would leave a
            // stray character that isn't anything.
            if (char.IsHighSurrogate(part[^1]))
            {
                part = part[..^1];
            }
        }

        return part;
    }

    /// <summary>
    /// Whether a stored path points at another machine: "\\server\share\x"
    /// or "//server/x", including the "\\?\" and "\\.\" device forms.
    ///
    /// Merely checking such a path exists makes Windows try to log on to
    /// that server with this account's credentials - so a sound or music
    /// path planted in a restored backup could hand a password hash to
    /// whoever runs the server, without anything even playing.
    /// </summary>
    public static bool IsNetworkPath(string? path) =>
        // Either slash in either place: Windows treats "\\" "//" "\/" and
        // "/\" alike.
        path is { Length: >= 2 }
        && (path[0] is '\\' or '/')
        && (path[1] is '\\' or '/');

    /// <summary>
    /// Whether <paramref name="path"/> resolves to somewhere strictly inside
    /// <paramref name="root"/>. The last line of defence before anything is
    /// deleted or moved: a name can pass every check above and still be
    /// combined into somewhere unexpected if the check is ever skipped.
    /// </summary>
    public static bool IsInside(string root, string path)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            return fullPath.Length > fullRoot.Length
                && fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
