namespace HeatCaller;

/// <summary>
/// Where an undoable action puts the files it would otherwise delete -
/// music, recorded voice lines, a club logo, a track photo - so Undo can
/// move them back.
///
/// Moved, never copied into memory: an undo entry holding a music library's
/// bytes would pin hundreds of MB for as long as it sat on the stack. A move
/// inside the same drive is a rename, however big the file.
///
/// The undo stack gives no word when an entry falls off the end, so nothing
/// here knows when a held file is no longer wanted. Instead every action gets
/// a folder of its own, stamped with when it was made, and anything older
/// than <see cref="KeepDays"/> is cleared by <see cref="Prune"/> - once per
/// session in the background, the first time something is held, and at
/// startup when the host calls it.
///
/// Two roots: the data folder (%AppData%\HeatCaller\undo-holding) for
/// the club's own files, and %LocalAppData% for the voice-line cache, which
/// lives there on purpose (it must not roam, nor go into a backup) and can
/// run to 500 MB.
///
/// Never throws: a file that can't be held is reported back as null, and the
/// caller decides what that means for its undo.
/// </summary>
internal static class UndoHoldingFolder
{
    /// <summary>The folder's name under each root.</summary>
    internal const string FolderName = "undo-holding";

    /// <summary>How long a held file is kept before Prune clears it.</summary>
    internal const int KeepDays = 7;

    /// <summary>For the club's own files - beside the rest of its data.</summary>
    internal static string RoamingRoot => Path.Combine(BackupService.DataDirectory, FolderName);

    /// <summary>For the voice-line cache, which lives in %LocalAppData% - see VoiceLibrary.</summary>
    internal static string LocalRoot => Path.Combine(
        AppPaths.LocalRoot,
        FolderName);

    private static int _prunedThisSession;

