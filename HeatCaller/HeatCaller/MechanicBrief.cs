namespace HeatCaller;

/// <summary>
/// What the mechanic in the recap's interview may say about the winning
/// car: the setup logged for it in the Garage before the race, and what
/// changed from the one before (27 Sep 2026). Lines for the interview's
/// facts - the model is told the car has nothing on it beyond them.
/// </summary>
/// <remarks>
/// Which setups are the winner's: those logged against a car the winner was
/// in on the day - their own, or one lent to them for that race day - and,
/// for an entry from before the Garage (no car on it), those under the
/// winner's name. When the winner has a car in the race's class, only those
/// cars count: a driver with a stock car and a mod car ran one of them.
///
/// Only the newest of those, and only when it was logged within
/// <see cref="RaceDay"/> of the race ending: a setup from last month is not
/// a change the team made for this race, and a mechanic saying it was would
/// be the invented fact every prompt here is written to prevent.
/// </remarks>
internal static class MechanicBrief
{
    /// <summary>How long before the race ended a logged setup still counts as this race day's.</summary>
    internal static readonly TimeSpan RaceDay = TimeSpan.FromHours(24);

    /// <param name="transponder">The winner's transponder.</param>
    /// <param name="driverName">The winner's name as their profile has it - what an entry from before the Garage was logged under.</param>
    /// <param name="className">The race's class, or null for a race that isn't a round.</param>
    /// <param name="eventId">The race day the race was run on, for cars lent for it. Null when there is none.</param>
    /// <param name="raceEndedUtc">When the race ended. A setup logged after it is not what the car ran.</param>
    /// <param name="cars">Every car in the Garage.</param>
    /// <param name="setups">Every logged setup.</param>
    /// <param name="templateFor">Looks a setup sheet up by id, for the fields' names.</param>
    /// <returns>Lines for the prompt; empty when no setup is logged for the winning car at all.</returns>
    internal static IReadOnlyList<string> SetupLines(
        string transponder,
        string driverName,
        string? className,
        string? eventId,
        DateTime raceEndedUtc,
        IEnumerable<Car> cars,
        IReadOnlyList<SetupEntry> setups,
        Func<string?, ChassisTemplate?> templateFor)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return Array.Empty<string>();
        }

        var theirs = cars
            .Where(car => string.Equals(car.DriverFor(eventId), transponder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrWhiteSpace(className))
        {
            var inClass = theirs
                .Where(car => string.Equals((car.ClassName ?? string.Empty).Trim(), className.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (inClass.Count > 0)
            {
                theirs = inClass;
            }
        }

        var carIds = new HashSet<string>(theirs.Select(car => car.Id), StringComparer.Ordinal);
        var name = (driverName ?? string.Empty).Trim();

        var latest = setups
            .Where(entry => entry.RecordedAtUtc <= raceEndedUtc
                            && (!string.IsNullOrEmpty(entry.CarId)
                                ? carIds.Contains(entry.CarId)
                                : name.Length > 0 && string.Equals((entry.DriverName ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(entry => entry.RecordedAtUtc)
            .FirstOrDefault();

        if (latest is null)
        {
            return Array.Empty<string>();
        }

        if (raceEndedUtc - latest.RecordedAtUtc > RaceDay)
        {
            return new[] { "No setup changes were logged for this race day - the car ran the setup it already had." };
        }

        var previous = SetupDiff.Previous(latest, setups);

        // Nothing to compare with: the car's first setup, or its first on a
        // setup sheet after entries from before them, which share no fields.
        // What was filled in is said instead.
        if (previous is null || previous.IsLegacy != latest.IsLegacy)
        {
            var filled = SetupDiff.Filled(latest, templateFor);
            var first = previous is null ? "the first one logged for this car" : "the first on a setup sheet for this car";

            return filled.Count == 0
                ? new[] { "A setup was logged for this race day, but nothing on it was filled in." }
                : new[] { $"Setup logged for this race day, {first}, so there is nothing to compare it with: " + string.Join(", ", filled) };
        }

        // Only values on the sheet - see SetupDiff.Change.OnSheet.
        var changes = SetupDiff.Between(previous, latest, templateFor).Where(change => change.OnSheet).ToList();

        if (changes.Count == 0)
        {
            return new[] { "Setup logged for this race day: no changes from the setup before it." };
        }

        var lines = new List<string> { "Setup changes logged for this race day:" };
        lines.AddRange(SetupDiff.Spoken(changes).Select(change => "- " + change));

        if (changes.Count > SetupDiff.SpokenMax)
        {
            lines.Add($"- and {Plural.Of(changes.Count - SetupDiff.SpokenMax, "more change")} besides");
        }

        return lines;
    }
}
