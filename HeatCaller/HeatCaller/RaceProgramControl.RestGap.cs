namespace HeatCaller;

/// <summary>
/// The rest gap, checked when the program is changed by hand (12 Nov 2026,
/// setup plan step 8). The build keeps tonight's "Races off between a
/// driver's races:" (RestOrder.Arrange); Move Drivers Between Heats and
/// dragging the running order used to let a director undo it without a
/// word. Now each warns before it saves a change that puts a driver closer
/// than that: "Max Power would race in Q2 Heat 1 and Q2 Heat 2 back to
/// back." Only clashes the change makes are named - the night may have had
/// some already (one tiny class has no choice), and those are not news.
/// </summary>
public sealed partial class RaceProgramControl
{
    /// <summary>Tonight's running order as (label, drivers), practice left out, with <paramref name="driversOf"/> for each round's drivers.</summary>
    private static List<(string Label, IReadOnlyCollection<string> Drivers)> RunningOrder(
        IEnumerable<RoundDefinition> inOrder, Func<RoundDefinition, IReadOnlyCollection<string>> driversOf) =>
        inOrder
            .Where(r => r.Kind != RoundKind.Practice)
            .Select(r => (r.RoundLabel, driversOf(r)))
            .ToList();

    /// <summary>
    /// The question to ask before a change by hand is saved, or null when it
    /// puts nobody closer than tonight's gap.
    /// </summary>
    private string? RestGapWarning(IReadOnlyList<RoundDefinition> orderAfter, Func<RoundDefinition, IReadOnlyCollection<string>> driversAfter)
    {
        var gap = RestGapTonight;

        if (gap == 0)
        {
            return null;
        }

        try
        {
            var before = RunningOrder(DayRounds(), r => r.AssignedTransponders);
            var after = RunningOrder(orderAfter, driversAfter);
            var tooClose = RestOrder.NewlyTooClose(before, after, gap);

            if (tooClose.Count == 0)
            {
                return null;
            }

            string NameOf(string transponder) =>
                _profiles.Get(transponder)?.DriverName is { Length: > 0 } name ? name : transponder;

            return RestOrder.Warning(tooClose, NameOf, gap) + "\r\n\r\nSave the change anyway?";
        }
        catch (Exception ex)
        {
            // A warning is never worth refusing the change over.
            StatusMessage?.Invoke($"[program] The rest gap could not be checked: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Move Drivers Between Heats' changes, checked against the gap - the
    /// question for its Save, or null. Asked by the form itself, so No leaves
    /// it open to put right.
    /// </summary>
    private string? RosterRestWarning(IReadOnlyList<HeatBalanceForm.RosterChange> changes)
    {
        var after = changes.ToDictionary(c => c.RoundId, c => (IReadOnlyCollection<string>)c.After, StringComparer.Ordinal);
        return RestGapWarning(DayRounds(), r => after.TryGetValue(r.Id, out var drivers) ? drivers : r.AssignedTransponders);
    }

    /// <summary>A drag of the running order, checked against the gap: true to go ahead.</summary>
    private bool MoveKeepsRest(IReadOnlyList<string> ids, string? anchorId)
    {
        var day = DayRounds();
        var moving = ids.ToHashSet(StringComparer.Ordinal);

        if (anchorId is not null && moving.Contains(anchorId))
        {
            return true; // goes nowhere - see RoundDefinitionStore.MoveBefore
        }

        var block = day.Where(r => moving.Contains(r.Id)).ToList();
        var rest = day.Where(r => !moving.Contains(r.Id)).ToList();
        var at = anchorId is null ? rest.Count : rest.FindIndex(r => r.Id == anchorId);
        rest.InsertRange(at < 0 ? rest.Count : at, block);

        var warning = RestGapWarning(rest, r => r.AssignedTransponders);

        return warning is null
               || AppDialog.Show(this, warning, "Too close together", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                   MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }
}
