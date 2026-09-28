namespace HeatCaller;

/// <summary>
/// What changed on a car between one logged setup and the one before it -
/// "Pinion 22 → 23, Front springs Soft → Medium".
/// </summary>
/// <remarks>
/// Two users (27 Sep 2026): the car's setup list in the Garage shows it
/// beside each entry, so a run of setups reads as a run of changes at a
/// glance; and the mechanic's interview after a main is told the latest
/// change, so what the mechanic says about the car is what was actually
/// logged.
///
/// Compared field by field on the stored strings, ignoring case and the
/// spaces round them - "medium" and "Medium " are the same choice. Notes
/// are left out: a changed note is not a changed car. Legacy entries (from
/// before setup sheets) compare their five fixed fields.
/// </remarks>
internal static class SetupDiff
{
    /// <param name="From">The earlier value; empty when it wasn't set.</param>
    /// <param name="To">The later value; empty when it has been cleared.</param>
    /// <param name="OnSheet">
    /// False for a value whose field is on neither setup's sheet any more -
    /// its Label is then the field's stored id, a code nobody would say out
    /// loud, so the mechanic isn't told about it. The Garage still lists it:
    /// the value did change.
    /// </param>
    internal sealed record Change(string Label, string From, string To, bool OnSheet = true);

    /// <summary>
    /// The setup logged for the same car just before this one, or null. Same
    /// car by its Garage id when the entry has one, and otherwise by driver
    /// and the typed car - all an entry from before the Garage has.
    /// </summary>
    internal static SetupEntry? Previous(SetupEntry entry, IEnumerable<SetupEntry> all) =>
        all.Where(other =>
                !string.Equals(other.Id, entry.Id, StringComparison.Ordinal)
                && other.RecordedAtUtc < entry.RecordedAtUtc
                && SameCar(entry, other))
            .OrderByDescending(other => other.RecordedAtUtc)
            .FirstOrDefault();

