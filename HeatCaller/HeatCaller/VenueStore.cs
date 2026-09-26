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
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            SafeFile.PreserveUnreadable(FilePath, ex);
        }

        return new VenueStore(new List<VenueProfile>());
    }

    public void Save()
    {
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
