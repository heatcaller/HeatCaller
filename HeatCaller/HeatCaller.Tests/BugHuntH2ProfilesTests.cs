namespace HeatCaller.Tests;

/// <summary>
/// A hand-edited or externally written profiles.json that says null where
/// a list or a name belongs must cost only that field, not the whole
/// roster: one "Cars": null used to make Load treat the file as unreadable
/// and start empty, so the next save replaced every driver.
/// </summary>
public sealed class BugHuntH2ProfilesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_profile_with_null_cars_does_not_empty_the_roster()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("profiles.json"), """
            [
              { "Transponder": "AAA111", "DriverName": "Alice", "CarModel": "MR-03", "Cars": null },
              { "Transponder": "BBB222", "DriverName": null, "Cars": ["MR-04", null] }
            ]
            """);

        var store = ProfileStore.Load();

        Assert.False(store.LoadedUnreadable);

        var alice = store.Get("AAA111");
        Assert.NotNull(alice);
        Assert.Equal(new[] { "MR-03" }, alice!.AllCars);

        var second = store.Get("BBB222");
        Assert.NotNull(second);
        Assert.Equal(string.Empty, second!.DriverName);
        Assert.Equal(new[] { "MR-04" }, second.Cars);
    }
}