    private static bool SameCar(SetupEntry a, SetupEntry b)
    {
        if (!string.IsNullOrEmpty(a.CarId) || !string.IsNullOrEmpty(b.CarId))
        {
            return string.Equals(a.CarId, b.CarId, StringComparison.Ordinal);
        }

        return string.Equals(a.DriverName.Trim(), b.DriverName.Trim(), StringComparison.OrdinalIgnoreCase)
               && string.Equals(a.CarModel.Trim(), b.CarModel.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What changed since the setup before this one on the same car - null
    /// when there is none to compare with. A setup from before setup sheets
    /// and one filled in on a sheet aren't compared: every field would read
    /// as a change.
    /// </summary>
    internal static IReadOnlyList<Change>? SincePrevious(SetupEntry entry, IEnumerable<SetupEntry> all, Func<string?, ChassisTemplate?> templateFor)
    {
        if (Previous(entry, all) is not { } previous || previous.IsLegacy != entry.IsLegacy)
        {
            return null;
        }

        return Between(previous, entry, templateFor);
    }

    /// <summary>Every field whose value differs between the two, in the sheet's own order.</summary>
    /// <param name="templateFor">Looks a setup sheet up by id - for the fields' names and order.</param>
    internal static IReadOnlyList<Change> Between(SetupEntry older, SetupEntry newer, Func<string?, ChassisTemplate?> templateFor)
    {
        var changes = new List<Change>();

        if (older.IsLegacy && newer.IsLegacy)
        {
            Compare(changes, "Tires", older.Tires, newer.Tires);
            Compare(changes, "Gearing", older.Gearing, newer.Gearing);
            Compare(changes, "Springs", older.Springs, newer.Springs);
            Compare(changes, "Ride height", older.RideHeight, newer.RideHeight);
            Compare(changes, "Motor", older.Motor, newer.Motor);
            return changes;
        }

        var newerSheet = templateFor(newer.ChassisTemplateId);
        var olderSheet = templateFor(older.ChassisTemplateId);

        // The newer sheet's order first, then anything only the older one had.
        var ids = new List<string>();

        foreach (var field in (newerSheet?.AllFields ?? Enumerable.Empty<SetupField>())
                     .Concat(olderSheet?.AllFields ?? Enumerable.Empty<SetupField>()))
        {
            if (!ids.Contains(field.Id))
            {
                ids.Add(field.Id);
            }
        }

        foreach (var id in newer.Values.Keys.Concat(older.Values.Keys))
        {
            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        foreach (var id in ids)
        {
            var field = newerSheet?.FindField(id) ?? olderSheet?.FindField(id);
            var label = field?.Label ?? id;

            if (label.Contains("note", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Compare(
                changes,
                label,
                Show(field, older.Values.GetValueOrDefault(id)),
                Show(field, newer.Values.GetValueOrDefault(id)),
                onSheet: field is not null);
        }

        return changes;
    }

    private static void Compare(List<Change> changes, string label, string? from, string? to, bool onSheet = true)
    {
        var a = (from ?? string.Empty).Trim();
        var b = (to ?? string.Empty).Trim();

        if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add(new Change(label, a, b, onSheet));
        }
    }

    /// <summary>A stored value as a person reads it: a tick as on or off, a number with its unit.</summary>
    private static string Show(SetupField? field, string? value)
    {
        var text = (value ?? string.Empty).Trim();

        if (field is null)
        {
            return text;
        }

        // A unit left null by a hand-edited or restored sheet reads as none.
        var unit = field.Unit ?? string.Empty;

        return field.Kind switch
        {
            SetupFieldKind.Boolean => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? "on" : "off",

            // "23T" typed into a field whose unit is T stays "23T".
            SetupFieldKind.Number when text.Length > 0 && unit.Length > 0 && !text.EndsWith(unit, StringComparison.OrdinalIgnoreCase) => text + unit,

            // In a fixed order, so ticking the same boxes in another order
            // isn't a change.
            SetupFieldKind.MultiChoice => string.Join(", ", text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)),

            _ => text,
        };
    }

    /// <summary>
    /// For the Garage list: "Pinion 22 → 23, Front springs Soft → Medium and 2
    /// more". "(first setup)" when there is nothing to compare with, and
    /// "no changes" when nothing differs.
    /// </summary>
    internal static string Describe(IReadOnlyList<Change>? changes, int max = 3)
    {
        if (changes is null)
        {
            return "(first setup)";
        }

        if (changes.Count == 0)
        {
            return "no changes";
        }

        var shown = changes.Take(max).Select(c =>
            c.From.Length == 0 ? $"{c.Label} → {c.To}"
            : c.To.Length == 0 ? $"{c.Label} {c.From} → (none)"
            : $"{c.Label} {c.From} → {c.To}");

        var more = changes.Count > max ? $" and {changes.Count - max} more" : string.Empty;

        return string.Join(", ", shown) + more;
    }

    /// <summary>
    /// The values filled in on one setup, in the sheet's order - "Pinion
    /// 23T", "Front springs Medium" - for a car's first setup, which has
    /// nothing to compare with. Notes are left out, as in Between, and so is
    /// a tick box that isn't ticked (an empty field was never set) and a
    /// value whose field is no longer on the sheet.
    /// </summary>
    internal static IReadOnlyList<string> Filled(SetupEntry entry, Func<string?, ChassisTemplate?> templateFor, int max = 4)
    {
        var filled = new List<string>();

        void Add(string label, string shown)
        {
            if (shown.Length > 0 && !label.Contains("note", StringComparison.OrdinalIgnoreCase))
            {
                filled.Add($"{label} {shown}");
            }
        }

        if (entry.IsLegacy)
        {
            Add("Tires", (entry.Tires ?? string.Empty).Trim());
            Add("Gearing", (entry.Gearing ?? string.Empty).Trim());
            Add("Springs", (entry.Springs ?? string.Empty).Trim());
            Add("Ride height", (entry.RideHeight ?? string.Empty).Trim());
            Add("Motor", (entry.Motor ?? string.Empty).Trim());
        }
        else
        {
            var sheet = templateFor(entry.ChassisTemplateId);

            var ids = (sheet?.AllFields.Select(field => field.Id) ?? Enumerable.Empty<string>())
                .Concat(entry.Values.Keys)
                .Distinct(StringComparer.Ordinal);

            foreach (var id in ids)
            {
                var raw = entry.Values.GetValueOrDefault(id);

                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                // A value whose field is no longer on the sheet has only its
                // stored id for a name - not something to say out loud.
                if (sheet?.FindField(id) is not { } field)
                {
                    continue;
                }

                if (field.Kind == SetupFieldKind.Boolean && !string.Equals(raw.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Add(field.Label ?? id, Show(field, raw));
            }
        }

        return filled.Take(max).ToList();
    }

    /// <summary>How many changes the mechanic is told about by name; the rest are counted.</summary>
    internal const int SpokenMax = 4;

    /// <summary>
    /// For the mechanic's facts: one change per line, in words -
    /// "Pinion changed from 22 to 23". At most <paramref name="max"/>.
    /// </summary>
    internal static IReadOnlyList<string> Spoken(IReadOnlyList<Change> changes, int max = SpokenMax) =>
        changes.Take(max).Select(c =>
                c.From.Length == 0 ? $"{c.Label} set to {c.To}"
                : c.To.Length == 0 ? $"{c.Label} no longer set (was {c.From})"
                : $"{c.Label} changed from {c.From} to {c.To}")
            .ToList();
}
