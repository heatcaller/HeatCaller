namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10, venues.json: a file holding only the literal JSON "null"
/// parses without throwing. VenueStore used to treat it as a fresh install,
/// so the first venue edit wrote over the file. It now holds its writes,
/// like the six stores fixed in bug hunt 5.
/// </summary>
public sealed class BugHunt10_VenueStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Venues_holding_only_null_are_not_replaced_by_the_next_venue_added()
    {
        File.WriteAllText(_data.PathOf("venues.json"), "null");

        var store = VenueStore.Load();

        Assert.True(store.LoadedUnreadable);

        store.Upsert(new VenueProfile { Name = "Tonight" });
        Assert.Equal("null", File.ReadAllText(_data.PathOf("venues.json")));
    }

    [Fact]
    public void A_missing_venues_file_is_still_a_fresh_install_that_saves()
    {
        var store = VenueStore.Load();

        Assert.False(store.LoadedUnreadable);

        store.Upsert(new VenueProfile { Name = "Clubhouse" });
        Assert.Contains(VenueStore.Load().All, v => v.Name == "Clubhouse");
    }
}
