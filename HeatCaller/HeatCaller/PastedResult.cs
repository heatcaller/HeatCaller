using System.Globalization;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// A race result pasted into Race History (1 Nov 2026, asked for by Asher):
/// the result table a club copies off its old timing software's results
/// page - LiveRC's "Pos | Driver | Qual | Laps/Time | Behind | Fastest Lap"
/// is the one it is made for - read by Paste a Table's reader
/// (<see cref="PastedTable"/>) and turned here into finishing order, laps,
/// total time and fast lap for each driver.
/// </summary>
/// <remarks>
/// What a pasted result can't carry: the lap-by-lap times (LiveRC keeps
/// them out of the table) and transponders (its result pages don't show
/// them). Drivers are matched to profiles by name; one that isn't matched
/// still comes in, by name alone. Every result read here is filed as
/// imported (<see cref="SessionRecord.ImportedFrom"/>), so it never scores.
/// Nothing is fetched: only the clipboard is read.
/// </remarks>
internal static class PastedResult
{
    /// <summary>One driver's line of a pasted result.</summary>
    internal sealed record Row(int Position, string Name, int Laps, long ElapsedMs, long FastLapMs, string Transponder);

    /// <summary>A pasted result: its drivers in finishing order, and what couldn't be read.</summary>
    /// <param name="Rows">Finishing order, first place first.</param>
    /// <param name="Problems">Rows left out and why, by their place in the table ("Row 6 has no driver").</param>
    internal sealed record Race(IReadOnlyList<Row> Rows, IReadOnlyList<string> Problems);

    private enum Column
    {
        Position,
        Driver,
        LapsAndTime,
        Laps,
        Time,
        FastLap,
        Transponder,
    }

    private static readonly (Column Column, string[] Headings)[] Headings =
    {
        (Column.Position, new[] { "pos", "position", "place", "finish", "fin", "p", "rank" }),
        (Column.Driver, new[] { "driver", "driver name", "name", "racer", "competitor", "entrant" }),
        (Column.LapsAndTime, new[] { "laps time", "laps and time", "result", "laps time behind" }),
        (Column.Laps, new[] { "laps", "total laps", "lap count", "l" }),
        (Column.Time, new[] { "time", "total time", "race time", "elapsed", "elapsed time", "total" }),
        (Column.FastLap, new[] { "fastest lap", "fastest lap lap #", "fast lap", "best lap", "fastest", "best", "fast", "best lap lap #" }),
        (Column.Transponder, new[] { "transponder", "transponder #", "tx", "transponder number" }),
    };

    /// <summary>
    /// The columns a pasted result has, by heading - each one the first
    /// column it fits. A column whose heading fits nothing ("Qual", "Behind",
    /// "Avg Lap", "Consistency") is passed over.
    /// </summary>
    private static Dictionary<Column, int> FindColumns(IReadOnlyList<string> headers)
    {
        var found = new Dictionary<Column, int>();

        for (var i = 0; i < headers.Count; i++)
        {
            var key = DriverImport.Normalise(headers[i]);

            foreach (var (column, names) in Headings)
            {
                if (!found.ContainsKey(column) && names.Contains(key))
                {
                    found[column] = i;
                    break;
                }
            }
        }

        return found;
    }

    /// <summary>Whether a pasted table has what a result needs: a driver column, and laps or a total time.</summary>
    internal static bool LooksLikeAResult(IReadOnlyList<string> headers)
    {
        var columns = FindColumns(headers);
        return columns.ContainsKey(Column.Driver)
               && (columns.ContainsKey(Column.LapsAndTime) || columns.ContainsKey(Column.Laps) || columns.ContainsKey(Column.Time));
    }

