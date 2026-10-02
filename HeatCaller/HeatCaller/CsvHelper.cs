using System.Globalization;
using System.Text;

namespace HeatCaller;

/// <summary>Minimal CSV read/write helpers - just enough for this app's export/import needs, no external dependency.</summary>
public static class CsvHelper
{
    /// <summary>Quotes a field only if it needs it (contains a comma, quote, or newline), escaping embedded quotes by doubling them per standard CSV rules.</summary>
    public static string EscapeField(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    /// <summary>
    /// Builds one CSV line. Text fields get <see cref="NeutraliseFormula"/>;
    /// numbers are passed as numbers (ints, or pre-formatted strings that
    /// parse as one) and are left exactly as they are.
    /// </summary>
    public static string BuildRow(params object?[] fields) =>
        string.Join(",", fields.Select(f => EscapeField((f is string text)
            ? NeutraliseFormula(text)
            : f?.ToString() ?? string.Empty)));

    /// <summary>
    /// Stops a text field being run as a spreadsheet formula.
    ///
    /// Excel, LibreOffice and Google Sheets all treat a cell starting with
    /// = + - or @ as a formula, and these exports carry text anyone can
    /// type in - a driver's name, a team, a check-in page entry. A name of
    /// "=HYPERLINK(...)" becomes a live link, and worse is possible. A
    /// leading apostrophe makes the cell plain text. Anything that reads as
    /// a number ("-1.5") is left alone so a negative value stays a value,
    /// and so is a lone "-", the usual placeholder for "none".
    /// </summary>
    public static string NeutraliseFormula(string value)
    {
        if (value.Length == 0 || value == "-")
        {
            return value;
        }

        if (value[0] is not ('=' or '+' or '-' or '@' or '\t' or '\r'))
        {
            return value;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return value;
        }

        return "'" + value;
    }

    /// <summary>
    /// The encoding for a CSV file written by this app: UTF-8 with a byte
    /// order mark. Without the mark Excel reads the file as the local code
    /// page, so "Zoë" and "Müller" come out as "ZoÃ«" and "MÃ¼ller".
    /// </summary>
    public static Encoding FileEncoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Turns the bytes of a CSV someone picked into text, whatever program
    /// wrote it.
    ///
    /// The same Excel mismatch as <see cref="FileEncoding"/>, the other way
    /// round: Excel's ordinary "CSV (Comma delimited)" save, and most older
    /// timing software, write the Windows code page with no byte order mark.
    /// Read as UTF-8 (what File.ReadAllLines does without a mark) every
    /// accented letter in such a file - "Zoë", "Müller" - came out as the
    /// replacement character, so an imported driver was created with a
    /// mangled name and one already on file was skipped as a name mismatch.
    ///
    /// So: a byte order mark, when there is one, decides. Without one the
    /// bytes are tried as strict UTF-8 first - a code-page file with accents
    /// in it is almost never valid UTF-8, while a UTF-8 file (this app's own,
    /// or Excel's "CSV UTF-8") always is - and only if that fails is the file
    /// read as <paramref name="fallback"/>. A file of plain English letters
    /// reads the same either way.
    /// </summary>
    public static string DecodeText(byte[] bytes, Encoding fallback)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return fallback.GetString(bytes);
        }
    }

    /// <summary><see cref="DecodeText(byte[], Encoding)"/> with this PC's own code page as the fallback.</summary>
    public static string DecodeText(byte[] bytes) => DecodeText(bytes, LocalCodePage());

    /// <summary>
    /// The Windows code page this PC's programs write "ANSI" text in (1252
    /// for English and most of Western Europe). .NET only knows the code
    /// pages once their provider is registered; registering it again is
    /// harmless. Should the code page still not be available, Latin-1 is
    /// the nearest thing and keeps the common accented letters.
    /// </summary>
    private static Encoding LocalCodePage()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;

            if (codePage > 0)
            {
                return Encoding.GetEncoding(codePage);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Fall through to Latin-1.
        }

        return Encoding.Latin1;
    }

    /// <summary>
    /// Splits text into its non-blank lines, each with the line number it
    /// has in the file (from 1). Blank lines are left out - a spacer line or
    /// a trailing newline is not a row - but the numbers are kept, so an
    /// import's "Row 12: missing transponder" points at line 12 of the file
    /// even when there are blank lines above it. Lines break the same way
    /// File.ReadAllLines breaks them (\r\n, \n or \r).
    /// </summary>
    public static List<(string Text, int LineNumber)> NonBlankLines(string text)
    {
        var result = new List<(string Text, int LineNumber)>();
        using var reader = new StringReader(text);
        var number = 0;

        while (reader.ReadLine() is { } line)
        {
            number++;

            if (!string.IsNullOrWhiteSpace(line))
            {
                result.Add((line, number));
            }
        }

        return result;
    }

    /// <summary>
    /// Parses one line of CSV into fields, handling quoted fields (with
    /// embedded commas/escaped quotes) but NOT fields containing embedded
    /// newlines - adequate for simple tabular exports like driver rosters,
    /// not a full RFC 4180 parser.
    /// </summary>
    public static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
