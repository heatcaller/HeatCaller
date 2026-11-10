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
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<List<TechInspection>>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (loaded is not null)
                {
                    return new TechInspectionStore(Repaired(loaded));
                }

                // A file holding the literal "null" parses to nothing without
                // throwing. It used to fall through to the empty store below
                // with the flag left false, so the first inspection of the
                // night replaced every earlier day's (as HandicapStore).
                unreadable = true;
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open -
            // but, when it could not be read at all or no copy of it could
            // be kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new TechInspectionStore(new List<TechInspection>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// Drops null entries (and ones with no transponder) and fills null
    /// lists and strings, which the deserializer returns as they are from a
    /// hand edit or a restored backup. Without this one null entry made every
    /// "is this car cleared?" throw - Who's Racing and staging a race with it.
    /// </summary>
    private static List<TechInspection> Repaired(List<TechInspection> inspections)
    {
        inspections.RemoveAll(i => i is null || string.IsNullOrWhiteSpace(i.Transponder));

        foreach (var i in inspections)
        {
            i.Id ??= Guid.NewGuid().ToString("N");
            i.DriverName ??= string.Empty;
            i.ClassName ??= string.Empty;
            i.CarModel ??= string.Empty;
            i.ItemsPassed ??= new();
            i.ItemsFailed ??= new();
            i.Inspector ??= string.Empty;
            i.Notes ??= string.Empty;
            i.EventId ??= string.Empty;
        }

        return inspections;
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds the club's
    /// day's scrutineering: nothing anybody could type back in. It
    /// used to be loaded as empty and then written over whole at the first
    /// change, so a backup or antivirus tool holding it open for a moment
    /// at launch was enough to lose all of it. What this store holds is
    /// then only what was added this session, and none of it is saved.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this session's near-empty list would replace it. The console
        // has already told the operator why and asked for a restart.
        if (LoadedUnreadable)
        {
            return;
        }

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(_inspections, new JsonSerializerOptions { WriteIndented = true }));
    }

    public IReadOnlyList<TechInspection> All =>
        _inspections.OrderByDescending(i => i.InspectedAtUtc).ToList();

    /// <summary>
    /// Whether a race day still exists - set by EventSpine.Bind from the
    /// event store, and asked by ForDay. A hook rather than a constructor
    /// argument for the same reason as DayScheduleStore.ProgramOrder: the
    /// stores load independently.
    ///
    /// Deleting a race day leaves its inspections here on purpose, so that
    /// undoing the delete brings them back - the same as its check-in sheet
    /// (CheckInStore.AdoptOrphans). Until then the day is gone, but ForDay
    /// went on listing them, so a test day created, scrutineered and
    /// deleted still put its inspections in that date's Event Report. Null
    /// (no event store to ask) counts every day as existing, as before.
    /// </summary>
    public Func<string, bool>? EventExists { get; set; }

    /// <summary>Inspections carried out on a given local day - leaving out any filed against a race day that has since been deleted (see EventExists).</summary>
    public IReadOnlyList<TechInspection> ForDay(DateTime localDay) =>
        _inspections
            .Where(i => i.InspectedAtUtc.ToLocalTime().Date == localDay.Date && OnExistingDay(i))
            .OrderByDescending(i => i.InspectedAtUtc)
            .ToList();

    /// <summary>
    /// False only for an inspection filed against a real race day that has
    /// been deleted. No event, the legacy holder, or no EventExists hook all
    /// count as there.
    /// </summary>
    private bool OnExistingDay(TechInspection inspection) =>
        EventExists is not { } exists
        || string.IsNullOrEmpty(inspection.EventId)
        || string.Equals(inspection.EventId, RaceEventStore.LegacyEventId, StringComparison.Ordinal)
        || exists(inspection.EventId);

    /// <summary>
    /// Every inspection that "is this car cleared?" can read right now,
    /// newest first: the ones Latest considers, for any driver and class.
    ///
    /// For the scrutineering log's "Today only". That used to be the
    /// calendar date (ForDay(DateTime.Now)), while clearance goes by the
    /// race day and the NoRaceDayWindow - so after midnight, or on the
    /// second day of a weekend meeting, the log showed nothing while every
    /// car it should have listed was still cleared to race.
    ///
    /// The same two rules as Latest: filed against the current event (and
    /// recent, when that is the legacy holder), or filed against no event
    /// or the legacy holder and recent.
    /// </summary>
    public IReadOnlyList<TechInspection> CountingNow()
    {
        var noRealEvent = string.Equals(CurrentEventId, RaceEventStore.LegacyEventId, StringComparison.Ordinal);

        return _inspections
            .Where(i => (string.Equals(i.EventId, CurrentEventId, StringComparison.Ordinal) && (!noRealEvent || IsRecent(i)))
                        || ((string.IsNullOrEmpty(i.EventId)
                             || string.Equals(i.EventId, RaceEventStore.LegacyEventId, StringComparison.Ordinal))
                            && IsRecent(i)))
            .OrderByDescending(i => i.InspectedAtUtc)
            .ToList();
    }

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

    /// <summary>
    /// The inspection the check-in desk and the phone show for a driver,
    /// across every class they race: the one for the first class they are
    /// NOT cleared in (null when that class has no inspection yet), or,
    /// cleared in all of them, the most recent of those. Only classes that
    /// <paramref name="techIsRunFor"/> says run tech are asked about; with
    /// none of those, it is their main class's latest, as it always was.
    ///
    /// Race Control asks about the round's class (UninspectedDrivers), and
    /// these used to ask about the driver's main class alone. A driver
    /// cleared in their main class but not in the other one they were
    /// racing showed as cleared at the desk, and then held up the start.
    /// Asking about every class they race makes the two agree.
    /// </summary>
    public TechInspection? LatestForDriver(DriverProfile profile, Func<string, bool> techIsRunFor)
    {
        var classes = profile.AllClasses.Where(techIsRunFor).ToList();

        if (classes.Count == 0)
        {
            return Latest(profile.Transponder, profile.ClassName ?? string.Empty);
        }

        TechInspection? newest = null;

        foreach (var className in classes)
        {
            var latest = Latest(profile.Transponder, className);

            if (latest is not { IsCleared: true })
            {
                return latest;
            }

            if (newest is null || latest.InspectedAtUtc > newest.InspectedAtUtc)
            {
                newest = latest;
            }
        }

        return newest;
    }

    /// <summary>
    /// The class <see cref="LatestForDriver"/> answers for, by the same
    /// rule: the first class they are not cleared in, or, cleared in all of
    /// them, the one with the newest pass - or their main class when no
    /// class they race runs tech.
    ///
    /// For Inspect Selected Car, which used to open the main class whatever
    /// the desk was showing. A driver cleared in Stock but not in Modified
    /// showed "-", and Inspect opened their Stock pass to edit: switching it
    /// to Modified and saving replaced the Stock inspection.
    /// </summary>
    public string TechClassForDriver(DriverProfile profile, Func<string, bool> techIsRunFor)
    {
        var classes = profile.AllClasses.Where(techIsRunFor).ToList();

        if (classes.Count == 0)
        {
            return profile.ClassName ?? string.Empty;
        }

        string? newestClass = null;
        TechInspection? newest = null;

        foreach (var className in classes)
        {
            var latest = Latest(profile.Transponder, className);

            if (latest is not { IsCleared: true })
            {
                return className;
            }

            if (newest is null || latest.InspectedAtUtc > newest.InspectedAtUtc)
            {
                newest = latest;
                newestClass = className;
            }
        }

        return newestClass!;
    }

    /// <summary>
    /// The classes a bulk log on Who's Racing inspects a driver in: every
    /// class they race that runs tech and they are not cleared in, in order -
    /// or, cleared in all of them (or none runs tech), the one class
    /// <see cref="TechClassForDriver"/> names.
    ///
    /// WHY (2 Nov 2026): a bulk log used to file one inspection per driver,
    /// in TechClassForDriver's class alone. A driver racing Stock and Box
    /// Stock got Box Stock passed, the Tech column - which reads every class
    /// they race - still showed "-" for Stock, and the bulk log looked as if
    /// it had skipped them.
    /// </summary>
    public IReadOnlyList<string> TechClassesForDriver(DriverProfile profile, Func<string, bool> techIsRunFor)
    {
        var notCleared = profile.AllClasses
            .Where(techIsRunFor)
            .Where(className => Latest(profile.Transponder, className) is not { IsCleared: true })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return notCleared.Count > 0 ? notCleared : new[] { TechClassForDriver(profile, techIsRunFor) };
    }

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
        //
        // WHY (23 Oct 2026, bug hunt 10): this stamped ANY inspection with
        // no race day, edits included. A pass recorded before race days
        // existed has none, so correcting a typo in its notes filed a
        // months-old pass under tonight's race day - and a pass filed under
        // the current race day counts however old it is, so the car showed
        // as cleared without anybody looking at it. Now only a new
        // inspection or a re-inspection is stamped. The edit form re-stamps
        // the time only when the car was looked at again, so a changed time
        // is what marks a re-inspection; a note-only edit keeps its blank
        // race day and stays judged by the recent window.
        var index = _inspections.FindIndex(i => i.Id == inspection.Id);
        var reinspection = index >= 0 && _inspections[index].InspectedAtUtc != inspection.InspectedAtUtc;
        if (string.IsNullOrEmpty(inspection.EventId) && (index < 0 || reinspection))
        {
            inspection.EventId = CurrentEventId;
        }

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
            _inspections.AddRange(Repaired(restored));
            Save();
        }
        catch
        {
            // A failed restore leaves the current state alone.
        }
    }
}
