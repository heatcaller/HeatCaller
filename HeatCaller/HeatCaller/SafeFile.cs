using System.Globalization;
using System.Text;

namespace HeatCaller;

/// <summary>
/// One place for writing settings files safely.
///
/// Every store used to call File.WriteAllText directly and unguarded. That
/// works on a developer machine and fails on plenty of real ones: a
/// locked-down club PC where %AppData% is restricted, a roaming profile
/// that's momentarily unavailable, a full disk, an antivirus holding the
/// file open, or a second copy of the app running. Any of those threw
/// straight out of a Save() call and took the whole app down mid-race.
///
/// Writes are also made atomic. A crash or power cut partway through
/// WriteAllText leaves a truncated file, and a truncated JSON file is a
/// silently emptied race programme the next time it loads - far worse than
/// a failed save, because it looks like data loss with no cause.
/// </summary>
public static class SafeFile
{
    private static readonly object _gate = new();
    private static Action<string>? _writeFailed;

    // Messages raised before anyone has subscribed. Every store loads in
    // MainForm's field initialisers and constructor, well before the
    // console subscribes - so a file found unreadable at startup, which is
    // exactly the moment it matters, would otherwise be reported to nobody.
    private static readonly List<string> _pending = new();

    private static volatile bool _frozen;
    private static int _frozenReported;

    // Every write holds this SHARED - any number at once, exactly as before
    // - and only two things ever take it alone: Freeze, to wait for a write
    // already under way to land before a restore starts replacing files,
    // and a backup, for the moment it takes to copy the settings files.
    // Without the second, a backup made while the app is saving (which is
    // any time a race is running, now that backups run in the background)
    // could take one file from before a change and the next from after it.
    //
    // Recursion allowed only so that a thread which somehow got here twice
    // is not thrown at; nothing in this class re-enters on purpose.
    private static readonly ReaderWriterLockSlim _writes = new(LockRecursionPolicy.SupportsRecursion);

    /// <summary>
    /// The longest Freeze or a backup waits for writes already under way.
    /// A save is a few milliseconds; one stuck far longer than this is on a
    /// dead network drive, and waiting on it would only hang the app.
    /// </summary>
    private static readonly TimeSpan ExclusiveWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Raised when a write fails, so the app can surface it rather than
    /// losing it silently. Anything reported before the first subscriber
    /// arrives is handed to that subscriber as it subscribes.
    /// </summary>
    public static event Action<string>? WriteFailed
    {
        add
        {
            List<string> backlog;
            lock (_gate)
            {
                _writeFailed += value;
                backlog = new List<string>(_pending);
                _pending.Clear();
            }

            foreach (var message in backlog)
            {
                try
                {
                    value?.Invoke(message);
                }
                catch
                {
                    // Same rule as Notify: a subscriber's failure is not ours.
                }
            }
        }
        remove
        {
            lock (_gate)
            {
                _writeFailed -= value;
            }
        }
    }

    /// <summary>
    /// Stops every further write through this class, for the rest of the
    /// process.
    ///
    /// Restoring a backup replaces the files on disk and then restarts the
    /// app - but the restart closes the main window first, and closing
    /// saves the settings and stores still held in memory. Those are the
    /// OLD ones, so without this the restart quietly writes the pre-restore
    /// data straight back over what was just restored. Called just BEFORE
    /// the restored files start going in - a save landing in the middle of
    /// them would put one old file back among the new ones - and nothing
    /// this process holds is worth keeping after that.
    ///
    /// Returns once any write that was already under way has finished (or
    /// after a few seconds, if one is stuck), so nothing lands after it.
    /// </summary>
    public static void Freeze()
    {
        _frozen = true;

        try
        {
            if (_writes.TryEnterWriteLock(ExclusiveWait))
            {
                _writes.ExitWriteLock();
            }
        }
        catch (LockRecursionException)
        {
            // Called from inside a write, which nothing does. The flag is
            // set either way, and no write starts after it.
        }
    }

    /// <summary>True once <see cref="Freeze"/> has been called.</summary>
    public static bool IsFrozen => _frozen;

