namespace HeatCaller;

/// <summary>
/// A picture's size from its header, without decoding it, and the limits
/// every picture the app takes in is held to before GDI+ decodes it.
///
/// Image.FromStream builds the whole bitmap there and then, whatever size
/// the file says it is. A full-resolution photo, or a small PNG whose
/// header claims tens of thousands of pixels each way, froze the app while
/// GDI+ tried, or failed with an out-of-memory error that can't be relied
/// on to be caught cleanly. The file's length and the size its header gives
/// cost next to nothing to read, so every button that takes a picture in
/// asks PickedProblem first, and everything that reads a stored picture
/// back asks ReadStored.
///
/// It started in ClubLogo, the first place this was found. Driver photos,
/// sponsor slides, track photos, chassis diagrams and the Track Designer's
/// traced photo all used to decode first too, and now share it.
/// </summary>
internal static class ImageProbe
{
    // The most a picture the user picks may be, checked before it is
    // decoded. Nothing the app does with a picture needs anything like this
    // much; the limits are only there so that GDI+ is never asked to build
    // an enormous bitmap. 40 million pixels is about 160 MB once decoded,
    // and an everyday 12-megapixel phone photo is well inside it.
    //
    // The pixel count is what bounds the memory; the side only stops a
    // header claiming absurd sizes. It was 8000 at first, which turned away
    // a panorama well inside the pixel budget.
    internal const long MaxSourceBytes = 50L * 1024 * 1024;
    internal const int MaxSourceSide = 20_000;
    internal const long MaxSourcePixels = 40_000_000;

    /// <summary>
    /// The pixel budget for a picture that is shrunk as it comes in and never
    /// kept as picked - a driver photo, the club logo: decoded once, on the
    /// way in. Big enough for a phone's full-resolution 48 or 50 megapixel
    /// mode (8064 x 6048, 8160 x 6144), which the 40 million every stored
    /// picture is held to turned away. About 256 MB while it is shrunk.
    /// </summary>
    internal const long MaxShrunkSourcePixels = 64_000_000;

    /// <summary>
    /// Why a picture the user has just picked can't be taken in, or null
    /// when it can - with the file's bytes, read once, in
    /// <paramref name="bytes"/> for the caller to decode (empty when there
    /// is a reason). Too long a file, not a PNG, JPEG, GIF or BMP (see
    /// HeaderSize), or by its header too many pixels: each is turned away
    /// before anything is decoded, with the reason in plain English.
    ///
    /// <paramref name="purpose"/> finishes both "too big to ..." and "a
    /// picture HeatCaller can ...": "use as a logo", "trace over".
    /// <paramref name="tooLongAdvice"/> is the sentence that follows the one
    /// about the file's length.
    ///
    /// Throws when the file can't be read at all - gone since it was picked,
    /// or locked - as File.ReadAllBytes does. Every caller catches that and
    /// says so in its own words.
    /// </summary>
    /// <param name="maxPixels">The pixel budget: MaxSourcePixels for a picture kept as picked, MaxShrunkSourcePixels for one shrunk on the way in.</param>
    internal static string? PickedProblem(string path, string purpose, string tooLongAdvice, out byte[] bytes, long maxPixels = MaxSourcePixels)
    {
        bytes = Array.Empty<byte>();

        // The length before anything is read, so a huge file is never
        // pulled into memory just to be turned away.
        if (new FileInfo(path).Length > MaxSourceBytes)
        {
            return $"That file is over {MaxSourceBytes / (1024 * 1024)} MB, too big to {purpose}. {tooLongAdvice}";
        }

        var read = File.ReadAllBytes(path);

        if (HeaderSize(read) is not { } size)
        {
            return $"That file isn't a picture HeatCaller can {purpose}. Choose a PNG, JPEG, GIF or BMP picture.";
        }

        if (!Fits(size, MaxSourceSide, maxPixels))
        {
            return $"That picture is {size.Width} x {size.Height} pixels, too big to {purpose}. It can be up to "
                   + $"{MaxSourceSide} pixels across or down, and {maxPixels / 1_000_000} million pixels in all - "
                   + "save a smaller copy and choose that.";
        }

        bytes = read;
        return null;
    }

