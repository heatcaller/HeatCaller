using System.Text.Json;

namespace HeatCaller;

/// <summary>How an inspection came out.</summary>
public enum TechResult
{
    /// <summary>Passed - clear to race.</summary>
    Passed,

    /// <summary>Failed - something needs fixing before racing.</summary>
    Failed,

    /// <summary>Passed subject to a fix being made. Common in practice: a minor issue the driver agrees to sort before the next heat.</summary>
    PassedWithNote,
}

/// <summary>One car's inspection for one event day.</summary>
public sealed class TechInspection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public required string Transponder { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string CarModel { get; set; } = string.Empty;

    public TechResult Result { get; set; } = TechResult.Passed;

    /// <summary>Which checklist items were ticked. Keyed by item text, since the list is per class and editable.</summary>
    public List<string> ItemsPassed { get; set; } = new();

    /// <summary>Items explicitly marked as failing - kept separate from "not ticked" so an unchecked box isn't read as a failure.</summary>
    public List<string> ItemsFailed { get; set; } = new();

    public string Inspector { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public DateTime InspectedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The event this inspection was carried out at.
    ///
    /// Empty on everything recorded before events were the spine; those
    /// count only while recent (TechInspectionStore.NoRaceDayWindow). Events are the
    /// better boundary because a weekend meeting is one event across two
    /// days - under the day rule every car had to be re-scrutineered on
    /// Sunday morning for no reason other than the clock.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>Cleared to race - a note doesn't stop someone going out.</summary>
    public bool IsCleared => Result is TechResult.Passed or TechResult.PassedWithNote;

    public string ResultText => Result switch
    {
        TechResult.Passed => "Passed",
        TechResult.Failed => "FAILED",
        _ => "Passed (note)",
    };
}

/// <summary>
/// Loads/saves tech inspections.
///
/// Inspections are scoped to a DAY rather than being permanent: a car
/// passing scrutineering last month says nothing about its condition this
/// morning, so "has this driver passed tech?" always means today unless
/// asked otherwise.
/// </summary>
public sealed class TechInspectionStore
{
    private readonly List<TechInspection> _inspections;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "tech-inspections.json");

    private TechInspectionStore(List<TechInspection> inspections)
    {
        _inspections = inspections;
    }

    /// <summary>Which event new inspections are filed against, and which one "is this car cleared" asks about. Kept in step with the event store.</summary>
    public string CurrentEventId { get; set; } = RaceEventStore.LegacyEventId;

    public static TechInspectionStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<List<TechInspection>>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (loaded is not null)
                {
                    return new TechInspectionStore(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open.
            SafeFile.PreserveUnreadable(FilePath, ex);
        }

        return new TechInspectionStore(new List<TechInspection>());
    }

