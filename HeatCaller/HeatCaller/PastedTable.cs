using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// A driver list pasted rather than opened (22 Oct 2026): the table a club
/// copies off its old timing software's web page, or out of a spreadsheet,
/// read off the clipboard into headings and rows for the same column
/// preview (CsvMappingForm) and the same import the CSV file goes through.
///
/// WHY THE HTML FIRST. A browser puts a copied table on the clipboard twice:
/// as HTML, cell by cell, and as plain text with a tab between cells. The
/// plain text is usually fine, but a cell with a line break in it (a name
/// over two lines, "Team<br>Sponsor") splits the row in two, and a cell
/// spanning two columns shifts everything after it one to the left. The
/// HTML has the cells as cells, so it is read first, and the text only when
/// there is no table in it - which is what Excel and Google Sheets copies
/// fall back to anyway, as tab-separated lines.
///
/// What it does with a messy page, because results pages are messy:
/// - the biggest table wins when the copy took in more than one (a page's
///   layout table around the results, a little one in the sidebar);
/// - a cell spanning columns keeps its text in the first and leaves the
///   rest blank, so every column after it stays under its heading;
/// - links, bold and the like are read as their words; a line break in a
///   cell becomes a space; scripts and styles are dropped;
/// - blank rows, a heading row repeated part way down (results pages do
///   that for every class), and a row with one cell in it across a wide
///   table (a class title, "A-Main") are left out;
/// - the first row is the headings when it is marked as headings, or when
///   any of it is a heading the import knows, or when none of it is a
///   number - otherwise the table has no headings and its columns are
///   named "Column 1", "Column 2" and so on, for the preview to be told
///   what each one is.
///
/// It reads the clipboard's words and nothing else: no link is followed and
/// nothing is fetched.
/// </summary>
internal static class PastedTable
{
    /// <summary>The table, ready for the column preview.</summary>
    /// <param name="Headers">One per column.</param>
    /// <param name="Rows">The data rows, each as wide as the headings.</param>
    /// <param name="RowNumbers">Each data row's place in the table as pasted (the headings are row 1), for "Row 12" in the summary.</param>
    /// <param name="FromWebPage">True when it was read from the HTML a browser put on the clipboard.</param>
    internal sealed record Table(
        IReadOnlyList<string> Headers,
        IReadOnlyList<List<string>> Rows,
        IReadOnlyList<int> RowNumbers,
        bool FromWebPage);

