using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Who turned up - per event.
///
/// A "waiver signed" tick used to live here beside the check-in. Nothing
/// in the app ever read it: no page, no heat build, no publish, no
/// report. It was a box that recorded a fact and was then never consulted
/// by anything, which is worse than not asking at all - it looks like a
/// gate. It has been taken out. The stored key is simply ignored when an
/// older file is read, so nothing has to be migrated.
///
/// This used to be one global list with a "Check Out Everyone" button, on
/// the understanding that somebody would remember to press it at the start
/// of each race day. Nobody does, so the first heat of a new day was
/// routinely seeded from last week's attendance. Worse, the record of who
/// was actually AT an event was destroyed the moment the button was
/// finally pressed, which made "who raced the summer series" a question
/// the app could not answer at all.
///
/// Check-ins are now filed against an event. Starting a new event starts
/// an empty sheet by itself, last week's sheet is still there to look at,
/// and Check Out Everyone goes back to meaning what it says - clear THIS
/// event - rather than being the mechanism the whole feature depended on.
///
/// The public shape is unchanged. Every existing caller asks about the
/// current event without knowing an event is involved, which is what keeps
/// this a data change rather than a rewrite of six pages.
/// </summary>
public sealed class CheckInStore
{
    /// <summary>One event's sheet.</summary>
    private sealed class Sheet
    {
        public HashSet<string> CheckedIn { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, Sheet> _sheets;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "checkin.json");

    private CheckInStore(Dictionary<string, Sheet> sheets)
    {
        _sheets = sheets;
    }

    /// <summary>
    /// Which event's sheet the plain methods below read and write.
    ///
    /// Set once at startup from the event store and kept in step with it
    /// afterwards. It is a property rather than a constructor argument
    /// because the two stores are loaded independently and neither should
    /// have to know the other's load order.
    /// </summary>
    public string CurrentEventId { get; set; } = RaceEventStore.LegacyEventId;

    private Sheet Current => SheetFor(CurrentEventId);

    private Sheet SheetFor(string eventId)
    {
        if (!_sheets.TryGetValue(eventId, out var sheet))
        {
            sheet = new Sheet();
            _sheets[eventId] = sheet;
        }

        return sheet;
    }

    // --- On-disk shapes ----------------------------------------------------

    /// <summary>The current file: one sheet per event.</summary>
    private sealed class CheckInFile
    {
        public Dictionary<string, SheetData> Events { get; set; } = new();
    }

    private sealed class SheetData
    {
        public List<string> CheckedIn { get; set; } = new();
    }

    /// <summary>The previous file: one global sheet, no events.</summary>
    private sealed class LegacyCheckInData
    {
        public List<string> CheckedIn { get; set; } = new();
    }

    public static CheckInStore Load()
    {
        if (!File.Exists(FilePath))
        {
            return new CheckInStore(new Dictionary<string, Sheet>(StringComparer.Ordinal));
        }

        string json;

        try
        {
            json = SafeFile.ReadAllTextWithRetry(FilePath);
        }
        catch (Exception ex)
        {
            // Could not be read at all: start empty rather than refusing to
            // open, but never save over it this session (see
            // LoadedUnreadable).
            var hold = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
            return new CheckInStore(new Dictionary<string, Sheet>(StringComparer.Ordinal)) { LoadedUnreadable = hold };
        }

        // Which shape the file is, decided by what is actually in it rather
        // than by which deserializer happens not to throw. Current format
        // first, recognised by the Events object rather than by whether
        // anything is in it - an event with an empty sheet is a real state
        // and must not be mistaken for an old file.
        //
        // This used to try each shape in turn and take the first that did
        // not throw. The older one-sheet shape accepts any JSON object at
        // all (names it does not know are skipped, and its list starts
        // empty), so a file whose Events had been turned into a list - a
        // hand-edit, a script, an odd restore - "loaded" as an empty sheet:
        // no copy kept, nothing said, writes allowed, and the first check-in
        // at the desk replaced every earlier race day's attendance. A file
        // that is none of the three shapes is now damaged, and is treated
        // like every other store's damaged file.
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("Events", out var events)
                && events.ValueKind == JsonValueKind.Object)
            {
                var file = JsonSerializer.Deserialize<CheckInFile>(json)!;
                var sheets = new Dictionary<string, Sheet>(StringComparer.Ordinal);

                foreach (var (eventId, data) in file.Events)
                {
                    // A null sheet has nobody on it; leaving it out loses
                    // nothing, where letting it throw would have lost the
                    // whole file.
                    if (data?.CheckedIn is null)
                    {
                        continue;
                    }

                    sheets[eventId] = new Sheet
                    {
                        CheckedIn = new HashSet<string>(data.CheckedIn, StringComparer.OrdinalIgnoreCase),
                    };
                }

                return new CheckInStore(sheets);
            }

            // The global sheet, and before that a bare array of transponders.
            // Both land in the legacy event, which is where everything that
            // pre-dates events belongs.
            List<string>? legacy = null;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("CheckedIn", out var checkedIn)
                && checkedIn.ValueKind == JsonValueKind.Array)
            {
                legacy = JsonSerializer.Deserialize<LegacyCheckInData>(json)!.CheckedIn;
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                legacy = JsonSerializer.Deserialize<List<string>>(json);
            }

            if (legacy is null)
            {
                throw new JsonException("not a check-in file: no Events object, no CheckedIn list, and not a list of transponders");
            }

