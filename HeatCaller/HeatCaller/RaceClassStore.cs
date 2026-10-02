using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeatCaller;

/// <summary>Loads/saves RaceClass records to %AppData%\HeatCaller\race-classes.json.</summary>
public sealed class RaceClassStore
{
    private readonly Dictionary<string, RaceClass> _classes;

    /// <summary>
    /// How this store reads and writes classes - the file, and the undo in
    /// RevertEdit. One set of options for both, so an undo can never read a
    /// class differently from how the file does.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "race-classes.json");

    private RaceClassStore(Dictionary<string, RaceClass> classes)
    {
        _classes = classes;
    }

    public static RaceClassStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<RaceClass>>(SafeFile.ReadAllTextWithRetry(FilePath), JsonOptions);
                if (list is not null)
                {
                    // Not ToDictionary: one duplicated or missing id in a
                    // hand-edited or restored file would throw and lose
                    // every class. Last one wins.
                    var classes = new Dictionary<string, RaceClass>();
                    foreach (var c in list)
                    {
                        if (c is not null && !string.IsNullOrEmpty(c.Id))
                        {
                            classes[c.Id] = c;
                        }
                    }
                    return new RaceClassStore(classes);
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new RaceClassStore(new Dictionary<string, RaceClass>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. What this store holds is then a
    /// guess, and nothing it does this session is saved over the club's
    /// real file.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        if (LoadedUnreadable)
        {
            // See LoadedUnreadable: the club's real file stays as it is.
            return;
        }

        var json = JsonSerializer.Serialize(_classes.Values.ToList(), JsonOptions);
        SafeFile.WriteAllText(FilePath, json);
    }

    public IReadOnlyList<RaceClass> All => _classes.Values.OrderBy(c => c.Name).ToList();

    public RaceClass? Get(string id) => _classes.TryGetValue(id, out var c) ? c : null;

    public void Upsert(RaceClass raceClass)
    {
        _classes[raceClass.Id] = raceClass;
        Save();
    }

    public void Delete(string id)
    {
        _classes.Remove(id);
        Save();
    }

    /// <summary>
    /// The undo half of an edit to a class, setting by setting: each setting
    /// the edit changed (<paramref name="before"/> against
    /// <paramref name="afterJson"/>, the class as the edit left it) goes back
    /// to its old value on the class as it is NOW. A setting the edit didn't
    /// touch keeps what it holds now, so a change made to it since - in a
    /// later edit with an undo of its own - is not wiped out by undoing an
    /// older one.
    ///
    /// <paramref name="keepName"/> leaves the name alone, for when a rename
    /// is put back separately (it has to take drivers, rounds and the rest
    /// with it). Returns false when the class has been deleted since.
    /// </summary>
    public bool RevertEdit(RaceClass before, string afterJson, bool keepName)
    {
        if (!_classes.TryGetValue(before.Id, out var live))
        {
            return false;
        }

        var beforeNode = JsonSerializer.SerializeToNode(before, JsonOptions)?.AsObject();
        var afterNode = JsonNode.Parse(afterJson)?.AsObject();

        if (beforeNode is null || afterNode is null)
        {
            return false;
        }

        // Every old value is read back first and only then written onto the
        // class, so a setting that won't read leaves the class untouched
        // rather than half put back.
        var putBack = new List<(System.Reflection.PropertyInfo Property, object? Value)>();

        foreach (var (name, value) in beforeNode)
        {
            if (keepName && name == nameof(RaceClass.Name))
            {
                continue;
            }

            var old = value?.ToJsonString() ?? "null";
            var edited = afterNode[name]?.ToJsonString() ?? "null";

            if (old != edited && typeof(RaceClass).GetProperty(name) is { CanWrite: true } property)
            {
                // Read afresh from the snapshot's JSON, so the list or format
                // put back is a copy of its own, never shared with the
                // snapshot (which a second undo of the same step could hand
                // out again).
                putBack.Add((property, JsonSerializer.Deserialize(old, property.PropertyType, JsonOptions)));
            }
        }

        // Onto the class that is on file NOW, not a new copy swapped in for
        // it. Anything holding the class - a page, or another undo that
        // checks the class's format is still the very one it set - would
        // otherwise be left holding one the store no longer has. And only
        // the settings the edit changed are assigned: one it didn't touch
        // keeps the very same object, so such a check still recognises it.
        foreach (var (property, value) in putBack)
        {
            property.SetValue(live, value);
        }

        Save();
        return true;
    }

    /// <summary>
    /// True when another class (not <paramref name="exceptId"/>) already
    /// has this name, ignoring case. Every store that refers to a class
    /// does it by name and case-insensitively, so two classes called
    /// "Stock" and "stock" would share one set of drivers, rounds and
    /// championships without anyone meaning them to.
    /// </summary>
    public bool NameTaken(string name, string? exceptId) =>
        _classes.Values.Any(c => c.Id != exceptId
                                 && string.Equals(c.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What else has to follow when a class is renamed. Set once at start-up
    /// by whoever owns the other stores (MainForm); null leaves a rename as
    /// a rename of the class alone, which is what it used to be.
    ///
    /// Hung on this store rather than passed to each page because a class
    /// can be edited from two doors - the Classes page and the dialog on a
    /// driver profile - and both of them already hold this store.
    /// </summary>
    public ClassRenameCascade? RenameCascade { get; set; }

    /// <summary>
    /// The app default race format as it stands now - what a class with no
    /// format of its own runs under. Set once at start-up by whoever owns
    /// the settings (MainForm), for the same two-doors reason as
    /// RenameCascade. A sanctioning preset edits a copy of it for such a
    /// class; null leaves that copy at the built-in defaults, which drops
    /// the club's own minimum lap time, staging and the rest.
    /// </summary>
    public Func<RaceFormat>? AppDefaultFormat { get; set; }

    /// <summary>
    /// Where each class's handicap mode is kept (by class name), so deleting
    /// a class from either door - the Classes page, or the classes window
    /// opened from Driver Profiles - forgets its mode, and a class added
    /// later under the same name does not inherit it. Set once at start-up
    /// by MainForm, for the same two-doors reason as AppDefaultFormat.
    /// </summary>
    public HandicapStore? Handicaps { get; set; }
}

/// <summary>
/// What a class rename moved, and what already carried the new name before
/// it (the Already... lists), kept so the undo can tell the two apart.
/// </summary>
public sealed record ClassRenameReceipt(
    string OldName,
    string NewName,
    IReadOnlyList<string> Drivers,
    IReadOnlyList<string> Rounds,
    IReadOnlyList<string> Championships,
    IReadOnlyList<string> Inspections,
    IReadOnlyList<string> RaceDays,
    IReadOnlyList<string> RunSheetLines,
    IReadOnlyList<string> AlreadyDrivers,
    IReadOnlyList<string> AlreadyRounds,
    IReadOnlyList<string> AlreadyChampionships,
    IReadOnlyList<string> AlreadyInspections,
    IReadOnlyList<string> AlreadyRaceDays,
    IReadOnlyList<string> AlreadyRunSheetLines);

/// <summary>
/// Carries a class's new name into everything that holds it as text.
///
/// A class is referred to BY NAME almost everywhere - a driver's class,
/// a round's class, a championship's class, a tech inspection's class, a
/// race day's list of classes, a run sheet line's class, handicap modes.
/// Renaming the class and nothing else left all of those pointing at a
/// name that no longer existed: the class lost its drivers, its programme,
/// its qualifying and its championship in one click.
///
/// The stores this class is given are moved here directly, noting by id
/// what already carried the new name, so the undo leaves that alone rather
/// than moving back something that happened to carry the new name before
/// the rename. The rest (handicap modes, remembered filters) are reached
/// through Renamed, which the owner wires up and which the undo raises
/// again the other way round - handicap modes keep their own record of
/// what a rename replaced (HandicapStore.RenameClass).
///
/// Race days and run sheet lines used to be among those reached through
/// Renamed, and so were moved back by the undo wholesale - everything
/// under the new name, including what had carried it all along.
/// </summary>
public sealed class ClassRenameCascade
{
    private readonly ProfileStore _profiles;
    private readonly RoundDefinitionStore _rounds;
    private readonly SeasonStore _seasons;
    private readonly TechInspectionStore? _techInspections;
    private readonly RaceEventStore? _raceEvents;
    private readonly DayScheduleStore? _daySchedule;

    /// <param name="raceEvents">
    /// The race days. When they are given here, a Renamed handler must not
    /// rename them too: the undo would then move back what this leaves alone.
    /// </param>
    /// <param name="daySchedule">The run sheet lines - the same goes for these.</param>
    public ClassRenameCascade(
        ProfileStore profiles,
        RoundDefinitionStore rounds,
        SeasonStore seasons,
        TechInspectionStore? techInspections = null,
        RaceEventStore? raceEvents = null,
        DayScheduleStore? daySchedule = null)
    {
        _profiles = profiles;
        _rounds = rounds;
        _seasons = seasons;
        _techInspections = techInspections;
        _raceEvents = raceEvents;
        _daySchedule = daySchedule;
    }

    /// <summary>
    /// Raised with (old name, new name) for everything else that holds a
    /// class name. Raised again with the names swapped when a rename is
    /// undone.
    /// </summary>
    public event Action<string, string>? Renamed;

    public ClassRenameReceipt Rename(string oldName, string newName)
    {
        // What already carries the new name, noted before anything moves,
        // so the undo leaves it where it was. Nothing for a change of case
        // alone ("stock" to "Stock"): names match ignoring case, so every
        // one of the class's own items would be counted as "already there"
        // and the undo would move nothing back.
        var caseOnly = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
        var alreadyDrivers = caseOnly ? Array.Empty<string>() : _profiles.WithClassName(newName);
        var alreadyRounds = caseOnly ? Array.Empty<string>() : _rounds.WithClassName(newName);
        var alreadyChampionships = caseOnly ? Array.Empty<string>() : _seasons.WithClassName(newName);
        var alreadyInspections = caseOnly ? Array.Empty<string>() : _techInspections?.WithClassName(newName) ?? Array.Empty<string>();
        var alreadyRaceDays = caseOnly ? Array.Empty<string>() : _raceEvents?.WithClassName(newName) ?? Array.Empty<string>();
        var alreadyRunSheetLines = caseOnly ? Array.Empty<string>() : _daySchedule?.WithClassName(newName) ?? Array.Empty<string>();

        var drivers = _profiles.RenameClass(oldName, newName);
        var rounds = _rounds.RenameClass(oldName, newName);
        var championships = _seasons.RenameClass(oldName, newName);
        var inspections = _techInspections?.RenameClass(oldName, newName) ?? Array.Empty<string>();
        var raceDays = _raceEvents?.RenameClass(oldName, newName) ?? Array.Empty<string>();
        var runSheetLines = _daySchedule?.RenameClass(oldName, newName) ?? Array.Empty<string>();

        Renamed?.Invoke(oldName, newName);

        return new ClassRenameReceipt(oldName, newName, drivers, rounds, championships, inspections, raceDays, runSheetLines,
            alreadyDrivers, alreadyRounds, alreadyChampionships, alreadyInspections, alreadyRaceDays, alreadyRunSheetLines);
    }

    /// <summary>
    /// Puts the old name back on everything that carries the new one NOW,
    /// except what already carried it before the rename.
    ///
    /// Not "write the old name onto what the rename moved": between the
    /// rename and the undo a driver may have been moved to another class
    /// (and must not be dragged back), and a driver, round, inspection,
    /// race day or run sheet line may have been added under the new name
    /// (and must not be stranded under a name that is about to stop
    /// existing). What is held by name elsewhere - handicap modes and
    /// remembered filters - is moved back by the Renamed handler.
    /// </summary>
    public void Undo(ClassRenameReceipt receipt)
    {
        _profiles.UndoRenameClass(receipt.NewName, receipt.OldName, receipt.AlreadyDrivers);
        _rounds.UndoRenameClass(receipt.NewName, receipt.OldName, receipt.AlreadyRounds);
        _seasons.UndoRenameClass(receipt.NewName, receipt.OldName, receipt.AlreadyChampionships);
        _techInspections?.UndoRenameClass(receipt.NewName, receipt.OldName, receipt.AlreadyInspections);
        _raceEvents?.UndoRenameClass(receipt.NewName, receipt.OldName, receipt.AlreadyRaceDays, receipt.RaceDays);
        _daySchedule?.UndoRenameClass(receipt.NewName, receipt.OldName, receipt.AlreadyRunSheetLines);

        Renamed?.Invoke(receipt.NewName, receipt.OldName);
    }
}