    /// <summary>
    /// Runs <paramref name="read"/> while no write through this class is
    /// under way and none can start, so every file it reads is from the
    /// same moment. For a backup, which must never catch one settings file
    /// before a change and the next one after it. Keep it short: every save
    /// in the app waits for it. If a write is stuck, it runs anyway after a
    /// few seconds rather than hanging the backup.
    /// </summary>
    internal static void WhileNoWritesUnderWay(Action read)
    {
        var held = false;

        try
        {
            try
            {
                held = _writes.TryEnterWriteLock(ExclusiveWait);
            }
            catch (LockRecursionException)
            {
                // This thread is inside a write - nothing does that, but a
                // backup is better made unguarded than not at all.
            }

            read();
        }
        finally
        {
            if (held)
            {
                _writes.ExitWriteLock();
            }
        }
    }

    /// <summary>
    /// Takes the shared side of the write lock for one write. False once
    /// frozen - checked again after the lock is taken, because Freeze may
    /// have been waiting for exactly this lock.
    /// </summary>
    private static bool BeginWrite()
    {
        if (_frozen)
        {
            return false;
        }

        _writes.EnterReadLock();

        if (_frozen)
        {
            _writes.ExitReadLock();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes text to a file, creating the directory, replacing the target
    /// atomically, and never throwing. Returns false if the write failed.
    /// </summary>
    public static bool WriteAllText(string path, string contents)
    {
        if (!BeginWrite())
        {
            ReportFrozen();
            return false;
        }

        Exception? failure;

        try
        {
            failure = WriteAtomically(path, contents);
        }
        finally
        {
            _writes.ExitReadLock();
        }

        // Reported only once the lock is let go. The report runs the app's
        // own handler, and a handler that waited on the UI thread while
        // the UI thread waited in Freeze for this lock would hang both.
        if (failure is not null)
        {
            Report(path, failure);
            return false;
        }

        return true;
    }

    /// <summary>The write itself: null when it worked, or what went wrong.</summary>
    private static Exception? WriteAtomically(string path, string contents)
    {
        string? temp = null;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a temp file alongside the target, then swap it in.
            // If anything fails partway, the existing file is untouched
            // rather than half-overwritten.
            //
            // The temp name is unique per write. A fixed "<name>.tmp" meant
            // two saves of the same file overlapping - a timer and a button
            // click, say - wrote into the same temp file and one of them
            // swapped in the other's half-written text.
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                // UTF-8 without a byte order mark: what File.WriteAllText
                // wrote, so files saved before and after this read the same.
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
                stream.Write(bytes, 0, bytes.Length);

                // Pushed through to the disk, not just to Windows' cache,
                // before the swap. Otherwise a power cut shortly after the
                // swap can leave the new name pointing at a file whose
                // contents never made it out of memory - an empty file,
                // which is the very thing the swap exists to prevent.
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temp, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, path);
            }

            temp = null;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            // Only still set if the swap never happened. A stray temp file
            // is harmless but would pile up one per failed save.
            if (temp is not null)
            {
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                    // Left behind; nothing reads it.
                }
            }
        }
    }

    /// <summary>
    /// Reads a whole text file, trying again briefly if it's in use.
    ///
    /// The usual reason a settings file can't be opened at startup is not
    /// that it's damaged but that something has it open for a moment - an
    /// antivirus scan, a backup tool, the previous copy of the app still
    /// closing after a restart. Treating that as "unreadable" loses the
    /// file's contents to the defaults for no reason. Throws, like
    /// File.ReadAllText, once the retries are used up.
    /// </summary>
    public static string ReadAllTextWithRetry(string path)
    {
        const int attempts = 4;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException ex) when (attempt < attempts
                && ex is not FileNotFoundException
                && ex is not DirectoryNotFoundException)
            {
                Thread.Sleep(150);
            }
        }
    }

    /// <summary>
    /// Keeps a copy of a file that couldn't be loaded, before the app falls
    /// back to defaults for it.
    ///
    /// Every store treats a file it can't read as empty so that one damaged
    /// file can't stop the app opening. But the next save then writes those
    /// empty defaults over the file - and a file that failed to load is
    /// very often perfectly good data the app just couldn't read this once
    /// (locked, or saved by a newer version). Copying it aside first means
    /// the drivers, history or API key in it can still be got back.
    /// Never throws, and never overwrites an earlier copy.
    /// </summary>
    public static void PreserveUnreadable(string path, Exception? ex)
    {
        var name = Path.GetFileName(path);
        var (copyName, copyError) = KeepCopy(path);

        if (copyError is not null)
        {
            Notify($"[storage] Couldn't read \"{name}\" ({ReasonFor(ex)}), and couldn't keep a copy of it either ({copyError}). The next save will replace it.");
        }
        else if (copyName is not null)
        {
            Notify($"[storage] Couldn't read \"{name}\" ({ReasonFor(ex)}). Started with defaults instead; the original was kept as \"{copyName}\".");
        }
    }

    /// <summary>
    /// PreserveUnreadable for a store holding what nobody can type back in -
    /// the race history, the program, the drivers - which then says whether
    /// that store must save NOTHING for the rest of the session.
    ///
    /// PreserveUnreadable's promise is only as good as its copy. A file that
    /// could not be READ - held open by a backup or sync tool at startup, the
    /// usual case - often cannot be copied either, and was then replaced
    /// whole at the next save: race-history.json locked for a second at
    /// launch, and the first race of the night was written over the club's
    /// entire history. That file was almost certainly fine, and the only
    /// copy there was.
    ///
    /// So a store holds its writes when the file could not be read at all,
    /// or when no copy of it could be kept. Damaged content that WAS copied
    /// aside is the one case where starting afresh is safe, and the store
    /// carries on as it always has.
    /// </summary>
    /// <returns>True when nothing may be saved over this file until the app restarts and can read it.</returns>
    public static bool HoldWritesAfterUnreadable(string path, Exception? ex)
    {
        var name = Path.GetFileName(path);
        var (copyName, copyError) = KeepCopy(path);

        var couldNotRead = ex is IOException or UnauthorizedAccessException;
        var hold = couldNotRead || copyError is not null;

        if (hold)
        {
            Notify($"[storage] Couldn't read \"{name}\" ({ReasonFor(ex)}). Nothing will be saved to it until HeatCaller is "
                + "restarted and can read it, so what is in it cannot be written over. Close anything that might have it "
                + "open (a backup or sync tool is the usual cause) and restart."
                + (copyName is not null ? $" A copy was also kept as \"{copyName}\"." : string.Empty));
        }
        else if (copyName is not null)
        {
            Notify($"[storage] Couldn't read \"{name}\" ({ReasonFor(ex)}). Started with defaults instead; the original was kept as \"{copyName}\".");
        }

        return hold;
    }

    private static string ReasonFor(Exception? ex) =>
        ex is null ? "unknown error" : $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>
    /// Copies a file that couldn't be loaded to "name.unreadable-stamp",
    /// never over an earlier copy. The copy's name, or null when there was
    /// nothing worth keeping (no file, or an empty one); and what went wrong
    /// if the copy could not be made.
    /// </summary>
    private static (string? CopyName, string? Error) KeepCopy(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
            {
                // Nothing in it worth keeping.
                return (null, null);
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var target = $"{path}.unreadable-{stamp}";
            for (var n = 2; File.Exists(target); n++)
            {
                target = $"{path}.unreadable-{stamp}-{n}";
            }

            File.Copy(path, target, overwrite: false);
            return (Path.GetFileName(target), null);
        }
        catch (Exception copyEx)
        {
            return (null, $"{copyEx.GetType().Name}: {copyEx.Message}");
        }
    }

    /// <summary>Copies a file without throwing. Returns false on failure.</summary>
    public static bool Copy(string source, string destination, bool overwrite = false)
    {
        if (!BeginWrite())
        {
            ReportFrozen();
            return false;
        }

        Exception? failure = null;

        try
        {
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(source, destination, overwrite);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            _writes.ExitReadLock();
        }

        // Outside the lock, for the same reason as WriteAllText.
        if (failure is not null)
        {
            Report(destination, failure);
            return false;
        }

        return true;
    }

    private static void Report(string path, Exception ex) =>
        Notify($"[storage] Couldn't write \"{Path.GetFileName(path)}\": {ex.GetType().Name}: {ex.Message}");

    private static void ReportFrozen()
    {
        // Once, not per file: after a restore, closing the window tries to
        // save everything, and a line per store says nothing more.
        if (Interlocked.Exchange(ref _frozenReported, 1) == 0)
        {
            Notify("[storage] Saving is switched off until the app restarts, so the restored files aren't overwritten.");
        }
    }

    private static void Notify(string message)
    {
        Action<string>? handlers;
        lock (_gate)
        {
            handlers = _writeFailed;
            if (handlers is null)
            {
                // Bounded, in case nothing ever subscribes.
                if (_pending.Count < 50)
                {
                    _pending.Add(message);
                }
                return;
            }
        }

        try
        {
            handlers.Invoke(message);
        }
        catch
        {
            // A failing subscriber must not turn a failed save into a crash,
            // which is the very thing this class exists to prevent.
        }
    }
}
