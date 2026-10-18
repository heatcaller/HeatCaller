namespace HeatCaller;

/// <summary>One driver on the grid, as an introduction needs them.</summary>
/// <param name="Transponder">Who.</param>
/// <param name="Slot">Grid slot, 1 for pole - or 0 when the race has no grid (a freeform start).</param>
/// <param name="SpokenName">The name the voice is given - the announcer name when the club set one.</param>
/// <param name="ScreenName">The name a screen shows - the driver's real name.</param>
/// <param name="Number">Racing number from the profile, or empty.</param>
/// <param name="Hometown">Home club or town from the profile, or empty.</param>
/// <param name="Stat">One line of form from their history (<see cref="DriverIntros.Stat"/>), or empty.</param>
public sealed record IntroDriver(
    string Transponder,
    int Slot,
    string SpokenName,
    string ScreenName,
    string Number = "",
    string Hometown = "",
    string Stat = "");

/// <summary>One car in a main's qualifying recap, in qualifying order.</summary>
/// <param name="SpokenName">The name the voice is given.</param>
/// <param name="ScreenName">The name a screen shows.</param>
/// <param name="Laps">Best heat's laps (a most-laps qualifier).</param>
/// <param name="ElapsedMs">Best heat's time for those laps (a most-laps qualifier).</param>
/// <param name="ConsecutiveMs">Best run of consecutive laps, in total (a consecutive-laps qualifier). 0 when they never put one together.</param>
/// <param name="FastLapMs">Best single lap in qualifying.</param>
public sealed record RecapCar(
    string SpokenName,
    string ScreenName,
    int Laps,
    long ElapsedMs,
    long ConsecutiveMs,
    long FastLapMs);

/// <summary>
/// The words of the broadcast's introductions - each driver on the grid, and
/// what qualifying decided before a main - built in code rather than in a
/// template, for the reason the DriverIntro defaults give: six optional
/// pieces do not stay a sentence in a template.
///
/// Every builder returns the SPOKEN form and the SCREEN form together, as
/// the rest of the announcer does: numbers the voice is given as words
/// ("two wins", "five oh six"), and the screens get numerals.
///
/// Pure: nothing here reads a store, so every sentence it can make is a
/// table test.
/// </summary>
internal static class DriverIntros
{
    /// <summary>
    /// "On pole", "Starting second" ... and, for the back of a full grid,
    /// "Rounding out the field". Empty for a slot of 0 - a freeform race with
    /// no grid, where calling anybody "on pole" would be inventing one.
    /// </summary>
    public static string GridPhrase(int slot, int fieldSize)
    {
        if (slot <= 0)
        {
            return string.Empty;
        }

        if (slot == 1)
        {
            return "On pole";
        }

        if (slot == fieldSize && fieldSize >= 4)
        {
            return "Rounding out the field";
        }

        return $"Starting {LeadTracker.SpokenOrdinal(slot)}";
    }

