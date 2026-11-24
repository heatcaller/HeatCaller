using System.Globalization;
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

        // A title on the first line ("10th Scale" over a class's rows) has
        // no separator in it, so the separator is found from the first line
        // with a tab when there is one near the top - a browser or a
        // spreadsheet copy is always tab-separated.
        var tabbed = lines.Take(10).ToList().FindIndex(line => line.Text.Contains('\t'));
        var delimiter = DriverImport.DetectDelimiter(lines[Math.Max(tabbed, 0)].Text);
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
        /// <summary>Where the table opened in the page, so tables joined together keep the page's order.</summary>
        public int Order { get; init; }
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
        var opened = 0;

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
                    open.Push(new TableBuilder { Order = opened++ });
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

        // WHY (1 Nov 2026, asked for by Asher): a LiveRC entry list is one
        // table per class, each with the same headings. Taking only the
        // biggest brought in one class of eleven. Every table with the
        // biggest one's headings is read with it, in the page's order; a
        // menu or sidebar table has other headings and is still left out.
        var signature = HeadingSignature(best);
        var chosen = signature is null
            ? new List<TableBuilder> { best }
            : finished.Where(t => t == best || HeadingSignature(t) == signature).OrderBy(t => t.Order).ToList();

        var grid = new List<List<string>>();
        var marked = new List<bool>();

        foreach (var row in chosen.SelectMany(t => t.Rows))
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

    /// <summary>
    /// A table's headings as they are compared with another table's: its
    /// first row of two or more cells with words in, when that row is
    /// headings. Null when the table has no such row, so it is never joined
    /// to another.
    /// </summary>
    private static string? HeadingSignature(TableBuilder table)
    {
        foreach (var row in table.Rows)
        {
            var cells = row.Cells.Select(c => Tidy(WebUtility.HtmlDecode(c.Text.ToString()))).ToList();

            if (cells.Count(c => c.Length > 0) < 2)
            {
                continue;
            }

            return row.IsHeadings || LooksLikeHeadings(cells)
                ? string.Join("|", cells.Where(c => c.Length > 0).Select(DriverImport.Normalise))
                : null;
        }

        return null;
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
        // cell further down. A title that is a class with its entry count
        // ("10th Scale Entries: 16", as LiveRC heads each class) is kept
        // for the rows under it.
        var start = 0;
        var classTitle = (string?)null;
        var lastTitle = (string?)null;
        while (start < kept.Count - 1 && IsTitleRow(kept[start].Cells, width))
        {
            classTitle = ClassFromTitle(kept[start].Cells, lastTitle) ?? classTitle;
            lastTitle = kept[start].Cells.First(c => c.Length > 0);
            start++;
        }

        var first = kept[start];
        var hasHeadings = first.Marked || LooksLikeHeadings(first.Cells);

        var headers = hasHeadings
            ? first.Cells.Select((cell, i) => cell.Length > 0 ? cell : $"Column {i + 1}").ToList()
            : Enumerable.Range(1, width).Select(i => $"Column {i}").ToList();

        var rows = new List<List<string>>();
        var rowNumbers = new List<int>();
        var rowClasses = new List<string?>();

        for (var i = hasHeadings ? start + 1 : start; i < kept.Count; i++)
        {
            var (cells, isMarked, number) = kept[i];

            // WHY (23 Nov 2026, bug hunt 24): any row with one cell filled in
            // was taken for a title and left out without a word, so a new
            // racer with only a name (no transponder or car yet) vanished
            // from a pasted list that the same sheet as a CSV file kept - and
            // in a class-by-class list the rows after them lost their class.
            // A lone name under the name heading, with an ordinary row after
            // it, is a driver; titles ("Modified" over repeated headings, a
            // class with its count, a race name under "Pos") still go.
            var nextCells = i + 1 < kept.Count ? kept[i + 1].Cells : null;
            var nextMarked = i + 1 < kept.Count && kept[i + 1].Marked;
            if (IsTitleRow(cells, width)
                && !IsNameOnlyDriver(cells, isMarked, hasHeadings ? headers : null, first.Cells, lastTitle,
                    nextCells, nextMarked, width))
            {
                classTitle = ClassFromTitle(cells, lastTitle) ?? (classTitle is null ? null : string.Empty);
                lastTitle = cells.First(c => c.Length > 0);
                continue;
            }

            lastTitle = null;

            // The headings again, part way down - a results page repeats
            // them for every class - or another row marked as headings.
            if (hasHeadings && (isMarked || cells.SequenceEqual(first.Cells, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            rows.Add(cells);
            rowNumbers.Add(number);
            rowClasses.Add(classTitle);
        }

        if (rows.Count == 0)
        {
            return null;
        }

        if (rowClasses.Any(c => !string.IsNullOrEmpty(c)))
        {
            TidyClassList(headers, rows, rowClasses);
        }

        return new Table(headers, rows, rowNumbers, fromWebPage);
    }

    // ------------------------------------------------------------------
    //  A LiveRC entry list (1 Nov 2026, asked for by Asher): one table per
    //  class headed "10th Scale Entries: 16", columns "#", "Driver" and
    //  "Transponder #", and each name in the page twice - "BLACK, KALEB"
    //  and "KALEB BLACK" side by side in one cell, so a copy reads as
    //  "BLACK, KALEBKALEB BLACK". Nothing here names LiveRC: any list laid
    //  out the same way is read the same way.
    // ------------------------------------------------------------------

    private static readonly Regex EntryCount = new(
        @"^(?<name>.*?)\s*[-–(]?\s*(?:(?:entries|entry|drivers|racers|entrants)\s*[:\-]?\s*\d+|\d+\s+(?:entries|entry|drivers|racers|entrants))\s*\)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// The class a title row names, when it is a class with its entry count:
    /// "10th Scale Entries: 16" is "10th Scale", "Stock (12 entries)" is
    /// "Stock". A title that is only the count ("Entries: 16", as a copy
    /// made as plain text puts it on a line of its own) names the title
    /// just above it, <paramref name="titleAbove"/>. Null for any other
    /// title - "Mini-Z Stock - A-Main" is a race, not a class list.
    /// </summary>
    internal static string? ClassFromTitle(IReadOnlyList<string> cells, string? titleAbove = null)
    {
        var title = cells.FirstOrDefault(c => c.Length > 0);

        if (title is null)
        {
            return null;
        }

        try
        {
            var match = EntryCount.Match(title);
            if (!match.Success)
            {
                return null;
            }

            var name = match.Groups["name"].Value.Trim().TrimEnd('-', '–', ':', ',').Trim();

            if (name.Length > 0)
            {
                return name;
            }

            return string.IsNullOrWhiteSpace(titleAbove) ? null : titleAbove.Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// A list read class by class: the class each row sat under becomes a
    /// Class column (when the table has none of its own), the doubled names
    /// are put back to one, and a "#" column that only counts the rows of
    /// each class is renamed so it isn't taken for race numbers.
    /// </summary>
    private static void TidyClassList(List<string> headers, List<List<string>> rows, List<string?> rowClasses)
    {
        var nameColumn = headers.FindIndex(h => DriverImport.Guess(h) == ImportColumn.DriverName);

        if (nameColumn >= 0)
        {
            foreach (var row in rows)
            {
                if (UndoubledName(row[nameColumn]) is { } name)
                {
                    row[nameColumn] = name;
                }
            }
        }

        var countColumn = headers.FindIndex(h => DriverImport.Guess(h) == ImportColumn.RaceNumber);

        if (countColumn >= 0 && OnlyCountsRows(rows.Select(r => r[countColumn]).ToList()))
        {
            headers[countColumn] = RowCountHeading;
        }

        if (!headers.Any(h => DriverImport.Guess(h) == ImportColumn.ClassName))
        {
            headers.Add(ClassHeading);

            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].Add(rowClasses[i] ?? string.Empty);
            }
        }
    }

    /// <summary>The heading of the Class column made from the tables' titles.</summary>
    internal const string ClassHeading = "Class";

    /// <summary>What a "#" column that only counts rows is renamed - nothing the import guesses.</summary>
    internal const string RowCountHeading = "Row count";

    /// <summary>Whether a column is 1, 2, 3 ... starting again at 1 for each class - a count, not race numbers.</summary>
    private static bool OnlyCountsRows(IReadOnlyList<string> cells)
    {
        var previous = 0;

        foreach (var cell in cells)
        {
            if (!int.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                return false;
            }

            if (n != 1 && n != previous + 1)
            {
                return false;
            }

            previous = n;
        }

        return cells.Count > 0;
    }

    /// <summary>
    /// One name from a cell that has it twice, "Last, First" then "First
    /// Last" ("BLACK, KALEBKALEB BLACK" or "BLACK, KALEB KALEB BLACK"), as
    /// "First Last" - in ordinary capitals when the page had it all in
    /// capitals ("Kaleb Black"). Null when the cell isn't that shape, so
    /// "Smith, Jr." and every ordinary name are left as they are.
    /// </summary>
    internal static string? UndoubledName(string cell)
    {
        var comma = cell.IndexOf(", ", StringComparison.Ordinal);

        if (comma <= 0)
        {
            return null;
        }

        var last = cell[..comma].Trim();
        var rest = cell[(comma + 2)..].Trim();
        var suffix = " " + last;

        if (last.Length == 0 || !rest.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var middle = rest[..^suffix.Length].Trim();
        string? first = null;

        if (middle.Length >= 2 && middle.Length % 2 == 0
            && string.Equals(middle[..(middle.Length / 2)], middle[(middle.Length / 2)..], StringComparison.OrdinalIgnoreCase))
        {
            first = middle[..(middle.Length / 2)];
        }
        else if (middle.Length >= 3 && middle.Length % 2 == 1 && middle[middle.Length / 2] == ' '
                 && string.Equals(middle[..(middle.Length / 2)], middle[(middle.Length / 2 + 1)..], StringComparison.OrdinalIgnoreCase))
        {
            first = middle[..(middle.Length / 2)];
        }

        if (string.IsNullOrWhiteSpace(first))
        {
            return null;
        }

        var name = first.Trim() + " " + last;
        return name.Any(char.IsLower) ? name : OrdinaryCapitals(name);
    }

    /// <summary>
    /// A name in capitals as a name is usually written: "KALEB BLACK" is
    /// "Kaleb Black", "MARY-ANN O'BRIEN" is "Mary-Ann O'Brien", "MCDONALD"
    /// is "McDonald", and "JR", "II", "III" and "IV" keep their usual form.
    /// The announcer reads either the same; this is for the screens.
    /// </summary>
    internal static string OrdinaryCapitals(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var w = 0; w < words.Length; w++)
        {
            var word = words[w];
            var bare = word.TrimEnd('.', ',');

            if (bare is "II" or "III" or "IV")
            {
                continue;
            }

            var chars = word.ToLowerInvariant().ToCharArray();
            var startOfPart = true;

            for (var i = 0; i < chars.Length; i++)
            {
                if (char.IsLetter(chars[i]))
                {
                    if (startOfPart)
                    {
                        chars[i] = char.ToUpperInvariant(chars[i]);
                        startOfPart = false;
                    }
                }
                else
                {
                    startOfPart = chars[i] is '-' or '\'';
                }
            }

            var result = new string(chars);

            if (result.Length > 2 && result.StartsWith("Mc", StringComparison.Ordinal) && char.IsLetter(result[2]))
            {
                result = "Mc" + char.ToUpperInvariant(result[2]) + result[3..];
            }

            words[w] = result;
        }

        return string.Join(" ", words);
    }

    /// <summary>One cell with words in it across a table three or more wide: a title, not a driver.</summary>
    private static bool IsTitleRow(List<string> cells, int width) =>
        width >= 3 && cells.Count(cell => cell.Length > 0) == 1;

    /// <summary>
    /// WHY (23 Nov 2026, bug hunt 24): whether a row with one cell filled in
    /// is a driver with only a name rather than a title. It is when the table
    /// has headings, the cell sits under the driver's name heading (Driver,
    /// Name, First name, Last name), it is not marked as headings, it is not
    /// a class with its entry count, and the row after it is an ordinary row
    /// - not the headings again, not marked as headings, not another lone
    /// cell. A title over a class ("Modified") is followed by the headings
    /// again, so it still goes; a driver at the very end has no row after it
    /// and is kept, where the old rule left them out without a word.
    /// </summary>
    private static bool IsNameOnlyDriver(
        List<string> cells,
        bool marked,
        IReadOnlyList<string>? headers,
        List<string> headingCells,
        string? lastTitle,
        List<string>? nextCells,
        bool nextMarked,
        int width)
    {
        if (headers is null || marked)
        {
            return false;
        }

        var column = cells.FindIndex(cell => cell.Length > 0);

        if (column < 0 || column >= headers.Count
            || DriverImport.Guess(headers[column]) is not (ImportColumn.DriverName or ImportColumn.FirstName or ImportColumn.LastName))
        {
            return false;
        }

        if (ClassFromTitle(cells, lastTitle) is not null)
        {
            return false;
        }

        if (nextCells is null)
        {
            return true;
        }

        return !nextMarked
               && !IsTitleRow(nextCells, width)
               && !nextCells.SequenceEqual(headingCells, StringComparer.OrdinalIgnoreCase);
    }

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
