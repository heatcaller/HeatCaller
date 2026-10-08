namespace HeatCaller;

/// <summary>
/// Whether a sponsor slide is a picture or a video, and the limits a video
/// is held to before it is taken in.
///
/// Slides were pictures only until videos were added, and the two live in
/// one rotation (SponsorStore.SlideFileNames) because a club thinks of them
/// as one list: "what the Big Screen shows between the standings". Which of
/// the two a name is decides nearly everything downstream - whether the Big
/// Screen decodes it into a PictureBox or hands it to LibVLC, whether the
/// web pages get an &lt;img&gt; or a &lt;video&gt;, and whether the music is
/// muted while it is up - so the question is answered in one place.
///
/// A video is NOT read into memory the way a picture is (see ImageProbe):
/// an ad can be hundreds of megabytes, LibVLC streams it from disk, and the
/// web server streams it too. So there is no pixel budget here, only a
/// length, and the file is copied rather than read and written back.
/// </summary>
internal static class SponsorMedia
{
    /// <summary>
    /// The most a sponsor video may be. An ad cut for a club screen is a
    /// few tens of megabytes; this is loose enough to take an
    /// uncompressed-ish master without complaint and tight enough that a
    /// mis-picked disk image is turned away before it is copied into
    /// %AppData%.
    /// </summary>
    internal const long MaxVideoBytes = 512L * 1024 * 1024;

    /// <summary>
    /// The video file extensions the Add Video... dialog offers and
    /// AddSlide accepts, lower case and with the dot.
    ///
    /// Not "whatever LibVLC can open", which is far wider and includes
    /// playlists and stream descriptors - a .m3u or .xspf handed to LibVLC
    /// would have it reach out to whatever the file named, which is not
    /// something a sponsor slide should ever do. These are self-contained
    /// container files and nothing else.
    /// </summary>
    internal static readonly IReadOnlyList<string> VideoExtensions = new[]
    {
        ".mp4", ".m4v", ".mov", ".webm", ".mkv", ".avi", ".wmv",
    };

    /// <summary>The picture extensions, as the Add Image... dialog has always offered them.</summary>
    internal static readonly IReadOnlyList<string> ImageExtensions = new[]
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif",
    };

    /// <summary>
    /// Whether a stored slide name is a video, by its extension.
    ///
    /// The extension is the whole answer here on purpose. The name came out
    /// of NewSlideFileName, which kept the extension the file was picked
    /// with, and AddSlide had already sniffed the bytes behind it (see
    /// LooksLikeVideo) before it was stored - so by the time anything asks
    /// this, the extension has been vouched for once. Asking again on every
    /// rotation would mean opening the file on the UI thread each time a
    /// slide came round.
    /// </summary>
    internal static bool IsVideoName(string fileName) =>
        VideoExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <summary>The filter string for an OpenFileDialog that takes videos.</summary>
    internal static string VideoFilter =>
        "Videos (" + string.Join(";", VideoExtensions.Select(e => "*" + e)) + ")|"
        + string.Join(";", VideoExtensions.Select(e => "*" + e))
        + "|All files (*.*)|*.*";

    /// <summary>
    /// Why a video the user has just picked can't be taken in, or null when
    /// it can. Too long a file, an extension that isn't one of
    /// VideoExtensions, or a header that isn't a container HeatCaller
    /// recognises: each is turned away before anything is copied, with the
    /// reason in plain English - the same shape as ImageProbe.PickedProblem.
    ///
    /// Throws when the file can't be read at all - gone since it was picked,
    /// or locked. The caller catches that and says so in its own words.
    /// </summary>
    internal static string? PickedProblem(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        if (!VideoExtensions.Contains(extension))
        {
            return "That file isn't a video HeatCaller can show as a sponsor slide. Choose an "
                   + string.Join(", ", VideoExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()))
                   + " file.";
        }

        // The length before anything is opened, so a huge file is never
        // read just to be turned away.
        var length = new FileInfo(path).Length;

        if (length > MaxVideoBytes)
        {
            return $"That video is over {MaxVideoBytes / (1024 * 1024)} MB, too big to show as a sponsor slide. "
                   + "Export a smaller copy - a screen ad doesn't need more.";
        }

        if (length == 0)
        {
            return "That video file is empty.";
        }

        // The extension is what everything downstream trusts (see
        // IsVideoName), so it is vouched for once, here, against the bytes.
        // Without this an .exe renamed to .mp4 would be stored as a slide
        // and handed to LibVLC every rotation.
        if (!LooksLikeVideo(ReadHeader(path)))
        {
            return "That file has a video's name but isn't one inside - it may be damaged, or renamed from something else. "
                   + "Export it again from whatever made it.";
        }

        return null;
    }

    /// <summary>The first bytes of a file, as much as any signature below needs.</summary>
    private static byte[] ReadHeader(string path)
    {
        using var stream = File.OpenRead(path);

        var header = new byte[16];
        var read = stream.Read(header, 0, header.Length);

        return read == header.Length ? header : header[..Math.Max(0, read)];
    }

    /// <summary>
    /// Whether these opening bytes are one of the containers the video
    /// extensions name. Deliberately a short list of signatures rather than
    /// a parse: it only has to tell a real container from a file that was
    /// renamed into one.
    /// </summary>
    internal static bool LooksLikeVideo(byte[] header) =>
        // ISO base media (MP4, M4V, MOV): a box length, then "ftyp". Some
        // MOV files lead with another box ("wide", "free", "skip", "moov",
        // "mdat"), so those four-letter box types count too.
        IsIsoBaseMedia(header)

        // Matroska and WebM, which share EBML's magic number.
        || header is [0x1A, 0x45, 0xDF, 0xA3, ..]

        // AVI: "RIFF" .... "AVI "
        || (header is [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x41, 0x56, 0x49, 0x20, ..])

        // ASF, which is what a .wmv is.
        || header is [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, ..];

    private static bool IsIsoBaseMedia(byte[] header)
    {
        if (header.Length < 8)
        {
            return false;
        }

        var box = System.Text.Encoding.ASCII.GetString(header, 4, 4);

        return box is "ftyp" or "wide" or "free" or "skip" or "moov" or "mdat";
    }
}
