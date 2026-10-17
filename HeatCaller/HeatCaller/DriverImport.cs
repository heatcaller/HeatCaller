using System.Globalization;
using System.Text;

namespace HeatCaller;

/// <summary>What one column of an imported driver list is - picked for each column on the import's preview.</summary>
/// <remarks>Public only so the tests can name one in a theory's parameters.</remarks>
public enum ImportColumn
{
    Ignore,
    Transponder,
    DriverName,
    FirstName,
    LastName,
    AnnouncerName,
    RaceNumber,
    ClassName,
    Car,
    Team,
    Sponsors,
    Email,
    Notes,
    PointsSoFar,
}

/// <summary>
/// Reading a driver list from any CSV - the club's old software's export,
/// or a spreadsheet (27 Sep 2026). Works out the separator, splits the
/// lines, and guesses what each column is from its heading; the Driver
/// Profiles page shows the guesses (CsvMappingForm) for them to be put
/// right before anything is imported.
/// </summary>
/// <remarks>
/// None of the other timing programs publishes its export format, so
/// nothing here is built against one: the headings are a list of the words
/// clubs use, and the preview is the real safeguard. Roadmap item 2, "column
/// mapping" and "season points as a starting number".
/// </remarks>
internal static class DriverImport
{
    /// <summary>What each kind of column is called on the preview.</summary>
    internal static string Caption(ImportColumn column) => column switch
    {
        ImportColumn.Transponder => "Transponder",
        ImportColumn.DriverName => "Driver name",
        ImportColumn.FirstName => "First name",
        ImportColumn.LastName => "Last name",
        ImportColumn.AnnouncerName => "Announcer name",
        ImportColumn.RaceNumber => "Race number",
        ImportColumn.ClassName => "Class",
        ImportColumn.Car => "Car",
        ImportColumn.Team => "Team",
        ImportColumn.Sponsors => "Sponsors",
        ImportColumn.Email => "Email",
        ImportColumn.Notes => "Notes",
        ImportColumn.PointsSoFar => "Points so far",
        _ => "(ignore)",
    };

    /// <summary>Every kind, in the order the preview lists them.</summary>
    internal static IReadOnlyList<ImportColumn> All { get; } = Enum.GetValues<ImportColumn>();

    /// <summary>
    /// The separator a file uses: a comma, a semicolon (Excel's CSV where a
    /// comma is the decimal point) or a tab - whichever the heading line has
    /// most of outside quotes. A comma when it has none of any.
    /// </summary>
    internal static char DetectDelimiter(string headerLine)
    {
        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
        var inQuotes = false;

        foreach (var c in headerLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && counts.ContainsKey(c))
            {
                counts[c]++;
            }
        }

