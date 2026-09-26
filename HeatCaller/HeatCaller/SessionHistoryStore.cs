using System.Text.Json;

namespace HeatCaller;

/// <summary>Loads/saves SessionRecord history to %AppData%\HeatCaller\race-history.json.</summary>
public sealed class SessionHistoryStore
{
    private readonly List<SessionRecord> _sessions;

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
                    return new SessionHistoryStore(list);
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new SessionHistoryStore(new List<SessionRecord>()) { LoadedUnreadable = unreadable };
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

        var json = JsonSerializer.Serialize(_sessions, new JsonSerializerOptions { WriteIndented = true });
        return SafeFile.WriteAllText(FilePath, json);
    }

    public IReadOnlyList<SessionRecord> All => _sessions.OrderByDescending(s => s.StartedAtUtc).ToList();

    public SessionRecord? Get(string id) => _sessions.FirstOrDefault(s => s.Id == id);

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
    /// </summary>
    public bool Add(SessionRecord record)
    {
        _sessions.Add(record);

        if (!Save())
        {
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

    /// <summary>Removes a session. False when the write did not reach disk, so it is back on the next launch.</summary>
    public bool Delete(string id)
    {
        _sessions.RemoveAll(s => s.Id == id);
        return Save();
    }

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
