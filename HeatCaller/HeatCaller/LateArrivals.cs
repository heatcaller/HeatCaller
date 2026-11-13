namespace HeatCaller;

/// <summary>
/// Drivers who checked in after tonight's heats were built (12 Nov 2026,
/// setup plan B2 and D6).
///
/// WHY: once the heats are built, a driver ticked in at the desk or from
/// their phone went nowhere. Somebody had to notice and do Add a Late Entry,
/// once per class, while the room waited. Now:
///  - before the heats are posted, a late check-in goes straight in with
///    Add a Late Entry's rule, which moves nobody else (nobody has looked yet);
///  - after they're posted, Next Steps asks first ("Max Power checked in
///    after the heats were posted"), because drivers have looked up their
///    heat and the director decides. A club setting adds them without asking.
/// Either way the late driver goes into the heat nearest their pace that
/// has room (LateEntry.Place).
/// </summary>
public static class LateArrivals
{
    /// <summary>A driver in a class tonight who isn't in its heats yet.</summary>
    public sealed record Arrival(string ClassName, string Transponder);

    /// <summary>
    /// The drivers on a class's roster tonight (checked in, when the night
    /// counts only those) who are in none of that class's qualifiers. Only
    /// for a class whose heats have been built and still has one to run.
    /// Ones the director said not to add are left out.
    /// </summary>
    /// <param name="classes">Each racing class and its roster's transponders.</param>
    /// <param name="roundsFor">The class's rounds on tonight's program.</param>
    /// <param name="onTrackRoundId">The round on track now, which can't take anybody.</param>
    /// <param name="declined">"class|transponder" the director said not tonight to.</param>
    public static List<Arrival> Find(
        IEnumerable<(string ClassName, IReadOnlyList<string> Roster)> classes,
        Func<string, IReadOnlyList<RoundDefinition>> roundsFor,
        string? onTrackRoundId,
        IEnumerable<string>? declined)
    {
        var no = new HashSet<string>(declined ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var found = new List<Arrival>();

        foreach (var (className, roster) in classes)
        {
            var qualifiers = roundsFor(className).Where(r => r is not null && r.Kind == RoundKind.Qualifier).ToList();

            var stillToRun = qualifiers.Any(r => !r.IsRecorded && !string.Equals(r.Id, onTrackRoundId, StringComparison.Ordinal));

            if (qualifiers.Count == 0 || !stillToRun)
            {
                continue;
            }

            var inHeats = qualifiers.SelectMany(r => r.AssignedTransponders).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var transponder in roster)
            {
                if (string.IsNullOrWhiteSpace(transponder)
                    || GhostService.IsGhost(transponder)
                    || inHeats.Contains(transponder)
                    || no.Contains(Key(className, transponder)))
                {
                    continue;
                }

                found.Add(new Arrival(className, transponder));
            }
        }

        return found;
    }

    /// <summary>How a declined arrival is kept on the race day.</summary>
    public static string Key(string className, string transponder) => $"{className.Trim()}|{transponder.Trim()}";

    /// <summary>
    /// The Next Steps card's headline: "Max Power checked in after the heats
    /// were posted." or "3 drivers checked in after the heats were posted."
    /// </summary>
    public static string Headline(IReadOnlyList<string> names, bool clubNight)
    {
        var what = clubNight ? "races" : "heats";

        return names.Count == 1
            ? $"{names[0]} checked in after the {what} were posted."
            : $"{names.Count} drivers checked in after the {what} were posted.";
    }
}