        var best = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key == ',' ? 0 : 1).First();

        return best.Value == 0 ? ',' : best.Key;
    }

    /// <summary>
    /// One line split into fields, quotes honoured ("Smith, Jr." is one
    /// field, and "" inside quotes is a quote). A field spread over two
    /// lines is read as one when given the whole record - which
    /// CsvHelper.NonBlankLines now keeps together, so a note typed over two
    /// lines in Excel is no longer split into a junk driver.
    /// </summary>
    internal static List<string> ParseLine(string line, char delimiter)
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
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    /// <summary>
    /// A heading as it is compared: lower case, anything but letters, digits
    /// and # a space, runs of spaces one. "E-Mail" is "e mail", "Team /
    /// Club" is "team club", "Transponder #" is "transponder #".
    /// </summary>
    internal static string Normalise(string header)
    {
        var text = new StringBuilder(header.Length);

        foreach (var c in header.Trim().ToLowerInvariant())
        {
            text.Append(char.IsLetterOrDigit(c) || c == '#' ? c : ' ');
        }

        return string.Join(" ", text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly (ImportColumn Column, string[] Headings)[] Headings =
    {
        (ImportColumn.Transponder, new[]
        {
            "transponder", "transponderid", "transponder id", "transponder number", "transponder no",
            "transponder #", "transponder code", "personal transponder", "tx", "tx id", "trans", "chip", "chip id",
            "mylaps", "mylaps id", "rc4", "decoder id",
        }),
        (ImportColumn.FirstName, new[] { "first name", "firstname", "first", "given name", "forename" }),
        (ImportColumn.LastName, new[] { "last name", "lastname", "last", "surname", "family name" }),
        (ImportColumn.DriverName, new[]
        {
            "drivername", "driver name", "driver", "name", "racer", "racer name", "pilot", "full name", "competitor",
            "entrant", "member", "member name",
        }),
        (ImportColumn.AnnouncerName, new[] { "announcer name", "announcername", "spoken name", "nickname", "nick name" }),
        (ImportColumn.RaceNumber, new[] { "race number", "racenumber", "number", "#", "no", "car number", "car #", "car no", "bib" }),
        (ImportColumn.ClassName, new[] { "classname", "class name", "class", "category", "division", "racing class" }),
        (ImportColumn.Car, new[] { "carmodel", "car model", "car", "model", "vehicle", "chassis", "kit" }),
        (ImportColumn.Team, new[] { "team", "club", "team club", "team name" }),
        (ImportColumn.Sponsors, new[] { "sponsors", "sponsor" }),
        (ImportColumn.Email, new[] { "email", "e mail", "email address", "e mail address", "mail" }),
        (ImportColumn.Notes, new[] { "notes", "note", "comments", "comment", "remarks" }),
        (ImportColumn.PointsSoFar, new[]
        {
            "points", "pts", "points so far", "pointssofar", "total points", "points total", "season points",
            "championship points", "series points", "total", "score",
        }),
    };

    /// <summary>What a column probably is, from its heading - Ignore when nothing matches.</summary>
    internal static ImportColumn Guess(string header)
    {
        var key = Normalise(header);

        foreach (var (column, headings) in Headings)
        {
            if (headings.Contains(key))
            {
                return column;
            }
        }

        // "Transponder (MyLaps)", "Driver's transponder" and the like.
        return key.Contains("transponder", StringComparison.Ordinal) ? ImportColumn.Transponder : ImportColumn.Ignore;
    }

    /// <summary>
    /// A guess for every column. Each kind is guessed for one column at most
    /// - the first - so two "Name" columns don't both become the driver's
    /// name. The preview lets that be changed.
    /// </summary>
    internal static List<ImportColumn> GuessAll(IReadOnlyList<string> headers)
    {
        var taken = new HashSet<ImportColumn>();
        var guesses = new List<ImportColumn>(headers.Count);

        foreach (var header in headers)
        {
            var guess = Guess(header);
            guesses.Add(guess != ImportColumn.Ignore && taken.Add(guess) ? guess : ImportColumn.Ignore);
        }

        return guesses;
    }

    /// <summary>
    /// A "points so far" cell as a whole number: "45", "45.0", "45,5",
    /// "45 pts", "1,234" or "1 234" (thousands), "-3". False for a blank
    /// cell or anything else - a row whose points can't be read carries
    /// none in. A fraction is rounded, since points are whole. A single
    /// point is always a decimal point ("45.500" is 45.5): only a comma or
    /// a space groups thousands, or points when there are two of them.
    /// </summary>
    internal static bool TryParsePoints(string? text, out int points)
    {
        points = 0;

        // The Unicode minus (U+2212) is what sv/nb/fi Windows and their
        // spreadsheets write for a negative number; the parse below is
        // invariant and only knows '-' (17 Oct 2026, Bug Hunt 8, A26-2).
        var value = (text ?? string.Empty).Trim().Replace('\u2212', '-');

        // "45 pts", "45 points".
        var end = value.Length;

        while (end > 0 && char.IsLetter(value[end - 1]))
        {
            end--;
        }

        value = value[..end].Trim();

        if (value.Length == 0)
        {
            return false;
        }

        // Digits grouped in threes by commas or spaces - or by two or more
        // points - are a thousands figure, not a fraction.
        var grouped = System.Text.RegularExpressions.Regex.IsMatch(value, @"^-?\d{1,3}([, ]\d{3})+$")
                      || System.Text.RegularExpressions.Regex.IsMatch(value, @"^-?\d{1,3}(\.\d{3}){2,}$");

        if (grouped)
        {
            value = value.Replace(",", string.Empty).Replace(".", string.Empty).Replace(" ", string.Empty);
        }
        else
        {
            value = value.Replace(',', '.');
        }

        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        if (number > int.MaxValue || number < int.MinValue)
        {
            return false;
        }

        points = (int)Math.Round(number, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>A whole name from a first-name and a last-name column, either of which can be blank.</summary>
    internal static string JoinName(string? first, string? last) =>
        string.Join(" ", new[] { first?.Trim(), last?.Trim() }.Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>
    /// A row's <b>Class</b> merged into the driver's profile. A blank cell
    /// changes nothing. <paramref name="repeatRow"/> is a second row for a
    /// driver already read from this sheet - a driver entered in two classes:
    /// a class they don't race yet is added to their others, and one they
    /// race already changes nothing. Any other row sets the main class.
    /// </summary>
    internal static void MergeClass(DriverProfile profile, bool repeatRow, string rowClass)
    {
        if (string.IsNullOrWhiteSpace(rowClass))
        {
            return;
        }

        // A class they race already used to fall through to setting the main
        // class. Importing the same sheet again - or a driver who already had
        // the second class ticked - left "Stock" with "Mod" among the others
        // as "Mod" with "Mod": out of Stock's heats without a word.
        if (repeatRow && !string.IsNullOrWhiteSpace(profile.ClassName))
        {
            if (!profile.RunsClass(rowClass))
            {
                profile.Classes = (profile.Classes ?? new List<string>()).Append(rowClass.Trim()).ToList();
            }

            return;
        }

        profile.ClassName = rowClass.Trim();
    }

    /// <summary>
    /// A row's <b>Car</b> merged into the driver's profile, as MergeClass does
    /// the class: a repeat row's car is added to their garage if it is new to
    /// them, and any other row sets the active car.
    /// </summary>
    internal static void MergeCar(DriverProfile profile, bool repeatRow, string rowCar)
    {
        if (string.IsNullOrWhiteSpace(rowCar))
        {
            return;
        }

        // Every row used to set the active car, so the second car of someone
        // running two replaced the first, which never reached the Garage.
        if (repeatRow && !string.IsNullOrWhiteSpace(profile.CarModel))
        {
            if (!profile.AllCars.Any(car => string.Equals(car, rowCar.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                profile.Cars = (profile.Cars ?? new List<string>()).Append(rowCar.Trim()).ToList();
            }

            return;
        }

        profile.CarModel = rowCar.Trim();
    }
}
