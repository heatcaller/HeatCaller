using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// One recorded car setup, filled in against a ChassisTemplate.
///
/// Values are keyed by SetupField.Id and stored as strings regardless of
/// the field's kind - MultiChoice as a comma-separated list, Boolean as
/// "true" or empty. Strings rather than typed values so a template's field
/// can be edited later (an option renamed, a kind changed) without
/// invalidating entries already saved against it.
/// </summary>
public sealed class SetupEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    public string DriverName { get; set; } = string.Empty;
    public string TrackLayoutName { get; set; } = string.Empty;
    public string CarModel { get; set; } = string.Empty;

    /// <summary>
    /// The car in the Garage this setup belongs to, or null.
    ///
    /// Null for every entry written before the Garage existed, and for one
    /// logged against a driver who has no car on file yet - which is why
    /// the Garage falls back to matching on <see cref="DriverName"/> and
    /// <see cref="CarModel"/> when this is not set. Nothing is rewritten
    /// behind the user's back: an old entry gains an id the first time it
    /// is opened and saved against a car.
    /// </summary>
    public string? CarId { get; set; }

    /// <summary>Which setup-sheet template this entry was filled in against.</summary>
    public string? ChassisTemplateId { get; set; }

    /// <summary>Field values, keyed by SetupField.Id.</summary>
    public Dictionary<string, string> Values { get; set; } = new();

    // --- Legacy fields ---
    // The setup log originally had five fixed text fields, before
    // templates existed. Kept so older entries still load and display
    // rather than appearing blank. New entries leave these empty.
    public string Tires { get; set; } = string.Empty;
    public string Gearing { get; set; } = string.Empty;
    public string Springs { get; set; } = string.Empty;
    public string RideHeight { get; set; } = string.Empty;
    public string Motor { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    /// <summary>Optional link to the session this setup was run in, so lap times can be shown beside it.</summary>
    public string? LinkedSessionId { get; set; }

    /// <summary>True for entries saved before templates existed - they carry only the legacy fields.</summary>
    public bool IsLegacy => ChassisTemplateId is null && Values.Count == 0;

    /// <summary>How many fields have been filled in - shown in the log list so partial entries are visible.</summary>
    public int FilledCount => Values.Count(kv => !string.IsNullOrWhiteSpace(kv.Value));

    /// <summary>
    /// Folds the old fixed fields into the template's fields, matching on
    /// label. Called once when an old entry is first opened under the new
    /// model, so nothing recorded before templates existed is stranded.
    ///
    /// A field whose whole label is the old field's name wins over one that
    /// merely contains it. Taking the first label that contained the name
    /// put the old Motor value into the Kyosho sheet's "Motor mount", which
    /// comes before "Motor", and left "Motor" empty.
    ///
    /// An old value with no field to go to - the Kyosho sheet has nothing
    /// called ride height - is added to the end of the sheet's notes, with
    /// its old name in front ("Ride height: 3mm"). Left out, it stayed only
    /// in the old properties, which nothing reads once the entry is saved
    /// against a sheet, so it vanished from the log, the Garage and the
    /// comparison. It is added after the old notes themselves, so those
    /// still come first.
    /// </summary>
    public void MigrateLegacyFields(ChassisTemplate template)
    {
        var legacy = new (string Value, string Caption, string[] Aliases)[]
        {
            (Tires, "Tires", new[] { "tyre", "tire" }),
            (Gearing, "Gearing", new[] { "pinion", "gear" }),
            (Springs, "Springs", new[] { "spring" }),
            (RideHeight, "Ride height", new[] { "ride height" }),
            (Motor, "Motor", new[] { "motor" }),
        };

        var leftovers = new List<string>();

        foreach (var (value, caption, aliases) in legacy)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var match = FindLegacyField(template, aliases);

            if (match is not null && !Values.ContainsKey(match.Id))
            {
                Values[match.Id] = value;
            }
            else
            {
                leftovers.Add($"{caption}: {value.Trim()}");
            }
        }

        // Only a free-text field takes the notes: one called "Notes" first,
        // then any whose label mentions a note. A choice or tick field would
        // show the text as a stray option, or not at all.
        var notesField = FindLegacyField(template, new[] { "notes", "note" }, SetupFieldKind.Text);

        if (notesField is null)
        {
            // No notes field on this sheet: the old values stay in the old
            // properties, as before, and nothing is placed where it does not belong.
            return;
        }

        var notes = Values.TryGetValue(notesField.Id, out var already) && !string.IsNullOrWhiteSpace(already)
            ? already.Trim()
            : Notes.Trim();

        // Joined on one line: the notes box on the setup sheet is a
        // single-line box, which would show a line break as nothing at all.
        var parts = new List<string>();
        if (notes.Length > 0) { parts.Add(notes); }
        parts.AddRange(leftovers);

        if (parts.Count > 0)
        {
            Values[notesField.Id] = string.Join("; ", parts);
        }
    }

    /// <summary>
    /// The field an old fixed field belongs in: one whose whole label is an
    /// alias (or its plural) first, then the first whose label contains
    /// one. Fields of another kind than <paramref name="kind"/> are passed
    /// over when a kind is given. Null when nothing fits.
    /// </summary>
    private static SetupField? FindLegacyField(ChassisTemplate template, string[] aliases, SetupFieldKind? kind = null)
    {
        var fields = template.AllFields.Where(f => kind is null || f.Kind == kind).ToList();

        return fields.FirstOrDefault(f => aliases.Any(a =>
                   string.Equals(f.Label.Trim(), a, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(f.Label.Trim(), a + "s", StringComparison.OrdinalIgnoreCase)))
               ?? fields.FirstOrDefault(f => aliases.Any(a => f.Label.Contains(a, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Short one-line summary of the filled values, for the log list.</summary>
    public string Summarise(ChassisTemplate? template)
    {
        if (template is null)
        {
            // No template: fall back to whatever the legacy fields hold.
            var legacyParts = new[] { Tires, Gearing, Springs, RideHeight, Motor }
                .Where(v => !string.IsNullOrWhiteSpace(v));
            return string.Join("  |  ", legacyParts);
        }

        var parts = template.AllFields
            .Where(f => Values.TryGetValue(f.Id, out var v) && !string.IsNullOrWhiteSpace(v))
            .Take(5)
            .Select(f => $"{f.Label}: {Values[f.Id]}");

        return string.Join("  |  ", parts);
    }

    /// <summary>
    /// Copy for "duplicate as a starting point". Deliberately drops the id,
    /// timestamp and session link - those belong to the original run, not
    /// the new one.
    /// </summary>
    public SetupEntry CloneForNewEntry() => new()
    {
        DriverName = DriverName,
        TrackLayoutName = TrackLayoutName,
        CarModel = CarModel,

        // The car comes with it: a duplicate is the next setup on the same
        // car, which is the whole reason for duplicating one.
        CarId = CarId,
        ChassisTemplateId = ChassisTemplateId,
        Values = new Dictionary<string, string>(Values),
    };
}

/// <summary>Loads/saves setup entries to %AppData%\HeatCaller\setup-log.json.</summary>
public sealed class SetupLogStore
{
    private readonly List<SetupEntry> _entries;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "setup-log.json");

    private SetupLogStore(List<SetupEntry> entries)
    {
        _entries = entries;
    }

    public static SetupLogStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<SetupEntry>>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (list is not null)
                {
                    return new SetupLogStore(list);
                }
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open -
            // but, when it could not be read at all or no copy of it could
            // be kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new SetupLogStore(new List<SetupEntry>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds every setup the
    /// club's drivers have logged, nothing anybody could type back in. It
    /// used to be loaded as empty and then written over whole at the first
    /// setup logged, so a backup or sync tool holding it open for a moment
    /// at launch was enough to lose all of it. What this store holds is then
    /// only what was logged this session, and none of it is saved.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    private void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this session's near-empty log would replace it. The console has
        // already told the operator why and asked for a restart.
        if (LoadedUnreadable)
        {
            return;
        }

        // SafeFile, like the other stores. A bare WriteAllText threw out of
        // every setup edit on a locked or full disk, and a crash partway
        // through left a truncated log behind.
        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Newest first - the most recent setup is almost always the one being looked for.</summary>
    public IReadOnlyList<SetupEntry> All => _entries.OrderByDescending(e => e.RecordedAtUtc).ToList();

    public SetupEntry? Get(string id) => _entries.FirstOrDefault(e => e.Id == id);

    public void Upsert(SetupEntry entry)
    {
        var index = _entries.FindIndex(e => e.Id == entry.Id);
        if (index >= 0)
        {
            _entries[index] = entry;
        }
        else
        {
            _entries.Add(entry);
        }
        Save();
    }

    public void Delete(string id)
    {
        _entries.RemoveAll(e => e.Id == id);
        Save();
    }

    /// <summary>
    /// Links entries to garage cars in one save: each id to its car, and the
    /// car's name written where the entry kept a typed one. Ids no longer
    /// here are skipped. Returns how many were linked.
    /// </summary>
    public int LinkToCars(IReadOnlyList<(string Id, string CarId, string CarName)> links)
    {
        var linked = 0;

        foreach (var (id, carId, carName) in links)
        {
            if (Get(id) is { } entry)
            {
                entry.CarId = carId;
                entry.CarModel = carName;
                linked++;
            }
        }

        if (linked > 0)
        {
            Save();
        }

        return linked;
    }

    // --- Undo -------------------------------------------------------------

    /// <summary>A detached copy of an entry, same id - for an edit dialog to work on and an undo to put back.</summary>
    public static SetupEntry CopyOf(SetupEntry entry) =>
        JsonSerializer.Deserialize<SetupEntry>(JsonSerializer.Serialize(entry))!;

    /// <summary>True when any entry was filled in against this chassis sheet.</summary>
    public bool UsesTemplate(string templateId) => _entries.Any(e => e.ChassisTemplateId == templateId);

    /// <summary>
    /// The undo half of Upsert: <paramref name="before"/> goes back over the
    /// entry with this id, or - when it is null, because the Upsert added the
    /// entry - that entry is removed. Returns false when it has been deleted
    /// since, and nothing is changed.
    /// </summary>
    public bool UndoUpsert(string id, SetupEntry? before)
    {
        var index = _entries.FindIndex(e => e.Id == id);

        if (index < 0)
        {
            return false;
        }

        if (before is null)
        {
            _entries.RemoveAt(index);
        }
        else
        {
            _entries[index] = before;
        }

        Save();
        return true;
    }

    /// <summary>The undo half of deleting entries: each one back, in one save, skipping any whose id is back already.</summary>
    public void RestoreDeleted(IEnumerable<SetupEntry> removed)
    {
        foreach (var entry in removed)
        {
            if (Get(entry.Id) is null)
            {
                _entries.Add(entry);
            }
        }

        Save();
    }
}
