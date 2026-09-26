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
