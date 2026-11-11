namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Import Entries on Who's Racing (11 Nov 2026, build plan 7.1): the
    /// column picks in their entry mode (no championship row, Next rather
    /// than Import) for an RCSignup export, and the preview of what the
    /// import will do - forty rows of every kind, with long names, long
    /// reasons and a class the club hasn't got - with a race day open, and
    /// again with the day closed, so the check-in tick is off and says why.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> EntryImportWindows() => new (string, Func<Form>)[]
    {
        ("Import drivers (entry list)", () => EntryMappingForm()),
        ("Import entries", () => EntryPreview("\"Club Night - Tuesday 11 November\"")),
        ("Import entries (closed day)", () => EntryPreview(null)),
    };

    private static CsvMappingForm EntryMappingForm()
    {
        var lines = new List<string> { "First Name,Last Name,Class,Transponder,Car Number,Email,Phone" };
        lines.AddRange(Enumerable.Range(1, 30).Select(i => $"Driver,Number {i},Stock Buggy,{4000000 + i},{i},d{i}@example.com,555-01{i:00}"));

        var delimiter = DriverImport.DetectDelimiter(lines[0]);
        var headers = DriverImport.ParseLine(lines[0], delimiter);
        var rows = lines.Skip(1).Select(line => DriverImport.ParseLine(line, delimiter)).ToList();

        return new CsvMappingForm("rcsignup-export.csv", headers, rows, DriverImport.GuessAll(headers), Array.Empty<Season>(), entries: true);
    }

    private static EntryImportForm EntryPreview(string? checkInTo)
    {
        var classes = new[] { "Stock Buggy", "Modified Touring", "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)" };

        var profiles = Enumerable.Range(1, 20)
            .Select(i => new DriverProfile
            {
                Transponder = (4000000 + i).ToString(),
                DriverName = i == 1 ? "Alexandria Montgomery-Fitzwilliam" : $"Driver {i}",
                ClassName = "Stock Buggy",
                RaceNumber = i.ToString(),
            })
            .ToList();

        var rows = Enumerable.Range(1, 40)
            .Select(i => new EntryImport.EntryRow(
                i + 1,
                i == 1 ? "Alexandria Montgomery-Fitzwilliam" : i == 25 ? "Somebody Else Entirely" : $"Driver {i}",
                i == 30 ? string.Empty : (4000000 + (i == 25 ? 3 : i)).ToString(),
                i % 3 == 0 ? new[] { classes[2] } : i % 7 == 0 ? new[] { "Vintage Trucks" } : new[] { classes[i % 2] },
                i % 4 == 0 ? (i + 50).ToString() : string.Empty,
                i % 5 == 0 ? "Associated RC10 B7 Team Kit" : string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty))
            .ToList();

        return new EntryImportForm("rcsignup-export.csv", rows, profiles, classes, checkInTo);
    }
}