            return new CheckInStore(new Dictionary<string, Sheet>(StringComparer.Ordinal)
            {
                [RaceEventStore.LegacyEventId] = new Sheet
                {
                    CheckedIn = new HashSet<string>(legacy, StringComparer.OrdinalIgnoreCase),
                },
            });
        }
        catch (Exception ex)
        {
            // Unreadable in every shape: start empty rather than refusing to
            // open - but, when no copy of it could be kept, never save over
            // it this session (see LoadedUnreadable).
            var unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
            return new CheckInStore(new Dictionary<string, Sheet>(StringComparer.Ordinal)) { LoadedUnreadable = unreadable };
        }
    }

    /// <summary>
    /// True when the file was there at startup but could not be read, or was
    /// damaged and no copy of it could be kept - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds every race day's
    /// check-in sheet, which "Race days attended" and who raced at which
    /// round are counted from: nothing anybody could type back in. It used
    /// to be loaded as empty and then written over whole at the first
    /// check-in at the desk, so a backup or sync tool holding it open for a
    /// moment at launch was enough to lose every earlier event's attendance.
    /// Tonight's check-ins still work in memory for the rest of the session;
    /// they are just not saved.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this session's sheets would replace every other event's. The
        // console has already told the operator why and asked for a restart.
        if (LoadedUnreadable)
        {
            return;
        }

        var file = new CheckInFile();

        foreach (var (eventId, sheet) in _sheets)
        {
            // An event whose sheet is empty is not worth a line in the
            // file; it comes back on demand.
            if (sheet.CheckedIn.Count == 0)
            {
                continue;
            }

            file.Events[eventId] = new SheetData
            {
                CheckedIn = sheet.CheckedIn.ToList(),
            };
        }

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }

    // --- This event --------------------------------------------------------

    public bool IsCheckedIn(string transponder) => Current.CheckedIn.Contains(transponder);

    public void SetCheckedIn(string transponder, bool checkedIn)
    {
        if (checkedIn)
        {
            Current.CheckedIn.Add(transponder);
        }
        else
        {
            Current.CheckedIn.Remove(transponder);
        }

        Save();
    }


    public void CheckInAll(IEnumerable<string> transponders)
    {
        foreach (var transponder in transponders)
        {
            Current.CheckedIn.Add(transponder);
        }

        Save();
    }

    public void CheckOutAll()
    {
        Current.CheckedIn.Clear();
        Save();
    }

    /// <summary>Snapshot of who's currently checked in - used to capture state before a bulk clear so it can be undone.</summary>
    public List<string> SnapshotCheckedIn() => Current.CheckedIn.ToList();

    /// <summary>
    /// Checks the snapshot's drivers back in at the event the snapshot was
    /// taken from. Pairs with SnapshotCheckedIn to undo a "Check Out
    /// Everyone".
    ///
    /// Adds rather than replaces, and names its event rather than using the
    /// current one. It used to clear whichever sheet was current at the
    /// moment of the undo and refill it: anyone checked in after the
    /// check-out was checked out again by the undo, and an undo made after
    /// switching events copied one event's attendance onto another's.
    /// </summary>
    public void RestoreCheckedIn(string eventId, IEnumerable<string> transponders)
    {
        var sheet = SheetFor(eventId);

        foreach (var transponder in transponders)
        {
            sheet.CheckedIn.Add(transponder);
        }

        Save();
    }

    public int Count => Current.CheckedIn.Count;

    // --- Across events -----------------------------------------------------

    /// <summary>Who was checked in at a given event. The question the old global list could not answer.</summary>
    public IReadOnlyList<string> CheckedInAt(string eventId) => SheetFor(eventId).CheckedIn.ToList();

    /// <summary>
    /// How many events this driver has been checked in at - attendance, for
    /// standings and awards.
    ///
    /// eventExists says which sheets still belong to a race day. A deleted
    /// day's sheet is deliberately kept here (see AdoptOrphans) so that
    /// undoing the delete brings its check-ins back, but until then the day
    /// is gone as far as the club is concerned. Counting every sheet made a
    /// test day that was created, filled and deleted still add one to
    /// "Race days attended" on each of its drivers' profiles. The store
    /// cannot answer the question itself - it does not know the event store
    /// and should not have to know its load order - so the caller passes the
    /// check in. Leaving it out keeps the old count-everything behavior for
    /// callers that have no event store to hand.
    /// </summary>
    public int EventsAttended(string transponder, Func<string, bool>? eventExists = null) =>
        _sheets.Count(pair =>
            (eventExists is null || eventExists(pair.Key))
            && pair.Value.CheckedIn.Contains(transponder));

    /// <summary>
    /// Moves every sheet that has no event onto the legacy one.
    ///
    /// Called by the migration at startup. Separate from Load so that a
    /// file written by a newer build is never quietly rewritten by an
    /// older one.
    ///
    /// "No event" means a blank key - the older one-sheet files are already
    /// filed under the legacy event by Load, so that is all that can be
    /// left. It used to mean any event that could not be found, and as this
    /// runs on every launch, deleting an event (or undoing its creation)
    /// poured its check-ins into the legacy sheet the next time
    /// the app opened. A sheet whose event has gone is left where it is:
    /// nothing shows it (EventsAttended skips it when the caller passes the
    /// event store's answer), and it comes back with the event if the delete
    /// is undone.
    /// </summary>
    public void AdoptOrphans()
    {
        var orphans = _sheets.Keys.Where(string.IsNullOrEmpty).ToList();

        if (orphans.Count == 0)
        {
            return;
        }

        var legacy = SheetFor(RaceEventStore.LegacyEventId);

        foreach (var orphan in orphans.Where(id => id != RaceEventStore.LegacyEventId))
        {
            foreach (var transponder in _sheets[orphan].CheckedIn)
            {
                legacy.CheckedIn.Add(transponder);
            }

            _sheets.Remove(orphan);
        }

        Save();
    }
}