    /// <summary>
    /// The result in a pasted table, or null when the table isn't a result
    /// (<see cref="LooksLikeAResult"/>). Rows are put in finishing order by
    /// their Pos cell, or kept in the table's order where there is none.
    /// </summary>
    internal static Race? Read(PastedTable.Table table)
    {
        if (!LooksLikeAResult(table.Headers))
        {
            return null;
        }

        var columns = FindColumns(table.Headers);
        string Cell(List<string> row, Column column) =>
            columns.TryGetValue(column, out var i) && i < row.Count ? row[i] : string.Empty;

        var rows = new List<(Row Row, int Order)>();
        var problems = new List<string>();

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var cells = table.Rows[r];
            var where = r < table.RowNumbers.Count ? $"Row {table.RowNumbers[r]}" : $"Row {r + 1}";
            var name = DriverName(Cell(cells, Column.Driver));

            if (name.Length == 0)
            {
                problems.Add($"{where} has no driver, so it was left out.");
                continue;
            }

            var laps = 0;
            var elapsed = 0L;

            if (columns.ContainsKey(Column.LapsAndTime))
            {
                TryLapsAndTime(Cell(cells, Column.LapsAndTime), out laps, out elapsed);
            }

            if (columns.ContainsKey(Column.Laps) && int.TryParse(FirstWord(Cell(cells, Column.Laps)), NumberStyles.None, CultureInfo.InvariantCulture, out var lapsOnly))
            {
                laps = lapsOnly;
            }

            if (columns.ContainsKey(Column.Time) && TryTime(Cell(cells, Column.Time), out var timeOnly))
            {
                elapsed = timeOnly;
            }

            TryTime(Cell(cells, Column.FastLap), out var fast);

            var position = int.TryParse(FirstWord(Cell(cells, Column.Position)).TrimEnd('.'), NumberStyles.None, CultureInfo.InvariantCulture, out var pos) && pos > 0
                ? pos
                : 0;

            rows.Add((new Row(position, name, laps, elapsed, fast, Cell(cells, Column.Transponder).Trim()), r));
        }

        // Finishing order: by Pos where the table has one, the table's own
        // order otherwise and for a row with none; then numbered from 1, so
        // two rows that both said "1" don't both finish first.
        var ordered = rows
            .OrderBy(x => x.Row.Position == 0 ? int.MaxValue : x.Row.Position)
            .ThenBy(x => x.Order)
            .Select((x, i) => x.Row with { Position = i + 1 })
            .ToList();

