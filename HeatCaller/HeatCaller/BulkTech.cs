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
    /// row out or puts it in. So:
    ///
    /// - With Ctrl held, it means the drivers picked before it and the row
    ///   double-clicked, whether that row was one of them or not: Ctrl-picking
    ///   three and Ctrl+double-clicking a fourth means all four. (It used to
    ///   mean the fourth alone, and open its one-car inspection.)
    /// - With Shift held, it means the run its Shift+click picks, as the list
    ///   shows it now, and the row double-clicked.
    /// - With neither, a double-click on one of two or more picked rows means
    ///   all of them, and anywhere else the row clicked, as it always did.
    ///
    /// In the order they sit in the list.
    /// </summary>
    public static IReadOnlyList<int> RowsToActOn(
        IReadOnlyCollection<int> selectedBeforeClick,
        IReadOnlyCollection<int> selectedNow,
        int clickedRow,
        bool ctrl = false,
        bool shift = false)
    {
        if (clickedRow < 0)
        {
            return Array.Empty<int>();
        }

        IEnumerable<int> rows = ctrl ? selectedBeforeClick.Append(clickedRow)
            : shift ? selectedNow.Append(clickedRow)
            : selectedBeforeClick.Count > 1 && selectedBeforeClick.Contains(clickedRow) ? selectedBeforeClick
            : new[] { clickedRow };

        return rows.Where(row => row >= 0).Distinct().Order().ToList();
    }

    /// <summary>
    /// Whether a press on the roster is the second press of a double-click.
    ///
    /// The press's own click count is not relied on alone. A second press
    /// taken for a new click remembers the selection its first press has just
    /// narrowed to one row, and a double-click on one of several picked
    /// drivers then acts on that driver alone - the likeliest way a bulk
    /// inspection came out for one driver on 1 Nov 2026. So a press that
    /// comes within the double-click time of the last one, and near it,
    /// counts as its second press too - the same test Windows makes - and the
    /// press after that starts afresh.
    /// </summary>
    public static bool IsSecondPress(int clicks, long msSinceLastPress, Point at, Point lastAt, int doubleClickMs, Size doubleClickSize) =>
        clicks > 1
        || (msSinceLastPress >= 0
            && msSinceLastPress <= doubleClickMs
            && Math.Abs(at.X - lastAt.X) <= doubleClickSize.Width / 2
            && Math.Abs(at.Y - lastAt.Y) <= doubleClickSize.Height / 2);

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
