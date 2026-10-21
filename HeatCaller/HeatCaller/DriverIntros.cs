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
/// The facts behind the broadcast's introductions - each driver on the grid,
/// and what qualifying decided before a main. Worked out here, exactly, and
/// handed to the AI commentary to say (CommentaryService.
/// BuildDriverIntroductionsAsync): the model words them, it never counts.
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
    /// One driver's line in the facts the AI introductions are written from
    /// (CommentaryService.BuildDriverIntroductionsAsync): every part
    /// labelled, and only the parts the driver has - "1. grid position 1;
    /// name Sam; racing number 7; from Riverside; form: won last time out".
    /// Labelled so a small model cannot hand one driver's number or town to
    /// the next.
    /// </summary>
    /// <param name="driver">The driver.</param>
    /// <param name="index">Their place in the list, from 0.</param>
    public static string FactLine(IntroDriver driver, int index)
    {
        var parts = new List<string>();

        if (driver.Slot > 0)
        {
            parts.Add($"grid position {driver.Slot}");
        }

        parts.Add($"name {CommentaryService.Field(driver.SpokenName)}");

        var number = (driver.Number ?? string.Empty).Trim().TrimStart('#');

        if (number.Length > 0)
        {
            parts.Add($"racing number {CommentaryService.Field(number)}");
        }

        if (!string.IsNullOrWhiteSpace(driver.Hometown))
        {
            parts.Add($"from {CommentaryService.Field(driver.Hometown)}");
        }

        if (!string.IsNullOrWhiteSpace(driver.Stat))
        {
            parts.Add($"form: {CommentaryService.Field(driver.Stat)}");
        }

        return $"{index + 1}. " + string.Join("; ", parts);
    }

    /// <summary>
    /// The name the timing last recorded for a car - for a driver with no
    /// profile, who would otherwise be introduced as nobody. Null when no
    /// race has a name for it, or only one with no letters in it.
    /// </summary>
    public static string? NameFromHistory(IEnumerable<SessionRecord> history, string transponder) =>
        (history ?? Enumerable.Empty<SessionRecord>())
            .Where(s => s?.Results is not null)
            .OrderByDescending(s => s.EndedAtUtc)
            .SelectMany(s => s.Results)
            .Where(r => r is not null
                        && string.Equals(r.Transponder, transponder, StringComparison.OrdinalIgnoreCase)
                        && LooksLikeAName(r.DriverName))
            .Select(r => r.DriverName.Trim())
            .FirstOrDefault();

    /// <summary>
    /// Something a voice can read as a name: has a letter in it. A name
    /// that is the transponder's digits would be read out as a number; one
    /// that is a demo car's "transponder" written as a name ("Terry Byte")
    /// is fine.
    /// </summary>
    public static bool LooksLikeAName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Any(char.IsLetter);

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

        // LAPS IN A TIME (20 Oct 2026). A most-laps qualifier is scored the
        // way the timing sheet shows it - 24/5:02.310, laps IN a time - and
        // the facts used to hand the model the laps alone, so it read them
        // out as "a qualifying time of thirty-four laps". The time goes with
        // the laps now, whenever the heat has one.
        var laps = $" with {RaceNumbers.Words(car.Laps)} {Plural.Word(car.Laps, "lap")}";
        var lapsScreen = $" with {Plural.Of(car.Laps, "lap")}";

        return car.ElapsedMs > 0
            ? ($"{laps} in {DurationSpoken(car.ElapsedMs)}", $"{lapsScreen} in {Clock(car.ElapsedMs)}")
            : (laps, lapsScreen);
    }

    /// <summary>
    /// A heat's run time read out: "five minutes flat", "five minutes and
    /// two point three one seconds", or under a minute "forty-eight point
    /// five oh seconds". Words only, like the rest of RaceNumbers.
    /// </summary>
    internal static string DurationSpoken(long ms)
    {
        var centiseconds = (long)Math.Round(ms / 10.0);
        var minutes = centiseconds / 6000;
        var rest = centiseconds % 6000;

        if (minutes <= 0)
        {
            return RaceNumbers.Plainly(ms);
        }

        var said = $"{RaceNumbers.Words(minutes)} {Plural.Word(minutes, "minute")}";

        return rest == 0 ? $"{said} flat" : $"{said} and {RaceNumbers.Plainly(rest * 10)}";
    }

    /// <summary>"5:02.310" - the timing sheet's way of writing a heat's run time.</summary>
    internal static string Clock(long ms)
    {
        var minutes = ms / 60_000;
        var seconds = (ms % 60_000) / 1000.0;

        return $"{minutes}:{seconds.ToString("00.000", System.Globalization.CultureInfo.InvariantCulture)}";
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