    /// <summary>
    /// A stored picture's bytes, ready to decode, or null when it is more
    /// than the limits allow: a file longer than <paramref name="maxBytes"/>,
    /// one whose size can't be read from its header (see HeaderSize), or one
    /// whose header claims more than <paramref name="maxSide"/> pixels either
    /// way or <paramref name="maxPixels"/> in all. The defaults are exactly
    /// what PickedProblem lets in, for a picture kept just as it was picked;
    /// one the app shrinks on the way in can be held to far less.
    ///
    /// For every picture read back from the app's own folder, or from a path
    /// kept in its files. Those are only as trustworthy as the backup they
    /// were restored from, and they are read as pages open and paint - so a
    /// planted picture that froze or crashed the app there would do it every
    /// time. The length is looked at before anything is read, so a huge file
    /// under a picture's name is never pulled into memory either. Throws when
    /// the file can't be read at all; every caller catches that.
    /// </summary>
    internal static byte[]? ReadStored(string path, long maxBytes = MaxSourceBytes, int maxSide = MaxSourceSide, long maxPixels = MaxSourcePixels)
    {
        if (new FileInfo(path).Length > maxBytes)
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);

        return bytes.Length <= maxBytes
               && HeaderSize(bytes) is { } size
               && Fits(size, maxSide, maxPixels)
            ? bytes
            : null;
    }

    /// <summary>Whether a picture this size is within the limits: no more than maxSide pixels either way, and no more than maxPixels in all.</summary>
    private static bool Fits(Size size, int maxSide, long maxPixels) =>
        size.Width <= maxSide && size.Height <= maxSide && (long)size.Width * size.Height <= maxPixels;

    /// <summary>Whether the bytes start with the eight-byte signature every PNG starts with.</summary>
    internal static bool IsPng(byte[] bytes) =>
        bytes.Length > 8
        && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
        && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    /// <summary>
    /// A picture's width and height from its header, without decoding it -
    /// or null when it isn't a PNG, JPEG, GIF or BMP, or its header can't be
    /// read.
    ///
    /// Those four are what every picture dialog in the app lists (the Track
    /// Designer's leaves out GIF, which works all the same). Their "All
    /// files" choice lets anything be picked, and GDI+ can open a few more
    /// kinds (TIFF, icons, Windows metafiles), but each would need a header
    /// reader of its own, and one decoded unchecked is exactly what this is
    /// here to stop - so they are turned away with a message saying what
    /// will work. The kind is told from the bytes, not the extension, so a
    /// JPEG saved as .jfif (as some browsers name them) still goes through.
    /// </summary>
    internal static Size? HeaderSize(byte[] bytes)
    {
        if (IsPng(bytes))
        {
            return PngSize(bytes);
        }

        // FF D8: a JPEG's start-of-image marker.
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return JpegSize(bytes);
        }

        // "GIF87a" or "GIF89a".
        if (bytes.Length >= 6
            && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38
            && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
        {
            return GifSize(bytes);
        }

        // "BM".
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return BmpSize(bytes);
        }

        return null;
    }

    /// <summary>
    /// From the IHDR chunk, which a PNG must have first, straight after the
    /// eight-byte signature: its length, "IHDR", then the width and the
    /// height, four bytes each, most significant first.
    /// </summary>
    internal static Size? PngSize(byte[] bytes)
    {
        if (bytes.Length < 24
            || bytes[12] != 0x49 || bytes[13] != 0x48 || bytes[14] != 0x44 || bytes[15] != 0x52)
        {
            return null;
        }

        var width = BigEndian32(bytes, 16);
        var height = BigEndian32(bytes, 20);

        return width is > 0 and <= int.MaxValue && height is > 0 and <= int.MaxValue
            ? new Size((int)width, (int)height)
            : null;
    }

    /// <summary>
    /// From the frame header (an SOF marker), found the way a JPEG decoder
    /// finds it: marker by marker from the start, stepping over each segment
    /// by the length it gives. That also steps over the little preview a
    /// camera tucks inside its Exif segment - a JPEG of its own, whose size
    /// is not the photo's.
    /// </summary>
    private static Size? JpegSize(byte[] bytes)
    {
        var at = 2;

        while (true)
        {
            // A marker is 0xFF and a code. Anything before the 0xFF is damage
            // a decoder skips over, and extra 0xFFs are padding.
            while (at < bytes.Length && bytes[at] != 0xFF)
            {
                at++;
            }

            while (at < bytes.Length && bytes[at] == 0xFF)
            {
                at++;
            }

            if (at >= bytes.Length)
            {
                return null;
            }

            var marker = bytes[at++];

            // Codes that stand alone, with no length after them: 00 (an
            // escaped 0xFF inside picture data, not a marker at all), TEM, a
            // repeated start-of-image, and the restart markers.
            if (marker is 0x00 or 0x01 or 0xD8 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            // The end of the picture, or the start of its compressed data,
            // and still no frame header: the size was never given.
            if (marker is 0xD9 or 0xDA)
            {
                return null;
            }

            // Every other marker has a two-byte length, which counts itself.
            if (at + 2 > bytes.Length)
            {
                return null;
            }

            var length = BigEndian16(bytes, at);

            if (length < 2)
            {
                return null;
            }

            // SOF0 to SOF15, apart from three codes in that range that mean
            // something else: C4 (Huffman tables), C8 (reserved) and CC
            // (arithmetic coding). After the length: the precision, then the
            // height and the width, two bytes each.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 8 || at + 7 > bytes.Length)
                {
                    return null;
                }

                var height = BigEndian16(bytes, at + 3);
                var width = BigEndian16(bytes, at + 5);

                // A height of 0 means "given later, after the picture data",
                // which GDI+ can't decode anyway.
                return width > 0 && height > 0 ? new Size(width, height) : null;
            }

            at += length;
        }
    }

    /// <summary>
    /// From the logical screen - the canvas the frames are drawn on - and
    /// from every frame, since nothing stops a frame claiming to be bigger
    /// than the canvas it sits on. Frames are found by walking the blocks,
    /// each of which says how long it is; their picture data is never
    /// unpacked.
    /// </summary>
    private static Size? GifSize(byte[] bytes)
    {
        if (bytes.Length < 13)
        {
            return null;
        }

        var width = LittleEndian16(bytes, 6);
        var height = LittleEndian16(bytes, 8);
        var at = 13 + ColourTableLength(bytes[10]);

        while (at < bytes.Length)
        {
            var block = bytes[at++];

            if (block == 0x2C)
            {
                // A frame: its left, top, width and height, then a flags byte.
                if (at + 9 > bytes.Length)
                {
                    break;
                }

                width = Math.Max(width, LittleEndian16(bytes, at) + LittleEndian16(bytes, at + 4));
                height = Math.Max(height, LittleEndian16(bytes, at + 2) + LittleEndian16(bytes, at + 6));

                // Then its own colour table if it has one, a byte of LZW
                // code size, and the compressed picture.
                var flags = bytes[at + 8];
                at += 9 + ColourTableLength(flags) + 1;
                at = SkipSubBlocks(bytes, at);
            }
            else if (block == 0x21)
            {
                // An extension (timing, transparency, a comment): a label
                // byte, then its data.
                at = SkipSubBlocks(bytes, at + 1);
            }
            else
            {
                // The trailer (3B), or something that isn't GIF at all:
                // either way, there are no more frames to size.
                break;
            }
        }

        return width > 0 && height > 0 ? new Size(width, height) : null;
    }

    /// <summary>How many bytes of colour table a GIF flags byte says follow: none, or 3 x 2^(n+1).</summary>
    private static int ColourTableLength(byte flags) =>
        (flags & 0x80) == 0 ? 0 : 3 << ((flags & 0x07) + 1);

    /// <summary>Past a run of GIF data sub-blocks - each a length byte and that many bytes - and the zero length that ends it.</summary>
    private static int SkipSubBlocks(byte[] bytes, int at)
    {
        while (at < bytes.Length)
        {
            var length = bytes[at++];

            if (length == 0)
            {
                break;
            }

            at += length;
        }

        return at;
    }

    /// <summary>
    /// From the header after the 14-byte file header, whose own length says
    /// which kind it is: the old OS/2 kind (12 bytes) gives the width and the
    /// height in two bytes each, every later kind in four - with a negative
    /// height meaning the rows are stored top row first.
    /// </summary>
    private static Size? BmpSize(byte[] bytes)
    {
        if (bytes.Length < 26)
        {
            return null;
        }

        var headerLength = LittleEndian32(bytes, 14);
        long width;
        long height;

        if (headerLength == 12)
        {
            width = LittleEndian16(bytes, 18);
            height = LittleEndian16(bytes, 20);
        }
        else if (headerLength >= 16)
        {
            width = LittleEndian32(bytes, 18);

            // As a long, so the most negative int turns positive instead of
            // overflowing.
            height = Math.Abs((long)LittleEndian32(bytes, 22));
        }
        else
        {
            return null;
        }

        return width is > 0 and <= int.MaxValue && height is > 0 and <= int.MaxValue
            ? new Size((int)width, (int)height)
            : null;
    }

    private static int BigEndian16(byte[] bytes, int at) => (bytes[at] << 8) | bytes[at + 1];

    private static int LittleEndian16(byte[] bytes, int at) => bytes[at] | (bytes[at + 1] << 8);

    /// <summary>Unsigned, so as a long: a PNG's width can be up to 2^31 - 1, and anything claiming more is simply refused.</summary>
    private static long BigEndian32(byte[] bytes, int at) =>
        ((long)bytes[at] << 24) | ((long)bytes[at + 1] << 16) | ((long)bytes[at + 2] << 8) | bytes[at + 3];

    /// <summary>Signed, as a BMP header's numbers are.</summary>
    private static int LittleEndian32(byte[] bytes, int at) =>
        bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24);
}
