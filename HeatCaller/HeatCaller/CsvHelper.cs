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
    /// <remarks>
    /// Non-text fields are written invariantly (17 Oct 2026, Bug Hunt 8,
    /// A26-2) - a negative total came out as "−5" (U+2212) on sv/nb/fi Windows.
    /// </remarks>
    public static string BuildRow(params object?[] fields) =>
        string.Join(",", fields.Select(f => EscapeField((f is string text)
            ? NeutraliseFormula(text)
            : Convert.ToString(f, CultureInfo.InvariantCulture) ?? string.Empty)));

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
    ///
    /// A quoted field with line breaks in it (a note typed over two lines in
    /// Excel) keeps its row together: the lines it spans come back as ONE
    /// entry, joined by a line break, numbered by the line it starts on.
    /// Each of those lines used to be a row of its own, and the second half
    /// of the note was imported as a junk driver. A quote only opens a
    /// quoted field at the start of one (as CSV has it), and a quote never
    /// closed by the end of the file joins nothing - those lines are read
    /// one by one, as they always were.
    /// </summary>
    public static List<(string Text, int LineNumber)> NonBlankLines(string text)
    {
        var lines = new List<string>();
        using (var reader = new StringReader(text))
        {
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }
        }

        var result = new List<(string Text, int LineNumber)>();
        var index = 0;

        while (index < lines.Count)
        {
            // How many lines this record spans: one, unless it ends inside
            // a quoted field and a later line closes it.
            var span = 1;
            var inQuotes = EndsInQuotes(lines[index], false);

            while (inQuotes && index + span < lines.Count)
            {
                inQuotes = EndsInQuotes(lines[index + span], true);
                span++;
            }

            if (inQuotes)
            {
                // Never closed: not a field over several lines after all.
                span = 1;
            }

            var record = span == 1
                ? lines[index]
                : string.Join(Environment.NewLine, lines.GetRange(index, span));

            if (!string.IsNullOrWhiteSpace(record))
            {
                result.Add((record, index + 1));
            }

            index += span;
        }

        return result;
    }

    /// <summary>
    /// Whether a line of CSV ends inside a quoted field - given whether it
    /// starts inside one (the line before ended in it). A quote opens a
    /// field only at its start: the start of the line, or after a comma,
    /// semicolon or tab (any of the separators an import accepts), spaces
    /// aside. "" inside quotes is a quote, not the end.
    /// </summary>
    private static bool EndsInQuotes(string line, bool inQuotes)
    {
        var atFieldStart = !inQuotes;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
            }
            else if (c == '"' && atFieldStart)
            {
                inQuotes = true;
                atFieldStart = false;
            }
            else if (c is ',' or ';' or '\t')
            {
                atFieldStart = true;
            }
            else if (c != ' ')
            {
                atFieldStart = false;
            }
        }

        return inQuotes;
    }

    /// <summary>
    /// Parses one line of CSV into fields, handling quoted fields (with
    /// embedded commas/escaped quotes). A field with a line break in it is
    /// parsed too when given the whole record (see NonBlankLines, which
    /// keeps one together) - adequate for simple tabular exports like
    /// driver rosters, not a full RFC 4180 parser.
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
