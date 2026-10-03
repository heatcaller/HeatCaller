using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A transponder changing hands for good - sold, given or swapped between
/// two drivers: Transfer, on the old owner's Transponders tab in Driver Hub.
///
/// Only a driver's extra transponders move. Their main one is their key
/// (DriverProfile.Transponder): every result, check-in, PIN and badge is
/// filed under it, so it stays theirs, as it does when a car is transferred
/// (CarTransferring).
///
/// The transponder leaves the old owner's list altogether - not retired,
/// which would keep it theirs - and joins the new owner's, fitted to one of
/// their cars or moving between them, and replacing (retiring) one of theirs
/// if it is a replacement, as Belongs To does for a transponder nobody has.
/// Laps timed on it from then on are the new owner's. Races already run
/// stay with whoever ran them: they are filed under the main transponder.
/// </summary>
internal static class TransponderTransferring
{
    /// <summary>
    /// What to do: the transponder, whose it is now and who it goes to (main
    /// transponders), the new owner's car it is fitted to (empty: it moves
    /// between their cars), and the new owner's transponder it replaces
    /// (empty: none).
    /// </summary>
    public sealed record Plan(string Id, string FromKey, string ToKey, string CarId, string Replaces);

    private static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

    private static string ListOf(DriverProfile profile) =>
        JsonSerializer.Serialize(profile.Transponders ?? new List<OwnedTransponder>());