    /// <summary>
    /// The table on the clipboard - from its HTML when that has a table in
    /// it, otherwise from its plain text. Null when neither holds at least a
    /// row of data.
    /// </summary>
    internal static Table? Read(string? html, string? text)
    {
        if (!string.IsNullOrWhiteSpace(html))
        {
            try
            {
                if (FromHtml(html) is { } fromHtml)
                {
                    return fromHtml;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // A page too big or too tangled to take apart in time - the
                // plain text the browser put beside it is still there.
            }
        }

        return string.IsNullOrWhiteSpace(text) ? null : FromText(text);
    }

    // ------------------------------------------------------------------
    //  Plain text: tab-separated from a browser or a spreadsheet, or a
    //  CSV pasted as text - read exactly as the CSV file is.
    // ------------------------------------------------------------------

    /// <summary>
    /// Lines of text as a table: the separator found the way the CSV import
    /// finds it (a tab, a semicolon or a comma), quotes honoured.
    /// </summary>
    internal static Table? FromText(string text)
    {
        var lines = CsvHelper.NonBlankLines(text);

        if (lines.Count == 0)
        {
            return null;
        }

        var delimiter = DriverImport.DetectDelimiter(lines[0].Text);
        var grid = lines.Select(line => DriverImport.ParseLine(line.Text, delimiter).Select(Tidy).ToList()).ToList();
        var marked = new bool[grid.Count];

        return Shape(grid, marked, lines.Select(line => line.LineNumber).ToList(), fromWebPage: false);
    }

    // ------------------------------------------------------------------
    //  HTML: the cells as the page had them.
    // ------------------------------------------------------------------

    private static readonly Regex Tag = new(
        @"<!--.*?-->|<(?<close>/)?(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>[^>]*)>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex ColspanAttribute = new(
        @"\bcolspan\s*=\s*[""']?(?<n>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>A colspan this wide is somebody's layout, not a column count.</summary>
    private const int MaxColspan = 50;

    private sealed class Cell
    {
        public StringBuilder Text { get; } = new();
        public int Span { get; init; } = 1;
    }

    private sealed class RowBuilder
    {
        public List<Cell> Cells { get; } = new();
        public bool InHead { get; init; }
        public int HeadingCells { get; set; }

        /// <summary>
        /// Headings: inside a thead, or every cell a th. A th at the start
        /// of a row and td after it is the row's own label, not headings.
        /// </summary>
        public bool IsHeadings => InHead || (Cells.Count > 0 && HeadingCells == Cells.Count);
    }

    private sealed class TableBuilder
    {
        public List<RowBuilder> Rows { get; } = new();
        public bool InHead { get; set; }
        public RowBuilder? Row { get; set; }
        public Cell? Cell { get; set; }
    }

    /// <summary>
    /// The biggest table in some HTML - the CF_HTML header Windows puts in
    /// front of it is text outside any table, so it is passed over. Null
    /// when there is no table with a data row in it.
    /// </summary>
    internal static Table? FromHtml(string html)
    {
        html = RepairUtf8(html);

        var finished = new List<TableBuilder>();
        var open = new Stack<TableBuilder>();
        var skipping = (string?)null;
        var at = 0;

        void AddText(string raw)
        {
            if (skipping is null && open.Count > 0 && open.Peek().Cell is { } cell)
            {
                cell.Text.Append(raw);
            }
        }

        foreach (Match match in Tag.Matches(html))
        {
            AddText(html[at..match.Index]);
            at = match.Index + match.Length;

            if (match.Value.StartsWith("<!--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = match.Groups["name"].Value.ToLowerInvariant();
            var closing = match.Groups["close"].Success;

            if (skipping is not null)
            {
                if (closing && name == skipping)
                {
                    skipping = null;
                }

                continue;
            }

            if (!closing && name is "script" or "style" or "template")
            {
                skipping = name;
                continue;
            }

            var table = open.Count > 0 ? open.Peek() : null;

            switch (name)
            {
                case "table" when !closing:
                    open.Push(new TableBuilder());
                    break;

                case "table":
                    if (open.Count > 0)
                    {
                        finished.Add(open.Pop());
                    }

                    break;

                case "thead" when table is not null:
                    table.InHead = !closing;
                    break;

                case "tbody" or "tfoot" when table is not null && !closing:
                    table.InHead = false;
                    break;

                case "tr" when table is not null:
                    table.Cell = null;
                    table.Row = null;

                    if (!closing)
                    {
                        table.Row = new RowBuilder { InHead = table.InHead };
                        table.Rows.Add(table.Row);
                    }

                    break;

                case "td" or "th" when table is not null:
                    table.Cell = null;

                    if (!closing)
                    {
                        // A cell with no <tr> before it - some pages leave
                        // the first one out - starts a row of its own.
                        if (table.Row is null)
                        {
                            table.Row = new RowBuilder { InHead = table.InHead };
                            table.Rows.Add(table.Row);
                        }

                        var span = ColspanAttribute.Match(match.Groups["attrs"].Value) is { Success: true } s
                                   && int.TryParse(s.Groups["n"].Value, out var n)
                            ? Math.Clamp(n, 1, MaxColspan)
                            : 1;

                        table.Cell = new Cell { Span = span };
                        table.Row.Cells.Add(table.Cell);

                        if (name == "th")
                        {
                            table.Row.HeadingCells++;
                        }
                    }

                    break;

                case "br" or "p" or "div" or "li":
                    AddText(" ");
                    break;
            }
        }

        AddText(html[at..]);

        // A table left open by a fragment cut short still counts.
        while (open.Count > 0)
        {
            finished.Add(open.Pop());
        }

        var best = finished
            .Select(t => (Table: t, Size: t.Rows.Sum(r => r.Cells.Count)))
            .Where(t => t.Table.Rows.Count >= 1)
            .OrderByDescending(t => t.Size)
            .Select(t => t.Table)
            .FirstOrDefault();

        if (best is null)
        {
            return null;
        }

        var grid = new List<List<string>>();
        var marked = new List<bool>();

        foreach (var row in best.Rows)
        {
            var cells = new List<string>();

            foreach (var cell in row.Cells)
            {
                cells.Add(Tidy(WebUtility.HtmlDecode(cell.Text.ToString())));

                for (var i = 1; i < cell.Span; i++)
                {
                    cells.Add(string.Empty);
                }
            }

            grid.Add(cells);
            marked.Add(row.IsHeadings);
        }

        var numbers = Enumerable.Range(1, grid.Count).ToList();

        return Shape(grid, marked.ToArray(), numbers, fromWebPage: true);
    }

    // ------------------------------------------------------------------
    //  The shared part: headings, and which rows are drivers.
    // ------------------------------------------------------------------

    /// <summary>
    /// Rows of cells into headings and data rows: blank rows out, the
    /// headings found, repeats of the headings and lone title cells out,
    /// every row as wide as the widest.
    /// </summary>
    private static Table? Shape(List<List<string>> grid, bool[] marked, IReadOnlyList<int> numbers, bool fromWebPage)
    {
        var kept = new List<(List<string> Cells, bool Marked, int Number)>();

        for (var i = 0; i < grid.Count; i++)
        {
            if (grid[i].Any(cell => cell.Length > 0))
            {
                kept.Add((grid[i], i < marked.Length && marked[i], numbers[i]));
            }
        }

        if (kept.Count == 0)
        {
            return null;
        }

        var width = kept.Max(row => row.Cells.Count);

        foreach (var row in kept)
        {
            while (row.Cells.Count < width)
            {
                row.Cells.Add(string.Empty);
            }
        }

        // Trailing columns nobody wrote anything in are no use to the preview.
        while (width > 1 && kept.All(row => row.Cells[width - 1].Length == 0))
        {
            width--;

            foreach (var row in kept)
            {
                row.Cells.RemoveAt(width);
            }
        }

        // A class title across a wide table ("Mini-Z Stock - A-Main") before
        // the headings - skip past it to the headings, the same as any lone
        // cell further down.
        var start = 0;
        while (start < kept.Count - 1 && IsTitleRow(kept[start].Cells, width))
        {
            start++;
        }

        var first = kept[start];
        var hasHeadings = first.Marked || LooksLikeHeadings(first.Cells);

        var headers = hasHeadings
            ? first.Cells.Select((cell, i) => cell.Length > 0 ? cell : $"Column {i + 1}").ToList()
            : Enumerable.Range(1, width).Select(i => $"Column {i}").ToList();

        var rows = new List<List<string>>();
        var rowNumbers = new List<int>();

        for (var i = hasHeadings ? start + 1 : start; i < kept.Count; i++)
        {
            var (cells, isMarked, number) = kept[i];

            if (IsTitleRow(cells, width))
            {
                continue;
            }

            // The headings again, part way down - a results page repeats
            // them for every class - or another row marked as headings.
            if (hasHeadings && (isMarked || cells.SequenceEqual(first.Cells, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            rows.Add(cells);
            rowNumbers.Add(number);
        }

        return rows.Count == 0 ? null : new Table(headers, rows, rowNumbers, fromWebPage);
    }

    /// <summary>One cell with words in it across a table three or more wide: a title, not a driver.</summary>
    private static bool IsTitleRow(List<string> cells, int width) =>
        width >= 3 && cells.Count(cell => cell.Length > 0) == 1;

    /// <summary>
    /// Whether a first row is headings: any of it a heading the import
    /// knows, or none of it a number. "Billy Smith | 1234567" is a driver;
    /// "Driver | Transponder" and "Name | Club" are headings.
    /// </summary>
    internal static bool LooksLikeHeadings(IReadOnlyList<string> cells)
    {
        if (cells.Any(cell => cell.Length > 0 && DriverImport.Guess(cell) != ImportColumn.Ignore))
        {
            return true;
        }

        return !cells.Any(IsNumberish);
    }

    private static bool IsNumberish(string cell)
    {
        var digits = cell.Count(char.IsDigit);
        return digits > 0 && digits * 2 >= cell.Count(c => !char.IsWhiteSpace(c));
    }

    /// <summary>Runs of whitespace one space, a non-breaking space a space, the ends trimmed.</summary>
    private static string Tidy(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == ' ')
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The clipboard's HTML is UTF-8, and a reader that took it for the
    /// Windows code page turns "José" into "JosÃ©". Put back when the text
    /// has the tell-tale pairs and reading it again as UTF-8 comes out
    /// clean; left alone otherwise.
    /// </summary>
    internal static string RepairUtf8(string html)
    {
        if (html.IndexOf('Ã') < 0 && html.IndexOf('Â') < 0 && html.IndexOf("â€", StringComparison.Ordinal) < 0)
        {
            return html;
        }

        try
        {
            var latin1 = Encoding.Latin1;
            var cp1252 = CodePages.Windows1252;

            foreach (var encoding in new[] { cp1252, latin1 })
            {
                if (encoding is null)
                {
                    continue;
                }

                var bytes = encoding.GetBytes(html);

                if (!encoding.GetString(bytes).Equals(html, StringComparison.Ordinal))
                {
                    continue;
                }

                var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
                return strict.GetString(bytes);
            }
        }
        catch (DecoderFallbackException)
        {
        }
        catch (ArgumentException)
        {
        }

        return html;
    }

    /// <summary>Windows-1252 where the platform has it (the code-pages provider), else null.</summary>
    private static class CodePages
    {
        internal static readonly Encoding? Windows1252 = Load();

        private static Encoding? Load()
        {
            try
            {
                // WHY (22 Oct 2026): Encoding.GetEncoding(1252) throws on .NET
                // until someone registers the code-pages provider, and only the
                // CSV import and network setup did - so a paste before either
                // cached null here for the session and the repair fell back to
                // Latin-1, which cannot hold the euro and curly-quote characters
                // a mis-read apostrophe or "É" turns into, and gave up on the
                // whole page. Asking the provider directly needs no registration.
                return CodePagesEncodingProvider.Instance.GetEncoding(1252);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
