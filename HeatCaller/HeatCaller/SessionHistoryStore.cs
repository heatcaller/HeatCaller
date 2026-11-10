using System.Text.Json;

namespace HeatCaller;

/// <summary>Loads/saves SessionRecord history to %AppData%\HeatCaller\race-history.json.</summary>
public sealed class SessionHistoryStore
{
    private readonly List<SessionRecord> _sessions;

    /// <summary>
    /// Records Add was handed but could not write. They stay in _sessions so
    /// the evening carries on - Race History still lists them, qualifying
    /// still counts them - but Save leaves them out of the file.
    ///
    /// Without this, a race whose filing failed rode along on the next
    /// unrelated save that did work: a note typed in Race History, a delete,
    /// a result correction. That put it in race-history.json with no round
    /// linked, never published, and with its crash journal still on disk -
    /// and the next launch, told by the journal that the race "never reached
    /// the race history", offered it back. Restoring and finishing it filed
    /// it again under a new id, so the club had the same A-main twice, both
    /// counting toward qualifying, personal bests and achievements. A failed
    /// race now reaches the file only through Add, the one path that is
    /// followed by linking its round and clearing its journal.
    ///
    /// By reference, not by Id: it is these exact objects that were never
    /// written, and SessionRecord has no equality of its own anyway.
    /// </summary>
    private readonly HashSet<SessionRecord> _unsaved = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Records Delete took out while they were still in _unsaved - never
    /// filed. Delete used to forget that outright, so undoing it handed the
    /// race straight to Add, which then wrote it: no round linked, its crash
    /// journal left on disk, and the next launch offered it back to be filed
    /// a second time. RestoreUnfiled puts such a record back exactly as it
    /// was instead, still waiting for Race Control to file it properly.
    /// ForgetDeletedUnfiled lets one go once nothing is waiting to file it
    /// any more. By reference, like _unsaved.
    /// </summary>
    private readonly HashSet<SessionRecord> _deletedUnfiled = new(ReferenceEqualityComparer.Instance);

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "race-history.json");

    /// <summary>
    /// A read-only view over an arbitrary set of sessions. Lets code that
    /// takes a store - the qualifying calculator - work over a subset (one
    /// event day, say) without the caller having to fake one. Never saved.
    /// </summary>
    public static SessionHistoryStore FromSessions(IEnumerable<SessionRecord> sessions) =>
        new(sessions.ToList(), isView: true);

    private readonly bool _isView;

    private SessionHistoryStore(List<SessionRecord> sessions, bool isView)
    {
        _sessions = sessions;
        _isView = isView;
    }

    private SessionHistoryStore(List<SessionRecord> sessions)
    {
        _sessions = sessions;
    }

    public static SessionHistoryStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<SessionRecord>>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (list is not null)
                {
                    // 17 Oct 2026, Bug Hunt 8, A23-1: a hand-edited or
                    // foreign file with a null entry (or "Results": null)
                    // used to throw later, outside this try, while MainForm
                    // was being built - "HeatCaller couldn't start" on every
                    // launch. Same repair ProfileStore.Load does (it skips
                    // nulls and calls FillMissingFields). The file WAS
                    // readable, so LoadedUnreadable stays false.
                    list.RemoveAll(s => s is null);
                    foreach (var session in list)
                    {
                        Repair(session);
                    }

                    return new SessionHistoryStore(list);
                }

                // Present, parsed, and not the shape this store writes. Not
                // a new install either, so the same rule applies. A file
                // holding the literal "null" parses to nothing without
                // throwing: it used to fall straight through to the empty
                // store below with the flag left false, so the first race of
                // the night was written over every result the club has.
                unreadable = true;
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new SessionHistoryStore(new List<SessionRecord>()) { LoadedUnreadable = unreadable };
    }

    // 17 Oct 2026, Bug Hunt 8, A23-1: fills the fields a damaged file can
    // leave null, so nothing that reads the history dereferences a null.
    // Mirrors ProfileStore.FillMissingFields.
    private static void Repair(SessionRecord s)
    {
        s.Id ??= Guid.NewGuid().ToString("N");
        s.Results ??= new();
        s.Results.RemoveAll(r => r is null);
        s.Label ??= string.Empty;
        s.TrackLayoutName ??= string.Empty;
        s.Notes ??= string.Empty;
        s.Edits ??= new();
        s.Edits.RemoveAll(e => e is null);
        foreach (var r in s.Results)
        {
            r.LapTimesMs ??= new();
            r.Warnings ??= new();
            r.Transponder ??= string.Empty;
            r.DriverName ??= string.Empty;
            r.CarModel ??= string.Empty;
            r.Status ??= string.Empty;
            r.Outcome ??= string.Empty;
        }
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. What this store holds is then a
    /// guess, and nothing it does this session is saved over the club's
    /// real file.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    /// <summary>
    /// Writes the history. FALSE when it did not reach disk.
    ///
    /// SafeFile.WriteAllText returns false rather than throwing - a locked or
    /// full disk must not take the app down mid-race - and this method used to
    /// drop that answer, so no caller could tell. The caller that matters is
    /// Race Control's save, which on the strength of "it returned" told the
    /// operator the race was saved, marked the heat recorded, published it,
    /// and then DELETED the race journal: the only other copy of the lap data.
    /// A backup tool holding race-history.json open for a second was enough to
    /// lose an A-main outright, with one console line to say so.
    ///
    /// A view returns true: it is not meant to reach disk, so "the write did
    /// not fail" is the honest answer, and no caller of a view ever asks.
    /// </summary>
    private bool Save()
    {
        // 17 Oct 2026, Bug Hunt 8, A26-3: counted before any early return so
        // every change (Add, SaveEdited, SaveChanges, Delete) moves it.
        Revision++;

        if (_isView)
        {
            // A view over a subset must never persist - it would replace the
            // real history file with just that subset.
            return true;
        }

        // Not written - and said so. The race is then "not saved", which
        // keeps its journal and tells the operator; saving it would have
        // replaced every result the club has with tonight's.
        if (LoadedUnreadable)
        {
            return false;
        }

        // Everything except what Add failed to file - see _unsaved.
        var toWrite = _unsaved.Count == 0 ? _sessions : _sessions.Where(s => !_unsaved.Contains(s)).ToList();
        var json = JsonSerializer.Serialize(toWrite, new JsonSerializerOptions { WriteIndented = true });
        return SafeFile.WriteAllText(FilePath, json);
    }

    /// <summary>
    /// Goes up on every change to the history (each Save, written or not).
    /// Lets a cache - the live qualifying projection - see an edited or
    /// deleted result at once (17 Oct 2026, Bug Hunt 8, A26-3).
    /// </summary>
    public int Revision { get; private set; }

    public IReadOnlyList<SessionRecord> All => _sessions.OrderByDescending(s => s.StartedAtUtc).ToList();

    /// <summary>
    /// As All, less the records Add could not write (see _unsaved) - what a
    /// restart would load. For anything that publishes: All still lists a
    /// race whose filing failed, so the evening carries on around it, and the
    /// relay's periodic passes read All - the results index went out listing
    /// a race whose result document was never sent, and the standings with
    /// its points, all of which changed back after a restart. SessionAdded is
    /// held back for exactly that reason; this is the same rule for code that
    /// reads the whole history rather than waiting for the event.
    /// </summary>
    public IReadOnlyList<SessionRecord> Saved => _unsaved.Count == 0
        ? All
        : _sessions.Where(s => !_unsaved.Contains(s)).OrderByDescending(s => s.StartedAtUtc).ToList();

    /// <summary>
    /// A read-only store over Saved, for code that takes a whole store
    /// (EventSpine.SessionsFor, RelayDocuments.Tracks) and is publishing.
    /// This store itself when nothing is held back, so the usual case costs
    /// nothing; callers only read from it either way.
    /// </summary>
    public SessionHistoryStore SavedView() => _unsaved.Count == 0 ? this : FromSessions(Saved);

    /// <summary>True for a record Add was handed but could not write - see _unsaved.</summary>
    public bool IsUnsaved(SessionRecord record) => _unsaved.Contains(record);

    public SessionRecord? Get(string id) => _sessions.FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// A scored race of the same round as this one, started after it - the
    /// re-run that replaced it. For EventSpine.SupersededByRerun once the
    /// round itself has gone. A scan rather than All, which sorts a copy.
    /// </summary>
    public SessionRecord? LaterRunOfRound(SessionRecord session) =>
        string.IsNullOrEmpty(session.RoundId)
            ? null
            : _sessions.FirstOrDefault(s => !ReferenceEquals(s, session)
                && !string.Equals(s.Id, session.Id, StringComparison.Ordinal)
                && string.Equals(s.RoundId, session.RoundId, StringComparison.Ordinal)
                && s.StartedAtUtc > session.StartedAtUtc
                && !s.IsNotScored());

    /// <summary>
    /// Raised after a session has been added and written to disk.
    ///
    /// It exists because more than one place finishes a race - Race
    /// Control builds and saves its own record, and the connection path
    /// saves another - and anything hooked onto those call sites one by
    /// one will miss whichever one gets added next. That is not
    /// hypothetical: the relay publisher was wired to one of the two and
    /// silently published nothing for races run the other way.
    ///
    /// There is exactly one way into history, so this is where code that
    /// cares about a new session listens.
    ///
    /// Raised on whatever thread called Add - the UI thread in every
    /// current path. A handler must not assume otherwise, and must not
    /// throw: an exception here would take down the save that caused it.
    /// </summary>
    public event Action<SessionRecord>? SessionAdded;

    /// <summary>
    /// Files a finished race. FALSE when it did not reach disk - the caller
    /// must then treat the race as NOT saved.
    ///
    /// The record stays in memory either way, so the evening carries on and
    /// the operator can try again; what must not happen is anything
    /// irreversible being done on the strength of a write that failed.
    /// SessionAdded is not raised in that case: every listener publishes the
    /// result somewhere, and publishing a race that is not on disk is how the
    /// club ends up with a result on its website that the laptop forgets.
    ///
    /// Nor does a failed record reach disk by any other save later on - it
    /// is held back (see _unsaved) until Add itself files it. Handing the
    /// same object to Add again is a retry: it is not listed a second time.
    /// </summary>
    /// <summary>
    /// For an undo that put deleted sessions back: when any of them could not
    /// be written (Add returned false), they are listed for this run only and
    /// no later save writes them (see _unsaved), and these have no crash
    /// journal to come back from. So rather than look restored and vanish
    /// at the next restart, a step that tries Add again is left on the Undo
    /// list, and the undo reports the failure (UndoService shows it). Does
    /// nothing when <paramref name="failed"/> is empty.
    ///
    /// <paramref name="onSaved"/> runs for each record the retry does get
    /// onto disk, and is handed on to any retry after that. It is how the
    /// caller finishes what the first undo had to hold back: an undo that
    /// failed to save a session also left its round unlinked (a link to a
    /// session that is not on disk would outlive it), and without this
    /// nothing put the link back once the retry did save it - the session
    /// was in Race History but its heat still read as unrun.
    /// </summary>
    public void KeepRetryForUnsaved(UndoService undo, IReadOnlyList<SessionRecord> failed, Action<SessionRecord>? onSaved = null)
    {
        if (failed.Count == 0)
        {
            return;
        }

        var what = failed.Count == 1 ? "the restored session" : $"{failed.Count} restored sessions";

        undo.Capture($"Save {what} to Race History", () =>
        {
            var saved = new List<SessionRecord>();
            var stillFailed = new List<SessionRecord>();

            foreach (var record in failed)
            {
                (Add(record) ? saved : stillFailed).Add(record);
            }

            // Each on its own, so one that throws does not stop the rest,
            // and none of them stops the retry below from being put back on
            // the Undo list for whatever is still unsaved - losing that
            // would lose those sessions at the next restart.
            Exception? onSavedFailed = null;

            foreach (var record in saved)
            {
                try
                {
                    onSaved?.Invoke(record);
                }
                catch (Exception ex)
                {
                    onSavedFailed ??= ex;
                }
            }

            KeepRetryForUnsaved(undo, stillFailed, onSaved);

            // Only reached when everything else did save, so this is the
            // one problem left to report.
            if (onSavedFailed is not null)
            {
                throw new InvalidOperationException(
                    $"Race History now has {what}, but finishing the restore failed: {onSavedFailed.Message}", onSavedFailed);
            }
        });

        throw new InvalidOperationException(
            $"{what} couldn't be written to the race history file - something may have it open, or the disk may be full. "
            + "They're listed until HeatCaller closes. Press Undo again to save them once the file can be written.");
    }

    public bool Add(SessionRecord record)
    {
        // A retry of one that failed is already in the list.
        if (!_unsaved.Remove(record))
        {
            // Already listed and not held back: filed before. An undo can
            // file a race and then Race Control's own retry
            // (FileUnfiledRaces) or a late "Save the restored session" step
            // hands the same one in again - and appending it here put the
            // same session in race-history.json twice, counted twice by
            // qualifying, points and personal bests. It is on disk, so that
            // is the answer; nothing is written and SessionAdded is not
            // raised again. Matched by Id too, so another copy of a session
            // already here is not listed beside it either - "saved" only
            // when the one listed is not itself held back.
            if (Get(record.Id) is { } listed)
            {
                return !_unsaved.Contains(listed);
            }

            _sessions.Add(record);
        }

        if (!Save())
        {
            _unsaved.Add(record);
            return false;
        }

        // After the save, so a handler reading the store back sees what a
        // restart would see rather than something only in memory.
        RaiseEach(SessionAdded, record);
        return true;
    }

    /// <summary>
    /// Calls each listener on its own, so one that throws neither stops the
    /// rest nor comes back out through Add or SaveEdited.
    ///
    /// The session is already on disk by the time these run. A plain Invoke
    /// let a failing listener - a relay publish, say - throw straight back
    /// into Race Control's save, which then skipped linking the heat to its
    /// round and clearing the crash journal: the result was in the history,
    /// but the heat still looked unrun and the next launch offered the same
    /// race back for "recovery", to be saved a second time.
    /// </summary>
    private static void RaiseEach(Action<SessionRecord>? handlers, SessionRecord record)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<SessionRecord>)handler)(record);
            }
            catch (Exception ex)
            {
                // Nowhere of its own to report to; the debugger still hears.
                System.Diagnostics.Debug.WriteLine($"A session history listener failed and was skipped: {ex}");
            }
        }
    }

    /// <summary>
    /// Removes a session. False when the write did not reach disk - and then
    /// a session that was on disk is left listed, as the file still has it.
    ///
    /// WHY (2026-11-09, Bug hunt 15, B24): it used to come out of memory
    /// either way. The file still held it, so it was back at the next launch
    /// - unless any later save landed first, which then wrote the history
    /// without it after all. Its caller could not tell which, so whether it
    /// unlinked the round or not, one of the two outcomes left race history
    /// and rounds.json disagreeing. Now a false means nothing was deleted.
    /// A record Add never managed to file (see _unsaved) is not on disk, so
    /// that one goes regardless.
    /// </summary>
    public bool Delete(string id)
    {
        var before = _sessions.ToList();
        var unsavedBefore = _unsaved.Where(s => s.Id == id).ToList();

        var removed = _sessions.Where(s => s.Id == id).ToList();

        _sessions.RemoveAll(s => s.Id == id);
        _unsaved.RemoveWhere(s => s.Id == id);
        var saved = Save();

        if (!saved)
        {
            // Back as it was, less what was never on disk.
            _sessions.Clear();
            _sessions.AddRange(before.Where(s => s.Id != id || !unsavedBefore.Contains(s)));
            removed = removed.Where(s => unsavedBefore.Contains(s)).ToList();
        }

        // Remembered as never filed - see _deletedUnfiled. Races only: a
        // practice has no crash journal and nothing in Race Control waiting
        // to file it, so its undo goes on restoring it with Add.
        foreach (var unfiled in unsavedBefore.Where(s => s.SessionType != RaceSessionType.Practice))
        {
            _deletedUnfiled.Add(unfiled);
        }

        // WHY (2026-11-05, Bug hunt 11: a deleted race's penalty lines and
        // report cards stayed on the Wi-Fi phone pages until the next race
        // was filed - nothing told MainForm a race had gone). Raised for
        // each record that is out of All, which is what the listeners read
        // back - after a failed write, only one that was never on disk.
        foreach (var record in removed)
        {
            RaiseEach(SessionDeleted, record);
        }

        return saved;
    }

    /// <summary>
    /// Raised after a session has been removed by Delete - Race History's
    /// delete and its undo of a paste, and a race day deleted whole. Same
    /// threading rule as SessionAdded: raised on the calling thread (always
    /// the UI thread), and a handler must not throw.
    /// </summary>
    public event Action<SessionRecord>? SessionDeleted;

    /// <summary>
    /// For an undo of a delete: puts back a record that was deleted before it
    /// was ever filed (see _deletedUnfiled) as it was - listed, held out of
    /// the file - and returns true, so Race Control files it the way every
    /// race is filed, linking its round and letting go of its journal. False
    /// for any other record, which the caller restores with Add as before,
    /// and for one already back in the store.
    /// </summary>
    public bool RestoreUnfiled(SessionRecord record)
    {
        if (!_deletedUnfiled.Remove(record) || Get(record.Id) is not null)
        {
            return false;
        }

        _sessions.Add(record);
        _unsaved.Add(record);
        return true;
    }

    /// <summary>
    /// Called when nothing is waiting to file this record any more - Race
    /// Control has given up on a race deleted before it was filed, and the
    /// crash journal holding it is about to be replaced. An undo of its
    /// delete then restores it with Add, which writes it, rather than
    /// putting it back to wait for a filing that will never come.
    /// </summary>
    public void ForgetDeletedUnfiled(SessionRecord record) => _deletedUnfiled.Remove(record);

    /// <summary>Persists changes made directly to a record already returned by Get() - e.g. editing its Notes - without adding a duplicate entry. False when the write did not reach disk.</summary>
    public bool SaveChanges() => Save();

    /// <summary>
    /// Raised after a saved session's RESULT has been corrected and written
    /// to disk - the result editor, from the laptop or a phone, and the
    /// undo of either.
    ///
    /// Separate from SaveChanges on purpose: a note typed into Race History
    /// changes nothing anybody else shows, but a corrected result changes
    /// the published results page and the championship standings, and
    /// those have to be sent again. Same threading rule as SessionAdded:
    /// raised on the calling thread (always the UI thread), and a handler
    /// must not throw.
    /// </summary>
    public event Action<SessionRecord>? SessionEdited;

    /// <summary>
    /// Saves a correction made to a record already in the store and tells
    /// everything that shows results about it. Ignores a record that is
    /// not in the store (a view's copy, or one deleted meanwhile).
    /// </summary>
    public bool SaveEdited(SessionRecord record)
    {
        if (!_sessions.Contains(record))
        {
            return false;
        }

        // A race Add could not file is not on disk, so neither is a
        // correction to it - and announcing one would publish a result the
        // laptop does not have. Correcting it does not file it either: that
        // stays Add's job, for the reasons on _unsaved.
        if (_unsaved.Contains(record))
        {
            return false;
        }

        if (!Save())
        {
            // Not announced. Every SessionEdited listener republishes - the
            // results page, the archive index, the championship standings - and
            // a correction that did not reach disk would go out to all three,
            // be reported to the phone that made it as "this is what qualifying
            // and the championship use from now on", and then be silently
            // reversed by the next launch reloading the uncorrected result.
            return false;
        }

        // After the save, for the same reason as SessionAdded.
        RaiseEach(SessionEdited, record);
        return true;
    }
}
