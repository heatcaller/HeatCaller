namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Import drivers (CsvMappingForm), from Driver Profiles' Import from
    /// CSV...: each file made up here as the lines of text it would hold, and
    /// read the way the page reads one (ProfileManagerControl.ImportFromCsv) -
    /// the separator found from the heading line, every line split, each
    /// column guessed from its heading - so nothing is read from disk. A
    /// club's own roster of forty with every heading guessed and a
    /// championship for every class and two for one class each; a wide export
    /// from other timing software - twenty-seven columns, headings long enough
    /// to be cut short, a blank one, rows short of fields, three hundred
    /// drivers and five championships with long names; a file whose headings
    /// match nothing; and a small file with points and no championship to put
    /// them in.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> CsvImportWindows() => new (string, Func<Form>)[]
    {
        ("Import drivers", () => CsvImportClubRoster()),
        ("Import drivers (wide file, long headings)", () => CsvImportWideExport()),
        ("Import drivers (no heading matches)", () => CsvImportNothingMatches()),
        ("Import drivers (small file, no championship)", () => CsvImportSmallFile()),
    };

    /// <summary>
    /// A club's roster as a spreadsheet saves it: comma-separated, a heading
    /// the guesses know for every column, the longest names Drivers makes,
    /// teams and sponsors with commas in them (quoted), and a note with
    /// quotes in it. Class and Points columns, and championships with and
    /// without a class, so "the championship for each driver's class" is
    /// offered first.
    /// </summary>
    private static CsvMappingForm CsvImportClubRoster()
    {
        const char comma = ',';

        var classes = new[] { "Stock Buggy", "Modified Touring", "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)" };

        var lines = new List<string>
        {
            CsvImportLine(comma, "Transponder", "Driver Name", "Nickname", "Car #", "Class", "Car Model", "Team", "Sponsors", "Email", "Notes", "Points"),
        };

        lines.AddRange(Drivers(40).Select((driver, i) => CsvImportLine(
            comma,
            driver.Transponder,
            driver.DriverName,
            i % 4 == 3 ? "Monty" : string.Empty,
            (i + 1).ToString(),
            classes[i % classes.Length],
            driver.CarModel,
            i % 2 == 0 ? "Hobby Hut Racing, Northside" : "Southend Mini-Z Club",
            i % 3 == 0 ? "Speed Shop, Tyre Barn, Hobby Hut" : string.Empty,
            CsvImportEmailFor(driver.DriverName),
            i % 5 == 0 ? "Says \"Monty\", not Montgomery - moved up from Juniors mid-season" : string.Empty,
            (412 - i * 9).ToString())));

        return CsvImportForm("club-roster-2026.csv", lines, new[]
        {
            new Season { Name = "2026 Club Championship" },
            new Season { Name = "2026 Stock Buggy Series", ClassName = "Stock Buggy" },
            new Season { Name = "2026 Winter Series", ClassName = "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)" },
        });
    }

    /// <summary>
    /// Another timing program's export: semicolon-separated, twenty-seven
    /// columns - most of them nothing the import takes, some with headings
    /// far past the widest a column is made, one with no heading at all -
    /// three hundred drivers, every seventh row three fields short, and
    /// five championships, the longest names among them.
    /// </summary>
    private static CsvMappingForm CsvImportWideExport()
    {
        const char semicolon = ';';

        var classes = new[] { "Stock Buggy", "Modified Touring", "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)", "Open" };

        var lines = new List<string>
        {
            CsvImportLine(
                semicolon,
                "Pos", "Driver", "Transponder (MyLaps personal transponder number)", "Nickname", "Car #", "Racing Class",
                "Chassis", "Motor / Pinion / Spur", "Tyres (front / rear)", "Team / Club", "Sponsor", "E-Mail Address",
                "Phone", "Membership number", "Membership expires", "National body registration number (BRCA / ROAR / EFRA)",
                "Emergency contact", "Round 1", "Round 2", "Round 3", "Round 4", "Round 5", "Dropped rounds", "Total",
                "Remarks", string.Empty, "Last updated by the timing software on export"),
        };

        lines.AddRange(Drivers(300).Select((driver, i) =>
        {
            var rounds = Enumerable.Range(1, 5).Select(round => 25 - (i + round) % 12).ToArray();

            var fields = new[]
            {
                (i + 1).ToString(),
                driver.DriverName,
                driver.Transponder,
                i % 4 == 3 ? "Monty" : string.Empty,
                (i + 1).ToString(),
                classes[i % classes.Length],
                driver.CarModel,
                "Atomic 98T / 12 / 64",
                "Sweep 30 / Sweep 20",
                "Northside Mini-Z Club",
                i % 3 == 0 ? "Hobby Hut; Speed Shop" : string.Empty,
                CsvImportEmailFor(driver.DriverName),
                "+1 555 0100",
                $"M-{10_000 + i}",
                "2026-12-31",
                $"BRCA {123_456 + i}",
                "Pat Montgomery-Fitzwilliam, +1 555 0199",
                rounds[0].ToString(),
                rounds[1].ToString(),
                rounds[2].ToString(),
                rounds[3].ToString(),
                rounds[4].ToString(),
                "R2 (19 laps)",
                (rounds.Sum() - rounds.Min()).ToString(),
                i % 5 == 0 ? "Moved up from Juniors mid-season, points carried over from the old system" : string.Empty,
                string.Empty,
                "2026-09-26 21:14",
            };

            return CsvImportLine(semicolon, i % 7 == 6 ? fields[..^3] : fields);
        }));

        return CsvImportForm("championship-export-2026-09-26 (Saturday Series, all classes).csv", lines, new[]
        {
            new Season { Name = "Mini-Z Club Championship 2026 - Saturday Series" },
            new Season { Name = "2026 Winter Series", ClassName = "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)" },
            new Season { Name = "Summer Endurance Cup", ClassName = "Stock Buggy" },
            new Season { Name = "Club Night Points", ClassName = "Modified Touring" },
            new Season { Name = "Regional Series Round-Up 2026" },
        });
    }

    /// <summary>
    /// A tab-separated file whose headings are nothing the guesses know - and
    /// one blank - so every list starts at (ignore). With no Points column
    /// picked, the championships are there but the picker is off, and its
    /// hint says to pick one.
    /// </summary>
    private static CsvMappingForm CsvImportNothingMatches()
    {
        const char tab = '\t';

        var lines = new List<string>
        {
            CsvImportLine(tab, "Ref", "Given", "Family", "Tag", "Grp", "Qual 1", "Qual 2", "Final", string.Empty),
        };

        lines.AddRange(Drivers(30).Select((driver, i) =>
        {
            var name = driver.DriverName;
            var space = name.LastIndexOf(' ');

            return CsvImportLine(
                tab,
                $"R{i + 1:000}",
                space > 0 ? name[..space] : name,
                space > 0 ? name[(space + 1)..] : string.Empty,
                driver.Transponder,
                i % 2 == 0 ? "A" : "B",
                (18 + i % 5).ToString(),
                (17 + i % 6).ToString(),
                (i % 10 + 1).ToString(),
                string.Empty);
        }));

        return CsvImportForm("export.txt", lines, new[]
        {
            new Season { Name = "2026 Club Championship" },
            new Season { Name = "2026 Stock Buggy Series", ClassName = "Stock Buggy" },
        });
    }

    /// <summary>
    /// Three columns and two drivers, with points and no championship yet to
    /// carry them into: the picker empty and off, and the hint saying to make
    /// one first.
    /// </summary>
    private static CsvMappingForm CsvImportSmallFile()
    {
        const char comma = ',';

        var lines = new List<string>
        {
            CsvImportLine(comma, "Name", "Transponder", "Points"),
            CsvImportLine(comma, "Driver 1", "4000137", "88"),
            CsvImportLine(comma, "Alexandria Montgomery-Fitzwilliam 12", "4001644", "112"),
        };

        return CsvImportForm("points.csv", lines, Array.Empty<Season>());
    }

    /// <summary>
    /// The window as Driver Profiles opens it for a file of these lines: the
    /// separator found from the heading line, every line split with it, and
    /// each column's guess from its heading.
    /// </summary>
    private static CsvMappingForm CsvImportForm(string fileName, IReadOnlyList<string> lines, IReadOnlyList<Season> seasons)
    {
        var delimiter = DriverImport.DetectDelimiter(lines[0]);
        var headers = DriverImport.ParseLine(lines[0], delimiter);
        var rows = lines.Skip(1).Select(line => DriverImport.ParseLine(line, delimiter)).ToList();

        return new CsvMappingForm(fileName, headers, rows, DriverImport.GuessAll(headers), seasons);
    }

    /// <summary>One line of a file, as a spreadsheet writes it: the fields joined by the separator, any with the separator or a quote in it quoted.</summary>
    private static string CsvImportLine(char delimiter, params string[] fields) =>
        string.Join(delimiter.ToString(), fields.Select(field =>
            field.Contains(delimiter) || field.Contains('"')
                ? "\"" + field.Replace("\"", "\"\"") + "\""
                : field));

    /// <summary>An address made from a made-up name: "alexandria.montgomery-fitzwilliam.12@example.com" for the longest.</summary>
    private static string CsvImportEmailFor(string name) =>
        name.ToLowerInvariant().Replace(' ', '.') + "@example.com";
}
