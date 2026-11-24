using System.Text.Json;

namespace HeatCaller;

/// <summary>Loads/saves rivalry lines to %AppData%\HeatCaller\rivalries.json.</summary>
public sealed class RivalryStore
{
    private readonly List<RivalryLine> _rivalries;

    /// <summary>
    /// The rivalries as of the last change, safe to read from any thread.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): All was _rivalries.ToList() over the
    /// live list the UI thread edits, and the Team Principal interviews read
    /// it on the thread pool - so a rivalry added or deleted at that moment
    /// could throw and lose the interview that was being written ahead. Now
    /// every change swaps in a fresh copy and All hands that copy out.
    /// </remarks>
    public IReadOnlyList<RivalryLine> All => _snapshot;

    private volatile RivalryLine[] _snapshot;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "rivalries.json");

    private RivalryStore(List<RivalryLine> rivalries)
    {
        _rivalries = rivalries;
        _snapshot = rivalries.ToArray();
    }

    private void Refresh() => _snapshot = _rivalries.ToArray();

    public static RivalryStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var json = SafeFile.ReadAllTextWithRetry(FilePath);
                var loaded = JsonSerializer.Deserialize<List<RivalryLine>>(json);
                if (loaded is not null)
                {
                    // A hand-edited or restored file can say null where the
                    // app never writes one (17 Oct 2026, Bug Hunt 8, A11-1):
                    // a null rivalry or "Templates": null crashed the
                    // Rivalries page at launch and the lap calls mid-race.
                    // Repaired as ThemeStore and VenueStore do. An empty set,
                    // not the defaults - a null is not a request for them.
                    loaded.RemoveAll(r => r is null);

                    foreach (var rivalry in loaded)
                    {
                        rivalry.Id ??= Guid.NewGuid().ToString("N");
                        rivalry.DriverA ??= string.Empty;
                        rivalry.DriverB ??= string.Empty;
                        rivalry.Templates ??= new List<string>();
                        rivalry.Templates.RemoveAll(t => t is null);
                    }

                    return new RivalryStore(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            // But, when it could not be read at all or no copy of it could be
            // kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new RivalryStore(new List<RivalryLine>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. A file locked for a moment at
    /// launch used to load empty, and the first rivalry added or edited
    /// then replaced every rivalry the club had written.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this near-empty store would replace it. The console has already
        // said why.
        if (LoadedUnreadable)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_rivalries, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(FilePath, json);
    }

    public RivalryLine? Get(string id) => _rivalries.FirstOrDefault(r => r.Id == id);

    public void Upsert(RivalryLine rivalry)
    {
        var index = _rivalries.FindIndex(r => r.Id == rivalry.Id);
        if (index >= 0)
        {
            _rivalries[index] = rivalry;
        }
        else
        {
            _rivalries.Add(rivalry);
        }
        Refresh();
        Save();
    }

    public void Delete(string id)
    {
        _rivalries.RemoveAll(r => r.Id == id);
        Refresh();
        Save();
    }

    /// <summary>Where a rivalry sits in the list, so a deleted one can go back in the same place.</summary>
    public int IndexOf(string id) => _rivalries.FindIndex(r => r.Id == id);

    /// <summary>The undo half of Delete: the rivalry back where it was, unless it is back already.</summary>
    public void RestoreDeleted(RivalryLine rivalry, int index)
    {
        if (IndexOf(rivalry.Id) >= 0)
        {
            return;
        }

        _rivalries.Insert(Math.Clamp(index, 0, _rivalries.Count), rivalry);
        Refresh();
        Save();
    }
}