    /// <summary>
    /// One driver's introduction without its closing mark - the template puts
    /// the "!" or "." on: "On pole, number 7, Sam Smith, from Riverside - two
    /// wins from the last five". Whatever the driver has not got is simply
    /// not said.
    /// </summary>
    public static (string Spoken, string Screen) Intro(IntroDriver driver, int fieldSize)
    {
        string Build(string name, string number)
        {
            var parts = new List<string>();
            var grid = GridPhrase(driver.Slot, fieldSize);

            if (grid.Length > 0)
            {
                parts.Add(grid);
            }

            if (number.Length > 0)
            {
                parts.Add(number);
            }

            parts.Add(name);

            var line = string.Join(", ", parts);

            if (!string.IsNullOrWhiteSpace(driver.Hometown))
            {
                line += $", from {driver.Hometown.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(driver.Stat))
            {
                line += $" - {driver.Stat.Trim()}";
            }

            return line;
        }

        var raw = (driver.Number ?? string.Empty).Trim().TrimStart('#');

        // "number seven" to the voice, "#7" on a screen - a bare "7" read
        // aloud between two commas is a number in search of a meaning.
        var spokenNumber = raw.Length == 0 ? string.Empty
            : long.TryParse(raw, out var n) && n is >= 0 and < 1000 ? $"number {RaceNumbers.Words(n)}"
            : $"number {raw}";
        var screenNumber = raw.Length == 0 ? string.Empty : $"#{raw}";

        return (Build(driver.SpokenName, spokenNumber), Build(driver.ScreenName, screenNumber));
    }

    /// <summary>
    /// One line of a driver's form, for their introduction - the most
    /// interesting true thing their saved races say, or empty:
    ///
    ///   a milestone start ("about to make a hundredth start"),
    ///   a first start,
    ///   a winning run ("going for three in a row"),
    ///   last time out ("won last time out"),
    ///   recent wins or podiums ("two wins from the last five"),
    ///   their average driver rating once they have three rated races.
    ///
    /// Races only, never a demo, and only before <paramref name="beforeUtc"/>
    /// - the race being introduced is not part of its own form. Places are
    /// counted among the real cars, as DriverStats counts them.
    /// </summary>
    public static string Stat(IEnumerable<SessionRecord> history, string transponder, DateTime beforeUtc)
    {
        var races = (history ?? Enumerable.Empty<SessionRecord>())
            .Where(s => s is not null
                && s.SessionType == RaceSessionType.Race
                && !s.IsDemoSession()
                && s.StartedAtUtc < beforeUtc
                && s.Results.Any(r => string.Equals(r.Transponder, transponder, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(s => s.StartedAtUtc)
            .ToList();

        var starts = races.Count;

        // A milestone start beats everything - it is the one the room claps.
        var milestone = (starts + 1) switch
        {
            10 => "tenth",
            25 => "twenty-fifth",
            50 => "fiftieth",
            100 => "hundredth",
            250 => "two hundred and fiftieth",
            500 => "five hundredth",
            _ => null,
        };

        if (milestone is not null)
        {
            return $"about to make a {milestone} start";
        }

        if (starts == 0)
        {
            return "making a first start";
        }

        var places = races.Select(s => PlaceAmongRealCars(s, transponder)).ToList();

        var streak = places.TakeWhile(p => p == 1).Count();

        if (streak >= 2)
        {
            return $"going for {RaceNumbers.Words(streak + 1)} in a row";
        }

        if (places[0] == 1)
        {
            return "won last time out";
        }

        var lastFive = places.Take(5).ToList();
        var wins = lastFive.Count(p => p == 1);
        var podiums = lastFive.Count(p => p is >= 1 and <= 3);
        var outOf = lastFive.Count == 5 ? "the last five" : $"their last {RaceNumbers.Words(lastFive.Count)}";

        if (wins >= 2)
        {
            return $"{RaceNumbers.Words(wins)} wins from {outOf}";
        }

        if (podiums >= 3)
        {
            return $"{RaceNumbers.Words(podiums)} podiums from {outOf}";
        }

        var ratings = races
            .Take(10)
            .Select(s => DriverRating.ForSession(s).TryGetValue(transponder, out var rating) ? (double?)rating : null)
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .ToList();

        if (ratings.Count >= 3)
        {
            return $"averaging a driver rating of {RaceNumbers.Words((long)Math.Round(ratings.Average()))}";
        }

        return string.Empty;
    }

    /// <summary>Where they finished among the real cars, or 0 for a race they did not finish (no laps, a DNF, a DQ).</summary>
    private static int PlaceAmongRealCars(SessionRecord race, string transponder)
    {
        var ranked = race.Results
            .Where(e => !GhostService.IsGhost(e.Transponder))
            .OrderBy(e => e.Position)
            .ToList();

        var index = ranked.FindIndex(e => string.Equals(e.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return 0;
        }

        var row = ranked[index];

        return row.Laps <= 0 || !string.IsNullOrEmpty(row.Outcome) ? 0 : index + 1;
    }

    /// <summary>
    /// What qualifying decided, for the cars in a main, in qualifying order:
    /// "From qualifying: Sam Smith takes pole with twenty-four laps. Bill
    /// Jones lines up second, two tenths back, and Frank Lee third." Empty
    /// when there is nobody to talk about.
    ///
    /// The score is said the way the class qualifies - laps for a most-laps
    /// qualifier, the average of the best run for a consecutive-laps one -
    /// because a recap that ranked by one and quoted the other would be
    /// reading out a different grid.
    /// </summary>
    public static (string Spoken, string Screen) Recap(IReadOnlyList<RecapCar> order, QualifyingMethod method, int consecutiveLaps)
    {
        if (order is null || order.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        var lapCount = Math.Max(1, consecutiveLaps);
        var pole = order[0];

        var (scoreSpoken, scoreScreen) = Score(pole, method, lapCount);

        var spoken = $"From qualifying: {pole.SpokenName} takes pole{scoreSpoken}.";
        var screen = $"From qualifying: {pole.ScreenName} takes pole{scoreScreen}.";

        if (order.Count >= 2)
        {
            var second = order[1];
            var (marginSpoken, marginScreen) = Margin(pole, second, method, lapCount);

            spoken += $" {second.SpokenName} lines up second{marginSpoken}";
            screen += $" {second.ScreenName} lines up second{marginScreen}";

            if (order.Count >= 3)
            {
                spoken += $", and {order[2].SpokenName} third";
                screen += $", and {order[2].ScreenName} third";
            }

            spoken += ".";
            screen += ".";
        }

        return (spoken, screen);
    }

    /// <summary>" with twenty-four laps" / " averaging a five oh six over three laps", and the screen's version.</summary>
    private static (string Spoken, string Screen) Score(RecapCar car, QualifyingMethod method, int lapCount)
    {
        if (method == QualifyingMethod.BestConsecutiveLaps)
        {
            if (car.ConsecutiveMs <= 0)
            {
                return (string.Empty, string.Empty);
            }

            var average = car.ConsecutiveMs / lapCount;

            return ($" averaging a {RaceNumbers.LapSpoken(average)} over {RaceNumbers.Words(lapCount)} laps",
                $" averaging {Seconds(average)} over {Plural.Of(lapCount, "lap")}");
        }

        if (car.Laps <= 0)
        {
            return (string.Empty, string.Empty);
        }

        return ($" with {RaceNumbers.Words(car.Laps)} {Plural.Word(car.Laps, "lap")}", $" with {Plural.Of(car.Laps, "lap")}");
    }

    /// <summary>", two tenths back" / ", a lap down", and the screen's version - empty when it cannot be measured honestly.</summary>
    private static (string Spoken, string Screen) Margin(RecapCar ahead, RecapCar behind, QualifyingMethod method, int lapCount)
    {
        if (method == QualifyingMethod.BestConsecutiveLaps)
        {
            if (ahead.ConsecutiveMs <= 0 || behind.ConsecutiveMs <= 0)
            {
                return (string.Empty, string.Empty);
            }

            var gap = Math.Max(0, behind.ConsecutiveMs - ahead.ConsecutiveMs) / 1000.0;

            return (Back(gap), $", +{gap.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}s");
        }

        if (ahead.Laps <= 0 || behind.Laps <= 0)
        {
            return (string.Empty, string.Empty);
        }

        if (behind.Laps < ahead.Laps)
        {
            var down = ahead.Laps - behind.Laps;

            return (down == 1 ? ", a lap down" : $", {RaceNumbers.Words(down)} laps down", $", {Plural.Of(down, "lap")} down");
        }

        if (ahead.ElapsedMs <= 0 || behind.ElapsedMs <= 0)
        {
            return (string.Empty, string.Empty);
        }

        var seconds = Math.Max(0, behind.ElapsedMs - ahead.ElapsedMs) / 1000.0;

        return (Back(seconds), $", +{seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}s");
    }

    /// <summary>", two tenths back" - or ", dead level" for a gap too small to say.</summary>
    private static string Back(double seconds)
    {
        var margin = RaceNumbers.MarginSpoken(seconds);

        return margin == "nothing" ? ", dead level" : $", {margin} back";
    }

    private static string Seconds(long ms) =>
        (ms / 1000.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "s";
}
