using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Per-driver (and optionally per-track) cutoff dates for personal bests.
///
/// Personal bests aren't stored anywhere - they're computed by scanning
/// session history for each driver's quickest lap. So "resetting" one
/// can't mean deleting a stored value; it means ignoring everything up to
/// a point in time. That's deliberately non-destructive: the race history
/// stays completely intact, results and season points are unaffected, and
/// a reset can be undone or cleared later.
///
/// A cutoff keyed to a specific track resets just that track's best. A
/// cutoff with an empty track applies to every track for that driver.
/// </summary>
public sealed class PersonalBestResetStore
{
    private readonly Dictionary<string, DateTime> _cutoffs;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "pb-resets.json");

    private PersonalBestResetStore(Dictionary<string, DateTime> cutoffs)
    {
        _cutoffs = cutoffs;
    }

    public static PersonalBestResetStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (loaded is not null)
                {
                    // Copied one at a time, last one wins: the copy
                    // constructor throws on two keys differing only by
                    // case, which would throw the whole file away.
                    var cutoffs = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in loaded)
                    {
                        cutoffs[pair.Key] = pair.Value;
                    }
                    return new PersonalBestResetStore(cutoffs);
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

        return new PersonalBestResetStore(new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds every driver's
    /// personal-best reset, and nobody remembers which ones were made or
    /// when. It used to be loaded as empty and then written over whole at
    /// the next reset, so a backup or sync tool holding it open for a
    /// moment at launch was enough to lose every other driver's. Resets
    /// made this session still apply until the app closes; they are just
    /// not saved.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    private void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this session's near-empty set would replace it. The console has
        // already told the operator why and asked for a restart.
        if (LoadedUnreadable)
        {
            return;
        }

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(_cutoffs, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Key(string driver, string track) => $"{driver}|{track}";

    /// <summary>
    /// The effective cutoff for a driver on a track - the later of any
    /// track-specific cutoff and any all-tracks cutoff for that driver, so
    /// whichever reset was more recent wins.
    /// </summary>
    public DateTime? EffectiveCutoff(string driver, string track)
    {
        DateTime? result = null;

        if (_cutoffs.TryGetValue(Key(driver, track), out var specific))
        {
            result = specific;
        }

        if (_cutoffs.TryGetValue(Key(driver, string.Empty), out var allTracks))
        {
            result = result is null || allTracks > result ? allTracks : result;
        }

        return result;
    }

    /// <summary>Sets a cutoff. An empty track applies to all of that driver's tracks.</summary>
    public void SetCutoff(string driver, string track, DateTime utc)
    {
        _cutoffs[Key(driver, track)] = utc;
        Save();
    }

    /// <summary>Removes a cutoff, bringing the driver's full history back into consideration.</summary>
    public void ClearCutoff(string driver, string track)
    {
        _cutoffs.Remove(Key(driver, track));
        Save();
    }

    /// <summary>Removes every cutoff for a driver, both track-specific and all-tracks.</summary>
    public void ClearAllForDriver(string driver)
    {
        var prefix = $"{driver}|";
        foreach (var key in _cutoffs.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _cutoffs.Remove(key);
        }
        Save();
    }

    /// <summary>Snapshot of every cutoff, for capturing undo state.</summary>
    public Dictionary<string, DateTime> Snapshot() => new(_cutoffs, StringComparer.OrdinalIgnoreCase);

    /// <summary>Replaces all cutoffs. Pairs with Snapshot to undo a reset.</summary>
    public void Restore(Dictionary<string, DateTime> snapshot)
    {
        _cutoffs.Clear();
        foreach (var (key, value) in snapshot)
        {
            _cutoffs[key] = value;
        }
        Save();
    }

    /// <summary>
    /// Puts back one driver's cutoffs as a Snapshot recorded them, leaving
    /// every other driver's alone - so undoing a reset for one driver does
    /// not also undo resets made for others since.
    /// </summary>
    public void RestoreForDriver(string driver, Dictionary<string, DateTime> snapshot)
    {
        var prefix = $"{driver}|";

        foreach (var key in _cutoffs.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _cutoffs.Remove(key);
        }

        foreach (var (key, value) in snapshot)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                _cutoffs[key] = value;
            }
        }

        Save();
    }

    public bool HasAnyFor(string driver) =>
        _cutoffs.Keys.Any(k => k.StartsWith($"{driver}|", StringComparison.OrdinalIgnoreCase));
}
