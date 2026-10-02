using System.Text.Json;

namespace HeatCaller;

/// <summary>Loads/saves venue profiles to %AppData%\HeatCaller\venues.json.</summary>
public sealed class VenueStore
{
    private readonly List<VenueProfile> _venues;

    public IReadOnlyList<VenueProfile> All => _venues.OrderBy(v => v.Name).ToList();

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "venues.json");

    private VenueStore(List<VenueProfile> venues)
    {
        _venues = venues;
    }

    public static VenueStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var json = SafeFile.ReadAllTextWithRetry(FilePath);
                var loaded = JsonSerializer.Deserialize<List<VenueProfile>>(json);
                if (loaded is not null)
                {
                    // A hand-edited or restored file can hold a null entry, or
                    // nulls where text is promised. The list is sorted by
                    // name and every venue's names are compared as text, so
                    // either threw each time the venues were listed.
                    loaded.RemoveAll(v => v is null);

                    foreach (var venue in loaded)
                    {
                        venue.Id ??= Guid.NewGuid().ToString("N");
                        venue.Name ??= string.Empty;
                        venue.Host ??= "127.0.0.1"; // the model's own default
                        venue.PreferredTrackLayoutName ??= string.Empty;
                        venue.PreferredThemeName ??= string.Empty;
                    }

                    return new VenueStore(loaded);
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

        return new VenueStore(new List<VenueProfile>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds every saved venue
    /// with its connection, track and soundscape. It used to be loaded as
    /// empty and then written over whole at the next venue edit, so a
    /// backup or sync tool holding it open for a moment at launch was
    /// enough to lose every other venue. What this store holds is then only
    /// what was added this session, and none of it is saved.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this session's near-empty list would replace it. The console has
        // already told the operator why and asked for a restart.
        if (LoadedUnreadable)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_venues, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(FilePath, json);
    }

    public VenueProfile? Get(string id) => _venues.FirstOrDefault(v => v.Id == id);

    public void Upsert(VenueProfile venue)
    {
        var index = _venues.FindIndex(v => v.Id == venue.Id);
        if (index >= 0)
        {
            _venues[index] = venue;
        }
        else
        {
            _venues.Add(venue);
        }
        Save();
    }

    public void Delete(string id)
    {
        _venues.RemoveAll(v => v.Id == id);
        Save();
    }
}