    public void Save()
    {
        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(_inspections, new JsonSerializerOptions { WriteIndented = true }));
    }

    public IReadOnlyList<TechInspection> All =>
        _inspections.OrderByDescending(i => i.InspectedAtUtc).ToList();

    /// <summary>Inspections carried out on a given local day.</summary>
    public IReadOnlyList<TechInspection> ForDay(DateTime localDay) =>
        _inspections
            .Where(i => i.InspectedAtUtc.ToLocalTime().Date == localDay.Date)
            .OrderByDescending(i => i.InspectedAtUtc)
            .ToList();

    /// <summary>
    /// The inspection that counts for this driver now, or null - the same
    /// one as <see cref="Latest"/>, kept under this name for the callers
    /// that already use it.
    ///
    /// It used to be "the newest inspection on today's calendar date",
    /// while Race Control's tech warning asked Latest, which goes by the
    /// race day. The two disagreed: on the second day of a weekend meeting
    /// Check-In showed a car as not inspected that Race Control counted as
    /// cleared, and a car inspected this morning before the race day was
    /// opened showed as passed at Check-In while Race Control warned about
    /// it. One rule everywhere, so every page says the same thing.
    /// </summary>
    public TechInspection? Today(string transponder, string? className = null) =>
        Latest(transponder, className);

    /// <summary>True when this driver is cleared to race today.</summary>
    public bool IsClearedToday(string transponder, string? className = null) =>
        Cleared(transponder, className) is not null;

    /// <summary>Every inspection carried out at an event, newest first.</summary>
    public IReadOnlyList<TechInspection> ForEvent(string eventId) =>
        _inspections
            .Where(i => string.Equals(i.EventId, eventId, StringComparison.Ordinal))
            .OrderByDescending(i => i.InspectedAtUtc)
            .ToList();

    /// <summary>
    /// The inspection that decides whether this car can go out, or null.
    ///
    /// Event first, day second. An inspection stamped with the current
    /// event counts however long ago it was - that is what makes a
    /// two-day meeting one meeting. Anything without an event is judged by
    /// how recent it is (NoRaceDayWindow) so records made before this
    /// existed still read correctly, rather than every car in the history
    /// silently becoming un-inspected.
    ///
    /// Class-scoped when a class is given, since a driver running two
    /// classes may have two cars and passing with one says nothing about
    /// the other.
    /// </summary>
    public TechInspection? Latest(string transponder, string? className = null)
    {
        bool Matches(TechInspection i) =>
            string.Equals(i.Transponder, transponder, StringComparison.OrdinalIgnoreCase)
            && (className is null || string.Equals(i.ClassName, className, StringComparison.OrdinalIgnoreCase));

        // The legacy holder is not a real meeting - it is where everything
        // goes when no race day is active, and it never ends. Letting "same
        // event" count however long ago there meant a pass stamped with it
        // never expired: a car scrutineered months ago still showed as
        // cleared. So with no real event the recent-window rule applies to
        // it too, exactly as it does to inspections with no event at all.
        var noRealEvent = string.Equals(CurrentEventId, RaceEventStore.LegacyEventId, StringComparison.Ordinal);

        var atEvent = _inspections
            .Where(i => Matches(i)
                        && string.Equals(i.EventId, CurrentEventId, StringComparison.Ordinal)
                        && (!noRealEvent || IsRecent(i)))
            .OrderByDescending(i => i.InspectedAtUtc)
            .FirstOrDefault();

        if (atEvent is not null)
        {
            return atEvent;
        }

        // No event at all, or the legacy holder, and recent. The holder had
        // to be in here: scrutineering usually starts before anybody opens
        // the race day, so those passes are stamped with the holder, and
        // reading only the blank ones threw away the whole morning's work
        // the moment the day was opened - every car that had already been
        // through came up as never inspected. The recent window is what
        // keeps the holder honest; it is doing that job either way.
        return _inspections
            .Where(i => Matches(i)
                        && (string.IsNullOrEmpty(i.EventId)
                            || string.Equals(i.EventId, RaceEventStore.LegacyEventId, StringComparison.Ordinal))
                        && IsRecent(i))
            .OrderByDescending(i => i.InspectedAtUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// How long an inspection counts when there is no race day to scope it.
    ///
    /// Long enough to cover the longest club night or race day from first
    /// scrutineering to the last main, short enough that the next meeting -
    /// tomorrow at the earliest - starts with every car to be looked at again.
    /// </summary>
    public static readonly TimeSpan NoRaceDayWindow = TimeSpan.FromHours(18);

    /// <summary>
    /// Inspected within <see cref="NoRaceDayWindow"/> of now.
    ///
    /// This used to be "on today's calendar date", and every pass expired at
    /// midnight: a club night running past twelve suddenly had every car
    /// un-inspected, and the tech warning listed the whole field for the
    /// last heats. A rolling window has no cliff at midnight.
    /// </summary>
    private static bool IsRecent(TechInspection inspection) =>
        inspection.InspectedAtUtc >= DateTime.UtcNow - NoRaceDayWindow;

    /// <summary>The inspection clearing this car to race, or null if there isn't one.</summary>
    public TechInspection? Cleared(string transponder, string? className = null) =>
        Latest(transponder, className) is { IsCleared: true } inspection ? inspection : null;

    public TechInspection? Get(string id) => _inspections.FirstOrDefault(i => i.Id == id);

    public void Upsert(TechInspection inspection)
    {
        // Stamped on the way in rather than at every call site, so a new
        // inspection cannot be filed against nothing by a caller that
        // predates events.
        if (string.IsNullOrEmpty(inspection.EventId))
        {
            inspection.EventId = CurrentEventId;
        }

        var index = _inspections.FindIndex(i => i.Id == inspection.Id);
        if (index >= 0)
        {
            _inspections[index] = inspection;
        }
        else
        {
            _inspections.Add(inspection);
        }

        Save();
    }

    public void Delete(string id)
    {
        _inspections.RemoveAll(i => i.Id == id);
        Save();
    }

    /// <summary>
    /// Re-files every inspection from one class name to another, for a
    /// class rename. Inspections hold the class by name, and "is this car
    /// cleared for this class" matches on it - so without this a rename
    /// quietly un-inspected every car in the class. Returns the ids of the
    /// inspections that moved, so an undo can move back exactly those.
    /// </summary>
    public IReadOnlyList<string> RenameClass(string oldName, string newName)
    {
        var moved = new List<string>();

        foreach (var inspection in _inspections)
        {
            if (string.Equals(inspection.ClassName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                inspection.ClassName = newName;
                moved.Add(inspection.Id);
            }
        }

        if (moved.Count > 0)
        {
            Save();
        }

        return moved;
    }

    /// <summary>
    /// The ids of the inspections carrying this class name now. Taken just
    /// before a rename, so its undo can leave alone an inspection that was
    /// already under the new name.
    /// </summary>
    public IReadOnlyList<string> WithClassName(string className) =>
        _inspections
            .Where(i => string.Equals(i.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Id)
            .ToList();

    /// <summary>
    /// The undo half of RenameClass: every inspection carrying
    /// <paramref name="newName"/> NOW goes back to <paramref name="oldName"/>,
    /// except those in <paramref name="leaveAlone"/>, which already carried
    /// the new name before the rename. Judged by what each one holds now, so
    /// a car inspected for the renamed class after the rename stays cleared
    /// for it once the old name is back.
    /// </summary>
    public void UndoRenameClass(string newName, string oldName, IReadOnlyCollection<string> leaveAlone)
    {
        var keep = leaveAlone.ToHashSet(StringComparer.Ordinal);
        var changed = false;

        foreach (var inspection in _inspections.Where(i => string.Equals(i.ClassName, newName, StringComparison.OrdinalIgnoreCase)
                                                           && !keep.Contains(i.Id)))
        {
            inspection.ClassName = oldName;
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    // --- Undo -------------------------------------------------------------
    //
    // Targeted: each undo touches only the inspections its action touched.
    // The whole-list Snapshot/Restore below put every inspection back as it
    // was, so undoing an older step also wiped out every car inspected since.

    /// <summary>A detached copy of one inspection, for an undo - or null when there is none with that id.</summary>
    public TechInspection? CopyOf(string id) =>
        Get(id) is { } inspection ? JsonSerializer.Deserialize<TechInspection>(JsonSerializer.Serialize(inspection)) : null;

    /// <summary>Where an inspection sits in the list, so a deleted one can go back in the same place.</summary>
    public int IndexOf(string id) => _inspections.FindIndex(i => i.Id == id);

    /// <summary>
    /// The undo half of Upsert: <paramref name="before"/> goes back over the
    /// inspection with this id, or - when it is null, because the Upsert
    /// filed a new one - that new one is removed. Returns false when the
    /// inspection has been deleted since, and nothing is changed.
    /// </summary>
    public bool UndoUpsert(string id, TechInspection? before)
    {
        var index = IndexOf(id);

        if (index < 0)
        {
            return false;
        }

        if (before is null)
        {
            _inspections.RemoveAt(index);
        }
        else
        {
            _inspections[index] = before;
        }

        Save();
        return true;
    }

    /// <summary>
    /// The undo half of a delete: each inspection back where it was (indexes
    /// as they were before any of them went), skipping one whose id is back
    /// already.
    /// </summary>
    public void RestoreDeleted(IReadOnlyList<(TechInspection Inspection, int Index)> removed)
    {
        foreach (var (inspection, index) in removed.OrderBy(r => r.Index))
        {
            if (IndexOf(inspection.Id) >= 0)
            {
                continue;
            }

            _inspections.Insert(Math.Clamp(index, 0, _inspections.Count), inspection);
        }

        Save();
    }

    public string Snapshot() => JsonSerializer.Serialize(_inspections);

    public void Restore(string snapshot)
    {
        try
        {
            var restored = JsonSerializer.Deserialize<List<TechInspection>>(snapshot);
            if (restored is null)
            {
                return;
            }

            _inspections.Clear();
            _inspections.AddRange(restored);
            Save();
        }
        catch
        {
            // A failed restore leaves the current state alone.
        }
    }
}
