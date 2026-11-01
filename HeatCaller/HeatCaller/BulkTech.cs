namespace HeatCaller;

/// <summary>
/// The rules behind acting on several drivers at once on Who's Racing (1 Nov
/// 2026): which rows a double-click means, which inspection the desk opens
/// for editing, and the inspections a bulk log files.
///
/// Kept apart from the page so each rule can be checked without a window,
/// and so the one-driver Inspect and the bulk log share one answer to "which
/// of today's inspections is this car's" rather than two that drift.
/// </summary>
internal static class BulkTech
{
    /// <summary>
    /// How long after midnight an inspection still counts as "today" for
    /// editing from the desk: a club night running past twelve edits the
    /// inspection made before it rather than starting over.
    /// </summary>
    public static readonly TimeSpan PastMidnight = TimeSpan.FromHours(6);

    /// <summary>
    /// The rows a double-click acts on.
    ///
    /// The selection the double-click is ABOUT is the one standing before its
    /// first click: that click is an ordinary click, and an ordinary click
    /// narrows the selection to its own row - or, with Ctrl held, takes its
    /// row out. So a double-click on a row that was part of a selection of two
    /// or more means all of them, in the order they sit in the list. Anywhere
    /// else it means the row clicked, as it always did.
    /// </summary>
    public static IReadOnlyList<int> RowsToActOn(IReadOnlyCollection<int> selectedBeforeClick, int clickedRow)
    {
        if (clickedRow < 0)
        {
            return Array.Empty<int>();
        }

        if (selectedBeforeClick.Count > 1 && selectedBeforeClick.Contains(clickedRow))
        {
            return selectedBeforeClick.Distinct().Order().ToList();
        }

        return new[] { clickedRow };
    }

    /// <summary>
    /// The inspection the desk opens to edit rather than filing a new one:
    /// the latest, but only when it was made today or in the last few hours
    /// (see <see cref="PastMidnight"/>). A race day can span a weekend, and
    /// the inspection that counts on Sunday morning can be Saturday's -
    /// editing that one overwrote Saturday's record with Sunday's look at the
    /// car, instead of filing a new one for Sunday.
    /// </summary>
    public static TechInspection? EditableToday(TechInspection? latest, DateTime nowUtc)
    {
        if (latest is null)
        {
            return null;
        }

        var today = nowUtc.ToLocalTime().Date;

        return latest.InspectedAtUtc.ToLocalTime().Date == today || nowUtc - latest.InspectedAtUtc < PastMidnight
            ? latest
            : null;
    }

    /// <summary>One car in a bulk log: who, the class it is inspected in, and today's inspection of it if there is one.</summary>
    public sealed record Car(DriverProfile Profile, string ClassName, TechInspection? Existing);

    /// <summary>
    /// The inspections a bulk log files: one per car, every one with the
    /// result, inspector and notes the scrutineer gave once.
    ///
    /// - A car already inspected today has that inspection updated, as the
    ///   one-driver Inspect does, rather than a second one filed beside it.
    ///   The record handed back is a copy; the store's is never touched here.
    /// - A pass marks every check on the class's checklist OK, so the
    ///   inspection reads "All checks passed" when it is opened later. A pass
    ///   with a note or a failure marks none: nothing says which check it was,
    ///   and the inspection window asks about a hand-picked failure when it
    ///   is edited.
    /// - A blank inspector or blank notes keep what today's inspection
    ///   already had, so a quick bulk pass does not wipe notes made at the
    ///   bench.
    /// - Each is stamped now, with no race day: the store files it under the
    ///   race day that is open (TechInspectionStore.Upsert).
    /// </summary>
    public static List<TechInspection> Build(
        IEnumerable<Car> cars,
        TechResult result,
        string inspector,
        string notes,
        Func<string, IReadOnlyList<string>> checklistFor,
        DateTime nowUtc)
    {
        var inspectorTyped = inspector.Trim();
        var notesTyped = notes.Trim();
        var built = new List<TechInspection>();
        var seen = new HashSet<(string, string)>();

        foreach (var car in cars)
        {
            // One car per driver and class: the same row picked twice files once.
            if (!seen.Add((car.Profile.Transponder.ToUpperInvariant(), car.ClassName.ToUpperInvariant())))
            {
                continue;
            }

            var existing = car.Existing;

            var record = new TechInspection
            {
                Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
                Transponder = car.Profile.Transponder,
                DriverName = car.Profile.DriverName,
                ClassName = car.ClassName,
                CarModel = existing is { CarModel.Length: > 0 } ? existing.CarModel : car.Profile.CarModel,
                Result = result,
                ItemsPassed = result == TechResult.Passed ? checklistFor(car.ClassName).ToList() : new List<string>(),
                ItemsFailed = new List<string>(),
                Inspector = inspectorTyped.Length > 0 ? inspectorTyped : existing?.Inspector ?? string.Empty,
                Notes = notesTyped.Length > 0 ? notes : existing?.Notes ?? string.Empty,
                InspectedAtUtc = nowUtc,
                EventId = string.Empty,
            };

            built.Add(record);
        }

        return built;
    }

    /// <summary>"Ana Lopez, Ben Ortiz and 3 more" - the drivers a bulk action is for, said in one line.</summary>
    public static string NamesLine(IReadOnlyList<string> names, int shown = 6)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        if (names.Count == 1)
        {
            return names[0];
        }

        if (names.Count <= shown)
        {
            return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        }

        return string.Join(", ", names.Take(shown)) + $" and {names.Count - shown} more";
    }
}
