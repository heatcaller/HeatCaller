using System.Text.Json;

namespace HeatCaller;

/// <summary>Loads/saves rivalry lines to %AppData%\HeatCaller\rivalries.json.</summary>
public sealed class RivalryStore
{
    private readonly List<RivalryLine> _rivalries;

    public IReadOnlyList<RivalryLine> All => _rivalries.ToList();

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "rivalries.json");

    private RivalryStore(List<RivalryLine> rivalries)
    {
        _rivalries = rivalries;
    }

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
        Save();
    }

    public void Delete(string id)
    {
        _rivalries.RemoveAll(r => r.Id == id);
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
        Save();
    }
}