        return new Race(ordered, problems);
    }

    private static string FirstWord(string cell) =>
        cell.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private static readonly Regex LapsSlashTime = new(
        @"^\s*(?<laps>\d+)\s*/\s*(?<time>\S+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex TimeToken = new(
        @"\d+(?::\d{1,2}){0,2}(?:\.\d+)?",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>"18/10:10.631" - laps, then the total time - as LiveRC's Laps/Time column has it.</summary>
    internal static bool TryLapsAndTime(string cell, out int laps, out long elapsedMs)
    {
        laps = 0;
        elapsedMs = 0;

        var match = LapsSlashTime.Match(cell);

        if (!match.Success)
        {
            return int.TryParse(FirstWord(cell), NumberStyles.None, CultureInfo.InvariantCulture, out laps);
        }

        laps = int.Parse(match.Groups["laps"].Value, CultureInfo.InvariantCulture);
        TryTime(match.Groups["time"].Value, out elapsedMs);
        return true;
    }

    /// <summary>
    /// A time as a results page writes it - "31.234", "1:02.5", "10:10.631",
    /// "1:05:00.000", or "31.234 (5)" with the lap it was set on after it -
    /// in milliseconds. The first time in the cell is the one read. False,
    /// and 0, for a cell with none.
    /// </summary>
    internal static bool TryTime(string cell, out long ms)
    {
        ms = 0;

        if (string.IsNullOrWhiteSpace(cell))
        {
            return false;
        }

        var match = TimeToken.Match(cell);

        if (!match.Success)
        {
            return false;
        }

        var parts = match.Value.Split(':');
        var secondsText = parts[^1];
        var dot = secondsText.IndexOf('.');
        var whole = dot < 0 ? secondsText : secondsText[..dot];
        var fraction = dot < 0 ? string.Empty : secondsText[(dot + 1)..];

        if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        long total = seconds * 1000;

        if (fraction.Length > 0)
        {
            total += long.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        }

        long multiplier = 60_000;

        for (var i = parts.Length - 2; i >= 0; i--)
        {
            if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var unit))
            {
                return false;
            }

            total += unit * multiplier;
            multiplier *= 60;
        }

        ms = total;
        return true;
    }

    /// <summary>
    /// A driver cell as a name: a car number in front taken off ("4 COLTON
    /// FURNARI" is "Colton Furnari"), a name given twice put back to one
    /// ("FURNARI, COLTONCOLTON FURNARI"), and a name all in capitals written
    /// the usual way.
    /// </summary>
    internal static string DriverName(string cell)
    {
        var name = cell.Trim();
        var space = name.IndexOf(' ');

        if (space > 0 && name[..space].All(char.IsDigit) && name[(space + 1)..].Any(char.IsLetter))
        {
            name = name[(space + 1)..].Trim();
        }

        name = Undoubled(name) ?? name;

        return name.Any(char.IsLetter) && !name.Any(char.IsLower) ? OrdinaryCapitals(name) : name;
    }

    /// <summary>"Last, First" then "First Last" in one cell, as "First Last". Null for any other name.</summary>
    private static string? Undoubled(string cell)
    {
        var comma = cell.IndexOf(", ", StringComparison.Ordinal);

        if (comma <= 0)
        {
            return null;
        }

        var last = cell[..comma].Trim();
        var rest = cell[(comma + 2)..].Trim();
        var suffix = " " + last;

        if (!rest.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var middle = rest[..^suffix.Length].Trim();
        var half = middle.Length / 2;

        if (middle.Length >= 2 && middle.Length % 2 == 0 && string.Equals(middle[..half], middle[half..], StringComparison.OrdinalIgnoreCase))
        {
            return middle[..half] + " " + last;
        }

        if (middle.Length >= 3 && middle.Length % 2 == 1 && middle[half] == ' '
            && string.Equals(middle[..half], middle[(half + 1)..], StringComparison.OrdinalIgnoreCase))
        {
            return middle[..half] + " " + last;
        }

        return null;
    }

    /// <summary>"COLTON MCDONALD-O'BRIEN" as "Colton McDonald-O'Brien"; "II", "III" and "IV" kept.</summary>
    internal static string OrdinaryCapitals(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var w = 0; w < words.Length; w++)
        {
            if (words[w].TrimEnd('.', ',') is "II" or "III" or "IV")
            {
                continue;
            }

            var chars = words[w].ToLowerInvariant().ToCharArray();
            var start = true;

            for (var i = 0; i < chars.Length; i++)
            {
                if (char.IsLetter(chars[i]))
                {
                    if (start)
                    {
                        chars[i] = char.ToUpperInvariant(chars[i]);
                        start = false;
                    }
                }
                else
                {
                    start = chars[i] is '-' or '\'';
                }
            }

            var word = new string(chars);

            if (word.Length > 2 && word.StartsWith("Mc", StringComparison.Ordinal) && char.IsLetter(word[2]))
            {
                word = "Mc" + char.ToUpperInvariant(word[2]) + word[3..];
            }

            words[w] = word;
        }

        return string.Join(" ", words);
    }

    // ------------------------------------------------------------------
    //  Where it came from, and what the race was called.
    // ------------------------------------------------------------------

    private static readonly Regex SourceUrlLine = new(
        @"^SourceURL:\s*(?<url>\S+)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Where a pasted result came from, for Race History's "Imported from":
    /// "LiveRC" when the browser says the copy was made on a liverc.com
    /// page, "another results page" otherwise. Only the site's name is
    /// kept - never the address.
    /// </summary>
    internal static string SourceName(string? clipboardHtml)
    {
        if (!string.IsNullOrEmpty(clipboardHtml))
        {
            try
            {
                var match = SourceUrlLine.Match(clipboardHtml);

                if (match.Success && Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var uri)
                    && (uri.Host.Equals("liverc.com", StringComparison.OrdinalIgnoreCase)
                        || uri.Host.EndsWith(".liverc.com", StringComparison.OrdinalIgnoreCase)))
                {
                    return "LiveRC";
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }

        return "another results page";
    }

    private static readonly Regex RaceTitle = new(
        @"^\s*Race\s+\d+\s*:\s*(?<title>.+?)\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// The race's name when the copy took in its title - "Race 33: Exp. 1-8
    /// Electric Buggy A-Main" is "Exp. 1-8 Electric Buggy A-Main" - from the
    /// plain text the browser put beside the HTML. Null when there is none.
    /// </summary>
    internal static string? TitleFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var match = RaceTitle.Match(text);
            return match.Success ? match.Groups["title"].Value : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static readonly Regex RoundAtEnd = new(
        @"(?<round>\b(?:[A-Z]-?\s?Main|Main|Final|Heat\s*\d+|Qualifier\s*\d+|Qual(?:ifying)?\s*(?:Round\s*)?\d+|Round\s*\d+|Q\d+)(?:\s*\(.*\))?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// A race name split into its class and its round: "Exp. 1-8 Electric
    /// Buggy A-Main" is ("Exp. 1-8 Electric Buggy", "A-Main"). The class is
    /// matched to one of the club's own when it names one, ignoring case.
    /// The whole name is the round, with no class, when no round is found.
    /// </summary>
    internal static (string ClassName, string Round) SplitTitle(string? title, IEnumerable<string> knownClasses)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (string.Empty, string.Empty);
        }

        title = title.Trim();
        var known = knownClasses.Where(c => !string.IsNullOrWhiteSpace(c)).OrderByDescending(c => c.Length).ToList();

        foreach (var name in known)
        {
            if (title.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && (title.Length == name.Length || !char.IsLetterOrDigit(title[name.Length])))
            {
                return (name, title[name.Length..].Trim(' ', '-', ':'));
            }
        }

        try
        {
            var match = RoundAtEnd.Match(title);

            if (match.Success && match.Index > 0)
            {
                return (title[..match.Index].Trim(' ', '-', ':'), match.Groups["round"].Value.Trim());
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return (string.Empty, title);
    }

    // ------------------------------------------------------------------
    //  Into the history.
    // ------------------------------------------------------------------

    /// <summary>
    /// The pasted result as a session for Race History: a race, labeled
    /// with its class and round, on the day it was run, marked as imported
    /// from <paramref name="source"/> so it never scores. Each driver is
    /// given the profile <paramref name="match"/> finds for their name -
    /// its name and transponder - or kept by the pasted name with no
    /// transponder when there is none.
    /// </summary>
    internal static SessionRecord ToSession(
        Race race,
        string className,
        string round,
        DateTime runLocal,
        string source,
        Func<string, DriverProfile?> match)
    {
        var label = string.Join(" ", new[] { className.Trim(), round.Trim() }.Where(p => p.Length > 0));
        var started = DateTime.SpecifyKind(runLocal, DateTimeKind.Local).ToUniversalTime();
        var longest = race.Rows.Count == 0 ? 0 : race.Rows.Max(r => r.ElapsedMs);

        var session = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = label.Length > 0 ? label : "Imported race",
            StartedAtUtc = started,
            EndedAtUtc = started.AddMilliseconds(longest),
            ImportedFrom = source,
        };

        foreach (var row in race.Rows)
        {
            var profile = match(row.Name);

            session.Results.Add(new SessionResultEntry
            {
                Position = row.Position,
                DriverName = profile is null || string.IsNullOrWhiteSpace(profile.DriverName) ? row.Name : profile.DriverName,
                Transponder = profile?.Transponder ?? row.Transponder,
                CarModel = profile?.CarModel ?? string.Empty,
                Laps = row.Laps,
                ElapsedMs = row.ElapsedMs,
                FastLapMs = row.FastLapMs,
                StartPosition = 0,
            });
        }

        return session;
    }

    /// <summary>
    /// The one profile with this name, ignoring case and spaces at the
    /// ends - null when there's none, or more than one, since a guess between
    /// two drivers would give one the other's result.
    /// </summary>
    internal static DriverProfile? MatchByName(IEnumerable<DriverProfile> profiles, string name)
    {
        var key = name.Trim();
        var found = profiles
            .Where(p => string.Equals(p.DriverName?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        return found.Count == 1 ? found[0] : null;
    }
}