    /// <summary>Why this one can't be transferred from this driver - or null when it can.</summary>
    public static string? WhyNot(string id, DriverProfile from)
    {
        var wanted = id?.Trim() ?? string.Empty;

        if (string.Equals(wanted, from.Transponder?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return $"{wanted} is {NameOf(from)}'s main transponder. Their results, check-ins and badges are filed under it, so it stays theirs.";
        }

        return from.ExtraTransponders.Any(t => string.Equals(t.Id.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            ? null
            : $"{wanted} isn't {NameOf(from)}'s any more.";
    }

    /// <summary>
    /// Transfers the transponder, with one Undo step for both drivers. Null
    /// when done; otherwise why it was refused, and nothing has changed.
    /// </summary>
    public static string? Apply(Plan plan, ProfileStore profiles, CarStore cars, UndoService? undo)
    {
        var id = plan.Id?.Trim() ?? string.Empty;

        if (profiles.Get(plan.FromKey) is not { } from
            || !string.Equals(from.Transponder, plan.FromKey?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "The driver it was coming from is no longer on file. Nothing was changed.";
        }

        if (WhyNot(id, from) is { } refused)
        {
            return refused + " Nothing was changed.";
        }

        if (profiles.Get(plan.ToKey) is not { } to
            || !string.Equals(to.Transponder, plan.ToKey?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "That driver is no longer on file. Nothing was changed.";
        }

        if (string.Equals(from.Transponder, to.Transponder, StringComparison.OrdinalIgnoreCase))
        {
            return $"{id} is {NameOf(to)}'s already.";
        }

        var fromKey = from.Transponder;
        var toKey = to.Transponder;
        var fromBefore = ListOf(from);
        var toBefore = ListOf(to);

        // The old owner: it goes from their list altogether.
        var fromUpdated = ProfileStore.Snapshot(from);
        fromUpdated.Transponders ??= new List<OwnedTransponder>();
        fromUpdated.Transponders.RemoveAll(t => t is not null && string.Equals(t.Id?.Trim(), id, StringComparison.OrdinalIgnoreCase));

        // The new owner: in use, fitted to one of THEIR cars or moving
        // between them - a car that is no longer theirs, or retired, is not
        // somewhere it can be fitted - and replacing one of theirs.
        var toUpdated = ProfileStore.Snapshot(to);
        toUpdated.Transponders ??= new List<OwnedTransponder>();

        var carId = plan.CarId?.Trim() ?? string.Empty;

        if (carId.Length > 0 && !cars.OwnedBy(to).Any(c => !c.Retired && string.Equals(c.Id, carId, StringComparison.Ordinal)))
        {
            carId = string.Empty;
        }

        var now = DateTime.UtcNow;
        toUpdated.Transponders.Add(new OwnedTransponder { Id = id, CarId = carId, AddedUtc = now });

        var replaces = plan.Replaces?.Trim() ?? string.Empty;

        if (replaces.Length > 0
            && !string.Equals(replaces, id, StringComparison.OrdinalIgnoreCase)
            && to.AllTransponderIds.Contains(replaces, StringComparer.OrdinalIgnoreCase)
            && !to.IsRetired(replaces))
        {
            var replaced = toUpdated.EntryToChange(replaces);
            replaced.Retired = true;
            replaced.RetiredUtc = now;
        }
        else
        {
            replaces = string.Empty;
        }

        // The old owner first: until then the transponder is theirs, and the
        // new owner's list would clash with it.
        profiles.Upsert(fromUpdated);

        if (profiles.DescribeClash(toUpdated, editing: toKey) is { } clash)
        {
            // Somebody else has it by now: the old owner as they were.
            var restored = ProfileStore.Snapshot(profiles.Get(fromKey) ?? fromUpdated);
            restored.Transponders = JsonSerializer.Deserialize<List<OwnedTransponder>>(fromBefore) ?? new List<OwnedTransponder>();
            profiles.Upsert(restored);
            return $"{clash} Nothing was changed.";
        }

        profiles.Upsert(toUpdated);

        var fromAfter = ListOf(fromUpdated);
        var toAfter = ListOf(toUpdated);
        var fromName = NameOf(from);
        var toName = NameOf(to);

        // Captured once it has all been written - a transfer refused above
        // has put everything back itself and leaves no step.
        undo?.Capture($"Transfer transponder {id} to {toName}", () =>
        {
            // Only while both drivers' transponders are as this left them -
            // otherwise a change made since would be undone with it.
            if (profiles.Get(fromKey) is not { } fromNow
                || profiles.Get(toKey) is not { } toNow
                || ListOf(fromNow) != fromAfter
                || ListOf(toNow) != toAfter)
            {
                throw new InvalidOperationException(
                    $"{fromName}'s or {toName}'s transponders have been changed since, and undoing the transfer of {id} now would undo that too. Nothing was changed.");
            }

            // The new owner first, so the transponder is nobody's for a
            // moment rather than two drivers'.
            var toRestored = ProfileStore.Snapshot(toNow);
            toRestored.Transponders = JsonSerializer.Deserialize<List<OwnedTransponder>>(toBefore) ?? new List<OwnedTransponder>();
            profiles.Upsert(toRestored);

            var fromRestored = ProfileStore.Snapshot(fromNow);
            fromRestored.Transponders = JsonSerializer.Deserialize<List<OwnedTransponder>>(fromBefore) ?? new List<OwnedTransponder>();

            if (profiles.DescribeClash(fromRestored, editing: fromKey) is { } taken)
            {
                // Taken by a third driver in the moment between: the new
                // owner keeps it rather than nobody having it.
                profiles.Upsert(toNow);
                throw new InvalidOperationException($"{taken} Nothing was changed.");
            }

            profiles.Upsert(fromRestored);
        });

        return null;
    }

    /// <summary>Asks who it goes to (BelongsToForm), and transfers it. True when it was.</summary>
    public static bool Run(IWin32Window owner, string id, DriverProfile from, ProfileStore profiles, CarStore cars, UndoService undo)
    {
        if (WhyNot(id, from) is { } refused)
        {
            AppDialog.Show(owner, refused, "Transfer transponder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var lead = $"Give transponder {id} to another driver for good - sold, given or swapped. It's {NameOf(from)}'s now. "
                   + "Laps timed on it from then on count for the new owner; races already run stay with whoever ran them.";

        using var form = new BelongsToForm(id, lead, profiles, cars, fromDriver: from.Transponder);

        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            return false;
        }

        if (Apply(new Plan(id, from.Transponder, form.DriverKey, form.CarId, form.Replaces), profiles, cars, undo) is { } refusal)
        {
            AppDialog.Show(owner, refusal, "Transfer transponder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }
}