    /// <summary>
    /// A fresh, empty folder for one action's files. Named by when it was
    /// made (UTC, in the invariant calendar), which is what Prune goes by.
    /// </summary>
    internal static string NewFolder(bool local = false)
    {
        // In the background: clearing a week-old voice cache can take a
        // while, and this is asked for on the UI thread. It never touches a
        // folder this new, so the one made below is safe from it.
        if (Interlocked.Exchange(ref _prunedThisSession, 1) == 0)
        {
            _ = Task.Run(Prune);
        }

        var folder = Path.Combine(
            local ? LocalRoot : RoamingRoot,
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch
        {
            // MoveIn tries again, and reports null if it still can't.
        }

        return folder;
    }

    /// <summary>
    /// Moves a file into a holding folder. Returns where it went, or null
    /// when there was no file or it couldn't be moved (it is then left where
    /// it was).
    /// </summary>
    internal static string? MoveIn(string? path, string folder)
    {
        try
        {
            // A network path is never even looked at: asking whether it
            // exists makes Windows log on to that server with this account's
            // password hash. (Unless the holding folder is on the network too
            // - a redirected %AppData%, where the app's own files all are.)
            // And only into a holding folder, never anywhere a caller's
            // mistake could name.
            if (string.IsNullOrEmpty(path) || OffToAnotherMachine(path, folder) || !IsHeld(folder) || !File.Exists(path))
            {
                return null;
            }

            Directory.CreateDirectory(folder);

            // Two files of the same name from different places (the same
            // song in two folders) must not land on each other.
            var name = Path.GetFileName(path);
            var target = Path.Combine(folder, name);

            for (var n = 2; File.Exists(target) || Directory.Exists(target); n++)
            {
                target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
            }

            File.Move(path, target);
            return target;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Moves a whole folder into a holding folder. Returns where it went, or
    /// null when there was nothing there or it couldn't be moved.
    /// </summary>
    internal static string? MoveFolderIn(string? path, string folder)
    {
        try
        {
            // The same two rules as MoveIn.
            if (string.IsNullOrEmpty(path) || OffToAnotherMachine(path, folder) || !IsHeld(folder) || !Directory.Exists(path))
            {
                return null;
            }

            Directory.CreateDirectory(folder);

            var target = Path.Combine(folder, Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
            for (var n = 2; File.Exists(target) || Directory.Exists(target); n++)
            {
                target = Path.Combine(folder, $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(path))} ({n})");
            }

            Directory.Move(path, target);
            return target;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Puts a held file back where it came from. Leaves a file already there
    /// alone unless told to overwrite it - something made since is newer
    /// than the undo. True when the held file is back in place.
    /// </summary>
    internal static bool MoveBack(string? held, string original, bool overwrite = false)
    {
        try
        {
            // Only a file this class holds comes back, and never to a network
            // path. Where it goes back to is the caller's to have checked -
            // it is the place the file was taken from.
            if (string.IsNullOrEmpty(held)
                || !IsHeld(held)
                || string.IsNullOrEmpty(original)
                || OffToAnotherMachine(original, held)
                || !File.Exists(held))
            {
                return false;
            }

            if (File.Exists(original) && !overwrite)
            {
                return false;
            }

            if (Path.GetDirectoryName(original) is { Length: > 0 } parent)
            {
                Directory.CreateDirectory(parent);
            }

            File.Move(held, original, overwrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a path is inside one of the holding roots - a held file or
    /// folder, or a folder from <see cref="NewFolder"/>. Lexical, after
    /// resolving ".." - which is what matters for a path this class made.
    /// </summary>
    internal static bool IsHeld(string? path) =>
        !string.IsNullOrEmpty(path)
        && (PlainFileName.IsInside(RoamingRoot, path) || PlainFileName.IsInside(LocalRoot, path));

    /// <summary>
    /// A network path on the far side of a move whose holding side is on
    /// this PC. The holding roots come from Windows itself, so when one of
    /// them is on the network the profile is redirected there and the app's
    /// own files are on that server too - touching them hands nothing to a
    /// stranger. Anything else on the network is refused.
    /// </summary>
    private static bool OffToAnotherMachine(string path, string holdingSide) =>
        PlainFileName.IsNetworkPath(path) && !PlainFileName.IsNetworkPath(holdingSide);

    /// <summary>
    /// Clears every held action older than <see cref="KeepDays"/> from both
    /// roots. Safe to call at any time and from any thread; the host calls it
    /// once at startup.
    ///
    /// Only folders named the way NewFolder names them, and aged by that
    /// name rather than by the file system's creation time (which a copy or
    /// a restore can reset). A root, or a folder in it, that is a junction or
    /// a link is never followed: a root that is one is left alone entirely,
    /// and a folder that is one has only the link itself removed - so a
    /// planted link can't turn this into a delete of somewhere else.
    /// </summary>
    internal static void Prune()
    {
        var cutoff = DateTime.UtcNow.AddDays(-KeepDays);

        foreach (var root in new[] { RoamingRoot, LocalRoot })
        {
            try
            {
                if (!Directory.Exists(root) || IsReparsePoint(root))
                {
                    continue;
                }

                foreach (var folder in Directory.GetDirectories(root))
                {
                    try
                    {
                        if (!TryReadStamp(Path.GetFileName(folder), out var made) || made >= cutoff)
                        {
                            continue;
                        }

                        if (IsReparsePoint(folder))
                        {
                            // Just the link - never what it points at.
                            Directory.Delete(folder, recursive: false);
                        }
                        else
                        {
                            // .NET removes a link met inside without going
                            // into it.
                            Directory.Delete(folder, recursive: true);
                        }
                    }
                    catch
                    {
                        // A locked file keeps its folder until next time.
                    }
                }
            }
            catch
            {
                // Nothing to clear is not an error.
            }
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// The UTC time in a NewFolder name - "yyyyMMdd-HHmmss-" then 32 hex
    /// digits. False for any other name, which Prune then leaves alone.
    /// </summary>
    private static bool TryReadStamp(string name, out DateTime made)
    {
        made = default;

        if (name.Length != 15 + 1 + 32 || name[15] != '-')
        {
            return false;
        }

        for (var i = 16; i < name.Length; i++)
        {
            if (name[i] is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return DateTime.TryParseExact(
            name[..15],
            "yyyyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out made);
    }
}
